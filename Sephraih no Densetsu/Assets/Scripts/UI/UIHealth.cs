﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIHealth : MonoBehaviour
{



    public Transform p1; // the health controller of a the character this script is attached to, determinign information such as current health
    private HealthController hc;
    public Image fgi; // fore ground image, this is a simple image that represents a health bar

    private void Start()
    {
        hc = p1.GetComponent<HealthController>();
    }

    private void Update()
    {
        fgi.fillAmount = 1.0f / ((float)hc.maxHealth / (float)hc.health); // based on curent and maximal health, calculate the percentual amount from left to right of the image that is to be shown
    }


}
