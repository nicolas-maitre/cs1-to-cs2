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
//   oc.utilities {kinds=["electricity","water"], mode="stub"|"node", stubLength=24 (pipes +8), perSide=1, minNodes=8, max=8,
//                 nodes?:[{x,z}], radius=3, dryRun=false}                       [phase + pump tool; a coroutine]
//                                                 electricity and water-pipe outside connections (docs/DECISIONS.md #136)
//                                                 at imported road nodes near the edge of the road network, one per road
//                                                 island (+ one per side for the largest). Roads carry low voltage and
//                                                 pipes, and the game's graph systems link a marker's owner node to the
//                                                 trade source/sink. mode "stub": a short "Low-voltage Line" / "Combined
//                                                 Small Pipe" from the road node outward, the marker on its far end node
//                                                 (a node nothing else touches). mode "node": the marker on the road node
//                                                 itself, which SubObjectSystem deletes as soon as the node is updated
//                                                 (a building growing beside it): kept for diagnostics only.
//   node.info   {ids:["index:version",…]}         diagnostics: does each entity still exist, its edges and sub-objects
//   water.reset {mode:"sea"|"empty"="sea"}        WaterSystem.ResetToSealevel() / Reset() (the editor's buttons)
//   counts      {}                                vehicles (cars, trains, personal, delivery, cargo) and outside-connection
//                                                 lanes, to see whether through traffic spawns
//   water.sample {points:[[x,z],...]}             water depth (m) at each point, from the water surface readback
//   water.set   {seaLevel?, clear=true, sources:[{x,z,y?,height,radius,polluted=0,constantDepth=0}]}  [phase tool]
//                                                 replaces the water sources (entities created directly: a definition
//                                                 only ever gives a Temp source, see NOTES §8)
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
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
                case "oc.utilities": return OcUtilities(a);
                case "node.info": return NodeInfo(a);
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

        // ---------------------------------------------------------------- utility outside connections (#136)

        /// <summary>
        /// Object prefabs whose archetype has Game.Objects.ElectricityOutsideConnection / WaterPipeOutsideConnection
        /// ("Powerline Outside Connection" and the water-pipe one). They carry no OutsideConnectionData, so oc.prefabs
        /// doesn't list them. Sorted by name, so the choice is deterministic.
        /// </summary>
        static SortedDictionary<string, Entity> UtilityMarkerPrefabs(ComponentType tag)
        {
            var r = new SortedDictionary<string, Entity>(StringComparer.Ordinal);
            var ps = Query(new[] { ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var p in ps)
            {
                var od = Em.GetComponentData<ObjectData>(p);
                if (!od.m_Archetype.Valid) continue;
                var types = od.m_Archetype.GetComponentTypes(Allocator.Temp);
                bool has = false;
                foreach (var t in types) if (t.TypeIndex == tag.TypeIndex) { has = true; break; }
                types.Dispose();
                if (has) r[PrefabName(p)] = p;
            }
            ps.Dispose();
            return r;
        }

        /// <summary>True when the node already owns a marker object with this tag (makes oc.utilities idempotent).</summary>
        static bool OwnsMarker(Entity node, ComponentType tag)
        {
            if (!Em.HasBuffer<Game.Objects.SubObject>(node)) return false;
            var buf = Em.GetBuffer<Game.Objects.SubObject>(node, true);
            for (int i = 0; i < buf.Length; i++)
            {
                var s = buf[i].m_SubObject;
                if (IsReal(s) && Em.HasComponent(s, tag)) return true;
            }
            return false;
        }

        struct UtilityPick { public string Kind; public ComponentType Conn; public Entity Marker; public Entity Node; public float3 Pos; public Dictionary<string, object> Item; public float3 End; }

        static System.Collections.IEnumerator OcUtilities(Dictionary<string, object> a)
        {
            string mode = Str(a, "mode", "stub");
            float stubLength = F(a, "stubLength", 24f);
            var pending = new List<UtilityPick>();
            var kinds = new List<string>();
            if (a.TryGetValue("kinds", out var ko) && ko is IList<object> kl) foreach (var k in kl) kinds.Add(k.ToString());
            else { kinds.Add("electricity"); kinds.Add("water"); }
            int perSide = (int)F(a, "perSide", 1f), minNodes = (int)F(a, "minNodes", 8f), max = (int)F(a, "max", 8f);
            float radius = F(a, "radius", 3f);
            bool dry = B(a, "dryRun");
            var explicitNodes = a.TryGetValue("nodes", out var no) ? no as IList<object> : null;

            // top-level road nodes (the imported network), with their positions
            var nodes = Query(new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<ConnectedEdge>() });
            var roadNodes = new List<Entity>();
            foreach (var n in nodes)
            {
                if (Em.HasComponent<Owner>(n)) continue;
                var buf = Em.GetBuffer<ConnectedEdge>(n, true);
                bool road = false;
                for (int i = 0; i < buf.Length && !road; i++)
                {
                    var e = buf[i].m_Edge;
                    if (IsReal(e) && !Em.HasComponent<Owner>(e) && Em.HasComponent<RoadData>(Em.GetComponentData<PrefabRef>(e).m_Prefab)) road = true;
                }
                if (road) roadNodes.Add(n);
            }
            nodes.Dispose();

            var result = new Dictionary<string, object> { { "op", "oc.utilities" }, { "roadNodes", roadNodes.Count }, { "dryRun", dry } };
            foreach (var kind in kinds)
            {
                bool elec = kind == "electricity";
                if (!elec && kind != "water") { result[kind] = new Dictionary<string, object> { { "error", "unknown kind (electricity, water)" } }; continue; }
                var tag = elec ? ComponentType.ReadOnly<Game.Objects.ElectricityOutsideConnection>() : ComponentType.ReadOnly<Game.Objects.WaterPipeOutsideConnection>();
                var conn = elec ? ComponentType.ReadOnly<Game.Simulation.ElectricityNodeConnection>() : ComponentType.ReadOnly<Game.Simulation.WaterPipeNodeConnection>();
                var info = new Dictionary<string, object>();
                result[kind] = info;
                var prefabs = UtilityMarkerPrefabs(tag);
                info["prefabs"] = new List<object>(prefabs.Keys);
                Entity prefab = Entity.Null;
                foreach (var kv in prefabs) { prefab = kv.Value; info["prefab"] = kv.Key; break; }
                if (prefab == Entity.Null) { info["error"] = "no marker prefab"; continue; }

                // candidates: road nodes linked into this utility's flow graph
                var cand = new List<Entity>();
                var index = new Dictionary<Entity, int>();
                foreach (var n in roadNodes) if (Em.HasComponent(n, conn)) { index[n] = cand.Count; cand.Add(n); }
                info["candidates"] = cand.Count;
                if (cand.Count == 0) { info["error"] = "no road node has a " + (elec ? "electricity" : "water pipe") + " connection"; continue; }
                var pos = new float3[cand.Count];
                for (int i = 0; i < cand.Count; i++) pos[i] = Em.GetComponentData<Game.Net.Node>(cand[i]).m_Position;

                var picks = new List<KeyValuePair<int, string>>(); // candidate index, why
                if (explicitNodes != null)
                {
                    foreach (var o in explicitNodes)
                    {
                        var c = o as Dictionary<string, object>;
                        float2 p = new float2(F(c, "x"), F(c, "z"));
                        int best = -1; float bd = radius;
                        for (int i = 0; i < cand.Count; i++) { float d = math.distance(pos[i].xz, p); if (d < bd) { bd = d; best = i; } }
                        if (best >= 0) picks.Add(new KeyValuePair<int, string>(best, "requested"));
                    }
                }
                else
                {
                    // road islands: union-find over road edges between candidates
                    var parent = new int[cand.Count];
                    for (int i = 0; i < parent.Length; i++) parent[i] = i;
                    int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                    for (int i = 0; i < cand.Count; i++)
                    {
                        var buf = Em.GetBuffer<ConnectedEdge>(cand[i], true);
                        for (int j = 0; j < buf.Length; j++)
                        {
                            var e = buf[j].m_Edge;
                            if (!IsReal(e) || !Em.HasComponent<Edge>(e)) continue;
                            var ed = Em.GetComponentData<Edge>(e);
                            var other = ed.m_Start == cand[i] ? ed.m_End : ed.m_Start;
                            if (index.TryGetValue(other, out int k)) { int ra = Find(i), rb = Find(k); if (ra != rb) parent[math.max(ra, rb)] = math.min(ra, rb); }
                        }
                    }
                    var comps = new Dictionary<int, List<int>>();
                    for (int i = 0; i < cand.Count; i++) { int r = Find(i); if (!comps.TryGetValue(r, out var l)) comps[r] = l = new List<int>(); l.Add(i); }
                    var order = new List<List<int>>(comps.Values);
                    // largest island first; ties by the lowest x, then z, of the first node (stable across runs)
                    order.Sort((p, q) => p.Count != q.Count ? q.Count.CompareTo(p.Count) : Key(pos, p).CompareTo(Key(pos, q)));
                    // the network's bounding box stands in for the crop edge
                    float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                    foreach (var p in pos) { minX = math.min(minX, p.x); maxX = math.max(maxX, p.x); minZ = math.min(minZ, p.z); maxZ = math.max(maxZ, p.z); }
                    info["bounds"] = new List<object> { minX, minZ, maxX, maxZ };
                    info["islands"] = order.Count;
                    int big = 0;
                    for (int ci = 0; ci < order.Count; ci++)
                    {
                        var comp = order[ci];
                        if (comp.Count < minNodes) continue;
                        big++;
                        // the island's node nearest the network edge
                        int best = comp[0]; float bd = float.MaxValue;
                        foreach (int i in comp) { float d = EdgeDistance(pos[i], minX, maxX, minZ, maxZ); if (d < bd - 1e-3f) { bd = d; best = i; } }
                        picks.Add(new KeyValuePair<int, string>(best, "island " + ci + " (" + comp.Count + " nodes)"));
                        if (ci == 0 && perSide > 0)
                        {
                            // the main grid also gets its westmost, eastmost, southmost and northmost node
                            string[] sides = { "west", "east", "south", "north" };
                            for (int s = 0; s < 4; s++)
                            {
                                var sorted = new List<int>(comp);
                                sorted.Sort((p, q) => SideKey(pos[p], s).CompareTo(SideKey(pos[q], s)));
                                for (int t = 0; t < perSide && t < sorted.Count; t++) picks.Add(new KeyValuePair<int, string>(sorted[t], "main grid, " + sides[s]));
                            }
                        }
                    }
                    info["islandsUsed"] = big;
                }

                // existing markers of this kind: a re-run adds none within reach of them
                var existing = new List<float3>();
                var ex = Query(new[] { tag, ComponentType.ReadOnly<Game.Objects.Transform>() });
                foreach (var e in ex) existing.Add(Em.GetComponentData<Game.Objects.Transform>(e).m_Position);
                ex.Dispose();
                float cx = 0, cz = 0;
                foreach (var p in pos) { cx += p.x; cz += p.z; }
                cx /= pos.Length; cz /= pos.Length;

                var made = new List<object>();
                var used = new HashSet<int>();
                int skippedOwned = 0;
                foreach (var pk in picks)
                {
                    if (made.Count >= max) break;
                    if (!used.Add(pk.Key)) continue;
                    var node = cand[pk.Key];
                    bool near = OwnsMarker(node, tag);
                    foreach (var q in existing) if (math.distance(q.xz, pos[pk.Key].xz) < stubLength + 16f) near = true;
                    if (near) { skippedOwned++; continue; }
                    var item = new Dictionary<string, object> { { "node", Id(node) }, { "pos", V(pos[pk.Key]) }, { "why", pk.Value } };
                    made.Add(item);
                    // outward: away from the network's centre of mass (the picks are at its edge)
                    var dir = pos[pk.Key].xz - new float2(cx, cz);
                    dir = math.lengthsq(dir) < 1f ? new float2(1, 0) : math.normalize(dir);
                    pending.Add(new UtilityPick { Kind = kind, Conn = conn, Marker = prefab, Node = node, Pos = pos[pk.Key], Item = item,
                        // pipes 8 m longer, so a line's and a pipe's far nodes never share a spot
                        End = pos[pk.Key] + new float3(dir.x, 0, dir.y) * (stubLength + (elec ? 0f : 8f)) });
                }
                info["created"] = 0;
                info["alreadyThere"] = skippedOwned;
                info["markers"] = made;
            }
            result["mode"] = mode;
            if (dry || pending.Count == 0) { result["frame"] = UnityEngine.Time.frameCount; yield return result; yield break; }

            if (mode == "node")
            {
                foreach (var p in pending) { CreateMarker(p.Marker, p.Node, p.Pos); p.Item["owner"] = Id(p.Node); }
            }
            else
            {
                // mode "stub": the nets first; CS2 connects a line or pipe end locally to the road it starts on
                var t = W.GetOrCreateSystemManaged<TerrainSystem>();
                var heights = t.GetHeightData(true);
                var stubPrefabs = new Dictionary<string, Entity>();
                var stubElevation = new Dictionary<string, float>();
                var ids = new Dictionary<string, PrefabID>
                {
                    { "electricity", new PrefabID("PowerLinePrefab", Str(a, "powerPrefab", "Low-voltage Line")) },
                    { "water", new PrefabID("PipelinePrefab", Str(a, "pipePrefab", "Combined Small Pipe")) },
                };
                foreach (var kv in ids)
                {
                    if (!Prefabs.TryGetPrefab(kv.Value, out PrefabBase pb) || !Prefabs.TryGetEntity(pb, out Entity pe)) continue;
                    stubPrefabs[kv.Key] = pe;
                    // the elevation the net tool would use: 0 if allowed, else the nearest end of the prefab's range (pipes)
                    float el = 0f;
                    if (Em.HasComponent<PlaceableNetData>(pe)) { var r = Em.GetComponentData<PlaceableNetData>(pe).m_ElevationRange; el = math.clamp(0f, r.min, r.max); }
                    stubElevation[kv.Key] = el;
                }
                var rnd = new Unity.Mathematics.Random(0x0c136u);
                for (int i = 0; i < pending.Count; i++)
                {
                    var p = pending[i];
                    if (!stubPrefabs.TryGetValue(p.Kind, out var sp)) { p.Item["error"] = "no stub net prefab"; continue; }
                    float el = stubElevation[p.Kind];
                    var a0 = new float3(p.Pos.x, TerrainUtils.SampleHeight(ref heights, p.Pos) + el, p.Pos.z);
                    var d0 = new float3(p.End.x, TerrainUtils.SampleHeight(ref heights, p.End) + el, p.End.z);
                    var curve = NetUtils.StraightCurve(a0, d0);
                    var def = Em.CreateEntity();
                    Em.AddComponentData(def, new CreationDefinition { m_Prefab = sp, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.Permanent | CreationFlags.SubElevation });
                    Em.AddComponentData(def, new NetCourse
                    {
                        m_Curve = curve,
                        m_StartPosition = CoursePoint(curve, a0, 0f, el, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight),
                        m_EndPosition = CoursePoint(curve, d0, 1f, el, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight),
                        m_Elevation = new float2(el), m_Length = MathUtils.Length(curve), m_FixedIndex = -1,
                    });
                    Em.AddComponent<Updated>(def);
                    Em.AddComponent<Deleted>(def);
                    p.Item["stub"] = PrefabName(sp);
                    p.Item["stubEnd"] = V(d0);
                    p.End = d0;
                    pending[i] = p;
                }
                // then, once a stub's far node exists and is in its utility's flow graph, the marker on it
                var done = new bool[pending.Count];
                int frames = 0;
                for (; frames < 240; frames++)
                {
                    yield return null;
                    bool all = true;
                    var ends = Query(new[] { ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<ConnectedEdge>() });
                    for (int i = 0; i < pending.Count; i++)
                    {
                        var p = pending[i];
                        if (done[i] || p.Item.ContainsKey("error")) continue;
                        Entity hit = Entity.Null;
                        foreach (var n in ends)
                        {
                            if (Em.HasComponent<Owner>(n) || !Em.HasComponent(n, p.Conn)) continue;
                            if (math.distance(Em.GetComponentData<Game.Net.Node>(n).m_Position.xz, p.End.xz) > 1.5f) continue;
                            hit = n; break;
                        }
                        if (hit == Entity.Null) { all = false; continue; }
                        CreateMarker(p.Marker, hit, Em.GetComponentData<Game.Net.Node>(hit).m_Position);
                        p.Item["owner"] = Id(hit);
                        p.Item["ownerEdges"] = Em.GetBuffer<ConnectedEdge>(hit, true).Length;
                        p.Item["frames"] = frames;
                        done[i] = true;
                    }
                    ends.Dispose();
                    if (all) break;
                }
                result["frames"] = frames;
                var left = Query(new[] { ComponentType.ReadOnly<Game.Net.Node>() });
                for (int i = 0; i < pending.Count; i++)
                {
                    if (done[i] || pending[i].Item.ContainsKey("error")) continue;
                    // diagnostics: the nearest node to the stub's end and what it has
                    Entity near = Entity.Null; float nd = 50f;
                    foreach (var n in left) { float d = math.distance(Em.GetComponentData<Game.Net.Node>(n).m_Position.xz, pending[i].End.xz); if (d < nd) { nd = d; near = n; } }
                    pending[i].Item["error"] = near == Entity.Null ? "no node within 50 m of the stub end (stub not built)" : "stub end node not in the flow graph";
                    if (near != Entity.Null)
                    {
                        var comps = new List<object>();
                        var ct = Em.GetComponentTypes(near, Allocator.Temp);
                        foreach (var c in ct) { var tn = c.GetManagedType()?.Name; if (tn != null && (tn.Contains("Electric") || tn.Contains("Water") || tn.Contains("Owner") || tn.Contains("Edge"))) comps.Add(tn); }
                        ct.Dispose();
                        pending[i].Item["nearest"] = new Dictionary<string, object> { { "id", Id(near) }, { "distance", nd }, { "prefab", Em.HasComponent<PrefabRef>(near) ? PrefabName(Em.GetComponentData<PrefabRef>(near).m_Prefab) : null }, { "components", comps } };
                    }
                }
                left.Dispose();
            }
            foreach (var kind in kinds)
                if (result.TryGetValue(kind, out var io) && io is Dictionary<string, object> d && d.TryGetValue("markers", out var mo))
                {
                    int c = 0;
                    foreach (var m in (List<object>)mo) if (m is Dictionary<string, object> md && md.ContainsKey("owner")) c++;
                    d["created"] = c;
                }
            for (int i = 0; i < 10; i++) yield return null;
            result["frame"] = UnityEngine.Time.frameCount;
            yield return result;
        }

        static CoursePos CoursePoint(Bezier4x3 curve, float3 pos, float t, float elevation, CoursePosFlags flags)
        {
            return new CoursePos
            {
                m_Entity = Entity.Null, m_Position = pos, m_Elevation = new float2(elevation),
                m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t)), m_CourseDelta = t, m_ParentMesh = -1, m_Flags = flags,
            };
        }

        static void CreateMarker(Entity prefab, Entity node, float3 pos)
        {
            var def = Em.CreateEntity();
            Em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_Owner = node, m_Flags = CreationFlags.Permanent });
            Em.AddComponentData(def, new ObjectDefinition
            {
                m_Position = pos, m_LocalPosition = float3.zero, m_Scale = new float3(1f), m_Rotation = quaternion.identity, m_LocalRotation = quaternion.identity,
                m_Intensity = 1f, m_ParentMesh = -1, m_GroupIndex = -1, m_Probability = 100, m_PrefabSubIndex = -1,
            });
            Em.AddComponent<Updated>(def);
            Em.AddComponent<Deleted>(def);
        }

        /// <summary>Diagnostics: does each entity ("index:version") still exist, and what is it (node, edges, sub-objects).</summary>
        static object NodeInfo(Dictionary<string, object> a)
        {
            var res = new List<object>();
            if (a.TryGetValue("ids", out var io) && io is IList<object> ids)
                foreach (var o in ids)
                {
                    var parts = o.ToString().Split(':');
                    var e = new Entity { Index = int.Parse(parts[0]), Version = int.Parse(parts[1]) };
                    var item = new Dictionary<string, object> { { "id", o.ToString() }, { "exists", Em.Exists(e) } };
                    if (Em.Exists(e))
                    {
                        item["deleted"] = Em.HasComponent<Deleted>(e);
                        item["prefab"] = Em.HasComponent<PrefabRef>(e) ? PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab) : null;
                        if (Em.HasComponent<Game.Net.Node>(e)) item["pos"] = V(Em.GetComponentData<Game.Net.Node>(e).m_Position);
                        if (Em.HasBuffer<ConnectedEdge>(e)) item["edges"] = Em.GetBuffer<ConnectedEdge>(e, true).Length;
                        if (Em.HasBuffer<Game.Objects.SubObject>(e))
                        {
                            var subs = new List<object>();
                            var buf = Em.GetBuffer<Game.Objects.SubObject>(e, true);
                            for (int i = 0; i < buf.Length; i++) { var s = buf[i].m_SubObject; subs.Add(Em.Exists(s) && Em.HasComponent<PrefabRef>(s) ? PrefabName(Em.GetComponentData<PrefabRef>(s).m_Prefab) : "?"); }
                            item["subObjects"] = subs;
                        }
                        item["electricityNode"] = Em.HasComponent<Game.Simulation.ElectricityNodeConnection>(e);
                        item["waterPipeNode"] = Em.HasComponent<Game.Simulation.WaterPipeNodeConnection>(e);
                    }
                    else
                    {
                        // same index, newer version: the slot was reused (deleted and something else created)
                        item["note"] = "gone";
                    }
                    res.Add(item);
                }
            return new Dictionary<string, object> { { "nodes", res } };
        }

        static float Key(float3[] pos, List<int> comp) { float k = float.MaxValue; foreach (int i in comp) k = math.min(k, pos[i].x * 100000f + pos[i].z); return k; }
        static float EdgeDistance(float3 p, float minX, float maxX, float minZ, float maxZ) => math.min(math.min(p.x - minX, maxX - p.x), math.min(p.z - minZ, maxZ - p.z));
        static float SideKey(float3 p, int side) => side == 0 ? p.x : side == 1 ? -p.x : side == 2 ? p.z : -p.z;

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
