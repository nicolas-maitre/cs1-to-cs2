import { describe, expect, it } from 'vitest';
import { buildPackage } from '../src/core/plan.ts';
import { BASIC } from '../src/core/synthetic.ts';
import type { NetInfo } from '../src/core/types.ts';
import { city, node, straight } from './fixtures.ts';

const PIPE: NetInfo = { name: 'Water Pipe', ai: 'WaterPipeAI', service: 'Water', parent: null, fields: { m_halfWidth: 4.5 }, lanes: [] };
const POWER: NetInfo = { name: 'Power Line', ai: 'PowerLineAI', service: 'Electricity', parent: null, fields: { m_halfWidth: 6.2 }, lanes: [] };

describe('plan.utilities: the player\'s power lines and pipes away from the roads (#115)', () => {
  const c = city({
    netInfos: [BASIC, PIPE, POWER],
    nodes: [node(1, 0, 0), node(2, 200, 0), node(3, 0, 3), node(4, 200, 3), node(5, 0, 100), node(6, 200, 100), node(7, 0, -100), node(8, 200, -100), node(9, 0, 60), node(10, 200, 60), node(11, 5000, 0)],
    segments: [
      straight(1, 'Basic Road', 0, 200, 0, false, 1, 2),
      { ...straight(2, 'Water Pipe', 0, 200, 3, false, 3, 4), ai: 'WaterPipeAI' }, // 3 m beside the road: along it
      { ...straight(3, 'Water Pipe', 0, 200, 100, false, 5, 6), ai: 'WaterPipeAI' }, // away
      { ...straight(4, 'Power Line', 0, 200, -100, false, 7, 8), ai: 'PowerLineAI' }, // away
      { ...straight(5, 'Water Pipe', 0, 200, 60, false, 9, 10), ai: 'WaterPipeAI', flags: 'Created, Untouchable' }, // part of a building
      { ...straight(6, 'Power Line', 200, 5000, 0, false, 2, 11), ai: 'PowerLineAI' }, // leaves the crop
    ],
  });
  const crop = { centerX: 100, centerZ: 0, size: 600 };

  it('keeps off-road segments in plan coordinates with their CS1 node ids, counts the rest', () => {
    const pkg = buildPackage(c, { crop });
    const u = pkg.plan.utilities!;
    expect(u.formatVersion).toBe(1);
    expect(u.segments.map(s => [s.id, s.utility, s.start, s.end, s.kind])).toEqual([[3, 'pipe', 5, 6, 'ground'], [4, 'power', 7, 8, 'ground']]);
    expect(u.stats).toMatchObject({ power: 1, pipe: 1, alongRoad: 1, alongRoadPipe: 1, buildingNet: 1, crossingCrop: 1 });
    // plan coordinates: CS1 + transform
    expect(u.segments[0].bezier[0]).toEqual([0 + pkg.plan.transform.dx, 0, 100 + pkg.plan.transform.dz]);
    expect(pkg.report.items.some(i => i.kind === 'utilities' && /1 power line and 1 pipe segment\(s\) away from the roads/.test(i.message))).toBe(true);
  });

  it('can be turned off', () => {
    expect(buildPackage(c, { crop, utilities: false }).plan.utilities).toBeUndefined();
  });
});
