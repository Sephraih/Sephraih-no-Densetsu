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
    static readonly Collider2D[] unstuckHitBuffer = new Collider2D[8];
    static bool warnedNoMapManager = false;

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

        int count = Physics2D.OverlapPointNonAlloc(Player.transform.position, unstuckHitBuffer, ObstacleQuery.ObstacleLayerMask);
        bool stuck = false;
        for (int i = 0; i < count; i++)
        {
            if (!unstuckHitBuffer[i].isTrigger) { stuck = true; break; }
        }

        var unitController = Player.GetComponent<UnitController>();

        if (stuck)
        {
            // saveSpot is kept up to date every frame the player is confirmed NOT stuck (below),
            // so any rescue snaps back to wherever they actually just were, not an unrelated stale
            // position (e.g. from the last ChargeAttack/ShadowImpact cast, or (0,0,0) if never).
            Player.transform.position = unitController.saveSpot;
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
