// Terrain files of the import package (terrain/heightmap.r16, terrain/worldmap.r16, terrain/terrain.json) and the
// plan.terrain pointer. The 4096² resampling is the expensive part of a package (two passes of 16.7 M samples), so
// it runs only when the package is written (CLI, download), not on every crop change.

import type { Cs1City, Plan } from '../types.ts';
import { convertTerrain, cs1Heights, rawToBytes } from './heightmap.ts';
import type { TerrainMeta } from './heightmap.ts';

export interface PlanTerrain {
  dir: string;
  meta: string;
  heightScale: number;
  heightOffset: number;
  heightMode: string;
  /** terrain/apply.mjs "auto" sea level: below everything (a dry map), for a city without water */
  seaLevelDry: number;
  min: number;
  max: number;
  world: boolean;
}

export interface TerrainFiles { files: { path: string; data: Uint8Array }[]; meta: TerrainMeta; terrain: PlanTerrain }

/** Heightmap + world map + metadata for the plan's crop and transform; null when the export has no terrain. */
export function terrainFiles(city: Cs1City, plan: Pick<Plan, 'crop' | 'transform' | 'source'>, opts: { world?: boolean } = {}): TerrainFiles | null {
  const t = city.terrain;
  if (!t) return null;
  const world = opts.world !== false;
  const { raw, worldRaw, meta } = convertTerrain(
    { resolution: t.resolution, size: t.size, heightScale: t.heightScale, min: t.min, max: t.max }, cs1Heights(t), plan.crop,
    { transform: { dx: plan.transform.dx, dz: plan.transform.dz }, world },
  );
  meta.source.file = plan.source.saveName ? `${plan.source.saveName} (.cs1x sha256 ${plan.source.cs1xSha256 ?? 'n/a'})` : undefined;
  const files = [{ path: 'terrain/heightmap.r16', data: rawToBytes(raw) }];
  if (worldRaw && meta.world) files.push({ path: `terrain/${meta.world.file}`, data: rawToBytes(worldRaw) });
  files.push({ path: 'terrain/terrain.json', data: new TextEncoder().encode(JSON.stringify(meta, null, 2) + '\n') });
  return {
    files, meta,
    terrain: {
      dir: 'terrain', meta: 'terrain/terrain.json', heightScale: meta.heightScale, heightOffset: meta.heightOffset, heightMode: meta.heightMode,
      seaLevelDry: Math.floor(meta.stats.min) - 20, min: meta.stats.min, max: meta.stats.max, world: !!meta.world,
    },
  };
}
