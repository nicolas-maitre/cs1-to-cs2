using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// districts.json + districts.u8 + parks.u8: districts and park areas (parks, industry areas, campuses, airports,
    /// pedestrian zones) as cell grids, with names, types and policies.
    ///
    /// CS1 keeps both as a grid of DistrictManager.Cell (DISTRICTGRID_RESOLUTION 512 × 512 in vanilla, 19.2 m cells).
    /// Each cell blends up to four ids with alpha weights; the raster stores the id with the highest alpha (0 = none).
    /// The resolution is taken from the array length, so a mod that enlarges the grid (81 Tiles) is handled.
    /// The orientation (row-major, row 0 = minimum z) is checked against DistrictManager.GetDistrict / GetPark.
    /// </summary>
    public sealed class DistrictsExporter
    {
        private readonly Report _report;
        public byte[] DistrictRaster, ParkRaster;
        public int Resolution, Districts, Parks;
        public float CellSize = 19.2f;

        public DistrictsExporter(Report report) { _report = report; }

        private static byte[] Raster(DistrictManager.Cell[] grid, out int res)
        {
            res = (int)Math.Round(Math.Sqrt(grid.Length));
            if (res * res != grid.Length) throw new InvalidOperationException("district grid of " + grid.Length + " cells is not square");
            var r = new byte[grid.Length];
            for (int i = 0; i < grid.Length; i++)
            {
                var c = grid[i];
                byte id = c.m_district1, a = c.m_alpha1;
                if (c.m_alpha2 > a) { id = c.m_district2; a = c.m_alpha2; }
                if (c.m_alpha3 > a) { id = c.m_district3; a = c.m_alpha3; }
                if (c.m_alpha4 > a) { id = c.m_district4; a = c.m_alpha4; }
                r[i] = id;
            }
            return r;
        }

        public void Capture()
        {
            var dm = Singleton<DistrictManager>.instance;
            int res2;
            DistrictRaster = Raster(dm.m_districtGrid, out Resolution);
            ParkRaster = Raster(dm.m_parkGrid, out res2);
            if (res2 != Resolution) throw new InvalidOperationException("district grid " + Resolution + " and park grid " + res2 + " differ");
            try { CellSize = DistrictManager.DISTRICTGRID_CELL_SIZE; } catch { }
            Verify(dm);
        }

        private Vector3 CellCentre(int col, int row)
        {
            float half = Resolution * CellSize / 2f;
            return new Vector3(-half + (col + 0.5f) * CellSize, 0, -half + (row + 0.5f) * CellSize);
        }

        /// <summary>Raster vs the game's own samplers at cell centres that have a district / park.</summary>
        private void Verify(DistrictManager dm)
        {
            // Up to 400 evenly spaced non-empty cells of each raster. GetDistrict/GetPark(Vector3) look up the cell under a
            // point (SampleDistrict would blend neighbouring cells and differ on area edges).
            int checkedD = 0, okD = 0, checkedP = 0, okP = 0;
            var dCells = new List<int>();
            var pCells = new List<int>();
            for (int i = 0; i < DistrictRaster.Length; i++)
            {
                if (DistrictRaster[i] != 0) dCells.Add(i);
                if (ParkRaster[i] != 0) pCells.Add(i);
            }
            for (int k = 0; k < Math.Min(400, dCells.Count); k++)
            {
                int i = dCells[(int)((long)k * dCells.Count / Math.Min(400, dCells.Count))];
                checkedD++;
                if (dm.GetDistrict(CellCentre(i % Resolution, i / Resolution)) == DistrictRaster[i]) okD++;
            }
            for (int k = 0; k < Math.Min(400, pCells.Count); k++)
            {
                int i = pCells[(int)((long)k * pCells.Count / Math.Min(400, pCells.Count))];
                checkedP++;
                if (dm.GetPark(CellCentre(i % Resolution, i / Resolution)) == ParkRaster[i]) okP++;
            }
            var msg = string.Format("raster check vs DistrictManager.GetDistrict/GetPark (exact cell lookup) at cell centres: districts {0}/{1}, parks {2}/{3} agree", okD, checkedD, okP, checkedP);
            bool bad = (checkedD > 20 && okD < checkedD * 0.9) || (checkedP > 20 && okP < checkedP * 0.9);
            if (bad) _report.Warn("districts", msg + " (layout may be wrong)"); else _report.Info("districts", msg);
        }

        public void WriteJson(JsonWriter w)
        {
            var dm = Singleton<DistrictManager>.instance;
            var districtCells = new int[256];
            var parkCells = new int[256];
            foreach (var b in DistrictRaster) districtCells[b]++;
            foreach (var b in ParkRaster) parkCells[b]++;
            var policies = (DistrictPolicies.Policies[])Enum.GetValues(typeof(DistrictPolicies.Policies));

            w.BeginObject();
            w.Name("grid").BeginObject();
            w.Prop("resolution", Resolution);
            w.Prop("cellSize", CellSize);
            w.Prop("size", Resolution * CellSize);
            w.Prop("districtFile", "districts.u8");
            w.Prop("parkFile", "parks.u8");
            w.Prop("rowOrder", "uint8 id per cell, row 0 = minimum z (south), column 0 = minimum x (west); 0 = none");
            w.EndObject();

            // District 0 is the whole city: its policies are the city-wide ones.
            w.Name("city").BeginObject();
            w.Name("policies"); Policies(w, policies, p => dm.m_districts.m_buffer[0].IsPolicySet(p));
            w.EndObject();

            w.Name("districts").BeginArray();
            var dbuf = dm.m_districts.m_buffer;
            for (int id = 1; id < dbuf.Length; id++)
            {
                var d = dbuf[id];
                if ((d.m_flags & District.Flags.Created) == 0) continue;
                Districts++;
                w.BeginObject();
                w.Prop("id", id);
                w.Prop("name", SafeName(() => dm.GetDistrictName(id)));
                w.Prop("nameLocation", d.m_nameLocation);
                w.Prop("cells", districtCells[id]);
                w.Prop("area", districtCells[id] * CellSize * CellSize);
                w.Name("policies"); Policies(w, policies, p => d.IsPolicySet(p));
                w.EndObject();
            }
            w.EndArray();

            w.Name("parks").BeginArray();
            var pbuf = dm.m_parks.m_buffer;
            for (int id = 1; id < pbuf.Length; id++)
            {
                var p = pbuf[id];
                if ((p.m_flags & DistrictPark.Flags.Created) == 0) continue;
                Parks++;
                w.BeginObject();
                w.Prop("id", id);
                w.Prop("name", SafeName(() => dm.GetParkName(id)));
                w.Prop("type", p.m_parkType.ToString());
                w.Prop("kind", p.IsIndustry ? "industry" : p.IsCampus ? "campus" : p.IsPark ? "park"
                    : p.m_parkType.ToString() == "Airport" ? "airport" : p.m_parkType.ToString() == "PedestrianZone" ? "pedestrianZone" : "other");
                w.Prop("level", p.m_parkLevel.ToString());
                w.Prop("mainGate", (int)p.m_mainGate);
                w.Prop("cells", parkCells[id]);
                w.Prop("area", parkCells[id] * CellSize * CellSize);
                w.Name("policies"); Policies(w, policies, x => p.IsPolicySet(x));
                w.Prop("flags", p.m_flags.ToString());
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
            _report.Info("districts", Districts + " districts, " + Parks + " park/industry/campus areas, grid " + Resolution + "² × " + CellSize + " m");
        }

        private static void Policies(JsonWriter w, DistrictPolicies.Policies[] all, Func<DistrictPolicies.Policies, bool> isSet)
        {
            w.BeginArray();
            var seen = new HashSet<string>();
            foreach (var p in all)
            {
                bool set;
                try { set = isSet(p); } catch { continue; }
                if (set && seen.Add(p.ToString())) w.Value(p.ToString());
            }
            w.EndArray();
        }

        private static string SafeName(Func<string> f)
        {
            try { return f(); } catch { return null; }
        }
    }
}
