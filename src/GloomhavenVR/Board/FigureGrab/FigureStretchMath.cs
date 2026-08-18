using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE INTERACTION VOLUME OF THE STRETCH GESTURE, as pure arithmetic.
///
/// <para>WHY IT IS ITS OWN FILE, free of hands, config, renderers and the game model: its two
/// functions decide whether a player can reach a mini they have already resized, and that verdict
/// is observed ONLY by feel, from inside a headset, one hardware round at a time. A ceiling that
/// stops growing does not throw, does not warn and does not look wrong — it produces the user
/// report this file exists to answer (2026-08-15, verbatim): "man kann vereinzelend Figuren die man
/// größer gezogen hat nicht mehr so einfach Kleiner machen weil die Area zu interagieren nicht mit
/// gewachsen ist." Written against nothing but <c>Mathf</c> so it is linked into
/// <c>tests/GloomhavenVR.WireTests</c> and driven size by size — the same argument that put
/// <c>ScrollTurnGate</c>, <c>ConfigSteps</c>, <c>EnvSoundSchedule</c> and <c>HauntFigures.Math</c>
/// on that list.</para>
///
/// <para>THE STORY OF THE BUG THESE FUNCTIONS FIX. The capture test measures from the pinch point
/// to the NEAREST POINT of the mini's visible body — min over its renderers' world AABBs — so the
/// zone follows the figure's surface as it grows, which is what "an jedem Punkt der Figur greifen"
/// asks for. That much shipped in ModBuild 137 and is correct. What did NOT scale was the SANITY
/// CEILING beside it: a renderer whose bounds implied a figure radius beyond a FIXED 0.5 m real was
/// dropped from the test as broken. A mini stretched large has a legitimately large radius, so at
/// the top of the gesture's own range the ceiling started excluding the figure's REAL renderers —
/// and when it excluded them all, the test fell back to the CENTRE distance, i.e. exactly the
/// pre-137 behaviour the fix was written to remove. The 2026-08-15 logs show it happening to the
/// main body mesh, by name: <c>MO_DeepTerror_Mesh</c> at 0.79 m real (host) and 0.82 m (peer),
/// <c>WP_Berserker_Axe</c> at 0.52–0.53 m, on figures the player was mid-gesture on. The fix is
/// therefore not a second mechanism beside the surface test — it is making the ceiling a function
/// of the size the player asked for.</para>
/// </summary>
internal static class FigureStretchMath
{
    /// <summary>The sanity ceiling on a figure's implied radius, in REAL metres at the hand, for a
    /// mini held at its board-home size at the DEFAULT diorama zoom. This is the constant the test
    /// used at every size through ModBuild 156, kept verbatim as the value at ratio 1 so nothing
    /// tightens for any size that already worked.</summary>
    internal const float CeilingAtUnitSizeRealMeters = 0.5f;

    /// <summary>The absolute ceiling on the ceiling, real metres. Bounds the growth so a broken
    /// renderer on a hugely stretched mini still cannot swallow the room, and — since the config
    /// dials can be switched off entirely (<c>[FigureGrab] StretchLimits</c>) — keeps the function
    /// bounded even when its input is not. 3 m is the far edge of a seated play space: a "figure"
    /// wider than that is not a figure.</summary>
    internal const float CeilingHardCapRealMeters = 3f;

    /// <summary>
    /// The ceiling for a hold whose TOTAL size is <paramref name="totalHeldSizeRatio"/> in
    /// default-zoom units (<c>FigureGrabbable.TotalHeldSizeRatio</c> = grab latch × manual stretch —
    /// the very product <c>[FigureGrab] StretchScaleMin/Max</c> bound).
    ///
    /// <para>MONOTONE, and never below the old constant. The <c>Max(1, …)</c> floor is the
    /// SHRINKING half of the symmetry: a mini pulled down to 0.5× must not have its renderers
    /// judged by a ceiling half as tall, because that would start excluding a figure for being
    /// SMALL — the same bug with the sign flipped. Below 1× the ceiling simply stays where it was,
    /// and the surface test carries the shrunk figure on its own (a small body plus the constant
    /// <c>[FigureGrab] StretchReachMillimeters</c> halo is a generous target).</para>
    ///
    /// <para>Degenerate input (non-finite, non-positive) reads as 1× — a bounds feature must never
    /// be the thing that breaks a gesture.</para>
    /// </summary>
    internal static float CaptureCeilingRealMeters(float totalHeldSizeRatio)
    {
        float ratio = float.IsNaN(totalHeldSizeRatio) || float.IsInfinity(totalHeldSizeRatio)
                      || totalHeldSizeRatio <= 0f
            ? 1f
            : totalHeldSizeRatio;
        return Mathf.Min(CeilingAtUnitSizeRealMeters * Mathf.Max(1f, ratio),
                         CeilingHardCapRealMeters);
    }

    /// <summary>
    /// The gesture's effective interaction radius about the mini's centre, real metres: the radius
    /// of its visible body plus the constant reach the player tunes
    /// (<c>[FigureGrab] StretchReachMillimeters</c>).
    ///
    /// <para>This is a DESCRIPTION of the volume the surface test produces, not a second test —
    /// <c>FigureStretch.CaptureDistanceReal</c> compares against the true AABB rather than a sphere
    /// (a mini is tall and thin; a sphere would push the zone half a body-height out sideways where
    /// there is nothing to point at). It exists so the volume can be REPORTED in one number in the
    /// diagnostic line and asserted in the wire tests: strictly increasing in the body radius,
    /// bounded whenever the body radius is, and — the multiplayer half — a function of nothing but
    /// its two arguments, so the owner and any mirror that asks the same question with the same
    /// numbers get the same answer.</para>
    ///
    /// <para>Negative or non-finite inputs read as 0, so the result is never negative and never
    /// NaN.</para>
    /// </summary>
    internal static float InteractionRadiusRealMeters(float bodyRadiusRealMeters,
                                                     float reachRealMeters)
        => Sane(bodyRadiusRealMeters) + Sane(reachRealMeters);

    private static float Sane(float v)
        => float.IsNaN(v) || float.IsInfinity(v) || v < 0f ? 0f : v;
}
