// README.txt written into every package zip / folder: what each file is and where it goes.

import type { BuiltPackage } from './plan.ts';
import type { ConvCategory } from './summary.ts';

export const RB_FOLDER = '%USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder';

const STATUS_LABEL: Record<string, string> = {
  converted: 'converted', partial: 'converted with approximations', 'report-only': 'listed in report.json only', 'not-converted': 'not converted', absent: 'not in this export',
};

/** Plain-text README (CRLF line endings so Notepad shows it properly). */
export function packageReadme(pkg: BuiltPackage, summary: ConvCategory[] = []): string {
  const p = pkg.plan;
  const rb = pkg.rbConfigs.length;
  const warn = pkg.report.items.filter(i => i.level === 'warning').length;
  const lines = [
    `CS1 -> CS2 import package for "${p.source.city}" (${p.source.saveName})`,
    `Made by ${p.generator?.name ?? 'cs1conv'} ${p.generator?.version ?? ''} on ${p.generator?.generatedAt ?? ''}`.trim(),
    '',
    `Crop: ${p.crop.size} m square centred on CS1 (${p.crop.centerX}, ${p.crop.centerZ}); it lands on the centre of the CS2 map.`,
    `Contents: ${p.stats.segments} road/track/path segments, ${p.stats.nodes} nodes, ${rb} Road Builder road types.`,
    '',
    'FILES',
    '  cs1conv-plan.json   The city plan, already in CS2 coordinates. The CS2 importer reads it.',
    '  report.json         Everything that was dropped or approximated, item by item.',
    `  roadbuilder\\        ${rb} generated Road Builder road types (*-cs1conv.json).`,
    '  README.txt          This file.',
    '',
    'HOW TO USE',
    '  1. Close Cities: Skylines II (Road Builder only reads its roads when the game starts).',
    '  2. Copy every file from the roadbuilder folder into:',
    `       ${RB_FOLDER}`,
    '     (paste that path into the Windows Explorer address bar, or Win+R). Create the folder if it does not exist.',
    '     Files ending in -cs1conv.json with the same name are replaced; nothing else there is touched.',
    '     PowerShell, from the folder holding this README:',
    `       Copy-Item .\\roadbuilder\\*-cs1conv.json "$env:USERPROFILE\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\ModsData\\RoadBuilder\\" -Force`,
    `  3. Start CS2 with these mods enabled: ${(p.requires?.length ? p.requires : ['RoadBuilder']).map(m => (m === 'RoadBuilder' ? 'Road Builder' : m)).join(', ')}.`,
    '  4. Start a new city (a flat map works best), then run the CS2 importer on cs1conv-plan.json.',
    '  5. Read report.json (or the web tool\'s Report tab) for what needs manual work.',
    '',
  ];
  if (summary.length) {
    lines.push('WHAT CONVERTS');
    for (const c of summary) lines.push(`  ${c.label}: ${STATUS_LABEL[c.status] ?? c.status} - ${c.summary}`);
    lines.push('');
  }
  lines.push(`REPORT: ${pkg.report.items.length} items, ${warn} warnings.`);
  for (const i of pkg.report.items.filter(i => i.level === 'warning').slice(0, 15)) lines.push(`  - ${i.message.length > 160 ? i.message.slice(0, 157) + '...' : i.message}`);
  if (warn > 15) lines.push(`  ... and ${warn - 15} more in report.json`);
  lines.push('');
  return lines.join('\r\n');
}

/** Short README for the "Road Builder configs only" zip. */
export function rbOnlyReadme(pkg: BuiltPackage): string {
  return [
    `Road Builder road types for "${pkg.plan.source.city}" (${pkg.rbConfigs.length} files, *-cs1conv.json).`,
    '',
    'With Cities: Skylines II closed, copy these files into:',
    `  ${RB_FOLDER}`,
    'Road Builder loads them the next time the game starts. Files with the same name are replaced.',
    '',
  ].join('\r\n');
}
