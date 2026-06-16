using UnityEngine;

public class WizardBehaviour : EnemyController
{
    private float distanceToTarget;

    void Start()
    {
        game.Register(transform);
        teamID = GetComponent<StatusController>().teamID;
        GetComponent<StatusController>().Int = 10;
        detectionRange = 20f;
    }

    void Update()
    {
        target = game.ClosestEnemy(transform);
        distanceToTarget = (target != null && target != transform)
            ? Vector2.Distance(transform.position, target.position)
            : float.MaxValue;

        UpdateState();
        Move();
        Aim();
        Attack();
        Die();
    }

    public override void Move()
    {
        if (state != BotState.Chase)
        {
            GetComponent<MovementController>().Move(Vector2.zero, 0f);
            return;
        }

        // Kite: close the gap from far, hold position at mid range, escape if too close
        if (distanceToTarget >= 15f && distanceToTarget <= 20f)
            movementDirection = (target.position - transform.position).normalized;
        else if (distanceToTarget <= 5f)
        {
            GetComponentInChildren<AbilityController>().Invoke(6, transform);
            movementDirection = (transform.position - target.position).normalized;
        }
        else
            movementDirection = Vector2.zero;

        msi = Mathf.Clamp(movementDirection.magnitude, 0f, 1f);
        GetComponent<MovementController>().Move(movementDirection, msi);
    }

    public override void Attack()
    {
        if (state != BotState.Chase) return;
        if (distanceToTarget >= 3f && distanceToTarget <= 15f)
            GetComponentInChildren<AbilityController>().Invoke(4, transform);
    }
}
