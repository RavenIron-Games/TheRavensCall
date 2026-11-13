<!-- README schema v2 -->
# 🐦‍⬛ The Raven's Call

![THE RAVEN'S CALL](https://img.shields.io/badge/THE_RAVEN%27S_CALL-YOUR_SERVER_REMEMBERS_EVERYTHING-c9a227?style=for-the-badge&labelColor=1b1b1b)
![VERSION](https://img.shields.io/badge/VERSION-1.7.2-8b1e1e?style=for-the-badge&labelColor=1b1b1b)
![SERVER SIDE](https://img.shields.io/badge/SERVER--SIDE-PLAYERS_ADD_WHERETHECROWFLIES-1f4e79?style=for-the-badge&labelColor=1b1b1b)

> *Odin kept two ravens. Huginn flew out each dawn to see what moved in Midgard; Muninn stayed close to hold what had already happened. The Raven's Call is Muninn, perched on your dedicated server, keeping the chronicle of every fallen god and every Viking's saga. Its companion, **[WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies)**, is Huginn, flying beside your players and carrying the news home.*

### Every death, every fallen god, every first step into the Mistlands: written down, and told to your Discord, your browser and your bot.

The Raven's Call is RavenIron's server admin and analytics mod. It keeps a record of every player who has ever joined your realm: kills, deaths, bosses, skills, titles, playtime, what they built and harvested, and a census of what stands in the world. It narrates the big moments to Discord, writes a daily Chronicle, serves a live web dashboard and writes a JSON export made for Discord bots.

It runs on the server alone. A dedicated server never sees combat, so each player adds the small client mod **WhereTheCrowFlies**, which reports their kills, deaths, damage and stats home. Without it a player still shows up, with less in their record.

---

![NEW IN 1.7.2](https://img.shields.io/badge/NEW_IN_1.7.2-VALHEIM_1.0.21-8b1e1e?style=for-the-badge&labelColor=1b1b1b)

**This version is The Raven's Call built for Valheim 1.0.21.**

| You | It |
|---|---|
| **Update the server and your players together** | Valheim 1.0.21 uses network version 41: the server and every player's game must be on it to meet |
| **Keep your settings and records** | nothing else changes: the same settings, the same API, the same dashboard |

---

![BEFORE YOU INSTALL](https://img.shields.io/badge/BEFORE_YOU_INSTALL-READ_THIS-b8860b?style=for-the-badge&labelColor=1b1b1b)

| | If… | Then… |
|---|---|---|
| 🖥️ | you run a **dedicated server** | install The Raven's Call on the server only. It is built and tested for dedicated servers; it loads on a listen-server host, but that has not been tested. |
| 🐦‍⬛ | you want **kills, deaths, damage, fish and stats** | each player installs [WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies). Without it a player still gets joins, leaves, playtime, biome discoveries and their census count, but their kills, deaths and build, craft and harvest counters stay at zero. |
| 🔐 | you open the **dashboard** to other machines | it answers only the server machine itself unless you change that. The API has no login and hands every player's stats, skills, titles and death positions to anyone who can reach it: set `HttpApiToken` and keep the port behind a firewall. |
| ☁️ | you **push to a hosted dashboard** | whoever holds the read token sees every player's stats and death positions, and the position of every portal, bed and ward, in effect where the bases are. Raven Iron's own receiver is for its own servers: deploy your own. With `PushUrl` empty, the default, nothing leaves the server. |
| 📄 | you once put **your own `theravenscall.html`** in `BepInEx/config/TheRavensCall/` or next to the DLL | delete it. The page ships inside the mod, and an old loose copy overrides it and cannot read the current API. The server log names the file. |

---

![I](https://img.shields.io/badge/I-THE_LEDGER-c9a227?style=for-the-badge&labelColor=1b1b1b)

**Every Viking's whole saga, kept on the server.**

| | |
|---|---|
| 🔹 **Deaths** | the cause, named: a creature, a boss, another player, fire, frost, poison, spirit, drowning or a fall; and each player's last ten deaths, with the killer, the biome and the place |
| 🔹 **Boss kills** | credited to everyone who stood near the kill, not only the one who struck last |
| 🔹 **Milestones** | kills at 1, 10, 100, 500, 1000 and beyond; deaths at 1, 5, 10, 25, 50, 100 and beyond |
| 🔹 **Biome discoveries** | per player, so a first step into the Mistlands is announced once, ever |
| 🔹 **Raids** | from the moment one starts to the moment it ends |
| 🔹 **Combat** | damage dealt and taken, blocks, parries and damage blocked |
| 🔹 **Every vanilla stat** | all of the player's own in-game Stats screen, about 205 counters, from jumps and portals to distance travelled |
| 🔹 **Skills** | the level and progress of every skill a player has raised |
| 🔹 **The work** | fish caught · building placed, removed and repaired · items crafted, upgraded and repaired · resources harvested, per type · food and potions · bosses summoned · guardian powers used |
| 🔹 **Playtime** | lifetime, kept across restarts |

<details>
<summary>📖 <b>Read the saga</b> — why the server needs a crow</summary>

**A dedicated server only relays the world.** Combat, damage and deaths are worked out on the game of the player who owns that part of the world, so a server on its own never sees a kill. WhereTheCrowFlies hooks those moments on each player's game and sends them to the server every ten seconds. The Raven's Call checks each report against the players actually connected before it credits anything.

**Nothing earned before is lost.** The vanilla stats and skills arrive as a full snapshot, not as changes, so the moment a player's game reports, everything they had earned before the mod was installed is filled in. A lost packet cannot make them drift.

**Without the crow** a player still shows up with joins, leaves, playtime and biome discoveries, and the census still counts what they have built. On a vanilla or unmodded server, WhereTheCrowFlies stays silent: no errors, no delay, nothing in the log.

**Gear tier** is in every record, but stays at zero on a dedicated server today.

</details>

---

![II](https://img.shields.io/badge/II-WHERE_THE_NEWS_GOES-1f4e79?style=for-the-badge&labelColor=1b1b1b)

**One event, told five ways.**

| | |
|---|---|
| 🔹 **Discord** | deaths, boss kills, kill and death milestones, biome discoveries and titles post the moment they happen, as coloured embeds |
| 🔹 **The Chronicle** | every event as one JSON line on disk, a new file each day: a permanent, grep-friendly history |
| 🔹 **The dashboard** | a live page in any browser, with a feed of the latest events |
| 🔹 **The bot export** | one JSON file of every player, refreshed every ten seconds, made to be read by a Discord bot |
| 🔹 **The server log** | each line with the day and how many are online, if you like |

<details>
<summary>📖 <b>Read the saga</b> — what goes where, and in what voice</summary>

**Discord gets the big moments.** Joins, leaves, raids, season starts and ends, lore lines, boss world-event messages and the server's start and stop go to the Chronicle, the dashboard feed and the server log, never to Discord. Each kind of player event can be switched off on its own.

**The narration has a Norse voice.** Each event is told from a set of flavour templates, picked at random. Turn `EnableNarrativeMode` off for plain fixed lines.

**Your own words, too.** Write a message per boss in the `WorldEvents` settings, and it is recorded after that boss falls. Fill `lore.txt` with lines of lore, set an interval, and the raven recites one at random. A few lines of Norse myth are there to start with.

**The online count is honest.** It counts the players actually in the world: a player who just left is not counted, and neither is one still at the password prompt. A player whose game crashed counts until the server drops the dead connection, up to half a minute, or a minute and a half on a crossplay server.

</details>

---

![III](https://img.shields.io/badge/III-THE_DASHBOARD-5b2c83?style=for-the-badge&labelColor=1b1b1b)

**The whole realm on one page, served by the server itself.**

| | |
|---|---|
| 🔹 **World overview** | who is online now (or who was seen last), an all-time leaderboard by kills, deaths, boss kills or playtime, the server's combined boss progress, and the latest deaths |
| 🔹 **Season and feed** | the running season's standings, and the latest events, newest first, filterable by type |
| 🔹 **World panel** | what stands in the world, a Builders leaderboard, a portal directory, and the beds and wards |
| 🔹 **Every player, six tabs** | Combat, Death, Progression, Crafting, Build and Raw, for everyone who has ever joined |
| 🔹 **API token** | enter it once in the page's settings (the gear, top right), and the browser keeps it |

<details>
<summary>📖 <b>Read the saga</b> — what a server can and cannot show</summary>

**It lives at `http://localhost:2112` on the server machine,** and the page ships inside the mod. To reach it from another PC, see the Toolbox below.

**No health, inventory or position for a player who is online.** A dedicated server never has them: they live on the player's own game. The dashboard shows each player's stored record instead: totals, titles, skills, milestones, the death history and everything WhereTheCrowFlies reported. The only positions it keeps are where players died.

**With a token set,** the page opens its settings panel for you the first time it is refused. The feed and season panels say "Needs the API token (Settings)." until you enter it.

**Just the files?** `EnableHttpServer = false` turns the page and the API off. The bot export, the Chronicle, Discord and the hosted push keep running.

</details>

---

![IV](https://img.shields.io/badge/IV-TITLES-8b1e1e?style=for-the-badge&labelColor=1b1b1b)

**Earn a name, then choose which one the raven calls you by.**

| | |
|---|---|
| 🔹 **Creature titles** | 100, 500 and 1000 kills of a creature family, from *Boar Hunter* to *Alpha of Alphas* |
| 🔹 **Boss titles** | one for each god felled: *Stag Breaker*, *Bane of the Swamp*, *The Ashen* and the rest |
| 🔹 **Boss count** | *Boss Hunter*, *World Breaker* and *God Ender* as the boss kills add up |
| 🔹 **Slayer of Gods** | for felling all seven |
| 🔹 **`/title`** | lists what you have earned; `/title Wolf Hunter` picks one; `/title clear` shows none |
| 🔹 **`/titles`** | a small window with your titles as buttons: click instead of typing |
| 🔹 **Admins** | `ravenscall title <player>` lists, sets or clears anyone's title, online or not |

<details>
<summary>📖 <b>Read the saga</b> — where a title shows, and where it does not</summary>

**The title is the server's name for you:** in its Discord and Chronicle narration, on the dashboard and in the bot export. Nothing changes on the in-game nameplate.

**Your first title wears itself.** After that you choose: titles you earn later are added to your list and wait there until you pick one. A cleared title stays cleared.

**Only earned titles.** Neither a player nor an admin can set a title that player has not earned. The player commands need WhereTheCrowFlies on that player's game and `AcceptClientReports` on. The admin command works from the server console with no client mod, or from an admin's game with WhereTheCrowFlies. A change shows on the dashboard and the export at the next refresh.

</details>

---

![V](https://img.shields.io/badge/V-SEASONS-2e5e3a?style=for-the-badge&labelColor=1b1b1b)

**Wipes and eras, each with its own winners.**

| | |
|---|---|
| 🔹 **`ravenscall season start [name]`** | opens a season; the dashboard ranks players by what they do from now on |
| 🔹 **`ravenscall season end`** | closes it, archives its Chronicle into its own folder and writes the season's summary |
| 🔹 **Season names in the log** | `AppendSeasonNameToMessages` puts the season's name in front of each server log line |

<details>
<summary>📖 <b>Read the saga</b> — how a season runs</summary>

**Run it from the server console,** or from an admin's game with WhereTheCrowFlies installed (F5 console): the server checks its admin list first. Leave the name off and the season is called `Season_` plus today's date. The name becomes the Chronicle's season folder, so trailing periods and spaces are trimmed.

**One season at a time.** `season start` refuses while a season is running and names it: end it first.

**Standings are deltas.** At the start the server notes each player's counters, so the season panel shows what was done during the season, not lifetime totals.

</details>

---

![VI](https://img.shields.io/badge/VI-THE_WORLD_CENSUS-b8860b?style=for-the-badge&labelColor=1b1b1b)

**What stands in the world, and who put it there.**

| | |
|---|---|
| 🔹 **The count** | portals, beds, wards, ships, carts, chests and crafting stations, player-built against the total |
| 🔹 **Builders** | every player ranked by what they have standing right now |
| 🔹 **Portal directory** | each portal's tag, kind, builder, whether it is connected, and where it stands |
| 🔹 **On every player** | the Build tab shows what that player has standing in the world |

<details>
<summary>📖 <b>Read the saga</b> — what it costs the server</summary>

**A short hitch every few minutes.** The census walks the loaded world's objects on a timer, every 5 minutes by default (`CensusIntervalMinutes`). A real server with about 1.1 million objects finishes in under half a second. Set it to `0` to turn it off; the dashboard then says so in one line.

</details>

---

![VII](https://img.shields.io/badge/VII-FOR_YOUR_DISCORD_BOT-2e5e3a?style=for-the-badge&labelColor=1b1b1b)

**Everything a bot needs to answer questions about your players, without touching the game.**

| | |
|---|---|
| 🔹 **`BarrkBOT_data1.json`** | every player's full record, rewritten every ten seconds |
| 🔹 **`BarrkBOT_data2.json`** | the game's items, recipes and buildables, written once per boot |
| 🔹 **Over FTP or HTTP** | the files sit in the server's config folder; the same player data is live at `/api/state` |
| 🔹 **Keyed by name** | `players` is keyed by each player's exact in-game name, ready for a leaderboard |

<details>
<summary>📖 <b>Read the saga</b> — made for BarrkBOT, open to any bot</summary>

**Two plain files, no new ports.** A bot that already reaches the server over FTP or SFTP needs nothing else. Deleting `BarrkBOT_data1.json` does no harm: it is back on the next refresh. Renaming either file breaks a bot that looks for it. The Toolbox has the file's shape and a short example.

</details>

---

![VIII](https://img.shields.io/badge/VIII-RENTED_HOSTS-1f4e79?style=for-the-badge&labelColor=1b1b1b)

**On Nitrado or G-Portal, with no port to open, the dashboard still reaches you.**

| | |
|---|---|
| 🔹 **Push, not listen** | the server sends its data out over HTTPS to a small receiver site, which serves the same dashboard at a public address |
| 🔹 **Your own receiver** | deploy your private copy of `hosting/netlify/` from the [GitHub repo](https://github.com/RavenIron-Games/TheRavensCall) |
| 🔹 **Off by default** | with `PushUrl` empty the server stays entirely local |
| 🔹 **Shows when it went quiet** | the hosted page says how long ago the server last reported, and greys out when it falls silent |

<details>
<summary>📖 <b>Read the saga</b> — what hosting publishes, and where it has run</summary>

**A rented server gives you the game's ports, a web panel and FTP:** no shell, and no way to open the dashboard's port. The push needs only outbound HTTPS, the same way out the Discord webhook already uses.

**What leaves the box.** A read-token holder sees the roster with every player's stats, skills, titles and death positions; the feed and season standings; and the census, with the rough position of every portal, bed and ward. Only for that server, and only with that server's token.

**One small difference.** On the hosted page, fish and resource names are shown as tidied-up game names instead of their real ones.

**Where it has run.** A Windows and a Linux dedicated server have pushed to a receiver over HTTPS, and a real Windows server pushes to Raven Iron's receiver at `https://trc.ravenirongames.com`. It has not yet been run on a rented host.

</details>

---

![THE TOOLBOX](https://img.shields.io/badge/THE_TOOLBOX-QUICK_START_%C2%B7_COMMANDS_%C2%B7_HOW--TO-1b1b1b?style=for-the-badge&labelColor=8b1e1e)

**First ten minutes:** install on the server with your mod manager (or put `TheRavensCall.dll` in `BepInEx/plugins/`) → start the server once → open `http://localhost:2112` on the server machine → ask your players to install WhereTheCrowFlies → paste a Discord webhook into `WebhookUrl` if you want the news posted.

| Command | | Address | |
|---|---|---|---|
| `/title` | your earned titles | `http://localhost:2112` | the dashboard |
| `/title <name>` · `/title clear` | pick one · show none | `/api/state` | every player (the same JSON as `BarrkBOT_data1.json`) |
| `/titles` | the title window | `/api/activity` | the event feed and season standings |
| `ravenscall season start [name]` | open a season (admin) | `/api/census` | the world census |
| `ravenscall season end` | close it (admin) | `/api/gamedata` | items, recipes and buildables |
| `ravenscall title <player>` | that player's titles (admin); add a title to set it, or `clear` | `/api/health` | always open, no token |

The player commands come with WhereTheCrowFlies. The `ravenscall` commands run in the server console, or from an admin's game with WhereTheCrowFlies installed.

<details>
<summary>🌐 <b>Open the dashboard from another PC</b></summary>

The server answers only itself by default. Set `HttpBindAllInterfaces = true` together with `HttpApiToken` (a long random value), restart, and keep port `2112` (`HttpServerPort`) behind a firewall. With a token set, `/api/state`, `/api/gamedata`, `/api/activity`, `/api/census` and `/api/pins` need `?token=<value>` or an `X-Api-Token` header; the page and `/api/health` stay open. In the browser, enter the token once in the page's settings (the gear, top right).

</details>

<details>
<summary>🪝 <b>Post the news to Discord</b></summary>

Make a webhook in your Discord channel's settings and paste its address into `WebhookUrl` under `[Discord]` in `BepInEx/config/com.raveniron.theravenscall.cfg`. Deaths, boss kills, kill and death milestones, biome discoveries and titles then post as embeds. With `WebhookUrl` empty nothing is posted, and everything else works the same.

</details>

<details>
<summary>🤖 <b>Read the export from a bot</b></summary>

The files are `BepInEx/config/TheRavensCall/BarrkBOT_data1.json` (players) and `BarrkBOT_data2.json` (game data). The players file looks like this:

```json
{
  "generated_at": "2026-08-17T04:12:00.000Z",
  "world_name": "Midgard",
  "day": 214,
  "online_count": 6,
  "raid_active": false,
  "raid_type": "",
  "players": {
    "Thorium Wubarrk": {
      "name": "Thorium Wubarrk",
      "kills_narrative": 341,
      "deaths_narrative": 12,
      "active_title": "Wolf Hunter",
      "...": "..."
    }
  }
}
```

| Field | What it is |
|---|---|
| `kills_narrative` · `kills_observed_lifetime` | the kill count titles and milestones use · every verified kill |
| `deaths_narrative` · `deaths_lifetime` | the same pair for deaths |
| `active_title` · `titles_earned` | the title the player shows · every title they have |
| `bosses_defeated` | short keys: `eikthyr`, `elder`, `bonemass`, `moder`, `yagluth`, `queen`, `fader` |
| `session_kills` · `damage_dealt_session` · `damage_taken_session` · `blocks_session` · `parries_session` · `damage_blocked_session` | reset each time the player connects |
| `playtime_seconds_lifetime` | kept across restarts; divide by 3600 for hours |
| `vanilla_stats` | the player's own Stats screen, keyed by Valheim's names; empty until their game has reported once |
| `skill_levels` · `skill_progress` | 0–100 and 0–1 per skill; a skill missing from both has never been raised |
| `death_history` | the last ten deaths: time, killer, place and biome |
| `builds_*` · `items_*` · `resources_harvested` · `consumables_eaten` · `bosses_summoned` · `guardian_powers_used` · `caught_fish` · `biomes_discovered` · `creature_kills` | lifetime counters and lists |

A minimal read in Node.js:

```js
import { readFileSync } from 'node:fs';

const data = JSON.parse(readFileSync('BarrkBOT_data1.json', 'utf8'));
const player = data.players['Thorium Wubarrk'];

if (player) {
  console.log(`${player.name}: ${player.kills_narrative} kills, ` +
    `${player.deaths_narrative} deaths, title: ${player.active_title || 'none'}`);
}
```

The full field list is in `docs/API.md` in the [GitHub repo](https://github.com/RavenIron-Games/TheRavensCall).

</details>

<details>
<summary>☁️ <b>Host the dashboard for a rented server</b></summary>

Raven Iron's receiver is for its own servers only, so deploy your own private copy of `hosting/netlify/` from the [GitHub repo](https://github.com/RavenIron-Games/TheRavensCall); its README has the receiver's side.

1. **Make two tokens:** run `openssl rand -hex 24` twice, one for `HttpApiToken` (unless you already have one of 24 characters or more) and one for `PushToken`.
2. **Set** `HttpApiToken` (24 characters or more) under `[Companion]`, and `PushUrl`, `PushToken` and `PushServerId` under `[Push]`, in `com.raveniron.theravenscall.cfg`. The id is 1 to 32 characters of `a-z`, `0-9` and `-`.
3. **Register the id** with both tokens' sha256 hashes in the receiver's `TRC_SERVERS` environment variable, then **redeploy** the receiver: a change in the Netlify UI does nothing until the site is redeployed.
4. **Open** `https://<your receiver>/s/<id>/`. Raven Iron's own runs at `https://trc.ravenirongames.com`.

**How often.** The push rides the same tick as the export, so it rounds up to a multiple of `StatsPushIntervalSeconds` (10 s by default): `PushIntervalSeconds = 15` pushes every 20 s. `PushHeartbeatMinutes` pushes everything even when nothing changed, so the page knows the server is alive.

**Changed the API token?** Restart the server and register the new hash: the pushed hash only changes at a restart.

</details>

<details>
<summary>🚨 <b>The hosted page shows nothing</b></summary>

Read the BepInEx log; on a rented host, pull it over FTP. A working push logs one `Push enabled: server_id='…'` line at boot, then stays quiet.

- `Push disabled:` names the setting that is missing or malformed.
- `Push failed:` means the receiver could not be reached; the mod keeps retrying.
- `Push rejected:` names one of four refusals, each retried every 15 minutes:
    - **401**: the push token was rejected. Check `PushToken` and `PushServerId` against the receiver's registry.
    - **409**: the receiver has a different `HttpApiToken` hash for this id. Re-register `read_token_sha256` and redeploy.
    - **422**: the receiver refused the bundle. Set `HttpApiToken` to 24 characters or more.
    - **413**: the bundle is over the receiver's 2 MB limit.

Until the first push lands the page reads "Registered, waiting for `<id>` to report"; "server silent since …" means no push has arrived for three heartbeats.

</details>

<details>
<summary>🗑️ <b>Take a server off a hosted dashboard</b></summary>

Send `DELETE /s/<id>/api` to the receiver with the write token in the `Authorization: Bearer` header. It answers `{"deleted":true}`, removes the stored copy, and the page reads "no data" again. Then remove the id from the receiver's registry and redeploy, so the tokens are dead too. Clearing `PushUrl` on the server stops new pushes at its next restart.

</details>

<details>
<summary>📦 <b>Move or rebuild the server</b></summary>

Copy the folder `BepInEx/config/TheRavensCall/` and the settings file `BepInEx/config/com.raveniron.theravenscall.cfg`. The folder holds:

| | |
|---|---|
| `players/` | one record per player |
| `Chronicle/` | the daily Chronicle files, and each season's folder |
| `seasons.json` · `season_baseline.json` | the seasons, and each player's counters at the running season's start |
| `event_feed.json` | the dashboard feed, so it survives a restart |
| `player_ids.json` | player ids and names, used to name a builder in the census |
| `lore.txt` | your lore lines, one per line |
| `BarrkBOT_data1.json` · `BarrkBOT_data2.json` | the bot export; rebuilt after a boot, no need to copy |

</details>

---

![MULTIPLAYER](https://img.shields.io/badge/MULTIPLAYER-ONE_SERVER%2C_EVERY_SAGA-1f4e79?style=for-the-badge&labelColor=1b1b1b)

| Works for players with no mod | Needs The Raven's Call on the server | Needs WhereTheCrowFlies on the player's game |
|---|---|---|
| joins, leaves and playtime · biome discoveries · the census count of what they built · seasons, and titles set by an admin from the server console · the dashboard, Discord, the Chronicle and the bot export | everything: The Raven's Call runs only there | kills, deaths and their causes · damage, blocks and parries · fish · the vanilla stats and skills · build, craft and harvest counters · `/title` and `/titles` · `ravenscall` commands from a game |

**Update together.** The server and every player's game must run the same Valheim version, so update them together. WhereTheCrowFlies on a server without The Raven's Call stays silent.

---

![COMPATIBILITY](https://img.shields.io/badge/COMPATIBILITY-PLAYS_WELL_WITH_OTHERS-2e5e3a?style=for-the-badge&labelColor=1b1b1b)

| Mod or place | |
|---|---|
| **[WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies)** | The companion: it carries each player's combat and stats to the server. |
| **BarrkBOT and other Discord bots** | Read the export files, or `/api/state` over HTTP. |
| **Nitrado · G-Portal and other rented hosts** | Use the hosted push; the local dashboard port cannot be opened there. Not yet run on a rented host. |
| **Linux dedicated servers** | Has run on one, the hosted push over HTTPS included. |
| **Listen servers (hosting from the game)** | Loads, but not tested. |

---

![SETTINGS](https://img.shields.io/badge/SETTINGS-BEPINEX_CONFIG-b8860b?style=for-the-badge&labelColor=1b1b1b)

All in `BepInEx/config/com.raveniron.theravenscall.cfg` on the server, and all safe to leave alone.

<details>
<summary>🔒 <b>On the server</b> — the server's own file decides</summary>

| Section | Setting | What it does |
|---|---|---|
| Events | `EnablePlayerDeath` · `EnableBossKill` · `EnablePlayerJoin` · `EnablePlayerLeave` · `EnableBiomeDiscovery` · `EnableKillMilestone` · `EnableDeathMilestone` · `EnableGearTier` · `EnableTitleEarned` | switch each kind of event's narration on or off |
| Events | `EnableRaid` | record raid starts and ends in the Chronicle and the feed (the raid banner works either way) |
| Format | `ShowDayNumber` · `ShowOnlineCount` · `MessagePrefix` | the day, the online count and a prefix on each server log line |
| Log | `EnableChronicleLog` · `LogServerStartStop` | write the daily Chronicle; record the server's start and stop |
| Bosses | `BossCreditRadius` | how near a boss's death, in metres, a player must be to share the kill |
| Discord | `WebhookUrl` | where to post the news; empty posts nothing |
| Narrative | `EnableNarrativeMode` | the Norse flavour templates, or plain lines |
| Seasons | `AppendSeasonNameToMessages` | the season's name in front of each server log line |
| WorldEvents | `Eikthyr` · `TheElder` · `Bonemass` · `Moder` · `Yagluth` · `TheQueen` · `Fader` | your own message after each boss falls; empty for none |
| Lore | `LoreBroadcastIntervalMinutes` | recite a random `lore.txt` line this often; 0 is off |
| Companion | `EnableHttpServer` · `HttpServerPort` | the dashboard and API, and their port |
| Companion | `HttpBindAllInterfaces` · `HttpApiToken` | answer other machines too; the token the API asks for |
| Companion | `StatsPushIntervalSeconds` | how often the records and the bot export refresh |
| Companion | `EventFeedCapacity` | how many recent events the feed keeps; 0 turns it off |
| Census | `CensusIntervalMinutes` | how often the census runs; 0 turns it off |
| Push | `PushUrl` · `PushToken` · `PushServerId` · `PushIntervalSeconds` · `PushHeartbeatMinutes` | the hosted dashboard: where, as whom, how often |
| Combat | `AcceptClientReports` | take reports from WhereTheCrowFlies (off also turns off the player's `/title`) |
| Combat | `LogCombatReports` | log every report, for checking a rollout |

</details>

🖥️ **Nothing to set on a player's game:** The Raven's Call runs on the server only. WhereTheCrowFlies has its own settings.

---

![CREDITS](https://img.shields.io/badge/CREDITS-THANK_YOU-1b1b1b?style=for-the-badge&labelColor=c9a227)

**Needs:** [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) `5.4.2360` on the server · **Companion:** [WhereTheCrowFlies](https://valheim.thunderstore.io/package/RavenIron/WhereTheCrowFlies) on each player · **Source:** [GitHub](https://github.com/RavenIron-Games/TheRavensCall)

*The Raven's Call is a RavenIron mod.* Every Raven Iron mod is free, and stays free: all of it, always. Nothing is held back for patrons, and nothing ever will be. If you'd like to help cover server hosting and test hardware:

- **Website**: <https://ravenirongames.com/>
- **Patreon**: <https://www.patreon.com/cw/RavenIronGames>
- **Discord**: <https://discord.gg/AGKDEurAVa>, a channel per mod, and where the testing happens
