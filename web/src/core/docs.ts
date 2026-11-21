// User documentation as data: rendered by the web app's Help page and written to docs/guide/*.md by
// scripts/gen-docs.mjs (a test checks the files are up to date). Text uses `backticks` for code and
// **double stars** for bold; nothing else.

export const APP_VERSION = '0.3.0';

export interface DocStep { title: string; body: string[] }
export interface Faq { q: string; a: string[] }
export type MatrixStatus = 'yes' | 'approx' | 'report' | 'no' | 'pending';
export interface MatrixRow { area: string; cs1: string; cs2: string; status: MatrixStatus; notes: string }

export const STATUS_TEXT: Record<MatrixStatus, string> = {
  yes: 'converts',
  approx: 'converts, approximated',
  report: 'listed in the report only',
  no: 'not converted',
  pending: 'in the package; CS2 side in progress',
};

export const PRIVACY = [
  'cs1conv runs entirely in your browser. Your .cs1x file is read on your computer and never uploaded; there is no server, no account, no analytics and no cookies.',
  'The page remembers your crop, mapping choices and options in your browser\'s local storage so they are still there next time. Clearing the site data removes them.',
];

export const REQUIREMENTS: { game: string; items: string[] }[] = [
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
      'The **Traffic** mod for TM:PE priority signs, lane arrows and lane connections (optional).',
      'The **CS1 → CS2 Importer** mod, which builds the package in your city.',
    ],
  },
];

export const GUIDE: DocStep[] = [
  {
    title: 'Install the exporter in Cities: Skylines',
    body: [
      'Subscribe to the **CS1 → CS2 Exporter** mod and enable it in Content Manager → Mods. (Until it is on the Workshop, build it from source: see the project README.)',
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
      'Drop the `.cs1x` on the cs1conv page, or press **Choose a .cs1x file…**. The file is read in your browser.',
      'If a part of the export failed (for example IMT data), the page says which one; everything else still converts.',
    ],
  },
  {
    title: 'Choose what to convert',
    body: [
      'CS2 maps are smaller than an 81-tile CS1 map, so you convert a square of up to 14.3 km. Drag the square to move it and its corners to resize it, or press **Auto** for the area with the most roads.',
      'Roads crossing the edge of the square are left out (dashed yellow on the map). Put the edge in open country where you can.',
      'Keyboard: click the map, then use the arrow keys to move the square, + and − to resize it.',
    ],
  },
  {
    title: 'Check the conversion',
    body: [
      '**Overview** shows what converts per category, the options (zone theme, which parts to convert) and the install steps.',
      '**Mapping** lists every road type with its CS2 equivalent; you can pick another one. **Road types** shows the generated Road Builder roads and any width change.',
      '**Assets** lists the CS1 buildings with the CS2 building chosen for each type, the match score and why; pick another one or skip a type.',
      '**Report** lists everything that is approximated or dropped.',
    ],
  },
  {
    title: 'Download and install the package',
    body: [
      'Press **Download package (.zip)** and unzip it. It contains `cs1conv-plan.json`, `report.json`, the `roadbuilder` folder and a `README.txt`.',
      'With CS2 closed, copy the files from `roadbuilder` into `%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder\\`. Road Builder only reads its roads when the game starts. (The importer mod can also do this for you, then asks you to restart.)',
    ],
  },
  {
    title: 'Import in Cities: Skylines II',
    body: [
      'Start CS2 with Road Builder, the CS1 → CS2 Importer and, if the plan needs it, Traffic. Start a new city; a flat map works best.',
      'Open Options → **CS1 → CS2 importer**, pick the package, press **Check**, then **Import into this city**. The roads are built in a few seconds.',
      'Save the city, then work through `report.json` for what needs manual attention.',
    ],
  },
];

export const FAQ: Faq[] = [
  { q: 'Is my city uploaded anywhere?', a: ['No. The web tool runs entirely in your browser; the file never leaves your computer. There is no server behind the page.'] },
  { q: 'Why can\'t I convert my whole 81-tile city?', a: ['A CS2 map\'s playable area is about 14.3 km across, smaller than CS1\'s 81 tiles (17.3 km). The web tool lets you pick the square that fits; you can convert another square into another city.'] },
  { q: 'Some roads are narrower or wider than in CS1. Why?', a: ['Road types are rebuilt with Road Builder, which has fixed lane widths and sidewalks of at most 7 m. A CS1 road with 9 m sidewalks becomes a few metres narrower. Every change is listed under **Road types** and in the report.'] },
  { q: 'The page says it can\'t open my file.', a: ['Open the `.cs1x` written by the exporter, not a `.crp` save. If it says the format version is too new, reload the page to get the latest tool; if it is too old, update the exporter and export again.'] },
  { q: 'A section of my export failed.', a: ['The rest converts normally. Export again; if it keeps failing, the exporter\'s message is in the web tool under Overview → File check.'] },
  { q: 'CS2 says a road type is missing.', a: ['Road Builder roads must be copied to ModsData\\RoadBuilder **before** CS2 starts. Copy them (or press Install in the importer), then restart CS2.'] },
  { q: 'Why weren\'t all my buildings placed?', a: ['Zoned (growable) buildings are not placed: CS2 grows its own on the converted zones. Pillars, pipe junctions and poles are rebuilt by CS2. A few CS1 building types have no CS2 equivalent (libraries, taxi stands, cable car stops), and buildings on roads outside the square are left out. The Assets tab shows each type.'] },
  { q: 'Do I need the Traffic mod?', a: ['Only for TM:PE priority signs, lane arrows and lane connections. Speed limits, bus lanes, traffic lights, all-way stops, turn bans and crosswalks work without it.'] },
  { q: 'What happens to timed traffic lights and Node Controller tweaks?', a: ['CS2 runs its own light phases and builds junctions itself. Timed lights become plain traffic lights; Node Controller settings are listed in the report.'] },
  { q: 'European or North American buildings?', a: ['Choose the zone theme under Overview → Options. It sets which CS2 residential and commercial zones the converted zones use.'] },
  { q: 'Does the exporter change my CS1 save?', a: ['No. It only reads the loaded city and writes the `.cs1x` file.'] },
  { q: 'Is my big city too large for the web tool?', a: ['The map and the package builder are made for large cities: a 60 000-segment test city stays smooth, and the package is built in the background.'] },
];

export const MATRIX: MatrixRow[] = [
  { area: 'Roads', cs1: 'Roads, highways, one-ways, Vanilla+ and workshop roads', cs2: 'Generated Road Builder roads', status: 'approx', notes: 'Lanes, medians, parking, bus and tram lanes, trees and speed are kept; widths can change (Road Builder sidewalks max 7 m).' },
  { area: 'Roads', cs1: 'Elevated roads, bridges, slopes, tunnels', cs2: 'The same Road Builder road at the CS1 height', status: 'yes', notes: 'Road Builder makes the elevated, bridge and tunnel forms itself.' },
  { area: 'Roads', cs1: 'Painted bike lanes in the carriageway', cs2: 'Sidewalk with bike lane', status: 'approx', notes: 'Only at the road edge; CS2 has no painted carriageway bike lanes.' },
  { area: 'Rail and paths', cs1: 'Train, metro and tram tracks', cs2: 'CS2 double, two-way or one-way tracks', status: 'yes', notes: '' },
  { area: 'Rail and paths', cs1: 'Pedestrian and bike paths', cs2: 'Pavement paths, bike paths', status: 'yes', notes: '' },
  { area: 'Rail and paths', cs1: 'Monorail, cable car', cs2: '–', status: 'no', notes: 'CS2 has neither.' },
  { area: 'IMT', cs1: 'Red paint over a whole lane / pavement or grass island over a whole lane', cs2: 'Bus lane / median in the Road Builder road', status: 'yes', notes: 'Partial fills and grey paint are reported.' },
  { area: 'IMT', cs1: 'Markings at junctions', cs2: '–', status: 'report', notes: 'No CS2 equivalent yet (decals are planned).' },
  { area: 'Terrain and water', cs1: 'Terrain heightmap', cs2: 'CS2 terrain (same heights)', status: 'yes', notes: 'Applied by the terrain step before the roads.' },
  { area: 'Terrain and water', cs1: 'Sea level, rivers and lakes (natural water sources)', cs2: 'CS2 sea level and water sources', status: 'approx', notes: 'CS2 re-simulates the water; pumps and drains come with buildings.' },
  { area: 'Zoning', cs1: 'Residential, commercial, industrial and office zones', cs2: 'CS2 zones (EU or NA theme)', status: 'pending', notes: 'District specialisations become generic zones.' },
  { area: 'Traffic (TM:PE)', cs1: 'Speed limits, bus-only lanes', cs2: 'Road Builder variants with that speed / bus lane', status: 'yes', notes: 'CS2 has one speed per road.' },
  { area: 'Traffic (TM:PE)', cs1: 'Traffic lights, all-stop junctions, turn restrictions from lane arrows, crosswalks', cs2: 'Vanilla CS2 junction settings', status: 'pending', notes: '' },
  { area: 'Traffic (TM:PE)', cs1: 'Priority signs, lane arrows, lane connections, U-turns', cs2: 'Traffic mod', status: 'pending', notes: 'Needs the Traffic mod in CS2.' },
  { area: 'Traffic (TM:PE)', cs1: 'Timed traffic lights, turn on red, blocked junctions, parking rules', cs2: '–', status: 'report', notes: 'Timed lights become plain lights.' },
  { area: 'Node Controller', cs1: 'Node types, offsets, hidden markings', cs2: '–', status: 'report', notes: 'CS2 builds junction geometry itself.' },
  { area: 'Transit', cs1: 'Bus and tram lines', cs2: 'CS2 lines with colour, name and stops', status: 'pending', notes: 'Trolleybus, tourist and intercity buses become bus lines.' },
  { area: 'Transit', cs1: 'Train, metro, ship and plane lines', cs2: 'CS2 lines', status: 'pending', notes: 'Need their stations (placed with the buildings).' },
  { area: 'Buildings', cs1: 'Service buildings, stations, depots, parks, landmarks', cs2: 'Closest CS2 building (same service, then capacity and size)', status: 'pending', notes: 'Check or change each type in the Assets tab.' },
  { area: 'Buildings', cs1: 'Zoned (growable) buildings', cs2: 'Grown by CS2 on the converted zones', status: 'yes', notes: 'Not placed one by one.' },
  { area: 'Buildings', cs1: 'Libraries, taxi stands, cable car stops', cs2: '–', status: 'no', notes: 'No CS2 equivalent.' },
  { area: 'Districts', cs1: 'District names and outlines; policies', cs2: 'CS2 districts and policies', status: 'pending', notes: 'Policies with a CS2 equivalent (heavy traffic ban, recycling, …); zone specialisations are reported.' },
  { area: 'Districts', cs1: 'Parks, industry areas, campuses, airports', cs2: '–', status: 'report', notes: 'Their buildings are placed as single buildings.' },
  { area: 'Other', cs1: 'Pipes, power lines', cs2: 'Built by CS2 along roads', status: 'no', notes: 'CS2 lays water and electricity with the roads.' },
  { area: 'Other', cs1: 'Free-standing props and trees', cs2: '–', status: 'no', notes: 'Exported; conversion is planned.' },
];

// ---------------------------------------------------------------- markdown rendering (docs/guide)

const GENERATED = (src: string) => `<!-- Generated from web/src/core/docs.ts by \`node web/scripts/gen-docs.mjs\` (${src}). Edit the source, not this file. -->`;

export function guideMarkdown(): string {
  const out = [GENERATED('GUIDE'), '', '# User guide', '', 'From a Cities: Skylines city to a Cities: Skylines II city in seven steps.', '', '## What you need', ''];
  for (const r of REQUIREMENTS) { out.push(`**${r.game}**`, ''); for (const i of r.items) out.push(`- ${i}`); out.push(''); }
  GUIDE.forEach((s, i) => { out.push(`## ${i + 1}. ${s.title}`, ''); for (const p of s.body) out.push(p, ''); });
  out.push('## Privacy', '', ...PRIVACY.flatMap(p => [p, '']), 'See also: [FAQ and troubleshooting](faq.md) · [What converts](what-converts.md)', '');
  return out.join('\n');
}

export function faqMarkdown(): string {
  const out = [GENERATED('FAQ'), '', '# FAQ and troubleshooting', ''];
  for (const f of FAQ) { out.push(`## ${f.q}`, ''); for (const p of f.a) out.push(p, ''); }
  return out.join('\n');
}

export function matrixMarkdown(): string {
  const out = [GENERATED('MATRIX'), '', '# What converts', '', 'Per city, the web tool also shows this per category with your numbers (Overview → What converts) and lists every approximation in `report.json`.', ''];
  let area = '';
  for (const r of MATRIX) {
    if (r.area !== area) { area = r.area; out.push('', `## ${area}`, '', '| CS1 | CS2 | Status | Notes |', '| --- | --- | --- | --- |'); }
    out.push(`| ${r.cs1} | ${r.cs2} | ${STATUS_TEXT[r.status]} | ${r.notes || ' '} |`);
  }
  out.push('');
  return out.join('\n');
}
