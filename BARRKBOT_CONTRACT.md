# The BarrkBOT contract — read before renaming or reshaping anything

**BarrkBOT reads these files off this server. They are an interface, not
internal state.** Changing a filename or a field name here breaks a program
running on another machine that has no way to find out.

> **Corrected 22 Aug 2026:** this file used to say "over FTP". That is no longer
> the live path. Valheim moved off G-Portal to **TheWall** on 22 Aug, and
> BarrkBOT now reads these from the **local filesystem** (`VALHEIM_SERVER_DIR`);
> TheWall does not run an FTP server at all. FTP is still supported for a remote
> host, so it is not gone — but do not reason about latency, caching or
> access from "it's an FTP pull", and do not tell anyone else to.

| Path | Written by | BarrkBOT reads it as |
| :--- | :--- | :--- |
| `BepInEx/config/TheRavensCall/BarrkBOT_data1.json` | `Companion.cs` → `PlayerRegistry.BuildBarrkBotJson` | every player's record — the whole stats system |
| `BepInEx/config/TheRavensCall/BarrkBOT_data2.json` | `Companion.cs` (recipe/item/buildable dump) | every recipe answer it gives |
| `BepInEx/config/TheRavensCall/Chronicle/TheRavensCall_Chronicle_YYYY-MM-DD.log` | `Chronicle` | "what happened today", "who was on" |
| `BepInEx/config/Fatty/BarrkBOT_feasts.json` | **Fatty** 1.1.11+ → `FeastRegistry.Flush` | every player's lifetime food counts — favourite food, most-eaten, glutton leaderboards |

`BepInEx/config/TheRavensCall/players/*.json` and
`BepInEx/config/Fatty/feasts/*.json` are persistence stores. BarrkBOT does
**not** read either — the matching aggregate carries the same fields, and one
read beats thirty-five. Keep it that way unless an aggregate stops being
complete, in which case say so here.

**Since 1.4.0, `GET /api/activity` (the dashboard's recent-events feed and season standings,
`docs/API.md`) exists on the same HTTP server as `/api/state`. It is not part of this
contract** — it is not written to disk, and its shape is not promised the way the paths
above are.

## What went wrong on 20 Aug 2026, and why this file exists

The exports were renamed — `barrkbot_players.json` → `BarrkBOT_data1.json`,
`steve_export.json` → `BarrkBOT_data2.json`. That was a reasonable change and
nobody did anything careless. **BarrkBOT served stale numbers for nine hours and
nothing anywhere reported an error.**

The reason it was invisible is worth understanding, because it will happen the
same way next time: the old files did not disappear. They sat on disk at their
last written state. So BarrkBOT's read *succeeded*, the JSON *parsed*, all 35
players were present, and every field held a plausible value. The only symptom
was a staleness warning on answers — which reads as "the mod is down", not "the
mod moved". An absent file would have been **louder**: a 550 is caught and
reported as "the mod has not written its export yet".

A file that exists and is wrong beats a file that is missing, every time, at
hiding.

## So: if you change any of this

1. **Say so in this file, in `HexiumDist/README.md`, and in `CHANGELOG.md`.**
   The README's "BarrkBOT & Discord AI Bot Integration" section is the canonical
   schema reference — it must not drift from what `PlayerRegistry.ToJson` and
   `BuildBarrkBotJson` actually emit.
2. **Prefer adding over renaming.** A new field costs BarrkBOT nothing; a
   renamed one silently zeroes a feature.
3. **If you must rename, keep writing the old name for one release.** BarrkBOT
   prefers the new name and falls back to the old, so an overlap of one version
   makes the change invisible in the good way.
4. **Tell whoever is working on BarrkBOT.** It lives at the repo **root**,
   `/home/rohan/WubarrkCODING/WindowsDEV/Discord-BarrkBOT` (branch `v6`); the readers are
   `src/actions/valheimRavensCall.js` and `src/actions/valheimGameData.js`, and
   the paths are in `EXPORT_PATHS` at the top of each.

   > **Corrected 22 Aug 2026:** this used to point at
   > `Discord-BarrkBOT/v5.7 - Final`. That tree is **superseded and edits there go
   > nowhere.** Exactly the failure this file exists to describe: a path that still
   > exists and still looks right.

   Newer exports may not need an `EXPORT_PATHS` entry at all. Since v5.x BarrkBOT
   also runs a **generic scanner** (`src/actions/valheimModData.js`) that walks
   `/BepInEx/config` breadth-first to depth 2 and picks up anything matching
   `/^(?:barrkbot[_-][\w.-]*|steve_export)\.json$/i` — case-insensitive, so
   `BarrkBOT_feasts.json` and `barrkbot_players.json` both match. Naming a new
   export to that pattern means it is discovered with no BarrkBOT-side patch.

## Fatty's feast export (added 22 Aug 2026, Fatty 1.1.11)

A second mod now writes into this contract. It lives in a different config
directory and is owned by a different repo (`/home/rohan/WubarrkCODING/Fatty`),
but the same rules apply to it.

| Path | Written by | BarrkBOT reads it as |
| :--- | :--- | :--- |
| `BepInEx/config/Fatty/BarrkBOT_feasts.json` | `FeastRegistry.Flush` | the aggregate — every player's food history |
| `BepInEx/config/Fatty/feasts/*.json` | same | **persistence store, not read.** Same rule as `TheRavensCall/players/*.json` |

Top level: `generated_at`, `source`, `players`.
Per player: `name`, `player_id`, `last_updated`, `total_meals`, `distinct_foods`,
`favourite_food`, `food_counts`, `category_counts`.

**Off by default.** Config section *11 - Feast Export*, `Enable Feast Export`,
server-authoritative, default `false`. A server that has not turned it on writes
**no file at all** — so a missing file is a normal outcome here, not an error,
and must read as "no feast data yet".

Four properties that are guarantees, not incidental behaviour:

- **`favourite_food` is `null` on a tie, and `null` when `total_meals` is 0.**
  Say "tied". Never resolve it by picking one — that invents a fact.
- **A player absent from `players` has NEVER BEEN SEEN.** That is a different
  answer from "has eaten nothing", and conflating them tells a real person they
  have never eaten anything. Offline players are never dropped, so absent really
  does mean unknown.
- **Counts are absolute lifetime totals, never deltas.** They will **jump
  retroactively** the first time a player syncs after updating Fatty — that is
  their pre-existing history arriving, not corruption. Do not diff two files to
  get "meals since yesterday"; ask Fatty to emit a per-period number instead.
- **`food_counts` keys are raw prefab names** (`CookedMeat`, not "Cooked Meat").
  Resolve display names against the wiki index. Two prettifiers would drift.

`category_counts` uses Fatty's six dietary categories. Note **`Produce`**
(vegetables + fruit): it was renamed from `Vegetable` in Fatty 1.1.9, so anything
hardcoding the old name is already wrong.

Written atomically (temp file, renamed over) and **without a BOM**, so a reader
cannot catch a half-written file and does not need to special-case
`Encoding.UTF8`'s byte-order mark the way the TheRavensCall exports do.

## Checking it from the other side

An admin can ask BarrkBOT directly:

```
ADMIN COMMAND: force a re-ingest from ftp
```

He re-reads all three sources and reports **the exact filename each one came
from**, plus how old the data is. If he reports `usingFallback`, he is reading a
pre-rename name and this server is behind.

## Field names currently depended on

Top level: `generated_at`, `world_name`, `day`, `online_count`, `raid_active`,
`raid_type`, `players`. **Since 1.4.0**, `raid_active`/`raid_type` track real raids start to
end (a pre-existing bug meant they previously only flipped for a raid restored from a saved
world, and never cleared); `raid_type` carries the raw vanilla event name (`army_eikthyr`,
`foresttrolls`, ...), not a friendly label.

Per player: `name`, `first_seen`, `last_seen`, `online`, `session_start`,
`playtime_seconds_lifetime`, `kills_narrative`, `deaths_narrative`, `gear_tier`,
`active_title`, `titles_earned`, `biomes_discovered`, `boss_kills_credited`,
`creature_kills`, `session_kills`, `damage_dealt_session`,
`damage_taken_session`, `kills_observed_lifetime`, `deaths_lifetime`,
`bosses_defeated`, `caught_fish`, `death_history`, and the v1.1.0 client-reported
set: `vanilla_stats`, `skill_levels`, `skill_progress`, `blocks_session`,
`parries_session`, `damage_blocked_session`, `builds_placed`, `builds_removed`,
`builds_repaired`, `items_crafted`, `items_upgraded`, `items_repaired`,
`resources_harvested`, `consumables_eaten`, `bosses_summoned`,
`guardian_powers_used`.

Inside `vanilla_stats`, BarrkBOT reads **`EnemyKills` and `Deaths` as the real
lifetime totals** — it prefers them over `kills_observed_lifetime` and
`deaths_lifetime`, because those only count what the server managed to observe
and undercount badly (Thorium: 13 observed against 136 actual). If those two key
names change, every lifetime number BarrkBOT reports silently reverts to the
undercount.

`skill_levels` may contain numeric keys for modded skills the server cannot name
(`"3968": 10`). BarrkBOT drops and counts them rather than reading "your 3968
skill is level 10" out loud. Resolving those names upstream would be a real
improvement.
