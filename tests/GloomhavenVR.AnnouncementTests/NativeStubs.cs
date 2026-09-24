using System;
using System.Collections;
using System.Collections.Generic;

// Boundary stubs contain no VR continuation decisions. The native reveal methods in
// NativeLevelUpProcess.cs are copied verbatim and checked against the read-only game reference.
namespace UnityEngine
{
    public static class Time { public static int frameCount; }
    public class GameObject
    {
        public bool activeInHierarchy = true;
        public void SetActive(bool value) => activeInHierarchy = value;
    }
}
namespace UnityEngine.UI
{
    public sealed class UIWindow { public bool isActiveAndEnabled = true, IsOpen = true; }
}
public sealed class Singleton<T> where T : class
{
    public static T Instance = null!;
    public static bool IsInitialized => Instance != null;
}
public static class FFSNetwork { public static bool IsOnline; }
public sealed class ESCMenu { public bool IsOpen; }
public sealed class CharacterService { public bool IsUnderMyControl = true; }
public sealed partial class ClickTrackerExtended
{
    public bool enabled = true, SkipNextClick;
    public UnityEngine.GameObject gameObject = new();
    public NativeClickEvent onClick = new();
    public string audioItemClick = "native-click";
    public int Clicks => onClick.Invocations;
}
public sealed class NativeClickEvent
{
    public Action Callback = delegate { };
    public int Invocations;
    public void Invoke() { Invocations++; Callback(); }
}
public static class Debug { public static void LogError(string text) => throw new Exception(text); }
public sealed class TestCard { }
public static class ListExtensions
{
    public static bool IsNullOrEmpty<T>(this List<T> list) => list == null || list.Count == 0;
}
public sealed class CardHighlighter
{
    public Action? HighlightFinished, UnhighlightFinished;
    public void HighlightWonCard(TestCard card, Action callback) => HighlightFinished = callback;
    public void UnhighlightWonCard(Action callback) => UnhighlightFinished = callback;
    public void FinishHighlight() { Action? action = HighlightFinished; HighlightFinished = null; action?.Invoke(); }
    public void FinishUnhighlight() { Action? action = UnhighlightFinished; UnhighlightFinished = null; action?.Invoke(); }
}
public sealed class Inventory
{
    public int Added;
    public bool Interactive;
    public void AddNewCard(TestCard card) => Added++;
    public void EnableInteraction(bool enabled) => Interactive = enabled;
}
public sealed class ControllerArea { public bool IsFocused; public void Focus() { } }
public static class AudioControllerUtils { public static void PlaySound(string id) { } }
public sealed class Text { public string text = ""; public UnityEngine.GameObject gameObject = new(); }
public static class LocalizationManager { public static string GetTranslation(string key) => "{0}"; }
public enum CampaignMapStateTag { LevelUp }
public sealed class UINavigation
{
    public StateMachine StateMachine = new();
}
public sealed class StateMachine { public void Enter(CampaignMapStateTag tag) { } }
public partial class UILevelUpWindow
{
    public bool isActiveAndEnabled = true, IsShowing = true;
    public bool isPlayingOpenAnimation, isOpenConfirmationBox, enableTracker;
    public UnityEngine.UI.UIWindow myWindow = new();
    public ClickTrackerExtended nextCardTracker = new();
    public CharacterService character = new();
    public CardHighlighter cardHolder = new();
    public Inventory inventory = new();
    public ControllerArea controllerArea = new();
    public UnityEngine.GameObject nextCardControllerTip = new();
    public List<TestCard> cardsAddedInLevel = new() { new(), new() };
    public int currentCard;
    public string audioItemShowNewCard = "native-audio";
    public Text cardsReceivedText = new();
    public static Action? FinishedShowCards;
    public UILevelUpWindow() => nextCardTracker.onClick.Callback = OnCardShown;
    public void Begin() => ShowCard();
    public void StartCoroutine(IEnumerator routine) { while (routine.MoveNext()) { } }
    private IEnumerator SkipAFrameAndSetIsShownToFalse() { IsShowing = false; yield break; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static bool ConversionActive = true; }
    internal static class FlatScreen { internal static bool ManualScreenActive; }
    internal sealed class AnnouncementContinueView
    {
        internal static int Ticks, Disposals;
        internal static bool Interactable;
        internal void Tick(UILevelUpWindow owner, bool canConfirm, Func<bool> onContinue)
        { Ticks++; Interactable = canConfirm; }
        internal void Dispose() { Disposals++; Interactable = false; }
    }
}
