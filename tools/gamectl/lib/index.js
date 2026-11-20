// Public module API of gamectl. Example:
//
//   import { connect, start, stop } from '../tools/gamectl/lib/index.js';
//   const cs2 = await connect('cs2');            // or: const { bridge: cs2 } = await start('cs2', { load: 'MyCity' });
//   await cs2.call('camera.set', { x: 0, z: 0, distance: 800 });
//   await cs2.screenshot(resolveShotPath('cs2'));

export { EXIT_CODES, GamectlError } from './errors.js';
export { gameConfig, installDir, readLocalProps, repoRoot, bridgeDir, resolveShotPath, cs2DataDir } from './config.js';
export { Bridge, connect, health, rpc, readDiscovery, readDiscoveryRaw, discoveryPath, CLIENT_GRACE_S } from './bridge.js';
export { start, stop, status, splitFlags } from './lifecycle.js';
export { tailFile, followFiles, LogCursor, snapshot } from './logs.js';
export { defaultDeps } from './proc.js';
