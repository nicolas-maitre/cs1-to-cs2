# Releasing

## Versions

**Scheme:** SemVer everywhere (`MAJOR.MINOR.PATCH`).

- **Product version:** the converter as a whole, in [`VERSION`](../VERSION). It follows the milestones while the project is pre-1.0: M1 = 0.1, M2 = 0.2, M3 = 0.3. It names the release folder and the top entry of [`CHANGELOG.md`](../CHANGELOG.md).
- **Component versions:** every deployable component keeps its own SemVer. Users see components separately: a Workshop mod in CS1, a mod in CS2 and a website, each updated on its own. A fix to the web tool shouldn't change the CS1 mod's version.

| Component | Declared in (source of truth) | Must match | Artefact |
| --- | --- | --- | --- |
| CS1 exporter mod | `cs1-exporter/src/Exporter.cs` (`ModInfo.Version`) | top heading of `cs1-exporter/CHANGELOG.md` | `cs1-exporter-<v>.zip` |
| CS2 importer mod | `cs2-importer/mod/Mod.cs` (`Mod.Version`) | `<Version>` in `Cs2ImporterMod.csproj`, `ModVersion` in `mod/Properties/PublishConfiguration.xml`, top heading of `cs2-importer/CHANGELOG.md` | `cs2-importer-<v>.zip` (mod folder, `Properties/` for Paradox Mods, changelog) |
| Web tool | `web/package.json` `version` | – | `web-<v>.zip` |
| Buildings steps (library) | `buildings/src/Steps.cs` (`Steps.Version`) | – | compiled into the importer mod |

**Bumping a version:**
- **When:** for a user-visible change in that component (PATCH = fixes, MINOR = features, MAJOR = an incompatible change to a format or behaviour).
- **Where:** the source-of-truth file and its "must match" twin, plus a changelog entry in the component's changelog.
- **The product version** moves when a set of component changes ships together, at least once per milestone.
- **File formats are versioned separately** (`.cs1x` `formatVersion`, plan `formatVersion`, see [schema/README.md](../schema/README.md)). A component version bump never changes a format version by itself.

`node tools/release/build.mjs --check` (also run in CI) fails when a declaration isn't SemVer or two declarations of the same component disagree.

## Building a release

```powershell
node tools/release/build.mjs            # everything; the mod builds need local.props (the games' DLLs)
node tools/release/build.mjs --skip-games   # web tool only (and checks), e.g. on a machine without the games
```

Output goes to `release/<product version>/` (git-ignored):

| Path | What |
| --- | --- |
| `cs1-exporter/Cs1Exporter/` | the CS1 mod folder: `Cs1Exporter.dll` + `PreviewImage.png` (copy to `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\`), plus `workshop-description.txt` and `CHANGELOG.md` next to it |
| `cs2-importer/Cs2Importer/` | the CS2 mod folder: `Cs2ImporterMod.dll` (copy to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\`) |
| `web/` | the static site (`web/dist`) |
| `<component>-<version>.zip` | one zip per component, folder layout as above |
| `versions.json` | product and component versions, git describe, artefacts |
| `SHA256SUMS.txt` | SHA-256 of every file in the release folder |
| `RELEASE-NOTES.md` | the top `CHANGELOG.md` entry, a component table, each component's latest changelog entry, install notes and the checksums |

**How the mods are built:** `dotnet build -c Release` with the deploy folder pointed into `release/` (`-p:CS1ModsDir=…`, `-p:CS2LocalMods=…`), so a release build never touches the installed games' mod folders. The web tool is built with `npm run build` (`--npm-ci` installs first).

**Checking a release:** unzip with any standard tool (Windows `Expand-Archive` was used) and compare against `SHA256SUMS.txt`. `node --test tools/release/build.test.mjs` checks the zip writer and the notes.

## Acceptance test

`node tools/release/acceptance.mjs` runs the user's flow with the release artefacts (needs local.props and the CS2 lock):
1. builds the release and installs the importer mod **from its zip** into CS2's Mods folder (the dev build there is backed up and put back; `--keep-mod` leaves the release build);
2. makes the package with the web CLI (`--cs1x`, default the Skibidi sample; `--auto-crop`, default 3000, or a fixed square with `--crop cx,cz,size`);
3. presses the mod's Options buttons as a user would: **Install Road Builder configs**, restart CS2, new city (`--map`, default Corral Riches), **Check**, **Prepare map**, **Import into this city**;
4. checks that every road runs the way it did in CS1: the importer's op `lanedirs` dumps each segment's CS2 car-lane directions, and [`lanedirs-compare.mjs`](../tools/release/lanedirs-compare.mjs) compares them with the export's CS1 lanes (one-way, two-way and asymmetric roads; left-hand cities included);
5. simulates for 60 s (`--simulate`), counts the public-transport vehicles by kind (bus, tram, metro, train) with the transit exec library (these totals include vehicles parked in depots), counts the vehicles moving on each imported line, and fails when an imported bus or tram line has none, unless every depot of that kind is out of vehicles (#164, #168), takes screenshots, saves `cs1conv-acceptance-<time>`, and writes `.gamectl/acceptance/<time>/summary.json` with each step's time and result.

The buttons are pressed through the Cs2Importer exec library's `op mod`, which calls the same `Actions` methods; there is no other way into the Options page from a script. Results go in [QA.md](QA.md) under "Release acceptance".

## Release candidate 3 (passed, 2026-11-21)

Built from a `git archive` of b71a160 and run through the real mod flow on Corral Riches with 5 minutes of simulation. Details are in [QA.md](QA.md) §11; decisions #151–152.

| | Skibidi 3 km | CacaoCity (left-hand) | Westfield |
| --- | --- | --- | --- |
| Roads built | 2035 / 2035 | 684 / 684 | 648 / 648 |
| Lane directions as in CS1 | 1949 / 1949 | 554 / 554 | 647 / 647 |
| Turn check (lane connections) | 117 / 117 | 167 / 167 | 81 / 81 |
| Roads and lane connections changed after save + reload | 0 | 0 | 0 |
| Homes supplied (electricity / water / sewage) | 99.4 / 99.2 / 99.2 % | 100 / 100 / 100 % | 100 / 100 / 100 % |
| Buildings placed | 78 / 108 | 26 / 43 | 7 / 13 |

- **Artefacts tested:** product 0.3.0; exporter 1.2.2, importer 0.3.4, web tool 0.9.3, buildings 0.3.3.
- **Fixed after RC3** (not in the tested build): matcher 1.2.1 (CS1's Water Tower → groundwater pump), the transit library built in acceptance, and the depot fallback of #152.
- **Known limits:** trains and metros (#99), buildings that overlap a road at their CS1 spot (#106), power producers away from any road (#150). They're in each changelog.
- **Correction (#164, #165):** imported bus and tram lines get no vehicles; the depot doesn't dispatch. This was found after RC3 and holds for RC2 and RC3 too: the bus counts in their acceptance runs (QA.md §8, §11) included parked buses. It's a known limit in each changelog, being fixed.
- **Fixed in 2680feb** (#166, #168): buildings are now placed through the game's own apply, so the depot is joined to the street. On Skibidi, 6 of 8 imported bus lines run 23 buses; the other 2 wait for vehicles from the city's one bus depot. The changelogs now say bus and tram lines run.

## Before you publish

1. **Versions:** bump each changed component (proposed: importer 0.4.0, buildings 0.4.0, product 0.3.1; exporter 1.2.2 and web tool 0.9.3 unchanged). Replace each changelog's "Unreleased" heading with the version and date. Importer: `Mod.Version`, the csproj `<Version>` and `ModVersion` in `PublishConfiguration.xml`.
2. **Checks:** `node tools/release/build.mjs --check` passes, and CI is green on the release commit.
3. **Build:** `node tools/release/build.mjs --npm-ci` from a clean checkout (or a `git archive`), with `local.props`.
4. **Acceptance:** `node tools/release/acceptance.mjs --skip-build --simulate 300` on Skibidi (the default) and on CacaoCity (`--cs1x %LOCALAPPDATA%/cs1-to-cs2/qa/cacao-v2.cs1x`). Every step must report ok, including roads after reload, utility stubs and report completeness. Add the results to [QA.md](QA.md).
5. **Store texts:** check the Steam Workshop description (`workshop-description.txt`) and the Paradox Mods page (`PublishConfiguration.xml`: description, changelog, version) against `RELEASE-NOTES.md`, including the known limits.
6. **Web tool:** the GitHub Pages build of `main` shows the same web version as the release.
7. **Publish** (below), then tag the release commit `v<product version>`.

## Publishing (by the owner)

**Nothing is uploaded automatically.**
- **CS1 exporter:** to the Steam Workshop from the game's Content Manager, with `workshop-description.txt` and the preview image.
- **CS2 importer:** to Paradox Mods with the game's mod uploader.
- **Web tool:** to any static host.

## CI

`.github/workflows/ci.yml` runs on every push and pull request. It's one Windows job: the tools and some tests assume Windows paths. It caches the npm cache.
- **Checks:** the version check; the web tool (tests, typecheck, build); gamectl, terrain, buildings and release tests; the CS2 importer core tests (`dotnet run`, .NET 8).
- **Not built:** anything compiled against the games' DLLs (both mods, both bridges, the buildings library, terrain's CS2 apply). The games aren't on CI runners and their DLLs can't be redistributed. The release build is the place where those compile.
