using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAgents;
public class WarriorAgent : BasicAgent
{

    //Skills
    private bool _q;
    private bool _e;


    //observation vector taken from parent, the basic agent class



    //action Vector
    public override void AgentAction(float[] vectorAction)
    {
        distanceToTarget = Vector2.Distance(this.transform.position, enemy.position);//required to set negative rewards based on distance
        if (GetStepCount() > 0 && GetStepCount() % 1000 == 0) GetComponent<CharacterStats>().DpSteps(GetStepCount()); ///required to set negative rewards based on distance

        // Actions -> unity documentation: By default the output from our provided PPO algorithm pre-clamps the values of vectorAction into the [-1, 1]
        Vector2 movementAction = Vector2.zero;
        movementAction.x = vectorAction[0];
        movementAction.y = vectorAction[1];

        //boolean triggers to enable the agents use abilities based on its policy
        _q = vectorAction[2] >= 0.5f ? true : false;
        if (_q)
        {
            GetComponent<MultiSlash>().Attack();
        }
        _e = vectorAction[3] >= 0.5f ? true : false;
        if (_e)
        {
            GetComponent<ChargeAttack>().Charge(enemy);
        }

        //normalize and apply the movement the agent's policy decides for the character it is in control of
        movementDirection = new Vector2(movementAction.x * 100, movementAction.y * 100);
        movementDirection.Normalize();
        msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f);
        GetComponent<MovementController>().Move(movementDirection, msi);

        //let its attack direction transform face the way of the agent's last movement (don't let it reset if the agent stand still)
        if (movementDirection != Vector2.zero)
        {
            attackingDirection.transform.localPosition = movementDirection * 0.5f;
        }

        if (distanceToTarget > 10.0f) SetReward(-0.005f); //far from enemy (being lame)
    }

}
