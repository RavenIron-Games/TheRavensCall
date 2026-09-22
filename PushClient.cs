using System;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TheRavensCall
{
    // ═══════════════════════════════════════════════════════════════════════
    // PUSH CLIENT — 1.6.0. Pushes the same three envelopes /api/state,
    // /api/activity and /api/census serve to a hosted receiver over HTTPS,
    // so a rented server (Nitrado, G-Portal — no reachable port, no shell)
    // can still feed a dashboard. See docs/SCOPE-1.6.0.md, the implementation
    // contract this file follows section by section (cited inline as §N).
    //
    // Fire-and-forget on a ThreadPool worker, the DiscordWebhook pattern
    // (HttpWebRequest, no HttpClient — §3), but NOT copied as written: the
    // webhook logs ex.Message only and can't tell response codes apart,
    // where this handler has to (§3, §6's four named refusals).
    //
    // Threading: MaybeSend() runs on the main thread from PollAllPlayers,
    // every poll tick. It never blocks on the network — it hands the
    // assembled body to a worker and returns. The worker writes an immutable
    // PushOutcome under _outcomeLock; the *next* MaybeSend() call drains and
    // acts on it (commits the change gate, steps backoff, logs). Nothing but
    // that drain step touches _lastAccepted*Blanked, _consecutiveFailures or
    // _nextAllowedAttemptUtc, so there is never a writer race on them.
    // ═══════════════════════════════════════════════════════════════════════
    internal static class PushClient
    {
        // Set once in Init() (called from Plugin.Awake, after the [Push]
        // binds) and never written again. MaybeSend() no-ops instantly when
        // this is false — the whole point of the widened Companion.cs
        // guards is that PushClient.Enabled can stand in for
        // EnableHttpServer, so this has to be safe to read from anywhere at
        // any time before Init() has run (it starts false, C#'s default).
        internal static volatile bool Enabled;

        private static readonly Regex ServerIdPattern = new Regex("^[a-z0-9-]{1,32}$", RegexOptions.Compiled);

        // Volatile fields blanked for the "changed since last accepted push"
        // compare (§2). Deliberately NOT Companion's stripGeneratedAt (see
        // BlankGeneratedAt/BlankCensusVolatile below) — that only blanks
        // generated_at and the scope calls out why that is too loose here.
        private static readonly Regex GeneratedAtField = new Regex("\"generated_at\":\"[^\"]*\"", RegexOptions.Compiled);
        private static readonly Regex DurationMsField = new Regex("\"duration_ms\":-?[0-9]+", RegexOptions.Compiled);
        private static readonly Regex ScannedObjectsField = new Regex("\"scanned_objects\":-?[0-9]+", RegexOptions.Compiled);

        private const long MaxBodyBytes = 2L * 1024 * 1024; // §3, receiver's own 413 limit

        // ── Config resolved once at Init() ──────────────────────────────────
        private static string _pushUrl;      // already validated https/loopback-http, no trailing-slash handling needed here — done at send time
        private static string _pushToken;
        private static string _serverId;
        private static string _readTokenSha256; // hex sha256 of HttpApiToken, computed once (§2) — the plaintext token never leaves this method
        private static int _effectiveIntervalSeconds;
        private static int _effectiveHeartbeatSeconds;

        // ── Schedule state (main thread only) ───────────────────────────────
        private static DateTime _lastAttemptUtc = DateTime.MinValue;
        private static DateTime _lastSuccessUtc = DateTime.MinValue;
        private static DateTime _nextAllowedAttemptUtc = DateTime.MinValue; // backoff / 15-min config-refusal retry / 429 Retry-After
        private static volatile bool _requestInFlight;

        // ── Change gate (main thread only — only DrainOutcome writes these,
        // and only for envelopes an outcome says were actually sent) ───────
        private static string _lastAcceptedStateBlanked;
        private static string _lastAcceptedActivityBlanked;
        private static string _lastAcceptedCensusBlanked;

        // ── Failure bookkeeping for the hourly re-log (§3) ──────────────────
        private static int _consecutiveFailures;
        private static string _lastErrorText;
        private static DateTime _lastFailureLogUtc = DateTime.MinValue;
        private static DateTime _lastOversizeWarnUtc = DateTime.MinValue; // rate-caps the over-2MB warning at most once an hour (§3)

        // §2: a failure of any kind (ConfigRefusal/OtherFailure) leaves this
        // set so the *next* attempt resends all three envelopes regardless
        // of the change gate — a receiver that lost its blobs during the
        // outage recovers on the first push after the fix, not the next
        // heartbeat (up to 24h). Cleared only once a bundle is actually
        // queued for send (right before QueueUserWorkItem below), so it
        // survives every path that returns early — including the new
        // oversize-drop-to-nothing return — and is re-set by the next
        // failure.
        private static bool _forceAllNextAttempt;

        // ── Outcome handoff between the worker thread and the main thread ──
        private static readonly object _outcomeLock = new object();
        private static PushOutcome _pendingOutcome;

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        // Called once from Plugin.Awake, right after the [Push] binds.
        internal static void Init()
        {
            string urlRaw = Plugin.PushUrl.Value?.Trim();
            if (string.IsNullOrEmpty(urlRaw))
            {
                // §1: PushUrl empty is the default and stays silent — 1.6.0
                // on a home server behaves exactly like 1.5.0, no log line.
                Enabled = false;
                return;
            }

            bool ok = true;

            Uri uri;
            bool validUri = Uri.TryCreate(urlRaw, UriKind.Absolute, out uri);
            bool schemeOk = validUri &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                 (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback));
            if (!schemeOk)
            {
                // Never a string-prefix test (§3) — http://localhost.example.com/
                // parses with scheme "http" and IsLoopback false, correctly refused.
                Plugin.Log.LogWarning("[TheRavensCall] Push disabled: [Push] PushUrl '" + urlRaw +
                    "' must be an https URL (http is accepted only to a loopback host, for local testing against netlify dev) — fix the URL and restart.");
                ok = false;
            }

            string apiToken = Plugin.HttpApiToken != null ? (Plugin.HttpApiToken.Value ?? "") : "";
            if (apiToken.Length < 24)
            {
                // §2: stricter than the mod's own local default, on purpose
                // — a hosted dashboard sits on a public URL.
                Plugin.Log.LogWarning("[TheRavensCall] Push disabled: a hosted dashboard needs [Companion] HttpApiToken to be at least 24 characters — set a longer random token and restart.");
                ok = false;
            }

            string serverId = Plugin.PushServerId.Value?.Trim() ?? "";
            if (!ServerIdPattern.IsMatch(serverId))
            {
                Plugin.Log.LogWarning("[TheRavensCall] Push disabled: [Push] PushServerId must be 1-32 characters of a-z, 0-9 and '-', matching the id registered in the receiver's TRC_SERVERS — set it and restart.");
                ok = false;
            }

            string pushToken = Plugin.PushToken.Value ?? "";
            if (string.IsNullOrEmpty(pushToken))
            {
                Plugin.Log.LogWarning("[TheRavensCall] Push disabled: [Push] PushToken is empty — set the write token registered for this server in the receiver and restart.");
                ok = false;
            }
            else if (pushToken.Length < 32)
            {
                // §6: a warning, not a disable — the push still runs.
                Plugin.Log.LogWarning("[TheRavensCall] [Push] PushToken is under 32 characters — generate a longer random token (e.g. `openssl rand -hex 24`) before pointing this server at a public receiver.");
            }

            if (!ok)
            {
                Enabled = false;
                return;
            }

            _pushUrl = urlRaw;
            _pushToken = pushToken;
            _serverId = serverId;

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(apiToken));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                _readTokenSha256 = sb.ToString();
            }

            // §3: PushClient only runs from the poll tick, so a push
            // interval that isn't a multiple of StatsPushIntervalSeconds
            // silently rounds up to the next tick that is actually due.
            int rawInterval = Clamp(Plugin.PushIntervalSeconds.Value, 15, 3600);
            float tickSeconds = (Plugin.StatsPushIntervalSeconds != null && Plugin.StatsPushIntervalSeconds.Value > 0f)
                ? Plugin.StatsPushIntervalSeconds.Value : 10f;
            _effectiveIntervalSeconds = (int)(Math.Ceiling(rawInterval / (double)tickSeconds) * tickSeconds);

            int rawHeartbeatSeconds = Clamp(Plugin.PushHeartbeatMinutes.Value, 1, 1440) * 60;
            _effectiveHeartbeatSeconds = Math.Max(rawHeartbeatSeconds, _effectiveIntervalSeconds);
            if (_effectiveHeartbeatSeconds > rawHeartbeatSeconds)
            {
                Plugin.Log.LogWarning("[TheRavensCall] [Push] PushHeartbeatMinutes raised so the heartbeat (" +
                    _effectiveHeartbeatSeconds.ToString(CultureInfo.InvariantCulture) +
                    "s) is never shorter than the effective push interval (" +
                    _effectiveIntervalSeconds.ToString(CultureInfo.InvariantCulture) + "s).");
            }

            Enabled = true;
            Plugin.Log.LogInfo("[TheRavensCall] Push enabled: server_id='" + _serverId + "', url=" + _pushUrl +
                ", interval=" + _effectiveIntervalSeconds.ToString(CultureInfo.InvariantCulture) +
                "s, heartbeat=" + _effectiveHeartbeatSeconds.ToString(CultureInfo.InvariantCulture) + "s.");
        }

        private static string BlankGeneratedAt(string json) => GeneratedAtField.Replace(json, "\"generated_at\":\"\"", 1);

        private static string BlankCensusVolatile(string json)
        {
            json = BlankGeneratedAt(json);
            json = DurationMsField.Replace(json, "\"duration_ms\":0", 1);
            json = ScannedObjectsField.Replace(json, "\"scanned_objects\":0", 1);
            return json;
        }

        // Called last inside PollAllPlayers's widened guard, after the three
        // cache assignments (§1), so every attempt sees this tick's strings.
        internal static void MaybeSend()
        {
            if (!Enabled) return;

            // _requestInFlight is checked *before* DrainOutcome(): it is
            // volatile and the worker releases _outcomeLock (publishing the
            // pending outcome) before it clears this flag, so reading it
            // false here guarantees any outcome from the just-finished
            // worker is already visible to the drain that follows. Draining
            // first (the old order) could miss an outcome published between
            // the drain and this read, leaving it undrained while a second
            // attempt starts — losing a gate commit or a backoff step when
            // that second outcome later overwrites the first.
            if (_requestInFlight) return;

            // Last tick's outcome (if any) has to update the gate/backoff
            // state before this tick decides anything.
            DrainOutcome();

            DateTime now = DateTime.UtcNow;
            if (_lastAttemptUtc != DateTime.MinValue && (now - _lastAttemptUtc).TotalSeconds < _effectiveIntervalSeconds) return;
            if (now < _nextAllowedAttemptUtc) return; // backoff, a 429's Retry-After, or a config-refusal's 15-minute retry

            // This tick is the one allowed to attempt a push — mark it now,
            // whether or not there turns out to be anything to send, so the
            // next opportunity is exactly one effective interval away and a
            // long quiet stretch can't make every later tick re-evaluate.
            _lastAttemptUtc = now;

            string rawState = Companion._stateCache ?? Companion.EmptyStateJson;
            string rawActivity = Companion._activityCache ?? Companion.EmptyActivityJson;
            string rawCensus = Companion._censusCache ?? WorldCensus.EmptyEnvelope();

            string blankedState = BlankGeneratedAt(rawState);
            string blankedActivity = BlankGeneratedAt(rawActivity);
            string blankedCensus = BlankCensusVolatile(rawCensus);

            // §3: the interval gate is never bypassed — a heartbeat only
            // lands on a tick already allowed to push, which is exactly
            // where we are here.
            bool heartbeatDue = _lastSuccessUtc == DateTime.MinValue ||
                (now - _lastSuccessUtc).TotalSeconds >= _effectiveHeartbeatSeconds;

            bool sendState = heartbeatDue || _forceAllNextAttempt || !string.Equals(blankedState, _lastAcceptedStateBlanked, StringComparison.Ordinal);
            bool sendActivity = heartbeatDue || _forceAllNextAttempt || !string.Equals(blankedActivity, _lastAcceptedActivityBlanked, StringComparison.Ordinal);
            bool sendCensus = heartbeatDue || _forceAllNextAttempt || !string.Equals(blankedCensus, _lastAcceptedCensusBlanked, StringComparison.Ordinal);

            if (!sendState && !sendActivity && !sendCensus) return; // nothing changed, no heartbeat due — no request at all (§3)

            string stateField = sendState ? "\"" + Companion.Esc(rawState) + "\"" : "null";
            string activityField = sendActivity ? "\"" + Companion.Esc(rawActivity) + "\"" : "null";
            string censusField = sendCensus ? "\"" + Companion.Esc(rawCensus) + "\"" : "null";

            string pushedAt = now.ToString("o", CultureInfo.InvariantCulture);
            string payload = BuildPayload(pushedAt, stateField, activityField, censusField);

            long bodyBytes = Encoding.UTF8.GetByteCount(payload);
            if (bodyBytes > MaxBodyBytes)
            {
                // §3: drop the largest of the envelopes actually being sent
                // and log once — a server a few hundred players have
                // visited reaches 2 MB on state alone and would otherwise
                // be rejected forever.
                long stateBytes = sendState ? Encoding.UTF8.GetByteCount(rawState) : -1;
                long activityBytes = sendActivity ? Encoding.UTF8.GetByteCount(rawActivity) : -1;
                long censusBytes = sendCensus ? Encoding.UTF8.GetByteCount(rawCensus) : -1;

                if (stateBytes >= activityBytes && stateBytes >= censusBytes && sendState)
                {
                    sendState = false;
                    stateField = "null";
                }
                else if (activityBytes >= stateBytes && activityBytes >= censusBytes && sendActivity)
                {
                    sendActivity = false;
                    activityField = "null";
                }
                else if (sendCensus)
                {
                    sendCensus = false;
                    censusField = "null";
                }

                if (!sendState && !sendActivity && !sendCensus)
                {
                    // The only envelope due was itself over 2MB: the drop
                    // chain above nulled it and there is nothing left to
                    // send. Sending the bundle anyway would be state/
                    // activity/census all null, which the receiver answers
                    // 200 {"stored":[]} — DrainOutcome takes that as Success
                    // and advances _lastSuccessUtc, so the hosted page keeps
                    // reporting a fresh "server reported Ns ago" over data
                    // that is never stored. Drop the whole attempt instead.
                    // _forceAllNextAttempt is left untouched — it is cleared
                    // only once a bundle is actually queued for send.
                    if (_lastOversizeWarnUtc == DateTime.MinValue || (now - _lastOversizeWarnUtc).TotalHours >= 1.0)
                    {
                        Plugin.Log.LogWarning("[TheRavensCall] Push bundle for '" + _serverId + "' was " +
                            bodyBytes.ToString(CultureInfo.InvariantCulture) + " bytes, over the receiver's 2 MB limit — dropped entirely (nothing sent) this attempt.");
                        _lastOversizeWarnUtc = now;
                    }
                    return;
                }

                payload = BuildPayload(pushedAt, stateField, activityField, censusField);
                if (_lastOversizeWarnUtc == DateTime.MinValue || (now - _lastOversizeWarnUtc).TotalHours >= 1.0)
                {
                    Plugin.Log.LogWarning("[TheRavensCall] Push bundle for '" + _serverId + "' was " +
                        bodyBytes.ToString(CultureInfo.InvariantCulture) + " bytes, over the receiver's 2 MB limit — dropped the largest envelope this attempt.");
                    _lastOversizeWarnUtc = now;
                }
            }

            var sentFlags = new bool[3] { sendState, sendActivity, sendCensus };
            var blankedForms = new string[3] { blankedState, blankedActivity, blankedCensus };

            _requestInFlight = true;
            string url = _pushUrl;
            string token = _pushToken;
            string body = payload;
            DateTime attemptUtc = now;

            // §2/mod-contract-1: this attempt is carrying the forced full
            // bundle (if it was set) — clear it here, only once the bundle
            // is actually queued for send, so it is not lost by an earlier
            // return (e.g. nothing to send, or dropped entirely for size).
            _forceAllNextAttempt = false;

            ThreadPool.QueueUserWorkItem(_ => SendWorker(url, token, body, attemptUtc, sentFlags, blankedForms));
        }

        private static string BuildPayload(string pushedAt, string stateField, string activityField, string censusField)
        {
            return "{\"v\":1," +
                "\"server_id\":\"" + Companion.Esc(_serverId) + "\"," +
                "\"mod_version\":\"" + Plugin.PluginVersion + "\"," +
                "\"pushed_at\":\"" + pushedAt + "\"," +
                "\"push_interval_seconds\":" + _effectiveIntervalSeconds.ToString(CultureInfo.InvariantCulture) + "," +
                "\"heartbeat_seconds\":" + _effectiveHeartbeatSeconds.ToString(CultureInfo.InvariantCulture) + "," +
                "\"read_token_sha256\":\"" + _readTokenSha256 + "\"," +
                "\"state\":" + stateField + "," +
                "\"activity\":" + activityField + "," +
                "\"census\":" + censusField + "}";
        }

        // Runs on a ThreadPool worker (§3, the Discord webhook's pattern).
        // Never logs the body, a header value, or a hash — only a status
        // code / transport-failure cause, per the hard rule in the scope.
        private static void SendWorker(string pushUrl, string pushToken, string payload, DateTime attemptUtc, bool[] sentFlags, string[] blankedForms)
        {
            var outcome = new PushOutcome { AttemptUtc = attemptUtc, EnvelopeSent = sentFlags, BlankedForms = blankedForms };
            try
            {
                // Join without a double slash when PushUrl already ends in one (§3).
                string url = (pushUrl.EndsWith("/", StringComparison.Ordinal) ? pushUrl.Substring(0, pushUrl.Length - 1) : pushUrl) + "/push";

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers[HttpRequestHeader.Authorization] = "Bearer " + pushToken;
                req.UserAgent = "TheRavensCall/1.6.0";
                req.Timeout = 20000;
                req.ReadWriteTimeout = 20000;

                byte[] data = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = data.Length;
                using (var stream = req.GetRequestStream())
                    stream.Write(data, 0, data.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    outcome.ResultKind = PushOutcome.Kind.Success;
                }
            }
            catch (WebException wex)
            {
                // Scoped and disposed (a using over a null value is legal):
                // an undisposed HttpWebResponse here leaks a pooled
                // connection on every non-2xx, and with
                // DefaultConnectionLimit = 2 the third refusal in a row
                // times out and is reported as a transport failure instead
                // of the named refusal it actually was.
                using (var httpResp = wex.Response as HttpWebResponse)
                {
                    if (httpResp == null)
                    {
                        // §3: DNS, connect timeout, TrustFailure/SecureChannelFailure
                        // (no response at all) — a transport failure, exponential backoff.
                        outcome.ResultKind = PushOutcome.Kind.OtherFailure;
                        if (wex.Status == WebExceptionStatus.TrustFailure || wex.Status == WebExceptionStatus.SecureChannelFailure)
                        {
                            // Named cause, no SecurityProtocol change and no
                            // ServerCertificateValidationCallback — that property
                            // is process-global and would blind the Discord
                            // webhook's own TLS validation too (§3).
                            outcome.ErrorText = "TLS certificate validation failed (" + wex.Status +
                                ") — the receiver's certificate is not trusted by this machine's CA bundle";
                        }
                        else
                        {
                            outcome.ErrorText = wex.Status + ": " + wex.Message;
                        }
                    }
                    else
                    {
                        int status = (int)httpResp.StatusCode;
                        switch (status)
                        {
                            case 429:
                                outcome.ResultKind = PushOutcome.Kind.RateLimited;
                                string retryAfter = httpResp.Headers["Retry-After"];
                                int retrySeconds;
                                outcome.RetryAfterSeconds = int.TryParse(retryAfter, NumberStyles.Integer, CultureInfo.InvariantCulture, out retrySeconds)
                                    ? (int?)retrySeconds : null;
                                break;
                            case 401:
                                outcome.ResultKind = PushOutcome.Kind.ConfigRefusal;
                                outcome.ConfigRefusalMessage = "PushToken rejected — check [Push] PushToken and PushServerId against the receiver's registry";
                                break;
                            case 409:
                                outcome.ResultKind = PushOutcome.Kind.ConfigRefusal;
                                outcome.ConfigRefusalMessage = "the receiver has a different HttpApiToken hash registered for '" + _serverId + "' — re-register read_token_sha256 and redeploy";
                                break;
                            case 413:
                                outcome.ResultKind = PushOutcome.Kind.ConfigRefusal;
                                outcome.ConfigRefusalMessage = "bundle over the receiver's 2 MB limit";
                                break;
                            case 422:
                                outcome.ResultKind = PushOutcome.Kind.ConfigRefusal;
                                outcome.ConfigRefusalMessage = "the receiver refused the bundle — set [Companion] HttpApiToken (24+ characters)";
                                break;
                            default:
                                outcome.ResultKind = PushOutcome.Kind.OtherFailure;
                                outcome.ErrorText = "HTTP " + status.ToString(CultureInfo.InvariantCulture);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                outcome.ResultKind = PushOutcome.Kind.OtherFailure;
                outcome.ErrorText = ex.Message;
            }

            lock (_outcomeLock) { _pendingOutcome = outcome; }
            // The HTTP call itself is finished — a new one can start on the
            // next MaybeSend() even before that call drains this outcome.
            _requestInFlight = false;
        }

        private static int BackoffSecondsFor(int consecutiveFailures)
        {
            switch (consecutiveFailures)
            {
                case 1: return 60;
                case 2: return 120;
                case 3: return 240;
                case 4: return 480;
                default: return 900;
            }
        }

        // Runs on the main thread, from the top of the next MaybeSend().
        private static void DrainOutcome()
        {
            PushOutcome outcome;
            lock (_outcomeLock) { outcome = _pendingOutcome; _pendingOutcome = null; }
            if (outcome == null) return;

            switch (outcome.ResultKind)
            {
                case PushOutcome.Kind.Success:
                    // Commit the gate only for envelopes this attempt
                    // actually sent — one dropped for size, or held back as
                    // unchanged, leaves the previous committed value alone.
                    if (outcome.EnvelopeSent[0]) _lastAcceptedStateBlanked = outcome.BlankedForms[0];
                    if (outcome.EnvelopeSent[1]) _lastAcceptedActivityBlanked = outcome.BlankedForms[1];
                    if (outcome.EnvelopeSent[2]) _lastAcceptedCensusBlanked = outcome.BlankedForms[2];

                    int failuresBeforeThis = _consecutiveFailures;
                    _consecutiveFailures = 0;
                    _lastErrorText = null;
                    _nextAllowedAttemptUtc = DateTime.MinValue;
                    _lastSuccessUtc = outcome.AttemptUtc;
                    if (failuresBeforeThis > 0)
                        Plugin.Log.LogInfo("[TheRavensCall] Push to the receiver recovered after " +
                            failuresBeforeThis.ToString(CultureInfo.InvariantCulture) + " failed attempt(s).");
                    break;

                case PushOutcome.Kind.RateLimited:
                    // §3/§6: not a failure — no warning, no backoff step,
                    // the change gate untouched. Honour Retry-After if present.
                    if (outcome.RetryAfterSeconds.HasValue && outcome.RetryAfterSeconds.Value > 0)
                        _nextAllowedAttemptUtc = outcome.AttemptUtc.AddSeconds(outcome.RetryAfterSeconds.Value);
                    break;

                case PushOutcome.Kind.ConfigRefusal:
                    // §3: the four named refusals retry every 15 minutes, gate untouched.
                    _nextAllowedAttemptUtc = outcome.AttemptUtc.AddMinutes(15);
                    // §2: a failure never advances the gate on its own, but
                    // it must not leave one behind either — force the next
                    // attempt to carry all three envelopes so a receiver
                    // that lost its blobs during the outage recovers
                    // immediately once the config is fixed.
                    _forceAllNextAttempt = true;
                    Plugin.Log.LogWarning("[TheRavensCall] Push rejected: " + outcome.ConfigRefusalMessage);
                    break;

                case PushOutcome.Kind.OtherFailure:
                    _consecutiveFailures++;
                    _forceAllNextAttempt = true; // §2: see ConfigRefusal above
                    _lastErrorText = outcome.ErrorText;
                    int backoffSeconds = BackoffSecondsFor(_consecutiveFailures);
                    _nextAllowedAttemptUtc = outcome.AttemptUtc.AddSeconds(backoffSeconds);
                    if (_consecutiveFailures == 1)
                    {
                        Plugin.Log.LogWarning("[TheRavensCall] Push failed: " + outcome.ErrorText +
                            " — retrying in " + backoffSeconds.ToString(CultureInfo.InvariantCulture) + "s.");
                        _lastFailureLogUtc = outcome.AttemptUtc;
                    }
                    else if ((outcome.AttemptUtc - _lastFailureLogUtc).TotalHours >= 1.0)
                    {
                        // §3: re-logged at most once an hour while it keeps failing.
                        Plugin.Log.LogWarning("[TheRavensCall] Push still failing (" +
                            _consecutiveFailures.ToString(CultureInfo.InvariantCulture) + " in a row) — last success " +
                            (_lastSuccessUtc == DateTime.MinValue ? "never" : _lastSuccessUtc.ToString("o", CultureInfo.InvariantCulture)) +
                            ", last error: " + outcome.ErrorText);
                        _lastFailureLogUtc = outcome.AttemptUtc;
                    }
                    break;
            }
        }

        // Immutable snapshot the worker hands to the main thread. Never
        // carries the body, a header value or a hash — only what DrainOutcome
        // needs to update the gate/backoff state and, at most, a status code
        // turned into one of the named messages above.
        private sealed class PushOutcome
        {
            internal enum Kind { Success, RateLimited, ConfigRefusal, OtherFailure }
            internal Kind ResultKind;
            internal DateTime AttemptUtc;
            internal bool[] EnvelopeSent;   // [state, activity, census] — true where this attempt sent real content, not null
            internal string[] BlankedForms; // the blanked form of each envelope, valid only where EnvelopeSent[i]
            internal int? RetryAfterSeconds;
            internal string ConfigRefusalMessage;
            internal string ErrorText;
        }
    }
}
