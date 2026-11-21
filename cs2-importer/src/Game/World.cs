// Game-side helpers: prefab lookup, terrain heights, definition entities, node lookup and edge inspection.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Cs2Importer.Core;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Importer
{
    internal sealed class GameWorld
    {
        public readonly World World;
        public readonly EntityManager Em;
        public readonly PrefabSystem Prefabs;
        readonly TerrainSystem m_Terrain;
        TerrainHeightData m_Height;
        bool m_HeightValid;

        public GameWorld()
        {
            World = World.DefaultGameObjectInjectionWorld;
            if (World == null) throw new InvalidOperationException("no ECS world");
            Em = World.EntityManager;
            Prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_Terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
        }

        // ---------------------------------------------------------------- prefabs

        public sealed class PrefabInfo
        {
            public PlanPrefab Plan;
            public Entity Entity;
            public bool Found;
            public string ActualType;
            public double Width = double.NaN, ElevationLimit = double.NaN;
        }

        public PrefabInfo LookupPrefab(PlanPrefab p)
        {
            var info = new PrefabInfo { Plan = p };
            if (string.IsNullOrEmpty(p.Name)) return info;
            PrefabBase prefab;
            Entity e;
            if (Prefabs.TryGetPrefab(new PrefabID(p.Type ?? "RoadPrefab", p.Name), out prefab) && Prefabs.TryGetEntity(prefab, out e) && Em.HasComponent<NetGeometryData>(e))
            {
                info.Found = true;
                info.Entity = e;
                info.ActualType = prefab.GetType().Name;
                var g = Em.GetComponentData<NetGeometryData>(e);
                info.Width = Math.Round(g.m_DefaultWidth, 2);
                info.ElevationLimit = Math.Round(g.m_ElevationLimit, 2);
            }
            return info;
        }

        // ---------------------------------------------------------------- terrain

        public void RefreshTerrain()
        {
            m_Height = m_Terrain.GetHeightData(true);
            m_HeightValid = m_Height.isCreated;
        }

        public float TerrainHeight(double x, double z)
        {
            if (!m_HeightValid) RefreshTerrain();
            if (!m_HeightValid) return 0f;
            return TerrainUtils.SampleHeight(ref m_Height, new float3((float)x, 0f, (float)z));
        }

        // ---------------------------------------------------------------- definitions

        /// <summary>
        /// One Permanent road definition (Spike 1 recipe, NOTES-cs2-placement.md §1.5): GenerateNodesSystem and
        /// GenerateEdgesSystem build it in the same frame when created in ToolUpdate. startNode/endNode = Entity.Null makes
        /// a new node at that exact position (definitions in the same frame that share a position share the node);
        /// a real node entity attaches to it.
        /// </summary>
        /// startPos/endPos: the node positions when the curve ends off its nodes (Node Controller shift, offNode): the
        /// course's CoursePos.m_Position is the node, the curve end stays where it is (roads have no StrictNodes, so
        /// GenerateEdge keeps the course curve's end points). Default: the curve ends.
        public Entity CreateSegment(Entity prefab, V3[] c, Entity startNode, Entity endNode, float elevStart, float elevEnd, float elevMiddle, V3? startPos = null, V3? endPos = null)
        {
            var curve = new Bezier4x3(F3(c[0]), F3(c[1]), F3(c[2]), F3(c[3]));
            CreationDefinition def = default;
            def.m_Prefab = prefab;
            def.m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue);
            def.m_Flags = CreationFlags.Permanent | CreationFlags.SubElevation;
            NetCourse course = default;
            course.m_Curve = curve;
            course.m_StartPosition = Pos(curve, startPos.HasValue ? F3(startPos.Value) : curve.a, startNode, 0f, elevStart, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
            course.m_EndPosition = Pos(curve, endPos.HasValue ? F3(endPos.Value) : curve.d, endNode, 1f, elevEnd, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
            course.m_Elevation = new float2(elevMiddle);
            course.m_Length = MathUtils.Length(curve);
            course.m_FixedIndex = -1;
            Entity e = Em.CreateEntity();
            Em.AddComponentData(e, def);
            Em.AddComponentData(e, course);
            Em.AddComponent<Updated>(e);
            Em.AddComponent<Deleted>(e); // the definition itself is cleaned up at the end of the frame
            return e;
        }

        static CoursePos Pos(Bezier4x3 curve, float3 pos, Entity node, float t, float elevation, CoursePosFlags flags)
        {
            CoursePos p = default;
            p.m_Entity = node;
            p.m_Position = pos;
            p.m_Elevation = new float2(elevation);
            p.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t));
            p.m_CourseDelta = t;
            p.m_ParentMesh = -1;
            p.m_Flags = flags;
            return p;
        }

        public static float3 F3(V3 v) { return new float3((float)v.X, (float)v.Y, (float)v.Z); }

        // ---------------------------------------------------------------- node lookup

        /// <summary>Spatial hash of the real (not Temp, not Deleted) nodes, rebuilt on demand.</summary>
        public sealed class NodeIndex
        {
            const float Cell = 8f;
            readonly Dictionary<long, List<KeyValuePair<Entity, float3>>> m_Cells = new Dictionary<long, List<KeyValuePair<Entity, float3>>>();
            public int Count;

            static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

            public void Add(Entity e, float3 p)
            {
                long k = Key((int)math.floor(p.x / Cell), (int)math.floor(p.z / Cell));
                List<KeyValuePair<Entity, float3>> l;
                if (!m_Cells.TryGetValue(k, out l)) m_Cells[k] = l = new List<KeyValuePair<Entity, float3>>();
                l.Add(new KeyValuePair<Entity, float3>(e, p));
                Count++;
            }

            /// <summary>
            /// Node within maxXZ metres horizontally and maxDY vertically; the best one scores horizontal distance first and
            /// vertical distance as a tie-breaker (stacked nodes at one x/z, e.g. a bridge over a road end).
            /// </summary>
            public Entity Find(float3 p, float maxXZ, float maxDY, out float3 found)
            {
                found = default;
                Entity best = Entity.Null;
                float bestD = float.MaxValue;
                int cx = (int)math.floor(p.x / Cell), cz = (int)math.floor(p.z / Cell);
                for (int x = cx - 1; x <= cx + 1; x++)
                    for (int z = cz - 1; z <= cz + 1; z++)
                    {
                        List<KeyValuePair<Entity, float3>> l;
                        if (!m_Cells.TryGetValue(Key(x, z), out l)) continue;
                        foreach (var kv in l)
                        {
                            float dy = math.abs(kv.Value.y - p.y);
                            if (dy > maxDY) continue;
                            float dxz = math.distancesq(kv.Value.xz, p.xz);
                            if (dxz > maxXZ * maxXZ) continue;
                            float d = dxz + 0.01f * dy * dy;
                            if (d < bestD) { bestD = d; best = kv.Key; found = kv.Value; }
                        }
                    }
                return best;
            }
        }

        public NodeIndex IndexRealNodes()
        {
            var idx = new NodeIndex();
            using (var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var ns = q.ToComponentDataArray<Node>(Allocator.Temp))
                for (int i = 0; i < es.Length; i++) idx.Add(es[i], ns[i].m_Position);
            return idx;
        }

        public bool IsReal(Entity e)
        {
            return e != Entity.Null && Em.Exists(e) && !Em.HasComponent<Temp>(e) && !Em.HasComponent<Deleted>(e);
        }

        /// <summary>
        /// The node whose real edges end at the given curve end points (x/z within `tol`), by votes; Entity.Null when none
        /// does. For offNode (Node Controller) ends: CS2 keeps every edge's curve end exactly but moves the node itself
        /// (NOTES-lane-alignment.md §3), so the edges find the node where positions don't. Scans all edges: used only
        /// when the position lookups fail.
        /// </summary>
        public Entity NodeByEdgeEnds(List<float3> ends, float tol, out float3 at)
        {
            at = default;
            var votes = new Dictionary<Entity, int>();
            using (var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var ed = q.ToComponentDataArray<Edge>(Allocator.Temp))
            using (var cv = q.ToComponentDataArray<Curve>(Allocator.Temp))
                for (int i = 0; i < ed.Length; i++)
                    foreach (var p in ends)
                    {
                        Entity n = Entity.Null;
                        if (math.distance(cv[i].m_Bezier.a.xz, p.xz) <= tol) n = ed[i].m_Start;
                        else if (math.distance(cv[i].m_Bezier.d.xz, p.xz) <= tol) n = ed[i].m_End;
                        if (n == Entity.Null || !IsReal(n)) continue;
                        int v; votes.TryGetValue(n, out v); votes[n] = v + 1;
                    }
            Entity best = Entity.Null; int bv = 0;
            foreach (var kv in votes) if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
            if (best != Entity.Null) at = Em.GetComponentData<Node>(best).m_Position;
            return best;
        }

        /// <summary>Real edges connected to a node.</summary>
        public List<Entity> EdgesOf(Entity node)
        {
            var r = new List<Entity>();
            if (!IsReal(node) || !Em.HasBuffer<ConnectedEdge>(node)) return r;
            var buf = Em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < buf.Length; i++) if (IsReal(buf[i].m_Edge) && Em.HasComponent<Edge>(buf[i].m_Edge)) r.Add(buf[i].m_Edge);
            return r;
        }

        public Entity OtherEnd(Entity edge, Entity node)
        {
            var e = Em.GetComponentData<Edge>(edge);
            return e.m_Start == node ? e.m_End : e.m_Start;
        }

        public CompositionFlags.General EdgeGeneralFlags(Entity edge)
        {
            if (!Em.HasComponent<Composition>(edge)) return 0;
            var c = Em.GetComponentData<Composition>(edge);
            if (c.m_Edge == Entity.Null || !Em.HasComponent<NetCompositionData>(c.m_Edge)) return 0;
            return Em.GetComponentData<NetCompositionData>(c.m_Edge).m_Flags.m_General;
        }

        /// <summary>Left | right side flags of an edge's composition (Raised = embankment wall, Lowered = cutting wall).</summary>
        public CompositionFlags.Side EdgeSideFlags(Entity edge)
        {
            if (!Em.HasComponent<Composition>(edge)) return 0;
            var c = Em.GetComponentData<Composition>(edge);
            if (c.m_Edge == Entity.Null || !Em.HasComponent<NetCompositionData>(c.m_Edge)) return 0;
            var f = Em.GetComponentData<NetCompositionData>(c.m_Edge).m_Flags;
            return f.m_Left | f.m_Right;
        }

        public int Count(params ComponentType[] types)
        {
            using (var q = Em.CreateEntityQuery(types)) return q.CalculateEntityCount();
        }

        public Dictionary<string, object> TempCounts()
        {
            return new Dictionary<string, object>
            {
                { "temp_nodes", Count(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Temp>()) },
                { "temp_edges", Count(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>()) },
                { "temp_any", Count(ComponentType.ReadOnly<Temp>()) },
            };
        }

        // ---------------------------------------------------------------- edges in an area

        /// <summary>Real top-level edges (no Owner: building sub-nets are left alone) that have a point inside the box.</summary>
        public List<Entity> EdgesInBox(double x0, double z0, double x1, double z1, out int ownedSkipped)
        {
            var r = new List<Entity>();
            ownedSkipped = 0;
            using (var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var cs = q.ToComponentDataArray<Curve>(Allocator.Temp))
            {
                for (int i = 0; i < es.Length; i++)
                {
                    var b = cs[i].m_Bezier;
                    int steps = math.max(2, (int)math.ceil(cs[i].m_Length / 10f));
                    bool inside = false;
                    for (int s = 0; s <= steps && !inside; s++)
                    {
                        var p = MathUtils.Position(b, (float)s / steps);
                        inside = p.x >= x0 && p.x <= x1 && p.z >= z0 && p.z <= z1;
                    }
                    if (!inside) continue;
                    if (Em.HasComponent<Owner>(es[i])) { ownedSkipped++; continue; }
                    r.Add(es[i]);
                }
            }
            return r;
        }

        /// <summary>Marks an entity and its sub-objects Deleted (Road Builder's pattern for removing nets).</summary>
        public void Delete(Entity e)
        {
            if (!IsReal(e)) return;
            var subs = new List<Entity>();
            if (Em.HasBuffer<Game.Objects.SubObject>(e))
            {
                var buf = Em.GetBuffer<Game.Objects.SubObject>(e, true);
                for (int i = 0; i < buf.Length; i++) subs.Add(buf[i].m_SubObject);
            }
            Em.AddComponent<Deleted>(e);
            foreach (var s in subs) if (IsReal(s)) Em.AddComponent<Deleted>(s);
        }

        public static string Id(Entity e) { return e.Index + ":" + e.Version; }
    }
}
