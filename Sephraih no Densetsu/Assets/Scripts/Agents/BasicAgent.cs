using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAgents;

public class BasicAgent : Agent
{

    public Transform enemy;
    public float distanceToTarget;
    public float targetHealth;

    public Vector2 movementDirection;
    public float msi;
    public float agentHealth;


    public int posResetRndScale = 14;
    public GameObject arena;
    public int teamID;



    public GameObject attackingDirection; // object used to calculate a vector of attack

    void Start()
    {
        GetComponent<StatusController>().AIControlled = true;
        targetHealth = enemy.GetComponent<HealthController>().maxHealth;
        agentHealth = GetComponent<HealthController>().maxHealth;
        Camera.main.GetComponent<GameBehaviour>().Register(transform);
        GetComponent<StatusController>().teamID = GetComponent<BehaviorParameters>().m_TeamID;
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena
    }

    //observation Vector
    public override void CollectObservations()
    {
        enemy = Camera.main.GetComponent<GameBehaviour>().ClosestEnemy(transform); //get closest enemy inside arena
        //observe arena-relative position of the enemy and this agent
        AddVectorObs(enemy.localPosition.x);
        AddVectorObs(enemy.localPosition.y);
        AddVectorObs(transform.localPosition.x);
        AddVectorObs(transform.localPosition.y);
    }


    public override void AgentReset()
    {
        //currentsteps at this point is zero, triggered at max steps or when Done();
        ResetPosition(transform);
        GetComponent<HealthController>().Max(); //reset to max health
        GetComponent<CharacterStats>().Reset(); //reset statistical values
        GetComponent<CharacterStats>().TotalSteps(maxStep); //Total steps over all reset periods
                                                            // arena.GetComponent<ArenaBehaviour>().deathcount++; //the death count is used to randomize trees
                                                            // int a = arena.GetComponent<ArenaBehaviour>().deathcount;
                                                            // if (a % 5 == 0) arena.GetComponent<ArenaBehaviour>().UpdateTrees(); //trees randomized every 5th death
    }

    //called by an enemy's HealthController when it dies through damage caused by agent this script is attached to
    public void Victory()
    {
        float ks = GetComponent<CharacterStats>().ks(); //statistics: update killing spree
        AddReward(0.05f + ks * 0.05f); ; //add rewardfor victory, increased based on killing spree of the agent
        GetComponent<CharacterStats>().Won(); //statistics: update victory count
        print("ks= " + (ks + 1));
       }

    //called by the agents HealthController when the agent dies
    public void Defeat()
    {
        AddReward(-0.5f);
        int sc = GetStepCount();
        GetComponent<CharacterStats>().TotalSteps(-maxStep + sc); //-maxstep because the method triggers done, which adds maxstep already

        Done(); //reset the agent
    }

    public void ResetPosition(Transform t)
    {
        float h = 0;
        float v = 0;
        /* // to cause the agent to spawn inside 4 spawn areas
        int a = Random.Range(1,5);
        
        
        switch (a)
        {
            case 1:
                v = Random.Range(-2f, 2f);
                h = Random.Range(-25.0f, -21.0f);
                break;
            case 2:
                v = Random.Range(-2f, 2f);
                h = Random.Range(21.0f, 25.0f);
                break;
            case 3:
                v = Random.Range(16f, 20f);
                h = Random.Range(-3.0f, 3.0f);
                break;
            case 4:
                v = Random.Range(-20f, -16f);
                h = Random.Range(-3.0f, 3.0f);
                break;
            default:
                break;
        }
        */
        v = Random.Range(-11.5f, 14.5f);
        h = Random.Range(-18.0f, 18.0f);


        Vector2 arenaPos = arena.transform.position;
        t.position = new Vector2(h, v) + arenaPos; //set the agent's spawn to be relative to the arena (could use localposition instead)
    }

    public void SetEnemy(Transform e) { enemy = e; } //a currently unused way to set the agents enemy target to a specific character
}