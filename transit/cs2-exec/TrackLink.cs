// Links rail/subway yards to the converted lines' track network, so CS2 can dispatch trains and metros.
// Ops (through Cs1ConvTransit.Ops.Run): yards.inspect, tracklink. Background: transit/NOTES-cs2-transit.md, "Yards".
//
//   yards.plan {bounds:{minX,maxX,minZ,maxZ}, lines:[{name,cs1Type,stops?}], depotMode?:{Metro,Train}, edgeMargin=80}
//        docs/DECISIONS.md #83: a rail/subway yard only at the crop edge, where that type's imported track leaves the
//        crop (an open track end within edgeMargin of the plan bounds). Returns yard items in the buildings-plan
//        item shape (for buildings' placer) and report entries for the lines that get none ("place it yourself").
//   yards.inspect {}                       every Subway/Train depot building: its sub-net tracks and their open ends
//   tracklink {maxDistance=2500, candidates=40, allowCrossings=true, dryRun=false}   [phase + pump tool]
//        for each Subway/Train yard: the open ends of its own track sub-net (nodes with one edge, owned by the yard)
//        are paired with track nodes of the same track type outside any building (the imported network); the
//        nearest pair whose straight course crosses no other edge gets one permanent track course, from the yard end
//        (CoursePos.m_Entity = node) to the network node, with the network edge's prefab and both end elevations.
//        Train/subway STATIONS first: every open end of their platform tracks is joined to the nearest imported track
//        dead end (stationReach=200 m, each used once): CS1 platforms are not imported, so without this the lines
//        have no path (stations:false skips it).
//        If every candidate crosses another edge, the shortest one is built the player's way instead: a Temp
//        definition plus one forced apply frame, so CS2 itself makes the level crossings / track junctions.
//        A yard that is already connected (a sub-net node with an outside track edge) is left alone.
using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs1ConvTransit
{
    public static class TrackLink
    {
        static World W => World.DefaultGameObjectInjectionWorld;
        static EntityManager Em => W.EntityManager;
        static PrefabSystem Prefabs => W.GetOrCreateSystemManaged<PrefabSystem>();
        static string PrefabName(Entity p) { try { return Prefabs.GetPrefabName(p); } catch { return "?"; } }
        static bool IsReal(Entity e) => e != Entity.Null && Em.Exists(e) && !Em.HasComponent<Temp>(e) && !Em.HasComponent<Deleted>(e);
        static string Id(Entity e) => e.Index + ":" + e.Version;
        static List<object> V(float3 v) => new List<object> { v.x, v.y, v.z };

        struct Yard { public Entity Building; public TransportType Type; public TrackTypes Track; }

        static List<Yard> Yards()
        {
            var r = new List<Yard>();
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!Em.HasComponent<TransportDepotData>(prefab)) continue;
                    var t = Em.GetComponentData<TransportDepotData>(prefab).m_TransportType;
                    if (t == TransportType.Subway) r.Add(new Yard { Building = e, Type = t, Track = TrackTypes.Subway });
                    else if (t == TransportType.Train) r.Add(new Yard { Building = e, Type = t, Track = TrackTypes.Train });
                }
            q.Dispose();
            return r;
        }

        static List<Yard> Stations()
        {
            var r = new List<Yard>();
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    var prefab = Em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (!Em.HasComponent<TransportStationData>(prefab) || Em.HasComponent<TransportDepotData>(prefab)) continue;
                    // which track types its own sub-net carries
                    var types = TrackTypes.None;
                    if (Em.HasBuffer<Game.Net.SubNet>(e))
                    {
                        var buf = Em.GetBuffer<Game.Net.SubNet>(e, true);
                        for (int i = 0; i < buf.Length; i++) if (Em.Exists(buf[i].m_SubNet) && Em.HasComponent<Edge>(buf[i].m_SubNet)) types |= EdgeTrack(buf[i].m_SubNet);
                    }
                    if ((types & TrackTypes.Train) != 0) r.Add(new Yard { Building = e, Type = TransportType.Train, Track = TrackTypes.Train });
                    if ((types & TrackTypes.Subway) != 0) r.Add(new Yard { Building = e, Type = TransportType.Subway, Track = TrackTypes.Subway });
                }
            q.Dispose();
            return r;
        }

        static TrackTypes EdgeTrack(Entity edge)
        {
            if (!Em.HasComponent<PrefabRef>(edge)) return TrackTypes.None;
            var p = Em.GetComponentData<PrefabRef>(edge).m_Prefab;
            return Em.HasComponent<TrackData>(p) ? Em.GetComponentData<TrackData>(p).m_TrackType : TrackTypes.None;
        }

        static List<Entity> ConnectedEdges(Entity node)
        {
            var r = new List<Entity>();
            if (!Em.HasBuffer<ConnectedEdge>(node)) return r;
            var b = Em.GetBuffer<ConnectedEdge>(node, true);
            for (int i = 0; i < b.Length; i++) if (IsReal(b[i].m_Edge)) r.Add(b[i].m_Edge);
            return r;
        }

        /// <summary>The building that owns an entity, following Owner links (sub-nets of sub-objects, …).</summary>
        static Entity TopOwner(Entity e)
        {
            for (int i = 0; i < 8 && e != Entity.Null && Em.HasComponent<Owner>(e); i++) e = Em.GetComponentData<Owner>(e).m_Owner;
            return e;
        }

        /// <summary>Open ends of the yard's own tracks, and whether the yard already links to outside track.</summary>
        static List<Entity> YardEnds(Yard y, out bool connected, out int subEdges)
        {
            var ends = new List<Entity>();
            connected = false; subEdges = 0;
            var nodes = new HashSet<Entity>();
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Owner>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    if (TopOwner(e) != y.Building || (EdgeTrack(e) & y.Track) == 0) continue;
                    subEdges++;
                    var ed = Em.GetComponentData<Edge>(e);
                    nodes.Add(ed.m_Start); nodes.Add(ed.m_End);
                }
            q.Dispose();
            foreach (var n in nodes)
            {
                if (!IsReal(n)) continue;
                var ce = ConnectedEdges(n);
                int track = 0, outside = 0;
                foreach (var e in ce)
                {
                    if ((EdgeTrack(e) & y.Track) == 0) continue;
                    track++;
                    if (TopOwner(e) != y.Building) outside++;
                }
                if (outside > 0) connected = true;
                if (track == 1) ends.Add(n);
            }
            return ends;
        }

        /// <summary>Diagnostic: for each route segment of the named line(s), is the end lane's edge reachable from the
        /// start lane's edge over track edges of the line's track type (ignoring lane directions)? If not, the
        /// reached node nearest to the target edge, and how far it is.</summary>
        public static object Reach(string name)
        {
            var names = W.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var r = new List<object>();
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Routes.Route>(), ComponentType.ReadOnly<Game.Routes.TransportLine>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var route in rs)
                {
                    names.TryGetCustomName(route, out string custom);
                    if (name != null && custom != name) continue;
                    var prefab = Em.GetComponentData<PrefabRef>(route).m_Prefab;
                    var tt = Em.HasComponent<TransportLineData>(prefab) ? Em.GetComponentData<TransportLineData>(prefab).m_TransportType : TransportType.None;
                    var track = tt == TransportType.Train ? TrackTypes.Train : tt == TransportType.Subway ? TrackTypes.Subway : tt == TransportType.Tram ? TrackTypes.Tram : TrackTypes.None;
                    if (track == TrackTypes.None || !Em.HasBuffer<Game.Routes.RouteSegment>(route)) continue;
                    var segs = Em.GetBuffer<Game.Routes.RouteSegment>(route, true);
                    var list = new List<object>();
                    for (int i = 0; i < segs.Length; i++)
                    {
                        var sg = segs[i].m_Segment;
                        if (!Em.HasComponent<Game.Routes.PathTargets>(sg)) { list.Add("no PathTargets"); continue; }
                        var pt = Em.GetComponentData<Game.Routes.PathTargets>(sg);
                        Entity e0 = LaneEdge(pt.m_StartLane), e1 = LaneEdge(pt.m_EndLane);
                        var it = new Dictionary<string, object> { { "seg", Id(sg) }, { "from", Id(e0) }, { "to", Id(e1) } };
                        if (e0 == Entity.Null || e1 == Entity.Null) { it["reach"] = "no lane edge"; list.Add(it); continue; }
                        // BFS over edges
                        var seen = new HashSet<Entity> { e0 }; var queue = new List<Entity> { e0 }; int qi = 0;
                        bool found = e0 == e1; int visited = 0;
                        var reachedNodes = new HashSet<Entity>(); var deadEnds = 0;
                        while (!found && qi < queue.Count && visited < 50000)
                        {
                            var e = queue[qi++]; visited++;
                            var ed = Em.GetComponentData<Edge>(e);
                            foreach (var n in new[] { ed.m_Start, ed.m_End })
                            {
                                if (!reachedNodes.Add(n)) continue;
                                int k = 0;
                                foreach (var ne in ConnectedEdges(n))
                                {
                                    if ((EdgeTrack(ne) & track) == 0) continue;
                                    k++;
                                    if (ne == e1) { found = true; break; }
                                    if (seen.Add(ne)) queue.Add(ne);
                                }
                                if (k == 1) deadEnds++;
                                if (found) break;
                            }
                        }
                        it["reach"] = found; it["visitedEdges"] = visited; it["deadEndsReached"] = deadEnds;
                        if (!found)
                        {
                            var ted = Em.GetComponentData<Edge>(e1);
                            float3 tp = Em.GetComponentData<Node>(ted.m_Start).m_Position;
                            float best = float.MaxValue; Entity bn = Entity.Null;
                            foreach (var n in reachedNodes) { if (!Em.HasComponent<Node>(n)) continue; float dd = math.distance(Em.GetComponentData<Node>(n).m_Position, tp); if (dd < best) { best = dd; bn = n; } }
                            it["targetPos"] = V(tp);
                            if (bn != Entity.Null) { it["nearestReached"] = V(Em.GetComponentData<Node>(bn).m_Position); it["gap"] = best; it["nearestEdges"] = ConnectedEdges(bn).Count; }
                        }
                        list.Add(it);
                    }
                    r.Add(new Dictionary<string, object> { { "name", custom }, { "type", tt.ToString() }, { "segments", list } });
                }
            q.Dispose();
            return r;
        }

        static Entity LaneEdge(Entity lane)
        {
            if (lane == Entity.Null || !Em.Exists(lane) || !Em.HasComponent<Owner>(lane)) return Entity.Null;
            var o = Em.GetComponentData<Owner>(lane).m_Owner;
            return Em.Exists(o) && Em.HasComponent<Edge>(o) ? o : Entity.Null;
        }

        public static object Inspect()
        {
            var r = new List<object>();
            foreach (var y in Yards())
            {
                var ends = YardEnds(y, out bool connected, out int subEdges);
                var endList = new List<object>();
                foreach (var n in ends) endList.Add(new Dictionary<string, object> { { "node", Id(n) }, { "pos", V(Em.GetComponentData<Node>(n).m_Position) }, { "owned", Em.HasComponent<Owner>(n) } });
                r.Add(new Dictionary<string, object>
                {
                    { "building", Id(y.Building) }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(y.Building).m_Prefab) }, { "type", y.Type.ToString() },
                    { "pos", Em.HasComponent<Game.Objects.Transform>(y.Building) ? V(Em.GetComponentData<Game.Objects.Transform>(y.Building).m_Position) : null },
                    { "subTrackEdges", subEdges }, { "openEnds", endList }, { "connected", connected },
                    { "detail", Detail(y.Building) },
                });
            }
            return new Dictionary<string, object> { { "yards", r } };
        }

        /// <summary>What a depot building carries: sub-nets (with components), sub-lanes, sub-objects, nearby nodes.</summary>
        static Dictionary<string, object> Detail(Entity b)
        {
            var d = new Dictionary<string, object>();
            var subnets = new List<object>();
            if (Em.HasBuffer<Game.Net.SubNet>(b))
            {
                var buf = Em.GetBuffer<Game.Net.SubNet>(b, true);
                for (int i = 0; i < buf.Length && i < 40; i++)
                {
                    var e = buf[i].m_SubNet;
                    var it = new Dictionary<string, object> { { "entity", Id(e) }, { "real", IsReal(e) } };
                    if (Em.Exists(e))
                    {
                        if (Em.HasComponent<PrefabRef>(e)) it["prefab"] = PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab);
                        it["edge"] = Em.HasComponent<Edge>(e); it["node"] = Em.HasComponent<Node>(e);
                        it["track"] = EdgeTrack(e).ToString();
                        if (Em.HasComponent<Node>(e)) { it["pos"] = V(Em.GetComponentData<Node>(e).m_Position); it["edges"] = ConnectedEdges(e).Count; }
                        if (Em.HasComponent<Owner>(e)) it["owner"] = Id(Em.GetComponentData<Owner>(e).m_Owner);
                        var types = Em.GetComponentTypes(e, Allocator.Temp);
                        var tn = new List<object>(); foreach (var t in types) tn.Add(t.GetManagedType()?.Name ?? "?"); types.Dispose();
                        it["components"] = tn;
                    }
                    subnets.Add(it);
                }
                d["subNetCount"] = buf.Length;
            }
            d["subNets"] = subnets;
            if (Em.HasBuffer<Game.Net.SubLane>(b))
            {
                var buf = Em.GetBuffer<Game.Net.SubLane>(b, true);
                var names = new Dictionary<string, int>();
                for (int i = 0; i < buf.Length; i++) { var l = buf[i].m_SubLane; string n = Em.HasComponent<PrefabRef>(l) ? PrefabName(Em.GetComponentData<PrefabRef>(l).m_Prefab) : "?"; names[n] = names.TryGetValue(n, out int c) ? c + 1 : 1; }
                d["subLanes"] = names;
            }
            if (Em.HasBuffer<Game.Objects.SubObject>(b))
            {
                var buf = Em.GetBuffer<Game.Objects.SubObject>(b, true);
                var names = new Dictionary<string, int>();
                for (int i = 0; i < buf.Length; i++) { var o = buf[i].m_SubObject; string n = Em.HasComponent<PrefabRef>(o) ? PrefabName(Em.GetComponentData<PrefabRef>(o).m_Prefab) : "?"; names[n] = names.TryGetValue(n, out int c) ? c + 1 : 1; }
                d["subObjects"] = names;
            }
            var prefab = Em.GetComponentData<PrefabRef>(b).m_Prefab;
            var pt = Em.GetComponentTypes(prefab, Allocator.Temp);
            var ptn = new List<object>(); foreach (var t in pt) ptn.Add(t.GetManagedType()?.Name ?? "?"); pt.Dispose();
            d["prefabComponents"] = ptn;
            return d;
        }

        struct Target { public Entity Node; public float3 Pos; public Entity Edge; public Entity Prefab; }

        /// <summary>Track nodes of the given type that belong to no building (the imported line network).</summary>
        static List<Target> NetworkNodes(TrackTypes track)
        {
            var r = new List<Target>();
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            });
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var n in es)
                    foreach (var e in ConnectedEdges(n))
                        if ((EdgeTrack(e) & track) != 0 && !Em.HasComponent<Owner>(e)) { r.Add(new Target { Node = n, Pos = Em.GetComponentData<Node>(n).m_Position, Edge = e }); break; }
            q.Dispose();
            return r;
        }

        /// <summary>Track nodes the lines of this transport type actually run over: the owner edges of the lanes in the
        /// routes' segment paths (PathElement.m_Target), their unowned end nodes. pathElements = how many path elements
        /// those routes have (0 = CS2 found no path for the line).</summary>
        static List<Target> LineNodes(TransportType tt, TrackTypes track, out int routes, out int pathElements)
        {
            routes = 0; pathElements = 0;
            var edges = new HashSet<Entity>();
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Routes.Route>(), ComponentType.ReadOnly<Game.Routes.TransportLine>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            using (var rs = q.ToEntityArray(Allocator.Temp))
                foreach (var r in rs)
                {
                    var prefab = Em.GetComponentData<PrefabRef>(r).m_Prefab;
                    if (!Em.HasComponent<TransportLineData>(prefab) || Em.GetComponentData<TransportLineData>(prefab).m_TransportType != tt) continue;
                    routes++;
                    if (!Em.HasBuffer<Game.Routes.RouteSegment>(r)) continue;
                    var segs = Em.GetBuffer<Game.Routes.RouteSegment>(r, true);
                    for (int i = 0; i < segs.Length; i++)
                    {
                        var seg = segs[i].m_Segment;
                        if (!Em.Exists(seg) || !Em.HasBuffer<Game.Pathfind.PathElement>(seg)) continue;
                        var path = Em.GetBuffer<Game.Pathfind.PathElement>(seg, true);
                        pathElements += path.Length;
                        for (int k = 0; k < path.Length; k++)
                        {
                            var lane = path[k].m_Target;
                            if (!Em.Exists(lane) || !Em.HasComponent<Owner>(lane)) continue;
                            var edge = Em.GetComponentData<Owner>(lane).m_Owner;
                            if (Em.Exists(edge) && Em.HasComponent<Edge>(edge) && (EdgeTrack(edge) & track) != 0) edges.Add(edge);
                        }
                    }
                }
            q.Dispose();
            var r2 = new List<Target>(); var seen = new HashSet<Entity>();
            foreach (var e in edges)
            {
                if (Em.HasComponent<Owner>(e)) continue; // station platforms etc.: join the open line, not a building's sub-net
                var ed = Em.GetComponentData<Edge>(e);
                foreach (var n in new[] { ed.m_Start, ed.m_End })
                    if (IsReal(n) && !Em.HasComponent<Owner>(n) && seen.Add(n)) r2.Add(new Target { Node = n, Pos = Em.GetComponentData<Node>(n).m_Position, Edge = e });
            }
            return r2;
        }

        /// <summary>Does the straight course a→b cross (in plan view) any real edge other than those at its two ends?</summary>
        static bool Crosses(float3 a, float3 b, Entity na, Entity nb, out string what)
        {
            what = null;
            var skip = new HashSet<Entity>(ConnectedEdges(na));
            foreach (var e in ConnectedEdges(nb)) skip.Add(e);
            var seg = new Line2.Segment(a.xz, b.xz);
            var q = Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            try
            {
                using (var es = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in es)
                    {
                        if (skip.Contains(e)) continue;
                        var c = Em.GetComponentData<Curve>(e).m_Bezier;
                        // cheap reject on bounds, then sample the curve as 8 chords
                        var bb = MathUtils.Bounds(c.xz);
                        var sb = new Bounds2(math.min(a.xz, b.xz) - 2f, math.max(a.xz, b.xz) + 2f);
                        if (!MathUtils.Intersect(bb, sb)) continue;
                        float2 p0 = c.a.xz;
                        for (int i = 1; i <= 8; i++)
                        {
                            float2 p1 = MathUtils.Position(c, i / 8f).xz;
                            if (MathUtils.Intersect(seg, new Line2.Segment(p0, p1), out float2 t) && t.x > 0.01f && t.x < 0.99f)
                            { what = Id(e) + " " + PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab); return true; }
                            p0 = p1;
                        }
                    }
            }
            finally { q.Dispose(); }
            return false;
        }

        static readonly Dictionary<string, KeyValuePair<TrackTypes, TransportType>> LineTypes = new Dictionary<string, KeyValuePair<TrackTypes, TransportType>>
        {
            { "Metro", new KeyValuePair<TrackTypes, TransportType>(TrackTypes.Subway, TransportType.Subway) },
            { "Train", new KeyValuePair<TrackTypes, TransportType>(TrackTypes.Train, TransportType.Train) },
        };
        static readonly Dictionary<string, string[]> YardPrefabs = new Dictionary<string, string[]>
        {
            { "Metro", new[] { "SubwayYard02", "SubwayYard01" } }, // 02 is the compact one (4x23 cells vs 44x20)
            { "Train", new[] { "RailYard02", "RailYard01" } },     // 02: 34x15 vs 44x28
        };

        public static object Plan(Dictionary<string, object> a)
        {
            var bounds = a.TryGetValue("bounds", out var bo) ? bo as Dictionary<string, object> : null;
            if (bounds == null) throw new ArgumentException("bounds {minX,maxX,minZ,maxZ} required");
            float minX = F(bounds, "minX", 0), maxX = F(bounds, "maxX", 0), minZ = F(bounds, "minZ", 0), maxZ = F(bounds, "maxZ", 0);
            float margin = F(a, "edgeMargin", 80f);
            var mode = a.TryGetValue("depotMode", out var mo) ? mo as Dictionary<string, object> : null;
            var byType = new Dictionary<string, List<Dictionary<string, object>>>();
            if (a.TryGetValue("lines", out var lo) && lo is IList<object> ll)
                foreach (var o in ll)
                {
                    var l = o as Dictionary<string, object>;
                    string t = l != null && l.TryGetValue("cs1Type", out var tv) ? tv as string : null;
                    if (t == null || !LineTypes.ContainsKey(t)) continue;
                    if (!byType.TryGetValue(t, out var list)) byType[t] = list = new List<Dictionary<string, object>>();
                    list.Add(l);
                }
            var existing = Yards();
            var items = new List<object>(); var report = new List<object>(); var types = new List<object>();
            int nextId = -100;
            foreach (var kv in byType)
            {
                string type = kv.Key; var track = LineTypes[type].Key; var tt = LineTypes[type].Value;
                var names = kv.Value.ConvertAll(l => l.TryGetValue("name", out var n) ? (object)n : "?");
                string kind = type == "Metro" ? "subway yard" : "rail yard";
                var info = new Dictionary<string, object> { { "type", type }, { "lines", names } };
                types.Add(info);
                if (existing.Exists(y => y.Type == tt)) { info["status"] = "depot-exists"; continue; }
                string m = mode != null && mode.TryGetValue(type, out var mv) ? mv as string : "edge";
                if (m == "manual")
                {
                    info["status"] = "manual";
                    foreach (var n in names) report.Add(new Dictionary<string, object> { { "level", "warning" }, { "kind", "depot-manual" }, { "line", n }, { "message", n + ": no depot placed (your choice): place a " + kind + " yourself and connect its track to the line." } });
                    continue;
                }
                var ends = new List<KeyValuePair<float3, float2>>(); // position, inward direction
                foreach (var t in NetworkNodes(track))
                {
                    int tracks = 0; foreach (var e in ConnectedEdges(t.Node)) if ((EdgeTrack(e) & track) != 0) tracks++;
                    if (tracks != 1) continue;
                    float dx = math.min(t.Pos.x - minX, maxX - t.Pos.x), dz = math.min(t.Pos.z - minZ, maxZ - t.Pos.z);
                    if (math.min(dx, dz) > margin) continue;
                    float2 inward = dx < dz ? new float2(t.Pos.x - minX < maxX - t.Pos.x ? 1 : -1, 0) : new float2(0, t.Pos.z - minZ < maxZ - t.Pos.z ? 1 : -1);
                    ends.Add(new KeyValuePair<float3, float2>(t.Pos, inward));
                }
                info["edgeTrackEnds"] = ends.Count;
                string depotKind = "edge";
                if (ends.Count == 0 && m == "interior")
                {
                    // opt-in (depotMode {Metro|Train: "interior"}, transit.mjs depots --interior-yards): a yard beside an
                    // open dead end of the line's track inside the crop, continuing it. Dead ends that a station link
                    // or a bridge will use are left out (within 300 m of a station, or facing another dead end).
                    var bridged = new HashSet<Entity>();
                    foreach (var bl in Bridges(track, 300f, 30f, new HashSet<Entity>())) { bridged.Add(bl.A); bridged.Add(bl.B.Node); }
                    var stationPos = new List<float3>();
                    foreach (var st in Stations()) if (st.Track == track && Em.HasComponent<Game.Objects.Transform>(st.Building)) stationPos.Add(Em.GetComponentData<Game.Objects.Transform>(st.Building).m_Position);
                    foreach (var de in DeadEnds(track))
                    {
                        if (bridged.Contains(de.Key)) continue;
                        float3 dp = Em.GetComponentData<Node>(de.Key).m_Position;
                        if (stationPos.Exists(sp => math.distance(sp.xz, dp.xz) < 300f)) continue;
                        ends.Add(new KeyValuePair<float3, float2>(dp, de.Value));
                    }
                    info["interiorTrackEnds"] = ends.Count;
                    if (ends.Count == 0)
                    {
                        // no free dead end (all joined to stations or bridged): a node the lines' paths run over, on the
                        // ground or elevated (a ground yard can ramp up to it, not down into a tunnel), with the yard
                        // 150 m to the side of its track
                        var hd = W.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(true);
                        foreach (var ln in LineNodes(tt, track, out int _, out int _))
                        {
                            if (ln.Pos.y - TerrainUtils.SampleHeight(ref hd, ln.Pos) < -0.5f || !Em.HasComponent<Curve>(ln.Edge)) continue;
                            var bz = Em.GetComponentData<Curve>(ln.Edge).m_Bezier;
                            float2 along = math.normalizesafe(bz.d.xz - bz.a.xz);
                            ends.Add(new KeyValuePair<float3, float2>(ln.Pos, new float2(-along.y, along.x)));
                        }
                        info["interiorLineNodes"] = ends.Count;
                    }
                    depotKind = "interior";
                }
                if (ends.Count == 0)
                {
                    info["status"] = "no-edge-track";
                    foreach (var n in names) report.Add(new Dictionary<string, object> { { "level", "warning" }, { "kind", "depot-none" }, { "line", n }, { "message", n + ": its track does not leave the converted area, so no depot was placed: place a " + kind + " yourself and connect its track to the line." } });
                    continue;
                }
                float2 c = float2.zero; int cn = 0;
                foreach (var l in kv.Value)
                    if (l.TryGetValue("stops", out var so) && so is IList<object> sl)
                        foreach (var s0 in sl) { var sd = s0 as Dictionary<string, object>; c += new float2(F(sd, "x", 0), F(sd, "z", 0)); cn++; }
                if (cn > 0) c /= cn;
                var best = ends[0];
                foreach (var e in ends) if (math.distance(e.Key.xz, c) < math.distance(best.Key.xz, c)) best = e;
                float2 at = best.Key.xz + best.Value * (depotKind == "edge" ? 120f : 150f); // inside the crop, beside the track end
                var prefabs = YardPrefabs[type];
                items.Add(new Dictionary<string, object>
                {
                    { "id", nextId-- }, { "cs1Info", null }, { "type", "BuildingPrefab" }, { "prefab", prefabs[0] }, { "family", "depot" },
                    { "x", at.x }, { "z", at.y }, { "accessSegment", 0 }, { "front", null }, { "facing", new List<object> { best.Value.x, best.Value.y } },
                    // the smaller prefab only; a lot facing a road a building can use (not a highway), within the link's reach
                    { "alternatives", new List<object>() }, { "fallback", "anywhere" }, { "requireRoad", true }, { "maxDistance", F(a, "maxPlaceDistance", 2500f) },
                    { "default", true }, { "depot", depotKind },
                    { "trackEnd", new List<object> { best.Key.x, best.Key.z } }, { "lines", names },
                    { "reason", "no CS1 " + type.ToLowerInvariant() + " depot; " + kind + (depotKind == "edge" ? " at the crop edge where the track leaves, linked to it" : " beside an open end of the line's track inside the area (interior yards, opt-in), linked to it") },
                });
                info["status"] = depotKind == "edge" ? "yard" : "interior-yard"; info["trackEnd"] = new List<object> { best.Key.x, best.Key.z };
            }
            return new Dictionary<string, object> { { "op", "yards.plan" }, { "types", types }, { "buildings", new Dictionary<string, object> { { "items", items } } }, { "report", report } };
        }

        public static System.Collections.IEnumerator Link(Dictionary<string, object> a)
        {
            var pendingTemp = new List<Dictionary<string, object>>();
            var usedTargets = new HashSet<Entity>();
            float maxDist = F(a, "maxDistance", 2500f);
            int candidates = (int)F(a, "candidates", 40f);
            bool dry = a != null && a.TryGetValue("dryRun", out var d) && d is bool b && b;
            bool allowCrossings = !(a != null && a.TryGetValue("allowCrossings", out var ac) && ac is bool acb && !acb);
            var data = W.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(true);
            var results = new List<object>();
            var nets = new Dictionary<TrackTypes, List<Target>>();
            // stations first: CS1 station platforms are building sub-nets (not imported), so the CS2 station's own
            // platform tracks end loose next to the imported line track. Join each open end to the nearest imported
            // dead end of the same track type (each target used once), or the lines get no path at all.
            var stationResults = new List<object>();
            var trimEdges = new List<Entity>(); var trimNodes = new List<Entity>(); var trimTo = new List<Entity>();
            var deferred = new List<KeyValuePair<Dictionary<string, object>, KeyValuePair<Entity, Target>>>();
            if (!(a != null && a.TryGetValue("stations", out var st) && st is bool stb && !stb))
                foreach (var y in Stations())
                {
                    var ends = YardEnds(y, out bool connected0, out int subEdges0);
                    var sitem = new Dictionary<string, object> { { "building", Id(y.Building) }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(y.Building).m_Prefab) }, { "track", y.Track.ToString() }, { "openEnds", ends.Count }, { "subTrackEdges", subEdges0 } };
                    stationResults.Add(sitem);
                    var links = new List<object>();
                    float3 center = Em.HasComponent<Game.Objects.Transform>(y.Building) ? Em.GetComponentData<Game.Objects.Transform>(y.Building).m_Position : float3.zero;
                    if (!nets.TryGetValue(y.Track, out var net)) nets[y.Track] = net = NetworkNodes(y.Track);
                    var deadEnds = net.FindAll(t => { int k = 0; foreach (var e in ConnectedEdges(t.Node)) if ((EdgeTrack(e) & y.Track) != 0) k++; return k == 1; });
                    foreach (var en in ends)
                    {
                        float3 ep = Em.GetComponentData<Node>(en).m_Position;
                        Target best = default; float bd = F(a, "stationReach", 200f);
                        foreach (var t in deadEnds) { if (usedTargets.Contains(t.Node)) continue; float dd = math.distance(ep.xz, t.Pos.xz); if (dd < bd && dd > 0.5f) { bd = dd; best = t; } }
                        if (best.Node == Entity.Null) { links.Add(new Dictionary<string, object> { { "from", Id(en) }, { "status", "no-track-end-near" } }); continue; }
                        usedTargets.Add(best.Node);
                        var li = new Dictionary<string, object>();
                        // CS2 train platforms are 200 m long, CS1's 128 m: at the CS1 spot the platform runs on past
                        // the imported track's dead end, which then lies under the platform (behind its end, close to
                        // its axis). Trim the imported track back to the first node beyond the platform end and join
                        // that node instead (QA 5c, buildings decision 36).
                        float2 dir = math.normalizesafe((ep - center).xz);
                        float2 rel = (best.Pos - ep).xz;
                        float along = math.dot(rel, dir), lateral = math.abs(math.dot(rel, new float2(-dir.y, dir.x)));
                        if (along < -1f && lateral < F(a, "overlapLateral", 16f))
                        {
                            var ce = new List<Entity>(); var cn = new List<Entity>();
                            if (TrimChain(best, y.Track, ep, dir, F(a, "beyond", 8f), ce, cn, out Target beyondT, out string why))
                            {
                                li["trim"] = new Dictionary<string, object> { { "edges", ce.Count }, { "deadEnd", V(best.Pos) }, { "to", V(beyondT.Pos) }, { "overlap", -along } };
                                trimEdges.AddRange(ce); trimNodes.AddRange(cn); trimTo.Add(beyondT.Node);
                                usedTargets.Add(beyondT.Node);
                                best = beyondT; bd = math.distance(ep.xz, beyondT.Pos.xz);
                            }
                            else li["overlap"] = new Dictionary<string, object> { { "deadEnd", V(best.Pos) }, { "behind", -along }, { "notTrimmed", why } };
                        }
                        if (Crosses(ep, best.Pos, en, best.Node, out string what)) li["crossing"] = what;
                        // always the player's way: a Permanent course that starts on a building-owned node (the
                        // station's platform end) is silently not built (QA 5c); Temp + apply attaches properly.
                        // Built after the trims (below), so the target node has its final edges.
                        deferred.Add(new KeyValuePair<Dictionary<string, object>, KeyValuePair<Entity, Target>>(li, new KeyValuePair<Entity, Target>(en, best)));
                        li["distance"] = bd;
                        links.Add(li);
                    }
                    sitem["links"] = links;
                }
            // bridges: CS1 station platforms that no CS2 station reproduces (e.g. the metro platforms of a CS1 train
            // station hub) leave two imported dead ends facing each other across the gap. Join them straight, so the
            // lines pass through (they lose that stop) (DECISIONS-transit #13; bridge:false skips it)
            var bridgeResults = new List<object>();
            if (!(a != null && a.TryGetValue("bridge", out var br) && br is bool brb && !brb))
                foreach (var tt in new[] { TrackTypes.Subway, TrackTypes.Train })
                    foreach (var bl in Bridges(tt, F(a, "bridgeReach", 300f), F(a, "bridgeAngle", 30f), usedTargets))
                    {
                        var li = new Dictionary<string, object> { { "track", tt.ToString() }, { "distance", bl.Distance } };
                        usedTargets.Add(bl.A); usedTargets.Add(bl.B.Node);
                        deferred.Add(new KeyValuePair<Dictionary<string, object>, KeyValuePair<Entity, Target>>(li, new KeyValuePair<Entity, Target>(bl.A, bl.B)));
                        bridgeResults.Add(li);
                    }
            if (!dry && trimEdges.Count > 0)
            {
                // the importer's delete pattern: Deleted on the edges, one frame, then the orphaned nodes; Updated on
                // the kept end nodes and their remaining edges
                foreach (var e in trimEdges) DeleteNet(e);
                yield return null;
                foreach (var n in trimNodes) if (IsReal(n) && ConnectedEdges(n).Count == 0) DeleteNet(n);
                foreach (var n in trimTo) if (IsReal(n)) { Em.AddComponent<Updated>(n); foreach (var e in ConnectedEdges(n)) Em.AddComponent<Updated>(e); }
                for (int i = 0; i < 5; i++) yield return null;
                nets.Clear();
            }
            foreach (var kv in deferred)
            {
                var li = kv.Key; var en = kv.Value.Key; var t = kv.Value.Value;
                if (!IsReal(en) || !IsReal(t.Node)) { li["status"] = "node-gone"; continue; }
                Make(li, en, t, math.distance(Em.GetComponentData<Node>(en).m_Position.xz, t.Pos.xz), ref data, dry, false);
                if (!dry) pendingTemp.Add(li);
            }
            foreach (var y in Yards())
            {
                var item = new Dictionary<string, object> { { "building", Id(y.Building) }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(y.Building).m_Prefab) }, { "type", y.Type.ToString() } };
                results.Add(item);
                var ends = YardEnds(y, out bool connected, out int subEdges);
                item["openEnds"] = ends.Count; item["subTrackEdges"] = subEdges;
                if (connected) { item["status"] = "already-connected"; continue; }
                if (ends.Count == 0) { item["status"] = "no-open-end"; continue; }
                // the nodes the lines of this type run over; any network track node only when no line path is known
                var targets = LineNodes(y.Type, y.Track, out int lineRoutes, out int pathElements);
                item["lineRoutes"] = lineRoutes; item["linePathElements"] = pathElements; item["lineNodes"] = targets.Count;
                if (targets.Count == 0) { if (!nets.TryGetValue(y.Track, out targets)) nets[y.Track] = targets = NetworkNodes(y.Track); item["targets"] = "network"; }
                else item["targets"] = "line";
                if (targets.Count == 0) { item["status"] = "no-network"; item["reason"] = "no " + y.Track + " track outside buildings"; continue; }
                // candidate pairs by distance
                var pairs = new List<KeyValuePair<float, KeyValuePair<Entity, Target>>>();
                foreach (var en in ends)
                {
                    float3 ep = Em.GetComponentData<Node>(en).m_Position;
                    foreach (var t in targets)
                    {
                        float dd = math.distance(ep.xz, t.Pos.xz);
                        // CS2 drops a track course steeper than it can build without a word (run 12: an 8 m link
                        // climbing 1.8 m was never built): only candidates within maxGrade (6 %), and at least 20 m
                        if (dd <= maxDist && dd >= 20f && math.abs(t.Pos.y - ep.y) <= F(a, "maxGrade", 0.06f) * dd) pairs.Add(new KeyValuePair<float, KeyValuePair<Entity, Target>>(dd, new KeyValuePair<Entity, Target>(en, t)));
                    }
                }
                pairs.Sort((p, q) => p.Key.CompareTo(q.Key));
                if (pairs.Count == 0) { item["status"] = "too-far"; item["reason"] = "nearest network track is more than " + maxDist + " m away"; continue; }
                var tried = new List<object>();
                bool done = false;
                // prefer a course that crosses nothing (a Permanent definition, built directly)
                for (int i = 0; i < pairs.Count && i < candidates && !done; i++)
                {
                    var en = pairs[i].Value.Key; var t = pairs[i].Value.Value;
                    float3 ep = Em.GetComponentData<Node>(en).m_Position;
                    if (Crosses(ep, t.Pos, en, t.Node, out string what)) { if (tried.Count < 5) tried.Add(new Dictionary<string, object> { { "to", Id(t.Node) }, { "distance", pairs[i].Key }, { "crosses", what } }); continue; }
                    Make(item, en, t, pairs[i].Key, ref data, dry, false); // the yard end is building-owned too
                    if (!dry) pendingTemp.Add(item);
                    done = true;
                }
                // otherwise the shortest course, built like the player's track tool: a Temp definition and one apply
                // frame, so ApplyNetSystem splits the crossed roads/tracks into level crossings / junctions
                // (a Permanent course across an existing edge leaves Temp ghosts, Spike 1)
                if (!done && allowCrossings && pairs.Count > 0)
                {
                    Make(item, pairs[0].Value.Key, pairs[0].Value.Value, pairs[0].Key, ref data, dry, false);
                    item["crossing"] = true;
                    if (!dry) pendingTemp.Add(item);
                    done = true;
                }
                if (!done) { item["status"] = "blocked"; item["reason"] = "every candidate course crosses another edge"; }
                if (tried.Count > 0) item["rejected"] = tried;
            }
            bool applied = false;
            if (pendingTemp.Count > 0)
            {
                yield return null;                // generation makes the Temp edges
                applied = Ops.SetApply();         // this frame's ToolOutputSystem runs the vanilla ApplyTool phase
                for (int i = 0; i < 30; i++) yield return null;
                foreach (var it in pendingTemp) it["status"] = applied ? "linked" : "not-applied";
            }
            yield return new Dictionary<string, object> { { "op", "tracklink" }, { "dryRun", dry }, { "applied", applied }, { "stations", stationResults }, { "bridges", bridgeResults }, { "yards", results } };
        }

        /// <summary>From an imported dead end lying under a station platform, the chain of imported track edges up to
        /// the first node more than `beyond` metres past the platform end (along dir). Stops at a junction (never
        /// deleted) or after 8 edges. nodes = the dead end and the chain's inner nodes (deleted once orphaned).</summary>
        static bool TrimChain(Target d, TrackTypes track, float3 ep, float2 dir, float beyond, List<Entity> edges, List<Entity> nodes, out Target to, out string why)
        {
            to = default; why = null;
            Entity node = d.Node, prev = Entity.Null;
            for (int i = 0; i < 8; i++)
            {
                Entity next = Entity.Null; int k = 0;
                foreach (var e in ConnectedEdges(node))
                    if ((EdgeTrack(e) & track) != 0 && !Em.HasComponent<Owner>(e) && e != prev) { next = e; k++; }
                if (next == Entity.Null) { why = "the imported track ends under the platform"; return false; }
                if (k > 1) { why = "a junction lies under the platform"; return false; }
                var ed = Em.GetComponentData<Edge>(next);
                Entity other = ed.m_Start == node ? ed.m_End : ed.m_Start;
                edges.Add(next); nodes.Add(node);
                float3 op = Em.GetComponentData<Node>(other).m_Position;
                if (math.dot((op - ep).xz, dir) > beyond)
                {
                    to = new Target { Node = other, Pos = op, Edge = next, Prefab = Em.GetComponentData<PrefabRef>(next).m_Prefab };
                    return true;
                }
                node = other; prev = next;
            }
            why = "more than 8 imported edges under the platform";
            return false;
        }

        /// <summary>Unowned dead ends of one track type, with the direction their own track points out of them.</summary>
        static List<KeyValuePair<Entity, float2>> DeadEnds(TrackTypes track)
        {
            var r = new List<KeyValuePair<Entity, float2>>();
            foreach (var t in NetworkNodes(track))
            {
                Entity only = Entity.Null; int k = 0;
                foreach (var e in ConnectedEdges(t.Node)) if ((EdgeTrack(e) & track) != 0) { only = e; k++; }
                if (k != 1 || Em.HasComponent<Owner>(only) || !Em.HasComponent<Curve>(only)) continue;
                var bz = Em.GetComponentData<Curve>(only).m_Bezier;
                var ed = Em.GetComponentData<Edge>(only);
                float2 dir = ed.m_Start == t.Node ? (bz.a.xz - bz.b.xz) : (bz.d.xz - bz.c.xz);
                if (math.lengthsq(dir) < 1e-6f) dir = ed.m_Start == t.Node ? (bz.a.xz - bz.d.xz) : (bz.d.xz - bz.a.xz);
                r.Add(new KeyValuePair<Entity, float2>(t.Node, math.normalizesafe(dir)));
            }
            return r;
        }

        struct BridgeLink { public Entity A; public Target B; public float Distance; }

        /// <summary>Imported (unowned) dead ends of one track type that face each other: within `reach` metres, and
        /// the straight course leaves each end within `angle` degrees of the direction its own track points out of
        /// it. Mutual nearest pairs only; ends in `used` (already joined to a station or yard) are left alone.</summary>
        static List<BridgeLink> Bridges(TrackTypes track, float reach, float angle, HashSet<Entity> used)
        {
            var ends = new List<KeyValuePair<Entity, KeyValuePair<Entity, float2>>>(); // node -> (edge, outward dir)
            foreach (var t in NetworkNodes(track))
            {
                if (used.Contains(t.Node)) continue;
                Entity only = Entity.Null; int k = 0;
                foreach (var e in ConnectedEdges(t.Node)) if ((EdgeTrack(e) & track) != 0) { only = e; k++; }
                if (k != 1 || Em.HasComponent<Owner>(only) || !Em.HasComponent<Curve>(only)) continue;
                var bz = Em.GetComponentData<Curve>(only).m_Bezier;
                var ed = Em.GetComponentData<Edge>(only);
                float2 dir = ed.m_Start == t.Node ? (bz.a.xz - bz.b.xz) : (bz.d.xz - bz.c.xz);
                if (math.lengthsq(dir) < 1e-6f) dir = ed.m_Start == t.Node ? (bz.a.xz - bz.d.xz) : (bz.d.xz - bz.a.xz);
                ends.Add(new KeyValuePair<Entity, KeyValuePair<Entity, float2>>(t.Node, new KeyValuePair<Entity, float2>(only, math.normalizesafe(dir))));
            }
            float cosMax = math.cos(math.radians(angle));
            int n = ends.Count;
            var best = new int[n]; var bestD = new float[n];
            for (int i = 0; i < n; i++)
            {
                best[i] = -1; bestD[i] = reach;
                float3 pi = Em.GetComponentData<Node>(ends[i].Key).m_Position;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    float3 pj = Em.GetComponentData<Node>(ends[j].Key).m_Position;
                    float2 ab = pj.xz - pi.xz; float d = math.length(ab);
                    if (d < 1f || d >= bestD[i]) continue;
                    float2 u = ab / d;
                    if (math.dot(u, ends[i].Value.Value) < cosMax || math.dot(-u, ends[j].Value.Value) < cosMax) continue;
                    best[i] = j; bestD[i] = d;
                }
            }
            var r = new List<BridgeLink>();
            for (int i = 0; i < n; i++)
            {
                int j = best[i];
                if (j <= i || best[j] != i) continue; // mutual, each pair once
                var nb = ends[j].Key;
                r.Add(new BridgeLink { A = ends[i].Key, Distance = bestD[i], B = new Target { Node = nb, Pos = Em.GetComponentData<Node>(nb).m_Position, Edge = ends[j].Value.Key } });
            }
            return r;
        }

        /// <summary>Deleted on a net entity and its sub-objects (the importer's / MapPrep's pattern).</summary>
        static void DeleteNet(Entity e)
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

        static void Make(Dictionary<string, object> item, Entity en, Target t, float distance, ref TerrainHeightData data, bool dry, bool permanent)
        {
            float3 ep = Em.GetComponentData<Node>(en).m_Position;
            var prefab = t.Prefab != Entity.Null ? t.Prefab : Em.GetComponentData<PrefabRef>(t.Edge).m_Prefab;
            float e0 = ep.y - TerrainUtils.SampleHeight(ref data, ep), e1 = t.Pos.y - TerrainUtils.SampleHeight(ref data, t.Pos);
            item["link"] = new Dictionary<string, object>
            {
                { "from", Id(en) }, { "fromPos", V(ep) }, { "to", Id(t.Node) }, { "toPos", V(t.Pos) }, { "distance", distance },
                { "prefab", PrefabName(prefab) }, { "elevation", new List<object> { e0, e1 } }, { "permanent", permanent },
            };
            if (!dry) CreateCourse(prefab, ep, t.Pos, en, t.Node, e0, e1, permanent);
            item["status"] = dry ? "planned" : "linked";
        }

        /// <summary>One permanent course between two existing nodes (the importer's / Spike 1b recipe), with end elevations
        /// above the terrain so an elevated network node gets a ramp or bridge.</summary>
        static void CreateCourse(Entity prefab, float3 a, float3 b, Entity na, Entity nb, float e0, float e1, bool permanent)
        {
            Bezier4x3 curve = NetUtils.StraightCurve(a, b);
            var def = new CreationDefinition { m_Prefab = prefab, m_RandomSeed = UnityEngine.Random.Range(0, int.MaxValue), m_Flags = (permanent ? CreationFlags.Permanent : 0) | CreationFlags.SubElevation };
            var course = new NetCourse
            {
                m_Curve = curve,
                m_StartPosition = Pos(curve, a, na, 0f, CoursePosFlags.IsFirst | CoursePosFlags.IsLeft | CoursePosFlags.IsRight, e0),
                m_EndPosition = Pos(curve, b, nb, 1f, CoursePosFlags.IsLast | CoursePosFlags.IsLeft | CoursePosFlags.IsRight, e1),
                m_Elevation = new float2(math.max(0f, e0), math.max(0f, e1)),
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = -1,
            };
            var e = Em.CreateEntity();
            Em.AddComponentData(e, def);
            Em.AddComponentData(e, course);
            Em.AddComponent<Updated>(e);
            Em.AddComponent<Deleted>(e);
        }

        static CoursePos Pos(Bezier4x3 curve, float3 pos, Entity node, float t, CoursePosFlags flags, float elevation)
        {
            return new CoursePos
            {
                m_Entity = node, m_Position = pos, m_Rotation = NetUtils.GetNodeRotation(MathUtils.Tangent(curve, t)),
                m_CourseDelta = t, m_ParentMesh = -1, m_Flags = flags, m_Elevation = new float2(math.max(0f, elevation)),
            };
        }

        static float F(Dictionary<string, object> a, string k, float d) => a != null && a.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v) : d;
    }
}
