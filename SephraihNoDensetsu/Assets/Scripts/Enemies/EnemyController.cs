using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemyController : UnitController
{
    public enum BotState { Idle, Chase, Return }

    protected Transform target;
    protected BotState state = BotState.Idle;

    // Exposes this bot's already-resolved AI target to abilities invoked on it via
    // AbilityController.Invoke() (e.g. GuardBehaviour's ChargeAttack call) - those calls have no
    // target parameter of their own and would otherwise fall back to Ability.Use()'s default
    // UseMouse(), which only makes sense for the human player's actual mouse cursor.
    public Transform CurrentTarget => target;

    [Header("Perception")]
    public float detectionRange = 8f;

    [Header("Pathfinding")]
    public float repathInterval = 0.35f;
    const float RepathDistanceThreshold = 0.75f;

    // Layer(s) a local physics scan should consider when looking for a target/ally - Unit (other
    // enemies) + Player combined, so the same mask serves both FindNearestEnemy (hostile target =
    // the player) and AlertNearby (allies = other enemies); the actual team check is still done
    // via StatusController.teamID on each candidate, this is just an efficient physics pre-filter.
    [SerializeField] protected LayerMask targetLayerMask;

    private float lostSightTimer;
    private const float LostSightDelay = 3f;

    private NavMeshAgent agent;
    private float repathTimer;
    private Vector3 lastPathTargetPos;
    private Vector2 lastSteerDir;

    // Time.time this unit died, or -1 if still alive. A deactivated-not-destroyed GameObject keeps
    // this (and its position/state) for as long as the level it belongs to stays loaded - a future
    // respawn check can compare Time.time - deathTime against a delay and reactivate.
    private float deathTime = -1f;
    protected bool IsDead => deathTime >= 0f;

    protected override void Awake()
    {
        base.Awake();
        GetComponent<HealthController>().OnDeath += HandleDeath;
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.updateUpAxis = false;
            // Default agent settings carry a nonzero baseOffset (how far a 3D agent's pivot sits
            // above the navmesh surface) - meaningless for this rotated-2D-navmesh hack, but its
            // presence means agent.nextPosition's snapped height doesn't match the raw navmesh
            // height NavMesh2DUtility/NavMesh.SamplePosition report, breaking SetDestination in a
            // way that looks like "path complete, already arrived" right next to any obstacle.
            agent.baseOffset = 0f;
            // NavMeshAgent.radius on the prefab is a separately-serialized field that does NOT
            // auto-update when the project's Agent Type radius changes (ProjectSettings/
            // NavMeshAreas.asset) - it only gets its initial value when the agent type was first
            // assigned in the Inspector. This project's real character body (BoxCollider2D, every
            // enemy type) is 0.88x0.57, i.e. a 0.44 half-width. Setting the radius to just barely
            // cover that half-width (0.45) still isn't enough: at a convex obstacle corner, the
            // real physical BoxCollider2D can wedge against the obstacle's real Collider2D even
            // though the abstract navmesh path never "invalidly" clips it - box colliders don't
            // slide around a corner the way a circle does, and MovementController.Move() reapplies
            // raw velocity every frame with no collision-deflection logic, so a wedge can hold
            // indefinitely (confirmed live: mob's real BoxCollider2D found overlapping the wall's
            // real CompositeCollider2D at its exact stuck position, velocity nonzero, position
            // frozen for 10+ real seconds). The fix is routing paths with real margin to spare
            // rather than the bare minimum, so the physical body never gets close enough to a
            // corner to wedge. Fixed at the project level (agentRadius: 0.65 in NavMeshAreas.asset)
            // and enforced here too so a stale per-prefab value can't silently reintroduce the bug.
            agent.radius = 0.75f;
            // NavMeshAgent.areaMask defaults to NavMesh.AllAreas (every bit set), which includes
            // the "Not Walkable" area NavMeshObstacleSync tags obstacle proxies with - that area
            // still generates real, traversable mesh when baked (NavMeshModifier reassigns the
            // area a piece of geometry contributes as, it doesn't remove the geometry), so an
            // agent whose mask doesn't exclude it will path straight through obstacles instead of
            // around them. Exclude it explicitly here so the flag actually does what obstacle
            // authoring assumes it does.
            agent.areaMask &= ~(1 << NavMesh.GetAreaFromName("Not Walkable"));
        }
    }

    // Subscribed to HealthController.OnDeath in Awake() - fires exactly once, on the >0 -> <=0
    // health crossing. Deactivates rather than destroys so this unit's state (position, whatever
    // is still true about it) survives for as long as its level stays loaded - see deathTime.
    private void HandleDeath(Transform killer)
    {
        deathTime = Time.time;
        gameObject.SetActive(false);
        // Future respawn hook: something (here, or a dedicated component) checking
        // `IsDead && Time.time - deathTime > respawnDelay` would reset position/state/health and
        // SetActive(true) again - not implemented yet. A respawn should reset `state`/`target`
        // and reposition to this unit's spawn/guard spot first, since both are frozen at wherever
        // it died.
    }

    // Local physics scan for the nearest hostile-team unit within range, replacing the old
    // GameBehaviour.characterList-based ClosestEnemy/ClosestVisibleEnemy. Deactivated (dead or
    // otherwise disabled) units never show up here, since Physics2D only returns colliders on
    // active GameObjects - this also means a level's inactive units can never be picked up by
    // code running in a different, active level, for free.
    protected Transform FindNearestEnemy(float range, bool requireLineOfSight)
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, range, targetLayerMask);
        Transform best = null;
        float bestDist = range;
        foreach (var h in hits)
        {
            if (h.transform == transform) continue;
            var status = h.GetComponent<StatusController>();
            if (status == null || status.teamID == teamID) continue;
            if (requireLineOfSight && ObstacleQuery.BlocksSight(transform.position, h.transform.position)) continue;
            float d = Vector2.Distance(transform.position, h.transform.position);
            if (d < bestDist) { bestDist = d; best = h.transform; }
        }
        return best;
    }

    // Returns true when no sight-blocking obstacle sits between this bot and the target.
    protected bool HasLineOfSight(Transform t)
    {
        return !ObstacleQuery.BlocksSight(transform.position, t.position);
    }

    // Transitions between Idle / Chase / Return based on detection range and LOS.
    // Call once per Update before Move/Attack.
    protected void UpdateState()
    {
        if (target == null || target == transform)
        {
            if (state == BotState.Chase) state = BotState.Return;
            return;
        }

        float dist = Vector2.Distance(transform.position, target.position);
        bool canSee = dist <= detectionRange && HasLineOfSight(target);

        if (canSee)
        {
            lostSightTimer = LostSightDelay;
            if (state == BotState.Idle)
            {
                state = BotState.Chase;
                AlertNearby();
            }
        }
        else if (state == BotState.Chase)
        {
            lostSightTimer -= Time.deltaTime;
            if (lostSightTimer <= 0f)
                state = BotState.Return;
        }
    }

    // Notifies nearby allied bots that are still idle to start chasing. Local physics scan,
    // replacing the old characterList iteration - also fixes a pre-existing bug where the old
    // version never actually checked the woken unit was on the same team (despite "allied" in the
    // name), so it could wake a hostile idle unit too.
    protected void AlertNearby(float radius = 6f)
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, radius, targetLayerMask);
        foreach (var h in hits)
        {
            if (h.transform == transform) continue;
            var status = h.GetComponent<StatusController>();
            if (status == null || status.teamID != teamID) continue;
            EnemyController ally = h.GetComponent<EnemyController>();
            if (ally != null && ally.state == BotState.Idle)
            {
                ally.state = BotState.Chase;
            }
        }
    }

    public new void Aim()
    {
        if (target == null || target == transform) return;
        Vector3 dir = target.position - transform.position;
        dir.Normalize();
        attackingDirection.transform.localPosition = dir;
        if (movementDirection == Vector2.zero)
            GetComponent<MovementController>().LookAt(target.position);
    }

    // Returns a normalized direction toward the next NavMesh path corner en route to
    // targetWorldPos, throttling repath queries to at most once per repathInterval seconds OR
    // whenever the target has moved more than RepathDistanceThreshold since the last query.
    // Falls back to a straight-line direction if there's no NavMeshAgent / it's off-mesh (keeps
    // pre-pathfinding behavior for any prefab not carrying a NavMeshAgent). Returns Vector2.zero
    // if the current path is confirmed invalid (target unreachable) - callers should treat that
    // as "hold position" rather than pushing into whatever is blocking the way.
    protected Vector2 GetPathDirection(Vector3 targetWorldPos)
    {
        if (agent == null || !agent.isOnNavMesh)
            return ((Vector2)(targetWorldPos - transform.position)).normalized;

        agent.nextPosition = NavMesh2DUtility.ToNavMesh(transform.position);

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f || Vector3.Distance(targetWorldPos, lastPathTargetPos) > RepathDistanceThreshold)
        {
            agent.SetDestination(NavMesh2DUtility.ToNavMesh(targetWorldPos));
            lastPathTargetPos = targetWorldPos;
            repathTimer = repathInterval;
        }

        if (agent.pathPending) return lastSteerDir;
        if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            return Vector2.zero;

        Vector2 steerTarget = NavMesh2DUtility.ToGame(agent.steeringTarget);
        Vector2 dir = steerTarget - (Vector2)transform.position;
        lastSteerDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.zero;
        return lastSteerDir;
    }

    // Same idea as GetPathDirection but for fleeing a threat: samples a valid NavMesh point in
    // the raw "away from threatPos" direction, then paths toward that point (so retreating bots
    // route around obstacles instead of backing straight into a wall). Falls back to the raw
    // away-vector if no valid NavMesh point is found nearby.
    protected Vector2 GetFleeDirection(Vector3 threatPos, float fleeDistance)
    {
        Vector2 away = ((Vector2)(transform.position - threatPos)).normalized;
        if (agent == null || !agent.isOnNavMesh) return away;

        Vector3 candidate = transform.position + (Vector3)away * fleeDistance;
        if (NavMesh.SamplePosition(NavMesh2DUtility.ToNavMesh(candidate), out var hit, fleeDistance, NavMesh.AllAreas))
        {
            Vector2 gamePos = NavMesh2DUtility.ToGame(hit.position);
            return GetPathDirection(new Vector3(gamePos.x, gamePos.y, 0f));
        }
        return away;
    }
}
