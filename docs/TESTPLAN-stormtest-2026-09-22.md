# 1.4.2 follow-ups — boot checks on StormTest, 2026-09-22

The four items the 1.4.1 pre-publish review left open (see `HexiumDist/CHANGELOG.md`
[1.4.2] and `HANDOFF.md`), checked on the `StormTest` folder
(`C:\Users\donfr\ValheimServers\StormTest`, Valheim **0.221.12**, launched on port 2496 with
its own save dir; Storm10 stayed closed). Two of the four are boot-time behaviours and were
run live; the other two cannot be reached on a dedicated server in one sitting and rest on
the code review. Build under test: the branch `fix/followups-1.4.2` at the time of the run,
`dotnet build -c Release`, 210,944 bytes (the log-line wording changed once more after the
run; the packaged DLL is not rebuilt by this change).

## Results

| Item | Check | Result | Evidence |
|---|---|---|---|
| Damaged `seasons.json` reads as no season | reflection on `SeasonSystem.MetaString` | PASS | a closed value (`Winter War`), the same as the last key, and with spaces around the colon all read back; a value with its closing quote dropped (`"Winter War, "season_start":…`) and a file cut off mid-value both read as absent; a file ending right after the closing quote still reads; escapes decode; `""` reads empty; a missing key reads absent |
| Damaged `seasons.json` reads as no season | boot with `current_season` unterminated | PASS | no `Active season loaded` line, `Chronicle: …\Chronicle\TheRavensCall_Chronicle_2026-09-22.log` (default folder), 0 warnings, `/api/activity` `active=false` with `last_ended "Old"` still read from the same file, no folder created under `Chronicle\seasons\` |
| Stale baseline rejected | boot with season `S2` active and a `season_baseline.json` naming `S1` (rows with 999 kills) | PASS | `season_baseline.json belongs to season S1, not S2; taking a fresh baseline now.` (warning), then the snapshot-now line with `standings count from this restart (2026-09-22T13:32:36Z)`, the file rewritten as `{"season":"S2","taken_at":"2026-09-22T13:32:36Z","rows":[]}`, `/api/activity` `standings_since` = that boot and `standings []` (no 999s), the Chronicle in `Chronicle\seasons\S2\` with the startup row, and the shutdown row written there on the graceful stop |
| Day rotation keys on the file name | review only | — | `Chronicle.Write` now tests `Path.GetFileName(_logPath)` for today's date; a midnight rollover inside a season folder named with a date was not waited for |
| Boot guard also re-points with no season active | review only | — | listen-server double-host case; `ZNet.Awake` runs once per process on a dedicated server, so it cannot be reached here |

StormTest was left as found: the staged plugin folder, the generated
`com.raveniron.theravenscall.cfg` and the `TheRavensCall` config folder were removed; the
BepInEx log of the two boots was copied aside.
