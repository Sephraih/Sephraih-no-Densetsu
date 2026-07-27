using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;

// Editor-only utility: generates 3D "shadow" BoxCollider proxies for every Obstacle with
// BlocksMovement+NavigationStatic in the open scene, so NavMeshSurface (which only understands
// 3D geometry) can carve holes matching this project's Collider2D-based obstacles, plus flat
// walkable floor boxes spanning each map/level's actual playable interior (obstacles alone don't
// imply a floor - NavMesh needs actual walkable geometry to bake onto; the obstacle proxies below
// explicitly override their area to "Not Walkable" via NavMeshModifier rather than relying on
// height/orientation tricks).
//
// Tilemap-backed obstacles (boundary/obstacle tilemaps) generate one box per POPULATED CELL,
// not per CompositeCollider2D path - a hollow rectangular wall ring decomposes into an outer +
// inner path whose bounding boxes each span almost the whole room, which would wrongly carve out
// nearly the entire interior as "Not Walkable". Per-cell boxes exactly match the painted tiles
// regardless of how concave/ring-shaped the overall composite path is.
//
// Floor generation is keyed off each MapBoundary component in the scene (explicit authoring -
// a MapBoundary lists exactly which Obstacles form a map/level's edge, tilemap-backed or discrete
// Collider2D or a mix), filling that boundary's ComputeWorldBounds() rectangle as one solid
// walkable floor, parented under the MapBoundary's own GameObject so it activates/deactivates with
// its level in multi-level scenes. This deliberately treats "no tile painted" as walkable - this
// game's actual physics only cares about real Collider2D geometry, never about whether a
// ground-art tile happens to be painted there, and this project's levels routinely paint boundary
// walls well beyond where ground art was ever finished. A scene with no MapBoundary gets no floor
// at all (and a warning) rather than a guessed fallback - every map must explicitly declare its
// own boundary.
//
// Proxies are kept in plain, identity-rotated world space (position = footprint center, box
// size = (footprintWidthX, footprintHeightY, generousZThickness)). This deliberately avoids
// needing to know the exact local-axis mapping the rotated NavMeshSurface bake volume uses -
// the proxy's world-space XY footprint is what matters for carving the hole.
public static class NavMeshObstacleSync
{
    const string HolderName = "[NavMeshProxies]";
    const string FloorName = "[NavMeshFloor]";
    const float ZThickness = 2f;
    const float ObstacleRasterCellSize = 0.5f;

    // Tilemap.GetCellCenterWorld()/CellToLocal() both silently return degenerate values (as if
    // cellPos were always zero) when the Tilemap's GameObject hierarchy is INACTIVE - the normal
    // state for every Dungeon level except whichever one happens to be active when this tool is
    // run, since it processes every level in one pass via FindObjectsInactive.Include. Confirmed
    // this collapses every obstacle/boundary proxy for inactive levels onto the scene origin.
    // cellSize/tileAnchor are plain serialized fields (unaffected by active state), and
    // Transform.TransformPoint is pure matrix math - computing the cell center manually from
    // those instead works correctly regardless of active state (verified against
    // GetCellCenterWorld's own correct output while active, for the identical cell).
    internal static Vector3 CellCenterWorld(Tilemap tilemap, Vector3Int cellPos)
    {
        Vector3 cellLocal = new Vector3(cellPos.x * tilemap.cellSize.x, cellPos.y * tilemap.cellSize.y, 0f);
        Vector3 centerLocal = cellLocal + Vector3.Scale(tilemap.cellSize, tilemap.tileAnchor);
        return tilemap.transform.TransformPoint(centerLocal);
    }

    // Collider2D.bounds/OverlapPoint() are backed by the native 2D physics engine's cached shape
    // data, which - just like Tilemap.GetCellCenterWorld() (see CellCenterWorld() above) - only
    // gets populated once OnEnable has actually run. On an INACTIVE GameObject hierarchy (the
    // normal state for every Dungeon level except whichever happens to be active when this tool
    // runs, since it processes every level's obstacles in one FindObjectsInactive.Include pass)
    // .bounds silently collapses to a zero-size point sitting at transform.position - confirmed
    // directly: an inactive tree's .bounds read back as (-17.40,-11.75)..(-17.40,-11.75) (its own
    // transform.position, zero size) while its serialized local `.points` were still completely
    // valid, and manually transforming one of those points gave the correct real-world vertex.
    // This meant every non-tilemap (individual-Collider2D) obstacle on any level other than the
    // one active at Sync()-time got an almost-invisible ~0.05-unit proxy instead of its real
    // footprint - the navmesh never learned those obstacles existed at all, so agents pathed
    // straight through them and only got stopped by raw Rigidbody2D collision with the real
    // (correctly-sized, always-live) 2D collider - looking exactly like "avoids slightly via
    // physics bump, then gets stuck" with zero actual pathfinding awareness of the obstacle.
    // Fixed the same way as CellCenterWorld(): read only plain serialized fields (points/offset/
    // radius/size, all unaffected by active state) and do the geometry ourselves via
    // Transform.TransformPoint (pure matrix math), never touching the native-engine-backed
    // .bounds/.OverlapPoint APIs. Rasterizing polygons/circles on a fine grid (rather than using
    // one big box per shape) is a secondary but real improvement even for the active-level case:
    // a roughly-circular tree canopy only fills ~65% of its own bounding rectangle, so several
    // such trees placed close together (a common decorative cluster) previously had their
    // inflated rectangular proxies overlap into one bigger, differently-shaped blocked area than
    // what the real Collider2D geometry blocks.
    static List<Bounds> BoxesForCollider(Collider2D col)
    {
        var t = col.transform;
        switch (col)
        {
            case BoxCollider2D box:
                {
                    Vector2 size = Vector2.Scale(box.size, t.lossyScale);
                    Vector3 center = t.TransformPoint(box.offset);
                    return new List<Bounds> { new Bounds(center, new Vector3(size.x, size.y, 0f)) };
                }
            case CircleCollider2D circle:
                {
                    float scale = Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y));
                    Vector3 center = t.TransformPoint(circle.offset);
                    return RasterizeCircle(center, circle.radius * scale);
                }
            case PolygonCollider2D poly:
                return RasterizePolygon(poly);
            default:
                // Rare in this project (CapsuleCollider2D etc.) - only reliable while active.
                return new List<Bounds> { col.bounds };
        }
    }

    static List<Bounds> RasterizeCircle(Vector3 center, float radius)
    {
        var result = new List<Bounds>();
        float cell = ObstacleRasterCellSize;
        int xMin = Mathf.FloorToInt((center.x - radius) / cell);
        int xMax = Mathf.CeilToInt((center.x + radius) / cell);
        int yMin = Mathf.FloorToInt((center.y - radius) / cell);
        int yMax = Mathf.CeilToInt((center.y + radius) / cell);
        var cellSize = new Vector3(cell, cell, 0f);
        float r2 = radius * radius;
        for (int x = xMin; x < xMax; x++)
            for (int y = yMin; y < yMax; y++)
            {
                Vector2 p = new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
                if (((Vector2)center - p).sqrMagnitude <= r2)
                    result.Add(new Bounds(p, cellSize));
            }
        if (result.Count == 0)
            result.Add(new Bounds(center, new Vector3(radius * 2f, radius * 2f, 0f)));
        return result;
    }

    static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        int n = poly.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((poly[i].y > p.y) != (poly[j].y > p.y)) &&
                (p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                inside = !inside;
        }
        return inside;
    }

    static List<Bounds> RasterizePolygon(PolygonCollider2D poly)
    {
        var t = poly.transform;
        var worldPaths = new List<Vector2[]>();
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < poly.pathCount; i++)
        {
            var path = poly.GetPath(i);
            var worldPath = new Vector2[path.Length];
            for (int p = 0; p < path.Length; p++)
            {
                Vector3 wp = t.TransformPoint(path[p] + poly.offset);
                worldPath[p] = wp;
                minX = Mathf.Min(minX, wp.x); maxX = Mathf.Max(maxX, wp.x);
                minY = Mathf.Min(minY, wp.y); maxY = Mathf.Max(maxY, wp.y);
            }
            worldPaths.Add(worldPath);
        }

        var result = new List<Bounds>();
        if (worldPaths.Count == 0) return result;

        float cell = ObstacleRasterCellSize;
        int xMin = Mathf.FloorToInt(minX / cell), xMax = Mathf.CeilToInt(maxX / cell);
        int yMin = Mathf.FloorToInt(minY / cell), yMax = Mathf.CeilToInt(maxY / cell);
        var cellSize = new Vector3(cell, cell, 0f);
        for (int x = xMin; x < xMax; x++)
        {
            for (int y = yMin; y < yMax; y++)
            {
                Vector2 center = new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
                bool inside = false;
                foreach (var wp in worldPaths)
                    if (PointInPolygon(center, wp)) { inside = true; break; }
                if (inside) result.Add(new Bounds(center, cellSize));
            }
        }
        if (result.Count == 0)
        {
            var b = new Bounds(new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0f),
                                new Vector3(maxX - minX, maxY - minY, 0f));
            result.Add(b);
        }
        return result;
    }

    [MenuItem("Tools/Sephraih/Sync NavMesh Obstacle Proxies")]
    public static void Sync()
    {
        int bakeLayer = LayerMask.NameToLayer("NavMeshBake");
        if (bakeLayer < 0)
        {
            Debug.LogError("[NavMeshObstacleSync] 'NavMeshBake' layer not found - aborting.");
            return;
        }
        int notWalkable = NavMesh.GetAreaFromName("Not Walkable");

        // Wipe every previous run's output before regenerating (there can be more than one floor
        // now - one per MapBoundary - so this can't rely on GameObject.Find finding just one).
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && (t.name == HolderName || t.name == FloorName))
                Object.DestroyImmediate(t.gameObject);

        int processed = 0;
        int totalBoxes = 0;

        var obstacles = Object.FindObjectsByType<Obstacle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var obstacle in obstacles)
        {
            if (!obstacle.BlocksMovement || !obstacle.NavigationStatic) continue;

            var boxes = new List<Bounds>();
            var tilemap = obstacle.GetComponentInChildren<Tilemap>(true);

            if (tilemap != null)
            {
                tilemap.CompressBounds();
                var cb = tilemap.cellBounds;
                var cellSize = Vector3.Scale(tilemap.cellSize, tilemap.transform.lossyScale);
                for (int x = cb.xMin; x < cb.xMax; x++)
                {
                    for (int y = cb.yMin; y < cb.yMax; y++)
                    {
                        var cellPos = new Vector3Int(x, y, cb.zMin);
                        if (!tilemap.HasTile(cellPos)) continue;
                        var worldCenter = CellCenterWorld(tilemap, cellPos);
                        boxes.Add(new Bounds(worldCenter, cellSize));
                    }
                }
            }
            else
            {
                foreach (var col in obstacle.GetComponentsInChildren<Collider2D>(true))
                {
                    if (col is TilemapCollider2D tilemapCol && tilemapCol.usedByComposite) continue;
                    boxes.AddRange(BoxesForCollider(col));
                }
            }
            if (boxes.Count == 0) continue;

            var holder = new GameObject(HolderName);
            holder.transform.SetParent(obstacle.transform, false);

            foreach (var b in boxes)
            {
                var proxy = new GameObject("proxy");
                proxy.transform.SetParent(holder.transform, false);
                proxy.transform.position = b.center;
                proxy.layer = bakeLayer;
                var box = proxy.AddComponent<BoxCollider>();
                box.size = new Vector3(Mathf.Max(b.size.x, 0.05f), Mathf.Max(b.size.y, 0.05f), ZThickness);
                var mod = proxy.AddComponent<NavMeshModifier>();
                mod.overrideArea = true;
                mod.area = notWalkable;
                totalBoxes++;
            }
            processed++;
        }

        int boundaryFloors = 0;
        var mapBoundaries = Object.FindObjectsByType<MapBoundary>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var boundary in mapBoundaries)
        {
            var floorBounds = boundary.ComputeWorldBounds();
            if (floorBounds.size.x <= 0f || floorBounds.size.y <= 0f)
            {
                Debug.LogWarning($"[NavMeshObstacleSync] MapBoundary on '{boundary.name}' has no BoundaryObstacles with real bounds - skipped.");
                continue;
            }

            var floorHolder = new GameObject(FloorName);
            floorHolder.transform.SetParent(boundary.transform, false);
            floorHolder.transform.position = new Vector3(floorBounds.center.x, floorBounds.center.y, 0f);
            floorHolder.layer = bakeLayer;
            var floorBox = floorHolder.AddComponent<BoxCollider>();
            floorBox.size = new Vector3(floorBounds.size.x, floorBounds.size.y, ZThickness);
            boundaryFloors++;
        }

        if (boundaryFloors == 0)
            Debug.LogWarning("[NavMeshObstacleSync] No MapBoundary found in the scene - no floor generated at all. Add a MapBoundary to declare this map/level's extent.");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[NavMeshObstacleSync] Processed " + processed + " obstacle(s), created " + totalBoxes +
                   " obstacle proxy box(es). Boundary floors: " + boundaryFloors + ".");
    }
}
