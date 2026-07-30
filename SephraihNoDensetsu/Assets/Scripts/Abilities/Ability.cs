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

    // "Teleport"-type movement / spell-placement check - shared flag (Obstacle.BlocksSpell) because
    // in practice they're the same question: if a spell can be conjured on the far side of an
    // obstacle, a teleport may as validly land there. Used by Teleport, ShadowImpact's per-hit
    // repositioning, and FireStorm's placement gate.
    protected bool SpellBlocked(Vector2 from, Vector2 to) => ObstacleQuery.BlocksSpell(from, to);

    // Excludes "Not Walkable" (the NavMesh area NavMeshObstacleSync tags every BlocksMovement=true
    // obstacle proxy with) from a path query - same exclusion EnemyController.Awake() applies to
    // its own NavMeshAgent.areaMask, duplicated here since abilities have no agent of their own to
    // carry it.
    static int walkableAreaMaskCache = -1;
    protected static int WalkableAreaMask
    {
        get
        {
            if (walkableAreaMaskCache == -1)
                walkableAreaMaskCache = NavMesh.AllAreas & ~(1 << NavMesh.GetAreaFromName("Not Walkable"));
            return walkableAreaMaskCache;
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