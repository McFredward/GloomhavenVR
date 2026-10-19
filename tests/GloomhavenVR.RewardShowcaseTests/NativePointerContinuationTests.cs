using System;
using System.Collections.Generic;
using System.Linq;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    // Native ProcessRewards is the oracle. Do not duplicate its authority formula
    // in an expected CanConfirm predicate: a real native mouse edge either advances
    // the original coroutine or remains waiting under the same role and settings.
    private static bool NativeMouseCanAdvance(bool networked, Func<bool>? checker)
    {
        var native = Component<UIRewardsManager>("Native mouse oracle");
        native.myWindow = native.gameObject.AddComponent<UIWindow>();
        native.networkProcessIfServer = networked;
        native.interactionChecker = checker;
        var process = native.BeginNativeRewards(new[] { 901, 902 });
        native.StepNativeRewards(process);
        Singleton<InputManager>.Instance.PlayerControl.MouseClickLeft.WasPressed = true;
        native.StepNativeRewards(process);
        Singleton<InputManager>.Instance.PlayerControl.MouseClickLeft.WasPressed = false;
        bool advanced = native.ShownRewards.Count == 2;
        // Only the fixture uses received native messages to finish this independent
        // oracle. No production presentation path is given MoveToNextReward access.
        for (int i = 0; i < 6 && native.ProcessingRewards; i++)
        {
            native.ReceiveNativeProcessNextReward();
            native.StepNativeRewards(process);
        }
        Check(!native.ProcessingRewards, "native authority oracle is reaped after observation");
        process.Dispose();
        return advanced;
    }

    private static List<RewardGroup> Groups(int offset = 0) => new()
    {
        new RewardGroup(new[] { offset + 10, offset + 20 }),
        new RewardGroup(new[] { offset + 30 }),
    };

    private static void NativePointerMenuAndAuthority()
    {
        foreach (string mode in new[] { "Campaign", "Guildmaster", "SingleScenario", "Tutorial", "MapEvent" })
        foreach (bool gamepad in new[] { false, true })
        foreach (bool managerEnabled in new[] { true, false })
        foreach (string role in new[] { "offline", "host", "owning-client", "observer-client", "observer-host", "local-client" })
        {
            var guild = new Guild(running: false);
            Check(!guild.Rewards.ProcessingRewards && !guild.Rewards.networkProcessIfServer && !guild.Rewards.PendingInput,
                "native construction starts idle without network processing or queued input");
            guild.Window.IsOpen = guild.Window.IsVisible = false;
            guild.Rewards.IsGuildmasterMode = mode == "Guildmaster";
            if (mode is "SingleScenario" or "Tutorial" or "MapEvent")
                Singleton<ScenarioRewardManager>.Instance = null!;
            else if (mode == "Campaign")
                Singleton<ScenarioRewardManager>.Instance = Component<CampaignScenarioRewardManager>("Campaign chest scenario");
            FFSNetwork.IsOnline = role != "offline";
            FFSNetwork.IsClient = role is "owning-client" or "observer-client" or "local-client";
            bool networked = role is not ("offline" or "local-client");
            Func<bool>? checker = role switch
            {
                "owning-client" => () => true,
                "observer-client" or "observer-host" => () => false,
                _ => null,
            };
            Singleton<ESCMenu>.Instance.IsOpen = true;
            bool nativePermits = NativeMouseCanAdvance(networked, checker);
            Synchronizer.Sent = 0;
            InputManager.GamePadInUse = gamepad;
            guild.Rewards.enabled = managerEnabled;
            int completed = 0;
            guild.Rewards.StartRewardsShowcase(Groups(), networkProcessIfServer: networked,
                onProcessEnded: () => completed++, interactionChecker: checker);
            var process = guild.Rewards.CurrentNativeProcess;
            Check(guild.Window.IsOpen && !guild.Rewards.ProcessingRewards,
                "native startup opens window without fixture inventing an active coroutine");
            guild.Rewards.StepNativeRewards(process);
            ModalFallback.PollRewardsForTest(true);
            Check(guild.Rewards.ShownRewards.Count == 1 && !guild.Rewards.PendingInput && completed == 0,
                "native first reward resets any input before pointer continuation");
            if (nativePermits)
            {
                Check(RewardShowcaseButton.Current != null && RewardShowcaseButton.Current.IsInteractable(),
                    managerEnabled
                        ? "visible chest Continue remains pointer-actionable with native ESC window open"
                        : "live MEC reward Continue remains pointer-actionable on a disabled manager");
                for (int displayed = 1; displayed <= 3; displayed++)
                {
                    Time.frameCount++;
                    RewardShowcase.Tick(true);
                    var button = RewardShowcaseButton.Current!;
                    int beforeCalls = guild.Rewards.NativeCalls;
                    int beforeMessages = Synchronizer.Sent;
                    button.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData());
                    button.OnPointerDown();
                    button.OnPointerUp();
                    button.OnPointerClick();
                    Check(guild.Rewards.PendingInput && guild.Rewards.NativeCalls == beforeCalls + 1,
                        "real Continue Selectable pointer dispatch feeds one native input latch");
                    Check(guild.Rewards.ShownRewards.Count == displayed && completed == 0 && Synchronizer.Sent == beforeMessages,
                        "pointer input never advances rewards or sends native progression itself");
                    button.OnPointerClick();
                    Check(guild.Rewards.NativeCalls == beforeCalls + 1,
                        "same-frame pointer repeat cannot overwrite pending native input");
                    Time.frameCount++;
                    RewardShowcase.Tick(true);
                    Check(!button.IsInteractable(), "queued native input disables Continue until consumed");
                    button.OnPointerClick();
                    Check(guild.Rewards.NativeCalls == beforeCalls + 1,
                        "disabled Selectable cannot dispatch another queued input");
                    for (int step = 0; step < 4 && guild.Rewards.ProcessingRewards
                        && guild.Rewards.ShownRewards.Count <= displayed; step++)
                        guild.Rewards.StepNativeRewards(process);
                    Check(guild.Rewards.ShownRewards.Count == Math.Min(3, displayed + 1),
                        "pointer continuation displays every native reward across group transition");
                    if (displayed < 3)
                        Check(completed == 0 && guild.Window.IsOpen, "intermediate native group cannot release chest callback");
                }
                Check(completed == 1 && guild.Rewards.CompletedProcesses == 1 && !guild.Rewards.ProcessingRewards && !guild.Window.IsOpen,
                    "final native EndProcess alone closes chest and releases callback exactly once");
                Check(!guild.Rewards.StepNativeRewards(process) && completed == 1,
                    "completed native process cannot repeat its release callback");
            }
            else
            {
                Check(RewardShowcaseButton.Current != null && !RewardShowcaseButton.Current.IsInteractable(),
                    "native observer receives visible disabled Continue rather than ownership bypass");
                int before = guild.Rewards.NativeCalls;
                RewardShowcaseButton.Current!.OnPointerClick();
                Check(!RewardShowcase.TryConfirm() && !guild.Rewards.PendingInput && guild.Rewards.NativeCalls == before,
                    "observer pointer and bridge both preserve actual native actor authority");
                guild.Rewards.StepNativeRewards(process);
                Check(guild.Rewards.ShownRewards.Count == 1 && completed == 0 && Synchronizer.Sent == 0,
                    "observer cannot advance or broadcast without original native message");
                for (int step = 0; step < 8 && guild.Rewards.ProcessingRewards; step++)
                {
                    guild.Rewards.ReceiveNativeProcessNextReward();
                    guild.Rewards.StepNativeRewards(process);
                }
                Check(completed == 1 && guild.Rewards.CompletedProcesses == 1 && Synchronizer.Sent == 0,
                    "original received native messages finish observer without local progression broadcast");
            }
            Check(Singleton<ESCMenu>.Instance.IsOpen,
                "pointer continuation never closes unrelated native ESC window");
            process.Dispose();
            Time.frameCount++;
            RewardShowcase.Tick(true);
            Check(RewardShowcaseButton.Current == null && !RewardShowcase.TryConfirm(),
                "native completed process removes helper and refuses further pointer input");
        }
    }

    private static void RepeatedNativePointerSessions()
    {
        var guild = new Guild(running: false);
        guild.Window.IsOpen = guild.Window.IsVisible = false;
        Singleton<ESCMenu>.Instance.IsOpen = true;
        int callbacks = 0;
        for (int session = 0; session < 4; session++)
        {
            // Same singleton, same reward twice, then changed groups, then same
            // changed groups again: original EndProcess intentionally leaves its
            // input latch set. Native first ProcessReward, not the mod, resets it.
            int oldShown = guild.Rewards.ShownRewards.Count;
            guild.Rewards.StartRewardsShowcase(Groups(session < 2 ? 0 : 100),
                onProcessEnded: () => callbacks++);
            var process = guild.Rewards.CurrentNativeProcess;
            Check(!guild.Rewards.ProcessingRewards, "repeated native start does not inherit a fabricated running flag");
            if (session > 0) Check(guild.Rewards.PendingInput, "native EndProcess retains terminal input until next native reward");
            guild.Rewards.StepNativeRewards(process);
            ModalFallback.PollRewardsForTest(true);
            Check(!guild.Rewards.PendingInput && guild.Rewards.ShownRewards.Count == oldShown + 1
                && RewardShowcaseButton.CanClick, "native first reward re-arms reused Continue without presentation resetting input");
            guild.Rewards.StepNativeRewards(process);
            Check(guild.Rewards.ShownRewards.Count == oldShown + 1 && callbacks == session,
                "stale terminal input cannot auto-skip a reward in the next native session");
            guild.Window.enabled = false;
            Time.frameCount++;
            RewardShowcase.Tick(true);
            Check(!RewardShowcase.TryConfirm(), "disabled original reward window remains a genuine pointer gate");
            guild.Window.enabled = true;
            for (int card = 0; card < 3; card++)
            {
                Time.frameCount++;
                RewardShowcase.Tick(true);
                Check(RewardShowcaseButton.Current != null && RewardShowcaseButton.Current.onClick.ListenerCount == 1,
                    "reused native session has exactly one original bridge callback");
                RewardShowcaseButton.Current!.OnPointerClick();
                for (int step = 0; step < 4 && guild.Rewards.ProcessingRewards
                    && guild.Rewards.ShownRewards.Count <= oldShown + card + 1; step++)
                    guild.Rewards.StepNativeRewards(process);
            }
            Check(callbacks == session + 1 && guild.Rewards.CompletedProcesses == session + 1,
                "each repeated same or changed reward session releases precisely its native callback");
            Check(guild.Rewards.ShownRewards.GetRange(oldShown, 3).SequenceEqual(new[]
                { session < 2 ? 10 : 110, session < 2 ? 20 : 120, session < 2 ? 30 : 130 }),
                "reused reward manager retains every native reward in its original order");
            process.Dispose();
            RewardShowcase.Tick(true);
        }
        Check(Singleton<ESCMenu>.Instance.IsOpen, "all repeated chest sessions leave unrelated ESC state unchanged");
    }

    private static void CampaignNativePointerWithMenu()
    {
        var campaign = new Campaign();
        Singleton<ESCMenu>.Instance.IsOpen = true;
        Check(RewardShowcase.CanConfirm, "campaign pointer Continue follows its original native button despite ESC window");
        campaign.Button.OnPointerClick();
        Check(campaign.Completions == 1 && Singleton<ESCMenu>.Instance.IsOpen,
            "campaign menu-open pointer dispatch invokes native callback without closing ESC");
        campaign.Rewards.isRevealing = true;
        Time.frameCount++;
        Check(!RewardShowcase.TryConfirm(), "menu-open campaign still preserves original reveal gate");
        campaign.Rewards.isRevealing = false;
        campaign.Button.interactable = false;
        Check(!RewardShowcase.TryConfirm(), "menu-open campaign still preserves original button authority");
    }
}
