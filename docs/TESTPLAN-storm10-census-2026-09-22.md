# 1.5.0 world census — live test on Storm10, 2026-09-22

`docs/SCOPE-1.5.0.md` §8, run on Storm10 (Valheim **1.0.12**, network version 40,
`C:\Users\donfr\ValheimServers\Storm10`, port 2477) — the world that already carries a
base, beds, ships and stations from the 2026-09-15 and 2026-09-21 runs. The admin joined
once as **TestNomad** (WhereTheCrowFlies 1.1.3) and placed four portals in two linked
pairs, tagged `plains` and `stones`, so the portal directory has real rows. Every start
was a PowerShell `Start-Process`, every stop a graceful `taskkill` (the vanilla save
completed on each stop: "World save (5/5) done"). Times below are local (UTC-7);
`generated_at` is UTC.

Two builds booted:

| Build | md5 | Bytes | Boots |
|---|---|---|---|
| branch at `15361d8` (the census as implemented) | `76dc6640bdecb95c6cac03a7163b3e42` | 243,200 | 08:08 |
| branch at `a5232e2` (after the second review's fixes) | `e073321c20298de82789033ae8cb61f7` | 243,200 | 08:45, 08:51 (interval 0), 08:53 (token), 08:55 (defaults) |

The page under test is the one embedded in each DLL (`<meta name="theravenscall-api"
content="1.5">`), read at `http://localhost:2112/` in the desktop app's browser pane.
`HttpApiToken` empty and `CensusIntervalMinutes = 5` except where a step says otherwise;
both restored before the last boot, which was left running.

## Results

| §8 step | Result | Evidence |
|---|---|---|
| 1 | PASS | `dotnet build -c Release`: 0 warnings, 0 errors on both builds; the extracted inline script passes `node --check` |
| 2 | PASS | first build, 08:08 boot: `World census: 239033 objects in 43 ms.` a few seconds after `Server systems initialized.`; `/api/census` → `enabled true`, `interval_minutes 5`, `groups.beds 5/5`, `groups.ships 7/7`, `groups.stations 2/2`, `groups.chests 370/0`, `duration_ms 43`. Second build, 08:45 boot: `262520 objects in 18 ms` (the world had grown by the admin's visit; classification now runs before the clock starts), `groups.chests 409/0`, `groups.wards 3/0` |
| 3 | PASS | before anyone joined, `player_ids.json` already named Nomadtest and "Wubarrk Dev" (learned from claimed beds — `beds[]` shows both as `owner`); when the admin joined as TestNomad the file gained `{"id":721169348,"name":"TestNomad"}` and every row that id built names them; `unknown_builders 0` on every boot |
| 4 | PASS | `portals[]` = the four the admin placed: `plains` ×2 and `stones` ×2, all `kind "wood"`, all `builder "TestNomad"`, all `connected true`, positions rounded to integers (`-3608, -1252` / `-3727, -2317` / `35, -21` / `-3705, -952`), sorted by tag; the World panel's directory shows the same four rows with "linked" chips and plain coordinate pairs (no thousands separators). The `connected false` case is covered by the fixture (step 7), not live: no unpaired portal stood on Storm10 |
| 5 | PASS | 08:53 boot with `HttpApiToken = census-gate-test`: `/api/census` → 401 with no token, 401 with `?token=nope`, 200 with `?token=`, 200 with the `X-Api-Token` header; `/api/state` 401, `/api/health` and `/` open. The page: both the census panel and the season panel read "Needs the API token (Settings)."; after entering the token in Settings and "Save & reconnect" the World panel rendered within 8 s (strip, builders, four portal rows) |
| 6 | PASS | 08:51 boot with `CensusIntervalMinutes = 0`: `/api/census` → 200 `{"generated_at":"","enabled":false,"interval_minutes":0,...}` the moment the HTTP server answered (the envelope is set at `Init()` from the configured value — the hardcoded `interval_minutes 5` fallback is gone) and unchanged 25 s later; no census ran and no census line was logged. The page showed the one-line "The census is off" card |
| 7 | PASS | fixture harness (a scratchpad `serve.py` serving `docs/fixtures/*.json` and the branch's page on 2113, and the same with `/api/census` answering 404 on 2114): the panel renders the fixture's 3 builders with "Builders — 1 unknown" and "Unknown (id 100000099)" in both the builders table and the stone portal's row; the Portals header sorts descending then ascending on the second click (unknown names after known ones); the directory shows "(no tag)", "no pair" for `east`, `stone` for the unknown builder's portal; Beds (2) / Wards (1) open and close; a builder link (Ragnvald) opens that player's detail view and Back restores the panel with both details blocks still open. 404 mode: no World card, the season and feed panels unaffected, the Build tab shows only "Building activity", Back leaves the season panel intact; the only console error is the 404 itself |
| 8 | PASS | 375 × 812: no horizontal page scroll (`documentElement.scrollWidth` 375 = `clientWidth`), the group strip wraps to 144 px, all four tables scroll inside their `.table-wrap` (`overflow-x: auto`; the builders table is 623 px wide in a 322 px wrap) |

## Beyond §8 — the second review's must-fixes, checked live

| Fix | Evidence |
|---|---|
| Back no longer leaves the panels blank | live and fixture: after opening a detail view the census panel still holds its content (`innerHTML` 4,775 chars live) while hidden, and the moment "Roster" is clicked the World panel is visible again with the strip and four portal rows — no wait for the next poll |
| "Not player-built" for world-generated objects | the three wards on Storm10 (`groups.wards 3/0`, dvergr guard stones at `-5625, -3462` and neighbours) read "Not player-built" — no "Unknown (id 0)" anywhere on the page |
| positions as coordinate pairs | every Position cell is `x, z` with no thousands separators |
| chests claim | `groups.chests 409/0` on a world nobody has placed a chest in — the world-generated chests carry `Piece`, as the corrected comment, scope table and README now say |
| Build tab guards | live: "Standing in the world: 137 pieces · 5 beds · 7 ships · 2 stations." for Nomadtest; on the 404 harness the tab has no census card at all |

## Also observed

- **Zero TheRavensCall warnings or errors** on any of the five boots (`LogOutput.log`
  scoped to each boot's `Loading [TheRavensCall` block).
- **The shutdown row landed on every stop today** — four "Server shutting down." rows on
  the feed for the four graceful stops, where the 2026-09-21 run saw it 2 of 5 times. The
  1.4.2 destruction-order fix is on this branch; still a race by design, so this is
  evidence, not proof.
- `duration_ms` on the same world: 43 ms with the prefab classification inside the clock
  (first build), 18 ms with it outside (second build) — the walk itself is the small part.
- `player_ids.json` after the run: Nomadtest, "Wubarrk Dev", TestNomad.
- Storm10 left **up** on `e073321c` (pid 37216, boot 08:55) with the defaults restored.

## Not covered

- A world with more than 500 portals, beds or wards (the `*_truncated` flags and the
  once-per-session truncation warning): no such world to hand.
- A modded portal, bed or station prefab (the `kind` fallback to the raw prefab name).
- `CensusIntervalMinutes` outside 1..1440 (the clamp) — checked by reading
  `ClampedIntervalMinutes()`, not live.
