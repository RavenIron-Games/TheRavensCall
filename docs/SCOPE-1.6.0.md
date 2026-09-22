# TheRavensCall 1.6.0 — Push to a hosted dashboard

Owner's ask (2026-09-22): *"ok so say the server is on nitrado"*, then *"ok scope the push
design as 1.6.0"*. On a rented game server (Nitrado, G-Portal and the like) the admin gets the
game's ports, a web panel and FTP; no shell, no extra services, no way to open the dashboard's
port. So the dashboard as the mod serves it today (`http://localhost:2112`, localhost-only since
1.2.4) is unreachable from anywhere. This scope is the implementation contract for the only
design that works there: the mod **pushes** its data out over HTTPS, a small **receiver** on the
owner's site keeps the latest copy and serves it, and the **same page** reads from the receiver.
Sizes and cadences below were measured on Storm10 (Valheim 1.0.12) on 2026-09-22 with the 1.5.0
build; prices are from the Netlify and Cloudflare pricing pages read the same day.

## 1. What ships

- A new `[Push]` config section. With `PushUrl` empty (the default) nothing changes: 1.6.0 on a
  home server behaves exactly like 1.5.0.
- **The push**: on its own schedule the mod POSTs its three cached envelopes — the same strings
  `/api/state`, `/api/activity` and `/api/census` serve — to `PushUrl`. Outbound only, the same
  path the Discord webhook already uses, so it works from any host.
- **The receiver**: a Netlify site (`hosting/netlify/` in this repo; Functions + Blobs) that
  stores the latest envelopes per server and serves them at the same three routes plus
  `/api/health`, behind the same read-token rule the mod applies, and serves the page itself so
  page and API share one origin.
- **The page** learns a hosted mode: it finds its API under its own path, sends `If-None-Match`
  and treats `304` as "unchanged", and shows how long ago the game server last reported.
- Version **1.6.0**. `/api/state`, the BarrkBOT files and `BARRKBOT_CONTRACT.md` are untouched.
  `/api/gamedata` (386 KB on Storm10) is **not** pushed: BarrkBOT reads it over FTP today and
  nothing on the dashboard uses it.

## 2. What the server pushes

One request per push:

```
POST <PushUrl>/push
Authorization: Bearer <PushToken>
Content-Type: application/json

{"v":1,"server_id":"storm10","mod_version":"1.6.0","pushed_at":"2026-09-22T17:02:11Z",
 "read_token":"<the server's HttpApiToken>",
 "state":{...}|null, "activity":{...}|null, "census":{...}|null}
```

- Each envelope is the exact cached string (`_stateCache`, `_activityCache`, `_censusCache`),
  spliced in as raw JSON — no new serialization, no second copy of the data model.
- An envelope is `null` when it has not changed since the last **accepted** push. "Changed" is
  the page's own rule: compare with `generated_at` and `duration_ms` blanked, so a census run
  that counted the same world is not a change, and a quiet server pushes nothing but heartbeats.
- A **heartbeat** push (every `PushHeartbeatMinutes`) carries all three envelopes regardless, so
  a receiver that lost or never had them recovers, and so "last reported" keeps meaning "the
  server is alive".
- `read_token` is the server's own `HttpApiToken`. The receiver enforces it on the hosted routes
  exactly as the mod does locally. A hosted dashboard sits on a public URL, so the receiver
  **refuses** a bundle whose read token is empty (`422`) — stricter than the mod's local default,
  on purpose. The value travels once per push inside the HTTPS body, never in a URL.
- `server_id` (config `PushServerId`, default: the world name run through the season folder
  sanitizer, lowercased, `[a-z0-9-]{1,32}`) is informational: the receiver keys storage by the
  id its registry maps the write token to, and answers `409` when the two disagree.

Measured sizes: `state` 12,312 bytes with 2 known players (about 6 KB per known player, so a
server 30 players have ever joined is around 180 KB); `activity` 5,796 bytes (the 200-event cap
is roughly 50 KB); `census` 1,765 bytes (the 500-row caps make about 60 KB the ceiling). Worst
case a push is about 300 KB; a typical one is 20–200 KB.

## 3. Schedule, backoff, cost on the game server

- `PushClient.MaybeSend()` runs from the poll tick (`PollAllPlayers`, every
  `StatsPushIntervalSeconds`) like the census does, and no-ops until `PushIntervalSeconds` have
  passed since the last attempt. Default 60, clamped 15..3600. Heartbeat default 10 minutes,
  clamped 1..1440.
- Nothing changed and no heartbeat due → no request at all.
- One request in flight at a time, on a ThreadPool worker with a 10 s timeout, the Discord
  webhook's pattern (`HttpWebRequest`, no `HttpClient`). The main thread assembles the bundle
  from strings it already holds and never waits on the network.
- Failure → exponential backoff: 60 s, 2, 4, 8, then 15 minutes flat. One warning on the first
  failure, one info line on recovery, never a line per attempt. `401` → warn once ("PushToken
  rejected") and retry every 15 minutes. Tokens never appear in a log line.
- `PushUrl` must be `https://` (plain `http://` is accepted for `localhost` and `127.0.0.1`
  only, for the test harness); anything else disables the push with one warning at boot.
- Bandwidth from the game server: at most about 300 KB a minute while players are online, so
  under 450 MB a day worst case and typically far less; nothing while idle beyond heartbeats.
  No gzip in 1.6.0 (a follow-up; `HttpWebRequest` can send `Content-Encoding: gzip`, but the
  receiver platform's handling of compressed request bodies has to be proven first).

## 4. The receiver

**Where.** A separate Netlify site in the owner's team, `dash.ravenirongames.com`, built from
`hosting/netlify/` in this repo: `netlify.toml`, `netlify/functions/push.mts`,
`netlify/functions/api.mts`, the page as a static file, and a README with the deploy steps
(`netlify init`, the env var, the custom domain).

**Registry.** An env var `TRC_SERVERS` holding JSON `{"<server_id>": "<sha256 of the write
token>"}`, set in the Netlify UI. No self-serve registration in 1.6.0 (§9); adding a server is
editing that variable. Write tokens are never stored in the clear.

**Storage.** Netlify Blobs, store `trc`, keys `<server_id>/state`, `<server_id>/activity`,
`<server_id>/census` and `<server_id>/meta` (`pushed_at`, `mod_version`, sha256 of the read
token, sizes). Latest copy only; envelopes are stored and served as opaque bytes, never parsed
or re-serialized on the receiver.

**Routes.**

| Route | Behaviour |
|---|---|
| `POST /push` | `401` missing or wrong bearer; `409` body `server_id` ≠ the token's id; `413` body over 2 MB; `422` empty `read_token`; `429` more often than every 15 s per server; else `200 {"ok":true,"stored":["state","census"]}` naming the envelopes that were non-null |
| `GET /s/<id>/api/state`, `/activity`, `/census` | the stored envelope, `Content-Type: application/json`, `ETag` = sha256 of the body, `Cache-Control: no-cache`; `If-None-Match` matching → `304`; read token as the mod takes it (`?token=` or `X-Api-Token`) checked against the stored hash, else `401`; `404` unknown id; `503 {"status":"no_data"}` before the first push carrying that envelope |
| `GET /s/<id>/api/health` | open, no token, like the mod's: `{"status":"ok","version":"<mod_version>","hosted":true,"pushed_at":"…","age_seconds":N,"stale_after_seconds":M}` where `M` = 3 × the heartbeat the server declared in its last push |
| `GET /s/<id>/` | the page (the release's `theravenscall.html`, copied into the site at cut time) with `<meta name="theravenscall-hosted" content="/s/<id>">` injected |
| anything else | `404` |

Page and API share the origin, so no CORS headers and no Base URL to type. Ids are validated
against `[a-z0-9-]{1,32}` before they touch a storage key.

**Cost.** Netlify's pricing page (2026-09-22): Free plan 300 credits a month; web requests 2
credits per 10,000; compute 10 credits per GB-hour; bandwidth 20 credits per GB; Blobs and
Functions included; Personal $9 a month for 1,000 credits, Pro $20 for 3,000 and up. For one
server: pushes at most 1,440 a day while players are online; viewing at the page's own cadence
(state and activity every 10 s, census every 60 s) is 13 requests a minute, so an hour of
viewing a day is about 780. Call it 2,200 requests a day, 67,000 a month: **14 credits**.
Compute at about 100 ms per invocation: 1.9 GB-hours, **19 credits**. Bandwidth is egress only:
with `304`s the page downloads a body only when something changed, a few credits; worst case
with no `304`s (a 180 KB state every 10 s for an hour a day) is about 2 GB a month, **40
credits**. So **35–75 credits a month per server**, which the Free plan's 300 covers for two to
four servers with headroom; past that the Personal plan's 1,000 credits cover twenty or more.
The extra cost of hosting the owner's own servers is therefore **$0** on the current plan, and
$9 a month if it ever outgrows it. Verify at implementation: that inbound request bodies are
not metered as bandwidth, and Blobs' own operation limits on the Free plan (the pricing page
lists none).

**Why not the Cloudflare Worker the site's old address points at.** Workers Free allows 100,000
requests a day, plenty, but Workers KV allows 1,000 writes a day, which one server's pushes
alone exceed; D1 or a Durable Object would be needed instead. Netlify Blobs carries no such cap
on the credit table, and ravenirongames.com already lives on Netlify.

## 5. Dashboard changes

- **Hosted mode.** When `<meta name="theravenscall-hosted">` is present, `apiUrl()` uses its
  content as the base. The Settings Base URL override still wins when set, so the page can
  also be opened from anywhere and pointed at a receiver by hand.
- **Conditional fetches.** `apiFetch` remembers the last `ETag` per route, sends
  `If-None-Match`, and treats `304` as "unchanged": no parse, no render, the freshness clock
  still ticks. Against the mod's own listener nothing changes (it sends no `ETag`).
- **Freshness.** In hosted mode the header reads "server reported 2m ago" from `/api/health`'s
  `age_seconds` (fetched with the state poll), and once `age_seconds` passes
  `stale_after_seconds` it reads "server silent since <time>" instead. Without this the receiver
  would keep answering "updated just now" while the game server is down.
- Nothing else. The same file serves `localhost:2112` and the receiver.

## 6. Security

- HTTPS only for the push (§3). Write token per server, 32+ characters, sent only in the
  `Authorization` header, stored only as a sha256, never logged on either side.
- A read token is required to be hosted (§2, `422`). The page keeps it in `localStorage` as it
  does today; on the hosted page the token is sent over HTTPS.
- Receiver caps: 2 MB body, 15 s per server, ids `[a-z0-9-]{1,32}`; no server-supplied string
  becomes a storage key; envelopes are opaque bytes end to end.
- What a read-token holder can see is exactly what `/api/state` shows a token holder today
  (every known player's stats, skills, titles, death coordinates); the README already says so
  and the hosted section repeats it.
- Denial of wallet: `429` per server and the 2 MB cap bound what a leaked write token can cost;
  the Netlify UI's spend limit is the backstop and the README says to set it.

## 7. Docs and versioning

- Version 1.6.0 in `Saga.cs`, `manifest.json`, README badge; the release name is the owner's.
- CHANGELOG 1.6.0; README: a "Hosted servers (Nitrado, G-Portal)" section with the setup in
  five steps (set `HttpApiToken`, `PushUrl`, `PushToken`; register the id on the receiver; open
  `https://dash.ravenirongames.com/s/<id>/`); `docs/API.md`: the push contract and the hosted
  routes; `docs/DASHBOARD.md`: hosted mode; `hosting/netlify/README.md`: deploy, env var,
  domain, and the page-copy step that belongs to every cut from 1.6.0 on; `HANDOFF.md`.
- `BARRKBOT_CONTRACT.md` untouched.

## 8. Test plan

1. Build: 0 warnings, 0 errors; the inline script passes `node --check`.
2. **Local receiver** (`netlify dev` on `http://localhost:8888`), Storm10 with
   `PushUrl = http://localhost:8888`, `PushToken` and `HttpApiToken` set: the first push lands
   within `PushIntervalSeconds` of boot and carries all three envelopes; one log line; the
   receiver's stored bytes equal `curl localhost:2112/api/state` etc. exactly.
3. **Change gate**: nobody online, no events → only heartbeats (3 requests in 30 minutes at the
   default); a player joins → `state` in the next bundle, `activity` with the join row; a
   census run over an unchanged world → `census` null.
4. **Failures**: stop the receiver → one warning, attempts at 60/120/240 s in the receiver's
   log once it is back, one info line on recovery, and the next bundle carries all three; a
   wrong `PushToken` → `401`, one warning, a retry 15 minutes later; `PushUrl = http://example.com`
   → disabled at boot with one warning; `HttpApiToken` empty → `422` and one warning naming
   the fix.
5. **Hosted page**: `http://localhost:8888/s/storm10/` shows the same roster, feed, season and
   World panel as `localhost:2112` from the same payloads; the token notice and recovery; `304`
   responses in the network log after the first fetch of each route; "server reported Ns ago";
   stop Storm10 → "server silent since …" once `stale_after_seconds` passes.
6. **Real site**: deploy to `dash.ravenirongames.com`, repeat 2 and 5 against it, and after
   24 hours read the credit usage in the Netlify UI into the changelog.
7. **Nitrado** (the owner's call; needs a rented test server): one boot — the mod's first on
   Linux — checking the BepInEx log, the FTP files and a push. This is the gate for the README
   claiming Nitrado support; until it runs, the section says "designed for, not yet run on".
8. **No regression**: with `PushUrl` empty the 1.5.0 test plan still passes on `localhost:2112`.

## 9. Non-goals

- Self-serve registration and hosting other admins' servers — the owner's word (2026-09-22) is
  "not yet". The registry is an env var of the owner's own servers; ids and tokens are already
  per server, so opening it later is a receiver change only.
- History, graphs, or keeping anything but the latest envelope.
- Pushing `/api/gamedata` or `/api/pins`.
- gzip on the push, WebSockets or live updates, more than one receiver.
- Any change to what the mod serves on `localhost:2112`.

## 10. Open for the owner

- The release name.
- The hostname (`dash.ravenirongames.com` is the placeholder).
- Whether to rent a Nitrado test server for step 7 before or after the cut.
