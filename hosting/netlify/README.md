# TheRavensCall receiver (hosting/netlify/)

A Netlify site **of its own** (Functions + Blobs) that receives TheRavensCall
1.6.0's push, stores the latest `state`/`activity`/`census` envelope per
server, and serves them — plus the dashboard page itself — at
`/s/<server_id>/`. See `docs/SCOPE-1.6.0.md` §4 for the full design; this
file is the operational how-to.

Nothing in this folder hardcodes the owner's domain, server ids or tokens —
every such value is either an environment variable (`TRC_SERVERS`) or comes
from the request's own URL path. A second admin can deploy their own private
copy of this folder and it works the same way, unmodified.

## 1. Deploy steps, in order

1. **Read the team's current monthly credit use first.** Netlify UI ->
   the team -> Usage. This site shares the Pro plan's 3,000 credits/month
   with `ravenirongames.com`; the receiver's budget is 3,000 minus whatever
   that reading shows. Note the number somewhere (the changelog, per §7 of
   the scope, after 24 hours of real traffic).
2. **Create the site from this folder.**
   ```bash
   cd hosting/netlify
   npm install
   netlify init
   ```
   Choose "Create & configure a new site" and pick the owner's team — NOT
   the team/site that serves ravenirongames.com. `netlify init` also links
   this local folder to the new site for later `netlify deploy` calls.
3. **Set `TRC_SERVERS`** in the Netlify UI (Site configuration -> Environment
   variables -> Add a variable). See "Registering a server" below for the
   value's shape.
4. **Redeploy.** `TRC_SERVERS` reaches Functions at *deploy* time, not
   instantly — a change in the UI does nothing until the site is redeployed:
   ```bash
   netlify deploy --prod
   ```
   Every time you edit `TRC_SERVERS` (add a server, rotate or revoke a
   token), redeploy again. This is also how a leaked write token gets
   revoked: edit the variable, redeploy.
5. **Custom domain.** Placeholder in the scope: `dash.ravenirongames.com` —
   the owner's decision, set in Site configuration -> Domain management.
   Until it's set, the site's `*.netlify.app` URL works the same way.
6. **Spend cap.** Site configuration -> Billing -> set a spend cap before
   the DNS record points anywhere. `/push` and `/s/*` are public and every
   rejected request (even a `401`) is still a billed invocation — see the
   scope §4 "Cost" section for the worst-case math.
7. **Firewall Traffic Rule rate limit.** Site configuration -> Firewall ->
   Traffic rules -> add a rate-limit rule on `/push` and on `/s/*` (per
   source address). This is the guessing-rate limit the read routes have no
   counter of their own for (Functions keep no state between invocations).
   **Record here what the rule actually enforces on this plan once it's
   set** (requests per window, per address) — this line is intentionally
   left for whoever sets it up to fill in, so the limit that's live is
   documented, not the one that was planned:

   > _Rule recorded: (fill in after setup — e.g. "N requests / M seconds per
   > source IP on /push and /s/*")_

## 2. Generating tokens and hashes

Each server needs **two** independently random tokens — a write token
(`PushToken` in the mod's `[Push]` config) and a read token (`HttpApiToken`
in `[Companion]`, 24+ characters). Never reuse one value for both.

**Generate a token:**

```bash
# bash / WSL / macOS
openssl rand -hex 24
```

```powershell
# PowerShell (CSPRNG — Get-Random is not cryptographically secure)
$b=[byte[]]::new(24); [System.Security.Cryptography.RandomNumberGenerator]::Fill($b); -join ($b | ForEach-Object { $_.ToString("x2") })
```

**Hash a token** (only the hash goes in `TRC_SERVERS` — never the plaintext;
the trailing newline matters, so use `printf`, not `echo`):

```bash
# bash / WSL / macOS
printf '%s' "$TOKEN" | sha256sum | awk '{print $1}'
```

```powershell
# PowerShell
$bytes = [System.Text.Encoding]::UTF8.GetBytes($Token)
-join ([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes) |
  ForEach-Object { $_.ToString("x2") })
```

## 3. Registering a server

`TRC_SERVERS` is one JSON object, keyed by `server_id`
(`[a-z0-9-]{1,32}`), each value `{"w": "<sha256 of PushToken>", "r": "<sha256
of HttpApiToken>"}` (both 64 lowercase hex characters). Example:

```json
{"storm10":{"w":"<64 hex>","r":"<64 hex>"}}
```

Add an entry per server, set the variable in the Netlify UI, then redeploy
(step 4 above). An entry whose id doesn't match the charset, or whose `w`/`r`
isn't 64 hex characters, is silently ignored (one line in the function log,
no value logged) — the site keeps working for every other entry. A registry
where two ids share the same `w` is rejected in full at load (both ids stop
working) — that's the mod-side "two servers configured with the same write
token" mistake caught before it can happen.

## 4. Unpublishing a server

```bash
curl -X DELETE https://<site>/s/<id>/api \
  -H "Authorization: Bearer <PushToken>"
# -> {"deleted":true}
```

This removes the four stored blobs (`state`, `activity`, `census`, `meta`)
for that id. The dashboard at `/s/<id>/` then reads as "no data" again. To
stop a server from being hosted at all, also remove its entry from
`TRC_SERVERS` and redeploy — otherwise it can push again and reappear.

## 5. The page-copy step (every cut from 1.6.0 on)

The static page this site serves is a **copy** of the repo's own
`theravenscall.html`, not a symlink. Every release cut from 1.6.0 on must
re-run:

```bash
npm run copy-page
```

before deploying, so the hosted page matches whatever the page track most
recently shipped. `netlify deploy` picks up whatever is currently in
`public/theravenscall.html` — an out-of-date copy ships an out-of-date page
with no error.

## 6. Local testing with `netlify dev`

Runs the site (functions + redirects + static `public/`) at
`http://localhost:8888`. Blobs run against a sandboxed local store under
`netlify dev` — no site linking or real Blobs account needed for this.

**Set `TRC_SERVERS` for the local run** (pick your own test tokens; do not
reuse a real server's tokens for local testing):

```bash
# bash / WSL / macOS
export TRC_SERVERS='{"storm10":{"w":"<sha256 of a test write token>","r":"<sha256 of a test read token>"}}'
npm run dev
```

```powershell
# PowerShell
$env:TRC_SERVERS = '{"storm10":{"w":"<sha256 of a test write token>","r":"<sha256 of a test read token>"}}'
npm run dev
```

Or put the same value in a local `.env` (see `.env.example`) — `netlify dev`
loads it automatically; `.env` is gitignored.

### curl walkthrough (scope §8 step 6)

Replace `$WRITE` / `$READ` with your test tokens (plaintext — the server
sends the plaintext bearer/header; the receiver hashes it) and `$WHASH` /
`$RHASH` with their sha256 hex per "Generating tokens and hashes" above.

```bash
BASE=http://localhost:8888

# A valid push (state/activity/census can be any JSON string; null is legal)
curl -i -X POST "$BASE/push" \
  -H "Authorization: Bearer $WRITE" \
  -H "Content-Type: application/json" \
  -d "{\"v\":1,\"server_id\":\"storm10\",\"mod_version\":\"1.6.0\",\"pushed_at\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\",\"push_interval_seconds\":60,\"heartbeat_seconds\":600,\"read_token_sha256\":\"$RHASH\",\"state\":\"{\\\"generated_at\\\":\\\"x\\\"}\",\"activity\":null,\"census\":null}"
# -> 200 {"ok":true,"stored":["state"]}

# Wrong bearer -> 401
curl -i -X POST "$BASE/push" -H "Authorization: Bearer wrong" -H "Content-Type: application/json" -d '{}'

# Over 2 MB -> 413
head -c 3000000 /dev/zero | tr '\0' 'a' | curl -i -X POST "$BASE/push" -H "Authorization: Bearer $WRITE" -H "Content-Type: application/json" --data-binary @-

# Burst inside 10 s -> 429 with Retry-After (send the same valid push twice in a row)

# Read with the right token
curl -i "$BASE/s/storm10/api/state" -H "X-Api-Token: $READ"
# -> 200, an ETag header

# Conditional read -> 304 (on the live site a browser's ETag reads "<sha>-df": the edge compresses
# function responses and suffixes the compressed variant's ETag; the receiver accepts the suffix)
curl -i "$BASE/s/storm10/api/state" -H "X-Api-Token: $READ" -H 'If-None-Match: "<etag from above, unquoted or quoted, both are handled>"'

# token as a query param -> 400
curl -i "$BASE/s/storm10/api/state?token=$READ"

# No token / unregistered id -> 401
curl -i "$BASE/s/storm10/api/state"
curl -i "$BASE/s/nope/api/state" -H "X-Api-Token: $READ"

# Health before any push
curl -i "$BASE/s/storm10/api/health" -H "X-Api-Token: $READ"

# The page
curl -i "$BASE/s/storm10"    # -> 200, the page (no redirect: both forms serve it)
curl -i "$BASE/s/storm10/"   # -> 200, the page HTML, CSP header present

# Unpublish
curl -i -X DELETE "$BASE/s/storm10/api" -H "Authorization: Bearer $WRITE"
```

## 7. Cost (scope §4, in short)

Netlify Pro is **3,000 credits/month**, shared with `ravenirongames.com`.
Rates (read on the pricing page 2026-09-22): web requests 2 credits/10,000;
compute 10 credits/GB-hour at the default 1 GB; bandwidth (egress) 20
credits/GB; a production deploy 15 credits.

- A pushing server costs roughly **45 credits/server-month** worst case
  (43,200 pushes/month at 60 s while players are online).
- An open dashboard tab costs roughly **1.2–2.5 credits/viewer-hour**; the
  page pauses all polling while its tab is hidden (§5 of the scope), which
  is what keeps a forgotten background tab from billing around the clock.
- Two or three servers plus normal viewing is on the order of **150–250
  credits/month** — extra cost **$0** of the 3,000/month allowance unless
  viewing habits change by an order of magnitude.
- **Confirmed: inbound push bodies are not metered as bandwidth.** Netlify's
  own docs page on credit-based billing (["Credit usage for
  bandwidth"](https://docs.netlify.com/manage/accounts-and-billing/billing/billing-for-credit-based-plans/how-credits-work/),
  read 2026-09-22) says: "Bandwidth is the amount of data traffic your site
  or app sends out to the internet." — outbound only, nothing about request
  bodies. That is what makes the per-server estimate above hold. Netlify
  support was not separately asked to confirm it.
- None of the above bounds an *unauthenticated* flood — that's what the
  spend cap and the Firewall Traffic Rule (step 1.7 above) are for, not
  advice.

## One more time

Nothing in this folder hardcodes the owner's domain, server ids or tokens —
`TRC_SERVERS` is an environment variable, and every id this code ever sees
comes from the request's own path or body.
