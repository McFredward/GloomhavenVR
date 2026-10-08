using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

/// <summary>Keep the running Quest Vulkan swapchain; downscale its viewport only.</summary>
internal static class QuestEyeResolution
{
    // QuestBuild sets every authored quality level to this before XR starts.
    internal static int StartupMsaa => QuestStandalonePlatform.StandaloneMsaaDefault;
    private static float _lastRequested = -1f;
    private static float _lastViewport = -1f;

    internal static float EffectiveScale(float requested) =>
        QuestStandalonePlatform.FixedEyeTextureAllocation ? Mathf.Clamp(requested, 0.5f, 1f) : requested;

    internal static bool TryApply(float requested)
    {
        if (!QuestStandalonePlatform.FixedEyeTextureAllocation) return false;
        float effective = EffectiveScale(requested);
        bool changed = Mathf.Abs(requested - _lastRequested) > 0.0005f;
        // Do not retry an ignored viewport request every frame. A changed user
        // value may try again, without destroying any external swapchain image.
        if (changed || Mathf.Abs(effective - _lastViewport) > 0.0005f)
        {
            if (Mathf.Abs(XRSettings.renderViewportScale - effective) > 0.0005f)
                XRSettings.renderViewportScale = effective;
            _lastRequested = requested;
            _lastViewport = effective;
            VRLog.Note("Rig", $"Quest Vulkan eye allocation retained: {XRSettings.eyeTextureWidth}x"
                + $"{XRSettings.eyeTextureHeight}; saved scale={requested:F2}, "
                + $"viewport requested={effective:F2}, readback={XRSettings.renderViewportScale:F2}. "
                + "Live swapchain resize is disabled after the B623 native crash; "
                + "supersampling above 1.00 is unavailable. Saved configuration is unchanged.");
        }
        return true;
    }
}
