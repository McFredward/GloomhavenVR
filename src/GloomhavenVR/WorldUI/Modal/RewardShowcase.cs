using System;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// VR input for the original scenario reward flow. UIRewardsManager waits for native mouse or
/// confirmation input, which a world-space uGUI click never sets. Campaign wires Continue only
/// during mouse-mode Awake, so entering VR after gamepad creation can leave that button unwired.
/// Neither case permits hiding the window: its original continuation releases the choreographer
/// and, in multiplayer, sends ConfirmReward/ProcessNextReward from the native controlling player.
/// </summary>
internal static partial class RewardShowcase
{
    private static UICampaignRewardWindow? _wiredCampaign;
    private static readonly RewardShowcaseButton GuildButton = new();
    private static UIWindow? _loggedWindow;
    private static bool? _loggedPermission;
    private static bool _enabled;
    private static int _lastConfirmFrame = -1;
    private static UIRewardsManager? _pendingNativeInput;

    internal static UIWindow? Window
    {
        get
        {
            // UIEventPanel.OnFinishedEvent starts the native showcase directly on the
            // map, without ScenarioRewardManager. Requiring a scenario controller hid
            // the VR input there forever, before DistributeRewards could run. Resolve
            // the live original process/window, while retaining the scenario's exact
            // campaign manager when one owns the current reward.
            UIWindow? campaignWindow = CampaignWindow?.window;
            if (IsLiveWindow(campaignWindow)) return campaignWindow;
            UIRewardsManager? guild = Guildmaster;
            return guild != null && guild.ProcessingRewards && IsLiveWindow(guild.myWindow)
                ? guild.myWindow : null;
        }
    }

    internal static uint ContentKey => RewardShowcaseIdentity.ContentKey(Window);

    private static ScenarioRewardManager? Manager => Singleton<ScenarioRewardManager>.IsInitialized
        ? Singleton<ScenarioRewardManager>.Instance : null;
    private static UIRewardsManager? Guildmaster => Singleton<UIRewardsManager>.IsInitialized
        ? Singleton<UIRewardsManager>.Instance : null;

    private static bool IsLiveWindow(UIWindow? window) => window != null && window.isActiveAndEnabled
        && (window.IsOpen || window.IsVisible);

    private static UICampaignRewardWindow? CampaignWindow
    {
        get
        {
            if (Manager is CampaignScenarioRewardManager campaign && campaign.IsShown)
                return campaign.manager?.rewardsWindow;
            return Singleton<CampaignRewardsManager>.IsInitialized
                ? Singleton<CampaignRewardsManager>.Instance?.rewardsWindow : null;
        }
    }

    internal static bool CanConfirm
    {
        get
        {
            if (!_enabled || !WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive || Window == null
                || (Singleton<ESCMenu>.IsInitialized && Singleton<ESCMenu>.Instance.IsOpen)) return false;
            UICampaignRewardWindow? rewards = CampaignWindow;
            if (rewards != null && ReferenceEquals(Window, rewards.window))
            {
                ExtendedButton? button = rewards?.continueButton;
                return rewards != null && rewards.isActiveAndEnabled && !rewards.isRevealing && rewards.continueAction != null
                    && button != null && button.gameObject.activeInHierarchy && button.enabled
                    && ((Selectable)button).IsInteractable();
            }
            UIRewardsManager? guild = Guildmaster;
            return guild != null && guild.isActiveAndEnabled && guild.ProcessingRewards && !guild.isConfirmPressed
                && (!guild.networkProcessIfServer
                    || (guild.interactionChecker != null ? guild.interactionChecker() : !FFSNetwork.IsClient));
        }
    }

    internal static void Tick(bool enabled)
    {
        if (_pendingNativeInput != null && (!_pendingNativeInput.ProcessingRewards || !_pendingNativeInput.isConfirmPressed))
        {
            VRLog.Note("WorldUI", "REWARD SHOWCASE INPUT: native showcase consumed VR Continue; "
                + $"processing={_pendingNativeInput.ProcessingRewards}, windowOpen={_pendingNativeInput.myWindow?.IsOpen}.");
            _pendingNativeInput = null;
        }
        if (!enabled) _pendingNativeInput = null;
        _enabled = enabled;
        TickMapRewardButtons(enabled);
        UIWindow? window = enabled ? Window : null;
        if (window == null)
        {
            GuildButton.Dispose();
            _wiredCampaign = null;
            _loggedWindow = null;
            _loggedPermission = null;
            return;
        }
        UICampaignRewardWindow? rewards = CampaignWindow;
        if (rewards != null && ReferenceEquals(window, rewards.window))
        {
            GuildButton.Dispose();
            if (_wiredCampaign != rewards && rewards.continueButton != null)
            {
                // Remove only this exact native runtime callback, then bind it once. Adding
                // unconditionally would double-dispatch every already mouse-wired window.
                rewards.continueButton.onClick.RemoveListener(rewards.OnContinueButtonClick);
                rewards.continueButton.onClick.AddListener(rewards.OnContinueButtonClick);
                _wiredCampaign = rewards;
                VRLog.Info("WorldUI", "REWARD SHOWCASE INPUT: original Campaign Continue callback bound exactly once.");
            }
        }
        else if (Guildmaster != null)
            GuildButton.Tick(Guildmaster, CanConfirm);
        bool permission = CanConfirm;
        if (_loggedWindow != window || _loggedPermission != permission)
        {
            _loggedWindow = window;
            _loggedPermission = permission;
            VRLog.Info("WorldUI", $"REWARD SHOWCASE INPUT: native window='{window.name}', "
                + $"flow={(rewards != null && ReferenceEquals(window, rewards.window) ? "Campaign" : "UIRewardsManager")}, "
                + $"canContinue={permission}; native reward continuation retains multiplayer authority.");
        }
    }

    internal static bool TryConfirm()
    {
        if (!CanConfirm || _lastConfirmFrame == Time.frameCount) return false;
        _lastConfirmFrame = Time.frameCount;
        UICampaignRewardWindow? rewards = CampaignWindow;
        if (rewards != null && ReferenceEquals(Window, rewards.window))
            rewards.OnContinueButtonClick();
        else
        {
            // UIRewardsManager is also used by SingleScenario / FrontEndTutorial. Its
            // ConfirmPressed method is a GAMEPAD adapter: outside Guildmaster it calls
            // LongConfirmHandler.Pressed, which refuses a uGUI click without the physical
            // CONFIRM_ACTION_BUTTON edge. Build 510 therefore drew a dead Continue button.
            // Feed only the native input latch, equivalent to the other MouseClickLeft
            // operand in ProcessRewards. That coroutine still checks authority, advances
            // each reward/group, sends ProcessNextReward and invokes onProcessEnded. This
            // is NOT nextRewardOverride, which would skip those input/authority checks.
            UIRewardsManager guild = Guildmaster!;
            guild.isConfirmPressed = true;
            _pendingNativeInput = guild;
        }
        VRLog.Note("WorldUI", "REWARD SHOWCASE INPUT: explicit VR Continue forwarded to the native reward input.");
        return true;
    }
}
