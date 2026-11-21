// @vitest-environment jsdom
// UI smoke and accessibility tests: the real App in jsdom (canvas, Path2D and downloads stubbed, see ui/dom.ts),
// a synthetic .cs1x opened through the file input, and axe-core on every view.
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import axe from 'axe-core';
import { strFromU8, unzipSync } from 'fflate';
import { afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { App } from '../src/ui/App.tsx';
import { I18nProvider } from '../src/ui/i18n.tsx';
import { downloads, installDomStubs, syntheticCs1x } from './ui/dom.ts';

beforeAll(installDomStubs);
beforeEach(() => { localStorage.clear(); downloads.length = 0; document.documentElement.lang = ''; });
afterEach(cleanup);

const renderApp = () => render(<I18nProvider><App /></I18nProvider>);

async function openSynthetic() {
  const r = renderApp();
  const input = r.container.querySelector<HTMLInputElement>('input[type="file"][accept*=".cs1x"]')!;
  fireEvent.change(input, { target: { files: [syntheticCs1x()] } });
  await screen.findByRole('tablist', {}, { timeout: 15000 });
  return r;
}

/**
 * Rules axe can't decide without layout: jsdom has none, so it reports them "incomplete" even when the page is fine
 * (a landing page with its <h1> and <main> still gets "page-has-heading-one: incomplete"). Colour contrast and these
 * are checked in a real browser instead (web/README.md, Accessibility); one <h1> and one <main> per view are
 * asserted directly below.
 */
const NEEDS_LAYOUT = new Set(['color-contrast', 'page-has-heading-one', 'landmark-one-main', 'heading-order']);

/** axe-core violations, plus the "incomplete" results jsdom can decide, as "rule: target – help" lines. */
async function axeViolations(root: Document | Element = document): Promise<string[]> {
  const res = await axe.run(root, { rules: { 'color-contrast': { enabled: false } } });
  return [...res.violations, ...res.incomplete.filter(r => !NEEDS_LAYOUT.has(r.id))]
    .flatMap(v => v.nodes.map(n => `${v.id}: ${n.target.join(' ')} – ${v.help}`));
}

/** What the layout-dependent rules check, asserted on the DOM: one level-one heading and one main landmark. */
function expectPageStructure() {
  expect(document.querySelectorAll('h1').length, 'level-one headings').toBe(1);
  expect(document.querySelectorAll('main, [role="main"]').length, 'main landmarks').toBe(1);
}

describe('UI smoke tests', () => {
  it('renders the landing page', () => {
    renderApp();
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Bring your Cities: Skylines city to Cities: Skylines II');
    expect(screen.getByRole('button', { name: 'Choose a .cs1x file…' })).toBeTruthy();
    expect(screen.getAllByRole('listitem').length).toBeGreaterThan(6);
    expect(document.documentElement.lang).toBe('en');
  });

  it('opens a .cs1x: map, stats, tabs and the What converts list', async () => {
    await openSynthetic();
    expect(screen.getByRole('application', { name: /City map with the crop square/ })).toBeTruthy();
    const tabs = within(screen.getByRole('tablist')).getAllByRole('tab').map(t => t.textContent);
    expect(tabs[0]).toBe('Overview');
    expect(tabs.slice(1).map(t => t!.replace(/ \(\d+\)$/, ''))).toEqual(['Mapping', 'Road types', 'Assets', 'Report']);
    const conv = await screen.findByRole('list', { name: 'What converts, per category' });
    const labels = within(conv).getAllByRole('listitem').map(li => li.querySelector('b')?.textContent);
    expect(labels).toEqual(expect.arrayContaining(['Roads', 'Rail, metro and paths', 'Terrain', 'Districts and policies']));
    expect(within(conv).getAllByText('Converts', { exact: false }).length).toBeGreaterThan(0);
  });

  it('moves the crop with the keyboard', async () => {
    await openSynthetic();
    const x = screen.getByRole('spinbutton', { name: 'Centre X' }) as HTMLInputElement;
    const z = screen.getByRole('spinbutton', { name: 'Z' }) as HTMLInputElement;
    const size = screen.getByRole('spinbutton', { name: /Size/ }) as HTMLInputElement;
    const [x0, z0, s0] = [+x.value, +z.value, +size.value];
    const map = screen.getByRole('application');
    map.focus();
    fireEvent.keyDown(map, { key: 'ArrowRight' });
    await waitFor(() => expect(+x.value).toBeGreaterThan(x0));
    fireEvent.keyDown(map, { key: 'ArrowUp' });
    await waitFor(() => expect(+z.value).not.toBe(z0));
    fireEvent.keyDown(map, { key: '+' });
    await waitFor(() => expect(+size.value).toBeGreaterThan(s0));
  });

  it('downloads a package zip with the expected entries', async () => {
    await openSynthetic();
    fireEvent.click(screen.getByRole('button', { name: 'Download package (.zip)' }));
    await waitFor(() => expect(downloads.length).toBe(1), { timeout: 15000 });
    expect(downloads[0].name).toMatch(/-cs1conv-package\.zip$/);
    const files = unzipSync(new Uint8Array(await downloads[0].blob.arrayBuffer()));
    const names = Object.keys(files);
    expect(names).toEqual(expect.arrayContaining(['cs1conv-plan.json', 'report.json', 'README.txt']));
    expect(names.filter(n => /^roadbuilder\/r[0-9a-f]+.*-cs1conv\.json$/.test(n)).length).toBeGreaterThan(0);
    const plan = JSON.parse(strFromU8(files['cs1conv-plan.json']));
    expect(plan).toMatchObject({ format: 'cs1conv-plan', formatVersion: 1 });
    expect(plan.segments.length).toBeGreaterThan(50);
    expect(plan.districts?.items.length).toBeGreaterThan(0);
  });

  it('switches to French', async () => {
    await openSynthetic();
    fireEvent.change(screen.getByRole('combobox', { name: 'Language' }), { target: { value: 'fr' } });
    await waitFor(() => expect(document.documentElement.lang).toBe('fr'));
    expect(within(screen.getByRole('tablist')).getAllByRole('tab')[0].textContent).toBe('Aperçu');
    expect(screen.getByRole('button', { name: 'Télécharger le paquet (.zip)' })).toBeTruthy();
    const conv = await screen.findByRole('list', { name: 'Ce qui est converti, par catégorie' });
    await waitFor(() => expect(within(conv).getAllByRole('listitem').map(li => li.querySelector('b')?.textContent)).toContain('Routes'));
    expect(localStorage.getItem('cs1conv.lang')).toBe('fr');
  });
});

describe('accessibility (axe-core)', () => {
  it('landing page', async () => {
    renderApp();
    expectPageStructure();
    expect(await axeViolations()).toEqual([]);
  });

  it('every tab of the main view', async () => {
    await openSynthetic();
    for (const name of ['Overview', /^Mapping/, /^Road types/, /^Assets/, /^Report/]) {
      fireEvent.click(screen.getByRole('tab', { name }));
      expectPageStructure();
      expect(await axeViolations(), String(name)).toEqual([]);
    }
  });

  it('help dialog, every section', async () => {
    renderApp();
    fireEvent.click(screen.getByRole('button', { name: 'Help' }));
    const dialog = screen.getByRole('dialog');
    for (const name of ['User guide', 'What converts', 'FAQ', 'About & privacy']) {
      fireEvent.click(within(dialog).getByRole('button', { name }));
      expect(await axeViolations(), name).toEqual([]);
    }
  });

  it('the French landing page', async () => {
    localStorage.setItem('cs1conv.lang', 'fr');
    renderApp();
    expect(document.documentElement.lang).toBe('fr');
    expectPageStructure();
    expect(await axeViolations()).toEqual([]);
  });
});
