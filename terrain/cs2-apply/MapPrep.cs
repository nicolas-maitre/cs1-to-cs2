// Map preparation for a new CS2 city (M2), as hot-loaded exec ops next to the terrain ops.
// Entry point: Cs1ConvTerrain.MapPrep.Run(args), dispatched on args["op"]. terrain/mapprep.mjs drives it.
// Background: terrain/NOTES-cs2-terrain.md (§7 outside connections, §8 water).
//
// Ops:
//   survey      {grid=15}                         what the base map ships with: nets by prefab, outside connections,
//                                                 buildings, water sources, sea level, terrain range
//   clear       {frames=10, all=false} [phase+pump tool]                     delete the base map's road and track nets (importer pattern) and
//                                                 with their end nodes the road/train outside-connection markers;
//                                                 powerlines, seaways and their connections stay
//   oc.prefabs  {}                                outside-connection marker prefabs (name, transfer type, remoteness)
//   oc.list     {}                                outside connections in the city
//   oc.create   {connections:[{x,z,type:"road"|"train",prefab?}], radius=3}  [phase tool]
//                                                 one marker per connection, owned by the real node at (x,z)
//   water.reset {mode:"sea"|"empty"="sea"}        WaterSystem.ResetToSealevel() / Reset() (the editor's buttons)
//   counts      {}                                vehicles (cars, trains, personal, delivery, cargo) and outside-connection
//                                                 lanes, to see whether through traffic spawns
//   water.sample {points:[[x,z],...]}             water depth (m) at each point, from the water surface readback
//   water.set   {seaLevel?, clear=true, sources:[{x,z,y?,height,radius,polluted=0,constantDepth=0}]}  [phase tool]
//                                                 replaces the water sources (entities created directly: a definition
//                                                 only ever gives a Temp source, see NOTES §8)
using System;
using System.Collections.Generic;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs1ConvTerrain
{
    public static class MapPrep
    {
        public static object Run(Dictionary<string, object> a)
        {
            string op = Str(a, "op", "survey");
            switch (op)
            {
                case "survey": return Survey((int)F(a, "grid", 15f));
                case "clear": return Clear((int)F(a, "frames", 10f), B(a, "all"), !B(a, "keepBuildings"));
                case "oc.prefabs": return OcPrefabs();
                case "oc.list": return OcList();
                case "oc.create": return OcCreate(a);
                case "water.reset": return WaterReset(Str(a, "mode", "sea"));
                case "water.set": return WaterSet(a);
                case "water.sample": return WaterSample(a);
                case "counts": return Counts();
                default: throw new ArgumentException("unknown op " + op);
            }
        }

        static World W => World.DefaultGameObjectInjectionWorld;
        static EntityManager Em => W.EntityManager;
        static PrefabSystem Prefabs => W.GetOrCreateSystemManaged<PrefabSystem>();

        static string PrefabName(Entity prefab)
        {
            try { return Prefabs.GetPrefabName(prefab); } catch { return "?"; }
        }

        static bool IsReal(Entity e) => e != Entity.Null && Em.Exists(e) && !Em.HasComponent<Temp>(e) && !Em.HasComponent<Deleted>(e);

        static NativeArray<Entity> Query(ComponentType[] all, ComponentType[] none = null)
        {
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = all,
                None = none ?? new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            var r = q.ToEntityArray(Allocator.Temp);
            q.Dispose();
            return r;
        }

        // ---------------------------------------------------------------- survey

        static object Survey(int grid)
        {
            var edges = Query(new[] { ComponentType.ReadOnly<Edge>() });
            var byPrefab = new Dictionary<string, int>();
            int owned = 0, ownedByBuilding = 0;
            foreach (var e in edges)
            {
                string n = PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab);
                if (Em.HasComponent<Owner>(e))
                {
                    owned++;
                    var o = Em.GetComponentData<Owner>(e).m_Owner;
                    if (Em.Exists(o) && Em.HasComponent<Building>(o)) ownedByBuilding++;
                    n += " (owned)";
                }
                byPrefab[n] = byPrefab.TryGetValue(n, out int c) ? c + 1 : 1;
            }
            var nodes = Query(new[] { ComponentType.ReadOnly<Game.Net.Node>() });
            var buildings = Query(new[] { ComponentType.ReadOnly<Building>() });
            var bByPrefab = new Dictionary<string, int>();
            foreach (var b in buildings)
            {
                string n = PrefabName(Em.GetComponentData<PrefabRef>(b).m_Prefab);
                bByPrefab[n] = bByPrefab.TryGetValue(n, out int c) ? c + 1 : 1;
            }

            var t = W.GetOrCreateSystemManaged<TerrainSystem>();
            var data = t.GetHeightData(true);
            float hmin = float.MaxValue, hmax = float.MinValue, centre = 0;
            if (data.isCreated)
            {
                float half = t.playableArea.x / 2 - 100;
                for (int i = 0; i < grid; i++)
                    for (int j = 0; j < grid; j++)
                    {
                        float x = -half + 2 * half * i / (grid - 1), z = -half + 2 * half * j / (grid - 1);
                        float h = TerrainUtils.SampleHeight(ref data, new float3(x, 0, z));
                        hmin = math.min(hmin, h); hmax = math.max(hmax, h);
                    }
                centre = TerrainUtils.SampleHeight(ref data, float3.zero);
            }
            var water = W.GetOrCreateSystemManaged<WaterSystem>();
            int sources = Query(new[] { ComponentType.ReadOnly<Game.Simulation.WaterSourceData>() }).Length;
            var r = new Dictionary<string, object>
            {
                { "edges", edges.Length }, { "edgesOwned", owned }, { "edgesOwnedByBuildings", ownedByBuilding }, { "nodes", nodes.Length },
                { "edgesByPrefab", Top(byPrefab, 25) },
                { "buildings", buildings.Length }, { "buildingsByPrefab", Top(bByPrefab, 25) },
                { "outsideConnections", OcList() },
                { "terrain", new Dictionary<string, object> { { "min", hmin }, { "max", hmax }, { "centre", centre }, { "grid", grid } } },
                { "water", new Dictionary<string, object> { { "seaLevel", water.SeaLevel }, { "sources", sources } } },
            };
            edges.Dispose(); nodes.Dispose(); buildings.Dispose();
            return r;
        }

        static List<object> Top(Dictionary<string, int> d, int n)
        {
            var l = new List<KeyValuePair<string, int>>(d);
            l.Sort((p, q) => q.Value.CompareTo(p.Value));
            var r = new List<object>();
            for (int i = 0; i < l.Count && i < n; i++) r.Add(new List<object> { l[i].Key, l[i].Value });
            return r;
        }

        // ---------------------------------------------------------------- outside connections

        static object OcPrefabs()
        {
            var r = new List<object>();
            var ps = Query(new[] { ComponentType.ReadOnly<OutsideConnectionData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var p in ps)
            {
                var d = Em.GetComponentData<OutsideConnectionData>(p);
                var item = new Dictionary<string, object>
                {
                    { "name", PrefabName(p) }, { "type", d.m_Type.ToString() }, { "remoteness", d.m_Remoteness },
                    { "isObject", Em.HasComponent<ObjectData>(p) }, { "isNet", Em.HasComponent<NetData>(p) },
                };
                // utility markers (electricity / water pipe): the prefab's own components say which
                var ct = Em.GetComponentTypes(p, Allocator.Temp);
                var util = new List<object>();
                foreach (var t in ct) { var n = t.GetManagedType()?.Name; if (n != null && (n.Contains("Electricity") || n.Contains("WaterPipe") || n.Contains("Sewage"))) util.Add(n); }
                ct.Dispose();
                if (util.Count > 0) item["utility"] = util;
                if (Em.HasComponent<RouteConnectionData>(p))
                {
                    var rc = Em.GetComponentData<RouteConnectionData>(p);
                    item["access"] = rc.m_AccessConnectionType.ToString() + "/" + rc.m_AccessRoadType + "/" + rc.m_AccessTrackType;
                    item["route"] = rc.m_RouteConnectionType.ToString() + "/" + rc.m_RouteRoadType + "/" + rc.m_RouteTrackType;
                }
                r.Add(item);
            }
            ps.Dispose();
            return new Dictionary<string, object> { { "prefabs", r } };
        }

        static List<object> OcList()
        {
            var r = new List<object>();
            var all = new List<Entity>(); var seen = new HashSet<Entity>();
            foreach (var ct in new[] { ComponentType.ReadOnly<Game.Objects.OutsideConnection>(), ComponentType.ReadOnly<Game.Objects.ElectricityOutsideConnection>(), ComponentType.ReadOnly<Game.Objects.WaterPipeOutsideConnection>() })
            {
                var q = Query(new[] { ct });
                foreach (var e in q) if (seen.Add(e)) all.Add(e);
                q.Dispose();
            }
            foreach (var e in all)
            {
                var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                var item = new Dictionary<string, object> { { "entity", Id(e) }, { "prefab", PrefabName(prefab) } };
                if (Em.HasComponent<OutsideConnectionData>(prefab)) item["type"] = Em.GetComponentData<OutsideConnectionData>(prefab).m_Type.ToString();
                if (Em.HasComponent<Game.Objects.Transform>(e)) item["pos"] = V(Em.GetComponentData<Game.Objects.Transform>(e).m_Position);
                if (Em.HasComponent<Game.Net.Node>(e)) item["isNode"] = true;
                if (Em.HasComponent<Game.Objects.ElectricityOutsideConnection>(e)) item["electricity"] = true;
                if (Em.HasComponent<Game.Objects.WaterPipeOutsideConnection>(e)) item["waterPipe"] = true;
                if (Em.HasComponent<Owner>(e))
                {
                    var o = Em.GetComponentData<Owner>(e).m_Owner;
                    item["owner"] = Id(o);
                    if (Em.Exists(o))
                    {
                        item["ownerPrefab"] = Em.HasComponent<PrefabRef>(o) ? PrefabName(Em.GetComponentData<PrefabRef>(o).m_Prefab) : null;
                        item["ownerIsNode"] = Em.HasComponent<Game.Net.Node>(o);
                        if (Em.HasBuffer<ConnectedEdge>(o)) item["ownerEdges"] = Em.GetBuffer<ConnectedEdge>(o, true).Length;
                    }
                }
                if (Em.HasBuffer<Game.Net.SubLane>(e)) item["subLanes"] = Em.GetBuffer<Game.Net.SubLane>(e, true).Length;
                r.Add(item);
            }
            return r;
        }

        static object OcCreate(Dictionary<string, object> a)
        {
            float radius = F(a, "radius", 3f);
            var res = new List<object>();
            if (!a.TryGetValue("connections", out var co) || !(co is IList<object> list)) throw new ArgumentException("connections: [{x,z,type}] required");
            // index of real nodes
            var nodes = Query(new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<ConnectedEdge>() });
            var pos = new List<KeyValuePair<Entity, float3>>();
            foreach (var n in nodes) if (!Em.HasComponent<Owner>(n)) pos.Add(new KeyValuePair<Entity, float3>(n, Em.GetComponentData<Game.Net.Node>(n).m_Position));
            nodes.Dispose();
            var markers = MarkerPrefabs();
            foreach (var o in list)
            {
                var c = o as Dictionary<string, object>;
                float2 p = new float2(F(c, "x"), F(c, "z"));
                string type = Str(c, "type", "road");
                string prefabName = Str(c, "prefab", null);
                var item = new Dictionary<string, object> { { "x", p.x }, { "z", p.y }, { "type", type } };
                res.Add(item);
                Entity node = Entity.Null; float best = radius;
                float3 np = default;
                foreach (var kv in pos)
                {
                    float d = math.distance(kv.Value.xz, p);
                    if (d < best) { best = d; node = kv.Key; np = kv.Value; }
                }
                if (node == Entity.Null) { item["error"] = "no node within " + radius + " m"; continue; }
                Entity prefab = Entity.Null;
                if (prefabName != null) { foreach (var m in markers) if (m.Key == prefabName) prefab = m.Value.Entity; }
                else foreach (var m in markers) if (m.Value.Type == (type == "train" ? OutsideConnectionTransferType.Train : OutsideConnectionTransferType.Road) && m.Value.Entity != Entity.Null && prefab == Entity.Null) prefab = m.Value.Entity;
                if (prefab == Entity.Null) { item["error"] = "no marker prefab for " + (prefabName ?? type); continue; }

                var def = Em.CreateEntity();
                Em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_Owner = node, m_Flags = CreationFlags.Permanent });
                Em.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = np, m_LocalPosition = float3.zero, m_Scale = new float3(1f), m_Rotation = quaternion.identity, m_LocalRotation = quaternion.identity,
                    m_Intensity = 1f, m_ParentMesh = -1, m_GroupIndex = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                });
                Em.AddComponent<Updated>(def);
                Em.AddComponent<Deleted>(def); // the definition itself is cleaned up at the end of the frame
                item["node"] = Id(node); item["nodeDistance"] = best; item["prefab"] = PrefabName(prefab); item["nodeEdges"] = Em.GetBuffer<ConnectedEdge>(node, true).Length;
            }
            return new Dictionary<string, object> { { "op", "oc.create" }, { "results", res }, { "frame", UnityEngine.Time.frameCount } };
        }

        struct Marker { public Entity Entity; public OutsideConnectionTransferType Type; }

        /// <summary>Object prefabs with OutsideConnectionData, by name. Road markers sort before others of the same type
        /// by name, so the choice is deterministic.</summary>
        static SortedDictionary<string, Marker> MarkerPrefabs()
        {
            var r = new SortedDictionary<string, Marker>(StringComparer.Ordinal);
            var ps = Query(new[] { ComponentType.ReadOnly<OutsideConnectionData>(), ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var p in ps) r[PrefabName(p)] = new Marker { Entity = p, Type = Em.GetComponentData<OutsideConnectionData>(p).m_Type };
            ps.Dispose();
            return r;
        }

        // ---------------------------------------------------------------- clear

        /// <summary>
        /// Deletes the base map's road and track nets (the ones that hold the terrain up) with the importer's pattern:
        /// Deleted on the edges and their sub-objects, one frame, then the end nodes that have no edge left (with their
        /// sub-objects, which include road/train outside-connection markers), Updated on the rest. Powerlines, seaways,
        /// pipes and their outside connections stay (a blanket delete of every net crashed CS2 natively).
        /// </summary>
        static System.Collections.IEnumerator Clear(int frames, bool all, bool buildings)
        {
            var before = Survey(5) as Dictionary<string, object>;
            int edgesDeleted = 0, nodesDeleted = 0, nodesKept = 0, keptOwned = 0, keptOther = 0;
            var edges = Query(new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PrefabRef>() });
            var ends = new HashSet<Entity>();
            var doomed = new List<Entity>();
            foreach (var e in edges)
            {
                if (Em.HasComponent<Owner>(e)) { keptOwned++; continue; }
                var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!all && !Em.HasComponent<RoadData>(prefab) && !Em.HasComponent<TrackData>(prefab)) { keptOther++; continue; }
                doomed.Add(e);
                var ed = Em.GetComponentData<Edge>(e);
                ends.Add(ed.m_Start); ends.Add(ed.m_End);
            }
            edges.Dispose();
            foreach (var e in doomed) { Delete(e); edgesDeleted++; }
            // base-map buildings (landmarks such as ruins) keep their old height like nets do, and their lots raise the
            // new terrain into pillars: remove the top-level ones (at clear time, every building is the base map's)
            int buildingsDeleted = 0;
            if (buildings)
            {
                var bs = Query(new[] { ComponentType.ReadOnly<Building>() });
                foreach (var e in bs) { if (Em.HasComponent<Owner>(e)) continue; Delete(e); buildingsDeleted++; }
                bs.Dispose();
            }
            yield return null;
            foreach (var n in ends)
            {
                if (!IsReal(n)) continue;
                var left = new List<Entity>();
                if (Em.HasBuffer<ConnectedEdge>(n))
                {
                    var buf = Em.GetBuffer<ConnectedEdge>(n, true);
                    for (int i = 0; i < buf.Length; i++) if (IsReal(buf[i].m_Edge)) left.Add(buf[i].m_Edge);
                }
                if (left.Count == 0) { Delete(n); nodesDeleted++; continue; }
                nodesKept++;
                Em.AddComponent<Updated>(n);
                foreach (var e in left) Em.AddComponent<Updated>(e);
            }
            for (int i = 0; i < frames; i++) yield return null;
            var t = W.GetOrCreateSystemManaged<TerrainSystem>();
            var b = new Colossal.Mathematics.Bounds2(t.playableOffset, t.playableOffset + t.playableArea);
            t.OnAreaChanged(in b); // the terrain under the removed nets is no longer held up
            var after = Survey(5) as Dictionary<string, object>;
            yield return new Dictionary<string, object>
            {
                { "op", "clear" }, { "all", all }, { "buildingsDeleted", buildingsDeleted }, { "edgesDeleted", edgesDeleted }, { "nodesDeleted", nodesDeleted }, { "nodesKept", nodesKept },
                { "keptOwnedEdges", keptOwned }, { "keptOtherEdges", keptOther },
                { "before", Pick(before, "edges", "nodes", "edgesByPrefab") },
                { "after", Pick(after, "edges", "nodes", "edgesByPrefab", "buildings", "outsideConnections") },
            };
        }

        static Dictionary<string, object> Pick(Dictionary<string, object> d, params string[] keys)
        {
            var r = new Dictionary<string, object>();
            foreach (var k in keys) if (d.TryGetValue(k, out var v)) r[k] = v;
            return r;
        }

        /// <summary>Deleted on the entity and its sub-objects (Road Builder's / the importer's pattern).</summary>
        static void Delete(Entity e)
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

        // ---------------------------------------------------------------- water

        static object WaterReset(string mode)
        {
            var w = W.GetOrCreateSystemManaged<WaterSystem>();
            if (mode == "empty") w.Reset(); else w.ResetToSealevel();
            return new Dictionary<string, object> { { "op", "water.reset" }, { "mode", mode }, { "seaLevel", w.SeaLevel } };
        }

        static object WaterSet(Dictionary<string, object> a)
        {
            var w = W.GetOrCreateSystemManaged<WaterSystem>();
            int removed = 0, created = 0;
            if (!a.ContainsKey("clear") || B(a, "clear"))
            {
                var src = Query(new[] { ComponentType.ReadOnly<Game.Simulation.WaterSourceData>() });
                foreach (var s in src) { Em.AddComponent<Deleted>(s); removed++; }
                src.Dispose();
            }
            if (a.ContainsKey("seaLevel") && a["seaLevel"] != null) w.SeaLevel = F(a, "seaLevel");
            var t = W.GetOrCreateSystemManaged<TerrainSystem>();
            var data = t.GetHeightData(true);
            if (a.TryGetValue("sources", out var so) && so is IList<object> list)
            {
                int id = 1000;
                var all = Query(new[] { ComponentType.ReadOnly<Game.Simulation.WaterSourceData>() }, new ComponentType[0]);
                foreach (var ex in all) id = Math.Max(id, Em.GetComponentData<Game.Simulation.WaterSourceData>(ex).m_Id + 1);
                all.Dispose();
                foreach (var o in list)
                {
                    var c = o as Dictionary<string, object>;
                    float x = F(c, "x"), z = F(c, "z");
                    float y = c.ContainsKey("y") && c["y"] != null ? F(c, "y") : TerrainUtils.SampleHeight(ref data, new float3(x, 0, z));
                    // GenerateWaterSourcesSystem's archetype always contains Temp (only ApplyWaterSourcesSystem, in the
                    // ApplyTool phase of an active tool, makes sources real), so a Permanent definition still yields a
                    // Temp source that the next tool clear removes. Create the source like the generator does, minus Temp.
                    var e = Em.CreateEntity(ComponentType.ReadWrite<Game.Simulation.WaterSourceData>(), ComponentType.ReadWrite<Game.Objects.Transform>(),
                        ComponentType.ReadWrite<Created>(), ComponentType.ReadWrite<Updated>());
                    Em.SetComponentData(e, new Game.Simulation.WaterSourceData
                    {
                        m_ConstantDepth = (int)F(c, "constantDepth", 0f), m_Radius = F(c, "radius", 20f), m_Polluted = F(c, "polluted", 0f),
                        m_Height = F(c, "height", 5f), m_Id = id++, m_Modifier = 1f, m_Multiplier = F(c, "multiplier", 0f),
                    });
                    Em.SetComponentData(e, new Game.Objects.Transform { m_Position = new float3(x, y, z), m_Rotation = quaternion.identity });
                    created++;
                }
            }
            return new Dictionary<string, object> { { "op", "water.set" }, { "removed", removed }, { "created", created }, { "seaLevel", w.SeaLevel } };
        }

        static object Counts()
        {
            int N<T>() { var q = Query(new[] { ComponentType.ReadOnly<T>() }); int n = q.Length; q.Dispose(); return n; }
            return new Dictionary<string, object>
            {
                { "vehicles", N<Game.Vehicles.Vehicle>() }, { "cars", N<Game.Vehicles.Car>() }, { "trains", N<Game.Vehicles.Train>() },
                { "personalCars", N<Game.Vehicles.PersonalCar>() }, { "deliveryTrucks", N<Game.Vehicles.DeliveryTruck>() },
                { "cargoTransports", N<Game.Vehicles.CargoTransport>() },
                { "outsideConnectionLanes", N<Game.Net.OutsideConnection>() },
                { "outsideConnectionObjects", N<Game.Objects.OutsideConnection>() },
                { "frame", UnityEngine.Time.frameCount },
            };
        }

        static object WaterSample(Dictionary<string, object> a)
        {
            var w = W.GetOrCreateSystemManaged<WaterSystem>();
            var deps = default(Unity.Jobs.JobHandle);
            var sd = w.GetSurfaceData(out deps);
            deps.Complete();
            var res = new List<object>();
            if (a.TryGetValue("points", out var po) && po is IList<object> pts)
                foreach (var p in pts)
                    if (p is IList<object> xz && xz.Count >= 2)
                    {
                        float x = Convert.ToSingle(xz[0]), z = Convert.ToSingle(xz[1]);
                        res.Add(new List<object> { x, z, WaterUtils.SampleDepth(ref sd, new float3(x, 0, z)) });
                    }
            return new Dictionary<string, object> { { "seaLevel", w.SeaLevel }, { "points", res } };
        }

        // ---------------------------------------------------------------- helpers

        static string Id(Entity e) => e.Index + ":" + e.Version;
        static List<object> V(float3 v) => new List<object> { v.x, v.y, v.z };
        static string Str(Dictionary<string, object> a, string k, string d) => a != null && a.TryGetValue(k, out var v) && v != null ? v.ToString() : d;
        static float F(Dictionary<string, object> a, string k, float d = 0f) => a != null && a.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v) : d;
        static bool B(Dictionary<string, object> a, string k) => a != null && a.TryGetValue(k, out var v) && v is bool b && b;
    }
}
