using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class MobController : EnemyController
{
    // Start is called before the first frame update
    public void Start()
    {
        Camera.main.GetComponent<GameBehaviour>().Register(transform); //upon creation add to list of enemies
        teamID = GetComponent<StatusController>().teamID;
    }


    void Update()
    {
        if (!GetComponent<MovementController>().stunned)
        {
            Move();
            Skills();
            Aim();
            Attack();
        }
        Die();
    }
    public override void Move()
    {

        target = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); // the player this bot interacts with is the one closest to it

        movementDirection = new Vector2(-1 * (transform.position.x - target.transform.position.x), -1 * (transform.position.y - target.transform.position.y)); // move towards the player
        movementDirection.Normalize(); // normalized so distance doesnt influence movement speed

        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f); // zero or one depending on whether the bot moves not. it always moves.
        GetComponent<MovementController>().Move(movementDirection, msi); // move using the controller
    }


    //attack if close to the target player
    public override void Attack()
    {
        if (Vector2.Distance(transform.position, target.position) < 1.0f)
        {
            this.GetComponent<BasicAttack>().Attack();
        }
    }


}
