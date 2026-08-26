using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Shared factory for the emissive "drop here" telegraph glow used by BOTH the control-board
/// slot overlay (<see cref="PlayTray"/>) and the hand-fan insertion gap overlay
/// (<see cref="CardFan"/>) — extracted so the two look IDENTICAL (gold, additive, card-shaped).
/// The glow material routes through <see cref="PlayTray.OverlayShader"/> (the bundled
/// <c>GloomhavenVR/Overlay</c> additive shader) with a <c>Sprites/Default</c> fallback, exactly
/// as the board slot glow did before extraction — so board behaviour is unchanged.
/// </summary>
internal static class CardGlow
{
    /// <summary>
    /// An emissive glow material tinted <paramref name="color"/>: Overlay with ADDITIVE blend
    /// (<c>_SrcBlend=One,_DstBlend=One</c>) so the tint reads as light ADDED over the surface,
    /// ZWrite off, drawn in the transparent queue after the opaque board so it occludes
    /// naturally (no shine-through). Falls back to alpha-blended Sprites/Default when Overlay is
    /// absent. Null only when even the fallback shader is missing. (Verbatim move of the former
    /// <c>PlayTray.MakeGlowMaterial</c>.)
    /// </summary>
    internal static Material? MakeGlowMaterial(Color color)
    {
        Shader? s = PlayTray.OverlayShader();
        if (s != null)
        {
            var m = new Material(s) { color = color };
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); // additive
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }
        Shader? fb = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        return fb != null ? new Material(fb) { color = color } : null;
    }

    /// <summary>
    /// Build a card-shaped glow quad (the exact recipe the board slot overlay used): a collider-
    /// free <see cref="PrimitiveType.Quad"/> parented to <paramref name="parent"/> at
    /// <paramref name="localPos"/>/<paramref name="localScale"/>, tinted via
    /// <see cref="MakeGlowMaterial"/>, created INACTIVE (the caller toggles it). Returns the quad
    /// GameObject so the caller can attach extra behaviour (e.g. a pulse) or toggle it.
    /// </summary>
    internal static GameObject CreateGlowQuad(string name, Transform parent, Vector3 localScale,
        Vector3 localPos, Color color)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, worldPositionStays: false);
        quad.transform.localScale = localScale;
        quad.transform.localPosition = localPos;
        Material? mat = MakeGlowMaterial(color);
        if (mat != null)
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        quad.SetActive(false);
        return quad;
    }

    /// <summary>
    /// Attach the ladder ranking (<see cref="CardCueOrder"/>) to a mod-owned card cue that floats
    /// FREE of the control board — the hand fan's insertion glow, the item fan's usable frame.
    /// Idempotent; a null subtree is a no-op. Board-mounted cues must NOT use this: they belong to
    /// the board's furniture group (<c>PlayTray.AdoptFurniture</c>), which ranks the whole board as
    /// one cluster and keeps the board's internal tiers intact.
    /// </summary>
    internal static void RankWithPanels(GameObject? cue)
    {
        if (cue == null || cue.GetComponent<CardCueOrder>() != null)
            return;
        cue.AddComponent<CardCueOrder>();
    }

    // NOTE (removed) — KEEP THIS COMMENT. It documents code that is deliberately absent, which is
    // the only kind of note a reader cannot reconstruct from the source, and deleting it as "a
    // comment about nothing" invites exactly the re-addition it forbids.
    // An AddPulse/GlowPulse pair used to live here so a glow QUAD could breathe on its
    // own. Its only two callers were the item-card and items-stack "usable now" cues, and the user
    // rejected that whole flat-quad look — the card is now framed by WorldUI.SoftCueArt's outline
    // (breathing via SoftFramePulse) and the stack drifts embers, so nothing wanted a pulsing quad any
    // more. The board play slots keep their own PlayTray.SlotPulse, which is where that math came from.
}

/// <summary>
/// PERSPECTIVE FOR A CARD'S OUTLINE (user hardware report 2026-08-08, MR: "Die mixed reality
/// hintergründe schieben sich vor den outlines von karten, das darf nicht sein - die Perspektive
/// soll auch hier voll gewährleistet sein"). Per frame, seat this cue at the converted-panel
/// distance ladder's answer for its OWN eye distance, so it paints over everything genuinely behind
/// it and under everything genuinely in front of it.
///
/// <para>ROOT CAUSE, and why the CARD never had this bug but its OUTLINE did. A fan/tray card's
/// backing slab is depth-writing AlphaTest geometry at queue 2450 (<see cref="CardMesh"/>): it
/// stamps its own silhouette into the depth buffer before any canvas draws, and the MR backing plate
/// is <c>ZTest LEqual</c> (<c>WorldUI.MrBacking</c>'s material recipe, and the
/// <c>Sprites/Default</c> fallback's baked default), so a plate farther away than a card is
/// DISCARDED over the card body, per pixel, whatever the sorting numbers say. The mod's cue art
/// AROUND a card is the exact complement of that: <c>ItemsPile</c>'s usable frame "floats just
/// OUTSIDE the card silhouette" (its own doc) and the fan insertion glow is a quad 1.24x the card,
/// so both draw where NOTHING wrote depth — and both are alpha-blended transparents with
/// <c>ZWrite</c> off, so they write none either. Unity resolves transparent renderers by
/// sortingLayer → sortingOrder FIRST and only then by material renderQueue, these cues shipped at
/// sortingOrder 0..1, and every MR plate rides its panel's ladder slot at ≥
/// <c>CanvasConversion.PanelOrderBase</c> (100). So the plate painted last and there was no depth to
/// stop it: the dark rectangle of a panel seven metres away covered the gold outline of a card an
/// arm's length from the eye. Exactly the split the report describes — the card survives, its
/// outline does not.</para>
///
/// <para>WHY NOT A DEPTH FIX ON THE CUE ITSELF. Letting the cue write depth is the one option that
/// would make the plate's existing LEqual test resolve this per pixel, and it is not available:
/// these cues are soft-falloff, HOLLOW art (a 9-sliced outline with a transparent centre, an
/// additive halo), and <c>ZWrite</c> on an alpha-blended surface stamps every rasterised fragment
/// including the fully transparent ones. The stamp would be the cue's bounding RECTANGLE — a
/// hard-edged rectangular hole punched through every farther panel, which is precisely the artifact
/// class CanvasConversion.8.Order.cs exists to have removed ("a bounding box around a portrait IS
/// the portrait's rect"). Alpha-clipping it would need a shader change, and this recipe must keep
/// working on the <c>Sprites/Default</c> fallback with no bundle at all.</para>
///
/// <para>SO THE CUE JOINS THE LADDER, WHICH IS THIS MOD'S DEPTH SURROGATE FOR DEPTH-LESS
/// TRANSPARENTS — through the same seam the board tooltip and the Steam identity tags already use
/// (<c>CanvasConversion.OrderAboveDistance</c>; see <c>Net.BoardVisual.OrderWithPanels</c> for the
/// identical root cause on the avatar tags, "the Steam logo mixes with the menu window behind it").
/// That is deliberately NOT an escalation to a fixed high number: the answer is recomputed from the
/// cue's measured distance every frame, so a menu the player pulls IN FRONT of the fan still covers
/// the outline, and the cue keeps <c>ZTest LEqual</c> so real geometry (the board, a wall, a nearer
/// card) still occludes it exactly as before. Both directions hold, in MR and out — the plates are
/// only what MADE the defect visible, they are not what this fixes.</para>
///
/// <para>WHY ONLY THE FREE-FLOATING FANS CARRY THIS, and not every cue in the mod: a cue that lies
/// ON the control board already has the board's own OPAQUE, depth-writing slab immediately behind
/// it, so every plate FARTHER than the board is discarded over the whole board face before the
/// question of order can arise — the board slot glows, the pile captions and the status placard
/// only ever needed the board's furniture band (<c>PlayTray.AdoptFurniture</c>), which ranks the
/// board as one cluster. The two cues that ride this instead are the two that hang in FREE SPACE
/// with nothing but the passthrough room behind them: the hand fan's insertion glow, and the item
/// fan's usable frame (that fan floats 26 cm above the board's top edge and 5 cm proud of its face,
/// i.e. clear off the slab and right where the board-docked initiative track's plate lives, which is
/// what made the report constant rather than occasional).</para>
///
/// <para>COST: one distance, one short walk over the listed panels and a change-gated int write, per
/// ACTIVE cue per frame — and these cues are inactive except while the fan is open and the item is
/// usable, so nothing here runs at all while no cue is shown.</para>
/// </summary>
internal sealed class CardCueOrder : MonoBehaviour
{
    /// <summary>
    /// Sub-step lift above the farther panel's ladder slot — the identity tags' value, chosen for
    /// the identical reason (see <c>Net.BoardVisual.TagPanelLift</c>): it must stay under
    /// <c>CanvasConversion.PanelOrderStep</c> (16) so a cue can never climb into the NEXT panel's
    /// slot, and 12 clears that window's own decorations too (close X +2, grab bar +4, menu-laid
    /// tooltip +10) — a card outline hovering in front of a window covers the whole window, its
    /// furniture included.
    /// </summary>
    private const int CuePanelLift = 12;

    private Renderer? _renderer;
    private Canvas? _canvas;

    /// <summary>Last written order (change-gate). <c>int.MinValue</c> = never written, so the first
    /// tick always seats the cue — a one-frame gap at order 0..1 is the defect band itself.</summary>
    private int _applied = int.MinValue;

    private void Awake()
    {
        // Either carrier: the fan insertion glow is a MeshRenderer quad, the item usable frame is a
        // world-space Canvas. Both express their draw slot as a sortingOrder, and a cue never has
        // both, so writing whichever exists needs no branch beyond the null checks.
        _renderer = GetComponent<Renderer>();
        _canvas = GetComponent<Canvas>();
    }

    /// <summary>
    /// LateUpdate, not Update: the fans pose themselves in their own tick, and ranking a cue from a
    /// stale pose would be one frame late on a hand that moves fast. Reads the PREVIOUS frame's
    /// measured panel distances (CanvasConversion.TickPanelOrder runs last in the WorldUI chain) —
    /// a one-frame lag on a hysteresis-damped ladder is not observable, the same trade every other
    /// <c>OrderAboveDistance</c> caller already accepts.
    /// </summary>
    private void LateUpdate()
    {
        Camera? cam = WorldUI.CanvasConversion.WorldCamera;
        if (cam == null)
            return;
        float eyeDistance = Vector3.Distance(cam.transform.position, transform.position);
        int order = WorldUI.CanvasConversion.OrderAboveDistance(eyeDistance, CuePanelLift);
        if (order == _applied)
            return;
        _applied = order;
        if (_renderer != null)
            _renderer.sortingOrder = order;
        if (_canvas != null)
            _canvas.sortingOrder = order;
    }
}
