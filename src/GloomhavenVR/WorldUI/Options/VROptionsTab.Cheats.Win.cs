using System;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    private static ScenarioState? _winArmedScenario;
    private static float _winArmedAt = float.NegativeInfinity;

    private static bool WinArmed => _winArmedScenario != null
        && ReferenceEquals(_winArmedScenario, ScenarioManager.CurrentScenarioState)
        && Time.unscaledTime - _winArmedAt < CheatArmSeconds;

    private static int BuildWinScenarioRow()
    {
        if (ContentRoot == null) return 0;
        BuildNote(ContentRoot, Loc.Mod("cheat_win_hint"));
        RegisterCheatRow(BuildLinkRow(ContentRoot, WinScenarioCaption(), OnWinScenario, asAction: true),
            WinScenarioCaption);
        return 1;
    }

    private static string WinScenarioCaption()
    {
        string reason = ScenarioWinCheat.UnavailableReason();
        return Loc.Mod(reason.Length != 0 ? reason : WinArmed ? "cheat_win_confirm" : "cheat_win");
    }

    private static void OnWinScenario()
    {
        try
        {
            if (!CheatsAvailable) return;
            string reason = ScenarioWinCheat.UnavailableReason();
            if (reason.Length != 0)
            {
                _winArmedScenario = null;
                VRLog.Info("WorldUI", "CHEAT 'win scenario': REFUSED — " + reason);
            }
            else if (!WinArmed)
            {
                _winArmedScenario = ScenarioManager.CurrentScenarioState;
                _winArmedAt = Time.unscaledTime;
            }
            else
            {
                ScenarioState expected = _winArmedScenario!;
                _winArmedScenario = null;
                if (ScenarioWinCheat.TryWin(expected, Close))
                    VRLog.Note("WorldUI", "CHEAT 'win scenario': native victory requested via DebugMenu.WinNoToggle; normal results and rewards follow.");
            }
            RefreshCheatRows();
        }
        catch (Exception ex)
        {
            _winArmedScenario = null;
            VRLog.Error("WorldUI", "CHEAT 'win scenario' failed: " + ex);
        }
    }
}
