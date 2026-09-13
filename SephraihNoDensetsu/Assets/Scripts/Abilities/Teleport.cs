using UnityEngine;

public class Teleport : Ability
{
    public GameObject teleportEffect; // sparse world-space sparks left trailing behind as the user walks away
    public GameObject teleportOutlineEffect; // bold ring flash rigidly attached to the user

    // Off by default - the player's own Teleport relies on TryFindWalkableLanding's deliberate
    // "can land in a sealed-off pocket" behavior (see that method's own doc comment), which is fine
    // for the player since they're never NavMeshAgent-pathed - if a landing spot happens to be
    // disconnected from the rest of the floor, the player just explores/walks out some other way,
    // never gets structurally "stuck." A NavMeshAgent-driven caster (WizardBehaviour's own flee-
    // teleport, GetComponentInChildren<AbilityController>().Invoke(6, ...)) has no such fallback:
    // GetPathDirection can never route across a genuinely disconnected pocket by definition, so a
    // wizard landing in one after fleeing is stranded outside the reachable navmesh permanently
    // (confirmed live: Dungeon Level3, wizard fled into an out-of-bounds sliver and never pathed
    // back). Set true on a per-instance basis (e.g. the wizard's own Teleport component, on its
    // Abilities child) to use TryFindReachableLanding instead - same connectivity mask ordinary
    // NavMeshAgent pathing uses, so a landing that passes it is guaranteed reachable-back-from by
    // construction, not just less likely to fail. Leave false for the player's own instance.
    [SerializeField] bool requireReachableLanding = false;

    // Two separate effects, not one: a single system tuned to look right both as a bold "landing
    // flash" AND a sparse decaying trail fought its own settings - the flash needs to stay dense and
    // rigidly glued to the user (Local sim space), while the trail needs to be sparse and left behind
    // in World space as the user moves. teleportOutlineEffect handles the former, teleportEffect the
    // latter; both parented to the user (worldPositionStays so they spawn exactly at the landing
    // spot), but only the outline's own Local sim space makes it actually follow afterward.
    void SpawnTeleportEffects()
    {
        // No downward offset here anymore - that -0.7 was tuned for the old ground-level ring
        // effect. Both effects now emit from the player's own sprite silhouette (see
        // TeleportOutlineEffect/TeleportEffect's Sprite shape), which already aligns correctly with
        // the character when spawned at its exact position.
        Vector3 spawnPos = user.position;
        GameObject outline = Instantiate(teleportOutlineEffect, spawnPos, Quaternion.identity);
        outline.transform.SetParent(user, true);
        Destroy(outline, 0.3f); // real-world: outline's own duration(0.2s)+lifetime(0.08s) - see TeleportOutlineEffect.prefab

        GameObject trail = Instantiate(teleportEffect, spawnPos, Quaternion.identity);
        trail.transform.SetParent(user, true);
        Destroy(trail, 0.65f); // real-world: trail's own duration(0.3s)+lifetime(0.3s) - see TeleportEffect.prefab. Note
        // Destroy() delays are real/unscaled time, unlike the particle systems' own duration/lifetime
        // fields, which are further divided by each system's own simulationSpeed (2.5x here) before
        // they translate to real seconds - a subtlety that cost real debugging time this session.
    }

    public override void Use()
    {
        Vector3 direction = user.transform.position - attackPos.position; // get the direction the caster is facing
        direction.Normalize(); // ignore distance
        user.GetComponent<UnitController>().SetSaveSpot(user.transform.position);
        // Single-shot: aim for the full range and let TryFindWalkableLanding's own search radius
        // handle snapping onto nearby navmesh - no straight-line obstacle check (no SpellBlocked)
        // and no shrink-until-clear fallback anymore. NavMesh itself is already the only gate that
        // matters: a caster teleport is allowed to land in a walkable pocket that's otherwise sealed
        // off by an ordinary wall (e.g. the top of a tower, by design - see TryFindWalkableLanding's
        // own doc comment), and can never land beyond the map's true edge since no floor/navmesh
        // exists out there in the first place (MapBoundary only generates floor within its own
        // extent). This also fixes the old "shortens distance and stops short of a wall/corner even
        // though the aimed spot is fine" behavior - it either lands at/near the full aimed distance,
        // or (aiming at something with no nearby navmesh at all, e.g. straight into a thick solid
        // mass) doesn't fire this cast, rather than creeping backward along the aim line.
        // TryFindWalkableLanding still refuses a landing behind a "Spell Boundary"-tagged obstacle
        // (Obstacle.BlocksSpell) - a real NavMesh connectivity check, not a raycast, so it can't be
        // fooled by a destination merely sitting near a wall's corner the way the old SpellBlocked
        // raycast was.
        Vector2 candidate = (Vector2)user.transform.position + (Vector2)direction * range;
        bool foundLanding = requireReachableLanding
            ? TryFindReachableLanding(user.transform.position, candidate, DefaultLandingSearchRadius, out Vector2 landing)
            : TryFindWalkableLanding(user.transform.position, candidate, DefaultLandingSearchRadius, out landing);
        if (foundLanding)
        {
            if (cd <= 0f) // if ability ready to use
            {
                user.GetComponent<MovementController>().LookAt(attackPos.position);
                user.transform.position = landing;
                SpawnTeleportEffects();

                cd = acd; // start cooldown
            }
        }
    }

    public override void UseMouse()
    {
        Vector2 mp = MousePosition();
        Vector2 direction = mp - new Vector2(user.transform.position.x, user.transform.position.y); // get the direction the caster is facing
        user.GetComponent<PlayerController>().SetSaveSpot(user.transform.position);

        
        float distance = direction.magnitude;
        direction.Normalize(); // ignore distance
        if (distance > range) distance = range; //set to max tp range if mouse further away
        // Single-shot - see the comment in Use() above for why there's no more SpellBlocked check
        // or shrink-until-clear loop, and for the Spell Boundary connectivity check that replaced it.
        Vector2 candidate = (Vector2)user.transform.position + direction * distance;
        bool foundLanding = requireReachableLanding
            ? TryFindReachableLanding(user.transform.position, candidate, DefaultLandingSearchRadius, out Vector2 landing)
            : TryFindWalkableLanding(user.transform.position, candidate, DefaultLandingSearchRadius, out landing);
        if (foundLanding)
        {
            if (cd <= 0f) // if ability ready to use
            {
                user.GetComponent<MovementController>().LookAt(mp);
                user.transform.position = landing;
                SpawnTeleportEffects();
                cd = acd; // start cooldown
            }
        }
    }




}
