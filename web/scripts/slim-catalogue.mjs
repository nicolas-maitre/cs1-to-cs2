#!/usr/bin/env node
// Writes the slim CS2 building catalogue the web app bundles (public/cs2-catalogue.json) from the full one
// that `node buildings/run.mjs catalogue` writes. Keeps only placeable buildings and the fields
// buildings/match/match.mjs reads, so matching gives the same result (test/m3.test.ts checks that).
//
//   node web/scripts/slim-catalogue.mjs [full.json] [out.json]
//   defaults: %LOCALAPPDATA%\cs1-to-cs2\samples\cs2-catalogue.json → web/public/cs2-catalogue.json
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { slimCatalogue } from '../src/core/m3/catalogue.ts';

const isMain = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
if (isMain) {
  const local = process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local');
  const src = process.argv[2] ?? path.join(local, 'cs1-to-cs2', 'samples', 'cs2-catalogue.json');
  const out = process.argv[3] ?? path.join(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..', 'public', 'cs2-catalogue.json');
  const full = JSON.parse(fs.readFileSync(src, 'utf8').replace(/^﻿/, ''));
  const slim = slimCatalogue(full);
  fs.mkdirSync(path.dirname(out), { recursive: true });
  fs.writeFileSync(out, JSON.stringify(slim) + '\n');
  console.log(`${src} (${full.prefabs.length} prefabs) → ${out} (${slim.prefabs.filter(p => p.kind === 'building').length} placeable buildings, ${slim.prefabs.filter(p => p.kind === 'object').length} trees and boulders, ${(fs.statSync(out).size / 1024).toFixed(0)} kB)`);
}
