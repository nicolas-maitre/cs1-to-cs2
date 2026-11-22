// Places the plan's buildings in a running CS2 city (exec op "place", phase tool + pump tool).
//
// Recipe (the game's own, from ZoneSpawnSystem.SpawnBuildingJob by IL inspection): one definition entity per building
// with CreationDefinition (prefab, random seed, Permanent) + ObjectDefinition (position, rotation, ParentMesh −1) +
// Updated + Deleted, created during ToolUpdate; GenerateObjectsSystem turns it into the real building in that frame.
// A CS2 lot's front (road side) is local +Z at depth × 4 m (BuildingUtils.CalculateFrontPosition).
//
// Snapping: an item with a "front" point (the CS1 building's road-side point, already in plan coordinates) is snapped
// to the nearest real CS2 road edge within `snap` metres: the lot's front edge is put on the road side, the lot faces
// the road, and the lot stays on the side of the road where the CS1 building was. Before placing, every lot is checked
// against roads, existing buildings and the lots placed earlier in this run; a collision is reported, not placed.
// After the batches the city is searched for each building; `connected` means CS2 linked it to a road edge.
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
using Game.Objects;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using GameBridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Buildings
{
    internal static class Placement
    {
        sealed class Item
        {
            public int Id;
            public string Prefab, Cs1Info, Type = "BuildingPrefab"; // Type: the plan item's prefab type (web plans set "BuildingPrefab")
            public double X, Z, Score;
            public double[] Front, Facing;
            public List<string> Alternatives;
            public int Alternative;
            public float Slide;
            public bool NearJunction;
            public string RoadSide;
            public string Fallback;          // "nearby" | "anywhere" | "skip" | null (run option)
            public float MaxDistance;        // > 0: no fallback spot farther than this from the CS1 spot
            public bool RequireRoad;         // only spots whose lot faces a road a building can connect to (no highway)
            public bool Station;             // plan family "station": stays at its CS1 spot (no slide, no fallback move)
            public string Family;            // plan family (buildings/match), e.g. "power", "waterPump"
            public double Dx, Dz, Ox, Oz;    // plan transform (CS1 → plan) and import offset (plan → CS2), for "place it yourself"
            public float Distance = -1;     // metres between the placed lot centre and the CS1 lot centre (every placed item)
            public string FallbackPass;      // "nearby" | "wide" | "anywhere" when a fallback pass placed it
            public float Moved = -1;         // metres from the CS1 position when the nearby fallback placed it
            public bool MovedToOtherRoad; // flags of the road side the lot faces, e.g. "Sidewalk" or "Raised"
            public int AccessSegment;
            // resolved
            public Entity PrefabEntity;
            public int2 Lot;
            public float3 Position;
            public quaternion Rotation;
            public float2 Forward;
            public Entity RoadEdge;
            public string Status = "pending", Reason;
            public float SnapDistance = -1;
            public Entity Placed;
            public int SubNetsExpected = -1, SubNetsBuilt = -1; // course definitions written / edges owned by the building
            public bool DepotRetry;
            public string AlignNote;         // decision #48: station anchored on the CS1 track ("shifted 21 m across, 3 m along, turned 1 deg")
            public float AlignShift = -1;
            public double[] Cs1Lot;
            public string PlatformCheck;     // decision #48: placed platform tracks against the nearest CS1 track ("2 tracks: 0.4 / 1.1 m")          // the CS1 lot in cells (width along the road, depth), for the station anchor    // metres between the aligned and the CS1 lot centre
            public bool ToolApplied;         // decision #47: built through the tool's Temp + Apply path          // decision #152: a bus / tram CS1 depot retried with "nearby" (none of its kind placed)
            public string PumpWater;         // surface-water pump: "shore N m away" (slid toward the water) or "groundwater pump" (swapped)
        }

        internal sealed class RoadEdge
        {
            public Entity Entity;
            public Bezier4x3 Curve;
            public float HalfWidth;
            public CompositionFlags Flags;
            public bool Highway;  // highway rules (RoadData.UseHighwayRules): buildings can't connect to it
            public float4 Bounds; // minX, minZ, maxX, maxZ (expanded by the half width)
        }

        // ---------------------------------------------------------------- op: place

        public static object Place(Dictionary<string, object> a) { return PlaceRoutine(a); }

        static IEnumerator PlaceRoutine(Dictionary<string, object> a)
        {
            var sw = Stopwatch.StartNew();
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var terrain = world.GetOrCreateSystemManaged<TerrainSystem>();
            double ox = P.Num(a, "offsetX", 0), oz = P.Num(a, "offsetZ", 0);
            float snap = (float)P.Num(a, "snap", 16);
            int batchSize = (int)P.Num(a, "batchSize", 50);
            int settle = (int)P.Num(a, "settleFrames", 3);
            bool checkCollisions = P.Bool(a, "collisions", true);
            int limit = (int)P.Num(a, "limit", int.MaxValue);
            float maxSlide = (float)P.Num(a, "maxSlide", 24);
            float endMargin = (float)P.Num(a, "endMargin", 12);
            bool useAlternatives = P.Bool(a, "alternatives", true);
            // Decision 106 (user): never moved to free land by default. Exact spot, a short slide, a smaller alternative;
            // "nearby" / "anywhere" only when the plan item (or the run) asks for it.
            string defaultFallback = P.Str(a, "fallback", PlacementRules.DefaultFallback);
            float nearbyRadius = (float)P.Num(a, "nearbyRadius", 150);
            int maxNearbyTries = (int)P.Num(a, "maxNearbyTries", 4000);
            double frameBudgetMs = P.Num(a, "frameBudgetMs", 20);
            // Second pass of the nearby search with a larger radius (0 = none), and the "anywhere" fallback: every imported
            // road in the crop, closest first to the CS1 spot.
            float wideRadius = (float)P.Num(a, "wideRadius", 400);
            int maxAnywhereTries = (int)P.Num(a, "maxAnywhereTries", 20000);
            if (defaultFallback != "nearby" && defaultFallback != "anywhere" && defaultFallback != "skip") throw new ArgumentException("fallback must be \"nearby\", \"anywhere\" or \"skip\"");
            var movedByPass = new Dictionary<string, int>();
            int moved = 0;
            // DECISIONS-buildings #42: surface-water pumps need water at their lot (CS1 and CS2 shorelines differ).
            float pumpShoreReach = (float)P.Num(a, "pumpShoreReach", PlacementRules.DefaultPumpShoreReach);
            string groundwaterPump = P.Str(a, "groundwaterPump", "GroundwaterPumpingStation01");
            var waterSystem = world.GetOrCreateSystemManaged<WaterSystem>();
            var waterDeps = default(Unity.Jobs.JobHandle);
            var surfaceData = waterSystem.GetSurfaceData(out waterDeps);
            waterDeps.Complete();
            Func<float2, float> waterDepth = q => WaterUtils.SampleDepth(ref surfaceData, new float3(q.x, 0f, q.y));
            var groundDeps = default(Unity.Jobs.JobHandle);
            var groundData = world.GetOrCreateSystemManaged<GroundWaterSystem>().GetData(true, out groundDeps);
            groundDeps.Complete();
            Func<float2, GroundWater> groundWater = q => GroundWaterSystem.GetGroundWater(new float3(q.x, 0f, q.y), groundData.m_Buffer);
            int pumpsSlidToWater = 0, pumpsSwapped = 0, pumpsDry = 0, pumpsUnknown = 0;
            var pumpSamples = new List<object>();

            var items = LoadItems(P.Required(a, "plan"), limit);
            // Decision #48 (opt-in, the mod's rail step turns it on): stations with platform tracks are placed so their
            // platform track lies on the CS1 track they served, not at the lot centre (Skibidi's TrainStation03 sat 14-30 m
            // off CS1's tracks, and Train Line 1's path failed on the S between them).
            bool alignStations = P.Bool(a, "alignStations", false);
            var cs1Tracks = alignStations ? LoadTracks(P.Required(a, "plan")) : new List<Cs1Track>();
            int stationsAligned = 0, stationsNoTrack = 0, stationsAnchorBlocked = 0;
            foreach (var x in items) { x.Ox = ox; x.Oz = oz; }
            // Stations first: they must take their CS1 spot (their tracks continue the imported track), while other
            // buildings can slide or move. A wide CS2 police station had taken the Large Trainstation's spot.
            float stationMaxSlide = (float)P.Num(a, "stationMaxSlide", PlacementRules.DefaultStationMaxSlide);
            {
                var ordered = new List<Item>(items.Count);
                foreach (var x in items) if (x.Station) ordered.Add(x);
                foreach (var x in items) if (!x.Station) ordered.Add(x);
                items = ordered;
            }
            int skippedAttached = 0;
            var roads = RoadEdges(em);
            var existing = ExistingLots(em);
            // for idempotency: an item already built near its spot is not placed again (run option idempotent=false turns it off, for stress tests)
            var already = P.Bool(a, "idempotent", true) ? FindBuildings(em) : new List<Found>();
            int alreadyPlaced = 0;
            var placedLots = new List<float2[]>();
            var heights = terrain.GetHeightData(true);
            var failures = new Dictionary<string, int>();

            // Resolve prefab, position and rotation; check collisions. On a collision the lot first slides along its road
            // (±4 … ±maxSlide m, staying on the same side), then the next alternative prefab is tried the same way, and
            // finally (fallback "nearby", the default) the closest free, connectable spot along any imported road within
            // nearbyRadius m of the CS1 position is searched. Fallback "skip" (per item or run option) stops after the slides.
            var slides = new List<float> { 0f };
            for (float s = 4f; s <= maxSlide + 0.01f; s += 4f) { slides.Add(s); slides.Add(-s); }
            var grid = new RoadGrid(roads);
            // Frame budget: the fit checks (slides, alternatives, fallback passes over thousands of road edges) yield a
            // frame whenever frameBudgetMs is used up, so the game keeps rendering (a 14 km city stalled CS2 for 27 s).
            double frameStart = sw.Elapsed.TotalMilliseconds;
            int resolveFrames = 0;
            // Decision #152: after the normal pass, a bus or tram depot kind with none placed retries its CS1 depot (one per
            // kind) with the default depots' "nearby" fallback: its lines get no vehicles otherwise. Run option
            // depotFallback=false opts out (the mod's buildings.depotFallback).
            bool depotFallback = P.Bool(a, "depotFallback", true);
            var queue = new List<Item>(items);
            bool depotRetried = false;
            int depotRetries = 0;
            for (int qi = 0; ; qi++)
            {
                if (qi == queue.Count)
                {
                    if (depotRetried || !depotFallback) break;
                    depotRetried = true;
                    var kinds = new List<string>();
                    var ok = new List<bool>();
                    var strict = new List<bool>();
                    foreach (var x in items)
                    {
                        kinds.Add(PlacementRules.DepotKind(x.Prefab));
                        ok.Add(x.Status == "ready" || x.Status == "alreadyPlaced");
                        strict.Add(x.Status != "prefabMissing" && PlacementRules.Fallback(x.Station, x.Fallback, defaultFallback) == "skip");
                    }
                    foreach (int ri in PlacementRules.DepotRetries(kinds, ok, strict))
                    {
                        var x = items[ri];
                        int n;
                        if (failures.TryGetValue(x.Status, out n)) failures[x.Status] = n - 1;
                        x.Status = "pending"; x.Reason = null; x.Fallback = "nearby"; x.DepotRetry = true;
                        queue.Add(x);
                        depotRetries++;
                    }
                    if (qi == queue.Count) break;
                }
                var it = queue[qi];
                if (frameBudgetMs > 0 && sw.Elapsed.TotalMilliseconds - frameStart > frameBudgetMs) { yield return null; resolveFrames++; frameStart = sw.Elapsed.TotalMilliseconds; }
                // Snap the front point to the nearest real road edge once; every attempt reuses it.
                RoadEdge road = null;
                float roadT = 0, roadLen = 0;
                var centre = new float2((float)(it.X + ox), (float)(it.Z + oz));
                if (it.Front != null)
                {
                    var fp = new float2((float)(it.Front[0] + ox), (float)(it.Front[1] + oz));
                    float bestD = float.MaxValue;
                    foreach (var r in grid.Query(fp - snap - 32f, fp + snap + 32f))
                    {
                        if (it.RequireRoad && r.Highway) continue;
                        float t;
                        float d = MathUtils.Distance(r.Curve.xz, fp, out t) - r.HalfWidth;
                        if (d < bestD) { bestD = d; road = r; roadT = t; }
                    }
                    if (road == null || bestD > snap)
                    {
                        Fail(it, failures, "noRoad", road == null ? "no CS2 road within " + snap + " m of the front point" : "nearest CS2 road side is " + bestD.ToString("F1") + " m away");
                        continue;
                    }
                    it.RoadEdge = road.Entity;
                    it.SnapDistance = bestD;
                    roadLen = math.max(1f, MathUtils.Length(road.Curve.xz));
                }
                // Which side of the road the CS1 building was on.
                float side = 1f;
                if (road != null)
                {
                    var c0 = MathUtils.Position(road.Curve, roadT).xz;
                    var t0 = math.normalizesafe(MathUtils.Tangent(road.Curve, roadT).xz, new float2(1, 0));
                    side = math.dot(centre - c0, new float2(-t0.y, t0.x)) >= 0 ? 1f : -1f;
                    var sf = side > 0 ? road.Flags.m_Left : road.Flags.m_Right;
                    it.RoadSide = sf.ToString() + ((road.Flags.m_General & CompositionFlags.General.Elevated) != 0 ? ", Elevated" : "");
                }

                // Candidate prefabs: the match, then its alternatives.
                var names = new List<string> { it.Prefab };
                if (useAlternatives && it.Alternatives != null) names.AddRange(it.Alternatives);
                var prefabs = new List<KeyValuePair<string, Entity>>();
                foreach (var nm in names)
                {
                    PrefabBase prefab;
                    Entity pe;
                    if (!string.IsNullOrEmpty(nm) && ps.TryGetPrefab(new PrefabID(it.Type, nm), out prefab) && ps.TryGetEntity(prefab, out pe) && em.HasComponent<BuildingData>(pe))
                    {
                        // A net-attached building (SubwayStationElevated01: NetObject.m_Attached) is built onto an existing
                        // track and has no tracks of its own; placed free-standing, its stops attach to nothing. Skipped
                        // here, so the next alternative (SubwayStationElevated02, with its own elevated tracks) is used.
                        if (em.HasComponent<PlaceableObjectData>(pe) && (em.GetComponentData<PlaceableObjectData>(pe).m_Flags & Game.Objects.PlacementFlags.Attached) != 0) { skippedAttached++; continue; }
                        prefabs.Add(new KeyValuePair<string, Entity>(nm, pe));
                    }
                }
                if (prefabs.Count == 0) { Fail(it, failures, "prefabMissing", "CS2 prefab '" + it.Prefab + "' (and its alternatives) not found"); continue; }
                // Stations never move to free land: a moved station would not continue the CS1 track (DECISIONS-buildings #36).
                string fallback = PlacementRules.Fallback(it.Station, it.Fallback, defaultFallback);
                // Without a free-land fallback only a smaller alternative (lot area, of the first usable prefab) is tried:
                // one that fits where the match didn't (decision 106). A larger one would only fit by covering more.
                if (prefabs.Count > 1)
                {
                    var l0 = em.GetComponentData<BuildingData>(prefabs[0].Value).m_LotSize;
                    var kept = new List<KeyValuePair<string, Entity>> { prefabs[0] };
                    for (int pi = 1; pi < prefabs.Count; pi++)
                    {
                        var l = em.GetComponentData<BuildingData>(prefabs[pi].Value).m_LotSize;
                        if (PlacementRules.TryAlternative(fallback, l0.x * l0.y, l.x * l.y)) kept.Add(prefabs[pi]);
                    }
                    prefabs = kept;
                }
                // Surface-water pump (DECISIONS-buildings #42): water under its lot at the CS1 spot → as is; water within
                // pumpShoreReach m more → the slides toward the water first (up to that far along its road); none → the
                // groundwater pump where the ground has water; else not placed (place it yourself).
                var itemSlides = slides;
                float itemMaxSlide = maxSlide;
                if (IsSurfacePump(em, prefabs[0].Value))
                {
                    var lot0 = em.GetComponentData<BuildingData>(prefabs[0].Value).m_LotSize;
                    float reach = 4f * math.cmax(lot0);
                    float wet = NearestWater(waterDepth, centre, reach + pumpShoreReach);
                    var gw = groundWater(centre);
                    int ground = gw.m_Amount;
                    // Read from an exec op, the water and groundwater maps can come back empty (all 0, even where a pump
                    // works: probe on cs1conv-util-cacao2). No water and a 0 groundwater capacity = no data: as planned.
                    var action = float.IsInfinity(wet) && gw.m_Max == 0 ? "unknown" : PlacementRules.PumpWaterAction(wet, reach, pumpShoreReach, ground > 0);
                    if (action == "unknown") pumpsUnknown++;
                    if (action == "slide")
                    {
                        it.PumpWater = "shore " + (wet - reach).ToString("F0") + " m away";
                        if (road != null)
                        {
                            var tan = math.normalizesafe(MathUtils.Tangent(road.Curve, roadT).xz, new float2(1, 0));
                            itemMaxSlide = math.max(maxSlide, pumpShoreReach);
                            itemSlides = new List<float> { 0f };
                            for (float s = 4f; s <= itemMaxSlide + 0.01f; s += 4f) { itemSlides.Add(s); itemSlides.Add(-s); }
                            var key = new Dictionary<float, float>();
                            foreach (var s in itemSlides) key[s] = NearestWater(waterDepth, centre + tan * s, reach + pumpShoreReach);
                            itemSlides.Sort((p, q) => key[p] != key[q] ? key[p].CompareTo(key[q]) : math.abs(p).CompareTo(math.abs(q)));
                        }
                    }
                    else if (action == "groundwater")
                    {
                        PrefabBase gp;
                        Entity ge;
                        if (ps.TryGetPrefab(new PrefabID(it.Type, groundwaterPump), out gp) && ps.TryGetEntity(gp, out ge) && em.HasComponent<BuildingData>(ge))
                        {
                            prefabs = new List<KeyValuePair<string, Entity>> { new KeyValuePair<string, Entity>(groundwaterPump, ge) };
                            it.PumpWater = "groundwater pump (no surface water within " + (reach + pumpShoreReach).ToString("F0") + " m)";
                        }
                        else action = "dry";
                    }
                    if (action == "dry")
                    {
                        pumpsDry++;
                        it.PumpWater = "dry: no surface water within " + (reach + pumpShoreReach).ToString("F0") + " m and no groundwater found (placed as planned; check it pumps)";
                    }
                    if (pumpSamples.Count < 20)
                        pumpSamples.Add(PlacementRules.Position(centre.x, centre.y) + " depth " + waterDepth(centre).ToString("F2") + " m, nearest water " + (float.IsInfinity(wet) ? "none" : wet.ToString("F0") + " m") + ", groundwater " + ground + " → " + action);
                }
                // Already in the city (an earlier run, or a save that already has it): the same prefab (or an alternative)
                // within 30 m + the slide range of the CS1 spot. Keeps re-runs from stacking duplicates.
                Found dup = null;
                foreach (var b in already)
                {
                    bool same = false;
                    foreach (var pf in prefabs) if (pf.Value == b.Prefab) { same = true; break; }
                    if (same && math.distance(b.Position.xz, centre) < 30f + maxSlide) { dup = b; break; }
                }
                if (dup != null)
                {
                    it.Status = "alreadyPlaced";
                    it.Reason = "already in the city (entity " + dup.Entity.Index + ")";
                    it.Position = dup.Position;
                    alreadyPlaced++;
                    continue;
                }

                string lastHit = null;
                bool done = false;
                if (alignStations && it.Station)
                {
                    string why = null;
                    for (int ni = 0; ni < prefabs.Count && !done; ni++)
                    {
                        float2 apos, afwd;
                        string note;
                        if (!StationAnchor(em, prefabs[ni].Value, it, centre, cs1Tracks, out apos, out afwd, out note)) { why = note; continue; }
                        // the anchor keeps the platform on the track: only short slides along the track (stationMaxSlide)
                        var along = new float2(afwd.y, -afwd.x);
                        foreach (var sl in new[] { 0f, 4f, -4f, 8f, -8f })
                        {
                            if (math.abs(sl) > stationMaxSlide + 0.01f) continue;
                            if (TryAt(it, prefabs[ni], ni, apos + along * sl, afwd, float.NaN, grid, existing, placedLots, checkCollisions, ref heights, ref lastHit))
                            {
                                done = true;
                                it.Slide = sl;
                                it.AlignShift = math.distance(apos + along * sl, centre);
                                it.AlignNote = note + (sl != 0 ? ", slid " + sl + " m along the track" : "");
                                stationsAligned++;
                                break;
                            }
                        }
                    }
                    // no CS1 track to anchor on, or the anchored lot collides (Skibidi's elevated metro stations over a ground
                    // road): the station is placed as before (its CS1 spot, short slides along its road), never failed for it
                    if (!done && lastHit != null) { stationsAnchorBlocked++; it.AlignNote = "not aligned: the anchored lot overlaps " + Article(lastHit) + "; placed at its CS1 spot as before"; lastHit = null; }
                    else if (!done && why != null) { stationsNoTrack++; it.AlignNote = "not aligned: " + why; }
                }
                if (done) { }
                else if (road == null)
                {
                    // No road to snap to: the CS1 position and facing, as is.
                    var forward = it.Facing != null ? math.normalizesafe(new float2((float)it.Facing[0], (float)it.Facing[1]), new float2(0, 1)) : new float2(0, 1);
                    for (int ni = 0; ni < prefabs.Count && !done; ni++)
                        done = TryAt(it, prefabs[ni], ni, centre, forward, float.NaN, grid, existing, placedLots, checkCollisions, ref heights, ref lastHit);
                }
                else
                {
                    // Pass 0 keeps the lot's front centre at least endMargin m from the ends of its road edge: CS2 connects a
                    // building only along an edge, and a front inside a junction stays unconnected. Pass 1 drops that rule.
                    for (int pass = 0; pass < 2 && !done; pass++)
                        for (int ni = 0; ni < prefabs.Count && !done; ni++)
                            foreach (var slide in itemSlides)
                            {
                                // A station's platform tracks must continue the imported CS1 track: only a short slide along
                                // the road (a CS2 lot wider than CS1's next to a side road), which the track link absorbs.
                                if (!PlacementRules.SlideAllowed(it.Station, slide, itemMaxSlide, stationMaxSlide)) continue;
                                float t = roadT + slide / roadLen;
                                if (t < 0f || t > 1f) continue;
                                if (pass == 0 && (t * roadLen < endMargin || (1f - t) * roadLen < endMargin)) continue;
                                if (TryOnRoad(it, prefabs[ni], ni, road, t, side, grid, existing, placedLots, checkCollisions, ref heights, ref lastHit))
                                {
                                    it.Slide = slide;
                                    it.NearJunction = pass == 1;
                                    done = true;
                                    break;
                                }
                            }
                }

                // Nearby free land: every road edge within the radius, both sides, every 6 m away from the edge ends,
                // tried closest-first (distance between the would-be lot centre and the CS1 centre).
                var passes = new List<KeyValuePair<string, float>>();
                if (fallback == "nearby" || fallback == "anywhere")
                {
                    if (nearbyRadius > 0) passes.Add(new KeyValuePair<string, float>("nearby", nearbyRadius));
                    if (wideRadius > nearbyRadius) passes.Add(new KeyValuePair<string, float>("wide", wideRadius));
                }
                if (fallback == "anywhere") passes.Add(new KeyValuePair<string, float>("anywhere", float.PositiveInfinity));
                if (it.MaxDistance > 0)
                {
                    // Cap every pass at maxDistance; passes that would start beyond it are dropped.
                    var capped = new List<KeyValuePair<string, float>>();
                    float prev = 0;
                    foreach (var p in passes) { if (prev >= it.MaxDistance) break; capped.Add(new KeyValuePair<string, float>(p.Key, math.min(p.Value, it.MaxDistance))); prev = p.Value; }
                    passes = capped;
                }
                float prevRadius = 0;
                float depth0 = em.GetComponentData<BuildingData>(prefabs[0].Value).m_LotSize.y * 4f;
                foreach (var pass in passes)
                {
                    if (done) break;
                    float radius = pass.Value;
                    var cands = new List<KeyValuePair<float, float4>>(); // distance → (edge index, t, side, unused)
                    var near = float.IsInfinity(radius) ? roads : grid.Query(centre - radius - 64f, centre + radius + 64f);
                    for (int ri = 0; ri < near.Count; ri++)
                    {
                        var r = near[ri];
                        if (it.RequireRoad && r.Highway) continue;
                        float len = MathUtils.Length(r.Curve.xz);
                        for (float s = endMargin; s <= len - endMargin; s += 6f)
                        {
                            float t = s / len;
                            var c = MathUtils.Position(r.Curve, t).xz;
                            var tan = math.normalizesafe(MathUtils.Tangent(r.Curve, t).xz, new float2(1, 0));
                            for (int sd = -1; sd <= 1; sd += 2)
                            {
                                // Only sides a building can connect to: no embankment / trench wall. (Requiring the Side.Sidewalk
                                // flag as well left no candidates at all: the road compositions don't carry it.)
                                var sideFlags = sd > 0 ? r.Flags.m_Left : r.Flags.m_Right;
                                if ((sideFlags & (CompositionFlags.Side.Raised | CompositionFlags.Side.Lowered)) != 0) continue;
                                var n = new float2(-tan.y, tan.x) * sd;
                                float d = math.distance(c + n * (r.HalfWidth + depth0), centre);
                                // Later passes only try what the earlier ones didn't reach.
                                if (d <= radius && d > prevRadius) cands.Add(new KeyValuePair<float, float4>(d, new float4(ri, t, sd, 0)));
                            }
                        }
                    }
                    prevRadius = radius;
                    cands.Sort((x, y) => x.Key.CompareTo(y.Key));
                    int tries = 0, maxTries = pass.Key == "anywhere" ? maxAnywhereTries : maxNearbyTries;
                    foreach (var cand in cands)
                    {
                        if (done || ++tries > maxTries) break;
                        if (frameBudgetMs > 0 && sw.Elapsed.TotalMilliseconds - frameStart > frameBudgetMs) { yield return null; resolveFrames++; frameStart = sw.Elapsed.TotalMilliseconds; }
                        var r = near[(int)cand.Value.x];
                        for (int ni = 0; ni < prefabs.Count && !done; ni++)
                            if (TryOnRoad(it, prefabs[ni], ni, r, cand.Value.y, cand.Value.z, grid, existing, placedLots, checkCollisions, ref heights, ref lastHit))
                            {
                                done = true;
                                it.Moved = math.distance(it.Position.xz, centre);
                                it.FallbackPass = pass.Key;
                                it.RoadEdge = r.Entity;
                                it.MovedToOtherRoad = road == null || r.Entity != road.Entity;
                                var sf = cand.Value.z > 0 ? r.Flags.m_Left : r.Flags.m_Right;
                                it.RoadSide = sf.ToString();
                            }
                    }
                    if (done) { moved++; int c0; movedByPass.TryGetValue(pass.Key, out c0); movedByPass[pass.Key] = c0 + 1; }
                }
                if (done) it.Distance = math.distance(it.Position.xz, centre);
                if (!done && it.Station) Fail(it, failures, "stationCollision", "station kept at its CS1 spot, where the lot overlaps " + Article(lastHit) + " (tried " + prefabs.Count
                    + " prefab(s), slides up to ±" + stationMaxSlide + " m); not moved, because a moved station would not continue the imported track: place it yourself at " + Cs1Pos(it));
                else if (!done && passes.Count == 0) Fail(it, failures, "collision", "not placed: at its CS1 spot the lot overlaps " + Article(lastHit) + " (tried " + prefabs.Count
                    + " prefab(s), slides up to ±" + maxSlide + " m); not moved to free land: place it yourself at " + Cs1Pos(it));
                else if (!done) Fail(it, failures, "collision", "lot overlaps " + Article(lastHit) + " (tried " + prefabs.Count + " prefab(s), slides up to ±" + maxSlide + " m"
                    + ", free land " + (fallback == "anywhere" ? "anywhere in the crop" : "within " + passes[passes.Count - 1].Value + " m") + "): place it yourself at " + Cs1Pos(it));
            }

            // Create the definitions in batches, a few frames apart.
            var ready = items.FindAll(i => i.Status == "ready");
            int batches = 0, frames = 0, subNetCourses = 0;
            // A building definition alone gets no sub-nets (depot / station tracks, internal paths, parking roads): the
            // object tool, ZoneSpawnSystem and BuildingConstructionSystem write them as separate Permanent course
            // definitions owned by the building definition. Same here (run option subNets, default on).
            bool subNets = P.Bool(a, "subNets", true);
            var rnd = new Unity.Mathematics.Random(0x5eed1234u);
            // Buildings go the object tool's way (buildings decision #47, DECISIONS.md #166): Temp definitions, then the
            // tool's Apply, in batches. Built from Permanent definitions, a building's spawn locations connected to its own
            // invisible paths instead of the street's lanes: dispatch found no path, every bus stayed parked and every line
            // was NotEnoughVehicles (worker-4). On the apply path CS2 builds the sub-nets, sub-lanes and spawn locations
            // itself. Run option toolApply: "all" (default), "depots" (transport depots and yards only) or "none" (the old
            // Permanent path with our own sub-net courses).
            string toolApply = P.Str(a, "toolApply", "all");
            var applied = new List<Item>();
            if (toolApply == "all") applied = new List<Item>(ready);
            else if (toolApply == "depots") applied = ready.FindAll(i => em.HasComponent<TransportDepotData>(i.PrefabEntity));
            ready.RemoveAll(i => applied.Contains(i));
            var roadGuard = RoadGuard.Take(em);
            for (int start = 0; start < ready.Count; start += batchSize)
            {
                for (int k = start; k < Math.Min(ready.Count, start + batchSize); k++)
                {
                    var it = ready[k];
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new CreationDefinition { m_Prefab = it.PrefabEntity, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.Permanent });
                    em.AddComponentData(e, new ObjectDefinition
                    {
                        m_Position = it.Position, m_Rotation = it.Rotation,
                        m_LocalPosition = it.Position, m_LocalRotation = it.Rotation,
                        m_Scale = new float3(1f), m_Intensity = 1f, m_ParentMesh = -1, m_GroupIndex = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                    });
                    em.AddComponent<Updated>(e);
                    em.AddComponent<Deleted>(e);
                    if (subNets) subNetCourses += CreateSubNets(em, it.PrefabEntity, it.Position, it.Rotation, ref rnd);
                    it.Status = "created";
                }
                batches++;
                for (int f = 0; f < settle; f++) { yield return null; frames++; }
            }
            int applyBatches = 0, applyFailed = 0, nodesGuarded = 0;
            // The apply runs CS2's NodeReductionSystem, which merges a plain two-edge node's two imported roads into one
            // edge (worker-4, rail; here a park's paths 137 m from the node merged two Skibidi roads). Its query skips
            // Game.Net.Fixed: every imported top-level two-edge node gets a temporary Fixed for all the apply batches (as
            // TrackLink guardRoads), removed after the last one.
            var guarded = applied.Count > 0 ? GuardNodes(em, null, 0f) : new List<Entity>();
            nodesGuarded = guarded.Count;
            for (int start = 0; start < applied.Count; start += batchSize)
            {
                var batch = applied.GetRange(start, Math.Min(batchSize, applied.Count - start));
                foreach (var it in batch)
                {
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new CreationDefinition { m_Prefab = it.PrefabEntity, m_RandomSeed = rnd.NextInt() });
                    em.AddComponentData(e, new ObjectDefinition
                    {
                        m_Position = it.Position, m_Rotation = it.Rotation, m_LocalPosition = float3.zero, m_LocalRotation = quaternion.identity,
                        m_Scale = new float3(1f), m_Intensity = 1f, m_ParentMesh = -1, m_GroupIndex = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                    });
                    em.AddComponent<Updated>(e);
                    em.AddComponent<Deleted>(e);
                    if (subNets) subNetCourses += CreateSubNets(em, it.PrefabEntity, it.Position, it.Rotation, ref rnd, false, ps);
                    it.Status = "created";
                    it.ToolApplied = true;
                }
                yield return null; frames++;          // generation makes the Temp objects
                if (!SetApply(world)) applyFailed++;  // this frame's ToolOutputSystem runs the vanilla ApplyTool phase
                for (int f = 0; f < settle + 10; f++) { yield return null; frames++; }
                applyBatches++;
            }
            ready.AddRange(applied);
            for (int f = 0; f < (applied.Count > 0 ? 60 : 0); f++) { yield return null; frames++; }
            foreach (var n in guarded) if (em.Exists(n) && em.HasComponent<Game.Net.Fixed>(n)) em.RemoveComponent<Game.Net.Fixed>(n);
            if (guarded.Count > 0) for (int f = 0; f < 5; f++) { yield return null; frames++; }
            for (int f = 0; f < 30; f++) { yield return null; frames++; }

            int roadsChanged;
            var roadCheck = roadGuard.Check(em, out roadsChanged);
            // Verify: the real building with this prefab at this spot, and its road connection.
            var found = FindBuildings(em);
            int placed = 0, connected = 0, connectedToSnapEdge = 0, usedAlternative = 0, slid = 0, withSubNets = 0, subNetsMissing = 0;
            foreach (var it in ready)
            {
                Entity best = Entity.Null;
                float bestD = 3f;
                foreach (var b in found)
                {
                    if (b.Prefab != it.PrefabEntity) continue;
                    float d = math.distance(b.Position.xz, it.Position.xz);
                    if (d < bestD) { bestD = d; best = b.Entity; }
                }
                if (best == Entity.Null) { Fail(it, failures, "notCreated", "no building appeared at the spot"); continue; }
                it.Placed = best;
                if (alignStations && it.AlignShift >= 0) it.PlatformCheck = PlatformCheck(em, best, cs1Tracks, (float)ox, (float)oz);
                if (subNets)
                {
                    it.SubNetsExpected = ExpectedSubNets(em, it.PrefabEntity, it.ToolApplied ? ps : null);
                    it.SubNetsBuilt = em.HasBuffer<Game.Net.SubNet>(best) ? em.GetBuffer<Game.Net.SubNet>(best, true).Length : 0;
                    if (it.SubNetsExpected > 0) { withSubNets++; if (it.SubNetsBuilt == 0) subNetsMissing++; }
                }
                it.Status = "placed";
                placed++;
                if (it.Alternative > 0) usedAlternative++;
                if (it.Slide != 0) slid++;
                var road = em.GetComponentData<Building>(best).m_RoadEdge;
                if (road != Entity.Null) { connected++; if (road == it.RoadEdge) connectedToSnapEdge++; }
                it.Reason = (road != Entity.Null ? "connected" : "placed, not connected to a road" + (it.RoadSide != null ? " (road side: " + it.RoadSide + ")" : ""))
                    + (it.Alternative > 0 ? "; alternative #" + it.Alternative : "") + (it.Slide != 0 ? "; slid " + it.Slide + " m along the road" : "") + (it.NearJunction ? "; front near a junction" : "")
                    + (it.Moved >= 0 ? "; moved " + it.Moved.ToString("F0") + " m to free land (" + it.FallbackPass + " pass)" + (it.MovedToOtherRoad ? " on another road" : " on the same road") : "")
                    + (it.DepotRetry ? "; " + PlacementRules.DepotMovedNote : "")
                    + (it.AlignNote != null ? "; " + it.AlignNote : "");
                if (it.PumpWater != null)
                {
                    if (it.PumpWater.StartsWith("shore", StringComparison.Ordinal))
                    {
                        float reach = 4f * math.cmax(it.Lot);
                        bool wetNow = NearestWater(waterDepth, it.Position.xz, reach) <= reach;
                        if (wetNow) pumpsSlidToWater++;
                        it.Reason += "; water " + it.PumpWater + (wetNow ? ", reached" : ", still dry at the placed spot");
                    }
                    else if (it.PumpWater.StartsWith("dry", StringComparison.Ordinal)) it.Reason += "; " + it.PumpWater;
                    else { pumpsSwapped++; it.Reason += "; " + it.PumpWater; }
                }
            }
            // Utility buildings that can't work without a road (no road edge: no electricity, no water or sewage
            // connection) get a "connect a road" line (DECISIONS-buildings #42).
            var connectRoad = new List<object>();
            foreach (var it in items)
                if (it.Status == "placed" && it.Placed != Entity.Null && PlacementRules.NeedsRoad(it.Family) && em.GetComponentData<Building>(it.Placed).m_RoadEdge == Entity.Null)
                    connectRoad.Add(PlacementRules.ConnectRoad(it.Cs1Info ?? it.Prefab, PlacementRules.Position(it.Position.x, it.Position.z)));

            var list = new List<object>();
            // Not placed: one "Hospital: place it yourself at CS1 (x, z) = CS2 (x, z) (why)" line each, in the web report's words.
            var placeYourself = new List<object>();
            int powerPlanned = 0, powerPlaced = 0, waterPlanned = 0, waterPlaced = 0;
            foreach (var it in items)
            {
                bool ok = it.Status == "placed" || it.Status == "alreadyPlaced";
                if (PlacementRules.IsPower(it.Family)) { powerPlanned++; if (ok) powerPlaced++; }
                if (PlacementRules.IsWater(it.Family)) { waterPlanned++; if (ok) waterPlaced++; }
            }
            var warnings = new List<object>();
            foreach (var w in PlacementRules.SupplyWarnings(powerPlanned, powerPlaced, waterPlanned, waterPlaced)) warnings.Add(w);
            // a depot type with none placed: its lines run no vehicles (RC3, Skibidi's Bus Depot)
            var depots = new SortedDictionary<string, int[]>();
            foreach (var it in items)
            {
                var k = PlacementRules.DepotKind(it.Prefab);
                if (k == null) continue;
                int[] c;
                if (!depots.TryGetValue(k, out c)) depots[k] = c = new int[2];
                c[0]++;
                if (it.Status == "placed" || it.Status == "alreadyPlaced") c[1]++;
            }
            foreach (var kv in depots) if (kv.Value[1] == 0) warnings.Add(PlacementRules.DepotWarning(kv.Key, kv.Value[0]));
            foreach (var it in items)
            {
                if (it.Status == "placed" || it.Status == "alreadyPlaced") continue;
                var why = it.Reason ?? it.Status;
                int cut = why.IndexOf(" (tried", StringComparison.Ordinal);
                if (cut > 0) why = why.Substring(0, cut);
                if (why.StartsWith("not placed: ", StringComparison.Ordinal)) why = why.Substring("not placed: ".Length);
                if (why.Contains("place it yourself")) why = it.Status; // a reason without "(tried …)": keep the line short
                placeYourself.Add(PlacementRules.PlaceYourself(it.Cs1Info ?? it.Prefab, Cs1Pos(it), why));
            }
            foreach (var it in items)
                list.Add(new Dictionary<string, object>
                {
                    { "id", it.Id }, { "prefab", it.Prefab }, { "cs1Info", it.Cs1Info }, { "status", it.Status }, { "reason", it.Reason },
                    { "cs1", new List<object> { R((float)(it.X - it.Dx)), R((float)(it.Z - it.Dz)) } }, // CS1 position, for "place it yourself"
                    { "cs2Spot", new List<object> { R((float)(it.X + it.Ox)), R((float)(it.Z + it.Oz)) } }, // the same spot in the CS2 city
                    { "x", R(it.Position.x) }, { "y", R(it.Position.y) }, { "z", R(it.Position.z) },
                    { "yaw", R(math.degrees(math.atan2(it.Forward.x, it.Forward.y))) },
                    { "lot", new List<object> { it.Lot.x, it.Lot.y } }, { "snapDistance", R(it.SnapDistance) },
                    { "alternative", it.Alternative }, { "slide", R(it.Slide) }, { "moved", R(it.Moved) }, { "distance", R(it.Distance) }, { "fallbackPass", it.FallbackPass }, { "movedToOtherRoad", it.MovedToOtherRoad },
                    { "alignShift", it.AlignShift >= 0 ? (object)R(it.AlignShift) : null }, { "alignNote", it.AlignNote }, { "platformCheck", it.PlatformCheck },
                    { "entity", it.Placed == Entity.Null ? null : (object)it.Placed.Index }, { "subNetsExpected", it.SubNetsExpected }, { "subNetsBuilt", it.SubNetsBuilt },
                });
            var report = new Dictionary<string, object>
            {
                { "items", items.Count }, { "placed", placed }, { "alreadyPlaced", alreadyPlaced }, { "warnings", warnings }, { "placeYourself", placeYourself }, { "connectRoad", connectRoad }, { "depotRetries", depotRetries }, { "alignStations", alignStations }, { "stationsAligned", stationsAligned }, { "stationsNotAligned", stationsNoTrack }, { "stationsAnchorBlocked", stationsAnchorBlocked }, { "toolApply", toolApply }, { "toolApplied", applied.Count }, { "applyBatches", applyBatches }, { "applyFailed", applyFailed }, { "nodesGuarded", nodesGuarded }, { "roadGuard", roadCheck }, { "ok", roadsChanged == 0 && applyFailed == 0 }, { "depotsMoved", DepotsMoved(items) },
                { "pumps", new Dictionary<string, object> { { "slidToWater", pumpsSlidToWater }, { "groundwater", pumpsSwapped }, { "dry", pumpsDry }, { "noWaterData", pumpsUnknown }, { "shoreReach", pumpShoreReach }, { "samples", pumpSamples } } }, { "usedAlternative", usedAlternative }, { "netAttachedSkipped", skippedAttached }, { "slid", slid }, { "movedToFreeLand", moved }, { "movedByPass", ToObj(movedByPass) }, { "distanceStats", DistanceStats(items) }, { "fallback", defaultFallback }, { "nearbyRadius", nearbyRadius }, { "wideRadius", wideRadius }, { "connected", connected }, { "connectedToSnappedRoad", connectedToSnapEdge },
                { "failed", items.Count - placed - alreadyPlaced }, { "failuresByReason", ToObj(failures) },
                { "roadEdges", roads.Count }, { "existingBuildings", existing.Count }, { "batches", batches }, { "frames", frames }, { "subNetCourses", subNetCourses }, { "buildingsWithSubNets", withSubNets }, { "subNetsMissing", subNetsMissing }, { "resolveFrames", resolveFrames }, { "frameBudgetMs", frameBudgetMs },
                { "offset", new List<object> { ox, oz } }, { "seconds", Math.Round(sw.Elapsed.TotalSeconds, 2) }, { "results", list },
            };
            var reportPath = P.Str(a, "report", null);
            if (!string.IsNullOrEmpty(reportPath))
            {
                reportPath = Environment.ExpandEnvironmentVariables(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
                File.WriteAllText(reportPath, MiniJson.Write(report), new UTF8Encoding(false));
            }
            // The full per-item list goes to the file; the exec reply stays small.
            var reply = new Dictionary<string, object>(report);
            reply.Remove("results");
            reply["sample"] = list.GetRange(0, Math.Min(10, list.Count));
            // every station, compact (decision #48: the alignment per station must reach the mod's import report)
            var stations = new List<object>();
            foreach (var it in items)
                if (it.Station)
                    stations.Add(new Dictionary<string, object>
                    {
                        { "cs1Info", it.Cs1Info }, { "prefab", it.Prefab }, { "status", it.Status }, { "alignShift", it.AlignShift >= 0 ? (object)R(it.AlignShift) : null },
                        { "alignNote", it.AlignNote }, { "platformCheck", it.PlatformCheck }, { "subNets", it.SubNetsBuilt + "/" + it.SubNetsExpected },
                    });
            reply["stations"] = stations;
            yield return reply;
        }

        /// <summary>
        /// The active tool's Apply (buildings decision #47): ToolSystem's last-updated tool gets applyMode = Apply (a
        /// protected setter), so ToolOutputSystem commits this frame's Temp entities, as the object tool does on a click.
        /// The same reflection as worker-4's transit Ops.SetApply.
        /// </summary>
        /// <summary>Temporary Game.Net.Fixed on imported top-level two-edge nodes (all of them when batch is null, else within r m of the batch's buildings).</summary>
        static List<Entity> GuardNodes(EntityManager em, List<Item> batch, float r)
        {
            var res = new List<Entity>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>(), ComponentType.ReadOnly<Game.Net.Fixed>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var n in es)
                {
                    var np = em.GetComponentData<Node>(n).m_Position.xz;
                    bool near = batch == null;
                    if (!near) foreach (var it in batch) if (math.distance(it.Position.xz, np) < r) { near = true; break; }
                    if (!near) continue;
                    var ce = em.GetBuffer<ConnectedEdge>(n, true);
                    int ends = 0; bool top = true;
                    for (int i = 0; i < ce.Length; i++)
                    {
                        var ed = em.GetComponentData<Edge>(ce[i].m_Edge);
                        if (ed.m_Start == n || ed.m_End == n) ends++;
                        if (em.HasComponent<Owner>(ce[i].m_Edge)) top = false;
                    }
                    if (ends != 2 || !top) continue;
                    em.AddComponentData(n, new Game.Net.Fixed { m_Index = -1 });
                    res.Add(n);
                }
            return res;
        }

        static bool SetApply(World world)
        {
            var ts = world.GetOrCreateSystemManaged<ToolSystem>();
            var f = typeof(ToolSystem).GetField("m_LastTool", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var tool = (f != null ? f.GetValue(ts) as ToolBaseSystem : null) ?? ts.activeTool;
            if (tool == null) return false;
            var p = typeof(ToolBaseSystem).GetProperty("applyMode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var set = p != null ? p.GetSetMethod(true) : null;
            if (set == null) return false;
            set.Invoke(tool, new object[] { ApplyMode.Apply });
            return true;
        }

        /// <summary>
        /// Decision #48: each placed track edge of the station (its sub-nets): the closest approach to a parallel CS1 track
        /// (sampled along the edge): "1 track edge(s): 0.4 m from the nearest parallel CS1 track".
        /// </summary>
        static string PlatformCheck(EntityManager em, Entity building, List<Cs1Track> tracks, float ox, float oz)
        {
            if (!em.HasBuffer<Game.Net.SubNet>(building)) return "no sub-nets";
            var buf = em.GetBuffer<Game.Net.SubNet>(building, true);
            var dists = new List<string>();
            for (int i = 0; i < buf.Length; i++)
            {
                var e = buf[i].m_SubNet;
                if (!em.HasComponent<Edge>(e) || !em.HasComponent<Curve>(e) || !em.HasComponent<PrefabRef>(e)) continue;
                var np = em.GetComponentData<PrefabRef>(e).m_Prefab;
                if (!em.HasComponent<NetData>(np) || (em.GetComponentData<NetData>(np).m_RequiredLayers & (Layer.TrainTrack | Layer.SubwayTrack | Layer.TramTrack)) == 0) continue;
                // the closest approach between the platform edge and a parallel CS1 track (a CS1 track that continues the
                // platform from its end counts as 0, wherever it bends afterwards)
                var pc = em.GetComponentData<Curve>(e).m_Bezier;
                var pdir = math.normalizesafe(pc.d.xz - pc.a.xz, new float2(1, 0));
                float best = float.MaxValue;
                foreach (var tr in tracks)
                {
                    var bz = new Bezier4x3(new float3(tr.A.x + ox, 0, tr.A.y + oz), new float3(tr.B.x + ox, 0, tr.B.y + oz), new float3(tr.C.x + ox, 0, tr.C.y + oz), new float3(tr.D.x + ox, 0, tr.D.y + oz));
                    for (int k = 0; k <= 16; k++)
                    {
                        var pp = MathUtils.Position(pc, k / 16f).xz;
                        float t;
                        float d = MathUtils.Distance(bz.xz, pp, out t);
                        if (d >= best) continue;
                        var tan = math.normalizesafe(MathUtils.Tangent(bz, t).xz, pdir);
                        if (math.abs(math.dot(tan, pdir)) < 0.87f) continue; // a crossing track
                        best = d;
                    }
                }
                dists.Add(best == float.MaxValue ? "none" : best.ToString("F1"));
            }
            return dists.Count == 0 ? "no platform track built" : dists.Count + " track edge(s): " + string.Join(", ", dists) + " m from the nearest parallel CS1 track";
        }

        /// <summary>Line offsets merged when closer than tol (their mean), sorted.</summary>
        static List<float> MergeLines(List<float> offsets, float tol)
        {
            var o = new List<float>(offsets); o.Sort();
            var res = new List<float>();
            int i = 0;
            while (i < o.Count)
            {
                int j = i; float sum = 0;
                while (j < o.Count && o[j] - o[i] <= tol) { sum += o[j]; j++; }
                res.Add(sum / (j - i));
                i = j;
            }
            return res;
        }

        sealed class Cs1Track { public float2 A, B, C, D; public string Family; }

        /// <summary>The plan's track segments (prefab type TrackPrefab), in plan coordinates, with their family.</summary>
        static List<Cs1Track> LoadTracks(string path)
        {
            path = Environment.ExpandEnvironmentVariables(path);
            if (Directory.Exists(path)) path = Path.Combine(path, "cs1conv-plan.json");
            var root = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            var res = new List<Cs1Track>();
            var prefabs = root != null && root.ContainsKey("prefabs") ? root["prefabs"] as Dictionary<string, object> : null;
            var segs = root != null && root.ContainsKey("segments") ? root["segments"] as List<object> : null;
            if (prefabs == null || segs == null) return res;
            foreach (var o in segs)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var key = P.Str(d, "prefab", "");
                var pf = prefabs.ContainsKey(key) ? prefabs[key] as Dictionary<string, object> : null;
                if (pf == null || P.Str(pf, "type", "") != "TrackPrefab") continue;
                var fam = TrackFamily(P.Str(pf, "name", "") + " " + P.Str(pf, "source", ""));
                var bz = d.ContainsKey("bezier") ? d["bezier"] as List<object> : null;
                if (fam == null || bz == null || bz.Count != 4) continue;
                Func<int, float2> pt = i => { var l = (List<object>)bz[i]; return new float2(Convert.ToSingle(l[0]), Convert.ToSingle(l[2])); };
                res.Add(new Cs1Track { A = pt(0), B = pt(1), C = pt(2), D = pt(3), Family = fam });
            }
            return res;
        }

        static string TrackFamily(string name)
        {
            if (name.IndexOf("Subway", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Metro", StringComparison.OrdinalIgnoreCase) >= 0) return "subway";
            if (name.IndexOf("Tram", StringComparison.OrdinalIgnoreCase) >= 0) return "tram";
            if (name.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Rail", StringComparison.OrdinalIgnoreCase) >= 0) return "train";
            return null;
        }

        /// <summary>
        /// Decision #48: where a station must stand so its platform tracks lie on the CS1 tracks it served. The platform
        /// axis and centre come from the prefab's track sub-nets (local space); the CS1 axis and bundle centre from the
        /// plan's tracks of the same family within the station's reach, roughly parallel to its lot. The station keeps the
        /// CS1 facing (of the two directions along the track, the one nearer it), and its platform centre goes to the CS1
        /// lot centre along the track. False (with a reason) when the prefab has no platform track or no CS1 track is near.
        /// </summary>
        static bool StationAnchor(EntityManager em, Entity prefab, Item it, float2 centre, List<Cs1Track> tracks, out float2 pos, out float2 forward, out string note)
        {
            pos = centre; forward = new float2(0, 1); note = null;
            if (!em.HasBuffer<Game.Prefabs.SubNet>(prefab)) { note = "no platform track in the prefab"; return false; }
            var buf = em.GetBuffer<Game.Prefabs.SubNet>(prefab, true);
            string fam = null;
            var mids = new List<float2>(); var dirs = new List<float2>();
            for (int i = 0; i < buf.Length; i++)
            {
                var np = buf[i].m_Prefab;
                if (np == Entity.Null || !em.HasComponent<NetData>(np)) continue;
                var layers = em.GetComponentData<NetData>(np).m_RequiredLayers;
                string f = (layers & Layer.TrainTrack) != 0 ? "train" : (layers & Layer.SubwayTrack) != 0 ? "subway" : (layers & Layer.TramTrack) != 0 ? "tram" : null;
                if (f == null) continue;
                if (fam == null) fam = f;
                var c = buf[i].m_Curve;
                var dv = c.d.xz - c.a.xz;
                if (math.lengthsq(dv) < 1f) continue;
                mids.Add(MathUtils.Position(c, 0.5f).xz);
                dirs.Add(math.normalize(dv));
            }
            if (fam == null || dirs.Count == 0) { note = "no platform track in the prefab"; return false; }
            var u = dirs[0];
            float2 usum = 0, csum = 0; int un = 0;
            for (int i = 0; i < dirs.Count; i++)
            {
                var dv = math.dot(dirs[i], u) < 0 ? -dirs[i] : dirs[i];
                if (math.abs(math.dot(dv, u)) < 0.87f) continue; // crossovers off the platform axis
                usum += dv; csum += mids[i]; un++;
            }
            u = math.normalize(usum);
            var cLocal = csum / un;
            // CS1 tracks of the same family near the station, roughly along its lot (perpendicular to the CS1 facing)
            var facing = it.Facing != null ? math.normalizesafe(new float2((float)it.Facing[0], (float)it.Facing[1]), new float2(0, 1)) : new float2(0, 1);
            var lotAxis = new float2(facing.y, -facing.x);
            var lotSize = em.HasComponent<BuildingData>(prefab) ? em.GetComponentData<BuildingData>(prefab).m_LotSize : new int2(16, 6);
            float reach = math.max(lotSize.x, lotSize.y) * 4f + 24f;
            var ox = (float)it.Ox; var oz = (float)it.Oz;
            // only the tracks inside the CS1 lot's depth band and near its length: a station can sit beside a whole
            // bundle of through tracks (Skibidi's west terminus: 6 parallel tracks, 2 within its lot)
            float halfDepth = (it.Cs1Lot != null ? (float)it.Cs1Lot[1] : lotSize.y) * 4f + 4f;
            float halfWidth = (it.Cs1Lot != null ? (float)it.Cs1Lot[0] : lotSize.x) * 4f + 40f;
            var closest = new List<float2>();
            foreach (var tr in tracks)
            {
                if (tr.Family != fam) continue;
                var bz = new Bezier4x3(new float3(tr.A.x + ox, 0, tr.A.y + oz), new float3(tr.B.x + ox, 0, tr.B.y + oz), new float3(tr.C.x + ox, 0, tr.C.y + oz), new float3(tr.D.x + ox, 0, tr.D.y + oz));
                float t;
                float dd = MathUtils.Distance(bz.xz, centre, out t);
                if (dd > reach) continue;
                var tan = math.normalizesafe(MathUtils.Tangent(bz, t).xz, lotAxis);
                if (math.abs(math.dot(tan, lotAxis)) < 0.87f) continue; // a crossing track
                var cp = MathUtils.Position(bz, t).xz;
                if (math.abs(math.dot(cp - centre, facing)) > halfDepth || math.abs(math.dot(cp - centre, lotAxis)) > halfWidth) continue;
                closest.Add(cp);
            }
            if (closest.Count == 0) { note = "no CS1 " + fam + " track along the CS1 lot"; return false; }
            // The station keeps the CS1 orientation: its platform runs along the CS1 lot (a CS1 station's own tracks do).
            // The CS1 tracks' tangent where they start, at the station's end, turned Skibidi's terminus 4.7 deg (6 m off at
            // its middle).
            var tdir = lotAxis;
            var n = new float2(-tdir.y, tdir.x);
            // CS1 track lines across the lot (offsets along n, merged within 3 m) and the station's own platform lines
            var cs1Lines = new List<float>();
            foreach (var q in closest) cs1Lines.Add(math.dot(q - centre, n));
            cs1Lines = MergeLines(cs1Lines, 3f);
            var uPerp = new float2(-u.y, u.x);
            var ownLines = new List<float>();
            for (int i = 0; i < mids.Count; i++) if (math.abs(math.dot(math.dot(dirs[i], u) < 0 ? -dirs[i] : dirs[i], u)) >= 0.87f) ownLines.Add(math.dot(mids[i], uPerp));
            int k = MergeLines(ownLines, 3f).Count;
            // where the platform centroid would be unaligned (CS1 centre and facing), across the track
            var right0 = new float2(facing.y, -facing.x);
            float off0 = math.dot(cLocal.x * right0 + cLocal.y * facing, n);
            // fewer platform lines than CS1 lines in the lot (Skibidi's terminus: 1 double-track platform, 2 CS1 double
            // tracks): the k adjacent CS1 lines nearest the unaligned platform, not the centre of all (which left the
            // platform between two tracks)
            float target = PlacementRules.NearestLines(cs1Lines, k, off0);
            var Q = centre + n * target;
            // rotation and position (PlacementRules.AnchorPose, tested game-free)
            double px, pz, fx, fz;
            PlacementRules.AnchorPose(u.x, u.y, cLocal.x, cLocal.y, tdir.x, tdir.y, Q.x, Q.y, centre.x, centre.y, facing.x, facing.y, out px, out pz, out fx, out fz);
            pos = new float2((float)px, (float)pz);
            forward = new float2((float)fx, (float)fz);
            float across = math.dot(pos - centre, n), alongShift = math.dot(pos - centre, tdir);
            float turned = math.degrees(math.acos(math.clamp(math.dot(forward, facing), -1f, 1f)));
            note = "anchored on " + Math.Min(k, cs1Lines.Count) + " of " + cs1Lines.Count + " CS1 " + fam + " track line(s): shifted " + math.abs(across).ToString("F1") + " m across and " + math.abs(alongShift).ToString("F1") + " m along, turned " + turned.ToString("F1") + " deg";
            return true;
        }

        /// <summary>Report lines for the depots placed away from their CS1 spot by decision #152.</summary>
        static List<object> DepotsMoved(List<Item> items)
        {
            var l = new List<object>();
            foreach (var it in items)
                if (it.DepotRetry && it.Status == "placed")
                    l.Add((it.Cs1Info ?? it.Prefab) + ": " + PlacementRules.DepotMovedNote + " (" + it.Distance.ToString("F0") + " m from its CS1 spot, " + Cs1Pos(it) + ")");
            return l;
        }

        /// <summary>op platformcheck {plan, offsetX, offsetZ} [main]: PlatformCheck for every placed station building with track sub-nets. Read only.</summary>
        public static object PlatformCheckOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var tracks = LoadTracks(P.Required(a, "plan"));
            float ox = (float)P.Num(a, "offsetX", 0), oz = (float)P.Num(a, "offsetZ", 0);
            var res = new List<object>();
            foreach (var b in FindBuildings(em))
            {
                var pf = b.Prefab;
                if (!em.HasComponent<TransportStationData>(pf) && !em.HasComponent<PublicTransportStationData>(pf)) continue;
                res.Add(ps.GetPrefabName(pf) + " @ " + PlacementRules.Position(b.Position.x, b.Position.z) + ": " + PlatformCheck(em, b.Entity, tracks, ox, oz));
            }
            return new Dictionary<string, object> { { "stations", res } };
        }

        static void Fail(Item it, Dictionary<string, int> failures, string kind, string reason)
        {
            it.Status = kind;
            it.Reason = reason;
            int n;
            failures.TryGetValue(kind, out n);
            failures[kind] = n + 1;
        }

        static bool IsSurfacePump(EntityManager em, Entity prefab)
        {
            if (!em.HasComponent<WaterPumpingStationData>(prefab)) return false;
            var t = em.GetComponentData<WaterPumpingStationData>(prefab).m_Types;
            return (t & AllowedWaterTypes.SurfaceWater) != 0 && (t & AllowedWaterTypes.Groundwater) == 0;
        }

        /// <summary>Metres from p to the nearest water deeper than 0.5 m (rings every 4 m, 24 directions), +inf beyond maxR.</summary>
        static float NearestWater(Func<float2, float> depth, float2 p, float maxR)
        {
            if (depth(p) > 0.5f) return 0f;
            for (float r = 4f; r <= maxR + 0.01f; r += 4f)
                for (int k = 0; k < 24; k++)
                {
                    float ang = k * math.PI * 2f / 24f;
                    if (depth(p + r * new float2(math.cos(ang), math.sin(ang))) > 0.5f) return r;
                }
            return float.PositiveInfinity;
        }

        static string Cs1Pos(Item it) { return PlacementRules.Where(it.X, it.Z, it.Dx, it.Dz, it.Ox, it.Oz); }

        static string Article(string what) { return what == null ? "something" : (what.StartsWith("another") ? "" : "a ") + what; }

        /// <summary>Lot on a road side at curve parameter t: front edge on the road side, facing the road, at the road's height.</summary>
        static bool TryOnRoad(Item it, KeyValuePair<string, Entity> prefab, int ni, RoadEdge road, float t, float side, RoadGrid grid,
            List<float2[]> existing, List<float2[]> placedLots, bool checkCollisions, ref TerrainHeightData heights, ref string lastHit)
        {
            var lot = World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<BuildingData>(prefab.Value).m_LotSize;
            var c = MathUtils.Position(road.Curve, t);
            var tan = math.normalizesafe(MathUtils.Tangent(road.Curve, t).xz, new float2(1, 0));
            var n = new float2(-tan.y, tan.x) * side;
            var frontPoint = c.xz + n * road.HalfWidth;
            // At the road's level, like the game's own tool (the lot terraforms to it): a front below an embanked road
            // leaves the building unconnected.
            return TryAt(it, prefab, ni, frontPoint + n * (lot.y * 4f), -n, c.y, grid, existing, placedLots, checkCollisions, ref heights, ref lastHit);
        }

        /// <summary>Checks a lot at pos2 facing `forward`; on success records it on the item and reserves the lot.</summary>
        static bool TryAt(Item it, KeyValuePair<string, Entity> prefab, int ni, float2 pos2, float2 forward, float y, RoadGrid grid,
            List<float2[]> existing, List<float2[]> placedLots, bool checkCollisions, ref TerrainHeightData heights, ref string lastHit)
        {
            var lot = World.DefaultGameObjectInjectionWorld.EntityManager.GetComponentData<BuildingData>(prefab.Value).m_LotSize;
            if (checkCollisions)
            {
                var corners = Corners(pos2, forward, lot, 0.6f);
                float reach = math.length(new float2(lot.x, lot.y)) * 4f + 2f;
                string hit = null;
                foreach (var r in grid.Query(pos2 - reach - 32f, pos2 + reach + 32f))
                {
                    // Tunnels run under the lot. Elevated roads pass over a station: CS1 stations sit under road
                    // bridges, and the station must stay at its spot (DECISIONS-buildings #37).
                    if (!PlacementRules.RoadCanBlock(it.Station, (r.Flags.m_General & CompositionFlags.General.Tunnel) != 0, (r.Flags.m_General & CompositionFlags.General.Elevated) != 0)) continue;
                    if (LotHitsRoad(pos2, forward, lot, r)) { hit = "road"; break; }
                }
                if (hit == null) foreach (var l in existing) if (Overlap(corners, l)) { hit = "existing building"; break; }
                if (hit == null) foreach (var l in placedLots) if (Overlap(corners, l)) { hit = "another imported building"; break; }
                if (hit != null) { lastHit = hit; return false; }
                placedLots.Add(corners);
            }
            var frontPoint = pos2 + forward * (lot.y * 4f);
            if (float.IsNaN(y)) y = TerrainUtils.SampleHeight(ref heights, new float3(frontPoint.x, 0, frontPoint.y));
            it.Prefab = prefab.Key;
            it.Alternative = ni;
            it.PrefabEntity = prefab.Value;
            it.Lot = lot;
            it.Forward = forward;
            it.Position = new float3(pos2.x, y, pos2.y);
            it.Rotation = quaternion.LookRotationSafe(new float3(forward.x, 0, forward.y), new float3(0, 1, 0));
            it.Status = "ready";
            return true;
        }

        /// <summary>Road edges in 64 m cells, for the many nearby queries of the fit tests.</summary>
        internal sealed class RoadGrid
        {
            const float Cell = 64f;
            readonly Dictionary<long, List<RoadEdge>> m_Cells = new Dictionary<long, List<RoadEdge>>();
            static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

            public RoadGrid(List<RoadEdge> roads)
            {
                foreach (var r in roads)
                    for (int x = (int)math.floor(r.Bounds.x / Cell); x <= (int)math.floor(r.Bounds.z / Cell); x++)
                        for (int z = (int)math.floor(r.Bounds.y / Cell); z <= (int)math.floor(r.Bounds.w / Cell); z++)
                        {
                            List<RoadEdge> l;
                            if (!m_Cells.TryGetValue(Key(x, z), out l)) m_Cells[Key(x, z)] = l = new List<RoadEdge>();
                            l.Add(r);
                        }
            }

            /// <summary>Distinct edges whose bounds touch the cells of [min, max].</summary>
            public List<RoadEdge> Query(float2 min, float2 max)
            {
                var seen = new HashSet<RoadEdge>();
                var list = new List<RoadEdge>();
                for (int x = (int)math.floor(min.x / Cell); x <= (int)math.floor(max.x / Cell); x++)
                    for (int z = (int)math.floor(min.y / Cell); z <= (int)math.floor(max.y / Cell); z++)
                    {
                        List<RoadEdge> l;
                        if (!m_Cells.TryGetValue(Key(x, z), out l)) continue;
                        foreach (var r in l) if (seen.Add(r)) list.Add(r);
                    }
                return list;
            }
        }

        // ---------------------------------------------------------------- plan

        static List<Item> LoadItems(string path, int limit)
        {
            path = Environment.ExpandEnvironmentVariables(path);
            if (Directory.Exists(path)) path = Path.Combine(path, "cs1conv-plan.json");
            var root = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("plan is not a JSON object");
            var section = root.ContainsKey("buildings") ? root["buildings"] as Dictionary<string, object> : root;
            var tr = root.ContainsKey("transform") ? root["transform"] as Dictionary<string, object> : null;
            double dx = tr != null ? P.Num(tr, "dx", 0) : 0, dz = tr != null ? P.Num(tr, "dz", 0) : 0;
            var arr = section != null && section.ContainsKey("items") ? section["items"] as List<object> : null;
            if (arr == null) throw new InvalidDataException("no buildings.items in " + path);
            var items = new List<Item>();
            foreach (var o in arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                if (items.Count >= limit) break;
                items.Add(new Item
                {
                    Id = (int)P.Num(d, "id", 0),
                    Prefab = P.Str(d, "prefab", null),
                    Type = P.Str(d, "type", "BuildingPrefab"),
                    Fallback = P.Str(d, "fallback", null),
                    RequireRoad = P.Bool(d, "requireRoad", false),
                    MaxDistance = (float)P.Num(d, "maxDistance", 0),
                    Station = P.Str(d, "family", null) == "station",
                    Family = P.Str(d, "family", null),
                    Dx = dx, Dz = dz,
                    Cs1Info = P.Str(d, "cs1Info", null),
                    X = P.Num(d, "x", 0), Z = P.Num(d, "z", 0), Score = P.Num(d, "score", 0),
                    Front = Pair(d, "front"), Facing = Pair(d, "facing"), Cs1Lot = Pair(d, "cs1Lot"),
                    AccessSegment = (int)P.Num(d, "accessSegment", 0),
                    Alternatives = Strings(d, "alternatives"),
                });
            }
            return items;
        }

        static List<string> Strings(Dictionary<string, object> d, string key)
        {
            object v;
            var l = d.TryGetValue(key, out v) ? v as List<object> : null;
            var r = new List<string>();
            if (l != null) foreach (var x in l) if (x != null) r.Add(x.ToString());
            return r;
        }

        static double[] Pair(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v)) return null;
            var l = v as List<object>;
            if (l == null || l.Count < 2) return null;
            return new[] { Convert.ToDouble(l[0]), Convert.ToDouble(l[1]) };
        }

        // ---------------------------------------------------------------- world queries

        internal static List<RoadEdge> RoadEdges(EntityManager em)
        {
            var list = new List<RoadEdge>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Composition>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in es)
                {
                    var c = em.GetComponentData<Curve>(e).m_Bezier;
                    var comp = em.GetComponentData<Composition>(e);
                    float hw = 4f;
                    CompositionFlags cf = default;
                    if (comp.m_Edge != Entity.Null && em.HasComponent<NetCompositionData>(comp.m_Edge))
                    {
                        var cd = em.GetComponentData<NetCompositionData>(comp.m_Edge);
                        hw = cd.m_Width * 0.5f;
                        cf = cd.m_Flags;
                    }
                    var mn = math.min(math.min(c.a.xz, c.b.xz), math.min(c.c.xz, c.d.xz)) - hw;
                    var mx = math.max(math.max(c.a.xz, c.b.xz), math.max(c.c.xz, c.d.xz)) + hw;
                    bool highway = false;
                    var pr = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (pr != Entity.Null && em.HasComponent<RoadData>(pr)) highway = (em.GetComponentData<RoadData>(pr).m_Flags & Game.Prefabs.RoadFlags.UseHighwayRules) != 0;
                    list.Add(new RoadEdge { Entity = e, Curve = c, HalfWidth = hw, Flags = cf, Highway = highway, Bounds = new float4(mn.x, mn.y, mx.x, mx.y) });
                }
            }
            return list;
        }

        sealed class Found { public Entity Entity, Prefab; public float3 Position; }

        static List<Found> FindBuildings(EntityManager em)
        {
            var list = new List<Found>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                    list.Add(new Found { Entity = e, Prefab = em.GetComponentData<PrefabRef>(e).m_Prefab, Position = em.GetComponentData<Game.Objects.Transform>(e).m_Position });
            return list;
        }

        internal static List<float2[]> ExistingLots(EntityManager em)
        {
            var lots = new List<float2[]>();
            foreach (var b in FindBuildings(em))
            {
                if (!em.HasComponent<BuildingData>(b.Prefab)) continue;
                var lot = em.GetComponentData<BuildingData>(b.Prefab).m_LotSize;
                var rot = em.GetComponentData<Game.Objects.Transform>(b.Entity).m_Rotation;
                var fwd = math.normalizesafe(math.rotate(rot, new float3(0, 0, 1)).xz, new float2(0, 1));
                lots.Add(Corners(b.Position.xz, fwd, lot, 0.6f));
            }
            return lots;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>Lot rectangle (shrunk by `inset` m) as 4 corners; forward = the lot's front direction.</summary>
        static float2[] Corners(float2 centre, float2 forward, int2 lot, float inset)
        {
            var right = new float2(forward.y, -forward.x);
            float hw = math.max(0.1f, lot.x * 4f - inset), hd = math.max(0.1f, lot.y * 4f - inset);
            return new[] { centre + right * hw + forward * hd, centre - right * hw + forward * hd, centre - right * hw - forward * hd, centre + right * hw - forward * hd };
        }

        /// <summary>Separating-axis test for two convex quads.</summary>
        static bool Overlap(float2[] a, float2[] b)
        {
            return !Separated(a, b) && !Separated(b, a);
        }

        static bool Separated(float2[] a, float2[] b)
        {
            for (int i = 0; i < 4; i++)
            {
                var e = a[(i + 1) % 4] - a[i];
                var n = new float2(-e.y, e.x);
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in a) { float d = math.dot(p, n); amin = math.min(amin, d); amax = math.max(amax, d); }
                foreach (var p in b) { float d = math.dot(p, n); bmin = math.min(bmin, d); bmax = math.max(bmax, d); }
                if (amax < bmin || bmax < amin) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the road's surface reaches into the lot shrunk by 1 m: the road's centre line is sampled every ~2 m
        /// and each point's distance to that rectangle is compared with the road's half width. (A first version sampled
        /// the lot on a grid whose spacing grew with the lot, and a 12 m road fitted between two rows of a large lot.)
        /// </summary>
        static bool LotHitsRoad(float2 centre, float2 forward, int2 lot, RoadEdge r)
        {
            float hw = lot.x * 4f - 1f, hd = lot.y * 4f - 1f;
            float reach = math.sqrt(hw * hw + hd * hd) + 1f;
            if (centre.x + reach < r.Bounds.x || centre.x - reach > r.Bounds.z || centre.y + reach < r.Bounds.y || centre.y - reach > r.Bounds.w) return false;
            var right = new float2(forward.y, -forward.x);
            var xz = r.Curve.xz;
            int n = math.clamp((int)math.ceil(MathUtils.Length(xz) / 2f), 2, 512);
            for (int i = 0; i <= n; i++)
            {
                var d = MathUtils.Position(xz, (float)i / n) - centre;
                float u = math.abs(math.dot(d, right)) - hw, v = math.abs(math.dot(d, forward)) - hd;
                float dist = math.length(math.max(new float2(u, v), 0f)) + math.min(math.max(u, v), 0f); // signed distance to the rectangle
                if (dist < r.HalfWidth) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- op: inspect

        public static object Inspect(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var p = new float2((float)P.Num(a, "x", 0), (float)P.Num(a, "z", 0));
            float r = (float)P.Num(a, "r", 60);
            var list = new List<object>();
            foreach (var b in FindBuildings(em))
            {
                if (math.distance(b.Position.xz, p) > r) continue;
                PrefabBase prefab;
                ps.TryGetPrefab(b.Prefab, out prefab);
                var bd = em.GetComponentData<Building>(b.Entity);
                var rot = em.GetComponentData<Game.Objects.Transform>(b.Entity).m_Rotation;
                var fwd = math.rotate(rot, new float3(0, 0, 1));
                list.Add(new Dictionary<string, object>
                {
                    { "entity", b.Entity.Index }, { "prefab", prefab != null ? prefab.name : null },
                    { "x", R(b.Position.x) }, { "y", R(b.Position.y) }, { "z", R(b.Position.z) },
                    { "yaw", R(math.degrees(math.atan2(fwd.x, fwd.z))) },
                    { "roadEdge", bd.m_RoadEdge == Entity.Null ? null : (object)bd.m_RoadEdge.Index },
                    { "subAreas", em.HasBuffer<Game.Areas.SubArea>(b.Entity) ? em.GetBuffer<Game.Areas.SubArea>(b.Entity, true).Length : 0 },
                    { "subNets", em.HasBuffer<Game.Net.SubNet>(b.Entity) ? em.GetBuffer<Game.Net.SubNet>(b.Entity, true).Length : 0 },
                    { "subObjects", em.HasBuffer<Game.Objects.SubObject>(b.Entity) ? em.GetBuffer<Game.Objects.SubObject>(b.Entity, true).Length : 0 },
                });
            }
            // Road edges near the point: prefab, width and whether the edge has pedestrian lanes (what buildings connect to).
            var roadsNear = new List<object>();
            foreach (var re in RoadEdges(em))
            {
                float t;
                float d = MathUtils.Distance(re.Curve.xz, p, out t);
                if (d > r) continue;
                PrefabBase rp;
                ps.TryGetPrefab(em.GetComponentData<PrefabRef>(re.Entity).m_Prefab, out rp);
                int ped = 0, car = 0;
                if (em.HasBuffer<Game.Net.SubLane>(re.Entity))
                    foreach (var sl in em.GetBuffer<Game.Net.SubLane>(re.Entity, true))
                    {
                        if (em.HasComponent<Game.Net.PedestrianLane>(sl.m_SubLane)) ped++;
                        if (em.HasComponent<Game.Net.CarLane>(sl.m_SubLane)) car++;
                    }
                roadsNear.Add(new Dictionary<string, object>
                {
                    { "entity", re.Entity.Index }, { "prefab", rp != null ? rp.name : null }, { "distance", R(d) }, { "halfWidth", R(re.HalfWidth) }, { "y", R(MathUtils.Position(re.Curve, t).y) }, { "t", R(t) },
                    { "pedestrianLanes", ped }, { "carLanes", car },
                });
            }
            // Every edge near the point, whatever its components (for debugging the road query's filters).
            var edgesNear = new List<object>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Curve>()))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    float t;
                    float d = MathUtils.Distance(em.GetComponentData<Curve>(e).m_Bezier.xz, p, out t);
                    if (d > r) continue;
                    PrefabBase ep = null;
                    if (em.HasComponent<PrefabRef>(e)) ps.TryGetPrefab(em.GetComponentData<PrefabRef>(e).m_Prefab, out ep);
                    edgesNear.Add(new Dictionary<string, object>
                    {
                        { "entity", e.Index }, { "prefab", ep != null ? ep.name : null }, { "distance", R(d) },
                        { "road", em.HasComponent<Road>(e) }, { "owner", em.HasComponent<Owner>(e) }, { "temp", em.HasComponent<Temp>(e) },
                        { "deleted", em.HasComponent<Deleted>(e) }, { "composition", em.HasComponent<Composition>(e) },
                    });
                }
            return new Dictionary<string, object> { { "count", list.Count }, { "buildings", list }, { "roads", roadsNear }, { "edges", edgesNear } };
        }

        /// <summary>
        /// The building prefab's sub-nets (Game.Prefabs.SubNet: depot / station tracks, internal paths and roads) as
        /// Permanent course definitions owned by the building definition created in the same frame, as
        /// ZoneSpawnSystem/BuildingConstructionSystem.CreateSubNet do: local curve → world, elevation = local height,
        /// IsFirst/IsLast | DisableMerge, OwnerDefinition = (building prefab, position, rotation). Every element is built:
        /// m_Upgrades is not "upgrade only" but the composition flags the edge gets (station platform pieces, stairs),
        /// passed on as an Upgraded component like vanilla ZoneSpawnSystem.CreateSubNet (buildings decision #39).
        /// Returns the number of courses.
        /// </summary>
        static int ExpectedSubNets(EntityManager em, Entity prefab, PrefabSystem skipInvisible = null)
        {
            if (!em.HasBuffer<Game.Prefabs.SubNet>(prefab)) return 0;
            var buf = em.GetBuffer<Game.Prefabs.SubNet>(prefab, true);
            int n = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].m_Prefab == Entity.Null) continue;
                if (skipInvisible != null) { var nm = skipInvisible.GetPrefabName(buf[i].m_Prefab); if (nm != null && nm.StartsWith("Invisible", StringComparison.Ordinal)) continue; }
                n++;
            }
            return n;
        }

        static int CreateSubNets(EntityManager em, Entity buildingPrefab, float3 pos, quaternion rot, ref Unity.Mathematics.Random rnd, bool permanent = true, PrefabSystem skipInvisible = null)
        {
            if (!em.HasBuffer<Game.Prefabs.SubNet>(buildingPrefab)) return 0;
            var buf = em.GetBuffer<Game.Prefabs.SubNet>(buildingPrefab, true);
            var subNets = new List<Game.Prefabs.SubNet>(buf.Length);
            for (int i = 0; i < buf.Length; i++) subNets.Add(buf[i]); // copy: creating entities invalidates the buffer
            int n = 0;
            foreach (var sn in subNets)
            {
                if (sn.m_Prefab == Entity.Null) continue;
                // the tool-apply path (decision #47) writes no invisible paths: applied, an invisible path ending on the
                // street splits the imported road edge (Skibidi: 8 road edges gone), and CS2 joins the building's spawn
                // locations to the street's lanes without them (the buses dispatch); tracks, platforms and visible roads stay
                if (skipInvisible != null) { var nm = skipInvisible.GetPrefabName(sn.m_Prefab); if (nm != null && nm.StartsWith("Invisible", StringComparison.Ordinal)) continue; }
                var local = sn.m_Curve;
                var curve = ObjectUtils.LocalToWorld(pos, rot, local);
                var start = new CoursePos
                {
                    m_Position = curve.a, m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve), rot),
                    m_CourseDelta = 0f, m_Elevation = new float2(local.a.y), m_ParentMesh = sn.m_ParentMesh.x,
                    m_Flags = CoursePosFlags.IsFirst | CoursePosFlags.DisableMerge,
                };
                var end = new CoursePos
                {
                    m_Position = curve.d, m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve), rot),
                    m_CourseDelta = 1f, m_Elevation = new float2(local.d.y), m_ParentMesh = sn.m_ParentMesh.y,
                    m_Flags = CoursePosFlags.IsLast | CoursePosFlags.DisableMerge,
                };
                if (curve.a.Equals(curve.d)) { start.m_Flags |= CoursePosFlags.IsLast; end.m_Flags |= CoursePosFlags.IsFirst; }
                var e = em.CreateEntity();
                // permanent=false (the tool-apply path, decision #47): a Temp course owned by the building's Temp definition,
                // as the object tool writes it; the Apply commits both
                em.AddComponentData(e, new CreationDefinition { m_Prefab = sn.m_Prefab, m_RandomSeed = rnd.NextInt(), m_Flags = permanent ? CreationFlags.Permanent : 0 });
                em.AddComponentData(e, new OwnerDefinition { m_Prefab = buildingPrefab, m_Position = pos, m_Rotation = rot });
                em.AddComponentData(e, new NetCourse { m_Curve = curve, m_StartPosition = start, m_EndPosition = end, m_Length = MathUtils.Length(curve), m_FixedIndex = -1 });
                if (!sn.m_Upgrades.Equals(default(CompositionFlags))) em.AddComponentData(e, new Upgraded { m_Flags = sn.m_Upgrades });
                em.AddComponent<Updated>(e);
                em.AddComponent<Deleted>(e);
                n++;
            }
            return n;
        }

        /// <summary>Distance between placed and CS1 lot centres over the placed items: median, p90, max and counts over 50/150/400 m.</summary>
        static Dictionary<string, object> DistanceStats(List<Item> items)
        {
            var d = new List<float>();
            foreach (var it in items) if (it.Status == "placed" && it.Distance >= 0) d.Add(it.Distance);
            d.Sort();
            Func<float, object> q = f => d.Count == 0 ? null : (object)R(d[Math.Min(d.Count - 1, (int)(f * (d.Count - 1)))]);
            return new Dictionary<string, object>
            {
                { "placed", d.Count }, { "median", q(0.5f) }, { "p90", q(0.9f) }, { "max", q(1f) },
                { "over50m", d.FindAll(x => x > 50).Count }, { "over150m", d.FindAll(x => x > 150).Count }, { "over400m", d.FindAll(x => x > 400).Count },
            };
        }

        static Dictionary<string, object> ToObj(Dictionary<string, int> d)
        {
            var o = new Dictionary<string, object>();
            foreach (var kv in d) o[kv.Key] = kv.Value;
            return o;
        }

        static double R(double v) { return Math.Round(v, 2); }
    }
}
