#!/usr/bin/env node
// Writes the slim vanilla CS2 road list the web app bundles (public/cs2-roads.json) from the one the importer's
// "roadlayouts" exec op dumps (cs2-importer/src/Game/RoadLayouts.cs). Keeps roads and highways and only the fields
// src/core/vanilla.ts reads, so matching gives the same result (test/vanilla.test.ts checks that).
//
//   node web/scripts/slim-roads.mjs [cs2-roads.json] [out.json]
//   defaults: %LOCALAPPDATA%\cs1-to-cs2\samples\cs2-roads.json → web/public/cs2-roads.json
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import { slimRoads } from '../src/core/vanilla.ts';

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const local = process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local');
const src = process.argv[2] ?? path.join(local, 'cs1-to-cs2', 'samples', 'cs2-roads.json');
const out = process.argv[3] ?? path.join(here, '..', 'public', 'cs2-roads.json');
const full = JSON.parse(fs.readFileSync(src, 'utf8').replace(/^\uFEFF/, ''));
const slim = slimRoads(full);
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify(slim) + '\n');
console.log(`${src} (${full.roads.length} roads) → ${out} (${slim.roads.length} roads and highways, ${(fs.statSync(out).size / 1024).toFixed(0)} kB)`);
