using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Process-wide VR session state shared by all modules (set by <see cref="CoreModule"/>,
/// read by everyone, including Harmony patches deciding whether to divert game code).
/// </summary>
internal static class VRSession
{
    /// <summary>
    /// True once OpenXR is initialized and an <c>XRDisplaySubsystem</c> is running —
    /// i.e. the HMD is rendering. Harmony patches must behave 100% vanilla while false.
    /// </summary>
    internal static bool IsRunning { get; set; }

    /// <summary>Shared Harmony instance (created in <see cref="Plugin.Awake"/>).</summary>
    internal static Harmony? Harmony { get; set; }

    /// <summary>Name of the active OpenXR runtime (after successful init), for logs/UI.</summary>
    internal static string? RuntimeName { get; set; }

    /// <summary>
    /// MonoBehaviour that owns mod coroutines (the <see cref="Plugin"/> instance; set in
    /// <c>Plugin.Awake</c>, cleared in <c>Plugin.OnDestroy</c> so hot reload kills them).
    /// Used by <see cref="OpenXRBootstrap"/> for the display-running watchdog.
    /// </summary>
    internal static MonoBehaviour? CoroutineHost { get; set; }
}
