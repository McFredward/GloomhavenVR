using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Displays a full side-by-side movie from the decoder's existing texture. The native
/// VideoPlayer, camera, audio and continuation lease remain owned by their original caller.
/// Only explicitly identified SBS sources may use this material; ordinary movies keep
/// their native UI presentation. Preflight before selecting SBS so failures retain 2D media.
/// </summary>
internal sealed class StereoVideoSurface : IDisposable
{
    internal const string BundleResource = "GloomhavenVR.StereoVideo.ui-sbs.bundle";
    private const string ShaderPath = "assets/stereovideo/stereovideoui.shader";
    private static readonly int EyeId = Shader.PropertyToID("_GvrStereoVideoEye");
    private static AssetBundle? _bundle;
    private static Shader? _shader;
    private static string? _loadFailure;
    private static int _liveSurfaces;
    private static readonly Stack<int> PreviousEyes = new(4);
    private static int _currentEye;

    private readonly RawImage _image;
    private readonly Material? _originalMaterial;
    private readonly Rect _originalUv;
    private readonly Material _material;
    private bool _disposed;

    private StereoVideoSurface(RawImage image, Material material)
    {
        _image = image;
        _originalMaterial = image.material;
        _originalUv = image.uvRect;
        _material = material;
        image.uvRect = new Rect(0f, 0f, 1f, 1f);
        image.material = material;
        if (_liveSurfaces++ == 0)
        {
            PreviousEyes.Clear();
            ApplyEye(0);
            Camera.onPreRender += BeforeCamera;
            Camera.onPostRender += AfterCamera;
        }
    }

    /// <summary>Available only for the production MultiPass path and a usable embedded shader.</summary>
    internal static bool IsAvailable(out string reason)
    {
        StereoModeConfig.Mode mode = StereoModeConfig.Current;
        if ((mode != StereoModeConfig.Mode.MultiPass
            || XRSettings.stereoRenderingMode != XRSettings.StereoRenderingMode.MultiPass)
            && !QuestStandalonePlatform.SharedStereoEyeRouting)
        {
            reason = "Stereo movie surfaces require MultiPass rendering.";
            return false;
        }
        if (_shader != null && _shader.isSupported)
        {
            reason = string.Empty;
            return true;
        }
        if (_loadFailure != null)
        {
            reason = _loadFailure;
            return false;
        }
        try
        {
            if (QuestStandalonePlatform.Enabled)
            {
                _shader = QuestMovieAssets.Shader("GloomhavenVR/StereoVideoUI");
                reason = string.Empty;
                return true;
            }
            using Stream? resource = typeof(StereoVideoSurface).Assembly.GetManifestResourceStream(BundleResource);
            if (resource == null) throw new InvalidDataException("Embedded stereo movie shader bundle is missing.");
            using var bytes = new MemoryStream();
            resource.CopyTo(bytes);
            _bundle = AssetBundle.LoadFromMemory(bytes.ToArray());
            if (_bundle == null) throw new InvalidDataException("Stereo movie shader bundle could not be loaded.");
            _shader = _bundle.LoadAsset<Shader>(ShaderPath);
            if (_shader == null || !_shader.isSupported)
                throw new InvalidDataException("Stereo movie UI shader is unavailable on this graphics API.");
            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            _shader = null;
            if (_bundle != null) _bundle.Unload(true);
            _bundle = null;
            _loadFailure = ex.Message;
            reason = _loadFailure;
            return false;
        }
    }

    internal static bool TryAttach(RawImage image, out StereoVideoSurface? surface, out string reason)
    {
        surface = null;
        if (image == null)
        {
            reason = "Stereo movie image is missing.";
            return false;
        }
        if (!IsAvailable(out reason)) return false;
        var material = new Material(_shader!) { name = "GloomhavenVR.StereoVideo", hideFlags = HideFlags.HideAndDontSave };
        surface = new StereoVideoSurface(image, material);
        return true;
    }

    /// <summary>Full SBS carries two complete eye pictures, not one double-width movie.</summary>
    internal static float PerEyeAspect(int packedWidth, int height) =>
        packedWidth > 0 && height > 0 ? packedWidth / (2f * height) : 1f;

    // Unity supplies the actual active eye in MultiPass. Never derive the eye from callback
    // count: auxiliary/menu cameras can render between the two headset passes. Mono cameras
    // and unknown observations deliberately use the left picture, including desktop mirrors.
    internal static int EyeIndex(Camera.MonoOrStereoscopicEye eye) =>
        eye == Camera.MonoOrStereoscopicEye.Right ? 1 : 0;

    private static void BeforeCamera(Camera camera)
    {
        PreviousEyes.Push(_currentEye);
        ApplyEye(EyeIndex(camera.stereoActiveEye));
    }

    // A later onPreRender listener can synchronously render a mono auxiliary camera
    // inside the head's right pass. Its own pre-render sets LEFT; returning without
    // restoring the enclosing eye would then draw the head's right picture from LEFT.
    // Paired camera callbacks preserve nesting as well as ordinary eye interleaving.
    private static void AfterCamera(Camera camera) =>
        ApplyEye(PreviousEyes.Count > 0 ? PreviousEyes.Pop() : 0);

    private static void ApplyEye(int eye)
    {
        _currentEye = eye;
        Shader.SetGlobalFloat(EyeId, eye);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_image != null)
        {
            _image.material = _originalMaterial;
            _image.uvRect = _originalUv;
        }
        UnityEngine.Object.Destroy(_material);
        if (--_liveSurfaces == 0)
        {
            Camera.onPreRender -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            PreviousEyes.Clear();
            ApplyEye(0);
        }
    }
}
