using GloomhavenVR.Core;
using GloomhavenVR.Rig;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    private static void ApplyGraphicsProfile(int index)
    {
        TickGuard.Run("VROptionsTab.GraphicsProfile", () =>
        {
            if (!GraphicsProfiles.Apply(index)) return;
            // Refresh dependencies and values. No stored index can overwrite later edits.
            Rebuild();
        }, "WorldUI");
    }
}
