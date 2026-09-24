using System;
using System.Globalization;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.Net.Desync;
using ScenarioRuleLibrary.YML;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Shares one accepted Continue for an exact native post-quest reward opening. Native reveal,
/// introductions, prosperity follow-ups, unlock videos, distribution and saves remain native.
/// Completion history survives the source closing so a slower peer can finish its own reveal.
/// </summary>
internal static partial class PostQuestRewardSync
{
    private static MapStoryOpeningLedger Ledger = new();
    private static readonly List<int> Peers = new();
    private static object? _opening;
    private static bool _consumed;
    private static uint _captureContext, _campaignContext, _key;
    private static CampaignRewardsManager? _campaign;
    private static UIGuildmasterAdventureRewardsManager? _guild;
    private static UIWindow? _window;
    private static bool _applying, _enabled, _captureFaultReported, _resetFaultReported;

    internal static UIWindow? Window => _window != null && _window.isActiveAndEnabled
        && (_window.IsOpen || _window.IsVisible) ? _window : null;
    internal static object? Opening => Window != null ? _opening : null;
    internal static uint CurrentKey => Window != null ? _key : 0;
    internal static bool SendDue => Ledger.Changed;
    internal static MapStoryOpening[] SampleCompletions()
    {
        if (_opening != null && !_consumed && Window != null) Ledger.Update(_opening, 0, Participants());
        UpdateIntroduction();
        return Ledger.Sample(ActiveIntroductionSubject ?? _opening, _opening);
    }
    internal static void ObserveCompletions(int sender, MapStoryOpening[] entries) => Ledger.Observe(sender, entries);
    internal static bool MatchesPose(int sender, int localPlayerId, uint key, MapStoryOpening[]? entries)
    {
        if (_opening == null || CurrentKey != key || _consumed || entries == null) return false;
        foreach (MapStoryOpening entry in entries)
            if (entry.ContentKey == key && !entry.Finished
                && Ledger.MatchesPose(_opening, sender, localPlayerId, entry.Epoch, entry.Token)) return true;
        return false;
    }
    private static IReadOnlyList<int> Participants()
    {
        VersionGuard.CollectContinuationPeers(Peers, NetPlayerActors.LocalPlayerId());
        return Peers;
    }
    private static void Open(uint context, List<Reward> rewards, string flow)
    {
        _key = RewardKey(context, rewards, flow);
        _opening = new object(); // Native Show reused the window, not the previous opening.
        _consumed = false;
        Ledger.Open(_opening, _key, _key, 1, Participants());
        Ledger.Update(_opening, 0, Participants());
    }
    internal static bool Owns(UIWindow? window) => window != null && ReferenceEquals(_window, window) && _key != 0;

    internal static uint BeginQueuedRewards()
    {
        uint previous = _captureContext;
        _captureContext = 0;
        try { _captureContext = SharedMapRunIdentity.Key; }
        catch (Exception exception)
        {
            if (!_captureFaultReported)
            {
                _captureFaultReported = true;
                VRLog.Warn("WorldUI", "POSTQUEST REWARD IDENTITY: native rewards continue without sharing after provenance capture failed: "
                    + exception.GetType().Name);
            }
        }
        return previous;
    }
    internal static void EndQueuedRewards(uint previous) => _captureContext = previous;

    internal static void CampaignStarting(CampaignRewardsManager manager)
    {
        _campaign = manager;
        _campaignContext = _captureContext;
        if (_campaignContext == 0 && ReferenceEquals(_window, manager.rewardsWindow?.window)) ClearWindow();
    }
    internal static void CampaignShowing(UICampaignRewardWindow window, List<Reward> rewards)
    {
        if (_campaignContext == 0 || _campaign == null || !ReferenceEquals(_campaign.rewardsWindow, window)) return;
        _guild = null;
        _window = window.window;
        Open(_campaignContext, rewards, "campaign");
    }
    internal static void GuildmasterShowing(UIGuildmasterAdventureRewardsManager manager, List<Reward> rewards)
    {
        if (_captureContext == 0)
        {
            if (ReferenceEquals(_guild, manager)) ClearWindow();
            return;
        }
        _campaignContext = 0;
        _guild = manager;
        _window = manager.window;
        Open(_captureContext, rewards, "guildmaster");
    }

    private static uint RewardKey(uint context, List<Reward> rewards, string flow)
    {
        uint hash = SharedMapRunIdentity.Add(context, "postquest-rewards-v1:" + flow);
        foreach (Reward reward in rewards)
        {
            hash = SharedMapRunIdentity.Add(hash, ((int)reward.Type).ToString(CultureInfo.InvariantCulture));
            hash = SharedMapRunIdentity.Add(hash, reward.Amount.ToString(CultureInfo.InvariantCulture));
            hash = SharedMapRunIdentity.Add(hash, reward.ItemID.ToString(CultureInfo.InvariantCulture));
            hash = SharedMapRunIdentity.Add(hash, reward.CharacterID);
            hash = SharedMapRunIdentity.Add(hash, reward.UnlockName);
            hash = SharedMapRunIdentity.Add(hash, reward.LevelUp ? "level-up" : "ordinary");
        }
        // Reward CardIDs and private choices are never part of a network identity.
        return hash == 0 ? 1u : hash;
    }

    internal static bool CanConfirm
    {
        get
        {
            if (_window == null || !_window.IsOpen || !_window.isActiveAndEnabled || _consumed || IntroductionOpen) return false;
            if (_guild != null)
                return _guild.closeButton != null && _guild.closeButton.enabled
                    && _guild.closeButton.gameObject.activeInHierarchy
                    && ((Selectable)_guild.closeButton).IsInteractable()
                    && _guild.rewardsPopupCanvasGroup != null && _guild.rewardsPopupCanvasGroup.interactable;
            UICampaignRewardWindow? window = _campaign?.rewardsWindow;
            return window != null && !window.isRevealing && window.continueAction != null
                && window.continueButton != null && window.continueButton.enabled
                && window.continueButton.gameObject.activeInHierarchy
                && ((Selectable)window.continueButton).IsInteractable();
        }
    }

    // Reserve before callbacks can reenter, publish only after native success.
    internal static bool BeforeCampaignContinue(UICampaignRewardWindow window, out object? opening)
    {
        opening = null;
        if (_applying || !_enabled || !Owns(window.window)) return true;
        opening = Reserve();
        return opening != null;
    }
    internal static object? BeforeGuildmasterHide(UIGuildmasterAdventureRewardsManager manager)
    {
        // Hide is also the native video-completion callback and must remain callable.
        return !_applying && _enabled && ReferenceEquals(_guild, manager) && !_consumed ? Reserve() : null;
    }
    private static object? Reserve()
    {
        if (!CanConfirm || _opening == null) return null;
        _consumed = true;
        return _opening;
    }
    internal static void NativeSucceeded(object? opening)
    {
        if (opening != null) DispatchGuard.Run("PostQuestReward.NativeSucceeded", () => Ledger.Finish(opening));
    }
    internal static void NativeFailed(object? opening)
    {
        if (opening != null && ReferenceEquals(_opening, opening) && _window != null && _window.IsOpen)
        {
            _consumed = false;
            Ledger.RetryTerminal(opening);
        }
    }
    internal static bool TryConfirm()
    {
        object? opening = Reserve();
        if (opening == null) return false;
        uint acceptedKey = _key;
        _applying = true;
        try
        {
            if (_guild != null) _guild.Hide();
            else _campaign!.rewardsWindow.OnContinueButtonClick();
            NativeSucceeded(opening);
        }
        catch
        {
            NativeFailed(opening);
            throw;
        }
        finally { _applying = false; }
        VRLog.Note("WorldUI", $"POSTQUEST REWARD CONTINUE: key={acceptedKey:X8}, applied through native continuation.");
        return true;
    }
    internal static void Tick(bool enabled)
    {
        _enabled = enabled;
        bool shared = enabled && SharedWindows.ParticipatesHere(SharedWindowKind.RewardShowcase);
        if (shared) { UpdateIntroduction(); TickIntroduction(); }
        if (shared && _opening != null && CanConfirm
            && Ledger.Resolve(_opening, NetPlayerActors.LocalPlayerId(), 0, nativeOpen: true) == 1)
            TryConfirm();
    }
    private static void ClearWindow() { _window = null; _guild = null; _key = 0; _opening = null; _consumed = false; }
    internal static void ResetAfterNativeSceneEnd()
    {
        try { Reset(); }
        catch (Exception exception)
        {
            if (!_resetFaultReported)
            {
                _resetFaultReported = true;
                VRLog.Warn("WorldUI", "POSTQUEST REWARD RESET: optional sharing cleanup failed: " + exception.GetType().Name);
            }
        }
    }
    internal static void Reset()
    {
        ClearWindow(); _campaign = null; _captureContext = _campaignContext = 0;
        _applying = _enabled = false;
        ResetIntroductions();
        Ledger = new MapStoryOpeningLedger();
    }
}
