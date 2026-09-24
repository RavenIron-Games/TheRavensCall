using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TheRavensCall
{
    // ═════════════════════════════════════════════════════════════════════════
    // PLAYER REGISTRY — server-held, multi-player record store.
    //
    // Both source mods kept exactly one character's data in static fields,
    // swapped in/out as "the locally loaded character" — a client-side
    // pattern. This mod runs on the dedicated server only, which sees every
    // player at once and owns none of them locally, so all Saga (titles,
    // milestones, gear, biomes) and Companion (session/lifetime stats, boss
    // defeats, fish, death history) data lives here instead, keyed by player
    // display name (same key BarrkBOT's own player-stats store already uses).
    // ═════════════════════════════════════════════════════════════════════════
    public class PlayerRecord
    {
        public string Name;
        public string FirstSeen = "";
        public string LastSeen = "";
        public bool Online = false;
        public string SessionStart = null; // null when offline
        public long PlaytimeSecondsLifetime = 0;

        // Saga: narrative progression (server-attributed)
        public int Kills = 0;
        public int Deaths = 0;
        public int GearTier = 0;
        public string ActiveTitle = "";
        public HashSet<string> EarnedTitles = new HashSet<string>();
        public HashSet<string> BiomesDiscovered = new HashSet<string>();
        public int BossKillsCredited = 0;
        public Dictionary<string, int> CreatureKills = new Dictionary<string, int>();

        // Companion: session + lifetime stats
        public int SessionKills = 0;
        public float SessionDmgDone = 0f;
        public float SessionDmgTaken = 0f;
        public int SessionBlocks = 0;
        public int SessionParries = 0;
        public float SessionDmgBlocked = 0f;
        public int TotalKillsLifetime = 0;
        public int TotalDeathsLifetime = 0;
        public HashSet<string> DefeatedBosses = new HashSet<string>();
        public HashSet<string> CaughtFish = new HashSet<string>();
        public List<string> DeathHistory = new List<string>(); // raw JSON objects, newest first, max 10

        // ── V2 telemetry (RavensCall_EventReport_V2, WhereTheCrowFlies) ──
        // Full vanilla PlayerStatType snapshot — everything on Valheim's own
        // in-game Stats screen (kills, hits, deaths, jumps, cheats, world
        // loads, PvP hits/kills, arrows shot, portals, distance, and the
        // rest of the ~105 counters) — keyed by PlayerStatType.ToString() so
        // the JSON key names match Valheim's own enum instead of inventing
        // parallel field names. Replaced wholesale on every StatSync — the
        // client sends its current absolute totals, not deltas, so this
        // self-backfills whatever a player already had before the mod was
        // installed and never drifts from a dropped packet.
        public Dictionary<string, float> VanillaStats = new Dictionary<string, float>();

        // Skill levels (0-100) and progress-to-next-level (0-1), both keyed
        // by Skills.SkillType.ToString(). Empty until WhereTheCrowFlies
        // reports a skill-sync event; a skill absent from both dictionaries
        // means the player has never raised it, not that it's at zero.
        public Dictionary<string, float> SkillLevels = new Dictionary<string, float>();
        public Dictionary<string, float> SkillProgress = new Dictionary<string, float>();

        // Lightweight lifetime counters from the V2 stream's non-combat
        // event types — aggregate counts only, not full histories, same
        // bounded-footprint philosophy as DeathHistory above.
        public int BuildsPlaced = 0;
        public int BuildsRemoved = 0;
        public int BuildsRepaired = 0;
        public int ItemsCrafted = 0;
        public int ItemsUpgraded = 0;
        public int ItemsRepaired = 0;
        public Dictionary<string, int> ResourcesHarvested = new Dictionary<string, int>();
        public int ConsumablesEaten = 0;
        public int BossesSummoned = 0;
        public int GuardianPowersUsed = 0;

        public bool Dirty = false;
    }

    // World-scoped (not per-player) live flags — mirrors the original
    // RaidActive/RaidType fields, which were always really a world event,
    // not a per-character one.
    public static class WorldState
    {
        public static bool RaidActive = false;
        public static string RaidType = "";
    }

    // ── Name ↔ account binding (1.8.0). A record is keyed by the character
    // name the client sends in PeerInfo, and the server does not stop two
    // accounts from using the same name. So two players both called "Bjorn"
    // overwrote each other's snapshots, and anyone could make a character
    // with a leader's name and send snapshots or /title changes as them
    // (review 2026-09-24). The first account seen under a name is remembered
    // in player_platforms.json next to the players folder; a later peer with
    // that name on another account is refused for self-reports and title
    // changes, and its join, leave and poll never touch the record. Kill
    // reports that name a player stay open to any reporter, as before (the
    // creature's owner reports a kill). Kept out of the player files on
    // purpose: those are the BarrkBOT export's shape, and account ids are
    // not for publishing. An admin can release a name with
    // `ravenscall unbind <player>` (the next account to join takes it).
    //
    // Which id identifies an account (1.0.15 decompile):
    // • Steam-only server (ZSteamSocket): GetHostName() is the SteamID64 of
    //   the Steam connection, and RPC_PeerInfo checks the Steam session
    //   ticket for it. Verified; stored as "Steam_<id>".
    // • Crossplay server (ZPlayFabSocket, every peer, Steam players too):
    //   GetHostName() is the platform id the client itself sends in the
    //   socket's first message (InternalReceive), and nothing checks it:
    //   an unparsable one becomes "" and PlayFabManager.
    //   CheckIfUserAuthenticated is a stub that always answers true. What
    //   the client does not choose is m_remotePlayerId, the PlayFab entity
    //   id of the PlayFab Party player the listen socket was created for
    //   (remotePlayer.EntityKey.Id); the game signs in to PlayFab with its
    //   platform's ticket (PlayFabAuthWithSteam for Steam), and PlayFab
    //   Party, not the game, vouches for that id. So on crossplay the name
    //   is bound to that PlayFab id ("pf"). The client-sent platform id is
    //   kept only as a claim, normalized, to carry a binding across a
    //   crossplay switch.
    // • Switching crossplay on or off: "Steam_<id>" (crossplay) and a bare
    //   "<id>" (Steam-only) are the same account. A name bound on a
    //   Steam-only server takes its PlayFab id from the first crossplay join
    //   whose claimed platform id is the bound Steam id; that one join is
    //   the step nothing can verify (a Steam id is public), after which the
    //   PlayFab id holds. A name first bound on crossplay must match its
    //   claimed platform id on a Steam-only server, or takes the verified
    //   Steam id there if it claimed none.
    // • A crossplay or Steam peer without a usable id is refused, not waved
    //   through (a modified client can send an empty or junk platform id).
    //   Only a socket that has no platform id at all (ZSocket2, an IP
    //   address) is not checked, as before.
    // • A name with a control character or a broken surrogate is refused
    //   everywhere (Plugin.GetConnectedPlayers, IsSelfReport, the join, and
    //   here): the JSON writer drops those characters, so "Nomad\u0001"
    //   would be saved as "Nomad" and could take over the real row after a
    //   restart. ──────────────────────────────────────────────────────────
    public static class PlatformBinding
    {
        private class Binding { public string Platform; public string PlayFab; }
        private static bool _warnedUnknownSocket;
        // True only while the main file on disk is known good (read with no
        // skipped rows, or written by Save); Save copies it to .bak only then,
        // so a bad or unmovable file never replaces the good backup.
        private static bool _cleanOnDisk;

        private static readonly Dictionary<string, Binding> _b =
            new Dictionary<string, Binding>(StringComparer.OrdinalIgnoreCase);

        private static string FilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "player_platforms.json");

        // False for a name the files cannot hold losslessly: any character
        // below 0x20 or DEL (Companion.Esc drops them, and the reader turns
        // an escaped \n into 'n'), or an unpaired surrogate (UTF-8 writes
        // it as U+FFFD).
        public static bool IsStorableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < 0x20 || c == 0x7F) return false;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1])) return false;
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }

        // "Steam_<digits>" and a bare "<digits>" (ZSteamSocket's SteamID64)
        // are one account; other platform ids ("Xbox_…") are kept as they
        // are. Anything else (empty, "None", junk) is no id.
        internal static string NormalizePlatformId(string h)
        {
            if (string.IsNullOrEmpty(h)) return null;
            h = h.Trim();
            if (h.Length == 0 || h == "None") return null;
            if (h.All(char.IsDigit)) return h.Length >= 5 ? "Steam_" + h : null;
            int us = h.IndexOf('_');
            if (us <= 0 || us == h.Length - 1) return null;
            string plat = h.Substring(0, us), rest = h.Substring(us + 1);
            if (!plat.All(char.IsLetter) || !rest.All(ch => char.IsLetterOrDigit(ch) || ch == '-')) return null;
            if (string.Equals(plat, "Steam", StringComparison.OrdinalIgnoreCase))
                return rest.All(char.IsDigit) ? "Steam_" + rest : null;
            return plat + "_" + rest;
        }

        public static void Load()
        {
            _b.Clear();
            _cleanOnDisk = false;
            if (TryLoadFrom(FilePath, out string why)) return;
            if (why == null) return; // no file yet
            // A broken file must not be overwritten by the next join's Save
            // and take every binding with it: move it aside, say so loudly,
            // and fall back to the copy the last good Save kept.
            _b.Clear();
            _cleanOnDisk = false;
            string moved;
            try { File.Copy(FilePath, FilePath + ".corrupt", true); File.Delete(FilePath); moved = "moved to player_platforms.json.corrupt"; }
            catch (Exception mex) { moved = "could NOT be moved aside (" + mex.Message + "); the .bak is left as it is"; }
            Plugin.Log.LogError($"[TheRavensCall] player_platforms.json is unreadable ({why}); {moved}.");
            string bak = FilePath + ".bak";
            if (File.Exists(bak) && TryLoadFrom(bak, out _))
            {
                Plugin.Log.LogError($"[TheRavensCall] Name bindings restored from player_platforms.json.bak ({_b.Count} name(s)); any binding made after that copy is missing, see the .corrupt file.");
                Save();
            }
            else
            {
                _b.Clear();
                Plugin.Log.LogError("[TheRavensCall] No usable player_platforms.json.bak: every name is unbound until its player joins again. Restore the bindings from player_platforms.json.corrupt by hand (server stopped) if they matter.");
            }
        }

        // why: null when the file does not exist, else why it failed.
        private static bool TryLoadFrom(string path, out string why)
        {
            why = null;
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path, Encoding.UTF8);
                string t = json.Trim();
                if (!t.StartsWith("{") || !t.EndsWith("]}") || t.IndexOf("\"bindings\":[", StringComparison.Ordinal) < 0)
                { why = "not a bindings file, or truncated"; return false; }
                int bad = 0;
                foreach (string row in Companion.JsonGetArray(json, "bindings"))
                {
                    string name = Companion.JsonGetString(row, "name");
                    string plat = NormalizePlatformId(Companion.JsonGetString(row, "id"));
                    string pf = Companion.JsonGetString(row, "pf");
                    if (string.IsNullOrEmpty(pf)) pf = null;
                    if (!IsStorableName(name) || (plat == null && pf == null)) { bad++; continue; }
                    // Two rows for one name: keep the first, never let a
                    // later row silently win.
                    if (_b.ContainsKey(name)) { bad++; continue; }
                    _b[name] = new Binding { Platform = plat, PlayFab = pf };
                }
                if (bad > 0) Plugin.Log.LogWarning($"[TheRavensCall] {Path.GetFileName(path)}: skipped {bad} malformed or duplicate row(s).");
                _cleanOnDisk = bad == 0 && path == FilePath;
                Plugin.Log.LogInfo($"[TheRavensCall] PlatformBinding loaded {_b.Count} name binding(s) from {Path.GetFileName(path)}.");
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                // Keep the last good file as .bak before replacing it (Load
                // falls back to it if the main file is ever unreadable).
                if (_cleanOnDisk && File.Exists(FilePath)) { try { File.Copy(FilePath, FilePath + ".bak", true); } catch { } }
                string rows = string.Join(",", _b.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => "{\"id\":\"" + Companion.Esc(kv.Value.Platform ?? "") + "\",\"pf\":\"" + Companion.Esc(kv.Value.PlayFab ?? "") +
                                  "\",\"name\":\"" + Companion.Esc(kv.Key) + "\"}"));
                PlayerRegistry.AtomicWrite(FilePath, "{\"bindings\":[" + rows + "]}");
                _cleanOnDisk = true;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] player_platforms.json write failed: " + ex.Message); }
        }

        // True when the peer may act as its name. Never true for an
        // unstorable name or a Steam/crossplay peer without a usable id.
        // Only the join passes allowBind: it binds a name seen for the first
        // time and fills in the other id of a known name when a crossplay
        // switch brings it (see the class comment). Reports and title
        // changes never bind: a name that is unbound (e.g. after `ravenscall
        // unbind` while its player is online) is refused until the next
        // join takes it, so the first same-name reporter cannot.
        public static bool Check(ZNetPeer peer, bool allowBind)
        {
            string name = peer?.m_playerName;
            if (!IsStorableName(name)) return false;
            string plat, pf = null;
            bool crossplay;
            try
            {
                if (peer.m_socket is ZPlayFabSocket pfs)
                {
                    crossplay = true;
                    pf = string.IsNullOrEmpty(pfs.m_remotePlayerId) ? null : pfs.m_remotePlayerId;
                    plat = NormalizePlatformId(pfs.GetHostName());
                    if (pf == null) return false;
                }
                else if (peer.m_socket is ZSteamSocket)
                {
                    crossplay = false;
                    plat = NormalizePlatformId(peer.m_socket.GetHostName());
                    if (plat == null) return false;
                }
                else
                {
                    // No platform id at all: a socket type another mod adds
                    // (a 1.0.15 server itself never accepts a ZSocket2). Not
                    // checked; said once so it is visible.
                    if (!_warnedUnknownSocket)
                    {
                        _warnedUnknownSocket = true;
                        Plugin.Log.LogWarning($"[TheRavensCall] A player connected through {peer.m_socket?.GetType().Name}, which has no platform id; names on such connections are not bound to an account.");
                    }
                    return true;
                }
            }
            catch { return false; }

            if (!_b.TryGetValue(name, out var b))
            {
                if (!allowBind) return false;
                _b[name] = new Binding { Platform = plat, PlayFab = pf };
                Save();
                return true;
            }
            if (crossplay)
            {
                if (b.PlayFab != null) return string.Equals(b.PlayFab, pf, StringComparison.Ordinal);
                // Bound on a Steam-only server: this join's claimed platform
                // id must be the bound one; then the PlayFab id is kept.
                if (!allowBind || plat == null || !string.Equals(b.Platform, plat, StringComparison.Ordinal)) return false;
                b.PlayFab = pf;
                Save();
                Plugin.Log.LogInfo($"[TheRavensCall] {name}: PlayFab account recorded for crossplay.");
                return true;
            }
            if (b.Platform != null) return string.Equals(b.Platform, plat, StringComparison.Ordinal);
            // Bound on crossplay with no platform id claimed: the verified
            // Steam id of this Steam-only join takes it.
            if (!allowBind) return false;
            b.Platform = plat;
            Save();
            return true;
        }

        public static bool Unbind(string name)
        {
            if (string.IsNullOrEmpty(name) || !_b.Remove(name)) return false;
            Save();
            return true;
        }
    }

    public static class PlayerRegistry
    {
        private static readonly Dictionary<string, PlayerRecord> _records =
            new Dictionary<string, PlayerRecord>(StringComparer.OrdinalIgnoreCase);

        private static string PlayersDir =>
            Path.Combine(BepInEx.Paths.ConfigPath, "TheRavensCall", "players");

        public static IEnumerable<PlayerRecord> All => _records.Values;

        public static PlayerRecord Get(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return null;
            if (_records.TryGetValue(playerName, out var rec)) return rec;

            rec = LoadFromDisk(playerName) ?? new PlayerRecord
            {
                Name = playerName,
                FirstSeen = DateTime.UtcNow.ToString("o"),
            };
            rec.Name = playerName;
            _records[playerName] = rec;
            return rec;
        }

        // Load every existing player file at server startup so the registry
        // (and therefore the BarrkBOT export and /api/state) is complete
        // immediately, not just for players who happen to reconnect first.
        public static void LoadAll()
        {
            try
            {
                Directory.CreateDirectory(PlayersDir);
                foreach (string file in Directory.GetFiles(PlayersDir, "*.json"))
                {
                    // The registry key is the player's real name, read back from
                    // the file's own "name" field. Deriving it from the file name
                    // broke every name SanitizeForFile had to alter: "Od:in" was
                    // loaded as "Od_in", then the real Od:in joined and got a
                    // second record over the same file (review 2026-09-15).
                    // Files written before this fix carry the field too, since
                    // ToJson has always emitted it first.
                    string fileKey = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(fileKey)) continue;
                    string name = null;
                    try { name = Companion.JsonGetString(File.ReadAllText(file, Encoding.UTF8), "name"); } catch { }
                    if (string.IsNullOrEmpty(name)) name = fileKey;
                    // Two files never load into one record (1.8.0): the first
                    // one read keeps the name and a second is left on disk
                    // untouched, with a warning.
                    if (_records.ContainsKey(name) || !PlatformBinding.IsStorableName(name))
                    {
                        Plugin.Log.LogWarning($"[TheRavensCall] PlayerRegistry: {Path.GetFileName(file)} was not loaded — its name is already taken by another file or cannot be stored as written.");
                        continue;
                    }
                    // A file that no longer sits where Save() will write it (a
                    // reserved device name, now prefixed) is migrated once, so
                    // two files can never claim one key. If the new-style file
                    // already exists it is the live one and the leftover is
                    // skipped, whatever order the directory enumerates in.
                    string want = SanitizedFileName(name);
                    bool renamed = !string.Equals(Path.GetFullPath(file), Path.GetFullPath(want), StringComparison.OrdinalIgnoreCase);
                    if (renamed && File.Exists(want)) continue;
                    var rec = LoadFromDisk(name, file);
                    if (rec == null) continue;
                    rec.Name = name; _records[name] = rec;
                    if (renamed)
                    {
                        try { rec.Dirty = true; Save(rec); File.Delete(file); }
                        catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] PlayerRegistry: could not migrate {Path.GetFileName(file)} to {Path.GetFileName(want)}: {ex.Message}"); }
                    }
                }
                Plugin.Log.LogInfo($"[TheRavensCall] PlayerRegistry loaded {_records.Count} player record(s).");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] PlayerRegistry.LoadAll error: " + ex.Message); }
        }

        // ── Sessions left open by an unclean stop (1.8.0). The leave hooks and
        // Plugin.OnDestroy only run on a clean shutdown; after a killed
        // process, a closed console window or a crash, every record that was
        // online still carries its session_start on disk, and that playtime
        // used to be dropped (review 2026-09-24). Called once at boot, after
        // LoadAll: credits last_seen − session_start (not "now", so the
        // server's downtime is not counted) and closes the session. The poll
        // tick persists last_seen about once a minute for this. ─────────────
        public static void CloseSessionsLeftOpen()
        {
            int closed = 0; long credited = 0;
            foreach (var rec in _records.Values)
            {
                if (string.IsNullOrEmpty(rec.SessionStart)) continue;
                var styles = System.Globalization.DateTimeStyles.RoundtripKind;
                if (DateTime.TryParse(rec.SessionStart, null, styles, out DateTime start) &&
                    DateTime.TryParse(rec.LastSeen, null, styles, out DateTime last))
                {
                    long secs = Math.Max(0, (long)(last.ToUniversalTime() - start.ToUniversalTime()).TotalSeconds);
                    rec.PlaytimeSecondsLifetime += secs;
                    credited += secs;
                }
                rec.SessionStart = null;
                rec.Online = false;
                rec.Dirty = true;
                Save(rec);
                closed++;
            }
            if (closed > 0)
                Plugin.Log.LogInfo($"[TheRavensCall] Closed {closed} session(s) left open by the last stop, crediting {credited} s of playtime up to each player's last-seen time.");
        }

        private static string SanitizedFileName(string playerName) =>
            Path.Combine(PlayersDir, SanitizeForFile(playerName) + ".json");

        // Windows reserved device names cannot be file names at all; a player
        // called CON or COM1 could never be saved (review 2026-09-15).
        private static readonly HashSet<string> ReservedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        // One file per name, never two names per file (1.8.0). This used to
        // replace every character a file name cannot hold with '_', so "Od:in"
        // and "Od_in" wrote the same file and overwrote each other, and
        // "_CON" collided with the prefixed "CON". Now such a character, '%'
        // itself, and trailing dots and spaces (Windows drops those) become
        // %XX, and a reserved device name gets a bare '%' in front, which no
        // escape can produce. Files written under the old scheme are moved
        // once by LoadAll, which reads each file's own "name" field.
        private static string SanitizeForFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var sb = new StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            int keep = name.Length;
            while (keep > 0 && (name[keep - 1] == '.' || name[keep - 1] == ' ')) keep--;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '%' || Array.IndexOf(invalid, c) >= 0 || i >= keep)
                    sb.Append('%').Append(((int)c).ToString("X2"));
                else sb.Append(c);
            }
            string s = sb.ToString();
            return ReservedFileNames.Contains(s) ? "%" + s : s;
        }

        // Temp file + rename, so a reader (BarrkBOT reads these off disk) can
        // never see a half-written file, and a crash mid-write leaves the
        // previous complete file in place instead of a truncated one that the
        // loader would read back as "never set" (review 2026-09-15). Same
        // UTF-8-with-BOM encoding as before; the contract documents the BOM.
        internal static void AtomicWrite(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, Encoding.UTF8);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception)
            {
                // Platform without an atomic replace: move the old file aside,
                // move the new one in, then drop the backup, so a failure at
                // any step leaves at least one complete copy on disk. (The
                // contract's "a missing file is loud" holds for the BarrkBOT
                // aggregates only; nobody but this mod reads players/*.json,
                // so a missing one there would be silent.)
                string bak = path + ".bak";
                if (File.Exists(path))
                {
                    try { File.Delete(bak); } catch { }
                    File.Move(path, bak);
                }
                try { File.Move(tmp, path); }
                catch
                {
                    if (File.Exists(bak) && !File.Exists(path)) File.Move(bak, path);
                    throw;
                }
                try { File.Delete(bak); } catch { }
            }
        }

        // path: pass the actual file when it is already known (LoadAll), so a
        // record whose sanitized name changed (a reserved name) still loads.
        private static PlayerRecord LoadFromDisk(string playerName, string path = null)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) path = SanitizedFileName(playerName);
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Encoding.UTF8);
                if (!json.TrimEnd().EndsWith("}"))
                {
                    // Every accessor below treats a missing key as "never set",
                    // so a truncated file would silently zero the player. Say
                    // so, keep the evidence, start fresh (review 2026-09-15).
                    Plugin.Log.LogWarning($"[TheRavensCall] PlayerRegistry: {Path.GetFileName(path)} is truncated; {playerName} starts from a fresh record. Copy kept as {Path.GetFileName(path)}.corrupt");
                    try { File.Copy(path, path + ".corrupt", true); } catch { }
                    return null;
                }

                var rec = new PlayerRecord
                {
                    Name = playerName,
                    FirstSeen = Companion.JsonGetString(json, "first_seen") ?? DateTime.UtcNow.ToString("o"),
                    LastSeen = Companion.JsonGetString(json, "last_seen") ?? "",
                    // Read back since 1.8.0 so a session left open by an
                    // unclean stop can be credited (CloseSessionsLeftOpen).
                    SessionStart = Companion.JsonGetString(json, "session_start"),
                    PlaytimeSecondsLifetime = Companion.JsonGetLong(json, "playtime_seconds_lifetime"),
                    Kills = Companion.JsonGetInt(json, "kills_narrative"),
                    Deaths = Companion.JsonGetInt(json, "deaths_narrative"),
                    GearTier = Companion.JsonGetInt(json, "gear_tier"),
                    ActiveTitle = Companion.JsonGetString(json, "active_title") ?? "",
                    BossKillsCredited = Companion.JsonGetInt(json, "boss_kills_credited"),
                    TotalKillsLifetime = Companion.JsonGetInt(json, "kills_observed_lifetime"),
                    TotalDeathsLifetime = Companion.JsonGetInt(json, "deaths_lifetime"),
                };
                rec.EarnedTitles = new HashSet<string>(Companion.JsonGetStringArray(json, "titles_earned"));
                rec.BiomesDiscovered = new HashSet<string>(Companion.JsonGetStringArray(json, "biomes_discovered"));
                rec.DefeatedBosses = new HashSet<string>(Companion.JsonGetStringArray(json, "bosses_defeated"));
                rec.CaughtFish = new HashSet<string>(Companion.JsonGetStringArray(json, "caught_fish"));
                rec.DeathHistory = Companion.JsonGetArray(json, "death_history");
                rec.CreatureKills = ParseCreatureKills(json);

                rec.VanillaStats = ParseStringFloatMap(json, "vanilla_stats");
                rec.SkillLevels = ParseStringFloatMap(json, "skill_levels");
                rec.SkillProgress = ParseStringFloatMap(json, "skill_progress");
                rec.BuildsPlaced = Companion.JsonGetInt(json, "builds_placed");
                rec.BuildsRemoved = Companion.JsonGetInt(json, "builds_removed");
                rec.BuildsRepaired = Companion.JsonGetInt(json, "builds_repaired");
                rec.ItemsCrafted = Companion.JsonGetInt(json, "items_crafted");
                rec.ItemsUpgraded = Companion.JsonGetInt(json, "items_upgraded");
                rec.ItemsRepaired = Companion.JsonGetInt(json, "items_repaired");
                rec.ResourcesHarvested = ParseStringIntMap(json, "resources_harvested");
                rec.ConsumablesEaten = Companion.JsonGetInt(json, "consumables_eaten");
                rec.BossesSummoned = Companion.JsonGetInt(json, "bosses_summoned");
                rec.GuardianPowersUsed = Companion.JsonGetInt(json, "guardian_powers_used");

                return rec;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] PlayerRegistry load failed for {playerName}: {ex.Message}"); return null; }
        }

        private static Dictionary<string, int> ParseCreatureKills(string json)
        {
            var result = new Dictionary<string, int>();
            string section = Companion.ExtractJsonField(json, "creature_kills", true);
            if (string.IsNullOrEmpty(section) || section.Length < 2) return result;
            // section looks like {"Boar":12,"Wolf":3}
            string inner = section.Substring(1, section.Length - 2);
            foreach (var raw in SplitTopLevel(inner))
            {
                int colon = raw.IndexOf(':');
                if (colon < 0) continue;
                string key = raw.Substring(0, colon).Trim().Trim('"');
                string val = raw.Substring(colon + 1).Trim();
                if (!string.IsNullOrEmpty(key) && int.TryParse(val, out int n)) result[key] = n;
            }
            return result;
        }

        private static Dictionary<string, float> ParseStringFloatMap(string json, string key)
        {
            var result = new Dictionary<string, float>();
            string section = Companion.ExtractJsonField(json, key, true);
            if (string.IsNullOrEmpty(section) || section.Length < 2) return result;
            string inner = section.Substring(1, section.Length - 2);
            foreach (var raw in SplitTopLevel(inner))
            {
                int colon = raw.IndexOf(':');
                if (colon < 0) continue;
                string mapKey = raw.Substring(0, colon).Trim().Trim('"');
                string val = raw.Substring(colon + 1).Trim();
                if (!string.IsNullOrEmpty(mapKey) &&
                    float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n))
                    { if (!float.IsNaN(n) && !float.IsInfinity(n)) result[mapKey] = n; } // skip poisoned values (review 2026-09-15)
            }
            return result;
        }

        private static Dictionary<string, int> ParseStringIntMap(string json, string key)
        {
            var result = new Dictionary<string, int>();
            string section = Companion.ExtractJsonField(json, key, true);
            if (string.IsNullOrEmpty(section) || section.Length < 2) return result;
            string inner = section.Substring(1, section.Length - 2);
            foreach (var raw in SplitTopLevel(inner))
            {
                int colon = raw.IndexOf(':');
                if (colon < 0) continue;
                string mapKey = raw.Substring(0, colon).Trim().Trim('"');
                string val = raw.Substring(colon + 1).Trim();
                if (!string.IsNullOrEmpty(mapKey) && int.TryParse(val, out int n)) result[mapKey] = n;
            }
            return result;
        }

        private static IEnumerable<string> SplitTopLevel(string s)
        {
            var parts = new List<string>();
            int depth = 0; bool inStr = false; bool esc = false; int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (esc) { esc = false; continue; }
                if (c == '\\' && inStr) { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            if (start < s.Length) parts.Add(s.Substring(start));
            return parts;
        }

        public static void Save(PlayerRecord rec)
        {
            if (rec == null) return;
            try
            {
                Directory.CreateDirectory(PlayersDir);
                AtomicWrite(SanitizedFileName(rec.Name), ToJson(rec));
                rec.Dirty = false;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] PlayerRegistry save failed for {rec?.Name}: {ex.Message}"); }
        }

        public static void SaveAll()
        {
            foreach (var rec in _records.Values) Save(rec);
        }

        public static void SaveDirty()
        {
            foreach (var rec in _records.Values) if (rec.Dirty) Save(rec);
        }

        public static string ToJson(PlayerRecord r)
        {
            string creatureKills = "{" + string.Join(",", r.CreatureKills.Select(kv => "\"" + Companion.Esc(kv.Key) + "\":" + kv.Value)) + "}";
            string deathHistory = "[" + string.Join(",", r.DeathHistory) + "]";
            string vanillaStats = "{" + string.Join(",", r.VanillaStats.Select(kv => "\"" + Companion.Esc(kv.Key) + "\":" + Companion.F(kv.Value))) + "}";
            string skillLevels = "{" + string.Join(",", r.SkillLevels.Select(kv => "\"" + Companion.Esc(kv.Key) + "\":" + Companion.F(kv.Value))) + "}";
            string skillProgress = "{" + string.Join(",", r.SkillProgress.Select(kv => "\"" + Companion.Esc(kv.Key) + "\":" + Companion.F(kv.Value))) + "}";
            string resourcesHarvested = "{" + string.Join(",", r.ResourcesHarvested.Select(kv => "\"" + Companion.Esc(kv.Key) + "\":" + kv.Value)) + "}";

            var sb = new StringBuilder("{");
            sb.Append("\"name\":\"" + Companion.Esc(r.Name) + "\",");
            sb.Append("\"first_seen\":\"" + Companion.Esc(r.FirstSeen) + "\",");
            sb.Append("\"last_seen\":\"" + Companion.Esc(r.LastSeen) + "\",");
            sb.Append("\"online\":" + Companion.B(r.Online) + ",");
            sb.Append("\"session_start\":" + (r.SessionStart != null ? "\"" + Companion.Esc(r.SessionStart) + "\"" : "null") + ",");
            sb.Append("\"playtime_seconds_lifetime\":" + r.PlaytimeSecondsLifetime + ",");
            sb.Append("\"kills_narrative\":" + r.Kills + ",");
            sb.Append("\"deaths_narrative\":" + r.Deaths + ",");
            sb.Append("\"gear_tier\":" + r.GearTier + ",");
            sb.Append("\"active_title\":\"" + Companion.Esc(r.ActiveTitle) + "\",");
            sb.Append("\"titles_earned\":[" + string.Join(",", r.EarnedTitles.Select(t => "\"" + Companion.Esc(t) + "\"")) + "],");
            sb.Append("\"biomes_discovered\":[" + string.Join(",", r.BiomesDiscovered.Select(b => "\"" + Companion.Esc(b) + "\"")) + "],");
            sb.Append("\"boss_kills_credited\":" + r.BossKillsCredited + ",");
            sb.Append("\"creature_kills\":" + creatureKills + ",");
            sb.Append("\"session_kills\":" + r.SessionKills + ",");
            sb.Append("\"damage_dealt_session\":" + Companion.F(r.SessionDmgDone) + ",");
            sb.Append("\"damage_taken_session\":" + Companion.F(r.SessionDmgTaken) + ",");
            sb.Append("\"blocks_session\":" + r.SessionBlocks + ",");
            sb.Append("\"parries_session\":" + r.SessionParries + ",");
            sb.Append("\"damage_blocked_session\":" + Companion.F(r.SessionDmgBlocked) + ",");
            sb.Append("\"kills_observed_lifetime\":" + r.TotalKillsLifetime + ",");
            sb.Append("\"deaths_lifetime\":" + r.TotalDeathsLifetime + ",");
            sb.Append("\"bosses_defeated\":[" + string.Join(",", r.DefeatedBosses.Select(b => "\"" + Companion.Esc(b) + "\"")) + "],");
            sb.Append("\"caught_fish\":[" + string.Join(",", r.CaughtFish.Select(f => "\"" + Companion.Esc(f) + "\"")) + "],");
            sb.Append("\"death_history\":" + deathHistory + ",");
            sb.Append("\"vanilla_stats\":" + vanillaStats + ",");
            sb.Append("\"skill_levels\":" + skillLevels + ",");
            sb.Append("\"skill_progress\":" + skillProgress + ",");
            sb.Append("\"builds_placed\":" + r.BuildsPlaced + ",");
            sb.Append("\"builds_removed\":" + r.BuildsRemoved + ",");
            sb.Append("\"builds_repaired\":" + r.BuildsRepaired + ",");
            sb.Append("\"items_crafted\":" + r.ItemsCrafted + ",");
            sb.Append("\"items_upgraded\":" + r.ItemsUpgraded + ",");
            sb.Append("\"items_repaired\":" + r.ItemsRepaired + ",");
            sb.Append("\"resources_harvested\":" + resourcesHarvested + ",");
            sb.Append("\"consumables_eaten\":" + r.ConsumablesEaten + ",");
            sb.Append("\"bosses_summoned\":" + r.BossesSummoned + ",");
            sb.Append("\"guardian_powers_used\":" + r.GuardianPowersUsed);
            sb.Append("}");
            return sb.ToString();
        }

        // ── The BarrkBOT-facing export: one aggregate file, all players. ──
        public static string BuildBarrkBotJson(string worldName, int day, int onlineCount)
        {
            var sb = new StringBuilder("{");
            sb.Append("\"generated_at\":\"" + DateTime.UtcNow.ToString("o") + "\",");
            sb.Append("\"world_name\":\"" + Companion.Esc(worldName) + "\",");
            sb.Append("\"day\":" + day + ",");
            sb.Append("\"online_count\":" + onlineCount + ",");
            sb.Append("\"raid_active\":" + Companion.B(WorldState.RaidActive) + ",");
            sb.Append("\"raid_type\":\"" + Companion.Esc(WorldState.RaidType) + "\",");
            sb.Append("\"players\":{");
            sb.Append(string.Join(",", All.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(r => "\"" + Companion.Esc(r.Name) + "\":" + ToJson(r))));
            sb.Append("}}");
            return sb.ToString();
        }
    }
}
