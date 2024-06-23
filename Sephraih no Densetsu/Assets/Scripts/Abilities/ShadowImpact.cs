using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Unity.Services.Analytics.Platform;
using UnityEngine;

public class ShadowImpact : Ability
{
    private int dmg = 80;


    private Vector2 chargeDirection;
    private float distanceToTarget;
    private Transform target;
    private Transform attackPos; //direction of attack
    private GameObject slashEffect; //particle slash
    private GameObject tpEffect; //particle slash

    private void Start()
    {
        attackPos = transform.GetChild(0); //loaded automatically instead of assignment through editor
        slashEffect = Resources.Load("Prefabs/Effects/ParticleSlashPrefab") as GameObject;
        tpEffect = Resources.Load("Prefabs/Effects/TeleportEffect") as GameObject;
    }


    public override void UseTarget(Transform target)
    {
        if (transform.GetComponent<StatusController>().teamID != target.GetComponent<StatusController>().teamID)
        {
            if (cd <= 0f) // if ability ready to use
            {
                //determine direction
                distanceToTarget = Vector2.Distance(transform.position, target.position);
                if (distanceToTarget <= range) // && distanceToTarget >= 2.0f at point blank atm
                {
                    this.target = target; //classwide access
                    StartCoroutine(SlashCoroutine()); //execute the charge, this is a process happening over time and will hence not be completed in a single frame.
                    cd = acd; //reset cooldown
                }

            }
        }

    }

    public override void UseMouse()
    {

        Transform t = Camera.main.GetComponent<GameBehaviour>().ClosestEnemyToLocation(MousePosition(), transform);
        UseTarget(t);

    }

    IEnumerator SlashCoroutine()
    {
        float time = 1f;
        float count = 0.0f;
        float random = 1f;
        while (count < time && target != null)
        {
            //transform.GetComponent<Rigidbody2D>().velocity = new Vector2(0,0);
            //target.GetComponent<Rigidbody2D>().velocity    = new Vector2(0, 0);

            Vector2 tp = target.position;
            //Vector2 direction = tp - new Vector2(transform.position.x, transform.position.y); // get the direction the caster is facing
            //float distance = direction.magnitude;
            //direction.Normalize(); // ignore distance
            Vector2 trpos = transform.position;
            //transform.position = trpos + direction * range;
            transform.position = target.position + offset(count);
            //GameObject tef = Instantiate(tpEffect, transform.position + new Vector3(0, -0.7f, 0), Quaternion.Euler(0f, 0f, 0)); //instantiate effect prefab at position and rotation
            //Destroy(tef, 0.5f); //free up memory


            Slash(30*random, Color.red);

            GetComponent<MovementController>().stuck = true; //disalow any other movement of the charging character
            GetComponent<MovementController>().LookAt(target.position);
            target.GetComponent<HealthController>().TakeDamage(dmg * (GetComponent<StatusController>().lvl + transform.GetComponent<StatusController>().Str), transform);
            count += 0.125f;
            random *= -1;
            yield return new WaitForSeconds(0.125f);
        }
        //after coroutine
        GetComponent<MovementController>().stuck = false;
    }

    private Vector3 offset(float count) {

        Debug.Log(count);
         Vector3 v = new Vector3(0, 1, 0);
        
        if (count >= 0.25f)
        {
            v = new Vector3(1, 0, 0);
        }
        if (count >= 0.5f)
        {
            v = new Vector3(-1, 0, 0);
        }
        if (count >= 0.75f)
        {
            v = new Vector3(0, -1, 0);
        }

        return v;
    }


    private void Slash(float angle, Color color)
    {

        //instantiate slash prefab
        GameObject slash = Instantiate(slashEffect, transform.position + attackPos.localPosition, Quaternion.identity);


        //get particle system to set it's color
        ParticleSystem.MainModule slashParticleMain = slash.GetComponent<ParticleSystem>().main;
        slashParticleMain.startColor = color;

        //effect
        slash.transform.parent = transform; //to set the simulation space (follow the character)
        slash.transform.Rotate(Mathf.Atan2(attackPos.localPosition.x, attackPos.localPosition.y) * Mathf.Rad2Deg, +90, 0); // direction user is facing
        slash.transform.Rotate(angle, 0, 0); //rotate the slash

        Destroy(slash, 0.2f); //free memory


    }

}
