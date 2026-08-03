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
    /// as a SUFFIX of the key: a suffix is where this project puts its units, and a substring test
    /// would read <c>ScaleFallback</c> (a bool-ish policy) or <c>InitiativeDepthMaxSpreadPx</c> as
    /// something they are not.</para>
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
        // lengths, in metres — a centimetre a press
        ("Meters", 0.01d), ("Offset", 0.01d), ("Radius", 0.01d), ("Width", 0.01d),
        ("Height", 0.01d), ("Depth", 0.01d), ("Thickness", 0.01d), ("Diameter", 0.01d),
        ("Distance", 0.01d), ("Inset", 0.01d), ("Margin", 0.01d), ("Padding", 0.01d),
        ("Forward", 0.01d), ("Right", 0.01d), ("Up", 0.01d), ("Down", 0.01d), ("Left", 0.01d),
        // rates
        ("Speed", 1d), ("Rate", 1d),
    };

    /// <summary>A step written down for this exact entry, or false.</summary>
    internal static bool TryExplicit(string section, string key, out double step) =>
        Explicit.TryGetValue(section + "/" + key, out step);

    /// <summary>A step implied by the unit in the key's name, or false.</summary>
    /// <remarks>
    /// A per-variant key carries its variant as a TRAILING tag — <c>AssetPitchDegrees_Oak</c>,
    /// <c>RestButtonDiameter_Steel</c> — so a plain suffix test never saw the unit word on any of
    /// them and every one fell through to the 0.01 fallback. That is how the asset rotation
    /// shipped stepping in HUNDREDTHS of a degree (the user pressed his way to -0.04° and
    /// correctly reported "no effect"). The tag is stripped before the test; underscores appear
    /// nowhere else in this project's key names.
    /// </remarks>
    internal static bool TryUnit(string key, out double step)
    {
        int tag = key.LastIndexOf('_');
        if (tag > 0)
            key = key.Substring(0, tag);
        for (int i = 0; i < Units.Length; i++)
        {
            if (key.EndsWith(Units[i].Suffix, StringComparison.Ordinal))
            {
                step = Units[i].Step;
                return true;
            }
        }
        step = 0d;
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
