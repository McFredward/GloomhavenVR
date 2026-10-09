using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.NativeTypes;

namespace GloomhavenVR.Core;

/// <summary>
/// Queries the actual instance once per system and controls standalone Frame's
/// native alpha composition only when explicitly requested. No native functions
/// are replaced and no second instance, session or private camera API is used.
/// </summary>
[Serializable]
internal sealed class OpenXrEnvironmentBlendFeature : OpenXRFeature
{
    private readonly OpenXrEnvironmentBlendProbe _probe = new();
    private readonly Dictionary<ulong, OpenXrEnvironmentBlendProbe.QueryResult> _systemResults = new();
    private ulong _instance;
    private ulong _system;
    private ulong _session;
    private bool _sessionBegun;
    private int? _reportedActiveMode;
    private OpenXrEnvironmentBlendProbe.QueryResult? _capabilities;
    private bool _active;
    private bool _pending;
    private bool _activationFailed;
    private XrEnvironmentBlendMode? _previousMode;
    private double _requestStartedSeconds;
    internal const double ActivationTimeoutSeconds = 3;

    internal bool PassthroughActive => _active && _instance != 0 && _sessionBegun;
    internal FrameNativePassthroughStatus PassthroughStatus
    {
        get
        {
            if (_activationFailed) return FrameNativePassthroughStatus.ActivationFailed;
            if (_capabilities == null) return FrameNativePassthroughStatus.Checking;
            if (!_capabilities.IsComplete) return FrameNativePassthroughStatus.QueryFailed;
            if (!_capabilities.Modes.Contains(3)) return FrameNativePassthroughStatus.Unsupported;
            return _instance != 0 && _session != 0 && _sessionBegun
                ? FrameNativePassthroughStatus.Available : FrameNativePassthroughStatus.Checking;
        }
    }

    // These overrides are public because the mod compiles against a publicized
    // reference. The shipped1.10 callbacks are protected internal virtual.

    public override bool OnInstanceCreate(ulong xrInstance)
    {
        ExitPassthrough();
        _instance = xrInstance;
        _system = _session = 0;
        _sessionBegun = false;
        _capabilities = null;
        _activationFailed = false;
        _systemResults.Clear();
        _reportedActiveMode = null;
        _probe.Clear(); // A recreated instance may reuse the previous native handle value.
        try { _probe.BeginInstance(xrInstance, xrGetInstanceProcAddr); }
        catch (Exception) { _probe.BeginInstance(xrInstance, IntPtr.Zero); }
        FrameNativePassthrough.Attach(this);
        // Missing passthrough support must never veto otherwise functional VR.
        return true;
    }

    public override void OnSystemChange(ulong xrSystem)
    {
        if (_system != xrSystem)
        {
            ExitPassthrough();
            _system = xrSystem;
            _activationFailed = false;
            _capabilities = null;
        }
        OpenXrEnvironmentBlendProbe.QueryResult? result = _probe.ObserveSystem(xrSystem);
        if (result == null)
        {
            if (_capabilities == null && xrSystem != 0)
                _capabilities = _systemResults.TryGetValue(xrSystem, out var cached) ? cached :
                    OpenXrEnvironmentBlendProbe.QueryResult.Unavailable("system capability result unavailable");
            return;
        }
        _capabilities = result;
        if (_systemResults.Count < OpenXrEnvironmentBlendProbe.MaximumSystems)
            _systemResults[xrSystem] = result;
        if (!result.IsComplete)
        {
            VRLog.Info("Core", "OpenXR environment blend capabilities: runtime=" + RuntimeName() +
                "; view=PrimaryStereo; query=unavailable; reason=" + result.Reason +
                (result.NativeResult.HasValue ? "; nativeResult=" + result.NativeResult.Value : "") +
                "; passthrough support remains unknown; diagnostic only.");
            return;
        }
        VRLog.Info("Core", "OpenXR environment blend capabilities: runtime=" + RuntimeName() +
            "; view=PrimaryStereo; query=complete; supported=" +
            string.Join(",", result.Modes.Select(OpenXrEnvironmentBlendProbe.DescribeMode)) +
            "; alphaBlend=" + (result.Modes.Contains(3) ? "yes" : "no") +
            "; additive=" + (result.Modes.Contains(2) ? "yes" : "no") +
            "; diagnostic only (no passthrough enabled).");
    }

    public override void OnEnvironmentBlendModeChange(XrEnvironmentBlendMode xrEnvironmentBlendMode)
    {
        int mode = (int)xrEnvironmentBlendMode;
        if ((_active && mode != 3) || (_pending && mode != 3 &&
            _previousMode.HasValue && xrEnvironmentBlendMode != _previousMode.Value))
        {
            // An external change wins. Never overwrite another mode owner while
            // the camera is switching back to its ordinary VR presentation.
            ForgetOwnership();
            FailActivation("active mode changed outside the passthrough request");
        }
        if (_instance == 0 || _reportedActiveMode == mode)
            return;
        _reportedActiveMode = mode;
        VRLog.Info("Core", "OpenXR environment blend mode: runtime=" + RuntimeName() +
            "; active=" + OpenXrEnvironmentBlendProbe.DescribeMode(mode) +
            "; native passthrough active=" + (_active ? "yes" : "no") + ".");
    }

    public override void OnSessionCreate(ulong xrSession)
    {
        ExitPassthrough();
        _session = _instance != 0 ? xrSession : 0;
        _sessionBegun = false;
        _activationFailed = false;
    }

    public override void OnSessionBegin(ulong xrSession)
    {
        if (_instance == 0 || _session == 0 || _session != xrSession) return;
        if (!_sessionBegun) _activationFailed = false;
        _sessionBegun = true;
    }

    public override void OnSessionEnd(ulong xrSession) => EndSession(xrSession, false, true);
    public override void OnSessionExiting(ulong xrSession) => EndSession(xrSession, false, true);
    public override void OnSessionDestroy(ulong xrSession) => EndSession(xrSession, true, false);
    public override void OnSessionLossPending(ulong xrSession) => EndSession(xrSession, true, false);

    private void EndSession(ulong session, bool discardSession, bool canRestore)
    {
        if (_session == 0 || _session != session) return;
        if (canRestore) ExitPassthrough();
        else ForgetOwnership(); // Lost/destroyed native handles may not be called.
        _sessionBegun = false;
        if (discardSession) _session = 0;
    }

    internal bool TryEnterPassthrough(double nowSeconds)
    {
        if (!FrameNativePassthrough.Required ||
            PassthroughStatus != FrameNativePassthroughStatus.Available) return false;
        if (_active) return true;
        try
        {
            XrEnvironmentBlendMode actual = GetEnvironmentBlendMode();
            if (_pending)
            {
                if (actual == XrEnvironmentBlendMode.AlphaBlend)
                {
                    _pending = false;
                    _active = true;
                    VRLog.Info("Core", "Steam Frame native passthrough: alpha composition accepted; runtime=" + RuntimeName() + ".");
                    return true;
                }
                if (_previousMode.HasValue && actual != _previousMode.Value)
                {
                    ForgetOwnership();
                    FailActivation("active mode changed outside the pending passthrough request");
                    return false;
                }
                if (nowSeconds - _requestStartedSeconds >= ActivationTimeoutSeconds)
                {
                    ExitPassthrough(); // Cancel the queued request even if actual mode stayed opaque.
                    FailActivation("alpha composition was not accepted within the activation deadline");
                }
                return false;
            }
            if ((int)actual <= 0) throw new InvalidOperationException("invalid active environment blend mode");
            _previousMode = actual;
            if (actual == XrEnvironmentBlendMode.AlphaBlend)
            {
                _active = true;
                return true; // No write or ownership claim if already selected externally.
            }
            _requestStartedSeconds = nowSeconds;
            _pending = true; // Before Set: a synchronous callback must see the pending request.
            SetEnvironmentBlendMode(XrEnvironmentBlendMode.AlphaBlend);
            return false; // The native submission path accepts or rejects this later.
        }
        catch (Exception e)
        {
            ExitPassthrough();
            FailActivation("native blend control exception " + e.GetType().Name);
            return false;
        }
    }

    internal void ExitPassthrough()
    {
        XrEnvironmentBlendMode? previous = _previousMode;
        bool wasPending = _pending;
        bool restore = (_pending || _active) && previous.HasValue &&
            previous.Value != XrEnvironmentBlendMode.AlphaBlend && _instance != 0 && _sessionBegun;
        ForgetOwnership(); // Clear before Set: its callback must not count restoration as drift.
        if (!restore) return;
        try
        {
            // A missing notification must not let us undo an external mode change.
            // Pending requests still need cancellation even while readback is opaque.
            XrEnvironmentBlendMode actual = GetEnvironmentBlendMode();
            if (actual != XrEnvironmentBlendMode.AlphaBlend)
            {
                if (!wasPending) return;
                if (actual != previous!.Value)
                {
                    FailActivation("active mode changed outside the pending passthrough request");
                    return;
                }
            }
            SetEnvironmentBlendMode(previous!.Value);
            VRLog.Info("Core", "Steam Frame native passthrough: previous composition requested; mode=" +
                OpenXrEnvironmentBlendProbe.DescribeMode((int)previous.Value) + ".");
        }
        catch (Exception e)
        {
            FailActivation("native blend restoration exception " + e.GetType().Name);
        }
    }

    private void ForgetOwnership()
    {
        _active = _pending = false;
        _previousMode = null;
    }

    private void FailActivation(string reason)
    {
        if (_activationFailed) return;
        _activationFailed = true;
        VRLog.Warn("Core", "Steam Frame native passthrough unavailable: " + reason +
            "; update Proton and SteamVR, then restart the game; ordinary VR remains available.");
    }

    public override void OnInstanceDestroy(ulong xrInstance) => EndInstance(xrInstance);
    public override void OnInstanceLossPending(ulong xrInstance) => EndInstance(xrInstance);

    private void EndInstance(ulong xrInstance)
    {
        _probe.EndInstance(xrInstance);
        if (_instance != xrInstance)
            return;
        ForgetOwnership();
        FrameNativePassthrough.Detach(this);
        _instance = 0;
        _system = _session = 0;
        _sessionBegun = false;
        _capabilities = null;
        _systemResults.Clear();
        _activationFailed = false;
        _reportedActiveMode = null;
    }

    private static string RuntimeName()
    {
        try
        {
            string name = OpenXRRuntime.name;
            return string.IsNullOrEmpty(name) ? "unknown" : name;
        }
        catch (Exception) { return "unknown"; }
    }
}
