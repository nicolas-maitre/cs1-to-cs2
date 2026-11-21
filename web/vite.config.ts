import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { defineConfig } from 'vite';
import type { Plugin } from 'vite';
import react from '@vitejs/plugin-react';

/** Dev only: serve %LOCALAPPDATA%\cs1-to-cs2\samples at /samples/ so `?open=/samples/skibidi.cs1x` works. */
function samples(): Plugin {
  const dir = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(), 'AppData', 'Local'), 'cs1-to-cs2', 'samples');
  return {
    name: 'cs1conv-samples',
    apply: 'serve',
    configureServer(server) {
      // Dev only: scripted checks POST a canvas snapshot here (saved to test/.out/, git-ignored).
      server.middlewares.use('/__shot', (req, res) => {
        if (req.method !== 'POST') { res.statusCode = 405; res.end(); return; }
        const name = (new URL(req.url ?? '/', 'http://x').searchParams.get('name') ?? 'shot').replace(/[^\w-]/g, '');
        const chunks: Buffer[] = [];
        req.on('data', c => chunks.push(c));
        req.on('end', () => {
          const out = path.join(import.meta.dirname, 'test', '.out');
          fs.mkdirSync(out, { recursive: true });
          fs.writeFileSync(path.join(out, `${name}.png`), Buffer.concat(chunks));
          res.end('ok');
        });
      });
      server.middlewares.use('/samples', (req, res, next) => {
        const name = decodeURIComponent((req.url ?? '/').split('?')[0]).replace(/^\/+/, '');
        if (!name) {
          const list = fs.existsSync(dir) ? fs.readdirSync(dir).filter(f => /\.(cs1x|json)$/.test(f)) : [];
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify(list));
          return;
        }
        const file = path.join(dir, name);
        if (path.dirname(file) !== dir || !/\.(cs1x|json)$/.test(file) || !fs.existsSync(file)) return next();
        res.setHeader('Content-Type', 'application/octet-stream');
        fs.createReadStream(file).pipe(res);
      });
    },
  };
}

export default defineConfig({
  // relative asset paths: dist/ can be hosted from any folder or sub-path
  base: './',
  plugins: [react(), samples()],
  worker: { format: 'es' },
  server: { port: 5178, strictPort: true },
  test: { include: ['test/**/*.test.ts'] },
} as never);
