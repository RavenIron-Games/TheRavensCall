using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace TheRavensCall
{
    // ═════════════════════════════════════════════════════════════════════════
    // WORLD CENSUS — 1.5.0 (docs/SCOPE-1.5.0.md). A periodic walk of every ZDO
    // in the loaded world (Companion.GetAllZDOs, reused here) that classifies
    // each object by component — not by name, so modded portals/beds/wards/
    // ships/carts/chests/stations count for free (§2.1) — into seven groups,
    // tallies totals and per-builder counts, and lists portals/beds/wards
    // individually with position. Served by GET /api/census (Companion.cs)
    // from a cache this class rebuilds on the main thread, from
    // Companion.PollAllPlayers, on its own CensusIntervalMinutes schedule —
    // never a live walk from an HTTP worker thread, same discipline as
    // Companion._stateCache/_activityCache.
    //
    // Builder names (§3) come from a small persisted id→name map
    // (BepInEx/config/TheRavensCall/player_ids.json), fed only by the two
    // free sources the contract names: connected peers (Plugin.
    // GetConnectedPlayers' playerID) and claimed beds' owner pair. This never
    // touches PlayerRecord/PlayerRegistry's own JSON — that is the BarrkBOT
    // export's shape, untouched by this feature.
    // ═════════════════════════════════════════════════════════════════════════
    internal static class WorldCensus
    {
        private const string GroupPortals = "portals";
        private const string GroupBeds = "beds";
        private const string GroupWards = "wards";
        private const string GroupShips = "ships";
        private const string GroupCarts = "carts";
        private const string GroupChests = "chests";
        private const string GroupStations = "stations";
        // Fixed order — both the classification table (§2.1) and the JSON
        // "groups" object (§5) walk this same array, so the two never drift.
        private static readonly string[] GroupOrder =
            { GroupPortals, GroupBeds, GroupWards, GroupShips, GroupCarts, GroupChests, GroupStations };

        private const int MaxListRows = 500;

        // ── Prefab classification cache — built once, lazily, the first time
        // ZNetScene.instance exists (§2.1), from ZNetScene.instance.m_prefabs
        // keyed by the prefab's stable hash (ZDO.GetPrefab()'s own return
        // value), so the per-object lookup in the walk below is a single
        // dictionary hit. ────────────────────────────────────────────────
        private class PrefabInfo
        {
            public string Group;
            public string PortalKind;   // portals only: "wood" / "stone" / the prefab name
            public string DisplayName;  // localized Piece.m_name via Companion.Loc, else the prefab name (§2.1) — kept for parity with the classification table; not surfaced in the §5 JSON shape today.
        }
        private static Dictionary<int, PrefabInfo> _prefabGroups;

        private static void EnsurePrefabGroups()
        {
            if (_prefabGroups != null) return;
            if (ZNetScene.instance == null) return; // try again on the next scheduled run

            var map = new Dictionary<int, PrefabInfo>();
            foreach (var prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;

                string group = null;
                string portalKind = null;
                if (prefab.GetComponent<TeleportWorld>() != null)
                {
                    group = GroupPortals;
                    portalKind = prefab.name == "portal_wood" ? "wood" : (prefab.name == "portal_stone" ? "stone" : prefab.name);
                }
                else if (prefab.GetComponent<Bed>() != null) group = GroupBeds;
                else if (prefab.GetComponent<PrivateArea>() != null) group = GroupWards;
                else if (prefab.GetComponent<Ship>() != null) group = GroupShips;
                else if (prefab.GetComponent<Vagon>() != null) group = GroupCarts;
                // Container AND Piece: placeable chests, vanilla or modded (§2.1).
                // World-generated chests (the TreasureChest_* prefabs in
                // locations and dungeons) carry Piece too, so they land in
                // `total`; `player_built` (s_creator != 0) is the player-
                // placed figure. Storm10 2026-09-22: 370 total / 0 player-
                // built on a world nobody had placed a chest in yet.
                else if (prefab.GetComponent<Container>() != null && prefab.GetComponent<Piece>() != null) group = GroupChests;
                else if (prefab.GetComponent<CraftingStation>() != null) group = GroupStations;
                if (group == null) continue;

                var piece = prefab.GetComponent<Piece>();
                string displayName = (piece != null && !string.IsNullOrEmpty(piece.m_name)) ? Companion.Loc(piece.m_name) : prefab.name;

                int hash = prefab.name.GetStableHashCode();
                map[hash] = new PrefabInfo { Group = group, PortalKind = portalKind, DisplayName = displayName };
            }
            _prefabGroups = map;
            Companion.Log.LogInfo("[TheRavensCall] World census: classified " + map.Count + " prefab(s) across 7 groups.");
        }

        // ── Builder id → name map (§3) ──────────────────────────────────────
        private static readonly Dictionary<long, string> _idToName = new Dictionary<long, string>();
        private static readonly object _idLock = new object();
        private static bool _idsDirty = false;

        private static string IdsPath => Path.Combine(Companion.OutputDir, "player_ids.json");

        // Called once from Patch_ZNetAwake.Postfix, next to EventFeed.Init()
        // (Saga.cs). Also resets the prefab classification cache and the
        // scheduler clock, since ZNetScene/the world are new for this
        // session — mirrors EventFeed.Init's own per-session reset.
        internal static void Init()
        {
            _prefabGroups = null;
            _lastRunUtc = DateTime.MinValue;
            _loggedFirstRun = false;
            _warnedEmptyZdos = false;
            _warnedPortalsTruncated = false;
            _warnedBedsTruncated = false;
            _warnedWardsTruncated = false;
            _warnedSlowRun = false;
            // A fresh world starts from the empty envelope (built from the
            // configured interval, not a hardcoded one) — the previous
            // world's count must not be served for up to a full interval
            // (review 2026-09-22).
            Companion._censusCache = EmptyEnvelope();

            try
            {
                lock (_idLock) { _idToName.Clear(); _idsDirty = false; }

                string path = IdsPath;
                if (!File.Exists(path)) return;
                string raw = File.ReadAllText(path, Encoding.UTF8);
                var entries = Companion.JsonGetArray(raw, "ids");

                int loaded = 0, dropped = 0;
                lock (_idLock)
                {
                    foreach (var e in entries)
                    {
                        long id = Companion.JsonGetLong(e, "id");
                        string name = Companion.JsonGetString(e, "name");
                        if (id == 0 || string.IsNullOrEmpty(name)) { dropped++; continue; }
                        _idToName[id] = name;
                        loaded++;
                    }
                }
                if (dropped > 0)
                    Companion.Log.LogWarning("[TheRavensCall] World census: dropped " + dropped + " malformed row(s) from player_ids.json.");
                Companion.Log.LogInfo("[TheRavensCall] World census: loaded " + loaded + " known builder name(s) from player_ids.json.");
            }
            catch (Exception ex) { Companion.Log.LogWarning("[TheRavensCall] World census: player_ids.json load failed: " + ex.Message); }
        }

        // id == 0 (not a player) or an empty name are never recorded — 0 is
        // "no creator" (§2), not an unknown player.
        private static void NoteBuilder(long id, string name)
        {
            if (id == 0 || string.IsNullOrEmpty(name)) return;
            lock (_idLock)
            {
                if (_idToName.TryGetValue(id, out string existing) && existing == name) return;
                _idToName[id] = name;
                _idsDirty = true;
            }
        }

        private static string ResolveName(long id)
        {
            if (id == 0) return null;
            lock (_idLock) { return _idToName.TryGetValue(id, out string name) ? name : null; }
        }

        // Written through PlayerRegistry.AtomicWrite only when a new pair
        // appeared this run (§3) — not on every tick.
        private static void PersistIdsIfDirty()
        {
            string json;
            lock (_idLock)
            {
                if (!_idsDirty) return;
                var sb = new StringBuilder("{\"ids\":[");
                bool first = true;
                foreach (var kv in _idToName)
                {
                    if (!first) sb.Append(',');
                    sb.Append("{\"id\":").Append(N(kv.Key)).Append(",\"name\":\"").Append(Companion.Esc(kv.Value)).Append("\"}");
                    first = false;
                }
                sb.Append("]}");
                json = sb.ToString();
            }
            try
            {
                Directory.CreateDirectory(Companion.OutputDir);
                PlayerRegistry.AtomicWrite(IdsPath, json);
                lock (_idLock) { _idsDirty = false; }
            }
            catch (Exception ex) { Companion.Log.LogWarning("[TheRavensCall] World census: player_ids.json write failed: " + ex.Message); }
        }

        // ── Scheduling (§4) ─────────────────────────────────────────────────
        private static DateTime _lastRunUtc = DateTime.MinValue;
        private static bool _loggedFirstRun = false;
        private static bool _warnedEmptyZdos = false;
        private static bool _warnedPortalsTruncated = false;
        private static bool _warnedBedsTruncated = false;
        private static bool _warnedWardsTruncated = false;
        // Fires once per session, like the truncation flags above — §4 says
        // "never log every run", but a mature world's scan can land over
        // 250 ms on every single run, which used to log identically forever
        // (review 2026-09-22).
        private static bool _warnedSlowRun = false;

        private static int ClampedIntervalMinutes()
        {
            int raw = Plugin.CensusIntervalMinutes != null ? Plugin.CensusIntervalMinutes.Value : 5;
            // 0 is "off" (§4); any other non-positive value is a typo, not a
            // request to disable the census, so it clamps to 1 like the
            // positive side below instead of silently folding into "off"
            // (review 2026-09-22).
            if (raw == 0) return 0;
            return Math.Max(1, Math.Min(1440, raw));
        }

        // Called every poll tick (Companion.PollAllPlayers, gated the same as
        // _stateCache/_activityCache — HTTP server on). No-ops until the
        // configured interval has elapsed, so a full walk never runs more
        // often than once every CensusIntervalMinutes regardless of how
        // often the poll tick itself fires (StatsPushIntervalSeconds).
        internal static void MaybeRun()
        {
            try
            {
                int minutes = ClampedIntervalMinutes();
                if (minutes <= 0)
                {
                    Companion._censusCache = BuildEmptyEnvelope(false, 0);
                    return;
                }

                if (_lastRunUtc != DateTime.MinValue && (DateTime.UtcNow - _lastRunUtc).TotalMinutes < minutes) return;
                if (ZNetScene.instance == null || ZDOMan.instance == null) return; // world not fully up yet — try next tick

                _lastRunUtc = DateTime.UtcNow;
                Run(minutes);
            }
            catch (Exception ex) { Companion.Log.LogError("[TheRavensCall] WorldCensus.MaybeRun: " + ex.Message); }
        }

        // ── Per-builder / per-list row shapes ───────────────────────────────
        private class BuilderRow
        {
            public long Id;
            public string Name;
            public int Portals, Beds, Wards, Ships, Carts, Chests, Stations, Pieces;

            public void Increment(string group)
            {
                if (group == GroupPortals) Portals++;
                else if (group == GroupBeds) Beds++;
                else if (group == GroupWards) Wards++;
                else if (group == GroupShips) Ships++;
                else if (group == GroupCarts) Carts++;
                else if (group == GroupChests) Chests++;
                else if (group == GroupStations) Stations++;
            }
        }
        private class PortalRow { public string Tag; public string Kind; public long BuilderId; public string BuilderName; public bool Connected; public int X, Z; }
        private class BedRow { public string Owner; public long BuilderId; public string BuilderName; public int X, Z; }
        private class WardRow { public long BuilderId; public string BuilderName; public bool Enabled; public int X, Z; }

        private static void Run(int intervalMinutes)
        {
            try
            {
                EnsurePrefabGroups();
                if (_prefabGroups == null) return; // ZNetScene disappeared mid-tick; try again next time

                // duration_ms is the walk itself: prefab classification (one-
                // off, above) and the player_ids.json write (after sw.Stop())
                // stay outside the clock (review 2026-09-22).
                var sw = Stopwatch.StartNew();

                var allZdos = Companion.GetAllZDOs();
                if (allZdos.Count == 0 && ZDOMan.instance != null && !_warnedEmptyZdos)
                {
                    _warnedEmptyZdos = true;
                    Companion.Log.LogWarning("[TheRavensCall] World census: GetAllZDOs() returned 0 objects while ZDOMan.instance exists — the reflected field may have been renamed by a game update.");
                }

                // Free name source #1 (§3): every ready peer, matched against
                // the character ZDO's own playerID.
                foreach (var p in Plugin.GetConnectedPlayers())
                    NoteBuilder(p.playerID, p.name);

                var totals = new Dictionary<string, int[]>(); // group -> [total, player_built]
                foreach (var g in GroupOrder) totals[g] = new int[2];

                var builders = new Dictionary<long, BuilderRow>();
                var portals = new List<PortalRow>();
                var beds = new List<BedRow>();
                var wards = new List<WardRow>();

                foreach (var zdo in allZdos)
                {
                    long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
                    bool isGroupMember = _prefabGroups.TryGetValue(zdo.GetPrefab(), out PrefabInfo info);

                    BuilderRow row = null;
                    if (creator != 0)
                    {
                        if (!builders.TryGetValue(creator, out row))
                        {
                            row = new BuilderRow { Id = creator };
                            builders[creator] = row;
                        }
                        row.Pieces++; // every object this creator placed, any prefab (§2.1)
                    }

                    if (!isGroupMember) continue;

                    var t = totals[info.Group];
                    t[0]++;
                    if (creator != 0) t[1]++;
                    if (row != null) row.Increment(info.Group);

                    Vector3 pos = zdo.GetPosition();
                    int x = Mathf.RoundToInt(pos.x);
                    int z = Mathf.RoundToInt(pos.z);

                    if (info.Group == GroupPortals)
                    {
                        string tag = zdo.GetString(ZDOVars.s_tag, "");
                        bool connected = zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None;
                        portals.Add(new PortalRow { Tag = tag, Kind = info.PortalKind, BuilderId = creator, Connected = connected, X = x, Z = z });
                    }
                    else if (info.Group == GroupBeds)
                    {
                        long ownerId = zdo.GetLong(ZDOVars.s_owner, 0L);
                        string ownerName = zdo.GetString(ZDOVars.s_ownerName, "");
                        // Free name source #2 (§3): a claimed bed's owner pair.
                        if (ownerId != 0 && !string.IsNullOrEmpty(ownerName)) NoteBuilder(ownerId, ownerName);
                        beds.Add(new BedRow { Owner = string.IsNullOrEmpty(ownerName) ? null : ownerName, BuilderId = creator, X = x, Z = z });
                    }
                    else if (info.Group == GroupWards)
                    {
                        // PrivateArea keeps its own creatorName on the ZDO (§2);
                        // used directly, with the persisted id map only as a
                        // fallback when that field is empty (an older ward
                        // placed before the field existed).
                        string creatorName = zdo.GetString(ZDOVars.s_creatorName, "");
                        bool enabled = zdo.GetBool(ZDOVars.s_enabled, false);
                        // Free name source #3 (mirrors the bed branch above):
                        // a ward's own creatorName pair, so the same builder
                        // isn't "Nomad" here and "Unknown (id …)" in
                        // builders[]/portals[]/beds[] on the same census
                        // (review 2026-09-22).
                        if (creator != 0 && !string.IsNullOrEmpty(creatorName)) NoteBuilder(creator, creatorName);
                        wards.Add(new WardRow { BuilderId = creator, BuilderName = string.IsNullOrEmpty(creatorName) ? null : creatorName, Enabled = enabled, X = x, Z = z });
                    }
                }

                // Second, cheap pass over the (bounded) result collections —
                // resolves every builder id against the id map now that
                // every free source this run could add (peers above, bed
                // owners during the walk) has already been noted. Avoids a
                // second walk of the full (hundreds-of-thousands) ZDO list
                // just to get name-resolution ordering right.
                foreach (var row in builders.Values) row.Name = ResolveName(row.Id);
                foreach (var p in portals) p.BuilderName = ResolveName(p.BuilderId);
                foreach (var b in beds) b.BuilderName = ResolveName(b.BuilderId);
                foreach (var w in wards) if (w.BuilderName == null) w.BuilderName = ResolveName(w.BuilderId);

                int unknownBuilders = 0;
                foreach (var row in builders.Values) if (row.Name == null) unknownBuilders++;

                var builderList = new List<BuilderRow>(builders.Values);
                // Sorted by pieces descending then name (§5); unknown (null)
                // names sort after every known name, id as a final tiebreak.
                builderList.Sort((a, b) =>
                {
                    int c = b.Pieces.CompareTo(a.Pieces);
                    return c != 0 ? c : CompareNameThenId(a.Name, a.Id, b.Name, b.Id);
                });

                // Sorted by tag (ordinal, case-insensitive) then builder (§5).
                portals.Sort((a, b) =>
                {
                    int c = string.Compare(a.Tag ?? "", b.Tag ?? "", StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : CompareNameThenId(a.BuilderName, a.BuilderId, b.BuilderName, b.BuilderId);
                });
                // Sorted by builder then x (§5).
                beds.Sort((a, b) =>
                {
                    int c = CompareNameThenId(a.BuilderName, a.BuilderId, b.BuilderName, b.BuilderId);
                    return c != 0 ? c : a.X.CompareTo(b.X);
                });
                wards.Sort((a, b) =>
                {
                    int c = CompareNameThenId(a.BuilderName, a.BuilderId, b.BuilderName, b.BuilderId);
                    return c != 0 ? c : a.X.CompareTo(b.X);
                });

                // 500-row caps (§5) — trim and flag; log once per list per run.
                bool portalsTruncated = portals.Count > MaxListRows;
                bool bedsTruncated = beds.Count > MaxListRows;
                bool wardsTruncated = wards.Count > MaxListRows;
                if (portalsTruncated) portals.RemoveRange(MaxListRows, portals.Count - MaxListRows);
                if (bedsTruncated) beds.RemoveRange(MaxListRows, beds.Count - MaxListRows);
                if (wardsTruncated) wards.RemoveRange(MaxListRows, wards.Count - MaxListRows);
                if (portalsTruncated && !_warnedPortalsTruncated) { _warnedPortalsTruncated = true; Companion.Log.LogWarning("[TheRavensCall] World census: portals list truncated to " + MaxListRows + " rows."); }
                if (bedsTruncated && !_warnedBedsTruncated) { _warnedBedsTruncated = true; Companion.Log.LogWarning("[TheRavensCall] World census: beds list truncated to " + MaxListRows + " rows."); }
                if (wardsTruncated && !_warnedWardsTruncated) { _warnedWardsTruncated = true; Companion.Log.LogWarning("[TheRavensCall] World census: wards list truncated to " + MaxListRows + " rows."); }

                sw.Stop();
                long ms = sw.ElapsedMilliseconds;
                PersistIdsIfDirty();
                string generatedAt = DateTime.UtcNow.ToString("o");

                string json = BuildJson(generatedAt, intervalMinutes, allZdos.Count, ms, totals, builderList, unknownBuilders,
                    portals, portalsTruncated, beds, bedsTruncated, wards, wardsTruncated);
                Companion._censusCache = json;

                if (!_loggedFirstRun)
                {
                    _loggedFirstRun = true;
                    Companion.Log.LogInfo("[TheRavensCall] World census: " + allZdos.Count + " objects in " + ms + " ms.");
                }
                if (ms > 250 && !_warnedSlowRun)
                {
                    _warnedSlowRun = true;
                    Companion.Log.LogWarning("[TheRavensCall] World census: scan took " + ms + " ms (> 250 ms) for " + allZdos.Count + " objects. Logged once per session.");
                }
            }
            catch (Exception ex) { Companion.Log.LogError("[TheRavensCall] WorldCensus.Run: " + ex); }
        }

        private static int CompareNameThenId(string aName, long aId, string bName, long bId)
        {
            if (aName == null && bName == null) return aId.CompareTo(bId);
            if (aName == null) return 1;
            if (bName == null) return -1;
            int c = string.Compare(aName, bName, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : aId.CompareTo(bId);
        }

        // ── JSON (§5) — string-built like EventFeed/SeasonSystem, all user
        // text (tags, names) through Companion.Esc. ────────────────────────

        private static string GroupsJson(Dictionary<string, int[]> totals)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < GroupOrder.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var t = totals[GroupOrder[i]];
                sb.Append('"').Append(GroupOrder[i]).Append("\":{\"total\":").Append(N(t[0])).Append(",\"player_built\":").Append(N(t[1])).Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string EmptyGroupsJson()
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < GroupOrder.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(GroupOrder[i]).Append("\":{\"total\":0,\"player_built\":0}");
            }
            sb.Append('}');
            return sb.ToString();
        }

        // Pre-first-run (enabled:true) and CensusIntervalMinutes=0
        // (enabled:false) share this same empty-but-shaped envelope (§4) —
        // /api/census never 404s and never changes shape.
        // What /api/census serves before Init() has run or before the first
        // completed count: the configured interval, enabled:false when it is 0.
        internal static string EmptyEnvelope()
        {
            int minutes = ClampedIntervalMinutes();
            return BuildEmptyEnvelope(minutes > 0, minutes);
        }

        // Every integer in the JSON goes through this: StringBuilder.Append(int)
        // and string concatenation format with the current culture, whose
        // negative sign is not '-' everywhere (review 2026-09-22).
        private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

        private static string BuildEmptyEnvelope(bool enabled, int intervalMinutes)
        {
            return "{\"generated_at\":\"\",\"enabled\":" + Companion.B(enabled) + ",\"interval_minutes\":" + N(intervalMinutes) +
                   ",\"scanned_objects\":0,\"duration_ms\":0,\"groups\":" + EmptyGroupsJson() +
                   ",\"builders\":[],\"unknown_builders\":0,\"portals\":[],\"beds\":[],\"wards\":[]}";
        }

        private static string BuildJson(string generatedAt, int intervalMinutes, int scannedObjects, long durationMs,
            Dictionary<string, int[]> totals, List<BuilderRow> builders, int unknownBuilders,
            List<PortalRow> portals, bool portalsTruncated, List<BedRow> beds, bool bedsTruncated, List<WardRow> wards, bool wardsTruncated)
        {
            var sb = new StringBuilder();
            sb.Append("{\"generated_at\":\"").Append(generatedAt).Append('"');
            sb.Append(",\"enabled\":true,\"interval_minutes\":").Append(N(intervalMinutes));
            sb.Append(",\"scanned_objects\":").Append(N(scannedObjects));
            sb.Append(",\"duration_ms\":").Append(N(durationMs));
            sb.Append(",\"groups\":").Append(GroupsJson(totals));

            sb.Append(",\"builders\":[");
            for (int i = 0; i < builders.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var b = builders[i];
                sb.Append("{\"name\":").Append(b.Name == null ? "null" : "\"" + Companion.Esc(b.Name) + "\"");
                sb.Append(",\"id\":").Append(N(b.Id));
                sb.Append(",\"portals\":").Append(N(b.Portals));
                sb.Append(",\"beds\":").Append(N(b.Beds));
                sb.Append(",\"wards\":").Append(N(b.Wards));
                sb.Append(",\"ships\":").Append(N(b.Ships));
                sb.Append(",\"carts\":").Append(N(b.Carts));
                sb.Append(",\"chests\":").Append(N(b.Chests));
                sb.Append(",\"stations\":").Append(N(b.Stations));
                sb.Append(",\"pieces\":").Append(N(b.Pieces));
                sb.Append('}');
            }
            sb.Append(']');
            sb.Append(",\"unknown_builders\":").Append(N(unknownBuilders));

            sb.Append(",\"portals\":[");
            for (int i = 0; i < portals.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var p = portals[i];
                sb.Append("{\"tag\":\"").Append(Companion.Esc(p.Tag)).Append('"');
                sb.Append(",\"kind\":\"").Append(Companion.Esc(p.Kind)).Append('"');
                sb.Append(",\"builder\":").Append(p.BuilderName == null ? "null" : "\"" + Companion.Esc(p.BuilderName) + "\"");
                sb.Append(",\"builder_id\":").Append(N(p.BuilderId));
                sb.Append(",\"connected\":").Append(Companion.B(p.Connected));
                sb.Append(",\"x\":").Append(N(p.X)).Append(",\"z\":").Append(N(p.Z));
                sb.Append('}');
            }
            sb.Append(']');
            if (portalsTruncated) sb.Append(",\"portals_truncated\":true");

            sb.Append(",\"beds\":[");
            for (int i = 0; i < beds.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var b = beds[i];
                sb.Append("{\"owner\":").Append(b.Owner == null ? "null" : "\"" + Companion.Esc(b.Owner) + "\"");
                sb.Append(",\"builder\":").Append(b.BuilderName == null ? "null" : "\"" + Companion.Esc(b.BuilderName) + "\"");
                sb.Append(",\"builder_id\":").Append(N(b.BuilderId));
                sb.Append(",\"x\":").Append(N(b.X)).Append(",\"z\":").Append(N(b.Z));
                sb.Append('}');
            }
            sb.Append(']');
            if (bedsTruncated) sb.Append(",\"beds_truncated\":true");

            sb.Append(",\"wards\":[");
            for (int i = 0; i < wards.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var w = wards[i];
                sb.Append("{\"builder\":").Append(w.BuilderName == null ? "null" : "\"" + Companion.Esc(w.BuilderName) + "\"");
                sb.Append(",\"builder_id\":").Append(N(w.BuilderId));
                sb.Append(",\"enabled\":").Append(Companion.B(w.Enabled));
                sb.Append(",\"x\":").Append(N(w.X)).Append(",\"z\":").Append(N(w.Z));
                sb.Append('}');
            }
            sb.Append(']');
            if (wardsTruncated) sb.Append(",\"wards_truncated\":true");

            sb.Append('}');
            return sb.ToString();
        }
    }
}
