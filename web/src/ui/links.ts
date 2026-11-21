// External links shown in the UI. `url: null` = not published yet (shown as "coming soon").
// Paradox Mods ids were read from the installed mods (…\.cache\Mods\pdx_mods\<id>_<version>).

export interface ModLink { name: string; game: 'CS1' | 'CS2'; url: string | null; why: string; required: boolean }

export const MODS: ModLink[] = [
  { name: 'CS1 → CS2 Exporter', game: 'CS1', url: null, why: 'exports your city to a .cs1x file', required: true },
  { name: 'Road Builder', game: 'CS2', url: 'https://mods.paradoxplaza.com/mods/87190/Windows', why: 'builds the converted road types', required: true },
  { name: 'Traffic', game: 'CS2', url: 'https://mods.paradoxplaza.com/mods/80095/Windows', why: 'lane connections and priority signs from TM:PE', required: false },
  { name: 'CS1 → CS2 Importer', game: 'CS2', url: null, why: 'builds the package in your CS2 city', required: true },
];
