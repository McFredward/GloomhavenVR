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
/// (0.5×–2×), release persists the layout as [SettingsPanel] Right/Up/Forward/Scale.
/// Every OPEN routes the spawn through <see cref="PanelPlacement"/> so the panel always
/// appears cleanly in the forward field of view (never clipped into the board or
/// stranded off to the side), then behaves world-static (PINNED) or seat-anchored
/// (FOLLOW) exactly like the combat log.
/// </summary>
internal sealed class SettingsPanel : IPanelGrabOwner
{
    private const float PanelWidthPx = 520f; // sidebar + content two-column (2026-07 redesign)
    private const float RowHeightPx = 34f;
    private const float RefreshInterval = 0.25f;

    // Frame geometry (real meters at diorama scale 1 — mirrors CombatLogSurface).
    private const float CanvasMetersPerPixel = 0.0007f; // 380 px ≈ 27 cm wide

    /// <summary>
    /// Item 6: the panel's FIXED real-world reference width (meters), roughly the control board
    /// width (PlayTray.BoardW ≈ 0.64 m). The panel size is DECOUPLED from the diorama zoom
    /// (PanelLayout.WorldScale): zooming the table no longer grows/shrinks the options menu.
    /// The holder scale is derived from this so the base canvas (PanelWidthPx × CanvasMetersPerPixel)
    /// renders at this width at [WorldUI] SettingsScale = 1; SettingsScale (0.5×–2×) is then the
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
        // the board keycaps got their OWN ButtonTuning sections and therefore their OWN
        // "Tasten" elements — [BoardButtons] = the Confirm/Undo ("Fortfahren"/"Rückgängig
        // machen") keycaps, [BoardDashboard] = the gear ("Einstellungen") + follow
        // ("Fixiert") plates. GLOBAL like RoundButtons (values ride every board).
        BoardButtons,
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
    private enum NavCat { Welt, Anzeige, Avatar, Debug }
    private const int NavCatCount = 4;

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
                DebugElement.ButtonColors },                                                                 // Debug
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
    private enum DebugSubCat { BoardLayout, KartenStapel, Tasten, Offsets, WeltTuning }
    private const int DebugSubCatCount = 5;

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
            if (_canvas != null)
                UguiPokeSurfaces.Unregister(_canvas);
            CanvasConversion.RemoveMaskRequest();
            _holder.gameObject.SetActive(false);
            VRLog.Info("WorldUI", "Settings panel closed.");
        }
    }

    // ---- placement (world-static/pinnable like the combat log) --------------------------------

    /// <summary>
    /// Keep the panel diorama-scaled and, while not grabbed, placed. A fresh open (or the
    /// FOLLOW re-derive, or a first placement) routes through <see cref="PanelPlacement"/>
    /// so the pose is guaranteed inside the forward FOV; PINNED then freezes it in the world.
    /// </summary>
    private void Placement(Camera head)
    {
        if (_holder == null || _frame == null)
            return;

        float worldScale = PanelLayout.WorldScale;
        // Item 1/6: the panel SIZE targets the CONTROL BOARD width (SettingsPanelWidthMeters ≈
        // PlayTray.BoardW). Because _holder is a WORLD-space root while the board lives at the
        // diorama scale, the world-meter target must be multiplied by the diorama WorldScale or
        // the menu renders ~WorldScale× too small (item 1: "absolutely tiny"). To still satisfy
        // item 6 ("don't grow/shrink with table zoom"), WorldScale is SNAPSHOTTED at open/respawn
        // into _sizeScale and held while the panel stays open — later zoom no longer rescales it.
        // The frame's own localScale carries the user's SettingsScale on top; worldScale is also
        // used live for POSITION/distance below.
        if (_respawnRequested || _sizeScale <= 0f)
            _sizeScale = Mathf.Max(worldScale, 0.01f);
        _holder.localScale = Vector3.one *
            (SettingsPanelWidthMeters / (PanelWidthPx * CanvasMetersPerPixel)) * _sizeScale;

        if (_handle != null && _handle.IsGrabbed)
            return; // the grab core owns the pose while held

        if (_respawnRequested)
        {
            _respawnRequested = false;
            PlaceInView(head);
            _placedFromConfig = true;
            _facedPoseVersion = VRRigDriver.RigPoseVersion;
        }
        else if (WorldUIConfig.SettingsFollow.Value || !_placedFromConfig)
        {
            // REGRESSION FIX (menu rescaled with zoom / chased the head): this branch used to
            // re-derive the pose EVERY tick — position from the LIVE diorama scale, then
            // ClampIntoView against the CURRENT head. Two visible failures on hardware:
            // (a) a world-grab ZOOM changes WorldScale continuously, so `offset * worldScale`
            //     slid the panel nearer/farther every frame — reads as "the menu rescales with
            //     the zoom" (its world size is fixed, but the distance to the head is not);
            // (b) the HEAL RATCHET — ClampIntoView parks the pose exactly ON the ±35° cone
            //     edge and PersistLayout writes that back as the new offset, so the very next
            //     head motion is "out of view" again: the panel visibly dragged along with the
            //     head, and BepInEx saved the cfg file every frame (hardware log: hundreds of
            //     consecutive "healed back into the forward field of view" lines).
            // The panel is a WORLD-anchored object: derive the pose at EVENTS only — the first
            // placement after an open, and a rig rebuild/recenter (RigPoseVersion bump, the
            // seat FOLLOW re-derive) — and leave the world pose untouched in between. The grab
            // handle stays the only other pose writer (grab-move only).
            int poseVersion = VRRigDriver.RigPoseVersion;
            if (_placedFromConfig && poseVersion == _facedPoseVersion)
                return; // world-anchored between events: no per-tick zoom/head coupling

            if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
                return;
            Vector3 offset = new(
                WorldUIConfig.SettingsRight.Value,
                WorldUIConfig.SettingsUp.Value,
                WorldUIConfig.SettingsForward.Value);
            Vector3 candidate = anchor + yaw * (offset * worldScale);

            // Item 2: heal a stale/out-of-view persisted offset back into the forward FOV.
            bool healed = PanelPlacement.ClampIntoView(head, worldScale, ref candidate,
                out Quaternion facing);
            _frame.position = candidate;
            _frame.localScale = Vector3.one *
                Mathf.Clamp(WorldUIConfig.SettingsScale.Value, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);

            // Orientation at events only (never per tick — the combat log's test #20 rule).
            // This whole block IS an event now, so the facing is (re)applied here.
            _frame.rotation = facing;
            _facedPoseVersion = poseVersion;
            if (healed)
            {
                PersistLayout();
                if (!_healLogged)
                {
                    _healLogged = true;
                    VRLog.Info("WorldUI", "Settings panel was out of view — healed back into " +
                                          "the forward field of view.");
                }
            }
            else
            {
                _healLogged = false;
            }
            _placedFromConfig = true;
        }
        else if (!WorldUIConfig.SettingsFollow.Value)
        {
            // PINNED + already placed: stay frozen, but heal the EXISTING world pose in if a
            // recenter/MR toggle (pose version bump) left it out of the new forward view (item 2).
            int poseVersion = VRRigDriver.RigPoseVersion;
            if (poseVersion != _facedPoseVersion)
            {
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
                        VRLog.Info("WorldUI", "Settings panel (PINNED) was stranded out of view — " +
                                              "healed back into the forward field of view.");
                    }
                }
                else
                {
                    _healLogged = false;
                }
            }
        }
    }

    /// <summary>
    /// Fresh in-view spawn (item 1/3): a comfortable reading distance in front of the head,
    /// slightly below eye level, upright and facing the head, then persist it. Used on every
    /// open so the panel appears cleanly in front of the player, never overlapping the board
    /// or the combat log.
    /// </summary>
    private void PlaceInView(Camera head)
    {
        if (_frame == null)
            return;
        float worldScale = PanelLayout.WorldScale;
        PanelPlacement.Spawn(head, worldScale, out Vector3 pos, out Quaternion rot);
        _frame.position = pos;
        _frame.rotation = rot;
        _frame.localScale = Vector3.one *
            Mathf.Clamp(WorldUIConfig.SettingsScale.Value, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        _healLogged = false;
        PersistLayout();
    }

    // ---- IPanelGrabOwner ----------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;
    bool IPanelGrabOwner.GrabVisible =>
        _open && _holder != null && _holder.gameObject.activeInHierarchy;
    bool IPanelGrabOwner.GrabCarriesYaw => true; // world-static carry, yaws like the tray/combat log

    void IPanelGrabOwner.OnGrabFinished()
    {
        // Release snaps upright — zero roll/pitch, yaw toward the head at THIS moment — then frozen.
        Camera? head = CanvasConversion.WorldCamera;
        if (head != null && _frame != null)
        {
            Vector3 away = _frame.position - head.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-6f)
                _frame.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        PersistLayout();
    }

    // ---- FOLLOW/PINNED + laser wiring ---------------------------------------------------------

    private void TogglePin()
    {
        bool follow = !WorldUIConfig.SettingsFollow.Value;
        WorldUIConfig.SettingsFollow.Value = follow; // BepInEx persists on set
        if (!follow)
            PersistLayout(); // freeze: re-derives the pin from these offsets on the next open
        ApplyPinVisual();
        VRLog.Info("WorldUI", "Settings panel anchor mode → " +
                              $"{(follow ? "FOLLOW (seat-anchored)" : "PINNED (world-anchored)")}.");
    }

    private void ApplyPinVisual()
    {
        if (_pin == null)
            return;
        bool follow = WorldUIConfig.SettingsFollow.Value;
        _pin.SetState(true, accent: !follow);
        _pin.SetLabel(follow ? Loc.Mod("follow") : Loc.Mod("pinned"));
    }

    /// <summary>
    /// Laser support rides the tray's LaserTargets list while a tray exists and is visible
    /// (CardsDriver ray-tests it); the list dies with each tray, so re-register per tray
    /// INSTANCE. Poke needs none of this (the BoardButton self-registers).
    /// </summary>
    private void TickPin()
    {
        if (_pin == null)
            return;
        PlayTray? tray = PlayTray.Current;
        if (tray != null && !ReferenceEquals(tray, _laserTray) && _pin.Collider != null)
        {
            tray.RegisterLaserTarget(_pin.Collider, _pin);
            _laserTray = tray;
        }
    }

    /// <summary>
    /// Inverse of the FOLLOW placement: the frame pose as table-anchor offsets in real
    /// meters (seat-yaw space, divided by the diorama scale) + the size factor. BepInEx
    /// writes the ConfigFile on set, so the layout survives sessions. Silent (called on
    /// every open) — the open/anchor-mode logs already narrate placement.
    /// </summary>
    private void PersistLayout()
    {
        if (_frame == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return;
        float worldScale = PanelLayout.WorldScale;
        if (worldScale < 1e-5f)
            return;
        Vector3 local = Quaternion.Inverse(yaw) * (_frame.position - anchor) / worldScale;
        WorldUIConfig.SettingsRight.Value = local.x;
        WorldUIConfig.SettingsUp.Value = local.y;
        WorldUIConfig.SettingsForward.Value = local.z;
        WorldUIConfig.SettingsScale.Value =
            Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
    }

    // ---- chord ---------------------------------------------------------------------------

    /// <summary>
    /// P6: fires on RELEASE via the shared <see cref="NonDominantHold"/> tracker —
    /// the same button carries the LONG-hold manual flat-screen chord (FlatScreen,
    /// fires at its threshold while held and marks the press Consumed). A short hold
    /// (≥ ChordHoldSeconds, released before the screen chord fired) toggles the
    /// settings panel; a consumed long hold does nothing extra here.
    /// </summary>
    private void TickChord()
    {
        float hold = WorldUIConfig.SettingsChordHoldSeconds.Value;
        if (hold <= 0f || !NonDominantHold.ReleasedThisFrame || NonDominantHold.Consumed)
            return;
        if (NonDominantHold.ReleasedAfterSeconds < hold)
            return;
        NonDominantHold.Hand?.SendHaptic(HapticPreset.ClickPulse);
        Toggle();
    }

    // ---- construction ----------------------------------------------------------------------

    private void Build()
    {
        // ROOT-CAUSE guard (bug #5): the holder GameObject is a plain scene object (NOT
        // DontDestroyOnLoad), so a scene unload destroys it AND every row/widget it owns,
        // yet this SettingsPanel instance survives on the DontDestroyOnLoad WorldUI driver.
        // SetOpen() then sees the Unity-null _holder and calls Build() AGAIN. If the
        // accumulation lists were not reset first, the rebuild would append a second set of
        // rows/refreshers on top of the stale, destroyed ones — every refresh would deref a
        // destroyed row (NRE flood, aborting the rest of RefreshAll) and the duplicated rows
        // would stack into the "super long / not embedded" layout. Clearing here guarantees
        // exactly ONE clean set per build (Build is only ever reached with _holder == null,
        // so the previous holder and its rows are already gone).
        _refreshers.Clear();
        _rows.Clear();
        _rowVisible.Clear();
        _rowParent = null;
        _rowGate = null;

        MixedReality.Bind(); // MR config may be read below before VRRigDriver's first tick

        BuildFrame();

        _root = new GameObject("Canvas") { layer = 5 };
        var rect = _root.AddComponent<RectTransform>();
        _canvas = _root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = CanvasConversion.WorldCamera;
        _root.AddComponent<GraphicRaycaster>();
        rect.sizeDelta = new Vector2(PanelWidthPx, 100f); // height grows via layout

        // Ride the grab frame: bottom-center pivot so the panel grows UP from the bar
        // (like the tray/combat-log mounts). localScale = real meters per uGUI pixel;
        // the holder's diorama scale and the frame's user size factor stack on top.
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        _root.transform.SetParent(_frame, worldPositionStays: false);
        _root.transform.localPosition = new Vector3(0f, BarGapMeters, 0f);
        _root.transform.localScale = Vector3.one * CanvasMetersPerPixel;

        var bg = _root.AddComponent<Image>();
        bg.color = new Color(0.07f, 0.07f, 0.10f, 0.92f);

        var layout = _root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 12);
        layout.spacing = 4f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        var fitter = _root.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ---- Header: breadcrumb (where am I) + close, spanning both columns --------
        _rowParent = null;
        _rowGate = null;
        var header = Row();
        TextMeshProUGUI crumb = Label(header, "", 18f, bold: true, flexible: true);
        _refreshers.Add(() => crumb.text = "GloomhavenVR   ›   " + NavCatLabel(CurrentNav)
            + (PerBoard() ? "   ›   " + DebugSubCatLabel(CurrentSubCat) : ""));
        Button(header, "X", 40f, () => SetOpen(false));

        // ---- Body: persistent category SIDEBAR | swappable CONTENT pane -----------
        BuildBody(out RectTransform sidebar, out RectTransform content);

        // Sidebar — one always-visible button per top-level category. Poke or laser
        // selects it (plain uGUI Button, same as every other control); the content pane
        // then shows ONLY that category's rows.
        _rowParent = sidebar;
        _rowGate = null;
        for (int c = 0; c < NavCatCount; c++)
            BuildNavButton(c);

        // Everything below builds into the CONTENT column, each row gated to its category
        // (or, for the two element-bearing tabs, to its category + selected element).
        _rowParent = content;

        // ===== Welt — Komfort & Bewegung =====
        GateCat(NavCat.Welt);

        // Table scale: SetScaleMultiplier applies live around the head + persists.
        Stepper(Loc.Mod("table_scale"),
            () => $"{CurrentScaleMultiplier():0.00}x",
            delta =>
            {
                if (ComfortSettings.IsBound)
                    Comfort.SetScaleMultiplier(CurrentScaleMultiplier() + delta * 0.25f);
            });

        // Turn mode + degrees.
        var turnRow = Row();
        Label(turnRow, Loc.Mod("turning"), 16f, flexible: true);
        CycleButton(turnRow, 86f,
            () => ComfortSettings.IsBound ? ComfortSettings.Turn.Value.ToString() : "-",
            () =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.Turn.Value = (TurnMode)(((int)ComfortSettings.Turn.Value + 1) % 3);
            });
        MiniStepper(turnRow,
            () => ComfortSettings.IsBound ? $"{ComfortSettings.SnapTurnDegrees.Value:0}°" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.SnapTurnDegrees.Value =
                    Mathf.Clamp(ComfortSettings.SnapTurnDegrees.Value + delta * 15f, 15f, 90f);
            });

        Stepper(Loc.Mod("table_height"),
            () => ComfortSettings.IsBound ? $"{ComfortSettings.TableHeightOffset.Value:+0.00;-0.00;0.00}m" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.TableHeightOffset.Value =
                    Mathf.Clamp(ComfortSettings.TableHeightOffset.Value + delta * 0.05f, -0.4f, 0.6f);
            });

        // Demeo-style world tilt ([Rig] WorldTiltDegrees, 5° steps, 0-60): the diorama appears
        // tilted toward the player (rig-side counter-rotation — VRRigDriver.TickWorldTilt reads
        // the entry every frame, so stepping applies live; BepInEx persists on set).
        Stepper("Welt-Neigung",
            () => $"{Mathf.Clamp(Plugin.WorldTiltDegrees.Value, 0f, 60f):0}°",
            delta => Plugin.WorldTiltDegrees.Value =
                Mathf.Clamp(Plugin.WorldTiltDegrees.Value + delta * 5f, 0f, 60f));

        Toggle(Loc.Mod("free_movement"),
            () => ComfortSettings.IsBound && ComfortSettings.FreeMovement.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.FreeMovement.Value = v; });

        var grabRow = Row();
        Label(grabRow, Loc.Mod("world_grab"), 16f, flexible: true);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.WorldGrabEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.WorldGrabEnabled.Value = v; });
        Label(grabRow, "rot", 13f);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.RotateEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.RotateEnabled.Value = v; });
        Label(grabRow, "scl", 13f);
        ToggleButton(grabRow,
            () => ComfortSettings.IsBound && ComfortSettings.ScaleEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.ScaleEnabled.Value = v; });

        var recenterRow = Row();
        Button(recenterRow, Loc.Mod("recenter_now"), 0f, Comfort.RequestRecenter, flexible: true);
        // ESCAPE HATCH next to the recenter (incident: "the control board was gone after I walked
        // around and briefly took the headset off"). The per-frame lost-board watchdog
        // (PlayTray.TickLostWatchdog) recovers it by itself, but a board the player cannot find is
        // an immediate hard stop — playing becomes impossible — so there must always be a manual,
        // zero-latency way back. Routed through the driver's request queue so the pose is written
        // on the main thread and the move stays a SANCTIONED one for the issue-C pose watchdog.
        Button(recenterRow, Loc.Mod("recall_board"), 0f, Cards.CardsDriver.RequestBoardRecall, flexible: true);

        Toggle(Loc.Mod("dominant_hand_right"),
            () => !string.Equals(Plugin.PrimaryHand.Value, "Left", StringComparison.OrdinalIgnoreCase),
            v => Plugin.PrimaryHand.Value = v ? "Right" : "Left");

        // ===== Anzeige — Display / Render / Mixed Reality =====
        GateCat(NavCat.Anzeige);

        // Re-spawn / hide the combat log window (item 6): SHOW clears the user-closed flag
        // set by the panel's X button and reconverts it at the persisted pose; HIDE releases
        // it back to its 2D home and keeps it from auto-reappearing.
        Toggle(Loc.Mod("show_combat_log"),
            () => CombatLogSurface.UserVisible,
            v => CombatLogSurface.SetUserVisible(v, "settings"));

        // MSAA on the VR eye textures ([RenderQuality] MsaaLevel — Rig.RenderQuality):
        // Cycles Off → 2x → 4x → 8x. Applies IMMEDIATELY (the rig's per-frame RenderQuality
        // tick re-asserts it and re-allocates the eye textures live — no restart).
        var msaaRow = Row();
        Label(msaaRow, "MSAA", 16f, flexible: true);
        CycleButton(msaaRow, 100f, RenderQuality.MsaaLabel, RenderQuality.CycleMsaa);

        // Supersampling ([RenderQuality] EyeResolutionScale) — eye-texture resolution scale,
        // 0.8–2.0 in 0.1 steps. The working AA lever under VDXR (MSAA capped at 1x there).
        var ssRow = Row();
        Label(ssRow, "Supersampling", 16f, flexible: true);
        MiniStepper(ssRow, RenderQuality.EyeScaleLabel, RenderQuality.StepEyeScale);
        var msaaNote = Row(28f);
        Label(msaaNote, "MSAA wirkt nicht unter VDXR – Supersampling nutzen", 12f, flexible: true);

        Toggle(Loc.Mod("disable_post"),
            () => Plugin.DisablePostProcessing.Value,
            v => Plugin.DisablePostProcessing.Value = v);

        Toggle(Loc.Mod("world_ui_surfaces"),
            () => WorldUIConfig.Master.Value,
            v => WorldUIConfig.Master.Value = v);

        Toggle(Loc.Mod("board_far_ray"),
            () => BoardConfigSafe(() => Board.BoardConfig.ForceFarMode.Value),
            v => { if (Board.BoardConfig.ForceFarMode != null) Board.BoardConfig.ForceFarMode.Value = v; });

        // Optional game wall see-through ([Compat] WallFade): ON lets the game's own
        // view-dependent wall fade run (it follows the HMD); OFF (default) keeps walls solid.
        // Applies LIVE — the Harmony postfix consults the entry every frame. USER-FACING, and the
        // old single-toggle "Wände" tab is gone (2026-07), so this normal-user switch now lives here
        // in "Anzeige"; the developer-grade fade FRACTIONS live under Debug → Wandüberblendung.
        Toggle("Wände durchsichtig",
            () => Plugin.WallFade.Value,
            v => Plugin.WallFade.Value = v);

        // User 7c: action-phase element hints (the tooltip parked at the board's top-left)
        // on/off. Live: WorldTooltips.LateTick reads WorldUIConfig.ActionElementHints every tick.
        Toggle("Element-Hinweise",
            () => WorldUIConfig.ActionElementHints.Value,
            v => WorldUIConfig.ActionElementHints.Value = v);

        var startNote = Row(22f);
        Label(startNote, Loc.Mod("applies_next_start"), 12f, flexible: true);

        // MR chroma-key mode (item 7): disables all skyboxes and clears the sky/background to
        // the key color so a compositor (Virtual Desktop) can passthrough-composite the room.
        Toggle(Loc.Mod("mixed_reality"),
            () => MixedReality.Enabled.Value,
            v => MixedReality.Enabled.Value = v);
        var mrColorRow = Row();
        Label(mrColorRow, Loc.Mod("key_color"), 16f, flexible: true);
        CycleButton(mrColorRow, 100f, () => MixedReality.KeyColorName, MixedReality.CycleKeyColor);

        // Which control-board MODEL is loaded (Oak/Steel/Bronze) — a genuinely user-facing choice
        // (issue 2 keep-list), so it lives here in the default view. It is the SAME [Cards] Board
        // entry the Debug per-board tuning selects, so switching here also retargets the Debug pane.
        // Live: CardsDriver rebuilds the tray on change.
        var boardModelRow = Row();
        Label(boardModelRow, Loc.Mod("control_board"), 16f, flexible: true);
        CycleButton(boardModelRow, 100f,
            () => CardsConfig.Board.Value.ToString(),
            () => CardsConfig.Board.Value = (ControlBoard)(((int)CardsConfig.Board.Value + 1) % 3));

        // Wall-fade thresholds (live [WallFade] config — WallSegmentFade re-reads the clamped
        // accessors every evaluation tick). Issue 2: developer-grade fractions, so each row gates
        // itself to Debug → Wandüberblendung (WallFadeRowsVisible). Built here in the content column
        // for convenience; the build order is irrelevant since every row registers its own gate. The
        // user-facing on/off toggle moved up into "Anzeige" (the one-row "Wände" tab is gone).
        WallFadeTuning.Bind();
        AddWallFadeRow("Einblenden", WallFadeTuning.OnFraction, 0.05f, 0.05f, 0.95f,
            v => $"{v * 100f:0}%");
        AddWallFadeRow("Ausblenden", WallFadeTuning.OffFraction, 0.05f, 0.01f, 0.95f,
            v => $"{v * 100f:0}%");
        AddWallFadeRow("Wieder ein (bewegt)", WallFadeTuning.ExitDwellMoved, 0.5f, 0.1f, 60f,
            v => $"{v:0.0}s");
        AddWallFadeRow("Wieder ein (ruhig)", WallFadeTuning.ExitDwellStationary, 0.5f, 0.1f, 120f,
            v => $"{v:0.0}s");

        // ===== Avatar — Mehrspieler-Erscheinung =====
        GateCat(NavCat.Avatar);

        // Multiplayer avatar: head mask (writes [Net] MaskId — synchronized), a local
        // self-preview mirror ([Net] MirrorEnabled), and how much of OTHER players' boards
        // this client renders ([Net] RemoteBoards). Force the [Net] entries bound so the
        // pickers work even with the networking hook off.
        Net.NetModule.BindConfig();
        var maskRow = Row();
        Label(maskRow, Loc.Mod("head_mask"), 16f, flexible: true);
        CycleButton(maskRow, 120f, MaskLabel, CycleMask);

        // Hand style: cycles Glove → Plate → Arcane (writes [Hands] HandStyle; HandsDriver
        // rebuilds the hand visuals live). Synchronized via the avatar rig packet. Also
        // reachable as the "Stil" row under Hände/Figuren/Handgelenk (same bind).
        var handStyleRow = Row();
        Label(handStyleRow, Loc.Mod("hands"), 16f, flexible: true);
        CycleButton(handStyleRow, 120f, HandStyleLabel, CycleHandStyle);

        Toggle(Loc.Mod("mirror"),
            () => Net.NetModule.MirrorEnabled != null && Net.NetModule.MirrorEnabled.Value,
            v => { if (Net.NetModule.MirrorEnabled != null) Net.NetModule.MirrorEnabled.Value = v; });

        var remoteBoardsRow = Row();
        Label(remoteBoardsRow, Loc.Mod("remote_boards"), 16f, flexible: true);
        CycleButton(remoteBoardsRow, 150f, RemoteBoardsLabel, CycleRemoteBoards);

        // ===== Debug elements folded in from the former top-level per-style tabs =====
        // These build into the CONTENT column (still set from the GateCat sections above) but each
        // row gates itself to Debug → its element (Hände-Offsets / Figuren-Offsets / Handgelenk), so
        // they only appear when that Debug element is selected. Build order is irrelevant.
        BuildHandsCategory();   // Debug → Hände-Offsets (per-style seat + card-fan geometry)
        BuildFiguresCategory(); // Debug → Figuren-Offsets (per-style held-mini pose)
        BuildWristCategory();   // Debug → Handgelenk (per-style wrist-HUD pose)

        // ===== Debug — the sole element-bearing tuning tab (board panels + button geometry + fade + per-style) =====
        BuildElementTuning();

        _rowParent = null;
        _rowGate = null;

        // Single visibility pass: each gated row shown iff its own predicate (category, or
        // category + selected element) is satisfied. Never more than one category's rows
        // are visible at once, so the pane stays a short, scannable list.
        _refreshers.Add(() =>
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                GameObject go = _rows[i];
                // A row can be Unity-null if its holder was destroyed out from under us
                // (scene unload) while the panel instance survived; skip rather than throw.
                if (go == null)
                    continue;
                bool show = _rowVisible[i]();
                if (go.activeSelf != show)
                    go.SetActive(show);
            }
        });

        // Mod layer in VR (inline 5s remain the dev-sim fallback; CAMERA-POLICY §2).
        if (_holder == null)
            return;
        VRLayers.Apply(_holder.gameObject);
        _holder.gameObject.SetActive(false);
    }

    // ---- sidebar + content scaffold (2026-07 redesign) ----------------------------------------

    /// <summary>Set the ambient row gate to "this sidebar category is selected".</summary>
    private void GateCat(NavCat c) => _rowGate = () => _navCat == (int)c;

    /// <summary>Localized sidebar/breadcrumb name of a top-level category (reuses existing Loc keys).</summary>
    private static string NavCatLabel(NavCat c) => c switch
    {
        NavCat.Welt => "Welt",
        NavCat.Anzeige => Loc.Mod("display"),
        NavCat.Avatar => Loc.Mod("avatar"),
        NavCat.Debug => "Debug",
        _ => c.ToString(),
    };

    /// <summary>
    /// Build the two-column body: a fixed-width category sidebar on the left and a
    /// flexible-width content pane on the right, both riding the root's vertical layout.
    /// </summary>
    private void BuildBody(out RectTransform sidebar, out RectTransform content)
    {
        var bodyGo = new GameObject("Body") { layer = 5 };
        bodyGo.AddComponent<RectTransform>();
        bodyGo.transform.SetParent(_root!.transform, worldPositionStays: false);
        var h = bodyGo.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childAlignment = TextAnchor.UpperLeft;
        var bodyRect = (RectTransform)bodyGo.transform;

        sidebar = BuildColumn(bodyRect, "Sidebar", 132f);
        content = BuildColumn(bodyRect, "Content", 0f);
    }

    /// <summary>One body column (VerticalLayoutGroup). fixedWidth &gt; 0 pins the width (sidebar); 0 = flexible (content).</summary>
    private static RectTransform BuildColumn(RectTransform parent, string name, float fixedWidth)
    {
        var go = new GameObject(name) { layer = 5 };
        var rect = go.AddComponent<RectTransform>();
        go.transform.SetParent(parent, worldPositionStays: false);
        if (fixedWidth > 0f)
        {
            // Subtle inset behind the sidebar so it reads as a distinct nav column.
            var img = go.AddComponent<Image>();
            img.color = new Color(0.04f, 0.04f, 0.07f, 0.85f);
            img.raycastTarget = false;
        }
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4f;
        v.padding = new RectOffset(fixedWidth > 0f ? 6 : 4, fixedWidth > 0f ? 6 : 4, 4, 4);
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childAlignment = TextAnchor.UpperLeft;
        var le = go.AddComponent<LayoutElement>();
        if (fixedWidth > 0f)
        {
            // Issue 1 (sidebar buttons rescale on page change): PIN the sidebar column so the
            // content pane's per-page ContentSizeFitter can never re-fit it. minWidth == preferredWidth
            // fixes the width in EVERY HorizontalLayoutGroup regime (surplus goes only to flexible
            // children, and the min==pref clamp survives the shrink regime), and flexibleWidth/Height = 0
            // guarantees the column is never stretched to the taller content pane's width OR height —
            // so the nav buttons render pixel-identical whatever page is open.
            le.preferredWidth = fixedWidth;
            le.minWidth = fixedWidth;
            le.flexibleWidth = 0f;
            le.flexibleHeight = 0f;
        }
        else
        {
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;
        }
        return rect;
    }

    /// <summary>One sidebar category button: selects the tab (poke/laser) and marks the active one.</summary>
    private void BuildNavButton(int idx)
    {
        var row = Row(30f);
        (Button btn, TextMeshProUGUI txt) = Button(row, "", 0f, () =>
        {
            _navCat = idx;
            _subCatListOpen = false;  // switching tabs collapses both accordions
            _elementListOpen = false;
            RefreshAll();
        }, flexible: true);
        var img = (Image)btn.targetGraphic;
        _refreshers.Add(() =>
        {
            bool sel = _navCat == idx;
            txt.text = NavCatLabel((NavCat)idx);
            txt.fontStyle = sel ? FontStyles.Bold : FontStyles.Normal;
            img.color = sel
                ? new Color(0.28f, 0.34f, 0.48f, 0.98f)
                : new Color(0.15f, 0.16f, 0.21f, 0.95f);
        });
    }

    // ---- per-style categories (Hände / Figuren / Handgelenk) ----------------------------------

    /// <summary>
    /// Debug → Hände-Offsets (GLOBAL, both hands; per HAND STYLE) + the card-fan geometry. Each row
    /// edits the style CURRENTLY worn; the "Stil" row cycles it right here so all three sets are
    /// reachable without leaving the element. Live: VRHand.SyncVisualOffset re-reads the ACTIVE
    /// style's entries every frame (a style switch re-seats/rescales instantly too).
    /// </summary>
    private void BuildHandsCategory()
    {
        HandsConfig.Bind(); // idempotent — so the seat rows work even if the Hands module has not inited
        _rowGate = HandOffsetRowsVisible;
        var handsStyleRow = Row();
        Label(handsStyleRow, "Stil", 16f, flexible: true);
        CycleButton(handsStyleRow, 120f, HandStyleLabel, CycleHandStyle);
        _rowGate = HandOffsetRowsVisible;
        var handsNote = Row(18f);
        Label(handsNote, "Alle Werte gelten pro Stil", 12f, flexible: true);

        AddStyleStepper(Loc.Mod("size"), () => Plugin.HandStyleScale, 0.02f, 0.2f, 3f,
            v => $"{v:0.00}x", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_x"), () => HandsConfig.StyleSeatLateral, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_y"), () => HandsConfig.StyleSeatVertical, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_z"), () => HandsConfig.StyleSeatForward, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm", HandOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("hand_pitch"), () => HandsConfig.StyleSeatPitch, 1f, -90f, 90f,
            v => $"{v:0}°", HandOffsetRowsVisible);

        // Card fan geometry ([Cards] fan entries, GLOBAL) — a labeled sub-section of Hände-Offsets.
        // Live-applied by CardsDriver (relayout) so tuning updates the open fan immediately.
        _rowGate = HandOffsetRowsVisible;
        Section(Loc.Mod("cat_fan"));
        AddFanStepper(Loc.Mod("fan_step"), CardsConfig.FanPerCardStepDegrees, 1f, 2f, 40f, v => $"{v:0}°");
        AddFanStepper(Loc.Mod("fan_arc"), CardsConfig.FanArcSweepDegrees, 1f, 20f, 180f, v => $"{v:0}°");
        AddFanStepper(Loc.Mod("fan_radius"), CardsConfig.FanEffectiveRadius, 0.002f, 0.05f, 0.4f,
            v => $"{v * 1000f:0}mm");
        AddFanStepper(Loc.Mod("fan_split"), CardsConfig.FanHoverSplitScale, 0.05f, 0.5f, 3f, v => $"{v:0.00}x");
        // Palm-gate reveal roll angle + fan-out animation length (all live).
        AddFanStepper("Aufdecken ein °", CardsConfig.RevealEnterDegrees, 5f, 15f, 85f, v => $"{v:0}°");
        AddFanStepper("Aufdecken aus °", CardsConfig.RevealExitDegrees, 5f, 5f, 80f, v => $"{v:0}°");
        AddFanStepper("Öffnungszeit", CardsConfig.FanOpenDuration, 0.02f, 0f, 0.6f, v => $"{v * 1000f:0}ms");
        // Depth curvature: side cards recede into (or bulge OUT of) depth with many cards (live).
        // User 4: min is −0.12 so the curvature can also go the OTHER direction (a separate worker
        // widens the bind's own clamp range to match).
        AddFanStepper("Krümmung Tiefe", CardsConfig.FanSideDepthCurve, 0.005f, -0.12f, 0.12f, v => $"{v * 1000f:0}mm");
        AddFanStepper("Krümmung Kurve", CardsConfig.FanCurvePower, 0.1f, 0.5f, 4f, v => $"{v:0.0}");
        _rowGate = HandOffsetRowsVisible;
        var curveMinRow = Row();
        Label(curveMinRow, "Krümmung ab Karten", 16f, flexible: true);
        MiniStepper(curveMinRow,
            () => $"{CardsConfig.FanCurveMinCards.Value}",
            d =>
            {
                ConfigEntry<int> e = CardsConfig.FanCurveMinCards;
                e.Value = Mathf.Clamp(e.Value + (int)Mathf.Sign(d), 1, 12);
            });
        _rowGate = HandOffsetRowsVisible;
        var gazeRow = Row();
        Label(gazeRow, "Blick-Neigung", 16f, flexible: true);
        ToggleButton(gazeRow, () => CardsConfig.FanGazeBias.Value, v => CardsConfig.FanGazeBias.Value = v);
    }

    /// <summary>
    /// Debug → Figuren-Offsets (GLOBAL across boards; per HAND STYLE): the HELD board-figure pose
    /// docked between thumb and index of the hand MESH (geometry differs per style). Live:
    /// FigureGrabConfig re-poses the held mini immediately. "Aufrecht" is a global MODE toggle.
    /// </summary>
    private void BuildFiguresCategory()
    {
        FigureGrabConfig.Bind();
        _rowGate = FigureOffsetRowsVisible;
        var figStyleRow = Row();
        Label(figStyleRow, "Stil", 16f, flexible: true);
        CycleButton(figStyleRow, 120f, HandStyleLabel, CycleHandStyle);
        _rowGate = FigureOffsetRowsVisible;
        var figNote = Row(18f);
        Label(figNote, "Alle Werte gelten pro Stil", 12f, flexible: true);

        AddFigureToggle(Loc.Mod("fig_upright"), FigureGrabConfig.HeldUpright);
        AddStyleStepper(Loc.Mod("fig_x"), () => FigureGrabConfig.StyleHeldOffsetSide, 0.002f,
            -0.2f, 0.2f, v => $"{v * 1000f:0}mm", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_y"), () => FigureGrabConfig.StyleHeldOffsetUp, 0.002f,
            -0.2f, 0.2f, v => $"{v * 1000f:0}mm", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_z"), () => FigureGrabConfig.StyleHeldOffsetForward, 0.002f,
            -0.2f, 0.2f, v => $"{v * 1000f:0}mm", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_tilt"), () => FigureGrabConfig.StyleHeldTiltDegrees, 5f,
            -180f, 180f, v => $"{v:0}°", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_yaw"), () => FigureGrabConfig.StyleHeldFaceYawDegrees, 5f,
            -180f, 180f, v => $"{v:0}°", FigureOffsetRowsVisible);
        AddStyleStepper(Loc.Mod("fig_scale"), () => FigureGrabConfig.StyleHeldScale, 0.1f,
            0.2f, 5f, v => $"{v:0.00}x", FigureOffsetRowsVisible);
    }

    /// <summary>
    /// Debug → Handgelenk (per HAND STYLE): the wrist overview HUD's TILT (pitch/yaw/roll) and
    /// POSITION offset (X/Y/Z). The accessors read/write the ACTIVE hand style's [WristHud] entry, and
    /// WristHud re-applies every Tick — so both a nudge and a style switch move the HUD immediately.
    /// </summary>
    private void BuildWristCategory()
    {
        _rowGate = WristRowsVisible;
        var wristStyleRow = Row();
        Label(wristStyleRow, "Stil", 16f, flexible: true);
        CycleButton(wristStyleRow, 120f, HandStyleLabel, CycleHandStyle);
        _rowGate = WristRowsVisible;
        var wristNote = Row(18f);
        Label(wristNote, "Alle Werte gelten pro Stil", 12f, flexible: true);

        AddWristStepper(Loc.Mod("wrist_pitch"), () => WristHud.PitchDeg, v => WristHud.PitchDeg = v, 2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_yaw"),   () => WristHud.YawDeg,   v => WristHud.YawDeg = v,   2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_roll"),  () => WristHud.RollDeg,  v => WristHud.RollDeg = v,  2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_x"), () => WristHud.OffsetX, v => WristHud.OffsetX = v, 0.002f, v => $"{v * 1000f:0}mm");
        AddWristStepper(Loc.Mod("wrist_y"), () => WristHud.OffsetY, v => WristHud.OffsetY = v, 0.002f, v => $"{v * 1000f:0}mm");
        AddWristStepper(Loc.Mod("wrist_z"), () => WristHud.OffsetZ, v => WristHud.OffsetZ = v, 0.002f, v => $"{v * 1000f:0}mm");
    }

    // ---- Debug — Board tuning (Part E) ---------------------------------------------------------

    /// <summary>
    /// The in-VR DEBUG MENU (Part E): live-tune every board-attached element PER BOARD and save
    /// to config. Built from the existing panel helpers; every stepper closes over the per-board
    /// <see cref="CardsConfig"/> entries, so writing a value both persists (BepInEx) AND live-applies
    /// (CardsDriver subscribes to each entry's SettingChanged). Gated behind [Cards] DebugMenu — the
    /// rows are built once but shown only while the toggle is on (the 0.25 s refresher flips them).
    /// </summary>
    private void BuildElementTuning()
    {
        // Issue 5: the "Board-Justierung aktivieren" toggle is GONE — board tuning is now ALWAYS
        // active. [Cards] DebugMenu stays bound (default ON) for config compatibility but the panel
        // never gates on it and never lets the user switch it off. The deep tuning is instead tucked
        // away under the dedicated "Debug" sidebar category (issue 2), which is the real
        // discoverability the user wanted.

        // Sub-category chooser — ACCORDION (user 5b: cluster the ~20 Debug elements so the pane
        // stays scannable). Shown for the whole Debug category; the header row shows the selected
        // sub-category, pressing it expands one option row PER sub-category right below, pressing an
        // option selects it (and resets the element chooser to that sub-category's first element).
        // Plain uGUI buttons via the shared helpers, so poke AND laser keep working unchanged.
        _rowGate = PerBoard;
        var subCatRow = Row();
        Label(subCatRow, "Bereich", 16f, flexible: true);
        CycleButton(subCatRow, 170f,
            () => DebugSubCatLabel(CurrentSubCat) + (_subCatListOpen ? " -" : " +"),
            () => _subCatListOpen = !_subCatListOpen);

        for (int i = 0; i < DebugSubCatCount; i++)
        {
            int idx = i; // capture per row
            _rowGate = () => _subCatListOpen && PerBoard();
            var subOptRow = Row(28f);
            Label(subOptRow, "", 13f); // fixed 28px gutter — reads as an indented sub-row
            (Button _, TextMeshProUGUI subOptText) = Button(subOptRow, "", 0f, () =>
            {
                _debugSubCat = idx;
                _subCatListOpen = false; // select + collapse
                _elementListOpen = false; // the element chooser now scopes to the new sub-category
                RefreshAll();
            }, flexible: true);
            _refreshers.Add(() =>
            {
                bool selected = Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1) == idx;
                subOptText.text = (selected ? "> " : "") + DebugSubCatLabel((DebugSubCat)idx);
            });
        }

        // Board cycle (Oak/Steel/Bronze) — the per-board element tuning edits the SELECTED
        // board. Hidden for the GLOBAL ButtonTuning elements (Rundenknöpfe / Zahnrad & Fixiert /
        // Knopf-Farben etc. — their values ride every board).
        _rowGate = () => PerBoard() && !ElementIsGlobalTuning(CurrentElement());
        var boardRow = Row();
        Label(boardRow, Loc.Mod("board"), 16f, flexible: true);
        CycleButton(boardRow, 100f,
            () => CardsConfig.Board.Value.ToString(),
            () => CardsConfig.Board.Value = (ControlBoard)(((int)CardsConfig.Board.Value + 1) % 3));

        // Element chooser — ACCORDION scoped to the selected SUB-CATEGORY (user 5b): the header row
        // shows the selected element; pressing it expands one option row PER element of the CURRENT
        // SUB-CATEGORY right below (the panel's ContentSizeFitter grows around them, exactly like
        // every conditional debug row); pressing an option selects it and collapses the list. Hidden
        // for single-element sub-categories (Welt-Tuning) where a chooser would be a no-op. Plain uGUI
        // buttons via the shared helpers, so poke AND laser keep working unchanged.
        _rowGate = () => PerBoard() && CurrentSubCatElements.Length > 1;
        var elemRow = Row();
        Label(elemRow, Loc.Mod("element"), 16f, flexible: true);
        CycleButton(elemRow, 150f,
            () => DebugElementLabel(CurrentElement()) + (_elementListOpen ? " -" : " +"),
            () => _elementListOpen = !_elementListOpen);

        // One option row per possible element slot (built once for the LARGEST sub-category; each
        // row's visibility predicate + label refresher re-scope it to the current sub-category, so
        // sub-category switches never rebuild anything).
        int maxElements = 0;
        foreach (DebugElement[] els in SubCatElements)
        {
            if (els.Length > maxElements)
                maxElements = els.Length;
        }
        for (int i = 0; i < maxElements; i++)
        {
            int idx = i; // capture per row
            _rowGate = () => _elementListOpen && PerBoard()
                             && CurrentSubCatElements.Length > 1
                             && idx < CurrentSubCatElements.Length;
            var optRow = Row(28f);
            Label(optRow, "", 13f); // fixed 28px gutter — reads as an indented sub-row
            (Button _, TextMeshProUGUI optText) = Button(optRow, "", 0f, () =>
            {
                DebugElement[] els = CurrentSubCatElements;
                if (idx < els.Length)
                    _subCatElement[Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1)] = idx;
                _elementListOpen = false; // select + collapse
                RefreshAll();
            }, flexible: true);
            _refreshers.Add(() =>
            {
                DebugElement[] els = CurrentSubCatElements;
                if (idx >= els.Length)
                    return; // row is hidden by its predicate anyway
                bool selected =
                    Mathf.Clamp(_subCatElement[Mathf.Clamp(_debugSubCat, 0, DebugSubCatCount - 1)], 0, els.Length - 1) == idx;
                optText.text = (selected ? "> " : "") + DebugElementLabel(els[idx]);
            });
        }

        // X / Y / Z offset steppers (mm), each drives the selected element's active-board offset.
        AddOffsetStepper("X", 0);
        AddOffsetStepper("Y", 1);
        AddOffsetStepper("Z (proud)", 2);

        // Size / Scale stepper — hidden for elements with no size (Overlays, Initiative).
        _rowGate = () => PerBoard() && ElementHasSize(CurrentElement());
        var sizeRow = Row();
        Label(sizeRow, Loc.Mod("size"), 16f, flexible: true);
        MiniStepper(sizeRow, FormatSize, StepSize);

        // Objectives-only WIDTH stepper (user request): widen the scenario TASK dock so its
        // progress bar reads longer. Separate from the uniform "Größe" scale above — this scales
        // ONLY the fit width budget (the panel grows leftward into open space). mm readout of the
        // effective width budget, consistent with the offset rows.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Objectives;
        var objWidthRow = Row();
        Label(objWidthRow, "Breite", 16f, flexible: true);
        MiniStepper(objWidthRow, FormatObjectivesWidth, StepObjectivesWidth);

        // Spacing stepper — group gap (Rest disc gap / Confirm-Undo gap / inter-pile gap; Active COL step).
        _rowGate = () => PerBoard() && ElementHasSpacing(CurrentElement());
        var spacingRow = Row();
        Label(spacingRow, Loc.Mod("spacing"), 16f, flexible: true);
        MiniStepper(spacingRow, FormatSpacing, StepSpacing);

        // Active-only ROW step stepper (the grid's vertical spacing).
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Active;
        var rowGapRow = Row();
        Label(rowGapRow, Loc.Mod("row_gap"), 16f, flexible: true);
        MiniStepper(rowGapRow, FormatActiveRowStep, StepActiveRowStep);

        // Shape cycle — shown only for the button GROUPS (Rest / Generic): flip Round <-> Square.
        _rowGate = () => PerBoard() && ElementHasShape(CurrentElement());
        var shapeRow = Row();
        Label(shapeRow, Loc.Mod("shape"), 16f, flexible: true);
        CycleButton(shapeRow, 100f, FormatShape, FlipShape);

        // Board-only Tilt / Yaw row (shown only when Element == Board).
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Board;
        var tiltYawRow = Row();
        Label(tiltYawRow, Loc.Mod("tilt_yaw"), 16f, flexible: true);
        MiniStepper(tiltYawRow,
            () => $"{CardsConfig.BoardTilt(CardsConfig.CurrentBoard).Value:0}°",
            d =>
            {
                ConfigEntry<float> e = CardsConfig.BoardTilt(CardsConfig.CurrentBoard);
                e.Value = Mathf.Clamp(e.Value + d * 1f, 0f, 90f);
            });
        MiniStepper(tiltYawRow,
            () => $"{CardsConfig.BoardYaw(CardsConfig.CurrentBoard).Value:0}°",
            d =>
            {
                ConfigEntry<float> e = CardsConfig.BoardYaw(CardsConfig.CurrentBoard);
                e.Value += d * 5f;
            });

        // Initiative-only 3D depth row (shown only when Element == Initiative): live-tune the
        // initiative track's front-to-back portrait spread cap ([WorldUI] InitiativeDepthMaxSpreadPx,
        // px). InitiativeTrackSurface.NormalizeDepth re-reads it every tick and re-clamps, so the
        // recession updates live (0 = flat). Analogous to the Board Tilt/Yaw row above.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Initiative;
        var initDepthRow = Row();
        Label(initDepthRow, "3D-Tiefe", 16f, flexible: true);
        MiniStepper(initDepthRow,
            () => $"{WorldUIConfig.InitiativeDepthMaxSpreadPx.Value:0}px",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.InitiativeDepthMaxSpreadPx;
                e.Value = Mathf.Clamp(e.Value + d * 1f, 0f, 40f);
            });

        // Initiative-only ENEMY-INFO CLEARANCE row (shown only when Element == Initiative): the
        // GLOBAL [WorldUI] EnemyRevealBoardClearance — how far the enemy round reveal must clear
        // the control board's top edge (real cm, measured AT the board). The reveal's content IS
        // the initiative track's enemyCardsHolder, so it belongs to this element; GLOBAL but edited
        // from an element's rows exactly like the 3D-depth row above (the clearance is derived from
        // the board's LIVE measured pose, so it already rides every board — no per-board entry).
        // EnemyRevealSurface re-reads the entry on every plant/follow step, so the next reveal (and
        // the next lazy-follow glide of a standing one) uses the new value; BepInEx persists on set.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Initiative;
        var revealClearRow = Row();
        Label(revealClearRow, "Gegnerinfo-Abstand", 16f, flexible: true);
        MiniStepper(revealClearRow,
            () => $"{WorldUIConfig.EnemyRevealBoardClearance.Value * 100f:0}cm",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.EnemyRevealBoardClearance;
                e.Value = Mathf.Clamp(e.Value + d * 0.01f, 0f, 0.5f);
            });

        // Decision-only ROW-GAP row (shown only when Element == Decision): the GLOBAL
        // [WorldUI] DecisionRowGapPx — target vertical gap (uGUI px) between a docked
        // decision prompt's text block and its button row. GLOBAL but edited from this
        // element's rows (precedent: the initiative 3D-depth row above). Live:
        // DecisionDockSurface subscribes SettingChanged and re-compresses an OPEN dock
        // immediately; BepInEx persists on set.
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Decision;
        var decisionGapRow = Row();
        Label(decisionGapRow, "Entscheidungs-Abstand", 16f, flexible: true);
        MiniStepper(decisionGapRow,
            () => $"{WorldUIConfig.DecisionRowGapPx.Value:0}px",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.DecisionRowGapPx;
                e.Value = Mathf.Clamp(e.Value + d * 4f, 0f, 120f);
            });

        // Piles-only BROWSE-fan anchor rows (shown only when Element == Piles): live-tune where
        // the poke-toggle pile browse fan floats above the board — [Cards] BrowseFanOffset,
        // board-local meters ADDED to the fixed above-board base. GLOBAL (not per-board): the
        // anchor is board-LOCAL, so it already rides each board's pose/scale — precedent: the
        // global initiative 3D-depth row above. PileBrowser.Tick re-reads the entry every frame
        // while a browse fan is open, so nudging a stepper moves the open fan immediately.
        AddBrowseOffsetStepper("Browse X", 0);
        AddBrowseOffsetStepper("Browse Y", 1);
        AddBrowseOffsetStepper("Browse Z", 2);

        // Reset element (element-bearing categories only — resets the selected element). Hidden for
        // the per-style folded-in elements (Hände-Offsets / Figuren-Offsets / Handgelenk): they carry
        // no per-board offset entry to reset and never had a reset control as top-level tabs.
        _rowGate = () => PerBoard() && !ElementIsPerStyle(CurrentElement());
        var actionRow = Row();
        Button(actionRow, Loc.Mod("reset_element"), 0f, ResetDebugElement, flexible: true);

        // ButtonTuning elements of the "Tasten" category (request C + the 2026-07 category
        // split): three first-class GLOBAL elements, each bound to ITS OWN cfg section so a
        // value can never leak across button categories, and EVERY stepper shows a real
        // number (the 0="Auto" sentinel is gone — the numeric defaults ARE the authored
        // values). Rows show while their element is selected. Every entry live-applies
        // WITHOUT restart: writing bumps ButtonTuning.Version and PlayTray.TickStatus /
        // ButtonCluster.Tick rebuild the affected caps on their next tick. External writes
        // (cfg edit/reload) refresh the rows via ButtonTuning.Changed (subscribed in the
        // constructor) on top of the 0.25 s refresh cadence.
        ButtonTuning.Bind();

        // Tasten → Rundenknöpfe ([RoundButtons] — the transient turn-flow cluster).
        AddButtonTuningRow("Versatz X", ButtonTuning.RoundOffsetX, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Versatz Y", ButtonTuning.RoundOffsetY, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        // User: "the Rundenknöpfe have no Versatz Z" — out-of-plane offset, + = toward the player.
        AddButtonTuningRow("Versatz Z", ButtonTuning.RoundOffsetZ, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        // Shape cycle for the transient group (Round puck <-> Square keycap).
        if (ButtonTuning.RoundShape != null)
        {
            ConfigEntry<ButtonShape> shapeEntry = ButtonTuning.RoundShape;
            _rowGate = RoundButtonRowsVisible;
            var transientShapeRow = Row();
            Label(transientShapeRow, Loc.Mod("shape"), 16f, flexible: true);
            CycleButton(transientShapeRow, 100f,
                () => ShapeLabel(shapeEntry.Value),
                () => shapeEntry.Value = shapeEntry.Value == ButtonShape.Round
                    ? ButtonShape.Square
                    : ButtonShape.Round);
        }
        AddButtonTuningRow("Kappengröße", ButtonTuning.RoundCapSize, 0.002f, 0.015f, 0.09f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Breite", ButtonTuning.RoundWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.RoundHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.RoundDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.RoundTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", RoundButtonRowsVisible);

        // Debug → Generisch ([BoardButtons] — the Confirm/Undo keycaps). Issue 6: these are the
        // SAME physical caps as the Generic element, so the exact rectangle side lengths (independent
        // Breite/Höhe) plus Tiefe/Hub now appear right under "Generisch" — no separate Boardtasten
        // element. For SQUARE Confirm/Undo (the default GenericButtonShape) these Width/Height are the
        // authoritative cap size (PlayTray.BuildButtons); the round "Größe" diameter row above only
        // drives the Round shape, exactly like the Rest element pairs a diameter with [RestButtons] W/H.
        AddButtonTuningRow("Breite", ButtonTuning.BoardWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.BoardHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.BoardDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.BoardTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", BoardButtonRowsVisible);

        // Tasten → Zahnrad & Fixiert ([BoardDashboard] — gear + follow/pin plates; the two
        // plates are authored at different widths, so each keeps its own width row).
        AddButtonTuningRow("Breite Zahnrad", ButtonTuning.DashGearWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Breite Fixiert", ButtonTuning.DashPinWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.DashHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.DashDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.DashTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", BoardDashboardRowsVisible);

        // Tasten → Ruhetasten ([RestButtons] — short/long rest keycaps; square caps use
        // W/H, round discs keep the per-board diameter row above; depth/travel both shapes).
        AddButtonTuningRow("Breite", ButtonTuning.RestWidth, 0.005f, 0.02f, 0.20f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);
        AddButtonTuningRow("Höhe", ButtonTuning.RestHeight, 0.005f, 0.015f, 0.20f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);
        AddButtonTuningRow("Tiefe", ButtonTuning.RestDepth, 0.002f, 0.006f, 0.08f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);
        AddButtonTuningRow("Hub", ButtonTuning.RestTravel, 0.001f, 0.002f, 0.02f,
            v => $"{v * 1000f:0}mm", RestButtonTuningRowsVisible);

        // Tasten → Knopf-Farben ([ButtonColors] — user 4 + user 1): the keycap LABEL text colour +
        // engrave outline + underlay shadow, and a per-category cap-FACE tint. The PRIMARY control is
        // now a LASER-CLICKABLE COLOUR PALETTE per target (user 1: "change the colour directly with
        // the laser instead of RGB values"). AddColorPalette lays out a grid of swatch buttons on the
        // panel canvas (same Image/Button interaction path as every other control, so poke + laser
        // both work); clicking a swatch writes its RGB into the target's three *R/G/B binds at once,
        // which bumps ButtonTuning.Version → NativeButtonSkin re-skins the caps live (keycaps on the
        // Version rebuild, cluster per-tick). The two engrave toggles + Konturbreite stay as steppers;
        // the reset case (restores every bind) is unchanged.
        _rowGate = ButtonColorRowsVisible;
        Section("Text");
        AddColorPalette("Text-Farbe", ButtonTuning.LabelR, ButtonTuning.LabelG, ButtonTuning.LabelB,
            ColorSwatches, ButtonColorRowsVisible);
        AddColorToggle("Kontur", ButtonTuning.LabelOutline);
        AddColorPalette("Kontur-Farbe", ButtonTuning.LabelOutlineR, ButtonTuning.LabelOutlineG,
            ButtonTuning.LabelOutlineB, ColorSwatches, ButtonColorRowsVisible);
        AddButtonTuningRow("Konturbreite", ButtonTuning.LabelOutlineW, 0.05f, 0f, 1f,
            v => $"{v:0.00}", ButtonColorRowsVisible);
        AddColorToggle("Schatten", ButtonTuning.LabelUnderlay);

        // Cap-face tints are MULTIPLIERS (1,1,1 = identity/unchanged). A palette of tint colours works
        // the same way — the swatch writes the three tint channels — and "Weiß" (1,1,1) is the neutral
        // no-tint swatch (which is also each cap tint's default, so it shows highlighted at rest).
        _rowGate = ButtonColorRowsVisible;
        Section("Kappen (Tönung)");
        AddColorPalette("Board-Kappe", ButtonTuning.BoardCapTintR, ButtonTuning.BoardCapTintG,
            ButtonTuning.BoardCapTintB, ColorSwatches, ButtonColorRowsVisible);
        AddColorPalette("Zahnrad-Kappe", ButtonTuning.DashCapTintR, ButtonTuning.DashCapTintG,
            ButtonTuning.DashCapTintB, ColorSwatches, ButtonColorRowsVisible);
        AddColorPalette("Zugleiste-Kappe", ButtonTuning.ClusterCapTintR, ButtonTuning.ClusterCapTintG,
            ButtonTuning.ClusterCapTintB, ColorSwatches, ButtonColorRowsVisible);
        AddColorPalette("Rast-Kappe", ButtonTuning.RestCapTintR, ButtonTuning.RestCapTintG,
            ButtonTuning.RestCapTintB, ColorSwatches, ButtonColorRowsVisible);
    }

    /// <summary>
    /// Shared colour palette (user 1) for the Knopf-Farben swatch grids. Covers what the keycaps
    /// need: bright parchment/near-white (= the LabelR/G/B default), warm golds, dark umber/near-black
    /// (= the LabelOutline default, and dark caps so light text reads), wood browns, brass, neutral
    /// greys, and a small spectrum. "Weiß" (1,1,1) is the cap-tint identity/default. Comment names are
    /// documentation only; the swatches render as pure colour chips.
    /// </summary>
    private static readonly Color[] ColorSwatches =
    {
        new(0.984f, 0.953f, 0.878f), // Pergament (= LabelR/G/B default)
        new(1f, 1f, 1f),             // Weiß (cap-tint identity / default)
        new(0.80f, 0.80f, 0.80f),    // Hellgrau
        new(0.50f, 0.50f, 0.50f),    // Grau
        new(0.25f, 0.25f, 0.25f),    // Dunkelgrau
        new(0.09f, 0.06f, 0.03f),    // Umbra (= LabelOutline default, near-black)
        new(0.02f, 0.02f, 0.02f),    // Schwarz
        new(0.85f, 0.65f, 0.13f),    // Gold
        new(0.95f, 0.82f, 0.45f),    // Helles Gold
        new(0.62f, 0.50f, 0.28f),    // Messing (brass)
        new(0.45f, 0.30f, 0.16f),    // Holz
        new(0.28f, 0.18f, 0.09f),    // Dunkles Holz
        new(0.75f, 0.18f, 0.15f),    // Rot
        new(0.25f, 0.55f, 0.28f),    // Grün
        new(0.25f, 0.40f, 0.65f),    // Blau
        new(0.90f, 0.55f, 0.20f),    // Bernstein
    };

    /// <summary>
    /// User 1: a LASER-CLICKABLE COLOUR PALETTE for one colour target (label fill, outline, or a cap
    /// tint). Builds a titled section then a grid of colour-chip buttons on the panel canvas — each a
    /// uGUI Image + Button (the SAME interaction path as every other panel button, so poke AND laser
    /// hit them). Clicking a swatch writes its RGB into the three *R/G/B binds AT ONCE (→ ButtonTuning
    /// .Version bump → live re-skin). The swatch whose colour currently matches the binds (within a
    /// small epsilon) shows a bright highlight frame, so the active choice is visible. Skipped whole if
    /// any bind failed to bind (config dir unwritable), matching the stepper-row policy. Rows gate to
    /// <paramref name="visible"/> (Debug → Knopf-Farben selected).
    /// </summary>
    private void AddColorPalette(string label, ConfigEntry<float>? rBind, ConfigEntry<float>? gBind,
        ConfigEntry<float>? bBind, Color[] swatches, Func<bool> visible)
    {
        if (rBind == null || gBind == null || bBind == null)
            return;

        _rowGate = visible;
        var titleRow = Row(22f);
        Label(titleRow, label, 14f, bold: true, flexible: true);

        const int perRow = 8;
        for (int start = 0; start < swatches.Length; start += perRow)
        {
            _rowGate = visible;
            var row = Row(30f);
            int end = Mathf.Min(start + perRow, swatches.Length);
            for (int i = start; i < end; i++)
            {
                Color c = swatches[i];
                BuildSwatch(row, c,
                    () => { rBind.Value = c.r; gBind.Value = c.g; bBind.Value = c.b; },
                    () => ColorMatches(rBind, gBind, bBind, c));
            }
        }
    }

    /// <summary>
    /// One colour chip: an outer border frame (highlighted while selected) around an inner Image +
    /// Button that carries the colour and the click. targetGraphic is the inner Image so poke/laser
    /// raycast the chip exactly like a normal panel button.
    /// </summary>
    private void BuildSwatch(RectTransform row, Color color, Action onClick, Func<bool> isSelected)
    {
        var outerGo = new GameObject("Swatch") { layer = 5 };
        outerGo.transform.SetParent(row, worldPositionStays: false);
        var border = outerGo.AddComponent<Image>();
        border.raycastTarget = false;
        var le = outerGo.AddComponent<LayoutElement>();
        le.preferredWidth = 34f;
        le.minWidth = 34f;
        le.flexibleWidth = 0f;

        var innerGo = new GameObject("Chip") { layer = 5 };
        innerGo.transform.SetParent(outerGo.transform, worldPositionStays: false);
        var chip = innerGo.AddComponent<Image>();
        chip.color = color;
        var button = innerGo.AddComponent<Button>();
        button.targetGraphic = chip;
        button.onClick.AddListener(() => Safe(() => { onClick(); RefreshAll(); }));
        var ir = (RectTransform)innerGo.transform;
        ir.anchorMin = Vector2.zero;
        ir.anchorMax = Vector2.one;
        ir.offsetMin = new Vector2(3f, 3f);
        ir.offsetMax = new Vector2(-3f, -3f);

        _refreshers.Add(() =>
            border.color = isSelected()
                ? new Color(1f, 0.92f, 0.35f, 1f)      // bright highlight frame = active choice
                : new Color(0.10f, 0.10f, 0.13f, 1f)); // subtle bezel otherwise
    }

    /// <summary>True while the three colour binds match a swatch's RGB within a small epsilon.</summary>
    private static bool ColorMatches(ConfigEntry<float>? r, ConfigEntry<float>? g, ConfigEntry<float>? b,
        Color c)
    {
        if (r == null || g == null || b == null)
            return false;
        const float eps = 0.02f;
        return Mathf.Abs(r.Value - c.r) < eps
               && Mathf.Abs(g.Value - c.g) < eps
               && Mathf.Abs(b.Value - c.b) < eps;
    }

    /// <summary>
    /// Knopf-Farben boolean row ([ButtonColors] LabelOutline / LabelUnderlay). Writing the entry
    /// bumps ButtonTuning.Version so NativeButtonSkin re-skins the caps live. Skipped if the bind
    /// failed (config dir unwritable). Shown while Debug → Knopf-Farben is selected.
    /// </summary>
    private void AddColorToggle(string label, ConfigEntry<bool>? entry)
    {
        if (entry == null)
            return;
        _rowGate = ButtonColorRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, () => entry.Value, v => entry.Value = v);
    }

    /// <summary>Elements that expose a Size/Scale stepper (Rest disc, Generic side, Active/Pile scale, Board scale, the two docks + the cluster).</summary>
    private static bool ElementHasSize(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic or DebugElement.Active
        or DebugElement.Piles or DebugElement.Board
        or DebugElement.Objectives or DebugElement.Elements or DebugElement.Cluster
        or DebugElement.Decision;

    /// <summary>
    /// Group elements that expose a Spacing stepper — every element that moves a PAIR/group of
    /// widgets together: both button groups, the two slot Overlays (item 1), the piles, the active grid.
    /// </summary>
    private static bool ElementHasSpacing(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic or DebugElement.Overlays
        or DebugElement.Piles or DebugElement.Active;

    /// <summary>The two button GROUPS carry a Round/Square shape toggle.</summary>
    private static bool ElementHasShape(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic;

    private void AddOffsetStepper(string label, int axis)
    {
        // Every per-board element carries a Vector3 offset — except the global ButtonTuning
        // elements: RoundButtons has its own [RoundButtons] Versatz X/Y/Z rows, and the two
        // board keycap elements have no offset at all (ElementOffsetEntry() is null there).
        _rowGate = () => PerBoard() && ElementOffsetEntry() != null;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row, () => FormatOffset(axis), d => StepOffset(axis, d));
    }

    /// <summary>
    /// Piles-element stepper row for ONE axis of the GLOBAL [Cards] BrowseFanOffset — the pile
    /// BROWSE fan's board-local anchor offset (added to its fixed above-board base). Writing the
    /// entry persists (BepInEx) and live-applies WITHOUT a SettingChanged hook: PileBrowser.Tick
    /// re-reads the entry every frame while a browse fan is open. Shown only for Panels → Piles.
    /// </summary>
    private void AddBrowseOffsetStepper(string label, int axis)
    {
        _rowGate = () => PerBoard() && CurrentElement() == DebugElement.Piles;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () =>
            {
                Vector3 v = CardsConfig.BrowseFanOffset.Value;
                float c = axis == 0 ? v.x : axis == 1 ? v.y : v.z;
                return $"{c * 1000f:0}mm";
            },
            d =>
            {
                ConfigEntry<Vector3> e = CardsConfig.BrowseFanOffset;
                Vector3 v = e.Value;
                float s = d * 0.002f; // 2 mm per press, same feel as StepOffset
                if (axis == 0) v.x += s;
                else if (axis == 1) v.y += s;
                else v.z += s;
                e.Value = v; // persists; PileBrowser.Tick re-reads per frame (live)
            });
    }

    /// <summary>
    /// Wall-fade threshold stepper row under the Wände category. Bound directly to a live
    /// [WallFade] entry — writing persists (BepInEx) and applies on WallSegmentFade's next
    /// evaluation tick (the driver re-reads the clamped WallFadeTuning accessors every frame).
    /// </summary>
    private void AddWallFadeRow(string label, ConfigEntry<float>? entry, float step, float min,
        float max, Func<float, string> format)
    {
        if (entry == null)
            return; // WallFadeTuning.Bind() failed (config dir unwritable) — skip the row
        _rowGate = WallFadeRowsVisible; // issue 2: moved into Debug → Wandüberblendung
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// The GLOBAL ButtonTuning elements (per-board board selector is hidden for them).
    /// WallFade is global too (the wall-fade fractions ride no board). BoardButtons is no longer a
    /// standalone element (issue 6 — merged into Generic), so it is not listed. The three per-STYLE
    /// elements (Hände-Offsets / Figuren-Offsets / Handgelenk) are not per-board either, so they hide
    /// the board selector too.
    /// </summary>
    private static bool ElementIsGlobalTuning(DebugElement e) =>
        e is DebugElement.RoundButtons or DebugElement.BoardDashboard or DebugElement.WallFade
        or DebugElement.HandOffsets or DebugElement.FigureOffsets or DebugElement.WristOffsets
        or DebugElement.ButtonColors;

    /// <summary>
    /// The per-HAND-STYLE Debug elements folded in from the former top-level tabs (2026-07). They
    /// carry NO per-board offset entry and NO generic size/spacing/shape rows, so the shared reset
    /// button (which resets a per-board element) hides for them — they never had a reset as tabs.
    /// </summary>
    private static bool ElementIsPerStyle(DebugElement e) =>
        e is DebugElement.HandOffsets or DebugElement.FigureOffsets or DebugElement.WristOffsets;

    /// <summary>Visibility of the per-style Hände-Offsets rows: Debug → Hände-Offsets element selected.</summary>
    private bool HandOffsetRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.HandOffsets;

    /// <summary>Visibility of the per-style Figuren-Offsets rows: Debug → Figuren-Offsets element selected.</summary>
    private bool FigureOffsetRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.FigureOffsets;

    /// <summary>Visibility of the per-style wrist-HUD rows: Debug → Handgelenk element selected.</summary>
    private bool WristRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.WristOffsets;

    /// <summary>Visibility of the [RoundButtons] rows: Debug → Rundenknöpfe element selected.</summary>
    private bool RoundButtonRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.RoundButtons;

    /// <summary>
    /// Visibility of the [BoardButtons] Confirm/Undo keycap geometry rows. Issue 6: these are the
    /// SAME physical Confirm/Undo caps as the Generic element, so the rectangle Width/Height/Depth/
    /// Travel steppers now surface under Debug → Generisch (there is no separate Boardtasten element).
    /// </summary>
    private bool BoardButtonRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.Generic;

    /// <summary>Visibility of the [BoardDashboard] rows: Debug → Zahnrad &amp; Fixiert element selected.</summary>
    private bool BoardDashboardRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.BoardDashboard;

    /// <summary>Visibility of the [RestButtons] geometry rows: Debug → Ruhetasten element selected.</summary>
    private bool RestButtonTuningRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.Rest;

    /// <summary>Visibility of the wall-fade fraction rows: Debug → Wandüberblendung element selected.</summary>
    private bool WallFadeRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.WallFade;

    /// <summary>Visibility of the [ButtonColors] rows: Debug → Knopf-Farben element selected.</summary>
    private bool ButtonColorRowsVisible() =>
        _navCat == (int)NavCat.Debug && CurrentElement() == DebugElement.ButtonColors;

    /// <summary>
    /// ButtonTuning stepper row bound directly to a live <see cref="ConfigEntry{T}"/> (mm
    /// readout, always a real number — no "Auto" sentinel any more; the entry defaults ARE
    /// the authored values). Writing persists (BepInEx) and live-applies: the entry's
    /// SettingChanged bumps ButtonTuning.Version and PlayTray/ButtonCluster rebuild the
    /// caps on their next tick. Skipped entirely if Bind() failed (config dir unwritable) —
    /// same policy as <see cref="AddWallFadeRow"/>. Shown while <paramref name="visible"/>
    /// says the row's Tasten element is selected.
    /// </summary>
    private void AddButtonTuningRow(string label, ConfigEntry<float>? entry, float step, float min,
        float max, Func<float, string> format, Func<bool> visible)
    {
        if (entry == null)
            return;
        _rowGate = visible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// Card-fan stepper bound to a global fan <see cref="ConfigEntry{T}"/>. Writing the entry
    /// persists (BepInEx) and live-applies (CardsDriver relayouts the open fan). Clamped to
    /// [min,max]. Shown under Debug → Hände-Offsets (Kartenfächer sub-section).
    /// </summary>
    private void AddFanStepper(string label, ConfigEntry<float> entry, float step, float min, float max,
        Func<float, string> format)
    {
        _rowGate = HandOffsetRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// Per-STYLE stepper row: drives the entry of the style CURRENTLY worn (indexed by
    /// [Hands] HandStyle into the given per-style entry array), so the element only ever shows
    /// the active style's values — Debug → Hände-Offsets (scale + the four seat controls),
    /// Debug → Figuren-Offsets (held-mini pose) and any future per-style group all go through here.
    /// Writing persists (BepInEx) and live-applies (VRHand.SyncVisualOffset re-reads per
    /// frame; FigureGrabConfig re-poses via SettingChanged). Shown while the given
    /// <paramref name="visible"/> predicate says its Debug element is selected; shows "—" while the
    /// config is not bound yet.
    /// </summary>
    private void AddStyleStepper(string label, Func<ConfigEntry<float>[]?> entries, float step,
        float min, float max, Func<float, string> format,
        Func<bool> visible)
    {
        _rowGate = visible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () =>
            {
                ConfigEntry<float>? e = ActiveStyleEntry(entries);
                return e != null ? format(e.Value) : "—";
            },
            d =>
            {
                ConfigEntry<float>? e = ActiveStyleEntry(entries);
                if (e != null)
                    e.Value = Mathf.Clamp(e.Value + d * step, min, max);
            });
    }

    /// <summary>The active-style element of a per-style [Hands] entry array (null-safe).</summary>
    private static ConfigEntry<float>? ActiveStyleEntry(Func<ConfigEntry<float>[]?> entries)
    {
        try
        {
            ConfigEntry<float>[]? arr = entries();
            if (arr == null)
                return null;
            int style = (int)Hands.HandStyles.Clamp(
                Plugin.HandStyle != null ? (int)Plugin.HandStyle.Value : 0);
            return arr[style];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Item 10: a WRIST-HUD pose stepper row bound to a WristHud getter/setter (degrees for
    /// pitch/yaw/roll, mm for the X/Y/Z offset). Since the per-style rework (request B) the
    /// accessors read/write the ACTIVE hand style's entry, so these rows edit the style
    /// currently worn. Setting the value re-poses the wrist HUD live — WristHud.ApplyPose
    /// re-reads it every Tick — and persists via BepInEx. Shown only under the Wrist tab.
    /// </summary>
    private void AddWristStepper(string label, Func<float> get, Action<float> set, float step,
        Func<float, string> format)
    {
        _rowGate = WristRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(get()),
            d => set(get() + d * step));
    }

    /// <summary>Held-figure boolean row (e.g. Upright) — same live-apply/persist path as the steppers.</summary>
    private void AddFigureToggle(string label, ConfigEntry<bool> entry)
    {
        _rowGate = FigureOffsetRowsVisible;
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, () => entry.Value, v => entry.Value = v);
    }

    /// <summary>The offset ConfigEntry the selected element edits (all board-local Vector3s).</summary>
    private ConfigEntry<Vector3>? ElementOffsetEntry()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => CardsConfig.RestButtonOffset(b),
            DebugElement.Generic => CardsConfig.ConfirmUndoOffset(b),
            DebugElement.Overlays => CardsConfig.SlotOverlayOffset(b),
            DebugElement.Initiative => CardsConfig.InitiativeOffset(b),
            DebugElement.Active => CardsConfig.ActiveOffset(b),
            DebugElement.Piles => CardsConfig.PileOffset(b),
            DebugElement.Board => CardsConfig.BoardPosOffset(b),
            DebugElement.Objectives => CardsConfig.ObjectivesOffset(b),
            DebugElement.Elements => CardsConfig.ElementsOffset(b),
            DebugElement.VRSettings => CardsConfig.VRSettingsOffset(b),
            DebugElement.Pin => CardsConfig.PinOffset(b),
            DebugElement.Readout => CardsConfig.ReadoutOffset(b),
            DebugElement.Cluster => CardsConfig.ClusterOffset(b),
            DebugElement.Decision => CardsConfig.DecisionOffset(b),
            DebugElement.ItemUse => CardsConfig.ItemUseSlotOffset(b),
            DebugElement.ItemCard => CardsConfig.ItemCardOffset(b),
            _ => null,
        };
    }

    private string FormatOffset(int axis)
    {
        ConfigEntry<Vector3>? e = ElementOffsetEntry();
        if (e == null)
            return "-";
        Vector3 v = e.Value;
        float c = axis == 0 ? v.x : axis == 1 ? v.y : v.z;
        return $"{c * 1000f:0}mm";
    }

    private void StepOffset(int axis, int delta)
    {
        ConfigEntry<Vector3>? e = ElementOffsetEntry();
        if (e == null)
            return;
        Vector3 v = e.Value;
        float s = delta * 0.002f; // 2 mm per press
        if (axis == 0) v.x += s;
        else if (axis == 1) v.y += s;
        else v.z += s;
        e.Value = v; // persists + fires SettingChanged → CardsDriver live-applies
    }

    private string FormatSize()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => $"{CardsConfig.RestButtonDiameter(b).Value * 1000f:0}mm",
            DebugElement.Generic => $"{CardsConfig.ConfirmUndoSize(b).Value * 1000f:0}mm",
            DebugElement.Active => $"{CardsConfig.ActiveCardScale(b).Value:0.00}x",
            DebugElement.Piles => $"{CardsConfig.PileScale(b).Value:0.00}x",
            DebugElement.Board => $"{CardsConfig.BoardScale(b).Value:0.00}x",
            DebugElement.Objectives => $"{CardsConfig.ObjectivesScale(b).Value:0.00}x",
            DebugElement.Elements => $"{CardsConfig.ElementsScale(b).Value:0.00}x",
            DebugElement.Cluster => $"{CardsConfig.ClusterScale(b).Value:0.00}x",
            DebugElement.Decision => $"{CardsConfig.DecisionScale(b).Value:0.00}x",
            _ => "-",
        };
    }

    private void StepSize(int delta)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                ConfigEntry<float> e = CardsConfig.RestButtonDiameter(b);
                e.Value = Mathf.Max(0.01f, e.Value + delta * 0.002f);
                break;
            }
            case DebugElement.Generic:
            {
                ConfigEntry<float> e = CardsConfig.ConfirmUndoSize(b);
                e.Value = Mathf.Max(0.01f, e.Value + delta * 0.002f);
                break;
            }
            case DebugElement.Active:
            {
                ConfigEntry<float> e = CardsConfig.ActiveCardScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.02f, 0.3f, 2f);
                break;
            }
            case DebugElement.Piles:
            {
                ConfigEntry<float> e = CardsConfig.PileScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Board:
            {
                ConfigEntry<float> e = CardsConfig.BoardScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Objectives:
            {
                ConfigEntry<float> e = CardsConfig.ObjectivesScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Elements:
            {
                ConfigEntry<float> e = CardsConfig.ElementsScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Cluster:
            {
                ConfigEntry<float> e = CardsConfig.ClusterScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
            case DebugElement.Decision:
            {
                ConfigEntry<float> e = CardsConfig.DecisionScale(b);
                e.Value = Mathf.Clamp(e.Value + delta * 0.05f, 0.3f, 3f);
                break;
            }
        }
    }

    /// <summary>
    /// Objectives dock width readout (user request): the EFFECTIVE fit-width budget in mm —
    /// base <see cref="PlayTray.ObjectivesMountWidth"/> × the per-board <c>ObjectivesWidth</c>
    /// multiplier — with the multiplier appended so both the physical size and the factor read.
    /// </summary>
    private string FormatObjectivesWidth()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        float mult = CardsConfig.ObjectivesWidth(b).Value;
        return $"{PlayTray.ObjectivesMountWidth * mult * 1000f:0}mm ({mult:0.0}x)";
    }

    /// <summary>
    /// Widen/narrow the objectives task dock (0.1× steps, 0.5–3.0). Live: ObjectivesSurface FORCES
    /// the resulting mm budget onto the game's objective rows as a pixel width each tick, so the
    /// task text re-wraps and the progress bar lengthens — the readout's mm is the real panel width,
    /// not just a fit ceiling. SHAPE ONLY: the dock's fit deliberately ignores the width axis for
    /// this panel (ObjectivesSurface.FitWidthToMount), so the rendered TYPE SIZE does not move with
    /// this dial — that is what the separate 'Größe' (ObjectivesScale) row is for. The lower bound
    /// goes below 1.0 on purpose: since the budget is literal, 1.0 (260 mm) is already wider than the
    /// game's authored wrap column, so the dial needs headroom DOWNWARD to reach a tighter look.
    /// </summary>
    private void StepObjectivesWidth(int delta)
    {
        ConfigEntry<float> e = CardsConfig.ObjectivesWidth(CardsConfig.CurrentBoard);
        e.Value = Mathf.Clamp(e.Value + delta * 0.1f, 0.5f, 3f);
    }

    // ---- Spacing / Row-gap / Shape steppers (round 2) --------------------------------------

    /// <summary>Spacing readout for the selected group element (mm for meter gaps, factor for the active COL step).</summary>
    private string FormatSpacing()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => $"{CardsConfig.RestButtonSpacing(b).Value * 1000f:0}mm",
            DebugElement.Generic => $"{CardsConfig.GenericButtonSpacing(b).Value * 1000f:0}mm",
            DebugElement.Overlays => $"{CardsConfig.SlotOverlaySpacing(b).Value * 1000f:0}mm",
            DebugElement.Piles => $"{CardsConfig.PileSpacing(b).Value * 1000f:0}mm",
            DebugElement.Active => $"{CardsConfig.ActiveGridSpacing(b).Value.x:0.00}",
            _ => "-",
        };
    }

    private void StepSpacing(int delta)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                ConfigEntry<float> e = CardsConfig.RestButtonSpacing(b);
                e.Value += delta * 0.002f; // 2 mm per press (may go negative — pulls the pair together)
                break;
            }
            case DebugElement.Generic:
            {
                ConfigEntry<float> e = CardsConfig.GenericButtonSpacing(b);
                e.Value += delta * 0.002f;
                break;
            }
            case DebugElement.Overlays:
            {
                ConfigEntry<float> e = CardsConfig.SlotOverlaySpacing(b);
                e.Value += delta * 0.002f; // 2 mm per press (may go negative — pulls the pair together)
                break;
            }
            case DebugElement.Piles:
            {
                ConfigEntry<float> e = CardsConfig.PileSpacing(b);
                e.Value = Mathf.Max(0f, e.Value + delta * 0.002f);
                break;
            }
            case DebugElement.Active:
            {
                ConfigEntry<Vector2> e = CardsConfig.ActiveGridSpacing(b);
                Vector2 v = e.Value;
                v.x = Mathf.Max(0.1f, v.x + delta * 0.02f);
                e.Value = v;
                break;
            }
        }
    }

    /// <summary>Active grid ROW step factor (only shown for the Active element).</summary>
    private string FormatActiveRowStep()
    {
        ConfigEntry<Vector2> e = CardsConfig.ActiveGridSpacing(CardsConfig.CurrentBoard);
        return $"{e.Value.y:0.00}";
    }

    private void StepActiveRowStep(int delta)
    {
        ConfigEntry<Vector2> e = CardsConfig.ActiveGridSpacing(CardsConfig.CurrentBoard);
        Vector2 v = e.Value;
        v.y = Mathf.Max(0.1f, v.y + delta * 0.02f);
        e.Value = v;
    }

    /// <summary>Cap-shape readout for the selected button group (Rest / Generic).</summary>
    private string FormatShape()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return CurrentElement() switch
        {
            DebugElement.Rest => ShapeLabel(CardsConfig.RestButtonShape(b).Value),
            DebugElement.Generic => ShapeLabel(CardsConfig.GenericButtonShape(b).Value),
            _ => "-",
        };
    }

    /// <summary>Localized name of a debug-tunable board element (the Element cycle readout).</summary>
    private static string DebugElementLabel(DebugElement e) => e switch
    {
        DebugElement.Rest => Loc.Mod("rest"),
        DebugElement.Generic => Loc.Mod("generic"),
        DebugElement.Overlays => Loc.Mod("overlays"),
        DebugElement.Initiative => Loc.Mod("initiative"),
        DebugElement.Active => Loc.Mod("active"),
        DebugElement.Piles => Loc.Mod("piles"),
        DebugElement.Board => Loc.Mod("board"),
        DebugElement.Objectives => Loc.Mod("objectives"),
        DebugElement.Elements => Loc.Mod("elements"),
        DebugElement.VRSettings => Loc.Mod("vr_settings"),
        DebugElement.Pin => Loc.Mod("pin"),
        DebugElement.Readout => Loc.Mod("readout"),
        DebugElement.Cluster => Loc.Mod("cluster"),
        DebugElement.Decision => Loc.Mod("decision"),
        // Items rework: the item-use clip-in slot.
        DebugElement.ItemUse => Loc.Mod("item_use"),
        // Items rework: the item pile fan / held item-card pose offset.
        DebugElement.ItemCard => Loc.Mod("item_card"),
        // Fully localized (the whole mod is localized — no hardcoded strings): the round-phase
        // skip buttons the user tunes here, plus the category-split board elements (Confirm/Undo
        // keycaps; gear + Pin plates) and the per-style / colour rows.
        DebugElement.RoundButtons => Loc.Mod("round_buttons"),
        DebugElement.BoardButtons => Loc.Mod("board_buttons"),
        DebugElement.BoardDashboard => Loc.Mod("board_dashboard"),
        DebugElement.WallFade => Loc.Mod("wall_fade"),
        // Per-style elements folded in from the former top-level tabs (2026-07).
        DebugElement.HandOffsets => Loc.Mod("hand_offsets"),
        DebugElement.FigureOffsets => Loc.Mod("figure_offsets"),
        DebugElement.WristOffsets => Loc.Mod("cat_wrist"),
        // User 4: the keycap label/cap colour rows.
        DebugElement.ButtonColors => Loc.Mod("button_colors"),
        _ => e.ToString(),
    };

    /// <summary>German name of a Debug sub-category (user 5b) — the sub-category chooser readout.</summary>
    private static string DebugSubCatLabel(DebugSubCat s) => s switch
    {
        DebugSubCat.BoardLayout => Loc.Mod("subcat_board_layout"),
        DebugSubCat.KartenStapel => Loc.Mod("subcat_cards_piles"),
        DebugSubCat.Tasten => Loc.Mod("cat_buttons"),
        DebugSubCat.Offsets => Loc.Mod("subcat_offsets"),
        // User 3: the old "Welt-Tuning" was misleading — this sub-category only holds the wall
        // see-through fade tuning, so it is named for what it actually controls.
        DebugSubCat.WeltTuning => Loc.Mod("subcat_wall_seethrough"),
        _ => s.ToString(),
    };

    /// <summary>Localized cap-shape name (the Shape cycle readout).</summary>
    private static string ShapeLabel(ButtonShape shape) =>
        shape == ButtonShape.Round ? Loc.Mod("round") : Loc.Mod("square");

    /// <summary>Flip the selected group's cap shape Round &lt;-&gt; Square (live rebuild via CardsDriver).</summary>
    private void FlipShape()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                ConfigEntry<ButtonShape> e = CardsConfig.RestButtonShape(b);
                e.Value = e.Value == ButtonShape.Round ? ButtonShape.Square : ButtonShape.Round;
                break;
            }
            case DebugElement.Generic:
            {
                ConfigEntry<ButtonShape> e = CardsConfig.GenericButtonShape(b);
                e.Value = e.Value == ButtonShape.Round ? ButtonShape.Square : ButtonShape.Round;
                break;
            }
        }
    }

    /// <summary>Restore one ButtonTuning float entry to its (authored numeric) default.</summary>
    private static void ResetTuningF(ConfigEntry<float>? e)
    {
        if (e != null)
            e.Value = (float)e.DefaultValue;
    }

    private void ResetDebugElement()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        ConfigEntry<Vector3>? off = ElementOffsetEntry();
        if (off != null)
            off.Value = (Vector3)off.DefaultValue;
        switch (CurrentElement())
        {
            case DebugElement.Rest:
            {
                CardsConfig.RestButtonDiameter(b).Value = (float)CardsConfig.RestButtonDiameter(b).DefaultValue;
                CardsConfig.RestButtonSpacing(b).Value = (float)CardsConfig.RestButtonSpacing(b).DefaultValue;
                CardsConfig.RestButtonShape(b).Value = (ButtonShape)CardsConfig.RestButtonShape(b).DefaultValue;
                // GLOBAL [RestButtons] square-cap geometry (W/H/D/Travel), edited from this element.
                ResetTuningF(ButtonTuning.RestWidth);
                ResetTuningF(ButtonTuning.RestHeight);
                ResetTuningF(ButtonTuning.RestDepth);
                ResetTuningF(ButtonTuning.RestTravel);
                break;
            }
            case DebugElement.Generic:
            {
                CardsConfig.ConfirmUndoSize(b).Value = (float)CardsConfig.ConfirmUndoSize(b).DefaultValue;
                CardsConfig.GenericButtonSpacing(b).Value = (float)CardsConfig.GenericButtonSpacing(b).DefaultValue;
                CardsConfig.GenericButtonShape(b).Value = (ButtonShape)CardsConfig.GenericButtonShape(b).DefaultValue;
                // Issue 6: the GLOBAL [BoardButtons] Confirm/Undo keycap geometry (the square
                // rectangle W/H/D/Travel) is edited from this element, so reset it here too.
                ResetTuningF(ButtonTuning.BoardWidth);
                ResetTuningF(ButtonTuning.BoardHeight);
                ResetTuningF(ButtonTuning.BoardDepth);
                ResetTuningF(ButtonTuning.BoardTravel);
                break;
            }
            case DebugElement.Active:
            {
                CardsConfig.ActiveCardScale(b).Value = (float)CardsConfig.ActiveCardScale(b).DefaultValue;
                CardsConfig.ActiveGridSpacing(b).Value = (Vector2)CardsConfig.ActiveGridSpacing(b).DefaultValue;
                break;
            }
            case DebugElement.Piles:
            {
                CardsConfig.PileScale(b).Value = (float)CardsConfig.PileScale(b).DefaultValue;
                CardsConfig.PileSpacing(b).Value = (float)CardsConfig.PileSpacing(b).DefaultValue;
                // The browse-fan anchor offset is GLOBAL (board-local, rides every board) but is
                // edited from this element's rows, so the Piles reset restores it too.
                CardsConfig.BrowseFanOffset.Value = (Vector3)CardsConfig.BrowseFanOffset.DefaultValue;
                break;
            }
            case DebugElement.Board:
            {
                CardsConfig.BoardTilt(b).Value = (float)CardsConfig.BoardTilt(b).DefaultValue;
                CardsConfig.BoardYaw(b).Value = (float)CardsConfig.BoardYaw(b).DefaultValue;
                CardsConfig.BoardScale(b).Value = (float)CardsConfig.BoardScale(b).DefaultValue;
                break;
            }
            case DebugElement.Overlays:
                CardsConfig.SlotOverlaySpacing(b).Value = (float)CardsConfig.SlotOverlaySpacing(b).DefaultValue;
                break;
            case DebugElement.Objectives:
                CardsConfig.ObjectivesScale(b).Value = (float)CardsConfig.ObjectivesScale(b).DefaultValue;
                CardsConfig.ObjectivesWidth(b).Value = (float)CardsConfig.ObjectivesWidth(b).DefaultValue;
                break;
            case DebugElement.Elements:
                CardsConfig.ElementsScale(b).Value = (float)CardsConfig.ElementsScale(b).DefaultValue;
                break;
            case DebugElement.Cluster:
                CardsConfig.ClusterScale(b).Value = (float)CardsConfig.ClusterScale(b).DefaultValue;
                break;
            case DebugElement.Decision:
                CardsConfig.DecisionScale(b).Value = (float)CardsConfig.DecisionScale(b).DefaultValue;
                break;
            case DebugElement.RoundButtons:
            {
                // GLOBAL [RoundButtons] group (not per-board): restore the full geometry set.
                ResetTuningF(ButtonTuning.RoundOffsetX);
                ResetTuningF(ButtonTuning.RoundOffsetY);
                ResetTuningF(ButtonTuning.RoundOffsetZ);
                ResetTuningF(ButtonTuning.RoundCapSize);
                ResetTuningF(ButtonTuning.RoundWidth);
                ResetTuningF(ButtonTuning.RoundHeight);
                ResetTuningF(ButtonTuning.RoundDepth);
                ResetTuningF(ButtonTuning.RoundTravel);
                if (ButtonTuning.RoundShape != null)
                    ButtonTuning.RoundShape.Value = (ButtonShape)ButtonTuning.RoundShape.DefaultValue;
                break;
            }
            case DebugElement.BoardButtons:
            {
                // GLOBAL [BoardButtons] group: the Confirm/Undo keycap geometry.
                ResetTuningF(ButtonTuning.BoardWidth);
                ResetTuningF(ButtonTuning.BoardHeight);
                ResetTuningF(ButtonTuning.BoardDepth);
                ResetTuningF(ButtonTuning.BoardTravel);
                break;
            }
            case DebugElement.BoardDashboard:
            {
                // GLOBAL [BoardDashboard] group: the gear + follow/pin plate geometry.
                ResetTuningF(ButtonTuning.DashGearWidth);
                ResetTuningF(ButtonTuning.DashPinWidth);
                ResetTuningF(ButtonTuning.DashHeight);
                ResetTuningF(ButtonTuning.DashDepth);
                ResetTuningF(ButtonTuning.DashTravel);
                break;
            }
            case DebugElement.WallFade:
            {
                // GLOBAL wall-fade fractions (issue 2): restore the four [WallFade] thresholds.
                ResetTuningF(WallFadeTuning.OnFraction);
                ResetTuningF(WallFadeTuning.OffFraction);
                ResetTuningF(WallFadeTuning.ExitDwellMoved);
                ResetTuningF(WallFadeTuning.ExitDwellStationary);
                break;
            }
            case DebugElement.ButtonColors:
            {
                // GLOBAL [ButtonColors] (user 4): restore the label text/outline/underlay + every
                // per-category cap-face tint to their authored defaults (= today's look). Writing each
                // entry bumps ButtonTuning.Version so the re-skin fires live.
                ResetTuningF(ButtonTuning.LabelR);
                ResetTuningF(ButtonTuning.LabelG);
                ResetTuningF(ButtonTuning.LabelB);
                if (ButtonTuning.LabelOutline != null)
                    ButtonTuning.LabelOutline.Value = (bool)ButtonTuning.LabelOutline.DefaultValue;
                ResetTuningF(ButtonTuning.LabelOutlineR);
                ResetTuningF(ButtonTuning.LabelOutlineG);
                ResetTuningF(ButtonTuning.LabelOutlineB);
                ResetTuningF(ButtonTuning.LabelOutlineW);
                if (ButtonTuning.LabelUnderlay != null)
                    ButtonTuning.LabelUnderlay.Value = (bool)ButtonTuning.LabelUnderlay.DefaultValue;
                ResetTuningF(ButtonTuning.BoardCapTintR);
                ResetTuningF(ButtonTuning.BoardCapTintG);
                ResetTuningF(ButtonTuning.BoardCapTintB);
                ResetTuningF(ButtonTuning.DashCapTintR);
                ResetTuningF(ButtonTuning.DashCapTintG);
                ResetTuningF(ButtonTuning.DashCapTintB);
                ResetTuningF(ButtonTuning.ClusterCapTintR);
                ResetTuningF(ButtonTuning.ClusterCapTintG);
                ResetTuningF(ButtonTuning.ClusterCapTintB);
                ResetTuningF(ButtonTuning.RestCapTintR);
                ResetTuningF(ButtonTuning.RestCapTintG);
                ResetTuningF(ButtonTuning.RestCapTintB);
                break;
            }
        }
        VRLog.Info("Cards", $"Debug: reset {CurrentElement()} for {b} to defaults.");
    }

    // ---- grab frame (bar + FOLLOW/PINNED pin) -------------------------------------------------

    /// <summary>
    /// Build the mod-owned frame: holder (diorama scale) → frame (grab root) → brass grab
    /// bar + FOLLOW/PINNED pin. Mirrors <c>CombatLogSurface.EnsureFrame</c> so the panel
    /// moves/scales/persists through the shared <see cref="PanelGrabHandle"/> exactly like
    /// the control board and the combat log.
    /// </summary>
    private void BuildFrame()
    {
        var holderGo = new GameObject("GloomhavenVR.SettingsPanel");
        _holder = holderGo.transform;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        float panelWidth = PanelWidthPx * CanvasMetersPerPixel;
        float barWidth = panelWidth * BarWidthFraction;

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        UnityEngine.Object.Destroy(bar.GetComponent<Collider>());
        bar.transform.SetParent(_frame, worldPositionStays: false);
        bar.transform.localScale = new Vector3(barWidth, BarThickness, BarThickness);
        bar.GetComponent<MeshRenderer>().sharedMaterial =
            WorldUIAssets.CreateFlatMaterial(new Color(0.62f, 0.5f, 0.28f)); // brass — same "grab me" as the tray
        _bar = bar.transform;

        // Grab zone + shared grab core (collider BEFORE the handle: OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.size = new Vector3(panelWidth * ZoneWidthFraction, 0.05f, 0.05f);
        _grabZone.isTrigger = true;
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        _handle.Init(this, bar.GetComponent<MeshRenderer>(), "WorldUI", "Settings");

        // FOLLOW/PINNED pin, right of the bar (the tray's toggle, same look & feel).
        _pinAnchor = new GameObject("PinToggle").transform;
        _pinAnchor.SetParent(_frame, worldPositionStays: false);
        _pinAnchor.localPosition = new Vector3(barWidth * 0.5f + 0.05f, 0f, -0.002f);
        _pin = PlayTray.BoardButton.Create(_pinAnchor, new Vector2(0.068f, 0.030f),
            new Color(0.75f, 0.55f, 0.2f), Loc.Mod("follow"), TogglePin);
        ApplyPinVisual();

        VRLog.Info("WorldUI", "Settings panel frame built (grab bar + FOLLOW/PINNED pin).");
    }

    /// <summary>Cycle-button readout for the head-mask picker: "Mask 1".."Mask 3" (id 0..2 + 1).</summary>
    private static string MaskLabel()
    {
        int id = Net.NetModule.MaskId != null ? Net.NetModule.MaskId.Value : 0;
        id = Mathf.Clamp(id, 0, Net.HeadMaskLibrary.MaskCount - 1);
        return $"{Loc.Mod("mask")} {id + 1}";
    }

    /// <summary>Advance the local head mask 0→1→2→0 (writes [Net] MaskId; BepInEx persists on set).</summary>
    private static void CycleMask()
    {
        if (Net.NetModule.MaskId == null)
            return;
        int cur = Mathf.Clamp(Net.NetModule.MaskId.Value, 0, Net.HeadMaskLibrary.MaskCount - 1);
        Net.NetModule.MaskId.Value = (cur + 1) % Net.HeadMaskLibrary.MaskCount;
    }

    /// <summary>Cycle-button readout for the hand-style picker (enum name, like the
    /// control-board selector's Oak/Steel/Bronze).</summary>
    private static string HandStyleLabel()
    {
        try
        {
            return Plugin.HandStyle != null ? Plugin.HandStyle.Value.ToString() : Hands.HandStyle.Glove.ToString();
        }
        catch
        {
            return Hands.HandStyle.Glove.ToString();
        }
    }

    /// <summary>Advance the hand style Glove→Plate→Arcane→Glove (writes [Hands] HandStyle;
    /// BepInEx persists on set and HandsDriver live-rebuilds via SettingChanged).</summary>
    private static void CycleHandStyle()
    {
        if (Plugin.HandStyle == null)
            return;
        int cur = (int)Hands.HandStyles.Clamp((int)Plugin.HandStyle.Value);
        Plugin.HandStyle.Value = (Hands.HandStyle)((cur + 1) % Hands.HandStyles.Count);
    }

    /// <summary>Cycle-button readout for the remote-boards visibility setting.</summary>
    private static string RemoteBoardsLabel()
    {
        var v = Net.NetModule.RemoteBoards != null ? Net.NetModule.RemoteBoards.Value : Net.RemoteBoardVisibility.ActionPhaseOnly;
        return v switch
        {
            Net.RemoteBoardVisibility.Off => Loc.Mod("remote_boards_off"),
            Net.RemoteBoardVisibility.Always => Loc.Mod("remote_boards_always"),
            _ => Loc.Mod("remote_boards_action"),
        };
    }

    /// <summary>Advance remote-board visibility Off→ActionPhaseOnly→Always→Off (writes [Net] RemoteBoards).</summary>
    private static void CycleRemoteBoards()
    {
        if (Net.NetModule.RemoteBoards == null)
            return;
        int cur = (int)Net.NetModule.RemoteBoards.Value;
        Net.NetModule.RemoteBoards.Value = (Net.RemoteBoardVisibility)((cur + 1) % 3);
    }

    private static bool BoardConfigSafe(Func<bool> read)
    {
        try
        {
            return Board.BoardConfig.ForceFarMode != null && read();
        }
        catch
        {
            return false;
        }
    }

    private static float CurrentScaleMultiplier()
    {
        Transform? rig = RigTarget.Current;
        float baseScale = RigTarget.BaseScale;
        if (rig == null || baseScale <= 0f)
            return ComfortSettings.IsBound ? ComfortSettings.SavedScaleMultiplier.Value : 1f;
        return rig.localScale.x / baseScale;
    }

    private void RefreshAll()
    {
        for (int i = 0; i < _refreshers.Count; i++)
            _refreshers[i]();
    }

    // ---- widget builders --------------------------------------------------------------------

    private RectTransform Row(float height = RowHeightPx)
    {
        var go = new GameObject("Row") { layer = 5 };
        var rect = go.AddComponent<RectTransform>();
        // Rows parent into the active build column (sidebar/content) while one is set; else the
        // canvas root (header). And when a gate is set, register the row for the show/hide pass —
        // this is the single mechanism that swaps the content pane per selected category.
        go.transform.SetParent((_rowParent != null ? _rowParent : _root!.transform), worldPositionStays: false);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = true;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childAlignment = TextAnchor.MiddleLeft;
        var el = go.AddComponent<LayoutElement>();
        el.preferredHeight = height;
        el.minHeight = height;
        if (_rowGate != null)
        {
            _rows.Add(go);
            _rowVisible.Add(_rowGate);
        }
        return rect;
    }

    private void Section(string title)
    {
        var row = Row(24f);
        Label(row, $"— {title} —", 14f, bold: true, flexible: true, center: true);
    }

    private TextMeshProUGUI Label(RectTransform row, string text, float size,
        bool bold = false, bool flexible = false, bool center = false)
    {
        var go = new GameObject("Label") { layer = 5 };
        go.transform.SetParent(row, worldPositionStays: false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
        tmp.color = new Color(0.92f, 0.9f, 0.85f);
        tmp.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(tmp);
        var el = go.AddComponent<LayoutElement>();
        if (flexible)
            el.flexibleWidth = 1f;
        else
            el.preferredWidth = Mathf.Max(28f, text.Length * size * 0.55f);
        return tmp;
    }

    private (Button button, TextMeshProUGUI label) Button(RectTransform row, string text, float width,
        Action onClick, bool flexible = false)
    {
        var go = new GameObject("Button") { layer = 5 };
        go.transform.SetParent(row, worldPositionStays: false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.22f, 0.24f, 0.32f, 0.95f);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => Safe(onClick));
        var el = go.AddComponent<LayoutElement>();
        if (flexible)
            el.flexibleWidth = 1f;
        else
            el.preferredWidth = width;

        var textGo = new GameObject("Text") { layer = 5 };
        textGo.transform.SetParent(go.transform, worldPositionStays: false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 15f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        WorldUIAssets.TryAssignGameFont(tmp);
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return (button, tmp);
    }

    /// <summary>Label ..... [-] value [+]</summary>
    private void Stepper(string label, Func<string> read, Action<int> step)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        MiniStepper(row, read, step);
    }

    /// <summary>[-] value [+] appended to an existing row.</summary>
    private void MiniStepper(RectTransform row, Func<string> read, Action<int> step)
    {
        Button(row, "-", 36f, () => { step(-1); RefreshAll(); });
        TextMeshProUGUI value = Label(row, read(), 15f, center: true);
        value.GetComponent<LayoutElement>().preferredWidth = 64f;
        Button(row, "+", 36f, () => { step(+1); RefreshAll(); });
        _refreshers.Add(() => value.text = read());
    }

    /// <summary>Label ..... [On/Off]</summary>
    private void Toggle(string label, Func<bool> read, Action<bool> write)
    {
        var row = Row();
        Label(row, label, 16f, flexible: true);
        ToggleButton(row, read, write);
    }

    private void ToggleButton(RectTransform row, Func<bool> read, Action<bool> write)
    {
        (Button button, TextMeshProUGUI text) = Button(row, read() ? Loc.Mod("on") : Loc.Mod("off"), 58f,
            () => { write(!read()); RefreshAll(); });
        Image image = (Image)button.targetGraphic;
        _refreshers.Add(() =>
        {
            bool on = read();
            text.text = on ? Loc.Mod("on") : Loc.Mod("off");
            image.color = on ? new Color(0.20f, 0.42f, 0.26f, 0.95f) : new Color(0.30f, 0.22f, 0.22f, 0.95f);
        });
    }

    private void CycleButton(RectTransform row, float width, Func<string> read, Action advance)
    {
        (Button _, TextMeshProUGUI text) = Button(row, read(), width, () => { advance(); RefreshAll(); });
        _refreshers.Add(() => text.text = read());
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            VRLog.Error("WorldUI", $"Settings panel action threw: {ex}");
        }
    }

}
