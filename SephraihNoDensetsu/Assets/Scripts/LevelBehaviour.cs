using UnityEngine;

// Marker component identifying a Dungeon Level's root GameObject - DungeonMap.GoToExit() finds
// "which level does this PortalExit belong to" by walking up the hierarchy
// (GetComponentInParent<LevelBehaviour>()) and reading the Level's sibling index as its 1-based
// level number. No longer holds obstacleMap/boundaryMap Tilemap references - those only ever fed
// DungeonMap.Unstuck(), which is a physics-overlap check now (see DungeonMap.cs) and works across
// any number of obstacle-tier tilemaps without needing specific references at all.
public class LevelBehaviour : MonoBehaviour
{
}
