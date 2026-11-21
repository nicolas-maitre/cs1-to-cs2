import { APP_VERSION, docsFor } from '../core/docs.ts';
import type { MessageKey } from '../core/i18n/index.ts';
import { useI18n } from './i18n.tsx';
import { MODS } from './links.ts';

export interface LoadError { title: string; message: string; hint?: string }

interface Props {
  busy: string | null;
  error: LoadError | null;
  onPick: () => void;
  onHelp: (section: 'guide' | 'converts' | 'faq' | 'about') => void;
}

const STEPS = [1, 2, 3, 4, 5, 6] as const;
const LIMITS: MessageKey[] = ['landing.limit1', 'landing.limit2', 'landing.limit3', 'landing.limit4'];

export function Landing({ busy, error, onPick, onHelp }: Props) {
  const { t, lang } = useI18n();
  const privacy = docsFor(lang).PRIVACY;
  return (
    <div className="landing">
      <section className="hero">
        <h1>{t('landing.title')}</h1>
        <p className="lead">{t('landing.lead')}</p>
        <div className="drop" role="group" aria-labelledby="drop-title">
          <h2 id="drop-title">{t('landing.dropBefore')} <code>.cs1x</code> {t('landing.dropAfter')}</h2>
          <p className="muted">{t('landing.or')}</p>
          <button className="primary big" onClick={onPick} disabled={!!busy}>{t('landing.choose')}</button>
          <p className="muted small">{t('landing.private')}</p>
          <div aria-live="polite" className="drop-status">
            {busy && <p className="busy-inline"><span className="spinner" aria-hidden="true" />{busy}</p>}
          </div>
          {error && (
            <div className="error-card" role="alert">
              <b>{error.title}</b>
              <p>{error.message}</p>
              {error.hint && <p className="muted">{error.hint}</p>}
            </div>
          )}
        </div>
      </section>
      <section aria-labelledby="steps-title">
        <h2 id="steps-title">{t('landing.how')} <button className="link small" onClick={() => onHelp('guide')}>{t('landing.fullGuide')}</button></h2>
        <ol className="steps">
          {STEPS.map(i => (
            <li key={i}><span className="num" aria-hidden="true">{i}</span><div><b>{t(`landing.step${i}.title`)}</b><p>{t(`landing.step${i}.text`)}</p></div></li>
          ))}
        </ol>
      </section>
      <section aria-labelledby="mods-title">
        <h2 id="mods-title">{t('landing.mods')}</h2>
        <ul className="mods">
          {MODS.map(m => (
            <li key={m.name}>
              <span className="tag">{m.game}</span>
              {m.url ? <a href={m.url} target="_blank" rel="noreferrer">{m.name}</a> : <b>{m.name}</b>}
              {!m.url && <span className="tag soon">{t('landing.soon')}</span>}
              {!m.required && <span className="tag">{t('landing.optional')}</span>}
              <span className="muted"> – {t(m.why)}</span>
            </li>
          ))}
        </ul>
      </section>
      <section aria-labelledby="limits-title">
        <h2 id="limits-title">{t('landing.limits')}</h2>
        <ul className="plain">{LIMITS.map(k => <li key={k}>{t(k)}</li>)}</ul>
        <p><button className="link" onClick={() => onHelp('converts')}>{t('landing.whatConverts')}</button> · <button className="link" onClick={() => onHelp('faq')}>{t('landing.faq')}</button></p>
      </section>
      <footer className="landing-foot">
        <p><b>{t('landing.privacy')}</b> {privacy[0]}</p>
        <p className="muted small">cs1conv {APP_VERSION} · {t('landing.notAffiliated')} · <button className="link" onClick={() => onHelp('about')}>{t('landing.about')}</button> · <a href="./THIRD-PARTY-NOTICES.txt" target="_blank" rel="noreferrer">{t('landing.notices')}</a></p>
      </footer>
    </div>
  );
}
