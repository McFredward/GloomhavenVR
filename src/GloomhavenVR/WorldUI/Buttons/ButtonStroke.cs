using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE PRESS STROKE — how far into its travel a keycap is, as a function of seconds since the press
/// edge. Attack, detent, spring-back past rest, settle.
///
/// <para><b>WHAT IT REPLACES, and why that was the complaint.</b> The impulse press was one line in
/// <c>Cards.PlayTray.BoardButton.Update</c>: a 0..1 amplitude set to 1 AT THE MOMENT OF THE PRESS
/// and then decayed LINEARLY at 6/s. Read as motion, that is a cap which TELEPORTS to the bottom of
/// its travel in a single frame and then rises back at constant speed with no ease at either end —
/// the two things that make an animation read as a slide rather than as a key. The user's request
/// was blunt: <i>"Auch Drück-Animation wenn der button nach unten gedrückt wird soll gut
/// funktionieren."</i> A linear decay from an instantaneous drop has no shape to improve, so the
/// shape became a function.</para>
///
/// <para><b>ONE COPY, TWO BOARDS.</b> The old shape lived as that inline literal on the owner's cap
/// and as a named <c>PressDecayPerSecond = 6f</c> on the peer's mirror of it, linted against drift
/// by <c>scripts/check-mirrors.sh</c> because neither side could call the other. Both sides now
/// advance a PHASE IN SECONDS and ask this one function what depth that phase is — so a mirrored
/// press IS the owner's press rather than a number that has to agree with it, and the mirror group
/// was DELETED rather than re-pointed, which is the resolution that lint's own header keeps
/// recommending (the same one <c>ButtonTuning.AssemblyColor</c> and
/// <c>DecisionDockSurface.BarClearanceMeters</c> already got).</para>
///
/// <para><b>THE WIRE CONTRACT IS UNTOUCHED.</b> What crosses the wire is still the press EDGE and
/// nothing else — <c>Cards.BoardCapPress</c> publishes which cap was pressed in five bits of
/// <c>ExtIdHalfHover</c>, because at the 5 Hz extras cadence an event this short falls between
/// packets. Each side derives the shape from that edge, exactly as before. No field changes, no
/// byte moves.</para>
///
/// <para><b>IT IS FREE OF EVERYTHING BUT Mathf</b>, deliberately: that is what lets
/// <c>tests/GloomhavenVR.WireTests</c> LINK it and drive the real curve. It needs that — the first
/// version of the release leg was a decaying sine added to an ease, and the arithmetic says such a
/// sum never actually crosses rest, so the "overshoot" would have been a comment describing motion
/// that was not there. It is two explicit smoothsteps now, and the crossing, the peak and the
/// endpoint are all checked rather than hoped for.</para>
/// </summary>
internal static class ButtonStroke
{
    /// <summary>How long the cap takes to reach the bottom of its travel — the attack.</summary>
    internal const float AttackSeconds = 0.035f;

    /// <summary>How long it stays there — the detent, the moment that makes it read as a key
    /// bottoming out rather than as a cap sliding through its own rest position.</summary>
    internal const float HoldSeconds = 0.030f;

    /// <summary>How long the spring-back takes, including the overshoot and its settle.</summary>
    internal const float ReleaseSeconds = 0.120f;

    /// <summary>How far PAST rest the cap springs, as a fraction of the travel — 0.4 mm at the
    /// authored 4 mm. Small on purpose: it has to be felt, not seen as a bounce.</summary>
    internal const float ReboundFraction = 0.10f;

    /// <summary>Where in the release the cap reaches the TOP of its rebound, as a fraction of
    /// <see cref="ReleaseSeconds"/>. The first leg rides from the detent down past rest to
    /// −<see cref="ReboundFraction"/>; the second settles that back to exactly 0.</summary>
    internal const float ReboundSplit = 0.62f;

    /// <summary>Total length of one stroke. Past this the cap is at rest and the animator has
    /// nothing to do — both sides use it to disarm, so an idle cap costs one float compare.</summary>
    internal const float StrokeSeconds = AttackSeconds + HoldSeconds + ReleaseSeconds;

    /// <summary>
    /// Depth at <paramref name="seconds"/> after the press edge. 0 = at rest, 1 = the bottom of the
    /// travel, NEGATIVE = proud of rest during the rebound.
    ///
    /// <para>The rebound is why the return is signed, and callers have to keep it: combining this
    /// with the finger-follow depth through a plain <c>Mathf.Max</c> clamps every negative value
    /// away at 0 and silently deletes the overshoot — the one part of the stroke a player feels
    /// rather than sees. Both call sites guard that explicitly.</para>
    /// </summary>
    internal static float Depth01(float seconds)
    {
        if (seconds <= 0f)
            return 0f;
        if (seconds < AttackSeconds)
        {
            // Ease OUT into the bottom: fast off the top, decelerating onto the detent. An ease-IN
            // here would read as the cap hesitating under a finger that has already committed.
            float t = seconds / AttackSeconds;
            return 1f - (1f - t) * (1f - t);
        }
        float held = seconds - AttackSeconds;
        if (held < HoldSeconds)
            return 1f;
        float rel = (held - HoldSeconds) / ReleaseSeconds;
        if (rel >= 1f)
            return 0f;
        // Two explicit legs rather than one closed form — see the class doc for the closed forms
        // that were tried and why they fail. This way the crossing, the peak and the endpoint are
        // arithmetic: the cap reaches exactly −ReboundFraction at ReboundSplit, and exactly 0 at
        // the end.
        if (rel < ReboundSplit)
            return 1f - (1f + ReboundFraction) * Mathf.SmoothStep(0f, 1f, rel / ReboundSplit);
        return -ReboundFraction * (1f - Mathf.SmoothStep(0f, 1f, (rel - ReboundSplit) / (1f - ReboundSplit)));
    }
}
