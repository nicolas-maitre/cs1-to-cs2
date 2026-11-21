// External links shown in the UI. `url: null` = not published yet (shown as "coming soon").
// Paradox Mods ids were read from the installed mods (…\.cache\Mods\pdx_mods\<id>_<version>).
import type { MessageKey } from '../core/i18n/index.ts';

export interface ModLink { name: string; game: 'CS1' | 'CS2'; url: string | null; why: MessageKey; required: boolean }

export const MODS: ModLink[] = [
  { name: 'CS1 → CS2 Exporter', game: 'CS1', url: null, why: 'mod.exporter', required: true },
  { name: 'Road Builder', game: 'CS2', url: 'https://mods.paradoxplaza.com/mods/87190/Windows', why: 'mod.roadBuilder', required: true },
  { name: 'Traffic', game: 'CS2', url: 'https://mods.paradoxplaza.com/mods/80095/Windows', why: 'mod.traffic', required: false },
  { name: 'CS1 → CS2 Importer', game: 'CS2', url: null, why: 'mod.importer', required: true },
];
