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
/// (<c>WorldTiltDegrees</c>, <c>RecenterHoldSeconds</c>, <c>LaserFingerOffsetMeters</c>), so the
/// name is the most reliable thing available short of writing every entry down.</para>
///
/// <para>SO: written down where it is a judgement, derived where it is a pattern. Every row of the
/// curated everyday tabs is in <see cref="Explicit"/>, chosen one at a time — those are the ones a
/// player actually touches, and "sensible" there is a decision, not a formula. The other ~130
/// numeric entries, nearly all of them Debug, fall to the unit rule and then to the old
/// range/magnitude derivation, which is still the right answer when a range is declared.</para>
///
/// <para>MOST OF THIS TABLE ONLY STARTED RUNNING ON 2026-08-22, and it is worth knowing why. A
/// slider NEVER READS A STEP — <c>BuildSliderRow</c> just hands the bar the two ends — and
/// <c>BuildRow</c> gave every bounded scalar a slider, so <b>16 of the 23 judgements below were
/// dead code</b>: written down, argued for, and never executed. The settings audit found it while
/// answering the user's question (d) about control shapes, and the bar+arrows row builder
/// (<c>VROptionsTab.2.Rows.BuildBarAndArrowsRow</c>) is what turned them on: those rows now carry
/// arrows as well as a bar, and the arrows step by exactly what is written here — the bar snaps to
/// the same grid, so the two inputs speak one unit.</para>
///
/// <para>THIRTEEN OF THE SIXTEEN ARE LIVE NOW; the other three are not, and neither is a bug:
/// <c>Rig/WorldTiltDegrees</c> has no row at all (the feature is parked, the step is kept for its
/// revival), and <c>Net/MaskId</c> and <c>Comfort/SnapTurnDegrees</c> are named-preset DROPDOWNS —
/// a dropdown reads no step either, and for both of them that is the correct control (a mask is an
/// enumeration; six snap angles are a list, not a continuum). <c>SnapTurnDegrees</c> is the one the
/// audit expected the new builder to revive; its 15° argument is now the argument for the FIVE
/// ENTRIES IN THE DROPDOWN rather than for a press, which is the same judgement arriving by a
/// better road. Keep the line: it is what documents where those five angles come from.</para>
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

        // ---- Erweitert ▸ the walk-in stand-down ------------------------------------------
        // A THRESHOLD IN METRES IS NOT AN OFFSET IN METRES. The suffix table below spells the
        // unit "Meters"; this key spells it "Metres", so it never matches and falls through to
        // magnitude stepping — 0.02 m a press, about sixty of them to walk the bar from its
        // shipped 1.20 down to 0, which is the documented escape hatch that switches the crest
        // term off entirely. Fixed HERE rather than by adding "Metres" to the suffix table,
        // because that table would then also re-step five other keys nobody has looked at.
        // 0.05 puts ~24 presses across the useful band and lands exactly on the two readings
        // the ModBuild 271 hardware log offers as candidates (0.94 m refused, 1.42 m passed).
        ["WallFade/WalkInMinCrestMetres"] = 0.05d,

        // ---- Erweitert ▸ the two sampling cadences (ModBuild 278) --------------------------
        // BOTH END IN "Seconds", SO BOTH RESOLVE TO 0.05, AND THAT IS WRONG IN OPPOSITE
        // DIRECTIONS. The unit rule is right that these are times; it cannot know that one of
        // them lives on a 0.50-15.00 range and the other on 0.00-0.25.
        //
        // The REBUILD cadence at 0.05 needs 290 presses to walk its range, and there is no
        // resolution in the answer to justify one of them: the number is "how many ~90 ms
        // frames per minute do I accept", and 2 / 3 / 4 / 6 / 8 are the answers anyone wants.
        // Half a second per press puts 29 presses across the range and lands exactly on every
        // one of them.
        ["WallFade/RescanIntervalSeconds"] = 0.5d,
        // The CHECK cadence is the opposite case: 0.05 is a QUARTER of its whole range, so the
        // dial would offer five positions and the useful ones (0.02 = 50 Hz, 0.05 = 20 Hz,
        // 0.10 = 10 Hz) are not among them beyond the middle. It also matters where the useful
        // band ends — the 0.20 s fade-in dwell stops debouncing at 0.20 — and a step that
        // cannot land near 0.10 hides the whole safe half of the range. 0.01 gives 25 presses
        // and hits every frame rate anyone would name.
        ["WallFade/EvalIntervalSeconds"] = 0.01d,
        // ModBuild 281 — the SLICE budget, range 0.25-8.0 ms against an 11.11 ms frame at 90 Hz.
        // 0.25 would give 31 presses but offers resolution the answer does not have: this number
        // is "how much of a frame may the mod take", and nobody wants 1.75 rather than 1.5. It
        // also has to be able to land on the shipped 1.5 exactly from either end, which 0.25
        // does and 0.3 does not. 31 presses across the range, every useful value on one of them.
        ["WallFade/SliceBudgetMillis"] = 0.25d,

        // ---- Komfort ▸ Drehen / Fortbewegung ----------------------------------------------
        // NOT a step any more — this row is a named-preset dropdown since 2026-08-22 (question d:
        // a six-position bar is a dropdown drawn badly). The 15 stays because it is where the five
        // offered angles come from; VROptionsTab.2.Rows.SnapTurnPresets is the list it produced.
        ["Comfort/SnapTurnDegrees"] = 15d,       // the angles anyone wants: 15/30/45/60/90
        ["Comfort/SmoothTurnSpeed"] = 10d,       // degrees per second, 30..270
        ["Comfort/RecenterHoldSeconds"] = 0.1d,  // a tenth of a second is the felt unit

        // ---- Komfort ▸ Welt greifen (2026-08 overhaul: the zoom clamp joined the gesture
        // rows it bounds — a bound must step like the value it bounds) ----------------------
        ["Comfort/ScaleMin"] = 0.05d,
        ["Comfort/ScaleMax"] = 0.05d,

        // ---- Komfort ▸ Hände & Zielen -----------------------------------------------------
        // Hands/ModalRayConeDegrees is GONE (2026-08 dead-settings sweep: the cone gate was
        // retired — VisualsAllowed is unconditionally true — and the key was deleted).
        // The grip-plateau remap is curated as an accessibility row ("Vollgriff-Hilfe"); a
        // twentieth of grip travel per press is felt but not jumpy.
        ["Hands/CurlInputFullAt"] = 0.05d,

        // ---- Grafik ▸ Darstellung (2026-08 overhaul promotions) ---------------------------
        ["RenderQuality/EyeResolutionScale"] = 0.05d, // 5 % per press — ~10 % pixel-work change
        ["RenderQuality/PixelLightCount"] = 1d,       // a light at a time (int; -1 = game's own)

        // ---- Brett & Karten ---------------------------------------------------------------
        ["Cards/TrayScale"] = 0.05d,             // 5 % of the board size
        ["Cards/InspectScale"] = 0.05d,          // 5 % of the close-up size
        // Half a centimetre per press: the shipped width is 6.35 cm, so the metre rule's
        // full centimetre would cross a sixth of the card in one press.
        ["Cards/CardWidth"] = 0.005d,
        ["WorldUI/HoverInfoScale"] = 0.05d,      // 5 % of the hover-info card size

        // ---- Tafeln ▸ 2D-Schirm (2026-08 overhaul promotions) -----------------------------
        // A 2.2 m screen at 1.6 m: centimetre steps would need two hundred presses to matter.
        ["WorldUI/ScreenWidth"] = 0.1d,
        ["WorldUI/ScreenDistance"] = 0.1d,

        // ---- Tafeln ▸ Lebensbalken ---------------------------------------------------------
        // The size is a factor, so it moves by 5 %. The unit rule already answers 0.05 for it
        // ("Scale"); it is written down because this is a curated everyday row, where the step is a
        // decision rather than a derivation.
        ["WorldUI/BarSizeScale"] = 0.05d,
        // [WorldUI] BarZoomMinScale / BarZoomMaxScale had rows here. GONE with their dials
        // (removed 2026-08-13 — the band they configured is the constant pair
        // ActorBars.ZoomFollowMin/Max). Left in place they would be DEAD rows, and the wire
        // suite's `configsteps/explicit-table-has-not-drifted` sweep says so by name: a written-down
        // step must name a key that still ships a Defaults line. Their NAMES survive one file over,
        // as pure resolver vectors in tests/GloomhavenVR.WireTests/ConfigStepVectors.cs — the
        // "Min/Max qualifier BEHIND the unit word" shape has shipped a dead dial before and must
        // keep resolving correctly for the day such a key comes back.

        // ---- Karte 3D ▸ der Reise-Knopf, den der Nutzer selbst setzt -----------------------
        // The travel-confirm button's two placement dials, written down because they became
        // STEPPERS at ModBuild 196 (user: "sollen keine Schieberegler sein, sondern die Pfeile, wo
        // man den echten Wert einfach einstellen kann" — see PrefersStepper in
        // VROptionsTab.4.Curated.cs). Until then they wore a bar and the step never mattered.
        //
        // WHY 0.01 AND NOT WHAT THE FALLBACK DERIVES. The unit rule cannot see these keys at all:
        // the unit word is "WindowHeights", plural, and the Units table matches "Height" as a
        // suffix — so both fell straight through to a fiftieth of the declared range, which is
        // 0.02 for X (range 1.0 wide) and 0.05 for Y (range 3.0 wide, snapped by NiceStep). TWO
        // DIALS OF ONE POSE MOVING BY DIFFERENT AMOUNTS is the exact fault [WristHud] GloveOffsetX
        // was reported for, and here it comes from nothing but the two clamps having different
        // widths — the unit is identical by construction (both are fractions of the SAME window
        // height, which is what their descriptions promise: "gleiche Zahl also gleiche echte
        // Strecke"). So they are written down together, at the finer of the two.
        //
        // 0.01 is the resolution the job needs. The floating quest window is ~0.4 m tall in the map
        // room, so one press is about 4 mm of button travel — small enough to settle on a placement
        // rather than straddle it, and 25 presses still cross X's whole useful span (the map edges
        // sit at about ±0.25, per the German description) instead of the 50-plus a finer step would
        // cost.
        //
        // THE ARROWS NOW REPEAT WHEN HELD, and these two rows are the only ones that do
        // (VROptionsTab.2.Rows.ArrowRepeat, wired by PrefersStepper). Until 2026-08-22 BuildArrow
        // was a plain Button.onClick, so "every press is a press and the count has to stay humane"
        // was a real constraint on the number above — and Y still needed 121 presses to cross its
        // range, X 51. The audit's question (d) offered a bar as the way out; the standing user
        // ruling forbids one here, so the hold-repeat is the answer instead. The step stays 0.01:
        // it was chosen for the RESOLUTION the job needs, not for the press count, and a held
        // arrow now crosses Y in about two seconds.
        ["WorldUI/TravelButtonOffsetXWindowHeights"] = 0.01d,
        ["WorldUI/TravelButtonOffsetYWindowHeights"] = 0.01d,

        // ---- Tafeln ▸ 2D-Schirm: the one step a DECLARED RANGE would have coarsened ---------
        // [WorldUI] ScreenParallaxScale gained an AcceptableValueRange(1, 60) on 2026-08-22 — the
        // clamp its own reader already applied, declared so the arrows stop where the effect does
        // (audit question d). That has a side effect the resolver is right about in general and
        // wrong about here: a fiftieth of a 59-wide range is 1.18, which outbids the "Scale" unit's
        // 0.05 and snaps to a whole 1 — TEN TIMES the 0.1 this dial stepped by before. It is not a
        // range-sized quantity: the shipped 6 came out of hardware test #16, the useful
        // neighbourhood around it is a couple of units wide, and 1 per press is 17 % of the tuned
        // value in one press. The bar beside the arrows is what crosses the range now
        // (PrefersBarAndArrows), so the arrows are free to stay fine. Written down at the value it
        // has always had, so declaring the clamp costs no resolution.
        ["WorldUI/ScreenParallaxScale"] = 0.1d,

        // ---- Avatar & Mehrspieler ▸ Dein Auftritt (hand size promoted, overhaul ruling 4) --
        ["Hands/GloveScale"] = 0.05d,
        ["Hands/PlateScale"] = 0.05d,
        ["Hands/ArcaneScale"] = 0.05d,

        // ---- Avatar & Mehrspieler ▸ Dein Auftritt: the mask pair ---------------------------
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
