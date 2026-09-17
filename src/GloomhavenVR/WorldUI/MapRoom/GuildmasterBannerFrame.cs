using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>The native UI coordinate frame behind a converted window. Read only, used on
/// banner transitions; the original widget never derives its next native pose from a VR host.</summary>
internal static class GuildmasterBannerFrame
{
    internal static Matrix4x4 Native(Transform? parent) => Native(parent, 0);

    private static Matrix4x4 Native(Transform? parent, int depth)
    {
        if (parent == null) return Matrix4x4.identity;
        if (depth >= 64) return parent.localToWorldMatrix;
        var panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel panel = panels[i];
            if (!ReferenceEquals(panel.Target, parent)) continue;
            return Native(panel.OriginalParent, depth + 1)
                * Matrix4x4.TRS(panel.OriginalLocalPosition, panel.OriginalLocalRotation,
                    panel.OriginalLocalScale);
        }
        return Native(parent.parent, depth + 1)
            * Matrix4x4.TRS(parent.localPosition, parent.localRotation, parent.localScale);
    }
}
