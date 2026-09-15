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
| 1 | Join as Nomadtest | Chronicle has exactly one `has entered the world` line; row appears with `online: true`; server log shows the StatSnapshot and SkillSnapshot accepted (`event report accepted` lines); `vanilla_stats` has ~205 keys, none `NaN`/`Infinity` | sender binding, NaN guards, 205 stats |
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

Steps 1 to 17 wait for the client session.
