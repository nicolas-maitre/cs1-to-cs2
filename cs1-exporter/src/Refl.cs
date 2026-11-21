using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;

namespace Cs1Exporter
{
    /// <summary>Reflection helpers for reading other mods' data without a compile-time dependency.</summary>
    public static class Refl
    {
        public const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        public const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        /// <summary>
        /// Test hook (ExportOptions.SimulateMissing): namespaces treated as if their mod were not installed, so the
        /// real not-found paths of the reflection readers run. Set for one export and cleared afterwards.
        /// </summary>
        public static readonly List<string> HiddenNamespaces = new List<string>();

        public static bool IsHidden(string fullName)
        {
            if (fullName == null) return false;
            foreach (var ns in HiddenNamespaces) if (fullName.StartsWith(ns, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>First loaded type with this full name (any assembly), or null.</summary>
        public static Type FindType(string fullName)
        {
            if (IsHidden(fullName)) return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(fullName, false); } catch { continue; }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Property or field (public or not, walking base types), or null.</summary>
        public static object Get(object o, string name)
        {
            if (o == null) return null;
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, Inst | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
                var f = t.GetField(name, Inst | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        public static object GetStatic(Type t, string name)
        {
            for (; t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, Stat | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(null, null);
                var f = t.GetField(name, Stat | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(null);
            }
            return null;
        }

        public static int ToInt(object o) { return o == null ? 0 : Convert.ToInt32(o); }
        public static uint ToUInt(object o) { return o == null ? 0u : Convert.ToUInt32(o); }

        /// <summary>
        /// Writes any plain data object: primitives, strings, enums (as their name), Vector3, Nullable (null or value),
        /// IDictionary (as an object, keys stringified), IEnumerable (as an array) and other classes/structs as an
        /// object of their public instance fields. Depth-limited so a cycle can't hang the export.
        /// </summary>
        public static void WriteData(JsonWriter w, object o, int depth = 0)
        {
            if (o == null) { w.Null(); return; }
            if (depth > 12) { w.Value("<too deep>"); return; }
            var t = o.GetType();
            if (o is string || t.IsPrimitive || t.IsEnum || o is UnityEngine.Vector3 || o is UnityEngine.Vector2
                || o is UnityEngine.Color || o is UnityEngine.Color32) { w.Any(o); return; }
            var dict = o as IDictionary;
            if (dict != null)
            {
                w.BeginObject();
                foreach (DictionaryEntry e in dict)
                {
                    w.Name(Convert.ToString(e.Key, System.Globalization.CultureInfo.InvariantCulture));
                    WriteData(w, e.Value, depth + 1);
                }
                w.EndObject();
                return;
            }
            var list = o as IEnumerable;
            if (list != null)
            {
                w.BeginArray();
                foreach (var e in list) WriteData(w, e, depth + 1);
                w.EndArray();
                return;
            }
            w.BeginObject();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                try { v = f.GetValue(o); } catch { continue; }
                w.Name(f.Name);
                WriteData(w, v, depth + 1);
            }
            w.EndObject();
        }
    }

    /// <summary>
    /// CS1 lane id → (segment, index in NetInfo.m_lanes), built once by walking every segment's lane chain, plus the
    /// node a lane flows into.
    /// </summary>
    public sealed class LaneMap
    {
        private readonly Dictionary<uint, int> _index = new Dictionary<uint, int>();

        public LaneMap()
        {
            var nm = Singleton<NetManager>.instance;
            var segs = nm.m_segments.m_buffer;
            var lanes = nm.m_lanes.m_buffer;
            for (int s = 1; s < segs.Length; s++)
            {
                if ((segs[s].m_flags & NetSegment.Flags.Created) == 0) continue;
                uint lane = segs[s].m_lanes;
                for (int i = 0; lane != 0 && i < 256; i++)
                {
                    _index[lane] = i;
                    lane = lanes[lane].m_nextLane;
                }
            }
        }

        public ushort Segment(uint laneId)
        {
            var lanes = Singleton<NetManager>.instance.m_lanes.m_buffer;
            return laneId < lanes.Length ? lanes[laneId].m_segment : (ushort)0;
        }

        public int Index(uint laneId)
        {
            int i;
            return _index.TryGetValue(laneId, out i) ? i : -1;
        }

        /// <summary>
        /// The node the lane's traffic flows into (where its arrows and outgoing connections apply), or 0 for a
        /// two-way / unknown lane. Direction is NetInfo.Lane.m_finalDirection, flipped when the segment is inverted.
        /// </summary>
        public ushort EndNode(uint laneId)
        {
            var nm = Singleton<NetManager>.instance;
            ushort seg = Segment(laneId);
            int idx = Index(laneId);
            if (seg == 0 || idx < 0) return 0;
            var s = nm.m_segments.m_buffer[seg];
            var info = s.Info;
            if (info == null || info.m_lanes == null || idx >= info.m_lanes.Length) return 0;
            var dir = info.m_lanes[idx].m_finalDirection;
            if ((s.m_flags & NetSegment.Flags.Invert) != 0) dir = NetInfo.InvertDirection(dir);
            if ((dir & NetInfo.Direction.Both) == NetInfo.Direction.Forward) return s.m_endNode;
            if ((dir & NetInfo.Direction.Both) == NetInfo.Direction.Backward) return s.m_startNode;
            return 0;
        }
    }
}
