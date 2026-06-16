using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// controller attached to the basic enemy and its prefab
public class EnemyController : UnitController
{

    protected Transform target; // the character the bot interacts with



    // die and respawn at fixed location (testing) when health is at or below zero
    protected void Die()
    {
        if (this.GetComponent<HealthController>().health <= 0)
        {

            //Instantiate((Resources.Load("Prefabs/Enemy") as GameObject), new Vector3(0, 0, 0), Quaternion.identity);
            Camera.main.GetComponent<GameBehaviour>().Remove(transform);
            Destroy(gameObject);
        }
    }

    public new void Aim()
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

}
