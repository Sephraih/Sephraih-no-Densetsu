using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class Ability : MonoBehaviour
{

    public float acd; // ability cd
    protected float cd = 0; //remaining cd
    public float range;
    

    
    protected Transform user;
    protected Transform attackPos;

    // Layer(s) to consider when resolving a mouse-targeted ability - Unit only, same convention as
    // BasicAttack/MultiSlash's own (separately-declared) `units` field. Named differently from
    // theirs since they redeclare their own field of that name rather than inheriting one - a
    // same-named field here would collide with theirs (Unity doesn't support two same-named
    // serialized fields across a class/subclass pair). Used by PreciseMouseTarget().
    [SerializeField] protected LayerMask mouseTargetUnits;
    [SerializeField] protected float mouseTargetRadius = 0.5f;

    public virtual void Use() {
        UseMouse();
    }

    public virtual void UseTarget(Transform target) { }
    public virtual void UseMouse() { }

    // Shared dispatch for target-based abilities (ChargeAttack, ShadowImpact) invoked via plain
    // Invoke() rather than InvokeMouse()/UseAbility(). AbilityController.Invoke(spellid, user) -
    // the path every AI caller uses (e.g. GuardBehaviour's charge/ranged attacks) - passes no
    // target of its own, so Use()'s default falls through to UseMouse(), which only makes sense
    // for the human player's actual mouse cursor. When the caller is an EnemyController that
    // already has its own resolved AI target, use that instead; otherwise (the player) fall back
    // to the normal mouse-based resolution.
    protected void UseAITargetOrMouse()
    {
        var enemyController = user.GetComponent<EnemyController>();
        if (enemyController != null && enemyController.CurrentTarget != null)
            UseTarget(enemyController.CurrentTarget);
        else
            UseMouse();
    }

    // Precise mouse targeting: only selects a target if it's actually within mouseTargetRadius of
    // the cursor's world position, unlike the old GameBehaviour.ClosestEnemyToLocation which would
    // always pick the closest enemy on the whole map regardless of how far off the cursor was.
    // Returns null when nothing hostile is under the cursor - callers must guard against that.
    protected Transform PreciseMouseTarget()
    {
        Vector2 pos = MousePosition();
        var hits = Physics2D.OverlapCircleAll(pos, mouseTargetRadius, mouseTargetUnits);
        Transform best = null;
        float bestDist = float.MaxValue;
        int myTeam = user.GetComponent<StatusController>().teamID;
        foreach (var h in hits)
        {
            var status = h.GetComponent<StatusController>();
            if (status == null || status.teamID == myTeam) continue;
            float d = Vector2.Distance(pos, h.transform.position);
            if (d < bestDist) { bestDist = d; best = h.transform; }
        }
        return best;
    }

    // "Walk"-type movement check - cheap single-raycast test against the same flag ordinary
    // Collider2D/Rigidbody2D movement already respects (Obstacle.BlocksMovement). For abilities
    // that just need a yes/no answer over a short straight hop, not full pathing - use
    // TryGetWalkPath instead for anything that needs to actually navigate around an obstacle (see
    // ChargeAttack).
    protected bool WalkBlocked(Vector2 from, Vector2 to) => ObstacleQuery.BlocksWalk(from, to);

    // Straight-line placement gate (Obstacle.BlocksSpell) for spells that conjure something AT a
    // point rather than moving the caster there - FireStorm's placement check is the only user now.
    // Teleport/ShadowImpact used to also gate on this, but a straight-line raycast and "is this
    // point reachable/reachable-by-walking" (TryFindWalkableLanding/TryFindReachableLanding) are
    // different questions - the raycast could block a destination that was perfectly legitimate by
    // NavMesh's own account whenever the straight line merely grazed a wall's corner (e.g. a bend in
    // the map boundary), and NavMesh's own floor generation already guarantees nothing walkable
    // exists beyond the map's true edge, so the raycast wasn't even load-bearing for that case. See
    // Teleport.cs's comment for the full reasoning.
    protected bool SpellBlocked(Vector2 from, Vector2 to) => ObstacleQuery.BlocksSpell(from, to);

    // Default search radius for snapping a candidate teleport/reposition point onto the navmesh -
    // see TryFindWalkableLanding/TryFindReachableLanding below. Generous enough to reliably find a
    // nearby walkable point across a typical wall's thickness in this project (walls are usually a
    // single tile) without searching so far that a snap ends up somewhere unrelated to where the
    // ability was actually aimed.
    protected const float DefaultLandingSearchRadius = 3f;

    // Finds the nearest point ON the navmesh to `point`, within `searchRadius` - purely spatial,
    // makes no attempt to verify that point is actually reachable (connected by a walkable route)
    // from anywhere else. This is deliberately how a "caster teleport" should behave: it can
    // relocate the user into a walkable pocket that's otherwise fully sealed off (e.g. the top of a
    // tower surrounded by walls) - NavMesh.SamplePosition only ever answers "what's nearby," never
    // "what's connected to what," and for this kind of ability that IS the intended behavior, not a
    // gap to close (a teleport that could only ever land somewhere you could otherwise walk to would
    // defeat half the point of having one). Use TryFindReachableLanding instead for any ability that
    // must never strand the user somewhere their own feet couldn't otherwise get them out of.
    //
    // The one thing this DOES still check: `from` must be able to reach the landing point without
    // crossing a "Spell Boundary"-tagged obstacle (Obstacle.BlocksSpell) - see the two-check
    // reasoning below. This is a real connectivity/area-tag query, not a straight-line raycast, so
    // it can't be fooled by a destination merely sitting near a wall's corner the way the old
    // SpellBlocked raycast was (see Teleport.cs's comment for that history).
    protected bool TryFindWalkableLanding(Vector2 from, Vector2 point, float searchRadius, out Vector2 landing)
    {
        landing = point;
        if (!NavMesh.SamplePosition(NavMesh2DUtility.ToNavMesh(point), out var hit, searchRadius, WalkableAreaMask))
            return false;

        // Two connectivity checks, not one, to tell "blocked by an ordinary wall" (allowed - the
        // whole point of this method is to ignore that, see above) apart from "blocked specifically
        // by a Spell Boundary obstacle" (rejected). This is NOT redundant with a single mask-
        // restricted check: confirmed empirically via NavMesh.CalculateTriangulation() that Unity's
        // own BUILT-IN "Not Walkable" area produces ZERO real triangles anywhere in the bake (Unity
        // special-cases its own default area name - contrary to an earlier, apparently mistaken
        // assumption logged elsewhere in this project's memory) - so literally no mask can ever
        // route a CalculatePath across one; a single "is there a path" check would incorrectly
        // reject every ordinary sealed pocket too, not just Spell Boundary ones. "Spell Boundary" is
        // a CUSTOM area, which Unity does NOT special-case - it keeps real, walkable-shaped geometry,
        // just tagged - so a path CAN cross it unless that specific area is excluded from the query
        // mask. That asymmetry is what makes the two-check comparison meaningful: only reject when
        // removing Spell Boundary specifically from the mask is what breaks an otherwise-complete
        // route.
        var permissivePath = new NavMeshPath();
        bool reachableIgnoringEverything = NavMesh.CalculatePath(NavMesh2DUtility.ToNavMesh(from), hit.position, NavMesh.AllAreas, permissivePath)
            && permissivePath.status == NavMeshPathStatus.PathComplete;

        if (reachableIgnoringEverything)
        {
            var restrictedPath = new NavMeshPath();
            bool reachableRespectingSpellBoundary = NavMesh.CalculatePath(NavMesh2DUtility.ToNavMesh(from), hit.position, TeleportConnectivityMask, restrictedPath)
                && restrictedPath.status == NavMeshPathStatus.PathComplete;
            if (!reachableRespectingSpellBoundary)
                return false; // reachable with Spell Boundary treated as passable, not without it - that IS the blocker
        }
        // Else: unreachable even with Spell Boundary geometry treated as passable - blocked by an
        // ordinary wall/true hole instead, which isn't this check's concern (allowed, per the
        // sealed-tower design above).

        landing = NavMesh2DUtility.ToGame(hit.position);
        return true;
    }

    // Same nearest-point search as TryFindWalkableLanding, but additionally requires the found point
    // be genuinely reachable FROM `from` by a real walkable path (NavMeshPathStatus.PathComplete),
    // not just spatially nearby - the same connectivity check TryGetWalkPath/ChargeAttack already
    // relies on to never walk a charge onto or through a wall. This is deliberately how a
    // "melee-ish" teleport/reposition should behave: it rejects a point that's close to the aim but
    // topologically sealed off (the tower again, but this time landing there would strand the
    // attacker with no way back down) instead of snapping to it the way TryFindWalkableLanding
    // would. Callers should treat a `false` return the same as "no valid reposition" - skip it
    // rather than falling back to the raw, unvalidated `point`.
    protected bool TryFindReachableLanding(Vector2 from, Vector2 point, float searchRadius, out Vector2 landing)
    {
        landing = point;
        if (!NavMesh.SamplePosition(NavMesh2DUtility.ToNavMesh(point), out var hit, searchRadius, WalkableAreaMask))
            return false;

        var path = new NavMeshPath();
        bool ok = NavMesh.CalculatePath(NavMesh2DUtility.ToNavMesh(from), hit.position, WalkableAreaMask, path);
        if (!ok || path.status != NavMeshPathStatus.PathComplete)
            return false;

        landing = NavMesh2DUtility.ToGame(hit.position);
        return true;
    }

    // Excludes "Not Walkable" (BlocksMovement=true, BlocksSpell=false obstacles) AND "Spell Boundary"
    // (BlocksMovement=true, BlocksSpell=true obstacles) - both physically block movement, so both
    // must be excluded for any ordinary movement/landing-on-the-ground purpose (this is the mask
    // TryGetWalkPath/ChargeAttack uses, and the one TryFindWalkableLanding/TryFindReachableLanding
    // use to find a real ground point to snap onto). Deliberately does NOT exclude "Spell Barrier"
    // (BlocksMovement=false, BlocksSpell=true obstacles, e.g. a spellBarrier tile) - those don't
    // physically block movement (their real Collider2D is a trigger), so ordinary pathing should see
    // straight through them same as physics does; only TeleportConnectivityMask cares about that
    // area. Same "Not Walkable" exclusion EnemyController.Awake() applies to its own
    // NavMeshAgent.areaMask, duplicated here since abilities have no agent of their own to carry it.
    static int walkableAreaMaskCache = -1;
    protected static int WalkableAreaMask
    {
        get
        {
            if (walkableAreaMaskCache == -1)
                walkableAreaMaskCache = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Not Walkable")) & ~(1 << NavMesh.GetAreaFromName("Spell Boundary"));
            return walkableAreaMaskCache;
        }
    }

    // Excludes "Spell Boundary" AND "Spell Barrier" - deliberately does NOT exclude "Not Walkable",
    // unlike WalkableAreaMask above. Unity's built-in "Not Walkable" area is a genuine hole (zero
    // real triangles - confirmed via NavMesh.CalculateTriangulation(), see the correction in
    // project-navmesh-2d-gotchas memory bug #5), so by NOT excluding it here, a CalculatePath query
    // against this mask can never route through it anyway - which is exactly what preserves
    // TryFindWalkableLanding's deliberate "can reach a sealed-by-an-ordinary-wall pocket" behavior.
    // "Spell Boundary"/"Spell Barrier" are CUSTOM areas, which Unity does NOT special-case into
    // holes - they keep real, walkable-shaped geometry, so excluding them here genuinely blocks a
    // path that would otherwise cross them. Both are excluded (not just one) because either
    // combination of Obstacle.BlocksMovement + BlocksSpell=true should reject Teleport equally - see
    // NavMeshObstacleSync.Sync()'s area-tagging comment for which combination gets which area.
    static int teleportConnectivityMaskCache = -1;
    protected static int TeleportConnectivityMask
    {
        get
        {
            if (teleportConnectivityMaskCache == -1)
                teleportConnectivityMaskCache = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Spell Boundary")) & ~(1 << NavMesh.GetAreaFromName("Spell Barrier"));
            return teleportConnectivityMaskCache;
        }
    }

    // Computes a NavMesh path from `from` to `to` (via the static NavMesh.CalculatePath API, which
    // - unlike EnemyController's GetPathDirection - needs no NavMeshAgent component, so this works
    // identically for the player, who has none, and AI casters alike), trimmed so it stops
    // `trimTail` units short of `to` measured ALONG the path rather than as a straight line (e.g.
    // so a melee charge stops just outside melee range without walking onto the target). Returns
    // false (and pathDistance = float.MaxValue) if no path exists at all - callers should treat
    // "unreachable" and "reachable but too far" identically. pathDistance is the TRUE walking
    // distance (sum of path corner segments) measured on the UNTRIMMED path, for range checks that
    // must reflect how far a character actually has to walk, not straight-line distance to target.
    protected bool TryGetWalkPath(Vector3 from, Vector3 to, float trimTail, out List<Vector2> waypoints, out float pathDistance)
    {
        waypoints = null;
        pathDistance = float.MaxValue;

        var path = new NavMeshPath();
        bool ok = NavMesh.CalculatePath(NavMesh2DUtility.ToNavMesh(from), NavMesh2DUtility.ToNavMesh(to), WalkableAreaMask, path);
        if (!ok || path.status == NavMeshPathStatus.PathInvalid || path.corners.Length < 2)
            return false;

        var corners = new List<Vector2>();
        foreach (var c in path.corners) corners.Add(NavMesh2DUtility.ToGame(c));

        float total = 0f;
        for (int i = 1; i < corners.Count; i++) total += Vector2.Distance(corners[i - 1], corners[i]);
        pathDistance = total;

        float remaining = trimTail;
        while (corners.Count > 1 && remaining > 0f)
        {
            float segLen = Vector2.Distance(corners[corners.Count - 1], corners[corners.Count - 2]);
            if (segLen <= remaining) { remaining -= segLen; corners.RemoveAt(corners.Count - 1); }
            else
            {
                Vector2 dir = (corners[corners.Count - 2] - corners[corners.Count - 1]).normalized;
                corners[corners.Count - 1] = corners[corners.Count - 1] + dir * remaining;
                remaining = 0f;
            }
        }

        waypoints = corners;
        return true;
    }

    public void InvokeMouse(Transform user)
    {
        this.user = user;
        this.attackPos = user.GetComponent<UnitController>().attackingDirection.transform;
        UseMouse();
    }

    public void Invoke(Transform user)
    {
        this.user = user;
        this.attackPos = user.GetComponent<UnitController>().attackingDirection.transform;
        Use();
    }


    public Vector2 MousePosition()
    {
        return Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }
    void Update()
    {
        if (cd >= 0)
        {
            cd -= Time.deltaTime; //decrease cooldown
        }
    }

}