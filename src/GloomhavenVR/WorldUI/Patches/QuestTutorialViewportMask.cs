using System;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// B688's native tutorial list clips built-in Images but leaks the original TMP
/// captions outside the viewport. Its recovered shader loses the common dynamic
/// stencil operation to untouched face defaults. Retained native TMP pixels prove
/// its rectangle-clipping variant works. Keep the original stencil Mask and add
/// the matching uGUI rectangle only to this authored, sprite-less viewport, so a
/// compact managed update can repair existing banks without replacing fonts.
/// </summary>
internal static class QuestTutorialViewportMask
{
    internal static bool Apply(RectTransform? viewport, bool questStandalone)
    {
        if (!questStandalone || viewport == null || viewport.GetComponent<RectMask2D>() != null)
            return false;
        Mask? mask = viewport.GetComponent<Mask>();
        Image? graphic = viewport.GetComponent<Image>();
        // A shaped, partial or visible native mask is not equivalent to a plain
        // rectangle. Do not alter it or apply this to unrelated scroll windows.
        if (mask == null || !mask.enabled || mask.showMaskGraphic
            || graphic == null || !graphic.enabled || graphic.overrideSprite != null
            || (graphic.type != Image.Type.Simple && graphic.type != Image.Type.Sliced)
            || !graphic.fillCenter)
            return false;
        viewport.gameObject.AddComponent<RectMask2D>();
        return true;
    }
}

[HarmonyPatch(typeof(UITutorialSelectorWindow), "Awake")]
internal static class QuestTutorialViewportMaskPatch
{
    private static bool _warned;

    private static void Postfix(ExtendedScrollRect ___scroll)
    {
        try { QuestTutorialViewportMask.Apply(___scroll != null ? ___scroll.viewport : null, QuestStandalonePlatform.Enabled); }
        catch (Exception error)
        {
            if (_warned) return;
            _warned = true;
            VRLog.Warn("Quest", "Native tutorial rectangle clipping could not be retained: " + error.Message);
        }
    }
}
