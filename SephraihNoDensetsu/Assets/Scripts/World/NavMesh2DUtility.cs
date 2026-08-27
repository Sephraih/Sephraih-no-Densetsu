using UnityEngine;
using UnityEngine.AI;

// Centralizes the single axis mapping between game-space (XY, 2D) and navmesh-space.
//
// NavMeshGround (the baking NavMeshSurface) is rotated -90 on X so its LOCAL frame treats
// world -Z as "up" for slope/voxelization purposes - but NavMesh.CalculateTriangulation()
// still returns baked vertices in real WORLD-SPACE coordinates, unaffected by that rotation.
// All obstacle/floor proxy geometry (Assets/Editor/NavMeshObstacleSync.cs) is built in plain,
// identity-rotated world space at (gameX, gameY, ~0) - so the baked mesh ends up lying flat in
// the world's XY plane already, and X/Y map straight across (no swap).
//
// The Z (navmesh "height") coordinate does NOT reliably bake out to exactly 0, though. In
// practice it settles to some small constant offset (observed as consistently ~-1.03 in this
// project, seemingly a fixed side effect of the rotated bake volume rather than anything tied to
// the geometry's actual position - confirmed uniform across the map via direct NavMesh.
// SamplePosition probes, including right next to obstacle edges). Feeding a query position with
// the WRONG Z into NavMeshAgent.nextPosition/SetDestination causes its on-mesh snap to fail
// outright (SetDestination returns a "successful" but degenerate PathComplete/hasPath=false
// result with zero real movement, which looks exactly like "the enemy reached its destination and
// stopped" even though it never moved) - this was the actual root cause of enemies walking partway
// then freezing. Rather than hardcode that constant (fragile - a future rebake with different
// agent settings could shift it, silently reintroducing this exact bug), the real offset is
// discovered once via a generous-radius NavMesh.SamplePosition probe and cached; call
// InvalidateCache() after any NavMeshSurface.BuildNavMesh() so a later rebake can't leave a stale
// cached offset (DungeonMap.RebuildNavMesh() does this; MapManager.TravelRoutine does it on
// every cross-scene map transition, after the previous scene's NavMesh data has actually unloaded).
//
// Note: NavMeshAgent.baseOffset must be 0 (set in EnemyController.Awake()) for this to work -
// a nonzero baseOffset makes agent.nextPosition report navmeshHeight + baseOffset instead of the
// raw navmesh height this utility deals in, which looks identical to a real per-location height
// variance bug but isn't one.
public static class NavMesh2DUtility
{
    const float ProbeSearchRadius = 50f;

    static float? cachedZOffset;

    public static void InvalidateCache() => cachedZOffset = null;

    // Probes near the position actually being converted rather than a fixed point - multi-area
    // maps (e.g. Dungeon) rebake a single shared NavMeshSurface fresh with only the CURRENTLY
    // ACTIVE area's geometry every time the player switches areas (see MultiAreaMap.RebuildNavMesh),
    // so a fixed probe point only ever finds something if that specific point happens to fall
    // inside whichever area is presently active. World origin only works for an area that happens
    // to sit there (e.g. Dungeon's Level1) - every other area (Level2, Level3, ...) would find
    // nothing nearby and permanently fall through to the 0f fallback for that whole session. The
    // discovered value itself is still assumed uniform across the whole map (a baking-rotation
    // quirk, not geometry-dependent) and is cached the same way - only the probe LOCATION changes.
    static float ZOffset(Vector2 nearPosition)
    {
        if (cachedZOffset.HasValue) return cachedZOffset.Value;
        if (NavMesh.SamplePosition(new Vector3(nearPosition.x, nearPosition.y, 0f), out var hit, ProbeSearchRadius, NavMesh.AllAreas))
        {
            cachedZOffset = hit.position.z;
            return cachedZOffset.Value;
        }
        return 0f; // no baked mesh reachable yet - don't cache a failure, retry next call
    }

    public static Vector3 ToNavMesh(Vector2 gamePos) => new Vector3(gamePos.x, gamePos.y, ZOffset(gamePos));
    public static Vector2 ToGame(Vector3 navPos) => new Vector2(navPos.x, navPos.y);
}
