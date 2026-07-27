using UnityEngine;

public class WizardBehaviour : EnemyController
{
    private float distanceToTarget;
    private Vector3 spawnSpot;

    void Start()
    {
        spawnSpot = transform.position;
        teamID = GetComponent<StatusController>().teamID;
        GetComponent<StatusController>().Int = 10;
        detectionRange = 20f;
    }

    void Update()
    {
        target = FindNearestEnemy(detectionRange, requireLineOfSight: state != BotState.Chase);
        distanceToTarget = (target != null && target != transform)
            ? Vector2.Distance(transform.position, target.position)
            : float.MaxValue;

        UpdateState();
        Move();
        Aim();
        Attack();
    }

    public override void Move()
    {
        switch (state)
        {
            case BotState.Chase:
                // Kite: close the gap from far, hold position at mid range, escape if too close
                if (distanceToTarget >= 15f && distanceToTarget <= 20f)
                    movementDirection = GetPathDirection(target.position);
                else if (distanceToTarget <= 5f)
                {
                    GetComponentInChildren<AbilityController>().Invoke(6, transform);
                    movementDirection = GetFleeDirection(target.position, 5f);
                }
                else
                    movementDirection = Vector2.zero;
                break;

            case BotState.Idle:
            case BotState.Return:
                if (Vector2.Distance(transform.position, spawnSpot) < 0.5f)
                {
                    movementDirection = Vector2.zero;
                    state = BotState.Idle;
                }
                else
                {
                    movementDirection = GetPathDirection(spawnSpot);
                }
                break;
        }

        msi = movementDirection.sqrMagnitude > 0.0001f ? 1f : 0f;
        GetComponent<MovementController>().Move(movementDirection, msi);
    }

    public override void Attack()
    {
        if (state != BotState.Chase) return;
        if (distanceToTarget >= 3f && distanceToTarget <= 15f)
            GetComponentInChildren<AbilityController>().Invoke(4, transform);
    }
}
