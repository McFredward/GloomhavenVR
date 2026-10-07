using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private static RenderTexture? DesktopScrubTarget;

    /// <summary>
    /// Native backbuffer cameras remain valid menu anchors after this mod redirects
    /// their output. Match actual owned target objects, never a texture name: the
    /// game's character-preview and other private render cameras are not anchors.
    /// </summary>
    internal static bool OwnsPresentationTarget(Camera camera)
    {
        if (camera == null)
            return false;
        if (CapturedSet.TryGetValue(camera, out CapturedCamera? captured) && captured != null)
        {
            RenderTexture? target = captured.Owner.TargetFor(captured);
            if (target != null && camera.targetTexture == target)
                return true;
        }
        return DesktopScrubTarget != null && camera.targetTexture == DesktopScrubTarget;
    }
}
