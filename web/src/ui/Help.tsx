import { Fragment, useEffect, useRef, useState } from 'react';
import { APP_VERSION, FAQ, GUIDE, MATRIX, PRIVACY, REQUIREMENTS, STATUS_TEXT } from '../core/docs.ts';
import type { MatrixStatus } from '../core/docs.ts';
import { MODS } from './links.ts';

/** Renders the docs' tiny markup: `code` and **bold**. */
export function Rich({ text }: { text: string }) {
  const parts = text.split(/(`[^`]+`|\*\*[^*]+\*\*)/g);
  return <>{parts.map((p, i) => (p.startsWith('`') ? <code key={i}>{p.slice(1, -1)}</code> : p.startsWith('**') ? <b key={i}>{p.slice(2, -2)}</b> : <Fragment key={i}>{p}</Fragment>))}</>;
}

const STATUS_CLS: Record<MatrixStatus, string> = { yes: 'ok', approx: 'partial', report: 'report', no: 'no', pending: 'absent' };
type Section = 'guide' | 'converts' | 'faq' | 'about';

export function Help({ onClose, initial = 'guide' }: { onClose: () => void; initial?: Section }) {
  const [section, setSection] = useState<Section>(initial);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  let area = '';
  return (
    <div className="help-backdrop" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="help" role="dialog" aria-modal="true" aria-labelledby="help-title">
        <div className="help-head">
          <h2 id="help-title" tabIndex={-1} ref={heading}>Help</h2>
          <nav className="chips" aria-label="Help sections">
            {([['guide', 'User guide'], ['converts', 'What converts'], ['faq', 'FAQ'], ['about', 'About & privacy']] as [Section, string][]).map(([id, label]) => (
              <button key={id} className={section === id ? 'on' : ''} aria-pressed={section === id} onClick={() => setSection(id)}>{label}</button>
            ))}
          </nav>
          <button onClick={onClose} aria-label="Close help">✕</button>
        </div>
        <div className="help-body">
          {section === 'guide' && (
            <>
              <h3>What you need</h3>
              {REQUIREMENTS.map(r => <div key={r.game}><b>{r.game}</b><ul>{r.items.map((i, k) => <li key={k}><Rich text={i} /></li>)}</ul></div>)}
              <ol className="guide">
                {GUIDE.map(s => <li key={s.title}><h3>{s.title}</h3>{s.body.map((p, k) => <p key={k}><Rich text={p} /></p>)}</li>)}
              </ol>
            </>
          )}
          {section === 'converts' && (
            <table className="matrix">
              <thead><tr><th>CS1</th><th>CS2</th><th>Status</th></tr></thead>
              <tbody>
                {MATRIX.map((r, i) => {
                  const head = r.area !== area;
                  area = r.area;
                  return (
                    <Fragment key={i}>
                      {head && <tr className="area"><th colSpan={3} scope="colgroup">{r.area}</th></tr>}
                      <tr><td>{r.cs1}</td><td>{r.cs2}{r.notes && <div className="small muted">{r.notes}</div>}</td><td><span className={`status ${STATUS_CLS[r.status]}`}>{STATUS_TEXT[r.status]}</span></td></tr>
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          )}
          {section === 'faq' && FAQ.map(f => <details key={f.q}><summary>{f.q}</summary>{f.a.map((p, k) => <p key={k}><Rich text={p} /></p>)}</details>)}
          {section === 'about' && (
            <>
              <p><b>cs1conv</b> {APP_VERSION}: converts a Cities: Skylines city into an import package for Cities: Skylines II. Not affiliated with Colossal Order or Paradox Interactive.</p>
              <h3>Privacy</h3>
              {PRIVACY.map((p, k) => <p key={k}>{p}</p>)}
              <h3>Mods</h3>
              <ul>{MODS.map(m => <li key={m.name}>{m.url ? <a href={m.url} target="_blank" rel="noreferrer">{m.name}</a> : m.name} ({m.game}) – {m.why}{m.url ? '' : ' (coming soon)'}</li>)}</ul>
              <h3>Open-source software</h3>
              <p>This page bundles React, React DOM, scheduler and fflate, all under the MIT licence. <a href="./THIRD-PARTY-NOTICES.txt" target="_blank" rel="noreferrer">Third-party notices</a>.</p>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
