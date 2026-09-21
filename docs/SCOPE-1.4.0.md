# TheRavensCall 1.4.0 — scope

*Status: proposed, 2026-09-21. Nothing here is built. Line numbers cite `main` at `d59f296`;
Valheim lines cite an ilspy decompile of `RandEventSystem` and `Terminal` from the 1.0.12
server build (`valheim_server_Data/Managed/assembly_valheim.dll`).*

1.4.0 is the dashboard release. The 1.3.0 page is a per-player stats browser with an
empty front door: a world with three players shows three cards and blank screen, and
nothing on it says what happened today, who leads, or whether a season is running. The
mod already computes all of that for Discord and the Chronicle; none of it reaches the
page because `/api/state` does not carry it.

Two tiers, one release, two PRs:

| Tier | What | Server change | PR |
|---|---|---|---|
| 1 | World overview above the roster computed from `/api/state` (online now, all-time leaderboard, server-wide boss progress, latest deaths); phone header fix; connection help behind a toggle | none | page-only, first |
| 2 | A `/api/activity` endpoint: the recent-events feed and the active season with its standings; the page's feed and season panels; three bundled fixes the feature depends on (raids, the Chronicle's season folder, the season start time) | yes | server + page, second |

`/api/state` keeps its exact shape and field set. It stays byte-for-byte the BarrkBOT export.

---

## 1. Decisions

1. **One new route, `GET /api/activity`**, carrying `season` and `events`. Both are built in
   the same main-thread tick from the same state; two routes would double the cache field,
   the empty-envelope constant, the token-gate entry, the route branch and the page's
   poll/compare/render cycle for nothing. One route is also one feature-detect.
2. **The feed is the last N Chronicle lines, held in memory.** A feed entry is exactly the
   JSON object `Chronicle.Write` already builds (`Saga.cs:2034-2043`): `timestamp_utc`,
   `day`, `online_count`, `event_type`, `player_name`, `message`, `detail`. No second
   format, no second escaper, and an admin who reads the Chronicle already knows the shape.
3. **The feed survives restarts.** It is written to its own small file on the poll tick when
   dirty and reloaded at boot. Seeding from the Chronicle log files was rejected (§10).
4. **Season standings are server-side, all-time standings are page-side.** Only the season
   board needs state the page cannot see (the counters at season start). The all-time board
   is tier 1, computed from `/api/state`.
5. **Three bundled fixes**, all pre-existing and all load-bearing for the feature: the raid
   patch hooks a method natural raids never call (§4.5); the Chronicle writes to the wrong
   folder after a mid-season restart and leaks a writer when re-pointed (§4.6); the season
   start time is re-read as local time and re-stamped as UTC (§4.4).
6. **Rebuild on the poll tick only.** Events append to the buffer in real time; the served
   string is joined once per `PollAllPlayers` tick (`Companion.cs:444-482`), the same
   staleness `/api/state` already has, never per event and never on the request thread.
7. **The panels live inside the page's content column.** `<main id="app">` becomes a
   `<main>` wrapper holding `<div id="worldPanels">` and `<div id="app">`, so the new panels
   inherit `main`'s width, margin and padding (`theravenscall.html:145-151`) and its phone
   rule (`:445-451`) without new layout CSS, while staying outside the `#app` swap.

---

## 2. `GET /api/activity`

```json
{
  "generated_at": "2026-09-21T18:32:04.1234567Z",
  "season": {
    "active": true,
    "name": "Ashen Dawn",
    "started_at": "2026-09-18T00:00:00Z",
    "standings_since": "2026-09-18T00:00:00Z",
    "last_ended": null,
    "last_ended_at": null,
    "standings": [
      { "name": "Ragnvald", "kills": 142, "deaths": 6, "boss_kills": 3, "playtime_seconds": 41302 }
    ]
  },
  "events": [
    { "timestamp_utc": "2026-09-21T18:31:58Z", "day": 143, "online_count": 3,
      "event_type": "boss_kill", "player_name": "Ragnvald",
      "message": "Ragnvald helped defeat Bonemass!", "detail": "Bonemass" }
  ]
}
```

| Field | Type | Notes |
|---|---|---|
| `generated_at` | string | `DateTime.UtcNow.ToString("o")`, the format `/api/state` uses (`PlayerRegistry.cs:436`), so the page's `stripGeneratedAt` (`theravenscall.html:710-712`) works unchanged. Empty string before the boot prime. |
| `season.active` | bool | `!string.IsNullOrEmpty(_currentSeason)` (`Saga.cs:2209`). |
| `season.name` | string | `""` when inactive. Never absent. |
| `season.started_at` | string or null | `yyyy-MM-ddTHH:mm:ssZ`, the format `SeasonSystem` already writes (`Saga.cs:2269,2291`), UTC once §4.4's parse fix lands. `null` when inactive. |
| `season.standings_since` | string or null | When the baseline was taken. Equal to `started_at` except after a mid-season upgrade (§4.4). `null` when inactive. |
| `season.last_ended`, `season.last_ended_at` | string or null | The single most recently ended season. Today `seasons.json` only holds it until the next season starts (`Saga.cs:2295-2298`); §4.4 makes it remembered. |
| `season.standings[]` | array | `[]` when no season is active. One row per known player, **unsorted**: the page ranks. Each counter is that player's lifetime counter minus the baseline taken at season start, clamped at 0. Rows whose counters are all zero are included; the page hides them. |
| `standings[].kills` | int | `TotalKillsLifetime` (`PlayerRegistry.cs:46`) delta. Incremented by every credited creature kill, client-reported or server-seen (`Saga.cs:677`). |
| `standings[].deaths` | int | `TotalDeathsLifetime` (`PlayerRegistry.cs:47`) delta (`Saga.cs:608,746`). |
| `standings[].boss_kills` | int | `BossKillsCredited` (`PlayerRegistry.cs:36`) delta (`Saga.cs:711`). |
| `standings[].playtime_seconds` | long | `PlaytimeSecondsLifetime` (`PlayerRegistry.cs:27`) delta. Accrues on leave (`Saga.cs:427,500`), so an online player's current session is not in it; the page may add `now - session_start` from `/api/state`. |
| `events[]` | array | Newest first, at most `EventFeedCapacity` entries, `[]` when the feed is off or nothing has happened. Each element is a Chronicle line verbatim. |
| `events[].event_type` | string | One of the 17 types in §4.1. |
| `events[].player_name` | string | `"world"` or `"SERVER"` for world-scoped rows, the defaults `Narrate`/`FireEvent` supply (`Saga.cs:160,174`) or the literals their callers pass (`Saga.cs:2241,2258,2356,2368`). |
| `events[].message` | string | The raw narrative `message`, exactly what Discord and the Chronicle get. Not `FormatMessage`'s output: the emoji prefix, season prefix, `[Day N]` and `(N online)` decorations reach only the BepInEx console (`Saga.cs:158-160,172-176`). `day` and `online_count` are typed fields on the row instead. |
| `events[].detail` | string | The event's `detail` as the Chronicle records it: the death cause, the boss prefab, the milestone count, the title, the biome key, the raid name, the `player_leave` duration text (`Saga.cs:432,510`). `""` when none (`world_event`, `lore`, `startup`, `shutdown`). |

**Token gate:** yes. `/api/activity` joins the path list in `ApiTokenOk` (`Companion.cs:327`).
Player names appear in every standings row and most event rows, the data the
`HttpBindAllInterfaces` description warns about (`Saga.cs:120`).

**Size:** a Chronicle line is 150-300 bytes in practice. 200 entries ≈ 50 KB typical; the
per-line cap and the capacity clamp in §4.2 bound the worst case at 2 MB. Standings ≈ 100
bytes per known player. Smaller than `/api/state` on any real roster at the defaults.

**Never 404 while the server is 1.4.0.** `EventFeedCapacity = 0` turns the feed off, but the
route still answers 200 with `"events":[]`. A 404 is the page's only signal that the server
predates 1.4.0; "turned off" and "too old" must stay distinguishable.

**Not a BarrkBOT contract.** The BarrkBOT contract is the export *file*
`BepInEx/config/TheRavensCall/BarrkBOT_data1.json` (`BARRKBOT_CONTRACT.md:16`, the authority
on the file per `docs/API.md:39`); `/api/state` is frozen to it because it serves that
byte-for-byte string (`docs/API.md:17`). `/api/activity` is the dashboard's own endpoint; a
future consumer is welcome to it, but its shape is documented in `docs/API.md` as 1.4.0's,
not promised.

---

## 3. The page

### 3.0 Tier 1 (page-only PR, ships first)

All computed from `/api/state`, all inside the existing `renderRoster()` output
(`theravenscall.html:821-840`), so they refresh with the roster and cost no server change:

- **Online now**: a strip of the online players (`online`, `session_start`), or "Nobody
  online" with the last-seen player.
- **All time** board: one table, every known player, ranked by kills with deaths, boss
  kills and playtime as sortable columns. Kills and deaths follow the existing
  `killsValue`/`deathsValue` rule (vanilla `EnemyKills`/`Deaths` when present, else the
  server-observed counters with the "server-observed" tag); boss kills from
  `boss_kills_credited`; playtime from `playtime_seconds_lifetime`.
- **Server-wide boss progress**: the union of every player's `bosses_defeated`, reusing the
  Combat tab's boss strip.
- **Latest deaths**: every player's `death_history` merged and sorted newest first, ten rows.
- **Header fix**: at 375 px the gear button wraps onto a third row under the world line,
  because `.header-row` is `flex-wrap: wrap` (`:72-78`) and lays children out in source
  order. Fix inside the existing `@media (max-width: 400px)` block (`:445-451`):
  `.gear-btn { order: 0 }` and `.world-meta, .header-sep, .conn-wrap { order: 1 }`, which
  puts the gear beside the logo and the header on two rows.
- **Connection help behind a toggle**: the three footer paragraphs (`:477-481`) collapse
  into a `<details>` headed "Connection help", closed by default, keyed with
  `data-details-key` so `render()` preserves it (`:766-770,785-788`).

### 3.1 Tier 2 panels

**Markup.** `<main id="app">` (`:473`) becomes:

```html
<main>
  <div id="worldPanels" hidden>
    <div id="seasonPanel"></div>
    <div id="feedPanel"></div>
  </div>
  <div id="app"></div>
</main>
```

`#app` keeps its id, so `getElementById('app')` and the `#app.innerHTML` swap in `render()`
(`:760-789`) work unchanged. `#worldPanels` is outside that swap on purpose: `pollState`
skips `render()` whenever `/api/state` is unchanged (`:727-733`), so panels inside `#app`
would refresh only when the roster happened to change, and would lose the feed's scroll
position on every roster tick. `renderHeader()` (`:791-819`) is the precedent: own
container, own render function, own trigger. `#worldPanels` starts `hidden` and is shown
only once `/api/activity` has answered.

**Feed panel.** Newest 30 rows after filtering, each: relative time (`relativeTime`,
`:607-617`), an event-type badge, the escaped message (`esc`, `:573-578`), and the player
name as a link into the detail view when `player_name` is a known player; world-scoped
rows (`"world"`, `"SERVER"`) show it as plain text, because `render()` bounces an unknown
selection back to the roster (`:778`). Filter chips by type group (combat, players, world,
season), with the selection kept in `state`, not in the DOM. Rows newer than the previous
poll's newest `timestamp_utc` get a brief highlight. "No events yet" on a fresh feed.

**Season panel.** Active: name, a green `ACTIVE` tag, "started N days ago", the standings
table headed **This season**, ranked by kills with deaths, boss kills and playtime as
sortable columns, all-zero rows hidden, a "counted since <date>" note when
`standings_since` is later than `started_at` by more than a minute. Inactive: "No season is
running" plus the last-ended line. The tier 1 board keeps its **All time** heading, so the
two never share one.

**Latest deaths.** When the feed has at least one `player_death` row, the widget is the feed
filtered to `player_death`, a true cross-player chronological source; otherwise the tier 1
merge of each player's `death_history`.

**State**, added to the object at `:554-567`: `activityData`, `lastActivityCompare`,
`activitySupported` (null = unknown, true, false), `activityAuth` (false after a 401),
`feedFilter`.

**Polling.** A sibling of `apiFetch` (`:697-705`), so `/api/state`'s error contract is
untouched. `pollActivity()`:

- 404 → `activitySupported = false`, `#worldPanels` stays hidden, polling of the route stops
  (one failed request per page load, which the browser's network panel shows; no banner, no
  retry every 10 s).
- 401 → `activityAuth = false`; the panels show one line, "Needs the API token (Settings)",
  and nothing else. Cleared by the next 200.
- 200 → compare `stripGeneratedAt(text)` with `lastActivityCompare` and re-render only on a
  change, the `pollState` pattern (`:726-733`), so an unchanged feed never resets scroll or
  chips.
- Any other failure (timeout, network) leaves the last known good data on screen and retries
  next poll.

`pollActivity` touches none of the shared `/api/state` signals: `connStatus` is set only in
`pollState` (`:717,735`) and on a settings save (`:1095`) and drives the connection dot and
error banner (`:805,816`); the once-only 401 settings auto-open is gated by the thrown
error's `unauthorized` flag and the `settingsAutoOpened` latch (`:736-743`).

**Detail view.** Both render paths consult `state.view`: `render()` hides `#worldPanels`
while `state.view === 'detail'` at its existing view branch (`:778-782`), and
`renderActivity()` returns early in that view, so the panels cannot reappear over the detail
view on the next activity tick.

**Click delegation.** The handler that opens a player's detail view is bound to `#app` alone
(`:1103-1105`); a link inside `#worldPanels` would be inert and throw nothing. Extract that
body into `selectPlayer(name)` and bind a second listener on `#worldPanels`.

**Settings save.** `reconnect()` (`:1090-1099`) re-runs the state poll and gamedata load
after a settings save; it must also reset `activitySupported = null`, `activityAuth = true`
and call `pollActivity()`, or a base-URL change to a newer server never retries the route.

**Boot** (`:1141-1146`): `pollActivity()` beside `pollState()`, and a second
`setInterval(pollActivity, POLL_MS)` on the existing `POLL_MS = 10000` (`:509`).

**CSS.** Reuses `.card`, `.notice`, `.table-wrap`, `.tag`, `.fallback-tag` and the `:root`
tokens (`:9-32`). New rules: the badge classes, the chip row, the tier 1 header `order`
rule. No new `@media` rule and no new layout rule: the panels sit inside `main`.

**Marker.** `<meta name="theravenscall-api" content="1.3">` (`:6`) becomes `"1.4"`.
Cosmetic: `ContainsApiMarker` (`Companion.cs:268-272`) checks only that the substring
`theravenscall-api` is present and never reads the value.

---

## 4. Server changes

Everything new runs on the main thread. The HTTP worker reads one new `volatile string`,
the discipline `Companion.cs:31-34` documents as the fix for the 2026-09-15 race.

### 4.1 Capture: inside `Chronicle.Write`

`Chronicle.Write` (`Saga.cs:2019-2048`) is the one place every narrated event already
passes through: `Narrate` (`Saga.cs:160`), `FireEvent` (`Saga.cs:174`), and the
`startup`/`shutdown` rows (`Saga.cs:340`, `Saga.cs:143`). Restructure it so the line is
built and handed to the feed **before** the `EnableChronicleLog` gate, and only the disk
write stays behind the gate:

```
lock (_lock)
{
    build jsonLine exactly as today
    EventFeed.Append(jsonLine)                       // new; always
    if (EnableChronicleLog) { rotate if the day changed; _writer.WriteLine(jsonLine) }
}
```

The feed then works with the disk log turned off, and the per-event `Enable*` toggles in
`[Events]` (`Saga.cs:90-98`), checked at the call sites before anything is narrated
(`Saga.cs:387,431,509,622,720,761,1571,1594,1617,1634,1650,1766`), gate the feed for free.

**Lock order.** `Chronicle._lock` is taken first, then `EventFeed`'s own lock inside
`Append`. The invariant to keep: `EventFeed` never calls into `Chronicle` or `Plugin.Narrate`,
including from its warning paths; it logs through `Plugin.Log` only.

**Covered, 17 types:** via `FireEvent`: `player_death`, `boss_kill`, `biome_discovery`,
`gear_tier`, `kill_milestone`, `death_milestone`, `title_earned`. Via `Narrate`:
`player_join`, `player_leave`, `season_start`, `season_end`, `world_event`, `lore`. Via
`Chronicle.Write` directly: `startup`, `shutdown` (gated by `LogServerStartStop`,
`Saga.cs:103`), which double as restart markers in the feed. New in §4.5: `raid_start`,
`raid_end`.

**Not covered, deliberately:** `session_summary` and `season_summary` go through
`Chronicle.WriteRaw` (`Saga.cs:2162`, `Saga.cs:2279`) with hand-built objects of a
different shape. `WriteRaw` (`Saga.cs:2050-2058`) is not touched.

**Inherited quirk, documented:** `gear_tier` fires only from the dormant `CheckPlayer` path
(`Saga.cs:1617`); the live path cannot (`Saga.cs:1553-1556`). It will not appear, so the
feed carries 16 types in practice.

### 4.2 `EventFeed` (new static class, `Saga.cs`, after `Chronicle`)

- `List<string> _entries`, newest first, `Insert(0, line)` and trim from the tail, the
  idiom `DeathHistory` uses (`Companion.cs:672-673`).
- Its own lock on every touch (`Append`, `ToJsonArray`, `Save`, `Load`). Uncontended in
  practice since every caller is main-thread, but it removes the "main thread only"
  invariant from the list of things a future patch can break.
- Capacity from `EventFeedCapacity` (§6), clamped 0..1000. `0` clears, disables, and deletes
  `event_feed.json` at `Init` so a later re-enable does not resurrect stale rows.
- **Per-line cap:** a line longer than 2048 characters is dropped with one warning per run.
  Gameplay lines are 150-300 characters; only a malformed `lore.txt` entry gets there. With
  the clamp this makes the bound real: 1000 × 2 KB = 2 MB of text worst case, plus one
  joined copy per tick.
- `_dirty` flag set by `Append`, consumed by `Save`.
- `ToJsonArray()` = `"[" + string.Join(",", _entries) + "]"`.

### 4.3 Persistence: `BepInEx/config/TheRavensCall/event_feed.json`

Written compact, one line: `{"saved_at":"<ISO>","events":[<the lines, newest first>]}`.
Compact matters: `Companion.JsonGetArray` matches the literal needle `"events":[` with no
whitespace tolerance (`Companion.cs:770`).

- **Save:** from `PollAllPlayers` when dirty, via `PlayerRegistry.AtomicWrite`
  (`PlayerRegistry.cs:193`), at most once per tick, **outside** the `EnableHttpServer` guard
  so the feed persists even with the HTTP server off; and from `Plugin.OnDestroy`
  (`Saga.cs:136-147`) before `Chronicle.Close()`. Skipped when capacity is 0.
- **Load:** `EventFeed.Init()` in `Patch_ZNetAwake.Postfix` immediately after
  `Chronicle.Init()` (`Saga.cs:328`), so the feed exists before the `startup` row is written
  at `Saga.cs:339-340`. Parses with `Companion.JsonGetArray(raw, "events")`
  (`Companion.cs:768-808`), which returns raw object elements. That helper has no failure
  signal, so each element is checked before it is trusted: it must start with
  `{"timestamp_utc":"` and end with `}`, the house heuristic
  (`PlayerRegistry.cs:235-243`); anything else is dropped and counted in one warning.
  **Load merges:** rows already appended before `Init` ran stay on top (the world-load raid
  case in §4.5); loaded rows go under them; then trim to capacity.
- The file is smaller than `BarrkBOT_data1.json`, which the same tick already writes
  unconditionally (`Companion.cs:466`).

### 4.4 Season state, baseline and standings (`SeasonSystem`, `Saga.cs:2207-2304`)

**Bundled fix, the start time.** `Init` restores `_seasonStart` with a bare
`DateTime.TryParse` (`Saga.cs:2225`). A `...Z` string parsed that way comes back as local
time, and `SaveMeta`/`SeasonJson` then re-stamp that local value with a `Z`
(`Saga.cs:2291`). On a host at UTC+2 `started_at` drifts two hours on every mid-season
boot. Fix: parse with `CultureInfo.InvariantCulture` and
`DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal`.

**Remembered last season.** `SeasonSystem` holds only `_currentSeason` and `_seasonStart`
(`Saga.cs:2209-2210`); `SaveMeta` emits `last_ended`/`last_ended_at` only on the ending call
(`Saga.cs:2295-2297`), so the next `StartSeason` erases them and a restart never reads them
back. Add `_lastEnded`/`_lastEndedAt` statics, two more regex parses in `Init`, and re-emit
the pair on every `SaveMeta` write.

**Why a separate baseline file.** `PlayerRegistry.ToJson` (`PlayerRegistry.cs:380-430`) is
the single serializer for both the per-player disk file (`PlayerRegistry.cs:364`) and
`/api/state`'s `players{}` (`PlayerRegistry.cs:444`), so a field added to `PlayerRecord`
lands in the contract. And `seasons.json` is read with regexes (`Saga.cs:2222,2224`) and
overwritten wholesale (`Saga.cs:2298`); it is not a place for a data store. So:
`BepInEx/config/TheRavensCall/season_baseline.json`, compact, one line:

```json
{"season":"Ashen Dawn","taken_at":"2026-09-18T00:00:00Z","rows":[
 {"name":"Ragnvald","kills":764,"deaths":11,"boss_kills":6,"playtime_seconds":512000}]}
```

An **array of rows, not name-keyed maps**, because the existing map parser splits each pair
on the first colon (`PlayerRegistry.cs:330-333`), which breaks a name like `Od:in`, the
case this repo already fixed once for file names (`PlayerRegistry.cs:130-143`), and
`ExtractJsonField` takes the first `"<key>":` anywhere in the document (`Companion.cs:336-338`),
so a player named `kills` would be found before the real key. Rows are read with
`Companion.JsonGetArray(raw, "rows")` and, per element, `JsonGetString`/`JsonGetInt`/
`JsonGetLong` (`Companion.cs:721-857`), the same helpers `death_history` reloads with. A
`name` is written through `Companion.Esc`, so no unescaped `"kills":` can occur inside it.
Playtime is a `long`. In memory: one `Dictionary<string, Baseline>` keyed by name,
`OrdinalIgnoreCase`, beside `_currentSeason`.

- **`StartSeason`** (`Saga.cs:2232-2245`): after `_seasonStart = DateTime.UtcNow`
  (`Saga.cs:2237`), snapshot every `PlayerRegistry.All` record (`PlayerRegistry.cs:105`),
  write the file, then the existing `SaveMeta()` and `Narrate`.
- **`EndSeason`** (`Saga.cs:2247-2262`): clear the map and delete the file after
  `SaveMeta(ended)`.
- **`Init`** (`Saga.cs:2216-2230`): everything new goes inside the existing `try`, after the
  "Active season loaded" log line, guarded on `!string.IsNullOrEmpty(_currentSeason)` (the
  early `return` at `Saga.cs:2220` when `seasons.json` is missing means no season, which is
  right). Load the baseline. If it is missing (a 1.4.0 upgrade in the middle of a season),
  snapshot **now**, write it, and log one line saying standings count from this moment;
  `standings_since` carries that timestamp. This is the honest choice: the alternative, no
  baseline, reports every player's lifetime total as their season score. Then §4.6's
  Chronicle re-point.
- A player with no baseline row reads 0 for every counter, which is correct: they joined
  after the season started, so their whole total accrued inside it.
- `SeasonJson()` is one `internal static` accessor after `GetCurrentSeasonName()`
  (`Saga.cs:2214`), reusing the private `EscJ` (`Saga.cs:2303`) for strings and
  `Companion.Esc` for names. Deltas are `Math.Max(0, current - baseline)`.

### 4.5 Bundled fix: the raid patch hooks the wrong method

`Patch_Raid` (`Saga.cs:1468-1477`) is a postfix on `RandEventSystem.SetRandomEventByName`
and is the only thing that sets `WorldState.RaidActive`/`RaidType` (`PlayerRegistry.cs:93-94`),
which `/api/state` exports as `raid_active`/`raid_type` (`PlayerRegistry.cs:440-441`) and
the page shows as the raid banner. On a dedicated server that method is reached only by
world load restoring a saved active event (`RandEventSystem.Load` → `SetRandomEventByName`,
decompile line 713). The `event <name>` console command calls it too (`Terminal.cs:986-1000`)
but dereferences `Player.m_localPlayer.transform.position` (`Terminal.cs:998`), which is
null on a headless server, so it throws before the patch runs; and `RPC_SetEvent`
(decompile 252-258) is client-only behind `!ZNet.instance.IsServer()`. Natural raids start
through `StartRandomEvent()` and the standalone-interval branch of `UpdateRandomEvent`
(decompile 335, 162), and end through `SetRandomEvent(null, ...)` from `FixedUpdate` (88)
or `ResetRandomEvent()` (363, the `stopevent` command). All of those go through the private
`SetRandomEvent(RandomEvent ev, Vector3 pos)` (371), and none through the hooked method. So
the flag never flips for a real raid and, once set by a world load, never clears.

**Fix:** **replace** the patch (not add a second one) with a postfix on
`SetRandomEvent` (`[HarmonyPatch(typeof(RandEventSystem), "SetRandomEvent")]`, the only
method of that name). The postfix reads `string now = ev?.m_name ?? ""`, keeps
`string before = WorldState.RaidType`, and when they differ: assigns both `WorldState`
fields **unconditionally**, then, only if `EnableRaid` (§6), calls `Plugin.Narrate` with
`raid_start` (message "A raid begins: <display name>!", detail = `now`) or `raid_end`
("The raid has ended.", detail = `before`). `EnableRaid` gates the narration only; the
banner and `raid_active` must work regardless. A small display map for the vanilla raid
names, raw name as fallback, lives beside `NarrativeSystem`. `Narrate`, not `FireEvent`:
Chronicle and feed, no Discord post, the same treatment as join/leave.

**Ordering at boot.** `RandEventSystem.Load` runs during the world load inside `ZNet.Awake`,
before `Patch_ZNetAwake.Postfix` initialises the Chronicle and the feed (`Saga.cs:328`). A
raid restored from the save therefore narrates before either exists: `Chronicle.Write` finds
a null writer (harmless, `_writer?.WriteLine`), and the row lands in the feed before
`EventFeed.Init` loads the file, which is why `Load` merges (§4.3). If `EnvMan` is not up
yet, `BuildContextData` (`Saga.cs:194-204`) throws inside `Narrate`'s own `try/catch` and
that one row is lost; the flag is still set. Acceptable, and documented.

### 4.6 Bundled fix: the Chronicle forgets the season folder on restart, and leaks a writer

`Patch_ZNetAwake.Postfix` calls `Chronicle.Init()` with no argument (`Saga.cs:328`) and then
`SeasonSystem.Init()` (`Saga.cs:332`), which restores `_currentSeason` but never re-points
the Chronicle at `Chronicle/seasons/<name>/`, the folder `StartSeason` set up
(`Saga.cs:2238-2239`). Every Chronicle line after a mid-season restart lands in the default
folder. Separately, `Chronicle.Init` assigns a new `StreamWriter` without closing the old one
(`Saga.cs:2013`); only the day rotation inside `Write` closes first (`Saga.cs:2027`), so
`StartSeason` (`Saga.cs:2239`) and `EndSeason` (`Saga.cs:2256`) already leak one open handle
each. **Fix:** `Chronicle.Init` closes `_writer` before reopening, which fixes all three call
sites; then, at the end of `SeasonSystem.Init` when a season is active, call
`Chronicle.Init(archiveDir)` with the same path expression `StartSeason` uses.

### 4.7 `Companion.cs`: cache, builder, prime, route, gate

- `internal static volatile string _activityCache;` and an `EmptyActivityJson` constant
  beside `_stateCache`/`EmptyStateJson` (`Companion.cs:40,45`): `{"generated_at":"",
  "season":{"active":false,"name":"","started_at":null,"standings_since":null,
  "last_ended":null,"last_ended_at":null,"standings":[]},"events":[]}`.
- `BuildActivityCache()` after `PrimeStateCache` (`Companion.cs:303-319`): same guard,
  concatenates `generated_at` + `SeasonSystem.SeasonJson()` + `EventFeed.ToJsonArray()`,
  own `try/catch`, `LogError` on failure.
- **Tick:** in `PollAllPlayers`, after the `BarrkBOT_data1.json` write (`Companion.cs:466`),
  `EventFeed.Save()` if dirty; then, inside the existing
  `if (Instance != null && Plugin.EnableHttpServer.Value)` block (`Companion.cs:474-477`),
  after `_stateCache = barrkBotJson;`, one `BuildActivityCache()` call. A fault in either can
  never disturb the `/api/state` assignment.
- **Boot prime:** `Companion.BuildActivityCache()` as the last call in
  `Patch_ZNetAwake.Postfix`, after the `startup` Chronicle line (`Saga.cs:339-340`). Not inside
  `PrimeStateCache`: that runs at `Saga.cs:330`, two lines before `SeasonSystem.Init()` at
  `Saga.cs:332`, and would serve `season.active=false` on every mid-season restart until the
  first tick.
- **Route:** `else if (path == "/api/activity")` after the `/api/pins` branch
  (`Companion.cs:201-205`), body `_activityCache ?? EmptyActivityJson`, `application/json`.
  Cached string or fixed envelope only, mirroring `/api/state` (`Companion.cs:181-188`).
- **Gate:** add `"/api/activity"` to the path list at `Companion.cs:327`; update the
  `HttpApiToken` description (`Saga.cs:121`), the startup log line (`Companion.cs:114`) and
  the Token paragraph in `docs/API.md:7-9`, which all enumerate the gated paths.

### 4.8 Escaping

`Chronicle.EscapeJson` (`Saga.cs:2062-2066`) escapes backslash, quote, `\n` and `\r` only.
A tab or other control character in a `lore.txt` line produces a Chronicle line that is not
valid JSON, which today only spoils that log line but would break `JSON.parse` of the whole
feed. Make `EscapeJson` delegate to `Companion.Esc` (`Companion.cs:387-406`), which escapes
`\t` and strips the rest. The log gets the same hardening.

### 4.9 Version

`Saga.cs:36` `PluginVersion = "1.3.0"` → `"1.4.0"`. It propagates at compile time to the
`[BepInPlugin]` attribute (`Saga.cs:31`), the startup log (`Saga.cs:126`),
`Companion.VERSION` (`Companion.cs:20`) and `/api/health`, which interpolates `VERSION`
(`Companion.cs:199`).

---

## 5. Compatibility

| Page | Server | Result |
|---|---|---|
| 1.4.0 (embedded) | 1.4.0 | Everything. |
| 1.4.0 | 1.3.0 | One `/api/activity` request returns 404 (visible in the browser's network panel); both panels stay hidden; roster and detail views unchanged, they only read `/api/state`. No banner, no retry. |
| 1.3.0 disk override (`BepInEx/config/TheRavensCall/theravenscall.html`) | 1.4.0 | Served ahead of the embedded page as today (`Companion.cs:206-229`); works, minus the panels; the stale-page warning does not fire because the 1.3 page carries the marker substring. |
| any | 1.4.0 with `EventFeedCapacity = 0` | 200 with `"events":[]`; the feed panel shows its empty line; the season panel works. |
| BarrkBOT | 1.4.0 | `/api/state` and `BarrkBOT_data1.json` keep the exact 1.3.0 shape and field set. One **value** change: `raid_active`/`raid_type` now flip on real raids (§4.5), with `raid_type` carrying the raw vanilla event names (`army_eikthyr`, `foresttrolls`, ...). Noted in `BARRKBOT_CONTRACT.md`. |
| WhereTheCrowFlies 1.1.x | 1.4.0 | Unaffected. Nothing on the RPC wire changes. |

---

## 6. Config

Additive only; no existing default changes.

| Section | Key | Default | Description |
|---|---|---|---|
| `Companion` | `EventFeedCapacity` | `200` | Number of recent Chronicle lines kept in memory and served by /api/activity, newest first (0 to 1000). They are saved to event_feed.json so the feed survives a restart. 0 turns the feed off; the endpoint still answers, with an empty events array, so a 1.4.0 dashboard can tell "turned off" from "older server". |
| `Events` | `EnableRaid` | `true` | Record raid start and end in the Chronicle and the dashboard feed (raid_start / raid_end). Not posted to Discord. The raid banner and raid_active work either way. |

Also corrected: the `HttpApiToken` description (`Saga.cs:121`) gains `/api/activity`. The
`AppendSeasonNameToMessages` description (`Saga.cs:107`) says "Prepend", which is what the
code does; leave the key name, admins already have it.

---

## 7. Versioning and docs

| File | Change |
|---|---|
| `Saga.cs:36` | `1.4.0` (§4.9) |
| `theravenscall.html:6` | marker `1.4` |
| `HexiumDist/manifest.json:3` | `"version_number": "1.4.0"` |
| `HexiumDist/README.md:10` | version badge; a Dashboard section describing the overview, feed and season panels; the raid line now true; the stale 1.3.0 claims (gear tier tracking, the "smoke" cause, death items lost) corrected in the same edit |
| `HexiumDist/CHANGELOG.md` | new `## 🟢 [1.4.0]` section above 1.3.0: additive, non-breaking; the three bundled fixes called out as fixes |
| `docs/API.md` | `/api/activity` row and shape section; the Token paragraph (`docs/API.md:7-9`) gains the route; `/api/health` example `1.4.0` (`docs/API.md:16`); one line reaffirming `/api/state` unchanged; the `raid_active`/`raid_type` note updated (they now track real raids) |
| `docs/DASHBOARD.md` | overview, feed and season panel sections; "Known gaps" (`docs/DASHBOARD.md:212`) updated; the fixture preview gains `api/activity` |
| `docs/fixtures/api-activity.json` | new fixture beside `api-state-1.3.json` |
| `BARRKBOT_CONTRACT.md` | two lines: `/api/activity` exists and is not part of the contract; `raid_active`/`raid_type` now carry real raid names |
| `HANDOFF.md` | status line at cut time |
| `HexiumDist/TheRavensCall-v1.4.0.zip` | same five entries as 1.3.0; no loose html, the page is embedded (`TheRavensCall.csproj:42`) |

---

## 8. Risks

1. **`ApiTokenOk` is a hand-maintained list.** A route added to `ProcessRequest` but not to
   `Companion.cs:327` serves player names to anyone who can reach the port even with a token
   set. Test steps 10-11.
2. **Click delegation fails silently** if the second listener is skipped. Step 6.
3. **The feed file is written on the same tick as the export.** One more atomic write per
   10 s while events are flowing. It is small and skipped when clean.
4. **Season standings use the server-credited counters**, which count only what reaches the
   server: crow-reported kills plus what the server itself sees. A player without
   WhereTheCrowFlies scores few kills. Vanilla `EnemyKills` was rejected for deltas because it
   is empty until the crow reports (`docs/API.md:65`); a player installing the crow mid-season
   would swap sources under the baseline and get a nonsense delta. The page's existing
   "server-observed" tag applies.
5. **Mid-season upgrade** starts standings from the upgrade moment, not the season start.
   Reported honestly via `standings_since` and a log line (§4.4).
6. **Re-targeting the raid patch** touches a private method Valheim may rename. Harmony logs
   a patch failure at load without affecting the rest of the mod; the raid rows and banner
   would go quiet, which is the state they are in today.
7. **A raid restored at world load** may not reach the feed if narration runs before
   `EnvMan` exists (§4.5). The flag and banner are unaffected.
8. **A stale disk-override page** keeps shadowing the embedded one (`Companion.cs:206-229`),
   from the config folder or the plugin folder (`Companion.cs:216-218`). Pre-existing; it is
   a precondition for testing (step 1).

---

## 9. Test plan (Storm10, one admin client)

Conventions as in `docs/TESTPLAN-storm10-2026-09-15.md`: server at
`C:\Users\donfr\ValheimServers\Storm10`, page at `http://localhost:2112`, config under
`Storm10\BepInEx\config\TheRavensCall\`, log `Storm10\BepInEx\LogOutput.log`. Start with
PowerShell `Start-Process`, never a backgrounded Bash call. **The admin client runs
WhereTheCrowFlies 1.1.3**: on a dedicated server `player_death` and the kill counters only
move through the client's reports (`Saga.cs:744-761`, `Saga.cs:677`), so steps 8 and 12
need it.

1. Build 1.4.0 Release, stage the DLL. Delete any `theravenscall.html` under the config
   folder **and** the plugin folder. **Pass:** no override present.
2. Start. **Pass:** `TheRavensCall 1.4.0 awakens (server-only).`, `Server systems
   initialized.`, no exception in the `ZNet.Awake` postfix; `/api/health` says `1.4.0`.
3. Before anyone joins, `curl /api/activity`. **Pass:** 200, `season.active=false`,
   `standings=[]`, `events` holds the `startup` row (and, on a second boot, the previous
   run's rows and `shutdown` row above it).
4. Open the page. **Pass:** feed shows the startup row; season panel says none running;
   the all-time board renders from the roster; no console errors.
5. Join. **Pass:** a `player_join` row within 10 s whose `message` equals the Chronicle
   line's `message` for the same timestamp (no prefix, no `[Day N]`).
6. Click a player name in the feed. **Pass:** the detail view opens and the panels hide;
   Back shows them again. A `startup` row's `SERVER` is plain text.
7. Raids. On the server console: `devcommands` (both raid commands are cheat-gated,
   `Terminal.cs:245`), `setkey defeated_eikthyr` (the Eikthyr army needs it), then with the
   client standing at a base of at least three base-value pieces (a workbench and a few
   walls) type `randomevent`. **Pass:** a `raid_start` row within 10 s with the raid name in
   `detail`; `/api/state` shows `raid_active=true` and the name; the header banner appears.
   Then `stopevent`. **Pass:** `raid_end` row with the same name in `detail`;
   `raid_active=false`; banner gone. Do not use `event <name>`: on a dedicated server it
   throws before reaching the game (§4.5).
8. Kill a creature by hand, die once, walk into a new biome. **Pass:** each lands within
   one poll, newest first, right badge, right `detail`.
9. Put a tab in a `lore.txt` line with `LoreBroadcastIntervalMinutes = 1`. **Pass:** the
   `lore` row arrives and the feed still parses (§4.8).
10. Set `HttpApiToken`, restart, `curl /api/activity` without a token. **Pass:** 401
    `{"error":"token required"}`; the page shows the "Needs the API token" line, not empty
    panels.
11. Same with `?token=`. **Pass:** 200; the page's settings token unlocks the panels.
    Clear the token.
12. `ravenscall season start TestSeason`. **Pass:** season panel shows `TestSeason` active
    within 10 s; every standings row hidden (all zero); a `season_start` row;
    `season_baseline.json` exists; the log names `Chronicle\seasons\TestSeason\`.
13. One kill, one death. **Pass:** that player's row appears with 1 and 1, strictly less than
    the same player's `kills_observed_lifetime` in `/api/state` if they had prior kills.
14. Restart mid-season. **Pass:** immediately after boot, before the first tick,
    `season.active=true`, `started_at` unchanged from before the restart (§4.4's UTC fix),
    `standings_since` equal to it, kills and deaths unchanged, playtime higher by the last
    session (it accrues at leave); the feed retains the pre-restart rows under the new
    `startup` row; the Chronicle log line names `Chronicle\seasons\TestSeason\` and no
    second default-folder file is created (§4.6).
15. `ravenscall season end`. **Pass:** `active=false`, `last_ended="TestSeason"`, a
    `season_end` row, `season_baseline.json` gone, the Chronicle back in the default folder.
    Restart. **Pass:** `last_ended` still `TestSeason` (§4.4, remembered).
16. Start a season, delete `season_baseline.json`, restart. **Pass:** one log line about
    counting from now; `standings_since` later than `started_at`; the page shows the
    "counted since" note. End the season.
17. `EventFeedCapacity = 5`, restart, fire 6+ events. **Pass:** never more than 5, oldest
    evicted. Restore 200.
18. `EventFeedCapacity = 0`, restart. **Pass:** 200 with `"events":[]`; feed panel shows
    its empty line; season panel unaffected; `event_feed.json` deleted. Restore 200,
    restart. **Pass:** the feed starts from the `startup` row only.
19. Drop the 1.3.0 page into the config folder, reload. **Pass:** roster and tabs work; no
    request to `/api/activity`; no stale-page warning (expected, §5). Delete it.
20. 1.4.0 page against the static fixture set with `api/activity` temporarily removed.
    **Pass:** panels absent; exactly one failed `/api/activity` request in devtools; roster
    normal. Restore the fixture. **Pass:** panels render from it.
21. Change the base URL in Settings to a wrong port, then back. **Pass:** the panels return
    (the `reconnect()` reset, §3.1).
22. 375 px wide. **Pass:** panels stack inside the content column with the same side
    gutter as the roster, no horizontal scroll, the gear button sits beside the logo, a name
    containing `&` or `'` renders as text.

---

## 10. Rejected alternatives

- **Seeding the feed from the Chronicle log files at boot.** `Chronicle` has no read API
  (`Write`, `WriteRaw`, `Close` only); the current file depends on the UTC date and on
  whether a season is active, and until §4.6 lands it is the wrong file after a mid-season
  restart; and `EnableChronicleLog = false` would leave nothing to seed from. A dedicated
  file is smaller than the code to do that safely.
- **Two routes, `/api/events` and `/api/season`.** Same tick, same data, twice the plumbing
  (§1). Split only when a consumer that wants one without the other exists.
- **A server-side ranked top-N leaderboard with a `LeaderboardSize` key.** The page ranks
  by any column; a top-N by kills is the wrong set for a top-N by playtime. The registry is
  tens of players, so sending every row costs nothing, and the all-time board does not need
  the server at all.
- **Rebuilding the served string on every event.** An O(capacity) join on the main thread
  inside the combat path, for freshness a 10 s poller cannot observe.
- **A per-row sequence number.** The page highlights rows newer than the previous poll's
  newest timestamp; a missed highlight on two events in the same second is the whole cost.
- **Name-keyed baseline maps parsed with `ParseStringIntMap`.** Breaks on a colon in a name
  and on a player named like a key (§4.4).
- **Panels as siblings of `<main>`.** They would render full-bleed with no side gutter on
  every width; wrapping `main` costs nothing (§1, decision 7).
- **Raids through `FireEvent`.** That posts to Discord; raids are frequent and
  join/leave-grade news. `Narrate` is the join/leave path.

---

## 11. Decisions for the owner

Defaults in bold; the scope above assumes them.

1. Raids in the feed, with the patch re-targeted: **yes**.
2. Feed persisted across restarts in `event_feed.json`: **yes**.
3. One route: **yes**.
4. `EventFeedCapacity` default: **200**, clamp 1000.
5. Bundle the Chronicle and season-time fixes (§4.4, §4.6): **yes**.
6. Tier 1 and tier 2 in one 1.4.0 cut, as two PRs (page first, server second): **yes**.
7. The `ContainsApiMarker` version check (make it compare the value so a 1.3 override on a
   1.4 server gets its own warning): **no, follow-up**. A behaviour change to working code
   outside this footprint.

---

## 12. Follow-ups, not 1.4.0

- `HandleConsumables` (`Saga.cs:1381-1392`) discards the food, stamina and eitr values the
  crow sends; a "what people eat" panel would need them kept.
- `gear_tier` only fires from a dormant path; a client report would make it real.
- Discord posts for raids, if wanted, are one `FireEvent` swap behind `EnableRaid`.
- The `ContainsApiMarker` value check (decision 7).
- The `event <name>` console command cannot work on a dedicated server (§4.5); a
  `ravenscall raid <name>` console command that calls `SetRandomEvent` with a player's
  position would give admins a way to start a chosen raid.
