using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AbilityController : MonoBehaviour
{

    public List<Ability> Abilities;


    public void Start()
    {


       Abilities.Add(transform.GetComponent<BasicAttack>()); //0
       Abilities.Add(transform.GetComponent<MultiSlash>()); //1
       Abilities.Add(transform.GetComponent<ChargeAttack>()); //2
       Abilities.Add(transform.GetComponent<ShadowImpact>()); //3
       Abilities.Add(transform.GetComponent<FireBolt>()); //4
       Abilities.Add(transform.GetComponent<FireStorm>()); //5
       Abilities.Add(transform.GetComponent<Teleport>());  //6
       Abilities.Add(transform.GetComponent<SelfHeal>()); //7fir
       Abilities.Add(transform.GetComponent<HealBolt>()); //8
       Abilities.Add(transform.GetComponent<HealWave>()); //9
    }


    public void UseAbility(int spellid) {
        Abilities[spellid].UseMouse();

        
    }

    public void Invoke(int spellid, Transform user)
    {
        Abilities[spellid].Invoke(user);
    }

    public void InvokeMouse(int spellid, Transform user)
    {
        Abilities[spellid].InvokeMouse(user);
    }



}
