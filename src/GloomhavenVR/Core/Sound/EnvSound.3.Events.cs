using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// EnvSound part 3 of 5 — see EnvSound.1.Core.cs for the type's own doc
// and for why the parts are digit-prefixed.
internal static partial class EnvSound
{
    // ---- the events -------------------------------------------------------------------------------

    // =============================================================================================
    //  THE JOIN, AND THE ONE THING A SHARED CLOCK CANNOT DO BY ITSELF — ModBuild 226.
    // =============================================================================================
    //
    //  Every scheduled cue in this file is a pure function of `SkyAlternative.EnvClockSeconds`, which
    //  is what makes two players hear the same owl from the same tree on the same frame. But that
    //  clock is not continuous. `SkyAlternative.FollowEnvClock` applies a JUMP — not a walk — on two
    //  paths: whenever the elected owner CHANGES (which includes the very first packet a joining
    //  client receives, because it goes from owning its own clock to following someone else's), and
    //  whenever the error exceeds `ClockJumpSeconds` = 1.5 s. Between those, the offset is SLEWED at
    //  `ClockSlewRate` = 0.2 s of clock per second of wall time.
    //
    //  SO THE CLOCK CAN LEAP, FORWARD OR BACKWARD, BY MINUTES, ON ONE FRAME. What that did to the
    //  schedules, before this guard, is two distinct faults:
    //
    //    * A FORWARD LEAP FIRED EVERYTHING AT ONCE. Every edge detector here compares an INDEX
    //      (`_lastDripIndex`, `_lastRatSlot`, `_lastNightCallSlot`) and fires when it changes. A leap
    //      changes all of them on the same frame, so the drip, the rat and the night call all sounded
    //      together, in a room where nothing had happened — which is the loudest possible way to
    //      announce that a peer just joined.
    //    * A BACKWARD LEAP REPLAYED SLOTS. The detectors hold the LAST index, not a set, so a leap
    //      back over a slot boundary and a walk forward over it again fires that slot's event a
    //      SECOND time. The rat crossed the floor twice with one animation.
    //
    //  THE FIX IS THE `first` SWALLOW THAT ALREADY EXISTS, APPLIED AGAIN. Every detector already
    //  knows how to say "I have just started observing: latch this slot and play nothing, because its
    //  event has already happened" — that is what `long.MinValue` means in each of them. A clock jump
    //  IS a fresh start on a new timeline, so it re-arms them all, and the first slot after the jump
    //  is swallowed exactly as the first slot after a build is. The deferred queue and the rat's
    //  pending squeak go with them: both hold ABSOLUTE shared-clock times that were written against
    //  a timeline that no longer exists.
    //
    //  WHAT IS DELIBERATELY NOT DONE: reading the jump from SkyAlternative. It has the information —
    //  `ApplyTimeOfs` knows exactly when it jumped and why — and a notification would be more direct
    //  than the inference below. It would also be a public event on a class this file is a CONSUMER
    //  of, added for one caller, and it is not needed: the discontinuity is fully observable from the
    //  clock's own readings against wall time. If a second consumer ever wants it, that is when the
    //  accessor is worth adding, and this comment is where the next reader is told so.

    /// <summary>How far the shared clock may diverge from wall time in one frame before this treats
    /// it as a JUMP rather than as progress.
    ///
    /// <para>It is bracketed from both sides rather than picked. BELOW: the slew is
    /// <c>SkyAlternative.ClockSlewRate</c> = 0.2 s of clock per second of wall time, and the real
    /// step this is measured over is clamped to 1 s, so a legitimate correction can never contribute
    /// more than 0.20 s of divergence. ABOVE: <c>SkyAlternative.ClockJumpSeconds</c> = 1.5 s is the
    /// error at which that class stops walking and jumps, so every jump it makes for that reason is
    /// at least that big. 0.75 s sits between the two with a factor of two of margin on each
    /// side.</para>
    ///
    /// <para><b>WHAT THIS DOES NOT CATCH, stated so nobody trusts it further than it goes:</b> an
    /// OWNER CHANGE applies a jump of ANY size, including a few milliseconds, and one smaller than
    /// this passes as progress. The worst that costs is one extra one-shot, if the jump happened to
    /// cross a slot boundary — which is the same harm as a single duplicated cue and is well under
    /// the level of the thing this guard exists to prevent.</para></summary>
    private const float ClockStepTolerance = 0.75f;

    /// <summary>The previous frame's shared-clock reading and the wall time it was taken at. NaN is
    /// "no previous frame" — a fresh build, which has just armed its schedules anyway.</summary>
    private static float _lastClockSeen = float.NaN;
    private static float _lastRealSeen;

    /// <summary>
    /// Detect a shared-clock discontinuity and re-arm the schedules across it. See the block above.
    /// Costs two float reads, a subtract and a compare in the settled case, which is every frame
    /// after the first packet.
    /// </summary>
    private static void TickClockContinuity(float clock)
    {
        float wasClock = _lastClockSeen;
        float wasReal = _lastRealSeen;
        _lastClockSeen = clock;
        _lastRealSeen = Time.unscaledTime;
        if (float.IsNaN(wasClock))
            return;

        // WALL TIME, MEASURED HERE, NOT `Time.unscaledDeltaTime`. That property is capped by
        // `Time.maximumDeltaTime` (0.333 s by default), so a five-second stall reports a third of a
        // second — and the shared clock, which is real time plus an offset, would then look like it
        // had leapt 4.7 s. The difference of two `unscaledTime` readings has no such cap.
        //
        // ...AND IT IS CLAMPED TO 1 s ANYWAY, which is not a contradiction: past a second of stall
        // the correct answer IS to re-arm. Every consumer of this clock already treats a stall of
        // that size as fatal to its schedule (see DeferredStaleSeconds and the rat squeak's own 4 s
        // guard), so a long hitch and a clock jump want the same response.
        float realStep = Mathf.Clamp(_lastRealSeen - wasReal, 0f, 1f);
        float drift = clock - wasClock - realStep;
        if (drift <= ClockStepTolerance && drift >= -ClockStepTolerance)
            return;

        ArmSchedules($"the shared environment clock moved {clock - wasClock:F2}s while {realStep:F2}s "
                     + $"of wall time passed (drift {drift:+0.00;-0.00}s, tolerance "
                     + $"{ClockStepTolerance:F2}s)");
    }

    /// <summary>
    /// Put every scheduled cue back into its "I have just started observing" state, and drop
    /// anything already queued against the old timeline.
    ///
    /// <para><b>THAT MEANS TWO DIFFERENT THINGS AND BOTH ARE CORRECT, which is worth stating because
    /// the difference looks like an inconsistency.</b> For the four SLOT detectors (the drip, the
    /// rat, the night call, the fire's ticks) <c>long.MinValue</c> means "swallow the slot the clock
    /// now sits in", because their events mark nothing a player can look at and one that already
    /// happened on the new timeline is simply missed. For the APPARITIONS it means the opposite —
    /// clearing <c>_lastHauntStart</c> lets the current slot fire — and that is right for the same
    /// reason: an apparition IS a picture, the shader draws it from the very same clock, so after a
    /// jump the cue must be free to belong to whatever the GPU is now drawing. A backward jump
    /// replays an apparition this client already heard, and it replays the apparition too.
    /// <see cref="TickHaunt"/>'s own staleness budget then decides whether the event is joinable at
    /// all, which is a decision this method must not pre-empt.</para>
    ///
    /// <para>Called from exactly two places, and they are the same event seen from two sides —
    /// <see cref="Teardown"/> (a new room, so a new observation) and
    /// <see cref="TickClockContinuity"/> (a new timeline, so a new observation). Writing it once is
    /// what stops the two drifting apart: the teardown path grew this list one field at a time over
    /// six rounds, and a jump path that had been given its own copy would have been missing whichever
    /// field was added last.</para>
    /// </summary>
    /// <param name="why">Null on a teardown, which logs its own line and does not need a second one.
    /// A reason on a jump, because a burst of re-armed schedules is otherwise indistinguishable from
    /// the feature having gone quiet, and this is the line that attributes it.</param>
    private static void ArmSchedules(string? why)
    {
        _lastDripIndex = long.MinValue;
        _lastRatSlot = long.MinValue;
        _lastNightCallSlot = long.MinValue;
        for (int i = 0; i < FireSites; i++)
            _lastFireTick[i] = long.MinValue;
        _lastHauntStart = float.NaN;
        _lastHauntCard = -1;
        _lastForcedStart = float.NaN;
        _lastForcedCard = -1;
        ClearDeferred();
        _squeakAt = float.NaN;
        _squeakFrom = null;

        if (why == null)
            return;
        VRLog.Info("Core", "ENV SOUND schedules RE-ARMED — " + why + ". Every scheduled cue here is a "
                           + "pure function of SkyAlternative.EnvClockSeconds, which is what makes "
                           + "two players in the same room hear the same event from the same place "
                           + "on the same frame; but that clock JUMPS when the elected owner changes "
                           + "(including on the first packet a joining client receives) and when the "
                           + "error passes its walk band. Across a jump the slot indices all change "
                           + "at once, so without this the drip, the rat and the night call would "
                           + "have fired together, and a BACKWARD jump would have replayed slots this "
                           + "client had already heard. So: the four SLOT schedules (drip, rat, "
                           + "night call, fire ticks) swallow the slot the clock now sits in, "
                           + "exactly as they do on the first frame after a build; the APPARITIONS' "
                           + "latch is cleared the other way, so their cue is free to belong to "
                           + "whatever the shader is now drawing from the same clock; and the "
                           + "deferred queue and the rat's pending squeak are dropped, because both "
                           + "hold absolute times on the timeline that just ended. NOTHING IS BROKEN "
                           + "and no sound is lost that was going to be correct: the next event "
                           + "plays in full, on the new clock, in step with every other client.");
    }

    private static void TickEvents(SkyStyle style, float clock)
    {
        if (style == SkyStyle.Cellar)
        {
            TickDrip(clock);
            TickRat(clock);
        }
        // THE NIGHT CALLS RUN IN BOTH ROOMS SINCE ModBuild 296, and this line is the whole of the
        // schedule change. USER: "Die Tiergeräusche kannst du von diesem Wald aus auch triggern, in
        // der selben Intensität wie im Wald". The cellar now has a wood outside its window, so it
        // has the wood's animals — the SAME 41 s slot, the SAME deck, the SAME 22% skip and the SAME
        // authored gains. There is no second schedule and no second rate: TickNightCall self-gates
        // on _perchFrame, which is the ground in the wood and the window's own marker in the cellar,
        // and is null in a room that has neither.
        //
        // IT CANNOT DOUBLE UP WITH WHAT THE CELLAR ALREADY PLAYS. The drip, the rat and the
        // apparitions index three different clocks (a drip period, a rat slot, an 83 s haunt slot)
        // against this schedule's 41 s, and two indices that are never compared cannot correlate —
        // the argument DripVariantChannel's doc makes at length. What HAS changed is that that doc's
        // parenthesis "the DRIP and the RAT are cellar-only and can never run in the same session's
        // room as these" is no longer true; the conclusion is, for the reason above, and the note
        // there has been corrected rather than left standing.
        TickNightCall(clock);
        TickDeferred(clock);
        TickFire(clock);
        // NOTHING ANSWERS ICE. There was a TickFrost here until ModBuild 149; see the ruling block
        // in EnvSound.Bank.cs for why the ice sound is deleted rather than silenced.
        TickHaunt(style, clock);
    }

    /// <summary>
    /// THE CRACKLE. One statistically scheduled event per fire site — a burst of bursting wood cells,
    /// or (one time in six) an ember settling — put through the site's own bed source so that it
    /// inherits the fire's gate, position, rolloff and every gain in the chain. See THE FIRE for why
    /// the layer exists, why it is not on the shared one-shot pool, and what it measures.
    ///
    /// =============================================================================================
    /// <para><b>THE TIMING IS A POISSON PROCESS, SAMPLED ON A GRID.</b> Cells bursting in a log are
    /// independent events at a slowly-changing average rate — the textbook definition of a Poisson
    /// process. That is not a decoration: such a process has a mode at the SHORTEST gap, so it
    /// produces genuine CLUSTERS (two crackles almost together, then a longer nothing), while "the
    /// mean plus or minus 40%" produces a wobbly metronome, and a wobbly metronome is still a
    /// metronome. The user has already condemned one cue in this feature for exactly that — the ice
    /// sound beat at a fixed 0.45 s and he called it "super nervig" — and that cue was deleted rather
    /// than re-timed.</para>
    ///
    /// <para><b>IT USED TO BE EXPRESSED AS A WAITING TIME AND IT IS NOW EXPRESSED AS A PER-TICK
    /// PROBABILITY, which is the same process with no memory.</b> Through ModBuild 225 this method
    /// wrote <c>next = now + EnvSoundSchedule.PoissonGap(mean, draw)</c> and kept <c>next</c>; since
    /// 226 it asks, once per <see cref="FireCrackleTickSeconds"/> tick of the SHARED CLOCK, whether
    /// this site crackles in this tick, with probability <c>tick / mean</c>
    /// (<see cref="EnvSoundSchedule.TickFires"/>). The gaps are then GEOMETRIC — the discrete
    /// exponential — so the clustering survives exactly, and the answer for a given tick no longer
    /// depends on anything this client has done. WHY that mattered is the multiplayer block below.
    /// <c>PoissonGap</c> is untouched and still has a caller: the night calls' offset inside their
    /// 41 s slot.</para>
    ///
    /// <para><b>WHY IT CANNOT WOODPECKER, which is the one thing a per-frame scheduler must not do.</b>
    /// Three independent guards, and none of them is a comparison against a magic number that could
    /// be tuned away:</para>
    /// <list type="number">
    ///   <item>The GAP is bounded below BY GEOMETRY: the event is placed inside the FIRST HALF of its
    ///   tick (<see cref="EnvSoundSchedule.TickOffset"/>), so the latest one crackle can be is half a
    ///   tick in and the earliest the next can be is zero into the following one — a floor of
    ///   0.655 s, for every draw, every mean and every <c>NaN</c>. That is not a clamp that a future
    ///   round can widen without noticing; it is what the half-tick window MEANS.</item>
    ///   <item>The scheduler is an <c>if</c> and not a <c>while</c>: at most ONE crackle per site per
    ///   frame leaves the method, so even a clock that leapt an hour cannot empty a backlog into one
    ///   frame.</item>
    ///   <item>And a backlog cannot exist. There is no "next event" being carried forward to catch up
    ///   with — only the current tick, which is read off the clock. A clock that jumps simply lands on
    ///   a different tick index, and THE JOIN's re-arm swallows one tick per site across it. This is
    ///   what replaced <c>FireCatchUpSeconds</c>, and it is strictly stronger: the old constant made
    ///   the walk re-anchor after falling 1.5 s behind, which was a repair; a schedule with no
    ///   memory has nothing to fall behind.</item>
    /// </list>
    ///
    /// =============================================================================================
    /// <para><b>MULTIPLAYER: IT USED TO BE THE ONE CUE IN THIS FILE TWO CLIENTS DID NOT SHARE, AND
    /// ModBuild 226 ENDS THAT.</b></para>
    ///
    /// <para>USER RULING, hardware, verbatim, and it is quoted in full because it is what overrides
    /// the paragraph that used to stand here: "Genau wie die Easter-Eggs sollen auch die Sounds mit
    /// allen Mitspieler synchronisiert sein die in der selben Map sind. Sind also zwei Spieler in der
    /// Wald Umgebung und dort kommt ein Geräusch eines Tieres aus einer Ecke sollen alle Spieler die
    /// auch im Wald sind zur selben Zeit aus der selben Location denselben Sound hören."</para>
    ///
    /// <para><b>WHAT THAT PARAGRAPH SAID AND WHY IT IS NOT WRONG, ONLY OVERRULED.</b> It said the
    /// crackle's sequence is a WALK — <c>next = now + PoissonGap(...)</c>, each gap drawn from the
    /// last — so two clients that began observing at different clock values sat on different phases
    /// of it; and it argued that nothing could observe the difference, because a crackle marks no
    /// visual, both clients draw from the same distribution at the same rate from the same seats,
    /// and everything that COULD be seen to disagree (whether the fires are lit, where they are, how
    /// fast they crackle) was already a pure function of the shared element channel and the bake.
    /// That argument is still true as far as it goes. What it did not weigh is that the user's
    /// sentence carves out no exception, and that "two players standing at the same fire hearing
    /// different crackles" is a thing a person can NOTICE even when no picture disagrees. He is the
    /// judge of that, so the walk goes.</para>
    ///
    /// <para><b>THE REPLACEMENT IS THE SAME PROCESS WITH NO MEMORY.</b> A Poisson process sampled on
    /// a fixed grid is a BERNOULLI process — each tick of <see cref="FireCrackleTickSeconds"/>
    /// independently carries a crackle with probability <c>tick / mean</c>, and the gaps are
    /// GEOMETRIC, which is the discrete exponential: mode at the minimum, therefore genuine
    /// CLUSTERS, which is the whole of <see cref="EnvSoundSchedule.PoissonGap"/>'s argument for not
    /// using a jittered constant. Every draw is keyed on <c>(tick, site)</c> and on nothing else, so
    /// two clients in the same room resolve the same crackle from the same seat on the same frame,
    /// with nothing added to the packet. See <see cref="EnvSoundSchedule.TickFires"/> for the
    /// arithmetic and <see cref="FireCrackleTickSeconds"/> for what the change cost (a 3.8% slower
    /// rate and an unclamped tail) stated as numbers.</para>
    ///
    /// <para><b>REJECTED: SENDING THE CRACKLES.</b> A per-event record would have kept the walk and
    /// made it agree, and it is forbidden by the standing project rule and by this file's own
    /// premise: the shared clock is already on the wire, and a fact derivable from a number everyone
    /// has is not a fact anyone should transmit. It would also have been strictly worse — a packet
    /// arrives late, and a crackle that arrives late is a crackle in the wrong place.</para>
    ///
    /// <para><b>REJECTED: KEEPING THE WALK AND RE-ANCHORING IT TO THE CLOCK PERIODICALLY.</b> Two
    /// clients would then agree at the anchors and drift between them, which is a defect that only
    /// appears sometimes — the worst kind this feature can ship, because a report about it is
    /// unreproducible.</para>
    /// </summary>
    private static void TickFire(float clock)
    {
        // OFF IS FREE. One float compare while no fire is lit — no loop, no hash, no draw. This is
        // the "ideally, no cost" half of the requirement; the other half (no SOUND) is TickBeds
        // pausing the sources, which FireBed's exact zero is what triggers.
        if (_fireGate <= 0f)
            return;

        // WHICH TICK, ONCE FOR THE ROOM. A clock that is negative, NaN or absurd has no tick and
        // therefore no crackle — see EnvSoundSchedule.TrySlot for why that is a bool rather than a
        // number, and for the sentinel collision the old inline cast could produce.
        if (!EnvSoundSchedule.TrySlot(clock, FireCrackleTickSeconds, out long tick))
            return;
        float tickStart = tick * FireCrackleTickSeconds;

        float fire = Mathf.Clamp01(ElementMood.Live(0));
        // THE ELEMENT IS SCENARIO-WIDE STATE THE GAME KEEPS BIT-IDENTICAL ON EVERY CLIENT, which is
        // what makes `mean` — and therefore the probability below — the same number everywhere. It
        // is the same property Haunt.Resolve leans on for the apparitions' rate.
        float mean = Mathf.Lerp(FireGapCalm, FireGapFull, fire);

        for (int s = 0; s < FireSites; s++)
        {
            Voice? v = _fireVoices[s];
            if (v == null || v.Source == null)
                continue;
            // A PAUSED SOURCE MAKES NO ONE-SHOT. Unity would accept the PlayOneShot and hold it until
            // the source resumed, which is a crackle fired into a room whose fire went out — so the
            // gate is tested where the sound is made as well as where the level is set.
            if (!v.Source.isPlaying)
                continue;

            // ---- has this site already answered this tick? The edge detector is the TICK INDEX and
            // never a timer, so a clock jump (a new owner elected, a scenario reload) simply lands on
            // a different index — the same discipline the drip's period index and the rat's slot use,
            // and the reason none of the three can double-fire or stall.
            if (tick == _lastFireTick[s])
                continue;

            // THE KEY. Every draw for this event comes off it and off nothing else, which is what
            // makes the crackle shared. The three sites can never collide: `tick * 3 + s` is a
            // bijection onto the integers for s in 0..2, so site s holds exactly the keys congruent
            // to s modulo 3, forever, whatever the clock does. Three sites drawing one key would
            // crackle in unison — one loud fire instead of three quiet ones in three places, i.e.
            // the exact failure "verortbar von seinen entsprechenden Quellen" forbids.
            long key = tick * FireSites + s;

            // WHERE IN THE TICK, and this is what keeps the crackles OFF A LATTICE. Firing on the
            // tick boundary itself would put every crackle in the room on a multiple of 1.31 s,
            // which is a metronome, which is the fault a whole cue was deleted for. The offset is a
            // hash of this event's key, inside the tick's FIRST HALF — so the shortest gap two
            // consecutive crackles can have is half a tick (0.655 s) by geometry rather than by a
            // clamp. See EnvSoundSchedule.TickOffset.
            float at = tickStart + EnvSoundSchedule.TickOffset(Haunt.Hash(key, FireWhenChannel),
                                                               FireCrackleTickSeconds);

            // NOT YET ARRIVED IS NOT THE SAME AS ANSWERED, so this path deliberately does NOT latch:
            // the site is simply not due, and it will come back here next frame with the same tick
            // and the same key and reach the same answer. That is safe precisely because everything
            // above is a pure function — there is no draw being consumed and no state advancing, so
            // re-entering costs one hash and decides identically.
            if (clock < at)
                continue;

            // ---- IT IS DUE, SO LATCH FIRST. Every path below this line has already advanced the
            // schedule: a `continue` that skipped the latch would re-enter on the next frame and
            // turn one crackle into a per-frame emitter, which is the woodpecker this whole design
            // exists to make unreachable.
            bool first = _lastFireTick[s] == long.MinValue;
            _lastFireTick[s] = tick;
            if (first)
                continue;   // the tick we walked in on: its crackle already happened

            // ---- does this tick carry one at all? p = tick / mean, so the mean gap is `mean` and
            // the shortest possible gap is half a tick. Drawn AFTER the latch, so a silent tick
            // still advances the schedule.
            if (!EnvSoundSchedule.TickFires(Haunt.Hash(key, FireGapChannel),
                                            FireCrackleTickSeconds, mean))
                continue;

            // ---- and play it. One draw decides WHICH of the two things happened and another which
            // realisation — off two different channels of the same key, which is what stops (say)
            // the loudest crackle being locked to the latest instant in its tick forever.
            //
            // AND THERE IS NO PITCH JITTER HERE, unlike every other one-shot in the file. AudioSource
            // .pitch is a property of the SOURCE and this source is also LOOPING THE ROAR: setting it
            // per crackle would transpose the fire underneath, and setting it back on the next line
            // is not safe either, because a one-shot voice follows its source's pitch while it plays.
            // The variety therefore lives where it costs nothing — four baked crackle realisations
            // and two settles, drawn per event. That is also the stronger form of it: MakeDrips'
            // note is that resampling one buffer is the most recognisable synthetic-audio tell there
            // is, and different realisations are what it recommends instead.
            bool ember = Haunt.Hash(key, FireEmberChannel) < FireEmberShare;
            float pick = Haunt.Hash(key, FireVariantChannel);
            AudioClip? clip = ember
                ? EnvSoundBank.EmberVariant((int)(2f * pick))
                : EnvSoundBank.CrackleVariant((int)(4f * pick));
            if (clip == null)
                continue;

            v.Source.PlayOneShot(clip, ember ? FireEmberLevel : FireCrackleLevel);
        }
    }

    /// <summary>
    /// THE DRIP. Fires when the drop LANDS, not when it forms — <see cref="DripImpactSeconds"/> into
    /// each <see cref="DripPeriod"/>, read from the same constants the shader is baked with rather
    /// than from a second timeline of our own.
    ///
    /// <para>The edge detector is the period INDEX, not an elapsed timer: if the shared clock jumps
    /// (a new clock owner is elected, or a scene reload resets it) the index simply becomes a
    /// different number and the next impact fires normally. A timer would either double-fire or go
    /// silent until it caught up.</para>
    ///
    /// =============================================================================================
    /// <para><b>THE LEVEL, and why it came down with the timbre. ModBuild 147.</b> The user's report
    /// was about the SOUND ("Außerdem gefällt mir das Geräusch nicht") and the rebuild of it is in
    /// <c>EnvSoundBank.MakeDrips</c>. But the standing rule on environment sound is the user's own
    /// and it is about EXPOSURE rather than about quality — "Auch hier sollen die sounds eher dezent
    /// sein und nie aufdringlich überlagernd. Die Umgebung spielt immer noch nur eine zweite Rolle
    /// neben dem eigentlichen Spiel." — and by that measure this cue is the worst offender in the
    /// room whatever it sounds like: at <see cref="DripPeriod"/> = 2.85 s it fires more often than
    /// everything else here PUT TOGETHER (the rat is on 26 s, the haunt on 83 s), and it fired at
    /// 0.13 of a <see cref="MaxEmitterGain"/> of 0.16 — 81% of the ceiling, for the most repeated
    /// event in the environment. That is backwards. Three numbers moved, and they are three separate
    /// arguments rather than one taste:</para>
    /// <list type="number">
    /// <item><b>Gain 0.13 -> 0.085</b>, which is -3.7 dB. On top of it the bank's own rebuild is
    /// -3.3 dB quieter in RMS at the same gain (it normalises to 0.78/0.52/0.70 instead of 0.90, and
    /// two of the three variants are much shorter events), so the source level is -7.0 dB before the
    /// rolloff below. The PERCEIVED drop is larger again, because loudness is not level: the old
    /// clip put its energy where the ear is most sensitive and the new one does not.</item>
    /// <item><b>maxDistance 9 m -> 6 m.</b> Nine perceived metres is most of a cellar — the drip was
    /// audible from anywhere in the room, which made it ambience rather than a thing in a corner.
    /// Six is "you hear it near the puddle", which is also what makes it worth having: the whole
    /// feature's premise is "verortbar von seinen entsprechenden Quellen", and a sound that reaches
    /// everywhere is not located anywhere.</item>
    /// <item><b>minDistance 0.5 m -> 0.4 m.</b> Under logarithmic rolloff the level past the minimum
    /// goes as <c>min/d</c>, so lowering the minimum steepens the whole curve: at 2 perceived metres
    /// this is a further -1.9 dB, and at arm's length from the puddle it is unchanged. The drip
    /// gets quieter with distance FASTER, which is the shape you want for something that repeats.</item>
    /// </list>
    /// <para><b>Net, measured off the finished buffers rather than estimated:</b> -8.9 dB at a
    /// typical 2 perceived metres for the AVERAGE drop, and -13.2 dB for the quiet no-bubble one,
    /// which is a third of them. That is before the duck, the master dial and the player's own two
    /// volume sliders, all of which still apply on top. Erring quiet is deliberate and is the
    /// standing rule read literally: a drip you notice every 2.85 s is a failure even when it is the
    /// right drip, and the failure mode in the other direction — a drop you have to look for — costs
    /// nothing, because the picture is already telling you it happened.</para>
    ///
    /// <para><b>AND THE VARIATION MOVED FROM PITCH TO TIMBRE.</b> The bank now bakes three
    /// realisations of the drop (one of which entrains no bubble and therefore has no tone at all);
    /// this picks between them by hash. The pitch jitter is NARROWED to compensate rather than
    /// removed — ±3% instead of ±6% — because the pitch knob resamples the whole clip, so a wide
    /// setting transposes the flagstone knock and the splash along with the bubble, and the floor
    /// does not change note between drops. A small residue is still worth keeping: it decorrelates
    /// two drops that drew the same variant, and 3% is under the threshold where the ear hears a
    /// transposition rather than a difference.</para>
    ///
    /// <para>The variant draw uses hash CHANNEL 7 and the pitch keeps channel 2
    /// (<see cref="Hash01"/>). Two draws off one channel would make the choice and the pitch
    /// perfectly correlated — variant 0 would forever be the flattest drop — which is a subtler way
    /// of having no variation at all. Both are pure functions of the period index, so every client
    /// hears the same drop with the same colour on the same frame, with nothing on the wire.</para>
    /// </summary>
    private static void TickDrip(float clock)
    {
        // THE PERIOD INDEX, THROUGH THE ONE PIECE OF SLOT ARITHMETIC THIS FEATURE HAS. It used to
        // be `(long)Mathf.Floor(shifted / DripPeriod)` written out here, which is correct for every
        // clock a healthy session produces and is UNSPECIFIED for a NaN or an infinity: on x64 that
        // cast yields long.MinValue, which is this method's own "not observing yet" sentinel three
        // lines down. A poisoned clock would therefore have re-armed the drip rather than skipping
        // one, silently. TrySlot refuses instead, and it also subsumes the `shifted < 0` guard this
        // block used to open with — see EnvSoundSchedule.TrySlot.
        if (!EnvSoundSchedule.TrySlot(clock - DripImpactSeconds, DripPeriod, out long idx))
            return;
        if (idx == _lastDripIndex)
            return;

        bool first = _lastDripIndex == long.MinValue;
        _lastDripIndex = idx;
        if (first)
            return;   // never fire on the frame we start observing — that drop already landed

        Transform? at = _dripNode;
        if (at != null)
        {
            int variant = (int)(3f * Haunt.Hash(idx, DripVariantChannel));
            PlayShot(EnvSoundBank.DripVariant(variant), at.position, 0.085f, 0.4f, 6f,
                     pitch: 0.97f + 0.06f * Hash01(idx));
        }
    }

    /// <summary>
    /// Hash channel for "which of the three drops this one is".
    ///
    /// <para>A CHANNEL IS NOT AN EXCLUSIVE RESOURCE, and the table is worth stating correctly because
    /// the obvious reading of it is wrong. <c>EnvHaunt.cginc</c>'s channel table (:186-192) already
    /// spends 0 RATE, 1 PICK, 3 START, 4 DUR, 5/6/7 VARA/VARB/VARC, and this file's own rat cue
    /// draws on 5 and 6 as well — so every channel is taken, and the rat has been sharing two of
    /// them with the apparitions since they were written. That is sound and not an oversight: the
    /// channels decorrelate two draws made from the SAME index, and these subsystems index different
    /// things (a drip period, a rat slot, a haunt slot). Two values that are never compared cannot
    /// be seen to correlate, and nothing in the mod ever compares them.</para>
    ///
    /// <para>7 is chosen for the drip anyway, in preference to 2: <see cref="Hash01"/> is 2, and the
    /// pitch jitter on the very same drop is drawn from it. THAT pair is compared — by the ear, on
    /// one event — and drawing both from one channel would lock the quietest variant to the lowest
    /// pitch forever, which is a subtler way of having no variation at all.</para>
    /// </summary>
    private const float DripVariantChannel = 7f;

    /// <summary>
    /// THE RAT — "Mäusepiepen", plus its feet. The schedule is
    /// <c>EnvCritter.shader</c>'s, mirrored: a slot is quiet if <c>H(n,0) &lt; _Skip</c> and
    /// otherwise the crossing starts <c>_Period * (_Timing.x + _Timing.y * H(n,1))</c> seconds into
    /// it (:374, :395). The hash is <see cref="Haunt.Hash"/> — literally the same cascade the haunt
    /// schedule uses, which is why there is one implementation of it and not two.
    /// </summary>
    private static void TickRat(float clock)
    {
        // THE CROSSING SLOT, through EnvSoundSchedule.TrySlot rather than an inline cast — see
        // TickDrip for the sentinel collision that motivated moving this arithmetic into one place.
        if (!EnvSoundSchedule.TrySlot(clock, RatPeriod, out long slot))
            return;

        bool first = _lastRatSlot == long.MinValue;
        if (slot == _lastRatSlot)
            return;

        // The slot's crossing may not have started yet — this is not "the slot changed, play", it is
        // "the crossing inside this slot has begun".
        float start = RatPeriod * (RatStartLo + RatStartSpan * Haunt.Hash(slot, 1f));
        if (clock - slot * RatPeriod < start)
            return;

        _lastRatSlot = slot;
        if (first)
            return;

        // A skipped slot is a slot the animal stays in its hole for. Silence is the correct sound.
        if (Haunt.Hash(slot, 0f) < RatSkip)
            return;

        Transform? at = _ratNode;
        if (at == null)
            return;

        PlayShot(EnvSoundBank.Bank(EnvSoundClip.Skitter), at.position, 0.075f, 0.4f, 7f,
                 pitch: 0.95f + 0.14f * Haunt.Hash(slot, 5f));
        // ...and the squeak a beat later, not on top of it. An animal that squeaks on its first
        // footfall reads as a toy; one that squeaks somewhere in the middle of its run reads as an
        // animal. The delay rides the same hash, so every client hears it at the same instant.
        _squeakAt = clock + 0.35f + 1.6f * Haunt.Hash(slot, 6f);
        _squeakFrom = at;
    }

    private static float _squeakAt = float.NaN;
    private static Transform? _squeakFrom;

}
