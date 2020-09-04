using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// the character controller allowing a player to control a healer
public class PlayerHealerController : PlayerController
{


    // used in the process of determining whether the player wants to use a skill
    private bool _a;
    private bool _q;
    private bool _e;

    public Transform ally;
    public Transform target;


    public override void Skills()
    {
        // attacks and skills
        _a = Input.GetButtonUp("a");
        _q = Input.GetButtonUp("q");
        _e = Input.GetButtonUp("e");
    }


    // using the skills assigned to the keys depending on input
    public override void Attack()
    {

        if (_e)
        {
            GetComponent<HealWave>().Blast();
        }
        if (_a)
        {
            GetComponent<SelfHeal>().Heal();
        }
        if (_q)
        {
            GetComponent<HealBolt>().Blast();
        }

    }

}