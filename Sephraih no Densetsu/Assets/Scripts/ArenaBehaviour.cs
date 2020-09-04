using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArenaBehaviour : MonoBehaviour


{
    public List<Transform> treeList;


    
    // function that may be called at the game's start or when an agent achieves a kill divisible through five 
    public void UpdateTrees()
    {
        //reassign every tree to a random location within the arena it belongs to
        foreach (Transform t in treeList)
        {
            float v = Random.Range(-11.5f, 14.5f);
            float h = Random.Range(-18.0f, 18.0f);
            Vector2 arenaPos = transform.transform.position;
            t.position = new Vector2(h, v) + arenaPos;
        }

    }

}