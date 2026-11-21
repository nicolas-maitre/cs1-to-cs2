import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { APP_VERSION, FAQ, GUIDE, MATRIX, faqMarkdown, guideMarkdown, matrixMarkdown } from '../src/core/docs.ts';
import { GENERATOR } from '../src/core/plan.ts';
import { bundledPackages, noticesText } from '../scripts/notices.mjs';

const web = path.resolve(import.meta.dirname, '..');
const guide = path.resolve(web, '..', 'docs', 'guide');

describe('documentation', () => {
  it('docs/guide is generated from src/core/docs.ts and up to date (run `npm run docs`)', () => {
    expect(fs.readFileSync(path.join(guide, 'user-guide.md'), 'utf8')).toBe(guideMarkdown());
    expect(fs.readFileSync(path.join(guide, 'faq.md'), 'utf8')).toBe(faqMarkdown());
    expect(fs.readFileSync(path.join(guide, 'what-converts.md'), 'utf8')).toBe(matrixMarkdown());
  });

  it('has the content the app shows', () => {
    expect(GUIDE.length).toBeGreaterThanOrEqual(7);
    expect(FAQ.length).toBeGreaterThanOrEqual(10);
    for (const area of ['Roads', 'Zoning', 'Traffic (TM:PE)', 'Transit', 'Buildings', 'Districts']) expect(MATRIX.some(r => r.area === area)).toBe(true);
    expect(guideMarkdown()).toMatch(/ModsData\\RoadBuilder/);
  });

  it('uses one version everywhere', () => {
    expect(JSON.parse(fs.readFileSync(path.join(web, 'package.json'), 'utf8')).version).toBe(APP_VERSION);
    expect(GENERATOR.version).toBe(APP_VERSION);
  });

  it('lists every bundled npm package and its licence in the notices', () => {
    const pkgs = bundledPackages(web);
    expect(pkgs.map(p => p.name)).toEqual(expect.arrayContaining(['fflate', 'react', 'react-dom']));
    for (const p of pkgs) expect(p.text.length, p.name).toBeGreaterThan(100);
    expect(fs.readFileSync(path.join(web, 'public', 'THIRD-PARTY-NOTICES.txt'), 'utf8')).toBe(noticesText(pkgs) + '\n');
  });
});
