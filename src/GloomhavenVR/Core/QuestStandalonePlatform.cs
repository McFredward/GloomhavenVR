using System;
using System.IO;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Explicit bridge from the owned-game Android player to the existing mod. Unity owns
/// the OpenXR instance/session; the bridge only adopts it and requests its native
/// passthrough underlay. Desktop installs never enter this path.
/// </summary>
public static class QuestStandalonePlatform
{
    private static string? _resourceDirectory;
    private static Func<bool, bool>? _setPassthrough;
    private static Func<bool>? _isPassthroughActive;
    private static Func<bool>? _isSessionRunning;
    private static Func<long>? _passthroughSessionGeneration;
    private static long _lastPassthroughSessionGeneration;
    private static bool _havePassthroughSessionGeneration;
    private static bool? _lastPassthroughRequest;
    private static bool _passthroughFailureLogged;

    public static bool Enabled => Application.platform == RuntimePlatform.Android && _resourceDirectory != null;
    public static string ResourceDirectory => Enabled ? _resourceDirectory! : throw new InvalidOperationException("Quest standalone platform is not configured.");
    public static bool ModRunning => Enabled && VRSession.IsRunning;
    public static bool RigReady => ModRunning && VRRigDriver.HeadCamera != null && VRRigDriver.HeadCamera.isActiveAndEnabled;
    public static Camera? HeadCamera => RigReady ? VRRigDriver.HeadCamera : null;
    public static int PresentationLayer => VRLayers.ModLayer;
    public static bool DebugLogging => Enabled && VRLog.Wants(VRLogLevel.Debug);

    /// <summary>
    /// The native Android movie adapter may write only the mod's current menu
    /// capture. Query actual ownership, rather than coupling the builder to RT
    /// names, dimensions or future menu/stereo implementation details.
    /// </summary>
    public static bool IsFlatScreenVideoTarget(Camera camera) =>
        Enabled && WorldUI.FlatScreen.OwnsVideoCapture(camera);

    internal const string FlatScreenShaderName = "Hidden/GloomhavenVR/QuestWorldScreen";

    /// <summary>
    /// Unity's internal screen-space blit shader changes its source sampler to an
    /// XR texture array. Our captured world screens are ordinary Tex2D surfaces,
    /// including their existing left/right depth captures. Keep those inputs 2D
    /// and let the world vertex stage handle the headset's instanced eye output.
    /// </summary>
    internal static Shader? SelectFlatScreenShader(Shader? desktopShader)
    {
        if (!Enabled) return desktopShader;
        Shader shader = Resources.Load<Shader>("QuestWorldScreen");
        if (shader == null || shader.name != FlatScreenShaderName || !shader.isSupported)
            throw new InvalidOperationException("Validated Quest world screen shader is unavailable.");
        return shader;
    }

    internal static void SetFlatScreenMono(Material? material, Texture? texture)
    {
        if (!Enabled || material == null || material.shader.name != FlatScreenShaderName) return;
        material.SetTexture("_MainTex", texture);
        material.SetTexture("_RightTex", texture);
        material.SetFloat("_StereoCapture", 0f);
    }

    internal static void SetFlatScreenEyes(Material material, Texture? left, Texture? right)
    {
        if (!Enabled || material == null || material.shader.name != FlatScreenShaderName) return;
        material.SetTexture("_MainTex", left);
        material.SetTexture("_RightTex", right != null ? right : left);
        material.SetFloat("_StereoCapture", 1f);
    }

    /// <summary>Quest output adapters prepare a completed capture before the shared eye sampler.</summary>
    public static event Action? FlatScreenVideoSampling;
    public static event Action? FlatScreenVideoSampled;

    internal static void PrepareFlatScreenVideoSample()
    {
        if (Enabled) FlatScreenVideoSampling?.Invoke();
    }

    internal static void ObserveFlatScreenVideoSample()
    {
        if (Enabled) FlatScreenVideoSampled?.Invoke();
    }

    public static void PrepareFlatScreenVideoSample(Camera rendering)
    {
        if (Enabled && rendering != null && rendering == HeadCamera)
            PrepareFlatScreenVideoSample();
    }

    /// <summary>The actual display material currently consuming an owned capture.</summary>
    public static Material? FlatScreenVideoConsumer(Camera camera) =>
        Enabled ? WorldUI.FlatScreen.VideoConsumer(camera) : null;

    public static Camera? FlatScreenVideoFinalCamera(Camera camera) =>
        Enabled ? WorldUI.FlatScreen.FinalVideoCamera(camera) : null;

    public static Texture? FlatScreenVideoGlassCapture(Camera camera) =>
        Enabled ? WorldUI.FlatScreen.VideoGlassCapture(camera) : null;

    /// <summary>
    /// The player-owned preparation scene contains only a neutral camera anchor,
    /// not an original menu composite. Never capture that empty anchor into the
    /// mod's floating desktop surface. Original Bootstrap, Intro and menu scenes
    /// immediately resume the existing visibility policy; desktop is unaffected.
    /// </summary>
    public static bool SuppressStartupScreen(string activeSceneName) =>
        Enabled && string.Equals(activeSceneName, "QuestOriginalStartup", StringComparison.Ordinal);

    /// <summary>Called by the player before plugin creation, with its verified local resource root.</summary>
    public static void Configure(string pluginDirectory, Func<bool, bool> setPassthrough,
        Func<bool> isPassthroughActive, Func<bool> isSessionRunning,
        Func<long>? passthroughSessionGeneration = null)
    {
        if (Application.platform != RuntimePlatform.Android)
            throw new InvalidOperationException("Quest standalone configuration requires the Android player.");
        if (_resourceDirectory != null)
            throw new InvalidOperationException("Quest standalone platform is already configured.");
        if (string.IsNullOrWhiteSpace(pluginDirectory) || !Path.IsPathRooted(pluginDirectory) || !Directory.Exists(pluginDirectory))
            throw new ArgumentException("Quest standalone resource directory must be an existing absolute path.", nameof(pluginDirectory));
        _setPassthrough = setPassthrough ?? throw new ArgumentNullException(nameof(setPassthrough));
        _isPassthroughActive = isPassthroughActive ?? throw new ArgumentNullException(nameof(isPassthroughActive));
        _isSessionRunning = isSessionRunning ?? throw new ArgumentNullException(nameof(isSessionRunning));
        _passthroughSessionGeneration = passthroughSessionGeneration;
        _resourceDirectory = Path.GetFullPath(pluginDirectory);
    }

    internal static bool SessionRunning => Enabled && _isSessionRunning!();
    internal static bool PassthroughActive => Enabled && _isPassthroughActive!();

    internal static Color MixedRealityClearColor(Color desktopKey)
    {
        if (Enabled)
            return Color.clear;
        desktopKey.a = 1f;
        return desktopKey;
    }

    internal static bool SetPassthrough(bool wanted)
    {
        if (!Enabled)
            return false;
        try
        {
            if (_passthroughSessionGeneration != null)
            {
                long generation = _passthroughSessionGeneration();
                if (!_havePassthroughSessionGeneration || generation != _lastPassthroughSessionGeneration)
                {
                    // Native instance destruction resets its wanted flag. A new
                    // session may be created wholly between two mod frames, so
                    // observing Available=false is insufficient. The player bumps
                    // this generation after every native OnSessionCreate; retry
                    // once for that new session without per-frame native traffic.
                    _lastPassthroughSessionGeneration = generation;
                    _havePassthroughSessionGeneration = true;
                    _lastPassthroughRequest = null;
                    _passthroughFailureLogged = false;
                }
            }
            // Native SetEnabled returns the resulting ACTIVE flag, so successful
            // disable returns false. Compare observed state rather than treating
            // that flag as an operation-success result. Dedup requests to avoid
            // per-frame native/log traffic; the native feature owns session resume.
            if (_lastPassthroughRequest != wanted)
            {
                _setPassthrough!(wanted);
                _lastPassthroughRequest = wanted;
            }
            bool matched = _isPassthroughActive!() == wanted;
            if (!matched && !_passthroughFailureLogged)
            {
                _passthroughFailureLogged = true;
                VRLog.Warn("Core", "Quest native passthrough did not reach the requested state; ordinary VR remains available.");
            }
            else if (matched)
                _passthroughFailureLogged = false;
            return matched;
        }
        catch (Exception e)
        {
            if (!_passthroughFailureLogged)
            {
                _passthroughFailureLogged = true;
                VRLog.Error("Core", "Quest native passthrough bridge failed: " + e);
            }
            return false;
        }
    }
}
