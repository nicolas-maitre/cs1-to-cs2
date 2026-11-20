# CS2: placing a road segment from code via the NetTool definition pipeline

Research notes, 2026-11-20. Sources:
- Repos under `scratchpad/src/`: Anarchy @22880bc, CS2-MoveIt @6bde569, RoadBuilder-CSII @5f4b1ab, Traffic.
- The installed game's `Game.dll` and friends (`E:\GamesLibrarySteam\steamapps\common\Cities Skylines II\Cities2_Data\Managed`). I read them with a throwaway System.Reflection.Metadata dumper and IL disassembler in `scratchpad/tools/meta`:
  - `dotnet scratchpad/tools/meta/out/meta.dll <dll> type|il|refs|types ...`
  - Statements marked **[IL]** were verified against the current Game.dll, not guessed.
- The ps1ke docs pages came back almost entirely as navigation sidebar through WebFetch (no member docs in the readable text), so nothing below relies on them.

A compile-checked spike mod is in `scratchpad/tools/RoadSpike/` (`Mod.cs` + `RoadSpike.csproj`). `dotnet build -c Release` succeeds against the game DLLs, which proves every type and member name used in the snippets exists. It has not been run in-game yet.

---

## 0. TL;DR

1. **Definition entity.** Create an entity with these components:
   - `CreationDefinition { m_Prefab = <road prefab entity>, m_Flags = CreationFlags.Permanent (| SubElevation) }`
   - `NetCourse { m_Curve, m_StartPosition(IsFirst), m_EndPosition(IsLast), m_Elevation, m_Length = MathUtils.Length(curve), m_FixedIndex = -1 }`
   - `Updated`, plus optionally `Deleted`.

   You can create it from any `GameSystemBase`; no tool needs to be active. `GenerateNodesSystem` (Modification1) and `GenerateEdgesSystem` (Modification2) turn it into real nodes and edges.
2. **Permanent means no Temp.** With `CreationFlags.Permanent`, `GenerateEdgesSystem` does not add `Temp` [IL]. The result is real permanent nets. There is no `ValidationSystem` pass (it only queries `Temp`) [IL], no `ApplyNetSystem`, and no cost is charged. So collision errors are moot: nothing validates the result.
3. **Vanilla precedent.** The game itself does exactly this outside any tool. `ZoneSpawnSystem` and `BuildingConstructionSystem` call `CreateSubNet` to create Permanent `CreationDefinition` + `NetCourse` definitions for building sub-nets [IL].
4. **Prefab lookup.** Use `prefabSystem.TryGetPrefab(new PrefabID("RoadPrefab", "Small Road"), out PrefabBase p)`, then `prefabSystem.GetEntity(p)`. Road Builder roads use `new PrefabID("RoadBuilderPrefab", <config ID>)`. The config ID is the JSON file name in `%LocalLow%\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\`, for example `r162d122a-…-<user-id>`.
5. **Hotkey.** For the spike, poll `UnityEngine.InputSystem.Keyboard.current.f9Key.wasPressedThisFrame` in `OnUpdate`. Anarchy and Traffic both use `Keyboard.current` in-game.
6. **Build and deploy.**
   - Target `net48` (the official toolchain's `Mod.props` says so). Reference the DLLs in the Managed folder with `Private=false`.
   - No post-processor or Burst step is needed for managed-only code. Also avoid `SystemAPI`, `partial` systems, `Entities.ForEach` and `IJobEntity`, because they need the Unity.Entities source generators.
   - Deploy to `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\<ModName>\<ModName>.dll`. This is `CSII_LOCALMODSPATH = kUserDataPath\Mods` [IL], and the `Mods` folder does not exist yet on this machine.

---

## 1. The recipe

### 1.1 Types (verified [IL] in current Game.dll)

```
Game.Tools.CreationDefinition : IComponentData
  Entity m_Prefab, m_SubPrefab, m_Original, m_Owner, m_Attached; CreationFlags m_Flags; int m_RandomSeed
Game.Tools.CreationFlags : uint
  Permanent=1 Select=2 Delete=4 Attach=8 Upgrade=16 Relocate=32 Invert=64 Align=128 Hidden=256 Parent=512
  Dragging=1024 Recreate=2048 Optional=4096 Lowered=8192 Native=16384 Construction=32768
  SubElevation=65536 Duplicate=131072 Repair=262144 Stamping=524288
Game.Tools.NetCourse : IComponentData
  CoursePos m_StartPosition, m_EndPosition; Bezier4x3 m_Curve; float2 m_Elevation; float m_Length; int m_FixedIndex
Game.Tools.CoursePos (plain struct)
  Entity m_Entity; float3 m_Position; quaternion m_Rotation; float2 m_Elevation; float m_CourseDelta;
  float m_SplitPosition; CoursePosFlags m_Flags; int m_ParentMesh
Game.Tools.CoursePosFlags : uint
  IsFirst=1 IsLast=2 HalfAlign=4 IsParallel=8 IsRight=16 IsLeft=32 IsFixed=64 FreeHeight=128
  LeftTransition=256 RightTransition=512 ForceElevatedNode=1024 ForceElevatedEdge=2048
  DisableMerge=4096 IsGrid=8192 DontCreate=16384
Game.Tools.Temp : IComponentData { Entity m_Original; float m_CurvePosition; int m_Value; int m_Cost; TempFlags m_Flags }
```

### 1.2 What vanilla NetToolSystem does for a straight segment [IL]

From `Game.Tools.NetToolSystem/CreateDefinitionsJob::CreateStraightLine`:

- `CreationDefinition`: `m_Prefab = m_NetPrefab`, `m_SubPrefab = m_LanePrefab`, `m_RandomSeed = random.NextInt()`, `m_Flags |= SubElevation (65536)`. There is no Permanent flag: the tool always makes Temp previews, and `ApplyNetSystem` later converts them when you click.
- `NetCourse`: `m_Curve = NetUtils.StraightCurve(start.m_Position, end.m_Position)`.
- `m_StartPosition = GetCoursePos(curve, startCP, 0f)` with `|= IsFirst`, and `m_EndPosition = GetCoursePos(curve, endCP, 1f)` with `|= IsLast`. If start and end are identical, both get both flags.
- With no parallel roads, both ends also get `|= IsLeft (32) | IsRight (16)`.
- `m_Length = MathUtils.Length(curve)` and `m_FixedIndex = -1`.
- The entity gets `CreationDefinition`, `Updated`, optionally `ColorVariationDefinition`, `OwnerDefinition` and `LocalCurveCache`, and `NetCourse`. All of this goes through an ECB from `ToolOutputBarrier`.

`GetCoursePos(curve, ControlPoint cp, float delta)` [IL]:

- `m_Entity`:
  - cp is on an Edge with t <= 0: `edge.m_Start`, and `m_SplitPosition = 0`.
  - cp is on an Edge with t >= 1: `edge.m_End`, and `m_SplitPosition = 1`.
  - cp is mid-edge: the edge entity itself, and `m_SplitPosition = t` (this splits the edge).
  - cp is on a Node: the node entity.
  - Otherwise: `Entity.Null`, and a new node is created.
- `m_Position = cp.m_Position`, `m_Elevation = (float2)cp.m_Elevation`.
- `m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, delta))`, `m_CourseDelta = delta`.
- `m_ParentMesh = cp.m_ElementIndex.x`. This is -1 for terrain hits; the remaining code only handles building-owned parents.

Traffic carries a simplified copy of this:

- `src/Traffic/Code/Tools/Helpers/ToolHelpers.cs:36-49` (`GetCoursePos`).
- Its use in `src/Traffic/Code/Tools/LaneConnectorToolSystem.CreateDefinitionsJob.cs:886-917` (`CreateNodeDefinition`) builds a zero-length course on an existing node:

```csharp
CreationDefinition nodeDef = new CreationDefinition() { m_Flags = 0, m_Original = node, m_Prefab = prefabRefData[node].m_Prefab };
NetCourse netCourse = default(NetCourse);
netCourse.m_Curve = new Bezier4x3(point.m_Position, point.m_Position, point.m_Position, point.m_Position);
netCourse.m_StartPosition = ToolHelpers.GetCoursePos(netCourse.m_Curve, point, 0f);
netCourse.m_StartPosition.m_Flags |= (CoursePosFlags.IsFirst);
netCourse.m_StartPosition.m_ParentMesh = -1;
netCourse.m_EndPosition = ToolHelpers.GetCoursePos(netCourse.m_Curve, point, 1f);
netCourse.m_EndPosition.m_Flags |= (CoursePosFlags.IsLast);
netCourse.m_EndPosition.m_ParentMesh = -1;
netCourse.m_Length = MathUtils.Length(netCourse.m_Curve);
netCourse.m_FixedIndex = -1;
Entity nodeEntity = commandBuffer.CreateEntity();
commandBuffer.AddComponent(nodeEntity, nodeDef);
commandBuffer.AddComponent(nodeEntity, netCourse);
commandBuffer.AddComponent<Updated>(nodeEntity);
```

That is a Temp definition (`m_Flags = 0`) created inside a ToolBaseSystem job. It is the only CreationDefinition + NetCourse creation in the four repos.

### 1.3 Vanilla precedent for Permanent definitions, outside any tool [IL]

`Game.Simulation.ZoneSpawnSystem/SpawnBuildingJob::CreateSubNet` (and the identical `BuildingConstructionSystem/BuildingConstructionJob::CreateSubNet`) is simulation code, not tool code:

```
e = ECB.CreateEntity()
CreationDefinition def = default; def.m_Prefab = netPrefab; def.m_RandomSeed = random.NextInt(); def.m_Flags |= Permanent(1)
AddComponent(e, def); AddComponent<Updated>(e); AddComponent(e, ownerDefinition)
NetCourse c = default;
c.m_Curve = ObjectUtils.LocalToWorld(ownerPos, ownerRot, localCurve)
c.m_StartPosition.m_Position = c.m_Curve.a; .m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(c.m_Curve), ownerRot)
c.m_StartPosition.m_CourseDelta = 0; .m_Elevation = (float2)localCurve.a.y; .m_ParentMesh = parentMesh.x
c.m_EndPosition  ... same with .d / EndTangent / CourseDelta = 1 / localCurve.d.y / parentMesh.y
c.m_Length = MathUtils.Length(c.m_Curve); c.m_FixedIndex = -1
c.m_StartPosition.m_Flags |= IsFirst|DisableMerge (4097); c.m_EndPosition.m_Flags |= IsLast|DisableMerge (4098)
if start==end position: start |= IsLast, end |= IsFirst
AddComponent(e, c); if (upgradeFlags != default) AddComponent(e, new Upgraded{...})
```

For its main building definition, `ZoneSpawnSystem` uses the archetype `CreationDefinition + ObjectDefinition + Updated + Deleted` (`ZoneSpawnSystem::OnCreate`, `m_DefinitionArchetype`). A definition can carry `Deleted` from birth because the Generate* queries do not exclude `Deleted` (see 1.4), and `CleanUpSystem` (phase `Cleanup`) destroys the entity at the end of the frame.

### 1.4 What consumes the definition [IL]

Phases are from `Game.Common.SystemOrder::Initialize`.

| System | Phase | Query |
|---|---|---|
| `CourseSplitSystem` | PostTool (16) | All{CreationDefinition, NetCourse, Updated}. Splits courses where they cross existing nets and creates intersections. Not gated on the active tool. |
| `GenerateNodesSystem` | Modification1 | All{CreationDefinition, Updated} Any{NetCourse, ObjectDefinition}. No `Deleted` exclusion. |
| `GenerateEdgesSystem` | Modification2 | All{CreationDefinition, NetCourse, Updated}. No `Deleted` exclusion. |
| `ValidationSystem` | ModificationEnd | All{**Temp**, Updated} None{Deleted,…}. Never sees Permanent nets. |
| `ApplyNetSystem` | ApplyTool (19) | Temp → permanent conversion. Not involved for Permanent. |

In `GenerateEdgesJob::GenerateEdge` [IL]:

```
IL_0000: isPermanent = (definition.m_Flags & 1) != 0   // stloc.0
...
IL_0FCD: ldloc.0; brtrue.s END; AddComponent<Game.Tools.Temp>(edge, temp)   // Temp only when NOT permanent
```

`TryGetNode(CoursePos, bool isPermanent, …)` and `FindNodeConnections(…, bool isPermanent, …)` show the same handling on the node side.

The frame order is MainLoop: `ToolSystem` (PreTool → ToolUpdate → PostTool [IL `ToolSystem::OnUpdate`]), then `ModificationSystem` (Modification1 … ModificationEnd), and later `CleanUpSystem`. A definition created in ToolUpdate is therefore split in PostTool and turned into nets in the same frame.

**Tool active or not?** No tool is needed. Any `GameSystemBase` can create the entity with `EntityManager` from the main thread (a sync point; fine on a hotkey).

Choosing the phase:
- **ToolUpdate.** Our system runs before `CourseSplitSystem`, so crossings with existing roads get proper intersections. This is the recommended default.
- **`UpdateBefore<X, GenerateNodesSystem>(Modification1)`.** This skips CourseSplit. Anarchy registers its `NetworkDefinitionSystem` this way to edit definitions (`src/Anarchy/Anarchy/AnarchyMod.cs:148`). The road would then pass through existing roads without intersections, which is "anarchy" overlap.

**Cleanup without `Deleted`.** If you leave `Deleted` off, active tools destroy stale definitions:
- `ToolBaseSystem.GetDefinitionQuery()` = All{CreationDefinition} None{Updated}, destroyed by `DestroyDefinitions`.
- Called from `DefaultToolSystem.UpdateDefinitions`, `NetToolSystem.Update`, and others [IL].

Adding `Deleted` the way `ZoneSpawnSystem` does is cleaner. If the road ever fails to appear, try without `Deleted` first.

### 1.5 Recommended code (compile-checked: `scratchpad/tools/RoadSpike/Mod.cs`)

```csharp
public static Entity CreateSegment(EntityManager em, Entity netPrefab, float3 start, float3 end,
                                   float elevation, Entity startNode, Entity endNode)
{
    Bezier4x3 curve = NetUtils.StraightCurve(start, end);          // or NetUtils.FitCurve(...) for curves

    CreationDefinition def = default;
    def.m_Prefab   = netPrefab;               // road prefab ENTITY (PrefabSystem.GetEntity)
    def.m_SubPrefab = Entity.Null;            // lane prefab; only for lane-type tools
    def.m_Original = Entity.Null;             // existing edge to replace/upgrade; Null = new
    def.m_Owner    = Entity.Null;             // owner object (building) — Null for free roads
    def.m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue);
    def.m_Flags = CreationFlags.Permanent | CreationFlags.SubElevation;  // SubElevation: what NetTool sets

    NetCourse course = default;
    course.m_Curve = curve;
    course.m_StartPosition = MakePos(curve, start, startNode, 0f, elevation,
        CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
    course.m_EndPosition = MakePos(curve, end, endNode, 1f, elevation,
        CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
    course.m_Elevation = new float2(elevation);   // -> Game.Net.Elevation on the edge if != 0
    course.m_Length = MathUtils.Length(curve);
    course.m_FixedIndex = -1;

    Entity e = em.CreateEntity();
    em.AddComponentData(e, def);
    em.AddComponentData(e, course);
    em.AddComponent<Updated>(e);
    em.AddComponent<Deleted>(e);   // ZoneSpawnSystem pattern; CleanUpSystem removes it end-of-frame
    return e;
}

static CoursePos MakePos(Bezier4x3 curve, float3 pos, Entity node, float t, float elevation, CoursePosFlags flags)
{
    CoursePos p = default;
    p.m_Entity = node;              // Null = new node; existing Node entity = connect (pos must match node pos);
                                    // existing Edge entity + m_SplitPosition = t => split that edge there
    p.m_SplitPosition = 0f;
    p.m_Position = pos;             // world position incl. y (terrain height + elevation)
    p.m_Elevation = new float2(elevation);
    p.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t));
    p.m_CourseDelta = t;            // 0 at start, 1 at end
    p.m_ParentMesh = -1;            // no parent building mesh
    p.m_Flags = flags;
    return p;
}
```

Terrain height comes from `TerrainHeightData h = terrainSystem.GetHeightData(true); y = TerrainUtils.SampleHeight(ref h, pos);` (both [IL]-verified).

Caveats to check in-game:
- No money is deducted and there is no undo. Cost lives in `Temp.m_Cost` and is only applied by the Temp → Apply flow.
- A ground road with a straight Bezier between two terrain points does not hug terrain. The road's terrain modification cuts and fills to match it.
- Colliding objects such as trees and props may still be handled by `Game.Objects.OverrideSystem` / `Game.Net.OverrideSystem` (Modification5). These run on updated entities regardless of Temp. I did not fully verify their queries.

**Alternative (Temp flow).** Omit `Permanent` and the edge gets `Temp`, so you see a preview with validation errors. To commit it you would also need the tool's apply step (`ToolSystem` apply mode / `ApplyNetSystem`). That is more complex and only worth it if you want the vanilla cost and validation behaviour.

### 1.6 Move It

Move It does **not** use definitions. It mutates existing net components in place:
- `src/CS2-MoveIt/Code/MoveIt/QAccessor/QEntity/QSegment.cs:71-75`: `Segment_UpdateCurve` writes `Curve.m_Bezier`.
- `QSegment.cs:26-43`: `Segment_SetUpdated` tags the edge, both nodes and the aggregate.
- `src/CS2-MoveIt/Code/MoveIt/QAccessor/QEntity/Utils.cs:23-33`: tagging adds `Game.Common.Updated` and `BatchesUpdated`.

The game's Net systems then recompute geometry. There is no validation step and it does not touch ValidationSystem. Its only CreationDefinition reference is a debug printer (`Code/MoveIt/Tool/Debug.cs:69`).

---

## 2. Prefab lookup

The API [IL] is:
- `PrefabSystem.TryGetPrefab(PrefabID id, out PrefabBase prefab)`
- `PrefabSystem.GetEntity(PrefabBase)` / `TryGetEntity(PrefabBase, out Entity)`
- `PrefabSystem.GetPrefabName(Entity)`

`PrefabID(string type, string name, Hash128 hash = default)`. The 2-argument form compiles: Anarchy uses `new PrefabID("NotificationIconPrefab", "Already Exists")` at `src/Anarchy/Anarchy/Systems/ErrorChecks/DisableToolErrorsSystem.cs:77`.

`PrefabID(PrefabBase)` sets `m_Type = prefab.GetType().Name` and `m_Name = prefab.name` [IL `PrefabID::.ctor`]. Note that `Equals` also compares `m_Hash` [IL]. The hash is only set when the prefab's asset meta carries a platform ID (PDX-Mods assets). Vanilla built-ins and Road Builder runtime prefabs match with the default hash.

```csharp
if (prefabSystem.TryGetPrefab(new PrefabID("RoadPrefab", "Small Road"), out PrefabBase p))
    Entity roadPrefabEntity = prefabSystem.GetEntity(p);
```

There is a vanilla example at `src/RoadBuilder-CSII/RoadBuilder/Systems/RoadBuilderSerializeSystem.cs:196`:
`prefabSystem.TryGetSpecificPrefab(new PrefabID(nameof(RoadPrefab), "Small Road"), out var smallRoad) ? prefabSystem.GetEntity(smallRoad) : Entity.Null`.
`TryGetSpecificPrefab` is RoadBuilder's own extension, `Utilities/Extensions.cs:257`, which wraps `TryGetPrefab`.

**Vanilla road names.**
- Confirmed in source: `"Small Road"` (RoadBuilder `RoadBuilderSerializeSystem.cs:196`), `"Pedestrian Street"` (RoadBuilder `NetworkConfigGenerationUtil.cs:166`), and the track `"Double Train Track"` (`TrackPrefab`, `RoadBuilderSerializeSystem.cs:197`).
- I could not extract the full list offline, because prefab names live in the game's `.cok` content blobs.
- To enumerate at runtime, query entities with `Game.Prefabs.RoadData` + `PrefabData` and log `prefabSystem.GetPrefabName(e)`. Likely names such as "Medium Road", "Large Road" and "Highway" must be confirmed this way.

**Road Builder prefabs.**
- `RoadBuilderPrefab : RoadPrefab` (`Domain/Prefabs/RoadBuilderPrefab.cs:12`), so the type string is `"RoadBuilderPrefab"`. The others are `TrackBuilderPrefab`, `PathBuilderPrefab` and `FenceBuilderPrefab`.
- The prefab name is the config ID:
  - `NetworkPrefabGenerationUtil.cs:43`: `prefab.Prefab.name = config?.ID`
  - `RoadBuilderSystem.cs:352-354`: `roadPrefab.Prefab.name = roadPrefab.Config.ID; prefabSystem.AddPrefab(roadPrefab.Prefab)`
- New IDs look like `$"{typeName.ToLower()[0]}{Guid.NewGuid()}-{userSpecificPath}"` (`NetworkPrefabGenerationUtil.cs:125`), giving `r<guid>-<steamid>` for roads.
- Configs are saved as `<ID>.json` in `EnvPath.kUserDataPath\ModsData\RoadBuilder` (`Utilities/FoldersUtil.cs:16`, `LocalSaveUtil.cs:24`). This machine already has around 10, for example `r162d122a-6a12-4279-8376-78a7fe19074b-<user-id>` ("one way street no parking").
- All local configs are registered once at startup by `RoadBuilderInitializerSystem.OnUpdate` → `LocalSaveUtil.LoadConfigs()` → `roadBuilderSystem.AddPrefab(config)` (`Systems/RoadBuilderInitializerSystem.cs:37-48`).
- So: `prefabSystem.TryGetPrefab(new PrefabID("RoadBuilderPrefab", id), out var p)`. Fallback: scan `RoadData` prefab entities comparing `GetPrefabName(e) == id`.
- Edges placed from code are still tagged by RB's `RoadBuilderApplyTagSystem`. Its query is Edge + PrefabRef, Any{Created, Updated}, None{RoadBuilderNetwork, Temp} (`Systems/RoadBuilderApplyTagSystem.cs:30-34`), so permanent edges qualify.

---

## 3. Hotkey

**Simplest, for the spike:** poll the Unity Input System device in your system's `OnUpdate`. A ToolUpdate system updates every frame, including while paused.

```csharp
var kb = UnityEngine.InputSystem.Keyboard.current;      // reference Unity.InputSystem.dll
if (kb != null && kb.f9Key.wasPressedThisFrame) { ... }
```

In-game precedent:
- `src/Anarchy/Anarchy/Systems/Common/SelectMultiplePrefabsUISystem.cs:251` (`Keyboard.current.leftCtrlKey.isPressed`)
- `src/Traffic/Code/Tools/LaneConnectorToolSystem.cs:415`

Downside: it fires even while typing in UI text fields and is not rebindable. Guard with `GameManager.instance.gameMode.IsGame()`.

**Proper way (later):** use `ModSetting` with key-binding attributes. See `src/Anarchy/Anarchy/Settings/AnarchyModSettings.cs:23-31,287-288`:
- `[SettingsUIKeyboardAction(name, ActionType.Button, usages)]` on the class
- `[SettingsUIKeyboardBinding(BindingKeyboard.A, actionName: name, ctrl: true)] public ProxyBinding X { get; set; }`
- then `Settings.RegisterKeyBindings()` (`AnarchyMod.cs:127`)
- `ProxyAction a = Settings.GetAction(name); a.shouldBeEnabled = true; if (a.WasPerformedThisFrame())` (`AnarchyUISystem.cs:301,366,403`)

This needs settings registration and persistence. Persistence (`AssetDatabase.global.LoadSettings`) changed signature recently. This machine's Modding.log shows C2VM failing with `MissingMethodException … AssetDatabase.LoadSettings(string,object,object)`. Skip it for the spike.

---

## 4. Collision and validation errors

**How Anarchy does it.** Anarchy keeps the Temp flow and turns off *error types*, not validation itself:
- `DisableToolErrorsSystem` (`src/Anarchy/Anarchy/Systems/ErrorChecks/DisableToolErrorsSystem.cs:94-113`) runs at Modification5 (`AnarchyMod.cs:133`), before `ValidationSystem` at ModificationEnd. Whenever a tool is active, it:
  - queries prefab entities with `ToolErrorData` + `NotificationIconData`;
  - for each `ErrorType` in the allowed list, ORs `ToolErrorFlags.DisableInGame | DisableInEditor` into `ToolErrorData.m_Flags`.
- `ValidationSystem` reads those flags through `m_ToolErrorPrefabQuery` (All{NotificationIconData, ToolErrorData}) [IL], so it ignores those errors and `ApplyNetSystem` allows the apply.
- `EnableToolErrorsSystem` (same folder, lines 74-110) runs at ModificationEnd (`AnarchyMod.cs:134`) and clears the flags again.
- The error list is in `Systems/Common/AnarchyUISystem.cs:40-66`. It includes OverlapExisting, InvalidShape, SteepSlope, TightCurve, ShortDistance, LongDistance, LowElevation, InWater, ExceedsCityLimits and NotEnoughClearance. Access errors (NoCarAccess and so on) are "Never".

Other Anarchy pieces:
- **Clearance.** `ModifyNetCompositionDataSystem` (Modification3) zeroes or shrinks `NetCompositionData.m_HeightRange` while Anarchy is active. `ResetNetCompositionDataSystem` restores it.
- **Override prevention.** This is for *objects*: `PreventOverrideSystem`, `RemoveOverridenSystem` and `AnarchyPlopSystem` remove `Overridden` from entities carrying Anarchy's `PreventOverride` component.
- **Harmony.** Anarchy uses it only for UI and raycast tweaks (`Patches/NetToolSystem_InitializeRaycast.cs` etc.), not for validation.

**For our code path:**
- With `CreationFlags.Permanent`, the generated edges and nodes never get `Temp`, so `ValidationSystem` never inspects them [IL query].
- Overlap, slope and curve errors are therefore not raised at all. No Anarchy-style flag toggling is needed.
- What remains is geometric side effects: crossing existing roads gets split by `CourseSplitSystem` if created before PostTool, and colliding trees and props may be overridden.

**If we ever use the Temp flow instead:** copy Anarchy's approach. A system at Modification5 sets `DisableInGame` on all `ToolErrorData` prefabs, and a system at ModificationEnd restores them.

**Move It:** no validation at all. It edits components directly and tags them `Updated`/`BatchesUpdated` (section 1.6).

---

## 5. Elevation, bridges and tunnels

- `NetCourse.m_Elevation` (float2) is copied into a `Game.Net.Elevation(m_Elevation)` component on the edge when `any(m_Elevation != 0)` [IL `GenerateEdge` IL_0613…IL_0942]. `CoursePos.m_Elevation` (float2) feeds node elevation (`GenerateNodesSystem/UpdateData::.ctor` [IL]).
- In NetTool, `CoursePos.m_Elevation = (float2)ControlPoint.m_Elevation`, the height above ground set by the elevation up/down keys (`NetUtils.DEFAULT_ELEVATION_STEP = 10`).
- The curve's y must be the *world* height (terrain + elevation).
- The composition is chosen by `NetCompositionHelpers.GetElevationFlags(startElev, middleElev, endElev, NetGeometryData)` [IL], with `L = NetGeometryData.m_ElevationLimit` of the prefab:

| Condition | Result |
|---|---|
| all of `max(cmin(start), cmin(end))` and `middle` >= `2*L` (or the prefab has `GeometryFlags.RequireElevated`) | `CompositionFlags.General.Elevated` (65536): bridge with pillars. Prefabs with `ElevatedIsRaised` get the side `Raised` flag instead. |
| `cmax(...) <= -2*L` and `cmin(...) <= -3*L` | `General.Tunnel` (131072) |
| in between | side Raised/Lowered (quay or retaining wall) |

- This matches Anarchy's constants for roads (`NetworkDefinitionSystem.cs:26-30`): ElevatedThreshold 8, TunnelThreshold -12, Quay 4, RetainingWall -4, which implies `L = 4`. Anarchy forces these by editing `NetCourse.m_Elevation`, `m_StartPosition.m_Elevation` and `m_EndPosition.m_Elevation` on Temp definitions (`CheckAndSetElevations`, lines 267-293).
- There are no separate "elevated prefab variants" for roads. The same `RoadPrefab` gets an elevated or tunnel composition from `Elevation`.
- `PlaceableNetData.m_UndergroundPrefab` and `NetGeometryData.m_ElevationRange` exist for nets that swap prefab underground (for example metro/train), and are worth reading per prefab.
- Useful CoursePos flags:
  - `FreeHeight` (128): Anarchy sets it when overriding y.
  - `ForceElevatedNode` (1024) and `ForceElevatedEdge` (2048).
  - `DisableMerge` (4096): used by building sub-nets.
- For a bridge spanning terrain, set elevation >= `2*L` at both ends and in the middle. Use a curve whose y follows terrain + elevation at the ends, and keep `m_Elevation` uniform. For a tunnel, use a negative elevation <= `-3*L` (y below terrain).

---

## 6. Scaffolding without the official toolchain

**What the official toolchain does.** It is shipped in the game folder: `Cities2_Data\Content\Game\.ModdingToolchain\Mod.props` and `Mod.targets`.
- `Mod.props`: `TargetFramework net48`, `LangVersion 9.0`, `OutputType Library`. It adds `$(ManagedPath)` to AssemblySearchPaths and references `$(MSCORLIBPath)` with `Private=false`. It adds Unity.Entities source generators as Analyzers from a Unity mod project's PackageCache.
- `Mod.targets`: after build it runs `ModPostProcessor PostProcess <dll> -u <UnityModProject> …`, which does Burst/IL post-processing for `[BurstCompile]` jobs. That step is what produces the `<Mod>_win_x86_64.dll` Burst libraries seen in Modding.log. It then copies `$(OutDir)\**` to `DeployDir = $(LocalModsPath)\$(TargetName)`.
- `CSII_LOCALMODSPATH` is set by the game to `Path.Combine(EnvPath.kUserDataPath, "Mods")` [IL `IToolchainDependency::get_envVars`], i.e. `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods`.
- That folder does not exist yet here; create it.
- `.cache\Mods\local` in `mod_directory.json` is the PDX SDK's own "local" source and is a different folder. If a DLL in `Mods\<Name>\` does not show in `Logs\Modding.log` ("Loaded RoadSpike, Version=…"), try that folder second.

**What we can skip (managed-only mod):**
- **ModPostProcessor / Burst.** Not needed if we have no `[BurstCompile]` jobs.
- **Entities source generators.** Not needed if we avoid `SystemAPI.*`, `partial` system codegen, `Entities.ForEach`, `IJobEntity` and `IAspect`. Plain `GameSystemBase` + `EntityManager` + `GetEntityQuery(...)` works. Most Anarchy systems are this style, for example `DisableToolErrorsSystem`.

**Minimal csproj.** Verified building with dotnet SDK 9 here (`scratchpad/tools/RoadSpike/RoadSpike.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <OutputType>Library</OutputType>
    <AssemblyName>RoadSpike</AssemblyName>
    <NoStdLib>true</NoStdLib>                                      <!-- compile against the game's Mono BCL -->
    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
    <ManagedPath Condition="'$(ManagedPath)'==''">E:\GamesLibrarySteam\steamapps\common\Cities Skylines II\Cities2_Data\Managed</ManagedPath>
    <LocalModsPath>$(USERPROFILE)\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods</LocalModsPath>
    <DeployToGame Condition="'$(DeployToGame)'==''">false</DeployToGame>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="$(ManagedPath)\mscorlib.dll" Private="false" />
    <Reference Include="$(ManagedPath)\netstandard.dll" Private="false" />
    <Reference Include="$(ManagedPath)\System.dll" Private="false" />
    <Reference Include="$(ManagedPath)\System.Core.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Game.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Colossal.Core.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Colossal.Logging.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Colossal.Mathematics.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Colossal.Collections.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Unity.Entities.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Unity.Mathematics.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Unity.Collections.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Unity.Burst.dll" Private="false" />
    <Reference Include="$(ManagedPath)\Unity.InputSystem.dll" Private="false" />
    <Reference Include="$(ManagedPath)\UnityEngine.CoreModule.dll" Private="false" />
  </ItemGroup>
  <Target Name="DeployLocalMod" AfterTargets="Build" Condition="'$(DeployToGame)'=='true'">
    <MakeDir Directories="$(LocalModsPath)\$(AssemblyName)" />
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(LocalModsPath)\$(AssemblyName)" />
  </Target>
</Project>
```

Notes on the csproj:
- There is no .NET Framework 4.8 targeting pack on this machine. A first attempt with `AutomaticallyUseReferenceAssemblyPackages=false` failed with MSB3644. With the default (auto) setting, the SDK restored `Microsoft.NETFramework.ReferenceAssemblies.net48` from NuGet (now in `~/.nuget/packages`) just to satisfy `GetReferenceAssemblyPaths`. Compilation still uses the game's `mscorlib.dll` because of `NoStdLib`.
- Build and deploy with `dotnet build -c Release -p:DeployToGame=true`.
- Add `0Harmony.dll` (copy local) only if you later patch with Harmony. Anarchy ships its own (`pdx_mods/74604_39/0Harmony.dll`, v2.2.2.0).

**IMod and system registration** [IL: `Game.Modding.IMod { void OnLoad(UpdateSystem); void OnDispose(); }`]:

```csharp
public sealed class Mod : IMod
{
    public static readonly ILog Log = LogManager.GetLogger("RoadSpike").SetShowsErrorsInUI(false);
    public void OnLoad(UpdateSystem updateSystem)
    {
        updateSystem.UpdateAt<PlaceRoadSystem>(SystemUpdatePhase.ToolUpdate);
        // alternatives: updateSystem.UpdateAt<T>(SystemUpdatePhase.Modification1);
        //               updateSystem.UpdateBefore<T, GenerateNodesSystem>(SystemUpdatePhase.Modification1);
    }
    public void OnDispose() { }
}

public class PlaceRoadSystem : GameSystemBase   // not partial, no SystemAPI
{
    PrefabSystem m_PrefabSystem; TerrainSystem m_TerrainSystem;
    protected override void OnCreate() { base.OnCreate();
        m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
        m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>(); }
    protected override void OnUpdate() {
        if (!GameManager.instance.gameMode.IsGame()) return;
        var kb = Keyboard.current;
        if (kb != null && kb.f9Key.wasPressedThisFrame) TryPlace(new PrefabID("RoadPrefab", "Small Road"), ...);
        if (kb != null && kb.f10Key.wasPressedThisFrame) TryPlace(new PrefabID("RoadBuilderPrefab", "<config id>"), ...);
    }
}
```

`SystemUpdatePhase` values [IL]:
- MainLoop=0, LateUpdate=1, Modification1=2, Modification2=3, Modification2B=4, Modification3=5, Modification4=6, Modification4B=7, Modification5=8, ModificationEnd=9
- PreSimulation=10, PostSimulation=11, GameSimulation=12, EditorSimulation=13, Rendering=14
- PreTool=15, PostTool=16, ToolUpdate=17, ClearTool=18, ApplyTool=19
- Serialize=20, Deserialize=21, UIUpdate=22, UITooltip=23, PrefabUpdate=24, …, Cleanup=31

Real registrations for reference: RoadBuilder `Mod.cs:63-79` (ToolUpdate, Modification1/2/3, UIUpdate, PrefabUpdate) and Anarchy `AnarchyMod.cs:132-155`.

Logs go to `%LocalLow%\Colossal Order\Cities Skylines II\Logs\RoadSpike.log`. Mod loading is reported in `Logs\Modding.log`.

---

## 7. Open points to verify in-game

1. Does the F9 segment appear?
   - If not, remove `Deleted` from the definition first.
   - Then try registering at `UpdateBefore<PlaceRoadSystem, GenerateNodesSystem>(Modification1)`.
2. Are crossings with existing roads split into intersections (ToolUpdate registration)? Are colliding trees and buildings removed or overridden?
3. Does the local mod load from `...\Cities Skylines II\Mods\RoadSpike\` (check Modding.log)? If not, try `.cache\Mods\local\RoadSpike\`.
4. Full vanilla road prefab name list: dump `RoadData` prefab names once.
5. Bridge test: elevation >= `2*m_ElevationLimit` (likely 8 m for roads) at both ends. Check that pillars are generated.
