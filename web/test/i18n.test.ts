import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { DOCS_EN, docsFor } from '../src/core/docs.ts';
import { detectLang, format, listOf, MESSAGES, translator, variablesOf } from '../src/core/i18n/index.ts';
import { KNOWN_SECTIONS } from '../src/core/summary.ts';
import { buildPackage } from '../src/core/plan.ts';
import { city, lane, node, straight } from './fixtures.ts';

const src = path.resolve(import.meta.dirname, '..', 'src');
const files = (dir: string, re: RegExp): string[] => fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => {
  const p = path.join(dir, e.name);
  return e.isDirectory() ? files(p, re) : re.test(e.name) ? [p] : [];
});
const EN = MESSAGES.en, FR = MESSAGES.fr;

describe('translations', () => {
  it('French has every English message, with the same variables', () => {
    const missing = Object.keys(EN).filter(k => !(k in FR) || (EN[k as keyof typeof EN] !== '' && !FR[k as keyof typeof FR]));
    expect(missing).toEqual([]);
    expect(Object.keys(FR).filter(k => !(k in EN))).toEqual([]);
    const mismatched = Object.keys(EN).filter(k => JSON.stringify(variablesOf(EN[k as keyof typeof EN])) !== JSON.stringify(variablesOf(FR[k as keyof typeof FR])));
    expect(mismatched).toEqual([]);
  });

  it('every message parses', () => {
    for (const lang of ['en', 'fr'] as const) for (const [k, m] of Object.entries(MESSAGES[lang])) expect(() => format(lang, m, {}), `${lang} ${k}`).not.toThrow();
  });

  it('every key the code uses exists', () => {
    const used = new Set<string>();
    for (const f of files(src, /\.(ts|tsx)$/)) {
      if (f.includes(`${path.sep}i18n${path.sep}`)) continue;
      const text = fs.readFileSync(f, 'utf8');
      for (const m of text.matchAll(/\b(?:t|tr|tk|ctx\.t)\(\s*'([a-z][\w.-]*)'/g)) used.add(m[1]);
      for (const m of text.matchAll(/\b(?:msg|hint|why): '([a-z]+\.[\w.-]+)'/g)) used.add(m[1]);
    }
    expect(used.size).toBeGreaterThan(200);
    expect([...used].filter(k => !(k in EN))).toEqual([]);
  });

  it('keys built at run time exist for every value', () => {
    const need = [
      ...[1, 2, 3, 4, 5, 6].flatMap(i => [`landing.step${i}.title`, `landing.step${i}.text`]),
      ...['converted', 'partial', 'report-only', 'not-converted', 'none', 'absent'].flatMap(s => [`status.${s}`, `readme.status.${s}`]),
      ...['road', 'highway', 'train', 'metro', 'tram', 'monorail', 'pedestrian', 'bike', 'utility', 'transport-line', 'building', 'other'].map(c => `net.${c}`),
      ...KNOWN_SECTIONS.map(s => `sec.${s.id}`),
      ...['ok', 'failed', 'partial', 'absent'].map(s => `fc.${s}`),
      ...['ground', 'elevated', 'bridge', 'slope', 'tunnel'].map(s => `elev.${s}`),
      ...['place', 'growable', 'skip', 'noEquivalent'].map(s => `decision.${s}`),
      ...['nearby', 'anywhere', 'skip'].map(s => `fallback.${s}`),
    ];
    // every report kind of the package format
    const types = fs.readFileSync(path.join(src, 'core', 'types.ts'), 'utf8');
    const kinds = [...types.slice(types.indexOf('export type PackageReportKind')).split(';')[0].matchAll(/'([\w-]+)'/g)].map(m => m[1]);
    expect(kinds.length).toBeGreaterThan(20);
    need.push(...kinds.map(k => `kind.${k}`));
    expect(need.filter(k => !(k in EN))).toEqual([]);
  });

  it('no English text is left in the UI components outside the messages', () => {
    const allowed = /^(cs1conv|CS1|CS2|IMT|RB|m|km|%|✕|·|–|→|\/|\.cs1x|\(|\)|,|:|\s)+$/;
    const found: string[] = [];
    for (const f of files(path.join(src, 'ui'), /\.tsx$/)) {
      const text = fs.readFileSync(f, 'utf8');
      // JSX text between tags, and literal title / aria-label / placeholder / label attributes
      // (text followed by a tag; no newline, semicolon, quote or "=" so TypeScript generics don't match)
      for (const m of text.matchAll(/>([^<>{}\n;=?'`]*[A-Za-z][^<>{}\n;=?'`]*)<\/?[A-Za-z]/g)) {
        const s = m[1].trim();
        if (s && !allowed.test(s)) found.push(`${path.basename(f)}: ${s}`);
      }
      for (const m of text.matchAll(/\b(title|aria-label|placeholder|label)="([^"]*[A-Za-z]{2,}[^"]*)"/g)) found.push(`${path.basename(f)}: ${m[1]}="${m[2]}"`);
    }
    expect(found).toEqual([]);
  });
});

describe('formatting', () => {
  it('plurals and numbers follow the language', () => {
    expect(translator('en')('sum.roads', { segments: 1, rb: 2 })).toBe('1 segment as 2 generated Road Builder roads');
    expect(translator('fr')('sum.roads', { segments: 0, rb: 1 })).toBe('0 segment en 1 route Road Builder générée');
    expect(translator('fr')('sum.zoning', { n: 16845, theme: 'NA' })).toBe('16 845 cellules zonées en zones NA');
    expect(translator('en')('sum.zoning', { n: 16845, theme: 'NA' })).toBe('16,845 zoned cells as NA zones');
    expect(format('en', '{n, plural, =0 {none} one {# item} other {# items}}', { n: 0 })).toBe('none');
    expect(listOf('fr', ['a', false, 'b', 'c'])).toBe('a, b et c');
    expect(detectLang(['fr-CH', 'en'])).toBe('fr');
    expect(detectLang(['de-DE'])).toBe('en');
  });

  it('builds the What converts list and the README in French', () => {
    const ROAD = { name: 'Basic Road', ai: 'RoadAI', service: 'Road', parent: null, fields: { m_halfWidth: 8, m_pavementWidth: 3 }, lanes: [lane(-1.5, 3, 'Vehicle', 'Car', 'Backward'), lane(1.5, 3, 'Vehicle', 'Car', 'Forward')] };
    const c = city({ netInfos: [ROAD], nodes: [node(1, -100, 0), node(2, 100, 0)], segments: [straight(1, 'Basic Road', -100, 100, 0, false, 1, 2)] });
    const fr = buildPackage(c, { crop: { centerX: 0, centerZ: 0, size: 1000 }, lang: 'fr' });
    expect(fr.summary.find(s => s.id === 'roads')).toMatchObject({ label: 'Routes', summary: '1 segment en 1 route Road Builder générée' });
    const en = buildPackage(c, { crop: { centerX: 0, centerZ: 0, size: 1000 } });
    expect(en.summary.find(s => s.id === 'roads')!.label).toBe('Roads');
    expect(en.report).toEqual(fr.report); // report.json stays English
  });
});

describe('French documentation', () => {
  it('mirrors the English one, step by step', () => {
    const fr = docsFor('fr');
    expect(fr.GUIDE.map(s => s.body.length)).toEqual(DOCS_EN.GUIDE.map(s => s.body.length));
    expect(fr.FAQ.map(f => f.a.length)).toEqual(DOCS_EN.FAQ.map(f => f.a.length));
    expect(fr.MATRIX.map(r => r.status)).toEqual(DOCS_EN.MATRIX.map(r => r.status));
    expect(fr.MATRIX.map(r => !!r.notes)).toEqual(DOCS_EN.MATRIX.map(r => !!r.notes));
    expect(fr.REQUIREMENTS.map(r => r.items.length)).toEqual(DOCS_EN.REQUIREMENTS.map(r => r.items.length));
    expect(fr.PRIVACY.length).toBe(DOCS_EN.PRIVACY.length);
  });

  // The importer's Options page labels (cs2-importer/mod/LocaleEN.cs, LocaleFR.cs) are quoted in the guide as the game
  // shows them: every option, group and tab, so a new option can't go undocumented. The files are read as committed
  // (git HEAD, what CI sees), so another worker's uncommitted options don't fail the web suite mid-work; set
  // CS1CONV_IMPORTER_WORKTREE=1 to check against the working copy instead. A locale the importer doesn't have yet is skipped.
  for (const [lang, file] of [['en', 'LocaleEN.cs'], ['fr', 'LocaleFR.cs']] as const) {
    it(`quotes every label of the importer's page (${file})`, () => {
      const text = importerFile(`cs2-importer/mod/${file}`);
      if (!text) return;
      const labels = new Set([...text.matchAll(/Get(?:OptionLabel|OptionGroup|OptionTab|Settings)LocaleID\([^)]*\)?\), "([^"]+)"/g)].map(m => m[1]));
      expect(labels.size).toBeGreaterThan(10);
      const quoted = new Set(docsFor(lang).GUIDE.flatMap(st => st.body.join(' ').match(/\*\*[^*]+\*\*/g) ?? []).map(b => b.slice(2, -2)));
      expect([...labels].filter(l => !quoted.has(l))).toEqual([]);
    });
  }
});

/** A repository file as committed (git HEAD), or the working copy (no git, or CS1CONV_IMPORTER_WORKTREE=1); null if absent. */
function importerFile(rel: string): string | null {
  const repo = path.resolve(src, '..', '..');
  const working = () => { const p = path.join(repo, rel); return fs.existsSync(p) ? fs.readFileSync(p, 'utf8') : null; };
  if (process.env.CS1CONV_IMPORTER_WORKTREE === '1') return working();
  try {
    return execFileSync('git', ['show', `HEAD:${rel}`], { cwd: repo, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
  } catch {
    // not in git (yet): absent at HEAD means "not committed", so skip; no git at all: use the working copy
    try { execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repo, stdio: 'ignore' }); return null; } catch { return working(); }
  }
}
