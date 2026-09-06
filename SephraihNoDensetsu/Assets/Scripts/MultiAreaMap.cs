using UnityEngine;
using Unity.AI.Navigation;

// Shared base for any Map controller that hosts more than one same-scene sub-area (a MapArea
// instance) toggled via SetActive and connected by SameSceneSubArea portals - DungeonMap (linear
// Level1/2/3) and FieldMap (non-linear zone network) both derive from this. Pulls out exactly the
// parts that don't depend on a specific area topology: which area is currently active, rebaking
// the shared NavMeshSurface on every switch, the tier-agnostic stuck-in-wall rescue, and resolving
// a target area purely by direct GameObject reference (via a SpawnPoint's owning LevelBehaviour)
// rather than any index/order assumption - which is what lets a scene with several
// non-linear zones, or several distinct entrances, work correctly, not just a strictly linear
// sequence with exactly one entrance.
public abstract class MultiAreaMap : MapBehaviour
{
    [SerializeField] protected NavMeshSurface navMeshSurface;

    // Second, much-tighter-radius bake (the "TeleportLanding" agent type, see ProjectSettings/
    // NavMeshAreas.asset) used ONLY by Ability.TryFindWalkableLanding/TryFindReachableLanding
    // (Teleport, ShadowImpact) so those can land as close to a collider as the player could
    // physically walk, independent of navMeshSurface's agentRadius (tuned instead for
    // EnemyController's NavMeshAgent pathing/corner-wedging - see project_navmesh_2d_gotchas
    // memory). Baked from the identical proxy geometry NavMeshObstacleSync generates - just
    // re-eroded at a different radius - so every floor-gap/rasterization fix already in place
    // applies to this bake automatically, no separate logic needed here.
    [SerializeField] protected NavMeshSurface teleportNavMeshSurface;

    // Tracks whichever Level/MapArea GameObject is currently active - the authoritative "what's on
    // now" reference for both GoToExit (same-scene) and OnMapEntered (cross-scene arrival) below,
    // so the two paths can never leave two areas active at once, even if a subclass additionally
    // routes through its own index-based activation (e.g. DungeonMap's ActivateLevel).
    protected GameObject activeAreaObject;

    protected GameObject Player => MapManager.Instance.Player;

    protected virtual void Update()
    {
        Unstuck();
    }

    // Rebuilds the NavMesh for whichever area is currently active. Only one area is ever active at
    // a time (the rest sit disabled), so a single shared NavMeshSurface is baked fresh on every
    // area switch rather than maintaining one pre-baked NavMeshData per area.
    protected void RebuildNavMesh()
    {
        if (navMeshSurface != null) navMeshSurface.BuildNavMesh();
        if (teleportNavMeshSurface != null) teleportNavMeshSurface.BuildNavMesh();
        // The baked mesh's real height (navmesh Z) can shift between bakes - drop the cached
        // value so the next NavMesh2DUtility query re-discovers it instead of using a stale one.
        NavMesh2DUtility.InvalidateCache();
    }

    // Activates `area` and rebakes. Works for any topology - the caller only ever needs a
    // GameObject reference, never an index or a position in some ordered list.
    //
    // Deactivates EVERY other LevelBehaviour-rooted area in this scene, not just whatever
    // activeAreaObject happens to already track - relying on activeAreaObject alone left every
    // OTHER area's active state entirely dependent on how the scene happened to be SAVED, since
    // activeAreaObject starts null on every fresh load and the old single-object deactivate never
    // ran against anything else on that first call. Worked for Dungeon purely by luck (its levels
    // were always manually deactivated before saving); a scene saved with more than one area
    // active (confirmed: a fresh multi-zone scene, nothing had ever explicitly turned the other
    // zones off) silently kept ALL of them live - simultaneously baked into the same NavMesh,
    // simultaneously ticking any per-area behaviour - with nothing in code ever catching it.
    protected void ActivateArea(GameObject area)
    {
        foreach (var root in area.scene.GetRootGameObjects())
            foreach (var lb in root.GetComponentsInChildren<LevelBehaviour>(true))
                if (lb.gameObject != area) lb.gameObject.SetActive(false);

        area.SetActive(true);
        activeAreaObject = area;

        // Every obstacle tier's CompositeCollider2D uses generationType=Manual (see
        // NavMeshObstacleSync's own doc comment - avoids recomputing the composite shape on every
        // single tile paint). Confirmed live: deactivating then reactivating a GameObject clears a
        // Manual-mode CompositeCollider2D's generated geometry (pathCount 41 -> 0) - Unity does NOT
        // regenerate it automatically on OnEnable the way it does on the very first scene load, and
        // nothing else in the runtime game code ever called GenerateGeometry() again afterward. The
        // navmesh bake below still worked fine (BuildNavMesh() reads collider geometry through its
        // own path, not the live Physics2D broadphase), which is exactly why this bug looked like
        // "Teleport still blocks correctly, but walking straight through every wall" - two
        // completely different systems reading two different snapshots of the same collider. Fixed
        // by explicitly regenerating every Manual composite in the area being activated, every
        // single time - not just relying on whatever shape happened to survive the scene's last
        // save (which is only reliable for an area's very first activation, per this exact bug).
        foreach (var cc in area.GetComponentsInChildren<CompositeCollider2D>(true))
            if (cc.generationType == CompositeCollider2D.GenerationType.Manual)
                cc.GenerateGeometry();

        RebuildNavMesh();
    }

    // Physics-overlap check against the real (non-trigger) colliders on the Obstacles/Boundaries
    // layers, via the same ObstacleLayerMask ObstacleQuery already uses - covers every obstacle
    // tier (low/high/boundary all have BlocksMovement=true, hence non-trigger colliders) with no
    // per-tilemap references needed. A trigger collider (spellBarrier, BlocksMovement=false)
    // correctly does NOT count as stuck - standing there is legitimate, not a rescue case.
    //
    // Deliberately OverlapCircle, NOT OverlapPoint - project_navmesh_2d_gotchas memory (the
    // diagnostic note right before bug #14) already proved Physics2D.OverlapPoint unreliably
    // returns ZERO hits against a CompositeCollider2D with geometryType=Outlines (which every
    // obstacle tier in this project uses, TowerWall's obstacle-companion included), confirmed live
    // in Play mode standing exactly on top of a real, correctly-configured wall - "Outlines"
    // geometry has no filled interior for a bare point-in-polygon test to land in, while every
    // shape/sweep query (OverlapCircle/OverlapBox/Raycast/CircleCast) works fine. Unstuck() using
    // OverlapPoint meant this exact rescue never fired for a player who ends up inside/on an
    // Outlines-geometry obstacle (e.g. Teleported into a TowerWall structure via a NavMesh landing
    // right at its edge) - real collision still correctly bounded them from crossing either the
    // inner or outer edge, so they could slide around trapped inside the wall's own solid ring
    // indefinitely, never triggering the snap-back-to-saveSpot rescue. A tiny radius (not a real
    // player-sized check) keeps this a near-point test in practice, just one OverlapPoint can't
    // reliably perform against this geometry type.
    const float UnstuckCheckRadius = 0.05f;
    static readonly Collider2D[] unstuckHitBuffer = new Collider2D[8];
    static bool warnedNoMapManager = false;

    // Root-cause fix for the wall-wobble bug (see project memory / this session's WallClampDebug +
    // UnstuckDebug logs): resting flush against a wall - the correct, expected outcome now that
    // MovementController clamps velocity into a contact - still leaves the player's pivot within
    // UnstuckCheckRadius of the wall's collider, since the probe is centered ON the pivot itself.
    // The old "any overlap at all = stuck" check couldn't tell that apart from genuinely being
    // embedded inside solid geometry, so it fired EVERY Update() frame during ordinary wall-holding,
    // snapping the player back to saveSpot only for held input to immediately drive them right back
    // into the same overlap next frame - a perpetual ~0.1-unit teleport oscillation, confirmed live
    // (UnstuckDebug.log: playerPos frozen at the wall, saveSpot frozen one snap-back behind it,
    // hitCount=1, on effectively every frame while the key was held).
    //
    // Fix: gate on actual geometric penetration DEPTH against the player's own real (non-trigger)
    // collider via Physics2D.Distance, not just "the tiny pivot probe touched something." Flush,
    // correctly-clamped wall contact produces ~zero penetration (confirmed via WallClampDebug.log -
    // zero position oscillation once the velocity clamp landed), so MinStuckPenetrationDepth only
    // trips for real embedding. If a future genuinely-stuck case (e.g. bug #14's Teleport-into-an-
    // Outlines-ring landing) turns out to be too shallow to clear this threshold, the fix is a
    // persistence/inescapability signal (e.g. "still overlapping after N frames of movement attempts
    // in multiple directions"), not just lowering this number back toward zero - that would
    // reintroduce this exact false-positive.
    const float MinStuckPenetrationDepth = 0.03f;
    static Collider2D cachedPlayerSolidCollider;
    static GameObject cachedPlayerSolidColliderOwner;

    static Collider2D GetPlayerSolidCollider(GameObject player)
    {
        if (cachedPlayerSolidColliderOwner == player && cachedPlayerSolidCollider != null)
            return cachedPlayerSolidCollider;

        cachedPlayerSolidColliderOwner = player;
        cachedPlayerSolidCollider = null;
        foreach (var c in player.GetComponents<Collider2D>())
        {
            if (!c.isTrigger) { cachedPlayerSolidCollider = c; break; }
        }
        return cachedPlayerSolidCollider;
    }

    // Diagnostic toggle (off by default) - logs every actual rescue (stuck==true) fire to a separate
    // batched file, same reasoning as MovementController's own DebugWallClamp (avoid unbatched
    // File.AppendAllText hanging the Editor at Update() frequency). Flip to true for a future
    // stuck/rescue investigation, see project_wall_wobble_unstuck memory for the bug this was built
    // to diagnose (Unstuck's penetration-depth gate false-positiving again, etc.).
    public static bool DebugUnstuck = false;
    static readonly System.Text.StringBuilder unstuckLogBuffer = new System.Text.StringBuilder();
    static int unstuckLogPendingLines = 0;
    const int UnstuckLogFlushThreshold = 10;

    static void FlushUnstuckLog()
    {
        if (unstuckLogBuffer.Length == 0) return;
        System.IO.File.AppendAllText(Application.persistentDataPath + "/UnstuckDebug.log", unstuckLogBuffer.ToString());
        unstuckLogBuffer.Clear();
        unstuckLogPendingLines = 0;
    }

    protected virtual void OnDisable()
    {
        if (DebugUnstuck) FlushUnstuckLog();
    }

    protected void Unstuck()
    {
        // MapManager only exists once Bootstrap.unity is part of the loaded set - true in every
        // real play session (Bootstrap is the Play Mode Start Scene), but pressing Play with only
        // a sub-scene like Dungeon.unity open (e.g. Play Mode Start Scene having reset - it's a
        // purely in-memory Editor setting, see PlayModeStartSceneSetup.cs) skips Bootstrap
        // entirely. Without this guard that's an NRE on Player every single frame forever, not a
        // one-time error - a single clear warning is far more actionable than that.
        if (MapManager.Instance == null)
        {
            if (!warnedNoMapManager)
            {
                Debug.LogWarning($"[{GetType().Name}] No MapManager in the loaded scenes - Bootstrap.unity isn't loaded. " +
                    "Press Play with Bootstrap as part of your scene setup (or via Play Mode Start Scene) instead of a bare sub-scene.");
                warnedNoMapManager = true;
            }
            return;
        }

        int count = Physics2D.OverlapCircleNonAlloc(Player.transform.position, UnstuckCheckRadius, unstuckHitBuffer, ObstacleQuery.ObstacleLayerMask);
        bool stuck = false;
        var playerCollider = GetPlayerSolidCollider(Player);
        for (int i = 0; i < count; i++)
        {
            if (unstuckHitBuffer[i].isTrigger) continue;

            if (playerCollider == null)
            {
                // No real solid collider found on the player (shouldn't happen) - fall back to the
                // old any-overlap behavior rather than silently never rescuing.
                stuck = true;
                break;
            }

            var d = Physics2D.Distance(playerCollider, unstuckHitBuffer[i]);
            if (d.isValid && d.distance < -MinStuckPenetrationDepth) { stuck = true; break; }
        }

        var unitController = Player.GetComponent<UnitController>();

        if (stuck)
        {
            if (DebugUnstuck)
            {
                unstuckLogBuffer.AppendLine("[UnstuckDebug] t=" + Time.time.ToString("F3") + " frame=" + Time.frameCount +
                    " playerPos=" + ((Vector2)Player.transform.position).ToString("F4") +
                    " saveSpot=" + ((Vector2)unitController.saveSpot).ToString("F4") + " hitCount=" + count);
                unstuckLogPendingLines++;
                if (unstuckLogPendingLines >= UnstuckLogFlushThreshold) FlushUnstuckLog();
            }
            // saveSpot is kept up to date every frame the player is confirmed NOT stuck (below),
            // so any rescue snaps back to wherever they actually just were, not an unrelated stale
            // position (e.g. from the last ChargeAttack/ShadowImpact cast, or (0,0,0) if never).
            Player.transform.position = unitController.saveSpot;
            // A plain position snap doesn't touch the Rigidbody2D's own velocity or the physics
            // engine's cached contact state - MovementController.Move() drives the player via
            // rb.linearVelocity every frame (same pattern documented as bug #9's corner-wedging
            // deadlock in project_navmesh_2d_gotchas), so leftover velocity from the moment they
            // got stuck can carry them straight back into the same overlap on the very next
            // physics step, producing a visible snap-back/re-stuck wobble instead of a clean
            // rescue. Zeroing velocity plus an explicit SyncTransforms (so the physics engine
            // re-evaluates contacts against the NEW position immediately, not on some later step)
            // closes that window.
            var playerRb = Player.GetComponent<Rigidbody2D>();
            if (playerRb != null) playerRb.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }
        else
        {
            unitController.SetSaveSpot(Player.transform.position);
        }
    }

    public override void OnPortalUsed(PortalBehaviour portalUsed)
    {
        if (portalUsed.Target != null)
            GoToExit(portalUsed.Target);
    }

    // Activates whichever area the given target belongs to (found by walking up its hierarchy to
    // the owning LevelBehaviour) and places the player exactly at the target's position. Purely
    // reference-based - works for any same-scene portal topology, not just a linear sequence.
    protected void GoToExit(SpawnPoint target)
    {
        var targetArea = target.GetComponentInParent<LevelBehaviour>(true);
        if (targetArea == null)
        {
            Debug.LogError($"[{GetType().Name}] SpawnPoint '{target.name}' isn't nested under a LevelBehaviour - can't tell which area to activate.");
            return;
        }

        ActivateArea(targetArea.gameObject);
        Player.transform.position = target.transform.position;
    }

    // Resolves a cross-scene arrival by spawnPointId: activates whichever area the matching
    // SpawnPoint lives inside (found the same way GoToExit finds one), then places the player
    // there. This is what lets a scene with MULTIPLE distinct entrances (e.g. a
    // field entered from a hub city on one edge and from two other cities on its far edges) land
    // the player in the correct area instead of always defaulting to a single fixed one - every
    // entrance SpawnPoint must physically sit inside the area's own hierarchy for this to resolve.
    // A subclass with a single fixed entrance (see DungeonMap.level1Entry) may override this
    // instead of relying on it.
    public override void OnMapEntered(string spawnPointId)
    {
        var spawn = GetSpawnPoint(spawnPointId);
        if (spawn == null)
        {
            Debug.LogError($"[{GetType().Name}] No SpawnPoint with Id '{spawnPointId}' - can't resolve an entrance.");
            return;
        }

        var targetArea = spawn.GetComponentInParent<LevelBehaviour>(true);
        if (targetArea != null) ActivateArea(targetArea.gameObject);
        else RebuildNavMesh(); // spawn isn't nested under any area (e.g. a single-area map) - just rebake, position below.

        Player.transform.position = spawn.transform.position;
    }
}
