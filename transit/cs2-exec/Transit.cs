// CS1 transit lines → CS2 stops and transport lines, as a hot-loaded exec library for the CS2 bridge.
// Entry point: Cs1ConvTransit.Ops.Run(args), dispatched on args["op"]. transit/transit.mjs drives it.
// Background: transit/NOTES-cs2-transit.md.
//
// Ops (CS2 world metres):
//   prefabs      {}                                  line prefabs (RouteData + TransportLineData), stop prefabs
//                                                    (TransportStopData objects) and depot prefabs, with transport type
//   stops.create {stops:[{key,x,z,tx,tz,side,prefab}], inset=1.5, radius=12}   [phase tool]
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
//   lines.name   {routes:[{route:"index:version",name,number}]}   [phase main] custom names + route numbers
//   lines.list   {}                                  routes: prefab, name, number, colour, stops, segments, complete,
//                                                    vehicles
//   counts       {}                                  public-transport vehicles by type, routes, stops, depots
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
                case "counts": return Counts();
                case "infoviews": return Infoviews(null);
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
                { "lines", results },
            };
        }

        /// <summary>ToolSystem.applyMode is the last-updated tool's applyMode (protected setter). The active tool has
        /// already updated this frame, so setting it to Apply makes ToolOutputSystem run the ApplyTool phase.</summary>
        static bool SetApply()
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

        static object LinesList()
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var r = new List<object>();
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
                    { "vehicles", Em.HasBuffer<RouteVehicle>(e) ? Em.GetBuffer<RouteVehicle>(e, true).Length : 0 },
                });
            }
            es.Dispose();
            return r;
        }

        static object Counts()
        {
            var byType = new Dictionary<string, int>();
            var vs = Query(new[] { ComponentType.ReadOnly<Game.Vehicles.PublicTransport>(), ComponentType.ReadOnly<PrefabRef>() });
            foreach (var v in vs)
            {
                string n = PrefabName(Em.GetComponentData<PrefabRef>(v).m_Prefab);
                byType[n] = byType.TryGetValue(n, out int k) ? k + 1 : 1;
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
            return new Dictionary<string, object> { { "publicTransportVehicles", total }, { "byPrefab", byType }, { "depots", depots }, { "routes", routes }, { "stops", stops }, { "frame", UnityEngine.Time.frameCount } };
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
