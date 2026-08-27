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
///   <item><see cref="TrySlot"/> — THE SHARED-CLOCK SLOT INDEX, added at ModBuild 226. Every
///   scheduled one-shot in <see cref="EnvSound"/> asks "which slot is the shared clock in", and
///   until this round every one of them wrote <c>(long)Mathf.Floor(clock / period)</c> for itself,
///   five times over. That expression has a defect nobody had written down: <c>(long)</c> of a
///   <c>NaN</c> or an <c>Infinity</c> is IMPLEMENTATION-DEFINED in IL and on x64 it produces
///   <c>long.MinValue</c> — which is the very sentinel four of those callers use to mean "not
///   observing yet". A poisoned clock therefore did not produce a wrong sound, it silently re-armed
///   the schedule. See the function.</item>
///   <item><see cref="TickFires"/> / <see cref="TickOffset"/> — THE BERNOULLI TICK, added at
///   ModBuild 226 so the fire's crackle could stop being the one cue in the feature that two players
///   hear at different moments. It is <see cref="PoissonGap"/>'s process expressed the other way
///   round — as "does an event happen in THIS interval" rather than "how long until the next one" —
///   which is what makes it a pure function of the clock instead of a walk. See its own doc for why
///   the two produce the same distribution and what it cost to change.</item>
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
/// </para></summary>
internal static class EnvSoundSchedule
{
    // =============================================================================================
    //  THE SHARED-CLOCK SLOT — ModBuild 226.
    // =============================================================================================
    //
    //  USER REQUEST, hardware, verbatim:
    //
    //      "Genau wie die Easter-Eggs sollen auch die Sounds mit allen Mitspieler synchronisiert
    //       sein die in der selben Map sind. Sind also zwei Spieler in der Wald Umgebung und dort
    //       kommt ein Geräusch eines Tieres aus einer Ecke sollen alle Spieler die auch im Wald
    //       sind zur selben Zeit aus der selben Location denselben Sound hören."
    //
    //  HOW THAT IS ANSWERED, AND IT IS NOT WITH A PACKET. `NetProtocol.ExtIdEnvClock` (record 31)
    //  already elects one owner per style and publishes its reading; `SkyAlternative.EnvClockSeconds`
    //  is the result. An event that is a PURE FUNCTION of that number is heard by every client in
    //  the same room on the same frame with zero wire bytes — which is exactly why the apparitions,
    //  the rat and the drip rings already agree, and it is the standing project rule ("never open a
    //  second network channel for a fact the game or an existing mod record already synchronises").
    //  So the whole of the work is: make every scheduled one-shot such a function.
    //
    //  THE SLOT INDEX IS THE ONE PIECE OF THAT ARITHMETIC WORTH PROVING, and it is here rather than
    //  in EnvSound.cs for this file's standing reason: EnvSound.cs cannot be compiled into
    //  tests/GloomhavenVR.WireTests (AudioSource, AudioClip, the whole Unity audio module) and this
    //  file deliberately can. What the vectors hold is the property the user's sentence actually
    //  needs — that the same clock gives the same slot, on every machine, for every input.

    /// <summary>The largest shared-clock reading this converts. 1e9 s is 31 years of level time, so
    /// no session with an intention is inside the bound this rejects; what it excludes is a POISONED
    /// clock — see <see cref="TrySlot"/>.
    ///
    /// <para>Float32 precision, not range, is the practical limit and it is stated so nobody trusts
    /// this further than it goes: <c>float</c> resolves 0.0625 s at 1e6 s and 1 s at 1e7 s. That is
    /// not a correctness problem for the property this exists for — two clients compute the SAME
    /// float32 and therefore the SAME slot — but a period shorter than the clock's own resolution
    /// would stop being a period. Every caller's slot is 1.31 s or longer against a level time that
    /// is minutes.</para></summary>
    internal const float MaxClockSeconds = 1e9f;

    /// <summary>The shortest slot this will accept. Below it the index would outrun the clock's own
    /// float32 resolution long before <see cref="MaxClockSeconds"/>.</summary>
    internal const float MinSlotSeconds = 0.01f;

    /// <summary>
    /// WHICH SLOT OF LENGTH <paramref name="slotSeconds"/> THE SHARED CLOCK IS IN — the one
    /// expression every scheduled cue in <see cref="EnvSound"/> starts from, written once.
    ///
    /// <para><b>WHY IT RETURNS A BOOL RATHER THAN A NUMBER, and this is the defect it exists to
    /// remove.</b> Every caller used to write <c>(long)Mathf.Floor(clock / period)</c> inline.
    /// <c>Mathf.Floor(NaN)</c> is <c>NaN</c>, and the IL <c>conv.i8</c> of a NaN or an infinity is
    /// UNSPECIFIED — on x64 it yields <c>long.MinValue</c>, which is the exact sentinel
    /// <c>EnvSound._lastRatSlot</c>, <c>_lastNightCallSlot</c> and <c>_lastDripIndex</c> use for "not
    /// observing yet". So a clock that went bad did not make a wrong sound; it quietly told the
    /// schedule it had never run, and the next real slot was then SWALLOWED as a first observation.
    /// That is the failure this file exists to make unreachable, reached through a cast instead of
    /// through a loop, and it is invisible from a log. A bool forces the caller to have an answer
    /// for "there is no slot this frame", and the answer is always the same one: do nothing, and try
    /// again next frame.</para>
    ///
    /// <para><b>WHAT IS AND IS NOT PROMISED ABOUT TWO CLIENTS.</b> Promised: identical
    /// <paramref name="clock"/> and <paramref name="slotSeconds"/> give an identical slot, on every
    /// machine, because this is one IEEE-754 divide and one floor. NOT promised: that two clients'
    /// CLOCKS are bit-equal. <c>SkyAlternative.TickEnvClock</c> leaves a residual inside
    /// <c>ClockDeadbandSeconds</c> = 0.02 s, so within 20 ms of a slot boundary two clients can be on
    /// either side of it. That is a 20 ms disagreement on a cue at most, against slots of 1.31 s and
    /// longer, and it is not fixable from here — it is the price of a clock that is followed rather
    /// than a clock that is on the wire per event. Every consumer therefore has to be one for which
    /// 20 ms is nothing, which every consumer here is (the shortest is a 55 ms crackle).</para>
    /// </summary>
    /// <param name="clock">The shared environment clock in seconds — <c>EnvClockSeconds</c>. A
    /// NEGATIVE clock is not an error and is not clamped: it is the state a client is in for the
    /// first frames after a clock owner with a smaller reading is adopted, and the correct answer
    /// there is "no slot", because a negative slot index fed to <c>Haunt.Hash</c> leaves the
    /// documented 0.. range the GPU mirror is written against.</param>
    /// <param name="slotSeconds">The slot length. Must be finite and at least
    /// <see cref="MinSlotSeconds"/>; anything else returns false rather than dividing.</param>
    /// <param name="slot">The slot index, an exact non-negative integer, valid only when this
    /// returns true. Never <c>long.MinValue</c> — see above.</param>
    internal static bool TrySlot(float clock, float slotSeconds, out long slot)
    {
        slot = 0L;

        // POSITIVE TESTS, for PoissonGap's reason written out in its own doc: `clock < 0` is FALSE
        // for NaN and so is `clock > MaxClockSeconds`, so a pair of rejecting comparisons would pass
        // a NaN straight into the divide and the cast. `!(a && b)` is TRUE for NaN.
        if (!(clock >= 0f && clock <= MaxClockSeconds))
            return false;
        if (!(slotSeconds >= MinSlotSeconds && slotSeconds <= MaxClockSeconds))
            return false;

        float f = Mathf.Floor(clock / slotSeconds);
        // BELT AND BRACES ON THE CAST. Both operands are already known finite and non-negative, so
        // the quotient is finite and non-negative and this can only fail if one of the two bounds
        // above is edited away. It is one compare on a path that already did two, and what it is
        // guarding is not a wrong number but the sentinel collision described above.
        if (!(f >= 0f && f <= 1e18f))
            return false;

        slot = (long)f;
        return true;
    }

    // =============================================================================================
    //  THE BERNOULLI TICK — ModBuild 226, and it is PoissonGap turned inside out.
    // =============================================================================================
    //
    //  WHY IT HAD TO EXIST AT ALL. PoissonGap answers "how long until the next one", so a caller
    //  using it writes `next = now + gap` and keeps `next` — which makes the schedule a WALK, whose
    //  phase depends on when the client started observing. That is stated plainly in
    //  EnvSound.TickFire's own doc and it was accepted for two rounds on the argument that a crackle
    //  marks no visual. The user has now asked for the whole ambience to agree between clients, in
    //  terms that do not carve out an exception ("Genau wie die Easter-Eggs sollen auch die Sounds
    //  mit allen Mitspieler synchronisiert sein"), so the walk has to go.
    //
    //  AND THE SAME PROCESS HAS A MEMORYLESS FORM. A Poisson process observed on a fixed grid of
    //  ticks of length T is a BERNOULLI process: each tick independently carries an event with
    //  probability p, and the gaps are GEOMETRIC — the discrete exponential, mode at the minimum,
    //  therefore genuine clusters, which is the entire content of PoissonGap's own argument for not
    //  using a jittered constant. The mean gap is exactly T/p, so a caller that wants a mean of
    //  `m` seconds sets p = T/m and gets it. Nothing about the SOUND is being changed here; what is
    //  being changed is that the answer for tick n is a pure function of n.
    //
    //  THE GRID IS THE ONE THING THAT COULD HAVE MADE IT WORSE, AND TickOffset IS WHY IT DOES NOT.
    //  Firing on the tick boundary itself would put every crackle on a multiple of T — three fire
    //  sites beating a common lattice, which is a metronome, which is precisely the fault the user
    //  reported as "super nervig" and for which a whole cue was deleted. So the event is placed at a
    //  HASHED OFFSET inside the first HALF of its tick. Two consequences, both by construction:
    //  the gaps are continuous (there is no lattice left to hear), and the SHORTEST possible gap is
    //  T/2, because the latest an event can be is T/2 into its tick and the earliest is 0 into the
    //  next. That is the floor PoissonGapMin buys the other form, obtained here from geometry
    //  instead of from a clamp.

    /// <summary>The largest per-tick probability <see cref="TickFires"/> will use. It is not a
    /// safety bound — even p = 1 is safe here, because the event still lands inside its own tick and
    /// the gap floor is <c>T/2</c> whatever p is — it is a SHAPE bound: at p = 1 every tick carries
    /// an event and the geometric distribution collapses to a single gap, i.e. back to the metronome.
    /// 0.90 keeps a real spread of gaps for any mean a caller can reach through a config edit.</summary>
    internal const float TickShareMax = 0.90f;

    /// <summary>
    /// DOES THIS TICK CARRY AN EVENT? <paramref name="u"/> is one hash draw keyed on the tick index,
    /// <paramref name="tickSeconds"/> is the grid and <paramref name="meanSeconds"/> the mean gap the
    /// caller wants; the probability is <c>tick / mean</c>, clamped to
    /// <c>[0, </c><see cref="TickShareMax"/><c>]</c>.
    ///
    /// <para>BOUNDED FOR EVERY INPUT, exactly as <see cref="PoissonGap"/> is and for the same
    /// reason: a caller lerps <paramref name="meanSeconds"/> from a live element strength and can
    /// reach 0 or a poisoned value through a config edit. A mean of 0 would make p infinite; a mean
    /// of <c>NaN</c> would make every comparison false and the fire silent forever. Both fall back
    /// to the same place, which is "the events come as often as the shape bound allows".</para>
    ///
    /// <para>The realised mean gap is <c>tick / p</c> ticks, i.e. <paramref name="meanSeconds"/>
    /// exactly while p is unclamped — there is no truncation factor to quote, unlike
    /// <see cref="PoissonGap"/>'s 0.962. Against the shipped fire that makes the crackle 3.8%
    /// SLOWER, which is the whole audible cost of this change and is stated rather than left to be
    /// discovered.</para>
    /// </summary>
    /// <param name="u">A uniform draw in [0,1). Anything outside — and <c>NaN</c>, which no
    /// comparison catches by accident — is folded to the median before it is compared.</param>
    internal static bool TickFires(float u, float tickSeconds, float meanSeconds)
    {
        if (!(u >= 0f && u < 1f))
            u = 0.5f;
        return u < TickShare(tickSeconds, meanSeconds);
    }

    /// <summary>The per-tick probability <see cref="TickFires"/> compares against, exposed so the
    /// vectors can assert the realised rate rather than infer it.</summary>
    internal static float TickShare(float tickSeconds, float meanSeconds)
    {
        // The upper bound on each is PoissonGap's defence restated: `x > 0f` is TRUE for +Infinity,
        // and an infinite mean gives p = 0 (a fire that never crackles) while an infinite tick gives
        // p = +Infinity (one that always does).
        float t = tickSeconds > 0f && tickSeconds < 1e6f ? tickSeconds : MinSlotSeconds;
        float m = meanSeconds > 0f && meanSeconds < 1e6f ? meanSeconds : 1f;
        return Mathf.Clamp(t / m, 0f, TickShareMax);
    }

    /// <summary>
    /// WHERE INSIDE ITS TICK the event lands: <c>0..tick/2</c> seconds after the tick begins, from
    /// one hash draw. See the block above for why the half and not the whole — the half is what
    /// makes <c>tick/2</c> the shortest gap two consecutive events can have, and therefore what
    /// keeps this out of the 0.2-2 s band the ear reads as a rhythm.
    ///
    /// <para>Always finite and always inside <c>[0, tick/2]</c>, for every input including the ones
    /// no caller passes — a <c>NaN</c> offset added to a tick start produces a comparison that is
    /// false forever, which is the same class of defect as the freeze this file exists for.</para>
    /// </summary>
    internal static float TickOffset(float u, float tickSeconds)
    {
        if (!(u >= 0f && u < 1f))
            u = 0.5f;
        float t = tickSeconds > 0f && tickSeconds < 1e6f ? tickSeconds : MinSlotSeconds;
        return 0.5f * t * u;
    }

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

    // =============================================================================================
    //  THE DECK — ModBuild 241. WHICH of several clips a scheduled event uses, WITHOUT touching
    //  how often the event happens.
    // =============================================================================================
    //
    //  USER REQUEST, 2026-08-24, verbatim, and the parenthesis is the whole specification:
    //
    //      "Füge noch mehr verschiedene Tiersounds hinzu die zu einem Wald in der Nacht passen für
    //       mehr Varianz (nicht mehr Häufigkeit)."
    //
    //  THE TRAP THIS FUNCTION EXISTS TO AVOID. The wood's night calls are ONE EVENT PER 41 s SLOT
    //  (EnvSound.TickNightCall). The obvious way to add five animals is to give each its own
    //  schedule, and that multiplies the rate by (5+2)/2 — which is the thing he ruled out, in
    //  brackets, before anyone could ship it. So the schedule is not touched at all: NightCallSlot,
    //  NightCallMean and NightCallSkip are byte-for-byte what ModBuild 223 shipped and 226 declined
    //  to re-tune. The ONLY thing that changed is which clip a slot that was already going to sound
    //  reaches for, and that decision is this function.
    //
    //  WHY A DECK AND NOT A WEIGHTED DRAW. A weighted draw over the seven clips ModBuild 241 shipped
    //  repeats itself back-to-back once in 5.8 calls (sum of the squared shares = 17.19%; over the
    //  ten of ModBuild 242, once in 8.0 calls), and a repeat is the one thing that makes a
    //  synthesized wood sound synthesized — two identical owls a minute apart is a sample player,
    //  not a wood. It also has no memory, so a session can go forty minutes without a fox and then
    //  produce two in a row. A DECK fixes both by construction:
    //
    //    * the deck is a fixed MULTISET (four hoots, three ke-wicks, ... one fox), shuffled once per
    //      cycle and dealt in order, so the long-run share of each clip is EXACTLY its multiplicity
    //      and the longest possible drought is 2m-1 calls rather than unbounded;
    //    * a bounded repair pass then walks the dealt cards and pushes apart any card that matches
    //      the one before it (hard rule) or the one before that (soft rule).
    //
    //  MEASURED over 400,000 consecutive draws of the shipped deck. The 16-card figures are ModBuild
    //  241's and are kept as the before column; the 20-card ones are what ships as of ModBuild 242,
    //  which added a wolf, a barn owl and an insect to the same schedule:
    //
    //                          16 cards (241)      20 cards (242)     a weighted draw
    //      immediate repeats      0.0723%             0.0318%           17.19% / 12.50%
    //      one-apart repeats      4.1503%             2.0360%           17.19% / 12.50%
    //      realised shares        exact               exact             statistical
    //      longest drought        13 .. 31 calls      17 .. 39 calls    unbounded
    //
    //  BOTH REPEAT RATES IMPROVED WHEN THE DECK GREW, and the reason is worth having written down
    //  because it is the argument for adding cards rather than re-weighting: the repair pass swaps a
    //  colliding card for a legal partner further down the deck, so its failure rate is set by how
    //  often NO legal partner exists — which falls as the vocabulary widens and the largest
    //  multiplicity's SHARE falls (4/16 = 25% to 4/20 = 20%). A deck that grows gets quieter about
    //  itself. The droughts lengthen in proportion to the deck, which is the intended trade: a card
    //  worth one twentieth is a sound heard about every 17 minutes, bounded at 2m-1 = 39 calls.
    //
    //  (The "weighted draw" column is the sum of the squared shares, i.e. what a memoryless draw of
    //  the same weights would do at BOTH distances. It is the thing being beaten, and it gets
    //  BETTER as the deck widens too — from 17.19% to 12.50% — which is why the comparison is
    //  restated per deck rather than quoted once.)
    //
    //  MULTIPLAYER: PURE FUNCTION OF THE INDEX, AND INTEGER-ONLY. Two clients that agree on the slot
    //  agree on the card, because everything below is 64-bit integer arithmetic — no float, no
    //  Haunt.Hash cascade, no Time.time and nothing per-client. That matters more here than for the
    //  perch: two players hearing a call from slightly different trees is a shrug, two players
    //  hearing DIFFERENT ANIMALS in the same second is a bug report. Integer ops are also why this
    //  does not reach for Haunt.Hash: the hash takes a FLOAT slot index and this needs m-1 = 15
    //  decorrelated draws per cycle, which would have meant either a sixteenth hash channel (the
    //  documented range is 0..7 and is a mirror of EnvHaunt.cginc's table) or feeding it synthetic
    //  slot numbers. Neither is a thing to do to a contract shared with a shader.
    //
    //  TERMINATION BY CONSTRUCTION, this file's standing contract: every loop below is a `for` over
    //  a count fixed before it starts, there is no accumulator whose convergence decides how long it
    //  runs, and no repair is retried. The worst case is m + m + m + m compares. A repair that
    //  cannot find a legal partner leaves the card where it is — the deck is still dealt, one
    //  adjacency is imperfect, and that is the whole of the 0.0723%.

    /// <summary>The largest deck this will deal. Not a safety bound — the loops are all bounded by
    /// <c>deck.Length</c> whatever it is — but a statement of intent: a deck is a VOCABULARY, and a
    /// vocabulary of more than 64 clips is a bank problem rather than a schedule problem.</summary>
    internal const int DeckMaxCards = 64;

    /// <summary>
    /// WHICH CARD OF <paramref name="deck"/> the event with index <paramref name="index"/> gets.
    /// The deck is dealt in cycles of <c>deck.Length</c>; each cycle is a fresh shuffle of the same
    /// multiset, repaired so a card very rarely follows itself. See THE DECK above.
    ///
    /// <para>A PURE FUNCTION of <paramref name="index"/>, <paramref name="deck"/> and
    /// <paramref name="salt"/>, in integer arithmetic only — which is what makes every client in a
    /// room deal the same card for the same shared-clock slot with zero wire bytes.</para>
    /// </summary>
    /// <param name="index">The event index — for the wood, the 41 s call slot from
    /// <see cref="TrySlot"/>, which is non-negative by construction. A negative index is not an
    /// error and is not clamped; it deals from a cycle with a negative number, which is still a
    /// deterministic deal.</param>
    /// <param name="deck">The multiset, as card values. Never modified — the shuffle and the repair
    /// both work on a copy. Returns 0 for a null or empty deck, which is the caller's own first
    /// entry and therefore never a card it does not have.</param>
    /// <param name="salt">Separates one deck's stream from another's, so two decks of the same
    /// length in the same session do not deal in lockstep.</param>
    internal static byte DeckDraw(long index, byte[] deck, uint salt)
    {
        if (deck == null || deck.Length == 0)
            return 0;
        if (deck.Length == 1 || deck.Length > DeckMaxCards)
            return deck[0];

        int m = deck.Length;
        // FLOOR division and a NON-NEGATIVE remainder. C#'s `/` truncates toward zero and `%` keeps
        // the dividend's sign, so a negative index would otherwise index the copy out of range. The
        // wood never passes one; this is the same belt-and-braces as TrySlot's third compare, and
        // for the same reason — the bound is what stops an edit elsewhere becoming a crash here.
        long cycle = index >= 0 ? index / m : ((index + 1) / m) - 1;
        int j = (int)(index - cycle * m);

        var cur = new byte[m];
        Deal(cur, deck, cycle, salt);

        // THE PREVIOUS CYCLE'S LAST TWO CARDS, so the repair below reaches ACROSS the cycle boundary
        // rather than starting each cycle blind — a deck that shuffles cleanly inside itself and
        // then repeats a card at every 16th call has moved the fault, not fixed it.
        //
        // back[m-1] is EXACT: index m-1 is frozen — no repair below ever writes it — so the raw deal
        // of cycle-1 IS what that slot played. back[m-2] is an APPROXIMATION, because the forward
        // pass may swap into m-2; only the soft one-apart rule leans on it, and the cost of it being
        // wrong is a one-apart repeat, never an immediate one.
        var back = new byte[m];
        Deal(back, deck, cycle - 1, salt);
        byte prev1 = back[m - 1];
        byte prev2 = back[m - 2];

        for (int t = 0; t < m - 1; t++)
        {
            if (cur[t] == prev1 || cur[t] == prev2)
            {
                // Look for a partner further down the deck. `both` clears the hard rule AND the soft
                // one; `one` clears only the hard rule and is the fallback. Neither may be m-1.
                int both = -1;
                int one = -1;
                for (int k = t + 1; k < m - 1; k++)
                {
                    if (cur[k] == prev1)
                        continue;
                    if (one < 0)
                        one = k;
                    if (cur[k] != prev2)
                    {
                        both = k;
                        break;
                    }
                }

                // A SOFT-ONLY COLLISION IS NOT WORTH A SOFT-ONLY CURE. If this card only broke the
                // one-apart rule, swapping it for another card that also breaks it changes nothing
                // and costs a shuffle's worth of variety, so that trade is declined.
                int pick = both >= 0 ? both : (cur[t] == prev1 ? one : -1);
                if (pick >= 0)
                {
                    byte swap = cur[t];
                    cur[t] = cur[pick];
                    cur[pick] = swap;
                }
            }

            prev2 = prev1;
            prev1 = cur[t];
        }

        // THE TWO TAIL PAIRS THE FORWARD PASS CANNOT REACH — (m-3, m-2) and (m-2, m-1). It runs out
        // of partners at m-2 (its range excludes m-1 by contract) and never inspects m-1 at all. One
        // bounded BACKWARD scan fixes both: swap m-2 with an earlier card whose two neighbours and
        // whose own new neighbours all come out different. Confined to 1..m-4 so the two
        // neighbourhoods cannot overlap and one check cannot invalidate the other, and m-1 is never
        // written — which is the invariant `back[m-1]` above depends on.
        if (m >= 6 && (cur[m - 2] == cur[m - 3] || cur[m - 2] == cur[m - 1]))
        {
            for (int k = m - 4; k >= 1; k--)
            {
                byte a = cur[k];
                byte b = cur[m - 2];
                if (a == cur[m - 1] || a == cur[m - 3])
                    continue;
                if (b == cur[k - 1] || b == cur[k + 1])
                    continue;
                cur[k] = b;
                cur[m - 2] = a;
                break;
            }
        }

        return cur[j];
    }

    /// <summary>
    /// One cycle's deal: <paramref name="deck"/> copied into <paramref name="into"/> and shuffled
    /// by a Fisher-Yates whose whole draw sequence comes from <paramref name="cycle"/>. Unbiased in
    /// the sense that matters here — every permutation reachable, no index favoured by a modulo
    /// fold — and identical on every machine, because <see cref="SplitMix"/> is integer-only.
    /// </summary>
    private static void Deal(byte[] into, byte[] deck, long cycle, uint salt)
    {
        int m = deck.Length;
        for (int i = 0; i < m; i++)
            into[i] = deck[i];

        // The cycle index is AVALANCHED before it is used, not fed in raw: consecutive seeds must
        // produce unrelated deals, and any counter-based generator handed 7 and then 8 gives two
        // streams a listener would hear as related.
        ulong state;
        unchecked
        {
            state = (ulong)cycle * 0x9E3779B97F4A7C15UL + salt;
        }

        for (int i = m - 1; i > 0; i--)
        {
            // Lemire's multiply-shift into 0..i: the top 32 bits of `u * (i+1)`. One multiply, no
            // divide, no modulo bias worth the name at these sizes, and — the property this is here
            // for — no branch whose outcome could differ between two clients.
            uint u = SplitMix(ref state);
            int k;
            unchecked
            {
                k = (int)(((ulong)u * (ulong)(i + 1)) >> 32);
            }
            byte swap = into[i];
            into[i] = into[k];
            into[k] = swap;
        }
    }

    /// <summary>SplitMix64's step and finaliser, returning the top-quality low 32 bits. Chosen over
    /// <see cref="EnvSoundRng"/> for the deal for two reasons: xorshift32 seeded from a counter
    /// gives visibly related first outputs for adjacent seeds (which is exactly what a cycle index
    /// is), and adding a method to <see cref="EnvSoundRng"/> would put the bank's byte-identical
    /// draw sequences in play for a change that has nothing to do with them.</summary>
    private static uint SplitMix(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (uint)(z ^ (z >> 31));
        }
    }
}
