// UI language: English or French, from the browser's languages, switchable in the header and remembered locally.
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { detectLang, fmtNumber, LANGS, MESSAGES, translator } from '../core/i18n/index.ts';
import type { Lang, MessageKey, T, Vars } from '../core/i18n/index.ts';

const STORE = 'cs1conv.lang';

interface I18n {
  lang: Lang;
  setLang: (l: Lang) => void;
  t: T;
  /** Translate a key built at run time (status, kind, section ids); `fallback` when there is no such key. */
  tk: (key: string, fallback: string, vars?: Vars) => string;
  /** Number in the UI language's format. */
  n: (v: number, digits?: number) => string;
}

const hasKey = (key: string): key is MessageKey => Object.prototype.hasOwnProperty.call(MESSAGES.en, key);

function initialLang(): Lang {
  try {
    const saved = localStorage.getItem(STORE);
    if (saved === 'en' || saved === 'fr') return saved;
  } catch { /* storage unavailable */ }
  return detectLang(typeof navigator === 'undefined' ? [] : navigator.languages ?? [navigator.language]);
}

const Ctx = createContext<I18n | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(initialLang);
  const setLang = useCallback((l: Lang) => {
    setLangState(l);
    try { localStorage.setItem(STORE, l); } catch { /* storage unavailable */ }
  }, []);
  useEffect(() => { document.documentElement.lang = lang; document.title = translator(lang)('app.title'); }, [lang]);
  const value = useMemo<I18n>(() => {
    const t = translator(lang);
    return {
      lang, setLang, t,
      tk: (key, fallback, vars) => (hasKey(key) ? t(key, vars) : fallback),
      n: (v, digits = 0) => fmtNumber(lang, v, digits),
    };
  }, [lang, setLang]);
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useI18n(): I18n {
  const v = useContext(Ctx);
  if (!v) throw new Error('useI18n outside I18nProvider');
  return v;
}

/** Language switch for the header. */
export function LangSwitch() {
  const { lang, setLang, t } = useI18n();
  return (
    <select className="lang" value={lang} aria-label={t('app.language')} title={t('app.language')} onChange={e => setLang(e.target.value as Lang)}>
      {LANGS.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}
    </select>
  );
}
