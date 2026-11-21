using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// Reads Intersection Marking Tool fillers by reflection.
    ///
    /// Why reflection and not IntersectionMarkingTool.API.dll: the public API (IMT.API.IFillerData)
    /// only exposes the filler's entrance points, not its geometry or style. The internal object
    /// graph we walk is (IMT 1.15, github.com/MacSergey/NodeMarkup):
    ///   ModsCommon.SingletonManager&lt;IMT.Manager.NodeMarkingManager|SegmentMarkingManager&gt;.Instance
    ///     : IEnumerable&lt;Marking&gt;  (Marking.Id, Marking.Type, Marking.Fillers)
    ///   MarkingFiller.Contour.TrajectoriesProcessed : List&lt;ITrajectory&gt;  (Position(t), Length)
    ///   MarkingFiller.Style.Value : BaseFillerStyle  (Type, Color, Elevation, ...)
    /// No compile-time reference, so a missing/changed IMT just yields a report entry.
    /// </summary>
    public static class ImtReader
    {
        private const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Returns the filler count, or -1 when IMT is not loaded (nothing written).</summary>
        public static int WriteFillers(JsonWriter w, Report report)
        {
            Type singletonDef = null, nodeMgr = null, segMgr = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; } catch { continue; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.FullName == "IMT.Manager.NodeMarkingManager") nodeMgr = t;
                    else if (t.FullName == "IMT.Manager.SegmentMarkingManager") segMgr = t;
                }
                if (nodeMgr != null)
                {
                    singletonDef = asm.GetType("ModsCommon.SingletonManager`1");
                    break;
                }
            }

            if (nodeMgr == null || singletonDef == null)
            {
                report.Info("imt", "IMT not loaded (IMT.Manager.NodeMarkingManager not found): no fillers exported");
                return -1;
            }
            report.Info("imt", "IMT assembly: " + nodeMgr.Assembly.GetName().Name + " " + nodeMgr.Assembly.GetName().Version);

            int count = 0;
            w.BeginObject();
            w.Name("fillers").BeginArray();
            foreach (var mgrType in new[] { nodeMgr, segMgr })
            {
                if (mgrType == null) continue;
                var instProp = singletonDef.MakeGenericType(mgrType).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var mgr = instProp.GetValue(null, null) as IEnumerable;
                if (mgr == null) { report.Warn("imt", mgrType.Name + ": Instance is null"); continue; }

                foreach (var marking in mgr)
                {
                    object fillers;
                    try { fillers = Get(marking, "Fillers"); } catch (Exception e) { report.Warn("imt", "marking.Fillers: " + Report.Describe(e)); continue; }
                    if (!(fillers is IEnumerable list)) continue;
                    foreach (var filler in list)
                    {
                        try
                        {
                            var one = new StringWriter();
                            var ow = new JsonWriter(one);
                            WriteFiller(ow, marking, filler);
                            ow.Dispose();
                            w.RawValue(one.ToString());
                            count++;
                        }
                        catch (Exception e)
                        {
                            report.Warn("imt", "IMT filler on " + SafeStr(Get(marking, "Type")) + " " + SafeStr(Get(marking, "Id")) + " failed: " +
                                       Report.Describe(e));
                        }
                    }
                }
            }
            w.EndArray();
            w.EndObject();
            return count;
        }

        private static void WriteFiller(JsonWriter w, object marking, object filler)
        {
            var contour = Get(filler, "Contour");
            var trajectories = (Get(contour, "TrajectoriesProcessed") as IList);
            if (trajectories == null || trajectories.Count == 0) trajectories = Get(contour, "TrajectoriesRaw") as IList;

            var polygon = new List<Vector3>();
            if (trajectories != null)
            {
                foreach (var tr in trajectories)
                {
                    if (tr == null) continue;
                    var posMethod = tr.GetType().GetMethod("Position", new[] { typeof(float) });
                    float length = Convert.ToSingle(Get(tr, "Length"));
                    string trType = SafeStr(Get(tr, "TrajectoryType"));
                    int steps = trType == "Line" ? 1 : Mathf.Clamp(Mathf.CeilToInt(length / 2f), 1, 32);
                    for (int i = 0; i <= steps; i++)
                    {
                        var p = (Vector3)posMethod.Invoke(tr, new object[] { (float)i / steps });
                        if (polygon.Count == 0 || (polygon[polygon.Count - 1] - p).sqrMagnitude > 1e-4f) polygon.Add(p);
                    }
                }
                if (polygon.Count > 1 && (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude < 1e-4f)
                    polygon.RemoveAt(polygon.Count - 1);
            }

            var styleProp = Get(filler, "Style");
            var style = styleProp != null ? Get(styleProp, "Value") : null;

            w.BeginObject();
            w.Prop("markingType", SafeStr(Get(marking, "Type")));   // Node / Segment
            w.Name("markingId").Any(Get(marking, "Id"));            // NetNode or NetSegment id
            w.Name("fillerId").Any(Get(filler, "Id"));
            w.Name("isMedian").Any(Get(filler, "IsMedian"));
            w.Prop("styleClass", style != null ? style.GetType().Name : null);
            w.Prop("styleType", style != null ? SafeStr(Get(style, "Type")) : null);
            object color = style != null ? Unwrap(Get(style, "Color")) : null;
            w.Name("color");
            if (color is Color32 c) w.Value(c); else w.Value((string)null);

            // Every PropertyValue<T> on the style (Elevation, CornerRadius, Theme, Angle, Step, ...).
            w.Name("styleProperties").BeginObject();
            if (style != null)
            {
                var seen = new HashSet<string>();
                foreach (var p in style.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length != 0 || !seen.Add(p.Name)) continue;
                    if (!p.PropertyType.Name.StartsWith("Property")) continue;
                    object v;
                    try { v = Unwrap(p.GetValue(style, null)); } catch { continue; }
                    w.Name(p.Name).Any(v);
                }
            }
            w.EndObject();

            w.Name("polygon").BeginArray();
            foreach (var p in polygon) w.Value(p);
            w.EndArray();
            w.EndObject();
        }

        private static object Unwrap(object propertyValue)
        {
            if (propertyValue == null) return null;
            var vp = propertyValue.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            return vp != null ? vp.GetValue(propertyValue, null) : propertyValue;
        }

        private static object Get(object o, string name)
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

        private static string SafeStr(object o) { try { return o != null ? o.ToString() : null; } catch { return null; } }
    }
}
