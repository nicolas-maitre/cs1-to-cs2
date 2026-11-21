import { forwardRef, useEffect, useImperativeHandle, useRef, useState } from 'react';
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
  showZones: boolean;
  showTransit: boolean;
  showBuildings: boolean;
  showDistricts: boolean;
  describe: (s: SegInfo) => string[];
}

export interface MapHandle { fitCity(): void; fitCrop(): void }

type Drag =
  | { mode: 'pan'; sx: number; sy: number; vx: number; vz: number }
  | { mode: 'move'; ox: number; oz: number }
  | { mode: 'resize'; ax: number; az: number; sgx: number; sgz: number }
  | { mode: 'pinch'; d0: number; s0: number; wx: number; wz: number };

interface View { x: number; z: number; scale: number }

/**
 * Two canvases: `base` (terrain, network, IMT fills; redrawn on pan/zoom/toggles) and `over` (crop
 * square, mask, crop-edge segments, labels; redrawn on every crop change). When a full base redraw
 * is slow (huge cities), pan/zoom first moves the last base image with a CSS transform and redraws
 * once the view settles.
 */
export const MapView = forwardRef<MapHandle, Props>(function MapView({ scene, crop, onCrop, showSkipped, showTerrain, showZones, showTransit, showBuildings, showDistricts, describe }, ref) {
  const wrap = useRef<HTMLDivElement>(null);
  const base = useRef<HTMLCanvasElement>(null);
  const over = useRef<HTMLCanvasElement>(null);
  const view = useRef<View>({ x: 0, z: 0, scale: 0.05 });
  const baseView = useRef<View>({ x: 0, z: 0, scale: 0.05 });
  const size = useRef({ w: 800, h: 600 });
  const drag = useRef<Drag | null>(null);
  const pointers = useRef(new Map<number, { x: number; y: number }>());
  const cropRef = useRef(crop);
  cropRef.current = crop;
  const props = useRef({ scene, showSkipped, showTerrain, showZones, showTransit, showBuildings, showDistricts });
  props.current = { scene, showSkipped, showTerrain, showZones, showTransit, showBuildings, showDistricts };
  const [cursor, setCursor] = useState('grab');
  const [hover, setHover] = useState<{ s: SegInfo; x: number; y: number } | null>(null);
  const [focused, setFocused] = useState(false);
  const focusedRef = useRef(false);
  focusedRef.current = focused;
  const raf = useRef(0);
  const dirty = useRef({ base: true, over: true });
  const lastBaseMs = useRef(0);
  const settle = useRef(0);
  const touchUi = useRef(false);

  const toWorld = (sx: number, sy: number, v = view.current) => {
    const { w, h } = size.current;
    return { x: v.x + (sx - w / 2) / v.scale, z: v.z - (sy - h / 2) / v.scale };
  };
  const toScreen = (x: number, z: number, v = view.current) => {
    const { w, h } = size.current;
    return { x: w / 2 + (x - v.x) * v.scale, y: h / 2 - (z - v.z) * v.scale };
  };
  const handleSize = () => (touchUi.current ? 22 : 12);

  const schedule = (what: { base?: boolean; over?: boolean }) => {
    if (what.base) dirty.current.base = true;
    if (what.over) dirty.current.over = true;
    if (!raf.current) raf.current = requestAnimationFrame(frame);
  };

  const frame = () => {
    raf.current = 0;
    if (dirty.current.base) {
      // Slow base → move the old image now, redraw when the view stops changing.
      if (lastBaseMs.current > 14 && !viewEquals(view.current, baseView.current)) {
        transformBase();
        clearTimeout(settle.current);
        settle.current = window.setTimeout(() => { drawBase(); dirty.current.base = false; }, 140);
      } else { drawBase(); dirty.current.base = false; }
    }
    if (dirty.current.over) { drawOverlay(); dirty.current.over = false; }
  };

  const transformBase = () => {
    const b = baseView.current, v = view.current, { w, h } = size.current;
    const k = v.scale / b.scale;
    const p = toScreen(b.x, b.z, v); // where the old centre is now
    base.current!.style.transform = `translate(${p.x - (w / 2) * k}px, ${p.y - (h / 2) * k}px) scale(${k})`;
  };

  const drawBase = () => {
    const cv = base.current;
    if (!cv) return;
    const t0 = performance.now();
    const { scene: sc, showSkipped: sk, showTerrain: st, showZones: sz, showTransit: stt, showBuildings: sb, showDistricts: sd } = props.current;
    const ctx = cv.getContext('2d')!;
    const dpr = window.devicePixelRatio || 1;
    const { w, h } = size.current, v = view.current;
    cv.style.transform = '';
    baseView.current = { ...v };
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.fillStyle = '#0e1318';
    ctx.fillRect(0, 0, w, h);
    ctx.setTransform(dpr * v.scale, 0, 0, dpr * v.scale, dpr * (w / 2 - v.x * v.scale), dpr * (h / 2 + v.z * v.scale));
    const px = 1 / v.scale;
    const half = CS1_MAP_SIZE / 2;
    if (st && sc.terrain) {
      ctx.imageSmoothingEnabled = true;
      ctx.drawImage(sc.terrain, -sc.terrainSize / 2, -sc.terrainSize / 2, sc.terrainSize, sc.terrainSize);
    } else {
      ctx.fillStyle = '#161d24';
      ctx.fillRect(-half, -half, CS1_MAP_SIZE, CS1_MAP_SIZE);
    }
    ctx.strokeStyle = '#ffffff22';
    ctx.lineWidth = px;
    ctx.strokeRect(-half, -half, CS1_MAP_SIZE, CS1_MAP_SIZE);
    if (sz && sc.zones.length) {
      ctx.globalAlpha = 0.55;
      for (const zz of sc.zones) { ctx.fillStyle = zz.color; ctx.fill(zz.path); }
      ctx.globalAlpha = 1;
    }
    if (sd && sc.districts) {
      for (const d of sc.districts.items) {
        ctx.globalAlpha = 0.18; ctx.fillStyle = d.color; ctx.fill(d.path, 'evenodd');
        ctx.globalAlpha = 0.9; ctx.strokeStyle = d.color; ctx.lineWidth = 2 * px; ctx.setLineDash([8 * px, 5 * px]); ctx.stroke(d.path);
      }
      ctx.setLineDash([]);
      ctx.globalAlpha = 1;
    }
    if (sb && sc.buildings) {
      ctx.globalAlpha = 0.85;
      for (const p of sc.buildings.paths) { ctx.fillStyle = p.color; ctx.fill(p.path); }
      ctx.globalAlpha = 1;
    }
    // visible world rect, for culling
    const a = toWorld(0, h, v), b = toWorld(w, 0, v);
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    for (const bk of sc.buckets) {
      const skipped = SKIPPED_CATS.has(bk.cat);
      if (skipped && !sk) continue;
      const m = bk.width + 4 * px;
      if (bk.maxX + m < a.x || bk.minX - m > b.x || bk.maxZ + m < a.z || bk.minZ - m > b.z) continue;
      const style = CATEGORY_STYLE[bk.cat];
      const lw = Math.max(style.minPx * px, skipped ? Math.min(bk.width, 2 * px) : bk.width);
      ctx.globalAlpha = bk.tunnel ? 0.35 : skipped ? 0.6 : 1;
      ctx.setLineDash(style.dash || bk.tunnel ? (style.dash ?? [4, 4]).map(d => d * px) : []);
      if (bk.elevated && lw > 2 * px) {
        ctx.strokeStyle = '#000000aa';
        ctx.lineWidth = lw + 2.5 * px;
        ctx.stroke(bk.path);
      }
      ctx.strokeStyle = style.color;
      ctx.lineWidth = lw;
      ctx.stroke(bk.path);
    }
    ctx.globalAlpha = 1;
    ctx.setLineDash([]);
    if (v.scale > 0.15) {
      for (const f of sc.fills) {
        ctx.fillStyle = f.color;
        ctx.fill(f.path);
        ctx.strokeStyle = f.stroke;
        ctx.lineWidth = Math.max(0.3, 0.8 * px);
        ctx.stroke(f.path);
      }
    }
    if (stt && sc.lines.length) {
      ctx.lineWidth = Math.max(3 * px, 6);
      ctx.setLineDash([10 * px, 6 * px]);
      for (const l of sc.lines) { ctx.strokeStyle = l.color; ctx.stroke(l.path); }
      ctx.setLineDash([]);
      for (const l of sc.lines) { ctx.fillStyle = l.color; ctx.fill(l.stops); ctx.strokeStyle = '#000'; ctx.lineWidth = 1.5 * px; ctx.stroke(l.stops); }
    }
    if (sd && sc.districts) {
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.font = '600 13px system-ui, sans-serif';
      ctx.textAlign = 'center';
      for (const d of sc.districts.items) {
        const p = toScreen(d.label[0], d.label[1], v);
        if (p.x < -100 || p.y < -20 || p.x > w + 100 || p.y > h + 20) continue;
        ctx.lineWidth = 3; ctx.strokeStyle = '#000c'; ctx.strokeText(d.name, p.x, p.y);
        ctx.fillStyle = d.color; ctx.fillText(d.name, p.x, p.y);
      }
      ctx.textAlign = 'start';
    }
    lastBaseMs.current = performance.now() - t0;
  };

  const drawOverlay = () => {
    const cv = over.current;
    if (!cv) return;
    const ctx = cv.getContext('2d')!;
    const dpr = window.devicePixelRatio || 1;
    const { w, h } = size.current, v = view.current;
    const { scene: sc } = props.current;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, w, h);
    const c = cropRef.current, hc = c.size / 2;
    // segments crossing the crop edge (left out of the plan)
    ctx.setTransform(dpr * v.scale, 0, 0, dpr * v.scale, dpr * (w / 2 - v.x * v.scale), dpr * (h / 2 + v.z * v.scale));
    const px = 1 / v.scale;
    ctx.strokeStyle = '#ffd43b';
    ctx.lineWidth = Math.max(2 * px, 3);
    ctx.setLineDash([5 * px, 4 * px]);
    ctx.beginPath();
    const e = sc.ends, minX = c.centerX - hc, maxX = c.centerX + hc, minZ = c.centerZ - hc, maxZ = c.centerZ + hc;
    for (let i = 0, n = sc.skipped.length; i < n; i++) {
      if (sc.skipped[i]) continue;
      const x0 = e[i * 4], z0 = e[i * 4 + 1], x1 = e[i * 4 + 2], z1 = e[i * 4 + 3];
      const ia = x0 >= minX && x0 <= maxX && z0 >= minZ && z0 <= maxZ;
      const ib = x1 >= minX && x1 <= maxX && z1 >= minZ && z1 <= maxZ;
      if (ia === ib) continue;
      const pts = sc.segs[i].pts;
      pts.forEach((p, k) => (k ? ctx.lineTo(p[0], -p[1]) : ctx.moveTo(p[0], -p[1])));
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
    ctx.strokeStyle = focusedRef.current ? '#a5d8ff' : '#4dabf7';
    ctx.lineWidth = focusedRef.current ? 3 : 2;
    ctx.strokeRect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
    ctx.fillStyle = '#4dabf7';
    const hs = handleSize();
    for (const [x, y] of [[tl.x, tl.y], [br.x, tl.y], [tl.x, br.y], [br.x, br.y]]) ctx.fillRect(x - hs / 2, y - hs / 2, hs, hs);
    ctx.font = '12px system-ui, sans-serif';
    const label = `${(c.size / 1000).toFixed(2)} km  ·  centre ${c.centerX}, ${c.centerZ}${c.size >= CS2_MAX_CROP ? '  ·  CS2 max' : ''}`;
    const tw = ctx.measureText(label).width;
    const ly = Math.max(22, tl.y);
    ctx.fillStyle = '#0b1016cc';
    ctx.fillRect(Math.max(0, tl.x), ly - 22, tw + 12, 20);
    ctx.fillStyle = '#d0ebff';
    ctx.fillText(label, Math.max(0, tl.x) + 6, ly - 8);
    // scale bar
    const meters = niceStep(120 / v.scale);
    const bar = meters * v.scale;
    ctx.fillStyle = '#0b1016cc';
    ctx.fillRect(12, h - 30, bar + 70, 20);
    ctx.fillStyle = '#e9ecef';
    ctx.fillRect(18, h - 18, bar, 3);
    ctx.fillText(meters >= 1000 ? `${meters / 1000} km` : `${meters} m`, 24 + bar, h - 15);
  };

  const setView = (v: View) => { view.current = v; schedule({ base: true, over: true }); };
  const fit = (minX: number, maxX: number, minZ: number, maxZ: number) => {
    const { w, h } = size.current;
    setView({ x: (minX + maxX) / 2, z: (minZ + maxZ) / 2, scale: Math.min(w / (maxX - minX || 1), h / (maxZ - minZ || 1)) * 0.92 });
  };
  const fitCity = () => { const e = props.current.scene.extent; fit(e.minX, e.maxX, e.minZ, e.maxZ); };
  const fitCrop = () => { const c = cropRef.current, m = c.size * 0.65; fit(c.centerX - m, c.centerX + m, c.centerZ - m, c.centerZ + m); };
  useImperativeHandle(ref, () => ({ fitCity, fitCrop }));
  // Dev only: lets scripted checks time the redraws without relying on requestAnimationFrame.
  if (import.meta.env.DEV) {
    (window as unknown as Record<string, unknown>).__cs1convMap = {
      timeBase: () => { const t = performance.now(); drawBase(); base.current!.getContext('2d')!.getImageData(0, 0, 1, 1); return performance.now() - t; },
      timeOverlay: () => { const t = performance.now(); drawOverlay(); over.current!.getContext('2d')!.getImageData(0, 0, 1, 1); return performance.now() - t; },
      fitCity, fitCrop, view: () => ({ ...view.current }),
      resize: (w: number, h: number) => { size.current = { w, h }; for (const cv of [base.current!, over.current!]) { cv.width = w; cv.height = h; cv.style.width = `${w}px`; cv.style.height = `${h}px`; } }, zoom: (k: number) => { const { w, h } = size.current; zoomAt(w / 2, h / 2, k); },
    };
  }

  useEffect(() => {
    const el = wrap.current!;
    const ro = new ResizeObserver(() => {
      const r = el.getBoundingClientRect();
      const dpr = window.devicePixelRatio || 1;
      size.current = { w: r.width, h: r.height };
      for (const cv of [base.current!, over.current!]) {
        cv.width = Math.round(r.width * dpr);
        cv.height = Math.round(r.height * dpr);
        cv.style.width = `${r.width}px`;
        cv.style.height = `${r.height}px`;
      }
      lastBaseMs.current = 0;
      schedule({ base: true, over: true });
    });
    ro.observe(el);
    return () => { ro.disconnect(); cancelAnimationFrame(raf.current); raf.current = 0; clearTimeout(settle.current); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // New city: frame the crop with some context.
  useEffect(() => {
    const c = cropRef.current, m = c.size * 1.25;
    lastBaseMs.current = 0;
    fit(c.centerX - m, c.centerX + m, c.centerZ - m, c.centerZ + m);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scene]);
  useEffect(() => { lastBaseMs.current = 0; schedule({ base: true }); }, [showSkipped, showTerrain, showZones, showTransit, showBuildings, showDistricts]);
  useEffect(() => { schedule({ over: true }); }, [crop, focused]);

  const hitHandle = (sx: number, sy: number) => {
    const c = cropRef.current, hc = c.size / 2, hs = handleSize();
    const corners: [number, number][] = [[-1, 1], [1, 1], [-1, -1], [1, -1]];
    for (const [cx, cz] of corners) {
      const p = toScreen(c.centerX + cx * hc, c.centerZ + cz * hc);
      if (Math.abs(p.x - sx) <= hs && Math.abs(p.y - sy) <= hs) return { ax: c.centerX - cx * hc, az: c.centerZ - cz * hc, sgx: cx, sgz: cz };
    }
    return null;
  };
  const inCropScreen = (sx: number, sy: number) => {
    const p = toWorld(sx, sy), c = cropRef.current;
    return Math.abs(p.x - c.centerX) <= c.size / 2 && Math.abs(p.z - c.centerZ) <= c.size / 2;
  };
  const local = (e: { clientX: number; clientY: number }) => {
    const r = over.current!.getBoundingClientRect();
    return { sx: e.clientX - r.left, sy: e.clientY - r.top };
  };

  const onDown = (e: React.PointerEvent) => {
    touchUi.current = e.pointerType === 'touch';
    const { sx, sy } = local(e);
    over.current!.setPointerCapture(e.pointerId);
    pointers.current.set(e.pointerId, { x: sx, y: sy });
    setHover(null);
    if (pointers.current.size === 2) {
      const [p, q] = [...pointers.current.values()];
      const mid = toWorld((p.x + q.x) / 2, (p.y + q.y) / 2);
      drag.current = { mode: 'pinch', d0: Math.hypot(p.x - q.x, p.y - q.y) || 1, s0: view.current.scale, wx: mid.x, wz: mid.z };
      return;
    }
    const hnd = hitHandle(sx, sy);
    if (e.button === 0 && hnd) drag.current = { mode: 'resize', ...hnd };
    else if (e.button === 0 && inCropScreen(sx, sy) && !e.shiftKey) {
      const p = toWorld(sx, sy);
      drag.current = { mode: 'move', ox: p.x - cropRef.current.centerX, oz: p.z - cropRef.current.centerZ };
    } else drag.current = { mode: 'pan', sx, sy, vx: view.current.x, vz: view.current.z };
    if (drag.current.mode === 'pan') setCursor('grabbing');
  };
  const onMove = (e: React.PointerEvent) => {
    const { sx, sy } = local(e);
    if (pointers.current.has(e.pointerId)) pointers.current.set(e.pointerId, { x: sx, y: sy });
    const d = drag.current;
    if (!d) {
      if (e.pointerType === 'touch') return;
      setCursor(hitHandle(sx, sy) ? 'nwse-resize' : inCropScreen(sx, sy) ? 'move' : 'grab');
      const p = toWorld(sx, sy);
      const s = hitSegment(props.current.scene, p.x, p.z, 8 / view.current.scale, props.current.showSkipped);
      setHover(s ? { s, x: sx, y: sy } : null);
      return;
    }
    if (d.mode === 'pinch') {
      const [p, q] = [...pointers.current.values()];
      if (!p || !q) return;
      const scale = clampScale(d.s0 * (Math.hypot(p.x - q.x, p.y - q.y) / d.d0));
      const mx = (p.x + q.x) / 2, my = (p.y + q.y) / 2, { w, h } = size.current;
      setView({ scale, x: d.wx - (mx - w / 2) / scale, z: d.wz + (my - h / 2) / scale });
      return;
    }
    const p = toWorld(sx, sy);
    if (d.mode === 'pan') setView({ ...view.current, x: d.vx - (sx - d.sx) / view.current.scale, z: d.vz + (sy - d.sy) / view.current.scale });
    else if (d.mode === 'move') onCrop({ ...cropRef.current, centerX: Math.round(p.x - d.ox), centerZ: Math.round(p.z - d.oz) }, false);
    else {
      const s = Math.round(Math.min(CS2_MAX_CROP, Math.max(MIN_CROP, Math.abs(p.x - d.ax), Math.abs(p.z - d.az))));
      onCrop({ size: s, centerX: Math.round(d.ax + (d.sgx * s) / 2), centerZ: Math.round(d.az + (d.sgz * s) / 2) }, false);
    }
  };
  const onUp = (e: React.PointerEvent) => {
    pointers.current.delete(e.pointerId);
    if (over.current!.hasPointerCapture(e.pointerId)) over.current!.releasePointerCapture(e.pointerId);
    const d = drag.current;
    if (d?.mode === 'pinch') { if (!pointers.current.size) drag.current = null; return; }
    drag.current = null;
    setCursor('grab');
    if (d && (d.mode === 'move' || d.mode === 'resize')) onCrop(cropRef.current, true);
  };
  const zoomAt = (sx: number, sy: number, k: number) => {
    const before = toWorld(sx, sy);
    const v = { ...view.current, scale: clampScale(view.current.scale * k) };
    const after = toWorld(sx, sy, v);
    setView({ ...v, x: v.x + before.x - after.x, z: v.z + before.z - after.z });
  };
  const onWheel = (e: React.WheelEvent) => { const { sx, sy } = local(e); zoomAt(sx, sy, Math.exp(-e.deltaY * 0.0015)); };

  // Keyboard: arrows move the crop (Shift ×10), Ctrl+arrows pan, +/− resize, [ ] zoom, F fit city, C zoom to crop.
  const commitTimer = useRef(0);
  const onKey = (e: React.KeyboardEvent) => {
    const c = cropRef.current;
    const step = e.shiftKey ? 500 : 50;
    const dirs: Record<string, [number, number]> = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, 1], ArrowDown: [0, -1] };
    let next: Crop | null = null;
    if (dirs[e.key]) {
      const [dx, dz] = dirs[e.key];
      if (e.ctrlKey || e.metaKey) { const k = 80 / view.current.scale; setView({ ...view.current, x: view.current.x + dx * k, z: view.current.z + dz * k }); }
      else next = { ...c, centerX: c.centerX + dx * step, centerZ: c.centerZ + dz * step };
    } else if (e.key === '+' || e.key === '=') next = { ...c, size: Math.min(CS2_MAX_CROP, c.size + (e.shiftKey ? 1000 : 250)) };
    else if (e.key === '-' || e.key === '_') next = { ...c, size: Math.max(MIN_CROP, c.size - (e.shiftKey ? 1000 : 250)) };
    else if (e.key === ']' || e.key === 'PageUp') { const { w, h } = size.current; zoomAt(w / 2, h / 2, 1.25); }
    else if (e.key === '[' || e.key === 'PageDown') { const { w, h } = size.current; zoomAt(w / 2, h / 2, 0.8); }
    else if (e.key === 'f' || e.key === 'F') fitCity();
    else if (e.key === 'c' || e.key === 'C') fitCrop();
    else return;
    e.preventDefault();
    if (next) {
      // update the ref now: several key events can arrive before React re-renders
      next.size = Math.min(CS2_MAX_CROP, Math.max(MIN_CROP, next.size));
      cropRef.current = next;
      onCrop(next, false);
      clearTimeout(commitTimer.current);
      commitTimer.current = window.setTimeout(() => onCrop(cropRef.current, true), 350);
    }
  };

  return (
    <div className="map" ref={wrap}>
      <canvas ref={base} className="map-base" aria-hidden="true" />
      <canvas ref={over} className="map-over" style={{ cursor }}
        tabIndex={0} role="application" aria-roledescription="map"
        aria-label={`City map with the crop square, ${(crop.size / 1000).toFixed(2)} km centred on ${crop.centerX}, ${crop.centerZ}.`}
        aria-describedby="map-keys"
        onFocus={() => setFocused(true)} onBlur={() => setFocused(false)} onKeyDown={onKey}
        onPointerDown={onDown} onPointerMove={onMove} onPointerUp={onUp} onPointerCancel={onUp} onPointerLeave={() => setHover(null)} onWheel={onWheel}
        onContextMenu={e => e.preventDefault()} />
      <p id="map-keys" className="sr-only">Arrow keys move the crop square, Shift for 500 m steps. Plus and minus resize it. Control plus arrows pan the map, square brackets zoom, F shows the whole city, C zooms to the crop.</p>
      <div className="map-tools">
        <button onClick={fitCity} title="Show the whole network (F)">Fit city</button>
        <button onClick={fitCrop} title="Zoom to the crop (C)">Zoom to crop</button>
      </div>
      <Legend />
      {hover && (
        <div className="tip" style={{ left: Math.max(4, Math.min(hover.x + 14, size.current.w - 260)), top: hover.y + 14 }} role="tooltip">
          {describe(hover.s).map((l, i) => <div key={i} className={i ? '' : 'tip-title'}>{l}</div>)}
        </div>
      )}
    </div>
  );
});

function Legend() {
  const cats = ['road', 'highway', 'train', 'metro', 'pedestrian'] as const;
  return (
    <div className="legend" aria-label="Map legend">
      {cats.map(c => <span key={c}><i style={{ background: CATEGORY_STYLE[c].color }} />{CATEGORY_STYLE[c].label}</span>)}
      <span><i style={{ background: 'rgba(136,0,0,0.9)' }} />IMT fill</span>
      <span><i style={{ background: '#ffd43b' }} />Crosses crop edge (left out)</span>
    </div>
  );
}

const clampScale = (s: number) => Math.min(20, Math.max(0.01, s));
const viewEquals = (a: View, b: View) => a.x === b.x && a.z === b.z && a.scale === b.scale;

function niceStep(v: number) {
  const p = 10 ** Math.floor(Math.log10(v));
  const m = v / p;
  return (m >= 5 ? 5 : m >= 2 ? 2 : 1) * p;
}
