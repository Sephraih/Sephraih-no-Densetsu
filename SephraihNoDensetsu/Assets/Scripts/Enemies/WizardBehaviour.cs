using UnityEngine;

public class WizardBehaviour : EnemyController
{
    private float distanceToTarget;
    private Vector3 spawnSpot;
    private FireBolt fireBolt;

    void Start()
    {
        spawnSpot = transform.position;
        teamID = GetComponent<StatusController>().teamID;
        GetComponent<StatusController>().Int = 10;
        fireBolt = GetComponentInChildren<FireBolt>();

        // Perception percentages (visionRangePercent/visionDegrees/awarenessRangePercent/
        // blindSpotDegrees/detectionRangePercent/maxChaseDistancePercent, inherited from
        // EnemyController) are deliberately NOT set here - they're plain serialized fields on this
        // prefab's Inspector, tunable per enemy type without touching code. Setting them here would
        // silently overwrite whatever's configured on the prefab every time Start() runs. NOTE if
        // tuning these on the prefab: maxChaseDistancePercent must stay >= 1 (100%), or a target
        // detected right at the edge of the vision cone would immediately exceed a smaller leash
        // and bounce straight back to Return the very next frame.
    }

    void Update()
    {
        target = FindNearestEnemy(isAcquiring: state != BotState.Chase);
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
                // Kite thresholds below are fractions of fireBolt.range (the wizard's actual,
                // enforced max spell distance - see FireBolt.rangePercent/RangeSettings) rather
                // than independent hardcoded numbers. The fractions themselves (1x/1.33x/0.33x)
                // are carried over unchanged from this behavior's original hand-tuned absolute
                // values (15/20/5, back when 15 was FireBolt's de-facto but unenforced range) -
                // only what they're a fraction OF changed, so the kite still feels the same
                // relative to the wizard's real casting range, whatever that range is tuned to.
                float castRange = fireBolt.range;
                // Kite: close the gap from far, hold position at mid range, escape if too close
                if (distanceToTarget >= castRange && distanceToTarget <= castRange * 4f / 3f)
                    movementDirection = DeflectAroundOtherUnits(GetPathDirection(target.position));
                else if (distanceToTarget <= castRange / 3f)
                {
                    GetComponentInChildren<AbilityController>().Invoke(6, transform); // Teleport - relocates transform directly, bypassing the NavMeshAgent
                    ResyncNavMeshAgent(); // keep the agent's path state in sync with the post-teleport position (see ResyncNavMeshAgent's doc comment)
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
                    movementDirection = DeflectAroundOtherUnits(GetPathDirection(spawnSpot));
                }
                break;
        }

        msi = movementDirection.sqrMagnitude > 0.0001f ? 1f : 0f;
        GetComponent<MovementController>().Move(movementDirection, msi);
    }

    public override void Attack()
    {
        if (state != BotState.Chase) return;
        // 0.2x/1x of fireBolt.range - same derivation as Move()'s kite thresholds above (carried
        // over from the original 3/15 absolute values). Upper bound matches castRange exactly so
        // the wizard never fires at a target the bolt can't actually reach before self-destructing.
        float castRange = fireBolt.range;
        if (distanceToTarget >= castRange * 0.2f && distanceToTarget <= castRange)
            GetComponentInChildren<AbilityController>().Invoke(4, transform);
    }
}
