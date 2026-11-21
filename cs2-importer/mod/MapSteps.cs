// Map preparation and outside connections inside the mod, from the self-contained package (schema/README.md §2:
// terrain/, plan.terrain, plan.water.apply, plan.water.checkpoints, plan.outsideConnections). The work itself is the
// terrain worker's code (terrain/cs2-apply, compiled in): MapPrep.Run / Ops.Run with the same args terrain/mapprep.mjs
// and terrain/apply.mjs send, in the order tools/e2e/run.mjs runs them (clear → terrain → water; connections after
// the import). Coroutines, stepped by ImportSystem in ToolUpdate.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cs1ConvTerrain;
using GameBridge;

namespace Cs2ImporterMod
{
    public static class MapSteps
    {
        // ---------------------------------------------------------------- prepare map

        /// <summary>
        /// A fresh city → the package's map: delete the base map's top-level nets and buildings, apply the heightmap and
        /// world map, set the sea level and the CS1 water sources (or a dry map), then check the terrain against the file.
        /// </summary>
        public static IEnumerator PrepareMap(string planFile, Action<string> progress, Func<bool> cancelled = null)
        {
            var pkg = Path.GetDirectoryName(Path.GetFullPath(planFile));
            var plan = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(planFile));
            var result = new Dictionary<string, object>();

            // 1. base map: every top-level net (all:true, terrain/mapprep.mjs clear's default) and building
            cancelled = cancelled ?? (() => false);
            progress("prep.step.clear");
            var clear = Drive(MapPrep.Run(new Dictionary<string, object> { { "op", "clear" }, { "frames", 10.0 }, { "all", true } }));
            while (clear.MoveNext()) yield return null;
            result["clear"] = Pick(clear.Result, "edgesDeleted", "nodesDeleted", "buildingsDeleted", "keptOwnedEdges", "keptOtherEdges");
            // the CS1 city's side of the road (exporter 1.2.1+: plan.source.leftHandTraffic), set while the map has no roads:
            // CompositionSelectSystem and the other net systems read CityConfigurationSystem.leftHandTraffic every update,
            // and the city saves it
            var lht = PlanLeftHand(plan);
            if (lht.HasValue)
            {
                var cfg = Unity.Entities.World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>();
                bool was = cfg.leftHandTraffic;
                cfg.leftHandTraffic = lht.Value;
                result["traffic"] = new Dictionary<string, object> { { "leftHand", lht.Value }, { "changed", was != lht.Value } };
            }
            for (int i = 0; i < 60; i++) yield return null; // the deletions settle (terrain worker: wait 60 frames)

            // 2. terrain + world map + sea level
            var terrain = P.Obj(plan, "terrain");
            var water = P.Obj(plan, "water");
            var apply = water != null ? P.Obj(water, "apply") : null;
            Dictionary<string, object> meta = null;
            string metaDir = null;
            if (terrain != null)
            {
                var metaFile = Path.Combine(pkg, P.Str(terrain, "meta", "terrain/terrain.json").Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(metaFile)) throw new InvalidOperationException("the package's terrain metadata is missing: " + metaFile);
                meta = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(metaFile));
                metaDir = Path.GetDirectoryName(metaFile);
            }
            double? seaLevel = apply != null && P.Has(apply, "seaLevel") ? P.Num(apply, "seaLevel", 0) : terrain != null && P.Has(terrain, "seaLevelDry") ? P.Num(terrain, "seaLevelDry", 0) : (double?)null;
            if (meta != null)
            {
                if (cancelled()) { result["cancelled"] = "prep.step.clear"; yield return result; yield break; }
                progress("prep.step.terrain");
                var file = Path.Combine(metaDir, P.Required(meta, "file"));
                var world = P.Obj(meta, "world");
                var worldFile = world != null && P.Has(world, "file") ? Path.Combine(metaDir, P.Str(world, "file", "")) : null;
                if (worldFile != null && !File.Exists(worldFile)) worldFile = null;
                var points = CheckPoints(meta);
                var a = new Dictionary<string, object>
                {
                    { "op", "apply" }, { "file", file }, { "worldFile", worldFile },
                    { "heightScale", P.Num(meta, "heightScale", 1024) }, { "heightOffset", P.Num(meta, "heightOffset", 0) },
                    { "seaLevel", seaLevel.HasValue ? (object)seaLevel.Value : null }, { "frames", 30.0 }, { "points", points },
                };
                var ap = Drive(Ops.Run(a));
                while (ap.MoveNext()) yield return null;
                // CPU heights come back from the GPU asynchronously: sample again a little later (terrain/apply.mjs)
                for (int i = 0; i < 60; i++) yield return null;
                var sampled = Drive(Ops.Run(new Dictionary<string, object> { { "op", "sample" }, { "points", points } }));
                while (sampled.MoveNext()) yield return null;
                result["terrain"] = TerrainCheck(meta, file, sampled.Result, worldFile != null, seaLevel);
            }
            else result["terrain"] = new Dictionary<string, object> { { "skipped", "the package has no terrain (export the city with its terrain)" } };

            // 3. water: the CS1 sources (water.apply), else a dry map at the lowered sea level (only with our terrain)
            if (apply != null || meta != null)
            {
                if (cancelled()) { result["cancelled"] = meta != null ? "prep.step.terrain" : "prep.step.clear"; yield return result; yield break; }
                progress("prep.step.water");
                var sources = apply != null && apply.TryGetValue("sources", out var so) && so is List<object> l ? l : new List<object>();
                var set = MapPrep.Run(new Dictionary<string, object> { { "op", "water.set" }, { "seaLevel", seaLevel.HasValue ? (object)seaLevel.Value : null }, { "clear", true }, { "sources", sources } }) as Dictionary<string, object>;
                MapPrep.Run(new Dictionary<string, object> { { "op", "water.reset" }, { "mode", "sea" } });
                result["water"] = new Dictionary<string, object>
                {
                    { "mode", apply != null ? "cs1" : "dry" }, { "seaLevel", seaLevel }, { "sources", set != null ? set["created"] : 0 },
                    { "baseMapSourcesRemoved", set != null ? set["removed"] : 0 },
                };
            }
            else result["water"] = new Dictionary<string, object> { { "skipped", "the package has no water or terrain" } };

            // settle before the road import, as tools/e2e does (terrain worker: about 600 frames after terrain + water)
            progress("prep.step.settle");
            for (int i = 0; i < 600 && !cancelled(); i++) yield return null; // a cancel only cuts the wait short
            if (water != null && P.Has(water, "checkpoints")) result["water_check"] = WaterCheck(plan);
            yield return result;
        }

        /// <summary>plan.source.leftHandTraffic (null for exports before exporter 1.2.1).</summary>
        public static bool? PlanLeftHand(Dictionary<string, object> plan)
        {
            var src = P.Obj(plan, "source");
            object v;
            return src != null && src.TryGetValue("leftHandTraffic", out v) && v is bool ? (bool?)(bool)v : null;
        }

        public static bool CityLeftHand()
        {
            try { return Unity.Entities.World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic; }
            catch { return false; }
        }

        /// <summary>Median gap (m) under which a city counts as prepared with this package's terrain.</summary>
        public const double PreparedTolerance = 1.0;

        /// <summary>
        /// Median |game height − package height| at the check points, or null without terrain in the package: tells a
        /// city prepared for this package (≈ 0 m) from any other map. Synchronous (CPU heights).
        /// </summary>
        public static double? TerrainMismatch(string planFile)
        {
            var pkg = Path.GetDirectoryName(Path.GetFullPath(planFile));
            var plan = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(planFile));
            var terrain = P.Obj(plan, "terrain");
            if (terrain == null) return null;
            var metaFile = Path.Combine(pkg, P.Str(terrain, "meta", "terrain/terrain.json").Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(metaFile)) return null;
            var meta = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(metaFile));
            var file = Path.Combine(Path.GetDirectoryName(metaFile), P.Required(meta, "file"));
            var points = CheckPoints(meta);
            var d = Drive(Ops.Run(new Dictionary<string, object> { { "op", "sample" }, { "points", points } }));
            while (d.MoveNext()) { }
            var check = TerrainCheck(meta, file, d.Result, false, null);
            return Convert.ToDouble(check["medianError"]);
        }

        /// <summary>A 9 × 9 grid over the playable area (terrain/apply.mjs gridCheckpoints) plus 10 × 10 over the crop.</summary>
        static List<object> CheckPoints(Dictionary<string, object> meta)
        {
            var pts = new List<object>();
            double size = P.Num(meta, "size", 14336);
            for (int i = 0; i < 9; i++)
                for (int j = 0; j < 9; j++)
                    pts.Add(new List<object> { -size / 2 + (i + 0.5) * size / 9 + 13.3, -size / 2 + (j + 0.5) * size / 9 - 7.9 });
            double crop = 3000;
            var c = P.Obj(meta, "crop");
            if (c != null && P.Has(c, "size")) crop = P.Num(c, "size", crop);
            for (int i = 0; i < 10; i++)
                for (int j = 0; j < 10; j++)
                    pts.Add(new List<object> { -crop / 2 + (i + 0.5) * crop / 10 + 3.1, -crop / 2 + (j + 0.5) * crop / 10 - 5.7 });
            return pts;
        }

        /// <summary>Game heights vs the package file at the same points (bilinear, terrain/lib/heightmap.mjs sampleCs2).</summary>
        static Dictionary<string, object> TerrainCheck(Dictionary<string, object> meta, string file, object sampled, bool world, double? sea)
        {
            int n = (int)P.Num(meta, "resolution", 4096);
            double size = P.Num(meta, "size", 14336), hs = P.Num(meta, "heightScale", 1024), ho = P.Num(meta, "heightOffset", 0);
            var bytes = File.ReadAllBytes(file);
            var errs = new List<double>();
            var pts = (List<object>)((Dictionary<string, object>)sampled)["points"];
            foreach (var o in pts)
            {
                var p = (List<object>)o;
                double x = Convert.ToDouble(p[0]), z = Convert.ToDouble(p[1]), game = Convert.ToDouble(p[2]);
                errs.Add(game - SampleFile(bytes, n, size, hs, ho, x, z));
            }
            var abs = errs.ConvertAll(Math.Abs);
            abs.Sort();
            return new Dictionary<string, object>
            {
                { "points", abs.Count }, { "worldMap", world }, { "seaLevel", sea },
                { "medianError", abs.Count == 0 ? 0 : Math.Round(abs[abs.Count / 2], 3) },
                { "p95Error", abs.Count == 0 ? 0 : Math.Round(abs[Math.Min(abs.Count - 1, (int)(abs.Count * 0.95))], 3) },
                { "maxError", abs.Count == 0 ? 0 : Math.Round(abs[abs.Count - 1], 3) },
            };
        }

        static double SampleFile(byte[] raw, int n, double size, double hs, double ho, double x, double z)
        {
            double scale = n / size;
            double fx = (x + size / 2) * scale - 0.5, fz = (z + size / 2) * scale - 0.5;
            int x0 = (int)Math.Floor(fx), z0 = (int)Math.Floor(fz);
            int cx0 = Clamp(x0, n), cx1 = Clamp(x0 + 1, n), cz0 = Clamp(z0, n), cz1 = Clamp(z0 + 1, n);
            double tx = Sat(fx - cx0), tz = Sat(fz - cz0);
            double a = U16(raw, cz0 * n + cx0), b = U16(raw, cz0 * n + cx1), c = U16(raw, cz1 * n + cx0), d = U16(raw, cz1 * n + cx1);
            double v = (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
            return v / 65535 * hs + ho;
        }

        static int Clamp(int i, int n) { return i < 0 ? 0 : i > n - 1 ? n - 1 : i; }
        static double Sat(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
        static double U16(byte[] b, int i) { return b[2 * i] | (b[2 * i + 1] << 8); }

        // ---------------------------------------------------------------- water check

        /// <summary>CS2 water depth at the package's CS1 checkpoints (terrain/mapprep.mjs water-check, same thresholds).</summary>
        public static Dictionary<string, object> WaterCheck(Dictionary<string, object> plan)
        {
            var cp = P.Obj(P.Obj(plan, "water"), "checkpoints");
            if (cp == null) return new Dictionary<string, object> { { "skipped", "no water checkpoints in the package" } };
            var all = new List<Dictionary<string, object>>();
            foreach (var k in new[] { "wet", "dry" })
                if (cp.TryGetValue(k, out var o) && o is List<object> l)
                    foreach (var p in l) all.Add((Dictionary<string, object>)p);
            var pts = all.ConvertAll(p => (object)new List<object> { P.Num(p, "x", 0), P.Num(p, "z", 0) });
            var r = (Dictionary<string, object>)MapPrep.Run(new Dictionary<string, object> { { "op", "water.sample" }, { "points", pts } });
            var depths = (List<object>)r["points"];
            int wet = 0, wetOk = 0, dry = 0, dryOk = 0;
            var errs = new List<double>();
            for (int i = 0; i < all.Count; i++)
            {
                double cs1 = P.Num(all[i], "cs1Depth", 0), cs2 = Convert.ToDouble(((List<object>)depths[i])[2]);
                if (cs1 > 0.5) { wet++; if (cs2 > 0.1) wetOk++; errs.Add(Math.Abs(cs2 - cs1)); }
                else if (cs1 == 0) { dry++; if (cs2 <= 0.1) dryOk++; }
            }
            errs.Sort();
            return new Dictionary<string, object>
            {
                { "wetAlsoWet", wetOk + "/" + wet }, { "dryAlsoDry", dryOk + "/" + dry },
                { "wetDepthMedianError", errs.Count == 0 ? (object)null : Math.Round(errs[errs.Count / 2], 2) },
            };
        }

        // ---------------------------------------------------------------- outside connections

        /// <summary>Import step "connections" (last): plan.outsideConnections → MapPrep oc.create on the imported end nodes.</summary>
        public static IEnumerator Connections(Dictionary<string, object> args)
        {
            var plan = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText((string)args["plan"]));
            var oc = P.Obj(plan, "outsideConnections");
            var list = oc != null && oc.TryGetValue("connections", out var co) ? co as List<object> : null;
            if (list == null || list.Count == 0) { yield return new Dictionary<string, object> { { "skipped", "no outside connections in the package" } }; yield break; }
            double ox = P.Num(args, "offsetX", 0), oz = P.Num(args, "offsetZ", 0);
            var conns = new List<object>();
            foreach (var o in list)
            {
                var c = (Dictionary<string, object>)o;
                conns.Add(new Dictionary<string, object> { { "x", P.Num(c, "x", 0) + ox }, { "z", P.Num(c, "z", 0) + oz }, { "type", P.Str(c, "type", "road") } });
            }
            int before = ((List<object>)MapPrep.Run(new Dictionary<string, object> { { "op", "oc.list" } })).Count;
            var created = (Dictionary<string, object>)MapPrep.Run(new Dictionary<string, object> { { "op", "oc.create" }, { "connections", conns } });
            for (int i = 0; i < 30; i++) yield return null;
            var after = (List<object>)MapPrep.Run(new Dictionary<string, object> { { "op", "oc.list" } });
            int ok = 0, withLanes = 0;
            var errors = new List<object>();
            foreach (var r in (List<object>)created["results"])
            {
                var d = (Dictionary<string, object>)r;
                if (d.ContainsKey("error")) errors.Add(d); else ok++;
            }
            foreach (var a in after) if (a is Dictionary<string, object> d && d.TryGetValue("subLanes", out var sl) && Convert.ToInt32(sl) > 0) withLanes++;
            int road = 0, train = 0;
            foreach (var c in conns) if (P.Str((Dictionary<string, object>)c, "type", "road") == "train") train++; else road++;
            yield return new Dictionary<string, object>
            {
                { "items", conns.Count }, { "placed", ok }, { "road", road }, { "train", train }, { "failed", errors.Count }, { "errors", errors },
                { "before", before }, { "after", after.Count }, { "withLanes", withLanes },
            };
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Steps a terrain op's result: a coroutine (IEnumerator, last non-null value = result) or a plain value.</summary>
        sealed class Driven : IEnumerator
        {
            readonly IEnumerator m_E;
            public object Result;
            public Driven(object r) { m_E = r as IEnumerator; if (m_E == null) Result = r; }
            public bool MoveNext()
            {
                if (m_E == null || !m_E.MoveNext()) return false;
                if (m_E.Current != null) Result = m_E.Current;
                return true;
            }
            public object Current { get { return null; } }
            public void Reset() { }
        }

        static Driven Drive(object r) { return new Driven(r); }

        static Dictionary<string, object> Pick(object o, params string[] keys)
        {
            var d = o as Dictionary<string, object>;
            var r = new Dictionary<string, object>();
            if (d == null) return r;
            foreach (var k in keys) if (d.TryGetValue(k, out var v)) r[k] = v;
            return r;
        }
    }
}
