using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIHealthTextBehaviour : MonoBehaviour
{
    private int health = 100;
    private int maxHealth = 100;
    public Text healthText;
    public Transform p1;
void Update()
    {
        health = p1.GetComponent<HealthController>().health;
        maxHealth = p1.GetComponent<HealthController>().maxHealth;
        healthText.text = "Health: " + health +" / "+ maxHealth;
        
    }
}
