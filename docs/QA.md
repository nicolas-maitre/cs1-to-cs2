# QA and robustness (2026-11-21)

**Status: partial.** The session hit its usage limit after the CI fix and three city exports. The full e2e run (step 3) and the exporter fix below are not done.

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
| Q1 | CI / web tests | CRLF checkouts fail `docs.test.ts` (above) | CI fixed (mine); test → worker-2 (sent) |
| Q2 | cs1-exporter `ZonesExporter.cs:94-128` | The in-game "geometry check" reports a **mean** distance of 3343 m on Rennmont: a few blocks with a stale or reused `m_segment` dominate it. The exported data is right: an offline check over all 6043 blocks against `network.json` gives median 12 m, p99 20.6 m, 9 blocks > 100 m. Fix: report the median and the count over 100 m, and skip segments that aren't `Created`. | mine, **not done** |
| Q3 | cs1 bridge `saves.list` / `save.load` | Saves are found by asset name or city name, not by file name. `DriniCity.crp` isn't loadable as "DriniCity"; its file date matches "NouvelleSauvegarde" (Rennmont). Duplicate names (local and cloud "Wod 2") resolve to the newest. Worth documenting in the gamectl README. | note |
| Q4 | web plan summary | "Transit lines: not-converted, no line has two stops in the crop" is shown for cities with **0** transit lines (Cacao, Westfield); "absent" would be right. | worker-2, **not yet sent** |
| Q5 | web mapping | The same workshop road family maps to very different widths: `Four-Lane One-Way Road (No Prkg)` (16 m) → RB 16 m, but `Four-Lane One-Way Road` (CS1 32 m) → RB 26 m with 9–11 m sidewalks. Probably faithful to the asset, but worth a look. | worker-2, **not yet sent** |
| Q6 | exporter terrain report | On Westfield, one ground node is 16.7 m off the bilinear terrain (p99 1.0 m). Probably a node on a raised Move It / terrain edit; not investigated. | mine, open |
| Q7 | web plan | Rennmont's 33 `Pedestrian Gravel` segments in the crop are skipped as Untouchable building sub-networks (park paths). This is correct; recorded so nobody chases it. | OK |
| Q8 | web traffic | TM:PE default speed limits (Cacao: Basic Road 0.6 = 30 km/h) are applied to roads without explicit limits (`m2/traffic.ts:57`). Verified. | OK |

## 3. Full pipeline on a new city

**Not done** (usage limit). The suggested candidate is CacaoCity: it has IMT, TM:PE default speeds, 32 % water and pathways, and its package is ready at `%LOCALAPPDATA%\cs1-to-cs2\qa\cacao-pkg`. Run `tools/e2e/run.mjs` on it with the cs2.lock, which worker-3's e2e held at 05:46.
