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
