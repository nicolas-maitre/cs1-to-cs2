import { MODS } from './links.ts';

export interface LoadError { title: string; message: string; hint?: string }

interface Props {
  busy: string | null;
  error: LoadError | null;
  onPick: () => void;
}

const STEPS: { title: string; text: string }[] = [
  { title: 'Install the exporter', text: 'Subscribe to the CS1 → CS2 Exporter mod in Cities: Skylines and enable it in Content Manager.' },
  { title: 'Export your city', text: 'Load the city, then Options → Mod Settings → CS1 → CS2 Exporter → Export city. It writes a .cs1x file.' },
  { title: 'Open it here', text: 'Drop the .cs1x on this page. It is read in your browser; nothing is uploaded.' },
  { title: 'Crop and map', text: 'Pick the part of the city that fits a CS2 map, check how each road type converts, and download the package.' },
  { title: 'Install the package', text: 'Copy the Road Builder road types into CS2\'s RoadBuilder folder (the package README says where) before starting CS2.' },
  { title: 'Import in CS2', text: 'Start a new city with Road Builder and the CS1 → CS2 Importer, then import the plan.' },
];

export function Landing({ busy, error, onPick }: Props) {
  return (
    <div className="landing">
      <section className="hero">
        <h1>Bring your Cities: Skylines city to Cities: Skylines II</h1>
        <p className="lead">cs1conv reads an export of your CS1 city and turns it into an import package for CS2: the road network with its road types, bridges and tunnels, rail and metro, the terrain, and the extras CS2 can express.</p>
        <div className="drop" role="group" aria-labelledby="drop-title">
          <h2 id="drop-title">Drop a <code>.cs1x</code> file here</h2>
          <p className="muted">or</p>
          <button className="primary big" onClick={onPick} disabled={!!busy}>Choose a .cs1x file…</button>
          <p className="muted small">Everything runs in this browser tab. Your city never leaves your computer.</p>
          <div aria-live="polite" className="drop-status">
            {busy && <p className="busy-inline"><span className="spinner" aria-hidden="true" />{busy}</p>}
          </div>
          {error && (
            <div className="error-card" role="alert">
              <b>{error.title}</b>
              <p>{error.message}</p>
              {error.hint && <p className="muted">{error.hint}</p>}
            </div>
          )}
        </div>
      </section>
      <section aria-labelledby="steps-title">
        <h2 id="steps-title">How it works</h2>
        <ol className="steps">
          {STEPS.map((s, i) => (
            <li key={s.title}><span className="num" aria-hidden="true">{i + 1}</span><div><b>{s.title}</b><p>{s.text}</p></div></li>
          ))}
        </ol>
      </section>
      <section aria-labelledby="mods-title">
        <h2 id="mods-title">Mods you need</h2>
        <ul className="mods">
          {MODS.map(m => (
            <li key={m.name}>
              <span className="tag">{m.game}</span>
              {m.url ? <a href={m.url} target="_blank" rel="noreferrer">{m.name}</a> : <b>{m.name}</b>}
              {!m.url && <span className="tag soon">coming soon</span>}
              {!m.required && <span className="tag">optional</span>}
              <span className="muted"> – {m.why}</span>
            </li>
          ))}
        </ul>
      </section>
      <section aria-labelledby="limits-title">
        <h2 id="limits-title">Good to know</h2>
        <ul className="plain">
          <li>CS2 maps are smaller: you convert a square of up to 14.3 km, the CS2 playable area. Large 81-tile cities need a crop.</li>
          <li>Road types are rebuilt with Road Builder, so roads keep their lanes, medians, bus lanes and trees. Some widths change slightly; the report lists each one.</li>
          <li>Buildings, parks and props are not converted yet.</li>
          <li>Works with exports from cities using 81 Tiles, TM:PE, Node Controller, Intersection Marking Tool and Vanilla+ roads.</li>
        </ul>
      </section>
    </div>
  );
}
