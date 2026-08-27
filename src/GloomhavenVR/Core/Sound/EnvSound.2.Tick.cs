using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// EnvSound part 2 of 5 — see EnvSound.1.Core.cs for the type's own doc
// and for why the parts are digit-prefixed.
internal static partial class EnvSound
{
    // ---- per-frame work ---------------------------------------------------------------------------

    /// <summary>
    /// The duck. Polls whether the GAME is making any sound and walks the ambience down to
    /// <see cref="DuckFloor"/> while it is. Throttled, because the game's query allocates.
    /// </summary>
    private static void TickDuck()
    {
        if (--_duckPollCountdown <= 0)
        {
            _duckPollCountdown = DuckPollFrames;
            try
            {
                // GetPlayingAudioObjects (AudioController.cs:880) is the only query the game offers
                // that answers "is anything at all playing"; GetPlayingAudioObjectsCount takes an
                // audioID and would need us to know what to ask about.
                // `var`, not the named type: AudioObject lives in the game's ClockStone namespace
                // and naming it here would mean importing that namespace into a file that wants
                // exactly one thing out of it. The static shape (a List with a Count) is all this
                // needs, and it is checked by the compiler either way.
                var playing = AudioController.GetPlayingAudioObjects();
                _gameAudible = playing != null && playing.Count > 0;
            }
            catch
            {
                // No controller yet (main menu, scene load), or a half-built item table. "Assume the
                // game is quiet" is the safe answer: it leaves the ambience at its own level, which
                // is already under everything.
                _gameAudible = false;
            }
        }

        float target = _gameAudible ? DuckFloor : 1f;
        float tau = _gameAudible ? DuckAttackSeconds : DuckReleaseSeconds;
        // Time.deltaTime, not the shared clock, and on purpose: the duck is a LOCAL smoothing of a
        // LOCAL observation (what this client's audio engine happens to be playing). Nothing about
        // it is correlated with a visual, so nothing about it needs to be shared.
        _duck = Mathf.MoveTowards(_duck, target, Time.deltaTime / Mathf.Max(tau, 0.01f));
    }

    private static void TickBeds()
    {
        bool windWasOpen = _windGate > 0f;
        bool fireWasOpen = _fireGate > 0f;

        TickWindGate();
        TickFireGate();

        float air = Mathf.Clamp01(ElementMood.Live(2));

        float master = Master();
        for (int i = 0; i < Beds.Count; i++)
        {
            Voice v = Beds[i];
            float m = v.Modulate != null ? Mathf.Clamp(v.Modulate(), 0f, 2f) : 1f;
            float want = v.BaseGain * m * master;

            // ================= THE ASSERTION THIS FEATURE HAS EARNED TWICE ============================
            //
            //  A WIND-CLIP BED LOUDER THAN THE RESTING DRAUGHT WHILE AIR IS DOWN IS THE SHIPPED
            //  DEFECT, and from ModBuild 153 it writes a line instead of just playing.
            //
            //  THE BOUND MOVED AT ModBuild 223 AND THE CHECK DID NOT GO WITH IT. It used to be
            //  `m > 0f`, because WindBed returned a literal zero with the gate shut. The user has
            //  narrowed that ruling — a very quiet draught is now wanted at rest in both rooms, see
            //  THE WIND GATE — so the invariant is now "at most WindRestFloor", which is still an
            //  exact number rather than a tolerance: WindBed returns the constant itself on that
            //  path, so the comparison is against a value this file wrote and not against an
            //  approximation. The epsilon is only there because the constant is a float literal and
            //  a future caller might scale it by 1.0f in some other order.
            //
            //  IT IS STILL ON THE MODULATOR AND NOT ON THE SOURCE VOLUME, deliberately: the volume
            //  walks over about a tenth of a second, so testing that would need a real tolerance,
            //  and a tolerance is a place for the next bug to live.
            //
            //  ONCE PER BUILD, not per frame: the flag is cleared by Teardown. A per-frame warning
            //  would be 90 lines a second in a log that has to stay readable.
            if (v.IsWindClip && !_windLeakLogged && air <= 0f && _windGate <= 0f
                && m > WindRestFloor + 1e-4f)
            {
                _windLeakLogged = true;
                VRLog.Warn("Core", $"ENV SOUND WIND LEAK — bed '{v.Name}' on '{v.NodeName}' plays " +
                                   $"the WIND buffer (EnvSoundClip.Bed) and its level is {m:F3} of " +
                                   "its own gain while the Air element reads 0.000 and the wind gate " +
                                   $"is fully shut. The most it may be at rest is {WindRestFloor:F3} " +
                                   "(EnvSound.WindRestFloor), which is the \"ganz leiser dezenter " +
                                   "Windzug\" the user asked for on 2026-08-22 — anything above it " +
                                   "is a WIND with no Air up, which he has ruled out twice. A bed " +
                                   "that plays this clip must take its level from WindBed(), which " +
                                   "returns exactly that constant with the gate shut. If this line " +
                                   "is in the log the fault is in whatever lambda that bed was " +
                                   "given, not in the gate. Logged once per build.");
            }
            // Walked, never jumped: a bed whose level stepped with the element ramp would click.
            v.Source.volume = Mathf.MoveTowards(v.Source.volume, want, Time.deltaTime * 0.6f);

            // A BED THAT HAS FADED TO NOTHING IS PAUSED, NOT LEFT SPINNING, and this is what makes
            // "sonst kein Geräusch" a property of the engine rather than of a multiply. Two things
            // buy it:
            //   * IT CANNOT BE HEARD. A paused source is not in the mix at all, so no future gain
            //     bug, no rounding, no filter tail and no spatialiser can put a wind back into a
            //     room the player switched the wind off in.
            //   * IT COSTS NOTHING WHILE IT IS OFF. A playing source is a spatialised voice plus an
            //     AudioLowPassFilter being evaluated every DSP block whether or not its volume is
            //     zero. The gated wind bed is off for almost the whole of a scenario (Air is up for
            //     seconds at a time), and the Earth rumble — which has NEVER had a resting level
            //     (see BuildCellar) — was spinning at volume 0 for the entire session in both rooms.
            //     This is the first frame either of them stops costing anything.
            // Pause/UnPause rather than Stop/Play: the playhead is kept, so a bed resumes where it
            // left off instead of restarting its buffer — which would also throw away the
            // decorrelating offset AddBed gives each bed at creation.
            if (want <= 0f && v.Source.volume <= 0f)
            {
                if (v.Source.isPlaying)
                    v.Source.Pause();
            }
            else if (!v.Source.isPlaying)
            {
                v.Source.UnPause();
            }
        }

        // ...and THE LINE THAT SETTLES THE REPORT FROM A LOG. See LogGates.
        //
        // ONE LINE EVEN WHEN BOTH GATES MOVE ON THE SAME FRAME, which a card that infuses two
        // elements at once really does produce. The label names both edges and the line reports both
        // gates and every bed regardless, so nothing is lost by not writing it twice — and "the wind
        // came up AND the fires caught" is the single most interesting frame in the whole feature.
        bool windEdge = windWasOpen != _windGate > 0f;
        bool fireEdge = fireWasOpen != _fireGate > 0f;
        if (windEdge || fireEdge)
        {
            string wind = windEdge ? (_windGate > 0f ? "THE WIND CAME UP" : "THE WIND WENT DOWN") : "";
            string fire = fireEdge ? (_fireGate > 0f ? "THE FIRES CAUGHT" : "THE FIRES WENT OUT") : "";
            LogGates(windEdge && fireEdge ? wind + " and " + fire : wind + fire, air);
        }
    }

    /// <summary>Set once per build, the first time a wind-clip bed is caught with a level while Air
    /// is down. Cleared by <see cref="Teardown"/>, so a session that stands two environments up gets
    /// at most one line per environment rather than one per frame.</summary>
    private static bool _windLeakLogged;

    /// <summary>
    /// EVERY BED, ITS CLIP, ITS GATES AND ITS LEVEL — written on the frame a gate opens or shuts and
    /// on no other frame.
    ///
    /// <para><b>THIS IS THE LINE THE LAST THREE ROUNDS DID NOT HAVE.</b> The user has now reported
    /// twice that a wind is audible when it should not be, and both times the log could not settle
    /// it: <see cref="LogBuilt"/> named the beds and their gains, and then nothing in the session
    /// ever said what any of them was DOING. A wind-clip bed playing at full level with Air at zero
    /// and a correctly-gated one at zero looked identical from Player.log, which is why the fix took
    /// three rounds to find and why it was found by reading BuildCellar rather than by reading a
    /// log.</para>
    ///
    /// <para>SO THE TRIGGER IS THE EVENT ITSELF. Air and Fire are infused a handful of times in a
    /// scenario, so this is a handful of lines — and each one is emitted exactly when the thing the
    /// report is about changes, with the source volumes as they stand on that frame. A reader with
    /// this line can answer "is the wind clip audible while Air is down" by looking, and can answer
    /// "what got louder when I infused Fire" by diffing two of them.</para>
    /// </summary>
    private static void LogGates(string what, float air)
    {
        float fire = Mathf.Clamp01(ElementMood.Live(0));
        var sb = new StringBuilder(560);
        sb.Append("ENV SOUND ").Append(what).Append(" — Air ").Append(air.ToString("F3"))
          .Append(" (wind gate ").Append(_windGate.ToString("F3")).Append("), Fire ")
          .Append(fire.ToString("F3")).Append(" (fire gate ").Append(_fireGate.ToString("F3"))
          .Append("), Earth ").Append(Mathf.Clamp01(ElementMood.Live(3)).ToString("F3"))
          .Append(", duck ").Append(_duck.ToString("F2")).Append(", master ")
          .Append(Master().ToString("F3")).Append(". EVERY BED, as it stands this frame: ");

        for (int i = 0; i < Beds.Count; i++)
        {
            Voice v = Beds[i];
            sb.Append('[').Append(v.Name).Append(" clip=").Append(v.Clip)
              .Append(v.AirLed ? " AIR-LED" : " not-air-led")
              .Append(v.FireLit ? " FIRE-LIT" : " not-fire-lit")
              .Append(" vol ").Append(v.Source.volume.ToString("F4"))
              .Append(v.Source.isPlaying ? " PLAYING" : " paused")
              .Append("] ");
        }

        // THE ROOM TONE, ON EVERY GATE LINE. It is the only emitter here that is supposed to be
        // audible on a frame where NOTHING is infused, so a reader comparing two of these lines has
        // to be able to see that it did not move — a room tone that rose with an element would be the
        // ModBuild 152 fault in a new place.
        // WHAT IS PLAYING AT REST, ON EVERY GATE LINE. There is no room tone any more (ModBuild 223
        // deleted both), so the question a reader has to be able to answer from two of these lines is
        // the NEW one: did the resting draught stay where it belongs across the edge? The floor
        // answers no element, so it must read the same on both sides of a Fire or Earth edge and it
        // must be exactly this number on the Air-down side of an Air edge.
        sb.Append("AT REST: the wind beds rest at ").Append(WindRestFloor.ToString("F2"))
          .Append(" of their own gain (EnvSound.WindRestFloor — user: \"ein ganz leiser dezenter "
                  + "Windzug\", \"der Windzug im Keller könnte vom Fenster ausgehen\") and are "
                  + "never paused. NOTHING ELSE IN EITHER ROOM PLAYS AT REST, and three deleted beds "
                  + "are named so this line can be read as a check rather than as a list: 'Stone' "
                  + "and 'NightAir' (the room tones, ModBuild 223) and 'Night' (the insect chorus, "
                  + "ModBuild 226 — the sound the user called \"Regen\"). A bed of any of those three "
                  + "names on this line means a merge went wrong. ");

        // The one sentence a reader should not have to assemble themselves.
        int windBeds = 0;
        float windLevel = 0f;
        for (int i = 0; i < Beds.Count; i++)
        {
            if (!Beds[i].IsWindClip)
                continue;
            windBeds++;
            windLevel += Beds[i].Source.volume;
        }
        sb.Append("THE WIND CLIP (EnvSoundClip.Bed) IS PLAYED BY ").Append(windBeds)
          .Append(" bed(s) in this room and their volumes sum to ").Append(windLevel.ToString("F4"))
          .Append(air <= 0f
                      ? " WITH AIR AT ZERO. THE CEILING FOR THAT SUM MOVED AT ModBuild 223 AND IT IS "
                        + "NO LONGER ZERO: the user asked for \"ein ganz leiser dezenter Windzug\" "
                        + "and for \"der Windzug im Keller könnte vom Fenster ausgehen\", so each "
                        + "wind bed rests at EnvSound.WindRestFloor of its own gain and neither is "
                        + "paused any more. Multiply each bed's gain above by that floor and by the "
                        + "master on this line and the sum must not exceed it; anything MORE is the "
                        + "ModBuild 152 defect back (the candle beds used to play this clip ungated, "
                        + "and their gain rose with FIRE, which is why 'beim Feuer Geräusch ist auch "
                        + "immer das Wind Geräusch mit dabei'), and TickBeds writes its own WIND LEAK "
                        + "line if the modulator itself is over the floor."
                      : " with Air up, which is the state in which it is allowed to be a WIND rather "
                        + "than a whisper.")
          .Append(" The candles play EnvSoundClip.Flutter and the seated fires EnvSoundClip.Roar; "
                  + "neither is this buffer, and neither answers Air.");
        VRLog.Info("Core", sb.ToString());
    }

    /// <summary>
    /// Walk <see cref="_windGate"/> toward whatever the AIR element is doing. See THE WIND GATE for
    /// the user report and for why this is a smoothed gate rather than a comparison.
    ///
    /// <para>Called from <see cref="TickBeds"/> and not from the modulators, because a lambda that
    /// integrated would integrate ONCE PER BED — two beds would walk the same field twice per frame
    /// and the gate would open at double rate in a room that happened to have both.</para>
    /// </summary>
    private static void TickWindGate()
    {
        float air = Mathf.Clamp01(ElementMood.Live(2));
        float target = Mathf.Clamp01((air - AirGateOn) / Mathf.Max(AirGateFull - AirGateOn, 1e-4f));
        target = target * target * (3f - 2f * target);   // smoothstep — no corner at either end

        float tau = target > _windGate ? AirGateOpenSeconds : AirGateCloseSeconds;
        _windGate = Mathf.MoveTowards(_windGate, target, Time.deltaTime / Mathf.Max(tau, 0.01f));
    }

    /// <summary>
    /// THE RESTING DRAUGHT — what the wind beds play with NO Air infusion up, which until ModBuild
    /// 223 was a literal zero.
    ///
    /// <para><b>USER RULING, 2026-08-22, verbatim, and it is the reason this constant exists:</b>
    /// "Statt generrell durchgehende sounds zu machen lieber die Tierrufe und im Wald ein ganz
    /// leiser dezenter Windzug. ... zB der Windzug im Keller könnte vom Fenster ausgehen (aber auch
    /// hier nur dezent!)". THE FULL READING IS IN THE WIND GATE's block above and it is written out
    /// there so it can be corrected in one sentence: a very quiet draught is now always present in
    /// both rooms, and the Air infusion is the RISE above it rather than the difference between
    /// silence and wind.</para>
    ///
    /// <para><b>0.22, AND IT IS CHOSEN AGAINST A CANDLE BED RATHER THAN BY FEEL.</b> Measured on the
    /// replica at 4 perceived metres through a 500 Hz high pass — the two corners
    /// <c>EnvSoundBank</c>'s delivered-level table is built on:</para>
    /// <code>
    ///   Draught at rest (this)     0.075 gain x 0.22, 1.2 m minimum   ->  0.00049
    ///   Leaves  at rest (this)     0.070 gain x 0.22, 2.0 m minimum   ->  0.00076
    ///   ONE candle bed                                                ->  0.00084
    ///   'Stone', the DELETED cellar room tone                         ->  0.00499
    /// </code>
    /// <para>So the cellar's resting draught is <b>20.1 dB under the bed this round deleted</b> and
    /// 4.7 dB under a single candle flame, and the wood's is 10.3 dB under its deleted bed. Its
    /// absolute 1-5 kHz energy is 6.39e-07 against a candle's 2.89e-06 — 13.1 dB of headroom in the
    /// speech band, so the "never mask" budget is not merely intact, it is barely touched.</para>
    ///
    /// <para><b>AND THE ROLLOFF IS WHAT MAKES "vom Fenster ausgehen" TRUE.</b> The draught's minimum
    /// is 1.2 perceived metres, so the numbers above (taken at 4 m, i.e. at the table) are already
    /// 10.5 dB down: standing AT the window it reaches 0.00167, about twice a candle bed, and it
    /// fades to almost nothing as you walk away. That is the difference between a room tone and a
    /// draught, and it is a property of the rolloff rather than of the level.</para>
    ///
    /// <para><b>IT IS FLAT.</b> No LFO, no gust, no swell — see the wind gate block. A resting bed
    /// that breathed would be a wind with no wind, which is what the 147 ruling is actually about
    /// and what <c>EnvSoundBank</c>'s reject list turned down for the room tones one round ago. The
    /// gusting begins when the element does.</para></summary>
    private const float WindRestFloor = 0.22f;

    /// <summary>
    /// The wind bed's level, 0..2, for both rooms. ONE function, so the cellar's draught and the
    /// swamp's canopy cannot drift into being two different winds — they are the same air.
    ///
    /// <para>NEVER RETURNS ZERO SINCE ModBuild 223: it returns <see cref="WindRestFloor"/> with the
    /// gate shut, so both wind beds now play for the whole scenario and neither is ever paused by
    /// <see cref="TickBeds"/>. That is the ruling in the block above, and it is the ONE place in this
    /// file where a level that used to be a literal zero no longer is — which is why the assertion in
    /// <see cref="TickBeds"/> was rewritten rather than deleted: it still holds a real invariant,
    /// namely that a wind-clip bed may not exceed the resting floor while Air is down.</para>
    ///
    /// <para><b>THE RISE IS SCALED SO THE SUM STILL TOPS OUT AT EXACTLY 2.</b> The gate term is
    /// multiplied by <c>(1 - rest/2)</c>, and the bracket's own maximum is
    /// <c>0.55 + 0.45 + 1.0 = 2</c>, so the total at full gate and full Air is
    /// <c>rest + (1 - rest/2) * 2 = 2</c> for ANY floor — the Air response keeps the same ceiling it
    /// had, rather than being clipped by <see cref="TickBeds"/>'s 0..2 clamp. Measured at the mean of
    /// the gust LFO with Air at 1.0 the modulator is 1.80 against the pre-223 build's 1.775, i.e.
    /// <b>+0.1 dB</b>: the wind the user has already judged is, to a tenth of a decibel, the wind he
    /// judged.</para>
    /// </summary>
    /// <param name="periodSeconds">The gust LFO's period. The two rooms differ ONLY here (7.93 s at
    /// a cellar window, 11.31 s through a canopy — a bigger, slower body of air), and both are
    /// non-commensurate with everything else in the file, which is item 6 of the class doc.</param>
    private static float WindBed(float periodSeconds)
    {
        if (_windGate <= 0f)
            return WindRestFloor;
        float air = Mathf.Clamp01(ElementMood.Live(2));
        return WindRestFloor
               + _windGate * (1f - 0.5f * WindRestFloor)
                 * (0.55f + 0.45f * Lfo(periodSeconds) + 1.0f * air);
    }

    /// <summary>
    /// Walk <see cref="_fireGate"/> toward whatever the FIRE element is doing. Structurally
    /// identical to <see cref="TickWindGate"/>, deliberately: it is the same problem (a bed that must
    /// arrive and leave without either edge being an event) with a different element, and two gates
    /// that were written differently would drift into behaving differently for no reason anybody
    /// could state.
    ///
    /// <para>Called from <see cref="TickBeds"/> and NOT from the modulators, for the reason the wind
    /// gate's doc gives and which is three times as sharp here: a lambda that integrated would
    /// integrate once per SITE, so the gate would open at TRIPLE rate in a room with three fires.</para>
    /// </summary>
    private static void TickFireGate()
    {
        float fire = Mathf.Clamp01(ElementMood.Live(0));
        float target = Mathf.Clamp01((fire - FireGateOn) / Mathf.Max(FireGateFull - FireGateOn, 1e-4f));
        target = target * target * (3f - 2f * target);   // smoothstep — no corner at either end

        float tau = target > _fireGate ? FireGateOpenSeconds : FireGateCloseSeconds;
        _fireGate = Mathf.MoveTowards(_fireGate, target, Time.deltaTime / Mathf.Max(tau, 0.01f));
    }

    /// <summary>
    /// A fire site's bed level, 0..2. Returns EXACTLY zero while the gate is shut — which is what
    /// <see cref="TickBeds"/> tests to PAUSE the source, and pausing is what makes "with Fire down
    /// there is no fire sound" a property of the audio engine rather than of a multiply. It also
    /// makes it free: a paused source is not spatialised, and (because a one-shot rides its source)
    /// it is what stops a crackle in a room with no fire in it even if a scheduling bug tried.
    ///
    /// <para>The element's own strength is IN the term as well as in the gate, so a fire that is
    /// merely smouldering is quieter than one at full infusion rather than merely later. The gate
    /// MULTIPLIES rather than adds, so at <c>_fireGate = 0</c> every term is gone, not small.</para>
    /// </summary>
    /// <param name="periodSeconds">The slow breathing LFO's period for this site. The three sites'
    /// periods are non-commensurate with each other and with the 6 s roar buffer, so no two fires in
    /// a room ever come into phase and the buffer's own wrap is never reinforced.</param>
    private static float FireBed(float periodSeconds)
    {
        if (_fireGate <= 0f)
            return 0f;
        float fire = Mathf.Clamp01(ElementMood.Live(0));
        return _fireGate * (0.58f + 0.14f * Lfo(periodSeconds) + 0.28f * fire);
    }

    /// <summary>
    /// The one multiply every voice ends up passing through: the player's dial, the hard ceiling,
    /// the duck, and the two volume sliders the player already set inside the GAME's own audio
    /// options. Item 4 of the "never intrusive" list.
    /// </summary>
    private static float Master() => MasterWith(_duck);

    /// <summary>As <see cref="Master"/>, with the duck REPLACED. One caller passes anything but the
    /// live duck: the bookshelf's arrival, which floors it at <see cref="ShelfImpactDuckFloor"/>.
    /// Written as a parameter rather than as a second formula so that the dial, the ceiling and the
    /// game's two volume sliders are applied in exactly one place — an exception that re-derived the
    /// chain would be an exception that stopped obeying the player's sliders the day one of them
    /// moved.</summary>
    private static float MasterWith(float duck)
    {
        float dial = Mathf.Clamp(Gain.Value, 0f, 2f) * (MasterCeiling / 2f);
        return dial * duck * GameVolume();
    }

    /// <summary>
    /// The player's own master and effects volumes, 0..1 each, read out of the save the game's audio
    /// options page writes (<c>Gloomhaven/AudioSettings.cs:242-258</c> stores them as ints 0..100 in
    /// <c>SaveData.Instance.Global</c>, GH.Runtime/GlobalData.cs:88-92).
    ///
    /// <para>THIS IS READ EVERY FRAME rather than cached, because the player can move those sliders
    /// while a scenario is running and the environment has to follow them the way the game's own
    /// sounds do. It is two int reads off a live object; there is nothing to cache.</para>
    ///
    /// <para>Falls back to 1 if the save is not up yet — never to 0, because a silent feature that
    /// looks broken is worse than a brief moment at the designed level, and the designed level is
    /// already quiet.</para>
    /// </summary>
    private static float GameVolume()
    {
        try
        {
            GlobalData? g = SaveData.Instance?.Global;
            if (g == null)
                return 1f;
            return Mathf.Clamp01(g.MasterVolume / 100f) * Mathf.Clamp01(g.SFXVolume / 100f);
        }
        catch
        {
            return 1f;
        }
    }

    /// <summary>
    /// A slow local LFO, 0..1, at a caller-chosen period. The periods passed in are deliberately
    /// non-commensurate with each other and with the 8 s noise bed, so two beds never come into
    /// phase and the buffer's own wrap is never reinforced — item 6 of the "never intrusive" list.
    ///
    /// <para><c>Time.time</c> is correct here and only here: this shapes a bed, not an event, so
    /// there is nothing for two clients to disagree about. Every EVENT in this file reads the shared
    /// clock instead.</para>
    /// </summary>
    private static float Lfo(float periodSeconds) =>
        0.5f + 0.5f * Mathf.Sin(Time.time * (2f * Mathf.PI / Mathf.Max(periodSeconds, 0.1f)));

}
