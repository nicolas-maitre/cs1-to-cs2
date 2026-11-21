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

**Packages on this PC** lists the packages you used last, then the folders under `%LOCALAPPDATA%\cs1-to-cs2\` that hold a `cs1conv-plan.json`. Press **Browse…** and choose the downloaded zip (the importer unpacks it under `packages\`) or a `cs1conv-plan.json`, or paste its path into **Package folder**. **Open packages folder** opens that folder in Explorer.

## The importer says road types are missing.

Road Builder roads must be in ModsData\RoadBuilder **before** CS2 starts. "Road Builder configs aren't installed": press **Install Road Builder configs**, then restart CS2. "Installed but not loaded yet": restart CS2. "Road Builder isn't running": enable Road Builder in your playset and restart. Then press **Check** again. Roads of a missing type are skipped.

## My city has none of its CS1 hills, rivers or sea.

Press **Prepare map** in a new city before importing: it replaces the map's terrain and surroundings with your city's and sets the sea level and the water sources. Then unpause for a few minutes so CS2 fills the rivers and lakes. A package made without water data gives a dry map.

## Where are my TM:PE lane connections and lane arrows?

**Lane connections (Traffic mod)** recreates them, with the U-turn rules, at junctions; it is on by default and needs the Traffic mod. Where a road just continues into another without a junction, CS2 keeps its own lane continuation, and lane splits there can't be reproduced. The import summary says how many CS1 lane references were matched.

## How do I undo an import?

Press **Remove imported**: it deletes the roads, buildings, trees and props, districts and areas, transit lines and outside connections the imports created in this city, even after saving and loading. Zoning and traffic settings go with the roads. The base map removed by **Prepare map** doesn't come back; start a new city for that.

## The import says this city's terrain isn't the package's.

Start a new city and press **Prepare map** before importing. To build on the map's own ground anyway, turn on **Keep this map's terrain**: roads then follow that ground, and only cuts of 4 m or more keep their CS1 depth.

## Why weren't all my buildings placed?

Zoned (growable) buildings are not placed: CS2 grows its own on the converted zones. Pillars, pipe junctions and poles are rebuilt by CS2. A few CS1 building types have no CS2 equivalent (libraries, taxi stands, cable car stops), and buildings on roads outside the square are left out. The Assets tab shows each type.

## Why did a building end up somewhere else?

CS2 buildings often need more room than their CS1 counterparts. When one doesn't fit at its CS1 spot, the importer tries the next-best CS2 buildings, then free land along an imported road nearby (up to 400 m). Very large buildings may go anywhere in the square. In the Assets tab you can change this per type, or leave the building out instead.

## My metro or train line has no vehicles.

CS2 lines get their vehicles from a depot of their type, and CS1 metro and train lines don't need one. Rail and subway yards are never placed inside your city: by default the importer puts a compact yard at the edge of the square, where the line's track leaves it, and links it. A line whose track stays inside the square gets none; the import report names it, and you place a yard yourself. Under Overview → Options you can choose "I'll place it myself" for metro or train. Bus and tram lines get a default depot near their first stop.

## Do I need the Traffic mod?

Only for TM:PE priority signs, lane connections, lane arrows and U-turn rules. Speed limits, bus lanes, traffic lights, all-way stops, turn bans and crosswalks work without it.

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
