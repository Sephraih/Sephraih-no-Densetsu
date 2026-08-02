using UnityEngine;

public class GuardBehaviour : EnemyController
{
    private Vector3 guardSpot;
    public float guardMaxChaseRadius = 25.0f;
    public float guardRadius = 5.0f;

    private float distanceToTarget;
    private float distanceToGuardSpot;

    void Start()
    {
        guardSpot = transform.position;
        teamID = GetComponent<StatusController>().teamID;
        GetComponentInChildren<FireBolt>().acd = 5.0f;

        // Perception tiers (visionRange/visionDegrees/awarenessRange/blindSpotDegrees/
        // detectionRange/maxChaseDistance, inherited from EnemyController) are deliberately NOT
        // set here - they're plain serialized fields on this prefab's Inspector, tunable per enemy
        // type without touching code. Setting them here would silently overwrite whatever's
        // configured on the prefab every time Start() runs. guardRadius stays a separate field
        // (still used for this file's own melee/ranged-ability-range checks in Move()/Attack()) -
        // it's no longer linked to visionRange in code, so the two can be tuned independently.
    }

    void Update()
    {
        target = FindNearestEnemy(isAcquiring: state != BotState.Chase);
        distanceToTarget = (target != null && target != transform)
            ? Vector2.Distance(transform.position, target.position)
            : float.MaxValue;
        distanceToGuardSpot = Vector2.Distance(transform.position, guardSpot);

        UpdateState();

        // Force return if the chase has led too far from the guard spot
        if (state == BotState.Chase && distanceToGuardSpot >= guardMaxChaseRadius)
            state = BotState.Return;

        Move();
        GuardAim();
        Attack();
    }

    public override void Move()
    {
        switch (state)
        {
            case BotState.Chase:
                // 1.2f, matching Attack()'s own melee-range check below (kept in sync
                // deliberately - see the note there). ChargeAttack lands the guard ~1.1 units from
                // its target (deliberately outside true melee range, to keep the colliders clear
                // of each other once solid again - see ChargeAttack.meleeRange). If this used a
                // tighter cutoff than Attack()'s own, the guard would keep trying to walk the last
                // sliver of distance closed every single frame via normal solid-body movement,
                // physically shoving the target the entire time it's stuck in that gap - a
                // sustained push, not a one-off nudge.
                movementDirection = distanceToTarget < 1.2f ? Vector2.zero : DeflectAroundOtherUnits(GetPathDirection(target.position));
                break;

            case BotState.Idle:
            case BotState.Return:
                if (distanceToGuardSpot < 0.5f)
                {
                    movementDirection = Vector2.zero;
                    state = BotState.Idle;
                }
                else
                {
                    movementDirection = DeflectAroundOtherUnits(GetPathDirection(guardSpot));
                }
                break;
        }

        msi = movementDirection.sqrMagnitude > 0.0001f ? 1f : 0f;
        GetComponent<MovementController>().Move(movementDirection, msi);
    }

    // Guards face their movement direction while moving, otherwise face the player if nearby.
    private void GuardAim()
    {
        if (movementDirection != Vector2.zero)
        {
            attackingDirection.transform.localPosition = movementDirection;
        }
        else if (target != null && target != transform && distanceToTarget < 20.0f)
        {
            Vector2 dir = (target.position - transform.position).normalized;
            attackingDirection.transform.localPosition = (Vector3)dir;
            GetComponent<MovementController>().LookAt(target.position);
        }
    }

    public override void Attack()
    {
        if (state != BotState.Chase || target == null || target == transform) return;

        // Matches Move()'s own chase-stop threshold above (also 1.2f) - they used to disagree
        // (this was 1.0f while Move() stopped walking at 1.2f), leaving a dead zone right where
        // ChargeAttack lands the guard (~1.1 units away): close enough to stop walking, not close
        // enough to satisfy this check, so the guard would just stand there unable to melee until
        // something else changed the distance.
        if (distanceToTarget < 1.2f)
            GetComponentInChildren<AbilityController>().Invoke(0, transform);
        else if (distanceToTarget < guardRadius && distanceToGuardSpot <= guardMaxChaseRadius)
            GetComponentInChildren<AbilityController>().Invoke(2, transform);

        if (distanceToTarget > 5.0f && distanceToTarget < 10.0f)
            GetComponentInChildren<AbilityController>().Invoke(4, transform);
    }
}
