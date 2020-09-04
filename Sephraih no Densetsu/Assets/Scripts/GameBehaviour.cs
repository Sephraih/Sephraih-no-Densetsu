using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameBehaviour : MonoBehaviour


{
    public List<Transform> characterList; // list of currently active enemies
    public int deathcount = 0;


    private void Update()
    {
        InstantiateEnemy();
    }

    //return a calling agent's closest enemy
    public Transform ClosestEnemy(Transform self)
    {

        float distance = 10000;
        int teamID = self.GetComponent<StatusController>().teamID;
        Transform ce = self; //return itself if there is no other enemy - this means that a default enemy has to be asigned in the inspector no longer. Also the size of the characterList doesn't have to be checked anymore.
        foreach (Transform e in characterList)
        {
            if (Vector2.Distance(self.position, e.position) < distance && e.GetComponent<StatusController>().teamID != teamID) //search for the enemy with the shortest distance to calling agent (closer than max distance or distance to previous enemy, not on the same team)
            {
                distance = Vector2.Distance(self.position, e.position);
                ce = e;
            }
        }
        return ce;
    }

    //return a calling agent's closest ally
    public Transform ClosestAlly(Transform self, Transform ca)
    {
        float distance = 10000;
        int teamID = self.GetComponent<StatusController>().teamID;

        if (characterList.Count >= 2) //at least self and and another character
        {

            foreach (Transform a in characterList)
            {
                if (Vector2.Distance(self.position, a.position) < distance && a.GetComponent<StatusController>().teamID == teamID && self != a) //search for the ally with the shortest distance (not self)
                {
                    distance = Vector2.Distance(self.position, a.position);
                    ca = a;
                }
            }
        }
        return ca;
    }

    public void Register(Transform self) //called by each agent at the start of the game or training, add it to the character list
    {
        characterList.Add(self);
    }
    public void Remove(Transform self) //remove character from list if it stops existing (currently, agents respawn upon death, hence this is not called)
    {
        characterList.Remove(self);
    }

    // function that may be called at the game's start or when an agent achieves a kill divisible through five 
    

    public void InstantiateEnemy()
    {

        if (Input.GetButtonDown("enemy1"))
        {
            // load an enemy at current mouse position, transformed to game world position
            Instantiate((Resources.Load("Prefabs/Enemy") as GameObject), Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1), Quaternion.identity);
        }
        if (Input.GetButtonDown("enemy2"))
        {
            // load an enemy at current mouse position, transformed to game world position
            GameObject a = Instantiate((Resources.Load("Prefabs/Guard") as GameObject), Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1), Quaternion.identity);
            a.GetComponent<GuardBehaviour>().guardSpot = Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1);
        }
        if (Input.GetButtonDown("enemy3"))
        {
            // load an enemy at current mouse position, transformed to game world position
            Instantiate((Resources.Load("Prefabs/Wizard") as GameObject), Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1), Quaternion.identity);
        }
    }
}