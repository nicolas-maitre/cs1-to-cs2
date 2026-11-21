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
