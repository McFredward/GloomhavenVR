using System;
using GloomhavenVR.WorldUI;
internal static class Program
{
    private static int _assertions;
    private static void Check(bool valid, string message)
    {
        _assertions++;
        if (!valid) throw new InvalidOperationException(message);
    }
    private static void Main()
    {
        var old = new UISubmenuGOWindow();
        var pane = new UISubmenuGOWindow();
        VROptionsTab.Bind(old);
        VROptionsTab.Open(null);
        VROptionsTab.Bind(pane);
        old.gameObject.activeSelf = false;
        int callbacks = 0;
        MenuWindowFamily.Registered = pane.Window;
        var panel = new WindowPanel { Window = pane.Window };
        ModalFallback.Current = panel;
        for (int i = 0; i < 256; i++)
        {
            panel.UserClosing = true;
            panel.Panel.PreRoll = true;
            bool latched = true;
            Check(VROptionsTab.Open(() => { callbacks++; latched = false; }), "opens on first request");
            Check(VROptionsTab.IsOpen && pane.gameObject.activeSelf, "native window is open");
            Check(!panel.UserClosing && !panel.Panel.PreRoll,
                "explicit reopen supersedes pending gap-close");
            // Repeated/stale events from a replaced or reopened pane cannot clear this opening.
            old.OnHidden.Invoke();
            pane.OnHidden.Invoke();
            Check(latched && callbacks == i, "stale close does not consume current callback");
            if ((i % 3) == 0) VROptionsTab.Close();
            else if ((i % 3) == 1) ModalFallback.CloseFloatedWindow(pane.Window);
            else pane.Window.Hide();
            Check(!latched && callbacks == i + 1, "native close synchronizes row before Hide returns");
            Check(!VROptionsTab.IsOpen && !pane.gameObject.activeSelf, "native state finishes closing");
            pane.OnHidden.Invoke();
            Check(callbacks == i + 1, "duplicate close invokes callback once");
            // No delayed tick or grace period is needed before the next opening.
        }
        var ordinary = new UIWindow();
        var ordinaryPanel = new WindowPanel { Window = ordinary, UserClosing = true };
        ordinaryPanel.Panel.PreRoll = true;
        ModalFallback.Current = ordinaryPanel;
        ModalFallback.PrepareModMenuReopen(ordinary);
        Check(ordinaryPanel.UserClosing && ordinaryPanel.Panel.PreRoll,
            "ordinary window pending close stays untouched");
        ModalFallback.Current = null;
        ModalFallback.PrepareModMenuReopen(pane.Window);
        ModalFallback.Current = panel;
        panel.UserClosing = true;
        panel.Panel.IsAlive = false;
        ModalFallback.PrepareModMenuReopen(pane.Window);
        Check(panel.UserClosing, "released panel cannot be revived by reopen");
        panel.Panel.IsAlive = true;
        VROptionsTab.Open(() => throw new InvalidOperationException("consumer"));
        bool laterListener = false;
        pane.OnHidden.AddListener(() => laterListener = true);
        pane.Window.Hide();
        Check(laterListener && VRLog.Warnings == 1, "bad consumer cannot amputate native hide listeners");
        Check(!VROptionsTab.IsOpen && !VROptionsTab.PendingTab, "failed callback still leaves menu closed");
        Console.WriteLine($"VR options close: {_assertions} runtime assertions passed.");
    }
}
