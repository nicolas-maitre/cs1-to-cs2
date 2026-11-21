using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;

namespace Cs1Exporter
{
    /// <summary>
    /// tmpe.json: everything TM:PE stores in the savegame, read by reflection (no compile-time dependency).
    ///
    /// Source: each TM:PE manager's ICustomDataManager&lt;List&lt;Configuration.X&gt;&gt;.SaveData(ref bool) — the exact
    /// records TM:PE serialises when the game saves, so they contain only what the user customised (no defaults).
    /// Checked by IL inspection (TM:PE 11.9.4): these methods only read state and build lists. Lane-based records are
    /// enriched with segment, lane index and the node the lane flows into, so readers don't need CS1 lane ids.
    /// Every part is isolated: a part that fails is listed in "parts" with its error and the others are still written.
    /// </summary>
    public sealed class TmpeExporter
    {
        private readonly Report _report;
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private Assembly _asm;

        public TmpeExporter(Report report) { _report = report; }

        /// <summary>False when TM:PE isn't loaded (the section is then "absent").</summary>
        public bool Available()
        {
            var t = Refl.FindType("TrafficManager.Manager.Impl.LaneArrowManager");
            _asm = t != null ? t.Assembly : null;
            return _asm != null;
        }

        private Type T(string name)
        {
            var t = _asm.GetType(name, false);
            if (t == null) throw new InvalidOperationException("type " + name + " not found in " + _asm.GetName().Name);
            return t;
        }

        private object Instance(string managerType)
        {
            var t = T(managerType);
            var inst = Refl.GetStatic(t, "Instance");
            if (inst == null) throw new InvalidOperationException(managerType + ".Instance is null");
            return inst;
        }

        /// <summary>Calls the manager's SaveData overload whose return type is List&lt;elementName&gt; (or the given full type name).</summary>
        private IEnumerable SaveData(string managerType, Func<Type, bool> returnMatches)
        {
            var inst = Instance(managerType);
            foreach (var m in inst.GetType().GetMethods(Refl.Inst))
            {
                if (!m.Name.EndsWith("SaveData", StringComparison.Ordinal)) continue;
                var ps = m.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsByRef) continue;
                if (!returnMatches(m.ReturnType)) continue;
                var args = new object[] { true };
                var r = m.Invoke(inst, args);
                if (!(bool)args[0]) _report.Warn("tmpe", managerType + "." + m.Name + " reported success = false");
                return r as IEnumerable ?? new object[0];
            }
            throw new MissingMethodException(managerType + ": no SaveData returning the expected type");
        }

        private static Func<Type, bool> ListOf(string elementName)
        {
            return t => t.IsGenericType && t.GetGenericArguments().Length == 1 && t.GetGenericArguments()[0].Name == elementName;
        }

        public void Write(JsonWriter root)
        {
            var lanes = new LaneMap();
            var segs = Singleton<NetManager>.instance.m_segments.m_buffer;
            var parts = new Dictionary<string, string>();
            var an = _asm.GetName();

            root.BeginObject();
            root.Name("assembly").BeginObject();
            root.Prop("name", an.Name);
            root.Prop("version", an.Version.ToString());
            root.EndObject();

            // Lane arrows (customised lanes only). "arrows" are TM:PE's final arrows for the lane via its own API.
            Part(root, parts, "laneArrows", w =>
            {
                var mgr = Instance("TrafficManager.Manager.Impl.LaneArrowManager");
                var final = mgr.GetType().GetMethod("GetFinalLaneArrows", new[] { typeof(uint) });
                int n = 0;
                w.BeginArray();
                foreach (var d in SaveData("TrafficManager.Manager.Impl.LaneArrowManager", ListOf("LaneArrowData")))
                {
                    uint lane = Refl.ToUInt(Refl.Get(d, "laneId"));
                    uint raw = Refl.ToUInt(Refl.Get(d, "arrows"));
                    w.BeginObject();
                    LaneProps(w, lanes, lane);
                    object fa = final != null ? final.Invoke(mgr, new object[] { lane }) : null;
                    w.Name("arrows"); FlagNames(w, fa);
                    w.Prop("savedRaw", raw);
                    w.EndObject();
                    n++;
                }
                w.EndArray();
                return n;
            });

            // Lane connections: source lane → target lane at the node given by sourceStartNode.
            Part(root, parts, "laneConnections", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var c in SaveData("TrafficManager.Manager.Impl.LaneConnection.LaneConnectionManager", ListOf("LaneConnection")))
                {
                    uint src = Refl.ToUInt(Refl.Get(c, "sourceLaneId"));
                    uint dst = Refl.ToUInt(Refl.Get(c, "targetLaneId"));
                    bool startNode = (bool)Refl.Get(c, "sourceStartNode");
                    ushort srcSeg = lanes.Segment(src), dstSeg = lanes.Segment(dst);
                    w.BeginObject();
                    w.Prop("sourceLane", src);
                    w.Prop("sourceSegment", (int)srcSeg);
                    w.Prop("sourceLaneIndex", lanes.Index(src));
                    w.Prop("targetLane", dst);
                    w.Prop("targetSegment", (int)dstSeg);
                    w.Prop("targetLaneIndex", lanes.Index(dst));
                    w.Prop("sourceStartNode", startNode);
                    w.Prop("node", srcSeg != 0 ? (int)(startNode ? segs[srcSeg].m_startNode : segs[srcSeg].m_endNode) : 0);
                    w.Name("group"); w.Any(Refl.Get(c, "group"));
                    w.EndObject();
                    n++;
                }
                w.EndArray();
                return n;
            });

            // Speed limits per lane (km/h, as TM:PE saves them: SpeedValue.ToKmphPrecise()).
            Part(root, parts, "speedLimits", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var s in SaveData("TrafficManager.Manager.Impl.SpeedLimitManager", ListOf("LaneSpeedLimit")))
                {
                    uint lane = Refl.ToUInt(Refl.Get(s, "laneId"));
                    w.BeginObject();
                    LaneProps(w, lanes, lane);
                    int kmh = Refl.ToInt(Refl.Get(s, "speedLimit"));
                    w.Prop("kmh", kmh);
                    w.Prop("unlimited", kmh >= 1000); // TM:PE SpeedValue.UNLIMITED = 20 game units = 1000 km/h
                    w.EndObject();
                    n++;
                }
                w.EndArray();
                return n;
            });

            // Custom default speed limits per NetInfo (TM:PE "default speed limits" tool), NetInfo name → game units (1 = 50 km/h).
            Part(root, parts, "defaultSpeedLimits", w =>
            {
                var d = SaveData("TrafficManager.Manager.Impl.SpeedLimitManager",
                    t => t.IsGenericType && t.GetGenericArguments().Length == 2 && t.GetGenericArguments()[0] == typeof(string));
                int n = 0;
                w.BeginObject();
                foreach (DictionaryEntry e in (IDictionary)d)
                {
                    w.Name((string)e.Key); w.Any(e.Value);
                    n++;
                }
                w.EndObject();
                return n;
            });

            // Vehicle restrictions: allowed vehicle types per lane (only lanes that differ from the default).
            Part(root, parts, "vehicleRestrictions", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var v in SaveData("TrafficManager.Manager.Impl.VehicleRestrictionsManager", ListOf("LaneVehicleTypes")))
                {
                    uint lane = Refl.ToUInt(Refl.Get(v, "laneId"));
                    var types = Refl.Get(v, "vehicleTypes");
                    w.BeginObject();
                    LaneProps(w, lanes, lane);
                    w.Name("allowed"); FlagNames(w, types);
                    w.Prop("allowedRaw", Refl.ToInt(types));
                    w.EndObject();
                    n++;
                }
                w.EndArray();
                return n;
            });

            // Junction restrictions per segment end; only flags the user set (null = game/TM:PE default) are written.
            Part(root, parts, "junctionRestrictions", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var c in SaveData("TrafficManager.Manager.Impl.JunctionRestrictionsManager", ListOf("SegmentNodeConf")))
                {
                    int seg = Refl.ToInt(Refl.Get(c, "segmentId"));
                    for (int end = 0; end < 2; end++)
                    {
                        var flags = Refl.Get(c, end == 0 ? "startNodeFlags" : "endNodeFlags");
                        if (flags == null) continue;
                        var set = new List<KeyValuePair<string, bool>>();
                        foreach (var f in flags.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            var v = f.GetValue(flags);
                            if (v is bool) set.Add(new KeyValuePair<string, bool>(f.Name, (bool)v));
                        }
                        if (set.Count == 0) continue;
                        w.BeginObject();
                        w.Prop("segment", seg);
                        w.Prop("startNode", end == 0);
                        w.Prop("node", seg > 0 && seg < segs.Length ? (int)(end == 0 ? segs[seg].m_startNode : segs[seg].m_endNode) : 0);
                        foreach (var kv in set) w.Prop(kv.Key, kv.Value);
                        w.EndObject();
                        n++;
                    }
                }
                w.EndArray();
                return n;
            });

            // Priority signs per segment end: Main / Stop / Yield.
            Part(root, parts, "prioritySigns", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var p in SaveData("TrafficManager.Manager.Impl.TrafficPriorityManager", ListOf("PrioritySegment")))
                {
                    int type = Refl.ToInt(Refl.Get(p, "priorityType"));
                    if (type == 0) continue;
                    w.BeginObject();
                    w.Prop("segment", Refl.ToInt(Refl.Get(p, "segmentId")));
                    w.Prop("node", Refl.ToInt(Refl.Get(p, "nodeId")));
                    w.Prop("type", type == 1 ? "Main" : type == 2 ? "Stop" : type == 3 ? "Yield" : "Type" + type);
                    w.EndObject();
                    n++;
                }
                w.EndArray();
                return n;
            });

            // Parking restrictions per segment direction (forward = start → end node).
            Part(root, parts, "parkingRestrictions", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var p in SaveData("TrafficManager.Manager.Impl.ParkingRestrictionsManager", ListOf("ParkingRestriction")))
                {
                    w.BeginObject();
                    w.Prop("segment", Refl.ToInt(Refl.Get(p, "segmentId")));
                    w.Prop("forwardParkingAllowed", (bool)Refl.Get(p, "forwardParkingAllowed"));
                    w.Prop("backwardParkingAllowed", (bool)Refl.Get(p, "backwardParkingAllowed"));
                    w.EndObject();
                    n++;
                }
                w.EndArray();
                return n;
            });

            // Traffic light toggles: junctions where the user added or removed a light.
            Part(root, parts, "trafficLights", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var l in SaveData("TrafficManager.Manager.Impl.TrafficLightManager", ListOf("NodeTrafficLight")))
                {
                    w.BeginObject();
                    w.Prop("node", Refl.ToInt(Refl.Get(l, "nodeId")));
                    w.Prop("trafficLight", (bool)Refl.Get(l, "trafficLight"));
                    w.EndObject();
                    n++;
                }
                w.EndArray();
                return n;
            });

            // Timed traffic lights: TM:PE's own record, written field by field (nodeId, nodeGroup, timedSteps with
            // minTime/maxTime/changeMetric/waitFlowBalance and per-segment lights keyed by segment id, then vehicle type).
            Part(root, parts, "timedTrafficLights", w =>
            {
                int n = 0;
                w.BeginArray();
                foreach (var t in SaveData("TrafficManager.Manager.Impl.TrafficLightSimulationManager", ListOf("TimedTrafficLights")))
                {
                    Refl.WriteData(w, t);
                    n++;
                }
                w.EndArray();
                return n;
            });

            root.Name("parts").BeginObject();
            foreach (var kv in parts) root.Prop(kv.Key, kv.Value);
            root.EndObject();
            root.EndObject();

            var sb = new System.Text.StringBuilder();
            foreach (var kv in Counts) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            _report.Info("tmpe", "TM:PE " + an.Version + ": " + sb);
        }

        /// <summary>Renders one part into a buffer first, so a failing part leaves valid JSON (null) behind.</summary>
        private void Part(JsonWriter outer, Dictionary<string, string> parts, string name, Func<JsonWriter, int> body)
        {
            var sw = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            var inner = new JsonWriter(sw);
            try
            {
                int n = body(inner);
                inner.Dispose();
                outer.Name(name);
                outer.RawValue(sw.ToString());
                parts[name] = "ok";
                Counts[name] = n;
            }
            catch (Exception e)
            {
                outer.Name(name).Null();
                parts[name] = "failed: " + Report.Describe(e);
                _report.Warn("tmpe", name + " failed: " + Report.Describe(e));
            }
        }

        private void LaneProps(JsonWriter w, LaneMap lanes, uint lane)
        {
            w.Prop("lane", lane);
            w.Prop("segment", (int)lanes.Segment(lane));
            w.Prop("laneIndex", lanes.Index(lane));
            w.Prop("node", (int)lanes.EndNode(lane));
        }

        /// <summary>A [Flags] enum value as an array of its set flag names (combined aliases skipped).</summary>
        private static void FlagNames(JsonWriter w, object value)
        {
            w.BeginArray();
            if (value != null && value.GetType().IsEnum)
            {
                long v = Convert.ToInt64(value);
                foreach (var f in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    long fv = Convert.ToInt64(f.GetValue(null));
                    if (fv == 0 || (fv & (fv - 1)) != 0) continue; // single bits only
                    if ((v & fv) == fv) w.Value(f.Name);
                }
            }
            w.EndArray();
        }
    }
}
