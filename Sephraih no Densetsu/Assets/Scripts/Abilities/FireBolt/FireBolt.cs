﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//a firebolt ability assignable to all characters launching a projectile that damages, burns and slows target hostile to the caster it hits
public class FireBolt : Ability
{
    private float offset = -90.0f; // default sprite rotation to align with vector.up
    //public string enemy = "Enemy";

    public GameObject projectile; //prefab
    public Transform shotPoint; // infront of character
   

    private int dmg = 50;
    
    public float slow = 0.5f; //default movement speed * slow

    // blast towards casters attacking point
    public override void Use()
    {
        Vector2 difference = transform.position - shotPoint.transform.position; //attack vector from transform to shotpoint

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg; //rotate projectile onto attack vector
        shotPoint.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Bolt();
    }

    //same use as blast but shooting towards mouse position, delay zero for testing
    public override void UseMouse()
    {
        Vector2 trpos = transform.position;
        Vector2 difference = MousePosition() - trpos; // vector from transform to mouse

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg - 180; //rotate projectile onto vector
        shotPoint.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Bolt(); 

    }

    // general blast logic
    public void Bolt()
    {
        //check whether the ability is ready to use
        if (cd <= 0)
        {
            //instantiate and assign values to a firebolt projectile, which handles damaging, position and collision logic based on the fireboltprojectile script attached to it.
            var bolt = Instantiate(projectile, transform.position, shotPoint.transform.rotation);
            bolt.GetComponent<FireBoltProjectile>().user = transform;
            bolt.GetComponent<FireBoltProjectile>().dmg = dmg * (GetComponent<StatusController>().lvl + transform.GetComponent<StatusController>().Int); //+= this.GetComponent<StatusController>().matk;
            //bolt.GetComponent<FireBoltProjectile>().dotd += this.GetComponent<StatusController>().matk;
            //bolt.GetComponent<FireBoltProjectile>().slow = slow;


            cd = acd;

        }

    }
    //same as Blast() but with a vector direction
    public void BlastVec(Vector2 dir)
    {

        dir += new Vector2(transform.position.x, transform.position.y); //move directionvectorcenter to character
        Vector2 difference = new Vector2(dir.x - transform.position.x, dir.y - transform.position.y); // vector from transform dir

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg - 180; //rotate projectile onto vector
        shotPoint.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Bolt();

    }

    //same as Blast() but with a character target
    public void BlastTarget(Transform target)
    {
        Vector2 dir = new Vector2(target.localPosition.x, target.localPosition.y);
        Vector2 difference = new Vector2(dir.x - transform.localPosition.x, dir.y - transform.localPosition.y); // vector from transform dir

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg - 180; //rotate projectile onto vector
        shotPoint.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Bolt();

    }

    public void BlastAngle(float angle)
    {
        //angle must be between -1 and 1 (mlagents clamps vectoraction space to -1 to 1)
        angle *= 180;

        shotPoint.transform.rotation = Quaternion.Euler(0f, 0f, angle - offset);
        Bolt();

    }

}


