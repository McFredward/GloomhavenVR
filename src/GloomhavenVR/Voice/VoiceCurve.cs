using UnityEngine;

namespace GloomhavenVR.Voice;

// =================================================================================================
//  VOICE CURVE — how loud a teammate is at a given distance, and how far the speaking indicator
//  deflects. Pure arithmetic over PERCEIVED METRES, free of the audio engine, the config system,
//  the game model and every Unity type except Mathf, precisely so it can be linked into
//  tests/GloomhavenVR.WireTests and driven metre by metre without a headset.
// =================================================================================================

/// <summary>
/// The attenuation curve for a peer's voice, and the level-to-indicator mapping.
///
/// <para><b>WHY THIS IS ITS OWN FILE.</b> The same reason
/// <c>Core/EnvSoundSchedule.cs</c>, <c>Rig/LiftWedge.cs</c> and <c>WorldUI/ConfigSteps.cs</c> are:
/// its output is observed ONLY by ear, from inside a headset, with a second person on the other end
/// of a microphone — which is the least reproducible test this repo has. A curve that is 12 dB too
/// steep does not throw, does not log and does not look wrong; it produces a teammate who "cuts
/// out" and a bug report nobody can localise. Keeping the arithmetic Unity-free means the shape can
/// be asserted in decibels by a gate that runs on every build
/// (<c>tests/GloomhavenVR.WireTests/VoiceCurveVectors.cs</c>).</para>
///
/// <para><b>WHAT THIS FILE DOES NOT PROVE.</b> It proves the curve we AUTHOR has the decibel values
/// we claim. It does not prove Unity's audio engine APPLIES it — that is a separate question with a
/// separate instrument (<c>scripts/voice-spatial-probe.sh</c>, which measures the real mixed output
/// of a real <c>AudioSource</c>). Do not let this file's green tick stand in for that one; this
/// project has already shipped a remedy that never ran.</para>
///
/// =============================================================================================
/// <para><b>PERCEIVED METRES, NOT WORLD UNITS — and this project has paid for that lesson once
/// already.</b> Every distance in this file is a PERCEIVED metre: what the player's body feels,
/// not what Unity measures. The rig root's <c>lossyScale</c> is the diorama scale
/// (<c>Rig/VRRigDriver.cs:61-65</c>, written in <c>Rig/Comfort.cs:78</c>) and it runs at roughly 13
/// to 26 world units per perceived metre in a scenario, more on the map table. The conversion is
/// <c>worldDistance = perceivedMetres * rigScale</c>, exactly as
/// <c>Core/EnvSound.cs:128-155</c> states it, and it is applied in exactly one place
/// (<c>VoiceSpatial.ApplyScale</c>) for exactly the same reason EnvSound gives. A rolloff authored
/// in metres and typed straight into <c>AudioSource.maxDistance</c> would silence a teammate
/// standing an arm's length away.</para>
///
/// =============================================================================================
/// <para><b>THE SHAPE, AND WHY IT IS NOT LOGARITHMIC.</b> <c>EnvSound</c> uses
/// <c>AudioRolloffMode.Logarithmic</c> and says so as "physical, and the default Unity tunes for".
/// That is right for a dripping ceiling and wrong for a person talking, and the difference is the
/// whole point of this file. Unity's logarithmic rolloff is <c>minDistance / d</c>: it is
/// -6 dB per doubling of distance forever, with no flat region at all. Two players standing around
/// a table are perhaps 1 to 3 perceived metres apart, so under a logarithmic curve a teammate
/// leaning in and a teammate leaning back would differ by roughly 6 dB — a level that visibly
/// pumps every time anybody shifts their weight, on the one signal in the game that has to stay
/// intelligible.</para>
///
/// <para>So the curve is CUSTOM and it has three regions:</para>
/// <list type="number">
/// <item><b>A flat plateau out to <c>fullLevelMeters</c>.</b> Inside it a voice is at full
/// level and moving your head does nothing at all. This is the region normal conversation actually
/// happens in, and it is flat ON PURPOSE.</item>
/// <item><b>A gentle power falloff</b> from there to <c>silenceMeters</c>, shaped by
/// <c>shape</c>. Speech stays intelligible right across a room: at the shipped defaults a
/// teammate at 8 m is about -4.2 dB, which is quieter and still perfectly clear.</item>
/// <item><b>Silence at and beyond <c>silenceMeters</c>.</b> A hard zero, so a voice cannot
/// arrive from a peer who is not in the room with you.</item>
/// </list>
///
/// <para><b>THE AUDIBLE RANGE, STATED IN NUMBERS BECAUSE "IT SOUNDS FINE" IS NOT A SPECIFICATION.</b>
/// At the shipped defaults (plateau 2 m, silence 25 m, shape 1.6) the curve reads:</para>
/// <code>
///     0.0 m    0.00 dB      8.0 m   -4.20 dB
///     2.0 m    0.00 dB     14.0 m  -10.25 dB
///     4.0 m   -1.26 dB     20.0 m  -21.21 dB
///     6.0 m   -2.66 dB     25.0 m   silence
/// </code>
/// <para>Those numbers are asserted by the wire-test gate, so they cannot drift unnoticed. Every
/// one of the three parameters is a dial — see <c>Defaults/Defaults.Voice.cs</c>.</para>
/// </summary>
internal static class VoiceCurve
{
    /// <summary>Below this the curve treats two distances as the same point.</summary>
    private const float Epsilon = 1e-4f;

    /// <summary>
    /// How many keys the sampled <c>AnimationCurve</c> gets. The falloff is a smooth power
    /// function; Unity interpolates between keys with Hermite splines, so a modest count tracks it
    /// closely and the error is asserted in the gate rather than assumed.
    /// </summary>
    internal const int KeyCount = 24;

    /// <summary>
    /// Linear gain (0..1) for a source <paramref name="metres"/> PERCEIVED metres away.
    ///
    /// <para>Parameters are passed rather than read from config so that this stays a pure function
    /// of its arguments — the wire-test gate drives it with the shipped defaults AND with the ends
    /// of every dial's range, which it could not do if the file reached for a
    /// <c>ConfigEntry</c>.</para>
    /// </summary>
    /// <param name="metres">Distance in perceived metres. Negative is clamped to 0.</param>
    /// <param name="fullLevelMeters">Radius of the flat plateau.</param>
    /// <param name="silenceMeters">Distance at which the voice reaches exactly zero.</param>
    /// <param name="shape">Falloff exponent. 1 is a straight line; above 1 holds the level up
    /// longer and then drops faster near the end; below 1 drops early.</param>
    internal static float Gain(float metres, float fullLevelMeters, float silenceMeters, float shape)
    {
        // Sanitise rather than assert: these arrive from a config file a user can hand-edit, and a
        // nonsensical pair must produce a working voice, not a divide by zero. The ordering fix is
        // deliberate — "plateau larger than the silence radius" reads as "never attenuate".
        if (float.IsNaN(metres) || metres < 0f)
            metres = 0f;
        if (!(fullLevelMeters >= 0f))
            fullLevelMeters = 0f;
        if (!(silenceMeters > fullLevelMeters + Epsilon))
            return metres <= fullLevelMeters ? 1f : 0f;
        if (!(shape > Epsilon))
            shape = 1f;

        if (metres <= fullLevelMeters)
            return 1f;
        if (metres >= silenceMeters)
            return 0f;

        float t = (silenceMeters - metres) / (silenceMeters - fullLevelMeters); // 1 at the plateau edge, 0 at silence
        return Mathf.Pow(t, shape);
    }

    /// <summary>
    /// <see cref="Gain"/> expressed in decibels, with a floor so silence is a number rather than
    /// negative infinity. Exists because the acceptance criterion for this curve is stated in dB
    /// and a test should assert the thing that was promised.
    /// </summary>
    internal static float GainDb(float metres, float fullLevelMeters, float silenceMeters, float shape)
    {
        float g = Gain(metres, fullLevelMeters, silenceMeters, shape);
        return g <= 1e-6f ? -120f : 20f * Mathf.Log10(g);
    }

    /// <summary>
    /// Fill <paramref name="times"/> / <paramref name="values"/> with <see cref="KeyCount"/> keys
    /// describing the curve for Unity's <c>AudioSourceCurveType.CustomRolloff</c>.
    ///
    /// <para><b>THE TIME AXIS IS NORMALISED BY <paramref name="silenceMeters"/>, i.e. by what will
    /// become <c>AudioSource.maxDistance</c>, and that is an assumption this file cannot
    /// verify.</b> Unity documents the custom rolloff curve as spanning 0..1 over
    /// 0..<c>maxDistance</c>, with <c>minDistance</c> unused; the alternative reading is that it
    /// spans <c>minDistance</c>..<c>maxDistance</c>. The two disagree by exactly the plateau width,
    /// which is the difference between a correct curve and one that reaches silence three metres
    /// early. THIS IS WHY THE HEADLESS PROBE EXISTS: <c>scripts/voice-spatial-probe.sh</c> measures
    /// real attenuation against real distance, and its rolloff column settles the question by
    /// measurement instead of by reading. If it ever shows the min..max reading, the fix is this
    /// one function and nothing else. See "WHAT I COULD NOT VERIFY" in
    /// <c>.planning/VOICE-SPATIAL.md</c>.</para>
    ///
    /// <para>The last key is pinned to exactly (1, 0) so the curve cannot end on an interpolated
    /// value slightly above zero — a voice that never quite stops is worse than one that stops a
    /// metre early.</para>
    /// </summary>
    internal static void SampleKeys(float[] times, float[] values,
                                    float fullLevelMeters, float silenceMeters, float shape)
    {
        if (times == null || values == null || times.Length < KeyCount || values.Length < KeyCount)
            return;
        if (!(silenceMeters > Epsilon))
            silenceMeters = 1f;

        for (int i = 0; i < KeyCount; i++)
        {
            float t = i / (float)(KeyCount - 1);           // 0..1
            float metres = t * silenceMeters;              // 0..silence, PERCEIVED
            times[i] = t;
            values[i] = Gain(metres, fullLevelMeters, silenceMeters, shape);
        }
        times[KeyCount - 1] = 1f;
        values[KeyCount - 1] = 0f;
    }

    // =============================================================================================
    //  THE INDICATOR'S DEFLECTION
    // =============================================================================================

    /// <summary>How many lit states the speaker icon has: silent, plus <see cref="Steps"/>-1 arcs.</summary>
    internal const int Steps = 4;

    /// <summary>
    /// Which of the <see cref="Steps"/> icon states a smoothed level belongs in, with hysteresis
    /// against <paramref name="previous"/>.
    ///
    /// <para><b>THE HYSTERESIS IS THE POINT.</b> Speech level crosses any fixed threshold many times
    /// a second, so a bare comparison produces an icon that strobes — which is precisely the
    /// "nervig" failure mode this project has already shipped once, on the ice sound. A step only
    /// rises when the level clears the threshold above and only falls when it drops a margin below
    /// the threshold it came from, so a level sitting exactly on a boundary holds still.</para>
    ///
    /// <para>Returns 0 when <paramref name="speaking"/> is false, unconditionally: the ON/OFF state
    /// of this indicator is the GAME's own <c>IUserVoice.IsSpeaking</c> and never our own opinion.
    /// See <c>VoiceSpatial</c>'s doc for why the gate and the deflection come from different
    /// places.</para>
    /// </summary>
    internal static int StepFor(float level, int previous, bool speaking)
    {
        if (!speaking)
            return 0;
        if (float.IsNaN(level) || level < 0f)
            level = 0f;

        // Thresholds to ENTER step 1, 2, 3. Chosen against typical voice-chat RMS after Photon's
        // own gain staging; every one of them is a guess until a second client confirms it, and
        // they are deliberately low so that a quiet talker still moves the icon at all.
        const float Up1 = 0.020f;
        const float Up2 = 0.070f;
        const float Up3 = 0.180f;
        const float Fall = 0.65f;   // drop to 65% of the entry threshold before stepping back down

        int step = previous < 0 ? 0 : previous > Steps - 1 ? Steps - 1 : previous;

        // Rise: at most one step per call, so a transient cannot slam the icon to full.
        if (step < 3 && level >= Up3) step = 3;
        else if (step < 2 && level >= Up2) step = 2;
        else if (step < 1 && level >= Up1) step = 1;

        // Fall.
        if (step == 3 && level < Up3 * Fall) step = 2;
        if (step == 2 && level < Up2 * Fall) step = 1;
        if (step == 1 && level < Up1 * Fall) step = 0;

        // The game says they are talking, so never show the fully silent frame: step 0 while
        // speaking would contradict the row the flat game's own ESC menu is drawing at that moment.
        return step < 1 ? 1 : step;
    }

    /// <summary>
    /// One-pole smoothing of a raw RMS reading, with a fast attack and a slow release so the icon
    /// snaps up on a syllable and eases down between words instead of flickering per frame.
    /// Frame-rate independent (the coefficient is an exponential in dt), because this mod runs at
    /// 90 Hz on the rig and at whatever the desktop mirror manages.
    /// </summary>
    internal static float Smooth(float previous, float raw, float dt)
    {
        if (float.IsNaN(raw) || raw < 0f)
            raw = 0f;
        if (!(dt > 0f))
            return raw;

        const float AttackSeconds = 0.020f;
        const float ReleaseSeconds = 0.220f;
        float tau = raw > previous ? AttackSeconds : ReleaseSeconds;
        float k = 1f - Mathf.Exp(-dt / tau);
        return previous + (raw - previous) * k;
    }
}
