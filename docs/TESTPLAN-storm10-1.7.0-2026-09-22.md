# TheRavensCall 1.7.0 + WhereTheCrowFlies 1.2.0 — Storm10 live runs, 2026-09-22 and 2026-09-23

Branches `feat/title-picker-1.7.0` (TheRavensCall, PR #21) and `feat/title-picker-1.2.0` (WhereTheCrowFlies, PR #6): the title picker (`/title`, the `/titles` panel, `ravenscall title`). Storm10 = the owner's Windows dedicated server on Valheim 1.0.12; the client = the owner's Gale `testing` profile. Both builds were staged and the server started by the owner's hand; the session tooling read the logs and the local API only.

| Step | Result |
|---|---|
| Builds | `dotnet build -c Release` of both branches, 0 warnings, 0 errors. Staged: `TheRavensCall.dll` md5 `6b37713ac259fd191586f4876b15c2ca` (279,552 bytes) in Storm10's plugin folder with the shipped 1.6.1 kept beside it as `TheRavensCall-1.6.1.dll.off`; `WhereTheCrowFlies.dll` md5 `338d0a39dc4d79f9059a0725a6932fbf` (63,488 bytes) in the testing profile with 1.1.3 kept the same way. Both checksums verified after the copies. |
| Boot | 19:29:38–44: `Loading [TheRavensCall 1.7.0]`, `TheRavensCall 1.7.0 awakens (server-only).`, Chronicle opened, `World census: 262520 objects in 30 ms.`, session registered with a join code; zero `[Warning]`/`[Error]` lines from the mod for the whole run. `GET /api/health` → `{"status":"ok","version":"1.7.0"}`. Client: `Loading [WhereTheCrowFlies 1.2.0]`, all patches applied. |
| Sessions | Nomad 19:32 (seconds, back to character select), TestNomad 19:33–19:39 (6 min) and again to 19:51 (10 min), Nomad 19:53–19:54 (1 min); one join refused on a wrong password at 19:56. Every join, leave and the first biome line narrated as before. |
| `/titles` panel, set and clear | **PASS.** Server log, in order: `TestNomad set no title`, `TestNomad chose the title 'Stag Breaker'`, `TestNomad set no title`, `TestNomad chose the title 'Stag Breaker'` — two full clear-and-set cycles. `/api/state` read `active_title` as `""` after a clear and `"Stag Breaker"` after a set, `titles_earned` `["Stag Breaker"]` throughout. No `dropped`, `rate-limited` or `unknown op` line. |
| Panel open/close | **PASS.** Client log: `title panel closed: key` ×5 (the owner bound `TitlePanelKey`; the key opens and closes), `title panel closed: close button` ×4. One opening as Nomad, whose record has no titles at all, exercised the empty-list branch (the note with no buttons). The reply text on screen is not logged, so its wording was checked by the owner's eyes only. |
| Input gate | No stray attack, inventory or menu reported by the owner while the panel was up; the gate is a postfix on `TextInput.IsVisible()` (see `WhereTheCrowFlies/Patches/TitlePanel.cs`). Not instrumented beyond that. |
| Stop | 19:57:20 graceful: `OnApplicationQuit`, `Shutting down`, `ZNet Shutdown`, `Session summary written to Chronicle.` The owner closed it; the shutdown save rewrote every player file from memory. |
| **Not run on 2026-09-22** | Esc to close; the scroll view with a long earned list; the admin `ravenscall title <player>`; the 5-second no-answer hint; a listen host. The `/title` command and the panel are indistinguishable in the server log, so the clear/set cycles above may have been either. |

## Second run, 2026-09-23 morning (same builds, same staging)

`players/TestNomad.json` was edited while the server was down to carry 16 earned titles (Stag Breaker plus fifteen real ones from the mod's own tables, Ghost of the Meadows last); the shutdown save had rewritten the file the night before, which is why the edit has to happen with the server off.

| Step | Result |
|---|---|
| Boot | 06:33: `Loading [TheRavensCall 1.7.0]`, awakens, `PlayerRegistry loaded 3 player record(s).`, Chronicle opened for the day, `World census: 262523 objects in 33 ms.`, join code registered; `/api/state` served TestNomad with 16 `titles_earned` and `active_title` Stag Breaker. Client: crow 1.2.0 loaded. |
| Long list + scroll | **PASS.** TestNomad opened the panel and picked `Ghost of the Meadows` (the 16th and last entry, only reachable by scrolling), then `Boss Hunter` (the 15th): `TestNomad chose the title 'Ghost of the Meadows'` and `… 'Boss Hunter'` in the server log, in that order. |
| Esc | **PASS** for the close itself: client log `title panel closed: escape`. Whether the pause menu also opened on that press was not reported either way; the one-frame gate grace is what is meant to prevent it. |
| Title in narration | **PASS.** A fall death right after was narrated as `TestNomad the Boss Hunter met their end at the hands of falling.` — the picked title is the display name from then on. The one warning of the run, `death report dropped — budget exceeded`, is the known twin drop of the crow's V1+V2 death pair (since 1.2.4), not a title-picker line. |
| Stop | 06:38:26 graceful (`OnApplicationQuit`, `Shutting down`, `ZNet Shutdown`, `Session summary written to Chronicle.`), closed by the owner; the saved record reads `active_title` Boss Hunter. |
| **Still not run** | The admin `ravenscall title <player>`; the 5-second no-answer hint; a listen host. |

Still present, unchanged since 1.3.0: the leave line reads `(1 online)` while counting the leaver (a `FormatMessage` count taken before the peer is removed).
