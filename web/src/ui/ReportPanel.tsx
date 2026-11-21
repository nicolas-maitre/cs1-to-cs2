import { useMemo, useState } from 'react';
import type { PackageReport } from '../core/types.ts';

export function ReportPanel({ report }: { report: PackageReport }) {
  const [kind, setKind] = useState<string>('all');
  const kinds = useMemo(() => {
    const m = new Map<string, number>();
    for (const i of report.items) m.set(i.kind, (m.get(i.kind) ?? 0) + 1);
    return [...m];
  }, [report]);
  const items = report.items.filter(i => kind === 'all' || i.kind === kind);
  return (
    <div>
      <div className="panel-bar chips">
        <button className={kind === 'all' ? 'on' : ''} onClick={() => setKind('all')}>all {report.items.length}</button>
        {kinds.map(([k, n]) => <button key={k} className={kind === k ? 'on' : ''} onClick={() => setKind(k)}>{k} {n}</button>)}
      </div>
      <ul className="report">
        {items.map((i, n) => (
          <li key={n} className={i.level}>
            <span className="tag">{i.kind}</span> {i.message}
            {i.cs1 && <span className="muted small"> {JSON.stringify(i.cs1)}</span>}
          </li>
        ))}
      </ul>
    </div>
  );
}
