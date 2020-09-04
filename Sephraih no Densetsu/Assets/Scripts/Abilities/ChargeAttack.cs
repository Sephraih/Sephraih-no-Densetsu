using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChargeAttack : MonoBehaviour
{
    public float acd; //ability cool down
    public float range = 5.0f;
    public GameObject chargeEffect;
    public float stunTime = 1.0f;
    private float cd; //cool down remaining
    public int dmg = 250;
    
    private Vector2 chargeDirection;
    private float distanceToTarget;
    private Transform target;
    private Rigidbody2D rb;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    //run at a target, damage based on character attack*3 and stun it for a short time
    public void Charge(Transform target)
    {
        if (cd <= 0f) // if ability ready to use
        {
            //determine direction
            distanceToTarget = Vector2.Distance(transform.position, target.position);
            if (distanceToTarget <= range && distanceToTarget >= 2.0f)
            {
                chargeDirection = target.position - transform.position;
                chargeDirection.Normalize();
                this.target = target; //classwide access
                StartCoroutine(ChargeCoroutine()); //execute the charge, this is a process happening over time and will hence not be completed in a single frame.
                cd = acd; //reset cooldown
            }

        }

    }

    void Update()
    {
        if (cd >= 0)
        {
            cd -= Time.deltaTime; //decrease cooldown
        }

    }

    IEnumerator ChargeCoroutine()
    {
        float time = 0.1f;
        float count = 0.0f;
        while (count < time)
        {
            //charge animation
            float rotZ = Mathf.Atan2(chargeDirection.y, chargeDirection.x) * Mathf.Rad2Deg; //determine rotation
            GameObject cef = Instantiate(chargeEffect, transform.position, Quaternion.Euler(0f, 0f, rotZ - 90)); //instantiate effect prefab at position and rotation
            cef.transform.parent = transform; // make child of the charging character so its emission point moves along with it
            Destroy(cef, 0.5f); //free up memory


            GetComponent<MovementController>().stuck = true; //disalow any other movement of the charging character
            GetComponent<MovementController>().WalkTowards(chargeDirection); // set movement animation, as default is disabled due to being stuck
            rb.velocity = chargeDirection * 70;
            count += time;
            yield return new WaitForSeconds(time);
        }
        //after charging
        target.GetComponent<MovementController>().Stun(stunTime);
        Camera.main.GetComponent<NeutralCam>().CamShake();
        GetComponent<MovementController>().stuck = false;
        target.GetComponent<HealthController>().TakeDamage(dmg, transform);



    }

}