# NavMesh Teleport Design & Tilemap Setup — Q&A Notes

Captured from a design/implementation discussion covering: reworking Teleport to use NavMesh instead of straight-line obstacle checks, the "tower" reachability question this raised, the resulting implementation, and how the tilemap/obstacle editing workflow actually works in this project.

---

## 1. Could Teleport use the NavMesh approach, like ChargeAttack?

**Q:** Could we use the navmesh approach, similar to ChargeAttack, to check the target location's closest walkable area and move the user there? Caveat: what if we added a "tower" with a walkable area in the middle, surrounded by walls — would the player be able to teleport into the center?

**A:** Yes — `NavMesh.SamplePosition(point, out hit, searchRadius, areaMask)` finds the nearest point *on* the navmesh to an arbitrary point (already used by `WizardBehaviour.GetFleeDirection` for exactly this). But `SamplePosition` only answers "what's nearby," never "what's connected to what" — it has no concept of reachability. So yes: aiming near a tower whose interior is baked as walkable NavMesh would let `SamplePosition` snap you inside it, even if that interior is fully sealed off by walls. This is exactly why `ChargeAttack` can never do this — it uses `NavMesh.CalculatePath` (via `TryGetWalkPath`), which requires an actual connected route and would report the tower's interior as unreachable, so the ability just wouldn't fire.

The fix/distinction: add a second check — `NavMesh.CalculatePath` from the caster's current position to the candidate, requiring `NavMeshPathStatus.PathComplete` — if you want a teleport to be limited to places you could otherwise walk to. Without that check, a teleport can reach sealed pockets (the tower). With it, it can't. Both sides of an *ordinary* wall (open ends) stay reachable either way, since they're on the same connected island, just requiring a detour on foot.

---

## 2. Follow-up: the tower should actually be reachable — three sub-questions

**Q (reframed):** My original idea was that the tower *should* be teleport-reachable — I was more worried the navmesh would bake it as unwalkable just because it's disconnected. So this is actually closer to ideal: we could define areas reachable by teleport and others not. Raised:
1. How does the tile-obstacle bake work in detail — with 4 wall tiles in a square, is there a crack in the middle reachable via diagonal touch?
2. What if a unit reaches a location via teleport with no way out? (Concrete scenario: wizard teleports onto a tower, player kills it there via ShadowImpact, and if that player doesn't have Teleport, they're stuck.)
3. The wall tier flags (Boundary/High wall/Low wall) should already define "is this reachable by teleport."

**A:**

1. **Solid 2×2 filled block**: no crack, confirmed by reading `NavMeshObstacleSync.cs` directly. Each wall tile generates its own "Not Walkable" proxy box, sized exactly to `cellSize`, centered at the exact cell center (`CellCenterWorld`). Four adjacent tiles at (0,0)/(1,0)/(0,1)/(1,1) produce boxes spanning `[0,1]×[0,1]`, `[1,2]×[0,1]`, `[0,1]×[1,2]`, `[1,2]×[1,2]` — these tile perfectly with zero gap by construction. **4 tiles touching only diagonally** (checkerboard/X pattern) is the genuinely real risk — voxelization + agent-radius erosion (0.75 in this project) don't always treat a corner-only touch as fully sealed. Not confirmed either way without an actual bake-and-probe test; worth doing before building a diamond/X-shaped tower specifically.

2. **Real, and sharper than it first sounds.** `ShadowImpact` uses the same landing-validity mechanism as `Teleport` (see implementation below) and repositions the *attacking* player, not just the target — so a player who never personally cast a teleport can still get relocated onto the tower via ShadowImpact's own reach, if the target happens to be standing up there. The clean long-term answer discussed: one-way drop-down ledges (walkable one direction, blocked the other) — a natural fit for this game's Zelda-like inspiration, but needs a new *directional* concept, since `Obstacle.BlocksMovement` is currently symmetric. **Deferred to a later session**, not built yet.

3. **Yes, exactly right, and no new mechanism was needed** — `Obstacle.BlocksSpell` is already this lever, per obstacle instance. Boundary tier = `BlocksSpell=true` (no teleport crossing at all); High/Low wall = `BlocksSpell=false` (crossing allowed) today, identically for both. If a tower's walls should read differently from an ordinary fence for teleport purposes specifically, that's a naming/preset question ("Vantage wall") rather than a new flag — the flag itself already exists.

---

## 3. Implementation

Built two new reusable methods on `Ability.cs`, replacing the older `ObstacleQuery.BlocksLanding`/`Ability.LandingBlocked` (deleted):

- **`TryFindWalkableLanding(point, searchRadius, out landing)`** — pure `NavMesh.SamplePosition`, no connectivity check. "Caster teleport" style: can land in an isolated walkable pocket (a sealed tower top) by design.
- **`TryFindReachableLanding(from, point, searchRadius, out landing)`** — same snap, plus `NavMesh.CalculatePath` requiring `PathComplete`. "Melee-ish teleport" style: refuses anything not actually walkable-to from the caster's current position.

Wired up:
- **`Teleport.cs`** (`Use()`/`UseMouse()`) → `TryFindWalkableLanding`. Can now reach isolated pockets.
- **`ShadowImpact.cs`** (`SlashCoroutine()`'s per-hit reposition) → `TryFindReachableLanding`. Will never strand the attacker on an unreachable ledge — directly addresses the stranding scenario from Q2 above. User noted this choice may get revisited once one-way ledges exist, since that would give a "real" way out and make the stricter check less necessary for this specific ability.
- **`ChargeAttack.cs`** — unchanged, already safe by construction (real NavMesh pathing via `TryGetWalkPath`, never a blind reposition).

**Verified live** against a real, freshly-baked isolated NavMesh island (a 4-wall ring built from `NavMeshModifier`/`BoxCollider` proxies + an actual `NavMeshSurface.BuildNavMesh()` rebake — spawned colliders alone aren't enough, since these methods query the *baked* navmesh):
- `TryFindWalkableLanding` landed within microns of a sealed tower's exact center.
- `TryFindReachableLanding` correctly refused the same point, while still succeeding normally for an ordinary connected point (sanity-checked).
- Full end-to-end `Teleport.Use()` landed the player 0.1 units from the sealed tower's center when aimed at it.
- **Test-construction gotcha worth remembering**: a first attempt with a ~1.6-unit interior baked with *no* walkable navmesh inside at all — smaller than 2× the agent radius (0.75), so erosion ate the whole interior. Not a bug in the fix; test towers need a genuinely walkable interior (comfortably >3 units across) to be meaningful.

Full architecture notes live in the auto-memory system (`project_teleport_wall_landing.md`) for future sessions.

---

## 4. Tilemap editing basics — how do I draw tiles again, do I need colliders?

**Q:** It's been a while since I made the existing tilemaps — how do I edit/draw them, do I need to add colliders, how do flags get set on new tiles?

**A:** Checked the actual `ObstacleTiles` GameObject to confirm rather than guess. The `Obstacle` component (with `BlocksMovement`/`BlocksSight`/`BlocksProjectiles`/`BlocksSpell`) lives on the **tilemap layer's own GameObject**, alongside `Tilemap`+`TilemapRenderer`+`Rigidbody2D`+`CompositeCollider2D`+`TilemapCollider2D` — one shared set of flags for the whole layer, **not per-tile**. `TilemapCollider2D`/`CompositeCollider2D` regenerate live as you paint.

Practical result: painting more tiles onto an *existing* layer needs zero extra setup — new tiles automatically inherit that layer's flags and get collision for free. For test towers specifically:
- Paint onto `Tilemap-Obstacles/ObstacleTiles` for a teleport-reachable tower (`BlocksSpell=false` already).
- Paint onto `Tilemap-Boundaries/BoundaryTiles` for a tower that should correctly refuse teleport (`BlocksSpell=true`).

How: select the target GameObject, open `Window > 2D > Tile Palette`, pick the matching tile set, paint in the Scene view.

**One easy-to-miss extra step**: after painting, re-run `Tools > Sephraih > Sync NavMesh Obstacle Proxies` — this project's own editor tool that snapshots obstacle geometry into NavMesh bake proxies. Not automatic/live; new tiles won't affect pathing/reachability until this runs. The actual bake itself then happens automatically at runtime (`DungeonMap` rebuilds on level load).

---

## 5. The two Tile Palette dropdowns, and the Tilemap-Obstacles/ObstacleTiles relationship

**Q:** Two dropdowns in the tile editor — one shows `GroundTiles/ObstacleTiles/BoundaryTiles`, the other shows `Boundary/Collider/Ground`. How are they wired? And is `Tilemap-Obstacles` containing `ObstacleTiles` a real connection, or just naming?

**A:** Confirmed via the actual asset database:

- **`GroundTiles/ObstacleTiles/BoundaryTiles`** = the **Active Tilemap** dropdown — which *scene* Tilemap GameObject your brush paints onto (the ones carrying `Obstacle` + its flags).
- **`Boundary/Collider/Ground`** = the **Palette** dropdown — separate assets at `Assets/Resources/Sprites/grf_tiles/{Boundary,Collider,Ground}.prefab`, i.e. which tile *art set* you're picking brush tiles from.

These are independent — nothing stops picking from the wrong palette while painting onto the wrong tilemap except naming discipline (`Collider` palette ↔ `ObstacleTiles` tilemap, `Boundary` ↔ `BoundaryTiles`, `Ground` ↔ `GroundTiles` — matched by convention, not enforced). Painting just stores the picked `Tile` asset into the *active tilemap's* cell data; the palette isn't remembered afterward.

`Tilemap-Obstacles` (parent) has only a `Grid` component — pure organizational container. `ObstacleTiles` (child) is where everything real lives. Purely naming convention, not a technical requirement — Unity would behave identically if they were named anything else.

---

## 6. GameObject structure choices, and does a new obstacle type need "wiring up" as a paint target?

**Q:** Could I create one `Tiles` parent GameObject and put `ObstacleTiles`/`BoundaryTiles`/new layers directly under it (or skip the parent entirely)? Also — after realizing selecting `BoundaryTiles` in the Hierarchy changes the active target in the Tile Palette — does a new obstacle type (a new wall tier) need to be manually registered as a paintable target, or does that happen automatically once the right components are added?

**A:**
- **GameObject structure**: either works. A `Tilemap` just needs *some* `Grid` ancestor for its cell coordinate system — one shared `Grid` with multiple `Tilemap` children (your `Tiles` idea) is actually the more common Unity convention than this project's current one-`Grid`-per-layer pattern. Recommendation: don't restructure the three existing layers (they're tested and working) — for a *new* layer, just add a new `Tilemap-HighWall` → `HighWallTiles` pair matching the existing per-layer convention. Consolidating everything under one shared `Tiles` grid is a valid but separate, optional cleanup for later.
- **Paint-target registration is fully automatic**, and specifically tied to one thing only: the presence of a `Tilemap` component. The Tile Palette's "Active Tilemap" dropdown is a live list Unity populates by scanning the open scene for `Tilemap` components — the moment you add one (easiest via `GameObject > 2D Object > Tilemap > Rectangular`, which also sets up its `Grid` parent), it's immediately paintable. Selecting a Tilemap-bearing GameObject in the Hierarchy syncs the Tile Palette to match it (the behavior just observed with `BoundaryTiles`) — the GameObject *is* the target, not something wired to it separately. The other components (`TilemapCollider2D`, `Rigidbody2D`, `CompositeCollider2D`, `Obstacle`) are unrelated to paintability — they only matter for what happens after painting (collision, obstacle flags), and can be added in any order, whenever, before or after you've started painting.

**Confirmed physics layer numbers** (used by `ObstacleQuery.ObstacleLayerMask`, which only ever scans these two): `Obstacles` = layer 12, `Boundaries` = layer 11. A new obstacle-type GameObject must be set to one of these, or every obstacle check in the game (sight/spell/walk/teleport) will silently ignore it even though it still physically collides.
