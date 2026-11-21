// cs1conv-plan.json (schema/README.md §2) as plain C# objects, plus the parser. No Unity or game types here, so the
// core builds and is tested without the game (tests/CoreTests).
using System;
using System.Collections.Generic;
using System.Globalization;
using GameBridge;

namespace Cs2Importer.Core
{
    public struct V3
    {
        public double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) { return new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static V3 operator -(V3 a, V3 b) { return new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static V3 operator *(V3 a, double k) { return new V3(a.X * k, a.Y * k, a.Z * k); }
        public double LengthXZ { get { return Math.Sqrt(X * X + Z * Z); } }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public override string ToString() { return string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", X, Y, Z); }
    }

    public sealed class PlanPrefab
    {
        public string Key, Type, Name, Source;
        /// <summary>conform | follow | walls for this type's ground segments (NOTES-road-heights.md); null = not set.</summary>
        public string HeightMode;
    }

    public sealed class PlanNode
    {
        public long Id;
        public double X, Y, Z;               // CS2 position; Y is informational in terrain-relative mode
        public double HeightAboveTerrain;
        public double Elevation;
    }

    public sealed class PlanSegment
    {
        public long Id;
        public string Prefab;                 // key into Plan.Prefabs
        public long Start, End;
        public V3[] Bezier;                   // 4 control points (CS2 x/z, CS1-derived y), or null
        public double ElevationStart, ElevationEnd;
        public string Kind = "ground";        // ground, elevated, bridge, slope, tunnel
        public bool Reversed;                 // CS1 Invert flag: start/end swapped in the plan (schema §2 "Orientation")
        /// <summary>segments[].offNode: the bezier's ends are deliberately off the node centres (CS1 Node Controller shift,
        /// docs/DECISIONS.md #107); the course keeps them (DECISIONS-nodecontroller.md).</summary>
        public bool OffNode;
        /// <summary>segments[].cs1Info: the CS1 NetInfo name (reports only).</summary>
        public string Cs1Info;
        /// <summary>segments[].halfWidth: CS1 NetInfo m_halfWidth (web 0.7.0+); NaN when absent.</summary>
        public double HalfWidth = double.NaN;
    }

    public sealed class Plan
    {
        public string Format;
        public int FormatVersion;
        public string SourceCity, SourceSave;
        public double CropCenterX, CropCenterZ, CropSize;
        public double Dx, Dz;
        public string HeightMode = "terrain-relative";
        /// <summary>plan.source.leftHandTraffic (exporter 1.2.1+): the CS1 city drove on the left; null in older plans.</summary>
        public bool? LeftHandTraffic;
        /// <summary>plan.terrain.roadConformed: the package's heightmap has the conform roads' CS1 profiles burned in.</summary>
        public bool RoadConformed;
        public readonly Dictionary<string, PlanPrefab> Prefabs = new Dictionary<string, PlanPrefab>();
        public readonly Dictionary<long, PlanNode> Nodes = new Dictionary<long, PlanNode>();
        public readonly List<PlanSegment> Segments = new List<PlanSegment>();
        public readonly List<Issue> ParseIssues = new List<Issue>();
    }

    /// <summary>A problem found in the plan or during the import. Kind is a stable slug used for counting.</summary>
    public sealed class Issue
    {
        public string Level;    // info, warning, error
        public string Kind;
        public string Message;
        public long Segment = -1, Node = -1;

        public Issue(string level, string kind, string message) { Level = level; Kind = kind; Message = message; }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object> { { "level", Level }, { "kind", Kind }, { "message", Message } };
            if (Segment >= 0) d["segment"] = Segment;
            if (Node >= 0) d["node"] = Node;
            return d;
        }
    }

    public static class PlanParser
    {
        public const string Format = "cs1conv-plan";
        public const int Version = 1;

        public static Plan Parse(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("plan is not a JSON object");
            var plan = new Plan();
            plan.Format = P.Str(root, "format", null);
            plan.FormatVersion = (int)P.Num(root, "formatVersion", 0);
            if (plan.Format != Format) throw new FormatException("not a cs1conv plan (format = '" + plan.Format + "')");
            if (plan.FormatVersion != Version) throw new FormatException("unsupported plan formatVersion " + plan.FormatVersion + " (this importer reads " + Version + ")");

            var src = P.Obj(root, "source");
            plan.SourceCity = P.Str(src, "city", null);
            plan.SourceSave = P.Str(src, "saveName", null);
            if (src != null && src.ContainsKey("leftHandTraffic") && src["leftHandTraffic"] is bool) plan.LeftHandTraffic = (bool)src["leftHandTraffic"];
            var crop = P.Obj(root, "crop");
            plan.CropCenterX = P.Num(crop, "centerX", 0);
            plan.CropCenterZ = P.Num(crop, "centerZ", 0);
            plan.CropSize = P.Num(crop, "size", 0);
            var tr = P.Obj(root, "transform");
            plan.Dx = P.Num(tr, "dx", 0);
            plan.Dz = P.Num(tr, "dz", 0);
            plan.HeightMode = P.Str(tr, "heightMode", "terrain-relative");
            if (plan.HeightMode != "terrain-relative")
                plan.ParseIssues.Add(new Issue("warning", "height-mode", "heightMode '" + plan.HeightMode + "' is not supported in M1; using terrain-relative"));

            plan.RoadConformed = P.Bool(P.Obj(root, "terrain"), "roadConformed", false);
            var prefabs = P.Obj(root, "prefabs");
            if (prefabs != null)
                foreach (var kv in prefabs)
                {
                    var o = kv.Value as Dictionary<string, object>;
                    if (o == null) continue;
                    plan.Prefabs[kv.Key] = new PlanPrefab { Key = kv.Key, Type = P.Str(o, "type", "RoadPrefab"), Name = P.Str(o, "name", null), Source = P.Str(o, "source", null), HeightMode = P.Str(o, "heightMode", null) };
                }

            var nodes = P.List(root, "nodes");
            if (nodes != null)
                foreach (var item in nodes)
                {
                    var o = item as Dictionary<string, object>;
                    if (o == null || !P.Has(o, "id")) continue;
                    var n = new PlanNode
                    {
                        Id = (long)P.Num(o, "id", -1),
                        X = P.Num(o, "x", 0), Y = P.Num(o, "y", 0), Z = P.Num(o, "z", 0),
                        HeightAboveTerrain = P.Num(o, "heightAboveTerrain", 0),
                        Elevation = P.Num(o, "elevation", 0),
                    };
                    if (plan.Nodes.ContainsKey(n.Id)) plan.ParseIssues.Add(new Issue("warning", "duplicate-node-id", "node " + n.Id + " is listed twice; the first one is used") { Node = n.Id });
                    else plan.Nodes[n.Id] = n;
                }

            var segs = P.List(root, "segments");
            if (segs != null)
                foreach (var item in segs)
                {
                    var o = item as Dictionary<string, object>;
                    if (o == null || !P.Has(o, "id")) continue;
                    var s = new PlanSegment
                    {
                        Id = (long)P.Num(o, "id", -1),
                        Prefab = P.Str(o, "prefab", null),
                        Start = (long)P.Num(o, "start", -1),
                        End = (long)P.Num(o, "end", -1),
                        ElevationStart = P.Num(o, "elevationStart", 0),
                        ElevationEnd = P.Num(o, "elevationEnd", 0),
                        Kind = P.Str(o, "kind", "ground"),
                        Cs1Info = P.Str(o, "cs1Info", null),
                        HalfWidth = P.Num(o, "halfWidth", double.NaN),
                        Reversed = P.Bool(o, "reversed", false),
                        OffNode = P.Bool(o, "offNode", false),
                        Bezier = ParseBezier(P.List(o, "bezier")),
                    };
                    plan.Segments.Add(s);
                }
            return plan;
        }

        static V3[] ParseBezier(List<object> pts)
        {
            if (pts == null || pts.Count != 4) return null;
            var r = new V3[4];
            for (int i = 0; i < 4; i++)
            {
                var p = pts[i] as List<object>;
                if (p == null || p.Count < 3) return null;
                r[i] = new V3(Num(p[0]), Num(p[1]), Num(p[2]));
            }
            return r;
        }

        static double Num(object v)
        {
            if (v is double) return (double)v;
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }
    }
}
