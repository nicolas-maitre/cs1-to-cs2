using System;
using ColossalFramework;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// water.json + water.r16: sea level, water sources (WaterSimulation.m_waterSources) and a water depth grid on the
    /// same 1081 × 1081 lattice as terrain.r16.
    /// Depth = TerrainManager.SampleRawHeightWithWater(world) − SampleRawHeightSmooth(world) at each sample point,
    /// in the same 1/64 m raw units as the terrain, 0 = dry.
    /// </summary>
    public sealed class WaterExporter
    {
        private readonly Report _report;
        public byte[] R16;
        public int Sources, WetSamples;
        public float SeaLevel, MaxDepth;

        public WaterExporter(Report report) { _report = report; }

        public static string SourceType(ushort t)
        {
            switch (t)
            {
                case WaterSource.TYPE_NATURAL: return "natural";
                case WaterSource.TYPE_FACILITY: return "facility";
                case WaterSource.TYPE_CLEANER: return "cleaner";
                case WaterSource.TYPE_NONE: return "none";
                default: return "type" + t;
            }
        }

        public void Write(JsonWriter w, TerrainExporter terrain)
        {
            var tm = Singleton<TerrainManager>.instance;
            var ws = tm.WaterSimulation;
            if (ws == null) throw new InvalidOperationException("TerrainManager.WaterSimulation is null");
            SeaLevel = ws.m_currentSeaLevel;

            int res = terrain != null && terrain.Resolution > 0 ? terrain.Resolution : 1081;
            float size = terrain != null && terrain.Size > 0 ? terrain.Size : 17280f;
            float scale = terrain != null && terrain.HeightScale > 0 ? terrain.HeightScale : 1f / 64f;
            float cell = size / (res - 1), half = size / 2f;
            R16 = new byte[res * res * 2];
            int i = 0;
            for (int row = 0; row < res; row++)
            {
                float z = -half + row * cell;
                for (int col = 0; col < res; col++, i += 2)
                {
                    var p = new Vector3(-half + col * cell, 0, z);
                    float depth = tm.SampleRawHeightWithWater(p, false, 0f) - tm.SampleRawHeightSmooth(p);
                    if (depth <= 0.001f) continue;
                    int raw = Mathf.Clamp(Mathf.RoundToInt(depth / scale), 0, 65535);
                    if (raw == 0) continue;
                    R16[i] = (byte)(raw & 0xFF);
                    R16[i + 1] = (byte)(raw >> 8);
                    WetSamples++;
                    if (depth > MaxDepth) MaxDepth = depth;
                }
            }

            w.BeginObject();
            w.Prop("seaLevel", SeaLevel);
            w.Prop("nextSeaLevel", ws.m_nextSeaLevel);
            w.Name("depth").BeginObject();
            w.Prop("file", "water.r16");
            w.Prop("resolution", res);
            w.Prop("size", size);
            w.Prop("heightScale", scale);
            w.Prop("meaning", "water depth above the terrain (surface − terrain), raw × heightScale = metres, 0 = dry; same lattice and row order as terrain.r16");
            w.Prop("wetSamples", WetSamples);
            w.Prop("maxDepth", MaxDepth);
            w.EndObject();

            w.Name("sources").BeginArray();
            var buf = ws.m_waterSources.m_buffer;
            int n = ws.m_waterSources.m_size;
            for (int k = 0; k < n && k < buf.Length; k++)
            {
                var s = buf[k];
                if (s.m_type == WaterSource.TYPE_NONE) continue;
                Sources++;
                w.BeginObject();
                w.Prop("index", k);
                w.Prop("id", k + 1);
                w.Prop("type", SourceType(s.m_type));
                w.Prop("inputPosition", s.m_inputPosition);
                w.Prop("outputPosition", s.m_outputPosition);
                w.Prop("inputRate", s.m_inputRate);
                w.Prop("outputRate", s.m_outputRate);
                w.Prop("target", (int)s.m_target);
                w.Prop("targetHeight", s.m_target * scale);
                w.Prop("flow", s.m_flow);
                w.Prop("water", s.m_water);
                w.Prop("pollution", s.m_pollution);
                w.Prop("terrainHeightAtOutput", tm.SampleRawHeightSmooth(s.m_outputPosition));
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            _report.Info("water", string.Format("sea level {0:F2} m, {1} water sources, {2} wet samples of {3}, max depth {4:F1} m",
                SeaLevel, Sources, WetSamples, res * res, MaxDepth));
        }
    }
}
