# 1.4.0 live test — Storm10, 2026-09-21

The client-side half of `docs/SCOPE-1.4.0.md` §9, run on Storm10 (Valheim **1.0.12**,
network version 40, `C:\Users\donfr\ValheimServers\Storm10`, port 2477) with one admin
client on the same build running **WhereTheCrowFlies 1.1.3** and **ServerDevcommands
1.113**. The server-only half ran earlier the same day on StormTest
(`docs/TESTPLAN-stormtest-2026-09-21.md`); steps marked *StormTest* below were not
repeated here.

Build under test: the release DLL, md5 `f9ee434b8b4ed8872c70023c759bd9a9`, 207,872 bytes,
staged as `BepInEx\plugins\RavenIronStudios-TheRavensCall\TheRavensCall.dll` with the 1.3.0
DLL renamed `.off`. The same bytes are in `TheRavensCall-v1.4.0.zip` on the v1.4.0
pre-release. Five server cycles on 1.4.0 (boots at 18:51, 19:33, 20:00, 20:13 and 20:23
local, UTC-7); Chronicle and feed stamps are UTC. Every start was a PowerShell `Start-Process`, every stop a graceful
`taskkill`.

## Results

| §9 step | Result | Evidence |
|---|---|---|
| 1 | PASS | no `theravenscall.html` under the config or plugin folder before the first boot |
| 2 | PASS | `TheRavensCall 1.4.0 awakens (server-only).`, `All patches applied.`, `Server systems initialized.`, `HTTP server listening on http://localhost:2112`; `/api/health` → `{"status":"ok","version":"1.4.0"}`; no exception attributed to the mod on any of the five boots. The `ravenscall` console command registered on this build (the StormTest `MissingMethodException` is a 0.221.12 artefact) |
| 3 | PASS | first boot: `season.active=false` with the last season remembered from the previous run, `events` = the `startup` row; later boots reload the previous run's rows under the new `startup` row from `event_feed.json` |
| 4 | PASS | feed showed the startup row, the season panel "No season is running. Last season: …", the all-time board from the roster; no console errors |
| 5 | PASS | `player_join` within one poll, `message` equal to the Chronicle line; on the client quitting, a `player_leave` row with detail `(connection lost)` 90 s after the drop (Valheim's lost-connection timeout), playtime accrued, the player offline in the export |
| 6 | PASS | a name in the feed opens the detail view and hides both panels; Back restores them; a `startup` row's `SERVER` is plain text |
| 7 | PASS | run through ServerDevcommands from the admin client (`devcommands`, `event army_eikthyr`, `stopevent`), not the scope's `randomevent`: `raid_start` row with the raid id in `detail`, `/api/state` `raid_active=true` with the name, the header banner; a 1 s sampler saw the flag true for 24 s and then cleared; `raid_end` row with the same id; run twice (before and during a season) |
| 8 | PASS | a kill by hand, one death and the Black Forest discovery each landed within one poll, newest first, right badge and `detail`; the all-time board flipped from the server-observed 9 kills / 8 deaths to the crow's 23 / 20 |
| 9 | not run | the lore tab case; the escape path is `Companion.Esc`, the same one `/api/state` has used since 1.2.4 |
| 10, 11 | *StormTest* | 401 without a token, 200 with `?token=` and the header, the page's token notice and recovery. `HttpApiToken` stayed empty on Storm10 |
| 12 | PASS, with a defect | the tester typed `ravenscall season start TestSeason.` (trailing period). Panel active within 10 s, all-zero rows hidden, `season_start` row, `season_baseline.json` written. **Defect:** Windows refuses a folder ending in a period, the Chronicle re-point to `Chronicle\seasons\TestSeason.\` failed, and because 1.4.0 closes the old writer before opening the new one, no Chronicle line reached disk for that season (8 `Chronicle` warnings: one `init failed`, six `write failed`, one `raw write failed`). The feed captures before the disk write, so the page never noticed. Fixed on PR #9 (1.4.1): open-before-close, sanitized folder names. Recovered live with `season end` + `season start TestSeason2`: the disk log resumed under `seasons\TestSeason2\` |
| 13 | PASS | one kill and one death → Nomadtest's standings row 1 / 1 while its lifetime counters in `/api/state` stayed far higher |
| 14 | PASS | restart mid-season (TestSeason2): `active=true` before the first tick, `started_at` unchanged on a UTC-7 host, `standings_since` equal to it, kills and deaths unchanged, playtime higher by the session, the pre-restart rows retained under the new `startup` row, the Chronicle log line naming the season folder, no second default-folder file. A new character (TestNomad) joining mid-season got a row without a baseline entry: 0 kills, 0 deaths, 1 boss kill, while Nomadtest kept 1 / 1 |
| 15 | PASS | `ravenscall season end`: `active=false`, `last_ended="TestSeason2"`, a `season_end` row, `season_baseline.json` gone, the Chronicle back in the default folder; after two more restarts `seasons.json` still says `last_ended "TestSeason2"`, `last_ended_at 2026-09-22T03:09:27Z` and the panel reads "Last season: TestSeason2, ended Nm ago" |
| 16 | PASS | baseline deleted, restart: the log line about counting from now, `standings_since` later than `started_at`, the panel's "counted since 8:00:54 PM" note and "No standings yet" (every row zero after the reset) |
| 17 | PASS | `EventFeedCapacity = 5`: never more than 5 rows, newest first; the rejoin evicted the oldest. Restored to 200 |
| 18 | *StormTest* | capacity 0 → `"events":[]`, file deleted, feed empty line, season panel unaffected |
| 19 | PASS | the 1.3.0 page (49,468 bytes) in the config folder: served with marker `1.3`, roster and per-player detail rendered, only `/api/state` polled, **no** `/api/activity` request, 0 stale-page warnings. Deleted → the 1.4.0 page (marker `1.4`) came back with both panels and `/api/activity` polling |
| 20 | *StormTest* | fixture harness in 404 mode: panels absent, one failed request, roster normal |
| 21 | PASS | base URL set to `http://localhost:2199`: banner "Could not reach http://localhost:2199/api/state (Failed to fetch). Showing last known data.", both panels hidden (the `reconnect()` reset clears `activitySupported` and no 200 arrives), polling kept trying; blank again → panels back, banner gone, 200s on 2112, no reload |
| 22 | PASS | 375 px: `scrollWidth` 375, no horizontal scroll; season and feed panels at x 10 / width 355, the same column as the roster; the gear (34 px, x 202) on the logo's row (x 12, 174 wide). A fixture harness serving the name `O'Brien & <b>Sons</b>` rendered it as text in 10 places (6 feed rows, 4 roster cells), zero `<b>` elements, escaped 17 times in the HTML; the standings table omits it only because its row is all-zero |

## Observations outside the numbered steps

- **Page recovery after a server restart.** On the last cycle the page, left open through
  the stop and the boot, showed the new `startup` row 10 s after the server was back with
  no reload, in a pane the browser reports as `visibilityState: hidden`. One earlier cycle
  needed a manual reload; that did not reproduce.
- **The `shutdown` row is a race on 1.0.12.** Five identical graceful stops of 1.4.0
  (`taskkill`, no `/F`); two wrote the `shutdown` row and the session summary (03:13:26Z
  and 03:36:34Z), three wrote nothing. Nothing in the stop procedure differed, and the
  engine's own `ZNet OnDestroy` log line appears after one silent stop and after one that
  wrote the row, so the log does not show the order of teardown; the guard asks
  `ZNet.instance` at teardown and Unity does not promise that order. The 1.3.0 Chronicle
  from 2026-09-18 holds 16 `startup` rows against 7 `shutdown` rows (not every one of
  those stops was graceful). PR #9 (1.4.1) remembers the server flag at boot instead of
  asking `ZNet` at teardown; every 1.4.1 stop of the evening wrote the row (3 of 3).
- **Raids** went through `event <name>` routed by ServerDevcommands from the admin client.
  The `SetRandomEvent` patch point caught every start and stop; the scope's
  `randomevent` path was not used.
- **Page nits for 1.4.1:** "started 0 days ago" should read "started today"; the empty
  filtered-feed notice should name the group.

## Not run

Step 9 (a tab in `lore.txt`). Steps 10, 11, 18 and 20 ran on StormTest only.

## 1.4.1 fix check (same evening, after PR #9 merged)

Two builds were checked. The first (md5 `426bd85f…`, 209,408 bytes, PR #9 plus the two page
nits and the folder cap) went through a 45-agent pre-publish review, which found the
"started today" label counting 24-hour windows instead of calendar days, the season file
being read back without decoding its escapes, and a boot path that could leave no Chronicle
open; those were fixed and the shipped build (md5 `a1ed4838d0aff30148e64d0fa9e33616`,
210,432 bytes) was checked the same way. With nobody able to type the console command, the
defect path was reproduced through the upgrade case: `seasons.json` hand-set to an active
season with a `season_start`, no baseline file, then a boot.

| Check | Result | Evidence |
|---|---|---|
| Sanitizer, direct | PASS | reflection on the built DLL: `TestSeason.` → `TestSeason`, `CON` → `_CON`, `a<b>c:d` → `a_b_c_d`, `Test\Season "Q".` → `Test_Season _Q_`, `...` and blanks → `season`, 80 chars → 64, 63 chars + `.` → 63, 63 chars + an emoji → 63 (the cut never splits a surrogate pair), an apostrophe kept |
| Season file reader, direct | PASS | `MetaString` on the shipped DLL decodes `Test\\Season \"Q\".` to `Test\Season "Q".`, tolerates spaces around the colon, and returns nothing for a missing key |
| Boot with the broken name, first build | PASS | season `TestSeason.`: `Active season loaded: TestSeason.`, the baseline-missing line with `standings count from this restart`, `Chronicle: …\Chronicle\seasons\TestSeason\TheRavensCall_Chronicle_2026-09-22.log`, 0 warnings; the file holds the `startup` row; `season_baseline.json` written; `/api/activity` `active=true`, `name "TestSeason."` (that build did not trim at load), `standings_since` = the boot |
| Boot with the broken name, shipped build | PASS | season `Test\Season "Q".`: `Active season loaded: Test\Season "Q"` (decoded, trimmed), `Chronicle: …\Chronicle\seasons\Test_Season _Q_\TheRavensCall_Chronicle_2026-09-22.log`, 0 warnings, the `startup` row on disk there, the baseline snapshotted under the trimmed name, `/api/activity` `name` = the decoded `Test` + backslash + `Season "Q"` (the raw body carries it JSON-escaped) |
| Page on the embedded copy, both builds | PASS | marker `1.4`, the season name as plain text (no child elements), "started today", "No standings yet.", the counted-since note; the Combat chip reads "Nothing in Combat yet." while the other chips list rows; the shipped page carries `calendarDaysAgo` |
| Graceful stop, both builds | PASS | `session_summary` and `shutdown` rows in the season-folder file for both season boots (03:39:32Z in `seasons\TestSeason\`, 04:09:28Z in `seasons\Test_Season _Q_\`), the feed on disk with `shutdown` on top, and nothing from either of those two stops in the default-folder file; the restore boot in between stopped gracefully into the default folder as intended (04:06:05Z), so every 1.4.1 stop of the evening wrote the row (3 of 3) |
| Restore, both builds | PASS | `seasons.json` back to no season / `last_ended TestSeason2`, baseline deleted, the server booted again with the Chronicle in the default folder and left up; `Chronicle\seasons\TestSeason\` and `Chronicle\seasons\Test_Season _Q_\` kept as evidence |

## Storm10 as left

Server up (started 21:10:08 local, the ninth cycle of the evening: five on 1.4.0, four on
1.4.1) on the shipped **1.4.1** DLL (`a1ed4838…`), `EventFeedCapacity = 200`,
`EnableRaid = true`, `HttpApiToken` empty, the page override deleted, no season running.
The config folder keeps the evening's artefacts: `event_feed.json`, `seasons.json` (last
ended TestSeason2), `seasons.json.pre141` (the backup taken for the fix checks),
`Chronicle\seasons\TestSeason2\`, the 1.4.1 checks' `Chronicle\seasons\TestSeason\` and
`Chronicle\seasons\Test_Season _Q_\`, and the empty `Chronicle\seasons\TestSeason.\` folder
from the 1.4.0 defect.
