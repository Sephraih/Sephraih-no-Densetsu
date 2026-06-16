using System.Collections.Generic;
using UnityEngine;

public class EnemyController : UnitController
{
    public enum BotState { Idle, Chase, Return }

    protected Transform target;
    protected BotState state = BotState.Idle;

    [Header("Perception")]
    public float detectionRange = 8f;
    public LayerMask wallMask;

    protected GameBehaviour game;

    private float lostSightTimer;
    private const float LostSightDelay = 3f;

    protected override void Awake()
    {
        base.Awake();
        game = Camera.main.GetComponent<GameBehaviour>();
    }

    protected void Die()
    {
        if (GetComponent<HealthController>().health <= 0)
        {
            game.Remove(transform);
            Destroy(gameObject);
        }
    }

    // Returns true when no wall collider sits between this bot and the target.
    // If wallMask is unconfigured (0), always returns true so bots fall back to distance-only detection.
    protected bool HasLineOfSight(Transform t)
    {
        if (wallMask == 0) return true;
        Vector2 dir = t.position - transform.position;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir.normalized, dir.magnitude, wallMask);
        return hit.collider == null;
    }

    // Transitions between Idle / Chase / Return based on detection range and LOS.
    // Call once per Update before Move/Attack.
    protected void UpdateState()
    {
        if (target == null || target == transform)
        {
            if (state == BotState.Chase) state = BotState.Return;
            return;
        }

        float dist = Vector2.Distance(transform.position, target.position);
        bool canSee = dist <= detectionRange && HasLineOfSight(target);

        if (canSee)
        {
            lostSightTimer = LostSightDelay;
            if (state == BotState.Idle)
            {
                state = BotState.Chase;
                AlertNearby();
            }
        }
        else if (state == BotState.Chase)
        {
            lostSightTimer -= Time.deltaTime;
            if (lostSightTimer <= 0f)
                state = BotState.Return;
        }
    }

    // Notifies nearby allied bots that are still idle to start chasing.
    protected void AlertNearby(float radius = 6f)
    {
        foreach (Transform t in game.characterList)
        {
            if (t == transform) continue;
            EnemyController ally = t.GetComponent<EnemyController>();
            if (ally != null && ally.state == BotState.Idle &&
                Vector2.Distance(transform.position, t.position) <= radius)
            {
                ally.state = BotState.Chase;
            }
        }
    }

    public new void Aim()
    {
        if (target == null || target == transform) return;
        Vector3 dir = target.position - transform.position;
        dir.Normalize();
        attackingDirection.transform.localPosition = dir;
        if (movementDirection == Vector2.zero)
            GetComponent<MovementController>().LookAt(target.position);
    }
}
