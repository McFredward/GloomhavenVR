using System;
using System.IO;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// The original scenario sky's render assets for scenes which have no sky of their
/// own. Build740's user request extends Default to the menu and map as well. The
/// source prefab is only Transform/MeshFilter/MeshRenderer; no native controller,
/// callback, light, scene load or gameplay object is needed to display it.
/// </summary>
internal static class NativeDefaultSky
{
    internal const string Resource = "GloomhavenVR.DefaultSky.bundle";
    private const string MeshAsset = "native/default-sky/mesh";
    private const string MaterialAsset = "native/default-sky/material";
    // Exact level8 GH_SkySphere_PR -> GH_SkySphere authored transforms. Preserve
    // their offset/flattened shape/yaw; centering the mesh bounds changes horizon.
    private const float AuthoredRootScale = 1.0629240274429321f;
    private static readonly Vector3 AuthoredOffset = new(0f, 14.749992370605469f, 0f);
    private static readonly Vector3 AuthoredScale = new(.15803000330924988f, .06696362793445587f, .15803000330924988f);
    private static readonly Quaternion AuthoredRotation = new(0f, .22705163061618805f, 0f, .9738827347755432f);
    private static AssetBundle? _bank;
    private static Mesh? _mesh;
    private static Material? _sourceMaterial;
    private static Material? _material;
    private static GameObject? _go;
    private static MeshRenderer? _renderer;
    private static Transform? _rig;
    private static Camera? _head;
    private static bool _failed;

    internal static Renderer? Renderer => _renderer;
    internal static bool Owns(Renderer? renderer) => renderer != null && ReferenceEquals(renderer, _renderer);

    /// <summary>The real rig/camera lifetime, rather than locomotion or zoom, owns
    /// the presentation. A menu-to-map rebuild must not retain the old sky lease.</summary>
    internal static bool RefreshLifetime()
    {
        if (ReferenceEquals(_rig, null)) return false;
        if (ReferenceEquals(_rig, VRRigDriver.RigRoot)
            && ReferenceEquals(_head, VRRigDriver.HeadCamera)) return false;
        Release();
        return true;
    }

    internal static Renderer? Acquire()
    {
        Camera? head = VRRigDriver.HeadCamera;
        Transform? rig = VRRigDriver.RigRoot;
        if (!VRSession.IsRunning || head == null || rig == null) return null;
        if (_renderer != null) return _renderer;
        if (!EnsureAssets()) return null;
        _rig = rig;
        _head = head;
        _go = new GameObject("GloomhavenVR.NativeDefaultSky", typeof(MeshFilter), typeof(MeshRenderer));
        _go.SetActive(false);
        _go.hideFlags = HideFlags.HideAndDontSave;
        VRLayers.Apply(_go);
        UnityEngine.Object.DontDestroyOnLoad(_go);
        _go.GetComponent<MeshFilter>().sharedMesh = _mesh;
        _material = new Material(_sourceMaterial!)
        { name = "GloomhavenVR.NativeDefaultSky.Material", hideFlags = HideFlags.HideAndDontSave };
        _renderer = _go.GetComponent<MeshRenderer>();
        _renderer.sharedMaterial = _material;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.lightProbeUsage = LightProbeUsage.Off;
        _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        UpdatePose();
        _go.SetActive(true);
        VRLog.Info("Core", "Default sky: original scenario mesh/material/animated shader available without a scenario scene; head-only render assets.");
        return _renderer;
    }

    /// <summary>
    /// This is an infinite sky, unlike the world-fixed room. Project the exact
    /// authored shape from its prefab's observer origin around the current eye.
    /// Uniform scaling changes no angular UV/texture animation and fits the
    /// complete dome inside the existing far plane, so camera policy is untouched.
    /// The native renderer remains authoritative whenever one exists.
    /// </summary>
    internal static void UpdatePose()
    {
        if (_go == null || _mesh == null || _head == null) return;
        Vector3 authoredScale = AuthoredScale * AuthoredRootScale;
        Vector3 authoredOffset = AuthoredOffset * AuthoredRootScale;
        Bounds bounds = _mesh.bounds;
        float extent = Vector3.Scale(bounds.extents, authoredScale).magnitude
            + Vector3.Scale(bounds.center, authoredScale).magnitude + authoredOffset.magnitude;
        float factor = _head.farClipPlane * .85f / Mathf.Max(extent, .001f);
        Vector3 position = _head.transform.position + authoredOffset * factor;
        Vector3 scale = authoredScale * factor;
        Transform transform = _go.transform;
        if (transform.position != position || transform.rotation != AuthoredRotation)
            transform.SetPositionAndRotation(position, AuthoredRotation);
        if (transform.localScale != scale) transform.localScale = scale;
    }

    /// <summary>Remove only this render-only presentation. Immutable source bank
    /// assets may be reused on the next explicit style/rig transition.</summary>
    internal static void Release()
    {
        if (_go != null)
        {
            _go.SetActive(false);
            UnityEngine.Object.Destroy(_go);
        }
        if (_material != null) UnityEngine.Object.Destroy(_material);
        _go = null;
        _renderer = null;
        _material = null;
        _rig = null;
        _head = null;
    }

    internal static void Reset()
    {
        Release();
        _mesh = null;
        _sourceMaterial = null;
        if (_bank != null) _bank.Unload(true);
        _bank = null;
        _failed = false;
    }

    private static bool EnsureAssets()
    {
        if (_mesh != null && _sourceMaterial != null) return true;
        if (_failed) return false;
        try
        {
            // isSupported can succeed through a different builtin fallback.
            // This bank contains the audited original D3D11 programs; silently
            // showing VertexLit/Diffuse on another API changes the chosen sky.
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                throw new PlatformNotSupportedException("original AMP_SkyShader bank contains Direct3D11 programs only");
            using Stream? resource = typeof(NativeDefaultSky).Assembly.GetManifestResourceStream(Resource);
            if (resource == null) throw new FileNotFoundException("missing native default sky bank", Resource);
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            _bank = AssetBundle.LoadFromMemory(bytes.ToArray());
            if (_bank == null) throw new InvalidDataException("native default sky bank could not load");
            _mesh = _bank.LoadAsset<Mesh>(MeshAsset);
            _sourceMaterial = _bank.LoadAsset<Material>(MaterialAsset);
            if (_mesh == null || _sourceMaterial == null || _sourceMaterial.shader == null
                || _sourceMaterial.shader.name != "AMP_SkyShader")
                throw new InvalidDataException("incomplete native default sky render assets");
            if (!_sourceMaterial.shader.isSupported)
                throw new NotSupportedException("the original native default sky shader is unsupported on this graphics API");
            return true;
        }
        catch (Exception error)
        {
            Reset();
            _failed = true;
            VRLog.Warn("Core", "Default sky unavailable without a native scene on " + SystemInfo.graphicsDeviceType
                + " (" + error.GetType().Name + ": " + error.Message + ").");
            return false;
        }
    }
}
