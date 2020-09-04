using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// the character controller allowing a player to control a healer
public class PlayerMagicianController : PlayerController
{

    // used in the process of determining whether the player wants to use a skill
    private bool _q;
    private bool _e;
    private bool _mouse0;


    public override void Skills()
    {
        // attacks and skills
        _q = Input.GetButtonUp("q");
        _e = Input.GetButtonUp("e");
        _mouse0 = Input.GetButtonUp("mouse0");
    }

    // using the skills assigned to the keys depending on input
    public override void Attack()
    {
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena

        if (_q)
        {
            GetComponent<FireBolt>().Blast();
        }
        if (_e)
        {
            GetComponent<Teleport>().Backjump();
        }

        if (_mouse0)
        {
            GetComponent<FireBolt>().BlastMouse();
        }

    }
}