using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// terrain.json + terrain.r16: TerrainManager.RawHeights as uint16 little-endian, row-major.
    ///
    /// CS1 constants (Assembly-CSharp TerrainManager, CS1 1.21): RAW_RESOLUTION = 1080 cells, RAW_CELL_SIZE = 16 m,
    /// TERRAIN_HEIGHT = 1024 m over the full uint16 range, so 1081 × 1081 samples over 17 280 m and
    /// heightScale = 1024 / 65536 = 1/64. They are read from the game at run time and the spec values are only
    /// a fallback. The index is row × 1081 + col with row 0 = minimum z and col 0 = minimum x; that is checked
    /// against the game's own TerrainManager.SampleRawHeight at probe points and against network node heights.
    /// </summary>
    public sealed class TerrainExporter
    {
        private readonly Report _report;
        public byte[] R16;
        public int Resolution;
        public float Size, HeightScale, Min, Max;
        public float ProbeMaxError = -1, NodeMaxError = -1;
        public int NodeChecks;

        public TerrainExporter(Report report) { _report = report; }

        public void Capture()
        {
            var tm = Singleton<TerrainManager>.instance;
            var raw = tm.RawHeights;
            if (raw == null) throw new InvalidOperationException("TerrainManager.RawHeights is null");

            int cells = ConstInt("RAW_RESOLUTION", 1080);
            float cell = ConstFloat("RAW_CELL_SIZE", 16f);
            float terrainHeight = ConstFloat("TERRAIN_HEIGHT", 1024f);
            Resolution = cells + 1;
            Size = cells * cell;
            HeightScale = terrainHeight / 65536f;

            if (raw.Length != Resolution * Resolution)
            {
                // Don't guess a layout we can't explain.
                int side = (int)Math.Round(Math.Sqrt(raw.Length));
                if (side * side != raw.Length) throw new InvalidOperationException("RawHeights has " + raw.Length + " samples, not a square");
                _report.Warn("terrain", "RawHeights is " + side + "×" + side + ", expected " + Resolution + "×" + Resolution + "; using " + side);
                Resolution = side;
                cell = Size / (side - 1);
            }

            R16 = new byte[raw.Length * 2];
            ushort lo = ushort.MaxValue, hi = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                ushort h = raw[i];
                R16[2 * i] = (byte)(h & 0xFF);
                R16[2 * i + 1] = (byte)(h >> 8);
                if (h < lo) lo = h;
                if (h > hi) hi = h;
            }
            Min = lo * HeightScale;
            Max = hi * HeightScale;

            VerifyAgainstGame(tm, raw, cell);
        }

        /// <summary>
        /// The game's own SampleRawHeight(x, z) at exact sample positions must equal raw[row * res + col] × scale with
        /// x = -size/2 + col × cell and z = -size/2 + row × cell. Probe points are spread over the map, including
        /// asymmetric ones, so a transposed or flipped layout can't pass by accident (unless the terrain is flat).
        /// </summary>
        private void VerifyAgainstGame(TerrainManager tm, ushort[] raw, float cell)
        {
            // SampleRawHeight(float, float) and SampleRawHeightSmooth(Vector3) take different inputs; rather than
            // hard-code a guess, test the plausible conventions (world metres or grid cells in; metres or raw units
            // out) and require that exactly the spec layout matches under the best one.
            int res = Resolution;
            float half = Size / 2f;
            string[] names = { "world→m", "cells→m", "world→raw", "cells→raw" };
            var err = new float[4];
            var errT = new float[4];
            float smoothErr = 0, smoothErrT = 0;
            int probes = 0;
            var rnd = new System.Random(1081);
            for (int k = 0; k < 400; k++)
            {
                int col = 2 + rnd.Next(res - 4), row = 2 + rnd.Next(res - 4);
                if (col == row) continue;
                float x = -half + col * cell, z = -half + row * cell;
                float ours = raw[row * res + col] * HeightScale;
                float transposed = raw[col * res + row] * HeightScale;
                float[] game =
                {
                    tm.SampleRawHeight(x, z),
                    tm.SampleRawHeight(col, row),
                    tm.SampleRawHeight(x, z) * HeightScale,
                    tm.SampleRawHeight(col, row) * HeightScale,
                };
                for (int h = 0; h < 4; h++)
                {
                    err[h] = Mathf.Max(err[h], Mathf.Abs(game[h] - ours));
                    errT[h] = Mathf.Max(errT[h], Mathf.Abs(game[h] - transposed));
                }
                // The smoothed world-space sampler (the one node heightAboveTerrain uses) at the exact sample point.
                float smooth = tm.SampleRawHeightSmooth(new Vector3(x, 0, z));
                smoothErr = Mathf.Max(smoothErr, Mathf.Abs(smooth - ours));
                smoothErrT = Mathf.Max(smoothErrT, Mathf.Abs(smooth - transposed));
                probes++;
            }
            int best = 0;
            for (int h = 1; h < 4; h++) if (err[h] < err[best]) best = h;
            ProbeMaxError = err[best];
            var detail = new System.Text.StringBuilder();
            for (int h = 0; h < 4; h++) detail.AppendFormat("{0}: {1:F3}/{2:F3} m; ", names[h], err[h], errT[h]);
            detail.AppendFormat("SampleRawHeightSmooth(world): {0:F3}/{1:F3} m", smoothErr, smoothErrT);
            if (err[best] <= 0.05f && errT[best] > 1f)
                _report.Info("terrain", string.Format("orientation verified: TerrainManager.SampleRawHeight ({0}) == raw[row*{1}+col]*{2} at {3} probes, max error {4:F4} m (transposed {5:F1} m). Max error spec/transposed per convention: {6}",
                    names[best], res, HeightScale, probes, err[best], errT[best], detail));
            else
                _report.Warn("terrain", string.Format("in-game probe inconclusive over {0} probes (max error spec/transposed per convention: {1}); the node height check below is the authority", probes, detail));
        }

        /// <summary>Bilinear sample of the exported grid at world (x, z), in metres.</summary>
        public float Sample(float x, float z)
        {
            int res = Resolution;
            float cell = Size / (res - 1);
            float fx = Mathf.Clamp((x + Size / 2f) / cell, 0, res - 1);
            float fz = Mathf.Clamp((z + Size / 2f) / cell, 0, res - 1);
            int c0 = Mathf.Min((int)fx, res - 2), r0 = Mathf.Min((int)fz, res - 2);
            float tx = fx - c0, tz = fz - r0;
            float h00 = H(r0, c0), h01 = H(r0, c0 + 1), h10 = H(r0 + 1, c0), h11 = H(r0 + 1, c0 + 1);
            return Mathf.Lerp(Mathf.Lerp(h00, h01, tx), Mathf.Lerp(h10, h11, tx), tz);
        }

        private float H(int row, int col)
        {
            int i = 2 * (row * Resolution + col);
            return (R16[i] | (R16[i + 1] << 8)) * HeightScale;
        }

        /// <summary>
        /// Second check, against the network: for ground nodes, position.y − heightAboveTerrain is the game's smoothed
        /// terrain height there; our bilinear sample should be close to it (smoothing makes it not exact).
        /// </summary>
        public void VerifyAgainstNodes()
        {
            if (R16 == null) return;
            var tm = Singleton<TerrainManager>.instance;
            var buf = Singleton<NetManager>.instance.m_nodes.m_buffer;
            var errs = new List<float>();
            for (int i = 1; i < buf.Length; i++)
            {
                var n = buf[i];
                if ((n.m_flags & NetNode.Flags.Created) == 0 || (n.m_flags & NetNode.Flags.Deleted) != 0) continue;
                if ((n.m_flags & NetNode.Flags.OnGround) == 0 || n.m_elevation != 0) continue;
                float game = tm.SampleRawHeightSmooth(n.m_position);
                errs.Add(Mathf.Abs(game - Sample(n.m_position.x, n.m_position.z)));
            }
            NodeChecks = errs.Count;
            if (errs.Count == 0) { _report.Warn("terrain", "no ground nodes to check the terrain against"); return; }
            errs.Sort();
            NodeMaxError = errs[errs.Count - 1];
            float median = errs[errs.Count / 2], p99 = errs[(int)(errs.Count * 0.99)];
            var msg = string.Format("node check: {0} ground nodes, |game smoothed height − bilinear r16| median {1:F3} m, p99 {2:F3} m, max {3:F3} m",
                errs.Count, median, p99, NodeMaxError);
            if (median > 0.5f) _report.Error("terrain", msg + " (too large: layout is probably wrong)");
            else _report.Info("terrain", msg);
        }

        /// <summary>Drops the heightmap bytes once they are in the zip (the grid parameters stay for water.json).</summary>
        public void R16Release() { R16 = null; }

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject();
            w.Prop("resolution", Resolution);
            w.Prop("size", Size);
            w.Prop("heightScale", HeightScale);
            w.Prop("file", "terrain.r16");
            w.Prop("rowOrder", "row 0 = minimum z (south), column 0 = minimum x (west)");
            w.Prop("byteOrder", "little-endian uint16");
            w.Prop("source", "TerrainManager.RawHeights");
            w.Name("seaLevel").Null();
            w.Prop("min", Min);
            w.Prop("max", Max);
            w.Name("verification").BeginObject();
            w.Prop("probeMaxErrorM", ProbeMaxError);
            w.Prop("nodeChecks", NodeChecks);
            w.Prop("nodeMaxErrorM", NodeMaxError);
            w.EndObject();
            w.EndObject();
        }

        private static int ConstInt(string name, int fallback)
        {
            try
            {
                var f = typeof(TerrainManager).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f != null) return Convert.ToInt32(f.GetValue(null));
            }
            catch { }
            return fallback;
        }

        private static float ConstFloat(string name, float fallback)
        {
            try
            {
                var f = typeof(TerrainManager).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f != null) return Convert.ToSingle(f.GetValue(null));
            }
            catch { }
            return fallback;
        }
    }
}
