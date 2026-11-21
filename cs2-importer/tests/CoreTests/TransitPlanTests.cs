// Golden test: Cs2Importer.Core.TransitPlan (the mod's port) against the transit worker's transitJobs
// (transit/lib/plan.mjs), whose output tests/fixtures/make-transit-golden.mjs writes to transit-jobs.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Cs2Importer.Core;
using GameBridge;

namespace CoreTests
{
    public static class TransitPlanTests
    {
        static string Fixture(string name)
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                var f = Path.Combine(dir, "tests", "fixtures", name);
                if (File.Exists(f)) return f;
                f = Path.Combine(dir, "fixtures", name);
                if (File.Exists(f)) return f;
            }
            throw new FileNotFoundException("fixture " + name + " not found above " + AppContext.BaseDirectory);
        }

        public static void TestTransitJobsMatchNodeOriginal()
        {
            var plan = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(Fixture("transit-plan.json")));
            var golden = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(Fixture("transit-jobs.json")));
            foreach (var theme in new[] { "EU", "NA" })
            {
                var expected = (Dictionary<string, object>)golden[theme];
                var j = TransitPlan.BuildJobs(plan, theme);
                // through JSON, so numbers compare as doubles the way the exec library receives them
                var actual = (Dictionary<string, object>)MiniJson.Parse(MiniJson.Write(new Dictionary<string, object>
                {
                    { "stops", new List<object>(j.Stops) }, { "lines", new List<object>(j.Lines) }, { "skipped", j.Skipped }, { "stats", j.Stats },
                }));
                Same(expected, actual, theme);
            }
            var eu = (Dictionary<string, object>)golden["EU"];
            Assert.True(((List<object>)eu["stops"]).Count > 100 && ((List<object>)eu["skipped"]).Count == 1, "fixture covers kerb stops and the skipped path");
        }

        public static void TestTransitJobsOffset()
        {
            var plan = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(Fixture("transit-plan.json")));
            var a = TransitPlan.BuildJobs(plan, "EU");
            var b = TransitPlan.BuildJobs(plan, "EU", 100, -50);
            Assert.Near(Convert.ToDouble(a.Stops[0]["x"]) + 100, Convert.ToDouble(b.Stops[0]["x"]), 1e-9, "stop x shifted");
            Assert.Near(Convert.ToDouble(a.Stops[0]["z"]) - 50, Convert.ToDouble(b.Stops[0]["z"]), 1e-9, "stop z shifted");
            Assert.Eq((string)a.Stops[0]["key"], (string)b.Stops[0]["key"], "keys don't depend on the offset");
        }

        /// <summary>Deep equality; a key the Node side left out (undefined) must be null on ours.</summary>
        static void Same(object e, object a, string path)
        {
            if (e is Dictionary<string, object> ed)
            {
                var ad = a as Dictionary<string, object>;
                Assert.True(ad != null, path + ": expected an object");
                foreach (var kv in ed)
                {
                    object av;
                    ad.TryGetValue(kv.Key, out av);
                    Same(kv.Value, av, path + "." + kv.Key);
                }
                foreach (var kv in ad)
                    if (!ed.ContainsKey(kv.Key)) Assert.True(kv.Value == null, path + "." + kv.Key + ": not in the Node output, but " + kv.Value);
                return;
            }
            if (e is List<object> el)
            {
                var al = a as List<object>;
                Assert.True(al != null, path + ": expected an array");
                Assert.Eq(el.Count, al.Count, path + " length");
                for (int i = 0; i < el.Count; i++) Same(el[i], al[i], path + "[" + i + "]");
                return;
            }
            if (e is double d)
            {
                Assert.True(a is double, path + ": expected a number, got " + (a ?? "null"));
                Assert.Near(d, (double)a, 1e-9, path);
                return;
            }
            Assert.True(Equals(e, a), path + ": expected " + (e ?? "null") + ", got " + (a ?? "null"));
        }
    }
}
