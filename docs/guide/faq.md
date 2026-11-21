<!-- Generated from web/src/core/docs.ts by `node web/scripts/gen-docs.mjs` (FAQ). Edit the source, not this file. -->

# FAQ and troubleshooting

## Is my city uploaded anywhere?

No. The web tool runs entirely in your browser; the file never leaves your computer. There is no server behind the page.

## Why can't I convert my whole 81-tile city?

A CS2 map's playable area is about 14.3 km across, smaller than CS1's 81 tiles (17.3 km). The web tool lets you pick the square that fits; you can convert another square into another city.

## Some roads are narrower or wider than in CS1. Why?

Road types are rebuilt with Road Builder, which has fixed lane widths and sidewalks of at most 7 m. A CS1 road with 9 m sidewalks becomes a few metres narrower. Every change is listed under **Road types** and in the report.

## The page says it can't open my file.

Open the `.cs1x` written by the exporter, not a `.crp` save. If it says the format version is too new, reload the page to get the latest tool; if it is too old, update the exporter and export again.

## A section of my export failed.

The rest converts normally. Export again; if it keeps failing, the exporter's message is in the web tool under Overview → File check.

## The importer doesn't list my package.

**Packages on this PC** lists the folders under `%LOCALAPPDATA%\cs1-to-cs2\` that hold a `cs1conv-plan.json`. Unzip the package there, or type its folder in **Package folder**.

## The importer says road types are missing.

Road Builder roads must be in ModsData\RoadBuilder **before** CS2 starts. "Road Builder configs aren't installed": press **Install Road Builder configs**, then restart CS2. "Installed but not loaded yet": restart CS2. Then press **Check** again. Roads of a missing type are skipped.

## My city has none of its CS1 hills, rivers or sea.

The importer doesn't apply the terrain and the water yet: the roads follow the CS2 map's own ground. The package already holds the converted terrain (the `terrain` folder), and the importer will use it in a later version.

## Where are my TM:PE lane connections and lane arrows?

They are in the package but not applied in this version: only the priority signs are (with the Traffic mod). The import report lists the rest.

## Why weren't all my buildings placed?

Zoned (growable) buildings are not placed: CS2 grows its own on the converted zones. Pillars, pipe junctions and poles are rebuilt by CS2. A few CS1 building types have no CS2 equivalent (libraries, taxi stands, cable car stops), and buildings on roads outside the square are left out. The Assets tab shows each type.

## Why did a building end up somewhere else?

CS2 buildings often need more room than their CS1 counterparts. When one doesn't fit at its CS1 spot, the importer tries the next-best CS2 buildings, then free land along an imported road nearby (up to 400 m). Very large buildings may go anywhere in the square. In the Assets tab you can change this per type, or leave the building out instead.

## My metro or train line has no vehicles.

CS2 lines get their vehicles from a depot of their type, and CS1 metro and train lines don't need one. When the city has none, the package adds a default depot or yard at the line's first stop. Connect a rail yard's track to the line by hand: CS2 yards only send trains over connected track.

## Do I need the Traffic mod?

Only for TM:PE priority signs, lane arrows and lane connections. Speed limits, bus lanes, traffic lights, all-way stops, turn bans and crosswalks work without it.

## What happens to timed traffic lights and Node Controller tweaks?

CS2 runs its own light phases and builds junctions itself. Timed lights become plain traffic lights; Node Controller settings are listed in the report.

## European or North American buildings?

Choose the zone theme under Overview → Options. It sets which CS2 residential and commercial zones the converted zones use, and which tree variants are preferred.

## Does the exporter change my CS1 save?

No. It only reads the loaded city and writes the `.cs1x` file.

## Why is the package so large?

Most of it is the terrain: two 4096 × 4096 heightmaps, the size CS2 uses, 64 MB unpacked and about 22 MB in the zip. Everything the CS2 mod needs is in the package, so nothing else has to run on your computer.

## Is my big city too large for the web tool?

The map and the package builder are made for large cities: a 60 000-segment test city stays smooth, and the package is built in the background.

## Can I use the web tool in French?

Yes: it follows your browser's language, and the language menu at the top of the page switches between English and French. The report (`report.json`) stays in English.
