#!/usr/bin/env node
// Golden data for TransitPlanTests (DECISIONS-importer.md #45): the transit worker's own transitJobs
// (transit/lib/plan.mjs) run on a trimmed plan, so the C# port in src/Core/TransitPlan.cs fails a test when the two drift.
//
//   node cs2-importer/tests/fixtures/make-transit-golden.mjs [<package dir or cs1conv-plan.json>]
//
// With a plan argument, transit-plan.json is rebuilt from it: plan.transit, the bezier of every segment a stop uses and
// the zoning theme, plus a stop on a segment that isn't in the plan (the "skipped" path). Without one, the existing
// transit-plan.json is reused and only transit-jobs.json is rewritten (run this after any change to transit/lib/plan.mjs).
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(HERE, '..', '..', '..');
const { transitJobs } = await import(pathToFileURL(path.join(REPO, 'transit', 'lib', 'plan.mjs')).href);
const input = path.join(HERE, 'transit-plan.json');

if (process.argv[2]) {
  let src = path.resolve(process.argv[2]);
  if (fs.statSync(src).isDirectory()) src = path.join(src, 'cs1conv-plan.json');
  const plan = JSON.parse(fs.readFileSync(src, 'utf8'));
  if (!plan.transit?.lines?.length) throw new Error(src + ' has no transit lines');
  const used = new Set(plan.transit.lines.flatMap(l => (l.stops || []).map(s => s.segment)).filter(s => s != null));
  const transit = structuredClone(plan.transit);
  const bus = transit.lines.find(l => !l.requiresStations && l.stops?.length);
  if (bus) bus.stops.push({ ...bus.stops[0], segment: -424242, cs1Node: 999999 });
  const trimmed = {
    note: 'Trimmed from a cs1conv package for TransitPlanTests; regenerate with make-transit-golden.mjs.',
    zoning: { theme: plan.zoning?.theme ?? 'EU' },
    segments: plan.segments.filter(s => used.has(s.id)).map(s => ({ id: s.id, bezier: s.bezier })),
    transit,
  };
  fs.writeFileSync(input, JSON.stringify(trimmed) + '\n');
}

const plan = JSON.parse(fs.readFileSync(input, 'utf8'));
const out = {};
for (const theme of ['EU', 'NA']) out[theme] = transitJobs(plan, { theme });
fs.writeFileSync(path.join(HERE, 'transit-jobs.json'), JSON.stringify(out) + '\n');
console.log(JSON.stringify({ input, EU: out.EU.stats, skipped: out.EU.skipped.length }));
