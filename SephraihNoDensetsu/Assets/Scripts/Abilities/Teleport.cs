using UnityEngine;

public class Teleport : Ability
{
    public GameObject teleportEffect; //effect to be displayed on teleport


    public override void Use()
    {
        Vector3 direction = user.transform.position - attackPos.position; // get the direction the caster is facing
        direction.Normalize(); // ignore distance
        user.GetComponent<UnitController>().SetSaveSpot(user.transform.position);
        for (float range = this.range; range > 0; range--) //shorter jump distance if location jumped at was out of boundary
        {
            Vector2 candidate = (Vector2)user.transform.position + (Vector2)direction * range;
            if (!SpellBlocked(user.transform.position, candidate)) //check for obstacle/boundary colliders along the way
            {
                if (cd <= 0f) // if ability ready to use
                {

                    user.GetComponent<MovementController>().LookAt(attackPos.position);
                    user.transform.position = user.transform.position + direction * range;
                    GameObject tef = Instantiate(teleportEffect, user.position + new Vector3(0, -0.7f, 0), Quaternion.Euler(0f, 0f, 0)); //instantiate effect prefab at position and rotation
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
        Vector2 mp = MousePosition();
        Vector2 direction = mp - new Vector2(user.transform.position.x, user.transform.position.y); // get the direction the caster is facing
        user.GetComponent<PlayerController>().SetSaveSpot(user.transform.position);

        
        float distance = direction.magnitude;
        direction.Normalize(); // ignore distance
        if (distance > range) distance = range; //set to max tp range if mouse further away
        for (float range = distance; range > 0; range--) //shorter jump distance if location jumped at was out of boundary
        {
            Vector2 candidate = (Vector2)user.transform.position + direction * range;
            if (!SpellBlocked(user.transform.position, candidate)) //check for obstacle/boundary colliders along the way
            {
                if (cd <= 0f) // if ability ready to use
                {

                    user.GetComponent<MovementController>().LookAt(mp);
                    Vector2 trpos = user.transform.position;
                    user.transform.position = trpos + direction * range;
                    GameObject tef = Instantiate(teleportEffect, user.transform.position + new Vector3(0, -0.7f, 0), Quaternion.Euler(0f, 0f, 0)); //instantiate effect prefab at position and rotation
                    //tef.transform.parent = transform; // make child of the charging character so its emission point moves along with it
                    Destroy(tef, 0.5f); //free up memory
                    cd = acd; // start cooldown
                    break;
                }
            }
        }

    }




}
