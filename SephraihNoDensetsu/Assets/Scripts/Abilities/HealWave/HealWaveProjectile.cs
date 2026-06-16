using System.Collections.Generic;
using UnityEngine;

public class HealWaveProjectile : MonoBehaviour
{
    //who to damage, who to heal, who used the spell
    public Transform user;
    public int teamID;

    public GameObject destroyEffect;

    public float speed; //travelspeed of ability
    public float lifetime;
    public int healAmount =0; //heal amount, set in inspector

    //lists of targets that have been healed or damaged already by an outbound or incoming wave
    private List<Collider2D> healedTargets = new List<Collider2D>();
    private List<Collider2D> damagedTargets = new List<Collider2D>();


    private bool inbound = false;

    //when instantiated
    private void Start()
    {
        teamID = user.GetComponent<StatusController>().teamID;
        Invoke("Turn", lifetime * 0.5f);
    }
    //once per frame
    private void FixedUpdate()
    {

        Collider2D[] overlapColliders = Physics2D.OverlapCircleAll(transform.position, 1.0f); //on each frame check for colliders in a circle area around the projectile's lcoation

        //heal or damage colliders found, only apply to trigger type colliders that don't belong to an unseen character
        foreach (Collider2D collider in overlapColliders)
        {
            if (collider.CompareTag("Player") && collider.transform != user && collider.isTrigger && collider.transform.GetComponent<StatusController>().teamID == teamID)
            {
                if (!healedTargets.Contains(collider))
                {
                    healedTargets.Add(collider);
                    collider.GetComponent<HealthController>().Heal(healAmount, user);
                }
            }

            if (collider.CompareTag("Player") && collider.isTrigger && collider.transform.GetComponent<StatusController>().teamID != teamID)
            {
                if (!damagedTargets.Contains(collider))
                {
                    damagedTargets.Add(collider);
                    collider.GetComponent<HealthController>().TakeDamage(healAmount / 2, user);
                }
            }

        }



        if (inbound)
        {
            Vector2 direction = user.position - transform.position; // in direction of user

            float rotZ = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg; //rotate towards user each frame
            transform.rotation = Quaternion.Euler(0f, 0f, rotZ - 90);

            foreach (Collider2D collider in overlapColliders)
            {
                if (collider.transform == user)
                {
                    DestroyProjectile();
                }

            }
        }

        transform.Translate(Vector2.up * speed * Time.deltaTime); //framewise relocation of the particle towards the way it is facing
    }

    void DestroyProjectile()
    {
        Destroy(gameObject);
    }

    //called through start method invocation to return the particle to the caster and make targets hitable again on the way backs
    void Turn()
    {
        inbound = true;
        healedTargets.Clear();
        damagedTargets.Clear();
    }



}
