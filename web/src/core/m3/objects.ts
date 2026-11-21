// Free-standing trees and rocks (plan.objects) and IMT junction fills as CS2 surfaces (plan.surfaces).
// Both sections come from the buildings worker's buildings/lib/objects.mjs (pure, shared with its CLI and the
// e2e plug-in), so the web package and the scripted flow give the CS2 importer the same rows. This adapter
// only adds the report lines and the "What converts" entries.

import { buildObjectsSection, buildSurfacesSection, DEFAULT_SURFACE_MAP } from '../../../../buildings/lib/objects.mjs';
import type { PackageReportItem, PlanObjects, PlanSurfaces } from '../types.ts';
import { plural } from '../m2/context.ts';
import type { PlanContext, SectionResult } from '../m2/context.ts';
import type { Cs2Catalogue } from './buildings.ts';

export const SURFACE_MAP: Record<string, string> = DEFAULT_SURFACE_MAP;

/** CS2 trees (Vegetation objects with a TreeObject) and the boulders the vegetation matcher picks from. */
export const isCs2Vegetation = (p: { name: string; kind?: string; uiGroup?: unknown; components?: unknown; flags?: unknown }) =>
  p.kind === 'object' && ((p.uiGroup === 'Vegetation' && (!!(p.components as Record<string, unknown> | undefined)?.TreeObject || ((p.flags as string[] | undefined) ?? []).includes('TreeObject'))) || /^Boulder0\d$/.test(p.name));

const roadPlanOf = (ctx: PlanContext) => ({ crop: ctx.crop, transform: { dx: ctx.dx, dz: ctx.dz } });

export function buildObjects(ctx: PlanContext, o: { enabled: boolean; catalogue: Cs2Catalogue | null; theme?: string }): SectionResult<PlanObjects> {
  const items: PackageReportItem[] = [];
  const tr = ctx.t, label = tr('cat.objects');
  const { trees, props, assets } = ctx.city;
  const none = (status: 'absent' | 'not-converted' | 'none', summary: string, details: string[] = []): SectionResult<PlanObjects> => ({ items, summary: { id: 'objects', label, status, summary, details } });
  if (!trees && !props) return none('absent', tr('sum.objects.absent'));
  const inCropCount = (rows: number[][] | undefined) => (rows ?? []).filter(r => ctx.inCrop(r[1], r[3])).length;
  const treesIn = inCropCount(trees?.trees), propsIn = inCropCount(props?.props);
  if (!treesIn && !propsIn) return none('none', tr('sum.objects.none'));
  if (!o.enabled) return none('not-converted', tr('sum.off'));
  if (!o.catalogue) return none('not-converted', tr('sum.objects.noCatalogue', { n: treesIn }), [tr('sum.objects.noCatalogue.d1')]);
  if (!o.catalogue.prefabs.some(p => isCs2Vegetation(p as never))) {
    return none('not-converted', tr('sum.objects.noTrees'), [tr('sum.objects.noTrees.d1')]);
  }
  const section = buildObjectsSection({ trees: trees ?? undefined, props: props ?? undefined, assets: assets ?? {} }, o.catalogue, roadPlanOf(ctx), { theme: o.theme }) as PlanObjects;
  const types = Object.entries(section.stats.byType).sort((a, b) => b[1].count - a[1].count);
  const placed = types.filter(([, t]) => t.decision === 'place');
  const notPlaced = types.filter(([, t]) => t.decision !== 'place');
  const treesPlaced = placed.filter(([n]) => trees?.infos.includes(n)).reduce((s, [, t]) => s + t.count, 0);
  const rocks = section.stats.items - treesPlaced;
  items.push({ level: 'info', kind: 'objects', message: `${plural(section.stats.items, 'tree or rock')} placed as ${plural(section.prefabs.length, 'CS2 object type')}: ${placed.slice(0, 12).map(([n, t]) => `${n} ${t.count.toLocaleString('en')} → ${t.cs2}`).join('; ')}${placed.length > 12 ? '; …' : ''}`, count: section.stats.items });
  for (const [n, t] of notPlaced.slice(0, 20)) items.push({ level: 'info', kind: 'objects', message: `${n}: ${plural(t.count, 'instance')} not placed (${t.reason})`, count: t.count });
  if (notPlaced.length > 20) items.push({ level: 'info', kind: 'objects', message: `${notPlaced.length - 20} more prop types are not placed`, count: notPlaced.slice(20).reduce((s, [, t]) => s + t.count, 0) });
  const skippedProps = section.stats.skipped.noEquivalent;
  return {
    section, items,
    summary: {
      id: 'objects', label, status: !section.stats.items ? 'not-converted' : skippedProps ? 'partial' : 'converted',
      summary: section.stats.items ? (rocks ? tr('sum.objectsRocks', { trees: treesPlaced, rocks }) : tr('sum.objects', { trees: treesPlaced })) : tr('sum.objects.noneMatched'),
      details: [
        tr('sum.objects.d1'),
        tr('sum.objects.d2'),
        ...(skippedProps ? [tr('sum.objects.d3', { n: skippedProps })] : []),
        tr('sum.objects.d4'),
      ],
    },
  };
}

export function buildSurfaces(ctx: PlanContext, enabled: boolean): SectionResult<PlanSurfaces> {
  const items: PackageReportItem[] = [];
  const nodeFills = ctx.city.imtFillers.filter(f => f.markingType === 'Node');
  if (!enabled || !nodeFills.length) return { items, summary: { id: 'surfaces', label: 'Junction surfaces', status: 'absent', summary: '', details: [] } };
  const section = buildSurfacesSection({ fillers: nodeFills }, roadPlanOf(ctx), {}) as PlanSurfaces;
  if (section.stats.items) {
    const byPrefab = new Map<string, number>();
    for (const s of section.items) byPrefab.set(s.prefab, (byPrefab.get(s.prefab) ?? 0) + 1);
    items.push({ level: 'info', kind: 'surfaces', message: `${plural(section.stats.items, 'IMT junction fill')} become CS2 surfaces: ${[...byPrefab].map(([p, n]) => `${n} ${p}`).join(', ')}`, count: section.stats.items });
  }
  return {
    section: section.stats.items ? section : undefined,
    items,
    summary: { id: 'surfaces', label: 'Junction surfaces', status: section.stats.items ? 'converted' : 'not-converted', summary: '', details: [] },
  };
}
