using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI; // the game's UIWindow lives here (decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs)

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Makes a floated modal window (<see cref="ModalFallback"/>) a GRABBABLE + SCALABLE
/// world element — exactly like the control board / combat log — by reusing the SHARED
/// grab core (<see cref="PanelGrabHandle"/> + <see cref="IPanelGrabOwner"/>): one hand
/// grips the brass bar under the panel to MOVE it, two hands RESIZE it (the SHARED range
/// <see cref="PanelGrabHandle.MinScale"/>–<see cref="PanelGrabHandle.MaxScale"/> = 0.15×–2×;
/// the floor was lowered from 0.5 and a local re-clamp at 0.5 would re-cap the pinch). No new
/// grab mechanism is invented; this only owns a small mod-owned holder/frame the same way
/// <see cref="Surfaces.CombatLogSurface"/> does, and lets the game-owned world-space host
/// FOLLOW that frame each tick.
///
/// TRANSFORM LAYOUT (mirrors CombatLogSurface): holder (identity pose, localScale =
/// diorama WorldScale) → frame (grab ROOT at the PANEL CENTER; localScale = user size
/// factor, clamped to PanelGrabHandle's [MinScale, MaxScale] = 0.15–2) → bar visual (a
/// child just under the panel's bottom edge). The
/// grab-zone collider lives on the frame with its centre offset down to the bar, so the
/// grip lands on the visible handle while the frame origin stays at the panel centre.
///
/// HOST FOLLOW: the game-owned host is never re-parented (mount-seam reversibility rule);
/// each <see cref="Tick"/> its world pose is copied from the frame and its scale is
/// metersPerPixel × WorldScale × <c>extraScale</c> × factor — the SAME convention
/// <see cref="ModalFallback"/> places it with, plus the live user grab factor. When the
/// user is not gripping, the frame is static, so the host is static too (no drift).
///
/// INPUT: poke/laser clicks on the menu widgets are unaffected — they drive the real
/// uGUI through the host's raycaster (UguiPokeSurfaces / RayUguiDriver), a different path
/// than the grip-grab, and the bar sits BELOW the content so it never overlaps a widget.
/// The world-grab yields any grip that starts on a highlighted/held grabbable, so gripping
/// the bar moves the menu instead of the diorama (PanelGrabHandle's documented arbitration).
/// </summary>
internal sealed class GrabbableModal : IPanelGrabOwner
{
    /// <summary>Gap below the panel's bottom edge to the bar centre (frame-local, scale-1 metres).</summary>
    private const float BarGapMeters = 0.03f;
    private const float BarThickness = 0.024f;
    private const float BarWidthFraction = 0.55f;
    private const float ZoneWidthFraction = 0.62f;
    private const float MinBarWidth = 0.04f;

    /// <summary>
    /// EMPTY-GOLD-PLATE FIX (torbogen screenshot 2026-08-02): panel height (real metres) at or
    /// above which the bar keeps its full thickness/gap. The fixed 2.4 cm bar + 3 cm gap were
    /// sized for board-scale menus; under the ~6 cm level-message ACTION STRIP the same bar
    /// rendered nearly as tall as the strip itself and a full strip-height away from it — on
    /// the flat mirror it read as a detached EMPTY GOLD RECTANGLE floating below the hint
    /// (identified in the screenshot by its brass colour, 55 % width and centred position one
    /// gap below the strip). Panels shorter than this reference get a proportionally slimmer,
    /// closer bar so the handle visually attaches to its window; taller panels (ESC/Options,
    /// results, tutorial boxes) are numerically unchanged.
    /// </summary>
    private const float BarFullSizePanelHeightMeters = 0.30f;

    /// <summary>Floor of the short-panel bar proportion — the visible strip (and its padded
    /// laser collider, which scales with it) must stay a comfortable target.</summary>
    private const float MinBarProportion = 0.5f;

    /// <summary>
    /// LOST-MENU FIX: cross-section pad of the LASER-only bar collider, in bar-local units
    /// (the bar cube is unit-sized, scaled to barWidth × BarThickness × BarThickness — so
    /// 1.5 ≈ a 3.6 cm strip). Just enough slack to point at the 2.4 cm visible bar
    /// comfortably, WITHOUT re-growing the swallow-everything zone the incident showed:
    /// the palm ZONE collider (5 cm, 62% width) had been the laser target too, and since
    /// the floated menu sits between the user and the board, every trigger aimed at the
    /// cards hit it and dragged the (possibly off-view) menu instead.
    /// </summary>
    private const float BarColliderPad = 1.5f;

    /// <summary>
    /// Item 3: the brass grab bar must OCCLUDE the menu content behind it (foreground is
    /// foreground). Unity sorts EVERY renderer by sortingLayer -> SORTINGORDER first (only then by
    /// renderQueue / distance - see <c>RayInteractor.RayVisualSortingOrder = 5000</c>, which is
    /// exactly how the laser dot draws over a floated modal). An opaque (ZWrite-on) brass material
    /// makes the bar read solid rather than semi-transparent; the order below is what lifts it over
    /// the depthless menu canvas.
    ///
    /// <para>TRANSPARENCY ROUND: this is no longer an absolute value (it was 1100, one hundred over
    /// the old fixed modal tier). The menu's own order now moves every frame with its eye distance
    /// (CanvasConversion.8.Order.cs), so the bar rides it as an ORDER FOLLOWER at this OFFSET -
    /// always over its own window, always under a panel that is genuinely nearer than the window.
    /// A fixed 1100 would have made the bar pierce every nearer panel, which is precisely the
    /// "order beats distance" defect the whole round is about. The offset stays below
    /// <c>CanvasConversion.PanelOrderStep</c>, which is what guarantees the second half.</para>
    /// </summary>
    private const int BarOrderOffset = 4;

    private ConvertedPanel _panel = null!;
    private float _extraScale = 1f;             // ModalFallback.WindowScaleFactor (host shrink)

    /// <summary>
    /// Item 2: the diorama scale CAPTURED ONCE at spawn. The menu SIZE is derived from this
    /// fixed reference instead of the live <see cref="PanelLayout.WorldScale"/>, so zooming the
    /// diorama after the menu opens no longer grows/shrinks it (position stays a fixed world
    /// point regardless). Only the user's two-hand grab factor still resizes it on top.
    /// </summary>
    private float _spawnWorldScale = 1f;
    private string _logName = "Menu";

    /// <summary>DIAG-throttle (spam fix): seconds between MODAL DIAG snapshot lines while the host moves.</summary>
    private const float DiagThrottleSeconds = 1f;

    // DIAG-throttle state: last host pose (movement detection) + per-panel next-allowed stamp.
    private Vector3 _diagLastPos;
    private Quaternion _diagLastRot = Quaternion.identity;
    private float _diagNextAllowed;

    /// <summary>
    /// EVERY GrabbableModal THAT HAS ACTUALLY BUILT ITS HOLDER (ModBuild 230).
    ///
    /// <para>The holder is a SCENE-ROOT tree — <c>EnsureFrame</c> creates
    /// <c>GloomhavenVR.ModalGrab_*</c> with no parent, because the game-owned host FOLLOWS the frame
    /// rather than hanging off it. That inversion is what makes this the one piece of window chrome
    /// that does not die when its panel is released: destroying the host cannot reach it. Every
    /// release path in <see cref="ModalFallback"/> calls <see cref="Destroy"/> first and is correct
    /// today, so this list is not a fix — it is the only way anything could NOTICE a holder that
    /// outlived its window, which is the artefact the user photographed three of in one frame
    /// (.planning/debug/leeres_fenster2.jpg). <c>ModalFallback.SweepOrphanChrome</c> reads it.</para>
    ///
    /// <para>Registration is in <see cref="EnsureFrame"/>, NOT in the constructor, on purpose: an
    /// un-built GrabbableModal owns nothing visible, and listing one would make the sweep's
    /// "holders against windows" comparison mean something else than it says.</para>
    /// </summary>
    internal static readonly List<GrabbableModal> LiveHolders = new(8);

    /// <summary>The name this modal reports itself under in the log — the window name
    /// <see cref="Build"/> was given. Exposed so the orphan sweep can NAME what it destroyed; the
    /// ModBuild 225 round cost a build precisely because the stray bars carried no identity.</summary>
    internal string LogName => _logName;

    private Transform? _holder;                 // identity pose, localScale = diorama WorldScale
    private Transform? _frame;                  // grab root at the panel centre; localScale = user factor
    private Transform? _visual;                 // THE DRAWN pose — see the REMOTE POSE EASING block
    private Transform? _bar;

    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;

    /// <summary>True while a hand grips the bar (owner skips no writes — the host just follows).</summary>
    internal bool IsGrabbed => _handle != null && _handle.IsGrabbed;

    /// <summary>
    /// USER-OWNED POSE (user report 2026-08-04: "Sie sollen dort fix bleiben, wo sie stehen,
    /// nicht springen"): latched TRUE the first time the player grips this window (and never
    /// cleared for the lifetime of the float). From that moment its pose belongs to the
    /// player - the presence-regain refloat (ModalFallback.RefloatOpenWindows) skips a
    /// user-moved window, because the user deliberately parks windows OUT of the view
    /// ("manchmal schiebe ich sie absichtlich zur Seite"). The lost-menu recall that used to
    /// share this exemption is GONE as of ModBuild 149: its 6 s out-of-view timer kept yanking
    /// windows back to the gaze (hardware log: repeated "MODAL RECALL: 'UI Options
    /// Window_unified' ... out of view for 6s" lines) and the user ruled that a window stays
    /// where it spawned unless it is actively moved, grabbed or not. So this flag now protects
    /// against ONE mover rather than two. ANY grip counts as the claim - even a grab released
    /// in place: the player
    /// touched it, so the mod stops second-guessing where it belongs. The X close button and
    /// the modal escape chord remain the rescue for a window the player genuinely loses.
    /// </summary>
    internal bool UserMoved { get; private set; }

    /// <summary>
    /// Item 1 (pause-menu size): re-seat the board-relative host shrink AFTER a full-screen menu's
    /// one-shot content fit shrank the host rect. The fit runs a few frames after Build, so the
    /// extraScale first derived from the pre-fit (full 1920) rect would leave the fitted panel
    /// mis-sized; the owner recomputes it from the fitted width and pushes it here. The next
    /// <see cref="Tick"/> applies it (host localScale = mpp × worldScale × extraScale × factor).
    /// </summary>
    /// <para>IT ALSO ENDS ANY IN-FLIGHT REMOTE GLIDE. A content re-fit is a SIZE change, and the
    /// user requirement is that a resize snaps rather than crawls (a glide would additionally be
    /// measured by <c>PanelPoseWatch</c>'s size-change assertion as the resize DISPLACING the
    /// window, which it did not).</para>
    internal void SetExtraScale(float extraScale)
    {
        if (!Mathf.Approximately(extraScale, _extraScale))
        {
            _easing = false;
            _visualValid = false;
        }
        _extraScale = extraScale;
    }

    /// <summary>
    /// Build the grab affordance for a freshly floated, freshly placed modal host. The
    /// frame is seeded at the host's CURRENT world pose (the host was just placed at the
    /// HMD), so the first follow tick keeps the panel exactly where it spawned — no jump.
    /// </summary>
    /// <para>TRANSPARENCY ROUND: the <c>depthMask</c> parameter is gone with the mask itself. Its
    /// job — "transparent HUD behind the floated menu must be occluded by it" — is now done by the
    /// draw ladder (CanvasConversion.8.Order.cs): the menu is simply painted after everything it is
    /// in front of, and before everything it is behind. The mask could only ever express that as a
    /// per-QUAD depth stamp, which is what cut the reported hard-edged holes into the panels behind
    /// a menu's transparent regions.</para>
    internal void Build(ConvertedPanel panel, float extraScale, string logName)
    {
        _panel = panel;
        _extraScale = extraScale;
        _logName = logName;
        // Item 2: snapshot the diorama scale now — the menu keeps THIS size regardless of later zoom.
        _spawnWorldScale = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        EnsureFrame();
        if (_frame != null && panel.HostGo != null)
        {
            Transform h = panel.HostGo.transform;
            _frame.SetPositionAndRotation(h.position, h.rotation);
            _frame.localScale = Vector3.one; // user factor 1x
        }
        Tick(); // place host from the frame + size the bar immediately
    }

    /// <summary>
    /// Re-seat the frame (and thus the whole panel) at a fresh pose — the ONE entry point every
    /// external pose writer uses: the spawn / presence-regain refloat / pre-reveal re-place
    /// (<c>ModalFallback.ComputeHmdPose</c>'s three callers) and the two multiplayer pose appliers
    /// (<c>Net.RemoteStorySync</c> record 19, <c>Net.RemoteMapStory</c> record 21).
    ///
    /// <para><b>THE FRAME IS ALWAYS WRITTEN IMMEDIATELY AND EXACTLY.</b> What may be eased is the
    /// DRAWN pose, and only for a remote-driven shared window — see the REMOTE POSE EASING block for
    /// the whole design, and in particular for why easing the frame itself would have been the
    /// wrong lever.</para>
    /// </summary>
    internal void PlaceFrameAt(Vector3 position, Quaternion rotation)
    {
        EnsureFrame();
        if (_frame == null)
            return;
        // THE EXACT DISCRIMINATOR, not a distance heuristic. Every LOCAL placement path funnels
        // through ModalFallback.ComputeHmdPose, which announces itself to the pose lock
        // (PanelPoseWatch.Announce(Writer.Placement)) in the same frame it hands the pose to the
        // caller that writes it. The two NET appliers announce nothing. So "a mod placement is
        // happening this frame" is a fact already recorded next door, and asking it is what makes
        // "opened / re-seated ⇒ snap" and "a peer dragged it ⇒ glide" separable WITHOUT a magnitude
        // threshold — which would have been exactly wrong here, since today a whole drag arrives as
        // ONE large jump (see the block below) and a threshold would snap the one case that must
        // glide.
        bool ease = _shared
                    && !IsGrabbed
                    && _visualValid
                    && _panel != null && _panel.IsAlive && !_panel.RenderHidden && !_panel.RevealPending
                    && !PanelPoseWatch.PlacementAnnounced(_panel);
        _frame.SetPositionAndRotation(position, rotation);
        _easing = ease;
        if (ease)
            PeerPlaced = true;
        Tick();
    }

    /// <summary>
    /// Re-seat the frame AND the drawn pose in one step, with no easing under any circumstances —
    /// the pose lock's restore path (<see cref="PanelPoseWatch"/>'s <c>Refuse</c>).
    ///
    /// <para>A restore is a CORRECTION of a write the ruling refuses, so it has to be instant: a
    /// glide would put the window visibly somewhere the lock has already decided it may not be, and
    /// the lock re-measures against its own locked pose on the very next frame, which an in-flight
    /// glide would read as a further unattributed write.</para>
    /// </summary>
    internal void SnapFrameTo(Vector3 position, Quaternion rotation)
    {
        EnsureFrame();
        if (_frame == null)
            return;
        _frame.SetPositionAndRotation(position, rotation);
        _easing = false;
        _visualValid = false;   // the next Tick pins the drawn pose to the frame outright
        Tick();
    }

    // ---- IPanelGrabOwner --------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;

    // User ruling 2026-08-02 round 2: a window that is still render-hidden behind the reveal gate
    // must not be grabbable either — "grabbing/poking an invisible panel" is impossible by
    // construction here, because PanelGrabHandle.CanGrab reads GrabVisible and BOTH grab paths
    // consult it (RayGrabDriver's bar-collider ray test and ProximityGrabber's palm candidate
    // scan). The uGUI side needs nothing extra: the poke and the laser skip surfaces whose Canvas
    // is not isActiveAndEnabled (PokeInteractor.TickCanvases / RayUguiDriver), and the hide
    // disables the host canvas — nested X hit-canvases are only ever consulted through a winning
    // host, so they cannot be reached either.
    // OwnerRenderHidden gets the SAME treatment, by the same argument: a panel its own surface
    // render-hid (the character focus — a decision row / use bar that belongs to somebody the
    // player is not looking at) is just as invisible as one behind the reveal gate, and grabbing or
    // resizing an invisible window is exactly the "grabbing something that is not there" the ruling
    // above forbids. It is also the safe direction for the focus feature itself: the row must come
    // back at the geometry it left with, and a grab is the one thing that would move it meanwhile.
    bool IPanelGrabOwner.GrabVisible =>
        _panel != null && _panel.IsAlive && !_panel.RenderHidden && !_panel.OwnerRenderHidden
        && _holder != null && _holder.gameObject.activeInHierarchy;

    // Carry the yaw with the hand like the combat log — nothing else authors the
    // modal's rotation, so there is no two-writer jitter. Level in the plain WORLD
    // frame: floated windows are not part of the item-11 "board stays level for its
    // owner" contract, so they keep their historic behavior under a world tilt.
    PanelCarryMode IPanelGrabOwner.CarryMode => PanelCarryMode.Level;
    Quaternion IPanelGrabOwner.GrabLevelFrame => Quaternion.identity;
    Vector2 IPanelGrabOwner.GrabPitchLimits => new(-180f, 180f);

    /// <summary>Modal windows have no apparent-size ruling — the handle's generic factor range
    /// IS their resize window (see <see cref="IPanelGrabOwner.GrabScaleLimits"/>).</summary>
    Vector2 IPanelGrabOwner.GrabScaleLimits => new(PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);

    // Free placement: the menu stays wherever the user left it while open; a re-open
    // re-floats it at the HMD (ModalFallback), so there is nothing to persist here.
    // (Level-message chains are the one family whose pose DOES persist across the game's
    // brief close/reopen gaps — but that lives in ModalFallback's shared chain store,
    // which simply reads the live host pose at its update edges; still nothing to do here.)
    //
    // RE-FACE ON RELEASE (user request): the one-hand carry yaws the panel with the HAND
    // (CarryMode Level), so dragging a window to the side leaves it turned to wherever the wrist
    // happened to point — readable only edge-on. The moment the LAST hand lets go (this is the
    // release edge: PanelGrabHandle.OnRelease calls it once _handA and _handB are both gone),
    // snap the rotation back to facing the player. POSITION IS UNTOUCHED — the window stays
    // exactly where it was put; only the orientation is re-derived, through the same
    // PanelPlacement.Facing the spawn placement uses, so a moved window reads identically to a
    // freshly floated one.
    //
    // ── 2026-08-22: THAT PARAGRAPH IS NOW THE "Always" MODE, NOT THE RULE ────────────────────────
    //
    // TWO SEPARATE USER STATEMENTS BOUND IT, and they are gated in that order below.
    //
    // (7b) A SHARED WINDOW NEVER RE-FACES, ON ANY CLIENT, AND THIS IS NOT CONFIGURABLE. Verbatim:
    //      "Da es ein Fenster für alle ist, sollen diese Fenster nach dem Greifen auch nicht die
    //      Orientierung nach dem Spieler ändern, wie es die anderen Fenster tun." … "Das gilt wie
    //      gesagt nur für die lokalen Fenster, Remote-Fenster (blau) sollen das gar nicht haben."
    //      The rotation on a window that belongs to everybody is a shared fact: correcting it toward
    //      the person who last moved it turns it AWAY from everyone else. And it is worse than
    //      cosmetic on the SENDER — the pose that record 19/21 published is corrected locally one
    //      frame later, so the two clients no longer agree about a pose that is supposed to be 1:1.
    //      This gate therefore sits FIRST and outranks the dial.
    //
    // (8) FOR LOCAL WINDOWS IT IS A THREE-WAY SETTING, [WorldUI] WindowFacing (see WindowFaceMode
    //     for the request verbatim and why the axis is the grab MODALITY): LaserOnly (the user's own
    //     default), Always, Never. The modality comes from PanelGrabHandle.LastGrabWasLaser, which
    //     is latched from the grabber's own identity at the gesture start — never guessed from how
    //     far away the hand was.
    //
    // WHAT IS NOT TOUCHED: this is a ONE-SHOT ON RELEASE in every mode. Nothing here makes a window
    // follow the head, and the standing project rule that nothing re-orients with head movement is
    // unaffected.
    void IPanelGrabOwner.OnGrabFinished()
    {
        if (_frame == null)
            return;
        if (!WantsReFaceOnRelease())
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        Quaternion facing = PanelPlacement.Facing(_frame.position, head.transform.position);
        // Nothing to say (and nothing to write) when the drag already left it facing the player.
        if (Quaternion.Angle(_frame.rotation, facing) < ReFaceEpsilonDeg)
            return;

        float turned = Quaternion.Angle(_frame.rotation, facing);
        _frame.rotation = facing;
        // Push the new frame rotation onto the game-owned host in the same frame, so the panel
        // does not visibly hang at the drag rotation until the next Tick.
        Tick();
        VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a move — re-faced the player " +
                              $"(turned {turned:F1}°, yaw now {_frame.eulerAngles.y:F1}°; position kept).");
    }

    /// <summary>Below this the released panel already faces the player — no snap, no log.</summary>
    private const float ReFaceEpsilonDeg = 0.5f;

    /// <summary>
    /// Does THIS release re-derive the facing? The two gates of the block above, in order:
    /// the non-negotiable shared-window rule first, the player's dial second.
    ///
    /// <para>Logged once per refused release rather than silently, because "my window did not turn"
    /// and "my window turned" are the same complaint from opposite directions and the log has to say
    /// which rule decided it — the shared-window rule reads identically to the Never mode from the
    /// outside, and confusing the two would send the next round looking at the wrong file.</para>
    /// </summary>
    private bool WantsReFaceOnRelease()
    {
        if (_shared)
        {
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a move and NOT re-faced " +
                                  "— it is a SHARED (blue-bar) window, whose orientation belongs to " +
                                  "the whole room. Turning it toward the player who moved it would " +
                                  "turn it away from everyone else, and it would silently disagree " +
                                  "with the pose this client just published on the wire. This is the " +
                                  "user's own rule ('Remote-Fenster (blau) sollen das gar nicht " +
                                  "haben') and it is NOT what [WorldUI] WindowFacing configures.");
            return false;
        }

        WindowFaceMode mode = WorldUIConfig.WindowFacing != null
            ? WorldUIConfig.WindowFacing.Value
            : Defaults.WindowFacing;   // a release before Bind completed (scene load): ship the default
        if (mode == WindowFaceMode.Always)
            return true;
        if (mode == WindowFaceMode.Never)
        {
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a move and NOT re-faced " +
                                  "— [WorldUI] WindowFacing is Never, so a released window keeps " +
                                  "exactly the orientation it was let go at.");
            return false;
        }

        // LaserOnly (default). A laser carry translates only — PanelGrabHandle's laser branch writes
        // position and returns — so the window arrives still facing the way it used to and would be
        // read edge-on; that is the case the snap exists for. A HAND carry has already yawed the
        // window with the wrist for the whole drag, so the player aimed it themselves.
        bool laser = _handle != null && _handle.LastGrabWasLaser;
        if (!laser)
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' released after a HAND move and NOT " +
                                  "re-faced — [WorldUI] WindowFacing is LaserOnly (the default): a " +
                                  "hand carry yaws the window with your wrist for the whole drag, so " +
                                  "the orientation you let go at is the one you aimed. A LASER drag " +
                                  "on the same window still snaps round, because that carry never " +
                                  "rotates it at all.");
        return laser;
    }

    // ---- per-frame follow -------------------------------------------------------------------

    /// <summary>The game-owned host follows the mod-owned grab frame (position, rotation, scale).</summary>
    internal void Tick()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _panel.HostRect == null)
            return;
        EnsureFrame();
        if (_holder == null || _frame == null)
            return;

        // Item 2 (no auto-scale with world zoom): the menu SIZE uses the diorama scale CAPTURED
        // ONCE at spawn (_spawnWorldScale), NOT the live PanelLayout.WorldScale — so zooming the
        // diorama after the menu opens no longer grows/shrinks it. POSITION is the frame's world
        // point (copied below), independent of worldScale.
        //
        // CRITICAL (deadlock fix): the holder MUST stay at identity scale. It used to be scaled by
        // worldScale, which tied the frame's WORLD position to worldScale — frame.position =
        // holder.scale(worldScale) × frame.localPosition. worldScale settles/animates at scenario
        // start, so the modal's position (and size) collapsed toward the origin in lock-step and the
        // start dialog flew away, undismissable. With the holder at identity, frame.position is a
        // true world point — grab-stable and immune to worldScale drift.
        float worldScale = _spawnWorldScale;
        _holder.localScale = Vector3.one;
        if (!_holder.gameObject.activeSelf)
            _holder.gameObject.SetActive(true);

        // USER-OWNED POSE: the first grip claims the window for the player (see UserMoved).
        // Latched here, on the grip's first follow tick, so both grab paths (palm zone and
        // laser bar) and the two-hand resize all count - they run through this same follow.
        if (!UserMoved && _handle != null && _handle.IsGrabbed)
        {
            UserMoved = true;
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{_logName}' grabbed - its pose is now PLAYER-OWNED: " +
                                  "no out-of-view recall and no presence-regain refloat will move it " +
                                  "while it stays open, and a re-open this scenario reuses the " +
                                  "player's last pose/size (X + escape chord remain the rescue).");
        }

        // Item 4: the user grab factor rides the SAME [MinScale, MaxScale] range the shared handle
        // clamps to — a higher local floor here would silently re-cap what the two-hand pinch shrank.
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;

        // REMOTE POSE EASING: advance (or pin) the DRAWN pose before anything reads it this frame.
        // Exactly ONE advance per frame, here, so the LateUpdate re-sync below cannot double the
        // rate. See the REMOTE POSE EASING block.
        AdvanceVisual(factor);

        Transform host = _panel.HostGo.transform;
        SyncHostToFrame(host, metersPerPixel, worldScale);
        // POSE-GAP INSTRUMENT (see LateSyncHost): remember what UPDATE published, so the LateUpdate
        // re-sync can say how far the window moved in between — i.e. how stale the pose is for every
        // consumer that samples it during Update.
        _updatePos = _visualPos;
        _updatePosValid = true;

        // DIAG SPAM FIX: while the host is being carried/moved, its position changes every
        // frame, so CanvasConversion's change-gated MODAL DIAG snapshot (host pos rounded to
        // cm) emitted one line PER FRAME for the whole drag (hundreds of lines in the incident
        // log). Throttle it to ~1 line/s per panel by gating the panel's Diagnostic opt-in.
        ThrottleDiagWhileMoving(host);

        // Bar/zone track the live host rect. The holder is now identity, so these frame-local
        // metres must carry worldScale themselves to reach the host's world size (the frame's
        // own localScale contributes the user grab factor). unit = world metres per host pixel.
        Rect rect = _panel.HostRect.rect;
        float unit = metersPerPixel * _extraScale * worldScale;
        float halfHeight = rect.height * unit * 0.5f;
        float width = rect.width * unit;
        SyncBar(halfHeight, width, worldScale);
    }

    /// <summary>
    /// Copy the DRAWN pose/scale onto the game-owned host (shared by the Update-time
    /// <see cref="Tick"/> and the LateUpdate re-sync in <see cref="HostLateSync"/>).
    ///
    /// <para>The drawn pose is the grab frame's for every window that is not gliding — see
    /// <see cref="AdvanceVisual"/>, which is what makes <c>_visual*</c> and the frame identical in
    /// every other case, so this method's output is unchanged for every private window.</para>
    /// </summary>
    private void SyncHostToFrame(Transform host, float metersPerPixel, float worldScale)
    {
        host.SetPositionAndRotation(_visualPos, _visualRot);
        host.localScale = Vector3.one * (metersPerPixel * worldScale * _extraScale * _visualScale);
    }

    /// <summary>
    /// DRAG-FLICKER FIX (tabs blink / submenu pane vanishes ONLY while moving the window):
    /// re-sync the host from the frame in LateUpdate, AFTER every Update-time frame writer ran.
    ///
    /// Root cause: <see cref="Tick"/> (the frame→host copy) runs from
    /// <c>ModalFallback.Tick</c> inside the WorldUI module's <c>Update</c>, while
    /// <see cref="PanelGrabHandle"/> moves the FRAME from its OWN MonoBehaviour
    /// <c>Update</c> — the relative script order is undefined. Whenever the handle's Update
    /// runs after the module's, the frame (and every rigid CHILD of it: the DEPTH MASK, the
    /// bar) renders at the NEW pose while the host — synced earlier from the STALE pose —
    /// renders one frame behind. A fast drag moves the frame 5–30 cm per frame (hardware
    /// log), dwarfing the mask's 2 mm behind-plane offset: dragging toward the viewer puts
    /// the mask plane IN FRONT of the (lagging) menu content, the menu fails its own
    /// ZTest-LEqual under every mask quad, and the content blinks out — exactly the
    /// "tabs flicker / pane briefly vanishes while moving" report. Re-copying the pose here
    /// in LateUpdate (after ALL Updates, before rendering) makes host, mask and bar agree
    /// at render time every frame, regardless of script order; a static (ungrabbed) frame
    /// makes it a change-free no-op write.
    /// </summary>
    internal void LateSyncHost()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _frame == null)
            return;
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        Transform host = _panel.HostGo.transform;
        // THE RE-PIN, and NOT a second easing step. The whole reason this method exists is that
        // PanelGrabHandle moves the FRAME from its own Update in undefined order against ours, so
        // the drawn pose has to be re-derived after every Update ran. For a window that is not
        // gliding that means "copy the frame" — byte-for-byte what this method did before the
        // easing existed. For one that IS gliding the drawn pose was already advanced in Tick and
        // must be left exactly as it is: advancing it again here would run the glide at twice the
        // intended rate and make it frame-order dependent, which is the very class of bug this
        // method was written to close.
        if (!_easing || !_visualValid)
            PinVisualToFrame(factor);

        // POSE-GAP INSTRUMENT (ModBuild 199, CORRECTED in ModBuild 200). The re-sync below exists
        // because the frame can move AFTER the module's Update. This measures how much it actually
        // does, in the window's OWN AUTHORED PIXELS — which is the unit the complaint is in: a gap of
        // N px means every consumer that sampled the host pose during Update (PanelSupersample.Tick
        // runs at the end of CanvasConversion.Tick, in Update) is working from a pose N authored
        // pixels behind what the eye will be shown this frame.
        //
        // WHAT ModBuild 199 SHIPPED WAS ZERO BY CONSTRUCTION, AND THE LOG PROVES IT: 95 report lines,
        // 84,647 samples, mean 0.00 px, WORST 0.00 px, 0 over threshold — across a session containing
        // 225 frames the guard budget independently counted as MOVING. It compared
        // host.position against _updatePos, and _updatePos IS host.position as Tick left it: nothing
        // between Update and here writes the host, so the instrument was subtracting a value from
        // itself. A flawless measurement of the wrong stage looks exactly like proof, and this one
        // "proved" the pose gap does not exist.
        //
        // THE STALE QUANTITY IS THE FRAME, NOT THE HOST. PanelGrabHandle moves _frame from its OWN
        // MonoBehaviour Update, in undefined order against the module's Update. So the real staleness
        // is how far the FRAME has travelled since Update published the host from it — i.e. the pose
        // the host is about to be given on the next line, minus the pose it has been carrying all
        // frame. Still windows still read 0 (the frame did not move); only a drag can move the
        // needle, which is exactly the interval under suspicion. Two floats and a subtract.
        //
        // ModBuild 226: the subject is the DRAWN pose on both ends of the subtraction, not the frame.
        // The quantity the instrument names is "how far the window moved between Update publishing
        // it and the eye being shown it", and since the easing landed that is the drawn pose by
        // definition — the frame can now be ahead of the picture on purpose (a remote glide), which
        // is not staleness and must not be reported as it. For every window that is not gliding the
        // drawn pose IS the frame (PinVisualToFrame ran two lines up), so the number is unchanged.
        float unit = metersPerPixel * _spawnWorldScale * _extraScale * _visualScale;
        if (_updatePosValid && unit > 1e-9f)
        {
            float gapPx = Vector3.Distance(_visualPos, _updatePos) / unit;
            _panel.PoseGapSamples++;
            _panel.PoseGapSumPx += gapPx;
            if (gapPx > _panel.PoseGapWorstPx)
                _panel.PoseGapWorstPx = gapPx;
            if (gapPx > PoseGapThresholdPx)
                _panel.PoseGapOverOnePx++;
        }
        _updatePosValid = false;

        SyncHostToFrame(host, metersPerPixel, _spawnWorldScale);
    }

    // ---- REMOTE POSE EASING -----------------------------------------------------------------
    //
    // USER REQUEST 7a (2026-08-22, verbatim):
    //
    //   "Die Bewegungen der 'blauen' MP-Fenster, die 1:1 synchronisiert werden sollen, sollen auch
    //    die Bewegung und die Position voll übertragen (flüssig, wie bei der Position des Boards
    //    auch)!"
    //
    // WHAT THE BOARD DOES THAT THIS WINDOW DID NOT. The remote control board is smooth because of
    // TWO mechanisms, and the window had NEITHER:
    //   1. THE RECEIVER EASES. Net.RemoteControlBoard.Tick does not assign the synced pose; it
    //      Lerp/Slerps toward it every frame at k = 1 − exp(−NetProtocol.InterpolationSharpness·dt)
    //      and snaps only on the first apply (_poseInit), so a fresh board never flies in from the
    //      origin. Its scale rides the SAME k — defect (e) of that round was exactly a board that
    //      "glides while it moves and stutters while it zooms", because the scale was assigned while
    //      the pose was eased.
    //   2. THE SENDER RAISES ITS CADENCE WHILE THE THING MOVES. NetAvatarDriver.TickExtrasSend's
    //      boardMoving/poseDue pair puts the whole extras packet on the rig rate (15 Hz) for as long
    //      as the board's pose keeps changing, and back on 5 Hz the moment it settles.
    // The shared window ASSIGNED its pose (this class's PlaceFrameAt, called straight from
    // Net.RemoteMapStory / Net.RemoteStorySync) at whatever rate the record arrived. Half of the fix
    // is here; the sender half is in NetAvatarDriver, keyed on SharedWindows.AnyGrabbedHere().
    //
    // WHY THE DRAWN POSE AND NOT THE FRAME — this is the load-bearing decision of the whole block.
    // The obvious implementation is to ease the grab FRAME toward the received pose. It would have
    // broken the sync outright, for two independent reasons, both in a file this lane does not own:
    //   * Net.RemoteMapStory.TrackFrame decides "a hand here moved this window" by watching the grab
    //     frame drift away from a baseline it records when it applies a remote pose — and the
    //     baseline it records is the FINAL pose. An eased frame is not at that pose for the next
    //     ~200 ms, so every remote apply would have been read back as a LOCAL user move: it sets
    //     local.Moving, and ResolvePose's first line is `if (local.Moving) return;`. The client
    //     would apply one pose and then refuse every following one for the rest of the drag.
    //   * When the (self-inflicted) move "settled", the same path bumps the pose stamp and this
    //     client would become the room's LAST MOVER — publishing a pose nobody made, which the
    //     original dragger then follows. A stamp war built out of an animation.
    // Easing the drawn pose leaves the frame exactly where the sender's arithmetic expects it: the
    // frame is the AUTHORITY (it is what TryReadFrame samples, what PanelPoseWatch locks, what the
    // grab handle carries) and _visual is the PICTURE. The bar hangs under _visual for the same
    // reason — a bar that stepped while its window glided would be worse than either alone.
    //
    // WHAT THE SPLIT COSTS, stated so the next reader does not have to find it: the palm grab ZONE
    // sits on the frame, so while a glide is in flight the near-grab volume is up to the glide error
    // ahead of the visible bar (tens of milliseconds, centimetres at most, and only on a window a
    // remote player is dragging out from under you). The LASER bar collider is on the bar itself and
    // therefore always agrees with the picture, which is the one that matters — you aim at what you
    // can see.
    //
    // CONVERGENCE. k = 1 − exp(−λ·dt) is the frame-rate-independent exponential: the error decays by
    // a factor e every 1/λ = 66 ms whatever the frame rate, monotonically, and never overshoots
    // (k ∈ (0,1) ⇒ the result is strictly between the current pose and the target). Against a
    // CONTINUOUS stream it does not fall behind without bound either — the steady-state lag of a
    // first-order filter tracking a constant velocity v is v/λ, i.e. 3 cm at a brisk 0.5 m/s — and
    // the instant the drag stops that residue decays to nothing. It also TERMINATES rather than
    // crawling: below one authored window pixel of position error (the smallest gap that can move a
    // rendered texel — the same unit and the same threshold the pose-gap instrument above reports
    // in) plus a tenth of a degree, the glide is ended and the drawn pose pinned to the frame
    // outright. And it never runs at all for the cases the requirement calls out — opened, resized
    // or re-seated all pin instead (see PlaceFrameAt, SetExtraScale, SnapFrameTo and Build).
    //
    // IT DOES NOT FIGHT PanelPoseWatch. The lock's subject is the grab frame, which this never
    // touches; the frame is written once, exactly, by the same external writer as before, and that
    // writer is already announced (ModalFallback's placements announce Writer.Placement, the two net
    // appliers are attributed Writer.Peer through ModalFallback.9.Spawn's peerOwned predicate).
    //
    // REJECTED, and why:
    //   * EASE THE FRAME. The two-paragraph reason above. This is the trap.
    //   * A DISTANCE THRESHOLD to tell "a remote drag step" from "a re-seat" ("snap if it jumped
    //     more than X"). Rejected: with today's sender a whole drag arrives as ONE large jump (see
    //     the note in PlaceFrameAt), so a threshold would snap precisely the case that must glide.
    //     The announce token is an exact answer where the threshold was a guess.
    //   * RAISING ExtrasSendRateHz for everybody. Rejected — bandwidth is a shared budget and the
    //     board already showed the right shape: raise the cadence only while something is moving.
    //   * AN INTERPOLATION BUFFER (hold the last two samples and play them back one interval late).
    //     Rejected for the reason the board rejected it: it buys exactness at the price of a fixed
    //     added latency on a pose a human is dragging, and the project already has one accepted
    //     answer to this exact question.

    /// <summary>
    /// Advance the DRAWN pose one frame — or pin it to the grab frame, which is what happens for
    /// every window that is not gliding and therefore in every session without multiplayer.
    /// </summary>
    private void AdvanceVisual(float targetFactor)
    {
        if (_frame == null)
            return;

        // A HAND ON THE BAR IS ALWAYS 1:1. A carry that lagged the palm would feel like rubber, and
        // the sender must publish exactly the pose the dragging player is looking at.
        if (IsGrabbed)
            _easing = false;

        if (!_easing || !_visualValid)
        {
            PinVisualToFrame(targetFactor);
            return;
        }

        // Unscaled: a floated window must keep gliding while the game's own clock is stopped (a
        // halted ActionProcessor, a pause), and none of this is game state.
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
        float k = 1f - Mathf.Exp(-Net.NetProtocol.InterpolationSharpness * dt);
        _visualPos = Vector3.Lerp(_visualPos, _frame.position, k);
        _visualRot = Quaternion.Slerp(_visualRot, _frame.rotation, k);
        // SCALE RIDES THE SAME k — the remote board's defect (e) ("das Bewegen ist jetzt flüssig,
        // aber das Skalieren/Zoomen des Bretts nicht") was exactly an eased pose beside an assigned
        // scale. A remote resize arrives in the same record as the pose; move and zoom are one
        // motion here too.
        _visualScale = Mathf.Lerp(_visualScale, targetFactor, k);

        // TERMINATION — one authored window pixel and a tenth of a degree. Below that there is
        // nothing left for the eye, so the glide ENDS instead of crawling toward a limit it never
        // reaches. metersPerPixel × the panel's own scale chain is the world size of one authored
        // pixel, i.e. the same unit the pose-gap instrument reports in.
        float unit = WorldUIConfig.CanvasScaleMm.Value * 0.001f
                     * _spawnWorldScale * _extraScale * Mathf.Max(_visualScale, 1e-4f);
        bool arrived = Vector3.Distance(_visualPos, _frame.position) <= unit * PoseGapThresholdPx
                       && Quaternion.Angle(_visualRot, _frame.rotation) <= ArrivedDegrees
                       && Mathf.Abs(_visualScale - targetFactor) <= ArrivedScale;
        if (arrived)
        {
            PinVisualToFrame(targetFactor);
            return;
        }
        WriteVisual();
    }

    /// <summary>A residual rotation error below this cannot be seen at reading distance — matches
    /// <see cref="PanelPoseWatch.TurnEpsilonDeg"/>'s order of magnitude, deliberately tighter so the
    /// glide can never end ON the lock's own noise floor.</summary>
    private const float ArrivedDegrees = 0.1f;

    /// <summary>A residual size-factor error below this is under the wire's own quantization step
    /// (<c>NetProtocol.EncodeStorySize</c>), so it cannot describe a size any peer actually sent.</summary>
    private const float ArrivedScale = 0.002f;

    /// <summary>Drawn pose := grab frame, glide over. The state every private window is in on every
    /// frame of its life.</summary>
    private void PinVisualToFrame(float factor)
    {
        if (_frame == null)
            return;
        _visualPos = _frame.position;
        _visualRot = _frame.rotation;
        _visualScale = factor;
        _visualValid = true;
        _easing = false;
        WriteVisual();
    }

    /// <summary>Push the drawn pose onto the transform the bar hangs under. The holder is at
    /// identity pose and identity scale (the deadlock fix), so world and local agree here.</summary>
    private void WriteVisual()
    {
        if (_visual == null)
            return;
        _visual.SetPositionAndRotation(_visualPos, _visualRot);
        _visual.localScale = Vector3.one * _visualScale;
    }

    // REMOTE POSE EASING state. _visualValid false = "nothing drawn yet", which pins on the next
    // tick; _easing is armed ONLY by PlaceFrameAt, and only for a revealed shared window whose write
    // no local placement announced.
    private bool _visualValid;
    private bool _easing;
    private Vector3 _visualPos;
    private Quaternion _visualRot = Quaternion.identity;
    private float _visualScale = 1f;

    /// <summary>One authored window pixel — the smallest gap that can move a rendered texel.</summary>
    internal const float PoseGapThresholdPx = 1f;

    // POSE-GAP INSTRUMENT state: the host position Update published this frame.
    private Vector3 _updatePos;
    private bool _updatePosValid;

    /// <summary>
    /// Mod-owned holder component whose ONLY job is the LateUpdate host re-sync (see
    /// <see cref="LateSyncHost"/>). Lives on the holder GameObject, so it is destroyed with
    /// it in <see cref="GrabbableModal.Destroy"/> — no explicit lifecycle management.
    /// </summary>
    private sealed class HostLateSync : MonoBehaviour
    {
        internal GrabbableModal? Owner;

        private void LateUpdate() => Owner?.LateSyncHost();
    }

    /// <summary>
    /// DIAG SPAM FIX: gate <c>ConvertedPanel.Diagnostic</c> so the change-gated MODAL DIAG
    /// snapshot fires at most ~1/s per panel WHILE the host pose is actually changing (a
    /// carry/laser-drag/recall); a static host keeps Diagnostic permanently ON, so every
    /// state CHANGE (open/close/adopt, canvas/order flips, the settle line after a drag
    /// ends) still logs immediately and unthrottled.
    ///
    /// <para><b>ModBuild 199 — THE THROTTLE NOW OWNS THE LOG AND NOTHING ELSE.</b> Until ModBuild 198
    /// <c>ConvertedPanel.Diagnostic</c> was named for LOG VERBOSITY but was ALSO the gate
    /// <c>CanvasConversion.4.Lifecycle.cs</c> used for three pieces of REAL PER-FRAME WORK: the
    /// every-frame <c>AdoptNestedCanvases</c> path, <c>ReassertConversionFrame</c>, and
    /// <c>ReassertAdoptedSorting</c>, whose own comment at the call site reads <i>"FLICKER FIX (modal
    /// hosts only) ... Re-assert every frame for the (few) modal hosts"</i>. Clearing the flag below
    /// therefore ran that every-frame fix at roughly 1 Hz <b>for exactly as long as the player held
    /// the window</b> — the one interval the user reports the flicker under. The two concerns are
    /// separate fields now (<c>ConvertedPanel.PerFrameGuards</c> is the work gate and nothing
    /// throttles it), so a log throttle can never again throttle a fix. This method may turn the log
    /// off as aggressively as it likes.</para>
    ///
    /// <para><b>AND THE COUPLING IS NOT THE SAME CLAIM AS THE CAUSE.</b> The guard can only have
    /// caused a visible defect if it CORRECTS something while a window moves.
    /// <c>CanvasConversion.TickGuardBudget</c> counts exactly that and prints the MOVING correction
    /// rate; the ModBuild 198 hardware log already suggests the answer is "almost never" (the
    /// overrideSorting re-clear branch fired 3 times in a whole session, all on one canvas, which
    /// then conceded permanently and never entered that branch again). Read the MODAL GUARD BUDGET
    /// line before building anything else on this.</para>
    /// </summary>
    private void ThrottleDiagWhileMoving(Transform host)
    {
        // Movement epsilon: 5 mm at diorama scale — below the snapshot's own cm rounding,
        // so anything smaller never spammed in the first place. Rotation guards a pure spin.
        float eps = 0.005f * _spawnWorldScale;
        bool moving = (host.position - _diagLastPos).sqrMagnitude > eps * eps
                      || Quaternion.Angle(host.rotation, _diagLastRot) > 0.5f;
        _diagLastPos = host.position;
        _diagLastRot = host.rotation;

        // Publish the motion state for the guard-budget instrument. This is the SAME epsilon the log
        // throttle uses, so "MOVING" in the budget line means exactly what "throttled" used to mean —
        // otherwise the instrument would be measuring a different interval than the one under
        // suspicion. HELD is narrower and is reported alongside: a window can move without a hand on
        // it (a recall, a presence-regain refloat), and only the HELD case is the user's complaint.
        _panel.GuardHostMoving = moving;
        _panel.GuardHostHeld = _handle != null && _handle.IsGrabbed;

        if (!moving)
        {
            _panel.Diagnostic = true; // static host: change-gated DIAG stays fully live
            return;
        }
        float now = Time.unscaledTime;
        if (now >= _diagNextAllowed)
        {
            _diagNextAllowed = now + DiagThrottleSeconds;
            _panel.Diagnostic = true; // one snapshot line for this second of movement
        }
        else
        {
            // LOG ONLY. ConvertedPanel.PerFrameGuards stays true through this write (Diagnostic's
            // setter latches the work gate on and never off), so the per-frame modal maintenance
            // keeps running at full rate for the whole drag.
            _panel.Diagnostic = false; // swallow the per-frame pos-churn lines
        }
    }

    // ---- SHARED-WINDOW BAR COLOUR -----------------------------------------------------------
    //
    // USER REQUEST (2026-08-22, verbatim):
    //
    //   "3) Die Fenster die für alle Spieler sichtbar sind sollen eine andere Farbe beim dem
    //    Greifbalken haben (zB Blau) um anzuzeigen, dass es ein Fenster ist das alle sehen."
    //
    // WHERE THE COLOUR IS DECIDED: in <see cref="SyncSharedBarTint"/> below, and nowhere else. It
    // asks <see cref="SharedWindows"/> — which owns the DEFINITION of "shared" and its whole
    // rationale — and turns the answer into exactly one of two colours. This class contributes no
    // policy: it does not know which windows are shared, only how to paint a bar.
    //
    // WHY IT IS RE-EVALUATED EVERY TICK RATHER THAN DECIDED AT BUILD. "Shared" is not a property of
    // the window CLASS, it is "shared FOR THIS CLIENT, RIGHT NOW" (SharedWindows' class doc), and
    // participation can FLIP WHILE THE WINDOW STANDS: the map story window and the quest popup are
    // synced only among players with the 3D world map on, MapRoomDriver reads that config live, and
    // the player may toggle it with the window open. A colour written once at Build would then be a
    // false statement for the rest of that window's life. The cost of being right is one Color
    // comparison per floated window per frame; the write itself is change-gated, so a standing
    // window costs the comparison and nothing else.
    //
    // WHAT A PRIVATE BAR COSTS: nothing at all. _barTint starts at PrivateBarColor, which is the
    // literal the bar material was constructed with, so the gate never opens for a private window —
    // no material write, no log line, no behavioural change. That is the proof that today's picture
    // is preserved for every window outside the shared set, INCLUDING the map story window and the
    // quest popup for a player with the 3D map switched off, for whom those windows really are
    // private (moving one moves nothing for anybody).
    //
    // WHAT WAS REJECTED:
    //   * ONE SHARED BLUE MATERIAL for every shared bar. Rejected — and it is the obvious trap here.
    //     WorldUIAssets.CreateFlatMaterial constructs a new Material per call, so every bar already
    //     owns its own; the hover/held highlight then writes sharedMaterial.color on exactly one
    //     bar. Hand two bars the same Material instance and a single hover would turn EVERY floated
    //     window's bar gold, and this tint would turn every bar blue.
    //   * WRITING THE MATERIAL FROM HERE. Rejected: the highlight re-derives the bar colour from
    //     PanelGrabHandle's own base field whenever it goes out, so a write from outside would be
    //     reverted to brass by the next un-highlight. The base colour is handed to the handle
    //     instead (PanelGrabHandle.SetBarBaseColor), which is the single writer of that material.
    //   * TINTING BY WINDOW CLASS ("a story box is always blue"). Rejected in SharedWindows' doc,
    //     recorded here so it is not re-litigated at the paint end either.
    //   * A CONFIG DIAL for the colour. Not asked for; the user named blue and the mod picks it.

    /// <summary>
    /// The bar's resting brass — the colour of a PRIVATE window's grab bar, unchanged since the
    /// handle was introduced and deliberately still expressed as the same literal, so a private bar
    /// is byte-identical to the one that shipped before the shared tint existed.
    /// </summary>
    private static readonly Color PrivateBarColor = new(0.62f, 0.5f, 0.28f);

    /// <summary>The bar's current RESTING colour (the highlight paints over it and falls back to
    /// it). Seeded with the colour <see cref="EnsureFrame"/> builds the material with, so the
    /// change gate below is closed for every window that is not shared.</summary>
    private Color _barTint = PrivateBarColor;

    /// <summary>Is this window SHARED for this client right now — <see cref="SharedWindows.IsShared"/>
    /// as of the last tick. False for every window in a single-player session and for every private
    /// window in a multiplayer one, so both behaviours it gates (the release re-face and the remote
    /// pose easing) are inert there. Written only by <see cref="SyncSharedBarTint"/>; see the note
    /// there for why it is cached rather than asked.</summary>
    private bool _shared;

    /// <summary>
    /// Has a REMOTE player's pose ever been applied to this window? Latched by
    /// <see cref="PlaceFrameAt"/> on the easing path and never cleared while the window floats — the
    /// peer analogue of <see cref="UserMoved"/>.
    ///
    /// <para>NOTHING IN THIS LANE READS IT YET, and that is deliberate rather than an oversight. It
    /// exists for the presence-regain refloat (<c>ModalFallback.RefloatOpenWindows</c>, a file this
    /// lane does not own), which skips a window the LOCAL player moved ("parked windows stay put")
    /// but not one a REMOTE player placed — so a doff/don currently yanks a shared window back to
    /// this player's gaze and, being a real user act, then publishes that pose to the room. The
    /// argument for the skip is identical to <see cref="UserMoved"/>'s, with "a user" widened to
    /// "any user"; the one-line condition is <c>wp.Grab.UserMoved || wp.Grab.PeerPlaced</c>.</para>
    /// </summary>
    internal bool PeerPlaced { get; private set; }

    /// <summary>
    /// Re-evaluate whether <paramref name="window"/> is shared FOR THIS CLIENT right now, and paint
    /// the grab bar accordingly. Called once per tick per floated window; see the block above.
    /// </summary>
    internal void SyncSharedBarTint(UIWindow? window)
    {
        if (_handle == null)
            return; // not built yet (or already torn down) — nothing to paint
        // This IS SharedWindows.IsShared(window), expanded only because the LOG LINE has to name the
        // kind: a hardware report saying "the quest window was brass" must be readable against a log
        // that says which kind that window was and whether this client took part in its sync.
        SharedWindowKind kind = SharedWindows.KindOf(window);
        bool shared = kind != SharedWindowKind.None && SharedWindows.ParticipatesHere(kind);
        // THE SAME ANSWER, CACHED FOR THE TWO CONSUMERS THAT CANNOT SEE THE UIWindow (2026-08-22,
        // requests 7a and 7b): the release re-face gate (OnGrabFinished) and the remote pose easing
        // (PlaceFrameAt). Both are called from paths that hold the mod-owned grab and NOT the game
        // window — the grab handle's release edge and the two net appliers — so neither can ask
        // SharedWindows itself. This is the one place per tick that knows both halves (it is called
        // from ModalFallback.Tick immediately before Tick(), with the window in hand), and the cached
        // answer is therefore at most one frame old — which is the same staleness the BAR COLOUR
        // already carries, so a window whose grab bar is blue is exactly a window that will not
        // re-face. Recomputing it per release rather than caching would need this class to store the
        // UIWindow, i.e. a second reference to a game object whose lifetime ModalFallback owns.
        _shared = shared;
        Color wanted = shared ? SharedWindows.BarTint : PrivateBarColor;
        if (wanted == _barTint)
            return;
        _barTint = wanted;
        _handle.SetBarBaseColor(wanted);
        VRLog.Info("WorldUI", $"SHARED WINDOW BAR: '{_logName}' (game window '{window?.name ?? "?"}', " +
                              $"kind {kind}) now wears the {(shared ? "SHARED BLUE" : "private brass")} " +
                              $"grab bar — {(shared
                                  ? "every player in this room sees this window's state, so moving it is a shared act"
                                  : "this window is private to this client right now (its sync is off, or it is not a shared kind)")}.");
    }

    // ---- build ------------------------------------------------------------------------------

    private void EnsureFrame()
    {
        if (_holder != null && _frame != null)
            return;

        // NOTHING IS BUILT FOR AN UNBUILT MODAL (ModBuild 226), and this guard is the belt to the
        // ModBuild-226 braces. The user, verbatim: "Als mein Mitspieler gejoint ist, kam ein LEERES
        // FENSTER auf - sowas soll per se niemals passieren." Those were grab bars with no window:
        // ModalFallback.8.Convert used to construct a GrabbableModal for every float and skip
        // Build() only for hover cards — then store the unbuilt object in wp.Grab anyway. Two
        // callers reach PlaceFrameAt on it, PlaceFrameAt calls this, and this built a holder, a
        // brass bar and a collider around `_panel == null`: never sized (Tick returns before
        // SyncBar), never ordered, never render-hidden, and never moved with the card the bar was
        // supposed to belong to. The log named all 204 of them by the field initialiser they still
        // carried — `MODAL GRAB: 'Menu'`, `_logName`'s value when Build never ran.
        //
        // The real repair is at the source (no GrabbableModal is constructed for a hover card any
        // more), so this branch is expected to be DEAD. It is here because the failure it prevents
        // is invisible: an unbuilt modal produces furniture that looks exactly like a real window's
        // and behaves like nothing at all, and there is no other place in the class that could
        // notice. Cheap, and it turns a silent absurdity into a refusal with a name.
        if (_panel == null)
        {
            VRLog.Warn("WorldUI", $"MODAL GRAB REFUSED for '{_logName}': EnsureFrame was reached on a "
                                  + "GrabbableModal whose Build() never ran, so there is no panel for "
                                  + "a frame to carry. Nothing is created. This is the empty-grab-bar "
                                  + "defect of ModBuild 225 and it should be UNREACHABLE since 226 — "
                                  + "if this line is in the log, a caller is constructing a modal it "
                                  + "does not build and then placing it.");
            return;
        }

        var holderGo = new GameObject($"GloomhavenVR.ModalGrab_{_logName}");
        _holder = holderGo.transform;
        // DRAG-FLICKER FIX: LateUpdate re-sync of the host from the frame — see LateSyncHost.
        holderGo.AddComponent<HostLateSync>().Owner = this;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        // THE DRAWN POSE (see the REMOTE POSE EASING block). A SIBLING of the frame, not a child:
        // it must be able to lag behind it, which a child cannot. Identical to the frame in every
        // frame of every window that is not gliding, which is every window outside multiplayer.
        var visualGo = new GameObject("Visual");
        _visual = visualGo.transform;
        _visual.SetParent(_holder, worldPositionStays: false);

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        // LOST-MENU FIX: keep the primitive's BoxCollider as the LASER-only drag-bar target
        // instead of destroying it. The unit box scaled by the bar transform matches the
        // VISIBLE brass strip exactly (padded slightly via BarColliderPad); handed to the
        // shared handle as BarCollider so RayGrabDriver ray-tests ONLY this strip. It is a
        // trigger on the mod render layer, so the physics ray (RayInteractor) still ignores
        // it, and it is NOT registered with VRInteractables — the palm grab keeps using the
        // generous frame zone below (near-grab is deliberate; the laser was the problem).
        var barCollider = bar.GetComponent<BoxCollider>();
        barCollider.isTrigger = true;
        barCollider.size = new Vector3(1f, BarColliderPad, BarColliderPad);
        // Under the DRAWN pose, not the frame: the visible bar and the window it belongs to must
        // move as one object, and a glide moves the window. Its local numbers are unchanged —
        // _visual carries the same localScale (the user grab factor) the frame does, so SyncBar's
        // frame-local metres still mean what they meant.
        bar.transform.SetParent(_visual, worldPositionStays: false);
        bar.transform.localScale = new Vector3(0.2f, BarThickness, BarThickness);
        var mr = bar.GetComponent<MeshRenderer>();
        // Item 3: opaque brass that OCCLUDES the menu. The bundled GloomhavenVR/Overlay shader
        // (overlay:true) exposes _ZWrite/_ZTest; force ZWrite ON so the bar draws solid (not the
        // Sprites/Default alpha-blend that read semi-transparent), while leaving ZTest at the
        // default LEqual so a hand held physically in front still occludes the solid handle. The
        // sortingOrder below is what actually lifts it OVER the depthless menu canvas.
        Material barMat = WorldUIAssets.CreateFlatMaterial(PrivateBarColor, overlay: true);
        if (barMat.HasProperty("_ZWrite"))
            barMat.SetInt("_ZWrite", 1);
        mr.sharedMaterial = barMat;
        // Rides the panel's ladder order at a fixed offset (see BarOrderOffset). Registered after
        // the renderer exists; the order pass seats it immediately, so there is no unordered frame.
        CanvasConversion.RegisterOrderFollower(_panel, mr, BarOrderOffset);
        _bar = bar.transform;

        // Grab zone + shared grab core (collider BEFORE the handle: its OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.isTrigger = true;
        _grabZone.size = new Vector3(0.25f, 0.05f, 0.05f);
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        _handle.Init(this, mr, "WorldUI", $"{_logName} menu");
        // LOST-MENU FIX: split laser vs palm — the far ray grabs ONLY the visible bar strip.
        _handle.SetBarCollider(barCollider);

        // Render-only mod layer — grabs/pokes route through the registries, not layers.
        VRLayers.Apply(holderGo);

        // User ruling 2026-08-02 round 2 ("ein Aufploppen der Greifbar ... woanders"): this holder
        // is a SCENE-ROOT tree, not a child of the host — the host follows the frame, not the other
        // way round — so the reveal gate's host-canvas hide could never touch the bar's MeshRenderer
        // or the modal depth mask. Register it as an extra render root: the panel's render hide now
        // walks this tree too, and because the panel is already render-hidden when Build runs, the
        // registration hides the bar in the very frame it was created (it is built at the PRE-FIT
        // rect/scale, which is exactly the wrong place the user saw it pop in at).
        CanvasConversion.AddRenderRoot(_panel, _holder);
        // ModBuild 230: the holder exists from here, so from here it is sweepable (see LiveHolders).
        if (!LiveHolders.Contains(this))
            LiveHolders.Add(this);
        VRLog.Info("WorldUI", $"MODAL GRAB: '{_logName}' is now a grabbable/scalable world element " +
                              "(grip the bar to move, two hands to resize 0.5x-2x).");
    }

    private void SyncBar(float halfHeight, float width, float worldScale)
    {
        if (_bar == null || _grabZone == null)
            return;
        // All dims are frame-local metres. With the holder now at identity scale (deadlock
        // fix) the fixed constants must carry worldScale themselves so the bar/zone keep the
        // same WORLD size relative to the (worldScale-sized) panel as before.
        //
        // Empty-gold-plate fix: proportion the bar to SHORT panels. halfHeight arrives in
        // frame-local metres (worldScale included), so divide it back out for the real panel
        // height; a panel shorter than the full-size reference slims the bar thickness AND
        // pulls it closer (smaller gap) by the same factor, floored at MinBarProportion so
        // the grab/laser target never vanishes. Board-scale menus land at proportion 1 —
        // numerically identical to the previous fixed constants.
        float panelHeight = halfHeight * 2f / Mathf.Max(worldScale, 1e-4f);
        float proportion = Mathf.Clamp(panelHeight / BarFullSizePanelHeightMeters,
            MinBarProportion, 1f);
        float gap = BarGapMeters * proportion * worldScale;
        float thickness = BarThickness * proportion * worldScale;
        float minWidth = MinBarWidth * worldScale;
        float zoneDepth = 0.05f * worldScale;
        float y = -(halfHeight + gap);
        float barWidth = Mathf.Max(width * BarWidthFraction, minWidth);
        _bar.localPosition = new Vector3(0f, y, 0f);
        _bar.localScale = new Vector3(barWidth, thickness, thickness);
        _grabZone.center = new Vector3(0f, y, 0f);
        _grabZone.size = new Vector3(Mathf.Max(width * ZoneWidthFraction, minWidth), zoneDepth, zoneDepth);
    }

    // ---- teardown ---------------------------------------------------------------------------

    /// <summary>Destroy the mod-owned holder (the game host is released separately by the caller).</summary>
    internal void Destroy()
    {
        LiveHolders.Remove(this); // ModBuild 230 — leaves the sweep's live set with the holder itself
        if (_holder != null)
            Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _visual = null;
        _bar = null;
        _grabZone = null;
        _handle = null;
        _visualValid = false;
        _easing = false;
    }
}
