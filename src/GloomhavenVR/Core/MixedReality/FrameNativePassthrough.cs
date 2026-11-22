using System;
using System.Diagnostics;
using GloomhavenVR.Core.SteamLink;

namespace GloomhavenVR.Core;

internal enum FrameNativePassthroughStatus
{
    Checking,
    Unsupported,
    QueryFailed,
    ActivationFailed,
    Available
}

/// <summary>
/// Standalone Frame uses the launch marker. For PC streaming, Valve advertises
/// alpha blend on the live SteamVR OpenXR system. Its native route is independent
/// of the legacy companion and of the optional OpenVR identity reader.
/// </summary>
internal static class FrameNativePassthrough
{
    private static OpenXrEnvironmentBlendFeature? _feature;
    private static bool? _standalone;

    // File.Exists is appropriate at startup, never for the many MR/backing/UI
    // predicates evaluated each frame. Refresh only for a genuinely new instance;
    // changing the installed opt-in marker takes effect on the next VR startup.
    internal static bool Standalone => _standalone ??= FrameDefaults.Active;
    internal static bool SteamVrRuntime => string.Equals(VRSession.RuntimeName,
        "SteamVR/OpenXR", StringComparison.OrdinalIgnoreCase);

    // A recognized Frame without AlphaBlend still selects this route, keeping
    // the ordinary game image while the UI explains the missing capability.
    // An explicit companion choice wins only on the connection it can serve.
    internal static bool Required => Standalone || SteamVrRuntime && (SteamLinkConnection.Available
        ? MixedReality.SteamLinkCompanion?.Value != true
        : _feature?.SupportsAlphaBlend == true);
    internal static FrameNativePassthroughStatus Status =>
        _feature?.PassthroughStatus ?? FrameNativePassthroughStatus.Checking;
    internal static bool IsAvailable => !Required || Status == FrameNativePassthroughStatus.Available;
    internal static bool IsActive => Required && _feature?.PassthroughActive == true;

    // Unity1.10 queues the requested mode. The actual value changes on the native
    // submission path, so the camera must remain opaque until that readback agrees.
    // Only this bounded transition polls a native getter; no capability is queried
    // or mode rewritten by steady-state frame maintenance.
    internal static bool TryEnter() => !Required ||
        _feature?.TryEnterPassthrough((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency) == true;

    internal static void Exit() => _feature?.ExitPassthrough();
    internal static bool RestorationPending => _feature?.IsAlphaRestorationPending() == true;

    internal static void Attach(OpenXrEnvironmentBlendFeature feature)
    {
        if (!ReferenceEquals(_feature, feature))
            _feature?.ExitPassthrough();
        _feature = feature;
        _standalone = FrameDefaults.Active;
    }

    internal static void Detach(OpenXrEnvironmentBlendFeature feature)
    {
        if (ReferenceEquals(_feature, feature))
            _feature = null;
    }
}
