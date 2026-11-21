using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// assets.json: a catalogue of every building, prop, tree and network prefab the city uses, with what a matcher
    /// needs to find a CS2 equivalent: name, workshop id, title, category, footprint, height, tags and the instance
    /// count. Building prefabs also carry the simple fields of their AI (capacities, radii, worker counts, …), which
    /// is where CS1 keeps service-building specifics.
    /// </summary>
    public sealed class AssetsExporter
    {
        private readonly Report _report;
        public int BuildingTypes, PropTypes, TreeTypes, NetworkTypes;

        public AssetsExporter(Report report) { _report = report; }

        private sealed class Use<T> where T : PrefabInfo
        {
            public T Info;
            public int Count, SubBuildings;
        }

        private static void Count<T>(Dictionary<string, Use<T>> map, T info, bool sub = false) where T : PrefabInfo
        {
            if (info == null) return;
            Use<T> u;
            if (!map.TryGetValue(info.name, out u)) map[info.name] = u = new Use<T> { Info = info };
            u.Count++;
            if (sub) u.SubBuildings++;
        }

        public void Write(JsonWriter w)
        {
            var buildings = new Dictionary<string, Use<BuildingInfo>>();
            var bbuf = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
            for (int i = 1; i < bbuf.Length; i++)
            {
                if ((bbuf[i].m_flags & Building.Flags.Created) == 0 || (bbuf[i].m_flags & Building.Flags.Deleted) != 0) continue;
                try { Count(buildings, bbuf[i].Info, bbuf[i].m_parentBuilding != 0); } catch { }
            }
            var props = new Dictionary<string, Use<PropInfo>>();
            var pbuf = Singleton<PropManager>.instance.m_props.m_buffer;
            for (int i = 1; i < pbuf.Length; i++)
            {
                if ((pbuf[i].m_flags & (ushort)PropInstance.Flags.Created) == 0 || (pbuf[i].m_flags & (ushort)PropInstance.Flags.Deleted) != 0) continue;
                try { Count(props, pbuf[i].Info); } catch { }
            }
            var trees = new Dictionary<string, Use<TreeInfo>>();
            var tbuf = Singleton<TreeManager>.instance.m_trees.m_buffer;
            for (int i = 1; i < tbuf.Length; i++)
            {
                if ((tbuf[i].m_flags & (ushort)TreeInstance.Flags.Created) == 0 || (tbuf[i].m_flags & (ushort)TreeInstance.Flags.Deleted) != 0) continue;
                try { Count(trees, tbuf[i].Info); } catch { }
            }
            var nets = new Dictionary<string, Use<NetInfo>>();
            var sbuf = Singleton<NetManager>.instance.m_segments.m_buffer;
            for (int i = 1; i < sbuf.Length; i++)
            {
                if ((sbuf[i].m_flags & NetSegment.Flags.Created) == 0 || (sbuf[i].m_flags & NetSegment.Flags.Deleted) != 0) continue;
                try { Count(nets, sbuf[i].Info); } catch { }
            }

            w.BeginObject();
            w.Prop("countUnit", "instances in the city (buildings include sub-buildings; networks count segments)");

            w.Name("buildings").BeginArray();
            foreach (var u in Sorted(buildings))
                Item(w, () => WriteBuilding(w, u.Info, u.Count, u.SubBuildings), u.Info.name, ref BuildingTypes);
            w.EndArray();

            w.Name("props").BeginArray();
            foreach (var u in Sorted(props))
                Item(w, () => WriteSimple(w, u.Info, u.Count, "prop", Generated(u.Info)), u.Info.name, ref PropTypes);
            w.EndArray();

            w.Name("trees").BeginArray();
            foreach (var u in Sorted(trees))
                Item(w, () => WriteSimple(w, u.Info, u.Count, "tree", Generated(u.Info)), u.Info.name, ref TreeTypes);
            w.EndArray();

            w.Name("networks").BeginArray();
            foreach (var u in Sorted(nets))
                Item(w, () => WriteNetwork(w, u.Info, u.Count), u.Info.name, ref NetworkTypes);
            w.EndArray();
            w.EndObject();

            _report.Info("assets", BuildingTypes + " building, " + PropTypes + " prop, " + TreeTypes + " tree and " + NetworkTypes + " network prefabs in use");
        }

        private static List<Use<T>> Sorted<T>(Dictionary<string, Use<T>> map) where T : PrefabInfo
        {
            var l = new List<Use<T>>(map.Values);
            l.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Info.name, b.Info.name));
            return l;
        }

        /// <summary>
        /// Writes one catalogue entry; a prefab whose reads throw is skipped with a warning. The writers read every
        /// value that can throw before they emit anything, so a skipped entry leaves no half-written JSON.
        /// </summary>
        private void Item(JsonWriter w, Action write, string name, ref int counter)
        {
            try { write(); counter++; }
            catch (Exception e) { _report.Warn("assets", "asset '" + name + "' skipped: " + Report.Describe(e)); }
        }

        private static void Common(JsonWriter w, PrefabInfo info, int count, string kind, string title)
        {
            w.Prop("name", info.name);
            w.Prop("kind", kind);
            w.Prop("workshopId", BuildingsExporter.WorkshopId(info.name));
            w.Prop("custom", SafeBool(() => info.m_isCustomContent));
            w.Prop("title", title);
            w.Prop("count", count);
        }

        private static string Title(PrefabInfo info)
        {
            try { return info.GetLocalizedTitle(); } catch { return null; }
        }

        private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }

        private static Vector3? Generated(PrefabInfo info)
        {
            try
            {
                var gen = Refl.Get(info, "m_generatedInfo");
                var size = gen != null ? Refl.Get(gen, "m_size") : null;
                return size is Vector3 ? (Vector3?)(Vector3)size : null;
            }
            catch { return null; }
        }

        private static void WriteSimple(JsonWriter w, PrefabInfo info, int count, string kind, Vector3? size)
        {
            string title = Title(info);
            w.BeginObject();
            Common(w, info, count, kind, title);
            if (size.HasValue) { w.Prop("size", size.Value); w.Prop("height", size.Value.y); }
            w.Name("tags").BeginArray().Value(kind).Value(BuildingsExporter.WorkshopId(info.name) != null ? "workshop" : "vanilla").EndArray();
            w.EndObject();
        }

        private static void WriteNetwork(JsonWriter w, NetInfo info, int count)
        {
            string title = Title(info);
            string service = info.m_class != null ? info.m_class.m_service.ToString() : null;
            string sub = info.m_class != null ? info.m_class.m_subService.ToString() : null;
            string ai = info.m_netAI != null ? info.m_netAI.GetType().Name : null;
            float halfWidth = info.m_halfWidth;
            w.BeginObject();
            Common(w, info, count, "network", title);
            w.Prop("service", service);
            w.Prop("subService", sub);
            w.Prop("ai", ai);
            w.Prop("width", halfWidth * 2);
            var tags = new List<string> { "network", BuildingsExporter.WorkshopId(info.name) != null ? "workshop" : "vanilla" };
            if (service == "Road") tags.Add(sub == "Highway" ? "highway" : "road");
            if (sub == "PublicTransportTrain") tags.Add("train");
            if (sub == "PublicTransportMetro") tags.Add("metro");
            if (sub == "PublicTransportTram") tags.Add("tram");
            if (service == "Beautification" || (ai != null && ai.Contains("Pedestrian"))) tags.Add("path");
            if (service == "Water" || service == "Electricity") tags.Add("utility");
            if (ai != null && (ai.Contains("TransportLine") || ai.Contains("Line"))) tags.Add("transportLine");
            w.Name("tags").BeginArray();
            foreach (var t in tags) w.Value(t);
            w.EndArray();
            w.EndObject();
        }

        private static void WriteBuilding(JsonWriter w, BuildingInfo info, int count, int subCount)
        {
            // Read everything that can throw before writing anything.
            string title = Title(info);
            var cls = info.m_class;
            string service = cls != null ? cls.m_service.ToString() : null;
            string sub = cls != null ? cls.m_subService.ToString() : null;
            string level = cls != null ? cls.m_level.ToString() : null;
            string zone = null;
            try { if (cls != null) { var z = cls.GetZone(); if (z != ItemClass.Zone.None && z != ItemClass.Zone.Unzoned) zone = z.ToString(); } } catch { }
            var ai = info.m_buildingAI;
            string aiName = ai != null ? ai.GetType().Name : null;
            bool growable = ai is PrivateBuildingAI;
            var tags = Tags(info, service, sub, aiName, growable, subCount);
            var sw = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            var aiW = new JsonWriter(sw);
            if (ai != null) AiFields(aiW, ai); else aiW.Null();
            aiW.Dispose();

            w.BeginObject();
            Common(w, info, count, "building", title);
            w.Prop("service", service);
            w.Prop("subService", sub);
            w.Prop("zone", zone);
            w.Prop("level", level);
            w.Prop("ai", aiName);
            w.Prop("growable", growable);
            w.Name("footprint").BeginArray().Value(info.m_cellWidth).Value(info.m_cellLength).EndArray();
            w.Prop("size", info.m_size);
            w.Prop("height", info.m_size.y);
            w.Prop("placementMode", info.m_placementMode.ToString());
            w.Prop("subBuildings", subCount);
            w.Name("tags").BeginArray();
            foreach (var t in tags) w.Value(t);
            w.EndArray();
            w.Name("aiFields");
            w.RawValue(sw.ToString());
            w.EndObject();
        }

        private static List<string> Tags(BuildingInfo info, string service, string sub, string ai, bool growable, int subCount)
        {
            var t = new List<string>();
            t.Add(growable ? "growable" : "ploppable");
            t.Add(BuildingsExporter.WorkshopId(info.name) != null ? "workshop" : "vanilla");
            if (!growable && service != null && service != "None") t.Add("service");
            string a = ai ?? "";
            if (service == "Beautification" || a.Contains("Park")) t.Add("park");
            if (a.Contains("Station") || a.Contains("Hub") || a.Contains("Airport") || a.Contains("Harbor")) t.Add("station");
            if (a.Contains("Depot")) t.Add("depot");
            if (service == "Monument" || a.Contains("Monument")) t.Add("landmark");
            if (service == "Monument") t.Add("unique");
            if (a.Contains("Campus") || a.Contains("Faculty") || a.Contains("Varsity")) t.Add("campus");
            if (a.Contains("Extracting") || a.Contains("Processing") || a.Contains("Warehouse") || a.Contains("UniqueFactory") || a.Contains("IndustryBuilding")) t.Add("industries");
            if (a.Contains("Intersection") || service == "Road" && !growable && a.Contains("Intersection")) t.Add("intersection");
            if (a.Contains("Dummy") || a.Contains("Decoration")) t.Add("decoration");
            if (subCount > 0) t.Add("subBuilding");
            if (sub != null && sub.Contains("Eco")) t.Add("eco");
            if (sub != null && sub.EndsWith("High")) t.Add("highDensity");
            if (sub != null && sub.EndsWith("Low")) t.Add("lowDensity");
            return t;
        }

        /// <summary>Simple public fields of the building AI: capacities, radii, workers, vehicle counts, costs, …</summary>
        private static void AiFields(JsonWriter w, object ai)
        {
            w.BeginObject();
            foreach (var f in ai.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var t = f.FieldType;
                if (!(t.IsPrimitive || t.IsEnum || t == typeof(string))) continue;
                object v;
                try { v = f.GetValue(ai); } catch { continue; }
                w.Name(f.Name);
                w.Any(v);
            }
            w.EndObject();
        }
    }
}
