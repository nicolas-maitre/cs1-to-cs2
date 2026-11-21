// Node Controller lane-alignment spike: can a CS2 road edge end off its node's centre (the CS1 Node Controller "shift"),
// and what do CS2's node geometry and lanes do with it? Findings: cs2-importer/NOTES-lane-alignment.md.
// Hot-loaded exec library for the CS2 bridge (tools/PROTOCOL.md "exec"); run.mjs drives it. Entry: NcAlign.Ops.Run.
//
// Ops (world metres, y from the terrain):
//   info    {prefabs:[name]}                      NetGeometryData flags / widths of road prefabs
//   courses {courses:[{prefab, a:[x,z], b:[x,z], ca?:[x,z], cb?:[x,z], p1?:[x,z], p2?:[x,z]}], temp=false} [phase tool]
//           one definition per course. a/b = node positions (an existing real node within 0.5 m is attached to,
//           otherwise a new node is made there); ca/cb = the curve's end points (default a/b); p1/p2 = inner control
//           points (default: thirds of ca→cb). temp=true: Temp definitions (the tool's preview path), then call apply.
//   apply   {}                                    [phase tool] one ApplyTool frame, as a click does
//   inspect {x,z,r=150,frames=0}                  nodes, edges (curve ends vs node), edge geometry ends, node car lanes
//   clear   {x,z,r=150}                           Deleted on every net (edge, node) within r
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace NcAlign
{
    public static class Ops
    {
        static World W => World.DefaultGameObjectInjectionWorld;
        static EntityManager Em => W.EntityManager;
        static PrefabSystem Prefabs => W.GetOrCreateSystemManaged<PrefabSystem>();

        public static object Run(Dictionary<string, object> a)
        {
            string op = a.TryGetValue("op", out var o) ? Convert.ToString(o) : "inspect";
            switch (op)
            {
                case "info": return Info(a);
                case "courses": return Courses(a);
                case "apply": return new Dictionary<string, object> { { "op", "apply" }, { "applied", SetApply() } };
                case "inspect": return Delay((int)F(a, "frames", 0), () => Inspect(F(a, "x"), F(a, "z"), F(a, "r", 150)));
                case "clear": return Clear(F(a, "x"), F(a, "z"), F(a, "r", 150));
                default: throw new ArgumentException("unknown op " + op);
            }
        }

        // ---------------------------------------------------------------- helpers

        static float F(Dictionary<string, object> a, string k, float d = 0) => a != null && a.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v) : d;
        static float2 P2(object v) { var l = (IList<object>)v; return new float2(Convert.ToSingle(l[0]), Convert.ToSingle(l[1])); }
        static List<object> V(float3 v) => new List<object> { R(v.x), R(v.y), R(v.z) };
        static List<object> V2(float2 v) => new List<object> { R(v.x), R(v.y) };
        static double R(float f) => Math.Round(f, 3);
        static string Id(Entity e) => e.Index + ":" + e.Version;
        static string Name(Entity prefab) { try { return Prefabs.GetPrefabName(prefab); } catch { return "?"; } }
        static bool IsReal(Entity e) => e != Entity.Null && Em.Exists(e) && !Em.HasComponent<Temp>(e) && !Em.HasComponent<Deleted>(e);

        static IEnumerator Delay(int frames, Func<object> f)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return f();
        }

        static float Height(float2 p)
        {
            var data = W.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(true);
            return TerrainUtils.SampleHeight(ref data, new float3(p.x, 0, p.y));
        }

        static Entity PrefabByName(string name)
        {
            foreach (var type in new[] { "RoadPrefab", "TrackPrefab", "PathwayPrefab" })
                if (Prefabs.TryGetPrefab(new PrefabID(type, name), out PrefabBase pb)) return Prefabs.GetEntity(pb);
            throw new ArgumentException("prefab not found: " + name);
        }

        static List<Entity> All(params ComponentType[] all)
        {
            var q = Em.CreateEntityQuery(new EntityQueryDesc { All = all, None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() } });
            var r = new List<Entity>();
            using (var es = q.ToEntityArray(Allocator.Temp)) foreach (var e in es) r.Add(e);
            q.Dispose();
            return r;
        }

        static Entity NodeNear(float2 p, float tol)
        {
            Entity best = Entity.Null; float bd = tol;
            foreach (var n in All(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>()))
            {
                float d = math.distance(Em.GetComponentData<Node>(n).m_Position.xz, p);
                if (d <= bd) { bd = d; best = n; }
            }
            return best;
        }

        internal static bool SetApply()
        {
            var ts = W.GetOrCreateSystemManaged<ToolSystem>();
            var f = typeof(ToolSystem).GetField("m_LastTool", BindingFlags.Instance | BindingFlags.NonPublic);
            var tool = (f?.GetValue(ts) as ToolBaseSystem) ?? ts.activeTool;
            if (tool == null) return false;
            var p = typeof(ToolBaseSystem).GetProperty("applyMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var set = p?.GetSetMethod(true);
            if (set == null) return false;
            set.Invoke(tool, new object[] { ApplyMode.Apply });
            return true;
        }

        // ---------------------------------------------------------------- ops

        static object Info(Dictionary<string, object> a)
        {
            var rows = new List<object>();
            foreach (var n in (IList<object>)a["prefabs"])
            {
                var name = Convert.ToString(n);
                var e = PrefabByName(name);
                var g = Em.GetComponentData<NetGeometryData>(e);
                rows.Add(new Dictionary<string, object> {
                    { "name", name }, { "flags", g.m_Flags.ToString() }, { "strictNodes", (g.m_Flags & Game.Net.GeometryFlags.StrictNodes) != 0 },
                    { "width", g.m_DefaultWidth }, { "minNodeOffset", g.m_MinNodeOffset },
                });
            }
            return new Dictionary<string, object> { { "op", "info" }, { "prefabs", rows } };
        }

        static object Courses(Dictionary<string, object> a)
        {
            bool temp = a.TryGetValue("temp", out var t) && Convert.ToBoolean(t);
            var made = new List<object>();
            foreach (var co in (IList<object>)a["courses"])
            {
                var c = (Dictionary<string, object>)co;
                var prefab = PrefabByName(Convert.ToString(c["prefab"]));
                float2 na = P2(c["a"]), nb = P2(c["b"]);
                float2 ca = c.ContainsKey("ca") ? P2(c["ca"]) : na, cb = c.ContainsKey("cb") ? P2(c["cb"]) : nb;
                float2 p1 = c.ContainsKey("p1") ? P2(c["p1"]) : math.lerp(ca, cb, 1f / 3), p2 = c.ContainsKey("p2") ? P2(c["p2"]) : math.lerp(ca, cb, 2f / 3);
                float ya = Height(na), yb = Height(nb);
                var curve = new Bezier4x3(new float3(ca.x, ya, ca.y), new float3(p1.x, math.lerp(ya, yb, 1f / 3), p1.y),
                                          new float3(p2.x, math.lerp(ya, yb, 2f / 3), p2.y), new float3(cb.x, yb, cb.y));
                Entity ea = NodeNear(na, 0.5f), eb = NodeNear(nb, 0.5f);
                var def = new CreationDefinition { m_Prefab = prefab, m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue), m_Flags = (temp ? 0 : CreationFlags.Permanent) | CreationFlags.SubElevation };
                var course = new NetCourse
                {
                    m_Curve = curve,
                    m_StartPosition = Pos(curve, new float3(na.x, ya, na.y), ea, 0f, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight),
                    m_EndPosition = Pos(curve, new float3(nb.x, yb, nb.y), eb, 1f, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight),
                    m_Length = MathUtils.Length(curve),
                    m_FixedIndex = -1,
                };
                var e = Em.CreateEntity();
                Em.AddComponentData(e, def);
                Em.AddComponentData(e, course);
                Em.AddComponent<Updated>(e);
                Em.AddComponent<Deleted>(e);
                made.Add(new Dictionary<string, object> { { "prefab", Convert.ToString(c["prefab"]) }, { "attachA", ea != Entity.Null }, { "attachB", eb != Entity.Null } });
            }
            return new Dictionary<string, object> { { "op", "courses" }, { "temp", temp }, { "made", made }, { "frame", UnityEngine.Time.frameCount } };
        }

        static CoursePos Pos(Bezier4x3 curve, float3 nodePos, Entity node, float t, CoursePosFlags flags)
        {
            return new CoursePos
            {
                m_Entity = node, m_Position = nodePos, m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t)),
                m_CourseDelta = t, m_ParentMesh = -1, m_Flags = flags,
            };
        }

        static object Clear(float x, float z, float r)
        {
            int n = 0;
            var c = new float2(x, z);
            foreach (var e in All(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>()))
                if (math.distance(MathUtils.Position(Em.GetComponentData<Curve>(e).m_Bezier, 0.5f).xz, c) < r) { Em.AddComponent<Deleted>(e); n++; }
            foreach (var e in All(ComponentType.ReadOnly<Node>()))
                if (!Em.HasComponent<Owner>(e) && math.distance(Em.GetComponentData<Node>(e).m_Position.xz, c) < r) { Em.AddComponent<Deleted>(e); n++; }
            return new Dictionary<string, object> { { "op", "clear" }, { "deleted", n } };
        }

        static object Inspect(float x, float z, float r)
        {
            var c = new float2(x, z);
            var nodes = new List<object>();
            var edges = new List<object>();
            foreach (var e in All(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>()))
            {
                var curve = Em.GetComponentData<Curve>(e).m_Bezier;
                if (math.distance(MathUtils.Position(curve, 0.5f).xz, c) > r) continue;
                var ed = Em.GetComponentData<Edge>(e);
                float3 ps = Em.GetComponentData<Node>(ed.m_Start).m_Position, pe = Em.GetComponentData<Node>(ed.m_End).m_Position;
                var row = new Dictionary<string, object> {
                    { "id", Id(e) }, { "prefab", Name(Em.GetComponentData<PrefabRef>(e).m_Prefab) },
                    { "start", Id(ed.m_Start) }, { "end", Id(ed.m_End) },
                    { "curve", new List<object> { V(curve.a), V(curve.b), V(curve.c), V(curve.d) } },
                    { "startOffNode", R(math.distance(curve.a.xz, ps.xz)) }, { "endOffNode", R(math.distance(curve.d.xz, pe.xz)) },
                };
                if (Em.HasComponent<EdgeGeometry>(e))
                {
                    var g = Em.GetComponentData<EdgeGeometry>(e);
                    row["geomStart"] = new List<object> { V(g.m_Start.m_Left.a), V(g.m_Start.m_Right.a) };
                    row["geomEnd"] = new List<object> { V(g.m_End.m_Left.d), V(g.m_End.m_Right.d) };
                }
                if (Em.HasComponent<Road>(e)) row["roadFlags"] = Em.GetComponentData<Road>(e).m_Flags.ToString();
                if (Em.HasComponent<Composition>(e))
                {
                    var comp = Em.GetComponentData<Composition>(e);
                    if (Em.HasComponent<NetCompositionData>(comp.m_Edge))
                    {
                        var cd = Em.GetComponentData<NetCompositionData>(comp.m_Edge);
                        row["compWidth"] = cd.m_Width; row["compMiddleOffset"] = cd.m_MiddleOffset;
                    }
                }
                row["lanes"] = CarLanes(e);
                edges.Add(row);
            }
            foreach (var n in All(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>()))
            {
                var np = Em.GetComponentData<Node>(n).m_Position;
                if (math.distance(np.xz, c) > r) continue;
                var row = new Dictionary<string, object> { { "id", Id(n) }, { "pos", V(np) }, { "edges", Em.GetBuffer<ConnectedEdge>(n, true).Length } };
                if (Em.HasComponent<NodeGeometry>(n))
                {
                    var g = Em.GetComponentData<NodeGeometry>(n);
                    row["bounds"] = new List<object> { V(g.m_Bounds.min), V(g.m_Bounds.max) };
                }
                row["lanes"] = CarLanes(n);
                nodes.Add(row);
            }
            return new Dictionary<string, object> { { "op", "inspect" }, { "nodes", nodes }, { "edges", edges }, { "frame", UnityEngine.Time.frameCount } };
        }

        /// <summary>Car lanes owned by a net (edge or node): end points, and for node lanes how far they bend sideways
        /// (the end point's offset from the start point's tangent line).</summary>
        static List<object> CarLanes(Entity owner)
        {
            var r = new List<object>();
            if (!Em.HasBuffer<Game.Net.SubLane>(owner)) return r;
            var subs = Em.GetBuffer<Game.Net.SubLane>(owner, true);
            for (int i = 0; i < subs.Length; i++)
            {
                var le = subs[i].m_SubLane;
                if ((subs[i].m_PathMethods & Game.Pathfind.PathMethod.Road) == 0) continue;
                if (!Em.HasComponent<Game.Net.CarLane>(le) || Em.HasComponent<Game.Net.SecondaryLane>(le) || Em.HasComponent<MasterLane>(le)) continue;
                var lc = Em.GetComponentData<Curve>(le).m_Bezier;
                var t0 = math.normalizesafe(MathUtils.StartTangent(lc).xz);
                float2 d = lc.d.xz - lc.a.xz;
                float lateral = t0.x * d.y - t0.y * d.x; // + = to the left of the start direction
                var t1 = math.normalizesafe(MathUtils.EndTangent(lc).xz);
                float turn = math.degrees(math.atan2(t0.x * t1.y - t0.y * t1.x, math.dot(t0, t1)));
                r.Add(new Dictionary<string, object> { { "a", V2(lc.a.xz) }, { "d", V2(lc.d.xz) }, { "lateral", R(lateral) }, { "turnDeg", R(turn) }, { "len", R(MathUtils.Length(lc)) } });
            }
            return r;
        }
    }
}
