using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DummyBehaviour : EnemyController
{
    // Start is called before the first frame update
    public void Start()
    {
        Camera.main.GetComponent<GameBehaviour>().Register(transform); //upon creation add to list of enemies
        teamID = GetComponent<StatusController>().teamID;

        GetComponent<StatusController>().lvl = 10;
    }


    void Update()
    {
        if (GetComponent<HealthController>().health <2000)
        {
            GetComponent<HealthController>().health = GetComponent<HealthController>().maxHealth;
        }
        Die();
    }



}
