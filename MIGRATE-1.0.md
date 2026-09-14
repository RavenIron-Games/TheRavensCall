# TheRavensCall — Valheim 1.0.12 verification

> **Verified 2026-09-11 on Valheim 1.0.12 (build 25253764/25253791, network version 40) - TheRavensCall 1.2.2, no change
> needed.** Not rebuilt, not bumped; the staged DLL (`~/valheim-testbed/profiles/trc/BepInEx/plugins/TheRavensCall/TheRavensCall.dll`,
> md5 `285dbac8e120dec7285664290945a0f4`, byte-identical to `bin/TheRavensCall.dll` and `HexiumDist/plugins/TheRavensCall.dll`
> inside `TheRavensCall-v1.2.2.zip`) is the 2026-09-10 build below, unchanged.
>
> **What changed in the engine.** Only `assembly_valheim` differs between 1.0.7 and 1.0.12 (37 types, 55 body changes, 5 methods
> added, 1 removed, 10 compiler-generated renames, 6 fields added, 1 removed, `Version.c_networkVersion` 39 -> 40);
> `assembly_utils`, `assembly_guiutils`, `gui_framework`, `assembly_postprocessing` and `Splatform` are IL-identical. There is
> **no** signature change, no interface change, no constructor reshape and no new non-defaulted parameter anywhere: the asmdiff
> JSON contains only the kinds body-changed / field-added / field-const-changed / field-removed / method-added / method-removed /
> method-renamed, and all 37 changed types are classes (checked against the 1.0.12 decompile). This mod implements no game
> interface (`Plugin : BaseUnityPlugin`, `Companion : MonoBehaviour` only), so the three break classes the 1.0.7 reports missed
> cannot apply. The client and server decompile diffs differ only in client-only `FejdStartup` / `PresentManager` lines.
>
> **Touchpoints (every Harmony target, reflection lookup and engine read in `Saga.cs` / `Companion.cs` / `PlayerRegistry.cs`,
> checked against `MigrationStation/diffs/server_assembly_valheim_1.0.7_to_1.0.12.{md,json,ildiff.txt}` and
> `libs-Tools/1.0/DECOMPILED/assembly_valheim_SERVER.decompiled.cs`).**
>
> | Where | Member | 1.0.12 delta | Verdict |
> |---|---|---|---|
> | `Saga.cs:305` | `ZNet.Awake` postfix | `ZNet.Awake` unchanged (ZNet's changes: `OpenServer`, `SendPeerInfo`, `RPC_PeerInfo`, `ListContainsId`, `DelayThenRegisterCoroutine`) | unaffected |
> | `Saga.cs:331` | `ZNet.RPC_PeerInfo(ZRpc, ZPackage)` postfix | body changed only by the inlined network-version literal: three `ldc.i4.s 39` -> `40` (ildiff.txt 794-816; decompile 79788-79809), same token count; the mod's hook is a postfix with no IL matching | unaffected. Operational note, not a mod change: a 1.0.7 client is now the mismatched peer; as on every earlier version the original returns before `m_playerName` is set, so the postfix registers/narrates the rejected peer as "A Viking" and the following disconnect closes it |
> | `Saga.cs:370` | `ZNet.RPC_Disconnect(ZRpc)` prefix | unchanged | unaffected |
> | `Saga.cs:414` | `ZNet.SendDisconnect(ZNetPeer)` prefix (pinned overload) | unchanged; overload still present (decompile) | unaffected |
> | `Saga.cs:429` | `ZNet.Update` postfix | unchanged | unaffected |
> | `Saga.cs:499` | `Character.Damage(HitData)` prefix | `Character.Damage` unchanged (Character's changes: `ApplyDamage`, `UpdateGroundContact`, `OnDeath`) | unaffected (still server-side inert, as documented) |
> | `Saga.cs:524` | `Character.OnDeath` prefix (by name) | body changed only `ldsfld PlayerProfile::s_bypassCheatChecks` -> `call PlayerProfile::get_s_bypassCheatChecks()` (ildiff.txt 127-135, 336 -> 336 tokens); prefix, no IL matching | unaffected |
> | `Saga.cs:1289` | `RandEventSystem.SetRandomEventByName(string, Vector3)` postfix | type not in the delta | unaffected |
> | `Saga.cs:1303` | `FishingFloat.Catch` postfix (by name) | type not in the delta; still `public static string Catch(Fish fish, Character owner)` (decompile 126980) | unaffected |
> | `Saga.cs:1987` / `:1992` | `Terminal.InitTerminal` postfix registering `new Terminal.ConsoleCommand("ravenscall", desc, action)` - every flag left at its default: `isCheat=false, isNetwork=false, onlyServer=false, isSecret=false, allowInDevBuild=false, hideBehindDevCommands=false, optionsFetcher=null, alwaysRefreshTabOptions=false, remoteCommand=false, onlyAdmin=false` (ctor at decompile 43112) | `InitTerminal` grew by vanilla's new `yesiuseddevcommandsbutiwantmyachievementsanyway` command (10453 -> 10544 tokens; postfix unaffected). The command-gating rewrite (`ConsoleCommand.IsValid`, `ShowCommand`, `Chat.isAllowedCommand`; ildiff.txt 136-145, 589-634) only changes commands with `HideBehindDevCommands` | unaffected: `ravenscall` is neither cheat nor hidden, so it is executable and tab-listed exactly as on 1.0.7 |
> | `Saga.cs:255`, `Companion.cs:727` | `AccessTools.Field(typeof(Character), "m_lastHit")` | field unchanged | unaffected |
> | `Companion.cs:45` | `typeof(Player).GetMethod("OnDeath")` | `Player.OnDeath` unchanged (decompile 12726); 1.0.12 boot log line 19 `Player.OnDeath method found: OnDeath` | unaffected |
> | `Companion.cs:402-541` | `AccessTools.Field` on `Player.m_foods / m_guardianSE / m_guardianPowerCooldown / m_knownRecipes / m_knownMaterial`, `StatusEffect.m_time`, `Character.m_level` | none of these is among the 7 field changes (`m_forcePlayUnlockSound`, `m_spawnFullDurability`, `m_pullSound`, `m_instanceSound`, `m_cheatedPopup`, `m_interactionSound`, `s_bypassCheatChecks`) | unaffected |
> | `Companion.cs:1169`, `:1190` | `ZNetScene.m_namedPrefabs`, `ZDOMan.m_objectsByID` via `GetField` | ZDOMan's delta is `Load` / `ConvertContainers` / `ConvertInventories` / `GetConvertHash` / `ConvertPrefabStrings` only; both fields unchanged | unaffected |
> | `Companion.cs:1220-1232` | `Fermenter.GetFermentationTime / GetStatus / GetContent / m_fermentationDuration` via reflection | Fermenter's only change is `DelayedTap` (ldsfld -> call get_, ildiff.txt 275-283); the reflected members are unchanged | unaffected. (Pre-existing and not a 1.0.12 matter, left alone: `GetContent()` returns `int` in 1.0.7 and 1.0.12 alike while `Companion.cs:1233` casts the boxed result to `string`, so that per-fermenter block lands in its own `catch {}` and fermenters are omitted from the census on both versions.) |
> | `Companion.cs:1391-1403` | `Minimap.m_pins` / `PinData` fields via reflection | unchanged (client-only object anyway) | unaffected |
> | `Companion.cs:391`, `:808`, `:1078-1088`; `Saga.cs:1423` | `Player.GetInventory().GetAllItems()`, `Container.GetInventory().GetAllItems()` - read-only | Inventory's delta is `AddItem` (ldsfld -> call get_) and `Changed` (client-side "$achievements_dropped_cheated_item" popup, private `m_cheatedPopup`); the mod never writes an inventory and never calls `ItemDrop.SaveToZDO` / `LoadFromZDO` or reads item-stand slot keys, so the `SaveToZDO` guard flip and the new `ZDOMan.ConvertContainers` slot loop (`"{index}_{name}"` hashes) do not reach it | unaffected |
> | `Companion.cs:539`, `:1085`, `:1229`, `:1263-1356` | ZDO reads: `ZDOVars.s_creator / s_fuel / s_queued / s_item / s_level / s_lastTime / s_plantTime`, `"accTime"`, `"SpawnOre"`, `"TamedName"`, `"wassap"`, `"SpawnTime"` via `GetZDO / GetLong / GetFloat / GetInt / GetString / GetBool` | none of these keys is rewritten by the world conversion (it touches durability / stack / quality / variant / crafterID / crafterName / dataCount / data_i / worldLevel / pickedUp / cheated and their `{index}_` forms only) | unaffected |
> | `Saga.cs:1041`, `:1234-1270` | `PlayerStatType` casts, `MaxStatPairs = 1024` | `PlayerStatType` not in the delta (207 member lines in both decompiles) | unaffected |
> | `Saga.cs:321-322` | `ZRoutedRpc.Register` `RavensCall_CombatReport_V1` / `_EventReport_V2` (mod-private `SchemaVersion` 1 / 2) | `ZRoutedRpc` unchanged; the game's `c_networkVersion` is not part of the mod's packet schema | unaffected |
> | `Saga.cs:228`, `:271`; `Companion.cs:738` | `ZDOMan.GetZDO`, `ZDO.GetPrefab`, `ZNetScene.GetPrefab`, `ZNet.GetPeers`, `ZNetPeer.m_playerName / m_rpc / m_uid / m_characterID / m_refPos / IsReady` | unchanged | unaffected |
> | `PlayerRegistry.cs` | no engine members (System.* only; grep for game types: 0 non-comment hits) | n/a | n/a |
>
> **Delta items with zero hits in this mod** (greps over `Saga.cs Companion.cs PlayerRegistry.cs`, bin/obj excluded): no
> transpiler (`Transpiler|CodeInstruction|OpCodes|CodeMatcher`: 0); no version or network-version read/print
> (`Version\b|networkVersion|GetVersionString|CurrentVersion|c_networkVersion|GameVersion`: 0 - only the mod's own
> `PluginVersion`/`SchemaVersion`/`VERSION`); no `s_bypassCheatChecks|bypasscheatchecks|cheat|Achievements|AnyCheatedItem|m_usedCheats`
> (0 outside two comments); no `SaveToZDO|LoadFromZDO|ItemStand|ArmorStand|Inventory.Load|Inventory.Save|m_customData|ZDOExtraData`
> (0); no `CookingStation|Smelter.Spawn|SpawnItem|DelayedTap|m_durability|m_maxDurability|m_spawnFullDurability` (0; "Smelter" appears only
> as display-name strings); no `m_onLand|UpdateGroundContact|m_lastGroundPoint|deepSnow|m_lastBiome` (0); no
> `TerrainComp|PaintCleared|TerrainOp|GetCultivationMask` (0; `Heightmap.Biome` enum only); no
> `ListContainsId|m_adminList|m_bannedList|m_permittedList|IsAdmin|SyncedList|PlatformUserID` (0); no
> `DoMeleeAttack|m_snowShovel|HaveRequirementItems|TryPlacePiece|Destructible|DropOnDestroyed|DropResources|MineRock|ItemSets|
> GrapplingPoint|RuneStone|AchievementUnlockPopup|PresentManager|InventoryGui|ServerOptionsGUI|ServerListGui|MasterClient|
> ZPlayFab*|ZSteamMatchmaking|TestSpawnLocation|FejdStartup|Console\b|Chat\b` (0).
>
> **Evidence.**
> - refcheck (Mono.Cecil exact-signature + Harmony-target resolution) of the staged 1.2.2 DLL against the 1.0.12 Managed folders:
>   CLIENT `RESULT: OK` (1990 references, 10 Harmony targets) - `/tmp/claude-1000/-home-rohan-WubarrkCODING/7e501e8a-0a25-46f2-aaca-99a485eceb49/scratchpad/refcheck-1012/trc-TheRavensCall-client.txt`;
>   SERVER `RESULT: OK` (1990 references, 10 Harmony targets) - `/tmp/claude-1000/-home-rohan-WubarrkCODING/7e501e8a-0a25-46f2-aaca-99a485eceb49/scratchpad/refcheck-1012/trc-TheRavensCall-server.txt`
>   (`SUMMARY.txt` line 17: `trc TheRavensCall client=0 server=0`). Reference set verified: `libs-Tools/1.0/server/assembly_valheim.dll`
>   md5 == the Steam dedicated-server Managed copy; `libs-Tools/1.0/client` == the Steam client copy.
> - Boot on the real 1.0.12 Linux dedicated server, profile `trc`: `/tmp/claude-1000/-home-rohan-WubarrkCODING/7e501e8a-0a25-46f2-aaca-99a485eceb49/scratchpad/boot-1012/trc.txt` - **BOOTED after 29 s**;
>   `~/valheim-testbed/profiles/trc/server-console.log:140` `Valheim version: l-1.0.12 (network version 40)`;
>   `~/valheim-testbed/profiles/trc/BepInEx/LogOutput.log` lines 16-32: `Loading [TheRavensCall 1.2.2]`, `TheRavensCall 1.2.2 awakens
>   (server-only).`, `All patches applied.`, `Player.OnDeath method found: OnDeath`, `HTTP server listening on http://localhost:2112`,
>   `Game data written OK`, `Chronicle: .../TheRavensCall_Chronicle_2026-09-11.log`, `PlayerRegistry loaded 0 player record(s).`,
>   `SessionTracker started.`, `LoreSystem loaded 5 entries.`, `Server systems initialized.`, `Session summary written to Chronicle.`
>   No Harmony / MissingMethod / MissingField / TypeLoad / Exception lines; the only errors are vanilla's headless
>   "Could not find video decode shader pass" noise.
> - Compared with the same profile's 1.0.7 run (`~/valheim-testbed/profiles/trc/logs-1.0.7-run/LogOutput.log` lines 16-31 and
>   `server-console.log:140` `Valheim version: l-1.0.7 (network version 39)`): the plugin lines are identical line for line.
> - Engine diff read in full: `/tmp/claude-1000/-home-rohan-WubarrkCODING/7e501e8a-0a25-46f2-aaca-99a485eceb49/scratchpad/decomp-diff/assembly_valheim_SERVER.decompiled.cs.diff` (863 lines) and the client diff;
>   `MigrationStation/diffs/server_assembly_valheim_1.0.7_to_1.0.12.md` / `.json` / `.ildiff.txt`.
> - Grep transcript: `/tmp/claude-1000/-home-rohan-WubarrkCODING/7e501e8a-0a25-46f2-aaca-99a485eceb49/scratchpad/agents/TheRavensCall/greps.txt`.
>
> **Open (unchanged from 2026-09-10).** Live pairing test with WhereTheCrowFlies 1.1.1 on 1.0.12; note that a WhereTheCrowFlies
> client still on 1.0.7 cannot connect to a 1.0.12 server at all (network version 39 vs 40), which is vanilla's doing, not this mod's.

# TheRavensCall — Valheim 1.0.7 migration

> **Applied 2026-09-10 - TheRavensCall 1.2.2.** One source change: the V2 receiver's `MaxStatPairs` cap
> (`Saga.cs`) went from 200 to 1024, because 1.0.7's `PlayerStatType` has 205 counters and WhereTheCrowFlies 1.1.1
> snapshots all of them - the old cap clamped the count and left the last five pairs unread, silently. Everything
> else compiled and bound unchanged: the mod is server-only and touches none of the sector, VisEquipment or
> PieceTable surfaces that moved in 1.0. `TheRavensCall.csproj` reference paths were corrected for the project's
> real location (they still pointed one directory too deep, from its time under `PAUSED/`). Rebuilt against the
> 1.0.7 refs in `libs-Tools` on BepInEx 5.4.2350. Version 1.2.1 -> 1.2.2 in lockstep (`Saga.cs` `PluginVersion`,
> `HexiumDist/manifest.json`, README badge), CHANGELOG entry added, manifest dependency moved to
> `denikson-BepInExPack_Valheim-5.4.2350`.
>
> **Evidence.**
> - Build: `dotnet build TheRavensCall.csproj -c Release` -> **0 errors, 0 warnings**.
> - refcheck (Mono.Cecil, exact-signature): CLIENT `RESULT: OK` (1990 references, 10 Harmony targets);
>   SERVER `RESULT: OK` (1990 references, 10 Harmony targets).
> - Boot-check on the real 1.0.7 Linux dedicated server, profile `~/valheim-testbed/profiles/trc`, port 2702:
>   **BOOTED after 32 s**. Key lines: `[Info : BepInEx] Loading [TheRavensCall 1.2.2]`,
>   `TheRavensCall 1.2.2 awakens (server-only).`, `All patches applied.`, `HTTP server listening on
>   http://localhost:2112`, `Game data written OK`, `Chronicle: .../TheRavensCall_Chronicle_2026-09-10.log`,
>   `PlayerRegistry loaded 0 player record(s).`, `SessionTracker started.`, `Server systems initialized.`
>   No Harmony / MissingMethod / MissingField / TypeLoad lines; the only errors are vanilla's headless
>   "Could not find video decode shader pass" noise.
>
> **Behaviour on 1.0.7.** `vanilla_stats` gains the hundred 1.0 counters (`BarrkBOT_data1.json` keys are still
> `PlayerStatType` names, resolved on the server with the 1.0.7 enum), so BarrkBOT sees new non-zero keys and no
> renamed ones - additive, per `BARRKBOT_CONTRACT.md`. The `Character.OnDeath` / `Character.Damage` prefixes stay
> server-side inert exactly as documented in `KILL_TRACKING_FINDINGS.md`; nothing about zone ownership changed in
> 1.0 in a way that would make a dedicated server run those.
>
> **Deviations / notes.** No fold script in the repo; the zip was made with the shared deterministic packer (same
> five entries as 1.2.1). A byte-identical copy of this project sits in `PAUSED/TheRavensCall/` from before it was
> un-paused; it is now stale (its csproj still carries the `PAUSED/`-depth paths).
>
> **Open.** Live pairing test with WhereTheCrowFlies 1.1.1: one client session, then confirm `BarrkBOT_data1.json`
> carries 205 `vanilla_stats` keys for that player.

## Audit table (every Harmony target and engine member, checked 2026-09-10 against `libs-Tools/1.0/DECOMPILED/`)

| Where | Target / member | 1.0.7 status |
|---|---|---|
| `Saga.cs` | `ZNet.Awake` (private, by name) | present, one overload |
| `Saga.cs` | `ZNet.RPC_PeerInfo(ZRpc, ZPackage)` | present, one overload |
| `Saga.cs` | `ZNet.RPC_Disconnect(ZRpc)` | present, one overload |
| `Saga.cs` | `ZNet.SendDisconnect(ZNetPeer)` (pinned `typeof(ZNetPeer)`) | present; the pinned overload exists |
| `Saga.cs` | `ZNet.Update` (private, by name) | present, one overload |
| `Saga.cs` | `Character.Damage(HitData)` / `Character.OnDeath` (by name) | present, one overload each (server-side inert by design) |
| `Saga.cs` | `RandEventSystem.SetRandomEventByName(string, Vector3)` | present, one overload |
| `Saga.cs` | `FishingFloat.Catch` (private, by name) | present, one overload |
| `Saga.cs` | `Terminal.InitTerminal` (by name); `new Terminal.ConsoleCommand("ravenscall", ..., args => ...)` | present; ctor gained `bool hideBehindDevCommands` (inserted, optional) - source binds by named/optional args |
| `Saga.cs` | `ZRoutedRpc.Register(string, Action<long, ZPackage>)`, `ZNet.GetPeers()`, `ZNetPeer.m_playerName/m_rpc/m_socket`, `ISocket.GetHostName()` | present, unchanged |
| `Saga.cs` | `ZNet.GetPlayerList()`, `ZNet.PlayerInfo.m_name/m_characterID/m_userInfo` | present; `PlayerInfo` lost `m_serverAssignedDisplayName` (not used here) |
| `Saga.cs` | `PlayerStatType` (enum cast for snapshot/delta keys) | present; 205 members on 1.0.7 (was 105) - cap raised |
| `Companion.cs` | `Object.FindObjectsByType<Container>` / `<Fermenter>`, `Container.GetInventory()`, `Inventory.GetAllItems()`, `Fermenter` members | present, unchanged |
| `Companion.cs` | `ObjectDB.instance.m_items / m_recipes`, `Recipe.m_item / m_resources / m_craftingStation`, `PieceTable` prefab dumps via `ObjectDB` | present, unchanged |
| `PlayerRegistry.cs` | no engine members (JSON + filesystem only) | n/a |

Catalogue items that do **not** apply to this mod: no sector API, no `Vector2i`/`Vector2s`, no `VisEquipment`, no
`Inventory.Load` patch, no `PieceTable.m_availablePieces`, no `Hoverable`, no `SetCreator` / `PlacePiece`, no
`IsTeleportable`, no `Minimap`, no `ZInput`, no save paths, no `Version.*`.
