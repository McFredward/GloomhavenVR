using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal static class Object
    {
        internal static object? Found;
        internal static int Finds;
        internal static T? FindObjectOfType<T>() where T : class { Finds++; return Found as T; }
    }
    internal class Transform { }
    internal sealed class RectTransform : Transform { }
    internal static class Time { internal static float unscaledTime; }
    internal sealed class GameObject
    {
        internal bool activeSelf = true;
        internal GameObject? parent;
        internal bool activeInHierarchy => activeSelf && (parent?.activeInHierarchy ?? true);
        internal void SetActive(bool active) => activeSelf = active;
    }
}
namespace UnityEngine.SceneManagement
{
    internal readonly struct Scene { internal int handle => 1; }
    internal static class SceneManager { internal static Scene GetActiveScene() => new(); }
}
namespace UnityEngine.UI
{
    internal class Selectable { }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static int Errors;
        internal static int Warnings;
        internal static void Error(string area, string text) => Errors++;
        internal static void Warn(string area, string text) => Warnings++;
        internal static void Note(string area, string text) => Warnings++;
        internal static void Alert(string area, string text) => Errors++;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal class UIWindow
    {
        internal string name = "";
        internal bool Mandatory;
        internal object? Control;
        internal T? GetComponentInChildren<T>(bool includeInactive) where T : class =>
            includeInactive ? Control as T : null;
    }
    internal sealed class ClickTracker { }
    internal sealed class ClickTrackerExtended { }
    internal sealed class UIMainMenuOption
    {
        internal UnityEngine.GameObject gameObject = new();
        internal bool enabled = true;
        internal bool IsInteractable = true;
        internal bool Focused;
        internal bool Selected;
        internal bool IsSelected => Selected;
        internal string name = "VR Options";
        internal UnityEngine.Transform transform = new UnityEngine.RectTransform();
        private Action? _select, _deselect;
        internal void Init(Action select, Action deselect) { _select = select; _deselect = deselect; }
        internal void SetSelected(bool value) => Selected = value; // native SetValue suppresses callbacks
        internal void Deselect() { if (!Selected) return; SetSelected(false); _deselect?.Invoke(); }
        internal void Press() { if (Selected) Deselect(); else { SetSelected(true); _select?.Invoke(); } }
        internal void SetFocused(bool value) => Focused = value;
    }
    internal class MenuHost
    {
        internal UnityEngine.GameObject gameObject = new();
        internal bool IsOpen;
        internal UIMainMenuOption[] rows = Array.Empty<UIMainMenuOption>();
        internal void SetFocused(bool focused) { foreach (var row in rows) row.SetFocused(focused); }
    }
    internal sealed class ESCMenu : MenuHost { }
    internal sealed class UIMainOptionsMenu : MenuHost { }
    internal static class Singleton<T> where T : class
    {
        internal static T? Instance;
        internal static bool IsInitialized => Instance != null;
    }
    internal sealed class UIOptionsWindow { }
    internal sealed class UISubmenuGOWindow { internal UnityEngine.GameObject gameObject = new(); }
    internal static partial class VROptionsTab
    {
        internal static bool IsOpen;
        internal static bool CanOpen = true;
        private static UISubmenuGOWindow? _window;
        private static Action? _onHidden;
        internal static int Opens, Closes;
        internal static bool Open(Action closed, UnityEngine.RectTransform? anchor)
        {
            if (!CanOpen) return false;
            Opens++;
            _window ??= new();
            _window.gameObject.SetActive(true);
            IsOpen = true; _onHidden = closed;
            return true;
        }
        internal static void Close() { Closes++; CloseFromX(); }
        internal static void CloseFromX()
        {
            if (_window == null) return;
            _window.gameObject.SetActive(false);
            NotifyHidden(_window); // native callback precedes IsOpen transition
            IsOpen = false;
        }
        internal static void Bind(UISubmenuGOWindow pane, Action closed) { _window = pane; _onHidden = closed; }
        internal static void Hidden(UISubmenuGOWindow pane) => NotifyHidden(pane);
        private static bool _degraded;
        private static UIOptionsWindow? _failedHost;
        private static float _nextInjectionRetry;
        private static int _consecutiveInjectionFailures;
        private const int MaxConsecutiveInjectionFailures = 3;
        internal static int Cleanups;
        internal static bool Degraded => _degraded;
        private static void Shutdown() { Cleanups++; _degraded = false; }
        internal static bool Ready(UIOptionsWindow host) => InjectionRetryReady(host);
        internal static void Fail(UIOptionsWindow host) => Degrade(host, "transient test failure");
    }
    internal static class MenuRowSeat
    {
        internal static bool Throw;
        internal static int Calls;
        internal static void Tick(bool pause, UIMainMenuOption? row, UIMainMenuOption? donor, bool shown)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("transient seat failure");
        }
    }
    internal static partial class MenuWindowFamily
    {
        private const string VROptionsWindowName = "GloomhavenVR.OptionsTabWindow";
        private static readonly List<UIWindow> ModOwned = new();
        internal static void Register(UIWindow window) => ModOwned.Add(window);
    }
    internal static partial class ModalFallback
    {
        private static bool IsMandatoryDecision(UIWindow window, out string reason)
        { reason = "test native waiter"; return window.Mandatory; }
        private const int ChurnMaxFloats = 3;
        private const float ChurnWindowSeconds = 60f;
        private static readonly HashSet<string> ChurnSuppressed = new();
        private static readonly Dictionary<string, (int Count, float WindowStart)> FloatChurn = new();
        internal static bool Admit(UIWindow window, bool hover = false)
        {
            // The early suppressed-name test is extracted separately from the production caller.
            return PassEarlySuppression(window, hover) && AllowCatchAllRepeat(window, hover);
        }
        internal static void Suppress(string name) => ChurnSuppressed.Add(name);
        internal static bool Counted(string name) => FloatChurn.ContainsKey(name);
        internal static void Reset() { FloatChurn.Clear(); ChurnSuppressed.Clear(); }
    }
    internal static partial class VRMenuEntry
    {
        private static float _retryAfter;
        private static bool _loggedTickFailure;
        private static bool _loggedSeatFailure;
        private static ESCMenu? _host;
        private static UIMainOptionsMenu? _mainHost;
        private static ESCMenu? _pauseInjectFailed;
        private static float _pauseRetryAfter;
        private static UIMainMenuOption? _entry, _mainEntry, _donor, _mainDonor;
        private const float MainScanInterval = 1f;
        private const int MainScanBudget = 12;
        private static int _mainScanScene, _mainScansLeft;
        private static float _nextMainScan;
        internal static UIMainMenuOption[]? _mainRivals;
        internal static bool _yieldArmed;
        internal static bool Throw, FailPause, FailMain;
        internal static int TickCalls, PauseInjections, MainInjections;
        private static void Inject(ESCMenu host)
        {
            PauseInjections++;
            if (!FailPause) _entry = new();
        }
        private static void InjectMain(UIMainOptionsMenu host)
        {
            MainInjections++;
            if (!FailMain) { _mainHost = host; _mainEntry = new(); }
        }
        private static void TickMainMenuExclusivity(bool open)
        {
            TickCalls++;
            if (Throw) throw new InvalidOperationException("transient entry failure");
        }
        private static bool _loggedLatch;
        private static UIMainMenuOption[]? ResolveRivals() => _mainRivals;
        internal static void BindForTest(UIMainMenuOption row, bool main) => BindRow(row, main);
        internal static void Discover() { TickPauseMenu(); TickMainMenu(); }
        internal static void DropRows() { _entry = null; _mainEntry = null; }
        internal static bool HasRows => _entry != null && _mainEntry != null;
        internal static void ResetDiscovery()
        {
            _host = null; _mainHost = null; _entry = null; _mainEntry = null;
            _mainScansLeft = 0; _mainScanScene = 0; _nextMainScan = 0;
            _pauseInjectFailed = null; _pauseRetryAfter = 0;
        }

        internal static void Setup(ESCMenu pause, UIMainOptionsMenu main, UIMainMenuOption row,
            UIMainMenuOption mainRow, UIMainMenuOption donor)
        {
            _host = pause; Singleton<ESCMenu>.Instance = pause; _mainHost = main; _entry = row; _mainEntry = mainRow;
            _donor = _mainDonor = donor; _mainScanScene = 1;
        }
    }
}
