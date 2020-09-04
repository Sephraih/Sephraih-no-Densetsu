using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAgents;
public class HealerAgent : BasicAgent
{

    public Transform ally;
    public Transform target;
    //Skills
    private bool _q;
    private bool _e;
    private bool _a;
    //targeting boolean
    private bool _t;


    //observation Vector
    public override void CollectObservations()
    {
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena if available
        ally = Camera.main.GetComponent<GameBehaviour>().ClosestAlly(transform, ally); //get closest ally inside arena
        
        //observe arena-relative position of the enemy and this agent
        AddVectorObs(target.localPosition.x);
        AddVectorObs(target.localPosition.y);
        AddVectorObs(transform.localPosition.x);
        AddVectorObs(transform.localPosition.y);
    }

    //action Vector
    public override void AgentAction(float[] vectorAction) //action vector size defined in the unity inspector
    {

        distanceToTarget = Vector2.Distance(this.transform.position, ally.position); //required to set negative rewards based on distance
        if (GetStepCount() > 0 && GetStepCount() % 1000 == 0) GetComponent<CharacterStats>().DpSteps(GetStepCount()); ///required to set negative rewards based on distance
        // Actions -> unity documentation: By default the output from our provided PPO algorithm pre-clamps the values of vectorAction into the [-1, 1]
        Vector2 movementAction = Vector2.zero;
        movementAction.x = vectorAction[0];
        movementAction.y = vectorAction[1];

        //boolean trigger to let the agent choose its target
        _t = vectorAction[2] >= 0f ? true : false;
        if (_t)
        {
            target = enemy;
        }
        else { target = ally; }

        //boolean triggers to enable the agents use abilities based on its policy
        _e = vectorAction[3] >= 0.5f ? true : false;
        if (_e)
        {
            GetComponent<HealWave>().BlastTarget(target);
        }
        _a = vectorAction[4] >= 0.5f ? true : false;
        if (_a)
        {
            GetComponent<SelfHeal>().Heal();
        }
        _q = vectorAction[5] >= 0.5f ? true : false;
        if (_q)
        {
            GetComponent<HealBolt>().BlastTarget(target);
            if (target == enemy) { AddReward(-0.05f); } // negative reward if the healer shoots its HealBolt at an ally
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


        if (distanceToTarget > 20.0f) SetReward(-0.005f); //far from enemy (being lame)
    }

}