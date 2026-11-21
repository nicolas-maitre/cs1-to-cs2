# e2e step plug-ins

Every `*.mjs` file in this folder is loaded by `tools/e2e/run.mjs` and runs as an optional step. Its failure is recorded in `summary.json` and the run goes on. This is the hook for steps owned by other workers (`transit/`, `buildings/`), so they don't have to edit the e2e script.

```js
// tools/e2e/steps/transit.mjs
export default {
  name: 'transit',                        // step name in summary.json; `--no-transit` skips it
  after: 'import',                        // import | connections | shots | end
  enabled: (ctx) => true,                 // optional
  async run(ctx) {
    // ctx: { bridge, repo, runDir, cs1x, package, planFile, placement: { heights, offsetX, offsetZ },
    //        importerDll, log, opt, flag }
    const r = await ctx.bridge.exec('…/Transit.dll', { phase: 'tool', pump: 'tool', args: { plan: ctx.planFile }, timeout: 600 });
    return { lines: r.lines };            // what summary.json records for this step
  },
};
```

**When `after` hooks run:**
- `import`: after the road import and the M2 steps (zoning, traffic, Traffic mod);
- `connections`: after the outside connections;
- `shots`: after the screenshots, before the save;
- `end`: last.

Within one point, plug-ins run in file name order.
