using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Teleport : Ability
{
    public GameObject teleportEffect; //effect to be displayed on teleport
    public Transform attackPos; // object to determine the direction
    public LayerMask boundaries; // all objects that act as game world boundaries belong to this layermask

    
    public override void Use()
    {
        Vector3 direction = transform.position - attackPos.position; // get the direction the caster is facing
        direction.Normalize(); // ignore distance

        for (float range = this.range; range > 0; range--) //shorter jump distance if location jumped at was out of boundary
        {
            RaycastHit2D hitInfo = Physics2D.Raycast(transform.position, direction, range, boundaries); //check for boundary colliders
            if (hitInfo.collider == null)
            {
                if (cd <= 0f) // if ability ready to use
                {
                    transform.position = transform.position + direction * range;
                    GameObject tef = Instantiate(teleportEffect, transform.position + new Vector3(0,-0.7f,0), Quaternion.Euler(0f, 0f, 0)); //instantiate effect prefab at position and rotation
                    //tef.transform.parent = transform; // make child of the charging character so its emission point moves along with it
                    Destroy(tef, 0.5f); //free up memory

                    cd = acd; // start cooldown
                    break;
                }
            }
        }


    }

    public override void UseMouse()
    {
        Vector2 direction = MousePosition() - new Vector2(transform.position.x,transform.position.y); // get the direction the caster is facing
        direction.Normalize(); // ignore distance

        for (float range = this.range; range > 0; range--) //shorter jump distance if location jumped at was out of boundary
        {
            RaycastHit2D hitInfo = Physics2D.Raycast(transform.position, direction, range, boundaries); //check for boundary colliders
            if (hitInfo.collider == null)
            {
                if (cd <= 0f) // if ability ready to use
                {
                    Vector2 trpos = transform.position;
                    transform.position = trpos + direction * range;
                    GameObject tef = Instantiate(teleportEffect, transform.position + new Vector3(0, -0.7f, 0), Quaternion.Euler(0f, 0f, 0)); //instantiate effect prefab at position and rotation
                    //tef.transform.parent = transform; // make child of the charging character so its emission point moves along with it
                    Destroy(tef, 0.5f); //free up memory

                    cd = acd; // start cooldown
                    break;
                }
            }
        }

    }




}
