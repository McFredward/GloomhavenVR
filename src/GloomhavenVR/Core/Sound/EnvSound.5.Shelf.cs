using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// EnvSound part 5 of 5 — see EnvSound.1.Core.cs for the type's own doc
// and for why the parts are digit-prefixed.
internal static partial class EnvSound
{
    // ---- the bookshelf's contacts ----------------------------------------------------------------
    //
    //  USER REPORT, ModBuild 147 hardware, verbatim: "Beim Umfallen des Regals sollte es schon ein
    //  Geräusch beim Impact auf dem Boden geben." — "When the shelf falls over there really should
    //  be a sound at the impact on the floor."
    //
    //  HE IS RIGHT AND THE CAUSE IS A SCHEDULING ERROR, not a missing clip: the bank has had `Fall`
    //  and `Settle` since the feature shipped. The shipped cue fired at the event START plus a
    //  0.15 s lead, and the clip put its thud 0.85 s into itself, so the only impact-like sound in
    //  the whole event happened about ONE SECOND in — at which point EnvShelfTip's curve has the
    //  shelf leaned over by roughly one degree. It does not reach the floor until 4.68 s. So the
    //  thud was 3.7 SECONDS EARLY, which is far too much to read as "early": it reads as a different
    //  sound belonging to something else, and then the actual collision is silent.
    //
    //  WHERE THE FLOOR IS, IN PHASE. Read directly off unity/.../Environments/EnvShelfTip.cginc,
    //  which is the ONE authority on the pose (the shelf itself and every rider — wax, flames,
    //  halos — take their transform from GhvrShelfTip, so there is no second curve to disagree
    //  with). Its schedule, in phase of the 26.002 s envelope:
    //
    //      GHVR_TIP_FALL  = 0.180   the topple, on the pendulum's exact separatrix -> 4.68 s
    //      GHVR_TIP_LAND  = 0.024   one ballistic rebound, 1.9 deg              -> 0.624 s
    //      GHVR_TIP_ARC   = 0.204   FALL + LAND: flat on the floor at last      -> 5.30 s
    //      GHVR_TIP_RISE  = 0.620   it lies there until here                    -> 16.12 s
    //                               then the same arc RUN BACKWARDS at 0.537x   -> 9.88 s
    //
    //  So there are exactly TWO contacts on the way down and the file says when: the arrival at
    //  0.180, and — because the landing term is a projectile arc 4w(1-w) that leaves the floor and
    //  comes back — a second, much softer touch at 0.204 when the rebound lands. Both are scheduled
    //  below. The rebound's relative energy is not guessed either: the cginc derives the restitution
    //  as 0.182 from its own impact rate and rebound height, so the second contact arrives at 0.182
    //  of the first speed, which is 3.3% of the energy, -14.8 dB. It is scheduled at 0.18 of the
    //  gain rather than at 0.033 because loudness is not energy — 0.18 of the amplitude is the
    //  honest translation of a 0.182 velocity ratio for an impact.
    //
    //  WHY THE LEAD CUE IS NOW A CREAK. Card 5 used to spend its one cue on `Fall` at the start.
    //  With the thud moved to where the thud belongs, the start needs something or the first 4.7 s
    //  of the single biggest event in the room are silent — and a bookcase committing to a topple
    //  is not silent: its joints take a load they have never taken, which is stick-slip, which is
    //  exactly what MakeCreak is. It is also QUIETER than the cue it replaces (0.045 against
    //  0.080), so the event now starts under the bed and ends with a bang instead of the other way
    //  round, which is the coordinator's rule for these cues stated in physical terms.
    //
    //  REJECTED:
    //    * PUTTING THE IMPACT ON A TIMER STARTED BY THE LEAD CUE. It would drift from the picture
    //      the moment DurationMul changed (Ice stretches every event by up to 35%), and the whole
    //      point of reading the phase constants is that a stretched event stretches the sound with
    //      it.
    //    * A THIRD CONTACT. The cginc rules it out by name: the second rebound would be 0.182^2 =
    //      3% of the first, "0.07 degrees, two millimetres — drawing it would be drawing a lie".
    //      The same argument applies to hearing it, at -29 dB.
    //    * SCHEDULING THE RIGHTING'S OWN TOUCH SEPARATELY. The recovery has one too — the reversed
    //      rebound, 1.162 s into it — but it does not need a cue, because it is INSIDE the Settle
    //      clip: MakeSettle's single contact was moved to exactly 1.162 s for this reason. One
    //      sound, one clip, one time.

    /// <summary>The shelf's own phase schedule, MIRRORED from
    /// <c>unity/.../Environments/EnvShelfTip.cginc</c>'s <c>GHVR_TIP_*</c> defines. Duplicated here
    /// for the same reason <see cref="DripPeriod"/> is duplicated from the bake: they are a CONTRACT
    /// with a file that states them from its side, and a sound that invented its own timeline would
    /// drift away from the picture. If the cginc's constants move, these move with them.</summary>
    private const float ShelfArrivalPhase = 0.180f;
    private const float ShelfReboundPhase = 0.204f;
    private const float ShelfRisePhase = 0.620f;

    /// <summary>Amplitude of the rebound's second contact against the first. The cginc derives a
    /// coefficient of restitution of 0.182 from the fall's impact rate and the rebound's height;
    /// this is that number, used as an amplitude ratio.</summary>
    private const float ShelfReboundLevel = 0.18f;

    /// <summary>
    /// The impact's minDistance, in PERCEIVED metres. Everything inside this radius hears the bang at
    /// full level; past it Unity's logarithmic curve takes min/d as usual, out to the same 24 m
    /// maximum every other cue has.
    ///
    /// <para><b>8 m IS THE CELLAR, NOT A FUDGE.</b> The room the bake builds is about 10.7 x 10 m of
    /// floor, so its half-diagonal is 7.3 m: a source at 8 m of minimum is flat everywhere INSIDE the
    /// room and starts falling only outside it. That is the whole intent, stated as a number — the
    /// permission granted was for the bang to be loud, and a bang whose audibility depends on which
    /// corner of a small stone cellar the player happens to be leaning over is not loud, it is a
    /// lottery. The three previous rounds all lost most of the exception to exactly that lottery
    /// (see <see cref="ShelfImpactGain"/> for the arithmetic off the user's own log).</para>
    ///
    /// <para><b>AND IT IS THE PHYSICALLY HONEST CURVE HERE, which is why it is not simply "turn the
    /// rolloff off".</b> Two independent reasons, both specific to this sound:
    /// <list type="bullet">
    ///   <item>THE SOURCE IS NOT A POINT. It is a 2.06 m carcass landing on its whole face at once.
    ///   The 1/d law is the far field of a point source; the near field of a radiator that size does
    ///   not begin to obey it until several metres out.</item>
    ///   <item>THE ROOM IS A SEALED STONE BOX. Past the critical distance of a reverberant space the
    ///   direct field stops dominating and the level goes FLAT — that is what a bang in a cellar
    ///   does, and it is the one thing about this event everybody has already heard in a real
    ///   building. This project ships no reverb (see the class doc), so a flat rolloff inside the
    ///   room is the cheapest correct model of the room, not the absence of a model.</item>
    /// </list></para>
    ///
    /// <para>maxMeters is untouched at 24: the permission is for the impact to be loud where it is,
    /// not for it to reach further.</para></summary>
    private const float ShelfImpactMinMeters = 8f;

    /// <summary>...and the righting's, which is NOT part of the exception and is deliberately
    /// smaller: 6 m still covers the room's centre, so the recovery does not vanish when the contact
    /// point is corrected, but it audibly falls off across the floor where the bang does not. A mass
    /// dragging itself upright is a local event; a bang is not.</summary>
    private const float ShelfSettleMinMeters = 6f;

    /// <summary>
    /// Put the bookshelf's three remaining sounds on the queue the moment its cue fires: the arrival
    /// on the floor, the rebound's second contact 0.624 s later, and the righting. See the block
    /// comment above for where every one of those times comes from.
    /// </summary>
    /// <param name="start">The event's own start on the shared clock — <c>Slot.StartClock</c>, the
    /// SAME number the shader's phase is measured from.
    ///
    /// <para>IT IS PASSED RATHER THAN RECONSTRUCTED FROM `now - lead`, and that is the difference
    /// between right and nearly right. The two agree on an event this feature watched from its
    /// beginning, and they do NOT agree when the environment stands up in the middle of one: the
    /// caller deliberately allows a cue to fire late (up to the whole run plus 1.5 s), so `now`
    /// can be twenty seconds past the start, and a reconstruction would then schedule an arrival
    /// twenty seconds after the shelf had already arrived. Reading the start directly makes every
    /// contact an ABSOLUTE time on the shared clock, so a client that joined late simply finds them
    /// already past and drops them — which is what should happen, and is what
    /// <see cref="TickDeferredCues"/>'s staleness guard does with no extra code.</para></param>
    /// <param name="runs">The event's real length: the card's authored envelope times the slot's
    /// <c>DurationMul</c>. Ice stretches it by up to 35%, and everything here scales with it because
    /// the shader's phase does.</param>
    /// <param name="now">Shared-clock seconds this frame. Diagnostics only.</param>
    /// <param name="pos">ONLY A FALLBACK, and it is passed rather than resolved here so the log line
    /// can print both: it is <see cref="HauntPosition"/>'s answer, the renderer bounds centre of
    /// whatever apparition node resolved, which for card 5 is the welded catalogue's middle and is
    /// not the shelf at all. The contacts are placed from the SHELF'S OWN NODE and only fall back to
    /// this if that node is missing; see <see cref="ShelfFloorContact"/>.</param>
    private static void ScheduleShelfContacts(float start, float runs, float now, Vector3 pos)
    {
        float arrival = start + runs * ShelfArrivalPhase;
        float rebound = start + runs * ShelfReboundPhase;
        float rise = start + runs * ShelfRisePhase;

        // THE SOUND COMES FROM WHERE THE CARCASS MEETS THE FLAGSTONES, and getting that point right
        // is fault (a) of three in the ModBuild 149 report. See ShelfFloorContact.
        Vector3 floor = ShelfFloorContact(pos, out string via);

        // THE BANG. Everything about this call is the user's exception being spent: the gain is
        // ShelfImpactGain rather than a fraction of MaxEmitterGain, the ceiling passed to PlayShot is
        // ShelfImpactCeiling rather than MaxEmitterGain, the duck has a floor of its own, and the cue
        // is LABELLED so that the next hardware round can read the played level and the clip's
        // measured peak straight out of Player.log instead of inferring them.
        //
        // minMeters GOES UP, from 1.2 to ShelfImpactMinMeters, and it is the single biggest level
        // change in this edit — bigger than the gain constant. The 1.2 m minimum was chosen on the
        // argument that "a bookcase hitting a stone floor is emphatically a thing that happens in ONE
        // PLACE", which is true of the EVENT and false of the SOUND FIELD. Under logarithmic rolloff
        // the level past the minimum goes as min/d, so at the 7.3 perceived metres the player
        // actually stood at (Player.log, Heartbeat #20 against the corrected contact point) a 1.2 m
        // minimum is -15.6 dB — the spatialiser was taking more off the bang than the whole
        // exception put on it. See ShelfImpactMinMeters for why the honest curve for THIS sound in
        // THIS room is flat.
        Defer(arrival, EnvSoundClip.Fall, floor,
              gain: ShelfImpactGain, minMeters: ShelfImpactMinMeters, maxMeters: 24f, pitch: 1f,
              cap: ShelfImpactCeiling, label: "SHELF ARRIVAL ON THE FLOOR (the loud one)",
              duckFloor: ShelfImpactDuckFloor);

        // Pitched slightly UP, because a lighter contact of the same body excites its higher modes
        // relatively more — the low mode needs momentum the rebound no longer has. 1.06 is small
        // enough not to read as a transposition and large enough to read as a lighter touch. It
        // rides the same exception because it is the same contact 0.624 s later — 0.18 of it.
        Defer(rebound, EnvSoundClip.Fall, floor,
              gain: ShelfImpactGain * ShelfReboundLevel, minMeters: ShelfImpactMinMeters,
              maxMeters: 24f, pitch: 1.06f, cap: ShelfImpactCeiling,
              label: "SHELF REBOUND (second contact)", duckFloor: ShelfImpactDuckFloor);

        // The righting is NOT part of the exception: it is a mass coming slowly back up, the half
        // that is meant to be unsettling rather than loud, it ducks the whole way and it stays inside
        // the ordinary budget. Its rolloff still has to move with the position fix, though, or the
        // correction would silently DELETE it: at 2 perceived metres of minimum it reached 0.026 of
        // full scale from the old (wrong, and accidentally nearby) point and would reach 0.007 from
        // the right one. ShelfSettleMinMeters keeps it where it was — audible, and still plainly
        // further away than the bang, which is what it is.
        Defer(rise, EnvSoundClip.Settle, floor,
              gain: 0.055f, minMeters: ShelfSettleMinMeters, maxMeters: 24f, pitch: 1f,
              label: "SHELF RIGHTING");

        // ONE LINE, ONCE PER SHELF EVENT — which is at most once every ~83 s in the cellar and only
        // for one card in six. It earns its place because the defect it replaces was INVISIBLE from
        // a log: the old cue fired, logged, and sounded, and nothing anywhere said that the thud
        // inside it had landed 3.7 s before the shelf did. These four times against the picture are
        // the whole verification, and nobody working on this can hear it. Each of the three cues
        // ALSO logs when it actually fires or is dropped (see Deferred.Label), so a missing bang can
        // now be attributed rather than guessed at.
        VRLog.Info("Core", $"ENV SOUND shelf contacts scheduled from EnvShelfTip's own phase — the "
                           + $"event starts {start:F2}s and runs {runs:F2}s (26.002s authored x the "
                           + "slot's DurationMul, which Ice stretches by up to 35%), so: creak now "
                           + $"at {now:F2}s (authored lead {ShelfLead:F2}s, the "
                           + $"carcass taking the lean), ARRIVAL ON THE FLOOR at {arrival:F2}s "
                           + $"(phase {ShelfArrivalPhase:F3}, +{runs * ShelfArrivalPhase:F2}s), the "
                           + $"rebound's second contact at {rebound:F2}s (phase {ShelfReboundPhase:F3}, "
                           + $"{ShelfReboundLevel:F2}x the level), and the righting at {rise:F2}s "
                           + $"(phase {ShelfRisePhase:F3}), whose own 1.162s contact is inside the "
                           + "Settle clip. THE ARRIVAL IS THE ONE SOUND IN THIS FEATURE THE USER HAS "
                           + "GIVEN WRITTEN PERMISSION TO BE LOUD (\"eine Ausnahmegenehmigung ... "
                           + "einen lauten Knall Sound ... in dem Moment in das Regal den Boden "
                           + $"berührt\"): gain {ShelfImpactGain:F2} against the {MaxEmitterGain:F2} "
                           + "every other emitter is capped at, which is "
                           + $"{20f * Mathf.Log10(ShelfImpactGain / 0.080f):F1} dB over the cue he "
                           + $"could not hear, it ducks no further than {ShelfImpactDuckFloor:F2} "
                           + $"(everything else goes to {DuckFloor:F2}), and its rolloff is FLAT out "
                           + $"to {ShelfImpactMinMeters:F1} perceived m so the whole cellar is inside "
                           + "it. PLACED AT THE CONTACT: "
                           + $"{floor:F2}, derived from node '{via}' at {(_shelfNode != null ? _shelfNode.position.ToString("F2") : "(none)")} "
                           + $"along its own forward {(_shelfNode != null ? _shelfNode.forward.ToString("F2") : "(none)")} by "
                           + $"{ShelfHalfDepthMeters + 0.5f * ShelfHeightMeters:F2} authored m — the "
                           + "CENTRE OF THE FALLEN FACE, i.e. where two things touch, not the "
                           + $"apparition catalogue's middle ({pos:F2}), which is what the last three "
                           + "rounds used and which is not even the same object.");
    }

    /// <summary>The lead on card 5's own creak, named so that <see cref="CueFor"/> and the
    /// scheduling log line cannot fall out of step about it.</summary>
    private const float ShelfLead = 0.15f;

    /// <summary>
    /// Which cue an apparition gets, and how it is placed. The ids are the content lane's array
    /// order, MIRRORED by <see cref="Haunt.CardSeconds"/>, which is the table this one is kept in
    /// step with (grep HAUNT FORCE ID TABLE in BuildEnvironmentRooms.cs for the other side).
    ///
    /// <para><b>RETURNS FALSE FOR A SILENT CARD, and silence is now a first-class answer rather
    /// than a gain of zero.</b> FOUR of the nine cards are deliberately mute — see the cases
    /// themselves. A bool is used rather than a <c>EnvSoundClip.None</c> member because
    /// <see cref="EnvSoundBank.Bank"/> is a total function with a <c>default</c> arm: a "None" that
    /// fell through it would return the SETTLE clip, which is the loudest possible way to express
    /// "no sound".</para>
    ///
    /// <para><b>THE ROOM'S CARD COUNTS ARE NOT SIX AND SIX, AND TWO OF THE CARDS THAT REMAIN ARE
    /// EMPTY.</b> ModBuild 147 re-cut both catalogues (<see cref="Haunt.CardSeconds"/>): the cellar
    /// has six cards and the swamp has three — what used to be the swamp's cards 1, 2 and 3 are now
    /// 0, 1 and 2. ModBuild 149 then DELETED two apparitions outright, the swamp's card 0 (Eyes) and
    /// the cellar's card 2 (the stair-top "door", which rendered as a cylinder standing in the moon
    /// puddle). Both CARDS survive as inert placeholders because the shader requires a card count
    /// divisible by three, and both are silent here: a card with no apparition behind it must
    /// produce no cue, or the sound becomes the only evidence of something that is not there. The
    /// unreachable arms for a swamp card 3+ are kept as a <c>default</c> so that a catalogue which
    /// grows back does not crash into a missing case.</para>
    /// </summary>
    /// <returns>True if there is a cue to play. False means this apparition is SILENT by design and
    /// the caller must play nothing at all.</returns>
    private static bool CueFor(SkyStyle style, int card, out EnvSoundClip clip, out float gain,
                               out float lead, out float minM, out float maxM)
    {
        clip = EnvSoundClip.Breath;
        gain = 0.06f;
        lead = 0f;
        minM = 1.5f;
        maxM = 18f;

        if (style == SkyStyle.Cellar)
        {
            switch (card)
            {
                // 0 WINDOW — a head and shoulders at the barred window. A breath outside the bars,
                // slightly BEFORE it arrives: the player should look up and then find it there.
                case 0: clip = EnvSoundClip.Breath; gain = 0.055f; lead = -1.1f; minM = 1f; maxM = 12f; return true;

                // 1 HANDS — SILENT. USER RULING, ModBuild 147 hardware, verbatim: "Lösche jeglichen
                // Sound dazu, hier macht ein Sound keinen Sinn!" ("Delete every sound for this, a
                // sound makes no sense here!") — and he is right on the physics, which is why this
                // is a deletion and not a quieter clip. Handprints BLOOM on wet stone; nothing
                // touches it. The shipped cue was a wet palm dragging, which asserts a contact the
                // picture explicitly does not show — the whole point of the apparition is that the
                // hands are not there. A sound that describes an absent hand converts the thing
                // that is frightening about the image into an ordinary event.
                case 1: return false;

                // 2 — NOTHING IS THERE ANY MORE. ModBuild 149 DELETED this apparition outright: the
                // stair-top door never rendered as a door, it rendered as a cylinder rising out of
                // the moon puddle, and the content lane removed it. The card itself has to STAY in
                // the array as an inert placeholder because the shader requires a card count that is
                // a multiple of three — but a placeholder that still made a noise would be the worst
                // of both worlds: a creak from a doorway with nothing in it, on an event the player
                // cannot see, which is a cue for an apparition that does not exist.
                //
                // The prose that used to sit here described a door taking its weight on old hinges.
                // It is deleted rather than reworded because it was a description of a thing that is
                // gone, and a stale description survives longer than the code it describes.
                case 2: return false;

                // 3 TREMBLE — SILENT. USER RULING, ModBuild 147 hardware, verbatim: "Lösch bei den
                // Spinnweben das Geräusch, das hört sich an wie eine Schlange." ("Delete the sound
                // on the cobwebs, it sounds like a SNAKE.") That is a precise report and the cue
                // deserved it: Drag is band-limited noise sweeping 3200 -> 1100 Hz over 1.4 s with a
                // 31 Hz roughness on it, which is a textbook hiss with a rattle in it. Nothing was
                // going to rescue that shape at that length.
                //
                // WHAT IT COSTS, stated because it is a real cost and not nothing: this was the one
                // card whose note said "the sound carries the whole event", since the apparition
                // draws no geometry — every cobweb in the room simply shivers. With the cue gone,
                // the event is now carried entirely by that shiver. That is acceptable and probably
                // better: a shiver you catch out of the corner of your eye is exactly the "something
                // you are not sure you saw" these are built to be, and the alternative the user
                // actually heard was a snake in the cellar.
                case 3: return false;

                // 4 STAIR — something too tall crosses the doorway in 0.7 s. A stair board taking
                // weight, just BEFORE: the fastest event in the room needs the player already looking.
                case 4: clip = EnvSoundClip.Creak; gain = 0.055f; lead = -0.55f; minM = 1.2f; maxM = 14f; return true;

                // 5 SHELF — it tips over, hits the floor 4.68 s later, rebounds, lies there and
                // rights itself. THIS CUE IS ONLY THE FIRST OF FOUR: the carcass creaking as it
                // commits to the lean. The three contacts are scheduled from the shader's own phase
                // constants by ScheduleShelfContacts — see the block comment there, and see the user
                // report that made it necessary.
                //
                // Its rolloff is ShelfSettleMinMeters, not 2 m, for the reason the righting's is:
                // the cue now sounds from the SHELF (TickHaunt) instead of from the middle of the
                // room, which is several metres further from the player, and a 2 m minimum would
                // have made the position fix silently delete the lead cue as well.
                default: clip = EnvSoundClip.Creak; gain = 0.045f; lead = ShelfLead; minM = ShelfSettleMinMeters; maxM = 24f; return true;
            }
        }

        switch (card)
        {
            // 0 — NOTHING IS THERE ANY MORE. ModBuild 149 DELETED the forest's Eyes, exactly as it
            // deleted the cellar's card 2. The card stays in the array as an inert placeholder (the
            // shader needs a card count divisible by three) and it makes NO sound: a dry shift of
            // leaves coming out of an empty understory is a cue with nothing behind it, which is
            // precisely the "converts a doubt into an event" failure these cues are built to avoid,
            // with the event missing as well.
            case 0: return false;
            // 1 WATCHER — a 2.7 m figure that does nothing at all. A breath from far too high up,
            // late, and quiet enough to be deniable.
            case 1: clip = EnvSoundClip.Breath; gain = 0.038f; lead = 2.2f; minM = 3f; maxM = 26f; return true;
            // 2 CROSS — a body walking 6.8 m through the understory. The sound cannot chase it, so
            // it arrives just after: the undergrowth closing behind whatever went through.
            default: clip = EnvSoundClip.Drag; gain = 0.055f; lead = 0.20f; minM = 2f; maxM = 18f; return true;
        }
    }

    /// <summary>
    /// Where an apparition's cue comes from.
    ///
    /// <para><b>THIS IS THE ONE INTEGRATION SEAM IN THE FEATURE, and it is deliberately a search
    /// rather than a table of coordinates.</b> Another lane is rebuilding all twelve apparitions as
    /// real 3D geometry in this same round, and several of them MOVE — so the authored positions in
    /// <c>BuildEnvironmentRooms.cs</c>'s card arrays are about to change, and typing them here would
    /// have baked in numbers with a known expiry date. Instead the node is looked up by name at
    /// runtime, through a fallback chain, and <see cref="LogBuilt"/> reports which link of that
    /// chain actually resolved — so a hardware log says plainly whether the cue is on the apparition
    /// or on the catalogue's centre.</para>
    ///
    /// <para>The chain is: an exact per-card node (<c>Haunt0</c>…<c>Haunt5</c>), then any descendant
    /// whose name starts with <c>Haunt</c>, then the welded catalogue node the current bake produces
    /// (<c>Haunts</c>, BuildEnvironmentRooms.cs:4759), then the room root. Only the last of those is
    /// a real degradation, and it degrades to "the sound is in the room" rather than to silence.</para>
    ///
    /// <para><b>IT NO LONGER HANDS BACK A FLOOR, and that removal is a bug fix rather than a
    /// tidy-up.</b> It used to return <c>bounds.min.y</c> of whatever resolved, and the bookshelf's
    /// contacts used that as "the flagstones". On the welded catalogue that number is not a floor at
    /// all — the mesh covers travelling apparitions and the bake expands its bounds to cover the
    /// travel — and it put the bang metres under the room. Nothing asks this function for a floor
    /// any more; the one caller that needed one asks the object that has one (see
    /// <see cref="ShelfFloorContact"/>).</para>
    /// </summary>
    private static Vector3 HauntPosition(int card)
    {
        Transform room = _root!.transform.parent!;

        Transform? exact = Find(room, "Haunt" + card);
        if (exact != null)
            return Center(exact);

        foreach (Transform t in FindByPrefix(room, "Haunt"))
            return Center(t);

        return Center(room);

        // The RENDERER's bounds centre, not the transform origin: a welded apparition card's pivot
        // is wherever the mesh builder happened to leave it, while the bounds centre is where the
        // thing visibly IS. Falls back to the transform when there is nothing to measure, which is
        // the honest answer for "we could not measure it".
        static Vector3 Center(Transform t)
        {
            var r = t.GetComponentInChildren<Renderer>();
            return r == null ? t.position : r.bounds.center;
        }
    }

    // ---- WHERE THE BOOKCASE ACTUALLY IS ------------------------------------------------------------
    //
    //  THE THIRD ROUND ON THIS BUG, and the first two both placed the sound somewhere else entirely.
    //  Player.log:7391, verbatim:
    //
    //      PLACED ON THE FLOOR at (-43.49, -9.97, 32.05) (apparition node 'Haunts',
    //      bounds centre (-43.49, 6.98, 32.05))
    //
    //  BOTH COORDINATES ARE WRONG AND FOR TWO DIFFERENT REASONS.
    //
    //   * x/z. There is no `Haunt5` node, so HauntPosition fell through to the second link of its
    //     chain and returned the WELDED HAUNT CATALOGUE's bounds centre — a single mesh covering
    //     every apparition in the room, whose centre is the middle of the cellar. And card 5 is not
    //     even IN it: the bake reports "[5] grp2 Shelf kind 4 — 0 verts, bbox 0.00x0.00x0.00 m",
    //     because the shelf is not drawn by the catalogue at all. It is its own prop
    //     (Env_C_ShelfTip) posed by its own shader. So the fallback was averaging the positions of
    //     the apparitions that are not the shelf.
    //   * y. `bounds.min.y` of that same catalogue, which is not a floor: the welded mesh contains
    //     TRAVELLING apparitions and the bounds the bake expands to cover their travel, so its
    //     bottom is far under the flagstones. The number the log prints (17 world units below the
    //     mesh's own centre, in a room whose whole ceiling is 3.4 m) says so on its face.
    //
    //  Together they put the bang about 107 world units — nearly eight perceived metres — from where
    //  the bookcase hits, on the far side of the room and below the floor. It happened to land NEAR
    //  THE PLAYER, which is why the level looked better in the log than it was; see ShelfImpactGain.
    //
    //  THE FIX IS TO ASK THE SHELF. `Shelf` is a real node with a real transform: BuildTippingShelf
    //  places it at CellarShelfAt with the room's own yaw, and the fall is READ OFF THAT TRANSFORM
    //  ("The shelf falls along its own forward", BuildEnvironmentRooms.cs). So the transform carries
    //  every fact this needs — where the footprint is, where the floor is, and which way the carcass
    //  goes — in world space, already scaled and yawed by the room frame, without this file knowing
    //  the room's placement, its 11.905x art scale or the rig scale.
    //
    //  WHY NOT THE RENDERER BOUNDS, which is what every other cue here uses. Because the shelf's mesh
    //  bounds are DELIBERATELY A LIE: HauntPropMesh ends with `bb.Expand(2.0f * bb.size.y)` so that a
    //  bookcase lying two metres from where it stood is not frustum-culled. Its centre is still the
    //  standing carcass but its min.y is 2 m of authored art below the flagstones — the same trap the
    //  catalogue's bounds set, in the shelf's own mesh. The transform has no such expansion.

    /// <summary>How far along its own forward the carcass's fallen face is centred, in AUTHORED
    /// METRES of the room's art — the room frame scales it (x11.905 in the shipped cellar) and this
    /// file never has to know by how much.
    ///
    /// <para>DERIVED, from the two numbers BuildTippingShelf measures off the placed prop and prints:
    /// the standing box is 0.58 x 2.06 x 1.37 m and the fall is about the base edge on its forward
    /// face, i.e. half the depth (0.29) ahead of the anchor. Rotated 90 degrees about that edge the
    /// carcass lies flat, reaching one full height (2.06) further along the same direction — so the
    /// FACE that slaps the flagstones spans [0.29, 2.35] and its centre is at 0.29 + 2.06/2 = 1.32.
    /// An impact sounds from where two things touch, and for a body that lands flat that is the whole
    /// face, whose acoustic centre is its middle. It is NOT the anchor (that is where the shelf
    /// STOOD, a metre and a half behind the contact) and it is NOT the top board (that is one end of
    /// it).</para>
    ///
    /// <para>MIRRORED, like <see cref="ShelfArrivalPhase"/> and <see cref="DripPeriod"/>: if the bake
    /// re-cuts the carcass, this moves with it. The bake states both halves on its own
    /// "Cellar SHELF is now a haunt" line, so the two can be checked against each other from one
    /// log.</para></summary>
    private const float ShelfHalfDepthMeters = 0.29f;
    private const float ShelfHeightMeters = 2.06f;

    /// <summary>
    /// Where the bookshelf's contacts SOUND FROM: the centre of the fallen carcass's footprint, on
    /// the flagstones. See the block comment above for what was there before and why it was two
    /// separate errors.
    /// </summary>
    /// <param name="fallback">Used only if the shelf node cannot be resolved at all — the apparition
    /// position the ordinary cue path produced. It is a bad answer and the log says so; it is kept
    /// because it is never worse than silence.</param>
    /// <param name="via">What actually resolved, for the scheduling log line. The point of naming it
    /// is that the day the bake renames the node this degrades VISIBLY instead of drifting back into
    /// the middle of the room.</param>
    private static Vector3 ShelfFloorContact(Vector3 fallback, out string via)
    {
        Transform? shelf = _shelfNode;
        if (shelf == null)
        {
            via = "NO 'Shelf' NODE — falling back to the apparition position, which is NOT the shelf";
            return fallback;
        }

        // TransformVector and not `shelf.forward * d`: the vector goes through the whole parent
        // chain, so the authored metres above become world units at whatever scale the room frame was
        // placed at, with no constant here to fall out of step with SkyAlternative's placement. The
        // room's scale is uniform, so this is a rotate-and-scale and nothing is skewed.
        Vector3 along = shelf.TransformVector(
            new Vector3(0f, 0f, ShelfHalfDepthMeters + 0.5f * ShelfHeightMeters));

        // The FLOOR is the node's own y and nothing else: the bake stands the prop on the flagstones
        // (`Rest(go, null, 0.015f, pos)`, "on ground sink=0.015 dy=-0.013"), so the transform sits on
        // the floor by construction. No bounds are read here — see the block comment.
        via = shelf.name;
        return new Vector3(shelf.position.x + along.x, shelf.position.y, shelf.position.z + along.z);
    }

    /// <summary>Where the CARCASS is while it is still standing and creaking — the same node, at
    /// half its own height. The lead cue is not an impact and must not come off the floor: a bookcase
    /// committing to a lean creaks along its whole body.</summary>
    private static Vector3 ShelfCarcass(Vector3 fallback)
    {
        Transform? shelf = _shelfNode;
        if (shelf == null)
            return fallback;
        Vector3 up = shelf.TransformVector(new Vector3(0f, 0.5f * ShelfHeightMeters, 0f));
        return shelf.position + up;
    }

    /// <summary>
    /// Play a one-shot from the pool at a world position. Round-robin, so a fourth simultaneous
    /// one-shot silently steals the oldest voice instead of adding to the pile — the concurrency cap
    /// from the class doc, enforced by construction rather than by a counter.
    /// </summary>
    /// <param name="cap">The ceiling <paramref name="gain"/> is clamped against. Defaults to
    /// <see cref="MaxEmitterGain"/>, which is what every caller but one passes; the bookshelf's
    /// contacts pass <see cref="ShelfImpactCeiling"/> under the written permission recorded there.
    /// A per-call ceiling rather than an unclamped path, so the exception is still a number compared
    /// against a stated maximum.</param>
    /// <param name="duckFloor">How far this cue is allowed to duck, 0..1 — 0 (the default) means
    /// "the whole way", which is what every caller but the bookshelf's two impacts passes. See
    /// <see cref="ShelfImpactDuckFloor"/>.</param>
    /// <returns>The volume actually written to the source on Unity's 0..1 scale — gain, clamped,
    /// times the master — or 0 if nothing played. RETURNED rather than recomputed by the caller
    /// because a log line that states a level nobody set is worse than no log line: this is the
    /// number the headset was handed.</returns>
    private static float PlayShot(AudioClip? clip, Vector3 world, float gain,
                                  float minMeters, float maxMeters, float pitch,
                                  float cap = MaxEmitterGain, float duckFloor = 0f)
    {
        if (clip == null || Shots.Count == 0)
            return 0f;

        Voice v = Shots[_nextShot];
        _nextShot = (_nextShot + 1) % Shots.Count;

        float volume = Mathf.Min(gain, cap) * MasterWith(Mathf.Max(_duck, Mathf.Clamp01(duckFloor)));
        v.Go.transform.position = world;
        v.Source.clip = clip;
        v.Source.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
        v.Source.minDistance = minMeters * _builtScale;
        v.Source.maxDistance = maxMeters * _builtScale;
        v.Source.volume = volume;
        v.Source.Play();
        return volume;
    }

    /// <summary>A stable 0..1 from a long, for the small per-event variations (a drip is never
    /// twice the same pitch). Deterministic, so two clients vary identically.</summary>
    private static float Hash01(long n) => Haunt.Hash(n, 2f);

    // ---- the listener ----------------------------------------------------------------------------

    /// <summary>
    /// Put the ear on the head. See the class doc for the whole argument; this is the mechanics.
    /// </summary>
    private static void TakeListener()
    {
        Core.HeadEar.Claim(EarClaim);
    }

    private static void ReleaseListener()
    {
        Core.HeadEar.Release(EarClaim);
    }

    // ---- teardown ---------------------------------------------------------------------------------

    /// <summary>
    /// Drop everything: sources, filters, the GameObjects that carry them, and the listener
    /// ownership. Idempotent — the guard is what makes the off path free and what stops a per-frame
    /// log line.
    ///
    /// <para>Called from BOTH <c>SkyAlternative.StandDown()</c> and
    /// <c>SkyAlternative.RestoreAll()</c>. Unlike <c>ElementMood</c>, which keeps publishing under
    /// mixed reality because a number is not geometry, this feature IS attached to geometry: with
    /// the room gone there is nothing left for a sound to come from, and an ambience playing over
    /// passthrough with no visible source is precisely the disembodied bed the user ruled out.</para>
    /// </summary>
    /// <param name="why">Named in the one log line this emits, so a reader can tell "the player
    /// turned it off" from "the scenario ended" without guessing.</param>
    internal static void StandDown(string why)
    {
        if (!_built && _root == null && !Core.HeadEar.Holds(EarClaim))
            return;

        int beds = Beds.Count;
        Teardown(keepListener: false);

        VRLog.Info("Core", $"ENV SOUND off — {why}. {beds} continuous source(s) and the one-shot pool " +
                           "destroyed, the AudioListener handed back to whatever had it, and every " +
                           "GameObject this feature created removed. Nothing is left standing; the " +
                           "synthesized clips stay in memory for the session because a clip nobody " +
                           "plays is inert and re-synthesizing them on every mixed-reality toggle " +
                           "would be pure waste.");
    }

    private static void Teardown(bool keepListener)
    {
        if (!keepListener)
            ReleaseListener();

        Beds.Clear();
        Shots.Clear();
        _nextShot = 0;

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }

        _dripNode = null;
        _ratNode = null;
        _perchFrame = null;
        _windowMouth = null;
        _perchOutwardOnly = false;
        _shelfNode = null;

        _built = false;
        _builtStyle = SkyStyle.Default;
        _builtScale = 1f;
        _duck = 1f;
        _gameAudible = false;
        _duckPollCountdown = 0;
        ArmSchedules(null);
        // ...and the continuity watch with them: the next build's first frame is a fresh
        // observation, not a jump, so it must not log one.
        _lastClockSeen = float.NaN;
        _lastRealSeen = 0f;
        // The wind gate goes back to SHUT rather than to its live value: the next environment must
        // fade its wind in from nothing exactly as the first one did, or a stand-down and rebuild
        // during an Air infusion would start the new room's bed at full level on its first frame.
        _windGate = 0f;
        // ...and the fire's, for the identical reason: a style change during a Fire infusion must not
        // put the new room's fires up at full level on the frame they are created.
        _fireGate = 0f;
        _candleGroups = 0;
        // The leak warning is per BUILD and not per session: a style change builds a different set
        // of beds, and a fault in the new room's table has to be able to report itself even if the
        // old room already reported one.
        _windLeakLogged = false;
        for (int i = 0; i < FireSites; i++)
            _fireVoices[i] = null;
        _fireResolution = string.Empty;
    }

    /// <summary>Release the synthesized clips as well. Only on a FULL teardown (VR stopped, the rig
    /// was destroyed) — see <see cref="EnvSoundBank.Release"/> for why an ordinary stand-down keeps
    /// them.</summary>
    internal static void ReleaseAll(string why)
    {
        StandDown(why);
        EnvSoundBank.Release();
    }

    // ---- the deferred cue queue --------------------------------------------------------------------

    /// <summary>
    /// One cue whose time is known but has not arrived: the bookshelf's arrival on the floor, its
    /// rebound's second contact, and its righting. See <see cref="ScheduleShelfContacts"/>.
    ///
    /// <para>A STRUCT IN A FIXED ARRAY, and both halves of that are deliberate. Fixed, because a
    /// growable queue is a way for a scheduling bug to allocate without bound inside a per-frame
    /// path; four is one more than the longest schedule anything here produces. A struct, because
    /// the whole array is then one allocation made once and never touched by the collector.</para>
    /// </summary>
    private struct Deferred
    {
        internal bool Live;
        internal float At;             // shared-clock seconds
        internal EnvSoundClip Clip;
        internal float Gain;
        internal float Cap;            // the ceiling Gain is clamped against — see PlayShot
        internal float DuckFloor;      // how far this cue may duck, 0 = all the way — see PlayShot
        internal float Pitch;
        internal float MinMeters;
        internal float MaxMeters;
        internal Vector3 Pos;          // WORLD, captured at schedule time

        /// <summary>What to call this cue in the log, or null for "do not log it".
        ///
        /// <para>IT EXISTS BECAUSE THE ONE THING NOBODY COULD SEE WAS WHETHER THIS QUEUE FIRED. The
        /// user reported the bookshelf's impact missing twice. The scheduling line proved the times
        /// were computed (Player.log:21575 prints all four), and after that the trail stopped: a cue
        /// dropped by the staleness guard, a cue dropped by the backwards-clock guard and a cue
        /// played at an inaudible level all looked exactly alike from a log — which is three
        /// different bugs sharing one symptom. A labelled cue now says which of them happened, with
        /// the level it was played at and the clip's own measured peak beside it, so the next
        /// hardware round is settled from Player.log rather than from another round of guessing.
        /// One line per shelf event, i.e. at most one every ~83 s and only for one card in six.</para></summary>
        internal string? Label;
    }

    private const int DeferredCues = 4;
    private static readonly Deferred[] _deferred = new Deferred[DeferredCues];

    /// <summary>How late a deferred cue may still fire. A frame or two of stall is normal; two
    /// seconds means the clock moved under us (a new owner was elected, a scene reloaded) and the
    /// picture the cue belonged to is not where the cue thinks it is. Dropping is then the correct
    /// answer, exactly as it is for the rat's squeak.</summary>
    private const float DeferredStaleSeconds = 2f;

    /// <summary>
    /// Put a cue on the queue. Takes the first free slot; if the queue is somehow full, it takes
    /// the one that fires SOONEST — an overflow means the schedule has more in it than this
    /// feature is allowed to make, and dropping the most imminent sound is the least intrusive
    /// possible failure, which is the standing rule applied to its own error path.
    /// </summary>
    private static void Defer(float at, EnvSoundClip clip, Vector3 pos, float gain,
                              float minMeters, float maxMeters, float pitch,
                              float cap = MaxEmitterGain, string? label = null,
                              float duckFloor = 0f)
    {
        int slot = -1;
        float soonest = float.MaxValue;
        for (int i = 0; i < _deferred.Length; i++)
        {
            if (!_deferred[i].Live)
            {
                slot = i;
                break;
            }
            if (_deferred[i].At < soonest)
            {
                soonest = _deferred[i].At;
                slot = i;
            }
        }

        _deferred[slot] = new Deferred
        {
            Live = true,
            At = at,
            Clip = clip,
            Gain = gain,
            Cap = cap,
            DuckFloor = duckFloor,
            Pitch = pitch,
            MinMeters = minMeters,
            MaxMeters = maxMeters,
            Pos = pos,
            Label = label,
        };
    }

    /// <summary>
    /// Fire whatever has come due. A <c>for</c> over a fixed array, once per frame — no queue to
    /// walk, nothing to sort, and no way for a mis-scheduled cue to keep the loop alive.
    ///
    /// <para>Two clock guards, and they are the same two the rat's squeak has: a cue that is more
    /// than <see cref="DeferredStaleSeconds"/> late is DROPPED rather than fired (the clock jumped
    /// forward and the picture has moved on), and a cue whose time is now absurdly far in the FUTURE
    /// is dropped too (the clock jumped backwards, so the schedule it was written against no longer
    /// exists). Without the second one a backwards jump would leave a bookshelf's thud sitting in
    /// the queue for as long as the clock took to catch up — and then fire it into whatever was
    /// happening by then.</para>
    /// </summary>
    private static void TickDeferredCues(float clock)
    {
        for (int i = 0; i < _deferred.Length; i++)
        {
            if (!_deferred[i].Live)
                continue;

            if (clock < _deferred[i].At - 90f)
            {
                if (_deferred[i].Label != null)
                    VRLog.Warn("Core", $"ENV SOUND {_deferred[i].Label} DROPPED — it was scheduled " +
                                       $"for shared clock {_deferred[i].At:F2}s and the clock now " +
                                       $"reads {clock:F2}s, i.e. more than 90s in the PAST. The " +
                                       "environment clock jumped backwards (a new owner was elected, " +
                                       "or the scenario reloaded), so the picture this cue belonged " +
                                       "to no longer exists. Nothing is broken; the next event " +
                                       "schedules normally.");
                _deferred[i].Live = false;
                continue;
            }
            if (clock < _deferred[i].At)
                continue;

            Deferred cue = _deferred[i];
            _deferred[i].Live = false;
            if (clock > cue.At + DeferredStaleSeconds)
            {
                if (cue.Label != null)
                    VRLog.Warn("Core", $"ENV SOUND {cue.Label} DROPPED as STALE — due at shared clock " +
                                       $"{cue.At:F2}s, first seen at {clock:F2}s, i.e. " +
                                       $"{clock - cue.At:F2}s late against a {DeferredStaleSeconds:F1}s " +
                                       "budget. The frame loop or the shared clock stalled between " +
                                       "scheduling and firing, so the sound would have landed on the " +
                                       "wrong picture. IF THIS LINE IS WHY A CONTACT IS MISSING, the " +
                                       "fault is the stall, not the cue.");
                continue;
            }

            AudioClip? clip = EnvSoundBank.Bank(cue.Clip);
            float volume = PlayShot(clip, cue.Pos, cue.Gain,
                                    cue.MinMeters, cue.MaxMeters, cue.Pitch, cue.Cap, cue.DuckFloor);
            if (cue.Label == null)
                continue;

            // THE LINE THAT SETTLES IT FROM A LOG. Everything a listener's "I heard nothing" has to
            // be checked against: that it fired at all, what the source was actually set to, and
            // what the clip itself contains — measured off the finished buffer on the DEVICE, at the
            // device's own sample rate, not quoted from a doc comment (EnvSoundBank.MeasuredShape).
            EnvSoundBank.MeasuredShape(clip, out float peak, out float peakAt);
            float effDuck = Mathf.Max(_duck, Mathf.Clamp01(cue.DuckFloor));
            VRLog.Info("Core", $"ENV SOUND {cue.Label} FIRED — {cue.Clip} at shared clock " +
                               $"{clock:F2}s (due {cue.At:F2}s, {(clock - cue.At) * 1000f:F0} ms late). " +
                               $"GAIN {cue.Gain:F3} before master, ceiling {cue.Cap:F2}, master " +
                               $"{MasterWith(effDuck):F3} (duck {effDuck:F2} — live duck {_duck:F2} " +
                               $"floored at {cue.DuckFloor:F2} for this cue, dial {Gain.Value:F2}, " +
                               $"game volume " +
                               $"{GameVolume():F2}) => SOURCE VOLUME {volume:F3} on Unity's 0..1 " +
                               $"scale, where a game cue at full level is 1.0. CLIP PEAK {peak:F3} " +
                               $"reached {peakAt * 1000f:F2} ms in — a peak at the start is an " +
                               "impact, a peak in the middle is a run-up. Rolloff " +
                               $"{cue.MinMeters:F1}..{cue.MaxMeters:F1} perceived m " +
                               $"(x{_builtScale:F2} = {cue.MinMeters * _builtScale:F0}.." +
                               $"{cue.MaxMeters * _builtScale:F0} world) from {cue.Pos:F2}. " +
                               (volume <= 0f
                                    ? "VOLUME IS ZERO — the clip is missing or the pool is empty."
                                    : "If the player still heard nothing with this line present, the " +
                                      "level and the clip are both accounted for and the remaining " +
                                      "suspects are the listener and the distance."));
        }
    }

    private static void ClearDeferred()
    {
        for (int i = 0; i < _deferred.Length; i++)
            _deferred[i].Live = false;
    }

    // ---- the deferred squeak -----------------------------------------------------------------------

    /// <summary>Fired from <see cref="Tick"/>'s event pass. SEPARATE from the queue above, and it
    /// stays separate: this cue follows a TRANSFORM (the rat is a moving node and the squeak must
    /// come from where the animal is when it squeaks, not from where it was when the schedule was
    /// written), while every shelf cue is pinned to a world point captured at schedule time because
    /// the thing making it is a 60 kg carcass that is not going anywhere.</summary>
    private static void TickDeferred(float clock)
    {
        if (float.IsNaN(_squeakAt) || clock < _squeakAt)
            return;
        // A clock that jumped backwards past the schedule point means the schedule is meaningless;
        // drop it rather than firing a squeak from a rat that is no longer running.
        bool stale = clock > _squeakAt + 4f;
        Transform? from = _squeakFrom;
        _squeakAt = float.NaN;
        _squeakFrom = null;
        if (stale || from == null)
            return;
        PlayShot(EnvSoundBank.Bank(EnvSoundClip.Squeak), from.position, 0.055f, 0.4f, 6f,
                 pitch: 0.92f + 0.2f * Hash01((long)(clock * 3f)));
    }

    // ---- node lookup -------------------------------------------------------------------------------

    /// <summary>
    /// Depth-first search for the first descendant with one of these exact names. The environment's
    /// interesting nodes (Drip, Puddle, Rat, Candles*, WindowGlow, Canopy, Ground, Wisp) are all
    /// children of <c>RoomGeo</c> and therefore invisible to <c>SkyAlternative</c>'s own top-level
    /// splitter, which only ever compares the shell's direct children.
    ///
    /// <para>Several names per call, tried in order, because the content lane may rename or merge a
    /// node and a fallback that lands one object away is enormously better than a sound that
    /// silently stops existing. Which one resolved is logged.</para>
    /// </summary>
    private static Transform? Find(Transform root, params string[] names)
    {
        for (int n = 0; n < names.Length; n++)
        {
            Transform? hit = Depth(root, names[n]);
            if (hit != null)
                return hit;
        }
        return null;

        static Transform? Depth(Transform t, string name)
        {
            if (string.Equals(t.name, name, System.StringComparison.Ordinal))
                return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform? hit = Depth(t.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }
    }

    /// <summary>Every descendant whose name STARTS with a prefix — the candle groups are
    /// "Candles0", "Candles1", ... and the rebuilt apparitions are expected to follow the same
    /// shape.</summary>
    private static List<Transform> FindByPrefix(Transform root, string prefix)
    {
        var found = new List<Transform>();
        Walk(root, prefix, found);
        return found;

        static void Walk(Transform t, string prefix, List<Transform> into)
        {
            if (t.name.StartsWith(prefix, System.StringComparison.Ordinal))
                into.Add(t);
            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), prefix, into);
        }
    }

    // ---- diagnostics ---------------------------------------------------------------------------------

    /// <summary>
    /// THE line. One per build — never per frame — and it carries everything a listener's report has
    /// to be matched against: which sources exist, WHICH NODE each one actually landed on (so a
    /// fallback is visible rather than silent), the level each will reach, the scale correction, and
    /// the state of the listener takeover. Nobody working on this can hear it; this log is the
    /// substitute for ears.
    /// </summary>
    private static void LogBuilt(SkyStyle style, float rigScale)
    {
        var sb = new StringBuilder(640);
        sb.Append("ENV SOUND up — ").Append(style).Append(", ").Append(Beds.Count)
          .Append(" continuous source(s) + ").Append(Shots.Count).Append(" one-shot voice(s). ");

        for (int i = 0; i < Beds.Count; i++)
        {
            Voice v = Beds[i];
            // CLIP, AIR, FIRE — on every bed, from ModBuild 153, because the defect the user
            // reported twice was exactly the combination of those three on one emitter and this
            // line named none of them. "Flame0 on 'CandlesTable' gain 0.055" was in the log of the
            // build that shipped the fault and told nobody anything.
            sb.Append('[').Append(v.Name).Append(" on '").Append(v.NodeName).Append("' clip=")
              .Append(v.Clip).Append(v.AirLed ? " AIR-LED" : " not-air-led")
              .Append(v.FireLit ? " FIRE-LIT" : " not-fire-lit").Append(" gain ")
              .Append(v.BaseGain.ToString("F3")).Append(", ")
              .Append(v.MinMeters.ToString("F1")).Append("..").Append(v.MaxMeters.ToString("F1"))
              .Append(" m perceived = ")
              .Append((v.MinMeters * rigScale).ToString("F0")).Append("..")
              .Append((v.MaxMeters * rigScale).ToString("F0")).Append(" world] ");
        }

        sb.Append("SCALE: rigScale ").Append(rigScale.ToString("F2"))
          .Append(" world units per perceived metre; every rolloff above is metres x that factor, ")
          .Append("recomputed whenever zoom moves it. LISTENER: ")
          .Append(Core.HeadEar.Holds(EarClaim)
                      ? "moved onto GloomhavenVR.HeadCamera (shared claim, see Core/HeadEar.cs)"
                      : "NOT taken — no head camera, so nothing here will be audible")
          .Append(", ").Append(Core.HeadEar.SuppressedCount)
          .Append(" pre-existing listener(s) disabled and remembered for restore. ")
          .Append("LEVELS: every source is capped at ").Append(MaxEmitterGain.ToString("F2"))
          .Append(", the dial can reach at most ").Append(MasterCeiling.ToString("F2"))
          .Append("x, the whole ambience ducks to ").Append(DuckFloor.ToString("F2"))
          .Append(" while the game is making any sound, and the player's own master and effects ")
          .Append("volumes multiply on top. WIND: the draught/leaves bed is LED BY THE AIR ")
          .Append("element above a RESTING FLOOR of ").Append(WindRestFloor.ToString("F2"))
          .Append(" of its own gain — it rises across ")
          .Append(AirGateOn.ToString("F2")).Append("..").Append(AirGateFull.ToString("F2"))
          .Append(" of Air over ").Append(AirGateOpenSeconds.ToString("F1"))
          .Append("s and falls back over ").Append(AirGateCloseSeconds.ToString("F1"))
          .Append("s. THE FLOOR IS NEW AT ModBuild 223 AND IT NARROWS A STANDING RULING: it was "
                  + "\"Wind Geräusch nur wenn auch Wind aktiv ist, sonst kein Geräusch\" "
                  + "(ModBuild 147) and the same user has now asked for \"ein ganz leiser dezenter "
                  + "Windzug\" and for the cellar's draught to come from the window, so a wind bed "
                  + "now plays for the whole scenario and is NEVER paused. A 'Draught' or 'Leaves' "
                  + "line above reading `paused` is therefore a FAULT, which is the opposite of what "
                  + "it used to mean. The Earth rumble is still paused whenever no Earth is up. ")
          .Append("EXACTLY THE BEDS MARKED clip=Bed ABOVE PLAY THE WIND BUFFER, and every one of ")
          .Append("them must also read AIR-LED. Until ModBuild 153 the three cellar 'Flame' beds ")
          .Append("played it NOT air-led and FIRE-LIT, which is the whole of the user's report ")
          .Append("(\"beim Feuer Geräusch ist auch immer das Wind Geräusch mit dabei\"); they now ")
          .Append("play clip=Flutter, a clip of their own with 1.1% of its energy under 200 Hz ")
          .Append("against the wind bed's 53.2% and a spectral spread of 0.66 octaves against 1.10. ")
          .Append("CANDLE GROUPS RESOLVED: ").Append(_candleGroups)
          .Append(_candleGroups == 0 && style == SkyStyle.Cellar
                      ? " — NONE, and in the cellar that is a FAULT, not a quiet room: the bake "
                        + "names them 'Candles<n>' (CandleGroup) and this build found no node with "
                        + "that prefix, so the candles are silent for the whole session. "
                      : ". ")
          .Append("FIRE: ").Append(_fireResolution.Length == 0 ? "no sites " : _fireResolution)
          .Append("— each site is ONE source carrying BOTH the roar (looped) and its crackles ")
          .Append("(AudioSource.PlayOneShot, so they inherit its gate, place, rolloff and every gain ")
          .Append("in the chain). GATED ON THE FIRE element across ")
          .Append(FireGateOn.ToString("F2")).Append("..").Append(FireGateFull.ToString("F2"))
          .Append(" over ").Append(FireGateOpenSeconds.ToString("F1")).Append("s up and ")
          .Append(FireGateCloseSeconds.ToString("F1"))
          .Append("s down, and while it is shut the sources are PAUSED — a 'Fire...' entry above ")
          .Append("with no fire sound under it is the gate working. A 'NO NODE' entry is NOT: it ")
          .Append("means the bake renamed that fire and this table has to follow it. The crackle is ")
          .Append("on a BERNOULLI TICK of ").Append(FireCrackleTickSeconds.ToString("F2"))
          .Append("s of shared clock (EnvSoundSchedule.TickFires — since ModBuild 226; it was a ")
          .Append("Poisson walk before, which is why two players used to crackle out of step), ")
          .Append("giving a mean gap of ")
          .Append(FireGapCalm.ToString("F1")).Append("s just-caught down to ")
          .Append(FireGapFull.ToString("F1")).Append("s fully alight, one event in ")
          .Append((1f / Mathf.Max(FireEmberShare, 1e-3f)).ToString("F0"))
          .Append(" being an ember settling instead, and its rolloff is ")
          .Append(FireMinMeters.ToString("F1")).Append("..").Append(FireMaxMeters.ToString("F1"))
          .Append(" perceived m — sized to the ROOM (the head measured 2.2-7.6 m from these seats) ")
          .Append("and not to the 0.6 m the candle beds use, which costs them 15-22 dB at that ")
          .Append("distance. CLOCK: EVERY scheduled event in this file is now a PURE FUNCTION of ")
          .Append("SkyAlternative.EnvClockSeconds — the drip, the rat and its squeak, the two night ")
          .Append("calls AND THE PERCH THEY COME FROM, the apparitions, the bookshelf's contacts ")
          .Append("and, since ModBuild 226, the fire's crackle. So two players in the same room ")
          .Append("hear the same sound from the same place on the same frame with ZERO wire bytes, ")
          .Append("which is the user's ruling (\"Genau wie die Easter-Eggs sollen auch die Sounds ")
          .Append("mit allen Mitspieler synchronisiert sein ... zur selben Zeit aus der selben ")
          .Append("Location denselben Sound\"). THERE IS NO EXCEPTION LEFT: the crackle was the last ")
          .Append("one and it is a Bernoulli tick now. What is deliberately NOT shared is the BEDS' ")
          .Append("shaping LFOs and the duck, which are levels rather than instants and have no ")
          .Append("moment for two clients to disagree about.");

        // WHAT THIS ROOM SOUNDS LIKE WITH NOTHING INFUSED AND NOTHING HAPPENING, SAID AT BUILD.
        // "The room is silent" and "the room is a television" are both reports this feature has now
        // received, and neither could be answered from a log: LogBuilt named the beds and their
        // gains and then nothing ever said which of them was audible AT REST. This clause is the
        // whole at-rest inventory, so the next report can name an emitter.
        sb.Append(" AT REST (ModBuild 223 — THE TWO CONTINUOUS ROOM TONES ARE DELETED, see the ")
          .Append("THE ROOM TONES, DELETED block in Core/EnvSound.cs for the ruling and the ")
          .Append("measurements): ");
        if (style == SkyStyle.Cellar)
        {
            sb.Append("the cellar plays the WINDOW DRAUGHT at ")
              .Append(WindRestFloor.ToString("F2")).Append(" of its own gain (user: \"der Windzug ")
              .Append("im Keller könnte vom Fenster ausgehen (aber auch hier nur dezent!)\") plus ")
              .Append(_candleGroups).Append(" candle flame bed(s). Measured at 4 perceived metres ")
              .Append("through a 500 Hz high pass the draught delivers 0.00049 against a single ")
              .Append("candle's 0.00084 and against the DELETED 'Stone' bed's 0.00499 — 20.1 dB ")
              .Append("under the thing he called \"Rauschen bei nem Fernseher\". Its rolloff ")
              .Append("minimum is 1.2 m, so it is roughly twice a candle bed AT the window and "
                      + "almost gone at the table: that is what makes it a draught and not a room ")
              .Append("tone. Everything else in this room is an EVENT (the drip every 2.85 s, the ")
              .Append("rat every 26 s, the apparitions every 83 s) or an element response.");

            // ---- AND THE WOOD OUTSIDE, ModBuild 296 ----------------------------------------------
            sb.Append("\nTHE WOOD THROUGH THE WINDOW (user: \"Die Tiergeraeusche kannst du von ")
              .Append("diesem Wald aus auch triggern, in der selben Intensitaet wie im Wald, d.h. ")
              .Append("im Keller hoert man sie weniger und immer von dem Fenster aus lokalisiert\"): ")
              .Append(_perchFrame != null && _windowMouth != null
                          ? "ON. The SCHEDULE IS THE WOOD'S, unchanged and unduplicated — the same "
                            + NightCallSlot.ToString("F0") + " s slot, the same "
                            + NightCallDeck.Length + "-card deck at salt 0x"
                            + NightCallDeckSalt.ToString("X8") + ", the same "
                            + (NightCallSkip * 100f).ToString("F0")
                            + "% of slots skipped and the same authored gain per animal, so this "
                            + "room is exactly as talkative as the wood is and no louder at source. "
                            + "THE PERCH is drawn on the same per-animal rings from the same three "
                            + "hash channels off the same slot index, in '" + _perchFrame.name
                            + "'s frame (the wall's outer face at outside ground level, +Z outward), "
                            + "over the OUTWARD half-plane only. IT IS NOT WHERE THE SOUND COMES "
                            + "FROM: every call is played from '" + _windowMouth.name + "', the "
                            + "opening's own mouth, so it is localised at the window at every head "
                            + "orientation — which is what the user asked for and what an aperture "
                            + "in a heavy wall really is. WHAT MAKES IT QUIETER is two legs of a "
                            + "path and no volume knob: the voice's OWN rolloff over the open air "
                            + "from the animal to the wall, times the aperture's insertion loss "
                            + (20f * Mathf.Log10(WindowInsertion)).ToString("F1")
                            + " dB (x" + WindowInsertion.ToString("F3") + " = sqrt(S/A), S = "
                            + CellarWindowOpeningM2.ToString("F3") + " m2 of outer opening against "
                            + CellarAbsorptionM2.ToString("F1") + " m2 sabins of room absorption). "
                            + "An owl's 0.050 therefore lands near 0.009-0.011 here against 0.041 "
                            + "in the wood: about 12 dB down, and comparable with this room's own "
                            + "drip. NO REVERB AND NO LOW PASS ARE MODELLED — see WindowInsertion "
                            + "for why that is a decision and not an omission."
                          : "OFF. The bake gave this room no 'WindowWood' marker or no 'WindowGlow' "
                            + "node, so there is nothing to measure the wood from and nothing to "
                            + "hear it through; the warning above says which. This room sounds "
                            + "exactly as it did before ModBuild 296.")
              .Append(" PER-FRAME COST: one integer slot division and one long compare, on the "
                      + "path this room already ran for the drip and the rat. No allocation, no "
                      + "scene query, no new voice — the calls take the shot pool this room already "
                      + "builds, so the emitter count is unchanged.");
        }
        else
        {
            sb.Append("the wood plays the CANOPY DRAUGHT at ").Append(WindRestFloor.ToString("F2"))
              .Append(" of its own gain (user: \"im Wald ein ganz leiser dezenter Windzug\") and ")
              .Append("NOTHING ELSE continuously. Delivered at 4 perceived metres through a 500 Hz ")
              .Append("high pass that is 0.00076 against the DELETED 'NightAir' bed's 0.00250. THE ")
              .Append("INSECT CHORUS IS DELETED TOO, at ModBuild 226, and it is what the user called ")
              .Append("rain (\"Im Wald gefällt mir nur dieser 'Regen' Sound nicht der ab und zu ")
              .Append("kommt und für eine Zeit bleibt, ansonsten finde ich es sehr gut\"): it put ")
              .Append("81.0% of its energy above 2 kHz against a rain control's 94.5% and the next ")
              .Append("continuous emitter in this room's 3.7%, it arrived every ~62 s and held ")
              .Append("~22 s, and removing it takes the room's spectral flatness from 0.610 to ")
              .Append("0.134 and its delivered level from 0.00203 to 0.00076 (-8.5 dB). A BED NAMED ")
              .Append("'Night' ON A GATE LINE BELOW MEANS A MERGE WENT WRONG — there is no such bed ")
              .Append("any more, and EnvSoundClip.Chirr no longer exists. The wood at rest is the ")
              .Append("canopy draught and nothing else; everything else here is an EVENT (the night ")
              .Append("calls, the apparitions) or an element response (the fires, the rumble).");
        }

        if (style == SkyStyle.SwampNight)
        {
            sb.Append(" NIGHT CALLS, AND THEY MOVE (user, 2026-08-22: \"Im Wald mal ne Eule oder ")
              .Append("ähnliches die ruft (auch aus dem Wald hörbar, hier sollte die Position auch ")
              .Append("random wechseln)\"): an Owl at gain ").Append(OwlGain.ToString("F3"))
              .Append(", rolloff ").Append(OwlMinMeters.ToString("F0")).Append("..")
              .Append(OwlMaxMeters.ToString("F0")).Append(" perceived m, on a perch ring ")
              .Append(OwlPerchNearMeters.ToString("F0")).Append("..")
              .Append(OwlPerchFarMeters.ToString("F0"))
              .Append(" authored m from the clearing's centre; and a NightBird at gain ")
              .Append(BirdGain.ToString("F3")).Append(", rolloff ")
              .Append(BirdMinMeters.ToString("F0")).Append("..").Append(BirdMaxMeters.ToString("F0"))
              .Append(" m, on a ring ").Append(BirdPerchNearMeters.ToString("F0")).Append("..")
              .Append(BirdPerchFarMeters.ToString("F0")).Append(" m out. AND FIVE MORE ANIMALS ")
              .Append("SINCE ModBuild 241 (user: \"Füge noch mehr verschiedene Tiersounds hinzu ")
              .Append("die zu einem Wald in der Nacht passen für mehr Varianz (nicht mehr ")
              .Append("Häufigkeit)\"): a KeWick, a Raven, a Fox, a RoeDeer and an OwletBeg, at gains ")
              .Append(KeWickGain.ToString("F3")).Append("/").Append(RavenGain.ToString("F3"))
              .Append("/").Append(FoxGain.ToString("F3")).Append("/")
              .Append(RoeDeerGain.ToString("F3")).Append("/").Append(OwletGain.ToString("F3"))
              .Append(" — every one BELOW the owl's ").Append(OwlGain.ToString("F3"))
              .Append(", so nothing in this room got louder. AND THREE EERIE ONES SINCE ModBuild 242 ")
              .Append("(user: \"ein paar gruseligere Tiersounds ... wie man es aus der Pop-Kultur ")
              .Append("kennt. Aber auch nicht aufdringlich. Gerne eventuell auch Insekten Sounds\"): ")
              .Append("a Howl (wolf, far), a BarnOwl (screech) and a Stridulate (one insect), at ")
              .Append("gains ").Append(HowlGain.ToString("F3")).Append("/")
              .Append(BarnOwlGain.ToString("F3")).Append("/").Append(StridGain.ToString("F3"))
              .Append(" — the three QUIETEST cards in the deck, all below the owlet's ")
              .Append(OwletGain.ToString("F3"))
              .Append(", and with three of the four slowest onsets in the deck (232/99/79 ms ")
              .Append("against the roe deer's 5.3): eerie by timbre and rarity, never by level or ")
              .Append("by a transient. THE RATE DID NOT MOVE AND THAT IS THE ")
              .Append("POINT: still one call per slot, still ")
              .Append((100f * NightCallSkip).ToString("F0")).Append("% of slots silent. The animal ")
              .Append("is DEALT from a ").Append(NightCallDeck.Length.ToString())
              .Append("-card deck (EnvSoundSchedule.DeckDraw, salt 0x")
              .Append(NightCallDeckSalt.ToString("X8"))
              .Append("), which repeats itself back-to-back 0.03% of the time against the 12.5% a ")
              .Append("weighted draw of the same shares would, and whose long-run shares are EXACT ")
              .Append("(4/3/3/2/2/2/1/1/1/1 of 20 — the fox, the roe deer, the wolf and the barn owl ")
              .Append("are one card each, about one per 18 minutes, and never more than 39 calls ")
              .Append("apart). It is integer-only and a ")
              .Append("pure function of the slot, so every client in this room deals the same card. ")
              .Append("A ROUND THAT WANTS MORE VARIETY MUST ADD CARDS, NEVER SLOTS. FRAME: ")
              .Append(_perchFrame != null ? "'" + _perchFrame.name + "'" : "NO NODE")
              .Append(_perchFrame == null
                          ? " — NEITHER CALL WILL SOUND AT ALL this session; see the warning above. "
                          : ", the bake's ground plane, so the ring is in AUTHORED metres and this ")
              .Append(_perchFrame == null ? "" :
                      "file never has to know the room's placement, art scale or yaw. ")
              .Append("EVERY CALL IS RE-PLACED: azimuth, radius (area-uniform in the annulus, as ")
              .Append("the bake seats the trees themselves) and height (a fraction of the canopy at ")
              .Append("that radius for the six that PERCH; authored metres off the ground plane ")
              .Append("for the fox, the roe deer, the wolf and the insect, which cannot climb) are ")
              .Append("three hash channels ")
              .Append("off the SLOT INDEX and nothing else, so ")
              .Append("both players hear the same animal from the same tree on the same frame with ")
              .Append("ZERO wire bytes. BOUNDED BY CONSTRUCTION: the inner radius is outside the ")
              .Append(ForestClearRadiusMeters.ToString("F1"))
              .Append(" m of open ground the board and the player stand on, so a call can never come ")
              .Append("from inside the player — and the rolloff is flat inside ")
              .Append(OwlMinMeters.ToString("F0"))
              .Append(" m anyway, so the nearest possible perch is no louder than the farthest; every ")
              .Append("PERCHED outer radius sits inside a canopy that has closed by 19 m and every ")
              .Append("outer radius at all (the wolf's 24 m is the largest) inside a trunk field that ")
              .Append("runs to 27 m, so a call can never come from outside the wood. RATE: one ")
              .Append("slot every ").Append(NightCallSlot.ToString("F0")).Append(" s, ")
              .Append((100f * NightCallSkip).ToString("F0"))
              .Append("% of them silent, the instant inside a slot an EXPONENTIAL waiting time ")
              .Append("(EnvSoundSchedule.PoissonGap, mean ").Append(NightCallMean.ToString("F1"))
              .Append(" s, bounded 4.3..40.3 s so it always lands inside the slot) — a realised mean ")
              .Append("gap of about ").Append((NightCallSlot / (1f - NightCallSkip)).ToString("F0"))
              .Append(" s, against the drip's 2.85 s and the apparitions' 83 s. The skip came down ")
              .Append("from 30% at ModBuild 223 because with the room tones deleted these calls ARE ")
              .Append("the wood.");
        }

        // THE BOOKCASE, SAID AT BUILD RATHER THAN AT THE FIRST EVENT. Its cues are the loudest thing
        // this feature makes and their position comes off ONE node; if that node is missing, the bang
        // goes back to the middle of the room and the next report is another round of "aus der
        // falschen Stelle". A shelf event happens at most every ~83 s and only for one card in six,
        // so waiting for one to find out is minutes of a test session. This is one clause, once.
        if (style == SkyStyle.Cellar)
        {
            sb.Append(" SHELF: ");
            if (_shelfNode == null)
            {
                sb.Append("NO 'Shelf' NODE UNDER THE ROOM — the bookcase's creak, its arrival on the ")
                  .Append("floor and its righting will all fall back to the apparition catalogue's ")
                  .Append("bounds centre, which is the MIDDLE OF THE ROOM and not the shelf. If the ")
                  .Append("bake renamed the node, rename it here.");
            }
            else
            {
                Vector3 contact = ShelfFloorContact(Vector3.zero, out _);
                sb.Append("standing at ").Append(_shelfNode.position.ToString("F2"))
                  .Append(", facing ").Append(_shelfNode.forward.ToString("F2"))
                  .Append(", so it will fall onto ").Append(contact.ToString("F2"))
                  .Append(" — that point, and not the apparition catalogue, is where the bang, the ")
                  .Append("rebound and the righting sound from.");
            }
        }

        VRLog.Info("Core", sb.ToString());
    }
}
