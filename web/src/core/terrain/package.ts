// Terrain files of the import package (terrain/heightmap.r16, terrain/worldmap.r16, terrain/terrain.json) and the
// plan.terrain pointer. The 4096² resampling is the expensive part of a package (two passes of 16.7 M samples), so
// it runs only when the package is written (CLI, download), not on every crop change. With road-conformed terrain
// (#110–113) the conform roads are burned in (conform.ts) and the water sources keep their absolute level.

import type { Cs1City, Plan } from '../types.ts';
import { adjustWaterSources, conformTerrain } from './conform.ts';
import type { ConformStats } from './conform.ts';
import { convertTerrain, cs1Heights, rawToBytes, CS2_TERRAIN } from './heightmap.ts';
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
  roadConformed?: boolean;
}

export interface TerrainFiles {
  files: { path: string; data: Uint8Array }[]; meta: TerrainMeta; terrain: PlanTerrain;
  /** road-conformed terrain: the stats (with the skipped segment ids) and how many water sources were adjusted */
  conform?: ConformStats | null; waterSourcesAdjusted?: number;
}

/**
 * Heightmap + world map + metadata for the plan's crop and transform; null when the export has no terrain.
 * With `conform` (default on) the plan's conform ground roads are burned in, and plan.water.apply sources are adjusted
 * in place so their absolute level is kept.
 */
export function terrainFiles(city: Cs1City, plan: Pick<Plan, 'crop' | 'transform' | 'source'> & Partial<Pick<Plan, 'segments' | 'nodes' | 'prefabs' | 'water'>>, opts: { world?: boolean; conform?: boolean } = {}): TerrainFiles | null {
  const t = city.terrain;
  if (!t) return null;
  const world = opts.world !== false;
  const { dx, dz } = plan.transform;
  let stats: ConformStats | null = null;
  const wantConform = opts.conform !== false && !!plan.segments?.length && !!plan.prefabs && Object.values(plan.prefabs).some(p => p.heightMode === 'conform');
  const d = city.water?.depth, g = city.water?.depthGrid;
  const burn = wantConform ? (grid: Float32Array, N: number, size: number) => {
    const depth = d && g ? Float32Array.from(g, v => v * d.heightScale) : null;
    stats = conformTerrain(grid, N, size, {
      segments: plan.segments!, nodes: plan.nodes ?? [], prefabs: plan.prefabs!,
      water: { seaLevel: city.water?.seaLevel ?? null, depthMeta: d && depth ? { resolution: d.resolution, size: d.size, heightScale: d.heightScale } : null, depth, dx, dz },
    });
    if (!stats) return null;
    const { skippedIds: _s, ...json } = stats;
    return json as unknown as Record<string, unknown>;
  } : undefined;
  const { raw, worldRaw, meta, natural, burned } = convertTerrain(
    { resolution: t.resolution, size: t.size, heightScale: t.heightScale, min: t.min, max: t.max }, cs1Heights(t), plan.crop,
    { transform: { dx, dz }, world, burn },
  );
  let adjusted = 0;
  if (natural && burned && plan.water?.apply?.sources?.length) adjusted = adjustWaterSources(plan.water.apply.sources, natural, burned, CS2_TERRAIN.resolution, CS2_TERRAIN.size);
  meta.source.file = plan.source.saveName ? `${plan.source.saveName} (.cs1x sha256 ${plan.source.cs1xSha256 ?? 'n/a'})` : undefined;
  const files = [{ path: 'terrain/heightmap.r16', data: rawToBytes(raw) }];
  if (worldRaw && meta.world) files.push({ path: `terrain/${meta.world.file}`, data: rawToBytes(worldRaw) });
  files.push({ path: 'terrain/terrain.json', data: new TextEncoder().encode(JSON.stringify(meta, null, 2) + '\n') });
  return {
    files, meta, conform: stats, waterSourcesAdjusted: adjusted,
    terrain: {
      dir: 'terrain', meta: 'terrain/terrain.json', heightScale: meta.heightScale, heightOffset: meta.heightOffset, heightMode: meta.heightMode,
      seaLevelDry: Math.floor(meta.stats.min) - 20, min: meta.stats.min, max: meta.stats.max, world: !!meta.world,
      ...(meta.roadConformed ? { roadConformed: true } : {}),
    },
  };
}
