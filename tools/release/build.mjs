#!/usr/bin/env node
// One-command release build: every deployable artefact into release/<version>/ (git-ignored). Node >= 22, no npm deps.
//
//   node tools/release/build.mjs [--skip-games] [--skip-web] [--npm-ci] [--out <dir>] [--check]
//
// Steps
//   1. version check: VERSION (the product version) plus every component's version declarations must agree (see
//      COMPONENTS: each component declares its version in one primary place; any duplicate must match it).
//   2. CS1 exporter mod   → release/<v>/cs1-exporter/Cs1Exporter/{Cs1Exporter.dll, PreviewImage.png} (+ Workshop text)
//   3. CS2 importer mod   → release/<v>/cs2-importer/Cs2Importer/Cs2ImporterMod.dll
//      (both: `dotnet build -c Release` with the deploy folder pointed into release/, so nothing is copied to the game)
//   4. web tool           → web/dist built with `npm run build`, zipped
//   5. one zip per component, SHA256SUMS.txt, versions.json, RELEASE-NOTES.md (top-level CHANGELOG entry + each
//      component's latest changelog entry + the checksums)
// --skip-games skips the two mod builds (they need the games' DLLs, see local.props); --check only runs step 1.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import zlib from 'node:zlib';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const REPO = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const argv = process.argv.slice(2);
const flag = (n) => argv.includes(`--${n}`);
const opt = (n, d) => { const i = argv.indexOf(`--${n}`); return i >= 0 ? argv[i + 1] : d; };
const log = (...a) => console.error('[release]', ...a);

/** Where each component declares its version. `primary` is the source of truth; `also` must match it. */
export const COMPONENTS = [
  {
    id: 'cs1-exporter', title: 'CS1 → CS2 Exporter (Cities: Skylines mod)',
    primary: { file: 'cs1-exporter/src/Exporter.cs', re: /public const string Version = "([^"]+)"/ },
    also: [{ file: 'cs1-exporter/CHANGELOG.md', re: /^## ([0-9][^\s]*)/m }],
    changelog: 'cs1-exporter/CHANGELOG.md',
  },
  {
    id: 'cs2-importer', title: 'CS1 → CS2 Importer (Cities: Skylines II mod, with the buildings, objects, districts and transit steps)',
    primary: { file: 'cs2-importer/mod/Mod.cs', re: /public const string Version = "([^"]+)"/ },
    also: [
      { file: 'cs2-importer/mod/Cs2ImporterMod.csproj', re: /<Version>([^<]+)<\/Version>/ },
      { file: 'cs2-importer/mod/Properties/PublishConfiguration.xml', re: /<ModVersion Value="([^"]+)"/ },
      { file: 'cs2-importer/CHANGELOG.md', re: /^## ([0-9][^\s]*)/m },
    ],
    changelog: 'cs2-importer/CHANGELOG.md',
  },
  {
    id: 'web', title: 'CS1 → CS2 web tool (static site)',
    primary: { file: 'web/package.json', json: 'version' },
    also: [],
    changelog: 'web/CHANGELOG.md',
  },
  {
    id: 'buildings', title: 'CS2 buildings / objects / districts steps (library, compiled into the importer mod)',
    primary: { file: 'buildings/src/Steps.cs', re: /public const string Version = "([^"]+)"/ },
    also: [],
    changelog: null,
    library: true,
  },
];

const SEMVER = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/;

function readVersion(decl) {
  const file = path.join(REPO, decl.file);
  if (!fs.existsSync(file)) return { error: `${decl.file} not found` };
  const text = fs.readFileSync(file, 'utf8');
  if (decl.json) return { version: JSON.parse(text)[decl.json] };
  const m = decl.re.exec(text);
  return m ? { version: m[1] } : { error: `no version in ${decl.file}` };
}

/** Product version + every component version, with problems (mismatches, non-SemVer, missing). */
export function checkVersions() {
  const problems = [];
  const product = fs.readFileSync(path.join(REPO, 'VERSION'), 'utf8').trim();
  if (!SEMVER.test(product)) problems.push(`VERSION "${product}" is not SemVer`);
  const components = {};
  for (const c of COMPONENTS) {
    const p = readVersion(c.primary);
    if (p.error) { problems.push(`${c.id}: ${p.error}`); continue; }
    if (!SEMVER.test(p.version)) problems.push(`${c.id}: version "${p.version}" (${c.primary.file}) is not SemVer`);
    for (const a of c.also) {
      const v = readVersion(a);
      if (v.error) problems.push(`${c.id}: ${v.error}`);
      else if (v.version !== p.version) problems.push(`${c.id}: ${a.file} says ${v.version}, ${c.primary.file} says ${p.version}`);
    }
    components[c.id] = p.version;
  }
  return { product, components, problems };
}

// ------------------------------------------------------------------ zip (stored / deflate, no dependencies)

const CRC_TABLE = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(buf) {
  if (typeof zlib.crc32 === 'function') return zlib.crc32(buf) >>> 0;
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

/** Zips a folder (paths inside the zip relative to `root`, forward slashes, prefixed with `prefix`). */
export function zipFolder(root, outFile, prefix = '') {
  const files = [];
  const walk = (dir) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p); else files.push(p);
    }
  };
  walk(root);
  const parts = [], central = [];
  let offset = 0;
  const d = new Date();
  const dosTime = (d.getHours() << 11) | (d.getMinutes() << 5) | (d.getSeconds() >> 1);
  const dosDate = ((d.getFullYear() - 1980) << 9) | ((d.getMonth() + 1) << 5) | d.getDate();
  for (const f of files) {
    const name = Buffer.from(prefix + path.relative(root, f).split(path.sep).join('/'), 'utf8');
    const data = fs.readFileSync(f);
    const deflated = zlib.deflateRawSync(data, { level: 9 });
    const useDeflate = data.length > 0 && deflated.length < data.length;
    const body = useDeflate ? deflated : data;
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4); local.writeUInt16LE(0x0800, 6);
    local.writeUInt16LE(useDeflate ? 8 : 0, 8); local.writeUInt16LE(dosTime, 10); local.writeUInt16LE(dosDate, 12);
    local.writeUInt32LE(crc, 14); local.writeUInt32LE(body.length, 18); local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(name.length, 26); local.writeUInt16LE(0, 28);
    parts.push(local, name, body);
    const cd = Buffer.alloc(46);
    cd.writeUInt32LE(0x02014b50, 0); cd.writeUInt16LE(20, 4); cd.writeUInt16LE(20, 6); cd.writeUInt16LE(0x0800, 8);
    cd.writeUInt16LE(useDeflate ? 8 : 0, 10); cd.writeUInt16LE(dosTime, 12); cd.writeUInt16LE(dosDate, 14);
    cd.writeUInt32LE(crc, 16); cd.writeUInt32LE(body.length, 20); cd.writeUInt32LE(data.length, 24);
    cd.writeUInt16LE(name.length, 28); cd.writeUInt32LE(offset, 42);
    central.push(cd, name);
    offset += 30 + name.length + body.length;
  }
  const cdSize = central.reduce((s, b) => s + b.length, 0);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(files.length, 8); end.writeUInt16LE(files.length, 10);
  end.writeUInt32LE(cdSize, 12); end.writeUInt32LE(offset, 16);
  fs.writeFileSync(outFile, Buffer.concat([...parts, ...central, end]));
  return files.length;
}

// ------------------------------------------------------------------ helpers

function run(cmd, args, cwd = REPO) {
  log(`${path.relative(REPO, cwd) || '.'}$ ${cmd} ${args.join(' ')}`);
  // npm is a .cmd on Windows: run it through cmd.exe instead of shell:true with arguments (Node DEP0190).
  if (process.platform === 'win32' && cmd === 'npm') { args = ['/d', '/s', '/c', 'npm', ...args]; cmd = process.env.ComSpec ?? 'cmd.exe'; }
  execFileSync(cmd, args, { cwd, stdio: ['ignore', 'inherit', 'inherit'] });
}

/** Latest "## …" section of a changelog (heading included), or null. */
export function latestChangelogEntry(file) {
  if (!file || !fs.existsSync(path.join(REPO, file))) return null;
  const text = fs.readFileSync(path.join(REPO, file), 'utf8');
  const start = text.search(/^## /m);
  if (start < 0) return null;
  const rest = text.slice(start + 3);
  const next = rest.search(/^## /m);
  return ('## ' + (next < 0 ? rest : rest.slice(0, next))).trim();
}

function sha256(file) { return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'); }

// ------------------------------------------------------------------ main

async function main() {
  const v = checkVersions();
  for (const p of v.problems) log(`version problem: ${p}`);
  log(`product ${v.product}; ${Object.entries(v.components).map(([k, x]) => `${k} ${x}`).join(', ')}`);
  if (v.problems.length) process.exit(1);
  if (flag('check')) return;

  const out = path.resolve(opt('out', path.join(REPO, 'release', v.product)));
  fs.rmSync(out, { recursive: true, force: true });
  fs.mkdirSync(out, { recursive: true });
  const built = [];

  if (!flag('skip-games')) {
    const cs1 = path.join(out, 'cs1-exporter');
    run('dotnet', ['build', 'cs1-exporter/Cs1Exporter.csproj', '-c', 'Release', '-nologo', '-v', 'q', `-p:CS1ModsDir=${path.join(cs1, 'Cs1Exporter')}`]);
    fs.copyFileSync(path.join(REPO, 'cs1-exporter/workshop/description.txt'), path.join(cs1, 'workshop-description.txt'));
    fs.copyFileSync(path.join(REPO, 'cs1-exporter/CHANGELOG.md'), path.join(cs1, 'CHANGELOG.md'));
    built.push({ id: 'cs1-exporter', dir: cs1 });

    const cs2 = path.join(out, 'cs2-importer');
    run('dotnet', ['build', 'cs2-importer/mod/Cs2ImporterMod.csproj', '-c', 'Release', '-nologo', '-v', 'q', `-p:CS2LocalMods=${cs2}`]);
    // Paradox Mods packaging (PublishConfiguration.xml, Thumbnail.png; not the thumbnail script) and the changelog.
    fs.cpSync(path.join(REPO, 'cs2-importer/mod/Properties'), path.join(cs2, 'Properties'), { recursive: true, filter: (f) => !f.endsWith('.py') });
    fs.copyFileSync(path.join(REPO, 'cs2-importer/CHANGELOG.md'), path.join(cs2, 'CHANGELOG.md'));
    built.push({ id: 'cs2-importer', dir: cs2 });
  } else log('skipping the game mod builds (--skip-games)');

  if (!flag('skip-web')) {
    const web = path.join(REPO, 'web');
    if (flag('npm-ci')) run('npm', ['ci'], web);
    run('npm', ['run', 'build'], web);
    const dst = path.join(out, 'web');
    fs.cpSync(path.join(web, 'dist'), dst, { recursive: true });
    built.push({ id: 'web', dir: dst });
  }

  // One zip per component (named with the component's own version), then checksums and notes.
  const zips = [];
  for (const b of built) {
    const name = `${b.id}-${v.components[b.id]}.zip`;
    const n = zipFolder(b.dir, path.join(out, name));
    zips.push({ id: b.id, file: name, files: n });
    log(`${name}: ${n} files`);
  }
  const versions = { product: v.product, components: v.components, builtAt: new Date().toISOString(), git: gitDescribe(), artefacts: zips };
  fs.writeFileSync(path.join(out, 'versions.json'), JSON.stringify(versions, null, 2) + '\n');

  const sums = [];
  const walk = (dir) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name))) {
      const p = path.join(dir, e.name);
      if (e.isDirectory()) walk(p); else if (e.name !== 'SHA256SUMS.txt' && e.name !== 'RELEASE-NOTES.md') sums.push(`${sha256(p)}  ${path.relative(out, p).split(path.sep).join('/')}`);
    }
  };
  walk(out);
  fs.writeFileSync(path.join(out, 'SHA256SUMS.txt'), sums.join('\n') + '\n');
  fs.writeFileSync(path.join(out, 'RELEASE-NOTES.md'), releaseNotes(v, zips, sums));
  log(`release ${v.product} → ${out}`);
  console.log(JSON.stringify({ out, ...versions }, null, 2));
}

function gitDescribe() {
  try { return execFileSync('git', ['describe', '--always', '--dirty'], { cwd: REPO, encoding: 'utf8' }).trim(); } catch { return null; }
}

export function releaseNotes(v, zips, sums) {
  const lines = [`# CS1 → CS2 converter ${v.product}`, ''];
  const top = latestChangelogEntry('CHANGELOG.md');
  if (top) lines.push(top.replace(/^## /, '## Overview: '), '');
  lines.push('## Components', '', '| Component | Version | Artefact |', '| --- | --- | --- |');
  for (const c of COMPONENTS) {
    const z = zips.find((x) => x.id === c.id);
    lines.push(`| ${c.title} | ${v.components[c.id]} | ${z ? `\`${z.file}\`` : c.library ? 'inside the importer mod' : 'not built'} |`);
  }
  lines.push('');
  for (const c of COMPONENTS) {
    const e = latestChangelogEntry(c.changelog);
    if (e) lines.push(`## ${c.id}: ${e.replace(/^## /, '')}`, '');
  }
  lines.push('## Install', '',
    '- **CS1 exporter:** copy the `Cs1Exporter` folder to `%LOCALAPPDATA%\\Colossal Order\\Cities_Skylines\\Addons\\Mods\\` and enable it in Content Manager.',
    '- **CS2 importer:** copy the `Cs2Importer` folder to `%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\Mods\\` (or publish to Paradox Mods with `Properties/PublishConfiguration.xml`; its ModId is empty until the first upload).',
    '- **Web tool:** unzip and serve the folder (any static web server), or open it from a host.', '',
    '## SHA-256', '', '```', ...sums, '```', '');
  return lines.join('\n');
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch((e) => { console.error(e.stack ?? e.message); process.exit(1); });
}
