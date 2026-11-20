using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ColossalFramework;
using UnityEngine;

namespace M0NetworkDump
{
    /// <summary>
    /// Reads the live NetManager buffers and writes one JSON document.
    /// Every section is wrapped so a failure in one (e.g. IMT reflection) becomes
    /// an entry in "report" instead of aborting the whole dump.
    /// </summary>
    public static class NetworkDumper
    {
        public static string LastSummary { get; private set; } = "";

        public static string Dump(string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var file = Path.Combine(outputDir, "network-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            var report = new List<string>();
            var usedInfos = new Dictionary<string, NetInfo>();
            int nodeCount = 0, segmentCount = 0, fillerCount = 0;

            using (var stream = new StreamWriter(file, false, new UTF8Encoding(false), 1 << 16))
            using (var w = new JsonWriter(stream))
            {
                w.BeginObject();
                w.Prop("format", "m0-network-dump");
                w.Prop("formatVersion", 1);
                w.Prop("exportedAt", DateTime.Now.ToString("o"));
                w.Prop("cityName", SafeCityName());
                w.Prop("gameVersion", SafeGameVersion());
                w.Prop("coordinateSystem", "CS1 world space, metres, Y up; map centre at origin");

                Section(w, report, "nodes", () => nodeCount = WriteNodes(w, usedInfos));
                Section(w, report, "segments", () => segmentCount = WriteSegments(w, usedInfos));
                Section(w, report, "netInfos", () => WriteNetInfos(w, usedInfos, report));
                Section(w, report, "imtFillers", () => fillerCount = ImtReader.WriteFillers(w, report));

                w.Name("report").BeginArray();
                foreach (var r in report) w.Value(r);
                w.EndArray();
                w.EndObject();
            }

            LastSummary = string.Format("{0} nodes, {1} segments, {2} NetInfos, {3} IMT fillers, {4} report entries",
                nodeCount, segmentCount, usedInfos.Count, fillerCount, report.Count);
            return file;
        }

        private static void Section(JsonWriter w, List<string> report, string name, Action body)
        {
            // Each section renders into its own buffer and only appends to the main document
            // once complete, so a failing section is simply missing (plus a report entry).
            try
            {
                body();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                report.Add("section '" + name + "' failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // ---------------------------------------------------------------- nodes

        private static int WriteNodes(JsonWriter w, Dictionary<string, NetInfo> used)
        {
            var nodes = Singleton<NetManager>.instance.m_nodes;
            var buf = nodes.m_buffer;
            var sb = new StringWriter();
            var tmp = new JsonWriter(sb);
            int count = 0;
            tmp.BeginArray();
            for (int i = 1; i < buf.Length; i++)
            {
                var n = buf[i];
                if ((n.m_flags & NetNode.Flags.Created) == 0 || (n.m_flags & NetNode.Flags.Deleted) != 0) continue;
                count++;
                var info = n.Info;
                if (info != null) used[info.name] = info;
                tmp.BeginObject();
                tmp.Prop("id", i);
                tmp.Prop("info", info != null ? info.name : null);
                tmp.Prop("position", n.m_position);
                tmp.Prop("flags", n.m_flags.ToString());
                tmp.Prop("elevation", (int)n.m_elevation);
                tmp.Prop("heightAboveTerrain", n.m_position.y - Singleton<TerrainManager>.instance.SampleRawHeightSmooth(n.m_position));
                tmp.Prop("building", (int)n.m_building);
                tmp.Name("segments").BeginArray();
                for (int s = 0; s < 8; s++)
                {
                    var seg = n.GetSegment(s);
                    if (seg != 0) tmp.Value((int)seg);
                }
                tmp.EndArray();
                tmp.EndObject();
            }
            tmp.EndArray();
            tmp.Dispose();
            w.Name("nodes");
            Raw(w, sb.ToString());
            return count;
        }

        // ------------------------------------------------------------- segments

        private static int WriteSegments(JsonWriter w, Dictionary<string, NetInfo> used)
        {
            var nm = Singleton<NetManager>.instance;
            var nodes = nm.m_nodes.m_buffer;
            var segs = nm.m_segments.m_buffer;
            var sb = new StringWriter();
            var tmp = new JsonWriter(sb);
            int count = 0;
            tmp.BeginArray();
            for (int i = 1; i < segs.Length; i++)
            {
                var s = segs[i];
                if ((s.m_flags & NetSegment.Flags.Created) == 0 || (s.m_flags & NetSegment.Flags.Deleted) != 0) continue;
                count++;
                var info = s.Info;
                if (info != null) used[info.name] = info;

                var a = nodes[s.m_startNode];
                var b = nodes[s.m_endNode];
                bool smoothStart = (a.m_flags & NetNode.Flags.Middle) != 0;
                bool smoothEnd = (b.m_flags & NetNode.Flags.Middle) != 0;
                Vector3 c1, c2;
                NetSegment.CalculateMiddlePoints(a.m_position, s.m_startDirection, b.m_position, s.m_endDirection,
                    smoothStart, smoothEnd, out c1, out c2);

                tmp.BeginObject();
                tmp.Prop("id", i);
                tmp.Prop("info", info != null ? info.name : null);
                tmp.Prop("ai", info != null && info.m_netAI != null ? info.m_netAI.GetType().Name : null);
                tmp.Prop("startNode", (int)s.m_startNode);
                tmp.Prop("endNode", (int)s.m_endNode);
                tmp.Prop("startDirection", s.m_startDirection);
                tmp.Prop("endDirection", s.m_endDirection);
                tmp.Name("bezier").BeginArray().Value(a.m_position).Value(c1).Value(c2).Value(b.m_position).EndArray();
                tmp.Prop("flags", s.m_flags.ToString());
                tmp.Prop("inverted", (s.m_flags & NetSegment.Flags.Invert) != 0);
                tmp.Prop("length", s.m_averageLength);
                if (info != null)
                {
                    // One-way = vehicle lanes only in one direction. With Invert set the
                    // lane directions of the NetInfo are mirrored relative to start→end.
                    bool fwd = info.m_hasForwardVehicleLanes, bwd = info.m_hasBackwardVehicleLanes;
                    tmp.Prop("oneWay", fwd != bwd);
                    tmp.Prop("elevationKind", ElevationKind(info));
                }
                tmp.EndObject();
            }
            tmp.EndArray();
            tmp.Dispose();
            w.Name("segments");
            Raw(w, sb.ToString());
            return count;
        }

        private static string ElevationKind(NetInfo info)
        {
            var ai = info.m_netAI;
            if (ai == null) return "unknown";
            var n = ai.GetType().Name;
            if (n.Contains("Tunnel")) return "tunnel";
            if (n.Contains("Bridge")) return "bridge";
            if (n.Contains("Elevated")) return "elevated";
            if (n.Contains("Slope")) return "slope";
            if (info.m_netAI.IsUnderground()) return "underground";
            return "ground";
        }

        // ------------------------------------------------------------- netInfos

        private static void WriteNetInfos(JsonWriter w, Dictionary<string, NetInfo> used, List<string> report)
        {
            // Pull in elevation variants (elevated/bridge/slope/tunnel) of used infos so the
            // converter sees the whole family even if a variant is not placed in this city.
            var queue = new List<NetInfo>(used.Values);
            foreach (var info in queue)
                foreach (var v in AiVariants(info))
                    if (!used.ContainsKey(v.name)) used[v.name] = v;

            var sb = new StringWriter();
            var tmp = new JsonWriter(sb);
            tmp.BeginArray();
            foreach (var kv in used)
            {
                try
                {
                    var one = new StringWriter();
                    var oneW = new JsonWriter(one);
                    WriteNetInfo(oneW, kv.Value);
                    oneW.Dispose();
                    tmp.RawValue(one.ToString());
                }
                catch (Exception e)
                {
                    report.Add("netInfo '" + kv.Key + "' failed: " + e.Message);
                }
            }
            tmp.EndArray();
            tmp.Dispose();
            w.Name("netInfos");
            Raw(w, sb.ToString());
        }

        private static IEnumerable<NetInfo> AiVariants(NetInfo info)
        {
            var ai = info.m_netAI;
            if (ai == null) yield break;
            foreach (var f in ai.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(NetInfo)) continue;
                var v = f.GetValue(ai) as NetInfo;
                if (v != null) yield return v;
            }
        }

        private static void WriteNetInfo(JsonWriter w, NetInfo info)
        {
            w.BeginObject();
            w.Prop("name", info.name);
            w.Prop("title", SafeTitle(info));
            w.Prop("ai", info.m_netAI != null ? info.m_netAI.GetType().FullName : null);
            w.Prop("service", info.m_class != null ? info.m_class.m_service.ToString() : null);
            w.Prop("subService", info.m_class != null ? info.m_class.m_subService.ToString() : null);
            w.Prop("level", info.m_class != null ? info.m_class.m_level.ToString() : null);
            w.Prop("className", info.m_class != null ? info.m_class.name : null);

            // All simple public fields of NetInfo (halfWidth, pavementWidth, surfaceLevel,
            // lane counts, createPavement/Gravel, terrain flags, ...).
            w.Name("fields");
            SimpleFields(w, info);

            // AI fields (speed limits live on lanes, but traffic lights, noise, elevation variants here).
            w.Name("aiFields");
            if (info.m_netAI != null) SimpleFields(w, info.m_netAI, true); else w.Value((string)null);

            w.Name("lanes").BeginArray();
            if (info.m_lanes != null)
            {
                for (int i = 0; i < info.m_lanes.Length; i++)
                {
                    var lane = info.m_lanes[i];
                    if (lane == null) continue;
                    w.BeginObject();
                    w.Prop("index", i);
                    w.Name("fields");
                    SimpleFields(w, lane);
                    w.Name("props").BeginArray();
                    var props = lane.m_laneProps != null ? lane.m_laneProps.m_props : null;
                    if (props != null)
                    {
                        foreach (var p in props)
                        {
                            if (p == null) continue;
                            w.BeginObject();
                            w.Prop("prop", p.m_prop != null ? p.m_prop.name : null);
                            w.Prop("tree", p.m_tree != null ? p.m_tree.name : null);
                            w.Prop("finalProp", p.m_finalProp != null ? p.m_finalProp.name : null);
                            w.Prop("finalTree", p.m_finalTree != null ? p.m_finalTree.name : null);
                            w.Name("fields");
                            SimpleFields(w, p);
                            w.EndObject();
                        }
                    }
                    w.EndArray();
                    w.EndObject();
                }
            }
            w.EndArray();
            w.EndObject();
        }

        /// <summary>Writes every public instance field whose type is primitive/enum/vector/string,
        /// plus NetInfo references by name.</summary>
        private static void SimpleFields(JsonWriter w, object o, bool includeNetInfoRefs = false)
        {
            w.BeginObject();
            foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = f.FieldType;
                object v;
                try { v = f.GetValue(o); } catch { continue; }
                if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(Vector3) || t == typeof(Vector2)
                    || t == typeof(Color) || t == typeof(Color32))
                {
                    w.Name(f.Name);
                    w.Any(v);
                }
                else if (includeNetInfoRefs && typeof(PrefabInfo).IsAssignableFrom(t))
                {
                    w.Name(f.Name);
                    w.Value(v is PrefabInfo pi && pi != null ? pi.name : null);
                }
            }
            w.EndObject();
        }

        // -------------------------------------------------------------- helpers

        private static void Raw(JsonWriter w, string json)
        {
            // JsonWriter.Name() already consumed the separator; append the pre-rendered value.
            w.RawValue(json);
        }

        private static string SafeTitle(NetInfo info)
        {
            try { return info.GetLocalizedTitle(); } catch { return null; }
        }

        private static string SafeCityName()
        {
            try { return Singleton<SimulationManager>.instance.m_metaData.m_CityName; } catch { return null; }
        }

        private static string SafeGameVersion()
        {
            try { return BuildConfig.applicationVersion; } catch { return null; }
        }
    }
}
