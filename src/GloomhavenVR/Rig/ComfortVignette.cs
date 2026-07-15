using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// Optional comfort vignette (<c>[Comfort] VignetteEnabled</c>, default OFF — the
/// diorama is stationary, so most players never need it): a procedurally built annulus
/// mesh parented to the head camera whose vertex alpha fades from transparent (center)
/// to opaque black (periphery). World grab and snap/smooth turn feed motion intensity
/// via <see cref="NotifyMotion"/>; opacity eases in fast and out slow.
///
/// Implementation notes: mesh + material are built once per camera lifetime (zero
/// per-frame allocations); shader is the game-shipped <c>Sprites/Default</c>
/// (vertex-color, transparent) at render queue 4500 so it draws over the scene. The
/// quad sits 0.12 local units in front of the eye — beyond the P1 near plane
/// (0.05 × scale) at any diorama scale, since both scale with the rig.
/// </summary>
internal sealed class ComfortVignette : MonoBehaviour
{
    private const float LocalDistance = 0.12f;
    private const float InnerRadius = 0.10f;
    private const float OuterRadius = 0.60f;
    private const int Segments = 48;
    private const float FadeInPerSecond = 6f;
    private const float FadeOutPerSecond = 2.5f;
    private const float TargetDecayPerSecond = 5f;

    private static ComfortVignette? _instance;

    private GameObject? _ring;
    private MeshRenderer? _renderer;
    private Material? _material;
    private Camera? _boundCamera;
    private float _target;
    private float _current;

    /// <summary>Current opacity 0..1 (gizmos).</summary>
    internal float CurrentOpacity => _current;

    /// <summary>
    /// Report rig motion (any positive intensity, roughly 0..1 per frame). The vignette
    /// holds at the max reported level and decays once motion stops. No-op unless enabled.
    /// </summary>
    internal static void NotifyMotion(float intensity)
    {
        if (_instance != null && intensity > 0f)
            _instance._target = Mathf.Max(_instance._target, Mathf.Clamp01(intensity));
    }

    /// <summary>Full-strength pulse (snap turn).</summary>
    internal static void Pulse() => NotifyMotion(1f);

    private void Awake() => _instance = this;

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        TearDown();
    }

    private void Update()
    {
        Camera? head = VRRigDriver.HeadCamera;
        bool want = head != null && ComfortSettings.IsBound && ComfortSettings.VignetteEnabled.Value;

        if (!want)
        {
            if (_ring != null)
                TearDown();
            _target = _current = 0f;
            return;
        }

        if (_ring == null || _boundCamera != head)
            Build(head!);

        float strength = ComfortSettings.VignetteStrength.Value;
        float goal = _target * strength;
        _current = _current < goal
            ? Mathf.MoveTowards(_current, goal, FadeInPerSecond * Time.deltaTime)
            : Mathf.MoveTowards(_current, goal, FadeOutPerSecond * Time.deltaTime);
        _target = Mathf.MoveTowards(_target, 0f, TargetDecayPerSecond * Time.deltaTime);

        bool visible = _current > 0.01f;
        if (_renderer != null && _renderer.enabled != visible)
            _renderer.enabled = visible;
        if (visible && _material != null)
            _material.color = new Color(0f, 0f, 0f, _current);
    }

    private void Build(Camera head)
    {
        TearDown();
        _boundCamera = head;

        _ring = new GameObject("GloomhavenVR.ComfortVignette")
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = head.gameObject.layer
        };
        _ring.transform.SetParent(head.transform, worldPositionStays: false);
        _ring.transform.localPosition = new Vector3(0f, 0f, LocalDistance);
        _ring.transform.localRotation = Quaternion.identity;

        var filter = _ring.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildAnnulus();

        Shader? shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("UI/Default");
        if (shader == null)
        {
            VRLog.Warn("Comfort", "Vignette shader not found — vignette disabled.");
            TearDown();
            return;
        }

        _material = new Material(shader)
        {
            color = new Color(0f, 0f, 0f, 0f),
            renderQueue = 4500,
            hideFlags = HideFlags.HideAndDontSave
        };

        _renderer = _ring.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        _renderer.enabled = false;

        VRLog.Debug("Comfort", "Vignette ring built on the head camera.");
    }

    /// <summary>
    /// Flat ring facing -Z→camera: inner edge alpha 0, outer band alpha 1. Vertex colors
    /// carry the gradient; the material tint animates overall opacity.
    /// </summary>
    private static Mesh BuildAnnulus()
    {
        var mesh = new Mesh { name = "ComfortVignetteRing", hideFlags = HideFlags.HideAndDontSave };

        var vertices = new Vector3[Segments * 2];
        var colors = new Color32[Segments * 2];
        var triangles = new int[Segments * 6];

        for (int i = 0; i < Segments; i++)
        {
            float angle = i * (Mathf.PI * 2f / Segments);
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            vertices[i * 2] = new Vector3(cos * InnerRadius, sin * InnerRadius, 0f);
            vertices[i * 2 + 1] = new Vector3(cos * OuterRadius, sin * OuterRadius, 0f);
            colors[i * 2] = new Color32(0, 0, 0, 0);
            colors[i * 2 + 1] = new Color32(0, 0, 0, 255);

            int next = (i + 1) % Segments;
            int t = i * 6;
            // Wound to face the camera (looking down +Z onto the ring at +Z ahead).
            triangles[t] = i * 2;
            triangles[t + 1] = i * 2 + 1;
            triangles[t + 2] = next * 2 + 1;
            triangles[t + 3] = i * 2;
            triangles[t + 4] = next * 2 + 1;
            triangles[t + 5] = next * 2;
        }

        mesh.vertices = vertices;
        mesh.colors32 = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private void TearDown()
    {
        if (_ring != null)
        {
            var filter = _ring.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
                Destroy(filter.sharedMesh);
            Destroy(_ring);
        }
        if (_material != null)
            Destroy(_material);
        _ring = null;
        _renderer = null;
        _material = null;
        _boundCamera = null;
    }
}
