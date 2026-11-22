// exec entry point of the CS2 buildings library (tools/PROTOCOL.md "exec"). One op per call, on args.op:
//
//   catalogue {path, objects=true}           [phase main]  dump every building / static-object prefab to {path} (JSON)
//   place     {plan, offsetX=0, offsetZ=0, …} [phase tool, pump tool]  place the plan's buildings, then verify;
//                                                           returns the report (and writes it to {report} if given)
//   inspect   {x, z, r=60}                    [phase main]  real buildings near a point (prefab, lot, road edge)
//   list      {type=regex, name=regex}         [phase main]  prefabs by class name (areas, policies, …)
//   objects   {plan, offsetX, offsetZ, batchSize=2000} [phase tool, pump tool]  trees / props of plan.objects
//   areas     {plan, section=districts|surfaces, offsetX, offsetZ} [phase tool, pump tool]  districts (name, policies) or surfaces
//   utilities {plan, offsetX, offsetZ, kinds, powerPrefab, pipePrefab, snapRadius} [phase tool, pump tool]  plan.utilities
//             power lines and pipes as CS2 nets, ends snapped to plant / pump / outlet markers (decision #40)
//   water     {points: "x,z;x,z", r=60}          [any phase]  surface-water depth, nearest water within r, groundwater (read only)
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
                case "utilities": return Utilities.Place(args);
                case "netlayers": return NetLayersOp(args);
                case "instance": return InstanceOp(args);
                case "water": return WaterOp(args);
                case "powerhook": return PowerHook.Start(args);
                case "linetest": return LineTest.Start(args);
                case "nodes": return NodesOp(args);
                case "census": return CensusOp(args);
                case "zonecells": return ZoneCellsOp(args);
                default: throw new ArgumentException("unknown op '" + op + "' (catalogue, place, inspect, list, objects, areas, prefab, utilities, netlayers, instance, water)");
            }
        }

        // zonecells {} [phase main]: zone cells by zone prefab and state (visible, occupied, blocked, shared …), from every
        // Game.Zones.Block's Cell buffer. Read only; is residential land zoned and free (QA §12)?
        static object ZoneCellsOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var names = new Dictionary<ushort, string>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<ZoneData>(), ComponentType.ReadOnly<PrefabData>()))
            using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var e in es) names[em.GetComponentData<ZoneData>(e).m_ZoneType.m_Index] = ps.GetPrefabName(e);
            var counts = new SortedDictionary<string, int>();
            int blocks = 0;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Zones.Block>(), ComponentType.ReadOnly<Game.Zones.Cell>()))
            using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var b in es)
                {
                    if (em.HasComponent<Game.Common.Deleted>(b)) continue;
                    blocks++;
                    var cells = em.GetBuffer<Game.Zones.Cell>(b, true);
                    for (int i = 0; i < cells.Length; i++)
                    {
                        var c = cells[i];
                        string zn;
                        if (!names.TryGetValue(c.m_Zone.m_Index, out zn)) zn = "zone" + c.m_Zone.m_Index;
                        var key = zn + " | " + c.m_State;
                        int n; counts.TryGetValue(key, out n); counts[key] = n + 1;
                    }
                }
            return new Dictionary<string, object> { { "blocks", blocks }, { "cells", counts } };
        }

        // census {} [phase main]: every building by its zone kind (the prefab's zone area type, or "service") and state
        // (UnderConstruction, Abandoned, Condemned, with ResidentialProperty / CommercialProperty / IndustrialProperty …),
        // plus households and the simulation frame. Read only; for "why so few homes after 300 s" (QA §12).
        static object CensusOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var res = new Dictionary<string, object>();
            var byKind = new SortedDictionary<string, int>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Game.Common.Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            }))
            using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var e in es)
                {
                    var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    string zone = "service";
                    if (em.HasComponent<SpawnableBuildingData>(pf))
                    {
                        var zp = em.GetComponentData<SpawnableBuildingData>(pf).m_ZonePrefab;
                        zone = zp != Entity.Null && em.HasComponent<ZoneData>(zp) ? em.GetComponentData<ZoneData>(zp).m_AreaType.ToString() : "spawnable?";
                    }
                    var st = new List<string>();
                    if (em.HasComponent<Game.Objects.UnderConstruction>(e)) st.Add("underConstruction");
                    if (em.HasComponent<Game.Buildings.Abandoned>(e)) st.Add("abandoned");
                    if (em.HasComponent<Game.Buildings.Condemned>(e)) st.Add("condemned");
                    if (em.HasComponent<Game.Common.Destroyed>(e)) st.Add("destroyed");
                    if (em.HasComponent<Game.Buildings.ResidentialProperty>(e)) st.Add("resProp");
                    if (em.HasComponent<Game.Buildings.CommercialProperty>(e)) st.Add("comProp");
                    if (em.HasComponent<Game.Buildings.IndustrialProperty>(e)) st.Add("indProp");
                    if (em.HasComponent<Game.Buildings.OfficeProperty>(e)) st.Add("offProp");
                    if (em.HasBuffer<Game.Buildings.Renter>(e)) st.Add("renters" + Math.Min(em.GetBuffer<Game.Buildings.Renter>(e, true).Length, 1));
                    var key = zone + " | " + (st.Count == 0 ? "-" : string.Join(",", st));
                    int c; byKind.TryGetValue(key, out c); byKind[key] = c + 1;
                }
            res["buildings"] = byKind;
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Citizens.Household>()))
                res["households"] = q.CalculateEntityCount();
            var sim = world.GetExistingSystemManaged<Game.Simulation.SimulationSystem>();
            if (sim != null) res["frameIndex"] = sim.frameIndex;
            var rd = world.GetExistingSystemManaged<Game.Simulation.ResidentialDemandSystem>();
            if (rd != null)
            {
                res["householdDemand"] = rd.householdDemand;
                res["buildingDemand"] = rd.buildingDemand.x + " / " + rd.buildingDemand.y + " / " + rd.buildingDemand.z + " (low / medium / high)";
                var names = Enum.GetNames(typeof(Game.Simulation.DemandFactor));
                foreach (var dens in new[] { "low", "high" })
                {
                    var deps = default(Unity.Jobs.JobHandle);
                    var f = dens == "low" ? rd.GetLowDensityDemandFactors(out deps) : rd.GetHighDensityDemandFactors(out deps);
                    deps.Complete();
                    var d = new Dictionary<string, object>();
                    for (int i = 0; i < f.Length && i < names.Length; i++) if (f[i] != 0) d[names[i]] = f[i];
                    res[dens + "DensityFactors"] = d;
                }
            }
            return res;
        }

        // nodes {x, z, r=4}: every node within r m, with each connected edge's prefab and whether this node is that edge's
        // start / end ("end") or only connected to it in the middle ("local"). Read only; for split-junction checks (#145).
        static object NodesOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            float x = (float)P.Num(a, "x", 0), z = (float)P.Num(a, "z", 0), r = (float)P.Num(a, "r", 4);
            var list = new List<object>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Net.Node>(), ComponentType.ReadOnly<PrefabRef>()))
            using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var n in es)
                {
                    var p = em.GetComponentData<Game.Net.Node>(n).m_Position;
                    float d = (float)Math.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z));
                    if (d > r || em.HasComponent<Game.Common.Deleted>(n)) continue;
                    var edges = new List<object>();
                    if (em.HasBuffer<Game.Net.ConnectedEdge>(n))
                    {
                        var buf = em.GetBuffer<Game.Net.ConnectedEdge>(n, true);
                        for (int i = 0; i < buf.Length; i++)
                        {
                            var e = buf[i].m_Edge;
                            var ed = em.GetComponentData<Game.Net.Edge>(e);
                            string role = ed.m_Start == n ? "start" : ed.m_End == n ? "end" : "local";
                            edges.Add(e.Index + " " + ps.GetPrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) + " (" + role + "; " + ed.m_Start.Index + "-" + ed.m_End.Index + ")");
                        }
                    }
                    list.Add(new Dictionary<string, object> { { "node", n.Index }, { "prefab", ps.GetPrefabName(em.GetComponentData<PrefabRef>(n).m_Prefab) }, { "d", Math.Round(d, 2) }, { "owned", em.HasComponent<Game.Common.Owner>(n) }, { "edges", edges } });
                }
            return new Dictionary<string, object> { { "x", x }, { "z", z }, { "nodes", list } };
        }

        // water {points: "x,z;…", r=60}: what the placer's pump rule (decision #42) sees at each point. Read only.
        static object WaterOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var ws = world.GetOrCreateSystemManaged<Game.Simulation.WaterSystem>();
            var deps = default(Unity.Jobs.JobHandle);
            var sd = ws.GetSurfaceData(out deps);
            deps.Complete();
            var gdeps = default(Unity.Jobs.JobHandle);
            var gd = world.GetOrCreateSystemManaged<Game.Simulation.GroundWaterSystem>().GetData(true, out gdeps);
            gdeps.Complete();
            float r = (float)P.Num(a, "r", 60);
            var res = new List<object>();
            foreach (var pt in P.Required(a, "points").Split(';'))
            {
                var xz = pt.Split(',');
                float x = float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), z = float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture);
                float nearest = -1, maxDepth = 0;
                for (float rr = 0; rr <= r && nearest < 0; rr += 4)
                    for (int k = 0; k < (rr == 0 ? 1 : 24); k++)
                    {
                        float ang = k * (float)Math.PI * 2f / 24f;
                        float d = Game.Simulation.WaterUtils.SampleDepth(ref sd, new Unity.Mathematics.float3(x + rr * (float)Math.Cos(ang), 0, z + rr * (float)Math.Sin(ang)));
                        if (d > maxDepth) maxDepth = d;
                        if (d > 0.5f && nearest < 0) nearest = rr;
                    }
                var g = Game.Simulation.GroundWaterSystem.GetGroundWater(new Unity.Mathematics.float3(x, 0, z), gd.m_Buffer);
                res.Add(new Dictionary<string, object> { { "x", x }, { "z", z }, { "depth", Game.Simulation.WaterUtils.SampleDepth(ref sd, new Unity.Mathematics.float3(x, 0, z)) }, { "nearestWater", nearest }, { "maxDepthSeen", maxDepth }, { "groundwater", (int)g.m_Amount }, { "groundwaterMax", (int)g.m_Max } });
            }
            return new Dictionary<string, object> { { "seaLevel", ws.SeaLevel }, { "points", res } };
        }

        // instance {prefab, limit=3} [phase main]: placed buildings of a prefab, with their own sub-net edges (Game.Net.SubNet)
        // and the layers of those edges' nodes, plus the road edge they connect to. Read only, for the utilities step.
        static object InstanceOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            PrefabBase prefab;
            Entity pe;
            if (!ps.TryGetPrefab(new PrefabID("BuildingPrefab", P.Required(a, "prefab")), out prefab) || !ps.TryGetEntity(prefab, out pe)) return "not found";
            int limit = (int)P.Num(a, "limit", 3);
            var list = new List<object>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<PrefabRef>()))
            using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var b in es)
                {
                    if (em.GetComponentData<PrefabRef>(b).m_Prefab != pe || list.Count >= limit) continue;
                    var o = new Dictionary<string, object> { { "entity", b.Index } };
                    var road = em.GetComponentData<Game.Buildings.Building>(b).m_RoadEdge;
                    o["roadEdge"] = road == Entity.Null ? null : (object)ps.GetPrefabName(em.GetComponentData<PrefabRef>(road).m_Prefab);
                    var subs = new List<object>();
                    if (em.HasBuffer<Game.Net.SubNet>(b))
                    {
                        var buf = em.GetBuffer<Game.Net.SubNet>(b, true);
                        for (int i = 0; i < buf.Length; i++)
                        {
                            var e = buf[i].m_SubNet;
                            if (!em.HasComponent<PrefabRef>(e)) continue;
                            var name = ps.GetPrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab);
                            if (name.StartsWith("Invisible")) continue;
                            string nodes = "";
                            if (em.HasComponent<Game.Net.Edge>(e))
                            {
                                var ed = em.GetComponentData<Game.Net.Edge>(e);
                                foreach (var n in new[] { ed.m_Start, ed.m_End })
                                {
                                    int c = em.HasBuffer<Game.Net.ConnectedEdge>(n) ? em.GetBuffer<Game.Net.ConnectedEdge>(n, true).Length : -1;
                                    nodes += " node" + n.Index + "(" + c + " edges)";
                                }
                            }
                            subs.Add(name + (em.HasComponent<Game.Net.Edge>(e) ? " edge" : " node") + nodes);
                        }
                    }
                    o["subNets"] = subs;
                    list.Add(o);
                }
            return new Dictionary<string, object> { { "prefab", prefab.name }, { "instances", list } };
        }

        // netlayers {names: "type:Name,…"} [phase main]: a net prefab's NetData layers (what it needs / connects to), read
        // only. With a loaded city, also counts the existing edges of each prefab. For the utilities step's snapping.
        static object NetLayersOp(Dictionary<string, object> a)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var res = new Dictionary<string, object>();
            foreach (var spec in P.Required(a, "names").Split(','))
            {
                var parts = spec.Split(new[] { ':' }, 2);
                PrefabBase prefab;
                Entity pe;
                if (parts.Length != 2 || !ps.TryGetPrefab(new PrefabID(parts[0].Trim(), parts[1].Trim()), out prefab) || !ps.TryGetEntity(prefab, out pe)) { res[spec] = "not found"; continue; }
                var o = new Dictionary<string, object>();
                if (em.HasComponent<NetData>(pe))
                {
                    var nd = em.GetComponentData<NetData>(pe);
                    o["required"] = nd.m_RequiredLayers.ToString();
                    o["connect"] = nd.m_ConnectLayers.ToString();
                    o["localConnect"] = nd.m_LocalConnectLayers.ToString();
                }
                if (em.HasComponent<PlaceableNetData>(pe))
                {
                    var pn = em.GetComponentData<PlaceableNetData>(pe);
                    o["elevationRange"] = pn.m_ElevationRange.min + ".." + pn.m_ElevationRange.max;
                    o["placementFlags"] = pn.m_PlacementFlags.ToString();
                    o["snapDistance"] = pn.m_SnapDistance;
                }
                int edges = 0;
                using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Net.Edge>(), ComponentType.ReadOnly<PrefabRef>()))
                using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                    foreach (var e in es) if (em.GetComponentData<PrefabRef>(e).m_Prefab == pe) edges++;
                o["edgesInCity"] = edges;
                res[spec] = o;
            }
            return res;
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
