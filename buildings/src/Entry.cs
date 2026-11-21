// exec entry point of the CS2 buildings library (tools/PROTOCOL.md "exec"). One op per call, on args.op:
//
//   catalogue {path, objects=true}           [phase main]  dump every building / static-object prefab to {path} (JSON)
//   place     {plan, offsetX=0, offsetZ=0, …} [phase tool, pump tool]  place the plan's buildings, then verify;
//                                                           returns the report (and writes it to {report} if given)
//   inspect   {x, z, r=60}                    [phase main]  real buildings near a point (prefab, lot, road edge)
//   list      {type=regex, name=regex}         [phase main]  prefabs by class name (areas, policies, …)
//   objects   {plan, offsetX, offsetZ, batchSize=2000} [phase tool, pump tool]  trees / props of plan.objects
//   areas     {plan, section=districts|surfaces, offsetX, offsetZ} [phase tool, pump tool]  districts (name, policies) or surfaces
//   prefab    {names: "A,B"}                  [phase main]  a building prefab's sub-nets, sub-objects and placement flags (read only)
//
// Statics never survive between calls (the bridge loads a fresh copy of the DLL every time).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Prefabs;
using GameBridge;
using Unity.Entities;

namespace Cs2Buildings
{
    public static class Entry
    {
        public static object Run(Dictionary<string, object> args)
        {
            string op = P.Str(args, "op", "catalogue");
            switch (op)
            {
                case "catalogue": return CatalogueOp(args);
                case "place": return Placement.Place(args);
                case "inspect": return Placement.Inspect(args);
                case "list": return Objects.List(args);
                case "objects": return Objects.Place(args);
                case "areas": return Objects.Areas(args);
                case "prefab": return PrefabOp(args);
                default: throw new ArgumentException("unknown op '" + op + "' (catalogue, place, inspect, list, objects, areas, prefab)");
            }
        }

        // prefab {names: "A,B"} [phase main]: a building prefab's runtime nets and sub-objects (SubNet elements, SubObject
        // elements with their own SubNet counts, placement flags), read only. For "why does this building get no tracks".
        static object PrefabOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var res = new Dictionary<string, object>();
            foreach (var nm in P.Required(a, "names").Split(','))
            {
                PrefabBase prefab;
                Entity pe;
                if (!ps.TryGetPrefab(new PrefabID("BuildingPrefab", nm.Trim()), out prefab) || !ps.TryGetEntity(prefab, out pe)) { res[nm] = "not found"; continue; }
                var o = new Dictionary<string, object>();
                if (em.HasComponent<PlaceableObjectData>(pe)) o["placementFlags"] = em.GetComponentData<PlaceableObjectData>(pe).m_Flags.ToString();
                var nets = new List<object>();
                if (em.HasBuffer<SubNet>(pe))
                {
                    var buf = em.GetBuffer<SubNet>(pe, true);
                    for (int i = 0; i < buf.Length; i++)
                        nets.Add((buf[i].m_Prefab == Entity.Null ? "null" : ps.GetPrefabName(buf[i].m_Prefab)) + " y=" + buf[i].m_Curve.a.y.ToString("0.0") + ".." + buf[i].m_Curve.d.y.ToString("0.0")
                            + (buf[i].m_Upgrades.Equals(default(CompositionFlags)) ? "" : " upgrade"));
                }
                o["subNets"] = nets;
                var subs = new List<object>();
                if (em.HasBuffer<SubObject>(pe))
                {
                    var buf = em.GetBuffer<SubObject>(pe, true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        var sp = buf[i].m_Prefab;
                        int n = sp != Entity.Null && em.HasBuffer<SubNet>(sp) ? em.GetBuffer<SubNet>(sp, true).Length : 0;
                        subs.Add((sp == Entity.Null ? "null" : ps.GetPrefabName(sp)) + " subNets=" + n + " flags=" + buf[i].m_Flags);
                    }
                }
                o["subObjects"] = subs;
                var authoring = new List<object>();
                foreach (var c in prefab.components) authoring.Add(c.GetType().Name);
                o["authoring"] = authoring;
                var osn = prefab.GetComponent<ObjectSubNets>();
                if (osn != null && osn.m_SubNets != null)
                {
                    var l = new List<object>();
                    foreach (var s in osn.m_SubNets) l.Add((s.m_NetPrefab != null ? s.m_NetPrefab.name : "null") + " upgrades=" + (s.m_Upgrades != null ? s.m_Upgrades.GetType().Name : "none"));
                    o["objectSubNets"] = l;
                }
                res[nm] = o;
            }
            return res;
        }

        static object CatalogueOp(Dictionary<string, object> a)
        {
            var path = Environment.ExpandEnvironmentVariables(P.Required(a, "path"));
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) throw new InvalidOperationException("no ECS world");
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int b, o;
            var cat = Catalogue.Build(world.EntityManager, ps, P.Bool(a, "objects", true), out b, out o);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, MiniJson.Write(cat), new UTF8Encoding(false));
            return new Dictionary<string, object>
            {
                { "path", Path.GetFullPath(path) },
                { "buildings", b },
                { "objects", o },
                { "errors", ((List<object>)cat["errors"]).Count },
                { "bytes", new FileInfo(path).Length },
                { "ms", sw.ElapsedMilliseconds },
            };
        }
    }
}
