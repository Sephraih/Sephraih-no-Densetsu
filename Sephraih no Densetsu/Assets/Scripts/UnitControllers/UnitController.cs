using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitController : MonoBehaviour
{

    public Vector2 movementDirection; // direction of movement
    protected float msi; // movement speed input, in the case of a bot this is either zero or one
    public GameObject attackingDirection; // object used to calculate a vector of attack
    public int teamID;
    public Vector3 saveSpot;
     

    public virtual void Move(){}

    public virtual void Skills(){}

    public void Aim()
    {
        //position the attacking direction object infront of the character, keep position when it stops moving
        if (movementDirection != Vector2.zero)
        {
            attackingDirection.transform.localPosition = movementDirection * 0.5f;
        }
    }

    public virtual void Attack() { }




    public void SetSaveSpot(Vector3 spot)
    {
        this.saveSpot = spot;

    }






}
