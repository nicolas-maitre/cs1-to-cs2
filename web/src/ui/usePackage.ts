import { useCallback, useEffect, useRef, useState } from 'react';
import { packageZip } from '../core/zip.ts';
import { buildPackage } from '../core/plan.ts';
import type { BuildOptions, BuiltPackage } from '../core/plan.ts';
import type { Cs1City } from '../core/types.ts';
import type { Cs2Catalogue } from '../core/m3/buildings.ts';
import type { WorkerRequest } from './packageWorker.ts';

export interface Source { city: Cs1City; bytes?: ArrayBuffer; synthetic?: number }

/**
 * Builds the package for the current options in a Web Worker (latest request wins), falling back to
 * the main thread if workers are unavailable (e.g. the page was opened from file://).
 */
export function usePackage(src: Source | null, opts: BuildOptions | null, catalogue: Cs2Catalogue | null = null) {
  const [pkg, setPkg] = useState<BuiltPackage | null>(null);
  const [busy, setBusy] = useState(false);
  const [ms, setMs] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const worker = useRef<Worker | null>(null);
  const ready = useRef<Promise<boolean> | null>(null);
  const seq = useRef(0);
  const sentCatalogue = useRef<Cs2Catalogue | null | undefined>(undefined);
  const zipSeq = useRef(1_000_000); // ids for zip requests, apart from build ids

  useEffect(() => {
    setPkg(null);
    if (!src) return;
    let w: Worker | null = null;
    try { w = new Worker(new URL('./packageWorker.ts', import.meta.url), { type: 'module' }); }
    catch { w = null; }
    worker.current = w;
    sentCatalogue.current = undefined;
    if (!w) { ready.current = Promise.resolve(false); return; }
    ready.current = new Promise<boolean>(resolve => {
      const onFirst = (e: MessageEvent) => {
        if (e.data.type === 'loaded') { w!.removeEventListener('message', onFirst); resolve(true); }
        else if (e.data.type === 'error' && e.data.id === -1) { w!.removeEventListener('message', onFirst); resolve(false); }
      };
      w!.addEventListener('message', onFirst);
      w!.addEventListener('error', () => resolve(false), { once: true });
      // The worker gets its own copy; the main thread keeps the bytes for re-hashing / reload.
      const msg: WorkerRequest = { type: 'load', sha256: src.city.sha256, bytes: src.bytes?.slice(0), synthetic: src.synthetic };
      w!.postMessage(msg, msg.bytes ? [msg.bytes] : []);
    });
    w.addEventListener('message', (e: MessageEvent) => {
      const d = e.data;
      if (d.type === 'built' && d.id === seq.current) { setPkg(d.pkg); setMs(d.ms); setBusy(false); setError(null); }
      else if (d.type === 'error' && d.id === seq.current) { setError(d.message); setBusy(false); }
    });
    return () => { w?.terminate(); worker.current = null; };
  }, [src]);

  const key = opts ? JSON.stringify(opts) : '';
  useEffect(() => {
    if (!src || !opts) return;
    const id = ++seq.current;
    setBusy(true);
    const local = () => {
      const t = performance.now();
      try { const p = buildPackage(src.city, { ...opts, catalogue }); if (id === seq.current) { setPkg(p); setMs(performance.now() - t); setError(null); } }
      catch (e) { if (id === seq.current) setError(e instanceof Error ? e.message : String(e)); }
      if (id === seq.current) setBusy(false);
    };
    void ready.current?.then(ok => {
      if (id !== seq.current) return;
      if (ok && worker.current) {
        if (sentCatalogue.current !== catalogue) { worker.current.postMessage({ type: 'catalogue', catalogue } satisfies WorkerRequest); sentCatalogue.current = catalogue; }
        worker.current.postMessage({ type: 'build', id, opts } satisfies WorkerRequest);
      }
      else setTimeout(local, 0);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [src, key, catalogue]);

  /** Builds the full package zip (with terrain) in the worker; main thread if there is no worker. */
  const zip = useCallback((): Promise<Uint8Array> => {
    if (!src || !opts) return Promise.reject(new Error('no city loaded'));
    const id = ++zipSeq.current;
    return (ready.current ?? Promise.resolve(false)).then(ok => {
      const w = worker.current;
      if (!ok || !w) return packageZip(src.city, { ...opts, catalogue }).zip;
      return new Promise<Uint8Array>((resolve, reject) => {
        const onMsg = (e: MessageEvent) => {
          const d = e.data;
          if (d.id !== id || !(d.type === 'zipped' || (d.type === 'error' && d.zip))) return;
          w.removeEventListener('message', onMsg);
          if (d.type === 'zipped') resolve(d.zip); else reject(new Error(d.message));
        };
        w.addEventListener('message', onMsg);
        if (sentCatalogue.current !== catalogue) { w.postMessage({ type: 'catalogue', catalogue } satisfies WorkerRequest); sentCatalogue.current = catalogue; }
        w.postMessage({ type: 'zip', id, opts } satisfies WorkerRequest);
      });
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [src, key, catalogue]);

  return { pkg, busy, ms, error, zip };
}
