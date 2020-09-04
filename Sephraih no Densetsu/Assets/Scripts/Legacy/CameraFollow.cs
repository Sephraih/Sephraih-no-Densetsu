using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{

    public Transform target; // target the camera looks at 

    //the three defined player characters
    public Transform ichi; // the first character, the dps
    public Transform dummy; //dummy target to see that there is no enemy

    public Vector3 offset;
    public List<Transform> enemylist; // list of currently active enemies
    public Animator ShakeAnimation; // various actions in the game use a shake animation to simulate collision effects

    // follow target
    void LateUpdate()
    {
        transform.position = target.position + offset;

    }

    private void Update()
    {

        target = ichi;
        target.gameObject.SetActive(true);
        InstantiateEnemy();
    }

    // plays a shake animation using an animator attached to the camera object, essentially altering the camera's position for the duration of  the animation
    public void CamShake()
    {
        ShakeAnimation.SetTrigger("shake");
    }

    // returns the closest player in relation to a character - enemy or player
    public Transform ClosestPlayer(Transform self)
    {
        return ichi;
    }

    // returns the enemy closest to the calling character
    public Transform ClosestEnemy(Transform self)
    {
        if (enemylist.Count == 0) { return dummy; }
        var distance = Vector2.Distance(self.position, enemylist[0].position);
        Transform enemy = enemylist[0];
        foreach (Transform e in enemylist)
        {
            if (Vector2.Distance(self.position, e.position) < distance)
            {
                distance = Vector2.Distance(self.position, e.position);
                enemy = e;
            }
        }

        return enemy;
    }

    // determines and returns the player with the lowest health among the three defined player characters
    public Transform LowestHealthPlayer()
    {
        return ichi;
    }

    // determines lowest health player that is not the user, should only be called from a player character


    public void InstantiateEnemy()
    {

        if (Input.GetButtonDown("enemy1"))
        {
            // load an enemy at current mouse position, transformed to game world position
            Instantiate((Resources.Load("Prefabs/Enemy") as GameObject), Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1), Quaternion.identity);
        }
        if (Input.GetButtonDown("enemy2"))
        {
            // load an enemy at current mouse position, transformed to game world position
            GameObject a = Instantiate((Resources.Load("Prefabs/Guard") as GameObject), Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1), Quaternion.identity);
            a.GetComponent<GuardBehaviour>().guardSpot = Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1);
        }
        if (Input.GetButtonDown("enemy3"))
        {
            // load an enemy at current mouse position, transformed to game world position
            Instantiate((Resources.Load("Prefabs/Wizard") as GameObject), Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1), Quaternion.identity);
        }
    }


}
