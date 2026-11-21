// Trees, props, districts and surfaces (exec ops "list", "objects", "areas").
//
// objects: one CreationDefinition (Permanent) + ObjectDefinition + Updated + Deleted per tree / prop, the same recipe as
//   buildings; ObjectDefinition.m_Age sets a tree's growth (ObjectUtils.InitializeTreeState: < 0.1 child, < 0.25 teen,
//   < 0.6 adult, …). Objects on a road surface or inside a building lot are skipped, so trees never block the imported
//   city. Created in batches (default 2000 per frame).
// areas: one CreationDefinition (Permanent) + a Node buffer (the polygon, elevation float.MinValue = on the terrain) +
//   Updated per area, as AreaToolSystem / the zone spawner do. Districts then get their name (what NameSystem.SetCustomName
//   does) and policies (a Game.Policies.Modify event, what PoliciesUISystem.SetPolicy does) once they exist; both are done
//   with the EntityManager because those methods use end-frame command buffers that exec code may not create.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Colossal.Mathematics;
using Game.Areas;
using Game.Buildings;
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
    internal static class Objects
    {
        // ---------------------------------------------------------------- op: list

        /// <summary>Prefabs whose class name matches `type` (regex), with UI group and component names.</summary>
        public static object List(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var re = new Regex(P.Str(a, "type", "AreaPrefab|PolicyPrefab"));
            var nameRe = new Regex(P.Str(a, "name", "."), RegexOptions.IgnoreCase);
            var list = new List<object>();
            foreach (var p in Catalogue.AllPrefabs(ps))
            {
                if (p == null) continue;
                bool match = false;
                for (var t = p.GetType(); t != null && !match; t = t.BaseType) match = re.IsMatch(t.Name);
                if (!match || !nameRe.IsMatch(p.name)) continue;
                var comps = new List<object>();
                string group = null;
                if (p.components != null)
                    foreach (var c in p.components)
                    {
                        if (c == null) continue;
                        comps.Add(c.GetType().Name);
                        var ui = c as UIObject;
                        if (ui != null && ui.m_Group != null) group = ui.m_Group.name;
                    }
                list.Add(new Dictionary<string, object> { { "name", p.name }, { "type", p.GetType().Name }, { "uiGroup", group }, { "components", comps } });
            }
            return new Dictionary<string, object> { { "count", list.Count }, { "prefabs", list } };
        }

        // ---------------------------------------------------------------- op: objects

        public static object Place(Dictionary<string, object> a) { return PlaceRoutine(a); }

        static IEnumerator PlaceRoutine(Dictionary<string, object> a)
        {
            var sw = Stopwatch.StartNew();
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var heights = world.GetOrCreateSystemManaged<TerrainSystem>().GetHeightData(true);
            float ox = (float)P.Num(a, "offsetX", 0), oz = (float)P.Num(a, "offsetZ", 0);
            int batch = (int)P.Num(a, "batchSize", 2000);
            float roadClearance = (float)P.Num(a, "roadClearance", 1.5f);
            // A tree on a (wider) CS2 road surface is pushed out perpendicular to that road, to just past its edge, if
            // that is at most roadsideMove metres and the new spot is free (no road, no lot). Otherwise it is dropped.
            float roadsideMove = (float)P.Num(a, "roadsideMove", 6f);
            int limit = (int)P.Num(a, "limit", int.MaxValue);
            var root = Load(P.Required(a, "plan"));
            var section = Section(root, "objects");

            // Prefab table.
            var names = section["prefabs"] as List<object>;
            var prefabEntities = new Entity[names.Count];
            var missing = new List<object>();
            for (int i = 0; i < names.Count; i++)
            {
                PrefabBase prefab;
                Entity e;
                var n = (string)names[i];
                if (ps.TryGetPrefab(new PrefabID("StaticObjectPrefab", n), out prefab) && ps.TryGetEntity(prefab, out e)) prefabEntities[i] = e;
                else missing.Add(n);
            }

            // Obstacles: road surfaces (grid of edges) and building lots.
            var roads = Placement.RoadEdges(em);
            var grid = new Placement.RoadGrid(roads);
            var lots = Placement.ExistingLots(em);
            var lotGrid = new Dictionary<long, List<float2[]>>();
            foreach (var l in lots)
            {
                var mn = math.min(math.min(l[0], l[1]), math.min(l[2], l[3]));
                var mx = math.max(math.max(l[0], l[1]), math.max(l[2], l[3]));
                for (int x = (int)math.floor(mn.x / 64); x <= (int)math.floor(mx.x / 64); x++)
                    for (int z = (int)math.floor(mn.y / 64); z <= (int)math.floor(mx.y / 64); z++)
                    {
                        long k = ((long)x << 32) ^ (uint)z;
                        List<float2[]> lst;
                        if (!lotGrid.TryGetValue(k, out lst)) lotGrid[k] = lst = new List<float2[]>();
                        lst.Add(l);
                    }
            }

            var rows = section["items"] as List<object>;
            int created = 0, onRoad = 0, inLot = 0, noPrefab = 0, frames = 0, moved = 0;
            double movedSum = 0;
            var perPrefab = new int[names.Count];
            double checkMs = 0, createMs = 0;
            var rnd = new Unity.Mathematics.Random(0x7EE5u);
            int pending = 0;
            for (int i = 0; i < rows.Count && created < limit; i++)
            {
                var r = rows[i] as List<object>;
                int pi = Convert.ToInt32(r[0]);
                var p = new float2(Convert.ToSingle(r[1]) + ox, Convert.ToSingle(r[2]) + oz);
                float angle = r.Count > 3 ? Convert.ToSingle(r[3]) : 0f;
                float age = r.Count > 4 ? Convert.ToSingle(r[4]) : 0.45f;
                if (pi < 0 || pi >= prefabEntities.Length || prefabEntities[pi] == Entity.Null) { noPrefab++; continue; }

                var t0 = sw.Elapsed.TotalMilliseconds;
                bool blocked = false;
                Placement.RoadEdge hit;
                float hitT;
                if (OnRoad(grid, p, roadClearance, out hit, out hitT))
                {
                    var np = roadsideMove > 0 ? PushOff(hit, hitT, p, roadClearance + 0.25f) : p;
                    float dist = math.distance(np, p);
                    Placement.RoadEdge h2;
                    float t2;
                    if (roadsideMove > 0 && dist <= roadsideMove && !OnRoad(grid, np, roadClearance, out h2, out t2) && !InLot(lotGrid, np)) { p = np; moved++; movedSum += dist; }
                    else { blocked = true; onRoad++; }
                }
                if (!blocked && InLot(lotGrid, p)) { blocked = true; inLot++; }
                checkMs += sw.Elapsed.TotalMilliseconds - t0;
                if (blocked) continue;

                t0 = sw.Elapsed.TotalMilliseconds;
                float y = TerrainUtils.SampleHeight(ref heights, new float3(p.x, 0, p.y));
                var pos = new float3(p.x, y, p.y);
                var rot = quaternion.RotateY(math.radians(angle));
                var d = em.CreateEntity();
                em.AddComponentData(d, new CreationDefinition { m_Prefab = prefabEntities[pi], m_RandomSeed = rnd.NextInt(), m_Flags = CreationFlags.Permanent });
                em.AddComponentData(d, new ObjectDefinition
                {
                    m_Position = pos, m_Rotation = rot, m_LocalPosition = pos, m_LocalRotation = rot,
                    m_Scale = new float3(1f), m_Intensity = 1f, m_Age = age, m_ParentMesh = -1, m_GroupIndex = -1, m_Probability = 100, m_PrefabSubIndex = -1,
                });
                em.AddComponent<Updated>(d);
                em.AddComponent<Deleted>(d);
                createMs += sw.Elapsed.TotalMilliseconds - t0;
                created++;
                perPrefab[pi]++;
                if (++pending >= batch) { pending = 0; yield return null; frames++; }
            }
            for (int f = 0; f < 10; f++) { yield return null; frames++; }

            // Verify: count real objects of these prefabs (Static objects with PrefabRef in the table).
            var wanted = new Dictionary<Entity, int>();
            for (int i = 0; i < prefabEntities.Length; i++) if (prefabEntities[i] != Entity.Null) wanted[prefabEntities[i]] = i;
            var found = new int[names.Count];
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Objects.Static>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Building>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    int idx;
                    if (wanted.TryGetValue(em.GetComponentData<PrefabRef>(e).m_Prefab, out idx)) found[idx]++;
                }

            var byPrefab = new Dictionary<string, object>();
            for (int i = 0; i < names.Count; i++)
                if (perPrefab[i] > 0 || found[i] > 0)
                    byPrefab[(string)names[i]] = new Dictionary<string, object> { { "created", perPrefab[i] }, { "inCity", found[i] } };
            yield return new Dictionary<string, object>
            {
                { "items", rows.Count }, { "created", created }, { "movedOffRoad", moved }, { "movedMeanM", moved > 0 ? Math.Round(movedSum / moved, 2) : 0 }, { "roadsideMove", roadsideMove }, { "skippedOnRoad", onRoad }, { "skippedInBuildingLot", inLot }, { "noPrefab", noPrefab },
                { "missingPrefabs", missing }, { "byPrefab", byPrefab }, { "frames", frames }, { "batchSize", batch },
                { "checkMs", Math.Round(checkMs) }, { "createMs", Math.Round(createMs) }, { "seconds", Math.Round(sw.Elapsed.TotalSeconds, 2) },
            };
        }

        static bool OnRoad(Placement.RoadGrid grid, float2 p, float clearance, out Placement.RoadEdge hit, out float hitT)
        {
            hit = null;
            hitT = 0;
            float best = float.MaxValue;
            foreach (var e in grid.Query(p - 40f, p + 40f))
            {
                float t;
                float d = MathUtils.Distance(e.Curve.xz, p, out t) - (e.HalfWidth + clearance);
                if (d < 0 && d < best) { best = d; hit = e; hitT = t; }
            }
            return hit != null;
        }

        /// <summary>p moved along the perpendicular from the road's closest point to halfWidth + margin from its centre line.</summary>
        static float2 PushOff(Placement.RoadEdge e, float t, float2 p, float margin)
        {
            var c = MathUtils.Position(e.Curve.xz, t);
            var n = p - c;
            if (math.lengthsq(n) < 1e-4f)
            {
                var tan = MathUtils.Tangent(e.Curve.xz, t);
                n = new float2(-tan.y, tan.x);
            }
            return c + math.normalizesafe(n) * (e.HalfWidth + margin);
        }

        static bool InLot(Dictionary<long, List<float2[]>> lotGrid, float2 p)
        {
            List<float2[]> lst;
            if (lotGrid.TryGetValue(((long)(int)math.floor(p.x / 64) << 32) ^ (uint)(int)math.floor(p.y / 64), out lst))
                foreach (var l in lst) if (Inside(l, p)) return true;
            return false;
        }

        static bool Inside(float2[] quad, float2 p)
        {
            bool pos = false, neg = false;
            for (int i = 0; i < 4; i++)
            {
                var a = quad[i];
                var b = quad[(i + 1) % 4];
                float c = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                if (c > 0) pos = true; else if (c < 0) neg = true;
            }
            return !(pos && neg);
        }

        // ---------------------------------------------------------------- op: areas

        public static object Areas(Dictionary<string, object> a) { return AreasRoutine(a); }

        static IEnumerator AreasRoutine(Dictionary<string, object> a)
        {
            var sw = Stopwatch.StartNew();
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            float ox = (float)P.Num(a, "offsetX", 0), oz = (float)P.Num(a, "offsetZ", 0);
            var root = Load(P.Required(a, "plan"));
            var sectionName = P.Str(a, "section", "districts");
            var section = Section(root, sectionName);
            var items = WebAreaItems(section["items"] as List<object> ?? new List<object>(), sectionName);
            var results = new List<object>();
            var before = AreaEntities(em);
            var created = new List<KeyValuePair<Dictionary<string, object>, float2>>();
            var createdRes = new List<Dictionary<string, object>>();
            int failed = 0;

            foreach (var o in items)
            {
                var it = (Dictionary<string, object>)o;
                var prefabName = P.Str(it, "prefab", null);
                var type = P.Str(it, "type", "DistrictPrefab");
                PrefabBase prefab;
                Entity pe;
                var res = new Dictionary<string, object> { { "name", P.Str(it, "name", null) }, { "prefab", prefabName } };
                results.Add(res);
                if (prefabName == null || !ps.TryGetPrefab(new PrefabID(type, prefabName), out prefab) || !ps.TryGetEntity(prefab, out pe))
                {
                    res["status"] = "prefabMissing";
                    failed++;
                    continue;
                }
                var poly = it["polygon"] as List<object>;
                if (poly == null || poly.Count < 3) { res["status"] = "badPolygon"; failed++; continue; }
                var e = em.CreateEntity();
                em.AddComponentData(e, new CreationDefinition { m_Prefab = pe, m_RandomSeed = 0, m_Flags = CreationFlags.Permanent });
                var nodes = em.AddBuffer<Game.Areas.Node>(e);
                float2 sum = 0;
                foreach (var pt in poly)
                {
                    var l = (List<object>)pt;
                    var p = new float2(Convert.ToSingle(l[0]) + ox, Convert.ToSingle(l[1]) + oz);
                    sum += p;
                    nodes.Add(new Game.Areas.Node(new float3(p.x, 0, p.y), float.MinValue));
                }
                em.AddComponent<Updated>(e);
                res["nodes"] = poly.Count;
                res["status"] = "created";
                created.Add(new KeyValuePair<Dictionary<string, object>, float2>(it, sum / poly.Count));
                createdRes.Add(res);
            }
            for (int f = 0; f < 10; f++) yield return null;

            // Find the new areas, name them, set policies.
            var after = AreaEntities(em);
            var names = world.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            int found = 0, named = 0, policiesSet = 0;
            var fresh = new List<Entity>();
            foreach (var e in after) if (!before.Contains(e)) fresh.Add(e);
            for (int i = 0; i < created.Count; i++)
            {
                var it = created[i].Key;
                var res = createdRes[i];
                Entity best = Entity.Null;
                float bestD = float.MaxValue;
                foreach (var e in fresh)
                {
                    var buf = em.GetBuffer<Game.Areas.Node>(e, true);
                    float2 c = 0;
                    for (int k = 0; k < buf.Length; k++) c += buf[k].m_Position.xz;
                    float d = math.distance(c / math.max(1, buf.Length), created[i].Value);
                    if (d < bestD) { bestD = d; best = e; }
                }
                if (best == Entity.Null || bestD > 50f) { res["status"] = "notCreated"; failed++; continue; }
                fresh.Remove(best);
                found++;
                res["status"] = "placed";
                res["entity"] = best.Index;
                var name = P.Str(it, "name", null);
                if (!string.IsNullOrEmpty(name) && SetName(em, names, best, name)) named++;
                var pol = it.ContainsKey("policies") ? it["policies"] as List<object> : null;
                var set = new List<object>();
                if (pol != null)
                    foreach (var pn in pol)
                    {
                        PrefabBase pp;
                        Entity pEnt;
                        if ((ps.TryGetPrefab(new PrefabID("PolicyTogglePrefab", (string)pn), out pp) || ps.TryGetPrefab(new PrefabID("PolicySliderPrefab", (string)pn), out pp)) && ps.TryGetEntity(pp, out pEnt))
                        {
                            // What PoliciesUISystem.SetPolicy does, without its end-frame command buffer: a Modify event.
                            var ev = em.CreateEntity(ComponentType.ReadWrite<Game.Common.Event>(), ComponentType.ReadWrite<Game.Policies.Modify>());
                            em.SetComponentData(ev, new Game.Policies.Modify(best, pEnt, true, 0f));
                            set.Add(pn);
                            policiesSet++;
                        }
                    }
                res["policiesSet"] = set;
            }
            // City-wide policies (web plan.districts.cityPolicies), on the city entity, as PoliciesUISystem.SetCityPolicy does.
            var citySet = new List<object>();
            var cityPol = section.ContainsKey("cityPolicies") ? section["cityPolicies"] as List<object> : null;
            if (cityPol != null && cityPol.Count > 0)
            {
                var city = world.GetOrCreateSystemManaged<Game.Simulation.CitySystem>().City;
                if (city != Entity.Null)
                    foreach (var pn in cityPol)
                    {
                        PrefabBase pp;
                        Entity pEnt;
                        if ((ps.TryGetPrefab(new PrefabID("PolicyTogglePrefab", (string)pn), out pp) || ps.TryGetPrefab(new PrefabID("PolicySliderPrefab", (string)pn), out pp)) && ps.TryGetEntity(pp, out pEnt))
                        {
                            var ev = em.CreateEntity(ComponentType.ReadWrite<Game.Common.Event>(), ComponentType.ReadWrite<Game.Policies.Modify>());
                            em.SetComponentData(ev, new Game.Policies.Modify(city, pEnt, true, 0f));
                            citySet.Add(pn);
                        }
                    }
            }
            for (int f = 0; f < 5; f++) yield return null;
            yield return new Dictionary<string, object>
            {
                { "items", items.Count }, { "placed", found }, { "named", named }, { "policiesSet", policiesSet }, { "failed", failed }, { "cityPoliciesSet", citySet },
                { "seconds", Math.Round(sw.Elapsed.TotalSeconds, 2) }, { "results", results },
            };
        }

        /// <summary>
        /// Accepts both area item shapes: this module's ({prefab, type, polygon}) and the web tool's plan.districts
        /// ({name, outlines: [[x,z]…][], policies}, no prefab). A web item becomes one area per outline, all with its
        /// name and policies; districts default to CS2's only district prefab, "District Area".
        /// </summary>
        static List<object> WebAreaItems(List<object> items, string section)
        {
            var outList = new List<object>();
            foreach (var o in items)
            {
                var it = o as Dictionary<string, object>;
                if (it == null) continue;
                var outlines = it.ContainsKey("outlines") ? it["outlines"] as List<object> : null;
                if (it.ContainsKey("polygon") || outlines == null) { outList.Add(WithDefaultPrefab(it, section)); continue; }
                foreach (var ol in outlines)
                {
                    var copy = new Dictionary<string, object>(it);
                    copy.Remove("outlines");
                    copy["polygon"] = ol;
                    outList.Add(WithDefaultPrefab(copy, section));
                }
            }
            return outList;
        }

        static Dictionary<string, object> WithDefaultPrefab(Dictionary<string, object> it, string section)
        {
            if (section == "districts" && !it.ContainsKey("prefab")) { it = new Dictionary<string, object>(it); it["prefab"] = "District Area"; it["type"] = "DistrictPrefab"; }
            return it;
        }

        /// <summary>
        /// What NameSystem.SetCustomName does (m_Names[entity] = name, then CustomName + BatchesUpdated), without its end-frame
        /// command buffer, which exec code may not create ("Trying to create EntityCommandBuffer when it's not allowed").
        /// </summary>
        static bool SetName(EntityManager em, Game.UI.NameSystem names, Entity e, string name)
        {
            var f = typeof(Game.UI.NameSystem).GetField("m_Names", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var dict = f != null ? f.GetValue(names) as Dictionary<Entity, string> : null;
            if (dict == null) return false;
            dict[e] = name;
            if (!em.HasComponent<Game.UI.CustomName>(e)) em.AddComponent<Game.UI.CustomName>(e);
            if (!em.HasComponent<BatchesUpdated>(e)) em.AddComponent<BatchesUpdated>(e);
            return true;
        }

        static HashSet<Entity> AreaEntities(EntityManager em)
        {
            var set = new HashSet<Entity>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Area>(), ComponentType.ReadOnly<Game.Areas.Node>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es) set.Add(e);
            return set;
        }

        // ---------------------------------------------------------------- plan

        static Dictionary<string, object> Load(string path)
        {
            path = Environment.ExpandEnvironmentVariables(path);
            var root = MiniJson.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("plan is not a JSON object");
            return root;
        }

        static Dictionary<string, object> Section(Dictionary<string, object> root, string name)
        {
            object v;
            if (root.TryGetValue(name, out v) && v is Dictionary<string, object>) return (Dictionary<string, object>)v;
            throw new InvalidDataException("no '" + name + "' section in the plan");
        }
    }
}
