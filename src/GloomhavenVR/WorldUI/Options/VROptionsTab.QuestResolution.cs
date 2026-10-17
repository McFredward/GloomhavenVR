using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    /// <summary>Keep the shared resolution control, but state its Quest restart boundary.</summary>
    private static bool TryBuildQuestEyeResolutionRow(Transform parent,
        ConfigCatalog.ConfigItem item, string? hintKey)
    {
        if (!QuestStandalonePlatform.FixedEyeTextureAllocation
            || item.Section != "RenderQuality" || item.Key != "EyeResolutionScale")
            return false;
        bool german = Loc.CurrentLanguage == "German";
        string caption = QuestText.Get("eyeResolutionName", german);
        if (!BuildBarAndArrowsRow(parent, item, caption, hintKey))
            BuildStepperRow(parent, item, 0, caption, hintKey);
        // Eye capacity is immutable during this app run. The ordinary shared row
        // still owns edits/readouts and BepInEx persistence. This note reads actual
        // native target pixels; it never predicts delivery from the chosen factor.
        BuildNote(parent, Rig.QuestEyeResolution.CurrentPixels(german), multiline: true);
        return true;
    }
}
