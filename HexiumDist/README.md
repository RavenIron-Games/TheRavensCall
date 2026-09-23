<div align="center">

# 🐦‍⬛ The Raven's Call

![Valheim Mod](https://img.shields.io/badge/Valheim-Server_Admin_Tool-orange.svg)
[![Deployment](https://img.shields.io/badge/Install-Server_Mod-critical.svg)]()
[![Companion](https://img.shields.io/badge/Client_Companion-WhereTheCrowFlies-blue.svg)]()
[![Framework](https://img.shields.io/badge/Requires-BepInEx-red.svg)]()
[![Publisher](https://img.shields.io/badge/RavenIron-Release-8B6F1F.svg)]()
[![Version](https://img.shields.io/badge/Version-1.7.0-lightgrey.svg)]()

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
   - Lightweight, zero-overhead client mod that hooks local simulation events (kills, deaths with cause resolution, damage dealt/taken, bosses slain, fish caught, and every vanilla stat, ~205 on Valheim 1.0).
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
- [☁️ Hosted Servers (Nitrado, G-Portal)](#-hosted-servers-nitrado-g-portal)
- [📖 The Chronicle](#-the-chronicle)
- [🏆 Titles & Milestones](#-titles--milestones)
- [🗓️ Seasons & Titles](#-seasons--titles)
- [⚙️ Configuration](#-configuration)
- [📁 Where Everything Lives](#-where-everything-lives)
- [📦 Installation & Dependencies](#-installation--dependencies)
- [❓ FAQ](#-faq)

</details>

---

## 🪶 What It Tracks

Every death, every kill, every fallen god, every shoreline first walked — The Raven's Call aggregates full-spectrum player telemetry sent by *WhereTheCrowFlies*:

- **Deaths**, with rich root-cause resolution — named creatures, bosses, PvP opponents, fire, frost, poison, spirit damage, drowning, or falls — and a rolling history of the last 10 deaths per player (killer, biome, position; the item-loss list is always empty on a dedicated server, see `docs/API.md`).
- **Boss kills**, credited to everyone within a configurable radius of the kill — no one who stood and fought goes unrecognized.
- **Kill and death milestones** — 1, 10, 100, 500, 1000 kills; 1, 5, 10, 25, 50, 100 deaths, and beyond.
- **Biome discovery**, tracked per player so "first time in the Mistlands" only fires once, ever, per Viking.
- **Raids**, tracked from the moment one starts to the moment it ends — the dashboard's raid banner and the export's `raid_active`/`raid_type` reflect a real raid in progress, not just one restored from a saved world.
- **Gear tier** is in the schema (`gear_tier`) but always reads `0` on a dedicated server today — advancing equipment needs a client report that doesn't exist yet.
- **Titles**, earned through creature-family kill counts, boss kills, and the number of gods felled — up to and including *Slayer of Gods* for felling all seven.
- **Damage dealt & taken**, tracked both for current sessions and lifetime cumulative combat, plus blocks, parries, and damage blocked.
- **Every vanilla stat on your own in-game Stats screen** — kills, hits, deaths, jumps, cheats, world loads, PvP hits/kills, arrows shot, portals, distance traveled, and the rest of Valheim's ~205 built-in counters (105 before 1.0) — reported as an absolute snapshot, so it backfills whatever a player had already earned before installing the mod instead of starting from zero.
- **Skill levels & progress**, for every skill a player has actually raised (Swords, Bows, Sneak, Jump, and the rest).
- **Fish caught**, structures built/removed/repaired, items crafted/upgraded/repaired, resources harvested per type, food & potions consumed, bosses summoned, and guardian powers used.
- **World census** — since 1.5.0, the server counts portals, beds, wards, ships, carts, chests and crafting stations standing in the world on a timer, how many of them players placed and by whom, and a per-player total of everything they have standing right now.

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
  "gear_tier": 0,
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
  "caught_fish": ["$item_fish1", "$item_fish3"],

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
- **Since 1.4.0, a world overview sits above the roster**: who's online now (or the last player seen, if nobody is), an all-time leaderboard sortable by kills, deaths, boss kills or playtime, the server's combined boss progress, and the most recent deaths across everyone — all read from `/api/state` (the deaths card also uses the feed below once it holds a death). Above it sit two panels fed by the new `/api/activity` endpoint: the **active season's standings**, and a **recent-events feed** (joins, leaves, deaths, boss kills, kill and death milestones, raids, titles, biome discoveries, lore and world events, season starts/ends, server start/stop — newest first, filterable by type); against a 1.3.0 server both panels stay hidden (one request, no retries), with `EventFeedCapacity = 0` the feed shows its empty state and the season panel works as normal, and the roster works either way.
- **Since 1.5.0, a World panel** joins the season and feed panels: a group strip showing how many portals, beds, wards, ships, carts, chests and crafting stations stand in the world (player-built vs. total for each), a sortable **Builders** table ranking players by what they've placed, a **portal directory** (tag, kind, builder, connected, position), and closed-by-default **Beds** and **Wards** tables. It's fed by the new `/api/census` endpoint, polled every 60 seconds on its own schedule, and driven by the server's `[Census] CensusIntervalMinutes` count (below) — against a server with the census turned off the panel shows one line saying so, and against a pre-1.5.0 server it stays hidden. The player detail view's Build tab also gains a line for what that player has standing in the world right now.
- Shows **every player** who has ever joined the realm across six tabs — **Combat, Death, Progression, Crafting, Build, Raw** — covering online status, lifetime kills and deaths (the crow-reported totals once a player runs WhereTheCrowFlies), titles, biomes, skills, boss kills, death history, fish caught, and the harvest, craft and build counters. A dedicated server has no live vitals, inventories or positions to show, and since 1.3.0 the API does not pretend otherwise.
- Includes JSON API endpoints: `/api/state`, `/api/gamedata`, `/api/activity`, `/api/census`, and `/api/health`. With `HttpApiToken` set, `/api/state`, `/api/gamedata`, `/api/activity`, `/api/census` and `/api/pins` need `?token=<value>` (or an `X-Api-Token` header); `/api/health` and the page itself stay open. Since 1.3.0 the bundled page holds the token in its own settings panel (gear icon, top right) — enter it there and it's kept in the browser and sent as an `X-Api-Token` header on every request; a 401 opens that panel automatically the first time (the feed/season panels show a one-line "Needs the API token (Settings)." notice instead of loading empty).
- Can be toggled off with `EnableHttpServer = false` if only file export is desired.
- **Since 1.6.0, a server on a rented host can publish this same dashboard to a hosted URL** — see [Hosted Servers (Nitrado, G-Portal)](#-hosted-servers-nitrado-g-portal) below. The hosted page shows the same roster, feed and World panel as `localhost:2112`, plus how long ago the game server last reported ("server reported Ns ago" / "server silent since …", greying out once it's gone quiet) and a "Registered, waiting for `<id>` to report" state before its first push lands; fish and resource names fall back to prettified tokens there instead of their real names — the one visible difference from `localhost:2112`.
- **Upgrading from 1.2.x:** delete any `theravenscall.html` you placed in `BepInEx/config/TheRavensCall/` or next to the DLL. A copy from before 1.3.0 still overrides the bundled page, cannot read the 1.3.0 `/api/state` shape, and makes the server log one warning per run naming the file.

---

## ☁️ Hosted Servers (Nitrado, G-Portal)

On a rented game server the admin gets the game's ports, a web panel and FTP — no shell, no extra services, no way to open the dashboard's port. So `http://localhost:2112` is unreachable from anywhere on a Nitrado or G-Portal box. Since 1.6.0 the mod can instead **push** its data out over HTTPS to a small receiver on the owner's Netlify team, which keeps the latest copy and serves the same dashboard page from a public URL. With `PushUrl` empty (the default) nothing changes — the server behaves exactly like 1.5.0, entirely local.

> [!IMPORTANT]
> **What hosting publishes.** A read-token holder sees everything the three pushed routes serve: every known player's stats, skills, titles and death coordinates (`/api/state`); the event feed and season standings (`/api/activity`); and the census, which lists every portal, bed and ward in the loaded world with its rounded x/z — in effect where the bases are. Until 1.6.0 all of that stayed on a machine the admin controls; hosting moves the latest copy to a third party's storage behind one shared token. **Leaving `PushUrl` empty keeps a server entirely local, exactly as before.**

### Setup

The receiver described here is the owner's own and isn't open to other servers in 1.6.0; if you want a hosted dashboard for your own server, deploy your own private copy of `hosting/netlify/` from the [GitHub repo](https://github.com/RavenIron-Games/TheRavensCall) — steps 3 and 4 below are then done in your own Netlify account against your own domain.

1. **Generate both tokens** — `openssl rand -hex 24`, run twice: once for `HttpApiToken` (if you don't already have one at least 24 characters long) and once for `PushToken`.
2. **Set `HttpApiToken` (24+ characters), `PushUrl`, `PushToken` and `PushServerId`** under `[Companion]`/`[Push]` in `com.raveniron.theravenscall.cfg`.
3. **Register the id** with both tokens' sha256 hashes in the receiver's `TRC_SERVERS` environment variable, then redeploy the receiver — a change in the Netlify UI does nothing until the site is redeployed.
4. **Open `https://trc.ravenirongames.com/s/<id>/`** (that is Raven Iron Games' receiver — use whatever address the receiver you push to is deployed at).
5. **To unpublish**, send `DELETE /s/<id>/api` with the write token in the `Authorization: Bearer` header. It removes the stored envelopes and answers `{"deleted":true}`; how an admin takes a server back offline. Deleting the id from the registry takes the dashboard offline on the next request too, but the stored copy stays until the `DELETE` runs.

### Cadence

The push runs from the same poll tick as everything else, so the effective cadence rounds up to the next multiple of `StatsPushIntervalSeconds` (default 10 s): `PushIntervalSeconds = 15` pushes every 20 s, and a `StatsPushIntervalSeconds` of 120 caps the push at 120 s no matter what `PushIntervalSeconds` is set to. The effective heartbeat is `max(PushHeartbeatMinutes × 60, effective interval)`.

### When the push is refused

A rented server's admin has no shell and can't reach `/api/health` — the BepInEx log pulled over FTP is the only place a broken push can be seen. Four refusals are named there, each retrying every 15 minutes:

- `401` — "PushToken rejected — check [Push] PushToken and PushServerId against the receiver's registry"
- `409` — "the receiver has a different HttpApiToken hash registered for '\<id>' — re-register read_token_sha256 and redeploy"
- `422` — "the receiver refused the bundle — set [Companion] HttpApiToken (24+ characters)"
- `413` — "bundle over the receiver's 2 MB limit"

### Where this has actually run

As of this release the 1.6.0 push has run against a local receiver (`netlify dev`) from a Windows test server and from a Linux dedicated server (Valheim 1.0.15, under WSL2), including the change gate, heartbeat, backoff and recovery, the four named refusals and the hosted page's waiting and stale states (`docs/TESTPLAN-local-1.6.0-2026-09-22.md`). The same Linux server then pushed over https, with certificate validation, to the receiver deployed on the owner's Netlify team (§8 step 9 of the scope, the release gate). Not yet run on a rented host. Since 2026-09-22 a real dedicated server (Windows, on another host, over the internet) pushes to the hosted receiver at `https://trc.ravenirongames.com`: state, feed and a census of about 1.1 million objects (370 ms), on the default 60 s interval and 10 min heartbeat.

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

**Choosing your title.** Since 1.7.0 a player picks which earned title the server uses — in its Discord/Chronicle narration and on the dashboard/export — rather than always the first one earned. Nothing changes on the in-game nameplate. With `WhereTheCrowFlies` 1.2.0+: `/title` lists what you've earned, `/title Wolf Hunter` sets it (only to a title you've already earned), `/title clear` shows no title until your next earned title, which becomes active automatically. An admin can do the same for anyone, online or not, with `ravenscall title <player> [<title>|clear]` — from the server console with no client mod needed, or from an admin's game client that has WhereTheCrowFlies installed. Either way the change shows up on the dashboard and export at the next poll.

---

## 🗓️ Seasons & Titles

Manage server wipes and eras, or set a player's title, using in-game console commands:

```
ravenscall season start [name]
ravenscall season end
ravenscall title <player>
ravenscall title <player> <title>
ravenscall title <player> clear
```

Seasons archive Chronicle logs into dated season folders and write summary stats on completion. Since 1.6.1 `season start` refuses while a season is running: end it first.

`ravenscall title` lists, sets or clears a player's active title from the titles they've already earned — the same rule the player's own `/title` follows. The player need not be online. From the server console it needs no client mod; from an admin's game client it goes through WhereTheCrowFlies' `ravenscall` routing stub, the same as the season commands above.

---

## ⚙️ Configuration

Configuration is located at `BepInEx/config/com.raveniron.theravenscall.cfg`:

| Section | Setting | Default | Description |
| :--- | :--- | :--- | :--- |
| **Combat** | `AcceptClientReports` | `true` | Accepts telemetry RPC reports from *WhereTheCrowFlies* client mods. Set to `false` it also disables the player's `/title` picker (the admin's `ravenscall title` still works). |
| **Combat** | `LogCombatReports` | `false` | Enables verbose console logging for incoming combat reports. |
| **Events** | `EnablePlayerDeath` ... `EnableTitleEarned` | `true` | Independently toggles narration/Chronicle for each event type. |
| **Events** | `EnableRaid` | `true` | Records raid start and end in the Chronicle and the dashboard feed (`raid_start`/`raid_end`). Not posted to Discord. The raid banner and `raid_active` in the API work either way — this only gates the narration. |
| **Format** | `ShowDayNumber` / `ShowOnlineCount` | `true` | Appends `[Day N]` and `(N online)` to announcements. |
| **Format** | `MessagePrefix` | `⚔ ` | Custom prefix added to all broadcast messages. |
| **Log** | `EnableChronicleLog` | `true` | Writes daily JSONL Chronicle logs. |
| **Bosses** | `BossCreditRadius` | `100` | Proximity radius (meters) for crediting boss assists. |
| **Discord** | `WebhookUrl` | `""` | Discord webhook URL for event embeds. |
| **Narrative**| `EnableNarrativeMode` | `true` | Uses dynamic Norse flavor-text templates. |
| **Companion**| `EnableHttpServer` | `true` | Runs the web dashboard and local HTTP API. |
| **Companion**| `HttpServerPort` | `2112` | Port for web dashboard and API access. |
| **Companion**| `HttpBindAllInterfaces` | `false` | Also listen on every interface. Off by default since 1.2.4; the API has no login. |
| **Companion**| `HttpApiToken` | `""` | If set, `/api/state`, `/api/gamedata`, `/api/activity`, `/api/census` and `/api/pins` require `?token=` or `X-Api-Token`. |
| **Companion**| `StatsPushIntervalSeconds` | `10` | Frequency (seconds) for updating `BarrkBOT_data1.json` / `BarrkBOT_data2.json`. |
| **Companion**| `EventFeedCapacity` | `200` | Number of recent events kept in memory and served by `/api/activity`, newest first (0 to 1000). Saved to `event_feed.json` so the feed survives a restart. `0` turns the feed off; the endpoint still answers, with an empty `events` array. |
| **Census** | `CensusIntervalMinutes` | `5` | How often (minutes) the server counts portals, beds, wards, ships, carts, chests and crafting stations standing in the world and rebuilds `/api/census`. `0` turns the census off; the endpoint still answers, with `enabled: false`. |
| **Push** | `PushUrl` | `""` | Since 1.6.0. The hosted receiver's base URL to push to (e.g. `https://trc.ravenirongames.com`). Empty (the default) disables the push entirely — the server behaves exactly like 1.5.0. See [Hosted Servers](#-hosted-servers-nitrado-g-portal). |
| **Push** | `PushToken` | `""` | Since 1.6.0. Bearer token sent with every push, checked against the receiver's registry. |
| **Push** | `PushServerId` | `""` | Since 1.6.0. This server's id in the receiver's registry. No default — required to push, and must match `[a-z0-9-]{1,32}` and the id registered in `TRC_SERVERS`. |
| **Push** | `PushIntervalSeconds` | `60` | Since 1.6.0. How often (seconds) to push, clamped 15..3600. The effective cadence rounds up to the next multiple of `StatsPushIntervalSeconds`. |
| **Push** | `PushHeartbeatMinutes` | `10` | Since 1.6.0. How often (minutes) to push all three envelopes even when nothing changed, clamped 1..1440. |

---

## 📁 Where Everything Lives

```
BepInEx/config/TheRavensCall/
├── BarrkBOT_data1.json         ← Aggregate player export for Discord bots
├── BarrkBOT_data2.json         ← Static game data export (items, recipes, buildables)
├── event_feed.json             ← Since 1.4.0: recent events backing /api/activity, so the
│                                   dashboard feed survives a restart
├── season_baseline.json        ← Since 1.4.0: each player's counters at season start, so
│                                   season standings can be reported as deltas; present only
│                                   while a season is active
├── players/                    ← Persistent JSON record per player
├── player_ids.json             ← Since 1.5.0: known player profile IDs mapped to names, used
│                                   to name a piece's builder in the world census
├── Chronicle/
│   └── TheRavensCall_Chronicle_2026-08-17.log
├── theravenscall.html          ← OPTIONAL: only needed to override the dashboard page,
│                                   which has shipped embedded inside TheRavensCall.dll since 1.3.0;
│                                   a copy from before 1.3.0 shadows the bundled page - delete it
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

---

## ❓ FAQ

**Do players have to install anything?**
No. The server mod works on its own. But a dedicated server never sees combat: kills, deaths, damage, fish and the ~205 vanilla stats reach the server only from [WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies) on a player's client. Without it a player still shows up with joins, leaves, biome discoveries and playtime, and the world census still credits them with the structures they have standing; but their kills, deaths, and their build, craft and harvest counters stay at zero.

**Why are a player's kills and deaths zero, or far too low?**
See above: that player is not running WhereTheCrowFlies. Once they install it, their vanilla stats arrive as an absolute snapshot, so everything they earned before the mod existed is backfilled rather than starting from zero.

**Where is the dashboard?**
`http://localhost:2112` on the server machine. Since 1.2.4 the server listens on localhost only. To open it from another PC set `HttpBindAllInterfaces = true` together with `HttpApiToken`, and keep the port behind a firewall: the API has no login of its own and hands every known player's stats, skills, titles and death coordinates to anyone who can reach it.

**The dashboard says "Needs the API token (Settings)." What do I enter?**
The `HttpApiToken` value from `com.raveniron.theravenscall.cfg`, once, in the page's Settings panel (the gear icon, top right). It is kept in that browser only. `/api/health` and the page itself never need it.

**My server is on Nitrado or G-Portal and I cannot open port 2112. Can I still get the dashboard?**
Yes, since 1.6.0: the mod pushes its data out over HTTPS to a small receiver site, and the same dashboard page is served from there. It needs nothing but outbound HTTPS, the same path out of the host the Discord webhook already uses — though it has not yet been run on a rented host. Raven Iron Games' own receiver is not open to other servers in 1.6.0, so deploy your own private copy of `hosting/netlify/` from the [GitHub repo](https://github.com/RavenIron-Games/TheRavensCall); its README has the steps. See [Hosted Servers](#-hosted-servers-nitrado-g-portal).

**Does pushing make my server's data public?**
Only to whoever holds that server's read token, and only for that server. They see everything the three pushed routes serve: every known player's stats, skills, titles and death coordinates, the event feed and season standings, and the census with the position of every portal, bed and ward. With `PushUrl` empty, the default, nothing leaves the box.

**The push is set up but the hosted page shows nothing.**
Read the BepInEx log. A working push logs one `Push enabled: server_id='…'` line at boot and then stays quiet. A `Push disabled:` line names the config key that is missing or malformed; a `Push rejected:` line names one of the four receiver refusals listed under [When the push is refused](#when-the-push-is-refused); a `Push failed:` line means the receiver could not be reached, and the mod keeps retrying on its own. Until the first push lands the hosted page reads "Registered, waiting for <id> to report"; "server silent since …" means no push has arrived for three heartbeats.

**How do I start or end a season?**
From an admin's game client that has WhereTheCrowFlies installed, in the console (F5): `ravenscall season start Autumn`, later `ravenscall season end`. The server checks its admin list before running it. Leave the name off and the season is named `Season_` plus today's date; the name becomes the Chronicle's season folder, so trailing periods and spaces are trimmed and it is capped at 64 characters. End the running season first: since 1.6.1 `season start` refuses while a season is running and names it (before 1.6.1 it replaced the running season in place: the console still answered "Season started", but the old season got no summary and no season-end line). See [Seasons & Titles](#-seasons--titles).

**Does the world census slow the server down?**
It walks the loaded world's objects on the main thread, on a timer, every 5 minutes by default; a real server with about 1.1 million objects finishes in under half a second, so it costs one short hitch every five minutes. `CensusIntervalMinutes = 0` turns it off, and `/api/census` then answers with `enabled: false`.

**What are `BarrkBOT_data1.json` and `BarrkBOT_data2.json`? Can I delete them?**
The exports a Discord bot reads: all players in one file, the game's items, recipes and buildables in the other. `BarrkBOT_data1.json` is rewritten every `StatsPushIntervalSeconds` (10 s by default), so deleting it does no harm — it is back on the next tick. `BarrkBOT_data2.json` is written once per server boot, so deleting it leaves `/api/gamedata` serving empty arrays until the next restart. Renaming them does: a bot finds them by name. See [BarrkBOT & Discord AI Bot Integration](#-barrkbot--discord-ai-bot-integration).

**Do I need a Discord webhook?**
No. With `WebhookUrl` empty nothing is posted to Discord and everything else, the Chronicle, the dashboard and the exports, works unchanged.

**I upgraded from 1.2.x and the dashboard is broken, or the log warns about `theravenscall.html`.**
Delete any `theravenscall.html` you placed in `BepInEx/config/TheRavensCall/` or next to the DLL. The page has shipped inside the DLL since 1.3.0; an older loose copy still overrides it and cannot read the current API.

**Does it run on a Linux server?**
Yes. 1.6.0 has run on a Linux dedicated server on Valheim 1.0.15 (under WSL2), including the push over HTTPS with certificate validation on.

**Which Valheim version does it need?**
Valheim 1.0. The 1.6.0 build has run on 1.0.12 (a Windows dedicated server) and 1.0.15 (a Linux one). The older 0.22x builds are not supported: on them the `ravenscall` console command fails to register at load, and the world census throws on its first run.

**Can I run it on a listen server hosted from the game?**
It is built and tested for dedicated servers. It loads on a listen-server host, but that setup has not been tested.

**How do I take a server off a hosted dashboard?**
Send `DELETE /s/<id>/api` to the receiver with the write token in the `Authorization: Bearer` header; the stored copy is removed and the page reads "no data" again. Then remove the id from the receiver's registry and redeploy, so the tokens are dead too. Clearing `PushUrl` on the server stops new pushes at the next restart.

**Why does the dashboard show no health, inventory or position for a player who is online right now?**
Because a dedicated server never has them: it relays the world, while a player's health, inventory and position live on that player's own client. The dashboard shows each player's stored record instead: totals, titles, skills, milestones, the death history and everything WhereTheCrowFlies reported. The only positions it keeps are where players died.

**Can I turn the web dashboard off and keep only the file exports?**
Yes: `EnableHttpServer = false`. The BarrkBOT exports, the Chronicle, the Discord webhook and, since 1.6.0, the push all keep running without it.

**Where does the mod keep its data, and what do I copy when I move or rebuild the server?**
Everything lives under `BepInEx/config/TheRavensCall/` (the per-player records in `players/`, the Chronicle, `seasons.json`, `season_baseline.json`, `event_feed.json`, `player_ids.json`, `lore.txt`) plus the settings file `BepInEx/config/com.raveniron.theravenscall.cfg`. Copy that folder and that file. The two BarrkBOT files are rebuilt after a boot, and the dashboard page ships inside the DLL. See [Where Everything Lives](#-where-everything-lives).

---
## 💜 Support Raven Iron

Every Raven Iron mod is free, and stays free — all of it, always. Nothing is held
back for patrons, and nothing ever will be.

If you'd like to help cover server hosting and test hardware:

- **Website** — <https://ravenirongames.com>
- **Patreon** — <https://www.patreon.com/cw/RavenIronGames>
- **Discord** — <https://discord.gg/AGKDEurAVa> — a channel per mod, and where the
  testing happens
