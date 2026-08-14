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

        if (!EasterEggs.Value)
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

        float freq = Mathf.Clamp01(Frequency.Value);
        Write(new Vector4(1f, freq, 0f, 0f));

        if (!_live)
        {
            _live = true;
            _zeroed = false;
            VRLog.Info("Core", $"HAUNT on — {style} easter eggs live at frequency {freq:F2}. "
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
}
