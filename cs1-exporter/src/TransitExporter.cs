using System;
using ColossalFramework;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// transit.json: TransportManager lines with type, colour, name and ordered stops.
    /// Stops are the line's NetNodes, walked with TransportLine.GetNextStop from m_stops until the loop closes. Each
    /// stop node's m_lane is the road/track lane it stands on (→ segment); for station-based types the nearest
    /// public-transport building of the line's sub-service within 64 m is given as the station.
    /// </summary>
    public sealed class TransitExporter
    {
        private readonly Report _report;
        public int Lines, Stops;

        public TransitExporter(Report report) { _report = report; }

        public void Write(JsonWriter w, LaneMap lanes)
        {
            var tm = Singleton<TransportManager>.instance;
            var nm = Singleton<NetManager>.instance;
            var bm = Singleton<BuildingManager>.instance;
            var buf = tm.m_lines.m_buffer;
            w.BeginObject();
            w.Name("lines").BeginArray();
            for (int i = 1; i < buf.Length; i++)
            {
                var line = buf[i];
                if ((line.m_flags & TransportLine.Flags.Created) == 0 || (line.m_flags & TransportLine.Flags.Deleted) != 0) continue;
                TransportInfo info = null;
                try { info = line.Info; } catch { }
                string name = null;
                Color color = Color.white;
                try { name = tm.GetLineName((ushort)i); } catch { }
                try { color = tm.GetLineColor((ushort)i); } catch { }

                Lines++;
                w.BeginObject();
                w.Prop("id", i);
                w.Prop("name", name);
                w.Prop("number", (int)line.m_lineNumber);
                w.Prop("info", info != null ? info.name : null);
                w.Prop("type", info != null ? info.m_transportType.ToString() : null);
                w.Prop("vehicleType", info != null ? info.m_vehicleType.ToString() : null);
                w.Name("color").BeginArray().Value(Mathf.RoundToInt(color.r * 255)).Value(Mathf.RoundToInt(color.g * 255)).Value(Mathf.RoundToInt(color.b * 255)).EndArray();
                w.Prop("customColor", (line.m_flags & TransportLine.Flags.CustomColor) != 0);
                w.Prop("complete", line.Complete);
                w.Prop("vehicles", CountVehicles(line.m_vehicles));
                w.Prop("flags", line.m_flags.ToString());

                w.Name("stops").BeginArray();
                ushort first = line.m_stops, stop = first;
                for (int guard = 0; stop != 0 && guard < 1024; guard++)
                {
                    var node = nm.m_nodes.m_buffer[stop];
                    uint lane = node.m_lane;
                    ushort seg = lane != 0 ? lanes.Segment(lane) : (ushort)0;
                    ushort building = 0;
                    if (info != null && info.m_stationService != ItemClass.Service.None && info.m_class != null)
                    {
                        try { building = bm.FindBuilding(node.m_position, 64f, info.m_stationService, info.m_class.m_subService, Building.Flags.None, Building.Flags.None); }
                        catch { }
                    }
                    w.BeginObject();
                    w.Prop("node", (int)stop);
                    w.Prop("position", node.m_position);
                    w.Prop("lane", lane);
                    w.Prop("segment", (int)seg);
                    w.Prop("laneIndex", lane != 0 ? lanes.Index(lane) : -1);
                    w.Prop("laneOffset", node.m_laneOffset / 255f);
                    w.Prop("building", (int)building);
                    w.EndObject();
                    Stops++;
                    stop = TransportLine.GetNextStop(stop);
                    if (stop == first) break;
                }
                w.EndArray();
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            _report.Info("transit", Lines + " transport lines, " + Stops + " stops");
        }

        private static int CountVehicles(ushort first)
        {
            var vm = Singleton<VehicleManager>.instance.m_vehicles.m_buffer;
            int n = 0;
            for (ushort v = first; v != 0 && n < 65536; v = vm[v].m_nextLineVehicle) n++;
            return n;
        }
    }
}
