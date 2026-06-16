using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//an ability launching a projectile healing characters friendly to the caster, see documentation of firebolt
public class HealBolt : Ability
{
    private readonly float offset = -90.0f;
    

    public GameObject projectile;

    private int healAmount =70;


    public override void Use()
    {

        attackPos = user.transform.GetChild(0);
        Vector2 difference = user.transform.position - attackPos.transform.position;

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;
        attackPos.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Bolt();

    }



    //same use as blast but shooting towards mouse position, delay zero for testing
    public override void UseMouse()
    {

        attackPos = user.transform.GetChild(0);
        Vector2 difference = Camera.main.ScreenToWorldPoint(Input.mousePosition) - user.transform.position;

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg - 180;
        attackPos.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Bolt();

    }
    //same as blast but at a specified target
    public void BlastTarget(Transform target)
    {

        Vector2 dir = new Vector2(target.localPosition.x, target.localPosition.y);
        Vector2 difference = new Vector2(dir.x - user.localPosition.x, dir.y - user.localPosition.y); // vector from transform dir

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg - 180; //rotate projectile onto vector
        attackPos.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Bolt();

    }

    public void Bolt() {

        // chech whether the ability is ready to use
        if (cd <= 0)
        {
            //instantiate projectile and assign values
            var bolt = Instantiate(projectile, user.position, attackPos.transform.rotation);
            bolt.GetComponent<HealBoltProjectile>().heal = healAmount; //this.GetComponent<StatusController>().matk;
            bolt.GetComponent<HealBoltProjectile>().user = user;

            cd = acd;

        }
    }

}


