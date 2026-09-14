# TheRavensCall — implementation handoff: make combat visible

You are picking up a mod that runs healthy in production and records nothing
about combat. Kills, deaths, damage, fish: zero for all 28 tracked players,
after a full day of active play. This is not a bug in a patch body — see
`KILL_TRACKING_FINDINGS.md` (same folder) for the evidence and root cause.
Short form: `Character.Damage`/`Character.OnDeath`/`FishingFloat.Catch` are
owner-side simulation methods, zones with players in them are owned by those
players' clients, and the dedicated server owns only empty zones. The patches
are correct code attached to methods that never execute here.

Read `HANDOFF.md` first for project history and conventions. Then §1 here
before touching anything.

---

## 1. Traps — read all of these before writing code

### 1.1 This mod stays server-only. That is a settled decision, twice.

`HANDOFF.md` records the owner correcting a previous session **twice** that
TheRavensCall runs only on the dedicated server. Do not add a client build to
this project. The plan below respects that: **TheRavensCall gains only a
server-side RPC receiver.** The client-side reporter is a separate ~80-line
addition to an *existing, already-whitelisted Wubarrk client mod* (see §4) —
different project, different author identity (this mod is RavenIron's;
the client mods are Wubarrk's). If anyone asks you to put client code in
THIS repo, stop and surface the conflict.

### 1.2 `Player.GetAllPlayers()` is an empty list on a dedicated server

`HandleBossKill` (Saga.cs:500) walks `Player.GetAllPlayers()` for
proximity-based boss credit, and `ResolveAttacker` (Saga.cs:187) resolves a
`Player` instance. On the dedicated server there are **no Player objects at
all** — connected players exist as ZNet peers and ZDOs, not instantiated
`Player`s. Even the parts of the old kill path that look server-safe are
dead. Every credit path you touch must become **name + position based**:
`ZNet.instance.GetPlayerList()` returns `ZNetPeer` player info including
`m_position` — that is the server-side replacement for the
`Player.GetAllPlayers()` proximity walk.

### 1.3 The comment at Saga.cs:16 is the bug's origin — rewrite it

The architecture comment asserts Character.OnDeath/Damage "fire there for
real combat resolution" on the server. False, and it will mislead the next
reader the way it misled the last. Rewrite it to describe the actual model:
join/leave/raids/day are server-observed; combat arrives via the
`RavensCall_CombatReport_V1` RPC from the owner client.

### 1.4 The BarrkBOT export contract is frozen

`barrkbot_players.json` field names are read by BarrkBOT
(`src/actions/valheimRavensCall.js` on the BarrkBOT side). Do not rename:
`playtime_seconds_lifetime, kills_narrative, kills_observed_lifetime,
session_kills, creature_kills, damage_dealt_session, damage_taken_session,
deaths_lifetime, deaths_narrative, bosses_defeated, biomes_discovered,
caught_fish, titles_earned, active_title, gear_tier, death_history, online,
first_seen, last_seen, session_start`. Add fields freely; never rename or
re-type existing ones. On kill, increment BOTH `rec.Kills` (narrative) and
`rec.TotalKillsLifetime` (observed), exactly as the dead patch at
Saga.cs:462-470 does.

### 1.5 Practicalities that will cost you an hour each

- **Not a git repo.** No history, no branches. Update `HANDOFF.md` and
  `CHANGELOG.md` by hand when you finish.
- **The build tool is `/home/rohan/.dotnet/dotnet`**, not on PATH.
- **Deployment target folder is `/BepInEx/plugins/TheRavensCall-v1.0.0/`**
  on the game host, with the dll under its `plugins/` subfolder. If you bump
  the folder name with the version, fine — it is a server-side mod, no
  AzuAntiCheat marker exists for it — but BarrkBOT's mod tools read the
  folder name as the version, so keep folder name and manifest version in
  agreement.
- **Keep the dead patches** (`Patch_Damage`, `Patch_CharacterDeath`,
  `FishingFloat.Catch`). They cost nothing, they are correct on any future
  architecture where the server simulates combat, and the dedupe in §3.4
  makes double-counting impossible. Fix their misleading context, don't
  delete working code.
- **The mead of this project is the RPC schema**: get the ZPackage
  read/write order wrong between the two mods and you get silent garbage,
  not errors. The schema in §2 is the single source of truth — copy it into
  both implementations verbatim, version field first.

---

## 2. The contract: `RavensCall_CombatReport_V1`

One routed RPC, client → server, registered on both ends by name. Versioned
name so a future schema change is a new name, never a silent format change.

**ZPackage write order (schema version 1):**

| # | Type | Field | Notes |
|---|---|---|---|
| 1 | int | schemaVersion | always `1` |
| 2 | byte | eventType | 1=creatureKill 2=playerDeath 3=damageBatch 4=fishCatch |
| 3 | string | victimPrefab | creature prefab for kills; fish prefab for catches; `""` for playerDeath/damageBatch |
| 4 | string | attackerName | in-game player name being credited; for playerDeath the dying player; empty = unattributed |
| 5 | Vector3 | position | victim position (kill/death), reporter position otherwise |
| 6 | float | dmgDealt | damageBatch only, else 0 |
| 7 | float | dmgTaken | damageBatch only, else 0 |

**Sender**: the client that OWNS the victim (`m_nview.IsOwner()` gate — this
is the dedupe across clients; without it every nearby client reports the same
death). Send with
`ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "RavensCall_CombatReport_V1", pkg)`.

**Receiver trust model** (server side, all enforced before crediting):
- The *reporter* is identified from the routed-RPC `sender` long, resolved
  through `ZNet.instance.GetPeer(sender)` — never from the payload.
- `attackerName` must match a currently-connected player in
  `ZNet.instance.GetPlayerList()` (case-insensitive) or the event is dropped.
  A modded client can lie about names; it cannot invent a connected player.
- Rate cap per sender: 30 reports/second, drop and count beyond it. Clamp
  damage floats to [0, 10000], cap prefab strings at 64 chars.
- Config: `[Combat] AcceptClientReports=true`, `LogCombatReports=false`
  (set true during rollout, §6).

---

## 3. Work item A — the server receiver (this repo)

### 3.1 Registration
In `Patch_ZNetAwake.Postfix` (Saga.cs:247), after the existing init calls:

```csharp
ZRoutedRpc.instance.Register<ZPackage>("RavensCall_CombatReport_V1", CombatReportReceiver.Handle);
```

`ZRoutedRpc.instance` exists by ZNet.Awake postfix time; this is the same
lifecycle point everything else initializes at.

### 3.2 Handler skeleton

```csharp
public static class CombatReportReceiver
{
    public static void Handle(long sender, ZPackage pkg)
    {
        try
        {
            if (!Plugin.IsServer() || !Plugin.AcceptClientReports.Value) return;
            if (RateLimiter.Exceeded(sender)) return;

            int schema = pkg.ReadInt();
            if (schema != 1) return;                    // unknown future schema: ignore, never guess
            byte eventType = (byte)pkg.ReadByte();
            string prefab = Cap(pkg.ReadString(), 64);
            string attacker = Cap(pkg.ReadString(), 32);
            Vector3 pos = pkg.ReadVector3();
            float dmgDealt = Clamp(pkg.ReadSingle());
            float dmgTaken = Clamp(pkg.ReadSingle());

            string verified = VerifyConnectedPlayer(attacker);   // null if not connected
            switch (eventType)
            {
                case 1: if (verified != null) CreditCreatureKill(verified, prefab, pos); break;
                case 2: if (verified != null) CreditPlayerDeath(verified, pos); break;
                case 3: if (verified != null) CreditDamage(verified, dmgDealt, dmgTaken); break;
                case 4: if (verified != null) CreditFish(verified, prefab); break;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"[TheRavensCall] combat report error: {ex.Message}"); }
    }
}
```

### 3.3 Credit functions: reuse the existing paths, refactored to names

The dead patch bodies already contain the correct bookkeeping — move it, do
not rewrite it:

- `CreditCreatureKill(name, prefab, pos)`: the body of
  `Patch_CharacterDeath.Prefix`'s non-boss branch (Saga.cs:462-471):
  `TotalKillsLifetime++`, `Kills++`, `SessionKills++`, `Dirty = true`,
  `MilestoneTracker.OnKill(name, rec)`, `TitleSystem.OnCreatureKill(name,
  prefab, rec)` — these already take `(name, rec)`, no changes needed.
  If `TitleSystem.BossTitles.ContainsKey(prefab)` route to boss credit
  instead, as the old code did.
- **Boss credit**: `HandleBossKill(prefab, position, Player killer)` must
  become `HandleBossKill(prefab, position, string killerName)`. Its
  `Player.GetAllPlayers()` proximity walk (trap 1.2) becomes a walk of
  `ZNet.instance.GetPlayerList()` using each entry's `m_position` against
  `Plugin.BossCreditRadius` — same radius semantics, same "everyone nearby
  gets boss credit" behavior.
- **`HandlePlayerDeath(Player victim)`** becomes
  `HandlePlayerDeath(string name, Vector3 pos)` — it needs the name for the
  registry/Chronicle/death_history and the position for the narrative;
  nothing else it does requires the Player object.
- `CreditDamage`: `rec.SessionDmgDone += dmgDealt; rec.SessionDmgTaken += dmgTaken; rec.Dirty = true;`
- `CreditFish`: the body of the existing `FishingFloat.Catch` patch's
  bookkeeping.

### 3.4 Dedupe between report path and (dormant) patch path
Shared recent-event set keyed `eventType|attacker|prefab|position-rounded-to-4m`,
3-second window (same shape as `AlreadyProcessed`). Both the RPC handler and
the old patches check it. Today the patches never fire, so this is insurance,
not behavior — but it is what makes "keep the dead patches" safe forever.

### 3.5 Also in this repo, while you are here
- Rewrite the Saga.cs:16 architecture comment (trap 1.3).
- **Session reconcile** (finding #2 in `KILL_TRACKING_FINDINGS.md`): today
  shows 11 joins vs 5 leaves — crash disconnects bypass
  `RPC_Disconnect`/`SendDisconnect`, leaving `online=true` stale and sessions
  unclosed. The poll tick already computes the true peer list; at each tick,
  any registry record marked online whose name is not in
  `ZNet.instance.GetPlayerList()` gets closed exactly as the leave patch
  would close it (end session, add playtime, write the leave Chronicle event
  with a `(connection lost)` detail so it is distinguishable from a clean
  quit).
- Bump `PluginVersion` to `1.1.0`, update `HexiumDist/manifest.json`,
  `HexiumDist/CHANGELOG.md`, and `HANDOFF.md`'s status line.

---

## 4. Work item B — the client reporter (NOT this repo)

Lives in an existing Wubarrk client mod that players already install through
the AzuAntiCheat whitelist. Recommendation: **Fatty** (actively developed;
already has a BarrkBOT-adjacent work order in
`/home/rohan/WubarrkCODING/Fatty/FEAST_EXPORT.md`, so its next session is
already expected). ~80 lines:

1. **`Character.OnDeath` prefix, client-side** — fires authoritatively on the
   owner. Gate: `if (!__instance.m_nview.IsOwner()) return;` (trap: this IS
   the network-wide dedupe — exactly one machine owns the victim).
   - Victim is a creature: resolve attacker from
     `Plugin.GetLastHit`-equivalent (`HitData.GetAttacker()` on the owning
     client CAN resolve, unlike the server) → send eventType 1 with the
     attacker's player name. No attacker → send with empty name
     (unattributed; server drops it, same as the old code's
     `if (killer == null) return`).
   - Victim is the local player: send eventType 2 with own name.
2. **Damage accumulator**: in a `Character.Damage` prefix (owner-side),
   accumulate per-local-player dealt/taken floats; flush as eventType 3
   every 10 seconds or ±500 accumulated, whichever first. Never per-hit —
   one RPC per hit on a raid night is real traffic; a 10s batch is nothing.
3. **`FishingFloat.Catch` postfix** (owner-side): eventType 4 with the fish
   prefab.
4. All sends wrapped in try/catch + a single `IsServerConnected` guard; the
   reporter must never be able to crash a client over stats.

The receiving name string, schema and field order come from §2 — copy them,
do not re-derive them.

---

## 5. Work item C — BarrkBOT follow-ups (cross-repo, after A+B are live)

In `/home/rohan/WubarrkCODING/WindowsDEV/Discord-BarrkBOT/v5.7 - Final`:

1. **The `stat-not-recorded` probe case will false-fail the moment kills are
   real.** It forbids `/has the most kills with/i` — written when the only
   possible such sentence was fabricated. Once data exists, the correct
   answer "X has the most kills with 47" matches the forbidden pattern.
   Rewrite the case with a `derive(truth)` that reads the live kills
   leaderboard: if all-zero, keep the prohibition; if data exists, require
   the named leader to be the actual leader. The offline fixture archive
   (`probes/README.md`) makes this edit safe to verify in seconds.
2. **Soften-to-truth the zero-kills wording.** The bot currently says the
   kill counter "has only just started tracking" — after A+B ship this
   becomes true; if A+B are deferred, change it to "is not currently
   tracked" (one string in `src/actions/valheimRavensCall.js`).
3. Small version bump + changelog per the owner's standing convention.

---

## 6. Build, deploy, verify

```bash
cd /home/rohan/WubarrkCODING/TheRavensCall
/home/rohan/.dotnet/dotnet build -c Release        # dll lands in bin/
# refresh HexiumDist/plugins/, manifest.json, CHANGELOG.md, re-zip
```

Deploy over the same FTP credentials BarrkBOT uses (host `.env` on the
BarrkBOT box — **never print that file**): replace the dll under
`/BepInEx/plugins/TheRavensCall-v1.0.0/plugins/`, restart the Valheim
server. Set `LogCombatReports=true` for the first session.

**Verification ladder — every rung is observable without touching a client:**

1. Server log shows the receiver registered, no Harmony errors.
2. A volunteer (Bronzebeard has been on all day) kills one boar:
   - log line for the accepted report (`LogCombatReports`),
   - their `players/<name>.json` shows `session_kills=1`,
     `creature_kills` gains the prefab, `kills_observed_lifetime=1`,
   - `damage_dealt_session` goes nonzero within one 10s flush.
3. `barrkbot_players.json` aggregate reflects the same within one poll tick.
4. BarrkBOT, asked "who has the most kills?", stops giving the zero-guard
   answer and names the volunteer — after Work item C.1, or expect that
   probe case to fail until C.1 lands.
5. A volunteer death writes a `player_death` Chronicle event — the first one
   this mod will ever have recorded itself.
6. Pull the plug on a client (kill the process, no clean quit): within one
   poll tick the registry closes the session with `(connection lost)` and
   `online=false` — Work item A §3.5.
7. Turn `LogCombatReports` back off.

**Rollback**: the previous `TheRavensCall-v1.0.0.zip` in `HexiumDist/` is the
known-good build; restore its dll and restart. The registry JSON files are
forward-compatible (new code adds no required fields), so no data migration
in either direction.

---

## 7. What NOT to do

- Do not move or "fix" the server-side Character patches to try to make them
  fire — they cannot; that is the disproven premise this whole handoff
  exists to correct.
- Do not put client code in this repo (trap 1.1).
- Do not trust any player name in an RPC payload without verifying it
  against the connected-player list (§2 trust model).
- Do not rename export fields (trap 1.4).
- Do not send per-hit damage RPCs (§4.2).
- Do not claim completion until rung 5 of the verification ladder has been
  seen with your own eyes in the Chronicle file.
