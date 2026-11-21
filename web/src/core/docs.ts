// User documentation as data, in English (this file) and French (docs.fr.ts): rendered by the web app's Help page and
// written to docs/guide/*.md and docs/guide/fr/*.md by scripts/gen-docs.mjs (a test checks the files are up to date).
// Text uses `backticks` for code and **double stars** for bold; nothing else.

import type { Lang } from './i18n/index.ts';
import { DOCS_FR } from './docs.fr.ts';

export const APP_VERSION = '0.5.0';

export interface DocStep { title: string; body: string[] }
export interface Faq { q: string; a: string[] }
export type MatrixStatus = 'yes' | 'approx' | 'report' | 'no' | 'pending';
export interface MatrixRow { area: string; cs1: string; cs2: string; status: MatrixStatus; notes: string }

export interface Docs {
  STATUS_TEXT: Record<MatrixStatus, string>;
  PRIVACY: string[];
  REQUIREMENTS: { game: string; items: string[] }[];
  GUIDE: DocStep[];
  FAQ: Faq[];
  MATRIX: MatrixRow[];
  /** Headings and fixed lines of the generated Markdown files. */
  MD: { guideTitle: string; guideIntro: string; need: string; privacy: string; seeAlso: string; faqTitle: string; matrixTitle: string; matrixIntro: string; matrixHead: string; generated: string };
}

/** Where the importer looks for packages ("Packages on this PC"). */
export const PACKAGES_DIR = '%LOCALAPPDATA%\\cs1-to-cs2';

export const DOCS_EN: Docs = {
  STATUS_TEXT: {
    yes: 'converts',
    approx: 'converts, approximated',
    report: 'listed in the report only',
    no: 'not converted',
    pending: 'in the package; CS2 side in progress',
  },

  PRIVACY: [
    'cs1conv runs entirely in your browser. Your .cs1x file is read on your computer and never uploaded; there is no server, no account, no analytics and no cookies.',
    'The page remembers your crop, mapping choices, options and language in your browser\'s local storage so they are still there next time. Clearing the site data removes them.',
  ],

  REQUIREMENTS: [
    {
      game: 'Cities: Skylines (CS1)',
      items: [
        'The city you want to convert, and the **CS1 → CS2 Exporter** mod (no other mod is required).',
        'Works with cities that use 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool and Vanilla+ / workshop roads; their data is exported when the mod is enabled.',
      ],
    },
    {
      game: 'Cities: Skylines II (CS2)',
      items: [
        'CS2 1.6 or later, with the **Road Builder** mod (converted road types are Road Builder roads).',
        'The **CS1 → CS2 Importer** mod, which builds the package in your city.',
        'The **Traffic** mod for TM:PE priority signs, lane arrows and lane connections (optional).',
      ],
    },
  ],

  GUIDE: [
    {
      title: 'Install the exporter in Cities: Skylines',
      body: [
        'Add the **CS1 → CS2 Exporter** mod and enable it in Content Manager → Mods. Until it is published, build it from source (see the project README); a Release build puts it in your local Mods folder.',
        'The exporter only reads your city. It never saves or changes it.',
      ],
    },
    {
      title: 'Export your city',
      body: [
        'Load the city, then open Options → Mod Settings → **CS1 → CS2 Exporter** → **Export city**.',
        'It writes `%LOCALAPPDATA%\\Colossal Order\\Cities_Skylines\\Cs1Exporter\\<city>-<date>.cs1x` in a few seconds and shows a summary.',
      ],
    },
    {
      title: 'Open the export in the web tool',
      body: [
        'Drop the `.cs1x` on the cs1conv page, or press **Choose a .cs1x file…**. The file is read in your browser; nothing is uploaded.',
        'If a part of the export failed (for example IMT data), the page says which one; everything else still converts.',
      ],
    },
    {
      title: 'Choose what to convert',
      body: [
        'CS2 maps are smaller than an 81-tile CS1 map, so you convert a square of up to 14.3 km. Drag the square to move it and its corners to resize it, or press **Auto** for the area with the most roads.',
        'Roads crossing the edge of the square are left out (yellow on the map). Put the edge in open country where you can. Highways and railways that leave the square become CS2 outside connections.',
        'Keyboard: click the map, then use the arrow keys to move the square, + and − to resize it.',
      ],
    },
    {
      title: 'Check the conversion',
      body: [
        '**Overview** shows what converts per category with your city\'s numbers, a preview of the terrain, the options (zone theme, which parts to convert) and the install steps.',
        '**Mapping** lists every road type with its CS2 equivalent; you can pick another one. **Road types** shows the generated Road Builder roads and any width change.',
        '**Assets** lists the CS1 buildings with the CS2 building chosen for each type, the match score and why; pick another one or skip a type. For each type you can also say what happens if it doesn\'t fit at its spot: look for free land nearby (up to 400 m), anywhere in the square, or leave it out. Very large buildings (universities, power plants, cargo terminals) look anywhere by default.',
        '**Report** lists everything that is approximated or dropped.',
      ],
    },
    {
      title: 'Download the package',
      body: [
        'Press **Download package (.zip)**. Building it takes a few seconds: the terrain is two 4096 × 4096 heightmaps.',
        'Unzip it into its own folder under `%LOCALAPPDATA%\\cs1-to-cs2\\` (paste that path into the Explorer address bar), for example `%LOCALAPPDATA%\\cs1-to-cs2\\my-city\\`. The importer then lists it under **Packages on this PC**. Any other folder works too; you then type it in **Package folder**.',
        'The folder holds `cs1conv-plan.json`, `report.json`, the `roadbuilder` and `terrain` folders and a `README.txt`.',
      ],
    },
    {
      title: 'Install the mods and the road types in CS2',
      body: [
        'Enable **Road Builder** and the **CS1 → CS2 Importer**. **Traffic** is optional: without it, the priority signs are skipped.',
        'Road Builder reads its road types only when the game starts, so install them first. Start CS2, open Options → **CS1 → CS2 importer**, pick the package (as in step 9) and press **Install Road Builder configs**. Confirm, wait for "Copied N Road Builder configs", then **restart the game**. Or, with CS2 closed, copy the files from the package\'s `roadbuilder` folder into `%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder\\`.',
      ],
    },
    {
      title: 'Start a new city',
      body: [
        'Start a new game (not the editor). Any map works; Sunshine Peninsula has the fewest pre-built roads.',
        'The importer doesn\'t apply the terrain, the water and the outside connections yet: the roads follow the map\'s own ground, and only cuts of 4 m or more keep their CS1 depth. The package already holds them (the `terrain` folder) for when it does.',
        'Save the city now: the import can\'t be undone.',
      ],
    },
    {
      title: 'Check the package',
      body: [
        'Open Options → **CS1 → CS2 importer** (tab **Import**). In the **Package** group, pick the package in **Packages on this PC**: it lists the packages under `%LOCALAPPDATA%\\cs1-to-cs2\\`, newest first, and checks the one you pick. Or paste the package\'s folder into **Package folder** and press **Check**.',
        'The status line says what the package holds, for example "Package: Skibidi.02, 2035 roads to build. All 37 road types are available." If road types are missing, install the Road Builder configs and restart the game. If there are roads already in the area, keep **Clear the area first** on.',
      ],
    },
    {
      title: 'Import into the city',
      body: [
        'In the **Import into this city** group, the steps are all on by default: **Clear the area first**, **Zoning**, **Traffic lights, stops, turn bans, crosswalks**, **Priority signs (Traffic mod)**, **Buildings, trees, districts** and **Transit lines**. Turn off what you don\'t want; the roads are always built first.',
        'Press **Import into this city** and confirm. **Status** shows the progress ("Importing… batch 3 of 6", then each step) and ends with a summary per step, for example "Done in 4.33 s: 2035 of 2035 roads built." and "Transit: 9 of 9 lines, 145 of 145 stops.". It also gives the path of the full report, under `%LOCALAPPDATA%\\cs1-to-cs2\\import-reports\\`.',
        'A 3 km square takes well under a minute: the roads about 4 seconds, the transit lines the longest. The game stays responsive.',
      ],
    },
    {
      title: 'After the import',
      body: [
        'Save the city. It still needs water, electricity and services to grow, like any new city; zoned buildings then grow on the converted zones.',
        'Read the web tool\'s **Report** tab (or `report.json`) and the import summary for what needs a hand:',
        'TM:PE lane connections, lane arrows and U-turn rules are not applied in this version (only the priority signs are); the import report lists them. About 7 % of the zone cells find no CS2 zone block, because CS2 blocks are shallower. Metro and train lines need their stations from the buildings step. Rail yards added for them need their track connected to the line by hand. A few roads with shallow grade separations get joined at a junction by CS2.',
      ],
    },
  ],

  FAQ: [
    { q: 'Is my city uploaded anywhere?', a: ['No. The web tool runs entirely in your browser; the file never leaves your computer. There is no server behind the page.'] },
    { q: 'Why can\'t I convert my whole 81-tile city?', a: ['A CS2 map\'s playable area is about 14.3 km across, smaller than CS1\'s 81 tiles (17.3 km). The web tool lets you pick the square that fits; you can convert another square into another city.'] },
    { q: 'Some roads are narrower or wider than in CS1. Why?', a: ['Road types are rebuilt with Road Builder, which has fixed lane widths and sidewalks of at most 7 m. A CS1 road with 9 m sidewalks becomes a few metres narrower. Every change is listed under **Road types** and in the report.'] },
    { q: 'The page says it can\'t open my file.', a: ['Open the `.cs1x` written by the exporter, not a `.crp` save. If it says the format version is too new, reload the page to get the latest tool; if it is too old, update the exporter and export again.'] },
    { q: 'A section of my export failed.', a: ['The rest converts normally. Export again; if it keeps failing, the exporter\'s message is in the web tool under Overview → File check.'] },
    { q: 'The importer doesn\'t list my package.', a: ['**Packages on this PC** lists the folders under `%LOCALAPPDATA%\\cs1-to-cs2\\` that hold a `cs1conv-plan.json`. Unzip the package there, or type its folder in **Package folder**.'] },
    { q: 'The importer says road types are missing.', a: ['Road Builder roads must be in ModsData\\RoadBuilder **before** CS2 starts. "Road Builder configs aren\'t installed": press **Install Road Builder configs**, then restart CS2. "Installed but not loaded yet": restart CS2. Then press **Check** again. Roads of a missing type are skipped.'] },
    { q: 'My city has none of its CS1 hills, rivers or sea.', a: ['The importer doesn\'t apply the terrain and the water yet: the roads follow the CS2 map\'s own ground. The package already holds the converted terrain (the `terrain` folder), and the importer will use it in a later version.'] },
    { q: 'Where are my TM:PE lane connections and lane arrows?', a: ['They are in the package but not applied in this version: only the priority signs are (with the Traffic mod). The import report lists the rest.'] },
    { q: 'Why weren\'t all my buildings placed?', a: ['Zoned (growable) buildings are not placed: CS2 grows its own on the converted zones. Pillars, pipe junctions and poles are rebuilt by CS2. A few CS1 building types have no CS2 equivalent (libraries, taxi stands, cable car stops), and buildings on roads outside the square are left out. The Assets tab shows each type.'] },
    { q: 'Why did a building end up somewhere else?', a: ['CS2 buildings often need more room than their CS1 counterparts. When one doesn\'t fit at its CS1 spot, the importer tries the next-best CS2 buildings, then free land along an imported road nearby (up to 400 m). Very large buildings may go anywhere in the square. In the Assets tab you can change this per type, or leave the building out instead.'] },
    { q: 'My metro or train line has no vehicles.', a: ['CS2 lines get their vehicles from a depot of their type, and CS1 metro and train lines don\'t need one. When the city has none, the package adds a default depot or yard at the line\'s first stop. Connect a rail yard\'s track to the line by hand: CS2 yards only send trains over connected track.'] },
    { q: 'Do I need the Traffic mod?', a: ['Only for TM:PE priority signs, lane arrows and lane connections. Speed limits, bus lanes, traffic lights, all-way stops, turn bans and crosswalks work without it.'] },
    { q: 'What happens to timed traffic lights and Node Controller tweaks?', a: ['CS2 runs its own light phases and builds junctions itself. Timed lights become plain traffic lights; Node Controller settings are listed in the report.'] },
    { q: 'European or North American buildings?', a: ['Choose the zone theme under Overview → Options. It sets which CS2 residential and commercial zones the converted zones use, and which tree variants are preferred.'] },
    { q: 'Does the exporter change my CS1 save?', a: ['No. It only reads the loaded city and writes the `.cs1x` file.'] },
    { q: 'Why is the package so large?', a: ['Most of it is the terrain: two 4096 × 4096 heightmaps, the size CS2 uses, 64 MB unpacked and about 22 MB in the zip. Everything the CS2 mod needs is in the package, so nothing else has to run on your computer.'] },
    { q: 'Is my big city too large for the web tool?', a: ['The map and the package builder are made for large cities: a 60 000-segment test city stays smooth, and the package is built in the background.'] },
    { q: 'Can I use the web tool in French?', a: ['Yes: it follows your browser\'s language, and the language menu at the top of the page switches between English and French. The report (`report.json`) stays in English.'] },
  ],

  MATRIX: [
    { area: 'Roads', cs1: 'Roads, highways, one-ways, Vanilla+ and workshop roads', cs2: 'Generated Road Builder roads', status: 'approx', notes: 'Lanes, medians, parking, bus and tram lanes, trees and speed are kept; widths can change (Road Builder sidewalks max 7 m).' },
    { area: 'Roads', cs1: 'Elevated roads, bridges, slopes, tunnels', cs2: 'The same Road Builder road at the CS1 height', status: 'yes', notes: 'Road Builder makes the elevated, bridge and tunnel forms itself.' },
    { area: 'Roads', cs1: 'Painted bike lanes in the carriageway', cs2: 'Sidewalk with bike lane', status: 'approx', notes: 'Only at the road edge; CS2 has no painted carriageway bike lanes.' },
    { area: 'Rail and paths', cs1: 'Train, metro and tram tracks', cs2: 'CS2 double, two-way or one-way tracks', status: 'yes', notes: '' },
    { area: 'Rail and paths', cs1: 'Pedestrian and bike paths', cs2: 'Pavement paths, bike paths', status: 'yes', notes: '' },
    { area: 'Rail and paths', cs1: 'Monorail, cable car', cs2: '–', status: 'no', notes: 'CS2 has neither.' },
    { area: 'IMT', cs1: 'Red paint over a whole lane / pavement or grass island over a whole lane', cs2: 'Bus lane / median in the Road Builder road', status: 'yes', notes: 'Partial fills and grey paint are reported.' },
    { area: 'IMT', cs1: 'Pavement, grass and gravel fills at junctions', cs2: 'CS2 surfaces (pavement, grass, sand)', status: 'yes', notes: 'Placed with the buildings step.' },
    { area: 'IMT', cs1: 'Painted markings at junctions', cs2: '–', status: 'report', notes: 'No CS2 equivalent yet (decals are planned).' },
    { area: 'Terrain and water', cs1: 'Terrain heightmap (and the land around the square)', cs2: 'CS2 terrain at the same heights, plus the world map around it', status: 'pending', notes: 'In the package (terrain folder); the importer doesn\'t apply it yet, so the roads follow the map\'s own ground.' },
    { area: 'Terrain and water', cs1: 'Sea level, rivers and lakes (natural water sources)', cs2: 'CS2 sea level and water sources', status: 'pending', notes: 'In the package; not applied by the importer yet. CS2 re-simulates the water; extra sources keep rivers and lakes above the sea filled.' },
    { area: 'Terrain and water', cs1: 'Highways and railways leaving the square', cs2: 'CS2 outside connections', status: 'pending', notes: 'In the package; not applied by the importer yet.' },
    { area: 'Zoning', cs1: 'Residential, commercial, industrial and office zones', cs2: 'CS2 zones (EU or NA theme)', status: 'approx', notes: 'District specialisations become generic zones.' },
    { area: 'Traffic (TM:PE)', cs1: 'Speed limits, bus-only lanes', cs2: 'Road Builder variants with that speed / bus lane', status: 'yes', notes: 'CS2 has one speed per road.' },
    { area: 'Traffic (TM:PE)', cs1: 'Traffic lights, all-stop junctions, turn restrictions from lane arrows, crosswalks', cs2: 'Vanilla CS2 junction settings', status: 'yes', notes: '' },
    { area: 'Traffic (TM:PE)', cs1: 'Priority signs', cs2: 'Traffic mod', status: 'yes', notes: 'Needs the Traffic mod in CS2.' },
    { area: 'Traffic (TM:PE)', cs1: 'Lane arrows, lane connections, U-turns', cs2: 'Traffic mod', status: 'pending', notes: 'In the package; not applied in this version (listed in the import report).' },
    { area: 'Traffic (TM:PE)', cs1: 'Timed traffic lights, turn on red, blocked junctions, parking rules', cs2: '–', status: 'report', notes: 'Timed lights become plain traffic lights.' },
    { area: 'Node Controller', cs1: 'Node types, offsets, hidden markings', cs2: '–', status: 'report', notes: 'CS2 builds junction geometry itself.' },
    { area: 'Transit', cs1: 'Bus and tram lines', cs2: 'CS2 lines with colour, name, number and stops', status: 'approx', notes: 'Trolleybus, tourist and intercity buses become bus lines.' },
    { area: 'Transit', cs1: 'Train, metro, ship and plane lines', cs2: 'CS2 lines at the placed stations', status: 'approx', notes: 'Need their stations from the buildings step; a default depot or yard is added when the city has none.' },
    { area: 'Buildings', cs1: 'Service buildings, stations, depots, parks, landmarks', cs2: 'Closest CS2 building (same service, then capacity and size)', status: 'approx', notes: 'Check or change each type in the Assets tab, including what happens when it doesn\'t fit.' },
    { area: 'Buildings', cs1: 'Zoned (growable) buildings', cs2: 'Grown by CS2 on the converted zones', status: 'yes', notes: 'Not placed one by one.' },
    { area: 'Buildings', cs1: 'Libraries, taxi stands, cable car stops', cs2: '–', status: 'no', notes: 'No CS2 equivalent.' },
    { area: 'Districts', cs1: 'District names and outlines; policies', cs2: 'CS2 districts and policies', status: 'approx', notes: 'Policies with a CS2 equivalent (heavy traffic ban, recycling, …); zone specialisations are reported.' },
    { area: 'Districts', cs1: 'Parks, industry areas, campuses, airports', cs2: '–', status: 'report', notes: 'Their buildings are placed as single buildings.' },
    { area: 'Other', cs1: 'Pipes, power lines', cs2: 'Built by CS2 along roads', status: 'no', notes: 'CS2 lays water and electricity with the roads.' },
    { area: 'Other', cs1: 'Free-standing trees and rocks', cs2: 'CS2 trees (same species, or same kind and closest height) and boulders', status: 'yes', notes: 'Trees on a road are moved just past its edge; trees inside building lots are left out.' },
    { area: 'Other', cs1: 'Other free-standing props (benches, lamps, decorations)', cs2: '–', status: 'no', notes: 'No sensible CS2 equivalent; counted in the report.' },
  ],

  MD: {
    guideTitle: 'User guide',
    guideIntro: 'From a Cities: Skylines city to a Cities: Skylines II city, step by step.',
    need: 'What you need',
    privacy: 'Privacy',
    seeAlso: 'See also: [FAQ and troubleshooting](faq.md) · [What converts](what-converts.md) · [Français](fr/user-guide.md)',
    faqTitle: 'FAQ and troubleshooting',
    matrixTitle: 'What converts',
    matrixIntro: 'Per city, the web tool also shows this per category with your numbers (Overview → What converts) and lists every approximation in `report.json`.',
    matrixHead: '| CS1 | CS2 | Status | Notes |',
    generated: 'Generated from web/src/core/docs.ts by `node web/scripts/gen-docs.mjs` ({src}). Edit the source, not this file.',
  },
};

export function docsFor(lang: Lang): Docs {
  return lang === 'fr' ? DOCS_FR : DOCS_EN;
}

// English names kept for the existing imports.
export const { STATUS_TEXT, PRIVACY, REQUIREMENTS, GUIDE, FAQ, MATRIX } = DOCS_EN;

// ---------------------------------------------------------------- markdown rendering (docs/guide, docs/guide/fr)

const generated = (d: Docs, src: string) => `<!-- ${d.MD.generated.replace('{src}', src)} -->`;

export function guideMarkdown(lang: Lang = 'en'): string {
  const d = docsFor(lang);
  const out = [generated(d, 'GUIDE'), '', `# ${d.MD.guideTitle}`, '', d.MD.guideIntro, '', `## ${d.MD.need}`, ''];
  for (const r of d.REQUIREMENTS) { out.push(`**${r.game}**`, ''); for (const i of r.items) out.push(`- ${i}`); out.push(''); }
  d.GUIDE.forEach((s, i) => { out.push(`## ${i + 1}. ${s.title}`, ''); for (const p of s.body) out.push(p, ''); });
  out.push(`## ${d.MD.privacy}`, '', ...d.PRIVACY.flatMap(p => [p, '']), d.MD.seeAlso, '');
  return out.join('\n');
}

export function faqMarkdown(lang: Lang = 'en'): string {
  const d = docsFor(lang);
  const out = [generated(d, 'FAQ'), '', `# ${d.MD.faqTitle}`, ''];
  for (const f of d.FAQ) { out.push(`## ${f.q}`, ''); for (const p of f.a) out.push(p, ''); }
  return out.join('\n');
}

export function matrixMarkdown(lang: Lang = 'en'): string {
  const d = docsFor(lang);
  const out = [generated(d, 'MATRIX'), '', `# ${d.MD.matrixTitle}`, '', d.MD.matrixIntro, ''];
  let area = '';
  for (const r of d.MATRIX) {
    if (r.area !== area) { area = r.area; out.push('', `## ${area}`, '', d.MD.matrixHead, '| --- | --- | --- | --- |'); }
    out.push(`| ${r.cs1} | ${r.cs2} | ${d.STATUS_TEXT[r.status]} | ${r.notes || ' '} |`);
  }
  out.push('');
  return out.join('\n');
}
