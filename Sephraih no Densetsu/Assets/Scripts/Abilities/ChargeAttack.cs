using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChargeAttack : Ability
{
    public GameObject chargeEffect;
    public float stunTime = 1.0f;
    private int dmg = 80;

    private Vector2 chargeDirection;
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
                    this.target = target; //classwide access
                    StartCoroutine(ChargeCoroutine()); //execute the charge, this is a process happening over time and will hence not be completed in a single frame.
                    cd = acd; //reset cooldown
                }

            }
        }

    }

    public override void UseMouse()
    {
        user.GetComponent<UnitController>().SetSaveSpot(user.position);
        Transform t = Camera.main.GetComponent<GameBehaviour>().ClosestEnemyToLocation(MousePosition(), user);
        UseTarget(t);

    }
    
    IEnumerator ChargeCoroutine()
    {
        float time = 0.1f;
        float count = 0.0f;
        while (count < time)
        {
            //charge animation
            float rotZ = Mathf.Atan2(chargeDirection.y, chargeDirection.x) * Mathf.Rad2Deg; //determine rotation
            GameObject cef = Instantiate(chargeEffect, user.position, Quaternion.Euler(0f, 0f, rotZ - 90)); //instantiate effect prefab at position and rotation
            cef.transform.parent = user; // make child of the charging character so its emission point moves along with it
            Destroy(cef, 0.5f); //free up memory

            
            user.GetComponent<MovementController>().stuck = true; //disalow any other movement of the charging character
            user.GetComponent<MovementController>().WalkTowards(chargeDirection); // set movement animation, as default is disabled due to being stuck
            user.GetComponent<Rigidbody2D>().velocity = chargeDirection * 70;
            count += time;
            yield return new WaitForSeconds(time);
        }
        //after charging
        if (target != null)
        {
            user.GetComponent<MovementController>().LookAt(target.position);
            if (target.GetComponent<HealthController>().health > dmg * (user.GetComponent<StatusController>().lvl + user.GetComponent<StatusController>().Str))
            {
                target.GetComponent<MovementController>().Stun(stunTime);
            }
            target.GetComponent<HealthController>().TakeDamage(dmg * (user.GetComponent<StatusController>().lvl + user.GetComponent<StatusController>().Str), user);
            Camera.main.GetComponent<NeutralCam>().CamShake();
            
        }
        user.GetComponent<MovementController>().stuck = false;
    }

}