using System.Diagnostics;

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
/// Standalone Frame uses the installed launch opt-in marker, not the headset name:
/// a PC streaming to that headset must keep its existing chroma-key presentation.
/// Compatibility is the real Wine/OpenXR instance's alpha-blend capability, not
/// a guessed Proton version floor which would incorrectly exclude backports.
/// </summary>
internal static class FrameNativePassthrough
{
    private static OpenXrEnvironmentBlendFeature? _feature;
    private static bool? _required;

    // File.Exists is appropriate at startup, never for the many MR/backing/UI
    // predicates evaluated each frame. Refresh only for a genuinely new instance;
    // changing the installed opt-in marker takes effect on the next VR startup.
    internal static bool Required => _required ??= !QuestStandalonePlatform.Enabled && FrameDefaults.Active;
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

    internal static void Attach(OpenXrEnvironmentBlendFeature feature)
    {
        if (!ReferenceEquals(_feature, feature))
            _feature?.ExitPassthrough();
        _feature = feature;
        _required = !QuestStandalonePlatform.Enabled && FrameDefaults.Active;
    }

    internal static void Detach(OpenXrEnvironmentBlendFeature feature)
    {
        if (ReferenceEquals(_feature, feature))
            _feature = null;
    }
}
