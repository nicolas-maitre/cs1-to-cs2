// CS1 building type → CS2 building prefab matching. Pure ES module: no I/O, no dependencies, deterministic.
// Node scripts and the web tool import the same file.
//
// Inputs:
//   assets    – the CS1 export's assets.json (schema/README.md §1): { buildings: [{ name, service, subService, ai,
//               level, growable, footprint, height, tags, aiFields, count, … }] }
//   catalogue – the CS2 catalogue (cs2-catalogue.json, buildings/README.md): { prefabs: [{ name, kind, service,
//               uiGroup, zone, level, lot, height, theme, content, components, capacity, placeable, … }] }
//
// Output per CS1 building type: a decision and, for "place", the best CS2 prefab with a score in 0..1 and a reason.
//   place        – a CS2 equivalent was found (service buildings, parks, landmarks, …)
//   growable     – a zoned CS1 building: not placed, CS2 zoning grows its own (decision recorded for the report)
//   skip         – not a building in CS2 terms (pillars, poles, pipe junctions, outside connections, decorations, …)
//   noEquivalent – a real building with no CS2 counterpart in this catalogue (e.g. CS1 libraries)
//
// Algorithm: both sides are classified into a common "family" (hospital, school/elementary, power/solar, park, …).
// Candidates are the placeable CS2 prefabs of the same family; each gets
//   the same kind (sub-family: education level, power source, transport type, …) is a filter when the catalogue has it;
//   score = 0.55 (same family) + 0.10 (same kind)
//         + 0.25 × capacity closeness (min/max of the family's capacity metric, when both sides have it)
//         + 0.10 × lot closeness (min/max of lot area in cells)
//         + 0.08 × name similarity (Jaccard of name words; 0.20 for parks, parking and landmarks, which have no capacity)
// and the best one wins (ties: preferred theme, then name), capped at 1. Without a capacity metric the lot weight is
// 0.35 − the name weight. For depots, stations, cargo terminals and schools the kind is strict: no prefab of that kind
// means noEquivalent (a ferry depot is not a subway yard).

export const MATCHER_VERSION = '1.0.0';

// ------------------------------------------------------------------ CS1 classification

const CS1_SKIP_AI = {
  PowerPoleAI: 'power pole: CS2 builds poles with its power lines',
  WaterJunctionAI: 'pipe junction: part of the CS1 pipe network',
  OutsideConnectionAI: 'outside connection: a map feature in CS2',
  DummyBuildingAI: 'sub-building / platform: comes with its parent building',
  CableCarPylonAI: 'cable car pylon: part of the cable car line',
  WildlifeSpawnPointAI: 'wildlife spawn point: not a building in CS2',
  DecorationBuildingAI: 'decoration (rocks, …): CS2 has these as props, not buildings',
  IntersectionAI: 'intersection asset: rebuilt from the road network',
};

/**
 * @param {object} a – one entry of assets.json buildings
 * @returns {{family: string|null, sub: string|null, metric: number|null, metricName: string|null, decision?: string, reason?: string}}
 */
export function classifyCs1(a) {
  const ai = a.ai ?? '';
  const name = (a.name ?? '').toLowerCase();
  const f = a.aiFields ?? {};
  const out = (family, sub = null, metric = null, metricName = null) => ({ family, sub, metric, metricName });
  if (a.growable) return { ...out(null), decision: 'growable', reason: `growable (${a.zone ?? a.subService}): CS2 zoning grows its own buildings` };
  if (CS1_SKIP_AI[ai]) return { ...out(null), decision: 'skip', reason: CS1_SKIP_AI[ai] };
  if (ai === 'BuildingAI' && /pillar|pylon/.test(name)) return { ...out(null), decision: 'skip', reason: 'bridge / elevated pillar: CS2 places pillars itself' };
  if ((a.tags ?? []).includes('subBuilding') && a.count === a.subBuildings) return { ...out(null), decision: 'skip', reason: 'only used as a sub-building: comes with its parent building' };

  switch (ai) {
    case 'HospitalAI': return out('hospital', f.m_patientCapacity > 0 && f.m_patientCapacity < 150 && !/hospital/.test(name) ? 'clinic' : null, f.m_patientCapacity ?? null, 'patients');
    case 'ChildcareAI': case 'EldercareAI': return out('hospital', 'clinic', f.m_patientCapacity ?? null, 'patients');
    case 'CemeteryAI': return out('deathcare', /cremat/.test(name) || !(f.m_graveCount > 0) ? 'crematorium' : 'cemetery', f.m_corpseCapacity ?? f.m_graveCount ?? null, 'bodies');
    case 'SchoolAI': {
      const lvl = a.level === 'Level1' ? 'elementary' : a.level === 'Level2' ? 'highSchool' : 'university';
      return out('school', lvl, f.m_studentCount ?? null, 'students');
    }
    case 'LibraryAI': return out('library');
    case 'PoliceStationAI':
      if (/prison/.test(name) || /Prison/.test(a.subService ?? '')) return out('prison', null, f.m_jailCapacity ?? null, 'prisoners');
      return out('police', null, f.m_jailCapacity ?? null, 'jail');
    case 'FireStationAI': return out('fire', null, f.m_fireTruckCount ?? null, 'fireEngines');
    case 'LandfillSiteAI':
      if (f.m_electricityProduction > 0 || /incinerat|combustion/.test(name)) return out('garbage', 'incinerator', f.m_garbageConsumption ?? f.m_garbageCapacity ?? null, 'garbage');
      return out('garbage', 'landfill', f.m_garbageCapacity ?? null, 'garbage');
    case 'PowerPlantAI': {
      const sub = /nuclear/.test(name) ? 'nuclear' : /coal/.test(name) ? 'coal' : /oil|gas/.test(name) ? 'gas' : /geotherm/.test(name) ? 'geothermal' : 'fuel';
      return out('power', sub, f.m_electricityProduction ?? null, 'electricity');
    }
    case 'SolarPowerPlantAI': return out('power', 'solar', f.m_electricityProduction ?? null, 'electricity');
    case 'WindTurbineAI': return out('power', 'wind', f.m_electricityProduction ?? null, 'electricity');
    case 'FusionPowerPlantAI': return out('power', 'nuclear', f.m_electricityProduction ?? null, 'electricity');
    case 'DamPowerHouseAI': return { ...out(null), decision: 'skip', reason: 'dam power house: CS1 dams are networks; CS2 hydro dams are built differently' };
    case 'WaterFacilityAI':
      if (f.m_waterIntake > 0) return out('waterPump', null, f.m_waterIntake, 'water');
      if (f.m_sewageOutlet > 0) return out('sewage', f.m_outletPollution > 0 && !/treatment/.test(name) ? 'outlet' : 'treatment', f.m_sewageOutlet, 'sewage');
      if (/treatment/.test(name)) return out('sewage', 'treatment', null, 'sewage');
      return out('waterPump', 'tower', f.m_waterStorage ?? null, 'water');
    case 'DepotAI': {
      const t = /taxi/.test(name) ? 'taxi' : /tram/.test(name) ? 'tram' : /ferry|ship/.test(name) ? 'ship' : /train/.test(name) ? 'train' : /metro|subway/.test(name) ? 'subway' : /trolley/.test(name) ? 'trolley' : 'bus';
      return out('depot', t, null, null);
    }
    case 'TransportStationAI': {
      const t = /metro|subway/.test(name) ? 'subway' : /train|rail/.test(name) ? 'train' : /bus/.test(name) ? 'bus' : /tram/.test(name) ? 'tram' : /airport|plane/.test(name) ? 'airplane' : 'train';
      return out('station', t, null, null);
    }
    case 'HarborAI': return out('station', 'ship', null, null);
    case 'CargoStationAI': return out('cargo', /harbor|ship/.test(name) ? 'ship' : /airport|plane/.test(name) ? 'airplane' : 'train', null, null);
    case 'CargoHarborAI': return out('cargo', 'ship', null, null);
    case 'TaxiStandAI': return out('taxiStand');
    case 'CableCarStationAI': return out('noEquivalent:cableCar');
    case 'ParkAI': case 'ParkBuildingAI': case 'ParkGateAI':
      if (/parking/.test(name)) return out('parking', null, null, null);
      if (/plaza|square/.test(name)) return out('park', 'plaza', null, null);
      return out('park', null, null, null);
    case 'MonumentAI': return out('landmark', /mall|shop|market|store/.test(name) ? 'commercial' : null, null, null);
    case 'MaintenanceDepotAI': return out('maintenance', /snow/.test(name) ? 'snow' : 'road', null, null);
    case 'ParkingAI': return out('parking', null, null, null);
    case 'PostOfficeAI': return out('post', null, null, null);
    case 'ShelterAI': return out('shelter', null, f.m_capacity ?? null, 'shelter');
    case 'DisasterResponseBuildingAI': return out('fire', 'disaster', null, null);
    case 'RadioMastAI': return out('telecom', null, null, null);
    default: break;
  }
  if (a.service === 'Monument') return out('landmark');
  if (a.service === 'Beautification') return out('park');
  return out(null);
}

// ------------------------------------------------------------------ CS2 classification

function comp(p, name) { return p.components?.[name] ?? ((p.flags ?? []).includes(name) ? {} : undefined); }

/**
 * @param {object} p – one entry of cs2-catalogue.json prefabs
 * @returns {{family: string|null, sub: string|null, metric: number|null}}
 */
export function classifyCs2(p) {
  const name = (p.name ?? '').toLowerCase();
  const cap = p.capacity ?? {};
  const out = (family, sub = null, metric = null) => ({ family, sub, metric });
  const has = (c) => comp(p, c) !== undefined;
  if (has('Hospital')) return out('hospital', cap.patients != null && cap.patients < 150 && !/hospital/.test(name) ? 'clinic' : null, cap.patients ?? null);
  if (has('DeathcareFacility')) return out('deathcare', comp(p, 'DeathcareFacility').m_LongTermStorage ? 'cemetery' : 'crematorium', cap.bodies ?? null);
  if (has('School')) {
    const s = comp(p, 'School');
    const lvl = String(s.m_Level ?? s.m_EducationLevel ?? '').toLowerCase();
    const sub = /elem|^1$/.test(lvl) ? 'elementary' : /high|^2$/.test(lvl) ? 'highSchool' : /college|^3$/.test(lvl) ? 'college' : 'university';
    return out('school', sub, cap.students ?? null);
  }
  if (has('Prison')) return out('prison', null, cap.prisoners ?? null);
  if (has('PoliceStation')) return out('police', null, cap.jail ?? null);
  if (has('FireStation')) return out('fire', comp(p, 'FireStation').m_DisasterResponseCapacity > 0 && !(cap.fireEngines > 0) ? 'disaster' : null, cap.fireEngines ?? null);
  if (has('GarbageFacility')) {
    const g = comp(p, 'GarbageFacility');
    const sub = has('GarbagePowered') || has('PowerPlant') ? 'incinerator' : g.m_LongTermStorage ? 'landfill' : 'recycling';
    return out('garbage', sub, cap.garbage ?? null);
  }
  if (has('PowerPlant') || has('SolarPowered') || has('WindPowered') || has('WaterPowered') || has('GroundWaterPowered')) {
    const sub = has('SolarPowered') ? 'solar' : has('WindPowered') ? 'wind' : has('WaterPowered') ? 'hydro' : has('GroundWaterPowered') ? 'geothermal'
      : /nuclear/.test(name) ? 'nuclear' : /coal/.test(name) ? 'coal' : /gas|oil/.test(name) ? 'gas' : 'fuel';
    return out('power', sub, cap.electricity ?? null);
  }
  // Sewage first: a wastewater treatment plant also has a WaterPumpingStation component (with 0 water capacity).
  if (has('SewageOutlet') && (cap.sewage > 0 || !(cap.water > 0)))
    return out('sewage', Number(comp(p, 'SewageOutlet').m_Purification ?? 0) > 0 || /treatment/.test(name) ? 'treatment' : 'outlet', cap.sewage ?? null);
  if (has('WaterPumpingStation')) return out('waterPump', /tower/.test(name) ? 'tower' : null, cap.water ?? null);
  if (has('TransportDepot')) return out('depot', transportType(comp(p, 'TransportDepot').m_TransportType, name), cap.depotVehicles ?? null);
  if (has('CargoTransportStation')) return out('cargo', transportType(null, name), null);
  if (has('TransportStation')) return out('station', transportType(null, name), null);
  if (has('MaintenanceDepot')) return out('maintenance', /snow/.test(name) ? 'snow' : 'road', null);
  if (has('ParkingFacility')) return out('parking', null, cap.parking ?? null);
  if (has('PostFacility')) return out('post', null, cap.mail ?? null);
  if (has('EmergencyShelter')) return out('shelter', null, cap.shelter ?? null);
  if (has('TelecomFacility')) return out('telecom', null, null);
  if (has('Park')) return out('park', /plaza|square/.test(name) ? 'plaza' : null, null);
  if (/landmark|signature/i.test(p.uiGroup ?? '') || has('SignatureBuilding')) {
    const g = (p.uiGroup ?? '').toLowerCase();
    return out('landmark', /commercial/.test(g) ? 'commercial' : /office/.test(g) ? 'office' : /industrial/.test(g) ? 'industrial' : /residential/.test(g) ? 'residential' : null, null);
  }
  return out(null);
}

function transportType(t, name) {
  const s = `${t ?? ''} ${name}`.toLowerCase();
  if (/airport|airplane|plane/.test(s)) return 'airplane';
  if (/taxi/.test(s)) return 'taxi';
  if (/tram/.test(s)) return 'tram';
  if (/subway|metro/.test(s)) return 'subway';
  if (/train|rail/.test(s)) return 'train';
  if (/ship|harbor|port|ferry/.test(s)) return 'ship';
  if (/air|plane/.test(s)) return 'airplane';
  if (/bus/.test(s)) return 'bus';
  return t ? String(t).toLowerCase() : null;
}

// ------------------------------------------------------------------ matching

/** Placeable CS2 building prefabs grouped by family. */
export function indexCatalogue(catalogue) {
  const byFamily = new Map();
  for (const p of catalogue.prefabs ?? []) {
    if (p.kind !== 'building' || !p.placeable || p.upgrade || p.components?.ServiceUpgrade) continue;
    const c = classifyCs2(p);
    if (!c.family) continue;
    if (!byFamily.has(c.family)) byFamily.set(c.family, []);
    byFamily.get(c.family).push({ prefab: p, ...c });
  }
  return { byFamily };
}

// Families where another kind is no substitute (a ferry depot is not a subway yard, a university not a school).
const STRICT_KIND = new Set(['depot', 'station', 'cargo', 'school']);
// How much the name counts. Parks, parking and landmarks have no capacity, and their names say what they are.
const NAME_WEIGHT = { park: 0.2, parking: 0.2, landmark: 0.2 };
const STOP = new Set(['the', 'of', 'and', 'a', 'decoration', 'data', 'eu', 'na', 'us']);

/** Lower-case word tokens of a prefab name: CamelCase and digits split, numbers and filler words dropped. */
export function nameTokens(name) {
  return String(name ?? '').replace(/^[0-9]+\./, '').replace(/_Data$/, '').replace(/([a-z])([A-Z])/g, '$1 $2')
    .toLowerCase().split(/[^a-z]+/).filter((t) => t.length > 1 && !STOP.has(t));
}

/** Jaccard similarity of the name tokens, 0..1. */
export function nameSimilarity(a, b) {
  const x = new Set(nameTokens(a)), y = new Set(nameTokens(b));
  if (!x.size || !y.size) return 0;
  let inter = 0;
  for (const t of x) if (y.has(t)) inter++;
  return inter / (x.size + y.size - inter);
}

const ratio = (a, b) => (a > 0 && b > 0 ? Math.min(a, b) / Math.max(a, b) : null);
const lotArea = (l) => (Array.isArray(l) && l.length === 2 ? l[0] * l[1] : null);

/**
 * @param {object} asset – assets.json building entry
 * @param {{byFamily: Map}} index – from indexCatalogue
 * @param {{theme?: string, alternatives?: number, minScore?: number}} [opts]
 */
export function matchAsset(asset, index, opts = {}) {
  const base = { cs1: asset.name, cs1Title: asset.title ?? null, count: asset.count ?? 0, footprint: asset.footprint ?? null };
  const c1 = classifyCs1(asset);
  if (c1.decision) return { ...base, decision: c1.decision, cs2: null, score: 0, reason: c1.reason, family: null };
  if (!c1.family || c1.family.startsWith('noEquivalent') || c1.family === 'library' || c1.family === 'taxiStand') {
    const why = c1.family === 'library' ? 'CS2 has no library' : c1.family === 'taxiStand' ? 'CS2 taxi stands are road-side stops, not buildings'
      : c1.family?.startsWith('noEquivalent:') ? `CS2 has no ${c1.family.split(':')[1]} building` : `no CS2 category for ${asset.service}/${asset.ai}`;
    return { ...base, decision: 'noEquivalent', cs2: null, score: 0, reason: why, family: c1.family };
  }
  let candidates = index.byFamily.get(c1.family) ?? [];
  if (!candidates.length) return { ...base, decision: 'noEquivalent', cs2: null, score: 0, reason: `no placeable CS2 ${c1.family} building in the catalogue`, family: c1.family };
  // The kind (education level, power source, transport type, …) is a filter, not a weight: a high school must never
  // become an elementary school because its capacity is closer. Only without any prefab of that kind does the whole
  // family compete (and the reason says so).
  let kindFallback = false;
  {
    const same = candidates.filter((c) => (c.sub ?? null) === (c1.sub ?? null));
    if (same.length) candidates = same;
    else if (c1.sub && STRICT_KIND.has(c1.family)) {
      return { ...base, decision: 'noEquivalent', cs2: null, score: 0, reason: `no CS2 ${c1.family} of kind ${c1.sub}`, family: c1.family };
    } else kindFallback = !!c1.sub;
  }

  const area1 = lotArea(asset.footprint);
  const scored = candidates.map((c) => {
    let score = 0.55;
    const parts = [`same family (${c1.family})`];
    if (c1.sub || c.sub) {
      if (c1.sub === c.sub) { score += 0.10; parts.push(`same kind (${c.sub})`); }
      else parts.push(`kind ${c1.sub ?? '-'} vs ${c.sub ?? '-'}`);
    } else score += 0.10;
    const capR = ratio(c1.metric, c.metric);
    const lotR = ratio(area1, lotArea(c.prefab.lot));
    if (capR != null) { score += 0.25 * capR; parts.push(`${c1.metricName} ${c1.metric} vs ${c.metric}`); }
    const nameW = NAME_WEIGHT[c1.family] ?? 0.08;
    if (lotR != null) {
      score += (capR != null ? 0.10 : 0.35 - nameW) * lotR;
      parts.push(`lot ${asset.footprint?.join('×')} vs ${c.prefab.lot.join('×')}`);
    }
    const sim = nameSimilarity(asset.name, c.prefab.name);
    if (sim > 0) { score += nameW * sim; parts.push(`name ${Math.round(sim * 100)}%`); }
    score = Math.min(1, score);
    if (kindFallback) parts.push(`no CS2 ${c1.family} of kind ${c1.sub}`);
    return { cs2: c.prefab.name, score: Math.round(score * 1000) / 1000, reason: parts.join('; '), theme: c.prefab.theme ?? null, lot: c.prefab.lot ?? null };
  });
  scored.sort((a, b) => b.score - a.score
    || (opts.theme ? (b.theme === opts.theme) - (a.theme === opts.theme) : 0)
    || a.cs2.localeCompare(b.cs2));
  const best = scored[0];
  const minScore = opts.minScore ?? 0;
  if (best.score < minScore) return { ...base, decision: 'noEquivalent', cs2: null, score: best.score, reason: `best ${best.cs2} below the minimum score ${minScore}`, family: c1.family };
  return {
    ...base, decision: 'place', family: c1.family, cs2: best.cs2, cs2Lot: best.lot, score: best.score, reason: best.reason,
    alternatives: scored.slice(1, 1 + (opts.alternatives ?? 3)).map(({ cs2, score }) => ({ cs2, score })),
  };
}

/**
 * Matches every CS1 building type of an assets.json against a catalogue.
 * @returns {{version: string, results: object[], byName: Object<string, object>, stats: object}}
 */
export function matchAssets(assets, catalogue, opts = {}) {
  const index = indexCatalogue(catalogue);
  const results = (assets.buildings ?? []).map((a) => matchAsset(a, index, opts));
  const stats = { types: results.length, instances: 0, byDecision: {} };
  for (const r of results) {
    const s = (stats.byDecision[r.decision] ??= { types: 0, instances: 0 });
    s.types++;
    s.instances += r.count;
    stats.instances += r.count;
  }
  const byName = Object.fromEntries(results.map((r) => [r.cs1, r]));
  return { version: MATCHER_VERSION, results, byName, stats };
}

// ------------------------------------------------------------------ slim catalogue

/** Components classifyCs2 reads; everything else can be dropped for the browser. */
export const MATCH_COMPONENTS = ['Hospital', 'DeathcareFacility', 'School', 'Prison', 'PoliceStation', 'FireStation', 'GarbageFacility',
  'GarbagePowered', 'PowerPlant', 'SolarPowered', 'WindPowered', 'WaterPowered', 'GroundWaterPowered', 'WaterPumpingStation', 'SewageOutlet',
  'TransportDepot', 'CargoTransportStation', 'TransportStation', 'MaintenanceDepot', 'ParkingFacility', 'PostFacility', 'EmergencyShelter',
  'TelecomFacility', 'Park', 'SignatureBuilding', 'ServiceUpgrade'];

/** The catalogue reduced to what matching needs: placeable buildings, matching components only. Same shape. */
export function slimCatalogue(catalogue) {
  const keep = new Set(MATCH_COMPONENTS);
  const prefabs = (catalogue.prefabs ?? []).filter((p) => p.kind === 'building' && p.placeable).map((p) => {
    const { components = {}, flags = [], ...rest } = p;
    return {
      ...rest,
      components: Object.fromEntries(Object.entries(components).filter(([k]) => keep.has(k))),
      flags: flags.filter((f) => keep.has(f)),
    };
  });
  return { ...catalogue, slim: true, counts: { ...catalogue.counts, slimBuildings: prefabs.length }, prefabs };
}
