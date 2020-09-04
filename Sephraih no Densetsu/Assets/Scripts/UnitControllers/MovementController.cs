using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// every character object has a movement controller, enabling it to move
public class MovementController : MonoBehaviour
{
    public Animator animator; // animator displaying movement based on zero to one speed and -1 to 1 x and y directional input.
    public GameObject attackPos; //the unit's attackPos transform

    private Vector2 md; // the movement direction determined by the character's player or bot controller
    private float msi;  // the movement speed input, which is the strength of movement input from zero, not moving, to one, moving at full speed determined by the unit's max speed

    private Rigidbody2D rb; // physical entity of the character, where velocity is applied to
    public bool stuck; // whether the character may not move aside from a fixed logic defined in the function causing the character to be stuck
    public bool stunned; // whether the character is stunned, meaning it cannot move at all.

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    //md is the movement direction, msi is a value between zero and one to determine movement speed from input
    public void Move(Vector2 md, float msi)
    {
        if (!stuck && !stunned)
        {
            this.md = md;
            this.msi = msi;
            rb.velocity = md * msi * this.GetComponent<StatusController>().mvspd; //direction, input strength, character movement speed
            MovementAnimation();
        }
        if (stunned) { rb.velocity = Vector3.zero; }
    }

    public void Idle() {

        Vector3 movementDirection = Camera.main.ScreenToWorldPoint(Input.mousePosition) + new Vector3(0, 0, 1) - transform.position; //move towards target
        movementDirection.Normalize(); // filter distance
        float msi = Mathf.Clamp(movementDirection.magnitude, 0.0f, 1.0f); // zero or one

        GetComponent<MovementController>().Move(movementDirection, msi); // move through controller
    }
    
    // each method must be run through in its entirety in each frame, therefore a method may not wait or be aware of time passed
    // a coroutine enables doing a seperate task over a defined time frame without blocking the flow of the game
    public void Stun(float time) {
        StartCoroutine(StunCoroutine(time));
    }
    IEnumerator StunCoroutine(float time)
    {
        float timePassed = 0;
        stunned = true;
        while (timePassed < time)
        {
            timePassed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }
        stunned = false;
    }


    // animate with help of the animator
    public void MovementAnimation()
    {
        //movement animation
        if (md != Vector2.zero && !stuck &&!stunned)
        {
            animator.SetFloat("moveX", md.x);
            animator.SetFloat("moveY", md.y);

        }
        if(!stunned)animator.SetFloat("Speed", msi);
        if (stunned) animator.SetFloat("Speed", 0.0f);

    }

    // walking animation in direction of a specific target point
    public void WalkTowards(Vector2 target) {
        if (animator.isInitialized)
        {
            animator.SetFloat("moveX", target.x);
            animator.SetFloat("moveY", target.y);
            attackPos.transform.localPosition = target.normalized;
            animator.SetFloat("Speed", 1.0f);
        }
    }

    // ability to face a target direction without moving
    public void LookAt(Vector2 target)
    {
        if (animator.isInitialized)
        {
            target = target - new Vector2(transform.position.x,transform.position.y);
            animator.SetFloat("moveX", target.x);
            animator.SetFloat("moveY", target.y);
            attackPos.transform.localPosition = target.normalized;
            animator.SetFloat("Speed", 0.0f);
        }
    }

}
