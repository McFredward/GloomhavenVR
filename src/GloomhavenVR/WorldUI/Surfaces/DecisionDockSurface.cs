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

    // ---- docked-row adjustments (users #5 + #7a + #11b; recorded & restored on undock) ---

    /// <summary>
    /// Only compress when the authored gap exceeds the configured target
    /// (<see cref="WorldUIConfig.DecisionRowGapPx"/>, user #11b) by at least this much —
    /// a small epsilon so a row already at target is not micro-shuffled.
    /// </summary>
    private const float RowGapMinDeltaPx = 1f;

    /// <summary>
    /// User #13a correction: the dark slab in the gap screenshot is the TRAY'S BRASS GRAB
    /// BAR (<c>PlayTray.BuildHandle</c>: root-local y −0.19, zone −0.215..−0.165) — a live
    /// affordance, never suppressed. A row that fits the mount budget stays ~28 mm below
    /// it by construction (mount y −0.29, max height 0.12 → row top ≤ −0.23), but a row
    /// still overflowing the height budget (the MinDensityScale clamp) is centered on the
    /// mount and could reach up into the bar — <see cref="Place"/> pushes the host down so
    /// the row's top edge keeps this clearance (scale-1 metres) under the bar's zone.
    /// </summary>
    private const float BarClearanceMeters = 0.008f;

    /// <summary>
    /// Antique multiply-tint for the docked row's widget backgrounds (user #5): the
    /// game-default light stone sprite sinks toward the dark wood / aged brass family
    /// of the mod's board buttons (the VR-settings gear look). Multiplied onto the
    /// authored Graphic colour, so per-widget differences survive; uGUI ColorTint
    /// transitions multiply on the CanvasRenderer ON TOP of this, so pressed/disabled
    /// dimming keeps working.
    /// </summary>
    private static readonly Color AntiqueTint = new(0.58f, 0.46f, 0.31f, 1f);

    private readonly List<(RectTransform rt, Vector2 anchoredPos)> _shiftedRects = new(4);
    private readonly List<(Graphic graphic, Color color)> _tintedGraphics = new(8);
    private readonly List<LayoutGroup> _disabledLayoutGroups = new(2);
    private Canvas? _deliberateCanvas;
    private bool _barClearanceLogged;
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

    /// <summary>User #11b: live re-apply of the docked-row gap when the config bind changes.</summary>
    private void OnRowGapSettingChanged(object? sender, System.EventArgs e)
    {
        if (Panel != null)
            AdjustDockedRow();
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
                AdjustDockedRow();          // users #5 + #7a/#13a: antique tint + text↔button gap compression
                RegisterDeliberateCanvas(); // user #13b: decision buttons take the deliberate v1 poke press
                _barClearanceLogged = false;
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
    /// Dock on the tray's <see cref="PlayTray.DecisionMount"/> (pose-follow, shared
    /// tray density — the <see cref="TrayMountedPanelSurface"/> math with a CENTERED
    /// origin: the row centers on the mount, which hangs below the cards). While no
    /// usable mount exists (Cards module off, tray hidden/destroyed) the row floats
    /// HMD-anchored at reading distance instead (placed once, ModalFallback pattern)
    /// — a decision must never be invisible.
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
        float metersPerPx = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale) / density;

        // Grab-bar clearance (user #13a correction): the tray's brass handle bar hangs
        // just under the board's bottom edge, ABOVE the decision zone — it is a live
        // grab affordance and must stay usable, and neither the prompt text (row top)
        // nor the widgets may slide up under it. A compressed row keeps ~28 mm static
        // clearance by construction; this guard covers the overflow case (row taller
        // than the mount budget after the MinDensityScale clamp, centered on the
        // mount): project the bar's grab zone onto the mount's up axis and push the
        // whole host DOWN just enough that the row's top edge stays below the zone
        // with BarClearanceMeters to spare.
        Vector3 pos = mount.position;
        if (PlayTray.Current?.HandleZone is BoxCollider barZone)
        {
            Vector3 up = mount.up;
            float rowTop = rect.yMax * metersPerPx * trayScale; // row top above mount origin, world m
            Transform bt = barZone.transform;
            float barCenterUp = Vector3.Dot(bt.TransformPoint(barZone.center) - mount.position, up);
            float barHalfUp = 0.5f * barZone.size.y * Mathf.Abs(bt.lossyScale.y);
            float overshoot = rowTop - (barCenterUp - barHalfUp - BarClearanceMeters * trayScale);
            if (overshoot > 0f)
            {
                pos -= up * overshoot;
                if (!_barClearanceLogged)
                {
                    _barClearanceLogged = true;
                    VRLog.Info("WorldUI", $"DECISION DOCK: row top would reach the tray grab bar — host " +
                                          $"pushed down {overshoot * 1000f:F0} mm so the bar stays clear " +
                                          $"(row top {rowTop * 1000f:F0} mm above mount, bar zone bottom " +
                                          $"{(barCenterUp - barHalfUp) * 1000f:F0} mm, clearance " +
                                          $"{BarClearanceMeters * trayScale * 1000f:F0} mm).");
                }
            }
        }

        Transform host = Panel.HostTransform;
        host.SetPositionAndRotation(pos, mount.rotation); // centered origin (bar-clearance shifted)
        host.localScale = Vector3.one * (metersPerPx * trayScale);
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
    /// One-shot per dock (re-run live on a <see cref="WorldUIConfig.DecisionRowGapPx"/>
    /// change). (a) USER #5 — antique restyle: every Selectable background in
    /// the docked row is multiply-tinted toward the mod's dark-wood/aged-brass board-
    /// button family and its labels turn parchment gold (<see cref="NativeButtonSkin.LabelColor"/>),
    /// so the docked native buttons ("Auswahl beenden", Ja/Nein, burn choices …) read
    /// like the VR-settings gear instead of the game's default look. (b) USERS
    /// #7a/#11b/#12a — gap compression: prompts docked WITH their prompt text (the
    /// take-damage row's "Erleide entweder Schaden…" block, the YesNoDialog box) author
    /// a large empty band between text and widgets (2D dialog spacing); the widget
    /// branches are shifted up until the visible gap equals
    /// <see cref="WorldUIConfig.DecisionRowGapPx"/> — see
    /// <see cref="CompressPromptGap"/> for the mechanism.
    ///
    /// USER #13a MECHANISM FIX (round 4 — no fork nodes): round 3 resolved the prompt
    /// text from the game's serialized <c>TakeDamagePanel.takeDamageText</c> and walked
    /// up to the text↔widget FORK node — but on the real prefab that serialized field is
    /// the take-damage BUTTON'S OWN LABEL ('Text' under 'Receive Damage'; its colour is
    /// driven together with <c>damageAmount</c> in <c>UpdateTakeDamageOptionVisuals</c>,
    /// TakeDamagePanel.cs:385/393), so "text and widget share a branch" held by
    /// construction and the layout was never touched (log evidence, build c55674f57).
    /// Fork nodes are GONE: only the widgets' OWN root rects (the serialized Selectable
    /// transforms, <see cref="ResolvePromptWidgets"/>) are ever moved — displacing a
    /// leaf rect cannot move the header text no matter how the branches fork. The
    /// header text is found GEOMETRICALLY (any TMP in the row outside every widget rect
    /// whose glyphs sit above the widget band), the gap is measured glyph-true header
    /// bottom → topmost VISIBLE widget graphic, and every widget rect shifts up by
    /// (gap − DecisionRowGapPx). A LayoutGroup on a moved widget's direct parent would
    /// re-assert the authored position on its next rebuild — it is component-disabled
    /// while docked. Everything is recorded and handed back by
    /// <see cref="RestoreRowAdjustments"/> on undock — live game widgets are never
    /// permanently mutated.
    /// </summary>
    private void AdjustDockedRow()
    {
        RestoreRowAdjustments(); // never double-record
        RectTransform? root = Panel?.Target;
        if (root == null)
            return;

        // (a) antique tint + parchment labels.
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

        // (b) text↔widget gap compression (users #7a/#11b/#12a/#13a): leaf-rect shifts of
        // the serialized widget rects only — see the method doc and CompressPromptGap.
        string gapNote = CompressPromptGap(root);

        VRLog.Info("WorldUI", $"DECISION DOCK: row adjusted — {styled} widget background(s) antique-tinted " +
                              "(dark-wood/brass + parchment labels, the VR-settings-button style)" + gapNote);
    }

    /// <summary>
    /// USER #13a gap mechanism (leaf rects only — see <see cref="AdjustDockedRow"/> for
    /// why the fork-node rounds failed): the actionable widgets' OWN root rects come
    /// from the game's serialized fields (<see cref="ResolvePromptWidgets"/>); the
    /// header text is any TMP in the row OUTSIDE every widget rect whose glyphs sit
    /// above the widget band (glyph-true — dialog labels are authored in rects far
    /// taller than their glyphs, and the take-damage Selectable rects span their whole
    /// option columns, so authored rects lie on both sides). The visible gap is the
    /// lowest such header line's glyph bottom minus the topmost VISIBLE widget graphic;
    /// every widget rect is shifted up by (gap − <see cref="WorldUIConfig.DecisionRowGapPx"/>),
    /// recorded in <see cref="_shiftedRects"/> for restore. A LayoutGroup on a moved
    /// widget's direct parent is disabled while docked (recorded in
    /// <see cref="_disabledLayoutGroups"/>) so it cannot re-assert the authored
    /// position on a rebuild. The tray's grab bar hangs ABOVE the decision zone and is
    /// handled purely by placement (<see cref="Place"/>'s clearance guard) — nothing in
    /// the row is suppressed. Returns the log fragment naming every moved rect (px) and
    /// every disabled layout component, or why nothing moved.
    /// </summary>
    private string CompressPromptGap(RectTransform root)
    {
        ResolvePromptWidgets(WidgetRectScratch);
        for (int i = WidgetRectScratch.Count - 1; i >= 0; i--)
        {
            RectTransform w = WidgetRectScratch[i];
            if (w == null || ReferenceEquals(w, root) || !w.IsChildOf(root))
                WidgetRectScratch.RemoveAt(i);
        }
        if (WidgetRectScratch.Count == 0)
            return ", gap: no known widget rects for this prompt — layout untouched.";

        float widgetTop = float.MinValue;
        for (int i = 0; i < WidgetRectScratch.Count; i++)
            widgetTop = Mathf.Max(widgetTop, VisualTopIn(root, WidgetRectScratch[i]));
        if (widgetTop <= float.MinValue)
            return ", gap: widget rects have no visible graphics — layout untouched.";

        // Header text: glyph-true BOTTOM of the LOWEST text line above the widget band.
        TextScratch.Clear();
        root.GetComponentsInChildren(includeInactive: false, TextScratch);
        TMP_Text? header = null;
        float textBottom = float.MaxValue;
        for (int i = 0; i < TextScratch.Count; i++)
        {
            TMP_Text label = TextScratch[i];
            if (label == null || IsUnderAnyWidget(label.transform))
                continue; // widget labels ('Receive Damage' → 'Text', toggle captions) never anchor the gap
            float bottom = GlyphEdgeIn(root, label, top: false);
            if (bottom < widgetTop)
                continue; // beside/below the widgets — not a header line
            if (bottom < textBottom)
            {
                textBottom = bottom;
                header = label;
            }
        }
        if (header == null)
            return ", gap: no prompt text above the widgets — layout untouched.";

        float gap = textBottom - widgetTop;
        float gapTarget = Mathf.Clamp(WorldUIConfig.DecisionRowGapPx.Value, 0f, 60f);
        if (gap <= gapTarget + RowGapMinDeltaPx)
            return $", text↔widget gap {gap:F0}px already ≤ target {gapTarget:F0}px — untouched.";

        float delta = gap - gapTarget;
        var moved = new System.Text.StringBuilder(96);
        var layouts = new System.Text.StringBuilder();
        for (int i = 0; i < WidgetRectScratch.Count; i++)
        {
            RectTransform w = WidgetRectScratch[i];
            // A LayoutGroup on the DIRECT parent owns this child's anchoredPosition and
            // would re-assert it on its next rebuild — stand it down while docked.
            Transform parent = w.parent != null ? w.parent : root;
            DisableFightingLayout(parent, layouts);
            // Express the root-space px delta in the widget's parent space (scale-safe).
            float localY = parent.InverseTransformVector(root.TransformVector(new Vector3(0f, delta, 0f))).y;
            _shiftedRects.Add((w, w.anchoredPosition));
            w.anchoredPosition += new Vector2(0f, localY);
            if (moved.Length > 0)
                moved.Append(", ");
            moved.Append('\'').Append(w.name).Append('\'');
        }
        string layoutNote = layouts.Length > 0
            ? $"; layout component(s) disabled while docked: {layouts}"
            : string.Empty;
        return $", text '{header.name}'↔widget gap {gap:F0}px → {gapTarget:F0}px: widget rect(s) {moved} " +
               $"moved up by {delta:F0}px ([WorldUI] DecisionRowGapPx){layoutNote}.";
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

    /// <summary>True when <paramref name="t"/> is one of the resolved widget rects or inside one.</summary>
    private static bool IsUnderAnyWidget(Transform t)
    {
        for (int i = 0; i < WidgetRectScratch.Count; i++)
        {
            RectTransform w = WidgetRectScratch[i];
            if (w != null && t.IsChildOf(w)) // IsChildOf is also true for t == w
                return true;
        }
        return false;
    }

    /// <summary>
    /// Component-disable an enabled <see cref="LayoutGroup"/> on a moved widget's direct
    /// parent (only the direct parent can own the widget's anchoredPosition) — recorded
    /// in <see cref="_disabledLayoutGroups"/>, re-enabled by
    /// <see cref="RestoreRowAdjustments"/> on undock so the game's 2D layout rebuilds
    /// exactly as authored.
    /// </summary>
    private void DisableFightingLayout(Transform parent, System.Text.StringBuilder note)
    {
        LayoutGroup? group = parent.GetComponent<LayoutGroup>();
        if (group == null || !group.enabled || _disabledLayoutGroups.Contains(group))
            return;
        group.enabled = false;
        _disabledLayoutGroups.Add(group);
        if (note.Length > 0)
            note.Append(", ");
        note.Append('\'').Append(group.GetType().Name).Append(" on ").Append(parent.name).Append('\'');
    }

    /// <summary>
    /// Topmost VISIBLE edge (root-local Y) of a widget branch: the highest rendered
    /// Graphic under it — glyph-true for TMP labels, rect corners otherwise. The
    /// branch's own rect is only the fallback (widget rects are authored spanning
    /// whole option columns, far above their drawn content).
    /// </summary>
    private static float VisualTopIn(RectTransform root, RectTransform unit)
    {
        GraphicScratch.Clear();
        unit.GetComponentsInChildren(includeInactive: false, GraphicScratch);
        float top = float.MinValue;
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g == null)
                continue;
            float edge = g is TMP_Text label
                ? GlyphEdgeIn(root, label, top: true)
                : EdgeYIn(root, (RectTransform)g.transform, min: false);
            top = Mathf.Max(top, edge);
        }
        return top > float.MinValue ? top : EdgeYIn(root, unit, min: false);
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

    /// <summary>Undo <see cref="AdjustDockedRow"/> — colours, positions and layout components back to the game's own values.</summary>
    private void RestoreRowAdjustments()
    {
        for (int i = 0; i < _tintedGraphics.Count; i++)
        {
            if (_tintedGraphics[i].graphic != null)
                _tintedGraphics[i].graphic.color = _tintedGraphics[i].color;
        }
        _tintedGraphics.Clear();
        for (int i = 0; i < _shiftedRects.Count; i++)
        {
            if (_shiftedRects[i].rt != null)
                _shiftedRects[i].rt.anchoredPosition = _shiftedRects[i].anchoredPos;
        }
        _shiftedRects.Clear();
        for (int i = 0; i < _disabledLayoutGroups.Count; i++)
        {
            if (_disabledLayoutGroups[i] != null)
                _disabledLayoutGroups[i].enabled = true; // its next rebuild restores the authored layout
        }
        _disabledLayoutGroups.Clear();
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
        DockingTakeDamage = false;
        ModalFallback.DecisionDock.Reset(); // any grace hand-off drops with us
        if (ReferenceEquals(Instance, this))
            Instance = null; // claim drops → generic fallback owns the windows again
    }
}
