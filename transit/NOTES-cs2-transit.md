# CS2 transport lines from code

Research notes, 2026-11-21. Source: CS2 1.6 `Game.dll`, read with the same metadata dumper and IL disassembler as `terrain/NOTES-cs2-terrain.md`. **[IL]** marks what was read from the IL; **[game]** marks what was measured in the running game (`transit/run-transit.mjs`).

## Data model [IL]

- **The line:** a route entity `Game.Routes.Route {m_Flags (Complete = 1), m_OptionMask}` with:
  - `TransportLine {m_VehicleRequest, m_VehicleInterval, m_UnbunchingFactor, m_Flags, m_TicketPrice}`;
  - `Game.Routes.Color {Color32}`, `RouteNumber {m_Number}`;
  - buffers `RouteWaypoint {m_Waypoint}`, `RouteSegment {m_Segment}`, `RouteVehicle {m_Vehicle}`;
  - `PrefabRef`, pointing to a `TransportLinePrefab` (`RouteData` + `TransportLineData {m_TransportType, …}`).
- **Waypoints:** each one is an entity `Waypoint {m_Index}` + `Position {m_Position}`. At a stop it also has `Connected {m_Connected = stop}`.
- **Segments:** each one is an entity `Segment` + `PathElement` buffer, i.e. the road path between two waypoints. Pathfinding fills it.
- **Stops:** objects with `Game.Routes.TransportStop {m_AccessRestriction, m_ComfortFactor, m_LoadingFactor, m_Flags}` and a `ConnectedRoute {m_Waypoint}` buffer. Their prefab has `TransportStopData {m_TransportType, m_AccessDistance, …}`.
  - **Kerbside stops** ("Bus Stop", "Tram Stop") are free objects attached to a road edge (`Attached {m_Parent = edge, m_CurvePosition}`).
  - **Station platforms** are sub-objects (`Owner`) of station buildings.
- **Transport types** (`Game.Prefabs.TransportType`): Bus 0, Train 1, Taxi 2, Tram 3, Ship 4, Post 5, Helicopter 6, Airplane 7, Subway 8, Rocket 9, Work 10, Ferry 11, Bicycle 12.

## How the line tool builds a line [IL]

1. **Definitions:** `RouteToolSystem/CreateDefinitionsJob` writes one definition entity:
   - `CreationDefinition {m_Prefab = line prefab}`;
   - `ColorDefinition {m_Color}`;
   - a `WaypointDefinition {m_Position, m_Connection, m_Original}` buffer.

   For a waypoint on a stop, `m_Connection` is the stop entity: `GetWaypointDefinition` takes `ControlPoint.m_OriginalEntity` when it has a `ConnectedRoute` buffer. A closed line ends on its first waypoint again.
2. **Generation:** `GenerateRoutesSystem` (Modification) creates the route from `RouteData.m_RouteArchetype`, with colour and `TransportLine(TransportLineData)`. `GenerateWaypointsSystem` creates the waypoints and connections (`RouteData.m_WaypointArchetype` / `m_ConnectedArchetype`).
   - **Always Temp:** `GenerateRoutesSystem` adds `Temp` **unconditionally**. Unlike nets and objects, there is no `Permanent` branch (IL_0299–02A0: `AddComponent<Temp>(new Temp(m_Original, flags))`).
3. **Apply:** `ApplyRoutesSystem` (ApplyTool phase) makes Temp routes real.
   - **New routes:** `HandleTempEntitiesJob` removes `Temp`, adds `Created` / `Updated` / `Applied`, sets `Connected` on waypoints, and creates the `PathTargetMoved` events.
   - **Edits:** for an edited route, it patches the original.

## Making the apply happen from a script [IL]

- **Frame order inside the tool phase:** `ToolSystem.ToolUpdate`:
  1. calls `activeTool.Update()` directly;
  2. runs the rest of phase 17 (ToolUpdate), which includes the bridge's `BridgeToolPhaseSystem`;
  3. runs `ToolOutputSystem`, registered `UpdateAfter` in phase 17;
  4. runs `ToolOutputBarrier`.
- **What `ToolOutputSystem` runs:** it reads `ToolSystem.applyMode` (the last tool's `applyMode`) and runs either `UpdateSystem.Update(ApplyTool = 19)` (ToolApplySystem plus every `Apply*` system) or `UpdateSystem.Update(ClearTool = 18)` (`ToolClearSystem`).
- **So a script can apply like this:**
  - frame N: create the route definitions in the tool phase; generation makes the Temp route.
  - frame N+1, tool phase: set the active tool's `applyMode` to `Apply`. The setter is protected, so this goes through reflection; the tool already ran this frame, so the value holds until `ToolOutputSystem` reads it.
  - `ToolOutputSystem` then runs the vanilla ApplyTool phase, exactly as a click with the line tool.
  - The default tool has no Temp entities of its own, so nothing else gets applied.
- **Kerbside stops need no apply:** `GenerateObjectsSystem` honours `Permanent`. The definition is `CreationDefinition {m_Prefab = stop, m_Attached = edge, Permanent}` + `ObjectDefinition`, which gives `Attached(edge)` via `CreateAttached` [IL].

## Vehicles [IL]

- A line gets vehicles from a **depot** of its transport type: a building with `TransportDepot`, prefab `TransportDepotData {m_TransportType, m_VehicleCapacity, …}`.
- `TransportLine.m_VehicleRequest` asks a depot to dispatch, at `m_VehicleInterval` (default from `TransportLineData.m_DefaultVehicleInterval`).
- With no depot of the right type, the line exists but has no vehicles. Depots come with the buildings (buildings worker).
