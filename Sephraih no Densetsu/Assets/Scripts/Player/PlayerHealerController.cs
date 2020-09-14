using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// the character controller allowing a player to control a healer
public class PlayerHealerController : PlayerController
{

    // using the skills assigned to the keys depending on input
    public override void Attack()
    {

        if (Input.GetButtonUp("q"))
        {
            GetComponent<HealWave>().Use();
        }
        if (Input.GetButtonUp("e"))
        {
            GetComponent<SelfHeal>().Use();
        }
        if (Input.GetButtonUp("r"))
        {
            GetComponent<HealBolt>().Use();
        }

    }

}