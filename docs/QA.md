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
