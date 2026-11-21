// Minimal test runner (no NuGet packages): every public static void Test* method in this assembly is a test.
using System;
using System.Linq;
using System.Reflection;

namespace CoreTests
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // `dotnet run -- plan <dir|json> [batchSize]`: run a real plan through the planner, without the game
            if (args.Length >= 2 && args[0] == "plan") return PlanSummary(args[1], args.Length > 2 ? int.Parse(args[2]) : 400);
            int failed = 0, passed = 0;
            var tests = typeof(Program).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(m => m.Name.StartsWith("Test", StringComparison.Ordinal))
                .OrderBy(m => m.DeclaringType.Name + "." + m.Name, StringComparer.Ordinal);
            foreach (var m in tests)
            {
                try { m.Invoke(null, null); passed++; Console.WriteLine("ok   " + m.DeclaringType.Name + "." + m.Name); }
                catch (TargetInvocationException e) { failed++; Console.WriteLine("FAIL " + m.DeclaringType.Name + "." + m.Name + ": " + e.InnerException.Message); }
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        static int PlanSummary(string path, int batch)
        {
            if (System.IO.Directory.Exists(path)) path = System.IO.Path.Combine(path, "cs1conv-plan.json");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var plan = Cs2Importer.Core.PlanParser.Parse(System.IO.File.ReadAllText(path));
            var parseMs = sw.ElapsedMilliseconds;
            var job = Cs2Importer.Core.ImportPlanner.Build(plan, new Cs2Importer.Core.ImportOptions { BatchSize = batch });
            var types = plan.Prefabs.Values.GroupBy(p => p.Type).Select(g => g.Key + "=" + g.Count());
            Console.WriteLine("parse " + parseMs + " ms, plan " + (sw.ElapsedMilliseconds - parseMs) + " ms");
            Console.WriteLine("segments " + plan.Segments.Count + " -> build " + job.Segments.Count + ", nodes " + job.Nodes.Count + ", batches " + job.Batches.Count + ", prefabs " + string.Join(" ", types));
            Console.WriteLine("bounds x " + job.MinX.ToString("0") + ".." + job.MaxX.ToString("0") + ", z " + job.MinZ.ToString("0") + ".." + job.MaxZ.ToString("0"));
            Console.WriteLine("skipped " + GameBridge.MiniJson.Write(Cs2Importer.Core.ImportPlanner.CountByKind(job.Skipped)));
            Console.WriteLine("warnings " + GameBridge.MiniJson.Write(Cs2Importer.Core.ImportPlanner.CountByKind(job.Warnings)));
            foreach (var w in job.Warnings.Where(w => w.Level == "warning").Take(8)) Console.WriteLine("  " + w.Kind + ": " + w.Message);
            var elev = job.Nodes.Values.GroupBy(n => n.Elevation > 0 ? "elevated" : n.Elevation < 0 ? "tunnel" : "ground").Select(g => g.Key + "=" + g.Count());
            Console.WriteLine("node elevations " + string.Join(" ", elev) + "; min " + job.Nodes.Values.Min(n => n.Elevation) + " max " + job.Nodes.Values.Max(n => n.Elevation));
            var kinds = job.Segments.GroupBy(s => s.Source.Kind).Select(g => g.Key + "=" + g.Count());
            Console.WriteLine("segment kinds " + string.Join(" ", kinds));
            return 0;
        }
    }

    public static class Assert
    {
        public static void True(bool c, string msg) { if (!c) throw new Exception(msg); }
        public static void Eq<T>(T expected, T actual, string what) { if (!Equals(expected, actual)) throw new Exception(what + ": expected " + expected + ", got " + actual); }
        public static void Near(double expected, double actual, double tol, string what) { if (Math.Abs(expected - actual) > tol) throw new Exception(what + ": expected " + expected + ", got " + actual); }
        public static void Throws<TEx>(Action a, string what) where TEx : Exception
        {
            try { a(); } catch (TEx) { return; }
            throw new Exception(what + ": expected " + typeof(TEx).Name);
        }
    }
}
