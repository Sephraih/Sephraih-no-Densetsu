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
        game.Register(transform);
        teamID = GetComponent<StatusController>().teamID;
        GetComponentInChildren<FireBolt>().acd = 5.0f;
        detectionRange = guardRadius;
    }

    void Update()
    {
        target = game.ClosestEnemy(transform);
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
        Die();
    }

    public override void Move()
    {
        switch (state)
        {
            case BotState.Chase:
                movementDirection = (Vector2)(target.position - transform.position);
                if (distanceToTarget < 1.0f) movementDirection = Vector2.zero;
                break;

            case BotState.Idle:
            case BotState.Return:
                movementDirection = (Vector2)(guardSpot - transform.position);
                if (distanceToGuardSpot < 0.5f)
                {
                    movementDirection = Vector2.zero;
                    state = BotState.Idle;
                }
                break;
        }

        movementDirection.Normalize();
        msi = Mathf.Clamp(movementDirection.magnitude, 0f, 1f);
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

        if (distanceToTarget < 1.0f)
            GetComponentInChildren<AbilityController>().Invoke(0, transform);
        else if (distanceToTarget < guardRadius && distanceToGuardSpot <= guardMaxChaseRadius)
            GetComponentInChildren<AbilityController>().Invoke(2, transform);

        if (distanceToTarget > 5.0f && distanceToTarget < 10.0f)
            GetComponentInChildren<AbilityController>().Invoke(4, transform);
    }
}
