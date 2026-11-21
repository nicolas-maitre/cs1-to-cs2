import { useMemo, useState } from 'react';
import type { PackageReport } from '../core/types.ts';
import { useI18n } from './i18n.tsx';

/** "segment 394 · road type Highway" instead of the raw JSON of an item's CS1 references. */
function refs(cs1: Record<string, number | string | number[]>, tk: (k: string, f: string) => string): string {
  return Object.entries(cs1).map(([k, v]) => {
    const value = Array.isArray(v) ? `${v.slice(0, 8).join(', ')}${v.length > 8 ? ', …' : ''}` : String(v);
    return `${tk(`report.${k}`, k)} ${value}`;
  }).join(' · ');
}

export function ReportPanel({ report }: { report: PackageReport }) {
  const { t, tk } = useI18n();
  const [kind, setKind] = useState<string>('all');
  const kinds = useMemo(() => {
    const m = new Map<string, number>();
    for (const i of report.items) m.set(i.kind, (m.get(i.kind) ?? 0) + 1);
    return [...m];
  }, [report]);
  const items = report.items.filter(i => kind === 'all' || i.kind === kind);
  const note = t('report.note');
  return (
    <div>
      {note && <p className="small muted">{note}</p>}
      <div className="panel-bar chips">
        <button className={kind === 'all' ? 'on' : ''} aria-pressed={kind === 'all'} onClick={() => setKind('all')}>{t('report.all', { n: report.items.length })}</button>
        {kinds.map(([k, c]) => <button key={k} className={kind === k ? 'on' : ''} aria-pressed={kind === k} onClick={() => setKind(k)}>{tk(`kind.${k}`, k)} {c}</button>)}
      </div>
      <ul className="report">
        {items.map((i, n) => (
          <li key={n} className={i.level}>
            <span className="tag">{tk(`kind.${i.kind}`, i.kind)}</span> {i.message}
            {i.cs1 && <span className="muted small"> ({refs(i.cs1, tk)})</span>}
          </li>
        ))}
      </ul>
    </div>
  );
}
