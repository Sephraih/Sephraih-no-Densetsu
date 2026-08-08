using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Explicitly declares which Obstacles form a map/level's outer edge - tilemap-backed and/or
// discrete-Collider2D-backed, any mix. NavMeshObstacleSync reads this to know exactly how far a
// map's walkable floor should extend, instead of guessing from object names.
[DisallowMultipleComponent]
public class MapBoundary : MonoBehaviour
{
    [Tooltip("Every Obstacle that forms this map/level's outer edge.")]
    public List<Obstacle> BoundaryObstacles = new();

    [Tooltip("Any GameObject known to sit INSIDE this map/level's play area (e.g. its spawn point). " +
             "NavMeshObstacleSync flood-fills the floor outward from here through open (non-boundary) " +
             "tiles, so the generated floor matches the boundary's actual shape instead of just its " +
             "bounding rectangle - required for any boundary that isn't itself roughly rectangular " +
             "(a diamond/organic outline, etc). Leave unset only for a boundary that genuinely IS " +
             "rectangular; NavMeshObstacleSync falls back to the old rectangle-fill behavior with a " +
             "warning if this is missing.")]
    public Transform InteriorSeed;

    public Bounds ComputeWorldBounds()
    {
        Bounds bounds = default;
        bool has = false;

        foreach (var obstacle in BoundaryObstacles)
        {
            if (obstacle == null) continue;

            // Tilemap-backed boundaries are measured via the Tilemap's own painted-cell extent,
            // not Collider2D.bounds - a collider on a GameObject that has never been active (e.g.
            // Dungeon.unity's Level1-3, disabled until their level is reached) never gets a live
            // PhysX shape, so .bounds on it reads back degenerate/zero regardless of the tiles
            // actually painted there.
            var tilemap = obstacle.GetComponentInChildren<Tilemap>(true);
            if (tilemap != null)
            {
                tilemap.CompressBounds();
                var cb = tilemap.cellBounds;
                if (cb.size.x <= 0 || cb.size.y <= 0) continue;
                var cellSize = Vector3.Scale(tilemap.cellSize, tilemap.transform.lossyScale);
                Vector3 worldMin = CellCenterWorld(tilemap, new Vector3Int(cb.xMin, cb.yMin, cb.zMin)) - cellSize / 2f;
                Vector3 worldMax = CellCenterWorld(tilemap, new Vector3Int(cb.xMax - 1, cb.yMax - 1, cb.zMin)) + cellSize / 2f;
                var tilemapBounds = new Bounds();
                tilemapBounds.SetMinMax(
                    new Vector3(Mathf.Min(worldMin.x, worldMax.x), Mathf.Min(worldMin.y, worldMax.y), 0f),
                    new Vector3(Mathf.Max(worldMin.x, worldMax.x), Mathf.Max(worldMin.y, worldMax.y), 0f));
                if (!has) { bounds = tilemapBounds; has = true; }
                else bounds.Encapsulate(tilemapBounds);
                continue;
            }

            // includeInactive: this map's level (and thus its boundary) is routinely inactive at
            // edit time when it's not the currently-active level. Collider2D.bounds is backed by
            // the native 2D physics engine's cached shape data and silently collapses to a
            // zero-size point at transform.position when the collider's hierarchy is inactive
            // (confirmed directly - the same failure mode as Tilemap.GetCellCenterWorld() above),
            // so it can't be used here either. ColliderWorldBounds() below computes the AABB from
            // plain serialized fields (points/offset/radius/size) via Transform.TransformPoint
            // instead, which works regardless of active state.
            foreach (var col in obstacle.GetComponentsInChildren<Collider2D>(true))
            {
                if (col is TilemapCollider2D tilemapCol && tilemapCol.usedByComposite) continue;
                if (!TryColliderWorldBounds(col, out var colBounds)) continue;

                if (!has) { bounds = colBounds; has = true; }
                else bounds.Encapsulate(colBounds);
            }
        }

        return bounds;
    }

    // Tilemap.GetCellCenterWorld()/CellToLocal() both silently return degenerate values (as if
    // cellPos were always zero) when the Tilemap's GameObject hierarchy is INACTIVE - the normal
    // state for any Dungeon level other than whichever one is currently active. cellSize/
    // tileAnchor are plain serialized fields (unaffected by active state), and
    // Transform.TransformPoint is pure matrix math - computing the cell center manually from
    // those instead works correctly regardless of active state (verified against
    // GetCellCenterWorld's own correct output while active, for the identical cell).
    static Vector3 CellCenterWorld(Tilemap tilemap, Vector3Int cellPos)
    {
        Vector3 cellLocal = new Vector3(cellPos.x * tilemap.cellSize.x, cellPos.y * tilemap.cellSize.y, 0f);
        Vector3 centerLocal = cellLocal + Vector3.Scale(tilemap.cellSize, tilemap.tileAnchor);
        return tilemap.transform.TransformPoint(centerLocal);
    }

    // Active-state-independent equivalent of Collider2D.bounds - see the comment above this
    // method's only call site. Only handles the shapes actually used by this project's boundary
    // obstacles (Box/Circle/Polygon); anything else falls back to the (possibly-degenerate-if-
    // inactive) native .bounds rather than silently guessing.
    static bool TryColliderWorldBounds(Collider2D col, out Bounds bounds)
    {
        var t = col.transform;
        switch (col)
        {
            case BoxCollider2D box:
                {
                    Vector2 size = Vector2.Scale(box.size, t.lossyScale);
                    bounds = new Bounds(t.TransformPoint(box.offset), new Vector3(size.x, size.y, 0f));
                    return true;
                }
            case CircleCollider2D circle:
                {
                    float scale = Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y));
                    float r = circle.radius * scale;
                    bounds = new Bounds(t.TransformPoint(circle.offset), new Vector3(r * 2f, r * 2f, 0f));
                    return true;
                }
            case PolygonCollider2D poly:
                {
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    for (int i = 0; i < poly.pathCount; i++)
                        foreach (var pt in poly.GetPath(i))
                        {
                            Vector3 wp = t.TransformPoint(pt + poly.offset);
                            minX = Mathf.Min(minX, wp.x); maxX = Mathf.Max(maxX, wp.x);
                            minY = Mathf.Min(minY, wp.y); maxY = Mathf.Max(maxY, wp.y);
                        }
                    if (poly.pathCount == 0) { bounds = default; return false; }
                    bounds = new Bounds();
                    bounds.SetMinMax(new Vector3(minX, minY, 0f), new Vector3(maxX, maxY, 0f));
                    return true;
                }
            default:
                bounds = col.bounds;
                return true;
        }
    }
}
