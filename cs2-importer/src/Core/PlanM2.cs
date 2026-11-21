// The plan's M2 sections (schema/README.md §2): zoning and traffic. Parsed separately from the road plan so a v1
// reader (and the road import) never depends on them.
using System;
using System.Collections.Generic;
using System.Globalization;
using GameBridge;

namespace Cs2Importer.Core
{
    public struct ZoneCell
    {
        public float X, Z;
        public int Prefab;   // index into ZoningPlan.Prefabs
    }

    public sealed class ZoningPlan
    {
        public string Theme;
        public double CellSize = 8;
        public readonly List<string> Prefabs = new List<string>();
        public readonly List<ZoneCell> Cells = new List<ZoneCell>();
    }

    public sealed class TrafficNode
    {
        public long Id;
        public bool? TrafficLights;
        public bool AllWayStop;
    }

    public sealed class TrafficSegmentEnd
    {
        public long Segment, Node;
        public string Priority;               // main, stop, yield or null
        public readonly List<string> Forbid = new List<string>();  // left, straight, right
        public bool? Crosswalk;
        public bool? UTurn;
    }

    public sealed class TrafficPlan
    {
        public readonly List<TrafficNode> Nodes = new List<TrafficNode>();
        public readonly List<TrafficSegmentEnd> SegmentEnds = new List<TrafficSegmentEnd>();
        public int LaneConnections, LaneArrows;
    }

    public sealed class PlanM2
    {
        public ZoningPlan Zoning;    // null when the plan has none
        public TrafficPlan Traffic;  // null when the plan has none

        public static PlanM2 Parse(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("plan is not a JSON object");
            var m = new PlanM2();
            var z = P.Obj(root, "zoning");
            if (z != null)
            {
                m.Zoning = new ZoningPlan { Theme = P.Str(z, "theme", "NA"), CellSize = P.Num(z, "cellSize", 8) };
                var pf = P.List(z, "prefabs");
                if (pf != null) foreach (var o in pf) m.Zoning.Prefabs.Add(o as string);
                var cells = P.List(z, "cells");
                if (cells != null)
                    foreach (var o in cells)
                    {
                        var c = o as List<object>;
                        if (c == null || c.Count < 3) continue;
                        m.Zoning.Cells.Add(new ZoneCell { X = (float)Num(c[0]), Z = (float)Num(c[1]), Prefab = (int)Num(c[2]) });
                    }
            }
            var t = P.Obj(root, "traffic");
            if (t != null)
            {
                m.Traffic = new TrafficPlan();
                var nodes = P.List(t, "nodes");
                if (nodes != null)
                    foreach (var o in nodes)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null || !P.Has(d, "id")) continue;
                        m.Traffic.Nodes.Add(new TrafficNode
                        {
                            Id = (long)P.Num(d, "id", -1),
                            TrafficLights = P.Has(d, "trafficLights") ? (bool?)P.Bool(d, "trafficLights", false) : null,
                            AllWayStop = P.Bool(d, "allWayStop", false),
                        });
                    }
                var ends = P.List(t, "segmentEnds");
                if (ends != null)
                    foreach (var o in ends)
                    {
                        var d = o as Dictionary<string, object>;
                        if (d == null) continue;
                        var e = new TrafficSegmentEnd
                        {
                            Segment = (long)P.Num(d, "segment", -1),
                            Node = (long)P.Num(d, "node", -1),
                            Priority = P.Str(d, "priority", null),
                            Crosswalk = P.Has(d, "crosswalk") ? (bool?)P.Bool(d, "crosswalk", true) : null,
                            UTurn = P.Has(d, "uturn") ? (bool?)P.Bool(d, "uturn", true) : null,
                        };
                        var f = P.List(d, "forbid");
                        if (f != null) foreach (var x in f) if (x is string s) e.Forbid.Add(s);
                        m.Traffic.SegmentEnds.Add(e);
                    }
                m.Traffic.LaneConnections = P.List(t, "laneConnections")?.Count ?? 0;
                m.Traffic.LaneArrows = P.List(t, "laneArrows")?.Count ?? 0;
            }
            return m;
        }

        static double Num(object v) { return v is double d ? d : Convert.ToDouble(v, CultureInfo.InvariantCulture); }
    }
}
