using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum PortalDestinationType { SameSceneSubArea, CrossScene }

public class PortalBehaviour : MonoBehaviour
{
    public PortalDestinationType destinationType = PortalDestinationType.SameSceneSubArea;
    public string targetSceneName;
    public string targetSpawnPointId;

    // Only meaningful when destinationType is SameSceneSubArea - the specific destination this
    // portal leads to, assigned directly in the Editor rather than resolved by a map-wide "always
    // advance one level" assumption. The map (DungeonMap.OnPortalUsed) reads this to know both
    // where to place the player AND which sub-area to activate.
    public PortalExit exit;

    // Without this, Use() fired on every single FixedUpdate the player's collider overlapped the
    // portal's radius - harmless while just walking past, but a real bug the moment a level's own
    // entry spawn point lands within that same radius (common: entries are usually placed near a
    // portal). Arriving there left the player still "inside" a portal trigger the very next fixed
    // frame, immediately re-firing Use() and chaining into another transition before the player
    // could move away - surfacing as an apparently random extra teleport right after entering a
    // level. Now Use() only fires once per approach; armed again after the player fully leaves the
    // radius.
    private bool triggered = false;

    private void FixedUpdate()
    {

        Collider2D[] overlapColliders = Physics2D.OverlapCircleAll(transform.position, 0.3f); //a circle located at the portal's position scanning for any colliders overlapped

        bool playerInRange = false;
        foreach (Collider2D collider in overlapColliders)
            if (collider.isTrigger && collider.CompareTag("Player")) // all enemy colliders, each character has 2 colliders, only the trigger collider is used
            {
                playerInRange = true;
                break;
            }

        if (playerInRange && !triggered)
        {
            triggered = true;
            Use();
        }
        else if (!playerInRange)
        {
            triggered = false;
        }
    }

    void Use()
    {
        if (MapManager.Instance == null)
        {
            Debug.LogError("[PortalBehaviour] No MapManager in scene - is Bootstrap.unity loaded?");
            return;
        }

        if (destinationType == PortalDestinationType.CrossScene)
            MapManager.Instance.TravelTo(targetSceneName, targetSpawnPointId);
        else
            MapManager.Instance.CurrentMap?.OnPortalUsed(this);
    }
}
