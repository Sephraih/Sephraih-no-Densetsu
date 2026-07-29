using UnityEngine;

// A placeable destination marker a PortalBehaviour can target directly (drag-and-drop in the
// Editor), instead of a hand-typed string ID that has to separately match a SpawnPoint elsewhere -
// that indirection is how the dungeon's single shared "Entry" spawn point ended up sitting at
// (0,0,0), disconnected from every level's actual layout, since nothing forced the string and the
// position to stay in sync. Position-only: a portal that targets this lands the player right here.
public class PortalExit : MonoBehaviour { }
