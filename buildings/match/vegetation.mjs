// CS1 tree / prop type → CS2 object prefab. Pure ES module, deterministic, no dependencies (same rules as match.mjs).
//
// CS1 side: assets.json `trees` and `props` entries (name, title, height). CS2 side: catalogue objects.
//   - trees: CS2 "Vegetation" objects with a TreeObject component (trees and wild bushes);
//   - rocks: CS2 "PropsNature" boulders.
// Rules, in order:
//   1. kind: bush (bush / shrub / hedge, or a CS1 tree under 3 m), conifer (conifer / pine / spruce / fir / cedar),
//      palm, rock; everything else is a deciduous tree. The kind is a filter.
//   2. species by name when CS2 has it (oak, birch, alder, chestnut, poplar, hickory, linden / lime, plane, apple / fruit,
//      pine, spruce / fir, palm); beech, elm, maple, ash and willow count as broad deciduous trees → oak.
//   3. otherwise the closest height (min/max ratio), preferring the theme prefix (EU_ / NA_) asked for, then the name.
//   Fruit trees and flowering bushes are only chosen when the CS1 name asks for them.
// CS1 rocks "Rock 0N" map one-to-one to CS2 "Boulder0N" (both games ship 8); any other prop is "skip".

export const VEGETATION_VERSION = '1.0.0';

const SPECIES = [
  [/oak|beech|elm|maple|\bash\b|willow/, 'oak'], [/birch/, 'birch'], [/alder/, 'alder'], [/chestnut/, 'chestnut'], [/poplar/, 'poplar'],
  [/hickory/, 'hickory'], [/linden|\blime/, 'linden'], [/plane/, 'londonplane'], [/apple|fruit|orchard/, 'apple'],
  [/pine/, 'pine'], [/spruce|\bfir\b|conifer|cedar/, 'spruce'], [/coconut/, 'coconutpalm'], [/royal/, 'royalpalm'], [/palm/, 'palm'],
];

function words(s) { return String(s ?? '').replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase(); }

/** Kind of a CS1 vegetation entry: bush | conifer | palm | deciduous | rock | other. */
export function cs1Kind(entry, isProp = false) {
  const w = `${words(entry.name)} ${words(entry.title)}`;
  if (isProp) return /rock|stone|boulder/.test(w) ? 'rock' : 'other';
  if (/bush|shrub|hedge/.test(w) || (entry.height != null && entry.height < 3)) return 'bush';
  if (/palm/.test(w)) return 'palm';
  if (/conifer|pine|spruce|\bfir\b|cedar/.test(w)) return 'conifer';
  return 'deciduous';
}

function cs2Kind(p) {
  const n = p.name.toLowerCase();
  if (/bush/.test(n)) return 'bush';
  if (/palm/.test(n)) return 'palm';
  if (/pine|spruce/.test(n)) return 'conifer';
  return 'deciduous';
}

const isTree = (p) => p.kind === 'object' && p.uiGroup === 'Vegetation' && (p.components?.TreeObject || (p.flags ?? []).includes('TreeObject'));
const ratio = (a, b) => (a > 0 && b > 0 ? Math.min(a, b) / Math.max(a, b) : 0);

/**
 * @param {{trees?: object[], props?: object[]}} assets – assets.json (trees / props catalogue entries)
 * @param {{prefabs: object[]}} catalogue – CS2 catalogue (full; the slim one has no objects)
 * @param {{theme?: 'EU'|'NA'}} [opts]
 * @returns {{version: string, byName: Object<string, {kind: string, cs2: string|null, decision: string, reason: string}>}}
 */
export function matchVegetation(assets, catalogue, opts = {}) {
  const trees = (catalogue.prefabs ?? []).filter(isTree).map((p) => ({ p, kind: cs2Kind(p), species: SPECIES.find(([re]) => re.test(p.name.toLowerCase()))?.[1] ?? null }));
  const boulders = new Map((catalogue.prefabs ?? []).filter((p) => p.kind === 'object' && /^Boulder0\d$/.test(p.name)).map((p) => [p.name, p]));
  const byName = {};

  for (const t of assets.trees ?? []) {
    const kind = cs1Kind(t);
    const w = `${words(t.name)} ${words(t.title)}`;
    const species = SPECIES.find(([re]) => re.test(w))?.[1] ?? null;
    let cands = trees.filter((c) => c.kind === kind);
    if (!/apple|fruit|orchard/.test(w)) cands = cands.filter((c) => c.species !== 'apple');
    if (!/flower/.test(w)) cands = cands.filter((c) => !/flower/i.test(c.p.name));
    if (!cands.length) { byName[t.name] = { kind, cs2: null, decision: 'noEquivalent', reason: `no CS2 ${kind} tree` }; continue; }
    const bySpecies = species ? cands.filter((c) => c.species === species) : [];
    const pool = bySpecies.length ? bySpecies : cands;
    const scored = pool.map((c) => ({
      c,
      s: ratio(t.height, c.p.height) + (opts.theme && c.p.name.startsWith(`${opts.theme}_`) ? 0.05 : 0),
    })).sort((a, b) => b.s - a.s || a.c.p.name.localeCompare(b.c.p.name));
    const best = scored[0].c;
    byName[t.name] = {
      kind, cs2: best.p.name, decision: 'place',
      reason: bySpecies.length ? `same species (${species}), height ${round(t.height)} vs ${round(best.p.height)} m`
        : `${kind}, closest height ${round(t.height)} vs ${round(best.p.height)} m`,
    };
  }

  for (const p of assets.props ?? []) {
    const kind = cs1Kind(p, true);
    const m = /rock\s*0?(\d)/i.exec(p.name);
    const b = kind === 'rock' && m ? boulders.get(`Boulder0${m[1]}`) : null;
    byName[p.name] = b ? { kind, cs2: b.name, decision: 'place', reason: `CS1 rock ${m[1]} → CS2 boulder ${m[1]}` }
      : { kind, cs2: null, decision: 'skip', reason: kind === 'rock' ? 'no matching CS2 boulder' : 'no sensible CS2 equivalent for this prop' };
  }
  return { version: VEGETATION_VERSION, byName };
}

function round(v) { return v == null ? null : Math.round(v * 10) / 10; }
