using GloomhavenVR.Core;
using UnityEngine;

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

    private Transform? _holder;                 // identity pose, localScale = diorama WorldScale
    private Transform? _frame;                  // grab root at the panel centre; localScale = user factor
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
    internal void SetExtraScale(float extraScale) => _extraScale = extraScale;

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
    /// Re-seat the frame (and thus the whole panel) at a fresh pose — used on presence
    /// regain (RefloatOpenWindows), where the user may have physically moved while the HMD
    /// was off and a menu stranded out of view would be un-dismissable.
    /// </summary>
    internal void PlaceFrameAt(Vector3 position, Quaternion rotation)
    {
        EnsureFrame();
        if (_frame == null)
            return;
        _frame.SetPositionAndRotation(position, rotation);
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
    void IPanelGrabOwner.OnGrabFinished()
    {
        if (_frame == null)
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

        Transform host = _panel.HostGo.transform;
        SyncHostToFrame(host, factor, metersPerPixel, worldScale);
        // POSE-GAP INSTRUMENT (see LateSyncHost): remember what UPDATE published, so the LateUpdate
        // re-sync can say how far the window moved in between — i.e. how stale the pose is for every
        // consumer that samples it during Update.
        _updatePos = host.position;
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
    /// Copy the frame pose/scale onto the game-owned host (shared by the Update-time
    /// <see cref="Tick"/> and the LateUpdate re-sync in <see cref="HostLateSync"/>).
    /// </summary>
    private void SyncHostToFrame(Transform host, float factor, float metersPerPixel, float worldScale)
    {
        host.SetPositionAndRotation(_frame!.position, _frame.rotation);
        host.localScale = Vector3.one * (metersPerPixel * worldScale * _extraScale * factor);
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
        float unit = metersPerPixel * _spawnWorldScale * _extraScale * factor;
        if (_updatePosValid && unit > 1e-9f)
        {
            float gapPx = Vector3.Distance(_frame.position, _updatePos) / unit;
            _panel.PoseGapSamples++;
            _panel.PoseGapSumPx += gapPx;
            if (gapPx > _panel.PoseGapWorstPx)
                _panel.PoseGapWorstPx = gapPx;
            if (gapPx > PoseGapThresholdPx)
                _panel.PoseGapOverOnePx++;
        }
        _updatePosValid = false;

        SyncHostToFrame(host, factor, metersPerPixel, _spawnWorldScale);
    }

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

    // ---- build ------------------------------------------------------------------------------

    private void EnsureFrame()
    {
        if (_holder != null && _frame != null)
            return;

        var holderGo = new GameObject($"GloomhavenVR.ModalGrab_{_logName}");
        _holder = holderGo.transform;
        // DRAG-FLICKER FIX: LateUpdate re-sync of the host from the frame — see LateSyncHost.
        holderGo.AddComponent<HostLateSync>().Owner = this;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

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
        bar.transform.SetParent(_frame, worldPositionStays: false);
        bar.transform.localScale = new Vector3(0.2f, BarThickness, BarThickness);
        var mr = bar.GetComponent<MeshRenderer>();
        // Item 3: opaque brass that OCCLUDES the menu. The bundled GloomhavenVR/Overlay shader
        // (overlay:true) exposes _ZWrite/_ZTest; force ZWrite ON so the bar draws solid (not the
        // Sprites/Default alpha-blend that read semi-transparent), while leaving ZTest at the
        // default LEqual so a hand held physically in front still occludes the solid handle. The
        // sortingOrder below is what actually lifts it OVER the depthless menu canvas.
        Material barMat = WorldUIAssets.CreateFlatMaterial(new Color(0.62f, 0.5f, 0.28f), overlay: true);
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
        if (_holder != null)
            Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _bar = null;
        _grabZone = null;
        _handle = null;
    }
}
