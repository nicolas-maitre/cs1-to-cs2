// #63 (DECISIONS.md #138): a plan is never imported into a city driving on the other side of the road unless asked for.
using Cs2Importer.Core;

namespace CoreTests
{
    public static class DrivingSideTests
    {
        public static void TestMismatchIsRefused()
        {
            Assert.True(DrivingSide.Mismatch(true, false).StartsWith("This package is for a left-hand city; this city drives on the right."), "left-hand plan, right-hand city");
            Assert.True(DrivingSide.Mismatch(false, true).StartsWith("This package is for a right-hand city; this city drives on the left."), "right-hand plan, left-hand city");
            Assert.True(DrivingSide.Mismatch(true, false).EndsWith("Start a new city and press Prepare map."), "tells what to do");
        }

        public static void TestMatchOrUnknownGoesAhead()
        {
            Assert.True(DrivingSide.Mismatch(true, true) == null, "both left");
            Assert.True(DrivingSide.Mismatch(false, false) == null, "both right");
            Assert.True(DrivingSide.Mismatch(null, false) == null, "older plans without the field");
            Assert.True(DrivingSide.Mismatch(true, null) == null, "city side unknown");
        }
    }
}
