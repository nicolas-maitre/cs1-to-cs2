// Height modes per road type (NOTES-road-heights.md, DECISIONS.md #110-112): conform / follow / walls, the node rule
// where they meet, and plans without modes keeping the M2 rules.
using System.Globalization;
using Cs2Importer.Core;

namespace CoreTests
{
    public static class HeightModeTests
    {
        /// <summary>Two ground segments 1-2 (prefab a) and 2-3 (prefab b); node 2 is hat m above (+) / below (-) CS1's terrain.</summary>
        static ImportJob Job(string modeA, string modeB, double hat, bool conformed = false, double hump = 0, double split = 1.5)
        {
            string f(double v) => v.ToString(CultureInfo.InvariantCulture);
            string pf(string key, string mode) => @"""" + key + @""": { ""type"": ""RoadPrefab"", ""name"": ""Small Road""" + (mode != null ? @", ""heightMode"": """ + mode + @"""" : "") + " }";
            double y2 = 100 + hat; // CS1 road height at node 2 (terrain is 100 everywhere in CS1)
            var json = @"{ ""format"": ""cs1conv-plan"", ""formatVersion"": 1, ""source"": { ""city"": ""C"" },
  ""terrain"": { ""roadConformed"": " + (conformed ? "true" : "false") + @" },
  ""prefabs"": { " + pf("a", modeA) + ", " + pf("b", modeB) + @" },
  ""nodes"": [ { ""id"": 1, ""x"": 0, ""z"": 0, ""y"": 100, ""heightAboveTerrain"": 0 },
               { ""id"": 2, ""x"": 100, ""z"": 0, ""y"": " + f(y2) + @", ""heightAboveTerrain"": " + f(hat) + @" },
               { ""id"": 3, ""x"": 200, ""z"": 0, ""y"": 100, ""heightAboveTerrain"": 0 } ],
  ""segments"": [ { ""id"": 1, ""prefab"": ""a"", ""start"": 1, ""end"": 2, ""kind"": ""ground"", ""bezier"": [[0,100,0],[33," + f(100 + hump) + @",0],[66," + f(y2 + hump) + @",0],[100," + f(y2) + @",0]] },
                  { ""id"": 2, ""prefab"": ""b"", ""start"": 2, ""end"": 3, ""kind"": ""ground"" } ] }";
            return ImportPlanner.Build(PlanParser.Parse(json), new ImportOptions { ProfileSplit = split });
        }

        public static void TestNoModesKeepTheM2Rules()
        {
            Assert.Eq(-6.0, Job(null, null, -6).Nodes[2].Elevation, "trench kept");
            Assert.Eq(0.0, Job(null, null, 5).Nodes[2].Elevation, "embankment snapped");
            Assert.Eq(0.0, Job(null, null, -2).Nodes[2].Elevation, "small offset is ground");
        }

        public static void TestConform()
        {
            var j = Job("conform", "conform", -6);
            Assert.Eq(0.0, j.Nodes[2].Elevation, "on the burned terrain = CS1 height");
            Assert.True(j.Nodes[2].Conformed, "conformed");
            Assert.Eq(0.0, Job("conform", "conform", 5).Nodes[2].Elevation, "embankment: the ground rises to it");
        }

        public static void TestRoadConformedPackageDefaultsToConform()
        {
            var j = Job(null, null, -6, conformed: true);
            Assert.Eq("conform", j.Segments[0].HeightMode, "default mode");
            Assert.Eq(0.0, j.Nodes[2].Elevation, "conformed");
        }

        public static void TestFollow()
        {
            Assert.Eq(0.0, Job("follow", "follow", -6).Nodes[2].Elevation, "on the natural terrain, no trench");
            Assert.Eq(0.0, Job("follow", "follow", 5).Nodes[2].Elevation, "no embankment");
        }

        public static void TestWalls()
        {
            Assert.Eq(-6.0, Job("walls", "walls", -6).Nodes[2].Elevation, "CS1 height: lowered, with walls");
            Assert.Eq(5.0, Job("walls", "walls", 5).Nodes[2].Elevation, "CS1 height: raised, with walls (no snapping)");
            Assert.Eq(0.0, Job("walls", "walls", 0.5).Nodes[2].Elevation, "within the ground snap");
        }

        /// <summary>#61 (DECISIONS.md #129, replaces #123's unclamped walls): CS1's elevation kind decides the composition
        /// family, so a CS1 ground road in walls mode is never a tunnel or on pillars. Beyond CS2's wall limits it stays at
        /// the limit and is reported; the package's terrain beside it (the walls burn) is what gives it its CS1 height.</summary>
        public static void TestGroundRoadsStayWallsBeyondTheLimits()
        {
            var up = Job("walls", "walls", 25.1);
            Assert.Eq(7.5, up.Nodes[2].Elevation, "CacaoCity's ramp top (+25.1 over the natural terrain): a quay at the limit, not pillars");
            Assert.True(up.Nodes[2].WallsGround, "a walls ground node");
            Assert.True(up.Warnings.Exists(w => w.Kind == "walls-beyond-limits"), "reported");
            Assert.Eq(-11.5, Job("walls", "walls", -20).Nodes[2].Elevation, "a 20 m CS1 trench: walls at the limit, not a tunnel");
            Assert.Eq(-11.5, Job("walls", "walls", -12).Nodes[2].Elevation, "an open 12 m trench stays open");
            Assert.Eq(7.5, Job("walls", "follow", 12).Nodes[2].Elevation, "walls over follow: at the limit");
            Assert.True(!Job("walls", "walls", -12).Warnings.Exists(w => w.Kind == "walls-beyond-limits"), "within the 1 m tolerance: not reported");
            Assert.Eq(0.0, Job(null, null, 25.1).Nodes[2].Elevation, "plans without modes keep the M2 rules");
            Assert.Eq(-11.5, Job(null, null, -20).Nodes[2].Elevation, "M2 trench clamp unchanged");
        }

        /// <summary>#61: in game, a walls ground node takes CS1's height over the real terrain, within the wall limits. With
        /// the walls burn (the ground beside the ramp raised to 7.5 m below the road) it is at its CS1 height; without it,
        /// it stays at the limit and is listed as short.</summary>
        public static void TestWallsNodesUseTheReshapedTerrain()
        {
            var j = Job("walls", "walls", 25.1, conformed: true, split: 0);
            System.Func<double, double, double> raised = (x, z) => x == 100 ? 117.6 : 100;   // CS1 road 125.1, ground 7.5 below
            ImportPlanner.KeepHeightsOnConformedTerrain(j, raised);
            Assert.Eq(7.5, j.Nodes[2].Elevation, "7.5 m above the raised ground = the CS1 height");
            Assert.Eq(0, j.WallsShort.Count, "nothing short");
            var k = Job("walls", "walls", 25.1, conformed: true, split: 0);
            ImportPlanner.KeepHeightsOnConformedTerrain(k, (x, z) => 100);
            Assert.Eq(7.5, k.Nodes[2].Elevation, "no burn: at the limit");
            Assert.Eq(1, k.WallsShort.Count, "listed");
            Assert.Eq(17.6, (double)k.WallsShort[0]["short_by"], "17.6 m short of its CS1 height");
            var t = Job("walls", "walls", -20, conformed: true, split: 0);
            ImportPlanner.KeepHeightsOnConformedTerrain(t, (x, z) => x == 100 ? 91.5 : 100); // trench burn: ground lowered to 11.5 above
            Assert.Eq(-11.5, t.Nodes[2].Elevation, "trench at its CS1 height below the lowered ground");
            Assert.Eq(0, t.WallsShort.Count, "nothing short");
        }

        /// <summary>#57/#61: on road-conformed terrain a walls node takes its CS1 height over the real terrain: a 6 m cut
        /// under a +7 m node asks for 13 m, beyond the wall limit, so it stays at 7.5 and is listed.</summary>
        public static void TestNodesKeepCs1HeightOnReshapedTerrain()
        {
            // natural terrain 100 everywhere; the burn cut 6 m at node 2
            System.Func<double, double, double> cut = (x, z) => x == 100 ? 94 : 100;
            var j = Job("walls", "walls", 7, conformed: true);
            Assert.Eq(1, ImportPlanner.KeepHeightsOnConformedTerrain(j, cut), "one node moved");
            Assert.Eq(7.5, j.Nodes[2].Elevation, "107 m (CS1) over a 94 m cut wants 13: walls at the limit");
            Assert.Eq(7.5, j.Nodes[2].PositionHeight, "placed there");
            Assert.Eq(1, j.WallsShort.Count, "listed as short");
            Assert.Eq(3.75, j.Segments[0].MiddleElevation, "middle elevation follows");
            Assert.Eq(0.0, j.Nodes[1].Elevation, "ground node left alone");
            var c = Job("conform", "conform", -6, conformed: true);
            ImportPlanner.KeepHeightsOnConformedTerrain(c, cut);
            Assert.Eq(0.0, c.Nodes[2].Elevation, "conformed node left alone (it is the terrain)");
            var plain = Job("walls", "walls", 7);
            Assert.Eq(0, ImportPlanner.KeepHeightsOnConformedTerrain(plain, cut), "natural terrain: nothing to keep");
            Assert.Eq(7.0, plain.Nodes[2].Elevation, "unchanged");
        }

        /// <summary>#57: a walls segment between two ground-level ends that crosses a dip on a CS1 embankment keeps CS1's
        /// height in the middle (CacaoCity 25425: 9 m dip, CS2 had laid the road in it).</summary>
        public static void TestWallsKeepsTheMiddleOverADip()
        {
            var j = Job("walls", "walls", 0, hump: 0);
            // terrain 100 everywhere except a 9 m dip around x = 50 (segment 1's midpoint); CS1 road flat at 100
            System.Func<double, double, double> dip = (x, z) => System.Math.Abs(x - 50) < 10 ? 91 : 100;
            Assert.Eq(1, ImportPlanner.KeepWallsProfile(j, dip), "one segment");
            Assert.Eq(7.5, j.Segments[0].MiddleElevation, "middle 9 m above the dip, within the wall limit (#61)");
            Assert.Eq(0.0, j.Segments[1].MiddleElevation, "segment without a bezier unchanged");
            var f = Job("follow", "follow", 0);
            Assert.Eq(0, ImportPlanner.KeepWallsProfile(f, dip), "follow segments lie on the terrain");
        }

        /// <summary>#61: the in-game height passes run only on the package's terrain (not at a flat test spot).</summary>
        public static void TestTerrainIsThePlans()
        {
            var j = Job("walls", "walls", 7, conformed: true);    // CS1 natural terrain 100 everywhere
            Assert.True(ImportPlanner.TerrainIsThePlans(j, (x, z) => 100.4), "the package's terrain");
            Assert.True(ImportPlanner.TerrainIsThePlans(j, (x, z) => x == 100 ? 90 : 100), "one burned node doesn't change the median");
            Assert.True(!ImportPlanner.TerrainIsThePlans(j, (x, z) => 40), "a flat test spot 60 m lower");
        }

                /// <summary>#62: a walls ground segment whose CS1 profile leaves the straight line by more than 1.5 m is split at
        /// the farthest point, with a node at the CS1 height; both halves keep the plan segment's id.</summary>
        public static void TestWallsSegmentSplitAtItsProfile()
        {
            var j = Job("walls", "walls", 0, hump: 3);              // segment 1 bulges 2.25 m at its middle
            Assert.Eq(1, j.SplitSegments, "one segment split");
            Assert.Eq(3, j.Segments.Count, "two halves + segment 2");
            var h0 = j.Segments[0];
            var h1 = j.Segments[1];
            Assert.Eq(1L, h0.Id, "first half keeps the plan id");
            Assert.Eq(1L, h1.Id, "second half too");
            Assert.True(h0.Half == 0 && h1.Half == 1, "halves");
            Assert.Eq(-1L, h0.End.Id, "split node id = -segment id");
            Assert.True(ReferenceEquals(h0.End, h1.Start), "shared split node");
            Assert.Near(49.625, h0.End.X, 1e-6, "split at t = 0.5 (the bezier's x there)");
            Assert.Near(102.25, h0.End.Cs1Y, 1e-6, "at the CS1 height there");
            Assert.True(h0.End.WallsGround, "a walls node");
            Assert.True(ReferenceEquals(j.SegmentAt(1, 2), h1), "the half at plan node 2");
            Assert.True(ReferenceEquals(j.SegmentAt(1, 1), h0), "the half at plan node 1");
            // the halves rebuild the original curve: the left half ends where the original is at t = 0.5
            var c = ImportPlanner.Curve(h0, new V3(0, 100, 0), new V3(49.625, 102.25, 0));
            Assert.Near(49.625, c[3].X, 1e-6, "first half ends at the split node");
            Assert.Eq(0, Job("walls", "walls", 0, hump: 1).SplitSegments, "0.75 m: not split");
            Assert.Eq(0, Job("conform", "conform", 0, hump: 3).SplitSegments, "conform: the terrain has the profile");
            Assert.Eq(0, Job("follow", "follow", 0, hump: 3).SplitSegments, "follow: lies on the terrain");
        }

        /// <summary>#62 in game: CacaoCity 25425, a straight CS1 highway across a 9 m dip between two ground-level ends. The
        /// profile is straight, so the planner doesn't split it; against the real terrain it is split at the dip, with a
        /// node 9 m above the ground there (7.5 within the wall limits), and re-batched.</summary>
        public static void TestWallsSegmentSplitOverATerrainDip()
        {
            var j = Job("walls", "walls", 0, conformed: true);
            Assert.Eq(0, j.SplitSegments, "straight profile: not split in the planner");
            System.Func<double, double, double> dip = (x, z) => System.Math.Abs(x - 50) < 12 ? 91 : 100;
            Assert.Eq(1, ImportPlanner.SplitOverTerrain(j, dip), "split over the dip");
            var mid = j.Segments[0].End;
            Assert.Eq(-1L, mid.Id, "split node");
            Assert.Near(9, mid.Cs1HeightAboveTerrain, 0.01, "9 m above the dip");
            Assert.Eq(7.5, mid.Elevation, "within the wall limits");
            int inBatches = 0; foreach (var b in j.Batches) inBatches += b.Count;
            Assert.Eq(j.Segments.Count, inBatches, "re-batched with both halves");
            Assert.Eq(0, ImportPlanner.SplitOverTerrain(j, dip), "not split twice");
            Assert.Eq(0, ImportPlanner.SplitOverTerrain(Job("walls", "walls", 0, conformed: true), (x, z) => 100), "flat ground: nothing to split");
        }

        public static void TestNodeRuleWhereModesMeet()
        {
            Assert.Eq(0.0, Job("conform", "walls", -6).Nodes[2].Elevation, "conform wins: the terrain is burned at the node");
            Assert.Eq(0.0, Job("follow", "conform", 5).Nodes[2].Elevation, "conform wins over follow");
            Assert.Eq(-6.0, Job("walls", "follow", -6).Nodes[2].Elevation, "walls over follow: CS1 height, the follow road ramps to it");
            Assert.Eq(-6.0, Job("walls", null, -6).Nodes[2].Elevation, "walls with a mode-less neighbour");
        }

        public static void TestConformAndWallsKeepTheCs1Profile()
        {
            // a 3 m hump in the middle of segment 1: kept on conform/walls ground courses, dropped on follow
            foreach (var m in new[] { "conform", "walls", "follow" })
            {
                var j = Job(m, m, 0, false, 3, split: 0); // #62 splits the walls one otherwise (its own test)
                var s = j.Segments[0];
                var c = ImportPlanner.Curve(s, new V3(0, 100, 0), new V3(100, 100, 0));
                bool humped = c[1].Y > 101;
                Assert.True(humped == (m != "follow"), m + ": profile " + (humped ? "kept" : "flat"));
            }
        }
    }
}
