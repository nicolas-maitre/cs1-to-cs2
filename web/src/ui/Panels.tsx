import { useState } from 'react';
import type { ZoningOptions } from '../core/m2/zoning.ts';
import type { Cs1City } from '../core/types.ts';

export interface ConvOptions { zoning: ZoningOptions; traffic: boolean; transit: boolean; water: boolean; buildings: boolean; districts: boolean; objects: boolean; surfaces: boolean }

/** M2 options: which extra sections to convert and how zones map. */
export function ConversionOptions({ value, onChange, city }: { value: ConvOptions; onChange: (v: ConvOptions) => void; city: Cs1City }) {
  const z = value.zoning;
  const setZ = (p: Partial<ZoningOptions>) => onChange({ ...value, zoning: { ...z, ...p } });
  return (
    <div className="options">
      <fieldset className="opt-group" disabled={!city.zones}>
        <legend><label className="check"><input type="checkbox" checked={z.enabled} onChange={e => setZ({ enabled: e.target.checked })} /> Zoning</label></legend>
        <label>Building theme
          <select value={z.theme} onChange={e => setZ({ theme: e.target.value as ZoningOptions['theme'] })} disabled={!z.enabled}>
            <option value="NA">North American</option>
            <option value="EU">European</option>
          </select>
        </label>
        <label>CS1 high-density residential →
          <select value={z.residentialHigh} onChange={e => setZ({ residentialHigh: e.target.value as ZoningOptions['residentialHigh'] })} disabled={!z.enabled}>
            <option value="High">Residential High</option>
            <option value="Medium">Residential Medium</option>
            <option value="Mixed">Residential Mixed</option>
          </select>
        </label>
        <label>CS1 office →
          <select value={z.office} onChange={e => setZ({ office: e.target.value as ZoningOptions['office'] })} disabled={!z.enabled}>
            <option value="High">Office High</option>
            <option value="Low">Office Low</option>
          </select>
        </label>
      </fieldset>
      <label className="check"><input type="checkbox" checked={value.traffic} onChange={e => onChange({ ...value, traffic: e.target.checked })} /> Traffic rules: TM:PE, speed limits, traffic lights</label>
      <label className="check"><input type="checkbox" checked={value.transit} disabled={!city.transit} onChange={e => onChange({ ...value, transit: e.target.checked })} /> Transit lines</label>
      <label className="check"><input type="checkbox" checked={value.water} disabled={!city.water} onChange={e => onChange({ ...value, water: e.target.checked })} /> Water: sea level and sources</label>
      <label className="check"><input type="checkbox" checked={value.buildings} disabled={!city.buildings} onChange={e => onChange({ ...value, buildings: e.target.checked })} /> Buildings: services, parks, landmarks (see the Assets tab)</label>
      <label className="check"><input type="checkbox" checked={value.districts} disabled={!city.districts} onChange={e => onChange({ ...value, districts: e.target.checked })} /> Districts and policies</label>
      <label className="check"><input type="checkbox" checked={value.objects} disabled={!city.trees && !city.props} onChange={e => onChange({ ...value, objects: e.target.checked })} /> Trees and rocks</label>
      <label className="check"><input type="checkbox" checked={value.surfaces} disabled={!city.imtFillers.some(f => f.markingType === 'Node')} onChange={e => onChange({ ...value, surfaces: e.target.checked })} /> IMT junction surfaces</label>
    </div>
  );
}
import { RB_FOLDER } from '../core/readme.ts';
import type { ConvCategory } from '../core/summary.ts';
import type { SectionCheck } from '../core/summary.ts';

const STATUS: Record<string, { label: string; cls: string }> = {
  converted: { label: 'Converts', cls: 'ok' },
  partial: { label: 'Converts, approximated', cls: 'partial' },
  'report-only': { label: 'Report only', cls: 'report' },
  'not-converted': { label: 'Not converted', cls: 'no' },
  absent: { label: 'Not in this file', cls: 'absent' },
};

export function ConversionPanel({ summary }: { summary: ConvCategory[] }) {
  return (
    <ul className="conv" aria-label="What converts, per category">
      {summary.map(c => (
        <li key={c.id}>
          <details>
            <summary>
              <span className={`status ${STATUS[c.status]?.cls ?? ''}`}>{STATUS[c.status]?.label ?? c.status}</span>
              <b>{c.label}</b>
              <span className="muted"> – {c.summary}</span>
            </summary>
            {c.details.length > 0 && <ul>{c.details.map((d, i) => <li key={i}>{d}</li>)}</ul>}
          </details>
        </li>
      ))}
    </ul>
  );
}

export function FileCheck({ sections, warnings }: { sections: SectionCheck[]; failed: string[]; warnings: string[] }) {
  const shown = sections.filter(s => s.status !== 'absent' || s.messages.length);
  const absent = sections.filter(s => s.status === 'absent').map(s => s.label);
  return (
    <div className="filecheck">
      <ul className="sections">
        {shown.map(s => (
          <li key={s.id}>
            <span className={`status ${s.status === 'ok' ? 'ok' : s.status === 'failed' ? 'no' : 'partial'}`}>{s.status}</span> {s.label}
            {s.messages.map((m, i) => <div key={i} className="small muted">{m}</div>)}
          </li>
        ))}
      </ul>
      {absent.length > 0 && <p className="small muted">Not in this export: {absent.join(', ')}.</p>}
      {warnings.length > 0 && (
        <details>
          <summary>{warnings.length} exporter warning(s)</summary>
          <ul className="small">{warnings.map((w, i) => <li key={i}>{w}</li>)}</ul>
        </details>
      )}
    </div>
  );
}

export function InstallHelp({ zipName, requires = [] }: { zipName: string; requires?: string[] }) {
  const ps = `Expand-Archive -Path "$env:USERPROFILE\\Downloads\\${zipName}" -DestinationPath "$env:USERPROFILE\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder" -Force`;
  return (
    <div className="install">
      <ol>
        <li>Close Cities: Skylines II. Road Builder reads its road types only when the game starts.</li>
        <li>
          Open this folder (paste it into the Explorer address bar, or press <kbd>Win</kbd>+<kbd>R</kbd>):
          <CopyBox text={RB_FOLDER} label="Road Builder folder path" />
        </li>
        <li>Copy the files from the package's <code>roadbuilder</code> folder into it. Existing <code>*-cs1conv.json</code> files are replaced; your own roads are not touched.</li>
        <li>
          Or download <b>Road Builder configs only</b> and run this in PowerShell:
          <CopyBox text={ps} label="PowerShell command" />
        </li>
        <li>Start CS2 with Road Builder{requires.includes('Traffic') ? <> and <b>Traffic</b> (this plan has lane connections and priority signs)</> : null} enabled, start a new city, and import <code>cs1conv-plan.json</code>.</li>
      </ol>
    </div>
  );
}

function CopyBox({ text, label }: { text: string; label: string }) {
  const [done, setDone] = useState(false);
  const copy = async () => {
    try { await navigator.clipboard.writeText(text); setDone(true); setTimeout(() => setDone(false), 1500); }
    catch { /* clipboard blocked: the text stays selectable */ }
  };
  return (
    <div className="copybox">
      <code tabIndex={0} aria-label={label}>{text}</code>
      <button onClick={copy} aria-label={`Copy ${label}`}>{done ? 'Copied' : 'Copy'}</button>
    </div>
  );
}
