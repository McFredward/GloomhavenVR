using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// Give a MOD-HOSTED item card's burn/spent shader the card footprint it is actually drawn at, so
/// the flat game's on-card burn look survives the move onto a world-space canvas.
///
/// ─── THE REPORT (user, hardware, ModBuild 348) ──────────────────────────────────────────────────
/// "Den glühenden Effekt auf den verbrannten Gegenstandskarten ist nur ganz leicht am Rand
/// sichtbar, wieso nicht auf den Karten selbst wie im flat game auch?"
///
/// ─── WHAT THE FLAT GAME ACTUALLY DOES, read from the decompiled source ──────────────────────────
/// <c>ItemCardUI.UpdateState</c> maps <c>CItem.EItemSlotState.Consumed</c> →
/// <c>ItemCardEffects.ToggleEffect(true, FXTask.Consumed)</c> → <c>BurnCardTimeline</c>
/// (ItemCardEffects.cs:272). That timeline paints the burned card in TWO face-wide halves — there
/// is no rim object anywhere in the system:
/// <list type="number">
/// <item><b>THE FACE HALF</b> — a material sweep over every Image in <c>imgComp[]</c> (the card's
///   own art/frame/plate Images, on per-instance clones of <c>GUI_CardEffect_Mat</c> /
///   <c>GUI/AbilityCard_Shd</c>): <c>_Burn</c> 0.691 with <c>_Burn_ColourTint</c> (0.369, 0.145,
///   0.075, 0.601), <c>_Flow</c> 1, <c>_Dissolve</c> 0.646, <c>_GreyOut</c> 1,
///   <c>_AnimNoise_Mask</c> tiled 40, <c>_Dissolve_VerticalGradient</c> 0.2. This is the glow ON
///   THE CARD ITSELF — the half the user is asking for.</item>
/// <item><b>THE FRAME HALF</b> — the serialized <c>fgFx</c> Image, live GameObject
///   <c>UIFX_Overlay</c>, switched on and given <c>_TintColor</c> (1, 0.3, 0, 0.8),
///   <c>_ParticleTexture = overlayFrameBurn</c>, <c>_Glow</c> 3 and a settled <c>_FXAnim</c> 0.5
///   (ItemCardEffects.cs:314-322, :365). The mod's own face census measured this graphic on a real
///   hosted item card: <c>'UIFX_Overlay' [no sprite] 132 % of the face … 6 % of it outside the
///   card</c>. Its texture is an authored FRAME (the field is literally named
///   <c>overlayFrameBurn</c>), so what this half paints is the licking edge.</item>
/// </list>
/// So "only faintly at the RIM" is the picture you get when the FRAME half survives and the FACE
/// half does not. That is the split this class addresses, and the split the instrument below
/// measures rather than assumes.
///
/// ─── WHY THE FACE HALF IS LOST IN VR ────────────────────────────────────────────────────────────
/// Every term of the face half is multiplied by a CARD-LOCAL coordinate the shader reconstructs
/// from <c>_PosAndBounds</c>, and <c>ItemCardEffects.Initialize()</c> (from <c>Awake</c>, once,
/// never refreshed) writes it like this — ItemCardEffects.cs:169-175:
/// <code>
/// imgComp[l].material.SetVector(_posAndBounds,
///     new Vector4(rectTransform.position.x, rectTransform.position.y, cardBounds.x, cardBounds.y));
/// </code>
/// <c>rectTransform.position</c> is a WORLD position; <c>cardBounds</c> is the rect size in CANVAS
/// units. On the flat game's screen-space canvas those two are the same units (pixels), so the
/// shader's <c>(vertex - pos) / bounds</c> spans 0..1 across the card and the burn lands on the
/// card. <c>ItemsPile</c> hosts the REAL game card under a <c>RenderMode.WorldSpace</c> FaceCanvas
/// (ItemsPile.cs, TryHostRealCard — the widget is spawned straight under <c>canvasRect</c>, so
/// <c>Awake</c>/<c>Initialize</c> run there): <c>position</c> is now METRES (~0.1 … 1.5) while
/// <c>bounds</c> stays ~300 canvas units and the vertices uGUI hands the shader are canvas units
/// too. The offset term is wrong by roughly three card widths, which saturates a card-local
/// gradient to a constant — the face half stops varying across the card and stops reading as a
/// burn, while the frame half (a different Image, a different material, NO <c>_PosAndBounds</c> at
/// all) is untouched and keeps drawing its rim.
///
/// This is the SAME hazard the mod already refuses to walk into on the peer path —
/// <c>Net.RemoteCardArt.ReportBoundsRefusal</c> declines to switch the FX terms on when
/// <c>_PosAndBounds</c> is degenerate, calling it "the 'card renders DEEP BLACK' failure mode".
/// The hosted LOCAL item card was never given the same scrutiny: it keeps <c>ItemCardEffects</c>
/// live on a world-space canvas and calls <c>UpdateState(..., force: true)</c>, which turns exactly
/// those terms on against exactly that degenerate footprint.
///
/// ─── THE FIX ────────────────────────────────────────────────────────────────────────────────────
/// Re-derive the vector in the space the shader is actually reading — the ROOT CANVAS's local
/// space, which is where uGUI batches a Graphic's vertices — and write it back:
/// <c>(cardLocalX, cardLocalY, rect.width, rect.height)</c>. For the hosted card that is
/// (0, 0, native, native), because <c>ItemsPile</c> centres the card on its own FaceCanvas. Note
/// the SIZE half was already right; only the ORIGIN was in the wrong units, which is why the card
/// still renders normally at rest (all four FX terms are 0 there, so the term is inert) and only
/// the burned state looks wrong.
///
/// The sibling class <c>CardEffects</c> — the ability card's twin — writes an <b>anchored</b>
/// (canvas-local) position instead of a world one for the same shader, which is the evidence that
/// canvas-local is the space this vector is meant to be in.
///
/// ─── SAFETY ─────────────────────────────────────────────────────────────────────────────────────
/// <list type="bullet">
/// <item>NO SHARED ASSET IS EVER WRITTEN. The write only happens once <c>Initialize</c> has run —
///   proven by <c>ItemCardEffects.cardBounds</c> being non-zero, which that method sets in the same
///   breath as it replaces every <c>imgComp[i].material</c> with a PRIVATE
///   <c>new Material(...)</c>. Before that the Images still point at the shared
///   <c>GUI_CardEffect_Mat</c> and this class does nothing at all.</item>
/// <item>ONLY WORLD-SPACE CANVASES. A card still on a screen-space canvas is the flat game's own
///   configuration and is left exactly alone.</item>
/// <item>ONLY THE ITEM CARD. The gate is <c>ItemCardUI.cardEffects</c> on the passed root, so
///   ability faces (<c>CardEffects</c>) and peer clones (whose FX driver
///   <c>RemoteCardArt.StripFragileEffects</c> has destroyed) can never be reached from here.</item>
/// <item>NO DEGENERATE WRITE. A rect that measures under a canvas unit is refused and logged —
///   handing the shader a 0x0 footprint is the DEEP BLACK failure mode itself.</item>
/// <item>CHANGE-GATED. The vector is compared before it is written, so the 1 s rescan cadence is a
///   float compare and nothing else once the card is seated.</item>
/// </list>
/// MULTIPLAYER: this is a purely local presentation write on a material the game minted for one
/// widget on THIS client. A peer's mirrored item card is built by <c>Net.RemoteCardArt</c>, which
/// reconstructs the used look from <c>CItem.SlotState</c> on the host-replicated inventory every
/// client already holds — no wire traffic, no viewer dial, nothing to sync.
///
/// COST: one component fetch and a walk over <c>imgComp</c> (a handful of Images) per hosted item
/// card per <see cref="CardFaceMipBake.Rescan"/> tick, i.e. once a second per card, with a float
/// compare deciding whether anything is written at all. Nothing runs per frame and nothing runs
/// while no item card is hosted.
/// </summary>
internal static class CardFxBounds
{
    /// <summary>The screen-space footprint the card-FX shader reconstructs its card-local
    /// coordinate from. Read AND written here — see the class doc for why that is safe.</summary>
    private static readonly int PosAndBoundsId = Shader.PropertyToID("_PosAndBounds");

    /// <summary>Second half of the material SIGNATURE. The pair <c>_GreyOut</c> +
    /// <c>_PosAndBounds</c> is the same two-property test <see cref="CardHalfTone"/> and
    /// <c>Net.RemoteCardArt</c> use to recognise a card-FX material, so an unrelated UI shader that
    /// happens to expose one of them can never be mistaken for one.</summary>
    private static readonly int GreyOutId = Shader.PropertyToID("_GreyOut");

    private static readonly int BurnId = Shader.PropertyToID("_Burn");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");
    private static readonly int FxAnimId = Shader.PropertyToID("_FXAnim");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");

    /// <summary>Name of the burn/ghost overlay graphic — <c>ItemCardEffects.fgFx</c> is private, so
    /// the census below finds it by the live GameObject name the face inventory already records
    /// ('UIFX_Overlay'). A diagnostic may match on a name; the FIX above never does.</summary>
    private const string OverlayName = "UIFX_Overlay";

    private static bool s_reported;
    private static bool s_refusalReported;

    /// <summary>
    /// Reseat the card-FX footprint on a mod-hosted ITEM card. No-op for every other root. Called
    /// from <see cref="CardFaceMipBake.Rescan"/>, which is the one per-face pump both card kinds
    /// already run (the same seam <c>CardFace.Offer</c> uses, for the same reason: the item path
    /// then needs no change in a file this work does not own).
    /// </summary>
    internal static void Reseat(Component? faceRoot)
    {
        if (faceRoot == null)
            return;
        try
        {
            ItemCardUI? cardUI = faceRoot as ItemCardUI ?? faceRoot.GetComponent<ItemCardUI>();
            ItemCardEffects? fx = cardUI != null ? cardUI.cardEffects : null;
            if (fx == null || fx.imgComp == null)
                return;

            // Initialize() has not run yet: the Images still wear the SHARED material asset and
            // nothing here may touch them. cardBounds is set in the same method that clones them.
            if (fx.cardBounds.x <= 0f || fx.cardBounds.y <= 0f)
                return;

            var rect = fx.transform as RectTransform;
            if (rect == null)
                return;

            Canvas? canvas = rect.GetComponentInParent<Canvas>();
            Canvas? root = canvas != null ? canvas.rootCanvas : null;
            if (root == null || root.renderMode != RenderMode.WorldSpace)
                return; // the flat game's own configuration — leave it exactly alone

            Vector2 size = rect.rect.size;
            if (size.x < 1f || size.y < 1f)
            {
                ReportRefusalOnce(size);
                return;
            }

            Vector3 local = root.transform.InverseTransformPoint(rect.position);
            var want = new Vector4(local.x, local.y, size.x, size.y);

            int written = 0;
            int seen = 0;
            Vector4 was = Vector4.zero;
            for (int i = 0; i < fx.imgComp.Length; i++)
            {
                Image img = fx.imgComp[i];
                if (img == null)
                    continue;
                Material? mat = img.material;
                if (mat == null || !mat.HasProperty(GreyOutId) || !mat.HasProperty(PosAndBoundsId))
                    continue;
                seen++;
                Vector4 have = mat.GetVector(PosAndBoundsId);
                if (Approximately(have, want))
                    continue;
                was = have;
                mat.SetVector(PosAndBoundsId, want);
                written++;
            }

            if (written > 0)
                ReportOnce(fx, was, want, written, seen);
        }
        catch (System.Exception ex)
        {
            // A card that keeps its old footprint still renders; never break the host path for it.
            VRLog.Debug("Cards", $"Item card FX footprint reseat skipped ({ex.Message}).");
        }
    }

    private static bool Approximately(Vector4 a, Vector4 b) =>
        Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y)
        && Mathf.Approximately(a.z, b.z) && Mathf.Approximately(a.w, b.w);

    /// <summary>
    /// The ONE line the next hardware test has to answer the burned-item report with: what the
    /// game had written, what it is now, and — because the diagnosis above splits the look into two
    /// independently breakable halves — what the FRAME half is actually doing at the same moment.
    /// If the glow is still rim-only after this, the frame-half numbers here say whether the face
    /// half is now running (and the look simply is not what he wants) or whether it is still inert
    /// and <c>_PosAndBounds</c> was the wrong lead.
    /// </summary>
    private static void ReportOnce(ItemCardEffects fx, Vector4 was, Vector4 want, int written, int seen)
    {
        if (s_reported)
            return;
        s_reported = true;

        string overlay = DescribeOverlay(fx);
        string face = DescribeFace(fx);

        // HW-VERIFY
        VRLog.Note("Cards", "ITEM CARD FX FOOTPRINT reseated on the hosted item card: "
                            + $"_PosAndBounds was ({was.x:0.###}, {was.y:0.###}, {was.z:0.#}x{was.w:0.#}) "
                            + $"-> now ({want.x:0.###}, {want.y:0.###}, {want.z:0.#}x{want.w:0.#}) on "
                            + $"{written} of {seen} card-FX image(s). ItemCardEffects.Initialize writes "
                            + "that vector from rectTransform.position (WORLD metres on our world-space "
                            + "FaceCanvas) against a size in CANVAS units, so the card-local coordinate "
                            + "the burn/dissolve/flow terms are multiplied by was off by roughly three "
                            + "card widths — the face half of the burn stopped varying across the card "
                            + "while the UIFX_Overlay frame half kept drawing its rim. THE TEST "
                            + "(user: 'nur ganz leicht am Rand sichtbar, wieso nicht auf den Karten "
                            + "selbst'): burn/consume an item and look at the card FACE, not its edge. "
                            + "FACE HALF NOW: " + face + " FRAME HALF NOW: " + overlay
                            + " If the face terms below are non-zero and the card face still shows "
                            + "nothing, _PosAndBounds was the wrong lead and the next round belongs to "
                            + "the shader's own card-local reconstruction, not to this vector.");
    }

    /// <summary>The face half's settled terms, read back off the material they were written to.</summary>
    private static string DescribeFace(ItemCardEffects fx)
    {
        for (int i = 0; i < fx.imgComp.Length; i++)
        {
            Image img = fx.imgComp[i];
            Material? mat = img != null ? img.material : null;
            if (mat == null || !mat.HasProperty(GreyOutId) || !mat.HasProperty(PosAndBoundsId))
                continue;
            return $"_GreyOut {Read(mat, GreyOutId):0.00}, _Burn {Read(mat, BurnId):0.00}, "
                   + $"_Dissolve {Read(mat, DissolveId):0.00}, _Flow {Read(mat, FlowId):0.00} on "
                   + $"'{mat.shader?.name ?? "?"}' (rest is 0/0/0/0, a burned card is 1/0.69/0.65/1).";
        }
        return "no card-FX material found on imgComp at all — the face half cannot be running.";
    }

    /// <summary>The frame half: is <c>UIFX_Overlay</c> even drawing, and at what glow?</summary>
    private static string DescribeOverlay(ItemCardEffects fx)
    {
        Image[] images = fx.GetComponentsInChildren<Image>(includeInactive: true);
        for (int i = 0; i < images.Length; i++)
        {
            Image img = images[i];
            if (img == null || !img.gameObject.name.StartsWith(OverlayName))
                continue;
            Material? mat = img.material;
            float alpha = img.canvasRenderer != null ? img.canvasRenderer.GetInheritedAlpha() : -1f;
            return $"'{img.gameObject.name}' active={img.gameObject.activeInHierarchy}, "
                   + $"enabled={img.enabled}, inherited alpha {alpha:0.00}, colour alpha "
                   + $"{img.color.a:0.00}, _Glow {Read(mat, GlowId):0.00}, _FXAnim "
                   + $"{Read(mat, FxAnimId):0.00}, _TintColor "
                   + (mat != null && mat.HasProperty(TintColorId)
                       ? mat.GetColor(TintColorId).ToString()
                       : "absent")
                   + " (a burned card is _Glow 3, _FXAnim 0.50, tint RGBA(1, 0.3, 0, 0.8)).";
        }
        return $"no '{OverlayName}' graphic under the card — the frame half is not present at all.";
    }

    private static float Read(Material? mat, int id) =>
        mat != null && mat.HasProperty(id) ? mat.GetFloat(id) : -1f;

    private static void ReportRefusalOnce(Vector2 size)
    {
        if (s_refusalReported)
            return;
        s_refusalReported = true;
        VRLog.Warn("Cards", $"Item card FX footprint REFUSED: the hosted card's rect measures "
                            + $"{size.x:0.##}x{size.y:0.##} canvas units, which is a degenerate "
                            + "footprint — writing it into _PosAndBounds is the 'card renders DEEP "
                            + "BLACK' failure mode Net.RemoteCardArt refuses for the same reason. The "
                            + "card keeps whatever ItemCardEffects.Initialize wrote.");
    }
}
