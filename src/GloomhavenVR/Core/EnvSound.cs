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
/// <para><b>THE ROOM ITSELF: ASKED FOR AT ModBuild 154, ANSWERED WITH TWO CONTINUOUS BEDS, AND THE
/// BEDS ARE NOW DELETED ON THE USER'S OWN RULING.</b> The request was "In Szenarios gibt es immer die
/// dortigen Hintergrundgeräusche deswegen ist mir die Stille vorher nie aufgefallen. Im Keller
/// scheint es constant geräusche zu geben, im Wald hingegen ist es absolut still. Ich will für beide
/// eine dezente Hintergrundgeräuschkullise die zu der Umgebung passt. Diese soll deaktivierbar sein."
/// Every sound in the request at the top of this doc is a THING HAPPENING IN A PLACE, so with nothing
/// happening the environment had no sound at all — and ModBuild 154 answered that with a bed
/// belonging to no object, 221 and 222 rebuilt it twice, and ModBuild 223 removes the whole approach
/// because he rejected it in those terms. <b>THE VERDICT AND THE NEW RULE ARE IN THE ROOM TONES,
/// DELETED, WITH BOTH GERMAN SENTENCES VERBATIM AND THE NUMBERS. Read that block before adding
/// anything continuous to this file.</b></para>
///
/// <para><b>...AND NOTHING SURVIVES OF IT AT ALL AS OF ModBuild 226.</b> This paragraph used to end
/// by saying that what remained from 154 was the wood's insect bed, repaired and turned into an
/// intermittent chorus. The user has now named that emitter too — "Im Wald gefällt mir nur dieser
/// 'Regen' Sound nicht der ab und zu kommt und für eine Zeit bleibt, ansonsten finde ich es sehr
/// gut" — and it is deleted with its clip. So the answer to "eine dezente
/// Hintergrundgeräuschkullise" is now, in both rooms and without exception, THINGS HAPPENING IN
/// PLACES: a draught with a source, animal calls from a tree, a drip, a rat, three candle flames,
/// and whatever the elements bring. See THE INSECT CHORUS, DELETED for the schedule that convicted
/// it, the rain control it was measured against and the alternatives that were falsified.</para>
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
/// <item><b>Almost nothing is continuous at all, since ModBuild 223 — and since 226 the list is
/// exhaustive.</b> The user's ruling is "Statt generrell durchgehende sounds zu machen lieber die
/// Tierrufe" — see THE ROOM TONES, DELETED. What plays at rest is the resting DRAUGHT (which he
/// asked for by name) and, in the cellar, the three candle flames. THAT IS THE WHOLE LIST: the
/// forest's insect chorus was the last other continuous emitter and it is deleted (THE INSECT
/// CHORUS, DELETED). Everything else is an event from a place. A sound the ear cannot adapt to and
/// then be irritated by is one that is not always there.</item>
/// <item><b>Spectral separation.</b> The continuous beds are noise held under the speech band —
/// the wind and the Earth rumble by a runtime filter at <see cref="BedLowPassHz"/>,
/// and the fire's roar and the candles' flutter by BAKED bands that are tighter still (three poles
/// at 820 Hz and four at 1050 against this filter's single pole at 1150; see
/// <c>EnvSoundBank</c>'s THE FIRE and THE CANDLE, and <see cref="AddBed"/>'s <c>lowPassHz</c> for
/// why those two turn the runtime filter off rather than adding to it). This is the one "never
/// mask" measure that had to be chosen on
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
/// <para><b>MULTIPLAYER — EVERY SCHEDULED SOUND IN THIS FILE IS THE SAME SOUND, IN THE SAME PLACE,
/// ON THE SAME FRAME, FOR EVERY PLAYER IN THE SAME ROOM. Zero wire bytes, and that is a conclusion
/// rather than an omission.</b></para>
///
/// <para>USER RULING, hardware on ModBuild 225, verbatim — it is the acceptance criterion for this
/// section and it carves out no exceptions: "Genau wie die Easter-Eggs sollen auch die Sounds mit
/// allen Mitspieler synchronisiert sein die in der selben Map sind. Sind also zwei Spieler in der
/// Wald Umgebung und dort kommt ein Geräusch eines Tieres aus einer Ecke sollen alle Spieler die
/// auch im Wald sind zur selben Zeit aus der selben Location denselben Sound hören."</para>
///
/// <para><b>HOW IT IS ANSWERED, AND WHY IT IS NOT A PACKET.</b> Audio is local — there is nothing to
/// replicate about a sound a headset makes. What has to agree between clients is the EVENT the sound
/// marks, and <c>NetProtocol.ExtIdEnvClock</c> (record 31) already publishes one thing that makes
/// every event agree: a shared epoch, whose owner is elected by lowest player id among everyone
/// showing the same style, read here as <see cref="SkyAlternative.EnvClockSeconds"/>. An event that
/// is a PURE FUNCTION of that number is heard identically everywhere with nothing added to the
/// packet — which is exactly why the apparitions the user is comparing against already agree, and
/// which is the standing project rule ("never open a second network channel for a fact the game or
/// an existing mod record already synchronises"). So the whole of the work is to make every
/// scheduled one-shot such a function, and as of ModBuild 226 every one of them is.</para>
///
/// <para><b>THE AUDIT, PER EVENT CLASS, so the claim above can be checked rather than believed. WHEN
/// it fires and WHERE it comes from are listed separately, because the user's sentence asks for
/// both ("zur selben Zeit aus der selben Location"):</b></para>
/// <list type="table">
/// <item><term>The drip</term><description>WHEN: the period INDEX of the shared clock, off the same
/// <see cref="DripPeriod"/> the shader is baked with; the variant and the pitch are hashes of that
/// index. WHERE: the bake's own 'Drip'/'Puddle' node. <see cref="TickDrip"/>.</description></item>
/// <item><term>The rat and its squeak</term><description>WHEN: the crossing slot, mirrored from
/// <c>EnvCritter.shader</c>'s own schedule; the squeak's delay is a hash of the same slot. WHERE:
/// the 'Rat' node. <see cref="TickRat"/>.</description></item>
/// <item><term>The night calls</term><description>WHEN: the 41 s slot, with a Poisson offset inside
/// it drawn from one hash of the slot. WHERE: a point on a perch ring, its azimuth, radius and
/// height all hashed off the same slot and resolved through the GROUND node's own frame — so the
/// owl is in the same tree for both players even though they sit at different seats around the
/// table. <see cref="TickNightCall"/>, <see cref="NightCallPerch"/>.</description></item>
/// <item><term>The apparitions and the bookshelf's contacts</term><description>WHEN:
/// <see cref="Haunt.Resolve"/>, the same slot the GPU draws; the contacts are ABSOLUTE times on the
/// shared clock derived from that slot's start. WHERE: the apparition's own node.
/// <see cref="TickHaunt"/>, <see cref="ScheduleShelfContacts"/>.</description></item>
/// <item><term>The fire's crackle</term><description>WHEN: a Bernoulli draw per
/// <see cref="FireCrackleTickSeconds"/> tick of the shared clock, keyed on (tick, site), placed at a
/// hashed offset inside its tick. WHERE: the site's own seat, one of the bake's three. <b>THIS IS
/// WHAT ModBuild 226 CHANGED</b> — see <see cref="TickFire"/> for the walk it replaced and what the
/// change cost. It was the last cue here that two clients did not share.</description></item>
/// </list>
///
/// <para><b>AND WHAT IS DELIBERATELY NOT SYNCHRONISED, because the user's sentence is about
/// SOUNDS THAT COME AND GO and these are not events at all.</b> The BEDS — the resting draught, the
/// candle flames, the fires' roar, the Earth rumble — are continuous levels shaped by
/// <see cref="Lfo"/>, which reads <c>Time.time</c>. That is correct and must stay: there is no
/// instant for two clients to disagree about in a level that is always on, the LFO periods are
/// chosen to be non-commensurate precisely so no listener can find a phase in them, and putting
/// them on the shared clock would buy nothing and cost the property that two beds in one room never
/// come into step. The same goes for the DUCK and the two element GATES, which respond to what the
/// LOCAL game is doing and to a smoothing constant. <c>Time.time</c> is used NOWHERE ELSE in this
/// file.</para>
///
/// <para><b>THE POSITIONS NEED NO SPECIAL TREATMENT AND THIS IS WHY.</b> Every source here is
/// parented to the ROOM branch (<see cref="Build"/> sets <c>_root</c>'s parent to
/// <c>roomGo.transform</c>), and the room is world-fixed to the BOARD by
/// <c>SkyAlternative.TryPlaceRoom</c> — not to a head, not to a rig, and it does not follow zoom.
/// So a node's world position is a function of the board, which every client agrees about, and
/// "aus derselben Ecke" is true by construction for anything sounding from a node. The one cue that
/// does NOT sound from a node — the night call, which needs a point that moves — is expressed in
/// the ground node's LOCAL frame for exactly this reason and converted with
/// <c>Transform.TransformPoint</c>, so it never touches world units.</para>
///
/// <para><b>AND THE CLOCK ITSELF JUMPS, which is the one thing being a pure function does not
/// cover.</b> A client that has just joined follows its own clock until the first record arrives and
/// is then moved onto the owner's in one step. Across that step every slot index changes at once —
/// so without a guard the drip, the rat and the night call would all fire on the same frame, and a
/// BACKWARD jump would replay slots this client had already heard. See THE JOIN, above
/// <see cref="TickEvents"/>.</para>
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

    // ---------------------------------------------------------------------------------------------
    //  THERE ARE TWO DIALS AND THERE USED TO BE FOUR. `[EnvSound] AmbienceBed` and
    //  `[EnvSound] AmbienceBedGain` are DELETED at ModBuild 223, with their Defaults lines, their
    //  Loc entries, their curated menu rows and their dependency edge.
    //
    //  WHY, AND IT IS NOT A TIDY-UP. Those two dials existed for exactly one thing: the two
    //  CONTINUOUS ROOM TONES added at ModBuild 154, which this round deletes on the user's ruling
    //  (see THE ROOM TONES, DELETED below for both German sentences verbatim and the measurements).
    //  The switch turned two named sources on and off; the gain multiplied those same two sources and
    //  nothing else. With the sources gone, `AmbienceBed` would be a toggle that toggles nothing and
    //  `AmbienceBedGain` a volume for silence — and the standing instruction for this file's settings
    //  surface is that a dial whose description no longer matches what it does is worse than no dial.
    //
    //  WHAT ANSWERS "Diese soll deaktivierbar sein" NOW. That sentence was written about a room tone,
    //  and the same user has now withdrawn the room tone. What is left playing at rest is the
    //  DRAUGHT, which the SAME REPORT asks for by name ("im Wald ein ganz leiser dezenter Windzug",
    //  "der Windzug im Keller könnte vom Fenster ausgehen") — content he requested rather than
    //  content that needs an escape hatch. `[EnvSound] Enabled` still removes the whole feature and
    //  `[EnvSound] Gain` still scales all of it, which is two dials for a feature this size and is
    //  the direction of travel for the settings surface.
    //
    //  IF HE WANTS THE RESTING DRAUGHT SWITCHABLE, the place is named so nobody has to look: it is
    //  ONE dial multiplying <see cref="WindRestFloor"/> in <see cref="WindBed"/>, and nothing else
    //  would have to change — the Air response is a separate term in the same expression.

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
            + "lands and the bookshelf BANGS on the frame it actually reaches the floor. FIRE "
            + "CRACKLES, and only while a Fire infusion has actually lit it: each burning thing — the "
            + "crates, the casks and the bookcase in the cellar, the snag, the deadfall and the "
            + "brushwood in the wood — gets its own low roar and its own irregular crackle from where "
            + "it is standing, so you can hear which of them is nearest. With no Fire up they are "
            + "silent and cost nothing. THE WIND: a very quiet draught is always there — at the "
            + "cellar window it comes in, in the forest it moves through the canopy — and an Air "
            + "infusion is what makes it RISE into a real wind and then die away again. NOTHING "
            + "ELSE PLAYS CONTINUOUSLY at all: in the forest an owl or a small bird calls now and "
            + "then from a different tree each time, and between the calls there is the draught and "
            + "nothing. ICE MAKES NO SOUND AT ALL — you can see the frost, you "
            + "never hear it. "
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

    /// <summary>The bookshelf's arrival on the floor, before master. 1.10 against the 0.080 this cue
    /// shipped with and the 0.55 of ModBuild 149 — <b>+22.8 dB</b> on the original, <b>+6.0 dB</b> on
    /// the round the user still called "viel zu leise".
    ///
    /// <para><b>WHY 0.55 DID NOT REACH HIM, MEASURED OFF HIS OWN LOG</b> (Player.log:7406, the one
    /// arrival in that session that fired un-ducked). The line reports SOURCE VOLUME 0.264 — and that
    /// is what was written to the source, not what reached the ear. The source stood at
    /// (-43.49, -9.97, 32.05) with a minDistance of 1.2 perceived m = 16.5 world units, and the head
    /// was at (-46.00, 9.30, 12.03) (Heartbeat #20, two lines earlier), i.e. 27.9 world units away.
    /// Unity's logarithmic rolloff is min/d past the minimum, so the SPATIALISER took another
    /// <b>-4.6 dB</b> off it and the bang arrived at 0.156. THE DISTANCE ATE MORE THAN A THIRD OF THE
    /// EXCEPTION, and it is about to eat far more, because fixing the POSITION (see
    /// <see cref="ShelfFloorContact"/>) moves the source from 27.9 to about 100 world units away —
    /// the wrong point happened to be near the player. At the shipped 1.2 m minimum that would have
    /// been 0.044, i.e. quieter than the round he complained about. So the rolloff is fixed in the
    /// same edit; see the <c>minMeters</c> argument in <see cref="ScheduleShelfContacts"/>.</para>
    ///
    /// <para>WHAT IT REACHES NOW, with the rolloff flat across the room: at the DEFAULT dial the
    /// master is 0.75, so the source volume is 0.825 on Unity's 0..1 scale against 1.0 for a game cue
    /// at full level, and the spatialiser no longer takes anything off it anywhere inside the cellar.
    /// At the reporting user's own settings (game volume 0.64, master 0.480) it is 0.528, against the
    /// 0.156 he heard — <b>+10.6 dB</b>. Under the duck it lands at 0.578 / 0.370 rather than at
    /// 0.289 / 0.185, because this cue's duck has a floor of its own
    /// (<see cref="ShelfImpactDuckFloor"/>).</para>
    ///
    /// <para>THE LEVEL IS ONLY HALF OF WHY HE COULD NOT HEAR IT. The other half is the clip, and it
    /// was only half fixed too: ModBuild 149 moved 19.6% of the energy above 1 kHz but left 72.7% of
    /// it BELOW 200 Hz, where a Quest 3 speaker gives nothing back, and only 7.6% in the 1-5 kHz band
    /// where the speaker and the ear are both at their best. That is measured, not assumed, and so is
    /// the rebuild that fixes it; see <c>EnvSoundBank.MakeFall</c>.</para></summary>
    private const float ShelfImpactGain = 1.10f;

    /// <summary>...and the ceiling the exception is clamped against, in place of
    /// <see cref="MaxEmitterGain"/> and for this cue only. 1.20 leaves the constant above a little
    /// room to be tuned upward on hardware without a second edit here.
    ///
    /// <para>PAST 1.0 IS NOT PAST THE GAME, and the difference matters. The number this ceiling
    /// bounds is a gain BEFORE the master, and the master at the shipped dial is 0.75 — so a gain of
    /// 1.20 is a source volume of 0.90, still under a game cue at full level. Only a player who has
    /// also pushed the environment dial past 1.33 can drive the product past 1.0, and Unity clamps
    /// <c>AudioSource.volume</c> there; the dial simply stops getting louder. That is the correct
    /// end for a dial the player chose to turn up, and it is stated here rather than discovered.</para></summary>
    private const float ShelfImpactCeiling = 1.20f;

    /// <summary>How far this ONE cue is allowed to duck. The ambience as a whole falls to
    /// <see cref="DuckFloor"/> = 0.35 while the game is making any sound; the bang falls no further
    /// than 0.70.
    ///
    /// <para><b>WHY THE EXCEPTION NEEDS THIS TO MEAN ANYTHING.</b> In the user's own session THREE of
    /// the five arrivals that fired were ducked (Player.log:8847, :8942 — "duck 0.35"), so the cue he
    /// was asked to judge was 9 dB down more often than it was not. The duck exists so that a
    /// CONTINUOUS BED does not sit under the game's speech and cues; the argument does not transfer
    /// to a 40 ms transient, which is the class doc's own duration argument applied to the one place
    /// it is strongest. It still ducks — the permission is to be loud, not to be unstoppable — it
    /// simply cannot be ducked into inaudibility.</para>
    ///
    /// <para>SCOPE: the arrival and its rebound only. The righting is not part of the exception and
    /// ducks the whole way, like everything else in the feature.</para></summary>
    private const float ShelfImpactDuckFloor = 0.70f;

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

    /// <summary>Hard cap on live sources. It exists so that no future addition can quietly turn the
    /// ambience into a crowd.
    ///
    /// <para><b>12 -&gt; 14, and the reason is that the cap had become a trip wire rather than a
    /// budget.</b> The cellar's draw is now 8 continuous sources (three candle groups, THREE FIRE
    /// SITES, the draught, the Earth rumble) plus the 3 one-shot voices = 11; the swamp's is 6 + 3 =
    /// 9. A cap one above the current draw does not bound anything — it silently deletes the next
    /// legitimate emitter somebody adds. Two spare is a budget; and the ACTUAL protection against a
    /// crowd was never this number but <see cref="MaxEmitterGain"/>, the duck and
    /// <see cref="AudioSource.priority"/> 200, all three of which are unchanged.</para>
    ///
    /// <para><b>ModBuild 154 SPENT ONE OF THE TWO SPARE ON A ROOM TONE PER ROOM; ModBuild 223 GIVES
    /// IT BACK, AND THE CAP IS STILL NOT MOVED.</b> With the two room tones deleted the cellar is
    /// back to 8 beds + 3 one-shot voices = <b>11 of 14</b> and the wood to 6 + 3 = 9. That is the
    /// second half of "raise it WITH A REASON, or take an existing bed out" actually happening: the
    /// bed came out, so the number did not have to move in either direction. The file's own test is
    /// <c>Beds.Count + OneShotVoices &gt;= MaxVoices</c>, so there is room for two more emitters in
    /// the cellar before <see cref="AddBed"/> starts refusing.</para>
    ///
    /// <para>AND A REFUSAL IS NOW LOGGED. Until ModBuild 152 <see cref="AddBed"/> returned in silence
    /// when the cap was reached, which is the same silent path a missing node takes — the exact class
    /// of defect that let a bed looking for "Wisp" against a node called "WispWisp" survive for
    /// months. See <see cref="AddBed"/>.</para></summary>
    private const int MaxVoices = 14;

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
    // ==================== THAT RULING IS NARROWED AT ModBuild 223, BY THE SAME USER ================
    //
    //  2026-08-22 hardware, verbatim, and BOTH clauses are the whole of the change:
    //
    //      "Statt generrell durchgehende sounds zu machen lieber die Tierrufe und im Wald ein ganz
    //       leiser dezenter Windzug. ... zB der Windzug im Keller könnte vom Fenster ausgehen (aber
    //       auch hier nur dezent!)"
    //
    //  HOW THIS ROUND READS IT, WRITTEN OUT SO IT CAN BE CORRECTED IN ONE SENTENCE IF IT IS READ
    //  WRONG. He is asking for a VERY QUIET DRAUGHT THAT IS ALWAYS THERE, in both rooms — in the
    //  forest as the thing that replaces the deleted bed, and in the cellar as the window's own
    //  sound — and the 147 ruling is not withdrawn but NARROWED: the Air infusion is no longer the
    //  difference between silence and wind, it is the RISE above a resting whisper. So:
    //
    //      * ONE resting floor (WindRestFloor), on the EXISTING wind beds and not on a second
    //        emitter, because "der Windzug im Keller könnte VOM FENSTER ausgehen" names the node
    //        'Draught' already stands on. A second quiet-air source would be the room tone again
    //        under another name, which is exactly what this round deleted.
    //      * The Air response is UNTOUCHED in shape and, measured, within 0.1 dB of what it was at
    //        full infusion — see WindBed, where the floor and the rise are two terms of one
    //        expression and the sum still tops out at exactly 2.
    //      * The floor is FLAT. It does not gust, swell or breathe. A resting bed that swelled would
    //        be "a wind with no wind", which is the thing the 147 ruling is actually about and which
    //        EnvSoundBank's own reject list turned down for the room tones one round ago.
    //
    //  IF HE MEANT SOMETHING NARROWER — the draught at rest in the CELLAR only, say, or only while
    //  the player is near the window — the change is one constant and one branch in WindBed, and the
    //  gate below is untouched either way.
    // ==============================================================================================
    //
    // THE DISTINCTION THE 147 REPORT DRAWS IS EXACT AND IT SPLITS ACROSS TWO LANES. The resting sway
    // of the leaves and the lean of the candle flames are the SHADERS' — EnvRoom/EnvFlame do that
    // from their own time uniforms and they are untouched by anything here. What he was switching
    // off is the AUDIO bed, which this file owns. So this is a gate on two emitters and on nothing
    // else: the cellar's "Draught" at the window and the swamp's "Leaves" in the canopy. The candle
    // flames and the Earth rumble are not wind and do not move. (The swamp's insect chorus was the
    // third emitter this sentence used to exclude; it is deleted at ModBuild 226 — see THE INSECT
    // CHORUS, DELETED.)
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

    /// <summary>Air intensity at which the wind bed starts to RISE, and the intensity at
    /// which it is fully open. Below <see cref="AirGateOn"/> the bed sits at
    /// <see cref="WindRestFloor"/> and nothing above it — which was "no wind sound whatsoever" until
    /// ModBuild 223 narrowed the ruling; see the block above. The span up to
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

    // ---- the candles -----------------------------------------------------------------------------
    //
    // USER REPORT, ModBuild 152 hardware, verbatim: "Beim Feuer Geräusch ist auch immer das Wind
    // geräusch mit dabei. Das soll nicht sein. Das Wind gEräusch soll nur dann kommen wenn Wind auch
    // aktiv ist."
    //
    // IT IS THE 147 RULING RESTATED, and he had to restate it because the round that answered it
    // answered only two thirds of it. THE WIND GATE above put Draught and Leaves behind _windGate
    // and stopped there; the cellar's three candle beds were left playing EnvSoundClip.Bed — THE
    // WIND BUFFER — with no gate at all and with `+ 0.9f * ElementMood.Live(0)` in their gain. So:
    //
    //   * the wind clip was audible in the cellar with Air fully off, which is the 147 ruling broken
    //     on its face; and
    //   * infusing FIRE made the WIND CLIP louder, by +6.2 dB on each of the room's three candle
    //     beds, which is the 152 report word for word.
    //
    // ModBuild 152 even wrote the diagnosis down — the comment above AddFireBeds says "until
    // ModBuild 152 the only thing that answered a Fire infusion here was the candle beds' gain — on
    // a clip that is the window draught" — added the proper Roar/Crackle/Ember beside it, and LEFT
    // THE CANDLE BEDS ON THE WIND CLIP. A diagnosis in a comment is not a fix.
    //
    // WHAT THE FIX IS, AND WHAT IT IS NOT. It is not "gate the candles on Air": a candle is not
    // wind, and gating it would have made the cellar silent rather than correct. It is a CLIP OF ITS
    // OWN — EnvSoundClip.Flutter, whose band, envelope and measurements are in EnvSoundBank's THE
    // CANDLE — plus the three placement changes in BuildCellar: the runtime low pass off (the band
    // is baked), the Fire term cut to a flare, and the group count logged.
    //
    // WHY THE CANDLES STILL ANSWER FIRE AT ALL, since removing the term entirely would have been the
    // simpler edit. Because the PICTURE does: EnvFlame.shader scales a candle's flame by
    // (1 + 0.55 * e.fire) in height and (1 + 1.05 * e.fire) in brightness, and its own comment names
    // that as the ModBuild 142 design ("Die Kerzen flackern auf, wenn der Raum infundiert ist") and
    // as the one candle response no later ruling touches. A sound that ignored a flame visibly
    // doubling in brightness would be the mirror image of this round's bug: the picture and the
    // sound disagreeing about the same object.

    /// <summary>The candle bed's gain. UNCHANGED at 0.055 across the rebuild, deliberately: the clip
    /// under it is new and the level must not be, or the next hardware report cannot say which of
    /// the two it is judging. The bank normalises the flutter so that this gain lands 1.76 dB under
    /// what the wind buffer gave the same emitter — measured, and erring quiet.</summary>
    private const float CandleBedGain = 0.055f;

    /// <summary>
    /// How much a full Fire infusion lifts the candle bed, on top of a resting 0.72..1.00.
    ///
    /// <para><b>0.90 -> 0.30, which is +6.2 dB -> +2.6 dB.</b> The old coefficient did not come from
    /// the candles at all: until ModBuild 152 these beds were the ONLY thing in the room that
    /// answered a Fire infusion, so the term was carrying the whole "the fire got louder" job on a
    /// clip that was a draught. The six seated fires now carry that job themselves, with their own
    /// clip, their own gate and their own place (see THE FIRE), and what is left for the candles is
    /// what the shader actually does to them: a FLARE. A candle that doubled in loudness under Fire
    /// while a burning crate three metres away was doing the same thing would put two fires in the
    /// room where the picture has one.</para>
    ///
    /// <para>NOT ZERO, and that is a decision rather than a leftover — see the block above for why
    /// the picture requires it. It is also the reason this is a named constant: the next reader who
    /// wants the candles inert under Fire has to disagree with EnvFlame.shader, not with a
    /// literal.</para></summary>
    private const float CandleFireLift = 0.30f;

    /// <summary>How many "Candles*" groups the room actually gave us, so <see cref="LogBuilt"/> can
    /// say. Zero in a cellar is not impossible but it IS a report: it means the bake renamed
    /// CandleGroup, and a bed that never resolves is silent in exactly the way a room with no
    /// candles is. That ambiguity cost this feature the swamp's wisp bed for months.</summary>
    private static int _candleGroups;

    /// <summary>
    /// A candle group's bed level, 0..2. NOT GATED ON AIR and that is correct: a candle flame is not
    /// wind, it is a small flame that is there whether or not the air is moving, and gating it would
    /// have answered the user's report by deleting a sound he never complained about. What answers
    /// his report is that this bed no longer plays the WIND CLIP — see
    /// <see cref="EnvSoundClip.Flutter"/> — and that the Fire term below is a flare rather than the
    /// room's entire fire response.
    ///
    /// <para>It is a separate function from <see cref="WindBed"/> and <see cref="FireBed"/> rather
    /// than a lambda in the room builder for the reason those two are: a level that lives in one
    /// named place can be asserted about, and the wire vectors do exactly that (no element state may
    /// make this a function of <c>_windGate</c>, and none may make <see cref="WindBed"/> a function
    /// of Fire).</para>
    ///
    /// <para>NEVER RETURNS ZERO, unlike the other two, and the difference is worth stating because
    /// <see cref="TickBeds"/> PAUSES a source that reaches zero: the candles are alight for the whole
    /// scenario, so this bed plays for the whole scenario. Its floor is 0.72.</para>
    /// </summary>
    /// <param name="periodSeconds">This group's slow LFO period. The three groups' periods are
    /// non-commensurate with each other and with the 7 s flutter buffer, so three candle beds in one
    /// room never come into phase — item 6 of the class doc, and the same treatment the three fire
    /// sites get.</param>
    private static float CandleBed(float periodSeconds) =>
        0.72f + 0.28f * Lfo(periodSeconds) + CandleFireLift * Mathf.Clamp01(ElementMood.Live(0));

    // =================================================================================================
    //  THE ROOM TONES, DELETED — ModBuild 223, and this is a DESIGN REVERSAL rather than a tuning.
    // =================================================================================================
    //
    //  WHAT WAS HERE. ModBuild 154 added two CONTINUOUS ROOM TONES — 'Stone' in the cellar and
    //  'NightAir' in the wood — each one source at the ROOM ROOT, at 180 degrees of spread, on a
    //  rolloff flat across the whole playable volume, deliberately NOT locatable, playing for the
    //  whole scenario with no element up. ModBuild 221 shipped them; 222 rebuilt both clips, split
    //  their gains and raised the cellar's hiss ceiling from a mix of 0.14 to 0.55. They had two
    //  dials of their own, `[EnvSound] AmbienceBed` and `AmbienceBedGain`.
    //
    //  THE RULING, 2026-08-22 hardware, verbatim, and it is two sentences because it is two rooms:
    //
    //      "2) Im Keller hören sich die Geräusche an wie Rauschen bei nem Fernseher. Es soll dezenter
    //       sein nicht aufdringliuch und auf keinen Fall nervig."
    //
    //      "3) Auch die kontinuierlichen Sounds im Wald nerven mich. Statt generrell durchgehende
    //       sounds zu machen lieber die Tierrufe und im Wald ein ganz leiser dezenter Windzug. Mach
    //       die meistens Sounds an eine Quelle in der Welt hörbar. zB der Windzug im Keller könnte
    //       vom Fenster ausgehen (aber auch hier nur dezent!). Im Wald mal ne Eule oder ähnliches die
    //       ruft (auch aus dem Wald hörbar, hier sollte die Position auch random wechseln)."
    //
    //  WHY THIS IS NOT A THIRD TUNING ROUND. The middle sentence is a design instruction and it is
    //  the one that governs: "statt generell durchgehende Sounds zu machen" — instead of making
    //  generally-continuous sounds. And the sentence after it says what to do instead: "mach die
    //  meisten Sounds an eine Quelle in der Welt hörbar", make most sounds audible as coming from a
    //  thing in the room. A ROOM TONE CANNOT SATISFY THAT, and not by accident: RoomToneSpread's own
    //  doc argued FOR the 180 degrees on the grounds that "this bed has no source to be locatable
    //  from: it is the room". The property the design was proud of is the property he is rejecting.
    //  There is nothing to move it onto, so there is nothing to rescue, so the beds go.
    //
    //  AND THE CELLAR'S WAS MEASURABLY TELEVISION STATIC, which the brief for this round required to
    //  be a decided question rather than an opinion. "Rauschen wie beim Fernseher" has a signature:
    //  a FLAT spectrum, a WIDE one, and NO envelope structure. All three are measurable, and
    //  .planning/envsound-replica/room.py's static_report() measures them against BAND-LIMITED WHITE
    //  NOISE as the control — which is literally what a detuned analogue set emits. SFM is the
    //  1/3-octave spectral flatness measure over 200 Hz-12 kHz (1.00 = perfectly flat); "spread" is
    //  the spectral spread in octaves; "env AC" is the largest normalised autocorrelation of the 8 ms
    //  amplitude envelope over 0.05-6.0 s of lag; "swing" is its 5th-to-95th percentile range.
    //
    //                            SFM    spread    env AC    swing    >2 kHz    delivered @4m/500 Hz
    //    TV static (control)    0.882  1.14 oct   0.079    0.67 dB    82.1%      —
    //    Stone   MB222          0.210  1.87 oct   0.075    2.70 dB    34.9%      0.00499   <— DELETED
    //    NightAir MB222         0.325  0.83 oct   0.059    1.71 dB    43.8%      0.00250   <— DELETED
    //    Stone   MB221          0.045  0.86 oct   0.072    4.31 dB     3.6%      0.00286
    //    Marsh   MB221          0.007  0.68 oct   0.065    3.61 dB     0.2%      0.00294
    //    Candle flutter         0.020  0.66 oct   0.130    7.46 dB     0.9%      0.00084
    //    Draught, Air up        0.133  1.10 oct   0.082    4.02 dB     3.7%      0.00402
    //    Chirr, chorus peak     0.652  0.94 oct   0.125    3.23 dB    81.0%      0.00192
    //
    //  READ DOWN THE COLUMNS. The ModBuild 222 stone bed is the FLATTEST bed (SFM 0.210 against every
    //  other BED's 0.007-0.133), the WIDEST (1.87 octaves, and nothing else in the bank passes 1.14),
    //  and the LEAST DYNAMIC (2.70 dB of swing against 3.6-7.5 for the others) thing this feature has
    //  ever played as a room — and it is also the LOUDEST thing in the cellar at rest, 15.5 dB over a
    //  candle bed. On every one of those axes it sits between the rest of the bank and the
    //  white-noise control, which is the ordering "es klingt wie ein Fernseher" describes.
    //
    //  TWO HONEST CAVEATS ON THE TABLE, because a measurement that only agrees with you is the thing
    //  this file has a written scar about:
    //    * env AC IS NOT THE DISCRIMINATING COLUMN HERE AND IT WAS NEVER GOING TO BE. White noise has
    //      no rhythm either (0.079), so every row scores 0.06-0.13 and the column separates nothing.
    //      It is in the table because it is the column that CONVICTED the insect bed's 3.45 Hz
    //      metronome one round ago (0.847) and because its absence is what rules that suspect out
    //      again here. SFM, SPREAD and SWING are what carry this verdict.
    //    * THE INSECT CHORUS SCORES WORSE ON SFM (0.652) AND ON HIGH-BAND SHARE (81.0%) THAN THE BED
    //      THIS ROUND DELETED, and that is stated rather than buried: a single-pole high pass at
    //      2200 Hz leaves a very broad skirt under the crickets. What it does not share with the
    //      deleted bed is the LEVEL (0.00192 against 0.00499, -8.3 dB), the ROOM (it is in the wood,
    //      where the complaint was "kontinuierlich" and not "Fernseher") or the PERMANENCE — it is
    //      now off about 77% of the time. If the next report says the WOOD hisses, that emitter is
    //      already named and MakeChirr's band is the fix; see THE INSECT CHORUS.
    //
    //  AND THE MOVE THAT DID IT IS IDENTIFIABLE. ModBuild 222 took StoneHissMix from 0.14 to 0.55 and
    //  the hiss band from 4000..8000 Hz to 2200..9000, on the argument that the cellar was authored
    //  below what the headset delivers (which was correct and is still correct). That layer IS
    //  band-limited white noise — the constant said so — and quadrupling it took the bed from
    //  SFM 0.045 / 3.6% above 2 kHz / 4.31 dB of swing to 0.210 / 34.9% / 2.70 dB. Every one of those
    //  three numbers moved toward the control. The round fixed audibility by making the cellar hiss.
    //
    //  WHAT THE CELLAR SOUNDS LIKE NOW, and it is the ruling's own answer rather than a compromise:
    //  the DRAUGHT AT THE WINDOW ("der Windzug im Keller könnte vom Fenster ausgehen"), on the
    //  window node it already stood on, at a resting floor 20.1 dB under the bed that was deleted
    //  and 4.7 dB under a single candle bed — plus the drip, the mouse, the candle flames and the
    //  fires, which are the "effektgeräusche" he reports hearing and liking. See WindRestFloor.
    //
    //  THE CANDLE FLUTTER IS STILL NOT TOUCHED, for the third round running, and now with a
    //  measurement behind the restraint rather than only an argument: at 4 perceived metres one
    //  candle bed delivers 0.00084 against the deleted bed's 0.00499, i.e. it is 15.5 dB under the
    //  thing that was convicted, and its SFM of 0.020 and 7.46 dB of swing put it at the OPPOSITE end
    //  of the static table from the control. It is a narrow, strongly-modulated, band-limited flame
    //  and it is the one sound in the cellar that is unambiguously coming from an object you can see.
    //  If the next report still says static in the cellar, CandleBedGain is the number and this is
    //  where it is named — but a round that cut it in the same edit that deleted the bed could not
    //  have told the two apart, which is the discipline CandleBedGain's own doc has enforced twice.
    //
    //  REJECTED, so that no future round re-derives one:
    //    * A QUIETER ROOM TONE. He did not ask for a quieter one, he asked for it not to be there:
    //      "statt generell durchgehende Sounds". Three rounds have now moved this level and the
    //      fourth would be arguing with the sentence rather than reading it.
    //    * MOVING THE ROOM TONE ONTO A NODE — the "an eine Quelle in der Welt" instruction taken
    //      literally on the existing bed. A stone room's air does not come from an object in it, so
    //      the node would be arbitrary, and the emitter would still be the same continuous broadband
    //      noise the first sentence rejects. The draught satisfies BOTH sentences because it is a
    //      continuous sound that genuinely has a source: air comes in through the window.
    //    * KEEPING THE BED BEHIND ITS OFF-BY-DEFAULT SWITCH. It would be dead content that reads like
    //      a feature — the exact failure mode this file's own wisp-bed scar is about — and the switch
    //      is deleted with it (see the config block at the top).
    //
    //  THE GENERATORS SURVIVE OFFLINE. MakeStone and MakeNightAir are gone from EnvSound.Bank.cs and
    //  are preserved sample-for-sample in .planning/envsound-replica/room.py as the BEFORE column of
    //  every table above, because a deletion whose before-column has been deleted is one nobody can
    //  check.

    // =================================================================================================
    //  THE INSECT CHORUS, DELETED — ModBuild 226. IT WAS THE "RAIN".
    // =================================================================================================
    //
    //  USER REPORT, hardware on ModBuild 225, verbatim, and it is two sentences of which the second
    //  is what decides the SHAPE of the fix rather than merely permitting one:
    //
    //      "Im Wald gefällt mir nur dieser 'Regen' Sound nicht der ab und zu kommt und für eine Zeit
    //       bleibt, ansonsten finde ich es sehr gut."
    //
    //  WHAT WAS HERE. The wood's insect floor — EnvSoundClip.Chirr on the Ground node, banded
    //  2200..6500 Hz by its generator and low-passed at 3000 Hz at runtime. It was a permanent bed
    //  from the day the feature shipped until ModBuild 223, which turned it into a CHORUS: a hash on
    //  a 53 s slot decided whether that slot carried one, it ran 14..30 s, ramped in and out over 5 s
    //  and was EXACTLY zero in between, peaking at 0.60 of a 0.050 gain. That change was itself the
    //  answer to "Auch die kontinuierlichen Sounds im Wald nerven mich", and it made this emitter
    //  intermittent for the first time — which is exactly when a thing starts being describable as
    //  "kommt ab und zu und bleibt für eine Zeit".
    //
    //  THE SENTENCE IS A SCHEDULE, SO THE SCHEDULE WAS MEASURED FIRST. .planning/envsound-replica/
    //  room.py's chorus_schedule() drives the shipped arithmetic — Haunt.Hash's cascade, character
    //  for character — over an hour of shared clock and prints what a player is actually in:
    //
    //      40 choruses in 67 slots (60% carry one)
    //      EACH ONE HOLDS   14.8 .. 29.5 s, mean 22.4 s        <- "für eine Zeit bleibt"
    //      THE GAPS BETWEEN 25.2 .. 236.1 s, mean 61.9 s       <- "ab und zu kommt"
    //      duty cycle 25%
    //
    //  NOTHING ELSE IN THE WOOD HAS THAT SHAPE, and the alternatives are named rather than waved at:
    //  the two calls are 2.30 s and 0.78 s one-shots; the resting draught never stops and never
    //  changes; the fires and the Earth rumble need an infusion; the apparitions are on 83 s and the
    //  wood has three cards of which one is silent.
    //
    //  ...AND THEN "REGEN" WAS MEASURED, BECAUSE A SCHEDULE ALONE IS NOT AN IDENTIFICATION.
    //  room.py's rain_report() adds ONE instrument this file did not have: a RAIN CONTROL. The
    //  static round's control is band-limited white noise, which is a television; rain is the same
    //  broadband hiss with a tilt and a slower envelope, so "es klingt wie Regen" needed a control of
    //  its own or it would have stayed an opinion. Every forest emitter, against both controls, at
    //  4 perceived metres:
    //
    //                          SFM   spread    >2 kHz   centroid   delivered@4m
    //    RAIN control         0.493  0.89 oct   94.5%    5176 Hz        -
    //    TV static control    0.884  1.14 oct   82.2%    3680 Hz        -
    //    Insect chorus, peak  0.652  0.94 oct   81.0%    3381 Hz     0.00192   <— DELETED
    //    Leaves, at rest      0.133  1.10 oct    3.7%     598 Hz     0.00076
    //    Leaves, AIR FULL     0.133  1.10 oct    3.7%     598 Hz     0.00625
    //    Fire roar (Fire up)  0.005  0.64 oct    0.1%     436 Hz     0.00503
    //    Rumble (Earth up)    0.151  1.24 oct    0.0%     545 Hz     0.00178
    //    Owl (an EVENT)       0.000  0.22 oct    0.0%     394 Hz     0.01008
    //    NightBird (an EVENT) 0.000  0.14 oct  100.0%    3055 Hz     0.01505
    //
    //  READ THE >2 kHz COLUMN. The chorus puts 81.0% of its energy above 2 kHz, against 94.5% for
    //  rain and 3.7% for the next-nearest CONTINUOUS emitter in the room. It is not near the rain
    //  control; it is TWENTY-TWO TIMES nearer to it than anything else the wood plays.
    //
    //  THE ALTERNATIVE THAT HAD TO BE FALSIFIED, AND IT WAS FALSIFIED ON BAND AND NOT ON LEVEL. The
    //  LEAVES bed under an Air infusion also "comes now and then and stays for a while" — the wind
    //  gate opens over 0.9 s, holds for the infusion and closes over 2.4 s — and it is the LOUDER of
    //  the two suspects by 10.3 dB (0.00625 against 0.00192). Level therefore does not convict it and
    //  could not have: what exonerates it is that it is an APERTURE, 53.2% of its energy under
    //  200 Hz, centroid 598 Hz, 3.7% above 2 kHz. A wind is not a hiss. The same column excludes the
    //  fire roar (0.1% above 2 kHz, and it needs Fire up) and the Earth rumble (0.0%, two sines under
    //  110 Hz, and it needs Earth up), and duration excludes both calls.
    //
    //  AND THE WOOD WAS RENDERED WITH THE SUSPECT AND WITHOUT IT, which is the instrument the
    //  ModBuild 222 round asked for by name and the one that killed the leaf litter. forest_mix()
    //  sums every continuous forest emitter at its DELIVERED weight — gain x modulator x rolloff at
    //  4 perceived metres — because a player does not hear a clip, he hears a room:
    //
    //                          SFM   spread    >2 kHz   centroid   delivered@4m
    //    WITH the chorus      0.610  1.55 oct   41.9%    1870 Hz     0.00203
    //    WITHOUT it (this)    0.134  1.10 oct    3.7%     599 Hz     0.00076   -8.5 dB
    //    WITH, Air full       0.167  1.17 oct    4.8%     629 Hz     0.00650
    //    WITHOUT, Air full    0.134  1.10 oct    3.7%     599 Hz     0.00624
    //
    //  Every rain-shaped property of the wood IS this one emitter. Removing it takes the room's
    //  1/3-octave flatness from 0.610 to 0.134 (rain control 0.493) and its high-band share from
    //  41.9% to 3.7% (rain control 94.5%).
    //
    //  WHY DELETION AND NOT A SHORTER SWELL, A QUIETER ONE OR A NARROWER BAND. "ansonsten finde ich
    //  es sehr gut" is the load-bearing half of his report, and the arithmetic above turns it into an
    //  argument rather than a preference: the chorus has a 25% duty cycle, so THE WOOD HE SAYS HE
    //  LIKES IS THE OTHER 75% — it is the "WITHOUT" row, which is what already plays for three
    //  quarters of every session. Deleting the emitter does not invent a new mix to be judged; it
    //  makes the mix he has already approved play all of the time. Shortening the swell would leave
    //  the same sound arriving less often, which is answering "I don't like this" with "you will hear
    //  it less"; lowering the gain is the move THREE previous rounds made on this exact emitter and
    //  all three were rejected; and narrowing the band would be a fourth round of tuning a thing he
    //  has now asked to not be there.
    //
    //  THIS WAS THE WRITTEN NEXT STEP, and that is the strongest reason of all. The block this text
    //  replaces ended: "IF THE NEXT REPORT STILL SAYS THE WOOD IS TOO BUSY, this emitter is named,
    //  its SFM is on the record above, and the next step is stated: delete it, or narrow its band
    //  with a second high-pass pole in MakeChirr so the hiss under the crickets goes. It is NOT
    //  another gain." The next report has arrived and it names this emitter's schedule. The prediction
    //  was made before the evidence, which is the only kind worth acting on.
    //
    //  WHAT THE WOOD SOUNDS LIKE NOW: the resting draught through the canopy ("im Wald ein ganz
    //  leiser dezenter Windzug", 0.00076 at 4 perceived m), the owl and the night bird from a perch
    //  that moves ("mal ne Eule oder ähnliches die ruft ... die Position auch random wechseln"), the
    //  apparitions, and the fires and the rumble while their elements are up. Every one of those is
    //  a thing happening in a place, which is his standing instruction ("Mach die meisten Sounds an
    //  eine Quelle in der Welt hörbar"), and none of them is continuous broadband noise.
    //
    //  WHAT WENT WITH IT: EnvSoundClip.Chirr, the Chirr property and MakeChirr in EnvSound.Bank.cs,
    //  NightBed(), NightBedGain, InsectLowPassHz, all six InsectChorus* constants and both of its
    //  hash channels, the AddBed("Night", ...) call in BuildSwamp and its warning, and the chorus
    //  paragraph in LogBuilt. The generator is preserved sample-for-sample in
    //  .planning/envsound-replica/room.py as make_chirr — a deletion whose before-column has been
    //  deleted is one nobody can check, which is the rule the room tones' deletion established.
    //
    //  ONE HONEST CAVEAT ON THE TABLE ABOVE, because a measurement that only agrees with you is what
    //  this file has a written scar about: the ENVELOPE columns separate nothing here and are left
    //  out of the printed table for that reason. The rain control's own envelope autocorrelation is
    //  0.887 (its shower swell) and the two one-shot calls score 0.86 and 0.75 simply for being
    //  events in silence, so the column ranks a 0.78 s bird call as more rain-like than rain. SFM,
    //  the HIGH-BAND SHARE and the SCHEDULE carry this verdict; the envelope is the column that
    //  convicted the 3.45 Hz tractor one round ago and it is the wrong instrument for this question.
    //
    //  THE 3000 Hz CORNER REPAIR DIES WITH THE BED IT REPAIRED, and that is not a regression being
    //  smuggled out. InsectLowPassHz existed because the clip was banded 2200..6500 Hz and was being
    //  played through a 1150 Hz one-pole corner that attenuated its entire band — a real defect, fixed
    //  in ModBuild 221 and kept unconditionally in 223. With the clip gone there is nothing left for
    //  it to correct. If a future round wants crickets in this room, it must NOT restore this bed:
    //  what he calls rain is broadband noise, and a cricket is a NARROWBAND TONAL CHIRP with a pulse
    //  rate. That is a different generator, not a different gain — and it should arrive as EVENTS on
    //  the shared clock, the way the owl did, because that is the form of this feature he has
    //  repeatedly said he likes.

    // ---- the fire ------------------------------------------------------------------------------------
    //
    // USER REQUEST, ModBuild 151 hardware, verbatim: "Geb auch Feuer dezente Geräusche."
    //
    // WHAT WAS THERE. Nothing, and the state file has said so since ModBuild 148: THE FIRE'S AUDIO
    // BED WAS LITERALLY A DRAUGHT. BuildCellar's three "Flame<n>" beds rode EnvSoundClip.Bed — the
    // same buffer as the window draught and the swamp canopy — with `0.9f * ElementMood.Live(0)` in
    // the gain lambda. So a Fire infusion made the WIND louder at the candles, and the eleven fires
    // the content lane actually seated (six in the cellar, five in the wood) made no sound at all.
    // What THIS round adds is the FIRES.
    //
    // AND THE SENTENCE THAT USED TO FOLLOW WAS WRONG, which is why it is quoted rather than deleted:
    // "The candle beds are untouched by this round; they are candles, they are steady, and the
    // shared bed is the right model for them." It is not. A candle is steady, but the shared bed is
    // the WIND, and leaving three ungated, fire-lit copies of the wind buffer standing beside a new
    // fire sound is exactly what the user reported one build later ("beim Feuer Geräusch ist auch
    // immer das Wind geräusch mit dabei"). ModBuild 153 gave the candles a clip of their own; see
    // THE CANDLES above.
    //
    // THREE LAYERS, THREE PLACES, ONE SOURCE EACH — and the shape of this is decided by the user's
    // own two standing rulings rather than by taste:
    //
    //   * "verortbar von seinen entsprechenden Quellen". A single bed at the room's centre is
    //     exactly what he ruled out. The bake seats the cellar's fires at THREE SITES (the crates,
    //     the casks, the bookcase) and the wood's at three more (the snag, the deadfall, the
    //     brushwood), and those are the six seats this file places a source on. Their node names are
    //     the bake's own — see FireSites — and WHICH NODE EACH ONE RESOLVED TO IS LOGGED, including
    //     when it does not resolve. That is not diligence, it is a scar: the swamp's wisp bed looked
    //     for "Wisp" while the bake had built "WispWisp", so it never played in any shipped build and
    //     nobody noticed for months, because a missing node and "this room has no such object" are
    //     the same silent path.
    //   * "dezent … nie aufdringlich überlagernd". The roar is a BED under everything; the crackle is
    //     a 55 ms transient; and both only exist while a Fire infusion is up, which is seconds at a
    //     time and not the whole scenario. That last point is worth stating because it is what buys
    //     the crackle its rate: unlike the drip, which fires every 2.85 s for the entire session,
    //     this cue has a duty cycle set by the game.
    //
    // WHY THE CRACKLE COMES OUT OF THE BED'S OWN SOURCE (AudioSource.PlayOneShot) RATHER THAN THE
    // ONE-SHOT POOL. Three reasons, and the first is the one that decided it:
    //   * THE POOL IS THREE VOICES AND IT IS NOT OURS ALONE. Three sites at a 2.2 s mean is about
    //     1.4 crackles a second across a room; the drip, the rat's feet, its squeak and every haunt
    //     cue share those same three voices, and PlayShot takes them round-robin. The fire would have
    //     evicted the drip and the bookshelf's bang within seconds.
    //   * IT INHERITS EVERYTHING THAT IS ALREADY RIGHT. PlayOneShot mixes into a source without
    //     touching its loop, so the crackle gets the site's position, its rolloff, its spatialisation
    //     and — because Unity multiplies a one-shot by AudioSource.volume — the fire's GATE, the
    //     duck, the player's dial and the game's two volume sliders, with no second gain path to keep
    //     in step. When the bed is paused because Fire is down, the crackle cannot sound: not because
    //     a branch says so, but because there is no source running.
    //   * IT COSTS NO VOICE. The cellar sits at 11 of MaxVoices with the fires added; a per-site
    //     crackle voice would have made it 14.

    /// <summary>Fire intensity at which a site starts to make any sound at all, and the intensity at
    /// which it is fully alight. The shape and the argument are THE WIND GATE's exactly — a cut is
    /// itself an event, and a threshold crossed instantly puts a step into the middle of
    /// ElementMood's own 1 s smoothstep — and the numbers are chosen against the PICTURE rather than
    /// by feel. <c>EnvFlame.shader</c> collapses every card of a seated fire to a point unless
    /// <c>saturate(e.fire) &gt; 0</c> and then scales it linearly (:615-628), so the flames appear the
    /// instant Fire leaves zero. Starting the SOUND at 0.05 and reaching full at 0.40 therefore puts
    /// the audio a little BEHIND the picture at both ends, which is the only safe direction: a fire
    /// you can hear before you can see it is a sound with no source, and that is the one thing this
    /// whole feature is not allowed to be.</summary>
    private const float FireGateOn = 0.05f;
    private const float FireGateFull = 0.40f;

    /// <summary>Seconds to light and to die. ASYMMETRIC like the wind's and for the mirror-image
    /// reason: a fire catches faster than it goes out. It is much less asymmetric than the draught's
    /// 0.9/2.4, because a fire that is no longer being infused stops being DRAWN over about a second
    /// — a sound that outlived its own flames by two seconds would be the disembodied bed again.</summary>
    private const float FireGateOpenSeconds = 0.7f;
    private const float FireGateCloseSeconds = 1.6f;

    /// <summary>The gate, 0..1, walked in <see cref="TickBeds"/> and read by <see cref="FireBed"/>
    /// and by <see cref="TickFire"/>. ONE field for every site in the room, exactly as
    /// <see cref="_windGate"/> is one field for both wind beds: "the fires are lit" is one fact about
    /// the room and not three that could disagree. The sites differ by WHERE they are and by their
    /// own crackle streams, not by whether they are burning.</summary>
    private static float _fireGate;

    /// <summary>What a fire site's source is set to before the modulator, the master and the rolloff.
    /// IT IS THE CEILING, and that is deliberate and is not the roar being loud: this one source
    /// carries BOTH layers, and its volume is the reference for the CRACKLE (see
    /// <see cref="FireCrackleLevel"/>). The roar's own level is set inside its buffer instead — the
    /// bank normalises it to a peak of 0.30 against the crackle's 0.95 — so the continuous layer
    /// leaves this source at an effective 0.048 peak, well under the candle flames' 0.055 x 0.85.
    /// Splitting a "bed gain" and a "crackle gain" would have been two numbers for one fire.</summary>
    private const float FireBedGain = MaxEmitterGain;

    /// <summary>
    /// THE ROLLOFF, AND IT IS SIZED TO THE ROOM RATHER THAN TAKEN FROM A DEFAULT — which is the
    /// lesson ModBuild 150 paid for on the bookshelf and the reason the candle flames have never been
    /// heard.
    ///
    /// <para><b>THE MEASUREMENT.</b> Player.log (ModBuild 151 session) places the room's centre at
    /// (-4.30, -12.94, 0.00) with the art scaled 11.905 world units per authored metre at
    /// rigScale 13.75, so one authored metre is 0.866 PERCEIVED metres and the 10.5 x 9.0 m cellar is
    /// 9.1 x 7.8 perceived m across. The head in that session sat at world (-46.5, 8.0, -7.2),
    /// (-40.7, 8.9, 3.6) and (-19.8, 7.7, -1.2) (Heartbeats #6, #30, #4). Against the three cellar
    /// fire seats derived from the bake that is:</para>
    /// <code>
    ///                     crates      casks      bookcase
    ///   Heartbeat  #6      3.56 m     2.16 m       7.61 m     (perceived)
    ///   Heartbeat #30      4.04 m     2.93 m       7.49 m
    ///   Heartbeat  #4      3.36 m     3.29 m       5.96 m
    /// </code>
    /// <para>So the listener is between 2.2 and 7.6 perceived metres from a fire, and typically 3-7.
    /// The candle flames' authored minimum is 0.6 m, and Unity's logarithmic rolloff is <c>min/d</c>
    /// past the minimum — so at 3.5 m a candle bed is already <b>-15.3 dB</b> and at 7.5 m
    /// <b>-22 dB</b>. That is not a quiet bed, it is an absent one, and it is the arithmetic reason
    /// the fire has never been heard however its gain was set.</para>
    ///
    /// <para><b>3.0 m IS CHOSEN SO THE NEAREST FIRE IS UNATTENUATED AND THE FURTHEST IS STILL THERE.</b>
    /// At the minimum the curve is flat, so a fire the player is leaning over plays at its authored
    /// level; at 7.6 m the far fire is at 3.0/7.6 = <b>-8.1 dB</b>. Eight decibels across the room is
    /// what makes the three sites LOCATABLE — together with the spatialiser's own panning, which is
    /// untouched and is what actually carries direction — while leaving all three audible. A smaller
    /// minimum buys more level contrast and costs the far fire entirely, which is precisely the trade
    /// the bookshelf got wrong in the other direction.</para>
    ///
    /// <para>18 m for the maximum: under logarithmic rolloff Unity stops attenuating at the maximum
    /// rather than cutting, so this simply says "the curve is honest across the whole room and the
    /// plateau (-15.6 dB) is well outside it". Nothing in either room is 18 perceived metres from
    /// anything else.</para>
    /// </summary>
    private const float FireMinMeters = 3.0f;
    private const float FireMaxMeters = 18f;

    /// <summary>How loud a crackle and a settle are as a fraction of the site's source volume — i.e.
    /// the <c>volumeScale</c> passed to <see cref="AudioSource.PlayOneShot(AudioClip,float)"/>.
    ///
    /// <para>AT THE PLAYER'S DEFAULT DIAL, with the game quiet and the fire fully alight, a site's
    /// source reaches <c>0.16 x 0.75 = 0.120</c> at the top of its slow breathing (0.112 on average),
    /// so a crackle peaks at <c>0.120 x 0.50 x 0.95 = 0.057</c> before
    /// the rolloff and at <b>0.049</b> at a typical 3.5 perceived m. At the reporting user's own
    /// settings (game volume 0.64, master 0.480) that is <b>0.031</b>, and at the far bookcase fire
    /// 7.5 m away <b>0.015</b>. Under the duck it is 0.35 of those. For scale: a game cue at full
    /// level is 1.0, the bookshelf's permitted
    /// bang was measured reaching him at 0.528, and the drip — the quietest deliberate thing in the
    /// feature — arrives at about 0.004. The crackle is 24.6 dB under the one sound allowed to be
    /// loud and it is the layer the user asked for.</para>
    ///
    /// <para>The settle is 0.38 rather than 0.50 because it is a LONGER event: it peaks lower but its
    /// loudest 20 ms window measures slightly HIGHER than a crackle's (0.117 against 0.109), and 20 ms
    /// is roughly what the ear integrates. 0.38 puts the two within 2 dB of each other by that
    /// measure, which is "rarer and duller", not "quieter and duller".</para></summary>
    private const float FireCrackleLevel = 0.50f;
    private const float FireEmberLevel = 0.38f;

    /// <summary>
    /// MEAN SECONDS BETWEEN CRACKLES at one site, with the fire barely caught and fully alight. The
    /// caller lerps between them on the element's own strength, and the element is scenario-wide
    /// state the game keeps bit-identical on every client — which is what lets the RATE be shared
    /// without a byte on the wire, exactly as <see cref="Haunt.Resolve"/> shares the apparitions'.
    ///
    /// <para><b>THE RATE IS THE ONE PLACE "dezent" IS AT RISK, so it is derived rather than picked.</b>
    /// A real fire crackles several times a second; three sites at the full-Fire mean give
    /// <c>3 / 2.2 = 1.36</c> crackles a second across a room, which is on the quiet side of a
    /// real hearth and is the number the standing rule wants. Two things bound the exposure further
    /// and neither is available to the drip: the fires only exist while a Fire infusion is up, and
    /// each crackle is 55 ms with 1.2 ms to -20 dB, so nothing here can mask a syllable — the class
    /// doc's duration test, which is what admits this layer into the 1-5 kHz band at all.</para>
    ///
    /// <para><b>THESE TWO NUMBERS DID NOT MOVE AT ModBuild 226 AND THE REALISED RATE DID, by 2%.</b>
    /// The walk that this schedule replaced ran through <see cref="EnvSoundSchedule.PoissonGap"/>,
    /// whose clamps truncate the exponential to 0.962 of its nominal mean, so 2.2 s of nominal
    /// produced a measured 2.14 s of realised gap and 1.40 crackles a second across the room. The
    /// Bernoulli tick has no truncation factor — a geometric distribution with p = tick/mean has
    /// mean exactly `mean` — so the same constants now realise 2.19 s and 1.37/s. The full
    /// before/after distributions are in <see cref="FireCrackleTickSeconds"/>, measured through the
    /// real hash rather than derived.</para></summary>
    private const float FireGapCalm = 4.0f;
    private const float FireGapFull = 2.2f;

    /// <summary>How often the scheduled event is an ember SETTLING rather than a crackle. One in six.
    /// It is drawn from the same stream on its own hash channel rather than scheduled separately,
    /// because a fire does not have two clocks: what is happening is one bed of burning wood, and
    /// what you hear from it next is a cell bursting or a lump shifting.</summary>
    private const float FireEmberShare = 0.17f;

    /// <summary>
    /// THE CRACKLE'S TICK GRID, in shared-clock seconds — the quantity that replaced the walk at
    /// ModBuild 226. Each site asks once per tick "does my fire crackle in this one", with
    /// probability <c>tick / mean</c>, and places the event at a hashed offset in the tick's first
    /// half. See <see cref="EnvSoundSchedule.TickFires"/> for the process and
    /// <see cref="TickFire"/> for the user ruling that required it.
    ///
    /// <para><b>1.31 s IS DERIVED FROM THE FLOOR IT HAS TO BUY, not picked.</b> The offset window is
    /// half a tick, so the shortest gap two consecutive crackles at one site can have is
    /// <c>tick / 2</c> = <b>0.655 s</b> — which has to stay clear of the 0.45 s repeat the user
    /// called "super nervig" and clear of the top of the 0.2-2 s band the ear reads as a rhythm
    /// rather than as separate events. The shipped walk's floor was
    /// <c>PoissonGapMin x mean</c> = 0.616 s at full Fire, so this is 0.6 dB of extra margin at the
    /// place it matters and a shorter floor than the walk had when the fire was merely caught
    /// (1.12 s) — which is the honest statement of the trade rather than the flattering one.</para>
    ///
    /// <para>It is also PRIME (131) and non-commensurate with every other period in this file — the
    /// drip's 2.85, the rat's 26, the calls' 41, the apparitions' 83 and every LFO — item 6 of the
    /// class doc, so three fires and a drip never fall into one rhythm.</para>
    ///
    /// <para><b>MEASURED, NOT ESTIMATED.</b> .planning/envsound-replica/room.py's
    /// <c>crackle_gaps()</c> drives BOTH schedules through the real <c>Haunt.Hash</c> cascade over
    /// six hours of shared clock and prints the gap distributions at one site:</para>
    /// <code>
    ///                        mean    min    p05    p50    p95     max    rate/room
    ///   226 tick  alight     2.19s   0.66   0.92   1.59   5.16   13.14    1.37/s
    ///   225 walk  alight     2.14s   0.62   0.62   1.54   5.72    5.72    1.40/s
    ///   226 tick  caught     3.93s   0.68   1.02   2.83  10.43   43.15    0.76/s
    ///   225 walk  caught     3.89s   1.12   1.12   2.81  10.40   10.40    0.77/s
    /// </code>
    /// <para>THE MIDDLE OF THE DISTRIBUTION IS THE SAME DISTRIBUTION — the medians agree to 0.05 s
    /// and the 95th percentiles to 0.03 s at both fire strengths, and the room's rate moves by 2%
    /// (1.40/s to 1.37/s), which is the truncation factor the walk's clamps imposed and the tick
    /// does not. That 2% is the entire audible cost of making this cue shared, and it is in the
    /// quieter direction.</para>
    ///
    /// <para><b>AND THE TAIL IS NO LONGER CLAMPED, which is the one property that got worse — read
    /// the max column.</b> The walk hard-capped a gap at <c>PoissonGapMax x mean</c>; a geometric
    /// tail is unbounded, so ONE SITE can be silent for 43 s in six hours where the walk could not
    /// pass 10.40. It is not worth a guard, and the reason is a measurement rather than an argument:
    /// the ear counts the ROOM, which has three sites drawing independently, and the same function's
    /// second table gives the gap between consecutive crackles ANYWHERE as <b>mean 0.73 s, p95
    /// 1.64 s, worst 6.43 s fully alight</b> and <b>mean 1.33 s, p95 3.70 s, worst 10.62 s just
    /// caught</b>, over the same six hours. A 43 s silence at one seat is invisible while the other
    /// two are burning.</para></summary>
    private const float FireCrackleTickSeconds = 1.31f;

    /// <summary>Hash channels for the fire's four per-event draws. They MUST differ from each other —
    /// all four are taken from the same key, and two draws off one channel would lock (say) the
    /// longest gaps to the loudest variant forever, which is a subtle way of having no variation.
    /// They need NOT differ from the drip's or the rat's: those index a different thing (a drip
    /// period, a rat slot) and nothing ever compares the two. That is the same reasoning
    /// <see cref="DripVariantChannel"/> sets out at length.
    ///
    /// <para><see cref="FireGapChannel"/> KEPT ITS NAME AND CHANGED ITS QUESTION at ModBuild 226: it
    /// used to carry the uniform that became an exponential gap and it now carries the one that
    /// decides whether this tick fires at all. It is the same draw off the same channel answering
    /// the same physical question ("when is the next cell going to burst"), so renaming it would
    /// have made a diff look bigger than the change.</para></summary>
    private const float FireGapChannel = 3f;
    private const float FireVariantChannel = 5f;
    private const float FireEmberChannel = 6f;

    /// <summary>WHERE INSIDE ITS TICK a crackle lands. It has to be a channel of its own: it is
    /// compared BY THE EAR against the gap draw on the very same event, and one channel for both
    /// would lock every crackle that happened at all to the same instant inside its tick, which is
    /// the lattice <see cref="FireCrackleTickSeconds"/> exists to avoid.</summary>
    private const float FireWhenChannel = 2f;

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

        // ---- WHAT THIS BED IS, recorded so a LOG can say it and so TickBeds can CHECK it.
        //
        // Three facts, and they exist because for five builds the only way to find out which clip an
        // emitter played and what gated it was to read BuildCellar. That is how a bed named "Flame"
        // went on playing the window draught, ungated, with a Fire term in its gain, through two
        // rounds of user reports about exactly that. A fact nobody can read from a log is a fact
        // nobody checks.
        internal EnvSoundClip Clip;
        internal bool AirLed;                         // its level is LED by Air, above a rest floor
        internal bool FireLit;                        // its level rises with a Fire infusion

        // THERE IS ONE LOW-PASS CORNER AGAIN. The Voice carried TWO from ModBuild 154 to 222 — one
        // for the room tone on and one for it off — because the insect bed's corner rode
        // `[EnvSound] AmbienceBed`. That dial is deleted (see the config block) and the fallback
        // corner it selected was never a setting: 1150 Hz attenuated the chirr's entire band and was
        // the defect the 3000 Hz corner repairs. The corner is now written once by AddBed and never
        // touched again, which also removes a per-frame comparison from every bed in the room.
        // (The chirr itself is gone as of ModBuild 226 — see THE INSECT CHORUS, DELETED. This
        // paragraph is kept because the DEFECT CLASS is what it records: AddBed's default corner is
        // right for a bed with body and wrong for one without, and the symptom is a bed nobody can
        // hear rather than one that sounds wrong.)

        /// <summary>True for the emitters that play the WIND buffer, which is the one clip in the
        /// bank with a user ruling attached to it. <see cref="TickBeds"/> asserts on this: a
        /// wind-clip bed whose level EXCEEDS <see cref="WindRestFloor"/> with Air down is the
        /// shipped ModBuild 152 defect, and it writes a warning instead of playing. (Until ModBuild
        /// 223 the bound was "not exactly zero"; the user narrowed the ruling to allow a resting
        /// draught — see THE WIND GATE — and the assertion was narrowed with it rather than
        /// dropped.)</summary>
        internal bool IsWindClip => Clip == EnvSoundClip.Bed;
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
    // ...and the SAME PAIR AGAIN, for the Advanced menu's forced apparitions only. A forced event is
    // a second, independent stream of events over the same cards, and until ModBuild 150 the two
    // shared one latch — so a tester pressing test buttons over a running scheduled event made the
    // scheduled stream forget what it had already played and fire it a second time. See TickHaunt.
    private static float _lastForcedStart = float.NaN;
    private static int _lastForcedCard = -1;

    private static int _nextShot;

    // THE EVENT NODES, RESOLVED ONCE AT BUILD. They are cached rather than looked up per event, and
    // that is not a micro-optimisation: `Find` is a recursive walk of a photoscanned room's whole
    // hierarchy, the drip alone fires every 2.85 s, and this project has just spent a round
    // DELETING periodic full-scene sweeps for exactly this cost. The room is frozen once placed and
    // these nodes never move within it, so one resolution per build is not merely cheaper, it is
    // the correct number.
    private static Transform? _dripNode;
    private static Transform? _ratNode;

    /// <summary>The frame the wood's night calls are placed in — the GROUND node, which the bake
    /// puts at the room's local origin with identity rotation and unit scale, so a local position in
    /// it is in the bake's own AUTHORED METRES. Null in the cellar, which has no calls, and null in a
    /// wood whose bake renamed the ground, which <see cref="BuildSwamp"/> warns about loudly. See
    /// <see cref="NightCallPerch"/> for why this is a frame and not a node the call sits on.</summary>
    private static Transform? _perchFrame;

    /// <summary>The bookcase's own node ('Shelf', BuildTippingShelf's <c>Place</c> call), or null in
    /// a room that has none. It is the ONLY thing that knows where the shelf stands and which way it
    /// falls; see <see cref="ShelfFloorContact"/> for why the apparition catalogue could not answer
    /// either question.</summary>
    private static Transform? _shelfNode;

    // ---- the fire sites, resolved ---------------------------------------------------------------
    //
    // THE NODE NAMES ARE THE BAKE'S OWN, and they are a CONTRACT with
    // unity/.../Editor/BuildEnvironmentRooms.cs exactly as the drip's constants are. Every fire in
    // both rooms is placed by `BuildFireCards`, whose last line is
    //
    //     return Place(root, $"Fire{n}", mesh, seat, ...);
    //
    // under RoomGeo — so the name of a fire's node is the literal "Fire" plus the name the room
    // builder passed. AddCellarFire passes CrateTop, CrateFoot, Barrel, Spill, ShelfTop, ShelfMid;
    // AddForestFire passes Snag0, Log0, Log1, Log2, Brush. THE PREFIX IS WHY THIS TABLE IS WRITTEN
    // OUT RATHER THAN GUESSED: the wisp bed asked for "Wisp" against a bake that had built
    // "WispWisp" (same `$"{family}{n}"` shape), never played in any shipped build, and was silent
    // about it because Find returning null is indistinguishable from a room with no such object.
    // Every site below therefore names its candidates explicitly and LOGS which one answered.
    //
    // SIX SITES AND ELEVEN FIRES: the sites are the bake's own grouping (its LightRig FireSeats are
    // "Crates", "Casks", "Shelf" / "Snag", "Log", "Brush"), so a site is where a THING is on fire
    // rather than where one flame card stands. Sounding all eleven separately would be eleven
    // sources for six causes, and the two fires of a site are 0.3-0.6 m apart — inside the spread of
    // a single source at any distance the player can get to.

    /// <summary>How many fire sites either room can have. Three, and it is the same three in both:
    /// the cellar's crates/casks/bookcase and the wood's snag/deadfall/brushwood.</summary>
    private const int FireSites = 3;

    /// <summary>What the sites are called in the log, per style. Not the node names — those are
    /// below.</summary>
    private static readonly string[] CellarFireSites = { "Crates", "Casks", "Bookcase" };
    private static readonly string[] SwampFireSites = { "Snag", "Deadfall", "Brushwood" };

    /// <summary>THE NODES, in the order <see cref="Find"/> tries them. The first name of each row is
    /// the fire the site is really named for; the second is the other fire at the same site, which is
    /// at most 0.6 m away and is a far better answer than silence if the content lane renames or
    /// merges one. See the block above for where the names come from.</summary>
    private static readonly string[][] CellarFireNodes =
    {
        new[] { "FireCrateTop", "FireCrateFoot", "Crate2", "Crate0" },
        new[] { "FireBarrel", "FireSpill", "Barrel1", "Barrel2" },
        new[] { "FireShelfTop", "FireShelfMid", "Shelf" },
    };

    private static readonly string[][] SwampFireNodes =
    {
        new[] { "FireSnag0", "FireSnag" },
        new[] { "FireLog1", "FireLog0", "FireLog2" },
        new[] { "FireBrush", "FireBrushwood" },
    };

    /// <summary>The three sites' sources, or null for a site this room has no node for. The VOICE and
    /// not the transform, because <see cref="TickFire"/> puts the crackle through the same
    /// <see cref="AudioSource"/> the roar is looping on — see THE FIRE.</summary>
    private static readonly Voice?[] _fireVoices = new Voice?[FireSites];

    /// <summary>
    /// Per site: the last CRACKLE TICK this client has already answered. It is an EDGE DETECTOR and
    /// not a schedule — the schedule itself is a pure function of the shared clock since ModBuild
    /// 226 (see <see cref="TickFire"/>) — so this array holds nothing another client would need to
    /// agree with, exactly as <c>_lastRatSlot</c> and <c>_lastNightCallSlot</c> hold nothing.
    ///
    /// <para><c>long.MinValue</c> is "not observing yet", the same sentinel and the same meaning as
    /// the rat's and the calls': the tick a client walked in on is SWALLOWED rather than fired,
    /// because its event has already happened.</para>
    ///
    /// <para>WHAT THIS REPLACES: <c>_fireNextAt</c> (a shared-clock time) and <c>_fireSeq</c> (a
    /// running index), which together were the WALK — each gap drawn from the last, so two clients
    /// that began observing at different clock values sat on different phases of it forever. That
    /// was documented as acceptable and the user has now ruled otherwise; see
    /// <see cref="TickFire"/>.</para>
    ///
    /// <para>THE ARRAY'S DEFAULT IS 0 AND NOT THE SENTINEL, and it does not need to be: the only
    /// path to <see cref="TickFire"/> runs through <see cref="Build"/>, which begins with
    /// <see cref="Teardown"/>, which calls <see cref="ArmSchedules"/>. The zeros are never read. If
    /// a future round gives this file a second entry point, that is the fact it breaks.</para>
    /// </summary>
    private static readonly long[] _lastFireTick = new long[FireSites];

    /// <summary>What <see cref="LogBuilt"/> says about the sites — built once during
    /// <see cref="Build"/>, while the candidate lists are in hand, and thrown away with the
    /// teardown.</summary>
    private static string _fireResolution = string.Empty;

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

        // BEFORE ANY CONSUMER READS IT. A jump has to re-arm the schedules on the SAME frame it
        // happens, or the detectors below fire against the new timeline before they are told the old
        // one ended — see THE JOIN.
        TickClockContinuity(clock);

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
        // THE BOOKCASE ITSELF. Exact name, and exact is what makes it safe: the room also carries
        // 'CandlesShelf', 'ShelfTop', 'ShelfMid', 'WebShelf' and 'WaxShelf', every one of which is a
        // thing standing ON the shelf rather than the shelf. A prefix search would have taken
        // whichever the walk reached first, which is precisely the class of near-miss that put the
        // last three rounds' bang in the middle of the room.
        _shelfNode = style == SkyStyle.Cellar ? Find(roomGo.transform, "Shelf") : null;

        _built = true;
        _builtStyle = style;
        ApplyScale(rigScale, "the environment was built");
        LogBuilt(style, rigScale);
    }

    private static void BuildCellar(GameObject room)
    {
        // THE CANDLES. Three groups, each a node the bake named "Candles<n>" (BuildEnvironmentRooms
        // .cs:2653, CandleGroup). See THE CANDLES for what this call used to be and why every part
        // of it moved: the clip, the filter and the Fire term are three separate corrections to the
        // same mistake.
        //
        // A MISSING GROUP IS NOW LOUD. This loop used to walk whatever it found and say nothing, so
        // "the cellar has no candles today" and "the bake renamed CandleGroup" were the same silence
        // — the WispWisp trap exactly. The count is recorded and LogBuilt reports it.
        _candleGroups = 0;
        float[] candlePeriods = { 3.11f, 4.37f, 5.83f };
        foreach (Transform t in FindByPrefix(room.transform, "Candles"))
        {
            float period = candlePeriods[_candleGroups];
            AddBed($"Flame{_candleGroups}", t, EnvSoundBank.Bank(EnvSoundClip.Flutter),
                   CandleBedGain, 0.6f, 5.5f,
                   () => CandleBed(period),
                   // NO RUNTIME LOW PASS, for the fire's reason: the flutter's band (470..1050 Hz,
                   // two poles up and FOUR down) is baked into its buffer and is far tighter than
                   // the 1150 Hz single pole this would add. See EnvSoundBank's THE CANDLE.
                   lowPassHz: 0f,
                   clipName: EnvSoundClip.Flutter, airLed: false, fireLit: true);
            _candleGroups++;
            if (_candleGroups >= candlePeriods.Length)
                break;
        }

        // THE WINDOW, AND SINCE ModBuild 223 IT IS THE CELLAR'S WHOLE RESTING AMBIENCE. The draught
        // enters here — the bake's DraftDir starts at the window and leaves under the stair door
        // (BuildEnvironmentRooms.cs:1710-1714), and the flames and dust motes already lean along it.
        //
        // USER, 2026-08-22, verbatim: "zB der Windzug im Keller könnte vom Fenster ausgehen (aber
        // auch hier nur dezent!)" — which names THIS emitter and THIS node. It already stood here;
        // what changed is that it no longer goes silent with Air down. It plays at WindRestFloor for
        // the whole scenario and the Air infusion is the rise above that (see WindBed), so the room
        // has a quiet sound that genuinely comes from a thing in it, which is what replaced the
        // 'Stone' room tone deleted this round.
        //
        // A MISSING WINDOW IS NOW LOUD. With the room tone gone this bed is the ONLY continuous
        // emitter in the cellar that is not a candle, so `Find` returning null here is no longer
        // "one cue is quiet" — it is a room with nothing at rest in it at all, which is the state
        // the whole 2026-08-22 report is about. Same WispWisp argument as every other refusal here.
        Transform? window = Find(room.transform, "WindowGlow", "WindowReveal", "WindowBars");
        if (window != null)
            AddBed("Draught", window, EnvSoundBank.Bank(EnvSoundClip.Bed), 0.075f, 1.2f, 14f,
                   () => WindBed(7.93f),
                   clipName: EnvSoundClip.Bed, airLed: true, fireLit: false);
        else
            VRLog.Warn("Core", "ENV SOUND bed 'Draught' NOT CREATED — no 'WindowGlow', "
                               + "'WindowReveal' or 'WindowBars' node under the cellar. That is the "
                               + "window, and since ModBuild 223 it carries the ROOM'S ENTIRE "
                               + "RESTING AMBIENCE: the user asked for \"der Windzug im Keller "
                               + "könnte vom Fenster ausgehen\" and the continuous room tone that "
                               + "used to fill this gap was deleted in the same round. Without this "
                               + "node the cellar at rest is three candle flames and nothing else. "
                               + "If the bake renamed the window, rename it here.");

        // THE FIRES. Three sites, and they are NOT the candles above: a burning crate is not a big
        // candle. Until ModBuild 152 the only thing that answered a Fire infusion in this room was
        // the candle beds' gain, on a clip that was the window draught — the fires got their own
        // voice at 152 and the candles got theirs at 153, which is the other half of the same fault.
        // See THE FIRE and THE CANDLES.
        AddFireBeds(room, CellarFireSites, CellarFireNodes);

        // EARTH. No node of its own — it is the room itself settling, so it sits at the room root
        // and is silent until an Earth infusion is up. This is the one bed with no resting level:
        // a permanent subsonic rumble in a cellar would be a drone, not an atmosphere.
        AddBed("Rumble", room.transform, EnvSoundBank.Bank(EnvSoundClip.Rumble), 0.10f, 2f, 26f,
               () => 1.30f * ElementMood.Live(3),
               clipName: EnvSoundClip.Rumble, airLed: false, fireLit: false);

        // THERE IS NO 'Stone' BED, AND THE ABSENCE IS THIS ROUND'S WHOLE CELLAR WORK. From ModBuild
        // 154 to 222 a continuous room tone stood here at the room root with 180 degrees of spread.
        // USER, 2026-08-22: "Im Keller hören sich die Geräusche an wie Rauschen bei nem Fernseher."
        // It measured as television static against a white-noise control on all four axes and it was
        // the loudest thing in the room at rest. See THE ROOM TONES, DELETED for the table, the
        // ruling and why a quieter one was rejected rather than tried. The cellar's resting sound is
        // the WINDOW DRAUGHT above.
    }

    private static void BuildSwamp(GameObject room)
    {
        // THE CANOPY, AND SINCE ModBuild 223 IT IS THE WOOD'S WHOLE RESTING AMBIENCE. Leaves in the
        // wind — the same shared noise bed as the cellar's draught, gusting on a slow LFO.
        //
        // USER, 2026-08-22, verbatim: "im Wald ein ganz leiser dezenter Windzug". That sentence is
        // this emitter, and it is what replaced the 'NightAir' bed deleted this round: it plays at
        // WindRestFloor for the whole scenario (delivered 0.00076 at 4 perceived metres through a
        // 500 Hz high pass, 10.3 dB under the bed that went) and an Air infusion is the rise above
        // it. With Air down the canopy also still SWAYS, which is EnvRoom's own vertex motion and is
        // the "leichte Bewegung die aktuell Standard ist" the ModBuild 147 ruling asked to keep.
        //
        // A NOTE FOR THE NEXT ROUND, BECAUSE IT IS A REAL LIMITATION AND NOT AN OVERSIGHT: this bed
        // plays EnvSoundClip.Bed, whose body is 53.2% under 200 Hz — that is an APERTURE's spectrum
        // (air forced through a cellar window), and a canopy's is not. It has been that way since the
        // feature shipped and the user has never reported it. It is left alone here for the reason
        // CandleBedGain is left alone: this round changes WHEN the wind plays, and a round that
        // changed the clip in the same edit could not tell the next report which of the two it was
        // judging. If the wood's resting draught comes back as "too low" rather than "too loud",
        // THAT is the fix — a second clip, high-passed, the way THE CANDLE got one.
        Transform? canopy = Find(room.transform, "Canopy", "TrunksNear", "Ground");
        if (canopy != null)
            AddBed("Leaves", canopy, EnvSoundBank.Bank(EnvSoundClip.Bed), 0.070f, 2f, 22f,
                   () => WindBed(11.31f),
                   clipName: EnvSoundClip.Bed, airLed: true, fireLit: false);
        else
            VRLog.Warn("Core", "ENV SOUND bed 'Leaves' NOT CREATED — no 'Canopy', 'TrunksNear' or "
                               + "'Ground' node under the wood. Since ModBuild 223 that bed IS the "
                               + "wood's resting ambience (\"im Wald ein ganz leiser dezenter "
                               + "Windzug\"), and since ModBuild 226 deleted the insect chorus as "
                               + "well it is the ONLY continuous emitter this room has: without this "
                               + "node the wood is silent between animal calls. If the bake renamed "
                               + "the canopy shell, rename it here.");

        // THE GROUND. IT NO LONGER CARRIES A BED — the 'Night' insect chorus that stood on it from
        // the day this feature shipped until ModBuild 225 is DELETED, because it is the sound the
        // user calls rain: "Im Wald gefällt mir nur dieser 'Regen' Sound nicht der ab und zu kommt
        // und für eine Zeit bleibt, ansonsten finde ich es sehr gut." The schedule, the two controls,
        // the falsified alternatives and the with/without on the whole room are in THE INSECT CHORUS,
        // DELETED, with every number. Do not restore this bed; if crickets are wanted, they are a
        // different generator and they arrive as EVENTS, which that block spells out.
        //
        // THE NODE IS STILL RESOLVED, and not as a leftover: it is the FRAME the night calls' perch
        // ring is measured in (see _perchFrame below and WHERE A CALL COMES FROM). One lookup, one
        // warning, one node — which is also why the warning below no longer mentions a bed.
        //
        // A MISSING GROUND NODE IS LOUD. `Find` returning null here is the WispWisp path exactly.
        Transform? ground = Find(room.transform, "Ground", "RoomGeo");

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

        // THE FIRES. The snag at the root flare, the deadfall's middle seat and the brushwood — the
        // same three sites the bake lights the wood from.
        AddFireBeds(room, SwampFireSites, SwampFireNodes);

        AddBed("Rumble", room.transform, EnvSoundBank.Bank(EnvSoundClip.Rumble), 0.10f, 2f, 26f,
               () => 1.30f * ElementMood.Live(3),
               clipName: EnvSoundClip.Rumble, airLed: false, fireLit: false);

        // THERE IS NO 'NightAir' BED. ModBuild 154 put a continuous wash here, 221 shipped it, 222
        // rebuilt it out of the band that had made the wood read as a tractor — and the user then
        // rejected it for being continuous at all: "Auch die kontinuierlichen Sounds im Wald nerven
        // mich. Statt generrell durchgehende sounds zu machen lieber die Tierrufe und im Wald ein
        // ganz leiser dezenter Windzug." Both halves of that sentence are now the two lines above and
        // the night calls below. See THE ROOM TONES, DELETED.
        //
        // THE 222 REBUILD WAS NOT WASTED AND THIS IS WHY THE RECORD MATTERS: it is what proved the
        // tractor was the INSECT modulator and not the wash, by rendering both with and without.
        // That falsification is preserved in .planning/envsound-replica/room.py and must not be
        // re-derived.

        // THE NIGHT CALLS, AND SINCE ModBuild 223 THEY MOVE. User, 2026-08-22, verbatim:
        //
        //     "Im Wald mal ne Eule oder ähnliches die ruft (auch aus dem Wald hörbar, hier sollte
        //      die Position auch random wechseln)"
        //
        // WHAT THAT ASKS FOR AND WHY IT COULD NOT BE A NODE. ModBuild 222 seated the owl on 'Canopy'
        // and the bird on 'TrunksFar' — and the wood HAS NO PER-TREE NODES to move them between: the
        // bake WELDS the whole forest into three meshes (BuildEnvironmentRooms.cs:16065-16067,
        // `Weld(trunkA, ..., "TrunksNear", ...)` and its two siblings), every one of them placed at
        // the room's local origin with an identity transform. So 'Canopy' is not a tree, it is the
        // ROOM CENTRE with a forest-shaped mesh hanging off it, and both calls were coming from the
        // middle of the clearing — which is also why they never sounded like they were out in the
        // wood. A different node each call was never available; a different PLACE is.
        //
        // SO THE PERCH IS A RING, IN THE BAKE'S OWN COORDINATES. See NightCallPerch for the
        // arithmetic and the two bounds. The frame is the GROUND node, which the bake places at the
        // room's local origin with identity rotation and unit scale — so a position expressed in it
        // is in AUTHORED METRES, exactly the units ClearR and CanopyY below are written in, and this
        // file never has to know the room's placement, its art scale or the rig scale.
        // SINCE ModBuild 226 THIS IS THE ONLY CONSUMER OF `ground`. It used to be the second — the
        // insect chorus stood on the same node, and the lookup was shared so the two could not
        // disagree about which object the wood's floor is. That bed is deleted (see THE INSECT
        // CHORUS, DELETED) and the lookup stays exactly where it was, because what it answers now is
        // the more demanding of the two questions: a bed only needs A place, while the perch ring is
        // measured IN this transform's frame and lands in the wrong part of the wood if it resolves
        // to a different object.
        _perchFrame = ground;
        if (_perchFrame == null)
            VRLog.Warn("Core", "ENV SOUND night calls HAVE NO FRAME — no 'Ground' or 'RoomGeo' node "
                               + "under the wood, so there is nothing to measure the perch ring "
                               + "from and the owl and the bird will NOT SOUND AT ALL this session. "
                               + "They are the near and far halves of the 2026-08-22 request for "
                               + "\"mal ne Eule oder ähnliches die ruft\", and since the room tone "
                               + "was deleted at ModBuild 223 and the insect chorus at 226 they are "
                               + "ALMOST ALL the wood has — what is left beside them is the resting "
                               + "draught through the canopy and whatever the elements bring. If the "
                               + "bake renamed the ground, rename it here; nothing else in this room "
                               + "reads that node any more, so this line is the whole symptom.");
    }

    /// <summary>
    /// Stand a fire bed on each of the room's three sites, and RECORD WHAT HAPPENED — including for
    /// the sites that resolved to nothing, which is the whole point of the method existing rather
    /// than three lines in each room builder.
    ///
    /// <para>The modulator captures the site index only so the log can be read against it; every site
    /// reads the same <see cref="_fireGate"/>, because "the fires are lit" is one fact (see the
    /// field). What differs per site is the LFO period, and the three are non-commensurate with each
    /// other and with everything else in the file — item 6 of the class doc — so three roars in one
    /// room never come into phase.</para>
    /// </summary>
    private static void AddFireBeds(GameObject room, string[] siteNames, string[][] nodeNames)
    {
        // Non-commensurate breathing periods, one per site. These are SLOW (the buffer already puffs
        // at ~5 Hz); this is the fire being fed, not the flame flickering.
        float[] periods = { 8.17f, 12.29f, 17.53f };

        var sb = new StringBuilder(220);
        for (int s = 0; s < FireSites; s++)
        {
            Transform? at = Find(room.transform, nodeNames[s]);
            if (at == null)
            {
                // NOT SILENT ABOUT SILENCE. A room legitimately without one of these sites is
                // possible (the content lane may move a fire), but it is indistinguishable from a
                // renamed node, and this feature has already lost a bed for months to exactly that.
                sb.Append('[').Append(siteNames[s]).Append(": NO NODE — tried ")
                  .Append(string.Join("/", nodeNames[s])).Append("] ");
                continue;
            }

            float period = periods[s];
            Voice? v = AddBed("Fire" + siteNames[s], at, EnvSoundBank.Bank(EnvSoundClip.Roar),
                              FireBedGain, FireMinMeters, FireMaxMeters,
                              () => FireBed(period),
                              clipName: EnvSoundClip.Roar, airLed: false, fireLit: true,
                              // NO RUNTIME LOW PASS. See AddBed's lowPassHz: the 1150 Hz corner every
                              // other bed uses would delete the crackle, which comes out of this same
                              // source. The roar's band is baked into its buffer instead.
                              lowPassHz: 0f);
            _fireVoices[s] = v;
            sb.Append('[').Append(siteNames[s]).Append(" on '").Append(at.name).Append('\'');
            if (v == null)
                sb.Append(" NO SOURCE — see the warning above");
            sb.Append("] ");
        }
        _fireResolution = sb.ToString();
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

    /// <summary>
    /// Create one continuous source on a node and start it. Returns the voice, or null when nothing
    /// was created — the fire sites need the reference back so they can put their crackles through
    /// the same source (see <see cref="TickFire"/>).
    /// </summary>
    /// <param name="lowPassHz">Corner of the runtime low-pass filter, or 0 for NO FILTER AT ALL.
    /// Every bed but the fire's passes <see cref="BedLowPassHz"/>; the fire passes 0, and that is
    /// load-bearing rather than an optimisation. The 1150 Hz corner is what keeps a stationary bed
    /// out of the speech band, and it would also remove the CRACKLE — which is 71-87% 1-5 kHz and
    /// rides this same source. The fire's band separation is baked into its two clips instead (the
    /// roar is three-pole low-passed at 820 Hz in the generator, i.e. tighter than this filter), so
    /// nothing is given up; see <c>EnvSoundBank</c>'s THE FIRE.</param>
    /// <param name="clipName">WHICH clip this is, as a name — recorded on the voice so that
    /// <see cref="LogBuilt"/> can print it and <see cref="TickBeds"/> can check it. It is passed
    /// separately from the <see cref="AudioClip"/> because an <c>AudioClip</c> reference cannot say
    /// what it IS, and "which emitter plays the wind buffer" is the exact question two user reports
    /// have now turned on. REQUIRED, so that a new emitter cannot be added without answering it.</param>
    /// <param name="airLed">True if this bed's level is LED BY the Air element — i.e. it goes
    /// through <see cref="WindBed"/>. Every wind-clip bed must set it.
    ///
    /// <para><b>RENAMED FROM <c>airGated</c> AT ModBuild 223, because the old name stopped being
    /// true.</b> It meant "EXACTLY zero whenever Air is down", which was the ModBuild 147 ruling read
    /// literally; the user has now asked for a very quiet draught at rest in both rooms (see THE WIND
    /// GATE), so a wind bed rests at <see cref="WindRestFloor"/> instead of at zero and is never
    /// paused. The flag still marks exactly the same emitters and still drives exactly the same log
    /// lines and the same assertion — what changed is what the assertion asserts, and a field whose
    /// name contradicts it is how the next reader gets it wrong.</para></param>
    /// <param name="fireLit">True if a Fire infusion raises this bed. Recorded so the log can answer
    /// "what got louder when I infused Fire" without anybody reading the source.</param>
    /// <remarks>
    /// TWO PARAMETERS WENT AT ModBuild 223 AND BOTH WENT WITH THE ROOM TONES:
    /// <list type="bullet">
    ///   <item><c>spreadDegrees</c> had exactly one caller — the room tone, at 180 degrees, because
    ///   it was the one emitter here deliberately NOT meant to be locatable. That is the property the
    ///   user rejected ("Mach die meisten Sounds an eine Quelle in der Welt hörbar"), so there is no
    ///   caller left and every source is back on <see cref="NewVoice"/>'s 25.</item>
    ///   <item><c>lowPassOffHz</c> had exactly one caller too — the insect bed, whose 3000 Hz corner
    ///   fell back to 1150 whenever <c>[EnvSound] AmbienceBed</c> was off. That dial is deleted, and
    ///   the 1150 Hz corner it fell back to was never a setting: it was the defect the corner
    ///   repairs. With it goes <see cref="TickBeds"/>'s per-frame filter re-assert.</item>
    /// </list>
    /// Both are DELETED rather than left unused, because a parameter with no caller reads like a
    /// feature — which is what the wisp bed did for months.
    /// </remarks>
    private static Voice? AddBed(string name, Transform at, AudioClip? clip, float gain,
                                 float minMeters, float maxMeters, System.Func<float> modulate,
                                 EnvSoundClip clipName, bool airLed, bool fireLit,
                                 float lowPassHz = BedLowPassHz)
    {
        if (clip == null)
        {
            VRLog.Warn("Core", $"ENV SOUND bed '{name}' NOT CREATED — the bank has no clip for it. " +
                               "The rest of the ambience is unaffected; this one emitter is silent.");
            return null;
        }
        // A REFUSAL THAT SAYS SO. See MaxVoices: a silent return here looks exactly like a room that
        // has no such object, and this feature has already lost a bed for months to that ambiguity.
        //
        // COUNTED AGAINST THE POOL THAT IS ABOUT TO EXIST, not against Shots.Count. Every AddBed call
        // happens inside Build's room branch, which runs BEFORE BuildShotPool — so Shots.Count is
        // always 0 here and the old test compared the beds against the cap alone while claiming to
        // include the pool. Three voices is the difference between a cap that means what it says and
        // one that is quietly three too generous.
        if (Beds.Count + OneShotVoices >= MaxVoices)
        {
            VRLog.Warn("Core", $"ENV SOUND bed '{name}' on '{at.name}' REFUSED — the voice cap " +
                               $"({MaxVoices}) is already reached with {Beds.Count} bed(s) plus the " +
                               $"{OneShotVoices} one-shot voice(s) still to be built. This emitter " +
                               "will be SILENT for the " +
                               "whole session. Either raise MaxVoices with a reason or take an " +
                               "existing bed out; do not leave this line in a shipped build.");
            return null;
        }
        // THE ONE RULE THIS FEATURE HAS BROKEN TWICE, CHECKED WHERE IT IS DECLARED. Nothing that is
        // not wind may play the wind buffer without the Air gate: the user has ruled on this clip
        // twice ("Wind Geräusch nur wenn auch Wind aktiv ist, sonst kein Geräusch") and both times
        // the violation was invisible from a log. It cannot be a compile-time check — the gate lives
        // in a lambda — so it is a build-time one, and it is LOUD.
        if (clipName == EnvSoundClip.Bed && !airLed)
        {
            VRLog.Warn("Core", $"ENV SOUND bed '{name}' on '{at.name}' plays the WIND buffer " +
                               "(EnvSoundClip.Bed) but declares itself NOT Air-led. That is the " +
                               "ModBuild 152 defect exactly: the candle beds played this clip with " +
                               "no gate, so a wind was audible with Air fully off and got LOUDER " +
                               "with a Fire infusion. Either route its level through WindBed() and " +
                               "pass airLed: true, or give it a clip of its own — see the bank's " +
                               "THE CANDLE for what that costs (about a hundred lines of " +
                               "arithmetic). The check survives ModBuild 223's narrowing of the " +
                               "wind ruling unchanged: a resting draught is now allowed, but only " +
                               "through WindBed, which is the ONE place the resting floor and the " +
                               "Air rise are decided together. The bed is still created; this line " +
                               "is here so the next hardware log names the fault.");
        }

        var v = NewVoice(name, at, clip, gain, minMeters, maxMeters, loop: true,
                         lowPass: lowPassHz > 0f);
        if (v.LowPass != null)
            v.LowPass.cutoffFrequency = lowPassHz;
        v.Modulate = modulate;
        v.NodeName = at.name;
        v.Clip = clipName;
        v.AirLed = airLed;
        v.FireLit = fireLit;
        v.Source.volume = 0f;   // faded in by the first TickBeds — nothing ever starts at full

        // START EACH BED AT A DIFFERENT POINT IN ITS BUFFER. The flame, the draught and the leaves
        // deliberately SHARE one noise clip, and so do the THREE FIRE SITES since ModBuild 152 (see
        // EnvSoundBank: one source of noise plus two filters is the correct physical model and is
        // what stops the buffer being findable). Started at the same instant they would play the
        // identical sample stream — perfectly correlated, so they would sum coherently to about
        // +10 dB instead of the +5 dB of independent noise, and the three would collapse into ONE
        // audible source coming from three places at once. Offsetting by an irrational fraction of
        // the clip decorrelates them completely, for one float write. It matters MORE for the fires
        // than it ever did for the beds: three roars in one room are three copies of one 6 s buffer,
        // and a fire is something the player will walk around.
        if (clip.length > 0.01f)
            v.Source.time = clip.length * (0.3819660f * Beds.Count % 1f);

        v.Source.Play();
        Beds.Add(v);
        return v;
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
        else
        {
            TickNightCall(clock);
        }
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

    // ---- the night calls -------------------------------------------------------------------------
    //
    // USER REQUEST, ModBuild 221 hardware, verbatim:
    //
    //     "Dezenter Wind kann bleiben und ansonsten eventuell hier und da noch ein ruf von tieren
    //      (was man so im Wald in der Nacht hört)"
    //
    // ...AND THE FOLLOW-UP ON ModBuild 222, verbatim, which is what ModBuild 223 answers here:
    //
    //     "Statt generrell durchgehende sounds zu machen lieber die Tierrufe ... Im Wald mal ne Eule
    //      oder ähnliches die ruft (auch aus dem Wald hörbar, hier sollte die Position auch random
    //      wechseln)"
    //
    // TWO THINGS CHANGE AND BOTH ARE IN THAT SENTENCE. First, the calls are now carrying the room
    // rather than decorating it — the two continuous beds are deleted (see THE ROOM TONES, DELETED),
    // so NightCallSkip comes down and the wood calls more often. Second, and this is the whole of
    // the placement work below: THE POSITION MOVES. See NightCallPerch.
    //
    // "HIER UND DA" IS THE WHOLE SPECIFICATION AND IT IS A RATE, so it is set against the rates this
    // file already ships and the user has already judged: the drip is on 2.85 s (and its own doc
    // calls that the worst offender in the room), the rat on 26 s, the apparitions on 83 s. A call
    // roughly every MINUTE sits between the last two, which is where "here and there" lives — you
    // notice it, you do not wait for it, and you never learn its rhythm.
    //
    // THE SCHEDULE IS A PURE FUNCTION OF THE SHARED CLOCK, so every client hears the same call from
    // the same tree on the same frame with ZERO wire bytes. That is the drip's and the rat's property
    // and, SINCE ModBuild 226, the fire crackle's as well — this paragraph used to end by naming the
    // crackle as the one exception in the file, on the argument that a crackle marks no visual. The
    // user's ruling ("Genau wie die Easter-Eggs sollen auch die Sounds mit allen Mitspieler
    // synchronisiert sein") carves out no exceptions, so there is no longer one; see TickFire.
    // The argument for doing it here FIRST still stands and is worth keeping: a call is a much more
    // findable event than a crackle — it is 2.3 s long, it comes from a fixed tree and there is
    // nothing else in the room — so two players standing together hearing owls at different moments
    // would have been an obvious defect rather than a subtle one. Hence a slot index, not a walk.
    //
    // ...AND IT IS STILL A POISSON WAITING TIME, which is the point of doing it this way rather than
    // with the rat's uniform draw. EnvSoundSchedule.PoissonGap turns ONE hash draw into an
    // exponential gap, whose mode is at zero — so calls CLUSTER (two owls close together, then a long
    // quiet) instead of arriving on a wobbly metronome. That distinction is not decoration in this
    // file: the ice sound beat at a fixed 0.45 s, the user called it "super nervig", and the whole
    // cue was deleted rather than re-timed. He has now used the same word about the wood.
    //
    // THE BOUNDS MAKE THE SLOT SAFE BY CONSTRUCTION. PoissonGap is clamped to
    // mean * [0.28, 2.60], so with NightCallMean = 15.5 s the offset is always inside
    // 4.34..40.30 s and therefore always inside the 41 s slot — the call can never land in the next
    // slot's window or before the current one opens, and no branch below has to test for it.

    /// <summary>The night calls' slot, in shared-clock seconds, and the mean of the Poisson offset
    /// inside it. 41 s is prime and non-commensurate with the rat's 26, the drip's 2.85, the haunt's
    /// 83 and every LFO period in this file — item 6 of the class doc. The mean is chosen so that
    /// <see cref="EnvSoundSchedule.PoissonGapMax"/> x mean (40.30 s) still fits inside the slot; see
    /// the block above.</summary>
    private const float NightCallSlot = 41f;
    private const float NightCallMean = 15.5f;

    /// <summary>The share of slots in which no animal calls at all. <b>0.30 -&gt; 0.22 at ModBuild
    /// 223</b>, so the realised mean gap goes from about 41 / 0.70 = 59 s to 41 / 0.78 = <b>53 s</b>.
    /// A skipped slot is not a missing sound: a wood in which something calls every single minute on
    /// the minute is a wood with a clock in it, and the skip is what makes the long quiets long
    /// enough to be quiet.
    ///
    /// <para>IT COMES DOWN BECAUSE THE CALLS ARE NOW THE ROOM. "Statt generrell durchgehende sounds
    /// zu machen lieber die Tierrufe" — with the wash deleted and the insects then intermittent,
    /// these are most of what the wood has, and 59 s of silence between them was authored when there
    /// was a continuous bed underneath. <b>THEY ARE NOW ALL OF IT.</b> ModBuild 226 deleted the
    /// insect chorus too (THE INSECT CHORUS, DELETED), so besides the resting draught and whatever
    /// the elements bring, these two calls are what the wood is. The skip and the mean were NOT
    /// re-tuned in that round on purpose: he wrote "ansonsten finde ich es sehr gut" about the build
    /// these numbers shipped in, and moving them would put a rate he has approved back in play in
    /// the same edit that removes the thing he objected to. <b>THE SLOT ITSELF IS DELIBERATELY NOT TOUCHED</b>: 41 s is what
    /// makes <see cref="EnvSoundSchedule.PoissonGapMax"/> x <see cref="NightCallMean"/> = 40.30 s fit
    /// inside a slot by construction, and moving it would put that proof back in play for a 10%
    /// change in rate. One number for one decision.</para></summary>
    private const float NightCallSkip = 0.22f;

    /// <summary>The share of calls that are the OWL rather than the far bird. Slightly more than
    /// half, because the owl is the near, low, two-second one and is the sound the request is most
    /// obviously about; the bird is the other end of the band so the room does not repeat itself.
    /// </summary>
    private const float NightCallOwlShare = 0.55f;

    /// <summary>
    /// What the two calls are played at, and how far they carry. <b>ALL SIX NUMBERS MOVED AT ModBuild
    /// 223, AND THEY MOVED TOGETHER SO THAT THE DELIVERED LEVEL DID NOT GO UP.</b>
    ///
    /// <para><b>WHY THE ROLLOFF MINIMUM HAD TO GROW.</b> Until this round both calls sounded from a
    /// welded mesh's transform at the room's local origin, i.e. from the middle of the clearing,
    /// typically 2.2-7.6 perceived metres from the head. They now sound from a RING out in the trees
    /// (see <see cref="NightCallPerch"/>) — 6.1-10.4 perceived m for the owl and 8.7-15.6 for the
    /// bird, before the player's own position is added. On the old 2.5 m minimum an owl at 10 m would
    /// have been 12 dB down, i.e. moved out to a tree and then muted for having moved. The minimum is
    /// what a real distant call HAS: an owl three hundred metres off is loud, and it is DIRECTION and
    /// not level that tells you where it is. 8 m and 10 m put the ring largely inside the flat part
    /// of Unity's curve, so which tree it is comes from the spatialiser's pan and the level stays
    /// roughly constant — which is also what makes "auch aus dem Wald hörbar" true.</para>
    ///
    /// <para><b>AND THE GAINS CAME DOWN BY WHAT THE MINIMA GAVE BACK.</b> Effective level is
    /// <c>gain x min(1, minMeters / distance)</c>, so:</para>
    /// <code>
    ///                 gain    min m    distance     effective       mean
    ///   Owl  MB222   0.075     2.5     2.2..7.6    0.075..0.025     0.041
    ///   Owl  223     0.050     8.0     3.9..12.6   0.050..0.032     0.041
    ///   Bird MB222   0.060     2.0     2.2..7.6    0.055..0.016     0.026
    ///   Bird 223     0.032    10.0     7.5..17.5   0.032..0.018     0.026
    /// </code>
    /// <para>The MEAN is held to two decimal places in both cases and the PEAK comes down by 3.5 dB
    /// (owl) and 4.6 dB (bird) — the loudest a call can now be is quieter than the loudest it could
    /// be before, and it varies far less. That is the honest way to move an emitter's position after
    /// three rounds of rejected levels: nothing gets louder anywhere.</para>
    ///
    /// <para>Both minima are well past <see cref="FireMinMeters"/>'s argument about near-field
    /// minima — a call authored at the candles' 0.6 m would be inaudible two steps away, which is the
    /// fault that doc spells out at length.</para>
    /// </summary>
    private const float OwlGain = 0.050f;
    private const float OwlMinMeters = 8f;
    private const float OwlMaxMeters = 40f;
    private const float BirdGain = 0.032f;
    private const float BirdMinMeters = 10f;
    private const float BirdMaxMeters = 34f;

    /// <summary>Hash channels for the SEVEN independent decisions one call needs: whether the slot
    /// is silent, WHEN inside it, WHICH animal, its pitch, and — since ModBuild 223 — WHERE, as an
    /// azimuth, a radius and a height on the perch ring.
    ///
    /// <para>A CHANNEL IS NOT AN EXCLUSIVE RESOURCE — <see cref="DripVariantChannel"/>'s doc makes
    /// the argument in full — but draws that are all compared BY THE EAR on one event must not share
    /// one, or (say) the latest call in every slot would forever be the highest-pitched owl in the
    /// nearest tree. These seven are distinct FROM EACH OTHER, which is the property that matters,
    /// and seven is exactly what <c>Haunt.Hash</c>'s documented 0..7 range affords.</para>
    ///
    /// <para>THAT RANGE IS NOW FULL, and the next round that wants an eighth per-call decision has to
    /// solve that rather than invent a channel 8: the range is a MIRROR of
    /// <c>EnvHaunt.cginc</c>'s channel table (:186-192) and a constant that exists on only one side
    /// of a contract this project checks is a defect waiting for a shader edit. The way out is to
    /// derive two decisions from one draw (an azimuth and a radius from one hash's high and low
    /// bits, say), not to widen the table.</para>
    ///
    /// <para>THE OVERLAPS WITH OTHER SUBSYSTEMS ARE UNCHANGED AND STILL SOUND: the DRIP and the RAT
    /// are cellar-only and can never run in the same session's room as these; the INSECT CHORUS
    /// indexes a 53 s slot and the APPARITIONS an 83 s one, against this schedule's 41 s — two values
    /// that are never compared cannot be seen to correlate.</para>
    /// </summary>
    private const float NightCallSkipChannel = 3f;
    private const float NightCallWhenChannel = 4f;
    private const float NightCallWhichChannel = 6f;
    private const float NightCallPitchChannel = 7f;
    private const float NightCallAzimuthChannel = 0f;
    private const float NightCallRadiusChannel = 1f;
    private const float NightCallHeightChannel = 2f;

    // ---- WHERE A CALL COMES FROM -------------------------------------------------------------------
    //
    //  USER, 2026-08-22, verbatim: "Im Wald mal ne Eule oder ähnliches die ruft (auch aus dem Wald
    //  hörbar, hier sollte die Position auch random wechseln)".
    //
    //  THE OBVIOUS IMPLEMENTATION IS NOT AVAILABLE, and finding that out is most of this block. "A
    //  different tree each time" wants a set of tree nodes to draw from, and THE WOOD HAS NONE: the
    //  bake grows every trunk into one of two accumulators and WELDS each into a single mesh —
    //  BuildEnvironmentRooms.cs:16065-16067 — so the room's hierarchy contains 'TrunksNear',
    //  'TrunksFar' and 'Canopy' and nothing per-tree, and all three sit at the room's local origin
    //  with an identity transform. ModBuild 222 seated the owl on 'Canopy' believing that was the
    //  canopy's position; it was the middle of the clearing.
    //
    //  SO THE PERCH IS A POINT ON A RING, DERIVED FROM THE BAKE'S OWN NUMBERS. Three constants are
    //  MIRRORED here exactly as DripPeriod and RatPeriod are, and for the same reason — they are a
    //  contract with the room builder, and a sound that invented its own geometry would drift away
    //  from the picture:
    //
    //      ClearR = 5.4 m      the open ground around the board (BuildEnvironmentRooms.cs:14503,
    //                          "open ground around the board"), i.e. the radius inside which there
    //                          are no trees at all;
    //      trunks out to 27 m  AddForest places each tree at r = Lerp(ClearR, 27, sqrt(u)), which is
    //                          area-uniform over the annulus (:15995);
    //      CanopyY(r) = 6.8 + 0.30 * (r - ClearR)     the canopy's height at radius r (:14763).
    //
    //  HOW THE TWO BOUNDS ARE ENFORCED, which is the question this design has to answer:
    //
    //    * A CALL CAN NEVER COME FROM INSIDE THE PLAYER. The inner radius is 7.0 authored metres,
    //      which is 1.6 m OUTSIDE ClearR — the player and the board are on the clearing floor, whose
    //      radius is 5.4 m, so the closest a call can be to a player standing at the very edge of
    //      the open ground is 1.6 authored m (~1.4 perceived m), and to one at the board about 7 m.
    //      It is a bound on the GEOMETRY rather than a test against the head, and that is deliberate:
    //      see the multiplayer note below. Belt and braces on top of it, the rolloff is FLAT inside
    //      OwlMinMeters = 8 perceived m, so even the closest possible perch cannot be louder than
    //      the farthest — a call has no near field to be inside of.
    //    * A CALL CAN NEVER COME FROM OUTSIDE THE ROOM. The outer radii are 12 m (owl) and 18 m
    //      (bird) against a trunk field that runs to 27 m and a canopy that has closed over by 19 m
    //      (CanopyMask's outer term, :14847), so both rings are inside the visible wood with room to
    //      spare. The height is a fraction of CanopyY at the drawn radius, so a call is always
    //      between the ground and the branches over it and never above the canopy.
    //
    //  MULTIPLAYER: EVERY DRAW IS A PURE FUNCTION OF THE SLOT INDEX AND THE BAKE. Nothing here reads
    //  the head, the rig scale, the zoom or Time.time, so two clients place the same call at the same
    //  point in the same room on the same frame with ZERO wire bytes — which is this file's contract
    //  for every scheduled event and the reason a "pick the nearest tree that is not too close to the
    //  listener" filter was rejected outright: the listener is per-client, so that would have put two
    //  players' owls in different trees, which is exactly the kind of disagreement a 2.3 s call from
    //  a fixed direction makes obvious.
    //
    //  AND IT IS THE GROUND NODE'S FRAME, not the room root's. The bake places 'Ground' at the room's
    //  local origin with identity rotation and unit scale (:15878), so TransformPoint on it converts
    //  authored metres to world units through whatever placement, art scale and yaw the room happens
    //  to have — none of which this file has to know, and all of which would have to be re-derived if
    //  the ring were built in world units.

    /// <summary>The perch rings, in the bake's AUTHORED metres, measured from the clearing's centre.
    /// The owl is the near animal and the bird the far one, which is what the two gain/rolloff pairs
    /// above assume. Both inner radii are outside <see cref="ForestClearRadiusMeters"/>; see the
    /// block above for both bounds.</summary>
    private const float OwlPerchNearMeters = 7f;
    private const float OwlPerchFarMeters = 12f;
    private const float BirdPerchNearMeters = 10f;
    private const float BirdPerchFarMeters = 18f;

    /// <summary>How high up the canopy a call comes from, as a fraction of
    /// <see cref="ForestCanopyY"/> at the drawn radius. The owl sits in the crown of the trunks and
    /// the bird higher and thinner, which is also where their two bands put them. Never 0 (a call
    /// from the ground is a footstep) and never 1 (a call from above the canopy is a call from the
    /// sky).</summary>
    private const float OwlPerchHeightLo = 0.50f;
    private const float OwlPerchHeightHi = 0.80f;
    private const float BirdPerchHeightLo = 0.70f;
    private const float BirdPerchHeightHi = 0.95f;

    /// <summary>MIRRORED from <c>BuildEnvironmentRooms.cs</c>: <c>ClearR</c> (:14503, "open ground
    /// around the board") and <c>CanopyY</c> (:14763). They are a CONTRACT with the room builder in
    /// exactly the sense <see cref="DripPeriod"/> is — if the bake opens the clearing up or lifts the
    /// canopy, these move with it or the owl ends up in a tree that is not there.</summary>
    private const float ForestClearRadiusMeters = 5.4f;
    private static float ForestCanopyY(float radiusMeters) =>
        6.8f + 0.30f * (radiusMeters - ForestClearRadiusMeters);

    /// <summary>
    /// WHERE THIS SLOT'S CALL COMES FROM, in world space — a point on the perch ring, drawn from
    /// three hash channels and therefore identical on every client. See WHERE A CALL COMES FROM.
    /// </summary>
    /// <param name="slot">The 41 s call slot. The ONLY input, which is what makes this shared.</param>
    /// <param name="owl">True for the near ring, false for the far one.</param>
    /// <param name="where">The world position, valid only when this returns true.</param>
    /// <returns>False when the wood has no frame to measure from, in which case
    /// <see cref="BuildSwamp"/> has already said so loudly and the caller must not sound the
    /// call — a call at the world origin would be somewhere under the table.</returns>
    private static bool NightCallPerch(long slot, bool owl, out Vector3 where)
    {
        where = Vector3.zero;
        if (_perchFrame == null)
            return false;

        float near = owl ? OwlPerchNearMeters : BirdPerchNearMeters;
        float far = owl ? OwlPerchFarMeters : BirdPerchFarMeters;

        // AREA-UNIFORM IN THE ANNULUS, exactly as the bake seats the trees themselves
        // (`r = Lerp(ClearR, 27, sqrt(u))`, :15995). A linear draw would crowd the calls into the
        // near ring, because an annulus has more area the further out you go — and the audible
        // consequence would be that the wood's animals all sound close, which is the opposite of
        // "auch aus dem Wald hörbar".
        float u = Haunt.Hash(slot, NightCallRadiusChannel);
        float radius = Mathf.Lerp(near, far, Mathf.Sqrt(Mathf.Clamp01(u)));

        float azimuth = 2f * Mathf.PI * Haunt.Hash(slot, NightCallAzimuthChannel);
        float hLo = owl ? OwlPerchHeightLo : BirdPerchHeightLo;
        float hHi = owl ? OwlPerchHeightHi : BirdPerchHeightHi;
        float height = ForestCanopyY(radius)
                       * Mathf.Lerp(hLo, hHi, Haunt.Hash(slot, NightCallHeightChannel));

        // AUTHORED METRES IN, WORLD UNITS OUT. The frame carries the room's placement, its yaw and
        // its art scale, so nothing above had to know any of the three — see the block's last
        // paragraph.
        where = _perchFrame.TransformPoint(new Vector3(radius * Mathf.Cos(azimuth),
                                                       height,
                                                       radius * Mathf.Sin(azimuth)));
        return true;
    }

    /// <summary>The last slot this client has already answered, so a call fires once and not once per
    /// frame. <c>long.MinValue</c> is "not observing yet" — and the FIRST slot observed is deliberately
    /// swallowed, exactly as the drip's and the rat's are: the call that belongs to the slot the
    /// player walked in during has already happened.</summary>
    private static long _lastNightCallSlot = long.MinValue;

    /// <summary>
    /// THE NIGHT CALLS — an owl in the canopy, a small bird further off, "hier und da". See the block
    /// above for the rate, the clock and why this is a slot index rather than the fire crackle's
    /// walk.
    /// </summary>
    private static void TickNightCall(float clock)
    {
        // THE CALL SLOT, through EnvSoundSchedule.TrySlot rather than an inline cast — see TickDrip
        // for the sentinel collision that motivated moving this arithmetic into one place.
        if (!EnvSoundSchedule.TrySlot(clock, NightCallSlot, out long slot))
            return;
        if (slot == _lastNightCallSlot)
            return;

        // THE CALL'S INSTANT INSIDE THIS SLOT. An exponential waiting time from one hash draw, and
        // bounded by PoissonGap's own clamp to 4.34..40.30 s — inside the 41 s slot by construction,
        // so this cannot schedule into the next slot and cannot fire on the slot boundary.
        float at = NightCallSlot * slot
                   + EnvSoundSchedule.PoissonGap(NightCallMean,
                                                 Haunt.Hash(slot, NightCallWhenChannel));
        if (clock < at)
            return;

        bool first = _lastNightCallSlot == long.MinValue;
        _lastNightCallSlot = slot;
        if (first)
            return;   // that call already happened before we started listening

        // A SKIPPED SLOT IS A SLOT NOTHING CALLED IN. Silence is the correct sound, and it is drawn
        // AFTER the slot is latched so a skipped slot still advances the schedule.
        if (Haunt.Hash(slot, NightCallSkipChannel) < NightCallSkip)
            return;

        bool owl = Haunt.Hash(slot, NightCallWhichChannel) < NightCallOwlShare;

        // A DIFFERENT TREE EVERY CALL — "hier sollte die Position auch random wechseln". Drawn from
        // the slot index and the bake's own geometry and from nothing else, so every client puts this
        // call in the same place on the same frame with no wire bytes. See WHERE A CALL COMES FROM
        // for the two bounds (never inside the player, never outside the wood).
        if (!NightCallPerch(slot, owl, out Vector3 from))
            return;   // BuildSwamp already warned, loudly, once — this path must not warn per event

        // +-4% of pitch, off a channel of its own so the quietest realisation is not locked to the
        // lowest note forever. Narrow, for MakeDrips' reason: AudioSource.pitch resamples the WHOLE
        // clip, and a wide setting transposes an owl into a pigeon.
        float pitch = 0.96f + 0.08f * Haunt.Hash(slot, NightCallPitchChannel);
        PlayShot(EnvSoundBank.Bank(owl ? EnvSoundClip.Owl : EnvSoundClip.NightBird),
                 from,
                 owl ? OwlGain : BirdGain,
                 owl ? OwlMinMeters : BirdMinMeters,
                 owl ? OwlMaxMeters : BirdMaxMeters,
                 pitch);
    }

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

        // Fire once per (start, card), and on TWO SEPARATE LATCHES — one for the events the shared
        // schedule produces and one for the Advanced menu's forced ones.
        //
        // WHY TWO, AND IT IS THE ANSWER TO A REPORTED DEFECT. Player.log:8629 and :8691 both schedule
        // the SAME shelf event (start 432.56s), 15 s apart, and both then have every contact dropped
        // as stale. The single latch had not failed on its own terms — it had been OVERWRITTEN. The
        // tester was using the test buttons: he latched apparition 0 (:8563, :8598), which wrote
        // (430.45, card 0) and then (437.35, card 0) over the latch; released it at 441.28, at which
        // point Resolve went back to the real schedule and returned card 5 — a different pair, so it
        // fired; then latched apparition 1 at 447.73 (another overwrite) and released THAT at 456.03,
        // at which point card 5 was still running and was, by the same argument, a different pair
        // again. A forced event is a different STREAM of events and must not be able to make the
        // scheduled stream forget what it has already played.
        //
        // ONE LATCH PER STREAM IS ENOUGH, and that is a property of the schedule rather than an
        // assumption: scheduled events never overlap (the bake measures the shortest quiet gap
        // between the end of one and the start of the next at 27 s), so between two visits to the
        // same scheduled event there can be no OTHER scheduled event to evict it.
        bool seen = slot.Forced
            ? Mathf.Approximately(_lastForcedStart, slot.StartClock) && _lastForcedCard == slot.Card
            : Mathf.Approximately(_lastHauntStart, slot.StartClock) && _lastHauntCard == slot.Card;
        if (seen)
            return;
        // ...and never fire for an event that has already finished — which is what would otherwise
        // happen on the frame the environment stands up in the middle of a slot.
        float runs = Haunt.CardSeconds(style, slot.Card) * slot.DurationMul;
        if (clock > slot.StartClock + runs + 1.5f)
            return;

        if (slot.Forced)
        {
            _lastForcedStart = slot.StartClock;
            _lastForcedCard = slot.Card;
        }
        else
        {
            _lastHauntStart = slot.StartClock;
            _lastHauntCard = slot.Card;
        }

        // A SILENT CARD IS DEBOUNCED LIKE ANY OTHER and then simply makes no sound — the lines above
        // have already run. Doing it in that order rather than returning early is what keeps one
        // apparition equal to at most one visit to this code, so a silent card cannot re-enter on the
        // next frame and cannot schedule anything twice.
        if (!audible)
            return;

        // ================================ THE MID-FLIGHT JOIN =========================================
        //
        //  ALL OF THE EVENT OR NONE OF IT. Until ModBuild 150 this method would fire a cue up to the
        //  event's whole run plus 1.5 s late, on the reasoning that a cue is better than silence when
        //  the environment stands up in the middle of a slot. The bookshelf disproved it, and the
        //  proof is in the user's log rather than in an argument: at Player.log:8630 the creak fired
        //  8.7 s into a 25 s event, and the contacts it then scheduled were ABSOLUTE times on the
        //  shared clock (which is right — see ScheduleShelfContacts) that had ALREADY PASSED. Three
        //  lines later all of them were dropped as stale (:8631, :8632). So the player heard the
        //  bookcase begin to lean and then never heard it land, twice, which is worse than either
        //  hearing the whole thing or hearing nothing: a cue with no consequence is a cue that says
        //  the feature is broken.
        //
        //  So the cue's own lateness is now BUDGETED, and the budget is DeferredStaleSeconds — the
        //  same constant the deferred queue drops on — because that is what makes the whole event
        //  coherent by construction rather than by coincidence. If the lead cue is inside its budget,
        //  every contact behind it is at least (its own phase offset - the budget) ahead of the
        //  clock: for the shelf the nearest is the arrival at +4.5 s, so it is still 2.4 s in the
        //  future and the queue cannot drop it. If the lead cue is outside the budget, nothing at all
        //  is played for this event.
        //
        //  IT IS DEBOUNCED FIRST, deliberately: the event is latched above, so a skipped event is
        //  skipped ONCE and cannot be reconsidered on the next frame as the clock walks further past
        //  it. And it is logged once, because "the shelf fell and I heard nothing" has to be
        //  attributable to a decision rather than to a hole.
        if (clock > at + DeferredStaleSeconds)
        {
            VRLog.Info("Core", $"ENV SOUND {style} card {slot.Card} SKIPPED ENTIRELY — this client " +
                               $"reached the event {clock - at:F2}s after its cue was due (cue at " +
                               $"{at:F2}s, event starts {slot.StartClock:F2}s and runs {runs:F2}s, " +
                               $"clock {clock:F2}s), which is past the {DeferredStaleSeconds:F1}s " +
                               "budget. Joining an event in flight means every contact behind the " +
                               "lead cue is already in the past, and a creak with no landing behind " +
                               "it is worse than silence — so the WHOLE event is silent. Nothing is " +
                               "broken; the next event plays in full. This is normally the " +
                               "environment standing up mid-slot, or an Advanced-menu latch being " +
                               "released over a scheduled event that was already running.");
            return;
        }

        Vector3 pos = HauntPosition(slot.Card);

        // The cellar's bookshelf (card 5) is the one apparition whose sound is not a hint but a
        // physical consequence — it tips over, hits a stone floor and later rights itself. Its cues
        // ALSO come off the shelf's own node rather than off the apparition catalogue: the creak is
        // the carcass taking the lean, so it sounds from the middle of the standing body, and the
        // contacts sound from the floor (ScheduleShelfContacts). Before ModBuild 150 both came from
        // the catalogue's bounds centre, i.e. from the middle of the room.
        bool shelf = style == SkyStyle.Cellar && slot.Card == 5;
        Vector3 cueAt = shelf ? ShelfCarcass(pos) : pos;

        PlayShot(EnvSoundBank.Bank(clip), cueAt, gain, minM, maxM, 1f);

        if (shelf)
            ScheduleShelfContacts(slot.StartClock, runs, clock, pos);

        VRLog.Info("Core", $"ENV SOUND haunt cue: {style} card {slot.Card} -> {clip} at shared clock " +
                           $"{clock:F2}s, lead {lead:+0.00;-0.00}s on an event that starts " +
                           $"{slot.StartClock:F2}s and runs {runs:F2}s" +
                           (slot.Forced ? " — FORCED from the Advanced menu" : " — scheduled") +
                           $". Gain {gain:F3} before master; position {cueAt:F2}" +
                           (shelf ? " (the SHELF's own carcass, not the apparition catalogue)" : "") +
                           ".");
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
        _perchFrame = null;
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
          .Append(_ourListener != null
                      ? "moved onto GloomhavenVR.HeadCamera"
                      : "NOT taken — no head camera, so nothing here will be audible")
          .Append(", ").Append(_suppressed.Count)
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
              .Append("canopy draught and nothing else; everything else here is an EVENT (the two ")
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
              .Append(BirdPerchFarMeters.ToString("F0")).Append(" m out. FRAME: ")
              .Append(_perchFrame != null ? "'" + _perchFrame.name + "'" : "NO NODE")
              .Append(_perchFrame == null
                          ? " — NEITHER CALL WILL SOUND AT ALL this session; see the warning above. "
                          : ", the bake's ground plane, so the ring is in AUTHORED metres and this ")
              .Append(_perchFrame == null ? "" :
                      "file never has to know the room's placement, art scale or yaw. ")
              .Append("EVERY CALL IS RE-PLACED: azimuth, radius (area-uniform in the annulus, as ")
              .Append("the bake seats the trees themselves) and height (a fraction of the canopy at ")
              .Append("that radius) are three hash channels off the SLOT INDEX and nothing else, so ")
              .Append("both players hear the same animal from the same tree on the same frame with ")
              .Append("ZERO wire bytes. BOUNDED BY CONSTRUCTION: the inner radius is outside the ")
              .Append(ForestClearRadiusMeters.ToString("F1"))
              .Append(" m of open ground the board and the player stand on, so a call can never come ")
              .Append("from inside the player — and the rolloff is flat inside ")
              .Append(OwlMinMeters.ToString("F0"))
              .Append(" m anyway, so the nearest possible perch is no louder than the farthest; the ")
              .Append("outer radii sit inside a trunk field that runs to 27 m and a canopy that has ")
              .Append("closed by 19 m, so a call can never come from outside the wood. RATE: one ")
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
