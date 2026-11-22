// User documentation as data, in English (this file) and French (docs.fr.ts): rendered by the web app's Help page and
// written to docs/guide/*.md and docs/guide/fr/*.md by scripts/gen-docs.mjs (a test checks the files are up to date).
// Text uses `backticks` for code and **double stars** for bold; nothing else.

import type { Lang } from './i18n/index.ts';
import { DOCS_FR } from './docs.fr.ts';

export const APP_VERSION = '0.9.5';

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
        'CS2 1.6 or later, with the **Road Builder** mod (road types that match a vanilla CS2 road use it; the others become Road Builder roads).',
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
        '**Mapping** lists every road type with its CS2 equivalent: a vanilla CS2 road when the generated road has the same lanes ("vanilla Small Road (matched)"), else a Road Builder road, with the reason in both cases; you can pick another one. **Prefer vanilla CS2 roads** (in the options, on by default) does the matching; turn it off to make every road a Road Builder road. Each type also has a **Height** setting: "ground follows the road" (the default for streets and tracks: the terrain is shaped to the road\'s CS1 height, with earth slopes on both sides), "CS1 height, retaining walls" (the default for highways and ramps) or "road follows the ground" (the default for paths). **Road types** shows the generated Road Builder roads and any width change.',
        '**Assets** lists the CS1 buildings with the CS2 building chosen for each type, the match score and why; pick another one or skip a type. For each type you can also say what happens if it doesn\'t fit at its spot. By default it is not moved: it slides a little or takes a smaller alternative, and if it still doesn\'t fit, the import report lists it with its CS1 position so you can place it yourself ("Don\'t move it (list it for me to place)"). You can instead let a type look for free land nearby (up to 400 m) or anywhere in the square. When the square has power, water or sewage buildings, **Overview** shows a notice with one button, **Let them move nearby (≤ 400 m) if they don\'t fit**, which does that for all of them at once: CS2\'s are often much bigger than CS1\'s, and without them the city has no power or water.',
        '**Report** lists everything that is approximated or dropped.',
      ],
    },
    {
      title: 'Download the package',
      body: [
        'Press **Download package (.zip)**. Building it takes a few seconds: the terrain is two 4096 × 4096 heightmaps.',
        'There is no need to unzip it: the importer takes the zip and unpacks it itself. It holds `cs1conv-plan.json`, `report.json`, the `roadbuilder` and `terrain` folders and a `README.txt`.',
      ],
    },
    {
      title: 'Install the mods and the road types in CS2',
      body: [
        'Enable **Road Builder** and the **CS1 → CS2 Importer**. **Traffic** is optional: it sets the priority signs, lane connections, lane arrows and U-turn rules. The line at the bottom of the importer\'s page says whether Road Builder and Traffic are running.',
        'Start CS2 and open Options → **CS1 → CS2 importer** (tab **Import**). Press **Browse…** and choose the downloaded zip: the importer unpacks it under `%LOCALAPPDATA%\\cs1-to-cs2\\packages\\`, shows "Unpacked <zip> to <folder>." and checks it. **Open packages folder** shows that folder in Explorer.',
        'Road Builder reads its road types only when the game starts, so install them now: press **Install Road Builder configs**, confirm, wait for "Copied N Road Builder configs", then **restart the game**. (Or, with CS2 closed, copy the files from the package\'s `roadbuilder` folder into `%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder\\`; the configs of other converted cities can stay there.)',
      ],
    },
    {
      title: 'Start a new city',
      body: [
        'Start a new game (not the editor). Any map works, since the importer replaces its terrain; Sunshine Peninsula has the fewest pre-built roads to remove.',
      ],
    },
    {
      title: 'Check the package',
      body: [
        'Open Options → **CS1 → CS2 importer** again. In the **Package** group, pick the package in **Packages on this PC** (the ones you used last come first), or use **Browse…**, or paste its folder, its `cs1conv-plan.json` or its zip into **Package folder** and press **Check**.',
        'The status line says what the package holds, for example "Package: Skibidi.02, 2035 roads to build. All 37 road types are available." and "Map: terrain with its surroundings, sea level 182 m, 5 water sources, 20 outside connections.". Its last lines say whether this city\'s terrain is already the package\'s, and whether it drives on the same side of the road as the CS1 city (in a new city, **Prepare map** sets the side). If road types are missing, install the Road Builder configs and restart the game.',
      ],
    },
    {
      title: 'Prepare the map',
      body: [
        'In the **New city** group, press **Prepare map** and confirm. It removes the map\'s own roads, tracks and buildings, replaces its terrain and surroundings with your city\'s, sets the CS1 sea level and water sources (a package without water data gives a dry map), and makes traffic drive on the same side as in the CS1 city. **Status** follows the steps ("Preparing the map… applying the terrain", "… setting up the water") and ends with "Map prepared.", the terrain check ("within 0 m of the package at 95 % of 181 check points"), the side ("Traffic drives on the left, as in the CS1 city.") and "Next: press "Import into this city".". It takes 10 to 20 seconds.',
        'It can\'t be undone: use it in a new city. Without a loaded city (main menu, editor) it stops with "Map not prepared: Start a new city first.". A package exported without the terrain keeps the map\'s own terrain ("Terrain skipped: the package has no terrain").',
      ],
    },
    {
      title: 'Import into the city',
      body: [
        'In the **Import into this city** group, choose the steps: **Clear the area first**, **Zoning**, **Traffic lights, stops, turn bans, crosswalks**, **Priority signs (Traffic mod)**, **Lane connections (Traffic mod)**, **Buildings, trees, districts**, **Transit lines** and, last, **Outside connections** are on by default; the roads are always built first. **Keep this map\'s terrain** is off: the import then refuses a city whose terrain isn\'t the package\'s; turn it on to import onto the map\'s own ground anyway (only cuts of 4 m or more keep their depth). **Import into a city driving on the other side** is off too: the import then refuses a city whose traffic drives on the other side of the road than the CS1 city (**Prepare map** sets the right side in a new city); turn it on to import anyway, and roads, one-way streets and lane connections come out mirrored. **Add a transformer station next to each imported power plant** is off by default: the import report then names each coal, gas or incineration plant that needs a transformer (CS2 plants only feed the high-voltage grid), and the city keeps its power through the outside connection.',
        'Press **Import into this city** and confirm. **Status** shows the progress ("Importing… batch 3 of 6", then each step) and ends with a summary per step, for example "Done in 4.33 s: 2035 of 2035 roads built.", "Transit: 9 of 9 lines, 145 of 145 stops." and "Outside connections: 20 of 20 (13 road, 7 train, 4 electricity, 4 water).". A step you turned off reads "skipped: turned off in the Options". The summary also gives the path of the full report, under `%LOCALAPPDATA%\\cs1-to-cs2\\import-reports\\`.',
        'A 3 km square takes under a minute: the roads about 4 seconds, the transit lines the longest. The game stays responsive, and **Cancel** (group **Status**) stops after the current step; what was built so far stays.',
      ],
    },
    {
      title: 'After the import',
      body: [
        'Unpause for a few minutes: CS2 fills the rivers and lakes from the water sources. Then save the city. Homes get electricity, water and sewage from the start: the import adds electricity and water outside connections at the edge of the imported roads, on top of your converted power plants and pumps. Zoned buildings then grow on the converted zones.',
        'To undo, press **Remove imported** and confirm: it deletes the roads, buildings, trees and props, districts and areas, transit lines and outside connections the imports created, even after saving and loading. Zoning and traffic settings go with the roads; the base map removed by **Prepare map** doesn\'t come back.',
        'Read the web tool\'s **Report** tab (or `report.json`) and the import summary for what needs a hand:',
        'Lane connections, lane arrows and U-turn rules apply at junctions; lane splits where a road continues into another without a junction stay as CS2 builds them (about 8 % of connections in our tests). In a city that drives on the left, **Prepare map** sets the driving side from the CS1 city; roads, one-ways and lane connections follow it. About 7 % of the zone cells find no CS2 zone block, because CS2 blocks are shallower. Coal, gas and incineration plants feed CS2\'s high-voltage grid, which reaches the city only through a transformer station: turn on **Add a transformer station next to each imported power plant**, or place one yourself (the import report names each plant). Wind turbines and other low-voltage producers are linked to the nearest road automatically. A plant that stands away from every road produces nothing until you give it one (the report names it). Metro and train lines need their stations from the buildings step; a metro or train line whose track stays inside the square gets no yard: place one yourself (the import report names the line). Where two CS1 roads leave a junction at a very shallow angle, CS2 merges their first metres into one road and splits them a little further on, connected as in CS1. A few overlapping CS1 bits that CS2 can\'t fit (for example a road a few metres from a parallel road) are left out and listed in the report.',
      ],
    },
  ],

  FAQ: [
    { q: 'Is my city uploaded anywhere?', a: ['No. The web tool runs entirely in your browser; the file never leaves your computer. There is no server behind the page.'] },
    { q: 'Why can\'t I convert my whole 81-tile city?', a: ['A CS2 map\'s playable area is about 14.3 km across, smaller than CS1\'s 81 tiles (17.3 km). The web tool lets you pick the square that fits; you can convert another square into another city.'] },
    { q: 'Why are some roads vanilla CS2 roads and others Road Builder roads?', a: ['When the road cs1conv would generate has the same lanes as a vanilla CS2 road (same driving lanes and directions on each side, each within 0.5 m in width, parking and bike lanes on the same sides, the same median, a total width within 1 m and the same speed), the vanilla road is used: fewer Road Builder configs to install, and the roads look like the game\'s own. Trees, bus lanes and other changes from IMT or TM:PE usually keep a road on Road Builder. **Mapping** gives the reason for each type. To force Road Builder, pick **Road Builder** for the type, or turn off **Prefer vanilla CS2 roads**.'] },
    { q: 'Why is the terrain reshaped along the roads?', a: ['CS1 flattens the ground under its roads, but the exported heightmap is too coarse (16 m cells) to keep that. So the package burns each street\'s CS1 height profile back into the CS2 terrain, with earth slopes back to the natural ground, and the roads lie exactly at their CS1 height instead of following bumps. Highways keep their CS1 height with retaining walls, and paths follow the ground. Change it per road type in the **Mapping** tab ("Height").'] },
    { q: 'Some roads are narrower or wider than in CS1. Why?', a: ['Road types are rebuilt with Road Builder, which has fixed lane widths and sidewalks of at most 7 m. A CS1 road with 9 m sidewalks becomes a few metres narrower. Every change is listed under **Road types** and in the report.'] },
    { q: 'The page says it can\'t open my file.', a: ['Open the `.cs1x` written by the exporter, not a `.crp` save. If it says the format version is too new, reload the page to get the latest tool; if it is too old, update the exporter and export again.'] },
    { q: 'A section of my export failed.', a: ['The rest converts normally. Export again; if it keeps failing, the exporter\'s message is in the web tool under Overview → File check.'] },
    { q: 'The importer doesn\'t list my package.', a: ['**Packages on this PC** lists the packages you used last, then the folders under `%LOCALAPPDATA%\\cs1-to-cs2\\` that hold a `cs1conv-plan.json`. Press **Browse…** and choose the downloaded zip (the importer unpacks it under `packages\\`) or a `cs1conv-plan.json`, or paste its path into **Package folder**. **Open packages folder** opens that folder in Explorer.'] },
    { q: 'The importer says road types are missing.', a: ['Road Builder roads must be in ModsData\\RoadBuilder **before** CS2 starts. "Road Builder configs aren\'t installed": press **Install Road Builder configs**, then restart CS2. "Installed but not loaded yet": restart CS2. "Road Builder isn\'t running": enable Road Builder in your playset and restart. Then press **Check** again. Roads of a missing type are skipped.'] },
    { q: 'My city has none of its CS1 hills, rivers or sea.', a: ['Press **Prepare map** in a new city before importing: it replaces the map\'s terrain and surroundings with your city\'s and sets the sea level and the water sources. Then unpause for a few minutes so CS2 fills the rivers and lakes. A package made without water data gives a dry map.'] },
    { q: 'Where are my TM:PE lane connections and lane arrows?', a: ['**Lane connections (Traffic mod)** recreates them, with the U-turn rules, at junctions; it is on by default and needs the Traffic mod. Where a road just continues into another without a junction, CS2 keeps its own lane continuation, and lane splits there can\'t be reproduced. The import summary says how many CS1 lane references were matched. They survive saving and reloading the city: the import\'s last step, the **lane connections check**, rewrites any junction whose roads a later step changed (for example a bus stop bay), and its line in the summary says how many.'] },
    { q: 'How do I undo an import?', a: ['Press **Remove imported**: it deletes the roads, buildings, trees and props, districts and areas, transit lines and outside connections the imports created in this city, even after saving and loading. Zoning and traffic settings go with the roads. The base map removed by **Prepare map** doesn\'t come back; start a new city for that.'] },
    { q: 'The import says "This package is for a left-hand city; this city drives on the right" (or the other way round).', a: ['The CS1 city and this CS2 city drive on different sides of the road. Start a new city and press **Prepare map**: it sets the CS1 city\'s driving side before any road is built. To import anyway, turn on **Import into a city driving on the other side**; roads, one-way streets and lane connections then come out mirrored.'] },
    { q: 'The import says this city\'s terrain isn\'t the package\'s.', a: ['Start a new city and press **Prepare map** before importing. To build on the map\'s own ground anyway, turn on **Keep this map\'s terrain**: roads then follow that ground, and only cuts of 4 m or more keep their CS1 depth.'] },
    { q: 'Why weren\'t all my buildings placed?', a: ['Zoned (growable) buildings are not placed: CS2 grows its own on the converted zones. Pillars, pipe junctions and poles are rebuilt by CS2. A few CS1 building types have no CS2 equivalent (libraries, taxi stands, cable car stops), and buildings on roads outside the square are left out. The Assets tab shows each type.'] },
    { q: 'A building is missing, or ended up somewhere else (my hospital moved). Why?', a: ['CS2 buildings often need more room than their CS1 counterparts. When one doesn\'t fit at its CS1 spot, the importer slides it a little or tries a smaller CS2 alternative. If it still doesn\'t fit, it is **not moved** elsewhere: the import report lists it as "place it yourself" with its CS1 position, and you place it by hand. A building only goes somewhere else if you set its type to look for free land nearby (up to 400 m) or anywhere in the square in the Assets tab, or, for power, water and sewage buildings, if you pressed **Let them move nearby** in Overview. One exception: when none of a city\'s bus (or tram) depots fits at its CS1 spot, one of them is placed nearby, because its lines get no vehicles without a depot; the import report says so.'] },
    { q: 'My metro or train line has no vehicles.', a: ['CS2 lines get their vehicles from a depot of their type, and CS1 metro and train lines don\'t need one. Rail and subway yards are never placed inside your city: by default the importer puts a compact yard at the edge of the square, where the line\'s track leaves it, and links it. A line whose track stays inside the square gets none; the import report names it, and you place a yard yourself. Under Overview → Options you can choose "I\'ll place it myself" for metro or train. Bus and tram lines always get a depot: if your CS1 city had none of that type, a default depot is added near the first stop; if its depots of that type don\'t fit at their CS1 spot, one of them is placed nearby. A known limit of this version: even with their yard, trains and metros don\'t leave it yet. The lines, stations and stops are all there.'] },
    { q: 'Do I need the Traffic mod?', a: ['Only for TM:PE priority signs, lane connections, lane arrows and U-turn rules. Speed limits, bus lanes, traffic lights, all-way stops, turn bans and crosswalks work without it.'] },
    { q: 'What happens to timed traffic lights and Node Controller tweaks?', a: ['CS2 runs its own light phases and builds junctions itself. Timed lights become plain traffic lights; Node Controller shifts are converted (the road ends sit off the node centre, so lanes line up as in CS1), and so are offsets at junctions (stretched junctions keep their length, with smooth splits and merges); the other Node Controller settings are listed in the report.'] },
    { q: 'European or North American buildings?', a: ['Choose the zone theme under Overview → Options. It sets which CS2 residential and commercial zones the converted zones use, and which tree variants are preferred.'] },
    { q: 'Does the exporter change my CS1 save?', a: ['No. It only reads the loaded city and writes the `.cs1x` file.'] },
    { q: 'Why is the package so large?', a: ['Most of it is the terrain: two 4096 × 4096 heightmaps, the size CS2 uses, 64 MB unpacked and about 22 MB in the zip. Everything the CS2 mod needs is in the package, so nothing else has to run on your computer.'] },
    { q: 'Is my big city too large for the web tool?', a: ['The map and the package builder are made for large cities: a 60 000-segment test city stays smooth, and the package is built in the background.'] },
    { q: 'Can I use the web tool in French?', a: ['Yes: it follows your browser\'s language, and the language menu at the top of the page switches between English and French. The report (`report.json`) stays in English.'] },
  ],

  MATRIX: [
    { area: 'Roads', cs1: 'Roads, highways, one-ways, Vanilla+ and workshop roads', cs2: 'A matching vanilla CS2 road, else a generated Road Builder road', status: 'approx', notes: 'Lanes, medians, parking, bus and tram lanes, trees and speed are kept; widths can change (Road Builder sidewalks max 7 m). A vanilla road is used only when the lanes match (each within 0.5 m, total within 1 m).' },
    { area: 'Roads', cs1: 'Elevated roads, bridges, slopes, tunnels', cs2: 'The same Road Builder road at the CS1 height', status: 'yes', notes: 'Road Builder makes the elevated, bridge and tunnel forms itself.' },
    { area: 'Roads', cs1: 'Painted bike lanes in the carriageway', cs2: 'Sidewalk with bike lane', status: 'approx', notes: 'Only at the road edge; CS2 has no painted carriageway bike lanes.' },
    { area: 'Rail and paths', cs1: 'Train, metro and tram tracks', cs2: 'CS2 double, two-way or one-way tracks', status: 'yes', notes: '' },
    { area: 'Rail and paths', cs1: 'Pedestrian and bike paths', cs2: 'Pavement paths, bike paths', status: 'yes', notes: '' },
    { area: 'Rail and paths', cs1: 'Monorail, cable car', cs2: '–', status: 'no', notes: 'CS2 has neither.' },
    { area: 'IMT', cs1: 'Red paint over a whole lane / pavement or grass island over a whole lane', cs2: 'Bus lane / median in the Road Builder road', status: 'yes', notes: 'Partial fills and grey paint are reported.' },
    { area: 'IMT', cs1: 'Pavement, grass and gravel fills at junctions', cs2: 'CS2 surfaces (pavement, grass, sand)', status: 'yes', notes: 'Placed with the buildings step.' },
    { area: 'IMT', cs1: 'Painted markings at junctions', cs2: '–', status: 'report', notes: 'No CS2 equivalent yet (decals are planned).' },
    { area: 'Terrain and water', cs1: 'Terrain heightmap (and the land around the square)', cs2: 'CS2 terrain at the same heights, plus the world map around it', status: 'yes', notes: 'Applied by the importer\'s Prepare map, in a new city, before the import.' },
    { area: 'Terrain and water', cs1: 'Ground shaped to the roads (CS1 flattens it under its roads)', cs2: 'Road profiles burned into the CS2 heightmap, with earth slopes', status: 'yes', notes: 'Per road type in the Mapping tab: ground follows the road (streets, tracks), CS1 height with retaining walls (highways), or road follows the ground (paths).' },
    { area: 'Terrain and water', cs1: 'Sea level, rivers and lakes (natural water sources)', cs2: 'CS2 sea level and water sources', status: 'approx', notes: 'Set by Prepare map. CS2 re-simulates the water; extra sources keep rivers and lakes above the sea filled. Pumps and drains come with their buildings.' },
    { area: 'Terrain and water', cs1: 'Highways and railways leaving the square', cs2: 'CS2 outside connections', status: 'yes', notes: 'The importer\'s last step (Outside connections), so traffic and trains can enter and leave the city.' },
    { area: 'Zoning', cs1: 'Residential, commercial, industrial and office zones', cs2: 'CS2 zones (EU or NA theme)', status: 'approx', notes: 'District specialisations become generic zones.' },
    { area: 'Traffic (TM:PE)', cs1: 'Speed limits, bus-only lanes', cs2: 'Road Builder variants with that speed / bus lane', status: 'yes', notes: 'CS2 has one speed per road.' },
    { area: 'Traffic (TM:PE)', cs1: 'Traffic lights, all-stop junctions, turn restrictions from lane arrows, crosswalks', cs2: 'Vanilla CS2 junction settings', status: 'yes', notes: '' },
    { area: 'Traffic (TM:PE)', cs1: 'Priority signs', cs2: 'Traffic mod', status: 'yes', notes: 'Needs the Traffic mod in CS2.' },
    { area: 'Traffic (TM:PE)', cs1: 'Lane arrows, lane connections, U-turn rules', cs2: 'Traffic mod lane connections and U-turn settings', status: 'approx', notes: 'Applied exactly at junctions (Lane connections (Traffic mod), on by default), in right- and left-hand cities; lane splits where a road continues without a junction stay as CS2 builds them.' },
    { area: 'Traffic (TM:PE)', cs1: 'Timed traffic lights, turn on red, blocked junctions, parking rules', cs2: '–', status: 'report', notes: 'Timed lights become plain traffic lights.' },
    { area: 'Node Controller', cs1: 'Segment-end shifts (lanes lined up across junctions and lane drops)', cs2: 'Road ends off the node centre, as in CS1', status: 'yes', notes: 'Not on free-form nodes, which CS2 has no equivalent for.' },
    { area: 'Node Controller', cs1: 'Offsets at junctions (stretched junctions)', cs2: 'Road ends pulled back, a longer junction', status: 'yes', notes: 'At junctions of 3+ roads, where the offset is more than 5 m above Node Controller\'s default (at most 45 % of the segment); option "Node Controller offsets at junctions".' },
    { area: 'Node Controller', cs1: 'Node types, offsets between 2 roads, slopes, twists, hidden markings', cs2: '–', status: 'report', notes: 'CS2 builds junction geometry itself.' },
    { area: 'Transit', cs1: 'Bus and tram lines', cs2: 'CS2 lines with colour, name, number and stops', status: 'approx', notes: 'Trolleybus, tourist and intercity buses become bus lines.' },
    { area: 'Transit', cs1: 'Train, metro, ship and plane lines', cs2: 'CS2 lines at the placed stations', status: 'approx', notes: 'Need their stations from the buildings step. Metro and train yards go at the crop edge where the track leaves it (or you place them); never inside the city.' },
    { area: 'Buildings', cs1: 'Service buildings, stations, depots, parks, landmarks', cs2: 'Closest CS2 building (same service, then capacity and size)', status: 'approx', notes: 'Check or change each type in the Assets tab. A building that doesn\'t fit at its spot is not moved by default: it is listed for you to place. Bus and tram depots are the exception when none of their type fits: one is placed nearby so the lines get vehicles.' },
    { area: 'Buildings', cs1: 'Zoned (growable) buildings', cs2: 'Grown by CS2 on the converted zones', status: 'yes', notes: 'Not placed one by one.' },
    { area: 'Buildings', cs1: 'Libraries, taxi stands, cable car stops', cs2: '–', status: 'no', notes: 'No CS2 equivalent.' },
    { area: 'Districts', cs1: 'District names and outlines; policies', cs2: 'CS2 districts and policies', status: 'approx', notes: 'Policies with a CS2 equivalent (heavy traffic ban, recycling, …); zone specialisations are reported.' },
    { area: 'Districts', cs1: 'Parks, industry areas, campuses, airports', cs2: '–', status: 'report', notes: 'Their buildings are placed as single buildings.' },
    { area: 'Other', cs1: 'Pipes and power lines along roads', cs2: 'Built by CS2 with the roads', status: 'yes', notes: 'CS2 roads carry water, sewage and low-voltage power themselves.' },
    { area: 'Other', cs1: 'Pipes and power lines away from roads', cs2: 'CS2 pipes and power lines at the same place', status: 'approx', notes: 'Placed by the importer\'s buildings step; a line that runs along a converted road for most of its length is left to the road.' },
    { area: 'Other', cs1: 'Power plants and wind turbines', cs2: 'CS2 power plants, linked to the grid', status: 'approx', notes: 'Wind turbines and other low-voltage producers get a short power line to the nearest road. Coal, gas and incineration plants need a transformer station: an opt-in in the importer\'s options (Add a transformer station next to each imported power plant); otherwise the report names them and the city runs on its outside connection.' },
    { area: 'Other', cs1: 'Electricity and water bought from outside the square', cs2: 'CS2 electricity and water outside connections', status: 'yes', notes: 'The importer\'s Outside connections step adds them at the edge of the imported roads (a short power line and pipe with the connection at the end), so a square without its own plants and pumps still gets power, water and sewage.' },
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
