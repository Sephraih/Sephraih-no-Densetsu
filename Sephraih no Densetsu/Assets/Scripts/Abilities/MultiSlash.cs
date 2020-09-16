using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MultiSlash : Ability
{

    private int basedmg =100;
    private int maxCombo = 4;
    private int comboCount = 1;
    private float comboDelay = 0.1f;


    public LayerMask whatIsEnemy; //Layer specified in editor "player" then matched against tags of objects to be determined player or not

    private Transform attackPos; //direction of attack

    //damage area of slash
    private float attackRangeX = 2.5f;
    private float attackRangeY = 1.5f;

    private GameObject slashEffect; //particle slash


    private void Start()
    {
        attackPos = transform.GetChild(0); //loaded automatically instead of assignment through editor
        slashEffect = Resources.Load("Prefabs/ParticleSlashPrefab") as GameObject;
    }
  
    public override void Use()
    {
        if (cd <= 0) //can't attack if the attack isnt ready to be used again
        {

            int dmg = basedmg;
            //use different slash animations based on the combo
            if (comboCount > 2)
            {
                DoubleSlash(); dmg *= 2;
            }
            else if (comboCount == 1) RightSlash();
            else LeftSlash();

            //attack stat of the using character influences damage
            //var atk = transform.GetComponent<StatusController>().atk;

            //determine damaged enemies, apply damage
            Collider2D[] enemiesToDamage = Physics2D.OverlapBoxAll(attackPos.position, new Vector2(attackRangeX, attackRangeY), attackPos.localPosition.x * 90, whatIsEnemy);
            for (int i = 0; i < enemiesToDamage.Length; i++)
            {
                if (enemiesToDamage[i].isTrigger && enemiesToDamage[i].transform != transform)
                {
                    if (enemiesToDamage[i].GetComponent<StatusController>().teamID != transform.GetComponent<StatusController>().teamID)
                    {
                        enemiesToDamage[i].GetComponent<HealthController>().TakeDamage(dmg * (GetComponent<StatusController>().lvl + transform.GetComponent<StatusController>().Str), transform);
                    }
                }
            }
            comboCount++;
            cd = comboDelay;

            if (comboCount > maxCombo)
            {
                cd = acd;
                comboCount = 1;
            }

        }
    }


    private void LeftSlash()
    {
        Slash(-30, Color.cyan);
    }

    private void RightSlash()
    {
        Slash(30, Color.cyan);
    }

    private void DoubleSlash()
    {
        Color sc = new Color(0.2f, 0, 0.7f, 1);
        Slash(30, sc);
        Slash(-30, sc);
        Camera.main.GetComponent<NeutralCam>().CamShake();
    }

    //create a particle system in based on color and rotation angle
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