using System;
using System.Collections.Generic;
using System.Linq;

// Controlled substitutes model native ownership, event identity and deferred input.
// The test executes the production bridge; no game assemblies or gameplay bodies ship here.
namespace UnityEngine
{
    public readonly record struct Color(float r, float g, float b, float a);
    public sealed class Sprite : Object { }
    public class Object
    {
        public bool Destroyed;
        public static implicit operator bool(Object? value) => value is not null && !value.Destroyed;
        public static void Destroy(Object value) => value.Destroyed = true;
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component =>
            gameObject.GetComponentInChildren<T>(includeInactive);
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : Component =>
            gameObject.GetComponentsInChildren(includeInactive, results);
    }
    public class Behaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component
    {
        public Vector3 localScale;
        public Transform? parent;
        public readonly List<Transform> Children = new();
        public void SetParent(Transform? value, bool preserve = false)
        {
            parent?.Children.Remove(this);
            parent = value;
            parent?.Children.Add(this);
        }
        public bool IsChildOf(Transform value) => this == value || (parent?.IsChildOf(value) ?? false);
    }
    public class GameObject : Object
    {
        public string name;
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public readonly Transform transform;
        public readonly List<Component> Components = new();
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObject = this };
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var value = new T { gameObject = this };
            Components.Add(value);
            return value;
        }
        public T? GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault();
        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : Component
        {
            if (!includeInactive && !activeInHierarchy) return null;
            return GetComponent<T>() ?? transform.Children.Select(child => child.gameObject.GetComponentInChildren<T>(includeInactive)).FirstOrDefault(value => value != null);
        }
        public void SetActive(bool value) => activeSelf = value;
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : Component
        {
            if (!includeInactive && !activeInHierarchy) return;
            results.AddRange(Components.OfType<T>());
            foreach (Transform child in transform.Children)
                child.gameObject.GetComponentsInChildren(includeInactive, results);
        }
    }
    public static class Time { public static int frameCount; public static float unscaledTime; }
    public readonly record struct Vector3(float x, float y, float z)
    {
        public static Vector3 one => new(1, 1, 1);
        public static Vector3 operator *(Vector3 value, float scale) => new(value.x * scale, value.y * scale, value.z * scale);
    }
    public readonly record struct Quaternion(float x, float y, float z, float w);
}
namespace UnityEngine.EventSystems
{
    public sealed class PointerEventData
    {
        public enum InputButton { Left }
        public InputButton button;
        public PointerEventData(object? system = null) { }
    }
    public static class EventSystem { public static object? current; }
    public static class ExecuteEvents
    {
        public static object pointerClickHandler = new();
        public static void Execute(UnityEngine.GameObject target, PointerEventData data, object handler) =>
            target.GetComponent<UnityEngine.UI.Button>()!.OnPointerClick();
    }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public class UnityEvent
    {
        private readonly List<UnityAction> _listeners = new();
        public int ListenerCount => _listeners.Count;
        public void AddListener(UnityAction listener) => _listeners.Add(listener);
        public void RemoveListener(UnityAction listener) => _listeners.RemoveAll(item => item == listener);
        public void Invoke() { foreach (var listener in _listeners.ToArray()) listener(); }
    }
}
namespace UnityEngine.UI
{
    public class UIWindow : UnityEngine.MonoBehaviour
    {
        public bool IsOpen = true;
        public bool IsVisible = true;
        public int ID;
        public Action? NativeHide;
        public void Hide()
        {
            if (NativeHide == null) throw new Exception("Presentation must not hide the native reward window");
            IsOpen = IsVisible = false;
            NativeHide();
        }
    }
    public class Selectable : UnityEngine.MonoBehaviour
    {
        public bool interactable = true;
        public bool AncestorsInteractable = true;
        public readonly UnityEngine.Events.UnityEvent onClick = new();
        public bool IsInteractable() => interactable && AncestorsInteractable;
    }
    public class Graphic : UnityEngine.MonoBehaviour { public UnityEngine.Color color; }
    public class Image : Graphic { public UnityEngine.Sprite? sprite; }
    public class Button : Selectable
    {
        protected enum SelectionState { Normal, Highlighted, Pressed, Selected, Disabled }
        protected SelectionState currentSelectionState;
        public Graphic? targetGraphic;
        protected virtual void DoStateTransition(SelectionState state, bool instant) { }
        private void Change(SelectionState state)
        {
            currentSelectionState = IsInteractable() ? state : SelectionState.Disabled;
            DoStateTransition(currentSelectionState, false);
        }
        public virtual void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data) => Change(SelectionState.Highlighted);
        public void OnPointerExit() => Change(SelectionState.Normal);
        public void OnPointerDown() => Change(SelectionState.Pressed);
        public void OnPointerUp() => Change(SelectionState.Highlighted);
        public void OnPointerClick() { if (IsInteractable()) onClick.Invoke(); }
    }
}
public class ExtendedButton : UnityEngine.UI.Button { }
public class Singleton<T> : UnityEngine.MonoBehaviour where T : class
{
    public static T Instance = null!;
    public static bool IsInitialized => Instance != null;
}
public abstract class ScenarioRewardManager : Singleton<ScenarioRewardManager>
{
    public bool Shown = true;
    public bool IsShown => Shown;
}
public sealed class CampaignScenarioRewardManager : ScenarioRewardManager
{
    public CampaignRewardsManager manager = null!;
    public CampaignRewardsManager Manager { get => manager; set => manager = value; }
}
public sealed class GuildmasterScenarioRewardManager : ScenarioRewardManager { }
public sealed class CampaignRewardsManager : Singleton<CampaignRewardsManager>
{
    public UIIntroductionRewardsProcess introductionProcess = new();
    public UICampaignRewardWindow rewardsWindow = null!;
    public UICampaignRewardWindow RewardsWindow { get => rewardsWindow; set => rewardsWindow = value; }
}
public sealed class UICampaignRewardWindow : UnityEngine.MonoBehaviour
{
    public ExtendedButton continueButton = null!;
    public bool isRevealing;
    public Action? continueAction;
    public ExtendedButton ContinueButton { get => continueButton; set => continueButton = value; }
    public bool Revealing { get => isRevealing; set => isRevealing = value; }
    public Action? ContinueAction { get => continueAction; set => continueAction = value; }
    public UnityEngine.UI.UIWindow window = null!;
    public int NativeCalls;
    public void OnContinueButtonClick()
    {
        if (!GloomhavenVR.WorldUI.PostQuestRewardSync.BeforeCampaignContinue(this, out object? opening)) return;
        try { NativeCalls++; continueAction?.Invoke(); GloomhavenVR.WorldUI.PostQuestRewardSync.NativeSucceeded(opening); }
        catch { GloomhavenVR.WorldUI.PostQuestRewardSync.NativeFailed(opening); throw; }
    }
    public void WireNativeMouseListener() => continueButton.onClick.AddListener(OnContinueButtonClick);
    public void Hide() => throw new Exception("Presentation must not hide campaign rewards");
}
public sealed partial class UIRewardsManager : Singleton<UIRewardsManager>
{
    private bool processingRewards = true;
    public bool networkProcessIfServer = true;
    public Func<bool>? interactionChecker;
    private bool _inputLatch;
    public bool isConfirmPressed { get => _inputLatch; set { _inputLatch = value; if (value) NativeCalls++; } }
    public UnityEngine.UI.UIWindow myWindow = null!;
    public bool ProcessingRewards { get => processingRewards; set => processingRewards = value; }
    public bool NetworkProcess { get => networkProcessIfServer; set => networkProcessIfServer = value; }
    public Func<bool>? InteractionChecker { get => interactionChecker; set => interactionChecker = value; }
    public bool PendingInput => isConfirmPressed;
    public UnityEngine.UI.UIWindow Window { get => myWindow; set => myWindow = value; }
    public bool IsShown => myWindow.IsOpen;
    public int NativeCalls;
    public int ConsumedInputs;
    public bool IsGuildmasterMode = true;
    public void ConfirmPressed()
    {
        // Native UIRewardsManager -> LongPressHandlerBase requires a real gamepad edge
        // outside Guildmaster. A VR uGUI click does not satisfy it.
        if (IsGuildmasterMode) isConfirmPressed = true;
    }
    public void NativeConsumeInput() { if (isConfirmPressed) ConsumedInputs++; isConfirmPressed = false; }
    public void MoveToNextReward() => throw new Exception("Presentation must not bypass native reward input");
    public int CompletedProcesses;
    public Action? onProcessEnded;
    private bool _nativeProcessStep;
    private void EndProcess()
    {
        if (!_nativeProcessStep) throw new Exception("Presentation must not end native reward processing");
        processingRewards = false;
        myWindow.IsOpen = myWindow.IsVisible = false;
        CompletedProcesses++;
        onProcessEnded?.Invoke();
    }
}
public sealed class ESCMenu : Singleton<ESCMenu>
{
    public bool IsOpen;
    public Action? BeforeMainMenuLoadingStarted;
    public Action<bool>? EscMenuStateChanged;
}
public static class FFSNetwork { public static bool IsClient; public static bool IsOnline; public static bool IsHost => !IsClient; }
public sealed class Choreographer
{
    public static Choreographer s_Choreographer = null!;
    public bool m_BlockClientMessageProcessing;
    public object? LastMessage;
}
public static class ScenarioManager { public enum ObjectImportType { Chest, GoalChest, Door, Decoration } }
namespace ScenarioRuleLibrary
{
    public sealed class CProp
    {
        public ScenarioManager.ObjectImportType ObjectType;
        public string PropGuid = string.Empty;
    }
    public sealed class CActivateProp_MessageData { public CProp? m_Prop; }
}
namespace GloomhavenVR.Core
{
    internal static class TickGuard { }
    internal static class PerfMonitor { }
    internal interface IPanelGrabOwner { UnityEngine.Transform? GrabRoot { get; } }
    internal static class VRLog
    {
        internal static void Note(string category, string message) { }
        internal static void Info(string category, string message) { }
        internal static void Warn(string category, string message) { }
        internal static void Error(string category, string message) { }
        internal static void Alert(string category, string message) { }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal enum SharedWindowKind { RewardShowcase }
    internal static class SharedWindows
    {
        internal static bool Participating = true;
        internal static bool ParticipatesHere(SharedWindowKind kind) => Participating;
    }
    internal static class SharedWindowSizeLaw { internal static float SharedGrabFactor(float size) => size / 100f; }
    internal sealed class GrabbableModal : GloomhavenVR.Core.IPanelGrabOwner
    {
        public UnityEngine.Transform? GrabRoot { get; set; }
        internal UnityEngine.Vector3 Position;
        internal UnityEngine.Quaternion Rotation;
        internal int Placements;
        internal void PlaceFrameAt(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation)
        { Position = position; Rotation = rotation; Placements++; }
    }
    internal sealed class ConvertedPanel { internal bool RevealPending = true; }
    internal static partial class ModalFallback
    {
        internal static int SpentAnchors;
        internal static UnityEngine.UI.UIWindow? PolledWindow;
        private static bool _rewardShowcaseOpen;
        internal static UnityEngine.UI.UIWindow? PlacementWindow;
        internal static ConvertedPanel? PlacementPanel;
        internal static GrabbableModal? PlacementGrab;
        internal static readonly HashSet<UnityEngine.UI.UIWindow> Failed = new();
        internal static int PreservedPoses;
        internal static ConvertedPanel? PanelFor(UnityEngine.UI.UIWindow? window) => window != null && ReferenceEquals(window, PlacementWindow) ? PlacementPanel : null;
        internal static bool TryGetGrabFor(UnityEngine.UI.UIWindow window, out GrabbableModal? grab)
        {
            grab = ReferenceEquals(window, PlacementWindow) ? PlacementGrab : null;
            return grab != null;
        }
        internal static void PreserveRewardInitialPose(UnityEngine.UI.UIWindow window)
        {
            if (!ReferenceEquals(window, PlacementWindow)) throw new Exception("Preserved foreign reward pose");
            PreservedPoses++;
        }
        internal static void NoteSharedAnchorSpent(SharedWindowKind kind, string reason) => SpentAnchors++;
        internal static void PollRewardsForTest(bool inScenario) { PolledWindow = null; AddRewardShowcaseWindow(inScenario); }
        internal static void DismissForTest(UnityEngine.UI.UIWindow window) => DismissTransient(window);
        private static void AddPollWindow(UnityEngine.UI.UIWindow? window) => PolledWindow = window;
        private static void LogPollTransition(ref bool previous, bool current, string description) => previous = current;
    }
    internal static class WorldUIConfig
    {
        internal static bool ConversionActive = true;
        internal static bool ModalWindowStyle = true;
    }
    internal static class FlatScreen { internal static bool ManualScreenActive; }
    internal static class NativeButtonSkin
    {
        internal enum FaceState { Idle, Accent, Pressed, Disabled }
        private static readonly UnityEngine.Sprite[] Sprites = { new(), new(), new(), new() };
        internal static UnityEngine.Sprite SpriteFor(FaceState state) => Sprites[(int)state];
        internal static UnityEngine.Color ColorFor(FaceState state) => new((int)state, 1, 1, 1);
    }
    internal sealed class RewardShowcaseButton
    {
        internal static UIRewardsManager? Owner;
        internal static bool CanClick;
        internal static int Disposals;
        internal void Tick(UIRewardsManager owner, bool canConfirm) { Owner = owner; CanClick = canConfirm; }
        internal void Dispose() { Owner = null; CanClick = false; Disposals++; }
    }
}
