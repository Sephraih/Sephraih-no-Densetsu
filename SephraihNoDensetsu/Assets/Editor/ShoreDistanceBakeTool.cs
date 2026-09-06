using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// Editor-only utility: bakes a shore-distance lookup texture for a water (or any) DualGridTilemapModule
// material's dataTilemap, for the Sephraih/ShoreGradient shader to sample. Mirrors TerrainBakeTool.cs's
// own "select a GameObject, run the menu command, write + reimport a PNG" pattern.
//
// Distance metric: exact 2D squared Euclidean Distance Transform (Felzenszwalb & Huttenlocher,
// "Distance Transforms of Sampled Functions", 2012) - two exact 1D lower-envelope passes (columns then
// rows), O(width*height) total. An earlier version used 8-connected multi-source Dijkstra with real
// Euclidean step costs (a chamfer-style approximation) - it's only exact along 0/45/90-degree
// directions, and every other angle accumulates a small directional bias that turned out NOT to stay
// invisible once mapped through a color gradient: on a large, roughly symmetric lake it produced a
// visible cross/starburst artifact through the center (confirmed live). The exact 2-pass transform has
// zero directional bias by construction, so this artifact can't recur regardless of a water body's
// size or shape.
//
// Boundary sampling: each fine sub-sample bilinearly interpolates between the 4 nearest DATA CELL fill
// values (treating a data cell's fill state as a point sample at its own integer grid coordinate, the
// same convention DualGridTilemapModule.ComputeOwnCorners already uses for its corner-fill lookup),
// then thresholds at 0.5 - NOT "which whole data cell does this sub-sample fall in." A per-whole-cell
// sample produces an axis-aligned blocky boundary that visibly disagrees with the actual rendered
// dual-grid art (whose 16-sprite combos already draw rounded/diagonal corners from that same corner
// interpolation) - confirmed live as a "double outline" right at the shore, where the baked field's
// blocky zero-contour sat slightly offset from the smooth rendered edge. Interpolating at bake time
// keeps the two boundaries aligned.
public static class ShoreDistanceBakeTool
{
    const string OutputFolder = "Assets/Generated/ShoreDistance";

    [MenuItem("Tools/Sephraih/Water/Bake Shore Distance Field")]
    public static void BakeSelected()
    {
        var go = Selection.activeGameObject;
        var module = go != null ? go.GetComponent<DualGridTilemapModule>() : null;
        if (module == null)
        {
            Debug.LogError("[ShoreDistanceBakeTool] Select a GameObject with a DualGridTilemapModule first.");
            return;
        }
        Bake(module);
    }

    // maxShoreDistance (world units): the real-world distance from shore at which a water body reaches
    // full "deep" color. Positive (default 6) = one fixed, GLOBAL scale, so "how fast it gets dark" has
    // the same meaning everywhere - a small pond's center (say 0.5 units from its own shore) stays
    // mostly light/shallow-looking, while a real lake's center (well past 6 units in) reaches full
    // dark, exactly reflecting their actual relative depth. This is the default and normal case.
    //
    // Pass 0 or negative instead to auto-normalize each disconnected water body (4-connected flood
    // fill) against its OWN deepest point - every body, however small, then reaches full dark at its
    // own center. Tried as the default first; reverted after it made small ponds look artificially,
    // uniformly deep ("small bodies get dark to extremely," confirmed live) - the per-point distance-
    // to-nearest-shore computation itself was never the problem (that's already correctly per-body,
    // regardless of which normalization mode is used below), only the color-mapping SCALE was.
    //
    // supersample subdivides each data cell into supersample x supersample sub-texels (corner-
    // interpolated, see class comment) before running the distance transform, and blurRadius (in those
    // sub-texel units, default = supersample, i.e. ~1 original data cell of smoothing) applies a light
    // separable box blur afterward as extra polish on top of the now-exact, now-corner-aligned field.
    //
    // deepSmoothRadius/deepSmoothBlend address a DIFFERENT problem than blurRadius: "distance to the
    // single nearest shore point" is an exact min-of-many-candidates function, and a min function has
    // real creases (C1 discontinuities) wherever the nearest shore point switches from one stretch of
    // shoreline to another - the medial axis of the shape. This is not a computation bug (confirmed:
    // still visible after moving to an exact, zero-bias EDT) - it's a genuine geometric feature of
    // "raw nearest-shore distance" for any non-circular body, and it reads as a starburst/seam pattern
    // once mapped through a color gradient, worse for bigger/more irregular lakes. The fix (matching
    // the user's own correct hunch that counting more than just the single closest point would help):
    // blend a HEAVILY blurred copy of the field back in as distance grows, so the near-shore band stays
    // exactly as crisp/art-aligned as before (blendT~0 there) while the deep interior - where the crease
    // structure actually lives - gets smoothed into soft, rounded contours instead. deepSmoothRadius
    // defaults to 4x blurRadius (a much wider kernel), deepSmoothBlend defaults to half of
    // maxShoreDistance (world units) as the distance over which raw fades into smoothed.
    public static void Bake(DualGridTilemapModule module, float maxShoreDistance = 6f, int supersample = 4,
        int blurRadius = -1, int deepSmoothRadius = -1, float deepSmoothBlend = -1f)
    {
        if (blurRadius < 0) blurRadius = supersample;
        if (deepSmoothRadius < 0) deepSmoothRadius = blurRadius * 4;
        if (deepSmoothBlend <= 0f) deepSmoothBlend = Mathf.Max(maxShoreDistance, 1f) * 0.5f;

        var so = new SerializedObject(module);
        var dataTilemap = so.FindProperty("dataTilemap").objectReferenceValue as Tilemap;
        if (dataTilemap == null)
        {
            Debug.LogError($"[ShoreDistanceBakeTool] '{module.name}' has no dataTilemap assigned.");
            return;
        }

        dataTilemap.CompressBounds();
        var cb = dataTilemap.cellBounds;
        if (cb.size.x <= 0 || cb.size.y <= 0)
        {
            Debug.LogError($"[ShoreDistanceBakeTool] '{module.name}'s dataTilemap has no painted cells.");
            return;
        }

        // Pad by 1 cell on every side so cells right at the tight compressed-bounds edge (guaranteed
        // filled, since CompressBounds trims to the tightest box containing filled cells) still have
        // real adjacent land sampled within array bounds, rather than an edge-of-array special case.
        const int pad = 1;
        int originCellX = cb.xMin - pad;
        int originCellY = cb.yMin - pad;
        int cellWidth = cb.size.x + pad * 2;
        int cellHeight = cb.size.y + pad * 2;

        int width = cellWidth * supersample;
        int height = cellHeight * supersample;

        // Cache each data cell's own fill state as a "point sample at its own integer grid coordinate"
        // (one extra ring beyond the padded bounds, so corner interpolation at the outermost fine
        // samples always has real neighbors to read, never an implicit "GetTile out of range = false"
        // edge case).
        var cellFill = new bool[cellWidth + 1, cellHeight + 1];
        for (int cx = 0; cx <= cellWidth; cx++)
            for (int cy = 0; cy <= cellHeight; cy++)
                cellFill[cx, cy] = dataTilemap.GetTile(new Vector3Int(originCellX + cx, originCellY + cy, 0)) != null;

        bool[,] filled = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            float gx = x / (float)supersample;
            int x0 = Mathf.Min((int)gx, cellWidth - 1);
            float tx = gx - x0;
            for (int y = 0; y < height; y++)
            {
                float gy = y / (float)supersample;
                int y0 = Mathf.Min((int)gy, cellHeight - 1);
                float ty = gy - y0;

                float v00 = cellFill[x0, y0] ? 1f : 0f;
                float v10 = cellFill[x0 + 1, y0] ? 1f : 0f;
                float v01 = cellFill[x0, y0 + 1] ? 1f : 0f;
                float v11 = cellFill[x0 + 1, y0 + 1] ? 1f : 0f;
                float bottom = Mathf.Lerp(v00, v10, tx);
                float top = Mathf.Lerp(v01, v11, tx);
                filled[x, y] = Mathf.Lerp(bottom, top, ty) > 0.5f;
            }
        }

        Vector3 cellSize = dataTilemap.layoutGrid != null ? dataTilemap.layoutGrid.cellSize : Vector3.one;
        float subCellSizeX = cellSize.x / supersample;
        float subCellSizeY = cellSize.y / supersample;

        float[,] dist = ComputeShoreDistanceExact(filled, width, height, subCellSizeX, subCellSizeY);
        dist = BoxBlur(dist, width, height, blurRadius);

        // Deep smoothing: round off the medial-axis creases (see doc comment above) by fading toward a
        // much more heavily blurred copy of the field as distance-from-shore grows, while leaving the
        // already shore-aligned near-boundary result untouched.
        var deepSmoothed = BoxBlur(dist, width, height, deepSmoothRadius);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float blendT = Mathf.Clamp01(dist[x, y] / deepSmoothBlend);
                dist[x, y] = Mathf.Lerp(dist[x, y], deepSmoothed[x, y], blendT);
            }
        }

        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, true); // linear - this is data, not color

        if (maxShoreDistance > 0f)
        {
            // Explicit override: one fixed scale across the whole bake, for when multiple water
            // bodies (even across different materials/bakes) need to agree on what a given absolute
            // depth looks like, at the cost of a small/shallow body not filling its own full range.
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    tex.SetPixel(x, y, GrayscaleFromDistance(dist[x, y], maxShoreDistance));
        }
        else
        {
            // Auto mode: each disconnected water body (4-connected flood fill over `filled`) gets its
            // OWN normalization scale, against its own deepest point - a small isolated puddle and a
            // big lake painted with the same material each use their full light->dark range instead of
            // a shared body elsewhere (often much deeper) flattening the smaller one toward all-light.
            var (componentId, componentMax) = FindComponentsAndMaxDistance(filled, dist, width, height);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    int id = componentId[x, y];
                    float scale = id >= 0 ? Mathf.Max(componentMax[id], 0.0001f) : 1f; // land: dist is always 0, scale is irrelevant
                    tex.SetPixel(x, y, GrayscaleFromDistance(dist[x, y], scale));
                }
            }
        }
        tex.Apply();

        if (!AssetDatabase.IsValidFolder(OutputFolder))
            CreateFolderRecursive(OutputFolder);

        string path = $"{OutputFolder}/{module.name}_ShoreDistance.png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        ConfigureImporter(path);

        var bakedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        // Local space, relative to dataTilemap's own transform (which this module's GameObject shares
        // - confirmed the module lives directly on the *Data GameObject alongside its own Tilemap) -
        // NOT world space, so the bake stays correct if this MapArea instance is ever moved.
        Vector2 localOrigin = dataTilemap.CellToLocal(new Vector3Int(originCellX, originCellY, 0));
        Vector2 localSize = new Vector2(cellWidth * cellSize.x, cellHeight * cellSize.y);

        so.FindProperty("shoreDistanceTexture").objectReferenceValue = bakedTex;
        so.FindProperty("shoreBakedLocalOrigin").vector2Value = localOrigin;
        so.FindProperty("shoreBakedLocalSize").vector2Value = localSize;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(module);

        // Refresh immediately rather than waiting on OnValidate, which isn't guaranteed to fire off the
        // SerializedObject write above the way an Inspector-driven edit would.
        module.ApplyShoreGradient();

        string normalizeDesc = maxShoreDistance > 0f
            ? $"fixed {maxShoreDistance:F2} world units"
            : "auto, per isolated water body";
        Debug.Log($"[ShoreDistanceBakeTool] Baked '{module.name}' -> {path} ({width}x{height}px, " +
            $"normalization={normalizeDesc}, supersample={supersample}, blurRadius={blurRadius}, " +
            $"deepSmoothRadius={deepSmoothRadius}, deepSmoothBlend={deepSmoothBlend:F2}).");
    }

    static Color GrayscaleFromDistance(float distance, float scale)
    {
        float t = Mathf.Clamp01(distance / scale);
        return new Color(t, t, t, 1f);
    }

    // 4-connected flood fill over the water mask - each disconnected island gets its own id and its
    // own max blurred-distance value, so BakeSelected's auto-normalize can scale each body against its
    // own deepest point instead of one shared value across every body a material happens to paint.
    // 4-connected (not 8) deliberately: two water cells that only touch diagonally read as visually
    // separate bodies on a tile grid, not one continuous lake.
    static (int[,] componentId, List<float> componentMax) FindComponentsAndMaxDistance(bool[,] filled, float[,] dist, int width, int height)
    {
        var componentId = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                componentId[x, y] = -1;

        var componentMax = new List<float>();
        var stack = new Stack<(int x, int y)>();
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!filled[x, y] || componentId[x, y] != -1) continue;

                int id = componentMax.Count;
                float maxDist = 0f;
                componentId[x, y] = id;
                stack.Push((x, y));

                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    if (!float.IsInfinity(dist[cx, cy])) maxDist = Mathf.Max(maxDist, dist[cx, cy]);

                    for (int i = 0; i < 4; i++)
                    {
                        int nx = cx + dx[i], ny = cy + dy[i];
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        if (!filled[nx, ny] || componentId[nx, ny] != -1) continue;
                        componentId[nx, ny] = id;
                        stack.Push((nx, ny));
                    }
                }

                componentMax.Add(maxDist);
            }
        }

        return (componentId, componentMax);
    }

    // Separable box blur (horizontal pass then vertical pass) over the raw distance field, run before
    // normalization - smooths the level-set geometry itself rather than the final color, so it can't
    // introduce any color-mixing artifact, just rounder contours.
    static float[,] BoxBlur(float[,] src, int width, int height, int radius)
    {
        if (radius <= 0) return src;

        var horizontal = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                int count = 0;
                for (int ox = -radius; ox <= radius; ox++)
                {
                    int nx = x + ox;
                    if (nx < 0 || nx >= width) continue;
                    sum += src[nx, y];
                    count++;
                }
                horizontal[x, y] = sum / count;
            }
        }

        var result = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float sum = 0f;
                int count = 0;
                for (int oy = -radius; oy <= radius; oy++)
                {
                    int ny = y + oy;
                    if (ny < 0 || ny >= height) continue;
                    sum += horizontal[x, ny];
                    count++;
                }
                result[x, y] = sum / count;
            }
        }

        return result;
    }

    // Exact 2D distance-to-nearest-non-filled-cell via two exact 1D passes (columns, then rows using
    // the column pass's result as the base function) - see class comment for why this replaced an
    // earlier Dijkstra/chamfer approximation. Every column and every row is guaranteed to contain at
    // least one non-filled (land) seed, since `filled` is built from a padded bounding box whose
    // outermost ring is always land - so neither pass ever hits the "no seed in this line" degenerate
    // case the classic algorithm doesn't handle.
    static float[,] ComputeShoreDistanceExact(bool[,] filled, int width, int height, float cellSizeX, float cellSizeY)
    {
        const float Inf = 1e20f;

        var f = new float[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                f[x, y] = filled[x, y] ? Inf : 0f;

        // Pass 1: exact 1D squared-distance transform down every column.
        var colPass = new float[width, height];
        var columnBuffer = new float[height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++) columnBuffer[y] = f[x, y];
            var d = DistanceTransform1D(columnBuffer, height, cellSizeY);
            for (int y = 0; y < height; y++) colPass[x, y] = d[y];
        }

        // Pass 2: exact 1D squared-distance transform across every row, using pass 1's result as the
        // base function - combining the two gives the exact 2D squared Euclidean distance transform.
        var result = new float[width, height];
        var rowBuffer = new float[width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++) rowBuffer[x] = colPass[x, y];
            var d = DistanceTransform1D(rowBuffer, width, cellSizeX);
            for (int x = 0; x < width; x++) result[x, y] = d[x];
        }

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                result[x, y] = Mathf.Sqrt(result[x, y]);

        return result;
    }

    // One-dimensional exact squared-distance lower-envelope transform (Felzenszwalb & Huttenlocher).
    // `spacing` is the real-world distance between adjacent samples, so a non-square cellSize is
    // handled correctly by calling this once per axis with that axis's own spacing.
    static float[] DistanceTransform1D(float[] f, int n, float spacing)
    {
        var d = new float[n];
        var v = new int[n];
        var z = new float[n + 1];
        int k = 0;
        v[0] = 0;
        z[0] = float.NegativeInfinity;
        z[1] = float.PositiveInfinity;

        for (int q = 1; q < n; q++)
        {
            float pq = q * spacing;
            float pv = v[k] * spacing;
            float s = ((f[q] + pq * pq) - (f[v[k]] + pv * pv)) / (2f * pq - 2f * pv);
            while (s <= z[k])
            {
                k--;
                pv = v[k] * spacing;
                s = ((f[q] + pq * pq) - (f[v[k]] + pv * pv)) / (2f * pq - 2f * pv);
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = float.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            float pq = q * spacing;
            while (z[k + 1] < pq) k++;
            float pv = v[k] * spacing;
            d[q] = (pq - pv) * (pq - pv) + f[v[k]];
        }

        return d;
    }

    // Uncompressed + linear + bilinear + clamp: this is a smooth data field the shader interpolates
    // per-pixel, not sprite art - compression artifacts or point filtering would visibly corrupt the
    // gradient, and sRGB gamma conversion would skew the distance values themselves.
    static void ConfigureImporter(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.isReadable = false;

        var platformSettings = importer.GetDefaultPlatformTextureSettings();
        platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformTextureSettings(platformSettings);

        importer.SaveAndReimport();
    }

    static void CreateFolderRecursive(string path)
    {
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
