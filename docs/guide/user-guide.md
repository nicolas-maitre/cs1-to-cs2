<!-- Generated from web/src/core/docs.ts by `node web/scripts/gen-docs.mjs` (GUIDE). Edit the source, not this file. -->

# User guide

From a Cities: Skylines city to a Cities: Skylines II city, step by step.

## What you need

**Cities: Skylines (CS1)**

- The city you want to convert, and the **CS1 → CS2 Exporter** mod (no other mod is required).
- Works with cities that use 81 Tiles, TM:PE, Node Controller (Renewal), Intersection Marking Tool and Vanilla+ / workshop roads; their data is exported when the mod is enabled.

**Cities: Skylines II (CS2)**

- CS2 1.6 or later, with the **Road Builder** mod (road types that match a vanilla CS2 road use it; the others become Road Builder roads).
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

**Mapping** lists every road type with its CS2 equivalent: a vanilla CS2 road when the generated road has the same lanes ("vanilla Small Road (matched)"), else a Road Builder road, with the reason in both cases; you can pick another one. **Prefer vanilla CS2 roads** (in the options, on by default) does the matching; turn it off to make every road a Road Builder road. Each type also has a **Height** setting: "ground follows the road" (the default for streets and tracks: the terrain is shaped to the road's CS1 height, with earth slopes on both sides), "CS1 height, retaining walls" (the default for highways and ramps) or "road follows the ground" (the default for paths). **Road types** shows the generated Road Builder roads and any width change.

**Assets** lists the CS1 buildings with the CS2 building chosen for each type, the match score and why; pick another one or skip a type. For each type you can also say what happens if it doesn't fit at its spot. By default it is not moved: it slides a little or takes a smaller alternative, and if it still doesn't fit, the import report lists it with its CS1 position so you can place it yourself ("Don't move it (list it for me to place)"). You can instead let a type look for free land nearby (up to 400 m) or anywhere in the square. When the square has power, water or sewage buildings, **Overview** shows a notice with one button, **Let them move nearby (≤ 400 m) if they don't fit**, which does that for all of them at once: CS2's are often much bigger than CS1's, and without them the city has no power or water.

**Report** lists everything that is approximated or dropped.

## 6. Download the package

Press **Download package (.zip)**. Building it takes a few seconds: the terrain is two 4096 × 4096 heightmaps.

There is no need to unzip it: the importer takes the zip and unpacks it itself. It holds `cs1conv-plan.json`, `report.json`, the `roadbuilder` and `terrain` folders and a `README.txt`.

## 7. Install the mods and the road types in CS2

Enable **Road Builder** and the **CS1 → CS2 Importer**. **Traffic** is optional: it sets the priority signs, lane connections, lane arrows and U-turn rules. The line at the bottom of the importer's page says whether Road Builder and Traffic are running.

Start CS2 and open Options → **CS1 → CS2 importer** (tab **Import**). Press **Browse…** and choose the downloaded zip: the importer unpacks it under `%LOCALAPPDATA%\cs1-to-cs2\packages\`, shows "Unpacked <zip> to <folder>." and checks it. **Open packages folder** shows that folder in Explorer.

Road Builder reads its road types only when the game starts, so install them now: press **Install Road Builder configs**, confirm, wait for "Copied N Road Builder configs", then **restart the game**. (Or, with CS2 closed, copy the files from the package's `roadbuilder` folder into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`; the configs of other converted cities can stay there.)

## 8. Start a new city

Start a new game (not the editor). Any map works, since the importer replaces its terrain; Sunshine Peninsula has the fewest pre-built roads to remove.

## 9. Check the package

Open Options → **CS1 → CS2 importer** again. In the **Package** group, pick the package in **Packages on this PC** (the ones you used last come first), or use **Browse…**, or paste its folder, its `cs1conv-plan.json` or its zip into **Package folder** and press **Check**.

The status line says what the package holds, for example "Package: Skibidi.02, 2035 roads to build. All 37 road types are available." and "Map: terrain with its surroundings, sea level 182 m, 5 water sources, 20 outside connections.". Its last lines say whether this city's terrain is already the package's, and whether it drives on the same side of the road as the CS1 city (in a new city, **Prepare map** sets the side). If road types are missing, install the Road Builder configs and restart the game.

## 10. Prepare the map

In the **New city** group, press **Prepare map** and confirm. It removes the map's own roads, tracks and buildings, replaces its terrain and surroundings with your city's, sets the CS1 sea level and water sources (a package without water data gives a dry map), and makes traffic drive on the same side as in the CS1 city. **Status** follows the steps ("Preparing the map… applying the terrain", "… setting up the water") and ends with "Map prepared.", the terrain check ("within 0 m of the package at 95 % of 181 check points"), the side ("Traffic drives on the left, as in the CS1 city.") and "Next: press "Import into this city".". It takes 10 to 20 seconds.

It can't be undone: use it in a new city. Without a loaded city (main menu, editor) it stops with "Map not prepared: Start a new city first.". A package exported without the terrain keeps the map's own terrain ("Terrain skipped: the package has no terrain").

## 11. Import into the city

In the **Import into this city** group, choose the steps: **Clear the area first**, **Zoning**, **Traffic lights, stops, turn bans, crosswalks**, **Priority signs (Traffic mod)**, **Lane connections (Traffic mod)**, **Buildings, trees, districts**, **Transit lines** and, last, **Outside connections** are on by default; the roads are always built first. **Keep this map's terrain** is off: the import then refuses a city whose terrain isn't the package's; turn it on to import onto the map's own ground anyway (only cuts of 4 m or more keep their depth). **Import into a city driving on the other side** is off too: the import then refuses a city whose traffic drives on the other side of the road than the CS1 city (**Prepare map** sets the right side in a new city); turn it on to import anyway, and roads, one-way streets and lane connections come out mirrored. **Add a transformer station next to each imported power plant** is off by default: the import report then names each coal, gas or incineration plant that needs a transformer (CS2 plants only feed the high-voltage grid), and the city keeps its power through the outside connection.

Press **Import into this city** and confirm. **Status** shows the progress ("Importing… batch 3 of 6", then each step) and ends with a summary per step, for example "Done in 4.33 s: 2035 of 2035 roads built.", "Transit: 9 of 9 lines, 145 of 145 stops." and "Outside connections: 20 of 20 (13 road, 7 train).". A step you turned off reads "skipped: turned off in the Options". The summary also gives the path of the full report, under `%LOCALAPPDATA%\cs1-to-cs2\import-reports\`.

A 3 km square takes under a minute: the roads about 4 seconds, the transit lines the longest. The game stays responsive, and **Cancel** (group **Status**) stops after the current step; what was built so far stays.

## 12. After the import

Unpause for a few minutes: CS2 fills the rivers and lakes from the water sources. Then save the city. Homes get electricity, water and sewage from the start: the import adds electricity and water outside connections at the edge of the imported roads, on top of your converted power plants and pumps. Zoned buildings then grow on the converted zones.

To undo, press **Remove imported** and confirm: it deletes the roads, buildings, trees and props, districts and areas, transit lines and outside connections the imports created, even after saving and loading. Zoning and traffic settings go with the roads; the base map removed by **Prepare map** doesn't come back.

Read the web tool's **Report** tab (or `report.json`) and the import summary for what needs a hand:

Lane connections, lane arrows and U-turn rules apply at junctions; lane splits where a road continues into another without a junction stay as CS2 builds them (about 8 % of connections in our tests). In a city that drives on the left, **Prepare map** sets the driving side from the CS1 city; roads, one-ways and lane connections follow it. About 7 % of the zone cells find no CS2 zone block, because CS2 blocks are shallower. Coal, gas and incineration plants feed CS2's high-voltage grid, which reaches the city only through a transformer station: turn on **Add a transformer station next to each imported power plant**, or place one yourself (the import report names each plant). Wind turbines and other low-voltage producers are linked to the nearest road automatically. A plant that stands away from every road produces nothing until you give it one (the report names it). Metro and train lines need their stations from the buildings step; a metro or train line whose track stays inside the square gets no yard: place one yourself (the import report names the line). Where two CS1 roads leave a junction at a very shallow angle, CS2 merges their first metres into one road and splits them a little further on, connected as in CS1. A few overlapping CS1 bits that CS2 can't fit (for example a road a few metres from a parallel road) are left out and listed in the report.

## Privacy

cs1conv runs entirely in your browser. Your .cs1x file is read on your computer and never uploaded; there is no server, no account, no analytics and no cookies.

The page remembers your crop, mapping choices, options and language in your browser's local storage so they are still there next time. Clearing the site data removes them.

See also: [FAQ and troubleshooting](faq.md) · [What converts](what-converts.md) · [Français](fr/user-guide.md)
