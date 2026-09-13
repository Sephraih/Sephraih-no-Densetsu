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
    // The flipX sign is NOT the same for every row - confirmed live (user report: "south walks
    // seem inversed, SE walks SW and vice versa") and verified directly against the source art
    // afterward. Walk/Idle's two rows face OPPOSITE natural sides: row0 (Up, NE) is drawn facing
    // right, but row1 (Down, SW) is drawn facing LEFT - so Down needs the flip sign inverted
    // relative to Up, not the same rule reused. Attack's two rows, by contrast, BOTH face right
    // (confirmed the same way) - Attack's Down row (SE, not SW - its row order is inverted, see
    // GoblinBehaviour's class comment) needs the ordinary, non-inverted rule, same as Up. So the
    // correct sign for "Down" depends on which ACTION is currently showing, not just which row -
    // read back live from the Animator's current state rather than threaded through as a
    // parameter, since Move() keeps re-calling SetFacing every single frame the melee-range branch
    // is active, including every frame of an in-progress attack swing - a one-shot override passed
    // in only from Attack() would get silently clobbered by Move()'s very next call.
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
                if (dist < 1.0f)
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
        if (Vector2.Distance(transform.position, target.position) < 1.0f)
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
