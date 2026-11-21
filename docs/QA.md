# QA and robustness (2026-11-21)

**Status: done.** First pass hit a usage limit; the second pass (same day) did the exporter fix, the e2e run and sent the remaining findings.

## 1. CI

- **First run:** 44872754927 failed in "Web tool – tests" (2 of 63). Windows runners check text files out with CRLF (`core.autocrlf=true`). `web/test/docs.test.ts` compares `docs/guide/*.md` and `web/public/THIRD-PARTY-NOTICES.txt` byte for byte with LF generator output.
- **Fixed in `.github/workflows/ci.yml`:** `git config --global core.autocrlf false` before checkout. Verified: a fresh `core.autocrlf=false` clone of HEAD passes `docs.test.ts` (4/4). Not pushed.
- **Sent to worker-2:** the same failure hits any local Windows clone with autocrlf=true, this machine's setting included. Suggested fix: normalise `\r\n` in the test, or add `.gitattributes` with `eol=lf` for the generated files.

## 2. Other cities

The saves were read-only, and nothing was saved. Each launch did a single `save.load`, and `autosave_blocked` was true throughout. The exports are in `%LOCALAPPDATA%\cs1-to-cs2\qa\` (`*.cs1x`, `*.export.txt`, `*.inspect.txt`, `*.plan.txt`, `*-pkg/`).

| City (save) | Size | Nodes / segments | Mods' data | Start + load | Export | inspect | plan (auto-crop 3000) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Rennmont ("NouvelleSauvegarde 2") | 6.4 MB | 5104 / 5582, 187 NetInfos | TM:PE, NC (46), 0 IMT, 166k trees, 11 lines, workshop roads | 51 s + 87 s | 5.3 s | 1.4 s | 3.9 s |
| CacaoCity ("…more lanes more traffi") | 5.6 MB | 2317 / 2458, 130 NetInfos | 186 IMT fills, TM:PE with 25 default speed limits, NC (306), 193k trees | 69 s | 3.8 s | 1.0 s | 2.7 s |
| Westfield ("at grade interchange 19", Steam Cloud save) | 1.0 MB | 4302 / 4400, 84 NetInfos | 64 IMT, TM:PE (517 lane connections, 22 parking restrictions), NC (309), no trees or props | 78 s | 2.1 s | 0.7 s | 2.0 s |

- **No crashes and no failed sections:** all 14 exporter sections are `ok` in all three cities, and every web plan section was built.
- **Unmapped ground NetInfos:** Rennmont 3, Cacao 1 (`Suburban Fence`, a DecorationWallAI, correctly skipped), Westfield 0. None of them is inside the crop.
- **None of the three has districts,** so district export is still only verified with the synthetic test (buildings decision #19).
- **Not done:** a fresh vanilla city. The CS1 bridge has no `game.new`, and starting one through the menus would leave a new city that the game may autosave.

### Findings

| # | Where | Finding | Owner / status |
| --- | --- | --- | --- |
| Q1 | CI / web tests | CRLF checkouts fail `docs.test.ts` (above) | CI fixed (mine, 9b4ac11); worker-2 normalised line endings in the test (working tree) |
| Q2 | cs1-exporter `ZonesExporter.cs:94-128` | The in-game "geometry check" reports a **mean** distance of 3343 m on Rennmont: a few blocks with a stale or reused `m_segment` dominate it. The exported data is right: an offline check over all 6043 blocks against `network.json` gives median 12 m, p99 20.6 m, 9 blocks > 100 m. Fix: report the median and the count over 100 m, and skip segments that aren't `Created`. | mine, **fixed**: the check now reports the median, the count over 100 m and skipped stale segments. Re-export of Rennmont: "col 0: 12.0 m … 756 blocks sampled, 1 with col 0 over 100 m, 0 skipped": one outlier block made the old mean |
| Q3 | cs1 bridge `saves.list` / `save.load` | Saves are found by asset name or city name, not by file name. `DriniCity.crp` isn't loadable as "DriniCity"; its file date matches "NouvelleSauvegarde" (Rennmont). Duplicate names (local and cloud "Wod 2") resolve to the newest. Worth documenting in the gamectl README. | note |
| Q4 | web plan summary | "Transit lines: not-converted, no line has two stops in the crop" is shown for cities with **0** transit lines (Cacao, Westfield); "absent" would be right. | worker-2, **fixed**: 0 lines → "absent / the city has no transport lines" (and the same for a city with no zoned cells) |
| Q5 | web mapping | The same workshop road family maps to very different widths: `Four-Lane One-Way Road (No Prkg)` (16 m) → RB 16 m, but `Four-Lane One-Way Road` (CS1 32 m) → RB 26 m with 9–11 m sidewalks. Probably faithful to the asset, but worth a look. | **faithful** (worker-2): that asset has m_halfWidth 16 and 9 m pavements; RB caps sidewalks at 7 m |
| Q6 | exporter terrain report | On Westfield, one ground node is 16.7 m off the bilinear terrain (p99 1.0 m). Probably a node on a raised Move It / terrain edit; not investigated. | mine, open |
| Q7 | web plan | Rennmont's 33 `Pedestrian Gravel` segments in the crop are skipped as Untouchable building sub-networks (park paths). This is correct; recorded so nobody chases it. | OK |
| Q8 | web traffic | TM:PE default speed limits (Cacao: Basic Road 0.6 = 30 km/h) are applied to roads without explicit limits (`m2/traffic.ts:57`). Verified. | OK |

## 3. Full pipeline on a new city: CacaoCity

`node tools/e2e/run.mjs --apply-terrain --cs1x %LOCALAPPDATA%/cs1-to-cs2/qa/cacao.cs1x --auto-crop 3000 --simulate 60` → run `.gamectl/e2e/20261121-083647`, **ok=true**, saved as `cs1conv-qa-cacao`. Waited ~18 min for the terrain worker's cs2.lock first.

| Step | Result | Time |
| --- | --- | --- |
| plan | 684 segments (554 RB + 130 pathways), 42 RB configs | 2.6 s |
| cs2 start + new city (Sunshine Peninsula) | ok | 54 s |
| clear, terrain, water | 197 base edges cleared; CS1 terrain applied; 6 water sources, sea 44.1 m | 14 s |
| import | **684/684 built**, 0 failed, 0 Temp | 1.9 s |
| traffic / Traffic mod / zoning | 160/160 lights as planned; 34 priority signs; 16 845 zoned cells planned | 3 s |
| **buildings** (new plug-in `tools/e2e/steps/buildings.mjs`) | buildings 33/43 placed (4 alternatives, 5 slid, 7 on nearby land), 26 connected, 10 collisions; trees 10 393 of 12 260 (1666 on a CS2 road, 201 in a lot); 44/44 IMT surfaces; no districts in the city | 5.4 s |
| connections | 4 road connections created, 62 lanes; 99 cars after 60 s | 1 s |
| water-check | dry 150/150 ok, but **wet only 42/150**, wet depth error median 7.6 m (Skibidi: 130/150, 0.68 m) | – |

- **The bridge fix for "EntityCommandBuffer not allowed" (worker-3) worked:** 0 occurrences in the whole run, so the retry in `buildings/run.mjs` and the plug-in never fired. Retries stay as a safety net.
- **The buildings steps** ran through the e2e plug-in (exec of `Cs2Buildings.dll`, same code the importer mod now compiles in). Not yet run through the mod's own Import button.
- **Q9 (terrain/water, sent to worker-4):** wet points mostly dry in CS2 on Cacao, which has 7 of 13 CS1 water sources as facilities and a sea level of 44 m; the screenshots show lakes but smaller. There's also a tall vertical terrain wall at the east edge of the oblique shot (`roads-oblique.png`), worth checking whether it's the CS1 terrain or the crop/border fill.
- **Not converted, as designed:** TM:PE lane connections (265), lane arrows (234), U-turn rules (40), timed lights (22).

## 4. Full city: Skibidi, 14 336 m crop (terrain/transit worker)

`node tools/e2e/run.mjs --cs1x %LOCALAPPDATA%/cs1-to-cs2/samples/skibidi.cs1x --auto-crop 14336 --apply-terrain --clear-with mapprep --simulate 180` → run `.gamectl/e2e/20261121-091737`, saved as `cs1conv-e2e-20261121-1025` (city `cs1conv-full-skibidi`).
- **Crop:** centre (1250, 1250), the whole CS2 playable area.
- **Map:** Sunshine Peninsula.
- **Overall:** `ok=false` only because of the importer's 8 known `edge-missing` segments (below).
- **How it was measured:** `terrain/tools/perf-monitor.mjs` sampled every 2 s:
  - CS2 and node working sets;
  - the bridge `/health` frame counter, giving fps;
  - the bridge's status age, i.e. how long the main thread stalled.

  Data: `.gamectl/full/perf.ndjson`, `perf-summary.json`.

| Step | Result | Time | CS2 peak WS | Main-thread stall (max) | fps min / mean |
| --- | --- | --- | --- | --- | --- |
| plan (web CLI) | 4741 segments (4148 RB, 553 track, 40 path), 4121 nodes, 78 RB configs, 253 buildings, 20 transit lines; package 72 MB (plan JSON 7.2 MB) | 3.4 s | – (node 428 MB) | – | – |
| cs2 start + new city | ok | 118 s | 4.0 GB | 10.9 s (loading) | – |
| clear (mapprep) | 197 base edges + 5 base buildings deleted, no crash | 1.9 s | 3.7 GB | 0.4 s | 28 / 42 |
| terrain | applied; terrain = file to 0.0003 m (max over 281 samples) | 14 s | 3.9 GB | 0.7 s | 43 / 54 |
| water | sea 182 m, 1 CS1 + 4 region sources (this run predates the spread sources; see below) | 0.5 s | – | – | – |
| import | **4732/4740**, 8 `edge-missing`, 30 `not-tunnel`, 1 `not-elevated`; 0 Temp; 12 batches | 3.9 s | 4.1 GB | 0.9 s | 6.8 / 45 |
| traffic + Traffic mod + zoning | 948/948 lights as planned, 623 priority signs, zoning ok | 9 s | 4.2 GB | 1.1 s | 6.7 / 13 |
| **buildings** | 252/253 placed (38 alternatives, 63 slid, 95 on nearby land), 196 connected; 39 439 objects planned | 36.6 s | 4.4 GB | **26.8 s** | **0** / 6 |
| connections | 10 (6 road, 4 train), 96 outside-connection lanes | 1.6 s | 4.4 GB | 0.9 s | 7 / 16 |
| **transit** | 305/305 stops; **20/20 lines** (16 bus, Train Line 1, Metro Lines 1–3 on placed stations), all complete | 49.6 s | 4.4 GB | 1.3 s | 6.9 / 27 |
| simulate 180 s | 75 buses running (no subway or rail yard, so 0 trains) | 180 s | 4.8 GB | 0.4 s | 25 / 39 |
| water-check | dry 150/150, wet **100/150**, median 2.9 m, p90 53 m | 0.4 s | – | – | – |
| save | 34.8 MB `.cok` | 2.8 s | **5.0 GB** (private 11.6 GB) | 1.8 s | 7 / 7.5 |

**Findings:**
- **F1, importer (worker-3, known):** 8 `edge-missing`, e.g. segments 33055, 2160, 24501, 25217, and 13 `other_cs2_edges_in_area`.
  - **Cause:** CS1 grade separations shallower than CS2 accepts, e.g. a highway in a 4.6–5.2 m cut beside or under other roads, and a footpath at −3 m. CS2 joins the crossing courses.
  - The 30 `not-tunnel` are cosmetic: CS2 builds an open cut instead of a tunnel cover.
  - **Fix idea:** detect crossings with less than about 8 m clearance and deepen the lower road or report it. Open, in cs2-importer/DECISIONS-importer.md.
- **F2, buildings (buildings worker):** the placer's resolve loop ran in one frame, freezing CS2 for about 27 s.
  - **Fixed (uncommitted):** the loop yields every `frameBudgetMs` (20 ms default) in `buildings/src/Placement.cs`.
  - **Verified at full scale** on `cs1conv-e2e-20261121-1025` with all 253 items. With `idempotent: false`, every item collided with its existing twin and ran the full slide and nearby/wide/anywhere search, a harsher case than a real import.
  - **Result:** max main-thread stall **0.94 s (was 26.8 s)**, min 14.4 fps, 246/253 placed, 2803 yielded frames, 92 s. Files: `.gamectl/buildings/stall-20261121T0950*/`.
- **F3, water (terrain worker), fixed:** one 3.5 km CS1 river stayed dry (wet 100/150, p90 53 m); a single source at a region's deepest cell can't feed a long channel.
  - **Fix:** `terrain/lib/water.mjs` `regionSources` spreads more sources over big regions (farthest-point sampling, one per 1500 cells, up to 8, radius 40 m). Big lakes that already have a CS1 source also get a "lake-boost" source (decision 17).
  - **Re-check:** `terrain/run-mapprep.mjs` on the same package, 300 s → `.gamectl/mapprep/full1`: **wet 146/150, dry 150/150, median 1.07 m, p90 6.8 m**, 15 sources.
  - **CacaoCity:** the same fix gives wet 143/150 (was 42 before any simulation, 105 with lake-boost only), median 1.8 m, p90 6.1 m.
  - Worker-2 is re-porting `regionSources` for the web package's `plan.water.apply`.
- **F4, the 253/684 import in my CacaoCity runner (terrain worker), fixed; not an importer trap:**
  - **Cause:** a later `cs1conv.ts plan --install-rb` without `--keep-other-rb` had removed CacaoCity's Road Builder types from ModsData, so 431 segments were `prefab-missing`. The importer reported each one as an error.
  - **Fix:** `terrain/run-mapprep.mjs` now installs the package's types with `--keep-other-rb` before starting CS2.
  - **Re-run:** 684/684 (`.gamectl/mapprep/cacao2`).
  - Loading saves is unaffected: Road Builder restores road types from the save, after a one-time "reload this save" dialog.
- **F5, metro and train vehicles (buildings worker), designed but not yet in a run:**
  - **Why:** CS1 metro and train lines need no depot; CS2 lines get vehicles only from a depot of their type. So converted station lines had 0 vehicles.
  - **Fix:** `buildings/lib/plan.mjs` `addDefaultDepots` (uncommitted, tests 24/24) adds BusDepot01 / TramDepot01 / SubwayYard01 / RailYard01 for every line type that has no matched depot, marked `default: true`.
  - **Rail yards:** they sit at the line's first station with `needsTrackLink: true`, because CS2 yards dispatch only over connected track and placing a yard's own track onto a track end isn't automated.
  - **Expected result:** buses get vehicles automatically; metro and train lines get vehicles once the user links the yard's track.
- **Scale limits seen:** none in transit (305 stops in batches of 20, 20 lines, one per call), outside connections (10), water sources (15) or the save (35 MB).
  - **Memory:** CS2 grew from 3.5 to 5.0 GB working set over the run.
  - **Main thread:** apart from F2, no step stalled it for more than 1.8 s, and the import ran at a minimum of 6.8 fps.

## 5. Buildings quality pass (buildings worker)

All on worker-3's road-only save `cs1conv-e2e-embank-fix` (Skibidi 3 km), not saved; report `.gamectl/buildings/objects-20261121-090240/`.

| Change | Before | After |
| --- | --- | --- |
| **Roadside trees** (`roadsideMove`, 6 m): a tree on a wider CS2 road is pushed perpendicular to just past the road edge if that spot is free | 2744 trees, 1359 dropped on roads | **3404 trees: 705 moved (mean 3.1 m), 654 dropped**, 158 in a lot |
| **Huge lots:** `fallback` `nearby` (150 m, then a 400 m `wideRadius` pass) or `anywhere` (any imported road in the crop, nearest first) | 96/108 placed, 12 big services failed | **108/108 placed**, 105 connected; passes: nearby 20, wide 3, anywhere 9 |
| **Distance reported** for every placed building (`distance`, `fallbackPass`; summary `distanceStats`) | only for nearby moves | median 9.6 m, p90 151 m, max 1222 m; 9 over 400 m (University, Prison, Cargo Center, five Combustion Plants) |
| **Districts in the web tool's shape** (`outlines`, no prefab; `cityPolicies`) | the mod could only read cs1conv-objects.json districts | 2 outlines → 2 districts, named, Heavy Traffic Ban + Recycling; a city-wide "Fire Safety Sprinkler System" reported as set, wrongly (no such CS2 policy; corrected, see A6) |
| **Default depots** (`addDefaultDepots`, also called by the web tool) | station lines had 0 vehicles (F5) | Skibidi 3 km: SubwayYard01 added for Metro Line 3, placed in the acceptance run; rail yards flagged `needsTrackLink` |
| **Frame budget** in the placer (F2) | 26.8 s main-thread stall | 0.94 s |

- **Web tool (worker-2):** the per-type fallback is in the Assets tab ("anywhere" by default for CS2 lots over 400 cells: University, incinerator, prison, coal/gas/solar plants, cargo terminal), and `plan.objects` / `plan.surfaces` are now in the package, so the mod places trees and IMT surfaces without cs1conv-objects.json.
- Decisions: `buildings/DECISIONS-buildings.md` #27–32.

## 6. Release acceptance

**Release 0.3.0 accepted** (exporter 1.2.0, importer 0.3.0, web 0.5.0, buildings 0.3.0): the user's flow with the release artefacts, `node tools/release/acceptance.mjs` → `.gamectl/acceptance/20261121-100459`, **ok=true**, saved as `cs1conv-acceptance-20261121-100459`.

How: the importer mod was installed **from `release/0.3.0/cs2-importer-0.3.0.zip`** (zip: `Cs2Importer/Cs2ImporterMod.dll`, `Properties/PublishConfiguration.xml` + `Thumbnail.png`, `CHANGELOG.md`; the dev build in Mods was backed up and restored afterwards). The package came from the web CLI. In CS2 the mod's Options buttons were pressed through the exec library's `op mod`, which calls the same `Actions` methods as the buttons; no import code ran by exec.

| # | User step | Time | Result (the mod's own status text) |
| --- | --- | --- | --- |
| 1 | Build the release (`build.mjs`) | 9 s | 3 zips, versions agree |
| 2 | Make the package (web CLI, Skibidi, auto-crop 3 km) | 2.5 s | 2035 roads, 33 RB types, 109 buildings (incl. 1 default SubwayYard), 4216 trees/rocks, 9 transit lines |
| 3 | Install the mod from the zip, start CS2 | 56 s | – |
| 4 | Options → **Install Road Builder configs** | 0.6 s | "Copied 33 Road Builder configs. Restart Cities: Skylines II so Road Builder loads them, then import." |
| 5 | Restart CS2, new city (Corral Riches) | 78 s | – |
| 6 | Options → package, **Check** | 0.8 s | "All 37 road types are available. 9 roads already in the area: keep "Clear the area first" on. Map: terrain …, sea level 182 m, 13 water sources, 20 outside connections. In a new city, press "Prepare map" before importing." |
| 7 | **Prepare map** | 15.6 s | "Removed 378 roads and tracks and 18 buildings of the base map. Terrain applied … within 0 m of the package at 95 % of 181 check points. Water: sea level 182 m, 13 CS1 water sources." |
| 8 | Unpause 20 s | 20 s | – |
| 9 | **Import into this city** | 49 s (roads 4.3 s) | "2035 of 2035 roads built. Problems: 1 not-elevated. Traffic: 1 lights added, 121 removed, 66 turn bans, 41 crosswalk changes. Traffic mod: 325 priority signs. Zoning: 71356 of 76658 cells (93.08 %). **Buildings: 109 of 109. Objects: 3380 of 4216.** districts/surfaces skipped (none in this crop). **Transit: 9 of 9 lines, 145 of 145 stops. Outside connections: 20 of 20.**" |
| 10 | Simulate 60 s, screenshots, save | 72 s | Skibidi on its own terrain, dry land, roads, rail, services; 116 citizens after 60 s |

Problems found on the way, all fixed or explained:
- **A1 (user flow):** without **Prepare map** the base map stays: on Corral Riches its lake flooded the whole imported city (run `20261121-095410`, screenshot `acceptance-oblique.png`). Check tells the user to press it, and the user guide's step 7 has it. The acceptance script presses it.
- **A2 (release build):** the importer was still versioned 0.2.0 with the M3 steps in it. Worker-3 bumped it to 0.3.0 with a changelog and Paradox Mods packaging; `build.mjs` checks the new version twins and ships `Properties/` (without the thumbnail script).
- **A3 (wording, mine):** the skipped steps said "no districts section in the plan or in cs1conv-objects.json"; now "none in this package" (`buildings/src/Steps.cs`; in the next mod build, not in this run).
- **A4 (process):** two release builds failed while the web tree was mid-edit (`npm run build` typecheck). Not a release defect; the build gate did its job.
- **Not in this release, by decision:** IMT junction fills on roads, TM:PE lane connections/arrows (Traffic mod limits), rail yard track links (reported per yard), CS1 park/industry areas.

### Two more cities with the final 0.3.0 artefacts

`node tools/release/acceptance.mjs --cs1x %LOCALAPPDATA%/cs1-to-cs2/qa/<city>.cs1x` after 9211fe5 (Cs2ImporterMod 0.3.0 + `build.mjs`): fresh release build, the importer installed from `cs2-importer-0.3.0.zip` before each city, then the same user flow (Install Road Builder configs → restart → new city on Corral Riches → Check → Prepare map → 20 s → Import). Both **ok=true**; the dev mod in Mods was restored after each.

| | CacaoCity (`20261121-103442`) | Westfield (`20261121-104220`) |
| --- | --- | --- |
| Package (web CLI, 3 km) | 684 roads, 42 RB types, 43 buildings, 12 260 trees, 44 IMT surfaces | 648 roads, 15 RB types, 13 buildings, no trees |
| Check | "All 44 road types are available." Warns that the map's terrain differs by 421 m: Prepare map first | "All 16 road types are available." (451 m terrain warning) |
| Prepare map | 15.5 s: 378 base roads/tracks + 18 buildings removed, terrain 95 % exact, sea 44.1 m, 29 sources | 15.4 s: same clearing, sea 40 m, 6 sources |
| Import | 16.9 s; **684/684 roads** (2.75 s) | 11.7 s; **648/648 roads** (1.94 s) |
| Traffic / Traffic mod | 4 lights added, 7 removed; 34 priority signs | 43 turn bans; 3 priority signs |
| Lane connections (Traffic mod) | **0 of 0 junctions** (265 planned) → finding A5 | 36 of 37 junctions, 139 connections |
| Zoning | 91.0 % | 99.99 % |
| Buildings | **43 of 43** | **13 of 13** |
| Trees and props | 10 813 of 12 260 (more than the e2e's 10 393: roadside trees are now moved aside) | none in the package |
| IMT surfaces | **44 of 44** (now from the web plan, no cs1conv-objects.json) | none |
| Districts | none | 0 of 0; the city-wide policy was not set → A6 |
| Transit | none in either city (0 CS1 lines) | none |
| Outside connections | 4 of 4 (road) | 22 of 22 (road) |
| "Remove imported" can undo | 12 253 items | 1 392 items |

- **Metro and train vehicles: pending.** Neither city has transit lines, and Skibidi's station lines wait for worker-4's crop-edge yard + track-link step (DECISIONS.md #83), which hadn't landed at this run. The prerequisite is done: placed buildings now get their sub-nets (station and yard tracks; buildings decision #34, verified with 0 missing).
- **A5 (importer, worker-3), fixed in their next build:** CacaoCity is a **left-hand-traffic** CS1 city (477 of 684 segments inverted), so every lane flows the other way and all 265 lane connections were rejected ("source lane leaves the node" 373, "lane count differs" 212). Worker-3's lanes step now tries mirrored ordinals and says so in the status line. The exporter (1.2.1, mine) now records it: `manifest.traffic.leftHand` (verified: CacaoCity → `true`); worker-2 passes it into the plan.
- **A6 (policy names, worker-2 + mine):** Westfield's city policy "Small Business Enthusiast" (from CS1 SmallBusiness) has no CS2 policy prefab of that name, so nothing was set and the status said only "0 of 0". The districts step now reports such names as `policiesMissing`; the CS2 policy names were listed from the game for worker-2's map: 18 PolicyTogglePrefab + 5 PolicySliderPrefab. Neither "Small Business Enthusiast" nor "Fire Safety Sprinkler System" is one. The latter had looked set in §5 because a name lookup returned a non-policy prefab; the districts step now accepts only real policy prefabs.

## 5. Trains and metros (terrain/transit worker): depots and track links, not running yet

Goal: converted metro and train lines get vehicles. The user's decision (docs/DECISIONS.md #83):
- a rail or subway yard goes only at the crop edge, where the line's own track leaves the crop;
- otherwise nothing is placed, and the report says to place one yourself.

Test runs:
- **6 km Skibidi crop:** `--crop 1870,-165,6000` → e2e `.gamectl/e2e/20261121-111610`, ok=true, save `cs1conv-e2e-20261121-1221`. It contains Train Line 1 and Metro Lines 1–3.
- **Full city:** `cs1conv-e2e-20261121-1137`.
- **Runner:** `node transit/run-tracklink.mjs --load <save> --plan <package plan> [--recreate]` → `.gamectl/tracklink/6k-run1…6`, `full-run1/2`.

What works (verified in game):

| Step | Result |
| --- | --- |
| `transit.mjs depots` → `yards.plan` (open track ends of each rail type within 80 m of the crop bounds) | 6 km: Train → 5 edge track ends, one `RailYard02` item. Metro → no metro track leaves the crop, so the report has "Metro Line N: its track does not leave the converted area, so no depot was placed: place a subway yard yourself…" for Metro Lines 1–3. |
| Buildings placer, with `requireRoad` / `maxDistance` (buildings worker) | 6 km: RailYard02 placed and road-connected, 265 m from the track end. Full city: no non-highway spot within 2.5 km (crop edge = map edge), so the line gets the "no free land with road access … place a rail yard yourself" report entry. |
| Sub-nets of placed buildings (buildings worker, new) | yards and stations now get their own tracks: RailYard02 81 sub-net entities, SubwayStation01/03 13, TrainStation02/03; `subNetsMissing` 0 |
| `tracklink`: yard → line track | yard linked; a clean Permanent course when possible, otherwise a Temp course plus one forced apply frame (CS2 builds the level crossings over highways). Yard `connected: true`. |
| `tracklink`: station platforms → imported track | CS1 platforms are building sub-nets (not imported), so CS2 station platform tracks ended loose. Each open end is now joined to the nearest imported dead end within 200 m: 6 km, 2 train stations + 5 subway stations, every end linked. |
| Inside-only metro | the report message as designed |

**Not working: trains and metros still don't run** (Train Line 1 and Metro Lines 1–3: 0 vehicles after 6 minutes).
- **Symptom:** every rail-line route segment has **0 path elements, and its path state is 0 (never requested), not Failed**. The bus lines on the same city, created by the same op, get paths (7 elements per segment) and 80–95 buses.
- **Ruled out:**
  1. **Stop lanes:** the segments' `PathTargets` point at real `Oneway Train Lane 4` lanes of the stations' platform `Double Train Track` edges, and those edges are joined (end nodes with 2 edges).
  2. **Updated segments:** marking the route segments `Updated` changes nothing.
  3. **Applied lanes:** tagging train/subway lanes `Applied` (RoutePathSystem re-checks paths when lanes come with Applied) changes nothing.
  4. **Recreating the lines** after the stations are joined (`--recreate`) changes nothing.
- **Next hypotheses:**
  - **Station sub-nets' lanes:** the station platform edges come from the placer's OwnerDefinition sub-net courses; maybe their lanes lack something the pathfinder needs (e.g. a pathfind graph entry for owned edges).
  - **Station stops:** the integrated station stops have no `Attached` (bus stops are attached to their road edge); maybe they need their access lanes set.
  - **Decisive next test:** in a plain new city, build a station by hand-equivalent means (the vanilla object tool through the same forced-apply trick that works for lines and tracks), then a line on it. If that line pathfinds, the difference is in how the placer creates station sub-nets.
