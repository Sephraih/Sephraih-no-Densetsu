using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AbilityController : MonoBehaviour
{


    public List<Ability> A1;

    public void Start()
    {
        A1.Add(GetComponent<BasicAttack>()); //0
        A1.Add(GetComponent<MultiSlash>()); //1
        A1.Add(GetComponent<ChargeAttack>()); //2
        A1.Add(GetComponent<ShadowImpact>()); //3
        A1.Add(GetComponent<FireBolt>()); //4
        A1.Add(GetComponent<FireStorm>()); //5
        A1.Add(GetComponent<Teleport>());  //6
        A1.Add(GetComponent<SelfHeal>()); //7
        A1.Add(GetComponent<HealBolt>()); //8
        A1.Add(GetComponent<HealWave>()); //9

    }

    public void UseAbility(int spellid) {
        A1[spellid].UseMouse();

        
    }




}
