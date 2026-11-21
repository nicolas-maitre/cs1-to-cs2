// Tiny i18n engine, no dependencies, no DOM: the UI and the core (What converts, the package README) share it.
//
// Messages use ICU-like syntax, enough for this app:
//   {name}                                  a variable (numbers are formatted for the language)
//   {n, plural, one {# road} other {# roads}}   plural forms ("#" = the formatted number); "=0 {…}" is an exact match
// English is the source (en.ts); fr.ts must have every key, with the same variables (test/i18n.test.ts).

import { EN } from './en.ts';
import type { MessageKey } from './en.ts';
import { FR } from './fr.ts';

export type Lang = 'en' | 'fr';
export const LANGS: { id: Lang; name: string }[] = [{ id: 'en', name: 'English' }, { id: 'fr', name: 'Français' }];
export type { MessageKey };
export type Vars = Record<string, string | number>;
export type T = (key: MessageKey, vars?: Vars) => string;

export const MESSAGES: Record<Lang, Record<MessageKey, string>> = { en: EN, fr: FR };

const LOCALE: Record<Lang, string> = { en: 'en', fr: 'fr-FR' };

/** Number in the language's format (thousands separators, decimal comma in French). */
export function fmtNumber(lang: Lang, n: number, maxFractionDigits = 2): string {
  return n.toLocaleString(LOCALE[lang], { maximumFractionDigits: maxFractionDigits });
}

/** CLDR cardinal rules for the two languages: French "one" covers 0 and 1. */
function pluralCategory(lang: Lang, n: number): 'one' | 'other' {
  if (lang === 'fr') return n >= 0 && n < 2 ? 'one' : 'other';
  return n === 1 ? 'one' : 'other';
}

/** Index of the brace closing the one opened at `open`. */
function closing(s: string, open: number): number {
  let depth = 0;
  for (let i = open; i < s.length; i++) {
    if (s[i] === '{') depth++;
    else if (s[i] === '}' && --depth === 0) return i;
  }
  throw new Error(`unbalanced braces in message: ${s}`);
}

function formatValue(lang: Lang, v: string | number | undefined, name: string): string {
  if (v === undefined) return `{${name}}`;
  return typeof v === 'number' ? fmtNumber(lang, v) : v;
}

/** Formats a message with its variables. Unknown variables stay visible as {name}, so a mistake shows. */
export function format(lang: Lang, msg: string, vars: Vars = {}, hash?: string): string {
  let out = '';
  for (let i = 0; i < msg.length; i++) {
    const ch = msg[i];
    if (ch === '#' && hash !== undefined) { out += hash; continue; }
    if (ch !== '{') { out += ch; continue; }
    const end = closing(msg, i);
    const body = msg.slice(i + 1, end);
    i = end;
    const m = /^\s*(\w+)\s*,\s*plural\s*,([\s\S]*)$/.exec(body);
    if (!m) { const name = body.trim(); out += formatValue(lang, vars[name], name); continue; }
    const n = Number(vars[m[1]]);
    const options = new Map<string, string>();
    const rest = m[2];
    for (let j = 0; j < rest.length; j++) {
      if (/\s/.test(rest[j])) continue;
      const sel = /^(=\d+|\w+)\s*/.exec(rest.slice(j));
      if (!sel) throw new Error(`bad plural in message: ${msg}`);
      j += sel[0].length;
      const e = closing(rest, j);
      options.set(sel[1], rest.slice(j + 1, e));
      j = e;
    }
    const branch = options.get(`=${n}`) ?? options.get(pluralCategory(lang, n)) ?? options.get('other') ?? '';
    out += format(lang, branch, vars, formatValue(lang, Number.isFinite(n) ? n : undefined, m[1]));
  }
  return out;
}

/** The translator for a language. Missing keys fall back to English (the test makes sure there are none). */
export function translator(lang: Lang): T {
  const dict = MESSAGES[lang] ?? EN;
  return (key, vars) => format(lang, dict[key] ?? EN[key] ?? key, vars);
}

/** "a, b and c" in the language, leaving out empty parts. */
export function listOf(lang: Lang, parts: (string | false | null | undefined)[]): string {
  const p = parts.filter(Boolean) as string[];
  if (p.length < 2) return p[0] ?? '';
  return `${p.slice(0, -1).join(', ')} ${lang === 'fr' ? 'et' : 'and'} ${p[p.length - 1]}`;
}

/** The browser's preferred language among ours, English otherwise. */
export function detectLang(languages: readonly string[] = []): Lang {
  for (const l of languages) {
    const base = l.toLowerCase().split('-')[0];
    if (base === 'fr') return 'fr';
    if (base === 'en') return 'en';
  }
  return 'en';
}

/** All placeholders and plural variables of a message (for the translation test). */
export function variablesOf(msg: string): string[] {
  const found = new Set<string>();
  const walk = (s: string) => {
    for (let i = 0; i < s.length; i++) {
      if (s[i] !== '{') continue;
      const end = closing(s, i);
      const body = s.slice(i + 1, end);
      i = end;
      const m = /^\s*(\w+)\s*,\s*plural\s*,([\s\S]*)$/.exec(body);
      if (!m) { found.add(body.trim()); continue; }
      found.add(m[1]);
      const rest = m[2];
      for (let j = 0; j < rest.length; j++) {
        if (rest[j] !== '{') continue;
        const e = closing(rest, j);
        walk(rest.slice(j + 1, e));
        j = e;
      }
    }
  };
  walk(msg);
  return [...found].sort();
}
