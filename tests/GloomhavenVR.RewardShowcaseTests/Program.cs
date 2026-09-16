using System;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

static class Program
{
    private static int _assertions;

    private static void Check(bool value, string reason)
    {
        _assertions++;
        if (!value) throw new Exception("Reward showcase assertion: " + reason);
    }

    private static T Component<T>(string name) where T : UnityEngine.Component, new() => new GameObject(name).AddComponent<T>();

    private static void Reset()
    {
        RewardShowcase.Tick(false);
        Singleton<InputManager>.Instance = Component<InputManager>("Input");
        Singleton<KeyActionHandlerController>.Instance = Component<KeyActionHandlerController>("Key actions");
        Synchronizer.Sent = ActionProcessor.HaltRequests = 0;
        Singleton<ScenarioRewardManager>.Instance = null!;
        Singleton<CampaignRewardsManager>.Instance = null!;
        Singleton<UIRewardsManager>.Instance = null!;
        Singleton<ESCMenu>.Instance = Component<ESCMenu>("Escape menu");
        WorldUIConfig.ConversionActive = true;
        WorldUIConfig.ModalWindowStyle = true;
        FlatScreen.ManualScreenActive = false;
        FFSNetwork.IsClient = FFSNetwork.IsOnline = false;
        Choreographer.s_Choreographer = null!;
        RewardShowcasePlacement.ClearInitialPose();
        RewardShowcasePlacement.ClearInitialAuthority();
        ModalFallback.PlacementWindow = null;
        ModalFallback.PlacementPanel = null;
        ModalFallback.PlacementGrab = null;
        ModalFallback.Failed.Clear();
        ModalFallback.PreservedPoses = 0;
        SharedWindows.Participating = true;
        Time.frameCount += 10;
    }

    private sealed class Campaign
    {
        internal readonly CampaignScenarioRewardManager Scenario = Component<CampaignScenarioRewardManager>("Scenario campaign owner");
        internal readonly CampaignRewardsManager Manager = Component<CampaignRewardsManager>("Owned campaign rewards");
        internal readonly UICampaignRewardWindow Rewards = Component<UICampaignRewardWindow>("Native campaign rewards");
        internal readonly ExtendedButton Button = Component<ExtendedButton>("Native Continue");
        internal readonly UIWindow Window;
        internal int Completions;

        internal Campaign(bool mouseWired = false)
        {
            Reset();
            Window = Rewards.gameObject.AddComponent<UIWindow>();
            Rewards.window = Window;
            Rewards.ContinueButton = Button;
            Button.transform.SetParent(Rewards.transform);
            Manager.RewardsWindow = Rewards;
            Scenario.Manager = Manager;
            Singleton<ScenarioRewardManager>.Instance = Scenario;
            Singleton<CampaignRewardsManager>.Instance = Manager;
            Rewards.ContinueAction = () => Completions++;
            if (mouseWired) Rewards.WireNativeMouseListener();
            ModalFallback.PollRewardsForTest(true);
            Check(ReferenceEquals(ModalFallback.PolledWindow, Window), "actual modal poll enrolls original campaign window");
        }

        internal void Denied(Action disable, Action restore, string reason)
        {
            int before = Rewards.NativeCalls;
            disable();
            Time.frameCount++;
            Check(!RewardShowcase.CanConfirm && !RewardShowcase.TryConfirm(), reason);
            Check(Rewards.NativeCalls == before, reason + " does not call native continuation");
            restore();
        }
    }

    private sealed class Guild
    {
        internal readonly GuildmasterScenarioRewardManager Scenario = Component<GuildmasterScenarioRewardManager>("Scenario guild owner");
        internal readonly UIRewardsManager Rewards = Component<UIRewardsManager>("Native guild rewards");
        internal readonly UIWindow Window;

        internal Guild()
        {
            Reset();
            Window = Rewards.gameObject.AddComponent<UIWindow>();
            Rewards.Window = Window;
            Singleton<ScenarioRewardManager>.Instance = Scenario;
            Singleton<UIRewardsManager>.Instance = Rewards;
            ModalFallback.PollRewardsForTest(true);
            Check(ReferenceEquals(ModalFallback.PolledWindow, Window), "actual modal poll enrolls original guild window");
        }

        internal void Denied(Action disable, Action restore, string reason)
        {
            int before = Rewards.NativeCalls;
            disable();
            Time.frameCount++;
            RewardShowcase.Tick(true);
            Check(!RewardShowcase.CanConfirm && !RewardShowcase.TryConfirm(), reason);
            Check(Rewards.NativeCalls == before, reason + " leaves native input untouched");
            restore();
        }
    }

    private static void CampaignButtonRepair()
    {
        var gamepad = new Campaign();
        Check(ReferenceEquals(RewardShowcase.Window, gamepad.Window), "campaign uses scenario-owned original window");
        Check(gamepad.Button.onClick.ListenerCount == 1, "gamepad-created campaign button receives one native listener");
        gamepad.Button.onClick.Invoke();
        Check(gamepad.Rewards.NativeCalls == 1 && gamepad.Completions == 1,
            "repaired campaign button advances through native continuation");
        int extra = 0;
        gamepad.Button.onClick.AddListener(() => extra++);
        for (int i = 0; i < 100; i++) { Time.frameCount++; RewardShowcase.Tick(true); }
        gamepad.Button.onClick.Invoke();
        Check(gamepad.Rewards.NativeCalls == 2 && gamepad.Completions == 2 && extra == 1,
            "stable ticks neither multiply native listener nor remove unrelated listeners");
        gamepad.Scenario.Shown = false;
        RewardShowcase.Tick(true);
        Check(RewardShowcase.Window == null && !RewardShowcase.TryConfirm(), "closed campaign cannot receive bridge input");
        gamepad.Scenario.Shown = true;
        Time.frameCount++;
        RewardShowcase.Tick(true);
        gamepad.Button.onClick.Invoke();
        Check(gamepad.Rewards.NativeCalls == 3 && extra == 2, "campaign close and reopen retain exactly one native binding");

        var mouse = new Campaign(mouseWired: true);
        Check(mouse.Button.onClick.ListenerCount == 1, "existing mouse listener is not duplicated by VR repair");
        mouse.Button.onClick.Invoke();
        Check(mouse.Rewards.NativeCalls == 1 && mouse.Completions == 1, "mouse-created campaign button continues exactly once");
        RewardShowcase.Tick(false);
        Check(!RewardShowcase.TryConfirm(), "module teardown disables reward bridge input");
        mouse.Button.onClick.Invoke();
        Check(mouse.Completions == 2, "module teardown preserves the original native button continuation");
        RewardShowcase.Tick(true);
        Time.frameCount++;
        mouse.Button.onClick.Invoke();
        Check(mouse.Completions == 3 && mouse.Button.onClick.ListenerCount == 1,
            "re-enabled bridge still has exactly one original callback");

        var decoy = Component<CampaignRewardsManager>("Unrelated global campaign manager");
        decoy.RewardsWindow = Component<UICampaignRewardWindow>("Unrelated reward window");
        Singleton<CampaignRewardsManager>.Instance = decoy;
        Check(ReferenceEquals(RewardShowcase.Window, mouse.Window), "global campaign singleton cannot replace scenario owner");
        Time.frameCount++;
        Check(RewardShowcase.TryConfirm() && mouse.Completions == 4, "bridge invokes only scenario-owned campaign continuation");
        Check(!RewardShowcase.TryConfirm() && mouse.Completions == 4, "same-frame duplicate bridge press is refused");

        var duplicate = new Campaign(mouseWired: true);
        Action? nativePendingContinuation = () => duplicate.Completions++;
        // CampaignRewardsManager clears its onConfirmed before starting deferred Finish.
        // Keep this native idempotence distinct from the mod's listener count: two real
        // clicks can enter the native handler twice without completing gameplay twice.
        duplicate.Rewards.ContinueAction = () =>
        {
            var pending = nativePendingContinuation;
            nativePendingContinuation = null;
            pending?.Invoke();
        };
        duplicate.Button.onClick.Invoke();
        duplicate.Button.onClick.Invoke();
        Check(duplicate.Rewards.NativeCalls == 2 && duplicate.Completions == 1,
            "two campaign native clicks preserve native completion idempotence without extra listeners");
    }

    private static void CampaignGates()
    {
        var campaign = new Campaign();
        campaign.Denied(() => campaign.Rewards.Revealing = true, () => campaign.Rewards.Revealing = false, "campaign reveal animation gates input");
        Action? callback = campaign.Rewards.ContinueAction;
        campaign.Denied(() => campaign.Rewards.ContinueAction = null, () => campaign.Rewards.ContinueAction = callback, "campaign without continuation is not actionable");
        campaign.Denied(() => campaign.Button.interactable = false, () => campaign.Button.interactable = true, "native campaign ownership disables Continue");
        campaign.Denied(() => campaign.Button.AncestorsInteractable = false, () => campaign.Button.AncestorsInteractable = true, "native ancestor interactability gates Continue");
        campaign.Denied(() => campaign.Button.enabled = false, () => campaign.Button.enabled = true, "disabled native campaign button cannot confirm");
        campaign.Denied(() => campaign.Button.gameObject.SetActive(false), () => campaign.Button.gameObject.SetActive(true), "hidden native campaign button cannot confirm");
        campaign.Denied(() => campaign.Window.gameObject.SetActive(false), () => campaign.Window.gameObject.SetActive(true), "inactive campaign root cannot confirm");
        campaign.Denied(() => campaign.Window.enabled = false, () => campaign.Window.enabled = true, "disabled campaign window cannot confirm");
        campaign.Denied(() => campaign.Rewards.enabled = false, () => campaign.Rewards.enabled = true, "disabled campaign component cannot confirm");
        campaign.Denied(() => campaign.Window.IsOpen = campaign.Window.IsVisible = false, () => campaign.Window.IsOpen = campaign.Window.IsVisible = true, "closed campaign window cannot confirm");
        campaign.Denied(() => campaign.Scenario.Shown = false, () => campaign.Scenario.Shown = true, "inactive scenario reward flow cannot confirm");
        campaign.Denied(() => WorldUIConfig.ConversionActive = false, () => WorldUIConfig.ConversionActive = true, "disabled VR conversion cannot confirm campaign");
        campaign.Denied(() => FlatScreen.ManualScreenActive = true, () => FlatScreen.ManualScreenActive = false, "manual desktop mode gates campaign bridge");
        campaign.Denied(() => Singleton<ESCMenu>.Instance.IsOpen = true, () => Singleton<ESCMenu>.Instance.IsOpen = false, "ESC modal gates campaign bridge");
        FFSNetwork.IsOnline = FFSNetwork.IsClient = true;
        Check(RewardShowcase.CanConfirm, "controlled campaign client follows native button authority rather than host-only rule");
        campaign.Rewards.ContinueAction = () => { campaign.Completions++; campaign.Rewards.ContinueAction = null; };
        Check(RewardShowcase.TryConfirm(), "campaign native continuation receives allowed explicit press");
        Time.frameCount++;
        Check(!RewardShowcase.TryConfirm() && campaign.Completions == 1, "completed native campaign continuation cannot repeat");
    }

    private static void ContinueHoverAndClick()
    {
        var guild = new Guild();
        var button = Component<RewardContinueButton>("Continue pointer surface");
        var image = button.gameObject.AddComponent<Image>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => RewardShowcase.TryConfirm());
        button.RefreshSkin();
        Check(image.sprite == NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle), "reward Continue starts with native idle artwork");
        button.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData());
        Check(image.sprite == NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Accent)
            && image.color == NativeButtonSkin.ColorFor(NativeButtonSkin.FaceState.Accent), "pointer hover paints native highlighted sprite and tint");
        button.RefreshSkin();
        Check(image.sprite == NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Accent), "per-frame refresh preserves pointer hover artwork");
        button.OnPointerDown();
        Check(image.sprite == NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Pressed), "pointer down paints native pressed artwork");
        button.OnPointerUp();
        Check(image.sprite == NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Accent), "pointer release returns to hovered artwork");
        button.OnPointerClick();
        Check(guild.Rewards.PendingInput, "ordinary reward Button pointer click reaches native input bridge");
        button.OnPointerExit();
        Check(image.sprite == NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Idle), "pointer exit restores native idle artwork");
        guild.Rewards.NativeConsumeInput();
        button.interactable = false;
        button.OnPointerEnter(new UnityEngine.EventSystems.PointerEventData());
        Check(image.sprite == NativeButtonSkin.SpriteFor(NativeButtonSkin.FaceState.Disabled), "disabled observer control does not highlight");
        button.OnPointerClick();
        Check(!guild.Rewards.PendingInput, "disabled observer pointer click cannot send native input");
    }

    private static void NativeRewardCompletion()
    {
        foreach (bool guildMode in new[] { false, true })
        foreach (bool online in new[] { false, true })
        {
            var guild = new Guild();
            guild.Rewards.IsGuildmasterMode = guildMode;
            FFSNetwork.IsOnline = online;
            FFSNetwork.IsClient = online; // Owning client must work too.
            guild.Rewards.InteractionChecker = () => true;
            bool blocked = true;
            guild.Rewards.onProcessEnded = () => blocked = false;
            var process = guild.Rewards.BeginNativeRewards(new[] { 10, 20 }, new[] { 30 });
            guild.Rewards.StepNativeRewards(process);
            Check(guild.Rewards.ShownRewards.Count == 1 && blocked, "native first reward shows before any confirmation");
            if (!guildMode)
            {
                guild.Rewards.ConfirmPressed();
                guild.Rewards.StepNativeRewards(process);
                Check(!guild.Rewards.PendingInput && guild.Rewards.ShownRewards.Count == 1 && blocked,
                    "old gamepad adapter reproduces tutorial reward deadlock without physical button edge");
            }
            for (int displayed = 1; displayed <= 3; displayed++)
            {
                Time.frameCount++;
                Check(RewardShowcase.TryConfirm() && guild.Rewards.PendingInput,
                    "explicit VR input works in tutorial and guild modes without physical gamepad edge");
                Check(blocked && guild.Rewards.CompletedProcesses == 0,
                    "VR input cannot itself complete reward process or release game queue");
                // Native group boundaries consume additional frames after the input.
                for (int step = 0; step < 3 && guild.Rewards.ProcessingRewards
                    && (guild.Rewards.PendingInput || guild.Rewards.ShownRewards.Count <= displayed); step++)
                    guild.Rewards.StepNativeRewards(process);
                Check(guild.Rewards.ShownRewards.Count == Math.Min(3, displayed + 1),
                    "native iterator shows every reward across group boundaries");
                if (displayed < 3) Check(blocked, "intermediate reward does not release native queue");
            }
            Check(!blocked && !guild.Window.IsOpen && guild.Rewards.CompletedProcesses == 1,
                "native final reward invokes onProcessEnded and releases game queue exactly once");
            Check((Synchronizer.Sent > 0) == online, "native iterator alone sends online reward progression");
            guild.Rewards.StepNativeRewards(process);
            Check(guild.Rewards.CompletedProcesses == 1, "completed native iterator cannot repeat callback");
        }
        var observer = new Guild();
        FFSNetwork.IsClient = FFSNetwork.IsOnline = true;
        observer.Rewards.InteractionChecker = () => false;
        bool observerBlocked = true;
        observer.Rewards.onProcessEnded = () => observerBlocked = false;
        var peerProcess = observer.Rewards.BeginNativeRewards(new[] { 99 });
        observer.Rewards.StepNativeRewards(peerProcess);
        Check(!RewardShowcase.TryConfirm() && !observer.Rewards.PendingInput,
            "shared observer cannot enqueue native reward continuation");
        observer.Rewards.StepNativeRewards(peerProcess);
        Check(observerBlocked && Synchronizer.Sent == 0, "observer remains waiting without authoritative native message");
        observer.Rewards.ReceiveNativeProcessNextReward();
        for (int i = 0; i < 3; i++) observer.Rewards.StepNativeRewards(peerProcess);
        Check(!observerBlocked && observer.Rewards.CompletedProcesses == 1 && Synchronizer.Sent == 0,
            "native peer message completes observer without rebroadcast or local ownership bypass");
    }

    private static void GuildInputAndLifecycle()
    {
        var guild = new Guild();
        Check(ReferenceEquals(RewardShowcase.Window, guild.Window), "guild uses native scenario reward window");
        Check(ReferenceEquals(RewardShowcaseButton.Owner, guild.Rewards) && RewardShowcaseButton.CanClick,
            "guild continuation surface is attached to actionable native owner");
        Check(RewardShowcase.TryConfirm() && guild.Rewards.NativeCalls == 1 && guild.Rewards.PendingInput,
            "guild press reaches native ConfirmPressed input latch");
        Check(guild.Rewards.ConsumedInputs == 0 && guild.Window.IsOpen && guild.Rewards.ProcessingRewards,
            "bridge does not advance rewards, hide native window or end processing");
        Time.frameCount++;
        Check(!RewardShowcase.TryConfirm() && guild.Rewards.NativeCalls == 1,
            "pending guild native input cannot be enqueued repeatedly");
        guild.Rewards.NativeConsumeInput();
        Check(guild.Rewards.ConsumedInputs == 1 && RewardShowcase.TryConfirm(), "native consumer re-arms the next guild reward press");
        guild.Rewards.NativeConsumeInput();
        guild.Rewards.ProcessingRewards = false;
        Time.frameCount++;
        Check(!RewardShowcase.TryConfirm(), "finished guild coroutine rejects further input");
        guild.Scenario.Shown = false;
        RewardShowcase.Tick(true);
        Check(RewardShowcaseButton.Owner == null && !RewardShowcaseButton.CanClick, "native guild close removes helper surface");
        guild.Rewards.ProcessingRewards = guild.Scenario.Shown = true;
        RewardShowcase.Tick(true);
        Check(RewardShowcaseButton.Owner == guild.Rewards && RewardShowcase.CanConfirm, "native guild reopening restores helper without stale pending state");
        ModalFallback.PollRewardsForTest(false);
        Check(RewardShowcaseButton.Owner == null && ModalFallback.PolledWindow == null && !RewardShowcase.TryConfirm(),
            "leaving scenario through actual poll disposes guild helper and disables continuation");
    }

    private static void GuildGatesAndAuthority()
    {
        var guild = new Guild();
        guild.Denied(() => guild.Window.gameObject.SetActive(false), () => guild.Window.gameObject.SetActive(true), "inactive guild root cannot confirm");
        guild.Denied(() => guild.Window.enabled = false, () => guild.Window.enabled = true, "disabled guild window cannot confirm");
        guild.Denied(() => guild.Window.IsOpen = guild.Window.IsVisible = false, () => guild.Window.IsOpen = guild.Window.IsVisible = true, "closed guild window cannot confirm");
        guild.Denied(() => guild.Rewards.ProcessingRewards = false, () => guild.Rewards.ProcessingRewards = true, "idle guild manager cannot confirm");
        guild.Denied(() => WorldUIConfig.ConversionActive = false, () => WorldUIConfig.ConversionActive = true, "disabled VR conversion cannot confirm guild");
        guild.Denied(() => FlatScreen.ManualScreenActive = true, () => FlatScreen.ManualScreenActive = false, "manual desktop mode gates guild bridge");
        guild.Denied(() => Singleton<ESCMenu>.Instance.IsOpen = true, () => Singleton<ESCMenu>.Instance.IsOpen = false, "ESC modal gates guild bridge");
        guild.Denied(() => guild.Rewards.enabled = false, () => guild.Rewards.enabled = true, "disabled guild component cannot confirm");

        // Cross native network processing and actor authority independently. The game permits
        // non-networked showcases locally; networked chest rewards use the actor predicate.
        foreach (bool networked in new[] { false, true })
        foreach (bool client in new[] { false, true })
        foreach (bool? controlled in new bool?[] { null, false, true })
        {
            guild.Rewards.NetworkProcess = networked;
            FFSNetwork.IsOnline = true;
            FFSNetwork.IsClient = client;
            guild.Rewards.InteractionChecker = controlled.HasValue ? () => controlled.Value : null;
            bool expected = !networked || (controlled ?? !client);
            Time.frameCount++;
            RewardShowcase.Tick(true);
            Check(RewardShowcase.CanConfirm == expected, "guild authority matches native processing predicate");
            Check(RewardShowcaseButton.CanClick == expected, "guild helper reflects current actor permission");
            int before = guild.Rewards.NativeCalls;
            Check(RewardShowcase.TryConfirm() == expected, "guild explicit input uses same native authority");
            Check(guild.Rewards.NativeCalls == before + (expected ? 1 : 0), "unauthorized guild press never touches native latch");
            guild.Rewards.NativeConsumeInput();
        }
        guild.Rewards.NetworkProcess = true;
        bool owner = false;
        guild.Rewards.InteractionChecker = () => owner;
        Check(!RewardShowcase.CanConfirm, "guild non-owner remains observer");
        owner = true;
        Check(RewardShowcase.CanConfirm, "ownership changes are read live rather than cached");
        Singleton<ScenarioRewardManager>.Instance = null!;
        Check(RewardShowcase.Window == null && !RewardShowcase.TryConfirm(), "unrelated guild singleton cannot claim absent scenario flow");
    }

    private static uint OpenChest(string guid, ScenarioManager.ObjectImportType type = ScenarioManager.ObjectImportType.Chest)
    {
        Choreographer.s_Choreographer = new Choreographer
        {
            m_BlockClientMessageProcessing = true,
            LastMessage = new ScenarioRuleLibrary.CActivateProp_MessageData
            {
                m_Prop = new ScenarioRuleLibrary.CProp { PropGuid = guid, ObjectType = type },
            },
        };
        return RewardShowcaseIdentity.HashChestGuid(guid);
    }

    private static void IdentityAndInitialPlacement()
    {
        var guild = new Guild();
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == 0, "missing native choreography has no reward identity");
        uint chest = OpenChest("Chest_A");
        Check(chest == 4133502334u, "public chest GUID uses stable FNV identity");
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == chest, "exact blocked activation identifies current chest reward");
        uint secondChest = OpenChest("Chest_B");
        Check(secondChest != chest && RewardShowcaseIdentity.ContentKey(guild.Window) == secondChest,
            "distinct chest GUIDs remain distinct even when reward display is identical");
        OpenChest("Chest_A", ScenarioManager.ObjectImportType.GoalChest);
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == chest, "goal chest activation also has native reward identity");
        Choreographer.s_Choreographer.m_BlockClientMessageProcessing = false;
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == 0, "unblocked stale activation cannot identify current reward");
        OpenChest("Chest_A", ScenarioManager.ObjectImportType.Door);
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == 0, "non-chest activation never becomes a reward identity");
        OpenChest("Chest_A");
        Choreographer.s_Choreographer.LastMessage = new object();
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == 0, "unrelated current message cannot use historical chest identity");
        Choreographer.s_Choreographer.LastMessage = new ScenarioRuleLibrary.CActivateProp_MessageData();
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == 0, "missing native prop cannot address a reward");
        OpenChest("");
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == 0 && RewardShowcaseIdentity.HashChestGuid(null) == 0,
            "unknown chest GUID remains unaddressable");
        OpenChest("Chest_A");
        guild.Window.IsOpen = guild.Window.IsVisible = false;
        Check(RewardShowcaseIdentity.ContentKey(guild.Window) == 0, "closed reward has no identity");
        guild.Window.IsOpen = guild.Window.IsVisible = true;
        Check(RewardShowcaseIdentity.ContentKey(null) == 0, "absent window has no identity");

        Time.unscaledTime = 100f;
        var grab = new GrabbableModal { GrabRoot = new GameObject("Native reward holder").transform };
        var position = new Vector3(3, 4, 5);
        var rotation = new Quaternion(0, .5f, 0, .8f);
        Check(!RewardShowcasePlacement.ApplyInitialPose(guild.Window, grab), "unset remote pose cannot move local reward");
        RewardShowcasePlacement.SetInitialPose(chest, position, rotation, 150f);
        int anchor = ModalFallback.SpentAnchors;
        Check(RewardShowcasePlacement.ApplyInitialPose(guild.Window, grab), "matching native chest accepts elected pose before reveal");
        Check(grab.Placements == 1 && grab.Position == position && grab.Rotation == rotation,
            "initial placement applies exact elected position and rotation");
        Check(grab.GrabRoot.localScale == new Vector3(1.5f, 1.5f, 1.5f) && ModalFallback.SpentAnchors == anchor + 1,
            "initial placement uses shared sizing and consumes default placement anchor");

        int placed = grab.Placements;
        SharedWindows.Participating = false;
        Check(!RewardShowcasePlacement.ApplyInitialPose(guild.Window, grab), "local-only reward participation refuses remote pose");
        SharedWindows.Participating = true;
        OpenChest("Chest_B");
        Check(!RewardShowcasePlacement.ApplyInitialPose(guild.Window, grab), "pose for previous chest cannot move next chest reward");
        OpenChest("Chest_A");
        var decoy = Component<UIWindow>("Other live reward window");
        Check(!RewardShowcasePlacement.ApplyInitialPose(decoy, grab), "same native message cannot position an unrelated window");
        Check(!RewardShowcasePlacement.ApplyInitialPose(guild.Window, null), "missing grab cannot receive initial pose");
        Check(!RewardShowcasePlacement.ApplyInitialPose(guild.Window, new GrabbableModal()), "unbuilt grab cannot receive initial pose");
        Time.unscaledTime = 102.01f;
        Check(!RewardShowcasePlacement.ApplyInitialPose(guild.Window, grab), "expired elected pose cannot relocate late unrelated presentation");
        Time.unscaledTime = 100.5f;
        RewardShowcasePlacement.ClearInitialPose();
        Check(!RewardShowcasePlacement.ApplyInitialPose(guild.Window, grab), "cleared network session cannot replay old pose");
        Check(grab.Placements == placed && ModalFallback.SpentAnchors == anchor + 1,
            "all rejected initial poses leave position and local anchor untouched");
    }

    private static void SharedRevealHandoff()
    {
        var guild = new Guild();
        uint chest = OpenChest("SharedChest");
        var panel = new ConvertedPanel();
        var otherPanel = new ConvertedPanel();
        var grab = new GrabbableModal { GrabRoot = new GameObject("Pending reward holder").transform };
        ModalFallback.PlacementWindow = guild.Window;
        ModalFallback.PlacementPanel = panel;
        ModalFallback.PlacementGrab = grab;
        Time.unscaledTime = 200f;
        Check(RewardShowcasePlacement.TryReveal(panel), "standalone reward without network authority reveals normally");
        RewardShowcasePlacement.SetInitialAuthority(chest, false);
        Check(!RewardShowcasePlacement.TryReveal(panel), "follower cannot reveal its local seat before the elected pose arrives");
        Check(RewardShowcase.CanConfirm, "presentation wait does not mutate native confirmation authority");
        Check(RewardShowcasePlacement.TryReveal(otherPanel), "reward election never delays an unrelated converted panel");
        for (int elapsed = 1; elapsed <= 30; elapsed++)
        {
            Time.unscaledTime = 200f + elapsed;
            Check(!RewardShowcasePlacement.TryReveal(panel), "follower remains hidden without a deadline fallback to local placement");
        }
        var position = new Vector3(10, 4, -2);
        var rotation = new Quaternion(0, .25f, 0, .95f);
        RewardShowcasePlacement.SetInitialPose(RewardShowcaseIdentity.HashChestGuid("PreviousChest"), position, rotation, 180f);
        Check(!RewardShowcasePlacement.TryReveal(panel) && grab.Placements == 0,
            "previous chest mailbox cannot release the follower reveal gate");
        RewardShowcasePlacement.SetInitialPose(chest, position, rotation, 180f, ready: false);
        Check(!RewardShowcasePlacement.TryReveal(panel) && grab.Placements == 0,
            "unsettled elected source pose cannot release follower first reveal");
        RewardShowcasePlacement.SetInitialPose(chest, position, rotation, 180f, ready: true);
        ModalFallback.PlacementGrab = null;
        Check(!RewardShowcasePlacement.TryReveal(panel), "follower waits until its native grab can accept the elected pose");
        ModalFallback.PlacementGrab = grab;
        Check(RewardShowcasePlacement.TryReveal(panel), "valid elected pose releases follower first reveal");
        Check(grab.Placements == 1 && grab.Position == position && grab.Rotation == rotation
            && grab.GrabRoot.localScale == new Vector3(1.8f, 1.8f, 1.8f),
            "follower adopts elected pose and size before it may reveal");
        Check(ModalFallback.PreservedPoses == 1, "imported reveal pose retires the fallback local anchor");
        Check(RewardShowcasePlacement.LocalRevealPending, "native pending reveal is exposed for readiness synchronization");
        panel.RevealPending = false;
        Check(!RewardShowcasePlacement.LocalRevealPending, "completed native reveal clears transported pending state");

        RewardShowcasePlacement.ClearInitialPose();
        Check(!RewardShowcasePlacement.TryReveal(panel), "clearing mailbox does not invent follower placement authority");
        RewardShowcasePlacement.SetInitialAuthority(chest, true);
        Check(RewardShowcasePlacement.TryReveal(panel), "elected source promotion can reveal without a peer mailbox");
        RewardShowcasePlacement.SetInitialPose(chest, position, rotation, 180f, ready: false);
        Check(RewardShowcasePlacement.TryReveal(panel), "elected source does not wait for its own first-reveal readiness");
        Check(grab.Placements == 1, "promotion itself does not reposition a native holder");
        RewardShowcasePlacement.SetInitialAuthority(chest, false);
        RewardShowcasePlacement.ClearInitialAuthority();
        Check(RewardShowcasePlacement.TryReveal(panel), "network teardown clears authority wait for native standalone continuation");
        RewardShowcasePlacement.SetInitialAuthority(RewardShowcaseIdentity.HashChestGuid("OtherChest"), false);
        Check(RewardShowcasePlacement.TryReveal(panel), "authority for a different chest cannot block current presentation");
        RewardShowcasePlacement.SetInitialAuthority(chest, false);
        SharedWindows.Participating = false;
        Check(RewardShowcasePlacement.TryReveal(panel), "local-only settings preserve ordinary reward reveal");
        SharedWindows.Participating = true;
        Choreographer.s_Choreographer.m_BlockClientMessageProcessing = false;
        Check(RewardShowcasePlacement.TryReveal(panel), "unknown current chest identity cannot create a permanent wait");
        OpenChest("SharedChest");
        Check(!RewardShowcasePlacement.LocalPlacementFailed, "pending placement is not falsely reported as a local failure");
        ModalFallback.Failed.Add(guild.Window);
        Check(RewardShowcasePlacement.LocalPlacementFailed, "actual local conversion failure is available for source re-election");
        guild.Scenario.Shown = false;
        Check(!RewardShowcasePlacement.LocalPlacementFailed && RewardShowcasePlacement.TryReveal(panel),
            "closed native reward drops failure identity and does not obstruct other reveals");
    }

    private static void NativeConversionAvailability()
    {
        var guild = new Guild();
        Check(!RewardShowcasePlacement.LocalPlacementFailed, "ordinary floating reward source remains available");
        FlatScreen.ManualScreenActive = true;
        Check(RewardShowcasePlacement.LocalPlacementFailed, "manual desktop reward source is unavailable for shared floating placement");
        FlatScreen.ManualScreenActive = false;
        Check(!RewardShowcasePlacement.LocalPlacementFailed, "leaving manual desktop restores source eligibility immediately");
        WorldUIConfig.ModalWindowStyle = false;
        Check(RewardShowcasePlacement.LocalPlacementFailed, "screen-style reward source cannot promise a shared floating pose");
        WorldUIConfig.ModalWindowStyle = true;
        WorldUIConfig.ConversionActive = false;
        Check(RewardShowcasePlacement.LocalPlacementFailed, "disabled conversion cannot become shared reward placement source");
        WorldUIConfig.ConversionActive = true;
        var unrelated = Component<UIWindow>("Failed unrelated float");
        ModalFallback.Failed.Add(unrelated);
        Check(!RewardShowcasePlacement.LocalPlacementFailed, "unrelated conversion failure does not reject the native reward source");
        ModalFallback.Failed.Add(guild.Window);
        Check(RewardShowcasePlacement.LocalPlacementFailed, "native reward conversion failure rejects this exact source");
        ModalFallback.Failed.Clear();
        Check(!RewardShowcasePlacement.LocalPlacementFailed, "native conversion recovery restores source eligibility");
        guild.Scenario.Shown = false;
        WorldUIConfig.ModalWindowStyle = false;
        FlatScreen.ManualScreenActive = true;
        Check(!RewardShowcasePlacement.LocalPlacementFailed, "closed reward is not reported unavailable by screen configuration alone");
    }

    static void Main()
    {
        CampaignButtonRepair();
        CampaignGates();
        GuildInputAndLifecycle();
        NativeRewardCompletion();
        ContinueHoverAndClick();
        GuildGatesAndAuthority();
        IdentityAndInitialPlacement();
        SharedRevealHandoff();
        NativeConversionAvailability();
        Reset();
        Check(RewardShowcase.Window == null && !RewardShowcase.TryConfirm(), "clean shutdown leaves no actionable reward surface");
        Console.WriteLine($"Reward showcase: {_assertions} assertions passed.");
    }
}
