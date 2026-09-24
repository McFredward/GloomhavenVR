using System;
using System.Collections.Generic;
using GLOO.Introduction;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class PostQuestRewardSync
{
    private sealed class Introduction
    {
        internal readonly UIIntroductionManager.MessageInfo Message;
        internal readonly uint Key, Scope;
        internal bool Consumed;
        internal Introduction(UIIntroductionManager.MessageInfo message, uint key, uint scope) { Message = message; Key = key; Scope = scope; }
    }
    private static readonly Dictionary<UIIntroductionManager.MessageInfo, Introduction> Introductions = new();
    private static uint _introductionScope;
    private static Introduction? _introduction;
    private static object? ActiveIntroductionSubject => IntroductionOpen ? _introduction!.Message : null;
    private static bool IntroductionOpen => _introduction != null && !_introduction.Consumed
        && Singleton<UIIntroductionManager>.IsInitialized
        && ReferenceEquals(Singleton<UIIntroductionManager>.Instance.m_CurrentlyDisplayedMessageInfo, _introduction.Message);

    internal static uint BeginIntroductionScope(UIIntroductionRewardsProcess process)
    {
        uint previous = _introductionScope;
        _introductionScope = (_campaign != null && ReferenceEquals(_campaign.introductionProcess, process)
            || _guild != null && ReferenceEquals(_guild.rewardIntroduction, process)) ? CurrentKey : 0;
        return previous;
    }
    internal static void EndIntroductionScope(uint previous) => _introductionScope = previous;
    internal static void CaptureIntroduction(UIIntroductionManager.MessageInfo message)
    {
        // Only informational introductions produced by this exact public reward process.
        // Character-owned level-up choices and unrelated tutorials never enter this ledger.
        if (_introductionScope == 0 || message.Message == null || message.OnClosedPressedAction == null
            || !message.Message.DismissTrigger.IsTriggeredByDismiss || Introductions.ContainsKey(message)) return;
        uint key = SharedMapRunIdentity.Add(_introductionScope, "reward-introduction");
        key = SharedMapRunIdentity.Add(key, message.ID);
        key = SharedMapRunIdentity.Add(key, message.Message.TitleKey);
        foreach (var page in message.Message.Pages) key = SharedMapRunIdentity.Add(key, page.PageTextKey);
        if (key == 0) key = 1;
        var entry = new Introduction(message, key, _introductionScope);
        Introductions.Add(message, entry);
        Action original = message.OnClosedPressedAction;
        message.OnClosedPressedAction = () =>
        {
            if (entry.Consumed) return;
            entry.Consumed = true;
            uint previous = _introductionScope;
            _introductionScope = entry.Scope;
            try { original(); } // Highlight-process promises can enqueue their next step here.
            catch
            {
                if (Singleton<UIIntroductionManager>.IsInitialized
                    && ReferenceEquals(Singleton<UIIntroductionManager>.Instance.m_CurrentlyDisplayedMessageInfo, message)
                    && Singleton<UIIntroductionManager>.Instance.LayoutGroup?.window?.IsOpen == true)
                { entry.Consumed = false; Ledger.RetryTerminal(message); }
                throw;
            }
            finally { _introductionScope = previous; }
            Ledger.Finish(message); // A failed native callback never becomes peer completion.
        };
    }
    internal static void ShowIntroduction(UIIntroductionManager.MessageInfo message)
    {
        _introduction = Introductions.TryGetValue(message, out Introduction? entry) ? entry : null;
        if (entry == null) return;
        int count = Math.Max(1, message.Message.Pages.Count);
        if (count > byte.MaxValue) return;
        Ledger.Open(message, entry.Key, entry.Key, (byte)count, Participants(), bidirectional: true);
    }
    private static LevelMessageUILayout? IntroductionLayout => IntroductionOpen
        ? Singleton<UIIntroductionManager>.Instance.LayoutGroup?._currentMessage : null;
    private static void UpdateIntroduction()
    {
        LevelMessageUILayout? layout = IntroductionLayout;
        if (layout?.pagination == null || _introduction == null) return;
        Ledger.Update(_introduction.Message, Math.Max(0, layout.pagination.currentPage - 1), Participants());
    }
    private static void TickIntroduction()
    {
        LevelMessageUILayout? layout = IntroductionLayout;
        if (layout == null || layout.pagination == null || !layout.gameObject.activeInHierarchy
            || layout.closeButton == null || !layout.closeButton.gameObject.activeInHierarchy
            || !layout.closeButton.enabled || !((Selectable)layout.closeButton).IsInteractable()
            || (layout.controllerArea != null && layout.controllerArea.IsFocused
                && Time.frameCount - layout._focusedFrame < 2)) return;
        int page = Math.Max(0, layout.pagination.currentPage - 1);
        int target = Ledger.Resolve(_introduction!.Message, NetPlayerActors.LocalPlayerId(), page, nativeOpen: true);
        if (target < 0) return;
        if (target >= Math.Max(1, layout.pagination.pages.Count))
        {
            // The native button's final-page path owns navigation, controller cleanup and
            // the saved callback. Do not invoke the queue's next-message function directly.
            layout.pagination.OpenPage(layout.pagination.pages.Count);
            layout.CloseButtonPressed();
        }
        else layout.pagination.OpenPage(target + 1);
    }
    private static void ResetIntroductions()
    {
        Introductions.Clear(); _introduction = null; _introductionScope = 0;
    }
}
