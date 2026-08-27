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
internal static partial class EnvSound
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

    /// <summary>
    /// This file's name in <see cref="HeadEar"/>'s claim set. The listener itself moved to
    /// <c>Core/HeadEar.cs</c> at ModBuild 297 because spatial voice chat needs the SAME ear and the
    /// two features start and stop on completely different schedules — read that file's class doc
    /// for the whole argument, including why the ear being private to this one feature was a defect
    /// rather than a tidy-up. The behaviour of this file is unchanged: it takes the ear in Build(),
    /// hands it back in StandDown(), and keeps it across a style rebuild.
    /// </summary>
    private const string EarClaim = "EnvSound";

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

    /// <summary>
    /// THE CELLAR'S WINDOW, AS AN ACOUSTIC SOURCE — ModBuild 296.
    ///
    /// <para>USER, verbatim: <i>"Die Tiergeräusche kannst du von diesem Wald aus auch triggern, in
    /// der selben Intensität wie im Wald, d.h. im Keller hört man sie weniger und immer von dem
    /// Fenster aus lokalisiert."</i> That sentence contains its own mechanism and it is worth taking
    /// apart, because two of its three clauses are constraints on the implementation:</para>
    /// <list type="bullet">
    /// <item><b>"in der selben Intensität wie im Wald"</b> — the SOURCE is unchanged. Same clips,
    /// same 41 s slot, same deck, same skip rate, same authored <c>Gain</c> per animal. Nothing in
    /// <see cref="NightCalls"/> is touched and there is no "cellar volume" anywhere in this file.</item>
    /// <item><b>"im Keller hört man sie weniger"</b> — the reduction is the WALL and the DISTANCE,
    /// and it is computed, not chosen. See <see cref="WindowInsertion"/>.</item>
    /// <item><b>"immer von dem Fenster aus lokalisiert"</b> — this is the one that decides the
    /// architecture, and it rules out the obvious implementation.</item>
    /// </list>
    ///
    /// <para><b>WHY THE EMITTER IS AT THE WINDOW AND NOT OUT IN THE WOOD.</b> The obvious reading of
    /// the request is "put the animals in the forest outside and let the distance model do the
    /// rest". That fails the third clause outright, and by a lot. The wood's near band stands 8.5 m
    /// beyond a wall 4.5 m from the room's centre; an animal on the perch ring at 10 authored m and
    /// 60 degrees off the window's normal is 55 degrees away from the window as seen from the seat.
    /// A player would hear a fox through the east wall. THE WINDOW IS THE ONLY DIRECTION THE SOUND
    /// CAN COME FROM, and that is not a cheat but the physics: an opening in a heavy wall is a
    /// SECONDARY SOURCE. Everything outside reaches the room through that hole and re-radiates from
    /// it, which is exactly why a fox two hundred metres off, heard through a slot, is localised at
    /// the slot and not at the fox. The user's sentence is a correct description of the acoustics and
    /// the implementation follows it rather than the geometry.</para>
    ///
    /// <para>So: the perch is still drawn — the same three hash channels off the same slot index,
    /// on the same per-animal rings — and it is used for ONE thing, the length of the outside leg.
    /// The shot is then played from this node.</para>
    ///
    /// <para>It is <c>WindowGlow</c>, resolved by EXACT name: the bake places that node at the
    /// opening's own centre, 6 cm inside the inner face (BuildEnvironmentRooms, "WindowGlow"), which
    /// is the mouth a player sees. The cellar's draught bed already stands on it. Null means the
    /// bake renamed or dropped the window, and then NO ANIMAL SOUNDS IN THIS ROOM — see
    /// <see cref="BuildCellar"/>, which says so once and loudly.</para>
    /// </summary>
    private static Transform? _windowMouth;

    /// <summary>True while the perch ring may only be drawn on the half-plane OUTSIDE the wall.
    /// In the wood a call comes from any bearing; through a cellar window the wood is only on one
    /// side, and an animal drawn at a bearing behind the player would be standing in the room. It
    /// changes the DISTANCE distribution and nothing else — the direction is the window's either
    /// way — but a fox 12 m inside the cellar is not a distance this file should ever compute.</summary>
    private static bool _perchOutwardOnly;

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
        // ruled out ("verortbar von seinen entsprechenden Quellen" — "locatable, coming from
        // their respective sources").
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

        // Cleared before either builder runs, so a style change cannot leak the cellar's window
        // routing into the wood (where the animals ARE the source) or the wood's whole-circle perch
        // ring into the cellar. Teardown clears them too; this is the belt to that's braces.
        _windowMouth = null;
        _perchOutwardOnly = false;

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

        // ---- THE WOOD BEYOND THE WINDOW, AND THE WINDOW IT IS HEARD THROUGH -----------------------
        // USER, ModBuild 296: "Die Tiergeräusche kannst du von diesem Wald aus auch triggern, in der
        // selben Intensität wie im Wald, d.h. im Keller hört man sie weniger und immer von dem
        // Fenster aus lokalisiert."
        //
        // TWO NODES, AND THEY ANSWER TWO DIFFERENT QUESTIONS. That is why they are two lookups and
        // not one:
        //   * 'WindowWood' is the FRAME the perch ring is measured in — the bake puts it at the
        //     wall's OUTER face at OUTSIDE GROUND level with identity rotation and unit scale
        //     (BuildEnvironmentRooms.AddWoodOutsideWindow), so a position expressed in it is in
        //     authored metres on the ground the trees stand on, and +Z is out through the window.
        //     It is a marker with no geometry on purpose: a node that carried a mesh could be moved
        //     by a later round for a visual reason and would silently move every fox in the room.
        //   * 'WindowGlow' is WHERE THE SOUND COMES OUT — the opening's own mouth. Exact name, for
        //     the reason the bookcase's lookup is exact: the room also carries 'WindowReveal' and
        //     'WindowBars', and both of those are welded meshes placed at the ROOM'S ORIGIN with an
        //     identity transform, so a prefix search or a fallback would put every animal in the
        //     middle of the cellar floor. (The 'Draught' bed above takes those two as fallbacks and
        //     has that latent fault; it is a bed and only needs A place, so it is left alone and
        //     recorded here rather than changed in a round about something else.)
        //
        // A MISSING NODE MEANS SILENCE, NOT A GUESS. "It must not fire when the window is not part
        // of the room" is a requirement, and the only way to honour it is to refuse: with either
        // node absent _perchFrame stays null, TickNightCall returns at its first gate, and this room
        // sounds exactly as it did before this round.
        Transform? wood = Find(room.transform, "WindowWood");
        _windowMouth = Find(room.transform, "WindowGlow");
        if (wood != null && _windowMouth != null)
        {
            _perchFrame = wood;
            _perchOutwardOnly = true;
        }
        else
        {
            _perchFrame = null;
            _windowMouth = null;
            VRLog.Warn("Core", "ENV SOUND cellar NIGHT CALLS ARE OFF — the room has "
                               + (wood == null ? "no 'WindowWood' marker" : "a 'WindowWood' marker")
                               + " and "
                               + (_windowMouth == null ? "no 'WindowGlow' node" : "a 'WindowGlow' node")
                               + ", and BOTH are needed: the first is the frame the perch ring is "
                               + "drawn in, the second is the opening the calls are heard through. "
                               + "The user asked for the wood's animals to be audible from the "
                               + "cellar, localised at the window (ModBuild 296) — with either node "
                               + "missing this room is silent between the drip and the rat, exactly "
                               + "as it was before. If the bake renamed them, rename them here; do "
                               + "NOT add a fallback, because every other node at the window is a "
                               + "welded mesh sitting at the room's origin and a fallback would put "
                               + "the animals in the middle of the floor.");
        }

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
        _perchOutwardOnly = false;   // in the wood a call may come from any bearing
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

}
