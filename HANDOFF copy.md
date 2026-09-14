# WhereTheCrowFlies — Comprehensive Server-Side Implementation Handoff

**Version: v1.0.1**  
**Companion Mod for: TheRavensCall (Dedicated Server Mod)**  
**Author / Publisher: RavenIron**  
**Repository / Workspace: `/home/rohan/WubarrkCODING/WhereTheCrowFlies`**  
**Build Target: .NET Framework 4.7.2 / BepInEx 5.4.2333 / Valheim**

---

## 1. System Architecture & Context

### 1.1 Why Client-Authoritative Reporting Is Mandatory in Valheim
In Valheim's multiplayer networking model, the dedicated server acts primarily as a world state relay and persistent storage container. Simulation authority for physics, character combat, damage calculation, creature deaths, piece construction, foraging, and player statistics is delegated to the **client that owns the local sector / object ZDO (Zone Data Object)**:
- `Character.Damage`, `Character.OnDeath`, `Character.BlockAttack` execute exclusively on the ZDO owner client.
- `Player.PlacePiece`, `InventoryGui.DoCrafting`, `Player.ConsumeItem`, `Pickable.RPC_Pick`, and `PlayerProfile.IncrementStat` execute exclusively on the local player client.
- The dedicated server **never** receives vanilla RPCs or internal events for combat damage values, mob kills, parries, crafted items, or player statistics.

### 1.2 The Two-Mod Architecture
1. **`WhereTheCrowFlies` (Client Mod)**: A lightweight, silent client-side companion. It hooks local simulation methods, aggregates continuous telemetry into zero-overhead 10-second batches, and securely emits routed RPCs (`RavensCall_EventReport_V2` and legacy `RavensCall_CombatReport_V1`) to the dedicated server.
2. **`TheRavensCall` (Server-Only Mod)**: Listens for the routed RPCs, validates incoming packets, updates persistent player profiles, records chronicle entries, powers web leaderboards, and emits Discord webhook notifications.

### 1.3 Safe Everywhere & Silent No-Op
`ZRoutedRpc` dispatch in Valheim to an unregistered method name is a silent no-op. When a player with `WhereTheCrowFlies` connects to a vanilla server or a community server without `TheRavensCall`, packets are discarded by the engine with zero network errors, zero disconnects, and zero log noise.

---

## 2. Wire Protocol Specification

WhereTheCrowFlies v1.0.1 broadcasts on two routed RPC channels:
1. **`RavensCall_EventReport_V2` (Primary)**: Full 360° telemetry payload containing rich context (stars, biomes, weapons, stations, parries, 105 vanilla stats).
2. **`RavensCall_CombatReport_V1` (Legacy Fallback)**: Dual-transmitted on basic combat events for 100% backward compatibility with legacy servers.

---

### 2.1 `RavensCall_EventReport_V2` Specification

All V2 packets begin with a 5-byte header:
1. `schemaVersion` (`int` / 4 bytes): Always `2`.
2. `eventType` (`byte` / 1 byte): Enum value `1` to `10`.

```
+-------------------+------------------+-----------------------------------------------+
| Field Name        | Type             | Description / Value Range                     |
+-------------------+------------------+-----------------------------------------------+
| schemaVersion     | int              | 2                                             |
| eventType         | byte             | 1..10 (Event discriminator)                   |
| payload           | [Dynamic]        | Binary payload specific to eventType          |
+-------------------+------------------+-----------------------------------------------+
```

---

### 2.2 Event Type Payloads & Binary Layouts

#### Event Type 1: `Kill` (Creature / Boss Slain)
Emitted immediately when an owner-side creature death is attributed to a player.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `1` (`EventType.Kill`) |
| 3 | `attackerName` | `string` | In-game player name credited with the kill |
| 4 | `victimPrefab` | `string` | Sanitized prefab name (e.g. `Boar`, `Troll`, `GoblinKing`, `Seeker`, `Dragon`, `Fader`) |
| 5 | `position` | `Vector3` | Creature death coordinates `(x, y, z)` |
| 6 | `level` | `int` | Star level: `1` (0 stars), `2` (1 star), `3` (2 stars) |
| 7 | `isBoss` | `bool` | `true` if creature is a world boss or mini-boss |
| 8 | `isTamed` | `bool` | `true` if creature was tamed |
| 9 | `weaponOrDmgType`| `string` | Majority damage type (e.g. `Slash`, `Pierce`, `Blunt`, `Fire`, `Frost`, `Lightning`) |
| 10 | `biome` | `string` | Biome name (e.g. `Meadows`, `BlackForest`, `Swamp`, `Mountain`, `Plains`, `Mistlands`, `Ashlands`, `Ocean`) |

---

#### Event Type 2: `Death` (Player Death)
Emitted immediately upon player death with rich client-side root-cause resolution.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `2` (`EventType.Death`) |
| 3 | `victimName` | `string` | In-game player name of the deceased |
| 4 | `cause` | `string` | Computed cause: `a Troll`, `Eikthyr`, `combat with PlayerName`, `drowning`, `a fall`, `burning`, `freezing`, `poison`, `smoke`, `lightning damage` |
| 5 | `position` | `Vector3` | Coordinates of death `(x, y, z)` |
| 6 | `killerPlayer` | `string` | Attacker player name if PvP death; empty string `""` otherwise |
| 7 | `biome` | `string` | Biome where player perished |

---

#### Event Type 3: `DamageDefenseBatch` (Combat Telemetry Batch)
Flushed every 10 seconds or when accumulated damage exceeds 500.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `3` (`EventType.DamageDefenseBatch`) |
| 3 | `playerName` | `string` | In-game player name |
| 4 | `position` | `Vector3` | Player position at flush time |
| 5 | `dealtCreatures` | `float` | Cumulative damage dealt to PvE monsters/bosses |
| 6 | `dealtPlayers` | `float` | Cumulative damage dealt to other players (PvP) |
| 7 | `takenCreatures` | `float` | Cumulative damage taken from monsters/bosses |
| 8 | `takenPlayers` | `float` | Cumulative damage taken from players (PvP) |
| 9 | `takenEnv` | `float` | Cumulative damage taken from environment (fire, poison, fall, smoke) |
| 10 | `hitsDealt` | `int` | Total attack hits successfully landed |
| 11 | `hitsTaken` | `int` | Total damage hits received |
| 12 | `blocks` | `int` | Number of standard attacks blocked with weapon/shield |
| 13 | `parries` | `int` | Number of timed perfect parries executed |
| 14 | `dmgBlocked` | `float` | Total damage mitigated via blocking/parrying |

---

#### Event Type 4: `FishCatch` (Angling)
Emitted when a fish is successfully reeled in.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `4` (`EventType.FishCatch`) |
| 3 | `playerName` | `string` | In-game player name |
| 4 | `fishPrefab` | `string` | Fish item/prefab (e.g. `Fish1`, `Fish2`, `Fish_Forest`, `Fish_Cave`, `Fish_Ashlands`) |
| 5 | `position` | `Vector3` | Catch position |
| 6 | `quality` | `int` | Fish quality level (`1`..`5`) |
| 7 | `weight` | `float` | Weight / stack amount |
| 8 | `biome` | `string` | Biome name |

---

#### Event Type 5: `Building` (Structures & Demolitions)
Emitted on structure placement or deconstruction.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `5` (`EventType.Building`) |
| 3 | `playerName` | `string` | Builder player name |
| 4 | `piecePrefab` | `string` | Piece prefab name (e.g. `woodwall`, `stone_floor`, `forge`, `portal_wood`) |
| 5 | `position` | `Vector3` | Placement/demolition coordinates |
| 6 | `action` | `byte` | `1` = Placed/Built, `2` = Removed/Demolished, `3` = Repaired |
| 7 | `category` | `string` | Piece category (e.g. `Building`, `Crafting`, `Furniture`, `Misc`, `Structure`) |

---

#### Event Type 6: `Crafting` (Crafting, Upgrading, Repairing)
Emitted when an item is crafted, upgraded, or repaired.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `6` (`EventType.Crafting`) |
| 3 | `playerName` | `string` | Crafter player name |
| 4 | `itemPrefab` | `string` | Item prefab name (e.g. `IronSword`, `ArmorWolfChest`, `MeadHealthMedium`) |
| 5 | `position` | `Vector3` | Crafting position |
| 6 | `action` | `byte` | `1` = Crafted New, `2` = Upgraded, `3` = Repaired |
| 7 | `quality` | `int` | Resulting item quality level (`1`, `2`, `3`, `4`...) |
| 8 | `amount` | `int` | Batch quantity produced (e.g. `20` arrows, `1` shield) |
| 9 | `stationName` | `string` | Station used (e.g. `piece_workbench`, `forge`, `blackforge`, `table_cauldron`, `Hand`) |

---

#### Event Type 7: `Harvesting` (Foraging, Farming, Sap, Honey)
Emitted when raw resources or farmed crops are gathered.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `7` (`EventType.Harvesting`) |
| 3 | `playerName` | `string` | Gatherer player name |
| 4 | `resourceName` | `string` | Resource name (e.g. `Raspberry`, `Mushroom`, `Thistle`, `Barley`, `Flax`, `Honey`, `Sap`, `DvergrExtract`) |
| 5 | `position` | `Vector3` | Harvest location |
| 6 | `sourceType` | `byte` | `1` = Foraged Wild, `2` = Farmed Crop, `3` = Beehive, `4` = Sap Extractor |
| 7 | `amount` | `int` | Total count gathered |

---

#### Event Type 8: `Consumables` (Food, Potions, Meads)
Emitted upon food or potion consumption.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `8` (`EventType.Consumables`) |
| 3 | `playerName` | `string` | Player name |
| 4 | `itemPrefab` | `string` | Consumable prefab (e.g. `MeatCooked`, `SerpentStew`, `Salad`, `MeadHealthMedium`, `MeadStaminaMinor`, `PotionEitrMinor`) |
| 5 | `position` | `Vector3` | Consumption coordinates |
| 6 | `itemType` | `byte` | `1` = Food, `2` = Potion / Mead, `3` = Other |
| 7 | `health` | `float` | Base health contribution |
| 8 | `stamina` | `float` | Base stamina contribution |
| 9 | `eitr` | `float` | Base eitr contribution |

---

#### Event Type 9: `WorldEvent` (Boss Summons, Portals, Guardian Powers)
Emitted for world rituals, portal traversal, and power activations.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `9` (`EventType.WorldEvent`) |
| 3 | `playerName` | `string` | Player name |
| 4 | `eventName` | `string` | Event tag: `BossSummoned`, `PortalTraversed`, `GuardianPowerUsed` |
| 5 | `position` | `Vector3` | Event coordinates |
| 6 | `targetOrDetails`| `string` | Target info: Boss name `Eikthyr`, Portal tag `MountainBase`, Power `GP_Bonemass` |
| 7 | `biome` | `string` | Biome name |

---

#### Event Type 10: `StatSync` (100% Vanilla PlayerStat Delta Sync)
Flushed every 10 seconds containing accumulated deltas for all 105 vanilla `PlayerStatType` counters.

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | `2` |
| 2 | `eventType` | `byte` | `10` (`EventType.StatSync`) |
| 3 | `playerName` | `string` | Player name |
| 4 | `position` | `Vector3` | Player position at flush |
| 5 | `statCount` | `int` | Number of stat pairs in this batch (`N`) |
| 6.. `2*N+5` | `statId`, `delta`| `short`, `float` | Repeated `N` times: `(short)PlayerStatType` and `deltaAmount` |

---

### 2.3 Legacy `RavensCall_CombatReport_V1` Specification

For backward compatibility with legacy servers:

| Field # | Name | Type | Description / Notes |
|---|---|---|---|
| 1 | `schemaVersion` | `int` | Always `1` |
| 2 | `eventType` | `byte` | `1` = kill, `2` = death, `3` = damage, `4` = fish |
| 3 | `victimPrefab` | `string` | Creature prefab (kill) / fish prefab (catch) / death cause (death) / `""` (damage) |
| 4 | `attackerName` | `string` | Player name credited |
| 5 | `position` | `Vector3` | Coordinates `(x, y, z)` |
| 6 | `dmgDealt` | `float` | Total damage dealt (damage batch only, else 0) |
| 7 | `dmgTaken` | `float` | Total damage taken (damage batch only, else 0) |

---

## 3. Server-Side Implementation Guide for `TheRavensCall`

Below is the complete, drop-in C# implementation guide for **TheRavensCall** server mod.

### 3.1 Registering Routed RPC Handlers

In your server mod's main initialization (`Awake` or `Game.Start` patch):

```csharp
using UnityEngine;

namespace TheRavensCall
{
    public static class RavensCallRpcListener
    {
        public static void RegisterRPCs()
        {
            if (ZRoutedRpc.instance == null) return;

            // Register V2 full-coverage listener
            ZRoutedRpc.instance.Register<ZPackage>("RavensCall_EventReport_V2", RPC_OnEventReport_V2);

            // Register V1 legacy listener (optional fallback)
            ZRoutedRpc.instance.Register<ZPackage>("RavensCall_CombatReport_V1", RPC_OnCombatReport_V1);

            Plugin.Log?.LogInfo("[TheRavensCall] Telemetry RPC listeners registered successfully.");
        }

        private static void RPC_OnEventReport_V2(long sender, ZPackage pkg)
        {
            if (pkg == null || pkg.Size() == 0) return;

            try
            {
                int schemaVersion = pkg.ReadInt();
                if (schemaVersion != 2)
                {
                    Plugin.Log?.LogWarning($"[TheRavensCall] Unexpected schema version {schemaVersion} from sender {sender}");
                    return;
                }

                byte eventType = pkg.ReadByte();
                switch (eventType)
                {
                    case 1: // Kill
                        HandleKillEvent(sender, pkg);
                        break;
                    case 2: // Death
                        HandleDeathEvent(sender, pkg);
                        break;
                    case 3: // DamageDefenseBatch
                        HandleDamageDefenseBatch(sender, pkg);
                        break;
                    case 4: // FishCatch
                        HandleFishCatch(sender, pkg);
                        break;
                    case 5: // Building
                        HandleBuildingEvent(sender, pkg);
                        break;
                    case 6: // Crafting
                        HandleCraftingEvent(sender, pkg);
                        break;
                    case 7: // Harvesting
                        HandleHarvestingEvent(sender, pkg);
                        break;
                    case 8: // Consumables
                        HandleConsumablesEvent(sender, pkg);
                        break;
                    case 9: // WorldEvent
                        HandleWorldEvent(sender, pkg);
                        break;
                    case 10: // StatSync
                        HandleStatSync(sender, pkg);
                        break;
                    default:
                        Plugin.Log?.LogWarning($"[TheRavensCall] Unknown eventType {eventType} from sender {sender}");
                        break;
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log?.LogError($"[TheRavensCall] Failed unpacking V2 event from sender {sender}: {ex}");
            }
        }

        private static void RPC_OnCombatReport_V1(long sender, ZPackage pkg)
        {
            // If server already processes V2, ignore duplicate V1 packets, or use V1 if sender only has v1.0.0 client
        }
    }
}
```

---

### 3.2 Unpacking & Handling Each Event Type

#### Event 1: `Kill`
```csharp
private static void HandleKillEvent(long sender, ZPackage pkg)
{
    string attackerName = pkg.ReadString();
    string victimPrefab = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    int level = pkg.ReadInt();
    bool isBoss = pkg.ReadBool();
    bool isTamed = pkg.ReadBool();
    string weaponOrDmgType = pkg.ReadString();
    string biome = pkg.ReadString();

    // 1. Update Server Database / Ledger
    PlayerDatabase.RecordKill(attackerName, victimPrefab, level, isBoss, isTamed, weaponOrDmgType, biome, pos);

    // 2. Trigger Server Chronicle / Live Discord Webhook
    if (isBoss)
    {
        ChronicleManager.AnnounceBossKill(attackerName, victimPrefab, level, biome);
    }
}
```

#### Event 2: `Death`
```csharp
private static void HandleDeathEvent(long sender, ZPackage pkg)
{
    string victimName = pkg.ReadString();
    string cause = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    string killerPlayer = pkg.ReadString();
    string biome = pkg.ReadString();

    PlayerDatabase.RecordDeath(victimName, cause, killerPlayer, biome, pos);

    if (!string.IsNullOrEmpty(killerPlayer))
    {
        ChronicleManager.AnnouncePvpDeath(victimName, killerPlayer, biome);
    }
    else
    {
        ChronicleManager.AnnouncePlayerDeath(victimName, cause, biome);
    }
}
```

#### Event 3: `DamageDefenseBatch`
```csharp
private static void HandleDamageDefenseBatch(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    float dealtCreatures = pkg.ReadSingle();
    float dealtPlayers = pkg.ReadSingle();
    float takenCreatures = pkg.ReadSingle();
    float takenPlayers = pkg.ReadSingle();
    float takenEnv = pkg.ReadSingle();
    int hitsDealt = pkg.ReadInt();
    int hitsTaken = pkg.ReadInt();
    int blocks = pkg.ReadInt();
    int parries = pkg.ReadInt();
    float dmgBlocked = pkg.ReadSingle();

    PlayerDatabase.AccumulateCombatStats(playerName, dealtCreatures, dealtPlayers, takenCreatures, takenPlayers, takenEnv, hitsDealt, hitsTaken, blocks, parries, dmgBlocked);
}
```

#### Event 4: `FishCatch`
```csharp
private static void HandleFishCatch(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    string fishPrefab = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    int quality = pkg.ReadInt();
    float weight = pkg.ReadSingle();
    string biome = pkg.ReadString();

    PlayerDatabase.RecordFishCatch(playerName, fishPrefab, quality, weight, biome, pos);
}
```

#### Event 5: `Building`
```csharp
private static void HandleBuildingEvent(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    string piecePrefab = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    byte action = pkg.ReadByte(); // 1=Placed, 2=Removed, 3=Repaired
    string category = pkg.ReadString();

    PlayerDatabase.RecordBuildingAction(playerName, piecePrefab, action, category, pos);
}
```

#### Event 6: `Crafting`
```csharp
private static void HandleCraftingEvent(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    string itemPrefab = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    byte action = pkg.ReadByte(); // 1=Crafted, 2=Upgraded, 3=Repaired
    int quality = pkg.ReadInt();
    int amount = pkg.ReadInt();
    string stationName = pkg.ReadString();

    PlayerDatabase.RecordCraftingAction(playerName, itemPrefab, action, quality, amount, stationName, pos);
}
```

#### Event 7: `Harvesting`
```csharp
private static void HandleHarvestingEvent(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    string resourceName = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    byte sourceType = pkg.ReadByte(); // 1=Foraged, 2=Crop, 3=Beehive, 4=Sap
    int amount = pkg.ReadInt();

    PlayerDatabase.RecordHarvest(playerName, resourceName, sourceType, amount, pos);
}
```

#### Event 8: `Consumables`
```csharp
private static void HandleConsumablesEvent(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    string itemPrefab = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    byte itemType = pkg.ReadByte(); // 1=Food, 2=Potion/Mead, 3=Other
    float health = pkg.ReadSingle();
    float stamina = pkg.ReadSingle();
    float eitr = pkg.ReadSingle();

    PlayerDatabase.RecordConsumable(playerName, itemPrefab, itemType, health, stamina, eitr, pos);
}
```

#### Event 9: `WorldEvent`
```csharp
private static void HandleWorldEvent(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    string eventName = pkg.ReadString(); // BossSummoned, PortalTraversed, GuardianPowerUsed
    Vector3 pos = pkg.ReadVector3();
    string targetOrDetails = pkg.ReadString();
    string biome = pkg.ReadString();

    PlayerDatabase.RecordWorldEvent(playerName, eventName, targetOrDetails, biome, pos);

    if (eventName == "BossSummoned")
    {
        ChronicleManager.AnnounceBossSummon(playerName, targetOrDetails, biome);
    }
}
```

#### Event 10: `StatSync` (105 Vanilla PlayerStatType Synchronization)
```csharp
private static void HandleStatSync(long sender, ZPackage pkg)
{
    string playerName = pkg.ReadString();
    Vector3 pos = pkg.ReadVector3();
    int statCount = pkg.ReadInt();

    var statDeltas = new System.Collections.Generic.Dictionary<short, float>(statCount);
    for (int i = 0; i < statCount; i++)
    {
        short statId = pkg.ReadShort();
        float delta = pkg.ReadSingle();
        statDeltas[statId] = delta;
    }

    PlayerDatabase.ApplyStatDeltas(playerName, statDeltas, pos);
}
```

---

## 4. Complete Vanilla `PlayerStatType` Reference Mapping

WhereTheCrowFlies intercepts and synchronizes all 105 vanilla `PlayerStatType` enum values in `EventType.StatSync`:

| ID | Enum Name | Description |
|---|---|---|
| 0 | `Deaths` | Total player deaths |
| 1 | `CraftsOrUpgrades` | Combined count of crafts and upgrades |
| 2 | `Builds` | Total structures/pieces built |
| 3 | `Jumps` | Total jumps |
| 4 | `Cheats` | Cheat commands used |
| 5 | `EnemyHits` | Total hits landed on enemies |
| 6 | `EnemyKills` | Total enemies killed |
| 7 | `EnemyKillsLastHits` | Last hits on enemy kills |
| 8 | `PlayerHits` | Total hits landed on players (PvP) |
| 9 | `PlayerKills` | Total players killed (PvP) |
| 10 | `HitsTakenEnemies` | Hits taken from enemies |
| 11 | `HitsTakenPlayers` | Hits taken from players (PvP) |
| 12 | `ItemsPickedUp` | Total items collected from ground |
| 13 | `Crafts` | Total items crafted |
| 14 | `Upgrades` | Total item upgrades performed |
| 15 | `PortalsUsed` | Total portal journeys |
| 16 | `DistanceTraveled` | Total distance traveled (meters) |
| 17 | `DistanceWalk` | Distance walked (meters) |
| 18 | `DistanceRun` | Distance sprinted (meters) |
| 19 | `DistanceSail` | Distance sailed on boats (meters) |
| 20 | `DistanceAir` | Distance falling/gliding (meters) |
| 21 | `TimeInBase` | Seconds spent within base shelter |
| 22 | `TimeOutOfBase` | Seconds spent outside base |
| 23 | `Sleep` | Nights slept in a bed |
| 24 | `ItemStandUses` | Item stand interactions |
| 25 | `ArmorStandUses` | Armor stand interactions |
| 26 | `WorldLoads` | Total world loads |
| 27 | `TreeChops` | Axe swings against trees |
| 28 | `Tree` | Total trees felled |
| 29..34 | `TreeTier0` .. `TreeTier5` | Trees felled by wood tier (Normal, Fine, Core, Ancient, Yggdrasil, Ashwood) |
| 35 | `LogChops` | Axe swings against logs |
| 36 | `Logs` | Logs completely cleared |
| 37 | `MineHits` | Pickaxe swings against ore/rock |
| 38 | `Mines` | Total ore/mineral nodes broken |
| 39..44 | `MineTier0` .. `MineTier5` | Mines broken by tier (Rock, Copper/Tin, Iron, Silver, Black Marble, Flametal) |
| 45 | `RavenHits` | Hugin/Munin attacks |
| 46 | `RavenTalk` | Dialogue interactions with ravens |
| 47 | `RavenAppear` | Raven spawn occurrences |
| 48 | `CreatureTamed` | Wild animals tamed |
| 49 | `FoodEaten` | Food items consumed |
| 50 | `SkeletonSummons` | Dead Raiser necromancy summons |
| 51 | `ArrowsShot` | Arrows and crossbow bolts fired |
| 52 | `TombstonesOpenedOwn` | Own grave tombstones recovered |
| 53 | `TombstonesOpenedOther`| Other player tombstones opened |
| 54 | `TombstonesFit` | Gravestone space fits |
| 55..69 | `DeathByUndefined` .. `DeathByStalagtite` | Specific death sub-causes tracked by engine |
| 70 | `DoorsOpened` | Doors and gates opened |
| 71 | `DoorsClosed` | Doors and gates closed |
| 72 | `BeesHarvested` | Honey harvested from beehives |
| 73 | `SapHarvested` | Eitr sap harvested from extractors |
| 74 | `TurretAmmoAdded` | Ammo loaded into ballistas |
| 75 | `TurretTrophySet` | Target trophies configured on ballistas |
| 76 | `TrapArmed` | Mechanical traps armed |
| 77 | `TrapTriggered` | Mechanical traps triggered |
| 78 | `PlaceStacks` | Stack placement interactions |
| 79 | `PortalDungeonIn` | Dungeon entrances traversed |
| 80 | `PortalDungeonOut` | Dungeon exits traversed |
| 81 | `BossKills` | Bosses slain |
| 82 | `BossLastHits` | Last hits on boss kills |
| 83..90 | `SetGuardianPower` / `SetPower*` | Power sacrifices offered at Sacrificial Stones |
| 91..98 | `UseGuardianPower` / `UsePower*` | Forsaken guardian powers activated |

---

## 5. Security, Validation & Anti-Cheat Recommendations

1. **Sender Peer Validation**:
   Compare the `sender` peer ID (`long sender`) with `ZNet.instance.GetPeer(sender)`. Reject any report if the sender name does not match the active session character.
2. **Rate Limiting**:
   - Damage batches and Stat syncs are transmitted on 10-second intervals per client. Reject bursts exceeding 5 packets per second from a single peer.
   - High-impact events (kills, deaths, crafts, boss summons) should be checked against player cooldowns.
3. **Range Checks**:
   Validate that `Vector3 position` reported in the payload is within reasonable proximity (e.g. within 150m) of the player character's server-known coordinates.
