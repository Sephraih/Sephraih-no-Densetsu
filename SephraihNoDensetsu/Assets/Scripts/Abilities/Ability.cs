using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ability : MonoBehaviour
{

    public float acd; // ability cd
    protected float cd = 0; //remaining cd
    public float range;
    

    
    protected Transform user;
    protected Transform attackPos;

    // Layer(s) to consider when resolving a mouse-targeted ability - Unit only, same convention as
    // BasicAttack/MultiSlash's own (separately-declared) `units` field. Named differently from
    // theirs since they redeclare their own field of that name rather than inheriting one - a
    // same-named field here would collide with theirs (Unity doesn't support two same-named
    // serialized fields across a class/subclass pair). Used by PreciseMouseTarget().
    [SerializeField] protected LayerMask mouseTargetUnits;
    [SerializeField] protected float mouseTargetRadius = 0.5f;

    public virtual void Use() {
        UseMouse();
    }

    public virtual void UseTarget(Transform target) { }
    public virtual void UseMouse() { }

    // Shared dispatch for target-based abilities (ChargeAttack, ShadowImpact) invoked via plain
    // Invoke() rather than InvokeMouse()/UseAbility(). AbilityController.Invoke(spellid, user) -
    // the path every AI caller uses (e.g. GuardBehaviour's charge/ranged attacks) - passes no
    // target of its own, so Use()'s default falls through to UseMouse(), which only makes sense
    // for the human player's actual mouse cursor. When the caller is an EnemyController that
    // already has its own resolved AI target, use that instead; otherwise (the player) fall back
    // to the normal mouse-based resolution.
    protected void UseAITargetOrMouse()
    {
        var enemyController = user.GetComponent<EnemyController>();
        if (enemyController != null && enemyController.CurrentTarget != null)
            UseTarget(enemyController.CurrentTarget);
        else
            UseMouse();
    }

    // Precise mouse targeting: only selects a target if it's actually within mouseTargetRadius of
    // the cursor's world position, unlike the old GameBehaviour.ClosestEnemyToLocation which would
    // always pick the closest enemy on the whole map regardless of how far off the cursor was.
    // Returns null when nothing hostile is under the cursor - callers must guard against that.
    protected Transform PreciseMouseTarget()
    {
        Vector2 pos = MousePosition();
        var hits = Physics2D.OverlapCircleAll(pos, mouseTargetRadius, mouseTargetUnits);
        Transform best = null;
        float bestDist = float.MaxValue;
        int myTeam = user.GetComponent<StatusController>().teamID;
        foreach (var h in hits)
        {
            var status = h.GetComponent<StatusController>();
            if (status == null || status.teamID == myTeam) continue;
            float d = Vector2.Distance(pos, h.transform.position);
            if (d < bestDist) { bestDist = d; best = h.transform; }
        }
        return best;
    }

    public void InvokeMouse(Transform user)
    {
        this.user = user;
        this.attackPos = user.GetComponent<UnitController>().attackingDirection.transform;
        UseMouse();
    }

    public void Invoke(Transform user)
    {
        this.user = user;
        this.attackPos = user.GetComponent<UnitController>().attackingDirection.transform;
        Use();
    }


    public Vector2 MousePosition()
    {
        return Camera.main.ScreenToWorldPoint(Input.mousePosition);
    }
    void Update()
    {
        if (cd >= 0)
        {
            cd -= Time.deltaTime; //decrease cooldown
        }
    }

}