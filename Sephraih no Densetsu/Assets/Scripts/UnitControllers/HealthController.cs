using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HealthController : MonoBehaviour
{
    //the script is attached to all character objects.

    public int maxHealth = 100; // maximal health the number here is default, overwritten in inspector
    public int health = 100; // current health - default set to avoid errors
    //private float rewardmodifier = 0.0001f; //for health and damage loss
    private GameObject dmgText; // a damage text prefab to be displayed when the character takes damage
    private GameObject healText; // a text prefab to display the amount of health recovered as a number
    private GameObject bloodEffect; //a blood effect spawned by the character when damage is taken
    private GameObject healedEffect; // a recovery effect
    private float htSpawnCount = 0; // a counter to influence the spawn position relative to the character of any health change numbers

    public GameObject ht; // health text variable, this can be a heal or dmg text


    void Start()
    {
        Max(); // initialize character to start at max health

        //loading prefabs to be instantiated later
        bloodEffect = Resources.Load("Prefabs/BloodEffectPrefab") as GameObject;
        healedEffect = Resources.Load("Prefabs/HealedEffectPrefab") as GameObject;

        dmgText = Resources.Load("Prefabs/DmgTextPrefab") as GameObject;
        healText = Resources.Load("Prefabs/HealTextPrefab") as GameObject;

    }

    // take damage, display number and blood effect
    public void TakeDamage(int damage, Transform dmger)
    {
        GameObject blood = Instantiate(bloodEffect, transform.position, Quaternion.identity); // at character's position without any rotation
        blood.transform.parent = transform; // make the effect child of the character to let the effect follow it
        Destroy(blood, 0.7f);

        ShowDamageText(damage);
        health -= damage;
        if (health < 0) { health = 0; }

        //Debug.Log("took dmg" + damage);

    }

    // recover damage, display number and recovery effect
    public void Heal(int heal, Transform healer)
    {

       
        GameObject hef = Instantiate(healedEffect, transform.position, Quaternion.identity);
        hef.transform.parent = transform;
        Destroy(hef, 1.0f);

        if (health < maxHealth) { health += heal; }
        if (health > maxHealth) { health = maxHealth; }
        ShowHealText(heal);
    }

    public void ShowDamageText(int damage)
    {
        if (htSpawnCount > 0.6) htSpawnCount = 0;
        if (dmgText)
        {
            ht = Instantiate(dmgText, transform.position + new Vector3(0, 1, 0), Quaternion.identity);
            ht.GetComponent<TextMesh>().text = damage.ToString();
            ht.transform.localPosition += new Vector3(htSpawnCount, htSpawnCount, 0);
            htSpawnCount += 0.3f;
            Destroy(ht, 2.0f);
        }

    }

    public void ShowHealText(int heal)
    {

        if (htSpawnCount > 0.6) htSpawnCount = 0;
        if (healText)
        {
            ht = Instantiate(healText, transform.position + new Vector3(0, 1, 0), Quaternion.identity);
            ht.GetComponent<TextMesh>().text = heal.ToString();
            ht.transform.localPosition += new Vector3(-htSpawnCount, htSpawnCount, 0);
            htSpawnCount += 0.3f;
            Destroy(ht, 2.0f);
        }

    }

    public void Max()
    {
        health = maxHealth;
        ShowHealText(maxHealth);
    }

}


//ai logic storage

//in take damage
/* bool dmgerIsAI = dmger.GetComponent<StatusController>().AIControlled;
       bool transformIsAI = GetComponent<StatusController>().AIControlled;

       if (dmgerIsAI)
       {
           dmger.GetComponent<BasicAgent>().AddReward(damage * rewardmodifier); //reward to attacker (dmger)
           dmger.GetComponent<CharacterStats>().DmgDone(damage); //update stats
       }

       if (transformIsAI)
       {
           GetComponent<BasicAgent>().AddReward(damage * -rewardmodifier); //reward to attacked character (dmgd)
           GetComponent<CharacterStats>().DmgTaken(damage); //update stats
       }

       if (damage >= health)
       {
           if (dmgerIsAI) dmger.GetComponent<BasicAgent>().Victory();
           if (transformIsAI) GetComponent<BasicAgent>().Defeat();
       }
*/
//heal
/*
 *  bool healerIsPlayer = healer.GetComponent<StatusController>().AIControlled;

        if (healerIsPlayer)
        {
            int toMax = maxHealth - health; // health missing
            if (heal >= toMax) //clip reward if overhealed
            {
                healer.GetComponent<BasicAgent>().AddReward(toMax * rewardmodifier); //reward to attacker (dmger)
                healer.GetComponent<CharacterStats>().HealDone(toMax); //update stats
            }
            else //heal less than health missing
            {
                healer.GetComponent<BasicAgent>().AddReward(heal * rewardmodifier);
                healer.GetComponent<CharacterStats>().HealDone(heal); //update stats
            }
        }

    */