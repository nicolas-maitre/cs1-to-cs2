import { useState } from 'react';
import type { ZoningOptions } from '../core/m2/zoning.ts';
import { RB_FOLDER } from '../core/readme.ts';
import type { ConvCategory } from '../core/summary.ts';
import type { SectionCheck } from '../core/summary.ts';
import type { Cs1City } from '../core/types.ts';
import { useI18n } from './i18n.tsx';
import { Rich } from './Rich.tsx';

export interface ConvOptions { zoning: ZoningOptions; traffic: boolean; transit: boolean; water: boolean; buildings: boolean; districts: boolean; objects: boolean; surfaces: boolean }

/** Which sections to convert and how zones map. */
export function ConversionOptions({ value, onChange, city }: { value: ConvOptions; onChange: (v: ConvOptions) => void; city: Cs1City }) {
  const { t } = useI18n();
  const z = value.zoning;
  const setZ = (p: Partial<ZoningOptions>) => onChange({ ...value, zoning: { ...z, ...p } });
  const check = (key: keyof Omit<ConvOptions, 'zoning'>, label: string, disabled = false) => (
    <label className="check"><input type="checkbox" checked={value[key]} disabled={disabled} onChange={e => onChange({ ...value, [key]: e.target.checked })} /> {label}</label>
  );
  return (
    <div className="options">
      <fieldset className="opt-group" disabled={!city.zones}>
        <legend><label className="check"><input type="checkbox" checked={z.enabled} onChange={e => setZ({ enabled: e.target.checked })} /> {t('opt.zoning')}</label></legend>
        <label>{t('opt.theme')}
          <select value={z.theme} onChange={e => setZ({ theme: e.target.value as ZoningOptions['theme'] })} disabled={!z.enabled}>
            <option value="NA">{t('opt.themeNA')}</option>
            <option value="EU">{t('opt.themeEU')}</option>
          </select>
        </label>
        <label>{t('opt.resHigh')}
          <select value={z.residentialHigh} onChange={e => setZ({ residentialHigh: e.target.value as ZoningOptions['residentialHigh'] })} disabled={!z.enabled}>
            <option value="High">{t('opt.resHigh.High')}</option>
            <option value="Medium">{t('opt.resHigh.Medium')}</option>
            <option value="Mixed">{t('opt.resHigh.Mixed')}</option>
          </select>
        </label>
        <label>{t('opt.office')}
          <select value={z.office} onChange={e => setZ({ office: e.target.value as ZoningOptions['office'] })} disabled={!z.enabled}>
            <option value="High">{t('opt.office.High')}</option>
            <option value="Low">{t('opt.office.Low')}</option>
          </select>
        </label>
      </fieldset>
      {check('traffic', t('opt.traffic'))}
      {check('transit', t('opt.transit'), !city.transit)}
      {check('water', t('opt.water'), !city.water)}
      {check('buildings', t('opt.buildings'), !city.buildings)}
      {check('districts', t('opt.districts'), !city.districts)}
      {check('objects', t('opt.objects'), !city.trees && !city.props)}
      {check('surfaces', t('opt.surfaces'), !city.imtFillers.some(f => f.markingType === 'Node'))}
    </div>
  );
}

const STATUS_CLS: Record<string, string> = { converted: 'ok', partial: 'partial', 'report-only': 'report', 'not-converted': 'no', none: 'absent', absent: 'absent' };

export function ConversionPanel({ summary }: { summary: ConvCategory[] }) {
  const { t, tk } = useI18n();
  return (
    <ul className="conv" aria-label={t('conv.aria')}>
      {summary.map(c => (
        <li key={c.id}>
          <details>
            <summary>
              <span className={`status ${STATUS_CLS[c.status] ?? ''}`}>{tk(`status.${c.status}`, c.status)}</span>
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
  const { t, tk } = useI18n();
  const label = (s: SectionCheck) => tk(`sec.${s.id}`, s.label);
  const shown = sections.filter(s => s.status !== 'absent' || s.messages.length);
  const absent = sections.filter(s => s.status === 'absent').map(label);
  return (
    <div className="filecheck">
      <ul className="sections">
        {shown.map(s => (
          <li key={s.id}>
            <span className={`status ${s.status === 'ok' ? 'ok' : s.status === 'failed' ? 'no' : 'partial'}`}>{tk(`fc.${s.status}`, s.status)}</span> {label(s)}
            {s.messages.map((m, i) => <div key={i} className="small muted">{m}</div>)}
          </li>
        ))}
      </ul>
      {absent.length > 0 && <p className="small muted">{t('fc.notInExport', { list: absent.join(', ') })}</p>}
      {warnings.length > 0 && (
        <details>
          <summary>{t('fc.warnings', { n: warnings.length })}</summary>
          <ul className="small">{warnings.map((w, i) => <li key={i}>{w}</li>)}</ul>
        </details>
      )}
    </div>
  );
}

export function InstallHelp({ zipName, requires = [] }: { zipName: string; requires?: string[] }) {
  const { t } = useI18n();
  const ps = `Expand-Archive -Path "$env:USERPROFILE\\Downloads\\${zipName}" -DestinationPath "$env:USERPROFILE\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder" -Force`;
  return (
    <div className="install">
      <ol>
        <li><Rich text={t('install.unzip')} /></li>
        <li>{t('install.close')}</li>
        <li>
          {t('install.folder')}
          <CopyBox text={RB_FOLDER} label={t('copy.rbFolder')} />
        </li>
        <li><Rich text={t('install.copy')} /></li>
        <li>
          <Rich text={t('install.ps')} />
          <CopyBox text={ps} label={t('copy.ps')} />
        </li>
        <li><Rich text={t(requires.includes('Traffic') ? 'install.startTraffic' : 'install.start')} /></li>
      </ol>
      <p className="small muted">{t('install.importerCopies')}</p>
    </div>
  );
}

function CopyBox({ text, label }: { text: string; label: string }) {
  const { t } = useI18n();
  const [done, setDone] = useState(false);
  const copy = async () => {
    try { await navigator.clipboard.writeText(text); setDone(true); setTimeout(() => setDone(false), 1500); }
    catch { /* clipboard blocked: the text stays selectable */ }
  };
  return (
    <div className="copybox">
      <code tabIndex={0} aria-label={label}>{text}</code>
      <button onClick={copy} aria-label={t('copy.aria', { label })}>{done ? t('copy.copied') : t('copy.copy')}</button>
    </div>
  );
}
