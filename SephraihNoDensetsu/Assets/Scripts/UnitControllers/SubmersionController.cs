using UnityEngine;

// Gives a unit a "wading" look while standing on a DualGridTilemapModule flagged submersive (tall
// grass, water). Uses the Sephraih/SpriteSubmersion shader on this unit's own SpriteRenderer material
// instead of a SpriteMask. The shader clips by the sprite mesh's own LOCAL vertex Y (world units,
// relative to this sprite's pivot = local Y 0) against a fixed WORLD-UNIT depth below the pivot - not
// a fraction of anything sprite-specific. Three other approaches were tried and rejected first, each
// confirmed live:
//   1. Texture UV as height fraction - broken, since almost every sprite here is sliced from a
//      multi-frame sheet, so its UV only spans a small sub-rectangle of the full sheet, not 0-1.
//   2. Sprite.vertices (Tight-mesh, alpha-cropped PER FRAME), normalized 0-1 - correctly scoped to
//      real content, but wobbly: a mid-swing pose's alpha silhouette shifts slightly frame to frame
//      (limbs, hair), so re-normalizing against that per-frame box every frame made the waterline
//      visibly jitter. A stable per-combo UNION of this across every frame, baked once at slice time,
//      fixed the wobble but is only as good as each hand-split char-layer frame's own alpha content -
//      and those aren't guaranteed complete/consistent (some combo frames are missing feet/legs where
//      the sword crosses them, others have a raised arm the others don't).
//   3. Sprite.bounds/full rect, normalized 0-1, corrected by how far this sheet's pivot sits from a
//      "centered" (0.5) baseline - stable and needs no per-frame data, but undershoots: pivot deviation
//      reflects the swing-envelope's OWN asymmetry (character + sword combined), not specifically how
//      padded the character-only content is, so the correction was too small on sheets with a lot of
//      excess crop padding (confirmed live on Down).
// A fixed world-unit depth sidesteps all of this: every combo sheet built this session shares the same
// PPU (64), so local Y already means the same real physical distance below pivot regardless of which
// direction/pose is showing or how differently that pose's own crop happens to be padded - the
// character's actual leg length doesn't change between combos, only the crop around it does.
// WeaponFX (the un-clipped weapon-effect layer) keeps its own default material and is untouched.
[RequireComponent(typeof(SpriteRenderer))]
public class SubmersionController : MonoBehaviour
{
    [Tooltip("Material using the Sephraih/SpriteSubmersion shader. Assigned to this unit's own " +
        "SpriteRenderer in Awake - shared across units, driven per-instance via MaterialPropertyBlock.")]
    [SerializeField] Material submersionMaterial;

    [Tooltip("World-space Y offset from this transform's position used for the submersion check - " +
        "tune if this unit's pivot isn't already at its feet.")]
    [SerializeField] float checkOffsetY = 0f;

    [Tooltip("World units below this unit's pivot (local Y = 0) the waterline sits while submerged - " +
        "a fixed real-world depth, not a fraction of any one sprite's own rect, so it stays visually " +
        "consistent across every animation/pose. Per-unit tunable so a tall player and a small chicken " +
        "can both wade convincingly; a reasonable starting point is checkOffsetY's own magnitude scaled " +
        "down to shin/ankle height rather than the full foot position.")]
    [SerializeField] float submergeDepth = 0.25f;

    [Tooltip("Thickness of the dithered transition band, in world units.")]
    [SerializeField] float bandThickness = 0.045f;

    [Tooltip("Extra depth the waterline dips at this unit's horizontal center vs. its edges, world " +
        "units - gives the band a concave \"meniscus\" curve instead of a flat horizontal line.")]
    [SerializeField] float curveDepth = 0.035f;

    [Tooltip("Half-width (world units, centered on this unit's own pivot X) the center dip fades out " +
        "over - beyond this the waterline is flat, matching the edges.")]
    [SerializeField] float curveWidth = 0.3f;

    SpriteRenderer spriteRenderer;
    MaterialPropertyBlock propertyBlock;

    static readonly int SubmergeDepthId = Shader.PropertyToID("_SubmergeDepth");
    static readonly int BandThicknessId = Shader.PropertyToID("_BandThickness");
    static readonly int CurveDepthId = Shader.PropertyToID("_CurveDepth");
    static readonly int CurveWidthId = Shader.PropertyToID("_CurveWidth");

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        propertyBlock = new MaterialPropertyBlock();
        if (submersionMaterial != null) spriteRenderer.material = submersionMaterial;
    }

    void Update()
    {
        if (submersionMaterial == null) return;

        bool submerged = DualGridTilemapModule.IsSubmerged(transform.position + Vector3.up * checkOffsetY);

        spriteRenderer.GetPropertyBlock(propertyBlock);
        // A large depth pushes the waterline far below any real content, so the dither branch never
        // triggers and the sprite renders exactly as it would with the default sprite shader.
        propertyBlock.SetFloat(SubmergeDepthId, submerged ? submergeDepth : 999f);
        propertyBlock.SetFloat(BandThicknessId, bandThickness);
        propertyBlock.SetFloat(CurveDepthId, curveDepth);
        propertyBlock.SetFloat(CurveWidthId, curveWidth);
        spriteRenderer.SetPropertyBlock(propertyBlock);
    }
}
