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
2. makes the package with the web CLI (`--cs1x`, default the Skibidi sample; `--auto-crop`, default 3000);
3. presses the mod's Options buttons as a user would: **Install Road Builder configs**, restart CS2, new city (`--map`, default Corral Riches), **Check**, **Import into this city**;
4. simulates for 60 s (`--simulate`), takes screenshots, saves `cs1conv-acceptance-<time>`, and writes `.gamectl/acceptance/<time>/summary.json` with each step's time and result.

The buttons are pressed through the Cs2Importer exec library's `op mod`, which calls the same `Actions` methods; there is no other way into the Options page from a script. Results go in [QA.md](QA.md) under "Release acceptance".

## Publishing (by the owner)

**Nothing is uploaded automatically.**
- **CS1 exporter:** to the Steam Workshop from the game's Content Manager, with `workshop-description.txt` and the preview image.
- **CS2 importer:** to Paradox Mods with the game's mod uploader.
- **Web tool:** to any static host.

## CI

`.github/workflows/ci.yml` runs on every push and pull request. It's one Windows job: the tools and some tests assume Windows paths. It caches the npm cache.
- **Checks:** the version check; the web tool (tests, typecheck, build); gamectl, terrain, buildings and release tests; the CS2 importer core tests (`dotnet run`, .NET 8).
- **Not built:** anything compiled against the games' DLLs (both mods, both bridges, the buildings library, terrain's CS2 apply). The games aren't on CI runners and their DLLs can't be redistributed. The release build is the place where those compile.
