import { useState } from 'react';
import type { T } from '../core/i18n/index.ts';
import { VANILLA_CHOICES } from '../core/mapping.ts';
import type { MappingOverride } from '../core/mapping.ts';
import type { MappingRow } from '../core/plan.ts';
import { useI18n } from './i18n.tsx';
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
  const { t, tk, n } = useI18n();
  const [all, setAll] = useState(false);
  const shown = rows.filter(r => all || r.segmentsInCrop > 0);
  const nOverrides = Object.keys(overrides).length;
  return (
    <div>
      <div className="panel-bar">
        <label className="check"><input type="checkbox" checked={all} onChange={e => setAll(e.target.checked)} /> {t('mapping.showAll')}</label>
        {nOverrides > 0 && <button onClick={() => Object.keys(overrides).forEach(k => onOverride(k, { mode: 'auto' }))}>{t('mapping.reset', { n: nOverrides })}</button>}
      </div>
      <table className="mapping">
        <thead>
          <tr><th>{t('mapping.th.netInfo')}</th><th title={t('mapping.th.segmentsTitle')}>{t('mapping.th.segments')}</th><th>{t('mapping.th.target')}</th></tr>
        </thead>
        <tbody>
          {shown.map(r => {
            const tg = r.target;
            const st = CATEGORY_STYLE[r.category];
            const sub = [r.netInfo !== r.title ? r.netInfo : '', r.infos.length > 1 ? t('mapping.variants', { n: r.infos.length - 1 }) : ''].filter(Boolean).join(' · ');
            return (
              <tr key={r.netInfo} className={tg.mode === 'skip' ? 'skipped' : ''}>
                <td>
                  <div className="ni"><i style={{ background: st.color }} title={tk(`net.${r.category}`, st.label)} />{r.title}</div>
                  {sub && <div className="muted small" title={r.infos.join('\n')}>{sub}</div>}
                  {tg.mode === 'rb' && r.rb && <div className="small">{t('mapping.width')} <span className={Math.abs(r.rb.diff) >= 0.5 ? 'loss' : ''}>{n(r.rb.cs1Width, 1)} → {n(r.rb.rbWidth, 1)} m</span> · RB {r.rb.category}</div>}
                </td>
                <td className="num">{n(r.segmentsInCrop)}<span className="muted"> / {n(r.segments)}</span></td>
                <td>
                  <select aria-label={t('mapping.targetFor', { name: r.title })} value={encode(overrides[r.netInfo])} onChange={e => onOverride(r.netInfo, decode(e.target.value))}>
                    <option value="auto">{t('mapping.auto', { target: autoLabel(r, t) })}</option>
                    {(r.category === 'road' || r.category === 'highway') && <option value="rb">{t('mapping.rb')}</option>}
                    <optgroup label={t('mapping.vanilla')}>
                      {VANILLA_CHOICES.map(v => <option key={v.name} value={`v|${v.type}|${v.name}`}>{v.name}</option>)}
                    </optgroup>
                    <option value="skip">{t('mapping.skip')}</option>
                  </select>
                  {overrides[r.netInfo] && <div className="small">{t('mapping.override', { target: tg.mode === 'vanilla' ? tg.name : tg.mode === 'skip' ? t('mapping.skip') : t('mapping.autoRb') })}</div>}
                  {r.prefabKeys.length > 1 && <div className="small muted">{t('mapping.prefabs', { n: r.prefabKeys.length })}</div>}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function autoLabel(r: MappingRow, t: T) {
  const tg = r.target;
  if (r.override && r.override.mode !== 'auto') {
    // what auto would be: roads → Road Builder, otherwise unknown here
    return r.category === 'road' || r.category === 'highway' ? t('mapping.autoRb') : t('mapping.autoDefault');
  }
  if (tg.mode === 'rb') return r.rb ? t('mapping.autoRbCat', { cat: r.rb.category }) : t('mapping.autoRbNoLane');
  if (tg.mode === 'vanilla') return tg.name;
  return t('mapping.autoSkip', { reason: tg.reason });
}
