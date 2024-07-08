using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AbilityController : MonoBehaviour
{

    public List<Ability> Abilities;

    public void Start()
    {
       Abilities.Add(Camera.main.GetComponentInChildren<BasicAttack>()); //0
       Abilities.Add(Camera.main.GetComponentInChildren<MultiSlash>()); //1
       Abilities.Add(Camera.main.GetComponentInChildren<ChargeAttack>()); //2
       Abilities.Add(Camera.main.GetComponentInChildren<ShadowImpact>()); //3
       Abilities.Add(Camera.main.GetComponentInChildren<FireBolt>()); //4
       Abilities.Add(Camera.main.GetComponentInChildren<FireStorm>()); //5
       Abilities.Add(Camera.main.GetComponentInChildren<Teleport>());  //6
       Abilities.Add(Camera.main.GetComponentInChildren<SelfHeal>()); //7
       Abilities.Add(Camera.main.GetComponentInChildren<HealBolt>()); //8
       Abilities.Add(Camera.main.GetComponentInChildren<HealWave>()); //9
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
