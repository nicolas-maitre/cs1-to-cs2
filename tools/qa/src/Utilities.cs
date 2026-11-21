// Utilities check (docs/DECISIONS.md #109): how well a converted city is supplied with electricity, water and sewage,
// as CS2 itself tracks it. Read-only: it queries components and changes nothing.
//
//   exec tools/qa/bin/Release/net48/Cs1ConvQa.dll --type Cs1ConvQa.Utilities --args '{"listLimit":40}'
//
// What it reads (Game.dll 1.6, all public types):
//  - consumers: Game.Buildings.ElectricityConsumer (m_Flags Connected / NoElectricityWarning, wanted vs fulfilled) and
//    WaterConsumer (WaterConnected / SewageConnected, wanted vs fulfilled fresh / sewage), per building category
//    (ResidentialProperty, CommercialProperty, IndustrialProperty / OfficeProperty / ExtractorProperty, else service);
//  - totals: ElectricityStatisticsSystem / WaterStatisticsSystem (what the game's infoviews show), ElectricityTradeSystem;
//  - producers: ElectricityProducer, WaterPumpingStation, SewageOutlet buildings with their prefab, position, capacity,
//    last production, the flags of their flow edge (Disconnected) and the game's notification icons on them (the
//    ElectricityParameterData / WaterPipeParameterData notification prefabs: "not connected", "not enough groundwater",
//    "not enough surface water", ...);
//  - outside connections: Game.Objects.ElectricityOutsideConnection / WaterPipeOutsideConnection (and road ones for context).
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Buildings;
using Game.Common;
using Game.Notifications;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using ServiceUpgrade = Game.Buildings.ServiceUpgrade;
using WaterPumpingStation = Game.Buildings.WaterPumpingStation;
using SewageOutlet = Game.Buildings.SewageOutlet;
using WaterTower = Game.Buildings.WaterTower;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs1ConvQa
{
    public static class Utilities
    {
        public static object Run(Dictionary<string, object> args)
        {
            int listLimit = args != null && args.TryGetValue("listLimit", out var l) ? Convert.ToInt32(l) : 60;
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var prefabs = world.GetOrCreateSystemManaged<PrefabSystem>();

            string PrefabName(Entity e)
            {
                if (!em.HasComponent<PrefabRef>(e)) return null;
                var p = em.GetComponentData<PrefabRef>(e).m_Prefab;
                if (p == Entity.Null || !em.HasComponent<PrefabData>(p)) return null;
                return prefabs.TryGetPrefab(em.GetComponentData<PrefabData>(p), out PrefabBase pb) && pb != null ? pb.name : null;
            }

            // notification prefabs → names
            var notes = new Dictionary<Entity, string>();
            void Note(Entity e, string name) { if (e != Entity.Null && !notes.ContainsKey(e)) notes[e] = name; }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<ElectricityParameterData>()))
                if (q.CalculateEntityCount() > 0)
                {
                    var p = q.GetSingleton<ElectricityParameterData>();
                    Note(p.m_ElectricityNotificationPrefab, "noElectricity");
                    Note(p.m_LowVoltageNotConnectedPrefab, "lowVoltageNotConnected");
                    Note(p.m_HighVoltageNotConnectedPrefab, "highVoltageNotConnected");
                    Note(p.m_BottleneckNotificationPrefab, "electricityBottleneck");
                    Note(p.m_BuildingBottleneckNotificationPrefab, "buildingBottleneck");
                    Note(p.m_NotEnoughProductionNotificationPrefab, "notEnoughProduction");
                    Note(p.m_TransformerNotificationPrefab, "transformer");
                    Note(p.m_NotEnoughConnectedNotificationPrefab, "notEnoughConnected");
                    Note(p.m_BatteryEmptyNotificationPrefab, "batteryEmpty");
                }
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<WaterPipeParameterData>()))
                if (q.CalculateEntityCount() > 0)
                {
                    var p = q.GetSingleton<WaterPipeParameterData>();
                    Note(p.m_WaterNotification, "noWater");
                    Note(p.m_DirtyWaterNotification, "dirtyWater");
                    Note(p.m_SewageNotification, "sewageBackup");
                    Note(p.m_WaterPipeNotConnectedNotification, "waterPipeNotConnected");
                    Note(p.m_SewagePipeNotConnectedNotification, "sewagePipeNotConnected");
                    Note(p.m_NotEnoughWaterCapacityNotification, "notEnoughWaterCapacity");
                    Note(p.m_NotEnoughSewageCapacityNotification, "notEnoughSewageCapacity");
                    Note(p.m_NotEnoughGroundwaterNotification, "notEnoughGroundwater");
                    Note(p.m_NotEnoughSurfaceWaterNotification, "notEnoughSurfaceWater");
                    Note(p.m_DirtyWaterPumpNotification, "dirtyWaterPump");
                }
            List<string> Icons(Entity e)
            {
                var r = new List<string>();
                if (!em.HasBuffer<IconElement>(e)) return r;
                var buf = em.GetBuffer<IconElement>(e, true);
                for (int i = 0; i < buf.Length; i++)
                {
                    var ic = buf[i].m_Icon;
                    if (ic == Entity.Null || !em.Exists(ic) || !em.HasComponent<PrefabRef>(ic)) continue;
                    if (notes.TryGetValue(em.GetComponentData<PrefabRef>(ic).m_Prefab, out var n)) r.Add(n);
                }
                return r;
            }
            object Pos(Entity e)
            {
                if (!em.HasComponent<Game.Objects.Transform>(e)) return null;
                float3 p = em.GetComponentData<Game.Objects.Transform>(e).m_Position;
                return new List<object> { Math.Round(p.x, 1), Math.Round(p.y, 1), Math.Round(p.z, 1) };
            }

            // ---- consumers per category
            var cats = new[] { "residential", "commercial", "industrial", "service" };
            var cov = cats.ToDictionary(c => c, c => new Dictionary<string, int>
            {
                { "buildings", 0 }, { "electricityConsumers", 0 }, { "electricityConnected", 0 }, { "electricitySupplied", 0 }, { "noElectricityWarning", 0 },
                { "waterConsumers", 0 }, { "waterConnected", 0 }, { "waterSupplied", 0 }, { "sewageConnected", 0 }, { "sewageServed", 0 },
            });
            var noteCount = new Dictionary<string, int>();
            int abandoned = 0;
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Building>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<ServiceUpgrade>(), ComponentType.ReadOnly<Destroyed>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    if (em.HasComponent<Abandoned>(e)) { abandoned++; continue; }
                    string cat = em.HasComponent<ResidentialProperty>(e) ? "residential"
                        : em.HasComponent<CommercialProperty>(e) ? "commercial"
                        : em.HasComponent<IndustrialProperty>(e) || em.HasComponent<OfficeProperty>(e) || em.HasComponent<ExtractorProperty>(e) ? "industrial"
                        : "service";
                    var c = cov[cat];
                    c["buildings"]++;
                    if (em.HasComponent<ElectricityConsumer>(e))
                    {
                        var ec = em.GetComponentData<ElectricityConsumer>(e);
                        c["electricityConsumers"]++;
                        if ((ec.m_Flags & ElectricityConsumerFlags.Connected) != 0) c["electricityConnected"]++;
                        if (ec.m_FulfilledConsumption >= ec.m_WantedConsumption) c["electricitySupplied"]++;
                        if ((ec.m_Flags & ElectricityConsumerFlags.NoElectricityWarning) != 0) c["noElectricityWarning"]++;
                    }
                    if (em.HasComponent<WaterConsumer>(e))
                    {
                        var wc = em.GetComponentData<WaterConsumer>(e);
                        c["waterConsumers"]++;
                        if ((wc.m_Flags & WaterConsumerFlags.WaterConnected) != 0) c["waterConnected"]++;
                        if (wc.m_FulfilledFresh >= wc.m_WantedConsumption) c["waterSupplied"]++;
                        if ((wc.m_Flags & WaterConsumerFlags.SewageConnected) != 0) c["sewageConnected"]++;
                        if (wc.m_FulfilledSewage >= wc.m_WantedConsumption) c["sewageServed"]++;
                    }
                    foreach (var n in Icons(e)) noteCount[n] = (noteCount.TryGetValue(n, out var k) ? k : 0) + 1;
                }

            // ---- totals as the infoviews show them
            var totals = new Dictionary<string, object>();
            var es2 = world.GetExistingSystemManaged<ElectricityStatisticsSystem>();
            if (es2 != null) totals["electricity"] = new Dictionary<string, object> { { "production", es2.production }, { "consumption", es2.consumption }, { "fulfilledConsumption", es2.fulfilledConsumption }, { "batteryCharge", es2.batteryCharge }, { "batteryCapacity", es2.batteryCapacity } };
            var et = world.GetExistingSystemManaged<ElectricityTradeSystem>();
            if (et != null) totals["electricityTrade"] = new Dictionary<string, object> { { "export", et.export }, { "import", et.import } };
            var ws = world.GetExistingSystemManaged<WaterStatisticsSystem>();
            if (ws != null) totals["water"] = new Dictionary<string, object>
            {
                { "freshCapacity", ws.freshCapacity }, { "freshConsumption", ws.freshConsumption }, { "fulfilledFreshConsumption", ws.fulfilledFreshConsumption },
                { "sewageCapacity", ws.sewageCapacity }, { "sewageConsumption", ws.sewageConsumption }, { "fulfilledSewageConsumption", ws.fulfilledSewageConsumption },
            };

            // ---- producers
            List<Dictionary<string, object>> Facilities<T>(Func<Entity, Dictionary<string, object>> describe) where T : unmanaged, IComponentData
            {
                var r = new List<Dictionary<string, object>>();
                using (var q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<T>() }, None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() } }))
                using (var es = q.ToEntityArray(Allocator.Temp))
                    foreach (var e in es)
                    {
                        var owner = em.HasComponent<ServiceUpgrade>(e) && em.HasComponent<Owner>(e) ? em.GetComponentData<Owner>(e).m_Owner : Entity.Null;
                        var d = new Dictionary<string, object> { { "entity", e.Index }, { "prefab", PrefabName(e) }, { "position", Pos(owner != Entity.Null ? owner : e) } };
                        if (owner != Entity.Null) d["upgradeOf"] = PrefabName(owner);
                        foreach (var kv in describe(e)) d[kv.Key] = kv.Value;
                        var ic = Icons(e); if (owner != Entity.Null) ic.AddRange(Icons(owner));
                        d["notifications"] = ic.Distinct().ToList();
                        r.Add(d);
                    }
                return r;
            }
            string ElecEdge(Entity e)
            {
                if (!em.HasComponent<ElectricityBuildingConnection>(e)) return "noConnection";
                var c = em.GetComponentData<ElectricityBuildingConnection>(e);
                if (c.m_ProducerEdge == Entity.Null || !em.HasComponent<ElectricityFlowEdge>(c.m_ProducerEdge)) return "noProducerEdge";
                var f = em.GetComponentData<ElectricityFlowEdge>(c.m_ProducerEdge);
                return f.isDisconnected ? "disconnected" : f.isBottleneck ? "bottleneck" : "ok";
            }
            string WaterEdge(Entity e, bool sewage)
            {
                if (!em.HasComponent<WaterPipeBuildingConnection>(e)) return "noConnection";
                var c = em.GetComponentData<WaterPipeBuildingConnection>(e);
                if (c.m_ProducerEdge == Entity.Null || !em.HasComponent<WaterPipeEdge>(c.m_ProducerEdge)) return "noProducerEdge";
                var f = em.GetComponentData<WaterPipeEdge>(c.m_ProducerEdge).m_Flags;
                return (f & (sewage ? WaterPipeEdgeFlags.SewageDisconnected : WaterPipeEdgeFlags.WaterDisconnected)) != 0 ? "disconnected" : "ok";
            }
            T PrefabData<T>(Entity e) where T : unmanaged, IComponentData
            {
                var p = em.GetComponentData<PrefabRef>(e).m_Prefab;
                return em.HasComponent<T>(p) ? em.GetComponentData<T>(p) : default;
            }
            var power = Facilities<ElectricityProducer>(e =>
            {
                var p = em.GetComponentData<ElectricityProducer>(e);
                return new Dictionary<string, object> { { "capacity", p.m_Capacity }, { "lastProduction", p.m_LastProduction }, { "edge", ElecEdge(e) } };
            });
            var pumps = Facilities<WaterPumpingStation>(e =>
            {
                var p = em.GetComponentData<WaterPumpingStation>(e);
                var pd = PrefabData<WaterPumpingStationData>(e);
                return new Dictionary<string, object>
                {
                    { "capacity", p.m_Capacity }, { "prefabCapacity", pd.m_Capacity }, { "lastProduction", p.m_LastProduction }, { "pollution", Math.Round(p.m_Pollution, 3) },
                    { "waterTypes", pd.m_Types.ToString() }, { "edge", WaterEdge(e, false) },
                };
            });
            var outlets = Facilities<SewageOutlet>(e =>
            {
                var p = em.GetComponentData<SewageOutlet>(e);
                var pd = PrefabData<SewageOutletData>(e);
                return new Dictionary<string, object> { { "capacity", p.m_Capacity }, { "prefabCapacity", pd.m_Capacity }, { "lastProcessed", p.m_LastProcessed }, { "lastPurified", p.m_LastPurified }, { "edge", WaterEdge(e, true) } };
            });
            var towers = Facilities<WaterTower>(e => new Dictionary<string, object> { { "stored", em.GetComponentData<WaterTower>(e).m_StoredWater }, { "edge", WaterEdge(e, false) } });

            bool NotConnected(Dictionary<string, object> d) => (string)d["edge"] != "ok" && (string)d["edge"] != "bottleneck"
                || ((List<string>)d["notifications"]).Any(n => n.EndsWith("NotConnected"));
            var problems = new Dictionary<string, object>
            {
                { "powerNotConnected", power.Where(NotConnected).ToList() },
                { "powerIdle", power.Where(d => !NotConnected(d) && Convert.ToInt32(d["capacity"]) > 0 && Convert.ToInt32(d["lastProduction"]) == 0).ToList() },
                { "pumpsNotConnected", pumps.Where(NotConnected).ToList() },
                { "pumpsNotOnWater", pumps.Where(d => ((List<string>)d["notifications"]).Any(n => n == "notEnoughGroundwater" || n == "notEnoughSurfaceWater") || Convert.ToInt32(d["prefabCapacity"]) > 0 && Convert.ToInt32(d["capacity"]) == 0).ToList() },
                { "outletsNotConnected", outlets.Where(NotConnected).ToList() },
                { "outletsWithoutCapacity", outlets.Where(d => Convert.ToInt32(d["prefabCapacity"]) > 0 && Convert.ToInt32(d["capacity"]) == 0).ToList() },
            };

            // ---- outside connections
            int Count<T>() where T : unmanaged, IComponentData
            {
                using (var q = em.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<T>() }, None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() } }))
                    return q.CalculateEntityCount();
            }
            var outside = new Dictionary<string, object>
            {
                { "electricity", Count<Game.Objects.ElectricityOutsideConnection>() },
                { "water", Count<Game.Objects.WaterPipeOutsideConnection>() },
                { "all", Count<Game.Objects.OutsideConnection>() },
            };

            object Cap(List<Dictionary<string, object>> l) => l.Take(listLimit).ToList();
            return new Dictionary<string, object>
            {
                { "op", "utilities" },
                { "coverage", cov.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "abandonedBuildings", abandoned },
                { "buildingNotifications", noteCount },
                { "totals", totals },
                { "facilities", new Dictionary<string, object> { { "power", power.Count }, { "pumps", pumps.Count }, { "outlets", outlets.Count }, { "towers", towers.Count } } },
                { "problems", problems.ToDictionary(kv => kv.Key, kv => (object)new Dictionary<string, object> { { "count", ((List<Dictionary<string, object>>)kv.Value).Count }, { "items", Cap((List<Dictionary<string, object>>)kv.Value) } }) },
                { "power", Cap(power) }, { "pumps", Cap(pumps) }, { "outlets", Cap(outlets) }, { "towers", Cap(towers) },
                { "outsideConnections", outside },
            };
        }
    }
}
