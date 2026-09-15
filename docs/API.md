# TheRavensCall HTTP API — the 1.3.0 contract

Server: `Companion.cs` (`StartHttpServer` / `ProcessRequest`). Listens on
`http://localhost:<HttpServerPort>` (default 2112). Localhost only unless
`HttpBindAllInterfaces = true` (config section `[Companion]`, Saga.cs ~line 117-121).

Token: when `HttpApiToken` is set, `/api/state` and `/api/gamedata` (and `/api/pins`)
require `?token=<value>` or an `X-Api-Token: <value>` header (`ApiTokenOk`). `/api/health`
and the page stay open. Ordinal string compare.

## Endpoints

| Route | Body |
|---|---|
| `GET /`, `/index`, `*.html` | the dashboard page, `text/html; charset=utf-8`. Disk override first: `BepInEx/config/TheRavensCall/theravenscall.html`, then the plugin folder; **1.3.0 adds the copy embedded in the DLL as the final fallback** (page PR). |
| `GET /api/health` | `{"status":"ok","version":"1.3.0"}` |
| `GET /api/state` | **1.3.0: exactly the BarrkBOT export** (the same string written to `BepInEx/config/TheRavensCall/BarrkBOT_data1.json`), shape below. Built on the main thread every `StatsPushIntervalSeconds` (10 s) in `PollAllPlayers`, unconditionally (the file needs it either way); primed once at boot after `PlayerRegistry.LoadAll` (`PrimeStateCache`). The worker thread only ever hands out the cached string; before the first prime it serves `{}`. |
| `GET /api/gamedata` | the contents of `BarrkBOT_data2.json`: `{"recipes":[...],"items":[...],"buildables":[...]}`, written once at boot by `WriteGameData` when `ObjectDB` is ready. `recipes[]` = `{slug, recipe_key, name, category, amount, station, ingredients:[{slug,name,qty}]}`; `items[]` = `{slug, name, category}`; `buildables[]` = `{name, category, ingredients:[{slug,name,qty}]}` (one entry per piece across every `ItemDrop`'s `m_buildPieces`; no `slug` on the buildable itself, only on its ingredients). Empty arrays until written. |
| `GET /api/pins` | `MapPinTracker.GetJson()`: always `[]` on a dedicated server (no minimap). Kept, unused by the page. |
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

Every row is `PlayerRegistry.ToJson(rec)` (PlayerRegistry.cs ~line 380), nothing else.

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
| `biomes_discovered` | string[] | `Heightmap.Biome` names |
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
| `vanilla_stats` | `{ "<PlayerStatType>": float }` | Valheim's own Stats-screen counters, keys = `PlayerStatType` enum names (`EnemyKills`, `Deaths`, `EnemyHits`, `PlayerKills`, `Jumps`, `ArrowsShot`, `PortalsUsed`, `DistanceTraveled`, ... ~205 in Valheim 1.0). Replaced wholesale on every crow StatSync. **Empty until the player has run WhereTheCrowFlies.** |
| `skill_levels` | `{ "<SkillType>": float 0-100 }` | empty until the crow reports; a missing skill = never raised, not zero |
| `skill_progress` | `{ "<SkillType>": float 0-1 }` | progress to the next level |
| `builds_placed`, `builds_removed`, `builds_repaired` | int | crow V2 counters |
| `items_crafted`, `items_upgraded`, `items_repaired` | int | crow V2 counters |
| `resources_harvested` | `{ "<resource>": int }` | keyed by whatever string WhereTheCrowFlies reports — the pickable's `GetHoverName()` (client-localized display name, e.g. `Raspberry`, `Mushroom`), or the cleaned prefab name if that's empty; capped to 64 chars server-side (`Saga.cs` `HandleHarvesting`/`CreditHarvest`), not renormalized here |
| `consumables_eaten`, `bosses_summoned`, `guardian_powers_used` | int | crow V2 counters |

### Consumer rules (the page follows these; BarrkBOT already does, see BARRKBOT_CONTRACT.md)

1. **Lifetime kills / deaths**: prefer `vanilla_stats.EnemyKills` / `vanilla_stats.Deaths` when present; fall back to `kills_observed_lifetime` / `deaths_lifetime` only while the player has never synced, and label that fallback as "server-observed".
2. **Absent means unknown, not zero.** Empty `vanilla_stats` / `skill_levels` = the player has not run the crow. Say so; do not print 0.
3. `online` + `online_count` are the live markers. There are no live vitals on a dedicated server.
4. Sort rosters: online first, then `last_seen` descending.

## Removed in 1.3.0 (breaking; the bundled page was the only consumer)

- The legacy aliases `bosses{}`, `combat{session_kills,damage_dealt,damage_taken,raid_active,raid_type}`, `total_kills`, `total_deaths` (the alias block that used to sit at the bottom of `AppendRegistryFields`, plus `CanonicalBossOrder`). `raid_active` / `raid_type` move to the top-level envelope, where they were always actually tracked (`WorldState`, a world flag, not a per-player one).
- The whole live-row builder `BuildStateJson` (health, max_health, stamina, eitr, comfort, weight, guardian, status_effects, position, skills[], inventory, food, chests, boats, timers, known_recipes, known_materials, weather, tamed) and `BuildOfflineStateJson` (`player_name`, `world_name`, `biome`, `updated_at`), and `BuildStateArray` + `AppendRegistryFields`. These ran only when `Player.GetAllPlayers()` had entries, which is never on a dedicated server (see the comment in `PollAllPlayers`); `/api/state` was always the offline row + registry fields there.
- The old array-of-rows top level. `/api/state` is now the envelope above.

Kept (not removed by this change, but now dead — zero callers left anywhere in the codebase once `BuildStateJson` is gone): `ChestTracker.GetJson`, `TimerTracker.GetJson`, `BoatTracker.GetJson`. Deleting them is a separate, owner-decided cleanup. Still alive and in active use: `MapPinTracker` (`/api/pins`), `RecipeDumper` (`/api/gamedata`), `BossKeys.Resolve` (`Saga.cs` boss credit), `FriendlyCreatureName` (`death_history.killer` text), `GetBiome(Player)` / `GetBiomeAt(Vector3)`, `Loc`, `B`, `F`, `Esc`, `JsonGetString`/`JsonGetStringArray`/`JsonGetInt`/`JsonGetLong`/`JsonGetArray`/`ExtractJsonField` (all read back by `PlayerRegistry.cs` when loading a player file from disk).
