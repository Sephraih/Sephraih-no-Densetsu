using UnityEngine;

public class MobBehaviour : EnemyController
{
    void Start()
    {
        game.Register(transform);
        teamID = GetComponent<StatusController>().teamID;
    }

    void Update()
    {
        if (GetComponent<MovementController>().stunned) { Die(); return; }

        target = game.ClosestEnemy(transform);
        UpdateState();
        Move();
        if (state == BotState.Chase) Aim();
        Attack();
        Die();
    }

    public override void Move()
    {
        switch (state)
        {
            case BotState.Chase:
                float dist = Vector2.Distance(transform.position, target.position);
                movementDirection = (target.position - transform.position).normalized;
                msi = dist > 1.0f ? 1f : 0f;
                break;
            default:
                movementDirection = Vector2.zero;
                msi = 0f;
                state = BotState.Idle;
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
