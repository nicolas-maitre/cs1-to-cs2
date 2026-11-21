#!/usr/bin/env node
// Writes docs/guide/{user-guide,faq,what-converts}.md (English) and docs/guide/fr/*.md (French) from
// web/src/core/docs.ts and docs.fr.ts (the same content the web app's Help page shows).
// test/docs.test.ts fails when the files are out of date.
//   node web/scripts/gen-docs.mjs
import fs from 'node:fs';
import path from 'node:path';
import { faqMarkdown, guideMarkdown, matrixMarkdown } from '../src/core/docs.ts';

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
export const GUIDE_DIR = path.resolve(here, '..', '..', 'docs', 'guide');
/** language → folder under docs/guide */
export const LANG_DIRS = { en: '.', fr: 'fr' };
export const FILES = { 'user-guide.md': guideMarkdown, 'faq.md': faqMarkdown, 'what-converts.md': matrixMarkdown };

const isMain = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(here, 'gen-docs.mjs');
if (isMain) {
  for (const [lang, sub] of Object.entries(LANG_DIRS)) {
    const dir = path.join(GUIDE_DIR, sub);
    fs.mkdirSync(dir, { recursive: true });
    for (const [name, fn] of Object.entries(FILES)) {
      fs.writeFileSync(path.join(dir, name), fn(lang));
      console.log(`wrote ${path.join(dir, name)}`);
    }
  }
}
