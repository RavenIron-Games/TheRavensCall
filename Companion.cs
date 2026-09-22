using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;

namespace TheRavensCall
{
    public class Companion : MonoBehaviour
    {
        public const string NAME = Plugin.PluginName;
        public const string VERSION = Plugin.PluginVersion;

        internal static Companion Instance;
        internal static ManualLogSource Log;
        internal static bool _recipesPushed = false;
        // Logged once per server run, the first time a disk-override
        // theravenscall.html is served that predates the 1.3.0 marker meta
        // tag — it can't read the new /api/state shape, so its dashboard is
        // permanently dead until the admin deletes it (review 2026-09-15).
        private static bool _warnedStaleDiskDashboard = false;

        // /api/state is served from this string, rebuilt on the main thread by
        // PollAllPlayers every StatsPushIntervalSeconds. The HTTP worker
        // threads used to walk PlayerRegistry and its per-record dictionaries
        // while the main thread was writing them (review 2026-09-15).
        // 1.3.0: this is now exactly the BarrkBOT export string — the same
        // one PollAllPlayers already builds and writes to BarrkBOT_data1.json
        // every tick regardless of whether anyone is watching the dashboard,
        // so gating the rebuild behind a "was it requested" flag (the old
        // _stateWanted mechanism) no longer saves any work and was dropped.
        internal static volatile string _stateCache;
        // Served until PrimeStateCache has run (or if it throws): the same
        // envelope PlayerRegistry.BuildBarrkBotJson emits, with no players,
        // so /api/state has one shape for its whole lifetime. Keep in step
        // with that method.
        internal const string EmptyStateJson = "{\"generated_at\":\"\",\"world_name\":\"\",\"day\":0,\"online_count\":0,\"raid_active\":false,\"raid_type\":\"\",\"players\":{}}";

        // /api/activity is served from this string, rebuilt on the main
        // thread alongside _stateCache (see BuildActivityCache). Same
        // discipline as _stateCache: the HTTP worker only ever hands out a
        // string the main thread already finished building.
        internal static volatile string _activityCache;
        // Served until the first BuildActivityCache() call (or if it
        // throws): the exact shape §2 documents, with no season and no
        // events, so /api/activity has one shape for its whole lifetime.
        internal const string EmptyActivityJson = "{\"generated_at\":\"\",\"season\":{\"active\":false,\"name\":\"\",\"started_at\":null,\"standings_since\":null,\"last_ended\":null,\"last_ended_at\":null,\"standings\":[]},\"events\":[]}";

        // /api/census is served from this string, rebuilt on the main thread
        // by WorldCensus.MaybeRun() (called from PollAllPlayers) on its own
        // CensusIntervalMinutes schedule — never a live walk from an HTTP
        // worker thread, same discipline as _stateCache/_activityCache.
        internal static volatile string _censusCache;

        // HTTP server
        private HttpListener _listener;
        private Thread _serverThread;

        // Output folder: BepInEx/config/TheRavensCall/ — all per-player data
        // lives under here as one file per player (see PlayerRegistry.cs).
        internal static string OutputDir;

        void Awake()
        {
            Instance = this;
            Log = Plugin.Log;

            OutputDir = Path.Combine(Paths.ConfigPath, "TheRavensCall");
            Directory.CreateDirectory(OutputDir);

            // Harmony patching for the whole assembly (Saga + Companion) is done once,
            // by the unified Plugin entry point — not here.
            // Verify Player.OnDeath patch applied
            var onDeathMethod = typeof(Player).GetMethod("OnDeath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Log.LogInfo("Player.OnDeath method found: " + (onDeathMethod != null ? onDeathMethod.Name : "NOT FOUND"));
            Log.LogInfo(NAME + " v" + VERSION + " loaded.");
            Log.LogInfo("Writing data to: " + OutputDir);
            if (Plugin.EnableHttpServer.Value) StartHttpServer();
        }

        // Per-player polling (biome/gear checks, state snapshots, the BarrkBOT
        // export) is driven externally by Patch_ZNetUpdate in Saga.cs, on the
        // server's own update loop rather than this component's Update() —
        // that keeps a single timer/interval in one place. This Update() only
        // has to catch the one-shot game-data export once recipes are loaded.
        void Update()
        {
            if (!_recipesPushed && ObjectDB.instance != null && ObjectDB.instance.m_recipes.Count > 0)
            {
                _recipesPushed = true;
                StartCoroutine(WriteGameData());
            }
        }

        void OnDestroy()
        {
            StopHttpServer();
            // PlayerRegistry.SaveAll() and Harmony unpatching for the whole
            // assembly are done once, by the unified Plugin.OnDestroy().
        }

        // ── HTTP Server ──

        void StartHttpServer()
        {
            try
            {
                int port = Plugin.HttpServerPort.Value;
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:" + port + "/");
                // Every interface only on request: the API serves every known
                // player's stats, skills, titles and death coordinates with no
                // login (review 2026-09-15). See HttpBindAllInterfaces and
                // HttpApiToken in the config.
                bool bindAll = Plugin.HttpBindAllInterfaces != null && Plugin.HttpBindAllInterfaces.Value;
                if (bindAll) _listener.Prefixes.Add("http://+:" + port + "/");
                _listener.Start();
                _serverThread = new Thread(HandleRequests) { IsBackground = true, Name = "SteveHTTP" };
                _serverThread.Start();
                Log.LogInfo("HTTP server listening on http://localhost:" + port + (bindAll ? " and every interface" : " (localhost only; set HttpBindAllInterfaces=true to expose it)"));
                if (bindAll) Log.LogInfo("Mobile devices: http://" + GetLocalIP() + ":" + port);
                if (!string.IsNullOrEmpty(Plugin.HttpApiToken?.Value)) Log.LogInfo("HTTP API token required on /api/state, /api/gamedata, /api/pins, /api/activity and /api/census.");
            }
            catch (Exception ex)
            {
                Log.LogError("HTTP server failed to start: " + ex.Message);
            }
        }

        static string GetLocalIP()
        {
            try
            {
                using (var s = new System.Net.Sockets.UdpClient("8.8.8.8", 80))
                    return ((System.Net.IPEndPoint)s.Client.LocalEndPoint).Address.ToString();
            }
            catch { return "localhost"; }
        }

        void StopHttpServer()
        {
            try { _listener?.Stop(); } catch { }
            try { _serverThread?.Abort(); } catch { }
        }

        void HandleRequests()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => ProcessRequest(ctx));
                }
                catch { }
            }
        }

        void ProcessRequest(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
                if (string.IsNullOrEmpty(path)) path = "/";

                // CORS headers so browser can fetch from file:// or any origin
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                ctx.Response.AddHeader("Access-Control-Allow-Headers", "Content-Type, X-Api-Token");

                if (ctx.Request.HttpMethod == "OPTIONS")
                { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

                if (path == "/favicon.ico")
                { ctx.Response.StatusCode = 204; ctx.Response.Close(); return; }

                if (!ApiTokenOk(ctx, path))
                {
                    byte[] denied = Encoding.UTF8.GetBytes("{\"error\":\"token required\"}");
                    ctx.Response.StatusCode = 401;
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.ContentLength64 = denied.Length;
                    ctx.Response.OutputStream.Write(denied, 0, denied.Length);
                    ctx.Response.Close();
                    return;
                }

                byte[] body = Encoding.UTF8.GetBytes("Not found");
                if (path == "/api/state")
                {
                    ctx.Response.ContentType = "application/json";
                    // Main-thread cache, never a live walk of the registry
                    // from this worker thread (see _stateCache). Before the
                    // first prime the empty envelope keeps the shape invariant.
                    body = Encoding.UTF8.GetBytes(_stateCache ?? EmptyStateJson);
                }
                else if (path == "/api/gamedata")
                {
                    ctx.Response.ContentType = "application/json";
                    string gdPath = Path.Combine(OutputDir, "BarrkBOT_data2.json");
                    string gd = File.Exists(gdPath) ? File.ReadAllText(gdPath, Encoding.UTF8) : "{\"recipes\":[],\"items\":[],\"buildables\":[]}";
                    body = Encoding.UTF8.GetBytes(gd);
                }
                else if (path == "/api/health")
                {
                    ctx.Response.ContentType = "application/json";
                    body = Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"version\":\"" + VERSION + "\"}");
                }
                else if (path == "/api/pins")
                {
                    ctx.Response.ContentType = "application/json";
                    body = Encoding.UTF8.GetBytes(MapPinTracker.GetJson());
                }
                else if (path == "/api/activity")
                {
                    ctx.Response.ContentType = "application/json";
                    // Same cached-string-or-fixed-envelope discipline as
                    // /api/state — never a live walk from this worker thread.
                    // Never 404 while the server is 1.4.0: EventFeedCapacity=0
                    // still answers 200 with an empty events array (§2), which
                    // is exactly what the empty envelope already carries.
                    body = Encoding.UTF8.GetBytes(_activityCache ?? EmptyActivityJson);
                }
                else if (path == "/api/census")
                {
                    ctx.Response.ContentType = "application/json";
                    // Same cached-string-or-fixed-envelope discipline as
                    // /api/activity — never a live walk from this worker
                    // thread, never 404 while the HTTP server is up (§5).
                    // The fallback is WorldCensus's own empty envelope, built
                    // from the configured interval (review 2026-09-22).
                    body = Encoding.UTF8.GetBytes(_censusCache ?? WorldCensus.EmptyEnvelope());
                }
                else if (path == "/" || path == "/index" || path.EndsWith(".html"))
                {
                    // Serve HTML from OutputDir, then plugin dir, then the
                    // copy embedded in this DLL as the final fallback — a
                    // fresh install's zip has not always shipped the loose
                    // file, and the embedded copy means the dashboard still
                    // comes up even then.
                    string htmlPath = Path.Combine(OutputDir, "theravenscall.html");
                    if (!File.Exists(htmlPath))
                    {
                        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                        htmlPath = Path.Combine(pluginDir, "theravenscall.html");
                    }
                    if (File.Exists(htmlPath))
                    {
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        body = File.ReadAllBytes(htmlPath);
                        if (!_warnedStaleDiskDashboard && !ContainsApiMarker(body))
                        {
                            _warnedStaleDiskDashboard = true;
                            Log.LogWarning("theravenscall.html on disk (" + htmlPath + ") predates 1.3.0 and cannot read the new /api/state shape. " +
                                "Delete it so the 1.3.0 page embedded in this DLL is served instead.");
                        }
                    }
                    else
                    {
                        byte[] embedded = ReadEmbeddedHtml();
                        if (embedded != null)
                        {
                            ctx.Response.ContentType = "text/html; charset=utf-8";
                            body = embedded;
                        }
                        else
                        {
                            ctx.Response.StatusCode = 404;
                            body = Encoding.UTF8.GetBytes("<h2>theravenscall.html not found.<br>Place it in: " + OutputDir + "</h2>");
                            ctx.Response.ContentType = "text/html";
                        }
                    }
                }
                else
                {
                    ctx.Response.StatusCode = 404;
                    body = Encoding.UTF8.GetBytes("Not found");
                }

                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
                ctx.Response.Close();
            }
            catch (Exception ex)
            {
                Log.LogError("HTTP request error: " + ex.Message);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        // The 1.3.0 page carries <meta name="theravenscall-api" content="1.3">
        // in its head. A disk override written before that (the only way any
        // existing install ever got a dashboard — the store zip never shipped
        // the loose file) has no such tag and silently shadows the bundled
        // page while being unable to read the new /api/state shape.
        private static bool ContainsApiMarker(byte[] htmlBytes)
        {
            try { return Encoding.UTF8.GetString(htmlBytes).IndexOf("theravenscall-api", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { return true; } // don't warn on a read we can't even inspect
        }

        // Last-resort source for the dashboard page: the copy embedded in
        // this DLL via <EmbeddedResource LogicalName="theravenscall.html">
        // in the csproj. Only reached when neither disk override location
        // (OutputDir, then the plugin folder) has the file. Returns null on
        // any failure so the caller can fall back to the 404 text.
        private static byte[] ReadEmbeddedHtml()
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("theravenscall.html"))
                {
                    if (stream == null) return null;
                    using (var mem = new MemoryStream())
                    {
                        stream.CopyTo(mem);
                        return mem.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogError("Embedded theravenscall.html read failed: " + ex.Message);
                return null;
            }
        }

        // Called once from the ZNet.Awake postfix right after
        // PlayerRegistry.LoadAll, so the first /api/state after a restart
        // sees the historical roster instead of an empty envelope.
        internal static void PrimeStateCache()
        {
            if (Instance == null || Plugin.EnableHttpServer == null || !Plugin.EnableHttpServer.Value) return;
            try
            {
                string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "Unknown";
                int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
                // Plugin.GetConnectedPlayers() over a hardcoded 0: this runs
                // from ZNet.Awake's own postfix, before ZNet has accepted any
                // peers, so it naturally evaluates to 0 today — but reading
                // it live keeps this the same "who's online" source
                // PollAllPlayers uses instead of a second, driftable count.
                int onlineCount = Plugin.GetConnectedPlayers().Count();
                _stateCache = PlayerRegistry.BuildBarrkBotJson(worldName, day, onlineCount);
            }
            catch (Exception ex) { Log.LogError("state cache prime: " + ex.Message); }
        }

        // Built alongside _stateCache: generated_at + the season object +
        // the event feed, all from the same main-thread tick. Called from
        // the poll tick (PollAllPlayers) and once more at boot, as the last
        // call in Patch_ZNetAwake.Postfix, after SeasonSystem/EventFeed both
        // exist — PrimeStateCache above runs two lines before
        // SeasonSystem.Init, which would otherwise serve season.active=false
        // on every mid-season restart until the first tick.
        internal static void BuildActivityCache()
        {
            if (Instance == null || Plugin.EnableHttpServer == null || !Plugin.EnableHttpServer.Value) return;
            try
            {
                string generatedAt = DateTime.UtcNow.ToString("o");
                _activityCache = "{\"generated_at\":\"" + generatedAt + "\"," + SeasonSystem.SeasonJson() + ",\"events\":" + EventFeed.ToJsonArray() + "}";
            }
            catch (Exception ex) { Log.LogError("activity cache build: " + ex.Message); }
        }

        // Gate the data routes behind HttpApiToken when one is configured.
        // /api/health and the dashboard page stay open (no player data).
        static bool ApiTokenOk(HttpListenerContext ctx, string path)
        {
            string token = Plugin.HttpApiToken?.Value;
            if (string.IsNullOrEmpty(token)) return true;
            if (path != "/api/state" && path != "/api/gamedata" && path != "/api/pins" && path != "/api/activity" && path != "/api/census") return true;
            string given = ctx.Request.QueryString["token"] ?? ctx.Request.Headers["X-Api-Token"];
            return !string.IsNullOrEmpty(given) && string.Equals(given, token, StringComparison.Ordinal);
        }

        // Minimal field extractor for the HTTP server — extracts raw value (object/array/string/number)
        internal static string ExtractJsonField(string json, string key, bool isComplex)
        {
            if (json == null) return null;
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            if (i >= json.Length) return null;

            if (!isComplex)
            {
                // String value
                if (json[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    bool esc = false;
                    while (i < json.Length)
                    {
                        char c = json[i++];
                        if (esc) { sb.Append(c); esc = false; }
                        else if (c == '\\') esc = true;
                        else if (c == '"') break;
                        else sb.Append(c);
                    }
                    return sb.ToString();
                }
                // Number / bool
                int start = i;
                while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']') i++;
                return json.Substring(start, i - start).Trim();
            }

            // Complex: object {} or array []
            char open = json[i];
            char close = open == '{' ? '}' : ']';
            int depth = 0, start2 = i;
            bool inStr2 = false; bool esc2 = false;
            while (i < json.Length)
            {
                char c = json[i];
                if (esc2) { esc2 = false; i++; continue; }
                if (c == '\\' && inStr2) { esc2 = true; i++; continue; }
                if (c == '"') { inStr2 = !inStr2; i++; continue; }
                if (inStr2) { i++; continue; }
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) { i++; break; } }
                i++;
            }
            return json.Substring(start2, i - start2);
        }

        public static string B(bool v) => v ? "true" : "false";
        public static string Esc(string s)
        {
            if (s == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) break; // strip other control chars
                        sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
        // Never emit the bare NaN/Infinity tokens: they are not JSON, a strict
        // reader rejects the whole export, and the loader would parse them
        // straight back in (review 2026-09-15).
        public static string F(float v) => (float.IsNaN(v) || float.IsInfinity(v)) ? "0.0" : v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);

        public static string Loc(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            try
            {
                var loc = Localization.instance;
                if (loc != null) return loc.Localize(key) ?? key;
            }
            catch { }
            return key;
        }

        internal static Heightmap.Biome GetBiome(Player player)
        {
            if (player == null) return Heightmap.Biome.None;
            return GetBiomeAt(player.transform.position);
        }

        // Position-only overload — the one that actually runs server-side
        // today (see BiomeAndGearTracking.CheckBiome). WorldGenerator.instance
        // is pure world-generation math and works on a dedicated server with
        // no Player instance involved, unlike the Player-based overload above.
        internal static Heightmap.Biome GetBiomeAt(Vector3 position) =>
            WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(position) : Heightmap.Biome.None;

        // ── Central per-tick driver, called from Patch_ZNetUpdate in Saga.cs on
        // Plugin.StatsPushIntervalSeconds. Replaces the original per-character
        // WriteState(): iterates every online player instead of "the local
        // one", checks for new biomes/gear tiers, marks who's online, and
        // refreshes the BarrkBOT export file. All persistence goes through
        // PlayerRegistry now — there is no more per-character state/session/
        // stats.json trio on disk. ────────────────────────────────────────
        internal static void PollAllPlayers()
        {
            try
            {
                // Player.GetAllPlayers() is always empty on a dedicated server
                // (see HANDOFF/KILL_TRACKING_FINDINGS.md) — this used to silently
                // poll zero players every tick. Plugin.GetConnectedPlayers() is
                // the server-side "who's online and where" API instead.
                var online = Plugin.GetConnectedPlayers().ToList();
                foreach (var p in online)
                {
                    if (string.IsNullOrEmpty(p.name)) continue;
                    var rec = PlayerRegistry.Get(p.name);
                    rec.Online = true;
                    rec.LastSeen = DateTime.UtcNow.ToString("o");
                    BiomeAndGearTracking.CheckBiome(p.name, p.pos, rec);
                }
                PlayerRegistry.SaveDirty();

                string worldName = ZNet.instance != null ? ZNet.instance.GetWorldName() : "Unknown";
                int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
                string barrkBotJson = PlayerRegistry.BuildBarrkBotJson(worldName, day, online.Count);
                PlayerRegistry.AtomicWrite(Path.Combine(OutputDir, "BarrkBOT_data1.json"), barrkBotJson);

                // Outside the EnableHttpServer guard below, same as the
                // BarrkBOT export above — the feed persists even with the
                // HTTP server off. No-op unless dirty.
                EventFeed.Save();

                // /api/state is exactly this same string. It is built above
                // unconditionally every tick regardless of whether anyone is
                // watching (BarrkBOT_data1.json needs it either way), so
                // there is no separate cost to also caching it here for the
                // HTTP worker — the old "only rebuild if requested" gate
                // saved nothing and has been dropped (1.3.0).
                if (Instance != null && Plugin.EnableHttpServer.Value)
                {
                    _stateCache = barrkBotJson;
                    // A fault inside can never disturb the assignment above.
                    BuildActivityCache();
                    // Own schedule (CensusIntervalMinutes), not every tick —
                    // WorldCensus checks its own last-run clock and no-ops
                    // until due (§4). A fault inside is caught there too.
                    WorldCensus.MaybeRun();
                }

                Log.LogInfo($"Poll tick — {online.Count} online, {PlayerRegistry.All.Count()} known player(s).");
            }
            catch (Exception ex) { Log.LogError("PollAllPlayers: " + ex); }
        }

        internal IEnumerator WriteGameData()
        {
            Log.LogInfo("Writing game data...");
            string recipesJson;
            try { recipesJson = RecipeDumper.BuildJson(); }
            catch (Exception ex) { Log.LogError("RecipeDumper: " + ex); yield break; }

            // Build items list from ObjectDB
            var itemsSb = new StringBuilder("[");
            try
            {
                foreach (var prefab in ObjectDB.instance.m_items)
                {
                    if (prefab == null) continue;
                    var item = prefab.GetComponent<ItemDrop>();
                    if (item == null) continue;
                    var shared = item.m_itemData.m_shared;
                    string slug = Esc(shared.m_name);
                    string name = Esc(Loc(shared.m_name));
                    string cat = shared.m_itemType.ToString();
                    itemsSb.Append("{\"slug\":\"" + slug + "\",\"name\":\"" + name + "\",\"category\":\"" + cat + "\"},");
                }
                if (itemsSb.Length > 1) itemsSb.Length--;
            }
            catch (Exception ex) { Log.LogError("Items build error: " + ex); }
            itemsSb.Append("]");

            // Build buildables list from all PieceTables
            var buildsSb = new StringBuilder("[");
            try
            {
                bool firstB2 = true;
                foreach (var prefab in ObjectDB.instance.m_items)
                {
                    if (prefab == null) continue;
                    var itemDrop = prefab.GetComponent<ItemDrop>();
                    if (itemDrop == null) continue;
                    var shared = itemDrop.m_itemData.m_shared;
                    if (shared.m_buildPieces == null) continue;
                    foreach (var piece in shared.m_buildPieces.m_pieces)
                    {
                        if (piece == null) continue;
                        var pc = piece.GetComponent<Piece>();
                        if (pc == null) continue;
                        string pname = Esc(Loc(pc.m_name));
                        string pcat = pc.m_category.ToString();
                        var resSb = new StringBuilder("[");
                        bool firstR = true;
                        foreach (var req in pc.m_resources)
                        {
                            if (req.m_resItem == null) continue;
                            string rSlug = Esc(req.m_resItem.m_itemData.m_shared.m_name);
                            string rName = Esc(Loc(req.m_resItem.m_itemData.m_shared.m_name));
                            if (!firstR) resSb.Append(",");
                            resSb.Append("{\"slug\":\"" + rSlug + "\",\"name\":\"" + rName + "\",\"qty\":" + req.m_amount + "}");
                            firstR = false;
                        }
                        resSb.Append("]");
                        if (!firstB2) buildsSb.Append(",");
                        buildsSb.Append("{\"name\":\"" + pname + "\",\"category\":\"" + pcat + "\",\"ingredients\":" + resSb + "}");
                        firstB2 = false;
                    }
                }
            }
            catch (Exception ex) { Log.LogError("Buildables build error: " + ex); }
            buildsSb.Append("]");

            string payload = "{\"items\":" + itemsSb + ",\"recipes\":" + recipesJson + ",\"buildables\":" + buildsSb + ",\"updated_at\":\"" + DateTime.UtcNow.ToString("o") + "\"}";

            try
            {
                string path = Path.Combine(OutputDir, "BarrkBOT_data2.json");
                PlayerRegistry.AtomicWrite(path, payload);
                Log.LogInfo("Game data written OK");
            }
            catch (Exception ex) { Log.LogError("WriteGameData file error: " + ex); }
        }

        // ── Appends one death_history entry to a player's record. Called once
        // from Patch_CharacterDeath (Saga.cs) per real player death — that
        // patch already dedupes Character.OnDeath firing once per death, so
        // the original two-patch/5-second dedupe hack is no longer needed. ──
        internal static void RecordDeathHistory(PlayerRecord rec, Player player)
        {
            Log.LogInfo("Death recorded for " + player.GetPlayerName());

            // Try to get cause of death from last hit
            string killer = "Unknown";
            try
            {
                var fLastHit = AccessTools.Field(typeof(Character), "m_lastHit");
                if (fLastHit != null)
                {
                    var lastHit = fLastHit.GetValue(player) as HitData;
                    if (lastHit != null)
                    {
                        var dmg = lastHit.m_damage;
                        // Try attacker ZDO
                        var attackerID = lastHit.m_attacker;
                        if (attackerID != ZDOID.None)
                        {
                            var zdo = ZDOMan.instance?.GetZDO(attackerID);
                            if (zdo != null)
                            {
                                var prefab = ZNetScene.instance?.GetPrefab(zdo.GetPrefab());
                                if (prefab != null)
                                {
                                    string pname = prefab.name.Replace("(Clone)", "").Trim();
                                    killer = FriendlyCreatureName(pname);
                                }
                            }
                        }
                        // Fall back to damage type if no attacker
                        if (killer == "Unknown")
                        {
                            float fire = dmg.m_fire, frost = dmg.m_frost, poison = dmg.m_poison,
                                  spirit = dmg.m_spirit, lightning = dmg.m_lightning,
                                  blunt = dmg.m_blunt, slash = dmg.m_slash, pierce = dmg.m_pierce;
                            float elemental = fire + frost + poison + spirit + lightning;
                            float physical = blunt + slash + pierce;
                            if (elemental > 0)
                            {
                                float max = Math.Max(fire, Math.Max(frost, Math.Max(poison, Math.Max(spirit, lightning))));
                                if (max == fire) killer = "Fire";
                                else if (max == frost) killer = "Frost";
                                else if (max == poison) killer = "Poison";
                                else if (max == spirit) killer = "Spirit";
                                else killer = "Lightning";
                            }
                            else if (physical > 0 && lastHit.m_attacker == ZDOID.None)
                            {
                                bool inWater = false;
                                try { inWater = player.IsSwimming() || player.InWater(); } catch { }
                                killer = inWater ? "Drowning" : "Fall Damage";
                            }
                            else if (physical > 0)
                            {
                                killer = "Combat";
                            }
                            else
                            {
                                // All damage zero — fall damage bypasses hit system in Valheim
                                bool inWater = false;
                                try { inWater = player.IsSwimming() || player.InWater(); } catch { }
                                killer = inWater ? "Drowning" : "Fall Damage";
                            }
                        }
                    }
                    else
                    {
                        // lastHit is null — fall damage bypasses the normal hit system in Valheim
                        bool inWater = false;
                        try { inWater = player.IsSwimming() || player.InWater(); } catch { }
                        killer = inWater ? "Drowning" : "Fall Damage";
                    }
                }
                else
                {
                    // m_lastHit field not found — assume fall/environment
                    bool inWater = false;
                    try { inWater = player.IsSwimming() || player.InWater(); } catch { }
                    killer = inWater ? "Drowning" : "Fall Damage";
                }
            }
            catch (Exception ex) { Log.LogWarning("Death cause lookup: " + ex.Message); }

            var pos = player.transform.position;
            string biome = GetBiome(player).ToString();

            var itemsSb = new StringBuilder("[");
            bool first = true;
            foreach (var item in player.GetInventory().GetAllItems())
            {
                if (!first) itemsSb.Append(",");
                string iname = Esc(Loc(item.m_shared.m_name));
                itemsSb.Append("{\"name\":\"" + iname + "\",\"qty\":" + item.m_stack + "}");
                first = false;
            }
            itemsSb.Append("]");

            string entry = "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"," +
                "\"killer\":\"" + Esc(killer) + "\"," +
                "\"location\":{\"x\":" + F(pos.x) + ",\"y\":" + F(pos.y) + ",\"z\":" + F(pos.z) + "}," +
                "\"biome\":\"" + Esc(biome) + "\"," +
                "\"items\":" + itemsSb + "}";

            // Prepend so index 0 is the most recent; keep max 10. Caller
            // (Patch_CharacterDeath in Saga.cs) persists rec afterward.
            rec.DeathHistory.Insert(0, entry);
            if (rec.DeathHistory.Count > 10) rec.DeathHistory.RemoveAt(rec.DeathHistory.Count - 1);
        }

        // ── The RPC-report path's death_history entry (CombatCredit.
        // CreditPlayerDeath in Saga.cs). No Player instance exists to read
        // inventory/swim-state from — cause comes pre-computed from the
        // reporting client instead of being reconstructed from m_lastHit
        // after the fact, and items is always empty. Biome still works,
        // since it's pure position math (GetBiomeAt). ─────────────────────
        internal static void RecordDeathHistoryFromReport(PlayerRecord rec, string cause, Vector3 position)
        {
            string biome = GetBiomeAt(position).ToString();
            string entry = "{\"timestamp\":\"" + DateTime.UtcNow.ToString("o") + "\"," +
                "\"killer\":\"" + Esc(cause) + "\"," +
                "\"location\":{\"x\":" + F(position.x) + ",\"y\":" + F(position.y) + ",\"z\":" + F(position.z) + "}," +
                "\"biome\":\"" + Esc(biome) + "\"," +
                "\"items\":[]}";

            rec.DeathHistory.Insert(0, entry);
            if (rec.DeathHistory.Count > 10) rec.DeathHistory.RemoveAt(rec.DeathHistory.Count - 1);
        }

        static string FriendlyCreatureName(string prefab)
        {
            var map = new Dictionary<string, string>
            {
                {"Greyling","Greyling"},{"Greydwarf","Greydwarf"},{"Greydwarf_Elite","Greydwarf Brute"},
                {"Greydwarf_Shaman","Greydwarf Shaman"},{"Troll","Troll"},{"Ghost","Ghost"},
                {"Skeleton","Skeleton"},{"Skeleton_Poison","Poison Skeleton"},{"SkeletonNoArcher","Skeleton"},
                {"Draugr","Draugr"},{"Draugr_Elite","Draugr Elite"},{"Draugr_Ranged","Draugr Archer"},
                {"Blob","Blob"},{"BlobElite","Oozer"},{"Wraith","Wraith"},{"Abomination","Abomination"},
                {"Surtling","Surtling"},{"Goblin","Fuling"},{"GoblinArcher","Fuling Archer"},
                {"GoblinBrute","Fuling Berserker"},{"GoblinShaman","Fuling Shaman"},
                {"GoblinKing","Yagluth"},{"Eikthyr","Eikthyr"},{"gd_king","The Elder"},
                {"Bonemass","Bonemass"},{"Dragon","Moder"},{"SeekerQueen","The Queen"},
                {"Fader","Fader"},{"Seeker","Seeker"},{"SeekerBrute","Seeker Soldier"},
                {"Tick","Tick"},{"Gjall","Gjall"},{"Charred_Melee","Charred"},
                {"Charred_Archer","Charred Archer"},{"Charred_Twitcher","Charred Twitcher"},
                {"Charred_Shaman","Charred Shaman"},{"FallenValkyrie","Valkyrie"},
                {"Lox","Lox"},{"Wolf","Wolf"},{"Serpent","Sea Serpent"},{"Deathsquito","Deathsquito"},
                {"StoneGolem","Stone Golem"},{"Bat","Bat"},{"Fenring","Fenring"},
                {"Hare","Hare"},{"Asksvin","Asksvin"},
            };
            return map.TryGetValue(prefab, out string friendly) ? friendly : prefab;
        }

        // ── JSON parsing helpers (reads our own well-formed stats.json) ──

        internal static string JsonGetString(string json, string key)
        {
            if (json == null) return null;
            string needle = "\"" + key + "\":\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;
            i += needle.Length;
            var sb = new StringBuilder();
            bool esc = false;
            while (i < json.Length)
            {
                char c = json[i++];
                if (esc) { sb.Append(c); esc = false; }
                else if (c == '\\') esc = true;
                else if (c == '"') break;
                else sb.Append(c);
            }
            return sb.ToString();
        }

        internal static int JsonGetInt(string json, string key)
        {
            if (json == null) return 0;
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            int start = i;
            if (i < json.Length && json[i] == '-') i++;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            int.TryParse(json.Substring(start, i - start), out int v);
            return v;
        }

        internal static bool JsonGetBossDefeated(string json, string boss)
        {
            if (json == null) return false;
            // Locate the "bosses":{...} section and check inside it
            int bossesIdx = json.IndexOf("\"bosses\":{", StringComparison.Ordinal);
            if (bossesIdx < 0) return false;
            int end = json.IndexOf("}", bossesIdx + 9);
            if (end < 0) return false;
            string section = json.Substring(bossesIdx + 9, end - bossesIdx - 9);
            return section.IndexOf("\"" + boss + "\":true", StringComparison.Ordinal) >= 0;
        }

        internal static List<string> JsonGetArray(string json, string key)
        {
            var result = new List<string>();
            if (json == null) return result;
            string needle = "\"" + key + "\":[";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return result;
            i += needle.Length; // points just after '['

            int depth = 0;
            int objStart = -1;
            bool inStr = false;
            bool esc = false;

            while (i < json.Length)
            {
                char c = json[i];
                if (esc) { esc = false; i++; continue; }
                if (c == '\\' && inStr) { esc = true; i++; continue; }
                if (c == '"') { inStr = !inStr; i++; continue; }
                if (inStr) { i++; continue; }

                if (c == '{')
                {
                    if (depth == 0) objStart = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && objStart >= 0)
                    {
                        result.Add(json.Substring(objStart, i - objStart + 1));
                        objStart = -1;
                    }
                }
                else if (c == ']' && depth == 0) break;
                i++;
            }
            return result;
        }

        internal static List<string> JsonGetStringArray(string json, string key)
        {
            var result = new List<string>();
            if (json == null) return result;
            string needle = "\"" + key + "\":[";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return result;
            i += needle.Length; // points just after '['

            bool inStr = false;
            bool esc = false;
            int strStart = -1;

            while (i < json.Length)
            {
                char c = json[i];
                if (esc) { esc = false; i++; continue; }
                if (c == '\\' && inStr) { esc = true; i++; continue; }
                if (c == '"')
                {
                    if (!inStr) { inStr = true; strStart = i + 1; }
                    else
                    {
                        if (strStart >= 0) result.Add(json.Substring(strStart, i - strStart));
                        inStr = false; strStart = -1;
                    }
                    i++; continue;
                }
                if (!inStr && c == ']') break;
                i++;
            }
            return result;
        }

        internal static long JsonGetLong(string json, string key)
        {
            if (json == null) return 0;
            string needle = "\"" + key + "\":";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return 0;
            i += needle.Length;
            while (i < json.Length && json[i] == ' ') i++;
            int start = i;
            if (i < json.Length && json[i] == '-') i++;
            while (i < json.Length && char.IsDigit(json[i])) i++;
            long.TryParse(json.Substring(start, i - start), out long v);
            return v;
        }

        // Reflects ZDOMan's own object table (ZDOMan.m_objectsByID, private)
        // — nothing public enumerates every ZDO in the loaded world. Used by
        // TimerTracker (client-era, dead on a dedicated server — see its own
        // comment) and, since 1.5.0, by WorldCensus's own walk. internal
        // (moved here from TimerTracker in 1.5.0) so WorldCensus can call it
        // as Companion.GetAllZDOs(). Returns an empty list, silently, on any
        // reflection failure; WorldCensus logs one warning the first time
        // that happens while ZDOMan.instance still exists, so a future game
        // build that renames the field is visible in the log instead of
        // silently reporting a zero census.
        internal static List<ZDO> GetAllZDOs()
        {
            var result = new List<ZDO>();
            try
            {
                var field = typeof(ZDOMan).GetField("m_objectsByID",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var dict = field.GetValue(ZDOMan.instance) as System.Collections.Generic.Dictionary<ZDOID, ZDO>;
                    if (dict != null) { result.AddRange(dict.Values); return result; }
                }
            }
            catch { }
            return result;
        }
    }

    static class RecipeDumper
    {
        public static string BuildJson()
        {
            if (ObjectDB.instance == null) return "[]";
            var sb = new StringBuilder("[");
            foreach (var recipe in ObjectDB.instance.m_recipes)
            {
                if (recipe == null || recipe.m_item == null) continue;
                var shared = recipe.m_item.m_itemData.m_shared;
                string name = Companion.Esc(Companion.Loc(shared.m_name));
                string slug = Companion.Esc(shared.m_name);
                string recipeKey = Companion.Esc(recipe.name); // This is what Player.m_knownRecipes stores
                string category = shared.m_itemType.ToString();
                int amount = recipe.m_amount;
                string station = recipe.m_craftingStation != null ? Companion.Esc(Companion.Loc(recipe.m_craftingStation.m_name)) : "Hand";
                var ingsSb = new StringBuilder("[");
                foreach (var res in recipe.m_resources)
                {
                    if (res.m_resItem == null) continue;
                    string iSlug = Companion.Esc(res.m_resItem.m_itemData.m_shared.m_name);
                    string iName = Companion.Esc(Companion.Loc(res.m_resItem.m_itemData.m_shared.m_name));
                    ingsSb.Append("{\"slug\":\"" + iSlug + "\",\"name\":\"" + iName + "\",\"qty\":" + res.m_amount + "},");
                }
                if (ingsSb.Length > 1) ingsSb.Length--;
                ingsSb.Append("]");
                sb.Append("{\"slug\":\"" + slug + "\",\"recipe_key\":\"" + recipeKey + "\",\"name\":\"" + name + "\",\"category\":\"" + category + "\",\"amount\":" + amount + ",\"station\":\"" + station + "\",\"ingredients\":" + ingsSb + "},");
            }
            if (sb.Length > 1) sb.Length--;
            sb.Append("]");
            return sb.ToString();
        }
    }

    // Damage/death/raid/fish tracking now happens server-side in Saga.cs
    // (Patch_Damage, Patch_CharacterDeath, Patch_Raid, Patch_FishCatch),
    // attributed to whichever real player is actually involved rather than
    // "the local player" — see project_ravenscall_server_side memory. The
    // boss prefab -> canonical short-key mapping below is kept here since
    // PlayerRegistry keys bosses_defeated by these names (PlayerRegistry.ToJson).
    internal static class BossKeys
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            {"Eikthyr",    "eikthyr"},
            {"gd_king",    "elder"},
            {"Bonemass",   "bonemass"},
            {"Dragon",     "moder"},
            {"GoblinKing", "yagluth"},
            {"SeekerQueen","queen"},
            {"Fader",      "fader"},
        };

        public static string Resolve(string prefab) => Map.TryGetValue(prefab, out string key) ? key : null;
    }

    static class ChestTracker
    {

        public static string GetJson(long playerID)
        {
            var sb = new StringBuilder("[");
            foreach (var container in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
            {
                if (container == null) continue;
                var inv = container.GetInventory();
                if (inv == null) continue;

                // Filter: only include containers built/owned by the given player
                if (playerID == 0L) continue;
                var nview = container.GetComponent<ZNetView>();
                if (nview == null || !nview.IsValid()) continue;
                long creator = nview.GetZDO().GetLong(ZDOVars.s_creator, 0L);
                if (creator == 0L || creator != playerID) continue;

                foreach (var item in inv.GetAllItems())
                {
                    string slug = Companion.Esc(item.m_shared.m_name);
                    string cname = Companion.Esc(Companion.Loc(item.m_shared.m_name));
                    string cat = item.m_shared.m_itemType.ToString();
                    sb.Append("{\"slug\":\"" + slug + "\",\"name\":\"" + cname + "\",\"qty\":" + item.m_stack + ",\"category\":\"" + cat + "\"},");
                }
            }
            if (sb.Length > 1) sb.Length--;
            sb.Append("]");
            return sb.ToString();
        }
    }

    static class TimerTracker
    {
        // Smelter prefab name -> display name
        static readonly Dictionary<string, string> SMELTER_NAMES = new Dictionary<string, string> {
            { "smelter",              "Smelter"       },
            { "charcoal_kiln",        "Kiln"          },
            { "blastfurnace",         "Blast Furnace" },
            { "piece_spinningwheel",  "Spinning Wheel"},
            { "windmill",             "Windmill"      },
            { "food_smelter",         "Food Smelter"  },
            { "eitrrefinery",    "Eitr Refinery"  },
        };

        // Plant prefab name -> display name
        static readonly Dictionary<string, string> PLANT_NAMES = new Dictionary<string, string> {
            { "sapling_carrot",     "Carrot"      },
            { "sapling_turnip",     "Turnip"      },
            { "sapling_onion",      "Onion"       },
            { "sapling_barley",     "Barley"      },
            { "sapling_flax",       "Flax"        },
            { "sapling_jotunpuffs", "Jotun Puffs" },
            { "sapling_magecap",    "Magecap"     },
            { "sapling_seedcarrot", "Seed Carrot" },
            { "sapling_seedturnip", "Seed Turnip" },
            { "sapling_seedonion",  "Seed Onion"  },
            { "ChickenEgg",      "Chicken Egg" },
        };

        // Fermenter duration constants (seconds)
        const float FERMENTER_DURATION = 2400f;
        // Beehive constants
        const float BEEHIVE_SEC_PER_UNIT = 1200f;
        const int BEEHIVE_MAX_HONEY = 4;
        // Plant grow times (seconds)
        static readonly Dictionary<string, float> PLANT_GROW_TIMES = new Dictionary<string, float> {
            { "sapling_carrot",     4000f },
            { "sapling_turnip",     4000f },
            { "sapling_onion",      4000f },
            { "sapling_barley",     4000f },
            { "sapling_flax",       4000f },
            { "sapling_jotunpuffs", 3000f },
            { "sapling_magecap",    3000f },
            { "sapling_seedcarrot", 5000f },
            { "sapling_seedturnip", 5000f },
            { "sapling_seedonion",  5000f },
            { "ChickenEgg",      1800f },
        };
        // Smelter sec per product
        static readonly Dictionary<string, float> SMELTER_SPP = new Dictionary<string, float> {
            { "smelter",             15f },
            { "charcoal_kiln",       15f },
            { "blastfurnace",        15f },
            { "piece_spinningwheel", 30f },
            { "windmill",            10f },
            { "food_smelter",        15f },
            { "eitrrefinery",  15f },
        };

        static readonly System.Collections.Generic.Dictionary<string, int> _hashCache
            = new System.Collections.Generic.Dictionary<string, int>();

        static int PrefabHash(string name)
        {
            if (_hashCache.TryGetValue(name, out int cached)) return cached;
            try
            {
                if (ZNetScene.instance == null) return 0;
                var field = typeof(ZNetScene).GetField("m_namedPrefabs",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    var dict = field.GetValue(ZNetScene.instance)
                        as System.Collections.Generic.Dictionary<int, UnityEngine.GameObject>;
                    if (dict != null)
                        foreach (var kv in dict)
                            if (kv.Value != null && kv.Value.name == name)
                            { _hashCache[name] = kv.Key; return kv.Key; }
                }
            }
            catch { }
            return 0;
        }

        public static string GetJson(long playerID)
        {
            if (ZDOMan.instance == null || ZNet.instance == null)
                return "{\"fermenters\":[],\"smelters\":[],\"beehives\":[],\"plants\":[]}";

            double now = ZNet.instance.GetTimeSeconds();
            long unixNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var allZDOs = Companion.GetAllZDOs();


            var fermenters = new System.Text.StringBuilder();
            var smelters = new System.Text.StringBuilder();
            var beehives = new System.Text.StringBuilder();
            var plants = new System.Text.StringBuilder();

            // ── Fermenters ── use component methods directly
            bool firstF = true;
            var allFermenters = UnityEngine.Object.FindObjectsByType<Fermenter>(FindObjectsSortMode.None);
            var getFermTime = typeof(Fermenter).GetMethod("GetFermentationTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var getFermStatus = typeof(Fermenter).GetMethod("GetStatus", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fermDuration = typeof(Fermenter).GetField("m_fermentationDuration", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var comp in allFermenters)
            {
                try
                {
                    var nview = comp.GetComponent<ZNetView>();
                    if (nview == null || !nview.IsValid()) continue;
                    if (playerID != 0 && nview.GetZDO().GetLong(ZDOVars.s_creator, 0L) != playerID) continue;

                    double fermentTime = getFermTime != null ? (double)getFermTime.Invoke(comp, null) : 0.0;
                    var getFermContentRaw = typeof(Fermenter).GetMethod("GetContent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    string slug = getFermContentRaw != null ? (string)getFermContentRaw.Invoke(comp, null) ?? "" : "";
                    string cname = "";
                    if (!string.IsNullOrEmpty(slug))
                    {
                        var prefab = ObjectDB.instance?.GetItemPrefab(slug);
                        cname = Companion.Esc(Companion.Loc(prefab?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? slug));
                    }
                    object statusObj = getFermStatus != null ? getFermStatus.Invoke(comp, null) : null;
                    string status = statusObj != null ? statusObj.ToString() : "Unknown";
                    float duration = fermDuration != null ? (float)fermDuration.GetValue(comp) : FERMENTER_DURATION;

                    float remaining = (float)System.Math.Max(0.0, duration - fermentTime);
                    float pct = duration > 0 ? Mathf.Clamp01((float)fermentTime / duration) * 100f : 0f;
                    if (!firstF) fermenters.Append(",");
                    long fermEndMs = remaining > 0 ? unixNow + (long)(remaining * 1000) : 0;
                    fermenters.Append("{\"status\":\"" + status + "\",\"content\":\"" + cname + "\",\"duration\":" + Companion.F(duration) + ",\"remaining\":" + Companion.F(remaining) + ",\"end_ms\":" + fermEndMs + ",\"percent\":" + Companion.F(pct) + "}");
                    firstF = false;
                }
                catch { }
            }

            // ── Smelters / Kilns ──
            bool firstS = true;
            foreach (var kv in SMELTER_NAMES)
            {
                int sHash = PrefabHash(kv.Key);
                float spp = SMELTER_SPP.TryGetValue(kv.Key, out float sv) ? sv : 15f;
                foreach (var zdo in allZDOs)
                {
                    if (zdo.GetPrefab() != sHash) continue;
                    if (playerID != 0 && zdo.GetLong(ZDOVars.s_creator, 0L) != playerID) continue;
                    try
                    {
                        int fuel = (int)zdo.GetFloat(ZDOVars.s_fuel, 0f);
                        int queue = zdo.GetInt(ZDOVars.s_queued, 0);
                        float accTime = zdo.GetFloat("accTime", 0f);
                        float remaining = queue > 0 ? spp * queue - accTime : 0f;
                        if (remaining < 0f) remaining = 0f;
                        // Blast furnace uses "SpawnOre" key; other smelters use "item"
                        bool isBlast = kv.Key == "blastfurnace";
                        string inputSlug = isBlast
                            ? zdo.GetString("SpawnOre", "")
                            : zdo.GetString(ZDOVars.s_item, "");
                        string inputName = "";
                        if (!string.IsNullOrEmpty(inputSlug))
                        {
                            var prefab = ObjectDB.instance?.GetItemPrefab(inputSlug);
                            inputName = Companion.Esc(Companion.Loc(prefab?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? inputSlug));
                        }
                        if (!firstS) smelters.Append(",");
                        long smeltEndMs = remaining > 0 ? unixNow + (long)(remaining * 1000) : 0;
                        smelters.Append("{\"name\":\"" + Companion.Esc(kv.Value) + "\",\"fuel\":" + fuel + ",\"queue\":" + queue + ",\"input\":\"" + inputName + "\",\"sec_per_item\":" + Companion.F(spp) + ",\"remaining\":" + Companion.F(remaining) + ",\"end_ms\":" + smeltEndMs + "}");
                        firstS = false;
                    }
                    catch { }
                }
            }

            // ── Beehives ──
            int beeHash = PrefabHash("piece_beehive");
            bool firstB = true;
            if (beeHash != 0)
            {
                foreach (var zdo in allZDOs)
                {
                    if (zdo.GetPrefab() != beeHash) continue;
                    if (playerID != 0 && zdo.GetLong(ZDOVars.s_creator, 0L) != playerID) continue;
                    try
                    {
                        int honey = zdo.GetInt(ZDOVars.s_level, 0);
                        double lastTime = (double)zdo.GetFloat(ZDOVars.s_lastTime, 0f);
                        double elapsed = lastTime > 0 ? now - lastTime : 0.0;
                        float remaining = (float)System.Math.Max(0.0, BEEHIVE_SEC_PER_UNIT - elapsed);
                        bool isReady = honey >= BEEHIVE_MAX_HONEY;
                        string status = isReady ? "Ready" : honey > 0 ? "Producing" : "Empty";
                        if (!firstB) beehives.Append(",");
                        long beeEndMs = (!isReady && remaining > 0) ? unixNow + (long)(remaining * 1000) : 0;
                        beehives.Append("{\"honey\":" + honey + ",\"max_honey\":" + BEEHIVE_MAX_HONEY + ",\"sec_per_unit\":" + Companion.F(BEEHIVE_SEC_PER_UNIT) + ",\"remaining\":" + Companion.F(isReady ? 0f : remaining) + ",\"end_ms\":" + beeEndMs + ",\"status\":\"" + status + "\"}");
                        firstB = false;
                    }
                    catch { }
                }
            }

            // ── Plants ──
            bool firstP = true;
            foreach (var kv in PLANT_NAMES)
            {
                int pHash = PrefabHash(kv.Key);
                float growTime = PLANT_GROW_TIMES.TryGetValue(kv.Key, out float gt) ? gt : 4000f;
                int ready = 0, growing = 0;
                float totalRemaining = 0f;
                foreach (var zdo in allZDOs)
                {
                    if (zdo.GetPrefab() != pHash) continue;
                    if (playerID != 0 && zdo.GetLong(ZDOVars.s_creator, 0L) != playerID) continue;
                    try
                    {
                        double plantTime = (double)zdo.GetFloat(ZDOVars.s_plantTime, 0f);
                        double elapsed = plantTime > 0 ? now - plantTime : 0.0;
                        float rem = (float)System.Math.Max(0.0, growTime - elapsed);
                        if (rem <= 0f) ready++; else { growing++; totalRemaining += rem; }
                    }
                    catch { }
                }
                if (ready + growing == 0) continue;
                float avgRem = growing > 0 ? totalRemaining / growing : 0f;
                float pPct = growTime > 0 && growing > 0 ? Mathf.Clamp01(1f - (avgRem / growTime)) * 100f : (ready > 0 ? 100f : 0f);
                if (!firstP) plants.Append(",");
                long plantEndMs = avgRem > 0 ? unixNow + (long)(avgRem * 1000) : 0;
                plants.Append("{\"name\":\"" + Companion.Esc(kv.Value) + "\",\"total\":" + (ready + growing) + ",\"ready\":" + ready + ",\"growing\":" + growing + ",\"avg_remaining\":" + Companion.F(avgRem) + ",\"end_ms\":" + plantEndMs + ",\"percent\":" + Companion.F(pPct) + "}");
                firstP = false;
            }

            // ── Sap Collectors ──
            int sapHash = PrefabHash("piece_sapcollector");
            var sapCollectors = new System.Text.StringBuilder();
            bool firstSap = true;
            if (sapHash != 0)
            {
                foreach (var zdo in allZDOs)
                {
                    if (zdo.GetPrefab() != sapHash) continue;
                    if (playerID != 0 && zdo.GetLong(ZDOVars.s_creator, 0L) != playerID) continue;
                    try
                    {
                        bool connected = zdo.GetBool("wassap", false);
                        double spawnTime = (double)zdo.GetFloat("SpawnTime", 0f);
                        double lastT = (double)zdo.GetFloat("lastTime", 0f);
                        // Sap collector produces 1 unit every 120s when connected
                        const float SAP_INTERVAL = 120f;
                        double elapsed = spawnTime > 0 ? now - spawnTime : 0.0;
                        float remaining = connected ? (float)System.Math.Max(0.0, SAP_INTERVAL - elapsed) : 0f;
                        string status = !connected ? "Not Connected" : remaining <= 0 ? "Ready" : "Producing";
                        long sapEndMs = (connected && remaining > 0) ? unixNow + (long)(remaining * 1000) : 0;
                        if (!firstSap) sapCollectors.Append(",");
                        sapCollectors.Append("{\"connected\":" + Companion.B(connected) + ",\"remaining\":" + Companion.F(remaining) + ",\"end_ms\":" + sapEndMs + ",\"status\":\"" + status + "\"}");
                        firstSap = false;
                    }
                    catch { }
                }
            }

            string timerJson = "{\"fermenters\":[" + fermenters.ToString() + "],\"smelters\":[" + smelters.ToString() + "],\"beehives\":[" + beehives.ToString() + "],\"plants\":[" + plants.ToString() + "],\"sap_collectors\":[" + sapCollectors.ToString() + "]}";
            return timerJson;
        }

    }

    static class MapPinTracker
    {
        public static string GetJson()
        {
            try
            {
                var minimap = Minimap.instance;
                if (minimap == null) return "[]";

                var pinsField = typeof(Minimap).GetField("m_pins",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pinsField == null) return "[]";
                var pins = pinsField.GetValue(minimap) as System.Collections.IList;
                if (pins == null || pins.Count == 0) return "[]";

                // Reflect PinData fields once
                var pinType = pins[0].GetType();
                var fPos = pinType.GetField("m_pos");
                var fType = pinType.GetField("m_type");
                var fName = pinType.GetField("m_name");
                var fChecked = pinType.GetField("m_checked");
                var fOwner = pinType.GetField("m_ownerID");

                var sb = new System.Text.StringBuilder("[");
                bool first = true;
                foreach (var pin in pins)
                {
                    if (pin == null) continue;
                    if (!first) sb.Append(",");
                    string name = Companion.Esc(fName != null ? (string)fName.GetValue(pin) ?? "" : "");
                    string type = fType != null ? fType.GetValue(pin).ToString() : "Unknown";
                    bool chkd = fChecked != null && (bool)fChecked.GetValue(pin);
                    long ownerID = fOwner != null ? (long)fOwner.GetValue(pin) : 0L;
                    bool shared = ownerID != 0L;
                    Vector3 pos = fPos != null ? (Vector3)fPos.GetValue(pin) : Vector3.zero;
                    sb.Append("{\"name\":\"" + name + "\",\"type\":\"" + type + "\",\"checked\":" + Companion.B(chkd) + ",\"shared\":" + Companion.B(shared) + ",\"x\":" + Companion.F(pos.x) + ",\"y\":" + Companion.F(pos.y) + ",\"z\":" + Companion.F(pos.z) + "}");
                    first = false;
                }
                sb.Append("]");
                return sb.ToString();
            }
            catch (Exception ex) { Companion.Log?.LogWarning("MapPinTracker: " + ex.Message); return "[]"; }
        }
    }

    static class BoatTracker
    {
        // NOTE: the original mod used Ship.GetLocalShip() (which ship "I" am
        // standing on) — a client-only concept. This is a best-effort,
        // unverified server-side equivalent: a player standing on a boat's
        // deck is physics-parented under that Ship's transform in Valheim,
        // so GetComponentInParent<Ship>() should resolve it for any given
        // player. Flagged for a live smoke test — not confirmed against
        // decompiled source.
        public static string GetJson(Player player)
        {
            try
            {
                var ship = player != null ? player.GetComponentInParent<Ship>() : null;
                if (ship == null) return "[]";
                string btype = Companion.Esc(ship.gameObject.name.Replace("(Clone)", "").Trim().ToLower());
                string speed = Companion.F(ship.GetSpeed());
                var wnt = ship.GetComponent<WearNTear>();
                string bhp = wnt != null ? Companion.F(wnt.GetHealthPercentage() * 1000f) : "0.0";
                return "[{\"name\":\"" + btype + "\",\"type\":\"" + btype + "\",\"speed\":" + speed + ",\"health\":" + bhp + ",\"max_health\":1000}]";
            }
            catch { return "[]"; }
        }
    }
}
