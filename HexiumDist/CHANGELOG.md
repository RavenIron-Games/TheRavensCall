<div align="center">

# 📜 Changelog
**The Raven's Call** — RavenIron

</div>

---

## 🟢 [1.7.1] — The Raven Missed Something

*One wrong number, fixed. No config, API shape or dashboard changes.*

### 🩹 Fixed
- **A leave line counted the player who had just left.** The server log read "Nomad has left the world (played 6m). [Day 19] (1 online)" with nobody left, and the Chronicle and `/api/activity` rows for the same leave carried `online_count` 1. The count came from the engine's peer list, which still holds the leaving player while the line is written, and which also holds anyone still connecting (a player at the password prompt counted as online). The server log's `(N online)` and the rows' `online_count` now count the players `/api/state` counts, with their session still open, each name once: the leaver is out of the count, a half-connected peer never counts, the log suffix is left off when the last player leaves (as `ShowOnlineCount` has always done at zero), and at a server shutdown the leave lines count down to none. One limit stays: a player whose connection dies without a disconnect (a crash, a pulled cable) still counts until the server drops the dead connection — the same as `/api/state`. That takes up to 30 seconds, or up to 90 on a crossplay server: Valheim lengthens the timeout for every connection once a crossplay player has connected, until the server restarts.

### Tested
- `dotnet build -c Release`, 0 warnings, 0 errors.
- **Run on Storm10 (Windows, Valheim 1.0.15, crossplay) on 2026-09-23:** the join line read "TestNomad has entered the world. [Day 19] (1 online)". The player's game then dropped without sending a disconnect, so the server kept the connection for the 90-second crossplay timeout (dropped 08:26:07, timed out 08:27:36) and wrote "TestNomad has left the world (connection lost). [Day 19]" with no count; the Chronicle and `/api/activity` rows for that leave carry `online_count` 0. This path read 0 before 1.7.1 too, so it confirms the join count and the crash-window limit, not the fix.
- **Not run yet:** a leave by logging out to the menu, the path the fix changes (1.7.0 wrote "(1 online)" there on Storm10 at 06:37 the same day), and a shutdown with players online.

---

## 🟢 [1.7.0] — The Raven Gives Names

*The store README has claimed since early on that "players can unlock many titles and equip their preferred epithet." Until now that wasn't true — `PlayerRecord.ActiveTitle` was set once, the moment a player's first title was earned, and never changed again. 1.7.0 lets a player pick, from the titles they've actually earned, which one the server uses when it names them — in its Discord and Chronicle narration and on the dashboard and export. Nothing changes on the in-game nameplate.*

### Added
- **The player's `/title` command**, with `WhereTheCrowFlies` 1.2.0+: `/title` (or the F5 console's `title`) lists what you've earned and which one is active; `/title <name>` (multi-word titles work, e.g. `/title Wolf Hunter`) sets it to one you've earned; `/title clear` shows no title, and stays that way. The same `WhereTheCrowFlies` 1.2.0 also adds a small `/titles` panel — click a button instead of typing the name. Travels over a new routed RPC pair — `RavensCall_EventReport_V2` event type 13 (`TitleRequest`) carrying the request, a new `RavensCall_TitleReply_V1` carrying the one-line answer back to that player only, never broadcast — and since this release that reply always carries the player's full earned-title list and active title too, not just the line, so the panel is fresh after every list, set or clear.
- **The admin's `ravenscall title <player> [<title>|clear]`** console subcommand — no client mod required on the server console; from an admin's game client it goes through WhereTheCrowFlies' `ravenscall` routing stub (1.1.3+), exactly like the season commands. Same earned-titles-only rule as the player path; the target player need not be online.
- A title change is logged on the server (`[TheRavensCall] <name> chose the title '<title>'` / `[TheRavensCall] <name> set no title`) but is not narrated: no Chronicle line, no Discord post, no `/api/activity` row. The Progression tab already shows `active_title` and the earned list, so no dashboard change was needed either.

### Compatibility
- A `WhereTheCrowFlies` 1.2.0+ client against an older `TheRavensCall` server: `/title` sends its request and gets nothing back. Against a 1.2.0–1.6.x server the packet still lands on the `RavensCall_EventReport_V2` channel that server already registers, finds no handler for event type 13, and is dropped there — logging one "unknown eventType 13" line only when `LogCombatReports` is on. (Against a `TheRavensCall` older than 1.2.0, which has no V2 channel at all, the routed RPC is ignored outright as unregistered.) Either way the client prints a "no answer" hint after 5 seconds.
- An older `WhereTheCrowFlies` (pre-1.2.0) against this server: no `/title` command exists client-side, but an admin can still use `ravenscall title …` from the server console or their own client's routing stub (WhereTheCrowFlies 1.1.3+).
- `AcceptClientReports=false` disables the player `/title` path the same way it disables every other client report; the admin console path is unaffected.

### Changed
- **Only a player's first earned title activates itself.** Through 1.6.1 any title earned while no title was active became the active one — harmless when the only way to have no title was never to have earned one, but it would have silently undone a `/title clear` at the player's next milestone. Since 1.7.0 a cleared title stays cleared; later titles are added to `titles_earned` and wait there until the player picks one. A player who has never cleared or picked sees no difference.
- `HexiumDist/plugins/TheRavensCall.dll` refreshed: a clean `dotnet build -c Release` of the 1.7.0 source (md5 `19d79acad1d5c4da35685fbe53fc2003`, 279,552 bytes; two clean builds of the same commit are byte-identical). Its decompiled IL is identical, line for line, to build `6b37713a…`, the one that ran on Storm10 in both live runs; no source file or the embedded page changed between the two commits, and the binaries differ in 70 bytes of per-commit Source Link and PE identity.

### Tested
- `dotnet build -c Release` of both halves in the same session, 0 warnings, 0 errors each. Two review passes, 42 and 48 agents (three lenses, three skeptics per finding): the first over the commands and the wire, the second over the reply's title list and the client's panel; the wire layout, the trust boundary and every public claim were read against the code and the decompiled game classes, and every finding that survived its skeptics was fixed.
- **Run on Storm10 (Windows, Valheim 1.0.15) on 2026-09-22 and 2026-09-23** with WhereTheCrowFlies 1.2.0 on the client (`docs/TESTPLAN-storm10-1.7.0-2026-09-22.md`): the build boots as 1.7.0 with zero warnings and the API reports it; clear-and-set cycles from the client landed on the server (`set no title` / `chose the title '…'` in the log, `active_title` following on `/api/state`); the `/titles` panel opened and closed on its key, its Close button and Esc, opened on a record with no titles, and on a record with 16 titles scrolled to the last entry and set it; a death right after was narrated with the picked title; graceful stops with the session summary written.
- **Not run:** the admin `ravenscall title`, the 5-second no-answer hint.
- **Correction to earlier entries:** Storm10 has logged `Valheim version: 1.0.15 (network version 40)` at every boot since at least 2026-09-19, the oldest log still on disk. The 1.4.1, 1.5.0 and 1.6.1 entries below say their Storm10 runs (2026-09-21 and 2026-09-22) were on 1.0.12; they were on 1.0.15.

---

## 🟢 [1.6.1] — The Raven Guards the Season

*One admin-facing fix and the store README's new FAQ. No config, API or dashboard changes.*

### 🩹 Fixed
- **`ravenscall season start` no longer replaces a running season.** Through 1.6.0 a second `season start` while a season was active swapped it in place: the old season got no summary and no `season_end` line, and `seasons.json` never recorded it as ended — its `current_season` and `season_start` were simply overwritten with the new season's, while the Chronicle folder and the standings baseline moved to the new name, so the old season's standings could no longer be worked out. Since 1.6.1 the command refuses while a season is running — the console answers with the running season's name and `ravenscall season end` as the next step, the server log carries one warning, and nothing on disk changes. `season end` first, then `season start`, as it always should have been.

### 📝 Docs
- The store README gained a FAQ of nineteen questions admins actually ask: what players must install, why kills read zero without the crow, the dashboard and its token, rented hosts and what pushing exposes, seasons, the census cost, the BarrkBOT files, Linux, Valheim versions, listen servers, and what to copy when moving a server.

### Tested
- Boot checks on the Storm10 Windows dedicated server (Valheim 1.0.12) on 2026-09-22, three times across the branch and the shipped bytes (`docs/TESTPLAN-storm10-1.6.1-2026-09-22.md`): the mod loads and awakens as 1.6.1, the Chronicle opens, the census runs, zero warnings, graceful stops.
- **Not run in game:** the refusal itself. `ravenscall` is a remote console command that reaches the server only from an admin's game client with WhereTheCrowFlies, so `season start` over a running season was proven by review and reading, not by a live console. The path is one guard on the season name the server already keeps; report anything unexpected on GitHub.

### 🔧 Changed
- `HexiumDist/plugins/TheRavensCall.dll` refreshed: a clean `dotnet build -c Release` of the 1.6.1 source (md5 `7bd20ab0eae86cfe9dc3e4d585dd6d0a`, 275,968 bytes; two clean builds of the same commit are byte-identical). The same source booted on Storm10 during review as build `104b6792…`; the two differ only in the per-commit Source Link and PE identity bytes, not in code.

---

## 🟢 [1.6.0] — The Raven Carries Word

*On a rented game server (Nitrado, G-Portal and the like) the admin gets the game's ports, a web panel and FTP — no shell, no way to open the dashboard's port — so `http://localhost:2112` is unreachable from anywhere. 1.6.0 pushes the mod's data out over HTTPS instead: a small RavenIron-hosted receiver keeps the latest copy, and the same dashboard page reads from it. With `PushUrl` empty (the default) nothing changes: 1.6.0 on a home server behaves exactly like 1.5.0.*

### Added
- **A new `[Push]` config section**: `PushUrl` (default `""`), `PushToken` (default `""`), `PushServerId` (default `""`, required to push, `[a-z0-9-]{1,32}`), `PushIntervalSeconds` (default `60`, clamped 15..3600) and `PushHeartbeatMinutes` (default `10`, clamped 1..1440). See `docs/API.md` and the README's Configuration table for the full rules.
- **The push**: on its own schedule the mod POSTs its three envelopes — the same strings `/api/state`, `/api/activity` and `/api/census` serve — to `PushUrl`, outbound only, the same path out of the host the Discord webhook already uses, so it works from any host. An envelope travels as `null` when unchanged since the last accepted push (a byte comparison with the volatile fields blanked); a heartbeat carries all three regardless, so a receiver that lost them recovers and "last reported" keeps meaning "the server is alive." Bearer-token authenticated, `HttpApiToken` required at 24+ characters before the push runs at all, exponential backoff on failure (60 s, 2, 4, 8, then 15 minutes flat), with the receiver's four configuration refusals each named in the log when they happen (re-logged only when the text changes or an hour has passed) and retried every 15 minutes, and a transport failure's warning re-logged at most once an hour with the consecutive-failure count. See `docs/API.md` for the exact request and refusal text.
- **The receiver** (`hosting/netlify/`): a Netlify site of its own — not the site serving ravenirongames.com — that stores the latest envelopes per server (Blobs, an id-first `TRC_SERVERS` registry) and serves them back under `/s/<id>/api/` (`state`, `activity`, `census`, `health` and `gamedata`), behind the server's own read token, plus the dashboard page itself as a static file. The site root serves a small landing page that says where a server's dashboard lives. See `hosting/netlify/README.md` for the deploy steps. RavenIron's own receiver is not open to other servers in 1.6.0: a hosted dashboard for your server means deploying your own private copy of `hosting/netlify/`.
- **The dashboard page learns a hosted mode**: it derives its API base from its own `/s/<id>/` path, keeps a per-route `ETag` and sends `If-None-Match`, treats a `304` response as "unchanged" with no re-render, pauses all four polls while its tab is hidden and refetches once on return, and shows how long ago the game server last reported ("server reported Ns ago" / "server silent since …", with the roster, feed and World panel greyed out once it's gone quiet), driven by `/api/health`'s body rather than by hosted mode itself. A server that's registered but hasn't pushed yet shows "Registered, waiting for `<id>` to report" with an amber connection dot until its first push lands.

### Known difference
- `/api/gamedata` (386 KB, written once per boot) is not pushed, so on the hosted page fish and resource names fall back to prettified tokens ("Fish1" rather than "Perch") instead of their real names. That's the one visible difference from `localhost:2112`; pushing it is the 1.7.0 follow-up.

### Security
- **What hosting publishes.** A read-token holder sees everything the three pushed routes serve: every known player's stats, skills, titles and death coordinates; the event feed and season standings; and the census, which lists every portal, bed and ward in the loaded world with its rounded x/z — in effect where the bases are. Until 1.6.0 all of that stayed on a machine the admin controls; hosting moves the latest copy to a third party's storage behind one shared token. Leaving `PushUrl` empty keeps a server local exactly as before.

### Tested
- The Linux boot of 1.5.0 passed on WSL2 Valheim 1.0.15 (`docs/TESTPLAN-linux-wsl2-2026-09-22.md`): the mod boots, serves, and counts a copy of Storm10's world to the object, and `HttpWebRequest` completes https requests with certificate validation on. 1.6.0's own push test plan (§8 of `docs/SCOPE-1.6.0.md`) ran locally the same day (`docs/TESTPLAN-local-1.6.0-2026-09-22.md`): steps 1–7 and 10 pass against a local receiver — Storm10 on Windows and the WSL2 Linux server (Valheim 1.0.15) both push; the change gate, the heartbeat, the backoff and recovery, the `401` and `409` refusals end to end (the receiver's `413` and `422` checked from a harness; the 15-minute retry and the hourly re-log not run), `429` handling and the hosted page's waiting, `304`, visibility and stale states all behave as scoped. Then the receiver was deployed to RavenIron's Netlify team and the Linux server pushed to it over https with certificate validation (§8 steps 8 and 9, the release gate); function durations measured at 4–130 ms warm. Not yet run on a rented host. Since 2026-09-22 a real Windows dedicated server on another host pushes to the deployed receiver over the internet (state, feed, a 1.1-million-object census) on the default interval and heartbeat.
- Netlify credit use for the receiver is to be read from the team's usage page after 24 hours of real hosted traffic and recorded here; it was not yet measured when this release was cut.

### 🔧 Changed
- `HexiumDist/plugins/TheRavensCall.dll` refreshed: a clean `dotnet build -c Release` of the 1.6.0 source (md5 `334d21ad2c0870933154cdf5d141bc15`, 274,944 bytes; two clean builds of the same commit are byte-identical). It differs from the build that ran the release gate and the first hosted server (md5 `1799895da19c894ab6d12d3e92a2acd2`, also 274,944 bytes) only in the embedded dashboard page, which carries the hosted-page ETag fix; no C# changed between the two.

---

## 🟢 [1.5.0] — The Raven Counts the Halls

*A world census now runs on the server on a timer and feeds a new World panel on the dashboard. `/api/state` and the BarrkBOT export are untouched — the census is a new route only.*

### Dashboard
- **A World panel joins the season and feed panels**, fed by the new `/api/census` endpoint: a group strip (portals, beds, wards, ships, carts, chests, crafting stations — player-built vs. total for each), a sortable **Builders** table ranking players by what they've placed and what they have standing in the world total, a **portal directory** (tag, kind, builder, connected, position), and closed-by-default **Beds** and **Wards** tables, with a footer naming how many objects were counted, how long it took, and how often it runs. Polls on its own 60-second schedule; a 404 hides the panel only (a pre-1.5.0 server), `enabled: false` shows a one-line notice that the census is off, and the empty pre-first-run payload shows "First count runs a few seconds after boot."
- **The player detail view's Build tab** gains a line summarizing what that player has standing in the world right now — pieces in total, then portals, beds, wards, ships, carts, chests and stations (the groups are parts of that total, zero groups omitted) — alongside the vanilla "portals placed, ever" stat when the server has it (a different, larger number, since it includes portals since torn down).

### Added
- **`GET /api/census`** — the world census: how many portals, beds, wards, ships, carts, chests and crafting stations stand in the world, per-player totals, and directories of portals, beds and wards with builder, position and (for portals) connection status; see `docs/API.md` for the full shape. Token-gated the same as `/api/state`. Never 404s: answers the empty envelope before the first run completes, and `enabled: false` with the interval at 0.
- New config: `[Census] CensusIntervalMinutes` (default `5`, minutes, clamped 1-1440; `0` turns the census off without breaking the endpoint).
- New file under `BepInEx/config/TheRavensCall/`: `player_ids.json` — known player profile IDs mapped to names, so a piece's builder can still be named across restarts.

### Tested
- Live on the Storm10 1.0.12 testbed on 2026-09-22 (`docs/TESTPLAN-storm10-census-2026-09-22.md`): every step of the scope's test plan passed, with real portals placed by the admin for the directory, the token gate and `CensusIntervalMinutes = 0` each on their own boot, the fixture harness for the cases the live world lacks (an unknown builder, an unpaired portal, a 1.4 server's 404), and a 375 px viewport; zero mod warnings across five boots. The chests row on that world reads 409 standing / 0 player-built — world-generated chests count in the total, which is what the docs now say.

### 🔧 Changed
- `HexiumDist/plugins/TheRavensCall.dll` refreshed: a clean `dotnet build -c Release` of the 1.5.0 source (md5 `47bb2d3e4622e3883acf850652aa9f5b`, 246,272 bytes; two clean builds of the same commit are byte-identical), the first packaged build since 1.4.1 — it carries the 1.4.2 fixes below as well. The eight-step live test on the Storm10 1.0.12 dedicated server (`docs/TESTPLAN-storm10-census-2026-09-22.md`) ran on this same source two small page respins earlier; the respins (builder links following the roster, the Build tab card following the census, no replayed feed highlight on Back) were checked on the live page and on timing harnesses, and these exact bytes boot on Storm10 as the release build. Same deterministic-per-commit build as 1.4.0 below.

---

## 🟢 [1.4.2] — The Raven Minds the Edges

*The four follow-ups the 1.4.1 pre-publish review left open, plus the hardening this branch's own review added — all in the season and Chronicle bookkeeping, none of it reachable in normal play on a dedicated server. No config, API or dashboard changes.*

### 🩹 Fixed
- **A stale `season_baseline.json` is no longer trusted.** If a season ended but its baseline file survived (the delete failed, or the server died before `season end` ran) and a new season was then started by hand in `seasons.json`, the old season's start-of-season counters were subtracted from every player's lifetime totals and reported as the new season's standings. The file names the season it was taken for; a mismatch now logs a warning and takes a fresh baseline.
- **A damaged `seasons.json` reads as "no season", with a warning,** instead of a fragment of the file becoming the season name and a folder under `Chronicle\seasons\`: a file cut off mid-value (a torn write; 1.4.1's reader returned whatever it had read, where 1.4.0's regex already refused it) and a value whose closing quote was dropped by hand (the next key's opening quote used to pass for the value's end, on every version). `seasons.json` is now also written atomically, the way `season_baseline.json` already was, so a crash mid-write no longer leaves a torn file behind.
- **Chronicle day rotation checks the log file's own name for the date**, not the whole path. A season folder named with a date in it (or a server installed under a dated directory) used to suppress the rollover for that day, so that day's rows went into the previous day's file.
- **A listen server that hosts twice in one process now re-points the Chronicle to the default folder when no season is active**, instead of keeping the previous world session's season writer. A `seasons.json` that has gone missing is treated as no season, and no season history, for the same reason, rather than leaving the previous session's season in memory. Not reachable on a dedicated server, where the world is loaded once per process.
- **A `season_baseline.json` written by 1.4.0 or 1.4.1 for a season whose name ends in a period is still accepted** after this update: the name it carries is compared trimmed, the way 1.4.1 already trims the running season's name at load. Without that, the first 1.4.2 boot would have taken a fresh baseline and reset the running season's standings.

### 🔧 Changed
- 1.4.2 was never packaged on its own: `HexiumDist/plugins/TheRavensCall.dll` kept 1.4.1's bytes until the 1.5.0 build above, which carries these fixes.

---

## 🟢 [1.4.1] — The Raven Keeps Writing

*Two logging failures found live on the Storm10 1.0.12 testbed on 2026-09-21 (`docs/TESTPLAN-storm10-2026-09-21.md`): a season name with a trailing period silently killed Chronicle disk logging for the rest of the season, and the shutdown line/session summary only wrote when the engine happened to tear the plugin down before its own networking. Both fixed, plus two dashboard wording fixes. No config or API changes.*

### 🩹 Fixed
- **Starting a season with a name ending in a period (e.g. `TestSeason.`) silently stopped Chronicle disk logging for the rest of that season.** Windows drops a trailing period from a folder name, so the log file failed to open in the folder that was actually created, and because the previous log had already been closed by that point, nothing more reached disk afterward (the dashboard's recent-events feed kept working — it doesn't depend on the file). Season names are now cleaned up before the folder is built (invalid filename characters and trailing periods/spaces stripped, Windows reserved names like `CON` prefixed, the folder name capped at 64 characters, with a safe fallback if that leaves nothing), and a bad re-point no longer takes the previous log file down with it — the previous log stays open and in use until the new one is confirmed to have opened, and if a boot ends up with no Chronicle open at all (a failed re-point with nothing to fall back on, or a failed baseline write on a mid-season boot, which now no longer skips the re-point) the default-folder Chronicle opens instead. A restart mid-season resolves the same cleaned-up folder as the original `season start`, including for a name holding a backslash or a quote: the season file is now read back with its escapes decoded, where the old reader handed the raw escaped text to the folder builder. The season's display name is kept as typed apart from trailing periods and spaces, which are trimmed both at `season start` and when a running season is loaded at boot — so a season that was already running as `TestSeason.` when you upgrade shows as `TestSeason` from the first 1.4.1 boot on.
- **The "Server shutting down." Chronicle line and the end-of-session summary (top killer, most deaths, bosses/biomes/titles for the session) were a coin toss on a dedicated server.** Both are written as the plugin is torn down, and the check that was supposed to confirm "this is a server" asked the engine's networking object, which is often already gone at that point — on the Storm10 1.0.12 testbed, 2 of 5 identical graceful stops of 1.4.0 wrote the line, and the 1.3.0 Chronicle from 2026-09-18 holds 16 startup rows against 7 shutdown rows (not every one of those stops was graceful, so that ratio is indicative only). The server now remembers that it's a server from startup and uses that instead.

### Dashboard
- The season panel says **"started today"** (or "started yesterday") instead of "started 0 days ago" on a fresh season.
- When the feed has events but none in the selected filter chip, the panel now says **"Nothing in Combat yet."** (naming the chip) instead of "No events yet.", which read like an empty server.

### 🔧 Changed
- `HexiumDist/plugins/TheRavensCall.dll` refreshed: a clean `dotnet build -c Release` of the 1.4.1 source (md5 `a1ed4838d0aff30148e64d0fa9e33616`, 210,432 bytes). Checked live on the Storm10 1.0.12 dedicated server on 2026-09-21, twice, through the upgrade case (a season already active in `seasons.json` when the server boots, no baseline file): first an earlier 1.4.1 build with the season `TestSeason.`, then these exact bytes with the season `Test\Season "Q".` — the name was read back with its escapes decoded and its trailing period trimmed, the Chronicle re-pointed to `Chronicle\seasons\Test_Season _Q_\` with no warning and the startup row on disk there, the missing baseline was snapshotted, a graceful stop wrote the session summary and the "Server shutting down." row into that same file, and the embedded page showed the name as plain text, "started today" and "Nothing in Combat yet." The `season start` console command itself was not re-run on 1.4.1 (it goes through the same trim and sanitizer, which were also exercised directly on `TestSeason.`, `CON`, `a<b>c:d`, `...`, an 80-character name and a 64-character cut that would split an emoji). Same deterministic-per-commit build as 1.4.0 below.

---

## 🟢 [1.4.0] — The Raven Remembers

*The dashboard's front door now says what's actually happening: a recent-events feed and live season standings, both from a new `/api/activity` endpoint. Getting there meant fixing three pre-existing bugs the feature depends on — raids never registered on a dedicated server, a mid-season restart lost the Chronicle to the wrong folder and leaked a file handle, and the season start time could drift by the host's UTC offset. `/api/state` and the BarrkBOT export are untouched.*

### Dashboard
- **A "World overview" now sits above the roster**, four cards computed from the existing `/api/state` payload (only **Latest deaths** also reads the new feed, see the next bullet): **Online now** (who's online and how long, or the last player seen when nobody is); **All time**, one sortable table ranking every known player by kills, deaths, boss kills and playtime, click a column header to sort by it, click again to flip direction; **Bosses defeated**, the union of every player's defeated bosses on the same strip the Combat tab uses; and **Latest deaths**, the ten most recent deaths merged across all players. Only shown once at least one player is known.
- **A recent-events feed**, backed by the new `/api/activity` endpoint: the last 30 events — joins, leaves, deaths, boss kills, kill and death milestones, biome discoveries, titles, raids, lore and world events, season starts/ends, server start/stop — newest first, with a relative timestamp, a type badge, and filter chips by group (combat, players, world, season). Once the feed holds at least one death, **Latest deaths** switches from the per-player merge above to this true cross-player order.
- **A season panel**, also from `/api/activity`: the active season's name and a standings table — kills, deaths, boss kills and playtime since the season started, sortable, ranked by the page — or "No season is running" plus the last one that ended.
- **The phone header no longer wraps to three rows at 375px.** The settings gear now stays on the logo's row; the world name/day/online-count line wraps to its own row underneath instead.
- **The two "how to reach this server" footer paragraphs are now collapsed behind a "Connection help" toggle**, closed by default, so the footer doesn't dominate a first-time load. The feedback link stays visible outside it.

### Added
- **`GET /api/activity`** — the recent-events feed and the active season's standings; see `docs/API.md` for the full shape. Token-gated the same as `/api/state`. Answers 200 with empty arrays rather than 404, so a 1.4.0 dashboard can tell "turned off" from "server predates 1.4.0".
- New config: `[Companion] EventFeedCapacity` (default `200`, 0-1000) — how many recent events are kept in memory and served, newest first; `0` turns the feed off without breaking the endpoint. `[Events] EnableRaid` (default `true`) — records raid start/end in the Chronicle and the feed; the raid banner and `raid_active` work either way, this only gates the narration.
- Two new files under `BepInEx/config/TheRavensCall/`: `event_feed.json` (the feed, so it survives a restart) and `season_baseline.json` (each known player's counters at season start, so the season standings can be reported as deltas).

### 🩹 Fixed
- **Raids never showed on a dedicated server.** The banner and the `raid_active`/`raid_type` fields only ever flipped for a raid restored from a saved world at boot, and once set, never cleared — a natural raid starting or ending on a running server never touched them at all. Raids now track correctly from start to end, and `raid_type` carries the real event name.
- **The Chronicle stayed in the default folder after a mid-season restart**, instead of returning to that season's own folder, and every restart leaked one open file handle (three call sites, one root cause). Both fixed.
- **The season start time could drift by the host's UTC offset.** A restart mid-season re-read the stored start time as local time and re-stamped it `Z`, so a host running at UTC+2 saw its season "start" two hours later on every reboot. Now parsed and stored correctly as UTC.

### 🔧 Changed
- The `HttpApiToken` config description now lists `/api/activity` among the routes it gates.
- Chronicle line escaping now goes through the same escaper as the rest of the mod, so a stray tab or other control character in a `lore.txt` broadcast can no longer produce a Chronicle line — or a feed entry — that isn't valid JSON.
- `HexiumDist/plugins/TheRavensCall.dll` refreshed: a clean `dotnet build -c Release` of the 1.4.0 source (md5 `f9ee434b8b4ed8872c70023c759bd9a9`, 207,872 bytes), the same bytes that booted on the StormTest dedicated server on 2026-09-21 — a Valheim 0.221.12 testbed, not the 1.0.12 build this release targets — where `/api/health`, the `/api/activity` envelope, feed persistence across a restart, the token gate on both data routes, `EventFeedCapacity = 0`, and the embedded page's panels and 401 notice were checked live, and the page's 404 fallback was checked against a fixture harness answering 404 (not a live 1.3.0 server). On that pre-1.0 testbed the `ravenscall` console command does not register and the shutdown Chronicle row does not fire; both are pre-existing and neither is a 1.4.0 change (see `docs/TESTPLAN-stormtest-2026-09-21.md`). Nothing in 1.4.0 has yet booted on a 1.0.12 server; the raid re-target and the season standings were verified by review against the 1.0.12 server decompile, not yet by a live raid or season with players. The build is deterministic per commit: two clean rebuilds of one checkout are byte-identical, while a rebuild from a different commit differs in exactly 72 of the 207,872 bytes, because the .NET SDK's implicit Source Link writes the commit hash into the PDB and the DLL carries that PDB's checksum: measured, the differences are the PE timestamp (4 bytes), the module version id (16), the debug-directory timestamp (4), the PDB GUID (16) and the PDB SHA-256 checksum (32); the IL, the resources and the embedded page are identical. This also corrects the 1.3.0 note below: those bytes change per commit, not per build.

---

## 🟢 [1.3.0] — The Raven Speaks Plainly

*`/api/state` is now exactly the BarrkBOT export. Breaking for anything reading the old shape; the bundled dashboard page was the only consumer, and its rebuild is a separate PR. Works with WhereTheCrowFlies 1.1.1 (the published client) and newer; nothing on the wire between the client mod and server changes.*

### ⚠️ Changed, read this
- **`/api/state` dropped the legacy dashboard shape and now returns exactly the BarrkBOT export** — the same envelope already written to `BarrkBOT_data1.json` every poll tick: `{generated_at, world_name, day, online_count, raid_active, raid_type, players:{"<name>": PlayerRecord, ...}}`, one `PlayerRegistry.ToJson` row per known player, keyed by name. `raid_active`/`raid_type` move from the removed per-player `combat{}` alias up to this top-level envelope, where they always actually lived (world state, not per-player).
- **The legacy aliases are gone**: `bosses{}`, `combat{session_kills,damage_dealt,damage_taken,raid_active,raid_type}`, `total_kills`, `total_deaths`. They existed only so the old dashboard's render functions kept working; nothing else read them.
- **The whole live-vitals row builder is gone**: health/max_health/stamina/eitr/comfort/weight/guardian/status_effects/position/skills[]/inventory/food/chests/boats/timers/known_recipes/known_materials/weather/tamed, plus the offline row (`player_name`, `biome`, `updated_at`) and the array-of-rows top level it was wrapped in. None of this was ever real on a dedicated server: `Player.GetAllPlayers()` is always empty there, so every `/api/state` response before this release was already just the offline row plus registry fields for every player, dressed up as a live snapshot. Deleting it removes dead branches, not working features.
- **The old `_stateWanted` "only rebuild if someone asked" cache gate is gone.** The BarrkBOT export string is built on the main thread every poll tick regardless — the file write needs it either way — so gating the `/api/state` cache behind a separate request flag saved nothing. The cache is now just assigned unconditionally from that same string.
- See `docs/API.md` for the full shape and field-by-field notes, including which fields are unknown-until-the-crow-reports rather than zero.

### Dashboard
- **`theravenscall.html` rebuilt from scratch** against the 1.3.0 `/api/state` envelope. Six tabs survive — **Combat, Death, Progression, Crafting, Build, Raw** — because a dedicated server can actually observe what they show. Map, Boats, Tamed, Food, Timers, the inventory/vitals widgets and the setup wizard are gone: none of that data exists on a dedicated server, and the old page was silently rendering zeros/blanks for all of it. The Fishing *tab* is gone too, but the data isn't — caught fish (`caught_fish`) now show on the Progression tab.
- **The bundled page now supports the API token.** A settings panel (gear icon) holds an optional base-URL override and the `HttpApiToken` value, both kept in the browser's `localStorage`, sent as an `X-Api-Token` header. A 401 opens the settings panel automatically the first time only — it does not keep re-opening (and overwriting whatever you've half-typed) on every failed poll after that. `Saga.cs`'s `HttpApiToken` description updated to match — it used to warn that "the bundled dashboard page does not send a token"; it now describes the settings panel.
- **"Absent means unknown, not zero" is enforced in the UI**, not just the API: when `vanilla_stats` / `skill_levels` is empty, the Combat / Progression / Crafting / Build tabs show a clear notice instead of a table of zeros. Server-observed data that doesn't need the crow (`creature_kills`, boss progress, titles, biomes, and the server-populated "This session" combat block) still renders normally even for a player who has never run WhereTheCrowFlies.
- **How the new page reads the data.** Lifetime kills and deaths prefer the crow-reported `vanilla_stats` totals and fall back to the server-observed counters with a "server-observed" label; the Death tab's second tile is `deaths_lifetime`, labelled "Server-observed deaths" (`deaths_narrative` is always equal to it, despite its name). The biome strip uses the nine spellings `Saga.cs`'s `NormalizeBiome` emits (`Meadows`, `BlackForest`, `Swamp`, `Mountain`, `Plains`, `Ocean`, `Mistlands`, `Ashlands`, `DeepNorth`). `caught_fish` values are lowercased localisation tokens (e.g. `$item_fish1`) and are resolved against `/api/gamedata`'s `items[]` by `slug`, with a prettified fallback when gamedata is unavailable. `skill_progress` above 1 is read as a percent (vanilla sends 0–1; the server clamps only to 0–100); numeric-string counter keys the server's enum can't name (e.g. `"3968"`) are hidden; `SeekerBrute` and `SeekerSoldier` have their own creature labels; the gear tier card shows the stored `gear_tier` beside its "not reported on a dedicated server" note. The page polls every 10 s; a poll whose payload is unchanged apart from `generated_at` does not re-render, and open `<details>` disclosures survive the re-renders that do happen.
- **No more network fonts.** The old page pulled three Google Fonts families (`Bebas Neue`, `Inter`, `JetBrains Mono`) from `fonts.googleapis.com` on every load; the new one is system-font-only, one file, no build step, and makes no request except to the configured API host.
- **The dashboard is now embedded in the DLL** as a fallback (`<EmbeddedResource Include="theravenscall.html">` in the csproj, `Companion.ReadEmbeddedHtml()`), so a fresh install still serves the page even when `HexiumDist/`'s zip doesn't include the loose HTML file — it never has. Disk overrides (`BepInEx/config/TheRavensCall/`, then the plugin folder) still take priority, in the same order as before. **Upgrade note:** a disk copy left over from before 1.3.0 shadows the new page and can't read the new `/api/state` shape; the server now logs one warning per run when it serves a disk copy missing the new `theravenscall-api` marker, telling the admin to delete it so the bundled 1.3.0 page is used instead.
- **CORS now allows the token header.** A cross-origin dashboard (the base-URL override case) sends `X-Api-Token`, which triggers a preflight request; the server's `Access-Control-Allow-Headers` only listed `Content-Type`, so the preflight failed and the page couldn't authenticate cross-origin. `X-Api-Token` is now allowed too.
- **Feedback link corrected** to `github.com/RavenIron-Games/TheRavensCall/issues` (the old page pointed at a stale fork, `github.com/NomadicWar/SteveCompanion`).
- Fixtures added at `docs/fixtures/api-state-1.3.json` and `docs/fixtures/api-gamedata.json` for previewing the page without a live server; see `docs/DASHBOARD.md`.

### 🔧 Changed
- `HexiumDist/plugins/TheRavensCall.dll` refreshed: a clean `dotnet build -c Release` of the 1.3.0 source (md5 `56716d22e09eccef14e54e5a8c5588fb`), the same bytes that ran on the Storm10 1.0.12 testbed on 2026-09-15 (dashboard, `/api/state`, the stale-page warning and the embedded page all checked live). A rebuild is not bit-identical: 72 of its 169,984 bytes are build identifiers (PE timestamp, module version id, PDB id and checksum) that change per build; the IL, the resources and the embedded page do not.

---

## 🟢 [1.2.4] — The Raven Bars the Door

*The should-fix batch from the 2026-09-15 review. 1.2.3 was never released, so this can ship as the first build after 1.2.2. Nothing on the wire changes shape and no BarrkBOT field is renamed. Pairs with WhereTheCrowFlies 1.1.3 for the console routing; every older client still works.*

### ⚠️ Changed, read this
- **The dashboard and API now listen on localhost only by default.** They used to bind every interface (`http://+:port`) with no login, handing every known player's stats, skills, titles and death coordinates to anyone who could reach the port. Set `HttpBindAllInterfaces = true` to restore network access, ideally with the new `HttpApiToken`, which gates `/api/state`, `/api/gamedata` and `/api/pins` behind `?token=` or an `X-Api-Token` header. `/api/health` and the page stay open. The bundled page does not send a token, so its live data stops loading while one is set.

### 🩹 Fixed
- **Discord masked-link injection.** Character names and death causes reached the webhook embed with only quote escaping, so a name shaped like `[text](url)` posted a clickable link under the bot's identity. `[`, `]` and the backtick are now backslash-escaped for Discord before the JSON escaping, and newlines are JSON-escaped too.
- **Exports are written atomically.** `players/*.json`, `BarrkBOT_data1.json` and `BarrkBOT_data2.json` were truncate-then-write, and the loader treated a truncated tail as "never set", so a crash mid-write silently zeroed a player. All three now go through a temp file and a rename, the way the contract already documents Fatty doing it. A truncated player file found on boot is reported, copied aside as `.corrupt`, and the player starts fresh instead of silently losing fields.
- **The HTTP worker no longer walks live game state.** `/api/state` used to build its rows on a thread-pool thread while the main thread wrote the same dictionaries; it is now served from a string built on the main thread: primed at boot after the registry loads, then rebuilt by the poll tick only after a request has been served, so an unwatched dashboard costs nothing. (`/api/pins` still reads the minimap on the worker, which a dedicated server never has, so it returns an empty list there.)
- **`ravenscall season start | end` can now be run from a client.** The command is flagged server-only and remote; with the WhereTheCrowFlies 1.1.3 routing stub, an admin typing it in their own console has it sent to the server, checked against the admin list, and run there. On the dedicated server console it works as before.
- **Death and fish credits no longer save inline.** Both still did a synchronous file write per accepted report; they now mark the record dirty for the poll tick like every other credit path. A sender also gets one credited death per five seconds, since a real player cannot die twice in that window and each accepted death is a Discord post.
- **Names with filename-invalid characters no longer double-record.** The registry key was read back off the sanitized file name, so `Od:in` loaded as `Od_in` and then got a second record when the real player joined, both saving over one file. The key now comes from the file's own `name` field. Windows reserved device names (`CON`, `COM1`, ...) are prefixed so those players can be saved at all.
- **Docs said 105 vanilla stats.** Valheim 1.0 has about 205; the handoff and README now say so, and that the count is read off the wire.

### 🔧 Changed
- `HexiumDist/plugins/TheRavensCall.dll` is **not** rebuilt in this change; it is still the 1.2.2 Linux build and needs rebuilding at release.

---

## 🟢 [1.2.3] — The Raven Checks the Messenger

*Five findings from the 2026-09-15 review of the pair, all on the server. Nothing on the wire changes shape and no BarrkBOT field is renamed; a 1.1.1 client still talks to this server unchanged.*

### 🩹 Fixed
- **Any connected player could report for any other online player.** Both receivers resolved the sender's peer and then only checked that the *named* player was connected. A ten-line client mod could forge deaths, kills, and, worse, absolute stat and skill snapshots for anyone online, overwriting their lifetime counters. Every self-report (death, damage batch, fish, building, crafting, harvesting, consumables, world events, stat sync, stat snapshot, skill snapshot) is now bound to the reporting peer's own character name; only a kill may still name someone else, because the creature's zone owner reports it and the killer can legitimately be another player.
- **Player deaths were dead code.** `Player.OnDeath` overrides `Character.OnDeath` without calling base, so the server's own `Character.OnDeath` patch could never see a player. The player branch now has its own `Player.OnDeath` target and shares the death dedup key with the RPC path, so a listen host running both mods cannot credit one death twice. Dormant on a dedicated server as before. **The client side of the same bug is WhereTheCrowFlies 1.1.2**: until players run that, no player death reaches this server at all.
- **NaN and Infinity could reach the export.** Stat and skill snapshot floats were assigned unvalidated and `Companion.F` printed them as bare `NaN`/`Infinity` tokens, which is not JSON: a strict reader rejects the whole `BarrkBOT_data1.json`, and `players/*.json` parsed the tokens straight back in on the next boot. Non-finite values are now skipped on receive, the remainder clamped, the formatter never emits a bare token, and the loader drops a poisoned value instead of reloading it.
- **Every rejected connection wrote a phantom player.** `ZNet.RPC_PeerInfo` returns before it assigns the peer id or name on a wrong password, version mismatch, ban, full server or bad session ticket, but the join postfix still ran, created a permanent `A Viking` record (and `players/A Viking.json`), narrated a join, and then swallowed every later rejection in that run. That row violated the contract's "a player absent from `players` has never been seen". The postfix now returns for a peer that is not ready. **Operator step:** delete `BepInEx/config/TheRavensCall/players/A Viking.json` once if it exists; it will not come back.
- **Boss-kill reports were an amplifier.** The dedup key bucketed the reported position at 4 m, so a few metres of jitter per packet minted a new key; each accepted boss kill then did a synchronous file save per player in radius plus a Discord POST, at up to 30 per second per sender. The boss dedup now buckets at 64 m, kill and boss credits mark the record dirty for the poll tick instead of saving inline (as the V2-only credit paths already did; player-death and fish-catch credits still save inline), and a sender gets one credit per boss per ten seconds.

### 🔧 Changed
- `HexiumDist/plugins/TheRavensCall.dll` is **not** rebuilt in this change; it is still the 1.2.2 Linux build and needs rebuilding at release.

---

## 🟢 [1.2.2] — The Raven Learns the Tenth World

*Valheim 1.0.7 landed and the crow started counting a hundred new things. The raven's ledger had room for two hundred lines; the crow now sends two hundred and five.*

### 🩹 Fixed
- **Stat snapshots were silently truncated on Valheim 1.0.** `PlayerStatType` grew from 105 to 205 counters in 1.0, and WhereTheCrowFlies 1.1.1 snapshots all of them. The V2 receiver clamped a snapshot at 200 pairs and never read the tail of the packet, so the last five counters vanished without a single log line - exactly the failure `BARRKBOT_CONTRACT.md` warns about (a file that exists and is wrong). The cap is now 1024; it only ever bounded a malicious sender.
- **Rebuilt for Valheim 1.0.7** against the 1.0.7 dedicated-server assemblies on BepInEx 5.4.2350. Every game reference and all 10 Harmony targets verified by exact signature on both the client and the server build; booted on the real 1.0.7 Linux dedicated server (HTTP dashboard, Chronicle, PlayerRegistry and session tracker all came up). No source change was needed beyond the cap: `Terminal.ConsoleCommand`'s constructor gained a parameter in 1.0, and the `ravenscall` command's registration binds to the new shape through named/optional arguments.
- **The build broke again when the project moved back out of `PAUSED/`** - the same one-directory-too-shallow reference paths 1.2.1 fixed in the other direction. Paths now resolve from the project's real location.

### 🔧 Changed
- Manifest dependency moved to `denikson-BepInExPack_Valheim-5.4.2350` (the Valheim 1.0 pack).
- `vanilla_stats` in `BarrkBOT_data1.json` will carry the 1.0 counters as soon as a WhereTheCrowFlies 1.1.1 client syncs, keyed by Valheim's own enum names as before. Adding, not renaming - nothing BarrkBOT reads today moves.

---

## 🟢 [1.2.1] — Trusting the Crow's Count

*A maintenance pass, caught while rejoining a multi-mod build session: the raven still answered to its old companion's name at startup, its own dashboard doubted the very telemetry it exists to relay, and the project's build had quietly broken the day it moved into a new nest.*

### 🩹 Fixed
- **Dashboard totals didn't trust the crow.** `total_kills`/`total_deaths` — the legacy-shaped fields the web dashboard reads — were still built from the server's own directly-observed counts, which undercount badly (a dedicated server owns no zone with a player in it). `vanilla_stats.EnemyKills`/`.Deaths`, reported by *WhereTheCrowFlies*, already carry the real lifetime totals, and BarrkBOT itself already prefers them per `BARRKBOT_CONTRACT.md` — but the dashboard alias never did. It now prefers the crow-reported totals, falling back to the observed count only until a player's client has synced at least once.
- **Startup logs and `/health` still answered to "SteveCompanion" and its long-retired version number** (`0.2.3`) — leftovers from the mod this one was merged from. Now reports its real name and version.
- **The build had silently broken** the day this project moved under `PAUSED/` — every library reference resolved one directory too shallow, failing 131 ways at once with no single obvious cause. Reference paths fixed; verified clean, 0 warnings, 0 errors.

---

## 🟢 [1.2.0] — Muninn Finally Listens to Both Ears

*`WhereTheCrowFlies` had been broadcasting a full second RPC channel (`RavensCall_EventReport_V2`) since its own 1.0.1 — kills, deaths, damage, building, crafting, harvesting, consumables, world events, and every one of Valheim's ~105 built-in player stats — and this mod only ever registered a listener for the old `RavensCall_CombatReport_V1` channel. An unregistered routed RPC is a silent no-op in Valheim, so none of it errored; it just vanished. A player with 123 lifetime kills and 266 hits on their own Stats screen showed 0 of either here. This release registers the other ear.*

### ✨ Added — `RavensCall_EventReport_V2` receiver
- Full server-side handling for all ten V2 event types: kill, death, damage/defense batches (now including blocks, parries, and damage blocked), fish catches, building, crafting, harvesting, consumables, and world events (boss summons, portal use, guardian powers).
- **Two new event types, added this pass in coordination with `WhereTheCrowFlies`**: `StatSnapshot` (11) and `SkillSnapshot` (12) — both absolute, not delta, reports. `PlayerRecord.VanillaStats` now carries every counter from a player's own in-game Stats screen (deaths, cheats, world loads, kills, hits, PvP kills/hits, jumps, arrows shot, portals, distance, and the rest), and `SkillLevels`/`SkillProgress` carry every skill they've raised — keyed by Valheim's own enum names, read straight off the client's `PlayerProfile.m_playerStats` and `Player.GetSkills()`, no hardcoded ID tables on either side to drift out of sync.
- Because these are absolute snapshots, not accumulated deltas, a player's *entire pre-existing* history backfills the moment their client first syncs — no waiting for enough new activity to "catch up," and no permanent drift if a packet is ever dropped.
- Kill/death/fish crediting is shared with the existing V1 path (`CombatEventDedup` already collapses the redundant dual-send every V2-capable client makes for backward compat). Damage batches avoid double-crediting via a new `V2DamageTracker`: V1's damage packet is only skipped for a player confirmed to also be sending V2 — so a V2-capable client's redundant dual-send doesn't double the numbers, but a player still on `WhereTheCrowFlies` v1.0.0 (V1-only, predates V2) keeps getting credited instead of going silently dark.

### 🔁 Changed
- **JSON exports renamed** for consistent, predictable naming: `barrkbot_players.json` → `BarrkBOT_data1.json`, `steve_export.json` → `BarrkBOT_data2.json`. Same content and refresh cadence, just a naming scheme with room to grow.

---

## 🟢 [1.1.1] — Official Studio Portal & Metadata Sync

*Synchronized release metadata across the RavenIron mod suite, linking the server mod to the official studio portal and setting the foundation for unified telemetry with companion client releases.*

### 🌐 Metadata & Links
- **Website URL updated** to the official RavenIron studio portal (`https://ravenirongames.rglmobile1.workers.dev/`), matching the client companion mod *WhereTheCrowFlies*.
- **Version bump to 1.1.1** across `manifest.json`, `PluginVersion`, and distribution archives.

---

## 🟢 [1.1.0] — The Raven Learns to Listen

*What 1.0.0 called "server-authoritative combat resolution" was, it turns out, resolution that never ran. `Character.Damage` and `Character.OnDeath` fire on whichever client owns the fight — never the dedicated server — so every kill, death, damage, and catch counter has read zero since launch, for every player, every day. The server was never blind by choice; it just never received a signal. This release gives it one.*

### 🔴 The Bug 1.0.0 Didn't Know It Had
A full day of live play across 28 players produced 11 joins, 5 leaves, and not one kill, death, damage point, or fish. No error, no Harmony failure — the patches are correctly written and attached to methods that simply never execute headless. Full writeup: `KILL_TRACKING_FINDINGS.md`.

### ✨ Added — `RavensCall_CombatReport_V1`
- A new server-side RPC receiver credits kills, deaths, damage, and fish catches reported by a companion client mod, **WhereTheRavenFlys** — the client's own `Character.OnDeath`/`Damage`/`FishingFloat.Catch` DO fire authoritatively (they're the owning machine), so it reports what it directly observed instead of the server trying and failing to see it itself.
- Every report is verified against the live connected-player list before anything is credited — a modded client can lie about a name in the payload, it cannot invent a connected player — rate-limited per sender, and deduplicated against the (still dormant, still kept) server-side patches so a future topology where both fire can't double-count.
- New config: `[Combat] AcceptClientReports` (on by default) and `LogCombatReports` (off by default — verbose, for rollout verification).

### 🩹 Fixed
- **Biome discovery and gear-tier polling were also walking an empty list.** `Player.GetAllPlayers()` — the API the old poll tick used to find "who's online" — is empty on a dedicated server, the same root cause as the combat gap above. Biome discovery is fixed the same way (it's pure position math); gear-tier detection needs a player's actual inventory, which still has no server-side API, so it remains a known gap pending its own client report.
- **Join/leave counts drifted apart over a day of play** (11 joins, 5 leaves on the day this was diagnosed) because a crash or Alt-F4 never fires `RPC_Disconnect`/`SendDisconnect`, leaving a session open and `online` stuck true forever. The poll tick now reconciles the registry against the true connected-player list every interval and closes anything orphaned, logging it to the Chronicle as `(connection lost)`.
- Boss-kill proximity credit no longer silently misses players who haven't opted into the minimap's "public position" sharing toggle — `ZNet.GetPlayerList()`'s position field reads `(0,0,0)` for anyone who hasn't, which would have quietly under-credited boss fights the moment this became position-based. Proximity now reads from each player's character ZDO instead, which stays live regardless of that toggle.

### 📜 Architecture note
`Saga.cs`'s header comment claimed combat "fires [on the server] for real combat resolution." It never did. Rewritten to describe what's actually true: join/leave/raids/day are server-observed; combat arrives by report.

---

## 🟢 [1.0.0] — The Two Ravens Become One

*RavenIron's first release. Not a new mod so much as a merger and a rebirth: two older birds — `SkaldSaga`, who narrated the saga, and `SteveCompanionMod`, who kept the ledger — folded into one raven and sent out from a new hall.*

### 🔴 Install It on the Server. Only the Server.
This is the change that matters most, and it isn't a bullet point buried in the middle of a list: **The Raven's Call runs entirely server-side.** Its two ancestors were client mods every player had to install; this one is not. Nothing here needs a menu key, a HUD panel, or a single Viking to know it exists. Drop the DLL in the **server's** `BepInEx/plugins/`, and every feature below is already live for every player who connects — no client install, no version-matching, no "everyone update before you log in."

### ✨ Added — Server-Authoritative Tracking
- **Kills, deaths, and damage are now attributed to the player who actually caused them**, resolved from the server's own combat resolution — not assumed to be "whoever's game client is currently reporting." A creature killed by no one's hand (environment, another creature) is no longer wrongly credited to a bystander.
- **Boss kills credit everyone in the fight**, not just whoever's client happened to detect it first — configurable radius, same as before, now computed once by the one machine that actually knows where everyone stood.
- **Biome discovery and gear-tier tracking moved off client UI callbacks onto server-side polling** — the old hooks (a UI toast, an equip-menu callback) never fired on a headless server to begin with, so this isn't a change in behavior so much as the feature actually working for the first time in a dedicated-server context.
- **A multi-player data store replaces the old single-active-character model.** Both ancestor mods kept exactly one character's data in memory, swapped in and out — fine for a client watching one player, useless for a server watching a whole roster at once. The Raven's Call now holds every known player's record simultaneously, one JSON file per player, loaded at startup and saved as it changes.

### 🤖 Added — The BarrkBOT Export
- **`barrkbot_players.json`**, refreshed on a timer, one aggregate file with every player's kills, deaths, titles, gear tier, biomes, bosses, caught fish, playtime, and recent death history — built specifically to be read by a Discord bot with zero game-client dependency. See the README for the full schema and a bot-side read example.
- Session-scoped stats (`session_kills`, `damage_dealt_session`, `damage_taken_session`) now correctly reset on every reconnect — previously (in development) these could silently accumulate for a server's entire uptime rather than resetting per session.
- Lifetime playtime is now tracked and persisted per player, accumulated on every disconnect and surviving server restarts.

### ✨ Added — Kept From Both Ancestors
- Discord webhook narration for deaths, boss kills, milestones, biome discovery, gear tier, and titles — colored embeds, same nine event types as `SkaldSaga` always sent.
- The JSONL Chronicle log, rotating daily, unchanged in format.
- Titles earned through creature-family kill counts and boss kills, up to *Slayer of Gods* for felling all seven — the full title table carried over from `SkaldSaga`.
- The admin season system (`ravenscall season start/end`), archiving the Chronicle by era.
- The full player-stats web dashboard (`theravenscall.html`, formerly `stevecompanion.html`) — inventory, crafting, combat, death history, fishing, timers, boats, tamed creatures, progression — now correctly showing **every player who has ever connected**, not just whoever was locally logged in when the old mod ran.
- Randomized narrative message templates (`EnableNarrativeMode`), so a death or a boss kill doesn't read the same way twice in a row.

### 🩹 Fixed (relative to the two ancestor mods)
- **The dashboard's `/api/state` no longer produces duplicate JSON keys.** The old per-character merge stitched three separate files together with raw string surgery, appending a second copy of `bosses`/`combat`/`death_history` on top of the first — technically valid JSON, but only correct under a "last key wins" parser. The new single-source-of-truth merge emits each field exactly once.
- **Kill counters no longer count deaths nobody actually caused.** The old companion mod incremented a kill counter for *any* creature death a client happened to observe, including ones dealt by other players, tamed creatures, or the environment.
- **A resummoned-and-refought boss now credits correctly.** An early build of this merge's boss-kill dedup logic kept a permanent record of "this boss, at this position, already killed" that never expired — silently blocking credit for every kill after the first at any altar players return to. Fixed before release; re-fighting a boss now always credits properly.
- **Fish-catch attribution now uses the actual catching player** instead of assuming it was whoever's client was running.
- Removed the old client→server RPC relay for boss-kill detection entirely — redundant now that the server detects its own combat authoritatively, and one less network round-trip per boss fight.

### ⛔ Removed
- All custom in-game UI: the event-feed HUD panel, the title-selection menu, the stats panel, and their keybinds. These required a client install to render at all, which contradicts this mod's whole reason for existing. Deaths, boss kills, and milestones are still fully visible — through Discord and the Chronicle, which don't ask anything of a player's machine.
- The per-peer "lore whisper" (a center-screen message shown only to a client with the mod installed) — replaced by periodic lore postings to Discord/Chronicle, visible to everyone rather than no one.
- The bespoke config-sync RPC system. With no client install to sync *to*, it had nothing left to do.
