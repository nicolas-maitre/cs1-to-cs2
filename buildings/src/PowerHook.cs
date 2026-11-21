// Op "powerhook" (buildings decision #44): connects every placed power producer to the city's grid, as CS2 needs and
// CS1 did implicitly (in CS1, power flows between touching buildings; a plant beside the city needs no line).
//
//   powerhook {lvReach=200, hvReach=600, lvMode="position"|"attach", transformer="TransformerStation01", transformers=false}
//             [phase tool, pump tool]
//
// - transformers (opt-in, default off: the importer invents no buildings by default): a producer with a high-voltage marker (coal, gas, incineration plants: "highVoltageNotConnected" otherwise) gets a
//   transformer placed by the nearest road (the placer, fallback "nearby"), and a high-voltage line from its marker to the
//   transformer's high-voltage marker. The transformer feeds the road grid's low voltage. Off, each such plant gets a
//   needTransformer line ("… feeds only the high-voltage grid: place a transformer station near it").
// - Low-voltage markers (wind turbines, and the plants' own) get a low-voltage line to the nearest road node or already
//   connected low-voltage marker within lvReach (closest producers first, so a wind farm chains to the road).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using GameBridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Buildings
{
    public static class PowerHook
    {
        sealed class Marker { public Entity Node, Building; public float3 Position; public bool High; }

        public static object Start(Dictionary<string, object> a) { return Routine(a); }

        static IEnumerator Routine(Dictionary<string, object> a)
        {
            var sw = Stopwatch.StartNew();
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            float lvReach = (float)P.Num(a, "lvReach", 200), hvReach = (float)P.Num(a, "hvReach", 600);
            bool attachRoad = P.Str(a, "lvMode", "position") == "attach";
            // Opt-in (coordinator, the importer invents no buildings by default: #83, #106, #128): off, a high-voltage plant
            // gets a report line instead, and the city keeps its power through the outside connection.
            bool withTransformers = P.Bool(a, "transformers", false);
            string transformerName = P.Str(a, "transformer", "TransformerStation01");
            Entity lvLine = Prefab(ps, "PowerLinePrefab", "Low-voltage Line"), hvLine = Prefab(ps, "PowerLinePrefab", "High-voltage Line");
            if (lvLine == Entity.Null || hvLine == Entity.Null) { yield return new Dictionary<string, object> { { "error", "power line prefabs not found" } }; yield break; }
            var rnd = new Unity.Mathematics.Random(0x9e3779b9u);
            var log = new List<object>();

            var guard = RoadGuard.Take(em);
            var producers = Producers(em);
            var markers = Markers(em);
            var byBuilding = new Dictionary<Entity, List<Marker>>();
            foreach (var m in markers) { List<Marker> l; if (!byBuilding.TryGetValue(m.Building, out l)) byBuilding[m.Building] = l = new List<Marker>(); l.Add(m); }

            // 1. Transformers for the producers with a high-voltage marker.
            var hvProducers = new List<KeyValuePair<Entity, Marker>>();
            foreach (var b in producers)
            {
                List<Marker> ms;
                if (!byBuilding.TryGetValue(b, out ms)) continue;
                foreach (var m in ms) if (m.High) { hvProducers.Add(new KeyValuePair<Entity, Marker>(b, m)); break; }
            }
            int transformersPlaced = 0, hvLines = 0, lvLines = 0;
            var needTransformer = new List<object>();
            if (!withTransformers)
                foreach (var kv in hvProducers)
                {
                    var pn = PrefabNameOf(em, ps, kv.Key);
                    needTransformer.Add(PlacementRules.NeedsTransformer(pn, PlacementRules.Position(kv.Value.Position.x, kv.Value.Position.z)));
                }
            object placeReport = null;
            var spots = new List<float2>();
            if (withTransformers && hvProducers.Count > 0)
            {
                var roads = Placement.RoadEdges(em);
                var items = new List<object>();
                int id = 900000;
                foreach (var kv in hvProducers)
                {
                    var p = kv.Value.Position.xz;
                    Placement.RoadEdge best = null;
                    float bestD = hvReach, bestT = 0;
                    foreach (var r in roads)
                    {
                        if (r.Highway) continue;
                        float t;
                        float d = MathUtils.Distance(r.Curve.xz, p, out t);
                        if (d < bestD) { bestD = d; best = r; bestT = t; }
                    }
                    if (best == null) { log.Add("no road within " + hvReach + " m of the plant at " + PlacementRules.Position(p.x, p.y)); spots.Add(new float2(float.NaN)); continue; }
                    var rp = MathUtils.Position(best.Curve, bestT).xz;
                    var dir = math.normalizesafe(p - rp, new float2(1, 0));
                    var front = rp + dir * best.HalfWidth;
                    var centre = front + dir * 12f;
                    spots.Add(centre);
                    items.Add(new Dictionary<string, object>
                    {
                        { "id", id++ }, { "prefab", transformerName }, { "cs1Info", "Transformer (for a power plant)" }, { "family", "power" },
                        { "x", (double)centre.x }, { "z", (double)centre.y }, { "front", new List<object> { (double)front.x, (double)front.y } },
                        { "facing", new List<object> { (double)-dir.x, (double)-dir.y } }, { "fallback", "nearby" }, { "requireRoad", true }, { "maxDistance", (double)hvReach },
                    });
                }
                if (items.Count > 0)
                {
                    var planPath = Path.Combine(Path.GetTempPath(), "cs1conv-powerhook-transformers.json");
                    File.WriteAllText(planPath, MiniJson.Write(new Dictionary<string, object> { { "buildings", new Dictionary<string, object> { { "items", items } } } }), new UTF8Encoding(false));
                    var inner = Placement.Place(new Dictionary<string, object> { { "plan", planPath }, { "fallback", "nearby" }, { "nearbyRadius", (double)hvReach }, { "wideRadius", 0.0 } }) as IEnumerator;
                    while (inner != null && inner.MoveNext())
                    {
                        if (inner.Current == null) yield return null;
                        else placeReport = inner.Current;
                    }
                    for (int f = 0; f < 10; f++) yield return null;
                    markers = Markers(em);
                }
            }

            // 2. High-voltage lines: plant marker → its transformer's high-voltage marker (nearest unused transformer).
            Entity transformerPrefab = Prefab(ps, "BuildingPrefab", transformerName);
            var usedTransformers = new HashSet<Entity>();
            foreach (var kv in withTransformers ? hvProducers : new List<KeyValuePair<Entity, Marker>>())
            {
                Marker best = null;
                float bestD = hvReach + 200f;
                foreach (var m in markers)
                {
                    if (!m.High || m.Building == kv.Key || usedTransformers.Contains(m.Building)) continue;
                    if (em.GetComponentData<PrefabRef>(m.Building).m_Prefab != transformerPrefab) continue;
                    float d = math.distance(m.Position.xz, kv.Value.Position.xz);
                    if (d < bestD) { bestD = d; best = m; }
                }
                if (best == null) { log.Add("no transformer for the plant at " + PlacementRules.Position(kv.Value.Position.x, kv.Value.Position.z)); continue; }
                usedTransformers.Add(best.Building);
                Line(em, hvLine, kv.Value.Position, best.Position, kv.Value.Node, best.Node, ref rnd, false);
                hvLines++;
                transformersPlaced++;
            }
            for (int f = 0; f < 5; f++) yield return null;

            // 3. Low-voltage lines: every producer's low-voltage markers (and the transformers' are fed through the road).
            var roadNodes = RoadLvNodes(em);
            var hooked = new List<Marker>();
            var lvMarkers = new List<Marker>();
            var producerSet = new HashSet<Entity>(producers);
            foreach (var m in markers) if (!m.High && producerSet.Contains(m.Building)) lvMarkers.Add(m);
            // closest to a road first, so clusters chain outward from the road
            Func<float3, float> roadDist = p => { float d = float.MaxValue; foreach (var r in roadNodes) d = math.min(d, math.distance(r.Item2.xz, p.xz)); return d; };
            lvMarkers.Sort((x, y) => roadDist(x.Position).CompareTo(roadDist(y.Position)));
            var buildingHooked = new HashSet<Entity>();
            foreach (var m in lvMarkers)
            {
                if (buildingHooked.Contains(m.Building)) continue; // one low-voltage link per building
                float bestD = lvReach;
                Entity target = Entity.Null;
                float3 tp = default;
                bool toRoad = false;
                foreach (var r in roadNodes) { float d = math.distance(r.Item2.xz, m.Position.xz); if (d < bestD) { bestD = d; target = r.Item1; tp = r.Item2; toRoad = true; } }
                foreach (var h in hooked) { float d = math.distance(h.Position.xz, m.Position.xz); if (d < bestD) { bestD = d; target = h.Node; tp = h.Position; toRoad = false; } }
                if (target == Entity.Null) { log.Add("no road within " + lvReach + " m of the low-voltage marker at " + PlacementRules.Position(m.Position.x, m.Position.z)); continue; }
                bool setback = toRoad && !attachRoad;
                if (setback) { var inward = math.normalizesafe(m.Position.xz - tp.xz, new float2(1, 0)); tp.x += inward.x * 3f; tp.z += inward.y * 3f; }
                Line(em, lvLine, m.Position, tp, m.Node, setback ? Entity.Null : target, ref rnd, setback);
                lvLines++;
                hooked.Add(m);
                buildingHooked.Add(m.Building);
            }
            // The producers (and transformers) whose markers got a line: marked Updated, so CS2 rebuilds their
            // ElectricityBuildingConnection (an off-road wind turbine kept none after its marker was linked).
            int updated = 0;
            foreach (var b in buildingHooked) if (em.Exists(b) && !em.HasComponent<Updated>(b)) { em.AddComponent<Updated>(b); updated++; }
            foreach (var b in usedTransformers) if (em.Exists(b) && !em.HasComponent<Updated>(b)) { em.AddComponent<Updated>(b); updated++; }
            foreach (var kv in hvProducers) if (em.Exists(kv.Key) && !em.HasComponent<Updated>(kv.Key)) { em.AddComponent<Updated>(kv.Key); updated++; }
            for (int f = 0; f < 10; f++) yield return null;
            int roadsTouched = RoadGuard.TouchLocallyConnected(em, new HashSet<Entity> { lvLine, hvLine });
            for (int f = 0; f < 5; f++) yield return null;
            int roadsChanged;
            var roadCheck = guard.Check(em, out roadsChanged);

            yield return new Dictionary<string, object>
            {
                { "roadGuard", roadCheck }, { "roadsTouched", roadsTouched }, { "ok", roadsChanged == 0 },
                { "producers", producers.Count }, { "withHighVoltage", hvProducers.Count }, { "transformers", withTransformers }, { "needTransformer", needTransformer }, { "transformersLinked", transformersPlaced }, { "hvLines", hvLines },
                { "buildingsUpdated", updated }, { "lvMarkers", lvMarkers.Count }, { "lvLines", lvLines }, { "lvMode", attachRoad ? "attach" : "position" }, { "roadNodes", roadNodes.Count },
                { "transformerPlacement", placeReport }, { "log", log }, { "seconds", Math.Round(sw.Elapsed.TotalSeconds, 2) },
            };
        }

        static void Line(EntityManager em, Entity prefab, float3 a, float3 b, Entity startNode, Entity endNode, ref Unity.Mathematics.Random rnd, bool endNoMerge)
        {
            var curve = NetUtils.StraightCurve(a, b);
            var e = em.CreateEntity();
            em.AddComponentData(e, new CreationDefinition { m_Prefab = prefab, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.Permanent | CreationFlags.SubElevation });
            em.AddComponentData(e, new NetCourse
            {
                m_Curve = curve,
                m_StartPosition = Pos(curve, a, startNode, 0f, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight),
                m_EndPosition = Pos(curve, b, endNode, 1f, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight | (endNoMerge ? CoursePosFlags.DisableMerge : 0)),
                m_Elevation = new float2(0f),
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = -1,
            });
            em.AddComponent<Updated>(e);
            em.AddComponent<Deleted>(e);
        }

        static CoursePos Pos(Bezier4x3 curve, float3 pos, Entity node, float t, CoursePosFlags flags)
        {
            return new CoursePos
            {
                m_Entity = node, m_Position = pos, m_Elevation = new float2(0f),
                m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t)), m_CourseDelta = t, m_ParentMesh = -1, m_Flags = flags,
            };
        }

        static string PrefabNameOf(EntityManager em, PrefabSystem ps, Entity b)
        {
            return em.HasComponent<PrefabRef>(b) ? ps.GetPrefabName(em.GetComponentData<PrefabRef>(b).m_Prefab) : "Power plant";
        }

        static Entity Prefab(PrefabSystem ps, string type, string name)
        {
            PrefabBase pb;
            Entity pe;
            return ps.TryGetPrefab(new PrefabID(type, name), out pb) && ps.TryGetEntity(pb, out pe) ? pe : Entity.Null;
        }

        /// <summary>Placed buildings whose prefab produces electricity (PowerPlantData).</summary>
        static List<Entity> Producers(EntityManager em)
        {
            var l = new List<Entity>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var b in es)
                    if (em.HasComponent<PowerPlantData>(em.GetComponentData<PrefabRef>(b).m_Prefab)) l.Add(b);
            return l;
        }

        /// <summary>Power markers (lone nodes owned by a building, layer PowerlineHigh / PowerlineLow) with their building.</summary>
        static List<Marker> Markers(EntityManager em)
        {
            var l = new List<Marker>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<PrefabRef>(), ComponentType.ReadOnly<Owner>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var n in es)
                {
                    var pr = em.GetComponentData<PrefabRef>(n).m_Prefab;
                    if (!em.HasComponent<NetData>(pr)) continue;
                    var layers = em.GetComponentData<NetData>(pr).m_RequiredLayers;
                    bool high = (layers & Layer.PowerlineHigh) != 0, low = (layers & Layer.PowerlineLow) != 0;
                    if (!high && !low) continue;
                    // a marker: no edge of its own (a line already attached to it keeps it a marker too)
                    var owner = em.GetComponentData<Owner>(n).m_Owner;
                    for (int i = 0; i < 4 && owner != Entity.Null && !em.HasComponent<Building>(owner) && em.HasComponent<Owner>(owner); i++) owner = em.GetComponentData<Owner>(owner).m_Owner;
                    if (owner == Entity.Null || !em.HasComponent<Building>(owner)) continue;
                    l.Add(new Marker { Node = n, Building = owner, Position = em.GetComponentData<Node>(n).m_Position, High = high });
                }
            return l;
        }

        /// <summary>Road nodes (not building-owned) of roads that take low voltage locally.</summary>
        static List<Tuple<Entity, float3>> RoadLvNodes(EntityManager em)
        {
            var l = new List<Tuple<Entity, float3>>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var n in es)
                {
                    var edges = em.GetBuffer<ConnectedEdge>(n, true);
                    for (int i = 0; i < edges.Length; i++)
                    {
                        if (!em.HasComponent<PrefabRef>(edges[i].m_Edge)) continue;
                        var pr = em.GetComponentData<PrefabRef>(edges[i].m_Edge).m_Prefab;
                        if (!em.HasComponent<NetData>(pr)) continue;
                        var nd = em.GetComponentData<NetData>(pr);
                        // highways carry no low voltage in CS2 (UseHighwayRules): a link there feeds nothing
                        bool highway = em.HasComponent<RoadData>(pr) && (em.GetComponentData<RoadData>(pr).m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;
                        if (!highway && (nd.m_RequiredLayers & Layer.Road) != 0 && (nd.m_LocalConnectLayers & Layer.PowerlineLow) != 0) { l.Add(Tuple.Create(n, em.GetComponentData<Node>(n).m_Position)); break; }
                    }
                }
            return l;
        }
    }
}
