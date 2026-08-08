using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;

// Editor-only utility: forces every Tilemap in all currently open scenes to fully re-evaluate its
// RuleTile neighbor matches and repaint. Exists as a diagnostic/workaround for the Tile Palette's
// interactive brush occasionally leaving stale visuals after a paint/erase stroke (tile DATA is
// correct - confirmed via scripted SetTile/SetTiles tests against MudRed.asset - but the rendered
// chunk doesn't always catch up), distinct from an actual RuleTile rule bug.
public static class TilemapRefreshTool
{
    [MenuItem("Tools/Sephraih/Tilemap/Refresh All Tiles In Open Scenes")]
    public static void RefreshAllOpenScenes()
    {
        var tilemaps = Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);
        foreach (var tilemap in tilemaps)
        {
            tilemap.RefreshAllTiles();
            EditorUtility.SetDirty(tilemap);
        }
        Debug.Log($"[TilemapRefreshTool] Refreshed {tilemaps.Length} Tilemap(s) across open scenes.");
    }
}
