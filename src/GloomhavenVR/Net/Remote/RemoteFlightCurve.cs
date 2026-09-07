using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE ONE CURVE EVERY MIRRORED CARD FLIGHT FLIES, and it is the owner's own — this type holds no
/// maths of its own, it CALLS <see cref="VRCard.SmootherStep"/> and <see cref="VRCard.FlyArcOffset"/>.
///
/// <para>WHY IT EXISTS (2026-09-07 sharing ruling, the maintainer's question in his own words:
/// "Warum haben wir so viel redundanz? Wir bauen Dinge nach die an anderer Stelle schon
/// funktionieren"). Until this build there were FOUR flight curves in the mod and three of them were
/// a DIFFERENT CURVE from the one the owner watches. <c>VRCard.FlyToPile</c> eases with SMOOTHERSTEP
/// (<c>6t⁵−15t⁴+10t³</c>, C² at both ends) and bows with <c>FlyArcOffset</c> ON THAT EASED s;
/// <c>RemoteCardFx</c>, <c>RemoteBurnFx</c> and <c>RemoteBrowserFan</c> each wrote out plain
/// SMOOTHSTEP (<c>t²(3−2t)</c>, C¹) along the chord and bowed with <c>sin(πt)</c> on the RAW t. Same
/// duration, same peak height, different path: at t = 0.25 the owner is 10.4 % along the chord at
/// 37.1 % of peak lift and the mirror was 15.6 % along at 70.7 %. The vertical separation is
/// therefore 0.336 x Arc, at both ends of every flight — the ModBuild 476 session's own
/// <c>Remote card FX</c> lines report arcs of 2.09 to 3.30 m, so between 0.70 m and 1.11 m of
/// daylight between the card its owner watched and the card everyone else did. The 1:1 ruling names
/// ANIMATION and POSITION explicitly.</para>
///
/// <para>IT SURVIVED EIGHT ROUNDS BECAUSE NO INSTRUMENT COULD SEE IT. Every arc line on either
/// machine printed the PEAK height, and the peak is identical by construction — both curves are
/// symmetric and both reach <c>Arc</c> at t = 0.5. Two logs agreeing on the only number either one
/// printed is not agreement about the path. <see cref="Describe"/> is the fix for that: it names the
/// easing FUNCTION and samples the curve away from the midpoint, where the two shapes differ.</para>
///
/// <para>DIRECTION OF DEPENDENCY: this is in <c>Net/</c> and calls DOWN into <c>Cards/</c>, never the
/// other way. <c>Cards/</c> must not learn that a mirror exists — a mirror that CALLS the owner's
/// curve is still "the mirror in one place"; it merely stops carrying a second copy, which makes the
/// 1:1 rule easier to check, not harder. A second implementation of either half now fails
/// <c>scripts/check-mirrors.sh</c> (group "mirrored flight ease" / "mirrored flight bow").</para>
///
/// <para>WHAT IS DELIBERATELY NOT HERE: the drawn object. Every caller keeps its own slab, its own
/// pool, its own lifetime and its own face gate. This type is pure math over floats and Vector3s and
/// touches no <c>GameObject</c> — see the sharing ruling's "NEVER SHARE THE OBJECT".</para>
/// </summary>
internal static class RemoteFlightCurve
{
    /// <summary>
    /// The flight's eased parameter: <c>VRCard.SmootherStep</c>, the identical call
    /// <c>VRCard.FlyToPile</c>'s own tick makes. ONE eased term drives the chord, the bow AND the
    /// scale ramp on both machines — the owner's tick states the reason ("no ease-out slide
    /// fighting a linear bow — that mismatch was the 'choppy'/lopsided look"), and a mirror that
    /// eased one of the three differently would be a second animation rather than a replay.
    /// </summary>
    internal static float Ease(float t) => VRCard.SmootherStep(t);

    /// <summary>
    /// World position of a mirrored flight at eased progress <paramref name="eased"/> (from
    /// <see cref="Ease"/>): the chord plus <c>VRCard.FlyArcOffset</c>'s parabolic lift, on the SAME
    /// eased term. <paramref name="arcUp"/> is WORLD up at every caller — <c>VRCard.FlyToPile</c>
    /// deliberately throws its caller's board-up away so a tilted board can never lean the arch
    /// sideways, and it stays a parameter here only so the caller keeps saying so out loud.
    /// </summary>
    internal static Vector3 Pose(float eased, Vector3 from, Vector3 to, Vector3 arcUp, float arc) =>
        Vector3.Lerp(from, to, eased) + VRCard.FlyArcOffset(eased, arcUp, arc);

    /// <summary>The point away from the midpoint that <see cref="Describe"/> samples. Anything but
    /// 0.5: the two curves this consolidation collapsed AGREE at 0.5 (both symmetric, both at the
    /// spatial midpoint and peak lift there), so a midpoint sample is exactly the reading that
    /// cannot tell them apart.</summary>
    private const float SampleT = 0.25f;

    /// <summary>
    /// THE CURVE, IN NUMBERS, for a flight's own log line — the reading that was missing for eight
    /// rounds. Computed from <see cref="Ease"/> and <c>VRCard.FlyArcOffset</c> themselves, never
    /// from copied literals, so a future retune of the owner's ease changes this line with it rather
    /// than leaving it asserting a shape the code no longer flies.
    /// </summary>
    internal static string Describe(float arc)
    {
        float s = Ease(SampleT);
        // FlyArcOffset's own lift, read back as a fraction of the peak, so this line cannot claim a
        // bow shape the shared function does not actually produce.
        float lift = arc > 1e-6f
            ? VRCard.FlyArcOffset(s, Vector3.up, arc).y / arc
            : 0f;
        return $"CURVE = VRCard.SmootherStep (6t^5-15t^4+10t^3) along the chord AND on the "
             + $"VRCard.FlyArcOffset bow (4s(1-s)) AND on the scale ramp — one eased term, the "
             + $"identical pair VRCard.FlyToPile flies on the owner's machine. SAMPLE at "
             + $"t={SampleT:F2}: {s * 100f:F1}% along the chord at {lift * 100f:F1}% of the "
             + $"{arc:F3} m peak. COMPARE those two numbers, never the peak: the peak is identical "
             + "under every symmetric ease and is why eight rounds of matching arc readings said "
             + "nothing. The mirrors before this build read 15.6% / 70.7% here (smoothstep + a "
             + "raw-t sine bow): 0.336 x arc of vertical separation from the owner, i.e. "
             + $"{0.336f * arc:F2} m on this flight.";
    }
}
