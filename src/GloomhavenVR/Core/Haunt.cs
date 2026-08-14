using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HAUNT — the creepy easter eggs. THIS FILE IS THE SWITCH AND NOTHING ELSE: it renders nothing,
//  schedules nothing and knows nothing about what the apparitions look like. It publishes ONE
//  global shader vector, and the bundle's own shaders do the rest.
// =================================================================================================

/// <summary>
/// Publishes the player's two haunt settings as a global shader vector, so that the mod's bundled
/// environments can show occasional creepy easter eggs — and so that one uniform switches the whole
/// feature off.
///
/// <para><b>USER REQUEST, 2026-08-14</b> (verbatim, abridged): "Ich will noch ein weiteres Feature:
/// 'Grusel-Easter-Eggs' in den Umgebungen. Also grusilige Animationen (ohne sound) die ab und zu
/// auftreten in den Umgebungen (nur Wald und Keller). Es soll deaktivierbar sein. ... Wie die
/// anderen events auch sollen sie synchron von allen Spielern an den selben Stellen sichtbar
/// sein."</para>
///
/// <para><b>THE PUBLISHED CHANNEL (the contract — this class doc is the ONE canonical place; the
/// bundle's EnvHaunt.cginc quotes this name verbatim and nothing else in the mod may write it).</b>
/// One <c>float4</c> global, set with <see cref="Shader.SetGlobalVector"/>:</para>
/// <code>
///   _GhvrHaunt = float4(Master, Frequency, 0, 0)
/// </code>
/// <list type="bullet">
/// <item><b>x — MASTER.</b> 1 while the feature is on and an environment that has haunts is being
/// shown; 0 otherwise, and 0 is what stands whenever the channel is not live (no scenario, no VR
/// session, mixed reality, a style with no haunts, teardown). 0 is a HARD off: every apparition's
/// quad collapses to a point in the vertex shader, the moonbeam stops asking whether to dim, the
/// cobwebs stop asking whether to shiver and the rat stops rolling for its stare. The cost of the
/// feature when off is one uniform read per vertex program that uses it.</item>
/// <item><b>y — FREQUENCY, 0..1.</b> How many of the scheduled events this client actually shows.
/// See "WHY THE DIAL IS A SUBSET" below; it is NOT an intensity.</item>
/// <item><b>z, w</b> — reserved, published as 0.</item>
/// </list>
/// <para>A SECOND global, <c>_GhvrHauntForce</c>, carries the Erweitert menu's TEST TRIGGER — one
/// button per apparition, so a tester does not have to wait out the schedule. Its contract is
/// documented at <see cref="ForceChannelName"/>; it is local, bounded, and never on the wire.</para>
///
/// <para><b>WHY THE DIAL IS A SUBSET AND NOT A RESHUFFLE — the one non-obvious thing in this
/// feature.</b> The user asked for two things that pull against each other: a frequency setting,
/// and "synchron von allen Spielern an den selben Stellen sichtbar". A dial that fed into the
/// schedule's hash would give two players on different settings two DIFFERENT schedules — different
/// events, in different places, at different times. So the schedule is FIXED and setting-independent
/// (a hash of the shared clock's slot number, EnvHaunt.cginc), and the dial is a monotone gate laid
/// over it: <c>H(slot, RATE) &lt; frequency</c>. A player at 0.9 therefore sees a strict SUPERSET of
/// what a player at 0.4 sees — same seconds, same places, one of them simply misses some. The bake
/// proves the nesting over 6000 slots and prints it (BuildEnvironmentRooms.ReportHauntSchedule).
/// The shipped default is deliberately 0.5 rather than 1.0 so the dial has room in BOTH directions
/// without ever having to invent an event, which is the only thing that could break the guarantee.
/// </para>
///
/// <para><b>MULTIPLAYER: ZERO NEW WIRE BYTES, and that is the design rather than an omission.</b>
/// Everything an apparition does is a pure function of <see cref="SkyAlternative.EnvClockSeconds"/>
/// — the mod's SHARED environment epoch, which is negotiated on the wire precisely for the
/// <c>Cellar</c> and <c>SwampNight</c> styles (<c>SkyAlternative.WireStyleCode</c>) and which is
/// exactly this feature's scope. Two clients showing the same room feed the same number into the
/// same hash and get the same bits: the same event, in the same place, starting at the same second.
/// Nothing about a haunt is state, so there is nothing to replicate. The frequency dial is the only
/// per-client input, and it is a subset selector for exactly that reason.</para>
///
/// <para><b>SCOPE: CELLAR AND NIGHT FOREST ONLY</b>, which is the user's own boundary ("nur Wald und
/// Keller"). It is also the only scope that could work: the apparitions are geometry in the two
/// bundled room prefabs, and the other styles (<c>Default</c>, <c>OffBlack</c>, mixed reality) draw
/// no mod environment at all. Under mixed reality this stands DOWN — unlike
/// <see cref="ElementMood"/>, which deliberately keeps publishing there. The difference is that a
/// mood is a number and an apparition is a surface, and the standing MR ruling is that the mod puts
/// no occluding geometry over passthrough.</para>
///
/// <para><b>NO SOUND, ever.</b> The user asked for "ohne sound" and the bundle ships no audio at
/// all. There is nothing here to switch off because there is nothing here to switch on.</para>
/// </summary>
/// <remarks>CLASSIFICATION: LOCAL — a presentation setting, ZERO wire. The CONTENT it gates is
/// GLOBAL by construction (a pure function of the shared environment clock), which is why no wire
/// field exists for either. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class Haunt
{
    // ---- the contract, as identifiers -----------------------------------------------------------

    /// <summary>Global <c>float4(Master, Frequency, 0, 0)</c>. Quote this name, not a literal.</summary>
    internal const string ChannelName = "_GhvrHaunt";

    private static readonly int ChannelId = Shader.PropertyToID(ChannelName);

    /// <summary>
    /// Global <c>float4(forcedId + 1, startClock, 0, 0)</c> — the TEST TRIGGER channel. Quote this
    /// name, not a literal.
    ///
    /// <para><b>THE CONTRACT (this doc is the ONE canonical place; the bundle's EnvHaunt.cginc quotes
    /// it verbatim and nothing else in the mod may write it):</b></para>
    /// <code>
    ///   _GhvrHauntForce = float4(id + 1, startedAtSharedClock, 0, 0)
    /// </code>
    /// <list type="bullet">
    /// <item><b>x — the forced event id PLUS ONE.</b> 0 means nothing is forced, which is what stands
    /// whenever this channel is not live; the +1 is what makes id 0 expressible without a second
    /// flag, and is why the shader tests <c>x &gt; 0.5</c> rather than <c>x != 0</c>.</item>
    /// <item><b>y — the SHARED-CLOCK time at which the force started</b>
    /// (<see cref="SkyAlternative.EnvClockSeconds"/>), so the shader plays the event from
    /// <c>phase = (clock − y) / duration</c> and suppresses everything else in the room while
    /// <c>x &gt; 0.5</c>.</item>
    /// <item><b>z, w</b> — reserved, published as 0.</item>
    /// </list>
    /// <para>The id is the room's own card index (0..<see cref="EventCount"/>−1), i.e. the same index
    /// the bake's catalogue prints, and it is per-room: which room it names is whichever of the two
    /// haunted environments is being shown.</para>
    /// </summary>
    internal const string ForceChannelName = "_GhvrHauntForce";

    private static readonly int ForceChannelId = Shader.PropertyToID(ForceChannelName);

    /// <summary>Below this, a change is not worth a uniform write.</summary>
    private const float WriteEpsilon = 0.002f;

    // ---- config ---------------------------------------------------------------------------------

    /// <summary>Master switch. OPTIONAL CONTENT under the standing settings ruling: it adds or
    /// removes an effect, it does not repair a broken interaction — and the user asked for it by
    /// name ("Es soll deaktivierbar sein").</summary>
    internal static ConfigEntry<bool> EasterEggs = null!;

    /// <summary>How many of the scheduled events this client shows. A SUBSET selector, not an
    /// intensity — see the class doc.</summary>
    internal static ConfigEntry<float> Frequency = null!;

    private static bool _bound;

    /// <summary>
    /// Bind the two dials into the RIG module's config file, riding along behind
    /// <c>SkyAlternative.BindConfig</c> and <c>ElementMood.BindConfig</c> — the same ride-along
    /// pattern and the same file, because this is a property OF the two bundled environments and a
    /// player looking for it on disk will look where the environment choice is.
    /// </summary>
    internal static void BindConfig(ConfigFile file)
    {
        if (_bound)
            return;
        _bound = true;

        EasterEggs = file.Bind("Haunt", "EasterEggs", Defaults.HauntEasterEggs,
            "Occasional creepy easter eggs in the CELLAR and the NIGHT FOREST: a pale smiling face "
            + "easing out from behind a tree, eyes that open in the undergrowth and blink once, a "
            + "tall figure that stands between two distant trunks and then is simply not there, "
            + "something looking in at the cellar window while the moonlight dims for it, "
            + "handprints blooming on the wet stone, a shape crossing the stair doorway, the "
            + "cobwebs shivering as if something large had gone past behind them, and now and then "
            + "the rat stopping in the middle of the floor to look at the room. There is NO sound "
            + "and nothing ever appears over the board, in the way of anything you need to read, or "
            + "close enough to reach — they are background, they are rare, and two never happen at "
            + "once. Every player in the game sees the SAME event in the SAME place at the SAME "
            + "moment: it is computed from the shared environment clock, so it needs no network "
            + "traffic and changes nothing about the game. OFF removes them completely and costs "
            + "nothing at all. Only inside a running scenario, and only with the Cellar or Night "
            + "forest environment selected; mixed reality switches them off. Applies live.");

        Frequency = file.Bind("Haunt", "Frequency", Defaults.HauntFrequency,
            new ConfigDescription(
                "How often the easter eggs happen. 0.5 (the default) is roughly one every three "
                + "minutes per environment. Lower is rarer, 0 is the same as switching them off, 1 "
                + "shows every one the schedule holds (about one every 80 seconds). This is a LOCAL "
                + "setting and it does not break the shared timing: the schedule itself is the same "
                + "on every client, and this only decides how many of its events your client shows "
                + "— so a player on a lower setting sees fewer of exactly the same events in "
                + "exactly the same places, never different ones. Has no effect at all while "
                + "'EasterEggs' is off. Applies live.",
                new AcceptableValueRange<float>(0f, 1f)));
    }

    // ---- TEST TRIGGER — one button per apparition -------------------------------------------------
    //
    // USER REQUEST (hardware, ModBuild 141, verbatim): "ich brauche zum Testen im Erweitert Menu die
    // möglichkeit die Elemente und Easter eggs einzeln auf Knopfdruck auslösen zu können." The
    // schedule is one event every ~83 s slot at half frequency, which is a median of nearly two
    // minutes of waiting per look at a random one of six — useless for judging them.
    //
    // THE TRIGGER HAS TO BE A GLOBAL, because there is no C# object per apparition to poke: the
    // bundle ships NO MonoBehaviours, the events are geometry in two room prefabs and every decision
    // about them is a hash of the shared clock inside EnvHaunt.cginc. So the trigger is published the
    // way everything else about this feature is — one uniform, one writer, see ForceChannelName.
    //
    // MULTIPLAYER: ZERO WIRE, and here that is not even a decision to defend. A haunt is not state;
    // the schedule is a pure function of the shared clock. Forcing one changes what THIS client's
    // shader draws for a few seconds and nothing else — a peer keeps computing and showing the real
    // schedule, and there is no value anywhere for the two to disagree about.

    /// <summary>
    /// How many forceable events a haunted room has — the card count both rooms are built with
    /// (Editor/BuildEnvironmentRooms.cs, <c>HauntCellarCards = 6</c>, asserted to be a multiple of
    /// the three schedule groups by <c>AssertHauntCards</c>). It is the number of BUTTONS the test
    /// page draws, so a room that ever grew a seventh card would show six buttons until this constant
    /// followed — which is why it is stated here, next to the channel, rather than counted in the UI.
    /// </summary>
    internal const int EventCount = 6;

    /// <summary>
    /// How long the forced flag is held up, in shared-clock seconds.
    ///
    /// <para>FOURTEEN, and it is an OVER-estimate on purpose. This side cannot know an event's length:
    /// the per-card reveal/hold/fade envelopes live in the bundle, and there is no channel back from
    /// the GPU. What the bake does state is the worst case — the longest authored event is 8.6 s
    /// (AssertHauntCards prints every card's <c>reveal+hold+fade</c>), the per-slot jitter scales it
    /// by up to 1.15 (<c>HauntDurLo + HauntDurSpan</c>) and Ice stretches it by a further 1.35, i.e.
    /// at most ≈13.4 s. Cutting a forced apparition off in mid-fade would make the tester judge a
    /// truncation instead of the effect, whereas holding too long costs only a few seconds in which
    /// the room is quiet because the force suppresses the schedule. A press ends the previous force
    /// immediately, so the tester never has to sit out the remainder.</para>
    /// </summary>
    internal const float ForceHoldSeconds = 14f;

    /// <summary>Forced event id, or -1 for none.</summary>
    private static int _forceId = -1;

    /// <summary>Shared-clock time the force started — published as y, and the expiry anchor.</summary>
    private static float _forceSince;

    private static Vector4 _lastForce;

    /// <summary>The force channel has been published as zero at least once. Same reason as
    /// <see cref="_zeroed"/>: the first tick must assert the "nothing forced" 0 rather than trust
    /// that no one ever wrote this global.</summary>
    private static bool _forceZeroed;

    /// <summary>
    /// Whether a forced apparition could be seen at all right now: VR running, a scenario board, and
    /// one of the two environments that HAS apparitions. It deliberately does NOT include
    /// <see cref="EasterEggs"/> — the switch is a preference the button overrides for its few seconds
    /// (see <see cref="Force"/>); these three are "there is no room to haunt".
    /// </summary>
    internal static bool ForceReady
    {
        get
        {
            if (!VRSession.IsRunning || !Events.VRModeStateMachine.ScenarioBoardExists)
                return false;
            SkyStyle style = SkyAlternative.Style.Value;
            return style == SkyStyle.Cellar || style == SkyStyle.SwampNight;
        }
    }

    /// <summary>True while an apparition is being forced.</summary>
    internal static bool Forcing => _forceId >= 0;

    /// <summary>
    /// Play one apparition now and suppress the rest of the room for
    /// <see cref="ForceHoldSeconds"/>. Returns false — and says why in the log — when there is no
    /// haunted room to play it in, so a press on the main menu or in the default environment is inert
    /// rather than an exception.
    ///
    /// <para>IT OVERRIDES <see cref="EasterEggs"/> FOR ITS DURATION, and the page says so in German.
    /// The choice was between "does nothing and says so" and "overrides briefly", and the deciding
    /// argument is that this is a TEST AID whose entire job is to put the apparition in front of the
    /// tester's eyes: a button that refuses because a toggle two pages away is off is
    /// indistinguishable from a button that is broken, which is precisely the failure this page
    /// exists to rule out. The override is bounded by the same fourteen seconds, it is local, and it
    /// never writes the setting — the toggle still reads the player's own choice, and that choice is
    /// what stands again the moment the force expires. Technically the override is unavoidable
    /// anyway: with the master at 0 the shader collapses every apparition's quad in the vertex
    /// program (<c>h.live *= step(0.0001, _GhvrHaunt.x)</c>, EnvHaunt.cginc), so a forced event with
    /// the feature off would be drawn nowhere.</para>
    /// </summary>
    /// <param name="id">Event id — the room's card index, 0..<see cref="EventCount"/>−1.</param>
    internal static bool Force(int id)
    {
        if (!_bound)
            Rig.RenderQuality.Bind();

        if (id < 0 || id >= EventCount)
            return false;

        if (!ForceReady)
        {
            string why = !VRSession.IsRunning
                ? "VR is not running"
                : !Events.VRModeStateMachine.ScenarioBoardExists
                    ? "there is no scenario board"
                    : $"the environment is '{SkyAlternative.Style.Value}', which has no apparitions "
                      + "(only Cellar and SwampNight do)";
            VRLog.Info("Core", $"HAUNT TEST TRIGGER ignored — apparition {id} was not forced because "
                               + why + ". There is no haunted room to draw it in, so the button is inert "
                               + "here on purpose rather than arming a force that would fire later.");
            return false;
        }

        _forceId = id;
        _forceSince = SkyAlternative.EnvClockSeconds;

        VRLog.Info("Core", $"HAUNT TEST TRIGGER: apparition {id} of the {SkyAlternative.Style.Value} room "
                           + $"forced from shared clock {_forceSince:F2}s, held until "
                           + $"{_forceSince + ForceHoldSeconds:F2}s ({ForceHoldSeconds:F0}s). "
                           + $"{ForceChannelName} = ({id + 1:F1}, {_forceSince:F2}, 0, 0) and "
                           + $"{ChannelName} = (master 1, frequency {Mathf.Clamp01(Frequency.Value):F2}, "
                           + "0, 0) from the next tick; while the force stands, the shader plays this "
                           + "one event from phase (clock - y) / duration and suppresses the scheduled "
                           + "ones"
                           + (EasterEggs.Value
                                  ? " (the 'EasterEggs' setting is on)"
                                  : " — the 'EasterEggs' setting is OFF and this press temporarily "
                                    + "overrides it, because with the master at 0 the shader collapses "
                                    + "every apparition to a point and there would be nothing to judge; "
                                    + "the setting itself is untouched and stands again the moment the "
                                    + "force expires")
                           + ". LOCAL TEST AID ONLY: a haunt is not game state and not on the wire — a "
                           + "peer keeps computing the real schedule from the same shared clock and is "
                           + "unaffected. Only this headset draws differently.");
        return true;
    }

    /// <summary>
    /// Drop the override and publish the "nothing forced" zero. Idempotent.
    /// </summary>
    internal static void ClearForce(string why)
    {
        if (_forceId < 0)
        {
            // Still assert the zero once, so the very first frame of a session states "nothing is
            // forced" instead of inheriting whatever the global happened to hold.
            WriteForce(Vector4.zero);
            return;
        }

        int id = _forceId;
        _forceId = -1;
        _forceSince = 0f;
        WriteForce(Vector4.zero);

        VRLog.Info("Core", $"HAUNT TEST TRIGGER over — apparition {id} is no longer forced ({why}). "
                           + $"{ForceChannelName} published as zero, so the room goes back to its own "
                           + "shared-clock schedule and nothing of the override is left standing.");
    }

    // ---- live state ------------------------------------------------------------------------------

    private static Vector4 _last;
    private static bool _live;

    /// <summary>Zeros have been published at least once. Starts false so the very first tick — even
    /// one that lands straight in the off path — writes the master 0 the art multiplies by, rather
    /// than trusting that nothing else ever wrote this global.</summary>
    private static bool _zeroed;

    // ---- per-frame driver -------------------------------------------------------------------------

    /// <summary>
    /// One frame of gating and publishing. Called from the top of <see cref="SkyAlternative.Tick"/>
    /// — the environment driver, which is the correct level for this and NOT the level
    /// <see cref="ElementMood"/> is ticked at. The difference is worth stating because the two
    /// features look identical from outside: the mood is a NUMBER and has to reach the player under
    /// every presentation including passthrough, so it rides <c>MixedReality.Tick</c>, which runs on
    /// both branches. A haunt is a SURFACE in one of two bundled room prefabs, so it exists exactly
    /// where those prefabs do — and <c>SkyAlternative.Tick</c> is only reached on the MR-OFF branch,
    /// which is precisely the gating this feature wants.
    ///
    /// <para>COST WHEN OFF: one bool read and an early return, then nothing — the published master
    /// is already 0 and <see cref="StandDown"/> is idempotent.</para>
    /// </summary>
    internal static void Tick()
    {
        // The dials live in the rig file; RenderQuality.Bind rides this along with the sky dial and
        // the element mood, so one call is enough and it is idempotent.
        if (!_bound)
            Rig.RenderQuality.Bind();

        // …UNLESS A TEST TRIGGER IS STANDING: a press overrides the switch for its few seconds (the
        // reasoning is at Force()). One extra field read on the off path, so "costs nothing when off"
        // still holds.
        if (!EasterEggs.Value && !Forcing)
        {
            StandDown("the setting is off");
            return;
        }

        if (!VRSession.IsRunning)
        {
            StandDown("VR is not running");
            return;
        }

        // SCENARIO-ONLY SCOPE — the same gate the environment itself uses. Outside a live scenario
        // board there is no bundled room for an apparition to hang in.
        if (!Events.VRModeStateMachine.ScenarioBoardExists)
        {
            StandDown("no scenario board");
            return;
        }

        // "nur Wald und Keller" — the user's own boundary, and the only one that could work: the
        // other styles draw no mod environment at all, so there is nothing to haunt. This is also
        // the exact set of styles for which the environment clock is SHARED on the wire
        // (SkyAlternative.WireStyleCode), which is what makes the events identical on every client.
        SkyStyle style = SkyAlternative.Style.Value;
        if (style != SkyStyle.Cellar && style != SkyStyle.SwampNight)
        {
            StandDown($"the environment is '{style}', which has no haunts");
            return;
        }

        // THE FORCE EXPIRES HERE, before anything is published, so the frame that ends the hold
        // already publishes the zero. The backwards test is the shared clock changing owner or a
        // scene reload resetting it: the elapsed time is then meaningless and the honest answer is to
        // drop the override rather than let it restart its fourteen seconds unseen.
        float clock = SkyAlternative.EnvClockSeconds;
        if (_forceId >= 0 && (clock - _forceSince >= ForceHoldSeconds || clock < _forceSince))
        {
            ClearForce(clock < _forceSince
                           ? "the shared clock jumped backwards"
                           : $"the {ForceHoldSeconds:F0}s test hold elapsed");

            // The force was the ONLY reason this tick got past the off-switch. With it gone the
            // switch decides again, and it has to decide THIS frame: falling through would publish
            // one frame of master 1 on a feature the player has switched off.
            if (!EasterEggs.Value)
            {
                StandDown("the setting is off — the test hold had been overriding it");
                return;
            }
        }

        float freq = Mathf.Clamp01(Frequency.Value);

        // MASTER IS 1 WHILE A FORCE STANDS even if the player's switch is off — see Force(): with 0
        // the shader collapses every apparition's quad and the forced event would be drawn nowhere.
        // The frequency dial is published UNCHANGED: it only selects which SCHEDULED slots fire, and
        // the shader suppresses those for the duration of a force anyway, so overriding it would
        // change nothing except what the log claims the player's settings are.
        Write(new Vector4(1f, freq, 0f, 0f));
        WriteForce(_forceId >= 0 ? new Vector4(_forceId + 1f, _forceSince, 0f, 0f) : Vector4.zero);

        if (!_live)
        {
            _live = true;
            _zeroed = false;
            // "on" has to mean what it says even when the reason is a test press: the setting can be
            // OFF here and the channel still live, which is a state a log reader must not have to
            // deduce from a nearby TEST TRIGGER line that may be seconds away.
            VRLog.Info("Core", $"HAUNT on — {style} easter eggs live at frequency {freq:F2}"
                               + (EasterEggs.Value
                                      ? string.Empty
                                      : " BECAUSE A TEST TRIGGER IS FORCING ONE; the 'EasterEggs' "
                                        + "setting itself is off and takes over again when the hold "
                                        + "expires")
                               + ". "
                               + $"{ChannelName} = (master 1, frequency {freq:F2}, 0, 0). Everything the "
                               + "apparitions do is a pure function of the SHARED environment clock "
                               + "(SkyAlternative.EnvClockSeconds), so every player in this scenario sees the "
                               + "same event in the same place at the same second with ZERO wire traffic. The "
                               + "frequency dial is a monotone SUBSET of that one schedule, never a different "
                               + "schedule — a lower setting shows fewer of exactly the same events, not "
                               + "other ones.");
        }
        _zeroed = false;
    }

    /// <summary>
    /// Drop the channel: publish master 0 ONCE, then do nothing until the gates open again.
    /// Idempotent — the guard is what makes the off path free, and what stops a per-frame log.
    ///
    /// <para>The drop is INSTANT and that is deliberate. A toggle the player just moved must answer
    /// immediately or it reads as broken; and an apparition caught mid-event by a teardown has
    /// nothing to fade for, because the room it was standing in is going away in the same frame.
    /// The one visible consequence — an event that vanishes a beat early when the setting is turned
    /// off — is indistinguishable from this catalogue's own instant-vanish events, which is a happy
    /// accident rather than a design.</para>
    /// </summary>
    /// <param name="why">Named in the one log line this emits, so a log reader can tell "the player
    /// turned it off" from "the scenario ended" without guessing.</param>
    internal static void StandDown(string why)
    {
        // BEFORE the idempotence guard, on purpose: every route that drops this channel — teardown
        // and the style change included (SkyAlternative.cs:880/890) — must also drop a test override,
        // and the guard would otherwise let one survive a stand-down that had already published its
        // zeros. ClearForce is itself idempotent and asserts the zero on the first call.
        ClearForce(why);

        if (!_live && _zeroed)
            return;

        bool wasLive = _live;
        _live = false;
        _zeroed = true;
        Write(Vector4.zero);

        if (wasLive)
            VRLog.Info("Core", $"HAUNT off — {why}. {ChannelName} published as zero, so every apparition's "
                               + "card collapses to a point in the vertex shader, the moonbeam stops asking "
                               + "whether to dim, the cobwebs stop asking whether to shiver and the rat stops "
                               + "rolling for its stare. Nothing is left standing and nothing is left "
                               + "costing.");
    }

    /// <summary>
    /// The ONE writer of <see cref="ChannelName"/> — the same discipline
    /// <c>SkyAlternative.ApplyTimeOfs</c> states for <c>_GhvrTimeOfs</c> and
    /// <c>ElementMood.Write</c> for the element vectors, and for the same reason: it is a global, so
    /// a second writer anywhere would fight this one with no way to see it.
    /// </summary>
    private static void Write(Vector4 v)
    {
        // A poisoned value must never reach a shader global: NaN would propagate into a slot index,
        // and floor(NaN) picks a card that does not exist — i.e. an apparition that is drawn nowhere
        // or everywhere, three lanes away from here.
        if (float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y))
            return;

        if (Mathf.Abs(v.x - _last.x) <= WriteEpsilon && Mathf.Abs(v.y - _last.y) <= WriteEpsilon
            && _zeroed == (v == Vector4.zero))
            return;

        _last = v;
        Shader.SetGlobalVector(ChannelId, v);
    }

    /// <summary>
    /// The ONE writer of <see cref="ForceChannelName"/>, held to the same discipline as
    /// <see cref="Write"/> — same NaN rejection, same write-only-on-change rule, same single-writer
    /// rule. The NaN case matters more here than anywhere else in this file: x is floored into an
    /// event INDEX and y is subtracted from the clock, so one poisoned value would either pick a card
    /// that does not exist or freeze a phase at NaN — an apparition drawn nowhere, or one drawn
    /// permanently, with the cause three lanes away.
    /// </summary>
    private static void WriteForce(Vector4 v)
    {
        if (float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y))
            return;

        // "Nothing forced" is a STATE, not a value near zero: the id is published as id+1, so any
        // x at or below 0 is the off case and gets written exactly once.
        bool off = v.x <= 0f;
        if (off)
        {
            if (_forceZeroed)
                return;
        }
        else if (Mathf.Abs(v.x - _lastForce.x) <= WriteEpsilon
                 && Mathf.Abs(v.y - _lastForce.y) <= WriteEpsilon)
        {
            return;
        }

        _forceZeroed = off;
        _lastForce = off ? Vector4.zero : v;
        Shader.SetGlobalVector(ForceChannelId, _lastForce);
    }
}
