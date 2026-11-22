// CS1 transit lines → CS2 stops and transport lines, as a hot-loaded exec library for the CS2 bridge.
// Entry point: Cs1ConvTransit.Ops.Run(args), dispatched on args["op"]. transit/transit.mjs drives it.
// Background: transit/NOTES-cs2-transit.md.
//
// Ops (CS2 world metres):
//   prefabs      {}                                  line prefabs (RouteData + TransportLineData), stop prefabs
//                                                    (TransportStopData objects) and depot prefabs, with transport type
//   stops.create {stops:[{key,x,z,tx,tz,side,prefab}], inset=1.5, radius=12}   [phase tool]
//   stops.touch  {}                                  [phase tool] after the last stops.create batch: the stops' road
//                                                    edges (and nodes) Updated, so CS2 picks the stop composition now
//                one stop object per entry on the road edge nearest to (x,z), on `side` ("right" | "left" of the
//                direction (tx,tz)), as a permanent definition attached to that edge (CreationDefinition.m_Attached)
//   stops.list   {type?}                             TransportStop objects: prefab, type, position, owner, routes
//   lines.create {lines:[{key,name,number,prefab,color:"#rrggbb",type,stops:[{x,z}]}], radius=12, stationRadius=200,
//                 frames=40}                          [phase + pump tool]
//                frame 0: resolve each stop to a TransportStop object of the line's transport type, write one route
//                definition per line (CreationDefinition + ColorDefinition + WaypointDefinition per stop, connected to
//                the stop object). GenerateRoutesSystem only makes Temp routes (no Permanent path), so frame 1 switches
//                the active tool's applyMode to Apply for that frame: ToolOutputSystem then runs the vanilla ApplyTool
//                phase (ApplyRoutesSystem), exactly as a click with the line tool. Then names and numbers are set.
//   lines.repath {types?:["Train","Subway"]}       mark the lines' route segments Updated: RoutePathSystem then
//                                                    recomputes their paths (after the track network changed)
//   lines.name   {routes:[{route:"index:version",name,number}]}   [phase main] custom names + route numbers
//   lines.list   {}                                  routes: prefab, name, number, colour, stops, segments, complete,
//                                                    vehicles
//   counts       {}                                  public-transport vehicles by type, routes, stops, depots
//   yards.inspect / tracklink                       rail/subway yards → track link to the lines (TrackLink.cs)
//   infoviews    {} / infoview {name|null}           list the infoview prefabs / switch the view (screenshots of the
//                                                    transport overlay); name null or "" turns it off
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs1ConvTransit
{
    public static class Ops
    {
        public static object Run(Dictionary<string, object> a)
        {
            string op = Str(a, "op", "prefabs");
            switch (op)
            {
                case "prefabs": return PrefabsList();
                case "stops.create": return StopsCreate(a);
                case "stops.list": return StopsList(Str(a, "type", null));
                case "lines.create": return LinesCreate(a);
                case "lines.list": return LinesList();
                case "lines.name": return LinesName(a);
                case "lines.repath": return LinesRepath(a);
                case "lines.delete": return LinesDelete(a);
                case "lines.debug": return LinesDebug(Str(a, "name", null));
                case "node.at": return NodeAt(a);
                case "lines.reach": return TrackLink.Reach(Str(a, "name", null));
                case "lanes.reach": return TrackLink.LaneReach(Str(a, "name", null), Str(a, "track", "Subway"));
                case "lanes.joint": return TrackLink.LaneJoint(a);
                case "edges.at": return EdgesAt(a);
                case "buses": return Buses(Str(a, "match", "Bus"));
                case "depots.check": return DepotsCheck();
                case "road.reach": return TrackLink.RoadReach();
                case "requests.reset": return RequestsReset();
                case "lanes.sizes": return LaneSizes(Str(a, "name", null));
                case "depot.spawns": return DepotSpawns();
                case "utility.deleteNear": return UtilityDeleteNear(a);
                case "depot.rebuild": return DepotRebuild(a);
                case "lines.stoppos": return LineStopPositions(Str(a, "name", null));
                case "counts": return Counts();
                case "stops.touch": return StopsTouch();
                case "vehicles": return Vehicles(Str(a, "match", "Subway"));
                case "infoviews": return Infoviews(null);
                case "yards.inspect": return TrackLink.Inspect();
                case "yards.plan": return TrackLink.Plan(a);
                case "tracklink": return TrackLink.Link(a);
                case "infoview": return Infoviews(Str(a, "name", null));
                default: throw new ArgumentException("unknown op " + op);
            }
        }

        static World W => World.DefaultGameObjectInjectionWorld;
        static EntityManager Em => W.EntityManager;
        static PrefabSystem Prefabs => W.GetOrCreateSystemManaged<PrefabSystem>();
        static string PrefabName(Entity p) { try { return Prefabs.GetPrefabName(p); } catch { return "?"; } }
        static bool IsReal(Entity e) => e != Entity.Null && Em.Exists(e) && !Em.HasComponent<Temp>(e) && !Em.HasComponent<Deleted>(e);

        static NativeArray<Entity> Query(ComponentType[] all, ComponentType[] none = null)
        {
            var q = Em.CreateEntityQuery(new EntityQueryDesc { All = all, None = none ?? new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() } });
            var r = q.ToEntityArray(Allocator.Temp);
            q.Dispose();
            return r;
        }

        // ---------------------------------------------------------------- prefabs

        static object PrefabsList()
        {
            var lines = new List<object>();
            var ps = Query(new[] { ComponentType.ReadOnly<TransportLineData>(), ComponentType.ReadOnly<RouteData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var p in ps)
            {
                var t = Em.GetComponentData<TransportLineData>(p);
                lines.Add(new Dictionary<string, object> { { "name", PrefabName(p) }, { "type", t.m_TransportType.ToString() }, { "passenger", t.m_PassengerTransport }, { "cargo", t.m_CargoTransport } });
            }
            ps.Dispose();
            var stops = new List<object>();
            ps = Query(new[] { ComponentType.ReadOnly<TransportStopData>(), ComponentType.ReadOnly<ObjectData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var p in ps)
            {
                var t = Em.GetComponentData<TransportStopData>(p);
                stops.Add(new Dictionary<string, object> { { "name", PrefabName(p) }, { "type", t.m_TransportType.ToString() }, { "passenger", t.m_PassengerTransport }, { "building", Em.HasComponent<BuildingData>(p) } });
            }
            ps.Dispose();
            var depots = new List<object>();
            ps = Query(new[] { ComponentType.ReadOnly<TransportDepotData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var p in ps) depots.Add(new Dictionary<string, object> { { "name", PrefabName(p) }, { "type", Em.GetComponentData<TransportDepotData>(p).m_TransportType.ToString() } });
            ps.Dispose();
            return new Dictionary<string, object> { { "lines", lines }, { "stops", stops }, { "depots", depots } };
        }

        static Entity FindPrefab<T>(string name) where T : unmanaged, IComponentData
        {
            var ps = Query(new[] { ComponentType.ReadOnly<T>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            try { foreach (var p in ps) if (PrefabName(p) == name) return p; }
            finally { ps.Dispose(); }
            return Entity.Null;
        }

        // ---------------------------------------------------------------- stops

        /// <summary>Nearest real, top-level road edge to p, with the curve parameter of the closest point.</summary>
        static Entity NearestEdge(float2 p, float radius, out float t, out float dist)
        {
            Entity best = Entity.Null; t = 0; dist = radius;
            var edges = Query(new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Game.Net.Road>() });
            foreach (var e in edges)
            {
                if (Em.HasComponent<Owner>(e)) continue;
                var c = Em.GetComponentData<Curve>(e).m_Bezier;
                float tt;
                float d = MathUtils.Distance(c.xz, p, out tt);
                if (d < dist) { dist = d; best = e; t = tt; }
            }
            edges.Dispose();
            return best;
        }

        /// <summary>After stops.create: marks every road edge that carries a kerbside stop (and its two nodes) Updated, so
        /// CS2 re-selects the edge composition now (the stop variant with its bus bay), as its own stop tool does. Without
        /// it the composition changes only on the next load, and lane connections written in session point at the
        /// wrong lanes afterwards (worker-3, roads changing after save/reload). Call once, in phase tool, after the
        /// last stops.create batch, then wait a few frames.</summary>
        static object StopsTouch()
        {
            var edges = new HashSet<Entity>();
            var es = Query(new[] { ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<Attached>() });
            foreach (var e in es)
            {
                var parent = Em.GetComponentData<Attached>(e).m_Parent;
                if (IsReal(parent) && Em.HasComponent<Edge>(parent)) edges.Add(parent);
            }
            es.Dispose();
            int nodes = 0;
            foreach (var e in edges)
            {
                if (!Em.HasComponent<Updated>(e)) Em.AddComponent<Updated>(e);
                var ed = Em.GetComponentData<Edge>(e);
                foreach (var n in new[] { ed.m_Start, ed.m_End }) if (IsReal(n) && !Em.HasComponent<Updated>(n)) { Em.AddComponent<Updated>(n); nodes++; }
            }
            return new Dictionary<string, object> { { "op", "stops.touch" }, { "edges", edges.Count }, { "nodes", nodes } };
        }

        static object StopsCreate(Dictionary<string, object> a)
        {
            float inset = F(a, "inset", 1.5f), radius = F(a, "radius", 12f);
            var res = new List<object>();
            var prefabCache = new Dictionary<string, Entity>();
            if (!(a.TryGetValue("stops", out var so) && so is IList<object> list)) throw new ArgumentException("stops: [...] required");
            foreach (var o in list)
            {
                var s = o as Dictionary<string, object>;
                string prefabName = Str(s, "prefab", "Bus Stop");
                var item = new Dictionary<string, object> { { "key", Str(s, "key", null) } };
                res.Add(item);
                if (!prefabCache.TryGetValue(prefabName, out var prefab)) prefabCache[prefabName] = prefab = FindPrefab<TransportStopData>(prefabName);
                if (prefab == Entity.Null) { item["error"] = "no stop prefab " + prefabName; continue; }
                var p = new float2(F(s, "x"), F(s, "z"));
                var edge = NearestEdge(p, radius, out float t, out float dist);
                if (edge == Entity.Null) { item["error"] = "no road edge within " + radius + " m"; continue; }
                var curve = Em.GetComponentData<Curve>(edge).m_Bezier;
                float3 cp = MathUtils.Position(curve, t);
                float3 tan = math.normalizesafe(MathUtils.Tangent(curve, t));
                // the plan's direction decides the side; the CS2 edge may run the other way
                float2 dir = math.normalizesafe(new float2(F(s, "tx", tan.x), F(s, "tz", tan.z)));
                float2 right = new float2(dir.y, -dir.x);
                bool rightSide = Str(s, "side", "right") != "left";
                float2 outward = rightSide ? right : -right;
                float half = 4f;
                if (Em.HasComponent<Composition>(edge))
                {
                    var comp = Em.GetComponentData<Composition>(edge).m_Edge;
                    if (Em.HasComponent<NetCompositionData>(comp)) half = Em.GetComponentData<NetCompositionData>(comp).m_Width / 2f;
                }
                float3 pos = cp + new float3(outward.x, 0, outward.y) * math.max(0.5f, half - inset);
                // the stop faces the road, its side parallel to the traffic direction
                quaternion rot = quaternion.LookRotationSafe(new float3(-outward.x, 0, -outward.y), math.up());

                var def = Em.CreateEntity();
                Em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab, m_Attached = edge, m_Flags = CreationFlags.Permanent });
                Em.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = pos, m_LocalPosition = float3.zero, m_Scale = new float3(1f), m_Rotation = rot, m_LocalRotation = quaternion.identity,
                    m_Intensity = 1f, m_ParentMesh = -1, m_GroupIndex = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                });
                Em.AddComponent<Updated>(def);
                Em.AddComponent<Deleted>(def);
                item["edge"] = Id(edge); item["edgeDistance"] = dist; item["pos"] = V(pos); item["halfWidth"] = half;
            }
            return new Dictionary<string, object> { { "op", "stops.create" }, { "results", res } };
        }

        static List<object> StopsList(string type)
        {
            var r = new List<object>();
            var es = Query(new[] { ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var e in es)
            {
                var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                string tt = Em.HasComponent<TransportStopData>(prefab) ? Em.GetComponentData<TransportStopData>(prefab).m_TransportType.ToString() : "?";
                if (type != null && tt != type) continue;
                var item = new Dictionary<string, object> { { "entity", Id(e) }, { "prefab", PrefabName(prefab) }, { "type", tt }, { "pos", V(Em.GetComponentData<Game.Objects.Transform>(e).m_Position) } };
                if (Em.HasComponent<Owner>(e)) { var ow = Em.GetComponentData<Owner>(e).m_Owner; item["owner"] = Id(ow); if (Em.Exists(ow) && Em.HasComponent<PrefabRef>(ow)) item["ownerPrefab"] = PrefabName(Em.GetComponentData<PrefabRef>(ow).m_Prefab); }
                if (Em.HasComponent<Attached>(e)) item["attached"] = Id(Em.GetComponentData<Attached>(e).m_Parent);
                if (Em.HasBuffer<ConnectedRoute>(e)) item["routes"] = Em.GetBuffer<ConnectedRoute>(e, true).Length;
                r.Add(item);
            }
            es.Dispose();
            return r;
        }

        struct StopRef { public Entity Entity; public float3 Pos; public TransportType Type; }

        static List<StopRef> AllStops()
        {
            var r = new List<StopRef>();
            var es = Query(new[] { ComponentType.ReadOnly<Game.Routes.TransportStop>(), ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var e in es)
            {
                var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!Em.HasComponent<TransportStopData>(prefab)) continue;
                r.Add(new StopRef { Entity = e, Pos = Em.GetComponentData<Game.Objects.Transform>(e).m_Position, Type = Em.GetComponentData<TransportStopData>(prefab).m_TransportType });
            }
            es.Dispose();
            return r;
        }

        // ---------------------------------------------------------------- lines

        static IEnumerator LinesCreate(Dictionary<string, object> a)
        {
            float radius = F(a, "radius", 12f), stationRadius = F(a, "stationRadius", 200f);
            int frames = (int)F(a, "frames", 40f);
            if (!(a.TryGetValue("lines", out var lo) && lo is IList<object> lines)) throw new ArgumentException("lines: [...] required");
            var stops = AllStops();
            var before = new HashSet<Entity>(Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>() }).ToArray());
            var results = new List<Dictionary<string, object>>();
            var pending = new List<KeyValuePair<Dictionary<string, object>, float3>>(); // result, first waypoint
            foreach (var o in lines)
            {
                var l = o as Dictionary<string, object>;
                var item = new Dictionary<string, object> { { "key", Str(l, "key", null) }, { "name", Str(l, "name", null) } };
                results.Add(item);
                string prefabName = Str(l, "prefab", "Bus Line");
                var prefab = FindPrefab<TransportLineData>(prefabName);
                if (prefab == Entity.Null) { item["status"] = "skipped"; item["reason"] = "no line prefab " + prefabName; continue; }
                var ttype = Em.GetComponentData<TransportLineData>(prefab).m_TransportType;
                bool station = B(l, "requiresStations");
                float r = station ? stationRadius : radius;
                var wps = new List<WaypointDefinition>();
                var missing = new List<object>();
                var used = new HashSet<Entity>();
                if (l.TryGetValue("stops", out var sto) && sto is IList<object> sl)
                    foreach (var so in sl)
                    {
                        var s = so as Dictionary<string, object>;
                        var p = new float2(F(s, "x"), F(s, "z"));
                        StopRef best = default; float bd = r;
                        foreach (var st in stops)
                        {
                            if (st.Type != ttype) continue;
                            float d = math.distance(st.Pos.xz, p);
                            if (d < bd) { bd = d; best = st; }
                        }
                        if (best.Entity == Entity.Null) { missing.Add(new List<object> { p.x, p.y }); continue; }
                        if (wps.Count > 0 && wps[wps.Count - 1].m_Connection == best.Entity) continue; // same stop twice in a row
                        used.Add(best.Entity);
                        wps.Add(new WaypointDefinition { m_Position = best.Pos, m_Connection = best.Entity, m_Original = Entity.Null });
                    }
                if (wps.Count > 1 && wps[0].m_Connection == wps[wps.Count - 1].m_Connection) wps.RemoveAt(wps.Count - 1);
                item["stopsResolved"] = wps.Count; item["stopsMissing"] = missing.Count;
                if (missing.Count > 0) item["missing"] = missing;
                if (wps.Count < 2)
                {
                    item["status"] = "skipped";
                    item["reason"] = station ? "fewer than 2 stations found (stations come with the buildings)" : "fewer than 2 stops found";
                    continue;
                }
                // closed loop: the route tool ends on the first waypoint again
                wps.Add(wps[0]);
                var def = Em.CreateEntity();
                Em.AddComponentData(def, new CreationDefinition { m_Prefab = prefab });
                Em.AddComponentData(def, new ColorDefinition { m_Color = ParseColor(Str(l, "color", "#808080")) });
                var buf = Em.AddBuffer<WaypointDefinition>(def);
                foreach (var w in wps) buf.Add(w);
                Em.AddComponent<Updated>(def);
                Em.AddComponent<Deleted>(def);
                item["status"] = "defined";
                pending.Add(new KeyValuePair<Dictionary<string, object>, float3>(item, wps[0].m_Position));
            }
            yield return null; // generation (Modification phases) makes Temp routes
            int tempRoutes = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<Temp>() }, new ComponentType[0]).Length;
            bool applied = pending.Count > 0 && SetApply();
            for (int i = 0; i < frames; i++) yield return null;

            // match the new routes to our lines by their first waypoint (names and numbers are set by lines.name in the
            // main phase: NameSystem needs a command buffer, which CS2 does not allow during the tool phase)
            var now = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>() });
            foreach (var e in now)
            {
                if (before.Contains(e) || !Em.HasBuffer<RouteWaypoint>(e)) continue;
                var wb = Em.GetBuffer<RouteWaypoint>(e, true);
                if (wb.Length == 0 || !Em.HasComponent<Position>(wb[0].m_Waypoint)) continue;
                float3 p0 = Em.GetComponentData<Position>(wb[0].m_Waypoint).m_Position;
                foreach (var kv in pending)
                {
                    if (kv.Key.ContainsKey("route") || math.distance(kv.Value, p0) > 1f) continue;
                    kv.Key["route"] = Id(e); kv.Key["status"] = "created";
                    break;
                }
            }
            now.Dispose();
            yield return new Dictionary<string, object>
            {
                { "op", "lines.create" }, { "tempRoutes", tempRoutes }, { "applied", applied },
                { "created", results.FindAll(x => (string)x["status"] == "created").Count },
                { "skipped", results.FindAll(x => (string)x["status"] == "skipped").Count },
                // as List<object>: the mod reads it in process with P.List (a typed list came back as null there, so the
                // mod's lines never got their CS1 names and numbers); over the bridge it is JSON either way
                { "lines", results.ConvertAll(x => (object)x) },
            };
        }

        /// <summary>ToolSystem.applyMode is the last-updated tool's applyMode (protected setter). The active tool has
        /// already updated this frame, so setting it to Apply makes ToolOutputSystem run the ApplyTool phase.</summary>
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

        static object LinesName(Dictionary<string, object> a)
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            int n = 0;
            if (a.TryGetValue("routes", out var ro) && ro is IList<object> list)
                foreach (var o in list)
                {
                    var r = o as Dictionary<string, object>;
                    var parts = Str(r, "route", "").Split(':');
                    if (parts.Length != 2) continue;
                    var e = new Entity { Index = int.Parse(parts[0]), Version = int.Parse(parts[1]) };
                    if (!IsReal(e) || !Em.HasComponent<Route>(e)) continue;
                    string name = Str(r, "name", null);
                    if (!string.IsNullOrEmpty(name)) names.SetCustomName(e, name);
                    if (r.ContainsKey("number") && r["number"] != null && Em.HasComponent<RouteNumber>(e)) Em.SetComponentData(e, new RouteNumber { m_Number = (int)F(r, "number") });
                    n++;
                }
            return new Dictionary<string, object> { { "op", "lines.name" }, { "named", n } };
        }

        /// <summary>Deletes the lines of the given transport types (route + its waypoints and segments), so they can be
        /// re-created once their stations are joined to the track (a line pathfinds when it is created).</summary>
        static object LinesDelete(Dictionary<string, object> a)
        {
            var types = new HashSet<string>();
            if (a.TryGetValue("types", out var to) && to is IList<object> tl) foreach (var t in tl) types.Add(t.ToString());
            var names = new List<object>();
            var ns = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var es = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var e in es)
            {
                var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                string tt = Em.HasComponent<TransportLineData>(prefab) ? Em.GetComponentData<TransportLineData>(prefab).m_TransportType.ToString() : "?";
                if (!types.Contains(tt)) continue;
                string n = null; ns.TryGetCustomName(e, out n); names.Add(n);
                var parts = new List<Entity>();
                if (Em.HasBuffer<RouteWaypoint>(e)) { var b = Em.GetBuffer<RouteWaypoint>(e, true); for (int i = 0; i < b.Length; i++) parts.Add(b[i].m_Waypoint); }
                if (Em.HasBuffer<RouteSegment>(e)) { var b = Em.GetBuffer<RouteSegment>(e, true); for (int i = 0; i < b.Length; i++) parts.Add(b[i].m_Segment); }
                Em.AddComponent<Deleted>(e);
                foreach (var x in parts) if (IsReal(x)) Em.AddComponent<Deleted>(x);
            }
            es.Dispose();
            return new Dictionary<string, object> { { "op", "lines.delete" }, { "deleted", names } };
        }

        static object LinesRepath(Dictionary<string, object> a)
        {
            var types = new HashSet<string>();
            if (a.TryGetValue("types", out var to) && to is IList<object> tl) foreach (var t in tl) types.Add(t.ToString());
            int routes = 0, segments = 0;
            var es = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var e in es)
            {
                var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                string tt = Em.HasComponent<TransportLineData>(prefab) ? Em.GetComponentData<TransportLineData>(prefab).m_TransportType.ToString() : "?";
                if (types.Count > 0 && !types.Contains(tt)) continue;
                routes++;
                if (!Em.HasBuffer<RouteSegment>(e)) continue;
                var sb = Em.GetBuffer<RouteSegment>(e, true);
                var segs = new List<Entity>();
                for (int i = 0; i < sb.Length; i++) segs.Add(sb[i].m_Segment);
                foreach (var sg in segs) if (IsReal(sg) && !Em.HasComponent<Updated>(sg)) { Em.AddComponent<Updated>(sg); segments++; }
            }
            es.Dispose();
            // RoutePathSystem re-checks existing lines' paths only when lanes of their type come with Applied (the tool's
            // apply step adds it; Permanent definitions and placed buildings' sub-nets never do). Tag one real track
            // lane per track type for a frame, so every train/subway line is re-checked against the joined network.
            int tagged = 0;
            var seen = new HashSet<Game.Net.TrackTypes>();
            var lanes = Query(new[] { ComponentType.ReadOnly<Game.Net.Lane>(), ComponentType.ReadOnly<Game.Net.TrackLane>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var l in lanes)
            {
                var lp = Em.GetComponentData<PrefabRef>(l).m_Prefab;
                if (!Em.HasComponent<TrackLaneData>(lp)) continue;
                var tt = Em.GetComponentData<TrackLaneData>(lp).m_TrackTypes;
                if (seen.Contains(tt)) continue;
                seen.Add(tt);
                if (!Em.HasComponent<Applied>(l)) Em.AddComponent<Applied>(l);
                tagged++;
            }
            lanes.Dispose();
            return new Dictionary<string, object> { { "op", "lines.repath" }, { "routes", routes }, { "segments", segments }, { "appliedLaneTags", tagged } };
        }

        /// <summary>One line in depth: per waypoint its connected stop (prefab, owner, lanes it can use), per segment the
        /// PathTargets lanes, path state and element count. Shows why a line gets no path.</summary>
        static object LinesDebug(string name)
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var es = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>() });
            var r = new List<object>();
            foreach (var e in es)
            {
                string custom = null; names.TryGetCustomName(e, out custom);
                if (name != null && custom != name) continue;
                var wps = new List<object>();
                if (Em.HasBuffer<RouteWaypoint>(e))
                {
                    var wb = Em.GetBuffer<RouteWaypoint>(e, true);
                    for (int i = 0; i < wb.Length && i < 12; i++)
                    {
                        var w = wb[i].m_Waypoint; var it = new Dictionary<string, object> { { "wp", Id(w) } };
                        if (Em.HasComponent<Game.Routes.Connected>(w))
                        {
                            var st = Em.GetComponentData<Game.Routes.Connected>(w).m_Connected;
                            it["stop"] = Id(st);
                            if (Em.Exists(st))
                            {
                                it["stopPrefab"] = Em.HasComponent<PrefabRef>(st) ? PrefabName(Em.GetComponentData<PrefabRef>(st).m_Prefab) : null;
                                if (Em.HasComponent<Owner>(st)) it["stopOwner"] = PrefabName(Em.GetComponentData<PrefabRef>(Em.GetComponentData<Owner>(st).m_Owner).m_Prefab);
                                if (Em.HasComponent<Game.Objects.Attached>(st)) { var at = Em.GetComponentData<Game.Objects.Attached>(st); it["attachedTo"] = Id(at.m_Parent); it["attachedReal"] = IsReal(at.m_Parent); }
                                if (Em.HasComponent<Game.Routes.TransportStop>(st)) it["stopFlags"] = Em.GetComponentData<Game.Routes.TransportStop>(st).m_Flags.ToString();
                                { var ct = Em.GetComponentTypes(st, Allocator.Temp); var tn = new List<object>(); foreach (var t in ct) tn.Add(t.GetManagedType()?.Name ?? "?"); ct.Dispose(); it["stopComponents"] = tn; }
                                if (Em.HasBuffer<Game.Net.SubLane>(st)) it["stopSubLanes"] = Em.GetBuffer<Game.Net.SubLane>(st, true).Length;
                                if (Em.HasComponent<Game.Routes.AccessLane>(st)) { var al = Em.GetComponentData<Game.Routes.AccessLane>(st); it["accessLane"] = Id(al.m_Lane); }
                                if (Em.HasComponent<Game.Routes.RouteLane>(st)) { var rl = Em.GetComponentData<Game.Routes.RouteLane>(st); it["routeLane"] = Id(rl.m_StartLane) + ">" + Id(rl.m_EndLane); }
                            }
                        }
                        if (Em.HasComponent<Game.Routes.RouteLane>(w)) { var rl = Em.GetComponentData<Game.Routes.RouteLane>(w); it["wpRouteLane"] = Id(rl.m_StartLane) + ">" + Id(rl.m_EndLane); }
                        wps.Add(it);
                    }
                }
                var segs = new List<object>();
                if (Em.HasBuffer<RouteSegment>(e))
                {
                    var sb = Em.GetBuffer<RouteSegment>(e, true);
                    for (int i = 0; i < sb.Length && i < 6; i++)
                    {
                        var sg = sb[i].m_Segment; var it = new Dictionary<string, object> { { "seg", Id(sg) } };
                        if (Em.HasComponent<Game.Routes.PathTargets>(sg)) { var pt = Em.GetComponentData<Game.Routes.PathTargets>(sg); it["startLane"] = LaneInfo(pt.m_StartLane); it["endLane"] = LaneInfo(pt.m_EndLane); }
                        if (Em.HasComponent<Game.Pathfind.PathInformation>(sg)) it["state"] = Em.GetComponentData<Game.Pathfind.PathInformation>(sg).m_State.ToString();
                        if (Em.HasBuffer<Game.Pathfind.PathElement>(sg)) it["elements"] = Em.GetBuffer<Game.Pathfind.PathElement>(sg, true).Length;
                        segs.Add(it);
                    }
                }
                r.Add(new Dictionary<string, object> { { "route", Id(e) }, { "name", custom }, { "waypoints", wps }, { "segments", segs } });
            }
            es.Dispose();
            return r;
        }

        /// <summary>Lane → exists, prefab, owner edge (prefab, owner building, end nodes' edge counts).</summary>
        static Dictionary<string, object> LaneInfo(Entity lane)
        {
            var d = new Dictionary<string, object> { { "lane", Id(lane) }, { "exists", Em.Exists(lane) }, { "real", IsReal(lane) } };
            if (!Em.Exists(lane)) return d;
            if (Em.HasComponent<PrefabRef>(lane)) d["prefab"] = PrefabName(Em.GetComponentData<PrefabRef>(lane).m_Prefab);
            if (Em.HasComponent<Owner>(lane))
            {
                var ow = Em.GetComponentData<Owner>(lane).m_Owner;
                d["owner"] = Id(ow); d["ownerReal"] = IsReal(ow);
                if (Em.Exists(ow))
                {
                    if (Em.HasComponent<PrefabRef>(ow)) d["ownerPrefab"] = PrefabName(Em.GetComponentData<PrefabRef>(ow).m_Prefab);
                    d["ownerIsEdge"] = Em.HasComponent<Edge>(ow);
                    if (Em.HasComponent<Owner>(ow)) d["ownerOwner"] = PrefabName(Em.GetComponentData<PrefabRef>(Em.GetComponentData<Owner>(ow).m_Owner).m_Prefab);
                    if (Em.HasComponent<Edge>(ow))
                    {
                        var ed = Em.GetComponentData<Edge>(ow);
                        int cs = Em.HasBuffer<ConnectedEdge>(ed.m_Start) ? Em.GetBuffer<ConnectedEdge>(ed.m_Start, true).Length : -1;
                        int ce = Em.HasBuffer<ConnectedEdge>(ed.m_End) ? Em.GetBuffer<ConnectedEdge>(ed.m_End, true).Length : -1;
                        d["endEdges"] = new List<object> { cs, ce };
                    }
                }
            }
            return d;
        }

        /// <summary>Nodes within r of each point: owner, connected edges (prefab, owner, elevation of the far end), node
        /// sub-lanes by prefab (track lane connections across the node live there).</summary>
        static object NodeAt(Dictionary<string, object> a)
        {
            float r = F(a, "r", 2f);
            var res = new List<object>();
            var nodes = Query(new[] { ComponentType.ReadOnly<Game.Net.Node>() });
            if (a.TryGetValue("points", out var po) && po is IList<object> pts)
                foreach (var p0 in pts)
                {
                    var xz = p0 as IList<object>;
                    var pt = new float3(Convert.ToSingle(xz[0]), Convert.ToSingle(xz[1]), Convert.ToSingle(xz[2]));
                    var hits = new List<object>();
                    foreach (var n in nodes)
                    {
                        var np = Em.GetComponentData<Game.Net.Node>(n).m_Position;
                        if (math.distance(np, pt) > r) continue;
                        var it = new Dictionary<string, object> { { "node", Id(n) }, { "pos", V(np) }, { "owned", Em.HasComponent<Owner>(n) } };
                        if (Em.HasComponent<Owner>(n)) it["owner"] = PrefabName(Em.GetComponentData<PrefabRef>(Em.GetComponentData<Owner>(n).m_Owner).m_Prefab);
                        var edges = new List<object>();
                        if (Em.HasBuffer<ConnectedEdge>(n))
                        {
                            var cb = Em.GetBuffer<ConnectedEdge>(n, true);
                            for (int i = 0; i < cb.Length; i++)
                            {
                                var e = cb[i].m_Edge;
                                var ed = new Dictionary<string, object> { { "edge", Id(e) }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab) }, { "owned", Em.HasComponent<Owner>(e) } };
                                if (Em.HasBuffer<Game.Net.SubLane>(e)) ed["lanes"] = Em.GetBuffer<Game.Net.SubLane>(e, true).Length;
                                edges.Add(ed);
                            }
                        }
                        it["edges"] = edges;
                        var nl = new Dictionary<string, int>();
                        if (Em.HasBuffer<Game.Net.SubLane>(n))
                        {
                            var sb = Em.GetBuffer<Game.Net.SubLane>(n, true);
                            for (int i = 0; i < sb.Length; i++) { var l = sb[i].m_SubLane; string ln = Em.HasComponent<PrefabRef>(l) ? PrefabName(Em.GetComponentData<PrefabRef>(l).m_Prefab) : "?"; nl[ln] = nl.TryGetValue(ln, out int c) ? c + 1 : 1; }
                        }
                        it["nodeLanes"] = nl;
                        hits.Add(it);
                    }
                    res.Add(new Dictionary<string, object> { { "at", V(pt) }, { "nodes", hits } });
                }
            nodes.Dispose();
            return res;
        }

        static object LinesList()
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var r = new List<object>();
            // vehicles per line: the route's RouteVehicle buffer can stay empty while buses run on it, so also count the
            // vehicles whose CurrentRoute is the line (engines / lead vehicles only: a controller with its own carriages
            // counts once)
            var onRoute = new Dictionary<Entity, int>(); var movingOn = new Dictionary<Entity, int>();
            var vq = Query(new[] { ComponentType.ReadOnly<Game.Routes.CurrentRoute>() });
            foreach (var v in vq)
            {
                if (Em.HasComponent<Game.Vehicles.Controller>(v) && Em.GetComponentData<Game.Vehicles.Controller>(v).m_Controller != v && Em.GetComponentData<Game.Vehicles.Controller>(v).m_Controller != Entity.Null) continue;
                var rt = Em.GetComponentData<Game.Routes.CurrentRoute>(v).m_Route;
                onRoute[rt] = onRoute.TryGetValue(rt, out int n) ? n + 1 : 1;
                if (IsMovingLead(v)) movingOn[rt] = movingOn.TryGetValue(rt, out int m) ? m + 1 : 1;
            }
            vq.Dispose();
            var es = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>() });
            foreach (var e in es)
            {
                var route = Em.GetComponentData<Route>(e);
                string custom = null;
                names.TryGetCustomName(e, out custom);
                var c = Em.HasComponent<Game.Routes.Color>(e) ? Em.GetComponentData<Game.Routes.Color>(e).m_Color : default;
                r.Add(new Dictionary<string, object>
                {
                    { "entity", Id(e) }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab) }, { "name", custom },
                    { "number", Em.HasComponent<RouteNumber>(e) ? Em.GetComponentData<RouteNumber>(e).m_Number : -1 },
                    { "color", string.Format("#{0:x2}{1:x2}{2:x2}", c.r, c.g, c.b) },
                    { "complete", (route.m_Flags & RouteFlags.Complete) != 0 },
                    { "waypoints", Em.HasBuffer<RouteWaypoint>(e) ? Em.GetBuffer<RouteWaypoint>(e, true).Length : 0 },
                    { "segments", Em.HasBuffer<RouteSegment>(e) ? Em.GetBuffer<RouteSegment>(e, true).Length : 0 },
                    { "vehicles", math.max(Em.HasBuffer<RouteVehicle>(e) ? Em.GetBuffer<RouteVehicle>(e, true).Length : 0, onRoute.TryGetValue(e, out int vr) ? vr : 0) },
                    { "movingVehicles", movingOn.TryGetValue(e, out int mv) ? mv : 0 },
                    { "pathElements", PathStats(e, out int failed) }, { "failedSegments", failed },
                });
            }
            es.Dispose();
            return r;
        }

        /// <summary>Path elements over all route segments; failed = segments whose PathInformation says Failed.</summary>
        static int PathStats(Entity route, out int failed)
        {
            failed = 0; int n = 0;
            if (!Em.HasBuffer<RouteSegment>(route)) return 0;
            var sb = Em.GetBuffer<RouteSegment>(route, true);
            for (int i = 0; i < sb.Length; i++)
            {
                var sg = sb[i].m_Segment;
                if (!Em.Exists(sg)) continue;
                if (Em.HasBuffer<Game.Pathfind.PathElement>(sg)) n += Em.GetBuffer<Game.Pathfind.PathElement>(sg, true).Length;
                if (Em.HasComponent<Game.Pathfind.PathInformation>(sg) && (Em.GetComponentData<Game.Pathfind.PathInformation>(sg).m_State & Game.Pathfind.PathFlags.Failed) != 0) failed++;
            }
            return n;
        }

        /// <summary>Diagnostic: vehicles whose prefab name contains `match`: position, parked or moving, the route they
        /// serve (CurrentRoute), PublicTransport state, owner (depot) and target.</summary>
        static object Vehicles(string match)
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var r = new List<object>();
            var es = Query(new[] { ComponentType.ReadOnly<Game.Vehicles.Vehicle>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var e in es)
            {
                string pn = PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab);
                if (pn == null || pn.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var it = new Dictionary<string, object> { { "entity", Id(e) }, { "prefab", pn } };
                if (Em.HasComponent<Game.Objects.Transform>(e)) it["pos"] = V(Em.GetComponentData<Game.Objects.Transform>(e).m_Position);
                it["parked"] = Em.HasComponent<Game.Vehicles.ParkedTrain>(e);
                if (Em.HasComponent<Game.Vehicles.PublicTransport>(e)) it["ptState"] = Em.GetComponentData<Game.Vehicles.PublicTransport>(e).m_State.ToString();
                if (Em.HasComponent<Game.Routes.CurrentRoute>(e))
                {
                    var rt = Em.GetComponentData<Game.Routes.CurrentRoute>(e).m_Route;
                    string rn = null; if (Em.Exists(rt)) names.TryGetCustomName(rt, out rn);
                    it["route"] = rn ?? Id(rt);
                }
                if (Em.HasComponent<Owner>(e)) { var o = Em.GetComponentData<Owner>(e).m_Owner; it["owner"] = Em.Exists(o) && Em.HasComponent<PrefabRef>(o) ? PrefabName(Em.GetComponentData<PrefabRef>(o).m_Prefab) : Id(o); }
                if (Em.HasComponent<Game.Common.Target>(e)) it["target"] = Id(Em.GetComponentData<Game.Common.Target>(e).m_Target);
                if (Em.HasComponent<Game.Objects.Moving>(e)) { var mv = Em.GetComponentData<Game.Objects.Moving>(e); it["speed"] = math.length(mv.m_Velocity); }
                if (Em.HasComponent<Game.Vehicles.Controller>(e)) { var c = Em.GetComponentData<Game.Vehicles.Controller>(e).m_Controller; if (c != e) it["controller"] = Id(c); }
                r.Add(it);
                if (r.Count >= 40) break;
            }
            es.Dispose();
            return r;
        }

        /// <summary>Diagnostic (#161 round 2): every edge whose curve passes within r of each point (x/z): prefab, owner
        /// (top building), start/end node positions and edge count, elevation, and whether it carries road or track.</summary>
        static object EdgesAt(Dictionary<string, object> a)
        {
            float r = F(a, "r", 6f);
            var pts = new List<float2>();
            if (a.TryGetValue("points", out var po) && po is IList<object> pl)
                foreach (var o in pl) { var xz = o as IList<object>; pts.Add(new float2(Convert.ToSingle(xz[0]), Convert.ToSingle(xz[xz.Count > 2 ? 2 : 1]))); }
            var res = new List<object>();
            var es = Query(new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var q in pts)
            {
                var hits = new List<object>();
                foreach (var e in es)
                {
                    var c = Em.GetComponentData<Curve>(e).m_Bezier;
                    float best = float.MaxValue; float bt = 0;
                    for (int i = 0; i <= 16; i++) { float d = math.distance(MathUtils.Position(c, i / 16f).xz, q); if (d < best) { best = d; bt = i / 16f; } }
                    if (best > r) continue;
                    var pf = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var ed = Em.GetComponentData<Edge>(e);
                    Entity top = e; for (int k = 0; k < 8 && Em.HasComponent<Owner>(top); k++) top = Em.GetComponentData<Owner>(top).m_Owner;
                    var it = new Dictionary<string, object> {
                        { "edge", Id(e) }, { "prefab", PrefabName(pf) }, { "dist", best }, { "y", MathUtils.Position(c, bt).y },
                        { "road", Em.HasComponent<RoadData>(pf) }, { "track", Em.HasComponent<TrackData>(pf) },
                        { "owner", top != e && Em.HasComponent<PrefabRef>(top) ? PrefabName(Em.GetComponentData<PrefabRef>(top).m_Prefab) : null },
                        { "start", V(Em.GetComponentData<Node>(ed.m_Start).m_Position) }, { "end", V(Em.GetComponentData<Node>(ed.m_End).m_Position) },
                        { "startEdges", Em.HasBuffer<ConnectedEdge>(ed.m_Start) ? Em.GetBuffer<ConnectedEdge>(ed.m_Start, true).Length : -1 },
                        { "endEdges", Em.HasBuffer<ConnectedEdge>(ed.m_End) ? Em.GetBuffer<ConnectedEdge>(ed.m_End, true).Length : -1 },
                    };
                    if (Em.HasComponent<Game.Net.Elevation>(e)) it["elevation"] = Em.GetComponentData<Game.Net.Elevation>(e).m_Elevation.ToString();
                    if (Em.HasComponent<Game.Net.Composition>(e)) it["composition"] = Id(Em.GetComponentData<Game.Net.Composition>(e).m_Edge);
                    hits.Add(it);
                }
                res.Add(new Dictionary<string, object> { { "at", new List<object> { q.x, q.y } }, { "edges", hits } });
            }
            es.Dispose();
            return res;
        }

        /// <summary>Diagnostic (bus check, 2026-11-22): every vehicle whose prefab name contains `match` (lead vehicles):
        /// parked or moving, PublicTransport state, what its CurrentRoute and Target are, its owner (depot), passengers;
        /// and per transit line: prefab, name, waypoints, stops with waiting passengers, vehicles (RouteVehicle and
        /// CurrentRoute counts).</summary>
        static object Buses(string match)
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            Func<Entity, string> kind = x =>
            {
                if (x == Entity.Null) return "null";
                if (!Em.Exists(x)) return "missing";
                if (Em.HasComponent<Game.Routes.TransportLine>(x)) { names.TryGetCustomName(x, out string n); return "line " + (n ?? Id(x)); }
                if (Em.HasComponent<Game.Routes.Segment>(x)) return "route segment";
                if (Em.HasComponent<Game.Routes.Waypoint>(x)) return "waypoint";
                if (Em.HasComponent<Game.Routes.TransportStop>(x)) return "stop";
                if (Em.HasComponent<Game.Buildings.Building>(x)) return "building " + (Em.HasComponent<PrefabRef>(x) ? PrefabName(Em.GetComponentData<PrefabRef>(x).m_Prefab) : "");
                if (Em.HasComponent<Game.Net.Lane>(x)) return "lane";
                return Em.HasComponent<PrefabRef>(x) ? PrefabName(Em.GetComponentData<PrefabRef>(x).m_Prefab) : Id(x);
            };
            var vehicles = new List<object>();
            var byState = new Dictionary<string, int>();
            var onRoute = new Dictionary<Entity, int>();
            var vq = Query(new[] { ComponentType.ReadOnly<Game.Vehicles.Vehicle>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var v in vq)
            {
                string pn = PrefabName(Em.GetComponentData<PrefabRef>(v).m_Prefab);
                if (pn == null || pn.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (Em.HasComponent<Game.Vehicles.Controller>(v)) { var c = Em.GetComponentData<Game.Vehicles.Controller>(v).m_Controller; if (c != Entity.Null && c != v) continue; }
                bool parked = Em.HasComponent<Game.Vehicles.ParkedCar>(v) || Em.HasComponent<Game.Vehicles.ParkedTrain>(v);
                string st = Em.HasComponent<Game.Vehicles.PublicTransport>(v) ? Em.GetComponentData<Game.Vehicles.PublicTransport>(v).m_State.ToString() : "-";
                string key = (parked ? "parked" : "moving") + " / " + st;
                byState[key] = byState.TryGetValue(key, out int n) ? n + 1 : 1;
                var it = new Dictionary<string, object> { { "entity", Id(v) }, { "prefab", pn }, { "parked", parked }, { "state", st } };
                if (Em.HasComponent<Game.Routes.CurrentRoute>(v)) { var rt = Em.GetComponentData<Game.Routes.CurrentRoute>(v).m_Route; it["currentRoute"] = kind(rt); onRoute[rt] = onRoute.TryGetValue(rt, out int k) ? k + 1 : 1; }
                if (Em.HasComponent<Game.Common.Target>(v)) it["target"] = kind(Em.GetComponentData<Game.Common.Target>(v).m_Target);
                if (Em.HasComponent<Owner>(v)) it["owner"] = kind(Em.GetComponentData<Owner>(v).m_Owner);
                if (Em.HasBuffer<Game.Vehicles.Passenger>(v)) it["passengers"] = Em.GetBuffer<Game.Vehicles.Passenger>(v, true).Length;
                if (Em.HasComponent<Game.Objects.Transform>(v)) it["pos"] = V(Em.GetComponentData<Game.Objects.Transform>(v).m_Position);
                if (vehicles.Count < 60) vehicles.Add(it);
            }
            vq.Dispose();
            var lines = new List<object>();
            var lq = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var e in lq)
            {
                names.TryGetCustomName(e, out string nm);
                int wps = 0, waitingStops = 0, waiting = 0;
                if (Em.HasBuffer<RouteWaypoint>(e))
                {
                    var wb = Em.GetBuffer<RouteWaypoint>(e, true);
                    wps = wb.Length;
                    for (int i = 0; i < wb.Length; i++)
                    {
                        var w = wb[i].m_Waypoint;
                        Entity stop = Em.HasComponent<Game.Routes.Connected>(w) ? Em.GetComponentData<Game.Routes.Connected>(w).m_Connected : Entity.Null;
                        foreach (var x in new[] { w, stop })
                            if (x != Entity.Null && Em.Exists(x) && Em.HasComponent<Game.Routes.WaitingPassengers>(x)) { int c = Em.GetComponentData<Game.Routes.WaitingPassengers>(x).m_Count; if (c > 0) { waitingStops++; waiting += c; } break; }
                    }
                }
                lines.Add(new Dictionary<string, object> {
                    { "name", nm }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab) }, { "waypoints", wps },
                    { "stopsWaiting", waitingStops }, { "waiting", waiting },
                    { "routeVehicles", Em.HasBuffer<RouteVehicle>(e) ? Em.GetBuffer<RouteVehicle>(e, true).Length : 0 },
                    { "currentRouteVehicles", onRoute.TryGetValue(e, out int cr) ? cr : 0 },
                    { "flags", Em.HasComponent<Game.Routes.TransportLine>(e) ? Em.GetComponentData<Game.Routes.TransportLine>(e).m_Flags.ToString() : "-" },
                    { "vehicleRequest", Em.HasComponent<Game.Routes.TransportLine>(e) ? Em.GetComponentData<Game.Routes.TransportLine>(e).m_VehicleRequest.ToString() : "-" },
                });
            }
            lq.Dispose();
            return new Dictionary<string, object> { { "byState", byState }, { "vehicles", vehicles }, { "lines", lines } };
        }

        /// <summary>Diagnostic (bus check): every transport depot (prefab, TransportDepot flags, available vehicles, target
        /// request, efficiency factors, road edge, building flags) and every TransportVehicleRequest (its route, fail count,
        /// flags, cooldown, dispatched handler, path state).</summary>
        static object DepotsCheck()
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var depots = new List<object>();
            var dq = Query(new[] { ComponentType.ReadOnly<Game.Buildings.TransportDepot>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var d in dq)
            {
                var td = Em.GetComponentData<Game.Buildings.TransportDepot>(d);
                var it = new Dictionary<string, object> { { "depot", Id(d) }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(d).m_Prefab) }, { "flags", td.m_Flags.ToString() }, { "available", (int)td.m_AvailableVehicles }, { "targetRequest", Id(td.m_TargetRequest) } };
                if (Em.HasBuffer<Game.Buildings.Efficiency>(d)) { var eff = new List<object>(); var eb = Em.GetBuffer<Game.Buildings.Efficiency>(d, true); for (int i = 0; i < eb.Length; i++) eff.Add(eb[i].m_Factor + "=" + eb[i].m_Efficiency.ToString("0.##")); it["efficiency"] = eff; }
                if (Em.HasComponent<Game.Buildings.Building>(d)) { var bd = Em.GetComponentData<Game.Buildings.Building>(d); it["roadEdge"] = Id(bd.m_RoadEdge); it["buildingFlags"] = bd.m_Flags.ToString(); }
                if (Em.HasBuffer<Game.Simulation.ServiceDispatch>(d)) it["dispatches"] = Em.GetBuffer<Game.Simulation.ServiceDispatch>(d, true).Length;
                if (Em.HasBuffer<Game.Vehicles.OwnedVehicle>(d)) it["ownedVehicles"] = Em.GetBuffer<Game.Vehicles.OwnedVehicle>(d, true).Length;
                if (Em.HasComponent<Game.Objects.Transform>(d)) it["pos"] = V(Em.GetComponentData<Game.Objects.Transform>(d).m_Position);
                depots.Add(it);
            }
            dq.Dispose();
            var reqs = new List<object>();
            var rq = Query(new[] { ComponentType.ReadOnly<Game.Simulation.TransportVehicleRequest>() });
            foreach (var r in rq)
            {
                var tr = Em.GetComponentData<Game.Simulation.TransportVehicleRequest>(r);
                names.TryGetCustomName(tr.m_Route, out string nm);
                var it = new Dictionary<string, object> { { "request", Id(r) }, { "route", nm ?? Id(tr.m_Route) }, { "priority", tr.m_Priority } };
                if (Em.HasComponent<Game.Simulation.ServiceRequest>(r)) { var sr = Em.GetComponentData<Game.Simulation.ServiceRequest>(r); it["failCount"] = (int)sr.m_FailCount; it["cooldown"] = (int)sr.m_Cooldown; it["flags"] = sr.m_Flags.ToString(); }
                if (Em.HasComponent<Game.Simulation.Dispatched>(r)) it["handler"] = Id(Em.GetComponentData<Game.Simulation.Dispatched>(r).m_Handler);
                if (Em.HasComponent<Game.Pathfind.PathInformation>(r)) { var pi = Em.GetComponentData<Game.Pathfind.PathInformation>(r); it["pathState"] = pi.m_State.ToString(); it["pathOrigin"] = Id(pi.m_Origin); it["pathDestination"] = Id(pi.m_Destination); }
                var types = Em.GetComponentTypes(r, Allocator.Temp); var tn = new List<object>(); foreach (var t in types) tn.Add(t.GetManagedType()?.Name ?? "?"); types.Dispose();
                it["components"] = tn;
                reqs.Add(it);
            }
            rq.Dispose();
            return new Dictionary<string, object> { { "depots", depots }, { "requests", reqs } };
        }

        /// <summary>Diagnostic (bus check): resets every TransportVehicleRequest's fail count and cooldown, so the
        /// dispatch tries again now (shows whether the failure is persistent or stale).</summary>
        static object RequestsReset()
        {
            int n = 0;
            var rq = Query(new[] { ComponentType.ReadOnly<Game.Simulation.TransportVehicleRequest>(), ComponentType.ReadOnly<Game.Simulation.ServiceRequest>() });
            foreach (var r in rq) { var sr = Em.GetComponentData<Game.Simulation.ServiceRequest>(r); sr.m_FailCount = 0; sr.m_Cooldown = 0; Em.SetComponentData(r, sr); n++; }
            rq.Dispose();
            return new Dictionary<string, object> { { "op", "requests.reset" }, { "requests", n } };
        }

        /// <summary>Diagnostic: the positions of a line's waypoints (Game.Routes.Position).</summary>
        static object LineStopPositions(string name)
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var r0 = new List<object>();
            var es = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>() });
            foreach (var e in es)
            {
                names.TryGetCustomName(e, out string nm);
                if (name != null && nm != name) continue;
                var wb = Em.GetBuffer<RouteWaypoint>(e, true);
                for (int i = 0; i < wb.Length; i++) if (Em.HasComponent<Game.Routes.Position>(wb[i].m_Waypoint)) r0.Add(V(Em.GetComponentData<Game.Routes.Position>(wb[i].m_Waypoint).m_Position));
                break;
            }
            es.Dispose();
            return r0;
        }

        /// <summary>Diagnostic (bus check): the bus prefab's size class and, along a line's computed path, how many lanes
        /// allow each max size (CarLaneData.m_MaxSize) and road type, with sample lanes too small for the bus. Also the
        /// depot's own lanes and its road edge's lanes.</summary>
        static object LaneSizes(string name)
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var r = new Dictionary<string, object>();
            var buses = new List<object>();
            var pq = Query(new[] { ComponentType.ReadOnly<Game.Prefabs.CarData>(), ComponentType.ReadOnly<Game.Prefabs.PublicTransportVehicleData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var pf in pq) { var ptd = Em.GetComponentData<Game.Prefabs.PublicTransportVehicleData>(pf); if (ptd.m_TransportType != TransportType.Bus) continue; buses.Add(PrefabName(pf) + " size=" + Em.GetComponentData<Game.Prefabs.CarData>(pf).m_SizeClass + " energy=" + Em.GetComponentData<Game.Prefabs.CarData>(pf).m_EnergyType); }
            pq.Dispose();
            r["busPrefabs"] = buses;
            var es = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>() });
            foreach (var e in es)
            {
                names.TryGetCustomName(e, out string nm);
                if (name != null && nm != name) continue;
                var bySize = new Dictionary<string, int>(); var small = new List<object>();
                var sb = Em.GetBuffer<RouteSegment>(e, true);
                for (int i = 0; i < sb.Length; i++)
                {
                    var sg = sb[i].m_Segment; if (!Em.HasBuffer<Game.Pathfind.PathElement>(sg)) continue;
                    var pe = Em.GetBuffer<Game.Pathfind.PathElement>(sg, true);
                    for (int k = 0; k < pe.Length; k++)
                    {
                        var l = pe[k].m_Target; if (!Em.Exists(l) || !Em.HasComponent<PrefabRef>(l)) continue;
                        var lp = Em.GetComponentData<PrefabRef>(l).m_Prefab;
                        if (!Em.HasComponent<Game.Prefabs.CarLaneData>(lp)) { bySize["(not a car lane) " + PrefabName(lp)] = bySize.TryGetValue("(not a car lane) " + PrefabName(lp), out int q0) ? q0 + 1 : 1; continue; }
                        var cl = Em.GetComponentData<Game.Prefabs.CarLaneData>(lp);
                        string key = cl.m_MaxSize + " " + cl.m_RoadTypes;
                        bySize[key] = bySize.TryGetValue(key, out int q) ? q + 1 : 1;
                        if (cl.m_MaxSize < Game.Vehicles.SizeClass.Large && small.Count < 8)
                        {
                            Entity top = l; for (int z = 0; z < 6 && Em.HasComponent<Owner>(top); z++) top = Em.GetComponentData<Owner>(top).m_Owner;
                            small.Add(PrefabName(lp) + " on " + (Em.HasComponent<PrefabRef>(top) ? PrefabName(Em.GetComponentData<PrefabRef>(top).m_Prefab) : Id(top)));
                        }
                    }
                }
                r["line"] = nm; r["pathLanesBySize"] = bySize; r["tooSmallSamples"] = small;
                break;
            }
            es.Dispose();
            return r;
        }

        /// <summary>A lead vehicle (Controller is itself or none) that is moving: not ParkedCar / ParkedTrain, and not
        /// DummyTraffic (through traffic from the outside connections). Decision 164's "vehicle running".</summary>
        static bool IsMovingLead(Entity v)
        {
            if (Em.HasComponent<Game.Vehicles.Controller>(v)) { var c = Em.GetComponentData<Game.Vehicles.Controller>(v).m_Controller; if (c != Entity.Null && c != v) return false; }
            if (Em.HasComponent<Game.Vehicles.ParkedCar>(v) || Em.HasComponent<Game.Vehicles.ParkedTrain>(v)) return false;
            if (Em.HasComponent<Game.Vehicles.PublicTransport>(v) && (Em.GetComponentData<Game.Vehicles.PublicTransport>(v).m_State & Game.Vehicles.PublicTransportFlags.DummyTraffic) != 0) return false;
            return true;
        }

        /// <summary>Diagnostic (bus dispatch, lead a): each bus depot's spawn locations (its sub-objects with SpawnLocation:
        /// prefab, connection type, road types, connected lanes and their owners, flags) and its component list.</summary>
        static object DepotSpawns()
        {
            var r = new List<object>();
            var dq = Query(new[] { ComponentType.ReadOnly<Game.Buildings.TransportDepot>(), ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var d in dq)
            {
                var pf = Em.GetComponentData<PrefabRef>(d).m_Prefab;
                if (!Em.HasComponent<TransportDepotData>(pf) || Em.GetComponentData<TransportDepotData>(pf).m_TransportType != TransportType.Bus) continue;
                var spawns = new List<object>();
                var stack = new List<Entity> { d };
                for (int i = 0; i < stack.Count && i < 500; i++)
                {
                    var e = stack[i];
                    if (Em.HasBuffer<Game.Objects.SubObject>(e)) { var sb = Em.GetBuffer<Game.Objects.SubObject>(e, true); for (int k = 0; k < sb.Length; k++) stack.Add(sb[k].m_SubObject); }
                    if (!Em.HasComponent<Game.Objects.SpawnLocation>(e)) continue;
                    var sl = Em.GetComponentData<Game.Objects.SpawnLocation>(e);
                    var spf = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                    var it = new Dictionary<string, object> { { "entity", Id(e) }, { "prefab", PrefabName(spf) }, { "flags", sl.m_Flags.ToString() },
                        { "lane1", LaneDesc(sl.m_ConnectedLane1) }, { "lane2", LaneDesc(sl.m_ConnectedLane2) }, { "curve", new List<object> { sl.m_CurvePosition1, sl.m_CurvePosition2 } }, { "access", Id(sl.m_AccessRestriction) } };
                    if (Em.HasComponent<Game.Prefabs.SpawnLocationData>(spf)) { var sd = Em.GetComponentData<Game.Prefabs.SpawnLocationData>(spf); it["connection"] = sd.m_ConnectionType.ToString(); it["roadTypes"] = sd.m_RoadTypes.ToString(); }
                    spawns.Add(it);
                }
                var ct = Em.GetComponentTypes(d, Allocator.Temp); var tn = new List<object>(); foreach (var t in ct) tn.Add(t.GetManagedType()?.Name ?? "?"); ct.Dispose();
                r.Add(new Dictionary<string, object> { { "depot", Id(d) }, { "prefab", PrefabName(pf) }, { "pos", V(Em.GetComponentData<Game.Objects.Transform>(d).m_Position) }, { "spawns", spawns }, { "components", tn } });
            }
            dq.Dispose();
            return r;
        }

        static string LaneDesc(Entity l)
        {
            if (l == Entity.Null) return "null";
            if (!Em.Exists(l)) return "missing " + Id(l);
            string own = "";
            if (Em.HasComponent<Owner>(l)) { var o = Em.GetComponentData<Owner>(l).m_Owner; own = " owner " + (Em.HasComponent<PrefabRef>(o) ? PrefabName(Em.GetComponentData<PrefabRef>(o).m_Prefab) : Id(o)); }
            return Id(l) + " " + (Em.HasComponent<PrefabRef>(l) ? PrefabName(Em.GetComponentData<PrefabRef>(l).m_Prefab) : "?") + own;
        }

        /// <summary>Diagnostic (bus dispatch, lead b): replaces every bus depot by the same prefab at the same transform,
        /// built the game's way (a Temp object definition and the forced apply frame, like the object tool), after
        /// deleting the placer's one. [phase tool, pump tool]</summary>
        static IEnumerator DepotRebuild(Dictionary<string, object> a)
        {
            var todo = new List<KeyValuePair<Entity, Game.Objects.Transform>>();
            var dq = Query(new[] { ComponentType.ReadOnly<Game.Buildings.TransportDepot>(), ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var d in dq)
            {
                var pf = Em.GetComponentData<PrefabRef>(d).m_Prefab;
                if (!Em.HasComponent<TransportDepotData>(pf) || Em.GetComponentData<TransportDepotData>(pf).m_TransportType != TransportType.Bus) continue;
                todo.Add(new KeyValuePair<Entity, Game.Objects.Transform>(pf, Em.GetComponentData<Game.Objects.Transform>(d)));
                Em.AddComponent<Deleted>(d);
            }
            dq.Dispose();
            for (int i = 0; i < 20; i++) yield return null;
            foreach (var kv in todo)
            {
                var def = Em.CreateEntity();
                Em.AddComponentData(def, new CreationDefinition { m_Prefab = kv.Key, m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue) });
                Em.AddComponentData(def, new ObjectDefinition
                {
                    m_Position = kv.Value.m_Position, m_LocalPosition = float3.zero, m_Scale = new float3(1f), m_Rotation = kv.Value.m_Rotation, m_LocalRotation = quaternion.identity,
                    m_Intensity = 1f, m_ParentMesh = -1, m_GroupIndex = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                });
                Em.AddComponent<Updated>(def);
                Em.AddComponent<Deleted>(def);
            }
            yield return null;
            bool applied = SetApply();
            for (int i = 0; i < 90; i++) yield return null;
            yield return new Dictionary<string, object> { { "op", "depot.rebuild" }, { "rebuilt", todo.Count }, { "applied", applied } };
        }

        /// <summary>Diagnostic (Bus Line 2 lead): deletes top-level edges within r of (x,z) that are neither road nor track
        /// (power lines, pipes: the utility outside-connection stubs), then their nodes left without edges, with their
        /// sub-objects (the outside-connection markers). [phase tool, pump tool]</summary>
        static IEnumerator UtilityDeleteNear(Dictionary<string, object> a)
        {
            float2 q = new float2(F(a, "x", 0f), F(a, "z", 0f)); float r = F(a, "r", 60f);
            var gone = new List<object>(); var ends = new HashSet<Entity>();
            var es = Query(new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var e in es)
            {
                if (Em.HasComponent<Owner>(e)) continue;
                var pf = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                if (Em.HasComponent<RoadData>(pf) || Em.HasComponent<TrackData>(pf)) continue;
                var c = Em.GetComponentData<Curve>(e).m_Bezier;
                if (math.min(math.distance(c.a.xz, q), math.distance(c.d.xz, q)) > r) continue;
                var ed = Em.GetComponentData<Edge>(e); ends.Add(ed.m_Start); ends.Add(ed.m_End);
                gone.Add(PrefabName(pf) + " " + V(c.a) + " -> " + V(c.d));
                DelWithSubs(e);
            }
            es.Dispose();
            yield return null;
            int nodes = 0;
            foreach (var n in ends) if (IsReal(n) && (!Em.HasBuffer<ConnectedEdge>(n) || Em.GetBuffer<ConnectedEdge>(n, true).Length == 0)) { DelWithSubs(n); nodes++; }
            for (int i = 0; i < 30; i++) yield return null;
            yield return new Dictionary<string, object> { { "op", "utility.deleteNear" }, { "edges", gone }, { "nodes", nodes } };
        }

        static void DelWithSubs(Entity e)
        {
            var subs = new List<Entity>();
            if (Em.HasBuffer<Game.Objects.SubObject>(e)) { var b = Em.GetBuffer<Game.Objects.SubObject>(e, true); for (int i = 0; i < b.Length; i++) subs.Add(b[i].m_SubObject); }
            Em.AddComponent<Deleted>(e);
            foreach (var s in subs) if (IsReal(s)) Em.AddComponent<Deleted>(s);
        }

        static object Counts()
        {
            var byType = new Dictionary<string, int>(); var moving = new Dictionary<string, int>();
            var vs = Query(new[] { ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var v in vs)
            {
                string n = PrefabName(Em.GetComponentData<PrefabRef>(v).m_Prefab);
                byType[n] = byType.TryGetValue(n, out int k) ? k + 1 : 1;
                if (IsMovingLead(v)) moving[n] = moving.TryGetValue(n, out int m) ? m + 1 : 1;
            }
            int total = vs.Length;
            vs.Dispose();
            int depots = 0;
            var bs = Query(new[] { ComponentType.ReadOnly<Game.Buildings.TransportDepot>() });
            depots = bs.Length; bs.Dispose();
            var rs = Query(new[] { ComponentType.ReadOnly<Route>(), ComponentType.ReadOnly<TransportLine>() });
            int routes = rs.Length; rs.Dispose();
            var ss = Query(new[] { ComponentType.ReadOnly<Game.Routes.TransportStop>() });
            int stops = ss.Length; ss.Dispose();
            return new Dictionary<string, object> { { "publicTransportVehicles", total }, { "byPrefab", byType }, { "movingByPrefab", moving }, { "depots", depots }, { "routes", routes }, { "stops", stops }, { "frame", UnityEngine.Time.frameCount } };
        }

        static object Infoviews(string name)
        {
            var names = new List<object>();
            InfoviewPrefab pick = null;
            var ps = Query(new[] { ComponentType.ReadOnly<InfoviewData>(), ComponentType.ReadOnly<PrefabData>() }, new ComponentType[0]);
            foreach (var p in ps)
            {
                var pf = Prefabs.GetPrefab<InfoviewPrefab>(p);
                if (pf == null) continue;
                names.Add(pf.name);
                if (name != null && pf.name == name) pick = pf;
            }
            ps.Dispose();
            if (name == null) return new Dictionary<string, object> { { "infoviews", names } };
            var ts = W.GetOrCreateSystemManaged<ToolSystem>();
            if (name.Length == 0) { ts.infoview = null; return new Dictionary<string, object> { { "infoview", null } }; }
            if (pick == null) throw new ArgumentException("no infoview " + name + "; have: " + string.Join(", ", names));
            ts.infoview = pick;
            return new Dictionary<string, object> { { "infoview", pick.name } };
        }

        // ---------------------------------------------------------------- helpers

        static UnityEngine.Color32 ParseColor(string s)
        {
            s = (s ?? "").TrimStart('#');
            if (s.Length < 6) return new UnityEngine.Color32(128, 128, 128, 255);
            return new UnityEngine.Color32(Convert.ToByte(s.Substring(0, 2), 16), Convert.ToByte(s.Substring(2, 2), 16), Convert.ToByte(s.Substring(4, 2), 16), 255);
        }
        static string Id(Entity e) => e.Index + ":" + e.Version;
        static List<object> V(float3 v) => new List<object> { v.x, v.y, v.z };
        static string Str(Dictionary<string, object> a, string k, string d) => a != null && a.TryGetValue(k, out var v) && v != null ? v.ToString() : d;
        static float F(Dictionary<string, object> a, string k, float d = 0f) => a != null && a.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v) : d;
        static bool B(Dictionary<string, object> a, string k) => a != null && a.TryGetValue(k, out var v) && v is bool b && b;
    }
}
