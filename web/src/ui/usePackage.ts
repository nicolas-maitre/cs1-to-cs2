import { useEffect, useRef, useState } from 'react';
import { buildPackage } from '../core/plan.ts';
import type { BuildOptions, BuiltPackage } from '../core/plan.ts';
import type { Cs1City } from '../core/types.ts';
import type { WorkerRequest } from './packageWorker.ts';

export interface Source { city: Cs1City; bytes?: ArrayBuffer; synthetic?: number }

/**
 * Builds the package for the current options in a Web Worker (latest request wins), falling back to
 * the main thread if workers are unavailable (e.g. the page was opened from file://).
 */
export function usePackage(src: Source | null, opts: BuildOptions | null) {
  const [pkg, setPkg] = useState<BuiltPackage | null>(null);
  const [busy, setBusy] = useState(false);
  const [ms, setMs] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const worker = useRef<Worker | null>(null);
  const ready = useRef<Promise<boolean> | null>(null);
  const seq = useRef(0);

  useEffect(() => {
    setPkg(null);
    if (!src) return;
    let w: Worker | null = null;
    try { w = new Worker(new URL('./packageWorker.ts', import.meta.url), { type: 'module' }); }
    catch { w = null; }
    worker.current = w;
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
      try { const p = buildPackage(src.city, opts); if (id === seq.current) { setPkg(p); setMs(performance.now() - t); setError(null); } }
      catch (e) { if (id === seq.current) setError(e instanceof Error ? e.message : String(e)); }
      if (id === seq.current) setBusy(false);
    };
    void ready.current?.then(ok => {
      if (id !== seq.current) return;
      if (ok && worker.current) worker.current.postMessage({ type: 'build', id, opts } satisfies WorkerRequest);
      else setTimeout(local, 0);
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [src, key]);

  return { pkg, busy, ms, error };
}
