using UnityEngine;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;

namespace GloomhavenVR.Rig;

/// <summary>
/// Resolves the transform the comfort features manipulate:
///
/// - In VR: the Phase-1 rig root (<see cref="VRRigDriver.RigRoot"/>). All world grab /
///   snap turn / recenter motion is applied to the RIG (inverse manipulation) —
///   game objects are never moved.
/// - Desktop dev harness ([Dev] SimulateHands, no HMD): a hidden proxy transform so
///   the full grab/turn math runs and is observable via [Comfort] DebugGizmos, even
///   though nothing renders through it (the sim hands are camera-parented).
/// </summary>
internal static class RigTarget
{
    /// <summary>Matches HandsDriver.SimScale so dev math sees a plausible diorama scale.</summary>
    private const float DevProxyScale = 12f;

    private static GameObject? _devProxy;

    /// <summary>True while <see cref="Current"/> is the dev proxy rather than the real rig.</summary>
    internal static bool IsDevProxy { get; private set; }

    /// <summary>Base (unmultiplied) diorama scale of the current target.</summary>
    internal static float BaseScale =>
        VRRigDriver.RigRoot != null && VRRigDriver.BaseWorldScale > 0f
            ? VRRigDriver.BaseWorldScale
            : DevProxyScale;

    /// <summary>The manipulable rig transform, or null while neither rig nor dev sim exist.</summary>
    internal static Transform? Current
    {
        get
        {
            Transform? rig = VRRigDriver.RigRoot;
            if (rig != null)
            {
                IsDevProxy = false;
                DestroyProxy();
                return rig;
            }

            bool devSim = !VRSession.IsRunning && Plugin.DevMode.Value
                          && Plugin.SimulateHands.Value && VRHands.Ready;
            if (!devSim)
            {
                IsDevProxy = false;
                DestroyProxy();
                return null;
            }

            if (_devProxy == null)
            {
                _devProxy = new GameObject("GloomhavenVR.DevRigProxy")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                Object.DontDestroyOnLoad(_devProxy);
                _devProxy.transform.localScale = Vector3.one * DevProxyScale;
                VRLog.Debug("Comfort", "Dev rig proxy created (world grab math runs against it).");
            }
            IsDevProxy = true;
            return _devProxy.transform;
        }
    }

    /// <summary>Module shutdown / sim off.</summary>
    internal static void DestroyProxy()
    {
        if (_devProxy == null)
            return;
        Object.Destroy(_devProxy);
        _devProxy = null;
    }
}
