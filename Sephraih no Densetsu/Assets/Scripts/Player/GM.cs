using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The character controller that allows a player to control a warrior
public class GM : PlayerController
{
    // used in the process of determining whether the player wants to use a skill
  
    // using the skills assigned to the keys depending on input
    public override void Attack()
    {
        int skill = 1000;
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena
        if (Input.GetButtonUp("r"))
        {
            skill = 3;
        }
        if (Input.GetButtonUp("e"))
        {
            skill = 6;
        }
        if (Input.GetButtonUp("q"))
        {
            skill = 2;
        }
        if (Input.GetButtonUp("f"))
        {
            skill = 7;
        }
        if (Input.GetButtonUp("mouse0"))
        {
            skill = 1;
        }
        if (Input.GetButtonUp("mouse1"))
        {
            skill = 4;
        }
        if (Input.GetButtonUp("x"))
        {
            skill = 5;
        }


        if (skill < 1000)
        {          
         transform.GetComponentInChildren<AbilityController>().InvokeMouse(skill, transform);


        }

    }

}


/*
      //            GetComponent<FireBolt>().UseMouse();
            //GetComponent<ShadowImpact>().UseMouse();
            
            //Ability a = GetComponent<ShadowImpact>();
            //a.UseMouse();

            Camera.main.GetComponent<AbilityController>().InvokeMouse(4,transform);
 
 */