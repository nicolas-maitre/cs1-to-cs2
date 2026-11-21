// node --test buildings/match/vegetation.test.mjs
import test from 'node:test';
import assert from 'node:assert/strict';
import { matchVegetation, cs1Kind } from './vegetation.mjs';

const tree = (name, height) => ({ name, kind: 'object', uiGroup: 'Vegetation', height, components: { TreeObject: {} }, flags: [] });
const catalogue = { prefabs: [
  tree('AppleTree01', 5.7), tree('BirchTree01', 16.8), tree('EU_ChestnutTree01', 9.0), tree('EU_PoplarTree01', 9.5), tree('NA_LindenTree01', 7.6),
  tree('OakTree01', 15.1), tree('PineTree01', 20.6), tree('SpruceTree01', 21.0), tree('GreenBushWild01', 2.4), tree('FlowerBushWild01', 1.4),
  tree('RoyalPalmTree01', 18.9),
  { name: 'Boulder01', kind: 'object', uiGroup: 'PropsNature', height: 2.4, components: {}, flags: [] },
  { name: 'Boulder04', kind: 'object', uiGroup: 'PropsNature', height: 2.6, components: {}, flags: [] },
] };

test('Skibidi trees: kind first, then species, then height', () => {
  const m = matchVegetation({ trees: [
    { name: 'Tree2variant', title: 'Tree with Leaves #1', height: 5.9 },
    { name: 'Tree3variant', title: 'Tree with Leaves #2', height: 10.7 },
    { name: 'Beech01low', title: 'Small Beech', height: 15.5 },
    { name: 'Bush01', title: 'Small Bush', height: 1.3 },
  ] }, catalogue).byName;
  assert.equal(m.Tree2variant.cs2, 'NA_LindenTree01', 'closest height, never the apple tree');
  assert.equal(m.Tree3variant.cs2, 'EU_PoplarTree01');
  assert.equal(m.Beech01low.cs2, 'OakTree01', 'beech counts as a broad deciduous tree');
  assert.equal(m.Bush01.cs2, 'GreenBushWild01', 'green bush, not the flowering one');
});

test('species and kinds by name', () => {
  const m = matchVegetation({ trees: [
    { name: 'Birch 01', height: 12 }, { name: 'Conifer', height: 18 }, { name: 'Palm Tree', height: 10 }, { name: 'Apple tree', height: 6 },
  ] }, catalogue).byName;
  assert.equal(m['Birch 01'].cs2, 'BirchTree01');
  assert.equal(m.Conifer.kind, 'conifer');
  assert.ok(['PineTree01', 'SpruceTree01'].includes(m.Conifer.cs2));
  assert.equal(m['Palm Tree'].cs2, 'RoyalPalmTree01');
  assert.equal(m['Apple tree'].cs2, 'AppleTree01');
});

test('theme prefix breaks near-ties', () => {
  const m = matchVegetation({ trees: [{ name: 'Tree X', height: 8.3 }] }, catalogue, { theme: 'EU' }).byName;
  assert.match(m['Tree X'].cs2, /^EU_/);
});

test('rocks map one-to-one to boulders; other props are skipped', () => {
  const m = matchVegetation({ props: [{ name: 'Rock 04' }, { name: 'Rock 07' }, { name: 'Bench 01' }] }, catalogue).byName;
  assert.equal(m['Rock 04'].cs2, 'Boulder04');
  assert.equal(m['Rock 07'].decision, 'skip', 'no Boulder07 in this catalogue');
  assert.equal(m['Bench 01'].decision, 'skip');
  assert.equal(cs1Kind({ name: 'Bench 01' }, true), 'other');
});
