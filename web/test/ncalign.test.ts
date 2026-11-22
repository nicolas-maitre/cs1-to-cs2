import { describe, expect, it } from 'vitest';
import { alignNcShifts, applyNodeControllerOffsets, applyNodeControllerShifts, endShifts, lengthXZ, OFFSET_CAP, shiftBezier, stretchedOffsets } from '../src/core/m2/ncalign.ts';
import type { PlanContext } from '../src/core/m2/context.ts';
import type { Bezier, PlanSegment } from '../src/core/types.ts';

const straightX: Bezier = [[0, 10, 0], [30, 10, 0], [70, 10, 0], [100, 10, 0]];
const seg = (id: number, start: number, end: number, bezier: Bezier = straightX): PlanSegment =>
  ({ id, prefab: 'rb:x', start, end, bezier: bezier.map(p => [...p]) as Bezier, elevationStart: 0, elevationEnd: 0, kind: 'ground' });

describe('shiftBezier (NC Renewal GetSegmentPosAndDir)', () => {
  it('moves each end to the left of the direction from its node into the segment', () => {
    // start dir +x → left = +z; end dir −x → left = −z
    const b = shiftBezier(straightX, 2, 0)!.bezier;
    expect(b[0]).toEqual([0, 10, 2]);
    expect(b[3]).toEqual([100, 10, 0]);
    const e = shiftBezier(straightX, 0, 2)!.bezier;
    expect(e[0]).toEqual([0, 10, 0]);
    expect(e[3]).toEqual([100, 10, -2]);
  });

  it('a uniform shift (s0 = −s1) moves the whole road sideways and keeps it straight', () => {
    const b = shiftBezier(straightX, 1.775, -1.775)!.bezier;
    for (const p of b) expect(p[2]).toBeCloseTo(1.775, 3);
    expect(b.map(p => p[0])).toEqual([0, 30, 70, 100]);
    expect(b.map(p => p[1])).toEqual([10, 10, 10, 10]);
  });

  it('turns both end directions by asin((s0 + s1) / L) so a diagonal shift stays straight', () => {
    const b = shiftBezier(straightX, 1, 1)!.bezier;
    // the ends go from z = +1 to z = −1: the handles point along the chord
    const chord = Math.atan2(b[3][2] - b[0][2], b[3][0] - b[0][0]);
    expect(Math.atan2(b[1][2] - b[0][2], b[1][0] - b[0][0])).toBeCloseTo(chord, 4);
    expect(Math.atan2(b[3][2] - b[2][2], b[3][0] - b[2][0])).toBeCloseTo(chord, 4);
  });

  it('clamps the direction fix on a short, strongly shifted segment', () => {
    const short: Bezier = [[0, 0, 0], [0.6, 0, 0], [1.4, 0, 0], [2, 0, 0]];
    const r = shiftBezier(short, 3, 12)!;
    expect(r.clamped).toBe(true);
    for (const p of r.bezier) for (const v of p) expect(Number.isFinite(v)).toBe(true);
  });
});

describe('applyNodeControllerShifts', () => {
  const nc = {
    nodes: [
      { id: 1, type: 'Custom', segmentEnds: [{ segment: 10, shift: 1.775 }, { segment: 11, shift: 0 }] },
      { id: 2, type: 'Middle', segmentEnds: [{ segment: 10, shift: -1.775 }] },
      { id: 3, type: 'Custom', segmentEnds: [{ segment: 12, shift: 2, mode: 'FreeForm' }, { segment: 13, shift: 0.01 }] },
    ],
  };

  it('collects only real shifts', () => {
    expect([...endShifts(nc).keys()].sort()).toEqual(['1:10', '2:10', '3:12']);
  });

  it('shifts the plan curve by node identity, also for a reversed segment, and marks it offNode', () => {
    const fwd = seg(10, 1, 2);
    const rev = seg(10, 2, 1); // the plan swapped start/end (CS1 Invert): the shifts follow the nodes
    const plain = seg(11, 1, 4);
    applyNodeControllerShifts(nc, [fwd]);
    const stats = applyNodeControllerShifts(nc, [rev, plain]);
    expect(fwd).toMatchObject({ offNode: true, ncShift: [1.775, -1.775] });
    expect(fwd.bezier[0][2]).toBeCloseTo(1.775);
    expect(rev).toMatchObject({ offNode: true, ncShift: [-1.775, 1.775] });
    expect(rev.bezier[0][2]).toBeCloseTo(-1.775); // rev starts at node 2 (x=0) heading +x: left is +z, shift −1.775
    expect(plain.offNode).toBeUndefined();
    expect(stats).toMatchObject({ segments: 1, ends: 2, freeForm: 0, maxShift: 1.775 });
  });

  it('leaves FreeForm ends and tiny shifts alone', () => {
    const s12 = seg(12, 3, 5), s13 = seg(13, 3, 6);
    const stats = applyNodeControllerShifts(nc, [s12, s13]);
    expect(s12.offNode).toBeUndefined();
    expect(s13.offNode).toBeUndefined();
    expect(stats).toMatchObject({ segments: 0, freeForm: 1 });
  });

  it('does nothing without Node Controller data', () => {
    const s = seg(10, 1, 2);
    expect(applyNodeControllerShifts(null, [s]).segments).toBe(0);
    expect(s.bezier).toEqual(straightX);
  });
});

describe('alignNcShifts (buildPackage entry)', () => {
  it('reports the shifted ends and the free-form ones', () => {
    const nc = { nodes: [
      { id: 1, type: 'Custom', segmentEnds: [{ segment: 10, shift: 2.225 }, { segment: 11, shift: 1, mode: 'FreeForm' }] },
    ] };
    const ctx = { city: { nodeController: nc } } as unknown as PlanContext;
    const segs = [seg(10, 1, 2), seg(11, 3, 1)];
    const r = alignNcShifts(segs, ctx);
    expect(r).toMatchObject({ segments: 1, ends: 1, freeForm: 1, maxShift: 2.225 });
    expect(r.items.map(i => [i.kind, i.count])).toEqual([['node-controller', 1], ['node-controller', 1]]);
    expect(r.items[0].cs1).toEqual({ segments: [10] });
    expect(segs[0].bezier.flat().every(v => Math.round(v * 1000) / 1000 === v)).toBe(true);
  });
});

describe('Node Controller offsets at junctions (#157)', () => {
  // node 1 at the origin with three roads (a junction), node 9 at x = 300 with two (a 2-road node)
  const junction = () => [seg(10, 1, 2), { ...seg(11, 1, 3), bezier: [[0, 10, 0], [0, 10, -30], [0, 10, -70], [0, 10, -100]] as Bezier }, { ...seg(12, 4, 1), bezier: [[-100, 10, 0], [-70, 10, 0], [-30, 10, 0], [0, 10, 0]] as Bezier }];
  const nc = (ends: { segment: number; offset: number; minOffset?: number; mode?: string }[], type = 'Custom', id = 1) => ({ nodes: [{ id, type, segmentEnds: ends }] });

  it('counts only offsets more than 5 m above NC\'s default for the node type (clamped to minOffset)', () => {
    const m = stretchedOffsets({ nodes: [
      { id: 1, type: 'Custom', segmentEnds: [{ segment: 10, offset: 13.5 }, { segment: 11, offset: 12.9 }, { segment: 12, offset: 40, minOffset: 33 }, { segment: 13, offset: 60, mode: 'FreeForm' }] },
      { id: 2, type: 'Crossing', segmentEnds: [{ segment: 10, offset: 7.5 }] },
    ] });
    expect([...m.keys()].sort()).toEqual(['1:10', '1:12', '2:10']); // 13.5 > 8+5; 12.9 not; 40 > 33+5; FreeForm never; 7.5 > 2+5
  });

  it('cuts the curve back along itself at a junction, off the node, and records the cut', () => {
    const segs = junction();
    const stats = applyNodeControllerOffsets(nc([{ segment: 10, offset: 30 }, { segment: 11, offset: 8 }, { segment: 12, offset: 20 }]), segs);
    expect(stats).toMatchObject({ junctions: 1, ends: 2, capped: 0 });
    expect(segs[0]).toMatchObject({ offNode: true, ncOffset: [30, 0] });
    expect(segs[0].bezier[0][0]).toBeCloseTo(30, 0); // pulled back 30 m along the road
    expect(segs[0].bezier[3]).toEqual([100, 10, 0]); // the far end untouched
    expect(segs[2].ncOffset).toEqual([0, 20]);
    expect(segs[2].bezier[3][0]).toBeCloseTo(-20, 0);
    expect(segs[1].offNode).toBeUndefined(); // 8 m = NC's default: not stretched
  });

  it('keeps the cut on the original curve', () => {
    const curved: Bezier = [[0, 0, 0], [40, 0, 0], [80, 0, 20], [100, 0, 60]];
    const segs = [{ ...seg(10, 1, 2), bezier: curved }, seg(11, 1, 3), seg(12, 4, 1)];
    applyNodeControllerOffsets(nc([{ segment: 10, offset: 25 }]), segs);
    const p = segs[0].bezier[0];
    // the new start is a point of the old curve about 25 m along it
    let best = Infinity;
    for (let i = 0; i <= 1000; i++) { const t = i / 1000, u = 1 - t; const q = [0, 2].map(k => u * u * u * curved[0][k] + 3 * u * u * t * curved[1][k] + 3 * u * t * t * curved[2][k] + t * t * t * curved[3][k]); best = Math.min(best, Math.hypot(q[0] - p[0], q[1] - p[2])); }
    expect(best).toBeLessThan(0.05);
    expect(Math.hypot(p[0], p[2])).toBeGreaterThan(20);
    expect(lengthXZ(segs[0].bezier)).toBeCloseTo(lengthXZ(curved) - 25, 0);
  });

  it('caps a cut at 45 % of a short segment', () => {
    const segs = [{ ...seg(10, 1, 2), bezier: [[0, 0, 0], [10, 0, 0], [20, 0, 0], [30, 0, 0]] as Bezier }, seg(11, 1, 3), seg(12, 4, 1)];
    const stats = applyNodeControllerOffsets(nc([{ segment: 10, offset: 60 }]), segs);
    expect(stats.capped).toBe(1);
    expect(segs[0].ncOffset![0]).toBeCloseTo(OFFSET_CAP * 30, 3);
  });

  it('leaves 2-road nodes alone', () => {
    const segs = [seg(10, 9, 2), seg(11, 5, 9)];
    const before = JSON.stringify(segs);
    const stats = applyNodeControllerOffsets(nc([{ segment: 10, offset: 40 }, { segment: 11, offset: 40 }], 'Custom', 9), segs);
    expect(stats.ends).toBe(0);
    expect(JSON.stringify(segs)).toBe(before);
  });

  it('is on by default in alignNcShifts and can be turned off', () => {
    const ctx = { city: { nodeController: nc([{ segment: 10, offset: 30 }]) } } as unknown as PlanContext;
    const on = junction(), off = junction();
    const r = alignNcShifts(on, ctx);
    expect(r.offsets).toMatchObject({ junctions: 1, ends: 1 });
    expect(r.items.some(i => /stretched as Node Controller had them/.test(i.message))).toBe(true);
    expect(alignNcShifts(off, ctx, { offsets: false }).offsets).toBeNull();
    expect(off[0].offNode).toBeUndefined();
  });
});
