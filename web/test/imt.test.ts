// Port of spikes/m0-roadbuilder-configs/imt-lanes.test.mjs (the Skibidi.02 dump has no
// segment-level pavement/grass islands, so the median rule can only be exercised on synthetic data).
import { describe, expect, it } from 'vitest';
import { classifyFiller, laneCoverage, overrideSignature, wholeLaneOverrides } from '../src/core/imt.ts';
import type { SlotLike } from '../src/core/imt.ts';
import { analyse, convert } from '../src/core/rb.ts';
import type { ImtFiller, NetInfo } from '../src/core/types.ts';
import { fiveLane as info, fill, laneZ, rect, straight } from './fixtures.ts';

// Segment 1 runs +X at z = 0; segment 2 is the same road drawn the other way with Invert set,
// at z = 100. In both, NetInfo position p lies at world z = base − p.
const dumpWith = (fills: ImtFiller[]) => ({
  netInfos: [info], segments: [straight(1, info.name, 0, 60, 0, false), straight(2, info.name, 60, 0, 100, true)], imtFillers: fills,
});
const slotsOf = (i: NetInfo): SlotLike[] => i.lanes.map(l => l.fields).filter(f => /Vehicle|Parking/.test(f.m_laneType))
  .map(f => ({ pos: f.m_position, width: f.m_width, kind: /Parking/.test(f.m_laneType) ? 'parking' : 'car' }));
const overrides = (fills: ImtFiller[]) => wholeLaneOverrides(laneCoverage(dumpWith(fills), slotsOf));

describe('IMT fill → lane overrides', () => {
  it('pavement island over a lane → median; grass on an inverted segment → median-grass', () => {
    const [a, b] = laneZ(0, 3, 3), [c, d] = laneZ(100, 3, 3);
    const ov = overrides([
      fill('Segment', 1, 'FillerPavement', [136, 136, 136, 224], rect(12, 48, a, b)),
      fill('Segment', 2, 'FillerGrass', [80, 120, 60, 255], rect(12, 48, c, d)),
    ]);
    expect([...ov.get(1)!]).toEqual([[3, 'median']]);
    expect([...ov.get(2)!]).toEqual([[3, 'median-grass']]);
  });

  it('red paint over parking +8.5 and car +6 → both covered', () => {
    const ov = overrides([fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, -9.2, -4.8))]);
    expect([...ov.get(1)!].sort()).toEqual([[6, 'bus'], [8.5, 'bus']]);
  });

  it('partial / diagonal / foreign / node / grey fills → no override', () => {
    const tri: [number, number, number][] = [[10, 0, -1.2], [10, 0, 1.2], [50, 0, 1.2]];
    const ov = overrides([
      fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], tri),
      fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, -6.0, -4.6)),
      fill('Segment', 2, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, ...laneZ(0, 0, 3))),
      fill('Node', 7, 'FillerSolid', [136, 0, 0, 94], rect(-5, 60, ...laneZ(0, -6, 3))),
      fill('Segment', 1, 'FillerSolid', [136, 136, 136, 224], rect(10, 50, ...laneZ(0, -3, 3))),
    ]);
    expect(ov.size).toBe(0);
  });

  it('end to end through the generator: median + merged bus lane in the RB config', () => {
    const [a, b] = laneZ(0, -3, 3);
    const ov = wholeLaneOverrides(laneCoverage(dumpWith([
      fill('Segment', 1, 'FillerPavement', [136, 136, 136, 224], rect(12, 48, a, b)),
      fill('Segment', 1, 'FillerSolid', [136, 0, 0, 94], rect(10, 50, -9.2, -4.8)),
    ]), i => analyse(i).slots));
    const res = convert(info, ov.get(1)!)!;
    const lanes = res.report.rbLanes.map(l => l.split(' ')[0]);
    expect(lanes).toEqual(['Sidewalk←', 'Car→', 'Median→', 'Car→', 'Car→', 'Bus→', 'Sidewalk→']);
    expect(res.report.rbLanes.at(-1)).toMatch(/"Parking":""/); // right parking absorbed by the bus paint
    expect(res.report.rbLanes[0]).toMatch(/"Parking":"P"/);    // left parking untouched
    expect(res.config.Name).toBe('[CS1] Test Five-Lane (IMT median+bus)');
    expect(res.report.imtOverrides).toBe('-3=median,6=bus,8.5=bus');
  });

  it('classifies fills and signs override sets like the spike', () => {
    expect(classifyFiller({ styleClass: 'SolidFillerStyle', styleType: 'FillerSolid', color: [136, 0, 0, 94] })).toBe('bus');
    expect(classifyFiller({ styleClass: 'SolidFillerStyle', styleType: 'FillerSolid', color: [136, 136, 136, 224] })).toBe('paint');
    expect(classifyFiller({ styleClass: 'PavementFillerStyle', styleType: 'FillerPavement', color: [1, 2, 3, 4] })).toBe('median');
    expect(classifyFiller({ styleClass: 'GrassFillerStyle', styleType: 'FillerGrass', color: [1, 2, 3, 4] })).toBe('median-grass');
    expect(overrideSignature(new Map([[8.5, 'bus'], [-3, 'median']]))).toBe('-3=median,8.5=bus');
    expect(overrideSignature(null)).toBe('');
  });
});

