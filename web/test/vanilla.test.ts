import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { readCityFile } from '../src/core/cs1x.ts';
import { buildPackage, autoCrop } from '../src/core/plan.ts';
import { convert } from '../src/core/rb.ts';
import type { ProfileLane } from '../src/core/rb.ts';
import { BASIC, HIGHWAY, MEDIUM } from '../src/core/synthetic.ts';
import type { NetInfo } from '../src/core/types.ts';
import { matchVanilla, profileDifferences, slimRoads } from '../src/core/vanilla.ts';
import type { Cs2Road, Cs2RoadCatalogue } from '../src/core/vanilla.ts';
import { city, lane, node, straight } from './fixtures.ts';

const L = (type: string, dir: ProfileLane['dir'], offset: number, width: number): ProfileLane => ({ type, dir, offset, width });
const sidewalks = (half: number, w: number, parking: boolean): ProfileLane[] => [
  ...(parking ? [L('parking', 'none', -(half - w - 1), 2), L('parking', 'none', half - w - 1, 2)] : []),
  L('pedestrian', 'both', -(half - w / 2), w), L('pedestrian', 'both', half - w / 2, w),
];
// A hand-made vanilla list, close to (not equal to) what the profiles give, plus decoys.
const ROADS: Cs2RoadCatalogue = {
  roads: [
    { name: 'Small Road', category: 'road', width: 16, speedKmh: 50, lanes: [...sidewalks(8, 3, true), L('car', 'backward', -1.6, 3), L('car', 'forward', 1.6, 3)] },
    { name: 'Small Road Oneway', category: 'road', width: 16, speedKmh: 50, lanes: [...sidewalks(8, 3, true), L('car', 'forward', -1.5, 3), L('car', 'forward', 1.5, 3)] },
    { name: 'Medium Road Divided', category: 'road', width: 27.5, speedKmh: 50, lanes: [...sidewalks(13.75, 5, false), L('car', 'backward', -7.2, 3), L('car', 'backward', -4.2, 3), L('median', 'none', 0, 5), L('car', 'forward', 4.2, 3), L('car', 'forward', 7.2, 3)] },
    { name: 'Medium Road', category: 'road', width: 24, speedKmh: 50, lanes: [...sidewalks(12, 5, false), L('car', 'backward', -4.5, 3), L('car', 'backward', -1.5, 3), L('car', 'forward', 1.5, 3), L('car', 'forward', 4.5, 3)] },
    { name: 'Highway Oneway - 3 lanes', category: 'highway', width: 16.5, speedKmh: 50, lanes: [L('car', 'forward', -4, 4), L('car', 'forward', 0, 4), L('car', 'forward', 4, 4)] },
    { name: 'Alley', category: 'road', width: 8, speedKmh: 20, lanes: [L('car', 'both', 0, 4)] },
  ],
};
const profile = (i: NetInfo, ...args: Parameters<typeof convert> extends [unknown, ...infer R] ? R : never) => convert(i, ...args)!.report.profile;

describe('vanilla road matching', () => {
  it('matches the CS1 vanilla roads to their CS2 counterparts', () => {
    expect(matchVanilla(profile(BASIC), ROADS).road?.name).toBe('Small Road');
    expect(matchVanilla(profile(MEDIUM), ROADS).road?.name).toBe('Medium Road Divided'); // a 5 m median: not the plain Medium Road
    expect(matchVanilla(profile(HIGHWAY), ROADS).road?.name).toBe('Highway Oneway - 3 lanes');
    expect(matchVanilla(profile(BASIC), ROADS).reason).toMatch(/same lanes as Small Road/);
  });

  it('keeps near misses on Road Builder, with the reason', () => {
    const wide: NetInfo = { ...BASIC, name: 'Wide Lanes', lanes: BASIC.lanes.map(l => (l.fields.m_laneType === 'Vehicle' && /Car/.test(String(l.fields.m_vehicleType)) ? { ...l, fields: { ...l.fields, m_width: 4, m_position: Math.sign(Number(l.fields.m_position)) * 2 } } : l)) };
    const m = matchVanilla(profile(wide), ROADS);
    expect(m.road).toBeNull();
    expect(m.reason).toMatch(/^closest: /);
    expect(profileDifferences(profile(wide), ROADS.roads[0]).join(';')).toMatch(/4 vs 3 m wide/);
    // trees, a TM:PE speed variant, no parking: all stay Road Builder
    expect(profileDifferences({ ...profile(BASIC), trees: true }, ROADS.roads[0])).toContain('trees (vanilla roads have none)');
    expect(matchVanilla(profile(BASIC, null, { speedKmh: 30 }), ROADS).road).toBeNull();
    expect(profileDifferences({ ...profile(BASIC), lanes: profile(BASIC).lanes.filter(l => l.type !== 'parking') }, ROADS.roads[0]).join(';')).toMatch(/parking on the left: no vs yes/);
    // a one-way and a two-way road never match each other
    expect(profileDifferences(profile(BASIC), ROADS.roads[1]).join(';')).toMatch(/backward vs forward/);
    // a bus lane (IMT / TM:PE variant) is not a car lane
    const bus = profile(BASIC, new Map([[1.5, 'bus' as const]]));
    expect(matchVanilla(bus, ROADS).road).toBeNull();
  });

  it('reads the cs2-roads.json shape from the importer dump', () => {
    const dump = { format: 'cs2-roads', roads: [
      { name: 'Small Road', highwayRules: false, width: 16, speedKmh: 50, medianWidth: 0, lanes: [
        { type: 'pedestrian', dir: null, offset: -6.5, width: 3 }, { type: 'parking', dir: null, offset: -4, width: 2 },
        { type: 'car', dir: 'backward', offset: -1.5, width: 3 }, { type: 'car', dir: 'forward', offset: 1.5, width: 3 },
        { type: 'parking', dir: null, offset: 4, width: 2 }, { type: 'pedestrian', dir: null, offset: 6.5, width: 3 }] },
      { name: 'Highway Oneway - 3 lanes', highwayRules: true, width: 16, speedKmh: 50, medianWidth: 0, lanes: [L('car', 'forward', -4, 4), L('car', 'forward', 0, 4), L('car', 'forward', 4, 4)] },
      { name: 'Tram Road', highwayRules: false, width: 16, speedKmh: 50, lanes: [L('car+tram', 'backward', -1.5, 3), L('car+tram', 'forward', 1.5, 3)] },
    ] } as unknown as Cs2RoadCatalogue;
    expect(matchVanilla(profile(BASIC), dump).road?.name).toBe('Small Road');
    expect(matchVanilla(profile(HIGHWAY), dump).road?.name).toBe('Highway Oneway - 3 lanes');
    expect(profileDifferences(profile(BASIC), dump.roads[0])).toEqual([]);
    // car+tram and car,tram are the same lane type; a 5 m median from the dump is a median
    expect(profileDifferences({ ...profile(BASIC), lanes: [L('car,tram', 'backward', -1.5, 3), L('car,tram', 'forward', 1.5, 3)] }, dump.roads[2])).toEqual([]);
    // the dump's medianWidth is not trusted (non-zero on roads without a median); the lane gap is
    expect(profileDifferences(profile(BASIC), { ...dump.roads[0], medianWidth: 5 })).toEqual([]);
    expect(profileDifferences(profile(BASIC), { ...dump.roads[0], lanes: [L('car', 'backward', -2.5, 3), L('car', 'forward', 2.5, 3), L('parking', 'none', -5, 2), L('parking', 'none', 5, 2)] }).join(';')).toMatch(/median none vs 2 m/);
    // the CS1 default speed doesn't count (Basic Road 50, Small Road 40); a TM:PE speed does
    expect(profileDifferences(profile(BASIC), { ...dump.roads[0], speedKmh: 40 })).toEqual([]);
    expect(profileDifferences(profile(BASIC, null, { speedKmh: 30 }), { ...dump.roads[0], speedKmh: 40 }).join(';')).toMatch(/TM:PE speed 30 vs 40/);
    expect(profileDifferences(profile(BASIC, null, { speedKmh: 40 }), { ...dump.roads[0], speedKmh: 40 })).toEqual([]);
    // bridges, quays and dams are never chosen
    expect(matchVanilla(profile(BASIC), { roads: [{ ...dump.roads[0], name: 'QuayMedium01' }, { ...dump.roads[0], name: 'Truss Arch Bridge - Small Road - 2 Lanes' }] }).road).toBeNull();
    expect(slimRoads(dump).roads.map(r => r.category)).toEqual(['road', 'highway', 'road']);
  });

  it('without a road list, nothing matches', () => {
    expect(matchVanilla(profile(BASIC), null)).toEqual({ road: null, reason: 'no vanilla road list' });
    expect(slimRoads({ roads: [...ROADS.roads, { name: 'Dam', category: 'other', width: 5, lanes: [] } as Cs2Road] }).roads).toHaveLength(ROADS.roads.length);
  });
});

describe('plan with vanilla roads', () => {
  const ODD: NetInfo = { ...BASIC, name: 'Odd Road', fields: { ...BASIC.fields, m_halfWidth: 10 }, lanes: [lane(-1.5, 3, 'Vehicle', 'Car', 'Backward'), lane(1.5, 3, 'Vehicle', 'Car', 'Forward')] };
  const c = city({
    netInfos: [BASIC, ODD],
    nodes: [node(1, 0, 0), node(2, 100, 0), node(3, 200, 0)],
    segments: [straight(1, 'Basic Road', 0, 100, 0, false, 1, 2), straight(2, 'Odd Road', 100, 200, 0, false, 2, 3)],
  });
  const crop = { centerX: 100, centerZ: 0, size: 600 };

  it('uses v:<name> for matched roads and installs fewer Road Builder configs', () => {
    const pkg = buildPackage(c, { crop, roads: ROADS });
    const seg = (id: number) => pkg.plan.segments.find(s => s.id === id)!;
    expect(seg(1).prefab).toBe('v:Small Road');
    expect(pkg.plan.prefabs['v:Small Road']).toMatchObject({ type: 'RoadPrefab', name: 'Small Road', source: 'Basic Road', matched: true });
    expect(pkg.plan.prefabs[seg(2).prefab].type).toBe('RoadBuilderPrefab');
    expect(pkg.rbConfigs).toHaveLength(1);
    expect(pkg.mapping.find(r => r.netInfo === 'Basic Road')!.vanilla?.road?.name).toBe('Small Road');
    expect(pkg.mapping.find(r => r.netInfo === 'Odd Road')!.vanilla?.reason).toMatch(/^closest: /);
  });

  it('"Prefer vanilla roads" off, or a Road Builder choice for the type, keeps Road Builder', () => {
    expect(buildPackage(c, { crop, roads: ROADS, preferVanilla: false }).rbConfigs).toHaveLength(2);
    const rbChoice = buildPackage(c, { crop, roads: ROADS, overrides: { 'Basic Road': { mode: 'rb' } } });
    expect(rbChoice.plan.segments.find(s => s.id === 1)!.prefab).toMatch(/^rb:/);
    expect(rbChoice.mapping.find(r => r.netInfo === 'Basic Road')!.vanilla).toBeUndefined();
  });
});

// With the real list from the game, when it's on this machine.
const ROADS_FILE = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local'), 'cs1-to-cs2', 'samples', 'cs2-roads.json');
const SAMPLE = path.join(path.dirname(ROADS_FILE), 'skibidi.cs1x');
describe.skipIf(!fs.existsSync(ROADS_FILE) || !fs.existsSync(SAMPLE))('Skibidi with the real CS2 road list', () => {
  it('maps the vanilla CS1 Basic Road to a vanilla CS2 road', async () => {
    const roads = JSON.parse(fs.readFileSync(ROADS_FILE, 'utf8').replace(/^﻿/, '')) as Cs2RoadCatalogue;
    const c = await readCityFile(new Uint8Array(fs.readFileSync(SAMPLE)), { hash: false });
    const { roadSegments: _r, ...crop } = autoCrop(c, 3000);
    const pkg = buildPackage(c, { crop, roads });
    const basic = pkg.mapping.find(r => r.netInfo === 'Basic Road')!;
    expect(basic.vanilla?.road?.name, basic.vanilla?.reason).toBe('Small Road');
    // the bundled slim list gives the same result for every road type
    const slim = JSON.parse(fs.readFileSync(path.join(import.meta.dirname, '..', 'public', 'cs2-roads.json'), 'utf8')) as Cs2RoadCatalogue;
    const viaSlim = buildPackage(c, { crop, roads: slim });
    expect(viaSlim.mapping.map(r => [r.netInfo, r.vanilla?.road?.name ?? null])).toEqual(pkg.mapping.map(r => [r.netInfo, r.vanilla?.road?.name ?? null]));
    expect(Object.keys(viaSlim.plan.prefabs).sort()).toEqual(Object.keys(pkg.plan.prefabs).sort());
  }, 60_000);
});
