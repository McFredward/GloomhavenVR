using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Index-finger ray for far interaction (FROZEN Phase-2 API). Implements
/// <see cref="IPickProvider"/> — Phase-3a consumes the pick for board targeting.
///
/// Ray pose: origin at the index knuckle, direction = hand forward (+Z, along the
/// fingers) — stable regardless of finger curl. Visual: a subtle LineRenderer laser
/// plus a reticle dot at the hit point, shown only while the interactor is enabled
/// (far-interaction modes / RayAlwaysOn config).
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
            return;
        }

        float scale = _hand.WorldScale;
        Vector3 origin = _hand.Rig.GetFinger(Finger.Index).Root.position;
        Vector3 direction = _hand.Rig.Root.forward;
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

        UpdateVisuals(origin, direction, maxDistance, scale);
    }

    // ---- visuals -----------------------------------------------------------------------

    private void UpdateVisuals(Vector3 origin, Vector3 direction, float maxDistance, float scale)
    {
        if (_laser == null)
            CreateVisuals();

        Vector3 end = _current.HasHit ? _current.HitPoint : origin + direction * (maxDistance * 0.25f);

        _laser!.widthMultiplier = 0.0018f * scale;
        _laser.SetPosition(0, origin + direction * (0.03f * scale));
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
