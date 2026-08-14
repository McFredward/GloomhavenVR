using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// The clips <see cref="EnvSoundBank"/> offers, as a NAME rather than a property reference.
///
/// <para>It exists so that <see cref="EnvSound"/>'s emitter table can be written as data — a cue is
/// "card 5 gets <c>Fall</c>", not "card 5 gets whatever this property currently returns" — and so a
/// log line can print WHICH clip a cue chose without a lookup table of its own. Every value maps to
/// exactly one property on the bank; <see cref="EnvSoundBank.Bank"/> is the only translation.</para>
/// </summary>
internal enum EnvSoundClip
{
    /// <summary>The shared stationary noise bed — flame, draught and leaves all ride this one.</summary>
    Bed,
    Drip,
    Squeak,
    Skitter,
    Frost,
    Rumble,
    Chirr,
    Hum,
    Creak,
    Breath,
    Drag,
    Fly,
    Fall,
    Settle,
}

// =================================================================================================
//  ENV SOUND — THE BANK. Every clip the environment can make, SYNTHESIZED into memory at spawn.
//  This file is the asset. There is no wav, no bundle entry, no download and no licence, and that
//  is a decision that was weighed rather than defaulted into — see WHY SYNTHESIS below.
// =================================================================================================

/// <summary>
/// The clip bank for <see cref="EnvSound"/>: a handful of mono <see cref="AudioClip"/>s built with
/// <see cref="AudioClip.Create(string,int,int,int,bool)"/> the first time an environment stands up,
/// kept for the session, and destroyed with the rest of the feature.
///
/// <para><b>WHY SYNTHESIS AND NOT FOUND RECORDINGS — the honest weighing, because the standing
/// instruction is "Suche nach coolen Assets im Internet statt es zwingend selber zu bauen" and this
/// file is a decision AGAINST that default.</b> Three things decided it, in order of weight:</para>
/// <list type="number">
/// <item><b>There is nowhere to put a recording.</b> A clip has to ship. The asset bundle is the
/// natural home (an <c>AudioClip</c> is data, not a MonoBehaviour, so unlike an
/// <c>AudioSource</c> it COULD live there) but the bundle is owned by other lanes this round and is
/// not available to this one. The remaining route is an <c>EmbeddedResource</c> in the plugin DLL,
/// which this project has never used for anything, would put binary blobs in git, and would need a
/// hand-written WAV parser because Unity cannot decode a byte[] into an <c>AudioClip</c> without
/// <c>UnityWebRequest</c> and a file on disk. That is a lot of new machinery for a texture that a
/// hundred lines of arithmetic produce exactly.</item>
/// <item><b>The licences are worse than they look.</b> Freesound's CC0 filter is genuinely
/// unencumbered (CC0 needs no attribution and permits redistribution, commercial included), so that
/// route is legally fine. The BBC Sound Effects library — the obvious first thought for a cellar
/// drip and a cave draught — is NOT: its 16 000 WAVs are BBC copyright released under the RemArc
/// licence, which permits personal, educational and research use only. Redistributing one inside a
/// publicly downloadable mod is exactly what it does not cover. Nothing here is CC0-blocked; it is
/// simply that the CC0 route buys nothing the arithmetic does not already give.</item>
/// <item><b>Synthesis is BETTER for this particular brief, not merely cheaper.</b> The requirement
/// is "dezent" and "nie aufdringlich überlagernd", and the specific failure mode of a recorded
/// ambience is the LOOP: the ear finds the seam and the one distinctive event inside the buffer
/// within a couple of passes, and from then on the environment is announcing itself. The beds here
/// are stationary filtered noise — statistically featureless, so there is no event to latch onto —
/// and every recognisable contour (the gust, the flame's breathing) is applied at RUNTIME by
/// <see cref="EnvSound"/> from LFOs whose periods are in irrational ratios, so the amplitude
/// envelope never repeats with the buffer. A recording cannot have that property; this can, and it
/// is the property the user actually asked for.</item>
/// </list>
///
/// <para><b>WHERE SYNTHESIS IS WEAKEST, stated rather than hidden.</b> The mouse squeak is the one
/// clip a recording would clearly beat: a real rodent call has formant structure that an FM chirp
/// only gestures at. It is kept because it is 90 ms long, plays at most once every 26 s, and sits
/// under a bed — at that exposure the difference is not worth the machinery above. If a later round
/// gets bundle access, <see cref="Squeak"/> is the ONE clip worth replacing with a CC0 recording.
/// The others (drip, flame, draught, rumble) are physically noise-and-resonance processes and the
/// arithmetic IS the honest model of them.</para>
///
/// <para><b>DETERMINISM.</b> Every clip is generated from <see cref="Rng"/>, an explicit xorshift
/// seeded per clip — never <c>UnityEngine.Random</c>, whose sequence is global state another
/// subsystem can disturb. Two clients therefore hold bit-identical buffers. Audio is local and
/// needs no wire, so this buys nothing on the network; it buys REPRODUCIBILITY, which is what makes
/// a listener's report ("the drip sounds wrong") mean the same thing on the machine that has to fix
/// it.</para>
///
/// <para><b>COST.</b> One shared 8 s noise bed plus nine short clips at the output sample rate,
/// mono, ~2 MB of float data total, generated once per session in a few milliseconds and never
/// touched again. Mono is not a saving but a REQUIREMENT: Unity refuses to spatialise a stereo
/// clip, and every clip here is meant to come from a place.</para>
/// </summary>
internal static class EnvSoundBank
{
    /// <summary>Sample rate of everything here. Taken from the live output device rather than
    /// assumed, so nothing is resampled on playback.</summary>
    private static int Rate => AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;

    /// <summary>
    /// THE SHARED BED. Eight seconds of stationary broadband noise, looped by every continuous
    /// emitter (flame, draught, leaves) and shaped per emitter by a runtime low/high-pass filter
    /// plus a runtime gain LFO.
    ///
    /// <para>ONE BUFFER FOR THREE SOUNDS is not a memory trick, it is what makes them not repeat.
    /// A fire and a draught differ physically by their spectrum and their envelope, not by their
    /// noise; giving them one source of noise and two different filters is the correct model, and
    /// it means the only thing that could ever sound periodic — the buffer — is the one thing that
    /// carries no information at all.</para>
    ///
    /// <para>EIGHT SECONDS, and the length is chosen against the FILTER rather than against the
    /// ear: a 40 Hz-cornered low pass needs a good many cycles of its lowest passed frequency
    /// inside the buffer or the wrap becomes a click. 8 s at 40 Hz is 320 cycles. The wrap itself is
    /// cross-faded (see below) so even that cannot tick.</para>
    /// </summary>
    internal static AudioClip? Bed { get; private set; }

    /// <summary>A single water drop landing in a shallow puddle. One-shot, ~0.40 s.</summary>
    internal static AudioClip? Drip { get; private set; }

    /// <summary>The rat, heard: "Mäusepiepen" (user, verbatim). One-shot, ~0.09 s.</summary>
    internal static AudioClip? Squeak { get; private set; }

    /// <summary>The rat's feet on flagstones — a run of tiny dry ticks. One-shot, ~0.55 s.</summary>
    internal static AudioClip? Skitter { get; private set; }

    /// <summary>Frost: a noise burst behind a high resonance, the sound of something crazing.
    /// One-shot, ~0.30 s.</summary>
    internal static AudioClip? Frost { get; private set; }

    /// <summary>Earth: a very low, slow settling. Looped, 6 s.</summary>
    internal static AudioClip? Rumble { get; private set; }

    /// <summary>The swamp at night — amplitude-modulated noise, the chirr of insects. Looped, 7 s.</summary>
    internal static AudioClip? Chirr { get; private set; }

    /// <summary>A wisp: two close sines beating slowly against each other. Looped, 5 s.</summary>
    internal static AudioClip? Hum { get; private set; }

    // ---- the haunt cues ---------------------------------------------------------------------
    //
    // SIX CUES FOR SIX APPARITIONS, and the design rule they all obey is the coordinator's, which
    // is also the only rule that makes a horror cue work: THE FRIGHTENING SOUND IS NEVER THE LOUD
    // ONE. There is no stinger here, no hit, no impact transient with a fast attack — every one of
    // these starts under the bed and grows into it. A jump-scare would break "nie aufdringlich" and
    // the standing rule that the easter eggs may never disturb play, and it would also be worse
    // horror: the apparitions are built to be things you are not sure you saw, and a sound that
    // announces them converts a doubt into an event.

    /// <summary>Rope or old wood taking weight — a slow, irregular creak. ~1.3 s.</summary>
    internal static AudioClip? Creak { get; private set; }

    /// <summary>A breath that is not yours. Noise through a moving vocal-ish resonance. ~1.0 s.</summary>
    internal static AudioClip? Breath { get; private set; }

    /// <summary>Cloth, or a palm, dragging on stone. Band-limited noise with a slow sweep. ~1.4 s.</summary>
    internal static AudioClip? Drag { get; private set; }

    /// <summary>One fly, close, looping past. AM/FM buzz. ~1.6 s.</summary>
    internal static AudioClip? Fly { get; private set; }

    /// <summary>Something heavy going over, heard through a cellar's worth of air: no crack, no
    /// clatter — a muffled low fall. ~1.9 s. This is the bookshelf.</summary>
    internal static AudioClip? Fall { get; private set; }

    /// <summary>...and the same mass coming back up, slower and quieter, which is the more
    /// unsettling half. ~2.8 s.</summary>
    internal static AudioClip? Settle { get; private set; }

    private static bool _built;

    /// <summary>True once <see cref="Build"/> has produced a usable bank.</summary>
    internal static bool Ready => _built && Bed != null;

    /// <summary>
    /// The one translation from <see cref="EnvSoundClip"/> to a clip. Returns null rather than
    /// throwing for anything the bank failed to build, because every caller already has to handle a
    /// null clip (the bank is allowed to fail wholesale on a device with no audio) — so a missing
    /// clip degrades to "that one cue is silent" instead of to an exception inside the environment
    /// driver's per-frame path.
    /// </summary>
    internal static AudioClip? Bank(EnvSoundClip which) => which switch
    {
        EnvSoundClip.Bed => Bed,
        EnvSoundClip.Drip => Drip,
        EnvSoundClip.Squeak => Squeak,
        EnvSoundClip.Skitter => Skitter,
        EnvSoundClip.Frost => Frost,
        EnvSoundClip.Rumble => Rumble,
        EnvSoundClip.Chirr => Chirr,
        EnvSoundClip.Hum => Hum,
        EnvSoundClip.Creak => Creak,
        EnvSoundClip.Breath => Breath,
        EnvSoundClip.Drag => Drag,
        EnvSoundClip.Fly => Fly,
        EnvSoundClip.Fall => Fall,
        _ => Settle,
    };

    /// <summary>
    /// Build every clip. Idempotent, and safe to call from the environment's per-frame path: the
    /// guard is the first line, so the cost after the first call is one bool read.
    ///
    /// <para>Never throws. A device with no audio output, a sample rate of 0, an
    /// <see cref="AudioClip.Create(string,int,int,int,bool)"/> that refuses — all of them must
    /// degrade to "the environment is silent", never take the environment driver down with them.
    /// <see cref="Ready"/> is what the caller tests, and it stays false.</para>
    /// </summary>
    internal static void Build()
    {
        if (_built)
            return;
        _built = true;

        try
        {
            int rate = Rate;

            Bed = MakeBed(rate);
            Drip = MakeDrip(rate);
            Squeak = MakeSqueak(rate);
            Skitter = MakeSkitter(rate);
            Frost = MakeFrost(rate);
            Rumble = MakeRumble(rate);
            Chirr = MakeChirr(rate);
            Hum = MakeHum(rate);

            Creak = MakeCreak(rate);
            Breath = MakeBreath(rate);
            Drag = MakeDrag(rate);
            Fly = MakeFly(rate);
            Fall = MakeFall(rate);
            Settle = MakeSettle(rate);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Core", $"ENV SOUND bank could not be synthesized ({ex.GetType().Name}: " +
                               $"{ex.Message}) — the environment stays silent this session. Nothing " +
                               "else is affected: every emitter tests EnvSoundBank.Ready before it " +
                               "is created, so a failed bank is a missing feature, not a fault.");
        }
    }

    /// <summary>
    /// Drop every clip. Called from <see cref="EnvSound.StandDown"/>'s teardown route only — NOT
    /// from an ordinary stand-down, because the bank is style-independent and rebuilding ~2 MB of
    /// noise on every mixed-reality toggle would be pure waste. What must never survive is a
    /// SOURCE (see <see cref="EnvSound"/>); a clip nobody plays is inert.
    /// </summary>
    internal static void Release()
    {
        if (!_built)
            return;
        _built = false;

        // Every clip made by Finish is in _made, Bed included — so the ONE loop below destroys
        // everything exactly once. A separate "kill the bed first" step used to live here and was
        // a double-destroy waiting to happen.
        Bed = null; Drip = null; Squeak = null; Skitter = null; Frost = null;
        Rumble = null; Chirr = null; Hum = null;
        Creak = null; Breath = null; Drag = null; Fly = null; Fall = null; Settle = null;

        foreach (AudioClip? c in _made)
        {
            if (c != null)
                Object.Destroy(c);
        }
        _made.Clear();
    }

    private static readonly System.Collections.Generic.List<AudioClip?> _made = new();

    // =============================================================================================
    //  THE GENERATORS
    // =============================================================================================

    /// <summary>
    /// Deterministic xorshift32. NOT <c>UnityEngine.Random</c>: that is process-global state which
    /// any other subsystem can reseed between two of our calls, which would make the bank differ
    /// between two runs of the same build for reasons nobody could trace. Returns -1..1.
    /// </summary>
    private struct Rng
    {
        private uint _s;

        internal Rng(uint seed) => _s = seed == 0u ? 0x9E3779B9u : seed;

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
    /// Wrap a finished float buffer into a clip and remember it for <see cref="Release"/>.
    /// Mono and at the output rate — the two properties every caller depends on.
    /// </summary>
    private static AudioClip Finish(string name, float[] data, int rate)
    {
        var clip = AudioClip.Create("GhvrEnvSound." + name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        _made.Add(clip);
        return clip;
    }

    /// <summary>
    /// Cross-fade the last <paramref name="tail"/> samples of a LOOPING buffer over its own head,
    /// so the wrap has no discontinuity and cannot tick. Applied to every looped clip; one-shots
    /// do not need it because they end in silence.
    ///
    /// <para>The fade is equal-POWER (sin/cos), not linear: two independent noise signals summed
    /// with linear weights lose 3 dB in the middle of the fade, which is audible on a bed as a
    /// periodic dip — the exact "the ear latches onto the loop" failure this whole file is built to
    /// avoid.</para>
    /// </summary>
    private static void LoopFade(float[] d, int tail)
    {
        int n = d.Length;
        if (tail <= 0 || tail * 2 >= n)
            return;
        for (int i = 0; i < tail; i++)
        {
            float t = (i + 0.5f) / tail;
            float a = Mathf.Sin(t * Mathf.PI * 0.5f);   // incoming head
            float b = Mathf.Cos(t * Mathf.PI * 0.5f);   // outgoing tail
            d[i] = d[i] * a + d[n - tail + i] * b;
        }
        // The tail has been folded into the head; blank it so the buffer ends where the head began.
        for (int i = n - tail; i < n; i++)
            d[i] = d[i - (n - tail)];
    }

    /// <summary>Normalise to a target peak. Every generator ends with this so the LEVELS in
    /// <see cref="EnvSound"/> are the only place loudness is decided — a generator that quietly
    /// ran hot would otherwise defeat the gain budget from underneath it.</summary>
    private static void Normalise(float[] d, float peak)
    {
        float max = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float a = d[i] < 0f ? -d[i] : d[i];
            if (a > max)
                max = a;
        }
        if (max <= 1e-6f)
            return;
        float g = peak / max;
        for (int i = 0; i < d.Length; i++)
            d[i] *= g;
    }

    /// <summary>One-pole low pass, in place. <paramref name="hz"/> is the -3 dB corner.</summary>
    private static void LowPass(float[] d, int rate, float hz)
    {
        float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * hz / rate));
        float y = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            y += a * (d[i] - y);
            d[i] = y;
        }
    }

    /// <summary>One-pole high pass, in place (the low-passed part subtracted out).</summary>
    private static void HighPass(float[] d, int rate, float hz)
    {
        float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * hz / rate));
        float y = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            y += a * (d[i] - y);
            d[i] -= y;
        }
    }

    // ---- the bed --------------------------------------------------------------------------------

    private static AudioClip MakeBed(int rate)
    {
        int n = rate * 8;
        var d = new float[n];
        var r = new Rng(0x5EEDBEDu);

        // Pink-ish rather than white: Voss-McCartney's cheap cousin, three one-pole low passes at
        // octave-spaced corners summed. White noise is unpleasant to sit under for minutes at a
        // time (all the energy is at the top, where the ear is most sensitive and where the game's
        // UI cues live); a 1/f-ish slope is what natural airflow actually sounds like and it leaves
        // the top of the band free for the game.
        float a1 = 0f, a2 = 0f, a3 = 0f;
        float k1 = 1f - Mathf.Exp(-2f * Mathf.PI * 40f / rate);
        float k2 = 1f - Mathf.Exp(-2f * Mathf.PI * 320f / rate);
        float k3 = 1f - Mathf.Exp(-2f * Mathf.PI * 2600f / rate);
        for (int i = 0; i < n; i++)
        {
            float w = r.Next();
            a1 += k1 * (w - a1);
            a2 += k2 * (w - a2);
            a3 += k3 * (w - a3);
            d[i] = a1 * 0.62f + a2 * 0.30f + a3 * 0.14f;
        }

        LoopFade(d, rate / 2);
        Normalise(d, 0.85f);
        return Finish("Bed", d, rate);
    }

    // ---- one-shots ------------------------------------------------------------------------------

    /// <summary>
    /// A drop hitting water. The model is Minnaert's: the sound is the RESONANCE OF THE BUBBLE the
    /// impact entrains, which is a decaying sinusoid whose frequency RISES as the bubble shrinks —
    /// that rise is the whole reason a drip reads as "water" and not as "a bell". A short noise
    /// transient in front of it is the surface break.
    /// </summary>
    private static AudioClip MakeDrip(int rate)
    {
        int n = (int)(rate * 0.40f);
        var d = new float[n];
        var r = new Rng(0xD819u);

        const float F0 = 720f;      // a shallow puddle: a big, low bubble
        const float Rise = 2.35f;   // ...climbing to F0*Rise as it collapses
        const float Decay = 17f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-Decay * t);
            // THE PHASE IS THE INTEGRAL OF THE FREQUENCY, and writing it out is the point of this
            // comment. The instantaneous frequency being modelled is
            //     f(t) = F0 * (1 + (Rise-1) * (1 - exp(-9t)))
            // and the naive sin(2*pi*f(t)*t) would sweep the phase at roughly TWICE the intended
            // rate (it multiplies the swept frequency by the elapsed time instead of accumulating
            // it), which turns a drip into a rising whistle. So the closed-form integral is used.
            float phase = 2f * Mathf.PI * (F0 * t + (Rise - 1f) * F0 * (t + (Mathf.Exp(-9f * t) - 1f) / 9f));
            d[i] = Mathf.Sin(phase) * env * 0.8f;

            // the surface break — 6 ms of bright noise, gone before the resonance is established
            if (t < 0.006f)
                d[i] += r.Next() * (1f - t / 0.006f) * 0.55f;
        }

        HighPass(d, rate, 180f);
        Normalise(d, 0.9f);
        return Finish("Drip", d, rate);
    }

    /// <summary>
    /// "Mäusepiepen". A rodent call is a fast upward FM chirp with a little vibrato, high and very
    /// short. THE ONE CLIP A RECORDING WOULD BEAT (see the class doc) — kept because 90 ms once
    /// every 26 s under a bed is not where realism is won.
    /// </summary>
    private static AudioClip MakeSqueak(int rate)
    {
        int n = (int)(rate * 0.09f);
        var d = new float[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 0.09f;
            // 3.4 -> 5.6 kHz with a 90 Hz vibrato. High, but 90 ms long: too short to mask anything
            // and far too short to be "a tone".
            float f = 3400f + 2200f * u + 140f * Mathf.Sin(2f * Mathf.PI * 90f * t);
            phase += 2f * Mathf.PI * f / rate;
            // a raised-cosine envelope: no click at either end, which is what would make it a chirp
            // rather than a squeak
            float env = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * u);
            d[i] = (Mathf.Sin(phase) * 0.85f + Mathf.Sin(phase * 2f) * 0.15f) * env;
        }

        Normalise(d, 0.85f);
        return Finish("Squeak", d, rate);
    }

    /// <summary>Small claws on stone: a run of ~14 dry ticks, irregularly spaced (a real gait is
    /// not a metronome) and getting quieter as the animal goes.</summary>
    private static AudioClip MakeSkitter(int rate)
    {
        int n = (int)(rate * 0.55f);
        var d = new float[n];
        var r = new Rng(0x5C177E2u);

        float t = 0.01f;
        while (t < 0.52f)
        {
            int at = (int)(t * rate);
            float amp = (1f - t / 0.55f) * (0.55f + 0.45f * Mathf.Abs(r.Next()));
            int len = (int)(rate * 0.006f);
            for (int i = 0; i < len && at + i < n; i++)
                d[at + i] += r.Next() * amp * Mathf.Exp(-i / (float)len * 5f);
            t += 0.026f + 0.020f * Mathf.Abs(r.Next());
        }

        HighPass(d, rate, 1400f);
        LowPass(d, rate, 7000f);
        Normalise(d, 0.7f);
        return Finish("Skitter", d, rate);
    }

    /// <summary>Frost: the crazing of something freezing — a noise burst pushed through a high,
    /// fast-decaying resonance, so it reads as brittle rather than as a hiss.</summary>
    private static AudioClip MakeFrost(int rate)
    {
        int n = (int)(rate * 0.30f);
        var d = new float[n];
        var r = new Rng(0x1CE0u);

        // three ticks, each a filtered impulse train
        for (int k = 0; k < 3; k++)
        {
            int at = (int)(rate * (0.005f + k * 0.075f));
            float f = 2600f + k * 900f;
            float amp = 1f - k * 0.28f;
            for (int i = 0; at + i < n && i < rate * 0.10f; i++)
            {
                float tt = i / (float)rate;
                d[at + i] += Mathf.Sin(2f * Mathf.PI * f * tt) * Mathf.Exp(-52f * tt) * amp * 0.6f;
                d[at + i] += r.Next() * Mathf.Exp(-140f * tt) * amp * 0.4f;
            }
        }

        HighPass(d, rate, 900f);
        Normalise(d, 0.75f);
        return Finish("Frost", d, rate);
    }

    // ---- loops ----------------------------------------------------------------------------------

    /// <summary>Earth: a slow settling under the floor. Two very low sines a few Hz apart (so they
    /// beat, and the beat is the "movement") plus low-passed noise for body.</summary>
    private static AudioClip MakeRumble(int rate)
    {
        int n = rate * 6;
        var d = new float[n];
        var r = new Rng(0xEA27Fu);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            d[i] = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.5f
                   + Mathf.Sin(2f * Mathf.PI * 47.5f * t) * 0.35f
                   + r.Next() * 0.5f;
        }

        LowPass(d, rate, 110f);
        LoopFade(d, rate / 2);
        Normalise(d, 0.8f);
        return Finish("Rumble", d, rate);
    }

    /// <summary>The swamp at night: noise amplitude-modulated at insect rates. Three modulators at
    /// non-commensurate rates so the texture never settles into a pulse.</summary>
    private static AudioClip MakeChirr(int rate)
    {
        int n = rate * 7;
        var d = new float[n];
        var r = new Rng(0xC317Fu);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float m = 0.55f
                      + 0.20f * Mathf.Sin(2f * Mathf.PI * 17.3f * t)
                      + 0.14f * Mathf.Sin(2f * Mathf.PI * 23.9f * t)
                      + 0.11f * Mathf.Sin(2f * Mathf.PI * 31.1f * t);
            d[i] = r.Next() * m;
        }

        HighPass(d, rate, 2200f);
        LowPass(d, rate, 6500f);
        LoopFade(d, rate / 2);
        Normalise(d, 0.55f);
        return Finish("Chirr", d, rate);
    }

    /// <summary>A wisp: two sines 0.7 Hz apart, beating. Barely a sound — it is meant to be the
    /// thing you only notice when it stops.</summary>
    private static AudioClip MakeHum(int rate)
    {
        int n = rate * 5;
        var d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            d[i] = Mathf.Sin(2f * Mathf.PI * 196f * t) * 0.5f
                   + Mathf.Sin(2f * Mathf.PI * 196.7f * t) * 0.5f
                   + Mathf.Sin(2f * Mathf.PI * 392f * t) * 0.10f;
        }
        LoopFade(d, rate / 2);
        Normalise(d, 0.6f);
        return Finish("Hum", d, rate);
    }

    // ---- the haunt cues ---------------------------------------------------------------------------

    /// <summary>
    /// Rope or old timber taking weight. A creak is STICK-SLIP: the load builds, the fibres let go
    /// a little, it builds again. So this is a train of short filtered bursts whose spacing
    /// SHORTENS as the load settles, under a slow swell — never one continuous groan, which is what
    /// makes a synthetic creak sound like a synthesizer.
    /// </summary>
    private static AudioClip MakeCreak(int rate)
    {
        int n = (int)(rate * 1.3f);
        var d = new float[n];
        var r = new Rng(0xC2EA00u);

        float t = 0.05f;
        float gap = 0.115f;
        while (t < 1.20f)
        {
            int at = (int)(t * rate);
            float f = 210f + 130f * Mathf.Abs(r.Next());
            float amp = 0.35f + 0.65f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.2f));
            for (int i = 0; at + i < n && i < rate * 0.09f; i++)
            {
                float tt = i / (float)rate;
                d[at + i] += Mathf.Sin(2f * Mathf.PI * f * tt) * Mathf.Exp(-24f * tt) * amp * 0.5f;
                d[at + i] += r.Next() * Mathf.Exp(-70f * tt) * amp * 0.25f;
            }
            gap *= 0.90f;                       // the slips come closer together as it settles
            t += gap * (0.75f + 0.5f * Mathf.Abs(r.Next()));
        }

        LowPass(d, rate, 2400f);
        Normalise(d, 0.7f);
        return Finish("Creak", d, rate);
    }

    /// <summary>
    /// A breath that is not yours: one slow out-breath. Noise through a resonance that MOVES the
    /// way a throat does — that movement is the whole difference between a breath and a hiss, and
    /// it is why this is worth 30 lines rather than being a filtered-noise blip.
    /// </summary>
    private static AudioClip MakeBreath(int rate)
    {
        int n = (int)(rate * 1.0f);
        var d = new float[n];
        var r = new Rng(0xB2EA7u);

        // two swept one-pole resonators, tracked by hand so the sweep is per sample
        float y1 = 0f, y2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 1.0f;
            // the envelope of an out-breath: fast in, long out
            float env = Mathf.Min(1f, u / 0.14f) * Mathf.Exp(-2.1f * u);
            // 620 -> 380 Hz: the mouth closing
            float f = 620f - 240f * u;
            float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * f / rate));
            float w = r.Next();
            y1 += a * (w - y1);
            y2 += a * (y1 - y2);
            d[i] = (y2 * 2.6f + w * 0.10f) * env;
        }

        HighPass(d, rate, 140f);
        Normalise(d, 0.55f);
        return Finish("Breath", d, rate);
    }

    /// <summary>Cloth, or a wet palm, on stone. Band-limited noise whose band sweeps down as the
    /// contact slows, with a slight roughness from a low AM.</summary>
    private static AudioClip MakeDrag(int rate)
    {
        int n = (int)(rate * 1.4f);
        var d = new float[n];
        var r = new Rng(0xD2A6u);

        float y = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 1.4f;
            float env = Mathf.Min(1f, u / 0.22f) * Mathf.Min(1f, (1f - u) / 0.30f);
            float f = 3200f - 2100f * u;                       // the band sliding down
            float a = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * f / rate));
            float w = r.Next();
            y += a * (w - y);
            float rough = 0.80f + 0.20f * Mathf.Sin(2f * Mathf.PI * 31f * t);
            d[i] = (w - y) * env * rough;                      // (w - y) is the high-passed part
        }

        HighPass(d, rate, 600f);
        Normalise(d, 0.5f);
        return Finish("Drag", d, rate);
    }

    /// <summary>One fly. A buzz is a harmonic-rich tone whose pitch WANDERS (the insect is
    /// manoeuvring) with amplitude that comes and goes as it turns.</summary>
    private static AudioClip MakeFly(int rate)
    {
        int n = (int)(rate * 1.6f);
        var d = new float[n];
        float phase = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 1.6f;
            float env = Mathf.Min(1f, u / 0.18f) * Mathf.Min(1f, (1f - u) / 0.22f);
            float f = 168f + 22f * Mathf.Sin(2f * Mathf.PI * 1.7f * t) + 11f * Mathf.Sin(2f * Mathf.PI * 4.3f * t);
            phase += 2f * Mathf.PI * f / rate;
            // a sawtooth-ish stack: wings are not a sine
            float s = Mathf.Sin(phase) + 0.5f * Mathf.Sin(phase * 2f) + 0.30f * Mathf.Sin(phase * 3f)
                      + 0.18f * Mathf.Sin(phase * 4f);
            float turn = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 0.9f * t + 1.1f);
            d[i] = s * env * turn * 0.3f;
        }

        Normalise(d, 0.45f);
        return Finish("Fly", d, rate);
    }

    /// <summary>
    /// THE BOOKSHELF GOING OVER, heard from the other side of a cellar. Deliberately NOT a crash:
    /// no clatter, no top end, no fast attack. A low mass arriving, with the air in front of it —
    /// distance is modelled by removing the high frequencies, which is what distance actually does.
    /// </summary>
    private static AudioClip MakeFall(int rate)
    {
        int n = (int)(rate * 1.9f);
        var d = new float[n];
        var r = new Rng(0xFA11u);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            // the topple: a slow lean (a rising rustle) then the arrival at ~0.85 s
            float lean = Mathf.Min(1f, t / 0.85f);
            d[i] += r.Next() * lean * lean * 0.22f;

            if (t >= 0.85f)
            {
                float tt = t - 0.85f;
                float env = Mathf.Exp(-3.4f * tt);
                d[i] += Mathf.Sin(2f * Mathf.PI * 58f * tt) * env * 0.85f;
                d[i] += Mathf.Sin(2f * Mathf.PI * 86f * tt) * env * 0.45f;
                d[i] += r.Next() * Mathf.Exp(-9f * tt) * 0.35f;
            }
        }

        // the distance filter — a cellar's worth of stone and air between it and the ear
        LowPass(d, rate, 420f);
        Normalise(d, 0.8f);
        return Finish("Fall", d, rate);
    }

    /// <summary>...and the righting. Slower, quieter, and with the arrival at the END rather than
    /// the beginning — a thing standing itself back up is the half you are not supposed to be
    /// comfortable with.</summary>
    private static AudioClip MakeSettle(int rate)
    {
        int n = (int)(rate * 2.8f);
        var d = new float[n];
        var r = new Rng(0x5E77u);

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float u = t / 2.8f;
            // a long, uneven scrape of wood coming up off stone
            float grind = 0.35f + 0.25f * Mathf.Sin(2f * Mathf.PI * 3.1f * t) + 0.18f * Mathf.Sin(2f * Mathf.PI * 7.7f * t);
            d[i] += r.Next() * grind * Mathf.Min(1f, u / 0.35f) * 0.30f;

            if (t >= 2.35f)
            {
                float tt = t - 2.35f;
                float env = Mathf.Exp(-5.5f * tt);
                d[i] += Mathf.Sin(2f * Mathf.PI * 64f * tt) * env * 0.40f;   // it comes to rest
            }
        }

        LowPass(d, rate, 520f);
        Normalise(d, 0.5f);
        return Finish("Settle", d, rate);
    }
}
