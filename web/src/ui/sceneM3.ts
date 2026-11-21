// Map layers for M3: building footprints by category, district areas with names.

import { closestOnBezier } from '../../../buildings/lib/plan.mjs';
import { facingFor } from '../core/m3/buildings.ts';
import { traceOutlines } from '../core/m3/districts.ts';
import type { Cs1City } from '../core/types.ts';

/**
 * Chrome's Path2D gets quadratically slower as one path grows (40 000 squares: 4.7 s in one path, 0.2 s in paths of
 * 2 000), so big layers (zones, building footprints) are split into chunks of at most this many shapes.
 */
export const PATH_CHUNK = 2000;

export const BUILDING_STYLE: Record<string, { color: string; label: string }> = {
  Residential: { color: '#7bd17b', label: 'Residential' },
  Commercial: { color: '#6cb4f0', label: 'Commercial' },
  Industrial: { color: '#e8c25a', label: 'Industrial' },
  Office: { color: '#5fd6d6', label: 'Office' },
  service: { color: '#ff7b72', label: 'Services' },
  park: { color: '#3fbf6f', label: 'Parks' },
  transport: { color: '#c58cff', label: 'Transport' },
  utility: { color: '#8b98a5', label: 'Utilities' },
  landmark: { color: '#ffb347', label: 'Landmarks' },
};

function buildingCategory(service: string | undefined, growable: boolean): string {
  if (growable) return service && BUILDING_STYLE[service] ? service : 'Residential';
  switch (service) {
    case 'HealthCare': case 'Education': case 'FireDepartment': case 'PoliceDepartment': case 'Garbage': case 'Disaster': case 'PlayerEducation': return 'service';
    case 'Beautification': case 'Natural': return 'park';
    case 'PublicTransport': case 'Road': return 'transport';
    case 'Water': case 'Electricity': return 'utility';
    case 'Monument': case 'Tourism': return 'landmark';
    default: return 'service';
  }
}

export interface BuildingLayer {
  paths: { cat: string; color: string; path: Path2D; count: number }[];
  /** CS1 building angle convention learned from buildings with an access road (same rule as the plan). */
  convention: string;
}

/** Footprints (width × length cells, depth towards the road) of all non-sub buildings. */
export function buildBuildingLayer(city: Cs1City): BuildingLayer | null {
  const bs = city.buildings?.buildings;
  if (!bs?.length) return null;
  const segs = new Map(city.segments.map(s => [s.id, s]));
  const votes: Record<string, number> = { 'cos,sin': 0, '-sin,cos': 0, 'sin,-cos': 0, '-cos,-sin': 0 };
  let sampled = 0;
  for (const b of bs) {
    if (sampled >= 400) break;
    const seg = b.accessSegment ? segs.get(b.accessSegment) : undefined;
    if (!seg || b.parent) continue;
    const c = closestOnBezier(seg.bezier, [b.position[0], b.position[2]]);
    const dx = c.point[0] - b.position[0], dz = c.point[1] - b.position[2], l = Math.hypot(dx, dz) || 1;
    for (const k of Object.keys(votes)) { const f = facingFor(k, b.angle); if (f[0] * dx / l + f[1] * dz / l > 0.9) votes[k]++; }
    sampled++;
  }
  const convention = Object.entries(votes).sort((a, b) => b[1] - a[1])[0][0];
  const byCat = new Map<string, { cat: string; color: string; path: Path2D; count: number }>();
  const chunks: { cat: string; color: string; path: Path2D; count: number }[] = [];
  for (const b of bs) {
    if (b.parent || !b.width || !b.length) continue;
    const cat = buildingCategory(b.service, b.growable);
    let e = byCat.get(cat);
    if (!e || e.count >= PATH_CHUNK) { e = { cat, color: BUILDING_STYLE[cat].color, path: new Path2D(), count: 0 }; byCat.set(cat, e); chunks.push(e); }
    e.count++;
    const [fx, fz] = facingFor(convention, b.angle); // depth axis (towards the road)
    const px = -fz, pz = fx; // width axis
    const hw = b.width * 4 - 0.5, hl = b.length * 4 - 0.5, x = b.position[0], z = b.position[2];
    const pts = [[-hw, -hl], [hw, -hl], [hw, hl], [-hw, hl]].map(([u, v]) => [x + u * px + v * fx, -(z + u * pz + v * fz)]);
    e.path.moveTo(pts[0][0], pts[0][1]);
    for (let k = 1; k < 4; k++) e.path.lineTo(pts[k][0], pts[k][1]);
    e.path.closePath();
  }
  return { paths: chunks, convention };
}

const DISTRICT_COLORS = ['#ff6b6b', '#4dabf7', '#ffd43b', '#69db7c', '#da77f2', '#ffa94d', '#38d9a9', '#f783ac'];

export interface DistrictLayer { items: { id: number; name: string; color: string; path: Path2D; label: [number, number] }[] }

export function buildDistrictLayer(city: Cs1City): DistrictLayer | null {
  const d = city.districts;
  if (!d?.districtGrid || !d.districts.length) return null;
  const n = d.grid.resolution, cs = d.grid.cellSize, half = d.grid.size / 2;
  const items = d.districts.map((dist, i) => {
    const path = new Path2D();
    let sx = 0, sz = 0, k = 0;
    for (const loop of traceOutlines(d.districtGrid!, n, dist.id).outers) {
      loop.forEach(([gx, gy], j) => {
        const x = -half + gx * cs, z = -half + gy * cs;
        sx += x; sz += z; k++;
        if (j) path.lineTo(x, -z); else path.moveTo(x, -z);
      });
      path.closePath();
    }
    const label: [number, number] = dist.nameLocation ? [dist.nameLocation[0], dist.nameLocation[2]] : k ? [sx / k, sz / k] : [0, 0];
    return { id: dist.id, name: dist.name, color: DISTRICT_COLORS[i % DISTRICT_COLORS.length], path, label };
  });
  return { items };
}
