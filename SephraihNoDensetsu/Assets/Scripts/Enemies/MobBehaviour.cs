using UnityEngine;

public class MobBehaviour : EnemyController
{
    private Vector3 spawnSpot;

    void Start()
    {
        spawnSpot = transform.position;
        teamID = GetComponent<StatusController>().teamID;
    }

    void Update()
    {
        if (GetComponent<MovementController>().stunned) { return; }

        target = FindNearestEnemy(isAcquiring: state != BotState.Chase);
        UpdateState();
        Move();
        if (state == BotState.Chase) Aim();
        Attack();
    }

    public override void Move()
    {
        switch (state)
        {
            case BotState.Chase:
                float dist = Vector2.Distance(transform.position, target.position);
                movementDirection = DeflectAroundOtherUnits(GetPathDirection(target.position));
                msi = dist > 1.0f ? 1f : 0f;
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
                }
                break;
        }
        GetComponent<MovementController>().Move(movementDirection, msi);
    }

    public override void Attack()
    {
        if (state != BotState.Chase || target == null || target == transform) return;
        if (Vector2.Distance(transform.position, target.position) < 1.0f)
            GetComponentInChildren<AbilityController>().InvokeMouse(0, transform);
    }
}
