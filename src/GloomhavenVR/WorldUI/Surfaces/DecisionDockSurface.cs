using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using Script.GUI.Popups;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// DECISION DOCK (test #22, generalizes the test-#21 take-damage dock): every
/// in-scenario decision/confirmation the game shows as a flat modal window — the
/// take-damage burn choice (<c>TakeDamagePanel</c>), the burn-confirm dialog
/// (<c>UIManager.dialogPopup</c>: "burn this card / choose another"), and any prompt
/// added later to the <see cref="ModalFallback.DecisionDock"/> registry — is invisible
/// in VR and would deadlock the game waiting for a click. Pre-#22 the generic
/// <see cref="ModalFallback"/> floated the WHOLE 1920×1080 window in front of the HMD
/// and asserted ModalUI, which disabled the card fan's palm gate — even though these
/// prompts have follow-ups that NEED the fan (the burn choice continues in a LoseCard
/// fan pick; test #21 log: <c>fan state: mode=LoseCard … gateEnabled=False
/// (vrMode=ModalUI)</c>). Test #21 solved that for TakeDamagePanel but docked the row
/// CENTERED OVER the two parked cards (the test-#22 complaint that the buttons "fly
/// over the cards").
///
/// This single surface docks ONLY the interactive widget row of whichever prompt is
/// open — the REAL game buttons/toggles, so labels/localization and the native
/// interactable/dim states ride along — onto <see cref="PlayTray.DecisionMount"/>, the
/// reserved zone BELOW the two cards (see the collision math in PlayTray.BuildMounts).
/// The row is found STRUCTURALLY (never by name) per prompt — the deepest common
/// ancestor of the actionable widgets that is a strict descendant of the window root
/// (see <see cref="ModalFallback.DecisionDock.IsolateRow"/>) — so it survives hierarchy
/// renames and drags nothing but the buttons. Poke AND laser click it through the
/// standard conversion registration (Convert's pokeable default → UguiPokeSurfaces +
/// RayUguiDriver), exactly like the floated modal the clicks already worked on.
///
/// The REST of the window — vignette, backdrop, the flat window frame AND the embedded
/// card (whose real rendering + burn VFX is a PARALLEL worker's job, not this one's) —
/// is VISUALLY suppressed the way the 2D hand is
/// (<see cref="Cards.Patches.HandSuppression"/>): the window root's CanvasGroup is
/// forced alpha 0 / blocksRaycasts false every tick (the game rewrites alpha on
/// Show/ToggleVisibility), and nested <see cref="Canvas"/> components under the root
/// (which render INDEPENDENTLY of the root CanvasGroup alpha) are component-disabled,
/// stopping their whole subtree rendering (the CanvasConversion nested-canvas lesson,
/// tests #19/#20). Nothing is destroyed; everything is restored on undock.
///
/// MODE MACHINE (task 3): no ModalUI is asserted for a docked decision —
/// <see cref="ModalFallback"/> stands its generic path down for any window
/// <see cref="ModalFallback.DecisionDock.ClaimsWindow"/> claims (both the ID-tracked
/// TakeDamagePanel AND the poll-tracked dialogPopup), so the flow mode
/// (HalfSelection/LoseCard/ChooseCards) and the fan's palm gate stay live through the
/// whole prompt, and the game itself never locks the UI here. The burn-confirm dialog
/// FOLLOWS a LoseCard pick, so the fan MUST stay live.
///
/// TIMING (task 4): docking is LEVEL-TRIGGERED on the game's own window open state
/// (<see cref="ModalFallback.DecisionDock.ActivePrompt"/> → the prompt's IsOpen), never
/// on a hover preview — nothing about hovering a choice pre-transforms the board, and
/// the row now lives BELOW the cards, so there is no over-slot overlay to mistake for a
/// hover change. The dock appears only while a prompt is ACTUALLY OPEN and vanishes the
/// moment it closes.
///
/// LIFECYCLE (task 5): any close path (any choice, game-side close, scene death)
/// releases the conversion — which restores the row to its exact 2D home
/// (CanvasConversion restore records) and lifts the suppression — and dock/undock are
/// logged once per flip. Decisions are sequential (the burn-confirm opens only after
/// the take-damage panel handed off), so this surface docks ONE prompt at a time.
///
/// SAFETY NET (task 5): while a claim holds the generic fallback stands down — but if
/// the row cannot be isolated (widgets missing/not yet pooled, common ancestor
/// degenerates to the window root, Convert failure) the claim is RELEASED after a short
/// grace (<see cref="ClaimGraceSeconds"/>) via
/// <see cref="ModalFallback.DecisionDock.MarkGaveUp"/> and ModalFallback floats the
/// whole window generically next tick (window + ModalUI): a worse experience, never a
/// deadlock. The manual A/X screen chord stays the universal rescue (it releases the
/// conversion so the full window shows on the 2D composite).
/// </summary>
internal sealed class DecisionDockSurface : WorldSurface
{
    /// <summary>Claim grace: how long a prompt may fail to isolate before the generic fallback takes over.</summary>
    private const float ClaimGraceSeconds = 1.5f;

    /// <summary>
    /// Density-scale guards, mirroring <see cref="TrayMountedPanelSurface"/>
    /// (test #16): max 1 keeps small content content-true, min 0.5 keeps a
    /// misfired-huge measurement readable (slight dock overflow over shrinking
    /// below readability).
    /// </summary>
    private const float MaxDensityScale = 1f;
    private const float MinDensityScale = 0.5f;

    /// <summary>
    /// Choice widgets are the one thing the player MUST read and hit under
    /// pressure — 0.8× the shared tray density renders them 1.25× bigger (the
    /// element-board rationale); the dock fit clamp still bounds the row to its
    /// mount budget.
    /// </summary>
    private const float DensityScale = 0.8f;

    /// <summary>No-tray fallback float distance (HMD-anchored, the ModalFallback pattern).</summary>
    private const float FloatDistanceMeters = 1.1f;

    /// <summary>Extra shrink on the no-tray float (the ModalFallback window factor).</summary>
    private const float FloatScaleFactor = 0.7f;

    /// <summary>Live instance for the static claim query (single instance per driver).</summary>
    internal static DecisionDockSurface? Instance { get; private set; }

    /// <summary>
    /// True while a decision prompt's widget row is ACTUALLY docked on the board.
    /// <see cref="UseBarsSurface"/> reads this to shift its bar stack below the decision
    /// row's zone — the two co-occur during take-damage and must never collide.
    /// </summary>
    internal static bool RowDocked => Instance != null && Instance.Panel != null;

    /// <summary>
    /// Live world-metre offset (along <c>DecisionMount.up</c>, relative to the mount
    /// position) of the docked decision row's visible BOTTOM edge — the lowest visible
    /// widget graphic of the active prompt, glyph/plate-true, produced by the SAME
    /// measurement walk the placement's top-edge anchor uses (so top and bottom can never
    /// disagree about what "the row" is). Null while no row is mount-docked or nothing has
    /// been measured yet. <see cref="UseBarsSurface"/> hangs its bar stack a small clearance
    /// below THIS instead of the mount's worst-case ±MaxHeight/2 extent — the paranoia
    /// spacing floated the armor-bonus bar FAR below the take-damage row (screenshot
    /// abstand.png), visually disconnecting one decision area into two. One-directional by
    /// construction, so no feedback loop: the bars only READ this value, while the row's own
    /// measurement walks exclusively its own converted subtree / the prompt's serialized
    /// widgets — separate host canvases the bar stack can never appear in.
    /// </summary>
    internal static float? RowBottomUpMeters { get; private set; }

    /// <summary>
    /// True while THIS surface has the <c>TakeDamagePanel</c>'s widget row docked on the
    /// board (task A). The take-damage widgets carry the game's mouse-hover preview
    /// handlers (<c>OnMouseEnter*/OnMouseExit*</c> → <c>Preview*/ResetPreviewing</c>),
    /// authored for a stable desktop cursor. Under a jittering VR laser they fire
    /// enter/exit dozens of times a second, thrashing the LoseCard preview fan
    /// (hand↔discard, log test #23) and making the burn pick unusable; the deliberate
    /// toggle CLICK already drives the preview, so <see cref="Patches.TakeDamagePanelSafety"/>
    /// stands the hover handlers down while this is set. Read cross-module (guarded by
    /// null-check on <see cref="Instance"/>); never true unless a live dock holds.
    /// </summary>
    internal static bool DockingTakeDamage { get; private set; }

    /// <summary>The prompt currently docked (or being docked); null while none is open.</summary>
    private ModalFallback.DecisionDock.Prompt? _active;

    /// <summary>The active prompt's window (open-state + suppression target).</summary>
    private UIWindow? _activeWindow;

    private float _wantSince;
    private bool _targetWarned;
    private bool _hmdFloatPlaced;

    // Suppression records (restored on undock; nothing destroyed).
    private UIWindow? _suppressedWindow;
    private CanvasGroup? _suppressedGroup;
    private readonly List<Canvas> _disabledCanvases = new(2);
    private static readonly List<Canvas> CanvasScratch = new(8);

    // ---- docked-row adjustments (user #5 antique tint; user #14 placement-driven gap) ---

    /// <summary>
    /// USER #14 — THE GAP FIX, ground-truth reasoned (see <see cref="Place"/>). The
    /// tray's brass grab bar
    /// (<c>PlayTray.BuildHandle</c>: root-local y −0.19, zone −0.215..−0.165) sits along
    /// the board's LOWER EDGE — exactly where the game draws the decision prompt text
    /// ("Schadensphase: Erleide entweder Schaden…"). That prompt is NOT a child of the
    /// isolated widget row (the row is the deepest common ancestor of the three serialized
    /// widgets — <see cref="ModalFallback.DecisionDock.IsolateRow"/> — and every hardware
    /// log reports "no prompt text above the widgets", so no in-row measurement can ever
    /// find or close the gap the user sees). So the gap is driven from a MOD-OWNED
    /// reference instead: the grab-bar bottom, minus this clearance, is the prompt anchor
    /// under which the interactive widget block is placed.
    /// </summary>
    private const float BarClearanceMeters = 0.008f;

    /// <summary>
    /// Fallback prompt-reference height above the <see cref="PlayTray.DecisionMount"/>
    /// (world m at diorama scale 1) used only when the tray exposes no grab-bar zone —
    /// the measured bar-zone-bottom distance (mount y −0.29, bar zone bottom −0.215 ≈
    /// 0.075 m above the mount) so the stepper keeps a sensible reference either way.
    /// </summary>
    private const float NoBarPromptRefUp = 0.075f;

    /// <summary>
    /// Antique multiply-tint for the docked row's widget backgrounds (user #5): the
    /// game-default light stone sprite sinks toward the dark wood / aged brass family
    /// of the mod's board buttons (the VR-settings gear look). Multiplied onto the
    /// authored Graphic colour, so per-widget differences survive; uGUI ColorTint
    /// transitions multiply on the CanvasRenderer ON TOP of this, so pressed/disabled
    /// dimming keeps working.
    /// </summary>
    private static readonly Color AntiqueTint = new(0.58f, 0.46f, 0.31f, 1f);

    private readonly List<(Graphic graphic, Color color)> _tintedGraphics = new(8);
    private Canvas? _deliberateCanvas;
    private bool _placementLogged;
    private float _lastLoggedGapPx = float.NaN;
    private static bool _takeDamageDumped;
    private static readonly List<Selectable> SelectableScratch = new(8);
    private static readonly List<TMP_Text> TextScratch = new(8);
    private static readonly List<Graphic> GraphicScratch = new(16);
    private static readonly List<RectTransform> WidgetRectScratch = new(4);
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    public DecisionDockSurface()
    {
        Instance = this;
        // User #11b: the gap target is live-tunable — re-apply the row adjustments in
        // place when it changes while a row is docked (AdjustDockedRow restores first,
        // so re-running is idempotent). Unsubscribed in Shutdown.
        if (WorldUIConfig.DecisionRowGapPx != null)
            WorldUIConfig.DecisionRowGapPx.SettingChanged += OnRowGapSettingChanged;
    }

    /// <summary>
    /// User #11b/#14: live re-apply of the docked-row gap when the config bind changes.
    /// The gap now drives the docked block's VERTICAL PLACEMENT (a quantity the mod owns
    /// outright — see <see cref="Place"/>), so re-run the placement immediately; the tint
    /// is gap-independent, so it does not need re-applying here.
    /// </summary>
    private void OnRowGapSettingChanged(object? sender, System.EventArgs e)
    {
        if (Panel != null)
            Place();
    }

    public override string Name => "DecisionDock";
    protected override bool ConfigEnabled => WorldUIConfig.DecisionDock.Value;

    /// <summary>
    /// Level-triggered on the game's own window state (the ModalFallback contract):
    /// converted while the active prompt is open + claimed, released the moment it
    /// closes, its claim is handed back after the grace, or the manual screen chord
    /// forces the full 2D composite (the window must be in the composite the screen
    /// mirrors). Equivalent to <see cref="ModalFallback.DecisionDock.ClaimsWindow"/>
    /// so the surface docks EXACTLY what the generic path stands down for.
    /// </summary>
    protected override bool WantConverted
    {
        get
        {
            if (!base.WantConverted || FlatScreen.ManualScreenActive)
                return false;
            return _activeWindow != null && ModalFallback.DecisionDock.ClaimsWindow(_activeWindow);
        }
    }

    /// <summary>
    /// The active prompt's interactive row, isolated structurally (see the class doc).
    /// Null (→ retry while the grace runs) when the widgets are missing/not yet pooled
    /// or the ancestor degenerates to the window root — docking the root would drag
    /// the vignette + card along, which is the pre-#22 behavior the generic fallback
    /// already provides.
    /// </summary>
    protected override RectTransform? FindTarget()
    {
        if (_active == null || _activeWindow == null)
            return null;
        RectTransform? row = _active.FindRow();
        if (row == null)
        {
            WarnTargetOnce(_active.Name);
            return null;
        }
        return row;
    }

    private void WarnTargetOnce(string prompt)
    {
        if (_targetWarned)
            return;
        _targetWarned = true;
        VRLog.Warn("WorldUI", $"DECISION DOCK: cannot isolate the widget row of '{prompt}' " +
                              "(widgets missing/not yet pooled, or the common ancestor is the window " +
                              $"root). Claim releases after {ClaimGraceSeconds:F1}s; the generic modal " +
                              "fallback (floating window + ModalUI) takes over.");
    }

    public override void Tick()
    {
        // Pick the currently-open, still-claimed prompt (decisions are sequential, so
        // at most one). ActivePrompt already skips a window the grace handed back.
        ModalFallback.DecisionDock.Prompt? active = ModalFallback.DecisionDock.ActivePrompt();
        UIWindow? window = active != null ? active.Window() : null;
        bool open = window != null && window.IsOpen;

        // Track the active prompt; a new/switched/closed prompt re-arms the latches.
        if (!ReferenceEquals(window, _activeWindow))
        {
            // The active prompt CHANGED (test #26: the short-rest Yes/No closed and the
            // burn/redraw DialogPopup opened in its place). WorldSurface only converts
            // while Panel == null, so a live conversion of the PREVIOUS prompt would
            // stick on the board forever and the new prompt would never dock — nor float,
            // since the claim keeps the generic path down. Tear the old conversion down
            // here (restore its suppression + release its row) so base.Tick re-converts
            // the new target THIS tick.
            if (Panel != null || _suppressedWindow != null)
            {
                UnregisterDeliberateCanvas();
                RestoreRowAdjustments();
                RestoreSuppression();
                RowBottomUpMeters = null; // stale row gone; the next prompt's Place re-publishes
                if (Panel != null && ReleaseCurrentPanel())
                    VRLog.Info("WorldUI", "DECISION DOCK: active prompt changed — previous row " +
                                          "released so the next prompt's row can dock in its place.");
            }
            _activeWindow = open ? window : null;
            _active = open ? active : null;
            _wantSince = 0f;
            _targetWarned = false;
        }
        else if (!open)
        {
            _activeWindow = null;
            _active = null;
        }

        // 2D-FLASH FIX (burn-confirm): alpha-0 the flat window the MOMENT the prompt is claimed
        // and open, before/independent of the row docking. The DialogPopup that confirms a card
        // burn re-parents the LIVE fullAbilityCard into itself and activates (DialogPopup.Show ->
        // gameObject.SetActive(true)); its CanvasGroup fades the whole popup (backdrop + full card)
        // in. Previously the window was suppressed only once the widget row had been isolated and
        // docked (Panel != null), so during the convert grace (>= 1 frame, up to ClaimGraceSeconds
        // if the row is not yet pooled) the raw popup rendered in the HMD as a brief 2D flash.
        // Doing the cheap CanvasGroup suppression up-front removes it; the nested-canvas (vignette)
        // suppression stays in the docked branch, where the row is already outside the window.
        if (_activeWindow != null)
            SuppressWindowGroup(_activeWindow);
        else if (_suppressedWindow != null)
            RestoreSuppression(); // claimed window closed without ever docking a row

        bool hadPanel = Panel != null;
        base.Tick(); // convert / release / Place (level-triggered on WantConverted)

        if (Panel != null)
        {
            _wantSince = 0f;
            if (!hadPanel)
            {
                VRLog.Info("WorldUI", $"DECISION DOCK: '{_active?.Name}' widget row docked below the cards " +
                                      "(window/vignette/card suppressed, fan gate stays live, no ModalUI) — " +
                                      "restored to 2D when the prompt closes.");
                DumpTakeDamageHierarchyOnce(); // user #14 STEP 1: one-time ground-truth hierarchy dump
                AdjustDockedRow();          // user #5: antique tint (gap is now placement-driven, see Place)
                RegisterDeliberateCanvas(); // user #13b: decision buttons take the deliberate v1 poke press
                _placementLogged = false;
                _lastLoggedGapPx = float.NaN;
            }
            ApplySuppression(_activeWindow!); // non-null: WantConverted required IsOpen
            // The one surface that must accept input even under the game's UI-lock
            // raycaster mirror — the ModalFallback floating-modal exemption.
            if (Panel.HostRaycaster != null && !Panel.HostRaycaster.enabled)
                Panel.HostRaycaster.enabled = true;
        }
        else
        {
            _hmdFloatPlaced = false;
            RowBottomUpMeters = null; // no docked row → the bar stack falls back to the zone top
            if (hadPanel)
            {
                UnregisterDeliberateCanvas();
                RestoreRowAdjustments();
                RestoreSuppression();
                VRLog.Info("WorldUI", "DECISION DOCK: widget row released — restored to its 2D home " +
                                      $"(open={open}), suppression lifted, row style/layout restored.");
            }
            // Claim grace: claimed but unconverted (row not isolatable / Convert
            // failed) → after the grace, hand the window to the generic fallback.
            if (_activeWindow != null && ModalFallback.DecisionDock.ClaimsWindow(_activeWindow))
            {
                if (_wantSince <= 0f)
                {
                    _wantSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _wantSince > ClaimGraceSeconds)
                {
                    ModalFallback.DecisionDock.MarkGaveUp(_activeWindow);
                    VRLog.Warn("WorldUI", $"DECISION DOCK: '{_active?.Name}' row not converted within " +
                                          $"{ClaimGraceSeconds:F1}s — claim released, the generic modal " +
                                          "fallback floats the whole window for this prompt.");
                }
            }
        }

        // Hover-thrash guard flag (task A): the take-damage widget row is docked and
        // driven by the VR laser/poke, so its mouse-hover preview handlers must stand
        // down (see the property doc). Only ever true while its row is actually docked.
        DockingTakeDamage = Panel != null && _active != null && _active.Name == "TakeDamagePanel";
    }

    /// <summary>
    /// Dock on the tray's <see cref="PlayTray.DecisionMount"/> (pose-follow, shared tray
    /// density — the <see cref="TrayMountedPanelSurface"/> math), horizontally CENTERED on
    /// the mount, VERTICALLY driven by <see cref="WorldUIConfig.DecisionRowGapPx"/>. While
    /// no usable mount exists (Cards module off, tray hidden/destroyed) the row floats
    /// HMD-anchored at reading distance instead (placed once, ModalFallback pattern) — a
    /// decision must never be invisible.
    ///
    /// USER #14 — PLACEMENT-DRIVEN GAP (the fix that cannot silently no-op). The mod owns
    /// where this block docks outright, so the gap stepper drives a MOD-OWNED quantity:
    /// the interactive-widget block's TOP is placed <see cref="WorldUIConfig.DecisionRowGapPx"/>
    /// pixels (in the row's own scale) BELOW the prompt reference — the tray grab-bar
    /// bottom along the board's lower edge, where the game draws the decision prompt text.
    /// Shrink the gap → the whole docked block rises toward the prompt; grow it → the block
    /// drops. Because the block top is measured from the VISIBLE widget graphics (glyph/plate
    /// bounds, not the authored option-column rects), the buttons themselves land at the
    /// chosen distance regardless of where the prompt text lives (H1: it is external to the
    /// row). The grab bar can never be overlapped: the widget-block top is always at least
    /// the clearance below the bar zone (gap ≥ 0). Every dock — and every live gap change —
    /// logs the resolved reference, the applied gap px and the chosen block Y.
    /// </summary>
    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = PlayTray.Current?.DecisionMount; // Unity-null aware
        if (mount == null || !mount.gameObject.activeInHierarchy)
        {
            if (!Panel.HostGo.activeSelf)
                Panel.HostGo.SetActive(true);
            if (!_hmdFloatPlaced)
                _hmdFloatPlaced = TryPlaceAtHmd();
            RowBottomUpMeters = null; // HMD-floated, not on the mount — the bar stack must not hang off it
            return;
        }
        _hmdFloatPlaced = false;

        if (!Panel.HostGo.activeSelf)
            Panel.HostGo.SetActive(true);

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return;

        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        float fitScale = Mathf.Min(
            PlayTray.DecisionMountWidth * density / rect.width,
            PlayTray.DecisionMountMaxHeight * density / rect.height);
        float scale = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale) / density * trayScale;

        Transform host = Panel.HostTransform;
        Vector3 up = mount.up;
        // Set the final orientation + scale BEFORE measuring: the widget-block-top and
        // row-top offsets below are taken relative to the host pivot (translation-
        // invariant), so the position can be solved afterward from those measurements.
        host.rotation = mount.rotation;
        host.localScale = Vector3.one * scale;

        // Prompt reference (world m above the mount along up): the grab-bar bottom minus
        // clearance — the board's lower edge under which the game draws the prompt text.
        string refNote;
        float promptRefUp;
        if (PlayTray.Current?.HandleZone is BoxCollider barZone)
        {
            Transform bt = barZone.transform;
            float barCenterUp = Vector3.Dot(bt.TransformPoint(barZone.center) - mount.position, up);
            float barHalfUp = 0.5f * barZone.size.y * Mathf.Abs(bt.lossyScale.y);
            promptRefUp = barCenterUp - barHalfUp - BarClearanceMeters * trayScale;
            refNote = "grab-bar bottom";
        }
        else
        {
            promptRefUp = NoBarPromptRefUp * trayScale;
            refNote = "no-bar board-edge estimate";
        }

        // Top/bottom-most VISIBLE widget graphics, world m above the host pivot (fall back
        // to the fitted row edges when a prompt has no resolvable widgets).
        WidgetBlockEdgesAbovePivot(host, up, rect.yMax * scale, rect.yMin * scale,
            out float blockTopAbovePivot, out float blockBottomAbovePivot);

        // Gap px → world m in the row's own scale; place the block top this far below the
        // prompt reference. gap ≥ 0 keeps the block clear of the grab bar by construction.
        float gapPx = Mathf.Max(0f, WorldUIConfig.DecisionRowGapPx.Value);
        float gapMeters = gapPx * scale;
        float targetBlockTopUp = promptRefUp - gapMeters;
        float d = targetBlockTopUp - blockTopAbovePivot; // shift along up from mount.position
        Vector3 pos = mount.position + up * d;

        host.position = pos;

        // Publish the row's MEASURED bottom edge (mount-relative, along up) for the
        // UseBarsSurface stack — the bars hang a small clearance below the row the player
        // actually SEES instead of the mount's worst-case extent (see RowBottomUpMeters doc).
        RowBottomUpMeters = d + blockBottomAbovePivot;

        if (!_placementLogged || float.IsNaN(_lastLoggedGapPx) || Mathf.Abs(gapPx - _lastLoggedGapPx) >= 0.5f)
        {
            _placementLogged = true;
            _lastLoggedGapPx = gapPx;
            VRLog.Info("WorldUI", $"DECISION DOCK: '{_active?.Name}' gap placement — [WorldUI] " +
                                  $"DecisionRowGapPx={gapPx:F0}px → widget block top {gapMeters * 1000f:F0} mm " +
                                  $"below the prompt reference ({refNote}, {promptRefUp * 1000f:F0} mm above " +
                                  $"the mount); block top set {(promptRefUp - gapMeters) * 1000f:F0} mm above " +
                                  $"the mount, host shifted {d * 1000f:F0} mm along the dock up-axis. The " +
                                  "stepper moves this mod-owned position, so it always changes the gap.");
        }
    }

    /// <summary>HMD-anchored fallback float (the ModalFallback.PlaceAtHmd pattern). True when placed.</summary>
    private bool TryPlaceAtHmd()
    {
        if (Panel == null)
            return false;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return false;
        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Vector3 pos = h.position + fwd * (FloatDistanceMeters * scale);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
        CanvasConversion.PlaceHost(Panel, pos, rot, scale * FloatScaleFactor);
        return true;
    }

    // ---- docked-row adjustments (users #5 + #7a) ----------------------------------------

    /// <summary>
    /// One-shot per dock. USER #5 — antique restyle only (the text↔widget gap is no longer
    /// an in-row layout shuffle; it is driven by the docked block's PLACEMENT, see
    /// <see cref="Place"/>). Every Selectable background in the docked row is multiply-
    /// tinted toward the mod's dark-wood/aged-brass board-button family and its labels turn
    /// parchment gold (<see cref="NativeButtonSkin.LabelColor"/>), so the docked native
    /// buttons ("Auswahl beenden", Ja/Nein, burn choices …) read like the VR-settings gear
    /// instead of the game's default look. Everything is recorded and handed back by
    /// <see cref="RestoreRowAdjustments"/> on undock — live game widgets are never
    /// permanently mutated.
    /// </summary>
    private void AdjustDockedRow()
    {
        RestoreRowAdjustments(); // never double-record
        RectTransform? root = Panel?.Target;
        if (root == null)
            return;

        SelectableScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, SelectableScratch);
        int styled = 0;
        for (int i = 0; i < SelectableScratch.Count; i++)
        {
            Selectable sel = SelectableScratch[i];
            if (sel == null)
                continue;
            Graphic? bg = sel.targetGraphic != null ? sel.targetGraphic : sel.image;
            if (bg != null)
            {
                _tintedGraphics.Add((bg, bg.color));
                bg.color = bg.color * AntiqueTint;
                styled++;
            }
            TextScratch.Clear();
            sel.GetComponentsInChildren(includeInactive: false, TextScratch);
            for (int t = 0; t < TextScratch.Count; t++)
            {
                TMP_Text label = TextScratch[t];
                if (label == null)
                    continue;
                _tintedGraphics.Add((label, label.color));
                Color gold = NativeButtonSkin.LabelColor;
                label.color = new Color(gold.r, gold.g, gold.b, label.color.a);
            }
        }

        VRLog.Info("WorldUI", $"DECISION DOCK: row adjusted — {styled} widget background(s) antique-tinted " +
                              "(dark-wood/brass + parchment labels, the VR-settings-button style); the " +
                              "text↔widget gap is now driven by DecisionRowGapPx placement (see Place).");
    }

    /// <summary>
    /// USER #14 STEP 1 — ONE-TIME GROUND TRUTH: on the first TakeDamage dock, dump BOTH the
    /// isolated docked row subtree (<c>Panel.Target</c>) AND the whole
    /// <c>TakeDamagePanel.Instance</c> window subtree to the log, indented by depth, with
    /// each node's active-in-hierarchy state, its TMP text (trimmed) + glyph-true Y bounds
    /// or its Image/Selectable rect Y bounds (each in ITS OWN subtree-root local space), and
    /// a marker for the three serialized widget fields. This resolves — from the next
    /// hardware log — whether the "Schadensphase…" prompt text lives inside the isolated row
    /// (then a measurement bug, H2) or is a sibling/banner OUTSIDE it (H1). Cheap, guarded by
    /// a static latch, kept in the shipped build.
    /// </summary>
    private void DumpTakeDamageHierarchyOnce()
    {
        if (_takeDamageDumped || _active?.Name != "TakeDamagePanel")
            return;
        RectTransform? root = Panel?.Target;
        TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized ? Singleton<TakeDamagePanel>.Instance : null;
        if (root == null || p == null)
            return;
        _takeDamageDumped = true;

        var widgets = new HashSet<Transform>();
        if (p.burnAvailableCardsToggle != null) widgets.Add(p.burnAvailableCardsToggle.transform);
        if (p.burnDiscardedCardsToggle != null) widgets.Add(p.burnDiscardedCardsToggle.transform);
        if (p.takeDamageButton != null) widgets.Add(p.takeDamageButton.transform);

        var sb = new System.Text.StringBuilder(4096);
        sb.Append("DECISION DOCK DIAGNOSTIC (one-time, first TakeDamage dock) — ground-truth hierarchy.\n");
        sb.Append("Y bounds are subtree-root-local px [bottom..top]; <WIDGET> marks a serialized field.\n");
        sb.Append("=== A. isolated docked ROW subtree (Panel.Target) ===\n");
        DumpSubtree(sb, root, root, 0, widgets);
        if (p.transform is RectTransform windowRt)
        {
            sb.Append("=== B. TakeDamagePanel WINDOW subtree (Singleton<TakeDamagePanel>.Instance) ===\n");
            DumpSubtree(sb, windowRt, windowRt, 0, widgets);
        }
        VRLog.Info("WorldUI", sb.ToString());
    }

    /// <summary>Recursively append one transform (and its children) to the diagnostic dump.</summary>
    private static void DumpSubtree(System.Text.StringBuilder sb, RectTransform reference, Transform node,
        int depth, HashSet<Transform> widgets)
    {
        sb.Append(' ', depth * 2);
        sb.Append(node.name).Append(" [active=").Append(node.gameObject.activeInHierarchy).Append(']');
        if (widgets.Contains(node))
            sb.Append(" <WIDGET>");
        var tmp = node.GetComponent<TMP_Text>();
        if (tmp != null)
        {
            string t = tmp.text ?? string.Empty;
            if (t.Length > 40) t = t.Substring(0, 40);
            t = t.Replace("\n", "\\n").Replace("\r", string.Empty);
            sb.Append(" TMP \"").Append(t).Append("\" glyphY=[")
              .Append(GlyphEdgeIn(reference, tmp, top: false).ToString("F0")).Append("..")
              .Append(GlyphEdgeIn(reference, tmp, top: true).ToString("F0")).Append(']');
        }
        else if (node is RectTransform rt && (node.GetComponent<Graphic>() != null || node.GetComponent<Selectable>() != null))
        {
            string kind = node.GetComponent<Selectable>() != null ? "SEL" : "IMG";
            sb.Append(' ').Append(kind).Append(" rectY=[")
              .Append(EdgeYIn(reference, rt, min: true).ToString("F0")).Append("..")
              .Append(EdgeYIn(reference, rt, min: false).ToString("F0")).Append(']');
        }
        sb.Append('\n');
        for (int i = 0; i < node.childCount; i++)
            DumpSubtree(sb, reference, node.GetChild(i), depth + 1, widgets);
    }

    /// <summary>
    /// The active prompt's actionable widget ROOT RECTS, straight from the game's own
    /// serialized fields (no structural guessing): TakeDamagePanel's two burn toggles +
    /// take-damage button, YesNoDialog's yes/no buttons, DialogPopup's pooled option
    /// buttons. These are the ONLY rects the gap fix ever moves — leaf displacement,
    /// never a shared ancestor (user #13a). Empty list → the caller logs and no-ops.
    /// </summary>
    private void ResolvePromptWidgets(List<RectTransform> widgets)
    {
        widgets.Clear();
        switch (_active?.Name)
        {
            case "TakeDamagePanel":
            {
                TakeDamagePanel? p = Singleton<TakeDamagePanel>.IsInitialized
                    ? Singleton<TakeDamagePanel>.Instance
                    : null;
                if (p == null)
                    return;
                AddWidgetRect(widgets, p.burnAvailableCardsToggle != null ? p.burnAvailableCardsToggle.transform : null);
                AddWidgetRect(widgets, p.burnDiscardedCardsToggle != null ? p.burnDiscardedCardsToggle.transform : null);
                AddWidgetRect(widgets, p.takeDamageButton != null ? p.takeDamageButton.transform : null);
                return;
            }
            case "YesNoDialog":
            {
                YesNoDialog? d = CardsGameApi.ShortRestDialog();
                if (d == null)
                    return;
                AddWidgetRect(widgets, d.yesButton != null ? d.yesButton.transform : null);
                AddWidgetRect(widgets, d.noButton != null ? d.noButton.transform : null);
                return;
            }
            case "DialogPopup":
            {
                UIManager? m = UIManager.Instance;
                DialogPopup? d = m != null ? m.dialogPopup : null;
                List<InputButton>? buttons = d != null ? d.optionButtons : null;
                if (buttons == null)
                    return;
                for (int i = 0; i < buttons.Count; i++)
                {
                    InputButton ib = buttons[i];
                    if (ib != null && ib.gameObject.activeInHierarchy && ib.ExtendedButton != null)
                        AddWidgetRect(widgets, ib.ExtendedButton.transform);
                }
                return;
            }
        }
    }

    private static void AddWidgetRect(List<RectTransform> widgets, Transform? t)
    {
        if (t is RectTransform rt && !widgets.Contains(rt))
            widgets.Add(rt);
    }

    /// <summary>
    /// USER #14 placement measurement, extended for the bar-stack gap fix: the top- and
    /// bottom-most VISIBLE widget graphics of the active prompt, expressed in world metres
    /// ABOVE the host pivot along <paramref name="up"/> (translation-invariant — measured
    /// relative to the host's current position, valid for the position <see cref="Place"/>
    /// is about to solve). Glyph-true for TMP labels, rect corners for plates/images. Falls
    /// back to <paramref name="rowTopAbovePivot"/>/<paramref name="rowBottomAbovePivot"/>
    /// (the fitted row edges) when the prompt exposes no resolvable widget graphics. The TOP
    /// anchors the BUTTONS themselves — not the authored option-column rects — so the gap the
    /// user tunes is the real distance from the board edge to the pressable widgets; the
    /// BOTTOM (one walk, same visibility rules — top and bottom can never disagree) feeds
    /// <see cref="RowBottomUpMeters"/> so the use-bars stack hangs directly under the row.
    /// </summary>
    private void WidgetBlockEdgesAbovePivot(Transform host, Vector3 up,
        float rowTopAbovePivot, float rowBottomAbovePivot, out float top, out float bottom)
    {
        ResolvePromptWidgets(WidgetRectScratch);
        Vector3 origin = host.position;
        top = float.MinValue;
        bottom = float.MaxValue;
        for (int i = 0; i < WidgetRectScratch.Count; i++)
        {
            RectTransform w = WidgetRectScratch[i];
            if (w == null)
                continue;
            GraphicScratch.Clear();
            w.GetComponentsInChildren(includeInactive: false, GraphicScratch);
            for (int g = 0; g < GraphicScratch.Count; g++)
            {
                Graphic gr = GraphicScratch[g];
                if (gr == null)
                    continue;
                if (gr is TMP_Text label)
                {
                    label.ForceMeshUpdate();
                    Bounds b = label.textBounds;
                    if (string.IsNullOrEmpty(label.text) || b.size.y <= 0.001f)
                    {
                        top = Mathf.Max(top, WorldUpEdge(label.rectTransform, origin, up, topEdge: true));
                        bottom = Mathf.Min(bottom, WorldUpEdge(label.rectTransform, origin, up, topEdge: false));
                    }
                    else
                    {
                        Vector3 hi = label.transform.TransformPoint(new Vector3(b.center.x, b.max.y, 0f));
                        Vector3 lo = label.transform.TransformPoint(new Vector3(b.center.x, b.min.y, 0f));
                        top = Mathf.Max(top, Vector3.Dot(hi - origin, up));
                        bottom = Mathf.Min(bottom, Vector3.Dot(lo - origin, up));
                    }
                }
                else
                {
                    var rt = (RectTransform)gr.transform;
                    top = Mathf.Max(top, WorldUpEdge(rt, origin, up, topEdge: true));
                    bottom = Mathf.Min(bottom, WorldUpEdge(rt, origin, up, topEdge: false));
                }
            }
        }
        if (top <= float.MinValue)
        {
            top = rowTopAbovePivot;
            bottom = rowBottomAbovePivot;
        }
    }

    /// <summary>Highest/lowest of a rect's four world corners projected onto <paramref name="up"/>, relative to <paramref name="origin"/>.</summary>
    private static float WorldUpEdge(RectTransform rt, Vector3 origin, Vector3 up, bool topEdge)
    {
        rt.GetWorldCorners(CornerScratch);
        float edge = topEdge ? float.MinValue : float.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            float y = Vector3.Dot(CornerScratch[i] - origin, up);
            edge = topEdge ? Mathf.Max(edge, y) : Mathf.Min(edge, y);
        }
        return edge;
    }

    /// <summary>
    /// Glyph-true top/bottom edge (root-local Y) of a TMP label: the rendered text
    /// bounds, not the authored rect — dialog labels are routinely authored in rects far
    /// taller than their glyphs, which made rect-based gaps lie (user #11b). Falls back
    /// to the rect edge when the label has no rendered glyphs.
    /// </summary>
    private static float GlyphEdgeIn(RectTransform root, TMP_Text label, bool top)
    {
        label.ForceMeshUpdate(); // one-shot per dock; the row was just reparented, ensure fresh bounds
        Bounds b = label.textBounds;
        if (string.IsNullOrEmpty(label.text) || b.size.x <= 0.001f || b.size.y <= 0.001f)
            return EdgeYIn(root, label.rectTransform, min: !top);
        Vector3 world = label.transform.TransformPoint(new Vector3(b.center.x, top ? b.max.y : b.min.y, 0f));
        return root.InverseTransformPoint(world).y;
    }

    /// <summary>Min/max local-Y of a rect's corners expressed in <paramref name="root"/> space.</summary>
    private static float EdgeYIn(RectTransform root, RectTransform rt, bool min)
    {
        rt.GetWorldCorners(CornerScratch);
        float edge = min ? float.MaxValue : float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float y = root.InverseTransformPoint(CornerScratch[i]).y;
            edge = min ? Mathf.Min(edge, y) : Mathf.Max(edge, y);
        }
        return edge;
    }

    /// <summary>Undo <see cref="AdjustDockedRow"/> — the antique tint colours back to the game's own values (placement leaves the row's own transforms untouched).</summary>
    private void RestoreRowAdjustments()
    {
        for (int i = 0; i < _tintedGraphics.Count; i++)
        {
            if (_tintedGraphics[i].graphic != null)
                _tintedGraphics[i].graphic.color = _tintedGraphics[i].color;
        }
        _tintedGraphics.Clear();
    }

    // ---- deliberate poke press-mode (user #13b) -------------------------------------------

    /// <summary>
    /// USER #13b: decision buttons take the DELIBERATE v1 poke press — plane contact
    /// only ARMS (pressed visual + light haptic), the click fires on the conscious
    /// WITHDRAWAL back past ReleaseDepth, and sweep-throughs/side-exits cancel silently
    /// — while every other converted surface keeps the v3 push-in depth-fire
    /// (PokePressDepthMm). The dock tags its host canvas in
    /// <see cref="Hands.Interact.DeliberatePokeSurfaces"/> per dock; the interactor
    /// reads [WorldUI] DecisionPokeDeliberate LIVE, so that config is a pure escape
    /// hatch (false → decision buttons press like everything else, no re-dock needed).
    /// </summary>
    private void RegisterDeliberateCanvas()
    {
        Canvas? host = Panel?.HostCanvas;
        if (host == null)
            return;
        _deliberateCanvas = host;
        Hands.Interact.DeliberatePokeSurfaces.Register(host, $"decision dock '{_active?.Name}'");
    }

    private void UnregisterDeliberateCanvas()
    {
        if (_deliberateCanvas is not null)
        {
            Hands.Interact.DeliberatePokeSurfaces.Unregister(_deliberateCanvas);
            _deliberateCanvas = null;
        }
    }

    // ---- window-remainder suppression (HandSuppression pattern; nothing destroyed) -----

    /// <summary>
    /// Cheap, idempotent alpha-0 of the window's root CanvasGroup (+ blocksRaycasts off),
    /// re-asserted every claimed tick. Applied the instant a decision prompt is claimed and
    /// open — even before its widget row is isolated/docked — so the raw flat window (the
    /// burn-confirm DialogPopup and its full card) never flashes in the HMD during the convert
    /// grace (2D-flash-on-burn fix). Records the window/group so <see cref="RestoreSuppression"/>
    /// hands them back. The nested-canvas (vignette/backdrop) suppression is applied separately
    /// in <see cref="ApplySuppression"/> once the row is docked and therefore outside the window.
    /// </summary>
    private void SuppressWindowGroup(UIWindow window)
    {
        _suppressedWindow = window;

        CanvasGroup? group = window.m_CanvasGroup != null
            ? window.m_CanvasGroup
            : window.GetComponent<CanvasGroup>();
        if (group != null)
        {
            _suppressedGroup = group;
            if (group.alpha != 0f)
                group.alpha = 0f;
            if (group.blocksRaycasts)
                group.blocksRaycasts = false;
        }
    }

    /// <summary>
    /// Full docked-tick suppression: the cheap CanvasGroup alpha-0 (via
    /// <see cref="SuppressWindowGroup"/>) PLUS disabling the window's nested vignette/backdrop
    /// canvases. Re-asserted every docked tick (the game rewrites the CanvasGroup alpha on
    /// Show/ToggleVisibility and could re-enable canvases live). The converted row is OUTSIDE the
    /// window subtree while docked, so neither touches it.
    /// </summary>
    private void ApplySuppression(UIWindow window)
    {
        SuppressWindowGroup(window);

        // Backdrops/vignettes are often NESTED CANVASES (adopted with
        // overrideSorting=true when the whole window floated) — a nested canvas
        // renders independently of ancestor CanvasGroup alpha, so it needs the
        // component disabled (stops its entire subtree rendering; the standard
        // hide-UI optimization, see CanvasConversion.AdoptNestedCanvases).
        CanvasScratch.Clear();
        window.GetComponentsInChildren(includeInactive: true, CanvasScratch);
        for (int i = 0; i < CanvasScratch.Count; i++)
        {
            Canvas nested = CanvasScratch[i];
            if (nested == null || !nested.enabled)
                continue;
            nested.enabled = false;
            if (!_disabledCanvases.Contains(nested))
            {
                _disabledCanvases.Add(nested);
                VRLog.Info("WorldUI", $"DECISION DOCK: nested canvas '{nested.name}' disabled " +
                                      "(vignette/backdrop/card suppressed while the widget row is docked).");
            }
        }
        CanvasScratch.Clear();
    }

    /// <summary>
    /// Undo (undock/shutdown): re-enable every canvas WE disabled; give the
    /// CanvasGroup back only while the window is still open (a closed window's
    /// group belongs to the game's own hide fade — the HandSuppression.Restore rule).
    /// </summary>
    private void RestoreSuppression()
    {
        for (int i = 0; i < _disabledCanvases.Count; i++)
        {
            if (_disabledCanvases[i] != null)
                _disabledCanvases[i].enabled = true;
        }
        _disabledCanvases.Clear();

        CanvasGroup? group = _suppressedGroup;
        UIWindow? window = _suppressedWindow;
        _suppressedGroup = null;
        _suppressedWindow = null;
        if (group != null && window != null && window.IsOpen)
        {
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }
    }

    public override void Shutdown()
    {
        if (WorldUIConfig.DecisionRowGapPx != null)
            WorldUIConfig.DecisionRowGapPx.SettingChanged -= OnRowGapSettingChanged; // user #11b live-apply
        bool hadPanel = Panel != null;
        UnregisterDeliberateCanvas();
        base.Shutdown(); // releases the conversion → row back in its 2D home
        if (hadPanel)
        {
            RestoreRowAdjustments();
            RestoreSuppression();
        }
        _active = null;
        _activeWindow = null;
        _wantSince = 0f;
        _targetWarned = false;
        _hmdFloatPlaced = false;
        _placementLogged = false;
        _lastLoggedGapPx = float.NaN;
        RowBottomUpMeters = null;
        _takeDamageDumped = false; // re-emit the one-time ground-truth dump after a module re-init
        DockingTakeDamage = false;
        ModalFallback.DecisionDock.Reset(); // any grace hand-off drops with us
        if (ReferenceEquals(Instance, this))
            Instance = null; // claim drops → generic fallback owns the windows again
    }
}
