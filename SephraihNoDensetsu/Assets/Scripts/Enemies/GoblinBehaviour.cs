using UnityEngine;

// Simple melee chaser, structurally identical to MobBehaviour (chase/return via GetPathDirection,
// BasicAttack at melee range) - the only real difference is how it's drawn. Mob/Guard/Wizard use
// the shared Animator blend tree ("Aniwalk", continuous moveX/moveY) for smooth 8-directional
// motion; this uses image-generated sprite-sheet animations instead, which only cover 2 of the 4
// diagonal facings per action (the other 2 are the same art mirrored via flipX) - the same
// 4-diagonal-via-2-sprites convention SlimeBehaviour already established, just with real per-frame
// animation clips instead of a single static sprite per facing.
//
// Since only 2 of the 4 diagonals have real source art per action, the Animator can't just blend
// smoothly between "up" and "down" the way Aniwalk does - it's a hard discrete switch. Implemented
// as two separate RuntimeAnimatorControllers sharing the same Idle/Walk/Attack state structure and
// parameters (Speed/Attack): Goblin.controller (the "Down"-facing NE/SE-mirrored-pair... see below)
// as the base, and GoblinUp.overrideController remapping the same three states to the "Up" clips.
// SetFacing swaps animator.runtimeAnimatorController between the two only when the up/down side
// actually changes (not every frame) - reassigning it always resets to that controller's own
// default state, which is fine since Idle is a harmless place to reset into and this only happens
// on an actual facing flip, not continuously.
public class GoblinBehaviour : EnemyController
{
    private Vector3 spawnSpot;
    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private BasicAttack basicAttack;

    [SerializeField] RuntimeAnimatorController downController;
    [SerializeField] RuntimeAnimatorController upController;

    // Which of the Down-bucket (the "down" controller's own facing, SE by this project's convention)
    // actions need SetFacing's flip-sign INVERTED relative to the Up-bucket's own plain rule
    // (flipX = dir.x < 0). This is a per-CHARACTER trait, not a universal constant - it depends
    // entirely on which physical direction each action's own source art happens to face, which
    // varies per sprite sheet/artist and isn't guaranteed to agree between actions on the SAME
    // character, let alone between different characters reusing this same script.
    //
    // Confirmed live, twice, that this genuinely varies: Goblin's own art (this struct's default
    // values below) needs Idle AND Walk inverted but Attack not. Gobking - despite reusing this
    // exact same script/formula - needed a DIFFERENT combination (Idle inverted, Walk NOT), because
    // its Walk-Down sheet happens to be drawn facing the opposite physical direction from its own
    // Idle-Down sheet - a real inconsistency between those two specific sheets' own art, not a
    // formula bug. Set this explicitly per new character based on a live playtest; don't assume
    // Goblin's own defaults carry over just because the script does.
    //
    // A character whose source art is drawn with BOTH buckets consistently facing the SAME physical
    // direction for every action (e.g. every sheet, every action, always drawn facing screen-right)
    // needs all three flags OFF - flipX alone already handles every case symmetrically in that case,
    // no per-action exception needed at all. That's the recommended convention for any future
    // character built with this script, specifically to avoid this exact class of bug recurring -
    // if adopted, just leave every flag false rather than copying Goblin's true/true/false.
    [System.Serializable]
    public struct DownBucketInvert
    {
        public bool idle;
        public bool walk;
        public bool attack;
    }
    [SerializeField] DownBucketInvert downInvert = new DownBucketInvert { idle = true, walk = true, attack = false };

    // How close counts as "melee range" - was a bare hardcoded `1.0f` literal (duplicated in both
    // Move() and Attack() below) until Gobking's own bigger Transform.localScale (1.2, vs Goblin's
    // 0.75) exposed it as a real problem: Collider2D size/offset and NavMeshAgent.radius both scale
    // automatically with the Transform, but a plain float distance comparison in code does NOT - a
    // bigger character's own real physical footprint (collider + NavMesh clearance) grows while this
    // stayed fixed, so Move() kept trying to close the gap to the SAME absolute 1.0 units it always
    // did, but the two characters' now-larger colliders physically shoved them apart before that
    // distance was reachable - reading as "he tries to ram the player, never quite gets close enough
    // to actually attack." Serialized so each character can scale this proportionally to their own
    // Transform.localScale rather than sharing Goblin's original tuning unconditionally.
    [SerializeField] float meleeRange = 1.0f;

    // null until the first SetFacing call - deliberately not initialized to a real bool so the
    // very first call always applies its controller, even if dir.y happens to make "facingUp"
    // evaluate the same as the field's default would have.
    private bool? facingUp;

    void Start()
    {
        spawnSpot = transform.position;
        teamID = GetComponent<StatusController>().teamID;
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        basicAttack = GetComponentInChildren<BasicAttack>();
    }

    // Same convention as SlimeBehaviour.SetFacing: dir.y decides up vs down (here: which of the two
    // authored diagonal pairs), dir.x decides flipX. Leaves facing alone when dir is ~zero so it
    // holds its last direction while idle, matching how the rest of this project's directional
    // sprites already behave.
    //
    // The flipX sign is NOT the same for every row, and not even the same for every ACTION within
    // the Down bucket - see downInvert's own comment for why this is a per-character, per-action
    // config rather than a hardcoded formula. Read back live from the Animator's current state
    // rather than threaded through as a parameter, since Move() keeps re-calling SetFacing every
    // single frame the melee-range branch is active, including every frame of an in-progress attack
    // swing or walk cycle - a one-shot override passed in only from Attack() would get silently
    // clobbered by Move()'s very next call.
    public void SetFacing(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;

        bool wantsUp = dir.y > 0f;
        if (facingUp == null || facingUp.Value != wantsUp)
        {
            facingUp = wantsUp;
            animator.runtimeAnimatorController = wantsUp ? upController : downController;
        }

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool needsInvert = stateInfo.IsName("Attack") ? downInvert.attack
            : stateInfo.IsName("Walk") ? downInvert.walk
            : downInvert.idle;
        bool invert = !wantsUp && needsInvert;
        spriteRenderer.flipX = invert ? (dir.x > 0f) : (dir.x < 0f);
    }

    void Update()
    {
        if (GetComponent<MovementController>().stunned) return;

        AcquireTarget();
        UpdateState();
        Move();
        if (state == BotState.Chase) Aim();
        Attack();
    }

    public override void Move()
    {
        var movementController = GetComponent<MovementController>();
        switch (state)
        {
            case BotState.Chase:
                float dist = Vector2.Distance(transform.position, target.position);
                if (dist < meleeRange)
                {
                    movementDirection = Vector2.zero;
                    SetFacing((Vector2)(target.position - transform.position));
                }
                else
                {
                    movementDirection = DeflectAroundOtherUnits(GetPathDirection(target.position));
                    SetFacing(movementDirection);
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
        if (Vector2.Distance(transform.position, target.position) < meleeRange)
        {
            // BasicAttack.Use()'s own PlayDirectionalAttack("Attack") call resolves a CARDINAL
            // state name (AttackUp/Down/Left/Right, from GetFacingDirectionName's moveX/moveY
            // read) - this controller only has diagonal-pair states, so that lookup harmlessly
            // no-ops (Animator.HasState returns false) and never plays anything. Trigger the swing
            // ourselves instead, independent of that cardinal system, matching how BumpAttack/
            // ChargeAttack each drive their own "Attack" trigger directly rather than relying on it.
            //
            // Gated on basicAttack.IsReady - this method runs every single Update() frame while in
            // melee range, but the ability itself only actually fires once per its own 1s cooldown.
            // Firing SetTrigger unconditionally every frame kept the Animator's "Attack" trigger
            // permanently armed: the AnyState->Attack transition can't fire AS a self-transition
            // while already in Attack (canTransitionToSelf=false), so the pending trigger just sat
            // there un-consumed for the whole ~0.36s clip, then fired the INSTANT the clip's own
            // exit transition reached Idle - yanking it straight back into Attack before Idle ever
            // showed a single frame. Confirmed live as the actual mechanism behind "roughly 2
            // animations play per attack, only every 2nd deals damage" - the animation was
            // retriggering itself on every exit, completely decoupled from the real cooldown.
            if (basicAttack.IsReady) animator.SetTrigger("Attack");
            GetComponentInChildren<AbilityController>().InvokeMouse(0, transform);
        }
    }
}
