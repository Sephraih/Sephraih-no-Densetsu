using UnityEngine;

// Attach to any world obstacle (tree, wall, tilemap obstacle/boundary tile group) to declare
// what it blocks. BlocksMovement drives real Collider2D/Rigidbody2D physics (via SyncColliders);
// BlocksSight/BlocksProjectiles/BlocksSpell have no physics-engine equivalent and are read on
// demand by ObstacleQuery instead. BlocksSpell governs both teleport-type movement abilities and
// placement/location-targeted spell AoEs - the two were merged into one flag because they're the
// same question in practice ("can something be conjured/moved to the other side of this obstacle
// without physically walking around it"): if a spell can be placed across, a teleport can just as
// validly land there. Flags are intentionally independent bools rather than a single tier enum, so
// future tiers (e.g. "projectiles pass over" or "indoor walls block everything") can be expressed
// as new flag combinations without a breaking enum change.
//
// Tier reference (flag combinations content authors should use - none of these are applied to
// existing scene content automatically; set them per-obstacle in the Inspector):
//   Boundary (blocks everything):   BlocksMovement=true, BlocksSight=true,  BlocksProjectiles=true,  BlocksSpell=true
//   High wall (blocks walk+shots):  BlocksMovement=true, BlocksSight=true*, BlocksProjectiles=true,  BlocksSpell=false
//   Low wall (blocks walk only):    BlocksMovement=true, BlocksSight=true*, BlocksProjectiles=false, BlocksSpell=false
//   * BlocksSight is independent of this tier scheme - set per-obstacle to taste, not implied by
//     the movement/spell/projectile tier.
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
