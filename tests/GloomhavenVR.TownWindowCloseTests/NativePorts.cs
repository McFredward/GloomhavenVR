using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI.MapRoom;
using GloomhavenVR.WorldUI;

namespace UnityEngine.UI
{
    // Native behavior is a configurable boundary, not a replacement close algorithm.
    // Every callback records the actual production scope at its point of invocation.
    internal sealed class UIWindow
    {
        internal string name = "Native service";
        internal int ID => 42;
        internal bool IsOpen = true;
        internal bool Destination, Permanent, Mandatory, Semantic;
        internal int Escapes, Hides, ExplicitHides;
        internal int CloseCaptures, CloseCompletions;
        internal TownServiceTutorialPatches.MerchantClose? PendingClose, CompletedClose;
        internal bool ClosedAtCompletion, FloatReleasedAtCompletion, CanvasReleasedAtCompletion;
        internal Action? EscapeAction, HideAction;
        internal bool EscapeResult = false;
        internal readonly List<(string Operation, bool Explicit)> Calls = new();
        internal void Observe(string operation) => Calls.Add((operation, TownWindowCloseScope.Active));
        internal bool Escape()
        {
            Escapes++; Observe("Escape"); EscapeAction?.Invoke(); return EscapeResult;
        }
        internal void Hide()
        {
            Hides++; Observe("Hide"); HideAction?.Invoke(); IsOpen = false;
            if (Destination && TownWindowCloseScope.Active) ExplicitHides++;
        }
    }
}

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Errors = new();
        internal static void Info(string scope, string message) { }
        internal static void Error(string scope, string message) => Errors.Add(message);
    }
}

namespace GloomhavenVR.WorldUI.MapRoom
{
    using UnityEngine.UI;
    internal static class GuildmasterDestinations
    {
        internal static Action<UIWindow>? Leave;
        internal static bool IsDestination(UIWindow window) => window.Destination;
        internal static void LeaveMode(UIWindow window, string source)
        {
            window.Observe("LeaveMode"); Leave?.Invoke(window);
        }
    }
}

namespace GloomhavenVR.WorldUI
{
    using UnityEngine.UI;
    // The separate onboarding suite executes actual promise eligibility and dedup.
    // This port observes only the opaque capture/completion call order and inputs.
    internal static class TownServiceTutorialPatches
    {
        internal sealed class MerchantClose { }
        internal static MerchantClose? CaptureConvertedMerchantClose(UIWindow window)
        {
            window.CloseCaptures++; window.Observe("CaptureMerchantClose"); return window.PendingClose;
        }
        internal static void CompleteConvertedMerchantClose(UIWindow window, MerchantClose? close)
        {
            window.CloseCompletions++; window.Observe("CompleteMerchantClose"); window.CompletedClose = close;
            window.ClosedAtCompletion = !window.IsOpen;
            window.FloatReleasedAtCompletion = ModalFallback.HasPanel && ModalFallback.Panel.UserClosing;
            var group = ModalFallback.Panel.WindowCanvasGroup;
            window.CanvasReleasedAtCompletion = group != null && group.alpha == 0
                && !group.blocksRaycasts && !group.interactable;
        }
    }
    internal static class MessageWindowContinuation
    {
        internal static bool TryClose(UIWindow window)
        {
            window.Observe("Semantic"); return window.Semantic;
        }
    }
    internal static partial class ModalFallback
    {
        internal sealed class CanvasGroup
        {
            internal float alpha = 1;
            internal bool blocksRaycasts = true, interactable = true;
        }
        internal sealed class WindowPanel
        {
            internal bool UserClosing;
            internal CanvasGroup? WindowCanvasGroup = new();
        }
        internal static WindowPanel Panel = new();
        internal static bool HasPanel = true;
        internal static Action? ResetMenu;
        private static WindowPanel? FindPanel(UIWindow window) => HasPanel ? Panel : null;
        private static bool IsMapRoomPermanent(UIWindow window)
        {
            window.Observe("PermanentGuard"); return window.Permanent;
        }
        private static string MapRoomPermanentReason(UIWindow window) => "permanent native furniture";
        private static bool IsMandatoryDecision(UIWindow window, out string reason)
        {
            window.Observe("MandatoryGuard"); reason = "native continuation"; return window.Mandatory;
        }
        private static void RescueForMandatoryDecision(UIWindow window, string reason, float held, string origin)
            => window.Observe("Rescue");
        private static void ResetEscMenuToggleGroup(UIWindow window)
        {
            window.Observe("ResetMenu"); ResetMenu?.Invoke();
        }
    }
}
