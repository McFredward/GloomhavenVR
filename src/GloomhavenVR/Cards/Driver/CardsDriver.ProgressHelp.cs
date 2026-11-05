using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Cards;

internal sealed partial class CardsDriver
{
    private (int actor, string line, int board)? _progressStatusKey;
    private readonly DecisionHelpClock _progressHelpClock = new();
    private readonly List<CAbilityCard> _progressHelpRound = new(2);
    private CPlayerActor? _progressHelpActor;
    private int _progressHelpResponse;

    /// <summary>
    /// Explain an otherwise empty ordinary decision without inventing a control. Mandatory
    /// card/item/panel choices and exhaustion own the same existing mirrored banner first.
    /// Automatic synchronization waits have a separate, passive notice; a yellow initiative
    /// portrait or missing card overlay is never used to infer that the player must rest.
    /// </summary>
    private void UpdateProgressStatus(CardsHandUI? hand)
    {
        // This optional fallback does not own an element/bonus or other mandatory
        // writer's existing placard. In particular, Never cannot clear that text.
        string? resident = _tray.PickBannerText;
        if (resident != null && (!_progressStatusKey.HasValue || resident != _progressStatusKey.Value.line))
        {
            _progressStatusKey = null;
            _progressHelpClock.Reset();
            return;
        }
        string? hint = null;
        Choreographer? owner = Choreographer.s_Choreographer;
        Choreographer.CWaitState? wait = owner != null ? owner.m_WaitState : null;
        CPlayerActor? actor = hand?.PlayerActor;
        bool automatic = false;
        if (CardsGameApi.InScenario && owner != null)
        {
            hint = ProgressWaitNotice.ImmediateHintKey;
            automatic = hint != null;
            bool inputOffer = !_burnLayoutPending;
            if (hint == null && hand != null && actor != null && inputOffer
                && PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest
                && CardsGameApi.Mode(hand) == CardHandMode.CardsSelection
                && !CardsGameApi.IsShortRestChoosing(hand))
            {
                hint = !CardsGameApi.IsLocalHand(hand) ? "progress_other_owner"
                    : CardsGameApi.IsConfirmed(hand) ? "progress_ready_wait"
                    : CardsGameApi.MustRestInsteadOfPlay(hand) ? "progress_rest_required"
                    : !CardsGameApi.IsSelectionReady(hand) ? "progress_choose_cards"
                    : CardsGameApi.CanConfirm() || CardsGameApi.ReadyToggleAvailable()
                        ? "progress_selection_confirm" : "progress_selection_other";
            }
            else if (hint == null && hand != null && actor != null && inputOffer && VRModeStateMachine.TargetingActive
                && Choreographer.s_Choreographer != null
                && Choreographer.s_Choreographer.ThisPlayerHasTurnControl)
                hint = "progress_choose_target";
            else if (hint == null && hand != null && actor != null && inputOffer && PhaseManager.PhaseType == CPhase.PhaseType.ActionSelection
                && CardsGameApi.IsActionTurn(hand) && _half.IsVisible && !_half.ReadOnly
                && _halfBuffer.Count > 0 && !CardsGameApi.CanConfirm())
                hint = "progress_choose_action";
        }

        // Only an accepted native card selection changes this snapshot. Fan/board/head
        // movement and widget recreation cannot restart the five-minute observation.
        int response = automatic ? 0 : ProgressHelpResponse(actor);
        object? decisionActor = automatic ? wait?.m_StateWaitActor : actor;
        bool shown = _progressHelpClock.Observe(hint, owner, wait, decisionActor,
            (int)PhaseManager.PhaseType, response, UnityEngine.Time.unscaledTime,
            CardsConfig.ProgressHelpMode.Value, CardsConfig.ProgressHelpDelaySeconds.Value);
        string? line = shown && _tray.IsVisible && !_overlayGateBlocked && !_modalInputBlocked
            ? Loc.Mod(hint!) : null;

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

    private int ProgressHelpResponse(CPlayerActor? actor)
    {
        var cards = actor?.CharacterClass.RoundAbilityCards;
        bool changed = !ReferenceEquals(actor, _progressHelpActor)
            || (cards?.Count ?? 0) != _progressHelpRound.Count;
        for (int i = 0; !changed && cards != null && i < cards.Count; i++)
            changed = !ReferenceEquals(cards[i], _progressHelpRound[i]);
        if (changed)
        {
            _progressHelpActor = actor;
            _progressHelpRound.Clear();
            if (cards != null) _progressHelpRound.AddRange(cards);
            unchecked { ++_progressHelpResponse; }
        }
        return _progressHelpResponse;
    }
}
