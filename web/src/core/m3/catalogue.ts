// Slim CS2 building catalogue: only placeable buildings and the fields buildings/match/match.mjs reads,
// so matching gives the same result (test/m3.test.ts checks it). Used by scripts/slim-catalogue.mjs (the bundled
// public/cs2-catalogue.json) and by the UI when the user loads a full catalogue.
import type { Cs2Catalogue } from "./buildings.ts";

/* eslint-disable @typescript-eslint/no-explicit-any */
const COMPONENTS = new Set(['CargoTransportStation', 'DeathcareFacility', 'EmergencyShelter', 'FireStation', 'GarbageFacility', 'GarbagePowered', 'GroundWaterPowered',
  'Hospital', 'MaintenanceDepot', 'Park', 'ParkingFacility', 'PoliceStation', 'PostFacility', 'PowerPlant', 'Prison', 'School', 'SewageOutlet', 'SignatureBuilding',
  'SolarPowered', 'TelecomFacility', 'TransportDepot', 'TransportStation', 'WaterPowered', 'WaterPumpingStation', 'WindPowered']);
const FIELDS = ['name', 'kind', 'type', 'lot', 'size', 'height', 'service', 'uiGroup', 'uiMenu', 'zone', 'level', 'theme', 'content', 'placeable', 'upgrade', 'capacity'];

export function slimCatalogue(full: any): Cs2Catalogue & { format: string; formatVersion: number; source: unknown } {
  const prefabs = (full.prefabs ?? []).filter((p: any) => p.kind === 'building' && p.placeable).map((p: any) => {
    const o: any = {};
    for (const f of FIELDS) if (p[f] !== undefined && p[f] !== null) o[f] = p[f];
    const comps = Object.fromEntries(Object.entries(p.components ?? {}).filter(([k]) => COMPONENTS.has(k)));
    if (Object.keys(comps).length) o.components = comps;
    const flags = (p.flags ?? []).filter((f: string) => COMPONENTS.has(f));
    if (flags.length) o.flags = flags;
    return o;
  });
  return { format: 'cs2-catalogue-slim', formatVersion: 1, source: { format: full.format, generatedAt: full.generatedAt ?? null }, game: full.game ?? null, prefabs };
}

