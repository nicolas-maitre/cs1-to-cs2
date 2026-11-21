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
  plugins: [react(), samples()],
  server: { port: 5178, strictPort: true },
  test: { include: ['test/**/*.test.ts'] },
} as never);
