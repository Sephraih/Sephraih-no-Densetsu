using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//simple basic attack, see multislash for explanation of functionality
public class BasicAttack : MonoBehaviour
{
    public int damage;
    public float startDelay;
    private float delay;


    public LayerMask whatIsEnemy;

    private Transform attackPos;
    private float attackRangeX = 2.5f;
    private float attackRangeY = 1.5f;

    private GameObject slashEffect;


    public Gradient particleColorGradient;


    private void Start()
    {
        attackPos = transform.GetChild(0);
        slashEffect = Resources.Load("prefabs/ParticleSlashPrefab") as GameObject;
        
    }
    void Update()
    {
        if (delay >= 0)
        {
            delay -= Time.deltaTime;
        }
    }

    public void Attack()
    {
        if (delay <= 0)
        {
            // instantiate slash prefab
            GameObject slash = Instantiate(slashEffect, transform.position + attackPos.localPosition, Quaternion.identity);


            //effect
            slash.transform.parent = transform;
            slash.transform.Rotate(Mathf.Atan2(attackPos.localPosition.x, attackPos.localPosition.y) * Mathf.Rad2Deg, +90, 0);
            Destroy(slash, 0.2f);

            //determine damaged enemies, apply damage
            Collider2D[] enemiesToDamage = Physics2D.OverlapBoxAll(attackPos.position, new Vector2(attackRangeX, attackRangeY), attackPos.localPosition.x * 90, whatIsEnemy);
            for (int i = 0; i < enemiesToDamage.Length; i++)
            {   
                if(enemiesToDamage[i].isTrigger)
                enemiesToDamage[i].GetComponent<HealthController>().TakeDamage(damage,transform);
            }
                delay = startDelay;
            
        }
    }

}