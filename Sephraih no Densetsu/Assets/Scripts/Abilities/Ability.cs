using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability : MonoBehaviour
{

    public float acd; // ability cd
    protected float cd = 0; //remaining cd
    public float range;
    

    
    protected Transform user;
    protected Transform attackPos;


    public virtual void Use() {
        UseMouse();
    }

    public virtual void UseTarget(Transform target) { }
    public virtual void UseMouse() { }

    public void InvokeMouse(Transform user)
    {
        this.user = user;
        this.attackPos = user.GetChild(0);
        UseMouse();
    }

    public void Invoke(Transform user)
    {
        this.user = user;
        this.attackPos = user.GetChild(0);
        Use();
    }


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