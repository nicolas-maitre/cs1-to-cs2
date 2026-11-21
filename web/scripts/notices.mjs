#!/usr/bin/env node
// Writes public/THIRD-PARTY-NOTICES.txt: name, version, licence and licence text of every npm package
// bundled into the web app (the production dependencies and their dependencies).
//   node web/scripts/notices.mjs
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..');

/** Production dependency closure of web/package.json, sorted by name. */
export function bundledPackages(dir = root) {
  const seen = new Map();
  const visit = (name) => {
    if (seen.has(name)) return;
    const pj = path.join(dir, 'node_modules', name, 'package.json');
    if (!fs.existsSync(pj)) return;
    const p = JSON.parse(fs.readFileSync(pj, 'utf8'));
    const pkgDir = path.dirname(pj);
    const licenceFile = fs.readdirSync(pkgDir).find(f => /^licen[cs]e(\.md|\.txt)?$/i.test(f));
    seen.set(name, {
      name, version: p.version, license: typeof p.license === 'string' ? p.license : p.license?.type ?? 'see package',
      homepage: p.homepage ?? (typeof p.repository === 'string' ? p.repository : p.repository?.url) ?? '',
      text: licenceFile ? fs.readFileSync(path.join(pkgDir, licenceFile), 'utf8').trim() : '',
    });
    for (const d of Object.keys(p.dependencies ?? {})) visit(d);
  };
  const app = JSON.parse(fs.readFileSync(path.join(dir, 'package.json'), 'utf8'));
  for (const d of Object.keys(app.dependencies ?? {})) visit(d);
  return [...seen.values()].sort((a, b) => a.name.localeCompare(b.name));
}

export function noticesText(pkgs) {
  const head = [
    'cs1conv web tool – third-party software notices',
    '',
    'The web app bundles the following open-source packages. Each is used under its licence, reproduced below.',
    '',
    ...pkgs.map(p => `- ${p.name} ${p.version} (${p.license})${p.homepage ? ` – ${p.homepage.replace(/^git\+/, '').replace(/\.git$/, '')}` : ''}`),
    '',
  ];
  const body = pkgs.flatMap(p => ['='.repeat(72), `${p.name} ${p.version} – ${p.license}`, '='.repeat(72), '', p.text || '(no licence file shipped with the package; licence: ' + p.license + ')', '']);
  return [...head, ...body].join('\n');
}

const isMain = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(root, 'scripts', 'notices.mjs');
if (isMain) {
  const pkgs = bundledPackages();
  const out = path.join(root, 'public', 'THIRD-PARTY-NOTICES.txt');
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, noticesText(pkgs) + '\n');
  console.log(`wrote ${out}: ${pkgs.map(p => `${p.name}@${p.version} (${p.license})`).join(', ')}`);
}
