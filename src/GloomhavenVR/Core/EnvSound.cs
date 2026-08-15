using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  ENV SOUND — the environment, heard. Spatialised sources attached to the things that make them,
//  kept quiet enough that the game always wins. THE ART IS IN Core/EnvSound.Bank.cs; this file is
//  the placement, the levels, the gating and the teardown.
// =================================================================================================

/// <summary>
/// Attaches spatialised <see cref="AudioSource"/>s to the spawned environment while one is standing,
/// drives them from the SHARED environment clock so a sound lands on the same frame as the visual it
/// belongs to on every client, and takes all of them down again on any teardown.
///
/// <para><b>USER REQUEST (2026-08-14, verbatim).</b> "Und dann noch ein größeres Thema:
/// Soundeffekte der Umgebungen. Es soll deaktivierbar sein. Mäusepiepen, ein Tropfgeräusch wegen dem
/// Tropfen, Feuergeräusch, Frostgeräusch, starker Wind etc. So gut wie alles in der Umgebung soll
/// auch mit Soundeffekten hinterlegt werden. Die Sounds sollen verortbar sein von seinen
/// entsprechenden Quellen. Auch hier sollen die Sounds eher dezent sein und nie aufdringlich
/// überlagernd. Die Umgebung spielt immer noch nur eine zweite Rolle neben dem eigentlichen
/// Spiel."</para>
///
/// <para><b>USER CORRECTION, same day, verbatim</b> — it reverses the earlier ruling that the horror
/// easter eggs are silent: "Ich nehme die Entscheidung von zuvor zurück, die Grusel-Erscheinungen
/// sollen NICHT stumm bleiben. Auch hier sollen Soundeffekte kommen aber auch nicht aufdringlich und
/// nur wenn die Umgebungssounds aktiviert sind." So the apparitions DO make sound, on this file's
/// one toggle and no second switch of their own. <c>Haunt.cs:74-75</c> still carries the old "ohne
/// sound" quotation as the reason it publishes no audio channel — that remains true of THAT file
/// (it is still the switch and nothing else); the sound is made here, from the schedule
/// <see cref="Haunt.Resolve"/> now exposes.</para>
///
/// <para><b>TWO LATER RULINGS OVERRIDE PARTS OF THE REQUEST ABOVE, and the request is quoted
/// verbatim rather than edited so that the override reads as an override.</b></para>
/// <list type="number">
/// <item><b>"Frostgeräusch" IS GONE.</b> ModBuild 149, verbatim: "Entferne das Geräusch für Eis
/// komplett." It was built, then rebuilt from the fracture physics up, and then deleted — clip,
/// scheduler, node and constants. Ice is a thing you SEE in these rooms. The ruling and everything
/// that went with it are recorded in <c>EnvSound.Bank.cs</c>.</item>
/// <item><b>ONE SOUND IS ALLOWED TO BE LOUD.</b> "nie aufdringlich überlagernd" has exactly one
/// written exception, ModBuild 149, verbatim: "ich gebe dir hierbei eine Ausnahmegenehmigung hier
/// auch einen lauten Knall Sound einzubauen in dem Moment in das Regal den Boden berührt." That is
/// the bookshelf's arrival and nothing else; see <see cref="ShelfImpactGain"/>, which is the ONLY
/// place in this file that is allowed past <see cref="MaxEmitterGain"/>.</item>
/// </list>
///
/// <para><b>WHY THIS IS RUNTIME CODE AND NOT BUNDLE CONTENT.</b> The environment asset bundle ships
/// no MonoBehaviours at all (<c>unity/.../Editor/BuildEnvironments.cs:35</c>) and no audio. An
/// <see cref="AudioSource"/> is a COMPONENT, so spatial sound cannot come from the bundle even in
/// principle — it has to be created at runtime. (An <see cref="AudioClip"/> is data and could have
/// shipped; <see cref="EnvSoundBank"/> explains why it is synthesized instead.)</para>
///
/// =============================================================================================
/// <para><b>THE LISTENER, AND THE FINDING THAT HAD TO BE FIXED BEFORE ANY OF THIS COULD WORK.</b></para>
///
/// <para>Spatial audio is meaningless without an <see cref="AudioListener"/> on the head, and this
/// mod did not have one. The listener is SCENE-AUTHORED — nothing in the decompiled game ever calls
/// <c>AddComponent&lt;AudioListener&gt;</c>; <c>AudioController.GetCurrentAudioListener()</c>
/// (GH.Runtime/AudioController.cs:940-951) resolves it once with <c>FindObjectOfType</c> and caches
/// it. The mod's own head camera (<c>Rig/VRRigDriver.HeadCamera.cs:288-367</c>) adds exactly a
/// <c>Camera</c> and a <c>TrackedPoseDriver</c> and no listener. Meanwhile the game camera that
/// carries the authored listener is deliberately kept ALIVE and ENABLED
/// (<c>WorldUI/FlatScreen.3.Desktop.cs:225-233</c> — disabling it would break <c>Camera.main</c>)
/// but PARKED: its transform writers are prefix-skipped
/// (<c>Rig/CameraControllerPatches.cs:30-34, 48-52</c>). So the ear has been sitting at a frozen
/// orbit pose, many world units from the head, at a world scale that is not 1.</para>
///
/// <para>That is not a new bug this feature introduces — it is the documented cause of the card-fan
/// sound bug (<c>Core/GameAudio.cs:16-22</c>: a 3D item played at a transform "attenuates to
/// nothing — SILENTLY"). What is new is that this feature CANNOT work around it: an ambience whose
/// sources are in the room is exactly a set of 3D items.</para>
///
/// <para><b>THE FIX: this file takes ownership of the listener while it is running.</b> It adds an
/// <see cref="AudioListener"/> to the mod's head camera and disables every other enabled one,
/// remembering each so <see cref="StandDown"/> can put them back exactly. Unity permits only one
/// enabled listener; more than one is undefined behaviour and logs a warning per frame.</para>
///
/// <para><b>WHY THIS IS SAFE FOR THE GAME'S OWN AUDIO, which is the obvious objection.</b> The
/// cached reference inside <c>AudioController</c> keeps pointing at the authored (now disabled)
/// component, and it re-resolves only if that reference goes NULL (:943-950) — disabling a component
/// does not null it, so the controller never notices. Every game sound the mod actually triggers
/// goes through <c>AudioController.Play(item)</c>, the LISTENER-ANCHORED overload
/// (<c>Core/GameAudio.cs:83</c>), which ignores position; and the game's own UI items are 2D
/// (<c>AudioItem.spatialBlend</c> defaults to 0, ClockStone/AudioItem.cs:67), so position is
/// ignored there too. The only class of sound whose behaviour could change is a 3D game item played
/// positionally — and those are ALREADY inaudible today for precisely the reason quoted above. The
/// change can therefore make that class better or leave it as it is; it cannot make it worse.</para>
///
/// <para><b>REJECTED: moving the authored listener's transform to the head.</b> A component cannot
/// be moved between GameObjects, so this would mean moving the game CAMERA — which is the flat
/// screen's own render source (<c>WorldUI/FlatScreen.2.CameraStack.cs:47-64</c>). The desktop mirror
/// would follow the player's head around the room.</para>
///
/// <para><b>REJECTED: registering our clips with the game's controller</b> via
/// <c>AudioController.AddToCategory(category, clip, id)</c> (AudioController.cs:1142). It is a real
/// API and it would inherit the player's category volumes for free. It also hands playback to a
/// POOL of the controller's own prefabs — no per-source rolloff we can scale, no persistent looping
/// voice we own, and a <c>MinTimeBetweenPlayCalls</c> throttle that silently drops plays
/// (:1611). We need long-lived, individually-tuned, scale-corrected sources. The player's volume
/// choice is honoured directly instead — see THE GAIN BUDGET.</para>
///
/// =============================================================================================
/// <para><b>THE SCALE PROBLEM, AND WHY A TYPED ROLLOFF WOULD HAVE BEEN WRONG BY A FACTOR OF ~20.</b></para>
///
/// <para>Unity's distance attenuation is in WORLD UNITS. The environment is not placed at world
/// scale: the rig root's <c>lossyScale</c> IS the diorama scale (<c>Rig/VRRigDriver.cs:61-65</c>,
/// written as <c>rig.localScale = Vector3.one * s</c> in <c>Rig/Comfort.cs:74</c>), and
/// <see cref="SkyAlternative"/> reads it as <c>anchor.lossyScale.x</c> (SkyAlternative.cs:1282) and
/// converts world to perceived by DIVIDING by it (":1373", <c>perceived = extent / rigScale</c>).
/// So:</para>
/// <code>
///     rigScale = world units per PERCEIVED METRE          (the log reports values like 21…26)
///     worldDistance = perceivedMetres * rigScale
/// </code>
/// <para>An attenuation curve authored in metres and typed straight into
/// <see cref="AudioSource.minDistance"/> would therefore reach full attenuation about twenty times
/// too close — every environment sound would be inaudible except when the player's head was
/// practically inside it. The fix is one multiply, applied to BOTH distances of every source, and
/// re-applied whenever the scale changes: <see cref="Tick"/> takes the live <c>rigScale</c> from the
/// caller and re-scales the sources when it moves by more than
/// <see cref="ScaleRefreshTolerance"/>. Zoom is exactly such a change
/// (<c>SkyAlternative.NotifyRigScaled</c> is the existing hook and both its callers are zoom
/// gestures), which is why this cannot be a one-time setup.</para>
///
/// <para><b>AND THE DOPPLER IS ZERO, for the same reason.</b> Every source here is static in world
/// space, but the LISTENER is not: at rigScale ~22 a comfortable head movement is ~22 world units
/// per second, which Unity would hear as a supersonic listener and pitch-shift accordingly.
/// <see cref="AudioSource.dopplerLevel"/> 0 is not a stylistic choice, it is the only correct value
/// in a scaled world.</para>
///
/// =============================================================================================
/// <para><b>"DIE UMGEBUNG SPIELT IMMER NOCH NUR EINE ZWEITE ROLLE" — HOW THAT IS ENFORCED, and it
/// is six separate mechanisms because it is the acceptance criterion for the whole feature.</b></para>
/// <list type="number">
/// <item><b>A hard gain budget.</b> Every emitter's level is a fraction under
/// <see cref="MaxEmitterGain"/>, and <see cref="MasterCeiling"/> caps what the dial can reach even
/// at its maximum. The player CAN turn it up; they cannot turn it up to where it competes.</item>
/// <item><b>Spectral separation.</b> The continuous beds are pink-ish noise low-passed at
/// <see cref="BedLowPassHz"/>. This is the one "never mask" measure that had to be chosen on
/// principle rather than measured: the game's clips live in AudioObject prefabs and asset bundles,
/// not in the decompiled C#, so their actual spectra are NOT readable from here. What IS certain is
/// that speech intelligibility and UI transients live in roughly 1–4 kHz, so the beds are rolled off
/// below that band and the only content above it is BRIEF (a 90 ms squeak, a 6 ms drip transient, a
/// 12 ms crack as the bookshelf reaches the floor) — too short to mask anything, which is a property
/// of duration and needs no measurement. THE ESCAPE CLAUSE IS NOT A LOOPHOLE AND ONE CLIP FAILED IT:
/// the shipped ice sound was a 200 ms ring in that band repeating every 0.45 s, which is a texture
/// rather than a transient, and the user's verdict on it was "super nervig". It was rebuilt as a
/// 14 ms fracture that did qualify — and then DELETED OUTRIGHT one round later on his ruling
/// ("Entferne das Geräusch für Eis komplett"), so ice is now seen and never heard. Duration is the
/// test, and the one deliberate exception to the whole budget is named in <see cref="ShelfImpactGain"/>.</item>
/// <item><b>Ducking against the game itself.</b> <see cref="Tick"/> polls
/// <c>AudioController.GetPlayingAudioObjects()</c> (AudioController.cs:880) and pulls the whole
/// ambience down to <see cref="DuckFloor"/> whenever the game is making ANY sound at all. This is
/// the mechanism that makes the requirement literally true rather than merely likely: while a
/// character speaks or an ability resolves, the environment is measurably quieter.</item>
/// <item><b>The player's own volume choice is obeyed.</b> A mod-owned <see cref="AudioSource"/>
/// bypasses the game's category volumes entirely, so the two sliders the player already set are read
/// directly out of the save (<c>SaveData.Instance.Global.MasterVolume</c> /
/// <c>.SFXVolume</c>, GH.Runtime/GlobalData.cs:88-92, the same ints
/// <c>Gloomhaven/AudioSettings.cs:242-258</c> writes) and multiplied in. A player who has muted
/// effects hears nothing from here.</item>
/// <item><b>A concurrency cap.</b> At most <see cref="MaxVoices"/> sources exist at once; one-shots
/// share a small pool and the oldest is reused. A crowd of simultaneous cues cannot build up.</item>
/// <item><b>Nothing loops at a period the ear can find.</b> The beds are stationary noise with an
/// equal-power wrap fade, and every recognisable contour is applied here at runtime by LFOs whose
/// periods are in irrational ratios to each other and to the buffer length. See
/// <see cref="EnvSoundBank"/>.</item>
/// </list>
///
/// =============================================================================================
/// <para><b>MULTIPLAYER.</b> Zero wire bytes, and that is a conclusion rather than an omission.
/// Audio is local — there is nothing to replicate about a sound a headset makes. What has to agree
/// between clients is the EVENT the sound marks, and every event here is already shared: the drip's
/// phase, the rat's crossing and the haunt's slot are all pure functions of
/// <see cref="SkyAlternative.EnvClockSeconds"/>, the mod's shared environment epoch, which the net
/// layer already elects an owner for. Two players therefore hear the drip on the same frame by
/// construction, with nothing added to the packet. <c>Time.time</c> is used NOWHERE in this file for
/// anything a listener could correlate with a visual — only for the local smoothing of the duck and
/// the gain LFOs, which are per-client by nature.</para>
///
/// <para><b>TEARDOWN.</b> No source, filter, listener change or clip may survive a stand-down, a
/// mixed-reality switch, a style change or leaving the scenario. This follows the pattern
/// <c>ElementMood</c> and <c>Haunt</c> established: an idempotent <see cref="StandDown"/> that is
/// called from BOTH <c>SkyAlternative.StandDown()</c> (the MR route — this feature is attached to
/// GEOMETRY, so unlike ElementMood it must go down with it) and
/// <c>SkyAlternative.RestoreAll()</c> (full teardown).</para>
/// </summary>
internal static class EnvSound
{
    // ---- config ---------------------------------------------------------------------------------

    /// <summary>Master switch. OPTIONAL CONTENT under the standing settings ruling: it adds an
    /// effect, it does not repair a broken interaction.</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>Volume dial. Named <c>Gain</c> rather than <c>Volume</c> on purpose: the config
    /// stepper picks its increment from a table of recognised unit SUFFIXES
    /// (<c>WorldUI/ConfigSteps.cs</c>), <c>Gain</c> is in it and <c>Volume</c> is not, and an
    /// unrecognised suffix silently falls back to "a fiftieth of the shipped default" and produces
    /// an unusable stepper — a defect this project has had user-reported twice. The player never
    /// sees the key; they see the localized display name.</summary>
    internal static ConfigEntry<float> Gain = null!;

    private static bool _bound;

    /// <summary>
    /// Bind into the RIG module's config file, riding along behind the sky, element and haunt
    /// dials — the fourth ride-along on that file, for the fourth time the same reason: this is a
    /// property OF the environment the <c>[Sky] Style</c> dropdown chooses, and a player looking for
    /// it on disk will look where the environment choice is.
    /// </summary>
    internal static void BindConfig(ConfigFile file)
    {
        if (_bound)
            return;
        _bound = true;

        Enabled = file.Bind("EnvSound", "Enabled", Defaults.EnvSoundEnabled,
            "Give the 3D environment SOUND: a drip into its puddle, the candle flames, the draught "
            + "at the window, the rat as it crosses, the night in the swamp — and a quiet cue for "
            + "the creepy easter eggs. Every sound comes from the object that makes it and is placed "
            + "in 3D, so a drip in the corner is heard in the corner, and every one of them is tied "
            + "to what is actually happening rather than to a timer: the drip sounds when the drop "
            + "lands, the bookshelf BANGS on the frame it actually reaches the floor, and the fire "
            + "answers a Fire infusion. THE WIND IS ONLY THERE WHILE AIR IS: with no Air "
            + "infusion up there is no draught and no rustle at all — the leaves still move, they "
            + "just make no noise. ICE MAKES NO SOUND AT ALL — you can see the frost, you never hear "
            + "it. "
            + "Deliberately QUIET and always secondary to the game — the whole ambience ducks "
            + "automatically whenever the game itself makes any sound, and it obeys the master and "
            + "effects volumes you already set in the game's own audio options. OFF removes it "
            + "completely and costs nothing: no source is created and nothing is read per frame. "
            + "Purely local — it changes NOTHING about the game state and adds NO network traffic, "
            + "because every event it follows is already on the shared environment clock. Only "
            + "inside a running scenario, like the environment itself; mixed reality switches it off "
            + "with the environment. Applies live.");

        Gain = file.Bind("EnvSound", "Gain", Defaults.EnvSoundGain,
            new ConfigDescription(
                "How loud the environment is. 1 = as designed, which is already deliberately quiet. "
                + "Lower is subtler, 0 is the same as switching the sound off. The maximum is capped "
                + "well below the game's own level on purpose: the environment is never allowed to "
                + "compete with speech or the game's cues. Has no effect at all while 'Enabled' is "
                + "off. Applies live.",
                new AcceptableValueRange<float>(0f, 2f)));
    }

    // ---- the gain budget -------------------------------------------------------------------------

    /// <summary>The most any SINGLE emitter may reach on the Unity 0..1 volume scale, before the
    /// master dial and the duck. A bed sitting at this level with the game silent is at the edge of
    /// noticeable in a quiet room, which is the brief. ONE cue is allowed past it and only one — see
    /// <see cref="ShelfImpactGain"/>, which carries the written permission for it.</summary>
    private const float MaxEmitterGain = 0.16f;

    // ---- THE ONE EXCEPTION -----------------------------------------------------------------------
    //
    //  USER RULING, ModBuild 149, verbatim: "Ich höre immer noch keine Impactsounds beim Bücherregal
    //  das umkippt - ich gebe dir hierbei eine Ausnahmegenehmigung hier auch einen lauten Knall Sound
    //  einzubauen in dem Moment in das Regal den Boden berührt."
    //
    //  IT IS AN EXCEPTION TO ONE RULE, NOT A RELAXATION OF THE BUDGET, and the scope is exactly the
    //  words he used: one bang, at the instant the shelf touches the floor. It is spent HERE and
    //  nowhere else. Every bed, the drip, the rat, the squeak and all five remaining haunt cues are
    //  still under MaxEmitterGain, and this cue still ducks, still obeys the player's dial and still
    //  obeys the two volume sliders in the game's own audio options — the permission is to be loud,
    //  not to be unstoppable.
    //
    //  WHY A SECOND CEILING AND NOT SIMPLY "NO CAP HERE". A cue with no ceiling is a cue whose level
    //  is decided by whatever number the last editor typed, and this file's whole gain discipline is
    //  that a number is compared against a stated maximum. So the exception gets its own maximum,
    //  stated, and PlayShot still clamps — it just clamps against a different constant for this one
    //  call. See ScheduleShelfContacts for the three cues that pass it in.

    /// <summary>The bookshelf's arrival on the floor, before master. 0.55 against the 0.080 this cue
    /// shipped with — <b>+16.7 dB</b>.
    ///
    /// <para>WHAT IT REACHES: at the default dial the master is 0.75, so the source volume is 0.41
    /// on Unity's 0..1 scale, against 1.0 for a game cue at full level. That is a bang the player
    /// cannot miss and is still not the loudest thing in the room, which is the honest reading of
    /// "einen lauten Knall" from someone whose standing rule is that the environment comes second.
    /// Under the duck — i.e. while the game itself is making any sound — it lands at 0.14, still
    /// louder than the 0.060 the un-ducked shipped cue managed.</para>
    ///
    /// <para>THE LEVEL IS ONLY HALF OF WHY HE COULD NOT HEAR IT. The other half was the clip: 97% of
    /// its energy sat below 500 Hz, in the band a Quest 3 speaker does not reproduce. Raising the
    /// gain without fixing that would have produced a louder inaudibility. Both are fixed; see
    /// <c>EnvSoundBank.MakeFall</c>.</para></summary>
    private const float ShelfImpactGain = 0.55f;

    /// <summary>...and the ceiling the exception is clamped against, in place of
    /// <see cref="MaxEmitterGain"/> and for this cue only. 0.60 leaves the constant above a little
    /// room to be tuned upward on hardware without a second edit here, and stops it from ever being
    /// tuned into "louder than the game", which is not what was granted.</summary>
    private const float ShelfImpactCeiling = 0.60f;

    /// <summary>What the dial multiplies up to at its maximum of 2. The cap is why the dial cannot
    /// be turned into a problem: at 2.0 the loudest emitter reaches 0.16 x 1.5 = 0.24, still far
    /// under a game cue at 1.0.</summary>
    private const float MasterCeiling = 1.5f;

    /// <summary>The ambience falls to this fraction of itself while the game is making any sound at
    /// all. 0.35 is about -9 dB — clearly out of the way without the duck itself becoming an audible
    /// event, which a deeper one would be.</summary>
    private const float DuckFloor = 0.35f;

    /// <summary>Seconds to fall into the duck. Fast, but not so fast that it clicks.</summary>
    private const float DuckAttackSeconds = 0.12f;

    /// <summary>Seconds to come back out. Slow and asymmetric on purpose: a fast release would
    /// pump audibly between two closely-spaced game cues.</summary>
    private const float DuckReleaseSeconds = 0.75f;

    /// <summary>Corner of the low pass on every continuous bed. Below the 1-4 kHz band that speech
    /// intelligibility and UI transients occupy — see the class doc's item 2, including why this
    /// number is reasoned rather than measured.</summary>
    private const float BedLowPassHz = 1150f;

    /// <summary>Hard cap on live sources. Beds plus the one-shot pool plus the element voices fit
    /// inside it with room to spare; it exists so that no future addition can quietly turn the
    /// ambience into a crowd.</summary>
    private const int MaxVoices = 12;

    /// <summary>One-shot voices. Three is enough for the densest legal moment (a drip landing while
    /// the rat crosses under a haunt cue) and is itself a concurrency limit: a fourth simultaneous
    /// one-shot steals the oldest voice rather than adding to the pile.</summary>
    private const int OneShotVoices = 3;

    /// <summary>Relative change in rig scale that forces every source's rolloff to be recomputed.
    /// A tenth of a percent — far below audibility, and cheap: the recompute is two float writes
    /// per source.</summary>
    private const float ScaleRefreshTolerance = 0.001f;

    /// <summary>Frames between polls of the game's playing-sound count. The poll allocates (the
    /// game's API builds a List), so it is throttled — 10 frames is ~9 Hz at 90 Hz, and the duck
    /// attack is 120 ms, so the throttle is never the thing you hear.</summary>
    private const int DuckPollFrames = 10;

    // ---- the wind gate ---------------------------------------------------------------------------
    //
    // USER REPORT, ModBuild 147 hardware, verbatim: "Wind Geräusch nur wenn auch Wind aktiv ist,
    // sonst kein Geräusch (die leichte Bewegung die aktuell Standard ist kann bleiben)." — "Wind
    // SOUND only when wind is actually active, otherwise no sound (the slight MOTION that is
    // currently the default can stay)."
    //
    // THE DISTINCTION HE DRAWS IS EXACT AND IT SPLITS ACROSS TWO LANES. The resting sway of the
    // leaves and the lean of the candle flames are the SHADERS' — EnvRoom/EnvFlame do that from
    // their own time uniforms and they are untouched by anything here. What he is switching off is
    // the AUDIO bed, which this file owns. So this is a gate on two emitters and on nothing else:
    // the cellar's "Draught" at the window and the swamp's "Leaves" in the canopy. The candle
    // flames, the swamp's insect floor, the wisp and the Earth rumble are not wind and do not move.
    //
    // WHY IT IS A SMOOTHED GATE AND NOT `if (air > 0)`. Two reasons, and the second is the one that
    // decided the shape:
    //   * A CUT IS ITSELF AN EVENT. A stationary noise bed that stops is more noticeable than one
    //     that plays — the ear tracks onsets and OFFSETS equally, and an offset with a 70 ms edge
    //     (which is what TickBeds' 0.6/s volume walk gives at these levels) reads as a gate closing.
    //     "Kein Geräusch" has to arrive without announcing itself.
    //   * AIR ITSELF IS NOT A STEP. ElementMood ramps every element over RampSeconds = 1.0 s with a
    //     smoothstep, so the element is already gentle going up; what it is not is gentle coming
    //     DOWN in the way air behaves — a draught that has stopped being driven dies away over
    //     seconds, not over one. Hence the asymmetry below, which is the same argument (and the same
    //     direction) as the duck's attack/release pair thirty lines up.

    /// <summary>Air intensity at which the wind bed starts to exist at all, and the intensity at
    /// which it is fully open. Below <see cref="AirGateOn"/> there is NO wind sound whatsoever — not
    /// a quiet one — which is the request read literally. The span up to
    /// <see cref="AirGateFull"/> exists so that the gate opens ACROSS the element's own ramp rather
    /// than at one point on it: a threshold crossed instantly would put a step into the middle of a
    /// smooth rise, which is the one place a step is most audible.</summary>
    private const float AirGateOn = 0.06f;
    private const float AirGateFull = 0.45f;

    /// <summary>Seconds for the gate to open, and to close. ASYMMETRIC, and much more so than the
    /// duck's: a draught is a mass of moving air with momentum, so it arrives faster than it leaves.
    /// 2.4 s of close is also what makes the OFF edge inaudible — at the bed's own level a 2.4 s
    /// decay is well under the just-noticeable rate for a slow fade, so the sound is simply not
    /// there any more rather than having stopped.</summary>
    private const float AirGateOpenSeconds = 0.9f;
    private const float AirGateCloseSeconds = 2.4f;

    /// <summary>The gate itself, 0..1, walked in <see cref="TickBeds"/> and read by
    /// <see cref="WindBed"/>. ONE field for both rooms because only one of the two wind beds can
    /// exist at a time (they belong to different styles), and a single field is what makes "the wind
    /// is up" one fact rather than two that could disagree.
    ///
    /// <para><c>Time.deltaTime</c> and not the shared clock, for the duck's reason: this is a LOCAL
    /// smoothing of a value that is itself already shared and already smoothed. Two clients open
    /// their gates within a frame of each other on a bed with no onset to correlate — there is
    /// nothing here a listener could compare.</para></summary>
    private static float _windGate;

    // ---- live state --------------------------------------------------------------------------------

    private sealed class Voice
    {
        internal string Name = string.Empty;          // diagnostic only
        internal GameObject Go = null!;
        internal AudioSource Source = null!;
        internal AudioLowPassFilter? LowPass;
        internal float BaseGain;                      // 0..MaxEmitterGain
        internal float MinMeters;                     // PERCEIVED metres — scaled by rigScale
        internal float MaxMeters;
        internal System.Func<float>? Modulate;        // per-frame 0..1 multiplier, null = flat
        internal string NodeName = "(unresolved)";    // which node it actually landed on
    }

    private static readonly List<Voice> Beds = new();
    private static readonly List<Voice> Shots = new();

    private static GameObject? _root;                 // parent for every source we own
    private static SkyStyle _builtStyle = SkyStyle.Default;
    private static bool _built;
    private static float _builtScale;

    private static AudioListener? _ourListener;
    private static readonly List<AudioListener> _suppressed = new();

    private static float _duck = 1f;
    private static int _duckPollCountdown;
    private static bool _gameAudible;

    // Event edge detectors. Each stores the identity of the LAST event fired, never a timer, so a
    // clock jump (a new clock owner, a scene reload) re-anchors instead of double-firing.
    private static long _lastDripIndex = long.MinValue;
    private static long _lastRatSlot = long.MinValue;
    private static float _lastHauntStart = float.NaN;
    private static int _lastHauntCard = -1;

    private static int _nextShot;

    // THE EVENT NODES, RESOLVED ONCE AT BUILD. They are cached rather than looked up per event, and
    // that is not a micro-optimisation: `Find` is a recursive walk of a photoscanned room's whole
    // hierarchy, the drip alone fires every 2.85 s, and this project has just spent a round
    // DELETING periodic full-scene sweeps for exactly this cost. The room is frozen once placed and
    // these nodes never move within it, so one resolution per build is not merely cheaper, it is
    // the correct number.
    private static Transform? _dripNode;
    private static Transform? _ratNode;

    // ---- the authored schedule, mirrored ------------------------------------------------------------
    //
    // These are the CONTENT LANE's numbers, and they are duplicated here for the same reason
    // RoomBoundShellChildren duplicates the node names: they are a CONTRACT with
    // unity/.../Editor/BuildEnvironmentRooms.cs, which states them from its side. A sound that
    // invented its own timeline instead of reading these would drift away from the picture within
    // seconds, which is the one failure this whole file exists to avoid.

    /// <summary>Seconds between drops. MIRROR of <c>DripPeriod</c> (BuildEnvironmentRooms.cs:1703),
    /// written onto both the drip and the puddle material (:5569, :5513).</summary>
    private const float DripPeriod = 2.85f;

    /// <summary>How long a drop clings to the plank before it falls (<c>DripHang</c>, :1704).</summary>
    private const float DripHang = 1.55f;

    /// <summary>The plank it forms on, and the water surface (<c>DripY0</c>/<c>DripY1</c>, :1705-6).</summary>
    private const float DripY0 = 3.252f;
    private const float DripY1 = 0.008f;

    /// <summary>
    /// WHEN THE DROP LANDS, in seconds from the start of each period — the ONE number this feature
    /// needs and the reason the four constants above are mirrored rather than a single "2.36".
    ///
    /// <para>It is DERIVED, exactly as the bake derives it: <c>EnvPuddle._Impact</c> is set to
    /// <c>DripHang + DripFall</c> (:5515) where <c>DripFall = sqrt(2*(Y0-Y1)/9.81)</c> (:1707) — the
    /// drop is in free fall, so the fall time is the physics and not a tunable. Typing the sum as a
    /// literal would be a fifth number to keep in step with four others; deriving it means a content
    /// change to the ceiling height moves the sound with the picture automatically.</para>
    /// </summary>
    private static float DripImpactSeconds => DripHang + Mathf.Sqrt(2f * (DripY0 - DripY1) / 9.81f);

    /// <summary>The rat's slot beat and its schedule, MIRRORED from
    /// <c>RatPeriod</c>/<c>RatSkip</c>/<c>RatTiming</c> (BuildEnvironmentRooms.cs:1760-1762) and
    /// from <c>EnvCritter.shader</c>'s <c>_Skip</c>/<c>_Timing</c> (:145-146), which are the same
    /// numbers on the GPU side.</summary>
    private const float RatPeriod = 26f;
    private const float RatSkip = 0.15f;
    private const float RatStartLo = 0.05f;
    private const float RatStartSpan = 0.55f;

    // ---- the per-frame driver ------------------------------------------------------------------------

    /// <summary>
    /// One frame. Called from <see cref="SkyAlternative.Tick"/>'s active branch, which is the one
    /// place that already knows the two branch roots, the applied style and the live rig scale — the
    /// four things this needs and none of which have an accessor (and deliberately so: see
    /// <c>ElementMood</c>'s class doc on why the environment roots are private). Passing them in as
    /// arguments keeps that encapsulation intact instead of opening the roots up to the whole mod.
    ///
    /// <para>NOT registered as a tail step. <c>VRRigDriver._tailSteps</c> is a LOCKED frame order
    /// (.planning/refactor/FRAME-ORDER.lock) whose last step must stay last, and this feature has no
    /// ordering relationship with any camera-state step in it — the same argument
    /// <c>ElementMood</c> makes for sitting where it sits.</para>
    /// </summary>
    /// <param name="roomGo">The world-fixed, board-anchored ROOM branch root, or null.</param>
    /// <param name="style">The style actually standing.</param>
    /// <param name="rigScale">World units per perceived metre — see THE SCALE PROBLEM.</param>
    internal static void Tick(GameObject? roomGo, SkyStyle style, float rigScale)
    {
        if (!_bound)
            Rig.RenderQuality.Bind();

        // OFF is free: one bool read and an early return. Everything this feature owns is torn down
        // on the first frame after the switch moves, and nothing is allocated while it is off.
        if (!Enabled.Value)
        {
            StandDown("the setting is off");
            return;
        }

        // Only the two styles that actually have content. Default shows the game's own sky and
        // OffBlack shows nothing at all; there is no object in either to attach a sound to, and an
        // ambience with no visible source would be exactly the disembodied stereo bed the user
        // ruled out ("verortbar von seinen entsprechenden Quellen").
        if (roomGo == null || (style != SkyStyle.Cellar && style != SkyStyle.SwampNight))
        {
            StandDown(style == SkyStyle.Default || style == SkyStyle.OffBlack
                          ? "the chosen environment has no objects to place sounds on"
                          : "the environment is not standing");
            return;
        }

        EnvSoundBank.Build();
        if (!EnvSoundBank.Ready)
            return;   // the bank said why, once, in its own log line

        if (!(rigScale > 0f) || float.IsInfinity(rigScale))
            rigScale = 1f;

        if (!_built || _builtStyle != style)
            Build(roomGo, style, rigScale);
        if (!_built)
            return;

        // ZOOM MOVED THE WORLD. Re-scale every rolloff rather than letting the ambience drift into
        // or out of earshot — the whole point of THE SCALE PROBLEM note.
        if (Mathf.Abs(rigScale - _builtScale) > ScaleRefreshTolerance * _builtScale)
            ApplyScale(rigScale, "the rig was rescaled (zoom)");

        float clock = SkyAlternative.EnvClockSeconds;

        TickDuck();
        TickBeds();
        TickEvents(style, clock);
    }

    // ---- construction ---------------------------------------------------------------------------------

    private static void Build(GameObject roomGo, SkyStyle style, float rigScale)
    {
        // keepListener: a REBUILD (a style change) must not hand the ear back and take it again in
        // one frame — see TakeListener's first guard for what that costs.
        Teardown(keepListener: true);

        _root = new GameObject("GloomhavenVR.EnvSound");
        // Parented to the ROOM branch, not world-anchored: the room is pinned to the board and
        // frozen, so the sources inherit exactly the frame the objects they belong to live in, and a
        // room that is destroyed takes its sounds with it even if some future path forgets to call
        // StandDown. Belt and braces on the "nothing left standing" requirement.
        _root.transform.SetParent(roomGo.transform, false);

        TakeListener();

        if (style == SkyStyle.Cellar)
            BuildCellar(roomGo);
        else
            BuildSwamp(roomGo);

        BuildShotPool();

        // Resolve the event nodes ONCE — see the fields' comment for why this may not be per event.
        _dripNode = Find(roomGo.transform, "Drip", "Puddle");
        _ratNode = Find(roomGo.transform, "Rat");

        _built = true;
        _builtStyle = style;
        ApplyScale(rigScale, "the environment was built");
        LogBuilt(style, rigScale);
    }

    private static void BuildCellar(GameObject room)
    {
        // THE CANDLES. Three groups, each a node the bake named "Candles<n>" (BuildEnvironmentRooms
        // .cs:2653, CandleGroup). A flame is the "Feuergeräusch" the user asked for at its resting
        // size; the Fire infusion pushes it up (see the modulator).
        int candles = 0;
        foreach (Transform t in FindByPrefix(room.transform, "Candles"))
        {
            AddBed($"Flame{candles}", t, EnvSoundBank.Bank(EnvSoundClip.Bed), 0.055f, 0.6f, 5.5f,
                   () => 0.72f + 0.28f * Lfo(3.11f) + 0.9f * ElementMood.Live(0));
            candles++;
            if (candles >= 3)
                break;
        }

        // THE WINDOW. The draught enters here — the bake's DraftDir starts at the window and leaves
        // under the stair door (BuildEnvironmentRooms.cs:1710-1714), and the flames and dust motes
        // already lean along it. "Starker Wind" is the Air infusion pushing this one.
        //
        // GATED ON AIR since ModBuild 148 — see THE WIND GATE. With no Air infusion up this bed is
        // silent AND PAUSED, which is the user's "sonst kein Geräusch" read literally rather than
        // "quietly".
        Transform? window = Find(room.transform, "WindowGlow", "WindowReveal", "WindowBars");
        if (window != null)
            AddBed("Draught", window, EnvSoundBank.Bank(EnvSoundClip.Bed), 0.075f, 1.2f, 14f,
                   () => WindBed(7.93f));

        // EARTH. No node of its own — it is the room itself settling, so it sits at the room root
        // and is silent until an Earth infusion is up. This is the one bed with no resting level:
        // a permanent subsonic rumble in a cellar would be a drone, not an atmosphere.
        AddBed("Rumble", room.transform, EnvSoundBank.Bank(EnvSoundClip.Rumble), 0.10f, 2f, 26f,
               () => 1.30f * ElementMood.Live(3));
    }

    private static void BuildSwamp(GameObject room)
    {
        // THE CANOPY. Leaves in the wind — the same shared noise bed as the cellar's draught, high
        // passed by its own filter rather than by a second clip, and gusting on a slow LFO.
        //
        // GATED ON AIR since ModBuild 148, and this is the emitter the user's report is most
        // literally about: LEAVES RUSTLING *ARE* THE WIND SOUND. With Air down the canopy still
        // SWAYS — that is EnvRoom's own vertex motion and is exactly the "leichte Bewegung die
        // aktuell Standard ist" he asked to keep — it simply makes no noise. The swamp's resting
        // ambience is then the insect floor and the wisp, which is what a still night is.
        Transform? canopy = Find(room.transform, "Canopy", "TrunksNear", "Ground");
        if (canopy != null)
            AddBed("Leaves", canopy, EnvSoundBank.Bank(EnvSoundClip.Bed), 0.070f, 2f, 22f,
                   () => WindBed(11.31f));

        // THE GROUND. Night insects. This is the swamp's floor, so it is the one sound the player
        // is inside rather than beside — a wide rolloff, deliberately.
        Transform? ground = Find(room.transform, "Ground", "RoomGeo");
        if (ground != null)
            AddBed("Night", ground, EnvSoundBank.Bank(EnvSoundClip.Chirr), 0.050f, 3f, 30f,
                   () => 0.80f + 0.20f * Lfo(13.77f));

        // THERE IS NO WISP BED, and this note is here so nobody re-derives one from the clip.
        // The wood used to carry three free-standing halos — WispWisp, WispLantern, WispFar — and
        // this bed hummed at whichever of them existed. USER, ModBuild 149, on Kugeln.jpg: "In der
        // Map sind nun dauerhaft so leuchtende Kugeln ... entferne die." All three were deleted from
        // the bake in the same round, so `Find` would now return null on every candidate and the bed
        // would be dead code that reads like a feature. It is worth recording that it was ALREADY
        // dead before that: the bake prefixes its own family name (`Place(root, "Wisp" + n, ...)`),
        // so the marsh light's node was literally called WispWisp, while the shipped lookup asked
        // for "Wisp" and `Find` is an exact ordinal match — the bed never played in any build that
        // shipped it, silently, because a missing node is the same "no emitter" path as a room that
        // has no wisp. Both facts together are why this is a deletion and not a rename.

        AddBed("Rumble", room.transform, EnvSoundBank.Bank(EnvSoundClip.Rumble), 0.10f, 2f, 26f,
               () => 1.30f * ElementMood.Live(3));
    }

    /// <summary>
    /// The one-shot pool. Fixed size, created once, repositioned per event — never
    /// <c>PlayClipAtPoint</c>, which creates and destroys a GameObject per sound and would leave
    /// exactly the kind of orphan this feature is required not to leave.
    /// </summary>
    private static void BuildShotPool()
    {
        for (int i = 0; i < OneShotVoices; i++)
        {
            var v = NewVoice($"Shot{i}", _root!.transform, null, 0f, 1f, 12f, loop: false, lowPass: false);
            Shots.Add(v);
        }
    }

    private static void AddBed(string name, Transform at, AudioClip? clip, float gain,
                               float minMeters, float maxMeters, System.Func<float> modulate)
    {
        if (clip == null || Beds.Count + Shots.Count >= MaxVoices)
            return;
        var v = NewVoice(name, at, clip, gain, minMeters, maxMeters, loop: true, lowPass: true);
        v.Modulate = modulate;
        v.NodeName = at.name;
        v.Source.volume = 0f;   // faded in by the first TickBeds — nothing ever starts at full

        // START EACH BED AT A DIFFERENT POINT IN ITS BUFFER. The flame, the draught and the leaves
        // deliberately SHARE one noise clip (see EnvSoundBank: one source of noise plus two filters
        // is the correct physical model and is what stops the buffer being findable). Started at the
        // same instant they would play the identical sample stream — perfectly correlated, so they
        // would sum coherently to about +10 dB instead of the +5 dB of independent noise, and the
        // three would collapse into ONE audible source coming from three places at once. Offsetting
        // by an irrational fraction of the clip decorrelates them completely, for one float write.
        if (clip.length > 0.01f)
            v.Source.time = clip.length * (0.3819660f * Beds.Count % 1f);

        v.Source.Play();
        Beds.Add(v);
    }

    private static Voice NewVoice(string name, Transform parent, AudioClip? clip, float gain,
                                  float minMeters, float maxMeters, bool loop, bool lowPass)
    {
        var go = new GameObject("GhvrEnvSound." + name);
        go.transform.SetParent(parent, false);

        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = loop;
        src.playOnAwake = false;
        src.volume = 0f;

        // THE FOUR SETTINGS THAT MAKE IT A PLACE RATHER THAN A BED:
        src.spatialBlend = 1f;                              // fully 3D — the user's "verortbar"
        src.rolloffMode = AudioRolloffMode.Logarithmic;     // physical, and the default Unity tunes for
        src.dopplerLevel = 0f;                              // see THE SCALE PROBLEM: the listener is fast
        src.spread = 25f;                                   // a hair of width so a source is not a pinpoint
        src.bypassReverbZones = true;                       // the game's reverb zones are sized for the
                                                            // game's world, not for a 20x diorama
        src.priority = 200;                                 // well below the game's default 128: if the
                                                            // platform ever runs out of voices, OURS are
                                                            // the ones it drops. That is the whole brief.

        if (lowPass)
        {
            var lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = BedLowPassHz;
            lp.lowpassResonanceQ = 1f;
        }

        var v = new Voice
        {
            Name = name,
            Go = go,
            Source = src,
            LowPass = go.GetComponent<AudioLowPassFilter>(),
            BaseGain = Mathf.Min(gain, MaxEmitterGain),
            MinMeters = minMeters,
            MaxMeters = maxMeters,
        };
        return v;
    }

    /// <summary>
    /// Convert every voice's PERCEIVED distances into world units. This is the whole of the scale
    /// fix, and it is deliberately one function so there is exactly one place where metres become
    /// world units.
    /// </summary>
    private static void ApplyScale(float rigScale, string why)
    {
        _builtScale = rigScale;
        for (int i = 0; i < Beds.Count; i++)
            Scale(Beds[i], rigScale);
        for (int i = 0; i < Shots.Count; i++)
            Scale(Shots[i], rigScale);

        VRLog.Info("Core", $"ENV SOUND rolloff rescaled — {why}. rigScale {rigScale:F2} world units " +
                           "per perceived metre, so every source's minDistance/maxDistance is its " +
                           "authored PERCEIVED distance times that factor. Without this correction " +
                           "the curves would be authored in metres and read in world units, and the " +
                           $"whole ambience would attenuate about {rigScale:F0}x too close to hear.");

        static void Scale(Voice v, float s)
        {
            v.Source.minDistance = v.MinMeters * s;
            v.Source.maxDistance = v.MaxMeters * s;
        }
    }

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
        TickWindGate();

        float master = Master();
        for (int i = 0; i < Beds.Count; i++)
        {
            Voice v = Beds[i];
            float m = v.Modulate != null ? Mathf.Clamp(v.Modulate(), 0f, 2f) : 1f;
            float want = v.BaseGain * m * master;
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
    /// The wind bed's level, 0..2, for both rooms. ONE function, so the cellar's draught and the
    /// swamp's canopy cannot drift into being two different winds — they are the same air.
    ///
    /// <para>Returns EXACTLY zero while the gate is shut, which is what
    /// <see cref="TickBeds"/> tests to pause the source. The gust shape multiplies the gate rather
    /// than adding to it, so there is no residue for the LFO to modulate at rest: at
    /// <c>_windGate = 0</c> every term is gone, not small.</para>
    /// </summary>
    /// <param name="periodSeconds">The gust LFO's period. The two rooms differ ONLY here (7.93 s at
    /// a cellar window, 11.31 s through a canopy — a bigger, slower body of air), and both are
    /// non-commensurate with everything else in the file, which is item 6 of the class doc.</param>
    private static float WindBed(float periodSeconds)
    {
        if (_windGate <= 0f)
            return 0f;
        float air = Mathf.Clamp01(ElementMood.Live(2));
        return _windGate * (0.55f + 0.45f * Lfo(periodSeconds) + 1.0f * air);
    }

    /// <summary>
    /// The one multiply every voice ends up passing through: the player's dial, the hard ceiling,
    /// the duck, and the two volume sliders the player already set inside the GAME's own audio
    /// options. Item 4 of the "never intrusive" list.
    /// </summary>
    private static float Master()
    {
        float dial = Mathf.Clamp(Gain.Value, 0f, 2f) * (MasterCeiling / 2f);
        return dial * _duck * GameVolume();
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

    // ---- the events -------------------------------------------------------------------------------

    private static void TickEvents(SkyStyle style, float clock)
    {
        if (style == SkyStyle.Cellar)
        {
            TickDrip(clock);
            TickRat(clock);
        }
        TickDeferred(clock);
        // NOTHING ANSWERS ICE. There was a TickFrost here until ModBuild 149; see the ruling block
        // in EnvSound.Bank.cs for why the ice sound is deleted rather than silenced.
        TickHaunt(style, clock);
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
        float shifted = clock - DripImpactSeconds;
        if (shifted < 0f)
            return;
        long idx = (long)Mathf.Floor(shifted / DripPeriod);
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
        if (clock < 0f)
            return;
        long slot = (long)Mathf.Floor(clock / RatPeriod);

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

    /// <summary>
    /// THE APPARITIONS. One cue per event, resolved from <see cref="Haunt.Resolve"/> — the shared
    /// schedule, not a timer — so the cue belongs to the apparition on every client at once, and a
    /// FORCED event from the Advanced-menu test buttons makes its sound too (the user will judge
    /// this feature by pressing those buttons).
    ///
    /// <para><b>THE CUES ARE NOT LOCKED TO THE VISUAL, and that is the design.</b> Each card carries
    /// a LEAD: a negative one puts the sound before the apparition, a positive one after. A cue that
    /// lands exactly on the reveal announces the event and converts a doubt into a fact; one that
    /// arrives a moment early makes the player look, and one that arrives after makes them doubt
    /// what they just saw. Both serve "something you are not sure you saw", which is what these
    /// apparitions are built to be. No stingers, no impacts, nothing with a fast attack — see
    /// <see cref="EnvSoundBank"/>'s note on why the frightening sound is never the loud one.</para>
    /// </summary>
    private static void TickHaunt(SkyStyle style, float clock)
    {
        // The bookshelf's own contacts — its arrival on the floor, the rebound, and the righting —
        // fire from here, BEFORE the gate below, so a schedule that was written while the switch
        // was on completes even if the player turns the easter eggs off mid-event. An apparition
        // that is already lying on the floor has to be allowed to get up again.
        TickDeferredCues(clock);

        // THE LATCH OVERRIDES THE SWITCH, and until ModBuild 148 this line did not know that. The
        // Advanced-menu test buttons latch one apparition on regardless of `EasterEggs`
        // (Haunt.Force: "IT OVERRIDES EasterEggs FOR AS LONG AS IT STANDS"), and Haunt.Resolve
        // returns a forced slot as Live whatever the setting says — so a tester with the setting
        // off, which is precisely the tester who is using the buttons to decide whether to turn it
        // on, saw the apparition and heard NOTHING. Pre-existing, cheap, and fixed here rather than
        // filed: `Forcing` is one field read on a path that already reads one.
        //
        // The gate is still worth having with the latch folded in: with the setting off and nothing
        // latched, Resolve would return Live = false anyway (it multiplies by a master of 0), so
        // this is purely the cheap path — one bool read instead of a slot resolution per frame.
        if (!Haunt.EasterEggs.Value && !Haunt.Forcing)
            return;

        Haunt.Slot slot = Haunt.Resolve(clock, style);
        if (!slot.Live)
            return;

        bool audible = CueFor(style, slot.Card, out EnvSoundClip clip, out float gain,
                              out float lead, out float minM, out float maxM);

        float at = slot.StartClock + lead;
        if (clock < at)
            return;

        // Fire once per (start, card). Both are needed: a forced event keeps the same start for its
        // whole hold, and two different cards can share a start only across a clock jump.
        if (Mathf.Approximately(_lastHauntStart, slot.StartClock) && _lastHauntCard == slot.Card)
            return;
        // ...and never fire for an event that has already finished — which is what would otherwise
        // happen on the frame the environment stands up in the middle of a slot.
        float runs = Haunt.CardSeconds(style, slot.Card) * slot.DurationMul;
        if (clock > slot.StartClock + runs + 1.5f)
            return;

        _lastHauntStart = slot.StartClock;
        _lastHauntCard = slot.Card;

        // A SILENT CARD IS DEBOUNCED LIKE ANY OTHER and then simply makes no sound — the two lines
        // above have already run. Doing it in that order rather than returning early is what keeps
        // one apparition equal to at most one visit to this code, so a silent card cannot re-enter
        // on the next frame and cannot schedule anything twice.
        if (!audible)
            return;

        Vector3 pos = HauntPosition(slot.Card);
        PlayShot(EnvSoundBank.Bank(clip), pos, gain, minM, maxM, 1f);

        // The cellar's bookshelf (card 5) is the one apparition whose sound is not a hint but a
        // physical consequence — it tips over, hits a stone floor and later rights itself.
        if (style == SkyStyle.Cellar && slot.Card == 5)
            ScheduleShelfContacts(slot.StartClock, runs, clock, pos);

        VRLog.Info("Core", $"ENV SOUND haunt cue: {style} card {slot.Card} -> {clip} at shared clock " +
                           $"{clock:F2}s, lead {lead:+0.00;-0.00}s on an event that starts " +
                           $"{slot.StartClock:F2}s and runs {runs:F2}s" +
                           (slot.Forced ? " — FORCED from the Advanced menu" : " — scheduled") +
                           $". Gain {gain:F3} before master; position {pos:F2}.");
    }

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
    /// <param name="pos">Where the apparition IS, from <see cref="HauntPosition"/> — the renderer
    /// bounds centre of whatever node resolved. The contacts are placed on the FLOOR under it rather
    /// than at it; see <see cref="ShelfFloorContact"/>.</param>
    private static void ScheduleShelfContacts(float start, float runs, float now, Vector3 pos)
    {
        float arrival = start + runs * ShelfArrivalPhase;
        float rebound = start + runs * ShelfReboundPhase;
        float rise = start + runs * ShelfRisePhase;

        // THE SOUND COMES FROM THE FLOOR, not from the middle of the bookcase. See below.
        Vector3 floor = ShelfFloorContact(pos);

        // THE BANG. Everything about this call is the user's exception being spent: the gain is
        // ShelfImpactGain rather than a fraction of MaxEmitterGain, the ceiling passed to PlayShot is
        // ShelfImpactCeiling rather than MaxEmitterGain, and the cue is LABELLED so that the next
        // hardware round can read the played level and the clip's measured peak straight out of
        // Player.log instead of inferring them.
        //
        // minMeters comes DOWN from 2 to 1.2 as well, and that is a level change in disguise: under
        // logarithmic rolloff the level past the minimum goes as min/d, so a lower minimum steepens
        // the whole curve — which for a bang is the right shape, since a bookcase hitting a stone
        // floor is emphatically a thing that happens in ONE PLACE. maxMeters stays at 24: the
        // permission is for the impact to be loud where it is, not for it to reach further.
        Defer(arrival, EnvSoundClip.Fall, floor,
              gain: ShelfImpactGain, minMeters: 1.2f, maxMeters: 24f, pitch: 1f,
              cap: ShelfImpactCeiling, label: "SHELF ARRIVAL ON THE FLOOR (the loud one)");

        // Pitched slightly UP, because a lighter contact of the same body excites its higher modes
        // relatively more — the low mode needs momentum the rebound no longer has. 1.06 is small
        // enough not to read as a transposition and large enough to read as a lighter touch. It
        // rides the same exception because it is the same contact 0.624 s later — 0.18 of it, i.e.
        // 0.099, which is under MaxEmitterGain anyway and passes the ceiling only for consistency.
        Defer(rebound, EnvSoundClip.Fall, floor,
              gain: ShelfImpactGain * ShelfReboundLevel, minMeters: 1.2f, maxMeters: 24f,
              pitch: 1.06f, cap: ShelfImpactCeiling, label: "SHELF REBOUND (second contact)");

        // The righting is NOT part of the exception: it is a mass coming slowly back up, the half
        // that is meant to be unsettling rather than loud, and it stays inside the ordinary budget.
        Defer(rise, EnvSoundClip.Settle, floor,
              gain: 0.055f, minMeters: 2f, maxMeters: 24f, pitch: 1f,
              label: "SHELF RIGHTING");

        // ONE LINE, ONCE PER SHELF EVENT — which is at most once every ~83 s in the cellar and only
        // for one card in six. It earns its place because the defect it replaces was INVISIBLE from
        // a log: the old cue fired, logged, and sounded, and nothing anywhere said that the thud
        // inside it had landed 3.7 s before the shelf did. These four times against the picture are
        // the whole verification, and nobody working on this can hear it. Each of the three cues
        // ALSO logs when it actually fires or is dropped (see Deferred.Label), so a missing bang can
        // now be attributed rather than guessed at.
        HauntPosition(5, out _, out string via);
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
                           + "could not hear. PLACED ON THE FLOOR at "
                           + $"{floor:F2} (apparition node '{via}', bounds centre {pos:F2}) — an "
                           + "impact sounds from where two things touch, not from the middle of the "
                           + "carcass.");
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
                default: clip = EnvSoundClip.Creak; gain = 0.045f; lead = ShelfLead; minM = 2f; maxM = 20f; return true;
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
    /// </summary>
    private static Vector3 HauntPosition(int card) => HauntPosition(card, out _, out _);

    /// <summary>As above, and it also hands back the FLOOR under the apparition and the name of the
    /// node that resolved — the two things the bookshelf's contacts need and the ordinary cues do
    /// not. See <see cref="ShelfFloorContact"/> for why a contact wants a different point from a
    /// breath.</summary>
    private static Vector3 HauntPosition(int card, out float floorY, out string via)
    {
        Transform room = _root!.transform.parent!;

        Transform? exact = Find(room, "Haunt" + card);
        if (exact != null)
            return Center(exact, "Haunt" + card, out floorY, out via);

        foreach (Transform t in FindByPrefix(room, "Haunt"))
            return Center(t, t.name, out floorY, out via);

        return Center(room, room.name + " (ROOM ROOT — no apparition node resolved)", out floorY, out via);

        // The RENDERER's bounds centre, not the transform origin: a welded apparition card's pivot
        // is wherever the mesh builder happened to leave it, while the bounds centre is where the
        // thing visibly IS. Falls back to the transform when there is nothing to measure — and then
        // the floor is the transform too, which is the honest answer for "we could not measure it".
        static Vector3 Center(Transform t, string name, out float floor, out string resolved)
        {
            resolved = name;
            var r = t.GetComponentInChildren<Renderer>();
            if (r == null)
            {
                floor = t.position.y;
                return t.position;
            }
            floor = r.bounds.min.y;
            return r.bounds.center;
        }
    }

    /// <summary>
    /// Where the bookshelf's contacts SOUND FROM: the point directly under the apparition's centre,
    /// on the floor.
    ///
    /// <para><b>WHY NOT SIMPLY THE APPARITION'S CENTRE, which is what every other cue uses.</b> A
    /// breath, a creak and a drag come from a whole body and the bounds centre is the right answer
    /// for all of them. An IMPACT does not: it happens at the one place two things touch, and for a
    /// bookcase going over that is the floor line, a metre or more below the carcass's middle. At
    /// this room's rig scale that is tens of world units of separation, and the user's report is
    /// specifically that the sound of the shelf reaching the floor is missing — placing it in the
    /// air above the floor is a way of getting it half right.</para>
    ///
    /// <para><b>AND THE FALLBACK IS HONEST ABOUT ITSELF.</b> The hardware log shows card 5 resolving
    /// through the second link of <see cref="HauntPosition"/>'s chain, not the first — there is no
    /// <c>Haunt5</c> node, so the position is the welded CATALOGUE's bounds, which covers every
    /// apparition in the room. Its bottom is still the floor those apparitions stand on, which is
    /// what this needs; its x/z is the catalogue's centre rather than the shelf's, which this cannot
    /// fix from here. The scheduling log line prints the node that resolved and the final point, so
    /// the day the content lane names a per-card node this improves without a code change and the
    /// log says that it did.</para>
    /// </summary>
    private static Vector3 ShelfFloorContact(Vector3 centre)
    {
        if (_root == null || _root.transform.parent == null)
            return centre;

        Vector3 body = HauntPosition(5, out float floorY, out _);
        // Guard the measurement rather than trusting it: a renderer with no mesh reports a degenerate
        // bounds, and a floor ABOVE the body's own centre is not a floor. Fall back to the centre,
        // which is where the cue used to be and is never worse than silence.
        if (float.IsNaN(floorY) || float.IsInfinity(floorY) || floorY > body.y)
            return centre;
        return new Vector3(body.x, floorY, body.z);
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
    /// <returns>The volume actually written to the source on Unity's 0..1 scale — gain, clamped,
    /// times the master — or 0 if nothing played. RETURNED rather than recomputed by the caller
    /// because a log line that states a level nobody set is worse than no log line: this is the
    /// number the headset was handed.</returns>
    private static float PlayShot(AudioClip? clip, Vector3 world, float gain,
                                  float minMeters, float maxMeters, float pitch,
                                  float cap = MaxEmitterGain)
    {
        if (clip == null || Shots.Count == 0)
            return 0f;

        Voice v = Shots[_nextShot];
        _nextShot = (_nextShot + 1) % Shots.Count;

        float volume = Mathf.Min(gain, cap) * Master();
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
        // ALREADY OURS ⇒ NOTHING TO DO, and this guard is load-bearing rather than defensive.
        // A style change runs Build, which tears the old environment down and builds the new one IN
        // THE SAME FRAME. If that teardown destroyed our listener, this method would then find the
        // doomed component with GetComponent (Object.Destroy is deferred to the end of the frame),
        // re-enable it, and Unity would delete it moments later — leaving the session with NO
        // enabled listener at all and the whole feature inaudible until the next full teardown.
        // Keeping ownership across a rebuild also keeps _suppressed intact, which is the ONLY
        // record of which listeners have to be handed back.
        if (_ourListener != null)
            return;

        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
            return;

        // Disable every listener that is currently enabled, remembering exactly which ones so
        // StandDown can restore precisely those and nothing else. There can legitimately be more
        // than one in a multiplayer session: the voice-chat player prefab carries its own and
        // enables it for the local owner (GH.Runtime/VoiceChat/BoltVoicePlayerController.cs:18).
        _suppressed.Clear();
        foreach (AudioListener l in Object.FindObjectsOfType<AudioListener>())
        {
            if (l == null || !l.enabled)
                continue;
            l.enabled = false;
            _suppressed.Add(l);
        }

        _ourListener = head.gameObject.GetComponent<AudioListener>();
        if (_ourListener == null)
            _ourListener = head.gameObject.AddComponent<AudioListener>();
        _ourListener.enabled = true;
    }

    private static void ReleaseListener()
    {
        if (_ourListener != null)
        {
            Object.Destroy(_ourListener);
            _ourListener = null;
        }

        for (int i = 0; i < _suppressed.Count; i++)
        {
            AudioListener l = _suppressed[i];
            // Unity fake-null when the scene that owned it unloaded; then there is nothing to
            // restore and the scene took the state with it.
            if (l != null)
                l.enabled = true;
        }
        _suppressed.Clear();
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
        if (!_built && _root == null && _ourListener == null && _suppressed.Count == 0)
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

        _built = false;
        _builtStyle = SkyStyle.Default;
        _builtScale = 1f;
        _duck = 1f;
        _gameAudible = false;
        _duckPollCountdown = 0;
        _lastDripIndex = long.MinValue;
        _lastRatSlot = long.MinValue;
        _lastHauntStart = float.NaN;
        _lastHauntCard = -1;
        ClearDeferred();
        _squeakAt = float.NaN;
        _squeakFrom = null;
        // The wind gate goes back to SHUT rather than to its live value: the next environment must
        // fade its wind in from nothing exactly as the first one did, or a stand-down and rebuild
        // during an Air infusion would start the new room's bed at full level on its first frame.
        _windGate = 0f;
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
                              float cap = MaxEmitterGain, string? label = null)
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
                                    cue.MinMeters, cue.MaxMeters, cue.Pitch, cue.Cap);
            if (cue.Label == null)
                continue;

            // THE LINE THAT SETTLES IT FROM A LOG. Everything a listener's "I heard nothing" has to
            // be checked against: that it fired at all, what the source was actually set to, and
            // what the clip itself contains — measured off the finished buffer on the DEVICE, at the
            // device's own sample rate, not quoted from a doc comment (EnvSoundBank.MeasuredShape).
            EnvSoundBank.MeasuredShape(clip, out float peak, out float peakAt);
            VRLog.Info("Core", $"ENV SOUND {cue.Label} FIRED — {cue.Clip} at shared clock " +
                               $"{clock:F2}s (due {cue.At:F2}s, {(clock - cue.At) * 1000f:F0} ms late). " +
                               $"GAIN {cue.Gain:F3} before master, ceiling {cue.Cap:F2}, master " +
                               $"{Master():F3} (duck {_duck:F2}, dial {Gain.Value:F2}, game volume " +
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
            sb.Append('[').Append(v.Name).Append(" on '").Append(v.NodeName).Append("' gain ")
              .Append(v.BaseGain.ToString("F3")).Append(", ")
              .Append(v.MinMeters.ToString("F1")).Append("..").Append(v.MaxMeters.ToString("F1"))
              .Append(" m perceived = ")
              .Append((v.MinMeters * rigScale).ToString("F0")).Append("..")
              .Append((v.MaxMeters * rigScale).ToString("F0")).Append(" world] ");
        }

        sb.Append("SCALE: rigScale ").Append(rigScale.ToString("F2"))
          .Append(" world units per perceived metre; every rolloff above is metres x that factor, ")
          .Append("recomputed whenever zoom moves it. LISTENER: ")
          .Append(_ourListener != null
                      ? "moved onto GloomhavenVR.HeadCamera"
                      : "NOT taken — no head camera, so nothing here will be audible")
          .Append(", ").Append(_suppressed.Count)
          .Append(" pre-existing listener(s) disabled and remembered for restore. ")
          .Append("LEVELS: every source is capped at ").Append(MaxEmitterGain.ToString("F2"))
          .Append(", the dial can reach at most ").Append(MasterCeiling.ToString("F2"))
          .Append("x, the whole ambience ducks to ").Append(DuckFloor.ToString("F2"))
          .Append(" while the game is making any sound, and the player's own master and effects ")
          .Append("volumes multiply on top. WIND: the draught/leaves bed is GATED ON THE AIR ")
          .Append("element (user: \"Wind Geräusch nur wenn auch Wind aktiv ist\") — it opens across ")
          .Append(AirGateOn.ToString("F2")).Append("..").Append(AirGateFull.ToString("F2"))
          .Append(" of Air over ").Append(AirGateOpenSeconds.ToString("F1"))
          .Append("s and closes over ").Append(AirGateCloseSeconds.ToString("F1"))
          .Append("s, and while it is shut the source is PAUSED, not merely silent — so a 'Draught' ")
          .Append("or 'Leaves' line above with no audible wind under it is the gate working, not a ")
          .Append("missing sound. The Earth rumble is paused the same way whenever no Earth is up. ")
          .Append("CLOCK: every event reads SkyAlternative.EnvClockSeconds, ")
          .Append("so the drip, the rat and the haunt cues land on the same frame on every client ")
          .Append("with ZERO wire bytes.");

        VRLog.Info("Core", sb.ToString());
    }
}
