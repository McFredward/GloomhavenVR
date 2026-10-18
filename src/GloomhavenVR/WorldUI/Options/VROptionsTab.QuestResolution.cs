using System;
using TMPro;
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
        // native target pixels and the shared Frame density reference. The factor
        // remains the saved user value, separate from the larger provider scale.
        Func<string> readPixels = () => Rig.QuestEyeResolution.CurrentPixels(german, ReadNumber(item, 0));
        GameObject note = BuildNote(parent, readPixels(), multiline: true);
        // BuildNote writes the caption selected by StampRow. Its inactive donor
        // option can contain another TMP label before Title in hierarchy order.
        TMP_Text? pixels = FindPart<TMP_Text>(note.transform, "Title");
        if (pixels != null) ValueLabels.Add((pixels, readPixels));
        return true;
    }
}
