using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine.UI;

// Native lifecycle boundary: UIMessage.Awake binds Hide to closeButton.onClick;
// MessageHandler.Start binds ShowNext to the SAME event. Their relevant source bodies
// are read from the readonly decompile in the local source-binding check below.
internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string why)
    {
        ++_assertions;
        if (!value) throw new InvalidOperationException(why);
    }

    private static void Main()
    {
        Check(!MessageWindowContinuation.TryClose(null), "null does not claim unrelated close");
        Check(!MessageWindowContinuation.TryClose(new UIWindow()), "ordinary windows retain their close route");
        for (int count = 1; count <= 12; ++count)
        {
            var window = new UIWindow { IsOpen = true };
            var message = new UIMessage(window);
            window.Message = message;
            var queue = new List<int>();
            for (int index = 0; index < count; ++index) queue.Add(index);
            int completed = 0;
            message.OnClose = () => ++completed;
            message.closeButton!.onClick.AddListener(() =>
            {
                queue.RemoveAt(0);
                if (queue.Count > 0) window.IsOpen = true;
            });
            for (int index = 0; index < count; ++index)
            {
                message.Page = 2;
                Check(MessageWindowContinuation.TryClose(window), "message owns semantic close");
                Check(completed == index + 1 && queue.Count == count - index - 1,
                    "native close callback AND queue continuation both run exactly once");
                Check(window.IsOpen == (index < count - 1), "queued message reopens same window without generic hide");
                Check(message.Page == 2, "close route never rewrites pagination");
            }
            Check(MessageWindowContinuation.TryClose(window) && completed == count,
                "closed message cannot dispatch continuation twice");
        }

        foreach (string blocked in new[] { "inactive-message", "missing-button", "inactive-button", "disabled-button", "hidden-window" })
        {
            var window = new UIWindow { IsOpen = true };
            var message = new UIMessage(window); window.Message = message;
            int calls = 0; message.OnClose = () => ++calls;
            if (blocked == "inactive-message") message.isActiveAndEnabled = false;
            if (blocked == "missing-button") message.closeButton = null;
            if (blocked == "inactive-button") message.closeButton!.Active = false;
            if (blocked == "disabled-button") message.closeButton!.Interactable = false;
            if (blocked == "hidden-window") window.IsOpen = false;
            Check(MessageWindowContinuation.TryClose(window), "unavailable message still consumes close; never generic Hide");
            Check(calls == 0, "native availability blocks dispatch: " + blocked);
        }

        var reentrantWindow = new UIWindow { IsOpen = true };
        var reentrant = new UIMessage(reentrantWindow); reentrantWindow.Message = reentrant;
        int reentrantCalls = 0;
        reentrant.OnClose = () =>
        {
            ++reentrantCalls;
            reentrantWindow.IsOpen = true;
            if (reentrantCalls < 2) MessageWindowContinuation.TryClose(reentrantWindow);
        };
        MessageWindowContinuation.TryClose(reentrantWindow);
        Check(reentrantCalls == 1, "callback reentrancy cannot consume the next message");
        reentrant.OnClose = () => throw new InvalidOperationException("native callback failure");
        try { MessageWindowContinuation.TryClose(reentrantWindow); } catch (InvalidOperationException) { }
        reentrantWindow.IsOpen = true; reentrant.OnClose = () => ++reentrantCalls;
        MessageWindowContinuation.TryClose(reentrantWindow);
        Check(reentrantCalls == 2, "throwing callback releases dispatch guard for future opening");
        CheckDialogs();
        Console.WriteLine($"Message continuation: {_assertions} assertions passed.");
    }

    private static void CheckDialogs()
    {
        foreach (bool separateObject in new[] { false, true })
        foreach (bool hasOption in new[] { false, true })
        {
            var window = new UIWindow { IsOpen = true };
            var popup = new DialogPopup(window) { allowHide = true };
            if (separateObject) UIManager.Instance = new UIManager { dialogPopup = popup };
            else window.Popup = popup;
            int cancelled = 0;
            if (hasOption)
            {
                popup.cancelOption = 0;
                var button = new Button();
                button.onClick.AddListener(() => { popup.Hide(); ++cancelled; });
                popup.optionButtons.Add(new InputButton { ExtendedButton = button });
                button.Interactable = false;
                Check(MessageWindowContinuation.TryClose(window) && window.IsOpen && cancelled == 0,
                    "disabled native cancel cannot become a forced hide");
                button.Interactable = true;
            }
            else popup.cancelAction = () => ++cancelled;
            Check(MessageWindowContinuation.TryClose(window), "dismissible dialog uses its controller");
            Check(cancelled == 1 && popup.ContentRestored && !popup.ControllerActive && !window.IsOpen,
                "native dialog cancellation restores content and releases controller before continuation");
            MessageWindowContinuation.TryClose(window);
            Check(cancelled == 1, "closed dialog cannot dispatch twice");
            UIManager.Instance = null;
        }
        var mandatoryWindow = new UIWindow { IsOpen = true };
        var mandatory = new DialogPopup(mandatoryWindow); mandatoryWindow.Popup = mandatory;
        Check(!MessageWindowContinuation.TryClose(mandatoryWindow) && mandatoryWindow.IsOpen,
            "mandatory dialog stays with native choice and rescue policy");
        mandatory.allowHide = true;
        Check(MessageWindowContinuation.TryClose(mandatoryWindow) && mandatory.ContentRestored,
            "plain dismissible dialog uses native TryHide cleanup");
    }
}

internal sealed class UIMessage
{
    internal readonly UIWindow Window;
    internal Button? closeButton = new();
    internal bool isActiveAndEnabled = true;
    internal Action? OnClose;
    internal int Page;
    internal UIMessage(UIWindow window)
    {
        Window = window;
        closeButton!.onClick.AddListener(Hide);
    }
    internal void Hide() { Window.Hide(); OnClose?.Invoke(); }
}

internal sealed class UIManager
{
    internal static UIManager? Instance;
    internal DialogPopup? dialogPopup;
}
internal sealed class InputButton { internal Button ExtendedButton = new(); }
internal sealed class DialogPopup
{
    internal readonly UIWindow Window;
    internal bool allowHide, ContentRestored;
    internal bool isActiveAndEnabled = true, ControllerActive = true;
    internal int cancelOption = -1;
    internal readonly List<InputButton> optionButtons = new();
    internal Action? cancelAction;
    internal DialogPopup(UIWindow window) { Window = window; }
    internal void Cancel()
    {
        if (cancelOption >= 0 && cancelOption < optionButtons.Count) optionButtons[cancelOption].ExtendedButton.onClick.Invoke();
        else if (cancelAction != null) { Hide(); cancelAction(); }
    }
    internal void TryHide() { if (allowHide) Hide(); }
    internal void Hide() { Window.Hide(); ContentRestored = true; ControllerActive = false; }
}

namespace UnityEngine.UI
{
    internal sealed class UIWindow
    {
        internal bool IsOpen;
        internal UIMessage? Message;
        internal DialogPopup? Popup;
        internal T? GetComponent<T>() where T : class => Message as T ?? Popup as T;
        internal void Hide() => IsOpen = false;
    }
    internal sealed class Button
    {
        internal bool Active = true, Interactable = true;
        internal readonly ClickEvent onClick = new();
        internal bool IsActive() => Active;
        internal bool IsInteractable() => Interactable;
    }
    internal sealed class ClickEvent
    {
        private readonly List<Action> _listeners = new();
        internal void AddListener(Action callback) => _listeners.Add(callback);
        internal void Invoke() { foreach (Action callback in _listeners.ToArray()) callback(); }
    }
}
