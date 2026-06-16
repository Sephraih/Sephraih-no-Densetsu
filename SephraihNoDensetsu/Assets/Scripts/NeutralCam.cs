using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NeutralCam : MonoBehaviour
{

    
    public Animator ShakeAnimation; // various actions in the game use a shake animation to simulate collision effects
    public Transform target; // target the camera looks at 
    public Vector3 offset;
    public bool fix = false;

    // plays a shake animation using an animator attached to the camera object, essentially altering the camera's position for the duration of  the animation
    public void CamShake()
    {
        ShakeAnimation.SetTrigger("shake");
    }

    private void Update()
    {
        if (fix==false)
        {            
        transform.position = target.position + offset;
        }

    }

    public void FixCam() { fix = true; }
    public void ReleaseCam() { fix = false; }

    public void SetPos(Vector3 pos)
    {
        transform.position = pos + offset;
    }


}