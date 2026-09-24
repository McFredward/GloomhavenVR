using System;
using System.Collections.Generic;

namespace UnityEngine { public sealed class CanvasGroup { public bool interactable; } }
namespace ScenarioRuleLibrary.YML
{
    public sealed class Reward
    {
        public int Type, Amount, ItemID;
        public string CharacterID = "NoneID", UnlockName = "";
        public bool LevelUp;
    }
}
namespace MapRuleLibrary.Adventure
{
    public static class AdventureState { public static MapState MapState = new(); }
    public sealed class MapState { public int Seed = 123; public List<Quest> AllQuestStates = new(); }
    public sealed class Quest { public string ID = "quest"; public Scenario ScenarioState = new(); }
    public sealed class Scenario { public string MatchSessionID = "first-run"; }
}
namespace GloomhavenVR.Net
{
    internal static class NetAvatarDriver
    {
        internal static int[] Peers = { 2 };
        internal static void CollectRewardPosePeers(List<int> into) { into.Clear(); into.AddRange(Peers); }
    }
    internal static class VersionGuard
    {
        internal static void CollectContinuationPeers(List<int> into, int localId) => NetAvatarDriver.CollectRewardPosePeers(into);
    }
    internal static class NetPlayerActors { internal static int LocalId = 1; internal static int LocalPlayerId() => LocalId; }
}
public sealed class UIIntroductionRewardsProcess { }
namespace GLOO.Introduction
{
    public sealed class UIIntroductionManager : Singleton<UIIntroductionManager>
    {
        public sealed class MessageInfo
        {
            public Action OnClosedPressedAction = () => { };
            public Message Message = new();
            public string ID = "reward-introduction";
        }
        public MessageInfo? m_CurrentlyDisplayedMessageInfo;
        public LevelMessageUILayoutGroup LayoutGroup = new();
    }
    public sealed class Message
    {
        public Dismiss DismissTrigger = new();
        public string TitleKey = "reward-introduction";
        public List<Page> Pages = new() { new Page() };
    }
    public sealed class Dismiss { public bool IsTriggeredByDismiss = true; }
    public sealed class Page { public string PageTextKey = "reward-help"; }
}
public sealed class LevelMessageUILayoutGroup { public UnityEngine.UI.UIWindow? window; public LevelMessageUILayout? _currentMessage; }
public sealed class ControllerArea { public bool IsFocused; }
public sealed class LevelMessageUILayout : UnityEngine.MonoBehaviour
{
    public PaginationHandler pagination = new();
    public ExtendedButton closeButton = null!;
    public ControllerArea? controllerArea;
    public int _focusedFrame;
    public Action OnClosed = () => { };
    public void CloseButtonPressed()
    {
        if (!gameObject.activeSelf || (controllerArea != null && controllerArea.IsFocused
            && UnityEngine.Time.frameCount - _focusedFrame < 2)) return;
        if (pagination.currentPage < pagination.pages.Count) { pagination.OpenPage(pagination.currentPage + 1); return; }
        OnClosed();
    }
}
public sealed class PaginationHandler
{
    public List<int> pages = new() { 1 };
    public int currentPage = 1;
    public void OpenPage(int page) => currentPage = Math.Clamp(page, 1, Math.Max(1, pages.Count));
}
