using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The character controller that allows a player to control a warrior
public class PlayerWarriorController : PlayerController
{
    // used in the process of determining whether the player wants to use a skill
    private bool _a;
    private bool _e;
    private bool _q;

    public override void Skills()
    {
        // attacks and skills
        _a = Input.GetButtonUp("a");
        _e = Input.GetButtonUp("e");
        _q = Input.GetButtonUp("q");
    }

    // using the skills assigned to the keys depending on input
    public override void Attack()
    {
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena
        if (_a)
        {
            GetComponent<MultiSlash>().Attack();
        }

        if (_e)
        {
            GetComponent<ChargeAttack>().Charge(enemy);
        }
        if (_q)
        {
            GetComponent<FireStorm>().Use();
        }
    }
}