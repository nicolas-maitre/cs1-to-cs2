# Decision log

Important decisions, newest last. Each entry says what was decided, why, and who decided it ("user" = the repo owner, "Claude" = taken autonomously while the user was away; review those).

## 2026-11-20

1. **Architecture: two mods + a web tool** (user). The CS1 exporter writes `.cs1x`. A client-side web tool previews it, crops it and maps assets and roads. The CS2 importer rebuilds the city.
2. **Must support** (user): 81 Tiles 2 (cropping is fine), TM:PE, Node Controller, IMT (at minimum it must not crash) and Vanilla+ (WQ) roads → Road Builder.
3. **IMT fill rules** (user):
   - pavement or grass fill over a whole lane → RB median;
   - red solid fill over a whole lane → RB bus lane.
4. **Game control by script, not mouse** (user asked; design by Claude). A bridge mod per game, loopback HTTP with a token, plus the `tools/gamectl` CLI. See `tools/PROTOCOL.md`.
5. **CS2 autosaves are blocked in script-driven sessions** (user asked for it; in memory only, never written to settings). Later the user said overwriting an autosave "isn't a big deal", so the guard stays only because it's harmless.
6. **CS2 junctions: the importer builds every junction itself** (Claude, from Spike 1b).
   - One position per CS1 node.
   - `Permanent` definitions, placed in batches.
   - A CS1 node → CS2 node entity map.
   - Courses never cross or end on an existing edge.

## 2026-11-21 (before the user left for the night)

7. **Partial IMT fills: full coverage only** (user). A lane change (median, bus lane) is applied only when the fill covers (almost) the whole segment. Partial fills are listed in the import report, and the road stays plain.
8. **Grey IMT fills are junction paint, reported only** (Claude, after checking with the user).
   - The 15 grey fills in Skibidi.02 are `FillerSolid` `rgba(136,136,136,224)`: flat paint, not raised pavement.
   - All of them are node (junction) markings, on 6 nodes, each 5–143 m².
   - Node fills have no CS2 equivalent, so they go into the import report.
   - A grey solid fill on a segment would also be reported only. It's paint, not a kerb, so it doesn't become a median.
9. **Test saves** (user): Claude may create new CS1/CS2 cities and saves for tests, named `cs1conv-…`. Existing named saves (e.g. Skibidi.02) are only ever loaded read-only and are never overwritten.
10. **Landing work** (user): sub-sessions build; Claude reviews, merges into `main` and pushes. No PRs.
11. **Overnight scope** (user): after M1, keep going through M2 and M3 (buildings/assets, parks, services, IMT node-fill decals) and make the result release-grade: polished mods and website. Claude coordinates the 4 worker sessions and checks on them every 30 minutes, restarting any that stalled (usage limits, errors).

## 2026-11-21: M1 (overnight, by the worker sessions)

Full reasoning is in each component's log: [cs1-exporter](../cs1-exporter/DECISIONS-exporter.md), [web](../web/DECISIONS-web.md), [cs2-importer](../cs2-importer/DECISIONS-importer.md), [terrain](../terrain/DECISIONS-terrain.md). The main ones:

12. **Exporter: one DLL is both the mod and the `exec` library** (Claude). It has an "Export city" button and a `Exporter.Run(args)` entry point.
    - Its zip writer is hand-written, because .NET 3.5 has none.
    - It writes to a temp file and then renames it, so a crash never leaves a half-written `.cs1x`.
    - Errors are isolated per item and per section.
13. **Exporter: TM:PE / Node Controller data stays out of M1** (Claude). The M1 importer couldn't apply it; it comes with M2.
14. **Exporter: the terrain layout was verified in game** (Claude). The spec's layout is right: the median error is 0.011 m against 6520 ground nodes, while transposed or flipped layouts are off by 22–57 m.
15. **Web: one TypeScript core for both the UI and the CLI** (Claude). The spike's RB generator was ported verbatim; a test checks its output against the spike scripts.
16. **Web: highways get an RB highway profile** (Claude). They have shoulders instead of sidewalks and lose width (22 → 16 m), which is reported.
17. **Web: elevated, bridge, slope and tunnel segments use their ground road's RB config** (Claude). RB builds those forms itself.
18. **Web: CS1 building sub-networks (`Untouchable`: station tracks, park paths, dam roads) are skipped until M3** (Claude). Placing them alone would leave stubs.
19. **Web: inverted CS1 segments are reversed in the plan** (`"reversed": true`; Claude). The importer always builds a segment from `start` to `end`.
20. **Web: auto crop picks the densest area by road segments, not nodes** (Claude). Pipes and power lines skew node density.
21. **Web: `--install-rb` manages only `*-cs1conv.json` files in RB's folder** (Claude). It also removes stale ones.
22. **Importer: a hot-loaded `exec` library, not a deployed mod, for M1** (Claude). One call runs the whole import as a coroutine in `ToolUpdate`. The core has no game references and its own tests. A user-facing mod comes in M2.
23. **Importer: the Spike 1b recipe with spatial batches of 400 segments** (Claude).
    - A node map links batches; nodes are matched within 1 m horizontally and 3 m vertically.
    - Missing prefabs fail their own segments only, never the whole import.
24. **Importer: node elevation rule** (Claude).
    - CS1 terrain offsets under 4 m are ignored.
    - Offsets of 4 m or more (real terraforming, e.g. a highway trench) are kept, within −11.5 … +7.5 m.
    - This changes once real terrain is imported.
25. **Importer: "built" means a path of real edges connects the two nodes** (Claude). The report also counts wrong-prefab edges, elevated and tunnel compositions, and Temp ghosts.
26. **Bridge: `game.new` writes city options only to the in-memory city overrides, never to user settings** (Claude). `save.write` accepts only `cs1conv-*` names.
27. **M1 test map: Corral Riches, with the plan shifted by (+2000, −4000)** (Claude). No built-in map is flat at its centre.
28. **CS1 autosave** (Claude, noted): CS1 rewrote its own `AutoSave.crp` while a script had Skibidi.02 loaded. Nothing unique was lost, and the user doesn't mind autosaves being overwritten. Skibidi.02 itself is never saved.
