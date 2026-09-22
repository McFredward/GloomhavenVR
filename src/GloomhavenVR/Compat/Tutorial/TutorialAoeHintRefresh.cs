using System;
using GloomhavenVR.Board;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Compat;

/// <summary>A visible native tutorial must describe a newly selected binding immediately.
/// Subscribe once; scan only on config changes, never per frame, and rewrite matching text only.</summary>
internal static class TutorialAoeHintRefresh
{
    private static bool _bound;

    internal static void EnsureBound()
    {
        if (_bound || BoardConfig.AoeRotationInput == null) return;
        _bound = true;
        BoardConfig.AoeRotationInput.SettingChanged += OnChanged;
        Plugin.PrimaryHand.SettingChanged += OnChanged;
        ComfortSettings.AnyChanged += OnComfortChanged;
    }

    private static void OnComfortChanged(string key)
    {
        if (key == "TurnHand" || key == "TurnMode") Refresh();
    }

    private static void OnChanged(object sender, EventArgs args) => Refresh();

    private static void Refresh()
    {
        if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive) return;
        try
        {
            foreach (LevelMessagePageUI page in UnityEngine.Object.FindObjectsOfType<LevelMessagePageUI>(true))
                if (page.page != null && page.information != null
                    && TutorialAoeHint.TryOverride(page.page.PageTextKey, page.page.PageTextKeyController, out string body))
                    page.information.text = body;
            foreach (LevelMessageUILayout layout in UnityEngine.Object.FindObjectsOfType<LevelMessageUILayout>(true))
                if (layout._message != null && layout.title != null
                    && TutorialAoeHint.TryOverride(layout._message.TitleKey, layout._message.TitleKeyController, out string title))
                    layout.title.text = title;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"AoE binding hint refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
