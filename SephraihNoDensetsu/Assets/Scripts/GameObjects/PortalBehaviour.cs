using UnityEngine;

public class PortalBehaviour : MonoBehaviour
{
    // Drag a SpawnPoint in and that's the whole authoring step - Use() below figures out on its
    // own whether that's a same-scene sub-area jump or a cross-scene trip, by comparing scenes.
    // Only ever holds a value while `target` lives in THIS portal's own scene: Unity cannot
    // serialize a reference to an object in a different scene file, so OnValidate (below) captures
    // a cross-scene assignment into targetSceneName/targetSpawnPointId instead and clears this
    // field right back to null - it's a live reference only when that's actually possible.
    [SerializeField] private SpawnPoint target;
    public SpawnPoint Target => target;

    // Auto-captured by OnValidate when `target` is assigned to a SpawnPoint in a different scene
    // (see above) - not meant to be hand-typed, but left as plain serialized strings rather than
    // read-only so an already-working portal can still be authored by hand if the target scene
    // isn't open in the Editor at the same time (can't drag a reference to something not loaded).
    public string targetSceneName;
    public string targetSpawnPointId;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (target == null) return;

        if (target.gameObject.scene == gameObject.scene)
        {
            // Live same-scene reference is valid and will serialize fine - this IS the target,
            // the string fields are meaningless here.
            targetSceneName = null;
            targetSpawnPointId = null;
            return;
        }

        // Cross-scene: only reachable at all because both scenes happened to be open
        // simultaneously in the Editor right now. Capture what we need into plain strings and
        // drop the reference - left as-is, it would just silently become null the next time this
        // scene is opened without the target's scene also open, which would look like data loss.
        targetSceneName = target.gameObject.scene.name;
        targetSpawnPointId = target.Id;
        target = null;
    }
#endif

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

        // CrossScene whenever a scene name is set, otherwise always delegate to the map controller
        // - even when `target` is null. A null target with no scene name is a valid, meaningful
        // state (e.g. DungeonMap's legacy LoadNextLevel() fallback for a portal deliberately left
        // unconfigured), not an error - the map controller itself decides what "no target" means.
        if (!string.IsNullOrEmpty(targetSceneName))
            MapManager.Instance.TravelTo(targetSceneName, targetSpawnPointId);
        else
            MapManager.Instance.CurrentMap?.OnPortalUsed(this);
    }
}
