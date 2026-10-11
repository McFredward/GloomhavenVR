using GloomhavenVR.Core;
using GloomhavenVR.Rig;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    internal static void RefreshAfterGraphicsProfile() => Rebuild();
}

// Keep deferred action execution outside the partial type's static row construction.
// Creating the Curated table never reads its UI fields or applies a graphics profile.
internal static class GraphicsProfileActions
{
    internal static void Apply(int index)
    {
        TickGuard.Run("VROptionsTab.GraphicsProfile", () =>
        {
            if (!GraphicsProfiles.Apply(index)) return;
            // Refresh dependencies and values. No stored index can overwrite later edits.
            VROptionsTab.RefreshAfterGraphicsProfile();
        }, "WorldUI");
    }
}
