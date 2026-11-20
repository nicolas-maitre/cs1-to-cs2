// M0 spike 1b (explicit junctions) and the spike 3 width checks, as one hot-loaded exec library for the CS2 bridge.
// Entry point: M0Junctions.Ops.Run(args), dispatched on args["op"]. run.mjs drives the sequences through gamectl.
//
// Ops (all coordinates are world metres; y is sampled from the terrain):
//   find     {x,z,search=3000,step=200,need=150,max=5}  free spots near (x,z): no nets/buildings within `need`, flat
//   scan     {x,z,r=150}                                what is around (x,z)
//   x1       {x,z,arm=60,temp=false}   [phase tool]     X junction: 4 arms from the centre, all in the same frame
//                                                       (temp=true: Temp definitions, the tool's preview path)
//   arm      {x,z,arm=60}              [phase tool]     west arm ending at the centre (creates the centre node)
//   attach   {x,z,arm=60,arms="ens"}   [phase tool]     arms from the real node at the centre (CoursePos.m_Entity = node)
//   apply    {}                        [phase tool]     run the ApplyTool phase once (Temp → permanent, as a click does)
//   tempx    {x,z,apply=false,frames=12} [phase tool]   Temp X definitions + per-frame timeline (apply in the first
//                                                       frame Temp nodes exist)
//   grid     {x,z,n=6,cell=60,prefabA="Small Road",prefabB="Medium Road"} [phase tool]
//                                                       n×n-cell street grid, every segment in the same frame
//   gridcheck {x,z,n,cell,frames=0}                     edge count at every grid point vs expected
//   inspect  {x,z,r=70,frames=0}                        evidence: nodes/edges near the centre, Temp counts (coroutine)
//   rb       {x,z,spacing=45,length=120} [phase tool]   place every *-cs1conv Road Builder road, parallel, along +x
//   rbwidth  {x,z,r=200,frames=0}                       measured width of every edge near (x,z) vs its prefab width,
//                                                       plus live / Overridden (hidden) trees under it
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace M0Junctions
{
    public static class Ops
    {
        static readonly PrefabID SmallRoad = new PrefabID("RoadPrefab", "Small Road");

        public static object Run(Dictionary<string, object> a)
        {
            string op = Str(a, "op", "scan");
            switch (op)
            {
                case "find": return Find(a);
                case "scan": return Scan(F(a, "x"), F(a, "z"), F(a, "r", 150f));
                case "x1": return X1(a);
                case "arm": return Arm(a);
                case "attach": return Attach(a);
                case "apply": return Apply();
                case "tempx": return TempX(a);
                case "grid": return Grid(a);
                case "gridcheck": return Delay((int)F(a, "frames", 0f), () => GridCheck(a));
                case "inspect": return Delay((int)F(a, "frames", 0f), () => Inspect(F(a, "x"), F(a, "z"), F(a, "r", 70f)));
                case "rb": return PlaceRb(a);
                case "rbwidth": return Delay((int)F(a, "frames", 0f), () => RbWidth(F(a, "x"), F(a, "z"), F(a, "r", 200f)));
                default: throw new ArgumentException("unknown op " + op);
            }
        }

        // ---------------------------------------------------------------- placement

        static object X1(Dictionary<string, object> a)
        {
            float2 c = new float2(F(a, "x"), F(a, "z"));
            float arm = F(a, "arm", 60f);
            bool temp = B(a, "temp");
            Entity prefab = Prefab(SmallRoad);
            var defs = new List<object>();
            foreach (var end in Arms(c, arm, "wens"))
                defs.Add(Id(CreateSegment(prefab, Ground(c), Ground(end), Entity.Null, !temp)));
            return new Dictionary<string, object> { { "op", "x1" }, { "temp", temp }, { "definitions", defs }, { "frame", UnityEngine.Time.frameCount } };
        }

        static object Arm(Dictionary<string, object> a)
        {
            float2 c = new float2(F(a, "x"), F(a, "z"));
            float2 w = Arms(c, F(a, "arm", 60f), "w")[0];
            var def = CreateSegment(Prefab(SmallRoad), Ground(w), Ground(c), Entity.Null, true); // ends at the centre
            return new Dictionary<string, object> { { "op", "arm" }, { "definition", Id(def) }, { "frame", UnityEngine.Time.frameCount } };
        }

        static object Attach(Dictionary<string, object> a)
        {
            float2 c = new float2(F(a, "x"), F(a, "z"));
            Entity node = FindRealNode(c, 1f, out Node data);
            if (node == Entity.Null) throw new InvalidOperationException("no real node within 1 m of the centre");
            Entity prefab = Prefab(SmallRoad);
            var defs = new List<object>();
            foreach (var end in Arms(c, F(a, "arm", 60f), Str(a, "arms", "ens")))
                defs.Add(Id(CreateSegment(prefab, data.m_Position, Ground(end), node, true)));
            return new Dictionary<string, object>
            {
                { "op", "attach" }, { "node", Id(node) }, { "node_pos", V(data.m_Position) }, { "definitions", defs },
                { "frame", UnityEngine.Time.frameCount },
            };
        }

        /// <summary>Runs the ApplyTool phase once, which is what ToolOutputSystem does when the active tool's applyMode is Apply.</summary>
        static object Apply()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            int tempBefore = Count(ComponentType.ReadOnly<Temp>());
            w.GetOrCreateSystemManaged<UpdateSystem>().Update(SystemUpdatePhase.ApplyTool);
            return new Dictionary<string, object> { { "op", "apply" }, { "temp_entities_before", tempBefore }, { "frame", UnityEngine.Time.frameCount } };
        }

        /// <summary>
        /// Temp variant with a per-frame timeline: Temp X definitions now (tool phase), then every following frame
        /// (main-loop pump) record Temp/real counts near the centre; with apply=true run the ApplyTool phase in the
        /// first frame Temp nodes exist.
        /// </summary>
        static IEnumerator TempX(Dictionary<string, object> a)
        {
            float x = F(a, "x"), z = F(a, "z");
            bool apply = B(a, "apply");
            int frames = (int)F(a, "frames", 12f);
            X1(new Dictionary<string, object> { { "x", (double)x }, { "z", (double)z }, { "temp", true } });
            var timeline = new List<object>();
            bool applied = false;
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                var ins = (Dictionary<string, object>)Inspect(x, z, 70f);
                var area = (Dictionary<string, object>)ins["area"];
                var row = new Dictionary<string, object> { { "frame", UnityEngine.Time.frameCount }, { "area", area } };
                if (apply && !applied && (int)area["nodes_temp"] > 0)
                {
                    row["apply"] = Apply();
                    applied = true;
                }
                timeline.Add(row);
            }
            yield return new Dictionary<string, object> { { "op", "tempx" }, { "apply", apply }, { "applied", applied }, { "timeline", timeline } };
        }

        /// <summary>
        /// Scale check: an n×n-cell street grid, every segment created in the same frame with Entity.Null ends at
        /// identical grid points. Rows alternate between prefabA and prefabB, columns use prefabA, so every interior
        /// node joins two prefabs.
        /// </summary>
        static object Grid(Dictionary<string, object> a)
        {
            float x0 = F(a, "x"), z0 = F(a, "z"), cell = F(a, "cell", 60f);
            int n = (int)F(a, "n", 6f);
            Entity pa = Prefab(new PrefabID("RoadPrefab", Str(a, "prefabA", "Small Road")));
            Entity pb = Prefab(new PrefabID("RoadPrefab", Str(a, "prefabB", "Medium Road")));
            int count = 0;
            for (int i = 0; i <= n; i++)
                for (int j = 0; j < n; j++)
                {
                    // row i along x (alternating prefab), column i along z
                    CreateSegment(i % 2 == 0 ? pa : pb, Ground(new float2(x0 + j * cell, z0 + i * cell)), Ground(new float2(x0 + (j + 1) * cell, z0 + i * cell)), Entity.Null, true);
                    CreateSegment(pa, Ground(new float2(x0 + i * cell, z0 + j * cell)), Ground(new float2(x0 + i * cell, z0 + (j + 1) * cell)), Entity.Null, true);
                    count += 2;
                }
            return new Dictionary<string, object> { { "op", "grid" }, { "segments", count }, { "frame", UnityEngine.Time.frameCount } };
        }

        static object GridCheck(Dictionary<string, object> a)
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            float x0 = F(a, "x"), z0 = F(a, "z"), cell = F(a, "cell", 60f);
            int n = (int)F(a, "n", 6f);
            var hist = new Dictionary<string, object>();
            int expectedOk = 0, wrong = 0, missing = 0, extraNodes = 0, temp = 0;
            var problems = new List<object>();
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    float2 p = new float2(x0 + j * cell, z0 + i * cell);
                    int expect = (i == 0 || i == n ? 1 : 2) + (j == 0 || j == n ? 1 : 2);
                    int nodesHere = 0, edges = -1;
                    using (var q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Node>() }, None = new[] { ComponentType.ReadOnly<Deleted>() } }))
                    using (var es = q.ToEntityArray(Allocator.Temp))
                    using (var ns = q.ToComponentDataArray<Node>(Allocator.Temp))
                        for (int k = 0; k < es.Length; k++)
                        {
                            if (math.distance(ns[k].m_Position.xz, p) > 2f) continue;
                            nodesHere++;
                            if (em.HasComponent<Temp>(es[k])) temp++;
                            edges = em.HasBuffer<ConnectedEdge>(es[k]) ? em.GetBuffer<ConnectedEdge>(es[k], true).Length : 0;
                        }
                    if (nodesHere == 0) { missing++; problems.Add(new List<object> { i, j, "missing" }); continue; }
                    if (nodesHere > 1) { extraNodes += nodesHere - 1; problems.Add(new List<object> { i, j, nodesHere + " nodes" }); }
                    string key = expect + "->" + edges;
                    hist[key] = (hist.TryGetValue(key, out object v) ? (int)v : 0) + 1;
                    if (edges == expect) expectedOk++; else { wrong++; if (problems.Count < 20) problems.Add(new List<object> { i, j, key }); }
                }
            return new Dictionary<string, object>
            {
                { "op", "gridcheck" }, { "grid_points", (n + 1) * (n + 1) }, { "ok", expectedOk }, { "wrong_edge_count", wrong },
                { "missing", missing }, { "extra_nodes", extraNodes }, { "temp_nodes", temp }, { "expected_to_actual", hist },
                { "problems", problems }, { "world_temp_any", Count(ComponentType.ReadOnly<Temp>()) },
            };
        }

        static object PlaceRb(Dictionary<string, object> a)
        {
            float2 c = new float2(F(a, "x"), F(a, "z"));
            float spacing = F(a, "spacing", 45f), len = F(a, "length", 120f);
            var ps = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<PrefabSystem>();
            var placed = new List<object>();
            int i = 0;
            foreach (var id in RbIds())
            {
                var pid = new PrefabID("RoadBuilderPrefab", id);
                float2 off = new float2(0f, i++ * spacing);
                var row = new Dictionary<string, object> { { "id", id }, { "z", c.y + off.y } };
                if (ps.TryGetPrefab(pid, out PrefabBase prefab) && ps.TryGetEntity(prefab, out Entity pe))
                {
                    var em = World.DefaultGameObjectInjectionWorld.EntityManager;
                    if (em.HasComponent<NetGeometryData>(pe)) row["prefab_default_width"] = R(em.GetComponentData<NetGeometryData>(pe).m_DefaultWidth);
                    CreateSegment(pe, Ground(c + off + new float2(-len / 2, 0f)), Ground(c + off + new float2(len / 2, 0f)), Entity.Null, true);
                    row["placed"] = true;
                }
                else row["placed"] = false;
                placed.Add(row);
            }
            return new Dictionary<string, object> { { "op", "rb" }, { "roads", placed } };
        }

        static IEnumerable<string> RbIds()
        {
            var dir = Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "RoadBuilder");
            if (!Directory.Exists(dir)) yield break;
            var files = Directory.GetFiles(dir, "*-cs1conv.json");
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files) yield return Path.GetFileNameWithoutExtension(f);
        }

        /// <summary>
        /// Same definition shape as the M0RoadSpike mod (NOTES-cs2-placement.md). permanent=false gives a Temp preview
        /// (no CreationFlags.Permanent), which only becomes real through the ApplyTool phase.
        /// </summary>
        static Entity CreateSegment(Entity netPrefab, float3 start, float3 end, Entity startNode, bool permanent)
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            Bezier4x3 curve = NetUtils.StraightCurve(start, end);
            CreationDefinition def = default;
            def.m_Prefab = netPrefab;
            def.m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue);
            def.m_Flags = (permanent ? CreationFlags.Permanent : 0) | CreationFlags.SubElevation;
            NetCourse course = default;
            course.m_Curve = curve;
            course.m_StartPosition = Pos(curve, start, startNode, 0f, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
            course.m_EndPosition = Pos(curve, end, Entity.Null, 1f, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight);
            course.m_Length = MathUtils.Length(curve);
            course.m_FixedIndex = -1;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, def);
            em.AddComponentData(e, course);
            em.AddComponent<Updated>(e);
            em.AddComponent<Deleted>(e); // definition is destroyed at the end of the frame (CleanUpSystem)
            return e;
        }

        static CoursePos Pos(Bezier4x3 curve, float3 pos, Entity node, float t, CoursePosFlags flags)
        {
            CoursePos p = default;
            p.m_Entity = node;
            p.m_Position = pos;
            p.m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t));
            p.m_CourseDelta = t;
            p.m_ParentMesh = -1;
            p.m_Flags = flags;
            return p;
        }

        /// <summary>Arm end points: w(est), e(ast), n(orth = +z), s(outh).</summary>
        static List<float2> Arms(float2 c, float d, string which)
        {
            var r = new List<float2>();
            foreach (char k in which)
            {
                switch (k)
                {
                    case 'w': r.Add(c + new float2(-d, 0f)); break;
                    case 'e': r.Add(c + new float2(d, 0f)); break;
                    case 'n': r.Add(c + new float2(0f, d)); break;
                    case 's': r.Add(c + new float2(0f, -d)); break;
                }
            }
            return r;
        }

        // ---------------------------------------------------------------- evidence

        static object Inspect(float x, float z, float r)
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            float2 c = new float2(x, z);
            var centre = new List<object>();
            int nodesReal = 0, nodesTemp = 0, edgesReal = 0, edgesTemp = 0, edgesDeleted = 0;
            using (var q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Node>() }, Options = EntityQueryOptions.IncludeDisabledEntities }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var ns = q.ToComponentDataArray<Node>(Allocator.Temp))
            {
                for (int i = 0; i < es.Length; i++)
                {
                    float d = math.distance(ns[i].m_Position.xz, c);
                    if (d > r) continue;
                    bool temp = em.HasComponent<Temp>(es[i]), deleted = em.HasComponent<Deleted>(es[i]);
                    if (deleted) continue;
                    if (temp) nodesTemp++; else nodesReal++;
                    if (d > 8f) continue;
                    var edges = new List<object>();
                    if (em.HasBuffer<ConnectedEdge>(es[i]))
                    {
                        var buf = em.GetBuffer<ConnectedEdge>(es[i], true);
                        for (int j = 0; j < buf.Length; j++)
                        {
                            Entity ed = buf[j].m_Edge;
                            var row = new Dictionary<string, object> { { "edge", Id(ed) }, { "temp", em.HasComponent<Temp>(ed) } };
                            if (em.HasComponent<Edge>(ed))
                            {
                                var ee = em.GetComponentData<Edge>(ed);
                                Entity other = ee.m_Start == es[i] ? ee.m_End : ee.m_Start;
                                if (em.HasComponent<Node>(other)) row["other_end"] = V(em.GetComponentData<Node>(other).m_Position);
                            }
                            edges.Add(row);
                        }
                    }
                    var nrow = new Dictionary<string, object>
                    {
                        { "node", Id(es[i]) }, { "dist", R(d) }, { "temp", temp }, { "pos", V(ns[i].m_Position) },
                        { "connected_edges", edges.Count }, { "edges", edges },
                    };
                    if (temp) nrow["temp_flags"] = em.GetComponentData<Temp>(es[i]).m_Flags.ToString();
                    centre.Add(nrow);
                }
            }
            using (var q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() } }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var cs = q.ToComponentDataArray<Curve>(Allocator.Temp))
            {
                for (int i = 0; i < es.Length; i++)
                {
                    if (math.distance(MathUtils.Position(cs[i].m_Bezier, 0.5f).xz, c) > r) continue;
                    if (em.HasComponent<Deleted>(es[i])) { edgesDeleted++; continue; }
                    if (em.HasComponent<Temp>(es[i])) edgesTemp++; else edgesReal++;
                }
            }
            return new Dictionary<string, object>
            {
                { "op", "inspect" }, { "centre", V2(c) }, { "radius", r }, { "frame", UnityEngine.Time.frameCount },
                { "centre_nodes", centre },
                { "area", new Dictionary<string, object> { { "nodes_real", nodesReal }, { "nodes_temp", nodesTemp }, { "edges_real", edgesReal }, { "edges_temp", edgesTemp }, { "edges_deleted", edgesDeleted } } },
                { "world", new Dictionary<string, object> { { "temp_nodes", Count(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Temp>()) }, { "temp_edges", Count(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>()) }, { "temp_any", Count(ComponentType.ReadOnly<Temp>()) } } },
            };
        }

        static object RbWidth(float x, float z, float r)
        {
            var w = World.DefaultGameObjectInjectionWorld;
            var em = w.EntityManager;
            var ps = w.GetOrCreateSystemManaged<PrefabSystem>();
            float2 c = new float2(x, z);
            var rows = new List<object>();
            var area = Collect();
            var trees = area.Trees;
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<EdgeGeometry>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in es)
                {
                    var curve = em.GetComponentData<Curve>(e);
                    float3 mid = MathUtils.Position(curve.m_Bezier, 0.5f);
                    if (math.distance(mid.xz, c) > r) continue;
                    Entity pe = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var prefab = ps.GetPrefab<PrefabBase>(pe);
                    var g = em.GetComponentData<EdgeGeometry>(e);
                    float measured = math.distance(g.m_Start.m_Left.d.xz, g.m_Start.m_Right.d.xz); // edge middle
                    var row = new Dictionary<string, object>
                    {
                        { "edge", Id(e) }, { "prefab_type", prefab.GetType().Name }, { "prefab", prefab.name },
                        { "mid", V(mid) }, { "length", R(curve.m_Length) }, { "measured_width", R(measured) },
                    };
                    if (em.HasComponent<NetGeometryData>(pe)) row["prefab_default_width"] = R(em.GetComponentData<NetGeometryData>(pe).m_DefaultWidth);
                    row["trees_on_road"] = TreesNear(trees, curve.m_Bezier, measured / 2f);
                    row["overridden_trees_on_road"] = TreesNear(area.OverriddenTrees, curve.m_Bezier, measured / 2f);
                    rows.Add(row);
                }
            }
            return new Dictionary<string, object> { { "op", "rbwidth" }, { "edges", rows } };
        }

        /// <summary>Live trees whose trunk is within halfWidth of the curve (sampled every ~2 m).</summary>
        static int TreesNear(List<float2> trees, Bezier4x3 b, float halfWidth)
        {
            var pts = new List<float2>();
            float len = MathUtils.Length(b);
            int steps = math.max(2, (int)(len / 2f));
            for (int i = 0; i <= steps; i++) pts.Add(MathUtils.Position(b, (float)i / steps).xz);
            int n = 0;
            float r2 = halfWidth * halfWidth;
            foreach (var t in trees)
                foreach (var p in pts)
                    if (math.distancesq(t, p) <= r2) { n++; break; }
            return n;
        }

        // ---------------------------------------------------------------- free ground

        sealed class Area
        {
            public readonly List<float2> NetPoints = new List<float2>();
            public readonly List<float2> Buildings = new List<float2>();
            public readonly List<float2> Trees = new List<float2>();
            public readonly List<float2> OverriddenTrees = new List<float2>();
            public readonly List<float2> Other = new List<float2>();
        }

        static Area Collect()
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var a = new Area();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Node>()))
            using (var ns = q.ToComponentDataArray<Node>(Allocator.Temp))
                foreach (var n in ns) a.NetPoints.Add(n.m_Position.xz);
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>()))
            using (var cs = q.ToComponentDataArray<Curve>(Allocator.Temp))
                foreach (var cv in cs)
                    for (float t = 0f; t <= 1f; t += 1f / math.max(1f, math.ceil(cv.m_Length / 20f)))
                        a.NetPoints.Add(MathUtils.Position(cv.m_Bezier, t).xz);
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Objects.Object>(), ComponentType.ReadOnly<Game.Objects.Transform>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Owner>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var ts = q.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
            {
                for (int i = 0; i < es.Length; i++)
                {
                    var p = ts[i].m_Position.xz;
                    if (em.HasComponent<Game.Buildings.Building>(es[i])) a.Buildings.Add(p);
                    else if (em.HasComponent<Game.Objects.Tree>(es[i])) (em.HasComponent<Overridden>(es[i]) ? a.OverriddenTrees : a.Trees).Add(p);
                    else a.Other.Add(p);
                }
            }
            return a;
        }

        static int Within(List<float2> pts, float2 c, float r)
        {
            int n = 0;
            float r2 = r * r;
            foreach (var p in pts) if (math.distancesq(p, c) <= r2) n++;
            return n;
        }

        static float2 HeightRange(float2 c, float r)
        {
            var ts = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData h = ts.GetHeightData(true);
            float lo = float.MaxValue, hi = float.MinValue;
            for (float dx = -r; dx <= r; dx += 10f)
                for (float dz = -r; dz <= r; dz += 10f)
                {
                    float y = TerrainUtils.SampleHeight(ref h, new float3(c.x + dx, 0f, c.y + dz));
                    lo = math.min(lo, y); hi = math.max(hi, y);
                }
            return new float2(lo, hi);
        }

        static object Scan(float x, float z, float r)
        {
            var a = Collect();
            float2 c = new float2(x, z);
            float2 h = HeightRange(c, r);
            return new Dictionary<string, object>
            {
                { "op", "scan" }, { "centre", V2(c) }, { "radius", r },
                { "net_points", Within(a.NetPoints, c, r) }, { "buildings", Within(a.Buildings, c, r) },
                { "trees", Within(a.Trees, c, r) }, { "other_objects", Within(a.Other, c, r) },
                { "height_min", R(h.x) }, { "height_max", R(h.y) },
            };
        }

        static object Find(Dictionary<string, object> args)
        {
            float2 c0 = new float2(F(args, "x"), F(args, "z"));
            float search = F(args, "search", 3000f), step = F(args, "step", 200f), need = F(args, "need", 150f);
            int max = (int)F(args, "max", 5f);
            var a = Collect();
            var found = new List<KeyValuePair<float, Dictionary<string, object>>>();
            for (float dx = -search; dx <= search; dx += step)
                for (float dz = -search; dz <= search; dz += step)
                {
                    float2 c = c0 + new float2(dx, dz);
                    if (math.abs(c.x) > 6500f || math.abs(c.y) > 6500f) continue;
                    if (Within(a.NetPoints, c, need + 30f) > 0 || Within(a.Buildings, c, need) > 0) continue;
                    float2 h = HeightRange(c, need);
                    if (h.y - h.x > 4f) continue;
                    int trees = Within(a.Trees, c, need), other = Within(a.Other, c, need);
                    float score = trees + other * 5 + (h.y - h.x) * 10 + math.length(c - c0) / 1000f;
                    found.Add(new KeyValuePair<float, Dictionary<string, object>>(score, new Dictionary<string, object>
                    {
                        { "x", R(c.x) }, { "z", R(c.y) }, { "trees", trees }, { "other_objects", other },
                        { "height_min", R(h.x) }, { "height_range", R(h.y - h.x) },
                    }));
                }
            found.Sort((p, q) => p.Key.CompareTo(q.Key));
            var res = new List<object>();
            for (int i = 0; i < found.Count && i < max; i++) res.Add(found[i].Value);
            return new Dictionary<string, object> { { "op", "find" }, { "candidates", found.Count }, { "best", res } };
        }

        // ---------------------------------------------------------------- helpers

        static IEnumerator Delay(int frames, Func<object> then)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return then();
        }

        static Entity FindRealNode(float2 at, float maxDist, out Node data)
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            data = default;
            Entity best = Entity.Null;
            float bestD = maxDist;
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var ns = q.ToComponentDataArray<Node>(Allocator.Temp))
            {
                for (int i = 0; i < es.Length; i++)
                {
                    float d = math.distance(ns[i].m_Position.xz, at);
                    if (d < bestD) { bestD = d; best = es[i]; data = ns[i]; }
                }
            }
            return best;
        }

        static Entity Prefab(PrefabID id)
        {
            var ps = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<PrefabSystem>();
            if (!ps.TryGetPrefab(id, out PrefabBase p) || !ps.TryGetEntity(p, out Entity e)) throw new InvalidOperationException("prefab not found: " + id);
            return e;
        }

        static float3 Ground(float2 p)
        {
            var ts = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<TerrainSystem>();
            TerrainHeightData h = ts.GetHeightData(true);
            var v = new float3(p.x, 0f, p.y);
            v.y = TerrainUtils.SampleHeight(ref h, v);
            return v;
        }

        static int Count(params ComponentType[] types)
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            using (var q = em.CreateEntityQuery(types)) return q.CalculateEntityCount();
        }

        static float F(Dictionary<string, object> a, string k, float def = 0f)
        {
            if (a != null && a.TryGetValue(k, out object v) && v != null)
            {
                if (v is double d) return (float)d;
                if (v is long l) return l;
                if (v is int i) return i;
                if (v is string s && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) return f;
            }
            return def;
        }

        static bool B(Dictionary<string, object> a, string k) => a != null && a.TryGetValue(k, out object v) && v is bool b && b;
        static string Str(Dictionary<string, object> a, string k, string def) => a != null && a.TryGetValue(k, out object v) && v is string s ? s : def;
        static double R(float f) => Math.Round(f, 2);
        static string Id(Entity e) => e.Index + ":" + e.Version;
        static List<object> V(float3 p) => new List<object> { R(p.x), R(p.y), R(p.z) };
        static List<object> V2(float2 p) => new List<object> { R(p.x), R(p.y) };
    }
}
