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
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (string.IsNullOrEmpty(name)) continue;
                    var rec = LoadFromDisk(name);
                    if (rec != null) { rec.Name = name; _records[name] = rec; }
                }
                Plugin.Log.LogInfo($"[TheRavensCall] PlayerRegistry loaded {_records.Count} player record(s).");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[TheRavensCall] PlayerRegistry.LoadAll error: " + ex.Message); }
        }

        private static string SanitizedFileName(string playerName) =>
            Path.Combine(PlayersDir, SanitizeForFile(playerName) + ".json");

        private static string SanitizeForFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var sb = new StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static PlayerRecord LoadFromDisk(string playerName)
        {
            try
            {
                string path = SanitizedFileName(playerName);
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Encoding.UTF8);

                var rec = new PlayerRecord
                {
                    Name = playerName,
                    FirstSeen = Companion.JsonGetString(json, "first_seen") ?? DateTime.UtcNow.ToString("o"),
                    LastSeen = Companion.JsonGetString(json, "last_seen") ?? "",
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
                File.WriteAllText(SanitizedFileName(rec.Name), ToJson(rec), Encoding.UTF8);
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
