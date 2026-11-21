import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { sha1Hex } from '../src/core/hash.ts';
import { fromM0Dump, parseJson } from '../src/core/cs1x.ts';
import { analyse, convert, rbConfigId } from '../src/core/rb.ts';
import type { NetInfo } from '../src/core/types.ts';
import { fiveLane, lane } from './fixtures.ts';

const repo = path.resolve(import.meta.dirname, '..', '..');
const SPIKE = path.join(repo, 'spikes', 'm0-roadbuilder-configs', 'wq-to-rb.mjs');
const DUMP = path.join(os.homedir(), 'AppData', 'Local', 'Colossal Order', 'Cities_Skylines', 'M0NetworkDump', 'network-20261120-170444.json');

describe('sha1', () => {
  it('matches node:crypto', () => {
    for (const s of ['', 'abc', 'cs1conv:Basic Road', 'x'.repeat(1000), 'ünïcødé →']) {
      expect(sha1Hex(s)).toBe(crypto.createHash('sha1').update(s).digest('hex'));
    }
  });
  it('builds the spike id shape', () => {
    expect(rbConfigId('2225246713.Five-Lane One-Way Road_Data', '0=bus')).toBe('r4a322dd1-b9dc-5b7e-8780-1d3f18c0d723-cs1conv');
  });
});

describe('RB generator', () => {
  it('two-way road with parking sidewalks and a tree median', () => {
    const info: NetInfo = {
      name: 'Tree Avenue', ai: 'RoadAI', service: 'Road', fields: { m_halfWidth: 12, m_pavementWidth: 3 }, aiFields: { m_trafficLights: true },
      lanes: [lane(-10.5, 3, 'Pedestrian', 'None', 'Both'), lane(10.5, 3, 'Pedestrian', 'None', 'Both'),
        lane(-8, 2, 'Parking', 'Car', 'Backward'), lane(8, 2, 'Parking', 'Car', 'Forward'),
        lane(-5.5, 3, 'Vehicle', 'Car', 'Backward'), lane(5.5, 3, 'Vehicle', 'Car', 'Forward'),
        lane(0, 0, 'None', 'None', 'None', ['Tree A'])],
    };
    const { config, report } = convert(info)!;
    expect(report.rbLanes.map(l => l.split(' ')[0])).toEqual(['Sidewalk←', 'Car←', 'Median→', 'Car→', 'Sidewalk→']);
    expect(config.Lanes[0].GroupOptions).toEqual({ Parking: 'P', Width: '5m', 'Parking Angle': '' });
    expect(config.Lanes[1].GroupOptions['Transport Option']).toBe('Transport');
    expect(config.Lanes[2].GroupOptions['Median Width']).toBe('5m');
    expect(config.Addons).toContain('TreesCenter');
    expect(config.Addons).toContain('GeneratesZoningBlocks');
    expect(config.Category).toBe('Road');
    expect(config.ID).toBe(config.OriginalID);
    expect(config.SpeedLimit).toBe(100);
  });

  it('highway: shoulders, Highway category, no zoning', () => {
    const info: NetInfo = {
      name: 'Highway', ai: 'RoadAI', service: 'Road', fields: { m_halfWidth: 11, m_pavementWidth: 2 }, aiFields: { m_highwayRules: true, m_enableZoning: false },
      lanes: [-4, 0, 4].map(p => ({ ...lane(p, 4, 'Vehicle', 'Car', 'Forward'), fields: { ...lane(p, 4, 'Vehicle', 'Car', 'Forward').fields, m_speedLimit: 2 } })),
    };
    const { config, report } = convert(info)!;
    expect(config.Category).toBe('Highway');
    expect(config.Lanes[0].GroupPrefabName).toBe('RoadBuilder.LaneGroups.ShoulderGroupPrefab');
    expect(config.Lanes.at(-1)!.GroupOptions).toEqual({ 'Ground Type': 'Asphalt', 'Lane Width': '2m' });
    expect(config.Addons).not.toContain('GeneratesZoningBlocks');
    expect(config.Addons).toContain('RequiresUpgradeForElectricity');
    expect(config.SpeedLimit).toBe(200);
    expect(report.rbWidth).toBe(16);
  });

  it('no drive lane → null', () => {
    expect(convert({ name: 'Path', fields: { m_halfWidth: 2, m_pavementWidth: 2 }, lanes: [lane(0, 2, 'Pedestrian', 'None', 'Both')] })).toBeNull();
  });

  it('IMT overrides change the id deterministically', () => {
    const a = convert(fiveLane)!.config.ID, b = convert(fiveLane, new Map([[0, 'bus']]))!.config.ID;
    expect(a).not.toBe(b);
    expect(convert(fiveLane, new Map([[0, 'bus']]))!.config.ID).toBe(b);
    expect(analyse(fiveLane, new Map([[0, 'bus']])).slots.find(s => s.pos === 0)!.kind).toBe('bus');
  });
});

// Byte-level parity with the M0 spike generator on every road NetInfo of the real dump.
describe.skipIf(!fs.existsSync(DUMP))('parity with spikes/m0-roadbuilder-configs/wq-to-rb.mjs (M0 dump)', () => {
  it('produces the same configs as the spike for all non-highway roads', () => {
    const dump = parseJson<{ netInfos: NetInfo[] }>(fs.readFileSync(DUMP, 'utf8'));
    const roads = dump.netInfos.filter(i => i.service === 'Road' && /RoadAI$/.test(i.ai ?? '') && !(i.aiFields?.m_highwayRules === true && !i.lanes.some(l => /Pedestrian/.test(l.fields.m_laneType))));
    const withLanes = roads.filter(i => analyse(i).slots.length > 0);
    expect(withLanes.length).toBeGreaterThanOrEqual(40);
    const out = fs.mkdtempSync(path.join(os.tmpdir(), 'cs1conv-parity-'));
    try {
      execFileSync(process.execPath, [SPIKE, DUMP, '--out', out, ...withLanes.map(i => i.name)], { encoding: 'utf8', maxBuffer: 64 << 20 });
      for (const info of withLanes) {
        const { config } = convert(info)!;
        const spike = JSON.parse(fs.readFileSync(path.join(out, `${config.ID}.json`), 'utf8'));
        expect(config, info.name).toEqual(spike);
      }
      // IMT variants: same files as `wq-to-rb.mjs --out-imt`.
      const imtOut = path.join(out, 'imt');
      const reports = JSON.parse(execFileSync(process.execPath, [SPIKE, DUMP, '--out-imt', imtOut], { encoding: 'utf8', maxBuffer: 64 << 20 }));
      expect(reports.length).toBeGreaterThan(3);
      const city = fromM0Dump(parseJson(fs.readFileSync(DUMP, 'utf8')));
      const infos = new Map(city.netInfos.map(i => [i.name, i]));
      for (const r of reports) {
        const ov = new Map<number, 'bus' | 'median' | 'median-grass'>(r.imtOverrides.split(',').map((kv: string) => { const [p, k] = kv.split('='); return [Number(p), k]; }));
        const { config } = convert(infos.get(r.cs1)!, ov)!;
        expect(config.ID).toBe(r.id);
        expect(config).toEqual(JSON.parse(fs.readFileSync(path.join(imtOut, `${r.id}.json`), 'utf8')));
      }
    } finally {
      fs.rmSync(out, { recursive: true, force: true });
    }
  }, 60_000);
});
