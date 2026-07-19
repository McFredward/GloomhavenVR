using System.Collections.Generic;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Index-finger ray for far interaction (FROZEN Phase-2 API). Implements
/// <see cref="IPickProvider"/> — Phase-3a consumes the pick for board targeting.
///
/// Ray pose (P1, hardware test #4): the OpenXR AIM ("pointer") pose when the device
/// delivers it (<see cref="VRHand.HasPointerPose"/>) — the runtime's authored
/// "where this controller points", unaffected by grip-pose tilt or the visual hand
/// offset. Fallback (simulated hands / no aim pose): origin at the index knuckle,
/// direction = hand forward (+Z, along the fingers). Visual: a subtle LineRenderer
/// laser plus a reticle dot, shown while the effective state (<see cref="Active"/>)
/// is on — for the DOMINANT hand that is EVERY mode while the hand has a pose and
/// holds nothing (test #19). Test #14: the visible beam is a
/// STRAIGHT segment of the aim ray — hits clamp its length, the reticle sits at
/// ray ∩ surface on that line, and nothing may re-aim it (see UpdateVisuals).
///
/// Physics only — uGUI far pointing goes through the virtual mouse bridge instead
/// (GloomhavenVR.WorldUI.VirtualMouse), matching UI-ARCH §4.4 strategy 1.
/// </summary>
internal sealed class RayInteractor : IPickProvider
{
    /// <summary>Max ray length in meters (scale 1) — spans the whole diorama when scaled.</summary>
    private const float MaxDistanceMeters = 20f;

    private readonly VRHand _hand;

    private LineRenderer? _laser;
    private Transform? _reticle;
    private bool _enabled = true;
    private PickPose _current;

    /// <summary>Layers the pick ray tests. Phase-3a sets the game's selection mask here.</summary>
    public LayerMask Mask = Physics.DefaultRaycastLayers;

    // Test #14 item 2: the former ReticleOverride (Board hex-snap moved the visible
    // dot to the hex center) is GONE — any override that moves the end point off the
    // aim line visibly re-aims the beam ('zaps' onto elements). The snapped hex is
    // communicated by the game's own hex hover highlight (HoverRegisterer/star
    // display via the projected cursor, BoardPick.TryGetCursorWorld), never by
    // bending the beam or dot.

    /// <summary>
    /// World point where the ray hits a code-intersected UI surface (the WorldUI flat
    /// screen has no physics collider — FlatScreen sets this every tick with its
    /// plane-intersection point, latched while a press is frozen). While fresh, the
    /// visible beam's LENGTH is clamped to this point's distance along the aim ray
    /// and the reticle shows at that ray point — the beam never passes THROUGH a
    /// menu (hardware test #7) and never changes direction (test #14: only the
    /// projection onto the aim line is used, so a latched press point slightly off
    /// the current aim cannot bend the beam). One-frame latch; pick data unaffected.
    /// </summary>
    public Vector3? UiHitOverride
    {
        get => _uiHitOverride;
        set
        {
            _uiHitOverride = value;
            _uiHitOverrideFrame = Time.frameCount;
        }
    }

    private Vector3? _uiHitOverride;
    private int _uiHitOverrideFrame = -1;

    /// <summary>
    /// True while <see cref="UiHitOverride"/> is fresh (set this frame or the last) —
    /// i.e. the beam is clamped to a code-intersected UI surface (world panel, fan
    /// card, flat screen). Far-click consumers (BoardClickDriver) skip the trigger
    /// while this is set so a UI point-and-click never doubles as a board click.
    /// </summary>
    public bool HasFreshUiHit => _uiHitOverride.HasValue && Time.frameCount - _uiHitOverrideFrame <= 1;

    // Constant ANGULAR size for the ray visuals (P6, hardware test #8): the reticle
    // used to scale with the rig's WorldScale — zooming the diorama out grew the dot
    // enormously (and doubly so: localScale under an already rig-scaled parent).
    // Angular sizing keeps it a fixed apparent size from the HMD regardless of rig
    // scale or distance. tan(0.45°) ≈ 0.00785, tan(0.06°) ≈ 0.00105.
    private const float ReticleAngularFactor = 0.00785f;
    private const float BeamWidthAngularFactor = 0.00105f;
    private const float ReticleMinMeters = 0.003f;
    private const float ReticleMaxMeters = 0.25f;
    private const float BeamWidthMinMeters = 0.0008f;
    private const float BeamWidthMaxMeters = 0.03f;

    // Test (user #1): the hit dot vanished ON the floated dialog/modal window. The
    // renderQueue-4600 material (see CreateBeamMaterial) only beats canvases at the
    // SAME sorting order — but a floated modal host is raised to sortingOrder=1000
    // (WorldUI.ModalFallback.ModalHostSortingOrder), and Unity sorts every renderer
    // by sortingLayer → SORTINGORDER first and only then by renderQueue. At the
    // reticle's default order 0 the modal painted straight over the dot. The laser
    // LineRenderer and reticle MeshRenderer get an order comfortably above the modal
    // so they draw last; the shader still ZTest-LEquals against the opaque depth
    // buffer, so solid furniture/board geometry keeps occluding them correctly (UI
    // shaders write no depth, so the depthless modal never does).
    private const int RayVisualSortingOrder = 5000;

    // ---- P5 (MISSION A.5): ModalUI visual constraint --------------------------------------
    // In ModalUI the ray stays ACTIVE (flat-screen pointer, dialogs) but its visuals only
    // show while it points near a known UI surface, so the laser doesn't sweep the room
    // while a dialog is up. UI surfaces = every registered UguiPokeSurfaces canvas plus
    // explicitly registered extra targets (the WorldUI flat screen registers its quad).

    private static readonly List<Transform> UiTargets = new(4);

    /// <summary>Register a world transform the ModalUI-constrained ray may point at (e.g. the flat screen quad).</summary>
    public static void RegisterUiTarget(Transform target)
    {
        if (target != null && !UiTargets.Contains(target))
            UiTargets.Add(target);
    }

    public static void UnregisterUiTarget(Transform target) => UiTargets.Remove(target);

    /// <summary>Hot-reload hygiene (HandsModule.Shutdown).</summary>
    internal static void ClearUiTargets() => UiTargets.Clear();

    internal RayInteractor(VRHand hand) => _hand = hand;

    /// <summary>Latest pick (updated once per frame while enabled).</summary>
    public PickPose Current => _current;

    /// <summary>
    /// Mode-policy input (<see cref="VRHand.SetInteractorMask"/>). ONE input into the
    /// effective state — <see cref="Active"/> re-derives on/off from live facts every
    /// frame and <see cref="Tick"/> syncs the visuals (test #19: never edge-latched).
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// LASER PERSISTENCE TRUTH TABLE (hardware test #19: the dominant laser silently
    /// vanished mid-scenario and never returned). Every path that can turn this ray
    /// or its visuals off — each must be a LEVEL (re-read from live state every
    /// frame), never an edge-latched flag, so a missed release/mode event can never
    /// strand the laser off:
    ///
    ///   input (re-read per Tick)      | turns off      | can it latch?
    ///   ------------------------------+----------------+----------------------------------
    ///   mode mask (Enabled=false)     | pick + visuals | WAS THE #19 LATCH: TableIdle and
    ///                                 |                | HalfSelection carried no Ray, and
    ///                                 |                | a single-target attack waits in
    ///                                 |                | Choreographer state
    ///                                 |                | WaitingForCardSelection — NOT a
    ///                                 |                | TargetingStates member — so the
    ///                                 |                | mode stayed HalfSelection and the
    ///                                 |                | laser was policy-off for the whole
    ///                                 |                | attack. Fixed: InteractorsFor ORs
    ///                                 |                | Ray into the DOMINANT role in
    ///                                 |                | every mode.
    ///   !VRHand.HasPose               | pick + visuals | no — device tracking level; the
    ///                                 |                | visuals return the frame the pose
    ///                                 |                | returns.
    ///   Grabber.Held != null          | pick + visuals | no — Held is itself level-derived
    ///                                 |                | (release re-checks the live button
    ///                                 |                | state every Tick; CancelAll on
    ///                                 |                | mode disable and tracking loss).
    ///   ModalUI cone (VisualsAllowed) | visuals only   | no — recomputed per frame; leaves
    ///                                 |                | with the mode.
    ///   UiHitOverride                 | nothing        | no — clamps beam LENGTH only,
    ///                                 |                | one-frame freshness window.
    ///   dominance switch              | via mode mask  | no — HandsDriver reapplies masks
    ///                                 |                | on PrimaryHand.SettingChanged and
    ///                                 |                | on every hands rebuild.
    ///   rig/hands rebuild             | visuals die    | no — Build → ApplyMode recreates
    ///                                 |                | hand, ray and visuals together.
    /// </summary>
    public bool Active => _enabled && _hand.HasPose && !IsHolding;

    /// <summary>Transient suppression: the hand is actually holding a grabbable RIGHT NOW.</summary>
    private bool IsHolding => _hand.Grabber != null && _hand.Grabber.Held != null;

    public bool TryGetPick(out PickPose pick)
    {
        pick = _current;
        return Active;
    }

    internal void Tick()
    {
        bool active = Active;
        SyncActiveState(active);
        if (!active)
        {
            _current.HasHit = false;
            return;
        }

        float scale = _hand.WorldScale;
        Vector3 origin;
        Vector3 direction;
        if (_hand.HasPointerPose)
        {
            // OpenXR aim pose — see class doc.
            origin = _hand.PointerOrigin;
            direction = _hand.PointerDirection;
        }
        else
        {
            origin = _hand.Rig.GetFinger(Finger.Index).Root.position;
            direction = _hand.Rig.Root.forward;
        }
        float maxDistance = MaxDistanceMeters * scale;

        _current.Origin = origin;
        _current.Direction = direction;

        // Modal input-block (menu open): while a modal window floats
        // (ModalFallback.WindowModalActive) or we are in ModalUI, the ray PICK must not hit
        // non-modal targets. Board hexes, cards and tray buttons all live on physics
        // colliders; the modal window and every registered modal surface (the WorldUI flat
        // screen quad) are collider-less uGUI reached through the virtual-mouse path — so
        // suppressing the physics pick leaves ONLY the menu clickable. Visuals (the ModalUI
        // cone) are unaffected. Recomputed + logged once per frame, shared by both hands.
        UpdateModalPickBlock();
        if (!_modalPickBlocked && Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, Mask))
        {
            _current.HasHit = true;
            _current.HitPoint = hit.point;
            _current.HitDistance = hit.distance;
            _current.HitCollider = hit.collider;
        }
        else
        {
            _current.HasHit = false;
            _current.HitCollider = null;
        }

        UpdateVisuals(origin, direction, maxDistance, scale);
    }

    /// <summary>
    /// ModalUI visual gate (MISSION A.5): true when the ray visuals should show.
    /// Outside ModalUI (or with [Hands] RayAlwaysOn / a zero cone) always true;
    /// in ModalUI only while pointing within [Hands] ModalRayConeDegrees of a
    /// registered UI surface (poke canvases + extra targets like the flat screen).
    /// </summary>
    private bool VisualsAllowed(Vector3 origin, Vector3 direction)
    {
        if (VRModeStateMachine.CurrentMode != VRMode.ModalUI || Plugin.RayAlwaysOn.Value)
            return true;
        // Hardware test #13: the ray IS on a UI surface right now (RayUguiDriver /
        // fan / flat screen clamped the beam) — visuals must always show. The cone
        // below measures the angle to the canvas CENTER only; on a floated story
        // window (1920 px × 0.7 scale ≈ 1.3 m wide at 1.2 m) the outer half sat
        // outside the 25° cone, so the dot vanished while clicks kept landing.
        if (HasFreshUiHit)
            return true;
        float cone = Plugin.ModalRayConeDegrees.Value;
        if (cone <= 0f)
            return true;

        var canvases = UguiPokeSurfaces.Surfaces;
        for (int i = 0; i < canvases.Count; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !canvas.isActiveAndEnabled)
                continue;
            if (WithinCone(origin, direction, canvas.transform.position, cone))
                return true;
        }
        for (int i = UiTargets.Count - 1; i >= 0; i--)
        {
            Transform target = UiTargets[i];
            if (target == null)
            {
                UiTargets.RemoveAt(i);
                continue;
            }
            if (target.gameObject.activeInHierarchy && WithinCone(origin, direction, target.position, cone))
                return true;
        }
        return false;
    }

    private static bool WithinCone(Vector3 origin, Vector3 direction, Vector3 target, float coneDegrees)
    {
        Vector3 to = target - origin;
        return to.sqrMagnitude > 1e-8f && Vector3.Angle(direction, to) <= coneDegrees;
    }

    // ---- modal input-block: gate the physics pick to the menu only ---------------------

    /// <summary>
    /// True while a modal menu is open — the ray physics pick is suppressed so nothing
    /// behind the menu (board hexes / cards / tray buttons) is pickable. Static because
    /// it is a global mode fact shared by both hands; recomputed once per frame in
    /// <see cref="UpdateModalPickBlock"/>. The modal window itself stays clickable through
    /// its own uGUI (virtual-mouse) path, which this never touches.
    /// </summary>
    private static bool _modalPickBlocked;
    private static int _modalPickFrame = -1;

    /// <summary>Recompute the modal pick-block once per frame (shared by both hands) and log each transition.</summary>
    private static void UpdateModalPickBlock()
    {
        if (Time.frameCount == _modalPickFrame)
            return;
        _modalPickFrame = Time.frameCount;
        // Item 4 (user): a NON-blocking reachable menu (pause/ESC, Options, Multiplayer,
        // Compendium) must NOT gate board/card/tray picks — the player keeps interacting while
        // it floats. Only BLOCKING floated windows (story/results/durability), which also assert
        // ModalUI, gate the pick. So key on BlockingWindowModalActive, not WindowModalActive.
        bool blocked = WorldUI.ModalFallback.BlockingWindowModalActive
                       || VRModeStateMachine.CurrentMode == VRMode.ModalUI;
        if (blocked == _modalPickBlocked)
            return;
        _modalPickBlocked = blocked;
        Core.VRLog.Info("Hands", blocked
            ? "Modal input-block ENGAGED — ray physics pick gated to the modal menu; non-modal board/card/tray targets ignored."
            : "Modal input-block RELEASED — ray physics pick restored to all targets.");
    }

    // ---- visuals -----------------------------------------------------------------------

    private void UpdateVisuals(Vector3 origin, Vector3 direction, float maxDistance, float scale)
    {
        if (_laser == null)
            CreateVisuals();

        // ModalUI constraint: keep the pick alive but hide the beam unless it points
        // at a UI surface (MISSION A.5). Change-deduped log (test #19): every visual
        // flip must be attributable from the log.
        bool show = VisualsAllowed(origin, direction);
        if (_laser!.gameObject.activeSelf != show)
        {
            _laser.gameObject.SetActive(show);
            Core.VRLog.Debug("Hands", $"{_hand.Side} ray visuals {(show ? "shown" : "hidden")} — " +
                                      "ModalUI UI-surface cone gate.");
        }
        if (!show)
        {
            if (_reticle!.gameObject.activeSelf)
                _reticle.gameObject.SetActive(false);
            return;
        }

        // Test #14 item 2 — the beam is ALWAYS the straight aim ray. Both endpoints
        // lie on (origin, direction); hits clamp the LENGTH only, so the controller
        // alone controls the beam angle. The old geometry started at the knuckle and
        // converged on the hit POINT — when the hit jumped onto a canvas plane
        // (UiHitOverride) or a snapped hex (ReticleOverride, now removed) the beam
        // visibly changed angle ('zapped' onto elements).
        //
        // Length priority: UI-surface hit (code-intersected canvas/flat screen —
        // never pass THROUGH a menu, test #7) → physics hit → open-ended segment.
        // The UI point is projected onto the aim line: RayUguiDriver points are on
        // it by construction; FlatScreen's latched press point may drift off it, and
        // only its along-ray distance may influence the visuals.
        bool uiHit = _uiHitOverride.HasValue && Time.frameCount - _uiHitOverrideFrame <= 1;
        float length = uiHit
            ? Mathf.Max(0.02f * scale, Vector3.Dot(_uiHitOverride!.Value - origin, direction))
            : _current.HasHit
                ? _current.HitDistance
                : maxDistance * 0.25f;
        Vector3 end = origin + direction * length;

        // Visual origin (test #7 + #14): the beam still reads as leaving the pointing
        // finger, but the knuckle anchor is PROJECTED ONTO THE AIM LINE — the start
        // point sits at the knuckle's along-ray distance plus the configured offset,
        // never off-axis, so the beam direction is exactly the aim direction at all
        // times (the tip curls with the trigger pull; the knuckle is curl-independent).
        Vector3 start = origin + direction * (0.03f * scale);
        if (Plugin.LaserFingerOrigin.Value)
        {
            Transform anchor = _hand.Rig.IndexKnuckle ?? _hand.Rig.IndexTip;
            if (anchor != null)
            {
                float along = Mathf.Max(0f, Vector3.Dot(anchor.position - origin, direction))
                              + Plugin.LaserFingerOffsetMeters.Value * scale;
                start = origin + direction * Mathf.Min(along, length * 0.9f);
            }
        }

        // Head-to-end distance drives BOTH the beam width and the reticle size —
        // constant angular size, independent of rig scale (see const block above).
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        float headDist = head != null ? Vector3.Distance(head.transform.position, end) : 1f;

        _laser.widthMultiplier = Mathf.Clamp(headDist * BeamWidthAngularFactor, BeamWidthMinMeters, BeamWidthMaxMeters);
        _laser.SetPosition(0, start);
        _laser.SetPosition(1, end);

        if (_current.HasHit || uiHit)
        {
            if (!_reticle!.gameObject.activeSelf)
                _reticle.gameObject.SetActive(true);
            _reticle.position = end;
            // localScale sits under the rig-scaled hand — divide the world-space
            // target size by the parent's lossy scale.
            float worldSize = Mathf.Clamp(headDist * ReticleAngularFactor, ReticleMinMeters, ReticleMaxMeters);
            float parentScale = Mathf.Max(1e-4f, _hand.transform.lossyScale.x);
            _reticle.localScale = Vector3.one * (worldSize / parentScale);
        }
        else if (_reticle!.gameObject.activeSelf)
        {
            _reticle.gameObject.SetActive(false);
        }
    }

    private void CreateVisuals()
    {
        var laserGo = new GameObject($"GloomhavenVR.Laser_{_hand.Side}");
        laserGo.transform.SetParent(_hand.transform, worldPositionStays: false);
        _laser = laserGo.AddComponent<LineRenderer>();
        _laser.useWorldSpace = true;
        _laser.positionCount = 2;
        _laser.material = CreateBeamMaterial(out Color color);
        _laser.startColor = color;
        _laser.endColor = new Color(color.r, color.g, color.b, 0.05f);
        _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _laser.receiveShadows = false;
        // Draw after the floated modal (sortingOrder 1000) so the beam stays visible
        // on dialogs; depth test still occludes it behind solid geometry.
        _laser.sortingOrder = RayVisualSortingOrder;

        GameObject reticleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticleGo.name = $"GloomhavenVR.Reticle_{_hand.Side}";
        Object.Destroy(reticleGo.GetComponent<Collider>());
        reticleGo.transform.SetParent(_hand.transform, worldPositionStays: true);
        Renderer reticleRenderer = reticleGo.GetComponent<Renderer>();
        reticleRenderer.sharedMaterial = _laser.material;
        // Same as the laser: draw the hit dot above the modal host (sortingOrder 1000)
        // so it never vanishes on a dialog; ZTest LEqual still hides it behind solids.
        reticleRenderer.sortingOrder = RayVisualSortingOrder;
        _reticle = reticleGo.transform;
        _reticle.gameObject.SetActive(false);

        // Lazily created AFTER HandsDriver's tree-wide VRLayers.Apply — layer them here.
        // Only reached from UpdateVisuals, i.e. while Active — the new laser GO's
        // default-active state is already correct; SyncActiveState keeps it so.
        Core.VRLayers.Apply(laserGo);
        Core.VRLayers.Apply(reticleGo);
    }

    private Material CreateBeamMaterial(out Color color)
    {
        color = new Color(0.45f, 0.8f, 1f, 0.35f);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        var material = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
        material.color = color;
        // Test #14 item 3: reticle/beam partially vanished ON dialogs — world-space
        // canvas graphics (UI/Default, TMP) draw in the transparent queue (~3000)
        // and within one queue transparents sort by depth, so canvas geometry at the
        // same plane could draw OVER the dot/beam. Render queue 4600 draws after
        // every canvas graphic unconditionally; real scene occlusion is preserved
        // because the shader still depth-TESTS (ZTest LEqual) against the opaque
        // scene's depth buffer while UI shaders write no depth at all. This is the
        // robust variant vs. a camera-facing plane offset, which would need per-
        // surface tuning and can still lose to TMP sub-mesh sorting.
        material.renderQueue = 4600;
        return material;
    }

    // ---- effective-state sync ----------------------------------------------------------

    private bool _wasActive;
    private string _lastReason = "";

    /// <summary>
    /// Applies the level-derived state to the visuals and emits ONE Debug line per
    /// state/reason change naming the cause (test #19: a future silent disappearance
    /// must be attributable from the log). Change-deduped — nothing per-frame.
    /// </summary>
    private void SyncActiveState(bool active)
    {
        string reason = active ? "active"
            : !_enabled ? $"mode policy — no Ray in the {VRModeStateMachine.CurrentMode} mask"
            : !_hand.HasPose ? "no pose (tracking lost)"
            : "hand is holding a grabbable (level-derived, releases with it)";
        if (active == _wasActive && reason == _lastReason)
            return;
        _wasActive = active;
        _lastReason = reason;
        Core.VRLog.Debug("Hands", $"{_hand.Side} ray {(active ? "ON" : "OFF")} — {reason}.");

        if (_laser != null && _laser.gameObject.activeSelf != active)
            _laser.gameObject.SetActive(active);
        if (_reticle != null && !active)
            _reticle.gameObject.SetActive(false);
    }

    internal void DestroyVisuals()
    {
        if (_laser != null)
        {
            Object.Destroy(_laser.gameObject);
            _laser = null;
        }
        if (_reticle != null)
        {
            Object.Destroy(_reticle.gameObject);
            _reticle = null;
        }
    }
}
