using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SelfHeal : Ability
{

    public GameObject effect; //effect to be displayed on ability use
    public int healAmount =50;

    //each frame, reduce cooldown based on time passed
 

    //heal yourself based on your matk.
    public override void Use()
    {
        if (cd <= 0)
        {
            GetComponent<HealthController>().Heal(healAmount, transform); // magical attack from status
            cd = acd;
            GameObject a = Instantiate(effect, transform.position, Quaternion.identity); // instantiate a heal effect
            a.transform.parent = transform; //so the particle system follows the character
            Destroy(a, 0.5f); // destroy the heal effect to safe memory
        }

    }

}
