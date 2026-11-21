import { useEffect, useRef, useState } from 'react';
import { CS1_MAP_SIZE, CS2_MAX_CROP, MIN_CROP } from '../core/crop.ts';
import type { Crop } from '../core/types.ts';
import { CATEGORY_STYLE, SKIPPED_CATS, hitSegment } from './scene.ts';
import type { Scene, SegInfo } from './scene.ts';

interface Props {
  scene: Scene;
  crop: Crop;
  onCrop: (c: Crop, committed: boolean) => void;
  showSkipped: boolean;
  showTerrain: boolean;
  fitKey: number;
  describe: (s: SegInfo) => string[];
}

type Drag =
  | { mode: 'pan'; sx: number; sy: number; vx: number; vz: number }
  | { mode: 'move'; ox: number; oz: number }
  | { mode: 'resize'; ax: number; az: number; sgx: number; sgz: number };

const HANDLE = 10;

export function MapView({ scene, crop, onCrop, showSkipped, showTerrain, fitKey, describe }: Props) {
  const wrap = useRef<HTMLDivElement>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const view = useRef({ x: 0, z: 0, scale: 0.05 });
  const size = useRef({ w: 800, h: 600 });
  const drag = useRef<Drag | null>(null);
  const cropRef = useRef(crop);
  cropRef.current = crop;
  const [cursor, setCursor] = useState('grab');
  const [hover, setHover] = useState<{ s: SegInfo; x: number; y: number } | null>(null);
  const frame = useRef(0);
  const draw = useRef<() => void>(() => {});

  const toWorld = (sx: number, sy: number) => {
    const v = view.current, { w, h } = size.current;
    return { x: v.x + (sx - w / 2) / v.scale, z: v.z - (sy - h / 2) / v.scale };
  };
  const toScreen = (x: number, z: number) => {
    const v = view.current, { w, h } = size.current;
    return { x: w / 2 + (x - v.x) * v.scale, y: h / 2 - (z - v.z) * v.scale };
  };
  const request = () => { if (!frame.current) frame.current = requestAnimationFrame(() => { frame.current = 0; draw.current(); }); };

  draw.current = () => {
    const cv = canvas.current;
    if (!cv) return;
    const ctx = cv.getContext('2d')!;
    const dpr = window.devicePixelRatio || 1;
    const { w, h } = size.current, v = view.current;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = '#0e1318';
    ctx.fillRect(0, 0, w, h);
    ctx.setTransform(dpr * v.scale, 0, 0, dpr * v.scale, dpr * (w / 2 - v.x * v.scale), dpr * (h / 2 + v.z * v.scale));
    const px = 1 / v.scale;
    const half = CS1_MAP_SIZE / 2;
    if (showTerrain && scene.terrain) {
      ctx.imageSmoothingEnabled = true;
      const s = scene.terrainSize;
      ctx.drawImage(scene.terrain, -s / 2, -s / 2, s, s);
    } else {
      ctx.fillStyle = '#161d24';
      ctx.fillRect(-half, -half, CS1_MAP_SIZE, CS1_MAP_SIZE);
    }
    ctx.strokeStyle = '#ffffff22';
    ctx.lineWidth = px;
    ctx.strokeRect(-half, -half, CS1_MAP_SIZE, CS1_MAP_SIZE);
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    for (const b of scene.buckets) {
      const skipped = SKIPPED_CATS.has(b.cat);
      if (skipped && !showSkipped) continue;
      const st = CATEGORY_STYLE[b.cat];
      const lw = Math.max(st.minPx * px, skipped ? Math.min(b.width, 2 * px) : b.width);
      ctx.globalAlpha = b.tunnel ? 0.35 : skipped ? 0.6 : 1;
      ctx.setLineDash(st.dash || b.tunnel ? (st.dash ?? [4, 4]).map(d => d * px) : []);
      if (b.elevated && lw > 2 * px) {
        ctx.strokeStyle = '#000000aa';
        ctx.lineWidth = lw + 2.5 * px;
        ctx.stroke(b.path);
      }
      ctx.strokeStyle = st.color;
      ctx.lineWidth = lw;
      ctx.stroke(b.path);
    }
    ctx.globalAlpha = 1;
    ctx.setLineDash([]);
    for (const f of scene.fills) {
      ctx.fillStyle = f.color;
      ctx.fill(f.path);
      ctx.strokeStyle = f.stroke;
      ctx.lineWidth = Math.max(0.3, 0.8 * px);
      ctx.stroke(f.path);
    }
    // segments crossing the crop edge (left out of the plan)
    const c = cropRef.current, hc = c.size / 2;
    const inside = (p: [number, number]) => Math.abs(p[0] - c.centerX) <= hc && Math.abs(p[1] - c.centerZ) <= hc;
    ctx.strokeStyle = '#ffd43b';
    ctx.lineWidth = Math.max(2 * px, 3);
    ctx.setLineDash([5 * px, 4 * px]);
    ctx.beginPath();
    for (const s of scene.segs) {
      if (SKIPPED_CATS.has(s.cat)) continue;
      const a = inside(s.pts[0]), b = inside(s.pts[s.pts.length - 1]);
      if (a === b) continue;
      s.pts.forEach((p, i) => (i ? ctx.lineTo(p[0], -p[1]) : ctx.moveTo(p[0], -p[1])));
    }
    ctx.stroke();
    ctx.setLineDash([]);
    // crop mask + frame (screen space)
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    const tl = toScreen(c.centerX - hc, c.centerZ + hc), br = toScreen(c.centerX + hc, c.centerZ - hc);
    ctx.fillStyle = 'rgba(5,8,12,0.55)';
    ctx.beginPath();
    ctx.rect(0, 0, w, h);
    ctx.rect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
    ctx.fill('evenodd');
    ctx.strokeStyle = '#4dabf7';
    ctx.lineWidth = 2;
    ctx.strokeRect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
    ctx.fillStyle = '#4dabf7';
    for (const [x, y] of [[tl.x, tl.y], [br.x, tl.y], [tl.x, br.y], [br.x, br.y]]) ctx.fillRect(x - HANDLE / 2, y - HANDLE / 2, HANDLE, HANDLE);
    ctx.font = '12px system-ui, sans-serif';
    const label = `${(c.size / 1000).toFixed(2)} km  ·  centre ${c.centerX}, ${c.centerZ}${c.size >= CS2_MAX_CROP ? '  ·  CS2 max' : ''}`;
    const tw = ctx.measureText(label).width;
    ctx.fillStyle = '#0b1016cc';
    ctx.fillRect(tl.x, tl.y - 22, tw + 12, 20);
    ctx.fillStyle = '#d0ebff';
    ctx.fillText(label, tl.x + 6, tl.y - 8);
    // scale bar
    const meters = niceStep(120 / v.scale);
    const bar = meters * v.scale;
    ctx.fillStyle = '#0b1016cc';
    ctx.fillRect(12, h - 30, bar + 70, 20);
    ctx.fillStyle = '#e9ecef';
    ctx.fillRect(18, h - 18, bar, 3);
    ctx.fillText(meters >= 1000 ? `${meters / 1000} km` : `${meters} m`, 24 + bar, h - 15);
  };

  const fit = (minX: number, maxX: number, minZ: number, maxZ: number) => {
    const { w, h } = size.current;
    view.current = { x: (minX + maxX) / 2, z: (minZ + maxZ) / 2, scale: Math.min(w / (maxX - minX || 1), h / (maxZ - minZ || 1)) * 0.92 };
    request();
  };

  useEffect(() => {
    const el = wrap.current!, cv = canvas.current!;
    const ro = new ResizeObserver(() => {
      const r = el.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      size.current = { w: r.width, h: r.height };
      cv.width = Math.round(r.width * dpr);
      cv.height = Math.round(r.height * dpr);
      cv.style.width = `${r.width}px`;
      cv.style.height = `${r.height}px`;
      request();
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  // On open: frame the crop with some context around it.
  useEffect(() => {
    const c = cropRef.current, m = c.size * 1.25;
    fit(c.centerX - m, c.centerX + m, c.centerZ - m, c.centerZ + m);
  }, [scene, fitKey]);

  useEffect(() => { request(); }, [crop, showSkipped, showTerrain, scene]);

  const hitHandle = (sx: number, sy: number) => {
    const c = cropRef.current, hc = c.size / 2;
    const corners: [number, number, number, number][] = [[-1, 1, 1, -1], [1, 1, -1, -1], [-1, -1, 1, 1], [1, -1, -1, 1]];
    for (const [cx, cz, ox, oz] of corners) {
      const p = toScreen(c.centerX + cx * hc, c.centerZ + cz * hc);
      if (Math.abs(p.x - sx) <= HANDLE && Math.abs(p.y - sy) <= HANDLE) return { ax: c.centerX + ox * hc, az: c.centerZ + oz * hc, sgx: cx, sgz: cz };
    }
    return null;
  };
  const inCropScreen = (sx: number, sy: number) => {
    const w = toWorld(sx, sy), c = cropRef.current;
    return Math.abs(w.x - c.centerX) <= c.size / 2 && Math.abs(w.z - c.centerZ) <= c.size / 2;
  };
  const local = (e: React.PointerEvent | React.WheelEvent) => {
    const r = canvas.current!.getBoundingClientRect();
    return { sx: e.clientX - r.left, sy: e.clientY - r.top };
  };

  const onDown = (e: React.PointerEvent) => {
    const { sx, sy } = local(e);
    canvas.current!.setPointerCapture(e.pointerId);
    const hnd = hitHandle(sx, sy);
    if (e.button === 0 && hnd) drag.current = { mode: 'resize', ...hnd };
    else if (e.button === 0 && inCropScreen(sx, sy) && !e.shiftKey) {
      const w = toWorld(sx, sy);
      drag.current = { mode: 'move', ox: w.x - cropRef.current.centerX, oz: w.z - cropRef.current.centerZ };
    } else drag.current = { mode: 'pan', sx, sy, vx: view.current.x, vz: view.current.z };
    setHover(null);
  };
  const onMove = (e: React.PointerEvent) => {
    const { sx, sy } = local(e);
    const d = drag.current;
    if (!d) {
      setCursor(hitHandle(sx, sy) ? 'nwse-resize' : inCropScreen(sx, sy) ? 'move' : 'grab');
      const w = toWorld(sx, sy);
      const s = hitSegment(scene, w.x, w.z, 8 / view.current.scale, showSkipped);
      setHover(s ? { s, x: sx, y: sy } : null);
      return;
    }
    const w = toWorld(sx, sy);
    if (d.mode === 'pan') {
      view.current = { ...view.current, x: d.vx - (sx - d.sx) / view.current.scale, z: d.vz + (sy - d.sy) / view.current.scale };
      request();
    } else if (d.mode === 'move') {
      onCrop({ ...cropRef.current, centerX: Math.round(w.x - d.ox), centerZ: Math.round(w.z - d.oz) }, false);
    } else {
      const s = Math.round(Math.min(CS2_MAX_CROP, Math.max(MIN_CROP, Math.abs(w.x - d.ax), Math.abs(w.z - d.az))));
      onCrop({ size: s, centerX: Math.round(d.ax + (d.sgx * s) / 2), centerZ: Math.round(d.az + (d.sgz * s) / 2) }, false);
    }
  };
  const onUp = (e: React.PointerEvent) => {
    const d = drag.current;
    drag.current = null;
    canvas.current!.releasePointerCapture(e.pointerId);
    if (d && d.mode !== 'pan') onCrop(cropRef.current, true);
  };
  const onWheel = (e: React.WheelEvent) => {
    const { sx, sy } = local(e);
    const before = toWorld(sx, sy);
    const k = Math.exp(-e.deltaY * 0.0015);
    const v = view.current;
    v.scale = Math.min(20, Math.max(0.01, v.scale * k));
    const after = toWorld(sx, sy);
    v.x += before.x - after.x;
    v.z += before.z - after.z;
    request();
  };

  return (
    <div className="map" ref={wrap}>
      <canvas ref={canvas} style={{ cursor: drag.current?.mode === 'pan' ? 'grabbing' : cursor }}
        onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp} onPointerLeave={() => setHover(null)} onWheel={onWheel}
        onContextMenu={e => e.preventDefault()} />
      <div className="map-tools">
        <button onClick={() => { const e = scene.extent; fit(e.minX, e.maxX, e.minZ, e.maxZ); }} title="Fit the whole network">Fit city</button>
        <button onClick={() => { const c = cropRef.current; fit(c.centerX - c.size * 0.6, c.centerX + c.size * 0.6, c.centerZ - c.size * 0.6, c.centerZ + c.size * 0.6); }} title="Zoom to the crop">Zoom to crop</button>
      </div>
      <Legend />
      {hover && (
        <div className="tip" style={{ left: hover.x + 14, top: hover.y + 14 }}>
          {describe(hover.s).map((l, i) => <div key={i} className={i ? '' : 'tip-title'}>{l}</div>)}
        </div>
      )}
    </div>
  );
}

function Legend() {
  const cats = ['road', 'highway', 'train', 'metro', 'pedestrian'] as const;
  return (
    <div className="legend">
      {cats.map(c => <span key={c}><i style={{ background: CATEGORY_STYLE[c].color }} />{CATEGORY_STYLE[c].label}</span>)}
      <span><i style={{ background: 'rgba(136,0,0,0.9)' }} />IMT fill</span>
      <span><i style={{ background: '#ffd43b' }} />Crosses crop edge (left out)</span>
    </div>
  );
}

function niceStep(v: number) {
  const p = 10 ** Math.floor(Math.log10(v));
  const m = v / p;
  return (m >= 5 ? 5 : m >= 2 ? 2 : 1) * p;
}
