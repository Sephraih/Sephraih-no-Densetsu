using UnityEngine;

public class Teleport : Ability
{
    public GameObject teleportEffect; //effect to be displayed on teleport


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
        if (TryFindWalkableLanding(user.transform.position, candidate, DefaultLandingSearchRadius, out Vector2 landing))
        {
            if (cd <= 0f) // if ability ready to use
            {
                user.GetComponent<MovementController>().LookAt(attackPos.position);
                user.transform.position = landing;
                GameObject tef = Instantiate(teleportEffect, user.position + new Vector3(0, -0.7f, 0), Quaternion.Euler(0f, 0f, 0)); //instantiate effect prefab at position and rotation
                //tef.transform.parent = transform; // make child of the charging character so its emission point moves along with it
                Destroy(tef, 0.5f); //free up memory

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
        if (TryFindWalkableLanding(user.transform.position, candidate, DefaultLandingSearchRadius, out Vector2 landing))
        {
            if (cd <= 0f) // if ability ready to use
            {
                user.GetComponent<MovementController>().LookAt(mp);
                user.transform.position = landing;
                GameObject tef = Instantiate(teleportEffect, user.transform.position + new Vector3(0, -0.7f, 0), Quaternion.Euler(0f, 0f, 0)); //instantiate effect prefab at position and rotation
                //tef.transform.parent = transform; // make child of the charging character so its emission point moves along with it
                Destroy(tef, 0.5f); //free up memory
                cd = acd; // start cooldown
            }
        }
    }




}
