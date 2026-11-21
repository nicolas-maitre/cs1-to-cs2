// What will and won't convert, per category, for the UI's "Conversion" panel and the package README.
// Also the .cs1x section check shown after opening a file.

import type { BuiltPackage } from './plan.ts';
import { terrainSummary } from './plan.ts';
import type { Cs1City } from './types.ts';
import { fmtNumber, listOf, translator } from './i18n/index.ts';

/** absent = the export has no such section; none = it has, but there is nothing of that kind in the city or the crop. */
export type ConvStatus = 'converted' | 'partial' | 'report-only' | 'not-converted' | 'none' | 'absent';

export interface ConvCategory {
  id: string;
  label: string;
  status: ConvStatus;
  /** One line: what happens. */
  summary: string;
  /** Details: what converts, what is approximated, what is dropped. */
  details: string[];
}

/** Sections a .cs1x can carry (schema/README.md §1); the M2 ones may be absent in older exports. */
export const KNOWN_SECTIONS: { id: string; label: string; milestone: 'M1' | 'M2' | 'M3' }[] = [
  { id: 'network', label: 'Road network', milestone: 'M1' },
  { id: 'netinfos', label: 'Road types (NetInfos)', milestone: 'M1' },
  { id: 'imt', label: 'IMT markings', milestone: 'M1' },
  { id: 'terrain', label: 'Terrain heightmap', milestone: 'M1' },
  { id: 'water', label: 'Water', milestone: 'M2' },
  { id: 'zones', label: 'Zoning', milestone: 'M2' },
  { id: 'tmpe', label: 'TM:PE traffic rules', milestone: 'M2' },
  { id: 'nodecontroller', label: 'Node Controller', milestone: 'M2' },
  { id: 'transit', label: 'Transit lines', milestone: 'M2' },
  { id: 'buildings', label: 'Buildings', milestone: 'M3' },
  { id: 'assets', label: 'Asset catalogue', milestone: 'M3' },
  { id: 'districts', label: 'Districts and park areas', milestone: 'M3' },
  { id: 'trees', label: 'Trees', milestone: 'M3' },
  { id: 'props', label: 'Props', milestone: 'M3' },
];

export interface SectionCheck { id: string; label: string; status: string; messages: string[] }

/** Status of each known section in the file, with the exporter's and reader's messages. */
export function sectionCheck(city: Cs1City): { sections: SectionCheck[]; failed: string[]; warnings: string[] } {
  const st = city.manifest.sections ?? {};
  const sections = KNOWN_SECTIONS.map(s => {
    const status = st[s.id] ?? (city.source === 'm0-dump' ? (['network', 'netinfos', 'imt'].includes(s.id) ? 'ok' : 'absent') : 'absent');
    const messages = city.report.filter(r => r.section === s.id && r.level !== 'info').map(r => r.message);
    return { id: s.id, label: s.label, status, messages };
  });
  for (const [id, status] of Object.entries(st)) {
    if (!KNOWN_SECTIONS.some(s => s.id === id)) sections.push({ id, label: id, status, messages: [] });
  }
  const failed = sections.filter(s => s.status === 'failed').map(s => s.label);
  const warnings = city.report.filter(r => r.level === 'warning' || r.level === 'error').map(r => `${r.section ? r.section + ': ' : ''}${r.message}`);
  return { sections, failed, warnings };
}


/** Per-category conversion summary for a built package, in the package's language. M2/M3 categories come in `extraSummary`. */
export function conversionSummary(city: Cs1City, pkg: BuiltPackage): ConvCategory[] {
  const lang = pkg.lang ?? 'en';
  const t = translator(lang);
  const out: ConvCategory[] = [];
  const st = pkg.plan.stats;
  const by = st.byPrefabType;
  const items = pkg.report.items;
  const count = (kind: string) => items.filter(i => i.kind === kind).reduce((a, i) => a + (i.count ?? 1), 0);
  const roads = by.RoadBuilderPrefab ?? 0, vanillaRoads = by.RoadPrefab ?? 0, tracks = by.TrackPrefab ?? 0, paths = by.PathwayPrefab ?? 0;
  const widthLoss = items.filter(i => i.kind === 'width-loss').length;
  const matchedTypes = Object.values(pkg.plan.prefabs).filter(p => p.matched).map(p => `${p.source} → ${p.name}`);
  out.push({
    id: 'roads', label: t('cat.roads'), status: roads + vanillaRoads ? (widthLoss ? 'partial' : 'converted') : 'not-converted',
    summary: vanillaRoads
      ? t('sum.roadsVanilla', { segments: roads + vanillaRoads, rb: st.rbConfigs, vanilla: vanillaRoads })
      : t('sum.roads', { segments: roads, rb: st.rbConfigs }),
    details: [
      t('sum.roads.d1'), t('sum.roads.d2'),
      ...(matchedTypes.length ? [t('sum.roads.vanilla', { n: matchedTypes.length, list: matchedTypes.join(', ') })] : []),
      ...(widthLoss ? [t('sum.roads.d3', { n: widthLoss })] : []), t('sum.roads.d4'),
    ],
  });
  out.push({
    id: 'tracks', label: t('cat.tracks'), status: tracks + paths ? 'converted' : 'none',
    summary: tracks + paths
      ? t('sum.tracks', { list: listOf(lang, [tracks > 0 && t('sum.tracks.tracks', { n: tracks }), paths > 0 && t('sum.tracks.paths', { n: paths })]) })
      : t('sum.tracks.none'),
    details: [t('sum.tracks.d1'), t('sum.tracks.d2'), t('sum.tracks.d3')],
  });
  const imtNone = city.source === 'cs1x' && ['ok', 'partial'].includes(city.manifest.sections?.imt ?? '');
  const nodeFills = count('imt-node-fill'), partial = count('imt-partial-fill'), paint = count('imt-paint'), ov = count('imt-override');
  const surf = pkg.plan.surfaces?.stats.items ?? 0;
  const reported = nodeFills + partial + paint;
  out.push({
    id: 'imt', label: t('cat.imt'), status: city.imtFillers.length ? (ov || surf ? 'partial' : reported ? 'report-only' : 'none') : imtNone ? 'none' : 'absent',
    summary: city.imtFillers.length
      ? (ov + surf + reported ? listOf(lang, [ov > 0 && t('sum.imt.lanes', { n: ov }), surf > 0 && t('sum.imt.surfaces', { n: surf }), reported > 0 && t('sum.imt.reported', { n: reported })]) : t('sum.imt.noneInCrop'))
      : imtNone ? t('sum.imt.noneInCity') : t('sum.imt.absent'),
    details: city.imtFillers.length ? [t('sum.imt.d1'), t('sum.imt.d2'), t('sum.imt.d3')] : [],
  });
  const skippedBuilding = items.filter(i => i.kind === 'not-placeable' && /building/i.test(i.message)).reduce((a, i) => a + (i.count ?? 0), 0);
  const skippedOther = items.filter(i => i.kind === 'not-placeable' && !/building/i.test(i.message)).reduce((a, i) => a + (i.count ?? 0), 0);
  out.push({
    id: 'skipped', label: t('cat.skipped'), status: skippedBuilding + skippedOther ? 'not-converted' : 'none',
    summary: skippedBuilding + skippedOther
      ? t('sum.skipped', { list: listOf(lang, [skippedOther > 0 && t('sum.skipped.other', { n: skippedOther }), skippedBuilding > 0 && t('sum.skipped.building', { n: skippedBuilding })]), n: skippedBuilding + skippedOther })
      : t('sum.skipped.none'),
    details: [t('sum.skipped.d1'), t('sum.skipped.d2')],
  });
  const apply = pkg.plan.water?.apply;
  out.push({
    id: 'terrain', label: t('cat.terrain'), status: city.terrain ? 'converted' : 'absent',
    summary: city.terrain ? terrainSummary(pkg.plan, lang) : t('sum.terrain.absent'),
    details: city.terrain ? [
      t('sum.terrain.d1'),
      apply?.seaLevel != null ? t('sum.terrain.d2water', { sea: fmtNumber(lang, Math.round(apply.seaLevel * 10) / 10, 1), n: apply.sources.length }) : t('sum.terrain.d2dry'),
      ...(pkg.plan.outsideConnections ? [t('sum.terrain.d3', { n: pkg.plan.outsideConnections.counts.connections })] : []),
    ] : [],
  });
  out.push(...(pkg.extraSummary ?? []));
  const bst = city.manifest.sections?.buildings;
  if (bst && bst !== 'absent' && !out.some(c => c.id === 'buildings')) out.push({
    id: 'buildings', label: t('sum.legacyBuildings'), status: 'not-converted', summary: t('sum.legacyBuildings.text'), details: [t('sum.legacyBuildings.d1')],
  });
  // A section whose export failed is not "absent": say so.
  const secStatus = city.manifest.sections ?? {};
  const sectionOf: Record<string, string> = { roads: 'network', tracks: 'network', imt: 'imt', terrain: 'terrain', zoning: 'zones', water: 'water', traffic: 'tmpe', nodecontroller: 'nodecontroller', transit: 'transit', buildings: 'buildings', districts: 'districts', objects: 'trees' };
  for (const c of out) {
    const sec = sectionOf[c.id];
    if (sec && secStatus[sec] === 'failed' && c.status === 'absent') {
      c.status = 'not-converted';
      c.summary = t('sum.failed');
    }
  }
  return out;
}
