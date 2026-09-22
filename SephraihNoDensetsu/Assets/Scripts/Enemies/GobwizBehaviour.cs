using System.Collections;
using UnityEngine;

// Ranged goblin caster: approaches to roughly FireBolt's own range (rangePercent=1 -> half the screen
// width, same as WizardBehaviour), holds at that range and casts in place, then resumes chasing or
// backs off (plain walking flee, not Wizard's Teleport-flee) to keep that distance. AI shape (Move/
// Attack threshold logic) is copied from WizardBehaviour; animation-driving (discrete Up/Down
// AnimatorOverrideController swap + SpriteRenderer.flipX mirroring) is copied from GoblinBehaviour,
// since gobwiz's art is goblin-style (2-of-4-diagonal sheets), not the Aniwalk continuous blend tree
// Wizard/Guard/Mob use.
public class GobwizBehaviour : EnemyController
{
    private Vector3 spawnSpot;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private FireBolt fireBolt;
    private float distanceToTarget;

    [SerializeField] RuntimeAnimatorController downController;
    [SerializeField] RuntimeAnimatorController upController;

    private bool? facingUp;

    // True from the moment the attack windup starts until the delayed cast actually fires (see
    // CastFireBoltAtReleaseFrame) - guards against Attack() re-triggering a second windup before the
    // first one's cast has happened. NOT the same thing as fireBolt.IsReady/its cd: that cooldown is
    // deliberately still only set inside FireBolt's own Bolt() at the moment the projectile actually
    // spawns (unchanged, original timing) - reserving it early here instead (an earlier version of
    // this fix tried exactly that, via a since-removed Ability.StartCooldown()) blocked the real cast
    // too, since by the time the delayed Invoke() finally ran, Time.deltaTime had already ticked cd
    // most of the way back down from acd but not all the way to 0, so Bolt()'s own `cd <= 0` check
    // failed and the bolt silently never fired at all. This flag is a separate, purely re-trigger-
    // guarding concern from the ability's own cooldown.
    private bool castPending = false;

    // How far into the attack clip (0-1, fraction of its length) the bolt actually fires - tunable
    // in the Inspector rather than hardcoded, since this is a feel/timing call, not a correctness
    // one. Default 0.5 = the start of frame 3 of the clip's 4 discrete frames (2/4 - each frame is an
    // equal 1/4 slice of the clip's length): the release was originally synced to the very end of
    // the clip (fraction 1.0, "frame 4"), but that read as too late - frame 3's own windup pose is
    // the intended release point instead.
    [Range(0f, 1f)]
    public float releaseTimeFraction = 0.5f;

    void Start()
    {
        spawnSpot = transform.position;
        teamID = GetComponent<StatusController>().teamID;
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        fireBolt = GetComponentInChildren<FireBolt>();
    }

    // Mirrors GoblinBehaviour.SetFacing's controller-swap mechanism AND its exact invert formula
    // (`!wantsUp && !isAttacking`) - a static screenshot comparison of the raw art (idle/walk/attack,
    // unflipped) earlier showed every row facing the same physical side, which led to briefly shipping
    // this with no exception at all, then a narrower "only Walk" exception after the user caught the
    // walk-cycle being backwards - but a single still frame can't distinguish "facing screen-left" from
    // "facing screen-right with the same torso/weapon orientation" the way real in-game movement can,
    // so the screenshot check was never trustworthy for this. The user's live playtest is the ground
    // truth: Idle's post-attack facing was ALSO backwards on the down side (confirmed only on the two
    // "south" variants, matching "both north sides work" - the up/NE bucket never needed inverting
    // either way). Down-facing Idle and Walk art both read as their LABELED direction only when shown
    // unflipped for LEFTWARD movement; only Attack's down-facing art is unflipped-for-rightward, exactly
    // like Goblin's own asymmetry - just broadened from "only Walk" to "anything but Attack."
    public void SetFacing(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;

        bool wantsUp = dir.y > 0f;
        if (facingUp == null || facingUp.Value != wantsUp)
        {
            facingUp = wantsUp;
            animator.runtimeAnimatorController = wantsUp ? upController : downController;
        }

        bool isAttacking = animator.GetCurrentAnimatorStateInfo(0).IsName("Attack");
        bool invert = !wantsUp && !isAttacking;
        spriteRenderer.flipX = invert ? (dir.x > 0f) : (dir.x < 0f);
    }

    void Update()
    {
        if (GetComponent<MovementController>().stunned) return;

        AcquireTarget();
        distanceToTarget = (target != null && target != transform)
            ? Vector2.Distance(transform.position, target.position) : float.MaxValue;
        UpdateState();
        Move();
        Attack();
    }

    public override void Move()
    {
        var movementController = GetComponent<MovementController>();
        switch (state)
        {
            case BotState.Chase:
                float castRange = fireBolt.range;
                bool casting = animator.GetCurrentAnimatorStateInfo(0).IsName("Attack");
                if (casting)
                {
                    // Hold still for the whole cast, same as melee Goblin holding still once in swing
                    // range - Attack() already fired/aimed before this state was entered.
                    movementDirection = Vector2.zero;
                    SetFacing((Vector2)(target.position - transform.position));
                }
                else if (distanceToTarget > castRange * 1.05f)
                {
                    // Too far - close the gap. Upper bound matches Attack()'s own max-range check
                    // exactly (both 1.05x) - previously this was 1.1x while Attack() capped at 1.05x,
                    // leaving a dead band (1.05x-1.10x) where Move() had already stopped approaching
                    // (distance <= 1.1x reads as "close enough, hold") but Attack() still refused to
                    // fire (distance > 1.05x reads as "still out of range") - approaching from far away
                    // lands almost exactly in that gap on the first frame distance drops below 1.1x, so
                    // he'd freeze there without ever attacking. Retreating from close range doesn't hit
                    // this, since it enters the hold band from its LOWER edge (0.75x) instead, which was
                    // always inside Attack()'s window - hence the reported chase-him-vs-he-chases-me
                    // asymmetry. Keeping both bounds identical removes the dead band entirely.
                    movementDirection = DeflectAroundOtherUnits(GetPathDirection(target.position));
                    SetFacing(movementDirection);
                }
                else if (distanceToTarget < castRange * 0.75f)
                {
                    // Too close - back off on foot (no Teleport-flee, unlike Wizard) to reopen range.
                    movementDirection = GetFleeDirection(target.position, 5f);
                    SetFacing(movementDirection);
                }
                else
                {
                    // In the sweet band - hold position, face the target, let Attack() fire.
                    movementDirection = Vector2.zero;
                    SetFacing((Vector2)(target.position - transform.position));
                }
                msi = movementDirection.sqrMagnitude > 0.0001f ? 1f : 0f;
                break;

            case BotState.Idle:
            case BotState.Return:
                if (Vector2.Distance(transform.position, spawnSpot) < 0.5f)
                {
                    movementDirection = Vector2.zero;
                    msi = 0f;
                    state = BotState.Idle;
                }
                else
                {
                    movementDirection = DeflectAroundOtherUnits(GetPathDirection(spawnSpot));
                    msi = movementDirection.sqrMagnitude > 0.0001f ? 1f : 0f;
                    SetFacing(movementDirection);
                }
                break;
        }
        movementController.Move(movementDirection, msi);
    }

    public override void Attack()
    {
        if (state != BotState.Chase || target == null || target == transform) return;
        if (animator.GetCurrentAnimatorStateInfo(0).IsName("Attack")) return; // one bolt per animation
        if (castPending) return; // already mid-windup for a cast that hasn't fired yet
        if (!fireBolt.IsReady) return;

        float castRange = fireBolt.range;
        if (distanceToTarget < castRange * 0.2f || distanceToTarget > castRange * 1.05f) return;

        // Aim true at the target's current position rather than relying on whatever
        // movementDirection Move() last set (which sits at zero while holding/casting) - UnitController.
        // Aim() only updates attackingDirection when movementDirection != zero, so it can't be trusted
        // to be pointed at a kiting target the moment a cast starts.
        Vector2 dirToTarget = (target.position - transform.position).normalized;
        attackingDirection.transform.localPosition = dirToTarget * 0.5f;
        SetFacing(dirToTarget);

        animator.SetTrigger("Attack");
        castPending = true;
        StartCoroutine(CastFireBoltAtReleaseFrame());
    }

    // The bolt now flies out at releaseTimeFraction through the attack animation (the release pose)
    // instead of the instant the swing starts - waits one frame first, since SetTrigger only
    // actually switches the Animator into the "Attack" state on its NEXT internal update, not this
    // one (confirmed via the AnyState->Attack transition itself: hasExitTime=false/duration=0, so
    // it's instant, just not same-frame instant). Reads the clip's own length back off the Animator
    // rather than a hardcoded delay, so retiming the attack clip later can't drift out of sync with
    // this (only releaseTimeFraction's own meaning - "which frame" - would need re-tuning if the
    // frame COUNT changes). Ability.Invoke()'s own stunned-check (see Ability.cs) still applies at
    // the moment this actually fires, so a gobwiz stunned mid-windup correctly never completes the
    // cast.
    IEnumerator CastFireBoltAtReleaseFrame()
    {
        yield return null;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        if (info.IsName("Attack")) yield return new WaitForSeconds(info.length * releaseTimeFraction);

        // Re-aim at the target's CURRENT position right before actually firing. Attack() set
        // attackingDirection once, back at the start of the windup - deliberately left untouched
        // here, since that's also what SetFacing used for the animation-facing choice, and the whole
        // point of telegraphing the windup is to give the player an honest cue to react to (see
        // Attack()'s own comment). But the bolt's actual flight direction should reflect where the
        // target really is by release time, not a stale frame-0 snapshot - otherwise a player who
        // correctly reads the telegraph and dodges can still get hit by a bolt aimed at where they
        // used to stand. `target` can go null between the windup starting and this coroutine resuming
        // (e.g. the target dying mid-cast) - if so, just fall back to whatever direction was already
        // set rather than crashing.
        if (target != null && target != transform)
        {
            Vector2 freshDirToTarget = (target.position - transform.position).normalized;
            attackingDirection.transform.localPosition = freshDirToTarget * 0.5f;
        }

        GetComponentInChildren<AbilityController>().Invoke(4, transform);
        castPending = false;
    }
}
