using UnityEngine;

namespace GloomhavenVR.Core;

internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // A budget mask suppresses pixel delivery only. Keep the outstanding wall restore
        // latch when the mask arrived after a piece was driven: unfade/drop/toggle-off must
        // still return native materials and enable state even if all pieces are now masked.
        private static bool SkipBudgetMaskedAttachment(MountedProp piece, int previousState,
                                                        ref int highest)
        {
            if (!ScenarioSceneryBudget.IsOwnedHidden(piece.Renderer))
                return false;
            if (piece.Driven && previousState > highest)
                highest = previousState;
            return true;
        }
    }
}
