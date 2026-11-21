<!-- Generated from web/src/core/docs.ts by `node web/scripts/gen-docs.mjs` (GUIDE). Edit the source, not this file. -->

# User guide

From a Cities: Skylines city to a Cities: Skylines II city, step by step.

## What you need

**Cities: Skylines (CS1)**

- The city you want to convert, and the **CS1 → CS2 Exporter** mod (no other mod is required).
- Works with cities that use 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool and Vanilla+ / workshop roads; their data is exported when the mod is enabled.

**Cities: Skylines II (CS2)**

- CS2 1.6 or later, with the **Road Builder** mod (converted road types are Road Builder roads).
- The **CS1 → CS2 Importer** mod, which builds the package in your city.
- The **Traffic** mod for TM:PE priority signs, lane arrows and lane connections (optional).

## 1. Install the exporter in Cities: Skylines

Add the **CS1 → CS2 Exporter** mod and enable it in Content Manager → Mods. Until it is published, build it from source (see the project README); a Release build puts it in your local Mods folder.

The exporter only reads your city. It never saves or changes it.

## 2. Export your city

Load the city, then open Options → Mod Settings → **CS1 → CS2 Exporter** → **Export city**.

It writes `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Cs1Exporter\<city>-<date>.cs1x` in a few seconds and shows a summary.

## 3. Open the export in the web tool

Drop the `.cs1x` on the cs1conv page, or press **Choose a .cs1x file…**. The file is read in your browser; nothing is uploaded.

If a part of the export failed (for example IMT data), the page says which one; everything else still converts.

## 4. Choose what to convert

CS2 maps are smaller than an 81-tile CS1 map, so you convert a square of up to 14.3 km. Drag the square to move it and its corners to resize it, or press **Auto** for the area with the most roads.

Roads crossing the edge of the square are left out (yellow on the map). Put the edge in open country where you can. Highways and railways that leave the square become CS2 outside connections.

Keyboard: click the map, then use the arrow keys to move the square, + and − to resize it.

## 5. Check the conversion

**Overview** shows what converts per category with your city's numbers, a preview of the terrain, the options (zone theme, which parts to convert) and the install steps.

**Mapping** lists every road type with its CS2 equivalent; you can pick another one. **Road types** shows the generated Road Builder roads and any width change.

**Assets** lists the CS1 buildings with the CS2 building chosen for each type, the match score and why; pick another one or skip a type. For each type you can also say what happens if it doesn't fit at its spot: look for free land nearby (up to 400 m), anywhere in the square, or leave it out. Very large buildings (universities, power plants, cargo terminals) look anywhere by default.

**Report** lists everything that is approximated or dropped.

## 6. Download the package

Press **Download package (.zip)**. Building it takes a few seconds: the terrain is two 4096 × 4096 heightmaps.

Unzip it into its own folder under `%LOCALAPPDATA%\cs1-to-cs2\` (paste that path into the Explorer address bar), for example `%LOCALAPPDATA%\cs1-to-cs2\my-city\`. The importer then lists it under **Packages on this PC**. Any other folder works too; you then type it in **Package folder**.

The folder holds `cs1conv-plan.json`, `report.json`, the `roadbuilder` and `terrain` folders and a `README.txt`.

## 7. Install the mods and the road types in CS2

Enable **Road Builder** and the **CS1 → CS2 Importer**. **Traffic** is optional: without it, the priority signs are skipped.

Road Builder reads its road types only when the game starts, so install them first. Start CS2, open Options → **CS1 → CS2 importer**, pick the package (as in step 9) and press **Install Road Builder configs**. Confirm, wait for "Copied N Road Builder configs", then **restart the game**. Or, with CS2 closed, copy the files from the package's `roadbuilder` folder into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`.

## 8. Start a new city

Start a new game (not the editor). Any map works; Sunshine Peninsula has the fewest pre-built roads.

The importer doesn't apply the terrain, the water and the outside connections yet: the roads follow the map's own ground, and only cuts of 4 m or more keep their CS1 depth. The package already holds them (the `terrain` folder) for when it does.

Save the city now: the import can't be undone.

## 9. Check the package

Open Options → **CS1 → CS2 importer** (tab **Import**). In the **Package** group, pick the package in **Packages on this PC**: it lists the packages under `%LOCALAPPDATA%\cs1-to-cs2\`, newest first, and checks the one you pick. Or paste the package's folder into **Package folder** and press **Check**.

The status line says what the package holds, for example "Package: Skibidi.02, 2035 roads to build. All 37 road types are available." If road types are missing, install the Road Builder configs and restart the game. If there are roads already in the area, keep **Clear the area first** on.

## 10. Import into the city

In the **Import into this city** group, the steps are all on by default: **Clear the area first**, **Zoning**, **Traffic lights, stops, turn bans, crosswalks**, **Priority signs (Traffic mod)**, **Buildings, trees, districts** and **Transit lines**. Turn off what you don't want; the roads are always built first.

Press **Import into this city** and confirm. **Status** shows the progress ("Importing… batch 3 of 6", then each step) and ends with a summary per step, for example "Done in 4.33 s: 2035 of 2035 roads built." and "Transit: 9 of 9 lines, 145 of 145 stops.". It also gives the path of the full report, under `%LOCALAPPDATA%\cs1-to-cs2\import-reports\`.

A 3 km square takes well under a minute: the roads about 4 seconds, the transit lines the longest. The game stays responsive.

## 11. After the import

Save the city. It still needs water, electricity and services to grow, like any new city; zoned buildings then grow on the converted zones.

Read the web tool's **Report** tab (or `report.json`) and the import summary for what needs a hand:

TM:PE lane connections, lane arrows and U-turn rules are not applied in this version (only the priority signs are); the import report lists them. About 7 % of the zone cells find no CS2 zone block, because CS2 blocks are shallower. Metro and train lines need their stations from the buildings step. Rail yards added for them need their track connected to the line by hand. A few roads with shallow grade separations get joined at a junction by CS2.

## Privacy

cs1conv runs entirely in your browser. Your .cs1x file is read on your computer and never uploaded; there is no server, no account, no analytics and no cookies.

The page remembers your crop, mapping choices, options and language in your browser's local storage so they are still there next time. Clearing the site data removes them.

See also: [FAQ and troubleshooting](faq.md) · [What converts](what-converts.md) · [Français](fr/user-guide.md)
