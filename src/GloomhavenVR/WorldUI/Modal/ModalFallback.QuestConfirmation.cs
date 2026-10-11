using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    /// <summary>
    /// The live floated native quest confirmation, addressed by the popup manager's actual
    /// subject. Flat hosts send the native SelectQuest action without a VR selection record.
    /// The manager also owns a separate multiplayer hover preview; an authored QuestPopup ID
    /// cannot distinguish those two windows. Parking Accept and publishing its shared pose
    /// must name the same native instance, even if both views are floated or their IDs differ.
    /// A missing conversion remains a presentation fallback, never a readiness restriction.
    /// </summary>
    internal static UIWindow? FloatedQuestConfirmationWindow()
    {
        UIWindow? window = MapRoom.NativeMapQuestSelection.ConfirmationWindow;
        return FloatIsLive(window) ? window : null;
    }
}
