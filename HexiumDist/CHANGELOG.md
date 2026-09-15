<div align="center">

# 📜 Changelog
**The Raven's Call** — RavenIron

</div>

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
