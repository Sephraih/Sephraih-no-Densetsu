using UnityEngine;

// Attach to any world obstacle (tree, wall, tilemap obstacle/boundary tile group) to declare
// what it blocks. BlocksMovement drives real Collider2D/Rigidbody2D physics (via SyncColliders);
// BlocksSight/BlocksProjectiles have no physics-engine equivalent and are read on demand by
// ObstacleQuery instead.
[DisallowMultipleComponent]
public class Obstacle : MonoBehaviour
{
    [Header("Blocking Flags")]
    public bool BlocksMovement = true;
    public bool BlocksSight = true;
    public bool BlocksProjectiles = true;

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
