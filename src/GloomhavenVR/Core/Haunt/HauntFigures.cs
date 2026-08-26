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
/// <para><b>WHAT IS FRIGHTENING HERE, stated as a rule rather than as a hope, and REWRITTEN after
/// the first hardware round.</b> The user's own first example was "vorbeilaufen" — something
/// CROSSING — and after watching two of those he rejected both ("Ich mag die Idee nicht dass jemand
/// am Kellerfenster vorbeirennt", "Mach doch auch einfach jemand der dort steht und beobachtet im
/// Schatten"). Three of the four events therefore now STAND. The ingredients are:
/// <list type="number">
/// <item><b>DISTANCE.</b> Nothing is ever nearer than the far side of the room. A figure fully lit
/// in the middle of the play space is a model viewer, not a haunting.</item>
/// <item><b>DARKNESS THAT IS THE ROOM'S OWN.</b> The user's ruling, verbatim: "Sie MÜSSEN an die
/// Lichtverhältnisse angeglichen werden, sonst geht der Gruselfaktor verloren." A creature's albedo
/// is multiplied by the light the room actually delivers where it stands, measured off the room's
/// own baked rig — a fiftieth in the cellar and a thirteenth in the wood since the two ModBuild 148
/// photographs were measured (HauntFigures.Clone.cs, THE DARKENING). This is the ingredient
/// that replaced the one below it, and it is the one the user calls the most important point.</item>
/// <item><b>PARTIAL OCCLUSION BY REAL GEOMETRY — WITH THE CAVEAT THAT COST US TWO EVENTS.</b> Every
/// event is framed by something the bake really built: the barred window, the stair alcove, the
/// second band of trunks. But the player looks at the board as a DIORAMA and his head is routinely
/// ABOVE the cellar's 3.3 m walls, so <b>an event that relies on a wall to crop the creature works
/// from inside the room and fails completely from the tabletop vantage</b> —
/// <c>.planning/debug/nachladen1.jpg</c> is a whole figure standing in the open beyond a wall that
/// was supposed to hide all but its shins. EVERY EVENT IS NOW JUDGED FROM BOTH VIEWPOINTS and each
/// one says in its own doc what it looks like from each.</item>
/// <item><b>BREVITY.</b> The longest event is visible for a few seconds. "Was that there?" is the
/// target; being startled is explicitly forbidden by the standing brief.</item>
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
/// <para><b>WHICH CARDS THIS LANE TAKES OVER.</b> Four, chosen because they are the ones that are a
/// CREATURE rather than a shape or a surface — see <c>HauntFigures.Events.cs</c> for each one's
/// design. Cellar 0 (the face at the window) and 4 (the watcher in the stair shaft); forest 1 (the
/// watcher at the treeline) and 2 (the crossing). THE FOREST PAIR IS 1 AND 2, not 2 and 3: the wood
/// went from six cards to three when the hand-built figures were deleted (ModBuild 147) and its
/// catalogue was renumbered rather than left with holes. Everything else keeps working exactly as
/// it does today and this file does not know it exists: the cellar's handprints (1), the DOOR that
/// opens at the top of the stair and closes again (2 — light where there was none, no body at all),
/// the TREMBLE card that draws nothing and only shivers the cobwebs (3), and the TOPPLING BOOKSHELF
/// (5), which is a real prop with a real physics-shaped fall and five materials riding its published
/// pose and is emphatically not an apparition. In the forest only the two eyeshines (0) are left to
/// the shader, and they could not be a game monster even in principle — there is no creature in the
/// roster that is a pair of eyes. THIS LIST IS THE BAKE'S, and the bake prints it: grep
/// <c>HAUNT FORCE ID TABLE</c> in BuildEnvironmentRooms.cs. It said "the face at floor level (2)"
/// and named four forest cards that no longer exist for one build after ModBuild 147 renumbered
/// both catalogues.</para>
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

    // =============================================================================================
    //  THE NO-BACKWARDS-STEP INTERLOCK. Two fields, and between them they make "a visible figure
    //  never jumps" a PROPERTY OF THE CODE rather than a property of the numbers.
    //
    //  USER REPORT, ModBuild 148, verbatim: "Die Animationen der Figuren die sich bewegen
    //  teleportieren sich immer noch anstatt flüssig zu gehen. Ich vermute es liegt daran das die
    //  Animation wiederholt wird und sie eben immer von einem punkt weiter vorne startet. Kannst du
    //  die Laufanimationen nicht loopen ohne eine bewegung und die Bewegung selber koordinieren
    //  damit keine Teleportation stattfindet?"
    //
    //  WHAT THE ARITHMETIC ACTUALLY SAYS about the loop he suspects, worked through for the one
    //  event in either room that moves (ForestCross: 6.8 m, reveal 1.00 + hold 2.40 + fade 0.80 =
    //  4.20 s, looped by a latched trigger every Haunt.ForceLoopSeconds = 4.20 + 1.40 = 5.60 s):
    //    * Envelope() returns EXACTLY 0 for t > reveal+hold+fade — `down` is a clamped smoothstep
    //      whose argument is negative there — so the figure's presence is 0 for the whole 1.40 s
    //      gap, and Clone.Shade switches the RENDERERS OFF at presence 0 rather than writing black.
    //    * At the restart t returns to ~0 and Envelope returns 0 again (`if (t <= 0f) return 0f`,
    //      and smoothstep(t/reveal) is 0.0007 one frame later at 90 Hz).
    //    * The renderers come back on when presence x Lighting.Level clears 0.0015 (Clone.Shade).
    //      At the ModBuild 149 forest Level of 0.078 that needs presence 0.019, i.e. t = 0.081 s,
    //      i.e. u = 0.019 — 13 cm along a 6.8 m path.
    //  So the wrap is invisible with about 1.55 s of margin, and the ModBuild 147 `sameCardLooped`
    //  path really does run now: the hardware log has ONE `armed at` line for a cellar latch that
    //  stood 115.5 s over sixteen loops (.planning/debug/Player.log:20649-20958), where the
    //  ModBuild 146 failure wrote one per loop. THE LOOP IS NOT THE REMAINING BUG.
    //
    //  IT IS STILL WORTH MAKING STRUCTURAL, because that margin is an emergent property of four
    //  numbers in three files (the envelope, the loop gap, the visibility threshold and the light
    //  level) and this round moved one of them: the Level fell by a factor of four, which happens to
    //  widen the margin and could just as easily have closed it. So the blanking below asserts the
    //  gap instead of inheriting it.

    /// <summary>Shared-clock time before which a restarted run may not be drawn AT ALL, whatever its
    /// envelope says. Set on every loop restart, never on a fresh arm (there is no previous position
    /// to jump from).</summary>
    private static float _quietUntil = float.NegativeInfinity;

    /// <summary>Path fraction of the last frame DRIVEN in the current run, or -1 between runs. The
    /// second half of the interlock: within one run <c>u</c> is monotone by construction (it is
    /// <c>t / len</c> and the clock only ever moves forward — a backwards clock retires), so this
    /// can only fire if that construction is ever broken, and when it fires it hides the figure for
    /// that frame rather than showing it behind where it was.</summary>
    private static float _lastDrivenU = -1f;

    /// <summary>How long a restarted run stays hidden. Eleven frames at 90 Hz — long enough that the
    /// anchor's reset can never share a drawn frame with the position it came from, and short enough
    /// to be free: at 0.12 s into a 1.0 s reveal the envelope is 0.040, which after the room's light
    /// level is 0.003 of albedo, i.e. below anything a headset resolves.</summary>
    private const float RestartBlankSeconds = 0.12f;

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

            // ---- THE TELEPORT, AND WHY THIS ONE LINE IS THE WHOLE FIX ----------------------------
            //
            // USER REPORT, verbatim: "Die Laufanimation der Figuren ist immer nur kurz flüssig dann
            // teleportiert sich die figure wieder ein stück nach hinten und läuft wieder nach vorne
            // und teleportiert sich wieder - dieses teleportieren kommt ständig und nimmt jegliche
            // immersion, das muss verschwinden."
            //
            // THE CAUSE IS A LATCHED TEST TRIGGER, and the ModBuild 146 hardware log proves the
            // mechanism rather than suggesting it: nine `HAUNT FIGURES: SwampNight card 3 … armed at
            // shared clock` lines, 2.5 s apart, on ONE latch (.planning/debug/Player.log:27358-27664)
            // — and an `armed` line is only ever written by Arm(), i.e. after a full Retire(). The
            // apparition was not looping. It was being DESTROYED AND REBUILT, over and over.
            //
            // WHY, EXACTLY. A latched force loops by moving _forceSince forward one
            // Haunt.ForceLoopSeconds at a time, and that period is the event's own length PLUS a
            // quiet gap (Haunt.ForceLoopGapSeconds). During that gap `clock` is past `end`, so the
            // test below answered "no event" — `want` went to -1, the driver called
            // Retire("the event ended"), and the clone, its instantiated materials, its Addressables
            // child and its light bind all went with it. The re-anchor then arrived on a later frame
            // and Arm() built the whole thing again from scratch. ModBuild 147 added the
            // `sameCardLooped` path below to keep the creature standing across a loop — IT COULD
            // NEVER RUN, because the gap always retired the figure first. It was dead code from the
            // day it was written.
            //
            // WHAT THE PLAYER SEES WHEN THAT HAPPENS, and it is exactly his three sentences. The
            // rebuild is asynchronous (an Instantiate, a strip, an InitialiseCharacterAsync, a child
            // instantiate), so it takes a variable handful of frames — while the ANCHOR keeps being
            // moved along the path every frame regardless. The figure therefore reappears not at the
            // start of the walk but wherever the anchor had got to by the time it finished loading:
            // "ein Stück nach hinten", by a different amount each time, for ever. And the old
            // dissolve made the disappearance itself invisible-as-a-disappearance — its emissive burn
            // edge was BRIGHTEST at the envelope's ends (HauntFigures.Clone.cs), so the frames that
            // were supposed to read as "gone" read as a glowing thing at the far end of the path.
            //
            // SO THE ARMING WINDOW NOW STAYS OPEN FOR THE WHOLE LOOP PERIOD WHILE A FORCE IS
            // LATCHED. `want` never drops to -1 between two runs, nothing is retired, nothing is
            // rebuilt; the re-anchor lands on `sameCardLooped`, which moves _startClock and nothing
            // else — one float write. The envelope is 0 for the entire gap, and presence 0 now means
            // the renderers are switched OFF (Clone.Shade), so the walk is repositioned across frames
            // on which the creature is not on screen at all. A loop is now "it went, and after a
            // beat it came back", which is what a repeating event can honestly look like.
            //
            // IT IS ASKED OF Haunt RATHER THAN RECOMPUTED: Haunt.ForceLoopSeconds is the side that
            // decides when the re-anchor happens (and it floors short events at 2.5 s), so deriving
            // the window from anything else here would be a fourth copy of a number that already
            // exists three times.
            if (slot.Forced)
                end = slot.StartClock + Mathf.Max(ev.Seconds * slot.DurationMul,
                                                  Haunt.ForceLoopSeconds(style, slot.Card));

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
        // THIS PATH WAS DEAD UNTIL THIS ROUND and the correction belongs next to the claim: the loop
        // period is the event's length PLUS a gap, so `want` went to -1 during that gap and the
        // figure was retired before a re-anchor could ever be recognised as one. The window above now
        // stays open for the whole loop period while a force is latched, which is what finally lets
        // the test below do what it was written to do. The user's report of a figure teleporting
        // backwards was the visible half of that dead code.
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
            // Keep the creature, restart its run. Two float writes and one bool, no allocation, no
            // load — which is the whole difference between this and the teardown it replaces.
            _startClock = wantStart;
            _durMul = wantDur;
            // ...and THIS is the frame the anchor jumps back to the top of the path, so it is the
            // frame the interlock exists for. See the block at _quietUntil.
            _quietUntil = wantStart + RestartBlankSeconds;
            _lastDrivenU = -1f;
            Clone.Restart();
        }

        if (want < 0)
            return;

        // ---- arm / run -------------------------------------------------------------------------
        if (_card < 0)
        {
            _card = want;
            _startClock = wantStart;
            _durMul = wantDur;
            // A FRESH ARM IS NOT A RESTART: there is no previous position for the figure to jump
            // from, and the arming lead already holds it at u = 0 with presence 0 while it loads.
            _quietUntil = float.NegativeInfinity;
            _lastDrivenU = -1f;
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
