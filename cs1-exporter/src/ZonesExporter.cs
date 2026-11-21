using System;
using System.Collections.Generic;
using System.Text;
using ColossalFramework;
using ColossalFramework.Math;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// zones.json: every created ZoneBlock with its position, angle, row count, segment and per-cell zone / valid /
    /// occupied state.
    ///
    /// ZoneBlock layout (Assembly-CSharp 1.21, by IL inspection):
    ///  - 4 columns (COLUMN_COUNT) × RowCount rows ((m_flags &amp; 0xFF00) &gt;&gt; 8, at most 8), each cell 8 m.
    ///  - GetZone(x, z): x = column 0..3, z = row; the 4-bit zone code is in m_zone1 (x &lt; 2) or m_zone2 (x ≥ 2)
    ///    at bit (z &lt;&lt; 3 | (x &amp; 1) &lt;&lt; 2).
    ///  - m_valid / m_shared / m_occupied1 / m_occupied2: one bit per cell at (z &lt;&lt; 3 | x).
    /// Cell centres are computed with the game's formula and checked against the block's road (report item).
    /// </summary>
    public sealed class ZonesExporter
    {
        private readonly Report _report;
        public int Blocks, Cells, ZonedCells, OccupiedCells;
        public readonly Dictionary<string, int> ByZone = new Dictionary<string, int>();

        public ZonesExporter(Report report) { _report = report; }

        public static readonly string[] ZoneNames =
            { "Unzoned", "Distant", "ResidentialLow", "ResidentialHigh", "CommercialLow", "CommercialHigh", "Industrial", "Office" };

        public static string ZoneName(int code)
        {
            if (code >= 0 && code < ZoneNames.Length) return ZoneNames[code];
            return code == 15 ? "None" : "Zone" + code;
        }

        /// <summary>Cell centre: position + (x − 3.5)·8·colDir + (z − 3.5)·8·rowDir, colDir = (cos a, sin a), rowDir = (sin a, −cos a) in XZ.</summary>
        public static Vector3 CellCentre(ref ZoneBlock b, int x, int z)
        {
            var colDir = new Vector3(Mathf.Cos(b.m_angle), 0, Mathf.Sin(b.m_angle)) * 8f;
            var rowDir = new Vector3(colDir.z, 0, -colDir.x);
            return b.m_position + colDir * (x - 3.5f) + rowDir * (z - 3.5f);
        }

        public void Write(JsonWriter w)
        {
            var zm = Singleton<ZoneManager>.instance;
            var blocks = zm.m_blocks.m_buffer;
            var segs = Singleton<NetManager>.instance.m_segments.m_buffer;

            // Geometry check: mean distance of each column's cell centres from the block's road centre line.
            var colDist = new double[4];
            var colN = new int[4];

            w.BeginObject();
            w.Prop("cellSize", 8);
            w.Prop("columns", 4);
            w.Name("zoneCodes").BeginObject();
            for (int i = 0; i < ZoneNames.Length; i++) w.Prop(i.ToString(), ZoneNames[i]);
            w.Prop("15", "None");
            w.EndObject();
            w.Name("blocks").BeginArray();
            var zone = new StringBuilder(32);
            var occ = new StringBuilder(32);
            for (int i = 1; i < blocks.Length; i++)
            {
                var b = blocks[i];
                if ((b.m_flags & ZoneBlock.FLAG_CREATED) == 0 || (b.m_flags & ZoneBlock.FLAG_DELETED) != 0) continue;
                int rows = Mathf.Clamp(b.RowCount, 0, 8);
                zone.Length = 0;
                occ.Length = 0;
                for (int z = 0; z < rows; z++)
                {
                    for (int x = 0; x < 4; x++)
                    {
                        ulong bit = 1UL << ((z << 3 | x) & 63);
                        bool valid = (b.m_valid & bit) != 0;
                        int code = (int)b.GetZone(x, z);
                        bool occupied = ((b.m_occupied1 | b.m_occupied2) & bit) != 0;
                        zone.Append(valid ? "0123456789ABCDEF"[code & 15] : '-');
                        occ.Append(occupied ? '1' : '0');
                        if (!valid) continue;
                        Cells++;
                        if (code >= 2 && code <= 7) ZonedCells++;
                        if (occupied) OccupiedCells++;
                        string zn = ZoneName(code);
                        int c;
                        ByZone.TryGetValue(zn, out c);
                        ByZone[zn] = c + 1;
                    }
                }

                if (b.m_segment != 0 && rows > 0 && (Blocks & 7) == 0)
                {
                    try
                    {
                        var seg = segs[b.m_segment];
                        for (int x = 0; x < 4; x++)
                        {
                            var p = CellCentre(ref b, x, 0);
                            Vector3 cp, cd;
                            seg.GetClosestPositionAndDirection(p, out cp, out cd);
                            colDist[x] += VectorUtils.LengthXZ(p - cp);
                            colN[x]++;
                        }
                    }
                    catch { }
                }

                Blocks++;
                w.BeginObject();
                w.Prop("id", i);
                w.Prop("position", b.m_position);
                w.Prop("angle", b.m_angle);
                w.Prop("rows", rows);
                w.Prop("segment", (int)b.m_segment);
                w.Prop("zones", zone.ToString());
                w.Prop("occupied", occ.ToString());
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();

            var sb = new StringBuilder();
            for (int x = 0; x < 4; x++) sb.AppendFormat("col {0}: {1:F1} m; ", x, colN[x] > 0 ? colDist[x] / colN[x] : double.NaN);
            _report.Info("zones", Blocks + " blocks, " + Cells + " valid cells, " + ZonedCells + " zoned, " + OccupiedCells + " occupied");
            _report.Info("zones", "geometry check, mean distance of cell centres (row 0) from the block's road centre line: " + sb);
        }
    }
}
