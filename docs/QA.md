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

### 5b. Root-cause hunt (time-boxed): started, cut short by the usage limit

- **Leads from the IL** (no game run yet):
  - Route segments are read by `Game.Routes.RoutePathSystem` and `Game.Pathfind.RoutesModifiedSystem` (`AddPathEdgeJob` / `UpdatePathEdgeJob`), among others.
  - There is no route-specific `*PathfindSetup` system, so a segment path is most likely requested inside `RoutePathSystem`'s jobs (`CheckRoutePathsJob`, `CheckSegmentRoutes`).
- **Next step:**
  1. Read `RoutePathSystem.OnUpdate` and `CheckSegmentRoutes` to see which condition skips the rail segments: state 0, 0 elements, while bus segments are computed.
  2. Then run the planned vanilla-station comparison (tool definitions plus the forced apply frame), diffing against a placer station.

### 5c. Root-cause hunt: findings (time-boxed, about 1.5 h). Rail lines still have no path; causes found

Runs: `.gamectl/tracklink/6k-run7`, diagnostics `node transit/tools/node-diag.mjs` / `line-diag.mjs` (6 km Skibidi, save `cs1conv-train6k-trains`).

1. **CS2 pathfinds a line segment only when its target lanes change** (`RoutePathSystem.SetupPathfind`, IL_0264–02AF).
   - It returns false when `PathTargets` already equal the waypoints' `RouteLane` ends.
   - So a rail line created before its stations are joined keeps an empty path forever. `Updated` segments or `Applied` lanes never re-ask.
   - **Fix (transit, done):** `transit.mjs depots --recreate` deletes the rail lines and creates them again after the track links.
2. **A Permanent course starting on a building-owned node (a station platform end) is silently not built.** Node diagnostics showed every subway "link" absent: both ends kept one edge.
   - **Fix (transit, done):** all station and yard links now go the player's way (Temp definition + one forced apply frame). The TrainStation02 link built this way is present: 2 edges per node, lanes on the link.
3. **The subway links are still rejected:** each must climb 12–15 m in about 20 m.
   - CS1 "Overground Metro Station **Elevated** 01" was matched to `SubwayStation03`, whose platform ends sit at ground level, while the imported metro track is elevated. The tool validation rejects such grades, so apply builds nothing.
   - **Fix → buildings worker:** match elevated CS1 metro stations to `SubwayStationElevated01/02` (they're in the alternatives list), or place the station at the track's elevation.
4. **TrainStation03 straddles the imported track:** both its platform ends (x −2526 / −2314) link to track ends near x −2350, inside the station's span.
   - The station was placed over the CS1 track instead of where CS1's platforms were, so Train Line 1 can't pass through it.
   - **Fix → buildings worker:** for stations, align the lot with the CS1 station's position and orientation (no slide, no "nearby" move), so its platform tracks continue the imported track.
5. **Status:** Train Line 1 and Metro Lines 1–3 still have 0 path elements. Causes 3 and 4 are on the station placement side. With them fixed, transit's `depots --recreate` (link + recreate) is ready to verify trains: `node transit/run-tracklink.mjs --load <save> --plan <plan> --recreate`.

## 7. Release candidate (buildings worker)

> **Correction (2026-11-22, DECISIONS.md #164, #165):** the bus totals in this section count every `Bus01` in the city, parked ones included. No imported bus line got a vehicle: the buses stayed parked in the depot and every line's vehicle request failed. Imported bus lines have never got vehicles in the mod flow; this is a known limit, being fixed.
>
> **Fixed in 2680feb** (DECISIONS.md #166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street, and imported bus lines get buses.

`node tools/release/acceptance.mjs --simulate 300 [--skip-build] [--cs1x %LOCALAPPDATA%/cs1-to-cs2/qa/<city>.cs1x]` after e17a74c: a fresh release build (product 0.3.0; exporter 1.2.1, **importer 0.3.1**, **web 0.5.2**, buildings 0.3.0).
- **Install:** the importer from `cs2-importer-0.3.1.zip` before each city.
- **Flow:** the user's: Install Road Builder configs → restart → new city on Corral Riches → Check → Prepare map → 20 s → Import → 300 s at speed 3.
- **New checks in the script:**
  - **lane directions:** the importer's op `lanedirs` against the export's CS1 lanes ([`tools/release/lanedirs-compare.mjs`](../tools/release/lanedirs-compare.mjs));
  - **vehicles:** public-transport vehicles counted by kind after the simulation (transit exec library, read only).
- **Result:** all three **ok=true**; the dev mod was restored after each; no lock left.

| | Skibidi 3 km (`20261121-133435`) | CacaoCity, left-hand (`20261121-134438`) | Westfield (`20261121-140001`) |
| --- | --- | --- | --- |
| Package | 2035 roads, 33 RB types, 108 buildings, 4216 trees, 9 lines | 684 roads, 42 RB types, 43 buildings, 12 260 trees, 44 IMT surfaces | 648 roads, 15 RB types, 13 buildings |
| Check | all 37 road types available | all 44 available; "The CS1 city drove on the left, this city drives on the right: … Prepare map sets the side" | all 16 available |
| Prepare map | 15.9 s, terrain 95 % exact | 15.6 s; **"Traffic drives on the left, as in the CS1 city."** | 15.5 s |
| Roads | **2035 / 2035** (4.2 s; 1 not-elevated note) | **684 / 684** (2.2 s) | **648 / 648** (2.2 s) |
| **One-way directions** (CS2 lanes vs CS1, per road) | **all 1949 the same**: 353 one-way, 1590 two-way, 6 asymmetric; 0 reversed or mirrored | **all 554 the same**: 162 one-way, 369 two-way, 23 asymmetric; CS2 city left-hand | **all 647 the same**: 403 one-way, 244 two-way |
| **Lane connections** (on by default now) | 362 at 70 of 95 junctions; 685/685 CS1 lane refs; explicit sources **92 %** (162/176) | 342 at 68 of 79 junctions; 727/729 refs; explicit sources **99 %** (206/208); handedness from `plan.source.leftHandTraffic` | 127 at 34 of 39 junctions; 299/301 refs; explicit sources **95.7 %** (88/92) |
| Traffic / Traffic mod | 66 turn bans, 121 lights removed; 325 priority signs | 4 lights added, 7 removed; 57 priority signs | 43 turn bans; 3 priority signs |
| Zoning | 93.1 % | 91.1 % | 99.99 % |
| Buildings | **108 / 108** | **43 / 43** | **13 / 13** |
| Trees and props | 3404 / 4216 | 10 828 / 12 260 | none |
| IMT surfaces | none | **44 / 44** | none |
| Transit lines | **9 / 9** (8 bus, 1 subway), 145 / 145 stops | none in the city | none in the city |
| Outside connections | 20 / 20 (13 road, 7 train) | 4 / 4 | 22 / 22 |
| Vehicles after 300 s | **26 buses** (Bus01), no tram lines in the city; **0 metro, 0 train** | 2 buses (no lines) | 10 buses (no lines) |

- **One-way and asymmetric roads run as in CS1 in all three cities, the left-hand one included.**
  - This is importer 0.3.1's left-hand fix, now checked in the release flow.
  - **Comparer validated:** on worker-3's earlier left-hand Cacao dump (`.gamectl/lht/run3`) it gives the same verdict as importer decision #55.
  - It handles the web tool's `reversed` plan segments: 477 of Cacao's 684 run end → start in the plan.
- **Lane connections:** what isn't matched is explained by the outcomes.
  - "pass-through not expressible": 14 in Skibidi, 2 in Cacao, 4 in Westfield. These are lane splits at nodes without a junction, which CS2 can't represent.
  - Two "target lane enters the node" in Cacao and Westfield.
  - Nothing resolved was wrong.
- **Trains and metros: known limit (DECISIONS.md #99), not a failure here.**
  - Skibidi's subway line was created complete with 0 vehicles, and no metro or train ran after 300 s.
  - The crop-edge yards and track links (transit, opt-in) are not part of the user flow, so this run doesn't exercise them.
  - Worker-4's §5c traced two causes to station placement. Both are fixed in the working tree as buildings 0.3.1 (decision #36: stations stay at the CS1 spot and keep their elevated / ground height). They are **not** in these artefacts and not yet verified in game.
  - Left for transit: CS2 train stations are 200 m long against CS1's 128 m, so the track link has to join the imported track at the platform ends.
- **The buses in Cacao and Westfield** (cities without lines) are not from imported lines (routes 0); they're counted as found.
- **Not run:** the Skibidi crop with a train line crossing the edge (`--crop 500,-400,3000`). With trains a known limit and the yards opt-in, it would only repeat "0 trains". It can be run as is when worker-4's track link and the station fix are both in.

**Verification after buildings 0.3.1 (matcher 1.1.0, buildings decision #36).** Runs: `.gamectl/e2e/20261121-141216` (6 km Skibidi rebuilt, save `cs1conv-train6k-st`), `.gamectl/tracklink/6k-run8`, and `line-diag-run8.json`. Result: still no rail line runs.

6. **All 3 train stations and all 5 underground metro entrances fail as `stationCollision`.** At the CS1 spot the CS2 lot overlaps a road (one Large Trainstation overlaps another imported building), with all 3 prefabs tried. So Train Line 1 and Metro Lines 1–2 are skipped (fewer than 2 stations), and the trim of imported track under long platforms (DECISIONS-transit #12) didn't get exercised.
7. **The 3 elevated metro stations are placed (SubwayStationElevated01), but with no sub-nets** (`subNetsExpected 0`, `subNetsBuilt 0`).
   - Metro Line 3 is created on their "Integrated Subway Stop - Middle" stops, but those stops are attached to nothing and have no lanes (`RouteLane 0>0`).
   - So the segments' `PathTargets` stay empty, and the path is never requested: state 0, 0 elements, and not marked failed.
   - The integrated stops need the station's own platform tracks, which aren't built for this prefab.
8. **The imported rail network itself carries trains:** 4 passenger trains (EU_TrainPassengerEngine01 + 12 cars) were running within 240 s, from the rail outside connection.

## 8. Release (RC2, vanilla roads) (cs2-importer worker)

> **Correction (2026-11-22, DECISIONS.md #164, #165):** the bus totals in this section count every `Bus01` in the city, parked ones included. No imported bus line got a vehicle: the buses stayed parked in the depot and every line's vehicle request failed. Imported bus lines have never got vehicles in the mod flow; this is a known limit, being fixed.
>
> **Fixed in 2680feb** (DECISIONS.md #166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street, and imported bus lines get buses.

`node tools/release/acceptance.mjs --skip-build --simulate 300 [--cs1x %LOCALAPPDATA%/cs1-to-cs2/qa/<city>.cs1x]` after `node tools/release/build.mjs`.

**Clean build:** both ran from a clean `git archive` of fa6eece, not the shared working tree, which held other sessions' uncommitted buildings and transit changes.
- **Artefacts:** product 0.3.0; exporter 1.2.1, **importer 0.3.2**, **web 0.6.0** (vanilla CS2 roads where they match, #100/#102), buildings 0.3.0. `versions.json` has `git: null` because the export has no `.git`.
- **Install:** the importer was installed from `cs2-importer-0.3.2.zip` before each city, and the dev mod was restored after each.
- **Flow:** the same as RC1: Install Road Builder configs → restart → new city on Corral Riches → Check → Prepare map → 20 s → Import → 300 s at speed 3.
- **Result:** all three **ok=true**; no lock left.

| | Skibidi 3 km (`20261121-143330`) | CacaoCity, left-hand (`20261121-144323`, `qa/cacao-v2.cs1x`) | Westfield (`20261121-150122`) |
| --- | --- | --- | --- |
| Package | 2035 roads: **341 segments vanilla** (7 road types) + 26 RB types (was 33); 108 buildings, 4216 trees, 9 lines | 684 roads: **117 vanilla** (3 types in the crop) + 32 RB types (was 42); 43 buildings, 12 260 trees, 44 IMT surfaces | 648 roads: **402 vanilla** (1 type) + 10 RB types (was 15); 13 buildings |
| Check | all 37 road types available | all 42 available; "The CS1 city drove on the left, this city drives on the right: … Prepare map sets the side" | all 16 available |
| Prepare map | 15.9 s, terrain 95 % exact | 15.7 s; "Traffic drives on the left, as in the CS1 city." | 15.9 s |
| Roads | **2035 / 2035** (4.25 s; 1 not-elevated note) | **684 / 684** (2.3 s) | **648 / 648** (2.1 s) |
| **Lane directions** (CS2 lanes vs CS1, per road) | **all 1949 the same**: 353 one-way, 1590 two-way, 6 asymmetric | **all 554 the same**: 162 one-way, 369 two-way, 23 asymmetric; CS2 city left-hand | **all 647 the same**: 403 one-way, 244 two-way |
| **Lane connections** (explicit sources as in CS1) | 360 at 69 of 95 junctions; 685/685 refs; **162/176 (92 %)**: 115 at junctions, 47 continuous, 14 not expressible; U-turns 8/8 | 342 at 68 of 79; 727/729 refs; **206/208 (99 %)**; U-turns 25/25 | 127 at 34 of 39; 299/301 refs; **88/92 (95.7 %)**; U-turns 2/2 |
| Traffic / Traffic mod | 66 turn bans, 123 lights removed; 325 priority signs | 14 lights removed; 57 priority signs | 43 turn bans; 3 priority signs |
| Zoning | 93.1 % | 91.1 % | 99.99 % |
| Buildings | **108 / 108** | **43 / 43** | **13 / 13** |
| Trees and props | 3408 / 4216 | 10 836 / 12 260 | none |
| IMT surfaces | none | **44 / 44** | none |
| Transit lines | **9 / 9**, 145 / 145 stops | none in the city | none in the city |
| Outside connections | 20 / 20 (13 road, 7 train) | 4 / 4 | 22 / 22 |
| Vehicles after 300 s | **34 buses** (Bus01) + 10 prison vans; 9 routes; **0 metro, 0 train** | 1 bus (no lines) | 8 buses (no lines) |
| Times | Prepare 15.9 s, import step 52 s | 15.7 s, 15.8 s | 15.9 s, 12.7 s |

**Against RC1 (§7): no regression.**
- **Roads, lane directions, lane connections (numbers and outcomes), buildings, surfaces, transit, outside connections and zoning:** the same to the digit, or within one (trees +4/+8, lights removed +2/+7, Westfield's priority signs 3 = 3).
- **Fewer Road Builder configs to install:** Skibidi 26 instead of 33, Cacao 32 instead of 42, Westfield 10 instead of 15.
- **Vehicles:** Skibidi had 34 buses after 300 s against RC1's 26. That's simulation variance, with the same 9 routes. Cacao 1 against 2 and Westfield 8 against 10: those cities have no lines, so these buses don't come from the import.

**Known limit, not a failure:** trains and metros (DECISIONS.md #99). Skibidi's subway line is created with 0 vehicles; no metro or train ran.

**Height issue reported by the user on this run's CacaoCity save** (a ramp following terrain bumps near the lake interchange): being fixed by road-conformed terrain and per-type height modes (#110–113, `cs2-importer/NOTES-road-heights.md`). It isn't covered by this acceptance's checks.

## 9. Utilities (web worker, docs/DECISIONS.md #109)

Utility networks aren't converted: CS2 roads carry water, sewage and low-voltage power. Service buildings (power plants, pumps, water towers, sewage outlets) are matched and placed. `node tools/qa/run.mjs utilities` measures what that gives, with the exec op `Cs1ConvQa.Utilities` (`tools/qa/README.md`). It reads CS2's own consumer flags, the infoview statistics, each facility's flow edge, and the game's notification icons. It is read-only.

Run on 2026-11-21 on the release-candidate cities of §7. Each save was loaded and given 3 min at top speed, then measured.

| | Skibidi 3 km (`…-133435`) | CacaoCity (`…-134438`) | Westfield (`…-140001`) |
| --- | --- | --- | --- |
| Buildings: residential / commercial / industrial / service | 328 / 35 / 206 / 105 | 482 / 35 / 192 / 42 | 223 / 0 / 0 / 13 |
| **Electricity** demand fulfilled (infoview) | **0 %** (production 0, consumption 150 010) | **86 %** (92 515 of 107 661) | **0 %** (production 0, consumption 24 634) |
| Buildings with all the electricity they want | 0–1 % | residential 99 %, service 86 % | 3 % |
| **Water** demand fulfilled | **0 %** (capacity 0) | **37 %** (91 134 of 245 277; capacity 101 304) | **0 %** (capacity 0) |
| **Sewage** demand fulfilled | **0 %** (capacity 0) | **74 %** (182 277 of 245 277) | **0 %** (capacity 0) |
| Facilities: power / pumps / outlets / towers | 6 / 0 / 0 / 0 | 7 / 7 / 3 / 0 | 4 / 0 / 0 / 0 |
| Power plants not connected | **6 of 6** (all incinerators: "high voltage not connected", edge disconnected, production 0) | **7 of 7** show "high voltage not connected"; only one small coal plant actually produces (92 697) | **4 of 4** (incinerators, as in Skibidi) |
| Pumps / outlets not connected | – | 4 water pumps: "water pipe not connected" and "no electricity", **production 0**; 1 wastewater plant: "sewage/water pipe not connected" | – |
| Pumps not on water | – | **2** ("not enough surface water": capacity 148 and 18 of 100 000) at CS2 (1156, −60) and (535, −941) | – |
| Electricity / water outside connections | **0 / 0** (22 road) | **0 / 0** (6 road) | **0 / 0** (24 road) |
| Buildings with a utility warning | 666 no electricity, 647 no water, 647 sewage backup | 669 no water, 5 no electricity | 225 no electricity, 229 no water, 229 sewage backup |

**What the numbers say:**
1. **A 3 km crop rarely contains its city's utilities.** Skibidi's and Westfield's power plants, pumps and outlets are outside the crop (or the CS1 city bought from outside). The crops hold only incinerators, so CS2 has nothing to supply them with, and with no utility outside connections the city can't import either. That's 0 % for everything.
2. **High-voltage producers are never connected.** CS2 roads carry only low voltage. A power plant feeds the grid through high-voltage lines to a transformer, and the converter builds neither. All 17 power plants in the three cities show "high voltage not connected". In Cacao the 86 % comes from one small coal plant that does produce (probably through its low-voltage side).
3. **Pumps need power and a pipe link.** Cacao's four water pumps produce nothing: they have no electricity and their pipe isn't connected (they stand at the shore, away from the roads' built-in pipes). The water that does flow comes from the two wastewater plants' purified output. Two pumps also stand where there's not enough surface water.
4. **Sewage works best:** wastewater plants on the road network are connected through the roads. 74 % in Cacao, capped by capacity ("not enough water capacity").

**Proposed fixes (sent to the owners with this evidence):**
- **Map prep (worker-4):** keep or create the base map's **electricity and water outside connections** after "Clear the area" and Prepare map. All three cities have none; whether Corral Riches had them before the clearing is not measured yet. This is the biggest lever: with them, a crop without plants or pumps imports what it needs, as a CS1 city at the edge of its utilities would.
- **Buildings (buildings worker):**
  - for each placed power plant, a short **high-voltage line to a transformer** next to the nearest imported road (or the plant's own transformer upgrade if the prefab has one);
  - for each water pump and sewage outlet away from a road, a short **pipe link** to the nearest road;
  - **pumps must stand on water:** snap them to the nearest CS1 shore cell with surface water, or use a groundwater pump when the CS1 spot is dry (`WaterPumpingStationData.m_Types`).
- **Importer (worker-3):** when the buildings step reports facilities that aren't connected, list them in the import report ("place a power line / pipe yourself"), consistent with #106.
- **Acceptance (buildings worker's file):** `tools/qa/acceptance-utilities.patch` adds a read-only `utilities` step after the vehicle count. It's informational and doesn't fail the run.

Raw results: `.gamectl/qa/utilities-cs1conv-acceptance-20261121-{133435,134438,140001}.json`.

**Verification after buildings decision #37** (save `cs1conv-train6k-st2`, runs `.gamectl/tracklink/6k-run9`, `reach-run9.json` from the new `lines.reach` op). Result: still 0 path elements on all 4 rail lines after 300 s.

9. **Stops now have lanes.**
   - 2 TrainStation03 and 5 SubwayStation01 are placed, each with its own platform tracks.
   - Their "Integrated … Stop"s give every waypoint a `RouteLane` on the station's platform edge, and every segment has `PathTargets`. So the paths were requested, and they found nothing.
10. **Track gaps** (`lines.reach`: search over track edges between each segment's start and end lane, ignoring direction):
    - **Metro, about 450 m and 830 m:** at (−2420, −431), the CS1 metro platforms of the train-station complex weren't reproduced, so the imported metro track dead-ends there on both sides.
    - **Train:**
      - one TrainStation03's platform is isolated, because its links weren't built;
      - the other station's east joint is missing (gap 34 m): its platform overruns the imported dead end, which lies 10.5 m off its axis, outside the 10 m trim limit (now 16 m, DECISIONS-transit #12).
11. **Connected but still no path.**
    - Metro Line 1/2/3 segments between the underground stations are connected end to end through the imported track (`reach: true`), and still get 0 path elements.
    - **So the cause isn't only gaps.** The likely suspect is lane direction: each waypoint gets one platform lane, and nothing makes consecutive stops use lanes whose directions agree.
    - **Not verified yet.** Next test: a directed lane search (`Lane.m_StartNode` / `m_EndNode`), or a station line built with the game's own line tool on the same stations, to compare the waypoint lanes.

**Utility outside connections: cause found** (worker-4, `node terrain/tools/oc-probe.mjs --all`, `.gamectl/mapprep/oc-probe-corral.json`).
- **Fresh Corral Riches:** 11 outside connections, of which **1 electricity**: "Powerline Outside Connection", owned by a "High-voltage Line" node at the map edge (−7181, 4492). There are **0 water-pipe outside connections**: the base map has none.
- **After MapPrep `clear {all: true}`**, which is what the importer mod's Prepare map runs: 2 left (airplane only). The clear deletes the high-voltage line, and its node takes the electricity marker with it.
- **So, for the fix:**
  - Water can't be kept, because the map has none; it has to be created.
  - Electricity can be kept (spare the power line chain that owns the marker) or recreated: a high-voltage line node at the crop edge plus a "Powerline Outside Connection" marker. A high-voltage line still needs a transformer to feed the roads' low-voltage grid.
  - This needs in-game tests. Not done yet.

**Utility outside connections: fixed and verified** (web worker, #136, 2026-11-21; `.gamectl/qa/oc136-stub.json`, saves `cs1conv-ocutil-{skibidi,westfield,cacao}-stub`).
- **What map prep does now:** MapPrep `oc.utilities` (terrain/cs2-apply), run by the importer's last step (Outside connections, mod `MapSteps.Connections`) and by `terrain/mapprep.mjs connections` / `utilities`.
  - It picks imported road nodes at the edge of the road network: one per road island of at least 8 nodes, plus the main grid's west, east, south and north extremes; at most 8 per kind.
  - From each it builds a 24 m **Low-voltage Line** (and a 32 m **Combined Small Pipe**, 10 m down) outward. CS2 connects the stub to the road's low-voltage line and pipes.
  - On each stub's far node it puts a **Powerline Outside Connection** / **Water Outside Connection** marker. The game's `Electricity/WaterPipeOutsideConnectionGraphSystem` links the marker's owner node to the trade source and sink. It doesn't check voltage, so no transformer is needed.
  - The marker prefabs carry no `OutsideConnectionData`, which is why `oc.prefabs` doesn't list them; `oc.utilities` finds them through `ObjectData.m_Archetype`.
- **Why the stub:** a first version hung the markers on the road nodes themselves. Supply jumped the same way, but 3 min later Skibidi had 2 of 4 markers left and Westfield 1 of 5. When a node is updated (a building grows beside it), `SubObjectSystem` deletes every sub-object of that node that isn't a building, service upgrade or `Secondary` object [IL]. The stub's far node has only the stub edge, so nothing updates it, like the base map's high-voltage marker.
- **Results** (acceptance saves of §7; markers added after loading; 3 min at speed 3, then saved, reloaded, 1 more min):

| | Skibidi 3 km (`…-133435`) | Westfield (`…-140001`) | CacaoCity (`…-134438`) |
| --- | --- | --- | --- |
| Markers electricity + water: created / after 3 min / after reload | 4+4 / 4+4 / 4+4 | 5+5 / 5+5 / 5+5 | 4+4 / 4+4 / 4+4 |
| Residential electricity / water / sewage supplied | 99.2 / 98.6 / 98.6 % (before: 0 / 0 / 0) | 99.2 / 98.6 / 98.6 % (before: ~3 / 0 / 0) | 100 / 99.4 / 99.4 % (before: 99 % electricity, 669 buildings without water) |
| Electricity: production, import | 0, **135 930** of 156 444 | 0, **27 155** of 27 155 | own plants, export 52 607 (after reload: import 100 498) |
| Water fulfilled (capacity of own pumps) | 233 983 of 237 733 (0) | 111 395 of 111 395 (0) | 182 855 of 245 855 (101 694) |
| Outside-connection lanes before → after 3 min | 173 → 179 | 188 → 188 | 51 → 51 |

- **Notes:**
  - The markers add no traffic lanes. `Net.OutsideConnectionSystem` excludes `Electricity/WaterPipeOutsideConnection` from both its queries [IL]. Skibidi's +6 lanes appeared during the simulation, not when the markers were created.
  - Cacao still shows its own facilities' problems: its plants aren't on the high-voltage grid, and its pumps aren't connected (buildings worker: `powerhook`, `utilities`). The city imports what they don't give.
  - "Remove imported" should delete the stubs with the rest, since they are new top-level edges and nodes in the importer's snapshot. Not tested.
  - These runs were on right-hand cities (e2e had skipped the driving side, #138). Driving side doesn't affect lines and pipes.

**Stub starts and junctions (#148)** (web worker; `.gamectl/qa/{beforeafter,variants,oc148,stubcheck}.json`, saves `cs1conv-ocutil-{westfield,skibidi,cacao}-148`).
- **No split.** A stub start at the road node's spot looked like a split junction: three nodes there listed the same road edge. Read by real endpoints (`Edge.m_Start` / `m_End`), the road keeps its junction node. The line's and the pipe's start nodes list the road edge only as a local connection to its middle, which is how CS2 joins a line or pipe to a road. Measured before and after on Westfield's 5 picks, for four start variants: free start at the node, `m_Entity` = road node (CS2 still makes its own node there), DisableMerge, and setback. No variant moved a road endpoint, and all supply the same.
- **Default now:** the stub starts 3 m along one of the road node's own road edges (always on the road surface), with DisableMerge. No second node sits at the junction's spot, and position-based tools see one node per junction.
- **`oc.stubcheck`** (MapPrep, read-only) checks every utility marker's stub:
  - it follows the stub's chain of line/pipe edges (CS2 may cut a stub where it crosses something) to the node that touches a road;
  - **split** = that node is a real endpoint of a road edge while another node within 3 m is one too;
  - **notTouchingRoad** = the chain never reaches a road.
  - The release acceptance runs it after the import (step `utility-stubs` in `tools/release/acceptance.mjs`, writing `stubcheck.json`): a split or no marker fails the step; `notTouchingRoad` is only reported.
- **Results** (acceptance saves of §7, markers added after loading, 3 min at speed 3, saved, reloaded, 1 more min):

| | Westfield | Skibidi 3 km | CacaoCity |
| --- | --- | --- | --- |
| Markers electricity + water, after 3 min and after reload | 5+5, 5+5 | 4+4, 4+4 | 4+4, 4+4 |
| Splits (created / reloaded) | 0 / 0 | 0 / 0 | 0 / 0 |
| Stubs not reaching a road | 0 | 1 (water) | 0 |
| Residential electricity / water / sewage, after reload | 98.8 / 98.6 / 98.6 % | 99.2 / 98.6 / 98.6 % | 99.4 / 99.4 / 99.4 % |
| Electricity import, with 0 production | 27 707 | 138 378 | 102 888 (own plants not on the grid) |

- **Skibidi's unlinked stub:** its pipe start lies 3 m into a three-way junction, and the pipe (10 m down) isn't joined to the road's pipes there. The low-voltage stub at the same spot is joined. The city's other three water connections carry the supply.

**Stub starts away from dead ends and stops** (web worker, after worker-4's Bus Line 2 test, `.gamectl/tracklink/bus-line2.json`; results `.gamectl/qa/oc167.json`).
- **Cause:** a stub node 2 m from an avenue's dead end (Skibidi node 19810, the "main grid, north" pick) kept CS2 from building the turnaround there. Bus Line 2's buses couldn't turn back, and the line got none. Without that stub it got 2 moving buses.
- **Fix** (`oc.utilities`): a stub never starts on a dead-end chain, meaning a node with one road edge (by real endpoints) and the two-edge nodes leading to it, up to the first junction. That also covers one-way chains to the crop edge. It also never starts within 50 m (`stopGuard`) of either end of a road segment that carries a kerbside bus or tram stop. Picks are made among the remaining nodes with ≥ 2 road edges.
- **Skibidi:** 241 dead-end-chain nodes and 290 stop segment ends excluded, leaving 1048 of 1407 candidates. The north pick moved from (618, 1498) to the junction at (−328, 1472); the south pick from (−1174, −1499) to (−1334, −1497). CacaoCity: 95 dead-end-chain nodes excluded.
- **Results**, 3 min at speed 3, then save, reload and 1 min: 0 splits before and after the reload in both cities. Skibidi residential supply 99.2 / 98.8 / 98.8 % after the reload, with 140 991 electricity imported and 0 produced. CacaoCity 99.2 / 97.9 / 97.9 %. Skibidi's east water stub still doesn't reach a road (the junction case above).
- **Not yet shown:** Bus Line 2 running with buses. It also needs the placer fix (#166) and comes with the acceptance.

**Rail hunt stopped (coordinator #133).** In run 13 (`cs1conv-train6k-st3-yards2`), the opt-in interior SubwayYard02's 110 m link to the elevated metro line **was built**: the yard node has the link edge, and the line node has 3 edges (`.gamectl/tracklink/yardjoint-run13.json`). Metro Lines 1 and 3 have paths (27 and 78 elements). Even so, the yard's 2 trains stayed parked for 480 s and the lines show 0 vehicles.
- **Open lead:** the depot doesn't dispatch to the lines. Possibly it can't pathfind from the yard to a line waypoint (lane direction), or the dispatch request needs something else.
- **Status:** trains and metros remain a known limit for this release. Interior yards and bridges stay opt-in or default as recorded in DECISIONS-transit #12–14.

## 10. Full city, all features (cs2-importer worker, docs/DECISIONS.md #143)

> **Correction (2026-11-22, DECISIONS.md #164, #165):** the bus totals in this section count every `Bus01` in the city, parked ones included. No imported bus line got a vehicle: the buses stayed parked in the depot and every line's vehicle request failed. Imported bus lines have never got vehicles in the mod flow; this is a known limit, being fixed.
>
> **Fixed in 2680feb** (DECISIONS.md #166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street, and imported bus lines get buses.

**Setup:**
- **Package:** Skibidi at the largest crop (`--auto-crop 14336`, centre 1250/1250), web 0.9.3 (HEAD f39eac3): 4741 segments (101 tunnels, 112 with Node Controller shifts), conform burn 3208 segments and walls burn 877 (none skipped), 253 buildings, 38,900 trees and 539 rocks, 16 transit lines, 928 TM:PE lane connections.
- **Flow:** the mod-only user flow with every default step, importer 0.3.4 built from the same HEAD: new city on Sunshine Peninsula → Prepare map → Check → Import → 5 min simulation at speed 3 → save → reload.
- **Script:** `stress.mjs` (cs2-importer worker's scratch tooling), `.gamectl/stress/full-rc3/`. A separate process pings the bridge every 250 ms, so a frame stall shows as a slow ping.

| Step | Time | Worst stall (ping) | Result |
| --- | --- | --- | --- |
| Start CS2 | 57 s | 7.7 s (loading) | |
| New city | 19 s | 4.5 s | |
| Prepare map | 17 s | 1.8 s | 197 base roads and 5 buildings removed; terrain within 0 m at 95 % of 181 check points; 15 water sources |
| Check | 4 s | 3.9 s | 4740 roads to build, all 77 road types available |
| Import (all steps) | 70 s | 3.0 s | roads 8.1 s, traffic 2.2, Traffic mod 1.8, lanes 3.6, zoning 4.4, buildings 2.7, trees 2.5, surfaces 0.9, utilities 0.8 |
| Simulation, 5 min at speed 3 | 300 s | 0.34 s | |
| Save | 2.6 s | 1.5 s | |
| Reload | 8.5 s | 2.7 s | Traffic mod: "couldn't load data from 16 of 233 intersections" |

Memory wasn't captured (the sampler's parsing failed on the localised `tasklist` output; fixed for the next run). No crash, no exception, no step error.

**Results:**
- **Roads:**
  - 4738 of 4740.
  - Node 17400 not created: two "Train Track Elevated" pieces at ground level, the same class as the whole-Skibidi M2 run.
  - 1 bridge without its elevated composition.
- **Tunnels:** CS1's 101 tunnel segments all built as tunnels (29 deepened, #124), plus 13 slopes. CS1 ground roads: 0 tunnels, 1 elevated (a pedestrian pavement).
- **Height error** (op `heights`):

  | Kind | Segments | Median | 90th percentile | Over 2 m |
  | --- | --- | --- | --- | --- |
  | Ground | 4101 | 0.06 m | 0.58 m | 18 (worst 5.8 m) |
  | Elevated | 440 | 0.11 m | 0.87 m | 15 |
  | Tunnels | 101 | | | 21 |

  - Tunnels: deepened on purpose (#58), up to 4.3 m below CS1.
  - Elevated: the worst is a pair of "Train Track Elevated" pieces at elevation 0 whose shared node is 9 m above the natural terrain in CS1. With no height mode there, the M2 rule snaps it, a legacy residual.
- **Other steps:**
  - lane references 2529 of 2530;
  - zoning 94.8 % of 122,119 cells;
  - buildings 166 of 253 (87 to place yourself, #106);
  - trees and props 38,335 of 39,439;
  - surfaces 2 of 2;
  - utilities 420 of 420 placed;
  - transit 20 of 20 lines and 305 of 305 stops;
  - outside connections 10 of 10 (6 road, 4 train).
- **Lane connections** (op `lanes` check-only):

  | | Lane refs | Explicit sources | Arms off | Turns wrong |
  | --- | --- | --- | --- | --- |
  | Before reload | 2462/2530 | 660/690 (95.7 %) | 0 of 700 | 2 of 496 |
  | After reload | 2288/2530 (174 "lane count differs") | 591/632 (93.5 %) | 0 | 16 of 463 |

  The loss after reload and the Traffic mod's 16 junctions that didn't load are #142 (worker-5). The run used committed code, without their uncommitted lane repair step.
- **Utilities after 5 min** (`tools/qa`):
  - electricity 96.7 %, water 99.1 %, sewage 99.1 % supplied;
  - service buildings' electricity 88.2 % connected;
  - 10 power facilities not connected (wind turbines and a small coal plant: low- or high-voltage not connected; buildings worker);
  - production 280 MW against 294 MW consumption, the rest imported.
- **Vehicles after 5 min:** 55 public transport vehicles (36 buses, 2 trains with 6 carriages, 10 prison vans, 1 plane) on 20 routes, from 12 depots. No metro or tram vehicles (#83).
- **Segments lost after the import:** 21 plan segments that were built can't be resolved by later read-only ops (`heights`, `lanedirs`; the lane check's 67 `segment_or_node_not_found`). Only 2 failed in the import itself.
  - **Cause:** the importer's resolver, not the utilities.
    - Where a CS1 power line or pipe ends on a road node, CS2 gives the line its own node at the same position. That node lists the road edge in `ConnectedEdge` as a local connection (how a line joins a road), not as an endpoint.
    - The importer's node lookup could pick that node, and its path walk followed the local connection (buildings worker's analysis, confirmed against `inspect-missing.json`).
    - Fixed in the importer: `GameWorld.EdgesOf` returns only edges the node starts or ends, and the node index ranks utility nodes last (DECISIONS-importer.md #64).
    - Confirmed on the saved city: `heights` and `lanedirs` now resolve 4738 segments with 2 missing (the import's 2 failures), down from 20.
    - The buildings worker also pulls line ends 3 m back from road nodes, with a road guard.
  - **Roads that change on reload (#142):** 19 segments on Skibidi.
    - They carry bus stops placed by the transit step. The edge's composition picks the stop variant (`PrimaryStop`, plus a Car Bay Lane) only on the next load, which shifts lane indices, so lane connections written in session break.
    - Fix with worker-4 (`Transit.cs` marks the attached edge `Updated`). Acceptance now fails on any road that differs after a save and reload (`roads-reload` step).
- **Not covered:** memory (to repeat with the fixed sampler), the lane repair step (#142, uncommitted), metro and tram vehicles.

## 11. Release candidate 3 (buildings worker)

> **Correction (2026-11-22, DECISIONS.md #164, #165):** the bus totals in this section count every `Bus01` in the city, parked ones included. No imported bus line got a vehicle: the buses stayed parked in the depot and every line's vehicle request failed. Imported bus lines have never got vehicles in the mod flow; this is a known limit, being fixed.
>
> **Fixed in 2680feb** (DECISIONS.md #166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street, and imported bus lines get buses.

`node tools/release/acceptance.mjs --skip-build --simulate 300 [--cs1x %LOCALAPPDATA%/cs1-to-cs2/qa/<city>.cs1x]` after `node tools/release/build.mjs --npm-ci` and `--check`.

**Clean build:** from a `git archive` of **b71a160** in `%LOCALAPPDATA%\cs1-to-cs2\rc3\b71a160`, plus the machine's `local.props`.
- **Artefacts:** product 0.3.0; exporter 1.2.2, **importer 0.3.4**, **web 0.9.3**, **buildings 0.3.3**. `build.mjs --check` passed.
- **Flow:** the user's, the same as RC2: Install Road Builder configs → restart → new city on Corral Riches → Check → Prepare map → 20 s → Import → 300 s at speed 3. The release mod was installed for each city and the dev mod restored after each. CS2 was stopped before the lock was released.
- **New checks:**
  - `report-complete`: every step the mod registered, plus the enabled built-ins, has an entry in the import report;
  - `heights-tunnels`: the heights dump finds every built road, and the CS1 tunnel count is met;
  - `utility-stubs`: no outside-connection stub splits a junction, counted by real edge endpoints.
- **Result:** all three **ok=true**, every step passed. The Skibidi run was repeated (`20261121-232522`) because the clean tree hadn't built the transit library, so the first run (`20261121-225501`, otherwise identical) skipped the vehicles count; see the findings.

| | Skibidi 3 km (`20261121-232522`) | CacaoCity, left-hand (`20261121-230438`, `qa/cacao-v2.cs1x`) | Westfield (`20261121-231503`) |
| --- | --- | --- | --- |
| Check / Prepare map | all 37 road types; terrain 95 % exact | all 42 types; **"Traffic drives on the left, as in the CS1 city."** | all 16 types |
| Roads | **2035 / 2035** (4.4 s) | **684 / 684** (2.4 s) | **648 / 648** (2.3 s) |
| **Report complete** | all 7 registered steps (buildings, objects, districts, surfaces, utilities, **powerhook**, transit) and 8 built-ins present | the same | the same |
| **Heights / tunnels** | 2035 segments, **0 missing** | 684, 0 missing | 648, 0 missing |
| **Utility stubs** | 8 markers, **0 split** (1 not touching a road) | 8, 0 split | 10, 0 split |
| Lane directions | **all 1949 the same** (353 one-way, 6 asymmetric) | **all 554 the same**; CS1 and CS2 left-hand | **all 647 the same** |
| Lane connections | 363 at 70 of 95 junctions; 685/685 refs; turns **117/117** | 341 at 68 of 79; 727/729; turns **167/167** | 127 at 34 of 39; 299/301; turns **81/81** |
| **Roads after reload** | **0 of 2035 differ** | 0 of 684 | 0 of 648 |
| **Lane connections after reload** | 70 junctions, 0 loader failures, turns 117/117 | 68, 0, 167/167 | 34, 0, 81/81 |
| Traffic / Traffic mod | 46 turn bans, 123 lights removed; 325 priority signs | 14 lights removed; 57 signs | 40 turn bans; 3 signs |
| Zoning | 94.2 % | 91.1 % | 99.99 % |
| Buildings | **78 / 108** (30 to place yourself, all lot overlaps) | **26 / 43**; warnings: no power plant placed (5 in CS1), 5 "connect a road" | **7 / 13**; 3 "place a transformer station" |
| Trees / surfaces | 3478 / 4216 | 11 300 / 12 260; surfaces 44 / 44 | none |
| Utility lines / power hookup | 76 / 76; powerhook done | 147 / 147; done | 106 / 106; done |
| Transit lines | **9 / 9**, 145 / 145 stops | none in the city | none in the city |
| Outside connections | 20 / 20 | 4 / 4 | 22 / 22 |
| Homes supplied after 300 s (electricity / water / sewage) | 99.4 / 99.2 / 99.2 %; QA ok | 100 / 100 / 100 % (11 homes); 2 pumps `notEnoughSurfaceWater` | 100 / 100 / 100 %; 3 incinerators `highVoltageNotConnected` |
| Vehicles after 300 s | **4 buses**, 9 routes, 16 depots; 0 metro, 0 train | – | – |

**Against RC2 (§8):**
- **Same or better:** roads, lane directions, lane connections (Skibidi 363 against 360, turn checks all correct), outside connections, surfaces and transit lines.
- **New and passing:** roads and lane connections after reload (0 differences; RC2 didn't check this), utilities (~99 % of homes through the outside connections; RC2 had no utilities), report completeness, heights and tunnels, utility stubs.
- **Buildings: fewer, by decision.** 78/108, 26/43 and 7/13, against 108/108, 43/43 and 13/13. RC2 still moved buildings to free land by default; #106 (user) keeps them at their CS1 spot. Every one not placed is listed as "place it yourself".

**Findings:**
1. **Skibidi's buses: 4 against RC2's 34.** The city's only Bus Depot overlaps a road at its CS1 spot, so under #106 it isn't placed. Because the CS1 city has a depot, the web tool adds no default one either, so the 9 bus lines have no depot to send buses from.
   - **Fixed in the working tree:** the placer now warns when a depot type the plan has gets none placed: "This city has no bus depot placed (1 in CS1): its bus lines get no vehicles; …" (RulesTests 10/10).
   - **Coordinator's call:** whether a CS1 depot should get the default depots' opt-out `nearby` fallback, since without it the lines run nothing.
2. **CacaoCity's Water Tower became the surface pump** (WaterPumpingStation01, `notEnoughSurfaceWater`). CS1's Water Tower has an intake (`m_waterIntake` 3750) and `m_useGroundWater: true`, which matcher 1.2.0's tower rule didn't cover.
   - **Fixed in the working tree:** matcher 1.2.1 (DECISIONS-buildings #43), so it becomes the groundwater pump.
   - The other flagged pump, at CS2 (535, −941), is a CS1 shore intake on dry CS2 land (#42, open).
3. **Westfield's 3 incinerators show `highVoltageNotConnected`.** Expected: transformers are opt-in (#150), and the report says "place a transformer station near it". Homes are supplied through the outside connection.
4. **Acceptance in a clean tree didn't build the transit exec library**, so the first Skibidi run had no vehicles count. Fixed in the working tree: `build-exec-lib` builds it too.

**Not covered:** trains and metros (known limit, #99; Skibidi's subway line is created with 0 vehicles). The CacaoCity city had only 11 homes after 300 s, so its percentages are on a small base.

## 12. Final release acceptance, 0.3.1 (buildings worker)

> **Correction (2026-11-22, DECISIONS.md #164, #165):** the bus totals in this section count every `Bus01` in the city, parked ones included. No imported bus line got a vehicle: the buses stayed parked in the depot and every line's vehicle request failed. Imported bus lines have never got vehicles in the mod flow; this is a known limit, being fixed.
>
> **Fixed in 2680feb** (DECISIONS.md #166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street, and imported bus lines get buses.

Same procedure as RC3 (§11): a `git archive` of **13c25e5** in `%LOCALAPPDATA%\cs1-to-cs2\final\13c25e5` plus `local.props`; `node tools/release/build.mjs --npm-ci`, then `--check`; then `node tools/release/acceptance.mjs --skip-build --simulate 300` per city.
- **Artefacts:** product **0.3.1**: exporter 1.2.2, **importer 0.4.0**, **buildings 0.4.0**, **web 0.9.4**. Build and `--check` passed.
- **Result:** all three **ok=true**, every step passed. The release mod was installed per city and the dev mod restored, and CS2 was stopped before the lock was released.

| | Skibidi 3 km (`20261122-000820`) | CacaoCity, left-hand (`20261122-001757`) | Westfield (`20261122-002658`) |
| --- | --- | --- | --- |
| Roads | **2035 / 2035** | **684 / 684**; "Traffic drives on the left, as in the CS1 city." | **648 / 648** |
| Report complete / heights / stubs | ok / 0 missing / 8 markers, 0 split | ok / 0 / 8, 0 split | ok / 0 / 10, 0 split |
| Lane directions | all 1949 the same | all 554 the same (left-hand) | all 647 the same |
| Lane connections (turn check) | 363 at 70/95; 685/685 refs; **117/117** | 341 at 68/79; 727/729; **167/167** | 127 at 34/39; 299/301; **81/81** |
| Roads / lane connections after reload | **0 differ** / 0 loader failures | 0 / 0 | 0 / 0 |
| Buildings | **79 / 108**; Bus Depot moved (#152) | 26 / 43; "no power plant placed" warning, 5 "connect a road" | 7 / 13; 3 "place a transformer station" |
| **In-game summary: outside connections** | "20 of 20 (13 road, 7 train, **4 electricity, 4 water**)" | "4 of 4 (4 road, 0 train, **4 electricity, 4 water**)" | "22 of 22 (22 road, 0 train, **5 electricity, 5 water**)" |
| **In-game summary: moved depot** | "Warning: Bus Depot: placed away from its CS1 spot because its lines need a depot (117 m from its CS1 spot, CS1 (3370, 198) = CS2 (−380, 1448))" | – | – |
| Transit / vehicles after 300 s | 9 / 9 lines; **33 buses** on 9 routes (RC2 34, RC3 4) | none in the city | none in the city (6 buses, no lines) |
| Homes supplied (electricity / water) | 100 / 100 %; QA ok | 100 / 100 %; 1 pump `notEnoughSurfaceWater` | 96.9 / 98.8 %; 3 incinerators `highVoltageNotConnected` (transformers are opt-in) |

**Against RC3 (§11):** the same roads, lanes, reload and report results to the digit, plus:
- **Skibidi's buses are back** (33 against 4), with the Bus Depot placed 117 m from its CS1 spot (#152).
- **CacaoCity's Water Tower** now matches a CS2 water tower (WaterTower02; matcher 1.2.1). Only the CS1 shore intake at CS2 (535, −941) remains on dry land (#42, open).
- **The new summary lines** for electricity and water outside connections, and for moved depots, show in the mod's status.

**Residential counts vary from run to run: CS2, not the build** (checked at the coordinator's request).
- **The metric** is the number of buildings with `ResidentialProperty` (tools/qa), not households.
- **Same inputs** in the 7-home run (`20261122-000820`) and RC3's 647-home run (`20261121-232522`):
  - packages: zoning cells by type identical (46 670 / 21 608 / 3474 / 4379 / 439 / 88);
  - import: zoning 72 197 of 76 658 cells; buildings 79 vs 78; outside connections 4 electricity, 4 water;
  - 300.1 s simulated in both, ending at the same simulation frame (7 981 163 against 7 981 303);
  - in game (new read-only ops `census` and `zonecells`): the same 4212 zone blocks, about 50 000 free "NA Residential Low" cells, the same residential building demand (0 / 100 / 0) and household demand 92 against 106.
- **The difference:** the low run had 554 households but only 7 residential buildings. 3 more minutes added just 1 (with an `EmptyBuildings` penalty of −257), while RC3's save grew from 647 to 1126 in the same 3 minutes. Commercial and industrial grew the same in both.
- **Repeat on the same final build** (`20261122-010305`, ok=true): **667 residential buildings and 34 buses**. The same thing happened in RC3's Westfield (7 homes) against this release's (160), and in CacaoCity (11 and 15 in acceptance, 228 in an e2e run).
- **Conclusion:** CS2 sometimes stalls residential spawning in a fresh city; it isn't the import or 0.3.1. The acceptance doesn't use these counts as a pass criterion, and the supply percentages are on a small base when it happens.

**Release zips and licence** (`release/0.3.1/`):
- **cs1-exporter-1.2.2.zip:** `Cs1Exporter/Cs1Exporter.dll`, `Cs1Exporter/PreviewImage.png`, `CHANGELOG.md`, `workshop-description.txt`.
- **cs2-importer-0.4.0.zip:** `Cs2Importer/Cs2ImporterMod.dll`, `Properties/PublishConfiguration.xml`, `Properties/Thumbnail.png`, `CHANGELOG.md`.
- **web-0.9.4.zip:** `index.html`, `assets/` (3 files), `cs2-catalogue.json`, `cs2-roads.json`, `THIRD-PARTY-NOTICES.txt` (fflate, React and the other bundled packages, each with its licence).
- **The AI disclaimer is in:** the importer's `PublishConfiguration.xml` (short and long description: "Made with AI coding assistance") and the exporter's `workshop-description.txt`.
- **Finding: none of the three zips contains the project's `LICENSE`** (MIT, in the repository root), and `RELEASE-NOTES.md` doesn't mention it. MIT asks that the copyright and permission notice go with copies of the software, so `LICENSE` should be added to each zip in `tools/release/build.mjs`. Per the instructions, `build.mjs` was left unchanged during this run.

**Not covered:** trains and metros (known limit, #99).

## 13. Final release acceptance, 0.3.1 with Node Controller offsets (buildings worker)

> **Correction (2026-11-22, DECISIONS.md #164, #165):** the bus totals in this section count every `Bus01` in the city, parked ones included. No imported bus line got a vehicle: the buses stayed parked in the depot and every line's vehicle request failed. Imported bus lines have never got vehicles in the mod flow; this is a known limit, being fixed.
>
> **Fixed in 2680feb** (DECISIONS.md #166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street, and imported bus lines get buses.

Same procedure as §11 and §12: a `git archive` of **693ba8a** in `%LOCALAPPDATA%\cs1-to-cs2\final\693ba8a` plus `local.props`; `build.mjs --npm-ci`, then `--check`; then `acceptance.mjs --skip-build --simulate 300` per city.
- **Artefacts:** product 0.3.1: exporter 1.2.2, importer 0.4.0, buildings 0.4.0, **web 0.9.5**. Build and `--check` passed.
- **Every zip now has `LICENSE.txt`:** cs1-exporter-1.2.2, cs2-importer-0.4.0 and web-0.9.5 (the §12 finding is closed).
- **The web report names the stretched junctions** (Skibidi package): "50 junctions stretched as Node Controller had them: 107 road ends pulled back by the CS1 offset (up to 52.069 m; 58 capped at 45 % of their segment), so CS2 builds longer junctions with smoother lane curves". It also lists the 17 road ends shifted sideways and the 22 nodes whose Node Controller type changed.
- **Result:** all three **ok=true**, every step passed; CS2 was stopped before the lock was released.

| | Skibidi 3 km (`20261122-012947`) | CacaoCity, left-hand (`20261122-013914`) | Westfield (`20261122-014813`) |
| --- | --- | --- | --- |
| Roads | 2035 / 2035 | 684 / 684 | 648 / 648 |
| **Heights / CS1 tunnels** (heights-tunnels) | 2035 segments, **0 missing**; tunnels as planned | 684, 0 missing | 648, 0 missing |
| Report complete / stubs | ok / 8 markers, 0 split | ok / 8, 0 split | ok / 10, 0 split |
| Lane directions | all 1949 the same | all 554 the same (left-hand) | all 647 the same |
| Lane connections (turn check) | **368 at 71 / 95** (was 363 at 70); 685/685 refs; **120 / 120** | 342 at 68 / 79; 727/729; 167 / 167 | **129 at 35 / 39** (was 127 at 34); 299/301; **83 / 83** |
| **Roads after reload** | **0 of 2035 differ** | 0 of 684 | 0 of 648 |
| **Lane connections after reload** | 71 junctions, 0 loader failures, 120 / 120 | 68, 0, 167 / 167 | 35, 0, 83 / 83 |
| Zoning | 93.96 % (94.18 % in §12) | 90.73 % (91.07 %) | 99.99 % |
| Buildings | 79 / 108; Bus Depot moved (#152) | 26 / 43; supply warnings as in §12 | 7 / 13; 3 "place a transformer station" |
| Outside connections (summary) | 20 of 20 (13 road, 7 train, 4 electricity, 4 water) | 4 of 4 (4 road, 4 electricity, 4 water) | 22 of 22 (22 road, 5 electricity, 5 water) |
| Vehicles after 300 s | **30 buses** on 9 routes; 24 train vehicles (6 engines, 18 cars), from the rail outside connections (this crop has no imported train line) | – | – |
| Homes supplied (electricity / water) | 98.9 / 99.4 %; QA ok | 100 / 100 %; 1 shore pump on dry land (#42) | 100 / 100 %; 3 incinerators need a transformer (opt-in) |
| **Households line** (new) | 851 households (0 homeless) in 634 residential buildings | 584 (0 homeless) in 143 | 10 (0 homeless) in 3 |

**Against §12:**
- **Node Controller offsets: more lane connections and no reload regressions.** Skibidi 368 at 71 junctions with 120/120 correct turns (363 at 70, 117/117 before), Westfield 129 at 35 (127 at 34). Roads and lane connections were still unchanged after a save and reload in all three cities.
- **Zoning 0.2–0.3 points lower** in Skibidi and CacaoCity: the longer junctions cover a little more of the zoned strip.
- **Everything else** is the same: roads, directions, heights, tunnels, stubs, report completeness, the moved Bus Depot, and supply.
- **The households line** shows where CS2 grew few homes: Westfield 3 buildings and 10 households after 300 s. That's the spawn variance of §12, not a supply problem (100 %, 0 homeless).

**Not covered:** imported metro and train lines (#99). Skibidi's crop has 8 bus lines and 1 subway line; its 24 train vehicles are outside-connection traffic.

### 5d. Train/metro hunt, round 2 (decision #159, 9 game runs, `.gamectl/tracklink/r159-*`, `lanereach-*`, `lanejoint-*`)

**Result: trains still don't leave the yard, but the cause is now located.** It's a missing lane connection at our link joints, not the lines.

1. **New tool:** `lanes.reach` (Transit op), a directed track-lane graph.
   - **Rule:** lane A leads to B when B's start, end or middle path node is A's exit; Twoway lanes are drivable both ways.
   - **Validated against CS2's own paths:** all consecutive lane pairs of the computed line paths are linked (26/26, 75/75).
2. **The yards are isolated.** SubwayYard02's and RailYard02's 4 own track lanes reach only each other, never the link or the line (`reachedOutside: 0`), so depot dispatch has no path and the trains stay parked. That holds in every variant tried:
   - the link in the line's double track;
   - the link in the yard's own "Twoway … Track" (`yardLinkPrefab`);
   - nodes and edges at the link ends marked Updated after the apply, by position, since applying replaces the node entity (`touchEnds`);
   - the owning building marked Updated as well.
3. **The joint, lane by lane** (`lanes.joint`, run 8): two edges meet at a building-owned node.
   - **Working station link** (2500, −422): CS2 generated switch node lanes joining the link and platform lanes.
   - **Failing station link** (1938, −398) and **the yard link** (1017, −402): no node lanes and no shared path node between the two edges' lanes (not even the owner). Both sides' lanes keep their dead-end flags (`StartingLane` / `EndingLane`), as if the other edge weren't there.
   - That also explains the lines' partial paths: segments through a failing joint get no path.
4. **What didn't help:** marking the joint nodes, edges or the building Updated doesn't make CS2 regenerate those lanes.

**Open leads** (not tried, run cap):
- Build the link with CS2's own net tool (NetToolSystem with control points), as a player would, and compare the joint's lanes with ours.
- Find what differs between the working joint at (2500, −422) and the failing ones.
- Check whether `ApplyNetSystem` / `LaneSystem` skip lane generation at a building-owned node joined by a non-owned edge, unless the building's sub-net is re-created.

Everything stays opt-in (`--interior-yards`: interior yards, `maxGrade`, `yardLinkPrefab`, `touchEnds`); trains and metros remain a known limit (#99).

### 5e. Train/metro hunt, round 3 (#160, 6 game runs, `.gamectl/tracklink/r160-run*`): a train runs

**Cause:** a link that meets a track end at a sharp angle gets no lane connection. CS2 only builds switch lanes where tracks meet at a shallow angle.
- **Game-free diff of the round-2 joints:**
  - the working station link at (2500, −422) and the one at (1762, −410) leave the platform end along the station axis;
  - the failing one at (1938, −398) leaves it about 73° off the axis (and climbs 4 m in 23 m);
  - the yard link leaves the yard's track at about 90°.

**Fix, opt-in with `--interior-yards`:** `tangentLinks`. A link is a cubic curve that leaves its end along that end's own track, and arrives at the target:
- **at a dead end:** against the target track's outward direction;
- **at a through node:** along the line. On a one-way track that means its travel direction; on a double track either way.

Smooth targets are preferred, within 45° of the chord at both ends; otherwise the old straight link is used.

**Result** (run 4, fresh `cs1conv-train6k-st3`):
- the RailYard02's track lanes reach 246 lanes outside the yard (0 in every earlier run);
- Train Line 1 gets a vehicle;
- **the train leaves the yard:** it's EnRoute and Boarding at TrainStation03 (2358, 432), about 600 m away, in the saved state of run 5 (`vehicles-r160.json`);
- metro lines keep their paths (75 and 26 elements).

**Also found:** marking the link ends Updated afterwards (`touchEnds`, round 2) *breaks* the lines. With it, every line lost its paths (runs 2–3). It's now off unless asked for (`--touch-ends`). Marking the owning buildings Updated is removed.

**Metro is still blocked:** the interior SubwayYard02's track ends point south and sit right beside the east–west line, at a right angle. No smooth link fits, so the straight fallback leaves the yard isolated, and its trains stay parked.
- **Next:** place the yard with its tracks parallel to the line (buildings placer orientation), or give the link a lead-out curve longer than one cubic.

Trains run with the opt-in; metros remain a known limit until the yard can be aligned.

## 14. Final release acceptance, 0.3.1 with CS1 line names (buildings worker)

> **Correction (2026-11-22, DECISIONS.md #164, #165):** the bus totals in this section count every `Bus01` in the city, parked ones included. No imported bus line got a vehicle: the buses stayed parked in the depot and every line's vehicle request failed. Imported bus lines have never got vehicles in the mod flow. The per-line count of 0 below was right; this is a known limit, being fixed.
>
> **Fixed in 2680feb** (DECISIONS.md #166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street, and imported bus lines get buses.

Same procedure as §13: a `git archive` of **af82ed1** in `%LOCALAPPDATA%\cs1-to-cs2\final\af82ed1` plus `local.props`; `build.mjs --npm-ci`, then `--check`; then `acceptance.mjs --skip-build --simulate 300` per city, with default settings (rail off).
- **Artefacts:** product 0.3.1: exporter 1.2.2, importer 0.4.0, buildings 0.4.0, web 0.9.5. Build and `--check` passed. **`LICENSE.txt` is in all three zips.**
- **Result:** all three cities **ok=true**, every step passed. CS2 was stopped after each run.

**Settings and the shared lock (worker-4's rail runs in parallel):**
- **Overlap:** worker-4's scripts took `cs2.lock` while these runs held it, twice: `rail-roads.mjs` 29 s into the Skibidi run, and the line diagnostics during the Westfield run.
- **Rail left on:** a crashed rail acceptance of theirs (run 3, 04:26Z) left the mod's persisted Options at **`ConnectRail: true`**, and its import of CacaoCity collided with the first CacaoCity run here (`20261122-042353`). That run is discarded.
- **Re-runs:** CacaoCity and Westfield were re-run with `--mod-set '{"ConnectRail":false}'` (`20261122-043717`, `20261122-045623`). Skibidi (`20261122-041416`) ran with the default settings, which were checked on disk before the run.
- **No visible effect:** every number below matches §13 apart from the line names, which this commit fixes.
- **Follow-ups:**
  - Worker-4 pins `ConnectRail: false` in the acceptance's base Options (their working copy, uncommitted), so a default acceptance runs with rail off whatever the persisted state.
  - The old restore flips booleans, so the persisted Options were left at `ConnectRail: true` after these runs; worker-4 is resetting it.
  - Lock-taking scripts must wait on an existing lock.

| | Skibidi 3 km (`20261122-041416`) | CacaoCity, left-hand (`20261122-043717`) | Westfield (`20261122-045623`) |
| --- | --- | --- | --- |
| Roads | 2035 / 2035 | 684 / 684 | 648 / 648 |
| **Transit lines named** | **9 created, 9 named** ("Bus Line 12", "Bus Line 10", …, "Metro Line 3"); `named: 0` since RC3 | none in the city | none in the city |
| **Heights / tunnels** (now without track trimmed under stations) | 2035, **0 missing** (track 0, other 0, rail trimmed 0) | 684, 0 missing | 648, 0 missing |
| Report complete / stubs | ok / 8 markers, 0 split | ok / 8, 0 split | ok / 10, 0 split |
| Lane directions | all 1949 the same | all 554 the same (left-hand) | all 647 the same |
| Lane connections (turn check) | 368 at 71 / 95; 685/685; **120 / 120** | 342 at 68 / 79; 727/729; **167 / 167** | 129 at 35 / 39; 299/301; **83 / 83** |
| **Roads / lane connections after reload** | **0 differ** / 0 loader failures | 0 / 0 | 0 / 0 |
| Buildings | 79 / 108; Bus Depot moved (#152) | 26 / 43; supply warnings | 7 / 13; 3 "place a transformer station" |
| Outside connections | 20 of 20 (13 road, 7 train, 4 electricity, 4 water) | 4 of 4 (4 electricity, 4 water) | 22 of 22 (5 electricity, 5 water) |
| Vehicles after 300 s | **33 buses**, 16 train vehicles (outside-connection traffic) | 3 buses (no lines) | 7 buses (no lines) |
| Homes supplied (electricity / water) | 98.9 / 98.8 %; QA ok | 88.9 / 100 % of 9 homes; 1 shore pump on dry land (#42) | 100 / 100 %; 3 incinerators need a transformer (opt-in) |
| Households | 654 residential buildings | 588 households in 9 buildings, flagged as spawn variance by the new line | 7 in 6 |

**Against §13:** the same results to the digit, plus the CS1 line names (9 of 9) and the stricter heights check (track trimmed under stations excluded, 0 missing in all three).

**Finding: the new per-line vehicle count reports 0 for every Skibidi line** (`vehicles.lines`, from the transit library's `lines.list`), while 33 buses run on the city's 9 routes. Most likely `lines.list` doesn't count a line's vehicles the way CS2 assigns them; the totals by prefab are right. For worker-4 (transit library); it doesn't change the result.

**Not covered:** imported metro and train lines with rail on (worker-4's runs).

### 5f. Rail in the mod, round 2 (#161, 8 game runs, `.gamectl/acceptance/20261122-04*`, `-0521*`; opt-in `ConnectRail`)

1. **Imported roads lost near elevated stations: cause found and fixed.** The roads weren't deleted: each pair of plan segments was merged into one CS2 edge (`edges.at`, `transit/tools/rail-roads.mjs`).
   - **Cause:** the links' forced apply runs CS2's `NodeReductionSystem`, which removes plain two-edge nodes in the affected area.
   - **Fix:** `guardRoads` gives imported top-level two-edge nodes near the links a temporary `Game.Net.Fixed`, which that system's query excludes, and removes it after the apply.
   - **Result:** in acceptance run 4 the 4 roads are back, roads-reload is 0/3409, and lane reload is 468/468.
2. **Still open: 4 track segments next to the gap bridges** (e.g. 4244 and 7481 at (−1557, 619) / (−1381, 617)) are merged into the bridge course. Guarding the link end nodes too made it worse (run 8: 33 missing, and the metro lines lost their paths), so that's reverted.
3. **Yard joins:** `avoidRoads` blocks every course from Skibidi's RailYard02 (all cross roads at grade). `bridgeYard` builds a 3-piece bridge cut from the smooth curve (ramps ≤ 6 %, 10 m span, 5 m clear of roads), and acceptance reports it "linked". Run 7 (pieces still straight) showed the yard lanes still isolated; the tangent pieces are untested.
4. **Train Line 1 has no path in the mod:** its two stations don't reach each other along the track (`lanes.reach`, run 7), independent of the yard.
5. **Per-line vehicles:** still 0 for every bus line even when counted by `CurrentRoute`. The 34 `Bus01` may be depot-parked rather than running; to check with the `vehicles` op.

Rail stays off by default.

## 15. Final release acceptance, 0.3.1 with running bus lines (buildings worker)

Decision 168: a `git archive` of **66558c8** in `%LOCALAPPDATA%\cs1-to-cs2\final\66558c8` plus `local.props`; `build.mjs --npm-ci`, then `--check`; then `acceptance.mjs --skip-build --simulate 300` per city, with default settings (rail off).
- **Artefacts:** product 0.3.1: exporter 1.2.2, importer 0.4.0, buildings 0.4.0, **web 0.9.6**. Build and `--check` passed.
- **New since §14:**
  - the placer builds through Temp definitions and the tool's Apply (buildings #47), so imported bus lines run;
  - the per-line vehicles gate (lines-check);
  - corrected utility-stub counting (22e74cb);
  - the release texts.
- **Settings:** the persisted Options had `ConnectRail: false` before and after the runs. This acceptance pins it off and restores the user's value exactly.
- **Result:** all three **ok=true**, every gate passed; CS2 was stopped before the lock was released.

| Gate | Skibidi 3 km (`20261122-073603`) | CacaoCity, left-hand (`20261122-074532`) | Westfield (`20261122-075430`) |
| --- | --- | --- | --- |
| **Vehicles** (running on a line's route only) | **Bus lines with vehicles: 6 of 8, 24 buses running**; the other 2 wait for capacity (the one Bus Depot's 24 buses are all out); Metro 0 of 1, reported only | no lines in the city | no lines in the city |
| **Utility stubs** | 8 markers, **8 ok, 0 split** | 8, **8 ok, 0 split** | 4, **4 ok, 0 split** |
| **Roads after reload** | **0 of 2035 differ** | 0 of 684 | 0 of 648 |
| **Lane connections after reload** | 71 junctions, 0 loader failures, 120 / 120 | 68, 0, 167 / 167 | 35, 0, 83 / 83 |
| **Heights** | 2035, **0 missing** | 684, 0 missing | 648, 0 missing |
| **Report complete** | ok (7 registered steps + built-ins) | ok | ok |
| **Transit lines named** | **9 created, 9 named** | – | – |
| Placer's road guard | 2015 road edges, 0 gone, 0 rewired | 583, 0, 0 | 691, 0, 0 |
| Lane directions / turns | 1949 the same / 120 of 120 | 554 the same (left-hand) / 167 of 167 | 647 the same / 83 of 83 |
| Buildings | 79 / 108 (Bus Depot moved, #152) | 26 / 43 | 7 / 13 |
| Outside connections (utilities) | 4 electricity, 4 water | 4, 4 | 2, 2 |
| Homes supplied | electricity 98.2 %, water 98.1 %; 918 households in 674 homes | 90 / 100 % of 10 homes; 639 households (spawn-variance note) | 100 / 100 %; 3 households in 3 homes |

**Zips:** `LICENSE.txt` in cs1-exporter-1.2.2, cs2-importer-0.4.0 and web-0.9.6.

**Web 0.9.6's dropped stops** (Skibidi package report): "Bus Line 14: 2 stops outside the crop or off the imported roads were dropped; the line runs between the remaining 17". The same line appears for Bus Lines 12 (7 dropped), 10 (4) and 16 (1).

**Against §14:**
- **Imported bus lines now run:** 24 buses on 6 of 8 lines, against 0 on every line (the earlier prefab totals counted parked buses).
- **No regression:** roads, lanes, reloads, heights, stubs (now 8/8, 8/8 and 4/4 ok) and supply are all unchanged.
- **Capacity limit:** Skibidi's 2 idle lines are a capacity limit of the CS1 city's single depot, not a fault. The acceptance reports them as "wait for capacity, not failed".

**Not covered:** imported metro and train lines (rail is opt-in, #99).
