using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChargeAttack : Ability
{
    public GameObject chargeEffect;
    public float chargeSpeed = 70f;
    // AOE overlap-check radius, roughly matching the user's own collider - used for the in-transit
    // hit check every step of the dash, and (via impactRadiusMultiplier) the impact check on arrival.
    // The dash travels the full distance to the destination point now (no separate arrival-trim
    // buffer) - passing straight through/over anyone in the way is the point, and the charger's own
    // colliders stay trigger for the whole dash (see ChargeCoroutine), so there's no physical shove
    // to guard against by stopping short anymore.
    public float hitRadius = 0.6f;
    // The impact AOE at the destination uses hitRadius * this - bigger than the in-transit circle so
    // landing near (not just exactly on top of) where a target used to be still reads as a real
    // impact, not a graze.
    public float impactRadiusMultiplier = 1.5f;
    public float maxChargeDuration = 1.0f; // safety net only - normal charges finish well before this
    public float trailEffectInterval = 0.1f; // how often the trail particle spawns during the dash

    [Header("Hit while dashing through (small circle, checked every step)")]
    public int pathDmg = 80;
    public float pathStunTime = 1.0f;

    [Header("Hit in the AOE at the destination (bigger circle, checked once on arrival)")]
    public int impactDmg = 80;
    public float stunTime = 1.0f;

    private List<Vector2> chargeWaypoints;
    // Tracks who's already been hit THIS charge, across both the in-transit and impact checks -
    // built fresh per ChargeCoroutine call. A target is only ever hit once per charge, whichever
    // check catches them first; this is what lets multiple different enemies all be hit in one
    // charge (each gets their own single entry) without any one of them taking damage repeatedly
    // from lingering inside the small radius across several FixedUpdate steps.
    private HashSet<Collider2D> hitThisCharge;

    // Charge attack is now a charge to a LOCATION, not a homing charge at a target Transform -
    // deliberately dodgeable by moving out of the way after the charge starts, since the destination
    // is fixed the instant the charge begins and never tracks the target afterward.
    //
    // NPC callers (see Use() -> UseAITargetOrMouse() -> this) still pick their target the exact same
    // way they always have (EnemyController.CurrentTarget) - only its POSITION at this instant is
    // captured below ("where the enemy is on use"), not the Transform itself, so this can't
    // accidentally keep homing on a moving target the way the old Transform-based version did.
    public override void UseTarget(Transform target)
    {
        ChargeToLocation(target.position);
    }

    // Invoked by AI callers via AbilityController.Invoke() (e.g. GuardBehaviour), which has no
    // target of its own to pass - resolve via the caller's own AI target instead of falling
    // through to UseMouse()'s player-only mouse-cursor lookup.
    public override void Use()
    {
        UseAITargetOrMouse();
    }

    // Player entry point: charges to wherever the mouse currently points, not to whatever unit (if
    // any) happens to be under the cursor - a target standing there is only hit if they're still
    // within the AOE by the time the charge actually reaches that point (or gets caught in the
    // dash's own path), same as an NPC's charge can now be sidestepped by moving away in time.
    public override void UseMouse()
    {
        user.GetComponent<UnitController>().SetSaveSpot(user.position);
        ChargeToLocation(MousePosition());
    }

    void ChargeToLocation(Vector2 point)
    {
        if (cd > 0f) return; // if ability not ready to use

        // Charge attack is "walk to destination", not a teleport - it must obey the same
        // obstacle rules as ordinary walking, and its range must reflect the TRUE walking
        // distance (around obstacles), not straight-line distance - a point on the far side of a
        // long wall might be well within straight-line range but require a much longer real walk,
        // and that should count as out of range too. No trim (0f) - the dash now travels the full
        // distance to the point itself, see hitRadius's own comment. waypoints.Count >= 2 excludes
        // the degenerate case where the user is already right on top of the point - nothing to
        // charge into, so don't fire (no cooldown wasted).
        if (TryGetWalkPath(user.position, point, 0f, out var waypoints, out float pathDistance)
            && pathDistance <= range
            && waypoints.Count >= 2)
        {
            chargeWaypoints = waypoints;
            StartCoroutine(ChargeCoroutine());
            cd = acd; //reset cooldown
        }
    }

    IEnumerator ChargeCoroutine()
    {
        hitThisCharge = new HashSet<Collider2D>();

        var rb = user.GetComponent<Rigidbody2D>();
        var movement = user.GetComponent<MovementController>();

        // Kinematic bodies are NOT the fix for the push - a kinematic body is treated as
        // immovable, so it shoves any dynamic body it collides with even harder (this was wrong
        // in the previous pass). What actually stops the push is making the charger's own
        // colliders triggers for the dash's duration, so there's no physical collision response
        // with anything at all while charging - it doesn't need one, since hit detection below is
        // its own explicit overlap-circle check, not physical collision response.
        //
        // Both locks are reference-counted on MovementController itself (BeginKinematicLock/
        // BeginPassThrough), NOT saved/restored locally here anymore - a local save-and-restore
        // used to be exactly what this did, but that broke the instant something ELSE ALSO toggled
        // this same Rigidbody2D concurrently (namely Stun: a guard's ChargeAttack stunning the
        // player mid-dash raced with the player's own dash restoring bodyType here, and whichever
        // finished last permanently stranded the player Kinematic - free to walk through walls).
        // See MovementController's own comment on BeginKinematicLock for the full story.
        movement.BeginKinematicLock();
        movement.BeginPassThrough();

        movement.stuck = true; //disallow any other movement of the charging character

        float elapsed = 0f;
        float sinceLastEffect = trailEffectInterval; // spawn one immediately on the first step
        int waypointIndex = 0;
        // Last non-degenerate travel direction, used to set final facing once the dash ends (see
        // below) - WalkTowards drives the Animator's moveX/moveY every step while movement.stuck
        // suppresses the player's own normal per-frame facing update, but nothing set a FINAL facing
        // once stuck went back to false, so the character kept whatever moveX/moveY happened to read
        // at that exact instant - confirmed live as reverting to a default/south-facing look right
        // after the charge, not the charge's own direction. Previously this was masked by an explicit
        // LookAt(target.position) at the end - now that the destination is a location rather than a
        // live target Transform, that same call would aim at (roughly) the charger's own now-current
        // position (a near-zero vector, since the dash no longer stops short of the destination),
        // so facing the actual direction of travel instead is both correct and necessary now.
        Vector2 lastDirection = Vector2.zero;

        // Walks the precomputed, obstacle-avoiding waypoint chain one corner at a time instead of a
        // single straight MovePosition to one fixed point. Reaching the final waypoint IS arriving,
        // no separate stop-check needed. 0.05f corner-arrival epsilon matches the original single-
        // destination arrival check. maxChargeDuration is only a safety net in case something
        // prevents ever arriving.
        while (elapsed < maxChargeDuration && waypointIndex < chargeWaypoints.Count)
        {
            Vector2 currentTarget = chargeWaypoints[waypointIndex];
            Vector2 segmentDirection = (currentTarget - rb.position).normalized;
            if (segmentDirection.sqrMagnitude > 0.0001f) lastDirection = segmentDirection;

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

            // In-transit AOE: a small circle roughly matching the user's own collider, checked every
            // step so anyone the charger physically passes over/through gets caught, not just
            // whoever's standing at the final destination. Multiple different enemies can each be
            // hit this way in a single charge.
            CheckOverlap(rb.position, hitRadius, pathDmg, pathStunTime);

            if (Vector2.Distance(rb.position, currentTarget) <= 0.05f)
                waypointIndex++;

            elapsed += Time.fixedDeltaTime;
            sinceLastEffect += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        rb.linearVelocity = Vector2.zero;
        movement.EndKinematicLock();
        movement.EndPassThrough();
        movement.stuck = false;

        // Face the direction the charge actually traveled, not any specific target - LookAt just
        // needs SOME point further along that direction from the current position, since it only
        // reads the normalized difference; rb.position + lastDirection works regardless of the
        // dash's actual length.
        if (lastDirection != Vector2.zero)
            movement.LookAt(rb.position + lastDirection);

        // Impact AOE at the destination, on arrival - bigger than the in-transit circle (see
        // impactRadiusMultiplier), separately tunable from the in-transit hit above.
        CheckOverlap(rb.position, hitRadius * impactRadiusMultiplier, impactDmg, stunTime);
    }

    // Shared hit-scan for both the in-transit and impact checks - same team/trigger/tag convention
    // used by every other AOE-style ability in this project (FireBolt/FireStorm/HealWave
    // projectiles: every unit is tagged "Player" regardless of team, with two colliders each - only
    // the trigger one is the real hurtbox). A target already in hitThisCharge is skipped so lingering
    // inside the radius across several steps (or being caught by both checks) can't apply the same
    // hit twice.
    void CheckOverlap(Vector2 center, float radius, int damage, float stun)
    {
        var myStatus = user.GetComponent<StatusController>();
        var hits = Physics2D.OverlapCircleAll(center, radius);
        foreach (var col in hits)
        {
            if (col.transform == user || !col.isTrigger || !col.CompareTag("Player")) continue;
            if (hitThisCharge.Contains(col)) continue;

            var status = col.GetComponent<StatusController>();
            if (status == null || status.teamID == myStatus.teamID) continue;

            var health = col.GetComponent<HealthController>();
            if (health == null) continue;

            hitThisCharge.Add(col);

            int totalDmg = damage * (myStatus.lvl + myStatus.Str);
            if (health.health > totalDmg)
                col.GetComponent<MovementController>().Stun(stun);
            health.TakeDamage(totalDmg, user);
            Camera.main.GetComponent<NeutralCam>().CamShake();
        }
    }

}
