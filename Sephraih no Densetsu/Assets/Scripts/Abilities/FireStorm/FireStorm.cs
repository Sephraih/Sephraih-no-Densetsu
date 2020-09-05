using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//a firebolt ability assignable to all characters launching a projectile that damages, burns and slows target hostile to the caster it hits
public class FireStorm : MonoBehaviour
{
    
    public GameObject projectile; //prefab
    public Transform user;

    private float cd; //remaining cooldown
    public float startcd; //ability cooldown
    public int dmg = 50;

    public float slow = 0.5f; //default movement speed * slow

    //reduce cooldown each frame based on time passed
    private void Update()
    {
        cd -= Time.deltaTime;

    }

    //same use as blast but shooting towards mouse position, delay zero for testing
    public void Use()
    {

        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        

        //check whether the ability is ready to use
        if (cd <= 0)
        {
            //instantiate and assign values to a firebolt projectile, which handles damaging, position and collision logic based on the fireboltprojectile script attached to it.
            var bolt = Instantiate(projectile, mousePosition + new Vector3(0, 0, 10), Quaternion.Euler(0f, 0f, 0f)); //-0.75 for animation offset
            bolt.GetComponent<FireStormEffect>().user = user;
            bolt.GetComponent<FireStormEffect>().dmg = dmg;

            cd = startcd;

        }

    }


}


