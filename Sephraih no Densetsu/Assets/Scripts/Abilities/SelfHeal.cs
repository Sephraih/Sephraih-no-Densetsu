using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SelfHeal : MonoBehaviour
{

    public float cd = 2.0f; //cooldown via editor
    private float cdtimer = 0; //current cooldown
    public GameObject effect; //effect to be displayed on ability use
    public int healAmount =50;

    //each frame, reduce cooldown based on time passed
    void Update()
    {
        if (cdtimer >= 0)
        {
            cdtimer -= Time.deltaTime;
        }
    }

    //heal yourself based on your matk.
    public void Heal()
    {
        if (cdtimer <= 0)
        {
            GetComponent<HealthController>().Heal(healAmount, transform); // magical attack from status
            cdtimer = cd;
            GameObject a = Instantiate(effect, transform.position, Quaternion.identity); // instantiate a heal effect
            a.transform.parent = transform; //so the particle system follows the character
            Destroy(a, 0.5f); // destroy the heal effect to safe memory
        }

    }

}
