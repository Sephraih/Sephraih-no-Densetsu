using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    // Reused every Move() call to avoid a per-call List allocation - one small buffer per unit, not
    // shared across units (each unit's Move() call fully consumes and finishes with it before any
    // other unit's runs, so a shared/static buffer isn't needed for correctness, just isn't worth the
    // subtlety when a plain instance field is just as cheap).
    private readonly List<ContactPoint2D> contactsBuffer = new List<ContactPoint2D>(8);

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

    // Prevents a visible "vibrate against the wall" wobble when holding a direction straight into a
    // static obstacle. rb.linearVelocity gets reasserted from raw input every physics step with no
    // awareness of what's currently being touched - Unity's own collision solver zeroes/redirects the
    // into-wall velocity component and nudges the body back out each step, but the very next Move()
    // call was blindly reasserting full speed back into the wall, undoing that correction every single
    // step (confirmed as the actual cause - moving the call site from Update to FixedUpdate, the more
    // commonly-suspected culprit, did NOT fix it on its own). This strips the component of desired
    // velocity pointing into a contact whose other body is anything but Dynamic - i.e. Kinematic,
    // Static, or no Rigidbody2D at all. Confirmed live this needs to cover BOTH: this project's
    // tilemap-backed wall tiers (BoundaryTiles/HighTiles/LowTiles/TowerWallObstacle/etc., every one of
    // them) use a Static Rigidbody2D on their CompositeCollider2D, while discrete-collider obstacle
    // prefabs (trees) use Kinematic - an earlier version of this check only excluded Kinematic,
    // silently skipping every tilemap wall in the game (Static != Kinematic) while still working
    // against trees, which is exactly backwards from what "walking into a wall wobbles" needed. Every
    // unit (player and enemies alike) uses Dynamic, so contact with another unit is still deliberately
    // left alone here, preserving Box2D's normal mass/impulse-based push response between units -
    // shoving another character still works exactly as before. Only removing the INTO-the-surface
    // component (not zeroing velocity outright) is what lets a diagonal input still slide along the
    // wall instead of fully stopping the instant any part of the input points at it.
    // Shared across all units' Move() calls - batched and only flushed to disk periodically (see
    // FlushDebugLogIfDue below). The FIRST version of this logging called File.AppendAllText (open +
    // write + close a file handle) every single physics step while any contact existed - at ~50Hz for
    // several seconds that's enough file I/O to plausibly hang the Editor outright (confirmed live:
    // the game froze during exactly this kind of sustained wall-contact test). Buffering in memory and
    // writing in large batches, plus skipping frames with zero contacts entirely (the vast majority),
    // cuts real file I/O by roughly two orders of magnitude.
    static readonly System.Text.StringBuilder debugLogBuffer = new System.Text.StringBuilder();
    static int debugLogPendingLines = 0;
    const int DebugLogFlushThreshold = 40;

    Vector2 ClampVelocityAgainstStaticContacts(Vector2 desiredVelocity)
    {
        int contactCount = rb.GetContacts(contactsBuffer);
        if (contactCount == 0) return desiredVelocity; // nothing to clamp, nothing worth logging

        Vector2 originalDesired = desiredVelocity;

        foreach (var contact in contactsBuffer)
        {
            // Do NOT trust which of rigidbody/otherRigidbody is "self" vs "the other body" - confirmed
            // live via a full field dump that Rigidbody2D.GetContacts() does not reorient these to the
            // calling rigidbody's perspective the way the names imply: querying from the PLAYER's own
            // rb returned contact.rigidbody = the WALL (Static) and contact.otherRigidbody = the PLAYER
            // itself (Dynamic) - backwards from the assumption both prior versions of this method made,
            // which is why the clamp never engaged for ANYTHING, tree or wall, the entire time (the
            // skip check was comparing the player's own always-Dynamic body against itself). Comparing
            // directly against this component's own known `rb` reference sidesteps trusting Unity's
            // naming here at all.
            Rigidbody2D otherRb = (contact.rigidbody == rb) ? contact.otherRigidbody : contact.rigidbody;
            bool skippedDynamic = otherRb != null && otherRb.bodyType == RigidbodyType2D.Dynamic;
            if (!skippedDynamic)
            {
                // Similarly, don't trust contact.normal's sign from memory/docs either - resolve it
                // geometrically instead, via the contact point's position relative to this body, so
                // it's correct regardless of which "side" Box2D happened to report first.
                Vector2 towardOther = contact.point - rb.position;
                Vector2 intoDir = contact.normal;
                if (Vector2.Dot(intoDir, towardOther) < 0f) intoDir = -intoDir;

                float intoComponent = Vector2.Dot(desiredVelocity, intoDir);
                if (intoComponent > 0f)
                    desiredVelocity -= intoComponent * intoDir;

                if (DebugWallClamp)
                    debugLogBuffer.AppendLine("  APPLIED collider=" + contact.collider.name + " otherCollider=" + contact.otherCollider.name +
                        " rigidbody=" + (contact.rigidbody != null ? contact.rigidbody.name + "/" + contact.rigidbody.bodyType : "NULL") +
                        " otherRigidbody=" + (contact.otherRigidbody != null ? contact.otherRigidbody.name + "/" + contact.otherRigidbody.bodyType : "NULL") +
                        " resolvedOtherRb=" + (otherRb != null ? otherRb.name + "/" + otherRb.bodyType : "NULL") +
                        " normal=" + contact.normal + " intoDir=" + intoDir + " separation=" + contact.separation + " intoComponent=" + intoComponent);
            }
            else if (DebugWallClamp)
            {
                debugLogBuffer.AppendLine("  SKIPPED collider=" + contact.collider.name + " otherCollider=" + contact.otherCollider.name +
                    " resolvedOtherRb=" + (otherRb != null ? otherRb.name + "/" + otherRb.bodyType : "NULL"));
            }
        }

        if (DebugWallClamp)
        {
            debugLogBuffer.AppendLine("[WallClampDebug] t=" + Time.time.ToString("F3") + " unit=" + gameObject.name +
                " pos=" + ((Vector2)transform.position).ToString("F4") + " contactCount=" + contactCount +
                " desiredBefore=" + originalDesired.ToString("F4") + " desiredAfter=" + desiredVelocity.ToString("F4"));
            debugLogPendingLines++;
            if (debugLogPendingLines >= DebugLogFlushThreshold) FlushDebugLog();
        }

        return desiredVelocity;
    }

    static void FlushDebugLog()
    {
        if (debugLogBuffer.Length == 0) return;
        File.AppendAllText(Application.persistentDataPath + "/WallClampDebug.log", debugLogBuffer.ToString());
        debugLogBuffer.Clear();
        debugLogPendingLines = 0;
    }

    // Catches whatever's left in the buffer when the unit (or Play mode) stops, so the last partial
    // batch isn't silently lost.
    void OnDisable()
    {
        if (DebugWallClamp) FlushDebugLog();
    }

    // Diagnostic toggle (off by default) - logs every Move() call (position, contact count, per-contact
    // normal/separation, velocity before/after clamp) to a plain file at
    // Application.persistentDataPath/WallClampDebug.log - NOT just Debug.Log, since this project's MCP
    // console-log capture does not retain anything logged during an actual Play Mode session (confirmed
    // live: it only ever shows Edit-mode script-execute output, nothing from a 10-second Play Mode test
    // in between). Flip to true for a future contact/velocity investigation, see
    // project_wall_wobble_unstuck memory for the bug this was built to diagnose.
    public static bool DebugWallClamp = false;

    //md is the movement direction, msi is a value between zero and one to determine movement speed from input
    public void Move(Vector2 md, float msi)
    {
        if (!stuck && !stunned)
        {
            this.md = md;
            this.msi = msi;
            Vector2 desiredVelocity = md * msi * this.GetComponent<StatusController>().mvspd; //direction, input strength, character movement speed
            rb.linearVelocity = ClampVelocityAgainstStaticContacts(desiredVelocity);
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

    // Raw (moveX, moveY) facing vector - the same signal Aniwalk's blend tree and
    // GetFacingDirectionName() read, but unsnapped (continuous, not cardinal) - usable directly for
    // angle-based checks (e.g. EnemyController's vision-cone detection) rather than animation state.
    public Vector2 GetFacingVector()
    {
        return animator.isInitialized ? new Vector2(animator.GetFloat("moveX"), animator.GetFloat("moveY")) : Vector2.zero;
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
