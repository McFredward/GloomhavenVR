using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE 3D MAP ROOM — phase 1: the player stands in the campaign map at a table-sized scale and
/// sees the parchment in stereo. Owner of the mode's on/off decision, of the held parchment
/// override and of the head-camera icon layer. It does NOT own the rig; <c>VRRigDriver</c> does,
/// and it asks this class two questions (is the mode wanted, and where is the seat).
///
/// <para>THE ARCHITECTURE IN ONE LINE: do not move the map — scale the rig to it. The game writes
/// ABSOLUTE world positions into every map location (<c>MapChoreographer</c> does it at eight
/// separate spawn sites, and the party token likewise), so a mod transform on a shared ancestor is
/// correct only until the next <c>InitMap</c>, quest unlock, city↔world switch or travel
/// animation. Scaling the player instead touches no game state at all, which is what makes the
/// whole feature multiplayer-safe by construction: two players on different settings are looking
/// at the same map, and the toggle is a PRESENTATION change, not a state migration.</para>
///
/// <para>THE PREDICATE IS POSITIVE, AND THAT IS THE HIGHEST-RISK DECISION IN THE FILE.
/// <c>VRRigDriver</c> re-asserts "MENU rig = the mod layer ONLY" EVERY FRAME, and hardware test
/// #10 is why: on the campaign map the anchor's mask is the whole 3D world, and following it
/// rendered the giant map 1:1 below the player while the flat quad showed on top of it. A map-rig
/// flavour that leaked into the MAIN MENU would therefore break the menu — the loudest possible
/// regression. So the mode is gated on a POSITIVELY DECIDED map-open signal — a
/// <c>MapChoreographer</c> whose <c>worldMap</c> or <c>cityMap</c> is <c>activeInHierarchy</c>,
/// the same signal <c>FlatScreenStereo.TickFastMapEngage</c> already trusts as proof — and NEVER
/// on "not a scenario". In the main menu there is no <c>MapChoreographer</c> at all, so
/// <see cref="Wanted"/> is false, the rig stays <c>Menu</c>, and the menu path is byte-identical
/// to a build without this feature.</para>
///
/// <para>AND IT IS NOT ANCHORED TO THE ORBIT CAMERA. Test #8 anchored the rig to
/// <c>CameraController.s_CameraController</c> on the map scene and produced "giant map below the
/// player, black flat window"; that failure is written into the <c>[Rig] Experimental3DMap</c>
/// config description, and <c>VRRigDriver.UpdateBody</c> carries the standing warning that the
/// switch "must never silently re-enable the broken orbit-camera anchoring". The seat here comes
/// from the PARCHMENT RENDERER'S WORLD BOUNDS (<see cref="MapRoomSeat"/>); the orbit camera is
/// read for one horizontal direction and one culling mask, both with pure fallbacks.</para>
///
/// <para>MULTIPLAYER: phase 1 changes nothing on the wire. Peers are already visible on the map
/// today, unconditionally, and every client's menu rig sits at the same authored vantage — so
/// avatars pile up. That is phase 8's problem, deliberately not fixed here.</para>
/// </summary>
internal static class MapRoomDriver
{
    private const string Scope = "MapRoom";

    /// <summary>
    /// Frames of "no active map" tolerated before the mode stands down. A world↔city switch
    /// deactivates one map GameObject before it activates the other, so a zero-tolerance predicate
    /// would tear the rig down and rebuild it — i.e. TELEPORT the player — every time they press
    /// the city button. ~0.4 s at 72 Hz, far shorter than any real map exit.
    /// </summary>
    private const int StandDownGraceFrames = 30;

    /// <summary>
    /// Frames between fallback <c>FindObjectOfType</c> sweeps — read ONLY inside the RESURRECTION
    /// WINDOW of <see cref="ResolveChoreographer"/>, i.e. on a scene that has already had a live
    /// choreographer while the game's own singleton has since gone cold. On a scenario, where no
    /// choreographer has ever awoken, this constant is never consulted at all.
    /// </summary>
    private const int FindIntervalFrames = 10;

    /// <summary>
    /// How many fallback sweeps the resurrection window is allowed before it gives up and says so
    /// in the log. The race it exists for (a dying instance's <c>OnDestroy</c> nulling the static
    /// AFTER its replacement's <c>Awake</c> set it) resolves within a frame or two, so three
    /// sweeps spread over <see cref="FindIntervalFrames"/> frames each is already generous; an
    /// UNBOUNDED window would be the ModBuild 226 defect again the moment a campaign map is torn
    /// down without a scene load.
    /// </summary>
    private const int ResurrectionSweepBudget = 3;

    private static global::MapChoreographer? _choreo;
    private static int _findFrame;
    private static bool _findFrameValid;
    private static int _absentFrames;
    private static string _verdict = "not evaluated";

    /// <summary>True once a live choreographer has been seen on the CURRENT scene. Cleared by
    /// <see cref="ForgetScene"/>. The whole resurrection window hangs off it — see
    /// <see cref="ResolveChoreographer"/>.</summary>
    private static bool _sawChoreoThisScene;

    /// <summary>The one-shot cross-check sweep of <see cref="ResolveChoreographer"/> is still
    /// owed for this scene. Re-armed by <see cref="ForgetScene"/>.</summary>
    private static bool _auditPending = true;

    /// <summary>Sweeps left in the current resurrection window; refilled every time the singleton
    /// answers, spent while it is cold and a choreographer has been seen on this scene.</summary>
    private static int _resurrectionSweepsLeft = ResurrectionSweepBudget;

    /// <summary>True once the resurrection window has been exhausted and said so (one line, not
    /// one per frame). Cleared whenever the singleton answers again.</summary>
    private static bool _resurrectionGaveUp;

    /// <summary>Lifetime count of full-scene sweeps this class has paid for. Log material — the
    /// number that must stay in single digits for the ModBuild 227 fix to be holding.</summary>
    private static long _sweepsPaid;

    /// <summary>The sweep counter has been declared with the perf monitor (once per session).</summary>
    private static bool _countersRegistered;

    private static readonly MapParchment Parchment = new();
    private static readonly MapIconLayer Icons = new();
    private static readonly MapLocationInteractor Locations = new();
    private static readonly MapButtonRail Buttons = new();
    private static readonly MapRoomHand Hand = new();
    private static readonly MapTableLegs TableLegs = new();

    // Facts the rig hands over at build time so the ONE map-room line can state them all together
    // (a diagnostic split across two lines is a diagnostic a log reader has to correlate by hand).
    private static bool _reportPending;
    private static int _maskBefore;
    private static int _maskAfter;
    private static string _maskSource = "?";
    private static float _reportScale;
    private static Vector3 _reportFloor;
    private static float _eyeHeightMeters;

    /// <summary>True when the mode is wanted THIS frame: the switch is on and a campaign map is
    /// provably open. Read by <c>VRRigDriver</c> to choose the rig flavour.</summary>
    internal static bool Wanted { get; private set; }

    /// <summary>True while the MAP rig is actually standing. This — not <see cref="Wanted"/> — is
    /// what other subsystems (the flat screen) test, so nothing hides before there is a room.</summary>
    internal static bool Active { get; private set; }

    /// <summary>Why <see cref="Wanted"/> currently reads as it does (log material).</summary>
    internal static string Verdict => _verdict;

    /// <summary>The parchment renderer while the room owns one — the bounds the seat came from.</summary>
    internal static MeshRenderer? ParchmentRenderer => Parchment.Renderer;

    /// <summary>The live map choreographer, or null. Exposed so the room's own parts can ask it the
    /// questions it alone can answer (which parents hold the locations, which map is shown) instead
    /// of sweeping the scene.</summary>
    internal static global::MapChoreographer? Choreographer => _choreo;

    /// <summary>True while the room is standing AND the parchment it acquired is the CITY map.
    /// Forwarded from <see cref="MapParchment.IsCity"/> so nothing outside the room has to know
    /// how the surface is decided. Note that <c>MapIconLayer.CurrentSurface</c> — not this — is
    /// what the wire publishes, because it has a real <c>Unknown</c> state for the frames of a
    /// world↔city switch and a bool cannot say "I do not know".</summary>
    internal static bool IsCityMap => Active && Parchment.IsCity;

    /// <summary>The map room's own location input, or null while no room stands. Handed out so the
    /// net module can READ what this client is pointing at and where an icon is drawn, without
    /// sweeping the scene and without any part of WorldUI having to know about the wire.</summary>
    internal static MapLocationInteractor? ActiveLocations => Active ? Locations : null;

    /// <summary>
    /// THE PARCHMENT FRAME — the shared frame wire record 21's poses travel in
    /// (<c>NetProtocol.SharedFrameParchment</c>): origin = the parchment renderer's world bounds
    /// CENTRE, axes = world axes, unit = the room's own derived seat scale.
    ///
    /// <para>WHY NOT RECORD 19'S FRAME. That one is the offset from
    /// <c>PanelLayout.TryGetAnchor</c>, whose position is <c>CameraController.FocusPoint</c> — the
    /// orbit camera's focal point, which every player pans for themselves — and whose yaw is the
    /// seat this client solved from its OWN map camera. Both are per-client, so in the map room
    /// those numbers mean "the same offset from wherever I happen to be looking", not "the same
    /// place". Request 3 asks for the second one: <i>"auch wenn es jemand woanders
    /// hinverschiebt"</i>.</para>
    ///
    /// <para>WHY THIS FRAME IS THE SAME ON EVERY CLIENT. Both terms are pure functions of the SAME
    /// shared parchment: the centre is that renderer's bounds, and the scale is re-derived here by
    /// exactly the arithmetic <see cref="MapRoomSeat.Solve"/> uses —
    /// <c>clamp(widest / TargetMapWidthMeters, MinScale, MaxScale)</c>, off a compile-time target
    /// width that is NOT a config entry. Nothing per-player enters it. It is deliberately the
    /// derived BASE scale and not the live <c>PanelLayout.WorldScale</c>, which includes this
    /// player's own pinch-zoom — a shared frame must not move when one player zooms.</para>
    ///
    /// <para>WHAT WOULD FALSIFY IT, free and without a build: two clients enter the 3D map room and
    /// compare the <c>scale</c> figure in the MAP ROOM ENGAGED line of their two
    /// <c>Player.log</c>s. Identical ⇒ this frame is shared and record 21's frame 1 is sound.
    /// Different ⇒ the parchment bounds are not identical across clients, and record 21 must fall
    /// back to <c>NetProtocol.SharedFrameSeatAnchor</c> — which is one constant in
    /// <c>Net/RemoteMapStory</c> and no wire change, because the frame is a BYTE.</para>
    /// </summary>
    internal static bool TryGetParchmentFrame(out Vector3 center, out float scale)
    {
        center = Vector3.zero;
        scale = 1f;
        if (!Active)
            return false;
        MeshRenderer? r = Parchment.Renderer;
        if (r == null)
            return false;
        Bounds b = r.bounds;
        float widest = Mathf.Max(Mathf.Abs(b.size.x), Mathf.Abs(b.size.z));
        if (widest < MapRoomSeat.MinUsableExtent)
            return false;
        center = b.center;
        scale = Mathf.Clamp(widest / MapRoomSeat.TargetMapWidthMeters,
                            MapRoomSeat.MinScale, MapRoomSeat.MaxScale);
        return true;
    }

    /// <summary>The world point a hover card should fly at — just above the hovered location icon —
    /// or false while nothing is hovered. See <see cref="MapLocationInteractor.TryHoverAnchor"/>.</summary>
    internal static bool TryHoverAnchor(out Vector3 world)
    {
        if (Active)
            return Locations.TryHoverAnchor(out world);
        world = default;
        return false;
    }

    /// <summary>
    /// Press one of the game's own guildmaster bar buttons by mode, through the table rail's
    /// single dispatch (<c>ExecuteEvents.pointerClickHandler</c> on the real Toggle). Used by
    /// <see cref="GuildmasterDestinations.LeaveMode"/> to return to the map, which is what runs
    /// the game's mode Exit. False when the room is down or the bar carries no such button.
    /// </summary>
    internal static bool PressGuildmasterMode(EGuildmasterMode mode, string source) =>
        Active && Buttons.PressMode(mode, source);

    /// <summary>
    /// Evaluate the mode predicate. Called once per frame from <c>VRRigDriver.UpdateBody</c>
    /// BEFORE the rig kind is resolved, because the rig kind depends on the answer.
    /// </summary>
    internal static void TickPredicate()
    {
        if (Plugin.Experimental3DMap == null || !Plugin.Experimental3DMap.Value)
        {
            if (Wanted)
                _verdict = "[Rig] Experimental3DMap is off";
            Wanted = false;
            _absentFrames = 0;
            return;
        }
        if (!VRSession.IsRunning)
        {
            Wanted = false;
            _verdict = "no VR session";
            return;
        }

        if (_choreo == null)
            _choreo = ResolveChoreographer();
        if (_choreo == null)
        {
            DecayAbsence("no MapChoreographer is awake — this is the MAIN MENU and SCENARIO case, "
                         + "and it is why the gate is POSITIVE and never 'not a scenario'");
            return;
        }

        GameObject? world = _choreo.worldMap;
        GameObject? city = _choreo.cityMap;
        GameObject? shown = world != null && world.activeInHierarchy ? world
                          : city != null && city.activeInHierarchy ? city : null;
        if (shown == null)
        {
            DecayAbsence("a MapChoreographer exists but neither worldMap nor cityMap is "
                         + "active in the hierarchy");
            return;
        }

        // AND THE PARCHMENT MUST BE MEASURABLE. "The map GameObject is active" is not the same
        // fact as "there is something to stand on": the mesh can arrive a frame or several later.
        // If the rig flavour changed on the first fact alone, the menu rig would be torn down into
        // that gap and the headset would go untracked. Cheap — the renderer is cached and only
        // re-found across a world↔city switch — and it touches no materials.
        if (!Parchment.Acquire(_choreo))
        {
            DecayAbsence($"campaign map '{shown.name}' is active but its parchment renderer has no "
                         + "usable world bounds yet (no seat can be solved, so the menu rig stands)");
            return;
        }

        _absentFrames = 0;
        // The verdict string is rebuilt only when the DECISION changes — this method runs every
        // frame of every session, including the main menu, and a per-frame interpolated string
        // that nobody reads until the next log line is pure garbage.
        if (!Wanted || !ReferenceEquals(shown, _verdictShown))
        {
            _verdictShown = shown;
            _verdict = $"[Rig] Experimental3DMap on AND MapChoreographer map '{shown.name}' is active in "
                       + "the hierarchy AND its parchment has measurable world bounds";
        }
        Wanted = true;
    }

    /// <summary>The map GameObject <see cref="_verdict"/> was last written for (change detector).</summary>
    private static GameObject? _verdictShown;

    // ==============================================================================================
    //  THE CHOREOGRAPHER LOOKUP — ModBuild 227, and it was 1.974 ms OF EVERY FRAME OF EVERY SCENARIO
    // ==============================================================================================
    //
    // THE REPORT, VERBATIM: "Ich merke deutliche Laggs wenn ich alle Räume von oben anschaue. In VR
    // ist dieses überblickende 'von oben schauen' sehr wichtig, dass es möglich ist."
    //
    // WHAT THE INSTRUMENTS SAID. In the ModBuild 226 capture (a scenario with every room revealed,
    // 43 ms mean frame against an 11.11 ms budget):
    //
    //     [Perf] STEPS  Rig.Update 1.989ms avg, worst 28.80ms, 85.0ms/s, frames 1284
    //                   Rig.MapRoomPredicate 1.974ms avg, worst 28.77ms, 84.4ms/s, frames 1284
    //
    // i.e. 1.974 of Rig.Update's 1.989 ms — 99.2 % of the entire rig frame — was THIS ONE LOOKUP,
    // in a scenario, for a campaign-map feature that cannot possibly stand there.
    //
    // THE PROOF THAT IT IS THE CADENCE AND NOT THE PREDICATE'S ARITHMETIC. Of the 255 [Perf] SPIKE
    // lines in that log whose worst step is Rig.Update, 252 land on a frame with
    // `Time.frameCount % 10 == 9`. FindIntervalFrames was 10. The spike frame IS the sweep frame,
    // 252 times out of 255; the other nine frames in each group of ten cost nothing at all. One
    // sweep priced at 18–29 ms, amortised over ten frames, is exactly the 1.97 ms average and
    // exactly the 28.77 ms worst. There is no second candidate.
    //
    // THE ROOT CAUSE IS THE COMMENT THAT USED TO SIT ON FindIntervalFrames: "Near-free when the
    // scene has none (the scan is type-indexed)". THAT IS FALSE, and Core/SceneRegistry.cs already
    // says so in writing off three independent hardware readings: Object.FindObjectOfType<T> is
    // "O(every loaded object), not O(objects of that type) — with Addressables holding a big room's
    // assets resident that is a six-figure scan". A scenario with every room revealed is the
    // largest object graph this game ever holds, which is why the cost RISES across the session in
    // the log (0.94 ms → 1.84 → 1.97 → 2.02 ms average as rooms open) — the signature of a scene
    // sweep, not of a predicate. And the sweep can NEVER succeed in a scenario, so it was paid
    // 6 times a second, forever, to learn the same "no" every time.
    //
    // THE FIX: ASK THE GAME. `MapChoreographer : Singleton<MapChoreographer>`
    // (decompiled/GH.Runtime/MapChoreographer.cs:33), and that base is the plain-static flavour
    // (decompiled/GH.Runtime/Singleton.cs) — `_instance` is written in `Awake` and nulled in
    // `OnDestroy`, with NO lazy FindObjectOfType in the getter (the FFSNet and Chronos Singletons in
    // this game DO have one; this is not that base, and the distinction is load-bearing).
    // MapChoreographer chains both: `Awake` calls `base.Awake()` at line 214 and `OnDestroy` calls
    // `base.OnDestroy()` at line 247. So `Singleton<MapChoreographer>.Instance` is a static field
    // read — free, exact, and available on the very frame the choreographer awakes instead of up to
    // ten frames later. The same shape is already shipped in this codebase eight times over
    // (Singleton<UIOptionsWindow>, Singleton<StoryController>, Singleton<ActorStatPanel>, …) and
    // GuildmasterDestinations retired the identical defect for UIGuildmasterHUD in ModBuild 195.
    //
    // WHY THE STATIC IS NOT MERELY *CHEAPER* BUT AT LEAST AS COMPLETE. Enumerate the ways the two
    // answers could differ:
    //   - Two choreographers alive: the static holds the last to awake; the parameterless sweep
    //     returns an arbitrary one. Neither is more correct, and the game is a singleton by design.
    //   - A choreographer instantiated INACTIVE: `Awake` has not run, so the static is cold — but
    //     `FindObjectOfType<T>()` without `includeInactive` skips it too. Identical.
    //   - A DontSave/HideAndDontSave choreographer: the sweep skips those (the round-6 finding in
    //     MaterialLoaderHeal); the static does not care. The static is the WIDER answer here.
    //   - THE ONE REAL GAP — the destroy-order race: instance B awakes (static = B), then instance
    //     A's OnDestroy runs and nulls the static while B is alive. A sweep would still find B.
    //     This needs a PRIOR choreographer on the same scene, which is precisely what
    //     _sawChoreoThisScene records, and it is the entire justification for the resurrection
    //     window below. On a scene that has never had one awake, there is no path by which a
    //     choreographer exists and the static is cold — so a scenario pays ZERO sweeps.
    //
    // WHY THERE IS STILL A SWEEP AT ALL (the anti-trap). Two of this project's standing lessons
    // apply directly. "A scan that only logs on success hides that it never ran" — so the audit
    // below logs BOTH outcomes and always prints its price, and MapRoom.Sweeps is REGISTERED with
    // the perf monitor so it appears on the [Perf] COUNTS line as an explicit 0/s rather than being
    // omitted (an omitted counter is indistinguishable from an instrument that never ran). And
    // "verify the outcome, not the path" — the one-shot audit does not ASSERT that the static is
    // authoritative, it CHECKS it once per scene against the very call it replaces, and shouts if
    // they ever disagree.
    //
    // REJECTED — a Harmony postfix on MapChoreographer.Awake feeding a ComponentRegistry, the
    // Core/SceneRegistry.cs shape. It is strictly more machinery for strictly less information: the
    // game's own static is written by the very method we would be patching, so the registry would
    // be a copy of a field we can already read for free, plus a patch to keep inventoried.
    //
    // REJECTED — simply raising FindIntervalFrames to 60 or 600. That divides the cost without
    // removing it, keeps a 20–30 ms hitch on a fixed cadence (which is what the user FEELS — a
    // periodic hitch reads worse than a uniform slowdown), and leaves the map room up to ten
    // seconds late to engage. The cadence was never the bug; asking the question that way was.
    //
    // REJECTED — gating on "are we in a scenario" (VRModeStateMachine.ScenarioBoardExists). That is
    // the NEGATIVE gate the class doc forbids in bold: the whole architecture rests on a positively
    // decided map-open signal so the MAIN MENU can never take a map rig. This change does not touch
    // the polarity of the gate — it replaces one positive signal with a cheaper, earlier, strictly
    // no-narrower positive signal.
    //
    // EXPECTED FIGURE: Rig.MapRoomPredicate falls from 1.974 ms avg / 84.4 ms/s to a static field
    // read plus (only on a live campaign map) MapParchment.Acquire's cached-renderer bounds check —
    // i.e. under 0.01 ms/frame and under 0.5 ms/s — and 252 multi-budget spike frames disappear.
    // Rig.Update should read ~0.015 ms avg, because that is all that was ever left of it.
    //
    // FALSIFIER, free and without hardware: grep the next log for 'MAP ROOM DISCOVERY'. One line
    // per scene, and it states the sweep's own price and whether the static agreed with it. If
    // Rig.MapRoomPredicate is still measured in milliseconds after this, the singleton has gone
    // cold with a live choreographer and MapRoom.Sweeps on the [Perf] COUNTS line will be non-zero
    // and climbing — a fact on the line, not an inference.

    /// <summary>
    /// The live <c>MapChoreographer</c>, from the game's own singleton static — see the block
    /// comment above for why that is free, why it is not narrower than the full-scene sweep it
    /// replaces, and what the two fallbacks below are for.
    /// </summary>
    private static global::MapChoreographer? ResolveChoreographer()
    {
        // Declare the sweep counter the first time this class can possibly sweep, so the
        // [Perf] COUNTS line carries "MapRoom.Sweeps 0/s" as an explicit measurement. Registered
        // HERE and not at module init because the switch is off by default and an always-zero row
        // for a feature nobody enabled is noise, not evidence.
        if (!_countersRegistered)
        {
            _countersRegistered = true;
            PerfMonitor.Register("MapRoom.Sweeps");
        }

        // (1) THE FREE ANSWER. A static field read, written by MapChoreographer.Awake.
        if (Singleton<global::MapChoreographer>.IsInitialized)
        {
            global::MapChoreographer live = Singleton<global::MapChoreographer>.Instance;
            if (live != null)
            {
                _sawChoreoThisScene = true;
                _resurrectionSweepsLeft = ResurrectionSweepBudget;
                _resurrectionGaveUp = false;
                return live;
            }
        }

        // (2) THE ONE-SHOT CROSS-CHECK, once per scene, and only while the experimental switch is
        // on (this method is unreachable otherwise). It prices the very call this change removed,
        // which is also the number the OTHER periodic sweeps in this mod should be judged against.
        if (_auditPending)
        {
            _auditPending = false;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            global::MapChoreographer? swept = Object.FindObjectOfType<global::MapChoreographer>();
            watch.Stop();
            NoteSweep();
            double sweptMs = watch.Elapsed.TotalMilliseconds;
            if (swept == null)
            {
                VRLog.Info(Scope,
                    $"MAP ROOM DISCOVERY: the game's own Singleton<MapChoreographer> static reads COLD, "
                    + $"and one full-scene FindObjectOfType<MapChoreographer>() AGREED — it also found "
                    + $"nothing, and it cost {sweptMs:F2} ms to say so. That is the whole reason this "
                    + "class no longer runs one every 10 frames: in ModBuild 226 that sweep was "
                    + "1.974 ms of EVERY frame of a scenario (99.2% of Rig.Update) and it could never "
                    + "have succeeded. The static is written by MapChoreographer.Awake and cleared by "
                    + "its OnDestroy, so from here on the answer is a field read. This line is printed "
                    + "once per scene whether the two agree or not — a cross-check that only spoke up "
                    + "on disagreement would be indistinguishable from one that never ran.");
                return null;
            }
            _sawChoreoThisScene = true;
            VRLog.Warn(Scope,
                $"MAP ROOM DISCOVERY DISAGREEMENT: Singleton<MapChoreographer> reads COLD but a "
                + $"full-scene sweep FOUND '{swept.name}' ({sweptMs:F2} ms). The static is therefore "
                + "NOT authoritative on this scene — either MapChoreographer.Awake stopped chaining to "
                + "base.Awake() (it did chain at decompiled MapChoreographer.cs:214) or a dying "
                + "instance's OnDestroy nulled the static after its replacement awoke. Falling back to "
                + "the sweep for this instance; the map room still works, but Rig.MapRoomPredicate "
                + "will be expensive again and MapRoom.Sweeps on the [Perf] COUNTS line will climb. "
                + "This line is the one to bring to the next round.");
            return swept;
        }

        // (3) THE RESURRECTION WINDOW. Only reachable on a scene that HAS had a live choreographer:
        // the destroy-order race is the one way a live instance can coexist with a cold static, and
        // it needs a prior instance. Bounded, because an unbounded window on a campaign map that is
        // torn down without a scene load is the very defect this change removes.
        if (!_sawChoreoThisScene || _resurrectionSweepsLeft <= 0)
        {
            if (_sawChoreoThisScene && !_resurrectionGaveUp)
            {
                _resurrectionGaveUp = true;
                VRLog.Info(Scope,
                    $"MAP ROOM DISCOVERY: the singleton went cold on a scene that had a choreographer, "
                    + $"and {ResurrectionSweepBudget} bounded re-sweeps did not find a live one — the "
                    + "campaign map is genuinely gone. No further sweeps until the singleton answers "
                    + "again or the scene changes; that bound is what stops a torn-down map from "
                    + "reinstating the per-frame scene scan this class was built to remove.");
            }
            return null;
        }
        if (_findFrameValid && Time.frameCount - _findFrame < FindIntervalFrames)
            return null;
        // NOT a `now - int.MinValue` sentinel: that overflows and the cadence never fires (the
        // documented sentinel-overflow trap). A separate validity bool has no arithmetic in it.
        _findFrame = Time.frameCount;
        _findFrameValid = true;
        _resurrectionSweepsLeft--;
        NoteSweep();
        return Object.FindObjectOfType<global::MapChoreographer>();
    }

    /// <summary>Book one full-scene sweep, on the counter AND in the lifetime total, so "how many
    /// did we pay for" is a number on the <c>[Perf] COUNTS</c> line rather than an inference.</summary>
    private static void NoteSweep()
    {
        _sweepsPaid++;
        PerfMonitor.Count("MapRoom.Sweeps");
    }

    /// <summary>
    /// Grace handling for a transient absence: keep the mode up for
    /// <see cref="StandDownGraceFrames"/> frames so a world↔city switch is not a teleport.
    /// </summary>
    private static void DecayAbsence(string why)
    {
        if (!Wanted)
        {
            _verdict = why;
            return;
        }
        if (++_absentFrames < StandDownGraceFrames)
        {
            _verdict = $"{why} — holding for {StandDownGraceFrames - _absentFrames} more frame(s) "
                       + "(world↔city switch grace)";
            return;
        }
        Wanted = false;
        _absentFrames = 0;
        _verdictShown = null;
        _verdict = why;
    }

    /// <summary>
    /// Solve the seat for the rig build. The view side comes from the game's own map camera —
    /// its HORIZONTAL OFFSET from the focal point is the direction the flat game looks at the map
    /// from, so the player is seated on the side the map was authored to be read from. Only that
    /// direction is taken; position, height and FOV are not (test #8). Falls back to world −Z.
    /// </summary>
    internal static bool TrySolveSeat(out MapRoomSeat.Seat seat, out string sideSource)
    {
        seat = default;
        sideSource = "world -Z (fallback: no readable map camera)";
        if (!Parchment.Ensure(_choreo))
            return false;

        Vector3 side = MapRoomSeat.FallbackViewSide;
        CameraController cc = CameraController.s_CameraController;
        if (cc != null)
        {
            Vector3 diff = cc.m_CameraToFocalTargetDiff;
            if (new Vector3(diff.x, 0f, diff.z).sqrMagnitude > 1e-6f)
            {
                side = diff;
                sideSource = $"CameraController.m_CameraToFocalTargetDiff {diff} (DIRECTION ONLY — "
                             + "no position, no height, no FOV: that is test #8's mistake)";
            }
            else if (cc.m_Camera != null)
            {
                // ModBuild 181 — SECOND SOURCE, AND IT IS THE ONE THAT USUALLY ANSWERS. User: "Der
                // Spawnpunkt soll auch direkt vor dem Tisch sein, so dass man ihn richtig rum
                // direkt sehen kann." The focal DIFF is zero whenever the orbit camera happens to
                // sit on its focus, so on hardware this solve kept landing on the world -Z
                // fallback — an arbitrary edge that has nothing to do with how the map is authored
                // to be read. The camera's own ROTATION is never degenerate: its forward IS the
                // direction the flat game looks at the map from (logged at euler (80, 90, 0), i.e.
                // reading the map from its -X side), so the player belongs on the opposite side of
                // the centre from where that forward points. Still DIRECTION ONLY — no position,
                // no height, no FOV. That remains test #8's mistake and this does not repeat it.
                Vector3 fwd = cc.m_Camera.transform.forward;
                var flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude > 1e-6f)
                {
                    side = -flat;
                    sideSource = $"the map camera's own FORWARD {fwd} negated (DIRECTION ONLY) — the "
                                 + "focal diff was degenerate, and this is the direction the flat game "
                                 + "reads the map from, so the seat is on the side it is authored for";
                }
            }
        }
        return MapRoomSeat.Solve(Parchment.WorldBounds, side, out seat);
    }

    /// <summary>
    /// The culling mask the map rig's head camera must use: the GAME MAP CAMERA'S OWN MASK, read
    /// and not guessed, OR'd with the mod layer. Falls back to the anchor's mask, and finally to
    /// the mod layer alone (i.e. exactly the menu policy) so a failure here can only ever make the
    /// map invisible — never the menu broken.
    /// </summary>
    /// <remarks>
    /// ALLOCATION-FREE, because this runs inside the per-frame mask re-assert. The human-readable
    /// half lives in <see cref="DescribeMapMaskSource"/>, which is called only at rig build.
    /// </remarks>
    internal static int ResolveMapMask(Camera? anchor, out int sourceMask)
    {
        CameraController cc = CameraController.s_CameraController;
        Camera? mapCam = cc != null ? cc.m_Camera : null;
        if (mapCam != null && mapCam.cullingMask != 0)
            sourceMask = mapCam.cullingMask;
        else if (anchor != null && anchor.cullingMask != 0)
            sourceMask = anchor.cullingMask;
        else
        {
            sourceMask = 0;
            return VRLayers.ModLayerMask;
        }
        return sourceMask | VRLayers.ModLayerMask;
    }

    /// <summary>Where <see cref="ResolveMapMask"/> took its source mask from — log material only,
    /// built once per rig build so the per-frame re-assert allocates nothing.</summary>
    internal static string DescribeMapMaskSource(Camera? anchor)
    {
        CameraController cc = CameraController.s_CameraController;
        Camera? mapCam = cc != null ? cc.m_Camera : null;
        if (mapCam != null && mapCam.cullingMask != 0)
            return $"the game map camera '{mapCam.name}'";
        if (anchor != null && anchor.cullingMask != 0)
            return $"the rig anchor camera '{anchor.name}' (no readable map camera)";
        return "NOTHING readable — falling back to the mod layer alone (i.e. exactly the menu policy, "
               + "so the worst case here is an invisible map, never a broken menu)";
    }

    /// <summary>
    /// The map rig has been built. Raises <see cref="Active"/>, tells the flat map render to stand
    /// down (the two must never both own the parchment) and banks the facts the report line needs.
    /// </summary>
    internal static void Engage(int maskBefore, int maskAfter, string maskSource,
                                float scale, Vector3 floorPosition)
    {
        FlatScreenStereo.MapRoomOwnsParchment = true;
        Active = true;
        // ModBuild 190: the travel confirmation. Installed from HERE rather than from
        // WorldUIModule because the room is the only thing it applies to, and because its prefix
        // must be live before the first location can be pressed. Idempotent.
        MapTravelConfirm.Install();
        // The selected character's loadout hand + wrist plate ([WorldUI] MapRoomHand, default on).
        // Nothing is built here — Engage only runs the one capability probe and arms the poll,
        // because the party display arrives with the map HUD several frames after the room does.
        Hand.Engage();
        // TELL THE MODE MACHINE THERE IS A TABLE HERE. Not a mode change — a correction to the
        // premise the three locomotion guards state in their own comments ("no table exists").
        // Without it the player stands in the room and cannot walk, fly, turn or zoom, because
        // every one of those subsystems stands down in Menu2D and the map screen IS Menu2D
        // (no Choreographer ⇒ no scenario). See Core/Events/VRModeStateMachine.TableInFrontOfPlayer.
        Core.Events.VRModeStateMachine.SetModRoom(true);
        _maskBefore = maskBefore;
        _maskAfter = maskAfter;
        _maskSource = maskSource;
        _reportScale = scale;
        _reportFloor = floorPosition;
        _eyeHeightMeters = 0f;
        _reportPending = true;
        // Re-arm the ONE map dump (FlatScreenStereo.LogMapSceneReport) so the room's own entry
        // produces a report too — the flat path's copy describes the same scene through a
        // different camera, and comparing the two is exactly how a later phase sizes what is
        // still invisible. Deliberately NOT a second logger.
        FlatScreenStereo.ArmMapSceneReport();
    }

    /// <summary>Record the player's measured eye height (real metres above the tracking floor) so
    /// the report can state where their eyes actually ended up relative to the parchment.</summary>
    internal static void NoteEyeHeight(float meters)
    {
        if (meters > 0.2f)
            _eyeHeightMeters = meters;
    }

    /// <summary>
    /// Per-frame upkeep while the map rig stands: keep the parchment acquired and overridden
    /// (it is re-found across a world↔city switch) and keep the icon buffer on the head camera.
    /// </summary>
    internal static void TickActive(Camera? head)
    {
        if (!Active)
            return;
        bool have = Parchment.Ensure(_choreo);
        Icons.Tick(have ? head : null, Parchment.Renderer, _choreo);
        // Laser + fingertip on the location icons (phase 4). Runs whether or not the parchment is
        // momentarily unmeasurable: the icons are their own GameObjects with their own colliders,
        // and losing input for the frames of a world↔city switch would be a worse bug than a
        // hover on an icon whose parchment is being swapped underneath it.
        Locations.Tick();
        // The guildmaster bar as physical table buttons (phase 6). Same reasoning as above for
        // running it unconditionally: the buttons are their own GameObjects and do not depend on
        // the parchment being measurable this frame.
        Buttons.Tick();
        // The game's own Reisen/Abbrechen buttons ride the floated quest window (phase 4b) — they
        // live in the flat map HUD, which this room does not draw, so without this they exist and
        // cannot be reached. Level-triggered; see MapTravelConfirm.
        MapTravelConfirm.Reconcile(ModalFallback.FloatedWindowWithId(UIWindowID.QuestPopup));
        // The loadout hand. Unconditional for the same reason as the two above: it hangs off the
        // player's own hand, not off the parchment, so a world<->city switch must not blink it. It
        // is entirely self-guarding (its own dial, its own capability latch, its own try) and never
        // throws into this call.
        Hand.Tick();
        // The four table legs at the corners of the game's own tabletop (user, against ModBuild 197:
        // "Instead I want the TABLE to get TABLE LEGS at its 4 CORNERS, and these should STAND ON
        // THE FLOOR of the environment"). Called UNCONDITIONALLY — not gated on `have` like the rest
        // — because it owns a LIVE style gate: the legs exist only in the two bundled 3D
        // environments, so a style change must be able to tear them down on the very next frame even
        // during the frames of a world<->city switch when the parchment is momentarily unmeasurable.
        // Its BUILD path needs the parchment and guards on it itself. Once standing it is two field
        // reads and a reference compare — it is world-fixed furniture with nothing to keep up to
        // date.
        TableLegs.Tick();
        if (have)
        {
            // The ONE map dump, from the room's own vantage (the flat path calls the same method
            // from its capture camera). One-shot per arm; free after that.
            FlatScreenStereo.LogMapSceneReport(
                CameraController.s_CameraController != null ? CameraController.s_CameraController.m_Camera : null,
                Parchment.Renderer,
                head != null ? head.cullingMask : 0,
                "3D map room");
        }
        if (_reportPending && have && _eyeHeightMeters > 0f)
            EmitReport();
    }

    /// <summary>
    /// Leave the map room: restore the parchment's own materials, detach the icon buffer, release
    /// the flat path's stand-down. Idempotent, and the ONLY exit — <c>VRRigDriver.TearDownRig</c>
    /// and <c>OnDestroy</c> both come here, so "leave nothing standing" has one implementation.
    /// </summary>
    internal static void StandDown(string reason)
    {
        if (!Active)
            return;
        Active = false;
        // Drop the table premise FIRST, before anything else is released: from this line on the
        // locomotion guards must read the plain Menu2D rule again, and the environment (which is
        // gated on the same predicate) must stand down with the room rather than one frame after it.
        Core.Events.VRModeStateMachine.SetModRoom(false);
        _reportPending = false;
        // FIRST: a borrowed card in the player's hand must never outlive the room it was read from.
        Hand.StandDown(reason);
        Locations.Release(reason);
        Buttons.Release(reason);
        // The table legs go with the room they furnish — the prop is world-fixed, so nothing else
        // would ever destroy it.
        TableLegs.Release(reason);
        Icons.Release(reason);
        // Hand the game's travel options back before the room disappears under them — a container
        // left parented into a host we are about to destroy would take the Reisen button with it.
        MapTravelConfirm.Reset();
        // AND THE ROOM'S WINDOWS GO WITH THE ROOM (ModBuild 226, user report 16: "Als ich dann zu
        // einem Szenario gejoint bin, habe ich dort zwei Fenster gesehen, die dort NICHT hingehören
        // … Beides Fenster aus der 3D-Map-Umgebung"). Deliberately AFTER MapTravelConfirm.Reset —
        // the travel container is parented INTO one of these windows and has to be handed back
        // before its host is released, or it goes home through a host that no longer exists — and
        // BEFORE the room's own furniture, so the sweep still runs even if a later release throws.
        // The count and the names are logged every time, including zero. See ReleaseMapRoomFloats
        // for the hardware evidence that a released float is not a closed window.
        ModalFallback.ReleaseMapRoomFloats(reason);
        Parchment.Release(reason);
        FlatScreenStereo.MapRoomOwnsParchment = false;
        VRLog.Info(Scope, $"MAP ROOM stood down ({reason}) — parchment materials restored, icon command "
                          + "buffer detached, the flat map render is free to take over again.");
    }

    /// <summary>
    /// Drop the cached choreographer on a scene change (Unity fake-null revives the find) and
    /// re-arm every per-scene fact the lookup hangs off: the one-shot cross-check, the "a
    /// choreographer has been seen here" flag that is the ONLY thing that can ever re-enable a
    /// sweep, and the resurrection budget. Registers the sweep counter at ZERO for the new scene
    /// as well, so the <c>[Perf] COUNTS</c> line prints "MapRoom.Sweeps 0/s" as a measurement
    /// rather than omitting the row — an omitted counter reads exactly like an instrument that
    /// never ran.
    /// </summary>
    internal static void ForgetScene()
    {
        _choreo = null;
        _findFrameValid = false;
        _findFrame = 0;
        _sawChoreoThisScene = false;
        _auditPending = true;
        _resurrectionSweepsLeft = ResurrectionSweepBudget;
        _resurrectionGaveUp = false;
        PerfMonitor.Register("MapRoom.Sweeps");
    }

    /// <summary>
    /// THE map-room line. One line, everything a hardware round needs to decide whether the
    /// architecture survived: the predicate's verdict, the bounds it anchored to, the scale and
    /// eye height it produced IN REAL METRES, the culling mask before and after, and what it
    /// overrode. Measured values are labelled measured; the two derived heights say what they are
    /// derived from.
    /// </summary>
    private static void EmitReport()
    {
        _reportPending = false;
        MeshRenderer? r = Parchment.Renderer;
        Bounds b = Parchment.WorldBounds;
        float scale = Mathf.Max(_reportScale, 0.0001f);
        float widest = Mathf.Max(Mathf.Abs(b.size.x), Mathf.Abs(b.size.z));
        float eyeAboveTop = _eyeHeightMeters - MapRoomSeat.TableTopHeightMeters;
        VRLog.Info(Scope,
            "MAP ROOM ENGAGED.\n"
            + $"  predicate : WANTED — {_verdict}. (The gate is positive by construction: no "
            + "MapChoreographer ⇒ no map rig, so the MAIN MENU is untouched and keeps the "
            + "mod-layer-only mask of test #10.) The choreographer was found through the game's own "
            + $"Singleton<MapChoreographer> static; this session has paid for {_sweepsPaid} full-scene "
            + "sweep(s) in total, and anything above the one-per-scene cross-check is the ModBuild "
            + "227 regression — see MAP ROOM DISCOVERY and MapRoom.Sweeps on [Perf] COUNTS.\n"
            + $"  parchment : '{(r != null ? r.name : "<none>")}' on layer "
            + $"{(r != null ? r.gameObject.layer : -1)}, {(Parchment.IsCity ? "CITY" : "WORLD")} map. "
            + $"World bounds center {b.center} size {b.size} (thickness {Mathf.Abs(b.size.y):F3} world "
            + $"units, widest horizontal extent {widest:F2}). MEASURED off the live renderer.\n"
            + $"  scale     : {scale:F2} game units per real metre = widest extent {widest:F2} / target "
            + $"{MapRoomSeat.TargetMapWidthMeters:F2} m. The map therefore reads "
            + $"{widest / scale:F2} m across.\n"
            + $"  seat      : tracking floor at {_reportFloor}, i.e. "
            + $"{MapRoomSeat.TableTopHeightMeters:F2} m (real) below the parchment top y={b.max.y:F2} and "
            + $"{MapRoomSeat.EdgeStandoffMeters:F2} m (real) outside the map's near edge.\n"
            + $"  eye       : {_eyeHeightMeters:F2} m above the tracking floor (MEASURED from the tracked "
            + $"head pose), i.e. {eyeAboveTop:F2} m above the parchment surface — a person standing at a "
            + "table.\n"
            + $"  mask      : 0x{_maskBefore:X8} → 0x{_maskAfter:X8}, source = {_maskSource} (READ off the "
            + "live camera, not guessed).\n"
            + $"  overrode  : the parchment renderer's {(r != null ? r.sharedMaterials.Length : 0)} submesh "
            + $"material(s) — override {(Parchment.Applied ? "HELD" : "NOT APPLIED, see the warning above")}. "
            + "Nothing else on the map is overridden, so anything ELSE that is invisible is a deferred "
            + "renderer with no forward pass — read the MAP SCENE REPORT's pass-name census to see which.\n"
            + "  DISPROOF  : if the next log shows this line but the headset shows a black or absent map, "
            + "the parchment is not the thing being looked at — compare the seat above with the MAP SCENE "
            + "REPORT's map-root bounds. If the map is visible but EMPTY, the icon layer is the suspect "
            + "(MAP ROOM icons line). If the MAIN MENU regressed, this line must be absent there; if it is "
            + "present in the menu the predicate is wrong, not the mask.");
    }
}
