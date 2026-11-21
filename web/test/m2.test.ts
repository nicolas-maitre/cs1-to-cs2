import { strToU8, zipSync } from 'fflate';
import { describe, expect, it } from 'vitest';
import { translator } from '../src/core/i18n/index.ts';
import { ReadError, readCityFile, readCs1x } from '../src/core/cs1x.ts';
import { inboundLanes, planLaneRef } from '../src/core/m2/context.ts';
import type { PlanContext } from '../src/core/m2/context.ts';
import { buildNodeController } from '../src/core/m2/nodecontroller.ts';
import { buildTraffic, isBusOnly, trafficLaneVariants } from '../src/core/m2/traffic.ts';
import { buildTransit, hexColor, transitTarget } from '../src/core/m2/transit.ts';
import { buildWater } from '../src/core/m2/water.ts';
import { DEFAULT_ZONING, buildZoning, cellCentre, zonePrefab } from '../src/core/m2/zoning.ts';
import { buildPackage, packageFiles } from '../src/core/plan.ts';
import { conversionSummary, sectionCheck } from '../src/core/summary.ts';
import { syntheticCity } from '../src/core/synthetic.ts';
import type { Cs1City, NetInfo } from '../src/core/types.ts';
import { city, lane, node, straight } from './fixtures.ts';

// A four-lane two-way road: car lanes −4.5, −1.5 (backward) and 1.5, 4.5 (forward), sidewalks ±7.
const ROAD: NetInfo = {
  name: 'Four Lane', title: 'Four-Lane Road', ai: 'RoadAI', service: 'Road', parent: null,
  fields: { m_halfWidth: 9, m_pavementWidth: 3 }, aiFields: { m_trafficLights: true },
  lanes: [lane(-7, 3, 'Pedestrian', 'None', 'Both'), lane(-4.5, 3, 'Vehicle', 'Car', 'Backward'), lane(-1.5, 3, 'Vehicle', 'Car', 'Backward'),
    lane(1.5, 3, 'Vehicle', 'Car', 'Forward'), lane(4.5, 3, 'Vehicle', 'Car', 'Forward'), lane(7, 3, 'Pedestrian', 'None', 'Both')],
};
const TRACK: NetInfo = { name: 'Train Track', ai: 'TrainTrackAI', service: 'PublicTransport', subService: 'PublicTransportTrain', fields: { m_halfWidth: 5 }, parent: null, lanes: [lane(-2, 4, 'Vehicle', 'Train', 'Backward'), lane(2, 4, 'Vehicle', 'Train', 'Forward')] };

// Cross junction at node 1 (0,0): seg 1 east (1→2), seg 2 west drawn inwards and inverted (3→1),
// seg 3 north (1→4), seg 4 south (5→1); a far road seg 5 (6→7) outside the crop; a track seg 6.
function cross(extra: Partial<Cs1City> = {}): Cs1City {
  return city({
    netInfos: [ROAD, TRACK],
    nodes: [node(1, 0, 0, { flags: 'Created, Junction, TrafficLights', segments: [1, 2, 3, 4] }), node(2, 100, 0), node(3, -100, 0), node(4, 0, 100), node(5, 0, -100),
      node(6, 5000, 5000), node(7, 5100, 5000), node(8, 200, 0), node(9, 200, 100)],
    segments: [
      straight(1, ROAD.name, 0, 100, 0, false, 1, 2), straight(2, ROAD.name, -100, 0, 0, true, 3, 1),
      { ...straight(3, ROAD.name, 0, 0, 0, false, 1, 4), bezier: [[0, 0, 0], [0, 0, 33], [0, 0, 66], [0, 0, 100]], length: 100 },
      { ...straight(4, ROAD.name, 0, 0, 0, false, 5, 1), bezier: [[0, 0, -100], [0, 0, -66], [0, 0, -33], [0, 0, 0]], length: 100 },
      straight(5, ROAD.name, 5000, 5100, 5000, false, 6, 7),
      { ...straight(6, TRACK.name, 200, 200, 0, false, 8, 9), bezier: [[200, 0, 0], [200, 0, 33], [200, 0, 66], [200, 0, 100]], length: 100 },
    ],
    ...extra,
  });
}
const CROP = { centerX: 0, centerZ: 0, size: 1000 };

function ctxOf(c: Cs1City): PlanContext {
  const pkg = buildPackage(c, { crop: CROP });
  return {
    city: c, crop: CROP, dx: 0, dz: 0, infos: new Map(c.netInfos.map(i => [i.name, i])), nodes: new Map(c.nodes.map(n => [n.id, n])),
    segments: new Map(c.segments.map(s => [s.id, s])), planSegments: new Set(pkg.plan.segments.map(s => s.id)), planNodes: new Set(pkg.plan.nodes.map(n => n.id)),
    reversed: new Set(pkg.plan.segments.filter(s => s.reversed).map(s => s.id)), inCrop: (x, z) => Math.abs(x) <= 500 && Math.abs(z) <= 500, t: translator('en'), lang: 'en',
  };
}

describe('zoning', () => {
  it('maps every CS1 zone to a CS2 zone prefab per theme and option', () => {
    const o = DEFAULT_ZONING;
    expect(zonePrefab('ResidentialLow', o)).toBe('NA Residential Low');
    expect(zonePrefab('ResidentialHigh', o)).toBe('NA Residential High');
    expect(zonePrefab('CommercialLow', o)).toBe('NA Commercial Low');
    expect(zonePrefab('CommercialHigh', o)).toBe('NA Commercial High');
    expect(zonePrefab('Industrial', o)).toBe('Industrial Manufacturing');
    expect(zonePrefab('Office', o)).toBe('Office High');
    for (const z of ['Unzoned', 'Distant', 'None', 'Whatever']) expect(zonePrefab(z, o)).toBeNull();
    const eu = { ...o, theme: 'EU' as const, residentialHigh: 'Medium' as const, office: 'Low' as const };
    expect(zonePrefab('ResidentialHigh', eu)).toBe('EU Residential Medium');
    expect(zonePrefab('CommercialHigh', eu)).toBe('EU Commercial High');
    expect(zonePrefab('Office', eu)).toBe('Office Low');
    expect(zonePrefab('ResidentialHigh', { ...o, residentialHigh: 'Mixed' })).toBe('NA Residential Mixed');
  });

  it('computes cell centres with the schema formula', () => {
    expect(cellCentre(0, 0, 0, 0, 0)).toEqual([-28, 28]);
    const [x, z] = cellCentre(10, 20, Math.PI / 2, 3, 3);
    expect(x).toBeCloseTo(10 - 0.5 * 0 + -0.5 * 8);
    expect(z).toBeCloseTo(20 - 0.5 * 8);
  });

  it('lists zoned cells of plan roads in CS2 coordinates, skips cells of roads outside the plan', () => {
    const zones = {
      cellSize: 8, columns: 4, zoneCodes: { 0: 'Unzoned', 2: 'ResidentialLow', 4: 'CommercialLow', 6: 'Industrial' },
      blocks: [
        { id: 1, position: [50, 0, -40] as [number, number, number], angle: 0, rows: 2, segment: 1, zones: '22-0' + '4466' },
        { id: 2, position: [5050, 0, 4960] as [number, number, number], angle: 0, rows: 1, segment: 5, zones: '2222' },
        { id: 3, position: [50, 0, 60] as [number, number, number], angle: 0, rows: 1, segment: 6, zones: '2222' },
      ],
    };
    const c = cross({ zones });
    const r = buildZoning(ctxOf(c), DEFAULT_ZONING);
    expect(r.section!.cells).toHaveLength(10); // block 1: 2 + 4 zoned cells ("-" invalid, 0 unzoned); block 2 outside; block 3: 4 (its segment 6 is in the plan)
    expect(r.section!.prefabs).toEqual(['NA Residential Low', 'NA Commercial Low', 'Industrial Manufacturing']);
    expect(r.section!.cells[0]).toEqual([22, -12, 0]);
    expect(r.section!.stats.outsideCrop).toBe(4);
    expect(r.summary.status).toBe('converted');
    const pkg = buildPackage(c, { crop: { centerX: 100, centerZ: 0, size: 1000 } });
    expect(pkg.plan.zoning!.cells[0]).toEqual([22 - 100, -12, 0]);
    expect(buildZoning(ctxOf(c), { ...DEFAULT_ZONING, enabled: false }).summary.status).toBe('not-converted');
    expect(buildZoning(ctxOf(cross()), DEFAULT_ZONING).summary.status).toBe('absent');
  });
});

describe('water', () => {
  it('keeps sea level and natural sources in the crop, measures the wet share, skips facility sources', () => {
    const n = 5, depthGrid = new Uint16Array(n * n);
    depthGrid[2 * n + 2] = 64 * 3; // 3 m deep in the middle sample (0, 0)
    const c = cross({
      water: {
        seaLevel: 40.123, depth: { resolution: n, size: 4000, heightScale: 1 / 64 }, depthGrid,
        sources: [
          { index: 0, type: 'natural', inputPosition: [10, 30, 20], outputPosition: [10, 30, 20], targetHeight: 45, outputRate: 1000 },
          { index: 1, type: 'natural', inputPosition: [9000, 0, 0], outputPosition: [9000, 0, 0] },
          { index: 2, type: 'facility', inputPosition: [0, 0, 0], outputPosition: [0, 0, 0] },
        ],
      },
    });
    const r = buildWater(ctxOf(c));
    expect(r.section).toMatchObject({ seaLevel: 40.12, maxDepth: 3, wetFraction: 1 });
    expect(r.section!.sources).toEqual([{ cs1Index: 0, x: 10, z: 20, y: 30, targetHeight: 45, outputRate: 1000 }]);
    expect(r.items.some(i => /pump, drain/.test(i.message))).toBe(true);
    expect(buildWater(ctxOf(cross())).summary.status).toBe('absent');
  });
});

describe('transit', () => {
  it('maps CS1 transport types to CS2 lines', () => {
    expect(transitTarget('Bus')).toEqual({ line: 'Bus Line', stop: 'Bus Stop' });
    expect(transitTarget('Tram')).toEqual({ line: 'Tram Line', stop: 'Tram Stop' });
    expect(transitTarget('Metro')).toEqual({ line: 'Subway Line', stop: null });
    expect(transitTarget('Train')!.line).toBe('Passenger Train Line');
    expect(transitTarget('Ship')!.line).toBe('Passenger Ship Line');
    expect(transitTarget('Airplane')!.line).toBe('Passenger Airplane Line');
    expect(transitTarget('Trolleybus')).toMatchObject({ line: 'Bus Line', approx: expect.any(String) });
    for (const t of ['Monorail', 'CableCar', 'Helicopter', 'Pedestrian', 'Taxi']) expect(transitTarget(t)).toBeNull();
    expect(hexColor([5, 254, 0])).toBe('#05fe00');
    expect(hexColor(undefined)).toBeNull();
  });

  it('keeps stops on plan roads with side and plan-frame position, drops the rest', () => {
    const stop = (nodeId: number, x: number, z: number, segment: number, laneIndex: number, laneOffset: number) => ({ node: nodeId, position: [x, 0, z] as [number, number, number], segment, laneIndex, laneOffset, building: 0 });
    const c = cross({
      transit: {
        lines: [
          { id: 1, name: 'Bus 1', number: 1, type: 'Bus', color: [255, 0, 0], vehicles: 3, stops: [stop(100, 50, -7, 1, 5, 0.5), stop(101, -60, 7, 2, 0, 0.25), stop(102, 5050, 5000, 5, 5, 0.5)] },
          { id: 2, name: 'Metro 1', type: 'Metro', color: [0, 0, 255], stops: [stop(200, 10, 10, 6, 0, 0.5), stop(201, 20, 20, 6, 0, 0.5)] },
          { id: 3, name: 'Mono', type: 'Monorail', stops: [stop(300, 0, 0, 1, 0, 0.5), stop(301, 1, 1, 1, 0, 0.5)] },
          { id: 4, name: 'Far', type: 'Bus', stops: [stop(400, 5050, 5000, 5, 5, 0.5), stop(401, 5060, 5000, 5, 5, 0.5)] },
        ],
      },
    });
    const r = buildTransit(ctxOf(c));
    const [bus, metro] = r.section!.lines;
    expect(r.section!.lines).toHaveLength(2);
    expect(bus).toMatchObject({ name: 'Bus 1', line: 'Bus Line', stopPrefab: 'Bus Stop', color: '#ff0000', truncated: true, requiresStations: false });
    expect(bus.stops).toHaveLength(2);
    expect(bus.stops[0]).toMatchObject({ segment: 1, side: 'right', t: 0.5 });
    expect(bus.stops[1]).toMatchObject({ segment: 2, side: 'left', t: 0.75 }); // segment 2 is reversed in the plan
    expect(metro).toMatchObject({ line: 'Subway Line', stopPrefab: null, requiresStations: true });
    expect(r.items.some(i => /Monorail line: no CS2 equivalent/.test(i.message))).toBe(true);
    expect(r.items.some(i => /fewer than two stops/.test(i.message))).toBe(true);
    expect(r.summary.status).toBe('partial');

    // metro / train yards: never inside the city (decision #83); the importer reads transit.depotMode
    const pkg = buildPackage(c, { crop: CROP });
    expect(pkg.plan.transit!.depotMode).toEqual({ Metro: 'edge' });
    expect(pkg.report.items.some(i => /Metro lines \(Metro 1\): the importer places a subway yard at the crop edge/.test(i.message))).toBe(true);
    expect(pkg.summary.find(s => s.id === 'transit')!.details.join(' ')).toMatch(/Metro lines: the importer places a yard at the crop edge/);
    const manual = buildPackage(c, { crop: CROP, depotMode: { Metro: 'manual', Train: 'manual' }, lang: 'fr' });
    expect(manual.plan.transit!.depotMode).toEqual({ Metro: 'manual' }); // only types with lines in the plan
    expect(manual.summary.find(s => s.id === 'transit')!.details.join(' ')).toMatch(/Lignes de métro : vous placez leur dépôt vous-même/);
    expect(buildPackage(cross({ transit: { lines: [c.transit!.lines[0]] } }), { crop: CROP }).plan.transit!.depotMode).toBeUndefined();
  });
});

describe('TM:PE', () => {
  it('recognises bus-only restrictions', () => {
    expect(isBusOnly(['Bus', 'Emergency'])).toBe(true);
    expect(isBusOnly(['Bus', 'Taxi', 'Service'])).toBe(true);
    expect(isBusOnly(['PassengerCar', 'Bus', 'Emergency'])).toBe(false);
    expect(isBusOnly(['Emergency'])).toBe(false);
  });

  it('resolves lanes to plan lane refs and inbound lanes', () => {
    const ctx = ctxOf(cross());
    expect(planLaneRef(ctx, 1, 4)).toEqual({ segment: 1, ordinal: 3, count: 4, offset: 4.5, dir: 'forward', cs1LaneIndex: 4 });
    expect(planLaneRef(ctx, 1, 0)).toBeNull(); // sidewalk
    expect(inboundLanes(ctx, 1, 1).map(l => l.index)).toEqual([1, 2]); // seg 1 starts at node 1: backward lanes flow in
    expect(inboundLanes(ctx, 2, 1).map(l => l.index)).toEqual([1, 2]); // seg 2 ends at node 1 but is inverted
  });

  it('turns speed limits and bus-only lanes into RB variants', () => {
    const c = cross({
      tmpe: {
        speedLimits: [{ segment: 1, laneIndex: 3, kmh: 60 }, { segment: 1, laneIndex: 4, kmh: 60 }, { segment: 1, laneIndex: 1, kmh: 40 }, { segment: 3, laneIndex: 3, kmh: 1000, unlimited: true }],
        defaultSpeedLimits: { 'Four Lane': 1.6 },
        vehicleRestrictions: [{ segment: 4, laneIndex: 4, allowed: ['Bus', 'Emergency'] }, { segment: 4, laneIndex: 3, allowed: ['PassengerCar'] }],
      },
    });
    const lv = trafficLaneVariants(c, new Map(c.netInfos.map(i => [i.name, i])), c.segments);
    expect(lv.speed.get(1)).toBe(60);
    expect(lv.speed.get(2)).toBe(80); // NetInfo default 1.6 × 50
    expect([...lv.bus.get(4)!]).toEqual([[4.5, 'bus']]);
    expect(lv.items.some(i => /restricted to PassengerCar/.test(i.message))).toBe(true);
    const pkg = buildPackage(c, { crop: CROP });
    const pre = (id: number) => pkg.plan.prefabs[pkg.plan.segments.find(s => s.id === id)!.prefab];
    expect(pre(1)).toMatchObject({ speedKmh: 60 });
    expect(pre(4)).toMatchObject({ imt: '4.5=bus', lanesFrom: 'TM:PE', speedKmh: 80 });
    const cfg1 = pkg.rbConfigs.find(r => r.ID === pre(1).name)!;
    expect(cfg1.SpeedLimit).toBe(120);
    expect(cfg1.Name).toMatch(/60 km\/h$/);
    const cfg4 = pkg.rbConfigs.find(r => r.ID === pre(4).name)!;
    expect(cfg4.Lanes.some(l => /BusGroupPrefab/.test(l.GroupPrefabName))).toBe(true);
    expect(buildPackage(c, { crop: CROP, traffic: false }).rbConfigs).toHaveLength(1);
  });

  it('maps lights, priority, all-way stops, turn bans, crosswalks and lane connections', () => {
    const c = cross({
      tmpe: {
        prioritySigns: [1, 2, 3, 4].map(s => ({ segment: s, node: 1, type: 'Stop' })),
        laneArrows: [{ segment: 3, laneIndex: 1, node: 1, arrows: ['Forward'] }, { segment: 3, laneIndex: 2, node: 1, arrows: ['Forward', 'Right'] }, { segment: 1, laneIndex: 1, node: 1, arrows: ['Left'] }],
        junctionRestrictions: [{ segment: 4, node: 1, pedestrianCrossingAllowed: false, uturnAllowed: true, enterWhenBlockedAllowed: true }],
        laneConnections: [{ sourceSegment: 4, sourceLaneIndex: 3, targetSegment: 3, targetLaneIndex: 4, node: 1, group: 'Road' }, { sourceSegment: 6, sourceLaneIndex: 1, targetSegment: 6, targetLaneIndex: 1, node: 8, group: 'Track' }],
        timedTrafficLights: [{ nodeId: 1 }],
      },
    });
    const r = buildTraffic(ctxOf(c));
    const t = r.section!;
    expect(t.nodes).toEqual([{ id: 1, trafficLights: true, allWayStop: true }]);
    expect(t.requires).toEqual(['Traffic']);
    const end = (s: number) => t.segmentEnds.find(e => e.segment === s && e.node === 1)!;
    expect(end(3)).toMatchObject({ priority: 'stop', forbid: ['left'] }); // both inbound lanes customised: no Left
    expect(end(1).forbid).toBeUndefined(); // only one of two inbound lanes customised
    expect(end(4)).toMatchObject({ crosswalk: false, uturn: true });
    expect(t.laneConnections).toEqual([{ node: 1, from: { segment: 4, ordinal: 2, count: 4, offset: 1.5, dir: 'forward', cs1LaneIndex: 3 }, to: { segment: 3, ordinal: 3, count: 4, offset: 4.5, dir: 'forward', cs1LaneIndex: 4 } }]);
    expect(t.laneArrows).toHaveLength(3);
    expect(r.items.some(i => /timed traffic light/.test(i.message))).toBe(true);
    expect(r.items.some(i => /enterWhenBlocked/.test(i.message))).toBe(true);
    expect(r.summary.status).toBe('partial');
    // without TM:PE the lights still come from the network
    const plain = buildTraffic(ctxOf(cross()));
    expect(plain.section!.nodes).toEqual([{ id: 1, trafficLights: true }]);
    expect(plain.section!.requires).toEqual([]);
  });
});

describe('Node Controller', () => {
  it('reports changed node types, hidden markings and free-form nodes', () => {
    const c = cross({
      nodeController: {
        nodes: [
          { id: 1, type: 'Custom', defaultType: 'Middle', mode: 'FreeForm', noMarkings: true, segmentEnds: [] },
          { id: 2, type: 'End', defaultType: 'End', segmentEnds: [{ segment: 1, shift: 1.5 }] },
          { id: 6, type: 'Crossing', defaultType: 'Middle' },
        ],
      },
    });
    const r = buildNodeController(ctxOf(c));
    expect(r.summary).toMatchObject({ status: 'report-only' });
    expect(r.items.map(i => i.count)).toEqual([1, 1, 2]);
    expect(buildNodeController(ctxOf(cross())).summary.status).toBe('absent');
  });
});

describe('package with M2 sections', () => {
  it('adds the sections, requirements and README, and keeps v1 fields', () => {
    const c = cross({
      zones: { cellSize: 8, columns: 4, zoneCodes: { 2: 'ResidentialLow' }, blocks: [{ id: 1, position: [50, 0, -40], angle: 0, rows: 1, segment: 1, zones: '2222' }] },
      tmpe: { prioritySigns: [{ segment: 1, node: 1, type: 'Yield' }] },
      manifest: { format: 'cs1x', formatVersion: 1, city: { name: 'Cross', saveName: 'Cross.01' }, sections: { network: 'ok', zones: 'ok', tmpe: 'ok', buildings: 'ok' } },
    });
    const pkg = buildPackage(c, { crop: CROP, zoning: { ...DEFAULT_ZONING, theme: 'EU' } });
    expect(pkg.plan).toMatchObject({ format: 'cs1conv-plan', formatVersion: 1, requires: ['RoadBuilder', 'Traffic'] });
    expect(pkg.plan.zoning!.prefabs).toEqual(['EU Residential Low']);
    expect(pkg.plan.traffic!.segmentEnds).toEqual([{ segment: 1, node: 1, priority: 'yield' }]);
    expect(pkg.summary.map(s => s.id)).toEqual(['roads', 'tracks', 'imt', 'skipped', 'terrain', 'zoning', 'water', 'transit', 'traffic', 'nodecontroller', 'buildings', 'districts', 'objects']);
    const readme = packageFiles(pkg).find(f => f.path === 'README.txt')!.text;
    expect(readme).toMatch(/Road Builder, CS1 → CS2 Importer, Traffic/);
    expect(readme).toMatch(/ModsData\\RoadBuilder/);
    expect(readme).toMatch(/Zoning: converted/);
    expect(readme).toContain('\r\n');
  });

  it('builds a large synthetic city quickly', () => {
    const c = syntheticCity(120);
    expect(c.segments.length).toBeGreaterThan(28000);
    const t = performance.now();
    const pkg = buildPackage(c, { crop: { centerX: 0, centerZ: 0, size: 14336 } });
    expect(performance.now() - t).toBeLessThan(5000);
    expect(pkg.plan.stats.segments).toBeGreaterThan(28000);
    expect(pkg.rbConfigs.length).toBe(3);
  });
});

describe('reader errors and section check', () => {
  const zip = (manifest: object) => zipSync({ 'manifest.json': strToU8(JSON.stringify(manifest)), 'network.json': strToU8('{"nodes":[],"segments":[]}') });
  const code = async (b: Uint8Array) => { try { await readCityFile(b, { hash: false }); return 'ok'; } catch (e) { return e instanceof ReadError ? e.code : String(e); } };
  it('classifies unusable files', async () => {
    expect(await code(new Uint8Array())).toBe('empty');
    expect(await code(new Uint8Array([0x43, 0x52, 0x41, 0x50, 0]))).toBe('crp-save');
    expect(await code(strToU8('hello'))).toBe('not-cs1x');
    expect(await code(strToU8('{"a":1}'))).toBe('not-cs1x');
    expect(await code(new Uint8Array([0x50, 0x4b, 3, 4, 0, 0, 0]))).toBe('corrupt-zip');
    expect(await code(zipSync({ 'x.txt': strToU8('x') }))).toBe('no-manifest');
    expect(await code(zip({ format: 'other', formatVersion: 1 }))).toBe('not-cs1x');
    expect(await code(zip({ format: 'cs1x', formatVersion: 0 }))).toBe('format-too-old');
    expect(await code(zip({ format: 'cs1x' }))).toBe('format-too-old');
    expect(await code(zip({ format: 'cs1x', formatVersion: 2 }))).toBe('format-too-new');
    expect(await code(zip({ format: 'cs1x', formatVersion: 1 }))).toBe('ok');
  });

  it('reads M2 sections, decodes water.r16 and surfaces failed sections', () => {
    const r16 = new Uint8Array(8); new DataView(r16.buffer).setUint16(2, 640, true);
    const c = readCs1x(zipSync({
      'manifest.json': strToU8(JSON.stringify({ format: 'cs1x', formatVersion: 1, sections: { network: 'ok', zones: 'ok', water: 'ok', transit: 'failed', tmpe: 'ok', nodecontroller: 'absent' } })),
      'network.json': strToU8('{"nodes":[],"segments":[]}'),
      'zones.json': strToU8(JSON.stringify({ cellSize: 8, columns: 4, zoneCodes: {}, blocks: [] })),
      'water.json': strToU8(JSON.stringify({ seaLevel: 12, depth: { resolution: 2, size: 100, heightScale: 1 / 64 }, sources: [] })),
      'water.r16': r16,
      'tmpe.json': strToU8(JSON.stringify({ parts: { laneArrows: 'ok', speedLimits: 'failed: NullReferenceException' } })),
      'report.json': strToU8(JSON.stringify({ items: [{ level: 'error', section: 'transit', message: 'TransportManager missing' }] })),
    }));
    expect(c.zones!.blocks).toEqual([]);
    expect([...c.water!.depthGrid!]).toEqual([0, 640, 0, 0]);
    expect(c.transit).toBeNull();
    expect(c.nodeController).toBeNull();
    expect(c.report.some(r => /speedLimits: failed/.test(r.message))).toBe(true);
    const chk = sectionCheck(c);
    expect(chk.failed).toEqual(['Transit lines']);
    expect(chk.sections.find(s => s.id === 'transit')!.messages).toEqual(['TransportManager missing']);
    const pkg = buildPackage({ ...c, netInfos: [], segments: [], nodes: [] }, { crop: CROP });
    expect(conversionSummary(c, pkg).find(s => s.id === 'transit')).toMatchObject({ status: 'not-converted', summary: expect.stringMatching(/failed/) });
  });
});

describe('plan JSON', () => {
  it('keeps flat arrays on one line and round-trips exactly', async () => {
    const { compactJson } = await import('../src/core/plan.ts');
    const o = { a: [1, -2.5, 3e-4, 1e21], b: { c: [[1, 2, 3], [4, 5, 6]], d: ['x, y', 'z', 'a"b', ''], e: [], f: [{ g: 1 }], h: [true, null, false] } };
    const s = compactJson(o);
    expect(JSON.parse(s)).toEqual(o);
    expect(s).toContain('"c": [\n   [1,2,3],');
    expect(s).toContain('"d": ["x, y","z","a\\"b",""]');
  });
});

describe('empty sections', () => {
  it('says "the city has no …" instead of "not converted" when a section is present but empty', () => {
    const c = cross({ transit: { lines: [] }, zones: { cellSize: 8, columns: 4, zoneCodes: { 0: 'Unzoned' }, blocks: [{ id: 1, position: [0, 0, 0], angle: 0, rows: 1, segment: 1, zones: '00--' }] } });
    const ctx = ctxOf(c);
    expect(buildTransit(ctx).summary).toMatchObject({ status: 'none', summary: 'the city has no transport lines' });
    expect(buildZoning(ctx, DEFAULT_ZONING).summary).toMatchObject({ status: 'none', summary: 'the city has no zoned areas' });
  });
});

describe('left-hand traffic', () => {
  it('passes manifest.traffic.leftHand into plan.source.leftHandTraffic (absent when the export predates it)', () => {
    const c = cross();
    expect('leftHandTraffic' in buildPackage(c, { crop: CROP }).plan.source).toBe(false);
    const left = cross();
    left.manifest = { ...left.manifest, traffic: { leftHand: true } };
    const pkg = buildPackage(left, { crop: CROP });
    expect(pkg.plan.source.leftHandTraffic).toBe(true);
    expect(buildPackage(left, { crop: CROP, traffic: false }).plan.source.leftHandTraffic).toBe(true); // with traffic off too
    expect(pkg.report.items.some(i => i.kind === 'traffic' && /drives on the left/.test(i.message))).toBe(true);
    expect(pkg.summary.find(s => s.id === 'traffic')!.details.join(' ')).toMatch(/drives on the left/);
    const right = cross();
    right.manifest = { ...right.manifest, traffic: { leftHand: false } };
    expect(buildPackage(right, { crop: CROP }).plan.source.leftHandTraffic).toBe(false);
  });
});
