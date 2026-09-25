using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TheRavensCall
{
    // ── Architecture ──────────────────────────────────────────────────────────
    // Server-only admin/BarrkBOT tool. TheRavensCall itself is never installed
    // by players. Two different attribution models coexist here:
    //   - Join/leave/raids/day/season are detected authoritatively BY the
    //     dedicated server itself (ZNet/RandEventSystem/EnvMan all run here).
    //   - Kills/deaths/damage/fish are NOT — Character.Damage/OnDeath and
    //     FishingFloat.Catch are owner-side simulation methods that fire on
    //     whichever client owns the zone a fight happens in, never on the
    //     server (see KILL_TRACKING_FINDINGS.md). Those patches are kept below
    //     as dormant insurance, but the live path is a routed RPC
    //     (RavensCall_CombatReport_V1, see CombatReportReceiver) sent by an
    //     opt-in client-side companion mod (WhereTheCrowFlies) whenever a
    //     player's own client observes a kill/death/damage/catch it owns.
    // Everything is narrated out through channels that don't require a player
    // to have TheRavensCall itself: the Discord webhook, the Chronicle log,
    // and the HTTP/BarrkBOT JSON exports (see PlayerRegistry.cs, Companion.cs).
    // ─────────────────────────────────────────────────────────────────────────

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.raveniron.theravenscall";
        public const string PluginName = "TheRavensCall";
        public const string PluginVersion = "1.8.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
        // Set once, in Patch_ZNetAwake.Postfix, the moment ZNet confirms this
        // process is the dedicated server. On a dedicated server ZNet.instance
        // is already torn down ("Net scene destroyed" precedes it in the log)
        // by the time OnDestroy runs, so the live ZNet.instance.IsServer()
        // check there always read false and skipped shutdown logging/saves —
        // pre-existing since 1.0 (0.221.12 + Storm10 1.0.12 testbeds, review
        // 2026-09-21). OnDestroy reads this remembered flag instead.
        internal static bool WasServer = false;
        private readonly Harmony _harmony = new Harmony(PluginGUID);

        // ── Config ────────────────────────────────────────────────────────────
        public static ConfigEntry<bool> EnablePlayerDeath;
        public static ConfigEntry<bool> EnableBossKill;
        public static ConfigEntry<bool> EnablePlayerJoin;
        public static ConfigEntry<bool> EnablePlayerLeave;
        public static ConfigEntry<bool> EnableBiomeDiscovery;
        public static ConfigEntry<bool> EnableKillMilestone;
        public static ConfigEntry<bool> EnableDeathMilestone;
        public static ConfigEntry<bool> EnableGearTier;
        public static ConfigEntry<bool> EnableTitleEarned;
        public static ConfigEntry<bool> SyncTitlesToNameplates;
        public static ConfigEntry<bool> AnnounceTitleEarnedInGame;
        public static ConfigEntry<bool> EnableRaid;
        public static ConfigEntry<bool> ShowDayNumber;
        public static ConfigEntry<bool> ShowOnlineCount;
        public static ConfigEntry<string> MessagePrefix;
        public static ConfigEntry<bool> EnableChronicleLog;
        public static ConfigEntry<bool> LogServerStartStop;
        public static ConfigEntry<float> BossCreditRadius;
        public static ConfigEntry<string> DiscordWebhookUrl;
        public static ConfigEntry<bool> EnableNarrativeMode;
        public static ConfigEntry<bool> AppendSeasonNameToMessages;

        // ── World event messages (one per boss, fires once after boss confirmed) ──
        public static ConfigEntry<string> WorldEventEikthyr;
        public static ConfigEntry<string> WorldEventElder;
        public static ConfigEntry<string> WorldEventBonemass;
        public static ConfigEntry<string> WorldEventModer;
        public static ConfigEntry<string> WorldEventYagluth;
        public static ConfigEntry<string> WorldEventQueen;
        public static ConfigEntry<string> WorldEventFader;

        // ── Lore ──────────────────────────────────────────────────────────────
        public static ConfigEntry<int> LoreBroadcastIntervalMinutes;

        // ── Companion (stats/HTTP) ───────────────────────────────────────────
        public static ConfigEntry<bool> EnableHttpServer;
        public static ConfigEntry<int> HttpServerPort;
        public static ConfigEntry<float> StatsPushIntervalSeconds;
        public static ConfigEntry<bool> HttpBindAllInterfaces;
        public static ConfigEntry<string> HttpApiToken;
        public static ConfigEntry<int> EventFeedCapacity;

        // ── Push (hosted dashboard, 1.6.0 — see PushClient.cs / docs/SCOPE-1.6.0.md) ──
        public static ConfigEntry<string> PushUrl;
        public static ConfigEntry<string> PushToken;
        public static ConfigEntry<string> PushServerId;
        public static ConfigEntry<int> PushIntervalSeconds;
        public static ConfigEntry<int> PushHeartbeatMinutes;

        // ── Census ────────────────────────────────────────────────────────────
        public static ConfigEntry<int> CensusIntervalMinutes;

        // ── Combat (client-reported — see CombatReportReceiver) ──────────────
        public static ConfigEntry<bool> AcceptClientReports;
        public static ConfigEntry<bool> LogCombatReports;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            EnablePlayerDeath = Config.Bind("Events", "EnablePlayerDeath", true, "Log/announce when a player dies");
            EnableBossKill = Config.Bind("Events", "EnableBossKill", true, "Log/announce when a boss is killed");
            EnablePlayerJoin = Config.Bind("Events", "EnablePlayerJoin", true, "Log when a player joins");
            EnablePlayerLeave = Config.Bind("Events", "EnablePlayerLeave", true, "Log when a player leaves");
            EnableBiomeDiscovery = Config.Bind("Events", "EnableBiomeDiscovery", true, "Log/announce when a player discovers a new biome");
            EnableKillMilestone = Config.Bind("Events", "EnableKillMilestone", true, "Log/announce enemy kill milestones");
            EnableDeathMilestone = Config.Bind("Events", "EnableDeathMilestone", true, "Log/announce death milestones");
            EnableGearTier = Config.Bind("Events", "EnableGearTier", true, "Log/announce new gear tier reached");
            EnableTitleEarned = Config.Bind("Events", "EnableTitleEarned", true, "Log/announce when a title is earned");
            SyncTitlesToNameplates = Config.Bind("Events", "SyncTitlesToNameplates", true, "Send every player's active title to the game clients, so players running WhereTheCrowFlies 1.3.0+ see each other's active title on a line under the name on the nameplate. Sent when a player joins and whenever an active title changes. Off = nothing is sent and the nameplates show no TheRavensCall title line (other mods' lines, like Ragnarok's Wrath's, are not affected)");
            AnnounceTitleEarnedInGame = Config.Bind("Events", "AnnounceTitleEarnedInGame", true, "Show \"<player> has earned the title: <title>\" in the middle of every connected player's screen when a player earns a title for the first time (picking or clearing a title is not announced). Needs no client mod. Every title gets its line; players who earn the same title together share one line with every name. Lines go out one after another, 4 seconds apart, and after a boss kill they wait until the game's own boss message has faded. At most 10 lines wait (a title past that is not shown on screen) and a line that waited more than 50 seconds is dropped. The Chronicle/Discord line (EnableTitleEarned) is not affected");
            EnableRaid = Config.Bind("Events", "EnableRaid", true, "Record raid start and end in the Chronicle and the dashboard feed (raid_start / raid_end). Not posted to Discord. The raid banner and raid_active work either way.");
            ShowDayNumber = Config.Bind("Format", "ShowDayNumber", true, "Append day number to messages");
            ShowOnlineCount = Config.Bind("Format", "ShowOnlineCount", true, "Append online count to messages");
            MessagePrefix = Config.Bind("Format", "MessagePrefix", "⚔ ", "Prefix for all narrated messages (Discord + Chronicle)");
            EnableChronicleLog = Config.Bind("Log", "EnableChronicleLog", true, "Write TheRavensCall_Chronicle.log");
            LogServerStartStop = Config.Bind("Log", "LogServerStartStop", true, "Log server start/stop events to Chronicle");
            BossCreditRadius = Config.Bind("Bosses", "BossCreditRadius", 100f, "Radius in meters around a boss's death position where players receive kill credit");
            DiscordWebhookUrl = Config.Bind("Discord", "WebhookUrl", "", "Discord webhook URL for event notifications. Leave empty to disable");
            EnableNarrativeMode = Config.Bind("Narrative", "EnableNarrativeMode", true, "Use randomized narrative message templates instead of fixed strings");
            AppendSeasonNameToMessages = Config.Bind("Seasons", "AppendSeasonNameToMessages", false, "Prepend the active season name to all narrated messages");
            WorldEventEikthyr = Config.Bind("WorldEvents", "Eikthyr", "", "Message posted after Eikthyr is defeated. Leave empty to disable");
            WorldEventElder = Config.Bind("WorldEvents", "TheElder", "", "Message posted after The Elder is defeated. Leave empty to disable");
            WorldEventBonemass = Config.Bind("WorldEvents", "Bonemass", "", "Message posted after Bonemass is defeated. Leave empty to disable");
            WorldEventModer = Config.Bind("WorldEvents", "Moder", "", "Message posted after Moder is defeated. Leave empty to disable");
            WorldEventYagluth = Config.Bind("WorldEvents", "Yagluth", "", "Message posted after Yagluth is defeated. Leave empty to disable");
            WorldEventQueen = Config.Bind("WorldEvents", "TheQueen", "", "Message posted after The Queen is defeated. Leave empty to disable");
            WorldEventFader = Config.Bind("WorldEvents", "Fader", "", "Message posted after Fader is defeated. Leave empty to disable");
            LoreBroadcastIntervalMinutes = Config.Bind("Lore", "LoreBroadcastIntervalMinutes", 0, "Post a random lore.txt entry to Discord/Chronicle at this interval in minutes. 0 = disabled");

            EnableHttpServer = Config.Bind("Companion", "EnableHttpServer", true, "Run the local HTTP dashboard/API on this machine (port below)");
            HttpServerPort = Config.Bind("Companion", "HttpServerPort", 2112, "Port for the HTTP dashboard/API (/api/state is exactly the BarrkBOT export, the same JSON written to BarrkBOT_data1.json)");
            StatsPushIntervalSeconds = Config.Bind("Companion", "StatsPushIntervalSeconds", 10f, "How often (seconds) to snapshot every online player's state, check for new biomes/gear tiers, and refresh the BarrkBOT export file");
            HttpBindAllInterfaces = Config.Bind("Companion", "HttpBindAllInterfaces", false, "Also listen on every network interface (http://+:port), not only localhost. Off by default since 1.2.4: the API hands every known player's stats, skills, titles and death coordinates to anyone who can reach the port, with no login. Turn on only behind a firewall or together with HttpApiToken");
            HttpApiToken = Config.Bind("Companion", "HttpApiToken", "", "If set, /api/state, /api/gamedata, /api/pins, /api/activity and /api/census require ?token=<this value> (or an X-Api-Token header). /api/health and the dashboard page stay open. Since 1.3.0 the bundled dashboard page has a settings panel (gear icon) to enter this token itself, stored in the browser and sent as X-Api-Token — a 401 opens that panel automatically. A pre-1.3.0 page still does not send a token. Since 1.8.0 the cross-origin (CORS) headers are only sent while this is set, so a dashboard opened from disk or from another address needs the token; the page served on this port does not. Changing this while the server runs does not update the read-token hash pushed to a hosted receiver — restart the server and re-register read_token_sha256 in the receiver's TRC_SERVERS");
            EventFeedCapacity = Config.Bind("Companion", "EventFeedCapacity", 200, "Number of recent Chronicle lines kept in memory and served by /api/activity, newest first (0 to 1000). They are saved to event_feed.json so the feed survives a restart. 0 turns the feed off; the endpoint still answers, with an empty events array, so a 1.4.0 dashboard can tell \"turned off\" from \"older server\".");

            PushUrl = Config.Bind("Push", "PushUrl", "", "Base URL of a hosted receiver (see docs/API.md) that this server pushes its /api/state, /api/activity and /api/census envelopes to, e.g. https://dash.example.com. Leave empty (the default) to disable pushing entirely — 1.6.0 then behaves exactly like 1.5.0. Must be https, or http only to a loopback host (for local testing against `netlify dev`); anything else disables the push at boot with a warning naming the host.");
            PushToken = Config.Bind("Push", "PushToken", "", "The write token this server pushes with — generate one randomly (e.g. `openssl rand -hex 24`) and register its sha256 as this PushServerId's 'w' in the receiver's TRC_SERVERS. The plaintext goes here and in the Authorization header only; it is never logged. 32+ characters recommended — a shorter one still works but logs a warning at boot.");
            PushServerId = Config.Bind("Push", "PushServerId", "", "The id this server is registered under in the receiver's TRC_SERVERS — must match exactly. 1-32 characters, lowercase a-z, 0-9 and '-' only. No default: pushing stays disabled with a warning at boot until this is set and registered.");
            PushIntervalSeconds = Config.Bind("Push", "PushIntervalSeconds", 60, "How often (seconds) to attempt a push once something has changed. Clamped 15..3600. The push only runs on the poll tick (StatsPushIntervalSeconds above, default 10s), so the effective cadence rounds up to the next multiple of that — 15 here pushes every 20s, and a StatsPushIntervalSeconds of 120 caps every push at 120s no matter what this is set to. The boot log line reports the effective value actually used.");
            PushHeartbeatMinutes = Config.Bind("Push", "PushHeartbeatMinutes", 10, "How often (minutes), at minimum, to push all three envelopes even when nothing changed, so a receiver that lost its copy (or never had one) recovers and 'last reported' keeps meaning the server is alive. Clamped 1..1440. The effective heartbeat is never shorter than the effective push interval above (a heartbeat only lands on a tick the interval already allows) — raised to match with a boot warning when this value is too small for that.");
            PushClient.Init();

            CensusIntervalMinutes = Config.Bind("Census", "CensusIntervalMinutes", 5, "How often (minutes) to walk every object in the loaded world and count portals, beds, wards, ships, carts, chests and crafting stations for /api/census. Clamped to 1..1440. 0 turns the census off; the endpoint still answers 200 with enabled:false instead of 404.");

            AcceptClientReports = Config.Bind("Combat", "AcceptClientReports", true, "Accept RavensCall_CombatReport_V1 RPC reports (kills/deaths/damage/fish) from players running the WhereTheCrowFlies client mod. Every report is verified against the connected-player list before anything is credited");
            LogCombatReports = Config.Bind("Combat", "LogCombatReports", false, "Log every accepted/dropped/rate-limited combat report. Verbose — enable during rollout/verification, then turn back off");

            Log.LogInfo($"{PluginName} {PluginVersion} awakens (server-only).");
            try { _harmony.PatchAll(); Log.LogInfo("[TheRavensCall] All patches applied."); }
            catch (Exception ex) { Log.LogError($"[TheRavensCall] PatchAll failed: {ex.Message}"); }

            // Companion is a plain MonoBehaviour (no [BepInPlugin] of its own),
            // so it only runs once attached here — this is the one and only
            // place it gets instantiated.
            gameObject.AddComponent<Companion>();
        }

        private void OnDestroy()
        {
            // ZNet.instance is already null here on a dedicated server (it's
            // torn down before OnDestroy runs), so this reads the flag
            // Patch_ZNetAwake.Postfix remembered instead of asking ZNet again
            // — see WasServer's own comment. WriteSessionSummary/SaveAll/
            // Chronicle.Write below touch none of ZNet/EnvMan, so nothing
            // here needs its own null guard beyond that.
            if (WasServer)
            {
                SessionTracker.WriteSessionSummary();
                PlayerRegistry.SaveAll();
                if (LogServerStartStop.Value)
                    Chronicle.Write("SERVER", "shutdown", "Server shutting down.", null);
            }
            EventFeed.Save();
            Chronicle.Close();
            _harmony.UnpatchSelf();
        }

        public static bool IsServer() => ZNet.instance != null && ZNet.instance.IsServer();

        // ── The listen-server host (1.8.0), the contract shared with
        // WhereTheCrowFlies 1.3.0: this process is the server, not a
        // dedicated one, and has a player of its own. A single-player world
        // with this mod installed is the same case with nobody else on. The
        // host plays inside the server process, so it has no ZNetPeer; its
        // uid is ZNet.GetUID(), which is also this process's routed-RPC id
        // (both are ZDOMan.GetSessionID(): ZNet.cs:396 and :2163, 1.0.15). It
        // is the machine running the server, so name binding (PlatformBinding)
        // never applies to it and it is never untrusted. A dedicated server
        // has no local player; a pure client is no server. ─────────────────
        internal static bool IsListenHost() =>
            ZNet.instance != null && ZNet.instance.IsServer() && !ZNet.instance.IsDedicated() && Player.m_localPlayer != null;

        // The host as a connected player, in GetConnectedPlayers' shape, when
        // its name is storable. Between a death and the respawn the game has
        // no local player (Game._RequestRespawn destroys it and the next one
        // spawns once the spawn area has loaded, Game.cs:655-668 and
        // :760-770), while a dead peer stays in ZNet's list; so the host
        // stays connected too while its session is open (HostSession), at
        // its reference position and with no player id, like a peer whose
        // character ZDO is gone.
        internal static bool TryGetHostPlayer(out (string name, Vector3 pos, long peerUid, long playerID) host)
        {
            host = default;
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || znet.IsDedicated()) return false;
            var local = Player.m_localPlayer;
            if (local != null)
            {
                string name = local.GetPlayerName();
                if (PlatformBinding.IsStorableName(name))
                {
                    host = (name, local.transform.position, ZNet.GetUID(), local.GetPlayerID());
                    return true;
                }
            }
            return HostSession.TryGetOpen(znet.GetReferencePosition(), out host);
        }

        // ── Narrate: writes to Chronicle only (used for join/leave/world-event/
        // lore/season lines, matching the original design where these were
        // considered too frequent/low-stakes for a Discord post) ──────────────
        public static void Narrate(string message, string eventType, string player = null, string detail = null)
        {
            try
            {
                string full = FormatMessage(message);
                Log.LogInfo($"[TheRavensCall] {full}");
                Chronicle.Write(player ?? "world", eventType, message, BuildContextData(detail));
            }
            catch (Exception ex) { Log.LogWarning($"[TheRavensCall] Narrate failed: {ex.Message}"); }
        }

        // ── Fire a full narrative event: Chronicle + Discord + session tracking.
        // Used for the 9 player-attributable events (death, boss kill, biome,
        // kill/death milestone, gear tier, title earned). ─────────────────────
        public static void FireEvent(string eventType, string playerName, string message, string detail = "")
        {
            try
            {
                string full = FormatMessage(message);
                Log.LogInfo($"[TheRavensCall] {full}");
                Chronicle.Write(playerName ?? "world", eventType, message, BuildContextData(detail));
                SessionTracker.Track(eventType, playerName, detail);
                DiscordWebhook.Send(eventType, message);
            }
            catch (Exception ex) { Log.LogWarning($"[TheRavensCall] FireEvent failed: {ex.Message}"); }
        }

        private static string FormatMessage(string message)
        {
            string prefix = MessagePrefix.Value ?? "⚔ ";
            string season = (AppendSeasonNameToMessages != null && AppendSeasonNameToMessages.Value)
                ? SeasonSystem.GetCurrentSeasonName() : "";
            if (!string.IsNullOrEmpty(season)) prefix = $"[{season}] {prefix}";
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            int online = OnlineCount();
            string dayPart = ShowDayNumber.Value && day > 0 ? $" [Day {day}]" : "";
            string countPart = ShowOnlineCount.Value && online > 0 ? $" ({online} online)" : "";
            return $"{prefix}{message}{dayPart}{countPart}";
        }

        // ── The "(N online)" suffix and the Chronicle/feed row's online count
        // (1.7.1). Through 1.7.0 both read ZNet.GetConnectedPeers().Count,
        // which is a copy of m_peers: it still holds the leaving peer while
        // the leave line is written (the RPC_Disconnect prefix runs before
        // ZNet.Disconnect removes the peer, and at shutdown the peers stay
        // listed while the SendDisconnect loop walks them), and it holds
        // peers still in the handshake (m_uid == 0, e.g. at the password
        // prompt). So "X has left the world" read "(1 online)" in the
        // server log with nobody left. Now: the same connected players
        // /api/state counts (GetConnectedPlayers: ready and named), only
        // those whose session is still open, each name once. FireLeaveFor
        // closes the leaver's session before it narrates, so the leaver is
        // not counted; at a shutdown the leave lines count down N-1, N-2, …
        // and the last has no count. Known limit: a connection that dies
        // without a disconnect (crash, cable) fires no leave hook, so that
        // player still counts until ZRpc's ping timeout drops the peer: 30 s
        // from ZNet.Start, but ZRpc.m_timeout is static and ZPlayFabSocket
        // sets it to 90 s when it accepts a crossplay connection, so on a
        // crossplay server it is 90 s for every peer until the next restart
        // (Storm10, 2026-09-23: dropped 08:26:07, "ZRpc timeout detected"
        // 08:27:36). Counting names once keeps a quick rejoin inside that
        // window from counting them twice (the rejoin gets a new uid). ───
        internal static int OnlineCount()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in GetConnectedPlayers())
                if (Patch_PlayerJoin.HasOpenSession(p.peerUid)) names.Add(p.name);
            return names.Count;
        }

        private static Dictionary<string, string> BuildContextData(string detail)
        {
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            int online = OnlineCount();
            return new Dictionary<string, string>
            {
                { "day",    day.ToString()    },
                { "online", online.ToString() },
                { "detail", detail ?? ""      }
            };
        }

        // ── Resolve which connected Player, if any, dealt a given hit. Only
        // useful when a real Player instance exists (i.e. never on a
        // dedicated server — see the Patch_Damage/Patch_CharacterDeath
        // comments below) since Player.GetAllPlayers() is empty there. ─────
        public static Player ResolveAttacker(HitData hit)
        {
            if (hit == null || hit.m_attacker == ZDOID.None) return null;
            foreach (var p in Player.GetAllPlayers())
                if (p != null && p.GetZDOID() == hit.m_attacker) return p;
            return null;
        }

        // ── The server-side "who's online and where" API. NOT
        // ZNet.GetPlayerList() — its position field reads (0,0,0) for any
        // player who hasn't opted into the minimap's "public position"
        // share toggle. ZDOMan's character ZDOs are synced every physics
        // tick by the owning client regardless of that toggle, so they're
        // both more reliable and fresher. Used for boss-kill proximity
        // credit, biome polling, combat-report name verification, and
        // orphaned-session reconcile. ─────────────────────────────────────
        // playerID: the profile ID Player.SetPlayerID wrote onto the
        // character's own ZDO (ZDOVars.s_playerID) — 0 when the character
        // ZDO isn't resolvable yet. Added in 1.5.0 for WorldCensus, which
        // matches it against ZDO.GetLong(ZDOVars.s_creator) to name builders
        // while they're connected (see WorldCensus.cs).
        // 1.8.0: a listen host's own player comes last, with peerUid = the
        // host uid, which no ZNetPeer has (RPC_PeerInfo turns away a uid
        // ZNet.IsConnected already knows, and that counts the host's own,
        // ZNet.cs:1238 and :1287). Nothing here may look a peerUid up with
        // ZNet.GetPeer; the join table, IsUntrusted and HasOpenSession are
        // keyed by uid alone and take the host's too.
        public static IEnumerable<(string name, Vector3 pos, long peerUid, long playerID)> GetConnectedPlayers()
        {
            if (ZNet.instance == null) yield break;
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || !peer.IsReady() || string.IsNullOrEmpty(peer.m_playerName)) continue;
                // 1.8.0: a name the files cannot hold as sent is never a
                // connected player here, so it can never be credited, named
                // in a report or given a record (see PlatformBinding).
                if (!PlatformBinding.IsStorableName(peer.m_playerName)) continue;
                var zdo = ZDOMan.instance?.GetZDO(peer.m_characterID);
                Vector3 pos = zdo != null ? zdo.GetPosition() : peer.m_refPos;
                long playerID = zdo != null ? zdo.GetLong(ZDOVars.s_playerID, 0L) : 0L;
                yield return (peer.m_playerName, pos, peer.m_uid, playerID);
            }
            if (TryGetHostPlayer(out var host)) yield return host;
        }

        // ── Trust boundary for CombatReportReceiver: a reported attacker
        // name must belong to someone actually connected right now. A
        // modded client can lie about names in the RPC payload; it cannot
        // invent a connected player. ────────────────────────────────────
        public static bool TryVerifyConnectedPlayer(string name, out string verifiedName, out Vector3 position)
        {
            verifiedName = null;
            position = Vector3.zero;
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var p in GetConnectedPlayers())
            {
                if (!string.Equals(p.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                verifiedName = p.name;
                position = p.pos;
                return true;
            }
            return false;
        }

        // ── Second half of that trust boundary: a self-report must name the
        // reporting peer's own character. m_playerName is what the peer sent
        // in PeerInfo (its profile name), the same string Player.GetPlayerName
        // returns on that client, so the two are expected to match exactly
        // modulo case (the registry is case-insensitive too). Since 1.8.0 the
        // peer must also be on the account that name is bound to (see
        // PlatformBinding), since the name alone is whatever the client
        // chose. ─────────────────────────────────────────────────────────────
        public static bool IsSelfReport(ZNetPeer reporter, string reportedName)
        {
            if (reporter == null || string.IsNullOrEmpty(reportedName) || string.IsNullOrEmpty(reporter.m_playerName)) return false;
            // A peer the join marked untrusted stays so for its whole
            // connection, and a report never binds a name (1.8.0).
            return string.Equals(reporter.m_playerName, reportedName, StringComparison.OrdinalIgnoreCase)
                && !Patch_PlayerJoin.IsUntrusted(reporter.m_uid)
                && PlatformBinding.Check(reporter, allowBind: false);
        }

        // The same check for whoever RoutedSenderGuard resolved (1.8.0): a
        // peer exactly as above; the listen host by its own character's name
        // alone (no binding, never untrusted, see IsListenHost).
        internal static bool IsSelfReport(Reporter reporter, string reportedName)
        {
            if (!reporter.IsHost) return IsSelfReport(reporter.Peer, reportedName);
            return !string.IsNullOrEmpty(reportedName) && string.Equals(reporter.Name, reportedName, StringComparison.OrdinalIgnoreCase);
        }

        public static HitData GetLastHit(Character c)
        {
            try { return HarmonyLib.AccessTools.Field(typeof(Character), "m_lastHit")?.GetValue(c) as HitData; }
            catch { return null; }
        }

        // ── Death cause, from a Character's last hit ──────────────────────────
        public static string GetDeathCause(Character victim)
        {
            try
            {
                var lastHit = GetLastHit(victim);
                bool inWater = false;
                try { inWater = victim is Player pv && (pv.IsSwimming() || pv.InWater()); } catch { }
                if (lastHit == null) return inWater ? "drowning" : "a fall";

                if (lastHit.m_attacker != ZDOID.None)
                {
                    var zdo = ZDOMan.instance?.GetZDO(lastHit.m_attacker);
                    if (zdo != null)
                    {
                        var prefab = ZNetScene.instance?.GetPrefab(zdo.GetPrefab());
                        if (prefab != null)
                        {
                            var ch = prefab.GetComponent<Character>();
                            if (ch != null && !string.IsNullOrEmpty(ch.m_name))
                            {
                                string locName = Localization.instance.Localize(ch.m_name);
                                return ch.IsBoss() ? locName : $"a {locName}";
                            }
                            return prefab.name.Replace("(Clone)", "").Trim();
                        }
                    }
                }

                var dmg = lastHit.m_damage;
                if (dmg.m_fire > 0f) return "burning";
                if (dmg.m_frost > 0f) return "freezing";
                if (dmg.m_poison > 0f) return "poison";
                if (dmg.m_spirit > 0f) return "spirit damage";
                if (dmg.m_blunt > 0f || dmg.m_slash > 0f || dmg.m_pierce > 0f)
                    return inWater ? "drowning" : "combat";
                return inWater ? "drowning" : "a fall";
            }
            catch { return "unknown causes"; }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SERVER LIFECYCLE — startup/shutdown, join/leave, periodic poll tick
    // ═════════════════════════════════════════════════════════════════════════

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Awake))]
    public static class Patch_ZNetAwake
    {
        private static void Postfix()
        {
            try
            {
                if (!ZNet.instance.IsServer()) return;
                Plugin.WasServer = true;
                // A new world session: a listen host's next first spawn is
                // its join (1.8.0). First, so nothing below (the state cache
                // prime) sees a host session an earlier one left open.
                HostSession.Reset();
                EventFeed.Init();
                WorldCensus.Init();
                PlayerRegistry.LoadAll();
                PlayerRegistry.CloseSessionsLeftOpen();
                PlatformBinding.Load();
                Companion.PrimeStateCache();
                SessionTracker.Begin();
                // SeasonSystem.Init() re-points the Chronicle at the active
                // season's archive folder when a season is running (§4.6).
                // Only call the bare Chronicle.Init() when that left no
                // writer open — calling it first would eagerly open (and
                // create) the default-folder log file, which SeasonSystem.Init
                // then abandons, leaving a stray 0-byte file behind on every
                // mid-season restart. Keyed on the writer as well as the
                // season name (1.4.1/1.4.2): a season whose re-point failed,
                // or a throw inside SeasonSystem.Init before the re-point,
                // must still end up with the default Chronicle, not with
                // none — and with no season active the default folder must
                // be re-pointed to even when a writer is still open from an
                // earlier world session of the same process (a listen server
                // hosting twice after seasons.json was cleared out of band).
                // Chronicle.Init is a no-op on an unchanged path, so this
                // never creates the stray 0-byte default file.
                SeasonSystem.Init();
                if (string.IsNullOrEmpty(SeasonSystem.GetCurrentSeasonName()) || Chronicle.CurrentLogPath == null) Chronicle.Init();
                LoreSystem.Init();
                // Per-world-session registration (ZRoutedRpc.instance is new
                // every time ZNet.Awake runs) — same lifecycle point every
                // other server system here initializes at.
                ZRoutedRpc.instance.Register<ZPackage>("RavensCall_CombatReport_V1", CombatReportReceiver.Handle);
                ZRoutedRpc.instance.Register<ZPackage>("RavensCall_EventReport_V2", EventReportReceiver.Handle);
                // No on-screen title line waits over from an earlier world
                // session (a listen host hosting twice in one process).
                TitleSync.Reset();
                if (Plugin.LogServerStartStop.Value)
                    Chronicle.Write("SERVER", "startup", "TheRavensCall is listening.", null);
                Plugin.Log.LogInfo("[TheRavensCall] Server systems initialized.");
                // Boot prime for /api/activity — last, so it reflects
                // SeasonSystem.Init's mid-season restore and EventFeed.Init's
                // reloaded rows above rather than the empty envelope.
                Companion.BuildActivityCache();
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] ZNet.Awake error: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
    public static class Patch_PlayerJoin
    {
        private static readonly Dictionary<long, DateTime> JoinTimes = new Dictionary<long, DateTime>();
        private static readonly HashSet<long> _fired = new HashSet<long>();
        // Peers whose name is bound to another account (PlatformBinding,
        // 1.8.0): counted online and narrated, but they never touch the
        // record that name belongs to.
        private static readonly HashSet<long> _untrusted = new HashSet<long>();

        internal static bool GetJoinTime(long uid, out DateTime joinTime) => JoinTimes.TryGetValue(uid, out joinTime);
        // A session is open from the join postfix until FireLeaveFor (or the
        // disconnect that never reached a leave hook, which also takes the
        // peer out of ZNet's list, so Plugin.OnlineCount never sees it).
        internal static bool HasOpenSession(long uid) => JoinTimes.ContainsKey(uid);
        internal static bool IsUntrusted(long uid) => _untrusted.Contains(uid);
        internal static void ClearJoinTime(long uid) { JoinTimes.Remove(uid); _fired.Remove(uid); _untrusted.Remove(uid); }

        private static void Postfix(ZRpc rpc)
        {
            try
            {
                if (!Plugin.IsServer()) return;
                var peer = ZNet.instance?.GetPeers()?.Find(p => p.m_rpc == rpc);
                // RPC_PeerInfo returns early on every rejection (wrong password,
                // version mismatch, banned, server full, bad session ticket)
                // BEFORE it assigns m_uid/m_playerName, and a Harmony postfix
                // still runs. A rejected peer therefore arrives here with
                // m_uid == 0 and an empty name; without this guard every such
                // rejection became a permanent "A Viking" record in the
                // registry and the BarrkBOT export (review 2026-09-15).
                // ZNetPeer.IsReady() is exactly m_uid != 0.
                if (peer == null || !peer.IsReady() || _fired.Contains(peer.m_uid)) return;
                _fired.Add(peer.m_uid);
                JoinTimes[peer.m_uid] = DateTime.UtcNow;

                string name = string.IsNullOrEmpty(peer.m_playerName) ? "A Viking" : peer.m_playerName;
                // A peer under the listen host's own name (1.8.0) is not the
                // host, who plays in this process: it is treated like a name
                // bound to another account, and checked before PlatformBinding
                // so it never binds the host's name to its own account.
                bool hostsName = HostSession.HoldsName(name);
                if (hostsName || !PlatformBinding.Check(peer, allowBind: true))
                {
                    _untrusted.Add(peer.m_uid);
                    if (!PlatformBinding.IsStorableName(name))
                    {
                        // A control character or a broken surrogate: the
                        // files cannot hold the name as sent (see
                        // PlatformBinding), so it gets no record and no line.
                        Plugin.Log.LogWarning($"[TheRavensCall] A player joined with a name containing control or invalid characters ({Companion.Esc(name)}); it is not tracked and its reports are refused.");
                        // Still a connected client: it gets the other
                        // players' titles (it is never an entry itself).
                        TitleSync.SendOnlineTable();
                        return;
                    }
                    Plugin.Log.LogWarning(hostsName
                        ? $"[TheRavensCall] {name} joined under the name of the player hosting this world; its self-reports and title changes are refused and it does not update {name}'s record."
                        : $"[TheRavensCall] {name} joined from a different account than the one this name is bound to, or with no usable account id; its self-reports and title changes are refused and it does not update {name}'s record (ravenscall unbind {name} releases the name).");
                    if (Plugin.EnablePlayerJoin.Value)
                        Plugin.Narrate($"{name} has entered the world.", "player_join", name);
                    TitleSync.SendOnlineTable();
                    return;
                }
                TrustedJoin(peer.m_uid, name);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] PlayerJoin error: {ex.Message}"); }
        }

        // ── The listen host's own join (1.8.0), from HostSession at its
        // character's first spawn in a world session: the trusted path a
        // peer's join takes, with no PlatformBinding (see Plugin.IsListenHost).
        // A peer already connected under the host's name (it joined in the
        // moments before the host's character spawned) is marked untrusted
        // first, as a later one is in Postfix, so it cannot write the host's
        // record or close the session that starts now. ────────────────────
        internal static void HostJoin(long uid, string name)
        {
            if (_fired.Contains(uid)) return;
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || !peer.IsReady() || _untrusted.Contains(peer.m_uid)) continue;
                if (!string.Equals(peer.m_playerName, name, StringComparison.OrdinalIgnoreCase)) continue;
                _untrusted.Add(peer.m_uid);
                Plugin.Log.LogWarning($"[TheRavensCall] {peer.m_playerName} is connected under the name of the player hosting this world; its self-reports and title changes are refused from now on and it does not update {name}'s record.");
            }
            _fired.Add(uid);
            JoinTimes[uid] = DateTime.UtcNow;
            TrustedJoin(uid, name);
        }

        // Shared by a trusted peer's join and the host's: the record, the
        // quick rejoin, the join line and the title table, keyed by uid.
        private static void TrustedJoin(long joinUid, string name)
        {
            var rec = PlayerRegistry.Get(name);

            // 1.8.0: a quick rejoin. When a game crashes or a connection
            // drops, the old peer stays connected until ZRpc's timeout
            // (90 s on a crossplay server), and 1.0.15 then removes it
            // without any leave hook. A rejoin inside that window used to
            // overwrite SessionStart, so the whole earlier session never
            // reached playtime_seconds_lifetime and got no leave line
            // (review 2026-09-24). Close it here instead, and retire the
            // earlier peers' join times under this name (and any whose
            // peer is already gone), so a late leave hook for them cannot
            // close the session that starts now. The listen host's open
            // session has no peer to find and is never retired here.
            bool closedEarlier = false;
            if (rec.Online && !string.IsNullOrEmpty(rec.SessionStart) &&
                DateTime.TryParse(rec.SessionStart, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime earlierStart))
            {
                rec.PlaytimeSecondsLifetime += Math.Max(0, (long)(DateTime.UtcNow - earlierStart.ToUniversalTime()).TotalSeconds);
                closedEarlier = true;
            }
            foreach (long uid in JoinTimes.Keys.ToList())
            {
                if (uid == joinUid || _untrusted.Contains(uid) || HostSession.IsOpen(uid)) continue;
                var other = ZNet.instance.GetPeer(uid);
                if (other == null || string.Equals(other.m_playerName, name, StringComparison.OrdinalIgnoreCase))
                { JoinTimes.Remove(uid); _fired.Remove(uid); }
            }
            if (closedEarlier && Plugin.EnablePlayerLeave.Value)
                Plugin.Narrate($"{name} has left the world (connection lost).", "player_leave", name, "(connection lost)");

            rec.Online = true;
            rec.SessionStart = DateTime.UtcNow.ToString("o");
            rec.LastSeen = rec.SessionStart;
            // Session-scoped counters reset on every (re)join — without
            // this they silently accumulate for the server's whole
            // uptime instead of resetting per session like the name implies.
            rec.SessionKills = 0;
            rec.SessionDmgDone = 0f;
            rec.SessionDmgTaken = 0f;
            rec.Dirty = true;

            if (Plugin.EnablePlayerJoin.Value)
                Plugin.Narrate($"{name} has entered the world.", "player_join", name);
            // Every client, the joining one included, gets the whole
            // online table: RPC_PeerInfo adds the new peer to
            // ZRoutedRpc's peer list as its last step
            // (m_routedRpc.AddPeer, 1.0.15), before the join postfix runs.
            // The host's own client gets it in this process (Everybody
            // runs locally too).
            TitleSync.SendOnlineTable();
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_Disconnect))]
    public static class Patch_PlayerLeave
    {
        internal static void FireLeave(ZRpc rpc)
        {
            try
            {
                if (!Plugin.IsServer()) return;
                var peer = ZNet.instance?.GetPeers()?.Find(p => p.m_rpc == rpc);
                if (peer == null) return;
                FireLeaveFor(peer.m_uid, peer.m_playerName);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] PlayerLeave error: {ex.Message}"); }
        }

        internal static void FireLeaveFor(long uid, string name)
        {
            if (string.IsNullOrEmpty(name)) name = "A Viking";

            // ZNet.RPC_Disconnect and ZNet.SendDisconnect can both fire for the
            // same disconnect — GetJoinTime only succeeds on whichever gets here
            // first, so this whole block (including the narration) runs exactly
            // once per real disconnect rather than twice.
            if (!Patch_PlayerJoin.GetJoinTime(uid, out DateTime joinTime)) return;
            bool untrusted = Patch_PlayerJoin.IsUntrusted(uid);
            Patch_PlayerJoin.ClearJoinTime(uid);

            var span = DateTime.UtcNow - joinTime;
            string duration = span.TotalHours >= 1 ? $" (played {(int)span.TotalHours}h {span.Minutes}m)" : $" (played {span.Minutes}m)";

            // A peer on another account than its name's (PlatformBinding):
            // the leave line only; the record belongs to someone else.
            if (untrusted)
            {
                if (Plugin.EnablePlayerLeave.Value && PlatformBinding.IsStorableName(name))
                    Plugin.Narrate($"{name} has left the world{duration}.", "player_leave", name, duration.Trim());
                return;
            }

            var rec = PlayerRegistry.Get(name);
            rec.Online = false;
            rec.SessionStart = null;
            rec.LastSeen = DateTime.UtcNow.ToString("o");
            rec.PlaytimeSecondsLifetime += Math.Max(0, (long)span.TotalSeconds);
            rec.Dirty = true;
            PlayerRegistry.Save(rec);

            if (Plugin.EnablePlayerLeave.Value)
                Plugin.Narrate($"{name} has left the world{duration}.", "player_leave", name, duration.Trim());
        }

        private static void Prefix(ZRpc rpc) => FireLeave(rpc);
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.SendDisconnect), new[] { typeof(ZNetPeer) })]
    public static class Patch_PlayerLeave_Send
    {
        private static void Prefix(ZNetPeer peer)
        {
            try { if (Plugin.IsServer() && peer?.m_rpc != null) Patch_PlayerLeave.FireLeave(peer.m_rpc); } catch { }
        }
    }

    // ── A listen host's own session (1.8.0). The host plays inside the
    // server process and is no ZNetPeer, so RPC_PeerInfo and RPC_Disconnect
    // never run for it. Its session opens when its own character first
    // spawns in a world session and closes when it leaves the world, through
    // the same join and leave code a peer's goes through, keyed by the host
    // uid (ZNet.GetUID()). Everything here is gated on Plugin.IsListenHost
    // or on a host session being open, so a dedicated server never gets
    // one. ───────────────────────────────────────────────────────────────
    internal static class HostSession
    {
        // This world session's first spawn has been seen (a respawn after a
        // death is not a join).
        private static bool _spawned;
        // The open host session: its uid (0 = none) and the name it opened
        // under, kept for the leave.
        private static long _uid;
        private static string _name;
        // The host's own WhereTheCrowFlies has reported this world session
        // (see CrowReportsDamage).
        private static bool _crowReports;

        // A new world session (Patch_ZNetAwake). A host session the last one
        // never closed is dropped from the join table; its record is closed
        // by PlayerRegistry.CloseSessionsLeftOpen right after, like any
        // session left open.
        internal static void Reset()
        {
            if (_uid != 0L) Patch_PlayerJoin.ClearJoinTime(_uid);
            _spawned = false;
            _uid = 0L;
            _name = null;
            _crowReports = false;
        }

        internal static bool IsOpen(long uid) => _uid != 0L && uid == _uid;

        // A remote peer under this name is not the host (Patch_PlayerJoin).
        internal static bool HoldsName(string name) =>
            _uid != 0L && string.Equals(_name, name, StringComparison.OrdinalIgnoreCase);

        // Plugin.TryGetHostPlayer's stand-in while the host has no local
        // player (between a death and the respawn).
        internal static bool TryGetOpen(Vector3 refPos, out (string name, Vector3 pos, long peerUid, long playerID) host)
        {
            host = default;
            if (_uid == 0L) return false;
            host = (_name, refPos, _uid, 0L);
            return true;
        }

        internal static void OnSpawned(Player player)
        {
            try
            {
                if (_spawned || player == null || player != Player.m_localPlayer || !Plugin.IsListenHost()) return;
                _spawned = true;
                string name = player.GetPlayerName();
                if (!PlatformBinding.IsStorableName(name))
                {
                    Plugin.Log.LogWarning($"[TheRavensCall] The player hosting this world has a name containing control or invalid characters ({Companion.Esc(name)}); it is not tracked and its reports are refused.");
                    return;
                }
                _uid = ZNet.GetUID();
                _name = name;
                Patch_PlayerJoin.HostJoin(_uid, _name);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Host join error: {ex.Message}"); }
        }

        // Once, whichever exit runs: the open session is taken before the
        // leave fires, and FireLeaveFor's own join-time check backs it up.
        internal static void Leave()
        {
            try
            {
                if (_uid == 0L) return;
                long uid = _uid;
                _uid = 0L;
                Patch_PlayerLeave.FireLeaveFor(uid, _name);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Host leave error: {ex.Message}"); }
        }

        internal static void NoteCrowReport() => _crowReports = true;

        // Patch_Damage runs in this process for every hit this process
        // produces, so on a listen host it sees the host's own swings and
        // every hit the host's character takes from something this process
        // simulates. The host's WhereTheCrowFlies (1.3.0+) reports that same
        // damage in its batches, so once it has reported this world session
        // (its first report is the snapshot it sends at spawn) the host's
        // damage comes from the batches alone. Without it the patch stays
        // the host's only source, as before 1.8.0. Every other player's
        // damage is left to the patch as before: WhereTheCrowFlies leaves
        // out the hits a server process produced (its FromServerProcess).
        internal static bool CrowReportsDamage(Player p) =>
            _crowReports && p != null && p == Player.m_localPlayer;
    }

    // Game.SpawnPlayer (private) is the only caller of Player.OnSpawned, and
    // calls it after SetLocalPlayer and LoadPlayerData (Game.cs:493-500), so
    // the local player and its name are set. It runs again for every respawn
    // after a death (UpdateRespawn, Game.cs:760-770), hence HostSession's
    // once-per-world-session flag. A dedicated server has no local player
    // and a pure client is no server, so neither passes Plugin.IsListenHost.
    [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
    public static class Patch_HostJoin
    {
        private static void Postfix(Player __instance) => HostSession.OnSpawned(__instance);
    }

    // Game.Shutdown (private) is where the host's two ways out of the world
    // meet: logout to the main menu (Game.Logout -> ContinueLogout ->
    // Shutdown, Game.cs:355-379) and quit to desktop (Menu.QuitGame ->
    // Application.Quit -> Game.OnApplicationQuit -> Shutdown, Menu.cs:527-531
    // and Game.cs:398-404). A prefix runs before ZNetScene.Shutdown resets
    // every ZNetView (the host's character included) and before
    // ZNet.Shutdown -> StopAll sends every peer its disconnect
    // (Game.cs:420-421, ZNet.cs:661-697), and on a quit before
    // Plugin.OnDestroy writes the session summary (Unity sends
    // OnApplicationQuit before it destroys anything). So the host's leave
    // line comes first, then each peer's from the SendDisconnect loop.
    [HarmonyPatch(typeof(Game), nameof(Game.Shutdown))]
    public static class Patch_HostLeave
    {
        private static void Prefix() => HostSession.Leave();
    }


    // ── Periodic server tick: biome/gear-tier polling, per-player state
    // snapshots (for the dashboard), and the BarrkBOT export refresh. Driven
    // from the plugin's own Update(), NOT Player.Update/IsOwner — those never
    // fire for anyone on a headless server since no player is ever "owned"
    // by the server itself. ───────────────────────────────────────────────
    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Update))]
    public static class Patch_ZNetUpdate
    {
        private static float _timer = 0f;

        private static void Postfix()
        {
            if (!Plugin.IsServer()) return;

            try { LoreSystem.Tick(Time.deltaTime); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Lore tick error: {ex.Message}"); }

            // Every frame, not on the poll timer: the on-screen title lines
            // wait here only for the 4 s gap between them, or for a boss's
            // own death line to fade (TitleSync).
            TitleSync.Tick();

            _timer += Time.deltaTime;
            if (_timer < Mathf.Max(1f, Plugin.StatsPushIntervalSeconds.Value)) return;
            _timer = 0f;
            try { SessionReconcile.CloseOrphanedSessions(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Session reconcile error: {ex.Message}"); }
            try { Companion.PollAllPlayers(); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Poll tick error: {ex.Message}"); }
        }
    }

    // ── RPC_Disconnect/SendDisconnect only fire for a clean or timed-out-
    // but-observed leave. A hard crash/power-loss/alt-F4 drops the
    // connection without either ever firing, leaving the registry's Online
    // flag — and an open session — stale forever (this is why join/leave
    // counts drift apart over a day of play). The poll tick already knows
    // the true connected-player list, so it's the natural place to catch
    // and close sessions the leave patches never saw. Reads rec.SessionStart
    // directly rather than Patch_PlayerJoin's in-memory JoinTimes, since
    // after a crash there's no ZNetPeer/uid left to look that dictionary up
    // by — the persisted record is the only surviving source of join time. ──
    public static class SessionReconcile
    {
        public static void CloseOrphanedSessions()
        {
            // A peer on another account than its name's does not keep that
            // name's record open (PlatformBinding, 1.8.0).
            var connected = new HashSet<string>(
                Plugin.GetConnectedPlayers().Where(p => !Patch_PlayerJoin.IsUntrusted(p.peerUid)).Select(p => p.name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var rec in PlayerRegistry.All.ToList())
            {
                if (!rec.Online || connected.Contains(rec.Name)) continue;

                if (!string.IsNullOrEmpty(rec.SessionStart) &&
                    DateTime.TryParse(rec.SessionStart, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime joinTime))
                {
                    var span = DateTime.UtcNow - joinTime;
                    rec.PlaytimeSecondsLifetime += Math.Max(0, (long)span.TotalSeconds);
                }

                rec.Online = false;
                rec.SessionStart = null;
                rec.LastSeen = DateTime.UtcNow.ToString("o");
                rec.Dirty = true;
                PlayerRegistry.Save(rec);

                if (Plugin.EnablePlayerLeave.Value)
                    Plugin.Narrate($"{rec.Name} has left the world (connection lost).", "player_leave", rec.Name, "(connection lost)");
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // KILLS / DEATHS / DAMAGE — server-authoritative attribution
    // ═════════════════════════════════════════════════════════════════════════

    // ── Dead on a dedicated server (Character.Damage is owner-side
    // simulation — see the Architecture note up top) but kept as insurance
    // for any future/alternate topology where it does fire. Delegates to
    // CombatCredit so it can never disagree with the live RPC path below.
    // It does fire on a listen host, for every hit that process produces;
    // there the host's own damage comes from its WhereTheCrowFlies batches
    // instead once they arrive (HostSession.CrowReportsDamage), since
    // CreditDamage has no dedup to collapse the two. ──────────────────
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    public static class Patch_Damage
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            try
            {
                if (!Plugin.IsServer() || hit == null) return;
                var attacker = Plugin.ResolveAttacker(hit);
                if (attacker != null && __instance != attacker && !HostSession.CrowReportsDamage(attacker))
                    CombatCredit.CreditDamage(attacker.GetPlayerName(), hit.GetTotalDamage(), 0f);
                if (__instance is Player victim && !HostSession.CrowReportsDamage(victim))
                    CombatCredit.CreditDamage(victim.GetPlayerName(), 0f, hit.GetTotalDamage());
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Damage tracking error: {ex.Message}"); }
        }
    }

    // ── Also dead on a dedicated server, also kept as insurance — see
    // Patch_Damage above. The non-boss/boss-kill branches delegate to
    // CombatCredit; player-death keeps its own body since it's the one
    // branch with a genuinely richer server-side path available to it (a
    // real Player instance — inventory, swim state) that the RPC path,
    // which only carries a name and a client-computed cause string,
    // can't reproduce. See CombatCredit.CreditPlayerDeath for that path. ──
    // Character.OnDeath is virtual and Player overrides it WITHOUT calling
    // base (Player.OnDeath in the 1.0.12 decompile), so this patch only ever
    // runs for creatures. The player branch that used to live in this Prefix
    // could never fire; player deaths get their own target, Patch_PlayerDeath
    // below (review 2026-09-15).
    [HarmonyPatch(typeof(Character), "OnDeath")]
    public static class Patch_CharacterDeath
    {
        // Character.OnDeath can re-enter for the same instance (the original
        // SteveCompanionMod needed a 5-second name+timestamp dedupe for player
        // deaths specifically for this reason). One guard here covers every
        // death path uniformly instead of only the boss-kill path having one.
        private static readonly Dictionary<int, float> _recentDeaths = new Dictionary<int, float>();
        private const float DEATH_DEDUP_WINDOW = 3f;

        internal static bool AlreadyProcessed(Character c)
        {
            float now = Time.realtimeSinceStartup;
            foreach (var key in new List<int>(_recentDeaths.Keys))
                if (now - _recentDeaths[key] > DEATH_DEDUP_WINDOW) _recentDeaths.Remove(key);

            int id = c.GetInstanceID();
            if (_recentDeaths.ContainsKey(id)) return true;
            _recentDeaths[id] = now;
            return false;
        }

        private static void Prefix(Character __instance)
        {
            try
            {
                if (!Plugin.IsServer()) return;
                if (__instance is Player) return; // Patch_PlayerDeath (unreachable here anyway, see the class comment)
                if (AlreadyProcessed(__instance)) return;

                string prefab = __instance.gameObject.name.Replace("(Clone)", "").Trim();
                var hit = Plugin.GetLastHit(__instance);
                Player killer = Plugin.ResolveAttacker(hit);

                // Non-boss kill — credit only when a real player dealt the blow
                // (environmental/creature-on-creature deaths are not attributed).
                // CombatCredit.CreditCreatureKill itself routes to boss crediting
                // when the prefab is a boss, so no separate branch is needed here.
                if (killer == null) return;
                CombatCredit.CreditCreatureKill(killer.GetPlayerName(), prefab, __instance.transform.position);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] CharacterDeath error: {ex.Message}"); }
        }

        internal static void HandlePlayerDeath(Player victim)
        {
            string name = victim.GetPlayerName();
            // Shares CombatEventDedup with CombatCredit.CreditPlayerDeath so a
            // listen host that also runs WhereTheCrowFlies cannot credit the
            // same death twice (once here, once from its own RPC report).
            if (CombatEventDedup.AlreadyProcessed("death", name, "", victim.transform.position)) return;
            string cause = Plugin.GetDeathCause(victim);
            var rec = PlayerRegistry.Get(name);

            rec.Deaths++;
            rec.TotalDeathsLifetime++;
            rec.Dirty = true;
            Companion.RecordDeathHistory(rec, victim);
            // Dirty only; SaveDirty flushes it (review 2026-09-15).

            string title = rec.ActiveTitle;
            string displayName = string.IsNullOrEmpty(title) ? name : $"{name} the {title}";
            string causeFriendly = cause == "a fall" ? "falling" : cause;
            string msg = NarrativeSystem.Get("player_death", new Dictionary<string, string>
            {
                { "name", displayName }, { "cause", causeFriendly },
            }, $"{displayName} has fallen to {cause}.");

            MilestoneTracker.OnDeath(name, rec);
            if (Plugin.EnablePlayerDeath.Value) Plugin.FireEvent("player_death", name, msg, cause);
        }
    }

    // ── The player half of the death patch. Same dormant-on-a-dedicated-
    // server status as Patch_CharacterDeath (no Player instance ever exists
    // headless), kept as the same insurance; on any topology where it does
    // fire, Player.OnDeath is the method that actually runs for a player. ──
    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    public static class Patch_PlayerDeath
    {
        private static void Prefix(Player __instance)
        {
            try
            {
                if (!Plugin.IsServer() || __instance == null) return;
                // Player.OnDeath is invoked on every client's copy of the
                // player and gates on ownership inside; a prefix runs before
                // that gate, so match it here (the client twin does the same).
                if (!__instance.IsOwner()) return;
                if (Patch_CharacterDeath.AlreadyProcessed(__instance)) return;
                Patch_CharacterDeath.HandlePlayerDeath(__instance);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] PlayerDeath error: {ex.Message}"); }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // COMBAT CREDIT — shared by the dormant Character.Damage/OnDeath/
    // FishingFloat patches above and CombatReportReceiver below. Every entry
    // point here is name+position based so neither caller needs a Player
    // instance, which the dedicated server never has. Today the RPC receiver
    // is the only caller that actually fires; the patches stay wired to the
    // same functions so the two paths can never compute a kill differently.
    // ═════════════════════════════════════════════════════════════════════════
    // ── Which client-sent names are real (1.8.0). A kill or fish report
    // names its victim as a string, and that string becomes a permanent key
    // in the named player's record (creature_kills, caught_fish), in the
    // BarrkBOT export and in every push. Unchecked, one client sending
    // random names could grow any online player's record by MBs an hour
    // (review 2026-09-24). A creature must be a ZNetScene prefab with a
    // Character; a fish is matched against every ZNetScene prefab with a
    // Fish component, by prefab name or by its item's name token (the
    // client sends m_shared.m_name, e.g. "$animal_perch"). ZNetScene is
    // always up on a running server; without one the check stands aside,
    // as before 1.8.0. ──────────────────────────────────────────────────
    internal static class KnownPrefabs
    {
        private static ZNetScene _fishScene;
        private static HashSet<string> _fish;

        public static bool IsCreature(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            var scene = ZNetScene.instance;
            if (scene == null) return true;
            var go = scene.GetPrefab(prefab);
            return go != null && string.Equals(go.name, prefab, StringComparison.Ordinal) && go.GetComponent<Character>() != null;
        }

        public static bool IsFish(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return false;
            var scene = ZNetScene.instance;
            if (scene == null) return true;
            if (_fish == null || _fishScene != scene)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var go in scene.m_prefabs)
                {
                    if (go == null || go.GetComponent<Fish>() == null) continue;
                    set.Add(go.name);
                    string token = go.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name;
                    if (!string.IsNullOrEmpty(token)) set.Add(token);
                }
                _fish = set;
                _fishScene = scene;
            }
            return _fish.Contains(slug);
        }
    }

    public static class CombatCredit
    {
        private const float BossDedupBucketMeters = 64f;

        // Wire floats are attacker-controlled. NaN/Infinity assigned here
        // would be written into the export as bare NaN/Infinity tokens (not
        // JSON) and parsed straight back in on the next boot (review
        // 2026-09-15). Every apply loop below skips non-finite values and
        // clamps the rest.
        private const float MaxStatMagnitude = 1e9f;
        private const float MaxSkillLevel = 1000f;   // vanilla caps at 100; tolerant of skill-cap mods
        private const float MaxSkillProgress = 100f; // vanilla sends a 0..1 fraction; tolerant of a percent
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // Distinct-key caps for the two maps whose keys a client picks and no
        // prefab list can check (1.8.0): harvest names are localized hover
        // names, and skill ids include mods' own skills. Far above what real
        // play reaches; a new key past the cap is ignored, known keys still
        // update.
        private const int MaxHarvestKeys = 512;
        private const int MaxSkillKeys = 256;

        public static void CreditCreatureKill(string name, string prefab, Vector3 position)
        {
            // The prefab name comes off the wire and becomes a permanent
            // creature_kills key, so only a real creature counts (1.8.0).
            if (!KnownPrefabs.IsCreature(prefab))
            {
                if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] kill report dropped — '{prefab}' is not a creature prefab");
                return;
            }
            if (TitleSystem.BossTitles.ContainsKey(prefab)) { CreditBossKill(prefab, position, name); return; }
            if (CombatEventDedup.AlreadyProcessed("kill", name, prefab, position)) return;

            var rec = PlayerRegistry.Get(name);
            rec.TotalKillsLifetime++;
            rec.Kills++;
            rec.SessionKills++;
            rec.Dirty = true;
            MilestoneTracker.OnKill(name, rec);
            TitleSystem.OnCreatureKill(name, prefab, rec);
            // Flushed by the poll tick's SaveDirty like every V2 credit path;
            // a synchronous save per accepted kill report was a disk-write
            // amplifier for a forging client (review 2026-09-15).
        }

        public static void CreditBossKill(string prefab, Vector3 position, string killerName)
        {
            // Keyed without the attacker name: credit fans out to everyone in
            // radius regardless of who's named as "the" killer, and the two
            // call paths (dormant patch vs. RPC report) can resolve that name
            // differently for the same physical death — the dedup should
            // still catch it as one event.
            // 64 m buckets, not the default 4 m: position comes off the wire, so
            // a 4 m bucket let a forging client mint a fresh key with a few
            // metres of jitter per packet (review 2026-09-15). A real boss
            // cannot die twice within 3 s and 64 m.
            if (CombatEventDedup.AlreadyProcessed("boss", "", prefab, position, BossDedupBucketMeters)) return;
            // Before any title of this kill is earned: its on-screen lines
            // wait until the game's own boss death line has faded.
            TitleSync.HoldForBossMessage();

            string bossDisplayName = TitleSystem.GetBossDisplayName(prefab);
            string bossKey = BossKeys.Resolve(prefab);
            float radius = Mathf.Clamp(Plugin.BossCreditRadius.Value, 5f, 500f);

            string namedKiller = null;
            foreach (var p in Plugin.GetConnectedPlayers())
            {
                if (Vector3.Distance(p.pos, position) > radius) continue;
                if (Patch_PlayerJoin.IsUntrusted(p.peerUid)) continue; // PlatformBinding, 1.8.0

                var rec = PlayerRegistry.Get(p.name);
                rec.BossKillsCredited++;
                if (bossKey != null) rec.DefeatedBosses.Add(bossKey);
                rec.Dirty = true;
                TitleSystem.OnBossKill(p.name, prefab, rec);
                // Dirty only; SaveDirty flushes it (see CreditCreatureKill).
                if (namedKiller == null) namedKiller = p.name;
            }

            string attributedName = !string.IsNullOrEmpty(killerName) ? killerName : namedKiller;
            if (attributedName != null && Plugin.EnableBossKill.Value)
            {
                string pDisplay = TitleSystem.GetDisplayName(attributedName);
                string bossMsg = NarrativeSystem.Get("boss_kill", new Dictionary<string, string>
                {
                    { "name", pDisplay }, { "boss", bossDisplayName },
                }, $"{pDisplay} helped defeat {bossDisplayName}!");
                Plugin.FireEvent("boss_kill", attributedName, bossMsg, prefab);
            }

            LoreSystem.FireWorldEvent(prefab);
        }

        // ── The RPC-report path for a player death: only a name, position,
        // and a client-computed cause string are available (no Player
        // instance to read inventory/swim-state from, unlike
        // Patch_CharacterDeath.HandlePlayerDeath above) — so death_history
        // entries via this path carry no item snapshot. See
        // Companion.RecordDeathHistoryFromReport. ──────────────────────────
        public static void CreditPlayerDeath(string name, Vector3 position, string cause)
        {
            if (CombatEventDedup.AlreadyProcessed("death", name, "", position)) return;
            if (string.IsNullOrEmpty(cause)) cause = "unknown causes";

            var rec = PlayerRegistry.Get(name);
            rec.Deaths++;
            rec.TotalDeathsLifetime++;
            rec.Dirty = true;
            Companion.RecordDeathHistoryFromReport(rec, cause, position);
            // Dirty only; SaveDirty flushes it. A synchronous save per accepted
            // death report was a disk-write amplifier (review 2026-09-15).

            string title = rec.ActiveTitle;
            string displayName = string.IsNullOrEmpty(title) ? name : $"{name} the {title}";
            string causeFriendly = (cause == "a fall" || cause == "fall") ? "falling" : cause;
            string msg = NarrativeSystem.Get("player_death", new Dictionary<string, string>
            {
                { "name", displayName }, { "cause", causeFriendly },
            }, $"{displayName} has fallen to {causeFriendly}.");

            MilestoneTracker.OnDeath(name, rec);
            if (Plugin.EnablePlayerDeath.Value) Plugin.FireEvent("player_death", name, msg, cause);
        }

        // No dedup here: a damage report is a 10-second accumulated batch,
        // not a single discrete event, so the eventType|attacker|prefab|
        // position dedup shape (built for one-off kills/deaths/catches)
        // doesn't apply — every batch is new information by construction.
        public static void CreditDamage(string name, float dmgDealt, float dmgTaken)
        {
            var rec = PlayerRegistry.Get(name);
            if (dmgDealt > 0f) rec.SessionDmgDone += dmgDealt;
            if (dmgTaken > 0f) rec.SessionDmgTaken += dmgTaken;
            if (dmgDealt > 0f || dmgTaken > 0f) rec.Dirty = true;
        }

        public static void CreditFish(string name, string fishSlug)
        {
            if (string.IsNullOrEmpty(fishSlug)) return;
            // Same rule as creature kills: caught_fish only takes a real fish.
            if (!KnownPrefabs.IsFish(fishSlug))
            {
                if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] fish report dropped — '{fishSlug}' is not a fish");
                return;
            }
            if (CombatEventDedup.AlreadyProcessed("fish", name, fishSlug, Vector3.zero)) return;

            var rec = PlayerRegistry.Get(name);
            rec.CaughtFish.Add(fishSlug.ToLowerInvariant());
            rec.Dirty = true;
            // Dirty only; SaveDirty flushes it (review 2026-09-15).
        }

        // ── V2-only credit paths below. Same batched-telemetry philosophy as
        // CreditDamage above: these mark Dirty and let the poll tick's
        // SaveDirty() flush them rather than saving on every call. ─────────

        public static void CreditBlockDefense(string name, int blocks, int parries, float dmgBlocked)
        {
            if (blocks <= 0 && parries <= 0 && dmgBlocked <= 0f) return;
            var rec = PlayerRegistry.Get(name);
            rec.SessionBlocks += blocks;
            rec.SessionParries += parries;
            rec.SessionDmgBlocked += dmgBlocked;
            rec.Dirty = true;
        }

        public static void CreditBuilding(string name, byte action)
        {
            var rec = PlayerRegistry.Get(name);
            switch (action)
            {
                case 1: rec.BuildsPlaced++; break;
                case 2: rec.BuildsRemoved++; break;
                case 3: rec.BuildsRepaired++; break;
                default: return;
            }
            rec.Dirty = true;
        }

        public static void CreditCrafting(string name, byte action, int amount)
        {
            var rec = PlayerRegistry.Get(name);
            int qty = Mathf.Max(1, amount);
            switch (action)
            {
                case 1: rec.ItemsCrafted += qty; break;
                case 2: rec.ItemsUpgraded += qty; break;
                case 3: rec.ItemsRepaired += qty; break;
                default: return;
            }
            rec.Dirty = true;
        }

        public static void CreditHarvest(string name, string resourceName, int amount)
        {
            if (string.IsNullOrEmpty(resourceName) || amount <= 0) return;
            var rec = PlayerRegistry.Get(name);
            // The client sends the pickable's hover name (localized), so there
            // is no prefab to check it against; cap the distinct names instead
            // so one client cannot grow a record without bound (1.8.0).
            if (!rec.ResourcesHarvested.TryGetValue(resourceName, out int cur) && rec.ResourcesHarvested.Count >= MaxHarvestKeys) return;
            rec.ResourcesHarvested[resourceName] = cur + amount;
            rec.Dirty = true;
        }

        public static void CreditConsumable(string name)
        {
            var rec = PlayerRegistry.Get(name);
            rec.ConsumablesEaten++;
            rec.Dirty = true;
        }

        public static void CreditWorldEvent(string name, string eventName)
        {
            var rec = PlayerRegistry.Get(name);
            // PortalTraversed is already covered by VanillaStats["PortalsUsed"]
            // (StatSync) with an exact vanilla count — only track the two
            // event types here that have no PlayerStatType equivalent.
            if (eventName == "BossSummoned") rec.BossesSummoned++;
            else if (eventName == "GuardianPowerUsed") rec.GuardianPowersUsed++;
            else return;
            rec.Dirty = true;
        }

        // Absolute snapshot, not delta — see the wire-protocol note atop
        // EventReportReceiver.HandleStatSnapshot. Every sync overwrites, so
        // a player's true lifetime totals (including whatever they had
        // before this mod was installed) land the moment their client sends
        // one, and a dropped packet can never cause permanent drift. This is
        // the periodic correction anchor for ApplyVanillaStatDeltas below —
        // whatever the delta stream drifted to between snapshots, this call
        // always wins the very next time it fires.
        public static void ApplyVanillaStats(string name, Dictionary<string, float> stats)
        {
            if (stats == null || stats.Count == 0) return;
            var rec = PlayerRegistry.Get(name);
            foreach (var kv in stats)
            {
                if (!Finite(kv.Value)) continue;
                rec.VanillaStats[kv.Key] = Mathf.Clamp(kv.Value, -MaxStatMagnitude, MaxStatMagnitude);
            }
            rec.Dirty = true;
        }

        // Incremental — the StatSnapshot (event 11) cadence is minutes, not
        // seconds (FullSyncIntervalSeconds on the WhereTheCrowFlies side,
        // default 5 minutes), so a pure snapshot-only design would leave
        // VanillaStats stale mid-session. StatSync (event 10, still every
        // 10s) fills that gap by accumulating deltas on top of whatever the
        // last snapshot set. Safe to combine with ApplyVanillaStats above:
        // a snapshot always overwrites outright, so any drift a lost delta
        // packet causes is corrected at the very next snapshot regardless.
        public static void ApplyVanillaStatDeltas(string name, Dictionary<string, float> deltas)
        {
            if (deltas == null || deltas.Count == 0) return;
            var rec = PlayerRegistry.Get(name);
            foreach (var kv in deltas)
            {
                if (!Finite(kv.Value)) continue;
                rec.VanillaStats.TryGetValue(kv.Key, out float cur);
                rec.VanillaStats[kv.Key] = Mathf.Clamp(cur + kv.Value, -MaxStatMagnitude, MaxStatMagnitude);
            }
            rec.Dirty = true;
        }

        // Same absolute-snapshot rule as ApplyVanillaStats — a skill absent
        // from levels/progress this call simply keeps whatever value it had
        // from the last sync (the sender only includes skills the player
        // has actually raised, per EventReportReceiver.HandleSkillSnapshot).
        public static void ApplySkillLevels(string name, Dictionary<string, float> levels, Dictionary<string, float> progress)
        {
            if ((levels == null || levels.Count == 0) && (progress == null || progress.Count == 0)) return;
            var rec = PlayerRegistry.Get(name);
            if (levels != null)
                foreach (var kv in levels)
                    if (Finite(kv.Value) && (rec.SkillLevels.ContainsKey(kv.Key) || rec.SkillLevels.Count < MaxSkillKeys))
                        rec.SkillLevels[kv.Key] = Mathf.Clamp(kv.Value, 0f, MaxSkillLevel);
            if (progress != null)
                foreach (var kv in progress)
                    if (Finite(kv.Value) && (rec.SkillProgress.ContainsKey(kv.Key) || rec.SkillProgress.Count < MaxSkillKeys))
                        rec.SkillProgress[kv.Key] = Mathf.Clamp(kv.Value, 0f, MaxSkillProgress);
            rec.Dirty = true;
        }
    }

    // ── Shared re-entrancy guard between the RPC report path and the
    // dormant patches above (handoff: "insurance, not behavior" — the
    // patches never actually fire today, but if some future topology makes
    // them fire alongside real client reports, this is what stops a kill
    // from being credited twice). Same shape as Patch_CharacterDeath's own
    // per-instance AlreadyProcessed, but keyed on the event's real-world
    // identity instead of a Character instance ID, since the two call paths
    // don't share an instance to key off of. ───────────────────────────────
    internal static class CombatEventDedup
    {
        private static readonly Dictionary<string, float> _recent = new Dictionary<string, float>();
        private const float WINDOW = 3f;

        public static bool AlreadyProcessed(string kind, string attacker, string prefab, Vector3 pos, float bucketMeters = 4f)
        {
            float now = Time.realtimeSinceStartup;
            foreach (var key in new List<string>(_recent.Keys))
                if (now - _recent[key] > WINDOW) _recent.Remove(key);

            string posKey = $"{Mathf.Round(pos.x / bucketMeters)}:{Mathf.Round(pos.y / bucketMeters)}:{Mathf.Round(pos.z / bucketMeters)}";
            string eventKey = $"{kind}|{attacker}|{prefab}|{posKey}";
            if (_recent.ContainsKey(eventKey)) return true;
            _recent[eventKey] = now;
            return false;
        }
    }

    // ── Routed-RPC sender binding (1.8.0). The `sender` a ZRoutedRpc handler
    // receives is RoutedRPCData.m_senderPeerID, read straight from the
    // client's bytes in ZRoutedRpc.RPC_RoutedRPC and never compared with the
    // connection that delivered them (1.0.15 decompile: RPC_RoutedRPC →
    // Deserialize → HandleRoutedRPC → Invoke(data.m_senderPeerID, …)). So a
    // modified client could name any other player's uid as the sender and
    // pass every "is this a self-report" check below as that player
    // (review 2026-09-24, reopening must-fix #2 of 2026-09-15). This
    // remembers the uid of the peer whose connection is delivering the
    // current RoutedRPC; the receivers accept a report only when the claimed
    // sender is that uid. Server-side only; clients are unaffected.
    // 1.8.0: a routed RPC this process invokes on itself never passes
    // through RPC_RoutedRPC: InvokeRoutedRPC runs the handler in place when
    // the target is this process's own id or everybody, with this process's
    // own id as the sender (ZRoutedRpc.cs:120-133). RoutedLocalDispatch marks
    // such a call explicitly, and TryResolveSender takes it as the listen
    // host's own report (the host's WhereTheCrowFlies, whose no-target
    // InvokeRoutedRPC targets this process on a server, :102-112) and
    // refuses it anywhere else, as before. A remote peer can only ever push
    // a peer entry, so it can never pass as the host. ──────────────────
    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.RPC_RoutedRPC))]
    public static class RoutedSenderGuard
    {
        // One entry per RPC_RoutedRPC or InvokeRoutedRPC on the call stack
        // (a stack, since either can run inside the other): the delivering
        // peer's uid (0 = no ready peer), or a local call of this process.
        // A local invoke made inside a peer's RoutedRPC is on top of it.
        private static readonly Stack<(long uid, bool local)> _delivering = new Stack<(long uid, bool local)>();
        private static readonly Dictionary<long, float> _lastWarn = new Dictionary<long, float>();

        private static void Prefix(ZRpc rpc, out int __state)
        {
            long uid = 0L;
            try { uid = ZNet.instance?.GetPeer(rpc)?.m_uid ?? 0L; } catch { }
            __state = Push(uid, false);
        }

        // A finalizer, not a postfix, so the entry is popped even when a
        // handler throws out of HandleRoutedRPC.
        private static void Finalizer(int __state) => PopTo(__state);

        // Returns the stack's depth with the new entry on it, for PopTo.
        internal static int Push(long uid, bool local)
        {
            _delivering.Push((uid, local));
            return _delivering.Count;
        }

        // Back to the depth below the entry Push returned `depth` for, so an
        // entry an inner frame left behind goes too. 0 = that Push never ran
        // (another patch's prefix threw before it; the finalizer still
        // runs), so nothing is popped.
        internal static void PopTo(int depth)
        {
            if (depth <= 0) return;
            while (_delivering.Count >= depth) _delivering.Pop();
        }

        // True only when `claimed` is the uid of the ready peer whose
        // connection delivered this RoutedRPC (the reporter is then that
        // peer), or, for a call this process made on itself, when it is a
        // listen host and `claimed` is the host uid (the reporter is then
        // the host, with no peer).
        public static bool TryResolveSender(long claimed, out Reporter reporter)
        {
            reporter = default;
            if (_delivering.Count == 0 || ZNet.instance == null) return false;
            var top = _delivering.Peek();
            if (top.local)
            {
                // Refused without a word otherwise: the "claimed to send as"
                // warning below is for a real connection only.
                if (claimed != ZNet.GetUID() || !Plugin.TryGetHostPlayer(out var host)) return false;
                HostSession.NoteCrowReport();
                reporter = new Reporter(null, true, claimed, host.name);
                return true;
            }
            long delivering = top.uid;
            if (delivering == 0L) return false;
            if (claimed != delivering)
            {
                // A real client never does this; say so, at most once a
                // minute per connection so a flood cannot flood the log.
                float now = Time.realtimeSinceStartup;
                if (!_lastWarn.TryGetValue(delivering, out float t) || now - t >= 60f)
                {
                    if (_lastWarn.Count > 256) _lastWarn.Clear();
                    _lastWarn[delivering] = now;
                    string who = ZNet.instance.GetPeer(delivering)?.m_playerName;
                    Plugin.Log.LogWarning($"[TheRavensCall] report dropped — connection {delivering} ({who}) claimed to send as {claimed}");
                }
                return false;
            }
            var peer = ZNet.instance.GetPeer(claimed);
            if (peer == null || !peer.IsReady()) return false;
            reporter = new Reporter(peer, false, claimed, peer.m_playerName);
            return true;
        }
    }

    // ── Marks every routed RPC this process invokes as LOCAL on
    // RoutedSenderGuard's stack for as long as the call runs (1.8.0). The
    // overload every other InvokeRoutedRPC funnels into (ZRoutedRpc.cs:97-105
    // -> :120). Marked always, not only when the target makes it local:
    // the only handler that can run inside this call is the in-place
    // HandleRoutedRPC for a target of this process's own id or everybody
    // (:130-133); for any other target RouteRPC only hands the package to
    // the peers' sockets (:140-173, ZRpc.cs:274-295), so an unused mark is
    // never read. Deciding here would need the private m_id, and the sender
    // a local call carries is always that id anyway (:124), which
    // TryResolveSender checks. The finalizer pops even when a handler
    // throws, and pops back to its own depth, so the stack stays balanced
    // whatever throws inside. ────────────────────────────────────────────
    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), new[] { typeof(long), typeof(ZDOID), typeof(string), typeof(object[]) })]
    public static class RoutedLocalDispatch
    {
        private static void Prefix(out int __state) => __state = RoutedSenderGuard.Push(0L, true);

        private static void Finalizer(int __state) => RoutedSenderGuard.PopTo(__state);
    }

    // ── Who sent a report RoutedSenderGuard resolved (1.8.0): a remote peer
    // (Peer set), or the listen host's own WhereTheCrowFlies (IsHost, no
    // Peer). Uid is the verified sender: the peer's uid or the host uid.
    // Name is the peer's m_playerName or the host character's name. ──────
    public readonly struct Reporter
    {
        public readonly ZNetPeer Peer;
        public readonly bool IsHost;
        public readonly long Uid;
        public readonly string Name;

        public Reporter(ZNetPeer peer, bool isHost, long uid, string name)
        {
            Peer = peer;
            IsHost = isHost;
            Uid = uid;
            Name = name;
        }
    }

    // ── Per-sender flood guard for the RPC receiver. A malicious or broken
    // client can invoke a routed RPC as fast as it wants; nothing upstream
    // rate-limits that for us. Keyed on the verified peer uid (see
    // RoutedSenderGuard), so a client can no longer rotate made-up sender
    // ids past the cap or grow this table with them (1.8.0). A listen
    // host's own reports are keyed on the host uid. ─────────────────────
    internal static class CombatReportRateLimiter
    {
        private const int MaxPerSecond = 30;
        private static readonly Dictionary<long, (int count, float windowStart)> _windows = new Dictionary<long, (int, float)>();

        public static bool Exceeded(long sender)
        {
            float now = Time.realtimeSinceStartup;
            // Every reconnect gets a new uid; drop idle windows now and then
            // so a long uptime does not keep one entry per past connection.
            if (_windows.Count > 128)
                foreach (var k in new List<long>(_windows.Keys))
                    if (now - _windows[k].windowStart >= 1f) _windows.Remove(k);
            if (!_windows.TryGetValue(sender, out var w) || now - w.windowStart >= 1f)
            {
                _windows[sender] = (1, now);
                return false;
            }
            _windows[sender] = (w.count + 1, w.windowStart);
            return w.count + 1 > MaxPerSecond;
        }

        // One boss-kill credit per sender per boss prefab per window. A real
        // boss dies once, and a V2 client's V1 dual-send of the same kill is
        // collapsed by CombatEventDedup anyway. Keyed by prefab as well as
        // sender because the reporter is the corpse's ZDO owner, not the
        // killer, and one peer can legitimately own two different bosses'
        // corpses inside ten seconds; a second report of the SAME boss from
        // one peer inside the window is forgery, and every accepted one fans
        // out into a per-player credit for everyone in radius plus a Discord
        // POST (review 2026-09-15).
        private const float BossCreditWindowSeconds = 10f;
        private static readonly Dictionary<string, float> _lastBossCredit = new Dictionary<string, float>();

        // One credited death per sender per window. A real player cannot die
        // twice in five seconds; a client varying its reported position a few
        // metres per packet could otherwise mint a fresh death dedup key at
        // up to 30/s, each with a death_history entry and a Discord POST
        // (review 2026-09-15). Self-only after the sender binding, so this
        // caps self-spam of the channel, not cross-player forgery.
        private const float DeathCreditWindowSeconds = 5f;
        private static readonly Dictionary<long, float> _lastDeathCredit = new Dictionary<long, float>();

        public static bool AllowDeathCredit(long sender)
        {
            float now = Time.realtimeSinceStartup;
            foreach (var k in new List<long>(_lastDeathCredit.Keys))
                if (now - _lastDeathCredit[k] > DeathCreditWindowSeconds) _lastDeathCredit.Remove(k);
            if (_lastDeathCredit.ContainsKey(sender)) return false;
            _lastDeathCredit[sender] = now;
            return true;
        }

        public static bool AllowBossCredit(long sender, string prefab)
        {
            float now = Time.realtimeSinceStartup;
            foreach (var k in new List<string>(_lastBossCredit.Keys))
                if (now - _lastBossCredit[k] > BossCreditWindowSeconds) _lastBossCredit.Remove(k);
            string key = $"{sender}|{prefab}";
            if (_lastBossCredit.ContainsKey(key)) return false;
            _lastBossCredit[key] = now;
            return true;
        }
    }

    // ── Distinguishes a v1.0.1+ client's redundant V1 damage dual-send
    // (skip — EventReportReceiver already credited it via V2) from a real
    // v1.0.0-only client's damage packet (credit it — it's the only copy
    // that player will ever send). Damage has no per-event dedup of its own
    // (CombatEventDedup deliberately excludes it — see CreditDamage's
    // comment), so without this, disabling V1's damage case unconditionally
    // would silently blackout damage tracking for any player still on
    // WhereTheCrowFlies v1.0.0 (V1-only, predates the V2 protocol). ────────
    internal static class V2DamageTracker
    {
        private static readonly Dictionary<string, float> _lastSeen = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private const float StaleAfterSeconds = 30f; // comfortably past the 10s V2 batch tick

        public static void MarkSeen(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return;
            _lastSeen[playerName] = Time.realtimeSinceStartup;
        }

        public static bool RecentlySeen(string playerName) =>
            !string.IsNullOrEmpty(playerName) &&
            _lastSeen.TryGetValue(playerName, out float t) &&
            Time.realtimeSinceStartup - t <= StaleAfterSeconds;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // COMBAT REPORT RECEIVER — server-side half of RavensCall_CombatReport_V1.
    // Sender is the WhereTheCrowFlies client mod's owner-gated Character.
    // Damage/OnDeath/FishingFloat.Catch patches (separate repo — this mod
    // stays server-only, see the Architecture note up top). Schema version 1,
    // ZPackage write order (must match the sender exactly — this is the
    // single source of truth for both sides):
    //   1. int    schemaVersion   always 1
    //   2. byte   eventType       1=creatureKill 2=playerDeath 3=damageBatch 4=fishCatch
    //   3. string victimPrefab    creature prefab (kill) / fish prefab (catch) /
    //                             cause-of-death string (playerDeath, e.g.
    //                             "combat:Draugr", "fall", "drowning" — NOT
    //                             empty; this is a deliberate deviation from
    //                             IMPLEMENTATION_HANDOFF.md's original "" spec,
    //                             made because the RPC path has no Player
    //                             instance to derive a cause from otherwise) /
    //                             "" for damageBatch
    //   4. string attackerName    in-game player name being credited; for
    //                             playerDeath, the dying player; empty = unattributed
    //   5. Vector3 position       victim position (kill/death), reporter position otherwise
    //   6. float   dmgDealt       damageBatch only, else 0
    //   7. float   dmgTaken       damageBatch only, else 0
    // ═════════════════════════════════════════════════════════════════════════
    public static class CombatReportReceiver
    {
        private const int SchemaVersion = 1;

        private static string Cap(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max) : s);

        private static float ClampDamage(float v) => Mathf.Clamp(v, 0f, 10000f);

        public static void Handle(long sender, ZPackage pkg)
        {
            try
            {
                if (!Plugin.IsServer() || !Plugin.AcceptClientReports.Value) return;

                // Reporter identity is the peer whose connection delivered
                // this packet, never the payload and never the client-chosen
                // routed sender id alone (RoutedSenderGuard, 1.8.0), or a
                // listen host's own crow, dispatched in this process. From
                // here on `sender` is that verified uid.
                if (!RoutedSenderGuard.TryResolveSender(sender, out var reporter)) return;

                if (CombatReportRateLimiter.Exceeded(sender))
                {
                    if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] combat report rate-limited from sender {sender}");
                    return;
                }

                int schema = pkg.ReadInt();
                if (schema != SchemaVersion) return; // unknown future schema: ignore, never guess

                byte eventType = pkg.ReadByte();
                string prefabOrCause = Cap(pkg.ReadString(), 64);
                string attackerName = Cap(pkg.ReadString(), 32);
                Vector3 position = pkg.ReadVector3();
                float dmgDealt = ClampDamage(pkg.ReadSingle());
                float dmgTaken = ClampDamage(pkg.ReadSingle());

                // Self-reports (death, damage batch, fish) must name the
                // reporter's own character. Only a kill may legitimately name
                // someone else: the creature's ZDO owner reports it and the
                // killer can be another player. Before this check, any
                // connected player could credit or overwrite any other online
                // player's record (review 2026-09-15).
                if (eventType != 1 && !Plugin.IsSelfReport(reporter, attackerName))
                {
                    if (Plugin.LogCombatReports.Value)
                        Plugin.Log.LogWarning($"[TheRavensCall] combat report dropped — '{attackerName}' is not the reporter ({reporter.Name})");
                    return;
                }

                if (!Plugin.TryVerifyConnectedPlayer(attackerName, out string verified, out _))
                {
                    if (Plugin.LogCombatReports.Value)
                        Plugin.Log.LogInfo($"[TheRavensCall] combat report dropped — '{attackerName}' not connected (reporter {reporter.Name})");
                    return;
                }

                if (Plugin.LogCombatReports.Value)
                    Plugin.Log.LogInfo($"[TheRavensCall] combat report accepted: type={eventType} attacker={verified} data={prefabOrCause} dmgDealt={dmgDealt} dmgTaken={dmgTaken}");

                switch (eventType)
                {
                    // Kill/death/fish are safe to credit from either wire: a
                    // V2-capable client dual-sends the same event on V1 for
                    // backward compat, and CombatEventDedup (keyed on
                    // kind+attacker+prefab+position, 3s window) collapses
                    // the redundant arrival regardless of which lands first.
                    case 1:
                        if (TitleSystem.BossTitles.ContainsKey(prefabOrCause) && !CombatReportRateLimiter.AllowBossCredit(sender, prefabOrCause))
                        {
                            if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] boss kill report dropped — budget exceeded for sender {sender}");
                            break;
                        }
                        CombatCredit.CreditCreatureKill(verified, prefabOrCause, position);
                        break;
                    case 2:
                        if (!CombatReportRateLimiter.AllowDeathCredit(sender))
                        {
                            if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] death report dropped — budget exceeded for sender {sender}");
                            break;
                        }
                        CombatCredit.CreditPlayerDeath(verified, position, prefabOrCause);
                        break;
                    // Damage has no such dedup (a batch is a rolling sum, not
                    // a discrete event — see CreditDamage's own comment). A
                    // V2 client dual-sends this as V1 too, and crediting it
                    // from both would double it every batch — but a real
                    // WhereTheCrowFlies v1.0.0 (V1-only, pre-V2) client's
                    // packet is the only copy it'll ever send, so only skip
                    // when this player has been confirmed to also be sending
                    // V2 (see V2DamageTracker).
                    case 3:
                        if (!V2DamageTracker.RecentlySeen(verified)) CombatCredit.CreditDamage(verified, dmgDealt, dmgTaken);
                        break;
                    case 4: CombatCredit.CreditFish(verified, prefabOrCause); break;
                    default:
                        if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] combat report: unknown eventType {eventType}");
                        break;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] combat report error: {ex.Message}"); }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // EVENT REPORT RECEIVER — server-side half of RavensCall_EventReport_V2.
    // Sender is WhereTheCrowFlies (separate repo, see CombatReportReceiver's
    // header above for the two-mod split). V2 is the full-coverage successor
    // to V1: every V1-capable client dual-sends the same kill/death/damage/
    // fish event on both channels for backward compat with servers that
    // don't know V2 yet, so kill/death/fish share dedup with the V1 path
    // (CombatEventDedup) and damage is V2-exclusive once this is registered
    // (see the V1 case 3 comment above). ZPackage write order (single source
    // of truth for both sides — must match WhereTheCrowFlies's
    // TelemetrySender.SendX methods exactly):
    //   1. int  schemaVersion   always 2
    //   2. byte eventType       1=kill 2=death 3=damageBatch 4=fishCatch
    //                           5=building 6=crafting 7=harvesting
    //                           8=consumables 9=worldEvent 10=statSync(delta)
    //                           11=statSnapshot(absolute) 12=skillSnapshot(absolute)
    //                           13=titleRequest (1.7.0 — the only type that
    //                           replies to the sender; see HandleTitleRequest)
    //   3.. event-specific payload — see each Handle* method below.
    //
    // eventType 10 (StatSync, every 10s) and 11 (StatSnapshot, absolute —
    // fires on WhereTheCrowFlies's FullSyncIntervalSeconds, default 5
    // minutes, plus once on player spawn) both feed VanillaStats, and that's
    // deliberate rather than a double-write: 10 accumulates incrementally
    // (ApplyVanillaStatDeltas) so the numbers stay live between the much
    // less frequent absolute snapshots, and 11 always overwrites outright
    // (ApplyVanillaStats) so it corrects any drift a lost delta packet
    // caused, every time it fires, no matter what 10 did in between.
    // ═════════════════════════════════════════════════════════════════════════
    public static class EventReportReceiver
    {
        private const int SchemaVersion = 2;
        // Valheim 1.0.7 grew PlayerStatType from 105 to 205 counters, and WhereTheCrowFlies 1.1.1 snapshots all of
        // them. The old cap of 200 silently dropped the last five (the count is clamped, the tail of the packet is
        // never read, nothing errors). Sized for the next expansion as well; it only bounds a malicious sender.
        private const int MaxStatPairs = 1024;

        private static string Cap(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length > max ? s.Substring(0, max) : s);

        public static void Handle(long sender, ZPackage pkg)
        {
            try
            {
                if (!Plugin.IsServer() || !Plugin.AcceptClientReports.Value) return;

                // The reporter's peer is the identity every self-report is
                // bound to below; the payload name alone was never enough
                // (review 2026-09-15), and neither is the client-chosen
                // routed sender id (RoutedSenderGuard, 1.8.0). From here on
                // `sender` is the verified uid of the delivering connection,
                // so the rate limits and the title reply's target are too;
                // for a listen host's own crow it is the host uid, and the
                // title reply to it runs in this process.
                if (!RoutedSenderGuard.TryResolveSender(sender, out var reporter)) return;

                if (CombatReportRateLimiter.Exceeded(sender))
                {
                    if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] event report rate-limited from sender {sender}");
                    return;
                }

                int schema = pkg.ReadInt();
                if (schema != SchemaVersion) return; // unknown future schema: ignore, never guess

                byte eventType = pkg.ReadByte();
                switch (eventType)
                {
                    case 1: HandleKill(sender, pkg); break;
                    case 2: HandleDeath(sender, reporter, pkg); break;
                    case 3: HandleDamageDefenseBatch(reporter, pkg); break;
                    case 4: HandleFishCatch(reporter, pkg); break;
                    case 5: HandleBuilding(reporter, pkg); break;
                    case 6: HandleCrafting(reporter, pkg); break;
                    case 7: HandleHarvesting(reporter, pkg); break;
                    case 8: HandleConsumables(reporter, pkg); break;
                    case 9: HandleWorldEvent(reporter, pkg); break;
                    case 10: HandleStatSyncDelta(reporter, pkg); break;
                    case 11: HandleStatSnapshot(reporter, pkg); break;
                    case 12: HandleSkillSnapshot(reporter, pkg); break;
                    case 13: HandleTitleRequest(sender, reporter, pkg); break;
                    default:
                        if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] event report: unknown eventType {eventType}");
                        break;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] event report error: {ex.Message}"); }
        }

        // Every handler below verifies its own playerName field against the
        // connected-player list before crediting anything — same trust
        // boundary as CombatReportReceiver, just per-handler since the name
        // field sits at a different offset in each payload shape.
        private static bool Verify(string reportedName, out string verified)
        {
            if (!Plugin.TryVerifyConnectedPlayer(reportedName, out verified, out _))
            {
                if (Plugin.LogCombatReports.Value)
                    Plugin.Log.LogInfo($"[TheRavensCall] event report dropped — '{reportedName}' not connected");
                return false;
            }
            return true;
        }

        // Every event type except Kill is a self-report: the name in the
        // payload must be the reporting peer's own character, not merely
        // some connected player's. Kill keeps the connected-name check
        // because the creature's ZDO owner reports it and the killer can
        // legitimately be someone else (review 2026-09-15).
        private static bool VerifySelf(Reporter reporter, string reportedName, out string verified)
        {
            verified = null;
            if (!Plugin.IsSelfReport(reporter, reportedName))
            {
                if (Plugin.LogCombatReports.Value)
                    Plugin.Log.LogWarning($"[TheRavensCall] event report dropped — '{reportedName}' is not the reporter ({reporter.Name})");
                return false;
            }
            return Verify(reportedName, out verified);
        }

        private static void HandleKill(long sender, ZPackage pkg)
        {
            string attackerName = Cap(pkg.ReadString(), 32);
            string victimPrefab = Cap(pkg.ReadString(), 64);
            Vector3 position = pkg.ReadVector3();
            pkg.ReadInt();     // level — CreditCreatureKill's dedup/credit path doesn't need it
            pkg.ReadBool();    // isBoss — already resolved from the prefab via TitleSystem.BossTitles
            pkg.ReadBool();    // isTamed
            pkg.ReadString();  // weaponOrDmgType
            pkg.ReadString();  // biome

            if (!Verify(attackerName, out string verified)) return;
            if (TitleSystem.BossTitles.ContainsKey(victimPrefab) && !CombatReportRateLimiter.AllowBossCredit(sender, victimPrefab))
            {
                if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] boss kill report dropped — budget exceeded for sender {sender}");
                return;
            }
            CombatCredit.CreditCreatureKill(verified, victimPrefab, position);
        }

        private static void HandleDeath(long sender, Reporter reporter, ZPackage pkg)
        {
            string victimName = Cap(pkg.ReadString(), 32);
            string cause = Cap(pkg.ReadString(), 64);
            Vector3 position = pkg.ReadVector3();
            pkg.ReadString();  // killerPlayer
            pkg.ReadString();  // biome

            if (!VerifySelf(reporter, victimName, out string verified)) return;
            if (!CombatReportRateLimiter.AllowDeathCredit(sender))
            {
                if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] death report dropped — budget exceeded for sender {sender}");
                return;
            }
            CombatCredit.CreditPlayerDeath(verified, position, cause);
        }

        private static void HandleDamageDefenseBatch(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadVector3(); // position
            float dealtCreatures = pkg.ReadSingle();
            float dealtPlayers = pkg.ReadSingle();
            float takenCreatures = pkg.ReadSingle();
            float takenPlayers = pkg.ReadSingle();
            float takenEnv = pkg.ReadSingle();
            pkg.ReadInt();     // hitsDealt — already covered by VanillaStats EnemyHits/PlayerHits via StatSnapshot
            pkg.ReadInt();     // hitsTaken
            int blocks = pkg.ReadInt();
            int parries = pkg.ReadInt();
            float dmgBlocked = pkg.ReadSingle();

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            V2DamageTracker.MarkSeen(verified); // tells V1's damage case this player's dual-sent V1 copy is redundant
            float dmgDealt = Mathf.Clamp(dealtCreatures + dealtPlayers, 0f, 20000f);
            float dmgTaken = Mathf.Clamp(takenCreatures + takenPlayers + takenEnv, 0f, 20000f);
            CombatCredit.CreditDamage(verified, dmgDealt, dmgTaken);
            CombatCredit.CreditBlockDefense(verified, blocks, parries, Mathf.Clamp(dmgBlocked, 0f, 20000f));
        }

        private static void HandleFishCatch(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            string fishPrefab = Cap(pkg.ReadString(), 64);
            pkg.ReadVector3(); // position
            pkg.ReadInt();     // quality
            pkg.ReadSingle();  // weight
            pkg.ReadString();  // biome

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditFish(verified, fishPrefab);
        }

        private static void HandleBuilding(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadString();  // piecePrefab
            pkg.ReadVector3(); // position
            byte action = pkg.ReadByte();
            pkg.ReadString();  // category

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditBuilding(verified, action);
        }

        private static void HandleCrafting(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadString();  // itemPrefab
            pkg.ReadVector3(); // position
            byte action = pkg.ReadByte();
            pkg.ReadInt();     // quality
            int amount = pkg.ReadInt();
            pkg.ReadString();  // stationName

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditCrafting(verified, action, amount);
        }

        private static void HandleHarvesting(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            string resourceName = Cap(pkg.ReadString(), 64);
            pkg.ReadVector3(); // position
            pkg.ReadByte();    // sourceType — ResourcesHarvested is a flat count per resource, not split by source
            int amount = pkg.ReadInt();

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditHarvest(verified, resourceName, amount);
        }

        private static void HandleConsumables(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadString();  // itemPrefab
            pkg.ReadVector3(); // position
            pkg.ReadByte();    // itemType
            pkg.ReadSingle();  // health
            pkg.ReadSingle();  // stamina
            pkg.ReadSingle();  // eitr

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditConsumable(verified);
        }

        private static void HandleWorldEvent(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            string eventName = Cap(pkg.ReadString(), 32);
            pkg.ReadVector3(); // position
            pkg.ReadString();  // targetOrDetails
            pkg.ReadString();  // biome

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditWorldEvent(verified, eventName);
        }

        // Applied incrementally — see the class header note on why both this
        // and HandleStatSnapshot feed VanillaStats.
        private static void HandleStatSyncDelta(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadVector3(); // position
            int count = Mathf.Clamp(pkg.ReadInt(), 0, MaxStatPairs);

            var deltas = new Dictionary<string, float>(count);
            for (int i = 0; i < count; i++)
            {
                short statId = pkg.ReadShort();
                float delta = pkg.ReadSingle();
                // An id this Valheim does not define would become a numeric
                // vanilla_stats key forever (1.8.0).
                if (Enum.IsDefined(typeof(PlayerStatType), (int)statId))
                    deltas[((PlayerStatType)statId).ToString()] = delta;
            }

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.ApplyVanillaStatDeltas(verified, deltas);
        }

        private static void HandleStatSnapshot(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadVector3(); // position
            int count = Mathf.Clamp(pkg.ReadInt(), 0, MaxStatPairs);

            var stats = new Dictionary<string, float>(count);
            for (int i = 0; i < count; i++)
            {
                short statId = pkg.ReadShort();
                float value = pkg.ReadSingle();
                if (Enum.IsDefined(typeof(PlayerStatType), (int)statId)) // see HandleStatSyncDelta
                    stats[((PlayerStatType)statId).ToString()] = value;
            }

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.ApplyVanillaStats(verified, stats);
        }

        private static void HandleSkillSnapshot(Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadVector3(); // position
            int count = Mathf.Clamp(pkg.ReadInt(), 0, MaxStatPairs);

            var levels = new Dictionary<string, float>(count);
            var progress = new Dictionary<string, float>(count);
            for (int i = 0; i < count; i++)
            {
                short skillId = pkg.ReadShort();
                float level = pkg.ReadSingle();
                float pct = pkg.ReadSingle();
                string name = ((Skills.SkillType)skillId).ToString();
                levels[name] = level;
                progress[name] = pct;
            }

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.ApplySkillLevels(verified, levels, progress);
        }

        // Player title picker (1.7.0) — payload: string playerName, byte op
        // (1=List 2=Set 3=Clear), string title (Set only, "" otherwise; the
        // client trims, this caps). The only V2 event type that answers the
        // sender: every other handler above only ever credits state, but a
        // player needs to know whether their pick took, so this always sends
        // exactly one RavensCall_TitleReply_V1 back to `sender` — never
        // broadcast, never silent except for a genuinely unrecognized op
        // (matches every other handler's "unknown eventType" drop). The List/
        // Set/Clear rules themselves live on TitleSystem so the admin's
        // `ravenscall title` console subcommand (Patch_Terminal, below)
        // applies the exact same rules instead of a second copy of them.
        private static void HandleTitleRequest(long sender, Reporter reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            byte op = pkg.ReadByte();
            string title = Cap(pkg.ReadString(), 64);

            if (!VerifySelf(reporter, playerName, out string verified)) return;

            var rec = PlayerRegistry.Get(verified);
            (byte kind, string text) reply;
            switch (op)
            {
                case 1: reply = TitleSystem.ListTitles(rec); break;
                case 2: reply = TitleSystem.SetTitle(verified, rec, title); break;
                case 3: reply = TitleSystem.ClearTitle(verified, rec); break;
                default:
                    if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] title request: unknown op {op}");
                    return;
            }
            SendTitleReply(sender, reply.kind, reply.text, rec);
        }

        // Reply-only RPC, registered by WhereTheCrowFlies — this server never
        // registers a receiver for it, only sends. Schema 1: int
        // schemaVersion, byte kind (1=ok/informational 2=refused), string
        // text (one human-readable line, already final — no localisation
        // tokens), int count (rec.EarnedTitles.Count), string title × count
        // (the earned titles, in HashSet iteration order — the same order
        // TitleCsv/ListTitles already use, so the panel's list always matches
        // what the text line would have said), string active
        // (rec.ActiveTitle, "" = none). The list rides every reply — list,
        // set, clear, even a refusal — so WhereTheCrowFlies' title panel
        // (1.2.0+) is always fresh after any op without a second round trip
        // (SPEC-title-ui.md). Always targeted at the requesting peer
        // (InvokeRoutedRPC's long targetPeerID overload), matching the wire
        // fact that a routed RPC name unregistered on the receiver is just
        // silently ignored, so an older WhereTheCrowFlies (pre-1.2.0) simply
        // never sees this either way.
        private static void SendTitleReply(long targetPeer, byte kind, string text, PlayerRecord rec)
        {
            var pkg = new ZPackage();
            pkg.Write(1); // schemaVersion
            pkg.Write(kind);
            pkg.Write(text);
            pkg.Write(rec.EarnedTitles.Count);
            foreach (string title in rec.EarnedTitles) pkg.Write(title);
            pkg.Write(rec.ActiveTitle ?? "");
            ZRoutedRpc.instance.InvokeRoutedRPC(targetPeer, "RavensCall_TitleReply_V1", pkg);
        }
    }

    // ── Re-targeted 2026-09-21 (SCOPE-1.4.0.md §4.5): the previous patch
    // hooked SetRandomEventByName, which a natural raid never calls on a
    // dedicated server — only RandEventSystem.Load (world load restoring a
    // saved active event) reaches it there, so RaidActive/RaidType never
    // flipped for a real raid and, once set by a world load, never cleared.
    // StartRandomEvent, UpdateRandomEvent's standalone-interval branch,
    // FixedUpdate's clear and ResetRandomEvent (the stopevent command) all
    // funnel through the one private SetRandomEvent(RandomEvent, Vector3)
    // instead — verified against the 1.0.12 decompile. Postfix on that.
    [HarmonyPatch(typeof(RandEventSystem), "SetRandomEvent")]
    public static class Patch_Raid
    {
        private static void Postfix(RandomEvent ev)
        {
            if (!Plugin.IsServer()) return;

            string now = ev?.m_name ?? "";
            string before = WorldState.RaidType;
            if (now == before) return;

            // The BarrkBOT-facing flag/name flip unconditionally, independent
            // of EnableRaid — only the Chronicle/feed narration below is gated.
            WorldState.RaidActive = !string.IsNullOrEmpty(now);
            WorldState.RaidType = now;

            if (Plugin.EnableRaid == null || !Plugin.EnableRaid.Value) return;
            if (!string.IsNullOrEmpty(now))
                Plugin.Narrate($"A raid begins: {RaidNames.Get(now)}!", "raid_start", "world", now);
            else
                Plugin.Narrate("The raid has ended.", "raid_end", "world", before);
        }
    }

    // ── Dead on a dedicated server (FishingFloat is client-owned), kept as
    // insurance — see Patch_Damage above. Delegates to CombatCredit.CreditFish
    // so it shares dedup with the RPC path. ─────────────────────────────────
    [HarmonyPatch(typeof(FishingFloat), "Catch")]
    public static class Patch_FishCatch
    {
        private static void Postfix(Fish fish, Character owner)
        {
            try
            {
                if (!Plugin.IsServer() || fish == null || !(owner is Player p)) return;
                var itemDrop = fish.GetComponent<ItemDrop>();
                if (itemDrop == null) return;
                string slug = itemDrop.m_itemData.m_shared.m_name;
                CombatCredit.CreditFish(p.GetPlayerName(), slug);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] FishCatch error: {ex.Message}"); }
        }
    }

    // ── Biome discovery + gear tier are detected by polling (see
    // Companion.PollAllPlayers) rather than patching client UI callbacks
    // (MessageHud.ShowBiomeFoundMsg / Humanoid.EquipItem never fire on a
    // headless server — there is no local UI for either to run through). ──
    public static class BiomeAndGearTracking
    {
        public static readonly Dictionary<string, (int tier, string label)> TierMap = new Dictionary<string, (int, string)>
        {
            { "ArmorLeatherChest",(1,"Leather") },{ "ArmorLeatherLegs",(1,"Leather") },{ "HelmetLeather",(1,"Leather") },
            { "AxeFlint",(1,"Flint") },{ "KnifeFlint",(1,"Flint") },{ "SpearFlint",(1,"Flint") },
            { "HelmetBronze",(2,"Bronze") },{ "ArmorBronzeChest",(2,"Bronze") },{ "ArmorBronzeLegs",(2,"Bronze") },
            { "AtgeirBronze",(2,"Bronze") },{ "AxeBronze",(2,"Bronze") },{ "SwordBronze",(2,"Bronze") },
            { "SpearBronze",(2,"Bronze") },{ "MaceBronze",(2,"Bronze") },{ "ShieldBronze",(2,"Bronze") },{ "ShieldBronzeBuckler",(2,"Bronze") },
            { "HelmetIron",(3,"Iron") },{ "ArmorIronChest",(3,"Iron") },{ "ArmorIronLegs",(3,"Iron") },
            { "SwordIron",(3,"Iron") },{ "AtgeirIron",(3,"Iron") },{ "AxeIron",(3,"Iron") },{ "MaceIron",(3,"Iron") },
            { "SledgeIron",(3,"Iron") },{ "ShieldIron",(3,"Iron") },{ "ShieldIronSquare",(3,"Iron") },
            { "ShieldIronTower",(3,"Iron") },{ "SpearElderbark",(3,"Iron") },
            { "SwordSilver",(4,"Silver") },{ "Frostner",(4,"Silver") },{ "KnifeSilver",(4,"Silver") },{ "ShieldSilver",(4,"Silver") },
            { "ArmorWolfChest",(4,"Wolf") },{ "ArmorWolfLegs",(4,"Wolf") },{ "HelmetDrake",(4,"Wolf") },
            { "CapeWolf",(4,"Wolf") },{ "ArmorWolf",(4,"Wolf") },
            { "SwordBlackmetal",(5,"Blackmetal") },{ "AtgeirBlackmetal",(5,"Blackmetal") },{ "AxeBlackmetal",(5,"Blackmetal") },
            { "KnifeBlackmetal",(5,"Blackmetal") },{ "ShieldBlackmetal",(5,"Blackmetal") },{ "ShieldBlackmetalTower",(5,"Blackmetal") },
            { "SwordBlackMetal",(5,"Blackmetal") },{ "AtgeirBlackMetal",(5,"Blackmetal") },{ "AxeBlackMetal",(5,"Blackmetal") },
            { "KnifeBlackMetal",(5,"Blackmetal") },{ "ShieldBlackMetal",(5,"Blackmetal") },{ "ShieldBlackMetalTower",(5,"Blackmetal") },
            { "ArmorPaddedCuirass",(5,"Padded") },{ "ArmorPaddedGreaves",(5,"Padded") },{ "HelmetPadded",(5,"Padded") },
            { "ArmorCarapaceChest",(6,"Carapace") },{ "ArmorCarapaceLegs",(6,"Carapace") },
            { "HelmetCarapace",(6,"Carapace") },{ "SwordCarapace",(6,"Carapace") },{ "ShieldCarapace",(6,"Carapace") },
            { "SwordFlametal",(7,"Flametal") },{ "AtgeirFlametal",(7,"Flametal") },{ "AxeFlametal",(7,"Flametal") },
            { "ArmorFlametalChest",(7,"Flametal") },{ "ArmorFlametalLegs",(7,"Flametal") },{ "HelmetFlametal",(7,"Flametal") },
        };

        public static string NormalizeBiome(string biome)
        {
            if (biome == null) return "";
            string lower = biome.ToLowerInvariant();
            if (lower.Contains("mistland")) return "Mistlands";
            if (lower.Contains("ashland")) return "Ashlands";
            if (lower.Contains("meadow")) return "Meadows";
            if (lower.Contains("black forest") || lower.Contains("blackforest")) return "BlackForest";
            if (lower.Contains("swamp")) return "Swamp";
            if (lower.Contains("mountain")) return "Mountain";
            if (lower.Contains("plain")) return "Plains";
            if (lower.Contains("ocean")) return "Ocean";
            if (lower.Contains("deep north") || lower.Contains("deepnorth")) return "DeepNorth";
            return biome;
        }

        // ── The live path: called once per poll tick, per connected player,
        // from Companion.PollAllPlayers using Plugin.GetConnectedPlayers()'s
        // name+position (never a Player instance — Player.GetAllPlayers() is
        // empty on a dedicated server, which is what made this and the old
        // CheckPlayer(Player, ...) below silently do nothing since launch;
        // see KILL_TRACKING_FINDINGS.md). Biome is pure world-position math
        // (WorldGenerator.instance works server-side), so it's fixable this
        // way. Gear tier is NOT — it needs the player's actual inventory,
        // which no server-side API exposes without owning their character —
        // so gear-tier detection stays broken here until a future client
        // report (same shape as RavensCall_CombatReport_V1) adds it. ──────
        public static void CheckBiome(string name, Vector3 position, PlayerRecord rec)
        {
            try
            {
                string biomeKey = NormalizeBiome(Companion.GetBiomeAt(position).ToString());
                if (string.IsNullOrEmpty(biomeKey) || rec.BiomesDiscovered.Contains(biomeKey)) return;

                rec.BiomesDiscovered.Add(biomeKey);
                rec.Dirty = true;
                string biomeDisplay = TitleSystem.GetDisplayName(name);
                string msg = NarrativeSystem.Get("biome_discovery", new Dictionary<string, string>
                {
                    { "name", biomeDisplay }, { "biome", biomeKey },
                }, $"{biomeDisplay} has discovered the {biomeKey} for the first time.");
                if (Plugin.EnableBiomeDiscovery.Value) Plugin.FireEvent("biome_discovery", name, msg, biomeKey);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Biome check error: {ex.Message}"); }
        }

        // ── Dead on a dedicated server (no Player instance ever exists here
        // to pass in) — kept as insurance for any topology where a real
        // Player is available, same philosophy as the combat patches. ─────
        public static void CheckPlayer(Player player, PlayerRecord rec)
        {
            try
            {
                string biomeKey = NormalizeBiome(Companion.GetBiome(player).ToString());
                if (!string.IsNullOrEmpty(biomeKey) && !rec.BiomesDiscovered.Contains(biomeKey))
                {
                    rec.BiomesDiscovered.Add(biomeKey);
                    rec.Dirty = true;
                    string name = player.GetPlayerName();
                    string biomeDisplay = TitleSystem.GetDisplayName(name);
                    string msg = NarrativeSystem.Get("biome_discovery", new Dictionary<string, string>
                    {
                        { "name", biomeDisplay }, { "biome", biomeKey },
                    }, $"{biomeDisplay} has discovered the {biomeKey} for the first time.");
                    if (Plugin.EnableBiomeDiscovery.Value) Plugin.FireEvent("biome_discovery", name, msg, biomeKey);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Biome check error: {ex.Message}"); }

            try
            {
                int highestTier = rec.GearTier;
                foreach (var item in player.GetInventory().GetAllItems())
                {
                    if (!item.m_equipped) continue;
                    string prefabName = item.m_dropPrefab?.name ?? item.m_shared.m_name?.Replace("$item_", "").Replace("$", "");
                    if (string.IsNullOrEmpty(prefabName) || !TierMap.TryGetValue(prefabName, out var t)) continue;
                    if (t.tier <= highestTier) continue;
                    highestTier = t.tier;
                    rec.GearTier = t.tier;
                    rec.Dirty = true;
                    string name = player.GetPlayerName();
                    string gearDisplay = TitleSystem.GetDisplayName(name);
                    string msg = NarrativeSystem.Get("gear_tier", new Dictionary<string, string>
                    {
                        { "name", gearDisplay }, { "gear", t.label },
                    }, $"{gearDisplay} has equipped {t.label} gear for the first time!");
                    if (Plugin.EnableGearTier.Value) Plugin.FireEvent("gear_tier", name, msg, t.label);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Gear tier check error: {ex.Message}"); }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // MILESTONE TRACKER
    // ═════════════════════════════════════════════════════════════════════════
    public static class MilestoneTracker
    {
        private static readonly int[] KillMilestones = { 1, 10, 100, 500, 1000, 5000, 10000 };
        private static readonly int[] DeathMilestones = { 1, 5, 10, 25, 50, 100, 500, 1000 };

        public static void OnKill(string playerName, PlayerRecord rec)
        {
            if (!Plugin.EnableKillMilestone.Value) return;
            string killDisplay = TitleSystem.GetDisplayName(playerName);
            foreach (int m in KillMilestones)
                if (rec.Kills == m)
                {
                    string killMsg = NarrativeSystem.Get("kill_milestone", new Dictionary<string, string>
                    {
                        { "name", killDisplay }, { "count", m.ToString() },
                    }, $"{killDisplay} has slain {m} enemies!");
                    Plugin.FireEvent("kill_milestone", playerName, killMsg, m.ToString());
                    break;
                }
        }

        public static void OnDeath(string playerName, PlayerRecord rec)
        {
            if (!Plugin.EnableDeathMilestone.Value) return;
            string deathDisplay = TitleSystem.GetDisplayName(playerName);
            foreach (int m in DeathMilestones)
            {
                if (rec.Deaths == m)
                {
                    string fallback = m == 1
                        ? $"{deathDisplay} has died for the first time. Welcome to Valheim."
                        : $"{deathDisplay} has died {m} times. The saga continues.";
                    string deathMsg = NarrativeSystem.Get("death_milestone", new Dictionary<string, string>
                    {
                        { "name", deathDisplay }, { "count", m.ToString() },
                    }, fallback);
                    Plugin.FireEvent("death_milestone", playerName, deathMsg, m.ToString());
                    break;
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TITLE SYSTEM
    // ═════════════════════════════════════════════════════════════════════════
    public static class TitleSystem
    {
        public static readonly Dictionary<string, (int kills, string title)[]> CreatureTitles =
            new Dictionary<string, (int, string)[]>
        {
            { "Boar",          new[] { (100, "Boar Hunter"),      (500, "Boar Slayer"),        (1000, "Swine Reaper")          } },
            { "Deer",          new[] { (100, "Deer Stalker"),     (500, "The Huntsman"),       (1000, "Ghost of the Meadows")  } },
            { "Neck",          new[] { (100, "Neck Breaker"),     (500, "Swamp Wader"),        (1000, "Neck's Nightmare")      } },
            { "Greyling",      new[] { (100, "Greyling Crusher"), (500, "Ash Maker"),          (1000, "Ember of Yggdrasil")    } },
            { "Greydwarf",     new[] { (100, "Greydwarf Hunter"), (500, "Greydwarf Bane"),     (1000, "Terror of the Forest")  } },
            { "Troll",         new[] { (100, "Troll Fighter"),    (500, "Troll Breaker"),      (1000, "Giant Slayer")          } },
            { "Draugr",        new[] { (100, "Draugr Slayer"),    (500, "Death Walker"),       (1000, "Bane of the Fallen")    } },
            { "Skeleton",      new[] { (100, "Bone Breaker"),     (500, "The Undying"),        (1000, "Lord of Ash")           } },
            { "Blob",          new[] { (100, "Blob Stomper"),     (500, "Ooze Wader"),         (1000, "Plague Walker")         } },
            { "Wolf",          new[] { (100, "Wolf Hunter"),      (500, "Pack Breaker"),       (1000, "Alpha of Alphas")       } },
            { "Drake",         new[] { (100, "Drake Hunter"),     (500, "Dragonslayer"),       (1000, "Wyrm's Bane")           } },
            { "StoneGolem",    new[] { (100, "Stone Breaker"),    (500, "Mountain Crusher"),   (1000, "Wrath of the Peak")     } },
            { "GoblinBrute",   new[] { (100, "Fuling Fighter"),   (500, "Plains Reaper"),      (1000, "Goblin King's Dread")   } },
            { "Goblin",        new[] { (100, "Fuling Fighter"),   (500, "Plains Reaper"),      (1000, "Goblin King's Dread")   } },
            { "Deathsquito",   new[] { (100, "Bug Swatter"),      (500, "Needle Dancer"),      (1000, "Lord of Needles")       } },
            { "Lox",           new[] { (100, "Lox Tamer"),        (500, "Lox Breaker"),        (1000, "Plains Titan")          } },
            { "Leech",         new[] { (100, "Leech Stomper"),    (500, "Bloodless Wader"),    (1000, "The Undrained")         } },
            { "Wraith",        new[] { (100, "Wraith Breaker"),   (500, "Ghost Render"),       (1000, "Banisher of Souls")     } },
            { "Abomination",   new[] { (100, "Root Render"),      (500, "Swamp Titan"),        (1000, "The Unmade")            } },
            { "Fenring",       new[] { (100, "Fenring Slayer"),   (500, "Moon Breaker"),       (1000, "The Unwolfed")          } },
            { "Bat",           new[] { (100, "Bat Swatter"),      (500, "Cave Clearer"),       (1000, "Darkness Render")       } },
            { "Cultist",       new[] { (100, "Cultist Slayer"),   (500, "Heretic Breaker"),    (1000, "The Unconverted")       } },
            { "Serpent",       new[] { (100, "Serpent Hunter"),   (500, "Sea Slayer"),         (1000, "Leviathan's Bane")      } },
            { "Seeker",        new[] { (100, "Seeker Slayer"),    (500, "Mistwalker"),         (1000, "Bug Crusher")           } },
            { "SeekerBrute",   new[] { (100, "Brute Breaker"),    (500, "Carapace Crusher"),   (1000, "Titan of the Mist")     } },
            { "SeekerSoldier", new[] { (100, "Soldier Slayer"),   (500, "Legion Breaker"),     (1000, "Mist Commander")        } },
            { "Tick",          new[] { (100, "Tick Flicker"),     (500, "Bloodless"),          (1000, "The Untapped")          } },
            { "Gjall",         new[] { (100, "Gjall Hunter"),     (500, "Sky Render"),         (1000, "The Unscreamed")        } },
            { "Dvergr",        new[] { (100, "Dvergr Duelist"),   (500, "Rune Breaker"),       (1000, "Slayer of Ancients")    } },
            { "Charred",       new[] { (100, "Ash Fighter"),      (500, "Ember Breaker"),      (1000, "Lord of Cinders")       } },
            { "Morgen",        new[] { (100, "Morgen Slayer"),    (500, "Ashen Reaper"),       (1000, "Wrath of the Ashlands") } },
            { "Volture",       new[] { (100, "Volture Hunter"),   (500, "Sky Scourge"),        (1000, "Wings of Ash")          } },
            { "FallenValkyrie",new[] { (100, "Valkyrie Breaker"), (500, "Heaven's Bane"),      (1000, "The Unchosen")          } },
            { "Ulv",           new[] { (100, "Ulv Hunter"),       (500, "Pack Render"),        (1000, "Alpha of the North")    } },
            { "Hare",          new[] { (100, "Hare Hunter"),      (500, "Swift Slayer"),       (1000, "The Unbounding")        } },
        };

        private static readonly Dictionary<string, string> CreatureKeyMap = new Dictionary<string, string>
        {
            { "Boar","Boar" },{ "Boar_piggy","Boar" },{ "Deer","Deer" },{ "Neck","Neck" },
            { "Greyling","Greyling" },
            { "Greydwarf","Greydwarf" },{ "Greydwarf_elite","Greydwarf" },{ "Greydwarf_shaman","Greydwarf" },
            { "Troll","Troll" },
            { "Draugr","Draugr" },{ "Draugr_Elite","Draugr" },
            { "Skeleton","Skeleton" },{ "Skeleton_NoArcher","Skeleton" },
            { "Blob","Blob" },{ "BlobElite","Blob" },
            { "Wolf","Wolf" },{ "Wolf_cub","Wolf" },
            { "Drake","Drake" },{ "Hatchling","Drake" },{ "DrakeHatchling","Drake" },{ "StoneGolem","StoneGolem" },
            { "Goblin","Goblin" },{ "GoblinArcher","Goblin" },{ "GoblinShaman","Goblin" },
            { "GoblinBrute","GoblinBrute" },
            { "Deathsquito","Deathsquito" },{ "Lox","Lox" },
            { "Leech","Leech" },{ "Wraith","Wraith" },{ "Abomination","Abomination" },
            { "Fenring","Fenring" },{ "Fenring_CultistStar","Fenring" },
            { "Bat","Bat" },{ "Cultist","Cultist" },{ "CultistStar","Cultist" },
            { "Serpent","Serpent" },
            { "Seeker","Seeker" },{ "SeekerStar","Seeker" },
            { "SeekerBrute","SeekerBrute" },{ "SeekerSoldier","SeekerSoldier" },
            { "Tick","Tick" },{ "Gjall","Gjall" },
            { "Dvergr","Dvergr" },{ "DvergrMage","Dvergr" },{ "DvergrRogue","Dvergr" },
            { "Charred","Charred" },{ "CharredMage","Charred" },{ "CharredArcher","Charred" },{ "CharredTwitcher","Charred" },
            { "Morgen","Morgen" },{ "MorgenStar","Morgen" },
            { "Volture","Volture" },{ "FallenValkyrie","FallenValkyrie" },
            { "Ulv","Ulv" },{ "Hare","Hare" },
        };

        public static string GetActiveTitle(PlayerRecord rec) => rec?.ActiveTitle ?? "";

        // Returns "Name the Title" if the player has an active title, else just "Name"
        public static string GetDisplayName(string playerName)
        {
            var rec = PlayerRegistry.Get(playerName);
            string title = rec?.ActiveTitle;
            return string.IsNullOrEmpty(title) ? playerName : $"{playerName} the {title}";
        }

        private static void EarnTitle(string playerName, string title, PlayerRecord rec)
        {
            if (string.IsNullOrEmpty(title)) return;
            if (rec.EarnedTitles.Contains(title)) return;
            // Only a player's very first title activates itself. Before 1.7.0
            // any title earned while ActiveTitle was empty became the active
            // one, which silently undid a `/title clear` at the next
            // milestone; now a cleared title stays cleared and later titles
            // wait in titles_earned until the player picks one (review
            // 2026-09-22).
            bool first = rec.EarnedTitles.Count == 0;
            rec.EarnedTitles.Add(title);
            bool activated = first && string.IsNullOrEmpty(rec.ActiveTitle);
            if (activated) rec.ActiveTitle = title;
            rec.Dirty = true;

            string titleDisplay = GetDisplayName(playerName);
            string titleMsg = NarrativeSystem.Get("title_earned", new Dictionary<string, string>
            {
                { "name", titleDisplay }, { "title", title },
            }, $"{titleDisplay} has earned the title: {title}!");
            if (Plugin.EnableTitleEarned.Value) Plugin.FireEvent("title_earned", playerName, titleMsg, title);
            // The on-screen line (1.8.0): the plain name, never titleDisplay
            // (it already carries the active title) or titleMsg (a template
            // may carry Discord formatting). Its own switch, not
            // EnableTitleEarned; queue rules on TitleSync.AnnounceEarned.
            TitleSync.AnnounceEarned(playerName, title);
            PlayerRegistry.Save(rec);
            if (activated) TitleSync.SendOne(rec.Name, title);
        }

        // ── Title picker (1.7.0) ────────────────────────────────────────
        // Shared by EventReportReceiver.HandleTitleRequest (the player's
        // `/title` in WhereTheCrowFlies, replying over the RPC) and
        // Patch_Terminal's `ravenscall title` admin subcommand (replying via
        // Terminal.AddString) so both apply identical rules — see the spec's
        // wording, echoed in the messages below, for what each reply says.
        // Deliberately no narration/Chronicle/Discord line here (non-goal
        // for 1.7.0): callers only log and reply.
        private static string TitleCsv(PlayerRecord rec) => string.Join(", ", rec.EarnedTitles);

        public static (byte kind, string text) ListTitles(PlayerRecord rec)
        {
            if (rec.EarnedTitles.Count == 0) return (1, "You have earned no titles yet.");
            string suffix = string.IsNullOrEmpty(rec.ActiveTitle) ? "" : $" (active: {rec.ActiveTitle})";
            return (1, $"Your titles: {TitleCsv(rec)}{suffix}");
        }

        public static (byte kind, string text) SetTitle(string playerName, PlayerRecord rec, string title)
        {
            title = (title ?? "").Trim();
            if (string.IsNullOrEmpty(title))
                return (2, "Usage: title <name> — pick one of your earned titles, or `title clear`.");

            // Match case-insensitively but store (and echo back) the exact
            // spelling already on the record, never the typed casing.
            string stored = rec.EarnedTitles.FirstOrDefault(t => string.Equals(t, title, StringComparison.OrdinalIgnoreCase));
            if (stored == null)
                return (2, rec.EarnedTitles.Count == 0
                    ? $"You have not earned '{title}'. You have earned no titles yet."
                    : $"You have not earned '{title}'. Your titles: {TitleCsv(rec)}");

            if (string.Equals(rec.ActiveTitle, stored, StringComparison.Ordinal))
                return (1, $"'{stored}' is already your title.");

            rec.ActiveTitle = stored;
            rec.Dirty = true;
            Plugin.Log.LogInfo($"[TheRavensCall] {playerName} chose the title '{stored}'");
            PlayerRegistry.Save(rec);
            TitleSync.SendOne(rec.Name, stored);
            return (1, $"Title set: {stored}. You are now {playerName} the {stored}.");
        }

        public static (byte kind, string text) ClearTitle(string playerName, PlayerRecord rec)
        {
            if (string.IsNullOrEmpty(rec.ActiveTitle)) return (1, "You have no active title.");

            rec.ActiveTitle = "";
            rec.Dirty = true;
            Plugin.Log.LogInfo($"[TheRavensCall] {playerName} set no title");
            PlayerRegistry.Save(rec);
            TitleSync.SendOne(rec.Name, "");
            // Stays cleared: EarnTitle only self-activates a player's first
            // title, so a later milestone lands in titles_earned without
            // becoming active (review 2026-09-22).
            return (1, $"Title cleared. You are {playerName} again.");
        }

        public static void OnCreatureKill(string playerName, string prefab, PlayerRecord rec)
        {
            string key = null;
            if (!CreatureKeyMap.TryGetValue(prefab, out key))
            {
                foreach (var kv in CreatureKeyMap)
                    if (prefab.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase)) { key = kv.Value; break; }
                if (key == null)
                    foreach (var kv in CreatureKeyMap)
                        if (prefab.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0) { key = kv.Value; break; }
                if (key == null) key = prefab;
            }

            rec.CreatureKills.TryGetValue(key, out int kills);
            kills++;
            rec.CreatureKills[key] = kills;
            rec.Dirty = true;

            if (!CreatureTitles.TryGetValue(key, out var tiers)) return;
            foreach (var (threshold, title) in tiers)
                if (kills == threshold) { EarnTitle(playerName, title, rec); break; }
        }

        public static readonly Dictionary<string, string> BossTitles = new Dictionary<string, string>
        {
            { "Eikthyr",     "Stag Breaker"         },
            { "gd_king",     "Voice of the Ancient" },
            { "Bonemass",    "Bane of the Swamp"    },
            { "Dragon",      "Veil of the Frost"    },
            { "GoblinKing",  "Ender of Giants"      },
            { "SeekerQueen", "Hive Ender"           },
            { "Fader",       "The Ashen"            },
        };

        private static readonly int[] BossKillMilestones = { 3, 10, 25 };
        private static readonly string[] BossKillTitles = { "Boss Hunter", "World Breaker", "God Ender" };

        public static readonly Dictionary<string, string> BossDisplayNames = new Dictionary<string, string>
        {
            { "Eikthyr",     "Eikthyr"   },
            { "gd_king",     "The Elder" },
            { "Bonemass",    "Bonemass"  },
            { "Dragon",      "Moder"     },
            { "GoblinKing",  "Yagluth"   },
            { "SeekerQueen", "The Queen" },
            { "Fader",       "Fader"     },
        };

        public static string GetBossDisplayName(string prefab) =>
            BossDisplayNames.TryGetValue(prefab, out string n) ? n : prefab;

        public static void OnBossKill(string playerName, string prefab, PlayerRecord rec)
        {
            if (BossTitles.TryGetValue(prefab, out string bossTitle))
                EarnTitle(playerName, bossTitle, rec);

            bool allDone = BossTitles.Values.All(bt => rec.EarnedTitles.Contains(bt));
            if (allDone) EarnTitle(playerName, "Slayer of Gods", rec);

            for (int i = 0; i < BossKillMilestones.Length; i++)
                if (rec.BossKillsCredited == BossKillMilestones[i]) EarnTitle(playerName, BossKillTitles[i], rec);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // TITLE SYNC (1.8.0) — active titles on other players' nameplates, and
    // the on-screen line when a title is earned
    // ═════════════════════════════════════════════════════════════════════════
    // RavensCall_TitleSync_V1 goes to everybody (ZRoutedRpc.Everybody): it
    // also runs locally, so a listen host's own WhereTheCrowFlies gets it,
    // and a receiver with no handler for it (a vanilla client, a crow before
    // 1.3.0, this dedicated server itself) drops it without a log line
    // (ZRoutedRpc.HandleRoutedRPC, 1.0.15). This mod only sends it and never
    // registers it, like RavensCall_TitleReply_V1. WhereTheCrowFlies 1.3.0+
    // keeps a name -> title cache from it and draws the title on a line
    // under the name. Schema 1, one ZPackage:
    //   int    schemaVersion   1
    //   int    count           entries that follow, 0..256
    //   count x:
    //     string playerName    the record's Name
    //     string title         the ACTIVE title; "" = none (the receiver
    //                          removes the name)
    // Keyed by name because the records are (case-insensitively), and the
    // join hook runs before the joining player's character exists. Known,
    // accepted limit: a player joining under a name bound to another
    // account (PlatformBinding, "untrusted") can show that name's title too
    // (when the player who owns the name is or was online in the same
    // session, or an admin changed that name's title with `ravenscall
    // title` during it); the nameplate already shows the name itself.
    // Sent at every join (the whole online table, to every client, the
    // joining one included) and wherever a player's active title changes:
    // SetTitle, ClearTitle (the admin's `ravenscall title` goes through
    // both) and a player's first title activating itself in EarnTitle.
    internal static class TitleSync
    {
        internal const string RpcName = "RavensCall_TitleSync_V1";
        private const int SchemaVersion = 1;
        private const int MaxEntries = 256;

        private static bool CanSync() =>
            Plugin.SyncTitlesToNameplates.Value && Plugin.IsServer() && ZRoutedRpc.instance != null;

        public static void SendOne(string name, string activeTitle)
        {
            try
            {
                if (string.IsNullOrEmpty(name) || !CanSync()) return;
                var pkg = new ZPackage();
                pkg.Write(SchemaVersion);
                pkg.Write(1);
                pkg.Write(name);
                pkg.Write(activeTitle ?? "");
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Title sync failed: {ex.Message}"); }
        }

        // A lookup that never creates a record (PlayerRegistry.Get would),
        // the same one the admin's `ravenscall title` uses.
        private static PlayerRecord FindRecord(string name) =>
            PlayerRegistry.All.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

        // One entry per connected player (the list /api/state counts, a
        // listen host's own player included), each name once, "" entries
        // included so a stale cache heals. A peer on another account than
        // its name's is left out.
        public static void SendOnlineTable()
        {
            try
            {
                if (!CanSync()) return;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = new List<KeyValuePair<string, string>>();
                foreach (var p in Plugin.GetConnectedPlayers())
                {
                    if (entries.Count >= MaxEntries) break;
                    if (Patch_PlayerJoin.IsUntrusted(p.peerUid) || !seen.Add(p.name)) continue;
                    var rec = FindRecord(p.name);
                    entries.Add(new KeyValuePair<string, string>(rec?.Name ?? p.name, rec?.ActiveTitle ?? ""));
                }
                var pkg = new ZPackage();
                pkg.Write(SchemaVersion);
                pkg.Write(entries.Count);
                foreach (var e in entries) { pkg.Write(e.Key); pkg.Write(e.Value); }
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Title sync failed: {ex.Message}"); }
        }

        // ── The on-screen line: vanilla's own routed "ShowMessage" (MessageHud
        // registers it on every client; type 2 = Center), so it needs no
        // client mod. A Center message replaces the one before it at once
        // (MessageHud.ShowMessage sets the text, then fades it out over 4 s),
        // so the lines wait in a short queue and go out one after another,
        // 4 s apart (Tick, every frame from Patch_ZNetUpdate). Every title
        // gets its line. Titles come in bursts: one boss kill credits
        // everyone in radius and can earn each of them the boss's title,
        // "Slayer of Gods" and a boss-count title together. All the
        // EarnTitle calls of one credit loop run in the same frame and only
        // Tick sends, so players who earn the same title together share one
        // line ("A, B and C have earned the title: T", at most 10 names); a
        // player's different titles each get a line of their own. After a
        // credited boss kill the queue first waits for the game's own boss
        // death line to fade (HoldForBossMessage). At most 10 lines wait: a
        // title past that is not shown on screen, and a line that waited
        // over 50 s is dropped; either way its Chronicle/Discord line goes
        // out as before. ─────────────────────────────────────────────────
        private const float AnnounceGapSeconds = 4f;
        // MessageHud.ShowMessage fades a Center line out over 4 s
        // (CrossFadeAlpha(0f, 4f), 1.0.15); the hold adds a margin for the
        // boss's line on its way through the server.
        private const float CenterMessageFadeSeconds = 4f;
        private const float BossMessageHoldSeconds = CenterMessageFadeSeconds + 0.5f;
        private const float AnnounceMaxWaitSeconds = 50f;
        private const int MaxQueuedAnnouncements = 10;
        private const int MaxNamesPerLine = 10;
        private const int CenterMessage = 2; // MessageHud.MessageType.Center

        // One waiting line: one title and every player who earned it
        // before the line went out.
        private sealed class AnnounceLine
        {
            public float EnqueuedAt;
            public string Title;
            public List<string> Names;
        }

        private static readonly Queue<AnnounceLine> _announcements = new Queue<AnnounceLine>();
        private static float _nextAnnounceAt;

        // A new world session (Patch_ZNetAwake, server side): nothing waits
        // over from the one before, and the first line is not held back.
        internal static void Reset()
        {
            _announcements.Clear();
            _nextAnnounceAt = 0f;
        }

        // Valheim's own boss death line (BaseAI.OnDeath ->
        // MessageHud.MessageAll, Center, 1.0.15) leaves the client that owns
        // the boss right behind that client's kill report, so it reaches
        // every other client just after a line sent now and replaces it.
        // CombatCredit.CreditBossKill calls this before the kill's titles
        // are earned, so they go out once that line has faded.
        internal static void HoldForBossMessage()
        {
            float until = Time.realtimeSinceStartup + BossMessageHoldSeconds;
            if (_nextAnnounceAt < until) _nextAnnounceAt = until;
        }

        // Only queues, never sends: Tick does, so the names one credit loop
        // adds in one frame end up on one line.
        public static void AnnounceEarned(string playerName, string title)
        {
            try
            {
                if (!Plugin.AnnounceTitleEarnedInGame.Value || !Plugin.IsServer()) return;
                if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(title)) return;
                var line = _announcements.FirstOrDefault(l => string.Equals(l.Title, title, StringComparison.OrdinalIgnoreCase));
                if (line != null)
                {
                    if (line.Names.Contains(playerName, StringComparer.OrdinalIgnoreCase)) return;
                    if (line.Names.Count >= MaxNamesPerLine)
                    {
                        Plugin.Log.LogInfo($"[TheRavensCall] {playerName} is not named on screen for the title '{title}': its line already names {MaxNamesPerLine} players.");
                        return;
                    }
                    line.Names.Add(playerName);
                    return;
                }
                if (_announcements.Count >= MaxQueuedAnnouncements)
                {
                    Plugin.Log.LogInfo($"[TheRavensCall] {playerName}'s title '{title}' is not shown on screen: {MaxQueuedAnnouncements} on-screen lines are already waiting.");
                    return;
                }
                _announcements.Enqueue(new AnnounceLine { EnqueuedAt = Time.realtimeSinceStartup, Title = title, Names = new List<string> { playerName } });
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Title announcement failed: {ex.Message}"); }
        }

        internal static void Tick()
        {
            try
            {
                if (!Plugin.AnnounceTitleEarnedInGame.Value) { _announcements.Clear(); return; }
                if (_announcements.Count == 0) return;
                float now = Time.realtimeSinceStartup;
                while (_announcements.Count > 0 && now - _announcements.Peek().EnqueuedAt > AnnounceMaxWaitSeconds)
                {
                    var stale = _announcements.Dequeue();
                    Plugin.Log.LogInfo($"[TheRavensCall] The on-screen line for the title '{stale.Title}' is dropped: it waited more than {AnnounceMaxWaitSeconds:0} s.");
                }
                if (_announcements.Count == 0 || now < _nextAnnounceAt || ZRoutedRpc.instance == null) return;
                var next = _announcements.Dequeue();
                _nextAnnounceAt = now + AnnounceGapSeconds;
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", CenterMessage, LineText(next));
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Title announcement failed: {ex.Message}"); }
        }

        // "A has earned", "A and B have earned", "A, B and C have earned".
        private static string LineText(AnnounceLine line)
        {
            var names = line.Names.Select(Plain).ToList();
            int n = names.Count;
            string who = n == 1 ? names[0] : string.Join(", ", names.Take(n - 1)) + " and " + names[n - 1];
            return $"{who} {(n == 1 ? "has" : "have")} earned the title: {Plain(line.Title)}";
        }

        // MessageHud localizes the line ($word is a token) and draws it as
        // TextMeshPro rich text: <...> is a tag, and a backslash starts an
        // escape (TMP_Text turns a literal \n, \r, \t or \uXXXX into the real
        // character). TextMeshPro also breaks a line on U+2028 and U+2029,
        // which char.IsControl (category Cc only) misses. The titles are this
        // mod's own; the name comes from the player's client.
        private static string Plain(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (!char.IsControl(c) && c != '<' && c != '>' && c != '$' && c != '\\' && c != '\u2028' && c != '\u2029') sb.Append(c);
            return sb.ToString().Trim();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // NARRATIVE SYSTEM — dynamic message templates with token replacement
    // ═══════════════════════════════════════════════════════════════════════
    public static class NarrativeSystem
    {
        public static string Resolve(string template, Dictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(template)) return "";
            foreach (var kv in tokens)
                template = template.Replace("{" + kv.Key + "}", kv.Value ?? "");
            return template;
        }

        public static string Get(string eventType, Dictionary<string, string> tokens, string fallback)
        {
            if (Plugin.EnableNarrativeMode == null || !Plugin.EnableNarrativeMode.Value)
                return fallback;
            if (!Templates.TryGetValue(eventType, out var variants) || variants.Length == 0)
                return fallback;
            string template = variants[UnityEngine.Random.Range(0, variants.Length)];
            return Resolve(template, tokens);
        }

        private static readonly Dictionary<string, string[]> Templates = new Dictionary<string, string[]>
        {
            { "player_death", new[]
                {
                    "The saga of {name} was cut short by {cause}.",
                    "{name} met their end at the hands of {cause}.",
                    "{name} fell. {cause} claimed another soul.",
                    "Valhalla gains a warrior — {name} was taken by {cause}.",
                    "The world grows quieter. {name} has fallen to {cause}.",
                }
            },
            { "boss_kill", new[]
                {
                    "The age of {boss} has ended. {name} struck the killing blow.",
                    "{boss} has fallen. {name} will be remembered.",
                    "By the hand of {name}, {boss} breathes no more.",
                    "{name} has slain {boss}. The realm shudders.",
                    "A great shadow lifts — {name} has defeated {boss}.",
                }
            },
            { "kill_milestone", new[]
                {
                    "{name} has slain {count} enemies. The blood debt grows.",
                    "The kill count of {name} reaches {count}. Death follows in their wake.",
                    "{count} have fallen before {name}. The tally rises.",
                    "{name} — {count} kills. Yggdrasil trembles.",
                    "The sagas will speak of {name}: slayer of {count}.",
                }
            },
            { "death_milestone", new[]
                {
                    "{name} has died {count} times. The saga continues.",
                    "Death knows {name} well — {count} times now.",
                    "{name} falls again. Death count: {count}. They rise each time.",
                    "The Norns have snipped the thread of {name} {count} times.",
                    "{count} deaths. {name} refuses to stay dead.",
                }
            },
            { "biome_discovery", new[]
                {
                    "{name} has discovered {biome} for the first time.",
                    "Uncharted land: {name} sets foot in {biome}.",
                    "{name} crosses into {biome}. New dangers await.",
                    "The map grows larger — {name} has found {biome}.",
                    "{biome} reveals itself to {name} for the first time.",
                }
            },
            { "gear_tier", new[]
                {
                    "{name} has forged their first {gear} equipment.",
                    "The smithy rings — {name} now wields {gear} gear.",
                    "{gear} armor adorns {name}. A new chapter begins.",
                    "{name} advances: {gear} tier unlocked.",
                    "Clad in {gear}, {name} steps forward.",
                }
            },
            { "title_earned", new[]
                {
                    "{name} has earned the title: {title}!",
                    "A new name is spoken — {name} is now {title}.",
                    "The sagas add a verse: {name}, known as {title}.",
                    "{title} — a title earned by {name} through blood and saga.",
                    "Henceforth, {name} shall be known as {title}.",
                }
            },
        };
    }

    // ── Vanilla raid/event name → display text for the raid_start/raid_end
    // narration message only. WorldState.RaidType (the BarrkBOT-facing
    // field) always carries the raw vanilla name unchanged (§5); this map
    // is cosmetic, covers the standard biome raids, and falls back to the
    // raw name for anything unmapped (a modded event, a new Valheim raid).
    public static class RaidNames
    {
        private static readonly Dictionary<string, string> Display = new Dictionary<string, string>
        {
            { "army_eikthyr",  "Eikthyr's forces" },
            { "army_theelder", "The Elder's forces" },
            { "army_bonemass", "Bonemass's forces" },
            { "army_moder",    "Moder's forces" },
            { "army_goblin",   "The Fuling horde" },
            { "foresttrolls",  "Forest trolls" },
            { "skeletons",     "Skeletons" },
            { "blobs",         "Blobs" },
            { "wolves",        "Wolves" },
            { "surtlings",     "Surtlings" },
        };

        public static string Get(string rawName) =>
            !string.IsNullOrEmpty(rawName) && Display.TryGetValue(rawName, out string d) ? d : (rawName ?? "");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DISCORD WEBHOOK — fire-and-forget, HttpWebRequest only (no HttpClient)
    // ═══════════════════════════════════════════════════════════════════════
    public static class DiscordWebhook
    {
        private static readonly Dictionary<string, int> Colors = new Dictionary<string, int>
        {
            { "player_death",    15158332 }, // red
            { "death_milestone", 15158332 }, // red
            { "boss_kill",       16766720 }, // gold
            { "title_earned",    10181046 }, // purple
            { "kill_milestone",   3447003 }, // blue
            { "biome_discovery",  3066993 }, // green
            { "gear_tier",        3066993 }, // green
        };

        public static void Send(string eventType, string message)
        {
            try
            {
                string url = Plugin.DiscordWebhookUrl?.Value?.Trim();
                if (string.IsNullOrEmpty(url)) return;
                if (eventType == "startup" || eventType == "shutdown" || eventType == "session_summary") return;

                int color = Colors.TryGetValue(eventType, out int c) ? c : 9807270;
                string label = eventType.Replace("_", " ");
                // Character names and death causes are player-controlled text
                // that lands inside this embed. Discord renders masked links
                // ([text](url)) and code spans in embed descriptions, so a
                // name shaped like one posts a clickable link under the bot's
                // identity (review 2026-09-15). Backslash-escape the three
                // characters that make those, then JSON-escape as before.
                string safeMsg = message.Replace("[", "\\[").Replace("]", "\\]").Replace("`", "\\`");
                string escapedMsg = safeMsg.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
                string payload =
                    "{\"embeds\":[{" +
                    "\"title\":\"" + label + "\"," +
                    "\"description\":\"" + escapedMsg + "\"," +
                    "\"color\":" + color +
                    "}]}";

                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                        req.Method = "POST";
                        req.ContentType = "application/json";
                        req.Timeout = 5000;

                        byte[] data = System.Text.Encoding.UTF8.GetBytes(payload);
                        req.ContentLength = data.Length;

                        using (var stream = req.GetRequestStream())
                            stream.Write(data, 0, data.Length);

                        using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                            Plugin.Log.LogInfo("[TheRavensCall] Discord webhook sent: " + (int)resp.StatusCode);
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] Discord webhook failed: " + ex.Message); }
                });
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] Discord webhook error: " + ex.Message); }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CHRONICLE — JSONL format, one JSON object per line
    // ═══════════════════════════════════════════════════════════════════════
    public static class Chronicle
    {
        private static System.IO.StreamWriter _writer;
        private static readonly object _lock = new object();
        private static string _logPath;
        private static string _logDir;

        public static string CurrentLogPath => _logPath;
        public static string LogDir => _logDir;

        public static void Init(string overrideDir = null)
        {
            try
            {
                // Open the new writer FIRST, close/replace the old one only
                // once it succeeds — StartSeason/EndSeason/SeasonSystem.Init
                // all re-point the Chronicle at a new directory by calling
                // Init again, and a bad re-point (e.g. a season name that
                // collapses to an invalid path component) used to close the
                // previous writer before the new one was known to open,
                // leaving _writer null and silently losing every Chronicle
                // line for the rest of the process (Storm10 "TestSeason."
                // review 2026-09-21). On failure below, _writer/_logDir/
                // _logPath are left exactly as they were, so the previous
                // writer keeps logging.
                string newDir = overrideDir ?? Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "Chronicle");
                Directory.CreateDirectory(newDir);
                string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
                string newPath = Path.Combine(newDir, $"TheRavensCall_Chronicle_{date}.log");

                // Re-pointing at the file the current writer already has open
                // fails outright (it holds the handle with FileShare.Read), so
                // a redundant Init(sameDir) — e.g. starting the same season
                // name twice, or restarting with the same season still active
                // — would log the very "Chronicle init failed" warning this
                // fix was meant to eliminate, even though nothing is wrong
                // (review 2026-09-21). Same path + already open == nothing to do.
                if (_writer != null && string.Equals(newPath, _logPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logDir = newDir;
                    return;
                }

                var newWriter = new System.IO.StreamWriter(newPath, append: true) { AutoFlush = true };

                var oldWriter = _writer;
                _writer = newWriter;
                _logDir = newDir;
                _logPath = newPath;
                try { oldWriter?.Close(); } catch { }

                Plugin.Log.LogInfo($"[TheRavensCall] Chronicle: {_logPath}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Chronicle init failed: {ex.Message}"); }
        }

        public static void Write(string player, string eventType, string message, Dictionary<string, string> data)
        {
            // Restructured 2026-09-21 (§4.1): the line is built and handed to
            // EventFeed unconditionally, on every narrated event; only the
            // disk write — and the day-rotation it needs — stays behind the
            // EnableChronicleLog gate, so the dashboard feed keeps working
            // with the Chronicle log turned off.
            lock (_lock)
            {
                try
                {
                    string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    string day = data != null && data.ContainsKey("day") ? data["day"] : "0";
                    string online = data != null && data.ContainsKey("online") ? data["online"] : "0";
                    string detail = data != null && data.ContainsKey("detail") ? data["detail"] : "";

                    string jsonLine =
                        "{" +
                        "\"timestamp_utc\":\"" + ts + "\"," +
                        "\"day\":" + day + "," +
                        "\"online_count\":" + online + "," +
                        "\"event_type\":\"" + EscapeJson(eventType) + "\"," +
                        "\"player_name\":\"" + EscapeJson(player ?? "") + "\"," +
                        "\"message\":\"" + EscapeJson(message ?? "") + "\"," +
                        "\"detail\":\"" + EscapeJson(detail) + "\"" +
                        "}";

                    EventFeed.Append(jsonLine);

                    if (Plugin.EnableChronicleLog != null && !Plugin.EnableChronicleLog.Value) return;

                    string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    // Only the file name carries the log's date (1.4.2): a
                    // season folder or an install path holding today's date
                    // used to suppress the rollover for that whole day.
                    if (_logPath != null && !Path.GetFileName(_logPath).Contains(today)) Init(_logDir);
                    _writer?.WriteLine(jsonLine);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Chronicle write failed: {ex.Message}"); }
            }
        }

        public static void WriteRaw(string jsonLine)
        {
            if (Plugin.EnableChronicleLog != null && !Plugin.EnableChronicleLog.Value) return;
            lock (_lock)
            {
                try { _writer?.WriteLine(jsonLine); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Chronicle raw write failed: {ex.Message}"); }
            }
        }

        // Leaves the class in its initial state (1.4.1): with _writer left
        // non-null after a Close, Init's same-path short-circuit would keep
        // a disposed writer.
        public static void Close() { lock (_lock) { try { _writer?.Close(); } catch { } _writer = null; _logPath = null; } }

        // §4.8: delegates to Companion.Esc, which additionally escapes \t and
        // strips other control characters — a tab or stray control char in a
        // lore.txt line used to produce a Chronicle line that was not valid
        // JSON, which would break JSON.parse of the whole feed.
        private static string EscapeJson(string s) => Companion.Esc(s);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EVENT FEED — bounded, in-memory, newest-first buffer of Chronicle
    // lines, served by /api/activity. INVARIANT: EventFeed never calls back
    // into Chronicle or Plugin.Narrate/FireEvent, including from its own
    // warning paths — it logs through Plugin.Log only. Chronicle.Write is
    // EventFeed's one and only caller (via Append), so a call back the other
    // way would be a cycle. Own lock, independent of Chronicle._lock — every
    // caller today is main-thread only (Chronicle.Write, the poll tick, boot
    // and shutdown), so it's uncontended in practice, but that's an
    // invariant a future patch could easily break without a lock of its own.
    // ═══════════════════════════════════════════════════════════════════════
    public static class EventFeed
    {
        private const int MaxLineChars = 2048;
        private static readonly object _lock = new object();
        private static readonly List<string> _entries = new List<string>(); // newest first
        private static bool _dirty = false;
        private static bool _warnedOversizeLine = false;

        private static string FeedPath => Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "event_feed.json");

        private static int Capacity()
        {
            int cap = Plugin.EventFeedCapacity != null ? Plugin.EventFeedCapacity.Value : 200;
            return Math.Max(0, Math.Min(1000, cap));
        }

        // Always called from Chronicle.Write, inside Chronicle._lock, for
        // every one of the 17 narrated event types (§4.1). Capacity 0 is off.
        public static void Append(string jsonLine)
        {
            try
            {
                if (Capacity() <= 0 || string.IsNullOrEmpty(jsonLine)) return;

                if (jsonLine.Length > MaxLineChars)
                {
                    if (!_warnedOversizeLine)
                    {
                        _warnedOversizeLine = true;
                        Plugin.Log.LogWarning($"[TheRavensCall] EventFeed: a Chronicle line exceeded {MaxLineChars} characters and was dropped from the feed (further oversize lines this run are silent).");
                    }
                    return;
                }

                lock (_lock)
                {
                    _entries.Insert(0, jsonLine);
                    int cap = Capacity();
                    while (_entries.Count > cap) _entries.RemoveAt(_entries.Count - 1);
                    _dirty = true;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] EventFeed.Append error: {ex.Message}"); }
        }

        public static string ToJsonArray()
        {
            lock (_lock) { return "[" + string.Join(",", _entries) + "]"; }
        }

        // Loads event_feed.json (if any) and merges it under whatever is
        // already in _entries — a raid restored at world load can narrate,
        // and therefore Append, before this runs (§4.5's boot ordering), and
        // those rows are newer than anything on disk. Capacity 0 clears and
        // deletes the file instead, so a later re-enable starts empty rather
        // than resurrecting stale rows.
        public static void Init()
        {
            try
            {
                if (Capacity() <= 0)
                {
                    lock (_lock) { _entries.Clear(); _dirty = false; }
                    try { if (File.Exists(FeedPath)) File.Delete(FeedPath); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] EventFeed: could not delete {FeedPath}: {ex.Message}"); }
                    return;
                }

                if (!File.Exists(FeedPath)) return;
                string raw = File.ReadAllText(FeedPath);
                List<string> loaded = Companion.JsonGetArray(raw, "events");

                var sane = new List<string>();
                int dropped = 0;
                foreach (var e in loaded)
                {
                    // The house heuristic (PlayerRegistry.cs's own load path):
                    // JsonGetArray has no failure signal of its own, so each
                    // element is sanity-checked before it's trusted.
                    if (e != null && e.StartsWith("{\"timestamp_utc\":\"") && e.EndsWith("}"))
                        sane.Add(e);
                    else
                        dropped++;
                }
                if (dropped > 0)
                    Plugin.Log.LogWarning($"[TheRavensCall] EventFeed: dropped {dropped} malformed row(s) from event_feed.json.");

                lock (_lock)
                {
                    _entries.AddRange(sane);
                    int cap = Capacity();
                    while (_entries.Count > cap) _entries.RemoveAt(_entries.Count - 1);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] EventFeed.Init error: {ex.Message}"); }
        }

        // Called from the poll tick (outside the EnableHttpServer guard, so
        // the feed persists even with the HTTP server off) and once more
        // from Plugin.OnDestroy before Chronicle.Close(). No-op when clean
        // or when the feed is off.
        public static void Save()
        {
            try
            {
                if (Capacity() <= 0) return;
                string json;
                lock (_lock)
                {
                    if (!_dirty) return;
                    json = "{\"saved_at\":\"" + DateTime.UtcNow.ToString("o") + "\",\"events\":" + ToJsonArray() + "}";
                }
                Directory.CreateDirectory(Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall"));
                PlayerRegistry.AtomicWrite(FeedPath, json);
                // Clear only after the write succeeds — if AtomicWrite throws
                // (full disk, an AV/backup handle on the .tmp, a permission
                // error), _dirty must stay set so the next poll tick's Save()
                // retries instead of silently dropping this batch of events.
                lock (_lock) { _dirty = false; }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] EventFeed.Save error: {ex.Message}"); }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SESSION TRACKER — server-only, in-memory, no persistence
    // ═══════════════════════════════════════════════════════════════════════
    public static class SessionTracker
    {
        private static DateTime _sessionStart;
        private static readonly Dictionary<string, int> _kills = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _deaths = new Dictionary<string, int>();
        private static readonly List<string> _bossesKilled = new List<string>();
        private static readonly List<string> _titlesEarned = new List<string>();
        private static readonly List<string> _biomes = new List<string>();
        private static string _firstBossKill = null;

        public static void Begin()
        {
            _sessionStart = DateTime.UtcNow;
            _kills.Clear(); _deaths.Clear();
            _bossesKilled.Clear(); _titlesEarned.Clear(); _biomes.Clear();
            _firstBossKill = null;
            Plugin.Log.LogInfo("[TheRavensCall] SessionTracker started.");
        }

        public static void Track(string eventType, string playerName, string detail)
        {
            if (string.IsNullOrEmpty(playerName)) return;
            try
            {
                switch (eventType)
                {
                    case "kill_milestone":
                        if (int.TryParse(detail, out int km)) _kills[playerName] = km;
                        break;
                    // death_milestone intentionally does not touch _deaths —
                    // "player_death" below fires for every death including
                    // milestone ones and is the single source of truth for
                    // the session death count, so this doesn't double-count.
                    case "boss_kill":
                        if (!string.IsNullOrEmpty(detail) && !_bossesKilled.Contains(detail)) _bossesKilled.Add(detail);
                        if (_firstBossKill == null) _firstBossKill = detail;
                        break;
                    case "title_earned":
                        if (!string.IsNullOrEmpty(detail) && !_titlesEarned.Contains(detail)) _titlesEarned.Add(detail);
                        break;
                    case "biome_discovery":
                        if (!string.IsNullOrEmpty(detail) && !_biomes.Contains(detail)) _biomes.Add(detail);
                        break;
                    case "player_death":
                        if (!_deaths.ContainsKey(playerName)) _deaths[playerName] = 0;
                        _deaths[playerName]++;
                        break;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] SessionTracker.Track error: {ex.Message}"); }
        }

        public static void WriteSessionSummary()
        {
            try
            {
                var now = DateTime.UtcNow;
                int duration = (int)(now - _sessionStart).TotalSeconds;

                string topKiller = ""; int topKills = 0;
                foreach (var kv in _kills) if (kv.Value > topKills) { topKills = kv.Value; topKiller = kv.Key; }

                string mostDeaths = ""; int maxDeaths = 0;
                foreach (var kv in _deaths) if (kv.Value > maxDeaths) { maxDeaths = kv.Value; mostDeaths = kv.Key; }

                string rarestTitle = _titlesEarned.Count > 0 ? _titlesEarned[_titlesEarned.Count - 1] : "";

                string bossArr = "[" + string.Join(",", _bossesKilled.ConvertAll(b => "\"" + Esc(b) + "\"")) + "]";
                string biomeArr = "[" + string.Join(",", _biomes.ConvertAll(b => "\"" + Esc(b) + "\"")) + "]";
                string titleArr = "[" + string.Join(",", _titlesEarned.ConvertAll(t => "\"" + Esc(t) + "\"")) + "]";

                string nowStr = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string startStr = _sessionStart.ToString("yyyy-MM-ddTHH:mm:ssZ");

                string json =
                    "{" +
                    "\"timestamp_utc\":\"" + nowStr + "\"," +
                    "\"event_type\":\"session_summary\"," +
                    "\"session_start\":\"" + startStr + "\"," +
                    "\"session_end\":\"" + nowStr + "\"," +
                    "\"duration_seconds\":" + duration + "," +
                    "\"top_killer\":\"" + Esc(topKiller) + "\"," +
                    "\"most_deaths\":\"" + Esc(mostDeaths) + "\"," +
                    "\"first_boss_kill\":\"" + Esc(_firstBossKill ?? "") + "\"," +
                    "\"rarest_title_earned\":\"" + Esc(rarestTitle) + "\"," +
                    "\"bosses_killed\":" + bossArr + "," +
                    "\"biomes_discovered\":" + biomeArr + "," +
                    "\"titles_earned\":" + titleArr +
                    "}";

                Chronicle.WriteRaw(json);
                Plugin.Log.LogInfo("[TheRavensCall] Session summary written to Chronicle.");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] SessionTracker.WriteSessionSummary error: {ex.Message}"); }
        }

        private static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SEASON SYSTEM — console commands + metadata + archive
    // ═══════════════════════════════════════════════════════════════════════
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    public static class Patch_Terminal
    {
        private static void Postfix()
        {
            // onlyServer + remoteCommand: on the dedicated server console the
            // command runs directly (IsValid is true there); typed on a client
            // that has the WhereTheCrowFlies routing stub, Terminal.TryRunCommand
            // sends it through ZNet.RemoteCommand, the server checks the admin
            // list in RPC_RemoteCommand, and this action runs here. Without
            // the stub a client never knew the name at all (review 2026-09-15).
            new Terminal.ConsoleCommand("ravenscall", "Usage: ravenscall season start [name] | ravenscall season end | ravenscall title <player> [<title>|clear] | ravenscall unbind <player>", args =>
            {
                if (args.Length >= 2 && args[1] == "unbind")
                {
                    // 1.8.0: release a name from the account it is bound to
                    // (PlatformBinding), e.g. a player who moved from Steam
                    // to Xbox. The next account to join under it takes it.
                    var utoks = args.Args.Skip(2).Where(t => !string.IsNullOrEmpty(t)).ToList();
                    if (utoks.Count < 1) { args.Context?.AddString("[TheRavensCall] Usage: ravenscall unbind <player>"); return; }
                    string uname = string.Join(" ", utoks);
                    args.Context?.AddString(PlatformBinding.Unbind(uname)
                        ? $"[TheRavensCall] {uname} is no longer bound to an account; the next one to join under that name takes it."
                        : $"[TheRavensCall] {uname} is not bound to an account.");
                    return;
                }
                if (args.Length >= 2 && args[1] == "title")
                {
                    // Admin path (1.7.0): no client mod needed — same rules as
                    // the player's own /title, via TitleSystem's shared
                    // List/Set/Clear. The player need not be online, but the
                    // name must already exist in the registry (PlayerRegistry.Get
                    // would silently create a record for a typo'd name, which
                    // a lookup against .All avoids).
                    // ConsoleEventArgs splits on single spaces and keeps the
                    // empty tokens, so a trailing or doubled space would
                    // otherwise read as an empty title or a two-space one
                    // (review 2026-09-22).
                    var toks = args.Args.Skip(2).Where(t => !string.IsNullOrEmpty(t)).ToList();
                    if (toks.Count < 1) { args.Context?.AddString("[TheRavensCall] Usage: ravenscall title <player> [<title>|clear]"); return; }
                    string targetName = toks[0];
                    var rec = PlayerRegistry.All.FirstOrDefault(r => string.Equals(r.Name, targetName, StringComparison.OrdinalIgnoreCase));
                    if (rec == null) { args.Context?.AddString($"[TheRavensCall] No such player: {targetName}"); return; }

                    (byte kind, string text) reply;
                    if (toks.Count == 1)
                        reply = TitleSystem.ListTitles(rec);
                    else if (toks.Count == 2 && string.Equals(toks[1], "clear", StringComparison.OrdinalIgnoreCase))
                        reply = TitleSystem.ClearTitle(rec.Name, rec);
                    else
                        reply = TitleSystem.SetTitle(rec.Name, rec, string.Join(" ", toks.Skip(1)));
                    // TitleSystem's strings are written second-person for the
                    // player themselves ("Your titles", "You are now …"); an
                    // admin querying someone else needs the target named so
                    // consecutive lookups aren't indistinguishable.
                    args.Context?.AddString($"[TheRavensCall] {rec.Name}: {reply.text}");
                    return;
                }
                if (args.Length < 2 || args[1] != "season") { args.Context?.AddString("[TheRavensCall] Usage: ravenscall season start [name] | ravenscall season end | ravenscall title <player> [<title>|clear] | ravenscall unbind <player>"); return; }
                if (args.Length >= 3 && args[2] == "start")
                {
                    string sName = args.Length >= 4 ? args[3] : "Season_" + DateTime.UtcNow.ToString("yyyyMMdd");
                    string running = SeasonSystem.GetCurrentSeasonName();
                    if (SeasonSystem.StartSeason(sName))
                        // Echo back what was actually stored (StartSeason trims
                        // trailing periods/whitespace), not the raw typed arg.
                        args.Context?.AddString("[TheRavensCall] Season started: " + SeasonSystem.GetCurrentSeasonName());
                    else if (!string.IsNullOrEmpty(running))
                        // 1.6.1: a running season is never replaced in place.
                        args.Context?.AddString("[TheRavensCall] Season '" + running + "' is still running — run `ravenscall season end` first.");
                    else
                        args.Context?.AddString("[TheRavensCall] Season not started — see the server log.");
                }
                else if (args.Length >= 3 && args[2] == "end")
                {
                    SeasonSystem.EndSeason();
                    args.Context?.AddString("[TheRavensCall] Season ended.");
                }
                else
                {
                    args.Context?.AddString("[TheRavensCall] Usage: ravenscall season start [name] | ravenscall season end | ravenscall title <player> [<title>|clear] | ravenscall unbind <player>");
                }
            }, onlyServer: true, remoteCommand: true);
        }
    }

    public static class SeasonSystem
    {
        private static string _currentSeason = null;
        private static DateTime _seasonStart = DateTime.MinValue;
        // Remembered across a restart (§4.4): SaveMeta only used to emit these
        // on the ending call, so the very next StartSeason erased them and a
        // restart never read them back.
        private static string _lastEnded = null;
        private static string _lastEndedAt = null;
        // The season-start counters every standings row subtracts from.
        // Keyed by player name, OrdinalIgnoreCase like PlayerRegistry itself.
        // Kept as an array-of-rows file, not a name-keyed map — see
        // SnapshotBaseline for why.
        private static Dictionary<string, Baseline> _baseline = new Dictionary<string, Baseline>(StringComparer.OrdinalIgnoreCase);
        private static string _standingsSince = null;

        private static string MetaPath => Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "seasons.json");
        private static string BaselinePath => Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "season_baseline.json");

        // Windows silently drops a trailing period or space from a path
        // component — StartSeason("TestSeason.") let Directory.CreateDirectory
        // make "TestSeason." happily, but the StreamWriter opened against it
        // then failed, because the OS resolved the path to "TestSeason"
        // without ever telling CreateDirectory that (Storm10 review
        // 2026-09-21). Trim the same way up front so what's displayed
        // matches what Windows would have done to it anyway — a display
        // name can never legitimately end with a period/space there, so a
        // trailing one only ever reads as a typo.
        private static string TrimTrailingPeriodsAndWhitespace(string name) =>
            string.IsNullOrEmpty(name) ? name : name.TrimEnd(' ', '\t', '\r', '\n', '.');

        // Windows reserved device names cannot be a path component at all —
        // same list PlayerRegistry.ReservedFileNames guards for player files
        // (review 2026-09-15), reused here because a season named NUL hit
        // the identical DirectoryNotFoundException on Directory.CreateDirectory
        // (review 2026-09-21).
        private static readonly HashSet<string> ReservedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        // Same approach as PlayerRegistry.SanitizeForFile (invalid filename
        // chars -> '_', reserved device names -> '_' + name), plus the trim
        // above, plus a fallback for a name that sanitizes away to nothing
        // (e.g. "..." or all-invalid chars). Used for the *folder* only —
        // the season's own display name is kept as typed (just trimmed),
        // see TrimTrailingPeriodsAndWhitespace.
        private const int MaxFolderNameLength = 64;

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "season";
            char[] chars = name.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            string s = TrimTrailingPeriodsAndWhitespace(new string(chars));
            // 1.4.1: keep the folder well inside MAX_PATH — the season log's
            // full path adds ~90 chars below the BepInEx config folder.
            if (s.Length > MaxFolderNameLength)
            {
                s = s.Substring(0, MaxFolderNameLength);
                if (char.IsHighSurrogate(s[s.Length - 1])) s = s.Substring(0, s.Length - 1);
                s = TrimTrailingPeriodsAndWhitespace(s);
            }
            if (string.IsNullOrEmpty(s)) return "season";
            return ReservedFolderNames.Contains(s) ? "_" + s : s;
        }

        // Single source of truth for the archive path — used by StartSeason
        // and by Init's mid-season re-point, so a restart resolves the same
        // folder a live StartSeason would have (Storm10 review 2026-09-21).
        private static string ArchiveDir(string seasonName) =>
            Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "Chronicle", "seasons", SanitizeFolderName(seasonName));

        // One string field of seasons.json, decoded: finds "<key>" : "
        // (whitespace allowed, as the old regex did) and then reads to the
        // closing quote honouring EscJ's backslash escapes. null when absent.
        private static string MetaString(string json, string key)
        {
            if (json == null) return null;
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\"\\s*:\\s*\"");
            if (!m.Success) return null;
            int i = m.Index + m.Length;
            var sb = new System.Text.StringBuilder();
            bool esc = false;
            bool closed = false;
            while (i < json.Length)
            {
                char c = json[i++];
                if (esc) { sb.Append(c); esc = false; }
                else if (c == '\\') esc = true;
                else if (c == '"') { closed = true; break; }
                else sb.Append(c);
            }
            // Fail closed (1.4.2): a value with no closing quote (a torn
            // write) reads as absent, and so does one whose closing quote is
            // followed by anything but a separator — a quote dropped by hand
            // makes the next key's opening quote look like the value's end,
            // e.g. "current_season":"Winter War, "season_start":... — so
            // Init sees "no season" instead of a fragment as a season name.
            if (!closed) return null;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i < json.Length && json[i] != ',' && json[i] != '}') return null;
            return sb.ToString();
        }

        private class Baseline
        {
            public int Kills;
            public int Deaths;
            public int BossKills;
            public long PlaytimeSeconds;
        }

        public static string GetCurrentSeasonName() => _currentSeason ?? "";

        public static void Init()
        {
            try
            {
                // No file means no season — including for a process that
                // already ran a world session with one (1.4.2): the fields
                // are process-wide statics, and a listen server hosting
                // again after seasons.json was deleted must not keep the old
                // season. (A file that exists but cannot be read is a
                // different case: the catch below keeps what is loaded.)
                if (!File.Exists(MetaPath)) { ResetSeasonState(); return; }
                string raw = File.ReadAllText(MetaPath);
                // 1.4.1: the four strings are read back through an
                // escape-aware reader. SaveMeta writes them through EscJ,
                // and the old regex handed the still-escaped text to
                // ArchiveDir, so a name holding a backslash or a quote
                // resolved a different folder after a restart than the live
                // season start had. The loaded name is trimmed the same way
                // StartSeason trims what was typed, so the name shown and the
                // folder used agree on the upgrade path too.
                string cs = MetaString(raw, "current_season");
                // MetaString reads "" for the legitimate no-season file and
                // null only when the key is absent or its value is damaged
                // (1.4.2): say so, rather than let a season vanish silently.
                if (cs == null && raw.Contains("current_season"))
                    Plugin.Log.LogWarning("[TheRavensCall] seasons.json holds a damaged current_season value; treating it as no active season.");
                _currentSeason = string.IsNullOrEmpty(cs) ? null : TrimTrailingPeriodsAndWhitespace(cs);
                if (string.IsNullOrEmpty(_currentSeason)) _currentSeason = null;
                string ss = MetaString(raw, "season_start");
                // §4.4 fix: a bare TryParse read a "...Z" string back as local
                // time, and SaveMeta/SeasonJson then re-stamped that local
                // value with a "Z" — a two-hour drift on a UTC+2 host on every
                // mid-season boot.
                if (!string.IsNullOrEmpty(ss)) DateTime.TryParse(ss, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out _seasonStart);
                string le = MetaString(raw, "last_ended");
                if (!string.IsNullOrEmpty(le)) _lastEnded = le;
                string lea = MetaString(raw, "last_ended_at");
                if (!string.IsNullOrEmpty(lea)) _lastEndedAt = lea;

                if (!string.IsNullOrEmpty(_currentSeason))
                {
                    Plugin.Log.LogInfo("[TheRavensCall] Active season loaded: " + _currentSeason);

                    if (!LoadBaseline())
                    {
                        // A 1.4.0 upgrade landing mid-season: no baseline file
                        // exists yet. Snapshot now rather than report every
                        // player's lifetime total as their season score.
                        // Guarded on its own (1.4.1) so a failed baseline
                        // write cannot skip the Chronicle re-point below.
                        try
                        {
                            SnapshotBaseline(_currentSeason, DateTime.UtcNow);
                            Plugin.Log.LogInfo("[TheRavensCall] no usable season_baseline.json for active season " +
                                _currentSeason + "; standings count from this restart (" + _standingsSince + ").");
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem: season_baseline.json could not be written: " +
                                ex.Message + " (standings count from this restart in memory; the next boot retries).");
                        }
                    }

                    // §4.6: re-point the Chronicle at the season's archive
                    // folder, same sanitized path ArchiveDir/StartSeason use —
                    // Init otherwise leaves it in the default folder after a
                    // mid-season restart.
                    Chronicle.Init(ArchiveDir(_currentSeason));
                }
            }
            catch (Exception ex)
            {
                // A read that failed is not a file that says "no season":
                // keep whatever is loaded unless the file is truly gone.
                bool missing = !File.Exists(MetaPath);
                if (missing) ResetSeasonState();
                Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.Init error: " + ex.Message +
                    (missing ? " (treating it as no active season)" : " (keeping the season already loaded)"));
            }
        }

        // "No file, no season, no history": every process-wide season static.
        private static void ResetSeasonState()
        {
            _currentSeason = null;
            _seasonStart = DateTime.MinValue;
            _baseline.Clear();
            _standingsSince = null;
            _lastEnded = null;
            _lastEndedAt = null;
        }

        // Array of rows, not a name-keyed map: the existing map parser splits
        // each pair on the first colon, which breaks a name like "Od:in", and
        // ExtractJsonField takes the first "<key>":  anywhere in the document,
        // so a player literally named "kills" would be matched before the
        // real key (§4.4).
        private static bool LoadBaseline()
        {
            try
            {
                if (!File.Exists(BaselinePath)) return false;
                string raw = File.ReadAllText(BaselinePath);
                // The file names the season it was taken for (1.4.2): a
                // baseline left behind by an earlier season — EndSeason's
                // delete failed, or the server died before it ran — must not
                // be subtracted from the new season's counters. Returning
                // false drops Init into its snapshot-now branch. Compared
                // trimmed, because a 1.4.0 baseline holds the name exactly as
                // typed (e.g. "TestSeason.") while 1.4.1+ trims it at load.
                string forSeason = Companion.JsonGetString(raw, "season");
                if (!string.IsNullOrEmpty(forSeason) &&
                    !string.Equals(TrimTrailingPeriodsAndWhitespace(forSeason), _currentSeason, StringComparison.Ordinal))
                {
                    Plugin.Log.LogWarning("[TheRavensCall] season_baseline.json belongs to season " + forSeason +
                        ", not " + _currentSeason + "; taking a fresh baseline now.");
                    return false;
                }
                string takenAt = Companion.JsonGetString(raw, "taken_at");
                var rows = Companion.JsonGetArray(raw, "rows");

                var map = new Dictionary<string, Baseline>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    string name = Companion.JsonGetString(row, "name");
                    if (string.IsNullOrEmpty(name)) continue;
                    map[name] = new Baseline
                    {
                        Kills = Companion.JsonGetInt(row, "kills"),
                        Deaths = Companion.JsonGetInt(row, "deaths"),
                        BossKills = Companion.JsonGetInt(row, "boss_kills"),
                        PlaytimeSeconds = Companion.JsonGetLong(row, "playtime_seconds"),
                    };
                }
                _baseline = map;
                _standingsSince = !string.IsNullOrEmpty(takenAt) ? takenAt : _seasonStart.ToString("yyyy-MM-ddTHH:mm:ssZ");
                return true;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem: season_baseline.json load failed: " + ex.Message); return false; }
        }

        private static void SnapshotBaseline(string seasonName, DateTime takenAt)
        {
            var map = new Dictionary<string, Baseline>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<string>();
            foreach (var rec in PlayerRegistry.All)
            {
                var b = new Baseline
                {
                    Kills = rec.TotalKillsLifetime,
                    Deaths = rec.TotalDeathsLifetime,
                    BossKills = rec.BossKillsCredited,
                    PlaytimeSeconds = rec.PlaytimeSecondsLifetime,
                };
                map[rec.Name] = b;
                rows.Add("{\"name\":\"" + Companion.Esc(rec.Name) + "\",\"kills\":" + b.Kills + ",\"deaths\":" + b.Deaths +
                    ",\"boss_kills\":" + b.BossKills + ",\"playtime_seconds\":" + b.PlaytimeSeconds + "}");
            }

            _baseline = map;
            _standingsSince = takenAt.ToString("yyyy-MM-ddTHH:mm:ssZ");

            string json = "{\"season\":\"" + EscJ(seasonName) + "\",\"taken_at\":\"" + _standingsSince + "\",\"rows\":[" + string.Join(",", rows) + "]}";
            Directory.CreateDirectory(Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall"));
            PlayerRegistry.AtomicWrite(BaselinePath, json);
        }

        // The exact §2 "season" object as a "season":{...} fragment — the
        // caller (Companion.BuildActivityCache) wraps it with generated_at
        // and events. Deltas are Math.Max(0, current - baseline); a player
        // absent from the baseline (joined after the season started) reads 0
        // for every counter, which is correct.
        internal static string SeasonJson()
        {
            try
            {
                bool active = !string.IsNullOrEmpty(_currentSeason);
                string name = active ? _currentSeason : "";
                string startedAt = active ? _seasonStart.ToString("yyyy-MM-ddTHH:mm:ssZ") : null;
                string standingsSince = active ? (_standingsSince ?? startedAt) : null;

                var rows = new List<string>();
                if (active)
                {
                    foreach (var rec in PlayerRegistry.All)
                    {
                        _baseline.TryGetValue(rec.Name, out Baseline b);
                        int kills = Math.Max(0, rec.TotalKillsLifetime - (b?.Kills ?? 0));
                        int deaths = Math.Max(0, rec.TotalDeathsLifetime - (b?.Deaths ?? 0));
                        int bossKills = Math.Max(0, rec.BossKillsCredited - (b?.BossKills ?? 0));
                        long playtime = Math.Max(0L, rec.PlaytimeSecondsLifetime - (b?.PlaytimeSeconds ?? 0L));
                        rows.Add("{\"name\":\"" + Companion.Esc(rec.Name) + "\",\"kills\":" + kills + ",\"deaths\":" + deaths +
                            ",\"boss_kills\":" + bossKills + ",\"playtime_seconds\":" + playtime + "}");
                    }
                }

                string startedAtJson = startedAt != null ? "\"" + startedAt + "\"" : "null";
                string standingsSinceJson = standingsSince != null ? "\"" + standingsSince + "\"" : "null";
                string lastEndedJson = !string.IsNullOrEmpty(_lastEnded) ? "\"" + EscJ(_lastEnded) + "\"" : "null";
                string lastEndedAtJson = !string.IsNullOrEmpty(_lastEndedAt) ? "\"" + EscJ(_lastEndedAt) + "\"" : "null";

                return "\"season\":{" +
                    "\"active\":" + Companion.B(active) + "," +
                    "\"name\":\"" + EscJ(name) + "\"," +
                    "\"started_at\":" + startedAtJson + "," +
                    "\"standings_since\":" + standingsSinceJson + "," +
                    "\"last_ended\":" + lastEndedJson + "," +
                    "\"last_ended_at\":" + lastEndedAtJson + "," +
                    "\"standings\":[" + string.Join(",", rows) + "]" +
                    "}";
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.SeasonJson error: " + ex.Message);
                return "\"season\":{\"active\":false,\"name\":\"\",\"started_at\":null,\"standings_since\":null,\"last_ended\":null,\"last_ended_at\":null,\"standings\":[]}";
            }
        }

        // Returns false, with nothing changed on disk, when a season is already
        // running: through 1.6.0 a second `season start` replaced the active season
        // in place — no summary, no season_end line, last_ended never set, only
        // the Chronicle folder and the baseline switched to the new name. The
        // console handler turns the false into a message naming the running
        // season and `ravenscall season end` as the next step (1.6.1).
        public static bool StartSeason(string name)
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentSeason))
                {
                    Plugin.Log.LogWarning("[TheRavensCall] Season '" + _currentSeason + "' is still running — `ravenscall season end` first; `season start` does not replace a running season.");
                    return false;
                }
                // Trim trailing periods/whitespace off the display name too
                // (see TrimTrailingPeriodsAndWhitespace above) — it can never
                // survive as typed on Windows anyway, so keep what's shown
                // and what's stored from drifting apart. A name made up of
                // nothing but periods/whitespace (".", "...") trims to "",
                // and "" means "no season" everywhere else in this class
                // (SeasonJson's active flag, EndSeason's guard) — fall back
                // to the same "season" fallback the folder already gets, so
                // the two always agree and the season stays endable
                // (review 2026-09-21).
                string display = TrimTrailingPeriodsAndWhitespace(name);
                if (string.IsNullOrEmpty(display)) display = SanitizeFolderName(name);
                _currentSeason = display;
                _seasonStart = DateTime.UtcNow;
                Chronicle.Init(ArchiveDir(_currentSeason));
                // Guarded the same way Init guards its mid-season snapshot: a
                // baseline write that throws must not leave a half-started
                // season behind, because the 1.6.1 guard above then refuses
                // every retry of `season start`.
                try { SnapshotBaseline(_currentSeason, _seasonStart); }
                catch (Exception bex)
                {
                    Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem: season_baseline.json could not be written: " +
                        bex.Message + " (standings count from this restart in memory; the next boot retries).");
                }
                SaveMeta();
                Plugin.Narrate($"A new season begins: {_currentSeason}!", "season_start", "SERVER");
                Plugin.Log.LogInfo("[TheRavensCall] Season started: " + _currentSeason);
                return true;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.StartSeason error: " + ex.Message); return false; }
        }

        public static void EndSeason()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSeason)) { Plugin.Log.LogWarning("[TheRavensCall] No active season to end."); return; }
                string ended = _currentSeason;
                var duration = DateTime.UtcNow - _seasonStart;
                WriteSeasonSummary(ended, duration);
                _currentSeason = null;
                Chronicle.Init();
                SaveMeta(ended);
                _baseline.Clear();
                _standingsSince = null;
                try { if (File.Exists(BaselinePath)) File.Delete(BaselinePath); }
                catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem: could not delete season_baseline.json: " + ex.Message); }
                Plugin.Narrate($"The season of {ended} has ended.", "season_end", "SERVER");
                Plugin.Log.LogInfo("[TheRavensCall] Season ended: " + ended);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.EndSeason error: " + ex.Message); }
        }

        private static void WriteSeasonSummary(string name, TimeSpan duration)
        {
            try
            {
                string nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string startStr = _seasonStart.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string json =
                    "{" +
                    "\"event_type\":\"season_summary\"," +
                    "\"season_name\":\"" + EscJ(name) + "\"," +
                    "\"timestamp_utc\":\"" + nowStr + "\"," +
                    "\"season_start\":\"" + startStr + "\"," +
                    "\"season_end\":\"" + nowStr + "\"," +
                    "\"duration_hours\":" + (int)duration.TotalHours +
                    "}";
                Chronicle.WriteRaw(json);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.WriteSeasonSummary error: " + ex.Message); }
        }

        private static void SaveMeta(string justEnded = null)
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall");
                Directory.CreateDirectory(dir);
                string startStr = _seasonStart.ToString("yyyy-MM-ddTHH:mm:ssZ");
                // Remembered (§4.4): update on the ending call only, but
                // re-emit the pair below on every SaveMeta write — including
                // the next StartSeason — so a restart reads last_ended back.
                if (justEnded != null) { _lastEnded = justEnded; _lastEndedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"); }
                string current = _currentSeason != null
                    ? "\"current_season\":\"" + EscJ(_currentSeason) + "\",\"season_start\":\"" + startStr + "\""
                    : "\"current_season\":\"\"";
                string endedEntry = !string.IsNullOrEmpty(_lastEnded)
                    ? ",\"last_ended\":\"" + EscJ(_lastEnded) + "\",\"last_ended_at\":\"" + EscJ(_lastEndedAt ?? "") + "\""
                    : "";
                // Atomic (1.4.2): a torn seasons.json used to be the one way
                // to lose a running season; the baseline next to it was
                // already written this way.
                PlayerRegistry.AtomicWrite(MetaPath, "{" + current + endedEntry + "}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.SaveMeta error: " + ex.Message); }
        }

        private static string EscJ(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LORE SYSTEM — world events + periodic lore.txt postings (Discord/Chronicle
    // only now; the original per-peer "whisper" and center-screen display are
    // gone since there is no client install to show them).
    // ═══════════════════════════════════════════════════════════════════════
    public static class LoreSystem
    {
        private static readonly List<string> _entries = new List<string>();
        private static float _loreTick = 0f;

        private static string LorePath => Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "lore.txt");

        private static readonly Dictionary<string, Func<string>> WorldEventMap = new Dictionary<string, Func<string>>
        {
            { "Eikthyr",     () => Plugin.WorldEventEikthyr?.Value  },
            { "gd_king",     () => Plugin.WorldEventElder?.Value    },
            { "Bonemass",    () => Plugin.WorldEventBonemass?.Value },
            { "Dragon",      () => Plugin.WorldEventModer?.Value    },
            { "GoblinKing",  () => Plugin.WorldEventYagluth?.Value  },
            { "SeekerQueen", () => Plugin.WorldEventQueen?.Value    },
            { "Fader",       () => Plugin.WorldEventFader?.Value    },
        };

        public static void Init()
        {
            try
            {
                _entries.Clear();
                if (!File.Exists(LorePath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LorePath));
                    File.WriteAllText(LorePath,
                        "The first tree was Yggdrasil, and all worlds hang from its branches.\n" +
                        "Odin sacrificed himself to himself to learn the runes.\n" +
                        "The great serpent Jormungandr encircles Midgard beneath the sea.\n" +
                        "Those who die in battle are chosen by the Valkyries for Valhalla.\n" +
                        "Ragnarok is not an ending — it is a turning of the age.\n");
                }
                foreach (var line in File.ReadAllLines(LorePath))
                    if (!string.IsNullOrWhiteSpace(line)) _entries.Add(line.Trim());
                Plugin.Log.LogInfo($"[TheRavensCall] LoreSystem loaded {_entries.Count} entries.");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] LoreSystem.Init error: " + ex.Message); }
        }

        public static void FireWorldEvent(string prefab)
        {
            if (!WorldEventMap.TryGetValue(prefab, out var getter)) return;
            string msg = getter();
            if (string.IsNullOrEmpty(msg)) return;
            Plugin.Narrate(msg, "world_event", "world");
        }

        // Ticked from the periodic poll (Patch_ZNetUpdate), not Player.Update.
        public static void Tick(float dt)
        {
            int interval = Plugin.LoreBroadcastIntervalMinutes?.Value ?? 0;
            if (interval <= 0 || _entries.Count == 0) return;
            _loreTick += dt;
            if (_loreTick < interval * 60f) return;
            _loreTick = 0f;
            string entry = _entries[UnityEngine.Random.Range(0, _entries.Count)];
            Plugin.Narrate(entry, "lore", "world");
        }
    }
}
