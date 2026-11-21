import { useMemo, useState } from 'react';
import type { AssetMatch, AssetOverrides, Cs2Catalogue } from '../core/m3/buildings.ts';

interface Props {
  matches: AssetMatch[];
  overrides: AssetOverrides;
  onOverride: (cs1: string, value: string | null) => void;
  catalogue: Cs2Catalogue | null;
  catalogueSource: string | null;
  onLoadCatalogue: () => void;
  hasBuildings: boolean;
}

const DECISION: Record<string, { label: string; cls: string }> = {
  place: { label: 'placed', cls: 'ok' },
  growable: { label: 'zoning', cls: 'report' },
  skip: { label: 'skipped', cls: 'absent' },
  noEquivalent: { label: 'no CS2 equivalent', cls: 'no' },
};

type Filter = 'placed' | 'problems' | 'all';

export function AssetsTable({ matches, overrides, onOverride, catalogue, catalogueSource, onLoadCatalogue, hasBuildings }: Props) {
  const [filter, setFilter] = useState<Filter>('placed');
  const [inCrop, setInCrop] = useState(true);
  const prefabs = useMemo(() => (catalogue?.prefabs ?? []).filter(p => p.kind === 'building' && p.placeable).map(p => p.name).sort(), [catalogue]);
  if (!hasBuildings) return <p className="muted">This export has no buildings. Exporter 1.2 or later exports them (assets.json and buildings.json).</p>;
  if (!catalogue) {
    return (
      <div className="empty-panel">
        <p><b>The CS2 building catalogue is not loaded.</b> Matching CS1 buildings to CS2 needs the list of CS2 buildings.</p>
        <p className="muted">Load <code>cs2-catalogue.json</code> (made by <code>node buildings/run.mjs catalogue</code> with CS2 running).</p>
        <button className="primary" onClick={onLoadCatalogue}>Load CS2 catalogue…</button>
      </div>
    );
  }
  const rows = matches
    .filter(m => !inCrop || m.countInCrop > 0)
    .filter(m => filter === 'all' || (filter === 'placed' ? m.decision === 'place' || !!m.override : m.decision === 'noEquivalent' || (m.decision === 'place' && m.score != null && m.score < 0.7)))
    .sort((a, b) => (inCrop ? b.countInCrop - a.countInCrop : b.count - a.count) || a.title.localeCompare(b.title));
  const counts = { placed: 0, zoning: 0, skipped: 0, none: 0 };
  for (const m of matches) if (!inCrop || m.countInCrop) {
    const n = inCrop ? m.countInCrop : m.count;
    if (m.decision === 'place') counts.placed += n; else if (m.decision === 'growable') counts.zoning += n; else if (m.decision === 'skip') counts.skipped += n; else counts.none += n;
  }
  return (
    <div>
      <p className="small muted">CS2 catalogue: {catalogueSource ?? 'loaded'} ({prefabs.length} placeable buildings){catalogue.game?.version ? `, CS2 ${catalogue.game.version}` : ''}. <button className="link" onClick={onLoadCatalogue}>Load another…</button></p>
      <p className="small">{inCrop ? 'In the crop' : 'In the city'}: <b>{counts.placed}</b> placed · {counts.zoning} left to zoning · {counts.skipped} rebuilt by CS2 (pillars, junctions…) · <span className={counts.none ? 'loss' : ''}>{counts.none} without equivalent</span></p>
      <div className="panel-bar chips" role="group" aria-label="Filter building types">
        {(['placed', 'problems', 'all'] as Filter[]).map(f => <button key={f} className={filter === f ? 'on' : ''} aria-pressed={filter === f} onClick={() => setFilter(f)}>{f === 'placed' ? 'Placed' : f === 'problems' ? 'Needs a look' : 'All types'}</button>)}
        <label className="check"><input type="checkbox" checked={inCrop} onChange={e => setInCrop(e.target.checked)} /> in the crop only</label>
        {Object.keys(overrides).length > 0 && <button onClick={() => Object.keys(overrides).forEach(k => onOverride(k, null))}>Reset {Object.keys(overrides).length} choice(s)</button>}
      </div>
      {!rows.length && <p className="muted">No building types match this filter.</p>}
      <table className="mapping assets">
        <thead><tr><th>CS1 building</th><th title="in the crop / in the city">Count</th><th>CS2 building</th></tr></thead>
        <tbody>
          {rows.map(m => {
            const d = DECISION[m.decision] ?? { label: m.decision, cls: '' };
            const options = [...new Set([...(m.prefab ? [m.prefab] : []), ...m.alternatives.map(a => a.prefab)])];
            return (
              <tr key={m.cs1} className={m.decision === 'place' ? '' : 'skipped'}>
                <td>
                  <div className="ni">{m.title}</div>
                  <div className="small muted">{m.cs1 !== m.title ? m.cs1 : ''}{m.service ? ` · ${m.service}` : ''}</div>
                </td>
                <td className="num">{m.countInCrop}<span className="muted"> / {m.count}</span></td>
                <td>
                  <div><span className={`status ${d.cls}`}>{d.label}</span>{m.score != null && <span className={m.score < 0.7 ? 'tag loss' : 'tag'} title="match score">{Math.round(m.score * 100)} %</span>}{m.override && <span className="tag">your choice</span>}</div>
                  {!m.growable && (
                    <select aria-label={`CS2 building for ${m.title}`} value={overrides[m.cs1] ?? ''} onChange={e => onOverride(m.cs1, e.target.value || null)}>
                      <option value="">Auto: {m.decision === 'place' && !m.override ? m.prefab : m.override ? 'best match' : d.label}</option>
                      {options.length > 0 && <optgroup label="Suggested">{options.map(p => <option key={p} value={p}>{p}{m.alternatives.find(a => a.prefab === p) ? ` (${Math.round(m.alternatives.find(a => a.prefab === p)!.score * 100)} %)` : ''}</option>)}</optgroup>}
                      <optgroup label="All CS2 buildings">{prefabs.map(p => <option key={p} value={p}>{p}</option>)}</optgroup>
                      <option value="skip">Don't place</option>
                    </select>
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
