using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Reactive per-cell particle spawner for the shared "PortalTiles" layer (one Tilemap per MapArea,
// painted with every portal's floor art project-wide - see project_portal_tile_system memory for
// the hybrid design this is half of). Deliberately NOT where the actual teleport trigger/logic
// lives - these tiles are pure decoration, painted onto whatever footprint looks right; the real
// PortalBehaviour + its own Collider2D is still hand-placed on top, independently, same as before.
// Splitting it this way sidesteps two harder problems a fully tile-derived trigger would have
// needed: knowing which painted cells belong to which of possibly several portals on one map (they
// don't need to know - it's just floor art), and keeping an auto-generated collider shape in sync
// with arbitrary paint edits.
//
// Subscribes to the static Tilemap.tilemapTileChanged event (same mechanism DualGridTilemapModule
// already uses) so painting or erasing a portal tile immediately spawns/despawns its own
// particlePrefab instance with no manual step for the common case. Sync() is also exposed publicly
// and via Tools/Sephraih/Sync Portal Tiles as a fallback for whatever the live event doesn't cover
// (a copy-pasted Tilemap that already has tiles baked in before OnEnable ever subscribed, an undo
// that doesn't replay through SetTile, etc).
[ExecuteAlways]
public class PortalTileSync : MonoBehaviour
{
    [Tooltip("Spawned once per painted tile, parented under 'particleParent' (or this object if that's " +
        "unset) at that cell's center. A real prefab instance (not a baked copy) - editing the prefab " +
        "later updates every already-placed portal tile's effect too, no re-sync needed.")]
    [SerializeField] GameObject particlePrefab;

    [Tooltip("Where spawned particle instances get parented - normally the map's shared 'MapParticles' " +
        "container, kept separate from the Grid/Tilemap hierarchy. Falls back to this GameObject's own " +
        "transform if left unset.")]
    [SerializeField] Transform particleParent;
    Transform ParticleParent => particleParent != null ? particleParent : transform;

    Tilemap tilemap;

    // Cell -> spawned child, keyed by parsing each child's own deterministic name (see
    // RebuildSpawnedFromChildren) rather than trusting this dictionary to survive a domain reload -
    // it doesn't, but the child GameObjects themselves are real persisted content, so they're the
    // actual source of truth. Rebuilding from them every Sync() is what makes this idempotent across
    // reloads instead of piling up duplicate particle instances on every OnEnable.
    readonly Dictionary<Vector3Int, GameObject> spawned = new();
    const string NamePrefix = "PortalParticle_";

    void OnEnable()
    {
        tilemap = GetComponent<Tilemap>();
        Tilemap.tilemapTileChanged += OnTilemapChanged;
        Sync();
    }

    void OnDisable()
    {
        Tilemap.tilemapTileChanged -= OnTilemapChanged;
    }

    void OnTilemapChanged(Tilemap changedTilemap, Tilemap.SyncTile[] syncTiles)
    {
        if (changedTilemap != tilemap) return;
        if (spawned.Count == 0) RebuildSpawnedFromChildren();
        foreach (var sync in syncTiles)
            SyncCell(sync.position);
    }

    // Full reconciliation: every painted cell gets an instance, every instance for a since-erased
    // cell is removed. Safe to call any number of times - never spawns a duplicate for a cell that
    // already has a live instance, whether this component just woke up fresh or has been running
    // the whole time.
    public void Sync()
    {
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        if (tilemap == null) return;

        RebuildSpawnedFromChildren();

        tilemap.CompressBounds();
        var painted = new HashSet<Vector3Int>();
        foreach (var pos in tilemap.cellBounds.allPositionsWithin)
            if (tilemap.GetTile(pos) != null) painted.Add(pos);

        var toRemove = new List<Vector3Int>();
        foreach (var kv in spawned)
            if (!painted.Contains(kv.Key)) toRemove.Add(kv.Key);
        foreach (var pos in toRemove)
        {
            DestroyGameObject(spawned[pos]);
            spawned.Remove(pos);
        }

        foreach (var pos in painted)
            SyncCell(pos);
    }

    void RebuildSpawnedFromChildren()
    {
        spawned.Clear();
        foreach (Transform child in ParticleParent)
        {
            if (child == null || !child.name.StartsWith(NamePrefix)) continue;
            var coords = child.name.Substring(NamePrefix.Length).Split('_');
            if (coords.Length == 2 && int.TryParse(coords[0], out int cx) && int.TryParse(coords[1], out int cy))
                spawned[new Vector3Int(cx, cy, 0)] = child.gameObject;
        }
    }

    void SyncCell(Vector3Int pos)
    {
        bool isPainted = tilemap.GetTile(pos) != null;
        bool hasInstance = spawned.TryGetValue(pos, out var existing) && existing != null;

        if (isPainted && !hasInstance)
        {
            if (particlePrefab == null) return;
            Vector3 worldPos = tilemap.GetCellCenterWorld(pos);
            GameObject instance = InstantiateParticle(worldPos);
            instance.name = $"{NamePrefix}{pos.x}_{pos.y}";
            spawned[pos] = instance;
        }
        else if (!isPainted && hasInstance)
        {
            DestroyGameObject(existing);
            spawned.Remove(pos);
        }
    }

    GameObject InstantiateParticle(Vector3 worldPos)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var editorInstance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(particlePrefab, ParticleParent);
            editorInstance.transform.position = worldPos;
            return editorInstance;
        }
#endif
        return Instantiate(particlePrefab, worldPos, Quaternion.identity, ParticleParent);
    }

    void DestroyGameObject(GameObject go)
    {
        if (go == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) { DestroyImmediate(go); return; }
#endif
        Destroy(go);
    }
}
