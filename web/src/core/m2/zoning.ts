// CS1 zone blocks → zoned cells for CS2 (schema/README.md §1 zones.json, §2 plan.zoning).
//
// CS2 makes its own zone blocks along roads that generate zoning (vanilla roads, and our RB roads
// with GeneratesZoningBlocks). The plan lists the centre of every zoned CS1 cell in CS2 coordinates
// with a CS2 zone prefab; the importer paints the CS2 cell under each point.

import type { PlanZoning } from '../types.ts';
import type { PackageReportItem } from '../types.ts';
import { plural, r1, tally } from './context.ts';
import type { PlanContext, SectionResult } from './context.ts';

export type ZoneTheme = 'EU' | 'NA';

export interface ZoningOptions {
  enabled: boolean;
  /** CS2 building theme for residential and commercial zones. */
  theme: ZoneTheme;
  /** CS2 density for CS1 high-density residential. */
  residentialHigh: 'High' | 'Medium' | 'Mixed';
  /** CS2 density for CS1 office. */
  office: 'High' | 'Low';
}

export const DEFAULT_ZONING: ZoningOptions = { enabled: true, theme: 'NA', residentialHigh: 'High', office: 'High' };

/** CS1 zone name → CS2 zone prefab (names from the CS2 prefab list), or null for "not a zone". */
export function zonePrefab(cs1Zone: string, o: ZoningOptions): string | null {
  const t = o.theme;
  switch (cs1Zone) {
    case 'ResidentialLow': return `${t} Residential Low`;
    case 'ResidentialHigh': return `${t} Residential ${o.residentialHigh}`;
    case 'CommercialLow': return `${t} Commercial Low`;
    case 'CommercialHigh': return `${t} Commercial High`;
    case 'Industrial': return 'Industrial Manufacturing';
    case 'Office': return `Office ${o.office}`;
    default: return null; // Unzoned, Distant, None
  }
}

/** World XZ centre of cell (column c, row r) of a CS1 zone block (formula from schema §1 zones.json). */
export function cellCentre(px: number, pz: number, angle: number, c: number, r: number, cell = 8): [number, number] {
  const cx = Math.cos(angle) * cell, cz = Math.sin(angle) * cell; // column direction
  const rx = Math.sin(angle) * cell, rz = -Math.cos(angle) * cell; // row direction
  return [px + (c - 3.5) * cx + (r - 3.5) * rx, pz + (c - 3.5) * cz + (r - 3.5) * rz];
}

export function buildZoning(ctx: PlanContext, o: ZoningOptions): SectionResult<PlanZoning> {
  const z = ctx.city.zones;
  const items: PackageReportItem[] = [];
  const label = 'Zoning';
  if (!z) return { items, summary: { id: 'zoning', label, status: 'absent', summary: 'no zoning in this export', details: [] } };
  if (!z.blocks.some(b => /[^-0]/.test(b.zones))) return { items, summary: { id: 'zoning', label, status: 'absent', summary: 'the city has no zoned areas', details: [] } };
  if (!o.enabled) return { items, summary: { id: 'zoning', label, status: 'not-converted', summary: 'switched off in the options', details: [] } };
  const cols = z.columns || 4;
  const prefabs: string[] = [];
  const prefabIdx = new Map<string, number>();
  const cells: [number, number, number][] = [];
  const byZone = new Map<string, number>();
  let outside = 0, noRoad = 0, blocksUsed = 0;
  for (const b of z.blocks) {
    let used = false;
    for (let i = 0; i < b.zones.length; i++) {
      const ch = b.zones[i];
      if (ch === '-') continue;
      const name = z.zoneCodes[String(parseInt(ch, 16))] ?? 'Unknown';
      const prefab = zonePrefab(name, o);
      if (!prefab) continue;
      const r = Math.floor(i / cols), c = i % cols;
      const [x, zz] = cellCentre(b.position[0], b.position[2], b.angle, c, r, z.cellSize || 8);
      if (!ctx.inCrop(x, zz)) { outside++; continue; }
      if (!ctx.planSegments.has(b.segment)) { noRoad++; continue; }
      let k = prefabIdx.get(prefab);
      if (k === undefined) { k = prefabs.length; prefabs.push(prefab); prefabIdx.set(prefab, k); }
      cells.push([r1(x + ctx.dx), r1(zz + ctx.dz), k]);
      tally(byZone, name);
      used = true;
    }
    if (used) blocksUsed++;
  }
  const breakdown = [...byZone].sort((a, b) => b[1] - a[1]).map(([n, c]) => `${n} ${c.toLocaleString('en')} → ${zonePrefab(n, o)}`);
  items.push({ level: 'info', kind: 'zoning', message: `${plural(cells.length, 'zoned cell')} in ${plural(blocksUsed, 'block')}: ${breakdown.join('; ')}`, count: cells.length });
  if (noRoad) items.push({ level: 'info', kind: 'zoning', message: `${plural(noRoad, 'zoned cell')} in the crop belong to roads that are not in the plan (crossing the crop edge or not converted) and are left out`, count: noRoad });
  items.push({ level: 'info', kind: 'zoning', message: 'CS1 district specialisations (self-sufficient, leisure, tourism, IT, specialised industry) are not in the export: those cells get the generic CS2 zone.' });
  return {
    section: { theme: o.theme, cellSize: z.cellSize || 8, prefabs, cells, stats: { cells: cells.length, blocks: blocksUsed, outsideCrop: outside, roadNotInPlan: noRoad } },
    items,
    summary: {
      id: 'zoning', label, status: cells.length ? 'converted' : 'not-converted',
      summary: cells.length ? `${plural(cells.length, 'zoned cell')} as ${o.theme} zones` : 'no zoned cells in the crop',
      details: [
        ...breakdown,
        'CS2 creates zone blocks along the imported roads; the importer paints the cell under each CS1 cell centre.',
        'District specialisations are not exported, so specialised cells get the generic zone.',
      ],
    },
  };
}
