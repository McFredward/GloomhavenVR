using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Makes a floated modal window (<see cref="ModalFallback"/>) a GRABBABLE + SCALABLE
/// world element — exactly like the control board / combat log — by reusing the SHARED
/// grab core (<see cref="PanelGrabHandle"/> + <see cref="IPanelGrabOwner"/>): one hand
/// grips the brass bar under the panel to MOVE it, two hands RESIZE it (0.5x-2x). No new
/// grab mechanism is invented; this only owns a small mod-owned holder/frame the same way
/// <see cref="Surfaces.CombatLogSurface"/> does, and lets the game-owned world-space host
/// FOLLOW that frame each tick.
///
/// TRANSFORM LAYOUT (mirrors CombatLogSurface): holder (identity pose, localScale =
/// diorama WorldScale) → frame (grab ROOT at the PANEL CENTER; localScale = user size
/// factor 0.5-2) → bar visual (a child just under the panel's bottom edge). The
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
    /// Item 3: the brass grab bar must OCCLUDE the menu content behind it (foreground is
    /// foreground). The floated menu host is a WORLD-space canvas at
    /// <c>ModalFallback.ModalHostSortingOrder = 1000</c>, and Unity sorts EVERY renderer by
    /// sortingLayer → SORTINGORDER first (only then by renderQueue / distance — see
    /// <c>RayInteractor.RayVisualSortingOrder = 5000</c>, which is exactly how the laser dot
    /// draws over the same order-1000 modal). Giving the bar's MeshRenderer a sortingOrder
    /// ABOVE the menu makes it composite unambiguously ON TOP of the canvas; an opaque
    /// (ZWrite-on) brass material then makes it read solid rather than semi-transparent. Kept
    /// below the ray visuals (5000) so the pointer dot still lands on top of the bar.
    /// </summary>
    private const int BarSortingOrder = 1100;

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

    private Transform? _holder;                 // identity pose, localScale = diorama WorldScale
    private Transform? _frame;                  // grab root at the panel centre; localScale = user factor
    private Transform? _bar;
    private BoxCollider? _grabZone;
    private PanelGrabHandle? _handle;

    /// <summary>True while a hand grips the bar (owner skips no writes — the host just follows).</summary>
    internal bool IsGrabbed => _handle != null && _handle.IsGrabbed;

    /// <summary>
    /// Build the grab affordance for a freshly floated, freshly placed modal host. The
    /// frame is seeded at the host's CURRENT world pose (the host was just placed at the
    /// HMD), so the first follow tick keeps the panel exactly where it spawned — no jump.
    /// </summary>
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

    bool IPanelGrabOwner.GrabVisible =>
        _panel != null && _panel.IsAlive && _holder != null && _holder.gameObject.activeInHierarchy;

    // Carry the yaw with the hand like the tray/combat log — nothing else authors the
    // modal's rotation, so there is no two-writer jitter.
    bool IPanelGrabOwner.GrabCarriesYaw => true;

    // Free placement: the menu stays wherever the user left it while open; a re-open
    // re-floats it at the HMD (ModalFallback), so there is nothing to persist here.
    void IPanelGrabOwner.OnGrabFinished() { }

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

        // Item 4: the user grab factor rides the SAME [MinScale, MaxScale] range the shared handle
        // clamps to — a higher local floor here would silently re-cap what the two-hand pinch shrank.
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;

        Transform host = _panel.HostGo.transform;
        host.SetPositionAndRotation(_frame.position, _frame.rotation);
        host.localScale = Vector3.one * (metersPerPixel * worldScale * _extraScale * factor);

        // Bar/zone track the live host rect. The holder is now identity, so these frame-local
        // metres must carry worldScale themselves to reach the host's world size (the frame's
        // own localScale contributes the user grab factor). unit = world metres per host pixel.
        Rect rect = _panel.HostRect.rect;
        float unit = metersPerPixel * _extraScale * worldScale;
        float halfHeight = rect.height * unit * 0.5f;
        float width = rect.width * unit;
        SyncBar(halfHeight, width, worldScale);
    }

    // ---- build ------------------------------------------------------------------------------

    private void EnsureFrame()
    {
        if (_holder != null && _frame != null)
            return;

        var holderGo = new GameObject($"GloomhavenVR.ModalGrab_{_logName}");
        _holder = holderGo.transform;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Bar";
        Object.Destroy(bar.GetComponent<Collider>());
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
        mr.sortingOrder = BarSortingOrder; // composite ON TOP of the order-1000 menu host canvas
        _bar = bar.transform;

        // Grab zone + shared grab core (collider BEFORE the handle: its OnEnable registers it).
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.isTrigger = true;
        _grabZone.size = new Vector3(0.25f, 0.05f, 0.05f);
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        _handle.Init(this, mr, "WorldUI", $"{_logName} menu");

        // Render-only mod layer — grabs/pokes route through the registries, not layers.
        VRLayers.Apply(holderGo);
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
        float gap = BarGapMeters * worldScale;
        float thickness = BarThickness * worldScale;
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
