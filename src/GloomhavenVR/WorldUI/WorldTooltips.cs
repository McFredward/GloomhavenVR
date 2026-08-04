using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// World-space tooltips on converted surfaces (ROADMAP P3c #8, config-gated).
///
/// SCENARIO-ONLY (I2, hardware test #5): in <see cref="VRMode.Menu2D"/> the tooltip
/// canvas must stay untouched — it is Screen-Space-Camera on the UICamera, which the
/// FlatScreen redirects into its RenderTexture, so menu tooltips already appear on
/// the quad at native size. Test #5 flipped it to world space in the menu anyway:
/// with no poke anchor the flip never applied a scale, leaving the canvas rect at
/// its pixel dimensions in WORLD METERS (a ~2560 px canvas ≈ 2.5 km), rendered by
/// the head camera — the "giant mouseover" report. The conversion now gates on the
/// VR mode state machine (flips in scenario modes, restores on entering Menu2D) and
/// applies its world scale AT FLIP TIME plus every frame, never only on anchor.
///
/// The trigger side is free: fingertip pokes on converted canvases synthesize REAL
/// <c>pointerEnter/Exit</c> events (Phase-2 UguiPointer), which drive the game's own
/// <c>UITooltipTarget.OnPointerEnter/Exit</c> → the static <c>UITooltip</c> API —
/// no patches needed. What this class owns is PRESENTATION: the tooltip lives on the
/// dedicated persistent <c>tooltipCanvas</c> (Screen-Space-Camera), which is useless
/// in the HMD; while active the canvas is flipped to WorldSpace and parked at a FIXED
/// spot — clearly ABOVE the CONTROL BOARD's (PlayTray) TOP edge, horizontally centered on
/// the board, facing the player and scaling / tilting with the board (user #7a; the
/// pre-existing curved-table <see cref="PanelSlot.Tooltip"/> slot is the menu / no-tray
/// fallback). It is NOT
/// anchored to the fingertip (the hover can come from the laser too, and the user wants a
/// stable reading spot, not a spot that jumps around). A short hover grace (user #7b) keeps
/// it from flickering away on micro-jitter off a tiny target: the game's own show/hide fade
/// is widened (<see cref="FadeGraceSeconds"/>) and the parked position is latched for
/// <see cref="HoverGraceSeconds"/> after the content stops showing. The whole presentation
/// is gated on <see cref="WorldUIConfig.ActionElementHints"/> (user #7c).
///
/// FLAT 2D (part A): the game tooltip's content carries baked local-z / local rotation
/// (subtle styling under the perspective UI camera) that becomes literal geometry on a
/// world-space host — the text protruded in 3D past the panel. Routing this SHARED,
/// game-repositioned canvas through <see cref="CanvasConversion.Convert"/> (reparent +
/// content-fit, the DamageTooltip path) would be too invasive for a persistent canvas
/// the game lays out internally, so we apply the same <see cref="CanvasConversion.FlattenSubtree"/>
/// idea in place — zero the baked local-z / rotation on every descendant every frame
/// (tooltip lines are pooled/rebuilt per hover, and the game rewrites them) — and add a
/// <see cref="RectMask2D"/> on the tooltip frame so any 2D overflow is clipped inside the
/// panel. Both are fully reversed on <see cref="Restore"/> (originals restored; a mask we
/// added is destroyed) so the vanilla 2D menu tooltip keeps its styling.
///
/// Scale sanity (I2): world scale = <see cref="WorldUIConfig.CanvasScaleMm"/> (mm per
/// uGUI pixel, default 1) × 0.001 × diorama scale × 0.5 — half the panel framework's
/// meters-per-pixel (CanvasConversion.cs: <c>metersPerPixel = CanvasScaleMm * 0.001f</c>)
/// so a ~400 px tooltip reads ~20 cm at arm's length instead of 40 — times the user's live
/// <see cref="WorldUIConfig.HoverInfoScale"/> dial (normalized against its default, so 1× at
/// the factory value; see the LateTick comment).
///
/// Verified via ilspycmd (GH.Runtime.dll): <c>CanvasManager</c> holds
/// <c>[SerializeField] private Canvas tooltipCanvas;</c> (publicized) and only
/// re-binds <c>worldCamera</c> on scene load — harmless in world mode, we rebind per
/// frame. <c>UITooltip.uiCamera</c> resolves per render mode incl. a world-camera
/// branch (UI-ARCH §6), so its internal placement math keeps working.
///
/// REVERSIBLE: render mode, worldCamera, plane distance, scale and sorting are
/// restored on disable/shutdown AND on every return to Menu2D; a Screen-Space canvas
/// re-drives its own rect once the mode is set back.
/// </summary>
internal sealed class WorldTooltips
{
    /// <summary>Unanchored world-space parking spot (out of every camera's view).</summary>
    private static readonly Vector3 ParkPosition = new(0f, -1000f, 0f);

    // ---- fixed-above-board anchor (user #7a, "above not inside" fix) -------------------
    // The tooltip is pinned clearly ABOVE the CONTROL BOARD's (PlayTray) TOP edge, along the
    // board's own UP axis and horizontally CENTERED on the board, proud toward the viewer, so
    // the whole PANEL BODY sits fully above the board face and never intrudes into it. The
    // anchor is recomputed from the board's LIVE world pose + lossy scale EVERY tick (see
    // TryResolveTooltipPose), so a tray grab-resize (which rewrites Root.localScale) and any
    // tray move/tilt re-aligns the hint immediately.
    //
    // Bug history: earlier revisions parked the panel at the board's top-LEFT corner and
    // pushed it outward by (board half-width + tooltip half-width + margin) derived from the
    // AUTHORED plate constants (InitiativeMountWidth / BoardTopLocalY). The VISIBLE board
    // (bundled frame + decorations) is LARGER than the authored plate, so a half-extent based
    // on the constants UNDER-estimated the real edge and the panel still sat inside the board.
    // The anchor now (1) derives the board's REAL top edge from the tray renderers' combined
    // bounds (in board-LOCAL space, so tilt does not inflate it) instead of the constants, and
    // (2) places the pivot straight ABOVE that edge by (tooltip half-height + margin) — a
    // stable "fixed above" spot, not the side corner.

    /// <summary>Extra clearance above the board's TOP edge, board-local metres × live scale.</summary>
    private const float BoardAnchorMarginY = 0.03f;

    /// <summary>Metres proud toward the viewer (board-local -Z is the viewer side) × live scale.</summary>
    private const float BoardAnchorProudZ = 0.02f;

    // ---- hover grace / stickiness (user #7b) ------------------------------------------
    /// <summary>
    /// Placement latch window: after the game's tooltip content stops showing, keep the
    /// canvas parked at the anchor this long so a micro-jitter off a tiny UITooltipTarget
    /// does not teleport the canvas offscreen-and-back (a jarring flicker of its own) and a
    /// re-hover within the window finds it already anchored.
    /// </summary>
    private const float HoverGraceSeconds = 0.5f;

    /// <summary>
    /// Native fade grace: while converted we widen the game's OWN show/hide fade (its
    /// <c>UITooltip.transition = Fade</c>, <c>transitionDuration</c>) to this so a jitter
    /// exit→enter inside the window is bridged by the game's own tween interruption (the
    /// fade-out is re-tweened back to full) — the tooltip never fully vanishes on tiny
    /// movements. Uses the game's self-cleaning mechanism only — we never fight its alpha /
    /// visual-state machine (it reads its own CanvasGroup alpha back, so forcing it would
    /// strand the shared singleton). Fully restored on <see cref="Restore"/>.
    /// </summary>
    private const float FadeGraceSeconds = 0.4f;

    /// <summary>Local rotation counts as 3D beyond this angle (degrees) off identity.</summary>
    private const float FlattenAngleEpsilon = 0.05f;

    /// <summary>Local z counts as 3D beyond this many uGUI pixels.</summary>
    private const float FlattenZEpsilon = 0.01f;

    /// <summary>Tooltip content is only "shown" (worth placing) above this CanvasGroup alpha.</summary>
    private const float ShownAlphaEpsilon = 0.05f;

    private Canvas? _canvas;
    private bool _converted;

    // Restore data.
    private RenderMode _originalMode;
    private Camera? _originalCamera;
    private float _originalPlaneDistance;
    private Vector3 _originalScale;
    // Sorting is only touched while the hint rides on a floated menu panel; both fields are
    // captured at the flip and written back verbatim in Restore() (reversibility).
    private int _originalSortingOrder;
    private bool _originalOverrideSorting;

    // ---- 2D flatten + frame clip (part A) ---------------------------------------------
    /// <summary>The persistent tooltip content singleton under the canvas (frame + text lines).</summary>
    private UITooltip? _tooltip;

    /// <summary>A RectMask2D WE added to the tooltip frame (null when none / the frame already had one).</summary>
    private RectMask2D? _addedMask;

    // ---- grace + native-fade override state (user #7b) --------------------------------
    /// <summary>Last time the game's tooltip content was genuinely shown (placement latch).</summary>
    private float _lastShownTime = float.NegativeInfinity;

    /// <summary>We widened the game's fade transition; originals for <see cref="Restore"/>.</summary>
    private bool _transitionOverridden;
    private UITooltip.Transition _originalTransition;
    private float _originalTransitionDuration;

    // ---- host ownership (user report 2026-08-03; see ResolveHostOwner) ------------------
    /// <summary>The floated window that owns the CURRENT hint, or null when the CONTROL BOARD does.</summary>
    private ConvertedPanel? _hostWindow;

    /// <summary>The host decision last LOGGED (reference-compared, so the change-gated diagnostic
    /// allocates its line only when the answer actually changes — never per frame).</summary>
    private ConvertedPanel? _hostLoggedWindow;

    /// <summary>False until the first host decision was logged (null == board is a real answer).</summary>
    private bool _hostLoggedOnce;

    // ---- diagnostics (dedupe so a per-frame path logs once) ---------------------------
    private bool _parkedLogged;
    private Vector3 _parkedLogPos;
    private bool _gateOffLogged;

    /// <summary>[Optimize] TooltipScanGate: earliest frame the CanvasManager may be searched for
    /// again while the tooltip canvas is still unresolved.</summary>
    private int _canvasSearchNextFrame;

    /// <summary>Frames between two CanvasManager searches (~1/6 s at 90 Hz) — a hover needs the
    /// canvas within human reaction time, not within one frame.</summary>
    private const int CanvasSearchIntervalFrames = 15;

    /// <summary>Extents captured in the last pose resolve (park diagnostic — prove the panel clears the board).</summary>
    private float _lastBoardTopEdgeWorldY;
    private Vector2 _lastTooltipHalfWorld;

    /// <summary>Descendant transforms flattened this session (original local z + rotation for Restore).</summary>
    private readonly List<FlattenEntry> _flattened = new(32);

    /// <summary>
    /// Graphics whose <c>raycastTarget</c> WE cleared while the canvas is world-space
    /// (all were true when cleared - the scan only records true->false flips), for
    /// verbatim restore. The tooltip is DISPLAY-ONLY: nothing under
    /// <c>CanvasManager.tooltipCanvas</c> carries a click/drag handler (verified in the
    /// decompiled sources - <c>UITooltip</c>/<c>UITooltipLines</c> hold no Button or
    /// IPointerClickHandler, and its line texts are already spawned with
    /// <c>raycastTarget = false</c>, UITooltip.cs:673), so making the remaining
    /// graphics (frame background, anchor graphic, icons) ray-transparent loses
    /// nothing. WHY: in world space the canvas is rendered by the HEAD camera, so the
    /// game's own InControl mouse module - which raycasts EVERY enabled
    /// GraphicRaycaster each frame at the parked mouse pixel - can suddenly "hover" the
    /// floating hint box and report pointer-over-UI while it is up; and no future
    /// surface registration can ever turn the hint into a hover thief for the mod's
    /// pointers either. Mutate-and-restore in the established ActorBars style.
    /// </summary>
    private readonly List<Graphic> _raycastCleared = new(16);

    /// <summary>Reused per-frame scan buffer (no steady-state allocation).</summary>
    private static readonly List<RectTransform> RectScratch = new(64);

    /// <summary>Reused raycast-neutralize scan buffer (no steady-state allocation).</summary>
    private static readonly List<Graphic> GraphicScratch = new(32);

    /// <summary>Reused world-corner buffer for measuring the tooltip's rendered size.</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    private struct FlattenEntry
    {
        public Transform Transform;
        public float OriginalLocalZ;
        public Quaternion OriginalLocalRotation;
    }

    /// <summary>
    /// THERE IS ONLY ONE TOOLTIP IN THE GAME. <c>CanvasManager.tooltipCanvas</c> is a single shared
    /// canvas, so wherever the player hovers — a figure, a card, a row of the mod's VR Options tab —
    /// the same canvas has to answer, and the right answer depends on WHO OWNS the thing being
    /// hovered (<see cref="ResolveHostOwner"/>, the ownership rule). Three cases, and the previous
    /// attempts each got one of them right:
    ///
    /// <list type="bullet">
    /// <item><description>The hover belongs to the BOARD — a card lying in a slot, a decision button
    /// docked under it, a world/hex tooltip: the canvas is world-space and parked above the control
    /// board, at the board's own angle, plus the user's per-board
    /// <c>[Cards] HoverHintOffset_&lt;board&gt;</c>.</description></item>
    /// <item><description>The hover belongs to a floated WINDOW or MENU — what a scenario does: the
    /// canvas is world-space and laid ONTO that panel (<see cref="TryResolveMenuPose"/>). Earlier
    /// attempts parked it above the board instead, so the hint left the menu and hung in the room
    /// facing the player; another sent it to screen space, and in a scenario the screen is not being
    /// shown at all, so it vanished outright.</description></item>
    /// <item><description>Menu on the FLAT SCREEN (ModalStyle=screen, or a conversion that failed):
    /// screen space is correct — that screen is what the player is looking at, exactly as in the
    /// main menu. Menu2D never converts in the first place, so the main menu was always this
    /// case.</description></item>
    /// </list>
    ///
    /// <para>The board/window split used to be "is ANY window floating" — the regression this class
    /// now avoids, because a tutorial box floating for minutes stole every card hint. See
    /// <see cref="ResolveHostOwner"/> and <see cref="ModalFallback.FindOwningWindow"/>.</para>
    /// </summary>
    private static bool MenuOnFlatScreen()
    {
        if (ModalFallback.MenuPanelHost != null)
            return false; // floated as a world panel — the panel case, not the screen case
        if (!Singleton<ESCMenu>.IsInitialized)
            return false;
        ESCMenu? menu = Singleton<ESCMenu>.Instance;
        return menu != null && menu.IsOpen;
    }

    /// <summary>
    /// The pose and scale that lay the tooltip flat ON the floated menu panel.
    ///
    /// <para>Both canvases hold the SAME screen rect: the tooltip canvas is the game's
    /// Screen-Space-Camera canvas (screen-sized), and the floated menu is that window's full screen
    /// rect converted to world space. Giving the tooltip canvas the panel's exact transform makes
    /// the two coincide, so the game's own placement — which positions the box beside the hovered
    /// row in screen coordinates — lands it beside that row on the panel, lying flat on it. Nothing
    /// here re-implements the game's arrangement; it just puts the two canvases in the same plane so
    /// the arrangement still means what it meant.</para>
    ///
    /// <para>A hair toward the viewer so the box renders in front of the menu rather than z-fighting
    /// with it.</para>
    ///
    /// <para>The panel is the one that OWNS the hover (<see cref="ModalFallback.FindOwningWindow"/>),
    /// never merely the topmost float — see that method for the regression this distinction fixes.</para>
    /// </summary>
    private static bool TryResolveMenuPose(ConvertedPanel? panel, out Vector3 position,
                                           out Quaternion rotation,
                                           out Vector3 scale, out int sortingOrder)
    {
        RectTransform? host = panel != null && panel.IsAlive ? panel.HostRect : null;
        if (panel == null || host == null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;
            sortingOrder = 0;
            return false;
        }

        rotation = host.rotation;
        scale = host.lossyScale;
        // TOWARD the viewer: the floated panel's +Z points AWAY from the head (PanelPlacement
        // convention — uGUI fronts render along −forward), so subtracting forward moves the
        // tooltip onto the viewer's side of the panel plane.
        position = host.position - host.forward * MenuPanelProudZ;
        // GEOMETRY IS NOT ENOUGH (hover-hint bug, second half): both canvases are drawn by the
        // SAME head camera in the transparent queue, where Unity sorts by sortingLayer →
        // sortingOrder → distance, and the game's shared tooltip canvas keeps its authored (much
        // lower) order, so the hint drew BEHIND the menu no matter how proud of the plane it sat.
        // Ride just above the host — restored verbatim in Restore().
        //
        // TRANSPARENCY ROUND: the host's order is no longer a constant. It is rewritten every
        // LateUpdate from the panel's eye distance (CanvasConversion.8.Order.cs), so this must keep
        // reading it LIVE (it does) and the lift must stay BELOW CanvasConversion.PanelOrderStep —
        // otherwise a hint laid on a far panel would climb into the next panel's slot and cover a
        // panel that is genuinely nearer than the menu it belongs to.
        sortingOrder = panel.HostCanvas != null ? panel.HostCanvas.sortingOrder + MenuPanelSortingLift : 0;
        return true;
    }

    /// <summary>Sorting steps the tooltip rides above the floated menu host it is laid on. Must
    /// stay under <c>CanvasConversion.PanelOrderStep</c> (16) — see the note at the read site.</summary>
    private const int MenuPanelSortingLift = 10;

    /// <summary>Metres in front of the floated menu panel the tooltip is laid, so it never z-fights.</summary>
    private const float MenuPanelProudZ = 0.004f;

    /// <summary>
    /// Decide WHO OWNS the hint that is on screen right now — a floated window/menu, or the
    /// CONTROL BOARD — and hold that answer for as long as the box is visible.
    ///
    /// <para>THE RULE (user report 2026-08-03, verbatim): "Sind die overlay-hints von einer Karte
    /// die auf dem Controllboard liegen, sollen sie auch am Controllboard angezeigt werden … Sind
    /// sie Teil eines Windows oder Menus, dann sollen sie auch innerhalb des Window/Menus angezeigt
    /// werden … Auch Hints die explizit mit Buttons (z.B. Decision-Buttons) zu tun haben die am
    /// Controllboard fixiert sind, sollen am Controllboard angezeigt werden." Ownership, not
    /// topmost-ness, and not a name match.</para>
    ///
    /// <para>THE SIGNAL IS STRUCTURAL AND ALREADY IN THE GAME'S DATA. <c>UITooltipTarget</c> anchors
    /// the shared box to the RectTransform the pointer entered
    /// (<c>UITooltip.AnchorToRect(base.transform, corner)</c>, decompiled UITooltipTarget.cs:139),
    /// and keeps it until its fade has fully finished (<c>InternalOnHide</c> nulls
    /// <c>m_AnchorToTarget</c>, UITooltip.cs:611). Converted windows OWN their content by
    /// re-parenting: <c>CanvasConversion.Convert</c> puts the game rect under the mod host
    /// (CanvasConversion.1.Core.cs:221). So the owner is whatever floated host is an ANCESTOR of
    /// that anchor rect — <see cref="ModalFallback.FindOwningWindow"/> — with three consequences
    /// that are exactly the three cases the user listed:</para>
    ///
    /// <list type="bullet">
    /// <item><description>An ability card lying in a board slot is game UI that was never
    /// converted (the VR card is a mesh; <c>Cards.CardFaceRaycaster</c> synthesizes the pointer
    /// events onto the game's own 2D card, which stays on its own canvas) → no floated ancestor →
    /// the BOARD anchor.</description></item>
    /// <item><description>A row of a floated window or menu — the item-unlock window, the pause /
    /// Options family, the mod's own VR options tab inside it — sits under that window's host →
    /// laid flat ON that window.</description></item>
    /// <item><description>A DECISION BUTTON docked under the board is converted too, but by
    /// <c>Surfaces.DecisionDockSurface</c>, which is NOT a floated modal (ModalFallback skips every
    /// window the dock claims, see <c>DecisionDock.ClaimsWindow</c>) → no floated ancestor → the
    /// BOARD anchor, next to the buttons the player is reading.</description></item>
    /// </list>
    ///
    /// <para>A NULL ANCHOR IS A REAL ANSWER, not a failure: the map's hex tooltips deliberately
    /// anchor to null so the box follows the cursor (<c>TileBehaviour.cs:66</c>
    /// <c>UITooltip.AnchorToRect(null, …)</c>). Those belong to the world, not to a window, and
    /// null resolves to the board anchor — the pre-regression behaviour.</para>
    ///
    /// <para>WHY THE ANSWER IS LATCHED THROUGH THE GRACE WINDOW. The placement latch
    /// (<see cref="HoverGraceSeconds"/>) deliberately keeps the box parked for a moment after the
    /// content stops showing, and the game clears <c>m_AnchorToTarget</c> inside exactly that
    /// window (at the end of its fade). Re-deriving there would read null and yank a fading WINDOW
    /// hint back onto the board for its last frames — a visible jump at the worst moment. So the
    /// decision is only re-taken while content is genuinely shown, and it is dropped when the
    /// grace expires.</para>
    /// </summary>
    private void ResolveHostOwner(bool contentShown, bool withinGrace)
    {
        if (!contentShown)
        {
            if (!withinGrace)
                _hostWindow = null; // hint gone for good — next hover decides afresh
            return;
        }

        Transform? hovered = _tooltip != null ? _tooltip.m_AnchorToTarget : null;
        ConvertedPanel? owner = ModalFallback.FindOwningWindow(hovered);
        _hostWindow = owner;

        // Change-gated (reference compare, so the steady state allocates nothing): one line per
        // genuine change of host, naming the hovered object — that is the whole evidence chain a
        // hardware log needs to settle "why did this hint go there".
        if (_hostLoggedOnce && ReferenceEquals(_hostLoggedWindow, owner))
            return;
        _hostLoggedOnce = true;
        _hostLoggedWindow = owner;
        string hoveredName = hovered != null ? hovered.name : "<mouse-follow (no anchor rect)>";
        if (owner != null)
        {
            VRLog.Info("WorldUI",
                $"Hover hint OWNER = floated window '{(owner.HostGo != null ? owner.HostGo.name : "?")}' " +
                $"(hovered '{hoveredName}' sits inside its converted host) — the hint is laid FLAT ON " +
                "that window, in its plane and at its scale.");
        }
        else
        {
            VRLog.Info("WorldUI",
                $"Hover hint OWNER = the CONTROL BOARD (hovered '{hoveredName}' is under no floated " +
                "window host — a card in a slot, a board-docked button, or a cursor-anchored world " +
                "tooltip) — the hint parks above the board's top edge at the board's own angle, " +
                $"plus [Cards] HoverHintOffset_{{board}} (live).");
        }
    }

    public void LateTick()
    {
        // Menu2D keeps the vanilla 2D tooltip path (UICamera → FlatScreen RT); every
        // scenario mode (incl. ModalUI/BoardTargeting — Recompute() only leaves
        // Menu2D while a scenario runs) gets the world-space presentation.
        bool modeWantsTooltip = WorldUIConfig.Tooltips.Value && WorldUIConfig.ConversionActive
                                && VRModeStateMachine.CurrentMode != VRMode.Menu2D
                                && !MenuOnFlatScreen();
        // User #7c: the in-VR settings toggle gates the whole world-space presentation.
        // Read live so a flip takes effect without a restart; the 2D menu tooltip (Menu2D
        // path above) is never touched by this gate.
        bool hintsEnabled = WorldUIConfig.ActionElementHints.Value;
        bool want = modeWantsTooltip && hintsEnabled;

        if (!want)
        {
            // Diagnostic (user #7c): note the one case where the USER'S hints toggle is
            // what holds the presentation off (would otherwise be showing), once per flip.
            if (modeWantsTooltip && !hintsEnabled && !_gateOffLogged)
            {
                _gateOffLogged = true;
                VRLog.Info("WorldUI",
                    "Action element hints disabled ([WorldUI] ActionElementHints=false) — " +
                    "tooltip never flipped to world space / never shown.");
            }
            Restore();
            return;
        }
        _gateOffLogged = false;

        if (_canvas == null)
        {
            // [Optimize] TooltipScanGate (2026-07 perf pass): this full-scene FindObjectOfType had
            // NO interval gate, so on any scene where the CanvasManager is absent or not yet built
            // it re-scanned the whole scene EVERY FRAME, forever. Retrying a handful of times a
            // second finds it just as fast in human terms (the canvas is needed for a hover, not
            // for a frame) and costs ~1/10 of the scans.
            if (PerfConfig.TooltipGateOn && Time.frameCount < _canvasSearchNextFrame)
                return;
            _canvasSearchNextFrame = Time.frameCount + CanvasSearchIntervalFrames;
            var manager = Object.FindObjectOfType<CanvasManager>();
            _canvas = manager != null ? manager.tooltipCanvas : null;
            if (_canvas == null)
                return;
        }

        // Scale WITH THE BOARD (user #7a): the control board's live lossy scale (diorama ×
        // tray-grab resize) when it exists, else the diorama scale (menu / no-tray fallback).
        float scale = ResolveWorldScale();
        // USER SIZE DIAL ("Infotafel-Größe", settings tab "Tafeln"): one factor for every mouseover info panel.
        // The prop/text hover cards apply it as their ABSOLUTE world factor (its default IS their
        // old hard-coded 0.6), so here it is NORMALIZED against that same default — at the factory
        // value the term is exactly 1 and this canvas keeps its established metres-per-pixel
        // (CanvasScaleMm × 0.001 × board scale × 0.5), i.e. nothing changes until the user tunes
        // the dial, and one stepper then scales BOTH families of hover panels by the same ratio.
        // Read every tick (like CanvasScaleMm above) so a stepper nudge resizes the SHOWN tooltip
        // immediately; the anchor math measures the tooltip's own world corners afterwards, so the
        // "clearly above the board top edge" placement re-derives itself from the NEW size for free.
        float sizeDial = WorldUIConfig.HoverInfoScaleLive() / WorldUIConfig.DefaultHoverInfoScale;
        Vector3 worldScale =
            Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * scale * 0.5f * sizeDial);

        if (!_converted)
        {
            _originalMode = _canvas.renderMode;
            _originalCamera = _canvas.worldCamera;
            _originalPlaneDistance = _canvas.planeDistance;
            _originalScale = _canvas.transform.localScale;
            _originalSortingOrder = _canvas.sortingOrder;
            _originalOverrideSorting = _canvas.overrideSorting;
            _canvas.renderMode = RenderMode.WorldSpace;
            // Scale + park IMMEDIATELY: an unanchored flip must never leave the
            // canvas rect at pixel size in world meters (test-#5 giant tooltip).
            _canvas.transform.localScale = worldScale;
            _canvas.transform.position = ParkPosition;
            _converted = true;
            CanvasConversion.AddMaskRequest();
            VRLog.Info("WorldUI", "Tooltip canvas flipped to world space (scenario mode).");
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        if (_canvas.worldCamera != head)
            _canvas.worldCamera = head;

        // Resolve the persistent tooltip content (singleton under the canvas) for the
        // flatten pass, frame clip, the visibility gate AND the ownership test below.
        if (_tooltip == null)
            _tooltip = _canvas.GetComponentInChildren<UITooltip>(includeInactive: true);

        // FIXED PLACEMENT (user #7a): while a tooltip is shown — OR within the placement
        // grace window just after it stopped (user #7b) — park the canvas at the anchor its
        // OWNER dictates. Otherwise leave it out of view — never at the fingertip.
        bool contentShown = _tooltip != null && _tooltip.IsActive() && _tooltip.alpha > ShownAlphaEpsilon;
        if (contentShown)
            _lastShownTime = Time.unscaledTime;
        bool withinGrace = _tooltip != null && Time.unscaledTime - _lastShownTime <= HoverGraceSeconds;
        bool visible = contentShown || withinGrace;

        // WHO OWNS THIS HINT decides where it goes — resolved BEFORE the pose/scale, because the
        // answer picks between two different pixels-to-metres factors (see ResolveHostOwner).
        ResolveHostOwner(contentShown, withinGrace);

        // ON THE MENU PANEL the scale is the PANEL's, not the board's: the two canvases only
        // coincide — and the game's own placement only lands where it means to — if they share a
        // pixels-to-metres factor. Off the menu, the board-derived scale above stands.
        bool onMenuPanel = TryResolveMenuPose(_hostWindow, out Vector3 menuPos, out Quaternion menuRot,
                                              out Vector3 menuScale, out int menuSorting);
        if (onMenuPanel)
        {
            worldScale = menuScale;
            // Draw IN FRONT of the panel it is laid on (see TryResolveMenuPose) — the 4 mm of
            // geometric clearance alone loses to the host's sortingOrder.
            if (_canvas.sortingOrder != menuSorting)
                _canvas.sortingOrder = menuSorting;
        }
        else if (_canvas.sortingOrder != _originalSortingOrder)
        {
            // Off the menu the authored order is right again (the board anchor is nowhere near
            // a floated panel), so hand it straight back instead of leaving the lift latched.
            _canvas.sortingOrder = _originalSortingOrder;
        }

        // Re-assert the scale every frame (config/diorama scale are live; the game
        // may rewrite the transform) — independent of placement.
        if (_canvas.transform.localScale != worldScale)
            _canvas.transform.localScale = worldScale;

        // FLATTEN + CLIP (part A): kill the baked local-z / rotation that renders as 3D
        // depth on a world-space host, and clip 2D overflow inside the frame. Both are
        // undone on Restore().
        //
        // [Optimize] TooltipScanGate (2026-07 perf pass): FlattenSubtree walks the WHOLE tooltip
        // canvas subtree (GetComponentsInChildren, includeInactive) and used to run on every frame
        // of every scenario — including the ~99 % of frames where no tooltip is shown at all and
        // the canvas is parked out of view. The visibility test that already exists two lines down
        // now gates it, so the walk happens exactly while the flattening can matter. Nothing the
        // player sees changes: the canvas is at ParkPosition whenever this gate is false, and the
        // flatten is re-asserted every frame it IS true.
        if (!PerfConfig.TooltipGateOn || visible)
        {
            FlattenSubtree();
            // RAY-TRANSPARENT WHILE WORLD-SPACE (see _raycastCleared): re-run on every
            // visible frame because the game pools/rebuilds the line objects per hover -
            // a fresh icon graphic must lose its raycastTarget the frame it appears.
            NeutralizeRaycasts();
        }
        EnsureFrameClip();
        // HOVER GRACE (user #7b): widen the game's own show/hide fade so a jitter off a
        // tiny target is bridged by its native tween. Undone on Restore().
        EnsureFadeGrace();

        // The window wins the placement only when it OWNS the hover (ResolveHostOwner); otherwise
        // the board anchor answers, which is what a card lying in a slot and a docked decision
        // button need even while some unrelated window floats.
        bool placed = onMenuPanel;
        Vector3 pos = menuPos;
        Quaternion rot = menuRot;
        if (!placed)
            placed = TryResolveTooltipPose(out pos, out rot);

        if (!(contentShown || withinGrace) || !placed)
        {
            if (_canvas.transform.position != ParkPosition)
            {
                _canvas.transform.position = ParkPosition;
                _parkedLogged = false; // re-arm the park diagnostic for the next appearance
            }
            return;
        }

        _canvas.transform.SetPositionAndRotation(pos, rot);

        // Diagnostic (user #7a): one line when the hint parks at the resolved board anchor
        // (deduped; re-logs if the anchor drifts > ~2 cm, e.g. the board was grabbed/moved).
        if (!_parkedLogged || (pos - _parkedLogPos).sqrMagnitude > 0.0004f)
        {
            _parkedLogged = true;
            _parkedLogPos = pos;
            if (onMenuPanel)
            {
                // Names the OWNING window (_hostWindow), not the topmost float — naming
                // MenuPanel here used to print a window the hint was never laid on.
                VRLog.Info("WorldUI",
                    $"Tooltip laid FLAT ON its OWNING floated window "
                    + $"'{(_hostWindow?.HostGo != null ? _hostWindow!.HostGo.name : "?")}' "
                    + $"at {pos:F3} (panel scale {menuScale.x:F5} m/px, sortingOrder {menuSorting} — "
                    + "in front of the host). The two canvases share the window's screen rect, so "
                    + "the game's own placement puts the box beside the hovered row, on the panel.");
                return;
            }

            bool onBoard = PlayTray.Current != null && PlayTray.Current.IsVisible;
            // Prove the panel sits ABOVE the board: the pivot is placed above the REAL board
            // top edge by (tooltip half-height + margin), so anchor.y − topEdge.y ≥ tooltip
            // half-height — the whole panel body clears the top edge. Logs the resolved top-
            // edge world Y, the tooltip half-height, and the final anchor for the hardware log.
            VRLog.Info("WorldUI",
                $"Tooltip parked {(onBoard ? "fixed ABOVE control-board top edge" : "at fallback slot")} " +
                $"anchor {pos:F3} (board scale {scale:F3}, board top-edge world Y {_lastBoardTopEdgeWorldY:F3} m, " +
                $"tooltip half {_lastTooltipHalfWorld.x:F3}×{_lastTooltipHalfWorld.y:F3} m → " +
                $"clearance above edge {(pos.y - _lastBoardTopEdgeWorldY):F3} m" +
                // The user dial only takes part in the BOARD anchor; the no-board fallback slot is
                // a fixed table pose, so naming an offset there would be a lie in the log.
                (onBoard
                    ? $", user offset [Cards] HoverHintOffset_" +
                      $"{(CardsConfig.Board != null ? CardsConfig.CurrentBoard.ToString() : "?")} " +
                      $"{BoardHintOffset()} board-local m"
                    : "") + ").");
        }
    }

    /// <summary>
    /// Resolve the world pose the tooltip parks at (user #7a), recomputed EVERY tick so it
    /// RE-ALIGNS the instant the board is moved/resized/tilted: clearly ABOVE the CONTROL
    /// BOARD's (PlayTray) TOP edge, along the board's own UP axis and horizontally CENTERED on
    /// the board, proud toward the viewer, facing the player exactly like the board's docked
    /// panels (their <c>mount.rotation == Root.rotation</c> faces the player).
    ///
    /// The top edge is the board's REAL rendered top, derived from the tray renderers' combined
    /// bounds (see <see cref="GetBoardTopEdgeWorld"/>) — NOT the authored plate constants:
    /// the visible board (bundled frame + decorations) is larger than the plate, so a
    /// constant-based half-height under-estimated the edge and the panel still sat inside the
    /// board (the "still inside" report). The pivot is then lifted above that edge by
    /// (tooltip half-height + margin), so the whole panel body clears the edge at ANY board
    /// scale and tilt (the offset is along the board up axis, so a tilt carries the panel with
    /// it). Falls back to the pre-existing <see cref="PanelSlot.Tooltip"/> table slot when no
    /// board is present (menu / Cards module off).
    /// </summary>
    private bool TryResolveTooltipPose(out Vector3 position, out Quaternion rotation)
    {
        if (TryGetBoardRoot(out Transform root))
        {
            // Board basis (unit vectors; Transform.* ignore scale) and live lossy scale.
            Quaternion boardRot = root.rotation;
            Vector3 up = boardRot * Vector3.up;          // board +Y  (top / far edge)
            Vector3 forward = boardRot * Vector3.forward;// board +Z  (away from viewer)

            Vector3 lossy = root.lossyScale;
            float scaleY = Mathf.Max(Mathf.Abs(lossy.y), 0.0001f);
            float scaleZ = Mathf.Max(Mathf.Abs(lossy.z), 0.0001f);

            // REAL board top edge in world space (horizontally centred over the board), from the
            // tray's combined renderer bounds — captures the visible frame/decorations, not just
            // the authored plate. Degrades to the BoardTopLocalY constant when no renderers.
            Vector3 topEdgeWorld = GetBoardTopEdgeWorld(root);

            // The tooltip's OWN rendered half-extents (world). Scales with the canvas world scale
            // we set this tick, so it tracks the board resize for free — the panel's whole body
            // must clear the top edge, so we lift by its half-HEIGHT.
            GetTooltipHalfExtents(out float ttHalfW, out float ttHalfH);

            // Clearance above the edge + proud toward the viewer, both proportional to board scale.
            float margin = BoardAnchorMarginY * scaleY;
            float proud = BoardAnchorProudZ * scaleZ;

            // Fixed ABOVE the top edge: pivot = topEdge + up·(tooltipHalfHeight + margin), so the
            // panel's bottom edge sits (margin) above the board's top edge. Proud toward viewer.
            //
            // Plus the USER'S OWN per-board offset ([Cards] HoverHintOffset_<board>, user request
            // 2026-08-03 "Position einstellbar im Debug-Menue"), read LIVE here — the whole pose is
            // recomputed every LateUpdate, so turning the dial moves a hint that is already open.
            // Same convention as every other per-board offset: X/Y in the board plane, Z proud
            // toward the player (negative = prouder), expressed in board-local metres and scaled by
            // the board's live lossy scale so a grab-resize does not change how far it sits.
            Vector3 tune = BoardHintOffset();
            Vector3 tuneWorld = boardRot * new Vector3(
                tune.x * Mathf.Max(Mathf.Abs(lossy.x), 0.0001f),
                tune.y * scaleY,
                tune.z * scaleZ);

            position = topEdgeWorld + up * (ttHalfH + margin) - forward * proud + tuneWorld;
            rotation = boardRot; // docked-panel facing (faces the player)

            _lastBoardTopEdgeWorldY = topEdgeWorld.y;
            _lastTooltipHalfWorld = new Vector2(ttHalfW, ttHalfH);
            return true;
        }

        // Safe fallback: the original curved-table slot (top-right, above initiative).
        _lastBoardTopEdgeWorldY = 0f;
        _lastTooltipHalfWorld = Vector2.zero;
        return PanelLayout.TryGetPose(PanelSlot.Tooltip, out position, out rotation);
    }

    /// <summary>
    /// The user's per-board hover-hint offset in BOARD-LOCAL metres
    /// (<c>[Cards] HoverHintOffset_&lt;board&gt;</c>), or zero while the Cards config is not bound
    /// (the board anchor is only reachable with a live tray, but this must never throw on a
    /// half-initialised session — an NRE in a LateUpdate step costs the whole step, see the
    /// WorldUI TickGuard). Read on every resolve, never cached: that is what makes the debug-menu
    /// dial move an OPEN hint.
    /// </summary>
    private static Vector3 BoardHintOffset()
    {
        if (CardsConfig.Board == null)
            return Vector3.zero; // Cards config never bound (module off) — no per-board dial exists
        BepInEx.Configuration.ConfigEntry<Vector3>? entry =
            CardsConfig.HoverHintOffset(CardsConfig.CurrentBoard);
        return entry != null ? entry.Value : Vector3.zero;
    }

    /// <summary>
    /// The CONTROL BOARD's REAL top edge in WORLD space, horizontally centred on the board.
    /// Delegates to <see cref="PlayTray.BoardTopEdgeWorld"/>, which derives the edge from the
    /// tray's combined MESH-RENDERER bounds expressed in the board's own LOCAL space (tilt-tight)
    /// because the visible board (bundled frame + decorations) sits PAST the authored plate
    /// (<see cref="PlayTray.BoardTopLocalY"/>) — a constant-based half-height under-estimated the
    /// edge and the panel still sat inside the board (the "still inside" report). The measurement
    /// now lives on <see cref="PlayTray"/> so the enemy-reveal spawn clearance
    /// (<c>Surfaces.EnemyRevealSurface</c>) clears the SAME measured board, with no second copy of
    /// this math that could drift out of sync.
    /// </summary>
    private static Vector3 GetBoardTopEdgeWorld(Transform root) => PlayTray.BoardTopEdgeWorld(root);

    /// <summary>
    /// Measure the tooltip frame's rendered size in WORLD metres (half-width / half-height).
    /// Uses the frame's world corners, which carry the canvas world scale we set this tick, so
    /// the measurement tracks a board resize. Returns zeros when there is no frame yet (the
    /// anchor then lifts only by the margin above the top edge — still above the board).
    /// </summary>
    private void GetTooltipHalfExtents(out float halfWidth, out float halfHeight)
    {
        halfWidth = 0f;
        halfHeight = 0f;
        if (_tooltip == null || _tooltip.transform is not RectTransform frame)
            return;
        frame.GetWorldCorners(CornerScratch); // 0 = bottom-left, 1 = top-left, 3 = bottom-right
        halfWidth = Vector3.Distance(CornerScratch[0], CornerScratch[3]) * 0.5f;
        halfHeight = Vector3.Distance(CornerScratch[0], CornerScratch[1]) * 0.5f;
    }

    /// <summary>The live, visible control board root, or false (menu / no tray).</summary>
    private static bool TryGetBoardRoot(out Transform root)
    {
        root = null!;
        PlayTray? tray = PlayTray.Current;
        if (tray == null || !tray.IsVisible)
            return false;
        Transform? r = tray.Root;
        if (r == null)
            return false;
        root = r;
        return true;
    }

    /// <summary>World scale for the tooltip canvas: the board's live lossy scale when the
    /// board exists (so the hint scales with it — user #7a), else the diorama scale.</summary>
    private static float ResolveWorldScale() =>
        TryGetBoardRoot(out Transform root) ? Mathf.Max(root.lossyScale.x, 0.01f) : PanelLayout.WorldScale;

    /// <summary>
    /// Widen the game's OWN tooltip fade so brief hover jitter is bridged (user #7b). We
    /// switch it to <see cref="UITooltip.Transition.Fade"/> at <see cref="FadeGraceSeconds"/>
    /// and store the originals — the game only ever sets these from the prefab at load, so a
    /// one-shot override holds. Fully reverted in <see cref="Restore"/>. We deliberately do
    /// NOT touch the tooltip's alpha / CanvasGroup: the game reads its own alpha back to
    /// drive its visual-state machine and clean up its content, so forcing it would strand
    /// the shared singleton — letting its native fade run long is safe and self-cleaning.
    /// </summary>
    private void EnsureFadeGrace()
    {
        if (_tooltip == null || _transitionOverridden)
            return;
        _originalTransition = _tooltip.transition;
        _originalTransitionDuration = _tooltip.transitionDuration;
        _tooltip.transition = UITooltip.Transition.Fade;
        _tooltip.transitionDuration = FadeGraceSeconds;
        _transitionOverridden = true;
    }

    /// <summary>
    /// Neutralize REAL 3D inside the tooltip subtree (part A): every descendant carrying a
    /// non-identity local rotation or non-zero local z is recorded once and clamped
    /// (rotation → identity, z → 0). X/Y are never touched (the game's fade/slide
    /// animations keep playing flat). Re-run every frame — tooltip lines are pooled and
    /// rebuilt per hover and the game rewrites them — with destroyed entries pruned so the
    /// record list stays bounded to the live subtree. Mirrors
    /// <see cref="CanvasConversion.FlattenSubtree"/> for this shared, un-converted canvas.
    /// </summary>
    private void FlattenSubtree()
    {
        if (_canvas == null)
            return;

        // Prune destroyed entries (pooled tooltip lines come and go per hover).
        for (int i = _flattened.Count - 1; i >= 0; i--)
        {
            if (_flattened[i].Transform == null)
                _flattened.RemoveAt(i);
        }

        RectScratch.Clear();
        _canvas.GetComponentsInChildren(includeInactive: true, RectScratch);
        Transform canvasTf = _canvas.transform;
        for (int i = 0; i < RectScratch.Count; i++)
        {
            RectTransform rect = RectScratch[i];
            // The canvas root's own pose is ours (scale + placement above) — flatten only
            // the content below it.
            if (rect == null || ReferenceEquals(rect, canvasTf))
                continue;

            Vector3 lp = rect.localPosition;
            Quaternion lr = rect.localRotation;
            bool tiltedRot = Quaternion.Angle(lr, Quaternion.identity) > FlattenAngleEpsilon;
            bool tiltedZ = Mathf.Abs(lp.z) > FlattenZEpsilon;
            if (!tiltedRot && !tiltedZ)
                continue;

            if (!IsFlattenRecorded(rect))
            {
                _flattened.Add(new FlattenEntry
                {
                    Transform = rect,
                    OriginalLocalZ = lp.z,
                    OriginalLocalRotation = lr,
                });
            }
            if (tiltedRot)
                rect.localRotation = Quaternion.identity;
            if (tiltedZ)
                rect.localPosition = new Vector3(lp.x, lp.y, 0f);
        }
        RectScratch.Clear();
    }

    private bool IsFlattenRecorded(Transform rect)
    {
        for (int i = 0; i < _flattened.Count; i++)
        {
            if (ReferenceEquals(_flattened[i].Transform, rect))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Clear <c>raycastTarget</c> on every graphic under the tooltip canvas that still
    /// has it set, recording each flip for <see cref="Restore"/> (see the
    /// <see cref="_raycastCleared"/> doc for why the tooltip must be ray-transparent
    /// while world-space). Only true->false flips are recorded, so graphics the game
    /// already spawns ray-transparent (the line texts) are never touched or restored.
    /// Destroyed entries are pruned so the record list stays bounded to the live
    /// pooled subtree - the same discipline as the flatten record above.
    /// </summary>
    private void NeutralizeRaycasts()
    {
        if (_canvas == null)
            return;

        for (int i = _raycastCleared.Count - 1; i >= 0; i--)
        {
            if (_raycastCleared[i] == null)
                _raycastCleared.RemoveAt(i);
        }

        GraphicScratch.Clear();
        _canvas.GetComponentsInChildren(includeInactive: true, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g == null || !g.raycastTarget)
                continue;
            g.raycastTarget = false;
            _raycastCleared.Add(g);
        }
        GraphicScratch.Clear();
    }

    /// <summary>
    /// Add a <see cref="RectMask2D"/> on the tooltip frame so text is clipped inside the
    /// panel (part A). Idempotent; if the frame already carries one we leave it alone and
    /// never destroy it on Restore.
    /// </summary>
    private void EnsureFrameClip()
    {
        if (_tooltip == null || _addedMask != null)
            return;
        var frame = _tooltip.transform as RectTransform;
        if (frame == null)
            return;
        RectMask2D existing = frame.GetComponent<RectMask2D>();
        if (existing != null)
            return; // game already clips this frame — don't touch/destroy it
        _addedMask = frame.gameObject.AddComponent<RectMask2D>();
    }

    private void Restore()
    {
        if (!_converted)
            return;
        _converted = false;
        CanvasConversion.RemoveMaskRequest();

        // Un-flatten: original local z + rotation back per live recorded transform.
        for (int i = 0; i < _flattened.Count; i++)
        {
            Transform tf = _flattened[i].Transform;
            if (tf == null)
                continue;
            Vector3 lp = tf.localPosition;
            tf.localPosition = new Vector3(lp.x, lp.y, _flattened[i].OriginalLocalZ);
            tf.localRotation = _flattened[i].OriginalLocalRotation;
        }
        _flattened.Clear();

        // Remove only a mask we added.
        if (_addedMask != null)
        {
            Object.Destroy(_addedMask);
            _addedMask = null;
        }

        // Hand back raycastTarget on every graphic we cleared (all were true when
        // recorded - see NeutralizeRaycasts), so the vanilla 2D menu tooltip keeps
        // whatever hit-testing the game authored.
        for (int i = 0; i < _raycastCleared.Count; i++)
        {
            Graphic g = _raycastCleared[i];
            if (g != null)
                g.raycastTarget = true;
        }
        _raycastCleared.Clear();

        // Revert the widened fade (user #7b) so the vanilla 2D menu tooltip keeps its
        // authored transition — before we drop the reference.
        if (_transitionOverridden && _tooltip != null)
        {
            _tooltip.transition = _originalTransition;
            _tooltip.transitionDuration = _originalTransitionDuration;
        }
        _transitionOverridden = false;

        _tooltip = null;
        _lastShownTime = float.NegativeInfinity;
        _parkedLogged = false;
        // Host ownership is per-hint state — a torn-down presentation must never hand the next
        // session a stale window reference (its panel is released by then), and the change-gated
        // diagnostic re-arms so the first decision after a restore is logged again.
        _hostWindow = null;
        _hostLoggedWindow = null;
        _hostLoggedOnce = false;

        if (_canvas != null)
        {
            _canvas.renderMode = _originalMode;
            _canvas.worldCamera = _originalCamera;
            _canvas.planeDistance = _originalPlaneDistance;
            _canvas.transform.localScale = _originalScale;
            _canvas.sortingOrder = _originalSortingOrder;
            _canvas.overrideSorting = _originalOverrideSorting;
            VRLog.Info("WorldUI", "Tooltip canvas restored to screen space (flatten + frame clip reverted).");
        }
    }

    public void Shutdown() => Restore();
}
