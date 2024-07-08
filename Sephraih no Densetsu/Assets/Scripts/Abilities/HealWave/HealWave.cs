using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//healwave ability launching healwave projectiles
public class HealWave : Ability
{
    private readonly float offset = -90.0f; //make the sprite and particle system face upwards
    
    public GameObject projectile; // the healwave projectile prefab is attached to the editor in this public field
    
    private int healAmount = 40;

    private void Start()
    {
    }

    //used to fire the projectile in the way the character is facing
    public override void Use()
    {

        attackPos = user.transform.GetChild(0);
        Vector2 difference = user.transform.position - attackPos.transform.position;

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;
        attackPos.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Wave();

    }
    

    //same use as blast but shooting towards mouse position
    public override void UseMouse()
    {


        attackPos = user.transform.GetChild(0);
        Vector2 difference = Camera.main.ScreenToWorldPoint(Input.mousePosition) - user.transform.position;

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg - 180;
        attackPos.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Wave();
    }

    //same as blaset but at a specified character target
    public void BlastTarget(Transform target)
    {

        Vector2 dir = new Vector2(target.localPosition.x, target.localPosition.y);
        Vector2 difference = new Vector2(dir.x - user.localPosition.x, dir.y - user.localPosition.y); // vector from transform dir

        float rotZ = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg - 180; //rotate projectile onto vector
        attackPos.transform.rotation = Quaternion.Euler(0f, 0f, rotZ - offset);
        Wave();

    }

    public void Wave()
    {

        //check whether ability cooldown is ready
        if (cd <= 0)
        {

            var bolt = Instantiate(projectile, user.position, attackPos.transform.rotation);
            bolt.GetComponent<HealWaveProjectile>().healAmount = healAmount * (user.GetComponent<StatusController>().lvl + user.GetComponent<StatusController>().Int); //this.GetComponent<StatusController>().matk;
            bolt.GetComponent<HealWaveProjectile>().user = user;

            cd = acd;

        }
    }

}


