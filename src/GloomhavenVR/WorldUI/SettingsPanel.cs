using System;
using System.Collections.Generic;
using BepInEx.Configuration;
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
    private enum DebugElement { Rest, Generic, Overlays, Initiative, Active, Piles, Board }
    private const int DebugElementCount = 7;
    private int _debugElement;
    private readonly List<GameObject> _debugRows = new(8);
    private bool _healLogged;           // change-dedup for the out-of-view heal log
    private bool _respawnRequested;     // every OPEN drops the panel in view in front of the head

    // ---- cross-module seam (test #15) ------------------------------------------------------

    /// <summary>The driver-owned live instance (single WorldUI driver; null after shutdown).</summary>
    private static SettingsPanel? _instance;

    public SettingsPanel()
    {
        _instance = this;
        Loc.OnChanged += RefreshLanguage; // live language following: rebuild while open
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
        _refreshers.Clear();
        _debugRows.Clear();
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
        _holder.localScale = Vector3.one * worldScale;

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
            _frame.localScale = Vector3.one * Mathf.Clamp(WorldUIConfig.SettingsScale.Value, 0.5f, 2f);

            // Orientation at events only (never per tick — the combat log's test #20 rule).
            int poseVersion = VRRigDriver.RigPoseVersion;
            if (!_placedFromConfig || poseVersion != _facedPoseVersion || healed)
            {
                _frame.rotation = facing;
                _facedPoseVersion = poseVersion;
            }
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
        _frame.localScale = Vector3.one * Mathf.Clamp(WorldUIConfig.SettingsScale.Value, 0.5f, 2f);
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
        WorldUIConfig.SettingsScale.Value = Mathf.Clamp(_frame.localScale.x, 0.5f, 2f);
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

        Toggle(Loc.Mod("seated_mode"),
            () => ComfortSettings.IsBound && ComfortSettings.SeatedMode.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.SeatedMode.Value = v; });

        Stepper(Loc.Mod("table_height"),
            () => ComfortSettings.IsBound ? $"{ComfortSettings.TableHeightOffset.Value:+0.00;-0.00;0.00}m" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.TableHeightOffset.Value =
                    Mathf.Clamp(ComfortSettings.TableHeightOffset.Value + delta * 0.05f, -0.4f, 0.6f);
            });

        var vignetteRow = Row();
        Label(vignetteRow, Loc.Mod("vignette"), 16f, flexible: true);
        ToggleButton(vignetteRow,
            () => ComfortSettings.IsBound && ComfortSettings.VignetteEnabled.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.VignetteEnabled.Value = v; });
        MiniStepper(vignetteRow,
            () => ComfortSettings.IsBound ? $"{ComfortSettings.VignetteStrength.Value:0.00}" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.VignetteStrength.Value =
                    Mathf.Clamp(ComfortSettings.VignetteStrength.Value + delta * 0.1f, 0.2f, 1f);
            });

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

        Section(Loc.Mod("mixed_reality"));

        // MR chroma-key mode (item 7): disables all skyboxes and clears the sky/background to
        // the key color so a compositor (Virtual Desktop) can passthrough-composite the room.
        Toggle(Loc.Mod("mixed_reality"),
            () => MixedReality.Enabled.Value,
            v => MixedReality.Enabled.Value = v);
        var mrColorRow = Row();
        Label(mrColorRow, Loc.Mod("key_color"), 16f, flexible: true);
        CycleButton(mrColorRow, 100f, () => MixedReality.KeyColorName, MixedReality.CycleKeyColor);

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

        // Board cycle (mirrors the Control-board cycle so the tuning rows key off the ACTIVE board).
        var boardRow = Row();
        _debugRows.Add(boardRow.gameObject);
        Label(boardRow, Loc.Mod("board"), 16f, flexible: true);
        CycleButton(boardRow, 100f,
            () => CardsConfig.Board.Value.ToString(),
            () => CardsConfig.Board.Value = (ControlBoard)(((int)CardsConfig.Board.Value + 1) % 3));

        // Element cycle (Rest, Generic, Overlays, Initiative, Active, Piles, Board).
        var elemRow = Row();
        _debugRows.Add(elemRow.gameObject);
        Label(elemRow, Loc.Mod("element"), 16f, flexible: true);
        CycleButton(elemRow, 130f,
            () => DebugElementLabel((DebugElement)_debugElement),
            () => _debugElement = (_debugElement + 1) % DebugElementCount);

        // X / Y / Z offset steppers (mm), each drives the selected element's active-board offset.
        AddOffsetStepper("X", 0);
        AddOffsetStepper("Y", 1);
        AddOffsetStepper("Z (proud)", 2);

        // Size / Scale stepper — hidden for elements with no size (Overlays, Initiative).
        var sizeRow = Row();
        _debugRows.Add(sizeRow.gameObject);
        GameObject sizeGo = sizeRow.gameObject;
        Label(sizeRow, Loc.Mod("size"), 16f, flexible: true);
        MiniStepper(sizeRow, FormatSize, StepSize);

        // Spacing stepper — group gap (Rest disc gap / Confirm-Undo gap / inter-pile gap; Active COL step).
        var spacingRow = Row();
        _debugRows.Add(spacingRow.gameObject);
        GameObject spacingGo = spacingRow.gameObject;
        Label(spacingRow, Loc.Mod("spacing"), 16f, flexible: true);
        MiniStepper(spacingRow, FormatSpacing, StepSpacing);

        // Active-only ROW step stepper (the grid's vertical spacing).
        var rowGapRow = Row();
        _debugRows.Add(rowGapRow.gameObject);
        GameObject rowGapGo = rowGapRow.gameObject;
        Label(rowGapRow, Loc.Mod("row_gap"), 16f, flexible: true);
        MiniStepper(rowGapRow, FormatActiveRowStep, StepActiveRowStep);

        // Shape cycle — shown only for the button GROUPS (Rest / Generic): flip Round <-> Square.
        var shapeRow = Row();
        _debugRows.Add(shapeRow.gameObject);
        GameObject shapeGo = shapeRow.gameObject;
        Label(shapeRow, Loc.Mod("shape"), 16f, flexible: true);
        CycleButton(shapeRow, 100f, FormatShape, FlipShape);

        // Board-only Tilt / Yaw row (shown only when Element == Board).
        var tiltYawRow = Row();
        _debugRows.Add(tiltYawRow.gameObject);
        GameObject tiltYawGo = tiltYawRow.gameObject;
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

        // Reset element (the "Copy Oak→active" button was removed in item 3b).
        var actionRow = Row();
        _debugRows.Add(actionRow.gameObject);
        Button(actionRow, Loc.Mod("reset_element"), 0f, ResetDebugElement, flexible: true);

        // Visibility: show the tuning rows only while DebugMenu is on; the conditional rows
        // (Size, Spacing, Row gap, Shape, Tilt/Yaw) additionally gate on the selected element.
        _refreshers.Add(() =>
        {
            bool on = CardsConfig.DebugMenu.Value;
            var el = (DebugElement)_debugElement;
            for (int i = 0; i < _debugRows.Count; i++)
            {
                GameObject go = _debugRows[i];
                bool show = on;
                if (ReferenceEquals(go, sizeGo)) show = on && ElementHasSize(el);
                else if (ReferenceEquals(go, spacingGo)) show = on && ElementHasSpacing(el);
                else if (ReferenceEquals(go, rowGapGo)) show = on && el == DebugElement.Active;
                else if (ReferenceEquals(go, shapeGo)) show = on && ElementHasShape(el);
                else if (ReferenceEquals(go, tiltYawGo)) show = on && el == DebugElement.Board;
                if (go.activeSelf != show)
                    go.SetActive(show);
            }
        });
    }

    /// <summary>Elements that expose a Size/Scale stepper (Rest disc, Generic side, Active/Pile scale, Board scale).</summary>
    private static bool ElementHasSize(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic or DebugElement.Active
        or DebugElement.Piles or DebugElement.Board;

    /// <summary>Group elements that expose a Spacing stepper (both button groups, the piles, the active grid).</summary>
    private static bool ElementHasSpacing(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic or DebugElement.Piles or DebugElement.Active;

    /// <summary>The two button GROUPS carry a Round/Square shape toggle.</summary>
    private static bool ElementHasShape(DebugElement e) =>
        e is DebugElement.Rest or DebugElement.Generic;

    private void AddOffsetStepper(string label, int axis)
    {
        var row = Row();
        _debugRows.Add(row.gameObject);
        Label(row, label, 16f, flexible: true);
        MiniStepper(row, () => FormatOffset(axis), d => StepOffset(axis, d));
    }

    /// <summary>The offset ConfigEntry the selected element edits (all board-local Vector3s).</summary>
    private ConfigEntry<Vector3>? ElementOffsetEntry()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return (DebugElement)_debugElement switch
        {
            DebugElement.Rest => CardsConfig.RestButtonOffset(b),
            DebugElement.Generic => CardsConfig.ConfirmUndoOffset(b),
            DebugElement.Overlays => CardsConfig.SlotOverlayOffset(b),
            DebugElement.Initiative => CardsConfig.InitiativeOffset(b),
            DebugElement.Active => CardsConfig.ActiveOffset(b),
            DebugElement.Piles => CardsConfig.PileOffset(b),
            DebugElement.Board => CardsConfig.BoardPosOffset(b),
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
        return (DebugElement)_debugElement switch
        {
            DebugElement.Rest => $"{CardsConfig.RestButtonDiameter(b).Value * 1000f:0}mm",
            DebugElement.Generic => $"{CardsConfig.ConfirmUndoSize(b).Value * 1000f:0}mm",
            DebugElement.Active => $"{CardsConfig.ActiveCardScale(b).Value:0.00}x",
            DebugElement.Piles => $"{CardsConfig.PileScale(b).Value:0.00}x",
            DebugElement.Board => $"{CardsConfig.BoardScale(b).Value:0.00}x",
            _ => "-",
        };
    }

    private void StepSize(int delta)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch ((DebugElement)_debugElement)
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
        }
    }

    // ---- Spacing / Row-gap / Shape steppers (round 2) --------------------------------------

    /// <summary>Spacing readout for the selected group element (mm for meter gaps, factor for the active COL step).</summary>
    private string FormatSpacing()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        return (DebugElement)_debugElement switch
        {
            DebugElement.Rest => $"{CardsConfig.RestButtonSpacing(b).Value * 1000f:0}mm",
            DebugElement.Generic => $"{CardsConfig.GenericButtonSpacing(b).Value * 1000f:0}mm",
            DebugElement.Piles => $"{CardsConfig.PileSpacing(b).Value * 1000f:0}mm",
            DebugElement.Active => $"{CardsConfig.ActiveGridSpacing(b).Value.x:0.00}",
            _ => "-",
        };
    }

    private void StepSpacing(int delta)
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch ((DebugElement)_debugElement)
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
        return (DebugElement)_debugElement switch
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
        _ => e.ToString(),
    };

    /// <summary>Localized cap-shape name (the Shape cycle readout).</summary>
    private static string ShapeLabel(ButtonShape shape) =>
        shape == ButtonShape.Round ? Loc.Mod("round") : Loc.Mod("square");

    /// <summary>Flip the selected group's cap shape Round &lt;-&gt; Square (live rebuild via CardsDriver).</summary>
    private void FlipShape()
    {
        ControlBoard b = CardsConfig.CurrentBoard;
        switch ((DebugElement)_debugElement)
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
        switch ((DebugElement)_debugElement)
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
                break;
            }
            case DebugElement.Board:
            {
                CardsConfig.BoardTilt(b).Value = (float)CardsConfig.BoardTilt(b).DefaultValue;
                CardsConfig.BoardYaw(b).Value = (float)CardsConfig.BoardYaw(b).DefaultValue;
                CardsConfig.BoardScale(b).Value = (float)CardsConfig.BoardScale(b).DefaultValue;
                break;
            }
        }
        VRLog.Info("Cards", $"Debug: reset {(DebugElement)_debugElement} for {b} to defaults.");
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
