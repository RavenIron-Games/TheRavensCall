# Storm10 test plan — TheRavensCall 1.2.4 + WhereTheCrowFlies 1.1.3

Written 2026-09-15 for the first live run of the two review batches (TheRavensCall PRs #1 and #2, WhereTheCrowFlies PRs #1 and #2, all merged, nothing released). Storm10 is Don's local Valheim 1.0.12 dedicated server (build 25253791, port 2477, world `Storm10`, password `stormhold`, crossplay on). The client is the Gale `testing` profile on the same machine (build 25253764).

## What is staged

| Where | File | Source | md5 |
|---|---|---|---|
| `C:\Users\donfr\ValheimServers\Storm10\BepInEx\plugins\RavenIronStudios-TheRavensCall\` | `TheRavensCall.dll` | main 34836a2, Release build | `16480701cdd9ab346e1c5e73063e420e` |
| same folder | `theravenscall.html`, `manifest.json` | repo root / HexiumDist | |
| Gale `testing` profile, `BepInEx\plugins\RavenIronStudios-WhereTheCrowFlies\` | `WhereTheCrowFlies.dll` | main bdcebb7, Release build | `ccdd6fa99caac6b318c3e9765ff8d8ca` |
| `Storm10\BepInEx\config\com.raveniron.theravenscall.cfg` | pre-seeded with `[Combat] LogCombatReports = true`; BepInEx fills every other key with its default on first boot | | |
| `Storm10\adminlist.txt` | `Steam_76561198392625778` (Don, plays as Nomadtest) — needed for the console command step | | |

Neither mod had ever been installed on Storm10 or the `testing` profile before this session. The Discord webhook is left empty; the Chronicle line and the "Discord webhook" log line stand in for it unless a test webhook is configured.

## Start and stop

Start (the .bat does not launch from a tool shell; run the exe directly):

```
cd C:\Users\donfr\ValheimServers\Storm10
set SteamAppId=892970
valheim_server.exe -logfile "%cd%\storm10-server.log" -nographics -batchmode -name "Storm10" -port 2477 -world Storm10 -password "stormhold" -public 0 -crossplay -savedir "%cd%\saves"
```

Stop gracefully (world saved, "Shutting down / ZNet Shutdown" in the log): `taskkill /PID <pid>` without `/F`. CTRL-BREAK in the console window does the same.

## Where to read results

- Export: `Storm10\BepInEx\config\TheRavensCall\BarrkBOT_data1.json` (rewritten every 10 s while the server runs).
- Per-player records: `Storm10\BepInEx\config\TheRavensCall\players\<name>.json`.
- Chronicle: `Storm10\BepInEx\config\TheRavensCall\Chronicle\TheRavensCall_Chronicle_<date>.log` (one JSON object per line).
- Mod log lines: `Storm10\BepInEx\LogOutput.log` (search `[TheRavensCall]`); the client's is the `testing` profile's `BepInEx\LogOutput.log` (search `[WhereTheCrowFlies]`).
- Dashboard: `http://localhost:2112` on the server machine.

## Boot check (before joining)

- [ ] Server log: `TheRavensCall 1.2.4 awakens (server-only).`, `[TheRavensCall] All patches applied.`, `[TheRavensCall] PlayerRegistry loaded 0 player record(s).`, `[TheRavensCall] Server systems initialized.`
- [ ] Server log: `HTTP server listening on http://localhost:2112 (localhost only; set HttpBindAllInterfaces=true to expose it)`.
- [ ] `com.raveniron.theravenscall.cfg` now holds every key, with `HttpBindAllInterfaces = false`, `HttpApiToken =`, `LogCombatReports = true`.
- [ ] `BarrkBOT_data2.json` written ("Game data written OK"); no `.tmp` left next to it.
- [ ] Wrong-password join attempt from the client (or a version-mismatched client if one is handy): no `A Viking` row in the export, no join line in the Chronicle. (must-fix 4)
- [ ] Client log after launching the `testing` profile: `WhereTheCrowFlies 1.1.3 loaded.`, `[WhereTheCrowFlies] All patches applied.`

## One action per fix

Read the export after each step. "Row" means Nomadtest's object under `players`.

| # | Action | Pass when | Fix under test |
|---|---|---|---|
| 1 | Join as Nomadtest | Chronicle has exactly one `has entered the world` line; row appears with `online: true`; `vanilla_stats` has ~205 keys and `skill_levels` is filled (the V2 receiver logs drops only, never accepts, so the row is the evidence); no `NaN`/`Infinity` | sender binding, NaN guards, 205 stats |
| 2 | Kill any creature | `creature_kills[<prefab>]` +1, `session_kills` +1; log shows the V2 kill accepted and the legacy V1 twin collapsed by the dedup | kill path, dual-send |
| 3 | Die once (`killme` from the devcommands console works) | `deaths_narrative` +1, `death_history` gains an entry with a cause; Chronicle has the death line; log shows `player_death` narrated (and `Discord webhook sent` if a webhook is set) | **Player.OnDeath hook (the headline fix)** |
| 4 | Die again within 5 s if possible (respawn, `killme`) | second death dropped with `death report dropped — budget exceeded`; a death after 5 s is credited | death budget |
| 5 | Pick one berry bush with a tap | `resources_harvested[<item>]` +amount, exactly one accepted harvest in the log | harvest attribution |
| 6 | Hold the Use key on the next bush | still exactly one harvest logged for that bush | harvest debounce |
| 7 | Craft one batch of arrows at a workbench (multi-craft if the station allows) | `items_crafted` +N where N is what landed in the inventory | craft counting |
| 8 | Fill the inventory, craft again | no change to `items_crafted`; log shows no crafting report | failed-craft guard |
| 9 | Wait past one full-sync interval (5 min) while doing nothing | `vanilla_stats` values do not jump; one StatSnapshot accepted, no delta arrives after it in the same second | snapshot/delta ordering |
| 10 | In the client console: `ravenscall season start Test` | server console prints `[TheRavensCall] Season started: Test`; client gets a one-line stub message; `ravenscall season end` closes it | console routing + admin list |
| 11 | Remove the admin entry, restart, repeat step 10 | client prints `You are not admin`; nothing runs on the server | admin gate |
| 12 | Open `http://localhost:2112` on the server machine | page loads; `/api/state` returns the row within 10 s of boot (not `[]`) | state cache, priming |
| 13 | Open `http://<this PC's LAN IP>:2112` from a phone | connection refused | localhost-only default |
| 14 | Set `HttpBindAllInterfaces = true`, restart, repeat 13 | page loads on the phone | bind switch |
| 15 | Set `HttpApiToken = test123`, restart, open `/api/state` with and without `?token=test123` | 401 without, rows with; `/api/health` open either way | token gate |
| 16 | `taskkill /F` the server mid-run, restart | `PlayerRegistry loaded 1 player record(s)`, no `is truncated` warning, no `.tmp` or `.bak` files in `config\TheRavensCall\` or `players\` | atomic writes |
| 17 | Leave with a clean disconnect | one `has left the world` line with the played time; `online: false`, `playtime_seconds_lifetime` grew | leave path unchanged |

## Not coverable on this rig

- A berry picked in a zone another player owns, and a kill reported by a zone owner who is not the killer. Both need a second client: Wu'barrk on his box, or a second account here.
- Forged reports (another player's name, a boss kill spam, NaN payloads). A vanilla client cannot send them; a throwaway test mod would be needed. These were verified by tracing the code against the 1.0.12 decompile in the review.
- A real Discord post, unless a test webhook URL is put in the config for the session.

## Results

### Boot, 2026-09-15 11:21 local (pid 33384)

Storm10 stopped gracefully at 11:20:44 (`Shutting down` / `ZNet Shutdown`) and relaunched with the direct exe line. Boot check: all green.

- `Loading [TheRavensCall 1.2.4]`, `awakens (server-only)`, `All patches applied`, `Player.OnDeath method found: OnDeath`, `HTTP server listening on http://localhost:2112 (localhost only; set HttpBindAllInterfaces=true to expose it)`, `Game data written OK`, `Chronicle: ...TheRavensCall_Chronicle_2026-09-15.log`, `PlayerRegistry loaded 0 player record(s)`, `SessionTracker started`, `LoreSystem loaded 5 entries`, `Server systems initialized`, then `Poll tick — 0 online, 0 known player(s)` every 10 s. No error line mentions the mod (the Unity shader and intro-cinematic errors are the headless server's usual noise; the YggdrasilsReckoning donor errors are that mod's).
- `GET /api/health` on localhost: `{"status":"ok","version":"1.2.4"}`. `GET /api/state`: `[]` (registry empty, primed cache). `GET /`: the dashboard page, 252,104 bytes.
- `com.raveniron.theravenscall.cfg` generated with every key; `HttpBindAllInterfaces = false`, `HttpApiToken =` present under `[Companion]`; the pre-seeded `LogCombatReports = true` kept.
- `config\TheRavensCall\` holds `BarrkBOT_data1.json` (146 bytes, empty players map), `BarrkBOT_data2.json` (386,318 bytes), `lore.txt`, `Chronicle\`, `players\`; no `.tmp` or `.bak` anywhere, so the atomic writer's rename path works on Windows.
- Chronicle first line: the `startup` event, `TheRavensCall is listening.`

### Client session, 11:51 to 12:35 local (Nomadtest, WhereTheCrowFlies 1.1.3)

Don played on the `testing` profile against pid 33384 for 44 minutes. Times below are local (UTC-7). The record read after each step is `players\Nomadtest.json` plus the Chronicle and the server log.

| # | Result | Evidence |
|---|---|---|
| 1 | PASS | One `player_join` line 18:51:10Z; row `online: true`; `vanilla_stats` 205 keys, `skill_levels` 13 skills, no NaN/Infinity. |
| 2 | PASS (second attempt) | Greyling killed by hand 12:09:23: `creature_kills {Greyling: 1}`, `session_kills 1`, Chronicle `kill_milestone`; the V1 twin logged `combat report accepted: type=1` and the dedup kept the count at 1; damage batch 11.8 dealt. The first attempt (a boar) produced no report at all: it was killed with debug mode's K key, which runs `killenemies`, a 1e10 HitData with no attacker; both mods drop attacker-less deaths on purpose, and the engine credits no kill for them either. |
| 3 | PASS | Died to a Neck 11:54:10 at (-189.5, 29.9, 37.1): Chronicle `death_milestone` + `player_death` "met their end at the hands of a Neck", `death_history` entry with killer/location/biome, `deaths_narrative 1`. The headline fix works live. |
| 4 | Covered | The V1 death twin arriving a moment after the V2 report was dropped with `death report dropped — budget exceeded for sender -282116223`. A real respawn cannot beat 5 s, so no second death was attempted. |
| 5 | PASS | Tap: `resources_harvested {"$item_raspberries": 2}`. The key is the item's localization token, unchanged from 1.1.2 (`GetHoverName()`), so BarrkBOT keys are stable; localizing it would be a rename. |
| 6 | PASS | Hold: one more report, amount 1, no repeat from the held key. The 2-then-1 split against a fixed per-bush yield is unresolved (vanilla `HarvestBerry` read 3 lifetime after both; Don's inventory count not confirmed). |
| 7 | PASS | `items_crafted` 1 to 82 (81 arrows, three crafts logged on the client at 12:17:17/20/22, so a multi-craft or a bonus arrow is in there); a later single batch at 12:34 added exactly 20. One repair counted separately. |
| 8 | PASS | Full-inventory craft attempt: record untouched, no crafting report. |
| 9 | PASS with one anomaly | Skill snapshot arrived on its 5-minute clock (crafting progress 0.3 after the arrows) and the stat deltas work (`Jumps` 108 to 118 within 10 s of ten jumps). Anomaly: the vanilla `Crafts`/`CraftAmmo`/`CraftsOrUpgrades` counters never moved for the 12:17 crafts, through both the delta path and at least one later snapshot, while the 12:34 single batch moved all three by one within 10 s. Not reproduced; the mod's own `items_crafted` was right both times. |
| 10 | PASS | `ravenscall season start Test` typed on the client 12:23:52: server logged `Season started: Test`, Chronicle switched to `Chronicle\seasons\Test\`, narration fired; `ravenscall season end` 12:34:57 closed it and the Chronicle switched back. |
| 11 | NOT RUN | Server was restarted with the admin entry removed and the bind/token settings on, but Don closed the session before rejoining. Admin entry and defaults restored. |
| 12 | PASS | With Don online: `GET /` 200 (252,104 bytes), `/api/health` `{"status":"ok","version":"1.2.4"}`, `/api/state` one row with live counters, `/api/pins` and `/api/gamedata` 200. |
| 13 | PASS | `http://192.168.12.140:2112/` (this machine's LAN address) refused under the default. Tested from the server machine, not a phone. |
| 14 | PASS | `HttpBindAllInterfaces = true`: log says `listening on http://localhost:2112 and every interface`; the LAN address serves the page. |
| 15 | PASS | `HttpApiToken = test123`: `/api/state`, `/api/gamedata`, `/api/pins` answer 401 `{"error":"token required"}` without a token or with a wrong one, 200 with `?token=test123` or `X-Api-Token`; `/api/health` and `/` stay open. |
| 16 | PASS | `taskkill /F` on the running server (two instances, see below): no `.tmp`/`.bak`/`.corrupt` anywhere under `config\TheRavensCall\`, both exports parsed, next boot `PlayerRegistry loaded 1 player record(s)` with no truncation warning. |
| 17 | PASS | Clean logout 12:35:12: one `player_leave` line "has left the world (played 44m)", `online: false`, `session_start` cleared, `playtime_seconds_lifetime` 2641. Graceful stop then wrote `Session summary written to Chronicle`. |

Operational notes from the session:

- The export files (`BarrkBOT_data1.json`, `BarrkBOT_data2.json`, `players\*.json`) start with a UTF-8 BOM. So did 1.2.2's (`Encoding.UTF8` in every writer), so this is not a regression, but a reader must open them as `utf-8-sig`.
- Launching the server through the Bash tool's background mode started two instances one second apart on the same world. PowerShell `Start-Process` with the same arguments starts exactly one; use that.
- A dedicated server simulates the area around the world origin as if a player stood at (0,0,0): `ZNet.m_referencePosition` stays `Vector3.zero` on the server build, `ZDOMan.ReleaseZDOS` claims unowned ZDOs in that active area for the server, and `ZNetScene.CreateDestroyObjects` instantiates them. A creature killed inside that area is owned by the server, so no client owns it and no client-side kill hook fires. Not exercised here (the session stayed ~190 m out), but it bounds what the client reporter can ever see near spawn.

Left over: step 11 (non-admin console), the two-player cases, and a re-test of the vanilla craft counters after several quick crafts.
