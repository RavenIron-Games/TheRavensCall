<div align="center">

# 🐦‍⬛ The Raven's Call

![Valheim Mod](https://img.shields.io/badge/Valheim-Server_Admin_Tool-orange.svg)
[![Deployment](https://img.shields.io/badge/Install-Server_Mod-critical.svg)]()
[![Companion](https://img.shields.io/badge/Client_Companion-WhereTheCrowFlies-blue.svg)]()
[![Framework](https://img.shields.io/badge/Requires-BepInEx-red.svg)]()
[![Publisher](https://img.shields.io/badge/RavenIron-Release-8B6F1F.svg)]()
[![Version](https://img.shields.io/badge/Version-1.3.0-lightgrey.svg)]()

**RavenIron's server admin & analytics engine: aggregates player telemetry from *WhereTheCrowFlies*, chronicles realm history, and feeds live web dashboards and Discord AI bots.**

</div>

---

> *Odin kept two ravens. Huginn flew out each dawn to see what moved in Midgard; Muninn stayed close to hold what had already happened. Together they were his whole knowledge of the nine worlds, whispered into his ear each evening.*
>
> *The Raven's Call is Muninn, perched upon your dedicated server holding the chronicle of every fallen god, player milestone, and Viking saga. Out in the field, its companion client mod **[WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies)** is Huginn, flying alongside your players to gather combat, kills, deaths, and telemetry from the living edge of Midgard and send it home.*

---

## ⚠️ How It Works: Server Mod + Client Companion

In Valheim's multiplayer architecture, the dedicated server acts as a world-state relay. **Combat resolution, damage calculations, creature deaths, and player statistics execute exclusively on the game client that owns the local simulation zone.** A dedicated server on its own never executes `Character.Damage` or `Character.OnDeath` for player combat.

To provide seamless, 100% accurate tracking without missing a single event, the system uses two coordinated mods:

1. **The Raven's Call (Server Mod — This Mod)**
   - Installed on the **Dedicated Server** (or listen-server host).
   - Listens for telemetry routed RPCs (`RavensCall_EventReport_V2` and `RavensCall_CombatReport_V1`) from connected clients.
   - Verifies incoming reports against connected players to ensure authentic data.
   - Maintains the persistent multi-player database (`BepInEx/config/TheRavensCall/players/`).
   - Serves the live web dashboard on port `2112`.
   - Generates the timer-refreshed `BarrkBOT_data1.json` (players) and `BarrkBOT_data2.json` (game data) exports for Discord bots.
   - Rotates the daily JSONL Chronicle audit log and posts real-time Discord webhook embeds.

2. **[WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies) (Client Companion Mod)**
   - Installed on **Player Game Clients**.
   - Lightweight, zero-overhead client mod that hooks local simulation events (kills, deaths with cause resolution, damage dealt/taken, bosses slain, fish caught, gear tier, and every vanilla stat, ~205 on Valheim 1.0).
   - Batches continuous telemetry into efficient 10-second updates and transmits them via routed RPC to The Raven's Call.
   - **Safe Everywhere**: When connecting to vanilla or unmodded servers, client RPC calls silently discard with zero errors, zero latency, and zero log noise.

> [!IMPORTANT]
> **Admins:** Install `TheRavensCall.dll` on your dedicated server.  
> **Players:** Install `WhereTheCrowFlies.dll` on game clients so their combat, deaths, boss encounters, and stats are reported to the server's ledger!

---

<details>
<summary>📜 <b>Contents</b></summary>

- [🪶 What It Tracks](#-what-it-tracks)
- [📯 Where the News Goes](#-where-the-news-goes)
- [🤖 BarrkBOT & Discord AI Bot Integration](#-barrkbot--discord-ai-bot-integration)
  - [The Export File](#the-export-file)
  - [Per-Player Schema](#per-player-schema)
  - [Reading It From a Bot](#reading-it-from-a-bot)
  - [The Discord Webhook (Narration)](#the-discord-webhook-narration)
- [🖥️ The Web Dashboard](#-the-web-dashboard)
- [📖 The Chronicle](#-the-chronicle)
- [🏆 Titles & Milestones](#-titles--milestones)
- [🗓️ Seasons](#-seasons)
- [⚙️ Configuration](#-configuration)
- [📁 Where Everything Lives](#-where-everything-lives)
- [📦 Installation & Dependencies](#-installation--dependencies)

</details>

---

## 🪶 What It Tracks

Every death, every kill, every fallen god, every shoreline first walked — The Raven's Call aggregates full-spectrum player telemetry sent by *WhereTheCrowFlies*:

- **Deaths**, with rich root-cause resolution — named creatures, bosses, PvP opponents, fire, frost, poison, drowning, smoke, or falls — and a rolling history of the last 10 deaths per player (killer, biome, position, items lost).
- **Boss kills**, credited to everyone within a configurable radius of the kill — no one who stood and fought goes unrecognized.
- **Kill and death milestones** — 1, 10, 100, 500, 1000 kills; 1, 5, 10, 25, 50, 100 deaths, and beyond.
- **Biome discovery**, tracked per player so "first time in the Mistlands" only fires once, ever, per Viking.
- **Gear tier**, tracked as players advance their equipment from Leather through Flametal.
- **Titles**, earned through creature-family kill counts, boss kills, and the number of gods felled — up to and including *Slayer of Gods* for felling all seven.
- **Damage dealt & taken**, tracked both for current sessions and lifetime cumulative combat, plus blocks, parries, and damage blocked.
- **Every vanilla stat on your own in-game Stats screen** — kills, hits, deaths, jumps, cheats, world loads, PvP hits/kills, arrows shot, portals, distance traveled, and the rest of Valheim's ~205 built-in counters (105 before 1.0) — reported as an absolute snapshot, so it backfills whatever a player had already earned before installing the mod instead of starting from zero.
- **Skill levels & progress**, for every skill a player has actually raised (Swords, Bows, Sneak, Jump, and the rest).
- **Fish caught**, structures built/removed/repaired, items crafted/upgraded/repaired, resources harvested per type, food & potions consumed, bosses summoned, and guardian powers used.

## 📯 Where the News Goes

The Raven's Call routes incoming telemetry across four server-driven channels:

1. **Discord**, via webhook — player-attributable events post as rich, colored embeds the moment they occur.
2. **The Chronicle** — a JSON-lines log on disk, one line per event, rotating daily for server history and audit trails.
3. **The Web Dashboard** — a live, standalone web page accessible in any browser for admins and players.
4. **The BarrkBOT Export** — a clean JSON file, refreshed on a timer, built specifically to be read by Discord bots.

---

## 🤖 BarrkBOT & Discord AI Bot Integration

The Raven's Call is designed from the ground up to **give Discord bots everything they need to answer real questions about server players, without the bot ever touching the game itself.**

### The Export File

Every `StatsPushIntervalSeconds` (default **10 seconds**), the mod writes:

```
BepInEx/config/TheRavensCall/BarrkBOT_data1.json   ← every player's full record
BepInEx/config/TheRavensCall/BarrkBOT_data2.json   ← static game data (items, recipes, buildables)
```

Two files, sitting on the server's disk, each overwritten in place. If your Discord bot accesses the game server over FTP/SFTP, these live in a standard location requiring no new ports or special credentials. (The same player data is also available live over HTTP — see [The Web Dashboard](#-the-web-dashboard)).

**Top-level shape:**

```json
{
  "generated_at": "2026-08-17T04:12:00.000Z",
  "world_name": "Midgard",
  "day": 214,
  "online_count": 6,
  "raid_active": false,
  "raid_type": "",
  "players": {
    "Thorium Wubarrk": { "...": "see below" },
    "Asfa W": { "...": "see below" }
  }
}
```

`players` is an object keyed by the player's exact in-game display name, matching standard bot player-stats stores and leaderboards.

### Per-Player Schema

Every player's record is verified against active server connections:

```json
{
  "name": "Thorium Wubarrk",
  "first_seen": "2026-01-04T18:30:00.000Z",
  "last_seen": "2026-08-17T04:11:52.000Z",
  "online": true,
  "session_start": "2026-08-17T02:00:00.000Z",
  "playtime_seconds_lifetime": 412884,

  "kills_narrative": 341,
  "deaths_narrative": 12,
  "gear_tier": 5,
  "active_title": "Wolf Hunter",
  "titles_earned": ["Boar Hunter", "Wolf Hunter", "Stag Breaker"],
  "biomes_discovered": ["Meadows", "BlackForest", "Swamp", "Mountain"],
  "boss_kills_credited": 4,
  "creature_kills": { "Boar": 118, "Wolf": 54, "Draugr": 31 },

  "session_kills": 9,
  "damage_dealt_session": 4120.5,
  "damage_taken_session": 860.0,
  "kills_observed_lifetime": 355,
  "deaths_lifetime": 12,

  "bosses_defeated": ["eikthyr", "elder", "bonemass", "moder"],
  "caught_fish": ["fish_1", "fish_3", "fish_9"],

  "death_history": [
    {
      "timestamp": "2026-08-17T02:04:11.000Z",
      "killer": "Fuling Berserker",
      "location": { "x": 1204.5, "y": 38.2, "z": -880.1 },
      "biome": "Plains",
      "items": [{ "name": "Bronze Sword", "qty": 1 }]
    }
  ],

  "vanilla_stats": { "Deaths": 3, "Cheats": 289, "WorldLoads": 3342, "EnemyHits": 266, "EnemyKills": 123, "PlayerHits": 0, "PlayerKills": 0, "HitsTakenEnemies": 403, "ArrowsShot": 0, "BossKills": 0, "Jumps": 324, "PortalsUsed": 5, "DistanceTraveled": 38018.4 },
  "skill_levels": { "Swords": 34.0, "Jump": 12.0, "Run": 8.0 },
  "skill_progress": { "Swords": 0.42, "Jump": 0.10, "Run": 0.75 },

  "blocks_session": 4,
  "parries_session": 1,
  "damage_blocked_session": 210.0,
  "builds_placed": 118,
  "builds_removed": 6,
  "builds_repaired": 2,
  "items_crafted": 54,
  "items_upgraded": 11,
  "items_repaired": 3,
  "resources_harvested": { "Wood": 812, "Stone": 240, "Raspberry": 40 },
  "consumables_eaten": 76,
  "bosses_summoned": 4,
  "guardian_powers_used": 9
}
```

**Field Reference:**

| Field | Description | Details |
| :--- | :--- | :--- |
| `kills_narrative` / `kills_observed_lifetime` | Milestone/title kill count vs. raw lifetime kill count. | Narrative count gates titles and milestones; observed tracks all verified kills. |
| `deaths_narrative` / `deaths_lifetime` | Narrative death count vs. lifetime death count. | Ideal for server leaderboards and death counters. |
| `active_title` / `titles_earned` | Currently displayed title vs. all unlocked titles. | Players can unlock many titles and equip their preferred epithet. |
| `bosses_defeated` | Defeated boss keys. | Canonical short keys: `eikthyr`, `elder`, `bonemass`, `moder`, `yagluth`, `queen`, `fader`. |
| `session_kills` / `damage_dealt_session` / `damage_taken_session` | Session-scoped combat stats. | Resets to zero on each new player connection. |
| `blocks_session` / `parries_session` / `damage_blocked_session` | Session-scoped defense stats. | Resets to zero on each new player connection. |
| `playtime_seconds_lifetime` | Total accumulated play time. | Persists across server restarts. Divide by 3600 for hours. |
| `vanilla_stats` | Every counter on the player's own in-game Stats screen — kills, hits, deaths, jumps, cheats, world loads, PvP hits/kills, arrows shot, portals, distance traveled, and the rest of Valheim's ~205 built-in `PlayerStatType` counters (105 before 1.0). | Keyed by Valheim's own enum names. Reported by the *WhereTheCrowFlies* client mod as an absolute snapshot (not deltas), so it backfills a player's true lifetime totals — including whatever they'd already earned before this mod was installed — the moment their client syncs, and can't drift from a dropped packet. Empty until a player's client has synced at least once. |
| `skill_levels` / `skill_progress` | Skill level (0-100) and progress-to-next-level (0-1) per skill, e.g. `Swords`, `Bows`, `Jump`, `Sneak`. | Same absolute-snapshot reporting as `vanilla_stats`. A skill absent from both means the player has never raised it, not that it's at zero. |
| `builds_*` / `items_*` / `resources_harvested` / `consumables_eaten` / `bosses_summoned` / `guardian_powers_used` | Lifetime activity counters — construction, crafting/upgrading/repairing, foraging/farming/sap totals per resource, food & potions consumed, bosses summoned at altars, and Forsaken guardian powers activated. | Aggregate counts only, not full histories — same bounded-footprint approach as `death_history`. |

### Reading It From a Bot

A minimal Node.js read example:

```js
import { readFileSync } from 'node:fs';

const data = JSON.parse(readFileSync('BarrkBOT_data1.json', 'utf8'));
const player = data.players['Thorium Wubarrk'];

if (player) {
  console.log(`${player.name}: ${player.kills_narrative} kills, ` +
    `${player.deaths_narrative} deaths, title: ${player.active_title || 'none'}`);
}
```

### The Discord Webhook (Narration)

Set `WebhookUrl` under `[Discord]` in `BepInEx/config/com.raveniron.theravenscall.cfg` to post real-time event embeds (deaths, boss kills, milestones, biome discoveries, gear advances) directly into your Discord channel.

---

## 🖥️ The Web Dashboard

A full player-stats dashboard served directly by the mod's built-in HTTP server:

- Open `http://localhost:2112` on the server machine. Since 1.2.4 the server listens on localhost only unless `HttpBindAllInterfaces = true`; the API hands every known player's stats, skills, titles and death coordinates to anyone who can reach the port, so open it to the network only behind a firewall or with `HttpApiToken` set.
- Displays live vitals, inventories, combat stats, death history, fishing, structure timers, boats, tamed creatures, and progression tracking for **every player** who has joined the realm.
- Includes JSON API endpoints: `/api/state`, `/api/gamedata`, and `/api/health`. With `HttpApiToken` set, `/api/state`, `/api/gamedata` and `/api/pins` need `?token=<value>` (or an `X-Api-Token` header); `/api/health` and the page itself stay open. The bundled page does not send a token, so its live data stops loading while one is set.
- Can be toggled off with `EnableHttpServer = false` if only file export is desired.

---

## 📖 The Chronicle

Every realm event is recorded as a structured JSON line in:

```
BepInEx/config/TheRavensCall/Chronicle/TheRavensCall_Chronicle_{yyyy-MM-dd}.log
```

Rotates daily at UTC midnight. Enable or disable with `EnableChronicleLog`. Provides a permanent, grep-friendly audit trail for your server.

---

## 🏆 Titles & Milestones

Players unlock titles by slaying creature families (100, 500, 1000 kills) and defeating Valheim's world bosses (*Stag Breaker*, *Bane of the Swamp*, *The Ashen*, etc.). Felling all seven gods awards the legendary title *Slayer of Gods*. Unlocked titles are written directly to `active_title` and `titles_earned` in the export data.

---

## 🗓️ Seasons

Manage server wipes and eras using in-game console commands:

```
ravenscall season start [name]
ravenscall season end
```

Archives Chronicle logs into dated season folders and writes summary stats on completion.

---

## ⚙️ Configuration

Configuration is located at `BepInEx/config/com.raveniron.theravenscall.cfg`:

| Section | Setting | Default | Description |
| :--- | :--- | :--- | :--- |
| **Combat** | `AcceptClientReports` | `true` | Accepts telemetry RPC reports from *WhereTheCrowFlies* client mods. |
| **Combat** | `LogCombatReports` | `false` | Enables verbose console logging for incoming combat reports. |
| **Events** | `EnablePlayerDeath` ... `EnableTitleEarned` | `true` | Independently toggles narration/Chronicle for each event type. |
| **Format** | `ShowDayNumber` / `ShowOnlineCount` | `true` | Appends `[Day N]` and `(N online)` to announcements. |
| **Format** | `MessagePrefix` | `⚔ ` | Custom prefix added to all broadcast messages. |
| **Log** | `EnableChronicleLog` | `true` | Writes daily JSONL Chronicle logs. |
| **Bosses** | `BossCreditRadius` | `100` | Proximity radius (meters) for crediting boss assists. |
| **Discord** | `WebhookUrl` | `""` | Discord webhook URL for event embeds. |
| **Narrative**| `EnableNarrativeMode` | `true` | Uses dynamic Norse flavor-text templates. |
| **Companion**| `EnableHttpServer` | `true` | Runs the web dashboard and local HTTP API. |
| **Companion**| `HttpServerPort` | `2112` | Port for web dashboard and API access. |
| **Companion**| `HttpBindAllInterfaces` | `false` | Also listen on every interface. Off by default since 1.2.4; the API has no login. |
| **Companion**| `HttpApiToken` | `""` | If set, `/api/state`, `/api/gamedata` and `/api/pins` require `?token=` or `X-Api-Token`. |
| **Companion**| `StatsPushIntervalSeconds` | `10` | Frequency (seconds) for updating `BarrkBOT_data1.json` / `BarrkBOT_data2.json`. |

---

## 📁 Where Everything Lives

```
BepInEx/config/TheRavensCall/
├── BarrkBOT_data1.json         ← Aggregate player export for Discord bots
├── BarrkBOT_data2.json         ← Static game data export (items, recipes, buildables)
├── players/                    ← Persistent JSON record per player
├── Chronicle/
│   └── TheRavensCall_Chronicle_2026-08-17.log
├── theravenscall.html          ← Embedded dashboard web UI
├── lore.txt                    ← Lore broadcast repository
└── seasons.json                ← Active and historical season metadata
```

---

## 📦 Installation & Dependencies

### Server Setup (Admins)
1. Install [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) on your dedicated server.
2. Place `TheRavensCall.dll` into `BepInEx/plugins/`.
3. Launch server to generate configuration files.

### Client Setup (Players)
1. Players install [WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies) on their Valheim game clients.
2. Combat, deaths, boss encounters, and stats will automatically transmit to The Raven's Call.
