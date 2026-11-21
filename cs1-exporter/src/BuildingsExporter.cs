using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>buildings.json, props.json and trees.json (M3 data, exported now so it's there).</summary>
    public sealed class BuildingsExporter
    {
        private readonly Report _report;
        public int Buildings, Growables, Props, Trees;

        public BuildingsExporter(Report report) { _report = report; }

        /// <summary>"2094491511.Some Asset_Data" → "2094491511"; vanilla names → null.</summary>
        public static string WorkshopId(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            int dot = prefabName.IndexOf('.');
            if (dot <= 0) return null;
            for (int i = 0; i < dot; i++) if (prefabName[i] < '0' || prefabName[i] > '9') return null;
            return prefabName.Substring(0, dot);
        }

        public void WriteBuildings(JsonWriter w)
        {
            var bm = Singleton<BuildingManager>.instance;
            var buf = bm.m_buildings.m_buffer;
            int skipped = 0;
            w.BeginObject();
            w.Prop("angleUnit", "degrees, CS1 Building.m_angle (radians) × 180/π, same rotation sense");
            w.Name("buildings").BeginArray();
            for (int i = 1; i < buf.Length; i++)
            {
                var b = buf[i];
                if ((b.m_flags & Building.Flags.Created) == 0 || (b.m_flags & Building.Flags.Deleted) != 0) continue;
                BuildingInfo info; string name = null, ai = null, service = null, subService = null, classLevel = null, flags, customName = null;
                bool growable = false;
                try
                {
                    info = b.Info;
                    flags = b.m_flags.ToString();
                    if (info != null)
                    {
                        name = info.name;
                        ai = info.m_buildingAI != null ? info.m_buildingAI.GetType().Name : null;
                        growable = info.m_buildingAI is PrivateBuildingAI;
                        if (info.m_class != null)
                        {
                            service = info.m_class.m_service.ToString();
                            subService = info.m_class.m_subService.ToString();
                            classLevel = info.m_class.m_level.ToString();
                        }
                    }
                    if ((b.m_flags & Building.Flags.CustomName) != 0)
                    {
                        var id = InstanceID.Empty;
                        id.Building = (ushort)i;
                        customName = Singleton<InstanceManager>.instance.GetName(id);
                    }
                }
                catch (Exception e)
                {
                    if (++skipped <= 10) _report.Warn("buildings", "building " + i + " skipped: " + Report.Describe(e));
                    continue;
                }

                Buildings++;
                if (growable) Growables++;
                w.BeginObject();
                w.Prop("id", i);
                w.Prop("info", name);
                w.Prop("workshopId", WorkshopId(name));
                w.Prop("position", b.m_position);
                w.Prop("angle", b.m_angle * Mathf.Rad2Deg);
                w.Prop("service", service);
                w.Prop("subService", subService);
                w.Prop("classLevel", classLevel);
                w.Prop("level", (int)b.m_level + 1);
                w.Prop("ai", ai);
                w.Prop("growable", growable);
                w.Prop("width", (int)b.m_width);
                w.Prop("length", (int)b.m_length);
                if (info != null) w.Prop("size", info.m_size);
                w.Prop("parent", (int)b.m_parentBuilding);
                w.Prop("subBuilding", (int)b.m_subBuilding);
                w.Prop("netNode", (int)b.m_netNode);
                w.Prop("flags", flags);
                if (customName != null) w.Prop("customName", customName);
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            if (skipped > 0) _report.Warn("buildings", skipped + " buildings skipped");
            _report.Info("buildings", Buildings + " buildings (" + Growables + " growable)");
        }

        /// <summary>props.json: { infos: [name…], props: [[infoIndex, x, y, z, angleDeg, fixedHeight 0|1], …] }.</summary>
        public void WriteProps(JsonWriter w)
        {
            var buf = Singleton<PropManager>.instance.m_props.m_buffer;
            var infos = new List<string>();
            var infoIndex = new Dictionary<string, int>();
            w.BeginObject();
            w.Prop("format", "props[i] = [infoIndex, x, y, z, angleDegrees, fixedHeight]");
            w.Name("props").BeginArray();
            for (int i = 1; i < buf.Length; i++)
            {
                var p = buf[i];
                if ((p.m_flags & (ushort)PropInstance.Flags.Created) == 0 || (p.m_flags & (ushort)PropInstance.Flags.Deleted) != 0) continue;
                PropInfo info;
                try { info = p.Info; } catch { continue; }
                string name = info != null ? info.name : "?";
                int idx;
                if (!infoIndex.TryGetValue(name, out idx)) { idx = infos.Count; infos.Add(name); infoIndex[name] = idx; }
                var pos = p.Position;
                w.BeginArray().Value(idx).Value(pos.x).Value(pos.y).Value(pos.z).Value(p.Angle * Mathf.Rad2Deg).Value(p.FixedHeight ? 1 : 0).EndArray();
                Props++;
            }
            w.EndArray();
            w.Name("infos").BeginArray();
            foreach (var n in infos) w.Value(n);
            w.EndArray();
            w.EndObject();
            _report.Info("props", Props + " props, " + infos.Count + " prop types");
        }

        /// <summary>trees.json: { infos: [name…], trees: [[infoIndex, x, y, z, fixedHeight 0|1], …] }.</summary>
        public void WriteTrees(JsonWriter w)
        {
            var buf = Singleton<TreeManager>.instance.m_trees.m_buffer;
            var infos = new List<string>();
            var infoIndex = new Dictionary<string, int>();
            w.BeginObject();
            w.Prop("format", "trees[i] = [infoIndex, x, y, z, fixedHeight]");
            w.Name("trees").BeginArray();
            for (int i = 1; i < buf.Length; i++)
            {
                var t = buf[i];
                if ((t.m_flags & (ushort)TreeInstance.Flags.Created) == 0 || (t.m_flags & (ushort)TreeInstance.Flags.Deleted) != 0) continue;
                TreeInfo info;
                try { info = t.Info; } catch { continue; }
                string name = info != null ? info.name : "?";
                int idx;
                if (!infoIndex.TryGetValue(name, out idx)) { idx = infos.Count; infos.Add(name); infoIndex[name] = idx; }
                var pos = t.Position;
                w.BeginArray().Value(idx).Value(pos.x).Value(pos.y).Value(pos.z).Value(t.FixedHeight ? 1 : 0).EndArray();
                Trees++;
            }
            w.EndArray();
            w.Name("infos").BeginArray();
            foreach (var n in infos) w.Value(n);
            w.EndArray();
            w.EndObject();
            _report.Info("trees", Trees + " trees, " + infos.Count + " tree types");
        }
    }
}
