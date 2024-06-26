using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AbilityController : MonoBehaviour
{


    
    public void Start()
    {
        
      
       

    }

    public void UseAbility(int spellid) {

        if (spellid == 0)
        {
            GetComponent<BasicAttack>().Attack();
        }
        if (spellid == 1)
        {
            GetComponent<FireStorm>().UseMouse();
        }
        if (spellid == 2)
        {
            GetComponent<Teleport>().UseMouse();
        }
        if (spellid == 3)
        {
            GetComponent<ChargeAttack>().UseMouse();
        }
        if (spellid == 4)
        {
            GetComponent<SelfHeal>().UseMouse();
        }
        if (spellid == 5)
        {
            GetComponent<MultiSlash>().UseMouse();
        }
        if (spellid == 6)
        {
            GetComponent<FireBolt>().UseMouse(); 
        }
        if (spellid == 7)
        {
            Ability a = GetComponent<ShadowImpact>();
            a.UseMouse();


          //  GetComponent<AbilityController>().Abilities[1].UseMouse();
        }



    }




}
