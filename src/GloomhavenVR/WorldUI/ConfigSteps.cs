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
/// number MEANS — degrees step in degrees, metres in millimetres, a scale factor in percent — and
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
/// bare slider NEVER READS A STEP — it just gets handed the two ends — and <c>BuildRow</c> gave
/// every bounded scalar a bare slider, so <b>16 of the 23 judgements below were dead code</b>:
/// written down, argued for, and never executed. The settings audit found it while answering the
/// user's question (d) about control shapes, and the bar+arrows row builder
/// (<c>VROptionsTab.2.Rows.BuildBarAndArrowsRow</c>) is what turned them on: those rows carry
/// arrows as well as a bar, and the arrows step by exactly what is written here — the bar snaps to
/// the same grid, so the two inputs speak one unit. Since ModBuild 271 there is no bare slider
/// left in the menu at all, so this file's answers now reach every bounded row there is.</para>
///
/// <para>THIRTEEN OF THE SIXTEEN ARE LIVE NOW; the other three are not, and neither is a bug:
/// <c>Rig/WorldTiltDegrees</c> has no row at all (the feature is parked, the step is kept for its
/// revival), and <c>Net/MaskId</c> and <c>Comfort/SnapTurnDegrees</c> are named-preset DROPDOWNS —
/// a dropdown reads no step either, and for both of them that is the correct control (a mask is an
/// enumeration; six snap angles are a list, not a continuum). <c>SnapTurnDegrees</c> is the one the
/// audit expected the new builder to revive; its 15° argument is now the argument for the FIVE
/// ENTRIES IN THE DROPDOWN rather than for a press, which is the same judgement arriving by a
/// better road. Keep the line: it is what documents where those five angles come from.</para>
///
/// <para>=====================================================================================
/// <b>THE RULE (ModBuild 271, and the thing to check a NEW dial against).</b> A step must be
/// small enough to LAND on the answer and large enough to REACH it:</para>
///
/// <para><b>One press is at most a QUARTER of the dial's own scale and at least a
/// TWO-HUNDRED-AND-FIFTIETH of it — where the dial's own scale is its declared range if it has
/// one, and otherwise the largest magnitude in its FAMILY (every component of a vector, every
/// board of a per-board family, every axis of a pose), never one component's own value.</b>
/// Integral dials are exempt (1 is the grid an integer has) and so is a step written down in
/// <see cref="Explicit"/> (that is where a human overrules this on purpose, with the argument
/// beside it).</para>
///
/// <para>WHY THE FAMILY AND NOT THE VALUE, in one line: a near-zero axis is where somebody put
/// the origin, not a statement about resolution. See <see cref="UnitScope"/> — the enum is the
/// long form of the same sentence, and <see cref="FamilyOf"/> is the mechanism that now carries
/// it for every scope at once.</para>
///
/// <para>WHAT THE RULE CAUGHT, on the day it was written: 24 shipped dials over the cap. Worst
/// first, as a multiple of the cap: <c>[Cards] HeldForward</c> (8x — one press was TWICE the whole
/// value), the three <c>[Cards] SlotOverlaySpacing_{board}</c> (5x — one press was five times the
/// Steel value), the six <c>[Hands] {Plate,Arcane}{Lateral,Vertical,Forward}Offset</c> (4.4x), and
/// down through <c>[Cards] ConfirmUndoInsetX</c>, <c>RestButtonInsetX</c>,
/// <c>[BoardButtons] Depth</c>, the three <c>SlotOverlayOffset_{board}</c> he actually wrote in
/// about, and eight more. And what the rule did NOT catch, which is the other half of the story:
/// <b>the metre itself was the wrong unit.</b> The rule alone would have taken SlotOverlayOffset
/// from 5 mm to 2 mm and still missed two of the five values he holds.</para>
///
/// <para>=====================================================================================
/// <b>THE USER REPORT (2026-08, verbatim).</b> <i>"In den VR Einstellungen pro board die Slider
/// mach sie zu diesen hybriden slidern, so dass man sie besser einstellen kann. Weiterhin
/// überdenke für jede Einstellung nochmal die Schrittweite. Aktuell kann ich die Kartenoverlay
/// positionen nicht präzise genug einstellen, da die Schrittweite zu hoch ist, und ich den
/// optimalen Punkt so immer überspringe."</i></para>
///
/// <para>THE MEASUREMENT THAT ANSWERED IT. He names <c>[Cards] SlotOverlayOffset_{board}</c>, a
/// Vector3. Take every non-zero component of every Vector2/Vector3 dial this mod ships — 63 of
/// them, all hand-tuned board-local geometry — and ask what grid they sit on:</para>
///
/// <list type="bullet">
/// <item>a 0.01 press (the old metre rule) reaches 27 of 63</item>
/// <item>a 0.005 press (what a Vector actually got) reaches 38 of 63</item>
/// <item>a 0.002 press reaches 49 of 63</item>
/// <item><b>a 0.001 press reaches 63 of 63</b>, and 0.0005 reaches no more</item>
/// </list>
///
/// <para>So the grid he tunes this mod's geometry on is exactly ONE MILLIMETRE, measured off his
/// own shipped values, and more than a third of them were values his arrows could not produce —
/// which is "ich überspringe den optimalen Punkt immer", stated as a number. The length rows in
/// <see cref="Units"/> therefore step 0.001 and not 0.01. A CENTIMETRE WAS NEVER THIS PROJECT'S
/// UNIT: of 152 length-worded dials, 57 ship at 5 cm or less and 18 at 1 cm or less — the mod is
/// furniture on a 0.64 x 0.32 m board, not architecture.</para>
///
/// <para>AND THE ARROWS ARE FREE TO BE FINE NOW, which is what makes the millimetre affordable.
/// Two things pay for it. Every bounded row carries a BAR beside its arrows since
/// <c>VROptionsTab.2.Rows</c> retired its allow-list, so the coarse travel is a drag and the
/// arrows only ever do the last millimetre — the argument <c>[WorldUI] ScreenParallaxScale</c>
/// below already makes for one entry, applied to all of them. And every arrow repeats when held,
/// so a press count in the hundreds is a hold of a couple of seconds rather than a hundred
/// presses. The <c>scale/250</c> floor is what keeps that honest: it is the widest press count
/// the rule will hand out, and exactly one dial in the mod exceeds it — ScreenParallaxScale, on
/// purpose, with its reason written down.</para>
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
        // [Cards] CardWidth HAD A WRITTEN-DOWN 0.005 HERE and it is gone, because the reason it was
        // written down has become the rule. Its argument was "the shipped width is 6.35 cm, so the
        // metre rule's full centimetre would cross a sixth of the card in one press" — i.e. it was
        // an exception carved out to escape a unit that was wrong for this whole project. The unit
        // is a millimetre now and the derivation answers 0.001 on its own (range 0.03-0.15, so
        // neither bound binds), which is finer than the exception was and is the resolution a card
        // actually wants: a real poker card is 63.5 mm and this dial exists to match one.
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
        // LENGTHS, IN METRES — A MILLIMETRE A PRESS, and it used to be a centimetre.
        //
        // THE CENTIMETRE WAS NEVER MEASURED, it was assumed, and the user's 2026-08 report is what
        // falsified it: "Aktuell kann ich die Kartenoverlay positionen nicht präzise genug
        // einstellen … ich überspringe den optimalen Punkt immer". The measurement is in the file
        // header — of the 63 non-zero components his Vector2/Vector3 dials ship, a 0.01 press can
        // land on 27 and a 0.001 press on all 63, with nothing finer buying anything. One
        // millimetre is not a guess about feel; it is the grid he has already tuned this mod on,
        // read back off his own values.
        //
        // IT IS ALSO THE RIGHT SCALE FOR THE OBJECTS. 57 of the 152 length-worded dials ship at
        // 5 cm or less and 18 at 1 cm or less: control-board keycaps, card insets, slot glows, a
        // 6.35 cm card. A centimetre press crossed a sixth of a card — [Cards] CardWidth had
        // already been written down as an Explicit exception for exactly that reason, and this
        // makes the exception the rule.
        //
        // NOTHING LARGE GOT FINER BY ACCIDENT. THE RULE's scale/250 floor coarsens the millimetre
        // straight back up wherever the dial is genuinely big: [Cards] BoardMaxWidthMeters steps
        // 0.02 on its 3.8 m range and [WorldUI] SharedWindowArcRadiusMeters stays at 0.01. The
        // millimetre only survives where the thing being tuned is millimetre-sized.
        //
        // ORDER STILL MATTERS as much as it did: this block must stay BELOW "Millimeters", which
        // ends in "Meters" and would otherwise take a 40 mm dial for a 40 m one.
        ("Meters", 0.001d), ("Offset", 0.001d), ("Radius", 0.001d), ("Width", 0.001d),
        ("Height", 0.001d), ("Depth", 0.001d), ("Thickness", 0.001d), ("Diameter", 0.001d),
        ("Distance", 0.001d), ("Inset", 0.001d), ("Margin", 0.001d), ("Padding", 0.001d),
        ("Forward", 0.001d), ("Right", 0.001d), ("Up", 0.001d), ("Down", 0.001d), ("Left", 0.001d),
        // …and the length words this table was missing, every one of them a dial that was
        // stepping off its own default's magnitude instead. Each is an unambiguous METRE in this
        // project — there is no second reading of any of them anywhere in the config:
        //   Spacing   — [Cards] SlotOverlaySpacing_Steel ships 0.002 and stepped 0.05 MILLIMETRES,
        //               was raised to 0.01 by the fix that added this row, and 0.01 is FIVE TIMES
        //               the whole value — the same dial overshot in both directions before the
        //               family scale below could bound it. It steps 0.001 now.
        //   Travel    — a keycap's press sink, [BoardButtons] Travel stepped 0.1 mm
        //   Gap       — [Cards] DecisionGap_<board>, the gap between the decision cards
        //   Clearance — [MixedReality] UnseenRimTopClearance stepped 0.5 mm
        //   Size      — [RoundButtons] CapSize, a cap radius of 42 mm that stepped 1 mm
        ("Spacing", 0.001d), ("Travel", 0.001d), ("Gap", 0.001d), ("Clearance", 0.001d),
        ("Size", 0.001d),
        // rates
        ("Speed", 1d), ("Rate", 1d),
    };

    /// <summary>A step written down for this exact entry, or false.</summary>
    internal static bool TryExplicit(string section, string key, out double step) =>
        Explicit.TryGetValue(section + "/" + key, out step);

    /// <summary>
    /// What KIND of number the key names — a quantity in its own right, one board of a per-board
    /// family, or one axis of a pose.
    ///
    /// <para>WHY IT EXISTS AT ALL. The step is bounded by the entry's own numbers, and that bound
    /// is right for a lone scalar and WRONG for a coordinate. That difference is the whole of the
    /// third bug report in this family. <c>[WristHud] GloveOffsetX</c> shipped -0.003 and
    /// <c>GloveOffsetY</c> -0.053: same offset, same hand, same unit, two magnitudes an order of
    /// magnitude apart, because ONE AXIS HAPPENED TO BE NEAR ITS ORIGIN. A near-zero axis is
    /// centred, not finely tuned, and deriving a resolution from it is deriving a resolution from
    /// where somebody put the origin. Two dials of one vector must move by the same amount or the
    /// coarse one reads as the only one that works.</para>
    ///
    /// <para>IT IS NO LONGER THE MECHANISM, and that is worth being clear about, because the first
    /// fix REMOVED the bound for Component and half-removed it for Variant — and that is how
    /// <c>[Cards] SlotOverlaySpacing_Steel</c> came to ship a step five times its own value with
    /// nothing left to stop it. <see cref="FamilyOf"/> carries the same judgement the right way
    /// round: the bound is read from the FAMILY's largest magnitude, so it applies to every scope
    /// and is identical across the members of one vector by construction. This enum survives
    /// because <see cref="TryUnit"/>'s answer to "what kind of number is this" is worth stating and
    /// worth asserting — the wire suite pins it for four dozen key shapes — and because it is the
    /// long-form explanation of why the family exists.</para>
    /// </summary>
    internal enum UnitScope
    {
        /// <summary>
        /// A lone scalar, and its family is itself — so its own scale is what bounds it, which is
        /// the case the two bounds were written for: <c>[Perf] SummaryIntervalSeconds</c> needs
        /// coarsening and <c>[Cards] FanFollowDeadzone</c> needs the cap.
        /// </summary>
        Value,

        /// <summary>
        /// One of a per-variant family (<c>…_Oak</c> / <c>…_Steel</c>). The magnitude THAT board
        /// happens to ship is that board's geometry, not a statement about the dial's resolution:
        /// deriving from it gave one key three boards and three different feels (0.05 mm on Steel,
        /// 0.2 mm on Oak). The family spans the boards, so all three now take one number.
        /// </summary>
        Variant,

        /// <summary>
        /// One component of a pose — an axis of an offset, or a euler angle. Its own magnitude says
        /// nothing at all: it is where the origin was put. The family spans the axes, so every axis
        /// of an offset in this mod moves a millimetre and every euler angle a degree.
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

    // ==============================================================================================
    //  THE RULE, as code
    // ==============================================================================================

    /// <summary>Presses to cross the dial's own scale at the COARSEST step the rule allows.</summary>
    internal const double CoarsestPresses = 4d;

    /// <summary>Presses to cross the dial's own scale at the FINEST step the rule allows.</summary>
    internal const double FinestPresses = 250d;

    /// <summary>
    /// How far one ◀ / ▶ press moves an entry — the whole of THE RULE, in one Unity-free place.
    ///
    /// <para>It lives here rather than in <see cref="ConfigCatalog"/> so that the wire suite runs
    /// the REAL resolver over every shipped key instead of a copy of it. The one thing the caller
    /// still decides is <paramref name="scale"/>, because only the catalog can see a declared range
    /// and only the catalog can see the whole family at once — see
    /// <c>ConfigCatalog.ResolveSteps</c>.</para>
    ///
    /// <para>THE LADDER, in three falling steps:</para>
    /// <list type="number">
    /// <item><description>A step WRITTEN DOWN for this entry (<see cref="Explicit"/>) — every
    /// curated everyday row, where the right step is a judgement about the setting. Returned as
    /// written and exempt from everything below, because that is what "written down"
    /// means.</description></item>
    /// <item><description>The unit named in the key — degrees step in degrees, metres in
    /// MILLIMETRES. This is what replaced "a hundredth of the default's magnitude", which had no
    /// answer at all for the twenty-eight entries whose default is 0 and gave the WORLD TILT a step
    /// of 0.01°.</description></item>
    /// <item><description>With no unit word: a fiftieth of the dial's scale — the last resort, and
    /// the only one that can serve a depth-buffer epsilon of 0.0002.</description></item>
    /// </list>
    ///
    /// <para>WHAT EACH BOUND IS FOR, with the entry that needed it. The CAP stops gross overshoot:
    /// <c>[Cards] FanFollowDeadzone</c> is 0.004 and "Deadzone" would have stepped it by 0.05,
    /// twelve times the whole value, so one press could only overshoot. The FLOOR stops a dial
    /// nobody can cross: <c>[Perf] SummaryIntervalSeconds</c> sits at 30 s on a 595 s range and
    /// "Seconds" would step it in twentieths of a second. Both used to read the entry's OWN
    /// magnitude, which is why they had to be switched off for coordinates and per-board variants —
    /// and switching them off is how <c>[Cards] SlotOverlaySpacing_Steel</c> came to step five times
    /// its own value. The family gives both bounds back to every scope at once, and gives them back
    /// IDENTICAL across the members of one vector, which is the invariant the reports were about.
    /// <see cref="UnitScope"/> is still the long-form explanation of why; it is no longer the
    /// mechanism.</para>
    ///
    /// <para>THE FLOOR IS 1/250 AND NOT 1/50 because the arrows no longer have to cross the range
    /// on their own: every bounded row carries a bar beside them and every arrow repeats when held
    /// (<c>VROptionsTab.2.Rows</c>). A fiftieth would have coarsened the millimetre straight back
    /// off every furniture dial larger than 5 cm, which is most of them.</para>
    ///
    /// <para>A snap NEVER breaks a bound: the cap is snapped DOWN and the floor UP, because
    /// <see cref="NiceStep"/> rounds to the nearest 1/2/5 and a cap that rounds up is not a
    /// cap.</para>
    /// </summary>
    /// <param name="scale">
    /// The dial's own scale: its declared range's width, or — with no range — the largest magnitude
    /// in its <see cref="FamilyOf"/> family. Zero where the whole family ships at 0, which is not a
    /// statement about resolution and therefore bounds nothing.
    /// </param>
    internal static double Resolve(string section, string key, double scale, bool integral)
    {
        // A WRITTEN-DOWN STEP IS RETURNED AS WRITTEN. It is neither snapped nor bounded: snap-turn's
        // 15° is deliberately off the 1/2/5 grid, and NiceStep rounded it to 20° — turning the one
        // value in the table chosen for what players actually want into one nobody asked for. It is
        // also where a human deliberately overrules THE RULE: [WorldUI] ScreenParallaxScale is the
        // one dial in the mod that needs more than 250 presses to cross its range, on purpose, with
        // the reason written beside it.
        if (TryExplicit(section, key, out double step))
            return step;

        if (!TryUnit(key, out step, out _))
            step = scale > 0d ? scale / 50d : (integral ? 1d : 0.01d);

        step = NiceStep(step, integral);

        if (scale > 0d && !double.IsNaN(scale) && !double.IsInfinity(scale))
        {
            double coarsest = scale / CoarsestPresses;
            if (step > coarsest)
                step = NiceStepAtMost(coarsest, integral);
            double finest = scale / FinestPresses;
            if (step < finest)
                step = NiceStepAtLeast(finest, integral);
        }

        return step;
    }

    /// <summary>Round a raw step to 1/2/5 x 10^k so the readout lands on round numbers.</summary>
    private static double NiceStep(double raw, bool integral)
    {
        if (integral)
            return Math.Max(1d, Math.Round(raw));
        if (raw <= 0d || double.IsNaN(raw) || double.IsInfinity(raw))
            return 0.01d;
        double exp = Math.Floor(Math.Log10(raw));
        double pow = Math.Pow(10d, exp);
        double m = raw / pow;
        double snapped = m < 1.5d ? 1d : m < 3.5d ? 2d : m < 7.5d ? 5d : 10d;
        // Floor at a millionth, not a thousandth: the old floor was five times LARGER than
        // [HexHighlight] StableDepthBias's whole value (0.0002), so its stepper could only ever
        // overshoot. Nothing a player meets is anywhere near this small.
        return Math.Max(0.000001d, snapped * pow);
    }

    /// <summary>
    /// The largest 1/2/5 x 10^k step that is NOT ABOVE <paramref name="limit"/> — the snap for a
    /// cap.
    ///
    /// <para><see cref="NiceStep"/> rounds to the NEAREST, which quietly breaks the bound that
    /// asked for it: a cap of 0.0037 came back as 0.005 and a cap of 0.0375 as 0.05, so the entry
    /// kept exactly the step the cap existed to take away from it. A cap that rounds up is not a
    /// cap.</para>
    /// </summary>
    private static double NiceStepAtMost(double limit, bool integral)
    {
        if (integral)
            return Math.Max(1d, Math.Floor(limit));
        if (limit <= 0d || double.IsNaN(limit) || double.IsInfinity(limit))
            return 0.000001d;
        double exp = Math.Floor(Math.Log10(limit));
        double pow = Math.Pow(10d, exp);
        double m = limit / pow;
        double snapped = m >= 5d ? 5d : m >= 2d ? 2d : 1d;
        return Math.Max(0.000001d, snapped * pow);
    }

    /// <summary>
    /// The smallest 1/2/5 x 10^k step that is NOT BELOW <paramref name="limit"/> — the snap for a
    /// floor, and the mirror of <see cref="NiceStepAtMost"/> for the same reason.
    /// </summary>
    private static double NiceStepAtLeast(double limit, bool integral)
    {
        if (integral)
            return Math.Max(1d, Math.Ceiling(limit));
        if (limit <= 0d || double.IsNaN(limit) || double.IsInfinity(limit))
            return 0.000001d;
        double exp = Math.Floor(Math.Log10(limit));
        double pow = Math.Pow(10d, exp);
        double m = limit / pow;
        double snapped = m <= 1d ? 1d : m <= 2d ? 2d : m <= 5d ? 5d : 10d;
        return Math.Max(0.000001d, snapped * pow);
    }

    // ==============================================================================================
    //  THE FAMILY — whose magnitude is allowed to say how finely this dial wants to move
    // ==============================================================================================

    /// <summary>
    /// Words that spell one axis of a pose where the axis is NOT a trailing decoration, grouped as
    /// the triples they come in. The first member of each triple names the family, so all three
    /// land in one bucket whatever order the config file binds them in.
    ///
    /// <para>The plain trailing cases (<c>OffsetX</c>, <c>HeldOffsetUp</c>) are already handled by
    /// <see cref="Decorations"/>; these are the ones where the direction is spelled as a word and
    /// sits in the middle (<c>GloveLateralOffset</c>) or IS the whole suffix and is also a unit
    /// (<c>GlovePalmPitch</c> — "Pitch" is an angle AND an axis, so the suffix test cannot separate
    /// the family for it).</para>
    ///
    /// <para>Ordered longest-first WITHIN the intent: <c>OffsetSide</c> is tried before a bare
    /// <c>Side</c> so <c>HeldOffsetSide</c> groups on the offset rather than on the word. This is
    /// the same table <c>ConfigStepVectors.Families</c> sweeps the shipped defaults with, and that
    /// is deliberate — the guard and the resolver must agree on what "one vector" means or the
    /// guard is checking a different question than the one the menu answers.</para>
    /// </summary>
    private static readonly string[][] Triples =
    {
        new[] { "PitchDegrees", "YawDegrees", "RollDegrees" },
        new[] { "RotPitch", "RotYaw", "RotRoll" },
        new[] { "LateralOffset", "VerticalOffset", "ForwardOffset" },
        // The arm HUD's pose, and the direct descendant of the report this whole mechanism exists
        // for: [WristHud] {style}OffsetX/Y/Z were RENAMED to {style}Palm{Side,Lift,Finger}Offset, so
        // the axis stopped being a letter and became a word that is not a direction word. Three
        // axes of one plate, one of which (GlovePalmSideOffset) ships at exactly 0 — the same
        // "one axis sits at its origin" shape as GloveOffsetX, arriving under new names.
        new[] { "SideOffset", "LiftOffset", "FingerOffset" },
        new[] { "OffsetSide", "OffsetUp", "OffsetForward" },
        new[] { "SideMeters", "DownMeters", "ForwardMeters" },
        new[] { "Pitch", "Yaw", "Roll" },
        new[] { "Side", "Up", "Forward" },
        new[] { "Left", "Right" },
        new[] { "X", "Y", "Z" },
    };

    /// <summary>
    /// The name of the group whose largest magnitude is this dial's SCALE — every component of one
    /// vector, every board of one per-board family, every axis of one pose, under one name.
    ///
    /// <para>WHY THIS EXISTS AND WHAT IT REPLACED. Three separate user reports, all the same
    /// sentence ("der X-Offset hat keinen Einfluss"), all the same cause: a step derived from the
    /// magnitude of ONE COMPONENT. <c>[WristHud] GloveOffsetX</c> ships −0.003 and
    /// <c>GloveOffsetY</c> −0.053 — same offset, same hand, same unit — and the only thing that
    /// differs is that X happens to sit near its origin. A near-zero axis is CENTRED, not finely
    /// tuned, and deriving a resolution from it is deriving a resolution from where somebody put
    /// the origin.</para>
    ///
    /// <para>The first fix was <see cref="UnitScope"/>: a Component's magnitude bounds NOTHING and a
    /// Variant's may only coarsen. That was right about the disease and wrong about the cure — it
    /// removed the bound instead of fixing WHOSE magnitude it reads, so the coordinate and per-board
    /// dials came out of the fix with no upper bound at all. That is how
    /// <c>[Cards] SlotOverlaySpacing_Steel</c> shipped a step FIVE TIMES its own value, and how a
    /// Vector3 offset never got a derived step in the first place. Reading the FAMILY's magnitude
    /// gives every scope the same bound and still never asks one axis about its own resolution —
    /// the two dials of one vector are bounded by the same number BY CONSTRUCTION, which is the
    /// invariant the reports were about.</para>
    ///
    /// <para>The variant tag comes off first (it is the outermost decoration), then a Min/Max
    /// qualifier, then the axis — as a triple member, else as a trailing decoration, else as a
    /// direction hump in the middle. Every strip is speculative in the same sense
    /// <see cref="TryUnit"/>'s are: it is kept only where it leaves something a unit word can still
    /// be read off, so a key that merely CONTAINS a direction is left in its own family. Menu-time
    /// only, once per entry at catalog build — never per frame.</para>
    /// </summary>
    internal static string FamilyOf(string section, string key)
    {
        int tag = key.LastIndexOf('_');
        string stem = tag > 0 ? key.Substring(0, tag) : key;

        // A clamp end is the thing it clamps: BoardPitchMin_Oak and BoardPitchMax_Oak are one pitch.
        for (int i = 0; i < Qualifiers.Length; i++)
        {
            string q = Qualifiers[i];
            if (stem.Length > q.Length && stem.EndsWith(q, StringComparison.Ordinal)
                && TryMatch(stem.Substring(0, stem.Length - q.Length), out _))
            {
                stem = stem.Substring(0, stem.Length - q.Length);
                break;
            }
        }

        for (int t = 0; t < Triples.Length; t++)
        {
            string[] triple = Triples[t];
            for (int m = 0; m < triple.Length; m++)
            {
                string member = triple[m];
                if (stem.Length > member.Length && stem.EndsWith(member, StringComparison.Ordinal))
                    return section + "/" + stem.Substring(0, stem.Length - member.Length)
                           + "<" + triple[0] + ">";
            }
        }

        // A trailing decoration (an axis letter or a direction) with a unit word behind it.
        for (int i = 0; i < Decorations.Length; i++)
        {
            string d = Decorations[i];
            if (stem.Length > d.Length && stem.EndsWith(d, StringComparison.Ordinal)
                && TryMatch(stem.Substring(0, stem.Length - d.Length), out _))
                return section + "/" + stem.Substring(0, stem.Length - d.Length) + "<axis>";
        }

        return section + "/" + stem;
    }
}
