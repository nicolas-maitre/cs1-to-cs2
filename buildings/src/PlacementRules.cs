// Game-free placement rules of the placer (Placement.cs), kept apart so buildings/tests/RulesTests runs them without the
// game's DLLs (CI). No Unity or Game types here.
//   - DECISIONS.md #106 (user): never moved to free land by default; a smaller alternative only.
//   - DECISIONS-buildings #36 / #37: stations stay at their CS1 spot (short slides), roads above / below don't block.
using System;
using System.Collections.Generic;

namespace Cs2Buildings
{
    public static class PlacementRules
    {
        public const string DefaultFallback = "skip";
        public const float DefaultStationMaxSlide = 8f;

        /// <summary>The fallback an item gets: stations always "skip"; else its own, else the run's (default "skip").</summary>
        public static string Fallback(bool station, string itemFallback, string runFallback)
        {
            if (station) return "skip";
            return itemFallback ?? runFallback ?? DefaultFallback;
        }

        /// <summary>
        /// Whether an alternative prefab is tried after the first usable one. With a free-land fallback, any alternative;
        /// without one ("skip"), only one whose lot area is at most the first one's: it may fit where the match didn't.
        /// </summary>
        public static bool TryAlternative(string fallback, int firstLotArea, int alternativeLotArea)
        {
            return fallback != "skip" || alternativeLotArea <= firstLotArea;
        }

        /// <summary>Whether a slide along the road is tried: up to maxSlide, and for stations up to stationMaxSlide.</summary>
        public static bool SlideAllowed(bool station, float slide, float maxSlide, float stationMaxSlide)
        {
            float s = Math.Abs(slide);
            if (s > maxSlide + 0.01f) return false;
            return !station || s <= stationMaxSlide + 0.01f;
        }

        /// <summary>Whether a road edge can block a lot: tunnels never do, elevated roads don't block stations.</summary>
        public static bool RoadCanBlock(bool station, bool tunnel, bool elevated)
        {
            if (tunnel) return false;
            if (station && elevated) return false;
            return true;
        }

        /// <summary>A position rounded to metres: "(x, z)".</summary>
        public static string Position(double x, double z)
        {
            return "(" + Math.Round(x).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Math.Round(z).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// Where to place a building yourself, in the web report's words: "CS1 (x, z) = CS2 (x, z)". A plan item's x/z is
        /// in plan coordinates (CS1 + transform dx/dz); the CS2 position adds the import offset.
        /// </summary>
        public static string Where(double planX, double planZ, double dx, double dz, double offsetX, double offsetZ)
        {
            return "CS1 " + Position(planX - dx, planZ - dz) + " = CS2 " + Position(planX + offsetX, planZ + offsetZ);
        }

        /// <summary>
        /// Decision 128 (user): a highlighted warning when the plan has power plants (or water facilities) and none of them
        /// was placed. A city whose CS1 original had none gets no warning.
        /// </summary>
        public static System.Collections.Generic.List<string> SupplyWarnings(int powerPlanned, int powerPlaced, int waterPlanned, int waterPlaced)
        {
            var l = new System.Collections.Generic.List<string>();
            const string Rerun = ": place them yourself, or re-run the package with 'let utilities move nearby'";
            if (powerPlanned > 0 && powerPlaced == 0) l.Add("This city has no power plant placed (" + powerPlanned + " in CS1)" + Rerun);
            if (waterPlanned > 0 && waterPlaced == 0) l.Add("This city has no water supply placed (" + waterPlanned + " in CS1)" + Rerun);
            return l;
        }

        /// <summary>
        /// RC3 finding: a transit depot type the plan has (BusDepot…, TramDepot…) with none placed leaves its lines without
        /// vehicles (Skibidi: the only Bus Depot overlapped a road, 9 bus lines, 4 buses after 300 s). The depot kind from
        /// the prefab name, or null for other buildings.
        /// </summary>
        public static string DepotKind(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            foreach (var k in new[] { "Bus", "Tram", "Subway", "Train", "Taxi", "Ship", "Trolley" })
                if (prefab.StartsWith(k + "Depot", StringComparison.Ordinal) || prefab.StartsWith(k + "Yard", StringComparison.Ordinal)) return k.ToLowerInvariant();
            if (prefab.StartsWith("RailYard", StringComparison.Ordinal)) return "train";
            return null;
        }

        /// <summary>
        /// Decision #152: which items to retry with "nearby" after the normal pass. Per bus or tram depot kind with none
        /// placed, the first CS1 depot of that kind that was strict (fallback "skip") and failed. Other kinds (rail and
        /// subway yards, #83) and other buildings (#106) are never retried.
        /// </summary>
        public static List<int> DepotRetries(IList<string> kinds, IList<bool> placed, IList<bool> strict)
        {
            var res = new List<int>();
            var placedKinds = new HashSet<string>();
            for (int i = 0; i < kinds.Count; i++) if (kinds[i] != null && placed[i]) placedKinds.Add(kinds[i]);
            var taken = new HashSet<string>();
            for (int i = 0; i < kinds.Count; i++)
            {
                var k = kinds[i];
                if (k != "bus" && k != "tram") continue;
                if (placed[i] || !strict[i] || placedKinds.Contains(k) || taken.Contains(k)) continue;
                taken.Add(k);
                res.Add(i);
            }
            return res;
        }

        /// <summary>The report note for a depot placed by decision #152.</summary>
        public const string DepotMovedNote = "placed away from its CS1 spot because its lines need a depot";

        /// <summary>"This city has no bus depot placed (1 in CS1): its bus lines get no vehicles; place it yourself …".</summary>
        public static string DepotWarning(string kind, int planned)
        {
            return "This city has no " + kind + " depot placed (" + planned + " in CS1): its " + kind + " lines get no vehicles; place it yourself (see the list below)";
        }

        /// <summary>Plan item families that count for SupplyWarnings (buildings/match/match.mjs families).</summary>
        public static bool IsPower(string family) { return family == "power"; }
        public static bool IsWater(string family) { return family == "waterPump"; }

        /// <summary>How far past its lot a surface-water pump looks for water (DECISIONS-buildings #42).</summary>
        public const float DefaultPumpShoreReach = 30f;

        /// <summary>
        /// DECISIONS-buildings #42, what a surface-water pump gets, from the distance to the nearest water (+inf: none),
        /// the lot's reach (half its long side) and whether the ground has water: "keep" (water under the lot), "slide"
        /// (water within shoreReach more: slides toward it first), "groundwater" (swap to the groundwater pump), "dry"
        /// (no water found either way: placed as planned and reported; never dropped on a water reading alone).
        /// </summary>
        public static string PumpWaterAction(float waterDistance, float lotReach, float shoreReach, bool groundwater)
        {
            if (waterDistance <= lotReach) return "keep";
            if (waterDistance <= lotReach + shoreReach) return "slide";
            return groundwater ? "groundwater" : "dry";
        }

        /// <summary>Families whose buildings need a road edge to work: power reaches them (and leaves plants) through it.</summary>
        public static bool NeedsRoad(string family) { return family == "power" || family == "waterPump" || family == "sewage"; }

        /// <summary>
        /// Decision #44: with transformers off (the default), a plant that feeds only the high-voltage grid gets this line.
        /// </summary>
        public static string NeedsTransformer(string name, string cs2Position)
        {
            return (string.IsNullOrEmpty(name) ? "Power plant" : name) + " at CS2 " + cs2Position + " feeds only the high-voltage grid: place a transformer station near it, or re-import with 'Add a transformer station next to each imported power plant'";
        }

        /// <summary>A "connect a road" line for a utility building placed without a road connection.</summary>
        public static string ConnectRoad(string name, string cs2Position)
        {
            return (string.IsNullOrEmpty(name) ? "Building" : name) + " at CS2 " + cs2Position + " is not connected to a road, so it gets no electricity: connect a road to it";
        }

        /// <summary>One report line for a building that wasn't placed: "Hospital: place it yourself at CS1 (…) = CS2 (…) (why)".</summary>
        public static string PlaceYourself(string name, string where, string why)
        {
            return (string.IsNullOrEmpty(name) ? "Building" : name) + ": place it yourself at " + where + (string.IsNullOrEmpty(why) ? "" : " (" + why + ")");
        }
    }
}
