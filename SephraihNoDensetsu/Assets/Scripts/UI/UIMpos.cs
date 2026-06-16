using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIMpos : MonoBehaviour
{
    public Text pos;
    void Update()
    {
        Vector2 mp = Camera.main.ScreenToWorldPoint(Input.mousePosition).normalized;
        pos.text = mp.x + " / " + mp.y;

    }
}
