using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAgents;

public class Statistics : MonoBehaviour
{

    public float hks = 0; //highest killing spree achieved during game by agent


    // Start is called before the first frame update
    void Start()
    {
        hks = 0;
    }

    //agents call this when they achieve a kill
    public void UpdateHks(float n)
    {
        hks = hks > n ? hks : n;
    }

}
