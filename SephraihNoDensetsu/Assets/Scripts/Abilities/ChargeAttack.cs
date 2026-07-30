using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChargeAttack : Ability
{
    public GameObject chargeEffect;
    public float stunTime = 1.0f;
    public float chargeSpeed = 70f;
    // How close counts as "arrived." Deliberately a bit more than the ~1.0f melee-range
    // convention used elsewhere (Mob/Guard's own attack checks): both characters' real colliders
    // are ~0.88 wide (0.44 half-width each), so landing at exactly 1.0 leaves only ~0.12 units of
    // genuine clearance - a slight discrepancy (trigger/kinematic restored a frame early, the
    // target having drifted slightly, etc.) can leave them overlapping, and Unity's physics then
    // forcibly separates two solid dynamic bodies the instant collision response resumes - a real
    // push, just delayed to the end of the dash instead of during it. 1.1f keeps genuine daylight
    // between the colliders once solid again.
    public float meleeRange = 1.1f;
    public float maxChargeDuration = 1.0f; // safety net only - normal charges finish well before this
    public float trailEffectInterval = 0.1f; // how often the trail particle spawns during the dash
    private int dmg = 80;

    private List<Vector2> chargeWaypoints;
    private Transform target;


    //run at a target, damage based on character attack*3 and stun it for a short time
    public override void UseTarget(Transform target)
    {
        if (user.GetComponent<StatusController>().teamID != target.GetComponent<StatusController>().teamID)
        {
            if (cd <= 0f) // if ability ready to use
            {
                // Charge attack is "walk to destination", not a teleport - it must obey the same
                // obstacle rules as ordinary walking, and its range must reflect the TRUE walking
                // distance (around obstacles), not straight-line distance - a target on the far
                // side of a long wall might be well within straight-line range but require a much
                // longer real walk, and that should count as out of range too. TryGetWalkPath's
                // pathDistance is measured on the untrimmed path (the real distance to the target
                // itself); waypoints comes back already trimmed meleeRange short so the charge
                // stops just outside melee range instead of walking onto the target.
                // waypoints.Count >= 2 excludes the degenerate case where the user is already
                // within meleeRange - nothing to charge into, so don't fire (no cooldown wasted).
                if (TryGetWalkPath(user.position, target.position, meleeRange, out var waypoints, out float pathDistance)
                    && pathDistance <= range
                    && waypoints.Count >= 2)
                {
                    chargeWaypoints = waypoints;
                    this.target = target; //classwide access
                    StartCoroutine(ChargeCoroutine()); //execute the charge, this is a process happening over time and will hence not be completed in a single frame.
                    cd = acd; //reset cooldown
                }

            }
        }

    }

    // Invoked by AI callers via AbilityController.Invoke() (e.g. GuardBehaviour), which has no
    // target of its own to pass - resolve via the caller's own AI target instead of falling
    // through to UseMouse()'s player-only mouse-cursor lookup.
    public override void Use()
    {
        UseAITargetOrMouse();
    }

    public override void UseMouse()
    {
        user.GetComponent<UnitController>().SetSaveSpot(user.position);
        Transform t = PreciseMouseTarget();
        if (t != null) UseTarget(t);

    }

    IEnumerator ChargeCoroutine()
    {
        var rb = user.GetComponent<Rigidbody2D>();
        var movement = user.GetComponent<MovementController>();

        // Kinematic bodies are NOT the fix for the push - a kinematic body is treated as
        // immovable, so it shoves any dynamic body it collides with even harder (this was wrong
        // in the previous pass). What actually stops the push is making the charger's own
        // colliders triggers for the dash's duration, so there's no physical collision response
        // with anything at all while charging - it doesn't need one, since the destination is
        // already precomputed and the arrival check below is purely distance-based. Both the
        // kinematic toggle and the trigger toggle are restored to their exact original values
        // once the dash ends.
        var originalBodyType = rb.bodyType;
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = Vector2.zero;

        var colliders = user.GetComponents<Collider2D>();
        var originalTriggerStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            originalTriggerStates[i] = colliders[i].isTrigger;
            colliders[i].isTrigger = true;
        }

        movement.stuck = true; //disallow any other movement of the charging character

        float elapsed = 0f;
        float sinceLastEffect = trailEffectInterval; // spawn one immediately on the first step
        int waypointIndex = 0;

        // Walks the precomputed, obstacle-avoiding waypoint chain one corner at a time instead of a
        // single straight MovePosition to one fixed point. chargeWaypoints already has the
        // meleeRange-short trim applied (see TryGetWalkPath) - reaching the final waypoint IS
        // arriving, no separate stop-check needed. 0.05f corner-arrival epsilon matches the
        // original single-destination arrival check. maxChargeDuration is only a safety net in case
        // something (the target dying mid-charge) prevents ever arriving.
        while (elapsed < maxChargeDuration && waypointIndex < chargeWaypoints.Count)
        {
            Vector2 currentTarget = chargeWaypoints[waypointIndex];
            Vector2 segmentDirection = (currentTarget - rb.position).normalized;

            movement.WalkTowards(segmentDirection); // set movement animation, as default is disabled due to being stuck

            if (sinceLastEffect >= trailEffectInterval)
            {
                float rotZ = Mathf.Atan2(segmentDirection.y, segmentDirection.x) * Mathf.Rad2Deg; //determine rotation for the current path segment
                GameObject cef = Instantiate(chargeEffect, user.position, Quaternion.Euler(0f, 0f, rotZ - 90)); //instantiate effect prefab at position and rotation
                cef.transform.parent = user; // make child of the charging character so its emission point moves along with it
                Destroy(cef, 0.5f); //free up memory
                sinceLastEffect = 0f;
            }

            rb.MovePosition(Vector2.MoveTowards(rb.position, currentTarget, chargeSpeed * Time.fixedDeltaTime));

            if (Vector2.Distance(rb.position, currentTarget) <= 0.05f)
                waypointIndex++;

            elapsed += Time.fixedDeltaTime;
            sinceLastEffect += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        rb.linearVelocity = Vector2.zero;
        rb.bodyType = originalBodyType;
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].isTrigger = originalTriggerStates[i];
        movement.stuck = false;

        //after charging - only actually hits if the charge really landed within melee range, so a
        // target that fled out of reach (or something blocking the path) results in a whiffed
        // charge rather than a guaranteed hit regardless of where the charger ended up.
        if (target != null && user != null && Vector2.Distance(user.position, target.position) <= meleeRange + 0.25f)
        {
            user.GetComponent<MovementController>().LookAt(target.position);
            if (target.GetComponent<HealthController>().health > dmg * (user.GetComponent<StatusController>().lvl + user.GetComponent<StatusController>().Str))
            {
                target.GetComponent<MovementController>().Stun(stunTime);
            }
            target.GetComponent<HealthController>().TakeDamage(dmg * (user.GetComponent<StatusController>().lvl + user.GetComponent<StatusController>().Str), user);
            Camera.main.GetComponent<NeutralCam>().CamShake();

        }
    }

}