using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// THE AXIS-SEPARATION RULE for the turn stick: does this push mean TURN, or does it mean
/// vertical LIFT? One pure function plus the four numbers it is made of, so the whole design
/// decision of <see cref="Flight"/>'s vertical lift sits in one place and can be driven vector by
/// vector outside Unity (<c>tests/GloomhavenVR.WireTests/LiftWedgeVectors.cs</c>).
///
/// <para><b>WHY IT IS A FILE OF ITS OWN.</b> Same reason <see cref="ScrollTurnGate"/> is: it
/// decides something that is only ever observed by FEEL, from inside a headset, one thumb push at
/// a time. A wedge that is one comparison too loose does not throw, does not log and does not look
/// wrong — it produces a player whose world yaws every time they try to rise, or a control that
/// silently refuses to work for anyone whose thumb is not perfectly vertical. Neither is
/// reachable from any other test in this repository. So this is written free of Unity beyond
/// <c>Mathf</c>, free of config and free of the hand model, precisely so it can be linked into the
/// test assembly.</para>
///
/// <para><b>USER REQUEST</b> (2026-08-15, verbatim): "Ich will es auch Optional einstelbar machen,
/// dass in der Hand mit der man dreht auch beim Joystick hoch und runter entsprechend nach oben
/// und unten fährt mit der Fluggeschwindigkeit." The turn stick's SIDEWAYS axis already turns, so
/// putting travel on its FORWARD axis means every push that is not perfectly cardinal has to be
/// resolved into "turn", "lift" or "both".</para>
///
/// <para><b>ONE HALF OF THE ANSWER IS FIXED BEFORE THE DESIGN STARTS: TURNING MAY NEVER BE
/// BLOCKED</b> (standing user ruling, hardware ModBuild 138). So this rule can only ever decide
/// when LIFT stands down. It never suppresses a degree of yaw, <see cref="SnapTurn"/> does not
/// call it, and <see cref="SnapTurn"/> does not even know it exists.</para>
///
/// <para><b>THE RULE: A DOMINANT-AXIS WEDGE WITH HYSTERESIS.</b></para>
/// <code>
///   engage   |y| >= 0.50  AND  |y| >= 1.50·|x|      (≈ 56.3° above horizontal)
///   sustain  |y| >= 0.35  AND  |y| >= 1.20·|x|      (≈ 50.2° above horizontal)
/// </code>
///
/// <para><b>AT 45° NOTHING LIFTS, by construction and in both directions of travel:</b> |y| == |x|
/// fails the sustain ratio as well as the engage ratio, so a diagonal push is a PURE TURN whether
/// the thumb arrived there from the side or from the top. That is the safe way round — turning is
/// the older, load-bearing control, and the ambiguous case has to fall to it.</para>
///
/// <para><b>WHY A RATIO AND NOT A PER-AXIS THRESHOLD.</b> The obvious alternative is "lift only
/// while |x| is below turning's own deadzone (0.2)", which separates the axes perfectly. It was
/// rejected for the reason <see cref="Flight"/> already gives about its own deadzone: a thumb that
/// pushes "up" on a real stick routinely carries 0.2–0.3 of sideways with it, so that rule gives a
/// control that works for a careful thumb and is a DEAD BUTTON for everyone else — and a dead
/// button is the failure mode this project keeps paying hardware rounds for. A ratio scales with
/// how hard the stick is pushed and is slop-proof.</para>
///
/// <para><b>WHAT THE HYSTERESIS IS FOR.</b> Without it a held push sitting near the wedge edge
/// flickers between lifting and not, once per frame. Engage is the stricter test, sustain the
/// looser one, so the wedge is ~6° wider once you are inside it and a held climb cannot chatter.
/// </para>
///
/// <para><b>THE TWO TURN MODES.</b></para>
/// <list type="bullet">
/// <item><b>SNAP</b> — a snap can never fire on the same push that lifts, and that is ARITHMETIC
/// rather than a rule. Snap engages at |x| ≥ 0.70; lift engages at |y| ≥ 1.50·|x|, i.e. |y| ≥ 1.05
/// at that deflection, which a stick whose gate is a circle (|x|² + |y|² ≤ 1) cannot produce. Even
/// the looser sustain wedge needs |y| ≥ 0.84 beside |x| = 0.70, a magnitude of 1.09. So under Snap
/// the two are physically exclusive, and <c>LiftWedgeVectors</c> asserts exactly that.</item>
/// <item><b>SMOOTH</b> — both are continuous and CAN run together, in the band 0.20 &lt; |x| ≤ 0.55
/// where the ratio still lets lift in. That overlap is deliberate and is a gentle helix, not a
/// fight: at the worst point the wedge allows (|x| = 0.55, |y| = 0.83) smooth turn is at 44 % of
/// its own response (~40 °/s of the shipped 90) while lift is at 55 % of the flight speed.
/// Suppressing the turn there is not an option the ruling leaves open, and suppressing the LIFT
/// there would put the dead-button failure back.</item>
/// </list>
///
/// <para><b>THE DEADZONE IS BIGGER THAN TURNING'S ON PURPOSE</b> (0.50 to start, against Smooth's
/// 0.20). Uncommanded vertical motion is the nausea risk in this feature — the player's own body
/// says they are not moving and the horizon says they are climbing — so lifting has to be
/// something the thumb DID, not something it drifted into. Turning may be twitchy; height may
/// not.</para>
/// </summary>
internal static class LiftWedge
{
    /// <summary>Deflection the forward axis must reach to START lifting.</summary>
    internal const float EngageDeflection = 0.5f;

    /// <summary>Deflection a lift already under way is kept alive down to (hysteresis). Also the
    /// base <see cref="Flight"/> measures its response curve from, so engaging is a soft ~5 % of
    /// full speed rather than a step.</summary>
    internal const float SustainDeflection = 0.35f;

    /// <summary>How much more vertical than sideways a push must be to START lifting (≈56°).</summary>
    internal const float EngageRatio = 1.5f;

    /// <summary>…and to KEEP lifting (≈50°). Must never be ≤ 1, or 45° would lift.</summary>
    internal const float SustainRatio = 1.2f;

    /// <summary>
    /// Should the turn stick's forward axis be read as vertical lift this frame?
    /// </summary>
    /// <param name="x">Stick sideways deflection, −1…+1 (turning's axis; only its magnitude matters).</param>
    /// <param name="y">Stick forward deflection, −1…+1 (the lift axis; only its magnitude matters).</param>
    /// <param name="lifting">Whether a lift was already running last frame — the hysteresis input.</param>
    internal static bool Evaluate(float x, float y, bool lifting)
    {
        float ax = Mathf.Abs(x);
        float ay = Mathf.Abs(y);
        return lifting
            ? ay >= SustainDeflection && ay >= SustainRatio * ax
            : ay >= EngageDeflection && ay >= EngageRatio * ax;
    }

    /// <summary>The deflection currently required, for the diagnostic line only.</summary>
    internal static float Deflection(bool lifting) => lifting ? SustainDeflection : EngageDeflection;

    /// <summary>The ratio currently required, for the diagnostic line only.</summary>
    internal static float Ratio(bool lifting) => lifting ? SustainRatio : EngageRatio;
}
