using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// the character controller of character one (and three, which is an identical copy)
public class PlayerController : MonoBehaviour
{


    public Vector2 movementDirection; // from input based on x and y axis
    public float msi; // strength of input between zero and one

    public Transform enemy;
    public GameObject arena;
    public int teamID;


    public GameObject attackingDirection; // object used to calculate a vector of attack

    // called once
    private void Start()
    {
        attackingDirection.transform.localPosition = new Vector2(0, -0.5f); // set an attacking direction before the player moves for the first time
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena
        Camera.main.GetComponent<GameBehaviour>().Register(transform);
        GetComponent<StatusController>().teamID = teamID;
    }

    // called each frame
    void Update()
    {
        Move();
        Skills();
        Aim();
        Attack();
        Reset();
    }

    //process movement input
    void Move()
    {
        // movement based on input
        movementDirection = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        movementDirection.Normalize();
        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f);
        GetComponent<MovementController>().Move(movementDirection, msi);
    }

    public virtual void Skills() { 
    
    }
    
    // set attacking direction object's position
    void Aim()
    {
        //position the attacking direction object infront of the character, keep position when it stops moving
        if (movementDirection != Vector2.zero)
        {
            attackingDirection.transform.localPosition = movementDirection * 0.5f;
        }
    }

    public virtual void Attack() { }


    void Reset()
    {
        if (GetComponent<HealthController>().health <= 0)
        {
            GetComponent<HealthController>().Max();
            GetComponent<BasicAgent>().ResetPosition(transform);
        }
    }
}