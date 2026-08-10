# Sephraih no Densetsu

Top-down hack-n-slash RPG (Zelda/Ragnarok Online inspired), built in Unity. Revived from an older
project and upgraded to a current Unity version. Unity project root is `SephraihNoDensetsu/`
(this file lives at the git repo root, one level above it).

**Roadmap (near to far, reflects actual priority)**: 1) smarter/more varied enemy AI (LOS + NavMesh
pathing already done), 2) build out the game world using the map/portal system below (authoring new
maps, not infra), 3) real art, 4) more abilities, 5) player progression, 6) UI/UX pass, 7) local
multiplayer, 8) online multiplayer. Default to treating (1) as the active target when asked "what
should I work on" without more context.

## Tooling

`com.ivanmurzak.unity.mcp` is installed — gives AI agents direct control of the Unity Editor
(GameObjects, assets, scenes, prefabs, console, profiler) via `SephraihNoDensetsu/.claude/skills/*`
skill wrappers and an MCP server. If a package version bump on this leaves the Editor crashing on
every reimport (`AssetImportWorkerN has crashed`, `error CS0234`/`CS0115` in `UnityMcpPlugin.cs`),
use **Tools → AI Game Developer → Dependencies → Force Resolve NuGet DLLs** before assuming
something else broke.

Two permission-allowlist files exist per-project (`.claude/settings.json` curated,
`settings.local.json` auto-appended) and can drift apart, causing repeat prompts for
already-allowlisted tools — merge into `settings.local.json` if this happens. Also: Bash and
PowerShell are separate permission namespaces keyed by exact tool name — an allowlisted Bash
command does not cover the identical PowerShell invocation.

Before entering Play mode, run `scene-list-opened` and unload anything other than `Bootstrap.unity`
that got manually opened during investigation — `MapManager` doesn't know about manually-opened
scenes and will load its own duplicate copy on top, causing phantom/duplicate objects. Editing a
`.cs` file while Play mode is running triggers a domain reload that wipes `MapManager.Instance` (and
other Play-mode statics) back to null with no re-`Awake()` — exit and re-enter Play mode after any
mid-Play script edit rather than continuing to test in the same session.

`EditorSceneManager.playModeStartScene` is purely in-memory and resets on every Editor restart — the
durable fix (already in place) is `Assets/Editor/PlayModeStartSceneSetup.cs`, an `[InitializeOnLoad]`
script that reassigns it to `Bootstrap.unity` on every domain reload. If a `NullReferenceException`
on `Player`/`MapManager.Instance` shows up, check this script still exists/compiles before doing
anything else.

## Scene & map architecture

`Assets/Scenes/Bootstrap.unity` is the one persistent scene (Play Mode Start Scene, Build index 0) —
holds Player, Main Camera, Canvas, EventSystem, and the `MapManager` singleton. Each named place
(overworld hub, dungeon, fields) is its own scene, loaded/unloaded *additively* by
`Assets/Scripts/World/MapManager.cs.TravelTo(sceneName, spawnPointId)`. Sub-areas within one scene
(e.g. Dungeon's levels, a field's multiple zones) use a cheap sibling-GameObject `SetActive` toggle
instead — `Assets/Scripts/MultiAreaMap.cs` (base class for `CityMap`/`DungeonMap`/`FieldMap`) owns
this via `ActivateArea()`, which deactivates every other `LevelBehaviour`-rooted area in the scene
every time (self-correcting regardless of what got saved active).

Each scene's root controller implements `IMap`/`MapBehaviour`: `OnMapEntered(spawnId)`,
`OnMapAboutToUnload()`, `OnPortalUsed(portal)`. Portals (`PortalBehaviour`) and targets
(`SpawnPoint` — `PortalExit` was deleted/unified into this) are data-driven: drag a `SpawnPoint`
into a portal's `target` field and it resolves same-scene vs. cross-scene automatically via
`OnValidate()`. Build new connections from the `Assets/Resources/Prefabs/GameObjects/Portal.prefab`
and `Exit.prefab` prefabs, not hand-assembled GameObjects.

**`Assets/Prefabs/Maps/MapArea.prefab`** is the reusable building block for any new map: one `Grid`
with `GroundTiles` (cosmetic only, no Obstacle) / `LowTiles` / `HighTiles` / `SpellBarrierTiles` /
`BoundaryTiles` (5-tier Obstacle scheme, see below) / `GroundMaterials` (dual-grid ground-material
patches, see below), plus `MapBoundary` + `InteriorSeed` + `LevelBehaviour` on the root. Instantiate
it multiple times as sibling levels under a `DungeonMap` (Dungeon-style), or once as a whole
standalone scene paired with a `CityMap`/`FieldMap` component (hub/field-style, e.g.
`MainFieldSouth.unity`). Does not include a `NavMeshSurface` — that's one per scene
(`NavMeshGround` root), regardless of how many `MapArea` instances live inside it.

**Place multiple `MapArea` instances in a scene at distinct, well-separated world positions, never
stacked near the origin** — `NavMeshSurface` registers globally, not per-scene, so overlapping
areas corrupt each other's bake unless strict mutual-exclusion is guaranteed.

After painting/editing any obstacle or boundary tiles in a scene, re-run
**`Tools/Sephraih/Sync NavMesh Obstacle Proxies`** (`NavMeshObstacleSync.cs`) — it rebuilds the 3D
proxy geometry NavMesh bakes against, including regenerating Manual-mode `CompositeCollider2D`
geometry (which does NOT auto-regenerate on its own — see the Manual-collider gotcha below).

## 5-tier Obstacle scheme

`Obstacle.cs` flags (its own header comment is authoritative):
```
boundary      BlocksMovement=true,  BlocksSight=true,  BlocksProjectiles=true,  BlocksSpell=true
high          BlocksMovement=true,  BlocksSight=false, BlocksProjectiles=true,  BlocksSpell=false
low           BlocksMovement=true,  BlocksSight=false, BlocksProjectiles=false, BlocksSpell=false
spellBarrier  BlocksMovement=false, BlocksSight=false, BlocksProjectiles=false, BlocksSpell=true
ground        no Obstacle component at all (purely cosmetic, e.g. GroundTiles/dual-grid layers)
```
Two custom NavMesh areas back this: `"Spell Boundary"` (boundary tier) and `"Spell Barrier"` (barrier
tier) — kept separate because a single area can't independently answer "excluded from ordinary
walking" vs. "excluded from teleport" for two different tier combinations. `WalkableAreaMask`
excludes `Not Walkable` + `Spell Boundary`; `TeleportConnectivityMask` excludes both spell areas.
These two custom areas get **zero** automatic erosion clearance from agentRadius (unlike ordinary
obstacles, which get real holes carved) — `NavMeshObstacleSync.SpellAreaPadding` (a standalone
`0.45f` constant, deliberately decoupled from `agentRadius`) pads their proxies manually. Retune
`SpellAreaPadding` directly if clipping reappears, not `agentRadius`.

Teleport (`Ability.TryFindWalkableLanding`) and ShadowImpact (`TryFindReachableLanding`) both query
the baked NavMesh, not live physics — Teleport deliberately allows landing in sealed-off walkable
pockets (caster-style, can reach isolated vantage points); ShadowImpact requires an actual walkable
route from the caster's position (melee-style, must never strand the attacker). Don't unify these
into one check — the distinction is intentional.

## NavMesh 2D — settled values, read before touching pathing

This project bakes a flat 2D game onto Unity's inherently-3D NavMesh via a `NavMeshSurface` rotated
-90° on X. This has produced 14 separate, non-obvious bugs across many sessions (full blow-by-blow
in local memory `project_navmesh_2d_gotchas.md`, 72 lines — read it before assuming a 15th bug if
pathing looks wrong again). Settled facts to know now:

- **`agentRadius = 0.6`** (`ProjectSettings/NavMeshAreas.asset`, "Humanoid" type) — balances
  teleport reaching small sealed rooms against enemies wedging on wall corners. This value has a
  real history of being fought over between those two concerns (0.2 → 0.45 → 0.65 → 0.75 → 0.4 →
  0.5 → 0.6) — don't re-tune without a new concrete symptom; if one appears, read the full history
  first. Edits via `SerializedObject` on the already-loaded asset take effect immediately, no Editor
  restart needed (the "restart required" note from earlier in that memory was specific to a
  different, file-write-based edit method).
- Every `NavMeshAgent` needs `baseOffset = 0` and `areaMask` excluding `"Not Walkable"` — both set
  in `EnemyController.Awake()`. Unity's own built-in `"Not Walkable"` area gets real holes carved
  (zero triangles); a *custom* area name does not get this treatment automatically — it needs an
  explicit mask exclusion, and still generates real underlying geometry.
- Any tilemap or discrete `Collider2D` obstacle on an **inactive** GameObject hierarchy silently
  returns degenerate/zero geometry from `Tilemap.GetCellCenterWorld()`/`CellToLocal()`/
  `Collider2D.bounds`/`OverlapPoint()` — this project's fix computes geometry from plain serialized
  fields (`cellSize`, `PolygonCollider2D.points`, etc.) + `Transform.TransformPoint` instead, which
  works regardless of active state. If adding new active-state-sensitive geometry queries, follow
  this pattern, don't call the native active-state-dependent APIs directly.
- A freshly-built `NavMeshGround` must have `transform.rotation = Quaternion.Euler(-90, 0, 0)` set
  explicitly — copying only the `NavMeshSurface` *component's* field values from an existing scene
  is not sufficient, rotation lives on the Transform and isn't part of the component.
- `CompositeCollider2D` obstacle tiers use `generationType = Manual` (perf fix — `Synchronous`
  recomputes on every single `Tilemap.SetTile` call, causing serious paint-lag). This means Manual
  geometry does **not** auto-regenerate after a `SetActive(false)`→`(true)` cycle —
  `MultiAreaMap.ActivateArea()` explicitly calls `GenerateGeometry()` on every Manual composite under
  the area being activated to cover this; if a boundary/wall ever becomes walk-through-able again
  after revisiting an area, check that loop still runs and still finds the right colliders.
- All Tile assets should have `colliderType = Grid`, not `Sprite` (the latter uses the sprite's own
  inset physics outline, leaving real gaps between adjacent tiles and exploding composite
  `pathCount`) — this is the default going forward, not a one-off fix.
- Enemy-vs-enemy avoidance is an omnidirectional separation force
  (`EnemyController.DeflectAroundOtherUnits`, tunable `separationRadius`/`separationStrength` per
  type), not NavMeshAgent RVO — deliberately, since `NavMeshAgent.updatePosition = false` in this
  project (Rigidbody2D is authoritative) and making the agent authoritative for RVO would reopen the
  whole Z-axis/coordinate-space bug class above. Revisit only if separation-force tuning breaks down
  under large-group congestion, not before.

## Ability architecture

Every unit (player and enemy) holds its ability scripts on a dedicated child GameObject named
`Abilities`, alongside an `AbilityController` that indexes them into fixed slots 0-9
(`AbilityController.cs`). Callers look it up via `GetComponentInChildren<AbilityController>()`. If a
`NullReferenceException` traces back to that call returning null, check the unit's hierarchy for a
missing/misconfigured `Abilities` child before assuming a logic bug elsewhere. New abilities/units
should follow this pattern, not put components directly on the unit root — it's what a future
action-bar/skill-unlock UI will depend on to query abilities generically.

**Screen-relative range system**: enemy perception ranges and spell ranges cascade from one shared
`Assets/Settings/RangeSettings.asset` (`FieldOfView`, currently `9.6` world units, derived from
screen half-width so mobs roughly stay on-screen while chasing), expressed as per-type/per-ability
percentages rather than hardcoded absolutes. Prefer retuning `FieldOfView` (global) or a specific
`*Percent` field (per-type) over hardcoding a new absolute range number anywhere.

`EnemyController.UpdateState()` sets `BotState.Return` after losing line-of-sight, but each
subclass's own `Move()` must implement the `Idle`/`Return` case itself (path back to a tracked home
spot, e.g. `GuardBehaviour`'s `guardSpot`) — a subclass without this case freezes in place forever
instead of returning. Check this first if an enemy "stops following" and it isn't a NavMesh issue.

## Ground-material tile painting (dual-grid system)

Hard-edged ground-material patches (mud, water, etc. — distinct from the smooth painted Terrain
background, see below) use jess::codes' true Dual Grid technique: one data+render `Tilemap` pair per
material, reactive via `Assets/Scripts/World/DualGridTilemapModule.cs`. Paint on the invisible
`*Data` tilemap (any non-null tile = filled); the offset `*Render` tilemap updates automatically via
the static `Tilemap.tilemapTileChanged` event, both in-Editor and at runtime. Each render cell's
sprite is picked from the 4 data cells touching its corners — `cornerFilled = diagonal && ortho1 &&
ortho2`, enumerated as 256 raw 8-neighbor rules mapped down to 16 sprites. A single-grid
(non-offset) version was tried first and rejected — it can't render a 1-wide line or an isolated
single-tile dot/hole as connected shapes, only true dual-grid's sub-cell resolution can.

Lives in `Assets/Prefabs/Maps/MapArea.prefab`'s `Grid/GroundMaterials/` (identity transform
required — any offset compounds with the render tilemap's own `(0.5,0.5)` offset), so it's available
on every map automatically. Current instances: `MudRedData`/`MudRedRender` (order `-6`),
`WaterCyanData`/`WaterCyanRender` + `WaterMatteBlueData`/`WaterMatteBlueRender` (order `-8` each,
independent alternates, not stacked). Sorting scheme (all layer `"tiles"`): Terrain background `-10`
< ground-material layers (spaced, room for more) < `GroundTiles` decorative overlay `0` = wall/
obstacle tiers `0`. Adding a new material needs no code changes — slice its 16-sprite sheet, make a
marker `Tile` asset, add a Data+Render pair under `GroundMaterials`, add
`DualGridTilemapModule` pointing at the new sprite path/prefix, set sorting order per this scale.

The older, non-dual-grid `grf_tiles` tileset (`Assets/Resources/Sprites/grf_tiles/`) is still in use
on `GroundTiles` for general ground decoration — its Tile Palette definition is stored as
`Ground.prefab` in that folder (has a "Palette Settings" component; it's the picker/swatch board,
not painted map content).

## Terrain (painted background) pipeline

Zone backgrounds are painted using Unity's real `Terrain` brushes in a disposable
`Assets/Scenes/_TerrainAuthoring.unity` scene (never added to Build Settings), then baked to a
static Sprite via `Assets/Editor/TerrainBakeTool.cs`
(`Tools/Sephraih/Terrain Bake/Bake Selected Terrain To Sprite`) — no live Terrain ever ships.
Downloaded PBR normal maps (ambientCG "NormalGL" or AI-generated) need their green channel inverted
before use as a `TerrainLayer.normalMapTexture` on this project's Built-in-RP setup (confirmed 3/3) —
do this by default, save as a `*_YFlip.png` sibling, don't skip it as "should already be correct."
