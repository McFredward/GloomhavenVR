using System;
using System.Collections.Generic;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// How far one press of a numeric row's ◀ / ▶ moves the value.
///
/// <para>WHY THIS FILE EXISTS. The step used to be derived from the value alone: a fiftieth of the
/// declared range, or — with no range — a hundredth of the DEFAULT's magnitude. The first half is
/// fine. The second is not, and it fails hardest exactly where it shows: a default of 0 has no
/// magnitude, so the derivation collapsed to its 0.01 fallback and the WORLD TILT stepped in
/// hundredths of a degree. Six thousand presses to cross its useful range. The same happened to the
/// world scale, and to twenty-six other entries whose default is 0.</para>
///
/// <para>THE UNIT IS THE ANSWER, NOT THE MAGNITUDE. What a sensible step is follows from what the
/// number MEANS — degrees step in degrees, metres in centimetres, a scale factor in percent — and
/// the meaning is not in the value. It is in the name: this project spells its units out
/// (<c>WorldTiltDegrees</c>, <c>RecenterHoldSeconds</c>, <c>HandForwardOffset</c>), so the name is
/// the most reliable thing available short of writing every entry down.</para>
///
/// <para>SO: written down where it is a judgement, derived where it is a pattern. Every row of the
/// curated everyday tabs is in <see cref="Explicit"/>, chosen one at a time — those are the ones a
/// player actually touches, and "sensible" there is a decision, not a formula. The other ~130
/// numeric entries, nearly all of them Debug, fall to the unit rule and then to the old
/// range/magnitude derivation, which is still the right answer when a range is declared.</para>
/// </summary>
internal static class ConfigSteps
{
    /// <summary>
    /// One press of ◀ / ▶ for the curated rows, decided individually.
    ///
    /// <para>Each of these is a judgement about the SETTING, which is why it is written down rather
    /// than computed. Snap turning steps 15° because 15/30/45/60/90 are the angles anyone actually
    /// wants; the world tilt steps 5° because its own description warns to increase it in small
    /// steps; scale factors step 5 % because that is a visible but not jarring change.</para>
    /// </summary>
    private static readonly Dictionary<string, double> Explicit = new(StringComparer.Ordinal)
    {
        // ---- The retired "Komfort ▸ Tisch & Welt" block -------------------------------------
        // Rig/WorldScale is GONE (2026-08: the "Tischgröße" setting was removed, legacy no-op)
        // and so is Comfort/TableHeightOffset (2026-08: "Tischhöhe" removed — free locomotion
        // replaced it, see Rig/ComfortSettings.cs). Only the parked tilt keeps a step here.
        ["Rig/WorldTiltDegrees"] = 5d,           // PARKED feature — kept for its revival; row gone

        // ---- Komfort ▸ Bewegung & Drehen --------------------------------------------------
        ["Comfort/SnapTurnDegrees"] = 15d,       // the angles anyone wants: 15/30/45/60/90
        ["Comfort/SmoothTurnSpeed"] = 10d,       // degrees per second, 30..270
        ["Comfort/RecenterHoldSeconds"] = 0.1d,  // a tenth of a second is the felt unit

        // ---- Komfort ▸ Hände & Zielen -----------------------------------------------------
        ["Hands/ModalRayConeDegrees"] = 5d,      // an aiming cone, 0..~45

        // ---- Tafeln ▸ Karten & Brett ------------------------------------------------------
        ["Cards/TrayScale"] = 0.05d,             // 5 % of the board size
        ["Cards/InspectScale"] = 0.05d,          // 5 % of the close-up size

        // ---- Avatar ▸ Aussehen ------------------------------------------------------------
        ["Hands/GloveScale"] = 0.05d,
        ["Hands/PlateScale"] = 0.05d,
        ["Hands/ArcaneScale"] = 0.05d,

        // ---- Multiplayer ▸ Was andere von dir sehen ---------------------------------------
        ["Net/MaskId"] = 1d,                     // an index into the mask library
        ["Net/MaskSize"] = 0.05d,                // 5 % of your mask size
    };

    /// <summary>
    /// Unit words and the step that unit deserves, for everything not written down above.
    ///
    /// <para>Ordered, first match wins, so the specific words come before the general ones. Matched
    /// as a SUFFIX — of the key with its DECORATIONS stripped, see <see cref="TryUnit"/>; never as a
    /// substring, because a substring test would read <c>ScaleFallback</c> (a bool-ish policy) or
    /// <c>InitiativeDepthMaxSpreadPx</c> as something they are not.</para>
    ///
    /// <para>Deliberately absent: "Bias". <c>HeldFaceBias</c> is an angle and <c>StableDepthBias</c>
    /// is a depth-buffer epsilon of 0.0002 — one word, two units three orders of magnitude apart, so
    /// neither answer is safe and both are better served by the magnitude derivation.</para>
    /// </summary>
    private static readonly (string Suffix, double Step)[] Units =
    {
        // angles
        ("Degrees", 1d), ("Pitch", 1d), ("Yaw", 1d), ("Roll", 1d), ("Tilt", 1d),
        // time
        ("Seconds", 0.05d), ("Interval", 0.05d), ("Ms", 10d),
        // proportions and factors
        ("Scale", 0.05d), ("Multiplier", 0.05d), ("Fraction", 0.05d), ("Opacity", 0.05d),
        ("Alpha", 0.05d), ("Gain", 0.05d), ("Falloff", 0.05d), ("Smoothing", 0.05d),
        ("Factor", 0.05d), ("Threshold", 0.05d), ("Deadband", 0.05d), ("Deadzone", 0.05d),
        // lengths in MILLIMETRES — five of them a press. FIRST, because "Millimeters" ends in
        // "Meters" and the suffix test is first-match-wins: below the metre entry it would read a
        // 40 mm dial as a 40 m one and step it by a hundredth of a millimetre, the exact failure
        // the header of this file is about. A key spells the unit out only where the quantity is
        // genuinely sub-centimetre ([FigureGrab] PickRadiusMillimeters is a pinch, ~4 cm), which is
        // also why the step is 5 and not 10: ten presses across the useful range.
        ("Millimeters", 5d),
        // lengths, in metres — a centimetre a press
        ("Meters", 0.01d), ("Offset", 0.01d), ("Radius", 0.01d), ("Width", 0.01d),
        ("Height", 0.01d), ("Depth", 0.01d), ("Thickness", 0.01d), ("Diameter", 0.01d),
        ("Distance", 0.01d), ("Inset", 0.01d), ("Margin", 0.01d), ("Padding", 0.01d),
        ("Forward", 0.01d), ("Right", 0.01d), ("Up", 0.01d), ("Down", 0.01d), ("Left", 0.01d),
        // …and the length words this table was missing, every one of them a dial that was
        // stepping off its own default's magnitude instead. Each is an unambiguous METRE in this
        // project — there is no second reading of any of them anywhere in the config:
        //   Spacing   — [Cards] SlotOverlaySpacing_Steel shipped 0.002 and stepped 0.05 MILLIMETRES
        //   Travel    — a keycap's press sink, [BoardButtons] Travel stepped 0.1 mm
        //   Gap       — [Cards] DecisionGap_<board>, the gap between the decision cards
        //   Clearance — [MixedReality] UnseenRimTopClearance stepped 0.5 mm
        //   Size      — [RoundButtons] CapSize, a cap radius of 42 mm that stepped 1 mm
        ("Spacing", 0.01d), ("Travel", 0.01d), ("Gap", 0.01d), ("Clearance", 0.01d),
        ("Size", 0.01d),
        // rates
        ("Speed", 1d), ("Rate", 1d),
    };

    /// <summary>A step written down for this exact entry, or false.</summary>
    internal static bool TryExplicit(string section, string key, out double step) =>
        Explicit.TryGetValue(section + "/" + key, out step);

    /// <summary>
    /// What an entry's own numbers are allowed to say about a step the UNIT WORD already answered.
    ///
    /// <para>WHY THIS EXISTS AT ALL. <see cref="ConfigCatalog"/> bounds the unit step by the entry's
    /// own default — see there for the two cases that need it. That bound is right for a lone
    /// scalar and WRONG for a coordinate, and the difference is the whole of the third bug report
    /// in this family. <c>[WristHud] GloveOffsetX</c> ships -0.003 and <c>GloveOffsetY</c> -0.053:
    /// same offset, same hand, same unit, two magnitudes an order of magnitude apart, because ONE
    /// AXIS HAPPENS TO BE NEAR ITS ORIGIN. A near-zero axis is centred, not finely tuned, and
    /// deriving a resolution from it is deriving a resolution from where somebody put the origin.
    /// Two dials of one vector must move by the same amount or the coarse one reads as the only
    /// one that works.</para>
    /// </summary>
    internal enum UnitScope
    {
        /// <summary>
        /// A lone scalar. Its own default's magnitude bounds the unit step from both sides, exactly
        /// as before — <c>[Perf] SummaryIntervalSeconds</c> still needs coarsening and
        /// <c>[Cards] FanFollowDeadzone</c> still needs the cap.
        /// </summary>
        Value,

        /// <summary>
        /// One of a per-variant family (<c>…_Oak</c> / <c>…_Steel</c>). The magnitude that variant
        /// happens to ship is that BOARD's geometry, not a statement about the dial's resolution,
        /// so it may only ever make the step COARSER. Without this the same dial stepped 0.05 mm on
        /// Steel and 0.2 mm on Oak — one key, three boards, three different feels.
        /// </summary>
        Variant,

        /// <summary>
        /// One component of a pose — an axis of an offset, or a euler angle. The unit word is the
        /// whole answer and the entry's own magnitude bounds nothing (a declared RANGE still does,
        /// in <see cref="ConfigCatalog"/>: a range is a real statement of scale, a lone default is
        /// not). This is what makes every axis of every offset in the mod move a centimetre and
        /// every euler angle a degree.
        /// </summary>
        Component,
    }

    /// <summary>
    /// Words that mark a number as ONE COMPONENT OF A POSE rather than a quantity in its own right —
    /// matched as whole camel humps of the key, anywhere in it.
    ///
    /// <para>Anywhere, because the direction is not always where the unit is: <c>GloveLateralOffset</c>
    /// takes its step from the trailing "Offset" while what makes it a coordinate sits in the
    /// middle. Whole humps, because a bare substring test would read <c>Upright</c> as "Up" and
    /// <c>Downforce</c> as "Down" — the hump must END where the word ends.</para>
    ///
    /// <para>Only orientation and direction words are here. A generic "Degrees" is NOT: it says the
    /// number is an angle, not that it is one axis of an orientation, and <c>FanArcSweepDegrees</c>
    /// (a 70° sweep) or <c>RevealEnterDegrees</c> (a threshold) are single quantities whose own
    /// magnitude is a perfectly good guide.</para>
    /// </summary>
    private static readonly string[] PoseWords =
    {
        "Pitch", "Yaw", "Roll", "Tilt",
        "Lateral", "Vertical", "Forward", "Side", "Up", "Down", "Left", "Right",
    };

    /// <summary>
    /// Decorations that may sit BEHIND the unit word — an axis letter or a direction, the two ways
    /// this project spells "which component of the vector". Stripped only speculatively: the strip
    /// counts only if a unit word is actually revealed behind it, so <c>[WorldUI] HexHintSide</c>
    /// (nothing behind "Side") is left exactly where it was.
    /// </summary>
    private static readonly string[] Decorations =
    {
        "X", "Y", "Z", "Side", "Up", "Down", "Left", "Right", "Forward", "Back",
    };

    /// <summary>
    /// Qualifiers that may sit behind the unit word without being one — the two ends of a clamp.
    /// <c>BoardPitchMin_Oak</c> is a pitch; nothing else in the config ends in these words.
    /// </summary>
    private static readonly string[] Qualifiers = { "Min", "Max" };

    /// <summary>A step implied by the unit in the key's name, or false.</summary>
    /// <remarks>
    /// THE UNIT WORD IS NOT ALWAYS THE LAST WORD, and every time this file has assumed it was, a
    /// dial shipped that the user reported as having no effect:
    ///
    /// <list type="number">
    /// <item>The per-variant tag. <c>AssetPitchDegrees_Oak</c>, <c>RestButtonDiameter_Steel</c> —
    /// the asset rotation shipped stepping in HUNDREDTHS of a degree (the user pressed his way to
    /// -0.04° and correctly reported "no effect"). Fixed by stripping the tag.</item>
    /// <item>The axis letter. <c>GloveOffsetX</c> ends in "X", not in "Offset", so the unit was
    /// invisible again and the step fell out of the shipped default's magnitude: 0.05 MILLIMETRES a
    /// press, next to a sibling <c>OffsetY</c> at 1 mm. Reported as "der X-Offset hat keinen
    /// Einfluss", and it was — twenty presses moved a hair.</item>
    /// <item><c>[RoundButtons] OffsetX</c> at 1 mm beside <c>OffsetY</c> at 5 mm, the same cause,
    /// on the Skip/Fixier button group the user had already reported once.</item>
    /// </list>
    ///
    /// <para>So the test is: strip the decorations a key may carry BEHIND its unit — the variant
    /// tag, then an axis letter or a direction, then a Min/Max qualifier — and suffix-match what is
    /// left. Every strip is speculative and is kept only if a unit word actually appears behind it,
    /// which is what keeps this from degenerating into the substring test the table's own remarks
    /// refuse: a key that merely CONTAINS a unit word is still no match.</para>
    ///
    /// <para>The scope tells the caller what the key's own magnitude is worth — see
    /// <see cref="UnitScope"/>. It is the other half of the same bug: seeing the unit word on
    /// <c>GloveOffsetX</c> raises it from 0.05 mm to 0.75 mm, and only refusing to bound a
    /// coordinate by its own magnitude gets it to the centimetre its sibling moves.</para>
    /// </remarks>
    internal static bool TryUnit(string key, out double step, out UnitScope scope)
    {
        scope = UnitScope.Value;

        // The variant tag first: it is the outermost decoration, and it is the only one that says
        // something about the SCOPE even when the unit word was plainly visible without stripping
        // it (RestButtonDiameter_Steel matches "Diameter" either way, and is still a variant).
        int tag = key.LastIndexOf('_');
        string stem = tag > 0 ? key.Substring(0, tag) : key;
        bool variant = tag > 0;

        bool decorated = false;
        if (!TryMatch(stem, out step))
        {
            decorated = TryStrip(stem, Decorations, out step);
            if (!decorated && !TryStrip(stem, Qualifiers, out step))
                return false;
        }

        // An axis letter or a direction behind the unit is a coordinate by construction; otherwise
        // look for the direction/orientation word that a key like GloveLateralOffset carries in the
        // middle. Component wins over Variant: BoardPitchMin_Oak is a pitch first, per-board second.
        if (decorated || HasPoseWord(stem))
            scope = UnitScope.Component;
        else if (variant)
            scope = UnitScope.Variant;
        return true;
    }

    /// <summary>Plain suffix match against <see cref="Units"/>, first hit wins.</summary>
    private static bool TryMatch(string stem, out double step)
    {
        for (int i = 0; i < Units.Length; i++)
        {
            if (stem.EndsWith(Units[i].Suffix, StringComparison.Ordinal))
            {
                step = Units[i].Step;
                return true;
            }
        }
        step = 0d;
        return false;
    }

    /// <summary>
    /// Try each decoration as a trailing word and keep the strip ONLY if a unit word stands behind
    /// it. A strip that reveals nothing is discarded, so this can never turn a non-match into a
    /// match on evidence the plain suffix test would have rejected.
    /// </summary>
    private static bool TryStrip(string stem, string[] words, out double step)
    {
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            if (stem.Length > w.Length && stem.EndsWith(w, StringComparison.Ordinal)
                && TryMatch(stem.Substring(0, stem.Length - w.Length), out step))
                return true;
        }
        step = 0d;
        return false;
    }

    /// <summary>
    /// Does a <see cref="PoseWords"/> entry appear as a COMPLETE camel hump of the key? A hump ends
    /// where the next capital begins (or at the end of the key), which is what separates
    /// <c>HeldOffsetUp</c> from <c>UprightBias</c> and <c>SpawnDownMeters</c> from a hypothetical
    /// <c>Downforce</c>. Menu-time only, once per entry at catalog build — never per frame.
    /// </summary>
    private static bool HasPoseWord(string stem)
    {
        for (int i = 0; i < PoseWords.Length; i++)
        {
            string w = PoseWords[i];
            int at = 0;
            while ((at = stem.IndexOf(w, at, StringComparison.Ordinal)) >= 0)
            {
                int end = at + w.Length;
                if (end == stem.Length || char.IsUpper(stem[end]))
                    return true;
                at = end;
            }
        }
        return false;
    }

    /// <summary>
    /// Every key that has a written-down step — for the static check that the curated tabs and this
    /// table have not drifted apart. A curated row whose step is missing here is not an error the
    /// player would ever see as an error: the value simply steps by whatever the fallback derives,
    /// which is how the world tilt came to move in hundredths of a degree in the first place.
    /// </summary>
    internal static IEnumerable<string> ExplicitKeys => Explicit.Keys;
}
