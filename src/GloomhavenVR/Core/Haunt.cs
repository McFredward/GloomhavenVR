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
/// documented at <see cref="ForceChannelName"/>; it is local, held until the tester releases it, and
/// never on the wire.</para>
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
internal static partial class Haunt
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
    /// <item><b>y — the SHARED-CLOCK time at which the CURRENT RUN of the forced event started</b>
    /// (<see cref="SkyAlternative.EnvClockSeconds"/>), so the shader plays the event from
    /// <c>phase = (clock − y) / duration</c> and suppresses everything else in the room while
    /// <c>x &gt; 0.5</c>. "Current run" rather than "the press": a latched force LOOPS, and the loop
    /// is nothing but this side moving y forward by one <see cref="ForceLoopSeconds"/> at a time. The
    /// shader is not aware of it and needs no change — every value it reads is still a plain force
    /// that began at y.</item>
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
    // shader draws and nothing else — a peer keeps computing and showing the real schedule, and there
    // is no value anywhere for the two to disagree about. THE LATCH DOES NOT WEAKEN THAT: it changes
    // how long this client draws differently, not what any client publishes, so a force left standing
    // for an hour is exactly as invisible to a peer as one that lasted a second.

    /// <summary>
    /// How many forceable events a haunted room has — the card count both rooms are built with
    /// (Editor/BuildEnvironmentRooms.cs, <c>HauntCellarCards = 6</c>, asserted to be a multiple of
    /// the three schedule groups by <c>AssertHauntCards</c>). It is the number of BUTTONS the test
    /// page draws, so a room that ever grew a seventh card would show six buttons until this constant
    /// followed — which is why it is stated here, next to the channel, rather than counted in the UI.
    /// </summary>
    internal const int EventCount = 6;

    // FOLLOW-UP USER REQUEST (hardware, verbatim): "In der Triggertestview möchte ich wenn ich etwas
    // triggere das es dauerhaft an ist und mit erneutem toggle wieder ausgemacht wird. So kann ich
    // die Mischungen besser testen." One press latches, the same press again releases, and NOTHING
    // expires by itself. The old fourteen-second hold is gone.
    //
    // ON THIS SIDE THAT IS NOT ONE DELETION BUT TWO CHANGES, and the second one is the whole subtlety
    // of this file. Removing the expiry alone produces a latch that LOOKS BROKEN: the shader plays
    // the forced card from `h.sIn = t - _GhvrHauntForce.y` with `start = 0` and `durMul = 1`
    // (EnvHaunt.cginc, the _GhvrHauntForce block), and GhvrHauntEnvelope returns 0 the moment
    // `sIn` passes the card's own reveal+hold+fade. So a "permanent" force would show the apparition
    // once, for its authored few seconds, and then hold an empty room for as long as the tester left
    // the button lit — while suppressing the real schedule the whole time, because a set x is what
    // suppresses it. That is strictly worse than the timed version it replaced.
    //
    // SO THE LATCH LOOPS: Tick re-anchors _forceSince every time the event's own duration has
    // elapsed, and the apparition plays again. The tester sees the thing they pressed, repeatedly,
    // for as long as they hold the latch — which is what "dauerhaft an" can honestly mean for an
    // effect that is an EVENT rather than a state. (The elements are a state, so over there
    // "dauerhaft" is literal; the two halves of the page therefore behave differently, and the page
    // says so in German.)
    //
    // REJECTED: the reserved _GhvrHauntForce.z as a "loop" flag, which is what the channel's own
    // contract block reserves a slot for. It is the tidier answer and it is unavailable in this
    // round: honouring a flag means editing EnvHaunt.cginc, and a shader edit forces a bundle re-bake
    // and collides with the lanes that own unity/. The C# re-anchor needs no shader change at all —
    // the shader keeps seeing exactly what it sees today, a force that started at y — so it also
    // cannot desynchronise the two halves of the contract.
    //
    // REJECTED: latching SEVERAL apparitions at once, which is what the element half of the page now
    // does. The channel cannot express it and this lane may not widen it: _GhvrHauntForce.x is ONE
    // card id + 1, the shader assigns `h.card = _GhvrHauntForce.x - 1.0` (a scalar), and every card
    // draws only when h.card is its own index — so two ids would need a second uniform, a second
    // compare in the vertex program of four shaders, and the very re-bake the paragraph above
    // avoids. And unlike the elements, mixing apparitions is not what the user asked for: "die
    // Mischungen" is about elements sitting together, while the catalogue's own rule is that two
    // apparitions never happen at once. So the haunt half is LAST-PRESS-WINS — pressing a second
    // apparition releases the first and says so in the log and on the row.

    /// <summary>
    /// The quiet gap left between two runs of a looping latch, in shared-clock seconds.
    ///
    /// <para>WITHOUT IT the next run starts on the exact frame the last one reached zero presence,
    /// and a tester cannot tell a loop from one long event — which matters most for the events whose
    /// whole identity is that they are brief. Six tenths of a second is under the eye's "is it gone?"
    /// threshold for these fades and over the frame budget by a factor of thirty.</para>
    /// </summary>
    private const float ForceLoopGapSeconds = 0.6f;

    /// <summary>
    /// The shortest a loop may be, whatever the card's own length.
    ///
    /// <para>THIS IS A SAFETY FLOOR, not a taste: the forest's card 3 runs 0.34 s
    /// (<see cref="CardSeconds"/> — "the fastest event in either room") and the cellar's card 4 runs
    /// 0.70 s. Looping those at their own length would put a flash on screen roughly twice a second
    /// and hold it there indefinitely, which is a strobe rather than a test — unjudgeable, and the
    /// one thing a VR mod must never generate by accident. At two and a half seconds the fast events
    /// read as a recurring event, which is exactly what the tester needs to judge them.</para>
    /// </summary>
    private const float ForceLoopMinSeconds = 2.5f;

    /// <summary>
    /// How long one run of a latched apparition takes before it starts again, in shared-clock
    /// seconds. The card's own authored envelope, plus the gap, floored by the minimum.
    ///
    /// <para>IT USES <see cref="CardSeconds"/> RATHER THAN A SECOND NUMBER, deliberately: that table
    /// is already the mirror of the two bake catalogues (reveal + hold + fade per card), and a loop
    /// period invented here would be a fourth copy of a duration that is hard enough to keep in step
    /// three times. The per-slot jitter and the Ice stretch are correctly NOT applied — the shader
    /// pins <c>durMul = 1</c> for a forced event, so the authored length is the exact length.</para>
    /// </summary>
    internal static float ForceLoopSeconds(SkyStyle style, int card)
    {
        // THE LENGTH OF THE THING THAT IS ACTUALLY DRAWN, which is not always the shader's.
        // CardSeconds is the BAKE catalogue's reveal+hold+fade for the shader-drawn apparition. For a
        // card that HauntFigures has taken over, the shader draws nothing at all and the figure runs
        // on its own, unrelated envelope — the forest's card 3 is 0.34 s in the catalogue and 4.2 s
        // as a figure. Looping on the catalogue number therefore re-anchored a forced test roughly
        // twelve times per walk, and the ModBuild 146 hardware log shows exactly that: nine `armed
        // at` lines 2.5 s apart on one card, none of which ever finished. The single tool the user
        // has for inspecting these events could not show him a whole one.
        //
        // FigureSeconds returns 0 when this card is not a figure card ON THIS MACHINE — the takeover
        // is conditional on the creature's assets actually resolving here — so the fallback is the
        // catalogue number, which is correct precisely when the shader is what draws.
        float own = HauntFigures.FigureSeconds(style, card);
        float len = own > 0f ? own : CardSeconds(style, card);
        return Mathf.Max(len + ForceLoopGapSeconds, ForceLoopMinSeconds);
    }

    /// <summary>Latched event id, or -1 for none. STILL A SINGLE INT: the shader channel carries one
    /// card id and this lane may not widen it — see the rejection above.</summary>
    private static int _forceId = -1;

    /// <summary>Shared-clock time the CURRENT RUN of the latched event started — published as y, and
    /// re-anchored by <see cref="Tick"/> once per loop. It is no longer an expiry anchor; nothing
    /// expires.</summary>
    private static float _forceSince;

    /// <summary>Shared-clock time the latch itself was pressed, as opposed to the current run.
    /// Diagnostics only — it is what lets a log reader say how long a latch has been standing.</summary>
    private static float _forceLatchedAt;

    /// <summary>True once the current latch has looped at least once, so the "it is looping" line is
    /// written exactly once per latch instead of once per run.</summary>
    private static bool _forceLooped;

    private static Vector4 _lastForce;

    /// <summary>The force channel has been published as zero at least once. Same reason as
    /// <see cref="_zeroed"/>: the first tick must assert the "nothing forced" 0 rather than trust
    /// that no one ever wrote this global.</summary>
    private static bool _forceZeroed;

    /// <summary>
    /// Whether a forced apparition could be seen at all right now: VR running, a scenario board, and
    /// one of the two environments that HAS apparitions. It deliberately does NOT include
    /// <see cref="EasterEggs"/> — the switch is a preference the button overrides for as long as its
    /// latch stands (see <see cref="Force"/>); these three are "there is no room to haunt".
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

    /// <summary>True while an apparition is latched. It is what keeps <see cref="Tick"/> alive past
    /// the player's own off-switch, and with the latch now unbounded in time that override lasts
    /// exactly as long as the latch does — which is the point of the request.</summary>
    internal static bool Forcing => _forceId >= 0;

    /// <summary>Is this exact button lit? At most one can be, because the channel carries one id —
    /// the test page asks once per row after every press so the tester can see which.</summary>
    internal static bool IsForced(int id) => _forceId >= 0 && _forceId == id;

    /// <summary>
    /// TOGGLE one apparition's latch: play it, and keep replaying it, until this same button is
    /// pressed again. Returns false — and says why in the log — when there is no haunted room to play
    /// it in, so a press on the main menu or in the default environment is inert rather than an
    /// exception.
    ///
    /// <para><b>THE THREE CASES.</b> Pressing the LATCHED apparition releases it ("mit erneutem toggle
    /// wieder ausgemacht"). Pressing a DIFFERENT one releases the first and latches the new one —
    /// last press wins, because <c>_GhvrHauntForce.x</c> is one card id and the shader assigns it to
    /// one scalar <c>h.card</c>; the log says which one was dropped so the tester is never left
    /// wondering. Pressing anything with nothing latched simply latches it.</para>
    ///
    /// <para><b>IT LOOPS while it is latched</b> — <see cref="Tick"/> re-anchors the start every
    /// <see cref="ForceLoopSeconds"/> — because an apparition is an EVENT with an authored envelope
    /// and not a state that can be held up. The long reasoning, including why the shader's reserved
    /// loop slot was not used, is in the block above the constants.</para>
    ///
    /// <para>IT OVERRIDES <see cref="EasterEggs"/> FOR AS LONG AS IT STANDS, and the page says so in
    /// German. The choice was between "does nothing and says so" and "overrides", and the deciding
    /// argument is that this is a TEST AID whose entire job is to put the apparition in front of the
    /// tester's eyes: a button that refuses because a toggle two pages away is off is
    /// indistinguishable from a button that is broken, which is precisely the failure this page
    /// exists to rule out. What the latch changes is the DURATION of that override — it used to be
    /// fourteen seconds and is now "until released". It is still local, and it still never writes the
    /// setting: the toggle reads the player's own choice, and that choice stands again the moment the
    /// latch goes, on the very next tick (Tick's off path is guarded by <see cref="Forcing"/>, so
    /// there is no frame in between). Technically the override is unavoidable anyway: with the master
    /// at 0 the shader collapses every apparition's quad in the vertex program
    /// (<c>h.live *= step(0.0001, _GhvrHaunt.x)</c>, EnvHaunt.cginc), so a forced event with the
    /// feature off would be drawn nowhere.</para>
    /// </summary>
    /// <param name="id">Event id — the room's card index, 0..<see cref="EventCount"/>−1.</param>
    internal static bool Force(int id)
    {
        if (!_bound)
            Rig.RenderQuality.Bind();

        if (id < 0 || id >= EventCount)
            return false;

        // PRESSED AGAIN = OFF, and it is tested BEFORE ForceReady on purpose: releasing must work in
        // every state a latch can survive into. A tester whose environment changed under the latch
        // would otherwise be told "that environment has no apparitions" by a button whose whole job
        // at that moment is to stop doing something.
        if (_forceId == id)
        {
            ClearForce("the tester pressed the same test trigger again");
            return true;
        }

        if (!ForceReady)
        {
            string why = !VRSession.IsRunning
                ? "VR is not running"
                : !Events.VRModeStateMachine.ScenarioBoardExists
                    ? "there is no scenario board"
                    : $"the environment is '{SkyAlternative.Style.Value}', which has no apparitions "
                      + "(only Cellar and SwampNight do)";
            VRLog.Info("Core", $"HAUNT TEST TRIGGER ignored — apparition {id} was not latched because "
                               + why + ". There is no haunted room to draw it in, so the button is inert "
                               + "here on purpose rather than arming a force that would fire later.");
            return false;
        }

        int dropped = _forceId;
        SkyStyle room = SkyAlternative.Style.Value;
        _forceId = id;
        _forceSince = SkyAlternative.EnvClockSeconds;
        _forceLatchedAt = _forceSince;
        _forceLooped = false;
        float loop = ForceLoopSeconds(room, id);

        VRLog.Info("Core", $"HAUNT TEST TRIGGER: apparition {id} of the {room} room latched from shared "
                           + $"clock {_forceSince:F2}s and held INDEFINITELY — it ends when the same "
                           + "button is pressed again, when another apparition is pressed, when the stop "
                           + "row is pressed, or when the channel stands down. "
                           + (dropped >= 0
                                  ? $"Apparition {dropped} was latched and is released by this press: "
                                    + "LAST PRESS WINS, because " + ForceChannelName + ".x carries ONE "
                                    + "card id and the shader assigns it to one scalar h.card, so two "
                                    + "apparitions at once is not a capability this channel has. "
                                  : string.Empty)
                           + $"{ForceChannelName} = ({id + 1:F1}, {_forceSince:F2}, 0, 0) and "
                           + $"{ChannelName} = (master 1, frequency {Mathf.Clamp01(Frequency.Value):F2}, "
                           + "0, 0) from the next tick; while the latch stands, the shader plays this "
                           + "one event from phase (clock - y) / duration and suppresses the scheduled "
                           + $"ones. IT LOOPS every {loop:F2}s — the card's own authored "
                           + $"{CardSeconds(room, id):F2}s envelope plus a {ForceLoopGapSeconds:F2}s "
                           + "gap, floored at " + ForceLoopMinSeconds.ToString("F2") + "s so the "
                           + "shortest events cannot strobe — because the envelope reaches zero "
                           + "presence at the end of the card and a latch that did not re-anchor would "
                           + "hold an EMPTY room"
                           + (EasterEggs.Value
                                  ? " and the 'EasterEggs' setting is on"
                                  : " — the 'EasterEggs' setting is OFF and this latch overrides it for "
                                    + "as long as it stands, because with the master at 0 the shader "
                                    + "collapses every apparition to a point and there would be nothing "
                                    + "to judge; the setting itself is untouched and stands again the "
                                    + "moment the latch goes")
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
        float held = Mathf.Max(0f, SkyAlternative.EnvClockSeconds - _forceLatchedAt);
        _forceId = -1;
        _forceSince = 0f;
        _forceLatchedAt = 0f;
        _forceLooped = false;
        WriteForce(Vector4.zero);

        VRLog.Info("Core", $"HAUNT TEST TRIGGER off — apparition {id} is no longer latched ({why}); the "
                           + $"latch had stood about {held:F1}s. {ForceChannelName} published as zero, so "
                           + "the room goes back to its own shared-clock schedule and nothing of the "
                           + "override is left standing — including the master, which the player's own "
                           + "'EasterEggs' setting decides again from the next tick.");
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

        // …UNLESS A TEST LATCH IS STANDING: a press overrides the switch for as long as the latch is
        // held (the reasoning is at Force()). One extra field read on the off path, so "costs nothing
        // when off" still holds however long the tester leaves it on.
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

        // THE LATCH LOOPS HERE, before anything is published, so the frame that starts a new run
        // already publishes the new anchor.
        //
        // NOTHING EXPIRES ANY MORE — the user asked for "dauerhaft an", so the only thing that ends a
        // latch is a press or a stand-down. What replaces the old expiry is a RE-ANCHOR: the shader
        // plays the forced card from `sIn = t - _GhvrHauntForce.y` with start 0 and durMul 1, and
        // GhvrHauntEnvelope returns 0 presence once `sIn` passes the card's own reveal+hold+fade
        // (EnvHaunt.cginc). Left alone, a latched force would therefore show the apparition once and
        // then hold an EMPTY room — while still suppressing the real schedule, because a set x is
        // what suppresses it. Moving y forward by exactly one loop makes the apparition play again.
        //
        // THE OFF-SWITCH FALLTHROUGH THAT USED TO LIVE HERE IS GONE, and that is safe rather than an
        // oversight: it existed because the force could END inside this method, which would leave a
        // tick that had only got past `!EasterEggs.Value && !Forcing` because of a force that no
        // longer existed. No path in Tick can end a latch now, so `Forcing` cannot change under this
        // method and the master stays up for exactly as long as the latch does — which is precisely
        // what an indefinite latch with the setting OFF has to mean. The routes that DO end a latch
        // are the two buttons (outside Tick — the next tick then takes the off path in the normal
        // way) and StandDown (which publishes the zeros itself).
        //
        // THE BACKWARDS-CLOCK CASE RE-ANCHORS RATHER THAN DROPPING THE LATCH. A new clock owner or a
        // scene reload makes the elapsed time meaningless, which used to be an argument for ending
        // the hold; with an explicit latch it is now an argument against, because the tester never
        // let go of it. Re-anchoring to the new clock costs one restarted run and keeps the button
        // honest. Guarding it at all is still necessary: without it, `clock - _forceSince` is
        // negative for as long as the new clock takes to catch up and the apparition would freeze at
        // presence 0.
        float clock = SkyAlternative.EnvClockSeconds;
        if (_forceId >= 0)
        {
            float loop = Mathf.Max(0.05f, ForceLoopSeconds(style, _forceId));
            if (clock < _forceSince)
            {
                _forceSince = clock;
            }
            else if (clock - _forceSince >= loop)
            {
                // Whole loops, in one step rather than a while-loop: a frame that lands after a long
                // stall (a level load, a headset taken off) would otherwise iterate once per skipped
                // run to arrive at the same answer.
                _forceSince += loop * Mathf.Floor((clock - _forceSince) / loop);

                if (!_forceLooped)
                {
                    _forceLooped = true;
                    VRLog.Info("Core", $"HAUNT TEST TRIGGER looping — apparition {_forceId} of the "
                                       + $"{style} room restarts every {loop:F2}s while its button "
                                       + "stays latched, re-anchored from C# by moving "
                                       + $"{ForceChannelName}.y forward; the shader is unchanged and "
                                       + "still sees a plain force that began at y. This line is "
                                       + "written ONCE per latch, not once per run. The apparition's "
                                       + "own envelope is "
                                       + $"{CardSeconds(style, _forceId):F2}s, then a quiet gap, and "
                                       + "the real schedule stays suppressed throughout — press the "
                                       + "same button again to release it.");
                }
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
                                      : " BECAUSE A TEST TRIGGER IS LATCHED ON; the 'EasterEggs' "
                                        + "setting itself is off and takes over again the moment the "
                                        + "latch is released")
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
