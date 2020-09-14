using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAgents;

public class Ability : MonoBehaviour
{

    public float acd; // ability cd
    protected float cd = 0; //remaining cd
    public float range;

    public virtual void Use() { }
    public virtual void UseTarget(Transform target) { }
    public virtual void UseMouse() { }

    public Vector2 MousePosition()
    {
        return Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }
    void Update()
    {
        if (cd >= 0)
        {
            cd -= Time.deltaTime; //decrease cooldown
        }
    }

}