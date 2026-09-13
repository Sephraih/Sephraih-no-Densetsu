using UnityEngine;

// Small "leap at the player" enemy archetype (slime, and future similar small animals) - chases
// within its perception range, then stops and jump-attacks via BumpAttack once close enough (no
// ranged/special attack). Idle/Chase/Return handling mirrors MobBehaviour's own pattern; the real
// differences are jumpTriggerRange (this creature stops well short of the target and covers the
// rest of the gap as part of the attack itself, not by walking into contact) and the attack itself
// (BumpAttack instead of AbilityController's slot-0 BasicAttack - see its own comment for why).
// BumpAttack.Use() fires the squash-stretch Attack animator trigger and takes over the caster's
// position (via a temporarily-kinematic Rigidbody2D), facing (SetFacing, called once with the
// jump's locked-in direction), AND target/state tracking for the whole windup+jump+landing-recovery
// sequence - Update() below explicitly steps aside entirely (checks MovementController.stuck)
// rather than continuing to re-scan/re-aim every frame underneath it.
public class SlimeBehaviour : EnemyController
{
    private Vector3 spawnSpot;
    private BumpAttack bumpAttack;
    private SpriteRenderer spriteRenderer;

    // How close the target must be before this creature stops walking and winds up its jump attack -
    // deliberately half of BumpAttack's own jumpDistance, so the jump OVERSHOOTS the target by that
    // same margin rather than just barely reaching them. Triggering at (close to) the full jump range
    // made the attack trivial to dodge: any movement away during the windup was enough to put the
    // target juuust out of reach by launch time. Triggering closer and overshooting means a target
    // that's simply walking away (not sprinting) is still very likely to get caught, since the extra
    // reach eats into whatever ground they cover during windup+flight. Kept as a separate tunable
    // here (rather than reading BumpAttack's own field) the same way GuardBehaviour keeps its own
    // melee-range constant in sync with ChargeAttack's landing distance by comment convention, not by
    // sharing the field - retune alongside BumpAttack.jumpDistance if that ever changes.
    public float jumpTriggerRange = 1.0f;

    // 4-diagonal facing set, no straight up/down/left/right - a mob that's always beelining toward
    // the player rarely moves purely cardinal, and this halves the art needed vs. a full 8-way set.
    // faceDownSprite is the "toward camera" pose (face visible, offset to one side); faceUpSprite is
    // "away from camera" (back of the head, no face at all - flipping it is a visual no-op since
    // it's symmetric, so a single sprite covers both up-left/up-right). Left/right within each pair
    // come from SpriteRenderer.flipX rather than separate art - faceDownSprite is authored with the
    // face offset toward its own right, so unflipped = down-right, flipped = down-left.
    [SerializeField] Sprite faceDownSprite;
    [SerializeField] Sprite faceUpSprite;

    void Start()
    {
        spawnSpot = transform.position;
        teamID = GetComponent<StatusController>().teamID;
        bumpAttack = GetComponentInChildren<BumpAttack>();
        // Lives on the SquashAnchor/Sprite grandchild now, not the root - moved there so the squash-
        // stretch animations can scale around the sprite's bottom edge instead of its center (see
        // SquashAnchor's own local position, offset to the sprite's bottom, with Sprite offset back
        // up to compensate at rest scale). Explicit path, NOT GetComponentInChildren - AttackPos (a
        // leftover ranged-attack-facing-indicator slot inherited from BaseEnemy, unused by this melee
        // creature but still carrying its own blank SpriteRenderer) sits earlier in sibling order and
        // was silently winning that search instead, leaving the real visible sprite's flipX/sprite
        // fields never touched - confirmed live as the actual cause of "always facing right."
        spriteRenderer = transform.Find("SquashAnchor/Sprite").GetComponent<SpriteRenderer>();
    }

    // Picks the down/up sprite from dir.y and flipX from dir.x - see the facing fields' own comment
    // for why only these two axes matter. Leaves the current facing alone when dir is ~zero (not
    // moving and not actively facing a melee target) so it holds its last direction rather than
    // snapping to some default, matching how MovementController.GetFacingVector() already behaves
    // for the directional-sprite units. Public so BumpAttack can lock facing to its own jump
    // direction once at launch instead of this script re-aiming it live every frame mid-jump.
    public void SetFacing(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        spriteRenderer.sprite = dir.y <= 0f ? faceDownSprite : faceUpSprite;
        spriteRenderer.flipX = dir.x < 0f;
    }

    // Thin public wrapper around EnemyController's own (protected) ResyncNavMeshAgent - BumpAttack
    // needs to call this right after its jump relocates the caster, for exactly the reason that
    // method's own doc comment already describes: it exists for "anything that relocates this
    // unit's transform OUTSIDE of normal GetPathDirection-driven walking", previously only
    // WizardBehaviour's flee-Teleport. BumpAttack's rb.MovePosition/rb.position jump is exactly that
    // - and skipping this call was the actual root cause of the "resets/gets stuck after landing"
    // bug: while movement.stuck holds true through windup+jump+recovery, Move() (and therefore
    // GetPathDirection, the ONLY place agent.nextPosition gets resynced to the real transform) never
    // runs, so the agent's internal position tracker sits frozen at wherever it was before the jump
    // while the real Rigidbody2D position jumps up to jumpDistance away. If the target's stationary
    // during the attack, that stale internal position happens to still be close enough that nothing
    // breaks; if the target keeps moving (confirmed live as the actual trigger condition), the real
    // landing spot ends up meaningfully different from the stale one, and the next SetDestination
    // call comes back with a misleadingly "successful" PathComplete status but hasPath=false and
    // zero real movement - permanently, since nothing ever resyncs it afterward either.
    public void ResyncAgent() => ResyncNavMeshAgent();

    void Update()
    {
        var movementController = GetComponent<MovementController>();
        if (movementController.stunned) return;

        // BumpAttack has taken over position/facing for its whole windup+jump+landing-recovery
        // sequence (see class comment) - back off ENTIRELY while that's happening, not just in
        // Move(). Re-running FindNearestEnemy/UpdateState every frame here, even during stuck, was a
        // real bug: a single frame where the perception scan doesn't turn up the target (edge of
        // range, a physics-query hiccup, doesn't matter) nulls the `target` field and UpdateState()
        // immediately flips `state` to Return - harmless to the jump itself (BumpAttack captured its
        // own separate, stable target reference before this could touch it), but the INSTANT stuck
        // clears, Move() sees state==Return and starts walking back toward spawn right away, reading
        // as "the slime resets backward right after landing" (confirmed live as the actual mechanism
        // behind that symptom).
        if (movementController.stuck) return;

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
                if (dist < jumpTriggerRange)
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
        if (Vector2.Distance(transform.position, target.position) < jumpTriggerRange)
            bumpAttack.Invoke(transform);
    }
}
