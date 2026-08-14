using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Deterministic xorshift32, shared by every generator in <see cref="EnvSoundBank"/> and by
/// <see cref="EnvSoundSchedule"/>.
///
/// <para>NOT <c>UnityEngine.Random</c>: that is process-global state which any other subsystem can
/// reseed between two of our calls, which would make the bank differ between two runs of the same
/// build for reasons nobody could trace. Returns -1..1.</para>
///
/// <para>It lives HERE rather than inside the bank so that the schedule below — the one piece of
/// the audio synthesis with a termination property worth proving — can be compiled into
/// <c>tests/GloomhavenVR.WireTests</c> without dragging <c>AudioClip</c> and the whole Unity audio
/// module in with it. The bank keeps using it under a file-scoped alias, so every generator's draw
/// sequence is byte-identical to the one this struct produced when it was private there.</para>
/// </summary>
internal struct EnvSoundRng
{
    private uint _s;

    internal EnvSoundRng(uint seed) => _s = seed == 0u ? 0x9E3779B9u : seed;

    internal float Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 17;
        _s ^= _s << 5;
        // 2^-31 scaling into -1..1; the cast is to int first so the sign bit is used.
        return (int)_s * 4.656613e-10f;
    }
}

/// <summary>
/// The BURST TRAIN schedules behind the bank's percussive one-shots: the rat's claws on stone, and
/// the stick-slip of rope or old timber taking weight. Both are "a run of short bursts, irregularly
/// spaced, spanning a window" — the only thing that separates them is how the spacing evolves.
///
/// <para>WHY THIS IS ITS OWN FILE, FREE OF UNITY. It is here for the reason
/// <c>Rig/ScrollTurnGate.cs</c> and <c>WorldUI/ConfigSteps.cs</c> are: it decides something that is
/// only ever observed from inside a headset, and its failure mode is not a wrong sound but NO
/// FRAME AT ALL. ModBuild 145 shipped the creak as a <c>while (t &lt; 1.20f)</c> whose step was
/// multiplied by 0.90 every pass, so the times it generated formed a GEOMETRIC SERIES — and the sum
/// of that series (0.05 + 0.115 x 0.9 / 0.1 ~ 1.09 s, against a 1.20 s window) is SHORTER than the
/// window it was trying to fill. The step underflowed to a denormal after ~800 passes and <c>t</c>
/// stopped moving altogether: an infinite loop on the main thread, inside the first
/// <c>EnvSoundBank.Build()</c>, which runs on the very frame the room is first placed. The game
/// froze on the loading screen with no exception, no warning and no log line — the last thing in
/// Player.log is the mod's own "ROOM placed", written a few statements earlier. The generator's
/// <c>try/catch</c> could not help: a spin throws nothing.
///
/// <para>So the contract here is TERMINATION BY CONSTRUCTION, and it is a contract a test can
/// hold: the caller states how many bursts it wants, the loop is a <c>for</c> over that count, and
/// the gaps are normalised AFTERWARDS to span the window exactly. There is no accumulator whose
/// convergence decides how long the loop runs, and there is no window a shrink factor can fail to
/// reach — the span is an input, not an outcome. Driven burst by burst in
/// <c>tests/GloomhavenVR.WireTests/EnvSoundScheduleVectors.cs</c>.</para>
/// </summary>
internal static class EnvSoundSchedule
{
    /// <summary>
    /// Fill <paramref name="into"/> with the times, in seconds, of a burst train: the first burst at
    /// <paramref name="first"/>, the last at <paramref name="last"/>, and the gaps between them
    /// scaled by <paramref name="shrink"/> each step so the train accelerates (&lt; 1), holds an
    /// even beat (= 1) or spreads out (&gt; 1). <paramref name="jitter"/> is the fraction each
    /// individual gap is randomly stretched or squeezed by — a real gait is not a metronome —
    /// applied BEFORE the normalisation, so it changes the rhythm without ever changing the span.
    ///
    /// <para>Returns the number of times written, which is always <c>into.Length</c> for a
    /// non-empty buffer. The times are strictly increasing and every one of them lies in
    /// [<paramref name="first"/>, <paramref name="last"/>] — the caller may index the buffer with
    /// them without a bounds test of its own beyond the one it already needs for the burst LENGTH.
    /// A degenerate window (<paramref name="last"/> at or before <paramref name="first"/>) is not an
    /// error: every burst lands on <paramref name="first"/> and the caller draws them on top of one
    /// another, which is inaudible rather than fatal.</para>
    /// </summary>
    internal static int SlipTrain(float[] into, float first, float last, float shrink, float jitter, uint seed)
    {
        if (into == null || into.Length == 0)
            return 0;

        int n = into.Length;
        into[0] = first;
        if (n == 1)
            return 1;

        float span = last - first;
        if (!(span > 0f))
        {
            for (int i = 1; i < n; i++)
                into[i] = first;
            return n;
        }

        // THE GAPS, in arbitrary units. `g` is the geometric envelope; the jitter multiplier is
        // floored well above zero so that no draw can produce a zero-length gap and turn two bursts
        // into one. Nothing here is compared against the window — the window is applied below.
        var r = new EnvSoundRng(seed);
        float g = 1f;
        float sum = 0f;
        for (int i = 1; i < n; i++)
        {
            float gap = g * Mathf.Max(1f + jitter * r.Next(), 0.05f);
            into[i] = gap;
            sum += gap;
            g *= shrink;
        }

        // ...and NOW the window. One scale factor turns whatever shape the envelope produced into a
        // train that starts at `first` and ends on `last`, whatever `shrink` was — which is exactly
        // the step ModBuild 145 did not have. The guard on `sum` is for a caller that passes
        // shrink = 0 (every gap after the first is zero); the train then degenerates to two bursts
        // at the ends, still bounded, still increasing.
        float k = span / Mathf.Max(sum, 1e-6f);
        float t = first;
        for (int i = 1; i < n; i++)
        {
            t += into[i] * k;
            into[i] = Mathf.Min(t, last);
        }
        into[n - 1] = last;
        return n;
    }
}
