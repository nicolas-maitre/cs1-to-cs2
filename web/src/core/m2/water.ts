// CS1 water → sea level and natural water sources for the CS2 terrain step (plan.water).
//
// The CS1 depth grid is a snapshot of the simulation; CS2 re-simulates water from its sea level and
// sources, so the plan carries those and only uses the grid to say how wet the crop is.

import type { PackageReportItem, PlanWater } from '../types.ts';
import { plural, r2, tally } from './context.ts';
import type { PlanContext, SectionResult } from './context.ts';

export function buildWater(ctx: PlanContext, enabled = true): SectionResult<PlanWater> {
  const w = ctx.city.water;
  const items: PackageReportItem[] = [];
  const label = 'Water';
  if (!w) return { items, summary: { id: 'water', label, status: 'absent', summary: 'no water data in this export', details: [] } };
  if (!enabled) return { items, summary: { id: 'water', label, status: 'not-converted', summary: 'switched off in the options', details: [] } };

  // Wet share of the crop and deepest point, from the depth grid.
  let wet = 0, total = 0, maxDepth = 0;
  const d = w.depth, g = w.depthGrid;
  if (d && g) {
    const n = d.resolution, sp = d.size / (n - 1);
    const b = { minX: ctx.crop.centerX - ctx.crop.size / 2, minZ: ctx.crop.centerZ - ctx.crop.size / 2 };
    const c0 = Math.max(0, Math.ceil((b.minX + d.size / 2) / sp)), c1 = Math.min(n - 1, Math.floor((b.minX + ctx.crop.size + d.size / 2) / sp));
    const r0 = Math.max(0, Math.ceil((b.minZ + d.size / 2) / sp)), r1 = Math.min(n - 1, Math.floor((b.minZ + ctx.crop.size + d.size / 2) / sp));
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) {
      total++;
      const v = g[r * n + c] * d.heightScale;
      if (v > 0.05) { wet++; if (v > maxDepth) maxDepth = v; }
    }
  }
  const byType = new Map<string, number>();
  const sources: PlanWater['sources'] = [];
  let sourcesOutside = 0;
  for (const s of w.sources) {
    tally(byType, s.type);
    if (s.type !== 'natural') continue; // pumps, drains and cleaners belong to buildings (M3)
    const p = s.outputPosition ?? s.inputPosition;
    if (!ctx.inCrop(p[0], p[2])) { sourcesOutside++; continue; }
    sources.push({
      cs1Index: s.index ?? sources.length, x: r2(p[0] + ctx.dx), z: r2(p[2] + ctx.dz), y: r2(p[1]),
      targetHeight: s.targetHeight != null ? r2(s.targetHeight) : null, outputRate: s.outputRate ?? null,
    });
  }
  const facility = (byType.get('facility') ?? 0) + (byType.get('cleaner') ?? 0);
  const wetFraction = total ? Math.round((wet / total) * 1000) / 1000 : null;
  const sea = w.seaLevel != null ? r2(w.seaLevel) : null;
  items.push({ level: 'info', kind: 'water', message: `sea level ${sea ?? 'unknown'} m (CS1 datum = CS2 datum once the terrain step has run); ${wetFraction != null ? `${(wetFraction * 100).toFixed(1)} % of the crop is under water, deepest ${maxDepth.toFixed(1)} m` : 'no depth grid'}` });
  if (sources.length || sourcesOutside) items.push({ level: 'info', kind: 'water', message: `${plural(sources.length, 'natural water source')} in the crop${sourcesOutside ? `, ${sourcesOutside} outside` : ''}` });
  if (facility) items.push({ level: 'info', kind: 'water', message: `${plural(facility, 'pump, drain or cleaner source')} belong to water buildings and are not converted (buildings come later)`, count: facility });
  return {
    section: { seaLevel: sea, wetFraction, maxDepth: r2(maxDepth), sources },
    items,
    summary: {
      id: 'water', label, status: sea != null || sources.length ? 'converted' : 'not-converted',
      summary: `sea level ${sea ?? '?'} m${sources.length ? `, ${plural(sources.length, 'natural source')}` : ''}${wetFraction != null ? `; ${(wetFraction * 100).toFixed(0)} % of the crop is water` : ''}`,
      details: [
        'The terrain step sets the CS2 sea level from CS1 and places the natural water sources (rivers, lakes) at their CS1 positions and target heights.',
        'CS2 then simulates the water itself, so rivers and lakes refill over the first in-game minutes.',
        ...(facility ? [`${plural(facility, 'water-building source')} (pumps, drains) come with buildings later.`] : []),
      ],
    },
  };
}
