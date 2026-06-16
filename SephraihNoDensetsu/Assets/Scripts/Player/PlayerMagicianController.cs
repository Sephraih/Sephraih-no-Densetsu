using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// the character controller allowing a player to control a healer
public class PlayerMagicianController : PlayerController
{

    
    // using the skills assigned to the keys depending on input
    public override void Attack()
    {
        
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
            GetComponent<FireBolt>().UseMouse();
        }

    }
}