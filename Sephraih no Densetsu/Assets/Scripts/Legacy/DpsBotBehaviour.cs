using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// the behaviour of the first character if it is a bot
public class DpsBotBehaviour : MonoBehaviour
{


    public Rigidbody2D rb; // physics entity
    public Animator animator; // to animate the character
    public Vector2 movementDirection; // calculated movement direction
    public float movementSpeedInput; // msi from zero to one deciding whether the character is moving or not
    
    // object used to calculate the direction of attack
    public GameObject attackingDirection;

    // target character to interact with
    Transform target;

    // called each frame
    void Update()
    {
        if (transform != Camera.main.GetComponent<CameraFollow>().target) // bot logic applies if not active character
        {

            target = Camera.main.GetComponent<CameraFollow>().ClosestEnemy(transform); // get the closest enemy
            if (target && target != Camera.main.GetComponent<CameraFollow>().dummy)
            {
                Move();
                Aim();
                UseSkills();
            }
            else { GetComponent<MovementController>().Idle(); }
        }

    }



    //move player based on input and play movement animation
    void Move()
    {

        movementDirection = target.transform.position - transform.position; //move towards target
        movementDirection.Normalize(); // filter distance
        
        if (Vector2.Distance(transform.position, target.position) < 1.0f) { movementSpeedInput = 0.5f; } // slow down movement if close to target
        movementSpeedInput = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f); // zero if still, one if moving
        GetComponent<MovementController>().Move(movementDirection, movementSpeedInput); // move through controller

    }
        

    // set attacking direction object's position towards movement direction of character
    void Aim()
    {
        if (movementDirection != Vector2.zero)
        {
            attackingDirection.transform.localPosition = movementDirection * 0.5f;
        }
    }
    
    // use basic attack (multislash) if close to the target
    void UseSkills()
    {
        if (Vector2.Distance(transform.position, target.position) < 1.0f)
        {
            this.GetComponent<MultiSlash>().Attack();
        }
    }



}
