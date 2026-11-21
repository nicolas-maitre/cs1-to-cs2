// Browser APIs jsdom lacks, stubbed just enough for the UI to run in tests: a canvas 2D context that draws
// nothing, Path2D, ResizeObserver, object URLs and download clicks. No Worker: usePackage then builds on the
// main thread (its file:// fallback), which is what the tests want.
import { strToU8, zipSync } from 'fflate';
import { syntheticCity } from '../../src/core/synthetic.ts';

class Path2DStub {
  addPath() {} moveTo() {} lineTo() {} bezierCurveTo() {} quadraticCurveTo() {} arc() {} arcTo() {} rect() {} ellipse() {} closePath() {}
}

function context2d(canvas: HTMLCanvasElement): CanvasRenderingContext2D {
  const state: Record<string | symbol, unknown> = { canvas };
  return new Proxy(state, {
    get(t, k) {
      if (k in t) return t[k];
      if (k === 'measureText') return (s: string) => ({ width: s.length * 6 });
      if (k === 'createImageData' || k === 'getImageData') return (a: number, b: number, c?: number, d?: number) => {
        const w = c ?? a, h = d ?? b;
        return { width: w, height: h, data: new Uint8ClampedArray(w * h * 4) };
      };
      if (k === 'getTransform') return () => ({ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 });
      return () => {};
    },
    set(t, k, v) { t[k] = v; return true; },
  }) as unknown as CanvasRenderingContext2D;
}

/** Downloads the app made (object URL blobs), in order. */
export const downloads: { name: string; blob: Blob }[] = [];

export function installDomStubs() {
  const g = globalThis as Record<string, unknown>;
  g.Path2D = Path2DStub;
  g.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
  const contexts = new WeakMap<HTMLCanvasElement, CanvasRenderingContext2D>();
  HTMLCanvasElement.prototype.getContext = function (this: HTMLCanvasElement) {
    let c = contexts.get(this);
    if (!c) { c = context2d(this); contexts.set(this, c); }
    return c;
  } as never;
  const blobs = new Map<string, Blob>();
  let n = 0;
  URL.createObjectURL = (b: Blob) => { const u = `blob:test/${++n}`; blobs.set(u, b); return u; };
  URL.revokeObjectURL = () => {};
  HTMLAnchorElement.prototype.click = function (this: HTMLAnchorElement) {
    const b = blobs.get(this.href);
    if (b && this.download) downloads.push({ name: this.download, blob: b });
  };
  // the app fetches the bundled catalogue: answer "not found" quickly instead of a network error
  g.fetch = async () => new Response('', { status: 404 });
}

/** A real .cs1x (zip) of the synthetic city, so tests go through the file reader like a user's export. */
export function syntheticCs1x(n = 12): File {
  const c = syntheticCity(n);
  const entries: Record<string, Uint8Array> = {
    'manifest.json': strToU8(JSON.stringify({ ...c.manifest, sections: { network: 'ok', netinfos: 'ok', imt: 'absent', terrain: 'absent', districts: c.districts ? 'ok' : 'absent' } })),
    'network.json': strToU8(JSON.stringify({ nodes: c.nodes, segments: c.segments })),
    'netinfos.json': strToU8(JSON.stringify({ netInfos: c.netInfos })),
  };
  if (c.districts?.districtGrid) {
    const { districtGrid, parkGrid: _p, ...d } = c.districts;
    entries['districts.json'] = strToU8(JSON.stringify({ ...d, grid: { ...d.grid, districtFile: 'districts.u8' } }));
    entries['districts.u8'] = districtGrid;
  }
  const zip = zipSync(entries);
  return new File([zip as Uint8Array<ArrayBuffer>], 'synthetic.cs1x');
}
