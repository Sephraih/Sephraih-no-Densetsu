using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//a firebolt ability assignable to all characters launching a projectile that damages, burns and slows target hostile to the caster it hits
public class FireStorm : Ability
{
    
    public GameObject projectile; //prefab
 
    private int dmg = 50;

    public float slow = 0.5f; //default movement speed * slow


    //same use as blast but shooting towards mouse position, delay zero for testing
    public override void UseMouse()
    {
        if (range == 0){ range = 7; }
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        float distance = Vector3.Distance(transform.position,mousePosition);
        Mathf.Abs(distance);
        //check whether the ability is ready to use
        if (cd <= 0 && distance <= range)
        {
            //instantiate and assign values to a firebolt projectile, which handles damaging, position and collision logic based on the fireboltprojectile script attached to it.
            var bolt = Instantiate(projectile, mousePosition + new Vector3(0, 0, 10), Quaternion.Euler(0f, 0f, 0f)); //-0.75 for animation offset
            bolt.GetComponent<FireStormEffect>().user = transform;
            bolt.GetComponent<FireStormEffect>().dmg = dmg * (GetComponent<StatusController>().lvl + transform.GetComponent<StatusController>().Int);

            cd = acd;

        }

    }


}


