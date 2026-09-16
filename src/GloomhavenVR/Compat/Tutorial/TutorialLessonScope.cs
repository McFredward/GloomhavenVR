using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Compat;

/// <summary>
/// Admission for ADDED VR lessons, distinct from the generic tutorial input/text bridge.
/// The user reported the controls course repeating in every tutorial (2026-09-16).
/// IsTutorialActive intentionally includes all frontend lessons, Guildmaster onboarding and
/// map introductions; it cannot identify the one tutorial allowed to add new steps.
///
/// UITutorialSelectorWindow.CreateTutorialOptions preserves GetTutorials() order. Its first
/// entry therefore identifies the actual first tutorial, regardless of internal names such as
/// Tutorial_2. Capture that descriptor before StartTutorial unloads the menu service, then compare
/// BOTH fields with the native current tutorial. No guessed filename, save mutation or permanent
/// "already completed" latch: replaying the first tutorial still teaches its VR controls.
/// Unknown/missing identity leaves the native tutorial alone, including its adapted controls text.
/// </summary>
internal static class TutorialLessonScope
{
    private static string? _firstId;
    private static string? _firstFilename;

    internal static void CaptureFirstTutorial(IReadOnlyList<ITutorial>? tutorials)
    {
        _firstId = null;
        _firstFilename = null;
        if (tutorials == null || tutorials.Count == 0 || tutorials[0] == null)
            return;
        // Read both before publishing either: a failed descriptor read must not retain a
        // partially captured identity from this launch or an earlier launch.
        string? id = tutorials[0].TutorialID;
        string? filename = tutorials[0].TutorialFileName;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(filename))
            return;
        _firstId = id;
        _firstFilename = filename;
    }

    internal static bool MatchesFirstTutorial(bool online, EGameMode mode,
        string? currentId, string? currentFilename, string? firstId, string? firstFilename)
        => !online && mode == EGameMode.FrontEndTutorial
           && !string.IsNullOrWhiteSpace(firstId) && !string.IsNullOrWhiteSpace(firstFilename)
           && string.Equals(currentId, firstId, StringComparison.Ordinal)
           && string.Equals(currentFilename, firstFilename, StringComparison.Ordinal);

    internal static bool IsActive
    {
        get
        {
            try
            {
                if (!TutorialVR.Enabled || !LevelEventsController.s_EventsControllerActive)
                    return false;
                GlobalData? global = SaveData.Instance?.Global;
                return global != null && MatchesFirstTutorial(FFSNetwork.IsOnline,
                    global.GameMode, global.CurrentFrontEndTutorialID,
                    global.CurrentFrontEndTutorialFilename, _firstId, _firstFilename);
            }
            catch (Exception)
            {
                return false; // unavailable native context never admits an additional lesson
            }
        }
    }
}

/// <summary>Read menu ordering before the native loader destroys its service. Never prevents
/// StartTutorial itself: only the optional VR additions fail closed if the catalogue is missing.</summary>
[HarmonyPatch(typeof(TutorialService), "StartTutorial")]
internal static class TutorialService_StartTutorial_Patch
{
    private static void Prefix(TutorialService __instance)
    {
        TutorialLessonScope.CaptureFirstTutorial(null);
        try
        {
            TutorialLessonScope.CaptureFirstTutorial(__instance.GetTutorials());
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", "First tutorial identity unavailable; added VR lessons remain "
                + $"disabled for this launch: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
