using System;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// VR input for the original scenario reward flow. Guildmaster waits for InControl mouse input,
/// which a world-space uGUI click never sets. Campaign wires its original Continue button only
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

    internal static UIWindow? Window
    {
        get
        {
            ScenarioRewardManager? manager = Manager;
            if (manager == null || !manager.IsShown) return null;
            UIWindow? window = manager is CampaignScenarioRewardManager campaign
                ? campaign.manager?.rewardsWindow?.window
                : Guildmaster?.myWindow;
            return window != null && window.isActiveAndEnabled
                && (window.IsOpen || window.IsVisible) ? window : null;
        }
    }

    internal static uint ContentKey => RewardShowcaseIdentity.ContentKey(Window);

    private static ScenarioRewardManager? Manager => Singleton<ScenarioRewardManager>.IsInitialized
        ? Singleton<ScenarioRewardManager>.Instance : null;
    private static UIRewardsManager? Guildmaster => Singleton<UIRewardsManager>.IsInitialized
        ? Singleton<UIRewardsManager>.Instance : null;

    internal static bool CanConfirm
    {
        get
        {
            if (!_enabled || !WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive || Window == null
                || (Singleton<ESCMenu>.IsInitialized && Singleton<ESCMenu>.Instance.IsOpen)) return false;
            if (Manager is CampaignScenarioRewardManager campaign)
            {
                UICampaignRewardWindow? rewards = campaign.manager?.rewardsWindow;
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
        _enabled = enabled;
        UIWindow? window = enabled ? Window : null;
        if (window == null)
        {
            GuildButton.Dispose();
            _wiredCampaign = null;
            _loggedWindow = null;
            _loggedPermission = null;
            return;
        }
        if (Manager is CampaignScenarioRewardManager campaign)
        {
            GuildButton.Dispose();
            UICampaignRewardWindow rewards = campaign.manager.rewardsWindow;
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
                + $"mode={(Manager is CampaignScenarioRewardManager ? "Campaign" : "Guildmaster")}, "
                + $"canContinue={permission}; native reward continuation retains multiplayer authority.");
        }
    }

    internal static bool TryConfirm()
    {
        if (!CanConfirm || _lastConfirmFrame == Time.frameCount) return false;
        _lastConfirmFrame = Time.frameCount;
        if (Manager is CampaignScenarioRewardManager campaign)
            campaign.manager.rewardsWindow.OnContinueButtonClick();
        else
            Guildmaster!.ConfirmPressed();
        VRLog.Info("WorldUI", "REWARD SHOWCASE INPUT: explicit VR Continue forwarded to the native reward input.");
        return true;
    }
}
