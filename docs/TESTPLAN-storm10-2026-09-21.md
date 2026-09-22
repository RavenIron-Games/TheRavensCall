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
pre-release. Six server cycles between 18:51 and 20:23 local (UTC-7); Chronicle and feed
stamps are UTC. Every start was a PowerShell `Start-Process`, every stop a graceful
`taskkill`.

## Results

| §9 step | Result | Evidence |
|---|---|---|
| 1 | PASS | no `theravenscall.html` under the config or plugin folder before the first boot |
| 2 | PASS | `TheRavensCall 1.4.0 awakens (server-only).`, `All patches applied.`, `Server systems initialized.`, `HTTP server listening on http://localhost:2112`; `/api/health` → `{"status":"ok","version":"1.4.0"}`; no exception attributed to the mod on any of the six boots. The `ravenscall` console command registered on this build (the StormTest `MissingMethodException` is a 0.221.12 artefact) |
| 3 | PASS | first boot: `season.active=false` with the last season remembered from the previous run, `events` = the `startup` row; later boots reload the previous run's rows under the new `startup` row from `event_feed.json` |
| 4 | PASS | feed showed the startup row, the season panel "No season is running. Last season: …", the all-time board from the roster; no console errors |
| 5 | PASS | `player_join` within one poll, `message` equal to the Chronicle line; on the client quitting, a `player_leave` row with detail `(connection lost)` 90 s after the drop (Valheim's lost-connection timeout), playtime accrued, the player offline in the export |
| 6 | PASS | a name in the feed opens the detail view and hides both panels; Back restores them; a `startup` row's `SERVER` is plain text |
| 7 | PASS | run through ServerDevcommands from the admin client (`devcommands`, `event army_eikthyr`, `stopevent`), not the scope's `randomevent`: `raid_start` row with the raid id in `detail`, `/api/state` `raid_active=true` with the name, the header banner; a 1 s sampler saw the flag true for 24 s and then cleared; `raid_end` row with the same id; run twice (before and during a season) |
| 8 | PASS | a kill by hand, one death and the Black Forest discovery each landed within one poll, newest first, right badge and `detail`; the all-time board flipped from the server-observed 9 kills / 8 deaths to the crow's 23 / 20 |
| 9 | not run | the lore tab case; the escape path is `Companion.Esc`, the same one `/api/state` has used since 1.2.4 |
| 10, 11 | *StormTest* | 401 without a token, 200 with `?token=` and the header, the page's token notice and recovery. `HttpApiToken` stayed empty on Storm10 |
| 12 | PASS, with a defect | the tester typed `ravenscall season start TestSeason.` (trailing period). Panel active within 10 s, all-zero rows hidden, `season_start` row, `season_baseline.json` written. **Defect:** Windows refuses a folder ending in a period, the Chronicle re-point to `Chronicle\seasons\TestSeason.\` failed, and because 1.4.0 closes the old writer before opening the new one, no Chronicle line reached disk for that season (7 `Chronicle` warnings). The feed captures before the disk write, so the page never noticed. Fixed on PR #9 (1.4.1): open-before-close, sanitized folder names. Recovered live with `season end` + `season start TestSeason2`: the disk log resumed under `seasons\TestSeason2\` |
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
- **The `shutdown` row is a race on 1.0.12.** Five graceful stops; one wrote the
  `shutdown` row and the session summary (03:13:26Z), four wrote nothing. On the stops
  that wrote nothing `BepInEx\LogOutput.log` shows `ZNet OnDestroy` before the plugin's
  own `OnDestroy`, so `ZNet.instance` was already null and the guard skipped. The 1.3.0
  Chronicle from 2026-09-18 shows the same pattern (9 boots, 7 `shutdown` rows). PR #9
  (1.4.1) remembers the server flag at boot instead of asking `ZNet` at teardown.
- **Raids** went through `event <name>` routed by ServerDevcommands from the admin client.
  The `SetRandomEvent` patch point caught every start and stop; the scope's
  `randomevent` path was not used.
- **Page nits for 1.4.1:** "started 0 days ago" should read "started today"; the empty
  filtered-feed notice should name the group.

## Not run

Step 9 (a tab in `lore.txt`). Steps 10, 11, 18 and 20 ran on StormTest only.

## Storm10 as left

Server up (started 20:23:32 local, the sixth cycle) with the 1.4.0 DLL, `EventFeedCapacity
= 200`, `EnableRaid = true`, `HttpApiToken` empty, the page override deleted. The config
folder keeps the run's artefacts: `event_feed.json`, `seasons.json` (no season running,
last ended TestSeason2), `Chronicle\seasons\TestSeason2\` and an empty
`Chronicle\seasons\TestSeason.\` folder from the defect.
