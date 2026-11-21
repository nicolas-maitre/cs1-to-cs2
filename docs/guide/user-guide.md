<!-- Generated from web/src/core/docs.ts by `node web/scripts/gen-docs.mjs` (GUIDE). Edit the source, not this file. -->

# User guide

From a Cities: Skylines city to a Cities: Skylines II city in seven steps.

## What you need

**Cities: Skylines (CS1)**

- The city you want to convert, and the **CS1 → CS2 Exporter** mod (no other mod is required).
- Works with cities that use 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool and Vanilla+ / workshop roads; their data is exported when the mod is enabled.

**Cities: Skylines II (CS2)**

- CS2 1.6 or later, with the **Road Builder** mod (converted road types are Road Builder roads).
- The **Traffic** mod for TM:PE priority signs, lane arrows and lane connections (optional).
- The **CS1 → CS2 Importer** mod, which builds the package in your city.

## 1. Install the exporter in Cities: Skylines

Subscribe to the **CS1 → CS2 Exporter** mod and enable it in Content Manager → Mods. (Until it is on the Workshop, build it from source: see the project README.)

The exporter only reads your city. It never saves or changes it.

## 2. Export your city

Load the city, then open Options → Mod Settings → **CS1 → CS2 Exporter** → **Export city**.

It writes `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Cs1Exporter\<city>-<date>.cs1x` in a few seconds and shows a summary.

## 3. Open the export in the web tool

Drop the `.cs1x` on the cs1conv page, or press **Choose a .cs1x file…**. The file is read in your browser.

If a part of the export failed (for example IMT data), the page says which one; everything else still converts.

## 4. Choose what to convert

CS2 maps are smaller than an 81-tile CS1 map, so you convert a square of up to 14.3 km. Drag the square to move it and its corners to resize it, or press **Auto** for the area with the most roads.

Roads crossing the edge of the square are left out (dashed yellow on the map). Put the edge in open country where you can.

Keyboard: click the map, then use the arrow keys to move the square, + and − to resize it.

## 5. Check the conversion

**Overview** shows what converts per category, the options (zone theme, which parts to convert) and the install steps.

**Mapping** lists every road type with its CS2 equivalent; you can pick another one. **Road types** shows the generated Road Builder roads and any width change.

**Assets** lists the CS1 buildings with the CS2 building chosen for each type, the match score and why; pick another one or skip a type.

**Report** lists everything that is approximated or dropped.

## 6. Download and install the package

Press **Download package (.zip)** and unzip it. It contains `cs1conv-plan.json`, `report.json`, the `roadbuilder` folder and a `README.txt`.

With CS2 closed, copy the files from `roadbuilder` into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`. Road Builder only reads its roads when the game starts. (The importer mod can also do this for you, then asks you to restart.)

## 7. Import in Cities: Skylines II

Start CS2 with Road Builder, the CS1 → CS2 Importer and, if the plan needs it, Traffic. Start a new city; a flat map works best.

Open Options → **CS1 → CS2 importer**, pick the package, press **Check**, then **Import into this city**. The roads are built in a few seconds.

Save the city, then work through `report.json` for what needs manual attention.

## Privacy

cs1conv runs entirely in your browser. Your .cs1x file is read on your computer and never uploaded; there is no server, no account, no analytics and no cookies.

The page remembers your crop, mapping choices and options in your browser's local storage so they are still there next time. Clearing the site data removes them.

See also: [FAQ and troubleshooting](faq.md) · [What converts](what-converts.md)
