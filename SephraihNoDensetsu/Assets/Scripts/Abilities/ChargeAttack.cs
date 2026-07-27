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

    private Vector2 chargeDirection;
    private Vector2 chargeDestination;
    private float distanceToTarget;
    private Transform target;


    //run at a target, damage based on character attack*3 and stun it for a short time
    public override void UseTarget(Transform target)
    {
        if (user.GetComponent<StatusController>().teamID != target.GetComponent<StatusController>().teamID)
        {
            if (cd <= 0f) // if ability ready to use
            {
                //determine direction
                distanceToTarget = Vector2.Distance(user.position, target.position);
                if (distanceToTarget <= range) // && distanceToTarget >= 2.0f at point blank atm
                {
                    chargeDirection = target.position - user.position;
                    chargeDirection.Normalize();
                    // Stop just short of the target instead of dashing a fixed distance for a fixed
                    // duration - the old approach overshot a close target and undershot a far one,
                    // regardless of how far away it actually was.
                    chargeDestination = (Vector2)target.position - chargeDirection * meleeRange;
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

        float rotZ = Mathf.Atan2(chargeDirection.y, chargeDirection.x) * Mathf.Rad2Deg; //determine rotation
        float elapsed = 0f;
        float sinceLastEffect = trailEffectInterval; // spawn one immediately on the first step

        // Distance-based, not time-based: stop as soon as the destination is reached rather than
        // always covering a fixed distance in a fixed time. maxChargeDuration is only a safety net
        // in case something (an obstacle, the target dying mid-charge) prevents ever arriving.
        while (elapsed < maxChargeDuration && Vector2.Distance(rb.position, chargeDestination) > 0.05f)
        {
            movement.WalkTowards(chargeDirection); // set movement animation, as default is disabled due to being stuck

            if (sinceLastEffect >= trailEffectInterval)
            {
                GameObject cef = Instantiate(chargeEffect, user.position, Quaternion.Euler(0f, 0f, rotZ - 90)); //instantiate effect prefab at position and rotation
                cef.transform.parent = user; // make child of the charging character so its emission point moves along with it
                Destroy(cef, 0.5f); //free up memory
                sinceLastEffect = 0f;
            }

            rb.MovePosition(Vector2.MoveTowards(rb.position, chargeDestination, chargeSpeed * Time.fixedDeltaTime));

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