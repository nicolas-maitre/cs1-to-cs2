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

        struct Target { public Entity Node; public float3 Pos; public Entity Edge; }

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
                float2 at = best.Key.xz + best.Value * 120f; // inside the crop, beside the track end
                var prefabs = YardPrefabs[type];
                items.Add(new Dictionary<string, object>
                {
                    { "id", nextId-- }, { "cs1Info", null }, { "type", "BuildingPrefab" }, { "prefab", prefabs[0] }, { "family", "depot" },
                    { "x", at.x }, { "z", at.y }, { "accessSegment", 0 }, { "front", null }, { "facing", new List<object> { best.Value.x, best.Value.y } },
                    // the smaller prefab only; a lot facing a road a building can use (not a highway), within the link's reach
                    { "alternatives", new List<object>() }, { "fallback", "anywhere" }, { "requireRoad", true }, { "maxDistance", F(a, "maxPlaceDistance", 2500f) },
                    { "default", true }, { "depot", "edge" },
                    { "trackEnd", new List<object> { best.Key.x, best.Key.z } }, { "lines", names },
                    { "reason", "no CS1 " + type.ToLowerInvariant() + " depot; " + kind + " at the crop edge where the track leaves, linked to it" },
                });
                info["status"] = "yard"; info["trackEnd"] = new List<object> { best.Key.x, best.Key.z };
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
            if (!(a != null && a.TryGetValue("stations", out var st) && st is bool stb && !stb))
                foreach (var y in Stations())
                {
                    var ends = YardEnds(y, out bool connected0, out int subEdges0);
                    var sitem = new Dictionary<string, object> { { "building", Id(y.Building) }, { "prefab", PrefabName(Em.GetComponentData<PrefabRef>(y.Building).m_Prefab) }, { "track", y.Track.ToString() }, { "openEnds", ends.Count }, { "subTrackEdges", subEdges0 } };
                    stationResults.Add(sitem);
                    var links = new List<object>();
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
                        bool crosses = Crosses(ep, best.Pos, en, best.Node, out string what);
                        Make(li, en, best, bd, ref data, dry, !crosses);
                        if (crosses) { li["crossing"] = what; if (!dry) pendingTemp.Add(li); }
                        links.Add(li);
                    }
                    sitem["links"] = links;
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
                        if (dd <= maxDist && dd > 1f) pairs.Add(new KeyValuePair<float, KeyValuePair<Entity, Target>>(dd, new KeyValuePair<Entity, Target>(en, t)));
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
                    Make(item, en, t, pairs[i].Key, ref data, dry, true);
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
            yield return new Dictionary<string, object> { { "op", "tracklink" }, { "dryRun", dry }, { "applied", applied }, { "stations", stationResults }, { "yards", results } };
        }

        static void Make(Dictionary<string, object> item, Entity en, Target t, float distance, ref TerrainHeightData data, bool dry, bool permanent)
        {
            float3 ep = Em.GetComponentData<Node>(en).m_Position;
            var prefab = Em.GetComponentData<PrefabRef>(t.Edge).m_Prefab;
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
