using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Main()
    {
        foreach (string scene in new[] { "Campaign", "Guildmaster", "Tutorial", "Main menu" })
        {
            ModalFallback.Reset();
            // The clone is intentionally ID-less in every scene. Test identity registration and
            // its existing exact-name recovery fallback, including an inherited suppression entry.
            var named = new UIWindow { name = "GloomhavenVR.OptionsTabWindow" };
            var registered = new UIWindow { name = scene + " registered settings" };
            MenuWindowFamily.Register(registered);
            ModalFallback.Suppress(named.name);
            ModalFallback.Suppress(registered.name);
            var host = new ESCMenu { IsOpen = true };
            var main = new UIMainOptionsMenu();
            var donor = new UIMainMenuOption { IsInteractable = false, enabled = false };
            var row = new UIMainMenuOption();
            var mainRow = new UIMainMenuOption();
            row.gameObject.parent = host.gameObject;
            mainRow.gameObject.parent = main.gameObject;
            VRMenuEntry.Setup(host, main, row, mainRow, donor);
            for (int cycle = 0; cycle < 128; cycle++)
            {
                Time.unscaledTime = cycle * .1f;
                Check(ModalFallback.Admit(named), "settings survive preexisting suppression and repeated opens");
                Check(ModalFallback.Admit(registered), "registered identity survives repeated opens");
                VROptionsTab.IsOpen = true;
                row.Selected = cycle % 2 == 0;
                row.IsInteractable = mainRow.IsInteractable = false;
                row.enabled = mainRow.enabled = false;
                row.gameObject.SetActive(false);
                mainRow.gameObject.SetActive(false);
                VRMenuEntry.Tick();
                VRMenuEntry.LateTick();
                Check(row.gameObject.activeSelf && mainRow.gameObject.activeSelf,
                    "both settings doors regain visibility");
                Check(row.IsInteractable && mainRow.IsInteractable && row.enabled && mainRow.enabled,
                    "both settings doors remain enabled");
                Check(row.Focused && mainRow.Focused, "settings door is visibly focused");
                Check(row.Selected == (cycle % 2 == 0), "availability repair does not toggle selection");
                Check(!donor.IsInteractable && !donor.enabled, "native donor gating stays untouched");
            }
            Check(!ModalFallback.Counted(named.name) && !ModalFallback.Counted(registered.name),
                "intentional settings opens never consume HUD churn budget");
            host.IsOpen = false;
            host.gameObject.SetActive(false);
            main.gameObject.SetActive(false);
            row.gameObject.SetActive(false);
            mainRow.gameObject.SetActive(false);
            VRMenuEntry.LateTick();
            Check(!host.gameObject.activeSelf && !main.gameObject.activeSelf &&
                  !row.gameObject.activeSelf && !mainRow.gameObject.activeSelf,
                "closed native menus are never reactivated by row repair");
        }
        ModalFallback.Reset();
        var hud = new UIWindow { name = "Unknown cycling HUD" };
        for (int i = 0; i < 3; i++) Check(ModalFallback.Admit(hud), "initial unknown HUD openings allowed");
        for (int i = 0; i < 128; i++) Check(!ModalFallback.Admit(hud), "unknown HUD remains bounded");
        Check(VRLog.Warnings == 1, "HUD fuse reports once");
        for (int i = 0; i < 128; i++) Check(ModalFallback.Admit(hud, true), "hover retains repeat exemption");
        ModalFallback.Reset();
        Check(ModalFallback.Admit(hud), "scene reset clears unknown HUD suppression");
        Time.unscaledTime += 61f;
        for (int i = 0; i < 3; i++) Check(ModalFallback.Admit(hud), "unknown HUD interval resets");

        // Pooled progress windows may open once for each character, or on repeated
        // visits. A disabled/pending control is still an explicit continuation.
        foreach (object? control in new object?[] { null, new UnityEngine.UI.Selectable(),
            new ClickTracker(), new ClickTrackerExtended() })
        {
            ModalFallback.Reset();
            var prompt = new UIWindow { name = "Pooled progression prompt", Control = control,
                Mandatory = control == null };
            for (int cycle = 0; cycle < 32; cycle++)
            {
                Check(ModalFallback.Admit(prompt), "interactive repeated openings stay available");
                Check(!ModalFallback.Counted(prompt.name), "interactive repeated openings never consume HUD churn budget");
            }
            ModalFallback.Suppress(prompt.name);
            Check(ModalFallback.Admit(prompt), "interactive window survives prior name suppression");
        }

        Time.unscaledTime = 1000f;
        VRMenuEntry.Throw = true;
        VRMenuEntry.Tick();
        int calls = VRMenuEntry.TickCalls;
        for (int i = 0; i < 128; i++) VRMenuEntry.Tick();
        Check(VRMenuEntry.TickCalls == calls, "entry retry avoids per-frame throwing loop");
        VRMenuEntry.Throw = false;
        Time.unscaledTime += 2.1f;
        VRMenuEntry.Tick();
        Check(VRMenuEntry.TickCalls == calls + 1, "entry recovers after transient failure");
        MenuRowSeat.Throw = true;
        VRMenuEntry.LateTick();
        calls = MenuRowSeat.Calls;
        for (int i = 0; i < 128; i++) VRMenuEntry.LateTick();
        Check(MenuRowSeat.Calls == calls, "seat retry avoids per-frame throwing loop");
        MenuRowSeat.Throw = false;
        Time.unscaledTime += 2.1f;
        VRMenuEntry.LateTick();
        Check(MenuRowSeat.Calls > calls, "seat recovers after transient failure");
        for (int i = 0; i < 64; i++)
        {
            Time.unscaledTime += 3f;
            VRMenuEntry.Throw = true;
            VRMenuEntry.Tick();
            Time.unscaledTime += 3f;
            MenuRowSeat.Throw = true;
            VRMenuEntry.LateTick();
        }
        Check(VRLog.Errors == 2, "recurring entry and seat failures produce bounded normal logs");
        Time.unscaledTime = 2000f;
        var failedHost = new UIOptionsWindow();
        Check(VROptionsTab.Ready(failedHost), "initial host is eligible");
        for (int attempt = 0; attempt < 128; attempt++)
        {
            int cleanup = VROptionsTab.Cleanups;
            VROptionsTab.Fail(failedHost);
            Check(VROptionsTab.Degraded && VROptionsTab.Cleanups == cleanup + 1,
                "partial injection clone is cleaned before retry");
            for (int frame = 0; frame < 10; frame++)
                Check(!VROptionsTab.Ready(failedHost), "injection retry avoids per-frame clone loop");
            Time.unscaledTime += attempt < 2 ? 2.1f : 30.1f;
            Check(VROptionsTab.Ready(failedHost) && !VROptionsTab.Degraded,
                "same host retries after cooldown");
        }
        VROptionsTab.Fail(failedHost);
        Check(VROptionsTab.Ready(new UIOptionsWindow()), "new scene host retries immediately");
        Check(VRLog.Errors == 3 && VRLog.Warnings == 3,
            "repeated injection failures produce three bounded reports");
        var oldPane = new UISubmenuGOWindow();
        var newPane = new UISubmenuGOWindow();
        int closed = 0;
        VROptionsTab.Bind(newPane, () => closed++);
        VROptionsTab.IsOpen = false;
        VROptionsTab.Hidden(oldPane);
        Check(closed == 0, "old clone cannot consume current close callback");
        VROptionsTab.IsOpen = true;
        VROptionsTab.Hidden(newPane);
        Check(closed == 0, "delayed close cannot deselect reopened pane");
        VROptionsTab.IsOpen = false;
        VROptionsTab.Hidden(newPane);
        VROptionsTab.Hidden(newPane);
        Check(closed == 1, "current close callback fires exactly once");
        VRMenuEntry.Throw = MenuRowSeat.Throw = false;
        VRMenuEntry.ResetDiscovery();
        UnityEngine.Object.Found = new UIMainOptionsMenu();
        Singleton<ESCMenu>.Instance = new ESCMenu();
        VROptionsTab.CanOpen = false;
        int searches = UnityEngine.Object.Finds;
        for (int second = 0; second < 64; second++)
        {
            Time.unscaledTime += 1f;
            VRMenuEntry.Discover();
        }
        Check(UnityEngine.Object.Finds == searches, "unavailable pane consumes no discovery scans");
        VROptionsTab.CanOpen = true;
        Time.unscaledTime += 2f;
        VRMenuEntry.Discover();
        Check(VRMenuEntry.HasRows, "late pane recovery creates both menu entries");
        searches = UnityEngine.Object.Finds;
        for (int cycle = 0; cycle < 128; cycle++)
        {
            VRMenuEntry.DropRows();
            Time.unscaledTime += 2f;
            VRMenuEntry.Discover();
            Check(VRMenuEntry.HasRows, "lost rows are recreated on known hosts");
        }
        Check(UnityEngine.Object.Finds == searches, "known main host never needs another scene search");
        VRMenuEntry.DropRows();
        VRMenuEntry.FailPause = VRMenuEntry.FailMain = true;
        Time.unscaledTime += 2f;
        VRMenuEntry.Discover();
        int pauseAttempts = VRMenuEntry.PauseInjections;
        int mainAttempts = VRMenuEntry.MainInjections;
        for (int frame = 0; frame < 128; frame++) VRMenuEntry.Discover();
        Check(VRMenuEntry.PauseInjections == pauseAttempts && VRMenuEntry.MainInjections == mainAttempts,
            "failed row construction respects retry cooldown");
        VRMenuEntry.FailPause = VRMenuEntry.FailMain = false;
        Time.unscaledTime += 2.1f;
        VRMenuEntry.Discover();
        Check(VRMenuEntry.HasRows, "failed row construction retries same native hosts");
        // Execute the real injected row callbacks, rather than only its availability helper.
        // Closing follows the native inactive -> callback -> IsOpen=false ordering.
        foreach (bool mainMenu in new[] { false, true })
        {
            var usable = new UIMainMenuOption { Focused = true };
            var disabled = new UIMainMenuOption { Focused = false, IsInteractable = false };
            var pause = new ESCMenu { IsOpen = true, rows = new[] { usable, disabled } };
            var main = new UIMainOptionsMenu { rows = pause.rows };
            var row = new UIMainMenuOption();
            var otherRow = new UIMainMenuOption();
            VRMenuEntry.Setup(pause, main, mainMenu ? otherRow : row,
                mainMenu ? row : otherRow, usable);
            int rivalClosed = 0;
            usable.Init(() => { }, () => rivalClosed++);
            VRMenuEntry._mainRivals = new[] { usable };
            VRMenuEntry.BindForTest(row, mainMenu);
            for (int cycle = 0; cycle < 128; cycle++)
            {
                usable.SetSelected(true);
                int rivalsBefore = rivalClosed;
                int opens = VROptionsTab.Opens;
                row.Press();
                Check(VROptionsTab.IsOpen && row.IsSelected && VROptionsTab.Opens == opens + 1,
                    "one press opens settings");
                Check(usable.Focused && !disabled.Focused && usable.IsInteractable && !disabled.IsInteractable,
                    "VR toggle preserves native focus and disabled state");
                Check(rivalClosed == rivalsBefore + (mainMenu ? 1 : 0),
                    "main toggle closes previous native window; pause keeps independent windows");
                int closes = VROptionsTab.Closes;
                VROptionsTab.CloseFromX();
                Check(!row.IsSelected && VROptionsTab.Closes == closes,
                    "X resets toggle without recursive close");
                row.Press();
                Check(VROptionsTab.IsOpen && row.IsSelected && VROptionsTab.Opens == opens + 2,
                    "first press after X reopens immediately without a Tick or grace period");
                row.Press();
                Check(!VROptionsTab.IsOpen && !row.IsSelected && VROptionsTab.Closes == closes + 1,
                    "second press closes exactly once");
            }
            VROptionsTab.CanOpen = false;
            row.Press();
            Check(!row.IsSelected && !VROptionsTab.IsOpen, "failed open leaves toggle off");
            VROptionsTab.CanOpen = true;
            row.SetSelected(true);
            otherRow.SetSelected(true);
            Time.unscaledTime += 100f;
            VRMenuEntry.Tick();
            Check(!row.IsSelected && !otherRow.IsSelected, "closed window clears both rows immediately");
        }
        Console.WriteLine($"VR options: {_assertions} runtime assertions passed.");
    }
}
