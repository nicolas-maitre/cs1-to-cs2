#!/usr/bin/env node
// Writes docs/guide/{user-guide,faq,what-converts}.md from web/src/core/docs.ts (the same content the web
// app's Help page shows). test/docs.test.ts fails when the files are out of date.
//   node web/scripts/gen-docs.mjs
import fs from 'node:fs';
import path from 'node:path';
import { faqMarkdown, guideMarkdown, matrixMarkdown } from '../src/core/docs.ts';

const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
export const GUIDE_DIR = path.resolve(here, '..', '..', 'docs', 'guide');
export const FILES = { 'user-guide.md': guideMarkdown, 'faq.md': faqMarkdown, 'what-converts.md': matrixMarkdown };

const isMain = process.argv[1] && path.resolve(process.argv[1]) === path.resolve(here, 'gen-docs.mjs');
if (isMain) {
  fs.mkdirSync(GUIDE_DIR, { recursive: true });
  for (const [name, fn] of Object.entries(FILES)) {
    fs.writeFileSync(path.join(GUIDE_DIR, name), fn());
    console.log(`wrote ${path.join(GUIDE_DIR, name)}`);
  }
}
