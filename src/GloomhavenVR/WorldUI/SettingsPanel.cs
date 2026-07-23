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
    private const float PanelWidthPx = 380f;
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
    private const float SettingsPanelWidthMeters = 0.6f;
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
    }

    /// <summary>
    /// Debug-menu top-level TAB. The user browses the debug menu by category first (an
    /// expandable accordion of option rows — same idiom as the Element chooser), and the
    /// Element chooser then offers only the 2–5 elements of that category
    /// instead of all 14 at once. <see cref="Board"/> is a single-element per-board tab; Fan and
    /// Hands are GLOBAL tabs (no per-board Oak/Steel/Bronze selector, no element cycle) — their
    /// settings apply to every board.
    /// </summary>
    private enum DebugCategory { Buttons, Panels, Overlays, Widgets, Board, Fan, Hands, Figures, Wrist }
    private const int DebugCategoryCount = 9;

    /// <summary>
    /// SINGLE source of truth for category → elements (re-slice by editing this one table). A
    /// per-board category lists the <see cref="DebugElement"/>s its Element cycle walks; a GLOBAL
    /// category (Fan, Hands) has an EMPTY list — <see cref="CategoryIsGlobal"/> keys off that and
    /// hides the board + element selectors, showing the category's own global steppers instead.
    /// Order matches <see cref="DebugCategory"/>.
    /// </summary>
    private static readonly DebugElement[][] CategoryElements =
    {
        new[] { DebugElement.Rest, DebugElement.Generic, DebugElement.Cluster, DebugElement.Decision },       // Buttons
        new[] { DebugElement.Objectives, DebugElement.Elements, DebugElement.Initiative,
                DebugElement.Piles, DebugElement.Active },                                                    // Panels
        new[] { DebugElement.Overlays, DebugElement.Readout },                                                // Overlays
        new[] { DebugElement.VRSettings, DebugElement.Pin },                                                  // Widgets
        new[] { DebugElement.Board },                                                                         // Board
        System.Array.Empty<DebugElement>(),                                                                  // Fan (global)
        System.Array.Empty<DebugElement>(),                                                                  // Hands (global)
        System.Array.Empty<DebugElement>(),                                                                  // Figures (global)
        System.Array.Empty<DebugElement>(),                                                                  // Wrist (global)
    };

    /// <summary>GLOBAL tabs (Fan, Hands) apply to every board — no per-board selector, no element cycle.</summary>
    private static bool CategoryIsGlobal(DebugCategory c) => CategoryElements[(int)c].Length == 0;

    private int _debugCategory;
    /// <summary>Remembered element index PER category (nice-to-have persistence within a session).</summary>
    private readonly int[] _categoryElement = new int[DebugCategoryCount];
    /// <summary>Element ACCORDION state: true while the per-element option rows are expanded.</summary>
    private bool _elementListOpen;
    /// <summary>Category ACCORDION state: true while the per-category option rows are expanded.</summary>
    private bool _categoryListOpen;
    /// <summary>Wall-fade tuning EXPANDER state (collapsed by default; same accordion idiom).</summary>
    private bool _wallFadeOpen;
    /// <summary>"Knöpfe" (ButtonTuning) EXPANDER state (collapsed by default; same accordion idiom).</summary>
    private bool _buttonTuningOpen;
    private readonly List<GameObject> _debugRows = new(24);          // every debug row (teardown + gate)
    private readonly List<Func<bool>> _debugRowVisible = new(24);    // parallel per-row visibility predicate
    private bool _healLogged;           // change-dedup for the out-of-view heal log
    private bool _respawnRequested;     // every OPEN drops the panel in view in front of the head
    private float _sizeScale;           // diorama WorldScale snapshotted at open (item 1/6: board-sized, zoom-stable)

    private DebugCategory CurrentCategory => (DebugCategory)_debugCategory;
    private DebugElement[] CurrentCategoryElements => CategoryElements[_debugCategory];

    /// <summary>The element the per-board steppers currently drive (the selected element of the current per-board category).</summary>
    private DebugElement CurrentElement()
    {
        DebugElement[] els = CategoryElements[_debugCategory];
        if (els.Length == 0)
            return DebugElement.Board; // GLOBAL category (Fan/Hands): element rows are hidden anyway
        int idx = Mathf.Clamp(_categoryElement[_debugCategory], 0, els.Length - 1);
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
        // "Knöpfe" rows immediately instead of waiting for the 0.25 s cadence. Cheap:
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
        _debugRows.Clear();
        _debugRowVisible.Clear();
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
        _debugRows.Clear();
        _debugRowVisible.Clear();
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
        _debugRows.Clear();
        _debugRowVisible.Clear();

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

        // Header ------------------------------------------------------------------
        var header = Row();
        Label(header, "GloomhavenVR", 20f, bold: true, flexible: true);
        Button(header, "X", 40f, () => SetOpen(false));

        Section(Loc.Mod("comfort"));

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

        // Seated mode ("Sitzmodus") row removed (user: irrelevant — the world is freely
        // draggable, so a recenter height preset adds nothing). Recenter always uses the
        // standing preset + TableHeightOffset now (ComfortSettings.EffectiveEyeHeightMeters).

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
        Stepper("World tilt",
            () => $"{Mathf.Clamp(Plugin.WorldTiltDegrees.Value, 0f, 60f):0}°",
            delta => Plugin.WorldTiltDegrees.Value =
                Mathf.Clamp(Plugin.WorldTiltDegrees.Value + delta * 5f, 0f, 60f));

        // Vignette row removed (user: no effect, not wanted) — ComfortVignette component
        // deleted with it; the ComfortSettings bindings are gone too.

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

        Section(Loc.Mod("modules"));

        Toggle(Loc.Mod("dominant_hand_right"),
            () => !string.Equals(Plugin.PrimaryHand.Value, "Left", StringComparison.OrdinalIgnoreCase),
            v => Plugin.PrimaryHand.Value = v ? "Right" : "Left");

        Toggle(Loc.Mod("board_far_ray"),
            () => BoardConfigSafe(() => Board.BoardConfig.ForceFarMode.Value),
            v => { if (Board.BoardConfig.ForceFarMode != null) Board.BoardConfig.ForceFarMode.Value = v; });

        Toggle(Loc.Mod("world_ui_surfaces"),
            () => WorldUIConfig.Master.Value,
            v => WorldUIConfig.Master.Value = v);

        // Optional game wall see-through ([Compat] WallFade): ON lets the game's own
        // view-dependent wall fade run (it follows the HMD — see WallFadeDisable); OFF
        // (default) keeps walls always solid. Applies LIVE — the Harmony postfix consults
        // the entry every frame, no restart needed.
        Toggle("Wall see-through",
            () => Plugin.WallFade.Value,
            v => Plugin.WallFade.Value = v);

        // Wall-fade decision thresholds (live [WallFade] config — WallSegmentFade re-reads
        // them every evaluation tick, no restart). Debug-gated steppers (same master gate as
        // the board-tuning rows below) so the Modules section stays compact in normal play,
        // but they LIVE here, right under the Wall see-through toggle they tune. Wrapped in
        // a collapsed-by-default EXPANDER row (accordion idiom, like the debug Element
        // chooser): the four steppers only show while "Wall fade tuning" is expanded.
        WallFadeTuning.Bind();
        var wallFadeHeader = Row();
        RegisterDebugRow(wallFadeHeader.gameObject, () => true);
        Label(wallFadeHeader, "Wall fade tuning", 16f, flexible: true);
        CycleButton(wallFadeHeader, 40f,
            () => _wallFadeOpen ? "-" : "+",
            () => _wallFadeOpen = !_wallFadeOpen);
        AddWallFadeRow("Fade on", WallFadeTuning.OnFraction, 0.05f, 0.05f, 0.95f,
            v => $"{v * 100f:0}%");
        AddWallFadeRow("Fade off", WallFadeTuning.OffFraction, 0.05f, 0.01f, 0.95f,
            v => $"{v * 100f:0}%");
        AddWallFadeRow("Unfade (moved)", WallFadeTuning.ExitDwellMoved, 0.5f, 0.1f, 60f,
            v => $"{v:0.0}s");
        AddWallFadeRow("Unfade (still)", WallFadeTuning.ExitDwellStationary, 0.5f, 0.1f, 120f,
            v => $"{v:0.0}s");

        Toggle(Loc.Mod("disable_post"),
            () => Plugin.DisablePostProcessing.Value,
            v => Plugin.DisablePostProcessing.Value = v);

        var note = Row(22f);
        Label(note, Loc.Mod("applies_next_start"), 12f, flexible: true);

        Section(Loc.Mod("board"));

        // Control-board model: cycles Oak → Steel → Bronze (mod 3), mirroring the Turn
        // enum cycle above. CardsDriver subscribes to Board.SettingChanged and rebuilds
        // the tray live, so the newly selected board loads without leaving the panel.
        var boardRow = Row();
        Label(boardRow, Loc.Mod("control_board"), 16f, flexible: true);
        CycleButton(boardRow, 100f,
            () => CardsConfig.Board.Value.ToString(),
            () => CardsConfig.Board.Value =
                (ControlBoard)(((int)CardsConfig.Board.Value + 1) % 3));

        Section(Loc.Mod("display"));

        // Re-spawn / hide the combat log window (item 6): SHOW clears the user-closed flag
        // set by the panel's X button and reconverts it at the persisted pose; HIDE releases
        // it back to its 2D home and keeps it from auto-reappearing.
        Toggle(Loc.Mod("show_combat_log"),
            () => CombatLogSurface.UserVisible,
            v => CombatLogSurface.SetUserVisible(v, "settings"));

        // MSAA on the VR eye textures ([RenderQuality] MsaaLevel — Rig.RenderQuality):
        // the game's own AA lived in the PostProcessLayer the mod kill-switches, so this
        // is the ONLY anti-aliasing in VR. Cycles Off → 2x → 4x → 8x. Applies IMMEDIATELY:
        // the rig's per-frame RenderQuality tick re-asserts QualitySettings.antiAliasing
        // and pushes the level to the XR display subsystem, which re-allocates the eye
        // textures live — no rig rebuild, no restart.
        var msaaRow = Row();
        Label(msaaRow, "MSAA", 16f, flexible: true);
        CycleButton(msaaRow, 100f, RenderQuality.MsaaLabel, RenderQuality.CycleMsaa);

        // Supersampling ([RenderQuality] EyeResolutionScale — Rig.RenderQuality): the
        // eye-texture resolution scale, 0.8–2.0 in 0.1 steps. NOTE: MSAA is proven DEAD
        // under VDXR (the runtime caps the swapchain at 1x — EYE-TARGET DIAG verdict), so
        // this brute-force lever is the working anti-aliasing control there. Applies live
        // (the swapchain re-allocates) and persists (BepInEx saves on set).
        var ssRow = Row();
        Label(ssRow, "Supersampling", 16f, flexible: true);
        MiniStepper(ssRow, RenderQuality.EyeScaleLabel, RenderQuality.StepEyeScale);
        var msaaNote = Row(18f);
        Label(msaaNote, "MSAA has no effect under VDXR — use supersampling", 12f, flexible: true);

        Section(Loc.Mod("mixed_reality"));

        // MR chroma-key mode (item 7): disables all skyboxes and clears the sky/background to
        // the key color so a compositor (Virtual Desktop) can passthrough-composite the room.
        Toggle(Loc.Mod("mixed_reality"),
            () => MixedReality.Enabled.Value,
            v => MixedReality.Enabled.Value = v);
        var mrColorRow = Row();
        Label(mrColorRow, Loc.Mod("key_color"), 16f, flexible: true);
        CycleButton(mrColorRow, 100f, () => MixedReality.KeyColorName, MixedReality.CycleKeyColor);

        Section(Loc.Mod("avatar"));

        // Multiplayer avatar (always visible while the panel is open, NOT debug-gated): pick one of
        // the three head masks (writes [Net] MaskId — synchronized so other VR players see it) and
        // toggle a local self-preview mirror (writes [Net] MirrorEnabled). Both live-apply:
        // LocalRigSampler stamps MaskId on every send and AvatarMirror reads MirrorEnabled each tick.
        // Force the [Net] entries bound so the picker works even with the networking hook off.
        Net.NetModule.BindConfig();
        var maskRow = Row();
        Label(maskRow, Loc.Mod("head_mask"), 16f, flexible: true);
        CycleButton(maskRow, 120f, MaskLabel, CycleMask);

        // Hand style: cycles Glove → Plate → Arcane (writes [Hands] HandStyle; HandsDriver
        // subscribes SettingChanged and rebuilds the hand visuals live, mirroring the
        // control-board selector). Synchronized: the choice rides the avatar rig packet
        // (AvatarState.HandStyle) so other VR players see it on your remote avatar.
        var handStyleRow = Row();
        Label(handStyleRow, Loc.Mod("hands"), 16f, flexible: true);
        CycleButton(handStyleRow, 120f, HandStyleLabel, CycleHandStyle);

        Toggle(Loc.Mod("mirror"),
            () => Net.NetModule.MirrorEnabled != null && Net.NetModule.MirrorEnabled.Value,
            v => { if (Net.NetModule.MirrorEnabled != null) Net.NetModule.MirrorEnabled.Value = v; });

        // How much of OTHER players' control boards this client renders (Off / only in the action
        // phase / always). Writes [Net] RemoteBoards. The anti-cheat reveal gate always applies on
        // top: a remote's round cards stay BACKS until the secret selection phase ends.
        var remoteBoardsRow = Row();
        Label(remoteBoardsRow, Loc.Mod("remote_boards"), 16f, flexible: true);
        CycleButton(remoteBoardsRow, 150f, RemoteBoardsLabel, CycleRemoteBoards);

        BuildDebugSection();

        // Mod layer in VR (inline 5s remain the dev-sim fallback; CAMERA-POLICY §2).
        if (_holder == null)
            return;
        VRLayers.Apply(_holder.gameObject);
        _holder.gameObject.SetActive(false);
    }

    // ---- Debug — Board tuning (Part E) ---------------------------------------------------------

    /// <summary>
    /// The in-VR DEBUG MENU (Part E): live-tune every board-attached element PER BOARD and save
    /// to config. Built from the existing panel helpers; every stepper closes over the per-board
    /// <see cref="CardsConfig"/> entries, so writing a value both persists (BepInEx) AND live-applies
    /// (CardsDriver subscribes to each entry's SettingChanged). Gated behind [Cards] DebugMenu — the
    /// rows are built once but shown only while the toggle is on (the 0.25 s refresher flips them).
    /// </summary>
    private void BuildDebugSection()
    {
        Section(Loc.Mod("debug_board_tuning"));

        Toggle(Loc.Mod("enable_board_tuning"),
            () => CardsConfig.DebugMenu.Value,
            v =>
            {
                CardsConfig.DebugMenu.Value = v;
                if (v)
                    VRLog.Info("Cards", "Debug board-tuning menu enabled — live per-board element tuning is now visible.");
            });

        // CATEGORY chooser — ACCORDION (exactly the Element chooser pattern below; the old
        // compact CYCLE button needed up to N-1 clicks to reach a tab once the categories
        // grew to 9): the header row shows the selected category; pressing it expands one
        // option row PER category right below (the panel's ContentSizeFitter grows around
        // them); pressing an option selects that tab and collapses the list. Selecting a
        // tab also collapses the Element accordion, exactly like the old cycle did. The
        // Element chooser then walks only the CURRENT category's 2–5 elements.
        var catRow = Row();
        RegisterDebugRow(catRow.gameObject, () => true);
        Label(catRow, Loc.Mod("category"), 16f, flexible: true);
        CycleButton(catRow, 130f,
            () => DebugCategoryLabel(CurrentCategory) + (_categoryListOpen ? " -" : " +"),
            () => _categoryListOpen = !_categoryListOpen);

        // One option row per category (fixed 9 — built once; label refreshers mark the
        // selected tab, the shared visibility pass shows them only while expanded).
        for (int c = 0; c < DebugCategoryCount; c++)
        {
            int idx = c; // capture per row
            var catOptRow = Row(28f);
            RegisterDebugRow(catOptRow.gameObject, () => _categoryListOpen);
            Label(catOptRow, "", 13f); // fixed 28px gutter — reads as an indented sub-row
            (Button _, TextMeshProUGUI catText) = Button(catOptRow, "", 0f, () =>
            {
                _debugCategory = idx;
                _categoryListOpen = false; // select + collapse
                _elementListOpen = false;  // switching tabs collapses the element accordion
                RefreshAll();
            }, flexible: true);
            _refreshers.Add(() =>
                catText.text = (_debugCategory == idx ? "> " : "")
                               + DebugCategoryLabel((DebugCategory)idx));
        }

        // Board cycle (Oak/Steel/Bronze) — HIDDEN for GLOBAL categories (Fan, Hands apply to all boards).
        var boardRow = Row();
        RegisterDebugRow(boardRow.gameObject, () => !CategoryIsGlobal(CurrentCategory));
        Label(boardRow, Loc.Mod("board"), 16f, flexible: true);
        CycleButton(boardRow, 100f,
            () => CardsConfig.Board.Value.ToString(),
            () => CardsConfig.Board.Value = (ControlBoard)(((int)CardsConfig.Board.Value + 1) % 3));

        // Element chooser — ACCORDION (replaced the old cycle button, which needed up to
        // N-1 clicks to reach an element once the categories grew): the header row shows
        // the selected element; pressing it expands one option row PER element of the
        // CURRENT category right below (the panel's ContentSizeFitter grows around them,
        // exactly like every conditional debug row); pressing an option selects it and
        // collapses the list. Hidden for global categories and for single-element
        // categories (Board) where a chooser would be a no-op. Rows are plain uGUI
        // buttons built by the shared helpers, so poke AND laser keep working unchanged.
        var elemRow = Row();
        RegisterDebugRow(elemRow.gameObject,
            () => !CategoryIsGlobal(CurrentCategory) && CurrentCategoryElements.Length > 1);
        Label(elemRow, Loc.Mod("element"), 16f, flexible: true);
        CycleButton(elemRow, 130f,
            () => DebugElementLabel(CurrentElement()) + (_elementListOpen ? " -" : " +"),
            () => _elementListOpen = !_elementListOpen);

        // One option row per possible element slot (built once for the LARGEST category;
        // each row's visibility predicate + label refresher re-scope it to the current
        // category, so category switches never rebuild anything).
        int maxElements = 0;
        foreach (DebugElement[] els in CategoryElements)
        {
            if (els.Length > maxElements)
                maxElements = els.Length;
        }
        for (int i = 0; i < maxElements; i++)
        {
            int idx = i; // capture per row
            var optRow = Row(28f);
            RegisterDebugRow(optRow.gameObject,
                () => _elementListOpen && !CategoryIsGlobal(CurrentCategory)
                      && CurrentCategoryElements.Length > 1
                      && idx < CurrentCategoryElements.Length);
            Label(optRow, "", 13f); // fixed 28px gutter — reads as an indented sub-row
            (Button _, TextMeshProUGUI optText) = Button(optRow, "", 0f, () =>
            {
                DebugElement[] els = CurrentCategoryElements;
                if (idx < els.Length)
                    _categoryElement[_debugCategory] = idx;
                _elementListOpen = false; // select + collapse
                RefreshAll();
            }, flexible: true);
            _refreshers.Add(() =>
            {
                DebugElement[] els = CurrentCategoryElements;
                if (idx >= els.Length)
                    return; // row is hidden by its predicate anyway
                bool selected =
                    Mathf.Clamp(_categoryElement[_debugCategory], 0, els.Length - 1) == idx;
                optText.text = (selected ? "> " : "") + DebugElementLabel(els[idx]);
            });
        }

        // Per-element steppers (visible for the current per-board category + element). ---------
        bool PerBoard() => !CategoryIsGlobal(CurrentCategory);

        // X / Y / Z offset steppers (mm), each drives the selected element's active-board offset.
        AddOffsetStepper("X", 0);
        AddOffsetStepper("Y", 1);
        AddOffsetStepper("Z (proud)", 2);

        // Size / Scale stepper — hidden for elements with no size (Overlays, Initiative).
        var sizeRow = Row();
        RegisterDebugRow(sizeRow.gameObject, () => PerBoard() && ElementHasSize(CurrentElement()));
        Label(sizeRow, Loc.Mod("size"), 16f, flexible: true);
        MiniStepper(sizeRow, FormatSize, StepSize);

        // Spacing stepper — group gap (Rest disc gap / Confirm-Undo gap / inter-pile gap; Active COL step).
        var spacingRow = Row();
        RegisterDebugRow(spacingRow.gameObject, () => PerBoard() && ElementHasSpacing(CurrentElement()));
        Label(spacingRow, Loc.Mod("spacing"), 16f, flexible: true);
        MiniStepper(spacingRow, FormatSpacing, StepSpacing);

        // Active-only ROW step stepper (the grid's vertical spacing).
        var rowGapRow = Row();
        RegisterDebugRow(rowGapRow.gameObject, () => PerBoard() && CurrentElement() == DebugElement.Active);
        Label(rowGapRow, Loc.Mod("row_gap"), 16f, flexible: true);
        MiniStepper(rowGapRow, FormatActiveRowStep, StepActiveRowStep);

        // Shape cycle — shown only for the button GROUPS (Rest / Generic): flip Round <-> Square.
        var shapeRow = Row();
        RegisterDebugRow(shapeRow.gameObject, () => PerBoard() && ElementHasShape(CurrentElement()));
        Label(shapeRow, Loc.Mod("shape"), 16f, flexible: true);
        CycleButton(shapeRow, 100f, FormatShape, FlipShape);

        // Board-only Tilt / Yaw row (shown only when Element == Board).
        var tiltYawRow = Row();
        RegisterDebugRow(tiltYawRow.gameObject, () => PerBoard() && CurrentElement() == DebugElement.Board);
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
        var initDepthRow = Row();
        RegisterDebugRow(initDepthRow.gameObject, () => PerBoard() && CurrentElement() == DebugElement.Initiative);
        Label(initDepthRow, "3D depth", 16f, flexible: true);
        MiniStepper(initDepthRow,
            () => $"{WorldUIConfig.InitiativeDepthMaxSpreadPx.Value:0}px",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.InitiativeDepthMaxSpreadPx;
                e.Value = Mathf.Clamp(e.Value + d * 1f, 0f, 40f);
            });

        // Decision-only ROW-GAP row (shown only when Element == Decision): the GLOBAL
        // [WorldUI] DecisionRowGapPx — target vertical gap (uGUI px) between a docked
        // decision prompt's text block and its button row. GLOBAL but edited from this
        // element's rows (precedent: the initiative 3D-depth row above). Live:
        // DecisionDockSurface subscribes SettingChanged and re-compresses an OPEN dock
        // immediately; BepInEx persists on set.
        var decisionGapRow = Row();
        RegisterDebugRow(decisionGapRow.gameObject,
            () => PerBoard() && CurrentElement() == DebugElement.Decision);
        Label(decisionGapRow, "Entscheidungs-Abstand", 16f, flexible: true);
        MiniStepper(decisionGapRow,
            () => $"{WorldUIConfig.DecisionRowGapPx.Value:0}px",
            d =>
            {
                ConfigEntry<float> e = WorldUIConfig.DecisionRowGapPx;
                e.Value = Mathf.Clamp(e.Value + d * 2f, 0f, 60f);
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

        // Reset element (per-board categories only — resets the selected element).
        var actionRow = Row();
        RegisterDebugRow(actionRow.gameObject, PerBoard);
        Button(actionRow, Loc.Mod("reset_element"), 0f, ResetDebugElement, flexible: true);

        // "Knöpfe" — 3D-button geometry group (ButtonTuning, dev.gloomhavenvr.buttons.cfg):
        // the transient round-phase button group's offset/shape/cap size ([TransientButtons])
        // and the square-keycap geometry ([SquareCaps] Width/Height/Depth/Travel, 0 = the
        // authored default, shown as "Auto"). Wrapped in a collapsed-by-default EXPANDER
        // (same accordion idiom as "Wall fade tuning") under the Buttons tab so the tab
        // stays compact; the group spans several button elements, hence category-level.
        // Every entry live-applies WITHOUT restart: writing bumps ButtonTuning.Version and
        // PlayTray.TickStatus / ButtonCluster.Tick rebuild the affected caps on their next
        // tick. External writes (cfg edit/reload) refresh the rows via ButtonTuning.Changed
        // (subscribed in the constructor) on top of the 0.25 s refresh cadence.
        ButtonTuning.Bind();
        var buttonTuningHeader = Row();
        RegisterDebugRow(buttonTuningHeader.gameObject,
            () => CurrentCategory == DebugCategory.Buttons);
        Label(buttonTuningHeader, "Knöpfe", 16f, flexible: true);
        CycleButton(buttonTuningHeader, 40f,
            () => _buttonTuningOpen ? "-" : "+",
            () => _buttonTuningOpen = !_buttonTuningOpen);

        AddButtonTuningRow("Versatz X", ButtonTuning.TransientOffsetX, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm");
        AddButtonTuningRow("Versatz Y", ButtonTuning.TransientOffsetY, 0.005f, -0.30f, 0.30f,
            v => $"{v * 1000f:0}mm");
        // Shape cycle for the transient group (Round puck <-> Square keycap).
        if (ButtonTuning.TransientShape != null)
        {
            ConfigEntry<ButtonShape> shapeEntry = ButtonTuning.TransientShape;
            var transientShapeRow = Row();
            RegisterDebugRow(transientShapeRow.gameObject, ButtonTuningRowsVisible);
            Label(transientShapeRow, Loc.Mod("shape"), 16f, flexible: true);
            CycleButton(transientShapeRow, 100f,
                () => ShapeLabel(shapeEntry.Value),
                () => shapeEntry.Value = shapeEntry.Value == ButtonShape.Round
                    ? ButtonShape.Square
                    : ButtonShape.Round);
        }
        AddButtonTuningRow("Kappengröße", ButtonTuning.TransientCapSize, 0.002f, 0.015f, 0.09f,
            v => $"{v * 1000f:0}mm");
        AddButtonTuningAutoRow("Breite", ButtonTuning.SquareCapWidth, 0.005f, 0.02f, 0.20f);
        AddButtonTuningAutoRow("Höhe", ButtonTuning.SquareCapHeight, 0.005f, 0.015f, 0.20f);
        AddButtonTuningAutoRow("Tiefe", ButtonTuning.SquareCapDepth, 0.002f, 0.006f, 0.08f);
        AddButtonTuningAutoRow("Hub", ButtonTuning.PressTravel, 0.001f, 0.002f, 0.02f);

        // FAN category (GLOBAL): the hand-card fan's width/roundness/spacing. Live-applied by
        // CardsDriver (relayout) so tuning updates the fan immediately; grab/hover geometry derives
        // from the same radius/step so it stays aligned. Shown only under the Fan tab.
        AddFanStepper(Loc.Mod("fan_step"), CardsConfig.FanPerCardStepDegrees, 1f, 2f, 40f, v => $"{v:0}°");
        AddFanStepper(Loc.Mod("fan_arc"), CardsConfig.FanArcSweepDegrees, 1f, 20f, 180f, v => $"{v:0}°");
        AddFanStepper(Loc.Mod("fan_radius"), CardsConfig.FanEffectiveRadius, 0.002f, 0.05f, 0.4f,
            v => $"{v * 1000f:0}mm");
        AddFanStepper(Loc.Mod("fan_split"), CardsConfig.FanHoverSplitScale, 0.05f, 0.5f, 3f, v => $"{v:0.00}x");

        // Reveal-angle round 2: the palm-gate reveal is a pure hand-ROLL angle in DEGREES
        // (0 = palm down, 90 = thumb up, 180 = palm fully up; pitch/yaw irrelevant) +
        // the fan-out animation length, all live: UpdatePalmGate re-reads the degrees
        // every frame and CardFan reads the duration at each reveal, so tuning is
        // interactive in-headset. Same Fan tab as the geometry rows.
        // Bounds match the v3 asin-roll scale (0° flat … 90° palm to face; CardsConfig clamps 15-85 / 5-80).
        AddFanStepper("Reveal enter °", CardsConfig.RevealEnterDegrees, 5f, 15f, 85f, v => $"{v:0}°");
        AddFanStepper("Reveal exit °", CardsConfig.RevealExitDegrees, 5f, 5f, 80f, v => $"{v:0}°");
        AddFanStepper("Open time", CardsConfig.FanOpenDuration, 0.02f, 0f, 0.6f, v => $"{v * 1000f:0}ms");

        // HANDS category (GLOBAL, both hands; NOT per-board). PER-STYLE rework (user):
        // EVERY hand-tuning value below is stored PER HAND STYLE — the four seat values
        // as [Hands] {Style}GripPitchDegrees/{Style}LateralOffset/… in
        // dev.gloomhavenvr.hands.cfg (seeded once from the old shared seat controls +
        // per-style trims, so tuned values carried over to all three styles) and the
        // scale as the existing per-style [Hands] {Style}Scale in the main cfg. Each row
        // edits the style CURRENTLY worn; the "Stil" row cycles it right here so all
        // three sets are reachable without leaving the tab. Everything live-applies per
        // frame (VRHand.SyncVisualOffset re-reads the ACTIVE style's entries every
        // frame — a style switch re-seats/rescales instantly too). Force the config bound
        // (idempotent) so the seat rows work even if the Hands module hasn't inited.
        HandsConfig.Bind();
        var handsStyleRow = Row();
        RegisterDebugRow(handsStyleRow.gameObject, () => CurrentCategory == DebugCategory.Hands);
        Label(handsStyleRow, "Stil", 16f, flexible: true);
        CycleButton(handsStyleRow, 120f, HandStyleLabel, CycleHandStyle);
        var handsNote = Row(18f);
        RegisterDebugRow(handsNote.gameObject, () => CurrentCategory == DebugCategory.Hands);
        Label(handsNote, "Alle Werte gelten pro Stil", 12f, flexible: true);

        AddStyleStepper(Loc.Mod("size"), () => Plugin.HandStyleScale, 0.02f, 0.2f, 3f,
            v => $"{v:0.00}x");
        AddStyleStepper(Loc.Mod("hand_x"), () => HandsConfig.StyleSeatLateral, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm");
        AddStyleStepper(Loc.Mod("hand_y"), () => HandsConfig.StyleSeatVertical, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm");
        AddStyleStepper(Loc.Mod("hand_z"), () => HandsConfig.StyleSeatForward, 0.002f, -0.3f, 0.3f,
            v => $"{v * 1000f:0}mm");
        AddStyleStepper(Loc.Mod("hand_pitch"), () => HandsConfig.StyleSeatPitch, 1f, -90f, 90f,
            v => $"{v:0}°");

        // FIGURES category (GLOBAL): live-tune the HELD board-figure pose — the mini is centered
        // in the palm and hard to place offline (user hardware feedback). Each stepper writes a
        // [FigureGrab] entry, which persists (dev.gloomhavenvr.figuregrab.cfg) AND live-applies:
        // FigureGrabConfig subscribes every entry's SettingChanged and re-poses the currently-held
        // mini immediately (FigureGrabbable.ReapplyAll), so tuning is interactive in-headset. Force
        // the config bound so the steppers work even before the board module inits. Shown only under
        // the Figures tab.
        FigureGrabConfig.Bind();
        AddFigureToggle(Loc.Mod("fig_upright"), FigureGrabConfig.HeldUpright);
        AddFigureStepper(Loc.Mod("fig_x"), FigureGrabConfig.HeldOffsetSide, 0.002f, -0.2f, 0.2f,
            v => $"{v * 1000f:0}mm");
        AddFigureStepper(Loc.Mod("fig_y"), FigureGrabConfig.HeldOffsetUp, 0.002f, -0.2f, 0.2f,
            v => $"{v * 1000f:0}mm");
        AddFigureStepper(Loc.Mod("fig_z"), FigureGrabConfig.HeldOffsetForward, 0.002f, -0.2f, 0.2f,
            v => $"{v * 1000f:0}mm");
        AddFigureStepper(Loc.Mod("fig_tilt"), FigureGrabConfig.HeldTiltDegrees, 5f, -180f, 180f,
            v => $"{v:0}°");
        AddFigureStepper(Loc.Mod("fig_yaw"), FigureGrabConfig.HeldFaceYawDegrees, 5f, -180f, 180f,
            v => $"{v:0}°");
        AddFigureStepper(Loc.Mod("fig_scale"), FigureGrabConfig.HeldScale, 0.1f, 0.2f, 5f,
            v => $"{v:0.00}x");

        // WRIST category (GLOBAL, item 10): live-tune the left-wrist overview HUD pose — its TILT
        // (pitch/yaw/roll on top of the flat-on-hand base) and its POSITION offset (X/Y/Z, meters)
        // from the wrist anchor. WristHud re-reads + re-applies these every Tick (WristHud.ApplyPose),
        // so nudging a stepper moves the watch-face HUD immediately. Backed today by WristHud's local
        // statics (in-session); the parent swaps them for [WorldUI] config entries (persist + live) —
        // see the report. Shown only under the Wrist tab.
        AddWristStepper(Loc.Mod("wrist_pitch"), () => WristHud.PitchDeg, v => WristHud.PitchDeg = v, 2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_yaw"),   () => WristHud.YawDeg,   v => WristHud.YawDeg = v,   2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_roll"),  () => WristHud.RollDeg,  v => WristHud.RollDeg = v,  2f, v => $"{v:0}°");
        AddWristStepper(Loc.Mod("wrist_x"), () => WristHud.OffsetX, v => WristHud.OffsetX = v, 0.002f, v => $"{v * 1000f:0}mm");
        AddWristStepper(Loc.Mod("wrist_y"), () => WristHud.OffsetY, v => WristHud.OffsetY = v, 0.002f, v => $"{v * 1000f:0}mm");
        AddWristStepper(Loc.Mod("wrist_z"), () => WristHud.OffsetZ, v => WristHud.OffsetZ = v, 0.002f, v => $"{v * 1000f:0}mm");

        // Single visibility pass: master DebugMenu gate ANDed with each row's own predicate
        // (category/element scope). Never more than a handful of rows visible at once.
        _refreshers.Add(() =>
        {
            bool on = CardsConfig.DebugMenu.Value;
            for (int i = 0; i < _debugRows.Count; i++)
            {
                GameObject go = _debugRows[i];
                // A row can be Unity-null if its holder was destroyed out from under us
                // (e.g. a scene unload) while the panel instance survived; skip it rather
                // than let go.activeSelf throw every refresh (bug #5 flood). The Build()
                // list-clear below is the real fix; this is the belt-and-braces guard.
                if (go == null)
                    continue;
                bool show = on && _debugRowVisible[i]();
                if (go.activeSelf != show)
                    go.SetActive(show);
            }
        });
    }

    /// <summary>Register a debug row for the master on/off gate + its own category/element visibility predicate.</summary>
    private void RegisterDebugRow(GameObject go, Func<bool> visible)
    {
        _debugRows.Add(go);
        _debugRowVisible.Add(visible);
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
        var row = Row();
        // Every per-board element carries a Vector3 offset, so this shows for any non-global category.
        RegisterDebugRow(row.gameObject, () => !CategoryIsGlobal(CurrentCategory));
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
        var row = Row();
        RegisterDebugRow(row.gameObject,
            () => !CategoryIsGlobal(CurrentCategory) && CurrentElement() == DebugElement.Piles);
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
    /// Wall-fade threshold stepper row (lives in the Modules section next to the Wall
    /// see-through toggle, but is debug-gated like the board-tuning rows: visible only
    /// while [Cards] DebugMenu is on). Bound directly to a live [WallFade] entry —
    /// writing persists (BepInEx) and applies on WallSegmentFade's next evaluation tick
    /// (the driver re-reads the clamped WallFadeTuning accessors every frame).
    /// </summary>
    private void AddWallFadeRow(string label, ConfigEntry<float>? entry, float step, float min,
        float max, Func<float, string> format)
    {
        if (entry == null)
            return; // WallFadeTuning.Bind() failed (config dir unwritable) — skip the row
        var row = Row();
        RegisterDebugRow(row.gameObject, () => _wallFadeOpen); // collapsed-by-default expander
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>Shared visibility of the "Knöpfe" expander's option rows (Buttons tab + expanded).</summary>
    private bool ButtonTuningRowsVisible() =>
        CurrentCategory == DebugCategory.Buttons && _buttonTuningOpen;

    /// <summary>
    /// "Knöpfe" stepper row bound directly to a live ButtonTuning <see cref="ConfigEntry{T}"/>
    /// (mm readout). Writing persists (BepInEx) and live-applies: the entry's SettingChanged
    /// bumps ButtonTuning.Version and PlayTray/ButtonCluster rebuild the caps on their next
    /// tick. Skipped entirely if Bind() failed (config dir unwritable) — same policy as
    /// <see cref="AddWallFadeRow"/>. Shown only while the expander is open on the Buttons tab.
    /// </summary>
    private void AddButtonTuningRow(string label, ConfigEntry<float>? entry, float step, float min,
        float max, Func<float, string> format)
    {
        if (entry == null)
            return;
        var row = Row();
        RegisterDebugRow(row.gameObject, ButtonTuningRowsVisible);
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// "Knöpfe" stepper row for the [SquareCaps] entries where 0 = "authored default"
    /// (readout "Auto"). Stepping + from Auto enters the range at its minimum; stepping
    /// - below the minimum collapses back to Auto (0) — so the authored look is always
    /// one press away. Live-applies exactly like <see cref="AddButtonTuningRow"/>.
    /// </summary>
    private void AddButtonTuningAutoRow(string label, ConfigEntry<float>? entry, float step,
        float min, float max)
    {
        if (entry == null)
            return;
        var row = Row();
        RegisterDebugRow(row.gameObject, ButtonTuningRowsVisible);
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => entry.Value <= 0f ? "Auto" : $"{entry.Value * 1000f:0}mm",
            d =>
            {
                float v = entry.Value <= 0f
                    ? (d > 0 ? min : 0f)              // from Auto: + enters at the minimum
                    : entry.Value + d * step;
                entry.Value = v < min ? 0f : Mathf.Min(v, max); // below min → back to Auto
            });
    }

    /// <summary>
    /// FAN category (global) stepper bound to a global fan <see cref="ConfigEntry{T}"/>. Writing the
    /// entry persists (BepInEx) and live-applies (CardsDriver relayouts the open fan). Clamped to
    /// [min,max]. Shown only under the Fan tab.
    /// </summary>
    private void AddFanStepper(string label, ConfigEntry<float> entry, float step, float min, float max,
        Func<float, string> format)
    {
        var row = Row();
        RegisterDebugRow(row.gameObject, () => CurrentCategory == DebugCategory.Fan);
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// Per-STYLE hand stepper row: drives the entry of the style CURRENTLY worn
    /// (indexed by [Hands] HandStyle into the given per-style entry array), so the Hands
    /// tab only ever shows the active style's values — since the per-style rework EVERY
    /// hand-tuning row (scale + the four seat controls) goes through here. Writing
    /// persists (BepInEx) and live-applies (VRHand.SyncVisualOffset re-reads per frame).
    /// Shown only under the Hands tab; shows "—" while the config is not bound yet.
    /// </summary>
    private void AddStyleStepper(string label, Func<ConfigEntry<float>[]?> entries, float step,
        float min, float max, Func<float, string> format)
    {
        var row = Row();
        RegisterDebugRow(row.gameObject, () => CurrentCategory == DebugCategory.Hands);
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
    /// Item 2b: a global held-figure stepper row bound directly to a [FigureGrab]
    /// <see cref="ConfigEntry{T}"/> (offset mm / rotation ° / scale). Writing the entry persists
    /// (BepInEx) and live-applies (FigureGrabConfig re-poses the held mini on SettingChanged).
    /// Clamped to [min,max]. Shown only under the Figures tab.
    /// </summary>
    private void AddFigureStepper(string label, ConfigEntry<float> entry, float step, float min, float max,
        Func<float, string> format)
    {
        var row = Row();
        RegisterDebugRow(row.gameObject, () => CurrentCategory == DebugCategory.Figures);
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(entry.Value),
            d => entry.Value = Mathf.Clamp(entry.Value + d * step, min, max));
    }

    /// <summary>
    /// Item 10: a global WRIST-HUD pose stepper row bound to a WristHud getter/setter (degrees for
    /// pitch/yaw/roll, mm for the X/Y/Z offset). Setting the value re-poses the wrist HUD live —
    /// WristHud.ApplyPose re-reads it every Tick — and (once the parent wires the [WorldUI] config
    /// entries behind these accessors) persists via BepInEx. Shown only under the Wrist tab.
    /// </summary>
    private void AddWristStepper(string label, Func<float> get, Action<float> set, float step,
        Func<float, string> format)
    {
        var row = Row();
        RegisterDebugRow(row.gameObject, () => CurrentCategory == DebugCategory.Wrist);
        Label(row, label, 16f, flexible: true);
        MiniStepper(row,
            () => format(get()),
            d => set(get() + d * step));
    }

    /// <summary>Held-figure boolean row (e.g. Upright) — same live-apply/persist path as the steppers.</summary>
    private void AddFigureToggle(string label, ConfigEntry<bool> entry)
    {
        var row = Row();
        RegisterDebugRow(row.gameObject, () => CurrentCategory == DebugCategory.Figures);
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

    /// <summary>Localized name of a debug CATEGORY tab (the Category cycle readout). Board/Hands reuse existing element labels.</summary>
    private static string DebugCategoryLabel(DebugCategory c) => c switch
    {
        DebugCategory.Buttons => Loc.Mod("cat_buttons"),
        DebugCategory.Panels => Loc.Mod("cat_panels"),
        DebugCategory.Overlays => Loc.Mod("overlays"),
        DebugCategory.Widgets => Loc.Mod("cat_widgets"),
        DebugCategory.Board => Loc.Mod("board"),
        DebugCategory.Fan => Loc.Mod("cat_fan"),
        DebugCategory.Hands => Loc.Mod("hands"),
        DebugCategory.Figures => Loc.Mod("cat_figures"),
        DebugCategory.Wrist => Loc.Mod("cat_wrist"),
        _ => c.ToString(),
    };

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
        _ => e.ToString(),
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
                break;
            }
            case DebugElement.Generic:
            {
                CardsConfig.ConfirmUndoSize(b).Value = (float)CardsConfig.ConfirmUndoSize(b).DefaultValue;
                CardsConfig.GenericButtonSpacing(b).Value = (float)CardsConfig.GenericButtonSpacing(b).DefaultValue;
                CardsConfig.GenericButtonShape(b).Value = (ButtonShape)CardsConfig.GenericButtonShape(b).DefaultValue;
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
        go.transform.SetParent(_root!.transform, worldPositionStays: false);
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
