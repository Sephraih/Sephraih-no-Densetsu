using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CharacterStats : MonoBehaviour
{

    public float dmgDone = 0; //damage done per reset
    public float dmgDoneTotal = 0; //damage done over all resets
    public float dmgTaken = 0; //damage taken per reset

    public float healDone = 0; //heal done per reset
    public float healDoneTotal = 0; //total healing over all resets


    public float lived = 0; //time lived per reset
    public float victories = 0; //victories achieved per reset

    public int totalSteps; //total steps done over all resets


    // Update is called once per frame
    void Update()
    {
        lived += Time.deltaTime;
    }

    public void DmgDone(float d)
    {
        dmgDone += d;
        dmgDoneTotal += d;
    }

    public void DmgTaken(float d)
    {
        dmgTaken += d;
    }

    public void HealDone(float h)
    {
        healDone += h;
        healDoneTotal += h;
    }

    public void DpSteps(int s)
    {
        float dps = dmgDone / s;
        print("Step: " + s + ": " + dps + " dps");
        GetComponent<BasicAgent>().SetReward(dps * 0.01f);
    }

    public void Won()
    {
        victories++;
    }

    public float ks()
    {
        return victories;
    }

    //reset statistics that are per reset period
    public void Reset()
    {
        dmgDone = 0;
        dmgTaken = 0;
        healDone = 0;
        lived = 0;
        victories = 0;
    }

    public void TotalSteps(int ts)
    {
        totalSteps += ts;
    }

    //called upon ending the game or training
    void OnApplicationQuit()
    {
       // Debug.Log("steps completed: " + totalSteps + " dps: " + dmgDoneTotal / totalSteps);
    }
}
