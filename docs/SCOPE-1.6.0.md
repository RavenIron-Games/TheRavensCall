# TheRavensCall 1.6.0 — Push to a hosted dashboard

Owner's ask (2026-09-22): *"ok so say the server is on nitrado"*, then *"ok scope the push
design as 1.6.0"*. On a rented game server (Nitrado, G-Portal and the like) the admin gets the
game's ports, a web panel and FTP; no shell, no extra services, no way to open the dashboard's
port. So the dashboard as the mod serves it today (`http://localhost:2112`, localhost-only since
1.2.4) is unreachable from anywhere.

This scope is the implementation contract for the design this release takes, and why. FTP pull
was weighed first: the mod already writes `BarrkBOT_data1.json` into the FTP-reachable config
folder every tick and BarrkBOT reads it that way today, so two more `AtomicWrite` calls would
give any rented-server admin all three envelopes with no receiver, no token and no recurring
cost. It is rejected as the *dashboard* answer because a page cannot poll FTP and an admin
would be pulling files by hand, not because it does not work; a follow-up may still ship those
writes as the offline path. What ships instead: the mod **pushes** its data out over HTTPS, a
small **receiver** on the owner's Netlify team keeps the latest copy and serves it, and the
**same page** reads from the receiver.

Sizes and cadences below were measured on Storm10 (Valheim 1.0.12) on 2026-09-22 with the 1.5.0
build; prices are from the Netlify pricing page read the same day, and the owner's plan is
**Pro, 3,000 credits a month**. This revision takes the edits of the 2026-09-22 design review
(five lenses, 69 findings, 66 verified, 25 must-change), of its second pass (three lenses, 15
findings, 9 must-change verified) and of a final pass (two lenses, 8 findings, 4 must-change). Amended 2026-09-22 after the
mod's first Linux boot (revision 5): §3's TLS bullet, §7's README line, §8 step 9 and §10 —
`docs/TESTPLAN-linux-wsl2-2026-09-22.md` is the evidence. Revision 6 (at implementation, the same
day): the `/s/<id>` → `/s/<id>/` 301 rule is dropped (§4, §5, §8 step 7) — see the `GET /s/<id>` row.
Revision 7 (at implementation, the same day): the site root answered Netlify's default 404 on
the first deploy; a static landing page now serves at `/` and `/s/` redirects to it — nothing
else changes (§4) — see the `GET /` and `GET /s/` rows.

## 1. What ships

- A new `[Push]` config section. With `PushUrl` empty (the default) nothing changes: 1.6.0 on a
  home server behaves exactly like 1.5.0.
- **The push**: on its own schedule the mod POSTs its three envelopes — the same strings
  `/api/state`, `/api/activity` and `/api/census` serve — to `PushUrl`. Outbound only, the same
  path the Discord webhook already uses, so it works from any host.
- **The caches the push splices are built behind the HTTP-server guard today**: `_stateCache`,
  `BuildActivityCache()` and `WorldCensus.MaybeRun()` all sit inside
  `if (Instance != null && Plugin.EnableHttpServer.Value)` in `PollAllPlayers`, and
  `PrimeStateCache`/`BuildActivityCache` return early on the same flag. 1.6.0 widens that
  condition to `EnableHttpServer.Value || PushClient.Enabled` in all three places — the
  `PollAllPlayers` guard and the early returns in `PrimeStateCache` and `BuildActivityCache`;
  `WorldCensus.MaybeRun()` has no flag of its own and is reached through that guard — while
  `StartHttpServer()` stays behind `EnableHttpServer` alone. An admin on a rented box can turn
  off a listener nothing can reach and still push. `PushClient.MaybeSend()` is the last call
  inside that block, after the three assignments, so every push sees this tick's strings.
- **The receiver**: a Netlify site of its own (`hosting/netlify/` in this repo; Functions +
  Blobs) that stores the latest envelopes per server and serves them at the same three routes
  plus `/api/health`, behind the server's own read token, and serves the page as a static file.
- **The page** learns a hosted mode: it finds its API under its own path, sends
  `If-None-Match` and treats `304` as "unchanged", pauses polling while its tab is hidden, and
  shows how long ago the game server last reported.
- Version **1.6.0**. `/api/state`, the BarrkBOT files and `BARRKBOT_CONTRACT.md` are untouched.
- `/api/gamedata` (386 KB on Storm10, written once per boot by `WriteGameData`) is **not**
  pushed: BarrkBOT reads it over FTP. The page does use it — `loadGameData()` runs at boot and
  on every settings save and backs `fishDisplay()` and the resources-harvested category tags —
  so on the hosted page fish and resource names fall back to prettified tokens ("Fish1" rather
  than "Perch"). That is the one visible difference from `localhost:2112`; pushing it once per
  boot is the 1.7.0 follow-up (§9).

## 2. What the server pushes

One request per push:

```
POST <PushUrl>/push
Authorization: Bearer <PushToken>
Content-Type: application/json

{"v":1,"server_id":"storm10","mod_version":"1.6.0","pushed_at":"2026-09-22T17:02:11Z",
 "push_interval_seconds":60,"heartbeat_seconds":600,
 "read_token_sha256":"<hex sha256 of the server's HttpApiToken>",
 "state":"<escaped JSON string>"|null,"activity":"<…>"|null,"census":"<…>"|null}
```

- Each envelope is the exact string the matching route serves — `_stateCache ??
  EmptyStateJson`, `_activityCache ?? EmptyActivityJson`, `_censusCache ??
  WorldCensus.EmptyEnvelope()`, the same fallbacks `ProcessRequest` applies — because
  `PrimeStateCache` and `BuildActivityCache` swallow a fault and leave the field null. `null` in
  the bundle therefore always means "unchanged since the last accepted push" and never "the
  mod had nothing to send".
- Each envelope travels as a **JSON string** (escaped with `Companion.Esc`, the escape path
  `/api/state` has used since 1.2.4), not spliced in as raw JSON: the receiver reads the wrapper
  with the platform's JSON parser and gets the original string back byte for byte, which a
  parse-and-restringify of raw JSON would not (escape form, number formatting). The receiver
  stores those bytes untouched and never parses them. Escaping costs a few percent on a JSON
  body (the quotes); base64 is the fallback if `Esc` proves slow on 300 KB, at a third more.
- An envelope is `null` when it has not changed since the last **accepted** push. "Changed" is
  a byte comparison with the volatile fields blanked: `generated_at` in all three, plus
  `duration_ms` and `scanned_objects` in the census. That is deliberately stricter than the
  page's own `stripGeneratedAt`, which blanks `generated_at` only and whose census gate
  consequently opens on every run (the page says so in a comment). `scanned_objects` is the
  whole loaded-world ZDO count and moves with every spawn and item drop, so a
  `generated_at`-only compare would push the census every cycle and a quiet server would never
  be quiet. Do not reuse `stripGeneratedAt` for the push. Portal `connected`, bed `owner` and
  ward `enabled` still count as real changes. No failure of any kind advances the gate, so the
  first push after a fix carries all three envelopes.
- A **heartbeat** push carries all three envelopes regardless, so a receiver that lost or never
  had them recovers, and so "last reported" keeps meaning "the server is alive".
  `heartbeat_seconds` and `push_interval_seconds` in the body are the **effective** clamped
  values (§3), not the raw config; the receiver validates `heartbeat_seconds` as 60..86400,
  stores it, and treats a missing or invalid value as 600.
- `read_token_sha256` is the hex sha256 of the server's own `HttpApiToken` — the hash, never
  the token, so the plaintext never leaves the game server or lands in an edge log. The
  receiver already holds that hash in its registry, beside the write token's (§4): reads have
  to be authenticable before the server's first push and on routes that read no envelope. The
  field is the consistency check that the admin registered the token the server is actually
  running — a bundle whose field does not equal the registered hash is answered `409` and
  nothing is stored; a missing field is `422`. A hosted dashboard sits on a public URL, so the
  push **refuses to run** unless `HttpApiToken` is at least 24 characters (one warning at boot
  naming the fix). Stricter than the mod's local default, on purpose.
- `server_id` is config `PushServerId`. It has **no default**: it must equal the id registered
  in the receiver's `TRC_SERVERS`, and when it is empty or not `[a-z0-9-]{1,32}` the push is
  disabled at boot with one warning naming the fix, the same treatment as a non-HTTPS `PushUrl`
  (§3). Deriving it from the world name was considered and dropped: every `ConfigEntry` binds in
  `Plugin.Awake` before `ZNet.instance` exists, so no world-derived value can be baked into the
  `.cfg`, and the season-folder sanitizer keeps spaces and case, so "Storm 10" would fail the
  receiver's charset anyway. The receiver resolves the body's `server_id` in its registry and
  compares the bearer's hash to the `w` registered under that id (§4); a mismatch is `401`, the
  same answer an unregistered id gets, because the registry is read id-first and never scanned
  — the receiver cannot tell a wrong token from a valid token registered under another id.
  `409` is reserved for a `read_token_sha256` that is not the registered `r`.

Measured sizes: `state` 12,312 bytes with 2 known players (about 6 KB per known player, so a
server 30 players have ever joined is around 180 KB); `activity` 5,796 bytes (the 200-event cap
is roughly 50 KB); `census` 1,765 bytes (the 500-row caps make about 60 KB the ceiling). Worst
case a push is about 300 KB; a typical one is 20–200 KB.

## 3. Schedule, backoff, TLS, cost on the game server

- `PushClient.MaybeSend()` runs from the poll tick (`PollAllPlayers`, every
  `StatsPushIntervalSeconds`) like the census does, and no-ops until `PushIntervalSeconds` have
  passed since the last attempt. Default 60, clamped 15..3600. Because it only runs on the poll
  tick, the effective cadence rounds up to the next multiple of `StatsPushIntervalSeconds`
  (default 10 s): `PushIntervalSeconds = 15` pushes every 20 s, and a `StatsPushIntervalSeconds`
  of 120 caps the push at 120 s whatever this is set to. The config description says so and the
  boot line reports the effective value.
- `PushHeartbeatMinutes` default 10, clamped 1..1440; the effective heartbeat is
  `max(PushHeartbeatMinutes × 60, effective interval)`, one warning at boot when the config value
  had to be raised, and the interval gate is never bypassed — a heartbeat only lands on a tick
  already allowed to push.
- Nothing changed and no heartbeat due → no request at all.
- One request in flight at a time, on a ThreadPool worker, the Discord webhook's pattern
  (`HttpWebRequest`, no `HttpClient`). The main thread assembles the bundle from strings it
  already holds and never waits on the network. `PushTimeoutSeconds` is 20, deliberately above
  the receiver Function's own execution budget (Netlify's synchronous default is 10 s), so a slow
  receiver returns its own error instead of the mod abandoning a push the receiver may already
  have stored. The page's own fetch timeout is unchanged.
- The failure path reads the status code off the response: on `HttpWebRequest` a non-2xx
  arrives as a `WebException`, so the handler casts `ex.Response` to `HttpWebResponse` and
  switches on `StatusCode`. The Discord webhook logs `ex.Message` only and cannot tell codes
  apart; it is not copied as written.
- The mod measures the assembled bundle before sending: over 2 MB it drops the largest envelope,
  sends the rest and logs one warning naming `server_id` and the size — at about 6 KB of `state`
  per known player, a server a few hundred players have visited reaches 2 MB on its own and
  would otherwise be rejected forever.
- Failure → exponential backoff: 60 s, 2, 4, 8, then 15 minutes flat. One warning on the first
  failure, one info line on recovery, never a line per attempt — but while the push keeps
  failing the warning is re-logged at most once an hour with the consecutive-failure count, the
  time of the last success and the last error text. A rented server's admin has no shell and
  cannot reach `/api/health`; the BepInEx log pulled over FTP is the only place a broken push
  can be seen, and one line at boot will be long gone.
- The receiver's four configuration refusals are named, each retrying every 15 minutes: `401`
  "PushToken rejected — check [Push] PushToken and PushServerId against the receiver's
  registry"; `409` "the receiver has a different HttpApiToken hash registered for '<id>' —
  re-register read_token_sha256 and redeploy"; `422` "the receiver refused the bundle — set
  [Companion] HttpApiToken (24+ characters)"; `413` "bundle over the receiver's 2 MB limit". A `429` is not a failure: no warning, no backoff step, the change gate
  untouched, retry on the next due tick honouring `Retry-After`. Tokens never appear in a log
  line; neither side logs a body, a hash or a header value.
- `PushUrl` is parsed with `new Uri()` and accepted only when its scheme is `https`, or `http`
  with `Uri.IsLoopback` true (the test harness) — never a string-prefix test, which
  `http://localhost.example.com/` would pass. Anything else disables the push at boot with one
  warning naming the host. `PushUrl` set while `HttpApiToken` is short or empty likewise
  disables the push at boot rather than sending 1,440 refused requests a day.
- **TLS on Linux — proven 2026-09-22** (`docs/TESTPLAN-linux-wsl2-2026-09-22.md`). Under
  BepInEx the push runs on Valheim's bundled Mono, and until that run the Discord webhook had
  only ever run on Windows. On the Linux dedicated server build that Storm10 and every current
  server runs (1.0.15, network version 40, Steam's default branch) `HttpWebRequest` completes
  https requests with certificate validation on and nothing installed or configured: an expired
  and a self-signed certificate are both rejected with `TrustFailure` (UnityTLS,
  `UNITYTLS_X509VERIFY_FLAG_NOT_TRUSTED`), so there is no `SecurityProtocol` change, no
  `cert-sync`, no README fix to write. The mod still logs one warning naming the cause on a
  `TrustFailure` or `SecureChannelFailure`, because a rented image with no CA bundle at all is
  untested. It does **not** install a `ServerCertificateValidationCallback`: that property is
  process-global and would disable certificate validation for the Discord webhook in the same
  process. One caveat stands: on the older build Steam's `public-test` branch carried that day
  (0.221.13, network version 37) `System.dll`'s `WebRequest` prefix table is empty and
  `HttpWebRequest` cannot complete a request at all, while `UnityWebRequest` (a main-thread
  coroutine) works with validation on both builds — it is the fallback transport if a future
  build ships that way, not this release's. §8 step 9 is the release gate for this.
- **Shutdown.** No final push is attempted: the sender is a background worker, a request in
  flight dies with the process, and `Plugin.OnDestroy` on a dedicated server is already a
  destruction-order minefield (the 1.4.1 shutdown-row race) that a blocking network call has no
  business in. The receiver keeps the last bundle, which can still show players online — that
  is what `age_seconds` (§4) and the greyed-out stale state (§5) are for.
- Bandwidth from the game server: at most about 300 KB a minute while players are online, so
  under 450 MB a day worst case and typically far less; nothing while idle beyond heartbeats.
  No gzip in 1.6.0 (a follow-up; `HttpWebRequest` can send `Content-Encoding: gzip`, but the
  receiver platform's handling of compressed request bodies has to be proven first).

## 4. The receiver

**Where.** A Netlify site **of its own** in the owner's team — not the site serving
ravenirongames.com, so an abusive month cannot take the main site down with it — at
`trc.ravenirongames.com` (the owner's hostname, decided 2026-09-22; `dash.ravenirongames.com` was the placeholder until then), built from `hosting/netlify/` in this repo:
`netlify.toml`, `netlify/functions/push.mts`, `netlify/functions/api.mts`, the dashboard page and the landing page as static
file, and a README with the deploy steps (`netlify init`, the env var, the custom domain, the
spend cap, the rate-limit rule).

**Registry.** An env var `TRC_SERVERS` holding JSON `{"<server_id>": {"w": "<sha256 of that
server's write token>", "r": "<sha256 of that server's HttpApiToken>"}}`, set in the Netlify
UI. Both hashes are registered at deploy time, because reads have to be authenticable before
the server's first push and on routes that read no envelope (`/api/health`, `/api/gamedata`).
It is read **id-first**: `POST /push` resolves the body's `server_id` and compares the bearer's
sha256 to `w` under that id; it never scans the registry for a matching hash. Ids are held to
`[a-z0-9-]{1,32}` when the variable is read, and an entry whose id fails or whose `w` or `r` is
not 64 hex characters is ignored with a log line, so a typo in the UI can never become a
storage key; a registry in which two ids share a `w` is rejected at load, so two servers
accidentally configured with the same write token can never write to one id. Tokens are never
stored in the clear. `TRC_SERVERS` reaches the Functions at **deploy** time: a
change in the UI does nothing until the site is redeployed, so adding a server — or revoking a
leaked write token — is "edit the variable, then trigger a deploy", and the README says so. The
variable shares the function's 4 KB environment block, about 20 entries at two hashes per id,
which bounds the
registry until §9's self-serve work replaces it; if revocation ever has to be immediate rather
than one deploy away, the registry moves into Blobs under the key `registry`, seeded from
`TRC_SERVERS` on the first deploy. No self-serve registration in 1.6.0 (§9).

**Storage.** Netlify Blobs, store `trc`, keys `<server_id>/state`, `<server_id>/activity`,
`<server_id>/census` and `<server_id>/meta`. Each envelope blob carries in its own Blobs
metadata the body's sha256 (the `ETag`), written at push time, so a gated read — the token
having already been checked against the registry — is one strongly consistent
`getWithMetadata`, and an `If-None-Match` hit is a `getMetadata` with no body fetched. `<server_id>/meta` holds `received_at` (the receiver's own
clock), `pushed_at` (the server's claim, echoed only), `mod_version`, `heartbeat_seconds`,
sizes, and the rate-limit counter. Latest copy only; envelopes are opaque bytes end to end.
Blobs is eventually consistent by default (an update reaches every edge within 60 s, the docs
say), which would hand the page a minute-old copy on a 60 s push cadence, so every read uses
`consistency: "strong"`; the docs cap an object at 5 GB and its metadata at 2 KB, neither near
a 300 KB envelope.

**Validation.** Every value taken from a pushed body is checked before it is stored or echoed —
`mod_version` against `[0-9A-Za-z.+-]{1,32}`, `pushed_at` as an ISO-8601 instant,
`heartbeat_seconds` as 60..86400, `server_id` against `[a-z0-9-]{1,32}`,
`read_token_sha256` as 64 hex characters — and anything that fails is `422` with nothing
stored. Every receiver response is produced by the platform's JSON serializer, never by string
concatenation. The only string that ever becomes a storage key is a validated registry id.

**Routes.**

| Route | Behaviour |
|---|---|
| `POST /push` | `401` missing bearer, or a bearer whose sha256 is not the `w` registered under the body's `server_id` — the same body whether that id is registered or not, so nobody can enumerate servers; `409` the body's `read_token_sha256` is not the `r` registered under that id; `413` body over 2 MB; `422` a field fails validation or `read_token_sha256` is missing; `429` more often than every **10 s** per server (below the mod's 15 s floor, so a correctly configured server is never throttled by tick jitter; the counter lives in `<server_id>/meta`, read and written strongly — a Function keeps nothing between invocations, so a module-level counter would limit one warm instance and let every cold or concurrent one through; the response carries `Retry-After`); else `200 {"ok":true,"stored":["state","census"]}` naming the envelopes that were non-null |
| `GET /s/<id>/api/state`, `/activity`, `/census` | read token in the `X-Api-Token` header **only**, hashed and compared to the registry's `r` for `<id>` before anything is looked up in Blobs, else `401 {"error":"token required"}` — the same body for a wrong token and an unregistered id, so nobody can enumerate servers; a request carrying a `token` query parameter is answered `400 {"error":"use the X-Api-Token header"}` and the parameter is never logged (a hosted URL has to stay safe to paste into a chat window; `?token=` stays supported on the mod's own `localhost:2112`, unchanged). Then the stored envelope, `Content-Type: application/json`, `ETag` = its sha256, `Cache-Control: private, no-cache`, `Vary: X-Api-Token`; `If-None-Match` matching → `304`. Before the first push carrying that envelope: **`200` with an empty envelope of the right shape** plus `"no_data":true` — never `503`, because `apiFetch` throws on any non-2xx and the first thing the owner would see on a fresh server would be "Could not reach …". For the census the receiver cannot reproduce the mod's own empty envelope (it is built from the game server's `CensusIntervalMinutes`, which no push carries), so it sends `enabled:true`, `interval_minutes:0`, empty groups and lists, and the page tests `no_data` before the `enabled`/`generated_at` guards (§5). A no-data response carries no `ETag` and is sent `Cache-Control: no-store`, so a page that has not yet seen a real envelope is never answered `304` |
| `GET /s/<id>/api/health` | read token required, exactly like the data routes (the page has it and fetches health with the state poll); `{"status":"ok","hosted":true,"pushed_at":"…","age_seconds":N,"stale_after_seconds":M}` — no `version`: which build an admin runs is the admin's business. `age_seconds` is measured from `received_at`, the receiver's own clock, never from `pushed_at`, so a skewed game-server clock or a forward-dated push from a stolen write token cannot make the dashboard say "just now" about a server that is down. `M` = 3 × the `heartbeat_seconds` of the last accepted push. The mod's own `/api/health` on `localhost:2112` stays open and unchanged: it is open because it is not reachable off the machine, and that reason does not travel to a public URL where the same route would be a liveness oracle |
| `GET /s/<id>/api/gamedata` | token-gated like the others; `200 {"recipes":[],"items":[],"buildables":[]}` so no 404 lands in the network log |
| `GET /s/<id>` | the page, exactly as `/s/<id>/` — revision 6: **no `301` rule**, because Netlify's edge matches redirect rules regardless of a trailing slash (its docs, "Trailing slash"), so a `/s/:id` → `/s/:id/` rule also matches `/s/<id>/` and redirects forever (seen under `netlify dev` at implementation); the page's hosted-id match accepts both forms instead |
| `GET /s/<id>/` | the release's `theravenscall.html` served as a **static asset**. `netlify.toml` declares the receiver's routes as ordered `[[redirects]]` rules, most specific first, because Netlify evaluates them top to bottom, the first match wins, and a `*` splat matches across `/`: `/push` → `/.netlify/functions/push` (200); `/s/:id/api/*` → `/.netlify/functions/api` (200, `force = true`); then `/s/` → `/` (302, revision 7); and only then the catch-all `/s/*` → `/theravenscall.html` (200) that serves the page. The functions are reached through these rules, not through a `config.path` declaration, so the order lives in one file; an unknown path under `/s/<id>/api/` is the function's own 404, anything outside `/`, `/push` and `/s/` falls through to the site's 404. The page derives its hosted base from its own location (§5). Netlify serves static assets from the CDN byte for byte and cannot template one per id, so nothing is injected; a page view is one CDN request and no compute |
| `DELETE /s/<id>/api` | write token in the `Authorization: Bearer` header; removes the four blobs and answers `200 {"deleted":true}` — how an admin unpublishes. It sits under `/s/<id>/api/` because the `/s/:id/api/*` rule is the only `/s/` rule that reaches a function: the rules match on path alone, so a `DELETE /s/<id>` would take the `/s/*` catch-all and never reach code. Deleting a registry entry takes the dashboard offline on the next request; the stored copy stays until this runs |
| `OPTIONS /s/<id>/api/*` | `204` with the CORS headers below |
| `GET /` | revision 7: a static landing page — no data, no ids — that says where a server's dashboard lives (the `/s/<server-id>/` pattern), served as a static asset with no function invocation |
| `GET /s/` or `GET /s` | revision 7: `302` to `/` — Netlify matches this rule with or without the trailing slash, and it sits above the `/s/*` catch-all so the empty remainder after `/s/` does not fall through to the dashboard with no id |
| anything else | `404` |

The read routes send `Access-Control-Allow-Origin: *`, `Access-Control-Allow-Methods: GET,
OPTIONS`, `Access-Control-Allow-Headers: X-Api-Token, If-None-Match` and
`Access-Control-Expose-Headers: ETag` — the mod's own listener already sends the first three —
so §5's Base URL override can point a page served anywhere at the receiver; the data is
token-gated either way, so `*` grants a browser nothing `curl` does not have. `POST /push`
needs no CORS. A `304` carries the same CORS headers as a `200`, or the one case they exist
for breaks. Every hosted response carries `X-Content-Type-Options: nosniff` and
`Referrer-Policy: no-referrer`; the dashboard page is served with `Content-Security-Policy:
default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'
https:; img-src 'self'; frame-ancestors 'none'; base-uri 'none'` — the page is one inline script
with inline `style` attributes and loads no images, and `connect-src https:` is what keeps §5's
Base URL override working from a hosted page (a typed base receiving the token is the admin's
own act) — so a future escaping slip in player-supplied text cannot load a foreign script or
frame the page. The landing page at `/` (revision 7) carries `default-src 'none'; style-src
'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'` — no `script-src`, `connect-src` or
`img-src`, because it has no script, fetches nothing and loads no images. Function routes set
their headers in code, and the dashboard page and the landing page each get their own
`[[headers]]` block in `netlify.toml`; the sets are not the same and the file says which is
which. The hosted read routes have no counter of their own to throttle by — a Function keeps
nothing between invocations, and a Blobs write per read would cost more than the read — so the
guessing-rate limit on `/s/*` is a Netlify Firewall Traffic Rule per source address, set in the
UI as a setup step, with what it enforces recorded at §8 step 8.

**Cost.** The owner's plan is Netlify **Pro, 3,000 credits a month**, a team allowance shared
with ravenirongames.com; the README's first step is to read the team's current monthly use in
the Netlify UI and note it, because the receiver's budget is 3,000 minus that. Rates from the
pricing page (2026-09-22): web requests 2 credits per 10,000; compute 10 credits per GB-hour at
the default 1 GB; bandwidth (egress) 20 credits per GB; Functions and Blobs included; a
production deploy 15 credits. Budget **250–400 ms per invocation** until measured:
`consistency: "strong"` sends each read to the blob's origin region rather than the edge, and
§8 step 8 replaces this estimate with the measured figure.

- *Per server*: at most 43,200 pushes a month (60 s while players are online) = 8.6 credits of
  requests + about 3.6 GB-hours at 300 ms = 36 credits of compute, so roughly **45 credits a
  server-month** worst case, and nothing while nobody is watching.
- *Per open dashboard*: state, activity and health every 10 s and census every 60 s is 19
  requests a minute, 1,140 an hour: about 0.2 credits of requests, about 1 credit of compute,
  and egress only when a body changed (a `304` still costs a request and an invocation, just no
  bytes) — call it **1.2–2.5 credits a viewer-hour**. The page polls on `setInterval` with no
  visibility handling today, so a tab left open on a second monitor would bill around the
  clock (730 viewer-hours a month, 900–1,800 credits); 1.6.0 therefore pauses all four polls
  while `document.visibilityState === 'hidden'` and refetches once on `visibilitychange` (§5).
- *So*: two or three of the owner's servers plus a few viewer-hours a day is on the order of
  **150–250 credits a month** of the 3,000, and the extra cost is $0 unless viewing habits
  change by an order of magnitude.
- *That is honest use only.* `/push` and the hosted routes are public and a request rejected
  with `401` is still a billed invocation: at the rates above, 10 requests a second from one
  host is 864,000 requests a day — about 170 credits of requests and, at the 300 ms budgeted
  above, about 720 of compute, roughly 900 credits a day, the Pro month in three or four days.
  Before the DNS record points anywhere:
  set the spend cap in the Netlify UI; add a Firewall Traffic Rule rate limit (the pricing page
  lists "Firewall Traffic Rules & basic rate limiting" on every plan) on `/push` and `/s/*`; and
  record here what those rules actually enforce on this plan. The per-id 10 s limit and the
  2 MB cap bound what a *valid* write token can cost (about 17 GB of ingest a day); they do not
  bound an unauthenticated flood, which is why the spend cap is a setup step, not advice.
- *This estimate holds only if inbound request bodies are not metered as bandwidth*, which is
  confirmed on the pricing page or with Netlify support **before a line of the receiver is
  written**, not at implementation. If inbound is metered, one server's pushes are about 2 GB a
  month at typical sizes (43 credits) and 13 GB at the worst case (260 credits), and the design
  changes: 5-minute default cadence, gzip moved into 1.6.0, or a platform that meters egress
  only.

**Why Netlify and not the Cloudflare Worker the site's old address points at.** Both work:
Workers Free allows 100,000 requests a day, and while Workers KV's 1,000 writes a day is below
one server's pushes, R2 or D1 carries it comfortably. The reason is co-location, not capacity:
the domain, the site and the team are already on Netlify, so the receiver is one more site
rather than a second platform to operate. Blobs' own per-operation limits are not on the credit
table and are not documented as unlimited; §8 step 8 reads actual usage after 24 hours, and if
Blobs operations turn out to be metered or capped, Workers + R2 is the fallback this paragraph
keeps open.

## 5. Dashboard changes

- **Hosted mode.** The page derives its base from its own location:
  `location.pathname.match(/^\/s\/([a-z0-9-]{1,32})(?:\/|$)/)` gives the hosted id (both `/s/<id>` and
  `/s/<id>/`, revision 6) and the base `/s/<id>`; when the path does not match, today's behaviour. The Settings Base URL override
  still wins when set, so the page can also be opened from anywhere and pointed at a receiver
  or at a mod on the admin's own machine; when hosted and an override is set, the Settings panel
  shows one line naming the receiver it is bypassing.
- **Per-id settings.** In hosted mode the saved token and Base URL are namespaced by the hosted
  id (`trc_token:<id>`, `trc_base_url:<id>`). Today's keys are fixed and every hosted dashboard
  shares one origin, so without this opening `/s/b/` would overwrite the token saved for
  `/s/a/`. On a page that is not hosted both keys behave exactly as today.
- **Conditional fetches.** `apiFetch` keeps a per-route `ETag` map, sends `If-None-Match` when
  it holds one, passes `cache: 'no-store'` so the browser's own revalidation cannot shadow the
  header, and returns an "unchanged" sentinel for `304` **before** the existing `!res.ok` throw
  (`304` is not `res.ok`; otherwise every unchanged poll would paint "Could not reach …" and the
  red dot over a current dashboard). The sentinel takes the path an unchanged body takes today:
  no parse, no re-render, but `pollState` still sets `lastUpdated`, `connStatus = 'ok'`, clears
  the error and calls `renderHeader()` — except that the sentinel leaves `connStatus` as it
  found it when that value is `waiting` — and `pollActivity`/`pollCensus` still take their
  `wasUnauthorized` re-render branch so a fixed token clears the notice. The map is cleared
  wherever `saveSettings()` clears the compare gates, so a settings save is always answered with
  a full body. Against the mod's own listener nothing changes (it sends no `ETag`).
- **Visibility.** All four polls pause while `document.visibilityState === 'hidden'` and run
  once immediately on `visibilitychange` back to visible — the cost control in §4.
- **Freshness.** Driven by the `/api/health` **body**, not by hosted mode: the page polls
  `<base>/api/health` alongside the state poll. The body is reduced to two derived values the
  moment it arrives — `state.serverReportedAt = Date.now() - age_seconds * 1000` and
  `state.staleAfterSeconds` — and only when both `age_seconds` and `stale_after_seconds` are
  numbers; a body without them (the mod's own `{"status":"ok","version":"…"}`) clears both and
  renders no extra line at all, exactly as 1.5.0 does, where keying on hosted mode would print
  "reported NaN ago" the moment the override points at a real mod. `renderHeader()`, which
  already runs every second, recomputes the line from `state.serverReportedAt` — "server
  reported 2m ago", then "server silent since <time>" (that value formatted with `fmtAbs`) once
  the age exceeds `staleAfterSeconds` — so it stays honest between health polls and while a
  health fetch is failing. `pushed_at` is never rendered: it is echoed for the record only, and
  measuring age from the receiver's clock buys nothing if the page turns the server's own claim
  back into a displayed time. The grey-out of the roster, feed and World panel is a CSS class
  that `renderHeader()` toggles on `#app` and `#worldPanels`, never markup emitted by the
  renderers: in the `304` steady state the sentinel path calls `renderHeader()` and nothing
  else, and a silent server sends no new payload, so a grey-out that waited for a re-render
  would never arrive in the one case it exists for. That is what keeps a clean shutdown from
  leaving a stale list of online players looking live.
- **Waiting.** `state.connStatus` gains a fourth value, `waiting`, and the stylesheet a fourth
  rule, `.conn-dot.wait { background: var(--gold); box-shadow: 0 0 8px var(--gold); }` — the
  palette has no amber today, `--gold` is it. `pollState` parses the body **before** it touches
  `connStatus` (today it sets `lastUpdated`, `connStatus = 'ok'` and clears the error first): an
  envelope carrying `"no_data":true` sets `connStatus = 'waiting'`, leaves `state.stateData`
  null, calls `render()` and returns — `render()` is the only thing that paints the
  not-yet-connected branch (with "Registered, waiting for <id> to report" in place of
  "Connecting…") and it calls `renderHeader()` on its first line, so the amber dot and the new
  connection text arrive with it, while `renderHeader()` still writes no world/day/online strip
  over a server that has never reported. The once-a-second header tick
  (`if (state.stateData) renderHeader()`) is unchanged and is a no-op while waiting: the
  waiting line carries no relative time. `renderHeader()`'s dot class and its connection text
  learn the new value, and the error banner and auto-open-Settings paths treat `waiting` as
  neither ok nor error. `renderActivity()` and `renderCensus()` test `no_data` on their own
  envelope before every other guard and write the same line into the season, feed and World
  panels. The state is left the moment a body arrives without the flag, within one push
  interval of the server's first push.
- **Copy.** In hosted mode the Settings panel relabels the first field "Base URL override
  (hosted: <id>)", its placeholder becomes "(blank = this server's hosted API)", and the
  footer's connection help reads the hosted text — what the receiver is, that the API token is
  still the server's `HttpApiToken`, that the game server pushes rather than listens — instead
  of today's `localhost:2112` / `HttpBindAllInterfaces` text, which is the opposite of what a
  hosted admin needs.
- **Known difference.** Fish and resource names on the detail view fall back to prettified
  tokens on the hosted page (§1, `/api/gamedata` not pushed).
- Everything else is the same file serving `localhost:2112` and the receiver.

## 6. Security

- HTTPS only for the push (§3). Write token per server, generated randomly (`openssl rand -hex
  24` in the README), one warning at boot when it is under 32 characters; sent only in the
  `Authorization` header, stored only as a sha256, never logged on either side. The receiver
  logs id, status and byte counts only, and its error handler never echoes a request.
- A read token of 24+ characters is required to be hosted (§2). `HttpApiToken` has no length or
  entropy rule anywhere in the mod today; `HttpApiToken = storm10` is fine on a localhost
  listener and a guessable public secret here. The README's setup step says to generate it
  randomly. Only its sha256 ever leaves the game server. A bare sha256 is only as strong as the
  token behind it; the length rule and the per-address firewall rate limit on `/s/*` (§4) are
  what make it enough. The page keeps the token in `localStorage` as today, namespaced by id.
- Receiver caps: 2 MB body, 10 s per server on `/push`, a per-address firewall rate limit on
  `/s/*`, ids `[a-z0-9-]{1,32}`; every value from a push validated before it is stored or echoed; the
  only string that becomes a storage key is a validated registry id; envelopes are opaque bytes
  end to end.
- **What hosting publishes.** A read-token holder sees everything the three pushed routes
  serve: every known player's stats, skills, titles and death coordinates (`/api/state`); the
  event feed and season standings (`/api/activity`); and the census, which lists every portal,
  bed and ward in the loaded world with its rounded x/z — in effect where the bases are. Until
  1.6.0 all of that stayed on a machine the admin controls; hosting moves the latest copy to a
  third party's storage behind one shared token. The README's hosted section says this in these
  words, and says that leaving `PushUrl` empty is how an admin keeps it local.
- Denial of wallet: the per-id rate limit and the 2 MB cap bound what a valid write token can
  cost; they do not bound an unauthenticated flood, which is why the spend cap and the firewall
  rate-limit rule are setup steps (§4). Revoking a leaked write token is "edit `TRC_SERVERS`,
  redeploy".
- Response headers: `nosniff`, `no-referrer`, `private, no-cache` with `Vary: X-Api-Token` on
  token-gated bodies (no shared cache can hand one viewer's payload to a request that carried no
  token), and the page's CSP (§4).

## 7. Docs and versioning

- Version 1.6.0 in `Saga.cs`, `HexiumDist/manifest.json`, the README badge; the release name is
  the owner's.
- `HexiumDist/CHANGELOG.md`: a `[1.6.0]` section on top with the "packaged DLL not rebuilt" note
  where 1.5.0's sat (replaced at the cut). `HexiumDist/README.md`: a "Hosted servers (Nitrado,
  G-Portal)" section — what hosting publishes, the setup in five steps (generate both tokens;
  set `HttpApiToken`, `PushUrl`, `PushToken`, `PushServerId`; register the id with both
  tokens' hashes in `TRC_SERVERS` and redeploy; open `https://trc.ravenirongames.com/s/<id>/`;
  how to unpublish), the `[Push]` rows in *Configuration*, and a line in *The Web Dashboard* on
  what the hosted page shows that the local one does not; until §8 step 9 has run, the section
  says "run on a Linux dedicated server (Valheim 1.0.15, under WSL2), not yet on a rented
  host". `docs/API.md`: the push contract
  and the hosted routes, and its title bumped from "the 1.4.0 contract". `docs/DASHBOARD.md`:
  hosted mode, the conditional fetches, the visibility gate, and a rewrite of the token/settings
  flow, which currently says a blank Base URL means this page's own origin.
  `hosting/netlify/README.md`: deploy, env var and redeploy, domain, the spend cap and the
  rate-limit rule, the page-copy step that belongs to every cut from 1.6.0 on, and one line
  saying nothing in it hardcodes the owner's domain, ids or tokens. `HANDOFF.md`.
- `BARRKBOT_CONTRACT.md` untouched.

## 8. Test plan

1. Build: 0 warnings, 0 errors; the inline script passes `node --check`.
2. **Local receiver** (`netlify dev` on `http://localhost:8888`), Storm10 with
   `PushUrl = http://localhost:8888`, `PushToken`, `PushServerId` and a 24+ character
   `HttpApiToken` set: the first push lands within the effective interval of boot and carries
   all three envelopes; one log line; the stored bytes equal `curl localhost:2112/api/state`
   (and `/activity`, `/census`) with `generated_at`, `duration_ms` and `scanned_objects` blanked
   on both sides — the local caches are rebuilt with a new timestamp every tick, so a literal
   comparison across two ticks differs on a perfectly good push.
3. **Change gate**: nobody online, no events → only heartbeats: 3 requests in 30 minutes at the
   default, 4 if the in-game day turns (`day` comes from `EnvMan` and advances with nobody
   connected, so `state` can legitimately change on its own); a player joins → `state` in the
   next bundle, `activity` with the join row; a census run over a world nobody has built in →
   `census` null across at least two consecutive runs.
4. **HTTP server off**: `PushUrl` set and `EnableHttpServer = false` → the hosted page still
   shows the roster, feed and World panel.
5. **Failures**: stop the receiver → one warning, attempts at 60/120/240 s visible in the
   receiver's log once it is back, the hourly re-log with the failure count, one info line on
   recovery, and the next bundle carries all three; a wrong `PushToken`, or a `PushServerId`
   whose registered `w` is not this token's → `401`, one named warning, a retry 15 minutes
   later; a `read_token_sha256` that is not the registered `r` → `409` and its named warning;
   `PushUrl = http://localhost.example.com/` → disabled at boot
   naming the host; `HttpApiToken` of 10 characters → disabled at boot naming the fix;
   `PushIntervalSeconds = 15` → pushes 20 s apart; a receiver `429` → no warning, no backoff.
6. **Receiver refusals** (curl against `netlify dev`, no game server needed): a 3 MB body →
   `413` and nothing stored; a burst inside the 10 s window → `429` with `Retry-After` from the
   second on, the first one's envelopes still readable; a bundle whose `server_id` is not the
   bearer's → `401` and nothing stored; ids `../trc`, `a%2fb`, a 33-character id and an empty
   string → `422`, never a blob key written; a missing `read_token_sha256` → `422`, one that is
   not the registered `r` → `409` and nothing stored; a read with no token, a wrong token, an
   unregistered id and the right token → `401`, `401`, `401`, `200`; a read with `?token=` →
   `400`; `/s/<id>/api/health` without a token → `401`, and with the right token before any push
   → `200` with `age_seconds` absent; `/s/<id>/api/state` before any push → `200` with
   `"no_data":true`. Confirm the store
   afterwards holds exactly `<id>/state|activity|census|meta` and nothing else, and that
   `DELETE /s/<id>/api` removes them.
7. **Hosted page**: open `http://localhost:8888/s/storm10/` **before** the first push →
   "Registered, waiting for storm10 to report" with the amber dot, replaced within one push
   interval; then the same roster, feed, season and World panel as `localhost:2112` from the
   same payloads, the detail view's fish names being the one expected difference; the token
   notice and recovery; `304` responses in the network log after the first fetch of each route;
   "server reported Ns ago"; the tab hidden for 5 minutes → no requests in the receiver's log,
   one burst on return; stop Storm10 → "server silent since …" and the greyed panels once
   `stale_after_seconds` passes; `/s/storm10` without the slash → the same page, same id; a second id's
   page → its own token field, the first id's token untouched.
8. **Real site**: deploy to `trc.ravenirongames.com`, repeat 2 and 7 against it, measure the
   Function duration and after 24 hours read the credit usage in the Netlify UI into the
   changelog, and confirm the spend cap and the rate-limit rule are in place.
9. **Linux — the release gate, not optional** (the WSL2 dedicated server at `/opt/valheim`,
   no rented box: the owner's call, 2026-09-22): one boot of the 1.6.0 build on it, on Valheim
   1.0.15, checking the BepInEx log and the point of the step: a real **https** push to the
   deployed receiver completing and landing. The mod's first Linux boot is already done — 1.5.0
   on 2026-09-22, `docs/TESTPLAN-linux-wsl2-2026-09-22.md`: it boots, serves, counts a copy of
   Storm10's world to the object, and `HttpWebRequest` completes https with certificate
   validation on. No earlier step exercises TLS at all (2 and 7 are plaintext localhost, 8 runs
   from Windows). Until this passes, the README says "not yet run on a rented host" and the
   changelog claims no Nitrado support.
10. **No regression**: with `PushUrl` empty the 1.5.0 test plan still passes on
    `localhost:2112`, including the page against a 1.5.0 server (no extra header line, no
    `NaN`).

## 9. Non-goals

- Hosting **other admins' servers on the owner's receiver** — the owner's word (2026-09-22) is
  "not yet". Opening it later is **not** a receiver change only: every hosted dashboard shares
  one origin and therefore one `localStorage`, so any tenant's stored token is readable by
  script on any other tenant's page. Hosting another admin's server means giving each server
  its own origin (`<id>.trc.ravenirongames.com`) plus the page and receiver changes that
  follow; that cost belongs to whoever takes the decision.
- A second admin deploying their **own private copy** of `hosting/netlify/` is a different
  thing and stays possible by construction: nothing in it hardcodes the owner's domain, ids or
  tokens — every such value is an env var or comes from the request's own path. The README
  says so in one line and offers no support for a copy the owner does not run.
- Pushing `/api/gamedata` (once per boot, 386 KB) and `/api/pins` — the 1.7.0 follow-up that
  removes the fish-name difference.
- History, graphs, or keeping anything but the latest envelope.
- gzip on the push, WebSockets or live updates, more than one receiver, the FTP-pull writes
  (a follow-up, not this release).
- Any change to what the mod serves on `localhost:2112`.

## 10. Open for the owner

- The release name.
- ~~The hostname~~ — settled 2026-09-22: `trc.ravenirongames.com` (the placeholder had been `dash.ravenirongames.com`); adding it to the site is the owner's step.
- ~~Renting a Nitrado test server for step 9~~ — settled 2026-09-22: no rented server; the
  WSL2 Linux dedicated server is the step 9 box (`docs/TESTPLAN-linux-wsl2-2026-09-22.md`).
- Whether the conditional-fetch rewrite (§5) ships in 1.6.0 or 1.6.1: it touches `apiFetch`,
  which every existing localhost install's pollers run every 10 s. Deferred, the receiver would
  still send `ETag` and the browser would revalidate on its own, the cost table stands (it
  already prices the no-`304` case), and step 7's "304 responses" moves with it. The scope
  keeps it in 1.6.0.
