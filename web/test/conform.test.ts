import { describe, expect, it } from 'vitest';
import { buildPackage } from '../src/core/plan.ts';
import { BASIC, HIGHWAY } from '../src/core/synthetic.ts';
import { adjustWaterSources, conformTerrain, samplePoints, wallsTerrain } from '../src/core/terrain/conform.ts';
import type { ConformInput } from '../src/core/terrain/conform.ts';
import type { NetInfo, PlanNode, PlanPrefab, PlanSegment } from '../src/core/types.ts';
import { city, lane, node, straight } from './fixtures.ts';

// a small grid: 64 × 64 cells of 3.5 m (224 m), flat at 100 m
const N = 64, SIZE = 224, CELL = SIZE / N;
const flat = (h = 100) => new Float32Array(N * N).fill(h);
const at = (g: Float32Array, x: number, z: number) => g[Math.floor((z + SIZE / 2) / CELL) * N + Math.floor((x + SIZE / 2) / CELL)];
const seg = (id: number, y0: number, y1: number, z: number, o: Partial<PlanSegment> = {}): PlanSegment => ({
  id, prefab: 'road', start: id * 10, end: id * 10 + 1, kind: 'ground', elevationStart: 0, elevationEnd: 0, halfWidth: 8,
  bezier: [[-100, y0, z], [-33, y0 + (y1 - y0) / 3, z], [33, y0 + (2 * (y1 - y0)) / 3, z], [100, y1, z]], ...o,
});
const prefabs = (mode: PlanPrefab['heightMode'] = 'conform'): Record<string, PlanPrefab> => ({ road: { type: 'RoadPrefab', name: 'Small Road', source: 'Basic Road', heightMode: mode } });
const input = (segments: PlanSegment[], o: Partial<ConformInput> = {}): ConformInput => ({ segments, nodes: [] as PlanNode[], prefabs: prefabs(), ...o });

describe('road-conformed terrain (schema §2, #110–113)', () => {
  it('samples the bezier by its 16-piece chord length', () => {
    expect(samplePoints([[0, 0, 0], [33, 0, 0], [67, 0, 0], [100, 0, 0]])).toHaveLength(101);
    expect(samplePoints([[0, 0, 0], [0, 0, 0], [0, 0, 0], [0, 0, 0]])).toHaveLength(2); // n ≥ 1
  });

  it('burns the road profile: exact core, smoothstep blend back to the natural terrain', () => {
    const g = flat();
    const s = conformTerrain(g, N, SIZE, input([seg(1, 105, 105, 0)]))!;
    expect(s).toMatchObject({ segments: 1, skipped: 0, blendFactor: 2, minBlend: 3.5, maxBlend: 40, before: { min: 100, max: 100 } });
    expect(at(g, 0, 1.75)).toBe(105); // core (|d| ≤ 8 m)
    expect(at(g, 0, -5.25)).toBe(105);
    // blend: Δ = 5 m → b = 10 m, from 8 to 18 m off the centre line, decreasing
    const blend = [8.75, 12.25, 15.75].map(z => at(g, 0, z));
    expect(blend[0]).toBeLessThan(105); expect(blend[2]).toBeGreaterThan(100);
    expect(blend[0]).toBeGreaterThan(blend[1]); expect(blend[1]).toBeGreaterThan(blend[2]);
    expect(at(g, 0, 19.25)).toBe(100); // beyond w + b
    expect(s.maxRaise).toBe(5);
  });

  it('follows the road\'s vertical profile along its length', () => {
    const g = flat();
    conformTerrain(g, N, SIZE, input([seg(1, 100, 110, 0)]));
    expect(at(g, -50, 0)).toBeGreaterThan(101); expect(at(g, -50, 0)).toBeLessThan(104);
    expect(at(g, 50, 0)).toBeGreaterThan(106); expect(at(g, 50, 0)).toBeLessThan(109);
  });

  it('leaves follow / walls prefabs and elevated, bridge, slope and tunnel segments alone (#124)', () => {
    for (const inp of [input([seg(1, 105, 105, 0)], { prefabs: prefabs('follow') }), input([seg(1, 105, 105, 0)], { prefabs: prefabs('walls') }),
      input([seg(1, 105, 105, 0, { kind: 'elevated' })]), input([seg(1, 105, 105, 0, { kind: 'bridge' })]), input([seg(1, 95, 95, 0, { kind: 'tunnel' })]),
      input([seg(1, 97, 97, 0, { kind: 'slope' })])]) { // a burned tunnel approach would leave no ground above the portal
      const g = flat();
      expect(conformTerrain(g, N, SIZE, inp)).toBeNull();
      expect(g.every(h => h === 100)).toBe(true);
    }
  });

  it('puts no junction disc where a slope or tunnel meets the node (#124)', () => {
    const ground = seg(1, 100, 100, 0, { end: 5 });
    const slope: PlanSegment = { ...seg(2, 100, 92, 0, { kind: 'slope' }), start: 5, end: 6, bezier: [[100, 100, 0], [100, 97, 33], [100, 95, 67], [100, 92, 100]] };
    const nodes = [{ id: 5, x: 100, z: 0, y: 100, heightAboveTerrain: 0, elevation: 0 }];
    const s = conformTerrain(flat(), N, SIZE, input([ground, slope], { nodes }))!;
    expect(s).toMatchObject({ segments: 1, nodes: 0 });
    expect(conformTerrain(flat(), N, SIZE, input([ground], { nodes }))!.nodes).toBe(1);
  });

  it('skips and counts a segment more than 30 m from the natural terrain', () => {
    const g = flat();
    const s = conformTerrain(g, N, SIZE, input([seg(1, 105, 105, 0), seg(2, 140, 140, 60)]))!;
    expect(s).toMatchObject({ segments: 1, skipped: 1, skippedIds: [2] });
    expect(at(g, 0, 60)).toBe(100);
  });

  it('puts a disc at the junction and lets the core win over a neighbour\'s blend', () => {
    const g = flat();
    const a = seg(1, 104, 104, 0, { end: 5 });
    const b: PlanSegment = { ...seg(2, 104, 104, 0), start: 5, end: 6, bezier: [[100, 104, 0], [100, 104, 33], [100, 104, 67], [100, 104, 100]] };
    const s = conformTerrain(g, N, SIZE, input([a, b], { nodes: [{ id: 5, x: 100, z: 0, y: 104, heightAboveTerrain: 0, elevation: 0 }] }))!;
    expect(s.nodes).toBe(1);
    expect(at(g, 103, -3)).toBe(104); // inside the disc (r = 8)
  });

  it('blends two roads at different heights continuously (w⁴ weighting), staying between them', () => {
    const g = flat();
    conformTerrain(g, N, SIZE, input([seg(1, 106, 106, -20), seg(2, 94, 94, 20)]));
    const col = Math.floor((0 + SIZE / 2) / CELL);
    let maxStep = 0;
    for (let row = Math.floor((-12 + SIZE / 2) / CELL); row < Math.floor((12 + SIZE / 2) / CELL); row++) {
      const h = g[row * N + col], h2 = g[(row + 1) * N + col];
      expect(h).toBeGreaterThanOrEqual(94); expect(h).toBeLessThanOrEqual(106);
      maxStep = Math.max(maxStep, Math.abs(h2 - h));
    }
    expect(maxStep).toBeLessThan(3); // no cliff where the two blend zones meet
    expect(Math.abs(at(g, 0, -0.1) + at(g, 0, 0.1) - 200)).toBeLessThan(0.01); // symmetric about the middle
  });

  it('keeps the blend at 3.5 m and below the water surface on wet pixels', () => {
    const g = flat(100);
    // sea at 101 m: every pixel is below it, so wet
    const s = conformTerrain(g, N, SIZE, input([seg(1, 106, 106, 0)], { water: { seaLevel: 101, dx: 0, dz: 0 } }))!;
    expect(at(g, 0, 1.75)).toBe(106); // cores are not capped
    expect(at(g, 0, 8.75)).toBeLessThanOrEqual(101); // blend capped at the surface
    expect(at(g, 0, 12.25)).toBe(100); // beyond w + 3.5
    expect(s.belowSeaLevelPixels).toBe(0);
  });

  it('keeps water sources at their absolute level', () => {
    const natural = flat(100), burned = flat(100);
    const i = Math.floor((10 + SIZE / 2) / CELL) * N + Math.floor((10 + SIZE / 2) / CELL);
    burned[i] = 102;
    const src = [{ x: 10, z: 10, height: 5 }, { x: -50, z: -50, height: 3 }];
    expect(adjustWaterSources(src, natural, burned, N, SIZE)).toBe(1);
    expect(src).toEqual([{ x: 10, z: 10, height: 3 }, { x: -50, z: -50, height: 3 }]);
  });
});

describe('walls burn (#129)', () => {
  const walls = (segments: PlanSegment[], o: Partial<ConformInput> = {}) => ({ segments, nodes: [] as PlanNode[], prefabs: prefabs('walls'), ...o });
  const run = (g: Float32Array, inp: ConformInput, cores = new Set<number>()) => wallsTerrain(g, N, SIZE, inp, g.slice(), cores);

  it('raises the ground to 7 m below a road high above it (embankment), blending back to the terrain', () => {
    const g = flat();
    const s = run(g, walls([seg(1, 125, 125, 0)]))!;
    expect(s).toMatchObject({ segments: 1, skipped: 0, maxRaise: 18 });
    expect(at(g, 0, 1.75)).toBe(118); // core (d ≤ w + 2): 125 − 7
    expect(at(g, 0, 8.75)).toBe(118);
    expect(at(g, 0, 15.75)).toBeLessThan(118); expect(at(g, 0, 15.75)).toBeGreaterThan(100);
    expect(at(g, 0, 70)).toBe(100);
  });

  it('lowers the ground to 11 m above a road deep below it (trench with walls, not a tunnel)', () => {
    const g = flat();
    run(g, walls([seg(1, 80, 80, 0)]));
    expect(at(g, 0, 1.75)).toBe(91);
  });

  it('changes nothing when the road is already within the wall range', () => {
    const g = flat();
    expect(run(g, walls([seg(1, 104, 104, 0)]))).toMatchObject({ segments: 1, pixelsChanged: 0 });
    expect(g.every(h => h === 100)).toBe(true);
  });

  it('skips a segment more than 30 m beyond the range, and only walls ground segments burn', () => {
    expect(run(flat(), walls([seg(1, 150, 150, 0)]))).toMatchObject({ segments: 0, skipped: 1, skippedIds: [1] });
    expect(run(flat(), walls([seg(1, 125, 125, 0, { kind: 'elevated' })]))).toBeNull();
    expect(run(flat(), input([seg(1, 125, 125, 0)]))).toBeNull(); // conform prefab
  });

  it('never touches conform core pixels', () => {
    const g = flat();
    const cores = new Set<number>([Math.floor((1.75 + SIZE / 2) / CELL) * N + Math.floor((0 + SIZE / 2) / CELL)]);
    run(g, walls([seg(1, 125, 125, 0)]), cores);
    expect(at(g, 0, 1.75)).toBe(100);
    expect(at(g, 0, -1.75)).toBe(118);
  });

  it('never raises a wet pixel above the water surface', () => {
    const g = flat();
    run(g, walls([seg(1, 125, 125, 0)], { water: { seaLevel: 101, dx: 0, dz: 0 } }));
    expect(at(g, 0, 1.75)).toBe(101);
  });
});

describe('plan height modes (#110–113)', () => {
  const PATH: NetInfo = { name: 'Gravel Path', ai: 'PedestrianPathAI', service: 'Road', parent: null, fields: { m_halfWidth: 2 }, lanes: [lane(0, 3, 'Pedestrian', 'None', 'Both')] };
  const c = city({
    netInfos: [BASIC, HIGHWAY, PATH],
    nodes: [node(1, 0, 0), node(2, 100, 0), node(3, 200, 0), node(4, 300, 0)],
    segments: [straight(1, 'Basic Road', 0, 100, 0, false, 1, 2), straight(2, 'Highway', 100, 200, 0, false, 2, 3), straight(3, 'Gravel Path', 200, 300, 0, false, 3, 4)],
  });
  const crop = { centerX: 150, centerZ: 0, size: 600 };
  const modeOf = (pkg: ReturnType<typeof buildPackage>, id: number) => { const s = pkg.plan.segments.find(x => x.id === id)!; return [s.prefab, pkg.plan.prefabs[s.prefab].heightMode]; };

  it('defaults: zoneable roads conform, highways walls (folded into the key), paths follow; halfWidth on every segment', () => {
    const pkg = buildPackage(c, { crop });
    expect(modeOf(pkg, 1)[1]).toBe('conform');
    expect(modeOf(pkg, 1)[0]).not.toMatch(/\|/);
    expect(modeOf(pkg, 2)[1]).toBe('walls');
    expect(modeOf(pkg, 2)[0]).toMatch(/\|walls$/);
    expect(modeOf(pkg, 3)).toEqual(['v:Pavement Path', 'follow']);
    expect(pkg.plan.segments.map(s => s.halfWidth)).toEqual([8, 11, 2]);
    expect(pkg.mapping.find(r => r.netInfo === 'Highway')).toMatchObject({ heightMode: 'walls', heightModeAuto: 'walls' });
  });

  it('per-type and global choices; every prefab key is used by a segment', () => {
    const pkg = buildPackage(c, { crop, heightModes: { Highway: 'conform', 'Basic Road': 'follow' } });
    expect(modeOf(pkg, 1)[1]).toBe('follow');
    expect(modeOf(pkg, 1)[0]).toMatch(/\|follow$/);
    expect(modeOf(pkg, 2)[1]).toBe('conform');
    expect(modeOf(pkg, 2)[0]).not.toMatch(/\|/);
    const all = buildPackage(c, { crop, heightMode: 'walls' });
    expect(all.plan.segments.map(s => all.plan.prefabs[s.prefab].heightMode)).toEqual(['walls', 'walls', 'walls']);
    const used = new Set(all.plan.segments.map(s => s.prefab));
    expect(Object.keys(all.plan.prefabs).every(k => used.has(k))).toBe(true);
  });
});
