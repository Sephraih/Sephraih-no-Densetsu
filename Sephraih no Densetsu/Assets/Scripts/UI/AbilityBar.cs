using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AbilityBar : MonoBehaviour
{
    private Transform abilitySlotTemplate;
    private void Awake()
    {
        abilitySlotTemplate = transform.Find("abilitySlotTemplate");
        abilitySlotTemplate.gameObject.SetActive(false);

    }

    private void UpdateVisual() { 
    
    }

}
