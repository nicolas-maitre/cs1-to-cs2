#!/usr/bin/env node
// Samples, every --interval s (default 2) until --stop-file exists or --max s pass:
//   Cities2.exe working set / private bytes / CPU, every node.exe running tools/e2e or web/cli (working set),
//   and the CS2 bridge /health (frame counter, status age, mode) → NDJSON lines {t, cs2:{…}, node:[…], health:{…}}.
//   node terrain/tools/perf-monitor.mjs --out perf.ndjson [--interval 2] [--stop-file f] [--max 7200]
// Then: node terrain/tools/perf-monitor.mjs --summarize perf.ndjson [--e2e <run dir>]  (peaks and fps per e2e step)
import { appendFileSync, existsSync, readFileSync, writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';
import { health, readDiscovery } from '../../tools/gamectl/lib/index.js';

const argv = process.argv.slice(2);
const opt = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };

function processes() {
  const ps = 'Get-CimInstance Win32_Process -Filter "Name=\'Cities2.exe\' OR Name=\'node.exe\'" | ForEach-Object { $p = Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue; if ($p) { "{0}|{1}|{2}|{3}|{4}|{5}" -f $_.Name, $_.ProcessId, $p.WorkingSet64, $p.PrivateMemorySize64, [math]::Round($p.TotalProcessorTime.TotalSeconds,1), ($_.CommandLine -replace "\\|"," ") } }';
  const out = execFileSync('powershell', ['-NoProfile', '-Command', ps], { encoding: 'utf8', timeout: 20000 });
  const r = { cs2: null, node: [] };
  for (const line of out.split(/\r?\n/)) {
    const [name, pid, ws, priv, cpu, cmd] = line.split('|');
    if (!name) continue;
    const rec = { pid: +pid, wsMB: Math.round(+ws / 1048576), privMB: Math.round(+priv / 1048576), cpuS: +cpu };
    if (/Cities2/i.test(name)) r.cs2 = rec;
    else if (/e2e[\\/]run\.mjs|cs1conv\.ts|terrain[\\/]|transit[\\/]|buildings[\\/]/.test(cmd || '')) r.node.push({ ...rec, cmd: (cmd || '').replace(/^.*?node(\.exe)?"?\s+/i, '').slice(0, 80) });
  }
  return r;
}

async function sample() {
  let h = null;
  try { const info = readDiscovery('cs2'); const x = await health(info.port, { timeoutMs: 1500 }); h = { frame: x.status?.frame ?? null, age: x.status_age_ms, mode: x.status?.mode, paused: x.status?.paused }; } catch { /* not running */ }
  let p = { cs2: null, node: [] };
  try { p = processes(); } catch { /* ignore */ }
  return { t: Date.now(), ...p, health: h };
}

if (opt('--summarize')) {
  const rows = readFileSync(opt('--summarize'), 'utf8').trim().split('\n').map(l => JSON.parse(l));
  const peak = (f) => rows.reduce((m, r) => Math.max(m, f(r) ?? 0), 0);
  const res = { samples: rows.length, from: new Date(rows[0].t).toISOString(), to: new Date(rows.at(-1).t).toISOString(),
    cs2PeakWsMB: peak(r => r.cs2?.wsMB), cs2PeakPrivMB: peak(r => r.cs2?.privMB), nodePeakWsMB: peak(r => Math.max(0, ...r.node.map(n => n.wsMB))) };
  // fps between samples while the bridge answered with a fresh status
  const fps = [];
  for (let i = 1; i < rows.length; i++) {
    const a = rows[i - 1], b = rows[i];
    if (a.health?.frame == null || b.health?.frame == null) continue;
    fps.push({ t: b.t, fps: (b.health.frame - a.health.frame) / ((b.t - a.t) / 1000), stale: b.health.age, paused: b.health.paused });
  }
  res.minFps = fps.length ? Math.round(Math.min(...fps.map(f => f.fps)) * 10) / 10 : null;
  res.maxStatusAgeMs = peak(r => r.health?.age);
  const dir = opt('--e2e');
  if (dir && existsSync(join(dir, 'summary.json'))) {
    const s = JSON.parse(readFileSync(join(dir, 'summary.json'), 'utf8'));
    let t = Date.parse(s.started);
    res.steps = (s.steps || []).map(st => {
      const sec = st.seconds ?? st.s ?? 0, t0 = t, t1 = t + sec * 1000; t = t1;
      const w = rows.filter(r => r.t >= t0 && r.t <= t1 + 2000), f = fps.filter(x => x.t >= t0 && x.t <= t1 + 2000);
      return { step: st.step || st.name, ok: st.ok, seconds: sec,
        cs2PeakWsMB: w.reduce((m, r) => Math.max(m, r.cs2?.wsMB ?? 0), 0),
        nodePeakWsMB: w.reduce((m, r) => Math.max(m, ...r.node.map(n => n.wsMB), 0), 0),
        minFps: f.length ? Math.round(Math.min(...f.map(x => x.fps)) * 10) / 10 : null,
        meanFps: f.length ? Math.round(f.reduce((a, x) => a + x.fps, 0) / f.length * 10) / 10 : null,
        maxStatusAgeMs: w.reduce((m, r) => Math.max(m, r.health?.age ?? 0), 0) };
    });
  }
  console.log(JSON.stringify(res, null, 2));
} else {
  const out = opt('--out', 'perf.ndjson'), stopFile = opt('--stop-file', null);
  const interval = Number(opt('--interval', 2)) * 1000, max = Number(opt('--max', 7200)) * 1000, t0 = Date.now();
  writeFileSync(out, '');
  while (Date.now() - t0 < max && !(stopFile && existsSync(stopFile))) {
    const s0 = Date.now();
    appendFileSync(out, JSON.stringify(await sample()) + '\n');
    await new Promise(r => setTimeout(r, Math.max(0, interval - (Date.now() - s0))));
  }
}
