using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PortalBehaviour : MonoBehaviour
{



    private void FixedUpdate()
    {

        Collider2D[] overlapColliders = Physics2D.OverlapCircleAll(transform.position, 0.3f); //a circle located at the portal's position scanning for any colliders overlapped

        foreach (Collider2D collider in overlapColliders)
            if (collider.isTrigger && collider.CompareTag("Player")) // all enemy colliders, each character has 2 colliders, only the trigger collider is used
            {
                Camera.main.GetComponent<LevelBehaviour>().LoadNextLevel();
                //Destroy(gameObject);
            }
    }  
        
}
