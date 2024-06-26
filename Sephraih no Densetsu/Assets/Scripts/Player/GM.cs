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
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena
        if (Input.GetButtonUp("r"))
        {
            GetComponent<FireStorm>().UseMouse();
        }
        if (Input.GetButtonUp("e"))
        {
            GetComponent<Teleport>().UseMouse();
        }
        if (Input.GetButtonUp("q"))
        {
            GetComponent<ChargeAttack>().UseMouse();
        }
        if (Input.GetButtonUp("f"))
        {
            GetComponent<SelfHeal>().Use();
        }
        if (Input.GetButtonUp("mouse0"))
        {
            GetComponent<MultiSlash>().Use();
        }
        if (Input.GetButtonUp("mouse1"))
        {
            //            GetComponent<FireBolt>().UseMouse();
            //GetComponent<ShadowImpact>().UseMouse();
            
            //Ability a = GetComponent<ShadowImpact>();
            //a.UseMouse();

            GetComponent<AbilityController>().UseAbility(7);
        }
    }

}