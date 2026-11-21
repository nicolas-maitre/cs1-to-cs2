import { strToU8, zipSync } from 'fflate';
import crypto from 'node:crypto';
import { describe, expect, it } from 'vitest';
import { fromM0Dump, readCityFile, readCs1x } from '../src/core/cs1x.ts';
import type { NetInfo } from '../src/core/types.ts';
import { lane } from './fixtures.ts';

const ground: NetInfo = {
  name: 'Basic Road', ai: 'RoadAI', service: 'Road', fields: { m_halfWidth: 8, m_pavementWidth: 3 }, parent: null,
  variants: { elevated: 'Basic Road Elevated' }, lanes: [lane(-1.5, 3, 'Vehicle', 'Car', 'Backward'), lane(1.5, 3, 'Vehicle', 'Car', 'Forward')],
};
const elevated: NetInfo = { ...ground, name: 'Basic Road Elevated', ai: 'RoadBridgeAI', parent: 'Basic Road', variants: undefined };

function cs1x(extra: Record<string, Uint8Array> = {}, sections: Record<string, string> = {}) {
  const res = 3;
  const r16 = new Uint8Array(res * res * 2);
  const dv = new DataView(r16.buffer);
  for (let i = 0; i < res * res; i++) dv.setUint16(i * 2, 1000 + i, true);
  return zipSync({
    'manifest.json': strToU8('﻿' + JSON.stringify({
      format: 'cs1x', formatVersion: 1, city: { name: 'Skibidi', saveName: 'Skibidi.02' }, mapSize: 17280,
      sections: { network: 'ok', netinfos: 'ok', imt: 'ok', terrain: 'ok', tmpe: 'absent', ...sections },
    })),
    'network.json': strToU8(JSON.stringify({
      nodes: [{ id: 1, info: 'Basic Road', position: [0, 10, 0] }, { id: 2, info: 'Basic Road', position: [100, 10, 0] }],
      segments: [{ id: 5, info: 'Basic Road', startNode: 1, endNode: 2, bezier: [[0, 10, 0], [33, 10, 0], [66, 10, 0], [100, 10, 0]], length: 100, elevationKind: 'ground' }],
    })),
    'netinfos.json': strToU8(JSON.stringify({ netInfos: [ground, elevated] })),
    'imt.json': strToU8(JSON.stringify({ fillers: [] })),
    'terrain.json': strToU8(JSON.stringify({ resolution: res, size: 17280, heightScale: 0.015625, file: 'terrain.r16', min: 15.6, max: 15.75 })),
    'terrain.r16': r16,
    'report.json': strToU8(JSON.stringify({ items: [{ level: 'info', section: 'imt', message: 'IMT assembly: IntersectionMarkingTool 1.15.0.0' }] })),
    ...extra,
  });
}

describe('.cs1x reader', () => {
  it('reads every v1 section, strips a BOM and decodes the little-endian heightmap', async () => {
    const bytes = cs1x();
    const c = await readCityFile(bytes);
    expect(c.source).toBe('cs1x');
    expect(c.manifest.city?.saveName).toBe('Skibidi.02');
    expect(c.nodes).toHaveLength(2);
    expect(c.segments[0].id).toBe(5);
    expect(c.netInfos.map(i => i.name)).toEqual(['Basic Road', 'Basic Road Elevated']);
    expect(c.netInfos[1].parent).toBe('Basic Road');
    expect(c.terrain?.resolution).toBe(3);
    expect([...c.terrain!.heights]).toEqual([1000, 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008]);
    expect(c.report[0].message).toMatch(/IntersectionMarkingTool/);
    expect(c.sha256).toBe(crypto.createHash('sha256').update(bytes).digest('hex'));
  });

  it('handles missing and failed sections without throwing', () => {
    const z = zipSync({
      'manifest.json': strToU8(JSON.stringify({ format: 'cs1x', formatVersion: 1, sections: { network: 'ok', netinfos: 'ok', imt: 'failed', terrain: 'absent' } })),
      'network.json': strToU8(JSON.stringify({ nodes: [], segments: [] })),
      'netinfos.json': strToU8('{ broken'),
    });
    const c = readCs1x(z);
    expect(c.netInfos).toEqual([]);
    expect(c.imtFillers).toEqual([]);
    expect(c.terrain).toBeNull();
    expect(c.report.some(r => /netinfos\.json unreadable/.test(r.message))).toBe(true);
    expect(c.report.some(r => /imt\.json missing \(section imt: failed\)/.test(r.message))).toBe(true);
    expect(c.report.some(r => /terrain/.test(r.message))).toBe(false); // absent → silent
  });

  it('rejects zips without a cs1x manifest', () => {
    expect(() => readCs1x(zipSync({ 'a.txt': strToU8('x') }))).toThrow(/manifest/);
  });

  it('adapts the M0 dump and derives parents, preferring the name-matching ground road', () => {
    const tree = { ...ground, name: 'Basic Road Decoration Trees', variants: undefined, aiFields: { m_elevatedInfo: 'Basic Road Elevated' } };
    const basic = { ...ground, variants: undefined, parent: undefined, aiFields: { m_elevatedInfo: 'Basic Road Elevated', m_bridgeInfo: 'Basic Road Bridge' } };
    const elev = { ...elevated, parent: undefined, aiFields: {} };
    const bridge = { ...elevated, name: 'Basic Road Bridge', parent: undefined, aiFields: { m_elevatedInfo: 'Basic Road Elevated' } };
    const c = fromM0Dump({ nodes: [], segments: [], netInfos: [tree, basic, elev, bridge] } as never);
    const by = new Map(c.netInfos.map(i => [i.name, i]));
    expect(by.get('Basic Road Elevated')!.parent).toBe('Basic Road');
    expect(by.get('Basic Road Bridge')!.parent).toBe('Basic Road');
    expect(by.get('Basic Road')!.parent).toBeNull();
    expect(by.get('Basic Road')!.variants).toEqual({ elevated: 'Basic Road Elevated', bridge: 'Basic Road Bridge' });
    expect(c.source).toBe('m0-dump');
    expect(c.terrain).toBeNull();
  });

  it('reads JSON (M0 dump) through readCityFile', async () => {
    const c = await readCityFile(strToU8(JSON.stringify({ cityName: 'X', nodes: [], segments: [], netInfos: [] })), { hash: false });
    expect(c.source).toBe('m0-dump');
    expect(c.manifest.city?.name).toBe('X');
  });
});
