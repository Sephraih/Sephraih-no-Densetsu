using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireBoltProjectile : MonoBehaviour
{
    //public string enemy ="Enemy"; // caster's enemy, from fire bolt script, default set to enemy units
    public float speed;
    public float lifetime;
    public int dmg; // damage of direct hit
    public int dotd; // damage over time damage
    public int dott; // damage over time duration
    public float slow; // slow amount
    public Transform user;
    public int teamID;

    // Max distance this bolt can travel from its spawn point before self-destructing - set by
    // FireBolt.Bolt() from its own computed `range` (see FireBolt.rangePercent/RangeSettings).
    // Independent of `lifetime` above: lifetime is a time-based safety net, this is the actual
    // enforced "max spell range" the ability is tuned to. 0 (unset) disables the check, matching
    // this field's default before this was wired up.
    public float maxRange;
    private Vector3 spawnPosition;

    private List<Collider2D> damagedTargets = new List<Collider2D>(); // list to save targets that have been damaged,

    public GameObject destroyEffect;

    // destroy the object based on its lifetime
    private void Start()
    {
        Invoke("DestroyProjectile", lifetime);
        teamID = user.GetComponent<StatusController>().teamID;
        transform.Translate(Vector2.up*1);
        spawnPosition = transform.position;
    }

    // every frame
    private void FixedUpdate()
    {
        if (maxRange > 0f && Vector2.Distance(transform.position, spawnPosition) >= maxRange)
        {
            DestroyProjectile();
            return;
        }

        Collider2D[] overlapColliders = Physics2D.OverlapCircleAll(transform.position, 0.3f); //a circle located at the projectile's position scanning for any colliders overlapped and adding them to a list

        foreach (Collider2D collider in overlapColliders)
        {

            if (collider.transform != user)
            {
                if (collider.isTrigger && collider.CompareTag("Player")) // all enemy colliders, each character has 2 colliders, only the trigger collider is used
                {
                    if (collider.transform.GetComponent<StatusController>().teamID != teamID)
                    {
                        if (!damagedTargets.Contains(collider)) //before the bolt is destroyed, it checks for colliders on every frame, which might result in duplicate application, which is unwished for
                        {
                            collider.GetComponent<HealthController>().TakeDamage(dmg, user); //apply damage to the character the collider belongs to
                                                                                             //collider.GetComponent<StatusController>().Burn(dotd, dott, user); //burn the character
                                                                                             //collider.GetComponent<StatusController>().Slow(slow, dott); //slow the character
                            damagedTargets.Add(collider);

                            DestroyProjectile(); // destroy on first hit, optional
                        }
                    }
                }
                else if (ObstacleRegistry.TryGet(collider, out var obs))
                {
                    if (obs.BlocksProjectiles) DestroyProjectile();
                }
                else DestroyProjectile(); // solid collider with no Obstacle component yet: fail-safe, keep prior behavior
            }

        }

        transform.Translate(Vector2.up * speed * Time.deltaTime); //always fly upwards, the upwards vector's direction is assigned during instantiation in the firebolt script
    }

    //destroy the projectile based on its lifetime / when it collides and deploy an explosion effect
    void DestroyProjectile()
    {
        GameObject a = Instantiate(destroyEffect, transform.position, Quaternion.Euler(0, 0f, 0f));
        Destroy(a, 1);
        Destroy(gameObject);
    }

}

// old way of scanning enemies, turned out to be slower but may find application elsewhere in the future, which is why this artifact is stored here
// checking for enemy using raycast
/*
 * 
 * public float distance; // distance of collision detection relative to projectile
    
    RaycastHit2D hitInfo = Physics2D.Raycast(transform.position, Vector2.up, distance);
     
        if (hitInfo.collider != null)
        {
            if (hitInfo.collider.CompareTag(enemy))
            {
                Debug.Log("enemy damage taken");
                hitInfo.collider.GetComponent<HealthController>().TakeDamage(dmg);
                hitInfo.collider.GetComponent<StatusController>().Burn(dotd, dott,slow);

                DestroyProjectile();
            }

        }
*/
