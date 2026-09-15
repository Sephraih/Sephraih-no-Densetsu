using UnityEditor;
using UnityEngine;

// Manual fallback for PortalTileSync's own live Tilemap.tilemapTileChanged reaction - covers
// whatever that event doesn't (a copy-pasted PortalTiles Tilemap that already has tiles baked in
// before OnEnable ever subscribed, an undo that doesn't replay through SetTile, etc). Scoped to
// every currently-loaded scene, matching how a MapArea can host more than one PortalTiles instance
// (Dungeon-style scenes have one per Level) - same "operate on everything currently open" scope
// Tools/Sephraih/Sync NavMesh Obstacle Proxies already uses.
public static class PortalTileSyncTool
{
    [MenuItem("Tools/Sephraih/Sync Portal Tiles")]
    public static void SyncAll()
    {
        var modules = Object.FindObjectsByType<PortalTileSync>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var m in modules)
            m.Sync();
        Debug.Log($"[PortalTileSyncTool] Synced {modules.Length} PortalTileSync instance(s) across all loaded scenes.");
    }
}
