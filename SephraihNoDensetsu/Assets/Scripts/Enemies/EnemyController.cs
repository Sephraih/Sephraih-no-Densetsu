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

    // Single shared "how far can anything reasonably see/chase/shoot" reference distance (screen-
    // relative, computed once from the camera's FOV - see RangeSettings' own doc comment). Every
    // enemy type's actual perception ranges below are a PERCENTAGE of this one asset rather than
    // an independent absolute number, so retuning the camera or the game's overall "how far" feel
    // is one edit to the asset instead of hunting down every prefab that also encodes a distance.
    [SerializeField] protected RangeSettings rangeSettings;

    [Header("Perception (percentages)")]
    // Three nested detection tiers, checked closest-tier-first (see CanSense) - together they
    // form something closer to a real field of view than a single omnidirectional range: a bot
    // notices anything right next to it regardless of facing (detectionRange), notices most of its
    // surroundings except a small blind spot directly behind it at medium range (awarenessRange /
    // blindSpotDegrees), and only notices things ahead of it at long range (visionRange /
    // visionDegrees). visionRangePercent is a % of the shared rangeSettings.FieldOfView;
    // awarenessRangePercent/detectionRangePercent are each a % of THIS type's own resulting
    // visionRange, not of the global value directly - so "detection is roughly a quarter of my
    // vision" stays true regardless of how the global FieldOfView is tuned. All four percentages
    // are public so each enemy type can configure its own values on the prefab.
    public float visionRangePercent = 0.8f;
    public float visionDegrees = 120f; // total cone width, centered on facing - not a distance, not tied to rangeSettings
    public float awarenessRangePercent = 0.5f;
    public float blindSpotDegrees = 30f; // total blind arc directly behind, awareness tier only
    public float detectionRangePercent = 0.25f; // closest tier: full 360, no angle check at all

    // How far a Chase can drift from the target before giving up regardless of line of sight - a
    // separate concern from LostSightDelay below (which only governs actually losing sight, not
    // simply being led further and further away by a target that's still visible the whole time,
    // e.g. across open ground). Expressed as a % of THIS type's own visionRange and must stay
    // >= 1 (100%), or a bot could detect a target at the edge of its vision cone and immediately
    // re-Return the very next frame because that same distance already exceeds the leash - the
    // extra headroom above 100% is deliberate buffer against exactly that flicker.
    // GuardBehaviour additionally leashes to its own guard spot on top of this (see
    // guardMaxChaseRadius) - this generic one applies to every type, Guard included.
    public float maxChaseDistancePercent = 1.15f;

    // Effective world-unit ranges, computed once in Awake() from the percentages above - every
    // consumer (CanSense, FindNearestEnemy, UpdateState) reads these exactly as it read the old
    // plain absolute fields; only where the numbers come FROM changed. Read-only from outside so
    // nothing can silently reintroduce the old "overwritten every Start()" footgun these were
    // already fixed away from once (see git history).
    public float visionRange { get; private set; }
    public float awarenessRange { get; private set; }
    public float detectionRange { get; private set; }
    public float maxChaseDistance { get; private set; }

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

    // Set (to AggroGraceDuration) by HandleDamaged whenever a hit lands - protects a freshly-
    // aggroed target from being immediately un-acquired by the very next Update() if the attacker
    // happens to be outside the bot's own maxChaseDistance (e.g. hit by a long-range spell from
    // beyond it). Without this, HandleDamaged's state=Chase/target=attacker was reverted within the
    // same frame: FindNearestEnemy(isAcquiring: state != BotState.Chase) reads isAcquiring=false
    // once state is Chase, which scans with maxChaseDistance instead of visionRange - a hit from
    // beyond that radius made the very next re-scan find nothing, nulling `target`, which
    // UpdateState()'s own target==null branch then read as "lost the target entirely" and flipped
    // straight back to Return, all in one frame. Confirmed live as the actual mechanism behind "hit
    // a mob with a max-range fireball, it briefly flickers to Chase then goes right back to
    // Return, never actually closes the distance."
    private float aggroGraceTimer;
    private const float AggroGraceDuration = 3f;

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
        GetComponent<HealthController>().OnDamaged += HandleDamaged;

        // Cascading percentage -> world-units resolution, once, before any Update() reads these -
        // see the Perception fields' own doc comments for why each tier multiplies against the
        // PREVIOUS result rather than the raw global value directly.
        float fieldOfView = rangeSettings != null ? rangeSettings.FieldOfView : 0f;
        visionRange = fieldOfView * visionRangePercent;
        awarenessRange = visionRange * awarenessRangePercent;
        detectionRange = visionRange * detectionRangePercent;
        maxChaseDistance = visionRange * maxChaseDistancePercent;

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
            // "Spell Boundary" (boundary-tier obstacles, Obstacle.BlocksSpell) was missing from this
            // exclusion entirely - found live via direct triangulation after a wizard got physically
            // wedged against a boundary wall while pathing home: the triangle right at the stuck
            // position was tagged area="Spell Boundary", not "Not Walkable"/"Walkable". This area is
            // DELIBERATELY not hole-carved like "Not Walkable" is (see Ability.cs/
            // project_teleport_wall_landing.md - the whole point is letting Teleport reach a sealed
            // pocket behind an ordinary wall), so it keeps real, fully-connected, walkable-looking
            // geometry unless a query's areaMask specifically excludes it. Ability.cs's own
            // WalkableAreaMask (used by Teleport/ShadowImpact/BumpAttack's landing checks) already
            // excludes both "Not Walkable" and "Spell Boundary" - this NavMeshAgent mask was never
            // updated to match when "Spell Boundary" was introduced, so ordinary GetPathDirection
            // pathing has been treating every boundary-tier wall as perfectly normal floor ever
            // since: an agent could compute a "valid" path straight through/along a boundary wall's
            // real (never-carved) navmesh triangles, then get physically stopped dead by the wall's
            // very real Collider2D - reading as a stuck/wedged unit near any boundary wall its path
            // happened to route close to, not something specific to one corner. Deliberately does
            // NOT also exclude "Spell Barrier" - that tier is movement-permeable (BlocksMovement=
            // false, a real trigger collider), so ordinary walking should see straight through it,
            // matching WalkableAreaMask's own exclusion set exactly (not TeleportConnectivityMask's
            // wider one, which excludes both spell areas for a different purpose - see Ability.cs).
            agent.areaMask &= ~(1 << NavMesh.GetAreaFromName("Spell Boundary"));
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

    // Subscribed to HealthController.OnDamaged in Awake() - fires on every hit that actually
    // applies (not e.g. a hit on an already-dead unit). Being hit is its own unconditional alert:
    // unlike ordinary acquisition (CanSense/HasLineOfSight, gated by the vision cone), a mob
    // getting attacked from behind/off-screen/out of range should still immediately know exactly
    // who's attacking and go after them - real damage landing is a much stronger signal than
    // merely coming into view. Deliberately bypasses CanSense/HasLineOfSight entirely rather than
    // routing through the normal acquisition path.
    private void HandleDamaged(Transform attacker)
    {
        if (attacker == null || attacker == transform) return; // no valid attacker to chase (e.g. environmental damage)
        var status = attacker.GetComponent<StatusController>();
        if (status == null || status.teamID == teamID) return; // ignore friendly-fire/self-inflicted sources

        target = attacker;
        state = BotState.Chase;
        lostSightTimer = LostSightDelay;
        aggroGraceTimer = AggroGraceDuration;
        AlertNearby();
    }

    // Every subclass's own Update() should call this INSTEAD of calling FindNearestEnemy and
    // assigning `target` directly - it's the same lookup, just with the aggro-grace-timer
    // protection HandleDamaged relies on layered in front of it. While the grace timer is still
    // running and the current target is still a valid, active Transform, the target is left alone
    // rather than handed to FindNearestEnemy's own (isAcquiring-dependent, range-limited) scan -
    // see aggroGraceTimer's own doc comment for exactly which same-frame bug this prevents. Once
    // the timer runs out (or the target goes away on its own - death, deactivation), acquisition
    // reverts to the ordinary FindNearestEnemy scan, unchanged from before this existed.
    protected void AcquireTarget()
    {
        if (aggroGraceTimer > 0f)
        {
            aggroGraceTimer -= Time.deltaTime;
            if (target != null && target.gameObject.activeInHierarchy) return;
        }
        target = FindNearestEnemy(isAcquiring: state != BotState.Chase);
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
    // Deliberately asymmetric: ACQUIRING a target (Idle/Return -> Chase) is gated by the full
    // vision-cone/awareness/detection tiers (CanSense) plus line of sight - that's what "field of
    // view" means for noticing something. SUSTAINING an already-active chase is NOT re-gated by
    // the cone at all - only two things end a chase in progress: exceeding maxChaseDistance, or
    // the target's line of sight being genuinely obstructed (an obstacle, not just an angle) for
    // LostSightDelay seconds. A target that circles behind the bot mid-fight, or briefly ducks
    // past a corner, must not cause Return on its own - it's still "in the fight," just not
    // currently in the cone/visible, and the timer exists precisely to tolerate that.
    //
    // Idle and Return share the SAME re-acquisition branch (not two separate ones) - a mob heading
    // home should notice the player again exactly as readily as one standing still, not be
    // deliberately deaf until it physically arrives. Confirmed live as a real gap: a mob walking
    // back to its spawn/guard spot ignored the player re-entering range entirely, only ever
    // resuming the chase after fully arriving and settling into Idle first (state==Return hit
    // neither branch below, so nothing ever re-checked target while it was set). Since
    // FindNearestEnemy(isAcquiring: state != BotState.Chase) already re-scans with the full
    // CanSense/line-of-sight filtering during Return too (every subclass's own Update() already
    // passes isAcquiring=true whenever not actively chasing), `target` was already being correctly
    // re-acquired the whole way home - this method just never acted on it. Also doubles as the
    // hook a future "passive" mob (only chases once actually attacked) needs: whatever gates
    // acquisition for Idle naturally gates re-engagement during Return too, with no separate
    // Return-specific logic to keep in sync - "free to go unless tagged on the way back" falls out
    // of this for free once passive detection is layered onto CanSense/FindNearestEnemy itself.
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
            // Skipped while aggroGraceTimer is still running - a target acquired via a hit from
            // beyond maxChaseDistance (see HandleDamaged/AcquireTarget) needs real time to actually
            // close that distance, not an instant Return the moment this check would otherwise
            // fire. Ordinary Chase (acquired within normal range to begin with) is never affected,
            // since the timer is only ever set by HandleDamaged.
            if (dist > maxChaseDistance && aggroGraceTimer <= 0f)
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
        else if ((state == BotState.Idle || state == BotState.Return) && CanSense(target.position) && HasLineOfSight(target))
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

        if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
            return Vector2.zero;

        // Confirmed live (see project_navmesh_2d_gotchas.md, bug #19): agent.hasPath can get
        // permanently stuck false - with pathStatus still PathComplete and agent.path.corners fully
        // populated with a genuinely valid, direct route (verified independently via a raw
        // NavMesh.CalculatePath call between the same two points, which succeeded cleanly) - and no
        // amount of repeat SetDestination/ResetPath, or even a full disable+enable+Warp cycle, ever
        // recovers it. This wedges the bot in place forever, since needsRepath above keeps firing
        // (hasPath false) but the recomputed path comes back in the exact same stuck state every
        // time. The underlying path data is trustworthy even when hasPath lies, so when hasPath is
        // false but the path is otherwise valid, steer off agent.path.corners directly instead of
        // giving up - agent.steeringTarget itself isn't trustworthy in this state (it tracks
        // internal path-progress bookkeeping, the same bookkeeping that's stuck), so use the raw
        // corner just ahead of the agent's current position instead.
        Vector2 steerTarget;
        if (agent.hasPath)
        {
            steerTarget = NavMesh2DUtility.ToGame(agent.steeringTarget);
        }
        else
        {
            var corners = agent.path.corners;
            if (corners.Length == 0) return Vector2.zero;
            steerTarget = NavMesh2DUtility.ToGame(corners.Length > 1 ? corners[1] : corners[0]);
        }
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
