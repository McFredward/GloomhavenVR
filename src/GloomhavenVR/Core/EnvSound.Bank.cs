using UnityEngine;

// The generator draws below are written `new Rng(...)`, and they stay that way: the struct moved to
// Core/EnvSoundSchedule.cs so the one part of this file with a TERMINATION property could be
// compiled into the wire tests without the Unity audio module, and an alias keeps every call site —
// and therefore every draw sequence, and therefore every clip — exactly as it was.
using Rng = GloomhavenVR.Core.EnvSoundRng;

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
/// <para><b>COST.</b> One shared 8 s noise bed plus fifteen shorter clips (thirteen named plus the
/// drip's two extra realisations) at the output sample rate, mono. That is 36.9 s of audio, so
/// ~7.1 MB of float data at 48 kHz — the <see cref="Build"/> log line prints the real figure for
/// the device's own rate rather than this estimate. Generated once per session on the frame the
/// room is first placed, measured at ~112 ms on the Quest 3 rig, and never touched again.
/// ModBuild 148's three rebuilt clips move that by <b>+1.3%</b> — the frost costs +0.70 ms and the
/// settle +0.55, and the bookshelf's fall gives back -0.46 because it is now half as long (timed
/// outside Unity against the same arithmetic, 200 runs each). Mono is not a saving but a
/// REQUIREMENT: Unity refuses to spatialise a stereo clip, and every clip here is meant to come
/// from a place.</para>
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

    /// <summary>A single water drop landing in a shallow puddle. One-shot, ~0.28 s. This is
    /// VARIANT 0 of three — the "ordinary" drop; see <see cref="DripVariant"/> for why there are
    /// three and <see cref="MakeDrips"/> for what physically separates them.</summary>
    internal static AudioClip? Drip { get; private set; }

    /// <summary>The rat, heard: "Mäusepiepen" (user, verbatim). One-shot, ~0.09 s.</summary>
    internal static AudioClip? Squeak { get; private set; }

    /// <summary>The rat's feet on flagstones — a run of tiny dry ticks. One-shot, ~0.55 s.</summary>
    internal static AudioClip? Skitter { get; private set; }

    /// <summary>Frost: a short BURST OF BRITTLE CRACKS — ice crazing, rebuilt from the fracture
    /// physics in ModBuild 148 after the user reported the shipped one as "super nervig". One-shot,
    /// ~0.42 s, and almost all of that is the silence between the cracks. See
    /// <see cref="MakeFrost"/>.</summary>
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

    /// <summary>One fly, close, looping past. AM/FM buzz. ~1.6 s.
    ///
    /// <para><b>NO CARD DRAWS THIS TODAY, and it is kept deliberately rather than by neglect.</b>
    /// It was authored for the cellar's old card 2 — a face at floor level among the barrels — and
    /// ModBuild 147 replaced that apparition with the stair-top DOOR, which wants a creak. The clip
    /// costs 1.6 s of PCM and about 1.5 ms of the bank's ~112 ms to build, and the card catalogue
    /// has been re-cut in two of the last three rounds; deleting a working generator that the next
    /// re-cut may well want back is a worse trade than the 1.5 ms. If a round goes by with the
    /// catalogue stable and nothing claiming it, delete <see cref="MakeFly"/>, this property and
    /// <see cref="EnvSoundClip.Fly"/> together.</para></summary>
    internal static AudioClip? Fly { get; private set; }

    /// <summary>THE BOOKSHELF ARRIVING ON THE FLOOR. Rebuilt in ModBuild 148: the impact is now at
    /// <c>t = 0</c> of the clip rather than 0.85 s into it, because the caller schedules it on the
    /// shelf's ACTUAL arrival and a clip with its own run-up cannot be placed on an instant. ~0.9 s.
    /// See <see cref="MakeFall"/>.</summary>
    internal static AudioClip? Fall { get; private set; }

    /// <summary>...and the same mass coming back up, slower and quieter, which is the more
    /// unsettling half. ~2.8 s, with its one contact at 1.162 s — the mirror of the fall's rebound,
    /// which is what the recovery curve actually does. See <see cref="MakeSettle"/>.</summary>
    internal static AudioClip? Settle { get; private set; }

    private static bool _built;

    /// <summary>True once <see cref="Build"/> has produced a usable bank.</summary>
    internal static bool Ready => _built && Bed != null;

    /// <summary>
    /// The three drops, indexed 0..2. <see cref="Drip"/> is element 0.
    ///
    /// <para>WHY AN ARRAY AND NOT THREE MORE <see cref="EnvSoundClip"/> MEMBERS. The enum exists so
    /// that <see cref="EnvSound"/>'s BED TABLE can be written as data ("this node gets Chirr") and so
    /// a log line can name the clip a cue chose. Nothing in that table would ever name
    /// <c>Drip2</c>/<c>Drip3</c>: the three are not three different sounds a designer picks between,
    /// they are one sound whose realisation is drawn per event. Putting them in the enum would
    /// advertise a choice that no caller has, and would leave <see cref="Bank"/> — the ONE
    /// translation, and the thing that keeps the table honest — with two cases nobody can reach.
    /// So the enum keeps one <c>Drip</c>, the accessor below owns the draw, and there is still
    /// exactly one place where "which drop" is decided.</para>
    /// </summary>
    private static readonly AudioClip?[] _drips = new AudioClip?[3];

    /// <summary>
    /// One of the three drops. <paramref name="which"/> is clamped rather than validated: the caller
    /// derives it from a hash, and a hash that returns exactly 1.0 (which
    /// <c>Haunt.Hash</c>'s <c>frac</c> is documented never to do, but which is one refactor away from
    /// being possible) must degrade to "the last drop" and not to an
    /// <see cref="System.IndexOutOfRangeException"/> inside the environment driver's per-frame path.
    /// Null before <see cref="Build"/>, and null forever on a device where the bank failed —
    /// <c>PlayShot</c> already treats a null clip as "that one cue is silent".
    /// </summary>
    internal static AudioClip? DripVariant(int which) =>
        _drips[which < 0 ? 0 : which >= _drips.Length ? _drips.Length - 1 : which];

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

        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            int rate = Rate;

            Bed = MakeBed(rate);
            Drip = MakeDrips(rate);   // fills _drips and hands back element 0
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

            // ONE LINE, ONCE PER SESSION, AND IT EARNED ITS PLACE. This synthesis runs on the main
            // thread on the frame the room is first placed, and until ModBuild 146 it wrote nothing
            // at all on the way through — so when MakeCreak spun forever there (see
            // EnvSoundSchedule), Player.log ended on the environment's "ROOM placed" and the next
            // suspect was every one of the dozen things that also start on that frame. A bank that
            // says it finished, and how long it took, turns that whole class of report into one
            // glance: the line is there and the freeze is elsewhere, or the line is missing and it
            // is here.
            VRLog.Info("Core", $"ENV SOUND bank synthesized in {watch.Elapsed.TotalMilliseconds:F0} ms — " +
                               $"{_made.Count} clip(s) at {rate} Hz, ~{TotalBytes() / 1024f / 1024f:F1} MB, " +
                               "built once per session on the frame the room is first placed and reused by " +
                               "every emitter and every cue from then on.");
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

        // The three drops are IN _made as well (Finish put them there), so this drops the
        // references only — the destroy loop below is still the single place anything is destroyed.
        // Clearing it here and not there is what keeps that true.
        System.Array.Clear(_drips, 0, _drips.Length);

        foreach (AudioClip? c in _made)
        {
            if (c != null)
                Object.Destroy(c);
        }
        _made.Clear();
    }

    private static readonly System.Collections.Generic.List<AudioClip?> _made = new();

    /// <summary>Bytes of PCM the bank holds, for the one build log line. Mono 32-bit float, which is
    /// what <see cref="Finish"/> creates.</summary>
    private static long TotalBytes()
    {
        long total = 0;
        foreach (AudioClip? c in _made)
        {
            if (c != null)
                total += (long)c.samples * c.channels * 4;
        }
        return total;
    }

    // =============================================================================================
    //  THE GENERATORS
    // =============================================================================================

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

    // =============================================================================================
    //  THE DRIP — rebuilt from the physics up. ModBuild 147.
    // =============================================================================================
    //
    //  THE USER REPORT, verbatim: "Die Pfütze ist zu extrem bzw. reagiert zu extrem den
    //  Wassertropfen. Außerdem gefällt mir das Geräusch nicht." The first sentence is the PUDDLE's
    //  reaction and belongs to the shader; this is the second sentence, and only the second.
    //
    //  WHAT THE OLD CLIP ACTUALLY WAS, in the units the numbers mean rather than in the numbers.
    //  Minnaert's relation for an air bubble in water at one atmosphere is
    //
    //      f0 = (1 / 2*pi*r) * sqrt(3*gamma*P0 / rho)   ~=   3.26 / r      (Hz with r in metres)
    //
    //  so a frequency IS a bubble radius and nothing else. Read that way, the shipped clip said:
    //    * F0 = 720 Hz          -> a 4.5 mm bubble. Plausible on its own.
    //    * Rise = 2.35          -> ...which then climbs to 1692 Hz, i.e. shrinks to a 1.9 mm bubble.
    //                              That is 42% of the radius, so SEVEN PER CENT of the volume, and
    //                              the clip claims it happens inside ~120 ms. Nothing does that. A
    //                              real drip's Minnaert tone rises by some tens of per cent over its
    //                              life (the bubble loses a little gas and drifts toward the free
    //                              surface, which lightens the load on it); it does not sweep an
    //                              octave and a fifth. An octave-and-a-fifth glide on a pure sine is
    //                              the definition of a synthesizer patch, and it is precisely what
    //                              the ear was reporting as "a bell".
    //    * Decay = 17           -> a 59 ms time constant, so the tone was still at -30 dB after a
    //                              fifth of a second. It RANG. It rang, in a room, every 2.85 s.
    //    * 6 ms of noise at 0.55 -> and this was the entire impact. Six milliseconds of broadband is
    //                              a CLICK, not a splash; it reads as the attack transient of the
    //                              tone that follows it, which is exactly how a struck bell is put
    //                              together. So the clip had the bell's envelope AND the bell's
    //                              spectrum, and the one thing it had almost none of is the thing a
    //                              drop landing in a puddle mostly IS.
    //    * HighPass 180 + Normalise 0.9 -> nothing removed above, everything pushed to the ceiling.
    //
    //  ...and one structural error underneath all of that: the sine started at FULL AMPLITUDE at
    //  t = 0, simultaneous with the transient. The bubble does not exist at the moment of impact.
    //  The drop has to open a crater first, and the crater has to pinch off behind it; only then is
    //  there a bubble to ring. That pinch-off is milliseconds AFTER contact. Starting the two
    //  together fuses them into one event with an attack and a tail — which the ear names "bell".
    //  Separating them by ~9 ms is most of what makes the same two ingredients name "plop" instead.
    //
    //  WHAT THIS ROOM'S DROP ACTUALLY IS. The content lane's own numbers say it: the drop forms on a
    //  plank at DripY0 = 3.252 and lands on water at DripY1 = 0.008 (BuildEnvironmentRooms.cs
    //  :1705-6, mirrored in EnvSound.cs), so it falls 3.244 m in free fall and arrives at
    //  sqrt(2 * 9.81 * 3.244) = 7.98 m/s. That is fast, and it lands in a PUDDLE — a film of water a
    //  few millimetres deep lying on a flagstone. Three consequences, and each one is a term below:
    //
    //    1. THE FLOOR STOPS IT, not the water. Eight metres per second of drop is arrested inside a
    //       couple of millimetres, against stone. Most of the energy leaves as a broadband SLAP
    //       (water thrown sideways, ~10-20 ms, no clean pitch) plus a very short low knock from the
    //       flagstone itself. The old clip's 6 ms tick was standing in for this and was far too
    //       brief and far too bright to do it.
    //    2. THE CAVITY IS WIDE AND SHALLOW, because it bottoms out on the stone before it can go
    //       deep. A wide shallow cavity pinches off a BIG bubble, and a big bubble is a LOW one:
    //       ~430 Hz is a 7.6 mm bubble, which is what a puddle of this depth plausibly entrains. The
    //       shipped 720 -> 1692 Hz was the sound of a deep, narrow entrainment — a sink, a bucket,
    //       a cave pool — not of 3 mm of water on a cellar floor.
    //    3. IT DIES FAST. A bubble oscillating within a few millimetres of a rigid floor AND within
    //       a few millimetres of the free surface loses energy into both. See BubbleQ.
    //
    //  REJECTED, in order of how tempting they were:
    //    * SCALING THE EXISTING CONSTANTS (F0 down, Decay up, done). It would not have worked, and
    //      that is a statement about the shape rather than about the taste: the clip's two real
    //      defects are the MISSING slap and the SIMULTANEITY of impact and ring, and no multiplier
    //      on F0/Rise/Decay reaches either. A quieter, lower bell is still a bell.
    //    * A CC0 RECORDING. The class doc weighs this in general; for THIS clip it is also the wrong
    //      answer specifically. A drip is one of the few sounds that is genuinely a small number of
    //      resonances plus an impact, so the arithmetic is an honest model rather than an imitation
    //      — and a recorded drip brings a recorded ROOM with it, which would then be heard inside
    //      our room. (The squeak remains the one clip worth replacing; see the class doc.)
    //    * SYNTHESIZING PER SHOT so every drop is unique. It would have to run on the audio thread
    //      or allocate a buffer per event, both of which this feature is built not to do. Three
    //      baked realisations cost 84 kB once and nothing per shot.
    //    * WIDER PITCH JITTER instead of the three realisations. Rejected on physics: the playback
    //      pitch knob shifts the WHOLE clip, so it transposes the flagstone and the slap along with
    //      the bubble — and the floor does not change note between drops. It is also the most
    //      recognisable synthetic-audio tell there is; the ear hears "one sample, transposed" within
    //      three or four repeats, which at a 2.85 s period is under fifteen seconds.
    //    * DELETING THE DRIP SOUND. Considered seriously under "dezent", and rejected: the picture
    //      shows a visible drop hitting a visible puddle every 2.85 s, and a visible impact with no
    //      sound reads as a bug rather than as restraint. The answer to "too noticeable" is small,
    //      not absent — see the level note in EnvSound.TickDrip.

    /// <summary>
    /// The three drops. Fills <see cref="_drips"/> and returns element 0, which is also
    /// <see cref="Drip"/>.
    ///
    /// <para><b>WHY THREE, and why they differ in TIMBRE rather than in pitch.</b> Successive drops
    /// off the same plank are almost identical in mass and in fall height — the one thing that is
    /// genuinely different each time is WHAT THE SURFACE DOES, because the surface is still moving
    /// from the last drop and the puddle is not the same depth in two places. The visible
    /// consequence of that is the bubble: sometimes the cavity pinches off a big one, sometimes a
    /// smaller one, and OFTEN IT PINCHES OFF NOTHING AT ALL and the drop is only its impact. That
    /// last case is not a degenerate variant, it is the common one in shallow water, and it is the
    /// single most useful thing in this whole change for the standing "dezent" rule: one drop in
    /// three now has no tone in it whatsoever, so there is nothing for the ear to latch onto and
    /// count.</para>
    ///
    /// <para><b>THE PEAKS ARE NOT EQUAL, and that is load-bearing.</b> <see cref="Normalise"/> sets
    /// each clip's peak independently, so if all three were normalised to the same number the
    /// quiet variant would come back at exactly the loudness of the loud one and the variation
    /// built above would be erased on the last line of the generator. The peaks below therefore
    /// carry the RELATIVE loudness of the three realisations explicitly: 0.78 / 0.52 / 0.70.</para>
    ///
    /// <para><b>MEASURED, off the finished buffers</b> (the generator was run outside Unity against
    /// the same arithmetic, because none of this is checkable by ear from a build machine):</para>
    /// <code>
    ///   variant     peak at   audible to -40 dB   RMS      impact leads ring by
    ///   ordinary     3.8 ms        126 ms         0.105          +2.4 dB
    ///   no bubble    0.9 ms         57 ms         0.046         +10.5 dB
    ///   small        1.5 ms         87 ms         0.075          +3.6 dB
    ///   SHIPPED     15   ms        244 ms         0.110       ring LOUDER than impact
    /// </code>
    /// <para>Three things in that table are the whole change: every variant now PEAKS IN ITS FIRST
    /// FOUR MILLISECONDS (the old clip peaked at 15 ms, on the tone), every variant is audible for
    /// well under half as long, and the average RMS across the three is 0.075 against 0.110 —
    /// -3.3 dB of loudness before the level in <c>EnvSound.TickDrip</c> is touched at all.</para>
    ///
    /// <para><b>COST.</b> 3 x 0.28 s where there was 1 x 0.40 s: 40 320 samples against 19 200, so
    /// ~84 kB more PCM against the bank's ~2 MB, and roughly two more milliseconds of synthesis
    /// against the bank's measured 112 ms. Per sample this is cheaper than the bed (one sin and two
    /// exp against three filter poles), and the drip is 0.8 s of the bank's ~26 s of audio either
    /// way. The log line in <see cref="Build"/> reports the real figure.</para>
    ///
    /// <para><b>TERMINATION.</b> Nothing here has one to prove. Every loop is a <c>for</c> over
    /// <c>n = (int)(rate * seconds)</c>, an int fixed before the loop starts; there is no float
    /// accumulator in any condition and no burst train (so no <see cref="EnvSoundSchedule"/> call
    /// and nothing new for the wire vectors to hold). This is the file ModBuild 145 froze the game
    /// in — see <see cref="EnvSoundSchedule"/> — so the property is stated rather than assumed.</para>
    /// </summary>
    private static AudioClip MakeDrips(int rate)
    {
        // VARIANT 0 — THE ORDINARY DROP. A wide shallow cavity pinches off a 7.6 mm bubble.
        //
        // THE ONE BALANCE THAT DECIDES WHETHER THIS IS A PLOP OR A PLINK: the loudest instant in the
        // buffer has to be the IMPACT, not the ring. It is a measurable property, not a taste — walk
        // the finished buffer and ask where its peak is. At bubbleAmp 0.30 the peak lands at 15 ms,
        // i.e. on the bubble, and the clip is a struck tone with a bit of noise in front of it (which
        // is exactly what shipped). At 0.20 the peak moves to 3.8 ms and the ring sits 2.4 dB under
        // it: the impact leads and the tone colours it. That is the whole difference, and it is worth
        // re-measuring rather than re-tuning if anyone ever touches slapAmp or stoneAmp.
        _drips[0] = MakeDripVariant(rate, "Drip", 0xD819u,
                                    bubbleHz: 430f, bubbleAmp: 0.20f, bubbleRise: 1.16f,
                                    slapHz: 1500f, slapTau: 0.0045f, slapAmp: 0.85f,
                                    stoneHz: 168f, stoneTau: 0.011f, stoneAmp: 0.22f,
                                    peak: 0.78f);

        // VARIANT 1 — NO BUBBLE. The crater collapsed without closing over, so there is no
        // oscillator and no tone at all: an impact and the flagstone under it, nothing else. More of
        // the momentum reached the stone (stoneAmp up, and a slightly lower, longer knock because it
        // is a squarer hit), and the whole event is quieter — a drop that fails to entrain has spent
        // its energy on spray, which radiates poorly. THE ONE THAT MAKES THE DRIP STOP BEING AN
        // EVENT: no pitch means nothing to recognise, and a third of all drops now have none.
        _drips[1] = MakeDripVariant(rate, "Drip.NoBubble", 0xD81Au,
                                    bubbleHz: 0f, bubbleAmp: 0f, bubbleRise: 1f,
                                    slapHz: 1250f, slapTau: 0.0040f, slapAmp: 0.85f,
                                    stoneHz: 152f, stoneTau: 0.013f, stoneAmp: 0.30f,
                                    peak: 0.52f);

        // VARIANT 2 — A SMALLER BUBBLE, 5.3 mm: the drop caught a thinner part of the film. Higher,
        // and by BubbleQ's constant-Q argument also shorter in absolute time. Quieter tone too — a
        // bubble is a monopole, so its output goes with the volume it displaces. 0.15 against 0.20
        // is a far gentler ratio than the (7.6/5.3)^3 = 2.9 that would imply, and deliberately: the
        // two bubbles are not the same bubble at two sizes, they are two different collapses, and the
        // one that pinched off a small bubble put more of the crater's energy into it. Taking the
        // cube law literally would make this variant's tone inaudible, which is not variation, it is
        // a second copy of variant 1.
        _drips[2] = MakeDripVariant(rate, "Drip.Small", 0xD81Bu,
                                    bubbleHz: 620f, bubbleAmp: 0.15f, bubbleRise: 1.22f,
                                    slapHz: 1700f, slapTau: 0.0042f, slapAmp: 0.80f,
                                    stoneHz: 175f, stoneTau: 0.010f, stoneAmp: 0.20f,
                                    peak: 0.70f);

        return _drips[0]!;
    }

    /// <summary>Seconds between the impact and the bubble's first ring: the crater has to open and
    /// its neck has to close behind the drop before there is anything to oscillate. Milliseconds,
    /// but the ear reads THIS gap as the difference between a wet event and a struck one — see the
    /// block comment above on why the old clip started both at t = 0.</summary>
    private const float DripRingDelay = 0.009f;

    /// <summary>
    /// The bubble's quality factor, and the ONE number the three decay rates are derived from
    /// instead of being three separate tunings.
    ///
    /// <para>Minnaert says <c>f * r</c> is a constant (~3.26 Hz.m), so <c>omega * r</c> is a
    /// constant too, so a bubble's RADIATION Q — which is <c>c_water / (omega * r)</c> — is the same
    /// ~72 for every bubble size. Its decay rate <c>alpha = pi * f / Q</c> therefore scales with its
    /// frequency: a higher bubble necessarily dies sooner in absolute time, and that relationship is
    /// not a taste decision to be re-made per variant. 36 is half the free-field radiation limit,
    /// which is what a rigid floor two millimetres below and a free surface two millimetres above
    /// cost it — the shallow puddle damps its own bubbles, which is the third consequence in the
    /// block comment. It gives the 430 Hz drop alpha = 37.5 (a 27 ms time constant, inaudible past
    /// ~150 ms) against the shipped 17 (59 ms, still ringing at 250 ms).</para>
    /// </summary>
    private const float DripBubbleQ = 36f;

    /// <summary>How fast the Minnaert tone climbs, per second. 26 puts the rise's time constant at
    /// 38 ms — the same order as the ring's own decay, i.e. the tone is still bending when it dies,
    /// which is what a rise sounds like as opposed to a glide that arrives somewhere.</summary>
    private const float DripRiseRate = 26f;

    /// <summary>
    /// One realisation of a drop: a SLAP, a flagstone KNOCK, and (optionally) a Minnaert BUBBLE that
    /// starts <see cref="DripRingDelay"/> after the other two.
    ///
    /// <para>Twelve parameters, named at both call sites, because the alternative is three
    /// near-copies of this function differing in constants — and the last time this file had three
    /// near-copies of a schedule, one of them froze the game.</para>
    /// </summary>
    /// <param name="bubbleHz">Minnaert frequency, i.e. bubble radius by another name (r ~ 3.26/f).
    /// ZERO means no bubble was entrained and the drop is only its impact.</param>
    /// <param name="bubbleAmp">Monopole strength — how much volume the bubble displaces. THE
    /// SENSITIVE ONE: it decides whether the finished buffer peaks on the impact or on the ring, and
    /// therefore whether the clip is a plop or a plink. See the note on variant 0.</param>
    /// <param name="bubbleRise">Where the tone ends up, as a multiple of where it started. Tens of
    /// per cent. The shipped 2.35 was the bell.</param>
    /// <param name="slapHz">Top of the slap's band. Water thrown sideways off stone is broadband but
    /// not bright — there is no hard edge anywhere in a puddle to make a click.</param>
    /// <param name="slapTau">Time constant of the slap, seconds. ~4 ms, so it is done in ~20 ms.</param>
    /// <param name="slapAmp">Slap level relative to the rest.</param>
    /// <param name="stoneHz">The flagstone's answer to being hit through 2 mm of water. Low.</param>
    /// <param name="stoneTau">...and heavily damped: a bedded flagstone is not a bell either.</param>
    /// <param name="stoneAmp">How much of the drop's momentum reached the stone.</param>
    /// <param name="peak">Normalisation target — the RELATIVE loudness of this realisation against
    /// the other two. See the note in <see cref="MakeDrips"/> on why these differ.</param>
    private static AudioClip MakeDripVariant(int rate, string name, uint seed,
                                             float bubbleHz, float bubbleAmp, float bubbleRise,
                                             float slapHz, float slapTau, float slapAmp,
                                             float stoneHz, float stoneTau, float stoneAmp,
                                             float peak)
    {
        // 0.28 s, and it is a bound rather than a length: the longest thing in here is the 430 Hz
        // bubble at alpha = 37.5, which is 100 dB down by 0.25 s. The buffer ends in silence, so a
        // one-shot needs no fade (see LoopFade's doc for who does).
        int n = (int)(rate * 0.28f);
        var d = new float[n];
        var r = new Rng(seed);

        // alpha = omega / (2Q) = pi*f/Q. Derived, not tuned — see DripBubbleQ.
        float bubbleDecay = Mathf.PI * bubbleHz / DripBubbleQ;

        // The slap's band, as two one-pole trackers run inline over the sample loop the way
        // MakeBreath and MakeDrag do theirs: yHi follows the noise up to slapHz, yLo follows it up
        // to 190 Hz, and the difference is the band between them. Band-passing the noise (rather
        // than the old clip's single high pass at the very end) is what turns a click into a splash
        // — a click is broadband BY DEFINITION, so the way to stop hearing one is to take the
        // extremes off the noise before anything else happens to it.
        float aHi = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * slapHz / rate));
        float aLo = Mathf.Clamp01(1f - Mathf.Exp(-2f * Mathf.PI * 190f / rate));
        float yHi = 0f, yLo = 0f;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;

            // ---- 1. THE SLAP. Zero-mean noise, so its envelope may start at 1 without a DC step;
            // an impact IS a discontinuity and does not want an attack. Fed as literal zero once the
            // envelope is spent rather than switching the filters off, because freezing the two
            // accumulators at their last value and continuing to read them would inject exactly the
            // constant offset this band-pass exists to remove.
            float w = t < 0.05f ? r.Next() * slapAmp * Mathf.Exp(-t / slapTau) : 0f;
            yHi += aHi * (w - yHi);
            yLo += aLo * (yHi - yLo);
            float sample = yHi - yLo;

            // ---- 2. THE FLAGSTONE. sin(wt)*exp(-t/tau) is the impulse response of a damped
            // oscillator, which is what a struck slab is; starting at sin(0) = 0 is not a fade, it
            // is the correct phase for something excited by a blow at t = 0.
            if (t < 0.09f)
                sample += Mathf.Sin(2f * Mathf.PI * stoneHz * t) * Mathf.Exp(-t / stoneTau) * stoneAmp;

            // ---- 3. THE BUBBLE, if one was entrained at all.
            float tb = t - DripRingDelay;
            if (bubbleAmp > 0f && tb > 0f)
            {
                // THE PHASE IS THE INTEGRAL OF THE FREQUENCY, and this comment survives the rewrite
                // because the trap it describes has not moved. The instantaneous frequency is
                //     f(tb) = bubbleHz * (1 + (Rise-1) * (1 - exp(-DripRiseRate*tb)))
                // and the naive sin(2*pi*f(tb)*tb) would sweep the phase at roughly TWICE the
                // intended rate — it multiplies the swept frequency by the elapsed time instead of
                // accumulating it — which turns a drip into a rising whistle. So the closed-form
                // integral of f is used instead.
                float phase = 2f * Mathf.PI * bubbleHz *
                              (tb + (bubbleRise - 1f) *
                                    (tb + (Mathf.Exp(-DripRiseRate * tb) - 1f) / DripRiseRate));

                // The bubble is not there yet at tb = 0 either: the neck takes a moment to close, so
                // the mode swells over ~3 ms rather than appearing. Without this the ring has an
                // attack of its own and reads as a second, smaller strike.
                float grow = 1f - Mathf.Exp(-tb * 350f);
                sample += Mathf.Sin(phase) * grow * Mathf.Exp(-bubbleDecay * tb) * bubbleAmp;
            }

            d[i] = sample;
        }

        // THE CEILING, and it is here for EnvSound's non-masking rule rather than for tone: item 2
        // of that class doc reserves roughly 1-4 kHz for speech and the game's UI transients, and
        // the shipped drip put both its click and the top of its glide straight into it. One pole at
        // 2 kHz on the finished buffer makes "there is nothing of ours up there" a property of the
        // clip instead of a property of the parameters above. It costs the tones nothing measurable
        // (620 Hz through it is -0.4 dB).
        LowPass(d, rate, 2000f);

        // ...and the floor, below everything real in here (the lowest knock is 152 Hz): it removes
        // the DC the one-pole low pass leaves behind and any sub-100 Hz content, which on a small
        // VR speaker is cone excursion that produces no sound.
        HighPass(d, rate, 110f);

        Normalise(d, peak);
        return Finish(name, d, rate);
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

    /// <summary>Small claws on stone: a run of 15 dry ticks, irregularly spaced (a real gait is
    /// not a metronome) and getting quieter as the animal goes. An even beat
    /// (<c>shrink = 1</c>) — an animal crossing a room does not accelerate.</summary>
    private static AudioClip MakeSkitter(int rate)
    {
        int n = (int)(rate * 0.55f);
        var d = new float[n];
        var r = new Rng(0x5C177E2u);

        var steps = new float[15];
        EnvSoundSchedule.SlipTrain(steps, 0.01f, 0.52f, shrink: 1f, jitter: 0.28f, seed: 0x5C177E2u);
        for (int s = 0; s < steps.Length; s++)
        {
            float t = steps[s];
            int at = (int)(t * rate);
            float amp = (1f - t / 0.55f) * (0.55f + 0.45f * Mathf.Abs(r.Next()));
            int len = (int)(rate * 0.006f);
            for (int i = 0; i < len && at + i < n; i++)
                d[at + i] += r.Next() * amp * Mathf.Exp(-i / (float)len * 5f);
        }

        HighPass(d, rate, 1400f);
        LowPass(d, rate, 7000f);
        Normalise(d, 0.7f);
        return Finish("Skitter", d, rate);
    }

    // =============================================================================================
    //  THE FROST — rebuilt from the fracture physics up. ModBuild 148.
    // =============================================================================================
    //
    //  THE USER REPORT, verbatim: "Der Sound vom Eis passt absolut garnicht. Ich dachte eher an ein
    //  dezentes Knacken von Eis oder einem 'freezing' sound, sehr dezent. Das was aktuell drin ist
    //  ist super nervig."  ("The ice sound does not fit at all. I was thinking more of a subtle
    //  CRACKING of ice or a 'freezing' sound, very subtle. What is in there now is super annoying.")
    //
    //  WHAT THE OLD CLIP ACTUALLY WAS, in the units the numbers mean. Three PURE SINES at 2600,
    //  3500 and 4400 Hz, each under exp(-52t):
    //    * Q = pi*f/alpha = pi * 2600 / 52 = 157. A wine glass is Q ~ 1000, a tuning fork ~10 000,
    //      a struck ceramic tile ~200. ICE IS NONE OF THOSE. A crack in ice is not a resonator being
    //      struck, it is a resonator being TORN, and the tear itself is the sound; whatever ring
    //      follows it is smothered by the medium the crack is propagating through. Q = 157 at
    //      2600 Hz means the tone was still at -30 dB after 66 ms and audible for a fifth of a
    //      second — long enough for the ear to assign it a PITCH, which is the single thing that
    //      most reliably turns an environmental noise into an instrument.
    //    * 2600 / 3500 / 4400 is a fixed set of three pitches, so every "crack" in the room was the
    //      same three-note chord. Two of them (3500, 4400) are a perfect fourth apart to within a
    //      quarter of a semitone.
    //    * struck at a FIXED 75 ms spacing. That is 13.3 Hz — a RHYTHM. Crazing is a cascade of
    //      independent stress-release events and has no beat at all.
    //    * ...and the clip could fire every 0.45 s at full Ice, with the widest rolloff of any
    //      one-shot in the bank (0.8-11 perceived metres), i.e. from anywhere in the room. A
    //      repeating three-note chord on a 0.45 s beat is not an ambience, it is a ringtone.
    //  All five properties are separate reasons for "super nervig", and all five are gone below.
    //
    //  WHAT ICE CRACKING PHYSICALLY IS, reasoned the way MakeDrips reasons about Minnaert.
    //  Ice is BRITTLE: it fails by cleavage, not by yielding. A crack nucleates at a flaw and runs
    //  at a large fraction of the Rayleigh wave speed — for ice c_R ~ 1.6-1.9 km/s, so a crack
    //  across a 5 mm facet is over in about 3 MICROSECONDS. Three consequences, and each one is a
    //  term below:
    //
    //    1. THE SOURCE IS A STEP IN STRESS, so its spectrum is BROADBAND with no line structure at
    //       all. A step's spectrum rolls off at 6 dB/octave above 1/(2*pi*t_rise), and t_rise here
    //       is microseconds, so within the audible band it is essentially FLAT. Broadband noise
    //       under a very fast decay — a CLICK — is the honest model, and the old clip's sines were
    //       the exact opposite of it.
    //    2. IT DOES NOT RING, because there is nothing left to ring. The energy goes into two new
    //       free surfaces and into the surrounding ice, which is a lossy polycrystal in contact
    //       with stone and water. What survives is a couple of cycles of whatever cavity the crack
    //       opened — see FrostRingQ, which is 6 against the shipped 157.
    //    3. THE SIZES FOLLOW A POWER LAW. Acoustic emission from a fracturing solid obeys a
    //       Gutenberg-Richter distribution — the same statistics as earthquakes, for the same
    //       reason (a scale-free network of flaws). Small events vastly outnumber large ones. That
    //       is what makes real crazing sound like crazing rather than like a drum: you hear a
    //       handful of faint ticks and, now and then, one that is properly loud. See
    //       FrostSizeExponent.
    //
    //  ...and the CADENCE and the LEVEL are handled in EnvSound.TickFrost, not here, because they
    //  are not properties of the clip: see the block comment there. In short, the interval became a
    //  Poisson waiting time with a mean of 2.6 s at full Ice instead of a fixed 0.45 s beat, and the
    //  reach came in from 11 perceived metres to 5.
    //
    //  REJECTED:
    //    * KEEPING THE SINES AND LOWERING THEIR Q. Q would have to fall by a factor of ~25 to stop
    //      being a pitch, at which point the sine contributes two cycles and is a colouration of a
    //      click rather than a tone — which is exactly what the ring term below is, so this is not
    //      so much rejected as taken to its conclusion and renamed.
    //    * MOVING THE BAND OUT OF 1-4 kHz ENTIRELY, to honour the class doc's speech/UI reserve by
    //      construction. It cannot be done and stay honest: a millimetre-scale source radiates with
    //      efficiency (ka)^2, so a crack CANNOT put its energy low, and taking the top off as well
    //      leaves nothing. The class doc's own escape clause is the right one and is used here
    //      instead — the reserve is about MASKING, masking is about duration, and a 14 ms transient
    //      that happens a handful of times a minute cannot mask a syllable. The old clip needed the
    //      escape clause and did not qualify for it (a 200 ms tone at a 0.45 s repeat is a texture);
    //      this one qualifies with room to spare.
    //    * THREE BAKED REALISATIONS, as MakeDrips has. Unnecessary here: ONE clip already contains
    //      seven independent cracks with power-law sizes and per-crack ring frequencies, so the
    //      within-clip variety a listener hears is larger than the between-variant variety would be.
    //    * A RECORDING. Same answer as the drip's, and for the sharper version of the same reason:
    //      what is wanted is a room-free transient, and every field recording of ice brings the lake
    //      it was recorded on with it.

    /// <summary>Cracks in one burst. A <c>for</c> over this count, and the times come from
    /// <see cref="EnvSoundSchedule.SlipTrain"/>, so the generator terminates by construction — see
    /// that file for the ModBuild 145 freeze this discipline exists to prevent.</summary>
    private const int FrostCrackCount = 7;

    /// <summary>The burst window, seconds. 0.354 s of crazing inside a 0.42 s buffer; the tail is
    /// the last crack's own decay and then silence.</summary>
    private const float FrostBurstFirst = 0.006f;
    private const float FrostBurstLast = 0.360f;

    /// <summary>The gaps GROW (&gt; 1), which is the opposite of <see cref="MakeCreak"/>'s. A creak
    /// accelerates because the load keeps building; a crazing DECELERATES because each crack
    /// relieves the stress that drove it, so the surface has to reload before the next one. 1.35
    /// spreads a 25 ms first gap out to 110 ms by the end of the burst — the burst thins out and
    /// stops rather than ending on a beat.</summary>
    private const float FrostCrackSpread = 1.35f;

    /// <summary>Fraction each individual gap is randomly stretched or squeezed by. 0.85 is nearly
    /// the whole gap and is deliberately extreme: the shipped clip's fixed 75 ms spacing was one of
    /// the five reasons it was "nervig", and a crazing has no beat WHATSOEVER.</summary>
    private const float FrostCrackJitter = 0.85f;

    /// <summary>Decay rate of a crack's broadband transient, per second. 560 is a 1.8 ms time
    /// constant, so a crack is finished inside about 8 ms. This is the number that makes it a
    /// fracture: the shipped 140 (7.1 ms) was already a scrape rather than a break, and the sines
    /// beside it at 52 (19 ms) were an instrument.</summary>
    private const float FrostCrackDecay = 560f;

    /// <summary>Rise rate of the transient, per second. 3000 is a 0.33 ms rise — not an attack, a
    /// DISPERSION: the crack's step arrives through ice and stone, and a solid path smears the
    /// wavefront by roughly this much over a few centimetres. Without it the burst starts on a
    /// sample discontinuity, which is a digital click rather than a physical one, and it is the
    /// harshest thing a short transient can have.</summary>
    private const float FrostCrackRise = 3000f;

    /// <summary>Quality factor of the little cavity a crack opens. SIX, against the shipped 157:
    /// alpha = pi*f/Q puts a 2500 Hz mode at a 0.76 ms time constant, i.e. under two cycles. Two
    /// cycles is below the ~4 the ear needs to assign a pitch, so this colours the click and does
    /// not become a note — which is the whole difference between "crazing" and "chime".</summary>
    private const float FrostRingQ = 6f;

    /// <summary>The band the per-crack ring is drawn from. A crack's cavity is millimetres, and its
    /// frequency is drawn PER CRACK rather than fixed, so no two cracks in the burst share a pitch
    /// — the shipped clip repeated one three-note chord every time it played.</summary>
    private const float FrostRingLo = 1600f;
    private const float FrostRingSpan = 1900f;

    /// <summary>Gutenberg-Richter, as one exponent. The size of crack <c>k</c> is <c>u^2.4</c> for
    /// uniform <c>u</c> — the inverse CDF of a power law — which puts the DISTRIBUTION's median at
    /// 0.5^2.4 = 0.19 of the maximum. What the seed actually drew is in the measured table on
    /// <see cref="MakeFrost"/>: three cracks that carry the burst and four that are barely
    /// there.</summary>
    private const float FrostSizeExponent = 2.4f;

    /// <summary>
    /// FROST — a burst of brittle fracture. Seven cracks, power-law sizes, irregular and widening
    /// gaps, each one a sub-10 ms broadband transient with a two-cycle colouration and no ring.
    /// See the block comment above for the physics and for what the shipped clip was instead.
    ///
    /// <para><b>MEASURED, off the finished buffer</b> (the generator was run outside Unity against
    /// the same arithmetic and the same seed, because none of this is checkable by ear from a build
    /// machine):</para>
    /// <code>
    ///   crack        1      2      3      4      5      6      7
    ///   at (ms)      6.0   42.6   90.4  161.0  225.9  280.8  360.0
    ///   gap (ms)      -    36.6   47.8   70.6   64.8   54.9   79.2
    ///   size       0.728  0.655  0.068  0.632  0.022  0.004  0.017
    ///   ring (Hz)   3133   1944   2018   2632   1852   3197   2017
    ///
    ///                peak at    RMS      audible to -40 dB
    ///   NEW           7.0 ms   0.0353        363 ms
    ///   SHIPPED       5.3 ms   0.0762        218 ms
    /// </code>
    /// <para>Three cracks carry the burst and four are barely there, which is the power law doing
    /// its job; no two share a pitch; the gaps widen from 37 ms to 79 ms with no beat anywhere in
    /// them. RMS is <b>-6.7 dB</b> against the shipped clip at the same gain, and that is before
    /// TickFrost's own cuts to the level, the reach and the rate.</para>
    ///
    /// <para><b>TERMINATION.</b> The times come from <see cref="EnvSoundSchedule.SlipTrain"/>, which
    /// terminates by construction, and every loop here is a <c>for</c> over an <c>int</c> fixed
    /// before it starts. There is no float accumulator in any condition. The train's own properties
    /// (spans its window, monotonic, in bounds, and — new for this caller — GAPS THAT WIDEN) are
    /// asserted in <c>tests/GloomhavenVR.WireTests/EnvSoundScheduleVectors.cs</c>.</para>
    /// </summary>
    private static AudioClip MakeFrost(int rate)
    {
        int n = (int)(rate * 0.42f);
        var d = new float[n];
        var r = new Rng(0x1CE0u);

        var cracks = new float[FrostCrackCount];
        EnvSoundSchedule.SlipTrain(cracks, FrostBurstFirst, FrostBurstLast,
                                   shrink: FrostCrackSpread, jitter: FrostCrackJitter, seed: 0x1CE0u);

        // 14 ms per crack: the transient is 100 dB down at 12 ms and the ring at 5, so this window
        // is a bound rather than a length. int, fixed before the loop.
        int len = (int)(rate * 0.014f);

        for (int k = 0; k < cracks.Length; k++)
        {
            int at = (int)(cracks[k] * rate);

            // THE SIZE, power-law. Abs() of the -1..1 draw is uniform on [0,1); raising it to
            // FrostSizeExponent is the inverse-CDF of the Gutenberg-Richter law, so this line IS
            // the statistics and not a taste.
            float amp = Mathf.Pow(Mathf.Abs(r.Next()), FrostSizeExponent);
            // The cavity this particular crack opened, and therefore its colour. Drawn per crack.
            float f = FrostRingLo + FrostRingSpan * Mathf.Abs(r.Next());
            // alpha = pi*f/Q — derived from the one Q above exactly as MakeDripVariant derives its
            // bubble decay from DripBubbleQ, so a higher-pitched crack necessarily dies sooner and
            // nobody has to tune two numbers to keep one relationship true.
            float ring = Mathf.PI * f / FrostRingQ;

            for (int i = 0; i < len && at + i < n; i++)
            {
                float tt = i / (float)rate;
                float rise = 1f - Mathf.Exp(-tt * FrostCrackRise);
                // 1. THE TEAR: broadband, because a step in stress has no line structure.
                d[at + i] += r.Next() * rise * Mathf.Exp(-tt * FrostCrackDecay) * amp;
                // 2. THE CAVITY: two cycles of colour, not a tone. 0.30 of the tear, so the click
                //    stays the loudest thing in every crack and the ear never gets a pitch to hold.
                d[at + i] += Mathf.Sin(2f * Mathf.PI * f * tt) * rise * Mathf.Exp(-tt * ring) * amp * 0.30f;
            }
        }

        // THE FLOOR, and it is physics rather than taste: an acoustically small source radiates
        // with efficiency (ka)^2, so a millimetre-scale crack is 12 dB/octave down as frequency
        // falls and simply cannot produce bass. Anything below 700 Hz in the buffer is an artefact
        // of the synthesis, and on a headset speaker it is cone excursion that makes no sound.
        HighPass(d, rate, 700f);
        // ...and the ceiling. A real crack has energy well above this; we choose not to emit it.
        // The class doc's non-masking budget is the reason and it is stated as a CHOICE, not
        // disguised as physics — 5.2 kHz keeps the crack brittle while taking off the very top,
        // which is where "harsh" lives and where the game's own UI transients are brightest.
        LowPass(d, rate, 5200f);
        // 0.62, down from 0.75. The peak of this buffer is ONE crack — the largest the power law
        // drew — so normalising to a lower ceiling lowers the whole burst, and the median crack
        // ends up at 0.19 of it.
        Normalise(d, 0.62f);
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
    ///
    /// <para>The 0.90 shrink is what makes it a creak rather than a knock-knock-knock, and it is
    /// also what froze the game on the loading screen in ModBuild 145 when it drove a
    /// <c>while</c> condition instead of a fixed count — see <see cref="EnvSoundSchedule"/>, which
    /// now owns the schedule and cannot fail to reach the end of its window.</para>
    /// </summary>
    private static AudioClip MakeCreak(int rate)
    {
        int n = (int)(rate * 1.3f);
        var d = new float[n];
        var r = new Rng(0xC2EA00u);

        var slips = new float[16];
        EnvSoundSchedule.SlipTrain(slips, 0.05f, 1.20f, shrink: 0.90f, jitter: 0.25f, seed: 0xC2EA00u);
        for (int s = 0; s < slips.Length; s++)
        {
            float t = slips[s];
            int at = (int)(t * rate);
            float f = 210f + 130f * Mathf.Abs(r.Next());
            float amp = 0.35f + 0.65f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.2f));
            for (int i = 0; at + i < n && i < rate * 0.09f; i++)
            {
                float tt = i / (float)rate;
                d[at + i] += Mathf.Sin(2f * Mathf.PI * f * tt) * Mathf.Exp(-24f * tt) * amp * 0.5f;
                d[at + i] += r.Next() * Mathf.Exp(-70f * tt) * amp * 0.25f;
            }
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

    // =============================================================================================
    //  THE BOOKSHELF'S IMPACT — rebuilt. ModBuild 148.
    // =============================================================================================
    //
    //  THE USER REPORT, verbatim: "Beim Umfallen des Regals sollte es schon ein Geräusch beim Impact
    //  auf dem Boden geben."  ("When the shelf falls over there really should be a sound at the
    //  impact on the floor.")
    //
    //  THERE ARE THREE INDEPENDENT REASONS HE HEARD NO IMPACT, and every one of them had to be
    //  fixed or the other two would still have hidden the result:
    //
    //    1. IT PLAYED 3.7 SECONDS TOO EARLY. This is the big one and it is not in this file — see
    //       EnvSound.TickHaunt. The cue fired at the event START plus a 0.15 s lead, and the clip
    //       put its thud 0.85 s in, so the thud landed ~1.0 s into an event whose shelf does not
    //       reach the floor until 4.68 s (EnvShelfTip.cginc: GHVR_TIP_FALL = 0.18 of a 26.002 s
    //       envelope). At 1.0 s the shelf has leaned about ONE DEGREE. A bang while a bookcase is
    //       still visibly upright does not read as an impact at all; it reads as "something else in
    //       the room made a noise", which is exactly the report.
    //    2. THE THUD WAS BELOW THE SPEAKERS. Two sines at 58 and 86 Hz behind a 420 Hz low pass,
    //       with nothing else in the buffer but a rustle. The rig is a Quest 3 over Virtual Desktop,
    //       and the Quest's own speakers are down hard below ~100 Hz — so on the hardware the report
    //       came from, more than half the clip's energy was inaudible BY CONSTRUCTION. The modes
    //       below sit at 78 and 135 Hz and the contact transient carries real content up to the low
    //       hundreds, so the event survives a small speaker.
    //    3. IT WAS A NOTE, NOT A THUD — the finding an earlier lane made and the reason the ear
    //       filed it under "sound effect" rather than "impact". 86 / 58 = 1.483, and an equal-
    //       tempered perfect fifth is 1.4983: the two partials were a fifth apart to within a fifth
    //       of a semitone. Worse, they shared one envelope, exp(-3.4t) — a 294 ms time constant, so
    //       the 58 Hz partial rang for SEVENTEEN CYCLES and the 86 Hz one for twenty-five. Pitch
    //       perception needs about four. A consonant interval held that long is a musical dyad, and
    //       the ear has no choice in the matter.
    //
    //  WHAT IT IS INSTEAD, as physics.
    //    * TWO CARCASS MODES IN AN INHARMONIC RATIO. sqrt(3) = 1.7320508 is 9.51 semitones — a
    //      quarter-tone off the major sixth below it and a quarter-tone off the minor seventh above
    //      it, i.e. as far from every simple interval as a ratio in that range can get. That is a
    //      deliberate anti-tuning and it is stated as one; the honest physical claim is only the
    //      weaker one, that a plywood-and-shelf carcass loaded with books has no reason to be
    //      harmonic and every reason not to be.
    //    * ...AND THEY BARELY RING, which matters more than the ratio. See FallCarcassQ: seven,
    //      derived once and applied to both modes, giving 2.2 cycles EACH (constant Q is constant
    //      cycles, which is why one number covers both). Under about four cycles there is no pitch
    //      to hear at all, so the interval question stops being decidable — which is a better fix
    //      than choosing a different interval, because it cannot be undone by a later retuning.
    //    * THE FLAGSTONE ANSWERS, at the SAME 160 Hz the drip's knock uses. There is one floor in
    //      this room and both impacts are hitting it; a slab's modal frequencies are a property of
    //      the slab and not of what fell on it, so the drip and the bookshelf agreeing about the
    //      floor is a correctness property, not a coincidence to be tuned away.
    //    * THE LOAD ARRIVES LATE. A carcass full of books is not one body: the boards stop first,
    //      the books keep going for another few centimetres and land on the shelves. That is a
    //      broadband slump 25 ms behind the contact, decaying over ~0.3 s, and it is the term that
    //      says "full bookcase" rather than "plank".
    //
    //  REJECTED:
    //    * KEEPING THE 0.85 s LEAN INSIDE THE CLIP and simply scheduling it 0.85 s early. It would
    //      work, and it would put a magic offset between two files that already have four numbers
    //      to keep in step. The clip is now an EVENT AT t = 0, which is a thing a scheduler can
    //      place; the run-up is a separate cue at a separate time (EnvSound.CueFor's card 5).
    //    * A CRASH — clatter, splintering, books tumbling. It is what a real bookcase does and it
    //      is ruled out by the standing rule twice over: the apparitions may never announce
    //      themselves, and this is the loudest thing in the environment. The 950 Hz ceiling keeps it
    //      a WUMPH.
    //    * DETUNING THE FIFTH AND LEAVING THE ENVELOPE. The interval is the symptom; the 294 ms
    //      decay is the disease. Any two partials held that long will be heard as an interval, and
    //      the next person to pick a ratio would be picking one for a chord that should not exist.

    /// <summary>
    /// The carcass's quality factor, and the ONE number both mode decays are derived from —
    /// the same discipline as <see cref="DripBubbleQ"/>.
    ///
    /// <para>SEVEN. A bookcase is plywood and pine with a hundred kilos of paper resting on it, and
    /// paper is about the most effective constrained-layer damper there is; loss factors for a
    /// loaded shelf are order 0.1, i.e. Q of order 5-10. alpha = pi*f/Q then puts the 78 Hz mode at
    /// a 28.6 ms time constant (2.2 cycles) and the 135 Hz mode at 16.5 ms (2.2 cycles as well —
    /// constant Q means constant CYCLES, which is why one number is enough for both). Under about
    /// four cycles the ear cannot extract a pitch, so this clip has a WEIGHT and no note. The
    /// shipped envelope was 294 ms flat for both partials: seventeen cycles and eleven.</para>
    /// </summary>
    private const float FallCarcassQ = 7f;

    /// <summary>The carcass's lowest mode, Hz, and the ratio of the second to it. 78 is chosen
    /// against the PLAYBACK as much as against the body: see reason 2 in the block comment — a
    /// headset speaker gives nothing back below ~100 Hz, so a thud whose fundamental is at 58 does
    /// not exist on the hardware the report came from.</summary>
    private const float FallModeHz = 78f;
    private const float FallModeRatio = 1.7320508f;

    /// <summary>The flagstone, hit hard. Deliberately the same slab as
    /// <c>MakeDripVariant</c>'s <c>stoneHz</c> band (152-175 Hz) and with the same Q — a floor's
    /// modes do not change with what lands on them.</summary>
    private const float FallSlabHz = 160f;
    private const float FallSlabTau = 0.0115f;

    /// <summary>The contact transient: 9 ms, so it is over in ~40. This is the "impact" the user
    /// asked for, and it is the loudest instant in the buffer by design — an arrival peaks on its
    /// contact, exactly as a drop does (see the plop-not-plink note in <see cref="MakeDrips"/>).
    /// </summary>
    private const float FallSlapTau = 0.009f;

    /// <summary>The books, arriving after the boards. 25 ms of lag and a 110 ms decay: the carcass
    /// stops, its load does not, and a few centimetres at arrival speed is tens of milliseconds.
    /// </summary>
    private const float FallLoadDelay = 0.025f;
    private const float FallLoadTau = 0.11f;

    /// <summary>
    /// THE BOOKSHELF ARRIVING. The impact is at <c>t = 0</c>: this clip is an EVENT, and the caller
    /// puts it on the frame the shelf actually reaches the floor (EnvSound.TickHaunt, from
    /// <c>EnvShelfTip.cginc</c>'s own phase constants). Everything before the arrival — the carcass
    /// creaking as it commits to the lean — is a different cue at a different time.
    ///
    /// <para>Played a second time, quieter and slightly sharper, for the ballistic rebound's second
    /// contact 0.624 s later; the shelf really does touch the floor twice and the curve says exactly
    /// when. See EnvSound's shelf schedule.</para>
    ///
    /// <para><b>MEASURED, off the finished buffers</b> (generated outside Unity against the same
    /// arithmetic and the same seed):</para>
    /// <code>
    ///                length   peak at    RMS     audible to -40 dB
    ///   NEW           0.90 s   1.4 ms   0.0548        294 ms
    ///   SHIPPED       1.90 s   854  ms  0.1195       1900 ms
    /// </code>
    /// <para>The peak moved from 854 ms to 1.4 ms — the clip now PEAKS ON ITS CONTACT, which is
    /// what an arrival does and what a run-up followed by a struck dyad does not. It is not a
    /// quieter impact, it is a shorter clip with the same impact in it: RMS over the shipped
    /// thud's own 300 ms window is 0.280, against 0.229 over the new clip's first 50 ms. What went
    /// away is the 1.6 s of ringing and rustle around it.</para>
    /// </summary>
    private static AudioClip MakeFall(int rate)
    {
        // 0.9 s, and it is a bound: the longest term is the load slump at 110 ms, which is 100 dB
        // down by 0.62 s. HALF the shipped length, because the shipped clip spent 0.85 s of it on a
        // run-up that is now a separate cue — so this rebuild makes the bank cheaper, not dearer.
        int n = (int)(rate * 0.9f);
        var d = new float[n];
        var r = new Rng(0xFA11u);

        // alpha = pi*f/Q for both modes, from the one Q. Derived, not tuned.
        float hi = FallModeHz * FallModeRatio;
        float aLo = Mathf.PI * FallModeHz / FallCarcassQ;
        float aHi = Mathf.PI * hi / FallCarcassQ;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;

            // ---- 1. THE CONTACT. Zero-mean noise, full amplitude at t = 0: an impact IS a
            // discontinuity and must not have an attack. This is the loudest instant in the buffer.
            float s = t < 0.06f ? r.Next() * Mathf.Exp(-t / FallSlapTau) * 0.90f : 0f;

            // ---- 2. THE FLAGSTONE, struck. sin(wt)*exp(-t/tau) starting at sin(0) = 0 is the
            // correct phase for something excited by a blow at t = 0, not a fade.
            if (t < 0.09f)
                s += Mathf.Sin(2f * Mathf.PI * FallSlabHz * t) * Mathf.Exp(-t / FallSlabTau) * 0.55f;

            // ---- 3. THE CARCASS. Two modes, inharmonic, and both dead inside three cycles.
            if (t < 0.35f)
            {
                s += Mathf.Sin(2f * Mathf.PI * FallModeHz * t) * Mathf.Exp(-aLo * t) * 0.80f;
                s += Mathf.Sin(2f * Mathf.PI * hi * t) * Mathf.Exp(-aHi * t) * 0.42f;
            }

            // ---- 4. THE LOAD. Broadband, late, and soft-edged — books do not click.
            float tl = t - FallLoadDelay;
            if (tl > 0f && tl < 0.55f)
                s += r.Next() * (1f - Mathf.Exp(-tl * 180f)) * Mathf.Exp(-tl / FallLoadTau) * 0.30f;

            d[i] = s;
        }

        // THE CEILING. 950 Hz, raised from the shipped 420. The old corner was justified as "a
        // cellar's worth of air between it and the ear" and that justification does not survive
        // contact with the picture: the room is a DIORAMA the player is leaning over, at two to
        // twenty-four perceived metres, not a sound from another building. 420 Hz removed
        // everything that says "wood" and left a pure boom. 950 keeps the contact's edge and still
        // guarantees no clatter — there is nothing of ours in the 1-4 kHz speech band, which is the
        // class doc's rule and the reason this is not simply opened up.
        LowPass(d, rate, 950f);
        // ...and the floor, below the lowest real mode: DC out, sub-audible excursion out.
        HighPass(d, rate, 55f);
        Normalise(d, 0.85f);
        return Finish("Fall", d, rate);
    }

    /// <summary>Seconds from the start of <see cref="Settle"/> to its one contact with the floor.
    ///
    /// <para>1.162 IS READ OFF THE CURVE, not chosen. <c>EnvShelfTip.cginc</c> plays the recovery as
    /// the fall run backwards at 0.537x, so the fall's 0.624 s rebound window becomes a 1.162 s one
    /// — and run backwards it means the shelf ROCKS UP 1.9 degrees over the first 0.58 s of the
    /// recovery, comes back down, TOUCHES, and only then lifts off. That touch is the single audible
    /// contact in the whole righting, and it is at 1.162 s. The shipped clip put a "comes to rest"
    /// thump at 2.35 s, which under this curve is a bang while the shelf is already in the air.</para>
    /// </summary>
    private const float SettleContactSeconds = 1.162f;

    /// <summary>...and the righting. Slower, quieter, and with its one contact 1.162 s in — the
    /// mirror of the fall's rebound, because the recovery IS the fall reversed (see
    /// <see cref="SettleContactSeconds"/>). A thing standing itself back up is the half you are not
    /// supposed to be comfortable with.</summary>
    private static AudioClip MakeSettle(int rate)
    {
        int n = (int)(rate * 2.8f);
        var d = new float[n];
        var r = new Rng(0x5E77u);

        // The same carcass on the same floor, so the same mode and the same Q as MakeFall — gently.
        float aLo = Mathf.PI * FallModeHz / FallCarcassQ;

        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            // a long, uneven grind of wood on stone
            float grind = 0.35f + 0.25f * Mathf.Sin(2f * Mathf.PI * 3.1f * t) + 0.18f * Mathf.Sin(2f * Mathf.PI * 7.7f * t);

            // TWO PHASES, and the seam between them is the contact. Before it the shelf is rocking
            // up onto its edge and back — the noise swells quadratically, which is what a body
            // rolling on a corner under a rising load does. After it the shelf leaves the floor and
            // the contact patch shrinks to nothing, so the grind dies away.
            float body = t < SettleContactSeconds
                ? (t / SettleContactSeconds) * (t / SettleContactSeconds) * 0.62f
                : Mathf.Exp(-(t - SettleContactSeconds) * 1.45f);
            d[i] += r.Next() * grind * body * 0.30f;

            if (t >= SettleContactSeconds && t < SettleContactSeconds + 0.30f)
            {
                float tt = t - SettleContactSeconds;
                d[i] += Mathf.Sin(2f * Mathf.PI * FallModeHz * tt) * Mathf.Exp(-aLo * tt) * 0.32f;
            }
        }

        LowPass(d, rate, 520f);
        Normalise(d, 0.5f);
        return Finish("Settle", d, rate);
    }
}
