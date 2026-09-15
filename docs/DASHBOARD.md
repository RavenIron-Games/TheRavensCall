# The dashboard (`theravenscall.html`)

Rebuilt from scratch for the 1.3.0 API contract (`/api/state` as an envelope
with a `players` map, no more live-vitals fields that a dedicated server can
never populate). One file, no build step, no network request except to the
configured API host (this page's own origin by default, or the base-URL
override — see below). Roughly 50 KB.

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
  "last update Ns ago", and the settings gear.
- **Roster** (landing view): one card per player from `players`
  (`Object.values`), sorted online-first then `last_seen` descending (rule
  4). Each card shows name, online dot, `active_title`, lifetime kills/deaths
  (rule 1, with a "server-observed" tag when falling back), playtime, last
  seen.
- **Player detail**: six tabs, described below. Selecting a player and a tab
  is kept in memory (not the URL) across the 10 s poll.

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
   extension) and `docs/fixtures/api-gamedata.json` to
   `<folder>/api/gamedata`.
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

A real deployment needs no `api/` folder at all — `/api/state` and
`/api/gamedata` are served live by the mod.

## Known gaps

- Fixed 10 s poll only; no manual refresh button (matches
  `StatsPushIntervalSeconds`, but there's no way to force an early check).
- No offline caching between page loads (a hard refresh always shows
  "Connecting…" until the first poll lands); only within-session state is
  preserved.
