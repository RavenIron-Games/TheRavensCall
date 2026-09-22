# TheRavensCall HTTP API — the 1.4.0 contract

Server: `Companion.cs` (`StartHttpServer` / `ProcessRequest`). Listens on
`http://localhost:<HttpServerPort>` (default 2112). Localhost only unless
`HttpBindAllInterfaces = true` (config section `[Companion]`, Saga.cs ~line 117-121).

Token: when `HttpApiToken` is set, `/api/state`, `/api/gamedata`, `/api/activity` and
`/api/census` (and `/api/pins`) require `?token=<value>` or an `X-Api-Token: <value>` header
(`ApiTokenOk`). `/api/health` and the page stay open. Ordinal string compare.

## Endpoints

| Route | Body |
|---|---|
| `GET /`, `/index`, `*.html` | the dashboard page, `text/html; charset=utf-8`. Disk override first: `BepInEx/config/TheRavensCall/theravenscall.html`, then the plugin folder, then the copy embedded in the DLL (`Companion.ReadEmbeddedHtml()`), then 404 only if that embedded read itself fails. A served disk copy that predates 1.3.0 (missing the `<meta name="theravenscall-api">` marker) logs one warning per server run telling the admin to delete it. |
| `GET /api/health` | `{"status":"ok","version":"1.5.0"}` (the plugin version) |
| `GET /api/state` | **Unchanged since 1.3.0: exactly the BarrkBOT export** (the same string written to `BepInEx/config/TheRavensCall/BarrkBOT_data1.json`), shape below. Built on the main thread every `StatsPushIntervalSeconds` (10 s) in `PollAllPlayers`, unconditionally (the file needs it either way); primed once at boot after `PlayerRegistry.LoadAll` (`PrimeStateCache`); before that prime (and if it ever throws) the body is the same envelope with `players: {}`, `online_count: 0` and an empty `generated_at`, so the shape never changes over the endpoint's lifetime. The worker thread only ever hands out the cached string. |
| `GET /api/gamedata` | the contents of `BarrkBOT_data2.json`: `{"items":[...],"recipes":[...],"buildables":[...],"updated_at":"<ISO-8601 UTC>"}`, written once at boot by `WriteGameData` when `ObjectDB` is ready. Until that write has happened the body is the literal `{"recipes":[],"items":[],"buildables":[]}` with no `updated_at`. `recipes[]` = `{slug, recipe_key, name, category, amount, station, ingredients:[{slug,name,qty}]}`; `items[]` = `{slug, name, category}`; `buildables[]` = `{name, category, ingredients:[{slug,name,qty}]}` (one entry per piece across every `ItemDrop`'s `m_buildPieces`; no `slug` on the buildable itself, only on its ingredients). Empty arrays until written. |
| `GET /api/pins` | `MapPinTracker.GetJson()`: always `[]` on a dedicated server (no minimap). Kept, unused by the page. |
| `GET /api/activity` | **New in 1.4.0.** The dashboard's recent-events feed and the active season's standings, shape below. Built on the main thread each poll tick alongside `/api/state`; primed once at boot. Never 404s on a 1.4.0 server — `EventFeedCapacity = 0` still answers 200 with `"events":[]`, so a 404 means the server predates 1.4.0. Token-gated the same as `/api/state`. |
| `GET /api/census` | **New in 1.5.0.** The world census — how many portals, beds, wards, ships, carts, chests and crafting stations stand in the world, per-player totals, and directories of portals/beds/wards, shape below. Runs on the main thread on its own schedule (`[Census] CensusIntervalMinutes`, default 5, 0 = off). Never 404s: before the first run it answers the empty envelope (`enabled: true`, empty groups and lists); with the interval at `0` it answers `enabled: false`, same shape. Token-gated the same as `/api/state`. |
| `OPTIONS *` | 204. `favicon.ico` 204. Unknown route 404. |

## `/api/state` shape (1.3.0)

```json
{
  "generated_at": "2026-09-15T12:00:00.0000000Z",   // ISO-8601 UTC ("o")
  "world_name": "Stormhold",
  "day": 143,
  "online_count": 2,
  "raid_active": false,
  "raid_type": "",
  "players": {
    "<player name>": { ...PlayerRecord... },          // map keyed by name, ordered by name (case-insensitive)
    ...
  }
}
```

Every row is `PlayerRegistry.ToJson(rec)` (PlayerRegistry.cs ~line 380), nothing else. The table below is that method, field by field; `BARRKBOT_CONTRACT.md` describes the same rows from the bot's side and stays the authority on the export FILE.

### PlayerRecord row

| Field | Type | Meaning |
|---|---|---|
| `name` | string | player name (the map key repeats it) |
| `first_seen`, `last_seen` | ISO-8601 UTC strings | `last_seen` is refreshed every poll tick while online |
| `online` | bool | the real online marker on a dedicated server |
| `session_start` | ISO string or `null` | null when offline |
| `playtime_seconds_lifetime` | int | |
| `kills_narrative`, `deaths_narrative` | int | the Saga narrative counters (server-attributed) |
| `gear_tier` | int | **always 0 on a dedicated server** (needs a client report that does not exist yet, HANDOFF.md) |
| `active_title` | string | |
| `titles_earned` | string[] | |
| `biomes_discovered` | string[] | not `Heightmap.Biome` names — the nine `BiomeAndGearTracking.NormalizeBiome` spellings (`Meadows`, `BlackForest`, `Swamp`, `Mountain`, `Plains`, `Ocean`, `Mistlands`, `Ashlands`, `DeepNorth`); anything that method doesn't recognise passes through raw |
| `boss_kills_credited` | int | |
| `creature_kills` | `{ "<key>": int }` | keyed by Saga.cs's own `CreatureKeyMap` short name (`OnCreatureKill`: exact prefab match, then prefix, then substring), **not** `Companion.FriendlyCreatureName` (that map is only used for `death_history.killer` text) — falls back to the raw prefab name when nothing in `CreatureKeyMap` matches |
| `session_kills` | int | |
| `damage_dealt_session`, `damage_taken_session`, `damage_blocked_session` | float (F1) | |
| `blocks_session`, `parries_session` | int | |
| `kills_observed_lifetime` | int | **what the server itself saw; undercounts badly** (a dedicated server owns no zone with a player in it) |
| `deaths_lifetime` | int | server-observed |
| `bosses_defeated` | string[] | canonical short keys: `eikthyr elder bonemass moder yagluth queen fader` (`BossKeys`) |
| `caught_fish` | string[] | |
| `death_history` | object[] | newest first, max 10. Entry: `{timestamp, killer, location:{x,y,z}, biome, items:[]}` (`items` is empty on the RPC path, the only path that runs on a dedicated server) |
| `vanilla_stats` | `{ "<PlayerStatType>": float }` | Valheim's own Stats-screen counters, keys = `PlayerStatType` enum names (`EnemyKills`, `Deaths`, `EnemyHits`, `PlayerKills`, `Jumps`, `ArrowsShot`, `PortalsUsed`, `DistanceTraveled`, ... ~205 in Valheim 1.0). Per-key absolute overwrite on every crow StatSnapshot (event 11, the client's `FullSyncIntervalSeconds`, default 5 min); incremented in between by StatSync deltas (event 10, every 10 s). The map is merged, never cleared: a counter absent from a snapshot keeps its last value. Values are clamped to ±1e9 server-side. A stat id the server's own enum cannot name arrives as its number as a string (e.g. `"3968"`: a newer client or a modded stat); consumers drop such keys, as BarrkBOT does (BARRKBOT_CONTRACT.md). **Empty until the player has run WhereTheCrowFlies.** |
| `skill_levels` | `{ "<SkillType>": float }` | vanilla range 0-100; the server only clamps to 0..1000 (tolerant of skill-cap mods), so survive values above 100. Empty until the crow reports; a missing skill = never raised, not zero. Numeric-string keys are possible for the same reason as `vanilla_stats`: drop them. |
| `skill_progress` | `{ "<SkillType>": float }` | progress to the next level; vanilla sends a 0-1 fraction, the server clamps only to 0..100, so treat anything above 1 as a percent |
| `builds_placed`, `builds_removed`, `builds_repaired` | int | crow V2 counters |
| `items_crafted`, `items_upgraded`, `items_repaired` | int | crow V2 counters |
| `resources_harvested` | `{ "<resource>": int }` | keyed by whatever string WhereTheCrowFlies reports — the pickable's `GetHoverName()` (client-localized display name, e.g. `Raspberry`, `Mushroom`), or the cleaned prefab name if that's empty; capped to 64 chars server-side (`Saga.cs` `HandleHarvesting`/`CreditHarvest`), not renormalized here |
| `consumables_eaten`, `bosses_summoned`, `guardian_powers_used` | int | crow V2 counters |

### Consumer rules (the page follows these; BarrkBOT already does, see BARRKBOT_CONTRACT.md)

1. **Lifetime kills / deaths**: prefer `vanilla_stats.EnemyKills` / `vanilla_stats.Deaths` when present; fall back to `kills_observed_lifetime` / `deaths_lifetime` only while the player has never synced, and label that fallback as "server-observed".
2. **Absent means unknown, not zero.** Empty `vanilla_stats` / `skill_levels` = the player has not run the crow. Say so; do not print 0.
3. `online` + `online_count` are the live markers. There are no live vitals on a dedicated server.
4. Sort rosters: online first, then `last_seen` descending.

## `/api/activity` shape (1.4.0)

`/api/state` is unchanged from 1.3.0 — same shape, same fields, still byte-for-byte the
BarrkBOT export; everything below is new and lives on its own route.

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
| `generated_at` | string | `DateTime.UtcNow.ToString("o")`, the format `/api/state` uses, so the page's `stripGeneratedAt` works unchanged. Empty string before the boot prime. |
| `season.active` | bool | `!string.IsNullOrEmpty(_currentSeason)`. |
| `season.name` | string | `""` when inactive. Never absent. |
| `season.started_at` | string or null | `yyyy-MM-ddTHH:mm:ssZ`, UTC. `null` when inactive. |
| `season.standings_since` | string or null | When the baseline was taken. Equal to `started_at` except after a mid-season upgrade. `null` when inactive. |
| `season.last_ended`, `season.last_ended_at` | string or null | The single most recently ended season, remembered across restarts (not just until the next season starts). |
| `season.standings[]` | array | `[]` when no season is active. One row per known player, **unsorted**: the page ranks. Each counter is that player's lifetime counter minus the baseline taken at season start, clamped at 0. Rows whose counters are all zero are included; the page hides them. |
| `standings[].kills` | int | `TotalKillsLifetime` delta. |
| `standings[].deaths` | int | `TotalDeathsLifetime` delta. |
| `standings[].boss_kills` | int | `BossKillsCredited` delta. |
| `standings[].playtime_seconds` | long | `PlaytimeSecondsLifetime` delta. Accrues on leave, so an online player's current session isn't in it. |
| `events[]` | array | Newest first, at most `EventFeedCapacity` entries, `[]` when the feed is off or nothing has happened. Each element is a Chronicle line verbatim. |
| `events[].event_type` | string | 16 types in practice: `player_join`, `player_leave`, `player_death`, `boss_kill`, `biome_discovery`, `kill_milestone`, `death_milestone`, `title_earned`, `season_start`, `season_end`, `world_event`, `lore`, `startup`, `shutdown`, `raid_start`, `raid_end`. (`gear_tier` exists in the schema but only fires from a dormant path, so it never appears on a dedicated server.) |
| `events[].player_name` | string | `"world"` or `"SERVER"` for world-scoped rows. |
| `events[].message` | string | The raw narrative message, exactly what Discord and the Chronicle get — no emoji prefix, no season prefix, no `[Day N]`, no `(N online)`; `day` and `online_count` are their own typed fields on the row instead. |
| `events[].detail` | string | The event's detail as the Chronicle records it: the death cause, the boss prefab, the milestone count, the title, the biome key, the raid name, the `player_leave` duration text. `""` when none (`world_event`, `lore`, `startup`, `shutdown`). |

**Token gate:** yes, the same as `/api/state` (see the Token paragraph above). Player names
appear in every standings row and most event rows.

**Size:** a Chronicle line is 150-300 bytes in practice. 200 entries ≈ 50 KB typical; the
per-line cap and the capacity clamp bound the worst case at 2 MB. Standings ≈ 100 bytes per
known player. Smaller than `/api/state` on any real roster at the defaults.

**Never 404s while the server is 1.4.0.** `EventFeedCapacity = 0` turns the feed off, but the
route still answers 200 with `"events":[]`. A 404 is the page's only signal that the server
predates 1.4.0; "turned off" and "too old" stay distinguishable.

**Not a BarrkBOT contract.** The BarrkBOT contract is the export *file*
`BepInEx/config/TheRavensCall/BarrkBOT_data1.json` (`BARRKBOT_CONTRACT.md`); `/api/state` is
frozen to it because it serves that byte-for-byte string. `/api/activity` is the dashboard's
own endpoint; a future consumer is welcome to it, but its shape is documented here as
1.4.0's, not promised.

## `/api/census` shape (1.5.0)

Also not a BarrkBOT contract, for the same reason `/api/activity` isn't: it is not written to
disk, and `/api/state` and the export file are unchanged (`BARRKBOT_CONTRACT.md`).

```json
{
  "generated_at": "2026-09-22T14:05:00.1234567Z",
  "enabled": true,
  "interval_minutes": 5,
  "scanned_objects": 812345,
  "duration_ms": 41,
  "groups": {
    "portals":  {"total": 12, "player_built": 12},
    "beds":     {"total": 9,  "player_built": 8},
    "wards":    {"total": 2,  "player_built": 2},
    "ships":    {"total": 3,  "player_built": 3},
    "carts":    {"total": 1,  "player_built": 1},
    "chests":   {"total": 40, "player_built": 40},
    "stations": {"total": 7,  "player_built": 7}
  },
  "builders": [
    {"name": "Nomadtest", "id": 123456789, "portals": 3, "beds": 1, "wards": 0,
     "ships": 1, "carts": 0, "chests": 14, "stations": 3, "pieces": 412}
  ],
  "unknown_builders": 1,
  "portals": [
    {"tag": "home", "kind": "wood", "builder": "Nomadtest", "builder_id": 123456789,
     "connected": true, "x": 123, "z": -456}
  ],
  "beds":  [ {"owner": "Nomadtest", "builder": "Nomadtest", "builder_id": 123456789, "x": 120, "z": -450} ],
  "wards": [ {"builder": "Nomadtest", "builder_id": 123456789, "enabled": true, "x": 118, "z": -448} ]
}
```

| Field | Meaning |
|---|---|
| `generated_at` | UTC timestamp of the census this payload reports, ISO-8601 round-trip (`"o"`, e.g. `2026-09-22T14:05:00.1234567Z`) — the same format `/api/state` uses. Empty string before the first run has completed. |
| `enabled` | `false` when `CensusIntervalMinutes = 0` (the census is off); `true` otherwise, including before the first run. |
| `interval_minutes` | The effective interval in minutes: `CensusIntervalMinutes` clamped to 1..1440, or `0` when the census is off. |
| `scanned_objects` | How many ZDOs the walk examined to build this census. |
| `duration_ms` | How long that walk took, in milliseconds. |
| `groups` | One entry per group (`portals`, `beds`, `wards`, `ships`, `carts`, `chests`, `stations`); `total` counts every object classified into that group, `player_built` counts only those with a non-zero creator. |
| `builders[]` | One row per distinct non-zero creator seen on any counted object, named or not; `name` is `null` when the server doesn't yet know that creator's name (a `builder_id` with no matching entry in `player_ids.json`). Each of `portals`, `beds`, `wards`, `ships`, `carts`, `chests`, `stations` on a row is that group's count for this builder; `pieces` is every object of *any kind* this builder placed and still stands in the world — the seven group counts on the same row are subsets of it (a builder with 4 portals and nothing else reads `portals: 4, pieces: 4`), and on a real base it is normally far larger than all of them together. |
| `unknown_builders` | Count of distinct non-zero creators seen with no known name — the rows in `builders[]` whose `name` is `null`. |
| `portals[]` | The portal directory: `tag` (the player-set text, `""` when unset), `kind` (`"wood"` or `"stone"`, or the raw prefab name for a modded portal), `builder`/`builder_id` (`builder` is `null` when unknown), `connected` (whether the portal has a linked pair), `x`/`z` (position, rounded to integers, no `y`). |
| `beds[]` | The bed directory. `owner` — the player who has currently claimed the bed as their spawn point (`null` when unclaimed). `builder`/`builder_id` — who placed the bed (`null` when the server doesn't know that id's name yet). These are two different fields about two different players: whoever last slept in a bed is not necessarily who built it, and a row can show one without the other. Also `x`/`z`. |
| `wards[]` | The ward directory: `builder`/`builder_id` (`builder` is `null` when unknown), `enabled` (whether the ward is currently active), and `x`/`z`. `builder` is read from the ward's own stored creator name and can be present even when that `builder_id` has no matching entry in `builders[]`. |

**Sorting and caps:** `builders[]` is sorted by `pieces` descending, then by name. `portals[]` is
sorted by `tag` (ordinal, case-insensitive) then by builder; `beds[]` and `wards[]` are sorted by
builder then `x`. Each of `portals[]`, `beds[]` and `wards[]` is capped at 500 rows; when a cap
trims a list, the payload adds a matching `"<group>_truncated": true` key next to it (e.g.
`"portals_truncated": true`) and the server logs it once. All strings — tags, names — go through
`Companion.Esc`, the same escaper as the rest of the API; they are user text.

**Never 404s.** Before the first run completes, the route answers the empty envelope: `enabled`
true, every group's `total`/`player_built` at 0, `builders`/`portals`/`beds`/`wards` all empty
arrays, `unknown_builders` 0, `generated_at` `""`. With `CensusIntervalMinutes = 0` it answers the
same shape with `enabled: false` instead — so, like `/api/activity`, a 404 is the only signal a
server predates 1.5.0; "off" and "too old" stay distinguishable.

**Token gate:** yes, the same as `/api/state` (see the Token paragraph above). Builder names and
positions appear throughout.

## Removed in 1.3.0 (breaking; the bundled page was the only consumer)

- The legacy aliases `bosses{}`, `combat{session_kills,damage_dealt,damage_taken,raid_active,raid_type}`, `total_kills`, `total_deaths` (the alias block that used to sit at the bottom of `AppendRegistryFields`, plus `CanonicalBossOrder`). `raid_active` / `raid_type` move to the top-level envelope, where they were always actually tracked (`WorldState`, a world flag, not a per-player one). Since 1.4.0 they also track *real* raids, not only one restored from a saved world at boot (a pre-existing bug, fixed as part of the 1.4.0 activity feed); `raid_type` carries the raw vanilla event name (`army_eikthyr`, `foresttrolls`, ...), not a friendly label.
- The whole live-row builder `BuildStateJson` (health, max_health, stamina, eitr, comfort, weight, guardian, status_effects, position, skills[], inventory, food, chests, boats, timers, known_recipes, known_materials, weather, tamed) and `BuildOfflineStateJson` (`player_name`, `world_name`, `biome`, `updated_at`), and `BuildStateArray` + `AppendRegistryFields`. Of these only `BuildStateJson` needed a `Player`, which `Player.GetAllPlayers()` never yields on a dedicated server, so every row there was `BuildOfflineStateJson` + `AppendRegistryFields`: the registry fields plus four duplicates (`player_name`, `world_name`, `biome` always `"Unknown"`, `updated_at`).
- The old array-of-rows top level. `/api/state` is now the envelope above.

Kept (not removed by this change, but now dead — zero callers left anywhere in the codebase once `BuildStateJson` is gone): `ChestTracker.GetJson`, `TimerTracker.GetJson`, `BoatTracker.GetJson`. Deleting them is a separate, owner-decided cleanup. Still alive and in active use: `MapPinTracker` (`/api/pins`), `RecipeDumper` (`/api/gamedata`), `BossKeys.Resolve` (`Saga.cs` boss credit), `FriendlyCreatureName` (`death_history.killer` text), `GetBiome(Player)` / `GetBiomeAt(Vector3)`, `Loc`, `B`, `F`, `Esc`, `JsonGetString`/`JsonGetStringArray`/`JsonGetInt`/`JsonGetLong`/`JsonGetArray`/`ExtractJsonField` (all read back by `PlayerRegistry.cs` when loading a player file from disk).
