import { strToU8, zipSync } from 'fflate';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { translator } from '../src/core/i18n/index.ts';
import { buildBuildingsPlan } from '../../buildings/lib/plan.mjs';
import { matchAssets } from '../../buildings/match/match.mjs';
import { readCityFile, readCs1x } from '../src/core/cs1x.ts';
import type { PlanContext } from '../src/core/m2/context.ts';
import { buildBuildings, facingFor, matchAssetTypes } from '../src/core/m3/buildings.ts';
import type { Cs2Catalogue } from '../src/core/m3/buildings.ts';
import { slimCatalogue } from '../src/core/m3/catalogue.ts';
import { buildDistricts, mapPolicies, traceOutlines } from '../src/core/m3/districts.ts';
import { buildPackage, packageFiles } from '../src/core/plan.ts';
import type { Cs1Asset, Cs1Building, Cs1City, NetInfo } from '../src/core/types.ts';
import { city, lane, node, straight } from './fixtures.ts';

const ROAD: NetInfo = {
  name: 'Basic Road', ai: 'RoadAI', service: 'Road', parent: null, fields: { m_halfWidth: 8, m_pavementWidth: 3 },
  lanes: [lane(-1.5, 3, 'Vehicle', 'Car', 'Backward'), lane(1.5, 3, 'Vehicle', 'Car', 'Forward')],
};
const CATALOGUE: Cs2Catalogue = {
  game: { version: '1.6.4f1' },
  prefabs: [
    { name: 'Hospital01', kind: 'building', placeable: true, lot: [5, 5], components: { Hospital: {} }, capacity: { patients: 500 } },
    { name: 'MedicalClinic01', kind: 'building', placeable: true, lot: [3, 3], components: { Hospital: {} }, capacity: { patients: 100 } },
    { name: 'ElementarySchool01', kind: 'building', placeable: true, lot: [4, 4], components: { School: { m_Level: 'Elementary' } }, capacity: { students: 1000 } },
    { name: 'CityPark01', kind: 'building', placeable: true, lot: [2, 2], components: { Park: {} } },
    { name: 'Some Prop', kind: 'prop', placeable: true },
  ],
};
const ASSETS: Cs1Asset[] = [
  { name: 'Hospital', title: 'Hospital', count: 3, service: 'HealthCare', ai: 'HospitalAI', growable: false, footprint: [4, 4], aiFields: { m_patientCapacity: 500 } },
  { name: 'Elementary School', count: 1, service: 'Education', ai: 'SchoolAI', level: 'Level1', growable: false, footprint: [3, 3], aiFields: { m_studentCount: 300 } },
  { name: 'Public Library', count: 1, service: 'Education', ai: 'LibraryAI', growable: false, footprint: [3, 3] },
  { name: 'L1 2x2 Detached01', count: 2, service: 'Residential', subService: 'ResidentialLow', zone: 'ResidentialLow', ai: 'ResidentialBuildingAI', growable: true, footprint: [2, 2] },
  { name: 'HighwayBridgePillar', count: 1, service: 'Road', ai: 'BuildingAI', growable: false, footprint: [1, 1] },
];
const bld = (id: number, info: string, x: number, z: number, extra: Partial<Cs1Building> = {}): Cs1Building =>
  ({ id, info, position: [x, 0, z], angle: 90, growable: false, width: 4, length: 4, parent: 0, accessSegment: 0, ...extra });

// Road 1 along x at z = 0 (in the plan); road 2 far away (outside the crop).
function town(extra: Partial<Cs1City> = {}): Cs1City {
  return city({
    netInfos: [ROAD],
    nodes: [node(1, -200, 0), node(2, 200, 0), node(3, 5000, 5000), node(4, 5100, 5000), node(5, 200, 400), node(6, 600, 400)],
    segments: [straight(1, ROAD.name, -200, 200, 0, false, 1, 2), straight(2, ROAD.name, 5000, 5100, 5000, false, 3, 4), straight(3, ROAD.name, 200, 600, 400, false, 5, 6)],
    buildings: {
      buildings: [
        bld(10, 'Hospital', 0, 30, { accessSegment: 1 }),
        bld(11, 'Hospital', 100, -40, { accessSegment: 1, angle: 270 }),
        bld(12, 'Hospital', 5050, 5030, { accessSegment: 2 }), // outside the crop
        bld(13, 'Elementary School', -100, 25, { accessSegment: 1, district: 3 }),
        bld(14, 'Public Library', 50, 25, { accessSegment: 1 }),
        bld(15, 'L1 2x2 Detached01', -50, 20, { growable: true, accessSegment: 1, width: 2, length: 2 }),
        bld(16, 'L1 2x2 Detached01', 60, -20, { growable: true, accessSegment: 1, width: 2, length: 2 }),
        bld(17, 'HighwayBridgePillar', 10, 10),
        bld(18, 'Hospital', 20, 60, { parent: 10 }), // sub-building
        bld(19, 'Elementary School', 300, 390, { accessSegment: 3 }), // road crosses the crop edge → not in the plan
      ],
    },
    assets: { buildings: ASSETS },
    ...extra,
  });
}
const CROP = { centerX: 0, centerZ: 0, size: 800 };

function ctxOf(c: Cs1City): PlanContext {
  const pkg = buildPackage(c, { crop: CROP, buildings: false });
  return {
    city: c, crop: CROP, dx: 0, dz: 0, infos: new Map(c.netInfos.map(i => [i.name, i])), nodes: new Map(c.nodes.map(n => [n.id, n])),
    segments: new Map(c.segments.map(s => [s.id, s])), planSegments: new Set(pkg.plan.segments.map(s => s.id)), planNodes: new Set(pkg.plan.nodes.map(n => n.id)),
    reversed: new Set(), inCrop: (x, z) => Math.abs(x) <= 400 && Math.abs(z) <= 400, t: translator('en'), lang: 'en',
  };
}

describe('asset matching adapter', () => {
  it('normalises the matcher output and applies per-type overrides', () => {
    const m = matchAssetTypes(ASSETS, CATALOGUE);
    const by = Object.fromEntries(m.map(x => [x.cs1, x]));
    expect(by['Hospital']).toMatchObject({ decision: 'place', prefab: 'Hospital01', lot: [5, 5], family: 'hospital', growable: false });
    expect(by['Hospital'].score).toBeGreaterThan(0.8);
    expect(by['Hospital'].alternatives).toEqual([]); // the matcher keeps to the same kind: a hospital is not a clinic
    expect(by['Elementary School']).toMatchObject({ decision: 'place', prefab: 'ElementarySchool01' });
    expect(by['Public Library']).toMatchObject({ decision: 'noEquivalent', prefab: null, score: null });
    expect(by['L1 2x2 Detached01']).toMatchObject({ decision: 'growable', growable: true });
    expect(by['HighwayBridgePillar'].decision).toBe('skip');
    const o = Object.fromEntries(matchAssetTypes(ASSETS, CATALOGUE, { overrides: { Hospital: 'MedicalClinic01', 'Elementary School': 'skip', 'Public Library': 'CityPark01', Nope: 'X' } }).map(x => [x.cs1, x]));
    expect(o['Hospital']).toMatchObject({ decision: 'place', prefab: 'MedicalClinic01', lot: [3, 3], score: null, reason: 'chosen by you', override: 'MedicalClinic01' });
    expect(o['Elementary School']).toMatchObject({ decision: 'skip', prefab: null, override: 'skip' });
    expect(o['Public Library']).toMatchObject({ decision: 'place', prefab: 'CityPark01' });
  });

  it('learns the angle convention and offers the four candidates', () => {
    expect(facingFor('cos,sin', 0)).toEqual([1, 0]);
    expect(facingFor('-sin,cos', 90).map(v => Math.round(v))).toEqual([-1, 0]);
  });
});

describe('plan.buildings', () => {
  it('places matched buildings on plan roads and counts the rest', () => {
    const r = buildBuildings(ctxOf(town()), { enabled: true, catalogue: CATALOGUE });
    const s = r.section!;
    expect(s.items.map(i => [i.id, i.prefab])).toEqual([[10, 'Hospital01'], [11, 'Hospital01'], [13, 'ElementarySchool01']]);
    expect(s.stats.skipped).toEqual({ outsideCrop: 1, subBuilding: 1, growable: 2, skip: 1, noEquivalent: 1, roadNotImported: 1 });
    expect(s.stats).toMatchObject({ items: 3, withRoad: 3, withoutRoad: 0, growablesInCrop: 2 });
    const h = s.items[0];
    expect(h).toMatchObject({ type: 'BuildingPrefab', x: 0, z: 30, front: [0, 0], facing: [0, -1], cs1Lot: [4, 4], lot: [5, 5], accessSegment: 1 });
    expect(s.items[2].district).toBe(3);
    expect(r.matches.find(m => m.cs1 === 'Hospital')!.countInCrop).toBe(2);
    expect(r.items.some(i => /Public Library: 1 building not placed – CS2 has no library/.test(i.message))).toBe(true);
    expect(r.items.some(i => /access road is not in the plan/.test(i.message))).toBe(true);
    expect(r.summary).toMatchObject({ id: 'buildings', status: 'partial' });
  });

  it('matches buildings/lib/plan.mjs exactly (same items)', () => {
    const c = town();
    const pkg = buildPackage(c, { crop: CROP, buildings: false });
    const ours = buildBuildings(ctxOf(c), { enabled: true, catalogue: CATALOGUE, theme: 'NA', fallbacks: { Hospital: 'skip' } }).section!;
    const theirs = buildBuildingsPlan({ network: { nodes: c.nodes, segments: c.segments }, buildings: c.buildings!, assets: c.assets! }, CATALOGUE, pkg.plan,
      { theme: 'North American', fallback: 'nearby', fallbackByType: { Hospital: 'skip' } });
    expect(ours.items.map(({ type: _t, district: _d, ...i }) => i)).toEqual(theirs.items);
    expect(ours.cs1AngleConvention).toEqual(theirs.cs1AngleConvention);
    expect({ ...ours.stats.skipped }).toEqual(theirs.stats.skipped);
  });

  it('without a catalogue or buildings it says so; overrides reach the package', () => {
    expect(buildBuildings(ctxOf(town()), { enabled: true, catalogue: null }).summary).toMatchObject({ status: 'not-converted', summary: expect.stringMatching(/catalogue/) });
    expect(buildBuildings(ctxOf(town({ buildings: null })), { enabled: true, catalogue: CATALOGUE }).summary.status).toBe('absent');
    expect(buildBuildings(ctxOf(town()), { enabled: false, catalogue: CATALOGUE }).summary.status).toBe('not-converted');
    const pkg = buildPackage(town(), { crop: CROP, catalogue: CATALOGUE, assetOverrides: { Hospital: 'MedicalClinic01' } });
    expect(pkg.plan.buildings!.items.filter(i => i.cs1Info === 'Hospital').map(i => i.prefab)).toEqual(['MedicalClinic01', 'MedicalClinic01']);
    expect(pkg.summary.find(s => s.id === 'buildings')!.status).toBe('partial');
    expect(packageFiles(pkg).find(f => f.path === 'README.txt')!.text).toMatch(/Buildings and services/);
  });

  it('writes a placement fallback per item: anywhere for big lots by default, the choice of the user otherwise', () => {
    const big: Cs2Catalogue = { ...CATALOGUE, prefabs: CATALOGUE.prefabs.map(p => (p.name === 'Hospital01' ? { ...p, lot: [25, 20] } : p)) };
    const pkg = buildPackage(town(), { crop: CROP, catalogue: big });
    const fb = (p: typeof pkg) => Object.fromEntries(p.plan.buildings!.items.map(i => [i.cs1Info, i.fallback]));
    expect(fb(pkg)).toEqual({ Hospital: 'anywhere', 'Elementary School': 'nearby' });
    expect(pkg.assetMatches!.find(m => m.cs1 === 'Hospital')).toMatchObject({ fallback: 'anywhere', fallbackAuto: 'anywhere' });
    const chosen = buildPackage(town(), { crop: CROP, catalogue: big, assetFallbacks: { Hospital: 'skip', 'Elementary School': 'bogus' as never } });
    expect(fb(chosen)).toEqual({ Hospital: 'skip', 'Elementary School': 'nearby' });
    expect(chosen.assetMatches!.find(m => m.cs1 === 'Hospital')!.fallbackOverride).toBe('skip');
    expect(chosen.report.items.some(i => /0 anywhere along the imported roads in the crop, 2 are not placed/.test(i.message))).toBe(true);
    expect(fb(buildPackage(town(), { crop: CROP, catalogue: big, assetFallback: 'skip' }))).toEqual({ Hospital: 'skip', 'Elementary School': 'skip' });
  });
});

describe('slim catalogue', () => {
  it('keeps placeable buildings and the matcher fields, with identical matches', () => {
    const slim = slimCatalogue({ format: 'cs2-catalogue', game: { version: 'x' }, prefabs: [...CATALOGUE.prefabs, { name: 'Unplaceable', kind: 'building', placeable: false }] });
    expect(slim.prefabs.map(p => p.name)).toEqual(['Hospital01', 'MedicalClinic01', 'ElementarySchool01', 'CityPark01']);
    expect(JSON.stringify(matchAssets({ buildings: ASSETS }, slim).results)).toBe(JSON.stringify(matchAssets({ buildings: ASSETS }, CATALOGUE).results));
  });
});

describe('districts', () => {
  it('traces outer outlines and counts holes', () => {
    // 5×5 grid: an L of id 1, a 3×3 ring of id 2 with a hole, a lone cell of id 1
    const n = 5, g = new Uint8Array(n * n);
    const set = (c: number, r: number, v: number) => { g[r * n + c] = v; };
    set(0, 0, 1); set(1, 0, 1); set(0, 1, 1); set(4, 4, 1);
    for (let r = 2; r <= 4; r++) for (let c = 2; c <= 4; c++) if (!(c === 3 && r === 3)) set(c, r, 2);
    set(4, 4, 1);
    const l = traceOutlines(g, n, 1);
    expect(l.outers).toHaveLength(2);
    expect(l.outers.map(o => o.length).sort()).toEqual([4, 6]);
    const ring = traceOutlines(g, n, 2);
    expect(ring.outers).toHaveLength(1);
    expect(ring.holes).toBe(1);
    expect(traceOutlines(g, n, 1, c => c < 3).outers).toHaveLength(1);
  });

  it('maps CS1 policies to CS2 where an equivalent exists', () => {
    expect(mapPolicies(['HeavyTrafficBan', 'SmokeDetectors', 'Leisure', 'PetBan'])).toEqual({
      cs2: ['Heavy Traffic Ban', 'Fire Safety Sprinkler System'], approx: ['smoke detectors → sprinkler systems'], specialisation: ['Leisure'], none: ['PetBan'],
    });
  });

  it('exports district outlines in plan coordinates with policies, and reports park areas', () => {
    const n = 10, grid = new Uint8Array(n * n);
    for (let r = 2; r < 5; r++) for (let c = 2; c < 6; c++) grid[r * n + c] = 3; // cells 2..5 × 2..4 → x −300…100? (size 1000, cell 100)
    const c = town({
      districts: {
        grid: { resolution: n, cellSize: 100, size: 1000 }, districtGrid: grid,
        city: { policies: ['Recycling'] },
        districts: [{ id: 3, name: 'Old Town', policies: ['HeavyTrafficBan', 'Tourist'] }, { id: 4, name: 'Nowhere', policies: [] }],
        parks: [{ id: 1, name: 'Ore Industry Area', kind: 'industry', type: 'Ore', cells: 40 }],
      },
    });
    const r = buildDistricts(ctxOf(c));
    expect(r.section!.cityPolicies).toEqual(['Recycling']);
    expect(r.section!.items).toEqual([{ cs1Id: 3, name: 'Old Town', outlines: [[[-300, -300], [100, -300], [100, 0], [-300, 0]]], policies: ['Heavy Traffic Ban'], cs1Policies: ['HeavyTrafficBan', 'Tourist'], area: 120000 }]);
    expect(r.items.some(i => /Tourist .*zone specialisation/.test(i.message))).toBe(true);
    expect(r.items.some(i => /park area .*industry 1/.test(i.message))).toBe(true);
    expect(r.summary).toMatchObject({ status: 'partial' });
    const pkg = buildPackage(c, { crop: { centerX: 100, centerZ: 0, size: 800 } });
    expect(pkg.plan.districts!.items[0].outlines[0][0]).toEqual([-400, -300]);
    expect(buildDistricts(ctxOf(town())).summary.status).toBe('absent');
  });
});

describe('reader: M3 sections', () => {
  it('reads buildings, assets and districts with their rasters', () => {
    const n = 2;
    const c = readCs1x(zipSync({
      'manifest.json': strToU8(JSON.stringify({ format: 'cs1x', formatVersion: 1, sections: { network: 'ok', buildings: 'ok', assets: 'ok', districts: 'ok' } })),
      'network.json': strToU8('{"nodes":[],"segments":[]}'),
      'buildings.json': strToU8(JSON.stringify({ buildings: [bld(1, 'Hospital', 0, 0)] })),
      'assets.json': strToU8(JSON.stringify({ buildings: ASSETS })),
      'districts.json': strToU8(JSON.stringify({ grid: { resolution: n, cellSize: 10, size: 20, districtFile: 'districts.u8', parkFile: 'parks.u8' }, districts: [{ id: 1, name: 'A' }], parks: [] })),
      'districts.u8': new Uint8Array([0, 1, 1, 0]),
    }));
    expect(c.buildings!.buildings[0].info).toBe('Hospital');
    expect(c.assets!.buildings).toHaveLength(5);
    expect([...c.districts!.districtGrid!]).toEqual([0, 1, 1, 0]);
    expect(c.districts!.parkGrid).toBeUndefined();
  });
});

// The real thing, when the buildings worker's catalogue and the Skibidi sample are on disk.
const LOCAL = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local'), 'cs1-to-cs2', 'samples');
const FULL = path.join(LOCAL, 'cs2-catalogue.json'), SAMPLE = path.join(LOCAL, 'skibidi.cs1x');
describe.skipIf(!fs.existsSync(FULL) || !fs.existsSync(SAMPLE))('Skibidi with the real CS2 catalogue', () => {
  it('slim catalogue matches like the full one, and plan items equal buildings/lib/plan.mjs', async () => {
    const full = JSON.parse(fs.readFileSync(FULL, 'utf8'));
    const slim = slimCatalogue(full);
    const c = await readCityFile(new Uint8Array(fs.readFileSync(SAMPLE)), { hash: false });
    if (!c.assets || !c.buildings) return;
    expect(JSON.stringify(matchAssets(c.assets, slim).results)).toBe(JSON.stringify(matchAssets(c.assets, full).results));
    const crop = { centerX: 3750, centerZ: -1250, size: 3000 };
    const pkg = buildPackage(c, { crop, catalogue: slim });
    const fallbackByType = Object.fromEntries((pkg.assetMatches ?? []).map(m => [m.cs1, m.fallback]));
    const theirs = buildBuildingsPlan({ network: { nodes: c.nodes, segments: c.segments }, buildings: c.buildings, assets: c.assets }, full, pkg.plan, { theme: 'North American', fallbackByType }); // the package uses the zoning theme (NA)
    const strip = (l: unknown[]) => (l as Record<string, unknown>[]).map(({ type: _t, district: _d, ...i }) => i); // default depots carry type in both
    expect(strip(pkg.plan.buildings!.items)).toEqual(strip(theirs.items));
    expect(pkg.plan.buildings!.defaultDepots ?? []).toEqual((theirs as { defaultDepots?: unknown[] }).defaultDepots ?? []);
    expect(pkg.plan.buildings!.items.length).toBeGreaterThan(50);
  }, 60_000);
});

describe('objects and surfaces (buildings/lib/objects.mjs)', () => {
  const VEG: Cs2Catalogue = {
    game: { version: 'x' },
    prefabs: [
      ...CATALOGUE.prefabs,
      { name: 'OakTree01', kind: 'object', uiGroup: 'Vegetation', height: 12, components: { TreeObject: {} } },
      { name: 'SpruceTree01', kind: 'object', uiGroup: 'Vegetation', height: 21, components: { TreeObject: {} } },
      { name: 'Boulder01', kind: 'object', uiGroup: 'PropsNature', height: 2.4 },
    ],
  };
  const fill = (markingId: number, styleType: string, x: number, z: number) =>
    ({ markingType: 'Node', markingId, styleType, color: [1, 2, 3, 4], polygon: [[x, 0, z], [x + 4, 0, z], [x + 4, 0, z + 4], [x, 0, z + 4]] as [number, number, number][] });
  const green = () => town({
    trees: { trees: [[0, 10, 0, 20, 0], [1, 30, 0, -50, 0], [0, 5000, 0, 5000, 0]], infos: ['Tree2variant', 'Conifer'] },
    props: { props: [[0, 15, 0, 15, 90, 0], [1, 20, 0, 20, 0, 0]], infos: ['Rock 01', 'Bench 01'] },
    assets: {
      buildings: ASSETS,
      trees: [{ name: 'Tree2variant', title: 'Oak', count: 2, height: 11 }, { name: 'Conifer', title: 'Conifer', count: 1, height: 20 }],
      props: [{ name: 'Rock 01', count: 1, height: 2 }, { name: 'Bench 01', count: 1, height: 1 }],
    } as never,
    imtFillers: [fill(1, 'FillerGrass', 0, 0), fill(2, 'FillerSolid', 100, 0), fill(3, 'FillerPavement', 5000, 5000)],
  });

  it('puts the same rows in the plan as buildObjectsSection / buildSurfacesSection', async () => {
    const { buildObjectsSection, buildSurfacesSection } = await import('../../buildings/lib/objects.mjs');
    const c = green();
    const pkg = buildPackage(c, { crop: CROP, catalogue: VEG });
    const roadPlan = { crop: pkg.plan.crop, transform: pkg.plan.transform };
    expect(pkg.plan.objects).toEqual(buildObjectsSection({ trees: c.trees ?? undefined, props: c.props ?? undefined, assets: c.assets ?? {} }, VEG, roadPlan, { theme: 'NA' }));
    expect(pkg.plan.objects!.prefabs.sort()).toEqual(['Boulder01', 'OakTree01', 'SpruceTree01']);
    expect(pkg.plan.objects!.stats.items).toBe(3); // 2 trees in the crop + the rock; the bench is not placed
    expect(pkg.plan.surfaces).toEqual(buildSurfacesSection({ fillers: c.imtFillers }, roadPlan, {}));
    expect(pkg.plan.surfaces!.items.map(s => [s.cs1Node, s.prefab])).toEqual([[1, 'Grass Surface 01']]);
    const nodeFillReports = pkg.report.items.filter(i => i.kind === 'imt-node-fill').map(i => i.cs1?.node);
    expect(nodeFillReports).not.toContain(1); // converted, so not "no CS2 equivalent"
    expect(pkg.summary.find(s => s.id === 'objects')).toMatchObject({ status: 'partial', summary: '2 trees and 1 rock as CS2 trees and boulders' });
    expect(pkg.summary.find(s => s.id === 'imt')!.summary).toContain('1 junction fill becomes a CS2 surface');
  });

  it('leaves them out when switched off, without trees in the catalogue, or without a catalogue', () => {
    const c = green();
    const off = buildPackage(c, { crop: CROP, catalogue: VEG, objects: false, surfaces: false });
    expect(off.plan.objects).toBeUndefined();
    expect(off.plan.surfaces).toBeUndefined();
    expect(off.report.items.filter(i => i.kind === 'imt-node-fill').map(i => i.cs1?.node)).toContain(1);
    const noTrees = buildPackage(c, { crop: CROP, catalogue: CATALOGUE });
    expect(noTrees.plan.objects).toBeUndefined();
    expect(noTrees.summary.find(s => s.id === 'objects')).toMatchObject({ status: 'not-converted', summary: 'the CS2 catalogue has no trees' });
    expect(buildPackage(c, { crop: CROP }).summary.find(s => s.id === 'objects')!.status).toBe('not-converted');
    expect(buildPackage(town(), { crop: CROP, catalogue: VEG }).summary.find(s => s.id === 'objects')!.status).toBe('absent');
  });

  it('keeps CS2 trees and boulders in the slim catalogue', () => {
    const slim = slimCatalogue(VEG);
    expect(slim.prefabs.filter(p => p.kind === 'object').map(p => p.name)).toEqual(['OakTree01', 'SpruceTree01', 'Boulder01']);
  });

  it('reader: reads trees.json and props.json', () => {
    const c = readCs1x(zipSync({
      'manifest.json': strToU8(JSON.stringify({ format: 'cs1x', formatVersion: 1, sections: { network: 'ok', trees: 'ok', props: 'ok' } })),
      'network.json': strToU8('{"nodes":[],"segments":[]}'),
      'trees.json': strToU8(JSON.stringify({ format: 'x', trees: [[0, 1, 2, 3, 0]], infos: ['Conifer'] })),
      'props.json': strToU8(JSON.stringify({ format: 'x', props: [[0, 1, 2, 3, 90, 0]], infos: ['Rock 01'] })),
    }));
    expect(c.trees!.infos).toEqual(['Conifer']);
    expect(c.props!.props[0][4]).toBe(90);
  });
});

describe.skipIf(!fs.existsSync(FULL) || !fs.existsSync(SAMPLE))('Skibidi trees with the real CS2 catalogue', () => {
  it('slim catalogue gives the same objects section as the full one', async () => {
    const { buildObjectsSection } = await import('../../buildings/lib/objects.mjs');
    const full = JSON.parse(fs.readFileSync(FULL, 'utf8'));
    const c = await readCityFile(new Uint8Array(fs.readFileSync(SAMPLE)), { hash: false });
    if (!c.trees) return;
    const crop = { centerX: 3750, centerZ: -1250, size: 3000 };
    const pkg = buildPackage(c, { crop, catalogue: slimCatalogue(full), buildings: false });
    expect(pkg.plan.objects).toEqual(buildObjectsSection({ trees: c.trees ?? undefined, props: c.props ?? undefined, assets: c.assets ?? {} }, full, pkg.plan, { theme: 'NA' }));
    expect(pkg.plan.objects!.stats.items).toBeGreaterThan(1000);
  }, 60_000);
});
