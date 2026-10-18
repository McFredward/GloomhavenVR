using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

/// <summary>Keep the preselected Quest Vulkan allocation and its complete viewport.</summary>
internal static class QuestEyeResolution
{
    // QuestBuild sets every authored quality level to this before XR starts.
    internal static int StartupMsaa => QuestStandalonePlatform.StandaloneMsaaDefault;
    private static float _lastRequested = -1f;
    private static bool _viewportRestoreAttempted;
    private static int _diagnosticReports;

    internal static float EffectiveScale(float requested) =>
        QuestStandalonePlatform.FixedEyeTextureAllocation ? QuestEyeAllocation.ClampScale(requested) : requested;

    internal static string CurrentPixels(bool german, float requested)
    {
        float provider = QuestEyeAllocation.ProviderScale(requested);
        return string.Format(QuestText.Get("eyeResolutionCurrent", german),
            XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight,
            (int)System.Math.Round(QuestEyeAllocation.Quest3DefaultWidth * provider),
            (int)System.Math.Round(QuestEyeAllocation.Quest3DefaultHeight * provider));
    }

    internal static bool TryApply(float requested)
    {
        if (!QuestStandalonePlatform.FixedEyeTextureAllocation) return false;
        float effective = EffectiveScale(requested);
        bool changed = Mathf.Abs(requested - _lastRequested) > 0.0005f;
        // B674 hardware still clips the entire tracked view at a .8 eye subrect,
        // with correct CPU poses and matching provider matrices. Select the saved
        // capacity on the initialized/stopped display instead. Never reintroduce
        // a partial viewport or resize a running Vulkan swapchain. A missing/late
        // startup callback may restore full viewport once, without a retry loop.
        if (!_viewportRestoreAttempted)
        {
            _viewportRestoreAttempted = true;
            try
            {
                if (Mathf.Abs(XRSettings.renderViewportScale - 1f) > 0.0005f)
                    XRSettings.renderViewportScale = 1f;
            }
            catch (System.Exception error)
            {
                VRLog.Warn("Rig", "Quest full eye viewport restoration was refused: "
                    + error.GetType().Name + ". The running allocation is retained; no automatic retry.");
            }
        }
        if (changed)
        {
            // A small protected startup record survives the general mod-log cap.
            // This is API state, not evidence of headset image quality. Never poll
            // native resources or format a diagnostic on the unchanged frame path.
            if (VRLog.Wants(VRLogLevel.Debug) && _diagnosticReports < 2)
            {
                _diagnosticReports++;
                UnityEngine.Debug.Log("[Quest startup] resolution current="
                    + XRSettings.eyeTextureWidth + "x" + XRSettings.eyeTextureHeight
                    + " allocation=" + XRSettings.eyeTextureResolutionScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " viewport=" + XRSettings.renderViewportScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " nextStart=" + effective.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    + " nextProvider=" + QuestEyeAllocation.ProviderScale(effective).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                    + " referenceShortAxis=" + QuestEyeAllocation.ReferencePixelsAtOne
                    + " policy=next-app-start");
            }
            _lastRequested = requested;
            VRLog.Note("Rig", $"Quest Vulkan eye allocation retained: {XRSettings.eyeTextureWidth}x"
                + $"{XRSettings.eyeTextureHeight}; saved scale={requested:F2}, "
                + $"next-start density={effective:F2}, provider scale={QuestEyeAllocation.ProviderScale(effective):F4}, "
                + $"reference short axis={QuestEyeAllocation.ReferencePixelsAtOne}px at 1.0, "
                + $"full viewport readback={XRSettings.renderViewportScale:F2}. "
                + "Resolution changes apply at the next app start; live swapchain resize and "
                + "partial eye viewports are disabled. Saved configuration is unchanged.");
        }
        return true;
    }
}
