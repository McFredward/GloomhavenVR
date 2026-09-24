using System;
using System.Collections.Generic;
using GLOO.Introduction;
using GloomhavenVR.Net;
using GloomhavenVR.WorldUI;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary.YML;
using UnityEngine;
using UnityEngine.UI;

static partial class Program
{
    private static readonly List<Reward> PublicRewards = new() { new Reward { Type = 4, Amount = 2, ItemID = 12 } };
    private static void OpenPostQuest(Campaign campaign)
    {
        uint previous = PostQuestRewardSync.BeginQueuedRewards();
        PostQuestRewardSync.CampaignStarting(campaign.Manager);
        PostQuestRewardSync.CampaignShowing(campaign.Rewards, PublicRewards);
        PostQuestRewardSync.EndQueuedRewards(previous);
        PostQuestRewardSync.Tick(true);
    }
    private static MapStoryOpening[] FinishFromPeer(MapStoryOpening sample, MapStoryOpeningLedger peer, object subject)
    {
        peer.Open(subject, sample.SemanticKey, sample.ContentKey, sample.PageCount, new[] { 1 }, sample.Bidirectional);
        peer.Update(subject, 0, new[] { 1 });
        peer.Finish(subject);
        return peer.Sample(subject);
    }
    private static void PostQuestContinuations()
    {
        var campaign = new Campaign();
        AdventureState.MapState.AllQuestStates.Clear();
        AdventureState.MapState.AllQuestStates.Add(new Quest());
        OpenPostQuest(campaign);
        uint firstKey = PostQuestRewardSync.CurrentKey;
        Check(firstKey != 0 && RewardShowcase.ContentKey == firstKey, "postquest native campaign joins shared pose identity");
        Check(PostQuestRewardSync.CanConfirm, "postquest original Continue retains native eligibility");
        var peer = new MapStoryOpeningLedger();
        var finished = FinishFromPeer(PostQuestRewardSync.SampleCompletions()[0], peer, new object());
        campaign.Rewards.isRevealing = true;
        PostQuestRewardSync.ObserveCompletions(2, finished);
        PostQuestRewardSync.Tick(true);
        Check(campaign.Completions == 0, "remote postquest completion waits for native reward reveal");
        campaign.Rewards.isRevealing = false;
        campaign.Button.interactable = false;
        PostQuestRewardSync.Tick(true);
        Check(campaign.Completions == 0, "remote postquest completion honors disabled original button");
        campaign.Button.interactable = true;
        SharedWindows.Participating = false;
        PostQuestRewardSync.Tick(true);
        Check(campaign.Completions == 0, "reward completion cannot drive an opted-out participant");
        SharedWindows.Participating = true;
        PostQuestRewardSync.Tick(true);
        Check(campaign.Completions == 1, "one remote postquest Continue invokes original native callback");
        PostQuestRewardSync.ObserveCompletions(2, finished);
        PostQuestRewardSync.Tick(true);
        Check(!PostQuestRewardSync.TryConfirm() && campaign.Completions == 1, "repeated remote and local postquest input cannot double callback");

        OpenPostQuest(campaign);
        Check(PostQuestRewardSync.CurrentKey == firstKey, "same public group has same semantic identity");
        PostQuestRewardSync.ObserveCompletions(2, finished);
        PostQuestRewardSync.Tick(true);
        Check(campaign.Completions == 1, "old completion cannot close repeated native opening with same group");
        finished = FinishFromPeer(PostQuestRewardSync.SampleCompletions()[0], peer, new object());
        PostQuestRewardSync.ObserveCompletions(2, finished);
        PostQuestRewardSync.Tick(true);
        Check(campaign.Completions == 2, "new peer occurrence confirms only matching repeated opening");
        AdventureState.MapState.AllQuestStates[0].ScenarioState.MatchSessionID = "second-run";
        OpenPostQuest(campaign);
        Check(PostQuestRewardSync.CurrentKey != firstKey, "repeating the same quest on a new native run changes reward identity");
        campaign.Rewards.ContinueAction = () => throw new InvalidOperationException("native reward failure");
        try { PostQuestRewardSync.TryConfirm(); Check(false, "native failure must propagate"); }
        catch (InvalidOperationException) { }
        Check(!PostQuestRewardSync.SampleCompletions()[0].Finished, "failed native callback never publishes completion");
        campaign.Rewards.ContinueAction = () => campaign.Completions++;
        Check(PostQuestRewardSync.TryConfirm(), "failed native callback releases only the input reservation");

        // Original Guildmaster Hide must own its complete unlock-video chain and final save callback.
        Reset();
        var guild = Component<UIGuildmasterAdventureRewardsManager>("Native map rewards");
        guild.window = guild.gameObject.AddComponent<UIWindow>();
        guild.closeButton = Component<ExtendedButton>("Original map Continue");
        Singleton<UIAdventureRewardsManager>.Instance = guild;
        int closed = 0;
        guild.window.NativeHide = () => closed++;
        guild.m_CharacterIDsUnlocked.AddRange(new[] { "A", "B" });
        uint scope = PostQuestRewardSync.BeginQueuedRewards();
        PostQuestRewardSync.GuildmasterShowing(guild, PublicRewards);
        PostQuestRewardSync.EndQueuedRewards(scope);
        RewardShowcase.Tick(true);
        Check(ReferenceEquals(RewardShowcase.Window, guild.window), "postquest Guildmaster preserves original reward window identity");
        guild.closeButton.onClick.Invoke();
        guild.closeButton.onClick.Invoke();
        Check(VideoCamera.s_This.Played.Count == 1 && closed == 0, "duplicate Guildmaster Continue cannot skip native unlock video");
        VideoCamera.s_This.Finish();
        Check(VideoCamera.s_This.Played.Count == 2 && closed == 0, "native first video callback retains second unlock video");
        VideoCamera.s_This.Finish();
        Check(closed == 1, "native last video alone invokes Guildmaster close callback");

        // The peer's terminal request stays retryable if the same native callback fails.
        campaign = new Campaign();
        OpenPostQuest(campaign);
        peer = new MapStoryOpeningLedger();
        finished = FinishFromPeer(PostQuestRewardSync.SampleCompletions()[0], peer, new object());
        PostQuestRewardSync.ObserveCompletions(2, finished);
        campaign.Rewards.ContinueAction = () => throw new InvalidOperationException("native peer dispatch failure");
        try { PostQuestRewardSync.Tick(true); Check(false, "native peer failure must propagate"); }
        catch (InvalidOperationException) { }
        Check(!PostQuestRewardSync.SampleCompletions()[0].Finished, "failed peer dispatch cannot become reward completion");
        campaign.Rewards.ContinueAction = () => campaign.Completions++;
        PostQuestRewardSync.Tick(true);
        Check(campaign.Completions == 1, "same native opening retries a failed peer terminal request");

        var nativeMap = AdventureState.MapState;
        AdventureState.MapState = new MapState { AllQuestStates = null! };
        uint failedScope = PostQuestRewardSync.BeginQueuedRewards();
        PostQuestRewardSync.CampaignStarting(campaign.Manager);
        PostQuestRewardSync.CampaignShowing(campaign.Rewards, PublicRewards);
        PostQuestRewardSync.EndQueuedRewards(failedScope);
        Check(PostQuestRewardSync.CurrentKey == 0, "failed provenance capture falls back to native rewards without an invented identity");
        AdventureState.MapState = nativeMap;
        PostQuestPoseBinding();
        PostQuestIntroduction();
        PostQuestIntroductionPagesAndChain();
        var bytes = new byte[256]; int offset = 0;
        Check(RewardContinuationCodec.Write(bytes, ref offset, finished) && bytes[0] == 84,
            "reward continuation uses only additive record84");
        Check(RewardContinuationCodec.TryRead(bytes, 2, offset - 2, out var decoded)
            && decoded.Length == finished.Length && decoded[0].Finished, "reward opening codec roundtrips durable completion");
        Check(!RewardContinuationCodec.TryRead(bytes, 2, offset - 3, out _), "reward opening codec rejects truncated payload");
    }
    private static void PostQuestPoseBinding()
    {
        var campaign = new Campaign();
        OpenPostQuest(campaign);
        uint key = PostQuestRewardSync.CurrentKey;
        var peer = new MapStoryOpeningLedger();
        var peerOpening = new object();
        peer.Open(peerOpening, key, key, 1, new[] { 1 });
        var original = peer.Sample(peerOpening);
        PostQuestRewardSync.ObserveCompletions(2, original);
        Check(PostQuestRewardSync.MatchesPose(2, 1, key, original), "coherent reward snapshot binds the exact live native opening pose");
        Check(!PostQuestRewardSync.MatchesPose(2, 1, key, null), "reward pose needs its opening in the same snapshot");
        peer.Finish(peerOpening);
        var completed = peer.Sample(peerOpening);
        PostQuestRewardSync.ObserveCompletions(2, completed);
        Check(!PostQuestRewardSync.MatchesPose(2, 1, key, completed), "finished remote reward cannot publish a new pose claim");
        PostQuestRewardSync.Tick(true);
        OpenPostQuest(campaign);
        Check(!PostQuestRewardSync.MatchesPose(2, 1, key, original), "stale pose cannot bind repeated native reward group");
        peerOpening = new object();
        peer.Open(peerOpening, key, key, 1, new[] { 1 });
        var successor = peer.Sample(peerOpening);
        PostQuestRewardSync.ObserveCompletions(2, successor);
        Check(PostQuestRewardSync.MatchesPose(2, 1, key, successor), "new peer occurrence binds repeated native reward group pose");
    }
    private static void PostQuestIntroduction()
    {
        var campaign = new Campaign();
        OpenPostQuest(campaign);
        var manager = Component<UIIntroductionManager>("Introduction");
        Singleton<UIIntroductionManager>.Instance = manager;
        var info = new UIIntroductionManager.MessageInfo();
        int dismissed = 0;
        info.OnClosedPressedAction = () => { dismissed++; manager.m_CurrentlyDisplayedMessageInfo = null; };
        uint prior = PostQuestRewardSync.BeginIntroductionScope(campaign.Manager.introductionProcess);
        PostQuestRewardSync.CaptureIntroduction(info);
        PostQuestRewardSync.EndIntroductionScope(prior);
        PostQuestRewardSync.ShowIntroduction(info);
        manager.m_CurrentlyDisplayedMessageInfo = info;
        var layout = Component<LevelMessageUILayout>("Native reward hint");
        layout.closeButton = Component<ExtendedButton>("Native hint Continue");
        layout.OnClosed = info.OnClosedPressedAction;
        layout.controllerArea = new ControllerArea { IsFocused = true };
        layout._focusedFrame = Time.frameCount;
        manager.LayoutGroup._currentMessage = layout;
        Check(!PostQuestRewardSync.CanConfirm, "original reward cannot close through its still-active introduction");
        var peer = new MapStoryOpeningLedger();
        var done = FinishFromPeer(PostQuestRewardSync.SampleCompletions()[0], peer, new object());
        PostQuestRewardSync.ObserveCompletions(2, done);
        PostQuestRewardSync.Tick(true);
        Check(dismissed == 0, "remote hint honors native newly-focused input guard");
        Time.frameCount += 2;
        PostQuestRewardSync.Tick(true);
        Check(dismissed == 1 && PostQuestRewardSync.CanConfirm, "remote hint uses native button callback before exposing reward continuation");
        PostQuestRewardSync.ObserveCompletions(2, done);
        PostQuestRewardSync.Tick(true);
        Check(dismissed == 1 && campaign.Completions == 0, "hint completion never advances the separate reward callback");
    }
    private static void PostQuestIntroductionPagesAndChain()
    {
        var campaign = new Campaign();
        OpenPostQuest(campaign);
        var manager = Component<UIIntroductionManager>("Introduction pages");
        Singleton<UIIntroductionManager>.Instance = manager;
        manager.LayoutGroup.window = Component<UIWindow>("Native introduction window");
        var info = new UIIntroductionManager.MessageInfo();
        info.Message.Pages.Add(new Page { PageTextKey = "second reward help page" });
        var successor = new UIIntroductionManager.MessageInfo { ID = "next-highlight-step" };
        int firstDone = 0, nextDone = 0;
        var layout = Component<LevelMessageUILayout>("Original multipage hint");
        layout.closeButton = Component<ExtendedButton>("Original hint Continue");
        layout.pagination.pages.Add(2);
        manager.LayoutGroup._currentMessage = layout;
        successor.OnClosedPressedAction = () => { nextDone++; manager.m_CurrentlyDisplayedMessageInfo = null; };
        info.OnClosedPressedAction = () =>
        {
            firstDone++;
            // Same native promise shape as UIIntroduceProcessHighlight: next AddMessage
            // runs after the outer Process(EIntroductionConcept) scope has already ended.
            PostQuestRewardSync.CaptureIntroduction(successor);
            PostQuestRewardSync.ShowIntroduction(successor);
            manager.m_CurrentlyDisplayedMessageInfo = successor;
            layout.pagination.pages.RemoveAt(1);
            layout.pagination.OpenPage(1);
            layout.OnClosed = successor.OnClosedPressedAction;
        };
        uint prior = PostQuestRewardSync.BeginIntroductionScope(campaign.Manager.introductionProcess);
        PostQuestRewardSync.CaptureIntroduction(info);
        PostQuestRewardSync.EndIntroductionScope(prior);
        PostQuestRewardSync.ShowIntroduction(info);
        manager.m_CurrentlyDisplayedMessageInfo = info;
        layout.OnClosed = info.OnClosedPressedAction;
        var local = PostQuestRewardSync.SampleCompletions()[0];
        var peer = new MapStoryOpeningLedger();
        object peerHint = new();
        peer.Open(peerHint, local.SemanticKey, local.ContentKey, local.PageCount, new[] { 1 }, bidirectional: true);
        peer.Update(peerHint, 1, new[] { 1 });
        PostQuestRewardSync.ObserveCompletions(2, peer.Sample(peerHint));
        PostQuestRewardSync.Tick(true);
        Check(layout.pagination.currentPage == 2, "native reward introduction next page mirrors through pagination");
        layout.pagination.OpenPage(1); // Local previous-page click before the next 5 Hz snapshot.
        PostQuestRewardSync.Tick(true);
        Check(layout.pagination.currentPage == 1, "local reward hint previous page survives Tick before Sample");
        PostQuestRewardSync.SampleCompletions();
        peer.Update(peerHint, 0, new[] { 1 });
        PostQuestRewardSync.ObserveCompletions(2, peer.Sample(peerHint));
        PostQuestRewardSync.Tick(true);
        Check(layout.pagination.currentPage == 1, "native reward introduction previous page remains shared");
        peer.Finish(peerHint);
        PostQuestRewardSync.ObserveCompletions(2, peer.Sample(peerHint));
        PostQuestRewardSync.Tick(true);
        Check(firstDone == 1 && nextDone == 0 && !PostQuestRewardSync.CanConfirm,
            "highlight continuation preserves producer scope for queued successor hint");
        var successorState = PostQuestRewardSync.SampleCompletions()[0];
        Check(successorState.ContentKey != local.ContentKey && successorState.PageCount == 1,
            "queued highlight successor gets its own native shared opening");
        object peerNext = new();
        peer.Open(peerNext, successorState.SemanticKey, successorState.ContentKey, 1, new[] { 1 }, bidirectional: true);
        peer.Update(peerNext, 0, new[] { 1 });
        peer.Finish(peerNext);
        PostQuestRewardSync.ObserveCompletions(2, peer.Sample(peerNext));
        PostQuestRewardSync.Tick(true);
        Check(nextDone == 1 && PostQuestRewardSync.CanConfirm, "queued highlight native close releases reward eligibility");

        // Disabling conversion must return the original map button to native flat input.
        Reset();
        var guild = Component<UIGuildmasterAdventureRewardsManager>("Flat original rewards");
        guild.window = guild.gameObject.AddComponent<UIWindow>();
        guild.closeButton = Component<ExtendedButton>("Flat original Continue");
        int nativeClosed = 0;
        guild.window.NativeHide = () => nativeClosed++;
        Singleton<UIAdventureRewardsManager>.Instance = guild;
        RewardShowcase.Tick(true);
        RewardShowcase.Tick(false);
        guild.closeButton.onClick.Invoke();
        Check(nativeClosed == 1, "disabling conversion restores native flat reward button callback");
    }

}
