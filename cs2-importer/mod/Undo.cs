// "Remove imported": what an import created, recorded per city and deleted on request.
//
// The import (and every step in it, including other workers' code) runs between two snapshots of the city's top-level
// entities: roads (edges), their nodes, buildings, static objects (trees, props, stops), areas (districts, surfaces) and
// transit routes. The difference is what the import created. Entity ids don't survive a save/load, so each item is stored
// by kind, prefab and position (edges: the curve's middle; routes: the first waypoint) in
// %LOCALAPPDATA%\cs1-to-cs2\import-reports\undo-<city>.json, and found again by those. Deletion uses the game's own
// patterns: Deleted on the entity (+ its sub-objects), then the nodes left without edges (the importer's clear), routes as
// the transport overview's DeleteLine does (Deleted on the route).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Colossal.Mathematics;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using GameBridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2ImporterMod
{
    public static class Undo
    {
        public static readonly string[] Kinds = { "route", "area", "object", "building", "edge", "node" };

        static World W { get { return World.DefaultGameObjectInjectionWorld; } }
        static EntityManager Em { get { return W.EntityManager; } }

        static EntityQueryDesc Desc(string kind)
        {
            var none = new List<ComponentType> { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() };
            switch (kind)
            {
                case "edge": none.Add(ComponentType.ReadOnly<Owner>()); return new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Game.Net.Edge>() }, None = none.ToArray() };
                case "node": none.Add(ComponentType.ReadOnly<Owner>()); return new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Game.Net.Node>() }, None = none.ToArray() };
                case "building": none.Add(ComponentType.ReadOnly<Owner>()); return new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>() }, None = none.ToArray() };
                case "object":
                    none.Add(ComponentType.ReadOnly<Owner>()); none.Add(ComponentType.ReadOnly<Game.Buildings.Building>());
                    return new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Game.Objects.Object>(), ComponentType.ReadOnly<Game.Objects.Static>(), ComponentType.ReadOnly<Game.Objects.Transform>() }, None = none.ToArray() };
                case "area": none.Add(ComponentType.ReadOnly<Owner>()); return new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Game.Areas.Area>() }, None = none.ToArray() };
                case "route": return new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Game.Routes.Route>() }, None = none.ToArray() };
            }
            throw new ArgumentException(kind);
        }

        static NativeArray<Entity> Query(string kind)
        {
            using (var q = Em.CreateEntityQuery(Desc(kind))) return q.ToEntityArray(Allocator.Temp);
        }

        // ---------------------------------------------------------------- snapshot + record

        public sealed class Snapshot
        {
            public readonly Dictionary<string, HashSet<Entity>> Sets = new Dictionary<string, HashSet<Entity>>();
        }

        public static Snapshot Take()
        {
            var s = new Snapshot();
            foreach (var k in Kinds)
            {
                var a = Query(k);
                s.Sets[k] = new HashSet<Entity>(a);
                a.Dispose();
            }
            return s;
        }

        /// <summary>Records the entities created since `before` for this city; returns their count per kind.</summary>
        public static Dictionary<string, object> Record(Snapshot before, string package)
        {
            var items = new List<object>();
            var counts = new Dictionary<string, object>();
            foreach (var k in Kinds)
            {
                var a = Query(k);
                int n = 0;
                foreach (var e in a)
                {
                    if (before.Sets[k].Contains(e)) continue;
                    var it = Describe(k, e);
                    if (it == null) continue;
                    items.Add(it);
                    n++;
                }
                a.Dispose();
                counts[k] = n;
            }
            var file = ManifestFile();
            var m = Load(file);
            var imports = (List<object>)m["imports"];
            imports.Add(new Dictionary<string, object> { { "at", DateTime.Now.ToString("s") }, { "package", package }, { "counts", counts }, { "items", items } });
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, MiniJson.Write(m), new UTF8Encoding(false));
            counts["total"] = items.Count;
            counts["file"] = file;
            return counts;
        }

        static Dictionary<string, object> Describe(string kind, Entity e)
        {
            if (!Em.HasComponent<PrefabRef>(e)) return null;
            float3 pos;
            if (!Position(kind, e, out pos)) return null;
            var d = new Dictionary<string, object> { { "k", kind }, { "p", PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab) }, { "x", R(pos.x) }, { "y", R(pos.y) }, { "z", R(pos.z) } };
            if (kind == "route" && Em.HasComponent<Game.Routes.RouteNumber>(e)) d["n"] = Em.GetComponentData<Game.Routes.RouteNumber>(e).m_Number;
            return d;
        }

        static bool Position(string kind, Entity e, out float3 pos)
        {
            pos = default;
            switch (kind)
            {
                case "edge":
                    if (!Em.HasComponent<Game.Net.Curve>(e)) return false;
                    pos = MathUtils.Position(Em.GetComponentData<Game.Net.Curve>(e).m_Bezier, 0.5f);
                    return true;
                case "node": pos = Em.GetComponentData<Game.Net.Node>(e).m_Position; return true;
                case "building":
                case "object":
                    pos = Em.GetComponentData<Game.Objects.Transform>(e).m_Position; return true;
                case "area":
                {
                    if (!Em.HasBuffer<Game.Areas.Node>(e)) return false;
                    var b = Em.GetBuffer<Game.Areas.Node>(e, true);
                    if (b.Length == 0) return false;
                    pos = b[0].m_Position;
                    return true;
                }
                case "route":
                {
                    if (!Em.HasBuffer<Game.Routes.RouteWaypoint>(e)) return false;
                    var b = Em.GetBuffer<Game.Routes.RouteWaypoint>(e, true);
                    if (b.Length == 0 || !Em.HasComponent<Game.Routes.Position>(b[0].m_Waypoint)) return false;
                    pos = Em.GetComponentData<Game.Routes.Position>(b[0].m_Waypoint).m_Position;
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- manifest

        public static string CityName()
        {
            try
            {
                var n = W.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().cityName;
                return string.IsNullOrEmpty(n) ? "unnamed" : n;
            }
            catch { return "unnamed"; }
        }

        public static string ManifestFile()
        {
            var n = CityName();
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return Path.Combine(Packages.LocalRoot, "import-reports", "undo-" + n + ".json");
        }

        static Dictionary<string, object> Load(string file)
        {
            if (File.Exists(file))
                try
                {
                    var m = MiniJson.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
                    if (m != null && m.ContainsKey("imports")) return m;
                }
                catch (Exception e) { Mod.Log.Warn("undo manifest unreadable, starting a new one: " + e.Message); }
            return new Dictionary<string, object> { { "format", "cs1conv-undo" }, { "city", CityName() }, { "imports", new List<object>() } };
        }

        public static int Recorded()
        {
            var f = ManifestFile();
            if (!File.Exists(f)) return 0;
            return ((List<object>)Load(f)["imports"]).Sum(i => ((List<object>)((Dictionary<string, object>)i)["items"]).Count);
        }

        // ---------------------------------------------------------------- remove

        /// <summary>
        /// Deletes every recorded item that can still be found, most dependent first (routes, areas, objects, buildings,
        /// roads, then the nodes left without roads). Items are batched per frame. The manifest is deleted at the end;
        /// on a cancel it keeps what is left.
        /// </summary>
        public static IEnumerator Remove(Func<bool> cancelled, Action<string> progress)
        {
            var file = ManifestFile();
            var m = Load(file);
            var all = ((List<object>)m["imports"]).SelectMany(i => (List<object>)((Dictionary<string, object>)i)["items"]).Cast<Dictionary<string, object>>().ToList();
            var removed = new Dictionary<string, int>();
            int gone = 0;
            foreach (var k in Kinds) removed[k] = 0;
            if (all.Count == 0) { yield return new Dictionary<string, object> { { "nothing", true } }; yield break; }

            var keptNodes = new List<Entity>();
            foreach (var kind in Kinds)
            {
                var mine = all.Where(i => (string)i["k"] == kind).ToList();
                if (mine.Count == 0) continue;
                progress(T.S("undo.kind." + kind));
                var index = Index(kind);
                var targets = new List<Entity>();
                foreach (var it in mine)
                {
                    var e = Find(index, it);
                    if (e == Entity.Null) gone++; else targets.Add(e);
                }
                for (int i = 0; i < targets.Count; i += 1000)
                {
                    if (cancelled()) { yield return new Dictionary<string, object> { { "cancelled", true }, { "removed", Counts(removed) } }; yield break; }
                    foreach (var e in targets.Skip(i).Take(1000))
                    {
                        if (!IsReal(e)) continue;
                        if (kind == "node")
                        {
                            // a node another (not imported) road still uses stays, and is rebuilt without our roads
                            if (RealEdges(e) > 0) { keptNodes.Add(e); continue; }
                        }
                        Delete(e, kind != "route");
                        removed[kind]++;
                    }
                    yield return null;
                }
                for (int f = 0; f < 5; f++) yield return null; // the game applies the deletions before the next kind
            }
            foreach (var n in keptNodes)
                if (IsReal(n))
                {
                    Em.AddComponent<Updated>(n);
                    foreach (var e in EdgesOf(n)) Em.AddComponent<Updated>(e);
                }
            for (int f = 0; f < 30; f++) yield return null;
            try { File.Delete(file); } catch (Exception e) { Mod.Log.Warn("undo manifest not deleted: " + e.Message); }
            var r = new Dictionary<string, object> { { "removed", Counts(removed) }, { "gone", gone }, { "keptNodes", keptNodes.Count } };
            yield return r;
        }

        static Dictionary<string, object> Counts(Dictionary<string, int> d) { return d.ToDictionary(kv => kv.Key, kv => (object)kv.Value); }

        /// <summary>Current entities of a kind by prefab name, with their positions.</summary>
        static Dictionary<string, List<KeyValuePair<Entity, float3>>> Index(string kind)
        {
            var idx = new Dictionary<string, List<KeyValuePair<Entity, float3>>>();
            var a = Query(kind);
            foreach (var e in a)
            {
                float3 p;
                if (!Em.HasComponent<PrefabRef>(e) || !Position(kind, e, out p)) continue;
                var name = PrefabName(Em.GetComponentData<PrefabRef>(e).m_Prefab);
                List<KeyValuePair<Entity, float3>> l;
                if (!idx.TryGetValue(name, out l)) idx[name] = l = new List<KeyValuePair<Entity, float3>>();
                l.Add(new KeyValuePair<Entity, float3>(e, p));
            }
            a.Dispose();
            return idx;
        }

        static Entity Find(Dictionary<string, List<KeyValuePair<Entity, float3>>> idx, Dictionary<string, object> it)
        {
            List<KeyValuePair<Entity, float3>> l;
            if (!idx.TryGetValue((string)it["p"], out l)) return Entity.Null;
            var p = new float3((float)P.Num(it, "x", 0), (float)P.Num(it, "y", 0), (float)P.Num(it, "z", 0));
            int best = -1;
            float bd = 0.25f;
            for (int i = 0; i < l.Count; i++)
            {
                float d = math.distance(l[i].Value, p);
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return Entity.Null;
            var e = l[best].Key;
            l.RemoveAt(best); // two identical items (e.g. stacked props) each take their own entity
            return e;
        }

        static bool IsReal(Entity e) { return e != Entity.Null && Em.Exists(e) && !Em.HasComponent<Temp>(e) && !Em.HasComponent<Deleted>(e); }

        static int RealEdges(Entity node)
        {
            int n = 0;
            foreach (var e in EdgesOf(node)) if (IsReal(e)) n++;
            return n;
        }

        static List<Entity> EdgesOf(Entity node)
        {
            var r = new List<Entity>();
            if (!Em.HasBuffer<Game.Net.ConnectedEdge>(node)) return r;
            var b = Em.GetBuffer<Game.Net.ConnectedEdge>(node, true);
            for (int i = 0; i < b.Length; i++) r.Add(b[i].m_Edge);
            return r;
        }

        /// <summary>Deleted on the entity and (for nets and objects) its sub-objects, as the importer's clear does.</summary>
        static void Delete(Entity e, bool withSubObjects)
        {
            var subs = new List<Entity>();
            if (withSubObjects && Em.HasBuffer<Game.Objects.SubObject>(e))
            {
                var buf = Em.GetBuffer<Game.Objects.SubObject>(e, true);
                for (int i = 0; i < buf.Length; i++) subs.Add(buf[i].m_SubObject);
            }
            Em.AddComponent<Deleted>(e);
            foreach (var s in subs) if (IsReal(s)) Em.AddComponent<Deleted>(s);
        }

        static string PrefabName(Entity prefab)
        {
            try { return W.GetOrCreateSystemManaged<PrefabSystem>().GetPrefabName(prefab); } catch { return "?"; }
        }

        static double R(float v) { return Math.Round(v, 3); }
    }
}
