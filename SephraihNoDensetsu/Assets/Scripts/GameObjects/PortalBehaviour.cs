using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum PortalDestinationType { SameSceneSubArea, CrossScene }

public class PortalBehaviour : MonoBehaviour
{
    public PortalDestinationType destinationType = PortalDestinationType.SameSceneSubArea;
    public string targetSceneName;
    public string targetSpawnPointId;

    private void FixedUpdate()
    {

        Collider2D[] overlapColliders = Physics2D.OverlapCircleAll(transform.position, 0.3f); //a circle located at the portal's position scanning for any colliders overlapped

        foreach (Collider2D collider in overlapColliders)
            if (collider.isTrigger && collider.CompareTag("Player")) // all enemy colliders, each character has 2 colliders, only the trigger collider is used
            {
                Use();
                return;
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
