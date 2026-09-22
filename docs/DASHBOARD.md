# The dashboard (`theravenscall.html`)

Rebuilt from scratch for the 1.3.0 API contract (`/api/state` as an envelope
with a `players` map, no more live-vitals fields that a dedicated server can
never populate). One file, no build step, no network request except to the
configured API host (this page's own origin by default, or the base-URL
override — see below). Roughly 76 KB as of 1.4.0 (tier 2 added the feed and
season panels and `/api/activity`'s own poll; still no build step, no new
dependency).

## What survived, what didn't

Six tabs, all keyed off fields a dedicated server can actually observe:
**Combat, Death, Progression, Crafting, Build, Raw**.

Everything else from the old page is gone, because a dedicated server has no
API to see any of it: **Map, Boats, Tamed, Fishing (the food list — not the
`caught_fish` tag list, which stays), Food, Timers, the inventory/vitals
widgets, and the setup wizard.** The old page also pulled three Google Fonts
families (`Bebas Neue`, `Inter`, `JetBrains Mono`) over the network and
pointed its feedback link at
`github.com/NomadicWar/SteveCompanion` (a stale fork name); both are gone —
system font stack only, feedback goes to
`github.com/RavenIron-Games/TheRavensCall/issues`.

## Layout

- **Header**: world name, day, online count, a pulsing raid banner when
  `raid_active` (with a friendly name for common `raid_type` values, falling
  back to a prettified raw string), a connection dot + "updated Ns ago" /
  "last update Ns ago", and the settings gear. At the 400px phone breakpoint
  the gear sits on the same row as the logo (`order: 0`) and the world
  meta/separator/connection group wraps to its own row below (`order: 1`),
  instead of the gear itself wrapping onto a third row.
- **Activity feed, season & world** (tier 2, new in 1.4.0; the third panel
  added in 1.5.0): `#worldPanels`, a `seasonPanel`, a `feedPanel` and (since
  1.5.0) a `censusPanel`, sitting inside a `<main>` wrapper alongside `#app`
  (not inside it — see below), so it renders above the world overview and
  roster. The season/feed pair is hidden until `/api/activity` first
  answers, `censusPanel` is hidden until `/api/census` first answers (its
  own latch — see the World panel section), and all three hide again
  whenever the detail view is open. Described in their own sections below.
- **World overview** (landing view, above the roster): four cards, described
  below. Only rendered once at least one player is known — an empty world
  still shows the plain "No players have ever joined" state instead.
- **Roster** (landing view): one card per player from `players`
  (`Object.values`), sorted online-first then `last_seen` descending (rule
  4). Each card shows name, online dot, `active_title`, lifetime kills/deaths
  (rule 1, with a "server-observed" tag when falling back), playtime, last
  seen.
- **Player detail**: six tabs, described below. Selecting a player and a tab
  is kept in memory (not the URL) across the 10 s poll.
- **Footer**: the two "how to reach this server" paragraphs now live inside a
  `<details>` headed "Connection help", closed by default. The footer is
  static markup outside `#app` — `render()` never touches it — so a plain
  `<details>` keeps its own open/closed state with no `data-details-key`
  needed. The feedback link stays outside the details, always visible.

## World overview

Four cards, rendered inside `renderRoster()`'s own output (so they refresh
with the roster on every poll and never need their own poll or render
trigger), in a two-column grid on desktop and one column at the phone
breakpoint. Every player name in these cards is a `data-select` link into
that player's detail view, same as a roster card. Everything here is
computed client-side from the same `/api/state` payload the roster already
has — no server change, no new endpoint (that's tier 2's `/api/activity`).

- **Online now**: the players with `online: true` (`sortedPlayers()` already
  puts them first, sorted by `last_seen` descending — since they're all
  online, that ends up ordering by how recently each one's status last
  ticked), each showing session length as `humanizeSeconds(now -
  session_start)`. When nobody is online, one line names the most-recently-
  seen player instead ("Nobody online. Last seen: `<name>` `<relative
  time>`.").
- **All time board**: one table, every known player, columns Player / Kills /
  Deaths / Boss kills / Playtime, plus a row number in an unlabeled first
  column. Kills and deaths reuse `killsValue`/`deathsValue` (rule 1; a player whose
  figures fall back gets one "server-observed" tag after their name); boss kills is
  `boss_kills_credited`; playtime is `humanizeSeconds(playtime_seconds_lifetime)`.
  Column headers (Player, Kills, Deaths, Boss kills, Playtime — not the rank
  column) are clickable: `state.boardSort = {key, dir}` drives the sort and
  survives re-renders (it's state, not read back from the DOM); clicking the
  active column flips its direction, clicking a different one switches to it
  at `dir: 'desc'` — except the Player column, which switches to it at
  `dir: 'asc'` (A→Z reads naturally on a name column; the four numeric
  columns still open biggest-first). Default on load is kills descending.
  The underlying sort
  (`sortBoardRows`) and row-building (`boardRows`) — along with `bossUnion`
  and `mergedDeathHistory` below — are all pure functions that take the raw
  `players` map and return plain arrays, so they can be unit-tested directly
  against the fixture with no DOM.
- **Bosses defeated**: the union of every known player's `bosses_defeated`
  across the same seven `BOSS_LIST` keys and `.strip`/`.strip-item.boss`
  markup the Combat tab's own boss-progress strip uses, so a boss lights up
  here the moment *any* player has it, not just the one you're looking at.
- **Latest deaths**: every player's `death_history` merged into one list
  (`mergedDeathHistory`, a pure function), sorted newest-first by timestamp,
  the ten newest rows. Columns are relative time (absolute time on hover,
  same as the Death tab), the player as a link, killer, and biome — no
  location column here (the per-player Death tab still has that). "No deaths
  recorded yet" when the merge is empty. **Tier 2 source switch:** once
  `/api/activity`'s feed has at least one `player_death` row
  (`feedPlayerDeathRows`), this card switches to that instead — a true
  cross-player chronological source, since it comes from one ordered feed
  rather than ten independently-truncated per-player lists. The feed's rows
  carry no biome (only `timestamp_utc`, `event_type`, `player_name`,
  `message`, `detail` — `/api/activity`'s events have no location data at
  all), so the Biome column reads "—" from this source; killer comes from
  the event's `detail` field. Falls back to the tier 1 merge above whenever
  the feed has zero death rows (a fresh 1.4.0 upgrade before anyone has
  died, or `EventFeedCapacity = 0`), so this card is never empty just
  because the feed is.

## Activity feed and season (tier 2, new in 1.4.0)

`<main id="app">` became a `<main>` wrapper holding `<div id="worldPanels"
hidden><div id="seasonPanel"></div><div id="feedPanel"></div></div>` beside
`<div id="app"></div>`. `#app` kept its id, so the existing roster/detail
swap (`render()`) is unchanged; `#worldPanels` is deliberately *outside* that
swap, with its own poll (`pollActivity()`), its own render
(`renderActivity()`) and its own trigger, the same pattern `renderHeader()`
already used in tier 1. Reasons this matters:

- `pollState()` skips `render()` whenever `/api/state` is byte-identical
  (apart from `generated_at`) — panels living inside `#app` would refresh
  only when the roster happened to change, and an `innerHTML` swap on every
  roster tick would blow away the feed's scroll position and any open chip
  selection even when nothing about the feed itself changed.
- The two panels need to hide while the player detail view is open, and
  reappear on Back — independent of whatever `#app` is currently showing.

**Feature detection.** `#worldPanels` starts `hidden` in the markup and stays
that way until `/api/activity` answers with a definite yes (200 or 401 — a
401 still proves the route exists, just not to this client yet) or a
definite no (404, a pre-1.4.0 server — `state.activitySupported` latches to
`false` and `pollActivity()` stops calling the route at all for the rest of
the page's life, so a pre-1.4.0 server produces exactly one failed request,
not one every 10s). Any other failure (timeout, a non-404/401 HTTP error,
network) is treated as transient: the panels keep showing the last known
good data and the next poll tries again. `stripGeneratedAt` (the same
regex `pollState` uses) skips a re-render when a 200 response is otherwise
unchanged. `pollActivity()` polls independently of `pollState()` on its own
`setInterval(pollActivity, POLL_MS)` (same 10s cadence) and never touches
`connStatus`, `lastErrorMsg` or the once-only 401 settings auto-open — those
stay `/api/state`'s alone. Saving Settings (`saveSettings()`) resets
`activitySupported` to `null` and `activityAuth` to `true` and calls
`pollActivity()` immediately, so pointing the base URL at a different (or
newer) server re-probes the route instead of staying latched to the old
server's verdict.

**Feed panel.** The newest 30 rows of `/api/activity`'s `events[]` (already
newest-first from the server) after filtering by chip group, each row:
relative time (absolute on hover), an event-type badge, the escaped
`message`, and the player as a link into the detail view — but only when
`player_name` is a key in `state.stateData.players`; the two world-scoped
values the server sends (`"world"`, `"SERVER"`) and any name the roster
doesn't recognise render as plain text instead, since `render()` bounces an
unknown `state.selectedPlayer` selection back to the roster. Filter chips
group the 17 documented `event_type` values (`EVENT_TYPE_GROUP`) into four
buckets — **Combat** (`player_death`, `boss_kill`, `raid_start`, `raid_end`,
`kill_milestone`, `death_milestone`), **Players** (`player_join`,
`player_leave`, `title_earned`, `biome_discovery`, `gear_tier`), **World**
(`world_event`, `lore`, `startup`, `shutdown`), **Season** (`season_start`,
`season_end`) — plus **All**; the page's own grouping, not part of the
server contract. The selected chip lives in `state.feedFilter`, not the DOM,
so it survives a re-render. A row whose `timestamp_utc` is strictly newer
than the previous poll's newest event gets a brief highlight
(`isNewFeedEvent`/`.feed-row-new`); nothing highlights on the very first
load, since there's no "previous poll" yet. "No events yet" on an empty
feed (a fresh install, or `EventFeedCapacity = 0`); since 1.4.1, "Nothing in <chip> yet."
instead when the feed has rows but the selected chip has none.

**Season panel.** `season.active === true`: the season name, a green
`ACTIVE` tag, "started today" / "started yesterday" / "started N days ago" (`startedText(calendarDaysAgo(...))`, calendar days in the viewer's local time, 1.4.1), and a standings table headed
**This season** — deliberately not "All time", so the two boards never share
a heading. Ranked by kills with deaths, boss kills and playtime as sortable
columns, reusing tier 1's exact `sortBoardRows` and click-to-sort mechanics
(`state.seasonSort` instead of `state.boardSort` — its own key, so sorting
one board never disturbs the other) after normalizing the server's
snake_case standings fields to the board's shape (`seasonStandingsRows`).
Rows where kills, deaths, boss kills and playtime are all zero are hidden
(`nonZeroSeasonRows`) — a player who's done nothing yet doesn't clutter a
season that just started. A "counted since `<date>`" note appears only when
`standings_since` trails `started_at` by more than a minute
(`standingsSinceNote`), the mid-season-upgrade case (`SCOPE-1.4.0.md` §4.4).
`season.active === false`: "No season is running" plus a last-ended line
(`seasonLastEndedLine`) naming `season.last_ended` when the server remembers
one, empty otherwise.

**Click delegation.** The click handler that opens a player's detail view
was bound only to `#app`, so a link inside `#worldPanels` — a different
element — was inert to it. That body is now `selectPlayer(name)`, called
from `#app`'s listener as before *and* from a second listener bound to
`#worldPanels`, which also owns the season table's sort-header clicks
(`state.seasonSort`) and the feed's filter-chip clicks (`state.feedFilter`).

**CSS.** New rules only: the badge classes (`.evt-badge` plus one
`.g-<group>` colour modifier per chip group), the chip row (`.chip-row`,
`.chip`, `.chip.active`), the feed row list and its highlight keyframe, and
`.tag.green` (the season's `ACTIVE` tag — `.tag.gold` already existed).
Everything else reuses `.card`, `.notice`, `.table-wrap` and the `:root`
tokens; no new `@media` rule and no new layout rule, since the panels sit
inside the same `<main>` the roster already does.

**Marker.** 1.4.0 shipped `<meta name="theravenscall-api" content="1.4">` —
cosmetic; `ContainsApiMarker` only checks the substring is present, never the
value (a follow-up per `SCOPE-1.4.0.md` §11.7). 1.5.0 bumps it to `content="1.5"`;
see the World panel section below.

## World panel (1.5.0)

A third panel inside `#worldPanels`, after the feed panel, fed by the new
`/api/census` endpoint (`docs/API.md`) — the count of portals, beds, wards,
ships, carts, chests and crafting stations standing in the world right now,
who placed them, and directories of portals, beds and wards.

**Layout**, top to bottom:

1. A **group strip**: seven small cards, one per group ("12 portals", "9
   beds", ...), each showing `player_built / total` underneath when the two
   differ.
2. A **Builders** table, sortable the same way the other boards are
   (`state.censusSort`, the same keyboard-accessible sortable headers):
   Player, Portals, Beds, Wards, Ships, Carts, Chests, Stations, Pieces. A
   known name links into the detail view through the existing `data-select`
   delegation; an unknown builder renders as "Unknown (id …)" in plain text,
   not a link.
3. A **portal directory** table: Tag, Kind, Builder, Connected (a tag badge,
   "linked" or "no pair"), Position ("x, z"). An empty tag renders as "(no
   tag)".
4. **Beds** and **Wards**, each as its own closed-by-default `<details>`
   block holding a compact table.
5. A footer line: "Counted N objects in M ms, T ago; runs every K minutes."

**Polling.** `/api/census` on load, then every **60 seconds** on its own
`setInterval` — unlike the feed and season panels, which share `/api/state`'s
10 s `POLL_MS` tick (`setInterval(pollActivity, POLL_MS)`); a full census is
far more expensive than the activity envelope, so it gets its own, slower
timer. A change gate compares the payload with `generated_at` stripped, the
same pattern `pollActivity` already uses, so an unchanged census doesn't
force a re-render.

**Outcomes:**

- **404** — a pre-1.5.0 server. The World panel alone hides; polling for it
  stops for the rest of the page's life, the same latch `pollActivity` uses
  for `/api/activity` against a pre-1.4.0 server. The season and feed panels
  are unaffected.
- **401** — the same "Needs the API token (Settings)." notice the other
  panels show.
- **`enabled: false`** — the panel shows one line: "The census is off
  (`CensusIntervalMinutes = 0`)."
- **The empty pre-first-run payload** (`enabled: true`, every count and list
  empty, `generated_at: ""`) — the panel shows one line: "First count runs a
  few seconds after boot."

**Detail view.** The Build tab gains one line built from that player's row in
`/api/census`'s `builders[]`: "Standing in the world: 412 pieces · 3 portals
· 1 bed · 2 wards · 1 ship · 14 chests · 3 stations" — the row's own order,
zero-count groups omitted; `pieces` is every object that player has standing,
so the group figures after it are parts of it, not additions to it. A player
with no row on the census reads "Nothing counted yet."; when the census is
off or has not run yet, the tab shows the same line the World panel does.
When the
vanilla `PortalsPlaced` stat is present on that player, it shows on the same
tab as "Portals placed, ever: 14" — a different, larger number than the
census's live portal count, since it includes portals since torn down; the
two are labelled so they're never mistaken for each other.

**Marker.** `<meta name="theravenscall-api" content="1.5">` — the same
cosmetic substring check as before (`ContainsApiMarker`), so nothing else
about the disk-override warning changes.

Names, tags and positions on this panel are escaped through `esc()` at every
render, the same as everywhere else on the page. At the 375px breakpoint its
tables scroll inside their own card, the same as the "All time" board.

## The token / settings flow

Gear icon opens a panel with two fields, both persisted in
`localStorage` (`trc_base_url`, `trc_token`) and never sent anywhere except
as the `X-Api-Token` request header on whatever host the base-URL override
names (blank = this page's own origin):

- **Base URL override** — only needed when the file is opened directly
  (`file://...`) instead of served by the mod; blank means "this page's own
  origin".
- **API token** — matches the server's `HttpApiToken` config value, if set.

A 401 response from `/api/state` opens the settings panel automatically with
a one-line explanation, exactly per the contract — but only the *first*
time: a flag (`settingsAutoOpened`) suppresses repeat auto-opens until the
next successful poll or a save, and `openSettings()` itself only seeds the
two inputs from stored state when the panel is going from closed to open. A
server that stays unreachable used to reopen the panel (and blow away a
half-typed token) on every failed 10 s poll; now it opens once and leaves
whatever's in the fields alone. Saving re-polls immediately.

## Polling

`/api/state` is polled every 10 s (`AbortSignal.timeout(8000)` per request),
matching `StatsPushIntervalSeconds`. `generated_at` changes on every tick
regardless of whether anything else did, so a byte-identical comparison
against the raw response never actually skips a re-render against a live
server. The page instead strips `generated_at` (`stripGeneratedAt()`, a
regex on the raw text) before comparing, and only re-renders the tab body
when the rest of the payload actually changed — otherwise it just refreshes
the header's "updated Ns ago" text. Because a real re-render can still
happen at any tick, `render()` separately remembers which `<details
data-details-key="...">` disclosures (currently just the "N more counters"
one on Combat) are open before replacing `#app`'s innerHTML, and reopens the
matching ones afterward — so an open disclosure survives a genuine data
change, not just a quiet tick. `/api/gamedata` is fetched once at load,
best-effort — the page is fully usable without it.

On any fetch failure (timeout, network error, non-401 HTTP error), the last
known good data stays on screen, the connection dot turns red, and an inline
banner explains what happened. The page never goes blank once it has loaded
data once, and shows a plain "Connecting…" message before the first
successful poll.

## Tab-by-tab

- **Combat**: rule-1 kills/deaths, boss progress strip (7 canonical keys),
  `creature_kills` table (own display-name map for the ~30 keys
  `Saga.cs`'s `CreatureKeyMap` produces — `SeekerBrute` and `SeekerSoldier`
  each get their own label — falling back to a prettified raw key — see
  VERIFY note below), then **unconditionally** the this-session block
  (`session_kills`, damage dealt/taken/blocked, blocks, parries) — these are
  populated directly by each combat/damage event as the server credits it,
  not by the periodic `vanilla_stats` StatSnapshot, so they must not wait on
  `vanilla_stats` before showing. Only once `vanilla_stats` is non-empty:
  boss activity (`bosses_summoned`, `guardian_powers_used`, genuinely crow
  V2-only) and vanilla stats (a curated "highlighted" set plus the rest
  under a disclosure; a numeric-string key the server's enum can't name,
  e.g. `"3968"`, is dropped rather than rendered, per the contract). When
  `vanilla_stats` is empty, those two crow-only sections are replaced by one
  notice; the boss strip, `creature_kills` table and this-session block
  still show regardless.
- **Death**: rule-1 death total, `deaths_lifetime` shown alongside as
  "Server-observed deaths" only when it differs from the rule-1 figure
  above (with an explanation that it's the server's own directly-observed
  count, a fallback only). It used to show `deaths_narrative` labeled
  "Server-narrated deaths" with a Discord/Chronicle explanation —
  `deaths_narrative` and `deaths_lifetime` are incremented on the same
  lines in `Saga.cs` (`HandlePlayerDeath` and `CreditPlayerDeath`), so
  they're always equal and the narration claim was simply wrong. Then
  `death_history` newest first (relative + absolute timestamp, killer,
  biome, rounded x/z), and an explicit "last 10 only" note.
- **Progression**: title + earned titles, biome strip (the nine
  `BiomeAndGearTracking.NormalizeBiome` spellings — `Meadows`, `BlackForest`,
  `Swamp`, `Mountain`, `Plains`, `Ocean`, `Mistlands`, `Ashlands`,
  `DeepNorth` — lit/unlit), skills (sorted by level, a numeric-string key
  the server's enum can't name is dropped; bars from `skill_progress`,
  read as a fraction when ≤1 and as an already-computed percent when >1,
  per the contract; replaced by the crow notice when `skill_levels` is
  empty — a missing skill reads as "never raised", never as 0), caught fish
  as tags (`caught_fish` values are lowercased localisation tokens like
  `$item_fish1`; resolved against `/api/gamedata`'s `items[].slug`
  case-insensitively to show `items[].name`, falling back to a prettified
  token with the `$item_` prefix stripped when gamedata has no match), first
  seen, playtime, and `gear_tier`'s stored value alongside a permanent "not
  reported" note explaining the dedicated server has no API to read a
  player's equipped inventory at all.
- **Crafting**: item counters, `resources_harvested` table sorted by amount
  — the whole tab becomes the crow notice when the player has never synced,
  since every field on it comes from a crow report.
- **Build**: `builds_placed`/`removed`/`repaired` plus a net-pieces figure;
  same whole-tab notice when never synced.
- **Raw**: the player's row pretty-printed (`JSON.stringify(p, null, 2)`)
  with a copy button (Clipboard API with an `execCommand` fallback).

## VERIFY findings (read against the code, not assumed)

- **`creature_kills` keys** come from `Saga.cs`'s `CreatureKeyMap`
  (`OnCreatureKill`), a *different* map from `Companion.cs`'s
  `FriendlyCreatureName` (which only formats `death_history` killer
  strings). Keys are grouped species names like `Boar`, `Draugr`,
  `GoblinBrute`, `SeekerBrute`, falling back to the raw prefab name for
  anything unmapped. The page ships its own small display map for these
  specific keys (e.g. `GoblinBrute` → "Fuling Berserker") and falls back to
  a generic camel-case-to-spaced-words prettifier for anything else — this
  is the page's own construction, not read from the server, and will drift
  silently if `CreatureKeyMap` changes.
- **`resources_harvested` keys are already human-readable**, not prefab
  slugs. Traced through `Saga.cs`'s `CombatCredit.CreditHarvest` back to
  `WhereTheCrowFlies/Patches/CombatReporter.cs`'s `Patch_Pickable`: the
  reported name is `Pickable.GetHoverName()` (the localized display name,
  e.g. "Wood", "Raspberries"), falling back to a cleaned prefab name only if
  the hover name is empty. The page does **not** need to resolve these
  against `/api/gamedata`; it does so anyway, opportunistically, matching
  each key case-insensitively against `gamedata.items[].name` to show a
  category badge when it lines up.
- **`buildables[]` in `/api/gamedata` has no `slug` field.** `WriteGameData`
  in `Companion.cs` builds each entry as `{name, category, ingredients:
  [{slug, name, qty}]}` — unlike `items[]` and `recipes[].ingredients[]`,
  the buildable itself carries no slug, only its ingredients do. The page
  does not read `buildables[]` today (nothing in the six tabs calls for it),
  but a future consumer should not assume the same shape as `items[]`.

## Disk override order (unchanged, `Companion.cs` `ProcessRequest`)

1. `BepInEx/config/TheRavensCall/theravenscall.html` (`OutputDir`)
2. The plugin folder next to `TheRavensCall.dll`
3. **New in 1.3.0**: the copy embedded in the DLL
   (`Assembly.GetExecutingAssembly().GetManifestResourceStream("theravenscall.html")`),
   via `Companion.ReadEmbeddedHtml()`. Only reached when neither disk
   location has the file. The stale 404 text (which still said
   `stevecompanion.html`) now says `theravenscall.html` and only appears if
   even the embedded resource read fails.

`TheRavensCall.csproj` adds
`<EmbeddedResource Include="theravenscall.html" LogicalName="theravenscall.html" />`
so the file travels inside `TheRavensCall.dll` regardless of what the store
zip includes.

**Upgrade hazard:** every pre-1.3.0 install that ever had a dashboard got it
by an admin hand-placing `theravenscall.html` at one of the two disk
locations above — the store zip never shipped the loose file. That old file
still shadows the new bundled page after an upgrade, and it cannot read the
1.3.0 `/api/state` shape, so its dashboard is silently dead forever. The
1.3.0 page carries `<meta name="theravenscall-api" content="1.3">` in its
`<head>`; when `ProcessRequest` serves a disk copy without that string in
its bytes, it logs one warning per server run (a static
`_warnedStaleDiskDashboard` bool) naming the path and telling the admin to
delete it so the bundled 1.3.0 page loads instead. The disk-override
contract itself is unchanged — the disk copy is still served, just with a
warning now.

## Previewing without a live server

1. Copy `theravenscall.html` to a scratch folder as `index.html`.
2. Copy `docs/fixtures/api-state-1.3.json` to `<folder>/api/state` (no
   extension), `docs/fixtures/api-gamedata.json` to `<folder>/api/gamedata`,
   and `docs/fixtures/api-activity.json` to `<folder>/api/activity`.
3. `python -m http.server 8765` (or `python3`) from that folder, or any
   static file server that serves extensionless files as-is.
4. Open `http://localhost:8765`. The fixture has three players: **Ragnvald**
   (online, full crow data — vanilla stats, skills, 3 deaths, 5 bosses, an
   `Ashlands` biome, a `Cooking` skill with `skill_progress` above 1 to
   exercise the percent-not-fraction path, numeric-string keys in
   `vanilla_stats`/`skill_levels` to exercise the hide-unnamed-counters
   path, and `caught_fish` tokens that partly resolve against the gamedata
   fixture and partly fall back to a prettified token), **Bjorn** (offline,
   has crow data from a past session, one resolvable `caught_fish` token),
   and **Sigrun** (online, has never run WhereTheCrowFlies — exercises the
   rule-2 notices on Combat/Progression/Crafting/Build, but still shows a
   populated this-session block on Combat since that doesn't need the crow).
   `docs/fixtures/api-activity.json` uses the same three names: an active
   "Ashen Dawn" season (Bjorn's standings row is all-zero, to exercise the
   hide-zero-rows rule) and ~25 events spanning all four feed chip groups,
   including a `startup`/`shutdown` pair (a simulated restart) and a
   `raid_start`/`raid_end` pair.

Exercising the feed/season panels' degrade paths needs a request that
returns something other than the static file above, which a plain
`http.server` can't do per-path:

- **404** (pre-1.4.0 server): skip step 2's `api/activity` copy entirely — a
  missing file 404s on its own, and the panels should stay hidden with
  exactly one failed request in the browser's network panel, never a retry.
- **401** (token required, not supplied): needs a small custom handler that
  returns HTTP 401 with `{"error":"token required"}` for `GET /api/activity`
  and falls back to serving the static files above for everything else — the
  panels should show only the one-line "Needs the API token (Settings)"
  notice, nothing else.

A real deployment needs no `api/` folder at all — `/api/state`,
`/api/gamedata` and `/api/activity` are served live by the mod.

## Known gaps

- Fixed 10 s poll only; no manual refresh button (matches
  `StatsPushIntervalSeconds`, but there's no way to force an early check).
- No offline caching between page loads (a hard refresh always shows
  "Connecting…" until the first poll lands); only within-session state is
  preserved.
- The "All time" board's column sort (`state.boardSort`) is in-memory only,
  same as the selected player/tab — it resets to kills descending on a full
  page reload, it isn't written to `localStorage`.
- Relative times on the landing view ("last seen", "7d ago" in Latest deaths, the
  session lengths) only advance when `/api/state` actually changes, because an
  unchanged payload skips the re-render. On an idle server with nobody online
  they can sit still between real events; the header's "updated" text still ticks.
- **Tier 2:** the season standings sort (`state.seasonSort`) and the feed's
  selected chip (`state.feedFilter`) are in-memory only, same as
  `state.boardSort` above — both reset on a full page reload.
- **Tier 2:** the "new row" feed highlight compares timestamps, not a
  per-row id (`SCOPE-1.4.0.md` §10 rejects a sequence number) — two events
  landing in the same second could in principle both miss the highlight;
  accepted as the whole cost of that tradeoff.
