using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// network.json (nodes + segments) and netinfos.json. Field shapes follow the M0 dump
    /// (spikes/m0-cs1-network-dump) so the M0 analysis scripts keep working; see schema/README.md.
    /// </summary>
    public sealed class NetworkExporter
    {
        private readonly Report _report;

        /// <summary>NetInfos used by at least one exported node or segment, by name.</summary>
        public readonly Dictionary<string, NetInfo> Used = new Dictionary<string, NetInfo>();

        public int NodeCount, SegmentCount, NetInfoCount, SkippedItems;

        // Variant relationships over all loaded NetInfos, built once (lazily).
        private Dictionary<string, List<KeyValuePair<string, NetInfo>>> _parents; // variant name -> (kind, parent)
        private bool _parentsBuilt;

        private static readonly string[] VariantFields = { "m_elevatedInfo", "m_bridgeInfo", "m_slopeInfo", "m_tunnelInfo" };
        private static readonly string[] VariantKinds = { "elevated", "bridge", "slope", "tunnel" };

        public NetworkExporter(Report report) { _report = report; }

        // ================================================================== network.json

        public void WriteNetwork(JsonWriter w)
        {
            BuildParents();
            w.BeginObject();
            w.Name("nodes");
            WriteNodes(w);
            w.Name("segments");
            WriteSegments(w);
            w.EndObject();
            if (SkippedItems > 0) _report.Warn("network", SkippedItems + " nodes/segments skipped because reading them threw");
        }

        private void WriteNodes(JsonWriter w)
        {
            var buf = Singleton<NetManager>.instance.m_nodes.m_buffer;
            var tm = Singleton<TerrainManager>.instance;
            var segs = new List<int>(8);
            w.BeginArray();
            for (int i = 1; i < buf.Length; i++)
            {
                var n = buf[i];
                if ((n.m_flags & NetNode.Flags.Created) == 0 || (n.m_flags & NetNode.Flags.Deleted) != 0) continue;

                NetInfo info; float hat; string flags;
                try
                {
                    info = n.Info;
                    hat = n.m_position.y - tm.SampleRawHeightSmooth(n.m_position);
                    flags = n.m_flags.ToString();
                    segs.Clear();
                    for (int s = 0; s < 8; s++)
                    {
                        var seg = n.GetSegment(s);
                        if (seg != 0) segs.Add(seg);
                    }
                }
                catch (Exception e)
                {
                    Skip("node " + i, e);
                    continue;
                }

                NodeCount++;
                if (info != null) Used[info.name] = info;
                w.BeginObject();
                w.Prop("id", i);
                w.Prop("info", info != null ? info.name : null);
                w.Prop("position", n.m_position);
                w.Prop("flags", flags);
                w.Prop("elevation", (int)n.m_elevation);
                w.Prop("heightAboveTerrain", hat);
                w.Prop("building", (int)n.m_building);
                w.Name("segments").BeginArray();
                foreach (var s in segs) w.Value(s);
                w.EndArray();
                w.EndObject();
            }
            w.EndArray();
        }

        private void WriteSegments(JsonWriter w)
        {
            var nm = Singleton<NetManager>.instance;
            var nodes = nm.m_nodes.m_buffer;
            var buf = nm.m_segments.m_buffer;
            w.BeginArray();
            for (int i = 1; i < buf.Length; i++)
            {
                var s = buf[i];
                if ((s.m_flags & NetSegment.Flags.Created) == 0 || (s.m_flags & NetSegment.Flags.Deleted) != 0) continue;

                NetInfo info; Vector3 a, b, c1, c2; string flags, ai, kind = null; bool? oneWay = null;
                try
                {
                    info = s.Info;
                    var na = nodes[s.m_startNode];
                    var nb = nodes[s.m_endNode];
                    a = na.m_position;
                    b = nb.m_position;
                    // Same smoothing rule the game uses when it renders the segment.
                    bool smoothStart = (na.m_flags & NetNode.Flags.Middle) != 0;
                    bool smoothEnd = (nb.m_flags & NetNode.Flags.Middle) != 0;
                    NetSegment.CalculateMiddlePoints(a, s.m_startDirection, b, s.m_endDirection, smoothStart, smoothEnd, out c1, out c2);
                    flags = s.m_flags.ToString();
                    ai = info != null && info.m_netAI != null ? info.m_netAI.GetType().Name : null;
                    if (info != null)
                    {
                        // One-way = vehicle lanes in one direction only.
                        oneWay = info.m_hasForwardVehicleLanes != info.m_hasBackwardVehicleLanes;
                        kind = ElevationKind(info);
                    }
                }
                catch (Exception e)
                {
                    Skip("segment " + i, e);
                    continue;
                }

                SegmentCount++;
                if (info != null) Used[info.name] = info;
                w.BeginObject();
                w.Prop("id", i);
                w.Prop("info", info != null ? info.name : null);
                w.Prop("ai", ai);
                w.Prop("startNode", (int)s.m_startNode);
                w.Prop("endNode", (int)s.m_endNode);
                w.Prop("startDirection", s.m_startDirection);
                w.Prop("endDirection", s.m_endDirection);
                w.Name("bezier").BeginArray().Value(a).Value(c1).Value(c2).Value(b).EndArray();
                w.Prop("flags", flags);
                w.Prop("inverted", (s.m_flags & NetSegment.Flags.Invert) != 0);
                w.Prop("length", s.m_averageLength);
                if (oneWay.HasValue) w.Prop("oneWay", oneWay.Value);
                if (kind != null) w.Prop("elevationKind", kind);
                w.EndObject();
            }
            w.EndArray();
        }

        private void Skip(string what, Exception e)
        {
            SkippedItems++;
            if (SkippedItems <= 20) _report.Warn("network", what + " skipped: " + Report.Describe(e));
        }

        /// <summary>
        /// ground | elevated | bridge | slope | tunnel. The variant relationship (which field of its parent's AI points
        /// to this NetInfo) wins, because modded AIs don't always follow the vanilla class names; the AI class name is
        /// the fallback (that's what M0 did).
        /// </summary>
        private string ElevationKind(NetInfo info)
        {
            List<KeyValuePair<string, NetInfo>> ps;
            if (_parents != null && _parents.TryGetValue(info.name, out ps) && ps.Count > 0) return ps[0].Key;
            var ai = info.m_netAI;
            if (ai == null) return "ground";
            var n = ai.GetType().Name;
            if (n.Contains("Tunnel")) return "tunnel";
            if (n.Contains("Bridge")) return "bridge";
            if (n.Contains("Elevated")) return "elevated";
            if (n.Contains("Slope")) return "slope";
            try { if (ai.IsUnderground()) return "tunnel"; } catch { }
            return "ground";
        }

        // ================================================================== variants / parents

        /// <summary>Map every loaded NetInfo's elevated/bridge/slope/tunnel variant back to its parent.</summary>
        private void BuildParents()
        {
            if (_parentsBuilt) return;
            _parentsBuilt = true;
            _parents = new Dictionary<string, List<KeyValuePair<string, NetInfo>>>();
            try
            {
                uint count = (uint)PrefabCollection<NetInfo>.LoadedCount();
                for (uint i = 0; i < count; i++)
                {
                    NetInfo info;
                    try { info = PrefabCollection<NetInfo>.GetLoaded(i); } catch { continue; }
                    if (info == null) continue;
                    foreach (var kv in CanonicalVariants(info))
                    {
                        List<KeyValuePair<string, NetInfo>> list;
                        if (!_parents.TryGetValue(kv.Value.name, out list)) _parents[kv.Value.name] = list = new List<KeyValuePair<string, NetInfo>>();
                        list.Add(new KeyValuePair<string, NetInfo>(kv.Key, info));
                    }
                }
            }
            catch (Exception e)
            {
                _report.Warn("netinfos", "could not scan loaded NetInfos for variant parents: " + Report.Describe(e));
            }
        }

        /// <summary>(kind, variant) for the four vanilla variant fields of the NetInfo's AI; self references are skipped.</summary>
        private static IEnumerable<KeyValuePair<string, NetInfo>> CanonicalVariants(NetInfo info)
        {
            var ai = info.m_netAI;
            if (ai == null) yield break;
            var t = ai.GetType();
            for (int k = 0; k < VariantFields.Length; k++)
            {
                var f = t.GetField(VariantFields[k], BindingFlags.Public | BindingFlags.Instance);
                if (f == null || f.FieldType != typeof(NetInfo)) continue;
                NetInfo v;
                try { v = f.GetValue(ai) as NetInfo; } catch { continue; }
                if (v != null && v != info && v.name != info.name) yield return new KeyValuePair<string, NetInfo>(VariantKinds[k], v);
            }
        }

        /// <summary>Every public NetInfo field of the AI (the canonical four plus mod-specific extras), as M0 did.</summary>
        private static IEnumerable<NetInfo> AllAiNetInfos(NetInfo info)
        {
            var ai = info.m_netAI;
            if (ai == null) yield break;
            foreach (var f in ai.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(NetInfo)) continue;
                NetInfo v;
                try { v = f.GetValue(ai) as NetInfo; } catch { continue; }
                if (v != null) yield return v;
            }
        }

        /// <summary>Preferred parent: one that is used in the city, then by name, so the choice is deterministic.</summary>
        private NetInfo ParentOf(NetInfo info, out string kind, out List<string> others)
        {
            kind = null; others = null;
            List<KeyValuePair<string, NetInfo>> ps;
            if (_parents == null || !_parents.TryGetValue(info.name, out ps) || ps.Count == 0) return null;
            var sorted = new List<KeyValuePair<string, NetInfo>>(ps);
            sorted.Sort((x, y) =>
            {
                bool ux = Used.ContainsKey(x.Value.name), uy = Used.ContainsKey(y.Value.name);
                if (ux != uy) return ux ? -1 : 1;
                return string.CompareOrdinal(x.Value.name, y.Value.name);
            });
            kind = sorted[0].Key;
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].Value.name == sorted[0].Value.name) continue;
                if (others == null) others = new List<string>();
                if (!others.Contains(sorted[i].Value.name)) others.Add(sorted[i].Value.name);
            }
            return sorted[0].Value;
        }

        // ================================================================== netinfos.json

        public void WriteNetInfos(JsonWriter w)
        {
            BuildParents();

            // Closure: used, plus the parent of every used variant, plus all AI NetInfo refs (variants) of both.
            var all = new Dictionary<string, NetInfo>(Used);
            int parentsAdded = 0;
            foreach (var info in new List<NetInfo>(Used.Values))
            {
                string kind; List<string> others;
                var p = ParentOf(info, out kind, out others);
                if (p != null && !all.ContainsKey(p.name)) { all[p.name] = p; parentsAdded++; }
            }
            foreach (var info in new List<NetInfo>(all.Values))
                foreach (var v in AllAiNetInfos(info))
                    if (!all.ContainsKey(v.name)) all[v.name] = v;

            var names = new List<string>(all.Keys);
            names.Sort(string.CompareOrdinal);

            w.BeginObject();
            w.Name("netInfos").BeginArray();
            int failed = 0;
            foreach (var name in names)
            {
                // Render each NetInfo on its own so one broken (modded) prefab is skipped, not the section.
                string json;
                try
                {
                    var sw = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                    var one = new JsonWriter(sw);
                    WriteNetInfo(one, all[name]);
                    one.Dispose();
                    json = sw.ToString();
                }
                catch (Exception e)
                {
                    failed++;
                    _report.Warn("netinfos", "NetInfo '" + name + "' skipped: " + Report.Describe(e));
                    continue;
                }
                w.RawValue(json);
                NetInfoCount++;
            }
            w.EndArray();
            w.EndObject();
            _report.Info("netinfos", NetInfoCount + " NetInfos: " + Used.Count + " used, " + parentsAdded + " parents added, the rest are variants"
                + (failed > 0 ? ", " + failed + " failed" : ""));
        }

        private void WriteNetInfo(JsonWriter w, NetInfo info)
        {
            string parentKind; List<string> otherParents;
            var parent = ParentOf(info, out parentKind, out otherParents);

            w.BeginObject();
            w.Prop("name", info.name);
            w.Prop("title", SafeTitle(info));
            w.Prop("ai", info.m_netAI != null ? info.m_netAI.GetType().FullName : null);
            w.Prop("service", info.m_class != null ? info.m_class.m_service.ToString() : null);
            w.Prop("subService", info.m_class != null ? info.m_class.m_subService.ToString() : null);
            w.Prop("level", info.m_class != null ? info.m_class.m_level.ToString() : null);
            w.Prop("className", info.m_class != null ? info.m_class.name : null);
            w.Prop("used", Used.ContainsKey(info.name));
            w.Prop("parent", parent != null ? parent.name : null);
            if (parent != null) w.Prop("variantKind", parentKind);
            if (otherParents != null)
            {
                w.Name("otherParents").BeginArray();
                foreach (var o in otherParents) w.Value(o);
                w.EndArray();
            }
            if (parent == null)
            {
                w.Name("variants").BeginObject();
                foreach (var kv in CanonicalVariants(info)) w.Prop(kv.Key, kv.Value.name);
                w.EndObject();
            }

            // All simple public fields of NetInfo (halfWidth, pavementWidth, surfaceLevel, lane counts, ...).
            w.Name("fields");
            SimpleFields(w, info);

            // AI fields (traffic lights, noise, elevation variants by name, ...).
            w.Name("aiFields");
            if (info.m_netAI != null) SimpleFields(w, info.m_netAI, true); else w.Null();

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

        /// <summary>Every public instance field whose type is primitive/enum/vector/colour/string, plus PrefabInfo refs by name.</summary>
        private static void SimpleFields(JsonWriter w, object o, bool includePrefabRefs = false)
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
                else if (includePrefabRefs && typeof(PrefabInfo).IsAssignableFrom(t))
                {
                    var pi = v as PrefabInfo;
                    w.Name(f.Name);
                    w.Value(pi != null ? pi.name : null);
                }
            }
            w.EndObject();
        }

        private static string SafeTitle(NetInfo info)
        {
            try { return info.GetLocalizedTitle(); } catch { return null; }
        }
    }
}
