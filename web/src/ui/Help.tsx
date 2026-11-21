import { useEffect, useRef, useState } from 'react';
import { APP_VERSION, docsFor } from '../core/docs.ts';
import type { MatrixStatus } from '../core/docs.ts';
import { useI18n } from './i18n.tsx';
import { MODS } from './links.ts';
import { Rich } from './Rich.tsx';

export { Rich };

const STATUS_CLS: Record<MatrixStatus, string> = { yes: 'ok', approx: 'partial', report: 'report', no: 'no', pending: 'absent' };
type Section = 'guide' | 'converts' | 'faq' | 'about';

export function Help({ onClose, initial = 'guide' }: { onClose: () => void; initial?: Section }) {
  const { t, lang } = useI18n();
  const { GUIDE, FAQ, MATRIX, PRIVACY, REQUIREMENTS, STATUS_TEXT } = docsFor(lang);
  const [section, setSection] = useState<Section>(initial);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    heading.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  const sections: [Section, string][] = [['guide', t('help.guide')], ['converts', t('help.converts')], ['faq', t('help.faq')], ['about', t('help.about')]];
  return (
    <div className="help-backdrop" onClick={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div className="help" role="dialog" aria-modal="true" aria-labelledby="help-title">
        <div className="help-head">
          <h2 id="help-title" tabIndex={-1} ref={heading}>{t('help.title')}</h2>
          <nav className="chips" aria-label={t('help.sections')}>
            {sections.map(([id, label]) => (
              <button key={id} className={section === id ? 'on' : ''} aria-pressed={section === id} onClick={() => setSection(id)}>{label}</button>
            ))}
          </nav>
          <button onClick={onClose} title={t('help.close')}><span aria-hidden="true">✕</span><span className="sr-only">{t('help.close')}</span></button>
        </div>
        {/* scrolls: focusable so keyboard users can scroll it (axe scrollable-region-focusable) */}
        <div className="help-body" tabIndex={0} role="region" aria-label={sections.find(([id]) => id === section)?.[1]}>
          {section === 'guide' && (
            <>
              <h3>{t('help.need')}</h3>
              {REQUIREMENTS.map(r => <div key={r.game}><b>{r.game}</b><ul>{r.items.map((i, k) => <li key={k}><Rich text={i} /></li>)}</ul></div>)}
              <ol className="guide">
                {GUIDE.map(s => <li key={s.title}><h3>{s.title}</h3>{s.body.map((p, k) => <p key={k}><Rich text={p} /></p>)}</li>)}
              </ol>
            </>
          )}
          {section === 'converts' && (
            // one table per area, named by its caption (like the Markdown version); a spanning header row per group
            // would be a header without data cells
            [...new Set(MATRIX.map(r => r.area))].map(a => (
              <table key={a} className="matrix">
                <caption>{a}</caption>
                <thead><tr><th scope="col">CS1</th><th scope="col">CS2</th><th scope="col">{t('help.status')}</th></tr></thead>
                <tbody>
                  {MATRIX.filter(r => r.area === a).map((r, i) => (
                    <tr key={i}><td>{r.cs1}</td><td>{r.cs2}{r.notes && <div className="small muted">{r.notes}</div>}</td><td><span className={`status ${STATUS_CLS[r.status]}`}>{STATUS_TEXT[r.status]}</span></td></tr>
                  ))}
                </tbody>
              </table>
            ))
          )}
          {section === 'faq' && FAQ.map(f => <details key={f.q}><summary>{f.q}</summary>{f.a.map((p, k) => <p key={k}><Rich text={p} /></p>)}</details>)}
          {section === 'about' && (
            <>
              <p><Rich text={t('help.aboutText', { version: APP_VERSION })} /></p>
              <h3>{t('help.privacy')}</h3>
              {PRIVACY.map((p, k) => <p key={k}>{p}</p>)}
              <h3>{t('help.mods')}</h3>
              <ul>{MODS.map(m => <li key={m.name}>{m.url ? <a href={m.url} target="_blank" rel="noreferrer">{m.name}</a> : m.name} ({m.game}) – {t(m.why)}{m.url ? '' : ` ${t('help.soon')}`}</li>)}</ul>
              <h3>{t('help.oss')}</h3>
              <p>{t('help.ossText')} <a href="./THIRD-PARTY-NOTICES.txt" target="_blank" rel="noreferrer">{t('landing.notices')}</a>.</p>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
