using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// projectile launched by heal wave ability, see fire bolt projectile for documentation, heals instead of damaging
public class HealBoltProjectile : MonoBehaviour
{
    public Transform user;
    public int teamID;

    public float speed;
    public float lifetime;
    public float rayDistance;
    public int heal;

    public LayerMask whatIsEnemy;

    public GameObject destroyEffect;

    private void Start()
    {
        teamID = user.GetComponent<StatusController>().teamID;
        Invoke("DestroyProjectile", lifetime);
    }
    private void FixedUpdate()
    {
        RaycastHit2D hitInfo = Physics2D.Raycast(transform.position, Vector2.up, rayDistance);

        if (hitInfo.collider != null)
        {
            if (hitInfo.collider.CompareTag("Player") && hitInfo.collider.transform != user && hitInfo.collider.transform.GetComponent<StatusController>().teamID == teamID)
            {
                Debug.Log(hitInfo.collider);
                hitInfo.collider.GetComponent<HealthController>().Heal(heal,user);
                DestroyProjectile();
            }

        }


        transform.Translate(Vector2.up * speed * Time.deltaTime);
    }

    void DestroyProjectile()
    {
        GameObject a = Instantiate(destroyEffect, transform.position, Quaternion.Euler(0f, 0f, 0f));
        Destroy(a, 1);
        Destroy(gameObject);
    }

}
