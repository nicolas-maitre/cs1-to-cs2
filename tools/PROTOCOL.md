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

`status` is a snapshot that the main thread refreshes about every 15 frames. Before the first frame it is `null`.

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

On the CS2 bridge `exec` takes an extra param, `phase`: `"main"` (default) or `"tool"`. `"tool"` runs the entry point inside a system in the `ToolUpdate` phase, the phase the road-placement spike needs.

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
4. **How the DLL is loaded:** `exec` reads the DLL's bytes on every call and loads them with `Assembly.Load(bytes)`. A rebuild therefore takes effect on the next call, and the file is never locked. Old copies stay loaded until the game exits, which is fine for development.
5. **Statics:** don't rely on static state surviving between calls, because each call loads a new copy. Keep state in the game world, or in a file.
6. **New ECS systems** cannot be registered into the CS2 update loop from `exec`. Use `phase:"tool"` or a coroutine instead.

## Lifecycle (gamectl)

| Game | Direct launch (Steam must be running) | Ready markers |
| --- | --- | --- |
| CS2 | `SteamAppId=949230 SteamGameId=949230 "<CS2>\Cities2.exe" -developerMode [-continuelastsave]` | `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\SceneFlow.log` contains `MainMenu reached`, then the bridge info file appears and `/health` answers |
| CS1 | `SteamAppId=255710 SteamGameId=255710 "<CS1>\Cities.exe" [-continuelastsave] [-noWorkshop] [-disableMods]` | `<CS1>\Cities_Data\output_log.txt` contains `Scene [MainMenu] loaded`, then the bridge |

- **Missing env var:** without `SteamAppId`, CS2 fails to initialise its platform services and floods its log.
- **Install paths:** the install folders come from `local.props` (`CS1Managed` / `CS2Managed` are `<install>\<Game>_Data\Managed`).
