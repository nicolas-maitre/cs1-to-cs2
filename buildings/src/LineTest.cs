// Op "linetest" (diagnostic, buildings decision #45): how a utility line end joins a road without splitting it.
//   linetest {count=3, prefab="Low-voltage Line", type="PowerLinePrefab", length=30, setback=4, frames=30}
// Picks count ground road junctions (3+ road edges, not building-owned) per variant and builds a short line ending there:
//   "merge"   end at the node position, m_Entity Null (the old utilities step: worker-3 saw the junction split)
//   "nomerge" the same with CoursePosFlags.DisableMerge
//   "setback" end pulled back `setback` m toward the line, DisableMerge
//   "attach"  m_Entity = the road node
// After `frames` frames, for each: the road node's road edge count before/after, nodes within 1 m of the junction, and
// the line end node's components (ElectricityNodeConnection / WaterPipeNodeConnection = joined the graph). Use on a
// loaded cs1conv-* save that is not saved afterwards.
using System;
using System.Collections;
using System.Collections.Generic;
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
    public static class LineTest
    {
        public static object Start(Dictionary<string, object> a) { return Routine(a); }

        static IEnumerator Routine(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var heights = world.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(true);
            int count = (int)P.Num(a, "count", 3), frames = (int)P.Num(a, "frames", 30), skip = (int)P.Num(a, "skip", 0);
            float length = (float)P.Num(a, "length", 30), setback = (float)P.Num(a, "setback", 4);
            PrefabBase pb;
            Entity pe;
            if (!ps.TryGetPrefab(new PrefabID(P.Str(a, "type", "PowerLinePrefab"), P.Str(a, "prefab", "Low-voltage Line")), out pb) || !ps.TryGetEntity(pb, out pe)) { yield return "prefab not found"; yield break; }
            float elev = 0f;
            if (em.HasComponent<PlaceableNetData>(pe)) { var r = em.GetComponentData<PlaceableNetData>(pe).m_ElevationRange; elev = math.clamp(0f, r.min, r.max); }
            var info = new Dictionary<string, object> { { "prefab", pb.name }, { "elevation", elev } };
            if (em.HasComponent<LocalConnectData>(pe)) { var lc = em.GetComponentData<LocalConnectData>(pe); info["localConnect"] = lc.m_Flags + " layers " + lc.m_Layers + " search " + lc.m_SearchDistance + " height " + lc.m_HeightRange.min + ".." + lc.m_HeightRange.max; }

            // junctions: 3+ road edges, no owner, all edges ground roads
            var junctions = new List<Entity>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>() }, None = new[] { ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() } }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var n in es)
                {
                    var edges = em.GetBuffer<ConnectedEdge>(n, true);
                    if (edges.Length < 3) continue;
                    bool ok = true;
                    for (int i = 0; i < edges.Length && ok; i++)
                    {
                        var pr = em.GetComponentData<PrefabRef>(edges[i].m_Edge).m_Prefab;
                        ok = em.HasComponent<NetData>(pr) && (em.GetComponentData<NetData>(pr).m_RequiredLayers & Layer.Road) != 0 && !em.HasComponent<Elevation>(edges[i].m_Edge);
                    }
                    if (ok && skip-- <= 0) junctions.Add(n);
                    if (junctions.Count >= count * 5 + 8) break;
                }
            if (junctions.Count < count * 5) { yield return new Dictionary<string, object> { { "error", "only " + junctions.Count + " junctions" } }; yield break; }
            if (em.HasComponent<PrefabRef>(junctions[0]))
            {
                var rp = em.GetComponentData<PrefabRef>(em.GetBuffer<ConnectedEdge>(junctions[0], true)[0].m_Edge).m_Prefab;
                if (em.HasComponent<LocalConnectData>(rp)) { var lc = em.GetComponentData<LocalConnectData>(rp); info["roadLocalConnect"] = lc.m_Flags + " layers " + lc.m_Layers + " search " + lc.m_SearchDistance; }
            }

            var variants = P.Str(a, "variants", "free,merge,nomerge,setback,attach").Split(',');
            var cases = new List<Dictionary<string, object>>();
            var rnd = new Unity.Mathematics.Random(0x1234567u);
            int ji = 0;
            foreach (var v in variants)
                for (int c = 0; c < count; c++)
                {
                    var j = junctions[ji++];
                    var jp = em.GetComponentData<Node>(j).m_Position;
                    // the line comes in from the side away from the first edge's direction (a free-ish quadrant)
                    var e0 = em.GetComponentData<Edge>(em.GetBuffer<ConnectedEdge>(j, true)[0].m_Edge);
                    var other = em.GetComponentData<Node>(e0.m_Start == j ? e0.m_End : e0.m_Start).m_Position;
                    var away = math.normalizesafe(new float2(-(other - jp).z, (other - jp).x) - math.normalizesafe((other - jp).xz), new float2(1, 0));
                    var far = new float3(jp.x + away.x * length, 0, jp.z + away.y * length);
                    far.y = TerrainUtils.SampleHeight(ref heights, far) + elev;
                    var end = jp;
                    end.y = TerrainUtils.SampleHeight(ref heights, end) + elev;
                    if (v == "free") { end = new float3(jp.x + away.x * (length + 12f), 0, jp.z + away.y * (length + 12f)); far = new float3(far.x + away.y * 30f, 0, far.z - away.x * 30f); far.y = TerrainUtils.SampleHeight(ref heights, far) + elev; end.y = TerrainUtils.SampleHeight(ref heights, end) + elev; }
                    if (v == "setback") { end.x += away.x * setback; end.z += away.y * setback; end.y = TerrainUtils.SampleHeight(ref heights, end) + elev; }
                    var flags = CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight | (v == "merge" || v == "attach" || v == "free" ? 0 : CoursePosFlags.DisableMerge);
                    var curve = NetUtils.StraightCurve(far, end);
                    var d = em.CreateEntity();
                    em.AddComponentData(d, new CreationDefinition { m_Prefab = pe, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.Permanent | CreationFlags.SubElevation });
                    em.AddComponentData(d, new NetCourse
                    {
                        m_Curve = curve,
                        m_StartPosition = new CoursePos { m_Entity = Entity.Null, m_Position = far, m_Elevation = new float2(elev), m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 0)), m_CourseDelta = 0, m_ParentMesh = -1, m_Flags = CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight },
                        m_EndPosition = new CoursePos { m_Entity = v == "attach" ? j : Entity.Null, m_Position = end, m_Elevation = new float2(elev), m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, 1)), m_CourseDelta = 1, m_ParentMesh = -1, m_Flags = flags },
                        m_Elevation = new float2(elev), m_Length = MathUtils.Length(curve), m_FixedIndex = -1,
                    });
                    em.AddComponent<Updated>(d);
                    em.AddComponent<Deleted>(d);
                    cases.Add(new Dictionary<string, object> { { "variant", v }, { "junction", j }, { "pos", jp }, { "end", end }, { "edgesBefore", em.GetBuffer<ConnectedEdge>(j, true).Length } });
                }
            for (int f = 0; f < frames; f++) yield return null;

            var res = new List<object>();
            foreach (var c in cases)
            {
                var j = (Entity)c["junction"];
                var jp = (float3)c["pos"];
                var endp = (float3)c["end"];
                var o = new Dictionary<string, object> { { "variant", c["variant"] }, { "junction", j.Index } };
                o["junctionAlive"] = em.Exists(j) && !em.HasComponent<Deleted>(j);
                o["roadEdges"] = c["edgesBefore"] + " → " + (em.Exists(j) && em.HasBuffer<ConnectedEdge>(j) ? RoadEdges(em, j) + " road + " + (em.GetBuffer<ConnectedEdge>(j, true).Length - RoadEdges(em, j)) + " other" : "gone");
                var near = new List<object>();
                Entity lineEnd = Entity.Null;
                float best = 9f;
                using (var q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>() }, None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() } }))
                using (var es = q.ToEntityArray(Allocator.Temp))
                    foreach (var n in es)
                    {
                        var np = em.GetComponentData<Node>(n).m_Position;
                        float dj = math.distance(np.xz, jp.xz), de = math.distance(np.xz, endp.xz);
                        if (dj < 1f) near.Add(n.Index + " " + ps.GetPrefabName(em.GetComponentData<PrefabRef>(n).m_Prefab) + " edges " + (em.HasBuffer<ConnectedEdge>(n) ? em.GetBuffer<ConnectedEdge>(n, true).Length : 0));
                        if (de < best && em.GetComponentData<PrefabRef>(n).m_Prefab == pe) { best = de; lineEnd = n; }
                    }
                o["nodesAtJunction"] = near;
                if (lineEnd != Entity.Null)
                {
                    var comps = new List<string>();
                    foreach (var t in em.GetComponentTypes(lineEnd, Allocator.Temp)) { var nm = t.GetManagedType().Name; if (!nm.StartsWith("Prefab") && nm != "Node") comps.Add(nm); }
                    o["lineEnd"] = lineEnd.Index + " edges " + (em.HasBuffer<ConnectedEdge>(lineEnd) ? em.GetBuffer<ConnectedEdge>(lineEnd, true).Length : 0) + " [" + string.Join(", ", comps) + "]";
                }
                else o["lineEnd"] = "none (merged into the junction?)";
                res.Add(o);
            }
            yield return new Dictionary<string, object> { { "info", info }, { "cases", res } };
        }

        static int RoadEdges(EntityManager em, Entity n)
        {
            int c = 0;
            var edges = em.GetBuffer<ConnectedEdge>(n, true);
            for (int i = 0; i < edges.Length; i++)
            {
                var pr = em.GetComponentData<PrefabRef>(edges[i].m_Edge).m_Prefab;
                if (em.HasComponent<NetData>(pr) && (em.GetComponentData<NetData>(pr).m_RequiredLayers & Layer.Road) != 0) c++;
            }
            return c;
        }
    }
}
