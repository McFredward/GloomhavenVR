using System;
using System.Linq;
using System.Threading;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value, string reason)
    {
        _assertions++;
        if (!value) throw new InvalidOperationException("Town window close assertion: " + reason);
    }
    private static void Reset()
    {
        Check(!TownWindowCloseScope.Active, "previous close never leaks explicit intent");
        ModalFallback.Panel = new(); ModalFallback.HasPanel = true; ModalFallback.ResetMenu = null;
        GuildmasterDestinations.Leave = null; VRLog.Errors.Clear();
    }
    private static void ScopeLaws()
    {
        Check(!TownWindowCloseScope.Active, "scope starts outside explicit close intent");
        using (TownWindowCloseScope.Enter())
        {
            Check(TownWindowCloseScope.Active, "explicit scope admits its native callbacks");
            using (TownWindowCloseScope.Enter(false))
                Check(TownWindowCloseScope.Active, "disabled child does not replace enclosing explicit intent");
            Check(TownWindowCloseScope.Active, "disabled nested scope preserves enclosing explicit close intent");
            using (TownWindowCloseScope.Enter())
                Check(TownWindowCloseScope.Active, "nested explicit close keeps native continuation admitted");
            Check(TownWindowCloseScope.Active, "nested disposal retains outer explicit intent");
        }
        Check(!TownWindowCloseScope.Active, "explicit scope disposal returns to unscoped context");
        using (TownWindowCloseScope.Enter(false))
            Check(!TownWindowCloseScope.Active, "disabled scope never invents explicit intent");
        Check(!TownWindowCloseScope.Active, "disabled scope leaves unscoped context intact");
        using (TownWindowCloseScope.Enter())
        {
            bool otherThreadBefore = true, otherThreadDuring = false, otherThreadAfter = true;
            var thread = new Thread(() =>
            {
                otherThreadBefore = TownWindowCloseScope.Active;
                using (TownWindowCloseScope.Enter()) otherThreadDuring = TownWindowCloseScope.Active;
                otherThreadAfter = TownWindowCloseScope.Active;
            });
            thread.Start(); thread.Join();
            Check(!otherThreadBefore && otherThreadDuring && !otherThreadAfter,
                "thread-local scope cannot borrow another native close's intent");
            Check(TownWindowCloseScope.Active, "other thread disposal does not release parent close");
        }
    }
    private static void GuardCases()
    {
        Reset(); ModalFallback.CloseFloatedWindow(null);
        Check(!ModalFallback.Panel.UserClosing && !TownWindowCloseScope.Active, "null close is inert");
        foreach (string guard in new[] { "Semantic", "Permanent", "Mandatory" })
        {
            Reset();
            var window = new UIWindow { Destination = true, Semantic = guard == "Semantic",
                Permanent = guard == "Permanent", Mandatory = guard == "Mandatory" };
            ModalFallback.CloseFloatedWindow(window);
            Check(window.Calls.All(call => !call.Explicit),
                guard == "Semantic" ? "semantic continuation stays outside explicit destination scope"
                    : "permanent and mandatory guards run before explicit destination scope");
            Check(window.IsOpen && window.Hides == 0 && window.Escapes == 0,
                "protected native continuation cannot become an Escape or Hide");
            Check(!window.Calls.Any(call => call.Operation is "LeaveMode" or "ResetMenu")
                && !ModalFallback.Panel.UserClosing, "guard refusal precedes native mode or float release");
            Check(!TownWindowCloseScope.Active, "guard refusal retains unscoped native context");
        }
    }
    private static void NativeCloseCases()
    {
        Reset();
        var ordinary = new UIWindow();
        ModalFallback.CloseFloatedWindow(ordinary);
        Check(ordinary.Calls.All(call => !call.Explicit), "ordinary native menu does not borrow destination close intent");
        Check(!ordinary.IsOpen && ordinary.Escapes == 1 && ordinary.Hides == 1,
            "ordinary native menu retains Escape then required Hide");
        foreach (string nativeState in new[] { "active 3D destination", "already home after purchase", "inactive 2D map" })
        {
            Reset();
            var window = new UIWindow { Destination = true };
            // The native mode callback is configurable: active Exit hides its window,
            // whereas an already-home HUD and a 2D destination leave it for fallback.
            if (nativeState == "active 3D destination") GuildmasterDestinations.Leave = w => w.Hide();
            ModalFallback.CloseFloatedWindow(window);
            Check(window.Calls.Single(call => call.Operation == "LeaveMode").Explicit,
                "native destination leave observes explicit close intent");
            Check(window.Calls.Where(call => call.Operation is "Escape" or "Hide" or "ResetMenu").All(call => call.Explicit),
                "native fallback and cleanup retain explicit intent after an already-home or 2D mode");
            Check(!window.IsOpen && window.Hides == 1 && window.ExplicitHides == 1,
                "real native destination Hide observes one explicit close after " + nativeState);
            Check(window.Escapes == (nativeState == "active 3D destination" ? 0 : 1),
                "fallback runs only when original native mode exit leaves the window open");
            Check(ModalFallback.Panel.UserClosing && !TownWindowCloseScope.Active,
                "completed native destination close releases only its float and scope");
        }
        Reset();
        var temporarilyHidden = new UIWindow { Destination = true };
        temporarilyHidden.Hide();
        Check(temporarilyHidden.Calls.Single().Explicit == false && temporarilyHidden.ExplicitHides == 0,
            "temporary native presentation Hide never impersonates an explicit service exit");
        Reset();
        var sticky = new UIWindow { Destination = true, IsOpen = false };
        ModalFallback.CloseFloatedWindow(sticky);
        Check(sticky.Escapes == 0 && sticky.Hides == 0 && ModalFallback.Panel.UserClosing,
            "already-hidden sticky destination releases float without inventing a native Hide");
        var group = ModalFallback.Panel.WindowCanvasGroup!;
        Check(group.alpha == 0 && !group.blocksRaycasts && !group.interactable,
            "already-hidden sticky window drops only its forced native canvas state");
        Check(!TownWindowCloseScope.Active, "already-hidden fallback never leaks explicit intent");
        Reset(); ModalFallback.HasPanel = false;
        var noPanel = new UIWindow { Destination = true };
        ModalFallback.CloseFloatedWindow(noPanel);
        Check(noPanel.ExplicitHides == 1 && !TownWindowCloseScope.Active,
            "missing float panel cannot skip native close or scope cleanup");
    }
    private static void ExceptionCases()
    {
        foreach (string failure in new[] { "LeaveMode", "Escape", "Hide", "ResetMenu" })
        {
            Reset();
            var window = new UIWindow { Destination = true };
            Action throwing = () => throw new ApplicationException("native " + failure);
            if (failure == "LeaveMode") GuildmasterDestinations.Leave = _ => throwing();
            if (failure == "Escape") window.EscapeAction = throwing;
            if (failure == "Hide") window.HideAction = throwing;
            if (failure == "ResetMenu") ModalFallback.ResetMenu = throwing;
            bool propagated = false;
            try { ModalFallback.CloseFloatedWindow(window); }
            catch (ApplicationException) { propagated = true; }
            Check(!TownWindowCloseScope.Active, "native exceptions always unwind explicit destination close intent");
            Check(propagated == (failure is "LeaveMode" or "ResetMenu"),
                "actual close preserves native catch and propagation boundaries");
            Check(VRLog.Errors.Count == (failure is "Escape" or "Hide" ? 1 : 0),
                "fallback failures retain the existing bounded native error report");
        }
        Reset();
        using (TownWindowCloseScope.Enter())
        {
            var window = new UIWindow { Destination = true, EscapeAction = () => throw new ApplicationException("nested") };
            ModalFallback.CloseFloatedWindow(window);
            Check(TownWindowCloseScope.Active, "failed nested close retains caller's own explicit scope");
        }
        Check(!TownWindowCloseScope.Active, "outer explicit intent ends after failed nested close");
    }
    private static void Main()
    {
        ScopeLaws(); GuardCases(); NativeCloseCases(); ExceptionCases();
        Console.WriteLine("Town window close: " + _assertions + " actual-source assertions passed.");
    }
}
