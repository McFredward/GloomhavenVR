using System;
namespace GloomhavenVR.WorldUI;
internal sealed class Signal
{
    private event Action? Listeners;
    internal void AddListener(Action listener) => Listeners += listener;
    internal void Invoke() => Listeners?.Invoke();
}
internal sealed class GameObject { internal bool activeSelf; }
internal sealed class UIWindow
{
    internal bool IsOpen;
    internal bool HasGoneToStartingState = true;
    internal readonly Signal onHidden = new();
    internal void Hide()
    {
        if (!IsOpen) return;
        // Native UIWindow.Hide invokes callbacks before updating its visual state.
        onHidden.Invoke();
        IsOpen = false;
    }
    internal void HideOrUpdateStartingState() => Hide();
}
internal sealed class UISubmenuGOWindow
{
    internal readonly UIWindow Window = new();
    internal readonly GameObject gameObject = new();
    internal readonly Signal OnHidden = new();
    internal UISubmenuGOWindow()
    {
        Window.onHidden.AddListener(() => {
            // Native UISubmenuGOWindow.OnCompleteHidden deactivates before OnHidden.
            gameObject.activeSelf = false;
            OnHidden.Invoke();
        });
    }
    internal T? GetComponent<T>() where T : class => Window as T;
    internal void Hide() => Window.Hide();
    internal void Show() { gameObject.activeSelf = true; Window.IsOpen = true; }
}
internal sealed class RectTransform { }
internal sealed class UIOptionsWindow
{
    internal void Show(RectTransform? pointAt, Action hidden) { }
    internal void Hide() { }
}
internal static class Singleton<T> where T : class
{
    internal static bool IsInitialized => false;
    internal static T Instance => throw new InvalidOperationException();
}
internal static class VRLog
{
    internal static int Warnings;
    internal static void Warn(string scope, string message) => Warnings++;
}
internal static partial class ModalFallback
{
    internal static WindowPanel? Current;
    private static WindowPanel? FindPanel(UIWindow window)
        => ReferenceEquals(Current?.Window, window) ? Current : null;
    internal static void CloseFloatedWindow(UIWindow window)
    {
        if (FindPanel(window) is { } panel) panel.UserClosing = true;
        window.Hide();
    }
}
internal static partial class VROptionsTab
{
    private static UISubmenuGOWindow? _window;
    private static Action? _onHidden;
    private static bool _hiddenHooked;
    private static bool _selectOnShow;
    private static bool _degraded;
    internal static bool IsStandalone => true;
    internal static bool CanOpen => _window != null && !_degraded;
    internal static object? ContentRoot => _window;
    private static bool ShowStandalone()
    {
        ModalFallback.PrepareModMenuReopen(_window!.Window);
        _window.Show(); return IsOpen;
    }
    internal static void Bind(UISubmenuGOWindow pane)
    {
        _window = pane; _hiddenHooked = false; _onHidden = null;
        _selectOnShow = false; _degraded = false;
        HookHidden(pane);
    }
    internal static bool PendingTab => _selectOnShow;
}

internal sealed class Panel { internal bool IsAlive = true; internal bool PreRoll; }
internal sealed class WindowPanel
{
    internal UIWindow? Window;
    internal bool UserClosing;
    internal readonly Panel Panel = new();
}
internal static class MenuWindowFamily
{
    internal static UIWindow? Registered;
    internal static bool IsModOwned(UIWindow window) => ReferenceEquals(Registered, window);
}
internal static class WindowMaterialise
{
    internal static void DropPreRoll(Panel panel, string reason) => panel.PreRoll = false;
}
