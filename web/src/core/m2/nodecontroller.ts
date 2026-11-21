// Node Controller → mostly report only. CS2 builds junction geometry itself; segment-end shifts are converted as
// segment ends off the node centre (ncalign.ts, #107); pedestrian crossings go through the TM:PE crosswalk mapping.

import type { PackageReportItem } from '../types.ts';
import { plural, tally } from './context.ts';
import type { PlanContext, SectionResult } from './context.ts';
import type { NcAlignStats } from './ncalign.ts';

export function buildNodeController(ctx: PlanContext, align?: NcAlignStats | null): SectionResult<never> {
  const nc = ctx.city.nodeController;
  const items: PackageReportItem[] = [];
  const tr = ctx.t, label = tr('cat.nodecontroller');
  if (!nc) return { items, summary: { id: 'nodecontroller', label, status: 'absent', summary: tr('sum.nc.absent'), details: [] } };
  const inPlan = nc.nodes.filter(n => ctx.planNodes.has(n.id));
  const changed = new Map<string, number>();
  const changedIds: number[] = [];
  let noMarkings = 0, custom = 0;
  for (const n of inPlan) {
    if (n.defaultType && n.type !== n.defaultType) { tally(changed, `${n.defaultType} → ${n.type}`); if (changedIds.length < 30) changedIds.push(n.id); }
    if (n.noMarkings || n.segmentEnds?.some(e => e.noMarkings)) noMarkings++;
    // shifts are converted (ncalign.ts) unless the node is free-form; slopes and twists have no CS2 counterpart
    if (n.mode === 'FreeForm' || n.segmentEnds?.some(e => (Number(e.slopeAngle) || 0) !== 0 || (Number(e.twistAngle) || 0) !== 0)) custom++;
  }
  const typeChanges = [...changed.values()].reduce((a, b) => a + b, 0);
  if (typeChanges) items.push({ level: 'info', kind: 'node-controller', message: `${plural(typeChanges, 'node')} with a changed Node Controller type (${[...changed].map(([k, v]) => `${k}: ${v}`).join(', ')}): CS2 decides junction shapes itself`, count: typeChanges, cs1: { nodes: changedIds } });
  if (noMarkings) items.push({ level: 'info', kind: 'node-controller', message: `${plural(noMarkings, 'node')} with hidden markings: not converted`, count: noMarkings });
  if (custom) items.push({ level: 'info', kind: 'node-controller', message: `${plural(custom, 'node')} free-form or with a slope or twist: not converted`, count: custom });
  return {
    items,
    summary: {
      id: 'nodecontroller', label, status: !inPlan.length ? 'none' : align?.ends ? 'partial' : 'report-only',
      summary: inPlan.length ? tr('sum.nc', { n: inPlan.length, changed: typeChanges }) : tr('sum.nc.none'),
      details: [...(align?.ends ? [tr('sum.nc.shifts', { n: align.ends, segments: align.segments })] : []), tr('sum.nc.d1'), tr('sum.nc.d2')],
    },
  };
}
