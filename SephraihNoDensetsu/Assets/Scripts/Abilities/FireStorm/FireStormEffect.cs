using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FireStormEffect : MonoBehaviour
{
    //public string enemy ="Enemy"; // caster's enemy, from fire bolt script, default set to enemy units
    private float lifetime = 5;
    public int dmg; // damage of direct hit
    public float slow; // slow amount
    public Transform user;
    public int teamID;

    private List<Collider2D> damagedTargets = new List<Collider2D>(); // list to save targets that have been damaged,

    //public GameObject destroyEffect;

    // destroy the object based on its lifetime
    private void Start()
    {
        Invoke("DestroyProjectile", lifetime);
        teamID = user.GetComponent<StatusController>().teamID;
    }

    // every frame
    private void FixedUpdate()
    {

        Collider2D[] overlapColliders = Physics2D.OverlapCircleAll(transform.position + new Vector3(0f, 0.75f, 0f), 2.5f); //a circle located at the projectile's position scanning for any colliders overlapped and adding them to a list 0.75 is the animation offset

        foreach (Collider2D collider in overlapColliders)
        {

            if (collider.isTrigger && collider.CompareTag("Player") && collider.transform.GetComponent<StatusController>().teamID != teamID) // all enemy colliders, each character has 2 colliders, only the trigger collider is used
            {
                if (!damagedTargets.Contains(collider)) //before the bolt is destroyed, it checks for colliders on every frame, which might result in duplicate application, which is unwished for
                {
                    collider.GetComponent<HealthController>().TakeDamage(dmg, user); //apply damage to the character the collider belongs to
                    collider.GetComponent<StatusController>().Slow(slow, 0.3f); //slow the character
                    damagedTargets.Add(collider);

                    StartCoroutine(DamageableCoroutine(collider));

                }

            }


        }

    }
    IEnumerator DamageableCoroutine(Collider2D collider)
    {
        float time = 0;
        float duration = 0.3f;
        while (time < duration)
        {
            time += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        damagedTargets.Remove(collider);


    }

    //destroy the projectile based on its lifetime / when it collides and deploy an explosion effect
    void DestroyProjectile()
    {
        Destroy(gameObject);

    }

}
