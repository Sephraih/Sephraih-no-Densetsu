using UnityEngine;

// Single shared source of truth for "how far can anything reasonably see, chase, or shoot" - a
// screen-relative reference distance, computed once from the camera's field of view rather than
// hand-picked per ability/enemy type. Every specific range (an enemy's vision, its awareness/
// detection tiers, its return leash, a spell's max travel distance) is expressed as a PERCENTAGE
// of this single value instead of its own independent absolute number, so retuning the camera's
// FOV or the game's overall "how far can I see" feel is one edit here instead of hunting down
// every prefab/ability that happens to also encode a distance.
//
// Deliberately NOT recomputed live from the camera at runtime - if the player can zoom the camera
// later, gameplay ranges should stay fixed at whatever this value says, not shift with the zoom.
// FieldOfView is a baked gameplay-balance number, hand-edited here when it needs retuning.
//
// Starting value (9.6) derived from the camera as configured this session: orthographic,
// orthographicSize 6, aspect ~1.777 (16:9) -> world half-width ~10.66 -> 0.9x that ~9.6. Meant as
// a starting point for hand-tuning, not a value to treat as exact/load-bearing on its own.
[CreateAssetMenu(fileName = "RangeSettings", menuName = "Sephraih/Range Settings")]
public class RangeSettings : ScriptableObject
{
    public float FieldOfView = 9.6f;
}
