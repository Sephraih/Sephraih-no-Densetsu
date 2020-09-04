using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//bot behaviour attached to the "guard" type enemy character and its prefab
public class GuardBehaviour : MonoBehaviour
{

    public GameObject attackingDirection; // object required to define attacking direction

    // definition of the guard spot and chase radius
    public Vector3 guardSpot = new Vector3(5.0f, 5.0f, 0f);
    public float guardMaxChaseRadius = 25.0f;
    public float guardRadius = 5.0f;

    // variables for movement
    private Vector2 movementDirection;
    private float msi;

    // required for attacking, following and guard logic
    private float distanceToTarget;
    private float distanceToGuardSpot;
    private bool returning = false;



    private Transform player; // target player

    //initialization
    void Start()
    {
        transform.position = guardSpot;
        player = Camera.main.GetComponent<CameraFollow>().target; // target the active player at start
        GetComponent<FireBolt>().startcd = 5.0f; // define frequency of firebolt ability usage
        Camera.main.GetComponent<CameraFollow>().enemylist.Add(transform); // add to list of enemies
    }

    void Update()
    {
        Move();
        Aim();
        Attack();
        Die();
    }

    void Move()
    {

        player = Camera.main.GetComponent<CameraFollow>().ClosestPlayer(transform); // interact with closest player determined each frame

        distanceToTarget = Vector2.Distance(transform.position, player.position);
        distanceToGuardSpot = Vector2.Distance(transform.position, guardSpot);

        // chase the player within guard radius and not outside of the maximal chase radius, follow it
        if (distanceToTarget <= guardRadius && distanceToGuardSpot < guardMaxChaseRadius && !returning)
        {
            movementDirection = player.transform.position - transform.position;
            if (distanceToTarget < 1.0f) { movementDirection = Vector2.zero; }
        }
        else // return to the spot if not there
        {
            returning = true;
            movementDirection = guardSpot - transform.position;

            if (distanceToGuardSpot < 0.5f) { movementDirection = Vector2.zero; }
            if (distanceToGuardSpot < guardRadius) { returning = false; }
        }

        // tell the movement controller where to move to
        movementDirection.Normalize(); //distance not to affect speed of movement
        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f);
        GetComponent<MovementController>().Move(movementDirection, msi);

    }

    // aim towards movement direction if moving, else, if within sight aim at the closest player
    void Aim()
    {
        if (movementDirection != Vector2.zero)
        {
            attackingDirection.transform.localPosition = movementDirection;
        }
        else if (distanceToTarget < 20.0f)
        {

            var x = player.position - transform.position;
            x.Normalize();
            attackingDirection.transform.localPosition = x;
            GetComponent<MovementController>().LookAt(player.position);
        }
    }

    // basic attack if next to enemy, charge if within charge range, firebolt if within sight and out of chase radius
    void Attack()
    {
        if (distanceToTarget < 1.0f)
        {
            this.GetComponent<BasicAttack>().Attack();
        }
        else if (distanceToTarget < guardRadius && distanceToGuardSpot <= guardMaxChaseRadius)
        {
            this.GetComponent<ChargeAttack>().Charge(player);
        }

        if (distanceToTarget > 5.0f && distanceToTarget < 10.0f)
        {
            GetComponent<FireBolt>().Blast();
        }
    }

    // handle death and respawn
    private void Die()
    {
        if (this.GetComponent<HealthController>().health <= 0)
        {
            //Instantiate((Resources.Load("Prefabs/Guard") as GameObject), new Vector3(0, 7, 0), Quaternion.identity);
            Camera.main.GetComponent<CameraFollow>().enemylist.Remove(transform);
            Destroy(gameObject);
        }
    }

}
