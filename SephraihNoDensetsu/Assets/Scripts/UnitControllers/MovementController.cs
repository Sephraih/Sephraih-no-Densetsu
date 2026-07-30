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
  
    // Assigned in Awake, not Start: Start() only guarantees ordering relative to this script's own
    // Update() - it does NOT guarantee running before a DIFFERENT script's Update() on the same
    // object. When a whole level's enemies activate together (SetActive(true) cascading through a
    // level's hierarchy), MobBehaviour.Update() could run before MovementController.Start() had a
    // chance to set rb, throwing a NullReferenceException in Move() (confirmed live - all of a
    // level's mobs hit this in the same frame right as the level activated). Awake() has the
    // stronger guarantee (every Awake() in the scene completes before any Start()/Update()), and
    // GetComponent<Rigidbody2D>() has no dependency on any other script's own initialization, so
    // there's no reason this needs to wait until Start().
    void Awake()
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
            rb.linearVelocity = md * msi * this.GetComponent<StatusController>().mvspd; //direction, input strength, character movement speed
            MovementAnimation();
        }
        if (stunned)
        {
            rb.linearVelocity = Vector3.zero;
            // Move() skips MovementAnimation() entirely while stunned (above), so without this the
            // animator's "Speed" param is left at whatever it was the instant stun began - if the
            // character was mid-walk, it stays stuck on the walking motion, frozen-but-still-playing,
            // for the whole stun. moveX/moveY are deliberately left untouched (still whatever
            // direction the character last faced), so forcing Speed to 0 resolves the blend tree to
            // that same direction's IDLE motion instead - stunned reads as "standing still facing
            // the direction they got hit from," not "walking in place."
            if (animator.isInitialized) animator.SetFloat("Speed", 0f);
        }
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
    // How far above this character's own pivot the stun effect spawns. A flat offset rather than
    // per-model art (a dedicated stunned pose/animation) is deliberately the cheap option here -
    // one visual effect works for every current and future character model without needing a
    // bespoke stun animation for each.
    public float stunEffectHeight = 0.9f;
    private GameObject stunEffectInstance;

    // Getting stunned again while already stunned (e.g. two charge attacks landing back to back)
    // starts a second, fully independent StunCoroutine - Unity doesn't cancel/replace running
    // coroutines by default. Without tracking how many are active, each one blindly does its own
    // setup/teardown: the second capture of "original body type" would actually read back
    // Kinematic (since the first coroutine already set it), permanently stranding the character as
    // Kinematic once the second coroutine "restores" it; and each Instantiate() overwrites the
    // single shared stunEffectInstance reference, orphaning whichever instance isn't referenced
    // anymore - nothing is left to ever Destroy() it, so it just keeps looping indefinitely. This
    // depth counter makes overlapping stuns share one setup/teardown: only the first entry captures
    // state and spawns the effect, only the last exit (depth back to 0) restores/destroys it.
    private int stunDepth = 0;
    private RigidbodyType2D preStunBodyType;

    IEnumerator StunCoroutine(float time)
    {
        stunDepth++;
        if (stunDepth == 1)
        {
            stunned = true;

            // Zeroing velocity in Move() only stops the stunned character from moving ITSELF - a
            // normal dynamic Rigidbody2D sitting still is still fully shovable by anything solid
            // that walks into it (e.g. the attacker that just landed the stun standing close by),
            // and stunned means the player can't move away to mask it. Kinematic bodies are
            // immovable by collision response, so toggling to Kinematic for the stun's duration -
            // the same trick ChargeAttack uses for its own dash - makes the stunned character
            // genuinely immune to being pushed instead of just not pushing itself.
            var rb2d = GetComponent<Rigidbody2D>();
            preStunBodyType = rb2d.bodyType;
            rb2d.bodyType = RigidbodyType2D.Kinematic;
            rb2d.linearVelocity = Vector2.zero;

            var stunEffectPrefab = Resources.Load("Prefabs/Effects/StunEffect") as GameObject;
            if (stunEffectPrefab != null)
            {
                stunEffectInstance = Instantiate(stunEffectPrefab, transform);
                stunEffectInstance.transform.localPosition = new Vector3(0, stunEffectHeight, 0);
            }
        }

        float timePassed = 0;
        while (timePassed < time)
        {
            timePassed += 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        stunDepth--;
        if (stunDepth == 0)
        {
            if (stunEffectInstance != null) { Destroy(stunEffectInstance); stunEffectInstance = null; }
            GetComponent<Rigidbody2D>().bodyType = preStunBodyType;
            stunned = false;
        }
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

    // Force-plays a named Animator state from the start, immediately, regardless of what's
    // currently playing - used for attack animations (BasicAttack/MultiSlash). Deliberately
    // Play() rather than a Trigger+transition setup: a trigger would have to wait for an "allow
    // interruption" transition to be configured correctly to feel responsive on a fast multi-hit
    // combo (MultiSlash can re-fire every ~0.1s, well inside the attack clip's own length) - Play()
    // just snaps to frame 0 of the state every time, so a rapid combo always looks like each hit
    // restarts the swing, no transition-graph tuning required. Currently only "AttackDown" exists;
    // characters without that state in their own Animator Controller (e.g. Jätter's mob, which
    // still uses the old particle-only attack) silently no-op here - Unity logs a warning but
    // nothing else happens, so their damage logic is unaffected.
    public void PlayAttackAnimation(string stateName)
    {
        if (animator.isInitialized) animator.Play(stateName, 0, 0f);
    }

    // How far into an attack clip (0-1 normalized) to resume from when re-triggered while already
    // mid-attack, instead of restarting at frame 0 - skips replaying the wind-up/"get in position"
    // frames on a rapid re-attack (e.g. mashing BasicAttack, or MultiSlash's fast combo), which
    // otherwise looks like the swing keeps resetting itself. ~0.27 matches "frame 3 of 11" on the
    // Up/Down clips; since this is a fraction rather than an absolute frame count it scales
    // sensibly to the shorter Left/Right clips too. Tune per feel.
    public float repeatAttackStartFraction = 0.27f;

    // States listed here always restart at frame 0 on a repeat hit instead of skipping ahead by
    // repeatAttackStartFraction - for clips whose meaningful motion is weighted toward the END of
    // the clip rather than spread evenly (e.g. AttackRight3/AttackLeft3, which after the side-combo
    // reshuffle hold the side4 art), the normal skip-ahead cuts into the actual swing instead of
    // just skipping a redundant wind-up, so those clips look wrong when resumed mid-combo.
    public List<string> repeatFromStartStates = new List<string> { "AttackRight3", "AttackLeft3" };

    // All 4 directional attack states now exist (AttackUp/Down/Left/Right) - this picks the one
    // matching whichever way the character is CURRENTLY drawn facing, read back from the same
    // moveX/moveY params the Aniwalk blend tree itself uses, so attack direction always agrees
    // with the last direction actually shown on screen (whether the character is mid-walk or
    // standing idle - Move()/WalkTowards()/LookAt() all keep these params current even at rest).
    //
    // variantSuffix optionally selects a combo-alternate clip for the resolved direction (e.g.
    // "2"/"3"/"4" -> AttackRight2/AttackRight3/AttackRight4, used by MultiSlash's combo steps) -
    // not every direction has every variant authored. Animator.HasState() checks whether the
    // requested variant actually exists in this character's controller; if not, this steps DOWN
    // through progressively lower variants (e.g. requested "4" but only "2" exists -> tries "3",
    // then "2", using the first one found) before finally falling back to the plain directional
    // state - so a direction missing its own highest variant reuses its best existing one instead
    // of jumping straight back to the base attack. On a character whose controller has no variants
    // at all, this naturally bottoms out at the base attack same as before.
    public void PlayDirectionalAttack(string actionPrefix, string variantSuffix = "")
    {
        if (!animator.isInitialized) return;
        // Every AttackX state is tagged "Attack" in the Animator Controller - checking the tag
        // (rather than listing all state names here) means any attack state added later is
        // covered automatically without touching this method.
        bool alreadyAttacking = animator.GetCurrentAnimatorStateInfo(0).IsTag("Attack");

        string direction = GetFacingDirectionName();
        string stateName = actionPrefix + direction; // base attack, always assumed to exist

        int variantNum = variantSuffix == "" ? 0 : int.Parse(variantSuffix);
        for (int v = variantNum; v >= 2; v--)
        {
            string candidate = actionPrefix + direction + v;
            if (animator.HasState(0, Animator.StringToHash(candidate)))
            {
                stateName = candidate;
                break;
            }
        }

        float startTime = (alreadyAttacking && !repeatFromStartStates.Contains(stateName)) ? repeatAttackStartFraction : 0f;
        animator.Play(stateName, 0, startTime);
    }

    private string GetFacingDirectionName()
    {
        float x = animator.GetFloat("moveX");
        float y = animator.GetFloat("moveY");
        if (Mathf.Abs(x) > Mathf.Abs(y)) return x > 0 ? "Right" : "Left";
        return y > 0 ? "Up" : "Down";
    }

    // ability to face a target direction without moving
    public void LookAt(Vector2 target)
    {
        if (animator.isInitialized)
        {
            target = target - new Vector2(transform.position.x,transform.position.y);
            target.Normalize();
            animator.SetFloat("moveX", target.x);
            animator.SetFloat("moveY", target.y);
            attackPos.transform.localPosition = target.normalized;
            animator.SetFloat("Speed", 0.0f);
        }
    }

}
