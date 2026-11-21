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

## CS2 says a road type is missing.

Road Builder roads must be copied to ModsData\RoadBuilder **before** CS2 starts. Copy them (or press Install in the importer), then restart CS2.

## Why weren't all my buildings placed?

Zoned (growable) buildings are not placed: CS2 grows its own on the converted zones. Pillars, pipe junctions and poles are rebuilt by CS2. A few CS1 building types have no CS2 equivalent (libraries, taxi stands, cable car stops), and buildings on roads outside the square are left out. The Assets tab shows each type.

## Why did a building end up somewhere else?

CS2 buildings often need more room than their CS1 counterparts. When one doesn't fit at its CS1 spot, the importer tries the next-best CS2 buildings, then free land along an imported road nearby (up to 400 m). Very large buildings may go anywhere in the square. In the Assets tab you can change this per type, or leave the building out instead.

## Do I need the Traffic mod?

Only for TM:PE priority signs, lane arrows and lane connections. Speed limits, bus lanes, traffic lights, all-way stops, turn bans and crosswalks work without it.

## What happens to timed traffic lights and Node Controller tweaks?

CS2 runs its own light phases and builds junctions itself. Timed lights become plain traffic lights; Node Controller settings are listed in the report.

## European or North American buildings?

Choose the zone theme under Overview → Options. It sets which CS2 residential and commercial zones the converted zones use.

## Does the exporter change my CS1 save?

No. It only reads the loaded city and writes the `.cs1x` file.

## Why is the package so large?

Most of it is the terrain: two 4096 × 4096 heightmaps, the size CS2 uses, 64 MB unpacked and about 22 MB in the zip. Everything the CS2 mod needs is in the package, so nothing else has to run on your computer.

## Is my big city too large for the web tool?

The map and the package builder are made for large cities: a 60 000-segment test city stays smooth, and the package is built in the background.
