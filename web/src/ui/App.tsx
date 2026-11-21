import { strToU8, zipSync } from 'fflate';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { CS2_MAX_CROP, clampCrop, validateCrop } from '../core/crop.ts';
import { ReadError, readCityFile } from '../core/cs1x.ts';
import type { MappingOverride } from '../core/mapping.ts';
import { DEFAULT_ZONING } from '../core/m2/zoning.ts';
import type { ZoningOptions } from '../core/m2/zoning.ts';
import type { AssetOverrides, Cs2Catalogue, FallbackOverrides } from '../core/m3/buildings.ts';
import { slimCatalogue } from '../core/m3/catalogue.ts';
import { AssetsTable } from './AssetsTable.tsx';
import { Help } from './Help.tsx';
import { TerrainPreview } from './TerrainPreview.tsx';
import { autoCrop, mappingStats, packageFiles } from '../core/plan.ts';
import type { BuildOptions, BuiltPackage } from '../core/plan.ts';
import { rbOnlyReadme } from '../core/readme.ts';
import { sectionCheck } from '../core/summary.ts';
import { syntheticCity } from '../core/synthetic.ts';
import type { Crop } from '../core/types.ts';
import { Landing } from './Landing.tsx';
import type { LoadError } from './Landing.tsx';
import { MapView } from './MapView.tsx';
import type { MapHandle } from './MapView.tsx';
import { MappingTable } from './MappingTable.tsx';
import { ConversionOptions, ConversionPanel, FileCheck, InstallHelp } from './Panels.tsx';
import type { ConvOptions } from './Panels.tsx';
import { ReportPanel } from './ReportPanel.tsx';
import { buildScene } from './scene.ts';
import type { SegInfo } from './scene.ts';
import { usePackage } from './usePackage.ts';
import type { Source } from './usePackage.ts';

const DEFAULT_SIZE = 3000;
const STORE = 'cs1conv.ui.v1';

interface Loaded extends Source { fileName: string; size: number }

const DEFAULT_CONV: ConvOptions = { zoning: DEFAULT_ZONING, traffic: true, transit: true, water: true, buildings: true, districts: true, objects: true, surfaces: true };

function loadPrefs(): { overrides?: Record<string, MappingOverride>; crops?: Record<string, Crop>; conv?: ConvOptions; assetOverrides?: AssetOverrides; assetFallbacks?: FallbackOverrides } {
  try { return JSON.parse(localStorage.getItem(STORE) ?? '{}'); } catch { return {}; }
}
function savePrefs(p: ReturnType<typeof loadPrefs>) {
  try { localStorage.setItem(STORE, JSON.stringify(p)); } catch { /* storage unavailable */ }
}

type Tab = 'overview' | 'mapping' | 'rb' | 'assets' | 'report';
const TABS: { id: Tab; label: (p: BuiltPackage) => string }[] = [
  { id: 'overview', label: () => 'Overview' },
  { id: 'mapping', label: p => `Mapping (${p.mapping.filter(r => r.segmentsInCrop).length})` },
  { id: 'rb', label: p => `Road types (${p.rbVariants.length})` },
  { id: 'assets', label: p => `Assets (${p.plan.buildings?.items.length ?? 0})` },
  { id: 'report', label: p => `Report (${p.report.items.length})` },
];

function toLoadError(e: unknown, name: string): LoadError {
  if (e instanceof ReadError) return { title: `Can't open ${name}`, message: e.message, hint: e.hint };
  return { title: `Can't open ${name}`, message: e instanceof Error ? e.message : String(e), hint: 'If this is a .cs1x from the exporter, export the city again and retry.' };
}

export function App() {
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<LoadError | null>(null);
  const [crop, setCrop] = useState<Crop>({ centerX: 0, centerZ: 0, size: DEFAULT_SIZE });
  const [committed, setCommitted] = useState<Crop>(crop);
  const [overrides, setOverrides] = useState<Record<string, MappingOverride>>({});
  const [conv, setConv] = useState<ConvOptions>(() => ({ ...DEFAULT_CONV, ...loadPrefs().conv, zoning: { ...DEFAULT_ZONING, ...loadPrefs().conv?.zoning } as ZoningOptions }));
  const [showZones, setShowZones] = useState(true);
  const [showTransit, setShowTransit] = useState(true);
  const [showBuildings, setShowBuildings] = useState(false);
  const [showDistricts, setShowDistricts] = useState(true);
  const [assetOverrides, setAssetOverrides] = useState<AssetOverrides>(() => loadPrefs().assetOverrides ?? {});
  const [assetFallbacks, setAssetFallbacks] = useState<FallbackOverrides>(() => loadPrefs().assetFallbacks ?? {});
  const [catalogue, setCatalogue] = useState<Cs2Catalogue | null>(null);
  const [catalogueSource, setCatalogueSource] = useState<string | null>(null);
  const catalogueInput = useRef<HTMLInputElement>(null);

  // The bundled slim catalogue (public/cs2-catalogue.json), if the site has one.
  useEffect(() => {
    fetch('./cs2-catalogue.json')
      .then(r => (r.ok ? r.json() : null))
      .then(c => { if (c && Array.isArray(c.prefabs)) { setCatalogue(c); setCatalogueSource('bundled with this site'); } })
      .catch(() => { /* no bundled catalogue */ });
  }, []);
  const loadCatalogueFile = useCallback(async (file: File) => {
    try {
      const j = JSON.parse((await file.text()).replace(/^\uFEFF/, ''));
      if (!Array.isArray(j.prefabs)) throw new Error('no prefabs array');
      setCatalogue(j.format === 'cs2-catalogue-slim' ? j : slimCatalogue(j));
      setCatalogueSource(file.name);
      setError(null);
    } catch (e) {
      setError({ title: `Can't use ${file.name} as a CS2 catalogue`, message: e instanceof Error ? e.message : String(e), hint: 'Use the cs2-catalogue.json written by node buildings/run.mjs catalogue.' });
    }
  }, []);
  const [tab, setTab] = useState<Tab>('overview');
  const [help, setHelp] = useState<null | 'guide' | 'converts' | 'faq' | 'about'>(null);
  const [showSkipped, setShowSkipped] = useState(false);
  const [showTerrain, setShowTerrain] = useState(true);
  const [dragOver, setDragOver] = useState(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const map = useRef<MapHandle>(null);
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});

  const accept = useCallback((l: Loaded) => {
    const key = l.city.sha256 ?? l.fileName;
    const prefs = loadPrefs();
    const c = prefs.crops?.[key] ?? (() => { const { roadSegments: _r, ...a } = autoCrop(l.city, DEFAULT_SIZE); return a; })();
    setOverrides(prefs.overrides ?? {});
    setCrop(c);
    setCommitted(c);
    setLoaded(l);
    setError(null);
    setTab('overview');
  }, []);

  const open = useCallback(async (file: File) => {
    if (/catalogue.*\.json$/i.test(file.name)) { await loadCatalogueFile(file); return; }
    setBusy(`Reading ${file.name}…`);
    setError(null);
    try {
      const buf = await file.arrayBuffer();
      const city = await readCityFile(new Uint8Array(buf));
      if (!city.segments.length) throw new ReadError('not-cs1x', 'This export has no road network.', 'Load the city in CS1 before exporting, and check that the network section did not fail.');
      accept({ city, bytes: buf, fileName: file.name, size: buf.byteLength });
    } catch (e) {
      setError(toLoadError(e, file.name));
    } finally {
      setBusy(null);
    }
  }, [accept, loadCatalogueFile]);

  // ?open=<url> (dev server serves local samples at /samples/) and ?synthetic=<n> (performance check).
  useEffect(() => {
    const q = new URLSearchParams(location.search);
    const syn = Number(q.get('synthetic'));
    if (syn > 1) {
      setBusy('Generating a synthetic city…');
      setTimeout(() => {
        const n = Math.min(180, Math.round(syn));
        const city = syntheticCity(n);
        accept({ city, synthetic: n, fileName: `synthetic-${n}`, size: 0 });
        setBusy(null);
      }, 0);
      return;
    }
    const src = q.get('open');
    if (!src) return;
    setBusy(`Fetching ${src}…`);
    fetch(src)
      .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.blob(); })
      .then(b => open(new File([b], src.split('/').pop() || 'city.cs1x')))
      .catch(e => { setError({ title: `Can't fetch ${src}`, message: e instanceof Error ? e.message : String(e) }); setBusy(null); });
  }, [open, accept]);

  const scene = useMemo(() => (loaded ? buildScene(loaded.city) : null), [loaded]);
  const check = useMemo(() => (loaded ? sectionCheck(loaded.city) : null), [loaded]);
  const opts: BuildOptions | null = useMemo(() => (loaded ? {
    crop: committed, overrides, zoning: conv.zoning, traffic: conv.traffic, transit: conv.transit, water: conv.water,
    buildings: conv.buildings, districts: conv.districts, objects: conv.objects, surfaces: conv.surfaces, assetOverrides, assetFallbacks,
  } : null), [loaded, committed, overrides, conv, assetOverrides, assetFallbacks]);
  const { pkg, busy: building, ms, error: buildError, zip: buildZip } = usePackage(loaded, opts, catalogue);
  const [zipping, setZipping] = useState(false);
  const stats = useMemo(() => (pkg ? mappingStats(pkg.mapping) : null), [pkg]);

  useEffect(() => {
    if (!loaded || loaded.synthetic) return;
    const prefs = loadPrefs();
    savePrefs({ ...prefs, overrides, conv, assetOverrides, assetFallbacks, crops: { ...prefs.crops, [loaded.city.sha256 ?? loaded.fileName]: committed } });
  }, [loaded, overrides, committed, conv, assetOverrides, assetFallbacks]);

  const onCrop = useCallback((c: Crop, done: boolean) => {
    const cc = clampCrop(c, loaded?.city.manifest.mapSize);
    setCrop(cc);
    if (done) setCommitted(cc);
  }, [loaded]);

  const describe = useCallback((s: SegInfo) => {
    const row = pkg?.mapping.find(r => r.netInfo === s.base);
    const ps = pkg?.plan.segments.find(p => p.id === s.seg.id);
    const t = row?.target;
    const target = !t ? '' : t.mode === 'rb' ? `→ Road Builder ${row?.rb ? `${row.rb.cs1Width} → ${row.rb.rbWidth} m` : ''}` : t.mode === 'vanilla' ? `→ ${t.name}` : `→ skipped: ${t.reason}`;
    return [
      s.seg.info,
      `segment ${s.seg.id} · ${s.seg.elevationKind ?? 'ground'} · ${s.seg.length?.toFixed(0)} m${s.seg.inverted ? ' · inverted' : ''}`,
      target,
      ps ? `in the plan as ${ps.prefab}${pkg?.plan.prefabs[ps.prefab]?.imt ? ` (IMT ${pkg.plan.prefabs[ps.prefab].imt})` : ''}` : 'not in the plan',
    ].filter(Boolean);
  }, [pkg]);

  const baseName = (loaded?.city.manifest.city?.saveName ?? loaded?.fileName ?? 'city').replace(/[^\w.-]+/g, '_');
  const rbZipName = `${baseName}-roadbuilder.zip`;
  const save = (zip: Uint8Array, name: string) => {
    const url = URL.createObjectURL(new Blob([zip as Uint8Array<ArrayBuffer>], { type: 'application/zip' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
  };
  const download = async (what: 'package' | 'rb') => {
    if (!pkg) return;
    if (what === 'rb') {
      const files = [...packageFiles(pkg).filter(f => f.path.startsWith('roadbuilder/')).map(f => [f.path.slice('roadbuilder/'.length), strToU8(f.text)] as const), ['README.txt', strToU8(rbOnlyReadme(pkg))] as const];
      save(zipSync(Object.fromEntries(files)), rbZipName);
      return;
    }
    // The full package (with the 4096² terrain) is built and zipped in the worker: a few seconds for a large crop.
    setZipping(true);
    try { save(await buildZip(), `${baseName}-cs1conv-package.zip`); }
    catch (e) { setError({ title: 'Could not build the package', message: e instanceof Error ? e.message : String(e) }); }
    finally { setZipping(false); }
  };

  const onDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setDragOver(false);
    const f = e.dataTransfer.files[0];
    if (f) void open(f);
  };
  const pick = () => fileInput.current?.click();
  const cv = validateCrop(crop, loaded?.city.manifest.mapSize);

  const onTabKey = (e: React.KeyboardEvent) => {
    const i = TABS.findIndex(t => t.id === tab);
    const n = e.key === 'ArrowRight' ? (i + 1) % TABS.length : e.key === 'ArrowLeft' ? (i + TABS.length - 1) % TABS.length : e.key === 'Home' ? 0 : e.key === 'End' ? TABS.length - 1 : -1;
    if (n < 0) return;
    e.preventDefault();
    setTab(TABS[n].id);
    tabRefs.current[TABS[n].id]?.focus();
  };

  return (
    <div className={`app ${dragOver ? 'drag' : ''}`} onDragOver={e => { e.preventDefault(); setDragOver(true); }} onDragLeave={e => { if (!e.currentTarget.contains(e.relatedTarget as Node)) setDragOver(false); }} onDrop={onDrop}>
      <a href="#main" className="skip">Skip to content</a>
      <header>
        <div className="brand"><b>cs1conv</b><span>Cities: Skylines → Cities: Skylines II</span></div>
        {loaded && (
          <div className="file" title={loaded.fileName}>
            <b>{loaded.city.manifest.city?.name}</b> <span className="muted">{loaded.city.manifest.city?.saveName} · {loaded.size ? fmtBytes(loaded.size) : 'generated'}{loaded.city.source === 'm0-dump' ? ' · M0 dump' : ''}</span>
          </div>
        )}
        <div className="spacer" />
        <button onClick={() => setHelp('guide')} aria-haspopup="dialog">Help</button>
        {loaded && <button onClick={pick}>Open another file…</button>}
        <input ref={fileInput} type="file" accept=".cs1x,.json,.zip" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void open(f); e.target.value = ''; }} />
        <input ref={catalogueInput} type="file" accept=".json" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void loadCatalogueFile(f); e.target.value = ''; }} />
      </header>
      {loaded && error && (
        <div className="error-bar" role="alert">
          <b>{error.title}.</b> {error.message} {error.hint && <span className="muted">{error.hint}</span>}
          <button onClick={() => setError(null)} aria-label="Dismiss">✕</button>
        </div>
      )}
      {!loaded ? (
        <main id="main" className="landing-wrap"><Landing busy={busy} error={error} onPick={pick} onHelp={s => setHelp(s)} /></main>
      ) : (
        <main id="main" className="work">
          <section className="left" aria-label="Map and crop">
            {scene && <MapView ref={map} scene={scene} crop={crop} onCrop={onCrop} showSkipped={showSkipped} showTerrain={showTerrain} showZones={showZones && conv.zoning.enabled} showTransit={showTransit && conv.transit} showBuildings={showBuildings} showDistricts={showDistricts} describe={describe} />}
            <div className="mapbar">
              <fieldset>
                <legend className="sr-only">Crop square (CS1 metres)</legend>
                <label>Centre X <input type="number" value={crop.centerX} step={50} onChange={e => onCrop({ ...crop, centerX: +e.target.value }, true)} /></label>
                <label>Z <input type="number" value={crop.centerZ} step={50} onChange={e => onCrop({ ...crop, centerZ: +e.target.value }, true)} /></label>
                <label>Size <input type="number" value={crop.size} step={250} min={100} max={CS2_MAX_CROP} onChange={e => onCrop({ ...crop, size: +e.target.value }, true)} /> m</label>
                <button onClick={() => { const { roadSegments: _r, ...a } = autoCrop(loaded.city, crop.size); onCrop(a, true); map.current?.fitCrop(); }} title="Move the crop to the square with the most roads">Auto</button>
                <button onClick={() => { onCrop({ ...crop, size: CS2_MAX_CROP }, true); }} title="Largest crop CS2 can hold">Max</button>
              </fieldset>
              <label className="check"><input type="checkbox" checked={showTerrain} onChange={e => setShowTerrain(e.target.checked)} disabled={!loaded.city.terrain} /> Terrain</label>
              {!!loaded.city.zones && <label className="check"><input type="checkbox" checked={showZones} onChange={e => setShowZones(e.target.checked)} /> Zones</label>}
              {!!loaded.city.transit?.lines.length && <label className="check"><input type="checkbox" checked={showTransit} onChange={e => setShowTransit(e.target.checked)} /> Transit</label>}
              {!!loaded.city.buildings && <label className="check"><input type="checkbox" checked={showBuildings} onChange={e => setShowBuildings(e.target.checked)} /> Buildings</label>}
              {!!loaded.city.districts?.districts.length && <label className="check"><input type="checkbox" checked={showDistricts} onChange={e => setShowDistricts(e.target.checked)} /> Districts</label>}
              <label className="check"><input type="checkbox" checked={showSkipped} onChange={e => setShowSkipped(e.target.checked)} /> Skipped networks</label>
              <span className="muted hint-keys">Drag the square to move it, corners to resize. Keyboard: focus the map, arrows move, +/− resize.</span>
              {cv.warnings.map(w => <span key={w} className="warn">{w}</span>)}
            </div>
          </section>
          <section className="right" aria-label="Package">
            <div className="sr-only" aria-live="polite">{pkg ? `Plan updated: ${pkg.plan.stats.segments} segments, ${pkg.plan.stats.rbConfigs} road types, ${pkg.plan.stats.skipped} skipped.` : ''}</div>
            {check && check.failed.length > 0 && (
              <div className="error-card" role="alert">
                <b>Part of this export failed: {check.failed.join(', ')}.</b>
                <p>Everything else converts normally. Export the city again; the File check in Overview has the exporter's message.</p>
              </div>
            )}
            {!pkg ? <p className="busy-inline"><span className="spinner" aria-hidden="true" />{buildError ?? 'Building the package…'}</p> : stats && (
              <>
                <div className={`stats ${building ? 'stale' : ''}`} aria-busy={building}>
                  <Stat n={pkg.plan.stats.segments} label="segments" />
                  <Stat n={pkg.plan.stats.nodes} label="nodes" />
                  <Stat n={pkg.plan.stats.rbConfigs} label="road types" />
                  <Stat n={pkg.plan.stats.skipped} label="skipped" />
                  <div className="stat wide">
                    <div>NetInfos in crop: <b>{stats.netInfos.inCrop}</b>{building && <span className="spinner small" aria-label="updating" />}</div>
                    <div className="muted">Road Builder {stats.rb.inCrop} · vanilla {stats.vanilla.inCrop} · not placeable {stats.skippedNotPlaceable.inCrop} · unmapped {stats.skippedUnmapped.inCrop}</div>
                  </div>
                </div>
                <div className="downloads">
                  <button className="primary" disabled={!!pkg.errors.length || !pkg.plan.segments.length || zipping} aria-busy={zipping} onClick={() => void download('package')}>{zipping ? <><span className="spinner small" aria-hidden="true" /> Building the package…</> : 'Download package (.zip)'}</button>
                  <button disabled={!pkg.rbConfigs.length} onClick={() => void download('rb')}>Road Builder configs only</button>
                  {zipping && <span className="muted small" role="status">Adding the terrain (two 4096² heightmaps) and compressing…</span>}
                  {pkg.errors.map(e => <span key={e} className="warn" role="alert">{e}</span>)}
                  {!pkg.plan.segments.length && !pkg.errors.length && <span className="warn">The crop holds no roads. Move it over the city or press Auto.</span>}
                </div>
                <div role="tablist" aria-label="Package details" className="tabs" onKeyDown={onTabKey}>
                  {TABS.map(t => (
                    <button key={t.id} role="tab" id={`tab-${t.id}`} aria-selected={tab === t.id} aria-controls={`panel-${t.id}`} tabIndex={tab === t.id ? 0 : -1}
                      className={tab === t.id ? 'on' : ''} ref={el => { tabRefs.current[t.id] = el; }} onClick={() => setTab(t.id)}>{t.label(pkg)}</button>
                  ))}
                </div>
                <div className="panel" role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} tabIndex={0}>
                  {tab === 'overview' && (
                    <div className="overview">
                      <h3>What converts</h3>
                      <ConversionPanel summary={pkg.summary} />
                      <h3>Terrain of the crop</h3>
                      <TerrainPreview city={loaded.city} crop={committed} seaLevel={pkg.plan.water?.apply?.seaLevel ?? pkg.plan.water?.seaLevel ?? null} />
                      <h3>Options</h3>
                      <ConversionOptions value={conv} onChange={setConv} city={loaded.city} />
                      <h3>Install</h3>
                      <InstallHelp zipName={rbZipName} requires={pkg.plan.requires} />
                      {check && <><h3>File check</h3><FileCheck {...check} /></>}
                      <p className="small muted">Package built in {ms.toFixed(0)} ms{loaded.synthetic ? ' (synthetic city)' : ''}.</p>
                    </div>
                  )}
                  {tab === 'mapping' && <MappingTable rows={pkg.mapping} overrides={overrides} onOverride={(name, o) => setOverrides(prev => { const n = { ...prev }; if (o.mode === 'auto') delete n[name]; else n[name] = o; return n; })} />}
                  {tab === 'rb' && <RbList pkg={pkg} />}
                  {tab === 'assets' && <AssetsTable matches={pkg.assetMatches ?? []} overrides={assetOverrides} catalogue={catalogue} catalogueSource={catalogueSource} hasBuildings={!!loaded.city.buildings && !!loaded.city.assets}
                    onLoadCatalogue={() => catalogueInput.current?.click()}
                    onOverride={(cs1, v) => setAssetOverrides(prev => { const n = { ...prev }; if (v) n[cs1] = v; else delete n[cs1]; return n; })}
                    fallbacks={assetFallbacks}
                    onFallback={(cs1, v) => setAssetFallbacks(prev => { const n = { ...prev }; if (v) n[cs1] = v; else delete n[cs1]; return n; })} />}
                  {tab === 'report' && <ReportPanel report={pkg.report} />}
                </div>
              </>
            )}
          </section>
        </main>
      )}
      {busy && loaded && <div className="busy" role="status">{busy}</div>}
      {help && <Help initial={help} onClose={() => setHelp(null)} />}
    </div>
  );
}

const fmtBytes = (b: number) => (b >= 1e6 ? `${(b / 1e6).toFixed(1)} MB` : `${Math.max(1, Math.round(b / 1e3))} kB`);

function Stat({ n, label }: { n: number; label: string }) {
  return <div className="stat"><b>{n.toLocaleString('en')}</b><span>{label}</span></div>;
}

function RbList({ pkg }: { pkg: BuiltPackage }) {
  return (
    <div className="rblist">
      {[...pkg.rbVariants].sort((a, b) => b.segments - a.segments).map(v => (
        <details key={v.id}>
          <summary>
            <b>{v.title}</b>{v.imtOverrides && <span className="tag imt">IMT {v.imtOverrides}</span>}
            <span className="tag">{v.category}</span>
            <span className={Math.abs(v.diff) >= 0.5 ? 'tag loss' : 'tag'}>{v.cs1Width} → {v.rbWidth} m</span>
            <span className="muted small">{v.segments} seg</span>
          </summary>
          <div className="lanes">{v.rbLanes.map((l, i) => <code key={i}>{l}</code>)}</div>
          {v.notes.length > 0 && <ul>{v.notes.map((n, i) => <li key={i}>{n}</li>)}</ul>}
          <div className="muted mono small">{v.id}</div>
        </details>
      ))}
    </div>
  );
}
