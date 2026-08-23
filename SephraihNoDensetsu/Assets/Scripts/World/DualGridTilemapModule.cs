using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

// Reactive dual-grid renderer (jess::codes' technique): an invisible "data" Tilemap holds simple
// boolean paint (any non-null tile = filled); this keeps a separate "render" Tilemap - offset by
// (0.5, 0.5) so it sits on the corners between data cells - in sync, picking one of 16 sprites per
// render cell from the 4 data cells touching that cell's corners. Subscribes to the static
// Tilemap.tilemapTileChanged event, which fires for both live Tile Palette painting in the Editor
// and runtime SetTile calls, so the visible result stays live with no manual refresh step.
//
// This exists because a single-grid RuleTile (one sprite per painted cell, chosen from that same
// cell's own neighbors) structurally can't draw a 1-wide line or an isolated single-tile dot/hole -
// every cell in a thin shape has zero filled corners under any single-grid corner formula, since
// there's no sub-cell resolution to place a single quarter-corner into. True dual grid fixes this by
// giving each RENDER cell its own corner state sampled from 4 DATA cells, so painting one data cell
// shows up as 4 quarter-corner sprites forming a rounded dot, and a 1-wide line renders as a
// connected strip. See project_ruletile_corner_scheme memory for the full history.
[ExecuteAlways]
public class DualGridTilemapModule : MonoBehaviour
{
    public Tilemap dataTilemap;
    public Tilemap renderTilemap;

    [Tooltip("Resources-relative path to the 16-sprite sheet, e.g. 'Tiles/mudred'.")]
    public string spriteResourcePath = "Tiles/mudred";

    [Tooltip("Sprites must be named '{spriteNamePrefix}_{row}_{col}'.")]
    public string spriteNamePrefix = "mudred";

    [Tooltip("Optional group id (e.g. \"water\", \"grass\"). Materials sharing the same non-empty " +
        "groundType, in the same scene, merge their corner-fill shape across their shared border so no " +
        "seam is drawn there - each render cell still shows exactly one member's own texture. Empty = " +
        "standalone, unchanged behavior.")]
    [SerializeField] string groundType = "";

    Dictionary<(bool nw, bool ne, bool sw, bool se), Tile> tileByCorner;

    // Grouped modules merge corner-fill SHAPE across every member sharing the same groundType+scene
    // (so a shared border reads as continuous, no double edge), while each render cell is still
    // written by exactly one "owning" member (the one contributing the most filled corners at that
    // cell) - see UpdateRenderCell. Scoped by scene (not just the string) so unrelated map instances,
    // inactive same-scene siblings, and Prefab Stage previews never cross-register with each other.
    static readonly Dictionary<(string groundType, Scene scene), List<DualGridTilemapModule>> membersByGroup = new();

    bool Grouped => !string.IsNullOrEmpty(groundType);
    (string, Scene) GroupKey => (groundType, gameObject.scene);
    List<DualGridTilemapModule> GroupMembers() => membersByGroup.TryGetValue(GroupKey, out var list) ? list : null;

    void OnEnable()
    {
        BuildLookup();
        Tilemap.tilemapTileChanged += OnTilemapChanged;

        if (Grouped)
        {
            var key = GroupKey;
            if (!membersByGroup.TryGetValue(key, out var list)) membersByGroup[key] = list = new List<DualGridTilemapModule>();
            if (!list.Contains(this)) list.Add(this);
        }

        RebuildAll();

        // Unity doesn't guarantee OnEnable order across independent GameObjects - a sibling that
        // already rebuilt before I registered saw an incomplete group and would otherwise never
        // recheck. Waking every other current member here makes this self-correct regardless of
        // load order.
        if (Grouped)
            foreach (var sib in membersByGroup[GroupKey])
                if (sib != this) sib.RebuildAll();
    }

    void OnDisable()
    {
        Tilemap.tilemapTileChanged -= OnTilemapChanged;

        if (Grouped && membersByGroup.TryGetValue(GroupKey, out var list))
        {
            list.Remove(this);
            if (list.Count == 0) membersByGroup.Remove(GroupKey);
            else foreach (var sib in list) sib.RebuildAll(); // reclaim cells I owned
        }
    }

    void BuildLookup()
    {
        tileByCorner = new Dictionary<(bool, bool, bool, bool), Tile>();
        var sprites = Resources.LoadAll<Sprite>(spriteResourcePath);
        var spriteByName = new Dictionary<string, Sprite>();
        foreach (var s in sprites) spriteByName[s.name] = s;

        // corner-combo (nw,ne,sw,se) -> (row,col) in the 4x4 sheet, from empirical pixel sampling
        // of the reference tileset. (false,false,false,false) is intentionally absent: a render
        // cell with zero filled corners should have no tile at all, not a visible "empty" sprite.
        var comboToRowCol = new Dictionary<(bool nw, bool ne, bool sw, bool se), (int row, int col)>
        {
            [(false, false, true, false)] = (0, 0),
            [(false, true, false, true)] = (0, 1),
            [(true, false, true, true)] = (0, 2),
            [(false, false, true, true)] = (0, 3),
            [(true, false, false, true)] = (1, 0),
            [(false, true, true, true)] = (1, 1),
            [(true, true, true, true)] = (1, 2),
            [(true, true, true, false)] = (1, 3),
            [(false, true, false, false)] = (2, 0),
            [(true, true, false, false)] = (2, 1),
            [(true, true, false, true)] = (2, 2),
            [(true, false, true, false)] = (2, 3),
            [(false, false, false, true)] = (3, 1),
            [(false, true, true, false)] = (3, 2),
            [(true, false, false, false)] = (3, 3),
        };

        foreach (var kvp in comboToRowCol)
        {
            string spriteName = $"{spriteNamePrefix}_{kvp.Value.row}_{kvp.Value.col}";
            if (!spriteByName.TryGetValue(spriteName, out var sprite))
            {
                Debug.LogError($"[DualGridTilemapModule] Missing sprite '{spriteName}' in Resources/{spriteResourcePath}");
                continue;
            }
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            tileByCorner[kvp.Key] = tile;
        }
    }

    [ContextMenu("Rebuild All Render Tiles")]
    public void RebuildAll()
    {
        if (dataTilemap == null || renderTilemap == null) return;
        if (tileByCorner == null) BuildLookup();

        renderTilemap.ClearAllTiles();
        dataTilemap.CompressBounds(); // cellBounds can lag behind a batch of SetTile calls otherwise
        var bounds = dataTilemap.cellBounds;
        var touched = new HashSet<Vector3Int>();
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (dataTilemap.GetTile(pos) == null) continue;
            AddTouchedRenderCells(pos, touched);
        }
        foreach (var rc in touched)
            UpdateRenderCell(rc);
    }

    void OnTilemapChanged(Tilemap changedTilemap, Tilemap.SyncTile[] syncTiles)
    {
        if (renderTilemap == null) return;
        bool isSelf = changedTilemap == dataTilemap;
        bool isSibling = !isSelf && Grouped && IsSiblingDataTilemap(changedTilemap);
        if (!isSelf && !isSibling) return;
        if (tileByCorner == null) BuildLookup();

        var touched = new HashSet<Vector3Int>();
        foreach (var sync in syncTiles)
            AddTouchedRenderCells(sync.position, touched);

        foreach (var rc in touched)
            UpdateRenderCell(rc);
    }

    bool IsSiblingDataTilemap(Tilemap tilemap)
    {
        var members = GroupMembers();
        if (members == null) return false;
        foreach (var m in members)
            if (m != null && m != this && m.dataTilemap == tilemap) return true;
        return false;
    }

    // A changed data cell at (dx,dy) is a corner of exactly the 4 render cells in the
    // (dx-1..dx, dy-1..dy) block - see project_ruletile_corner_scheme memory for the derivation.
    static void AddTouchedRenderCells(Vector3Int dataPos, HashSet<Vector3Int> touched)
    {
        touched.Add(new Vector3Int(dataPos.x - 1, dataPos.y - 1, 0));
        touched.Add(new Vector3Int(dataPos.x, dataPos.y - 1, 0));
        touched.Add(new Vector3Int(dataPos.x - 1, dataPos.y, 0));
        touched.Add(new Vector3Int(dataPos.x, dataPos.y, 0));
    }

    (bool nw, bool ne, bool sw, bool se) ComputeOwnCorners(Vector3Int renderCell)
    {
        bool sw = dataTilemap.GetTile(new Vector3Int(renderCell.x, renderCell.y, 0)) != null;
        bool se = dataTilemap.GetTile(new Vector3Int(renderCell.x + 1, renderCell.y, 0)) != null;
        bool nw = dataTilemap.GetTile(new Vector3Int(renderCell.x, renderCell.y + 1, 0)) != null;
        bool ne = dataTilemap.GetTile(new Vector3Int(renderCell.x + 1, renderCell.y + 1, 0)) != null;
        return (nw, ne, sw, se);
    }

    void WriteMergedTile(Vector3Int renderCell, (bool nw, bool ne, bool sw, bool se) c)
    {
        if (!c.nw && !c.ne && !c.sw && !c.se)
        {
            renderTilemap.SetTile(renderCell, null);
            return;
        }
        if (tileByCorner.TryGetValue(c, out var tile))
            renderTilemap.SetTile(renderCell, tile);
    }

    void UpdateRenderCell(Vector3Int renderCell)
    {
        if (!Grouped)
        {
            WriteMergedTile(renderCell, ComputeOwnCorners(renderCell));
            return;
        }

        // Shape merges across the whole group (OR), but exactly one member "owns" this render cell
        // for texture purposes - whichever contributes the most filled corners here. Every other
        // member must explicitly clear its own render tile at this position, in case it owned this
        // cell before a repaint shifted ownership away.
        var members = GroupMembers() ?? new List<DualGridTilemapModule> { this };
        bool nw = false, ne = false, sw = false, se = false;
        DualGridTilemapModule owner = null;
        int bestOwnCount = -1;

        foreach (var m in members)
        {
            if (m == null) continue;
            var c = m.ComputeOwnCorners(renderCell);
            nw |= c.nw; ne |= c.ne; sw |= c.sw; se |= c.se;

            int ownCount = (c.nw ? 1 : 0) + (c.ne ? 1 : 0) + (c.sw ? 1 : 0) + (c.se ? 1 : 0);
            if (ownCount == 0) continue;
            if (ownCount > bestOwnCount ||
                (ownCount == bestOwnCount && string.CompareOrdinal(m.gameObject.name, owner.gameObject.name) < 0))
            {
                bestOwnCount = ownCount;
                owner = m;
            }
        }

        if (owner != this)
        {
            renderTilemap.SetTile(renderCell, null);
            return;
        }
        WriteMergedTile(renderCell, (nw, ne, sw, se));
    }
}
