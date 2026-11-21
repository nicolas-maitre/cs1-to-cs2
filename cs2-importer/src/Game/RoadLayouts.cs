// Op "roadlayouts" (DECISIONS.md #100: prefer vanilla CS2 roads when they match the CS1 road): builds one straight 60 m
// test segment of every vanilla road and highway prefab in a box of the loaded (throwaway) city, then reads what CS2
// made of it: the edge composition's lanes (NetCompositionLane: lateral offset, flags, lane prefab width), its width and
// median pieces, and the managed RoadPrefab's speed limit, zoning and highway rules. Writes
// %LOCALAPPDATA%\cs1-to-cs2\samples\cs2-roads.json (or args.out). Phase + pump "tool" (it creates definitions).
//
//   args: x0, z0 (box corner, default 1400,-4600: the flat Corral Riches spot), spacing (default 100), out, include (regex
//   on prefab names, optional), clear (default true: bulldoze the box first, op clear's code)
//
// Road Builder roads (generated "r<uuid>-…" prefabs) and other mods' roads are left out: only prefabs of the game's own
// asset packs (no RoadBuilder prefix, PrefabBase.isBuiltin when available).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cs2Importer.Core;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using GameBridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Importer
{
    public static class RoadLayouts
    {
        static readonly Regex RoadBuilderId = new Regex(@"^r[0-9a-f]{8}-[0-9a-f]{4}-", RegexOptions.IgnoreCase);

        public static IEnumerator Dump(Dictionary<string, object> a)
        {
            var w = new GameWorld();
            var em = w.Em;
            double x0 = P.Num(a, "x0", 1400), z0 = P.Num(a, "z0", -4600), spacing = P.Num(a, "spacing", 100);
            var include = P.Has(a, "include") ? new Regex(P.Str(a, "include", ""), RegexOptions.IgnoreCase) : null;
            var outFile = P.Str(a, "out", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cs1-to-cs2", "samples", "cs2-roads.json"));

            // vanilla road prefabs
            var prefabs = new List<KeyValuePair<Entity, RoadPrefab>>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<RoadData>(), ComponentType.ReadOnly<NetGeometryData>(), ComponentType.ReadOnly<PrefabData>()))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    RoadPrefab rp;
                    if (!w.Prefabs.TryGetPrefab(em.GetComponentData<PrefabData>(e), out rp) || rp == null) continue;
                    var name = rp.name;
                    if (RoadBuilderId.IsMatch(name) || name.EndsWith("-cs1conv")) continue;
                    if (include != null && !include.IsMatch(name)) continue;
                    prefabs.Add(new KeyValuePair<Entity, RoadPrefab>(e, rp));
                }
            prefabs.Sort((p, q2) => string.CompareOrdinal(p.Value.name, q2.Value.name));
            int cols = (int)Math.Ceiling(Math.Sqrt(prefabs.Count));
            double size = cols * spacing + spacing;

            if (!P.Has(a, "clear") || P.Bool(a, "clear", true))
            {
                var c = (IEnumerator)Entry.Run(new Dictionary<string, object> { { "op", "clear" }, { "x0", x0 - spacing }, { "z0", z0 - spacing }, { "x1", x0 + size }, { "z1", z0 + size } });
                while (c.MoveNext()) yield return null;
            }

            // one straight segment per prefab, along +z, on the terrain
            var placed = new Dictionary<Entity, float2>();
            for (int i = 0; i < prefabs.Count; i++)
            {
                double x = x0 + (i % cols) * spacing, z = z0 + (i / cols) * spacing;
                float ya = w.TerrainHeight(x, z), yb = w.TerrainHeight(x, z + 60);
                var c = new[] { new V3(x, ya, z), new V3(x, ya + (yb - ya) / 3, z + 20), new V3(x, ya + 2 * (yb - ya) / 3, z + 40), new V3(x, yb, z + 60) };
                w.CreateSegment(prefabs[i].Key, c, Entity.Null, Entity.Null, 0f, 0f, 0f);
                placed[prefabs[i].Key] = new float2((float)x, (float)z + 30);
            }
            for (int f = 0; f < 60; f++) yield return null;

            // read the edges back
            var byPrefab = new Dictionary<Entity, Entity>();
            int owned;
            foreach (var e in w.EdgesInBox(x0 - spacing, z0 - spacing, x0 + size, z0 + size, out owned))
            {
                var pf = em.GetComponentData<PrefabRef>(e).m_Prefab;
                float2 at;
                if (!placed.TryGetValue(pf, out at) || byPrefab.ContainsKey(pf)) continue;
                byPrefab[pf] = e;
            }
            var roads = new List<object>();
            var notBuilt = new List<object>();
            foreach (var kv in prefabs)
            {
                Entity edge;
                if (!byPrefab.TryGetValue(kv.Key, out edge) || !em.HasComponent<Composition>(edge)) { notBuilt.Add(kv.Value.name); continue; }
                roads.Add(Describe(w, kv.Key, kv.Value, edge));
            }
            var doc = new Dictionary<string, object>
            {
                { "format", "cs2-roads" }, { "formatVersion", 1 }, { "generatedAt", DateTime.UtcNow.ToString("s") + "Z" },
                { "game", UnityEngine.Application.version },
                { "leftHandTraffic", w.World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic },
                { "conventions", "lanes left → right along the road's drawing direction (start → end); offset in metres from the centre line, + = right; dir forward = along start → end, backward = against it, both = two-way lane, null for pedestrian and parking lanes; widths in metres; speedKmh as the game shows it (speedPrefab = RoadPrefab.m_SpeedLimit, twice that); category highway (highway rules) / road (in a roads or transport-lane menu) / other; placeable = in one of those menus" },
                { "roads", roads }, { "notBuilt", notBuilt },
            };
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            File.WriteAllText(outFile, MiniJson.Write(doc), new System.Text.UTF8Encoding(false));
            yield return new Dictionary<string, object> { { "op", "roadlayouts" }, { "out", outFile }, { "prefabs", prefabs.Count }, { "described", roads.Count }, { "notBuilt", notBuilt } };
        }

        static Dictionary<string, object> Describe(GameWorld w, Entity prefab, RoadPrefab rp, Entity edge)
        {
            var em = w.Em;
            var comp = em.GetComponentData<Composition>(edge).m_Edge;
            var cd = em.GetComponentData<NetCompositionData>(comp);
            var rd = em.GetComponentData<RoadData>(prefab);
            var gd = em.GetComponentData<NetGeometryData>(prefab);
            // the composition's lane offsets are along the edge as built; flip them when CS2 laid the course the other way
            var curve = em.GetComponentData<Curve>(edge).m_Bezier;
            float sign = curve.d.z >= curve.a.z ? 1f : -1f;
            var lanes = new List<KeyValuePair<float, Dictionary<string, object>>>();
            if (em.HasBuffer<NetCompositionLane>(comp))
            {
                var buf = em.GetBuffer<NetCompositionLane>(comp, true);
                for (int i = 0; i < buf.Length; i++)
                {
                    var l = buf[i];
                    var f = l.m_Flags;
                    if ((f & (LaneFlags.Master | LaneFlags.Utility | LaneFlags.Virtual | LaneFlags.Secondary)) != 0) continue;
                    string type = (f & LaneFlags.Pedestrian) != 0 ? "pedestrian"
                        : (f & LaneFlags.Parking) != 0 ? "parking"
                        : (f & LaneFlags.Track) != 0 && (f & LaneFlags.Road) != 0 ? "car+tram"
                        : (f & LaneFlags.Track) != 0 ? "tram"
                        : (f & LaneFlags.BicyclesOnly) != 0 ? "bike"
                        : (f & LaneFlags.Road) != 0 && (f & LaneFlags.PublicOnly) != 0 ? "bus"
                        : (f & LaneFlags.Road) != 0 ? "car" : "other";
                    if (type == "other") continue;
                    bool invert = (f & LaneFlags.Invert) != 0;
                    if (sign < 0) invert = !invert;
                    string dir = (f & LaneFlags.Twoway) != 0 ? "both" : invert ? "backward" : "forward";
                    float width = em.HasComponent<NetLaneData>(l.m_Lane) ? em.GetComponentData<NetLaneData>(l.m_Lane).m_Width : 0f;
                    float off = l.m_Position.x * sign;
                    lanes.Add(new KeyValuePair<float, Dictionary<string, object>>(off, new Dictionary<string, object>
                    {
                        { "type", type }, { "dir", type == "pedestrian" || type == "parking" ? null : dir }, { "width", R(width) }, { "offset", R(off) },
                        { "carriageway", (int)l.m_Carriageway }, { "group", (int)l.m_Group }, { "index", (int)l.m_Index }, { "lane", w.Prefabs.GetPrefabName(l.m_Lane) },
                    }));
                }
            }
            lanes.Sort((p, q) => p.Key.CompareTo(q.Key));
            var lanesOut = lanes.Select(x => (object)x.Value).ToList();
            var drive = lanes.Where(x => (string)x.Value["type"] != "pedestrian" && (string)x.Value["type"] != "parking").ToList();
            double half = cd.m_Width / 2;
            // sidewalks: from the road edge to the outermost lane that isn't a pedestrian lane
            var nonPed = lanes.Where(x => (string)x.Value["type"] != "pedestrian").ToList();
            double swLeft = nonPed.Count > 0 ? (nonPed[0].Key - Convert.ToDouble(nonPed[0].Value["width"]) / 2) + half : 0;
            double swRight = nonPed.Count > 0 ? half - (nonPed[nonPed.Count - 1].Key + Convert.ToDouble(nonPed[nonPed.Count - 1].Value["width"]) / 2) : 0;
            double median = 0;
            if (em.HasBuffer<NetCompositionPiece>(comp))
            {
                var pieces = em.GetBuffer<NetCompositionPiece>(comp, true);
                for (int i = 0; i < pieces.Length; i++)
                    if ((pieces[i].m_SectionFlags & NetSectionFlags.Median) != 0) median += pieces[i].m_Size.x;
            }
            var dirs = drive.Select(x => (string)x.Value["dir"]).Distinct().ToList();
            string ui = null;
            try { var o = rp.GetComponent<UIObject>(); ui = o != null && o.m_Group != null ? o.m_Group.name : null; } catch { }
            return new Dictionary<string, object>
            {
                { "name", rp.name }, { "uiGroup", ui }, { "roadType", rp.m_RoadType.ToString() },
                // RoadPrefab.m_SpeedLimit is twice the km/h the game shows (Highway 240 → 120 km/h, Small Road 80 → 40)
                { "speedKmh", R(rp.m_SpeedLimit / 2f) }, { "speedPrefab", R(rp.m_SpeedLimit) }, { "highwayRules", rp.m_HighwayRules }, { "trafficLights", rp.m_TrafficLights },
                { "category", rp.m_HighwayRules ? "highway" : ui != null && (ui.StartsWith("Roads") || ui == "TransportationRoad") ? "road" : "other" },
                { "placeable", ui != null && (ui.StartsWith("Roads") || ui == "TransportationRoad") },
                { "zoning", rp.m_ZoneBlock != null }, { "separatedCarriageways", (rd.m_Flags & Game.Prefabs.RoadFlags.SeparatedCarriageways) != 0 },
                { "oneWay", dirs.Count == 1 && dirs[0] != "both" }, { "width", R(cd.m_Width) }, { "defaultWidth", R(gd.m_DefaultWidth) },
                { "sidewalkLeft", R((float)swLeft) }, { "sidewalkRight", R((float)swRight) }, { "medianWidth", R((float)MedianGap(drive)) }, { "medianPieces", R((float)median) },
                { "carLanes", drive.Count(x => ((string)x.Value["type"]).StartsWith("car")) }, { "drivingLanes", drive.Count },
                { "elevationLimit", R(gd.m_ElevationLimit) }, { "geometryFlags", gd.m_Flags.ToString() },
                { "lanes", lanesOut },
            };
        }

        /// <summary>
        /// The space between the two directions: from the right edge of the last backward driving lane to the left edge of
        /// the first forward one (0 for one-way roads). The composition's median pieces aren't a reliable measure: they
        /// add up to 2 m on Large Road, whose lanes meet at the centre line (web worker's finding).
        /// </summary>
        static double MedianGap(List<KeyValuePair<float, Dictionary<string, object>>> drive)
        {
            for (int i = 0; i + 1 < drive.Count; i++)
            {
                var l = drive[i].Value; var r = drive[i + 1].Value;
                if ((string)l["dir"] == "backward" && (string)r["dir"] == "forward")
                    return Math.Max(0, (drive[i + 1].Key - Convert.ToDouble(r["width"]) / 2) - (drive[i].Key + Convert.ToDouble(l["width"]) / 2));
            }
            return 0;
        }

        static double R(float v) { return Math.Round(v, 2); }
    }
}
