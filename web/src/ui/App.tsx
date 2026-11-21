import { strToU8, zipSync } from 'fflate';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { CS2_MAX_CROP, clampCrop, validateCrop } from '../core/crop.ts';
import { readCityFile } from '../core/cs1x.ts';
import type { MappingOverride } from '../core/mapping.ts';
import { autoCrop, buildPackage, mappingStats, packageFiles } from '../core/plan.ts';
import type { BuiltPackage } from '../core/plan.ts';
import type { Crop, Cs1City } from '../core/types.ts';
import { MapView } from './MapView.tsx';
import { MappingTable } from './MappingTable.tsx';
import { ReportPanel } from './ReportPanel.tsx';
import { buildScene } from './scene.ts';
import type { SegInfo } from './scene.ts';

const DEFAULT_SIZE = 3000;
const STORE = 'cs1conv.ui.v1';

interface Loaded { city: Cs1City; fileName: string; bytes: number }

function loadPrefs(): { overrides?: Record<string, MappingOverride>; crops?: Record<string, Crop> } {
  try { return JSON.parse(localStorage.getItem(STORE) ?? '{}'); } catch { return {}; }
}
function savePrefs(p: ReturnType<typeof loadPrefs>) {
  try { localStorage.setItem(STORE, JSON.stringify(p)); } catch { /* storage unavailable */ }
}

export function App() {
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [crop, setCrop] = useState<Crop>({ centerX: 0, centerZ: 0, size: DEFAULT_SIZE });
  const [committed, setCommitted] = useState<Crop>(crop);
  const [overrides, setOverrides] = useState<Record<string, MappingOverride>>({});
  const [tab, setTab] = useState<'mapping' | 'report' | 'rb'>('mapping');
  const [showSkipped, setShowSkipped] = useState(false);
  const [showTerrain, setShowTerrain] = useState(true);
  const [dragOver, setDragOver] = useState(false);
  const [fitKey, setFitKey] = useState(0);

  const open = useCallback(async (file: File) => {
    setBusy(`Reading ${file.name}…`);
    setError(null);
    try {
      const bytes = new Uint8Array(await file.arrayBuffer());
      const city = await readCityFile(bytes);
      const key = city.sha256 ?? file.name;
      const prefs = loadPrefs();
      const c = prefs.crops?.[key] ?? (() => { const { roadSegments: _r, ...a } = autoCrop(city, DEFAULT_SIZE); return a; })();
      setOverrides(prefs.overrides ?? {});
      setCrop(c);
      setCommitted(c);
      setLoaded({ city, fileName: file.name, bytes: bytes.length });
      setFitKey(k => k + 1);
    } catch (e) {
      setError(`Could not read ${file.name}: ${e instanceof Error ? e.message : e}`);
    } finally {
      setBusy(null);
    }
  }, []);

  // ?open=<url> loads a file from a URL (the dev server serves local samples at /samples/).
  useEffect(() => {
    const src = new URLSearchParams(location.search).get('open');
    if (!src) return;
    setBusy(`Fetching ${src}…`);
    fetch(src)
      .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.blob(); })
      .then(b => open(new File([b], src.split('/').pop() || 'city.cs1x')))
      .catch(e => { setError(`Could not fetch ${src}: ${e instanceof Error ? e.message : e}`); setBusy(null); });
  }, [open]);

  const scene = useMemo(() => (loaded ? buildScene(loaded.city) : null), [loaded]);
  const pkg: BuiltPackage | null = useMemo(() => (loaded ? buildPackage(loaded.city, { crop: committed, overrides }) : null), [loaded, committed, overrides]);
  const stats = useMemo(() => (pkg ? mappingStats(pkg.mapping) : null), [pkg]);

  useEffect(() => {
    if (!loaded) return;
    const prefs = loadPrefs();
    savePrefs({ ...prefs, overrides, crops: { ...prefs.crops, [loaded.city.sha256 ?? loaded.fileName]: committed } });
  }, [loaded, overrides, committed]);

  const onCrop = useCallback((c: Crop, done: boolean) => {
    const cc = clampCrop(c, loaded?.city.manifest.mapSize);
    setCrop(cc);
    if (done) setCommitted(cc);
  }, [loaded]);

  const describe = useCallback((s: SegInfo) => {
    const row = pkg?.mapping.find(r => r.netInfo === s.base);
    const ps = pkg?.plan.segments.find(p => p.id === s.seg.id);
    const t = row?.target;
    const target = !t ? '' : t.mode === 'rb' ? `→ RB ${row?.rb ? `${row.rb.cs1Width}→${row.rb.rbWidth} m` : ''}` : t.mode === 'vanilla' ? `→ ${t.name}` : `→ skipped: ${t.reason}`;
    return [
      s.seg.info,
      `segment ${s.seg.id} · ${s.seg.elevationKind ?? 'ground'} · ${s.seg.length?.toFixed(0)} m${s.seg.inverted ? ' · inverted' : ''}`,
      target,
      ps ? `in plan as ${ps.prefab}${pkg?.plan.prefabs[ps.prefab]?.imt ? ` (IMT ${pkg.plan.prefabs[ps.prefab].imt})` : ''}` : 'not in the plan',
    ].filter(Boolean);
  }, [pkg]);

  const download = (what: 'package' | 'rb') => {
    if (!pkg || !loaded) return;
    const files = packageFiles(pkg).filter(f => what === 'package' || f.path.startsWith('roadbuilder/'));
    const zip = zipSync(Object.fromEntries(files.map(f => [what === 'rb' ? f.path.replace(/^roadbuilder\//, '') : f.path, strToU8(f.text)])));
    const name = `${(loaded.city.manifest.city?.saveName ?? 'city').replace(/[^\w.-]+/g, '_')}-${what === 'rb' ? 'roadbuilder' : 'cs1conv-package'}.zip`;
    const url = URL.createObjectURL(new Blob([zip as Uint8Array<ArrayBuffer>], { type: 'application/zip' }));
    const a = document.createElement('a');
    a.href = url; a.download = name; a.click();
    setTimeout(() => URL.revokeObjectURL(url), 5000);
  };

  const onDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const f = e.dataTransfer.files[0];
    if (f) void open(f);
  };
  const picker = (
    <label className="btn primary">
      Open .cs1x…
      <input type="file" accept=".cs1x,.json,.zip" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void open(f); e.target.value = ''; }} />
    </label>
  );
  const cv = validateCrop(crop, loaded?.city.manifest.mapSize);

  return (
    <div className={`app ${dragOver ? 'drag' : ''}`} onDragOver={e => { e.preventDefault(); setDragOver(true); }} onDragLeave={e => { if (e.currentTarget === e.target) setDragOver(false); }} onDrop={onDrop}>
      <header>
        <div className="brand"><b>cs1conv</b><span>CS1 → CS2 road converter</span></div>
        {loaded && (
          <div className="file">
            <b>{loaded.city.manifest.city?.name}</b> <span className="muted">({loaded.city.manifest.city?.saveName}) · {loaded.fileName} · {(loaded.bytes / 1e6).toFixed(1)} MB · {loaded.city.source === 'm0-dump' ? 'M0 dump' : '.cs1x'}</span>
          </div>
        )}
        <div className="spacer" />
        {picker}
      </header>
      {error && <div className="error">{error}</div>}
      {!loaded ? (
        <div className="empty">
          <div className="drop">
            <h1>Drop a <code>.cs1x</code> here</h1>
            <p>Everything runs in this browser tab; nothing is uploaded.</p>
            <p className="muted">The M0 network dump (<code>network-*.json</code>) works too, without terrain.</p>
            {busy ? <p>{busy}</p> : picker}
          </div>
        </div>
      ) : (
        <main>
          <section className="left">
            {scene && <MapView scene={scene} crop={crop} onCrop={onCrop} showSkipped={showSkipped} showTerrain={showTerrain} fitKey={fitKey} describe={describe} />}
            <div className="mapbar">
              <label>Centre X <input type="number" value={crop.centerX} step={50} onChange={e => onCrop({ ...crop, centerX: +e.target.value }, true)} /></label>
              <label>Z <input type="number" value={crop.centerZ} step={50} onChange={e => onCrop({ ...crop, centerZ: +e.target.value }, true)} /></label>
              <label>Size <input type="number" value={crop.size} step={250} min={100} max={CS2_MAX_CROP} onChange={e => onCrop({ ...crop, size: +e.target.value }, true)} /> m</label>
              <button onClick={() => { const { roadSegments: _r, ...a } = autoCrop(loaded.city, crop.size); onCrop(a, true); }} title="Move the crop to the square with the most road segments">Auto</button>
              <label className="check"><input type="checkbox" checked={showTerrain} onChange={e => setShowTerrain(e.target.checked)} disabled={!loaded.city.terrain} /> Terrain</label>
              <label className="check"><input type="checkbox" checked={showSkipped} onChange={e => setShowSkipped(e.target.checked)} /> Skipped networks</label>
              <span className="muted">Drag the square to move, corners to resize; drag elsewhere to pan, wheel to zoom.</span>
              {cv.warnings.map(w => <span key={w} className="warn">{w}</span>)}
            </div>
          </section>
          <section className="right">
            {pkg && stats && (
              <>
                <div className="stats">
                  <Stat n={pkg.plan.stats.segments} label="segments" />
                  <Stat n={pkg.plan.stats.nodes} label="nodes" />
                  <Stat n={pkg.plan.stats.rbConfigs} label="RB configs" />
                  <Stat n={pkg.plan.stats.skipped} label="skipped" />
                  <div className="stat wide">
                    <div>NetInfos in crop: <b>{stats.netInfos.inCrop}</b></div>
                    <div className="muted">RB {stats.rb.inCrop} · vanilla {stats.vanilla.inCrop} · not placeable {stats.skippedNotPlaceable.inCrop} · unmapped {stats.skippedUnmapped.inCrop}</div>
                  </div>
                </div>
                <div className="downloads">
                  <button className="primary" disabled={!!pkg.errors.length || !pkg.plan.segments.length} onClick={() => download('package')}>Download package (.zip)</button>
                  <button disabled={!pkg.rbConfigs.length} onClick={() => download('rb')}>Road Builder configs only</button>
                  {pkg.errors.map(e => <span key={e} className="warn">{e}</span>)}
                </div>
                <p className="hint muted">Unzip the Road Builder configs into <code>%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\</code> before starting CS2 (or run the CLI with <code>--install-rb</code>).</p>
                <nav className="tabs">
                  <button className={tab === 'mapping' ? 'on' : ''} onClick={() => setTab('mapping')}>Mapping ({pkg.mapping.filter(r => r.segmentsInCrop).length})</button>
                  <button className={tab === 'rb' ? 'on' : ''} onClick={() => setTab('rb')}>RB configs ({pkg.rbVariants.length})</button>
                  <button className={tab === 'report' ? 'on' : ''} onClick={() => setTab('report')}>Report ({pkg.report.items.length})</button>
                </nav>
                <div className="panel">
                  {tab === 'mapping' && <MappingTable rows={pkg.mapping} overrides={overrides} onOverride={(name, o) => setOverrides(prev => { const n = { ...prev }; if (o.mode === 'auto') delete n[name]; else n[name] = o; return n; })} />}
                  {tab === 'rb' && <RbList pkg={pkg} />}
                  {tab === 'report' && <ReportPanel report={pkg.report} />}
                </div>
              </>
            )}
          </section>
        </main>
      )}
      {busy && loaded && <div className="busy">{busy}</div>}
    </div>
  );
}

function Stat({ n, label }: { n: number; label: string }) {
  return <div className="stat"><b>{n.toLocaleString()}</b><span>{label}</span></div>;
}

function RbList({ pkg }: { pkg: BuiltPackage }) {
  return (
    <div className="rblist">
      {pkg.rbVariants.sort((a, b) => b.segments - a.segments).map(v => (
        <details key={v.id}>
          <summary>
            <b>{v.title}</b>{v.imtOverrides && <span className="tag imt">IMT {v.imtOverrides}</span>}
            <span className="tag">{v.category}</span>
            <span className={Math.abs(v.diff) >= 0.5 ? 'tag loss' : 'tag'}>{v.cs1Width} → {v.rbWidth} m</span>
            <span className="muted">{v.segments} seg · {v.key}</span>
          </summary>
          <div className="lanes">{v.rbLanes.map((l, i) => <code key={i}>{l}</code>)}</div>
          {v.notes.length > 0 && <ul>{v.notes.map((n, i) => <li key={i}>{n}</li>)}</ul>}
          <div className="muted mono">{v.id}</div>
        </details>
      ))}
    </div>
  );
}
