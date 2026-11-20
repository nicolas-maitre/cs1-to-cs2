#!/usr/bin/env node
// M0 spike 3: turn CS1 road NetInfo lane layouts (from the M0 network dump) into
// CS2 Road Builder RoadConfig JSON files.
//
//   node wq-to-rb.mjs <dump.json> --list                 list candidate road NetInfos with features
//   node wq-to-rb.mjs <dump.json> --out <dir> "<netinfo name>" ...   generate configs + width report
//
// Option names/values come from Road Builder 0.7/0.8 source (see NOTES-roadbuilder.md).
// Only exact, existing section combinations are emitted (RB 0.7.0 has no fuzzy fallback).

import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const RB = 'RoadBuilder.LaneGroups.';
const SIDEWALK_WIDTHS = {
  '': [1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5, 5.5, 6, 7],
  P: [5, 5.5, 6, 7],
  B: [3.5, 4.5, 5, 5.5, 6, 7],
};
const MEDIAN_WIDTHS = [1, 2, 5];

const args = process.argv.slice(2);
if (args.length < 2) {
  console.error('usage: wq-to-rb.mjs <dump.json> --list | --out <dir> <netinfo name>...');
  process.exit(2);
}
const dump = JSON.parse(fs.readFileSync(args[0], 'utf8').replace(/^﻿/, ''));
const infos = new Map(dump.netInfos.map(i => [i.name, i]));
const usage = new Map();
for (const s of dump.segments) usage.set(s.info, (usage.get(s.info) ?? 0) + 1);

const has = (flags, f) => String(flags ?? '').split(',').map(s => s.trim()).includes(f);
const nearest = (v, list) => list.reduce((a, b) => (Math.abs(b - v) < Math.abs(a - v) ? b : a));
const fmtM = v => `${v}m`;

/** Normalise a CS1 NetInfo into a left→right cross-section model. */
function analyse(info) {
  const f = info.fields;
  const halfWidth = f.m_halfWidth, pavement = f.m_pavementWidth;
  const lanes = info.lanes.map(l => ({
    pos: l.fields.m_position, width: l.fields.m_width,
    laneType: l.fields.m_laneType, vehicleType: l.fields.m_vehicleType,
    direction: l.fields.m_direction, speed: l.fields.m_speedLimit,
    trees: l.props.filter(p => p.tree || p.finalTree).map(p => p.tree ?? p.finalTree),
    props: l.props.map(p => p.prop).filter(Boolean),
  }));
  const drive = lanes
    .filter(l => /Vehicle|TransportVehicle|Parking/.test(l.laneType) && l.vehicleType !== 'None')
    .filter(l => Math.abs(l.pos) < halfWidth - pavement + 0.01);
  // Merge lanes sharing a position (e.g. car + tram rails on the same lane).
  const slots = [];
  for (const l of [...drive].sort((a, b) => a.pos - b.pos)) {
    const s = slots.find(s => Math.abs(s.pos - l.pos) < 0.2);
    if (s) { s.parts.push(l); s.width = Math.max(s.width, l.width); }
    else slots.push({ pos: l.pos, width: l.width, parts: [l] });
  }
  for (const s of slots) {
    const types = s.parts.map(p => `${p.laneType}/${p.vehicleType}`).join(' + ');
    const vt = s.parts.map(p => p.vehicleType).join(',');
    const lt = s.parts.map(p => p.laneType).join(',');
    s.kind = /Parking/.test(lt) ? 'parking'
      : /Bicycle/.test(vt) && !/Car/.test(vt) ? 'bike'
      : /TransportVehicle/.test(lt) && !/(^|,)\s*Vehicle/.test(lt) ? 'bus'
      : /Trolleybus/.test(vt) ? 'trolleybus'
      : /Monorail/.test(vt) ? 'monorail'
      : /Car/.test(vt) ? 'car'
      : /Tram/.test(vt) ? 'tram'
      : 'other';
    s.tram = /Tram/.test(vt);
    s.types = types;
    const dirs = s.parts.map(p => p.direction).join(',');
    s.invert = /Backward/.test(dirs) && !/Forward/.test(dirs);
    s.speedKmh = Math.max(...s.parts.map(p => p.speed ?? 0)) * 50;
  }
  // Decoration lanes (laneType None) with trees, located between drive slots → median trees.
  const decor = lanes.filter(l => l.laneType === 'None');
  return { halfWidth, pavement, slots, decor, lanes };
}

function convert(info) {
  const a = analyse(info);
  const notes = [];
  const out = [];
  const addons = new Set(['HasUndergroundWaterPipes', 'HasUndergroundElectricityCable']);
  const ai = info.aiFields ?? {};
  if (ai.m_trafficLights) addons.add('GeneratesTrafficLights');
  if (ai.m_enableZoning !== false) addons.add('GeneratesZoningBlocks');

  const slots = [...a.slots];
  const takeEdge = (fromLeft) => {
    const extra = { parking: 0, bike: 0 };
    while (slots.length) {
      const s = fromLeft ? slots[0] : slots[slots.length - 1];
      if (s.kind !== 'parking' && s.kind !== 'bike') break;
      extra[s.kind] += s.width;
      fromLeft ? slots.shift() : slots.pop();
    }
    return extra;
  };
  const leftExtra = takeEdge(true), rightExtra = takeEdge(false);

  const sidewalk = (extra, invert, side) => {
    const kind = extra.parking ? 'P' : extra.bike ? 'B' : '';
    const want = a.pavement + extra.parking + extra.bike;
    const w = nearest(want, SIDEWALK_WIDTHS[kind]);
    if (Math.abs(w - want) > 0.01) notes.push(`${side} sidewalk: CS1 ${want}m (pavement ${a.pavement}m + parking ${extra.parking}m + bike ${extra.bike}m) → RB ${w}m`);
    if (extra.parking && extra.bike) notes.push(`${side}: CS1 has both parking and bike lane; RB sidewalk supports only one → kept parking, dropped bike lane`);
    const sideTrees = a.decor.some(d => d.trees.length && Math.abs(d.pos) >= a.halfWidth - a.pavement - 0.01 && Math.sign(d.pos) === (side === 'left' ? -1 : 1));
    if (sideTrees) {
      if (w >= 5 && kind !== 'B' || kind === 'B' && w >= 4.5) addons.add(side === 'left' ? 'TreesLeft' : 'TreesRight');
      else notes.push(`${side} sidewalk has trees in CS1 but RB ${kind || 'plain'} sidewalk ${w}m cannot carry trees (needs ≥5m)`);
    }
    return { lane: { Version: 5, SectionPrefabName: null, GroupPrefabName: RB + 'SidewalkGroupPrefab', GroupOptions: { Parking: kind, Width: fmtM(w), 'Parking Angle': '' }, Invert: invert }, width: w };
  };

  const left = sidewalk(leftExtra, true, 'left');
  out.push(left);

  // Outermost car lane per direction gets "Transport" (bus/taxi stops attach there), as in the user's own RB roads.
  const carIdx = slots.map((s, i) => (s.kind === 'car' ? i : -1)).filter(i => i >= 0);
  const outer = new Set();
  for (const inv of [true, false]) {
    const same = carIdx.filter(i => slots[i].invert === inv);
    const hasBus = slots.some(s => s.kind === 'bus' && s.invert === inv);
    if (same.length && !hasBus) outer.add(inv ? same[0] : same[same.length - 1]);
  }

  for (let i = 0; i < slots.length; i++) {
    const s = slots[i];
    if (i > 0) {
      const p = slots[i - 1];
      const gap = (s.pos - s.width / 2) - (p.pos + p.width / 2);
      if (gap > 0.6) {
        const trees = a.decor.some(d => d.trees.length && d.pos > p.pos && d.pos < s.pos);
        const grassHint = /grass/i.test(info.name) || /median/i.test(info.name);
        let mw = trees ? 5 : nearest(gap, MEDIAN_WIDTHS);
        if (trees) { addons.add('TreesCenter'); addons.add('GrassCenter'); }
        else if (mw === 5 && grassHint) addons.add('GrassCenter');
        if (Math.abs(mw - gap) > 0.01) notes.push(`median: CS1 gap ${gap.toFixed(2)}m → RB ${mw}m${trees ? ' (5m forced: only 5m medians carry trees)' : ''}`);
        out.push({ lane: { Version: 5, SectionPrefabName: null, GroupPrefabName: RB + 'MedianGroupPrefab', GroupOptions: { 'Median Width': fmtM(mw) }, Invert: false }, width: mw });
      } else if (gap > 0.25) {
        const ew = Math.round(gap * 2) / 2;
        out.push({ lane: { Version: 5, SectionPrefabName: null, GroupPrefabName: RB + 'EmptyGroupPrefab', GroupOptions: { 'Ground Type': 'Asphalt', 'Lane Width': fmtM(ew) }, Invert: false }, width: ew });
        notes.push(`gap ${gap.toFixed(2)}m between lanes → RB Empty ${ew}m`);
      }
    }
    const lw = s.width >= 3.5 ? 4 : 3;
    if (Math.abs(lw - s.width) > 0.01) notes.push(`${s.kind} lane @${s.pos}: CS1 ${s.width}m → RB ${lw}m`);
    let lane;
    switch (s.kind) {
      case 'car':
        lane = { GroupPrefabName: RB + 'CarGroupPrefab', GroupOptions: { 'Lane Width': fmtM(lw), 'Transport Option': s.tram ? 'Tram' : outer.has(i) ? 'Transport' : '', Markings: '' } };
        break;
      case 'bus':
        lane = { GroupPrefabName: RB + 'BusGroupPrefab', GroupOptions: { 'Lane Width': fmtM(lw), 'Transport Option': s.tram ? 'Tram' : '' } };
        break;
      case 'tram':
        lane = { GroupPrefabName: RB + 'TramGroupPrefab', GroupOptions: {} };
        if (lw !== 3) notes.push('tram lane forced to 3m (only width RB offers)');
        break;
      case 'parking':
        lane = { GroupPrefabName: RB + 'ParkingGroupPrefab', GroupOptions: { 'Parking Angle': 'Parallel', Markings: '' } };
        notes.push(`interior parking lane @${s.pos} → RB Parking 2m`);
        break;
      default:
        notes.push(`UNSUPPORTED ${s.kind} lane @${s.pos} (${s.types}) → mapped to car lane`);
        lane = { GroupPrefabName: RB + 'CarGroupPrefab', GroupOptions: { 'Lane Width': fmtM(lw), 'Transport Option': 'Transport', Markings: '' } };
    }
    out.push({ lane: { Version: 5, SectionPrefabName: null, ...lane, Invert: s.invert }, width: s.kind === 'parking' ? 2 : s.kind === 'tram' ? 3 : lw });
  }

  out.push(sidewalk(rightExtra, false, 'right'));

  const speed = Math.max(0, ...a.slots.filter(s => s.kind === 'car' || s.kind === 'bus').map(s => s.speedKmh));
  const rbWidth = out.reduce((t, l) => t + l.width, 0);
  const cs1Width = a.halfWidth * 2;
  const idHash = crypto.createHash('sha1').update('cs1conv:' + info.name).digest('hex');
  const id = `r${idHash.slice(0, 8)}-${idHash.slice(8, 12)}-5${idHash.slice(13, 16)}-8${idHash.slice(17, 20)}-${idHash.slice(20, 32)}-cs1conv`;
  const title = (info.title || info.name.replace(/^\d+\./, '').replace(/_Data$/, ''));

  const config = {
    Type: 'RoadConfig', Version: 5, OriginalID: id, ID: id,
    Name: `[CS1] ${title}`.slice(0, 60),
    PillarPrefabName: 'Pillar Placeholder',
    SpeedLimit: Math.round(speed / 5) * 5 * 2 || 100,
    MaxSlopeSteepness: 0.2,
    Category: 'Road',
    Addons: [...addons].join(', '),
    Lanes: out.map(o => o.lane),
    ToolbarState: 'Inherit',
  };
  return { config, report: { cs1: info.name, title, id, cs1Width, rbWidth, diff: +(rbWidth - cs1Width).toFixed(2), speedKmh: speed, notes,
    cs1Slots: a.slots.map(s => `${s.kind}${s.invert ? '←' : '→'} ${s.width}m @${s.pos}`), rbLanes: out.map(o => `${o.lane.GroupPrefabName.replace(RB, '').replace('GroupPrefab', '')}${o.lane.Invert ? '←' : '→'} ${o.width}m ${JSON.stringify(o.lane.GroupOptions)}`) } };
}

if (args[1] === '--list') {
  const rows = [];
  for (const info of infos.values()) {
    if (info.service !== 'Road' || !/RoadAI$/.test(info.ai ?? '')) continue;
    const a = analyse(info);
    const kinds = a.slots.map(s => s.kind);
    let gaps = 0;
    for (let i = 1; i < a.slots.length; i++) if ((a.slots[i].pos - a.slots[i].width / 2) - (a.slots[i - 1].pos + a.slots[i - 1].width / 2) > 0.6) gaps++;
    rows.push({ name: info.name, used: usage.get(info.name) ?? 0, width: a.halfWidth * 2, lanes: kinds.join(' '), median: gaps > 0, bus: kinds.includes('bus'), wq: /WQ/.test(info.name) });
  }
  rows.sort((x, y) => y.used - x.used);
  console.table(rows);
} else if (args[1] === '--out') {
  const dir = args[2];
  fs.mkdirSync(dir, { recursive: true });
  const reports = [];
  for (const name of args.slice(3)) {
    const info = infos.get(name);
    if (!info) { console.error(`not found: ${name}`); continue; }
    const { config, report } = convert(info);
    fs.writeFileSync(path.join(dir, `${config.ID}.json`), JSON.stringify(config, null, 4));
    reports.push(report);
  }
  console.log(JSON.stringify(reports, null, 2));
}
