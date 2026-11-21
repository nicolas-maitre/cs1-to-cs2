import { useState } from 'react';
import { VANILLA_CHOICES } from '../core/mapping.ts';
import type { MappingOverride } from '../core/mapping.ts';
import type { MappingRow } from '../core/plan.ts';
import { CATEGORY_STYLE } from './scene.ts';

interface Props {
  rows: MappingRow[];
  overrides: Record<string, MappingOverride>;
  onOverride: (netInfo: string, o: MappingOverride) => void;
}

const encode = (o: MappingOverride | undefined) => (!o || o.mode === 'auto' ? 'auto' : o.mode === 'vanilla' ? `v|${o.type}|${o.name}` : o.mode);
const decode = (v: string): MappingOverride => {
  if (v.startsWith('v|')) { const [, type, name] = v.split('|'); return { mode: 'vanilla', type: type as never, name }; }
  return { mode: v as 'auto' | 'rb' | 'skip' };
};

export function MappingTable({ rows, overrides, onOverride }: Props) {
  const [all, setAll] = useState(false);
  const shown = rows.filter(r => all || r.segmentsInCrop > 0);
  return (
    <div>
      <div className="panel-bar">
        <label className="check"><input type="checkbox" checked={all} onChange={e => setAll(e.target.checked)} /> Show NetInfos outside the crop too</label>
        {Object.keys(overrides).length > 0 && <button onClick={() => Object.keys(overrides).forEach(k => onOverride(k, { mode: 'auto' }))}>Reset {Object.keys(overrides).length} override(s)</button>}
      </div>
      <table className="mapping">
        <thead>
          <tr><th>CS1 NetInfo</th><th title="segments in the crop / in the whole city">Segments</th><th>CS2 target</th></tr>
        </thead>
        <tbody>
          {shown.map(r => {
            const t = r.target;
            const st = CATEGORY_STYLE[r.category];
            return (
              <tr key={r.netInfo} className={t.mode === 'skip' ? 'skipped' : ''}>
                <td>
                  <div className="ni"><i style={{ background: st.color }} title={st.label} />{r.title}</div>
                  <div className="muted small" title={r.infos.join('\n')}>{r.netInfo !== r.title ? r.netInfo : ''}{r.infos.length > 1 ? ` · +${r.infos.length - 1} variant(s)` : ''}</div>
                  {t.mode === 'rb' && r.rb && <div className="small">width <span className={Math.abs(r.rb.diff) >= 0.5 ? 'loss' : ''}>{r.rb.cs1Width} → {r.rb.rbWidth} m</span> · RB {r.rb.category}</div>}
                </td>
                <td className="num">{r.segmentsInCrop}<span className="muted"> / {r.segments}</span></td>
                <td>
                  <select value={encode(overrides[r.netInfo])} onChange={e => onOverride(r.netInfo, decode(e.target.value))}>
                    <option value="auto">Auto: {autoLabel(r)}</option>
                    {(r.category === 'road' || r.category === 'highway') && <option value="rb">Generated Road Builder road</option>}
                    <optgroup label="Vanilla CS2">
                      {VANILLA_CHOICES.map(v => <option key={v.name} value={`v|${v.type}|${v.name}`}>{v.name}</option>)}
                    </optgroup>
                    <option value="skip">Skip</option>
                  </select>
                  {overrides[r.netInfo] && <div className="small">override → {t.mode === 'vanilla' ? t.name : t.mode}</div>}
                  {r.prefabKeys.length > 1 && <div className="small muted">{r.prefabKeys.length} prefabs (IMT variants)</div>}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function autoLabel(r: MappingRow) {
  const t = r.target;
  if (r.override && r.override.mode !== 'auto') {
    // show what auto would be: road → RB, otherwise unknown here
    return r.category === 'road' || r.category === 'highway' ? 'Road Builder' : 'default';
  }
  if (t.mode === 'rb') return r.rb ? `Road Builder (${r.rb.category})` : 'Road Builder (no drive lane!)';
  if (t.mode === 'vanilla') return t.name;
  return `skip – ${t.reason}`;
}
