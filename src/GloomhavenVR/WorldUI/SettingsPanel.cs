using System;
using System.Collections.Generic;
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
    private bool _healLogged;           // change-dedup for the out-of-view heal log
    private bool _respawnRequested;     // every OPEN drops the panel in view in front of the head

    // ---- cross-module seam (test #15) ------------------------------------------------------

    /// <summary>The driver-owned live instance (single WorldUI driver; null after shutdown).</summary>
    private static SettingsPanel? _instance;

    public SettingsPanel() => _instance = this;

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
        _pin.SetLabel(follow ? "FOLLOW" : "PINNED");
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

        Section("Comfort");

        // Table scale: SetScaleMultiplier applies live around the head + persists.
        Stepper("Table scale",
            () => $"{CurrentScaleMultiplier():0.00}x",
            delta =>
            {
                if (ComfortSettings.IsBound)
                    Comfort.SetScaleMultiplier(CurrentScaleMultiplier() + delta * 0.25f);
            });

        // Turn mode + degrees.
        var turnRow = Row();
        Label(turnRow, "Turning", 16f, flexible: true);
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

        Toggle("Seated mode",
            () => ComfortSettings.IsBound && ComfortSettings.SeatedMode.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.SeatedMode.Value = v; });

        Stepper("Table height",
            () => ComfortSettings.IsBound ? $"{ComfortSettings.TableHeightOffset.Value:+0.00;-0.00;0.00}m" : "-",
            delta =>
            {
                if (!ComfortSettings.IsBound) return;
                ComfortSettings.TableHeightOffset.Value =
                    Mathf.Clamp(ComfortSettings.TableHeightOffset.Value + delta * 0.05f, -0.4f, 0.6f);
            });

        var vignetteRow = Row();
        Label(vignetteRow, "Vignette", 16f, flexible: true);
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

        Toggle("Free movement",
            () => ComfortSettings.IsBound && ComfortSettings.FreeMovement.Value,
            v => { if (ComfortSettings.IsBound) ComfortSettings.FreeMovement.Value = v; });

        var grabRow = Row();
        Label(grabRow, "World grab", 16f, flexible: true);
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
        Button(recenterRow, "Recenter now", 0f, Comfort.RequestRecenter, flexible: true);

        Section("Modules");

        Toggle("Dominant hand right",
            () => !string.Equals(Plugin.PrimaryHand.Value, "Left", StringComparison.OrdinalIgnoreCase),
            v => Plugin.PrimaryHand.Value = v ? "Right" : "Left");

        Toggle("Board: far ray only",
            () => BoardConfigSafe(() => Board.BoardConfig.ForceFarMode.Value),
            v => { if (Board.BoardConfig.ForceFarMode != null) Board.BoardConfig.ForceFarMode.Value = v; });

        Toggle("World UI surfaces",
            () => WorldUIConfig.Master.Value,
            v => WorldUIConfig.Master.Value = v);

        Toggle("Disable post-processing*",
            () => Plugin.DisablePostProcessing.Value,
            v => Plugin.DisablePostProcessing.Value = v);

        var note = Row(22f);
        Label(note, "* applies on next VR start", 12f, flexible: true);

        Section("Anzeige");

        // Re-spawn / hide the combat log window (item 6): SHOW clears the user-closed flag
        // set by the panel's X button and reconverts it at the persisted pose; HIDE releases
        // it back to its 2D home and keeps it from auto-reappearing.
        Toggle("Kampflog anzeigen",
            () => CombatLogSurface.UserVisible,
            v => CombatLogSurface.SetUserVisible(v, "settings"));

        Section("Mixed Reality");

        // MR chroma-key mode (item 7): disables all skyboxes and clears the sky/background to
        // the key color so a compositor (Virtual Desktop) can passthrough-composite the room.
        Toggle("Mixed Reality",
            () => MixedReality.Enabled.Value,
            v => MixedReality.Enabled.Value = v);
        var mrColorRow = Row();
        Label(mrColorRow, "Key-Farbe", 16f, flexible: true);
        CycleButton(mrColorRow, 100f, () => MixedReality.KeyColorName, MixedReality.CycleKeyColor);

        // Mod layer in VR (inline 5s remain the dev-sim fallback; CAMERA-POLICY §2).
        if (_holder == null)
            return;
        VRLayers.Apply(_holder.gameObject);
        _holder.gameObject.SetActive(false);
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
            new Color(0.75f, 0.55f, 0.2f), "FOLLOW", TogglePin);
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
        (Button button, TextMeshProUGUI text) = Button(row, read() ? "On" : "Off", 58f,
            () => { write(!read()); RefreshAll(); });
        Image image = (Image)button.targetGraphic;
        _refreshers.Add(() =>
        {
            bool on = read();
            text.text = on ? "On" : "Off";
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
