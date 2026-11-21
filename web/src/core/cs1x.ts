// .cs1x v1 reader (schema/README.md §1) plus an adapter for the M0 network dump
// (spikes/m0-cs1-network-dump), which has the same nodes/segments/netInfos/imtFillers fields
// but no manifest, no terrain and no parent/variants on NetInfos.

import { unzipSync, strFromU8 } from 'fflate';
import { sha256Hex } from './hash.ts';
import type { AssetsSection, BuildingsSection, Cs1City, Cs1Node, Cs1ReportItem, Cs1Segment, DistrictsSection, ImtFiller, Manifest, NetInfo, NodeControllerSection, Terrain, TmpeSection, TransitSection, WaterSection, ZonesSection } from './types.ts';

const VARIANT_FIELDS: Record<string, string> = {
  elevated: 'm_elevatedInfo', bridge: 'm_bridgeInfo', slope: 'm_slopeInfo', tunnel: 'm_tunnelInfo',
};

export function parseJson<T = unknown>(text: string): T {
  return JSON.parse(text.charCodeAt(0) === 0xfeff ? text.slice(1) : text) as T;
}

const isZip = (b: Uint8Array) => b.length > 4 && b[0] === 0x50 && b[1] === 0x4b && (b[2] === 3 || b[2] === 5);

/** .cs1x format versions this reader understands (the version bumps only on breaking changes). */
export const SUPPORTED_FORMAT_VERSION = 1;

export type ReadErrorCode = 'empty' | 'crp-save' | 'not-cs1x' | 'corrupt-zip' | 'no-manifest' | 'format-too-old' | 'format-too-new';

/** A file that cannot be read at all, with a message and a hint meant for the user. */
export class ReadError extends Error {
  code: ReadErrorCode;
  hint: string;
  constructor(code: ReadErrorCode, message: string, hint: string) {
    super(message);
    this.name = 'ReadError';
    this.code = code;
    this.hint = hint;
  }
}

const EXPORT_HINT = 'Export the city with the CS1 → CS2 Exporter mod in Cities: Skylines (Options → Mod Settings → Export city) and open the .cs1x it writes.';

/** Read a .cs1x (zip) or an M0 dump (JSON) from raw bytes. Throws ReadError for unusable files. */
export async function readCityFile(bytes: Uint8Array, opts: { hash?: boolean } = {}): Promise<Cs1City> {
  if (!bytes.length) throw new ReadError('empty', 'The file is empty.', EXPORT_HINT);
  let city: Cs1City;
  if (isZip(bytes)) city = readCs1x(bytes);
  else if (bytes[0] === 0x43 && bytes[1] === 0x52 && bytes[2] === 0x41 && bytes[3] === 0x50) {
    throw new ReadError('crp-save', 'This is a Cities: Skylines save or asset (.crp), not a .cs1x export.', EXPORT_HINT);
  } else {
    let json: unknown;
    try { json = parseJson(strFromU8(bytes)); }
    catch { throw new ReadError('not-cs1x', 'This file is neither a .cs1x export nor a network dump.', EXPORT_HINT); }
    try { city = fromM0Dump(json as never); }
    catch { throw new ReadError('not-cs1x', 'This JSON file is not a .cs1x export or a network dump.', EXPORT_HINT); }
  }
  if (opts.hash !== false) city.sha256 = await sha256Hex(bytes);
  return city;
}

/** Synchronous .cs1x reader (no hash). Throws only when the file is not a .cs1x at all. */
export function readCs1x(bytes: Uint8Array): Cs1City {
  let entries: Record<string, Uint8Array>;
  try { entries = unzipSync(bytes); }
  catch (e) { throw new ReadError('corrupt-zip', `The file looks like a zip but cannot be unpacked (${e instanceof Error ? e.message : e}).`, 'The download or copy may be incomplete. Export the city again.'); }
  const text = (name: string) => (entries[name] ? strFromU8(entries[name]) : null);
  const manifestText = text('manifest.json');
  if (manifestText == null) throw new ReadError('no-manifest', 'This zip has no manifest.json, so it is not a .cs1x export.', EXPORT_HINT);
  let manifest: Manifest;
  try { manifest = parseJson<Manifest>(manifestText); }
  catch { throw new ReadError('no-manifest', 'manifest.json in this file is not valid JSON.', 'The export may have been interrupted. Export the city again.'); }
  if (manifest.format !== 'cs1x') throw new ReadError('not-cs1x', `This zip is not a .cs1x export (format ${JSON.stringify(manifest.format)}).`, EXPORT_HINT);
  const v = Number(manifest.formatVersion);
  if (!Number.isFinite(v) || v < 1) throw new ReadError('format-too-old', `This .cs1x has format version ${manifest.formatVersion ?? '(none)'}, from a pre-release exporter.`, 'Update the CS1 → CS2 Exporter mod and export the city again.');
  if (v > SUPPORTED_FORMAT_VERSION) throw new ReadError('format-too-new', `This .cs1x has format version ${v}; this tool reads version ${SUPPORTED_FORMAT_VERSION}.`, 'The exporter is newer than this web tool. Reload the page to get the latest version of the tool.');
  const report: Cs1ReportItem[] = [];
  const exporterReport = text('report.json');
  if (exporterReport) {
    try { report.push(...(parseJson<{ items?: Cs1ReportItem[] }>(exporterReport).items ?? [])); }
    catch (e) { report.push({ level: 'warning', section: 'reader', message: `report.json unreadable: ${e}` }); }
  }
  const status = (s: string) => manifest.sections?.[s] ?? 'ok';
  // Every section is optional and may be "failed": read what is there, report what is not.
  const section = <T>(name: string, entry: string, pick: (j: never) => T, empty: T): T => {
    const st = status(name);
    const t = text(entry);
    if (t == null) {
      if (st !== 'absent') report.push({ level: st === 'failed' ? 'warning' : 'info', section: 'reader', message: `${entry} missing (section ${name}: ${st})` });
      return empty;
    }
    if (st === 'failed') report.push({ level: 'warning', section: 'reader', message: `section ${name} is marked failed; using the partial data` });
    try { return pick(parseJson(t) as never) ?? empty; }
    catch (e) { report.push({ level: 'warning', section: 'reader', message: `${entry} unreadable: ${e}` }); return empty; }
  };
  const network = section('network', 'network.json', (j: { nodes?: Cs1Node[]; segments?: Cs1Segment[] }) => j, {} as { nodes?: Cs1Node[]; segments?: Cs1Segment[] });
  const netInfos = section('netinfos', 'netinfos.json', (j: { netInfos?: NetInfo[] }) => j.netInfos ?? [], [] as NetInfo[]);
  const imtFillers = section('imt', 'imt.json', (j: { fillers?: ImtFiller[] }) => j.fillers ?? [], [] as ImtFiller[]);
  let terrain: Terrain | null = null;
  const terrainMeta = section('terrain', 'terrain.json', (j: Omit<Terrain, 'heights'> & { file?: string }) => j, null as (Omit<Terrain, 'heights'> & { file?: string }) | null);
  if (terrainMeta) {
    const raw = entries[terrainMeta.file ?? 'terrain.r16'];
    const n = terrainMeta.resolution * terrainMeta.resolution;
    if (!raw) report.push({ level: 'warning', section: 'reader', message: `${terrainMeta.file ?? 'terrain.r16'} missing` });
    else if (raw.length < n * 2) report.push({ level: 'warning', section: 'reader', message: `terrain.r16 has ${raw.length} bytes, expected ${n * 2}` });
    else {
      const heights = new Uint16Array(n);
      const dv = new DataView(raw.buffer, raw.byteOffset, raw.byteLength);
      for (let i = 0; i < n; i++) heights[i] = dv.getUint16(i * 2, true);
      terrain = { ...terrainMeta, heights };
    }
  }
  // M2 sections: read when present and not "absent"; a damaged one never stops the rest.
  const decodeR16 = (file: string, n: number) => {
    const raw = entries[file];
    if (!raw || raw.length < n * 2) { report.push({ level: 'warning', section: 'reader', message: `${file} missing or short` }); return undefined; }
    const out = new Uint16Array(n);
    const dv = new DataView(raw.buffer, raw.byteOffset, raw.byteLength);
    for (let i = 0; i < n; i++) out[i] = dv.getUint16(i * 2, true);
    return out;
  };
  const zones = section('zones', 'zones.json', (j: ZonesSection) => (Array.isArray(j.blocks) ? j : null), null as ZonesSection | null);
  const water = section('water', 'water.json', (j: WaterSection) => ({ ...j, sources: j.sources ?? [] }), null as WaterSection | null);
  if (water?.depth?.resolution) water.depthGrid = decodeR16(water.depth.file ?? 'water.r16', water.depth.resolution * water.depth.resolution);
  const transit = section('transit', 'transit.json', (j: TransitSection) => (Array.isArray(j.lines) ? j : null), null as TransitSection | null);
  const tmpe = section('tmpe', 'tmpe.json', (j: TmpeSection) => j, null as TmpeSection | null);
  const nodeController = section('nodecontroller', 'nodecontroller.json', (j: NodeControllerSection) => (Array.isArray(j.nodes) ? j : null), null as NodeControllerSection | null);
  const buildings = section('buildings', 'buildings.json', (j: BuildingsSection) => (Array.isArray(j.buildings) ? j : null), null as BuildingsSection | null);
  const assets = section('assets', 'assets.json', (j: AssetsSection) => (Array.isArray(j.buildings) ? j : null), null as AssetsSection | null);
  const districts = section('districts', 'districts.json', (j: DistrictsSection) => (j.grid ? { ...j, districts: j.districts ?? [], parks: j.parks ?? [] } : null), null as DistrictsSection | null);
  if (districts) {
    const n = districts.grid.resolution * districts.grid.resolution;
    const u8 = (file: string | undefined) => {
      const raw = file ? entries[file] : undefined;
      if (!raw || raw.length < n) { report.push({ level: 'warning', section: 'reader', message: `${file ?? 'district raster'} missing or short` }); return undefined; }
      return raw.slice(0, n);
    };
    districts.districtGrid = u8(districts.grid.districtFile ?? 'districts.u8');
    if (districts.parks.length) districts.parkGrid = u8(districts.grid.parkFile ?? 'parks.u8');
  }
  for (const [k, v] of Object.entries(tmpe?.parts ?? {})) if (v !== 'ok') report.push({ level: 'warning', section: 'tmpe', message: `TM:PE part ${k}: ${v}` });
  completeVariants(netInfos);
  return {
    source: 'cs1x', manifest, nodes: network.nodes ?? [], segments: network.segments ?? [],
    netInfos, imtFillers, terrain, zones, water, transit, tmpe, nodeController, buildings, assets, districts, report, sha256: null,
  };
}

interface M0Dump {
  format?: string; exportedAt?: string; cityName?: string; gameVersion?: string; coordinateSystem?: string;
  nodes: Cs1Node[]; segments: Cs1Segment[]; netInfos: NetInfo[]; imtFillers?: ImtFiller[];
  report?: (Cs1ReportItem | string)[];
}

/** Adapter: M0 network dump → Cs1City. */
export function fromM0Dump(d: M0Dump): Cs1City {
  if (!Array.isArray(d?.segments) || !Array.isArray(d?.netInfos)) throw new Error('not a .cs1x or M0 network dump');
  const netInfos = d.netInfos.map(i => ({ ...i }));
  completeVariants(netInfos);
  const report: Cs1ReportItem[] = (d.report ?? []).map(r => (typeof r === 'string' ? { level: 'info', message: r } : r));
  report.push({ level: 'info', section: 'reader', message: 'source is the M0 network dump: no terrain, no manifest; parents derived from variant fields' });
  return {
    source: 'm0-dump',
    manifest: {
      format: 'cs1x', formatVersion: 1, exportedAt: d.exportedAt, city: { name: d.cityName ?? 'unknown', saveName: d.cityName ?? 'unknown' },
      game: { version: d.gameVersion }, coordinateSystem: d.coordinateSystem, mapSize: 17280,
      sections: { network: 'ok', netinfos: 'ok', imt: d.imtFillers ? 'ok' : 'absent', terrain: 'absent' },
    },
    nodes: d.nodes ?? [], segments: d.segments, netInfos, imtFillers: d.imtFillers ?? [], terrain: null, report, sha256: null,
  };
}

/** Fill `variants` / `parent` from the AI fields where the exporter did not write them (M0 dump). */
function completeVariants(infos: NetInfo[]) {
  const byName = new Map(infos.map(i => [i.name, i]));
  for (const i of infos) {
    if (i.variants && Object.keys(i.variants).length) continue;
    const v: Record<string, string> = {};
    for (const [k, f] of Object.entries(VARIANT_FIELDS)) {
      const name = i.aiFields?.[f];
      if (typeof name === 'string' && name && name !== i.name) v[k] = name;
    }
    if (Object.keys(v).length) i.variants = v;
  }
  // A variant can list further variants (e.g. a bridge AI naming the elevated info): only infos
  // that are not themselves someone's variant count as ground parents.
  // CS1 shares variants between roads ("Basic Road Decoration Trees" uses "Basic Road Elevated"),
  // so pick the candidate whose name the variant's name starts with (longest match), else the first.
  const referenced = new Set(infos.flatMap(i => Object.values(i.variants ?? {})));
  const candidates = new Map<string, string[]>();
  for (const i of infos) {
    if (referenced.has(i.name)) continue;
    for (const name of Object.values(i.variants ?? {})) {
      if (!candidates.has(name)) candidates.set(name, []);
      candidates.get(name)!.push(i.name);
    }
  }
  for (const [name, cands] of candidates) {
    const variant = byName.get(name);
    if (!variant || variant.parent !== undefined) continue;
    const stem = name.replace(/^\d+\./, '').replace(/_Data$/, '');
    const prefixed = cands.filter(c => stem.startsWith(c.replace(/^\d+\./, '').replace(/_Data$/, '')));
    variant.parent = prefixed.sort((a, b) => b.length - a.length)[0] ?? cands[0];
  }
  for (const i of infos) if (i.parent === undefined) i.parent = null;
}
