using UnityEditor;
using UnityEngine;

// Editor-only utility: bakes a Terrain painted in the disposable Assets/Scenes/_TerrainAuthoring.unity
// scene into a flat top-down PNG, imported as an ordinary Sprite matching this project's existing tile-art
// import conventions (see Assets/Resources/Sprites/grf_tiles/Sprites/grf.png.meta). The Terrain itself is
// authoring-only - Unity's texture-layer painting brushes let map artists blend material textures the way
// this project's tile-based ground can't (continuous alpha blending vs. discrete tile edges, plus free
// height-driven shading if sculpted) - but nothing Terrain-specific ever ships: no live Terrain, no
// TerrainCollider, nothing beyond the baked pixels ends up in a real game scene.
public static class TerrainBakeTool
{
    const string OutputFolder = "Assets/Resources/Sprites/Backgrounds";
    const string BakeCameraName = "[TerrainBakeCamera]";

    [MenuItem("Tools/Sephraih/Terrain Bake/Bake Selected Terrain To Sprite")]
    public static void BakeSelected()
    {
        var go = Selection.activeGameObject;
        var terrain = go != null ? go.GetComponent<Terrain>() : null;
        if (terrain == null)
        {
            Debug.LogError("[TerrainBakeTool] Select a GameObject with a Terrain component first.");
            return;
        }
        Bake(terrain);
    }

    // Renders `terrain` from directly overhead into a PNG and imports it as a Sprite. `ppu` (pixels per
    // world unit) defaults to 16, not this project's tile-art 64 - a full-res bake of a ~100x100-unit zone
    // at 64 PPU would be several thousand pixels square (well past this project's existing 2048 texture-size
    // cap) for art that only ever sits BEHIND 64px/unit foreground tiles; 16 stays comfortably under that cap
    // and is plenty crisp at that visual depth. Pass a higher value for a later final/hero-quality pass.
    public static void Bake(Terrain terrain, float ppu = 16f)
    {
        var data = terrain.terrainData;
        Vector3 corner = terrain.transform.position; // Terrain's own position is its MIN corner, not its center.
        Vector3 center = corner + new Vector3(data.size.x / 2f, 0f, data.size.z / 2f);

        int width = Mathf.RoundToInt(data.size.x * ppu);
        int height = Mathf.RoundToInt(data.size.z * ppu);

        // heightmapPixelError/basemapDistance are LOD settings tuned for a perspective gameplay camera
        // moving through the world at normal draw distances, not a single static overhead snapshot - left
        // alone, a bake can silently render at less than the terrain's full painted/sculpted detail. Force
        // full detail for the bake, then restore whatever the terrain was actually set to.
        float savedPixelError = terrain.heightmapPixelError;
        float savedBasemapDistance = terrain.basemapDistance;
        terrain.heightmapPixelError = 1f;
        terrain.basemapDistance = Mathf.Max(terrain.basemapDistance, 5000f);

        GameObject camGO = null;
        RenderTexture rt = null;
        Texture2D tex = null;
        try
        {
            camGO = new GameObject(BakeCameraName);
            var cam = camGO.AddComponent<Camera>();
            camGO.transform.position = center + Vector3.up * (data.size.y + 50f);
            camGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // straight down
            cam.orthographic = true;
            cam.orthographicSize = data.size.z / 2f;
            cam.aspect = (float)width / height;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = data.size.y + 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.allowMSAA = true;

            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            // A Terrain's internal render/quadtree data isn't always ready on the very first frame
            // after creation or a property change (confirmed live: the first bake of a freshly-created
            // Terrain came back fully blank/transparent - camera, RT, and Terrain all correctly
            // configured, nothing wrong except timing). One throwaway Render() before the real one gives
            // the Terrain a chance to finish initializing; costs one extra render, never visible.
            cam.Render();
            cam.Render();

            tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;

            byte[] png = tex.EncodeToPNG();

            if (!AssetDatabase.IsValidFolder(OutputFolder))
                CreateFolderRecursive(OutputFolder);

            string path = $"{OutputFolder}/{terrain.name}_bg.png";
            System.IO.File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            ConfigureImporter(path, ppu, Mathf.Max(width, height));

            Debug.Log($"[TerrainBakeTool] Baked '{terrain.name}' -> {path} ({width}x{height}px @ {ppu} PPU).");
        }
        finally
        {
            if (camGO != null) Object.DestroyImmediate(camGO);
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (tex != null) Object.DestroyImmediate(tex);
            terrain.heightmapPixelError = savedPixelError;
            terrain.basemapDistance = savedBasemapDistance;
        }
    }

    // Matches this project's existing tile-art import convention (see grf_tiles/Sprites/grf.png.meta) -
    // Bilinear/Clamp/Compressed/no-mipmaps/alphaIsTransparency - except spriteImportMode, which is Single
    // here (one big background image) rather than Multiple (a tile sheet with per-cell SpriteRects).
    static void ConfigureImporter(string path, float ppu, int maxDimension)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ppu;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;

        var platformSettings = importer.GetDefaultPlatformTextureSettings();
        platformSettings.textureCompression = TextureImporterCompression.Compressed;
        platformSettings.compressionQuality = 50;
        platformSettings.maxTextureSize = NextPow2AtLeast(maxDimension);
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

    static int NextPow2AtLeast(int value)
    {
        int p = 32;
        while (p < value) p *= 2;
        return Mathf.Min(p, 8192);
    }
}
