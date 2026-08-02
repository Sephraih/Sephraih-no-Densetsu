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
    // Three nested detection tiers, checked closest-tier-first (see CanSense) - together they
    // replace a single omnidirectional detectionRange with something closer to a real field of
    // view: a bot notices anything right next to it regardless of facing (detectionRange), notices
    // most of its surroundings except a small blind spot directly behind it at medium range
    // (awarenessRange / blindSpotDegrees), and only notices things ahead of it at long range
    // (visionRange / visionDegrees). All five are public so each enemy type can configure its own
    // values - see GuardBehaviour/WizardBehaviour's Start() for per-type overrides; Mob/Dummy use
    // these base defaults (matching Mob's old un-overridden detectionRange of 8).
    public float visionRange = 8f;
    public float visionDegrees = 120f; // total cone width, centered on facing
    public float awarenessRange = 4f;
    public float blindSpotDegrees = 30f; // total blind arc directly behind, awareness tier only
    public float detectionRange = 2f; // closest tier: full 360, no angle check at all

    // How far a Chase can drift from the target before giving up regardless of line of sight - a
    // separate concern from LostSightDelay below (which only governs actually losing sight, not
    // simply being led further and further away by a target that's still visible the whole time,
    // e.g. across open ground). Must stay >= visionRange for a given type, or a bot could detect a
    // target at the edge of its vision cone and immediately re-Return the very next frame because
    // that same distance already exceeds the leash - see WizardBehaviour.Start()'s override.
    // GuardBehaviour additionally leashes to its own guard spot on top of this (see
    // guardMaxChaseRadius) - this generic one applies to every type, Guard included.
    public float maxChaseDistance = 15f;

    [Header("Pathfinding")]
    public float repathInterval = 0.35f;
    const float RepathDistanceThreshold = 0.75f;

    [Header("Collision Avoidance")]
    // Radius around this bot to scan for other units to steer away from - see
    // DeflectAroundOtherUnits. Two units with opposing goals (e.g. one returning to spawn while
    // another keeps chasing) can otherwise walk straight into each other and wedge indefinitely:
    // MovementController.Move() reapplies raw velocity every single frame with no collision-
    // awareness of its own, and box colliders don't slide around each other the way circles do
    // (the same underlying issue that caused the earlier enemy-vs-static-obstacle wedging bug -
    // see NavMeshAreas.asset's agentRadius comment). Omnidirectional (Physics2D.OverlapCircleAll)
    // rather than a forward-only probe, since a unit approaching from the side or one already
    // overlapping needs to be caught too, not just whatever happens to be directly ahead.
    public float separationRadius = 1.2f;
    // How strongly to push away from nearby units, blended into the desired movement direction.
    // Higher values let the push dominate over the original direction - tune per feel.
    public float separationStrength = 1.2f;

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
    // Set by the most recent GetPathDirection call - true when the agent's current path can't
    // actually reach its destination (NavMeshPathStatus.PathPartial/PathInvalid), as opposed to
    // just not having reached it yet. UpdateState reads this alongside HasLineOfSight so a target
    // behind a sight-permeable-but-unreachable obstacle still eventually triggers Return, even
    // though it's never technically "out of sight". One frame stale (Move runs after UpdateState
    // each Update()), which is negligible against LostSightDelay's 3s window.
    private bool pathUnreachable;

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
    //
    // isAcquiring distinguishes "looking for a NEW target" (Idle) from "still tracking the one I'm
    // already chasing" (Chase): only acquiring a target is gated by the vision-cone/awareness/
    // detection tiers (CanSense) and line of sight - the field-of-view flags only govern whether a
    // bot NOTICES something in the first place, per the user's design intent. Once already
    // chasing, the scan widens out to maxChaseDistance and drops both the cone and LOS checks
    // entirely, so a target that steps behind the bot mid-fight (outside the cone) or briefly
    // ducks a corner doesn't cause this scan to lose it and null out `target` - UpdateState()'s
    // own distance-leash and line-of-sight-over-time checks are what actually end a chase.
    protected Transform FindNearestEnemy(bool isAcquiring)
    {
        float scanRadius = isAcquiring ? visionRange : maxChaseDistance;
        var hits = Physics2D.OverlapCircleAll(transform.position, scanRadius, targetLayerMask);
        Transform best = null;
        float bestDist = scanRadius;
        foreach (var h in hits)
        {
            if (h.transform == transform) continue;
            var status = h.GetComponent<StatusController>();
            if (status == null || status.teamID == teamID) continue;
            if (isAcquiring && !CanSense(h.transform.position)) continue;
            if (isAcquiring && ObstacleQuery.BlocksSight(transform.position, h.transform.position)) continue;
            float d = Vector2.Distance(transform.position, h.transform.position);
            if (d < bestDist) { bestDist = d; best = h.transform; }
        }
        return best;
    }

    // Returns true if targetPos falls within any of the three perception tiers above, relative to
    // this bot's current position and facing (read from its own MovementController - the same
    // moveX/moveY signal that already drives its walk animation and attack direction, so the cone
    // always matches whichever way the bot is actually shown facing on screen, even while idle).
    protected bool CanSense(Vector2 targetPos)
    {
        Vector2 toTarget = targetPos - (Vector2)transform.position;
        float dist = toTarget.magnitude;

        if (dist <= detectionRange) return true; // closest tier: 360, no angle check

        // Vector2.Angle returns an unsigned 0-180 magnitude regardless of which side the target is
        // on - exactly what a symmetric forward-cone/rear-blind-spot check needs, no sign-handling.
        // A zero facing vector (shouldn't normally happen - Aniwalk always holds a last direction)
        // falls back to "no angle information" by treating everything as directly ahead (angle 0),
        // so a broken/uninitialized facing signal fails open (still detects) rather than closed.
        Vector2 facing = GetComponent<MovementController>().GetFacingVector();
        float angle = facing.sqrMagnitude > 0.0001f ? Vector2.Angle(facing, toTarget) : 0f;

        if (dist <= awarenessRange && angle <= (180f - blindSpotDegrees / 2f)) return true;
        if (dist <= visionRange && angle <= visionDegrees / 2f) return true;
        return false;
    }

    // Returns true when no sight-blocking obstacle sits between this bot and the target.
    protected bool HasLineOfSight(Transform t)
    {
        return !ObstacleQuery.BlocksSight(transform.position, t.position);
    }

    // Transitions between Idle / Chase / Return. Call once per Update before Move/Attack.
    //
    // Deliberately asymmetric: ACQUIRING a target (Idle -> Chase) is gated by the full
    // vision-cone/awareness/detection tiers (CanSense) plus line of sight - that's what "field of
    // view" means for noticing something. SUSTAINING an already-active chase is NOT re-gated by
    // the cone at all - only two things end a chase in progress: exceeding maxChaseDistance, or
    // the target's line of sight being genuinely obstructed (an obstacle, not just an angle) for
    // LostSightDelay seconds. A target that circles behind the bot mid-fight, or briefly ducks
    // past a corner, must not cause Return on its own - it's still "in the fight," just not
    // currently in the cone/visible, and the timer exists precisely to tolerate that.
    protected void UpdateState()
    {
        if (target == null || target == transform)
        {
            if (state == BotState.Chase) state = BotState.Return;
            return;
        }

        float dist = Vector2.Distance(transform.position, target.position);

        if (state == BotState.Chase)
        {
            if (dist > maxChaseDistance)
            {
                state = BotState.Return;
                return;
            }

            if (HasLineOfSight(target) && !pathUnreachable)
            {
                lostSightTimer = LostSightDelay;
            }
            else
            {
                lostSightTimer -= Time.deltaTime;
                if (lostSightTimer <= 0f)
                    state = BotState.Return;
            }
        }
        else if (state == BotState.Idle && CanSense(target.position) && HasLineOfSight(target))
        {
            lostSightTimer = LostSightDelay;
            state = BotState.Chase;
            AlertNearby();
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
        // Flatten to the XY plane before writing into attackingDirection's local position - this is
        // a 2D game and attackingDirection's Z must stay 0. A stray Z component here is especially
        // dangerous for Wizard: Teleport.cs reads attackPos.position (attackingDirection's world
        // position) to compute its jump vector, so any non-zero Z gets baked into the wizard's own
        // world Z on the next teleport; that shifted Z then feeds back into THIS Vector3 subtraction
        // next frame, drifting further every teleport cycle with nothing to ever bring it back to 0
        // - a runaway feedback loop that silently pushes the wizard's Z far from the camera/sprite
        // sort plane (looks like it "disappears" while still fully functional, since none of the
        // AI/combat logic reads or depends on Z at all).
        Vector3 dir = target.position - transform.position;
        dir.z = 0f;
        dir.Normalize();
        attackingDirection.transform.localPosition = dir;
        if (movementDirection == Vector2.zero)
            GetComponent<MovementController>().LookAt(target.position);
    }

    // Returns a normalized direction toward the next NavMesh path corner en route to
    // targetWorldPos. repathInterval THROTTLES how often a repath can happen (at most once per
    // interval) but does NOT by itself force one - a repath only actually fires once the target
    // has moved more than RepathDistanceThreshold since the last query, or there's no path yet.
    // This matters near a straight wall: two routes around opposite ends can be nearly identical
    // length, and recomputing on a bare timer (even with a perfectly stationary target) can pick
    // a different one each time purely from float noise, which reads as the bot rapidly
    // reversing direction ("jiggling") instead of committing to one side. Requiring actual target
    // movement before recommitting to a new path keeps an already-chosen route stable.
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
        bool needsRepath = !agent.hasPath || Vector3.Distance(targetWorldPos, lastPathTargetPos) > RepathDistanceThreshold;
        if (repathTimer <= 0f)
        {
            if (needsRepath)
            {
                agent.SetDestination(NavMesh2DUtility.ToNavMesh(targetWorldPos));
                lastPathTargetPos = targetWorldPos;
            }
            repathTimer = repathInterval;
        }

        if (agent.pathPending) return lastSteerDir;

        // PathPartial means the destination itself is unreachable (e.g. it's on the far side of a
        // "low wall" tier obstacle - BlocksSight=false so HasLineOfSight stays true, but
        // BlocksMovement=true with no navmesh-connected route around it from here) - the agent
        // instead walks toward the closest reachable point and then just sits there once arrived,
        // which on its own would leave UpdateState's HasLineOfSight-only check convinced the chase
        // is still going fine forever, since sight was never actually lost. See pathUnreachable's
        // use in UpdateState for the other half of this fix.
        // Deliberately NOT gated on agent.hasPath: Unity clears hasPath back to false once the
        // agent arrives at wherever its current path ends - including a partial path's stand-in
        // endpoint - which is exactly the moment this needs to still read true. pathStatus itself
        // stays PathPartial/PathInvalid regardless of arrival, so it alone is the reliable signal.
        pathUnreachable = agent.pathStatus != NavMeshPathStatus.PathComplete;

        if (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid)
            return Vector2.zero;

        Vector2 steerTarget = NavMesh2DUtility.ToGame(agent.steeringTarget);
        Vector2 dir = steerTarget - (Vector2)transform.position;
        lastSteerDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.zero;
        return lastSteerDir;
    }

    // Blends a "push away from nearby units" force into a computed movement direction - see the
    // Collision Avoidance fields above for why this exists. Replaces an earlier single forward
    // raycast probe: that only ever caught a unit directly ahead, so one approaching from the side
    // or one already overlapping went undetected and could still wedge. This instead scans every
    // unit within separationRadius (omnidirectional) and sums a per-neighbor push vector weighted
    // by proximity - closer neighbors push harder - so the result reflects the whole local cluster
    // around this bot, not just whatever's directly in front of it. Deliberately does NOT push away
    // from `target` itself, since closing distance onto a solid target is the entire point of
    // Chase/melee-approach; only incidental bystanders get pushed away from. Wrap the result of a
    // GetPathDirection call with this wherever two bots' independent goals could plausibly converge
    // on the same point (Chase-approach and Return-approach both qualify; a flee direction moving
    // AWAY from something is far less likely to head-on collide and is left unwrapped).
    protected Vector2 DeflectAroundOtherUnits(Vector2 desiredDirection)
    {
        if (desiredDirection.sqrMagnitude < 0.0001f) return desiredDirection;

        Vector2 separation = Vector2.zero;
        var hits = Physics2D.OverlapCircleAll(transform.position, separationRadius, targetLayerMask);
        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform == target) continue;
            if (hit.GetComponent<StatusController>() == null) continue; // not a character (targetLayerMask can carry other Unit-layer geometry)

            Vector2 offset = (Vector2)transform.position - (Vector2)hit.transform.position;
            float dist = offset.magnitude;
            // Exactly overlapping (dist == 0) has no defined push direction - break the tie
            // deterministically from this bot's own instance ID so two overlapping bots don't both
            // compute the identical push and stay stuck exactly on top of each other.
            Vector2 pushDir = dist > 0.0001f
                ? offset / dist
                : new Vector2(Mathf.Cos(GetInstanceID()), Mathf.Sin(GetInstanceID()));
            float weight = 1f - Mathf.Clamp01(dist / separationRadius); // closer neighbor = stronger push
            separation += pushDir * weight;
        }

        if (separation.sqrMagnitude < 0.0001f) return desiredDirection;

        return (desiredDirection + separation * separationStrength).normalized;
    }

    // Call after anything that relocates this unit's transform OUTSIDE of normal GetPathDirection-
    // driven walking (currently: WizardBehaviour's flee-Teleport). Teleport.cs writes
    // transform.position directly with no NavMeshAgent awareness at all - GetPathDirection's own
    // per-call agent.nextPosition assignment (line below) is meant for small incremental
    // corrections, not a big instantaneous jump, and doesn't reliably clear the agent's internal
    // path state. Left unsynced, a later SetDestination can come back permanently
    // NavMeshPathStatus.PathInvalid even though the destination is genuinely reachable, and
    // GetPathDirection's documented "hold position" fallback (return Vector2.zero) then holds
    // forever - the unit freezes, unable to ever get close enough to its Return target to flip
    // back to Idle. agent.Warp() (unlike plain nextPosition) properly resets the agent's path
    // state alongside the position, and running it right after a teleport avoids paying that
    // reset cost on every ordinary GetPathDirection call.
    //
    // agent.Warp() also moves this unit's actual Transform to match the position it's given -
    // that's the whole point when warping to a genuinely new spot, but NavMesh2DUtility.ToNavMesh
    // exists specifically to INJECT the baked navmesh's Z offset (~-1.1 in this project, an
    // artifact of the rotated bake volume - see its own doc comment) into a query, for NavMesh API
    // calls that expect navmesh-space coordinates. Warp() has no such filtering: it takes that
    // offset Z at face value and plants it directly in transform.position.z - permanently, since
    // nothing else in the game ever touches Z afterward to correct it. This was the actual cause
    // of Wizards visually vanishing after teleporting away from the player: still fully
    // functional (chasing, casting), just physically sitting ~1.1 units off the sprite/camera's Z
    // plane where nothing renders it. Restore Z to 0 immediately after Warp - every other
    // game-space position in this project is implicitly 2D (see NavMesh2DUtility.ToGame, which
    // only ever returns a Vector2) and Warp is the one place that convention wasn't upheld.
    protected void ResyncNavMeshAgent()
    {
        if (agent != null && agent.isOnNavMesh)
        {
            agent.Warp(NavMesh2DUtility.ToNavMesh(transform.position));
            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
        }
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
