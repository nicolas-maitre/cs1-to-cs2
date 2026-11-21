// e2e step plug-in (owner: buildings/). After the roads and the M2 steps: CS1 buildings (from the plan's `buildings`
// section, written by the web tool), then trees/rocks, districts and IMT surfaces (a cs1conv-objects.json built here
// from the .cs1x). Uses buildings/bin/Release/net48/Cs2Buildings.dll, built if missing. `--no-buildings` skips it.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { LOCAL_DIR } from '../../../cs2-importer/lib/driver.mjs';
import { readCs1x } from '../../../buildings/lib/cs1x.mjs';
import { buildObjectsSection, buildDistrictsSection, buildSurfacesSection } from '../../../buildings/lib/objects.mjs';

const pick = (r, keys) => Object.fromEntries(keys.filter(k => r && r[k] !== undefined).map(k => [k, r[k]]));

export default {
  name: 'buildings',
  after: 'import',
  async run(ctx) {
    const dll = path.join(ctx.repo, 'buildings', 'bin', 'Release', 'net48', 'Cs2Buildings.dll');
    execFileSync('dotnet', ['build', path.join(ctx.repo, 'buildings', 'Cs2Buildings.csproj'), '-c', 'Release', '-nologo', '-v', 'q'], { stdio: ['ignore', 'ignore', 'inherit'] });
    const { offsetX = 0, offsetZ = 0 } = ctx.placement ?? {};
    const exec = async (args, timeout = 1800) => {
      try { return await ctx.bridge.exec(dll, { phase: 'tool', pump: 'tool', args, timeout }); } catch (e) {
        if (!/EntityCommandBuffer/.test(e.message)) throw e;
        ctx.log('[buildings] EntityCommandBuffer error on a tool-phase exec; retrying once');
        await ctx.bridge.wait(30);
        return await ctx.bridge.exec(dll, { phase: 'tool', pump: 'tool', args, timeout });
      }
    };
    const out = {};
    const t = () => Date.now();

    // 1. buildings (plan.buildings, if the web tool wrote one)
    const roadPlan = JSON.parse(fs.readFileSync(ctx.planFile, 'utf8'));
    if (roadPlan.buildings?.items?.length) {
      const t0 = t();
      const r = await exec({ op: 'place', plan: ctx.planFile, offsetX, offsetZ, report: path.join(ctx.runDir, 'buildings-report.json') });
      out.buildings = { ...pick(r, ['items', 'placed', 'alreadyPlaced', 'usedAlternative', 'slid', 'movedToFreeLand', 'connected', 'failed', 'missingPrefabs']), ms: t() - t0 };
    } else out.buildings = { skipped: 'no buildings section in the plan' };

    // 1b. utilities: the player's off-road CS1 power lines and pipes (plan.utilities, web 0.8.0+), after the buildings
    if (roadPlan.utilities?.segments?.length) {
      const t0 = t();
      const r = await exec({ op: 'utilities', plan: ctx.planFile, offsetX, offsetZ });
      out.utilities = { ...pick(r, ['ok', 'roadGuard', 'items', 'placed', 'byUtility', 'ends', 'endsSnapped', 'innerNodesToMarkers', 'snappedTo', 'prefabsMissing', 'elevation', 'notSnappedSample']), ms: t() - t0 };
    } else out.utilities = { skipped: 'no utilities section in the plan' };

    // 1c. power plants and wind turbines joined to the grid: transformers, high- and low-voltage links (buildings #44)
    {
      const t0 = t();
      const r = await exec({ op: 'powerhook' });
      out.powerhook = { ...pick(r, ['ok', 'roadGuard', 'producers', 'withHighVoltage', 'transformers', 'needTransformer', 'transformersLinked', 'hvLines', 'lvMarkers', 'lvLines', 'log']), ms: t() - t0 };
    }

    // 2. objects plan from the .cs1x: trees/rocks, districts, surfaces
    const catalogueFile = path.join(LOCAL_DIR, 'samples', 'cs2-catalogue.json');
    if (!ctx.cs1x || !fs.existsSync(catalogueFile)) { out.objects = { skipped: 'no .cs1x or CS2 catalogue' }; return out; }
    const cs1 = readCs1x(ctx.cs1x);
    const catalogue = JSON.parse(fs.readFileSync(catalogueFile, 'utf8'));
    const objectsPlan = {
      format: 'cs1conv-objects', formatVersion: 1, crop: roadPlan.crop, transform: roadPlan.transform,
      objects: buildObjectsSection(cs1, catalogue, roadPlan, {}),
      districts: cs1.districts ? buildDistrictsSection({ districts: cs1.districts, districtRaster: cs1.districtRaster }, roadPlan) : { items: [], stats: { items: 0 } },
      surfaces: buildSurfacesSection(cs1.imt, roadPlan, {}),
    };
    const ofile = path.join(ctx.runDir, 'cs1conv-objects.json');
    fs.writeFileSync(ofile, JSON.stringify(objectsPlan));

    let t0 = t();
    const ro = await exec({ op: 'objects', plan: ofile, offsetX, offsetZ });
    out.objects = { planned: objectsPlan.objects.stats, ...pick(ro, ['items', 'created', 'skippedOnRoad', 'skippedInBuildingLot', 'noPrefab', 'missingPrefabs', 'frames', 'seconds']), ms: t() - t0 };
    for (const section of ['districts', 'surfaces']) {
      if (!objectsPlan[section].items?.length) { out[section] = { skipped: `no ${section} in the crop` }; continue; }
      t0 = t();
      const r = await exec({ op: 'areas', section, plan: ofile, offsetX, offsetZ }, 600);
      out[section] = { ...pick(r, ['items', 'placed', 'named', 'policiesSet', 'failed']), ms: t() - t0 };
    }
    return out;
  },
};
