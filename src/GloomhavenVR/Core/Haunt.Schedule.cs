using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HAUNT — THE SCHEDULE, IN C#. Which apparition is running right now, when it started and how long
//  it lasts, computed from the shared clock EXACTLY as the GPU computes it.
// =================================================================================================

internal static partial class Haunt
{
    // =============================================================================================
    //  WHY THIS EXISTS, AND WHY IT IS HERE AND NOT IN THE THING THAT NEEDS IT
    //
    //  USER CORRECTION, 2026-08-14, verbatim — it REVERSES the earlier "ohne sound" ruling that
    //  Haunt.cs:74-75 still quotes as the reason the apparitions are silent:
    //      "Ich nehme die Entscheidung von zuvor zurück, die Grusel-Erscheinungen sollen NICHT
    //       stumm bleiben. Auch hier sollen Soundeffekte kommen aber auch nicht aufdringlich und
    //       nur wenn die Umgebungssounds aktiviert sind."
    //  So the apparitions now make sound: subtle, never intrusive, and gated on the ONE environment
    //  sound toggle ([EnvSound] Enabled) — deliberately NOT a second switch of their own.
    //
    //  A SOUND THAT MERELY LOOKS SYNCHRONISED IS NOT ACCEPTABLE. The schedule is not "roughly every
    //  83 seconds"; it is an exact function of the shared environment clock, and a cue on an
    //  independent timer would drift away from the thing it belongs to within a minute and would be
    //  in a different place on every client. So the audio has to resolve the SAME slot the GPU
    //  resolved, from the same clock, to the same bits.
    //
    //  THE COUNT OF MIRRORS, AND WHY THIS ONE IS THE RIGHT SHAPE.
    //  The schedule already exists twice on purpose:
    //    1. unity/.../Bundle/Environments/EnvHaunt.cginc — the GPU's single copy, shared by the four
    //       shaders that draw or react to an apparition (its header explains at length why those
    //       four may not each have their own).
    //    2. unity/.../Editor/BuildEnvironmentRooms.cs (HauntH / HauntCardOfSlot / ...) — the BAKE's
    //       copy, which exists because the builder has to MEASURE the resulting schedule and assert
    //       things about it (no two consecutive slots on the same card, every crossing finishing
    //       inside its slot) before it writes the numbers.
    //  This is the third, and it is the runtime's. It could not be avoided by reading a value back
    //  off the GPU — there is nothing to read back; the shaders compute the schedule per vertex and
    //  publish nothing. It could have been put inside the audio driver, and that would have been
    //  the wrong place: the schedule is a property of the HAUNT FEATURE, not of sound, and the next
    //  consumer (a haptic, a log line, a peer-visible cue) would then have had to either reach into
    //  the audio driver or make a fourth copy. So it lives here, beside the switch that governs it
    //  and the force channel it has to honour, and <see cref="EnvSound"/> merely asks it questions.
    //
    //  THIS MIRROR IS *NOT* MACHINE-CHECKED, AND THAT IS A KNOWN DEBT RATHER THAN AN OVERSIGHT.
    //  scripts/check-mirrors.sh is the project's mirrored-constant lint and it cannot cover this
    //  pair: it resolves every site as `src/<path>` and extracts with a regex for
    //  `const (float|string) <Name> = <value>;` (check-mirrors.sh:145-157), so it can compare two
    //  C# constants inside src/ and nothing else. The other side of THIS pair is a `#define` in
    //  unity/.../Bundle/Environments/EnvHaunt.cginc — a different directory and a different
    //  language. Teaching the lint to read #defines under unity/ is the right fix and was
    //  deliberately NOT done in this round: unity/ is owned by four other lanes right now, and a
    //  shared gate script is the worst possible thing to edit underneath them.
    //  UNTIL THEN THE GUARD IS THIS COMMENT PLUS THE CITED LINE NUMBERS, and the failure it is
    //  guarding against is specific: changing GHVR_HAUNT_STARTLO (or any sibling) on the GPU side
    //  without changing it here does not break anything visible — it makes the SOUND drift away
    //  from the apparition it belongs to, which nobody notices until a player says the cellar feels
    //  slightly wrong. Whoever touches EnvHaunt.cginc's constants must touch this block too.
    //
    //  FLOAT, NOT DOUBLE, AND THIS ORDER OF OPERATIONS. Every value in the cascade is under 200 and
    //  every step is one IEEE-754 single-precision multiply, add or frac — which is exactly why the
    //  shader's header bans sin() from it: sin() is where GPU vendors differ, and multiply/add/frac
    //  are correctly rounded everywhere. Reproducing that in C# means matching BOTH the type and
    //  the association: writing the hash in double, or reassociating `x * (x + 31.70)`, would give
    //  a number that is very nearly right and therefore fails in a way nobody can see until two
    //  players compare notes.
    // =============================================================================================

    /// <summary>The slot beat, seconds. MIRROR of <c>HauntPeriod</c>
    /// (unity/.../Editor/BuildEnvironmentRooms.cs:4226) and of the <c>_HauntPeriod</c> the bake
    /// writes onto every haunt-reading material (:2473).</summary>
    internal const float PeriodSeconds = 83f;

    // MIRROR of the GHVR_HAUNT_* #defines in EnvHaunt.cginc. Names kept recognisable across the
    // language boundary on purpose — a grep for STARTLO must land in both files.
    private const float Groups = 3f;
    private const float StartLo = 0.15f;
    private const float StartSp = 0.40f;
    private const float DurLo = 0.85f;
    private const float DurSp = 0.30f;

    // Hash channels. "Numbers are load-bearing: the C# mirror reads the same ones" — EnvHaunt.cginc.
    private const float HcRate = 0f;
    private const float HcPick = 1f;
    private const float HcStart = 3f;
    private const float HcDur = 4f;

    /// <summary>
    /// THE CASCADE, exposed for the one other schedule that shares it.
    ///
    /// <para><c>EnvCritter.shader</c>'s rat schedule uses the SAME hash, character for character —
    /// its bake-side mirror <c>RatH</c> (BuildEnvironmentRooms.cs:1942-1948) is byte-identical to
    /// <see cref="H"/> below, because the rat's cascade is where the haunt's was copied FROM (the
    /// cginc header says so: "Character for character the rat's cascade"). So the runtime keeps one
    /// implementation instead of two, and <see cref="EnvSound"/> reads the rat's crossing times
    /// through this rather than growing a fourth copy.</para>
    /// </summary>
    /// <param name="n">Slot index — an exact non-negative integer.</param>
    /// <param name="k">Channel selector, 0..7.</param>
    internal static float Hash(float n, float k) => H(n, k);

    /// <summary>
    /// The schedule's only source of variety — character for character
    /// <c>GhvrHauntH</c> (EnvHaunt.cginc). <paramref name="n"/> is the slot index (an exact
    /// non-negative integer), <paramref name="k"/> selects one of eight decorrelated channels.
    ///
    /// <para>The <c>+ 1f</c> is not cosmetic and must not be tidied away: without it channel 0 of
    /// slot 0 starts at <c>frac(0)</c>, and 0 is this cascade's one fixed point — slot 0 would
    /// resolve to the same numbers forever.</para>
    /// </summary>
    private static float H(float n, float k)
    {
        float x = Frac((n + 1f + k * 7.13f) * 0.7548776662f);
        x = Frac(x * (x + 31.70f));
        x = Frac(x * (x + 17.31f));
        return Frac(x * (x + 43.19f));
    }

    /// <summary>HLSL <c>frac</c>. <c>Mathf.Repeat(x, 1f)</c> is NOT the same function — it is
    /// implemented as <c>x - floor(x/1)*1</c> with a clamp that can return exactly 1.</summary>
    private static float Frac(float x) => x - Mathf.Floor(x);

    /// <summary>HLSL <c>step(edge, x)</c>: 1 when <paramref name="x"/> is at or above the edge.</summary>
    private static float Step(float edge, float x) => x >= edge ? 1f : 0f;

    /// <summary>One resolved slot — the C# face of the shader's <c>GhvrHaunt</c> struct, reduced to
    /// the fields a consumer outside the vertex shader can actually use.</summary>
    internal readonly struct Slot
    {
        /// <summary>The slot index (an exact integer).</summary>
        internal readonly float Index;

        /// <summary>Which of the room's events this slot belongs to, 0..<see cref="EventCount"/>-1.</summary>
        internal readonly int Card;

        /// <summary>True when this slot fires on THIS client — master, dial and elements folded in.
        /// A slot that is not live is not a quiet apparition, it is no apparition.</summary>
        internal readonly bool Live;

        /// <summary>Shared-clock time at which the event begins. Absolute, not slot-relative, so a
        /// consumer never has to know what a slot is.</summary>
        internal readonly float StartClock;

        /// <summary>Per-slot scale on the event's authored duration (Ice holds it longer).</summary>
        internal readonly float DurationMul;

        /// <summary>True while this is a FORCED event from the Advanced-menu test buttons rather
        /// than a scheduled one.</summary>
        internal readonly bool Forced;

        internal Slot(float index, int card, bool live, float startClock, float durationMul, bool forced)
        {
            Index = index;
            Card = card;
            Live = live;
            StartClock = startClock;
            DurationMul = durationMul;
            Forced = forced;
        }
    }

    /// <summary>
    /// Resolve the slot containing shared-clock time <paramref name="clock"/> — the C# mirror of
    /// <c>GhvrHauntAt</c> (EnvHaunt.cginc), including the force override, in the same order and with
    /// the same guards.
    ///
    /// <para>THE ELEMENT TERMS ARE READ THE SAME WAY THE SHADER READS THEM: already folded with
    /// ElementMood's own master (<see cref="ElementMood.Live"/>), so nothing here needs an "is the
    /// element channel on" branch — when the feature is off every term is 0 and every expression
    /// below collapses to the plain schedule, exactly as <c>GhvrElems()</c> does on the GPU.</para>
    /// </summary>
    internal static Slot Resolve(float clock)
    {
        float per = Mathf.Max(PeriodSeconds, 1f);
        float slot = Mathf.Floor(clock / per);

        // WHICH EVENT — the group partition. slot mod 3, exact for an integer well inside float32's
        // integer range. Consecutive slots are in different groups, therefore consecutive slots are
        // different events, with no history to walk.
        float grp = slot - Groups * Mathf.Floor(slot / Groups);
        float inGroup = Mathf.Max(Mathf.Floor(EventCount / Groups + 0.5f), 1f);
        // min() rather than trusting the hash — the shader's own comment: frac() can return exactly
        // 0, and an edit that let it reach 1.0 would index one card past the end and silently draw
        // nothing for one slot in a few thousand.
        float j = Mathf.Min(Mathf.Floor(H(slot, HcPick) * inGroup), inGroup - 1f);
        int card = (int)(grp + Groups * j);

        // WHETHER IT FIRES HERE. Dark makes the room more haunted, Light less; both are
        // scenario-wide state the game keeps bit-identical on every client, so two players'
        // schedules stay NESTED rather than merely similar.
        float dark = ElementMood.Live(5);
        float light = ElementMood.Live(4);
        float ice = ElementMood.Live(1);

        float freq = Mathf.Clamp01(Frequency.Value) * Mathf.Clamp01(1f + 0.60f * dark - 0.35f * light);
        float master = EasterEggs.Value ? 1f : 0f;
        bool live = Step(H(slot, HcRate), freq) * Step(0.0001f, master) > 0.5f;

        float start = per * (StartLo + StartSp * H(slot, HcStart));
        float durMul = (DurLo + DurSp * H(slot, HcDur)) * (1f + 0.35f * ice);

        // ---- ON DEMAND. One compare when nothing is forced. The forced run is expressed in the
        // SAME terms as a scheduled one — a start with no jitter and durMul 1 — because the tester
        // asked for THAT event, not for a random stretch of it, and because a second "forced" code
        // path is how a debug mode ends up being the thing that was tested.
        //
        // A LATCHED FORCE LOOPS, and this mirror needs no special case for it: Haunt.Tick moves
        // _forceSince forward by one run at a time, so every read below is simply "the run that is
        // playing now" — exactly what the shader sees in _GhvrHauntForce.y. It does have one visible
        // consequence worth stating, because it is a behaviour and not an accident: StartClock
        // changes once per run, and EnvSound.TickHaunt fires one cue per (StartClock, Card) pair
        // (EnvSound.cs, "Fire once per (start, card)"). So a latched apparition makes its sound
        // again on every repetition, which is what a tester judging the cue against the picture
        // needs — and it stays one cue per appearance, never one per frame.
        if (_forceId >= 0)
            return new Slot(Mathf.Floor(_forceSince / per), _forceId, true, _forceSince, 1f, true);

        return new Slot(slot, card, live, slot * per + start, durMul, false);
    }

    /// <summary>
    /// The authored envelope length of one card, seconds, BEFORE the per-slot
    /// <see cref="Slot.DurationMul"/>. MIRROR of the two card catalogues in
    /// <c>unity/.../Editor/BuildEnvironmentRooms.cs</c> (cellar :6010ff, forest :8800ff), summing
    /// each card's <c>reveal + hold + fade</c>.
    ///
    /// <para>WHY THE SUM AND NOT THE THREE PARTS: a consumer outside the shader cannot use the
    /// three separately — the shader needs them to shape an opacity curve, while everything here
    /// needs one answer to "is it still running". Carrying three numbers to throw two away would be
    /// three numbers to keep in step instead of one.</para>
    ///
    /// <para>The ids are the array order, which is also the id table the test buttons use (grep
    /// HAUNT FORCE ID TABLE). Cellar: 0 Window, 1 Hands, 2 Floor, 3 Tremble, 4 Stair, 5 Shelf.
    /// Forest: 0 Face, 1 Eyes, 2 Watcher, 3 Cross, 4 Loom, 5 Hang.</para>
    /// </summary>
    internal static float CardSeconds(SkyStyle style, int card)
    {
        if (card < 0 || card >= EventCount)
            return 0f;

        return style == SkyStyle.SwampNight
            ? card switch
            {
                0 => 8.6f,   // Face    3.4 + 2.4 + 2.8
                1 => 3.0f,   // Eyes    1.1 + 1.4 + 0.5
                2 => 8.5f,   // Watcher 3.5 + 5.0 + 0
                3 => 0.34f,  // Cross   0.06 + 0.22 + 0.06 — the fastest event in either room
                4 => 6.8f,   // Loom    4.2 + 2.6 + 0
                _ => 6.8f,   // Hang    2.6 + 2.0 + 2.2
            }
            : card switch
            {
                0 => 7.6f,   // Window  3.2 + 2.6 + 1.8
                1 => 8.0f,   // Hands   2.8 + 2.2 + 3.0
                2 => 7.4f,   // Floor   4.0 + 3.4 + 0
                3 => 2.0f,   // Tremble 0 + 1.1 + 0.9 — draws nothing at all; the webs shiver
                4 => 0.70f,  // Stair   0.18 + 0.34 + 0.18
                _ => 8.2f,   // Shelf   3.6 + 2.0 + 2.6
            };
    }
}
