using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The character controller that allows a player to control a warrior
public class PlayerWarriorController : PlayerController
{


    // using the skills assigned to the keys depending on input
    public override void Attack()
    {
        if (Input.GetButtonUp("Mouse0"))
        {
            GetComponent<MultiSlash>().Use();
        }

        if (Input.GetButtonUp("Mouse1"))
        {
            GetComponent<ChargeAttack>().UseMouse();
        }
        if (Input.GetButtonUp("q"))
        {
            GetComponent<FireBolt>().UseMouse();
        }
    }
}