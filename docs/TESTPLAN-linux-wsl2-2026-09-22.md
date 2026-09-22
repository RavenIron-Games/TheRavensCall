# TheRavensCall on a Linux dedicated server — first boot, 2026-09-22

**Why this exists.** `docs/SCOPE-1.6.0.md` called TLS on Linux "the one unproven piece" and made a
rented Nitrado boot the release gate (§8 step 9). The owner is not renting a server; on his word a
Linux dedicated server under WSL2 on the development laptop stands in for it, on the Valheim version
Storm10 runs. Two builds were booted the same day: the one Steam's `public-test` branch served
(0.221.13) and, on the owner's word ("it needs the 1.0.15 version on the linux"), the default
branch's 1.0.15. The 1.0.15 results are the ones that count; the 0.221.13 results are kept because
they differ in two places that matter.

Result: **TheRavensCall 1.5.0 (the shipped DLL, md5 `47bb2d3e…`) boots on Linux 1.0.15, serves all
five routes, counts a copy of Storm10's world to the object, and `HttpWebRequest` completes https
requests with certificate validation on and nothing installed.** The scope's transport (§3) stands
as written; the Nitrado rental is off the list.

## Setup

| Item | Value |
|---|---|
| Host | the development laptop, WSL2 (kernel `6.18.33.2-microsoft-standard-WSL2`), Ubuntu 26.04.1 LTS, run as root |
| Server | `/opt/valheim`, copied from a Windows-side SteamCMD download (`+@sSteamCmdForcePlatformType linux … +app_update 896660`) |
| Loader | BepInEx 5.4.23.5 (BepInExPack Valheim 5.4.2350, the same core Storm10 runs) through doorstop's `libdoorstop_x64.so`, started by a `start_server_bepinex.sh` that sets the doorstop environment and `LD_PRELOAD` |
| Mod | `HexiumDist/plugins/TheRavensCall.dll` 1.5.0 as shipped, default config (`EnableHttpServer` on, localhost only) |
| Probe | `TlsProbe` 0.2, a throwaway plugin that tries `WebRequest.Create`, a direct `HttpWebRequest(Uri)` constructor and `UnityWebRequest.Get` against `https://example.com/`, `https://api.github.com/zen`, `https://expired.badssl.com/` and `https://self-signed.badssl.com/`, and writes what happened to `BepInEx/config/TlsProbe.txt` |
| Server args | `-nographics -batchmode -name LinuxTest -port 2477 -world <name> -password linuxtest -public 0 -savedir /opt/valheim/saves -logfile /opt/valheim/server.log` |
| Stop | `kill -INT <pid>`; the process logged `Shutting down` and exited within 4 s |

## The two builds

| Install | Steam branch | buildid | `assembly_valheim.dll` | Reports as | Unity |
|---|---|---|---|---|---|
| `LinuxServer` | `public-test` | 23105022 | 2,165,248 bytes, md5 `d6d6179a…` | `l-0.221.13 (network version 37)` | 6000.0.66f2 |
| `LinuxServer115` | `public` (the default) | 25390671 | 2,561,536 bytes, md5 `47df8869…` | `l-1.0.15 (network version 40)` | 6000.0.75f1 |
| Storm10 (Windows, for reference) | — | — | 2,561,536 bytes, md5 `33697297…` | `1.0.15 (network version 40)` | — |

The `public-test` branch was the older build that day: a lower buildid and an older version than the
default branch. Mono is 6.13.0 on both. The Linux and Windows 1.0.15 assemblies are the same size
and differ only as the two platforms' builds do.

## Results on 1.0.15

| # | Result | Evidence |
|---|---|---|
| 1 | PASS — boots and patches | `Loading [TheRavensCall 1.5.0]` → `All patches applied.` → `Player.OnDeath method found` → `TheRavensCall v1.5.0 loaded.` → `HTTP server listening on http://localhost:2112` → `Game data written OK` → `Server systems initialized.`, about 25 s after launch; zero plugin warnings or errors on the Storm10-world boot |
| 2 | PASS — every route answers | `/api/health` `200 {"status":"ok","version":"1.5.0"}`; `/api/state` 200 (144 bytes, nobody known); `/api/activity` 200 (353 bytes); `/api/census` 200 (1,730 bytes); `/api/gamedata` 200 (385,656 bytes) |
| 3 | PASS with a note — census on a fresh world | `World census: classified 79 prefab(s) across 7 groups.` (the classification that throws on 0.221.13, see below) then `World census: 0 objects in 0 ms.` and, once, `GetAllZDOs() returned 0 objects while ZDOMan.instance exists — the reflected field may have been renamed by a game update.` A brand-new world holds no objects until a player loads a zone, so the count is right and the one-line warning is a false alarm on an empty world. Note for a later release: test the reflected field's presence rather than the count |
| 4 | PASS — census on a copy of Storm10's world | The world's `worlds_local/Storm10/` folder (46 files, 9 MB, the chunked 1.0 save format) copied into the Linux server's save dir; the server logged `ZDOMan.LoadChunks - Starting to load 262,520 zdos from 42 Chunks … WorldVersion: 41`. `World census: 262520 objects in 20 ms.` — the Windows run counted the same 262,520 objects in 18 ms. Groups identical to `docs/TESTPLAN-storm10-census-2026-09-22.md`: portals 4/4, beds 5/5, wards 3/0, ships 7/7, carts 0/0, chests 409/0, stations 2/2; the four portals at `-3608, -1252` / `-3727, -2317` / `35, -21` / `-3705, -952`, all `wood`, all `connected true`, `builder_id 721169348`; the five beds name Nomadtest (and "Wubarrk Dev" as one owner); builders `Nomadtest 137 pieces`. The one difference is by design: the four portals read `builder null` and `unknown_builders 1` because `player_ids.json` starts empty on this server and TestNomad never joined it — the Storm10 plan's step 3 is where that name was learned from a connected peer |
| 5 | PASS — `HttpWebRequest` over https, validation on | `WebRequest`'s prefix table has its 4 entries; `WebRequest.Create` → `https://example.com/` **200** (559 bytes) and `http://example.com/` 200; the direct `HttpWebRequest(Uri)` constructor → `https://example.com/` 200 and `https://api.github.com/zen` 200; `https://expired.badssl.com/` and `https://self-signed.badssl.com/` both **rejected** with `WebException TrustFailure … TlsException: Handshake failed - error code: UNITYTLS_INTERNAL_ERROR, verify result: UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED`. No `ServicePointManager` call, no certificate store work, `SecurityProtocol` left at `SystemDefault` |
| 6 | PASS — `UnityWebRequest` over https, validation on | `https://example.com/` **200** (559 bytes); `https://expired.badssl.com/` **rejected**, `ConnectionError … SSL CA certificate error` |
| 7 | PASS — graceful stop | `kill -INT` → `Shutting down` in the server log, process gone within 4 s |

## Results on 0.221.13 (the `public-test` build), for the record

Same loader, same DLL, a fresh world. The probe's text report from this boot was overwritten by the
next boot; these are the lines as logged during the session.

- TheRavensCall 1.5.0 loads, patches and serves `/api/health` and the files, but the census dies
  in `WorldCensus.Run` with `MissingMethodException: Method not found: int
  .StringExtensionMethods.GetStableHashCode(string)`. On that build the extension is
  `GetStableHashCode(this string str, bool addToNameHash = true)`; the DLL was compiled against
  the 1.0.x `libs-Tools` where it is `GetStableHashCode(this string str)`, and Mono binds by exact
  signature. `WorldCensus.cs:93` is the one call (`prefab.name.GetStableHashCode()`); iterating
  `ZNetScene.instance.m_namedPrefabs` (hash → prefab) instead of hashing names would make it
  version-safe. Not urgent: the default branch does not have this signature.
- `HttpWebRequest` cannot complete a request: `WebRequest`'s prefix table is **empty**, so
  `WebRequest.Create` throws `NotSupportedException: The URI prefix is not recognized` for
  `https://` and `http://` alike; registering the prefixes by reflection, or constructing
  `HttpWebRequest(Uri)` directly, gets as far as `GetResponse()` and dies with a
  `NullReferenceException`. The Discord webhook (`HttpWebRequest`) could not have worked there.
- `UnityWebRequest` works on this build too: `https://example.com/` 200, the expired certificate
  rejected with `SSL CA certificate error`.

## What this settles for 1.6.0

- **§3's transport stands**: `HttpWebRequest` on a ThreadPool worker, the Discord webhook's
  pattern, completes https with certificate validation on the build every current server runs.
  The scope's `SecurityProtocol` / `cert-sync` / `mozroots` text is withdrawn — nothing was needed.
  A `TrustFailure` still gets its one named warning, because a rented image with no CA bundle at
  all is untested. `ServerCertificateValidationCallback` stays forbidden (process-global).
- **`UnityWebRequest` is the fallback**, not this release's transport: it works on both builds,
  including the one where `HttpWebRequest` does not, but it is a main-thread coroutine and would
  change the sender's shape. If a future Valheim build ships the empty prefix table, that is the
  move — and it would take the Discord webhook with it.
- **§8 step 9's gate is this box**: one boot of the 1.6.0 build here, pushing over real https to
  the deployed receiver. No Nitrado rental (§10).
- **Two things to flag** outside 1.6.0: the census throws on 0.221.13 (one-line fix, whenever a
  1.5.x lands), and on that build the Discord webhook cannot send at all. Both are moot on 1.0.15.

## How to repeat

Scripts live in the session scratchpad (`wsl-setup.sh`, `wsl-swap-run.sh`, `wsl-world-run.sh`,
`wsl-probe-stop.sh`; run with `MSYS_NO_PATHCONV=1 wsl.exe -d Ubuntu -u root -e sh <path>` from Git
Bash). The Linux download is `C:\Users\donfr\ValheimServers\LinuxServer115` (`steamcmd.exe
+@sSteamCmdForcePlatformType linux +force_install_dir <dir> +login anonymous +app_update 896660
-beta public validate +quit`); `/opt/valheim` holds it plus the BepInEx layout; the Storm10 world
copy sits at `/opt/valheim/saves/worlds_local/Storm10` and is a copy — Storm10's own files were not
touched, and Storm10 was down throughout. The server was stopped at the end of the run.
