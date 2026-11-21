// NetInfo → CS2 target. Roads get a generated Road Builder config (rb.ts); tracks and paths map to
// vanilla CS2 prefabs; everything else (pipes, power lines, transport lines, flight/ship paths,
// building-internal connections, monorail, cable car) is not placeable and is reported.
//
// CS2 prefab names: "Small Road", "Medium Road", "Pedestrian Street" and "Double Train Track" are
// confirmed (M0 spikes / Road Builder source). The other names come from the CS2 prefab name list
// in the game's Locale.cok (Assets.NAME[<prefab>] keys), see web/DECISIONS-web.md.

import type { NetInfo, PrefabType } from './types.ts';

export type NetCategory = 'road' | 'highway' | 'train' | 'metro' | 'tram' | 'monorail' | 'pedestrian' | 'bike' | 'utility' | 'transport-line' | 'building' | 'other';

export interface VanillaTarget { type: PrefabType; name: string }

export type MappingOverride =
  | { mode: 'auto' }
  | { mode: 'rb' }
  | { mode: 'vanilla'; type: PrefabType; name: string }
  | { mode: 'skip' };

export type ResolvedTarget =
  | { mode: 'rb' }
  | { mode: 'vanilla'; type: PrefabType; name: string }
  | { mode: 'skip'; reason: string; kind: 'not-placeable' | 'unmapped-netinfo' };

/** Vanilla CS2 prefabs offered in the UI's override list (all present in the CS2 prefab name list). */
export const VANILLA_CHOICES: VanillaTarget[] = [
  ...['Small Road', 'Small Road Oneway', 'Small Road Oneway - 3 lanes', 'Medium Road', 'Medium Road Oneway', 'Medium Road Divided', 'Large Road', 'Large Road Oneway', 'Large Road Divided',
    'Alley', 'Gravel Road', 'Pedestrian Street', 'Highway', 'Highway Oneway - 1 lane', 'Highway Oneway - 2 lanes', 'Highway Oneway - 3 lanes', 'Highway Oneway - 4 lanes', 'Highway Twoway - 2 lanes']
    .map(name => ({ type: 'RoadPrefab' as const, name })),
  ...['Double Train Track', 'Oneway Train Track', 'Twoway Train Track', 'Double Subway Track', 'Oneway Subway Track', 'Twoway Subway Track', 'Double Tram Track', 'Oneway Tram Track']
    .map(name => ({ type: 'TrackPrefab' as const, name })),
  ...['Pavement Path', 'TwoWayBikePath', 'OneWayBikePath'].map(name => ({ type: 'PathwayPrefab' as const, name })),
];

const ai = (i: NetInfo) => i.ai ?? '';

/** Category of a NetInfo (use the ground info: parent, when known). */
export function categorize(info: NetInfo): NetCategory {
  const a = ai(info), svc = info.service ?? '', sub = info.subService ?? '';
  if (/TransportLineAI|FlightPathAI|ShipPathAI|ShipDockAI|CableCarPathAI|CargoPathAI|TransportPathAI/.test(a)) return 'transport-line';
  if (/WaterPipeAI|PowerLineAI|HeatingPipeAI|DamAI|CanalAI|QuayAI|FloodWallAI|WaterAI/.test(a)) return 'utility';
  if (/Monorail/i.test(a) || /Monorail/.test(sub)) return 'monorail';
  if (/^Train/.test(a) || sub === 'PublicTransportTrain') return 'train';
  if (/^Metro/.test(a) || sub === 'PublicTransportMetro') return 'metro';
  if (/^Tram/.test(a) || sub === 'PublicTransportTram') return 'tram';
  if (/^Pedestrian/.test(a) || /^Bicycle/.test(a)) {
    if (/Connection/.test(info.name)) return 'building';
    const lanes = info.lanes.map(l => String(l.fields.m_vehicleType ?? ''));
    const ped = info.lanes.some(l => /Pedestrian/.test(String(l.fields.m_laneType ?? '')));
    return !ped && lanes.some(v => /Bicycle/.test(v)) ? 'bike' : 'pedestrian';
  }
  if (/^Road|^PedestrianZoneRoad|^RoadBase/.test(a) || (svc === 'Road' && /Road/.test(a))) {
    return info.aiFields?.m_highwayRules === true ? 'highway' : 'road';
  }
  return 'other';
}

/** Track layout from rail lanes: two or more lanes → double, one lane both ways → twoway, else oneway. */
function trackLayout(info: NetInfo, vehicle: RegExp): 'Double' | 'Twoway' | 'Oneway' {
  const rails = info.lanes.filter(l => vehicle.test(String(l.fields.m_vehicleType ?? '')));
  if (rails.length >= 2) return 'Double';
  const dir = String(rails[0]?.fields.m_direction ?? '');
  return /Both/.test(dir) ? 'Twoway' : 'Oneway';
}

/** Default vanilla target for non-road categories. */
export function vanillaTargetFor(info: NetInfo, cat: NetCategory): VanillaTarget | null {
  switch (cat) {
    case 'train': return { type: 'TrackPrefab', name: `${trackLayout(info, /Train/)} Train Track` };
    case 'metro': return { type: 'TrackPrefab', name: `${trackLayout(info, /Metro/)} Subway Track` };
    case 'tram': {
      const l = trackLayout(info, /Tram/);
      return { type: 'TrackPrefab', name: l === 'Oneway' ? 'Oneway Tram Track' : 'Double Tram Track' };
    }
    case 'pedestrian': return { type: 'PathwayPrefab', name: 'Pavement Path' };
    case 'bike': {
      const bikes = info.lanes.filter(l => /Bicycle/.test(String(l.fields.m_vehicleType ?? '')));
      const oneWay = bikes.length === 1 && !/Both/.test(String(bikes[0].fields.m_direction ?? ''));
      return { type: 'PathwayPrefab', name: oneWay ? 'OneWayBikePath' : 'TwoWayBikePath' };
    }
    default: return null;
  }
}

const NOT_PLACEABLE: Partial<Record<NetCategory, string>> = {
  'transport-line': 'transport line / vehicle path, not a network you build (M2: transit lines)',
  utility: 'utility network (pipes, power lines, dams), out of scope for M1',
  building: 'part of a building (sub-network), comes with buildings in M3',
  monorail: 'CS2 has no monorail',
};

/** Resolve the target of a ground NetInfo given an optional user override. */
export function resolveTarget(info: NetInfo, override?: MappingOverride): ResolvedTarget {
  const cat = categorize(info);
  if (override && override.mode !== 'auto') {
    if (override.mode === 'skip') return { mode: 'skip', reason: 'skipped by user override', kind: 'unmapped-netinfo' };
    if (override.mode === 'vanilla') return { mode: 'vanilla', type: override.type, name: override.name };
    if (override.mode === 'rb') return { mode: 'rb' };
  }
  if (cat === 'road' || cat === 'highway') return { mode: 'rb' };
  const np = NOT_PLACEABLE[cat];
  if (np) return { mode: 'skip', reason: np, kind: 'not-placeable' };
  const v = vanillaTargetFor(info, cat);
  if (v) return { mode: 'vanilla', ...v };
  return { mode: 'skip', reason: `no CS2 mapping for ${info.ai ?? 'unknown AI'} (${info.service ?? '?'}/${info.subService ?? '?'})`, kind: 'unmapped-netinfo' };
}

export const prefabKeyVanilla = (name: string) => `v:${name}`;
