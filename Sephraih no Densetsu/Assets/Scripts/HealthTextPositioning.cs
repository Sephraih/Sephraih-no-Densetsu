using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HealthTextPositioning : MonoBehaviour
{
    // Update is called once per frame
    void Update()
    {
        transform.localPosition += new Vector3(0, 1, 0) * Time.deltaTime; //move the health text upwards over time
    }
}
