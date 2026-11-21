# Cs2Bridge

Cities: Skylines II mod that implements the game bridge protocol ([../PROTOCOL.md](../PROTOCOL.md)) on port 17760.

```
dotnet build -c Release      # builds and deploys to %USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\Cs2Bridge
```

Needs `CS2Managed` in the repo's `local.props`. Same build setup as `spikes/m0-cs2-place-road`: net48 against the game's Mono BCL, managed code only, no source generators. The shared server and dispatcher come from `../bridge-common/*.cs`, compiled in.

## How it hooks in

| Piece | File | Notes |
| --- | --- | --- |
| `Mod : IMod` | `Mod.cs` | Starts `BridgeServer("cs2", 17760)` in `OnLoad` and stops it in `OnDispose`, which also deletes the info file. Logs to `Logs\Cs2Bridge.log`. |
| `BridgeDispatchSystem` | `BridgeSystems.cs` | Runs at `SystemUpdatePhase.MainLoop`, so it runs in the menu, during loads and in a city. Calls `BridgeCore.Tick()` every frame. |
| `BridgeToolPhaseSystem` | `BridgeSystems.cs` | Runs at `SystemUpdatePhase.ToolUpdate` and handles `exec` with `phase:"tool"`. Those calls fail if no city is loaded, because ToolUpdate doesn't run in the menu. A coroutine started there continues on the main-loop pump, or, with `pump:"tool"`, keeps running in ToolUpdate (one step per frame). |
| Game methods | `Cs2Methods.cs` | `state`, `saves.list`, `save.load`, `menu`, `quit`, `camera.get/set`, `sim.set`, screenshots |
| Converter test cities | `Cs2CityMethods.cs` | `maps.list`, `game.new` (new city from a map), `save.write` (only `cs1conv-*` names) |

## CS2 specifics

- **Speed:** CS2's 1x / 2x / 3x buttons set `SimulationSystem.selectedSpeed` to 1 / 2 / 4, and pausing sets it to 0. The bridge reports and accepts `speed` as the button level, 1–3. While the game is paused, `speed` is the last level that was used. `sim.set` also returns the raw `selected_speed`.
- **Camera:** these values map onto `CameraUpdateSystem.gamePlayController`:
  - `x`, `y` and `z` are the `pivot`;
  - `distance` is the `zoom`;
  - `yaw` and `pitch` are the `angle`, in degrees.

  Changes apply immediately, so `instant` is ignored. The game snaps the pivot's `y` to the terrain within a frame, so a `y` you set doesn't stick.
- **`saves.list`:** `name` is the save's asset name, which is its file name without `.cok`. `cloud` is true for saves that are not in the local user database, i.e. Paradox cloud saves. The list also includes `display_name`.
- **`save.load`:** matches on the asset name first, then the display name, then the city name, taking the newest save that matches. It replies about 10 frames after `city_loaded` becomes true. It fails if the game reports the load as failed.
- **`game.new`:** replays `MenuUISystem.NewGame` (read from the game's IL): sets the map name and prefab references on `MapMetadataSystem`, the city name, theme and options as `CityConfigurationSystem` overrides, normal game mode, the starting year, then `GameManager.Load(GameMode.Game, Purpose.NewGame, map)`. Unlike the menu it never writes the options to the user's settings. Defaults for test cities: unlock all, unlimited money and all map tiles on; natural disasters and left-hand traffic off.
- **`save.write`:** replays `AutoSaveSystem.AutoSave`: a 680×383 preview from the main camera, delete an existing asset of that name, `MenuUISystem.GetSaveInfo(false)`, `GameManager.Save(name, info, AssetDatabase.user, preview)`. Names must start with `cs1conv-`.
- **Autosave:** the game keeps autosaving while a city is loaded, using the interval and count from Options. When it creates an autosave it deletes the oldest one beyond that count, for every city. Keep test sessions short, or back up autosaves you care about.
- **HTTP clients:** Node's global `fetch` adds `Sec-Fetch-Mode: cors`, which the bridge refuses with 403. Use `node:http`, curl or PowerShell instead.

## Autosave guard

`AutosaveGuard.cs` keeps scripted sessions from rotating out the user's autosaves.

- **When it turns on:** the game was launched by `gamectl` (env `CS1TOCS2_GAMECTL=1`), a save was loaded through the bridge, or `autosave.set {"enabled":false}` was called.
- **What it does:** it disables the game's `AutoSaveSystem` in memory only. Settings are never written, and quitting the game undoes it.
- **How to undo it:** `autosave.set {"enabled":true}`, or the mod unloading.
- **Where it shows:** `state.autosave_blocked`.

Details: `tools/PROTOCOL.md`, section "CS2 autosave guard".

## Example exec library

`examples/HelloExec/` is a plain class library, not deployed. Build it with `dotnet build -c Release` and call it like this:

```json
{"method":"exec","params":{"dll":"C:/.../examples/HelloExec/bin/Release/net48/HelloExec.dll","type":"HelloExec.Hello","args":{"x":1}}}
{"method":"exec","params":{"dll":"...","type":"HelloExec.HelloCoroutine","args":{"frames":3}}}
{"method":"exec","params":{"dll":"...","type":"HelloExec.Hello","phase":"tool"}}
```

`Hello.Run` returns the build tag, the city name, entity counts and `called_from`. `called_from` is `BridgeDispatchSystem` for `phase` main and `BridgeToolPhaseSystem` for `phase` tool. To check hot reload, change `Hello.Build`, rebuild and call `exec` again without restarting the game.
