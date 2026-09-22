# TheRavensCall 1.6.1 — Storm10 boot check, 2026-09-22

Branch `fix/season-start-guard-1.6.1` (the `season start` guard, version 1.6.1). Storm10 = the owner's Windows dedicated server on Valheim 1.0.12, started once via PowerShell `Start-Process` and stopped with a graceful `taskkill` (no `/F`).

| Step | Result |
|---|---|
| Build | `dotnet build -c Release` of the branch after the review's code fix (`SnapshotBaseline` guarded inside `StartSeason`), clean: 0 warnings, 0 errors; DLL md5 `104b6792c5999cca0d4b5a3ca3bfafb8`, 275,968 bytes; the strings `is still running`, `Season not started`, `season_baseline.json could not be written` and `1.6.1` present in the assembly. (An earlier boot of the pre-review build `ac0416a2…`, commit `fa38868`, gave the same result at 16:42.) |
| Boot | 16:52:18 on the final build: `Loading [TheRavensCall 1.6.1]`, `TheRavensCall 1.6.1 awakens (server-only).`, Chronicle opened in the default folder (no season active on Storm10), `World census: 262520 objects in 28 ms.`; zero `[Warning]`/`[Error]` lines from the mod during boot and until the stop |
| Stop | graceful, 3 shutdown lines (Shutting down / ZNet Shutdown / session summary), no process left; 0 warnings for the whole run |
| `season start` while a season runs | **NOT RUN** — the `ravenscall` command is `remoteCommand` and reaches the server only from an admin's game client with WhereTheCrowFlies; needs the owner in game. Expected: console `Season '<name>' is still running — run `ravenscall season end` first.`, one server-log warning, `seasons.json` unchanged |
| `season start` with no season, then `season end` | **NOT RUN** — same reason. Expected: unchanged 1.6.0 behaviour (`Season started: <name>`, then `Season ended.`) |

The refusal is a pure code path in `SeasonSystem.StartSeason` guarded by `_currentSeason`, which `Init` loads from `seasons.json` at boot, so a restart with an active season refuses the same way. The 1.6.1 build stays staged on Storm10.
