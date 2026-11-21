// CS1 tunnels shallower than CS2's tunnel rule are deepened (DECISIONS-importer.md #58, DECISIONS.md #124).
// CS2 (NetCompositionHelpers.GetElevationFlags) builds a tunnel when every elevation of the course is at most -2L and the
// deepest at most -3L (L = m_ElevationLimit: 4 for roads and tracks, 2 for paths).
using System.Globalization;
using Cs2Importer.Core;

namespace CoreTests
{
    public static class TunnelDepthTests
    {
        /// <summary>
        /// A street crossing over a highway tunnel, Skibidi-style: ground 1 (0 m) - slope - portal 2 - tunnel - 3 - tunnel -
        /// 4 portal - slope - ground 5, 100 m apart along x; CS1 terrain 100 m. Node elevations given for 2, 3, 4.
        /// </summary>
        static ImportJob Job(double e2, double e3, double e4, string type = "RoadPrefab", string mode = null, bool conformed = false, double slopeLen = 100)
        {
            string f(double v) => v.ToString(CultureInfo.InvariantCulture);
            string node(int id, double x, double e) => @"{ ""id"": " + id + @", ""x"": " + f(x) + @", ""z"": 0, ""y"": " + f(100 + e) + @", ""heightAboveTerrain"": " + f(e) + " }";
            string seg(int id, int a, int b, string kind, double ea, double eb) => @"{ ""id"": " + id + @", ""prefab"": ""h"", ""start"": " + a + @", ""end"": " + b + @", ""kind"": """ + kind + @""", ""elevationStart"": " + f(ea) + @", ""elevationEnd"": " + f(eb) + @", ""cs1Info"": ""Two Lane Highway " + kind + @""" }";
            double x2 = slopeLen, x3 = x2 + 100, x4 = x3 + 100, x5 = x4 + slopeLen;
            var json = @"{ ""format"": ""cs1conv-plan"", ""formatVersion"": 1, ""source"": { ""city"": ""C"" },
  ""terrain"": { ""roadConformed"": " + (conformed ? "true" : "false") + @" },
  ""prefabs"": { ""h"": { ""type"": """ + type + @""", ""name"": ""Highway""" + (mode != null ? @", ""heightMode"": """ + mode + @"""" : "") + @" } },
  ""nodes"": [ " + node(1, 0, 0) + ", " + node(2, x2, e2) + ", " + node(3, x3, e3) + ", " + node(4, x4, e4) + ", " + node(5, x5, 0) + @" ],
  ""segments"": [ " + seg(1, 1, 2, "slope", 0, e2) + ", " + seg(2, 2, 3, "tunnel", e2, e3) + ", " + seg(3, 3, 4, "tunnel", e3, e4) + ", " + seg(4, 4, 5, "slope", e4, 0) + " ] }";
            return ImportPlanner.Build(PlanParser.Parse(json), new ImportOptions());
        }

        public static void TestCs2TunnelRule()
        {
            Assert.True(ImportPlanner.IsCs2Tunnel(-12, -12, 4), "-12/-12: tunnel");
            Assert.True(ImportPlanner.IsCs2Tunnel(-8.5, -16, 4), "portal -8.5, deep end -16: tunnel");
            Assert.True(!ImportPlanner.IsCs2Tunnel(-9, -9, 4), "-9/-9 (Skibidi's highway tunnels): open cut");
            Assert.True(!ImportPlanner.IsCs2Tunnel(-11.9, -11.9, 4), "-11.9: open cut");
            Assert.True(!ImportPlanner.IsCs2Tunnel(-7, -20, 4), "an end above -8: open cut");
            Assert.True(ImportPlanner.IsCs2Tunnel(-6.5, -6.5, 2), "paths (L = 2): -6.5 is a tunnel");
        }

        public static void TestShallowTunnelIsDeepened()
        {
            var j = Job(-9, -9.2, -9);
            foreach (var s in j.Segments)
                if (s.Source.Kind == "tunnel")
                    Assert.True(ImportPlanner.IsCs2Tunnel(s.Start.Elevation, s.End.Elevation, 4), "segment " + s.Id + " is a CS2 tunnel (" + s.Start.Elevation + " / " + s.End.Elevation + ")");
            Assert.Eq(-12.5, j.Nodes[3].Elevation, "the deep middle node at -(3L + 0.5)");
            Assert.Eq(-9.0, j.Nodes[2].Elevation, "portal kept at its CS1 depth (already below -8.5)");
            Assert.Eq(-9.0, j.Nodes[4].Elevation, "other portal kept");
            Assert.Eq(2, j.TunnelsDeepened.Count, "both tunnel segments reported");
            var row = j.TunnelsDeepened[0];
            Assert.Eq(-9.2, (double)row["deepest_before"], "CS1 depth");
            Assert.Eq(-12.5, (double)row["deepest_cs2"], "CS2 depth used");
            Assert.Eq(-3.3, (double)row["delta"], "delta");
            Assert.True(j.Warnings.Exists(w => w.Kind == "tunnel-deepened"), "reported as tunnel-deepened");
            Assert.Eq(0.0, j.Nodes[1].Elevation, "the road above the descent stays at ground level");
        }

        public static void TestShallowPortalsGoToMinusEightAndAHalf()
        {
            var j = Job(-6, -9, -6);
            Assert.Eq(-8.5, j.Nodes[2].Elevation, "portal lowered just to -(2L + 0.5)");
            Assert.Eq(-12.5, j.Nodes[3].Elevation, "middle to -(3L + 0.5)");
        }

        public static void TestDeepTunnelsUntouched()
        {
            var j = Job(-14, -20, -14);
            Assert.Eq(0, j.TunnelsDeepened.Count, "already a CS2 tunnel");
            Assert.Eq(-20.0, j.Nodes[3].Elevation, "unchanged");
        }

        public static void TestPathsUseTheirOwnLimit()
        {
            var j = Job(-7, -7, -7, type: "PathwayPrefab");
            Assert.Eq(0, j.TunnelsDeepened.Count, "a -7 m pedestrian tunnel is already a CS2 tunnel (L = 2)");
        }

        public static void TestTunnelNodesNeverConformedAndSlopesNotBurned()
        {
            var j = Job(-9, -9.2, -9, mode: "conform", conformed: true);
            Assert.Eq("walls", j.Segments[0].HeightMode, "a conform slope is built as walls (in the natural terrain)");
            Assert.True(!j.Nodes[2].Conformed, "portal not conformed");
            Assert.True(j.Nodes[2].Elevation <= -8.5, "portal below ground, so the tunnel has ground above it");
            Assert.Eq(0.0, j.Nodes[1].Elevation, "the ground end of the slope at ground level");
        }

        public static void TestSteepDescentIsExtendedAlongTheSlope()
        {
            // a 3-segment descent: ground 0 - slope - a (-3) - slope - portal (-9) - tunnel - (-9.2) ...; 15 m slope pieces
            var json = @"{ ""format"": ""cs1conv-plan"", ""formatVersion"": 1, ""source"": { ""city"": ""C"" },
  ""prefabs"": { ""h"": { ""type"": ""RoadPrefab"", ""name"": ""Highway"" } },
  ""nodes"": [ { ""id"": 1, ""x"": 0, ""z"": 0, ""y"": 100, ""heightAboveTerrain"": 0 },
               { ""id"": 2, ""x"": 30, ""z"": 0, ""y"": 94, ""heightAboveTerrain"": -6 },
               { ""id"": 3, ""x"": 60, ""z"": 0, ""y"": 91, ""heightAboveTerrain"": -9 },
               { ""id"": 4, ""x"": 160, ""z"": 0, ""y"": 91, ""heightAboveTerrain"": -9 } ],
  ""segments"": [ { ""id"": 1, ""prefab"": ""h"", ""start"": 1, ""end"": 2, ""kind"": ""slope"", ""elevationStart"": 0, ""elevationEnd"": -6 },
                  { ""id"": 2, ""prefab"": ""h"", ""start"": 2, ""end"": 3, ""kind"": ""slope"", ""elevationStart"": -6, ""elevationEnd"": -9 },
                  { ""id"": 3, ""prefab"": ""h"", ""start"": 3, ""end"": 4, ""kind"": ""tunnel"", ""elevationStart"": -9, ""elevationEnd"": -9 } ] }";
            var j = ImportPlanner.Build(PlanParser.Parse(json), new ImportOptions());
            // tunnel 3-4 needs one end at -12.5 and the other at -8.5 at most: node 3 or 4 goes to -12.5
            var s = j.Segments[2];
            Assert.True(ImportPlanner.IsCs2Tunnel(s.Start.Elevation, s.End.Elevation, 4), "tunnel built");
            if (j.Nodes[3].Elevation <= -12.5)
            {
                // the portal went deep: slope 2-3 (30 m) would rise 6.5 m = 22 %: node 2 is lowered to keep 20 %
                Assert.True(j.Nodes[2].Elevation < -6, "descent extended into the slope (node 2 " + j.Nodes[2].Elevation + ")");
                Assert.True(j.Warnings.Exists(w => w.Kind == "tunnel-descent-extended"), "reported");
            }
            Assert.Eq(0.0, j.Nodes[1].Elevation, "the ground end stays at ground level");
        }

        public static void TestInGamePassUsesTheRealTerrain()
        {
            // planned -12.5 in the middle, but the burn cut 2 m of ground above it: on the real terrain it's only -10.5
            var j = Job(-9, -9.2, -9);
            j.Nodes[3].Elevation = -10.5; // what KeepHeightsOnConformedTerrain leaves after a 2 m cut
            int n = ImportPlanner.DeepenTunnels(j, (x, z) => 100);
            Assert.True(n > 0, "deepened again on the real terrain");
            Assert.Eq(-12.5, j.Nodes[3].Elevation, "back to the CS2 tunnel depth");
            Assert.Eq(-12.5, j.Nodes[3].PositionHeight, "placed there");
        }
    }
}
