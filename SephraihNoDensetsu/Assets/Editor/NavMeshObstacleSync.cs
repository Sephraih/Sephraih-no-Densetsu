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
    // Hoisted to class scope (was local to Sync()) so GenerateFloor() can also read it - see its own
    // use of this constant for why: the floor gap it opens at an interior BlocksSpell=true obstacle's
    // footprint must match the SAME padded size as that obstacle's own NavMeshModifier proxy, or a
    // thin ring where the (larger) padded proxy extends past the (smaller) unpadded floor gap still
    // has a floor proxy directly overlapping it - confirmed live this ring alone was enough to keep
    // reproducing the exact same "Teleport lands inside the tree" symptom even after the floor-gap
    // fix landed, just shrunk from the whole tree down to this ring. See Sync()'s own call site below
    // for the full reasoning on the value itself.
    const float SpellAreaPadding = 0.7f;
    // Floor-gap-only minimum padding for BlocksSpell=false interior obstacles (every current tree) -
    // see GenerateFloor()'s use of this for the full story. Zero padding there (matching the
    // obstacle's own genuinely-unpadded NavMeshModifier proxy 1:1) gave wall-quality landings for two
    // of three test trees but left the third - one sitting close enough to its neighbors' own floor
    // gaps to reintroduce the giant-triangle-swallow bug - fully stuck again (dist=0.000). A small
    // non-zero floor-gap margin (independent of SpellAreaPadding, which stays reserved for
    // BlocksSpell=true's real erosion-compensation need) closes that without reopening the original
    // "lands 1.5-2 units away" complaint - still well under a full SpellAreaPadding's worth.
    const float FloorGapMinPadding = 0.3f;
    // Only feeds discrete-Collider2D (Polygon/Circle) obstacles - decorative props like trees, never
    // tilemap-backed tiers (walls/boundary/etc, which iterate their own tilemap.cellSize directly) -
    // so shrinking this can never affect wall/Teleport behavior, only prop obstacles. 0.5 was too
    // coarse for a small object: a typical pine tree's real silhouette (~1.1-1.8 units across) only
    // spans 2-4 cells at that resolution, letting a concave notch or branch tip narrower than one
    // cell go completely undetected by every sample point in that cell (center AND all 4 corners -
    // see RasterizePolygon's own corner-sampling fix) - confirmed live: SpellAreaPadding=0.45 was
    // nowhere near enough margin to compensate for gaps this size, leaving Teleport landing 0.2-0.25
    // units deep inside the tree's real Collider2D at multiple scales. 0.2 (matching this project's
    // established NavMesh voxelSize) gives 5-9 cells across a typical tree instead, closing gaps
    // corner-sampling alone couldn't catch, at ~6x more proxy boxes per prop - a real but
    // editor-Sync-time-only cost, not a runtime one.
    const float ObstacleRasterCellSize = 0.2f;

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
        bool AnyPathContains(Vector2 p)
        {
            foreach (var wp in worldPaths)
                if (PointInPolygon(p, wp)) return true;
            return false;
        }

        var cellSize = new Vector3(cell, cell, 0f);
        float half = cell * 0.5f;
        for (int x = xMin; x < xMax; x++)
        {
            for (int y = yMin; y < yMax; y++)
            {
                Vector2 center = new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
                // Center-only sampling missed slivers near a polygon's edge whenever the true
                // boundary crosses a cell without its exact center falling inside - invisible for a
                // small/simple shape but grows with the obstacle's own scale (more perimeter, more
                // cells straddled this way), confirmed live: a 0.5-scale tree teleported onto cleanly,
                // an otherwise-identical 0.6/0.8-scale tree left a real gap between the padded proxy
                // and the true Collider2D edge - `SpellAreaPadding` (a fixed absolute margin) alone
                // can't compensate for an under-rasterized base footprint that itself grows with
                // scale. Sampling all 4 corners in addition to the center is a cheap, deliberately
                // conservative fix (editor-time only, never touches runtime) - guarantees this cell's
                // rasterized footprint is always a superset of center-only sampling, catching any
                // sliver the polygon boundary clips through without needing true polygon-cell
                // intersection math.
                bool inside = AnyPathContains(center) ||
                    AnyPathContains(new Vector2(center.x - half, center.y - half)) ||
                    AnyPathContains(new Vector2(center.x + half, center.y - half)) ||
                    AnyPathContains(new Vector2(center.x - half, center.y + half)) ||
                    AnyPathContains(new Vector2(center.x + half, center.y + half));
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

    // Generates one MapBoundary's floor as a set of BoxColliders covering exactly its true interior
    // extent - found via a flood fill from InteriorSeed through cells NOT covered by any tilemap-
    // backed boundary obstacle - rather than the old single box spanning ComputeWorldBounds()'s
    // bounding RECTANGLE. The rectangle approach silently included real, walkable, NavMesh-reachable
    // floor in every corner outside a non-rectangular boundary's actual outline (confirmed live:
    // Level3's diamond-shaped boundary left all four bounding-rect corners as genuine Teleport-
    // reachable NavMesh despite no boundary tile anywhere near them - units could reposition straight
    // past the visible wall into that phantom area).
    //
    // CORRECTED UNDERSTANDING (this comment previously claimed "Not Walkable" obstacles are immune to
    // this and can be safely left out of the wall-set - that was WRONG, found the hard way): the
    // "Not Walkable"-is-a-true-zero-triangle-hole-immune-to-floor-overlap claim, repeated all over
    // this project's history, was only ever verified against LARGE tilemap-backed obstacles (walls,
    // boundary rings spanning many cells). It does NOT hold for a SMALL, ISOLATED discrete-collider
    // obstacle (a single decorative tree) sitting inside one big merged floor row - confirmed live,
    // twice, via NavMesh.CalculateTriangulation() point-in-triangle lookup: first with trees at
    // BlocksSpell=true (a giant floor-row triangle absorbed their "Spell Boundary" proxy tag
    // entirely), then AGAIN after retagging trees to BlocksSpell=false/"Not Walkable" specifically to
    // get the true-hole treatment - same giant triangle, same complete absorption, this time of a
    // "Not Walkable"-tagged proxy that should have been unconditionally immune. Whatever makes
    // "Not Walkable" special-cased into a real hole for Recast evidently still needs the region to
    // clear some size/isolation threshold first; a lone small tree apparently doesn't. Bottom line:
    // EVERY interior obstacle with BlocksMovement=true - regardless of its BlocksSpell tier - needs
    // its own footprint excluded from the floor row-merge, not just Spell-tagged ones.
    // `interiorMovementObstacles` (built in Sync() from every Obstacle with BlocksMovement=true, not
    // filtered by BlocksSpell) closes this for real: each one's own tilemap/collider geometry joins
    // this method's wall-set alongside the boundary's own, so a floor row-run breaks (opens a small
    // gap) at its footprint too - exactly like a boundary obstacle already does - so a competing floor
    // proxy is never generated there for ANY tier to lose a tag-fight against in the first place.
    // Without this a "Not Walkable" hole could get silently swallowed by a floor row exactly the same
    // way a Spell Boundary tag did - Teleport would sample a "walkable" landing exactly on the
    // obstacle's real (still solid) Collider2D, move the player there, and MultiAreaMap.Unstuck()'s
    // per-frame physics-overlap check would immediately snap them straight back (invisible in
    // CityMap-based scenes, which have no such per-frame rescue - see feedback memory on this).
    // Passed in rather than re-queried here so Sync() (which already enumerates every Obstacle in the
    // scene once) stays the single source of truth for that list. Falls back to the old
    // single-rectangle behavior (with a warning) when InteriorSeed isn't assigned, so an existing
    // MapBoundary doesn't silently lose its floor before someone assigns a seed - correct only for a
    // genuinely rectangular boundary, same as this project's behavior always was.
    static int GenerateFloor(MapBoundary boundary, int bakeLayer, List<Obstacle> interiorMovementObstacles)
    {
        var outerBounds = boundary.ComputeWorldBounds();
        if (outerBounds.size.x <= 0f || outerBounds.size.y <= 0f)
        {
            Debug.LogWarning($"[NavMeshObstacleSync] MapBoundary on '{boundary.name}' has no BoundaryObstacles with real bounds - skipped.");
            return 0;
        }

        var floorHolder = new GameObject(FloorName);
        floorHolder.transform.SetParent(boundary.transform, false);

        if (boundary.InteriorSeed == null)
        {
            Debug.LogWarning($"[NavMeshObstacleSync] MapBoundary on '{boundary.name}' has no InteriorSeed assigned - using its full bounding rectangle as floor (may include phantom walkable area outside a non-rectangular boundary shape, e.g. the corners of a diamond/organic outline). Assign InteriorSeed to any GameObject known to sit inside the play area to fix.");
            var floorObj = new GameObject("floor");
            floorObj.transform.SetParent(floorHolder.transform, true);
            floorObj.transform.position = outerBounds.center;
            floorObj.layer = bakeLayer;
            var rectBox = floorObj.AddComponent<BoxCollider>();
            rectBox.size = new Vector3(outerBounds.size.x, outerBounds.size.y, ZThickness);
            return 1;
        }

        // Boundary obstacles can be tilemap-backed (Dungeon/MainCity) or discrete-Collider2D-backed
        // (Arena's BoundaryL/R/T/B, plain BoxCollider2Ds with no Tilemap) - same dual handling
        // ComputeWorldBounds() already does above, reusing the same BoxesForCollider() rasterization
        // the obstacle-proxy loop uses, so a non-rectangular discrete boundary would be handled
        // correctly too, not just tilemap ones.
        var boundaryTilemaps = new List<Tilemap>();
        var boundaryBoxes = new List<Bounds>();
        var boundaryObstacleSet = new HashSet<Obstacle>(boundary.BoundaryObstacles);
        foreach (var obs in boundary.BoundaryObstacles)
        {
            if (obs == null) continue;
            var tm = obs.GetComponentInChildren<Tilemap>(true);
            if (tm != null) { boundaryTilemaps.Add(tm); continue; }
            foreach (var col in obs.GetComponentsInChildren<Collider2D>(true))
            {
                if (col is TilemapCollider2D tilemapCol && tilemapCol.usedByComposite) continue;
                boundaryBoxes.AddRange(BoxesForCollider(col));
            }
        }
        // EVERY interior BlocksMovement=true obstacle (every decorative tree included - regardless of
        // its BlocksSpell tier, see this method's header comment for why that qualifier was dropped)
        // also joins the wall-set here, not just the boundary ring: a merged floor row-run can silently
        // paint straight over a small isolated obstacle's own proxy - "Not Walkable" tier included,
        // despite it supposedly being a true zero-triangle hole - since neither tier is safe against
        // this once the obstacle is small/isolated enough. Skips anything already in BoundaryObstacles
        // to avoid duplicate entries.
        //
        // Padding here mirrors the main obstacle-proxy loop's own `pad` logic below EXACTLY (padded
        // only when BlocksSpell=true) rather than being applied uniformly - that's deliberate, tuned
        // after two rounds of getting it wrong in both directions. Applying it universally (an earlier
        // version of this fix) matched the ring-overlap case for Spell-tagged obstacles but made
        // ordinary "Not Walkable" obstacles (every tree, now that they're BlocksSpell=false) land
        // noticeably short of a wall-quality landing - their own NavMeshModifier proxy carries zero
        // padding (real automatic erosion handles clearance instead, same as any wall), so padding
        // ONLY the floor-gap for them just pushed the floor uselessly far back with no matching
        // exclusion to justify it. Mirroring the obstacle's own padding here keeps floor-gap size equal
        // to actual proxy size for both tiers: BlocksSpell=true still gets the extra margin it
        // genuinely needs (no automatic erosion for a custom area, see the padding const's own doc
        // comment), BlocksSpell=false (every current tree) now lands as tight as a wall does, relying
        // on the same agentRadius erosion + tight rasterization (RasterizePolygon's corner-sampling,
        // ObstacleRasterCellSize 0.2) walls already rely on.
        foreach (var obs in interiorMovementObstacles)
        {
            if (obs == null || boundaryObstacleSet.Contains(obs)) continue;
            var tm = obs.GetComponentInChildren<Tilemap>(true);
            if (tm != null) { boundaryTilemaps.Add(tm); continue; }
            float pad = obs.BlocksSpell ? SpellAreaPadding * 2f : FloorGapMinPadding * 2f;
            foreach (var col in obs.GetComponentsInChildren<Collider2D>(true))
            {
                if (col is TilemapCollider2D tilemapCol && tilemapCol.usedByComposite) continue;
                foreach (var b in BoxesForCollider(col))
                {
                    var padded = b;
                    padded.size += new Vector3(pad, pad, 0f);
                    boundaryBoxes.Add(padded);
                }
            }
        }

        // Tilemap.WorldToCell() relies on the same live-transform-matrix caching as
        // GetCellCenterWorld()/CellToLocal() (see CellCenterWorld()'s own comment above) and is
        // JUST AS BROKEN on an inactive hierarchy - confirmed the hard way: this returned degenerate
        // cells for every query while Level3 was inactive (the normal state whenever Sync() runs on
        // a level that isn't the currently-active one), so IsWall() below silently never matched
        // anything and the flood fill filled the entire bounding rectangle anyway, identical to the
        // bug this method exists to fix. WorldToCellManual is CellCenterWorld's exact inverse - pure
        // matrix math via InverseTransformPoint, unaffected by active state.
        Vector3Int WorldToCellManual(Tilemap tm, Vector3 worldPos)
        {
            Vector3 local = tm.transform.InverseTransformPoint(worldPos) - Vector3.Scale(tm.cellSize, tm.tileAnchor);
            return new Vector3Int(Mathf.FloorToInt(local.x / tm.cellSize.x), Mathf.FloorToInt(local.y / tm.cellSize.y), 0);
        }

        bool IsWall(Vector2 worldPos)
        {
            foreach (var tm in boundaryTilemaps)
                if (tm.HasTile(WorldToCellManual(tm, worldPos))) return true;
            foreach (var b in boundaryBoxes)
                if (worldPos.x >= b.min.x && worldPos.x <= b.max.x && worldPos.y >= b.min.y && worldPos.y <= b.max.y) return true;
            return false;
        }

        const float cell = 1f; // this project's level tilemaps are uniformly 1-unit cells
        int xMin = Mathf.FloorToInt(outerBounds.min.x / cell);
        int xMax = Mathf.CeilToInt(outerBounds.max.x / cell);
        int yMin = Mathf.FloorToInt(outerBounds.min.y / cell);
        int yMax = Mathf.CeilToInt(outerBounds.max.y / cell);

        Vector2 CellWorldCenter(int gx, int gy) => new Vector2((gx + 0.5f) * cell, (gy + 0.5f) * cell);

        int seedX = Mathf.FloorToInt(boundary.InteriorSeed.position.x / cell);
        int seedY = Mathf.FloorToInt(boundary.InteriorSeed.position.y / cell);

        if (IsWall(CellWorldCenter(seedX, seedY)))
        {
            Debug.LogWarning($"[NavMeshObstacleSync] MapBoundary on '{boundary.name}': InteriorSeed sits exactly on a boundary tile - floor flood-fill found nothing. Move it further inside the play area.");
            return 0;
        }

        var visited = new HashSet<Vector2Int>();
        var stack = new Stack<Vector2Int>();
        var start = new Vector2Int(seedX, seedY);
        visited.Add(start);
        stack.Push(start);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            foreach (var d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
            {
                var n = c + d;
                if (n.x < xMin || n.x >= xMax || n.y < yMin || n.y >= yMax) continue;
                if (visited.Contains(n)) continue;
                if (IsWall(CellWorldCenter(n.x, n.y))) continue;
                visited.Add(n);
                stack.Push(n);
            }
        }

        // Merge each row's cells into contiguous horizontal runs - one box per run instead of one
        // per cell, keeping box count proportional to the boundary's silhouette complexity (a
        // handful of runs per row) rather than its full interior cell count (thousands for a large
        // level).
        int boxCount = 0;
        var rows = new Dictionary<int, List<int>>();
        foreach (var g in visited)
        {
            if (!rows.TryGetValue(g.y, out var xs)) rows[g.y] = xs = new List<int>();
            xs.Add(g.x);
        }
        foreach (var kv in rows)
        {
            var xs = kv.Value;
            xs.Sort();
            int runStart = xs[0];
            for (int i = 1; i <= xs.Count; i++)
            {
                bool endOfRun = i == xs.Count || xs[i] != xs[i - 1] + 1;
                if (!endOfRun) continue;

                int runEnd = xs[i - 1];
                float width = (runEnd - runStart + 1) * cell;
                float worldX = (runStart + runEnd + 1) / 2f * cell;
                float worldY = (kv.Key + 0.5f) * cell;

                var floorObj = new GameObject("floor");
                floorObj.transform.SetParent(floorHolder.transform, true);
                floorObj.transform.position = new Vector3(worldX, worldY, 0f);
                floorObj.layer = bakeLayer;
                var box = floorObj.AddComponent<BoxCollider>();
                box.size = new Vector3(width, cell, ZThickness);
                boxCount++;

                if (i < xs.Count) runStart = xs[i];
            }
        }

        if (boxCount == 0)
            Debug.LogWarning($"[NavMeshObstacleSync] MapBoundary on '{boundary.name}': floor flood-fill produced 0 boxes - check InteriorSeed and BoundaryObstacles.");

        return boxCount;
    }

    // Every obstacle tilemap tier's TilemapCollider2D/CompositeCollider2D pair is configured with
    // CompositeCollider2D.generationType = Manual (both on MapArea.prefab, for every future zone,
    // and retrofitted onto every existing scene's tiers) - Unity's DEFAULT (Synchronous) recomputes
    // the full composite physics shape on every single Tilemap.SetTile call, which is what made
    // painting many tiles in one stroke stall on "Application.updating scene info" (a well-known
    // Unity Tilemap+CompositeCollider2D characteristic, not specific to this project - see
    // https://issuetracker.unity3d.com/issues/stuck-on-applicaton-dot-updatescene-when-drawing-tilemap).
    // Manual generation means painting is instant, but the real Collider2D shape then only updates
    // when GenerateGeometry() is explicitly called - this method is that call, run once at the
    // start of every Sync() so anything that reads collider bounds downstream (this method itself,
    // any live Physics2D query) always sees the just-painted tiles, not a stale shape.
    static void RegenerateManualComposites()
    {
        int count = 0;
        foreach (var cc in Object.FindObjectsByType<CompositeCollider2D>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (cc.generationType != CompositeCollider2D.GenerationType.Manual) continue;

            // Toggling TilemapCollider2D.usedByComposite off/on forces the composite to fully
            // re-pull from the tilemap collider rather than trusting whatever it already cached -
            // covers the ordinary "tiles were painted/erased since the last Sync()" case a plain
            // GenerateGeometry() call handles anyway, plus this extra step for free.
            //
            // NOTE what this does NOT cover, confirmed directly: a per-tile Collider2D SHAPE that
            // was already baked in at paint time does not get re-derived just from this toggle, or
            // even from GenerateGeometry() alone, if the source Tile ASSET's own colliderType
            // changes afterward (e.g. the project-wide Sprite->Grid fix this method's comment used
            // to describe). That shape is only re-derived by a real SetTile pass over the affected
            // positions (remove then re-place - confirmed live: one project boundary tilemap stayed
            // stuck at 3301 composite paths through every combination of GenerateGeometry()/toggle/
            // RefreshAllTiles(), and only dropped to 4 once every position was actually re-set).
            // Deliberately NOT doing that heavier full-retile pass here on every Sync() - it's
            // proportional to total tile count and would reintroduce exactly the kind of paint/sync
            // lag this whole system exists to avoid, for a case (a Tile asset's own colliderType
            // changing) that's a rare, deliberate one-time edit, not routine level-building. If a
            // Tile asset's colliderType ever changes again, re-run the one-time full-retile fix by
            // hand (see project_navmesh_2d_gotchas memory) rather than assuming Sync() catches it.
            var tc = cc.GetComponent<TilemapCollider2D>();
            if (tc != null)
            {
                tc.usedByComposite = false;
                tc.usedByComposite = true;
            }

            cc.GenerateGeometry();
            count++;
        }
        if (count > 0) Debug.Log($"[NavMeshObstacleSync] Regenerated {count} manually-deferred CompositeCollider2D(s).");
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
        // Three-way area tagging, keyed off the BlocksMovement/BlocksSpell COMBINATION, not either
        // flag alone - a single NavMesh area can only ever encode one combination, so
        // "blocks movement AND spell" (boundary tier) and "blocks spell only" (spellBarrier tier)
        // need genuinely separate tags for Ability's two masks to treat them differently:
        //   - "Not Walkable": BlocksMovement=true, BlocksSpell=false (ordinary wall) - a true hole,
        //     Unity's own built-in area, no real geometry (confirmed via CalculateTriangulation()).
        //   - "Spell Boundary": BlocksMovement=true, BlocksSpell=true (e.g. the boundary tier) -
        //     real, walkable-shaped geometry just tagged differently (custom areas aren't special-
        //     cased into holes the way the built-in name is) - WalkableAreaMask excludes this too
        //     (so ordinary movement/pathing correctly treats it as solid, same as Not Walkable),
        //     while TeleportConnectivityMask excludes it specifically for Teleport's connectivity
        //     check to catch.
        //   - "Spell Barrier": BlocksMovement=false, BlocksSpell=true (e.g. spellBarrier tier) -
        //     same real geometry, but WalkableAreaMask does NOT exclude this one - ordinary
        //     movement/pathing (ChargeAttack included) sees straight through it, matching what its
        //     real (trigger) Collider2D already does - only TeleportConnectivityMask excludes it.
        int spellBoundary = NavMesh.GetAreaFromName("Spell Boundary");
        int spellBarrier = NavMesh.GetAreaFromName("Spell Barrier");

        // "Spell Boundary"/"Spell Barrier" proxies get ZERO automatic erosion clearance from
        // adjacent walkable floor - confirmed live via NavMesh.SamplePosition probes (landed exactly
        // on the tile edge, distance 0.000). Unity's agentRadius erosion only pushes the walkable
        // region away from genuinely non-walkable-classified geometry; "Not Walkable" gets that
        // treatment (built-in name, confirmed zero real triangles - see this file's earlier comment
        // and project-navmesh-2d-gotchas bug #5's correction), but a CUSTOM area like these two is
        // still walkable-classified terrain as far as the erosion pass is concerned, just relabeled
        // afterward - so there's no hole for erosion to measure distance from. Without a fix here,
        // Teleport (whose only remaining gate near these obstacles is TryFindWalkableLanding's
        // SamplePosition) can land the character flush against - visually clipped into - a boundary/
        // spellBarrier tile, something an ordinary "Not Walkable" wall never allows. Manually padding
        // the proxy box by agentRadius on every side recreates the same clearance an automatic hole
        // would have gotten - it doesn't matter that this makes the "Spell Boundary"/"Spell Barrier"
        // area itself bigger than the tile, since nothing is ever supposed to path/land inside a wall
        // anyway. Minor accepted tradeoff: at a tier transition (a spell-tagged tile sitting right
        // next to a differently-tagged neighbor), this padding can bleed a sliver of that neighbor's
        // own footprint into the spell tag - harmless since that sliver still sits inside solid wall
        // geometry either way.
        //
        // Deliberately its OWN tunable constant, not tied to agentRadius (0.6, tuned separately for
        // enemy pathing/corner-wedging - see project_navmesh_2d_gotchas memory) - using the full
        // agentRadius here was needlessly generous and produced a noticeable "nudge back" when
        // teleporting from right next to a wall (SamplePosition snapping the landing out past the
        // padded margin). Originally set to 0.45 (just above the player's real worst-case diagonal
        // half-extent, ~0.42, from its 0.67x0.50 BoxCollider2D) when this was tuned only against flat
        // tilemap-tier walls. Bumped to 0.7 after decorative tree obstacles (small, irregular
        // PolygonCollider2D props, BlocksSpell=true by Obstacle's own class defaults) exposed a real
        // gap 0.45 didn't cover: confirmed live via NavMesh.SamplePosition + Physics2D.OverlapPoint
        // that Teleport landings still clipped 0.18-0.25 units inside a tree's real collider at 0.45,
        // for every scale tested (0.5x/0.6x/0.8x) - REGARDLESS of independently improving the base
        // rasterization (RasterizePolygon's corner-sampling, ObstacleRasterCellSize 0.5->0.2) or
        // matching this same padding into GenerateFloor()'s floor-gap sizing. Only raising this value
        // itself actually closed it (verified: 1.0 cleared all three with margin to spare; 0.7 still
        // clears all three, chosen as the smaller of the two that measurably worked, to keep the wall
        // "nudge back" this constant was originally tuned to avoid as small as this fix allows). If
        // wall-teleport ever starts feeling pushed-back again, this is why - the fix would be to split
        // this into two differently-tuned constants (a small one for flat tilemap walls, a larger one
        // for small/irregular discrete-collider props) rather than reverting the tree fix. (Declared
        // at class scope now - see there.)

        // Every obstacle tilemap tier's CompositeCollider2D is set to Manual generation (see
        // MapArea.prefab / RegenerateManualComposites' own doc comment) specifically so painting
        // tiles doesn't trigger a synchronous physics-shape recompute on every single brush stroke
        // (the "Application.updating scene info" lag). That means its real Collider2D geometry can
        // be stale relative to whatever was just painted - regenerate every one now, before this
        // method reads any collider bounds, so the sync (and the bake that follows it) always sees
        // the current tiles, not last session's.
        RegenerateManualComposites();

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
            // Previously gated on BlocksMovement alone, which silently skipped every
            // BlocksMovement=false obstacle - including a spellBarrier (BlocksMovement=false,
            // BlocksSpell=true), meaning it would never get ANY NavMesh proxy/area tag and
            // TryFindWalkableLanding's Spell Boundary check would have nothing to detect. An
            // obstacle needs a proxy if it blocks movement OR spell-crossing - either alone
            // justifies generating one.
            if ((!obstacle.BlocksMovement && !obstacle.BlocksSpell) || !obstacle.NavigationStatic) continue;

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
                // BlocksSpell obstacles get padded by SpellAreaPadding on every side (see the comment
                // above) - ordinary BlocksMovement-only obstacles don't need this, since
                // "Not Walkable" already gets real erosion for free.
                float pad = obstacle.BlocksSpell ? SpellAreaPadding * 2f : 0f;
                box.size = new Vector3(Mathf.Max(b.size.x, 0.05f) + pad, Mathf.Max(b.size.y, 0.05f) + pad, ZThickness);
                var mod = proxy.AddComponent<NavMeshModifier>();
                mod.overrideArea = true;
                mod.area = obstacle.BlocksSpell
                    ? (obstacle.BlocksMovement ? spellBoundary : spellBarrier)
                    : notWalkable;
                totalBoxes++;
            }
            processed++;
        }

        // Every interior BlocksMovement=true obstacle needs to break the floor flood-fill/row-merge at
        // its own footprint too, not just the map's outer boundary ring - see GenerateFloor()'s header
        // comment for the full reasoning (a hard-won correction: this used to be scoped to
        // BlocksSpell=true only, on the assumption "Not Walkable" didn't need it - confirmed live that
        // assumption was wrong for small isolated obstacles). Scoped here to DISCRETE-collider
        // obstacles only (no Tilemap component) - tilemap-tier obstacles (walls/boundary/etc.) are
        // large, contiguous, multi-cell shapes that have never actually exhibited this bug in practice
        // (unlike a single small tree), so leaving them out keeps this list - and every per-cell
        // IsWall() check in GenerateFloor()'s flood fill - proportional to prop count, not total tile
        // count. Built once here, from the same `obstacles` pass above, rather than re-querying inside
        // GenerateFloor() per boundary.
        var interiorMovementObstacles = new List<Obstacle>();
        foreach (var obstacle in obstacles)
        {
            if (obstacle == null || !obstacle.BlocksMovement) continue;
            if (obstacle.GetComponentInChildren<Tilemap>(true) != null) continue; // tilemap tiers excluded, see above
            interiorMovementObstacles.Add(obstacle);
        }

        int boundaryFloors = 0;
        int totalFloorBoxes = 0;
        var mapBoundaries = Object.FindObjectsByType<MapBoundary>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var boundary in mapBoundaries)
        {
            int boxes = GenerateFloor(boundary, bakeLayer, interiorMovementObstacles);
            if (boxes > 0) { boundaryFloors++; totalFloorBoxes += boxes; }
        }

        if (boundaryFloors == 0)
            Debug.LogWarning("[NavMeshObstacleSync] No MapBoundary found in the scene - no floor generated at all. Add a MapBoundary to declare this map/level's extent.");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[NavMeshObstacleSync] Processed " + processed + " obstacle(s), created " + totalBoxes +
                   " obstacle proxy box(es). Boundary floors: " + boundaryFloors + " (" + totalFloorBoxes + " floor box(es)).");

        // Bakes every NavMeshSurface in the scene, not just the primary (Humanoid) one - as of the
        // dual-agent-type Teleport-landing change, a scene carries a second surface (e.g.
        // "NavMeshGroundTeleport") baked at a much smaller agentRadius purely for
        // TryFindWalkableLanding/TryFindReachableLanding queries, sharing this same proxy geometry.
        // Previously baking was always a separate manual step (the Inspector's "Bake" button, or
        // MultiAreaMap.RebuildNavMesh() at runtime - see project_navmesh_2d_gotchas bug #16); doing
        // it here removes that fragility for BOTH surfaces at once instead of adding a second manual
        // step on top of the first.
        var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var surface in surfaces)
        {
            if (surface == null) continue;
            surface.BuildNavMesh();
            EditorUtility.SetDirty(surface);
        }
        NavMesh2DUtility.InvalidateCache();
        Debug.Log("[NavMeshObstacleSync] Baked " + surfaces.Length + " NavMeshSurface(s).");
    }
}
