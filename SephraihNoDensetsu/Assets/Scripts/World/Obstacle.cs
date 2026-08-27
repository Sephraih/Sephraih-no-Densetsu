using UnityEngine;

// Attach to any world obstacle (tree, wall, tilemap obstacle/boundary tile group) to declare
// what it blocks. BlocksMovement drives real Collider2D/Rigidbody2D physics (via SyncColliders);
// BlocksSight/BlocksProjectiles/BlocksSpell have no physics-engine equivalent and are read on
// demand by ObstacleQuery instead. Flags are intentionally independent bools rather than a single
// tier enum, so future tiers (e.g. "projectiles pass over" or "indoor walls block everything") can
// be expressed as new flag combinations without a breaking enum change.
//
// BlocksSpell drives TWO separate enforcement mechanisms, both keyed off this one flag - no second
// flag needed:
//   - FireStorm's placement check (ObstacleQuery.BlocksSpell/SpellBlocked) - a straight-line raycast,
//     appropriate there since a placement AoE genuinely needs a clear casting line. Needs a real
//     Collider2D (trigger is fine) on the Obstacles/Boundaries layer to register a hit at all.
//   - Teleport/ShadowImpact's landing check (Ability.TryFindWalkableLanding/TeleportConnectivityMask)
//     - a NavMesh connectivity query, NOT a raycast, so it can't be fooled by a destination merely
//     grazing a wall's corner.
//
// NavMeshObstacleSync picks ONE of three NavMesh areas per obstacle based on the BlocksMovement +
// BlocksSpell COMBINATION (a single area can't encode two independent flags at once - see
// NavMeshObstacleSync.Sync()'s own comment for the full reasoning):
//   BlocksMovement=true,  BlocksSpell=false -> "Not Walkable" (ordinary wall; genuine hole, no real
//                                               geometry - confirmed via CalculateTriangulation())
//   BlocksMovement=true,  BlocksSpell=true  -> "Spell Boundary" (blocks movement/pathing too - e.g.
//                                               the Boundary tier)
//   BlocksMovement=false, BlocksSpell=true  -> "Spell Barrier" (movement/pathing pass through freely,
//                                               only Teleport/FireStorm are blocked - e.g. spellBarrier)
// Set BlocksSpell=true on any specific wall/door that should reject Teleport, independent of tier -
// a locked boss-room door, a vault - not just the Boundary tier. Gaplessness matters here more than
// for an ordinary wall: NavMesh.CalculatePath only needs ONE complete route to exist, so a single
// gap (a diagonal-only seam, an untagged doorway) in an otherwise-sealed BlocksSpell=true perimeter
// reconnects it and the whole block silently stops working - test the actual perimeter, don't assume
// a ring of tiles is airtight.
//
// Tier reference (flag combinations content authors should use - set per-obstacle in the Inspector;
// GroundTiles-style purely-cosmetic layers need no Obstacle component at all):
//   boundary      (blocks everything):        BlocksMovement=true,  BlocksSight=true,  BlocksProjectiles=true,  BlocksSpell=true
//   high wall     (blocks walk+shots+sight):  BlocksMovement=true,  BlocksSight=true,  BlocksProjectiles=true,  BlocksSpell=false
//   high          (blocks walk+shots):        BlocksMovement=true,  BlocksSight=false, BlocksProjectiles=true,  BlocksSpell=false
//   low           (blocks walk only):         BlocksMovement=true,  BlocksSight=false, BlocksProjectiles=false, BlocksSpell=false
//   spellBarrier  (blocks spells only):       BlocksMovement=false, BlocksSight=false, BlocksProjectiles=false, BlocksSpell=true
//   "high wall" is a real architectural wall - occludes vision like boundary does, but still lets
//   spells/teleport through (that's reserved for sealed-room/outer-map perimeters). Put it on the
//   Obstacles layer (not Boundaries) - ObstacleQuery's raycast mask includes both layers for every
//   query type, so layer choice is purely a tier-family/organizational label, never a functional gate.
//   Not yet built: a ledge that only blocks from one direction (jump-down) - deliberately deferred,
//   see [[project-teleport-wall-landing]].
[DisallowMultipleComponent]
public class Obstacle : MonoBehaviour
{
    [Header("Blocking Flags")]
    public bool BlocksMovement = true;
    public bool BlocksSight = true;
    public bool BlocksProjectiles = true;
    public bool BlocksSpell = true;

    [Tooltip("Included when baking the NavMesh obstacle proxies. Turn off for obstacles that move at runtime.")]
    public bool NavigationStatic = true;

    void OnEnable()
    {
        ObstacleRegistry.Register(this);
        SyncColliders();
    }

    void OnDisable()
    {
        ObstacleRegistry.Unregister(this);
    }

    void OnValidate()
    {
        SyncColliders();
    }

    void SyncColliders()
    {
        foreach (var c in GetComponentsInChildren<Collider2D>())
            c.isTrigger = !BlocksMovement;
    }
}
