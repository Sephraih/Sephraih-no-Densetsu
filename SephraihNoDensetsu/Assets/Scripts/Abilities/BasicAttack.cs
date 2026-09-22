using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//simple basic attack, see multislash for explanation of functionality
public class BasicAttack : Ability
{
    private int dmg = 100;


    public LayerMask units;

    // The slash used to be a standalone particle effect with no character animation behind it -
    // now that real slash-sprite animations exist (starting with AttackDown), that particle
    // becomes optional. Off by default so the new animation is what actually plays; flip this on
    // per-instance (e.g. mob, which still uses Jätter's art and has no AttackDown state yet) to
    // fall back to the old look instead of attacking silently.
    public bool useParticleSlashEffect = false;

    // Size of the OverlapBoxAll hit-detection box below - was private (shared, identical value for
    // every character regardless of size) until Gobking's bigger Transform.localScale exposed the
    // need for this to scale per-character too: Collider2D/NavMeshAgent both have their own separate
    // scale-awareness stories (see GoblinBehaviour.meleeRange's own comment), but this box has NONE
    // at all - it's a flat world-space size with no connection to the user's Transform.localScale
    // whatsoever. Serialized so a bigger (or smaller) character's own BasicAttack instance can be
    // tuned to match their own actual reach instead of silently sharing this default forever.
    [SerializeField] float attackRangeX = 2.5f;
    [SerializeField] float attackRangeY = 1.5f;

    private GameObject slashEffect;


    public Gradient particleColorGradient;


    private void Start()
    {
        slashEffect = Resources.Load("prefabs/Effects/ParticleSlashPrefab") as GameObject;
        acd = 1;
    }
    void Update()
    {
        if (cd >= 0)
        {
            cd -= Time.deltaTime;
        }
    }
    public void Attack()
    {
        Use();
    }

    // Cursor-aimed variant: Use() (called directly by AI callers, and by this project's other player-
    // input paths) keeps attacking in whatever direction the character is already facing/moving -
    // unchanged. This, the actual mouse-triggered path (reached via AbilityController.InvokeMouse ->
    // Ability.InvokeMouse -> this), instead faces/aims toward wherever the cursor currently is.
    // LookAt() already updates BOTH the Animator's moveX/moveY (so PlayDirectionalAttack resolves the
    // correct directional clip - Up/Down/Left/Right) AND attackPos (the OverlapBoxAll hit-detection
    // anchor used below in Use()) from one shared call - no separate Animator setup needed, since
    // PlayDirectionalAttack/GetFacingDirectionName only ever reads back whatever's currently in
    // moveX/moveY, regardless of whether movement or this set it.
    public override void UseMouse()
    {
        user.GetComponent<MovementController>().LookAt(MousePosition());
        Use();
    }




    public override void Use()
    {

        if (cd <= 0)
        {
            user.GetComponent<MovementController>().PlayDirectionalAttack("Attack");

            if (useParticleSlashEffect)
            {
                // instantiate slash prefab
                GameObject slash = Instantiate(slashEffect, user.transform.position + attackPos.localPosition, Quaternion.identity);

                //effect
                slash.transform.parent = user.transform;
                slash.transform.Rotate(Mathf.Atan2(attackPos.localPosition.x, attackPos.localPosition.y) * Mathf.Rad2Deg, +90, 0);
                Destroy(slash, 0.2f);
            }

            //determine damaged enemies, apply damage
            Collider2D[] enemiesToDamage = Physics2D.OverlapBoxAll(attackPos.position, new Vector2(attackRangeX, attackRangeY), attackPos.localPosition.x * 90, units);
            for (int i = 0; i < enemiesToDamage.Length; i++)
            {
                if (enemiesToDamage[i].isTrigger && enemiesToDamage[i].GetComponent<StatusController>().teamID != user.transform.GetComponent<StatusController>().teamID)
                    enemiesToDamage[i].GetComponent<HealthController>().TakeDamage(dmg, user.transform);

            }
            cd = acd;

        }
    }

}