// What will and won't convert, per category, for the UI's "Conversion" panel and the package README.
// Also the .cs1x section check shown after opening a file.

import type { BuiltPackage } from './plan.ts';
import type { Cs1City } from './types.ts';

export type ConvStatus = 'converted' | 'partial' | 'report-only' | 'not-converted' | 'absent';

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

const plural = (n: number, w: string) => `${n.toLocaleString('en')} ${w}${n === 1 ? '' : 's'}`;

/** Per-category conversion summary for a built package. M2 categories extend this through `extra`. */
export function conversionSummary(city: Cs1City, pkg: BuiltPackage): ConvCategory[] {
  const out: ConvCategory[] = [];
  const st = pkg.plan.stats;
  const by = st.byPrefabType;
  const items = pkg.report.items;
  const count = (kind: string) => items.filter(i => i.kind === kind).reduce((t, i) => t + (i.count ?? 1), 0);
  const roads = by.RoadBuilderPrefab ?? 0, vanillaRoads = by.RoadPrefab ?? 0, tracks = by.TrackPrefab ?? 0, paths = by.PathwayPrefab ?? 0;
  const widthLoss = items.filter(i => i.kind === 'width-loss').length;
  out.push({
    id: 'roads', label: 'Roads', status: roads + vanillaRoads ? (widthLoss ? 'partial' : 'converted') : 'not-converted',
    summary: `${plural(roads + vanillaRoads, 'segment')} as ${plural(st.rbConfigs, 'generated Road Builder road')}${vanillaRoads ? ` and ${vanillaRoads} vanilla` : ''}`,
    details: [
      'Lane layout, medians, parking, bus and tram lanes, trees and speed are rebuilt with Road Builder.',
      'Elevated roads, bridges, slopes and tunnels keep their height; Road Builder makes those forms itself.',
      ...(widthLoss ? [`${plural(widthLoss, 'road type')} come out narrower or wider than in CS1 (Road Builder sidewalks max 7 m, fixed lane widths).`] : []),
      'Painted bike lanes inside the carriageway become sidewalk bike lanes at the edge only.',
    ],
  });
  out.push({
    id: 'tracks', label: 'Rail, metro and paths', status: tracks + paths ? 'converted' : 'absent',
    summary: `${plural(tracks, 'track segment')}, ${plural(paths, 'path segment')} as vanilla CS2 networks`,
    details: ['Train, metro and tram tracks map to the matching CS2 double, two-way or one-way track.', 'Pedestrian paths become CS2 pavement paths; bike paths become CS2 bike paths.', 'Monorail has no CS2 equivalent and is skipped.'],
  });
  const nodeFills = count('imt-node-fill'), partial = count('imt-partial-fill'), paint = count('imt-paint'), ov = count('imt-override');
  out.push({
    id: 'imt', label: 'IMT markings', status: city.imtFillers.length ? (ov ? 'partial' : 'report-only') : 'absent',
    summary: city.imtFillers.length ? `${plural(ov, 'segment')} get bus lanes or medians from IMT fills; ${plural(nodeFills + partial + paint, 'fill')} are reported only` : 'no IMT data',
    details: ['A red fill over a whole lane becomes a bus lane; a pavement or grass fill over a whole lane becomes a median.', 'Fills at junctions, partial fills and grey paint have no CS2 equivalent and are listed in the report.'],
  });
  const skippedBuilding = items.filter(i => i.kind === 'not-placeable' && /building/i.test(i.message)).reduce((t, i) => t + (i.count ?? 0), 0);
  const skippedOther = items.filter(i => i.kind === 'not-placeable' && !/building/i.test(i.message)).reduce((t, i) => t + (i.count ?? 0), 0);
  out.push({
    id: 'skipped', label: 'Pipes, power lines, building networks', status: skippedBuilding + skippedOther ? 'not-converted' : 'absent',
    summary: `${plural(skippedOther, 'utility or transport-line segment')} and ${plural(skippedBuilding, 'building sub-network segment')} are not placed`,
    details: ['CS2 lays water and electricity along roads automatically.', 'Station tracks, park paths and other networks inside buildings come with buildings in a later version.'],
  });
  out.push({
    id: 'terrain', label: 'Terrain', status: city.terrain ? 'converted' : 'absent',
    summary: city.terrain ? `heightmap ${city.terrain.resolution}² (${city.terrain.min?.toFixed(0) ?? '?'}–${city.terrain.max?.toFixed(0) ?? '?'} m), applied by the terrain step` : 'no terrain in this file: roads follow the CS2 map\'s ground',
    details: city.terrain ? ['The crop of the CS1 heightmap replaces the CS2 map\'s terrain, so roads sit on the original ground.'] : [],
  });
  out.push(...(pkg.extraSummary ?? []));
  const bst = city.manifest.sections?.buildings;
  if (bst && bst !== 'absent') out.push({
    id: 'buildings', label: 'Buildings, parks, props', status: 'not-converted',
    summary: 'in the export, not converted yet: CS2 grows its own buildings on the converted zones',
    details: ['Service buildings, stations, parks and unique buildings need an asset mapping, planned for a later version.'],
  });
  // A section whose export failed is not "absent": say so.
  const secStatus = city.manifest.sections ?? {};
  const sectionOf: Record<string, string> = { roads: 'network', tracks: 'network', imt: 'imt', terrain: 'terrain', zoning: 'zones', water: 'water', traffic: 'tmpe', nodecontroller: 'nodecontroller', transit: 'transit' };
  for (const c of out) {
    const sec = sectionOf[c.id];
    if (sec && secStatus[sec] === 'failed' && c.status === 'absent') {
      c.status = 'not-converted';
      c.summary = 'the export of this section failed: export the city again';
    }
  }
  return out;
}
