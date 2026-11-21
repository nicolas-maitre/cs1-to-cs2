import { strToU8, zipSync } from 'fflate';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterAll, describe, expect, it } from 'vitest';
import { installRb } from '../cli/cs1conv.ts';
import { fiveLane, straight } from './fixtures.ts';

const CLI = path.resolve(import.meta.dirname, '..', 'cli', 'cs1conv.ts');
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'cs1conv-cli-'));
afterAll(() => fs.rmSync(tmp, { recursive: true, force: true }));

const file = path.join(tmp, 'mini.cs1x');
fs.writeFileSync(file, zipSync({
  'manifest.json': strToU8(JSON.stringify({ format: 'cs1x', formatVersion: 1, city: { name: 'Mini', saveName: 'Mini.01' }, sections: { network: 'ok', netinfos: 'ok', imt: 'absent', terrain: 'absent' } })),
  'network.json': strToU8(JSON.stringify({
    nodes: [{ id: 10, info: fiveLane.name, position: [0, 0, 0] }, { id: 11, info: fiveLane.name, position: [60, 0, 0] }],
    segments: [straight(1, fiveLane.name, 0, 60, 0, false, 10, 11)],
  })),
  'netinfos.json': strToU8(JSON.stringify({ netInfos: [fiveLane] })),
}));
const run = (...args: string[]) => execFileSync(process.execPath, [CLI, ...args], { encoding: 'utf8' });

describe('cs1conv CLI', () => {
  it('inspect prints a summary (and JSON)', () => {
    expect(run('inspect', file)).toMatch(/Mini \(Mini\.01\) – cs1x/);
    const j = JSON.parse(run('inspect', file, '--json'));
    expect(j.counts).toEqual({ nodes: 2, segments: 1, netInfos: 1, imtFillers: 0 });
    expect(j.mapping.rb.all).toBe(1);
  });

  it('plan writes a valid package', () => {
    const out = path.join(tmp, 'pkg');
    expect(run('plan', file, '--crop', '30,0,1000', '--out', out)).toMatch(/1 segments, 2 nodes, 1 RB configs/);
    const plan = JSON.parse(fs.readFileSync(path.join(out, 'cs1conv-plan.json'), 'utf8'));
    expect(plan.segments).toHaveLength(1);
    expect(plan.transform).toMatchObject({ dx: -30, dz: 0 });
    const rb = fs.readdirSync(path.join(out, 'roadbuilder'));
    expect(rb).toHaveLength(1);
    expect(rb[0]).toMatch(/-cs1conv\.json$/);
    expect(JSON.parse(fs.readFileSync(path.join(out, 'report.json'), 'utf8')).format).toBe('cs1conv-report');
  });

  it('install-rb from the command line: keeps by default, --prune removes, --keep-other-rb is a harmless alias', () => {
    const out = path.join(tmp, 'pkg');
    const target = path.join(tmp, 'RB-cli');
    fs.mkdirSync(target, { recursive: true });
    const run = (...flags: string[]) => execFileSync(process.execPath, [CLI, 'install-rb', out, ...flags], { encoding: 'utf8', env: { ...process.env, CS1CONV_RB_FOLDER: target } });
    fs.writeFileSync(path.join(target, 'rother-cs1conv.json'), '{}');
    expect(run()).toMatch(/kept 1 cs1conv config/);
    expect(fs.existsSync(path.join(target, 'rother-cs1conv.json'))).toBe(true);
    run('--keep-other-rb');
    expect(fs.existsSync(path.join(target, 'rother-cs1conv.json'))).toBe(true);
    expect(run('--prune')).toMatch(/removed 1 other cs1conv config/);
    expect(fs.existsSync(path.join(target, 'rother-cs1conv.json'))).toBe(false);
  });

  it('rejects an oversized crop', () => {
    expect(() => execFileSync(process.execPath, [CLI, 'plan', file, '--crop', '0,0,20000', '--out', path.join(tmp, 'x')], { stdio: 'pipe' })).toThrow();
  });

  it('install-rb keeps other cities\' configs by default, removes them only with prune, and never touches other files', () => {
    const out = path.join(tmp, 'pkg');
    const target = path.join(tmp, 'RoadBuilder');
    fs.mkdirSync(target);
    fs.writeFileSync(path.join(target, 'r1234-76561198000000000.json'), '{"user":"config"}');
    fs.writeFileSync(path.join(target, 'rstale-cs1conv.json'), '{}');
    fs.writeFileSync(path.join(target, 'notes.txt'), 'keep');
    // default: another package's cs1conv config stays
    const r = installRb(out, { target });
    expect(r.written).toHaveLength(1);
    expect(r.removed).toEqual([]);
    expect(r.kept).toEqual(['rstale-cs1conv.json']);
    expect(fs.readdirSync(target).sort()).toEqual(['notes.txt', 'r1234-76561198000000000.json', 'rstale-cs1conv.json', r.written[0]].sort());
    // prune: only the other cs1conv configs go; the user's own RB roads and other files stay
    const p = installRb(out, { target, prune: true });
    expect(p.removed).toEqual(['rstale-cs1conv.json']);
    expect(fs.readdirSync(target).sort()).toEqual(['notes.txt', 'r1234-76561198000000000.json', r.written[0]].sort());
    expect(fs.readFileSync(path.join(target, 'r1234-76561198000000000.json'), 'utf8')).toBe('{"user":"config"}');
  });
});
