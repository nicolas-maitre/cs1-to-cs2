import { strToU8, zipSync } from 'fflate';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { CS2_MAX_CROP, clampCrop, validateCrop } from '../core/crop.ts';
import { ReadError, readCityFile } from '../core/cs1x.ts';
import type { MappingOverride } from '../core/mapping.ts';
import { DEFAULT_ZONING } from '../core/m2/zoning.ts';
import type { ZoningOptions } from '../core/m2/zoning.ts';
import type { AssetOverrides, Cs2Catalogue, FallbackOverrides } from '../core/m3/buildings.ts';
import { slimCatalogue } from '../core/m3/catalogue.ts';
import type { Cs2RoadCatalogue } from '../core/vanilla.ts';
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
import { LangSwitch, useI18n } from './i18n.tsx';
import type { MessageKey, T } from '../core/i18n/index.ts';
import type { Source } from './usePackage.ts';

const DEFAULT_SIZE = 3000;
const STORE = 'cs1conv.ui.v1';

interface Loaded extends Source { fileName: string; size: number }

const DEFAULT_CONV: ConvOptions = { preferVanilla: true, zoning: DEFAULT_ZONING, traffic: true, transit: true, water: true, buildings: true, districts: true, objects: true, surfaces: true };

function loadPrefs(): { overrides?: Record<string, MappingOverride>; crops?: Record<string, Crop>; conv?: ConvOptions; assetOverrides?: AssetOverrides; assetFallbacks?: FallbackOverrides } {
  try { return JSON.parse(localStorage.getItem(STORE) ?? '{}'); } catch { return {}; }
}
function savePrefs(p: ReturnType<typeof loadPrefs>) {
  try { localStorage.setItem(STORE, JSON.stringify(p)); } catch { /* storage unavailable */ }
}

type Tab = 'overview' | 'mapping' | 'rb' | 'assets' | 'report';
const TABS: { id: Tab; label: (p: BuiltPackage, t: T) => string }[] = [
  { id: 'overview', label: (_p, t) => t('app.tab.overview') },
  { id: 'mapping', label: (p, t) => t('app.tab.mapping', { n: p.mapping.filter(r => r.segmentsInCrop).length }) },
  { id: 'rb', label: (p, t) => t('app.tab.rb', { n: p.rbVariants.length }) },
  { id: 'assets', label: (p, t) => t('app.tab.assets', { n: p.plan.buildings?.items.length ?? 0 }) },
  { id: 'report', label: (p, t) => t('app.tab.report', { n: p.report.items.length }) },
];

function toLoadError(e: unknown, name: string, t: T): LoadError {
  const title = t('app.cantOpen', { name });
  if (e instanceof ReadError) {
    return e.i18n
      ? { title, message: t(e.i18n.msg as MessageKey, e.i18n.vars), hint: t(e.i18n.hint as MessageKey) }
      : { title, message: e.message, hint: e.hint };
  }
  return { title, message: e instanceof Error ? e.message : String(e), hint: t('app.cantOpenHint') };
}

export function App() {
  const { t, tk, n, lang } = useI18n();
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
  const [roads, setRoads] = useState<Cs2RoadCatalogue | null>(null);
  // The bundled vanilla CS2 road list (public/cs2-roads.json) for "Prefer vanilla roads".
  useEffect(() => {
    fetch('./cs2-roads.json')
      .then(r => (r.ok ? r.json() : null))
      .then(c => { if (c && Array.isArray(c.roads)) setRoads(c); })
      .catch(() => { /* no bundled road list: every road is a Road Builder road */ });
  }, []);
  const [catalogueSource, setCatalogueSource] = useState<string | null>(null);
  const catalogueInput = useRef<HTMLInputElement>(null);

  // The bundled slim catalogue (public/cs2-catalogue.json), if the site has one.
  useEffect(() => {
    fetch('./cs2-catalogue.json')
      .then(r => (r.ok ? r.json() : null))
      .then(c => { if (c && Array.isArray(c.prefabs)) { setCatalogue(c); setCatalogueSource(null); } })
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
      setError({ title: t('app.badCatalogue', { name: file.name }), message: e instanceof Error ? e.message : String(e), hint: t('app.badCatalogueHint') });
    }
  }, [t]);
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
    setBusy(t('app.reading', { name: file.name }));
    setError(null);
    try {
      const buf = await file.arrayBuffer();
      const city = await readCityFile(new Uint8Array(buf));
      if (!city.segments.length) throw new ReadError('not-cs1x', 'This export has no road network.', 'Load the city in CS1 before exporting, and check that the network section did not fail.', { msg: 'app.noNetwork', hint: 'app.noNetworkHint' });
      accept({ city, bytes: buf, fileName: file.name, size: buf.byteLength });
    } catch (e) {
      setError(toLoadError(e, file.name, t));
    } finally {
      setBusy(null);
    }
  }, [accept, loadCatalogueFile, t]);

  // ?open=<url> (dev server serves local samples at /samples/) and ?synthetic=<n> (performance check).
  useEffect(() => {
    const q = new URLSearchParams(location.search);
    const syn = Number(q.get('synthetic'));
    if (syn > 1) {
      setBusy(t('app.generating'));
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
    setBusy(t('app.fetching', { src }));
    fetch(src)
      .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`); return r.blob(); })
      .then(b => open(new File([b], src.split('/').pop() || 'city.cs1x')))
      .catch(e => { setError({ title: t('app.cantFetch', { src }), message: e instanceof Error ? e.message : String(e) }); setBusy(null); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, accept]);

  const scene = useMemo(() => (loaded ? buildScene(loaded.city) : null), [loaded]);
  const check = useMemo(() => (loaded ? sectionCheck(loaded.city) : null), [loaded]);
  const opts: BuildOptions | null = useMemo(() => (loaded ? {
    crop: committed, overrides, roads, preferVanilla: conv.preferVanilla !== false, zoning: conv.zoning, traffic: conv.traffic, transit: conv.transit, water: conv.water,
    buildings: conv.buildings, districts: conv.districts, objects: conv.objects, surfaces: conv.surfaces, depotMode: conv.depotMode, assetOverrides, assetFallbacks, lang,
  } : null), [loaded, committed, overrides, roads, conv, assetOverrides, assetFallbacks, lang]);
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
    const tg = row?.target;
    const target = !tg ? '' : tg.mode === 'rb' ? t('app.tip.rb', { widths: row?.rb ? `${n(row.rb.cs1Width, 1)} → ${n(row.rb.rbWidth, 1)} m` : '' }) : tg.mode === 'vanilla' ? t('app.tip.vanilla', { name: tg.name }) : t('app.tip.skipped', { reason: tg.reason });
    const kind = s.seg.elevationKind ?? 'ground';
    return [
      s.seg.info,
      t('app.tip.segment', { id: String(s.seg.id), kind: tk(`elev.${kind}`, kind), length: Math.round(s.seg.length ?? 0) }) + (s.seg.inverted ? ` · ${t('app.tip.inverted')}` : ''),
      target,
      ps ? t('app.tip.inPlan', { prefab: ps.prefab }) + (pkg?.plan.prefabs[ps.prefab]?.imt ? ` (IMT ${pkg.plan.prefabs[ps.prefab].imt})` : '') : t('app.tip.notInPlan'),
    ].filter(Boolean);
  }, [pkg, t, tk, n]);

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
    catch (e) { setError({ title: t('app.zipFailed'), message: e instanceof Error ? e.message : String(e) }); }
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
      <a href="#main" className="skip">{t('app.skip')}</a>
      <header>
        <div className="brand"><b>cs1conv</b><span>{t('app.tagline')}</span></div>
        {loaded && (
          <div className="file" title={loaded.fileName}>
            <b>{loaded.city.manifest.city?.name}</b> <span className="muted">{loaded.city.manifest.city?.saveName} · {loaded.size ? fmtBytes(loaded.size, n) : t('app.generated')}{loaded.city.source === 'm0-dump' ? ` · ${t('app.m0dump')}` : ''}</span>
          </div>
        )}
        <div className="spacer" />
        <LangSwitch />
        <button onClick={() => setHelp('guide')} aria-haspopup="dialog">{t('app.help')}</button>
        {loaded && <button onClick={pick}>{t('app.openAnother')}</button>}
        <input ref={fileInput} type="file" accept=".cs1x,.json,.zip" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void open(f); e.target.value = ''; }} />
        <input ref={catalogueInput} type="file" accept=".json" hidden onChange={e => { const f = e.target.files?.[0]; if (f) void loadCatalogueFile(f); e.target.value = ''; }} />
      </header>
      {loaded && error && (
        <div className="error-bar" role="alert">
          <b>{error.title}.</b> {error.message} {error.hint && <span className="muted">{error.hint}</span>}
          <button onClick={() => setError(null)} title={t('app.dismiss')}><span aria-hidden="true">✕</span><span className="sr-only">{t('app.dismiss')}</span></button>
        </div>
      )}
      {!loaded ? (
        <main id="main" className="landing-wrap"><Landing busy={busy} error={error} onPick={pick} onHelp={s => setHelp(s)} /></main>
      ) : (
        <main id="main" className="work">
          <h1 className="sr-only">{t('app.workTitle', { city: loaded.city.manifest.city?.name ?? loaded.fileName })}</h1>
          <section className="left" aria-labelledby="h-map">
            <h2 id="h-map" className="sr-only">{t('app.mapAndCrop')}</h2>
            {scene && <MapView ref={map} scene={scene} crop={crop} onCrop={onCrop} showSkipped={showSkipped} showTerrain={showTerrain} showZones={showZones && conv.zoning.enabled} showTransit={showTransit && conv.transit} showBuildings={showBuildings} showDistricts={showDistricts} describe={describe} />}
            <div className="mapbar">
              <fieldset>
                <legend className="sr-only">{t('app.cropLegend')}</legend>
                <label>{t('app.centreX')} <input type="number" value={crop.centerX} step={50} onChange={e => onCrop({ ...crop, centerX: +e.target.value }, true)} /></label>
                <label>{t('app.centreZ')} <input type="number" value={crop.centerZ} step={50} onChange={e => onCrop({ ...crop, centerZ: +e.target.value }, true)} /></label>
                <label>{t('app.size')} <input type="number" value={crop.size} step={250} min={100} max={CS2_MAX_CROP} onChange={e => onCrop({ ...crop, size: +e.target.value }, true)} /> m</label>
                <button onClick={() => { const { roadSegments: _r, ...a } = autoCrop(loaded.city, crop.size); onCrop(a, true); map.current?.fitCrop(); }} title={t('app.autoTitle')}>{t('app.auto')}</button>
                <button onClick={() => { onCrop({ ...crop, size: CS2_MAX_CROP }, true); }} title={t('app.maxTitle')}>{t('app.max')}</button>
              </fieldset>
              <label className="check"><input type="checkbox" checked={showTerrain} onChange={e => setShowTerrain(e.target.checked)} disabled={!loaded.city.terrain} /> {t('app.layer.terrain')}</label>
              {!!loaded.city.zones && <label className="check"><input type="checkbox" checked={showZones} onChange={e => setShowZones(e.target.checked)} /> {t('app.layer.zones')}</label>}
              {!!loaded.city.transit?.lines.length && <label className="check"><input type="checkbox" checked={showTransit} onChange={e => setShowTransit(e.target.checked)} /> {t('app.layer.transit')}</label>}
              {!!loaded.city.buildings && <label className="check"><input type="checkbox" checked={showBuildings} onChange={e => setShowBuildings(e.target.checked)} /> {t('app.layer.buildings')}</label>}
              {!!loaded.city.districts?.districts.length && <label className="check"><input type="checkbox" checked={showDistricts} onChange={e => setShowDistricts(e.target.checked)} /> {t('app.layer.districts')}</label>}
              <label className="check"><input type="checkbox" checked={showSkipped} onChange={e => setShowSkipped(e.target.checked)} /> {t('app.layer.skipped')}</label>
              <span className="muted hint-keys">{t('app.hintKeys')}</span>
              {cv.beyondMap !== undefined && <span className="warn">{t('app.cropBeyond', { h: cv.beyondMap })}</span>}
            </div>
          </section>
          <section className="right" aria-labelledby="h-package">
            <h2 id="h-package" className="sr-only">{t('app.package')}</h2>
            <div className="sr-only" aria-live="polite">{pkg ? t('app.planUpdated', { segments: pkg.plan.stats.segments, roadTypes: pkg.plan.stats.rbConfigs, skipped: pkg.plan.stats.skipped }) : ''}</div>
            {check && check.failed.length > 0 && (
              <div className="error-card" role="alert">
                <b>{t('app.exportFailed', { sections: check.failed.map(id => tk(`sec.${check.sections.find(s => s.label === id)?.id ?? id}`, id)).join(', ') })}</b>
                <p>{t('app.exportFailedHint')}</p>
              </div>
            )}
            {!pkg ? <p className="busy-inline"><span className="spinner" aria-hidden="true" />{buildError ?? t('app.building')}</p> : stats && (
              <>
                <div className={`stats ${building ? 'stale' : ''}`} aria-busy={building}>
                  <Stat n={n(pkg.plan.stats.segments)} label={t('app.stat.segments')} />
                  <Stat n={n(pkg.plan.stats.nodes)} label={t('app.stat.nodes')} />
                  <Stat n={n(pkg.plan.stats.rbConfigs)} label={t('app.stat.roadTypes')} />
                  <Stat n={n(pkg.plan.stats.skipped)} label={t('app.stat.skipped')} />
                  <div className="stat wide">
                    <div>{t('app.netInfosInCrop')} <b>{stats.netInfos.inCrop}</b>{building && <span className="spinner small" aria-label={t('app.updating')} />}</div>
                    <div className="muted">{t('app.netInfoBreakdown', { rb: stats.rb.inCrop, vanilla: stats.vanilla.inCrop, notPlaceable: stats.skippedNotPlaceable.inCrop, unmapped: stats.skippedUnmapped.inCrop })}</div>
                  </div>
                </div>
                <div className="downloads">
                  <button className="primary" disabled={!!pkg.errors.length || !pkg.plan.segments.length || zipping} aria-busy={zipping} onClick={() => void download('package')}>{zipping ? <><span className="spinner small" aria-hidden="true" /> {t('app.building')}</> : t('app.download')}</button>
                  <button disabled={!pkg.rbConfigs.length} onClick={() => void download('rb')}>{t('app.downloadRb')}</button>
                  {zipping && <span className="muted small" role="status">{t('app.zipping')}</span>}
                  {pkg.errors.map(e => <span key={e} className="warn" role="alert">{e}</span>)}
                  {!pkg.plan.segments.length && !pkg.errors.length && <span className="warn">{t('app.noRoads')}</span>}
                </div>
                <div role="tablist" aria-label={t('app.packageDetails')} className="tabs" onKeyDown={onTabKey}>
                  {TABS.map(tb => (
                    <button key={tb.id} role="tab" id={`tab-${tb.id}`} aria-selected={tab === tb.id} aria-controls={`panel-${tb.id}`} tabIndex={tab === tb.id ? 0 : -1}
                      className={tab === tb.id ? 'on' : ''} ref={el => { tabRefs.current[tb.id] = el; }} onClick={() => setTab(tb.id)}>{tb.label(pkg, t)}</button>
                  ))}
                </div>
                <div className="panel" role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} tabIndex={0}>
                  {tab === 'overview' && (
                    <div className="overview">
                      <h3>{t('app.h.converts')}</h3>
                      <ConversionPanel summary={pkg.summary} />
                      <h3>{t('app.h.terrain')}</h3>
                      <TerrainPreview city={loaded.city} crop={committed} seaLevel={pkg.plan.water?.apply?.seaLevel ?? pkg.plan.water?.seaLevel ?? null} />
                      <h3>{t('app.h.options')}</h3>
                      <ConversionOptions value={conv} onChange={setConv} city={loaded.city} />
                      <h3>{t('app.h.install')}</h3>
                      <InstallHelp zipName={rbZipName} requires={pkg.plan.requires} />
                      {check && <><h3>{t('app.h.fileCheck')}</h3><FileCheck {...check} /></>}
                      <p className="small muted">{t(loaded.synthetic ? 'app.builtInSynthetic' : 'app.builtIn', { ms: Math.round(ms) })}</p>
                    </div>
                  )}
                  {tab === 'mapping' && <MappingTable rows={pkg.mapping} overrides={overrides} onOverride={(name, o) => setOverrides(prev => { const n = { ...prev }; if (o.mode === 'auto') delete n[name]; else n[name] = o; return n; })} />}
                  {tab === 'rb' && <RbList pkg={pkg} />}
                  {tab === 'assets' && <AssetsTable matches={pkg.assetMatches ?? []} overrides={assetOverrides} catalogue={catalogue} catalogueSource={catalogueSource ?? t('app.catalogueBundled')} hasBuildings={!!loaded.city.buildings && !!loaded.city.assets}
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

const fmtBytes = (b: number, n: (v: number, d?: number) => string) => (b >= 1e6 ? `${n(b / 1e6, 1)} MB` : `${Math.max(1, Math.round(b / 1e3))} kB`);

function Stat({ n, label }: { n: string; label: string }) {
  return <div className="stat"><b>{n}</b><span>{label}</span></div>;
}

function RbList({ pkg }: { pkg: BuiltPackage }) {
  const { t, tk, n } = useI18n();
  return (
    <div className="rblist">
      {[...pkg.rbVariants].sort((a, b) => b.segments - a.segments).map(v => (
        <details key={v.id}>
          <summary>
            <b>{v.title}</b>{v.imtOverrides && <span className="tag imt">IMT {v.imtOverrides}</span>}
            <span className="tag">{tk(`net.${v.category.toLowerCase()}`, v.category)}</span>
            <span className={Math.abs(v.diff) >= 0.5 ? 'tag loss' : 'tag'}>{n(v.cs1Width, 1)} → {n(v.rbWidth, 1)} m</span>
            <span className="muted small">{t('rb.segments', { n: v.segments })}</span>
          </summary>
          <div className="lanes">{v.rbLanes.map((l, i) => <code key={i}>{l}</code>)}</div>
          {v.notes.length > 0 && <ul>{v.notes.map((n, i) => <li key={i}>{n}</li>)}</ul>}
          <div className="muted mono small">{v.id}</div>
        </details>
      ))}
    </div>
  );
}
