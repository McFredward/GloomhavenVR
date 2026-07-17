using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Combat log as a grabbable, yaw-billboarded world panel (test #19; moved out of
/// the fixed arc-slot layout in TablePanelSurfaces).
/// Verified target: <c>[RequireComponent(typeof(UIWindow))] public class
/// CombatLogHandler : Singleton&lt;CombatLogHandler&gt;, IPointerEnterHandler, ...</c>.
/// The ScrollRect keeps working — poke/laser drags synthesize real pointer events
/// on the host raycaster.
///
/// ORIENTATION FIX (test #19: "text reads skewed/oblique"): the panel used to take
/// the arc-slot rotation from <see cref="PanelLayout.TryGetPose"/> — azimuth 56°
/// around the table anchor plus a 10° pitch. That rotation points the panel's
/// normal back along the arc RADIAL toward the TABLE CENTER, not toward the
/// player's head, so from the seat the panel (farthest out on the arc of all
/// slots) was viewed ~20–25° off its axis and pitched — reading as slanted/sheared
/// text. No actual shear existed (the host scale is uniform); the POSE was wrong.
/// Now the panel is a YAW-ONLY billboard: upright on world up (zero roll/pitch),
/// re-facing the head every tick, position fully decoupled from rotation.
///
/// GRAB (test #19: movable/scalable/pinnable EXACTLY like the control board): a
/// brass grab bar under the panel's bottom edge drives the tray's shared
/// <see cref="PanelGrabHandle"/> core — one hand moves, two hands resize
/// (0.5×–2×), and the final release persists the layout as [WorldUI] CombatLog*
/// (table-anchor offsets in real meters + the size factor), so it survives
/// sessions and diorama scale. Yaw carry is OFF: the billboard owns the rotation
/// (two writers would fight), the grab drives position + scale only.
///
/// FOLLOW/PINNED: a <see cref="PlayTray.BoardButton"/> next to the bar (same
/// visual/behavior as the tray's pin toggle) flips [WorldUI] CombatLogFollow.
/// FOLLOW re-derives the pose from the persisted offsets every tick (moves with
/// recenters/diorama like every panel); PINNED freezes the world pose. Like the
/// tray, a pinned WORLD pose does not survive sessions — on each conversion the
/// panel first places from the persisted offsets, then freezes. Poke always works
/// on the pin (PokeableBehaviour self-registration); the laser reaches it through
/// the tray's LaserTargets list while a tray exists and is visible (CardsDriver
/// ray-tests that list — without a tray the pin is poke-only).
///
/// TRANSFORM LAYOUT: holder (identity pose, localScale = diorama WorldScale)
/// → frame (grab root at the BAR CENTER; localScale = user size factor 0.5–2)
/// → bar/pin visuals. The frame's lossyScale is therefore WorldScale × factor —
/// exactly the scale the converted host is placed with, so bar and panel resize
/// and diorama-scale together, and the grab core's 0.5–2 localScale clamp keeps
/// its meaning (the tray's _pinRoot trick). The game-owned host is pose-followed,
/// never re-parented (mount-seam reversibility rule).
/// </summary>
internal sealed class CombatLogSurface : WorldSurface, IPanelGrabOwner
{
    /// <summary>Panel bottom edge sits this far above the bar center (the tray's handle gap).</summary>
    private const float BarGapMeters = 0.03f;
    private const float BarThickness = 0.024f;
    /// <summary>Bar/zone width relative to the panel width (the tray uses 0.55/0.62 of its board).</summary>
    private const float BarWidthFraction = 0.55f;
    private const float ZoneWidthFraction = 0.62f;

    public override string Name => "CombatLog";
    protected override bool ConfigEnabled => WorldUIConfig.CombatLog.Value;

    private Transform? _holder;   // identity pose, carries the diorama scale
    private Transform? _frame;    // grab root at the bar center; localScale = user factor
    private Transform? _bar;
    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;
    private PlayTray.BoardButton? _pin;
    private Transform? _pinAnchor;
    private PlayTray? _laserTray;
    private float _builtBarWidth = -1f;
    private bool _placedFromConfig;

    protected override RectTransform? FindTarget() =>
        Singleton<CombatLogHandler>.IsInitialized
            ? Singleton<CombatLogHandler>.Instance.transform as RectTransform
            : null;

    // ---- IPanelGrabOwner -------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;
    bool IPanelGrabOwner.GrabVisible =>
        Panel != null && _holder != null && _holder.gameObject.activeInHierarchy;
    bool IPanelGrabOwner.GrabCarriesYaw => false; // rotation belongs to the billboard (Place)
    void IPanelGrabOwner.OnGrabFinished() => PersistLayout();

    // ---- lifecycle -------------------------------------------------------------------------

    public override void Tick()
    {
        base.Tick();
        // Gate closed / scene unloaded: the frame hides with the panel (it must not
        // float alone in the world), and the next conversion re-derives the pose
        // from the persisted offsets (pinned WORLD poses do not survive — tray rule).
        if (Panel == null)
        {
            _placedFromConfig = false;
            if (_holder != null && _holder.gameObject.activeSelf)
                _holder.gameObject.SetActive(false);
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_holder != null)
            Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _bar = null;
        _grabZone = null;
        _handle = null;
        _pin = null;
        _pinAnchor = null;
        _laserTray = null;
        _builtBarWidth = -1f;
        _placedFromConfig = false;
    }

    // ---- placement (every tick while converted) ----------------------------------------------

    protected override void Place()
    {
        if (Panel == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        EnsureFrame();
        if (_holder == null || _frame == null)
            return;

        float worldScale = PanelLayout.WorldScale;
        _holder.localScale = Vector3.one * worldScale;
        if (!_holder.gameObject.activeSelf)
            _holder.gameObject.SetActive(true);

        bool grabbed = _handle != null && _handle.IsGrabbed;
        if (!grabbed && (WorldUIConfig.CombatLogFollow.Value || !_placedFromConfig))
        {
            // FOLLOW: re-derive from the persisted offsets every tick (world-anchored
            // like every panel — the seat yaw is cached in PanelLayout). PINNED:
            // derive ONCE per conversion, then the world pose stays frozen.
            Vector3 offset = new(
                WorldUIConfig.CombatLogRight.Value,
                WorldUIConfig.CombatLogUp.Value,
                WorldUIConfig.CombatLogForward.Value);
            _frame.position = anchor + yaw * (offset * worldScale);
            _frame.localScale = Vector3.one
                                * Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f);
            _placedFromConfig = true;
        }

        // Test #19 orientation fix: yaw-only billboard — upright, facing the head.
        // uGUI fronts render along -forward, so +Z points AWAY from the viewer.
        Vector3 away = _frame.position - head.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude > 1e-6f)
            _frame.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return;

        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        float hostScale = worldScale * _frame.localScale.x;

        SyncBarWidth(rect.width * metersPerPixel);

        // Panel grows UP from the bar (bottom-center convention, like the tray mounts).
        Vector3 center = _frame.position + _frame.rotation *
            (Vector3.up * ((BarGapMeters + rect.height * metersPerPixel * 0.5f) * hostScale));
        CanvasConversion.PlaceHost(Panel, center, _frame.rotation, hostScale);

        TickPin();
    }

    // ---- frame (grab bar + pin) ---------------------------------------------------------------

    /// <summary>
    /// Build the mod-owned frame lazily; scene loads destroy the holder (top-level,
    /// deliberately NOT DontDestroyOnLoad — the combat log only lives inside a
    /// scenario), so a Unity-dead holder rebuilds everything from scratch.
    /// </summary>
    private void EnsureFrame()
    {
        if (_holder != null && _frame != null)
            return;

        _frame = null;
        _bar = null;
        _grabZone = null;
        _handle = null;
        _pin = null;
        _pinAnchor = null;
        _laserTray = null;
        _builtBarWidth = -1f;

        var holderGo = new GameObject("GloomhavenVR.CombatLogPanel");
        _holder = holderGo.transform;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        Object.Destroy(bar.GetComponent<Collider>());
        bar.transform.SetParent(_frame, worldPositionStays: false);
        bar.transform.localScale = new Vector3(0.30f, BarThickness, BarThickness);
        bar.GetComponent<MeshRenderer>().sharedMaterial =
            WorldUIAssets.CreateFlatMaterial(new Color(0.62f, 0.5f, 0.28f)); // brass — same "grab me" as the tray
        _bar = bar.transform;

        // Grab zone + shared grab core (collider BEFORE the handle: OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.size = new Vector3(0.35f, 0.05f, 0.05f);
        _grabZone.isTrigger = true;
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        _handle.Init(this, bar.GetComponent<MeshRenderer>(), "WorldUI", "Combat log");

        // FOLLOW/PINNED pin, right of the bar (the tray's toggle, same look & feel).
        _pinAnchor = new GameObject("PinToggle").transform;
        _pinAnchor.SetParent(_frame, worldPositionStays: false);
        _pinAnchor.localPosition = new Vector3(0.22f, 0f, -0.002f);
        _pin = PlayTray.BoardButton.Create(_pinAnchor, new Vector2(0.068f, 0.030f),
            new Color(0.75f, 0.55f, 0.2f), "FOLLOW", TogglePin);
        ApplyPinVisual();

        // Render-only mod layer — grabs and pokes go through the registries.
        VRLayers.Apply(holderGo);
        VRLog.Info("WorldUI", "Combat log frame built (grab bar + FOLLOW/PINNED pin; " +
                              "yaw-billboard placement).");
    }

    /// <summary>Bar/zone/pin track the CONTENT-FIT panel width (change-gated; re-fits are rare).</summary>
    private void SyncBarWidth(float panelWidthMeters)
    {
        if (_bar == null || _grabZone == null)
            return;
        float barWidth = panelWidthMeters * BarWidthFraction;
        if (Mathf.Abs(barWidth - _builtBarWidth) < 0.005f)
            return;
        _builtBarWidth = barWidth;
        _bar.localScale = new Vector3(barWidth, BarThickness, BarThickness);
        _grabZone.size = new Vector3(panelWidthMeters * ZoneWidthFraction, 0.05f, 0.05f);
        if (_pinAnchor != null)
            _pinAnchor.localPosition = new Vector3(barWidth * 0.5f + 0.05f, 0f, -0.002f);
    }

    // ---- FOLLOW/PINNED ------------------------------------------------------------------------

    private void TogglePin()
    {
        bool follow = !WorldUIConfig.CombatLogFollow.Value;
        WorldUIConfig.CombatLogFollow.Value = follow; // BepInEx persists on set
        if (!follow)
            PersistLayout(); // freeze: next scenario re-derives the pin from these offsets
        ApplyPinVisual();
        VRLog.Info("WorldUI", "Combat log anchor mode → " +
                              $"{(follow ? "FOLLOW (seat-anchored)" : "PINNED (world-anchored)")}.");
    }

    private void ApplyPinVisual()
    {
        if (_pin == null)
            return;
        bool follow = WorldUIConfig.CombatLogFollow.Value;
        _pin.SetState(true, accent: !follow);
        _pin.SetLabel(follow ? "FOLLOW" : "PINNED");
    }

    /// <summary>
    /// Laser support rides the tray's LaserTargets list (CardsDriver ray-tests it
    /// while the tray is visible); the list dies with each tray, so re-register per
    /// tray INSTANCE. Poke needs none of this (PokeableBehaviour self-registers).
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

    // ---- persistence --------------------------------------------------------------------------

    /// <summary>
    /// Inverse of the FOLLOW placement: the frame's pose as table-anchor offsets in
    /// real meters (seat-yaw space, divided by the diorama scale) + the size factor.
    /// BepInEx writes the ConfigFile on set, so the layout survives sessions.
    /// </summary>
    private void PersistLayout()
    {
        if (_frame == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return;
        float worldScale = PanelLayout.WorldScale;
        if (worldScale < 1e-5f)
            return;
        Vector3 local = Quaternion.Inverse(yaw) * (_frame.position - anchor) / worldScale;
        WorldUIConfig.CombatLogRight.Value = local.x;
        WorldUIConfig.CombatLogUp.Value = local.y;
        WorldUIConfig.CombatLogForward.Value = local.z;
        WorldUIConfig.CombatLogScale.Value = Mathf.Clamp(_frame.localScale.x, 0.5f, 2f);
        VRLog.Info("WorldUI", $"Combat log layout persisted: right {local.x:F2} m, " +
                              $"up {local.y:F2} m, fwd {local.z:F2} m, " +
                              $"scale {WorldUIConfig.CombatLogScale.Value:F2}x.");
    }
}
