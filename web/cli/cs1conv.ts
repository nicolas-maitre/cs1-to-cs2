#!/usr/bin/env node
// cs1conv CLI: the web tool's core for scripts. Node ≥ 24 runs this file directly (type stripping).
//
//   node web/cli/cs1conv.ts inspect <file.cs1x|dump.json> [--json]
//   node web/cli/cs1conv.ts plan <file> --crop <cx>,<cz>,<size> --out <dir> [--overrides <map.json>] [--install-rb] [--keep-other-rb]
//   node web/cli/cs1conv.ts plan <file> --auto-crop <size> --out <dir> ...      (square holding the most road segments)
//   node web/cli/cs1conv.ts install-rb <package dir> [--keep-other-rb]
//
// --install-rb copies the package's roadbuilder/*-cs1conv.json into CS2's ModsData\RoadBuilder
// folder. Only files ending in -cs1conv.json are ever written or removed there; other cs1conv
// configs not in this package are removed unless --keep-other-rb is given.

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { readCityFile } from '../src/core/cs1x.ts';
import { validateCrop } from '../src/core/crop.ts';
import { autoCrop, buildPackage, mappingStats, packageFiles } from '../src/core/plan.ts';
import type { MappingOverride } from '../src/core/mapping.ts';
import type { Crop, Cs1City } from '../src/core/types.ts';

const RB_SUFFIX = '-cs1conv.json';

function usage(msg?: string): never {
  if (msg) console.error(`error: ${msg}\n`);
  console.error(`usage:
  node web/cli/cs1conv.ts inspect <file.cs1x|dump.json> [--json]
  node web/cli/cs1conv.ts plan <file> (--crop <cx>,<cz>,<size> | --auto-crop <size>) --out <dir> [--overrides <map.json>] [--install-rb] [--keep-other-rb]
  node web/cli/cs1conv.ts install-rb <package dir> [--keep-other-rb]`);
  process.exit(2);
}

function parseArgs(argv: string[]) {
  const pos: string[] = [];
  const flags: Record<string, string | true> = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const name = a.slice(2);
      const boolFlags = ['json', 'install-rb', 'keep-other-rb', 'help'];
      if (boolFlags.includes(name)) flags[name] = true;
      else { const v = argv[++i]; if (v == null) usage(`--${name} needs a value`); flags[name] = v; }
    } else pos.push(a);
  }
  return { pos, flags };
}

async function load(file: string): Promise<Cs1City> {
  if (!fs.existsSync(file)) usage(`file not found: ${file}`);
  const bytes = new Uint8Array(fs.readFileSync(file));
  return readCityFile(bytes);
}

export function rbFolder(): string {
  return path.join(os.homedir(), 'AppData', 'LocalLow', 'Colossal Order', 'Cities Skylines II', 'ModsData', 'RoadBuilder');
}

function cs2Running(): boolean {
  if (process.platform !== 'win32') return false;
  try { return /Cities2\.exe/i.test(execFileSync('tasklist', ['/FI', 'IMAGENAME eq Cities2.exe', '/NH'], { encoding: 'utf8' })); }
  catch { return false; }
}

/** Copy <pkg>/roadbuilder/*-cs1conv.json into the RB folder. Touches only *-cs1conv.json files. */
export function installRb(pkgDir: string, opts: { keepOther?: boolean; target?: string } = {}) {
  const src = path.join(pkgDir, 'roadbuilder');
  const dst = opts.target ?? rbFolder();
  if (!fs.existsSync(src)) throw new Error(`no roadbuilder folder in ${pkgDir}`);
  const files = fs.readdirSync(src).filter(f => f.endsWith(RB_SUFFIX));
  fs.mkdirSync(dst, { recursive: true });
  const written: string[] = [], removed: string[] = [];
  for (const f of files) {
    const text = fs.readFileSync(path.join(src, f), 'utf8');
    const cfg = JSON.parse(text);
    if (`${cfg.ID}.json` !== f || !String(cfg.ID).endsWith('-cs1conv')) throw new Error(`refusing to install ${f}: ID does not match the file name`);
    fs.writeFileSync(path.join(dst, f), text);
    written.push(f);
  }
  if (!opts.keepOther) {
    for (const f of fs.readdirSync(dst)) {
      if (f.endsWith(RB_SUFFIX) && !files.includes(f)) { fs.rmSync(path.join(dst, f)); removed.push(f); }
    }
  }
  return { target: dst, written, removed };
}

function printInstall(r: ReturnType<typeof installRb>) {
  console.log(`Road Builder folder: ${r.target}`);
  console.log(`  installed ${r.written.length} cs1conv config(s)${r.removed.length ? `, removed ${r.removed.length} stale cs1conv config(s): ${r.removed.join(', ')}` : ''}`);
  if (cs2Running()) console.log('  note: CS2 is running; Road Builder only reads configs at game start, restart CS2 to load them');
}

async function cmdInspect(file: string, json: boolean) {
  const city = await load(file);
  const center = autoCrop(city, 3000);
  const pkg = buildPackage(city, { crop: { centerX: 0, centerZ: 0, size: 17280 } });
  const stats = mappingStats(pkg.mapping);
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (const n of city.nodes) { minX = Math.min(minX, n.position[0]); maxX = Math.max(maxX, n.position[0]); minZ = Math.min(minZ, n.position[2]); maxZ = Math.max(maxZ, n.position[2]); }
  const summary = {
    file, source: city.source, sha256: city.sha256, city: city.manifest.city, exportedAt: city.manifest.exportedAt, game: city.manifest.game,
    exporter: city.manifest.exporter, sections: city.manifest.sections, mods: city.manifest.mods,
    counts: { nodes: city.nodes.length, segments: city.segments.length, netInfos: city.netInfos.length, imtFillers: city.imtFillers.length },
    extent: { minX, maxX, minZ, maxZ },
    terrain: city.terrain ? { resolution: city.terrain.resolution, size: city.terrain.size, heightScale: city.terrain.heightScale, min: city.terrain.min, max: city.terrain.max } : null,
    autoCrop3km: center,
    mapping: stats,
    netInfos: pkg.mapping.map(r => ({
      netInfo: r.netInfo, category: r.category, segments: r.segments, variants: r.infos.filter(i => i !== r.netInfo),
      target: r.target.mode === 'rb' ? `RB ${r.rb ? `${r.rb.category} ${r.rb.cs1Width}→${r.rb.rbWidth} m` : '(no drive lane)'}` : r.target.mode === 'vanilla' ? `${r.target.type} "${r.target.name}"` : `skip: ${r.target.reason}`,
    })),
    report: city.report,
  };
  if (json) { console.log(JSON.stringify(summary, null, 2)); return; }
  console.log(`${summary.city?.name ?? '?'} (${summary.city?.saveName ?? '?'}) – ${city.source}, exported ${summary.exportedAt ?? '?'}`);
  console.log(`  ${summary.counts.nodes} nodes, ${summary.counts.segments} segments, ${summary.counts.netInfos} NetInfos, ${summary.counts.imtFillers} IMT fills`);
  console.log(`  extent x ${minX.toFixed(0)}…${maxX.toFixed(0)}, z ${minZ.toFixed(0)}…${maxZ.toFixed(0)}; best 3 km crop centre (${center.centerX}, ${center.centerZ}) with ${center.roadSegments} road segments`);
  console.log(`  terrain: ${summary.terrain ? `${summary.terrain.resolution}² over ${summary.terrain.size} m, ${summary.terrain.min}…${summary.terrain.max} m` : 'none'}`);
  if (city.manifest.sections) console.log(`  sections: ${Object.entries(city.manifest.sections).map(([k, v]) => `${k}=${v}`).join(' ')}`);
  console.log(`  used ground NetInfos: ${stats.netInfos.all} → RB ${stats.rb.all}, vanilla ${stats.vanilla.all}, not placeable ${stats.skippedNotPlaceable.all}, unmapped ${stats.skippedUnmapped.all}`);
  console.table(summary.netInfos.map(r => ({ ...r, variants: r.variants.join(', ') })));
  for (const r of city.report) console.log(`  [${r.level}] ${r.section ?? ''} ${r.message}`);
}

async function cmdPlan(file: string, flags: Record<string, string | true>) {
  const out = flags.out;
  if (typeof out !== 'string') usage('--out <dir> is required');
  const city = await load(file);
  let crop: Crop;
  if (typeof flags.crop === 'string') {
    const [cx, cz, size] = flags.crop.split(',').map(Number);
    crop = { centerX: cx, centerZ: cz, size };
  } else if (typeof flags['auto-crop'] === 'string') {
    const { roadSegments, ...c } = autoCrop(city, Number(flags['auto-crop']));
    crop = c;
    console.log(`auto crop: centre (${c.centerX}, ${c.centerZ}) holds ${roadSegments} road segments`);
  } else usage('--crop <cx>,<cz>,<size> or --auto-crop <size> is required');
  const v = validateCrop(crop);
  if (v.errors.length) usage(v.errors.join('; '));
  let overrides: Record<string, MappingOverride> = {};
  if (typeof flags.overrides === 'string') overrides = JSON.parse(fs.readFileSync(flags.overrides, 'utf8'));
  const pkg = buildPackage(city, { crop, overrides });
  if (pkg.errors.length) usage(pkg.errors.join('; '));

  // Rewrite the package folder: remove our own previous outputs only.
  fs.mkdirSync(out, { recursive: true });
  const rbDir = path.join(out, 'roadbuilder');
  if (fs.existsSync(rbDir)) for (const f of fs.readdirSync(rbDir)) if (f.endsWith(RB_SUFFIX)) fs.rmSync(path.join(rbDir, f));
  for (const f of packageFiles(pkg)) {
    const p = path.join(out, f.path);
    fs.mkdirSync(path.dirname(p), { recursive: true });
    fs.writeFileSync(p, f.text);
  }
  const s = pkg.plan.stats;
  const ms = mappingStats(pkg.mapping);
  console.log(`package written to ${path.resolve(out)}`);
  console.log(`  crop centre (${crop.centerX}, ${crop.centerZ}), size ${crop.size} m → CS2 bounds x ${pkg.plan.bounds!.minX}…${pkg.plan.bounds!.maxX}, z ${pkg.plan.bounds!.minZ}…${pkg.plan.bounds!.maxZ}`);
  console.log(`  ${s.segments} segments, ${s.nodes} nodes, ${s.rbConfigs} RB configs; by type ${JSON.stringify(s.byPrefabType)}`);
  console.log(`  skipped ${s.skipped} (crossing crop edge ${s.crossingCropEdge}, not placeable / unmapped ${s.unmapped}); outside crop ${s.outsideCrop}`);
  console.log(`  ground NetInfos in crop: ${ms.netInfos.inCrop} → RB ${ms.rb.inCrop}, vanilla ${ms.vanilla.inCrop}, not placeable ${ms.skippedNotPlaceable.inCrop}, unmapped ${ms.skippedUnmapped.inCrop}`);
  const warn = pkg.report.items.filter(i => i.level === 'warning').length;
  console.log(`  report: ${pkg.report.items.length} items (${warn} warnings)`);
  if (flags['install-rb']) printInstall(installRb(out, { keepOther: !!flags['keep-other-rb'] }));
}

async function main() {
  const { pos, flags } = parseArgs(process.argv.slice(2));
  const [cmd, arg] = pos;
  if (!cmd || flags.help) usage();
  if (cmd === 'inspect') { if (!arg) usage('inspect needs a file'); await cmdInspect(arg, !!flags.json); }
  else if (cmd === 'plan') { if (!arg) usage('plan needs a file'); await cmdPlan(arg, flags); }
  else if (cmd === 'install-rb') { if (!arg) usage('install-rb needs a package dir'); printInstall(installRb(arg, { keepOther: !!flags['keep-other-rb'] })); }
  else usage(`unknown command ${cmd}`);
}

const isMain = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
if (isMain) main().catch(e => { console.error(e instanceof Error ? e.stack ?? e.message : e); process.exit(1); });
