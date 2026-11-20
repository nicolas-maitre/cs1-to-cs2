using System;
using System.Collections.Generic;
using System.IO;
using Colossal.Logging;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Modding;
using Game.Net;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.InputSystem;

namespace M0RoadSpike
{
    /// <summary>
    /// M0 spike 1/1b. In a loaded city, hold Ctrl+Shift and press (all at the ground point under the screen centre):
    ///   1  vanilla "Small Road", 120 m straight
    ///   2  every generated "*-cs1conv" Road Builder road, side by side (spike 3 check)
    ///   3  a second Small Road crossing road #1 at right angles (result: FAILS, existing edge left as Temp ghost)
    ///   4  elevated Small Road (+12 m, should become a bridge with pillars)
    ///   5  1b: X junction, 4 arms from one centre point, all created in the same frame
    ///   6  1b: X junction in two frames: west arm first, then 3 arms attached to its real end Node entity
    ///   7  1b: T junction in two frames: west arm first, then east + north arms attached to the Node
    ///   9  1b: log nodes near the last junction centre (edge count, Temp?) and count Temp edges/nodes in the world
    ///   0  log every road prefab name (RoadData) to M0RoadSpike.log
    /// Placement is permanent (CreationFlags.Permanent): no Temp entities, so the tool
    /// validation (collisions, cost) never runs, which is the same effect Anarchy achieves.
    /// </summary>
    public sealed class Mod : IMod
    {
        public static readonly ILog Log = LogManager.GetLogger(nameof(M0RoadSpike)).SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("OnLoad");
            // ToolUpdate runs before CourseSplitSystem / GenerateNodes / GenerateEdges, so the
            // new course is split at crossings with existing roads like a player-drawn road.
            updateSystem.UpdateAt<PlaceRoadSystem>(SystemUpdatePhase.ToolUpdate);
        }

        public void OnDispose() => Log.Info("OnDispose");
    }

    // Not partial, no SystemAPI / Entities.ForEach / IJobEntity → no source generators needed.
    public class PlaceRoadSystem : GameSystemBase
    {
        // Hard-coded segment, as offsets (metres) from a pivot 150 m in front of the camera.
        private static readonly float2 A = new float2(-60f, 0f);
        private static readonly float2 B = new float2(60f, 0f);
        private const float RoadBuilderSpacing = 45f;

        private PrefabSystem m_PrefabSystem;
        private TerrainSystem m_TerrainSystem;
        private EntityQuery m_RoadPrefabs;
        private EntityQuery m_RealNodes;
        private EntityQuery m_TempNodes;
        private EntityQuery m_TempEdges;
        private float2 m_LastPivot;
        private bool m_HavePivot;

        // Spike 1b phase 2: arms to attach to the node at m_JunctionCentre once it exists.
        private readonly List<float2> m_PendingArms = new List<float2>();
        private float2 m_JunctionCentre;
        private bool m_HaveJunction;
        private int m_PendingWait;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_RoadPrefabs = GetEntityQuery(ComponentType.ReadOnly<RoadData>(), ComponentType.ReadOnly<PrefabData>());
            m_RealNodes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            m_TempNodes = GetEntityQuery(ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<Temp>());
            m_TempEdges = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>());
        }

        protected override void OnUpdate()
        {
            if (GameManager.instance == null || !GameManager.instance.gameMode.IsGame()) return;
            if (m_PendingArms.Count > 0) AttachPendingArms();
            var kb = Keyboard.current;
            if (kb == null || !kb.ctrlKey.isPressed || !kb.shiftKey.isPressed) return;

            try
            {
                if (kb.digit1Key.wasPressedThisFrame)
                {
                    m_LastPivot = CameraPivot();
                    m_HavePivot = true;
                    TryPlace(new PrefabID("RoadPrefab", "Small Road"), m_LastPivot + A, m_LastPivot + B, 0f);
                }
                else if (kb.digit2Key.wasPressedThisFrame)
                {
                    var pivot = CameraPivot();
                    int i = 0;
                    foreach (var id in GeneratedRoadBuilderIds())
                    {
                        var off = new float2(0f, (i++ - 1) * RoadBuilderSpacing);
                        TryPlace(new PrefabID("RoadBuilderPrefab", id), pivot + A + off, pivot + B + off, 0f);
                    }
                    if (i == 0) Mod.Log.Warn("No *-cs1conv.json configs found in ModsData/RoadBuilder");
                }
                else if (kb.digit3Key.wasPressedThisFrame)
                {
                    var pivot = m_HavePivot ? m_LastPivot : CameraPivot();
                    TryPlace(new PrefabID("RoadPrefab", "Small Road"), pivot + new float2(0f, -60f), pivot + new float2(0f, 60f), 0f);
                }
                else if (kb.digit4Key.wasPressedThisFrame)
                {
                    var pivot = CameraPivot() + new float2(0f, 80f);
                    TryPlace(new PrefabID("RoadPrefab", "Small Road"), pivot + A, pivot + B, 12f);
                }
                else if (kb.digit5Key.wasPressedThisFrame)
                {
                    var c = StartJunction();
                    Mod.Log.Info($"1b X same-frame at {c}");
                    foreach (var arm in Arms(c, 4)) TryPlace(SmallRoad, c, arm, 0f);
                }
                else if (kb.digit6Key.wasPressedThisFrame || kb.digit7Key.wasPressedThisFrame)
                {
                    bool x = kb.digit6Key.wasPressedThisFrame;
                    var c = StartJunction();
                    var arms = Arms(c, 4);
                    Mod.Log.Info($"1b {(x ? "X" : "T")} two-frame at {c}: phase 1 west arm");
                    TryPlace(SmallRoad, arms[0], c, 0f); // ends at the centre → creates the node there
                    m_PendingArms.Clear();
                    m_PendingArms.Add(arms[1]);          // east
                    m_PendingArms.Add(arms[2]);          // north
                    if (x) m_PendingArms.Add(arms[3]);   // south
                    m_PendingWait = 0;
                }
                else if (kb.digit9Key.wasPressedThisFrame)
                {
                    InspectJunction();
                }
                else if (kb.digit0Key.wasPressedThisFrame)
                {
                    LogRoadPrefabs();
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e, "placement failed");
            }
        }

        private static readonly PrefabID SmallRoad = new PrefabID("RoadPrefab", "Small Road");

        private float2 StartJunction()
        {
            m_JunctionCentre = CameraPivot();
            m_HaveJunction = true;
            return m_JunctionCentre;
        }

        /// <summary>Arm end points 60 m from the centre: west, east, north, south.</summary>
        private static float2[] Arms(float2 c, int n) => new[]
        {
            c + new float2(-60f, 0f), c + new float2(60f, 0f), c + new float2(0f, 60f), c + new float2(0f, -60f),
        };

        /// <summary>Phase 2 of keys 6/7: wait for the real (non-Temp) node at the centre, then attach arms to it.</summary>
        private void AttachPendingArms()
        {
            if (++m_PendingWait < 3) return; // let GenerateNodes/Edges + CleanUp finish
            Entity node = FindNode(m_JunctionCentre, 1f, out Game.Net.Node data);
            if (node == Entity.Null)
            {
                if (m_PendingWait > 120) { Mod.Log.Warn("1b phase 2: no real node at centre after 120 frames, giving up"); m_PendingArms.Clear(); }
                return;
            }
            if (!m_PrefabSystem.TryGetPrefab(SmallRoad, out PrefabBase prefab) || !m_PrefabSystem.TryGetEntity(prefab, out Entity prefabEntity)) { m_PendingArms.Clear(); return; }
            TerrainHeightData heights = m_TerrainSystem.GetHeightData(true);
            Mod.Log.Info($"1b phase 2 (frame +{m_PendingWait}): node {node} at {data.m_Position}, attaching {m_PendingArms.Count} arms");
            foreach (var arm in m_PendingArms)
            {
                float3 end = new float3(arm.x, 0f, arm.y);
                end.y = TerrainUtils.SampleHeight(ref heights, end);
                CreateSegment(EntityManager, prefabEntity, data.m_Position, end, 0f, node, Entity.Null);
            }
            m_PendingArms.Clear();
        }

        private Entity FindNode(float2 at, float maxDist, out Game.Net.Node data)
        {
            data = default;
            Entity best = Entity.Null;
            float bestD = maxDist;
            using (var es = m_RealNodes.ToEntityArray(Allocator.Temp))
            using (var ns = m_RealNodes.ToComponentDataArray<Game.Net.Node>(Allocator.Temp))
            {
                for (int i = 0; i < es.Length; i++)
                {
                    float d = math.distance(ns[i].m_Position.xz, at);
                    if (d < bestD) { bestD = d; best = es[i]; data = ns[i]; }
                }
            }
            return best;
        }

        private void InspectJunction()
        {
            Mod.Log.Info($"1b inspect: Temp nodes in world {m_TempNodes.CalculateEntityCount()}, Temp edges {m_TempEdges.CalculateEntityCount()}");
            if (!m_HaveJunction) return;
            using (var es = m_RealNodes.ToEntityArray(Allocator.Temp))
            using (var ns = m_RealNodes.ToComponentDataArray<Game.Net.Node>(Allocator.Temp))
            {
                for (int i = 0; i < es.Length; i++)
                {
                    float d = math.distance(ns[i].m_Position.xz, m_JunctionCentre);
                    if (d > 8f) continue;
                    int edges = EntityManager.HasBuffer<ConnectedEdge>(es[i]) ? EntityManager.GetBuffer<ConnectedEdge>(es[i], true).Length : -1;
                    Mod.Log.Info($"  node {es[i]} {d:0.00} m from centre, connected edges {edges}");
                }
            }
        }

        /// <summary>Ground point under the screen centre (ray marched against the terrain).</summary>
        private float2 CameraPivot()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return float2.zero;
            var ray = cam.ViewportPointToRay(new UnityEngine.Vector3(0.5f, 0.5f, 0f));
            float3 o = ray.origin, dir = ray.direction;
            TerrainHeightData heights = m_TerrainSystem.GetHeightData(true);
            for (float t = 0f; t < 5000f; t += 2f)
            {
                float3 p = o + dir * t;
                if (p.y <= TerrainUtils.SampleHeight(ref heights, p)) return p.xz;
            }
            var fwd = math.normalizesafe(dir.xz, new float2(0f, 1f));
            return o.xz + fwd * 150f; // looking at the sky: fall back to 150 m ahead
        }

        private static IEnumerable<string> GeneratedRoadBuilderIds()
        {
            var dir = Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "RoadBuilder");
            if (!Directory.Exists(dir)) yield break;
            var files = Directory.GetFiles(dir, "*-cs1conv.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files) yield return Path.GetFileNameWithoutExtension(f);
        }

        private void LogRoadPrefabs()
        {
            using (var entities = m_RoadPrefabs.ToEntityArray(Allocator.Temp))
            {
                Mod.Log.Info($"{entities.Length} road prefabs:");
                foreach (var e in entities)
                {
                    var prefab = m_PrefabSystem.GetPrefab<PrefabBase>(e);
                    Mod.Log.Info($"  {prefab.GetType().Name} \"{prefab.name}\"");
                }
            }
        }

        private void TryPlace(PrefabID id, float2 a, float2 b, float elevation)
        {
            if (!m_PrefabSystem.TryGetPrefab(id, out PrefabBase prefab) || !m_PrefabSystem.TryGetEntity(prefab, out Entity prefabEntity))
            {
                Mod.Log.Warn($"Prefab not found: {id}");
                return;
            }

            TerrainHeightData heights = m_TerrainSystem.GetHeightData(true);
            float3 p0 = new float3(a.x, 0f, a.y);
            float3 p1 = new float3(b.x, 0f, b.y);
            p0.y = TerrainUtils.SampleHeight(ref heights, p0) + elevation;
            p1.y = TerrainUtils.SampleHeight(ref heights, p1) + elevation;

            CreateSegment(EntityManager, prefabEntity, p0, p1, elevation, Entity.Null, Entity.Null);
            Mod.Log.Info($"Placed {id} {p0} -> {p1} elevation {elevation}");
        }

        /// <summary>
        /// Same shape as NetToolSystem's CreateDefinitionsJob / ZoneSpawnSystem.CreateSubNet,
        /// but with CreationFlags.Permanent so GenerateEdgesSystem creates real (non-Temp)
        /// nodes and edges directly. startNode/endNode: Entity.Null = new node; an existing
        /// Game.Net.Node entity = connect to it (position must match).
        /// </summary>
        public static Entity CreateSegment(EntityManager em, Entity netPrefab, float3 start, float3 end, float elevation, Entity startNode, Entity endNode)
        {
            Bezier4x3 curve = NetUtils.StraightCurve(start, end);

            CreationDefinition def = default;
            def.m_Prefab = netPrefab;
            def.m_SubPrefab = Entity.Null;
            def.m_Original = Entity.Null;
            def.m_Owner = Entity.Null;
            def.m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue);
            def.m_Flags = CreationFlags.Permanent | CreationFlags.SubElevation;

            NetCourse course = default;
            course.m_Curve = curve;
            course.m_StartPosition = MakePos(curve, start, startNode, 0f, elevation, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
            course.m_EndPosition = MakePos(curve, end, endNode, 1f, elevation, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
            course.m_Elevation = new float2(elevation);
            course.m_Length = MathUtils.Length(curve);
            course.m_FixedIndex = -1;

            Entity e = em.CreateEntity();
            em.AddComponentData(e, def);
            em.AddComponentData(e, course);
            em.AddComponent<Updated>(e);
            em.AddComponent<Deleted>(e); // definition entity is cleaned up at end of frame, like ZoneSpawnSystem's
            return e;
        }

        private static CoursePos MakePos(Bezier4x3 curve, float3 pos, Entity node, float t, float elevation, CoursePosFlags flags)
        {
            CoursePos p = default;
            p.m_Entity = node;
            p.m_SplitPosition = 0f;
            p.m_Position = pos;
            p.m_Elevation = new float2(elevation);
            p.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t));
            p.m_CourseDelta = t;
            p.m_ParentMesh = -1;
            p.m_Flags = flags;
            return p;
        }
    }
}
