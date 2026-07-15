using UnityEngine.XR;

namespace GloomhavenVR.Hands;

/// <summary>Haptic feedback presets. FROZEN Phase-2 API — request effects by intent, not amplitude.</summary>
internal enum HapticPreset
{
    /// <summary>Subtle tick when a hover/highlight starts.</summary>
    HoverTick,

    /// <summary>Confirmation pulse for a poke/click commit.</summary>
    ClickPulse,

    /// <summary>Stronger pulse when an object is grabbed.</summary>
    GrabPulse
}

/// <summary>
/// Thin wrapper over the engine haptics API.
///
/// Verified against the REAL UnityEngine.XRModule.dll (2021.3.5f1) with ilspycmd (2026-07-15):
/// <code>
///   // UnityEngine.XR.InputDevice (struct)
///   public bool SendHapticImpulse(uint channel, float amplitude, float duration = 1f)
///   public bool TryGetHapticCapabilities(out HapticCapabilities capabilities)
///   public void StopHaptics()
/// </code>
/// OpenXR exposes one impulse channel (0). Failures (no device / no haptics) are silent
/// by design — haptics are decoration, never load-bearing.
/// </summary>
internal static class VRHaptics
{
    internal static void Play(in InputDevice device, HapticPreset preset)
    {
        if (!device.isValid)
            return;

        switch (preset)
        {
            case HapticPreset.HoverTick:
                device.SendHapticImpulse(0u, 0.15f, 0.012f);
                break;
            case HapticPreset.ClickPulse:
                device.SendHapticImpulse(0u, 0.55f, 0.03f);
                break;
            case HapticPreset.GrabPulse:
                device.SendHapticImpulse(0u, 0.8f, 0.05f);
                break;
        }
    }
}
