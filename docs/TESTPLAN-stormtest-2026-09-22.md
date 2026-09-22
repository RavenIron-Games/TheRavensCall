# 1.4.2 follow-ups — boot checks on StormTest, 2026-09-22

The four items the 1.4.1 pre-publish review left open (see `HexiumDist/CHANGELOG.md`
[1.4.2] and `HANDOFF.md`), plus what the pre-PR review of this branch added (the baseline
name compared trimmed, season state reset when `seasons.json` is missing, an atomic
`seasons.json` write, a warning for a damaged value). Checked on the `StormTest`
folder (`C:\Users\donfr\ValheimServers\StormTest`, Valheim **0.221.12**, launched on port
2496 with its own save dir; Storm10 stayed closed).

Build under test: branch `fix/followups-1.4.2` as opened, `dotnet build -c Release`, md5
`6eea563f6085810c36488d5357eaf134`, 211,456 bytes. The packaged DLL is not rebuilt by this
change. Every boot below ran on those bytes; an earlier build of the branch had run the
same torn-file and stale-baseline boots before the review, with the same outcome.

## Results

| Item | Check | Result | Evidence |
|---|---|---|---|
| Damaged `seasons.json` reads as no season | reflection on `SeasonSystem.MetaString` | PASS | a closed value (`Winter War`), the same as the last key, and with spaces around the colon all read back; a value with its closing quote dropped (`"Winter War, "season_start":…`) and a file cut off mid-value both read as absent; a file ending right after the closing quote still reads; escapes decode; `""` reads empty; a missing key reads absent |
| Damaged `seasons.json` reads as no season | boot with `current_season` unterminated | PASS | `seasons.json holds a damaged current_season value; treating it as no active season.` (warning), no `Active season loaded` line, `Chronicle: …\Chronicle\TheRavensCall_Chronicle_2026-09-22.log` (default folder), `/api/activity` `active=false` with `last_ended "Old"` still read from the same file, no folder created under `Chronicle\seasons\` |
| Pre-1.4.2 baseline still accepted | boot with season `S2` active and a `season_baseline.json` naming `S2.` (the untrimmed spelling a 1.4.0 or 1.4.1 file can carry) with rows of 999 | PASS | `Active season loaded: S2`, no baseline warning, no snapshot-now line, the file left as it was, `/api/activity` `standings_since 2026-09-22T11:00:00Z` = the file's `taken_at`, the Chronicle in `Chronicle\seasons\S2\` |
| Stale baseline rejected | boot with season `S2` active and a `season_baseline.json` naming `S1` (rows of 999) | PASS | `season_baseline.json belongs to season S1, not S2; taking a fresh baseline now.` (warning), then `no usable season_baseline.json for active season S2; standings count from this restart (2026-09-22T13:53:54Z).`, the file rewritten as `{"season":"S2","taken_at":"2026-09-22T13:53:54Z","rows":[]}`, `/api/activity` `standings_since` = that boot and `standings []` (no 999s), the Chronicle in `Chronicle\seasons\S2\`, the shutdown row written there on the graceful stop |
| Day rotation keys on the file name | review only | — | `Chronicle.Write` now tests `Path.GetFileName(_logPath)` for today's date; a midnight rollover inside a season folder named with a date was not waited for |
| Boot guard also re-points with no season active | review only | — | listen-server double-host case, with `seasons.json` edited to no season or deleted (the season state, last-ended pair included, now resets on a missing file; a file that exists but cannot be read keeps the loaded season); `ZNet.Awake` runs once per process on a dedicated server, so it cannot be reached here |
| `seasons.json` written atomically | review only | — | `SaveMeta` goes through `PlayerRegistry.AtomicWrite`, the same path `season_baseline.json` already used |

StormTest was left as found: the staged plugin folder, the generated
`com.raveniron.theravenscall.cfg` and the `TheRavensCall` config folder were removed; the
BepInEx log of the boots was copied aside.
