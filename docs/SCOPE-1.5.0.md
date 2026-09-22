# TheRavensCall 1.5.0 — World census

Owner's ask (2026-09-22): *"can we add some data? like how many portals placed by player and in
world"*, then *"add the rest of them then, beds ships carts, the lot"*. This scope is the
implementation contract for that feature. Everything below was checked against the Valheim
1.0.12 server assembly (`scratchpad/decompile/*.decompiled.cs` from
`Storm10\valheim_server_Data\Managed\assembly_valheim.dll`); line numbers refer to those
decompiles.

## 1. What ships

- A **world census** taken on the server on a timer: how many portals, beds, wards, ships,
  carts, chests and crafting stations stand in the world right now, how many of each were
  placed by each player, and a per-player total of every standing piece they ever placed.
- A **portal directory**: every portal's tag, kind (wood or stone), builder, whether it is
  connected, and its position. Bed and ward lists in the same shape.
- **`GET /api/census`** serving the latest census, and a **World panel** on the dashboard.
- **Per player**, on the detail view's Build tab: what of theirs is standing in the world.
- Version **1.5.0**. The BarrkBOT export files and `/api/state` are **untouched**
  (`BARRKBOT_CONTRACT.md`); the census is a new route only.

Already covered, no work needed: the vanilla per-character stat `PortalsPlaced` (lifetime
placements, including ones since torn down) reaches the export through the crow's stat
snapshot today. The Build tab may show it next to the census line, but it is a different
number and must be labelled as such ("placed, ever" vs "standing now").

## 2. Data source (server)

A dedicated server holds every object of the loaded world in `ZDOMan.m_objectsByID`
(`private readonly Dictionary<ZDOID, ZDO>`, ZDOMan.cs:91). Nothing public enumerates it;
`Companion.GetAllZDOs()` (Companion.cs:1077) already reads it through reflection and
copies the values into a list. **Reuse that helper** (move it to the census class; keep the
reflection, keep the `try/catch` that returns an empty list when the field is missing, and
log one warning the first time it comes back empty while `ZDOMan.instance` exists, so a
future game build that renames the field is visible in the log instead of silently
reporting zero).

Per object:
- **Prefab**: `ZDO.GetPrefab()` (int, the prefab name's stable hash; ZDO.cs:563).
- **Builder**: `zdo.GetLong(ZDOVars.s_creator, 0L)` — the placing player's profile ID,
  written by `Piece.SetCreator` (Piece.cs:434). `0` = not placed by a player (world
  generation, spawns).
- **Position**: `zdo.GetPosition()` (ZDO.cs:568); report `x` and `z` rounded to integers,
  omit `y`.
- **Portals** (`TeleportWorld`): `zdo.GetString(ZDOVars.s_tag)` (tag, user text),
  `zdo.GetString(ZDOVars.s_tagauthor)`, connected =
  `zdo.GetConnectionZDOID(ZDOExtraData.ConnectionType.Portal) != ZDOID.None` (ZDO.cs:748;
  TeleportWorld.cs:141-202 is the game's own use).
- **Beds** (`Bed`): `zdo.GetLong(ZDOVars.s_owner)` / `zdo.GetString(ZDOVars.s_ownerName)`
  — the player who claimed it as spawn point (Bed.cs:193-215); `0`/empty = unclaimed.
- **Wards** (`PrivateArea`): `zdo.GetString(ZDOVars.s_creatorName)` (PrivateArea.cs:326),
  `zdo.GetBool(ZDOVars.s_enabled)` (PrivateArea.cs:307).

### 2.1 Classification by component, not by name

Build a `Dictionary<int prefabHash, Group>` **once**, lazily on the first census (needs
`ZNetScene.instance`), from `ZNetScene.instance.m_prefabs` (`public List<GameObject>`,
ZNetScene.cs:13; the hash is `prefab.name.GetStableHashCode()`, ZNetScene.cs:38). A prefab
belongs to the first matching group:

| Group | Component on the prefab | Notes |
|---|---|---|
| `portals` | `TeleportWorld` | kind = `"wood"` for `portal_wood`, `"stone"` for `portal_stone`, else the prefab name |
| `beds` | `Bed` | |
| `wards` | `PrivateArea` | |
| `ships` | `Ship` | |
| `carts` | `Vagon` | |
| `chests` | `Container` **and** `Piece` | the `Piece` requirement keeps dungeon and treasure chests (no `Piece`) out |
| `stations` | `CraftingStation` | workbench, forge, stonecutter, artisan table, black forge, galdr table, and any modded station |

Modded portals, beds and the rest count automatically, which is the point of classifying
by component. Keep each group's prefab display name: the localized `Piece.m_name` through
`Companion.Loc` when the prefab has a `Piece`, else the prefab name.

`pieces` (per builder) = every object whose `s_creator` equals that builder's ID, any
prefab — the count of what that player has standing in the world.

## 3. Builder names

`s_creator` is the profile ID that `Player.SetPlayerID` also writes into the character's
ZDO as `ZDOVars.s_playerID` (Player.cs:747, read back at :758). The server therefore learns
a player's ID whenever they are connected: in `Companion.PollAllPlayers`, for each ready
peer, `ZDOMan.instance.GetZDO(peer.m_characterID)?.GetLong(ZDOVars.s_playerID, 0L)`
(`Plugin.GetConnectedPlayers` at Saga.cs:246 already resolves that ZDO; extend it to yield
the ID, or read the peers again in the census class). Two more free sources: a claimed
bed's `s_owner` + `s_ownerName` pair, and the mapping is persisted so it survives restarts:

`BepInEx/config/TheRavensCall/player_ids.json` — `{"ids":[{"id":<long>,"name":"<name>"}]}`,
written through `PlayerRegistry.AtomicWrite` only when a new pair appears, loaded at boot.
Names go through `Companion.Esc`. A creator with no known name is reported with
`"builder": null` and its `builder_id`, and counted once in `unknown_builders`. Do **not**
add the ID to `PlayerRecord`/`ToJson` — that is the export's shape.

## 4. Schedule and cost

New config `[Census] CensusIntervalMinutes`, int, default `5`, `0` = census off, otherwise
clamped to 1..1440. Runs on the main thread from `PollAllPlayers` when
`UtcNow - lastRun >= interval` (so the first census lands on the first poll tick after
boot, when `ZNetScene` and `ZDOMan` exist). A full walk of a mature world is hundreds of
thousands of objects; measure it (`Stopwatch`) and put `duration_ms` in the payload. Log
one Info line for the first run (`World census: N objects in M ms`) and a Warning whenever
a run exceeds 250 ms; never log every run.

Cache the JSON string in `Companion._censusCache` (volatile, like `_activityCache`), built
on the main thread at the end of the census. Before the first run serve
`EmptyCensusJson` — `enabled` true, empty groups, empty lists, `generated_at ""`. With
the interval at 0, serve `{"enabled":false,...}` with the same shape and 200 — **never
404**, so the page can tell "off" from "a 1.4 server" the way `/api/activity` does.

## 5. `GET /api/census`

Token-gated like the other data routes (add it to `ApiTokenOk`'s list, the `HttpApiToken`
config description, and the startup log line naming the gated routes). Shape:

```json
{
  "generated_at": "2026-09-22T14:05:00Z",
  "enabled": true,
  "interval_minutes": 5,
  "scanned_objects": 812345,
  "duration_ms": 41,
  "groups": {
    "portals":  {"total": 12, "player_built": 12},
    "beds":     {"total": 9,  "player_built": 8},
    "wards":    {"total": 2,  "player_built": 2},
    "ships":    {"total": 3,  "player_built": 3},
    "carts":    {"total": 1,  "player_built": 1},
    "chests":   {"total": 40, "player_built": 40},
    "stations": {"total": 7,  "player_built": 7}
  },
  "builders": [
    {"name": "Nomadtest", "id": 123456789, "portals": 3, "beds": 1, "wards": 0,
     "ships": 1, "carts": 0, "chests": 14, "stations": 3, "pieces": 412}
  ],
  "unknown_builders": 1,
  "portals": [
    {"tag": "home", "kind": "wood", "builder": "Nomadtest", "builder_id": 123456789,
     "connected": true, "x": 123, "z": -456}
  ],
  "beds":  [ {"owner": "Nomadtest", "builder": "Nomadtest", "builder_id": 123456789, "x": 120, "z": -450} ],
  "wards": [ {"builder": "Nomadtest", "builder_id": 123456789, "enabled": true, "x": 118, "z": -448} ]
}
```

- `total` counts every object in the group; `player_built` those with a non-zero creator.
- `builders` holds one row per distinct non-zero creator seen on any counted object,
  named or not, sorted by `pieces` descending then name; `name` is null when unknown.
- `portals` is sorted by tag (ordinal, case-insensitive) then builder; `beds` and `wards`
  by builder then x. Each list is **capped at 500 rows**; when a cap trims a list, add
  `"truncated": true` next to it (`"portals_truncated": true`) and log once.
- All strings through `Companion.Esc`; tags and names are user text.
- `docs/API.md` gets a `/api/census` section in this shape, and a fixture
  `docs/fixtures/api-census.json` with three builders, one unknown, five portals (one
  unconnected, one stone), two beds, one ward.

## 6. Dashboard

A **World** panel inside `#worldPanels`, after the feed panel:

1. A group strip: seven small cards, "12 portals", "9 beds" …, each showing
   `player_built / total` underneath when they differ.
2. **Builders** table, sortable like the other boards (`state.censusSort`, keyboard access
   the same way): Player, Portals, Beds, Wards, Ships, Carts, Chests, Stations, Pieces.
   Known names link to the detail view through the existing `data-select` delegation;
   an unknown builder renders as "Unknown (id …)" in plain text.
3. **Portal directory**: Tag, Kind, Builder, Connected (a tag badge: "linked" / "no pair"),
   Position ("x, z"). Empty tag renders as "(no tag)".
4. **Beds** and **Wards** as two closed `<details>` blocks with compact tables.
5. Footer line: "Counted N objects in M ms, T ago; runs every K minutes."

Polling: `/api/census` on load and then **every 60 s** (its own interval, not
`POLL_MS`), with a change gate on the payload minus `generated_at`, like
`pollActivity`. Outcomes: 404 → hide the World panel only, stop polling it (a 1.4
server); 401 → the same token notice the other panels use; `enabled:false` → the panel
shows one line, "The census is off (`CensusIntervalMinutes = 0`)."; the empty
pre-first-run payload → "First count runs a few seconds after boot."

Detail view, Build tab: one line built from that player's `builders` row — "Standing in
the world: 412 pieces · 3 portals · 1 bed · 14 chests · 3 stations · 1 ship" (omit zero
groups); when the player has no row, "Nothing counted yet." If the vanilla
`PortalsPlaced` stat is present, show it on the same tab as "Portals placed, ever: 14".

Bump `<meta name="theravenscall-api" content="1.5">`; the server's marker check only tests
presence (Companion.cs:283-290), so nothing else changes there. Names, tags and positions
are escaped through `esc()` at every render. At 375 px the tables scroll inside their card
like the standings table does.

## 7. Docs and versioning

- `HexiumDist/README.md`: a "World census" bullet under *What It Tracks*, a paragraph in
  *The Web Dashboard*, the `[Census]` row in *Configuration*, `player_ids.json` under
  *Where Everything Lives*, badge 1.5.0.
- `docs/API.md`: route row + shape section; `docs/DASHBOARD.md`: the World panel;
  `BARRKBOT_CONTRACT.md`: one line — since 1.5.0 `GET /api/census` exists, the export is
  unchanged.
- `HexiumDist/CHANGELOG.md`: a `[1.5.0]` section on top; the "packaged DLL not rebuilt"
  note in the same place 1.4.2's sits (replaced at the cut).
- `Saga.cs` `PluginVersion = "1.5.0"`, `manifest.json` 1.5.0, `HANDOFF.md` status.

## 8. Test plan

1. Build: 0 warnings, 0 errors; the page's inline script passes `node --check`; the helper
   self-test still passes.
2. Storm10 (1.0.12, the world has real portals, beds and a base): boot, wait for the first
   poll tick, `curl /api/census`: `enabled` true, non-zero `portals`/`beds`/`chests`,
   `duration_ms` reported, `scanned_objects` plausible for that world. Note the duration.
3. The builder name resolves: before anyone joins, portals show `builder null` unless a
   claimed bed already named the owner; after the admin joins, `player_ids.json` appears
   with their pair and the rows name them.
4. The directory: the portal tags on Storm10 match what the admin sees in game; a portal
   with no pair shows `connected false`.
5. Token: with `HttpApiToken` set, `/api/census` → 401 without, 200 with.
6. `CensusIntervalMinutes = 0` → 200 with `enabled false`; the panel's one-line notice.
7. Fixture harness (`serve.py` with `docs/fixtures/api-census.json`): the panel renders
   the fixture, sorts, links a known builder, shows "Unknown (id …)", both details blocks
   open and close; 404 mode hides only the World panel.
8. 375 px: the panel stacks, tables scroll inside their card, no horizontal page scroll.

## 9. Non-goals

No change to the export files or `/api/state`. No client-side work. No live player
positions. No history of counts (the census is "now"; the Chronicle is the history).
No hosting of any of this off the server (owner, 2026-09-22).
