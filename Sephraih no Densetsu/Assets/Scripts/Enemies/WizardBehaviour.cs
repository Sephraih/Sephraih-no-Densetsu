using System.Collections;
using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

// bot behaviour attached to the wizard enemy character and prefab
public class WizardBehaviour : EnemyController
{
    
   
    public float distanceToTarget;

       
    void Start()
    {

        Camera.main.GetComponent<GameBehaviour>().Register(transform); //upon creation add to list of enemies
        teamID = GetComponent<StatusController>().teamID;
        //GetComponent<FireBolt>().acd = 1.0f; // initial cooldown of firebolt ability
        GetComponent<StatusController>().Int = 10; // set the wizard's default magical attack strenght value to 10


    }

    // each frame
    void Update()
    {
        Move();
        Aim();
        Attack();
        Die();
    }

    public override void Move()
    {

        target = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); // target is the closest player
        distanceToTarget = Vector2.Distance(transform.position, target.position); // distance to the player


        // try to be in shot range for the firebolt, stay out of reach of player's basic attacks
        if (distanceToTarget >= 15.0f && distanceToTarget <= 20.0f) // walk to target within targeting range, out of cast range
        {
            movementDirection = target.transform.position - transform.position;
        }
        if (distanceToTarget <= 10.0f && distanceToTarget > 5.0f || distanceToTarget > 20.0f) movementDirection = Vector2.zero; // don't move
        if (distanceToTarget <= 5.0f)
        {
            transform.GetComponentInChildren<AbilityController>().Invoke(6, transform);
            movementDirection = transform.position - target.transform.position; //walk away from target
        }

        movementDirection.Normalize(); // distance will not affect movement speed
        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f); // zero if still, one if moving
        GetComponent<MovementController>().Move(movementDirection, msi); // move through controller

    }

    //aim at the target


    // shoot a firebolt whenever ready and within the defined range
    public override void Attack()
    {
        if (distanceToTarget >= 3.0f && distanceToTarget <= 15.0f)
        {
            transform.GetComponentInChildren<AbilityController>().Invoke(4, transform);
        }
    }


}
