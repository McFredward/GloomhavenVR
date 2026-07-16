using System.Collections.Generic;
using GloomhavenVR.Core.Events;
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
/// laser plus a reticle dot at the hit point, shown only while the interactor is
/// enabled (far-interaction modes / RayAlwaysOn config).
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

    /// <summary>
    /// P5 (MISSION A.1): world position that overrides the VISIBLE reticle/laser end
    /// while the ray has a hit — Board sets this to the hovered hex center
    /// ([Board] SnapToHexCenter) so the reticle snaps like the game cursor does.
    /// Consumers set it per frame; it is cleared automatically when the ray misses,
    /// is disabled, or nobody re-sets it (one-frame latch). Pick data is unaffected.
    /// </summary>
    public Vector3? ReticleOverride
    {
        get => _reticleOverride;
        set
        {
            _reticleOverride = value;
            _reticleOverrideFrame = Time.frameCount;
        }
    }

    private Vector3? _reticleOverride;
    private int _reticleOverrideFrame = -1;

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

    /// <summary>Enable/disable (mode policy). Hides the laser when disabled.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            UpdateVisualActive();
        }
    }

    public bool TryGetPick(out PickPose pick)
    {
        pick = _current;
        return _enabled && _hand.HasPose;
    }

    internal void Tick()
    {
        if (!_enabled || !_hand.HasPose)
        {
            _current.HasHit = false;
            _reticleOverride = null;
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

        if (Physics.Raycast(origin, direction, out RaycastHit hit, maxDistance, Mask))
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

        // One-frame latch: consumers (Board) re-set the override every frame they want it.
        if (_reticleOverride.HasValue && Time.frameCount > _reticleOverrideFrame + 1)
            _reticleOverride = null;

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

    // ---- visuals -----------------------------------------------------------------------

    private void UpdateVisuals(Vector3 origin, Vector3 direction, float maxDistance, float scale)
    {
        if (_laser == null)
            CreateVisuals();

        // ModalUI constraint: keep the pick alive but hide the beam unless it points
        // at a UI surface (MISSION A.5).
        bool show = VisualsAllowed(origin, direction);
        if (_laser!.gameObject.activeSelf != show)
            _laser.gameObject.SetActive(show);
        if (!show)
        {
            if (_reticle!.gameObject.activeSelf)
                _reticle.gameObject.SetActive(false);
            return;
        }

        // Reticle snap (MISSION A.1): Board substitutes the hovered hex center.
        Vector3 end = _current.HasHit
            ? (_reticleOverride ?? _current.HitPoint)
            : origin + direction * (maxDistance * 0.25f);

        // Visual origin at the index fingertip (test #6, requirement 3): the PICK ray
        // keeps the OpenXR aim pose (origin/direction above), but the visible beam
        // starts at the hand rig's index tip and converges on the aim ray's end point,
        // so it reads as leaving the pointing finger instead of the controller/palm
        // center. [Hands] LaserFingerOffsetMeters nudges the start along the beam.
        Vector3 start = origin + direction * (0.03f * scale);
        if (Plugin.LaserFingerOrigin.Value)
        {
            Transform tip = _hand.Rig.IndexTip;
            if (tip != null)
            {
                Vector3 toEnd = end - tip.position;
                if (toEnd.sqrMagnitude > 1e-8f)
                    start = tip.position + toEnd.normalized * (Plugin.LaserFingerOffsetMeters.Value * scale);
            }
        }

        _laser.widthMultiplier = 0.0018f * scale;
        _laser.SetPosition(0, start);
        _laser.SetPosition(1, end);

        if (_current.HasHit)
        {
            if (!_reticle!.gameObject.activeSelf)
                _reticle.gameObject.SetActive(true);
            _reticle.position = end;
            _reticle.localScale = Vector3.one * (0.008f * scale);
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

        GameObject reticleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticleGo.name = $"GloomhavenVR.Reticle_{_hand.Side}";
        Object.Destroy(reticleGo.GetComponent<Collider>());
        reticleGo.transform.SetParent(_hand.transform, worldPositionStays: true);
        reticleGo.GetComponent<Renderer>().sharedMaterial = _laser.material;
        _reticle = reticleGo.transform;
        _reticle.gameObject.SetActive(false);

        // Lazily created AFTER HandsDriver's tree-wide VRLayers.Apply — layer them here.
        Core.VRLayers.Apply(laserGo);
        Core.VRLayers.Apply(reticleGo);

        UpdateVisualActive();
    }

    private Material CreateBeamMaterial(out Color color)
    {
        color = new Color(0.45f, 0.8f, 1f, 0.35f);
        Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        var material = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
        material.color = color;
        return material;
    }

    private void UpdateVisualActive()
    {
        if (_laser != null)
            _laser.gameObject.SetActive(_enabled);
        if (_reticle != null && !_enabled)
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
