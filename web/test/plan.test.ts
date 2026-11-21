import { describe, expect, it } from 'vitest';
import { CS2_MAX_CROP, clampCrop, densestCenter, inCrop, validateCrop } from '../src/core/crop.ts';
import { categorize, resolveTarget } from '../src/core/mapping.ts';
import { strFromU8, strToU8, unzipSync, zipSync } from 'fflate';
import { autoCrop, buildPackage, mappingStats, nodeElevation, packageFiles, segmentKind } from '../src/core/plan.ts';
import type { NetInfo } from '../src/core/types.ts';
import { city, fiveLane, fill, lane, laneZ, node, rect, straight } from './fixtures.ts';

const track: NetInfo = {
  name: 'Train Track', ai: 'TrainTrackAI', service: 'PublicTransport', subService: 'PublicTransportTrain', fields: { m_halfWidth: 5, m_pavementWidth: 1.875 },
  lanes: [lane(-2, 4, 'Vehicle', 'Train', 'Backward'), lane(2, 4, 'Vehicle', 'Train', 'Forward')],
};
const pipe: NetInfo = { name: 'Water Pipe', ai: 'WaterPipeAI', service: 'Water', fields: { m_halfWidth: 4 }, lanes: [] };
const path: NetInfo = { name: 'Pedestrian Pavement', ai: 'PedestrianPathAI', service: 'Beautification', fields: { m_halfWidth: 3, m_pavementWidth: 1.5 }, lanes: [lane(0, 3, 'Pedestrian', 'None', 'Both')] };
const elevated: NetInfo = { ...fiveLane, name: 'Test Five-Lane Elevated', ai: 'RoadBridgeAI', parent: 'Test Five-Lane' };
const oneTrack: NetInfo = { ...track, name: 'Train Oneway Track', lanes: [lane(0, 4, 'Vehicle', 'Train', 'Forward')] };

describe('crop', () => {
  it('validates against the CS2 playable area', () => {
    expect(validateCrop({ centerX: 0, centerZ: 0, size: 3000 }).errors).toEqual([]);
    expect(validateCrop({ centerX: 0, centerZ: 0, size: CS2_MAX_CROP + 1 }).errors[0]).toMatch(/exceeds/);
    expect(validateCrop({ centerX: 8000, centerZ: 0, size: 3000 }).warnings[0]).toMatch(/beyond/);
    expect(clampCrop({ centerX: 9000, centerZ: -9000, size: 20000 })).toEqual({ centerX: 1472, centerZ: -1472, size: CS2_MAX_CROP });
    expect(inCrop({ centerX: 100, centerZ: 100, size: 200 }, 0, 200)).toBe(true);
    expect(inCrop({ centerX: 100, centerZ: 100, size: 200 }, -1, 100)).toBe(false);
  });
  it('finds the densest area', () => {
    const nodes = [...Array(50)].map((_, i) => ({ position: [1000 + (i % 7) * 10, 0, -2000 + i] as [number, number, number] })).concat([{ position: [-5000, 0, 5000] }]);
    const c = densestCenter(nodes);
    expect(Math.abs(c.x - 1000)).toBeLessThan(300);
    expect(Math.abs(c.z + 2000)).toBeLessThan(300);
  });
});

describe('mapping', () => {
  it('categorizes and resolves NetInfos', () => {
    expect(categorize(fiveLane)).toBe('road');
    expect(resolveTarget(track)).toEqual({ mode: 'vanilla', type: 'TrackPrefab', name: 'Double Train Track' });
    expect(resolveTarget(oneTrack)).toEqual({ mode: 'vanilla', type: 'TrackPrefab', name: 'Oneway Train Track' });
    expect(resolveTarget(path)).toEqual({ mode: 'vanilla', type: 'PathwayPrefab', name: 'Pavement Path' });
    expect(resolveTarget(pipe)).toMatchObject({ mode: 'skip', kind: 'not-placeable' });
    expect(resolveTarget({ name: 'Weird', ai: 'SomethingAI', fields: {}, lanes: [] })).toMatchObject({ mode: 'skip', kind: 'unmapped-netinfo' });
    expect(resolveTarget(fiveLane, { mode: 'vanilla', type: 'RoadPrefab', name: 'Large Road' })).toEqual({ mode: 'vanilla', type: 'RoadPrefab', name: 'Large Road' });
    expect(resolveTarget(track, { mode: 'skip' })).toMatchObject({ mode: 'skip' });
  });
});

describe('elevation', () => {
  it('derives kind and course elevation', () => {
    expect(segmentKind(straight(1, 'x', 0, 1, 0, false), fiveLane)).toBe('ground');
    expect(segmentKind({ ...straight(1, 'x', 0, 1, 0, false), elevationKind: 'bridge' }, elevated)).toBe('elevated');
    expect(segmentKind({ ...straight(1, 'x', 0, 1, 0, false), elevationKind: 'bridge' }, { ...elevated, name: 'X Bridge' })).toBe('bridge');
    expect(segmentKind({ ...straight(1, 'x', 0, 1, 0, false), elevationKind: 'tunnel' }, { ...elevated, name: 'X Slope', ai: 'RoadTunnelAI' })).toBe('slope');
    expect(segmentKind({ ...straight(1, 'x', 0, 1, 0, false), elevationKind: 'underground' }, fiveLane)).toBe('tunnel');
    expect(nodeElevation('ground', node(1, 0, 0, { heightAboveTerrain: 12 }))).toBe(0);
    expect(nodeElevation('elevated', node(1, 0, 0, { heightAboveTerrain: 12.345 }))).toBe(12.35);
    expect(nodeElevation('elevated', node(1, 0, 0, { heightAboveTerrain: -0.3 }))).toBe(0);
    expect(nodeElevation('tunnel', node(1, 0, 0, { heightAboveTerrain: -20 }))).toBe(-20);
  });
});

describe('buildPackage', () => {
  // Network: road 1 (0→60 at z=0), road 2 inverted with a red fill over lane 0 (whole lane), an elevated
  // road, a train track, a pipe, a building path, a segment crossing the crop edge, one far outside.
  const nodes = [
    node(10, 0, 0), node(11, 60, 0), node(20, 60, 100), node(21, 0, 100),
    node(30, 0, 200, { heightAboveTerrain: 8 }), node(31, 60, 200, { heightAboveTerrain: 10 }),
    node(40, 0, 300), node(41, 60, 300), node(50, 0, 400), node(51, 60, 400), node(60, 0, 450), node(61, 60, 450),
    node(70, 60, 500), node(71, 900, 500), node(80, 5000, 5000), node(81, 5060, 5000),
  ];
  const segs = [
    straight(1, fiveLane.name, 0, 60, 0, false, 10, 11),
    straight(2, fiveLane.name, 60, 0, 100, true, 20, 21),
    { ...straight(3, elevated.name, 0, 60, 200, false, 30, 31), elevationKind: 'bridge' },
    straight(4, track.name, 0, 60, 300, false, 40, 41),
    straight(5, pipe.name, 0, 60, 400, false, 50, 51),
    { ...straight(6, path.name, 0, 60, 450, false, 60, 61), flags: 'Created, Untouchable' },
    straight(7, fiveLane.name, 60, 900, 500, false, 70, 71),
    straight(8, fiveLane.name, 5000, 5060, 5000, false, 80, 81),
  ];
  const c = city({
    nodes, segments: segs, netInfos: [fiveLane, elevated, track, pipe, path],
    imtFillers: [
      fill('Segment', 2, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, ...laneZ(100, 0, 3))),
      fill('Node', 10, 'FillerSolid', [136, 0, 0, 94], rect(-5, 5, -5, 5)),
      fill('Segment', 1, 'FillerSolid', [136, 136, 136, 224], rect(10, 50, ...laneZ(0, 3, 3))),
      fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, -6.0, -4.6)),
    ],
    sha256: 'abc',
  });
  const pkg = buildPackage(c, { crop: { centerX: 100, centerZ: 200, size: 800 }, generatedAt: '2026-11-21T00:00:00Z' });
  const { plan } = pkg;

  it('includes segments with both nodes in the crop and a mapped prefab', () => {
    expect(plan.segments.map(s => s.id).sort()).toEqual([1, 2, 3, 4]);
    expect(plan.stats).toMatchObject({ segments: 4, outsideCrop: 1, crossingCropEdge: 1, unmapped: 2, skipped: 3, rbConfigs: 2 });
    expect(plan.stats.byPrefabType).toEqual({ RoadBuilderPrefab: 3, TrackPrefab: 1 });
    expect(plan.nodes.map(n => n.id)).toEqual([10, 11, 20, 21, 30, 31, 40, 41]);
  });

  it('transforms to CS2 coordinates (crop centre → origin)', () => {
    expect(plan.transform).toEqual({ dx: -100, dz: -200, heightMode: 'terrain-relative' });
    expect(plan.bounds).toEqual({ minX: -400, maxX: 400, minZ: -400, maxZ: 400 });
    const n10 = plan.nodes.find(n => n.id === 10)!;
    expect([n10.x, n10.z]).toEqual([-100, -200]);
    expect(plan.segments.find(s => s.id === 1)!.bezier[0]).toEqual([-100, 0, -200]);
    expect(plan.source).toMatchObject({ city: 'Test', saveName: 'Test.01', cs1xSha256: 'abc' });
  });

  it('reverses inverted segments and maps the elevated variant to its ground road', () => {
    const s2 = plan.segments.find(s => s.id === 2)!;
    expect(s2.reversed).toBe(true);
    expect([s2.start, s2.end]).toEqual([21, 20]);
    expect(s2.bezier[0]).toEqual([-100, 0, -100]);
    const s3 = plan.segments.find(s => s.id === 3)!;
    expect(s3).toMatchObject({ kind: 'elevated', elevationStart: 8, elevationEnd: 10, cs1Info: elevated.name });
    expect(plan.prefabs[s3.prefab]).toMatchObject({ type: 'RoadBuilderPrefab', source: fiveLane.name });
    expect(s3.prefab).toBe(plan.segments.find(s => s.id === 1)!.prefab);
  });

  it('turns a whole-lane red fill into an RB variant, reports node fills, partial fills and paint', () => {
    const s2 = plan.segments.find(s => s.id === 2)!;
    expect(plan.prefabs[s2.prefab].imt).toBe('0=bus');
    expect(s2.prefab).not.toBe(plan.segments.find(s => s.id === 1)!.prefab);
    const kinds = pkg.report.items.map(i => i.kind);
    for (const k of ['imt-override', 'imt-node-fill', 'imt-partial-fill', 'imt-paint', 'not-placeable', 'outside-crop', 'crossing-crop-edge', 'width-loss']) expect(kinds).toContain(k);
    expect(pkg.report.items.find(i => i.kind === 'not-placeable' && /Untouchable/.test(i.message))).toBeTruthy();
    expect(pkg.rbVariants.map(v => v.imtOverrides ?? '').sort()).toEqual(['', '0=bus']);
  });

  it('writes package files with RB configs named by id', () => {
    const files = packageFiles(pkg);
    expect(files.map(f => f.path).filter(p => p.startsWith('roadbuilder/'))).toHaveLength(2);
    for (const f of files.filter(f => f.path.startsWith('roadbuilder/'))) {
      const cfg = JSON.parse(f.text);
      expect(f.path).toBe(`roadbuilder/${cfg.ID}.json`);
      expect(cfg.ID).toMatch(/^r[0-9a-f-]+-cs1conv$/);
    }
    const p = JSON.parse(files.find(f => f.path === 'cs1conv-plan.json')!.text);
    expect(p).toMatchObject({ format: 'cs1conv-plan', formatVersion: 1 });
    for (const s of p.segments) expect(p.prefabs[s.prefab]).toBeTruthy();
    const ids = new Set(p.nodes.map((n: { id: number }) => n.id));
    for (const s of p.segments) { expect(ids.has(s.start)).toBe(true); expect(ids.has(s.end)).toBe(true); }
  });

  it('zips and unzips the package unchanged (UI download path)', () => {
    const files = packageFiles(pkg);
    const back = unzipSync(zipSync(Object.fromEntries(files.map(f => [f.path, strToU8(f.text)]))));
    expect(Object.keys(back).sort()).toEqual(files.map(f => f.path).sort());
    for (const f of files) expect(strFromU8(back[f.path])).toBe(f.text);
  });

  it('auto crop picks the square holding the most road segments', () => {
    const a = autoCrop(c, 500);
    expect(a.roadSegments).toBe(3); // roads 1, 2 and 3; road 7 is 840 m long, road 8 is far away
    expect(inCrop(a, 0, 0) && inCrop(a, 60, 200)).toBe(true);
  });

  it('applies user overrides and summarises the mapping', () => {
    const o = buildPackage(c, { crop: { centerX: 100, centerZ: 200, size: 800 }, overrides: { [fiveLane.name]: { mode: 'vanilla', type: 'RoadPrefab', name: 'Large Road' }, 'Train Track': { mode: 'skip' } } });
    expect(o.plan.stats.byPrefabType).toEqual({ RoadPrefab: 3 });
    expect(o.plan.prefabs['v:Large Road']).toMatchObject({ type: 'RoadPrefab', source: fiveLane.name });
    expect(o.report.items.filter(i => i.kind === 'mapping-override')).toHaveLength(2);
    const st = mappingStats(pkg.mapping);
    expect(st).toMatchObject({ rb: { all: 1, inCrop: 1 }, vanilla: { all: 2, inCrop: 2 }, skippedNotPlaceable: { all: 1, inCrop: 1 } });
  });

  it('reports crop errors', () => {
    expect(buildPackage(c, { crop: { centerX: 0, centerZ: 0, size: 20000 } }).errors[0]).toMatch(/exceeds/);
  });
});
