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
            public float Distance = -1;      // metres between the placed lot centre and the CS1 lot centre (every placed item)
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
        }

        internal sealed class RoadEdge
        {
            public Entity Entity;
            public Bezier4x3 Curve;
            public float HalfWidth;
            public CompositionFlags Flags;
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
            string defaultFallback = P.Str(a, "fallback", "nearby");
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

            var items = LoadItems(P.Required(a, "plan"), limit);
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
            foreach (var it in items)
            {
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
                        prefabs.Add(new KeyValuePair<string, Entity>(nm, pe));
                }
                if (prefabs.Count == 0) { Fail(it, failures, "prefabMissing", "CS2 prefab '" + it.Prefab + "' (and its alternatives) not found"); continue; }
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
                if (road == null)
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
                            foreach (var slide in slides)
                            {
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
                string fallback = it.Fallback ?? defaultFallback;
                var passes = new List<KeyValuePair<string, float>>();
                if (fallback == "nearby" || fallback == "anywhere")
                {
                    if (nearbyRadius > 0) passes.Add(new KeyValuePair<string, float>("nearby", nearbyRadius));
                    if (wideRadius > nearbyRadius) passes.Add(new KeyValuePair<string, float>("wide", wideRadius));
                }
                if (fallback == "anywhere") passes.Add(new KeyValuePair<string, float>("anywhere", float.PositiveInfinity));
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
                if (!done) Fail(it, failures, "collision", "lot overlaps a " + lastHit + " (tried " + prefabs.Count + " prefab(s), slides up to ±" + maxSlide + " m"
                    + (passes.Count > 0 ? ", free land " + (fallback == "anywhere" ? "anywhere in the crop" : "within " + passes[passes.Count - 1].Value + " m") : ", fallback " + fallback) + ")");
            }

            // Create the definitions in batches, a few frames apart.
            var ready = items.FindAll(i => i.Status == "ready");
            int batches = 0, frames = 0, subNetCourses = 0;
            // A building definition alone gets no sub-nets (depot / station tracks, internal paths, parking roads): the
            // object tool, ZoneSpawnSystem and BuildingConstructionSystem write them as separate Permanent course
            // definitions owned by the building definition. Same here (run option subNets, default on).
            bool subNets = P.Bool(a, "subNets", true);
            var rnd = new Unity.Mathematics.Random(0x5eed1234u);
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
            for (int f = 0; f < 30; f++) { yield return null; frames++; }

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
                if (subNets)
                {
                    it.SubNetsExpected = ExpectedSubNets(em, it.PrefabEntity);
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
                    + (it.Moved >= 0 ? "; moved " + it.Moved.ToString("F0") + " m to free land (" + it.FallbackPass + " pass)" + (it.MovedToOtherRoad ? " on another road" : " on the same road") : "");
            }

            var list = new List<object>();
            foreach (var it in items)
                list.Add(new Dictionary<string, object>
                {
                    { "id", it.Id }, { "prefab", it.Prefab }, { "cs1Info", it.Cs1Info }, { "status", it.Status }, { "reason", it.Reason },
                    { "x", R(it.Position.x) }, { "y", R(it.Position.y) }, { "z", R(it.Position.z) },
                    { "yaw", R(math.degrees(math.atan2(it.Forward.x, it.Forward.y))) },
                    { "lot", new List<object> { it.Lot.x, it.Lot.y } }, { "snapDistance", R(it.SnapDistance) },
                    { "alternative", it.Alternative }, { "slide", R(it.Slide) }, { "moved", R(it.Moved) }, { "distance", R(it.Distance) }, { "fallbackPass", it.FallbackPass }, { "movedToOtherRoad", it.MovedToOtherRoad },
                    { "entity", it.Placed == Entity.Null ? null : (object)it.Placed.Index }, { "subNetsExpected", it.SubNetsExpected }, { "subNetsBuilt", it.SubNetsBuilt },
                });
            var report = new Dictionary<string, object>
            {
                { "items", items.Count }, { "placed", placed }, { "alreadyPlaced", alreadyPlaced }, { "usedAlternative", usedAlternative }, { "slid", slid }, { "movedToFreeLand", moved }, { "movedByPass", ToObj(movedByPass) }, { "distanceStats", DistanceStats(items) }, { "fallback", defaultFallback }, { "nearbyRadius", nearbyRadius }, { "wideRadius", wideRadius }, { "connected", connected }, { "connectedToSnappedRoad", connectedToSnapEdge },
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
            yield return reply;
        }

        static void Fail(Item it, Dictionary<string, int> failures, string kind, string reason)
        {
            it.Status = kind;
            it.Reason = reason;
            int n;
            failures.TryGetValue(kind, out n);
            failures[kind] = n + 1;
        }

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
                    if (LotHitsRoad(pos2, forward, lot, r)) { hit = "road"; break; }
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
                    Cs1Info = P.Str(d, "cs1Info", null),
                    X = P.Num(d, "x", 0), Z = P.Num(d, "z", 0), Score = P.Num(d, "score", 0),
                    Front = Pair(d, "front"), Facing = Pair(d, "facing"),
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
                    list.Add(new RoadEdge { Entity = e, Curve = c, HalfWidth = hw, Flags = cf, Bounds = new float4(mn.x, mn.y, mx.x, mx.y) });
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
        /// IsFirst/IsLast | DisableMerge, OwnerDefinition = (building prefab, position, rotation). Upgrade-only sub-nets
        /// (m_Upgrades set) are skipped: the placed building has no upgrades. Returns the number of courses.
        /// </summary>
        static int ExpectedSubNets(EntityManager em, Entity prefab)
        {
            if (!em.HasBuffer<Game.Prefabs.SubNet>(prefab)) return 0;
            var buf = em.GetBuffer<Game.Prefabs.SubNet>(prefab, true);
            int n = 0;
            for (int i = 0; i < buf.Length; i++) if (buf[i].m_Prefab != Entity.Null && buf[i].m_Upgrades.Equals(default(CompositionFlags))) n++;
            return n;
        }

        static int CreateSubNets(EntityManager em, Entity buildingPrefab, float3 pos, quaternion rot, ref Unity.Mathematics.Random rnd)
        {
            if (!em.HasBuffer<Game.Prefabs.SubNet>(buildingPrefab)) return 0;
            var buf = em.GetBuffer<Game.Prefabs.SubNet>(buildingPrefab, true);
            var subNets = new List<Game.Prefabs.SubNet>(buf.Length);
            for (int i = 0; i < buf.Length; i++) subNets.Add(buf[i]); // copy: creating entities invalidates the buffer
            int n = 0;
            foreach (var sn in subNets)
            {
                if (sn.m_Prefab == Entity.Null || !sn.m_Upgrades.Equals(default(CompositionFlags))) continue;
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
                em.AddComponentData(e, new CreationDefinition { m_Prefab = sn.m_Prefab, m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.Permanent });
                em.AddComponentData(e, new OwnerDefinition { m_Prefab = buildingPrefab, m_Position = pos, m_Rotation = rot });
                em.AddComponentData(e, new NetCourse { m_Curve = curve, m_StartPosition = start, m_EndPosition = end, m_Length = MathUtils.Length(curve), m_FixedIndex = -1 });
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
