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
    /// M0 spike 1. In a loaded city, hold Ctrl+Shift and press:
    ///   1  vanilla "Small Road", 120 m straight, ~150 m in front of the camera
    ///   2  every generated "*-cs1conv" Road Builder road, side by side (spike 3 check)
    ///   3  a second Small Road crossing road #1 at right angles (do crossings become intersections?)
    ///   4  elevated Small Road (+12 m, should become a bridge with pillars)
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
        private float2 m_LastPivot;
        private bool m_HavePivot;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_TerrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            m_RoadPrefabs = GetEntityQuery(ComponentType.ReadOnly<RoadData>(), ComponentType.ReadOnly<PrefabData>());
        }

        protected override void OnUpdate()
        {
            if (GameManager.instance == null || !GameManager.instance.gameMode.IsGame()) return;
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

        private static float2 CameraPivot()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return float2.zero;
            var p = cam.transform.position;
            var f = cam.transform.forward;
            var fwd = math.normalizesafe(new float2(f.x, f.z), new float2(0f, 1f));
            return new float2(p.x, p.z) + fwd * 150f;
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
