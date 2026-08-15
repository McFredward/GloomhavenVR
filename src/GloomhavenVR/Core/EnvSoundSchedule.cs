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
/// THE TIMING ARITHMETIC behind the bank's percussive one-shots and behind the one EVENT in
/// <see cref="EnvSound"/> that is scheduled statistically rather than from the environment's
/// picture. Two functions:
///
/// <list type="bullet">
///   <item><see cref="SlipTrain"/> — the BURST TRAIN: the rat's claws on stone, the stick-slip of
///   rope or old timber taking weight, and (since ModBuild 149) the scatter of a bookcase's contents
///   arriving on the floor behind it. All three are "a run of short bursts, irregularly spaced,
///   spanning a window"; the only thing that separates them is how the spacing evolves — the creak's
///   gaps CLOSE as the load settles, the rat's hold an even beat, and the scatter's WIDEN as the
///   heavy things land first and the light ones keep tumbling.</item>
///   <item><see cref="PoissonGap"/> — the WAITING TIME between two independent events, for a caller
///   that schedules "the next one" rather than filling a window.
///
///   <para><b>IT IS BACK IN USE AS OF ModBuild 152, AND THE ROUND IT SPENT WITH NO CALLER IS THE
///   ARGUMENT FOR HAVING KEPT IT.</b> Its first caller was <c>EnvSound.TickFrost</c>, deleted with
///   the whole ice sound on the user's ruling ("Entferne das Geräusch für Eis komplett" — the record
///   is in <c>EnvSound.Bank.cs</c>), and the note that stood here through ModBuild 149-151 said the
///   function was kept because it is "the shape the next statistically-scheduled event will want".
///   That event is <c>EnvSound.TickFire</c>, the fire's crackle: wood cells burst independently at a
///   slowly-changing average rate, which is a Poisson process and therefore an exponential gap, and
///   what it inherited by reaching for this function instead of writing <c>-mean * ln(u)</c> again
///   is the whole of the paragraph below — a bound that makes a woodpecker unreachable and a NaN
///   fold that makes a never-arriving event unreachable, both already proven by vectors.</para></item>
/// </list>
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
///
/// <para>THE SAME CONTRACT, IN THE OTHER DIRECTION, IS WHAT <see cref="PoissonGap"/> IS FOR. It
/// does not loop at all, so it cannot spin — but a caller does, in the sense that a scheduler
/// writes <c>next = now + gap</c> and then waits for the clock to reach it. A gap of zero makes
/// that a per-frame emitter, and a gap of <c>NaN</c> or <c>Infinity</c>
/// makes it an event that never comes; both are the same class of defect as the freeze, reached
/// through arithmetic instead of through a loop. So the function's output is BOUNDED BY
/// CONSTRUCTION, for every input including <c>NaN</c>, and the vectors drive it with exactly those.
/// (The scheduler that motivated it — the ice sound's — is deleted; the one that uses it now is the
/// fire's crackle, and it leans on both bounds explicitly: see <c>EnvSound.TickFire</c>.)</para>
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

    /// <summary>Hard floor and ceiling on <see cref="PoissonGap"/>, as multiples of the mean. The
    /// floor is what makes the function safe: a gap of zero would leave a scheduler that writes
    /// <c>next = now + gap</c> firing on every frame forever, which is this file's failure mode in
    /// its other guise — not a wrong sound but a machine that never gets past this event. The
    /// ceiling is taste rather than safety (an exponential's tail is unbounded, and a 40 s silence in
    /// the middle of an event a player is watching reads as the feature having broken), but it is
    /// enforced the same way, so BOTH ends are properties a test can hold.</summary>
    internal const float PoissonGapMin = 0.28f;
    internal const float PoissonGapMax = 2.60f;

    /// <summary>
    /// A POISSON WAITING TIME with mean <paramref name="mean"/>, drawn from one uniform
    /// <paramref name="u"/> in [0,1], and BOUNDED — the result is always inside
    /// <c>mean * [</c><see cref="PoissonGapMin"/><c>, </c><see cref="PoissonGapMax"/><c>]</c> and
    /// always strictly positive, for every input including the ones no caller passes.
    ///
    /// <para><b>WHY AN EXPONENTIAL AND NOT A JITTERED CONSTANT.</b> Events that occur independently
    /// at a constant average rate — cracks in a surface under a slowly changing stress field, drips
    /// off a saturated ceiling, clicks in a Geiger tube — have a Poisson COUNT in any window and
    /// therefore an EXPONENTIAL gap between consecutive events, whose inverse CDF is
    /// <c>-mean * ln(u)</c>. That is the whole of the arithmetic here. The audible difference from
    /// "the mean, plus or minus 40%" is not subtlety: an exponential's mode is at ZERO, so it
    /// produces genuine CLUSTERS — two events almost together, then a long gap — while a jittered
    /// constant produces a wobbly metronome, and a wobbly metronome is still a metronome. The ice
    /// sound this was written for was not even wobbly — it beat at a fixed 0.45 s, the user called
    /// it "super nervig", and the whole cue is now deleted rather than re-timed.</para>
    ///
    /// <para><b>WHY IT LIVES IN THIS FILE.</b> Same reason as <see cref="SlipTrain"/>: it decides
    /// something only observable from inside a headset, and its failure mode is not a wrong sound
    /// but a scheduler that cannot advance. <c>Mathf.Log(0)</c> is <c>-Infinity</c> and
    /// <c>Mathf.Log</c> of a negative is <c>NaN</c>; either one, added to a "next event" time,
    /// produces a comparison that is false forever (NaN) or a wait that never ends (Infinity). The
    /// clamp below makes both unreachable, and <c>EnvSoundScheduleVectors</c> drives it with exactly
    /// those inputs.</para>
    ///
    /// <para><b>WHAT THE CLAMPS COST, stated rather than assumed.</b> Truncating an exponential at
    /// 0.28 and 2.60 of its mean moves the REALISED mean to
    /// <c>0.28(1-e^-0.28) + [1.28 e^-0.28 - 3.60 e^-2.60] + 2.60 e^-2.60 = 0.962</c> of the nominal,
    /// so a caller asking for 2.6 s gets 2.50 s. Under a quarter of draws land on the floor and
    /// seven per cent on the ceiling; the shape between them is untouched, which is where the
    /// clustering the caller wants actually lives.</para>
    /// </summary>
    /// <param name="mean">Mean gap in seconds. Non-positive is clamped up rather than rejected: a
    /// caller that lerps a mean from an element intensity can reach 0 through a config edit, and
    /// "the events come as fast as the floor allows" is a survivable answer where "next = now" is
    /// not.</param>
    /// <param name="u">A uniform draw in [0,1]. Anything outside — and <c>NaN</c>, which no
    /// comparison catches by accident — is folded back inside before the logarithm sees it.</param>
    internal static float PoissonGap(float mean, float u)
    {
        // NaN-SAFE BY CONSTRUCTION, and written as a positive test for that reason: `u < 0` is false
        // for NaN and so is `u > 1`, so a pair of rejecting comparisons would pass NaN straight
        // through to Mathf.Log. `!(u > lo && u < hi)` is true for NaN, so NaN lands on the median
        // draw and the scheduler keeps moving.
        if (!(u > 0f && u <= 1f))
            u = 0.5f;

        // The upper bound is not politeness, it is the same defence as the NaN fold: `mean > 0f` is
        // TRUE for +Infinity, and an infinite mean multiplied through the clamp comes back as an
        // infinite gap — an event that is scheduled for never. 1e6 s is eleven days, so no caller
        // with a real intention is inside the bound this rejects.
        float m = mean > 0f && mean < 1e6f ? mean : 0.05f;

        // -ln(u) is the exponential's inverse CDF; u is already known to be in (0, 1], so the log is
        // finite and non-positive and the gap is non-negative before the clamp.
        float gap = -m * Mathf.Log(u);
        return Mathf.Clamp(gap, m * PoissonGapMin, m * PoissonGapMax);
    }
}
