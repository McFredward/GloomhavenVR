using System.Collections.Generic;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Keep the message's native close event and its queue continuation together.</summary>
internal static class MessageWindowContinuation
{
    private static readonly HashSet<UIMessage> Dispatching = new();

    /// <summary>
    /// Consumes a close request for a UIMessage, including an unavailable original button.
    /// UIMessage.Hide alone is insufficient: MessageHandler.Start also subscribes ShowNext
    /// to OnClosePressed. Hiding the UIWindow strands both callbacks; calling only Hide
    /// strands the queue. Invoke the same event as the visible native close button, and do
    /// not mark the float as closing: its callback may synchronously show the next message
    /// on the same pooled window. Pagination and native button eligibility stay untouched.
    /// </summary>
    internal static bool TryClose(UIWindow? window)
    {
        if (window == null) return false;
        UIMessage? message = window.GetComponent<UIMessage>();
        if (message == null) return TryCloseDialog(window);
        Button? close = message.closeButton;
        if (window.IsOpen && message.isActiveAndEnabled && close != null
            && close.IsActive() && close.IsInteractable() && Dispatching.Add(message))
        {
            try { close.onClick.Invoke(); }
            finally { Dispatching.Remove(message); }
        }
        return true;
    }

    private static bool TryCloseDialog(UIWindow window)
    {
        DialogPopup? popup = window.GetComponent<DialogPopup>();
        // The native dialog serializes its window separately; it need not live on
        // the same GameObject. Match the exact published window, never an ancestor.
        if (popup == null && UIManager.Instance != null)
            popup = UIManager.Instance.dialogPopup;
        if (popup == null || popup.Window != window || !popup.allowHide) return false;
        if (!window.IsOpen || !popup.isActiveAndEnabled) return true;

        // Cancel owns the option callback as well as returning borrowed content and
        // releasing the controller area. A disabled cancel option is not permission
        // to fall back to Hide. Mandatory dialogs remain with the decision policy.
        if (popup.cancelOption >= 0 && popup.cancelOption < popup.optionButtons.Count)
        {
            Button? cancel = popup.optionButtons[popup.cancelOption].ExtendedButton;
            if (cancel != null && cancel.IsActive() && cancel.IsInteractable()) popup.Cancel();
        }
        // Show(string, ...) does not clear a prior content overload's cancelAction.
        // Its visible contentText is the native per-Show discriminator; never invoke
        // an earlier borrowed-content transaction from a later textual notice.
        else if (popup.cancelAction != null && popup.contentText != null
            && !popup.contentText.gameObject.activeSelf) popup.Cancel();
        else popup.TryHide();
        return true;
    }
}
