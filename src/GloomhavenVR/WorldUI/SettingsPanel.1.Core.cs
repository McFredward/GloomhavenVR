using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them to something
// prettier. Compile order is the filename sort of the SDK's default `**/*.cs`
// glob, and a partial class's members land in metadata in compile order, so the
// numbering is what makes the six parts concatenate back into the original
// member order (and what keeps the field initializers in their original order).

/// <summary>
/// In-VR settings panel (Phase 5, MISSION B): a compact world-space panel of poke-able
/// steppers/toggles bound to the Phase-4 <see cref="ComfortSettings"/> accessors and
/// key module toggles. Changes apply LIVE (the comfort stack subscribes to its own
/// change events; module toggles are read live every frame) and persist automatically
/// (BepInEx saves on every ConfigEntry write).
///
/// Opening:
/// - the tray dashboard's gear button (Cards module, via <see cref="RequestToggle"/>;
///   test #20 removed the old free-floating world 'SET' pokeable at the table edge —
///   the tray gear superseded it), and
/// - a controller chord anywhere hands exist: hold the NON-dominant lower face button
///   (A/X — free for features, the recenter chord uses B+Y on both hands) for at least
///   [SettingsPanel] ChordHoldSeconds and RELEASE (P6: fires on release, because the
///   same button held longer is the manual flat-screen chord — see NonDominantHold).
///
/// Interaction: the panel canvas registers with <see cref="UguiPokeSurfaces"/>
/// (SmallDialog tuning — MISSION A.10) so the Phase-2 fingertip poke drives real uGUI
/// buttons; no new input paths. Menu2D includes Poke since P5, so the panel also works
/// in the main menu (menu rig).
///
/// Availability deliberately does NOT gate on <see cref="WorldUIConfig.ConversionActive"/>:
/// the panel hosts the [WorldUI] Master switch, so it must stay reachable to turn the
/// physicalized UI back ON.
///
/// MOVABLE (test #24): like the control board and the combat log, the panel rides a
/// mod-owned frame — holder (identity pose, diorama scale) → frame (grab root, user
/// size factor) → brass grab bar + FOLLOW/PINNED pin + the settings canvas. It drives
/// the SAME shared <see cref="PanelGrabHandle"/> core: one hand moves, two hands resize
/// (<see cref="PanelGrabHandle.MinScale"/>–<see cref="PanelGrabHandle.MaxScale"/> = 0.15×–2×),
/// release persists the layout as [SettingsPanel] Right/Up/Forward/Scale.
/// Every OPEN routes the spawn through <see cref="PanelPlacement"/> so the panel always
/// appears cleanly in the forward field of view (never clipped into the board or
/// stranded off to the side), then behaves world-static (PINNED) or seat-anchored
/// (FOLLOW) exactly like the combat log.
/// </summary>
internal sealed partial class SettingsPanel : IPanelGrabOwner
{
    private const float PanelWidthPx = 520f; // sidebar + content two-column (2026-07 redesign)
    private const float RowHeightPx = 34f;
    private const float RefreshInterval = 0.25f;

    // Frame geometry (real meters at diorama scale 1 — mirrors CombatLogSurface).
    private const float CanvasMetersPerPixel = 0.0007f; // PanelWidthPx (520) × this ≈ 36 cm wide

    /// <summary>
    /// Item 6: the panel's FIXED real-world reference width (meters). It started at "roughly the
    /// control board width" (PlayTray.BoardW = 0.64 m) but is an INDEPENDENT tunable and is now
    /// 28 % wider than the board — it was widened together with PanelWidthPx to keep the text
    /// density constant. Do not "re-sync" it to BoardW or to ModalFallback's 0.80 m; the three
    /// are separate numbers. The panel size is DECOUPLED from the diorama zoom
    /// (PanelLayout.WorldScale): zooming the table no longer grows/shrinks the options menu.
    /// The holder scale is derived from this so the base canvas (PanelWidthPx × CanvasMetersPerPixel)
    /// renders at this width at [WorldUI] SettingsScale = 1; SettingsScale — clamped everywhere to
    /// the SHARED <see cref="PanelGrabHandle.MinScale"/>–<see cref="PanelGrabHandle.MaxScale"/>
    /// range (0.15×–2×), not the 0.5× floor its config description still names — is then the
    /// user's ONLY size control. WorldScale is still used for POSITION/distance, not size.
    /// </summary>
    private const float SettingsPanelWidthMeters = 0.82f; // widened with PanelWidthPx to keep text density constant
    private const float BarGapMeters = 0.03f;           // panel bottom edge sits this far above the bar center
    private const float BarThickness = 0.024f;
    private const float BarWidthFraction = 0.55f;
    private const float ZoneWidthFraction = 0.62f;

    private GameObject? _root;   // the settings canvas (child of the frame)
    private Canvas? _canvas;
    private bool _open;
    private float _nextRefresh;
    private readonly List<Action> _refreshers = new(16);

    // Mod-owned grab frame (holder → frame → bar/pin/canvas), built lazily in Build().
    private Transform? _holder;   // identity pose, carries the diorama scale
    private Transform? _frame;     // grab root at the bar center; localScale = user size factor
    private Transform? _bar;
    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;
    private PlayTray.BoardButton? _pin;
    private Transform? _pinAnchor;
    private PlayTray? _laserTray;
    private bool _placedFromConfig;
    private int _facedPoseVersion = -1; // RigPoseVersion the orientation was derived at

    // ---- Debug — Board tuning (Part E) ---------------------------------------------------
    /// <summary>Which board-attached element the debug X/Y/Z/Size/Spacing/Shape steppers currently drive.</summary>
    private enum DebugElement
    {
        Rest, Generic, Overlays, Initiative, Active, Piles, Board,
        // Items 4/6: every remaining board-attached element is now tunable per board.
        Objectives, Elements, VRSettings, Pin, Readout, Cluster,
        // Item C: the shared decision dock (text + buttons under the board).
        Decision,
        // Items rework (requirement 3): the ITEM-USE clip-in slot under the board next to the
        // Confirm/Undo buttons. Per-board offset only (no size/spacing/shape), so only the generic
        // offset rows show — like the other pure-offset elements (Initiative, Readout, Pin, ...).
        ItemUse,
        // Items rework (requirement 2): the ITEM pile fan + held item-card pose offset — nudges the
        // item cards independently of the ability fan (they are a different, near-square shape).
        // Per-board offset only (no size/spacing/shape), so only the generic offset rows show.
        ItemCard,
        // Request C (2026-07): the transient round-phase buttons ("Bewegung überspringen",
        // "Angriff überspringen", …) as their OWN clearly-named element under the "Tasten"
        // category — the ButtonTuning [RoundButtons] geometry (offset X/Y/Z, shape, cap
        // size, width/height/depth/travel) lives here now instead of the old collapsed
        // "Knöpfe" expander the user could not find. GLOBAL values (not per board), so the
        // generic per-board offset/board rows hide themselves for this element.
        RoundButtons,
        // Category split (2026-07, user: "every value applies ONLY to its own category"):
        // [BoardDashboard] = the gear ("Einstellungen") + follow ("Fixiert") plates. GLOBAL like
        // RoundButtons (values ride every board). NOTE (settings audit 2026-07): the sibling
        // BoardButtons member is GONE — issue 6 merged the [BoardButtons] Confirm/Undo geometry
        // rows into the Generic element (they are the SAME physical caps), which removed it from
        // both NavElements and SubCatElements; CurrentElement() can only ever return an element
        // listed there, so the member, its chooser label and its reset case were unreachable.
        BoardDashboard,
        // Issue 2 (2026-07 Debug section): the four wall see-through FADE fractions are
        // developer-grade tuning, so they moved out of the user-facing "Wände" category into
        // the new "Debug" section as their OWN global element (like RoundButtons). GLOBAL —
        // no per-board offset/board selector.
        WallFade,
        // Per-style embodiment tuning (2026-07, user: "I count ALL offsets including Hände,
        // Figuren, Handgelenk as Debug"): the former top-level Hände / Figuren / Handgelenk
        // categories became Debug elements. Each carries its OWN "Stil" cycle + "gilt pro Stil"
        // note + the per-STYLE steppers (per HAND STYLE, NOT per board — so the board selector
        // and the generic per-board offset/size/spacing/shape rows all hide, like the other
        // ElementIsGlobalTuning elements). HandOffsets also hosts the global card-fan geometry.
        HandOffsets, FigureOffsets, WristOffsets,
        // User 4 (2026-07): the [ButtonColors] keycap LABEL text + cap-FACE tint binds as their OWN
        // "Knopf-Farben" element under the Tasten sub-category. GLOBAL (rides every board/cap), so it
        // hides the board selector / per-board offset rows like the other ElementIsGlobalTuning
        // elements. Live-applied automatically: writing a bind bumps ButtonTuning.Version and the
        // NativeButtonSkin re-skins the caps (keycaps on the Version rebuild, cluster per-tick).
        ButtonColors,
        // 2026-07 (user: "Alle config einstellungen sollen im VR Menu anpassbar sein!"): the GENERIC
        // config browser's topics. One element per ConfigCatalog.ConfigTopic, in the SAME order, so
        // the existing element chooser IS the topic chooser — no second navigation idiom. These are
        // not board elements at all: they carry no offset/size/spacing/shape and no per-board
        // meaning, which is exactly what ElementIsConfigTopic tells the per-board rows.
        CfgDiagnostics, CfgVisual, CfgMovement, CfgHands, CfgCards, CfgButtons,
        CfgPanels, CfgBoardTargeting, CfgBoardGeometry, CfgNetwork, CfgSystem, CfgOther,
    }

    /// <summary>
    /// TOP-LEVEL navigation category — the persistent left-hand SIDEBAR entries (2026-07 UX
    /// redesign, user: "the debug menu is cluttered — give me a faster overview and better
    /// navigation"). Selecting a category in the sidebar swaps the right-hand content pane to
    /// show ONLY that category's rows/elements (poke- and laser-driven, exactly like every
    /// other button). The element-BEARING category (Debug) additionally exposes an in-pane
    /// Element chooser (the existing accordion) over the many board-attached elements; every other
    /// category is a flat scannable list. Order matches <see cref="NavElements"/>.
    /// </summary>
    // Issue 2 (2026-07): the element-BEARING tuning tabs plus the wall-fade fractions were
    // consolidated into ONE top-level "Debug" category. 2026-07 follow-up (user: "I count ALL
    // offsets as Debug — Debug should hold the individual sub-items one by one; the normal-user
    // choices belong OUTSIDE Debug, but no single-setting tab either"): the per-style Hände /
    // Figuren / Handgelenk tabs also folded INTO Debug as elements, and the one-row "Wände" tab
    // folded its user-facing see-through toggle into "Anzeige". What remains is a short, scannable
    // top-level list of genuinely user-facing tabs (Welt / Anzeige / Avatar) plus the single Debug
    // tab that holds ALL the deep per-element tuning. Debug is the sole element-bearing category.
    // 2026-07 performance pass: "Leistung" is added as its OWN top-level category rather than being
    // squeezed into "Anzeige". It is a coherent, self-contained block (frame-time measurement +
    // one switch per optimization) built by the single method BuildPerformanceCategory(), so the
    // inbound settings-menu reorganization can move it wholesale by moving that one call and this
    // one enum member — nothing else in this file reaches into it.
    private enum NavCat { Welt, Anzeige, Avatar, Leistung, Debug }
    private const int NavCatCount = 5;

    /// <summary>
    /// SINGLE source of truth for category → board-attached elements (re-slice by editing this
    /// one table). Only the ONE element-BEARING category carries a list — <see cref="NavCat.Debug"/>
    /// (every board panel/overlay/widget PLUS every button group: rest keys, Confirm/Undo group
    /// including its [BoardButtons] rectangle geometry, cluster, the [RoundButtons] /
    /// [BoardDashboard] geometry sets, the decision dock, and the wall-fade fractions). All other
    /// categories have an EMPTY list — <see cref="CategoryHasElements"/> keys off that and hides
    /// the board + element selectors, showing the category's own flat rows instead. Order matches
    /// <see cref="NavCat"/>.
    /// </summary>
    private static readonly DebugElement[][] NavElements =
    {
        Array.Empty<DebugElement>(),                                                                          // Welt
        Array.Empty<DebugElement>(),                                                                          // Anzeige
        Array.Empty<DebugElement>(),                                                                          // Avatar
        Array.Empty<DebugElement>(),                                                                          // Leistung (flat rows only)
        // Debug (issue 2 + 2026-07 fold-in): the union of the former Board (panels/widgets) + Tasten
        // (button geometry) elements, the wall-fade fractions as one global WallFade element, AND the
        // former per-style top-level tabs — Hände-Offsets / Figuren-Offsets / Handgelenk — now Debug
        // elements you open one by one. Issue 6: the former standalone BoardButtons element is GONE —
        // its [BoardButtons] rectangle W/H/D/Travel rows now live under Generic (the SAME physical
        // Confirm/Undo caps). Board is element 0 so the Debug pane opens on the whole-board tuning.
        new[] { DebugElement.Board, DebugElement.Objectives, DebugElement.Elements,
                DebugElement.Initiative, DebugElement.Piles, DebugElement.Active,
                DebugElement.Overlays, DebugElement.Readout, DebugElement.VRSettings, DebugElement.Pin,
                DebugElement.Rest, DebugElement.Generic, DebugElement.Cluster,
                DebugElement.RoundButtons, DebugElement.BoardDashboard, DebugElement.Decision,
                DebugElement.ItemUse, DebugElement.ItemCard,
                DebugElement.WallFade,
                DebugElement.HandOffsets, DebugElement.FigureOffsets, DebugElement.WristOffsets,
                DebugElement.ButtonColors,
                // The generic config browser's topics (2026-07) — listed here too so this table
                // stays the documented union of SubCatElements.
                DebugElement.CfgDiagnostics, DebugElement.CfgVisual, DebugElement.CfgMovement,
                DebugElement.CfgHands, DebugElement.CfgCards, DebugElement.CfgButtons,
                DebugElement.CfgPanels, DebugElement.CfgBoardTargeting, DebugElement.CfgBoardGeometry,
                DebugElement.CfgNetwork, DebugElement.CfgSystem, DebugElement.CfgOther },                    // Debug
    };

    /// <summary>
    /// User 5b (2026-07): the ~20 Debug elements are too many to scan as one flat chooser, so they
    /// cluster into a handful of SUB-CATEGORIES the user picks first, then picks the element within.
    /// This is the SINGLE re-slice table (edit here to regroup). Every Debug element appears in
    /// EXACTLY one sub-category, and the union equals <see cref="NavElements"/>[Debug] (minus the
    /// merged-away BoardButtons). The chooser lists only the SELECTED sub-category's elements;
    /// <see cref="CurrentElement()"/> then returns the selected element, so every per-element row/gate
    /// (which keys off CurrentElement()) keeps working unchanged. Order matches <see cref="DebugSubCat"/>.
    /// </summary>
    private static readonly DebugElement[][] SubCatElements =
    {
        // Alle Einstellungen — the GENERIC config browser (2026-07, user: "Alle config
        // einstellungen sollen im VR Menu anpassbar sein!"). FIRST on purpose: it is the umbrella
        // that contains every entry the mod binds, including the ones the curated Debug panes
        // below never got a hand-written row for, so it is what Debug should open on. Its
        // "elements" are ConfigCatalog TOPICS in catalog order — measurement first, since that is
        // what a power user opens this pane for.
        new[] { DebugElement.CfgDiagnostics, DebugElement.CfgVisual, DebugElement.CfgMovement,
                DebugElement.CfgHands, DebugElement.CfgCards, DebugElement.CfgButtons,
                DebugElement.CfgPanels, DebugElement.CfgBoardTargeting, DebugElement.CfgBoardGeometry,
                DebugElement.CfgNetwork, DebugElement.CfgSystem, DebugElement.CfgOther },
        // Board & Layout — the board itself + every board-attached panel/widget/overlay geometry.
        new[] { DebugElement.Board, DebugElement.Objectives, DebugElement.Elements,
                DebugElement.Initiative, DebugElement.Readout, DebugElement.Pin, DebugElement.VRSettings },
        // Karten & Stapel — the card piles, active-card grid, slot overlays, decision dock, item-use slot.
        new[] { DebugElement.Piles, DebugElement.Active, DebugElement.Overlays, DebugElement.Decision, DebugElement.ItemUse, DebugElement.ItemCard },
        // Tasten — every button group's geometry + the new Knopf-Farben (label/cap colours).
        new[] { DebugElement.Rest, DebugElement.Generic, DebugElement.Cluster,
                DebugElement.RoundButtons, DebugElement.BoardDashboard, DebugElement.ButtonColors },
        // Hände/Offsets — the per-hand-style embodiment offsets folded in from the old top-level tabs.
        new[] { DebugElement.HandOffsets, DebugElement.FigureOffsets, DebugElement.WristOffsets },
        // Wand-Durchsicht — the developer-grade wall see-through fade fractions (user 3: renamed
        // from the misleading "Welt-Tuning" since it only controls wall see-through).
        new[] { DebugElement.WallFade },
    };

    /// <summary>Debug sub-categories (user 5b) — the FIRST-level chooser inside the Debug pane.</summary>
    private enum DebugSubCat { AllSettings, BoardLayout, KartenStapel, Tasten, Offsets, WeltTuning }
    private const int DebugSubCatCount = 6;

    /// <summary>The element-BEARING category (Debug) exposes the in-pane board + element chooser.</summary>
    private static bool CategoryHasElements(NavCat c) => NavElements[(int)c].Length > 0;

    /// <summary>True while the current sidebar category is one of the element-bearing tuning tabs.</summary>
    private bool PerBoard() => CategoryHasElements(CurrentNav);

    private int _navCat;
    /// <summary>Selected Debug SUB-CATEGORY (user 5b): the first-level chooser inside the Debug pane.</summary>
    private int _debugSubCat;
    /// <summary>Remembered element index PER Debug sub-category (persistence within a session).</summary>
    private readonly int[] _subCatElement = new int[DebugSubCatCount];
    /// <summary>Sub-category ACCORDION state: true while the sub-category option rows are expanded.</summary>
    private bool _subCatListOpen;
    /// <summary>Element ACCORDION state: true while the per-element option rows are expanded.</summary>
    private bool _elementListOpen;
    private readonly List<GameObject> _rows = new(64);          // every gated content row (teardown + show/hide)
    private readonly List<Func<bool>> _rowVisible = new(64);    // parallel per-row visibility predicate
    private bool _healLogged;           // change-dedup for the out-of-view heal log
    private bool _respawnRequested;     // every OPEN drops the panel in view in front of the head
    private float _sizeScale;           // diorama WorldScale snapshotted at open (item 1/6: board-sized, zoom-stable)

    // ---- content build cursor (2026-07 sidebar redesign) --------------------------------------
    /// <summary>Rows built by <see cref="Row"/> parent here while set (sidebar/content column); else the canvas root.</summary>
    private RectTransform? _rowParent;
    /// <summary>Rows built by <see cref="Row"/> register with this visibility predicate while set; null = always visible (header/sidebar).</summary>
    private Func<bool>? _rowGate;

    private NavCat CurrentNav => (NavCat)_navCat;

    /// <summary>The selected Debug sub-category (clamped).</summary>
    private DebugSubCat CurrentSubCat => (DebugSubCat)Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1);

    /// <summary>
    /// The elements the in-pane chooser lists: the SELECTED Debug sub-category's slice (user 5b).
    /// Empty for the flat (non-element-bearing) categories, so the board + element choosers hide.
    /// </summary>
    private DebugElement[] CurrentSubCatElements =>
        _navCat == (int)NavCat.Debug ? SubCatElements[Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1)]
                                     : Array.Empty<DebugElement>();

    /// <summary>The element the per-board steppers currently drive (the selected element of the current Debug sub-category).</summary>
    private DebugElement CurrentElement()
    {
        DebugElement[] els = CurrentSubCatElements;
        if (els.Length == 0)
            return DebugElement.Board; // flat category: element rows are hidden anyway
        int idx = Mathf.Clamp(_subCatElement[Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1)], 0, els.Length - 1);
        return els[idx];
    }

    // ---- cross-module seam (test #15) ------------------------------------------------------

    /// <summary>The driver-owned live instance (single WorldUI driver; null after shutdown).</summary>
    private static SettingsPanel? _instance;

    public SettingsPanel()
    {
        _instance = this;
        Loc.OnChanged += RefreshLanguage; // live language following: rebuild while open
        // External ButtonTuning writes (cfg edit/reload, another writer) refresh the
        // "Rundenknöpfe" rows immediately instead of waiting for the 0.25 s cadence. Cheap:
        // only fires on entry writes, and RefreshAll is the same pass a stepper click runs.
        ButtonTuning.Changed += OnButtonTuningChanged;
    }

    /// <summary>ButtonTuning.Changed → refresh the open panel's readouts (no-op while closed).</summary>
    private void OnButtonTuningChanged()
    {
        if (_open)
            RefreshAll();
    }

    /// <summary>
    /// Live language following (Loc.OnChanged): the panel's section titles and static
    /// button/label captions are built once, so a language change is handled by a full
    /// clear + rebuild WHILE OPEN (the cheapest correct option — every static label then
    /// re-reads the new language). Closed panels rebuild fresh on the next open.
    /// </summary>
    private void RefreshLanguage()
    {
        // The config browser's group/section names are resolved once when its catalog is built (they
        // are read four times a second afterwards), so a language change has to drop that cache too
        // — otherwise the rebuilt pane would come back up with the previous language's group names.
        ConfigCatalog.Invalidate();
        if (!_open)
            return;
        SetOpen(false); // unregister poke, drop the mask request, hide the holder
        if (_holder != null)
            UnityEngine.Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _bar = null;
        _grabZone = null;
        _handle = null;
        _pin = null;
        _pinAnchor = null;
        _laserTray = null;
        _root = null;
        _canvas = null;
        _placedFromConfig = false;
        _facedPoseVersion = -1;
        _healLogged = false;
        _refreshers.Clear();
        _rows.Clear();
        _rowVisible.Clear();
        _rowParent = null;
        _rowGate = null;
        ResetTooltip();
        SetOpen(true); // Build() re-runs with the new language and re-registers everything
    }

    /// <summary>
    /// Test #15: toggle the panel from outside WorldUI — the tray dashboard's gear
    /// button (Cards) uses this. No-op while no panel exists (WorldUI off).
    /// </summary>
    internal static void RequestToggle() => _instance?.Toggle();

    // ---- per-frame -----------------------------------------------------------------------

    public void Tick()
    {
        bool available = VRSession.IsRunning || Plugin.DevMode.Value;
        if (!available)
        {
            if (_open)
                SetOpen(false);
            return;
        }

        TickChord();

        if (!_open || _root == null)
            return;

        // Keep the canvas camera fresh and hide when no world camera exists at all.
        Camera? cam = CanvasConversion.WorldCamera;
        if (cam == null)
        {
            SetOpen(false);
            return;
        }
        if (_canvas != null && _canvas.worldCamera != cam)
            _canvas.worldCamera = cam;

        Placement(cam);
        TickPin();

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshAll();
        }
    }

    public void Shutdown()
    {
        Loc.OnChanged -= RefreshLanguage;
        ButtonTuning.Changed -= OnButtonTuningChanged;
        if (ReferenceEquals(_instance, this))
            _instance = null;
        SetOpen(false);
        if (_holder != null)
            UnityEngine.Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _bar = null;
        _grabZone = null;
        _handle = null;
        _pin = null;
        _pinAnchor = null;
        _laserTray = null;
        _root = null;
        _canvas = null;
        _placedFromConfig = false;
        _facedPoseVersion = -1;
        _healLogged = false;
        _respawnRequested = false;
        _sizeScale = 0f; // re-snapshot the diorama scale on the next open
        _refreshers.Clear();
        _rows.Clear();
        _rowVisible.Clear();
        _rowParent = null;
        _rowGate = null;
        ResetTooltip();
    }

    // ---- open/close ----------------------------------------------------------------------

    internal void Toggle() => SetOpen(!_open);

    private void SetOpen(bool open)
    {
        if (_open == open)
            return;
        _open = open;

        if (open)
        {
            if (_holder == null)
                Build();
            if (_holder == null)
                return;
            // Every open drops the panel cleanly in view in front of the head (item 1/3):
            // the next Placement() consumes this and spawns via the shared clamp, so the
            // panel never opens clipped into the board or stranded outside the view border.
            _respawnRequested = true;
            _placedFromConfig = false;
            _holder.gameObject.SetActive(true);
            Camera? head = CanvasConversion.WorldCamera;
            if (head != null)
                Placement(head); // place THIS frame so it never flashes at a stale pose
            if (_canvas != null)
                UguiPokeSurfaces.Register(_canvas, PokeSurfaceTuning.SmallDialog);
            CanvasConversion.AddMaskRequest(); // head camera must render the UI layer
            RefreshAll();
            VRLog.Info("WorldUI", "Settings panel opened — placed in view in front of the head.");
        }
        else if (_holder != null)
        {
            // A bubble left open across a close would be the first thing on screen at the next
            // open, describing a row nobody is pointing at.
            HideTooltip();
            if (_canvas != null)
                UguiPokeSurfaces.Unregister(_canvas);
            CanvasConversion.RemoveMaskRequest();
            _holder.gameObject.SetActive(false);
            VRLog.Info("WorldUI", "Settings panel closed.");
        }
    }

}
