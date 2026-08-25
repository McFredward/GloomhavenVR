using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// LOCALIZED TEXT CUT INTO THE BOARD — the round number, and the captions that teach a new player
/// what the symbol-only caps mean.
///
/// <para><b>THE USER'S REQUIREMENT, verbatim.</b> <i>"Eventuell kannst du auch Text dynamisch in das
/// board mit einarbeiten? Zb über dem long-rest button. Wichtig ist hierbei nur das a) es
/// lokalisiert sein kann (deutsch, englisch) und b) es nativ und immersiv in dem board verarbeitet
/// ist, nicht einfach als schwebender Text darüber. Das gilt übrigens auch für den Rundentext."</i>
/// Both halves are constraints, and they pull against each other: engraving lives in a texture,
/// localization does not.</para>
///
/// <para><b>WHY IT IS NOT WHAT THE DESIGN DOC ASKED FOR, and the doc is wrong rather than the
/// implementation.</b> <c>.planning/BOARD-BUTTON-OVERHAUL.md</c> specifies "a TMP mesh … using the
/// board's own lit material family (<c>BoardLit</c>) rather than a UI shader, so it takes the same
/// light". That cannot be built. A TMP glyph is not a shape, it is a SIGNED DISTANCE FIELD in the
/// font atlas, decoded by the TMP shader's own smoothstep against a per-glyph gradient scale;
/// any shader that samples that atlas as an ordinary texture — <c>BoardLit</c> included — draws a
/// grey blur, not a letter. <c>BoardLit</c> is also strictly opaque (<c>Tags Queue=Geometry</c>,
/// <c>return fixed4(col, 1.0)</c>, no blend state, no cutout), so it has no way to leave the board
/// showing between the strokes. The doc's fallback — cutting shallow recesses in
/// <c>gen_board.py</c> — is closed this round because another lane owns that file and the board
/// FBXes.</para>
///
/// <para><b>WHAT IS BUILT INSTEAD.</b> The carve is done by the TMP distance-field material itself,
/// with the recipe INVERTED from the one the keycap labels wear, and the inversion is the whole
/// argument. A cap label sits PROUD on a key, so it is lit on top and casts its shadow down: bright
/// parchment fill, dark keyline, dark underlay offset down-right
/// (<c>WorldUI.NativeButtonSkin.StyleEngravedLabel</c>). A board label is CUT IN, so the inside of
/// the stroke is in shadow and the far lip of the cut catches the light: dark fill the colour of
/// the board's own material in shadow, a darker keyline for the shaded wall, and a LIGHT underlay
/// offset toward the lit side for the lip. Same machinery, opposite physics, and both are visibly
/// consistent with the one light the board is actually under.</para>
///
/// <para><b>WHERE THE LIGHT COMES FROM — read off the shader, not assumed.</b> <c>BoardLit</c>'s
/// baked key is <c>normalize(0.35, 0.85, -0.45)</c> in WORLD space: up, toward the viewer, and a
/// little to the right. A board faces the player with its local +Y up and its local +X right, so in
/// the label's own frame the light arrives from ABOVE and slightly from the RIGHT. For an incision
/// that puts the lit lips on the BOTTOM and LEFT of every stroke (those walls face the light) and
/// the shadowed lips on the top and right. The underlay is therefore offset DOWN AND LEFT, which is
/// the opposite of the cap labels' down-and-right drop shadow — as it must be.</para>
///
/// <para><b>DEPTH HONESTY.</b> The label is seated <see cref="ProudLocalZ"/> toward the viewer, i.e.
/// 0.8 mm in board-local units, which is 0.34–0.70 mm in world across the shipped board scales. It
/// cannot z-fight: a TMP mesh draws in the transparent queue with ZWrite off and ZTest LEqual, so
/// it is decided by ONE comparison against the board's own depth, and 0.34 mm is four orders of
/// magnitude above the depth quantum at any distance a player can put the board at (≈ 0.3 µm at
/// 0.5 m, ≈ 11 µm at 3 m for a 24-bit buffer over this near/far). The offset is small because it
/// has to READ as flush, not because the depth buffer needs it. What a distant label can lose is
/// stroke crispness — an aliasing property of the SDF, not of depth — and that is measured in the
/// preview station rather than argued here.</para>
///
/// <para><b>NO BACKING PLATE, EVER.</b> A quad behind the text is exactly the defect the round
/// readout was reported for (<c>Net/RemoteStatusReadouts</c>: <i>"die Runden-Anzeige sitzt auf einem
/// grauen Kasten"</i>). It would also be unbuildable honestly here: matching the board would mean
/// sampling that board's atlas, and the atlases import non-readable.</para>
/// </summary>
internal static class BoardEngraving
{
    /// <summary>
    /// How far toward the viewer (board-local −Z) an engraved label is seated. See the depth
    /// paragraph in the class doc: it is small so the label reads as part of the surface, and it
    /// is not zero so the label is never coplanar with the face it is cut into.
    /// </summary>
    internal const float ProudLocalZ = -0.0008f;

    /// <summary>
    /// THE PALETTE, one row per board style: the groove FLOOR (the fill), the LIT LIP (the
    /// underlay) and the shaded KEYLINE (the outline).
    ///
    /// <para>They are AUTHORED here rather than sampled, and the reason is a hard one: the board
    /// atlases import with <c>isReadable = 0</c>, so the plugin cannot call <c>GetPixel</c> on one,
    /// and turning readability on would double a 4 MB texture's memory to recover three colours.
    /// They are MEASURED, not chosen — each row is that style's own BOARD ALBEDO FACE BAND mean RGB
    /// times 0.45 / 1.35 / 0.22, printed by <c>unity/board-prep/buttons/cap_check.py --palette</c>,
    /// which exists so the numbers can be re-derived instead of re-guessed.</para>
    ///
    /// <para><b>THE REFERENCE IS THE BOARD, NOT THE KEYCAP PLATE, and the first version got that
    /// wrong.</b> It derived these from the cap material plate, on the reasoning that a board and
    /// its keys are the same material family. For oak and bronze that is very nearly true — the cap
    /// plate is 0.96x and 1.06x the board's own face band. For STEEL it is not: the cap is dark
    /// blued iron (0.275) and the board's face is bright brushed silver (0.418), a ratio of 0.68.
    /// Cut into the real steel board, the plate-derived fill would have landed a 70 % drop where
    /// 55 % was intended — black text rather than a groove — and, worse, the "lit lip" at 0.372
    /// would have been DARKER than the 0.418 board around it, inverting the one cue that says the
    /// mark is cut IN rather than raised. An engraving's reference is the surface it is engraved
    /// into; there was never a second candidate.</para>
    /// </summary>
    private static readonly Color[] GrooveFloor =
    {
        new(0.260f, 0.192f, 0.129f, 1f), // Oak    — shadowed heartwood
        new(0.188f, 0.178f, 0.174f, 1f), // Steel  — the dark of a struck groove in brushed steel
        new(0.201f, 0.177f, 0.118f, 1f), // Bronze — shadowed metal under the patina
    };

    private static readonly Color[] LitLip =
    {
        new(0.780f, 0.577f, 0.387f, 1f),
        new(0.565f, 0.535f, 0.522f, 1f),
        new(0.604f, 0.531f, 0.353f, 1f),
    };

    private static readonly Color[] Keyline =
    {
        new(0.127f, 0.094f, 0.063f, 1f),
        new(0.092f, 0.087f, 0.085f, 1f),
        new(0.098f, 0.087f, 0.057f, 1f),
    };

    /// <summary>Width of the shaded keyline as a fraction of the SDF spread. Thin: it is the wall
    /// of a cut seen almost edge-on, not a drawn border.</summary>
    private const float KeylineWidth = 0.09f;

    /// <summary>How far the lit lip peeks out from under the glyph, in SDF units. Negative X and Y
    /// = down and LEFT, i.e. the two walls the baked key actually falls on (see the class doc).</summary>
    private const float LipOffsetX = -0.28f;
    private const float LipOffsetY = -0.28f;

    /// <summary>How soft the lit lip is. Softer than the cap labels' drop shadow (0.35): a lip is a
    /// chamfer catching light along its length, not a shadow thrown onto a surface behind.</summary>
    private const float LipSoftness = 0.14f;

    /// <summary>The lit lip's opacity. Below 1 because the lip is a narrow face at a glancing angle
    /// to the key, not a fully-lit surface.</summary>
    private const float LipAlpha = 0.85f;

    // ---- WHERE THE CAPTIONS SIT — one home, two boards ---------------------------------------
    //
    // These live HERE and not at their call sites because the owner's board and every peer's
    // mirror of it both place the same three captions, and a caption that is 4 mm higher on a
    // teammate's board than on their own is exactly the 1:1 divergence this project keeps paying
    // for. The alternative — an authored constant on each side plus an entry in
    // scripts/check-mirrors.sh — is the arrangement that file's own header keeps recommending
    // against when the value can simply be shared. Nothing here is tunable and nothing is on the
    // wire: both sides derive the same number from the same board.

    /// <summary>Clearance from a rest disc's CENTRE to its caption's centre, board-local metres:
    /// half the disc plus a little. Taken from the disc that was actually built, so a board whose
    /// pads force a smaller disc gets its caption closer in rather than stranded out on the plate.</summary>
    internal static float RestCaptionOffsetY(float capHeight) => capHeight * 0.5f + 0.016f;

    /// <summary>The rest captions' fit box, board-local metres. Sized so the longer of each DE/EN
    /// pair fits at the same size as the shorter — "KURZE RAST" against "SHORT REST", "LANGE RAST"
    /// against "LONG REST" — because a caption that changes size with the language reads as two
    /// different engravings rather than one.</summary>
    internal static readonly Vector2 RestCaptionBox = new(0.105f, 0.017f);

    /// <summary>How far ABOVE the follow/pin anchor its caption sits, board-local metres. The pin
    /// anchor is BELOW the board's own bottom edge (it lives beside the handle bar, at y −0.19
    /// against a −0.16 edge), so the caption is lifted back onto the board's bottom margin: its
    /// centre lands near −0.148, inside the edge with room for its own half-height.</summary>
    internal const float PinCaptionLiftY = 0.042f;

    /// <summary>The follow/pin caption's fit box, board-local metres — sized for the longer of BOTH
    /// pairs at once ("FIXIERT"/"PINNED", "FOLGEN"/"FOLLOW") so the engraving does not resize when
    /// the state changes any more than when the language does.</summary>
    internal static readonly Vector2 PinCaptionBox = new(0.085f, 0.016f);

    /// <summary>Preferred glyph size for a board caption. One number for all of them: they are the
    /// same kind of mark cut by the same hand, and TmpFit shrinks each into its own box.</summary>
    internal const float CaptionMaxFontSize = 0.20f;

    private static bool _logged;

    private static int Row(ControlBoard style)
    {
        int i = (int)style;
        return i >= 0 && i < GrooveFloor.Length ? i : 0;
    }

    /// <summary>
    /// Build one engraved caption under <paramref name="parent"/>.
    ///
    /// <para><paramref name="localPos"/> is in the PARENT's frame and its Z is overwritten with
    /// <see cref="ProudLocalZ"/> — every caller wants the surface, and letting each one pass its
    /// own depth is how a "flush" label ends up floating on one board and buried on another.
    /// <paramref name="box"/> is the fit box in the same units; the text shrinks to fit it, which
    /// is what makes the longer of each DE/EN pair ("Kurze Rast" against "Short Rest") land inside
    /// the same cut.</para>
    /// </summary>
    internal static TextMeshPro Create(Transform parent, string name, Vector3 localPos, Vector2 box,
                                       float maxFontSize, ControlBoard style,
                                       TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = new Vector3(localPos.x, localPos.y, ProudLocalZ);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = string.Empty;
        tmp.alignment = align;
        // The game's own HUD face, so the cut looks like it was made by whoever made the board —
        // the same font every native caption on this board already uses. Font BEFORE style: the
        // material instance a font swap creates would otherwise be styled and then thrown away
        // (the ordering NativeButtonSkin's own callers document).
        WorldUI.NativeButtonSkin.ApplyFont(tmp);
        Restyle(tmp, style);
        Core.TmpFit.Fit(tmp, box.x, box.y, maxFontSize: maxFontSize, wrap: false);
        // A CUT IN THE BOARD IS STILL A TRANSPARENT RENDERER THAT WRITES NO DEPTH. It is the
        // 2026-08-04 status-placard defect family exactly: the board's opaque surfaces resolve
        // against a converted panel per pixel, and its depthless transparent ones do not, so a
        // panel docked BEHIND the board paints straight over them. The caller adopts the subtree
        // into the board's furniture draw-order group; this sets the in-band order FIRST, because
        // AdoptFurniture captures whatever order it finds as the offset. Two, so an engraving sits
        // above the slot glows (0..1) and below the keycap captions (3) — a caption is on a key in
        // front of the board and a carving is in the board itself.
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
            mr.sortingOrder = 2;
        return tmp;
    }

    /// <summary>
    /// Apply the carve to an existing label. Split out of <see cref="Create"/> because a font can
    /// arrive LATE — <c>NativeButtonSkin</c> harvests the HUD font off a live widget and has none
    /// until one exists — and a restyle has to run again once it does, exactly as the keycap
    /// labels' does.
    /// </summary>
    internal static void Restyle(TMP_Text label, ControlBoard style)
    {
        if (label == null)
            return;
        int row = Row(style);
        label.color = GrooveFloor[row];
        if (label.font == null)
            return; // no font yet -> no material instance to style; the caller restyles later
        Material mat = label.fontMaterial; // per-label instance, never the shared asset
        if (mat == null)
            return;
        if (mat.HasProperty("_OutlineColor"))
            mat.SetColor("_OutlineColor", Keyline[row]);
        if (mat.HasProperty("_OutlineWidth"))
        {
            mat.SetFloat("_OutlineWidth", KeylineWidth);
            mat.EnableKeyword("OUTLINE_ON"); // mobile TMP variants gate the outline pass on this
        }
        if (mat.HasProperty("_UnderlayColor"))
        {
            Color lip = LitLip[row];
            lip.a = LipAlpha;
            mat.SetColor("_UnderlayColor", lip);
            if (mat.HasProperty("_UnderlaySoftness")) mat.SetFloat("_UnderlaySoftness", LipSoftness);
            if (mat.HasProperty("_UnderlayOffsetX")) mat.SetFloat("_UnderlayOffsetX", LipOffsetX);
            if (mat.HasProperty("_UnderlayOffsetY")) mat.SetFloat("_UnderlayOffsetY", LipOffsetY);
            mat.EnableKeyword("UNDERLAY_ON");
        }
        if (!_logged)
        {
            _logged = true;
            VRLog.Info("Cards", $"BOARD ENGRAVING ({style}): fill {GrooveFloor[row]} (the groove " +
                $"floor — that board's own material in shadow), keyline {Keyline[row]} at width " +
                $"{KeylineWidth:F2} (the shaded wall of the cut), lit lip {LitLip[row]} at alpha " +
                $"{LipAlpha:F2} offset ({LipOffsetX:F2}, {LipOffsetY:F2}) — DOWN and LEFT, which is " +
                "where BoardLit's baked key (0.35, 0.85, -0.45: above, toward the viewer, slightly " +
                "right) actually falls on the walls of an incision. This is the keycap label's " +
                "recipe inverted: a cap label sits proud and drops its shadow down-RIGHT, a board " +
                "label is cut in and catches its light down-LEFT. Seated " +
                $"{-ProudLocalZ * 1000f:F1} mm proud of the board face in board-local units — enough " +
                "that it is never coplanar, small enough that it reads as flush.");
        }
    }

    /// <summary>Change-gated text write. TMP rewrites re-trigger auto-size layout (the badge
    /// flicker lesson, test #13), and these labels are refreshed from per-tick state.</summary>
    internal static void SetText(TMP_Text? label, string text)
    {
        if (label != null && label.text != text)
            label.text = text;
    }
}
