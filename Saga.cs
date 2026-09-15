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
        public const string PluginVersion = "1.3.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;
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
            HttpApiToken = Config.Bind("Companion", "HttpApiToken", "", "If set, /api/state, /api/gamedata and /api/pins require ?token=<this value> (or an X-Api-Token header). /api/health and the dashboard page stay open. Since 1.3.0 the bundled dashboard page has a settings panel (gear icon) to enter this token itself, stored in the browser and sent as X-Api-Token — a 401 opens that panel automatically. A pre-1.3.0 page still does not send a token");

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
            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                SessionTracker.WriteSessionSummary();
                PlayerRegistry.SaveAll();
                if (LogServerStartStop.Value)
                    Chronicle.Write("SERVER", "shutdown", "Server shutting down.", null);
            }
            Chronicle.Close();
            _harmony.UnpatchSelf();
        }

        public static bool IsServer() => ZNet.instance != null && ZNet.instance.IsServer();

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
            int online = ZNet.instance != null ? ZNet.instance.GetConnectedPeers().Count : 0;
            string dayPart = ShowDayNumber.Value && day > 0 ? $" [Day {day}]" : "";
            string countPart = ShowOnlineCount.Value && online > 0 ? $" ({online} online)" : "";
            return $"{prefix}{message}{dayPart}{countPart}";
        }

        private static Dictionary<string, string> BuildContextData(string detail)
        {
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            int online = ZNet.instance != null ? ZNet.instance.GetConnectedPeers().Count : 0;
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
        public static IEnumerable<(string name, Vector3 pos, long peerUid)> GetConnectedPlayers()
        {
            if (ZNet.instance == null) yield break;
            foreach (var peer in ZNet.instance.GetPeers())
            {
                if (peer == null || !peer.IsReady() || string.IsNullOrEmpty(peer.m_playerName)) continue;
                var zdo = ZDOMan.instance?.GetZDO(peer.m_characterID);
                Vector3 pos = zdo != null ? zdo.GetPosition() : peer.m_refPos;
                yield return (peer.m_playerName, pos, peer.m_uid);
            }
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
        // modulo case (the registry is case-insensitive too). ────────────────
        public static bool IsSelfReport(ZNetPeer reporter, string reportedName)
        {
            if (reporter == null || string.IsNullOrEmpty(reportedName) || string.IsNullOrEmpty(reporter.m_playerName)) return false;
            return string.Equals(reporter.m_playerName, reportedName, StringComparison.OrdinalIgnoreCase);
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
                Chronicle.Init();
                PlayerRegistry.LoadAll();
                Companion.PrimeStateCache();
                SessionTracker.Begin();
                SeasonSystem.Init();
                LoreSystem.Init();
                // Per-world-session registration (ZRoutedRpc.instance is new
                // every time ZNet.Awake runs) — same lifecycle point every
                // other server system here initializes at.
                ZRoutedRpc.instance.Register<ZPackage>("RavensCall_CombatReport_V1", CombatReportReceiver.Handle);
                ZRoutedRpc.instance.Register<ZPackage>("RavensCall_EventReport_V2", EventReportReceiver.Handle);
                if (Plugin.LogServerStartStop.Value)
                    Chronicle.Write("SERVER", "startup", "TheRavensCall is listening.", null);
                Plugin.Log.LogInfo("[TheRavensCall] Server systems initialized.");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] ZNet.Awake error: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.RPC_PeerInfo))]
    public static class Patch_PlayerJoin
    {
        private static readonly Dictionary<long, DateTime> JoinTimes = new Dictionary<long, DateTime>();
        private static readonly HashSet<long> _fired = new HashSet<long>();

        internal static bool GetJoinTime(long uid, out DateTime joinTime) => JoinTimes.TryGetValue(uid, out joinTime);
        internal static void ClearJoinTime(long uid) { JoinTimes.Remove(uid); _fired.Remove(uid); }

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
                var rec = PlayerRegistry.Get(name);
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
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] PlayerJoin error: {ex.Message}"); }
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
            Patch_PlayerJoin.ClearJoinTime(uid);

            var span = DateTime.UtcNow - joinTime;
            string duration = span.TotalHours >= 1 ? $" (played {(int)span.TotalHours}h {span.Minutes}m)" : $" (played {span.Minutes}m)";

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
            var connected = new HashSet<string>(
                Plugin.GetConnectedPlayers().Select(p => p.name), StringComparer.OrdinalIgnoreCase);

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
    // CombatCredit so it can never disagree with the live RPC path below. ──
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    public static class Patch_Damage
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            try
            {
                if (!Plugin.IsServer() || hit == null) return;
                var attacker = Plugin.ResolveAttacker(hit);
                if (attacker != null && __instance != attacker)
                    CombatCredit.CreditDamage(attacker.GetPlayerName(), hit.GetTotalDamage(), 0f);
                if (__instance is Player victim)
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

        public static void CreditCreatureKill(string name, string prefab, Vector3 position)
        {
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

            string bossDisplayName = TitleSystem.GetBossDisplayName(prefab);
            string bossKey = BossKeys.Resolve(prefab);
            float radius = Mathf.Clamp(Plugin.BossCreditRadius.Value, 5f, 500f);

            string namedKiller = null;
            foreach (var p in Plugin.GetConnectedPlayers())
            {
                if (Vector3.Distance(p.pos, position) > radius) continue;

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
            rec.ResourcesHarvested.TryGetValue(resourceName, out int cur);
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
                    if (Finite(kv.Value)) rec.SkillLevels[kv.Key] = Mathf.Clamp(kv.Value, 0f, MaxSkillLevel);
            if (progress != null)
                foreach (var kv in progress)
                    if (Finite(kv.Value)) rec.SkillProgress[kv.Key] = Mathf.Clamp(kv.Value, 0f, MaxSkillProgress);
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

    // ── Per-sender flood guard for the RPC receiver. A malicious or broken
    // client can invoke a routed RPC as fast as it wants; nothing upstream
    // rate-limits that for us. ──────────────────────────────────────────
    internal static class CombatReportRateLimiter
    {
        private const int MaxPerSecond = 30;
        private static readonly Dictionary<long, (int count, float windowStart)> _windows = new Dictionary<long, (int, float)>();

        public static bool Exceeded(long sender)
        {
            float now = Time.realtimeSinceStartup;
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

                // Reporter identity comes from the routed-RPC sender (resolved
                // through ZNet.GetPeer), never trusted from the payload.
                var reporterPeer = ZNet.instance?.GetPeer(sender);
                if (reporterPeer == null) return;

                // Self-reports (death, damage batch, fish) must name the
                // reporter's own character. Only a kill may legitimately name
                // someone else: the creature's ZDO owner reports it and the
                // killer can be another player. Before this check, any
                // connected player could credit or overwrite any other online
                // player's record (review 2026-09-15).
                if (eventType != 1 && !Plugin.IsSelfReport(reporterPeer, attackerName))
                {
                    if (Plugin.LogCombatReports.Value)
                        Plugin.Log.LogWarning($"[TheRavensCall] combat report dropped — '{attackerName}' is not the reporter ({reporterPeer.m_playerName})");
                    return;
                }

                if (!Plugin.TryVerifyConnectedPlayer(attackerName, out string verified, out _))
                {
                    if (Plugin.LogCombatReports.Value)
                        Plugin.Log.LogInfo($"[TheRavensCall] combat report dropped — '{attackerName}' not connected (reporter {reporterPeer.m_playerName})");
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

                if (CombatReportRateLimiter.Exceeded(sender))
                {
                    if (Plugin.LogCombatReports.Value) Plugin.Log.LogWarning($"[TheRavensCall] event report rate-limited from sender {sender}");
                    return;
                }

                int schema = pkg.ReadInt();
                if (schema != SchemaVersion) return; // unknown future schema: ignore, never guess

                // The reporter's peer is the identity every self-report is
                // bound to below; the payload name alone was never enough
                // (review 2026-09-15).
                var reporterPeer = ZNet.instance?.GetPeer(sender);
                if (reporterPeer == null) return;

                byte eventType = pkg.ReadByte();
                switch (eventType)
                {
                    case 1: HandleKill(sender, pkg); break;
                    case 2: HandleDeath(sender, reporterPeer, pkg); break;
                    case 3: HandleDamageDefenseBatch(reporterPeer, pkg); break;
                    case 4: HandleFishCatch(reporterPeer, pkg); break;
                    case 5: HandleBuilding(reporterPeer, pkg); break;
                    case 6: HandleCrafting(reporterPeer, pkg); break;
                    case 7: HandleHarvesting(reporterPeer, pkg); break;
                    case 8: HandleConsumables(reporterPeer, pkg); break;
                    case 9: HandleWorldEvent(reporterPeer, pkg); break;
                    case 10: HandleStatSyncDelta(reporterPeer, pkg); break;
                    case 11: HandleStatSnapshot(reporterPeer, pkg); break;
                    case 12: HandleSkillSnapshot(reporterPeer, pkg); break;
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
        private static bool VerifySelf(ZNetPeer reporter, string reportedName, out string verified)
        {
            verified = null;
            if (!Plugin.IsSelfReport(reporter, reportedName))
            {
                if (Plugin.LogCombatReports.Value)
                    Plugin.Log.LogWarning($"[TheRavensCall] event report dropped — '{reportedName}' is not the reporter ({reporter?.m_playerName})");
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

        private static void HandleDeath(long sender, ZNetPeer reporter, ZPackage pkg)
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

        private static void HandleDamageDefenseBatch(ZNetPeer reporter, ZPackage pkg)
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

        private static void HandleFishCatch(ZNetPeer reporter, ZPackage pkg)
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

        private static void HandleBuilding(ZNetPeer reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadString();  // piecePrefab
            pkg.ReadVector3(); // position
            byte action = pkg.ReadByte();
            pkg.ReadString();  // category

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditBuilding(verified, action);
        }

        private static void HandleCrafting(ZNetPeer reporter, ZPackage pkg)
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

        private static void HandleHarvesting(ZNetPeer reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            string resourceName = Cap(pkg.ReadString(), 64);
            pkg.ReadVector3(); // position
            pkg.ReadByte();    // sourceType — ResourcesHarvested is a flat count per resource, not split by source
            int amount = pkg.ReadInt();

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.CreditHarvest(verified, resourceName, amount);
        }

        private static void HandleConsumables(ZNetPeer reporter, ZPackage pkg)
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

        private static void HandleWorldEvent(ZNetPeer reporter, ZPackage pkg)
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
        private static void HandleStatSyncDelta(ZNetPeer reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadVector3(); // position
            int count = Mathf.Clamp(pkg.ReadInt(), 0, MaxStatPairs);

            var deltas = new Dictionary<string, float>(count);
            for (int i = 0; i < count; i++)
            {
                short statId = pkg.ReadShort();
                float delta = pkg.ReadSingle();
                deltas[((PlayerStatType)statId).ToString()] = delta;
            }

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.ApplyVanillaStatDeltas(verified, deltas);
        }

        private static void HandleStatSnapshot(ZNetPeer reporter, ZPackage pkg)
        {
            string playerName = Cap(pkg.ReadString(), 32);
            pkg.ReadVector3(); // position
            int count = Mathf.Clamp(pkg.ReadInt(), 0, MaxStatPairs);

            var stats = new Dictionary<string, float>(count);
            for (int i = 0; i < count; i++)
            {
                short statId = pkg.ReadShort();
                float value = pkg.ReadSingle();
                stats[((PlayerStatType)statId).ToString()] = value;
            }

            if (!VerifySelf(reporter, playerName, out string verified)) return;
            CombatCredit.ApplyVanillaStats(verified, stats);
        }

        private static void HandleSkillSnapshot(ZNetPeer reporter, ZPackage pkg)
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
    }

    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.SetRandomEventByName))]
    public static class Patch_Raid
    {
        private static void Postfix(string name)
        {
            if (!Plugin.IsServer()) return;
            WorldState.RaidActive = !string.IsNullOrEmpty(name);
            WorldState.RaidType = name ?? "";
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
            rec.EarnedTitles.Add(title);
            if (string.IsNullOrEmpty(rec.ActiveTitle)) rec.ActiveTitle = title;
            rec.Dirty = true;

            string titleDisplay = GetDisplayName(playerName);
            string titleMsg = NarrativeSystem.Get("title_earned", new Dictionary<string, string>
            {
                { "name", titleDisplay }, { "title", title },
            }, $"{titleDisplay} has earned the title: {title}!");
            if (Plugin.EnableTitleEarned.Value) Plugin.FireEvent("title_earned", playerName, titleMsg, title);
            PlayerRegistry.Save(rec);
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
                _logDir = overrideDir ?? Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "Chronicle");
                Directory.CreateDirectory(_logDir);
                string date = DateTime.UtcNow.ToString("yyyy-MM-dd");
                _logPath = Path.Combine(_logDir, $"TheRavensCall_Chronicle_{date}.log");
                _writer = new System.IO.StreamWriter(_logPath, append: true) { AutoFlush = true };
                Plugin.Log.LogInfo($"[TheRavensCall] Chronicle: {_logPath}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] Chronicle init failed: {ex.Message}"); }
        }

        public static void Write(string player, string eventType, string message, Dictionary<string, string> data)
        {
            if (Plugin.EnableChronicleLog != null && !Plugin.EnableChronicleLog.Value) return;
            lock (_lock)
            {
                try
                {
                    string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                    if (_logPath != null && !_logPath.Contains(today)) { _writer?.Close(); Init(_logDir); }

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

        public static void Close() { lock (_lock) { try { _writer?.Close(); } catch { } } }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
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
            new Terminal.ConsoleCommand("ravenscall", "Usage: ravenscall season start [name] | ravenscall season end", args =>
            {
                if (args.Length < 2 || args[1] != "season") { args.Context?.AddString("[TheRavensCall] Usage: ravenscall season start [name] | ravenscall season end"); return; }
                if (args.Length >= 3 && args[2] == "start")
                {
                    string sName = args.Length >= 4 ? args[3] : "Season_" + DateTime.UtcNow.ToString("yyyyMMdd");
                    SeasonSystem.StartSeason(sName);
                    args.Context?.AddString("[TheRavensCall] Season started: " + sName);
                }
                else if (args.Length >= 3 && args[2] == "end")
                {
                    SeasonSystem.EndSeason();
                    args.Context?.AddString("[TheRavensCall] Season ended.");
                }
                else
                {
                    args.Context?.AddString("[TheRavensCall] Usage: ravenscall season start [name] | ravenscall season end");
                }
            }, onlyServer: true, remoteCommand: true);
        }
    }

    public static class SeasonSystem
    {
        private static string _currentSeason = null;
        private static DateTime _seasonStart = DateTime.MinValue;

        private static string MetaPath => Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "seasons.json");

        public static string GetCurrentSeasonName() => _currentSeason ?? "";

        public static void Init()
        {
            try
            {
                if (!File.Exists(MetaPath)) return;
                string raw = File.ReadAllText(MetaPath);
                var m = System.Text.RegularExpressions.Regex.Match(raw, "\"current_season\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) _currentSeason = m.Groups[1].Value;
                var ms = System.Text.RegularExpressions.Regex.Match(raw, "\"season_start\"\\s*:\\s*\"([^\"]+)\"");
                if (ms.Success) DateTime.TryParse(ms.Groups[1].Value, out _seasonStart);
                if (!string.IsNullOrEmpty(_currentSeason))
                    Plugin.Log.LogInfo("[TheRavensCall] Active season loaded: " + _currentSeason);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.Init error: " + ex.Message); }
        }

        public static void StartSeason(string name)
        {
            try
            {
                _currentSeason = name;
                _seasonStart = DateTime.UtcNow;
                string archiveDir = Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "Chronicle", "seasons", name);
                Chronicle.Init(archiveDir);
                SaveMeta();
                Plugin.Narrate($"A new season begins: {name}!", "season_start", "SERVER");
                Plugin.Log.LogInfo("[TheRavensCall] Season started: " + name);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] SeasonSystem.StartSeason error: " + ex.Message); }
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
                string nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string startStr = _seasonStart.ToString("yyyy-MM-ddTHH:mm:ssZ");
                string current = _currentSeason != null
                    ? "\"current_season\":\"" + EscJ(_currentSeason) + "\",\"season_start\":\"" + startStr + "\""
                    : "\"current_season\":\"\"";
                string endedEntry = justEnded != null
                    ? ",\"last_ended\":\"" + EscJ(justEnded) + "\",\"last_ended_at\":\"" + nowStr + "\""
                    : "";
                File.WriteAllText(MetaPath, "{" + current + endedEntry + "}");
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
