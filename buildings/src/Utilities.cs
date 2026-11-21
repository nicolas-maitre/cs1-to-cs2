// Op "utilities" (DECISIONS.md #115, buildings decision #40): the player's own off-road CS1 power lines and pipes
// (plan.utilities, web 0.8.0+, schema §2) built as CS2 nets with the Permanent course recipe, after the buildings.
//
//   utilities {plan, offsetX, offsetZ, kinds="power,pipe", powerPrefab="Low-voltage Line",
//              pipePrefab="Combined Small Pipe", snapRadius=40, batchSize=200}   [phase tool, pump tool]
//
// - Segments that share a CS1 node are created in the same frame with identical end positions, so CS2 gives them one
//   node (the importer's recipe, cs2-importer/src/Game/World.cs CreateSegment).
// - Each line end (a CS1 node used by one segment) snaps within snapRadius, in this order:
//   1. a building's connection marker: a lone node owned by the building, with the marker net's prefab ("Low-voltage
//      Marker" on plants and transformers, "Small Water Marker" on pumps, "Small Sewage Marker" on outlets, "Small
//      Combined Marker" on treatment plants); the course attaches to it;
//   2. otherwise a road node (roads take low-voltage lines and pipes through NetData.m_LocalConnectLayers): the end is
//      put there and keeps its own depth, and CS2 connects it locally; or a node of another line of the same kind.
//   The layer test is the net's m_ConnectLayers against the node's layers. An inner node (shared by two segments)
//   snaps only to a marker, when one is within snapRadius: a CS1 line that runs through the building.
// - CS1 has one pipe type for water and sewage: "Combined Small Pipe" (10 m underground, the prefab's range). CS1 power
//   lines feed off-road consumers from the city (pumps, treatment plants): "Low-voltage Line", which connects to roads
//   and to the plants' low-voltage markers. A high-voltage line would need a transformer at each end; powerPrefab
//   chooses another prefab.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using GameBridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Buildings
{
    public static class Utilities
    {
        sealed class Seg
        {
            public long Id, Start, End;
            public string Utility, Cs1Info, Kind;
            public double[][] Bezier;
            public double ElevStart, ElevEnd;
        }

        // Kind: 0 = a building's connection marker (a node with no edges, owned by the building: attach to it),
        //       1 = a road node (the road takes power lines and pipes through m_LocalConnectLayers: put the end there, CS2
        //           connects them locally; the pipe stays at its depth), 2 = a node of another line of the same kind (attach).
        sealed class SnapNode { public Entity Entity; public float3 Position; public Layer Layers; public int Kind; }

        public static object Place(Dictionary<string, object> a) { return Routine(a); }

        static IEnumerator Routine(Dictionary<string, object> a)
        {
            var sw = Stopwatch.StartNew();
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var heights = world.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(true);
            float ox = (float)P.Num(a, "offsetX", 0), oz = (float)P.Num(a, "offsetZ", 0);
            float snapRadius = (float)P.Num(a, "snapRadius", 40);
            int batchSize = (int)P.Num(a, "batchSize", 200);
            var kinds = new HashSet<string>(P.Str(a, "kinds", "power,pipe").Split(','));
            var prefabNames = new Dictionary<string, KeyValuePair<string, string>>
            {
                { "power", new KeyValuePair<string, string>("PowerLinePrefab", P.Str(a, "powerPrefab", "Low-voltage Line")) },
                { "pipe", new KeyValuePair<string, string>("PipelinePrefab", P.Str(a, "pipePrefab", "Combined Small Pipe")) },
            };

            var segs = Load(P.Required(a, "plan"));
            if (segs == null) { yield return new Dictionary<string, object> { { "skipped", "no utilities section in this package (web 0.8.0+)" } }; yield break; }

            // Prefabs and what they may connect to.
            var prefab = new Dictionary<string, Entity>();
            var connect = new Dictionary<string, Layer>();
            var elevation = new Dictionary<string, float>();
            var missing = new List<object>();
            foreach (var kv in prefabNames)
            {
                PrefabBase pb;
                Entity pe;
                if (!kinds.Contains(kv.Key)) continue;
                if (!ps.TryGetPrefab(new PrefabID(kv.Value.Key, kv.Value.Value), out pb) || !ps.TryGetEntity(pb, out pe)) { missing.Add(kv.Value.Value); continue; }
                prefab[kv.Key] = pe;
                connect[kv.Key] = em.HasComponent<NetData>(pe) ? em.GetComponentData<NetData>(pe).m_ConnectLayers : (Layer)0;
                // The elevation the net tool would use: 0 if allowed, else the nearest end of the prefab's range (pipes).
                float el = 0f;
                if (em.HasComponent<PlaceableNetData>(pe))
                {
                    var r = em.GetComponentData<PlaceableNetData>(pe).m_ElevationRange;
                    el = math.clamp(0f, r.min, r.max);
                }
                elevation[kv.Key] = el;
            }

            var nodes = SnapNodes(em);
            var grid = new Dictionary<long, List<SnapNode>>();
            const float Cell = 32f;
            Func<float, float, long> key = (x, z) => ((long)(int)math.floor(x / Cell) << 32) ^ (uint)(int)math.floor(z / Cell);
            foreach (var n in nodes) { long k = key(n.Position.x, n.Position.z); List<SnapNode> l; if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<SnapNode>(); l.Add(n); }

            // How many plan segments of the same utility use each CS1 node: ends used once are the line's ends.
            var uses = new Dictionary<string, int>();
            foreach (var s in segs) { foreach (var id in new[] { s.Start, s.End }) { var k = s.Utility + ":" + id; int c; uses.TryGetValue(k, out c); uses[k] = c + 1; } }

            int created = 0, skippedKind = 0, frames = 0;
            var snapped = new Dictionary<string, int>();
            int ends = 0, endsSnapped = 0, innerToMarker = 0;
            var decided = new Dictionary<string, SnapNode>();
            var notSnapped = new List<object>();
            var byUtility = new Dictionary<string, int>();
            var rnd = new Unity.Mathematics.Random(0x5eed1234u);
            int inBatch = 0;
            foreach (var s in segs)
            {
                Entity pe;
                if (!kinds.Contains(s.Utility) || !prefab.TryGetValue(s.Utility, out pe)) { skippedKind++; continue; }
                var b = new float3[4];
                for (int i = 0; i < 4; i++)
                {
                    b[i] = new float3((float)s.Bezier[i][0] + ox, 0f, (float)s.Bezier[i][2] + oz);
                    b[i].y = TerrainUtils.SampleHeight(ref heights, b[i]) + elevation[s.Utility];
                }
                Entity startNode = Entity.Null, endNode = Entity.Null;
                // A line end (a CS1 node used by one segment) snaps to a marker, a road or a line node; an inner node only to
                // a marker (a CS1 line running through the building, as at CacaoCity's treatment plant). One decision per
                // CS1 node, so every segment at an inner node moves the same way.
                for (int side = 0; side < 2; side++)
                {
                    long id = side == 0 ? s.Start : s.End;
                    string nk = s.Utility + ":" + id;
                    bool isEnd = uses[nk] == 1;
                    var p = side == 0 ? b[0] : b[3];
                    SnapNode best;
                    if (!decided.TryGetValue(nk, out best))
                    {
                        // The nearest marker within the radius wins over any closer road or line node: CS1's line ended at
                        // that building. Otherwise (ends only) the nearest road or line node.
                        SnapNode bestMarker = null, bestOther = null;
                        float dMarker = snapRadius, dOther = snapRadius;
                        for (int gx = -2; gx <= 2; gx++)
                            for (int gz = -2; gz <= 2; gz++)
                            {
                                List<SnapNode> l;
                                if (!grid.TryGetValue(key(p.x + gx * Cell, p.z + gz * Cell), out l)) continue;
                                foreach (var n in l)
                                {
                                    if ((n.Layers & connect[s.Utility]) == 0) continue;
                                    float d = math.distance(n.Position.xz, p.xz);
                                    if (n.Kind == 0) { if (d < dMarker) { dMarker = d; bestMarker = n; } }
                                    else if (d < dOther) { dOther = d; bestOther = n; }
                                }
                            }
                        best = bestMarker ?? (isEnd ? bestOther : null);
                        decided[nk] = best;
                        if (isEnd) ends++;
                        if (best != null)
                        {
                            if (isEnd) endsSnapped++; else innerToMarker++;
                            var tag = (best.Kind == 0 ? "marker " : best.Kind == 1 ? "road " : "line ") + LayerName(best.Layers & connect[s.Utility]) + (isEnd ? "" : " (inner node)");
                            int c0; snapped.TryGetValue(tag, out c0); snapped[tag] = c0 + 1;
                        }
                        else if (isEnd && notSnapped.Count < 20) notSnapped.Add(new Dictionary<string, object> { { "segment", s.Id }, { "utility", s.Utility }, { "x", R(p.x) }, { "z", R(p.z) } });
                    }
                    if (best == null) continue;
                    // Move the end onto the node and its inner control point with it (keeps the tangent).
                    // A road node: only the position (xz; the end keeps its own height and depth), no attachment.
                    var target = best.Position;
                    if (best.Kind == 1) target.y = p.y;
                    var node = best.Kind == 1 ? Entity.Null : best.Entity;
                    if (side == 0) { var dlt = target - b[0]; b[0] += dlt; b[1] += dlt; startNode = node; }
                    else { var dlt = target - b[3]; b[3] += dlt; b[2] += dlt; endNode = node; }
                }
                CreateCourse(em, pe, new Bezier4x3(b[0], b[1], b[2], b[3]), startNode, endNode, elevation[s.Utility], ref rnd);
                created++;
                int cu; byUtility.TryGetValue(s.Utility, out cu); byUtility[s.Utility] = cu + 1;
                if (++inBatch >= batchSize) { inBatch = 0; yield return null; frames++; }
            }
            for (int i = 0; i < 3; i++) { yield return null; frames++; }

            yield return new Dictionary<string, object>
            {
                { "items", segs.Count }, { "placed", created }, { "byUtility", ToObj(byUtility) }, { "skippedKind", skippedKind },
                { "prefabs", PrefabList(prefabNames, kinds) }, { "prefabsMissing", missing }, { "elevation", ToObjF(elevation) },
                { "ends", ends }, { "endsSnapped", endsSnapped }, { "innerNodesToMarkers", innerToMarker }, { "snappedTo", ToObj(snapped) }, { "notSnappedSample", notSnapped },
                { "snapRadius", snapRadius }, { "snapNodes", nodes.Count }, { "frames", frames }, { "seconds", Math.Round(sw.Elapsed.TotalSeconds, 2) },
            };
        }

        static void CreateCourse(EntityManager em, Entity prefab, Bezier4x3 curve, Entity startNode, Entity endNode, float elevation, ref Unity.Mathematics.Random rnd)
        {
            var e = em.CreateEntity();
            em.AddComponentData(e, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.Permanent | CreationFlags.SubElevation });
            em.AddComponentData(e, new NetCourse
            {
                m_Curve = curve,
                m_StartPosition = Pos(curve, curve.a, startNode, 0f, elevation, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight),
                m_EndPosition = Pos(curve, curve.d, endNode, 1f, elevation, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight),
                m_Elevation = new float2(elevation),
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = -1,
            });
            em.AddComponent<Updated>(e);
            em.AddComponent<Deleted>(e);
        }

        static CoursePos Pos(Bezier4x3 curve, float3 pos, Entity node, float t, float elevation, CoursePosFlags flags)
        {
            return new CoursePos
            {
                m_Entity = node, m_Position = pos, m_Elevation = new float2(elevation),
                m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t)), m_CourseDelta = t, m_ParentMesh = -1, m_Flags = flags,
            };
        }

        /// <summary>Real nodes with the layers of their connected edges (what a new net end may attach to).</summary>
        static List<SnapNode> SnapNodes(EntityManager em)
        {
            var list = new List<SnapNode>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var n in es)
                {
                    Layer layers = 0;
                    int kind = 2;
                    int edgeCount = em.HasBuffer<ConnectedEdge>(n) ? em.GetBuffer<ConnectedEdge>(n, true).Length : 0;
                    if (edgeCount == 0)
                    {
                        // A building's connection marker ("High-voltage Marker", "Small Water Marker", …): a lone node
                        // owned by the building, with the marker net's prefab.
                        var pr = em.GetComponentData<PrefabRef>(n).m_Prefab;
                        if (!em.HasComponent<Game.Common.Owner>(n) || !em.HasComponent<NetData>(pr)) continue;
                        layers = em.GetComponentData<NetData>(pr).m_RequiredLayers;
                        kind = 0;
                    }
                    else
                    {
                        var edges = em.GetBuffer<ConnectedEdge>(n, true);
                        for (int i = 0; i < edges.Length; i++)
                        {
                            var pr = em.HasComponent<PrefabRef>(edges[i].m_Edge) ? em.GetComponentData<PrefabRef>(edges[i].m_Edge).m_Prefab : Entity.Null;
                            if (pr == Entity.Null || !em.HasComponent<NetData>(pr)) continue;
                            var nd = em.GetComponentData<NetData>(pr);
                            if ((nd.m_RequiredLayers & Layer.Road) != 0) { kind = 1; layers |= nd.m_LocalConnectLayers; }
                            else layers |= nd.m_RequiredLayers;
                        }
                        // Building-owned road paths (Invisible …) are not the road network.
                        if (kind == 1 && em.HasComponent<Game.Common.Owner>(n)) continue;
                    }
                    if (layers == 0) continue;
                    list.Add(new SnapNode { Entity = n, Position = em.GetComponentData<Node>(n).m_Position, Layers = layers, Kind = kind });
                }
            }
            return list;
        }

        static string LayerName(Layer l)
        {
            var s = l.ToString();
            return s.Length > 60 ? s.Substring(0, 60) : s;
        }

        static List<Seg> Load(string path)
        {
            path = Environment.ExpandEnvironmentVariables(path);
            if (Directory.Exists(path)) path = Path.Combine(path, "cs1conv-plan.json");
            var root = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            object v;
            if (root == null || !root.TryGetValue("utilities", out v) || !(v is Dictionary<string, object>)) return null;
            var arr = ((Dictionary<string, object>)v).TryGetValue("segments", out v) ? v as List<object> : null;
            var list = new List<Seg>();
            if (arr == null) return list;
            foreach (var o in arr)
            {
                var d = o as Dictionary<string, object>;
                var bz = d != null && d.TryGetValue("bezier", out v) ? v as List<object> : null;
                if (bz == null || bz.Count != 4) continue;
                var pts = new double[4][];
                for (int i = 0; i < 4; i++)
                {
                    var p = bz[i] as List<object>;
                    pts[i] = new[] { Convert.ToDouble(p[0]), Convert.ToDouble(p[1]), Convert.ToDouble(p[2]) };
                }
                list.Add(new Seg
                {
                    Id = (long)P.Num(d, "id", 0), Start = (long)P.Num(d, "start", 0), End = (long)P.Num(d, "end", 0),
                    Utility = P.Str(d, "utility", null), Cs1Info = P.Str(d, "cs1Info", null), Kind = P.Str(d, "kind", "ground"),
                    Bezier = pts, ElevStart = P.Num(d, "elevationStart", 0), ElevEnd = P.Num(d, "elevationEnd", 0),
                });
            }
            return list;
        }

        static List<object> PrefabList(Dictionary<string, KeyValuePair<string, string>> names, HashSet<string> kinds)
        {
            var l = new List<object>();
            foreach (var kv in names) if (kinds.Contains(kv.Key)) l.Add(kv.Key + " → " + kv.Value.Value);
            return l;
        }

        static Dictionary<string, object> ToObj(Dictionary<string, int> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = kv.Value; return o; }
        static Dictionary<string, object> ToObjF(Dictionary<string, float> d) { var o = new Dictionary<string, object>(); foreach (var kv in d) o[kv.Key] = R(kv.Value); return o; }
        static double R(float f) { return Math.Round(f, 2); }
    }
}
