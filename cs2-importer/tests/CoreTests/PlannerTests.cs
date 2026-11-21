using System;
using System.IO;
using System.Linq;
using System.Text;
using Cs2Importer.Core;

namespace CoreTests
{
    public static class PlannerTests
    {
        // A tiny plan as the web tool writes it (schema/README.md §2).
        const string Plan = @"{
  ""format"": ""cs1conv-plan"", ""formatVersion"": 1,
  ""source"": { ""city"": ""T"", ""saveName"": ""T.01"" },
  ""crop"": { ""centerX"": 100, ""centerZ"": -50, ""size"": 1000 },
  ""transform"": { ""dx"": -100, ""dz"": 50, ""heightMode"": ""terrain-relative"" },
  ""prefabs"": { ""v:Small Road"": { ""type"": ""RoadPrefab"", ""name"": ""Small Road"", ""source"": ""Basic Road"" } },
  ""nodes"": [
    { ""id"": 1, ""x"": 0, ""z"": 0, ""y"": 50, ""heightAboveTerrain"": 0.07, ""elevation"": 0 },
    { ""id"": 2, ""x"": 100, ""z"": 0, ""y"": 50, ""heightAboveTerrain"": -0.2, ""elevation"": 0 },
    { ""id"": 3, ""x"": 100, ""z"": 100, ""y"": 62, ""heightAboveTerrain"": 12, ""elevation"": 12 },
    { ""id"": 4, ""x"": 100.01, ""z"": 100, ""y"": 62, ""heightAboveTerrain"": 12, ""elevation"": 12 },
    { ""id"": 5, ""x"": 9000, ""z"": 0, ""y"": 50, ""heightAboveTerrain"": 0, ""elevation"": 0 }
  ],
  ""segments"": [
    { ""id"": 10, ""prefab"": ""v:Small Road"", ""start"": 1, ""end"": 2, ""bezier"": [[0,50,0],[33,50,10],[66,50,10],[100,50,0]], ""elevationStart"": 0, ""elevationEnd"": 0, ""kind"": ""ground"" },
    { ""id"": 11, ""prefab"": ""v:Small Road"", ""start"": 2, ""end"": 3, ""bezier"": [[100,50,0],[100,60,33],[100,70,66],[100,62,100]], ""elevationStart"": 0, ""elevationEnd"": 12, ""kind"": ""elevated"" },
    { ""id"": 12, ""prefab"": ""v:Small Road"", ""start"": 2, ""end"": 1, ""kind"": ""ground"" },
    { ""id"": 13, ""prefab"": ""v:Missing"", ""start"": 1, ""end"": 3 },
    { ""id"": 14, ""prefab"": ""v:Small Road"", ""start"": 1, ""end"": 99 },
    { ""id"": 15, ""prefab"": ""v:Small Road"", ""start"": 2, ""end"": 5 },
    { ""id"": 16, ""prefab"": ""v:Small Road"", ""start"": 3, ""end"": 4 },
    { ""id"": 17, ""prefab"": ""v:Small Road"", ""start"": 4, ""end"": 1, ""elevationStart"": 12, ""elevationEnd"": 0, ""kind"": ""elevated"" }
  ],
  ""stats"": { ""segments"": 8, ""skipped"": 0 }
}";

        static ImportJob Job(ImportOptions o = null) { return ImportPlanner.Build(PlanParser.Parse(Plan), o ?? new ImportOptions()); }

        public static void TestParse()
        {
            var p = PlanParser.Parse(Plan);
            Assert.Eq(5, p.Nodes.Count, "nodes");
            Assert.Eq(8, p.Segments.Count, "segments");
            Assert.Eq("Small Road", p.Prefabs["v:Small Road"].Name, "prefab name");
            Assert.Eq(-100.0, p.Dx, "dx");
            Assert.Eq(1000.0, p.CropSize, "crop size");
            Assert.True(p.Segments[0].Bezier != null && p.Segments[0].Bezier[1].Z == 10, "bezier parsed");
            Assert.True(p.Segments[2].Bezier == null, "missing bezier is null");
        }

        public static void TestRejectsWrongFormat()
        {
            Assert.Throws<FormatException>(() => PlanParser.Parse(@"{""format"":""cs1x"",""formatVersion"":1}"), "format");
            Assert.Throws<FormatException>(() => PlanParser.Parse(@"{""format"":""cs1conv-plan"",""formatVersion"":2}"), "version");
        }

        public static void TestSkipsInvalidSegments()
        {
            var j = Job();
            var kinds = j.Skipped.ToDictionary(i => i.Segment, i => i.Kind);
            Assert.Eq("unknown-prefab-key", kinds[13], "seg 13");
            Assert.Eq("missing-node", kinds[14], "seg 14");
            Assert.Eq("outside-cs2-map", kinds[15], "seg 15");
            Assert.Eq("too-short", kinds[16], "seg 16 (1 cm)");
            Assert.Eq(4, j.Skipped.Count, "skipped count");
            Assert.Eq(4, j.Segments.Count, "segments to build: 10, 11, 12, 17");
            Assert.True(!j.Nodes.ContainsKey(5), "node 5 only used by a skipped segment");
        }

        public static void TestWarnings()
        {
            var j = Job();
            Assert.True(j.Warnings.Any(w => w.Kind == "parallel-segments" && w.Segment == 12), "10 and 12 join the same nodes");
            Assert.True(j.Warnings.Any(w => w.Kind == "merged-nodes"), "nodes 3 and 4 are 1 cm apart");
        }

        public static void TestElevationNormalisation()
        {
            var j = Job();
            Assert.Eq(0.0, j.Nodes[1].Elevation, "ground node (its CS1 heightAboveTerrain 0.07 is ignored)");
            Assert.Eq(0.0, j.Nodes[2].Elevation, "ground node at the foot of the ramp (segment 11 starts at 0)");
            Assert.Eq(12.0, j.Nodes[3].Elevation, "elevated node");
            var s11 = j.Segments.First(s => s.Id == 11);
            Assert.Eq(6.0, s11.MiddleElevation, "middle elevation is the mean of the ends");
            Assert.Eq(0.0, ImportPlanner.NormalizeElevation(double.NaN, 1), "NaN");
            Assert.Eq(-12.0, ImportPlanner.NormalizeElevation(-12, 1), "tunnel");
        }

        public static void TestGroundNodeOffsetsIgnored()
        {
            // a CS1 ground road on an embankment: node heightAboveTerrain 2.5 / -1.7, ground segment ends 0
            var j = ImportPlanner.Build(PlanParser.Parse(@"{""format"":""cs1conv-plan"",""formatVersion"":1,""prefabs"":{""p"":{""name"":""Small Road""}},
              ""nodes"":[{""id"":1,""x"":0,""z"":0,""heightAboveTerrain"":2.5},{""id"":2,""x"":50,""z"":0,""heightAboveTerrain"":-1.7},{""id"":3,""x"":100,""z"":0,""heightAboveTerrain"":10}],
              ""segments"":[{""id"":1,""prefab"":""p"",""start"":1,""end"":2,""kind"":""ground""},{""id"":2,""prefab"":""p"",""start"":2,""end"":3,""elevationStart"":0,""elevationEnd"":10.3,""kind"":""elevated""}]}"), null);
            Assert.Eq(0.0, j.Nodes[1].Elevation, "embankment node is ground");
            Assert.Eq(0.0, j.Nodes[2].Elevation, "ramp foot is ground");
            Assert.Eq(10.3, j.Nodes[3].Elevation, "elevated end from the segment");
            Assert.Eq(5.15, j.Segments[1].MiddleElevation, "middle");
        }

        public static void TestTerrainFeaturesKept()
        {
            // a CS1 highway in an 11 m trench (ground segments) crossed by a road at ground level
            var j = ImportPlanner.Build(PlanParser.Parse(@"{""format"":""cs1conv-plan"",""formatVersion"":1,""prefabs"":{""p"":{""name"":""Small Road""}},
              ""nodes"":[{""id"":1,""x"":0,""z"":0,""heightAboveTerrain"":-11.2},{""id"":2,""x"":90,""z"":0,""heightAboveTerrain"":-11.0},{""id"":3,""x"":180,""z"":0,""heightAboveTerrain"":-3.9},{""id"":4,""x"":0,""z"":50,""heightAboveTerrain"":6},{""id"":5,""x"":0,""z"":-90,""heightAboveTerrain"":-20}],
              ""segments"":[{""id"":1,""prefab"":""p"",""start"":1,""end"":2,""kind"":""ground""},{""id"":2,""prefab"":""p"",""start"":2,""end"":3,""kind"":""ground""},{""id"":3,""prefab"":""p"",""start"":1,""end"":4,""kind"":""ground""},{""id"":4,""prefab"":""p"",""start"":1,""end"":5,""kind"":""ground""}]}"), null);
            Assert.Eq(-11.2, j.Nodes[1].Elevation, "trench node keeps its depth");
            Assert.Eq(-11.0, j.Nodes[2].Elevation, "trench node");
            Assert.Eq(0.0, j.Nodes[3].Elevation, "-3.9 is under the 4 m threshold: ground");
            Assert.Eq(6.0, j.Nodes[4].Elevation, "embankment node keeps its height");
            Assert.Eq(-11.1, j.Segments[0].MiddleElevation, "trench segment middle");
            Assert.Eq(-11.5, j.Nodes[5].Elevation, "a 20 m trench is clamped to -11.5 so CS2 keeps it an open cut");
        }

        public static void TestOffsetMovesNodesAndCurves()
        {
            var j = Job(new ImportOptions { OffsetX = 2000, OffsetZ = -4000 });
            Assert.Eq(2100.0, j.Nodes[2].X, "node x shifted");
            Assert.Eq(-4000.0, j.Nodes[2].Z, "node z shifted");
            var s = j.Segments.First(x => x.Id == 10);
            var c = ImportPlanner.Curve(s, new V3(j.Nodes[1].X, 0, j.Nodes[1].Z), new V3(j.Nodes[2].X, 0, j.Nodes[2].Z));
            Assert.Near(2033, c[1].X, 1e-9, "inner point shifted with its end"); Assert.Near(-3990, c[1].Z, 1e-9, "inner z");
            Assert.True(ImportPlanner.Build(PlanParser.Parse(Plan), new ImportOptions { OffsetX = 7100 }).Skipped.Any(i => i.Kind == "outside-cs2-map" && i.Segment == 10), "offset pushes segment 10 off the map");
        }

        public static void TestNodesShareOneObject()
        {
            var j = Job();
            var at2 = j.Segments.Where(s => s.Start.Id == 2 || s.End.Id == 2).Select(s => s.Start.Id == 2 ? s.Start : s.End).Distinct().ToList();
            Assert.Eq(1, at2.Count, "every segment at node 2 uses the same NodeJob (one CS2 position per CS1 node)");
            Assert.Eq(3, j.Nodes[2].Uses, "node 2 uses");
        }

        public static void TestGroundCurveIsFlatAndKeepsXZ()
        {
            var j = Job();
            var s = j.Segments.First(x => x.Id == 10);
            var c = ImportPlanner.Curve(s, new V3(0, 5, 0), new V3(100, 8, 0));
            Assert.Near(33, c[1].X, 1e-9, "b.x"); Assert.Near(10, c[1].Z, 1e-9, "b.z");
            Assert.Near(66, c[2].X, 1e-9, "c.x"); Assert.Near(10, c[2].Z, 1e-9, "c.z");
            Assert.Near(6, c[1].Y, 1e-9, "b.y on the straight line between the CS2 ends");
            Assert.Near(7, c[2].Y, 1e-9, "c.y");
            Assert.Eq(0.0, c[0].X, "a is the start node"); Assert.Eq(8.0, c[3].Y, "d is the end node");
        }

        public static void TestElevatedCurveKeepsBulge()
        {
            var j = Job();
            var s = j.Segments.First(x => x.Id == 11);
            // CS1: 50 -> 62, control y 60 and 70: bulges +6 and +12 over the straight line (54, 58)
            var c = ImportPlanner.Curve(s, new V3(100, 0, 0), new V3(100, 12, 100));
            Assert.Near(4 + 6, c[1].Y, 1e-9, "b.y = lerp + bulge");
            Assert.Near(8 + 12, c[2].Y, 1e-9, "c.y = lerp + bulge");
        }

        public static void TestCurveShiftsWithEnds()
        {
            var j = Job();
            var s = j.Segments.First(x => x.Id == 10);
            var c = ImportPlanner.Curve(s, new V3(1, 0, 2), new V3(100, 0, 0));
            Assert.Near(34, c[1].X, 1e-9, "b moves with a"); Assert.Near(12, c[1].Z, 1e-9, "b.z moves with a");
            Assert.Near(66, c[2].X, 1e-9, "c stays with d");
        }

        public static void TestStraightWithoutBezier()
        {
            var j = Job();
            var s = j.Segments.First(x => x.Id == 12);
            var c = ImportPlanner.Curve(s, new V3(100, 0, 0), new V3(0, 3, 0));
            Assert.Near(66.666, c[1].X, 1e-2, "b.x"); Assert.Near(1, c[1].Y, 1e-9, "b.y");
        }

        public static void TestBatchesCoverEverySegmentOnce()
        {
            var j = Job(new ImportOptions { BatchSize = 3 });
            var ids = j.Batches.SelectMany(b => b).Select(s => s.Id).ToList();
            Assert.Eq(j.Segments.Count, ids.Count, "count");
            Assert.Eq(ids.Count, ids.Distinct().Count(), "no duplicates");
            Assert.True(j.Batches.All(b => b.Count <= 3), "batch size");
            Assert.Eq(2, j.Batches.Count, "4 segments / 3");
        }

        public static void TestSpatialOrder()
        {
            // 40 segments on a line along x across 4 cells of 100 m, listed scrambled: batches of 10 stay inside one cell
            var sb = new StringBuilder(@"{""format"":""cs1conv-plan"",""formatVersion"":1,""prefabs"":{""p"":{""type"":""RoadPrefab"",""name"":""Small Road""}},""nodes"":[");
            for (int i = 0; i <= 40; i++) sb.Append((i > 0 ? "," : "") + @"{""id"":" + i + @",""x"":" + (i * 10 + 0.5) + @",""z"":5}");
            sb.Append(@"],""segments"":[");
            var order = Enumerable.Range(0, 40).OrderBy(i => (i * 17) % 40).ToList();
            for (int k = 0; k < 40; k++) { int i = order[k]; sb.Append((k > 0 ? "," : "") + @"{""id"":" + (100 + i) + @",""prefab"":""p"",""start"":" + i + @",""end"":" + (i + 1) + "}"); }
            sb.Append("]}");
            var j = ImportPlanner.Build(PlanParser.Parse(sb.ToString()), new ImportOptions { BatchSize = 10, CellSize = 100 });
            Assert.Eq(4, j.Batches.Count, "batches");
            foreach (var b in j.Batches)
            {
                var cells = b.Select(s => (int)Math.Floor((s.Start.X + s.End.X) / 2 / 100)).Distinct().Count();
                Assert.Eq(1, cells, "one cell per batch");
            }
        }

        public static void TestHandmadeSamplePlanIsValid()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "m1-handmade", "cs1conv-plan.json");
            if (!File.Exists(path)) throw new Exception("sample plan missing: " + Path.GetFullPath(path));
            var j = ImportPlanner.Build(PlanParser.Parse(File.ReadAllText(path)), new ImportOptions());
            Assert.Eq(0, j.Skipped.Count, "nothing skipped in the hand-made plan");
            Assert.True(j.Segments.Count >= 20, "hand-made plan has the grid, curve, bridge, tunnel and RB road");
            Assert.True(!j.Warnings.Any(w => w.Level == "warning"), "no warnings: " + string.Join("; ", j.Warnings.Select(w => w.Message)));
        }
    }
}
