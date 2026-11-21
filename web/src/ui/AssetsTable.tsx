import { useMemo, useState } from 'react';
import type { AssetMatch, AssetOverrides, Cs2Catalogue, FallbackOverrides, PlacementFallback } from '../core/m3/buildings.ts';
import { useI18n } from './i18n.tsx';
import { Rich } from './Rich.tsx';

interface Props {
  matches: AssetMatch[];
  overrides: AssetOverrides;
  onOverride: (cs1: string, value: string | null) => void;
  fallbacks: FallbackOverrides;
  onFallback: (cs1: string, value: PlacementFallback | null) => void;
  catalogue: Cs2Catalogue | null;
  catalogueSource: string | null;
  onLoadCatalogue: () => void;
  hasBuildings: boolean;
}

const DECISION_CLS: Record<string, string> = { place: 'ok', growable: 'report', skip: 'absent', noEquivalent: 'no' };

type Filter = 'placed' | 'problems' | 'all';

export function AssetsTable({ matches, overrides, onOverride, fallbacks, onFallback, catalogue, catalogueSource, onLoadCatalogue, hasBuildings }: Props) {
  const { t, tk, n } = useI18n();
  const [filter, setFilter] = useState<Filter>('placed');
  const [inCrop, setInCrop] = useState(true);
  const prefabs = useMemo(() => (catalogue?.prefabs ?? []).filter(p => p.kind === 'building' && p.placeable).map(p => p.name).sort(), [catalogue]);
  if (!hasBuildings) return <p className="muted">{t('assets.noBuildings')}</p>;
  if (!catalogue) {
    return (
      <div className="empty-panel">
        <p><b>{t('assets.noCatalogue')}</b> {t('assets.noCatalogueWhy')}</p>
        <p className="muted"><Rich text={t('assets.noCatalogueHint')} /></p>
        <button className="primary" onClick={onLoadCatalogue}>{t('assets.loadCatalogue')}</button>
      </div>
    );
  }
  const rows = matches
    .filter(m => !inCrop || m.countInCrop > 0)
    .filter(m => filter === 'all' || (filter === 'placed' ? m.decision === 'place' || !!m.override : m.decision === 'noEquivalent' || (m.decision === 'place' && m.score != null && m.score < 0.7)))
    .sort((a, b) => (inCrop ? b.countInCrop - a.countInCrop : b.count - a.count) || a.title.localeCompare(b.title));
  const counts = { placed: 0, zoning: 0, skipped: 0, none: 0 };
  for (const m of matches) if (!inCrop || m.countInCrop) {
    const c = inCrop ? m.countInCrop : m.count;
    if (m.decision === 'place') counts.placed += c; else if (m.decision === 'growable') counts.zoning += c; else if (m.decision === 'skip') counts.skipped += c; else counts.none += c;
  }
  const nChoices = Object.keys(overrides).length + Object.keys(fallbacks).length;
  const fallbackText = (f: PlacementFallback) => t(`fallback.${f}`);
  return (
    <div>
      <p className="small muted">
        {t('assets.catalogue', { source: catalogueSource ?? '', n: prefabs.length })}{catalogue.game?.version ? t('assets.catalogueGame', { version: catalogue.game.version }) : ''}.{' '}
        <button className="link" onClick={onLoadCatalogue}>{t('assets.loadAnother')}</button>
      </p>
      <p className="small">{t('assets.counts', { scope: inCrop ? t('assets.inCrop') : t('assets.inCity'), placed: counts.placed, zoning: counts.zoning, skipped: counts.skipped, none: counts.none })}</p>
      <div className="panel-bar chips" role="group" aria-label={t('assets.filter')}>
        {(['placed', 'problems', 'all'] as Filter[]).map(f => <button key={f} className={filter === f ? 'on' : ''} aria-pressed={filter === f} onClick={() => setFilter(f)}>{t(f === 'placed' ? 'assets.f.placed' : f === 'problems' ? 'assets.f.problems' : 'assets.f.all')}</button>)}
        <label className="check"><input type="checkbox" checked={inCrop} onChange={e => setInCrop(e.target.checked)} /> {t('assets.inCropOnly')}</label>
        {nChoices > 0 && <button onClick={() => { Object.keys(overrides).forEach(k => onOverride(k, null)); Object.keys(fallbacks).forEach(k => onFallback(k, null)); }}>{t('assets.reset', { n: nChoices })}</button>}
      </div>
      {!rows.length && <p className="muted">{t('assets.noMatch')}</p>}
      <table className="mapping assets">
        <thead><tr><th>{t('assets.th.building')}</th><th title={t('assets.th.countTitle')}>{t('assets.th.count')}</th><th>{t('assets.th.cs2')}</th></tr></thead>
        <tbody>
          {rows.map(m => {
            const options = [...new Set([...(m.prefab ? [m.prefab] : []), ...m.alternatives.map(a => a.prefab)])];
            const label = tk(`decision.${m.decision}`, m.decision);
            return (
              <tr key={m.cs1} className={m.decision === 'place' ? '' : 'skipped'}>
                <td>
                  <div className="ni">{m.title}</div>
                  <div className="small muted">{m.cs1 !== m.title ? m.cs1 : ''}{m.service ? ` · ${m.service}` : ''}</div>
                </td>
                <td className="num">{n(m.countInCrop)}<span className="muted"> / {n(m.count)}</span></td>
                <td>
                  <div><span className={`status ${DECISION_CLS[m.decision] ?? ''}`}>{label}</span>{m.score != null && <span className={m.score < 0.7 ? 'tag loss' : 'tag'} title={t('assets.score')}>{Math.round(m.score * 100)} %</span>}{m.override && <span className="tag">{t('assets.yourChoice')}</span>}</div>
                  {!m.growable && (
                    <select aria-label={t('assets.cs2For', { name: m.title })} value={overrides[m.cs1] ?? ''} onChange={e => onOverride(m.cs1, e.target.value || null)}>
                      <option value="">{t('assets.auto', { x: m.decision === 'place' && !m.override ? m.prefab ?? '' : m.override ? t('assets.bestMatch') : label })}</option>
                      {options.length > 0 && <optgroup label={t('assets.suggested')}>{options.map(p => <option key={p} value={p}>{p}{m.alternatives.find(a => a.prefab === p) ? ` (${Math.round(m.alternatives.find(a => a.prefab === p)!.score * 100)} %)` : ''}</option>)}</optgroup>}
                      <optgroup label={t('assets.allCs2')}>{prefabs.map(p => <option key={p} value={p}>{p}</option>)}</optgroup>
                      <option value="skip">{t('assets.dontPlace')}</option>
                    </select>
                  )}
                  {m.decision === 'place' && (
                    <label className="fallback small">{t('assets.fallback')}{' '}
                      <select aria-label={t('assets.fallbackFor', { name: m.title })} value={fallbacks[m.cs1] ?? ''} onChange={e => onFallback(m.cs1, (e.target.value || null) as PlacementFallback | null)}>
                        <option value="">{t('assets.auto', { x: fallbackText(m.fallbackAuto) })}</option>
                        <option value="nearby">{fallbackText('nearby')}</option>
                        <option value="anywhere">{fallbackText('anywhere')}</option>
                        <option value="skip">{fallbackText('skip')}</option>
                      </select>
                    </label>
                  )}
                  <div className="small muted">{m.reason}</div>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
