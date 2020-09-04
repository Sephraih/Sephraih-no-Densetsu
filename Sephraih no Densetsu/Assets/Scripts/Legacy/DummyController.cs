using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// controller attached to the basic enemy and its prefab
public class DummyController : MonoBehaviour
{

    public Animator animator; // animation
    public GameObject attackingDirection; //attacking direction object used to calculate vector of attack

    public Vector2 movementDirection; // direction of movement
    private float msi; // movement speed input, in the case of a bot this is either zero or one


    private Rigidbody2D rb; // physics entity
    public Transform player; // the player character the bot interacts with

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
       // Camera.main.GetComponent<camerafollow>().enemylist.Add(transform); //upon creation add to list of enemies
    }

    void Update()
    {
        // the dummy only has to reset upon death
      //  Move();
      // Aim();
      //  Attack();
        Die();
    }

    void Move()
    {

        player = Camera.main.GetComponent<CameraFollow>().ClosestPlayer(transform); // the player this bot interacts with is the one closest to it

        movementDirection = new Vector2(-1 * (rb.position.x - player.transform.position.x), -1 * (rb.position.y - player.transform.position.y)); // move towards the player
        movementDirection.Normalize(); // normalized so distance doesnt influence movement speed

        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f); // zero or one depending on whether the bot moves not. it always moves.
        GetComponent<MovementController>().Move(movementDirection, msi); // move using the controller
    }

    // aim towards movement direction
    void Aim()
    {
        if (movementDirection != Vector2.zero)
        {
            attackingDirection.transform.localPosition = movementDirection;
        }
    }

    //attack if close to the target player
    void Attack()
    {
        if (Vector2.Distance(transform.position, player.position) < 1.0f)
        {
            this.GetComponent<BasicAttack>().Attack();
        }
    }

    // die and respawn at fixed location (testing) when health is at or below zero
    private void Die()
    {
        if (this.GetComponent<HealthController>().health <= 0)
        {
            this.GetComponent<HealthController>().Max();
            player.GetComponent<PlayerAgent>().SetReward(1.0f);
            player.GetComponent<PlayerAgent>().Done();
        }
    }

}
