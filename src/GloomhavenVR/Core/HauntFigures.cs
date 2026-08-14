using UnityEngine;

namespace GloomhavenVR.Core;

// =================================================================================================
//  HAUNT FIGURES — the apparitions, played by THE GAME'S OWN MONSTERS.
//
//  THIS FILE IS THE SWITCH AND THE CLOCK. It owns no geometry, no animation and no audio: it
//  decides WHETHER an apparition is running right now and WHICH one, hands that decision to
//  HauntFigures.Events.cs to be placed and moved, and to HauntFigures.Clone.cs to be built out of a
//  real enemy prefab. It publishes ONE global shader vector so the shader-drawn apparition it
//  replaced stops drawing.
// =================================================================================================

/// <summary>
/// Occasional apparitions in the CELLAR and the NIGHT FOREST that are the game's OWN enemy models,
/// spawned decoratively, playing their OWN idle animation, walking past on their own legs.
///
/// <para><b>USER REQUEST, verbatim</b> (this replaces a feature the user has now rejected twice, so
/// the words are quoted rather than summarised): "Ich hab getestet und mir gefallen die Figuren und
/// animationen gar nicht. Ich habe eine andere Idee. Ich möchte dass du die Gegner-Figuren aus dem
/// Spiel nimmst (insbesondere die grusiligen), die die eingebauten passenden Animationen ausführen
/// (zB vorbeilaufen). Auch die Sounds der assets kannst du dann aus dem Spiel nutzen."</para>
///
/// <para><b>WHAT THAT ACTUALLY ASKS FOR, and why it is the right call.</b> The apparitions this
/// replaces were custom geometry, posed at bake time from one humanoid base mesh and drawn by
/// <c>EnvHaunt.shader</c>. Three rounds went into them and they were rejected outright — the
/// recurring complaint being that they read as an EFFECT rather than as a creature. The game's own
/// monsters cannot have that problem: they are modelled, rigged, skinned, animated, voiced and lit
/// by the same artists who made everything else the player is looking at, so they are stylistically
/// correct BY CONSTRUCTION rather than by taste. The one thing they do not have is a walk animation
/// with root motion (see ANIMATION below), and that is the single technical fact this whole feature
/// is shaped around.</para>
///
/// <para><b>WHAT IS FRIGHTENING HERE, stated as a rule rather than as a hope.</b> The user's own
/// example is "vorbeilaufen" — something CROSSING. Every one of the four events below is built out
/// of the same three ingredients and nothing else:
/// <list type="number">
/// <item><b>DISTANCE.</b> Nothing is ever nearer than the far side of the room. A figure fully lit
/// in the middle of the play space is a model viewer, not a haunting.</item>
/// <item><b>PARTIAL OCCLUSION BY REAL GEOMETRY.</b> Every event is framed by something the bake
/// actually built: a barred window slot 1.11 m wide, a doorway 1.58 m wide, the second band of
/// tree trunks. The player never sees the whole creature, and the thing doing the hiding is real
/// geometry with real depth rather than an authored fade.</item>
/// <item><b>BREVITY.</b> The longest event is visible for a few seconds and most of that is spent
/// behind something. "Was that there?" is the target; being startled is explicitly forbidden by
/// the standing brief.</item>
/// </list></para>
///
/// <para><b>THE PUBLISHED CHANNEL (the contract — this doc is the ONE canonical place; the bundle's
/// EnvHaunt.cginc quotes this name verbatim and nothing else in the mod may write it).</b> One
/// <c>float4</c> global, set with <see cref="Shader.SetGlobalVector"/>:</para>
/// <code>
///   _GhvrHauntFigures = float4(SuppressMask, 0, 0, 0)
/// </code>
/// <list type="bullet">
/// <item><b>x — SUPPRESS MASK.</b> A bitmask of the current room's card indices, as an exact small
/// integer carried in a float (0..63): bit <c>k</c> set means "card <c>k</c> of this room is
/// played by a real figure now, so the shader-drawn apparition for it must NOT be drawn". 0 —
/// which is also what an unwritten global reads as, and what stands whenever this feature is not
/// live — means nothing is suppressed and the room behaves EXACTLY as it did before this feature
/// existed. That is the whole degradation story: if anything at all goes wrong on this side, the
/// mask stays 0 and the player sees the old apparitions rather than an empty room.</item>
/// <item><b>y, z, w</b> — reserved, published as 0.</item>
/// </list>
///
/// <para><b>WHERE THE MASK IS APPLIED, AND WHY IT IS NOT APPLIED TO EVERYTHING.</b> Four shaders
/// read the haunt schedule and only ONE of them draws an apparition; the other three REACT to one
/// (the moonbeam dims for the thing at the window, the cobwebs shiver, the rat refuses to cross
/// during an event). Suppressing a card must stop the DRAWING and must not stop the REACTING — the
/// moonbeam should still dim while a real figure passes the window, and the rat must still stay in
/// its hole so two events never land together. EnvHaunt.cginc therefore splits the resolve in two:
/// <c>GhvrHauntAtRaw</c> is the untouched schedule that <c>GhvrHauntPresence</c> (the reactors) and
/// nothing else reads, and <c>GhvrHauntAt</c> is that plus the mask, which is what the DRAWING
/// shader and <c>EnvCritter</c> call. The mask is expressed there as <c>h.card = -1</c> — a card
/// index no mesh owns — so every existing "is this my card" test in the drawing shader already
/// answers no, with no edit to any .shader file. See EnvHaunt.cginc's own block for the full
/// argument.</para>
///
/// <para><b>WHICH CARDS THIS LANE TAKES OVER.</b> Four of the twelve, chosen because they are the
/// ones that are a CREATURE DOING SOMETHING rather than a shape or a surface — see
/// <c>HauntFigures.Events.cs</c> for each one's design. Cellar 0 (the window) and 4 (the stair
/// doorway); forest 2 (the watcher) and 3 (the crossing). Everything else keeps working exactly as
/// it does today and this file does not know it exists: the cellar's handprints (1), the face at
/// floor level (2), the TREMBLE card that draws nothing at all and only shivers the cobwebs (3),
/// and the TOPPLING BOOKSHELF (5) — which is a real prop with a real physics-shaped fall and five
/// materials riding its published pose, and is emphatically not an apparition. In the forest: the
/// face easing out from behind the bark (0), the two eyeshines (1), the featureless looming mass
/// (4) and the hanged thing (5). The last three could not be a game monster even in principle:
/// there is no hang-upside-down animation, no featureless 3 m mass in the roster, and the "eases
/// sideways out from behind the trunk" motion is a sub-decimetre translation that no walk cycle
/// can express.</para>
///
/// <para><b>THE SCHEDULE IS NOT THIS FEATURE'S. It is <see cref="Haunt"/>'s, unchanged.</b> Every
/// decision below — whether an event happens, which card it is, when it starts, how long it runs —
/// comes from <see cref="Haunt.Resolve"/>, i.e. from a hash of a slot index derived from
/// <see cref="SkyAlternative.EnvClockSeconds"/>, the mod's SHARED environment epoch. The two
/// decisions this feature adds on top (WHICH CREATURE and WHICH WAY it walks) are hashes of the
/// same slot index on channels nothing else uses. There is no <c>Random</c>, no per-client state,
/// no <c>Time.time</c> and NO NEW WIRE BYTES: two clients showing the same room feed the same
/// integer into the same cascade and get the same creature, in the same place, walking the same
/// way, at the same second. The frequency dial stays a monotone subset selector exactly as
/// <see cref="Haunt"/> documents, because it is <see cref="Haunt.Resolve"/> that applies it.</para>
///
/// <para><b>MULTIPLAYER — WHICH SIDE OF THE LINE THIS SITS ON.</b> Under the classification in
/// <c>.planning/refactor/INVARIANTS-Net-Rig.md</c> ("LOCAL presentation over GLOBAL-derived
/// content") this is LOCAL PRESENTATION OF GLOBALLY-DERIVED CONTENT, the same bucket as
/// <see cref="Haunt"/> and <see cref="EnvSound"/>. The CONTENT is global by construction: it is a
/// pure function of the shared environment clock and of the base-game monster library
/// (<c>MonsterClassManager.Classes</c> filtered to <c>BundleDLC == None</c>, which is why DLC can
/// never make two players disagree — see HauntFigures.Roster.cs). The PRESENTATION is local: a
/// decorative clone of an enemy prefab, with every game-side hook stripped before it can run.
/// Nothing here is state, so there is nothing to replicate and no wire field exists. The
/// must-NOT-touch list is honoured by construction and each item is evidenced at its call site in
/// HauntFigures.Clone.cs: no <c>ControllableRegistry</c> (that is exactly what
/// <c>isPreview: true</c> guards), no <c>PathFinder.Nodes[].Blocked</c> (<c>UnityGameEditorObject</c>
/// is stripped before its <c>Start</c> can run), no <c>CActor</c>/<c>CClientTile</c>/
/// <c>ScenarioState</c> read or written, no <c>ObjectPool</c> (the weapon lists are cleared so the
/// one path that would have used it never runs), no <c>ObjectCacheService</c>, no
/// <c>WorldspaceUITools</c> registration, no <c>TimeManager.FreezeTime</c> (which is why only
/// idle-family animator states are ever played).</para>
///
/// <para><b>SETTINGS: NONE OF ITS OWN, deliberately.</b> This rides <see cref="Haunt.EasterEggs"/>
/// and <see cref="Haunt.Frequency"/> unchanged. It is the same optional content those two dials
/// were added for — the standing ruling is that a setting may configure optional content, and
/// "which technique draws the apparition" is not a thing a player has an opinion about. The
/// Advanced-menu TEST TRIGGER works too and needs no change: <see cref="Haunt.Force"/> writes the
/// forced card into the schedule, <see cref="Haunt.Resolve"/> returns it, and this file spawns the
/// figure for it like any other event.</para>
///
/// <para><b>MIXED REALITY: STANDS DOWN</b>, via the same route as the rest of the environment —
/// this is only ticked from <c>SkyAlternative.Tick</c>, which is only reached on the MR-OFF branch.
/// A skinned monster standing in the player's living room is the exact thing the standing MR ruling
/// forbids.</para>
/// </summary>
/// <remarks>CLASSIFICATION: LOCAL presentation, GLOBAL-derived content, ZERO wire. See the
/// MULTIPLAYER paragraph above.</remarks>
internal static partial class HauntFigures
{
    // ---- the contract, as identifiers -----------------------------------------------------------

    /// <summary>Global <c>float4(SuppressMask, 0, 0, 0)</c>. Quote this name, not a literal.</summary>
    internal const string ChannelName = "_GhvrHauntFigures";

    private static readonly int ChannelId = Shader.PropertyToID(ChannelName);

    /// <summary>
    /// How long BEFORE an event's start the machinery arms itself, in shared-clock seconds.
    ///
    /// <para>THREE, and every one of the three is load-bearing. (1) The enemy prefab is loaded
    /// ASYNCHRONOUSLY (HauntFigures.Clone.cs explains at length why the game's own synchronous
    /// loader is not usable in a 90 Hz frame) and an Addressables asset load off a cold bundle can
    /// take a second or more; arming early means the figure is standing there at the exact
    /// shared-clock second the schedule names rather than "some time after it". (2) The audio cue
    /// <see cref="EnvSound"/> plays for an apparition can LEAD it by up to 1.1 s, and it is placed
    /// on a node named <c>Haunt&lt;card&gt;</c> that this feature creates — so that node has to
    /// exist before the cue looks for it. (3) It bounds how wrong a late load can make things: an
    /// event that could not be built within its arming window is skipped entirely rather than
    /// arriving halfway through and being cut off.</para>
    /// </summary>
    private const float ArmLeadSeconds = 3f;

    // ---- live state ------------------------------------------------------------------------------

    /// <summary>The room root the figures hang under — handed in by the caller, never fetched.
    /// <c>SkyAlternative</c>'s branch roots are private with no accessor and that is a design fact
    /// rather than an oversight (see <see cref="ElementMood"/>'s class doc); the same push-don't-pull
    /// shape <see cref="EnvSound"/> uses.</summary>
    private static Transform? _room;

    private static SkyStyle _style = SkyStyle.Default;

    /// <summary>Card index of the event currently armed or running, or -1.</summary>
    private static int _card = -1;

    /// <summary>Shared-clock start of the armed event — the identity of THIS run, so a re-resolve
    /// of the same card in the same slot is recognised as "still the same apparition" and a new
    /// slot with the same card is recognised as a new one.</summary>
    private static float _startClock;

    /// <summary>Per-slot duration scale, taken from the schedule so a figure stretches with Ice
    /// exactly as the shader-drawn apparitions do.</summary>
    private static float _durMul = 1f;

    private static float _lastMask = -1f;

    /// <summary>False once anything has gone wrong badly enough that suppressing the shader
    /// apparitions would leave the room empty. See <see cref="Disable"/>.</summary>
    private static bool _capable = true;

    private static bool _loggedOn;

    // ---- the per-frame driver ---------------------------------------------------------------------

    /// <summary>
    /// One frame of the feature. Call from <c>SkyAlternative.Tick</c> immediately after
    /// <c>EnvSound.Tick</c> — that is the environment driver, it is only reached on the MR-OFF
    /// branch (which is exactly the gating this wants), and it runs after <c>TickEnvClock</c> so the
    /// shared clock is THIS frame's rather than the last one's. Both arguments are handed in for the
    /// reason <c>EnvSound</c>'s are: the room root is private to <c>SkyAlternative</c> with no
    /// getter, and it is passed only once it is PLACED, because a figure positioned against an
    /// unresolved room pose would stand in the wrong corner of the world.
    ///
    /// <para>COST WHEN OFF: one bool read and an early return. COST WHEN ON WITH NOTHING RUNNING:
    /// one schedule resolve (a handful of multiply/add/frac) and one uniform compare.</para>
    ///
    /// <para>NEVER THROWS. Everything below it talks to the GAME — an asset bundle, a prefab, an
    /// animator, an audio bank — and every one of those can be absent, half-loaded or owned by a
    /// DLC this player does not have. The whole body is guarded, and a failure degrades to "no
    /// apparition, and the shader-drawn one plays instead", which is a strictly better outcome than
    /// the old behaviour it replaces.</para>
    /// </summary>
    /// <param name="roomGo">The PLACED environment room root, or null when it is not standing.</param>
    /// <param name="style">The environment being shown.</param>
    internal static void Tick(GameObject? roomGo, SkyStyle style)
    {
        using (PerfMonitor.Scope("Env.HauntFigures"))
        {
            try
            {
                TickBody(roomGo, style);
            }
            catch (System.Exception ex)
            {
                // ONE line, once, with the type and the stack — the mod restores process-wide stack
                // traces (ModBuild 136+), so this is attributable rather than a mystery — and then
                // the feature is off for the rest of the session. It does NOT retry: a fault in
                // here is a fault against the game's own object model, and a per-frame retry would
                // turn one bad frame into a log nobody can read.
                Disable($"it threw {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    private static void TickBody(GameObject? roomGo, SkyStyle style)
    {
        // THE SAME TWO DIALS, NOT A THIRD. Haunt.EasterEggs is the master and Haunt.Frequency is
        // folded into Haunt.Resolve; a test press overrides the master for its hold exactly as it
        // does for the shader apparitions (Haunt.Force), so the Advanced-menu buttons show the
        // FIGURE for a card this lane owns without any wiring of their own.
        if (Haunt.EasterEggs == null || (!Haunt.EasterEggs.Value && !Haunt.Forcing))
        {
            StandDown("the setting is off");
            return;
        }

        if (!VRSession.IsRunning)
        {
            StandDown("VR is not running");
            return;
        }

        // Only the two styles that have a room to stand in — the same boundary the rest of the
        // feature draws, and the only one that could work: the other styles draw no mod environment
        // at all, so there is no floor, no trunk and no window to frame anything.
        if (roomGo == null || (style != SkyStyle.Cellar && style != SkyStyle.SwampNight))
        {
            StandDown(roomGo == null
                          ? "the environment room is not standing"
                          : $"the environment is '{style}', which has no apparitions");
            return;
        }

        if (!ReferenceEquals(_room, roomGo.transform) || _style != style)
        {
            // A new room (style change, scenario reload, rig rebuild). Everything cached — the
            // ground sampler, the anchor node, any figure that was up — belonged to the old one.
            Release("the environment room was rebuilt or the style changed");
            _room = roomGo.transform;
            _style = style;
            _loggedOn = false;
        }

        float clock = SkyAlternative.EnvClockSeconds;

        // THE ROSTER FIRST, because it decides whether suppressing is even legal. If no base-game
        // enemy model can be resolved on this machine there is nothing to replace the shader
        // apparitions WITH, and publishing the mask would empty the room.
        //
        // A NEGATIVE ANSWER IS NOT FATAL AND MUST NOT BE, which is why this is a plain return and
        // not a Disable(). MonsterClassManager.Classes is empty until the game has called Load()
        // (:46-49), and the very first frames of a scenario can legitimately land here before that
        // has happened. Retrying is free — the resolve caches itself the moment it succeeds — and
        // the cost of getting it wrong in the other direction is a room whose apparitions never
        // come back for the rest of the session.
        if (!_capable || !Roster.Ready(style))
        {
            PublishMask(0f);
            return;
        }

        PublishMask(MaskFor(style));

        if (!_loggedOn)
        {
            _loggedOn = true;
            VRLog.Info("Core", $"HAUNT FIGURES on — {style}: cards {CardList(style)} are now played by the "
                               + "game's own enemy models instead of by EnvHaunt.shader, and "
                               + $"{ChannelName} = ({MaskFor(style):F0}, 0, 0, 0) tells the bundle to stop "
                               + "drawing them. Every other card in this room is untouched and still drawn by "
                               + "the shader. Which creature appears, where it walks and which way it faces "
                               + "are all hashes of the SHARED environment clock's slot index, so every player "
                               + "in this scenario sees the same creature in the same place at the same second "
                               + "with zero wire traffic.");
        }

        // ---- what should be running --------------------------------------------------------------
        Haunt.Slot slot = Haunt.Resolve(clock, style);

        int want = -1;
        float wantStart = 0f;
        float wantDur = 1f;
        if (slot.Live && IsMine(style, slot.Card))
        {
            HauntEvent ev = EventFor(style, slot.Card);
            float end = slot.StartClock + ev.Seconds * slot.DurationMul;
            // The arming window opens EARLY (see ArmLeadSeconds) and closes at the event's end. A
            // clock that has run backwards — a scene reload, a new owner of the shared epoch —
            // fails this test and simply retires whatever is up, which is the honest answer.
            if (clock >= slot.StartClock - ArmLeadSeconds && clock <= end)
            {
                want = slot.Card;
                wantStart = slot.StartClock;
                wantDur = slot.DurationMul;
            }
        }

        // ---- retire what should not be --------------------------------------------------------
        //
        // A RE-ANCHOR OF THE SAME CARD IS NOT A DIFFERENT EVENT. ModBuild 147 gave the Advanced
        // menu's test triggers an indefinite LATCH, and a latched apparition has to LOOP or the
        // shader's presence envelope runs out and holds an empty room (see Haunt.ForceLoopSeconds).
        // It loops by advancing _forceSince — i.e. the SAME card arrives with a new StartClock every
        // ForceLoopSeconds, which is floored at 2.5 s. Read literally, the test below called that "a
        // different event took over" and tore the clone down: an Addressables instantiate, a strip,
        // an InitialiseCharacterAsync and a fresh light bind EVERY 2.5 SECONDS, for as long as the
        // tester held the latch. Found in the hardware log as nine `armed at` lines 2.5 s apart on
        // one forest card. It did not change the picture, which is exactly why it needed finding in
        // the log rather than in a headset.
        //
        // So the card decides the CLONE and the start clock decides only the MOTION: same card and a
        // start clock that moved FORWARD keeps the creature standing and re-runs its path from the
        // top, which is what a looping apparition should look like anyway. Everything else still
        // retires — a different card, and a clock that ran BACKWARDS (a scene reload, a new owner of
        // the shared epoch), because that is a discontinuity rather than a repetition.
        bool sameCardLooped = _card >= 0 && want == _card
                              && !Mathf.Approximately(wantStart, _startClock)
                              && wantStart > _startClock;
        if (_card >= 0 && want != _card)
            Retire(want < 0 ? "the event ended" : "a different event took over");
        else if (_card >= 0 && want < 0)
            Retire("the event ended");
        else if (_card >= 0 && !Mathf.Approximately(wantStart, _startClock) && !sameCardLooped)
            Retire("the shared clock jumped backwards under a running apparition");
        else if (sameCardLooped)
        {
            // Keep the creature, restart its run. One float write, no allocation, no load.
            _startClock = wantStart;
            _durMul = wantDur;
        }

        if (want < 0)
            return;

        // ---- arm / run -------------------------------------------------------------------------
        if (_card < 0)
        {
            _card = want;
            _startClock = wantStart;
            _durMul = wantDur;
            Arm(style, want, slot.Index);
            // Arm CLEARS _card when this event's cast could not be resolved on this machine — the
            // slot is then simply quiet. Falling through would drive an event that was never armed.
            if (_card < 0)
                return;
        }

        Drive(style, clock);
    }

    /// <summary>
    /// Drop everything this feature owns and publish the "nothing suppressed" zero, so the room
    /// goes straight back to the shader-drawn apparitions. Idempotent; the guard is what makes the
    /// off path free and what stops a per-frame log.
    ///
    /// <para>The drop is INSTANT and deliberately so, for <see cref="Haunt.StandDown"/>'s reason: a
    /// toggle the player just moved must answer immediately, and a figure caught mid-walk by a
    /// teardown has nothing to fade for because the room it was walking in is going away in the
    /// same frame.</para>
    /// </summary>
    /// <param name="why">Named in the one log line this emits.</param>
    internal static void StandDown(string why)
    {
        bool had = _card >= 0 || _room != null;
        Release(why);
        PublishMask(0f);
        _room = null;
        _loggedOn = false;
        if (had)
            VRLog.Info("Core", $"HAUNT FIGURES off — {why}. {ChannelName} published as zero, so every card "
                               + "this lane had taken over is drawn by EnvHaunt.shader again and nothing is "
                               + "left standing in the scene.");
    }

    /// <summary>
    /// Full teardown (VR stopped, rig destroyed, hot reload): everything <see cref="StandDown"/>
    /// does, plus the session-lifetime caches — the loaded enemy prefab handle and the resolved
    /// roster. An ordinary stand-down keeps those (a prefab nobody instantiates is inert, and
    /// re-resolving the roster on every mixed-reality toggle would be pure waste); a rig that is
    /// gone must leave nothing at all behind, Addressables refcounts included.
    /// </summary>
    internal static void ReleaseAll(string why)
    {
        StandDown(why);
        Clone.Shutdown("the feature was fully torn down");
        Roster.Forget();
        _capable = true;   // a fresh session gets a fresh chance
    }

    // ---- the suppression channel ------------------------------------------------------------------

    /// <summary>
    /// The ONE writer of <see cref="ChannelName"/> — the same single-writer discipline
    /// <see cref="Haunt.Write"/> states for <c>_GhvrHaunt</c> and <c>ElementMood</c> for the element
    /// vectors, and for the same reason: it is a global, so a second writer anywhere would fight
    /// this one with no way to see it.
    ///
    /// <para>The mask is an exact small integer in a float (0..63) and is compared exactly, not with
    /// an epsilon: it is a BITMASK, so "nearly the same" is not a meaningful state and a tolerance
    /// would only be a way to miss a change. A poisoned value is rejected outright — the shader
    /// divides by <c>exp2(card)</c> and floors, and <c>floor(NaN)</c> would suppress an arbitrary
    /// set of cards three lanes away from here.</para>
    /// </summary>
    private static void PublishMask(float mask)
    {
        if (float.IsNaN(mask) || float.IsInfinity(mask) || mask < 0f)
            return;
        if (mask == _lastMask)
            return;
        _lastMask = mask;
        Shader.SetGlobalVector(ChannelId, new Vector4(mask, 0f, 0f, 0f));
    }

    /// <summary>
    /// Give up for the rest of the session: publish the zero mask so the shader apparitions come
    /// back, drop everything, and say why exactly once.
    ///
    /// <para>THE FALLBACK IS THE OLD FEATURE, NOT AN EMPTY ROOM, and that is the whole reason the
    /// suppression is a published mask rather than a bake-time deletion of the cards. Whatever goes
    /// wrong here — a missing bundle, a mesh-less child, an animator with no idle state, an
    /// exception — the player keeps getting apparitions.</para>
    /// </summary>
    private static void Disable(string why)
    {
        if (!_capable)
            return;
        _capable = false;
        Release(why);
        PublishMask(0f);
        VRLog.Warn("Core", $"HAUNT FIGURES disabled for this session — {why}. {ChannelName} is published as "
                           + "zero, so every card this lane would have taken over is drawn by EnvHaunt.shader "
                           + "again and the player still sees apparitions; nothing is lost except that they are "
                           + "the shader's rather than the game's own monsters. This is the designed "
                           + "degradation, not a broken state.");
    }

    /// <summary>Tear down the running apparition and the caches that belong to one room. Keeps the
    /// session-lifetime prefab handle and roster — see <see cref="ReleaseAll"/>.</summary>
    private static void Release(string why)
    {
        Retire(why);
        Ground.Forget();
    }
}
