import { useEffect, useMemo, useRef } from 'react';
import { cs1Heights, sampleCs1 } from '../core/terrain/heightmap.ts';
import type { Crop, Cs1City } from '../core/types.ts';
import { useI18n } from './i18n.tsx';

const N = 256;

/** Hillshade of the crop as CS2 will get it (north up), with CS1 water (depth grid, else below sea level) in blue. */
export function TerrainPreview({ city, crop, seaLevel }: { city: Cs1City; crop: Crop; seaLevel: number | null }) {
  const { t, n } = useI18n();
  const canvas = useRef<HTMLCanvasElement>(null);
  const terrain = city.terrain;
  const heights = useMemo(() => (terrain ? cs1Heights(terrain) : null), [terrain]);
  const stats = useMemo(() => {
    if (!terrain || !heights) return null;
    const meta = { resolution: terrain.resolution, size: terrain.size, heightScale: terrain.heightScale };
    const w = city.water, d = w?.depth, g = w?.depthGrid;
    const h = new Float32Array(N * N), wet = new Uint8Array(N * N);
    let min = Infinity, max = -Infinity;
    const cell = crop.size / N;
    for (let r = 0; r < N; r++) for (let c = 0; c < N; c++) {
      const x = crop.centerX - crop.size / 2 + (c + 0.5) * cell, z = crop.centerZ + crop.size / 2 - (r + 0.5) * cell; // row 0 = north
      const v = sampleCs1(meta, heights, x, z);
      h[r * N + c] = v;
      if (v < min) min = v;
      if (v > max) max = v;
      if (d && g) {
        const sp = d.size / (d.resolution - 1);
        const gc = Math.round((x + d.size / 2) / sp), gr = Math.round((z + d.size / 2) / sp);
        if (gc >= 0 && gr >= 0 && gc < d.resolution && gr < d.resolution && g[gr * d.resolution + gc] * d.heightScale > 0.3) wet[r * N + c] = 1;
      } else if (seaLevel != null && v < seaLevel) wet[r * N + c] = 1;
    }
    return { h, wet, min, max, cell };
  }, [terrain, heights, city.water, crop, seaLevel]);

  useEffect(() => {
    const cv = canvas.current;
    if (!cv || !stats) return;
    const ctx = cv.getContext('2d')!;
    const img = ctx.createImageData(N, N);
    const { h, wet, min, max, cell } = stats;
    const range = Math.max(1, max - min);
    const H = (c: number, r: number) => h[Math.min(N - 1, Math.max(0, r)) * N + Math.min(N - 1, Math.max(0, c))];
    for (let r = 0; r < N; r++) for (let c = 0; c < N; c++) {
      const dzdx = (H(c + 1, r) - H(c - 1, r)) / (2 * cell), dzdy = (H(c, r - 1) - H(c, r + 1)) / (2 * cell); // row − 1 is north
      const nx = -dzdx * 4, ny = -dzdy * 4, len = Math.hypot(nx, 1, ny);
      const shade = Math.max(0, (nx * -0.6 + 0.55 + ny * 0.6) / len);
      const k = 0.5 + 0.8 * shade, e = (H(c, r) - min) / range;
      const o = (r * N + c) * 4;
      const [R, G, B] = wet[r * N + c] ? [40, 90, 150] : [60 + 90 * e, 80 + 60 * e, 55 + 30 * e];
      img.data[o] = Math.min(255, R * k); img.data[o + 1] = Math.min(255, G * k); img.data[o + 2] = Math.min(255, B * k); img.data[o + 3] = 255;
    }
    ctx.putImageData(img, 0, 0);
  }, [stats]);

  if (!terrain || !stats) return <p className="muted small">{t('tp.none')}</p>;
  const lo = Math.round(stats.min), hi = Math.round(stats.max);
  const sea = seaLevel != null ? n(Math.round(seaLevel * 10) / 10, 1) : null;
  const caption = [
    t('tp.square', { km: n(crop.size / 1000, 2) }),
    hi - lo < 1 ? t('tp.flat', { h: lo }) : t('tp.ground', { min: lo, max: hi }),
    ...(sea != null ? [t('tp.sea', { sea })] : []),
    ...(stats.wet.some(v => v) ? [t('tp.water')] : []),
  ].join(' · ');
  return (
    <figure className="terrain-preview">
      <canvas ref={canvas} width={N} height={N} role="img" aria-label={t('tp.aria', { min: lo, max: hi }) + (sea != null ? t('tp.ariaSea', { sea }) : '')} />
      <figcaption className="small muted">{caption}</figcaption>
    </figure>
  );
}
