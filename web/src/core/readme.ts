// README.txt written into every package zip / folder: what each file is and where it goes, in the package's language.

import { translator } from './i18n/index.ts';
import type { BuiltPackage } from './plan.ts';
import type { ConvCategory } from './summary.ts';

export const RB_FOLDER = '%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder';

/** Plain-text README (CRLF line endings so Notepad shows it properly). */
export function packageReadme(pkg: BuiltPackage, summary: ConvCategory[] = []): string {
  const t = translator(pkg.lang ?? 'en');
  const p = pkg.plan;
  const rb = pkg.rbConfigs.length;
  const warn = pkg.report.items.filter(i => i.level === 'warning').length;
  const file = (name: string, text: string) => `  ${name.padEnd(20)}${text}`;
  const lines = [
    t('readme.title', { city: p.source.city, save: p.source.saveName }),
    t('readme.madeBy', { generator: `${p.generator?.name ?? 'cs1conv'} ${p.generator?.version ?? ''}`.trim(), date: p.generator?.generatedAt ?? '' }).trim(),
    '',
    t('readme.crop', { size: String(p.crop.size), x: String(p.crop.centerX), z: String(p.crop.centerZ) }),
    t('readme.contents', { segments: p.stats.segments, nodes: p.stats.nodes, rb }),
    '',
    t('readme.files'),
    file('cs1conv-plan.json', t('readme.file.plan')),
    file('report.json', t('readme.file.report')),
    file('roadbuilder\\', t('readme.file.rb', { n: rb })),
    ...(p.terrain ? [file('terrain\\', t('readme.file.terrain'))] : []),
    file('README.txt', t('readme.file.readme')),
    '',
    t('readme.how'),
    `  1. ${t('readme.how0')}`,
    `  2. ${t('readme.how1')}`,
    `     ${t('readme.how2')}`,
    `       ${RB_FOLDER}`,
    `     ${t('readme.how2b')}`,
    `     ${t('readme.how2c')}`,
    `     ${t('readme.how2d')}`,
    `       Copy-Item .\\roadbuilder\\*-cs1conv.json "$env:USERPROFILE\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder\\" -Force`,
    `  3. ${t('readme.how3', { mods: ['RoadBuilder', 'CS1 → CS2 Importer', ...(p.requires ?? []).filter(m => m !== 'RoadBuilder')].map(m => (m === 'RoadBuilder' ? 'Road Builder' : m)).join(', ') })}`,
    `  4. ${t('readme.how4')}`,
    `  5. ${t('readme.how5')}`,
    '',
  ];
  if (summary.length) {
    lines.push(t('readme.converts'));
    for (const c of summary) lines.push(`  ${t('readme.line', { label: c.label, status: t(`readme.status.${c.status}` as 'readme.status.converted'), summary: c.summary })}`);
    lines.push('');
  }
  lines.push(t('readme.report', { n: pkg.report.items.length, w: warn }));
  for (const i of pkg.report.items.filter(i => i.level === 'warning').slice(0, 15)) lines.push(`  - ${i.message.length > 160 ? i.message.slice(0, 157) + '...' : i.message}`);
  if (warn > 15) lines.push(`  ${t('readme.more', { n: warn - 15 })}`);
  lines.push('');
  return lines.join('\r\n');
}

/** Short README for the "Road Builder configs only" zip. */
export function rbOnlyReadme(pkg: BuiltPackage): string {
  const t = translator(pkg.lang ?? 'en');
  return [
    t('readme.rbOnly.title', { city: pkg.plan.source.city, n: pkg.rbConfigs.length }),
    '',
    t('readme.rbOnly.copy'),
    `  ${RB_FOLDER}`,
    t('readme.rbOnly.loads'),
    '',
  ].join('\r\n');
}
