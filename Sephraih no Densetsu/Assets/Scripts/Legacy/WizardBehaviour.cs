using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// bot behaviour attached to the wizard enemy character and prefab
public class WizardBehaviour : MonoBehaviour
{
    
    public GameObject attackingDirection; //object used to determine attacking direction vector
    
    private Vector2 movementDirection;
    private float msi; // between zero and one determining movement strength
    public float distanceToTarget;

    private Transform target; // the derived target the wizard interacts with

    void Start()
    {
        target = Camera.main.GetComponent<CameraFollow>().target; // initial target
        GetComponent<FireBolt>().startcd = 1.0f; // initial cooldown of firebolt ability
        Camera.main.GetComponent<CameraFollow>().enemylist.Add(transform); // add to list of enemies
        GetComponent<StatusController>().matk = 100; // set the wizard's default magical attack strenght value to 100


    }

    // each frame
    void Update()
    {
        Move();
        Aim();
        Attack();
        Die();
    }

    void Move()
    {

        target = Camera.main.GetComponent<CameraFollow>().ClosestPlayer(transform); // target is the closest player
        distanceToTarget = Vector2.Distance(transform.position, target.position); // distance to the player


        // try to be in shot range for the firebolt, stay out of reach of player's basic attacks
        if (distanceToTarget >= 15.0f && distanceToTarget <= 20.0f) // walk to target within targeting range, out of cast range
        {
            movementDirection = target.transform.position - transform.position;
        }
        if (distanceToTarget <= 10.0f && distanceToTarget > 5.0f || distanceToTarget > 20.0f) movementDirection = Vector2.zero; // don't move
        if (distanceToTarget <= 5.0f)
        {
            GetComponent<Teleport>().Backjump();
            movementDirection = transform.position - target.transform.position; //walk away from target
        }

        movementDirection.Normalize(); // distance will not affect movement speed
        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f); // zero if still, one if moving
        GetComponent<MovementController>().Move(movementDirection, msi); // move through controller

    }

    //aim at the target
    void Aim()
    {
        var x = target.position - transform.position;
        x.Normalize();
        attackingDirection.transform.localPosition = x;

        //if not moving use the animator to look at the target
        if (movementDirection == Vector2.zero)
        {
            GetComponent<MovementController>().LookAt(target.position);
        }
    }

    // shoot a firebolt whenever ready and within the defined range
    void Attack()
    {
        if (distanceToTarget >= 3.0f && distanceToTarget <= 15.0f)
        {
            GetComponent<FireBolt>().Blast();
        }
    }

    // death and respawn when health is zero
    private void Die()
    {
        if (this.GetComponent<HealthController>().health <= 0)
        {
            //Instantiate((Resources.Load("Prefabs/Wizard") as GameObject), new Vector3(0, 0, 0), Quaternion.identity);
            Camera.main.GetComponent<CameraFollow>().enemylist.Remove(transform);
            Destroy(gameObject);
        }
    }

}
