# Decision log

Important decisions, newest last. Each entry says what was decided, why, and who decided it ("user" = Nicolas, "Claude" = taken autonomously while the user was away; review those).

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
