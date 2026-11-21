// .cs1x v1 reader (schema/README.md §1) plus an adapter for the M0 network dump
// (spikes/m0-cs1-network-dump), which has the same nodes/segments/netInfos/imtFillers fields
// but no manifest, no terrain and no parent/variants on NetInfos.

import { unzipSync, strFromU8 } from 'fflate';
import { sha256Hex } from './hash.ts';
import type { Cs1City, Cs1Node, Cs1ReportItem, Cs1Segment, ImtFiller, Manifest, NetInfo, Terrain } from './types.ts';

const VARIANT_FIELDS: Record<string, string> = {
  elevated: 'm_elevatedInfo', bridge: 'm_bridgeInfo', slope: 'm_slopeInfo', tunnel: 'm_tunnelInfo',
};

export function parseJson<T = unknown>(text: string): T {
  return JSON.parse(text.charCodeAt(0) === 0xfeff ? text.slice(1) : text) as T;
}

const isZip = (b: Uint8Array) => b.length > 4 && b[0] === 0x50 && b[1] === 0x4b && (b[2] === 3 || b[2] === 5);

/** Read a .cs1x (zip) or an M0 dump (JSON) from raw bytes. */
export async function readCityFile(bytes: Uint8Array, opts: { hash?: boolean } = {}): Promise<Cs1City> {
  const city = isZip(bytes) ? readCs1x(bytes) : fromM0Dump(parseJson(strFromU8(bytes)));
  if (opts.hash !== false) city.sha256 = await sha256Hex(bytes);
  return city;
}

/** Synchronous .cs1x reader (no hash). Throws only when the file is not a .cs1x at all. */
export function readCs1x(bytes: Uint8Array): Cs1City {
  const entries = unzipSync(bytes);
  const text = (name: string) => (entries[name] ? strFromU8(entries[name]) : null);
  const manifestText = text('manifest.json');
  if (manifestText == null) throw new Error('not a .cs1x: manifest.json is missing');
  const manifest = parseJson<Manifest>(manifestText);
  if (manifest.format !== 'cs1x') throw new Error(`not a .cs1x: manifest format is ${JSON.stringify(manifest.format)}`);
  const report: Cs1ReportItem[] = [];
  if (manifest.formatVersion !== 1) report.push({ level: 'warning', section: 'reader', message: `formatVersion ${manifest.formatVersion}; this reader knows v1` });
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
  completeVariants(netInfos);
  return {
    source: 'cs1x', manifest, nodes: network.nodes ?? [], segments: network.segments ?? [],
    netInfos, imtFillers, terrain, report, sha256: null,
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
