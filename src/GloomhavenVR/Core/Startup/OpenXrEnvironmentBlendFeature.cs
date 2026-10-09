using System;
using System.Linq;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.NativeTypes;

namespace GloomhavenVR.Core;

/// <summary>
/// Startup-only capability diagnostic. It reuses Unity's actual OpenXR instance;
/// no native functions are replaced, no second instance/session is created and
/// no environment blend, camera, render layer or frame submission is changed.
/// </summary>
[Serializable]
internal sealed class OpenXrEnvironmentBlendFeature : OpenXRFeature
{
    private readonly OpenXrEnvironmentBlendProbe _probe = new();
    private ulong _instance;
    private int? _reportedActiveMode;

    // These overrides are public because the mod compiles against a publicized
    // reference. The shipped1.10 callbacks are protected internal virtual.

    public override bool OnInstanceCreate(ulong xrInstance)
    {
        _instance = xrInstance;
        _reportedActiveMode = null;
        _probe.Clear(); // A recreated instance may reuse the previous native handle value.
        try { _probe.BeginInstance(xrInstance, xrGetInstanceProcAddr); }
        catch (Exception) { _probe.BeginInstance(xrInstance, IntPtr.Zero); }
        // A diagnostic must never veto an otherwise functional VR startup.
        return true;
    }

    public override void OnSystemChange(ulong xrSystem)
    {
        OpenXrEnvironmentBlendProbe.QueryResult? result = _probe.ObserveSystem(xrSystem);
        if (result == null)
            return;
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
        if (_instance == 0 || _reportedActiveMode == mode)
            return;
        _reportedActiveMode = mode;
        VRLog.Info("Core", "OpenXR environment blend mode: runtime=" + RuntimeName() +
            "; active=" + OpenXrEnvironmentBlendProbe.DescribeMode(mode) + "; diagnostic only.");
    }

    public override void OnInstanceDestroy(ulong xrInstance) => EndInstance(xrInstance);
    public override void OnInstanceLossPending(ulong xrInstance) => EndInstance(xrInstance);

    private void EndInstance(ulong xrInstance)
    {
        _probe.EndInstance(xrInstance);
        if (_instance != xrInstance)
            return;
        _instance = 0;
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
