using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Combat log as a grabbable, WORLD-STATIC flat panel (test #19 moved it out of
/// the fixed arc-slot layout in TablePanelSurfaces; test #20 killed the follow +
/// billboard behavior).
/// Verified target: <c>[RequireComponent(typeof(UIWindow))] public class
/// CombatLogHandler : Singleton&lt;CombatLogHandler&gt;, IPointerEnterHandler, ...</c>.
/// The ScrollRect keeps working — poke/laser drags synthesize real pointer events
/// on the host raycaster.
///
/// STATIC IN THE WORLD (test #20: "content shifts with my head, keeps rotating to
/// face me"): the test-#19 yaw billboard re-faced the head EVERY TICK — correct
/// against the #19 skew, but the constant re-facing itself read as the content
/// tracking head motion. Now orientation is computed at DERIVE EVENTS only —
/// once per conversion, on grab release, and (FOLLOW mode) when the seat yaw
/// re-derives on a rig rebuild/recenter — always upright on world up (zero
/// roll/pitch), yaw toward the head at that moment. Between events the panel
/// behaves exactly like the control board: it does not move at all.
///
/// GRAB (test #19: movable/scalable/pinnable EXACTLY like the control board): the
/// SHARED WINDOW ROD (<see cref="GrabBarVisual"/>, sized by
/// <see cref="GrabBarLayout"/> and presented by <see cref="GrabBarTween"/>) under
/// the panel's bottom edge drives the tray's shared <see cref="PanelGrabHandle"/>
/// core — one hand moves, two hands resize (0.5×–2×), and the final release
/// persists the layout as [WorldUI] CombatLog* (table-anchor offsets in real
/// meters + the size factor), so it survives sessions and diorama scale. Yaw
/// carry is ON like the tray (no billboard is fighting the carry anymore);
/// release snaps the panel upright with its yaw toward the head, then it freezes
/// again. Until 2026-09-05 this bar was a stretched <c>PrimitiveType.Cube</c> with
/// four size constants of its own — the "alter Greifbalken" of the user's report.
///
/// FOLLOW/PINNED: the CONTROL BOARD'S OWN dashboard keycap
/// (<c>PlayTray.BoardButton.CreateFollowPin</c> — same dials, same aged brass,
/// same engraved state symbol) next to the bar flips [WorldUI]
/// CombatLogFollowSeat — and since 2026-09-05 it also runs THE CONTROL BOARD'S OWN
/// MECHANISM (<see cref="FollowPinAnchor"/>), because the words meant two
/// different things on the two objects. User ruling, verbatim: "Beim Kampflog
/// funktioniert das 'Folgen' nicht genau gleich wie es bei dem Controlboard der
/// Fall ist. Wieso nicht? Es soll hier am besten den selben Code nutzen und sich
/// genauso verhalten was fixiert und folgen genau bedeutet."
///
/// <para>WHAT THEY MEAN NOW, ON BOTH OBJECTS. FOLGEN re-parents the frame onto the
/// rig-space anchor and stops: the rig transform carries it, so it keeps the pose
/// the player gave it relative to their play space and it scales with the diorama.
/// FIXIERT re-parents it onto a world-static holder whose scale is the rig's
/// FROZEN AT PIN TIME, so a pinned panel is completely unaffected by a world-grab
/// zoom (position AND size), and it is carried through a tracking-origin change
/// (recentre / rig rebuild) by its rig-relative pose so a pin can never be
/// stranded at the old seat. Toggling either way MOVES NOTHING — the re-parent
/// preserves the world pose, which is the board's item-4 rule.</para>
///
/// <para>WHAT IT USED TO MEAN HERE, i.e. the answer to his "wieso nicht": FOLLOW
/// re-derived <c>anchor + yaw * (offset * worldScale)</c> from three persisted keys
/// on EVERY TICK, so it followed the TABLE rather than the player and toggling into
/// it teleported the panel to its config offsets; PINNED froze the world POSITION
/// but re-read the diorama scale into the holder every tick, so a pinned panel
/// still swelled with the world-grab zoom. Both are gone. A pinned WORLD pose still
/// does not survive a session (the tray rule): each conversion seats the panel ONCE
/// from the persisted offsets, healed into the forward view, and the anchor owns it
/// from there.</para>
///
/// CLOSE: the SHARED X (<see cref="ModalCloseButton"/>) on the host canvas, the
/// same muted plate and brass cross every floated window wears, running THIS
/// surface's own close action (<c>SetUserVisible(false, "X button")</c>) rather
/// than <c>UIWindow.Escape()</c>.
///
/// INPUT: poke works on both controls (PokeableBehaviour self-registration for the
/// pin, the host's own poke surface for the X). THE LASER REACHES BOTH TOO, by two
/// different routes and neither of them the control board's: the X is a uGUI
/// button on a registered poke surface, so RayUguiDriver drives it like any other
/// converted widget, and the 3D pin is covered by this surface's own geometric
/// scan (see TickCapLaser). Up to ModBuild 350 both caps rode PlayTray.LaserTargets,
/// whose scan returns immediately when the tray is hidden, so a combat log standing
/// on its own had a close cross the laser could not press.
///
/// TRANSFORM LAYOUT: anchor (the rig root while FOLGEN, the world-static pin
/// holder at its frozen rig scale while FIXIERT — <see cref="FollowPinAnchor"/>)
/// → frame (grab root at the BAR CENTER; localScale = user size factor 0.5–2)
/// → bar/pin visuals. The frame's lossyScale is therefore the anchor's scale ×
/// factor — exactly the scale the converted host is placed with, so bar and panel
/// resize together, and the grab core's 0.5–2 localScale clamp keeps its meaning
/// (the tray's _pinRoot trick, now literally the tray's code). Until 2026-09-05
/// this was a private identity-pose holder re-reading the live diorama scale every
/// tick, which is why a FIXIERT panel used to zoom with the world. The game-owned
/// host is pose-followed, never re-parented (mount-seam reversibility rule).
/// </summary>
internal sealed class CombatLogSurface : WorldSurface, IPanelGrabOwner
{
    // ---- THIS PANEL'S CHROME IS NOT ITS OWN ANY MORE (2026-09-05) -----------------------------
    //
    // USER RULING, verbatim: "Der Kampflog ist jetzt spawnable, ABER er sieht anders aus als die
    // anderen Fenster. Ich will das du es angleichst: a) Er hat noch den alten Greifbalken - auch
    // soll er den normalen Greifbalken bekommen, b) Der fixiert button sollte gleich sein wie der
    // button am controllboard, c) der X Button ist anders. Am liebsten wäre es mir wenn du so wenig
    // extra code nur für den Kampflog hast wie möglich und es wie ein normales Fenster behandelst."
    //
    // All three deltas had the same cause: this file owned three private copies of chrome that every
    // other floated window gets from a shared owner. The BAR was a GameObject.CreatePrimitive cube
    // with a flat brass material and four size constants of its own, while every other window wears
    // the drawn ROD (GrabBarVisual, three pieces and one material) sized by GrabBarLayout. The PIN
    // was a hand-sized rounded gold cap with a bare word, while the control board's identical
    // control is the [BoardDashboard] keycap with an engraved symbol that swaps on toggle. The X was
    // a loud red 3D BoardButton reading the letter "X", while every other window gets the muted
    // brass cross on the host canvas (ModalCloseButton).
    //
    // So the constants that used to be declared here are GrabBarLayout's, the pin is
    // PlayTray.BoardButton.CreateFollowPin's, and the X is ModalCloseButton's. What is left in this
    // file about chrome is the three things that are genuinely this panel's own: WHERE the frame
    // sits (the bar centre, because the panel grows up from the handle), what the X DOES (this
    // surface's session-visibility seam, not UIWindow.Escape) and the laser scan the pin still needs
    // because it is a 3D cap on a panel that is routinely up without the control board.

    public override string Name => "CombatLog";
    // ONE ACTION AND ONE PREFERENCE, never one toggle doing both jobs — see the
    // SHOW/HIDE seam below for what this used to be and why it could not work.
    protected override bool ConfigEnabled => _sessionVisible;

    // ---- show/hide seam ---------------------------------------------------------------------

    /// <summary>
    /// <b>THE DEFECT THIS SEAM WAS REBUILT AROUND (user, 2026-09-05, verbatim): "Der Kampflog wird
    /// als Fenster nicht mehr angezeigt. Er taucht einfach gar nicht mehr auf, auch wenn ich ihn in
    /// den Einstellungen einschalte."</b>
    ///
    /// <para>The old seam was <c>ConfigEnabled =&gt; [WorldUI] CombatLog &amp;&amp;
    /// ![WorldUI] CombatLogUserClosed</c>, and the second term was a PERSISTED latch that only
    /// <c>SetUserVisible(true, …)</c> could clear. Two independent commits then killed it, and
    /// either one alone was enough:</para>
    /// <list type="number">
    /// <item><b>9a6db78c</b> ("remove the old VR settings panel and everything that fed it",
    /// 2026-07-30) deleted the ONLY call site that ever passed <c>true</c> —
    /// <c>v =&gt; CombatLogSurface.SetUserVisible(v, "settings")</c>. Its replacement, the curated
    /// row in the game's own options window, writes the <c>[WorldUI] CombatLog</c> ConfigEntry
    /// directly and has never touched the latch. From that commit on the latch could go true and
    /// never false, and the X button — the only remaining writer — is on a panel that is not
    /// there to press.</item>
    /// <item><b>c5d6bbc9</b> ("every shipped default now lives on one annotated line in
    /// Defaults/") promoted the tester's LIVE value into the shipped default:
    /// <c>Defaults.CombatLogUserClosed = true</c>, where the original bind had said
    /// <c>false</c>. So every config that had never pressed the (already deleted) toggle started
    /// latched CLOSED. That is the <i>a refactor promoted an A/B value</i> shape exactly.</item>
    /// </list>
    ///
    /// <para><b>WHAT THE ModBuild 435 LOG PROVES.</b> In one session the mod logged
    /// <c>Converted 'InitiativeTrack'</c>, <c>Converted 'ElementBoard'</c> and
    /// <c>Converted 'Objectives'</c> three times each (three scenario entries) and
    /// <c>Converted 'CombatLog'</c> ZERO times — <c>CanvasConversion.Convert</c> logs that line
    /// unconditionally and the tester was running the debug tier, so its absence is not a tier
    /// artefact. At the same three moments the mod's own OPTIONS KEY census listed
    /// <c>'CombatLog'</c> among the OPEN game windows. The target existed and was open the whole
    /// time; the surface's gate never once asked for it. Nothing was hidden, mis-placed or
    /// mis-drawn — it was never built.</para>
    ///
    /// <para><b>THE SHAPE THAT REPLACES IT.</b> A persisted latch that outlives the only thing
    /// able to clear it is a trap, so there is no persisted latch any more.
    /// <c>[WorldUI] CombatLogUserClosed</c> is UNBOUND (a stale cfg line is an inert BepInEx
    /// orphan and drops on the next save). Visibility is SESSION state — one bool, no file — and
    /// exactly two things write it: the start-up preference <c>[WorldUI] CombatLog</c> at each
    /// scenario entry, and the player, live, via the "Kampflog jetzt einblenden" options button
    /// or the panel's own X. The worst a wrong session state can now cost is one button press.
    /// </para>
    ///
    /// <para><b>MULTIPLAYER: NO WIRE FIELD, AND NOT BECAUSE IT WAS FORGOTTEN.</b> The combat log
    /// panel is this client's own presentation of a window the game gives every client
    /// independently: <c>CombatLogHandler</c> is a local singleton fed by the same rule messages
    /// every peer already receives, so there is no shared value here to agree on. The bool this
    /// seam writes decides whether THIS headset sees a panel, exactly like the wrist HUD or the
    /// loading spinner, and it drives no game call — the X and the spawn button both end in
    /// <c>CanvasConversion.Convert/Release</c> on mod-owned objects. Putting it on the wire would
    /// mean one player's X closing another player's log, which is the opposite of what a local
    /// window is for, and there is no "sync this sub-feature" dial because there is nothing to
    /// sync. The panel's POSE is local for the same reason: it rides the persisted
    /// <c>[WorldUI] CombatLog*</c> offsets, which are per-installation tuning.</para>
    /// </summary>
    private static bool _sessionVisible;

    /// <summary>
    /// True once the player has spoken for THIS scenario (spawn button or X). While it stands the
    /// scenario-entry seed does not overrule the choice; the gate's FALLING edge clears it, so the
    /// next scenario starts from the preference again. It is also what makes a press from the map
    /// room ("show it when I get there") survive into the scenario that follows.
    /// </summary>
    private static bool _manualOverride;

    /// <summary>Previous state of the conversion gate — the edge detector for the two rules above.</summary>
    private static bool _gateWasOpen;

    /// <summary>What last made the panel visible, for the HW-VERIFY line. Never a decision input.</summary>
    private static string _shownBy = "nothing yet";

    // Change-dedup for the show/hide log. Nullable so the FIRST action always logs — a
    // plain bool seeded false silently swallowed an initial hide (the panel is shown by
    // default), which read as the toggle doing nothing.
    private static bool? _loggedVisible;

    // Set by every SHOW (spawn button / start-up spawn / X-recover): the next Place() ignores the
    // (possibly stale or grabbed-away) persisted pose and drops the panel in front of the head,
    // then persists THAT — so a spawn always brings the log INTO VIEW, and an X-close is always
    // recoverable to where the user is looking. Static because the writers are static (the options
    // button has no surface reference); consumed once, in Place().
    private static bool _respawnRequested;

    /// <summary>
    /// The options row <b>"Kampflog jetzt einblenden"</b> — the ACTION half of the 2026-09-05
    /// request ("ich möchte nicht, dass das ein Einschalten ist, sondern einfach nur ein Button
    /// 'Spawn Kampflog' oder so"). It spawns the panel in front of the head and marks the choice
    /// as the player's, so the scenario-entry seed will not undo it.
    ///
    /// <para>Pressing it outside a scenario is not a no-op and not an error: the gate is shut, so
    /// nothing can be converted yet, but <see cref="_manualOverride"/> carries the request across
    /// the gate's next rising edge and the log comes up with the scenario.</para>
    ///
    /// <para>THIS IS NOT REACHABLE THROUGH THE OPTIONS KEY. The key opens and closes the pause
    /// menu and touches no other window (standing ruling); this row is a row INSIDE the mod's
    /// settings tab, pressed by hand like every other row there.</para>
    /// </summary>
    internal static void SpawnFromOptions() => SetUserVisible(true, "spawn button");

    /// <summary>
    /// Show/hide the combat log for THIS SESSION. Writes no ConfigEntry: the start-up preference
    /// <c>[WorldUI] CombatLog</c> is the player's answer to a different question ("should it be up
    /// when a scenario begins?") and a live show/hide may not silently re-answer it. SHOW requests
    /// a respawn so the next tick converts and places the panel in view in front of the head
    /// (never a stale/out-of-view persisted pose). HIDE releases the conversion back to its 2D
    /// home. Change-deduped.
    /// </summary>
    internal static void SetUserVisible(bool visible, string source)
    {
        _sessionVisible = visible;
        _manualOverride = true;
        if (visible)
        {
            _shownBy = source;
            _respawnRequested = true;                        // bring it back into view
        }
        if (_loggedVisible != visible)
        {
            _loggedVisible = visible;
            VRLog.Info("WorldUI", visible
                ? $"Combat log shown ({source}) — converting and placing in front of the head."
                : $"Combat log hidden ({source}) — released to its 2D home; "
                  + "'Kampflog jetzt einblenden' in the VR options brings it back.");
        }
    }

    /// <summary>
    /// SCENARIO ENTRY APPLIES THE START-UP PREFERENCE, and nothing else does.
    ///
    /// <para>Run at the top of <see cref="Tick"/>, i.e. BEFORE the base class reads
    /// <see cref="ConfigEnabled"/>, so the seed and the conversion it implies land in the same
    /// tick rather than one apart.</para>
    /// </summary>
    private void UpdateSessionVisibility()
    {
        bool gate = WorldUIConfig.ConversionActive && Choreographer.s_Choreographer != null;
        if (gate && !_gateWasOpen && !_manualOverride)
            SeedFromPreference();
        else if (!gate && _gateWasOpen)
            _manualOverride = false;     // the next scenario starts from the preference again
        _gateWasOpen = gate;
    }

    /// <summary>Scenario entry with no live choice standing: the preference decides.</summary>
    private void SeedFromPreference()
    {
        bool atStart = WorldUIConfig.CombatLogAtStart.Value;
        _sessionVisible = atStart;
        _shownBy = atStart ? "start-up preference" : "nothing yet";
        _respawnRequested = atStart;
        _loggedVisible = null;           // a new scenario always logs its first verdict
        VRLog.Info("WorldUI", "Combat log start-up preference applied at scenario entry: "
                              + $"[WorldUI] CombatLog = {atStart} → "
                              + (atStart ? "spawning." : "not spawned (use the options button)."));
    }

    /// <summary>
    /// Test #21: the log CONTENT is styled in real 3D — entries recede obliquely
    /// into depth behind the window plane, the round header banner angles backward
    /// and parallax-shifts with head motion. The tilt is baked into the serialized
    /// prefab RectTransforms and shown through the game's perspective UICamera
    /// (fine as styling in 2D; literal geometry on a world-space host), and pooled
    /// entry spawns + the banner's MOVE_LOCAL intro tween keep re-writing it live.
    /// Flatten the subtree per frame: rotations → identity, local z → 0, x/y
    /// animations untouched (<see cref="CanvasConversion.FlattenSubtree"/>).
    /// </summary>
    protected override bool Flatten2D => true;

    /// <summary>
    /// THE FOLGEN/FIXIERT MECHANISM, WHICH IS THE CONTROL BOARD'S AND IS NO LONGER RE-INVENTED HERE
    /// (2026-09-05). See <see cref="FollowPinAnchor"/> for what the two words mean and why this
    /// panel's own answer was a different one. Named holder object so a hierarchy dump says which
    /// pin it is.
    /// </summary>
    private readonly FollowPinAnchor _anchor = new("GloomhavenVR.CombatLogPin", dontDestroyOnLoad: false);

    /// <summary>The mod-owned frame: the grab root AT THE BAR CENTRE, localScale = the user's 0.5x-2x
    /// size factor, PARENTED by <see cref="_anchor"/> — under the rig root while FOLGEN, under the
    /// world-static pin holder while FIXIERT. Its parent therefore carries the diorama scale in both
    /// modes, exactly as the separate identity-pose holder it replaced did, which is what keeps the
    /// grab core's 0.5x-2x localScale clamp and <see cref="SyncBar"/>'s frame-local metres meaning
    /// what they meant.</summary>
    private Transform? _frame;

    /// <summary>The drawn rod — shaft plus two end knobs, ONE material — exactly the object every
    /// floated window's handle is. It replaced a stretched <c>PrimitiveType.Cube</c>.</summary>
    private GrabBarVisual? _bar;

    /// <summary>THE ONE WRITER of the rod's presented pose, shared with <c>GrabbableModal</c> and
    /// <c>SurfaceGrabBar</c>: this class sets a TARGET and the LateUpdate step eases the drawn rod,
    /// its laser capsule and the palm zone toward it. Without it this panel's handle would be the
    /// only one in the mod that still snaps ("es ploppt", 2026-09-03).</summary>
    private GrabBarTween? _barTween;

    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;
    private PlayTray.BoardButton? _pin;
    private Transform? _pinAnchor;

    /// <summary>The shared uGUI close X on the HOST canvas (<see cref="ModalCloseButton"/>), found
    /// once per conversion so the per-tick re-seat costs a null check. It is destroyed with the
    /// host, so it needs no teardown of its own.</summary>
    private RectTransform? _closePlate;

    /// <summary>The solved bar-centre-to-panel-bottom gap, in the frame's units. Written by
    /// <see cref="SyncBar"/> and read by the two placements that hang off the bar (the host itself
    /// and the empty-state note), so the three can never disagree about where the panel's bottom
    /// edge is — it is one number now instead of a constant repeated in three expressions.</summary>
    private float _barGap = GrabBarLayout.BarGapMeters;

    /// <summary>The shared settle rule, one instance per size term, exactly as the other two bar
    /// owners hold it: a host rect that moves and moves straight back must not change the rod's
    /// thickness and its gap ("Wenn sich das Fenster nicht wirklich vergrößert, sollte der
    /// Greifbalken auch nicht größer werden", asked for "allgemein").</summary>
    private readonly BarSizeSettle _widthSettle = new("width");
    private readonly BarSizeSettle _heightSettle = new("height");

    /// <summary>True while the beam is resting on the FOLLOW/PINNED cap — the only 3D cap this
    /// panel still has (see <see cref="TickCapLaser"/>).</summary>
    private bool _pinLaserHovered;

    private bool _placedFromConfig;
    /// <summary>RigPoseVersion the orientation was derived at — and, since the two modes were
    /// unified, the version the HEAL was last adjudicated at. Both questions are "has the tracking
    /// origin moved since we last looked at this panel", so they are one field on purpose.</summary>
    private int _facedPoseVersion = -1;
    private bool _healLogged;           // change-dedup for the out-of-view heal log
    private bool _locHooked;            // subscribed to Loc.OnChanged (live language following)

    // ---- empty state ("Es darf niemals leere Fenster geben") ----------------------------------
    private TMPro.TextMeshPro? _emptyNote;
    private Transform? _emptyAnchor;
    private ScrollRect? _scroll;        // the game's log list, captured once per conversion
    private int _entryCount = -1;       // visible log rows; -1 = not measured yet
    private int _lastChildCount = -1;   // cheap change trigger for the recount
    private float _nextEntryScan;
    private float _fittedNoteWidth = -1f;
    private MeshRenderer? _emptyNoteRenderer;

    // ---- HW-VERIFY bookkeeping ---------------------------------------------------------------
    private int _ticks;                 // UNCONDITIONAL liveness: "never ran" vs "ran and did nothing"
    /// <summary>Tracking-origin carries this pin has actually taken (recentre / rig rebuild). Zero
    /// across a session with recentres in it means the FIXIERT pin is not being carried, which is
    /// the one way a pinned panel can strand at the old seat.</summary>
    private int _pinCarryCount;
    private int _verifyLines;
    private int _verifySuppressed;
    private int _lastVerdict = int.MinValue;

    protected override RectTransform? FindTarget() =>
        Singleton<CombatLogHandler>.IsInitialized
            ? Singleton<CombatLogHandler>.Instance.transform as RectTransform
            : null;

    /// <summary>
    /// Test #20: NO dynamic content re-fit for this panel. The host kept thrashing
    /// 569x138 ↔ 569x291 as log entries faded in and out (the test #17 damping only
    /// slowed the churn), and on a static panel every re-fit reads as the content
    /// jumping. The combat log converts at its own full window rect — the game's
    /// max layout, small enough to stand as the permanent laser/poke plane — so the
    /// host stays pinned there: static beats hugging. (A degenerate convert keeps
    /// the fit — the 100 px placeholder is no real layout to pin to.)
    /// </summary>
    protected override void OnConverted()
    {
        if (Panel != null && !Panel.FitFrameDegenerate)
            Panel.FitEnabled = false;
        // The game's own log list, captured ONCE per conversion (the per-tick recount below reads
        // only childCount off it). Missing is a legitimate answer — the empty note then keeps its
        // last verdict rather than guessing, and the HW-VERIFY line reports entries as unknown.
        _scroll = Panel != null && Panel.HostRect != null
            ? Panel.HostRect.GetComponentInChildren<ScrollRect>(true)
            : null;
        _entryCount = -1;
        _lastChildCount = -1;
        _nextEntryScan = 0f;
        // A SETTLED SIZE DESCRIBES A RECT THAT NO LONGER EXISTS. Reset before the frame is built, so
        // a second conversion never holds the previous float's rod size for a whole settle window on
        // a panel it was never measured against.
        _widthSettle.Reset();
        _heightSettle.Reset();
        EnsureFrame();
        AttachPanelChrome();
    }

    /// <summary>
    /// The two pieces of chrome that belong to the CONVERSION rather than to the frame: the rod's
    /// place on the panel's draw-order ladder, and the close X.
    ///
    /// <para>Both are per-<see cref="ConvertedPanel"/> and this panel is released and re-converted
    /// every time the player hides and re-shows the log, so this runs once per conversion rather
    /// than once per frame build — the mod-owned holder deliberately outlives a conversion (a scene
    /// load destroys it; a hide does not), and a registration made against the previous panel is
    /// dead the moment that panel is.</para>
    ///
    /// <para>THE ORDER FOLLOWERS ARE ALL THREE RENDERERS, not one. The rod is a shaft and two caps;
    /// registering only the shaft would sort it over the panel and leave both knobs behind it, which
    /// is a bar with its ends bitten off. Registration is idempotent per panel, so a second call
    /// costs three reference compares.</para>
    /// </summary>
    private void AttachPanelChrome()
    {
        if (Panel == null)
            return;
        if (_bar != null)
        {
            System.Collections.Generic.IReadOnlyList<MeshRenderer> rs = _bar.Renderers;
            for (int i = 0; i < rs.Count; i++)
                CanvasConversion.RegisterOrderFollower(Panel, rs[i], GrabBarLayout.BarOrderOffset);
        }

        // THE SAME X EVERY OTHER FLOATED WINDOW GETS — a uGUI button on the HOST canvas (a sibling
        // of the game subtree), a muted dark plate carrying a brass cross drawn as two crossed
        // Images, brightening on hover. Because it is a uGUI widget on a registered poke surface it
        // is driven by UguiPokeSurfaces / RayUguiDriver like every other converted widget: the
        // fingertip AND THE LASER reach it through the same ExecuteEvents path, with nothing to
        // register and no scan of this surface's own (ModBuild 348: "Alle buttons müssen auch mit
        // dem Laser drückbar sein"). It is destroyed with the host, so there is no teardown here.
        //
        // THE CLOSE ACTION IS STILL THIS SURFACE'S OWN, and that is the one thing the shared button
        // could not do before today: UIWindow.Escape() would close the GAME's combat log for the
        // flat UI as well and leave this surface's own gate still asking for it. SetUserVisible is
        // the seam that owns whether this panel is up, and the press ends in the panel being
        // released back to its 2D home on the very next tick — the visible confirmation an exit
        // control owes a press.
        _closePlate = null;
        UIWindow? window = Singleton<CombatLogHandler>.IsInitialized
            ? Singleton<CombatLogHandler>.Instance.GetComponent<UIWindow>()
            : null;
        if (window != null)
            ModalCloseButton.Attach(Panel, window, () => SetUserVisible(false, "X button"));
    }

    // ---- IPanelGrabOwner -------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;
    bool IPanelGrabOwner.GrabVisible =>
        Panel != null && _frame != null && _frame.gameObject.activeInHierarchy;
    // No billboard anymore (test #20) — the carry yaws like the tray. Level in the plain WORLD
    // frame (identity): panels are outside the item-11 board-leveling contract.
    PanelCarryMode IPanelGrabOwner.CarryMode => PanelCarryMode.Level;
    Quaternion IPanelGrabOwner.GrabLevelFrame => Quaternion.identity;
    Vector2 IPanelGrabOwner.GrabPitchLimits => new(-180f, 180f);

    /// <summary>The combat log has no apparent-size ruling — the handle's generic factor range
    /// IS its resize window (see <see cref="IPanelGrabOwner.GrabScaleLimits"/>).</summary>
    Vector2 IPanelGrabOwner.GrabScaleLimits => new(PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);

    void IPanelGrabOwner.OnGrabFinished()
    {
        // Test #20: release snaps the panel upright — zero roll/pitch, yaw toward
        // the head at THIS moment — then the pose stays frozen (no re-facing).
        Camera? head = CanvasConversion.WorldCamera;
        if (head != null)
            FaceHead(head);
        PersistLayout();
    }

    // ---- lifecycle -------------------------------------------------------------------------

    public override void Tick()
    {
        _ticks++;                       // liveness, counted before anything can return early
        UpdateSessionVisibility();      // BEFORE base.Tick() reads ConfigEnabled
        base.Tick();
        // Gate closed / scene unloaded: the frame hides with the panel (it must not
        // float alone in the world), and the next conversion re-derives the pose
        // from the persisted offsets (pinned WORLD poses do not survive — tray rule).
        if (Panel == null)
        {
            _placedFromConfig = false;
            _scroll = null;
            _entryCount = -1;
            _lastChildCount = -1;
            // The beam hover is dropped HERE and not only in TickCapLaser, because Place() — where
            // the scan lives — returns before it whenever the panel is down or the table anchor is
            // missing. A hover that survives its own control is the [[gate-outliving-its-edge]]
            // shape: the cap would stay lit and pressed-looking with nothing behind it.
            ClearCapLaserHover();
            if (_frame != null && _frame.gameObject.activeSelf)
                _frame.gameObject.SetActive(false);
        }
        LogVerdict();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_locHooked)
        {
            Loc.OnChanged -= ApplyLocalisedText;
            _locHooked = false;
        }
        if (_frame != null)
            Object.Destroy(_frame.gameObject);
        _frame = null;
        // The world-static pin holder is the frame's PARENT while FIXIERT, so destroying the frame
        // leaves it behind as an empty stray unless it goes too. ResetCarry drops the rig-relative
        // offset with it: the next frame is a different object and must not be carried by an offset
        // measured against the one that is gone.
        _anchor.DestroyHolder(immediate: false);
        _anchor.ResetCarry();
        _bar = null;
        _barTween?.Release();   // leaves the LateUpdate tick list; the rod it presented is gone
        _barTween = null;
        _grabZone = null;
        _handle = null;
        _pin = null;
        _pinAnchor = null;
        _closePlate = null;     // the plate itself dies with the host it is parented to
        _emptyNote = null;
        _emptyAnchor = null;
        _emptyNoteRenderer = null;
        _scroll = null;
        ClearCapLaserHover();
        _widthSettle.Reset();
        _heightSettle.Reset();
        _placedFromConfig = false;
        _facedPoseVersion = -1;
        _pinCarryCount = 0;
        _healLogged = false;
        _entryCount = -1;
        _lastChildCount = -1;
        _respawnRequested = false;
        // Session visibility dies with the session, deliberately: the next run starts from the
        // start-up preference, which is the whole point of retiring the persisted latch.
        _sessionVisible = false;
        _manualOverride = false;
        _gateWasOpen = false;
        _loggedVisible = null;
        _shownBy = "nothing yet";
    }

    // ---- placement (every tick while converted) ----------------------------------------------

    protected override void Place()
    {
        if (Panel == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        EnsureFrame();
        if (_frame == null)
            return;

        float worldScale = PanelLayout.WorldScale;
        if (!_frame.gameObject.activeSelf)
            _frame.gameObject.SetActive(true);

        bool grabbed = _handle != null && _handle.IsGrabbed;

        // ------------------------------------------------------------------ THE SEAT (events only)
        //
        // FOLGEN AND FIXIERT BOTH SEAT THE PANEL EXACTLY ONCE AND THEN LEAVE IT ALONE, which is the
        // whole of the 2026-09-05 ruling ("Es soll hier am besten den selben Code nutzen und sich
        // genauso verhalten was fixiert und folgen genau bedeutet"). Until today FOLLOW re-derived
        // `anchor + yaw * offset * worldScale` on EVERY TICK, so it followed the TABLE, and toggling
        // into it teleported the panel to its config offsets — the exact move the control board's
        // ApplyFollowMode refuses to make in writing. What carries the panel afterwards is the
        // shared anchor (rig-parented while FOLGEN, world-pinned while FIXIERT), never this method.
        if (_respawnRequested && !grabbed)
        {
            // A SHOW (settings toggle / X-recover) always drops the panel in view in front of the
            // head and persists that pose — regardless of FOLLOW/PINNED and any stale offsets — so
            // the toggle can never appear to do nothing (item 1).
            _respawnRequested = false;
            ReseatIntoLiveFrame();
            PlaceInView(head);
            _placedFromConfig = true;
            _facedPoseVersion = Rig.VRRigDriver.RigPoseVersion;
        }
        else if (!grabbed && !_placedFromConfig)
        {
            // FIRST SEAT OF THIS CONVERSION, in BOTH modes: derive from the persisted offsets
            // (a pinned WORLD pose does not survive a session — the tray rule), heal it into the
            // forward FOV, face the head once. From here on the anchor owns the pose.
            ReseatIntoLiveFrame();
            Vector3 offset = new(
                WorldUIConfig.CombatLogRight.Value,
                WorldUIConfig.CombatLogUp.Value,
                WorldUIConfig.CombatLogForward.Value);
            Vector3 candidate = anchor + yaw * (offset * worldScale);

            // Item 2 (test #24): a stale/out-of-view persisted offset — e.g. after an MR
            // toggle re-derived the pose — is HEALED back into the forward FOV here. In
            // view the clamp is a no-op; out of view it pulls the panel in front of the
            // head and we persist the healed offset so it never strands again.
            bool healed = PanelPlacement.ClampIntoView(head, worldScale, ref candidate,
                out Quaternion facing);
            _frame.position = candidate;
            _frame.rotation = facing;
            _facedPoseVersion = Rig.VRRigDriver.RigPoseVersion;
            _placedFromConfig = true;
            if (healed)
            {
                PersistLayout();
                if (!_healLogged)
                {
                    _healLogged = true;
                    VRLog.Info("WorldUI", "Combat log was out of view (stale/MR-toggled pose) " +
                                          "— healed back into the forward field of view.");
                }
            }
            else
            {
                _healLogged = false;
            }
        }
        else if (!grabbed)
        {
            TickAnchorHousekeeping(head, worldScale);
        }

        // THE SIZE FACTOR IS THE PLAYER'S DIAL, NOT AN AUTOMATIC SOURCE. Asserted in both modes
        // (until today only the per-tick FOLLOW branch wrote it, so the options slider was dead on
        // a PINNED panel), change-gated so a frozen panel takes no write on a tick that changes
        // nothing. It is a LOCAL scale under a parent that carries the diorama scale, so it keeps
        // meaning 0.5x-2x of the panel's own size in either frame — the pin holder's whole job.
        if (!grabbed)
        {
            float factor = Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f);
            if (!Mathf.Approximately(_frame.localScale.x, factor))
                _frame.localScale = Vector3.one * factor;
        }

        // …and only NOW is the mode applied, in the pose the seat above just produced: the shared
        // re-parent preserves the world pose, so seating first and pinning second is the same order
        // PlayTray.PlaceAtHead uses and for the same reason.
        ApplyAnchorMode();

        Rect rect = Panel.HostRect.rect; // pinned to the full window layout (OnConverted)
        if (rect.width < 1f || rect.height < 1f)
            return;

        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        // THE FRAME'S OWN WORLD SCALE, read rather than recomposed. It used to be
        // `worldScale * _frame.localScale.x`, which was the same number only while the frame's
        // parent was a holder held at the LIVE diorama scale. A FIXIERT frame now hangs under a
        // holder frozen at pin time, so the live diorama scale is no longer its parent's — and a
        // pinned panel whose host kept resizing with the world zoom would be exactly the "world zoom
        // zooms the pinned control board too" defect the board's holder freeze exists to prevent.
        float hostScale = _frame.lossyScale.x;

        SyncBar(rect, metersPerPixel);

        // Panel grows UP from the bar (bottom-center convention, like the tray mounts). The gap is
        // the one SyncBar just solved, not a constant re-quoted here: on a short panel the shared
        // rule pulls the handle CLOSER to its window, and a fixed gap in this expression would leave
        // the panel where the full-size handle would have put it.
        Vector3 center = _frame.position + _frame.rotation *
            (Vector3.up * ((_barGap + rect.height * metersPerPixel * 0.5f) * hostScale));
        CanvasConversion.PlaceHost(Panel, center, _frame.rotation, hostScale);

        SyncCloseX(rect);
        TickEmptyNote(rect, metersPerPixel);
        TickCapLaser();
    }

    // ---- empty state --------------------------------------------------------------------------

    /// <summary>
    /// <b>"ES DARF NIEMALS LEERE FENSTER GEBEN" (standing constraint).</b> A window you can now
    /// summon on demand can be summoned at any moment — including round 1 of a scenario, before
    /// anything has happened, when the game's own log list holds exactly nothing. The game's flat
    /// UI can afford that (there the log is a strip that has always been there); a panel that the
    /// player just conjured into the room and that comes up as a blank rectangle reads as broken.
    ///
    /// <para>So the panel says what it is and what will fill it, in the player's language, and the
    /// note disappears the instant the first entry lands. It is mod-owned world text parked over
    /// the CENTRE of the host rect in the frame's own local metres — the same frame-local
    /// convention the X corner uses, so it scales with the diorama and the user's size factor
    /// without a second placement rule.</para>
    ///
    /// <para>THE COUNT IS CHANGE-GATED, NOT PER-FRAME. <c>childCount</c> is one integer read; the
    /// loop over the children only runs when that number moved or once a second (a FILTER change
    /// re-uses the same pooled objects via SetActive and moves no count, so the cadence is what
    /// catches it). No allocation on either path.</para>
    /// </summary>
    /// <summary>
    /// Keep the shared close X seated against the window, through the SAME method
    /// <c>GrabbableModal</c>'s follow tick calls — so the two can never disagree about where the
    /// corner of a window is. Idempotent and allocation-free: it only writes when the corner moved.
    ///
    /// <para><b>THE FRAME PATH, DELIBERATELY, AND IT IS THE SHIPPED PLACEMENT.</b> The INK path
    /// needs <c>PanelInkBounds</c>' committed union, which ModalFallback's fit machinery feeds and
    /// which does not exist for a surface panel — the same reason <c>SurfaceGrabBar</c> places its
    /// rod off the host rect. <c>ModalCloseButton.PlaceAgainstInk</c> with <c>inkValid: false</c> is
    /// byte-for-byte what anchor (1,1) + <c>anchoredPosition (-7,-7)</c> resolved to, i.e. the
    /// window's own top-right corner, which is exactly where this panel's own X sat before today.
    /// It is also right on the merits here: <see cref="OnConverted"/> pins this host at the game's
    /// full window layout, so the frame IS the picture rather than a mostly-empty margin.</para>
    /// </summary>
    private void SyncCloseX(Rect rect)
    {
        if (Panel == null || !Panel.IsAlive || Panel.HostRect == null)
            return;
        if (_closePlate == null)
        {
            _closePlate = ModalCloseButton.FindPlate(Panel);
            if (_closePlate == null)
                return;
        }
        ModalCloseButton.PlaceAgainstInk(_closePlate, rect, inkValid: false, ink: default);
    }

    private void TickEmptyNote(Rect rect, float metersPerPixel)
    {
        RectTransform? content = _scroll != null ? _scroll.content : null;
        if (content != null)
        {
            int children = content.childCount;
            if (children != _lastChildCount || Time.unscaledTime >= _nextEntryScan)
            {
                _lastChildCount = children;
                _nextEntryScan = Time.unscaledTime + 1f;
                int visible = 0;
                for (int i = 0; i < children; i++)
                {
                    Transform child = content.GetChild(i);
                    // A CombatLogText, active, is one visible ROW. The component test is not
                    // decoration: a layout spacer or any other permanent child of the content
                    // root would make "childCount > 0" mean "not empty" for a log that has
                    // nothing in it, and the note would then never appear on the one panel it
                    // exists for. UpdateLogs recycles filtered-out rows and SetActive(true)s the
                    // ones that come back, so activeSelf is the game's own visible/not verdict.
                    if (child != null && child.gameObject.activeSelf
                        && child.GetComponent<CombatLogText>() != null)
                        visible++;
                }
                _entryCount = visible;
            }
        }

        // Unknown (no ScrollRect found) is NOT empty: a note over a panel that may well be full is
        // worse than no note, and the HW-VERIFY line reports the unknown so it can be fixed.
        bool empty = _entryCount == 0;
        if (_emptyNote == null || _emptyAnchor == null)
            return;
        if (_emptyNote.gameObject.activeSelf != empty)
            _emptyNote.gameObject.SetActive(empty);
        if (!empty)
            return;
        _emptyAnchor.localPosition = new Vector3(
            0f, _barGap + rect.height * metersPerPixel * 0.5f, -0.004f);

        // DRAW IT OVER THE PANEL, NOT UNDER IT. A converted host is a world-space uGUI canvas that
        // writes no depth and rides a sortingOrder ladder rewritten every frame from its measured
        // eye distance (CanvasConversion.8.Order); a plain mod renderer sits at sortingOrder 0 and
        // would be painted BEFORE it — i.e. invisible behind the very panel it annotates, at any z
        // offset. One above the panel's live order is the same term the furniture pass writes, so
        // the note tracks the ladder instead of fighting it. Change-gated.
        if (_emptyNoteRenderer != null && Panel != null && Panel.HostCanvas != null)
        {
            int want = Panel.HostCanvas.sortingOrder + 1;
            if (_emptyNoteRenderer.sortingOrder != want)
                _emptyNoteRenderer.sortingOrder = want;
        }

        // Fit into the panel's own width, change-gated on that width (the host rect is pinned by
        // OnConverted, so in practice this runs once per conversion).
        float boxWidth = rect.width * metersPerPixel * 0.84f;
        if (Mathf.Abs(boxWidth - _fittedNoteWidth) > 0.002f)
        {
            _fittedNoteWidth = boxWidth;
            TmpFit.Fit(_emptyNote, boxWidth, rect.height * metersPerPixel * 0.6f,
                maxFontSize: 0.05f, wrap: true);
        }
    }

    /// <summary>Re-state every mod-owned string on this panel after a live language change.</summary>
    private void ApplyLocalisedText()
    {
        ApplyPinVisual();
        ApplyEmptyNoteText();
    }

    /// <summary>The empty-state sentence, in the player's language. Re-fits on the next tick.</summary>
    private void ApplyEmptyNoteText()
    {
        if (_emptyNote == null)
            return;
        _emptyNote.text = Loc.Mod("combatlog_empty");
        _fittedNoteWidth = -1f;     // a new string needs a new fit
    }

    /// <summary>
    /// Respawn placement (item 1): drop the panel a comfortable reading distance in front of
    /// the head, slightly below eye level, upright and facing the head, then PERSIST it as the
    /// new layout. Used whenever the log is shown from the settings toggle or recovered from its
    /// X close — the panel is guaranteed to appear where the user is looking, healing any stale
    /// or grabbed-away persisted pose that would otherwise re-show it out of view.
    /// </summary>
    private void PlaceInView(Camera head)
    {
        if (_frame == null)
            return;
        float worldScale = PanelLayout.WorldScale;
        // Shared in-view spawn (item 2): a comfortable reading distance in front of the
        // head, slightly below eye level, upright and facing the head.
        PanelPlacement.Spawn(head, worldScale, out Vector3 pos, out Quaternion rot);
        _frame.position = pos;
        _frame.rotation = rot;
        _frame.localScale = Vector3.one * Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f);
        _healLogged = false;
        // Re-author the pin against THIS pose and the CURRENT tracking origin. Without both halves
        // the next origin change would carry the panel by an offset measured before this deliberate
        // re-seat and quietly undo it — the same pair of lines the board's arrival seat guard runs
        // for the same reason. A no-op while FOLGEN (nothing is pinned).
        _anchor.ReauthorOrigin();
        _anchor.RecacheRigLocal(_frame);
        PersistLayout();
    }

    /// <summary>
    /// The rig-space anchor this panel follows while FOLGEN: the rig root itself, which is the very
    /// transform <see cref="PanelLayout.WorldScale"/> reads its diorama scale off, so the frame's
    /// parent scale and every other panel's scale term are the same number by construction. (The
    /// control board uses the HANDS root instead — a child of this one at identity local scale —
    /// because a board without hands has nothing to be docked to; a combat log routinely stands on
    /// its own with the hands down, so it anchors one level up.)
    /// </summary>
    private static Transform? RigAnchor => Rig.VRRigDriver.RigRoot;

    /// <summary>
    /// Put the frame back into the LIVE rig frame before a fresh seat is written into it, and drop
    /// any holder frozen at an older rig scale.
    ///
    /// <para><b>THIS IS THE ORDER THE CONTROL BOARD USES AND IT IS LOAD-BEARING, NOT TIDINESS.</b>
    /// <c>PlayTray.EnsureBuilt</c> parents the fresh board onto the rig anchor with
    /// <c>worldPositionStays: false</c> and only then does <c>PlaceAtHead</c> write the pose and the
    /// size, so the board's localScale is ALWAYS authored in a frame that carries the diorama scale.
    /// Writing it at the world root instead and pinning afterwards would be a
    /// <c>worldPositionStays: true</c> re-parent onto a scaled holder, and Unity solves the local
    /// scale to hold the WORLD size across that — i.e. it would silently divide the player's 0.5x-2x
    /// size dial by the diorama scale and PersistLayout would then write that quotient back into
    /// [WorldUI] CombatLogScale.</para>
    ///
    /// <para>Dropping the holder here is the second half: a seat derived from the persisted offsets
    /// is expressed in the LIVE diorama scale, so the pin frozen beside it has to be the live one
    /// too. Without it a hide/show inside a scenario the player had zoomed would put the panel at a
    /// freshly-derived place in a stale size. The frame has already left the holder on the line
    /// above, so destroying it cannot take the panel with it.</para>
    /// </summary>
    private void ReseatIntoLiveFrame()
    {
        Transform? rigAnchor = RigAnchor;
        if (_frame == null || rigAnchor == null)
            return;
        if (_frame.parent != rigAnchor)
            _frame.SetParent(rigAnchor, worldPositionStays: false);
        _anchor.DestroyHolder(immediate: false);
    }

    /// <summary>
    /// Put the frame into the frame its FOLGEN/FIXIERT setting asks for, through the control board's
    /// own mechanism. Idempotent and allocation-free: on the overwhelming majority of ticks both
    /// terms already hold and it returns after two reference compares, which is why it can sit on
    /// the per-frame path at all. The guard is not an optimisation only — <see cref="FollowPinAnchor.Apply"/>
    /// writes the pin holder's scale from the LIVE rig, and calling it every tick on a pinned panel
    /// would be the live holder rescale the board's watchdog forbids in a comment block with no code
    /// under it.
    /// </summary>
    private void ApplyAnchorMode()
    {
        if (_frame == null)
            return;
        bool follow = WorldUIConfig.CombatLogFollow.Value;
        Transform? rigAnchor = RigAnchor;
        bool settled = follow
            ? rigAnchor != null && _frame.parent == rigAnchor && _anchor.Holder == null
            : _anchor.Holder != null && _frame.parent == _anchor.Holder;
        if (settled)
            return;
        _anchor.Apply(_frame, follow, rigAnchor);
    }

    /// <summary>
    /// What keeps a SEATED panel where the player put it, in both modes — the two jobs the control
    /// board's <c>SyncPinHolder</c> plus arrival guard do for the board:
    ///
    /// <list type="number">
    /// <item><b>The tracking-origin carry (FIXIERT only).</b> A pin is raw world space and a
    /// recentre teleports the rig without moving the world, which would strand the panel at the old
    /// seat. Shared with the board, down to the rig-relative cache. FOLGEN needs nothing: it is
    /// rig-parented, so it carries itself.</item>
    /// <item><b>The heal into view (BOTH modes).</b> A recenter or a Mixed-Reality toggle can still
    /// leave the panel outside the new forward view — the carry preserves the player-relative pose,
    /// which is right, but an MR toggle changes what "in view" means. Checked once per
    /// <see cref="Rig.VRRigDriver.RigPoseVersion"/>, never per tick, and idempotent inside the cone,
    /// so a deliberately placed visible panel is never disturbed.</item>
    /// </list>
    ///
    /// <para>THERE IS NO RE-FACE ON A POSE-VERSION BUMP ANY MORE, and nothing lost it. It existed
    /// because the old FOLLOW derived its POSITION from the cached seat yaw, so a re-derived seat had
    /// to be re-faced to match. Both modes now carry their orientation with the player — rig-parented
    /// or rig-relative-carried — so a recentred panel already faces him, and re-facing would be a
    /// write on a frozen object with nothing to correct. Orientation still derives at events only
    /// (test #20): the first seat, a grab release, and a heal that actually relocated the panel.</para>
    /// </summary>
    private void TickAnchorHousekeeping(Camera head, float worldScale)
    {
        if (_frame == null)
            return;
        bool follow = WorldUIConfig.CombatLogFollow.Value;

        FollowPinAnchor.Carry carry = _anchor.TickCarry(_frame, follow);
        if (carry.Carried)
            _pinCarryCount++;
        if (carry.Carried)
            VRLog.Info("WorldUI", "Combat log (PINNED) carried through a tracking-origin change " +
                                  $"(rig pose version {carry.FromVersion} → {carry.ToVersion}: rig " +
                                  $"rebuild or recentre): {carry.From} → {carry.To}. A world-space " +
                                  "pin would have been stranded at the old seat.");

        int poseVersion = Rig.VRRigDriver.RigPoseVersion;
        if (poseVersion == _facedPoseVersion)
            return;
        _facedPoseVersion = poseVersion;
        Vector3 pos = _frame.position;
        if (PanelPlacement.ClampIntoView(head, worldScale, ref pos, out Quaternion facing))
        {
            _frame.position = pos;
            _frame.rotation = facing;
            PersistLayout();
            if (!_healLogged)
            {
                _healLogged = true;
                VRLog.Info("WorldUI", follow
                    ? "Combat log was out of view (stale/MR-toggled pose) " +
                      "— healed back into the forward field of view."
                    : "Combat log (PINNED) was stranded out of view by a " +
                      "recenter/MR toggle — healed back into the forward view.");
            }
            // The heal MOVED a pinned panel in world space, so the pin has to be re-authored
            // against this pose or the next origin change would carry it by the offset measured
            // before the correction and quietly undo it (the board's arrival-guard lesson).
            _anchor.RecacheRigLocal(_frame);
        }
        else
        {
            _healLogged = false;
        }
    }

    /// <summary>
    /// Upright orientation: zero roll/pitch, yaw toward the head at THIS moment
    /// (uGUI fronts render along -forward, so +Z points AWAY from the viewer).
    /// Called at derive events and on grab release only — never per tick (test #20:
    /// the per-tick re-facing read as the content shifting with head motion).
    /// </summary>
    private void FaceHead(Camera head)
    {
        if (_frame == null)
            return;
        Vector3 away = _frame.position - head.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-6f)
            _frame.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    // ---- frame (grab bar + pin) ---------------------------------------------------------------

    /// <summary>
    /// Build the mod-owned frame lazily; scene loads destroy the holder (top-level,
    /// deliberately NOT DontDestroyOnLoad — the combat log only lives inside a
    /// scenario), so a Unity-dead holder rebuilds everything from scratch.
    /// </summary>
    private void EnsureFrame()
    {
        if (_frame != null)
            return;

        // A REBUILT FRAME HAS NO POSE, so the seat must run again. This matters more than it did:
        // while FOLGEN the frame hangs off the rig root, so a rig teardown takes it with it (the
        // control board loses its root to exactly the same event and re-places for the same reason).
        // Without this line the new frame would keep the old one's "already placed" verdict and sit
        // at the world origin forever.
        _placedFromConfig = false;
        _bar = null;
        _barTween?.Release();
        _barTween = null;
        _grabZone = null;
        _handle = null;
        _pin = null;
        _pinAnchor = null;
        _closePlate = null;
        _emptyNote = null;
        _emptyAnchor = null;
        _emptyNoteRenderer = null;
        ClearCapLaserHover();

        // ONE OBJECT WHERE THERE USED TO BE TWO. The old inner "Frame" hung under a private
        // identity-pose holder that carried the diorama scale; the holder's job is now the SHARED
        // anchor's (the rig root while FOLGEN, the pin holder while FIXIERT), so the frame is the
        // top mod-owned object and _anchor.Apply switches its parent. The transform CONTRACT is
        // unchanged — the frame's parent carries the diorama scale, the frame's own localScale is
        // the user's size factor — which is why nothing downstream of it had to move.
        var frameGo = new GameObject("GloomhavenVR.CombatLogPanel");
        _frame = frameGo.transform;

        // THE NORMAL WINDOW ROD, and the frame origin IS its centre (this panel grows UP from its
        // handle, which is why the rod sits at the frame's own origin rather than one gap below a
        // panel centre the way GrabbableModal's does). Style Generic is the neutral window rod —
        // dark oiled walnut with small aged-brass knobs — i.e. member 0 of the enum, so this call
        // could not accidentally have picked up a board's material. overlay:true is what makes it a
        // WINDOW rod: GrabBarVisual then builds it on the bundled GloomhavenVR/Overlay shader and
        // forces _ZWrite on there, so the handle draws SOLID and occludes the log behind it while a
        // hand held physically in front still occludes the handle.
        GrabBarVisual bar = GrabBarVisual.Build(_frame, "Bar", GrabBarStyle.Generic,
                                                GrabBarLayout.BarRadius, overlay: true);
        if (!bar.Textured)
            VRLog.Warn("WorldUI", "Combat log built its handle WITHOUT the wood strip (the embedded "
                                  + "texture did not decode — EmbeddedTexture has already named it). "
                                  + "The rod falls back to the flat brass the cube wore, so the "
                                  + "handle works and only the grain is lost.");
        _bar = bar;

        // The LASER's grab target is a capsule down the rod's own axis, which SetLength keeps in
        // step; the PALM's target is the generous box on the frame below. The split is the
        // lost-menu fix and it is why this panel can now be carried by the far ray at all — the
        // cube never had a bar collider, so PanelGrabHandle's laser-carry had nothing to test.
        Collider barCollider = bar.AttachLaserTarget();

        // Grab zone + shared grab core (collider BEFORE the handle: OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.size = new Vector3(0.35f, GrabBarLayout.ZoneDepthMeters, GrabBarLayout.ZoneDepthMeters);
        _grabZone.isTrigger = true;
        // THE TWEEN OWNS THE ROD'S POSE FROM HERE ON: SyncBar sets a TARGET on it and WorldUIModule's
        // GrabBarTween.Late step eases the drawn rod, its laser capsule and this palm zone toward it.
        _barTween = new GrabBarTween(bar, _grabZone, "Combat log", visible: true);
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        // bar.Renderer is the SHAFT and all three pieces share ONE Material, so the handle's single
        // sharedMaterial.color write in OnGrabHighlight lights the whole rod, and its Init seeds the
        // highlight fallback from that same material — GrabBarVisual.RestingTint for a textured rod
        // and the historic brass for an untextured one. Nothing here names a resting colour: it is a
        // TINT multiplied onto the strip now and must stay white or the rod is drawn through a filter.
        _handle.Init(this, bar.Renderer, "WorldUI", "Combat log");
        _handle.SetBarCollider(barCollider);

        // FOLLOW/PINNED pin, right of the bar — THE CONTROL BOARD'S OWN CAP, built by the same call
        // with the same [BoardDashboard] dials (user, 2026-09-05: "Der fixiert button sollte gleich
        // sein wie der button am controllboard"). The engraved state SYMBOL that swaps on toggle
        // comes with it; the board's engraved WORD does not, because an engraving needs a board.
        _pinAnchor = new GameObject("PinToggle").transform;
        _pinAnchor.SetParent(_frame, worldPositionStays: false);
        _pinAnchor.localPosition = new Vector3(0.22f, 0f, -0.002f);
        _pin = PlayTray.BoardButton.CreateFollowPin(_pinAnchor, WorldUIConfig.CombatLogFollow.Value,
                                                    TogglePin);
        ApplyPinVisual();

        // Live language following: the FOLLOW/PINNED pin label and the empty-state note are set at
        // events only, so re-apply them whenever the game language changes (subscribe once;
        // Shutdown detaches).
        if (!_locHooked)
        {
            _locHooked = true;
            Loc.OnChanged += ApplyLocalisedText;
        }

        // NO X IS BUILT HERE ANY MORE. It was a loud red 3D BoardButton reading the letter "X",
        // parented to this frame — the one piece of chrome on this panel that no other window has.
        // The shared one (ModalCloseButton) is a uGUI widget on the HOST canvas and is therefore
        // attached per CONVERSION rather than per frame: see AttachPanelChrome, which also carries
        // why the close ACTION is still this surface's own.

        // The empty-state note (see TickEmptyNote). Built here so it shares the frame's lifetime
        // and its scale; parked inactive — Place() decides, once the entry count is known.
        _emptyAnchor = new GameObject("EmptyNote").transform;
        _emptyAnchor.SetParent(_frame, worldPositionStays: false);
        var noteGo = new GameObject("Label");
        noteGo.transform.SetParent(_emptyAnchor, worldPositionStays: false);
        _emptyNote = noteGo.AddComponent<TMPro.TextMeshPro>();
        _emptyNoteRenderer = noteGo.GetComponent<MeshRenderer>();
        _emptyNote.alignment = TMPro.TextAlignmentOptions.Center;
        _emptyNote.color = new Color(0.86f, 0.82f, 0.70f);
        NativeButtonSkin.ApplyFont(_emptyNote);
        NativeButtonSkin.StyleWorldReadableLabel(_emptyNote);
        ApplyEmptyNoteText();
        noteGo.SetActive(false);

        // Render-only mod layer — grabs and pokes go through the registries. AFTER the rod exists,
        // because it walks the tree it is given.
        VRLayers.Apply(frameGo);
        VRLog.Info("WorldUI", "Combat log frame built (grab bar + FOLLOW/PINNED pin; " +
                              "world-static placement, orientation derived at events only). "
                              + "Since 2026-09-05 the bar is the SHARED window rod and the pin is the "
                              + "control board's own dashboard cap; the close X is not built here at "
                              + "all — it is attached per conversion (AttachPanelChrome). The "
                              + "COMBAT LOG VERDICT line's chrome= field is what says whether all "
                              + "three actually arrived.");

        // PARKED OFF UNTIL Place() HAS A POSE FOR IT, and that became load-bearing on 2026-09-05.
        // Until then this method was only ever reached from Place(), AFTER its table-anchor and head
        // guards, so a frame could not exist without a place to be. It is now also called from
        // OnConverted (the rod's draw order and the close X are per-CONVERSION, not per frame), and
        // Place() still returns early when there is no anchor or no head — which would leave a brass
        // rod hanging at the world origin with no window on it. That is the leeres_fenster.jpg shape
        // exactly, and one SetActive is the whole guard: Place activates the holder on the same tick
        // it positions it, and Tick() switches it back off whenever the panel goes down.
        frameGo.SetActive(false);
    }

    /// <summary>
    /// Size the rod, the palm zone and the pin's seat off the panel's live rect — through the same
    /// two shared rules every other window's handle goes through, so this handle cannot look or
    /// behave like a different piece of furniture.
    ///
    /// <para><b>THE WORLD SCALE TERM IS 1 HERE, AND THAT IS NOT AN OMISSION.</b>
    /// <see cref="GrabBarLayout.Solve"/>'s third argument is the scale its fixed metre constants
    /// must be expressed in to land in the FRAME's units. <c>GrabbableModal</c> and
    /// <c>SurfaceGrabBar</c> hold their holders at identity and therefore pass the live diorama
    /// scale; this frame's PARENT carries the diorama scale (the rig root while FOLGEN, the pin
    /// holder while FIXIERT — the transform-layout contract at the top of this file), so a
    /// frame-local 1 is already a scaled metre and passing the scale again would apply it twice.
    /// While FIXIERT that parent scale is the one FROZEN at pin time, which is the point: the rod
    /// keeps the physical size the player pinned it at instead of swelling with a world-grab zoom,
    /// the same way the pinned control board does.</para>
    ///
    /// <para>THE CHANGE GATE IS GONE and nothing lost it: it used to be a hand-rolled 5 mm
    /// dead-band on the bar width alone. <see cref="BarSizeSettle"/> now owns "has this window
    /// really changed size?" (a transient that reverts inside a second never reaches the rod) and
    /// <see cref="GrabBarTween"/> owns "and how does the rod get there" — both shared, both
    /// allocation-free on a tick that changes nothing.</para>
    /// </summary>
    private void SyncBar(Rect rect, float metersPerPixel)
    {
        if (_bar == null || _grabZone == null)
            return;
        // The visibility term is the same predicate GrabVisible answers with: "grabbing something
        // that is not there" and "gating a size the player cannot see" are the same question about
        // the same rod. A change made while the panel is down is adopted at once.
        bool onScreen = Panel != null && Panel.IsAlive && !Panel.RenderHidden
                        && !Panel.OwnerRenderHidden
                        && _frame != null && _frame.gameObject.activeInHierarchy;
        float panelHeight = _heightSettle.Apply(rect.height * metersPerPixel, onScreen, "Combat log");
        float sourceWidth = _widthSettle.Apply(rect.width * metersPerPixel, onScreen, "Combat log");
        GrabBarLayout.Rod rod = GrabBarLayout.Solve(sourceWidth, panelHeight, worldScale: 1f);
        _barGap = rod.Gap;

        // TARGETS, NOT WRITES. The rod sits at the frame's ORIGIN — this frame IS the bar centre —
        // so the position term is zero and only the size ever moves. barWidth is in frame-local
        // metres and SetLength wants the rod's OWN, under a root scaled by rod.Scale, so it is
        // divided back out and the drawn end-to-end length is barWidth exactly. Snapped, never
        // eased, while a hand carries the panel or while the rod is off the screen — the same two
        // exemptions the other two bar owners state, for the same reasons.
        bool carried = _handle != null && _handle.IsGrabbed;
        string? snapWhy = carried
            ? "a hand is carrying the window"
            : !onScreen ? "the rod is off the screen (the panel is down or render-hidden)" : null;
        _barTween?.SetTarget(Vector3.zero, rod.Scale, rod.BarWidth / rod.Scale,
                             new Vector3(rod.ZoneWidth, rod.ZoneDepth, rod.ZoneDepth), snapWhy,
                             "CombatLogSurface.SyncBar (the host rect)");

        // The pin sits one knob-and-a-bit clear of the rod's right end. Off the TARGET width rather
        // than the presented one: a cap that slid along with a growing rod would read as the control
        // drifting, and the rod reaches this length within one tween.
        if (_pinAnchor != null)
            _pinAnchor.localPosition = new Vector3(rod.BarWidth * 0.5f + 0.05f, 0f, -0.002f);
    }

    // ---- FOLLOW/PINNED ------------------------------------------------------------------------

    private void TogglePin()
    {
        bool follow = !WorldUIConfig.CombatLogFollow.Value;
        WorldUIConfig.CombatLogFollow.Value = follow; // BepInEx persists on set
        // THE PANEL DOES NOT MOVE. The shared re-parent preserves the world pose in both directions
        // — this is the control board's item-4 rule ("toggling INTO follow must NOT zap the tray to
        // the head-relative config pose"), which this panel used to break by re-deriving from its
        // config offsets on the very next tick. Applied here rather than left to Place() so the
        // switch lands on the press, in the same frame the cap's symbol flips.
        ApplyAnchorMode();
        if (!follow)
            PersistLayout(); // freeze: next scenario re-derives the pin from these offsets
        ApplyPinVisual();
        VRLog.Info("WorldUI", "Combat log anchor mode → " +
                              $"{(follow ? "FOLLOW (seat-anchored)" : "PINNED (world-anchored)")}.");
    }

    /// <summary>Put the pin into the state it is actually in — accent, word and engraved SYMBOL —
    /// through the same helper the control board's own toggle calls, so the two cannot drift. The
    /// symbol is the half this panel's cap simply did not have before 2026-09-05.</summary>
    private void ApplyPinVisual() =>
        PlayTray.BoardButton.ApplyFollowPinState(_pin, WorldUIConfig.CombatLogFollow.Value);

    /// <summary>
    /// THIS PANEL'S OWN LASER SCAN OVER THE ONE 3D KEYCAP IT STILL HAS: the FOLLOW/PINNED pin.
    ///
    /// <para><b>USER RULING (ModBuild 348 multiplayer hardware test), verbatim, item 2:</b> <i>"Alle
    /// buttons müssen auch mit dem Laser drückbar sein. … Prüfe, dass das bei allen Knöpfen der Fall
    /// ist."</i></para>
    ///
    /// <para><b>IT SHRANK TO ONE CAP ON 2026-09-05, AND THE OTHER HALF DID NOT LOSE THE LASER — IT
    /// STOPPED NEEDING THIS.</b> The scan used to cover the close X as well, for the reason stated
    /// below, and the X is now the shared <see cref="ModalCloseButton"/>: a uGUI <c>Button</c> on the
    /// HOST canvas, which is already registered with <c>UguiPokeSurfaces</c> and hit by the
    /// dominant-hand laser (<c>RayUguiDriver</c>), so the fingertip poke AND the beam drive its
    /// onClick through the same <c>ExecuteEvents</c> path as every other converted widget. Nothing
    /// to register and nothing to scan. The PIN is still a 3D <c>BoardButton</c> — it is the control
    /// board's own cap, deliberately, because that is what the user asked it to look like — so it is
    /// still reachable only through a geometric ray test, and this method is that test.</para>
    ///
    /// <para><b>WHY THE TEST HAS TO LIVE HERE AT ALL.</b> Up to ModBuild 350 the caps were handed to
    /// <c>PlayTray.RegisterLaserTarget</c>, i.e. their ONLY laser route was the control board's scan
    /// — and that scan's first statement is <c>if (!_tray.IsVisible …) { ClearBoardHover(); return; }</c>
    /// (Cards/Driver/CardsDriver.3.Laser.cs:1252). The combat log is a GRABBABLE, independently
    /// placed panel with its own show/hide seam: it is routinely up while the board is not. It was
    /// also one tray rebuild away from losing the route even while the board WAS up, because the
    /// registration was per tray INSTANCE and the list dies with each tray.</para>
    ///
    /// <para><b>THE SHAPE IS <c>MapButtonRail.TickLaser</c>'s, for its reason.</b> A geometric
    /// <c>Collider.Raycast</c> over this surface's own cap needs no physics layer and no mask, so
    /// it cannot disturb anybody else's pick mask, and it is owned by the object that owns the
    /// cap's lifetime — so it cannot go stale when some third party is rebuilt. The tray
    /// registration is GONE rather than kept alongside: two scans over one collider would fight for
    /// the hover (the press itself is debounced, the hover is not).</para>
    ///
    /// <para><b>PRECEDENCE.</b> A nearer uGUI hit wins — which now includes this panel's own X, one
    /// window-width away on the host plane, so a click meant for the close cross can never also
    /// press the pin behind it — and so does a nearer SOLID mod surface (the control board or a
    /// raised card fan), read off the ray's own precomputed
    /// <c>RayInteractor.SolidOccluderDistance</c>, the same term <c>RayUguiDriver</c> uses.</para>
    ///
    /// <para><b>NO COMMIT GATE, DELIBERATELY.</b> The board's own laser path suppresses presses while
    /// a blocking modal is open, because tray keycaps call game APIs directly and END TURN is not
    /// undoable. This cap touches no game state at all: it flips a BepInEx config key. Suppressing
    /// it under a modal would only mean a log panel the player cannot get out of his way while he
    /// reads a dialog.</para>
    ///
    /// <para><b>MULTIPLAYER: nothing here goes on the wire.</b> It is a local hit test over one
    /// local collider that drives one local presentation flag.</para>
    /// </summary>
    private void TickCapLaser()
    {
        PlayTray.BoardButton? cap = _pin;
        VRHand? hand = VRHands.Primary;
        if (cap == null || cap.Collider == null || !cap.Collider.enabled
            || !cap.gameObject.activeInHierarchy
            || hand == null || !hand.HasPose || !hand.Ray.Active || hand.Grabber.Held != null)
        {
            ClearCapLaserHover();
            return;
        }

        hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        float reach = MaxCapLaserMeters * hand.WorldScale;
        if (!cap.Collider.Raycast(new Ray(origin, direction), out RaycastHit rh, reach))
        {
            ClearCapLaserHover();
            return;
        }
        // A nearer game-UI hit wins (the X on this panel's own host canvas is one of them), and so
        // does a nearer solid mod surface (control board / raised fan) — the same two vetoes
        // RayUguiDriver applies to a uGUI panel standing behind them.
        if ((hand.RayUgui.HasHit && hand.RayUgui.HitDistance < rh.distance)
            || hand.Ray.SolidOccluderDistance < rh.distance - SolidOccluderEpsilonMeters * hand.WorldScale)
        {
            ClearCapLaserHover();
            return;
        }

        if (!_pinLaserHovered)
        {
            _pinLaserHovered = true;
            cap.OnPokeEnter(hand); // the cap does its own hover tint/haptic vocabulary
        }
        hand.Ray.UiHitOverride = rh.point; // beam clamps to the cap (also suppresses the far click)

        if (hand.TriggerDown)
        {
            hand.Ray.SuppressFarClick();
            cap.Press(hand, $"combat-log laser ({hand.Side})");
        }
    }

    // ---- the round's instrument ---------------------------------------------------------------

    /// <summary>Cached every second: -1 unknown / 0 closed / 1 open on the game's own UIWindow.</summary>
    private int _gameWindowOpen = -1;
    private float _nextWindowPoll;

    /// <summary>Normal lines before the cap; past it every 100th further change still prints.</summary>
    private const int VerifyLineCap = 20;

    /// <summary>
    /// ONE LINE THAT DECIDES THE NEXT ROUND, printed on every VERDICT CHANGE (a gate edge, a
    /// button, an X, the first entry landing) and never per frame.
    ///
    /// <para>THE DEFECT IT WAS WRITTEN AGAINST WAS PURE SILENCE. The ModBuild 435 log holds no
    /// combat-log line of any kind, and silence had four candidate meanings that no field
    /// separated: never built, built and never shown, shown and immediately hidden, or shown
    /// where the player could not see it. Every one of those is now a named field.</para>
    ///
    /// <para><b>ticks IS UNCONDITIONAL LIVENESS AND IS WHY THE LINE IS TRUSTWORTHY.</b> It is
    /// incremented at the top of <see cref="Tick"/> before any early return, so a printed line
    /// always says how many times this surface has run. A log with NO line at all means the
    /// surface object is not being ticked (a module-registration defect); a line with a large
    /// tick count and <c>built=no</c> means it ran and refused — the two failure modes that
    /// looked identical last round.</para>
    /// </summary>
    private void LogVerdict()
    {
        if (Time.unscaledTime >= _nextWindowPoll)
        {
            _nextWindowPoll = Time.unscaledTime + 1f;
            _gameWindowOpen = ReadGameWindowState();
        }

        bool built = _frame != null;
        bool converted = Panel != null;
        bool target = Singleton<CombatLogHandler>.IsInitialized;
        int content = _entryCount < 0 ? -1 : (_entryCount == 0 ? 0 : 1);

        // THE THREE PIECES OF CHROME, AS THREE INDEPENDENT BITS (2026-09-05). They are in the change
        // gate and not only in the printed text, because each one can go missing on its own and a
        // verdict that did not move would swallow it: the rod's strip can fail to decode, the pin
        // can fail to build, and the shared X is attached against a UIWindow this surface has to
        // find. Without these bits "the X is not there" would print only if some UNRELATED term
        // happened to move in the same session.
        bool rod = _bar != null;
        bool rodTextured = rod && _bar!.Textured;
        bool closeX = _closePlate != null;

        // THE ANCHOR FRAME, AS THREE MORE INDEPENDENT BITS (2026-09-05, the FOLGEN/FIXIERT
        // unification). The mode itself was never in this gate, so a toggle printed no line at all
        // and the FOLLOW/PINNED word in `placed=` below could only be read off a verdict some
        // UNRELATED term happened to move. It is here now together with the two facts that say
        // whether the mode was actually CARRIED OUT: is there a pin holder, and is the frame really
        // hanging off the rig anchor. Those three are the whole difference between "the config says
        // FOLGEN" and "this panel follows the way the board does".
        bool follow = WorldUIConfig.CombatLogFollow.Value;
        Transform? rigAnchor = RigAnchor;
        bool pinHolder = _anchor.Holder != null;
        bool onRig = built && rigAnchor != null && _frame!.parent == rigAnchor;

        // Allocation-free change gate: nothing is composed until a verdict actually moved.
        int verdict = (_gateWasOpen ? 1 : 0)
                      | (_sessionVisible ? 1 << 1 : 0)
                      | (_manualOverride ? 1 << 2 : 0)
                      | (built ? 1 << 3 : 0)
                      | (converted ? 1 << 4 : 0)
                      | (target ? 1 << 5 : 0)
                      | ((_gameWindowOpen + 1) << 6)
                      | ((content + 1) << 8)
                      | (rod ? 1 << 10 : 0)
                      | (rodTextured ? 1 << 11 : 0)
                      | (_pin != null ? 1 << 12 : 0)
                      | (closeX ? 1 << 13 : 0)
                      | (follow ? 1 << 14 : 0)
                      | (pinHolder ? 1 << 15 : 0)
                      | (onRig ? 1 << 16 : 0);
        if (verdict == _lastVerdict)
            return;
        _lastVerdict = verdict;

        if (_verifyLines >= VerifyLineCap)
        {
            _verifySuppressed++;
            if (_verifySuppressed % 100 != 0)
                return;                  // capped, but NEVER silent — see the suppressed field
        }
        _verifyLines++;

        Camera? head = CanvasConversion.WorldCamera;
        string where = "not placed";
        if (built && _frame != null)
        {
            Vector3 p = _frame.position;
            float dist = head != null ? Vector3.Distance(p, head.transform.position) : -1f;
            where = $"({p.x:F2},{p.y:F2},{p.z:F2}) {(dist >= 0f ? $"{dist:F2} m from the head" : "head unknown")}"
                    + $", {(WorldUIConfig.CombatLogFollow.Value ? "FOLLOW" : "PINNED")}"
                    + $", size {Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f):F2}x";
        }

        // HW-VERIFY: was the combat log surface BUILT, was it SHOWN, by WHAT, WHERE, and did it
        // have CONTENT — the four meanings last round's silence could not tell apart. 2026-09-05
        // adds a fifth question to the same line, because the round after this one is about how the
        // panel LOOKS: does it wear the three shared pieces of chrome every other window wears (the
        // rod, the control board's dashboard cap, the shared close X), or one of its own?
        VRLog.Note("WorldUI", $"COMBAT LOG VERDICT #{_verifyLines} (ticks={_ticks}, "
            + $"suppressed={_verifySuppressed}): gate={(_gateWasOpen ? "OPEN" : "shut")} "
            + $"target={(target ? "CombatLogHandler present" : "ABSENT")} "
            + $"gameWindow={(_gameWindowOpen < 0 ? "unknown" : _gameWindowOpen == 1 ? "OPEN" : "CLOSED (the game's own combat-log setting)")} "
            + $"visible={_sessionVisible} shownBy='{_shownBy}' "
            + $"playerChose={_manualOverride} startupPref=[WorldUI] CombatLog={WorldUIConfig.CombatLogAtStart.Value} "
            + $"built={(built ? "yes" : "NO")} converted={(converted ? "yes" : "NO")} "
            + $"placed={where} entries={(_entryCount < 0 ? "unknown" : _entryCount.ToString())} "
            + $"emptyNote={(content == 0 ? "SHOWN" : content < 0 ? "not decidable" : "hidden")} "
            // The chrome field: one field, three named pieces, each one the shared object every
            // other window wears — so "it still looks different" is answered by WHICH of the three
            // is missing rather than by another photograph. See the marker above the call.
            + $"chrome=[bar={(!rod ? "NONE" : rodTextured ? "shared rod (wood strip)" : "shared rod (FALLBACK BRASS — the strip did not decode)")}"
            + $", pin={(_pin != null ? "control-board dashboard cap" : "MISSING")}"
            + $", closeX={(closeX ? "shared ModalCloseButton plate on the host canvas" : "NOT FOUND on the host — no X on this panel")}]. "
            // THE FIELD THE FOLGEN/FIXIERT ROUND IS DECIDED ON. Appended, never folded into an
            // existing field: it says which FRAME the panel is actually in, not which mode the
            // config asks for, and the two disagreeing IS the defect. A healthy FOLGEN reads
            // parent='rig anchor' with holder=none; a healthy FIXIERT reads parent='pin holder'
            // with a frozen scale that does NOT track the live diorama scale beside it.
            + $"anchor=[mode={(follow ? "FOLGEN (rig-anchored)" : "FIXIERT (world-pinned)")}"
            + $", parent={(!built ? "no frame" : onRig ? "rig anchor" : pinHolder && _frame!.parent == _anchor.Holder ? "pin holder" : _frame!.parent == null ? "NONE (world root — the mode has not been applied yet)" : $"UNEXPECTED '{_frame!.parent.name}'")}"
            + $", holderScale={(pinHolder ? _anchor.Holder!.localScale.x.ToString("F4") : "n/a (following)")}"
            + $", liveDiorama={PanelLayout.WorldScale:F4}, carriedThroughRecentres={_pinCarryCount}]. "
            + "HOW TO READ IT: 'ticks' is unconditional — a log with NO line at all means this "
            + "surface is not being ticked, while a large tick count with built=NO means it ran "
            + "and refused, and the refusing term is whichever of gate/visible reads shut/False. "
            + "built=yes converted=NO with target present means the conversion itself failed "
            + "(look for the 'Converted' line's absence). converted=yes with a placed distance "
            + "far outside arm's reach is the 'shown where he cannot see it' case. entries=0 with "
            + "emptyNote=SHOWN is the correct picture for a log summoned before anything happened "
            + "— and an entries count that is NEVER 0 on a visibly empty panel means the content "
            + "root holds a permanent non-CombatLogText child, which is the one way the note can "
            + "fail to appear. chrome names the three shared pieces this panel wears; every entry "
            + "in it should read 'shared'/'control-board' on a healthy build, and a closeX of "
            + "NOT FOUND with converted=yes means ModalCloseButton.Attach did not run or threw "
            + "(its own MODAL CLOSE lines say which). anchor decides the FOLGEN/FIXIERT round: "
            + "mode=FOLGEN with parent='rig anchor' and holder=n/a is a panel that follows the way "
            + "the control board does; mode=FIXIERT with parent='pin holder' and a holderScale that "
            + "STAYS PUT while liveDiorama moves under a world-grab zoom is a panel that is pinned "
            + "the way the board is. parent=NONE or UNEXPECTED means the mode was never applied — "
            + "look for a Place() that returned before ApplyAnchorMode. A holderScale that TRACKS "
            + "liveDiorama across two lines is the pre-2026-09-05 defect back (a pinned panel "
            + "riding the zoom). carriedThroughRecentres counts the tracking-origin carries; it "
            + "must be non-zero on any FIXIERT session with a B+Y recenter in it, and a stranded "
            + "pinned panel with a count of 0 means the carry never ran.");
    }

    /// <summary>The game's own window state. Its own options hold a DisabledCombatLog switch that
    /// hides this window independently of anything the mod does — a closed window with the mod's
    /// gate open is that, and it is the one cause this surface cannot fix from here.</summary>
    private static int ReadGameWindowState()
    {
        if (!Singleton<CombatLogHandler>.IsInitialized)
            return -1;
        CombatLogHandler handler = Singleton<CombatLogHandler>.Instance;
        if (handler == null)
            return -1;
        var window = handler.GetComponent<UnityEngine.UI.UIWindow>();
        return window == null ? -1 : (window.IsOpen ? 1 : 0);
    }

    /// <summary>How far the beam may reach a combat-log cap, in real meters before diorama scale —
    /// the same budget <c>MapButtonRail.TickLaser</c> gives its table caps.</summary>
    private const float MaxCapLaserMeters = 20f;

    /// <summary>Shared with <c>RayUguiDriver</c>'s occlusion epsilon: a surface coplanar with (or
    /// proud of) its own occluder must not sit in that occluder's shadow.</summary>
    private const float SolidOccluderEpsilonMeters = 0.005f;

    /// <summary>Drop the beam hover. The flag is cleared FIRST so a re-entrant <c>OnPokeExit</c>
    /// cannot loop, and the cap is Unity-null-checked because the two teardown paths call this
    /// after it is gone.</summary>
    private void ClearCapLaserHover()
    {
        if (!_pinLaserHovered)
            return;
        _pinLaserHovered = false;
        PlayTray.BoardButton? cap = _pin;
        VRHand? hand = VRHands.Primary;
        if (cap != null && hand != null)
            cap.OnPokeExit(hand);
    }

    // ---- persistence --------------------------------------------------------------------------

    /// <summary>
    /// Inverse of the FOLLOW placement: the frame's pose as table-anchor offsets in
    /// real meters (seat-yaw space, divided by the diorama scale) + the size factor.
    /// BepInEx writes the ConfigFile on set, so the layout survives sessions.
    /// </summary>
    private void PersistLayout()
    {
        if (_frame == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return;
        float worldScale = PanelLayout.WorldScale;
        if (worldScale < 1e-5f)
            return;
        Vector3 local = Quaternion.Inverse(yaw) * (_frame.position - anchor) / worldScale;
        WorldUIConfig.CombatLogRight.Value = local.x;
        WorldUIConfig.CombatLogUp.Value = local.y;
        WorldUIConfig.CombatLogForward.Value = local.z;
        WorldUIConfig.CombatLogScale.Value = Mathf.Clamp(_frame.localScale.x, 0.5f, 2f);
        VRLog.Info("WorldUI", $"Combat log layout persisted: right {local.x:F2} m, " +
                              $"up {local.y:F2} m, fwd {local.z:F2} m, " +
                              $"scale {WorldUIConfig.CombatLogScale.Value:F2}x.");
    }
}
