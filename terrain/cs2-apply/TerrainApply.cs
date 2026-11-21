// Hot-loaded exec library for the CS2 bridge: puts a converted CS1 heightmap into the running city.
// Entry point: Cs1ConvTerrain.Ops.Run(args), dispatched on args["op"]. terrain/apply.mjs drives it through gamectl.
// Background: terrain/NOTES-cs2-terrain.md.
//
// Ops (world metres):
//   info    {}                                       terrain + water state (scale/offset, areas, sea level, sources)
//   apply   {file, heightScale, heightOffset, worldFile?, seaLevel?, resetWater=true, removeWorldmap=false, refresh=true, frames=10, points?}
//           file = absolute path of heightmap.r16 (4096² LE uint16, row 0 = south, 14 336 m). worldFile = the same
//           for the 57 344 m world (backdrop) map. Sets the height scale/offset, replaces the world map (or drops
//           it with removeWorldmap) and the heightmap, optionally sets the sea level, queues the playable area for
//           GroundHeightSystem (objects follow the ground), waits `frames` and samples `points` ([[x,z],...]).
//           Coroutine.
//   refresh {x0,z0,x1,z1}? (default: playable area)  TerrainSystem.OnAreaChanged: objects/nets there re-snap
//   sample  {points: [[x,z],...], frames=0}          TerrainUtils.SampleHeight at each point (CPU heights)
//   objects {x, z, r=500, max=2000}                  trees/objects within r: |transform.y - terrain| stats, to see
//                                                    whether objects followed the new terrain
//   sea     {level}                                  set WaterSystem.SeaLevel only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Game.Common;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Cs1ConvTerrain
{
    public static class Ops
    {
        const int Res = 4096;

        public static object Run(Dictionary<string, object> a)
        {
            string op = Str(a, "op", "info");
            switch (op)
            {
                case "info": return Info();
                case "apply": return Apply(a);
                case "sample": return Delay((int)F(a, "frames", 0f), () => Sample(Points(a)));
                case "objects": return Objects(F(a, "x"), F(a, "z"), F(a, "r", 500f), (int)F(a, "max", 2000f));
                case "sea": { var w = Water; w.SeaLevel = F(a, "level"); return new Dictionary<string, object> { { "seaLevel", w.SeaLevel } }; }
                case "refresh": return Refresh(a);
                default: throw new ArgumentException("unknown op " + op);
            }
        }

        static World W => World.DefaultGameObjectInjectionWorld;
        static TerrainSystem Terrain => W.GetOrCreateSystemManaged<TerrainSystem>();
        static WaterSystem Water => W.GetOrCreateSystemManaged<WaterSystem>();

        // ---------------------------------------------------------------- info

        static Dictionary<string, object> Info()
        {
            var t = Terrain;
            var hm = t.heightmap;
            var wm = t.worldHeightmap;
            var d = new Dictionary<string, object>
            {
                { "heightScaleOffset", V(t.heightScaleOffset) },
                { "playableArea", V(t.playableArea) }, { "playableOffset", V(t.playableOffset) },
                { "worldSize", V(t.worldSize) }, { "worldOffset", V(t.worldOffset) }, { "worldHeightMinMax", V(t.worldHeightMinMax) },
                { "positionOffset", V(t.positionOffset) },
                { "heightmap", hm == null ? null : new Dictionary<string, object> { { "w", hm.width }, { "h", hm.height }, { "format", hm.graphicsFormat.ToString() } } },
                { "worldHeightmap", wm == null ? null : new Dictionary<string, object> { { "w", wm.width }, { "h", wm.height }, { "format", wm.graphicsFormat.ToString() } } },
                { "hasBackdrop", TerrainSystem.HasBackdrop }, { "baseLod", TerrainSystem.baseLod },
            };
            var data = t.GetHeightData(true);
            if (data.isCreated)
            {
                d["heightData"] = new Dictionary<string, object> { { "resolution", V(data.resolution) }, { "scale", V(data.scale) }, { "offset", V(data.offset) } };
                d["centreHeight"] = TerrainUtils.SampleHeight(ref data, float3.zero);
            }
            var w = Water;
            d["water"] = new Dictionary<string, object> { { "seaLevel", w.SeaLevel }, { "useLegacySources", w.UseLegacyWaterSources }, { "sources", WaterSources() } };
            return d;
        }

        static List<object> WaterSources()
        {
            var list = new List<object>();
            var q = W.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WaterSourceData>(), ComponentType.ReadOnly<Game.Objects.Transform>());
            using (var ents = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in ents)
                {
                    var s = W.EntityManager.GetComponentData<WaterSourceData>(e);
                    var tr = W.EntityManager.GetComponentData<Game.Objects.Transform>(e);
                    list.Add(new Dictionary<string, object>
                    {
                        { "entity", e.Index + ":" + e.Version }, { "pos", V(tr.m_Position) }, { "height", s.m_Height }, { "radius", s.m_Radius },
                        { "constantDepth", s.m_ConstantDepth }, { "multiplier", s.m_Multiplier }, { "id", s.m_Id },
                    });
                }
            }
            q.Dispose();
            return list;
        }

        // ---------------------------------------------------------------- apply

        static IEnumerator Apply(Dictionary<string, object> a)
        {
            string file = Str(a, "file", null);
            if (string.IsNullOrEmpty(file) || !Path.IsPathRooted(file)) throw new ArgumentException("file must be an absolute path");
            float scale = F(a, "heightScale", 1024f), offset = F(a, "heightOffset", 0f);
            int frames = (int)F(a, "frames", 10f);
            var sw = Stopwatch.StartNew();
            string worldFile = Str(a, "worldFile", null);
            bool removeWorld = B(a, "removeWorldmap");
            var tex = LoadR16(file, "cs1conv-heightmap");
            var worldTex = !removeWorld && !string.IsNullOrEmpty(worldFile) ? LoadR16(worldFile, "cs1conv-worldmap") : null;
            long readMs = sw.ElapsedMilliseconds, texMs = readMs;

            var t = Terrain;
            var before = Info();
            t.SetTerrainProperties(new float2(scale, offset)); // scale/offset first: the Replace* calls keep the current ones
            // In game mode SetWorldHeightmap keeps the passed texture itself as worldHeightmap: never destroy worldTex.
            if (worldTex != null) t.ReplaceWorldHeightmap(worldTex);
            else if (removeWorld && t.worldHeightmap != null) t.ReplaceWorldHeightmap(null);
            t.ReplaceHeightmap(tex);
            UnityEngine.Object.Destroy(tex); // ReplaceHeightmap copied it (Graphics.CopyTexture + CPU heights)
            long replaceMs = sw.ElapsedMilliseconds;

            float? sea = a.ContainsKey("seaLevel") && a["seaLevel"] != null ? F(a, "seaLevel") : (float?)null;
            if (sea.HasValue) Water.SeaLevel = sea.Value;
            // a new sea level alone does not drain the base map's water: reset the simulation to it (default on)
            bool resetWater = sea.HasValue && (!a.ContainsKey("resetWater") || B(a, "resetWater"));
            if (resetWater) Water.ResetToSealevel();

            // ReplaceHeightmap queues nothing for GroundHeightSystem, so trees, props, buildings and nets keep their old
            // y. Queue the whole playable area, as the terrain brush does for its own area.
            object refreshed = null;
            if (!a.ContainsKey("refresh") || B(a, "refresh")) refreshed = Refresh(null);

            for (int i = 0; i < frames; i++) yield return null;

            var result = new Dictionary<string, object>
            {
                { "op", "apply" }, { "file", file }, { "heightScale", scale }, { "heightOffset", offset }, { "seaLevel", sea },
                { "worldFile", worldTex != null ? worldFile : null }, { "removeWorldmap", removeWorld }, { "refreshed", refreshed }, { "waterReset", resetWater },
                { "ms", new Dictionary<string, object> { { "read", readMs }, { "texture", texMs - readMs }, { "replace", replaceMs - texMs }, { "total", sw.ElapsedMilliseconds } } },
                { "before", before }, { "after", Info() },
            };
            if (a.ContainsKey("points")) result["samples"] = Sample(Points(a));
            yield return result;
        }

        /// <summary>4096² LE uint16 file → R16_UNorm Texture2D, CPU-readable (FinalizeTerrainData reads it back with
        /// GetRawTextureData&lt;ushort&gt;() for the CPU heights).</summary>
        static Texture2D LoadR16(string file, string name)
        {
            if (!Path.IsPathRooted(file)) throw new ArgumentException("path must be absolute: " + file);
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length != Res * Res * 2) throw new ArgumentException(file + " must be " + Res + "² uint16 (" + (Res * Res * 2) + " bytes), got " + bytes.Length);
            var tex = new Texture2D(Res, Res, GraphicsFormat.R16_UNorm, TextureCreationFlags.None) { name = name };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false, false);
            if (!TerrainSystem.IsValidHeightmapFormat(tex)) throw new InvalidOperationException("IsValidHeightmapFormat refused " + file + " (" + tex.graphicsFormat + ")");
            return tex;
        }

        // ---------------------------------------------------------------- refresh

        /// <summary>TerrainSystem.OnAreaChanged(bounds): queues the area in GroundHeightSystem, which re-snaps
        /// objects and nets there once the new heights are read back. Default: the playable area.</summary>
        static object Refresh(Dictionary<string, object> a)
        {
            var t = Terrain;
            float2 min = t.playableOffset, max = t.playableOffset + t.playableArea;
            if (a != null && a.ContainsKey("x0")) { min = new float2(F(a, "x0"), F(a, "z0")); max = new float2(F(a, "x1"), F(a, "z1")); }
            var b = new Colossal.Mathematics.Bounds2(min, max);
            t.OnAreaChanged(in b);
            return new Dictionary<string, object> { { "min", V(min) }, { "max", V(max) } };
        }

        // ---------------------------------------------------------------- sample

        static object Sample(List<float2> pts)
        {
            var data = Terrain.GetHeightData(true);
            if (!data.isCreated) throw new InvalidOperationException("terrain height data not created");
            var res = new List<object>();
            foreach (var p in pts) res.Add(new List<object> { p.x, p.y, TerrainUtils.SampleHeight(ref data, new float3(p.x, 0, p.y)) });
            return new Dictionary<string, object> { { "frame", UnityEngine.Time.frameCount }, { "points", res } };
        }

        // ---------------------------------------------------------------- objects

        static object Objects(float x, float z, float r, int max)
        {
            var em = W.EntityManager;
            var data = Terrain.GetHeightData(true);
            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Objects.Transform>(), ComponentType.ReadOnly<Game.Objects.Tree>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Game.Tools.Temp>() },
            });
            var diffs = new List<float>();
            using (var ents = q.ToEntityArray(Allocator.Temp))
            {
                foreach (var e in ents)
                {
                    var p = em.GetComponentData<Game.Objects.Transform>(e).m_Position;
                    if (math.distance(p.xz, new float2(x, z)) > r) continue;
                    diffs.Add(p.y - TerrainUtils.SampleHeight(ref data, p));
                    if (diffs.Count >= max) break;
                }
            }
            q.Dispose();
            diffs.Sort((m, n) => math.abs(m).CompareTo(math.abs(n)));
            int c = diffs.Count;
            return new Dictionary<string, object>
            {
                { "trees", c },
                { "medianAbsDy", c == 0 ? 0f : math.abs(diffs[c / 2]) },
                { "p90AbsDy", c == 0 ? 0f : math.abs(diffs[Math.Min(c - 1, (int)(c * 0.9f))]) },
                { "maxAbsDy", c == 0 ? 0f : math.abs(diffs[c - 1]) },
                { "within1m", diffs.FindAll(d => math.abs(d) <= 1f).Count },
            };
        }

        // ---------------------------------------------------------------- helpers

        static IEnumerator Delay(int frames, Func<object> f)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return f();
        }

        static List<float2> Points(Dictionary<string, object> a)
        {
            var list = new List<float2>();
            if (!a.TryGetValue("points", out var o) || !(o is IList<object> pts)) return list;
            foreach (var p in pts)
                if (p is IList<object> xz && xz.Count >= 2) list.Add(new float2(Convert.ToSingle(xz[0]), Convert.ToSingle(xz[1])));
            return list;
        }

        static List<object> V(float2 v) => new List<object> { v.x, v.y };
        static List<object> V(float3 v) => new List<object> { v.x, v.y, v.z };
        static List<object> V(int3 v) => new List<object> { v.x, v.y, v.z };
        static string Str(Dictionary<string, object> a, string k, string d) => a != null && a.TryGetValue(k, out var v) && v != null ? v.ToString() : d;
        static float F(Dictionary<string, object> a, string k, float d = 0f) => a != null && a.TryGetValue(k, out var v) && v != null ? Convert.ToSingle(v) : d;
        static bool B(Dictionary<string, object> a, string k) => a != null && a.TryGetValue(k, out var v) && v is bool b && b;
    }
}
