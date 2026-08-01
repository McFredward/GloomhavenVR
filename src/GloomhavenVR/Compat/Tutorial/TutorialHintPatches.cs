using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary.CustomLevels;

namespace GloomhavenVR.Compat;

/// <summary>
/// VR HINT TEXT — swaps tutorial hints that teach the FLAT camera controls (mouse /
/// WASD / edge scroll) for the VR locomotion instructions, at display time, keyed by the
/// message's LOCALIZATION KEY (never by display string — a display-string match would be
/// language-dependent and could hit story prose).
///
/// INTERCEPT POINT (verified in the decompiled sources): every level-message page
/// resolves its text in ONE funnel, <c>LevelMessagePageUI.OnLanguageChanged()</c>
/// (LevelMessagePageUI.cs:41-49 — called from <c>Init</c> and <c>RefreshText</c>, and by
/// the I2 language-change event), reading <c>page.PageTextKey</c>; the box title resolves
/// in <c>LevelMessageUILayout.Init/OnLanguageChanged</c> from <c>_message.TitleKey</c>.
/// Postfixes there get the last word after every path that (re)writes the text,
/// including the gamepad-variant rewrites.
///
/// KEY MATCHING, two tiers:
/// 1. <see cref="ExactBodyKeys"/> — pinned keys, exact match. Starts EMPTY on purpose:
///    the tutorial data is a binary blob we cannot read in the repo (see
///    <see cref="TutorialFlowPatches"/> header) so keys cannot be pinned headless.
///    The flow dump prints every tutorial hint's keys; after one hardware run the
///    matched keys get promoted here and the pattern tier can be retired.
/// 2. Pattern tier — the KEY (not the text) contains "CAMERA" (ordinal, case-insensitive).
///    Loc keys are internal IDs; a camera-controls hint key carries the word, story keys
///    do not. Double-gated to tutorial scenarios only, so a coincidental match in normal
///    play is impossible (the patch no-ops there).
///
/// Story/game-rule hints are untouched: anything whose key matches neither tier keeps the
/// game's own translation. Reversible: config off ⇒ postfixes no-op ⇒ vanilla text.
/// </summary>
internal static class TutorialHints
{
    /// <summary>Pinned camera-hint PAGE keys (exact, ordinal-ignore-case). Empty until a
    /// hardware run pins them — the pattern tier carries detection meanwhile.</summary>
    private static readonly HashSet<string> ExactBodyKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pinned camera-hint TITLE keys. Same lifecycle as <see cref="ExactBodyKeys"/>.</summary>
    private static readonly HashSet<string> ExactTitleKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Keys already logged as overridden this session (log once per key, not per repaint).</summary>
    private static readonly HashSet<string> Logged = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsCameraHintKey(string? key, HashSet<string> exact)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        if (exact.Contains(key!))
            return true;
        return key!.IndexOf("CAMERA", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool TryOverrideBody(string? key, out string text)
    {
        text = string.Empty;
        if (!IsCameraHintKey(key, ExactBodyKeys))
            return false;
        text = Loc.Mod("tut_vr_move_body");
        LogOnce(key!, "page");
        return true;
    }

    internal static bool TryOverrideTitle(string? key, out string text)
    {
        text = string.Empty;
        if (!IsCameraHintKey(key, ExactTitleKeys))
            return false;
        text = Loc.Mod("tut_vr_move_title");
        LogOnce(key!, "title");
        return true;
    }

    private static void LogOnce(string key, string kind)
    {
        if (Logged.Add(kind + ":" + key))
            VRLog.Info("Tutorial", $"flat camera hint {kind} key '{key}' replaced with the "
                + "VR locomotion text (keyed override, tutorial only).");
    }
}

/// <summary>Body text of every level-message page — the single funnel all repaint paths
/// share (Init, RefreshText, I2 language change, gamepad-variant swap).</summary>
[HarmonyPatch(typeof(LevelMessagePageUI), "OnLanguageChanged")]
internal static class LevelMessagePageUI_OnLanguageChanged_Patch
{
    private static void Postfix(LevelMessagePageUI __instance)
    {
        try
        {
            if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive)
                return;
            CLevelMessagePage? page = __instance.page; // publicized private
            if (page == null || __instance.information == null)
                return;
            if (TutorialHints.TryOverrideBody(page.PageTextKey, out string text))
                __instance.information.text = text;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"page-text override failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>Box/help-text TITLE — set in Init (and re-set on language/controller change);
/// both funnels get the same postfix so the override survives every rewrite.</summary>
[HarmonyPatch(typeof(LevelMessageUILayout))]
internal static class LevelMessageUILayout_Title_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch("Init")]
    private static void InitPostfix(LevelMessageUILayout __instance, CLevelMessage message)
        => Apply(__instance, message);

    [HarmonyPostfix]
    [HarmonyPatch("OnLanguageChanged")]
    private static void LanguagePostfix(LevelMessageUILayout __instance)
        => Apply(__instance, __instance._message); // publicized private

    private static void Apply(LevelMessageUILayout ui, CLevelMessage? message)
    {
        try
        {
            if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive)
                return;
            if (message == null || ui.title == null || !ui.title.gameObject.activeSelf)
                return;
            if (TutorialHints.TryOverrideTitle(message.TitleKey, out string text))
                ui.title.text = text;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"title override failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
