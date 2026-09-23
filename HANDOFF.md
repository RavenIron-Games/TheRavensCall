# TheRavensCall — handoff (2026-08-19 update, originally 2026-08-17)

**Status: v1.7.1 "The Raven Missed Something" IN PROGRESS 2026-09-23 (branch `fix/leave-online-count-1.7.1`, PR #22, not yet merged; LIVE on Storm10 with build 4b40afe3: logout-to-menu leave and shutdown leave both write no count, rows `online_count` 0; dead-connection leave waits the 90 s crossplay timeout; two-player shutdown not run) — the leave line's `(N online)` no longer counts the player who left: `Plugin.OnlineCount()` counts `GetConnectedPlayers()` with an open session (`Patch_PlayerJoin.HasOpenSession`) for both the suffix and the Chronicle/feed `online_count`. Version 1.7.1; DLL not rebuilt (the cut).**

**Status: v1.7.0 CUT + PUBLISHED 2026-09-23 as "The Raven Gives Names" (PR #21 MERGED = main `c8894ab`; tag `v1.7.0` on the release commit; shipped DLL md5 `19d79acad1d5c4da35685fbe53fc2003`, 279,552 bytes, a clean build of the merge commit, byte-identical on a second clean build and IL-identical to the build that ran on Storm10; the STORE UPLOAD is the owner's; paired with WhereTheCrowFlies v1.2.0, cut the same day) — the title picker. `PlayerRecord.ActiveTitle` had only ever been set once, the moment a title was first earned; this lets a player choose which earned title shows. Wire addition: a new `RavensCall_EventReport_V2` event type 13 (`TitleRequest` — payload `playerName`, `byte op` 1=List/2=Set/3=Clear, `title`) received by `EventReportReceiver.HandleTitleRequest` in `Saga.cs`, which always answers the requesting peer with a new reply-only RPC `RavensCall_TitleReply_V1` (schema 1: `byte kind` 1=ok/2=refused, `text`, then `int count` + `title` × count + `active` — every reply carries the player's full earned list and active title, not just list/set/clear's line, so WhereTheCrowFlies' title panel is always fresh after any op) that this mod sends but never registers (`WhereTheCrowFlies` 1.2.0+ registers the receiver and draws the panel from it). The List/Set/Clear rules live on `TitleSystem` (new `ListTitles`/`SetTitle`/`ClearTitle`, sharing one earned-titles-only check), used by both the RPC path and a new `ravenscall title <player> [<title>|clear]` admin console subcommand (no client mod on the server console; an admin's own game client still goes through WhereTheCrowFlies' `ravenscall` routing stub, like the season commands). `TitleSystem.EarnTitle` now self-activates only a player's FIRST title, so a `/title clear` is not undone by the next milestone. No narration, Chronicle line, Discord post or `/api/activity` row for a title change — logged server-side only, by design (non-goal for this release). Docs, changelog and manifest bumped to 1.7.0; `HexiumDist/plugins/TheRavensCall.dll` NOT rebuilt (that happens at the cut). Paired with `WhereTheCrowFlies` 1.2.0 (`feat/title-picker-1.2.0`, built at the same time, separate repo).**

**Status: v1.6.1 CUT + PUBLISHED 2026-09-22 as "The Raven Guards the Season" (PR #20 MERGED = main `86dc540`; tag `v1.6.1` on the release-docs commit; GitHub release https://github.com/RavenIron-Games/TheRavensCall/releases/tag/v1.6.1, full and Latest, asset `TheRavensCall-v1.6.1.zip`; shipped DLL md5 `7bd20ab0eae86cfe9dc3e4d585dd6d0a`, 275,968 bytes, a clean build of the merge commit, byte-identical on a second clean build, booted on Storm10; pre-publish pass 13 agents, judge CUT with no fixes; the STORE UPLOAD is the owner's; the in-game refusal check remains his). The change: `ravenscall season start` refuses while a season is running instead of replacing it in place (`SeasonSystem.StartSeason` returns `bool`; the console names the running season and `season end` as the next step); version 1.6.1 in Saga.cs/manifest/README badge, a `[1.6.1]` changelog entry that also records the README FAQ (PR #19, merged); Storm10 boot check clean (`docs/TESTPLAN-storm10-1.6.1-2026-09-22.md`), the console refusal itself NOT RUN (needs an admin client). HexiumDist DLL NOT rebuilt (the cut). Before that: v1.6.0 CUT + PUBLISHED 2026-09-22 as "The Raven Carries Word" (tag `v1.6.0` on the release-docs commit; GitHub release https://github.com/RavenIron-Games/TheRavensCall/releases/tag/v1.6.0, full and Latest, asset `TheRavensCall-v1.6.0.zip`; shipped DLL md5 `334d21ad2c0870933154cdf5d141bc15`, 274,944 bytes, a clean build of main at `7d2742f`, byte-identical on a second clean build, differing from the gate build `1799895d` only by the embedded page; pre-publish pass 25 agents, judge CUT, four changelog wording fixes applied; the STORE UPLOAD is the owner's). Before that: 1.6.0 GATE PASSED 2026-09-22 — the receiver is DEPLOYED on the owner's word as `https://theravenscall-dash.netlify.app` (RavenIron Games team, project `5364e09f`, `TRC_SERVERS` = `storm10` + `linuxtest`, hashes only; the team default made it Private until the owner set it Public), and the WSL2 Linux 1.0.15 server pushed to it over https (`docs/TESTPLAN-local-1.6.0-2026-09-22.md` steps 8–9). PR #16 (the receiver tolerates a `-df` ETag suffix) and PR #17 (the page stores the bare sha, because the edge drops any suffixed `If-None-Match` before the function sees it) are MERGED = main `fffead2`, no open PRs; the current production deploy is `6ab2f01f`, and a browser-style read on the live site (gzip, `"<sha>-df"` back, the bare sha echoed) returns `304`. Every further deploy needs the owner's explicit word (15 credits each). TARTARUS REGISTERED 15:09 2026-09-22 on the owner's word (his Windows dedicated server on the always-on host): a fresh token pair (files in the session scratchpad only), `tartarus` added to `TRC_SERVERS`, production deploy `6ab2fc81` (functions rebuilt, now the current deploy), proven live 8/8 (read as tartarus → `no_data`, storm10's token refused, a throwaway push stored and read back byte-equal, health `age 0 / stale_after 1800`, then `DELETE` → clean); the server side (the 1.6.0 gate build `1799895d` plus the `[Push]` config) was the owner's hands via a zip handed to him, and TARTARUS IS PUSHING since 22:20 UTC 2026-09-22: the first real hosted server (state day 161, the boot feed row, a census of 1,098,604 objects in 370 ms), heartbeats landing on the 600 s clock, health age counted from the receiver's own clock. `netlify logs --source functions` proved incomplete (it listed none of the pushes health confirmed), so it cannot prove an absence. Scope revision 7 = PR #18 MERGED = main `7427928` and DEPLOYED on the owner's word as `6ab2f6e8` (the current production deploy): a static landing page at the site root (the bare hostname had answered Netlify's default 404) and a `302` from `/s/` to it, proven under `netlify dev` and then live, 12/12 both times (`docs/TESTPLAN-local-1.6.0-2026-09-22.md`, "Landing page"). Still the owner's: ~~the custom hostname~~ (DONE 2026-09-22 on the owner's word: `https://trc.ravenirongames.com` is the site's primary domain, set with `netlify api updateSite`; the `NETLIFY` record appeared in the team's Netlify DNS zone by itself, the existing `*.ravenirongames.com` certificate covers it, and the 12 routing checks pass on the new name; the `*.netlify.app` name keeps working beside it, nothing paid was added), the spend cap, the Firewall rule, the 24-hour credit read, the release name, pointing Storm10 at the live site, the DLL refresh at the cut. PR #15 MERGED = main `58f8652` (`feat/push-1.6.0-impl`, four commits ending `6bee859`: the mod's `[Push]` section and `PushClient.cs`, the receiver under `hosting/netlify/`, the page's hosted mode, the docs; two review rounds, 16 must-fixes and 18 should-fixes applied and re-verified; the local test plan `docs/TESTPLAN-local-1.6.0-2026-09-22.md` passes §8 steps 1–7 and 10 on Storm10 and on the WSL2 Linux 1.0.15 server; scope revision 6 dropped the `/s/<id>` 301 rule because Netlify matches rules regardless of a trailing slash), MERGE ON THE OWNER'S WORD. Still the owner's: the Netlify deploy and the hostname (§8 step 8; `trc.ravenirongames.com`, decided later the same day), then the Linux https push to it (step 9, the release gate), the release name, the packaged DLL refresh at the cut. Storm10 was left running on its original config with the final build staged (md5 `1799895d`), behaving as 1.5.0. Before that: 1.6.0 SCOPE ACCEPTED 2026-09-22 — `docs/SCOPE-1.6.0.md` (push to a hosted dashboard for rented servers such as Nitrado: the mod pushes its three envelopes outbound, a Netlify receiver under `hosting/netlify/` keeps and serves the latest copy, the page gains a hosted mode) merged as PR #13 = main `aa08c8f` after three review passes; implementation starts on the owner's word; the release gate is a boot on the WSL2 Linux dedicated server (no rented Nitrado box, the owner's call) — the mod's FIRST LINUX BOOT ran 2026-09-22 on Valheim 1.0.15 (`docs/TESTPLAN-linux-wsl2-2026-09-22.md`: 1.5.0 boots and serves, the census on a copy of Storm10's world matches the Windows run to the object, `HttpWebRequest` completes https with certificate validation on, so §3's transport stands as written; on the older 0.221.13 build that Steam's public-test branch carried the same call cannot complete a request and the census throws `MissingMethodException`, both noted there); §10 lists what the owner still decides (release name, hostname, conditional fetches in 1.6.0 or 1.6.1). v1.5.0 CUT 2026-09-22 (PR #11 merged = main `9cce860`; tag `v1.5.0` on the release-docs commit right after it; GitHub release with `TheRavensCall-v1.5.0.zip`; shipped DLL md5 `47bb2d3e4622e3883acf850652aa9f5b`, 246,272 bytes, staged on Storm10). The store upload is the owner's. Scope `docs/SCOPE-1.5.0.md` (portals with a directory, plus beds, wards, ships, carts, chests and crafting stations; a new `GET /api/census`; a World panel on the dashboard); `WorldCensus.cs` is the implementation; two review passes and two pre-publish passes (48 + 19 agents) with their fixes are in; all 8 scope test steps passed on Storm10 (`docs/TESTPLAN-storm10-census-2026-09-22.md`). `manifest.json` `website_url` switched to `https://ravenirongames.com` on the owner's word after the cut (PR #12 = main `41cbe35`, same day; WhereTheCrowFlies' manifest switched alongside, its PR #5); it rides with the next cut, since the v1.5.0 zip shipped with the workers.dev link. 1.4.2 (PR #10) was never tagged on its own; v1.5.0 carries it. Last released: v1.4.1, cut 2026-09-21 (PR #9, plus what its pre-publish review turned up: the season file read back with its escapes decoded and trimmed at load, a boot that ends with no Chronicle open falls back to the default folder, a calendar-day "started today" label, the filtered-empty feed notice naming its chip, and a 64-character cap on the season folder name). v1.4.0 was cut the same day (`docs/SCOPE-1.4.0.md`; server side run live on StormTest, docs/TESTPLAN-stormtest-2026-09-21.md; client side run live on the Storm10 1.0.12 testbed with WhereTheCrowFlies 1.1.3, docs/TESTPLAN-storm10-2026-09-21.md, which found the season-folder defect 1.4.1 fixes). v1.3.0 was cut 2026-09-15 and run live on Storm10 (docs/TESTPLAN-storm10-2026-09-15.md).** RavenIron release —
a merge of two client-side Valheim mods (`SkaldSaga`, `SteveCompanionMod`) into one server-only
admin tool. Source of truth for both originals is gone from this repo; only their `.txt` dumps
remain (`25d72c2b-...txt` = SkaldSaga, `46dff3c1-...txt` = SteveCompanionMod) alongside the merged
project. Not a git repo — nothing here has commit history to inspect.

**2026-09-21: 1.4.1 cut** — PR #9 plus the pre-publish review's fixes, shipped DLL md5 `a1ed4838d0aff30148e64d0fa9e33616`; the client side of the 1.4.0 plan ran live on Storm10 (docs/TESTPLAN-storm10-2026-09-21.md). **1.4.2 follow-ups, DONE on branch `fix/followups-1.4.2` (2026-09-22, unreleased until the 1.4.2 cut)**, from the second review pass (all low-reach on a dedicated server, none a data-loss path): (1) the boot guard in `Patch_ZNetAwake.Postfix` should read `if (no season || Chronicle.CurrentLogPath == null) Chronicle.Init();` — on a listen server that hosts twice in one process with `seasons.json` cleared out of band, the second session's Chronicle stays in the ended season's folder; (2) `SeasonSystem.MetaString` should fail closed (return null) on a value with no closing quote, so a torn `seasons.json` reads as "no season" again; (3) `LoadBaseline` should compare the file's `season` field with the running season and re-snapshot on a mismatch; (4) Chronicle day rotation should test `Path.GetFileName(_logPath)` for today's date, not the whole path — a season folder named with a date suppresses that day's rollover (pre-existing since 1.4.0). The branch's own review added: the baseline's season name compared trimmed (a 1.4.0 file holds `TestSeason.` where 1.4.1+ runs `TestSeason`; without that the upgrade would re-snapshot and wipe the running season's standings), season state (including the last-ended pair) reset when `seasons.json` is missing (a file that exists but cannot be read keeps the loaded season), `seasons.json` written atomically, and a warning when its value is damaged. Boot checks: docs/TESTPLAN-stormtest-2026-09-22.md. Also still open from 1.3.1: the merchant event counted as a raid, "hands of falling", the leave line's online count.

**2026-09-21: 1.4.0 cut** — the `/api/activity` feed/season endpoint, its two dashboard panels, and three bundled fixes (raid tracking, the Chronicle's season folder plus a file-handle leak, the season-start UTC drift); PRs #6 (scope), #7 (tier 1), #8 (tier 2); server side booted live on StormTest, client-side steps pending on Storm10.

**v1.0.0's central claim was false.** It shipped believing combat was server-authoritative
(`Character.OnDeath`/`Damage` "fire there for real combat resolution" — the old `Saga.cs:16`
comment). A live day of play proved otherwise: 28 players, 0 kills/deaths/damage/fish recorded.
Root cause and fix are `KILL_TRACKING_FINDINGS.md` and `IMPLEMENTATION_HANDOFF.md` (both this
folder) — short version: those methods are owner-side simulation, the dedicated server owns no
zone with a player in it, so the patches were correct code wired to methods that never execute
here. v1.1.0 adds a server-side RPC receiver (`RavensCall_CombatReport_V1`, in this repo) fed by
a new sibling client mod, **WhereTheCrowFlies** (`/home/rohan/WubarrkCODING/WhereTheCrowFlies`,
separate repo, separate BepInEx plugin) — TheRavensCall itself is still server-only, per the
"twice-settled" rule below; see that project's own handoff for its half.

## Read this first

- **Before renaming or reshaping any export, read `BARRKBOT_CONTRACT.md` (this folder).**
  The files under `BepInEx/config/TheRavensCall/` are an interface another program
  on another machine depends on. On 20 Aug 2026 renaming the two exports served
  BarrkBOT nine hours of stale numbers with no error anywhere, because the old
  files stayed on disk and still parsed. That file says how to make the next
  change safely.
- **This mod installs on the dedicated server only.** No client build exists or is planned. If a
  future session is asked to "add a client feature," stop and confirm that's really wanted — it
  would be a second, deliberate reversal of the exact thing this session was corrected on twice
  (see "How this session actually went," below). See `[[project-ravenscall-server-side]]` memory.
- **Author identity is RavenIron**, not Wubarrk — PluginGUID `com.raveniron.theravenscall`,
  Discord `https://discord.gg/s8yUeuhCWj` (different server from the Wubarrk mods' invite). See
  `[[project-raveniron-author-group]]` memory.
- **The build tool is at `/home/rohan/.dotnet/dotnet`**, not on `PATH`. `dotnet` bare will fail;
  use the full path or add it to `PATH` first.
- **The main deliverable is `barrkbot_players.json`**, not the dashboard, not the Discord webhook.
  Everything else in this mod is scaffolding SkaldSaga/SteveCompanionMod already had; the actual
  ask was "give BarrkBOT detailed player data," and that file is the answer.

## How this session actually went (matters for judging what's solid vs. rushed)

1. User asked to combine two Valheim mods into one, primarily to feed BarrkBOT player data.
2. Digested both source files + the dashboard HTML via 3 parallel research agents, then designed
   and half-built a merge that preserved both mods' original *client-side* architecture
   (`Player.m_localPlayer` gating throughout) — i.e., a mod players would install.
3. **User corrected this twice, explicitly**, mid-build: "this needs to run completely server
   side," then "need to be SERVER ONLY" (caps in original). This was the right call and not a
   minor tweak — it meant every kill/death/damage/biome/gear-tier detection hook had to be
   rewritten from "assume it's about the locally-controlled player" to "resolve which of *all*
   connected players actually caused this," and the entire single-active-character data model
   (`SkaldData`, Companion's static fields) had to become a real multi-player store
   (`PlayerRegistry.cs`). All in-game client UI (HUD panels, title menu, lore whispers) was
   deleted outright rather than kept-but-inert, per the user's direction.
4. Self-review caught 2 wiring bugs before anything ran: `Companion` (a plain `MonoBehaviour`)
   was never `AddComponent`'d anywhere, and `LoreSystem.Tick` was defined but never called.
5. User granted 2 review agents (had earlier capped subagent use at 2 total for the whole task,
   which had already been exceeded by 1 via an earlier 3-agent research workflow — that overshoot
   was disclosed at the time, not hidden). One agent deep-reviewed `Saga.cs`, the other
   `Companion.cs`/`PlayerRegistry.cs`/the HTML — both manual/analytical, no compiler available to
   either of them at that point. Found real bugs, listed below under "Fixed this session."
6. User then said "add the tools you need to build it" — a local dotnet SDK turned out to already
   exist on disk, just off `PATH`. First real build attempt surfaced 6 compile errors (all from
   the csproj template choice — see below), fixed, then **0 warnings, 0 errors**. This was the
   first point anything in this project was verified by an actual compiler rather than manual
   read-through.
7. Hexium package built: manifest/README/CHANGELOG in the house style, `TheRavensCall-v1.0.0.zip`
   with correct forward-slash zip paths (Python's `zipfile`, since neither `zip` nor `unzip` exist
   in this environment).

**Net effect for whoever picks this up:** the C# has been through self-review, two independent
agent reviews, and a real compiler. It has **not** been run inside Valheim even once. Compiling
proves the code is well-formed; it proves nothing about whether `ZNet.Update`/`Character.OnDeath`/
etc. actually fire the way the comments claim on a real dedicated server, or whether
`AssemblyPublicizer` correctly publicized the exact private members this code calls into at
runtime (it only proves the *reference* resolved at compile time).

## Architecture

| File | What it owns |
| :--- | :--- |
| `Saga.cs` (~1650 lines) | `Plugin` (the one `[BepInPlugin]` entry point, all config, `GetConnectedPlayers`/`TryVerifyConnectedPlayer` — the server-side "who's online and where" API, ZDO-backed, NOT `ZNet.GetPlayerList()`), narrative event pipeline (`Narrate`/`FireEvent`), the dormant-but-kept `Patch_Damage`/`Patch_CharacterDeath`/`Patch_FishCatch` (owner-side methods that never fire headless — see the v1.1.0 note above), the live combat path (`CombatCredit`, `CombatReportReceiver`, `CombatEventDedup`, `CombatReportRateLimiter`), join/leave, `SessionReconcile` (closes sessions a crash/Alt-F4 leaves orphaned), the periodic poll-tick dispatcher (`Patch_ZNetUpdate`), biome polling (`BiomeAndGearTracking.CheckBiome` — position-based, live; `CheckPlayer` — Player-based, dormant), `TitleSystem`, `MilestoneTracker`, `NarrativeSystem`, `DiscordWebhook`, `Chronicle`, `SessionTracker`, `SeasonSystem`, `LoreSystem`. |
| `Companion.cs` (~1230 lines) | The HTTP server on `:2112` (`StartHttpServer`/`ProcessRequest`), the dashboard-serving logic, the `/api/state` cache (since 1.3.0 `_stateCache` is the `PlayerRegistry.BuildBarrkBotJson` string set by `PollAllPlayers` every tick and by `PrimeStateCache` at boot; the old per-row builders are gone, see `docs/API.md`), `PollAllPlayers` (the actual per-tick driver, called from `Saga.cs`'s `Patch_ZNetUpdate` — now walks `Plugin.GetConnectedPlayers()`, not `Player.GetAllPlayers()`, which is always empty here), `GetBiomeAt` (position-only, works headless) alongside the old Player-based `GetBiome`, `RecordDeathHistoryFromReport` (the RPC path's lighter death_history writer — no inventory/swim-state available, unlike the Player-based `RecordDeathHistory`), `RecipeDumper`, `ChestTracker`/`TimerTracker`/`MapPinTracker`/`BoatTracker`, `BossKeys` (prefab → canonical short-key map). Renamed from SteveCompanionMod's own `Plugin` class — it is now a plain `MonoBehaviour`, instantiated exactly once via `gameObject.AddComponent<Companion>()` in `Saga.cs`'s `Plugin.Awake()`. |
| `PlayerRegistry.cs` (260 lines) | New this session. `Dictionary<string playerName, PlayerRecord>`, one JSON file per player under `BepInEx/config/TheRavensCall/players/`, loaded at server startup, saved on dirty/interval. `BuildBarrkBotJson()` is the function that produces the actual deliverable. |
| `TheRavensCall.csproj` | net472, `Publicize="true"` on `assembly_valheim` + `BepInEx.AssemblyPublicizer.MSBuild` package (**required** — several `[HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]`-style attributes reference non-public Valheim methods by `nameof()`, which only resolves against a publicized reference; Njord's csproj, the initial template, didn't have this and failed with 6 `CS0117` errors). References are HintPath-based against the shared `../libs-Tools/` DLLs, not NuGet, except the publicizer package itself. |
| `theravenscall.html` (~1150 lines) | The dashboard, rebuilt from scratch in 1.3.0 against `docs/API.md`: one file, no build step, no network request except the mod's own API; a roster plus six per-player tabs (Combat, Death, Progression, Crafting, Build, Raw); a settings panel keeping a base-URL override and the API token in `localStorage` (sent as `X-Api-Token`); embedded in the DLL as the final serving fallback after the two disk override locations, with a `<meta name="theravenscall-api">` marker the server checks to warn about pre-1.3.0 disk copies. See `docs/DASHBOARD.md`. (Until 1.2.4 this was the renamed SteveCompanion page, 4468 lines.) |
| `HexiumDist/` | `manifest.json` (v1.2.1), `README.md`, `CHANGELOG.md`, `icon.png` (placeholder — user is doing the real one), `plugins/TheRavensCall.dll` (refreshed), `TheRavensCall-v1.2.1.zip` (current — older versioned zips are deleted on request rather than kept as rollback baselines, per the 2026-08-19 session). |

## The BarrkBOT contract

`BepInEx/config/TheRavensCall/barrkbot_players.json`, rewritten every `StatsPushIntervalSeconds`
(config, default 10s). One object, `players` keyed by exact in-game display name. Full schema and
a bot-side read example are in `HexiumDist/README.md` under "BarrkBOT & Discord AI Bot
Integration" — that section is the canonical reference, don't let it drift from what
`PlayerRegistry.ToJson`/`BuildBarrkBotJson` actually emit if either changes.

BarrkBOT itself (`/home/rohan/WubarrkCODING/WindowsDEV/Discord-BarrkBOT/v5.7 - Final`) was **not modified** —
user explicitly scoped this to "mod only" when asked. It does not yet read this file. Wiring
BarrkBOT to actually consume `barrkbot_players.json` (replacing its current Discord-message-
scraping approach in `src/actions/valheimEvents.js`, which only tracks deaths+playtime from
parsing `wonderland-world-feed` channel embeds) is a real, separate follow-up task, not done here.

## Fixed this session (via the 2-agent review + self-review, all before the first real build)

- `Companion` was never `AddComponent`'d — the entire stats/HTTP subsystem would have been dead
  code at runtime. Fixed in `Plugin.Awake()`.
- `LoreSystem.Tick` was defined, never called. Fixed in `Patch_ZNetUpdate.Postfix`.
- Boss-kill dedup (`_recentBossKeys`, a permanent `HashSet<string>` keyed on prefab+position) never
  expired — would have silently blocked crediting a resummoned-and-refought boss forever after its
  first kill. Removed outright: the per-Character-instance-ID re-entrancy guard added alongside it
  (`_recentDeaths`, 3s window) already covers genuine double-fire, and a resummoned boss is a fresh
  instance ID that must be allowed through.
- Session stats (`SessionKills`/`SessionDmgDone`/`SessionDmgTaken`) never reset on rejoin — would
  have accumulated for the server's entire uptime instead of resetting per session. Fixed in
  `Patch_PlayerJoin.Postfix`.
- Player-death and non-boss-kill paths had no re-entrancy guard at all (only the boss path did,
  and that one was itself wrong — see above). Added one unified guard (`AlreadyProcessed`, keyed
  by `Character.GetInstanceID()`) covering all three paths in `Patch_CharacterDeath`.
- `SessionTracker.Track`'s `"death_milestone"` case **set** the session death count to the
  milestone threshold while `"player_death"` (which fires for every death, milestone or not)
  separately **incremented** it — double-counted by one on every milestone death. Removed the
  `death_milestone` write; `player_death` is now the sole writer. Only affected the cosmetic
  shutdown session-summary log, never persisted player data.
- **Highest severity:** the dashboard's JS was never updated to match the new field names —
  `renderCombat()`/the boss-defeats widget/`renderDeath()` read `row.kills`, `row.bosses`,
  `row.combat`, `row.total_kills`, `row.total_deaths`, none of which the refactored backend
  emitted (it emitted `kills_narrative`, `bosses_defeated`, `deaths_lifetime`, etc. instead).
  Fixed by having `Companion.AppendRegistryFields` emit the old field *shapes* as aliases
  alongside the new clean ones, rather than rewriting the 4000+ line HTML's render functions.
  Verify this stays true if the schema changes again — the alias-building code
  (`CanonicalBossOrder`, the `bosses`/`combat`/`total_kills`/`total_deaths` block) lives at the
  bottom of `Companion.AppendRegistryFields`. *(Superseded in 1.3.0: the aliases and
  `AppendRegistryFields` were dropped on the owner's word; the contract is `docs/API.md`.)*
- `PlayerRegistry.SanitizedFileName` was running `Companion.Esc()` (JSON-string escaping) on a
  filename — wrong transformation for a filesystem path (harmless in practice since
  `SanitizeForFile` already strips anything `Esc` would touch, but confusing and wrong). Removed.
- A dead leftover loop in `PlayerRegistry.LoadFromDisk` (`_creature_kill_keys`/`_ck_` prefix
  lookups that could never match anything, immediately overwritten by the correct
  `ParseCreatureKills` call on the next line) — removed.

## v1.1.0: the combat RPC receiver (2026-08-16, same day, later pass)

Implemented `IMPLEMENTATION_HANDOFF.md`'s Work item A (this repo) in full, plus its Work item B
(the client sender) as a **new sibling mod** rather than folding into an existing Wubarrk mod as
that doc originally suggested — the owner's call, made explicitly when asked whether to keep the
server/client split or collapse TheRavensCall into a client+server mod (kept the split; see
`[[project-ravenscall-server-side]]`). The new mod is **WhereTheCrowFlies**
(`/home/rohan/WubarrkCODING/WhereTheCrowFlies`), a small RavenIron-branded client mod whose only
job is reporting combat back to this one — see its own `HANDOFF.md` for details; not duplicated
here.

Two corrections to `IMPLEMENTATION_HANDOFF.md`'s own plan, both verified against
`libs-Tools/VALHEIM-DEDICATED-SERVER-FACTS.md` and the actual dedicated-server decompile
(`libs-Tools/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim_SERVER.decompiled.cs`), not just the
client one:

1. **The handoff's own suggested position source is unreliable.** `ZNet.GetPlayerList()`'s
   `PlayerInfo.m_position` reads `(0,0,0)` for any player who hasn't opted into the minimap's
   "public position" share toggle — confirmed both by decompiling `Minimap.OnTogglePublicPosition`
   and by the FACTS doc, which says outright "Never use for game logic." Used
   `ZDOMan`-backed character-ZDO positions instead (`Plugin.GetConnectedPlayers()`), which the
   FACTS doc calls "THE server-side 'where is every player' API" — synced every physics tick,
   never gated by that toggle.
2. **The same `Player.GetAllPlayers()`-is-empty bug the handoff diagnosed for combat also broke
   `PollAllPlayers`/biome/gear-tier polling**, which wasn't in the handoff's scope but shares the
   exact root cause and explains two more all-zero fields from `KILL_TRACKING_FINDINGS.md`
   (`gear_tier`, `biomes_discovered`). Biome is fixed the same way as position resolution above
   (`BiomeAndGearTracking.CheckBiome`, pure position math via `WorldGenerator.instance`). Gear
   tier is **not** fixed — it needs the player's actual equipped inventory, which has no
   server-side API at all (not gated, not stale — simply doesn't exist without owning the
   character), so it stays broken pending a future client report of the same shape as
   `RavensCall_CombatReport_V1`. Said plainly in code (`BiomeAndGearTracking.CheckBiome`'s
   comment) rather than left to look silently fixed alongside biome.

Also done, per `IMPLEMENTATION_HANDOFF.md` §3.5: rewrote the `Saga.cs:16` architecture comment;
added `SessionReconcile.CloseOrphanedSessions` (closes a session a crash/Alt-F4 left orphaned,
reading `rec.SessionStart` directly since there's no surviving `ZNetPeer`/uid to key
`Patch_PlayerJoin`'s in-memory `JoinTimes` by after a real crash); bumped to 1.1.0.

**One deliberate deviation from the handoff's wire schema** (§2): `victimPrefab` is documented
there as always `""` for `eventType=2` (playerDeath). Left that way, RPC-driven deaths would
narrate with no cause and write inventory-less, cause-less `death_history` entries — a real
regression versus what the (dormant) `Patch_CharacterDeath` does when it has a real `Player` to
inspect. Field 3 now carries a client-computed cause string for playerDeath instead
(`"combat:Draugr"`, `"fall"`, `"drowning"`, etc.) — documented at the top of
`CombatReportReceiver` in `Saga.cs`, and WhereTheCrowFlies's sender implements it to match. If
either side changes, keep both in sync — there's no version negotiation beyond the schema-version
int refusing anything but exactly `1`.

**Still true of this whole path, same caveat as everything else in this file:** never run against
a real Valheim server. `Plugin.GetConnectedPlayers()`, the RPC registration/round-trip, and
`ZDO.GetPosition()`'s actual freshness are all verified against decompiled source and the FACTS
docs, not a live boot.

## v1.2.0: the V2 event receiver, and closing the "we're not getting rich data" gap (2026-08-19)

**Root cause, found this pass:** `WhereTheCrowFlies` had shipped a full second RPC channel,
`RavensCall_EventReport_V2`, since its own v1.0.1 — 10 event types covering kills, deaths,
damage/defense, fish, building, crafting, harvesting, consumables, world events, and a delta
sync of every one of Valheim's built-in `PlayerStatType` counters (~205 since 1.0, 105 before; the count is read off the wire). This repo's `Saga.cs`
only ever registered a listener for the old `RavensCall_CombatReport_V1` channel. Valheim
silently no-ops an unregistered routed RPC — no error, no dropped-connection, nothing in the
log — so every V2 packet a client sent had been vanishing since the day v1.0.1 shipped. The
trigger: the owner showed a screenshot of Valheim's own in-game Stats screen (123 kills, 266
hits, 3 deaths, 324 jumps, 289 cheats, 3,342 world loads, 44 pages of stats) next to a registry
that had recorded essentially none of it.

**What changed, `TheRavensCall` side** (`Saga.cs`, `PlayerRegistry.cs`):
- New `EventReportReceiver` class registers and handles `RavensCall_EventReport_V2` alongside
  the existing V1 listener. Kill/death/fish route through the same `CombatCredit` methods V1
  already used (safe — `CombatEventDedup` already collapses the redundant dual-send every
  V2-capable client makes for V1 backward compat, confirmed still true by the WhereTheCrowFlies
  session: `SendKill`/`SendDeath`/`SendFishCatch` still dual-send V1 unchanged). Damage has no
  such dedup of its own (`CreditDamage`'s own comment: "every batch is new information by
  construction"), so a naive V1+V2 double-registration would double every damage batch. Fixed
  with `V2DamageTracker` (30s recency window, keyed by player name): V1's damage case only
  credits when that player has *not* been recently seen sending V2 damage — i.e. a v1.0.1+
  client's redundant V1 dual-send gets skipped, but a real WhereTheCrowFlies **v1.0.0** client
  (V1-only, predates the V2 protocol — a real shipped version per this repo's own CHANGELOG, not
  hypothetical) still gets its damage credited instead of silently going dark. Caught by the
  WhereTheCrowFlies session during the cross-check below, not by me — a naive unconditional
  "V1 case 3 → no-op" would have quietly broken damage tracking for anyone still on that version.
- `PlayerRecord` gained `VanillaStats` (`Dictionary<string,float>`, one entry per
  `PlayerStatType` the client has reported — kills, hits, deaths, jumps, cheats, world loads,
  PvP hits/kills, arrows shot, portals, distance, etc.), `SkillLevels`/`SkillProgress` (same
  shape, per `Skills.SkillType`), and lightweight lifetime counters for the non-combat V2 event
  types (`BuildsPlaced/Removed/Repaired`, `ItemsCrafted/Upgraded/Repaired`,
  `ResourcesHarvested` per resource, `ConsumablesEaten`, `BossesSummoned`,
  `GuardianPowersUsed`, plus `SessionBlocks`/`SessionParries`/`SessionDmgBlocked` alongside the
  existing session damage fields). All wired through `PlayerRegistry.ToJson`/`LoadFromDisk`.
- Both new dictionaries are keyed by casting the wire `short` id straight through Valheim's own
  enums (`((PlayerStatType)statId).ToString()`, `((Skills.SkillType)skillId).ToString()`) rather
  than a hand-maintained ID table on either side — if a future game patch adds a stat or skill,
  neither repo needs a coordinated update to stay in sync.

**Backfill, and why VanillaStats/SkillLevels are snapshots, not deltas:** the owner's screenshot
proved players already had real history (123 kills) before this pipeline existed. A pure
delta-accumulator (which is what V2's original `StatSync`, event 10, already did and still does)
can never recover that — the server's copy starts at 0 and only grows from new activity observed
*after* the mod is live, and any dropped packet causes permanent, uncorrectable drift from the
true value. Fixed by adding two new event types, agreed in real time with the session working
`WhereTheCrowFlies` (a separate Claude session, coordinated via cross-session messages while both
of us were mid-implementation — see that repo's own HANDOFF.md for its half):
- **11 (`StatSnapshot`)** — absolute values read straight from
  `Game.instance.GetPlayerProfile().m_playerStats` (public, no reflection needed — verified
  against the decompiled assembly this pass, see the research note below).
- **12 (`SkillSnapshot`)** — absolute level + 0-1 progress-to-next-level, read from
  `Player.GetSkills().GetSkillList()`; only skills the player has actually raised are included.
- Both fire once immediately on `Player.OnSpawned` (quick-connect-then-alt-tab still backfills
  fast), then on their own clock — `WhereTheCrowFlies`'s `FullSyncIntervalSeconds`, default **5
  minutes**, not the 10s combat-batch tick — after the owner flagged every-10s full snapshots as
  excessive chatter for ~205 stats + skills.
- `EventReportReceiver` **assigns** (`=`) these into `VanillaStats`/`SkillLevels` via
  `CombatCredit.ApplyVanillaStats`/`ApplySkillLevels` — every sync is a fresh ground-truth
  snapshot, immune to drift, and self-heals from a dropped packet the next time it fires.
- Because the snapshot cadence is minutes, event 10 (`StatSync`, still every 10s, still delta) was
  brought back in as a **live fill-in**: `CombatCredit.ApplyVanillaStatDeltas` accumulates it onto
  `VanillaStats` incrementally between snapshots, so a kill mid-session shows up in seconds rather
  than sitting stale for up to 5 minutes. This is safe to combine with the absolute snapshot
  because 11 always *overwrites* rather than adds — whatever 10's deltas drifted to (e.g. from a
  lost packet) gets corrected outright the next time 11 fires, regardless of what happened between.
- Confirmed via a research pass against
  `libs-Tools/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`: `PlayerProfile`'s
  stats are local-profile-only (`Game.instance.GetPlayerProfile()`, populated from the player's
  own save file) — there is no RPC or ZDO path anywhere in the client or server assembly that
  exposes another player's stats or skills to anyone but themselves, consistent with every other
  "server can't see remote players" finding already documented in this repo. This is *why* the
  client-report architecture (both mods) is mandatory rather than a shortcut — there is no
  server-side alternative to ask for this data.

**JSON export rename (the pass's second, independent ask):** `barrkbot_players.json` →
`BarrkBOT_data1.json`, `steve_export.json` → `BarrkBOT_data2.json` (`Companion.cs`'s
`PollAllPlayers`/`WriteGameData`/`/api/gamedata`). No consumer reads these files yet (BarrkBOT
itself still doesn't, per the note above), so this was a pure rename with no schema or contract
implications — same content, same refresh cadence, just the new naming scheme `HexiumDist/README.md`
now documents. Chosen as two files matching what already existed rather than inventing a
category split nobody asked for; `players/*.json` (the internal per-player persistence store,
not really an "export") was deliberately left alone.

**Cross-checked, not just self-reviewed:** at the owner's explicit request, the full 12-event wire
protocol (every field, in order, with types) was diffed against the actual `TelemetrySender.SendX`
`Write()` calls in the WhereTheCrowFlies repo by the session working there — not against this
repo's own prose description of the protocol. All 12 event shapes confirmed byte-for-byte, the
`PlayerStatType`/`Skills.SkillType` enum-cast approach confirmed symmetric on both sides (no
hardcoded ID table on either end to drift), and the V1-dual-send assumption confirmed still true.
The V2DamageTracker fix above came directly out of that exchange.

**Not done, deliberately, to keep this pass scoped:** `theravenscall.html`'s dashboard JS was not
updated to render any of the new fields (it already had one prior "field names drifted from what
the backend emits" incident — see the v1.1.0-era "Fixed this session" list above — so silently
half-wiring new fields into a 4000+ line render layer felt riskier than leaving it alone; the
JSON export has the data either way). Building/crafting/harvesting/consumable/world-event data is
stored as lifetime aggregate counts, not full histories, matching `DeathHistory`'s existing
bounded-footprint precedent — richer per-event logs (what was crafted, when, where) were not
requested and would need their own retention/size decision if wanted later.

## v1.2.1 (2026-08-22): three small fixes found joining the multi-session BarrkBOT party

- **Build was broken for anyone else pulling this repo.** `TheRavensCall.csproj`'s `Reference`
  HintPaths were all `..\libs-Tools\...`, correct only when this project sat directly under
  `WubarrkCODING/`. It now lives under `WubarrkCODING/PAUSED/TheRavensCall/`, so every Valheim/
  Unity/BepInEx type reference (`Player`, `Character`, `ZPackage`, `Vector3`, `HarmonyPatch`, etc.)
  failed to resolve — 131 compile errors. Fixed by repointing every HintPath to `..\..\libs-Tools\`.
  Confirmed `libs-Tools` itself is untouched at `/home/rohan/WubarrkCODING/libs-Tools/` — only this
  project's own location moved.
- **Leftover "SteveCompanion" startup branding.** `Companion.NAME`/`Companion.VERSION` were still
  the original SteveCompanionMod's own constants (`"SteveCompanion"`, `"0.2.3"`), logged verbatim
  at server startup (`Companion.Awake()`) and served from `/health`. Repointed both to
  `Plugin.PluginName`/`Plugin.PluginVersion` (`"TheRavensCall"`/`"1.2.1"`) so both places track the
  real plugin identity/version instead of drifting from it.
- **Dashboard's `total_kills`/`total_deaths` alias didn't honor the crow's real totals.**
  `Companion.AppendRegistryFields` built the `total_kills`/`total_deaths` legacy-alias fields
  straight from `rec.TotalKillsLifetime`/`TotalDeathsLifetime` — the server's own directly-observed
  counts, which undercount badly (see `BARRKBOT_CONTRACT.md`'s Thorium example: 13 observed vs.
  136 actual) because a dedicated server owns no zone with a player in it. `vanilla_stats.EnemyKills`/
  `.Deaths`, reported by WhereTheCrowFlies and applied via `CombatCredit.ApplyVanillaStats`/
  `ApplyVanillaStatDeltas`, already carry the real lifetime totals — BarrkBOT itself already prefers
  them per the contract file, but this repo's own dashboard alias never did. Now prefers
  `VanillaStats["EnemyKills"]`/`["Deaths"]` when present, falling back to the old observed counts
  only when no WhereTheCrowFlies client has reported yet.

**Version bumped 1.2.0 → 1.2.1** (`Saga.cs`'s `PluginVersion`, `HexiumDist/manifest.json`, the
README badge). `HexiumDist/plugins/TheRavensCall.dll` refreshed from a clean rebuild;
`TheRavensCall-v1.2.0.zip` replaced with `TheRavensCall-v1.2.1.zip` (same Python `zipfile` approach
as prior passes — neither `zip` nor `unzip` exist in this environment), old zip deleted per the
one-zip-at-a-time convention. `CHANGELOG.md` got a new `[1.2.1]` entry covering all three fixes
above.

## Known-unverified / follow-ups if picked back up

- **Never run against a real Valheim server.** Compiling is necessary, not sufficient — the whole
  design rests on assumptions about which Harmony patch targets actually fire headless
  (`Character.OnDeath`/`Character.Damage`/`RandEventSystem.SetRandomEventByName` should, per
  Valheim's server-authoritative combat model and the fact `Player.GetAllPlayers()` was already
  used successfully server-side in the original SkaldSaga's boss-credit code; the biome/gear-tier
  *polling* replacement for the old client-UI hooks is a much safer bet than the hooks themselves
  ever were headless, but still unverified in practice). **Update, v1.1.0: this assumption was
  wrong for combat** — see the section above. Kept here rather than deleted as a reminder that
  "should fire, per the game's architecture" is not the same claim as "verified against a live
  server," even when the reasoning sounds sound.
- `Companion.cs`'s `BoatTracker.GetJson(Player)` — "which ship is this player on" was rewritten
  from `Ship.GetLocalShip()` (a client-only concept) to `player.GetComponentInParent<Ship>()`, an
  explicitly-flagged guess, not verified against decompiled Valheim source. Check
  `/home/rohan/WubarrkCODING/All_Deep.txt` (mentioned in `[[user-wubarrk-valheim-modding]]`
  memory as the project's standard way to verify Valheim API guesses) before trusting it further.
- *(Resolved by deletion in 1.3.0: the tamed list went with `BuildStateJson`.)* The dashboard's `tamed` creature list (`Companion.cs`, inside `BuildStateJson`) was built from
  `Character.GetAllCharacters()` globally with no per-player distance/ownership filter — every
  player's row shows the same world-wide list. This is inherited from the original
  SteveCompanionMod (confirmed via the original source dump), not a new regression, but it's more
  visible now that the dashboard genuinely shows multiple simultaneous players instead of one.
- ~~`theravenscall.html`'s GitHub feedback buttons and the wizard's "Built by" link still point at
  `github.com/NomadicWar/SteveCompanion` — no RavenIron repo URL was given, so nothing was
  invented. Update `GITHUB_REPO` (search the HTML for it) once one exists.~~ (done in 1.3.0: the
  rebuilt page's feedback link points at `github.com/RavenIron-Games/TheRavensCall/issues`.)
- `icon.png` in `HexiumDist/` is an ImageMagick-generated placeholder ("RC" monogram) — user said
  they're doing the real one themselves. Re-zip after it's replaced (see the Python `zipfile`
  snippet used this session, since neither `zip` nor `unzip` exist in this environment).
- `/api/map` (fetched by the dashboard's dead Map tab JS, `loadMap()`/`drawMap()`) was never
  implemented by either original mod and still isn't — pre-existing, not a regression, and the tab
  itself has no button/container in the markup so it's unreachable by a user regardless.
- **Gear-tier detection is still broken**, server-side-unfixable without a client report (see the
  v1.1.0 section above). `rec.GearTier`/`biome_discovery`'s sibling field will stay at 0 for
  everyone until something reports equipped gear the same way `RavensCall_CombatReport_V1` reports
  combat. Not attempted this pass — flagging it rather than leaving it to look silently fixed
  alongside the biome-discovery fix it shipped next to.
- `HexiumDist/plugins/TheRavensCall.dll` and `HexiumDist/TheRavensCall-v1.2.1.zip` are refreshed
  and current as of the 2026-08-22 pass (see that section above). Older versioned zips
  (`v1.0.0`, `v1.1.0`, `v1.1.1`, `v1.2.0`) were deleted on explicit request rather than kept as
  rollback baselines — `HexiumDist/` now holds exactly one zip, the current version, at all times.
