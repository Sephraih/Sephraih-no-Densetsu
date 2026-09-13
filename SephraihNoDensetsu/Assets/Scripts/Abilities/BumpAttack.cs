using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

// Jump-attack for small, non-humanoid enemies (slime, bat, and similar "leap at the player"
// creatures). Same shape as ChargeAttack's coroutine (precompute a destination, move the caster
// there over time via a temporarily-kinematic Rigidbody2D with its own colliders switched to
// trigger so nothing physically shoves it mid-jump, restore both afterward) - but shorter-range,
// with an added vertical arc for the hop. Not wired into AbilityController's fixed 0-9 slot list
// (that roster mirrors the player's fixed spell types specifically) - small-enemy behaviours (see
// SlimeBehaviour) invoke this directly via GetComponentInChildren<BumpAttack>().Invoke(transform)
// instead of going through AbilityController.
//
// Damage is applied exactly once, after the jump ends - NOT a per-frame overlap check. (An earlier
// version of this ability never set the inherited `acd` cooldown field, which defaults to 0, so
// Use() re-armed on the very next frame while the target stayed in range - that's what "continuous
// damage" actually was. acd is now set on the prefab; the attacking guard below is a second,
// independent guard against overlapping calls while a jump is already in flight.)
//
// Only the FLIGHT PATH (dir/fullDestination) and facing are locked in once the jump actually
// launches (after windup) - a committed, non-homing leap reads better than one that visibly bends
// mid-air toward a moving target. The HIT CHECK below deliberately does NOT share that lock: it
// reads the target's live position every step, so a player who moves away during the jump can
// genuinely dodge it. (An earlier version locked the hit check to the same frozen snapshot too -
// that meant "landed near where the target USED to be" still counted as a hit and damaged whatever
// the target's HealthController currently was, regardless of whether they'd actually moved out of
// the way - a dodge was never possible. Only the path needed to stay fixed, not the hit test.)
public class BumpAttack : Ability
{
    // Diagnostic toggle (off by default) - logs every attack's full sequence (launch, every step's
    // hit-check distance, why the loop ended, damage-check inputs/outcome, recovery) to a plain file
    // at Application.persistentDataPath/BumpAttackDebug.log. Same pattern as MovementController's
    // own DebugWallClamp - this project's MCP console-log capture does not retain anything logged
    // during an actual Play Mode session, so a real bug report from live testing needs a file to be
    // diagnosable at all.
    public static bool DebugLog = false;

    [SerializeField] float dmg = 10f;

    [Header("Jump")]
    // "Roughly 2 cells" - hop distance when nothing is hit before landing.
    [SerializeField] float jumpDistance = 2f;
    [SerializeField] float jumpDuration = 0.35f;
    [SerializeField] float jumpHeight = 0.5f;
    // How close to the target's LIVE position counts as "landed on it" - checked every step, so a
    // moving target can still be caught mid-air or dodge it, even though the flight path itself
    // (start/dir/fullDestination) never bends to chase them.
    [SerializeField] float hitRadius = 0.6f;
    // "Gather momentum" - caster holds still (movement.stuck) while the Attack clip's anticipation
    // squash plays, before actually launching.
    [SerializeField] float windupTime = 0.25f;
    // Fallback landing-recovery hold, used only if the Attack animator state can't be found (no
    // Animator, or it's not currently playing "Attack" for some reason) - the normal case ties the
    // recovery hold directly to however long that clip actually plays, see the coroutine's tail.
    [SerializeField] float landRecoverTime = 0.15f;

    bool attacking;

    public override void Use()
    {
        if (cd > 0f || attacking) return;
        StartCoroutine(JumpAttackCoroutine());
    }

    IEnumerator JumpAttackCoroutine()
    {
        attacking = true;
        cd = acd;

        var log = DebugLog ? new StringBuilder() : null;
        log?.AppendLine($"=== ATTACK START t={Time.time:F2} pos={((Vector2)user.position):F3} ===");

        var movement = user.GetComponent<MovementController>();
        var rb = user.GetComponent<Rigidbody2D>();
        var animator = user.GetComponent<Animator>();
        var enemyController = user.GetComponent<EnemyController>();
        var slimeBehaviour = user.GetComponent<SlimeBehaviour>();
        Transform targetTf = enemyController != null ? enemyController.CurrentTarget : null;
        log?.AppendLine($"targetTf={(targetTf != null ? targetTf.name + " " + (Vector2)targetTf.position : "NULL")}");

        // Wind-up: stop dead and hold (movement.stuck blocks MovementController.Move() the same way
        // it does for ChargeAttack) while the Attack clip's squash plays. Colliders go to trigger and
        // the body to Kinematic HERE, not after windup - see the tail of this coroutine for why they
        // now stay that way clear through landing recovery too, not just the jump itself.
        movement.stuck = true;
        rb.linearVelocity = Vector2.zero;
        if (animator != null && animator.isInitialized) animator.SetTrigger("Attack");
        var originalBodyType = rb.bodyType;
        rb.bodyType = RigidbodyType2D.Kinematic;
        var colliders = user.GetComponents<Collider2D>();
        var originalTrigger = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++) { originalTrigger[i] = colliders[i].isTrigger; colliders[i].isTrigger = true; }

        yield return new WaitForSeconds(windupTime);

        // Flight path + facing locked in now, once, for the whole jump - see class comment. The hit
        // check below intentionally does NOT reuse this snapshot.
        Vector2 start = rb.position;
        Vector2 launchAimPoint = targetTf != null ? (Vector2)targetTf.position : start + (Vector2)user.right;
        Vector2 toTarget = launchAimPoint - start;
        Vector2 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : (Vector2)user.right;

        // Clamp travel distance to whatever's actually a genuine, reachable landing spot along this
        // direction - collision is disabled for the whole jump+recovery sequence (kept that way
        // deliberately so landing near the target doesn't get physically shoved apart mid-squash),
        // so an unvalidated straight-line hop has no physical feedback if it goes somewhere bad,
        // until recovery ends and colliders go solid again (reading as "the slime resets right after
        // landing"). Two DIFFERENT failure modes needed checking, not one:
        //   - WalkBlocked: the straight line clips a real Obstacle-tier wall/tree (checked first;
        //     confirmed as a real, fixable case earlier, but not the whole story).
        //   - TryFindReachableLanding: the destination is off the baked NavMesh entirely (no wall in
        //     the way at all, just empty void past the map's walkable area) - confirmed live as the
        //     ACTUAL cause of a "stuck" repro: NavMesh.SamplePosition found nothing within a full
        //     unit of the landed position, CalculatePath came back PathInvalid with zero corners, and
        //     the creature was left with no path back, permanently. WalkBlocked alone never catches
        //     this - it only tests discrete obstacle colliders, not NavMesh coverage.
        // Binary search (not a raycast hit-distance) since neither check reports where along the
        // segment things stop being safe, only yes/no for a given endpoint.
        bool IsSafeLanding(Vector2 candidate) => !WalkBlocked(start, candidate) && TryFindReachableLanding(start, candidate, 0.15f, out _);

        float safeDistance = 0f;
        Vector2 fullDestination = start;
        if (IsSafeLanding(start + dir * jumpDistance))
        {
            safeDistance = jumpDistance;
            TryFindReachableLanding(start, start + dir * jumpDistance, 0.15f, out fullDestination);
        }
        else
        {
            float lo = 0f, hi = jumpDistance;
            for (int i = 0; i < 10; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (IsSafeLanding(start + dir * mid)) lo = mid; else hi = mid;
            }
            safeDistance = lo;
            if (lo > 0.01f) TryFindReachableLanding(start, start + dir * lo, 0.15f, out fullDestination);
            else fullDestination = start; // nothing safe in this direction at all - stay put rather than jump nowhere
        }
        if (slimeBehaviour != null) slimeBehaviour.SetFacing(dir);
        log?.AppendLine($"launch t={Time.time:F2} start={start:F3} aimPoint={launchAimPoint:F3} dir={dir:F3} safeDistance={safeDistance:F3}/{jumpDistance:F3} fullDestination={fullDestination:F3} hitRadius={hitRadius:F3}");

        bool hitTarget = false;
        Vector2 landingGroundPos = fullDestination;
        float elapsed = 0f;
        int stepCount = 0;
        while (elapsed < jumpDuration)
        {
            elapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(elapsed / jumpDuration);
            stepCount++;

            // Once a hit lands, horizontal travel stops there but the vertical hop keeps playing out
            // its full duration - an attack that connects (or was already point-blank) should still
            // visibly lift off and land, not just teleport-squash in place with no arc at all.
            Vector2 groundPos = hitTarget ? landingGroundPos : Vector2.Lerp(start, fullDestination, t);
            float arc = Mathf.Sin(t * Mathf.PI) * jumpHeight;
            rb.MovePosition(groundPos + Vector2.up * arc);

            if (!hitTarget && targetTf != null)
            {
                float d = Vector2.Distance(groundPos, targetTf.position);
                if (log != null && (stepCount <= 3 || stepCount % 5 == 0)) log.AppendLine($"  step {stepCount} t={t:F2} groundPos={groundPos:F3} targetPos={(Vector2)targetTf.position:F3} dist={d:F3}");
                if (d <= hitRadius)
                {
                    hitTarget = true;
                    landingGroundPos = groundPos;
                    log?.AppendLine($"  HIT at step {stepCount} t={t:F2} groundPos={groundPos:F3} targetPos={(Vector2)targetTf.position:F3} dist={d:F3}");
                }
            }

            yield return new WaitForFixedUpdate();
        }
        log?.AppendLine($"loop ended: steps={stepCount} elapsed={elapsed:F3} hitTarget={hitTarget} landingGroundPos={landingGroundPos:F3} fullDestination={fullDestination:F3}");

        // Direct position assignment, NOT MovePosition - MovePosition only takes effect for a still-
        // kinematic body on its NEXT physics step, and this needs to apply immediately.
        Vector2 finalLandingPos = hitTarget ? landingGroundPos : fullDestination;
        rb.position = finalLandingPos;
        rb.linearVelocity = Vector2.zero;
        // rb.position updates immediately when assigned, but Transform.position does NOT - the two
        // stay desynced until an actual physics step runs (confirmed live: reading transform.position
        // in the same instant as this assignment, with no intervening Physics2D step, still showed
        // the OLD pre-jump position). ResyncAgent() below reads user.position (Transform), so without
        // this explicit sync it was warping the NavMeshAgent to the stale pre-attack spot every time,
        // not the real landing spot - the actual reason the resync fix alone didn't work.
        user.position = new Vector3(finalLandingPos.x, finalLandingPos.y, 0f);

        // THE actual fix for "resets/gets stuck after landing": this jump just relocated the caster
        // via rb.MovePosition/rb.position, entirely outside GetPathDirection's normal flow - the only
        // place that keeps the NavMeshAgent's internal position tracker (agent.nextPosition) synced
        // to reality. Skipping this resync (as an earlier version of this ability did) left the agent
        // believing it was still wherever it was before the jump; its next SetDestination call could
        // then come back with a misleadingly "successful" PathComplete status but hasPath=false and
        // zero real movement (confirmed live via direct NavMeshAgent inspection on a stuck repro) -
        // exactly the failure mode EnemyController.ResyncNavMeshAgent()'s own doc comment already
        // describes for this exact class of bug, previously only wired up for Wizard's teleport.
        if (slimeBehaviour != null) slimeBehaviour.ResyncAgent();
        log?.AppendLine($"resynced agent after landing: pos={(Vector2)user.position:F3}");

        if (hitTarget && targetTf != null)
        {
            var status = targetTf.GetComponent<StatusController>();
            var myStatus = user.GetComponent<StatusController>();
            log?.AppendLine($"damage check: status={(status != null ? status.teamID.ToString() : "NULL")} myTeam={(myStatus != null ? myStatus.teamID.ToString() : "NULL")}");
            if (status != null && status.teamID != myStatus.teamID)
            {
                targetTf.GetComponent<HealthController>().TakeDamage((int)dmg, user);
                log?.AppendLine($"DAMAGE APPLIED dmg={dmg}");
            }
            else
            {
                log?.AppendLine("damage SKIPPED (same team or null status)");
            }
        }
        else
        {
            log?.AppendLine($"no damage: hitTarget={hitTarget} targetTf={(targetTf != null)}");
        }

        // Landing recovery: stays Kinematic/trigger-collided (i.e. immune to real physics push)
        // through this hold too, not just the jump itself - restoring solid Dynamic collision the
        // instant the jump loop ends left a window where, if it landed close to what it just
        // attacked, Unity's own collision solver would shove the two overlapping solid bodies apart
        // mid-squash-pose - confirmed live as exactly the "wobbles in place / resets to a previous
        // position" glitch. Recovery duration is tied to however long the Attack clip is actually
        // still playing (falls back to landRecoverTime if that can't be read) rather than a separate
        // hardcoded number, so retiming the clip later can't drift out of sync with this hold.
        Vector2 posBeforeRecovery = rb.position;
        log?.AppendLine($"pre-recovery: rb.position={rb.position:F3} transform.position={(Vector2)user.position:F3}");
        if (animator != null && animator.isInitialized)
        {
            float deadline = Time.time + landRecoverTime * 4f; // safety net only, in case the state is ever renamed/missing
            int recoverySteps = 0;
            while (Time.time < deadline)
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);
                recoverySteps++;
                if (log != null && recoverySteps % 15 == 0)
                    log.AppendLine($"  recovery frame {recoverySteps}: rb.position={rb.position:F3} transform.position={(Vector2)user.position:F3} normalizedTime={info.normalizedTime:F2}");
                if (!info.IsName("Attack") || info.normalizedTime >= 1f)
                {
                    log?.AppendLine($"recovery ended after {recoverySteps} frames: isAttack={info.IsName("Attack")} normalizedTime={info.normalizedTime:F2}");
                    break;
                }
                yield return null;
            }
            if (Time.time >= deadline) log?.AppendLine($"recovery hit SAFETY DEADLINE after {recoverySteps} frames");
        }
        else
        {
            log?.AppendLine("recovery: no animator, using flat landRecoverTime wait");
            yield return new WaitForSeconds(landRecoverTime);
        }
        log?.AppendLine($"post-recovery (before restoring bodyType/colliders): rb.position={rb.position:F3} transform.position={(Vector2)user.position:F3} driftFromPreRecovery={Vector2.Distance(posBeforeRecovery, rb.position):F3}");

        rb.bodyType = originalBodyType;
        for (int i = 0; i < colliders.Length; i++) colliders[i].isTrigger = originalTrigger[i];
        movement.stuck = false;

        log?.AppendLine($"=== ATTACK END t={Time.time:F2} rb.position={rb.position:F3} transform.position={(Vector2)user.position:F3} ===");
        attacking = false;

        // Log-only tail: watch the handoff back to normal chase movement for a few frames, in case
        // the FIRST Move() call (or Unity's own collision response now that colliders are solid
        // again) causes a visible snap that wouldn't show up in anything logged above.
        if (log != null)
        {
            for (int i = 0; i < 15; i++)
            {
                yield return null;
                log.AppendLine($"  post-handoff frame {i + 1}: rb.position={rb.position:F3} transform.position={(Vector2)user.position:F3} stuck={movement.stuck} bodyType={rb.bodyType}");
            }
            File.AppendAllText(Application.persistentDataPath + "/BumpAttackDebug.log", log.ToString());
        }
    }
}
