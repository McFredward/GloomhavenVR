using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Cards;

internal sealed partial class CardsDriver
{
    private (int actor, string line, int board)? _progressStatusKey;

    /// <summary>
    /// Explain an otherwise empty ordinary decision without inventing a control. Mandatory
    /// card/item/panel choices and exhaustion own the same existing mirrored banner first.
    /// Automatic synchronization waits have a separate, passive notice; a yellow initiative
    /// portrait or missing card overlay is never used to infer that the player must rest.
    /// </summary>
    private void UpdateProgressStatus(CardsHandUI? hand)
    {
        string? line = null;
        if (_tray.IsVisible && hand != null && CardsGameApi.InScenario && !_overlayGateBlocked)
        {
            line = ProgressWaitNotice.CurrentHint;
            if (line == null && PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest
                && CardsGameApi.Mode(hand) == CardHandMode.CardsSelection
                && !CardsGameApi.IsShortRestChoosing(hand))
            {
                line = Loc.Mod(!CardsGameApi.IsLocalHand(hand) ? "progress_other_owner"
                    : CardsGameApi.IsConfirmed(hand) ? "progress_ready_wait"
                    : CardsGameApi.MustRestInsteadOfPlay(hand) ? "progress_rest_required"
                    : !CardsGameApi.IsSelectionReady(hand) ? "progress_choose_cards"
                    : CardsGameApi.CanConfirm() || CardsGameApi.ReadyToggleAvailable()
                        ? "progress_selection_confirm" : "progress_selection_other");
            }
            else if (line == null && VRModeStateMachine.TargetingActive
                && Choreographer.s_Choreographer != null
                && Choreographer.s_Choreographer.ThisPlayerHasTurnControl)
                line = Loc.Mod("progress_choose_target");
            else if (line == null && PhaseManager.PhaseType == CPhase.PhaseType.ActionSelection
                && CardsGameApi.IsActionTurn(hand) && _half.IsVisible && !_half.ReadOnly)
                line = Loc.Mod("progress_choose_action");
        }

        if (line == null)
        {
            if (_progressStatusKey.HasValue)
            {
                _progressStatusKey = null;
                _tray.SetPickStatus(null, null, null);
            }
            return;
        }

        var key = (hand?.PlayerActor != null ? Net.NetFigures.StableActorId(hand.PlayerActor) : 0,
            line, _tray.Root != null ? _tray.Root.GetInstanceID() : 0);
        if (_progressStatusKey.HasValue && _progressStatusKey.Value.Equals(key)) return;
        _progressStatusKey = key;
        _tray.SetPickStatus(line, null, null);
    }
}
