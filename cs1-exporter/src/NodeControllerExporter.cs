using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Cs1Exporter
{
    /// <summary>
    /// nodecontroller.json: Node Controller Renewal data, read by reflection (no compile-time dependency).
    ///
    /// Object graph (Node Controller Renewal 3.5, by metadata inspection):
    ///   ModsCommon.SingletonManager&lt;NodeController.Manager&gt;.Instance.Buffer : NodeData[] indexed by node id
    ///   NodeData: Type (NodeStyleType), Mode, Offset, Shift, RotateAngle, SlopeAngle, TwistAngle, Stretch, NoMarkings, …
    ///   NodeData.SegmentEnds : Dictionary&lt;ushort segmentId, SegmentEndData&gt; with the per-segment-end settings.
    /// Each mod by this author ships its own ModsCommon copy, so the singleton type is taken from the Node Controller
    /// assembly itself. Only the settings a user can change are written (a fixed property list); derived geometry
    /// and UI state are skipped. A property that is missing in another NC version is written as null.
    /// </summary>
    public sealed class NodeControllerExporter
    {
        private readonly Report _report;
        public int Nodes, SegmentEnds;
        public readonly Dictionary<string, int> ByType = new Dictionary<string, int>();
        private Type _manager;

        public NodeControllerExporter(Report report) { _report = report; }

        private static readonly string[] NodeProps =
        {
            "Type", "DefaultType", "Mode", "Offset", "Shift", "RotateAngle", "SlopeAngle", "TwistAngle", "Stretch", "StretchPercent",
            "Gap", "NoMarkings", "Collision", "ForceNodeLess", "FollowSlope", "IsEnd", "IsJunction", "IsTwoRoads", "IsUnderground",
        };

        private static readonly string[] EndProps =
        {
            "IsStartNode", "Mode", "Offset", "LeftOffset", "RightOffset", "MinOffset", "MaxOffset", "RotateAngle", "SlopeAngle",
            "TwistAngle", "Shift", "Stretch", "StretchPercent", "DeltaHeight", "NoMarkings", "Collision", "ForceNodeLess",
            "FollowSlope", "LeftFlatEnd", "RightFlatEnd", "KeepDefaults", "IsNodeLess", "ShouldHideCrossingTexture",
            "WidthRatio", "HeightRatio", "IsMainRoad",
        };

        public bool Available()
        {
            _manager = Refl.FindType("NodeController.Manager");
            return _manager != null;
        }

        public void Write(JsonWriter w)
        {
            var singleton = _manager.Assembly.GetType("ModsCommon.SingletonManager`1", false);
            if (singleton == null) throw new InvalidOperationException("ModsCommon.SingletonManager`1 not found in " + _manager.Assembly.GetName().Name);
            var mgr = singleton.MakeGenericType(_manager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            if (mgr == null) throw new InvalidOperationException("Node Controller manager instance is null");
            var buffer = Refl.Get(mgr, "Buffer") as IList;
            if (buffer == null) throw new InvalidOperationException("Node Controller Manager.Buffer not found");
            var an = _manager.Assembly.GetName();

            w.BeginObject();
            w.Name("assembly").BeginObject();
            w.Prop("name", an.Name);
            w.Prop("version", an.Version.ToString());
            w.EndObject();
            w.Name("nodes").BeginArray();
            int failed = 0;
            for (int id = 1; id < buffer.Count; id++)
            {
                var data = buffer[id];
                if (data == null) continue;
                string json;
                int ends = 0;
                string type = null;
                try
                {
                    var sw = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                    var one = new JsonWriter(sw);
                    one.BeginObject();
                    one.Prop("id", id);
                    foreach (var p in NodeProps) Prop(one, data, p);
                    type = SafeStr(Refl.Get(data, "Type"));
                    one.Name("segmentEnds").BeginArray();
                    var dict = Refl.Get(data, "SegmentEnds") as IDictionary;
                    if (dict != null)
                    {
                        var keys = new List<int>();
                        foreach (var k in dict.Keys) keys.Add(Convert.ToInt32(k));
                        keys.Sort();
                        foreach (var k in keys)
                        {
                            var end = dict[(ushort)k];
                            if (end == null) continue;
                            one.BeginObject();
                            one.Prop("segment", k);
                            foreach (var p in EndProps) Prop(one, end, p);
                            one.EndObject();
                            ends++;
                        }
                    }
                    one.EndArray();
                    one.EndObject();
                    one.Dispose();
                    json = sw.ToString();
                }
                catch (Exception e)
                {
                    if (++failed <= 10) _report.Warn("nodecontroller", "node " + id + " skipped: " + Report.Describe(e));
                    continue;
                }
                w.RawValue(json);
                Nodes++;
                SegmentEnds += ends;
                if (type != null) { int c; ByType.TryGetValue(type, out c); ByType[type] = c + 1; }
            }
            w.EndArray();
            w.EndObject();

            var sb = new System.Text.StringBuilder();
            foreach (var kv in ByType) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            _report.Info("nodecontroller", "Node Controller " + an.Version + ": " + Nodes + " nodes, " + SegmentEnds + " segment ends; types " + sb
                + (failed > 0 ? "; " + failed + " failed" : ""));
        }

        private static void Prop(JsonWriter w, object o, string name)
        {
            object v;
            try { v = Refl.Get(o, name); }
            catch { v = null; }
            w.Name(char.ToLowerInvariant(name[0]) + name.Substring(1));
            w.Any(v);
        }

        private static string SafeStr(object o) { try { return o != null ? o.ToString() : null; } catch { return null; } }
    }
}
