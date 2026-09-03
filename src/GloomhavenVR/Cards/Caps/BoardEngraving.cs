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
    ///
    /// <para><b>THE CUT WAS DEEPENED (user request 5, 2026-09-03): "Gewährleiste, dass der Text
    /// lesbar ist AUF DEN BOARDS, indem du die Schriftfarbe entsprechend wählst. Es soll immersiv
    /// sein weiterhin und gut aussehen, aber lesbar sein!"</b> The multipliers moved; the derivation
    /// did not. groove 0.45 → 0.13, lit lip 1.35 → 1.60, keyline 0.22 → 0.07 (and
    /// <see cref="KeylineWidth"/> 0.09 → 0.14, <see cref="LipAlpha"/> 0.85 → 1.00). Re-derive with
    /// <c>unity/board-prep/buttons/cap_check.py --palette</c>, whose printed multipliers are updated
    /// to match.</para>
    ///
    /// <para><b>MEASURED, OFF HIS OWN SCREENSHOT, BEFORE ANY OF IT WAS CHANGED.</b> Sampling the
    /// three engraved captions in text-board.jpg (darkest tenth = the groove, middle third = the
    /// stone, brightest tenth = the lip):</para>
    /// <list type="bullet">
    /// <item>"LANGE RAST." groove 1.64:1 against its own stone, lip 1.50:1, and the carve's own
    /// dark-to-light step across a stroke only 2.46:1.</item>
    /// <item>"RUNDE 2" 1.96:1 / 1.83:1, step 3.59:1.</item>
    /// <item>"FIXIERT" 1.68:1 / 1.99:1, step 3.34:1.</item>
    /// </list>
    /// <para>Every one of those is under the 3:1 floor for large text. That is what "der Text
    /// untergeht" IS on the board itself, and it is a much larger effect than anything measured on
    /// the floating labels beside it (whose median is 17:1).</para>
    ///
    /// <para><b>WHY THE FIX IS A DEEPER CUT AND NOT A BRIGHTER FILL.</b> Glyph-vs-stone has a
    /// CEILING here that no colour can beat: the board renders at L≈0.11 in this scene, so even a
    /// perfectly black groove tops out at (0.11+0.05)/0.05 = 3.2:1. Making the fill LIGHTER instead
    /// would invert the carve into raised lettering, which is the one thing the user has ruled out
    /// twice ("nativ und immersiv in dem board verarbeitet"). What is NOT capped is the carve's own
    /// internal step — the groove floor against the lit lip on the other side of the same stroke —
    /// because that spans the whole range rather than one side of the stone. So the groove goes
    /// DEEP (a real incision's floor is near-black in ambient occlusion, which is the physically
    /// honest value and not a legibility cheat), the lip goes brighter (a deeper chamfer catches
    /// more of the same key), and the shaded wall gets real area at HUD size. Computed on the same
    /// scene factor the measurement above establishes (0.826, rendered stone L over albedo L):</para>
    /// <list type="bullet">
    /// <item>groove-vs-stone: Oak 2.49 → <b>3.63:1</b>, Steel 2.20 → <b>2.99:1</b>, Bronze
    /// 2.19 → <b>2.97:1</b>.</item>
    /// <item>the carve's internal step: Oak 3.66 → <b>8.49:1</b>, Steel 3.14 → <b>6.62:1</b>,
    /// Bronze 3.12 → <b>6.55:1</b> — 2.1x to 2.3x.</item>
    /// </list>
    /// <para>Nothing was added behind the glyphs. The class rule below still holds.</para>
    /// </summary>
    private static readonly Color[] GrooveFloor =
    {
        new(0.075f, 0.056f, 0.037f, 1f), // Oak    — deep shadowed heartwood (face × 0.13)
        new(0.054f, 0.051f, 0.050f, 1f), // Steel  — the dark of a struck groove in brushed steel
        new(0.058f, 0.051f, 0.034f, 1f), // Bronze — shadowed metal under the patina
    };

    private static readonly Color[] LitLip =
    {
        new(0.925f, 0.683f, 0.459f, 1f), // face × 1.60 — the chamfer of a deeper cut catching the
        new(0.669f, 0.634f, 0.619f, 1f), // same baked key over more of its length. Oak's red channel
        new(0.715f, 0.629f, 0.419f, 1f), // is the binding one at 0.925 and is deliberately under 1.
    };

    private static readonly Color[] Keyline =
    {
        new(0.040f, 0.030f, 0.020f, 1f), // face × 0.07 — the shaded wall, darker than the floor it
        new(0.029f, 0.028f, 0.027f, 1f), // runs down to, which is what a wall in shadow is
        new(0.031f, 0.028f, 0.018f, 1f),
    };

    /// <summary>Width of the shaded keyline as a fraction of the SDF spread. Still the wall of a cut
    /// seen almost edge-on rather than a drawn border — but 0.09 gave that wall almost no AREA at
    /// the angular size a board caption is actually read at, so the dark half of the carve was
    /// carried by the fill alone. 0.14 is the widest that does not start closing the counters of
    /// 'e' and 'a' at the solved caption font size these labels land on (36, band 0.045..0.111).</summary>
    private const float KeylineWidth = 0.14f;

    /// <summary>How far the lit lip peeks out from under the glyph, in SDF units. Negative X and Y
    /// = down and LEFT, i.e. the two walls the baked key actually falls on (see the class doc).</summary>
    private const float LipOffsetX = -0.28f;
    private const float LipOffsetY = -0.28f;

    /// <summary>How soft the lit lip is. Softer than the cap labels' drop shadow (0.35): a lip is a
    /// chamfer catching light along its length, not a shadow thrown onto a surface behind.</summary>
    private const float LipSoftness = 0.14f;

    /// <summary>The lit lip's opacity. It WAS 0.85 "because the lip is a narrow face at a glancing
    /// angle to the key, not a fully-lit surface" — true of the shallow cut this started as, and the
    /// 0.85 was then multiplying a lip that already measured only 1.42–1.47:1 against its own stone.
    /// A deeper cut turns its chamfer further INTO the key rather than further away from it, so 1.0
    /// is the same physical argument at the new depth, not an exception to it.</summary>
    private const float LipAlpha = 1.00f;

    // ---- WHERE THE CAPTIONS SIT — one home, two boards ---------------------------------------
    //
    // These live HERE and not at their call sites because the owner's board and every peer's
    // mirror of it both place the same three captions, and a caption that is 4 mm higher on a
    // teammate's board than on their own is exactly the 1:1 divergence this project keeps paying
    // for. The alternative — an authored constant on each side plus an entry in
    // scripts/check-mirrors.sh — is the arrangement that file's own header keeps recommending
    // against when the value can simply be shared. Nothing here is tunable and nothing is on the
    // wire: both sides derive the same number from the same board.

    /// <summary>
    /// HOW FAR PROUD OF ITS OWN ANCHOR A REST CAPTION IS SEATED — and the whole of the
    /// invisible-caption defect, as one number.
    ///
    /// <para><b>WHAT WENT WRONG.</b> Every engraving used to be seated <see cref="ProudLocalZ"/> —
    /// 0.8 mm — in front of its PARENT, and that is only the right answer when the parent stands on
    /// the same surface the caption lands on. The rest captions' parents are the board's own
    /// <c>ShortRestToken</c> / <c>LongRestToken</c> empties, and those sit on the RECESS FLOOR of
    /// the rest pad: measured off the shipped meshes, the pad floor triangle and the anchor agree to
    /// the micron (Oak −0.00940, Steel −0.01090, Bronze −0.01100 on the boards' own thickness axis),
    /// which is what a seat anchor is FOR — the disc stands in the recess. The caption does not: it
    /// is moved 46–53 mm along the short axis, OUT of the recess, onto the panel surface around it,
    /// and that panel stands 3.6 mm (Oak) / 3.8 mm (Steel) / 4.0 mm (Bronze) PROUDER than the floor
    /// the caption's depth was measured from. Seated 0.8 mm in front of the floor, the caption ended
    /// up 2.8–3.2 mm INSIDE the opaque board. A TMP mesh draws in the transparent queue with ZWrite
    /// off and ZTest LEqual, so it is decided by one comparison against the board's own depth and
    /// loses it completely: not dim, not clipped — not drawn at all, which is exactly what the user
    /// saw and what no state probe could have shown.</para>
    ///
    /// <para><b>WHY IT IS ONE NUMBER AND NOT THREE.</b> 4.8 mm clears the deepest of the three
    /// recesses and leaves 0.8–1.2 mm of flush proudness on every board — the same span
    /// <see cref="ProudLocalZ"/> was chosen for. A per-board constant would buy 0.4 mm of nothing and
    /// would be a third place the owner's board and every peer's mirror of it could disagree.</para>
    /// </summary>
    internal const float RestCaptionProudLocalZ = -0.0048f;

    /// <summary>
    /// The same correction for the FOLLOW/PIN caption, which had the same defect from the other end.
    ///
    /// <para>Its anchor is mod-built (<c>PlayTray.NewAnchor</c>) and seated on the nominal proud
    /// plane at −5 mm, which would be in front of the board's FACE — but the caption is lifted
    /// <see cref="PinCaptionLiftY"/> back up onto the board's bottom margin, and that margin is the
    /// raised DECORATIVE BORDER, which stands 5.6 mm (Oak) / 8.0 mm (Steel) / 2.8 mm (Bronze) proud
    /// of the face. The caption was therefore 4.7–7.4 mm inside the border frame. 9.0 mm clears the
    /// worst of them with the same flush margin.</para>
    /// </summary>
    internal const float PinCaptionProudLocalZ = -0.0090f;

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
                                       TextAlignmentOptions align = TextAlignmentOptions.Center,
                                       float proudZ = ProudLocalZ)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = new Vector3(localPos.x, localPos.y, proudZ);
        // A FRESH GameObject IS ON LAYER 0 AND REPARENTING DOES NOT CHANGE THAT. PlayTray runs
        // Core.VRLayers.Apply over the tray root exactly once, at board build, so an engraving built
        // by a later rebuild — RestControls.EnsureBuilt and RebuildDashboardButtons both destroy and
        // re-create theirs — sat on the Default layer while every cap beside it sat on the mod
        // layer. BoardButton.Create carries the same line for the same reason. It is NOT what made
        // these captions invisible (the head camera's mask is 0xFFFFFFFF in scenario, and the wall
        // fade's own census reports them "stays visible"), but a board part on the world's layer is
        // offered to every sweep that classifies world geometry, and that is not a property to leave
        // to luck.
        Core.VRLayers.Apply(go);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = string.Empty;
        tmp.alignment = align;
        // STATED, NOT ASSUMED. A TMP whose isRightToLeftText is true lays its glyphs out along
        // local -X, which on a board caption is indistinguishable at a glance from a mirrored
        // transform and is one of the two things that could produce the reversed FIXIERT caption in
        // text-board.jpg. It defaults to false, so this line changes nothing today — it exists so
        // that the NEXT reader of LogFacing's output can rule the RTL half out from the log instead
        // of from a memory of what TMP's default is.
        tmp.isRightToLeftText = false;
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
            // HW-VERIFY
            // TIER PROMOTED, TEXT UNTOUCHED (2026-09-03). This line already named every number the
            // legibility round changed — the groove floor, the keyline and its width, the lit lip
            // and its alpha — and it was at Info, which the shipped log level drops. So the one line
            // that says what the carve actually got was invisible in exactly the report it would
            // have answered. Promoting the tier is the sanctioned move; the words are the words.
            VRLog.Note("Cards", $"BOARD ENGRAVING ({style}): fill {GrooveFloor[row]} (the groove " +
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

    /// <summary>
    /// EVERY NUMBER THAT DECIDES WHETHER A CAPTION IS ON THE BOARD OR IN IT — once per engraving,
    /// per board build, and LOUDLY when the answer is "in it".
    ///
    /// <para><b>WHY THIS EXISTS.</b> Three engravings shipped completely invisible and every
    /// instrument in the mod agreed they were fine: they were <c>SetActive(true)</c>, their material
    /// recipe had run, the atlas gate that creates them had passed, their facing diagnostic said
    /// "FACES the player", and the wall-fade census listed them as drawing. Not one of those
    /// measured the only thing that was wrong — how deep they sat against the surface they were cut
    /// into. A depth-rejected transparent mesh is not dim and not clipped, it is ABSENT, so there is
    /// nothing for a state probe to find. This line states the depth in the same frame the seat
    /// constants are written in, beside the board's own proudest surface, so the comparison is on
    /// the page instead of in somebody's head.</para>
    ///
    /// <para><b>THE VERDICT IS HONEST ABOUT WHAT IT CANNOT SEE.</b> <paramref name="boardFrontZ"/>
    /// is the PROUDEST point of the whole board mesh — its raised border — not the surface under
    /// this particular caption, which the mod cannot sample without a readable mesh. A caption over
    /// a RECESSED field legitimately sits behind it. So the warning fires on the margin actually
    /// being negative, and says which of the two readings applies rather than asserting one.</para>
    /// </summary>
    /// <param name="boardLocalZ">The caption's depth in the BOARD's own face frame — negative is
    /// toward the player. Board-root local metres.</param>
    /// <param name="anchorLocalZ">The same for the anchor it hangs from, so a caption that inherited
    /// a recess floor says so.</param>
    /// <param name="boardFrontZ">The board visual's proudest surface in the same frame, measured
    /// from its own mesh bounds, or NaN when no board mesh could be measured.</param>
    internal static void LogSeat(TMP_Text? label, string what, ControlBoard style,
                                 float boardLocalZ, float anchorLocalZ, float boardFrontZ)
    {
        if (label == null)
        {
            VRLog.Warn("Cards", $"BOARD ENGRAVING SEAT — {what} ({style}): the caption does not " +
                "exist. Either its board supplies no anchor for it, or the keycap-atlas gate that " +
                "creates it was false at build time and nothing re-runs that gate.");
            return;
        }
        var mr = label.GetComponent<MeshRenderer>();
        Material? mat = mr != null ? mr.sharedMaterial : null;
        float margin = boardFrontZ - boardLocalZ; // negative when the board's front is prouder
        string depth = float.IsNaN(boardFrontZ)
            ? "board front NOT MEASURED (no readable board mesh) — the depth verdict is unavailable"
            : $"board front {boardFrontZ * 1000f:F2} mm ⇒ margin {margin * 1000f:+0.00;-0.00} mm " +
              (margin >= 0f
                  ? "(in front of the board's PROUDEST surface — it cannot be buried anywhere)"
                  : "(BEHIND the board's proudest surface; over a RECESSED field that is legitimate, " +
                    "past the board's own relief it is buried and draws nothing)");
        string line = $"BOARD ENGRAVING SEAT — {what} ({style}): text '{label.text}' at world " +
            $"{label.transform.position}, layer {label.gameObject.layer}, " +
            $"depth {boardLocalZ * 1000f:F2} mm (anchor {anchorLocalZ * 1000f:F2} mm, so this " +
            $"caption is {(anchorLocalZ - boardLocalZ) * 1000f:F2} mm prouder than the thing it " +
            $"hangs from), {depth}. Renderer " +
            (mr == null
                ? "MISSING — nothing can draw this caption"
                : $"enabled={mr.enabled} sortingOrder={mr.sortingOrder} " +
                  $"queue={(mat != null ? mat.renderQueue.ToString() : "<no material>")} " +
                  $"shader='{(mat != null && mat.shader != null ? mat.shader.name : "<none>")}'") +
            $", solved font {label.fontSize:F4} (auto {label.enableAutoSizing}, band " +
            $"{label.fontSizeMin:F3}..{label.fontSizeMax:F3}), rect {label.rectTransform.sizeDelta}.";
        // THE FAILURE CASE IS THE ONE THAT HAS TO BE FINDABLE. A seat that is fine is an Info line
        // nobody needs to grep for; a seat behind the board, a missing renderer or a collapsed font
        // is the thing the next report will be about, so it is a Warn.
        bool bad = mr == null || !mr.enabled || mat == null
                   || (!float.IsNaN(boardFrontZ) && margin < 0f)
                   || label.fontSize <= 0f;
        if (bad)
            VRLog.Warn("Cards", line);
        else
            VRLog.Info("Cards", line);
    }

    /// <summary>Captions whose facing has already been reported (see <see cref="SeatFacing"/>).
    /// Every one of these is destroyed and rebuilt on a board switch and on any ButtonTuning edit,
    /// so an ungated line would be hundreds of copies of an answer that cannot change between
    /// them — the same argument RestControls makes for its own seat log.</summary>
    private static readonly System.Collections.Generic.HashSet<string> _facingLogged = new();

    /// <summary>
    /// SEAT A CAPTION AGAINST THE BOARD'S OWN FACE FRAME, AND SAY WHICH WAY IT ENDED UP FACING.
    ///
    /// <para><b>THE DEFECT THIS EXISTS FOR.</b> In text-board.jpg the FIXIERT caption on the board's
    /// bottom frame is drawn MIRRORED — it reads "TЯƎIXIꟻ". "LANGE RAST." and "RUNDE 2" on the SAME
    /// board in the SAME frame read correctly, so it is not a whole-board handedness flip. Confirmed
    /// numerically rather than by eye: correlating each caption's column ink profile against a
    /// rendered reference and against that reference mirrored, the two known-good captions prefer
    /// the un-mirrored reference (LANGE RAST 48/48 windows, RUNDE 2 39/48) and FIXIERT prefers the
    /// MIRRORED one in 40 of 40 windows. TMP's distance-field material ships Cull Off, so a
    /// back-facing or mirror-scaled label is not culled — it is simply drawn reversed.</para>
    ///
    /// <para><b>WHY THE CAUSE IS NOT IN THIS FILE'S SIGHT, AND WHY THAT MADE AN INSTRUMENT THE
    /// DELIVERABLE.</b> Every transform in the FollowEngraving's chain is shared with a caption that
    /// renders correctly: its anchor is a <c>_root</c> child carrying <c>_boardFaceFrame</c>, which
    /// is exactly what the round readout's holder carries, and the round readout is right. The
    /// existing ITEM2 diagnostic already reports that anchor as "vs faceNormal dot 1.00 → FACES the
    /// player". Nothing in the board path multiplies a negative scale or a 180° roll. So a static
    /// read cannot name the culprit, and shipping a guess would be the fifth round of that on this
    /// board. What was missing was a MEASUREMENT of the rendered basis — nothing anywhere logged the
    /// handedness of a caption's world matrix or which of its faces the eye is on.</para>
    ///
    /// <para><b>WHAT THIS DOES, AND IT IS NOT GATED ON THE FINDING.</b> It writes the caption's
    /// WORLD rotation from the board's own frame, unconditionally, for every caption — so a parent
    /// chain that has picked up a roll anywhere is overwritten by the one frame the board defines,
    /// whether or not anybody has proved that is what happened. It then measures the result: the
    /// determinant of the world basis (negative = mirrored by scale, which a rotation write cannot
    /// undo, so that case is corrected on the caption's own localScale.x and said out loud), the
    /// world direction the glyph advance actually points, and which face the eye is on. The line
    /// prints at a tier the shipped log carries, on every caption, pass or fail — a remedy that only
    /// speaks when it fires is a remedy whose silence means nothing.</para>
    /// </summary>
    /// <param name="boardFaceWorld">The board's own face frame in WORLD space — for the local board
    /// <c>_root.rotation * _boardFaceFrame</c>, i.e. the exact rotation the bundle's own anchors
    /// were aligned to.</param>
    internal static void SeatFacing(TMP_Text? label, string what, Quaternion boardFaceWorld)
    {
        if (label == null)
            return;
        Camera? head = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        bool eyeKnown = head != null;
        Vector3 eye = eyeKnown ? head!.transform.position : Vector3.zero;
        Transform t = label.transform;

        // THE ROLL, OVERWRITTEN BY CONSTRUCTION. Position is untouched — Transform.rotation moves
        // nothing — so a caption whose chain was already correct is written the value it already
        // had and nothing about the shipped picture changes.
        Quaternion before = t.rotation;
        t.rotation = boardFaceWorld;

        // THE MIRROR, WHICH A ROTATION CANNOT FIX. Transform.rotation is a quaternion and knows
        // nothing about a negative scale anywhere above; the basis the mesh is actually drawn with
        // does. Read it off the matrix, which is the thing the GPU gets.
        Matrix4x4 m = t.localToWorldMatrix;
        Vector3 bx = m.MultiplyVector(Vector3.right);
        Vector3 by = m.MultiplyVector(Vector3.up);
        Vector3 bz = m.MultiplyVector(Vector3.forward);
        float det = Vector3.Dot(Vector3.Cross(bx, by), bz);
        bool mirrored = det < 0f;
        if (mirrored)
        {
            Vector3 s = t.localScale;
            t.localScale = new Vector3(-s.x, s.y, s.z);
            m = t.localToWorldMatrix;
            bx = m.MultiplyVector(Vector3.right);
        }

        // WHICH WAY THE GLYPHS ADVANCE, IN THE WORLD. TMP lays a left-to-right line along local +X,
        // so this vector IS the direction the reader's eye travels along the word — and comparing it
        // to the board's own in-plane right is the whole question "is this word reversed on the
        // board", asked of the matrix rather than of the hierarchy.
        Vector3 boardRight = boardFaceWorld * Vector3.right;
        float advance = Vector3.Dot(bx.normalized, boardRight);

        // WHICH FACE THE EYE IS ON. A TMP is readable from its -Z side, so the eye is on the correct
        // side when the label's world forward points AWAY from the eye.
        Vector3 fwd = (boardFaceWorld * Vector3.forward).normalized;
        float eyeSide = eyeKnown ? Vector3.Dot(fwd, (t.position - eye).normalized) : float.NaN;

        if (!_facingLogged.Add(what))
            return;
        float rolled = Quaternion.Angle(before, boardFaceWorld);
        string verdict = mirrored
            ? "MIRRORED BY SCALE — corrected on this caption's own localScale.x"
            : advance < 0f
                ? "ADVANCE REVERSED even after the frame write, which the frame write cannot explain"
                : rolled > 1f
                    ? $"ROLLED {rolled:F1}° out of the board frame and re-seated"
                    : "correct as built — nothing was corrected";
        // HW-VERIFY
        VRLog.Note("Cards", $"ENGRAVING FACING — {what}: {verdict}. World-basis determinant " +
            $"{det:F4} (negative = mirrored; a rotation write cannot undo that, a localScale flip " +
            $"can). Glyph advance · board right = {advance:+0.000;-0.000} (want ≈ +1; −1 is the " +
            $"word running backwards). Rotation was {rolled:F2}° off the board face frame before " +
            "this write. Label forward · (label − eye) = " +
            (eyeKnown ? $"{eyeSide:+0.000;-0.000} (want > 0: a TMP is readable from its −Z side, " +
                        "so a NEGATIVE value is the player looking at the BACK of the caption, " +
                        "which TMP's Cull Off draws as a mirror image)"
                      : "eye unknown (no camera this frame)") +
            $", isRightToLeftText={label.isRightToLeftText} (true would reverse the run without " +
            "touching a single transform). THIS IS THE LINE THAT DECIDES THE REVERSED 'FIXIERT' " +
            "CAPTION: compare this caption's four numbers against LongRestEngraving's and RoundText's " +
            "in the same log — three of them render correctly, so whichever term differs is the cause.");
    }

    /// <summary>Change-gated text write. TMP rewrites re-trigger auto-size layout (the badge
    /// flicker lesson, test #13), and these labels are refreshed from per-tick state.</summary>
    internal static void SetText(TMP_Text? label, string text)
    {
        // Engravings wear game strings too (GUI_LONG_REST, upper-cased by the caller) and are
        // bare TMP labels with no sprite asset — the same seam rule as the keycaps
        // (RichTextTags): a tag the label cannot render is stripped, never printed as text.
        // The strip is a same-instance no-op on a tag-free string, so the change gate below
        // stays allocation-free.
        string raw = text;
        text = RichTextTags.Strip(raw, out int tags);
        if (tags > 0 && label != null)
            WorldUI.NativeButtonSkin.LogStrippedTags(raw, text, tags, label.name);
        if (label != null && label.text != text)
            label.text = text;
    }
}
