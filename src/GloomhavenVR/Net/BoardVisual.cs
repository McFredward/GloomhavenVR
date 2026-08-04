using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Tiny shared factory for the read-only remote-board visuals (<see cref="RemoteControlBoard"/>
/// + <see cref="OwnerTag"/>). Everything is UNLIT: the remote board and its ownership tag are
/// mod-owned cosmetics rendered by the mod head camera, and Gloomhaven's scenario/void lighting
/// is not something we control — the same rule the hands and head masks follow
/// (<see cref="HeadMaskLibrary"/>). Never touches game state.
/// </summary>
internal static class BoardVisual
{
    // ---- fixed intra-board draw sub-ladder --------------------------------------------------
    //
    // ROOT CAUSE (the remote twin of the LOCAL board's angle-dependent placard pop, user report
    // 2026-08-04 #2): every transparent visual on a remote board - the pick-banner parchment +
    // label, the mirrored widget canvases, the fallback chips, the synced tooltip - shipped at
    // sortingOrder 0. Unity resolves transparent renderers by sortingLayer -> sortingOrder FIRST
    // and falls back to per-renderer camera distance only on a tie, so two order-0 elements that
    // overlap on the board plane (the peer's pick banner hovers exactly where their initiative
    // mirror grows up past the board's top edge) swap winners with the viewing angle - the same
    // "mal so, mal so" blend-through the local board showed between Statustafel and portraits.
    //
    // THE SAME RULE as the local fix (CanvasConversion.9.Furniture.cs ROUND 2): the winner inside
    // one board's plane is decided ONCE, structurally, never by distance. Furniture at the
    // bottom, the board's docked widgets above it, the proud tooltip above both. The steps leave
    // room for an element's own internal offsets, and the whole sub-ladder stays far below the
    // converted-panel distance ladder (PanelOrderBase = 100), the dropdown overlays (3999+) and
    // the ray visuals (5000), so nothing outside the remote board is affected.

    /// <summary>Board furniture - the pick-banner plate/label and any other decor ON the board
    /// face. The default renderer order; named so the intent is greppable.</summary>
    internal const int OrderFurniture = 0;

    /// <summary>The board's docked widgets - mirrored widget canvases and the fallback chips.
    /// Strictly above <see cref="OrderFurniture"/>, at every viewing angle.</summary>
    internal const int OrderDockedWidget = 4;

    /// <summary>The synced board tooltip - sits PROUD of the board and annotates its content, so
    /// it must beat both tiers below.</summary>
    internal const int OrderTooltip = 8;

    // ---- free-floating identity tags ride the PANEL distance ladder -------------------------
    //
    // ROOT CAUSE (user report 2026-08-04: "the Steam logo mixes with the menu window behind it").
    // The Steam-avatar identity tags (RemoteNameTag over a peer's head, OwnerTag on a peer's
    // board corner) are transparent renderers (Sprites/Default quad + TMP label) that shipped at
    // sortingOrder 0. Unity resolves transparent renderers by sortingLayer -> sortingOrder FIRST
    // and only falls back to distance on a tie, and EVERY converted panel lives on the distance
    // ladder at order >= CanvasConversion.PanelOrderBase (100) - so a floated menu that was
    // spatially BEHIND the tag still painted LATER and alpha-blended over the avatar picture:
    // exactly the reported mixing. Perspective must be respected mod-wide (standing user
    // ruling), so the tags now rank against the ladder per frame through the same seam the
    // board tooltip uses: CanvasConversion.OrderAboveDistance answers "which order does a
    // non-panel plate at THIS eye distance need to draw over everything farther and under
    // every panel genuinely nearer".

    /// <summary>
    /// Sub-step lift of an identity tag above the farther panel's ladder slot. Must stay under
    /// CanvasConversion.PanelOrderStep (16) so the tag can never climb into the NEXT panel's
    /// slot; 12 puts a tag that is genuinely in front of a window above that window's own
    /// furniture too (close X +2, grab bar +4, menu-laid tooltip +10) - a billboard hovering
    /// before a window covers the whole window, decorations included.
    /// </summary>
    private const int TagPanelLift = 12;

    /// <summary>
    /// Per-frame: seat a free-floating tag's renderers at the converted-panel-ladder order for
    /// its eye distance (see the root-cause note above). One shared order for the whole row -
    /// the row's own internals (label 1 mm proud of its plate/quad) resolve on the distance
    /// tie-break exactly as before. Change-gated writes; Unity-null entries are skipped (the
    /// caller refreshes its cache on rebuild).
    /// </summary>
    internal static void OrderWithPanels(Renderer?[] renderers, float eyeDistance)
    {
        int order = WorldUI.CanvasConversion.OrderAboveDistance(eyeDistance, TagPanelLift);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer? r = renderers[i];
            if (r != null && r.sortingOrder != order)
                r.sortingOrder = order;
        }
    }

    /// <summary>An unlit material (optionally textured), so mod visuals read the same regardless
    /// of the surrounding scene lights. Mirrors <c>HeadMaskLibrary.UnlitMaterial</c>.</summary>
    internal static Material Unlit(Color color, Texture? texture = null)
    {
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default")
                        ?? Shader.Find("Hidden/InternalErrorShader");
        var m = new Material(shader)
        {
            color = new Color(color.r, color.g, color.b, color.a),
        };
        if (texture != null)
            m.mainTexture = texture;
        return m;
    }

    /// <summary>
    /// A collider-free quad on <paramref name="parent"/>, sized <paramref name="size"/> local
    /// metres in its XY plane. By the module convention (+Z points AWAY from the viewer) the quad's
    /// front reads from the −Z side, so a parent whose −Z faces the viewer shows the quad face-on.
    /// </summary>
    internal static MeshRenderer Quad(Transform parent, string name, Vector2 size, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }
}
