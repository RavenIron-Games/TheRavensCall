# Why every kill counter is zero, and what to do about it

Investigated 2026-08-16 after the owner asked: "has no one really not killed
anything yet?" Answer: **people have been killing things all day. The mod
cannot see any of it, and under its current design it never will.** This is
not a bug in a patch body — every combat patch is correctly written and
attached to a method that does not execute on a dedicated server.

## Evidence, all read live off the server

**The Chronicle for 2026-08-16** (18 events): `startup ×2, player_join ×11,
player_leave ×5`. Nothing else all day.

**Session counters for players online at the time of reading:**

```
Asfaloth:   online=true  session_kills=0  dmg_dealt=0.0  dmg_taken=0.0
SamkatWb:   online=true  session_kills=0  dmg_dealt=0.0  dmg_taken=0.0
```

Asfaloth had been on since 13:13 UTC — multiple hours. You cannot play
Valheim for an afternoon and take **0.0 damage**. The counters are not low;
they are disconnected.

**The registry across all 28 players**: kills, creature_kills, gear_tier,
bosses_defeated, biomes_discovered, caught_fish, titles_earned, death_history
— all empty for everyone. The only populated fields are playtime (28/28) and
deaths (19/28), and both of those were seeded by BarrkBOT's world-feed
backfill, not observed by this mod.

**The log**: no `Damage tracking error`, no `CharacterDeath error`, no
Harmony failures. The mod is healthy. The hooks simply never fire.

## Root cause

`Saga.cs` line 16 states the design premise:

> "every event is detected authoritatively on the dedicated server
> (Character.OnDeath/Damage fire there for real combat resolution)"

**That premise is false for Valheim's architecture.** Every entity is a ZDO
owned by exactly one peer, and the *owner* simulates it. `Character.Damage`
and `Character.OnDeath` are local simulation methods — they run on the machine
that owns the character. Combat only happens near players, zones near players
are owned by those players' *clients*, and the dedicated server owns only the
empty zones where nothing is fighting. So on the server:

| Patch | Fires server-side? | Explains |
|---|---|---|
| `Character.Damage` prefix | ~never for player combat | dmg_dealt/taken = 0.0 |
| `Character.OnDeath` prefix | ~never | kills, creature_kills, boss credits = 0 |
| `Character.OnDeath` → `HandlePlayerDeath` | ~never (dying player's client owns them) | no player_death Chronicle events, ever |
| `FishingFloat.Catch` | ~never (float is client-owned) | caught_fish will also stay empty |
| `ZNet.RPC_PeerInfo` / disconnect patches | YES — ZNet runs on the server | joins/leaves work |
| `RandEventSystem.SetRandomEventByName` | YES — the server runs random events | raid events work |
| Day/poll tick via `ZNet.Update` | YES | day + playtime work |

The pattern in the working rows is the fix's blueprint: **everything that
works is something the server itself runs.**

## Options, ranked

### A. Client-assisted reporting (reliable, exact, small)
The "no client install" purity is already spent: this community ships several
Wubarrk client mods through the AzuAntiCheat whitelist (Fatty, VikingOS,
Wings of the Valkyrie…). Add ~30 lines to one of them: patch
`Character.OnDeath` client-side (it fires authoritatively on the owner),
and send one custom routed RPC to the server — attacker player name, victim
prefab, position. TheRavensCall registers a server-side handler
(`ZRoutedRpc.Register`) and feeds the existing `PlayerRegistry` exactly where
`Patch_CharacterDeath` writes today. Damage totals same way, batched (e.g.
one RPC per 10s of accumulated damage, or on ±100 damage) to keep traffic
trivial. Kill credit becomes exact, including boss credits and creature
histograms.

### B. Server-side relay sniffing (keeps server-only, fuzzier)
Every routed RPC between clients relays through the server. Patch
`ZRoutedRpc.HandleRoutedRPC` on the server and decode packages whose method
hash matches the damage family (`Damage`, `DamageText`) — `HitData`
deserializes from the package and carries the attacker ZDOID and damage, which
covers dmg_dealt/dmg_taken exactly. Kills are harder: there is no single
reliable "died" routed RPC across game versions; you would infer deaths from
ZDO destruction shortly after damage traffic (heuristic, needs testing against
the current game version). Real effort, some wrongly-credited edge cases.

### C. Do both
A for kills/deaths/fish (exact where exactness matters), B's damage sniffing
only if you want damage without touching clients.

Recommendation: **A.** The client-mod channel already exists, the change is
tiny, and it turns every currently-dead field on with exact data.

## Two smaller findings from the same read

1. **Join/leave asymmetry**: 11 joins vs 5 leaves today, while the poll tick
   logged `0 online` — crash/timeout disconnects don't pass through
   `RPC_Disconnect`/`SendDisconnect`, so sessions are left open and the
   registry's `online` flag goes stale (Asfaloth showed `online=true` while
   the poll saw 0). The poll already knows the truth — reconcile the
   registry's online flags and close orphaned sessions from the poll's peer
   list instead of trusting the disconnect patches.
2. **BarrkBOT wording**: BarrkBOT currently tells members the kill counter
   "has only just started tracking", which implies data is coming. Until one
   of the fixes above ships, it never will. After the mod-side fix, that
   wording becomes true on its own; if the fix is deferred, the bot's note
   should say the stat is not currently tracked.

## What was verified working, for the avoidance of doubt

BarrkBOT's readers are fine: the aggregate export, per-player files and
Chronicle are all read correctly, and the bot honestly reports "not yet
recorded" rather than presenting zeros as fact. Fixing the mod requires no
BarrkBOT changes — the moment real numbers appear in the export, every
leaderboard and stat answer lights up on its own.
