# 1.4.0 server-side boot test — StormTest, 2026-09-21

The server-only part of `docs/SCOPE-1.4.0.md` §9, run on the `StormTest` folder
(`C:\Users\donfr\ValheimServers\StormTest`, launched on port 2496 with its own save dir so
Storm10 stayed untouched). Build: `feat/activity-1.4.0` working tree, `dotnet build -c
Release`, DLL md5 `f9ee434b8b4ed8872c70023c759bd9a9`, 207,872 bytes. Nobody joined; every
step that needs a client (deaths, kills, raids, seasons, capacity eviction) still has to
run on Storm10 with WhereTheCrowFlies on the client.

**Caveat on the testbed.** StormTest's `valheim_server.exe` is Valheim **0.221.12** (network
version 36), not 1.0.12. The mod loaded and every patch applied, but two things differ from
Storm10: the `ravenscall` console command registration throws a `MissingMethodException` on
this build (the `Terminal.ConsoleCommand` constructor gained parameters after 0.221; not a
1.4.0 change, the 1.0.12 Storm10 test on 2026-09-15 proved that command), and at shutdown
`ZNet.instance` is already gone when `Plugin.OnDestroy` runs, so the pre-existing
`if (ZNet.instance != null && ZNet.instance.IsServer())` guard skips the `shutdown` Chronicle
row and the session summary. Neither is tier 2's footprint; both are worth one look on
Storm10 (step 3's "second boot" expectation of a `shutdown` row depends on the second).

## Results

| §9 step | Result | Evidence |
|---|---|---|
| 1 | PASS | fresh folder, no disk override anywhere |
| 2 | PASS | `TheRavensCall 1.4.0 awakens (server-only).`, `All patches applied.`, `Server systems initialized.`, `HTTP server listening on http://localhost:2112`; no exception attributed to the mod; `/api/health` → `{"status":"ok","version":"1.4.0"}` |
| 3 | PASS | `/api/activity` → 200, `season` = the empty object from §4.7 with `active:false`, `events` = one `startup` row; the row's `message` equals the Chronicle line; `event_feed.json` written on the first poll tick (UTF-8 BOM, like every other export) |
| 4 | PASS | embedded page: "This season / No season is running.", "Recent activity" with the filter chips and the `STARTUP` row, "No players have ever joined this world yet." below; no console errors |
| 3, second boot | PASS (feed) | after a graceful `taskkill` and restart, `/api/activity` holds two `startup` rows newest first: the reloaded one from the file under the new one. No `shutdown` row on this build (caveat above) |
| 10 | PASS | `HttpApiToken` set: `/api/activity` and `/api/state` → 401 `{"error":"token required"}`; `/api/health` and `/` stay 200; the page shows "Needs the API token (Settings)." and the existing settings auto-open |
| 11 | PASS | `?token=` and `X-Api-Token` both → 200; entering the token in Settings brings both panels back without a reload (the must-fix from review) |
| 18 | PASS | `EventFeedCapacity = 0`: 200 with `"events":[]`, not 404; `event_feed.json` deleted at boot; the feed panel shows "No events yet."; the season panel unaffected |
| 20 | PASS | against the fixture harness in 404 mode (a 1.3.0 server): both panels hidden, exactly one failed `/api/activity` request, the tier 1 overview renders normally |
| §5 `/api/state` | PASS | envelope byte-identical in shape: `{generated_at, world_name, day, online_count, raid_active, raid_type, players}` |
| §6 config | PASS | `EventFeedCapacity = 200` and `EnableRaid = true` written with their defaults on first boot; the startup log line names `/api/activity` among the gated routes |

## Not run (needs a client on Storm10)

Steps 5-9 (join row, player link, raids with `devcommands` + `randomevent` + `stopevent`,
kills/deaths/biome, the tab in `lore.txt`), 12-17 (season start/end, standings deltas,
mid-season restart with the Chronicle in the season folder, the deleted-baseline case,
capacity 5 eviction), 19 (1.3.0 page on the 1.4.0 server), 21-22 (settings base-URL
round trip, phone width with an adversarial name).

StormTest was left as found: the staged plugin folder, the generated
`com.raveniron.theravenscall.cfg` and the `TheRavensCall` config folder were removed;
only `stormtest-1.4.0-server.log` remains as evidence.
