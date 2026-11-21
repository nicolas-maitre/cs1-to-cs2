# Game bridge protocol (v1)

Scripted, deterministic control of Cities: Skylines 1 and 2 for development and testing, instead of mouse/keyboard automation.

```
gamectl (Node CLI)  ──HTTP, 127.0.0.1 only──▶  bridge mod in the game  ──main-thread queue──▶  game API
       │                                         (tools/cs1-bridge, tools/cs2-bridge)
       └── starts / stops the game exe directly (no Paradox launcher), waits on log markers and the bridge
```

| Piece | Folder | Runtime |
| --- | --- | --- |
| Shared server + dispatcher | `tools/bridge-common/` | compiled into both bridges (C# 9, .NET 3.5 compatible) |
| CS2 bridge mod `Cs2Bridge` | `tools/cs2-bridge/` | .NET 4.8 against the game's Mono BCL, deploys to CS2 local `Mods\Cs2Bridge` |
| CS1 bridge mod `Cs1Bridge` | `tools/cs1-bridge/` | .NET 3.5 (Mono 2), deploys to CS1 `Addons\Mods\Cs1Bridge` |
| Driver `gamectl` | `tools/gamectl/` | Node ≥ 20, no npm dependencies |

## Discovery

On start the bridge writes `%LOCALAPPDATA%\cs1-to-cs2\bridge\<game>.json` (`<game>` = `cs1` or `cs2`):

```json
{"game":"cs2","port":17760,"token":"<48 hex chars>","pid":12345,"bridge_version":"0.1.0","protocol":1,"started":"2026-11-20T18:00:00.0000000Z"}
```

The token is new on every game start. A client must check that `pid` is a running game process (stale files survive a crash).

| Game | Port |
| --- | --- |
| cs2 | 17760 |
| cs1 | 17761 |

## Transport

The server listens on 127.0.0.1 only and handles one request per connection (`Connection: close`).

It refuses any request that carries `Origin`, `Sec-Fetch-Site` or `Sec-Fetch-Mode`, because those headers mean a browser sent it.

**`GET /health`** needs no token. It answers from the HTTP thread, so it still answers while the main thread is busy, for example during a load:

```json
{"ok":true,"game":"cs2","bridge_version":"0.1.0","protocol":1,"pid":12345,"status_age_ms":120,"status":{ ...state object... }}
```

`status` is a snapshot that the main thread refreshes about every 15 frames. Before the first frame it is `null` and `status_age_ms` is -1. While the main thread is blocked, for example during parts of a load, the snapshot goes stale (`status_age_ms` grows).

Clients must not use Node's built-in `fetch()`. It adds `Sec-Fetch-Mode`, which the bridge refuses with 403, and it gives up after 300 s. Use `node:http`, as `gamectl` does.

**`POST /rpc`** needs these headers:
- `Authorization: Bearer <token>`
- `Content-Type: application/json`

Body:

```json
{"method":"camera.set","params":{"x":0,"z":0},"timeout_s":60}
```

`timeout_s` is optional: default 60, maximum 3600. Responses:

| HTTP | Body | Meaning |
| --- | --- | --- |
| 200 | `{"ok":true,"result":...}` | done |
| 400 | `{"ok":false,"error":"..."}` | bad request, unknown method, handler threw, or loading |
| 401 / 403 / 415 | `{"ok":false,"error":"..."}` | token / browser / content type |
| 504 | `{"ok":false,"executed":false,"error":...}` | timed out before the game started it: it never runs |
| 504 | `{"ok":false,"executed":"unknown",...}` | timed out while running: its pumps are told to stop |

Commands run on the game's main thread, at most 8 per frame, in arrival order. A command can span several frames; for example `wait`, `screenshot`, `save.load` and coroutine `exec` reply only once they finish. While `state.loading` is true, only methods marked "while loading" run.

## The state object

Both games return the same keys. A key the game can't provide is `null`.

| Key | Type | Meaning |
| --- | --- | --- |
| `game` | `"cs1"` / `"cs2"` | |
| `mode` | string | `"menu"`, `"game"`, `"editor"`, `"loading"` or `"other"` |
| `loading` | bool | a load or unload is in progress |
| `city_loaded` | bool | a playable city is loaded and ready for commands |
| `city_name` | string | |
| `save_name` | string | the save the city was loaded from, if known |
| `paused` | bool | simulation paused |
| `speed` | number | selected simulation speed (CS1 1–3, CS2 1–3 as the game reports it) |
| `frame` | number | bridge frame counter; it increments once per rendered frame |
| `bridge_version` | string | |
| `autosave_blocked` | bool | CS2 only: the game's autosave is switched off by the bridge (see below); CS1 omits it |

## Methods

The common methods live in `bridge-common/BridgeCore.cs`; every game has them.

| Method | Params | Result | While loading |
| --- | --- | --- | --- |
| `ping` | – | state object | yes |
| `methods` | – | `[{name, help, while_loading}]` | yes |
| `wait` | `frames?`, `seconds?` | `{frames, seconds}` once both have elapsed | yes |
| `exec` | `dll` (absolute), `type?`, `method?`="Run", `args?` (any JSON) | see below | no |
| `screenshot` | `path` (absolute `.png`), `supersize?` 1–4 | `{path, bytes, frames}` once the file is written | yes |

Each game implements these game-specific methods, with the same names and shapes in both:

| Method | Params | Result | While loading |
| --- | --- | --- | --- |
| `state` | – | state object (same as `ping`) | yes |
| `saves.list` | `limit?`=50 | `{saves:[{name, city, modified (ISO), cloud?, autosave?}]}`, newest first | yes |
| `save.load` | `name` (save name; city name as fallback) | `{loaded:name, city}` once `city_loaded` (the bridge waits) | no |
| `menu` | – | `{menu:true}` once back at the main menu (unsaved progress is lost) | no |
| `quit` | – | `{quitting:true}`, then the process exits | yes |
| `camera.get` | – | `{x, y, z, distance, yaw, pitch}`: pivot / target position, zoom distance, angles in degrees | no |
| `camera.set` | any of `x, y?, z, distance, yaw, pitch`, `instant?`=true | the new camera, same shape as `camera.get` | no |
| `sim.set` | `paused?`, `speed?` | `{paused, speed}` | no |
| `autosave.set` | `enabled` (bool) | CS2 only: `{autosave_blocked, reason}` | yes |

CS2 also has these, for the converter's test cities:

| Method | Params | Result | While loading |
| --- | --- | --- | --- |
| `maps.list` | `limit?`=100 | `{maps:[{id, name, asset, theme, area, buildable_land, starting_year, playable, readonly, cloud}]}`, sorted by name | yes |
| `game.new` | `map` (id, display name or asset name), `name?`="cs1conv-city", `theme?` (the map's), `options?` `{unlockAll, unlimitedMoney, unlockMapTiles}` (default true) `{leftHandTraffic, naturalDisasters}` (default false) | `{map, map_id, city, theme, unlock_all, unlimited_money, unlock_map_tiles}` once `city_loaded` (the bridge waits) | no |
| `save.write` | `name`: must start with `cs1conv-`; letters, digits, `-`, `_`, `.` only | `{saved, city, overwritten, seconds}` once the file is written | no |

- **`game.new`** does what the menu's New Game button does (`MenuUISystem.NewGame`), with one difference: the options go only to `CityConfigurationSystem`'s overrides. The menu also stores them in the user's settings as the next defaults; the bridge never does. It turns the autosave guard on, as `save.load` does.
- **`save.write`** refuses every name that doesn't start with `cs1conv-`, so a script can never overwrite one of the user's saves. An existing `cs1conv-*` save of the same name is deleted first and rewritten (`overwritten: true`), as the game's autosave does with its own slots. It saves to the local user database (`AssetDatabase.user`), with a preview image rendered like an autosave's.

On the CS2 bridge `exec` takes two extra params:
- `phase`: `"main"` (default) or `"tool"`. `"tool"` runs the entry point inside a system in the `ToolUpdate` phase, the phase the road-placement spike needs.
- `pump`: `"main"` (default) or `"tool"`, for an entry point that returns an `IEnumerator`. With `"main"` the coroutine continues on the main-loop pump after its first call. With `"tool"` (needs `phase:"tool"`) every later step also runs in `ToolUpdate`, one step per frame, so each step can create definitions that the same frame's net generation picks up. The importer uses this to run a whole multi-frame import in one call. If `ToolUpdate` stops running (the city is unloaded) for 600 frames, the call fails.

### `exec`: hot-loaded command code

Converter commands and spikes run this way without restarting the game.

1. **Build a plain class library** against the game's DLLs, as the spikes do. It does not need to be a mod and needs no reference to the bridge.
2. **Add a public static entry point** in one of these shapes:
   ```csharp
   public static object Run()
   public static object Run(string argsJson)                  // args re-serialized as JSON
   public static object Run(Dictionary<string, object> args)  // args as parsed by MiniJson
   ```
3. **Return a result:**
   - a JSON-serializable object (dictionaries, lists, strings, numbers);
   - or a string that is JSON, which is passed through as-is;
   - or an `IEnumerator`. The bridge steps it once per frame, like a Unity coroutine. The last non-null value it yields is the result. This is for anything that needs a frame to pass, such as creating a road and then reading back the nodes the game generated.
4. **How the DLL is loaded:** `exec` reads the DLL's bytes on every call and loads them with `Assembly.Load(bytes)`. A rebuild therefore takes effect on the next call, and the file is never locked. Old copies stay loaded until the game exits, which is fine for development. Give every build a new assembly version (`<Deterministic>false</Deterministic><AssemblyVersion>1.0.*</AssemblyVersion>`): CS1's Mono returns the copy already loaded for an unchanged name and version, and `exec` then fails with an error instead of running stale code.
5. **Statics:** don't rely on static state surviving between calls, because each call loads a new copy. Keep state in the game world, or in a file.
6. **New ECS systems** cannot be registered into the CS2 update loop from `exec`. Use `phase:"tool"` or a coroutine instead.

## Lifecycle (gamectl)

| Game | Direct launch (Steam must be running) | Ready markers |
| --- | --- | --- |
| CS2 | `SteamAppId=949230 SteamGameId=949230 "<CS2>\Cities2.exe" -developerMode [-continuelastsave]` | `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\SceneFlow.log` contains `MainMenu reached`, then the bridge info file appears and `/health` answers |
| CS1 | `SteamAppId=255710 SteamGameId=255710 "<CS1>\Cities.exe" [-continuelastsave] [-noWorkshop] [-disableMods]` | `<CS1>\Cities_Data\output_log.txt` contains `Scene [MainMenu] loaded`, then the bridge |

- **Missing env var:** without `SteamAppId`, CS2 fails to initialise its platform services and floods its log.
- **Install paths:** the install folders come from `local.props` (`CS1Managed` / `CS2Managed` are `<install>\<Game>_Data\Managed`).

## CS2 autosave guard

With the user's settings (autosave every minute, keep 10), each minute a script spends in a loaded city would write an autosave and delete the oldest one. The CS2 bridge prevents that:

- **When it's on:**
  - when the game was started by `gamectl`, which sets `CS1TOCS2_GAMECTL=1` in the game's environment;
  - after a `save.load` through the bridge;
  - after `autosave.set {"enabled":false}`.
- **How it blocks autosaves:** it disables the game's `AutoSaveSystem` in memory (`Enabled = false`). That system's update is the only place that autosaves and prunes old autosaves. `GeneralSettings.autoSave` is never changed, so nothing reaches `Settings.coc`, and a normal game start always has autosave as configured.
- **How it's undone:** `autosave.set {"enabled":true}` gives the system back, and so does the mod unloading when the game quits.
  - Re-enabling it in a city that has been running longer than the autosave interval triggers an autosave straight away. Re-enable from the main menu if that matters.
- **Verified:** a city ran unpaused for 3 minutes with no autosave written. The autosave lines in `Settings.coc` and the save files were identical before and after.

## CS1 notes

Verified in the running game (CS1 with Unity 5.6, Mono 2) with the user's mod set:

- **Enabling the mod:** CS1 keeps new local mods disabled, and there is no command-line switch that enables them. Run `tools/cs1-bridge/enable-mod.ps1 -Write` once while the game is closed. Note that `-disableMods` also disables the bridge.
- **`exec` hot reload needs a new assembly version on every build.** Mono 2's `Assembly.Load(bytes)` returns the copy already loaded under the same name and version. A rebuilt DLL then silently runs the old code. Set `<Deterministic>false</Deterministic><AssemblyVersion>1.0.*</AssemblyVersion>` in the library's project, as `cs1-bridge/examples/HelloExec` does. A portable `.pdb` next to the DLL does no harm.
- **One `save.load` per game session.** A second load, whether from the menu or from inside a game, hangs forever at "Custom assets loaded n/m". Loading Screen Mod Revisited throws `IndexOutOfRangeException` in `ScenesAndAssetsStatus.EnsureLines`, which kills its asset loader. The first load after a start works (40–70 s for Skibidi.02). To switch saves, `quit` and relaunch the game (`-continuelastsave` also works).
- **While the main thread is blocked:** during `menu` (about 14 s) and parts of a load, the main thread barely ticks. Gated commands wait in the queue and run once it is free, instead of being refused. `/health` keeps answering and shows `mode:"loading"`.
- **`quit`** works in the menu, in game and in the middle of a hung load. With a city loaded, the process takes about 55 s to exit. An RPC still waiting at exit gets a dropped connection, not a reply.
- **`state.mode`:** `"menu"` means a `MainMenu` scene is loaded. At startup that scene sits additively under `IntroScreen`. `save_name` falls back to the game's own last loaded or saved name.
- **`sim.set`:** in CS1, changing the speed unpauses the game, so the bridge applies `speed` before `paused`.
- **`camera`:** `x`/`y`/`z` is the camera controller's target pivot, and the controller adjusts `y` itself. `distance` is clamped to the controller's minimum and maximum.
