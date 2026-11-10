using System;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Net;

internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string reason)
    {
        _checks++;
        if (!condition) throw new Exception("Peer retirement assertion: " + reason);
    }
    private static NetAvatarDriver Start()
    {
        CleanupPorts.Reset();
        var driver = new NetAvatarDriver(); driver.Add(1); driver.Add(2);
        driver.Enable();
        return driver;
    }
    private static void Invoke(Action action)
    {
        try { action(); }
        catch (Exception ex) { throw new Exception("Peer retirement assertion: teardown must isolate a failed owner", ex); }
    }
    private static void Retired(NetAvatarDriver driver, int id)
    {
        Check(!driver._avatars.ContainsKey(id), "failed cosmetic cleanup cannot retain a departed avatar");
        Check(!driver._pending.ContainsKey(id) && !driver._pendingExtras.ContainsKey(id)
            && !driver._pendingAnimations.ContainsKey(id) && !driver._createRetryAt.ContainsKey(id)
            && !driver._peerEnv.ContainsKey(id), "departed peer loses queued input and placement/clock eligibility");
    }
    private static void Main()
    {
        NetAvatarDriver driver = Start();
        Invoke(() => driver.Retire(1)); Retired(driver, 1);
        Check(!CleanupPorts.Figures.Contains(1) && !CleanupPorts.Props.Contains(1), "ordinary departure restores both held-object owners");
        Check(driver._avatars.ContainsKey(2) && driver._pending.ContainsKey(2) && driver._peerEnv.ContainsKey(2)
            && CleanupPorts.Figures.Contains(2) && CleanupPorts.Props.Contains(2), "one peer departure preserves unrelated pending peers");
        driver.Retire(1);
        Check(CleanupPorts.Calls.Count(x => x == "avatar:1") == 1, "duplicate departure never destroys the old avatar twice");
        Check(VRLog.Lines.Count == 0, "ordinary cleanup emits no false failure");

        string[] owners = { "transport", "figures", "props", "native", "fetch", "focus", "triggers", "census", "preview", "decision", "avatar" };
        foreach (string failure in owners)
        {
            driver = Start(); CleanupPorts.Fail.Add(failure + ":1");
            Invoke(() => driver.Retire(1)); Retired(driver, 1);
            foreach (string owner in owners)
                Check(CleanupPorts.Calls.Contains(owner + ":1"), "one failed owner must not starve another owner's release");
            Check(failure == "figures" || !CleanupPorts.Figures.Contains(1), "failed preview or cosmetic teardown cannot retain a held figure");
            Check(failure == "props" || !CleanupPorts.Props.Contains(1), "failed preview or cosmetic teardown cannot retain a held prop");
            Check(VRLog.Lines.Any(x => x.Contains("ISOLATED") && x.Contains("Net.ForgetPeer")), "failed owner retains a bounded normal-level diagnostic");
            Check(driver._avatars.ContainsKey(2) && driver._pending.ContainsKey(2), "fault isolation never discards another peer");
        }

        driver = Start();
        bool retiredBeforeDestruction = false;
        CleanupPorts.After = name =>
        {
            if (name != "avatar:1") return;
            retiredBeforeDestruction = !driver._avatars.ContainsKey(1) && !driver._peerEnv.ContainsKey(1);
            driver.Add(1);
        };
        Invoke(() => driver.Retire(1));
        Check(retiredBeforeDestruction, "driver identity is retired before destructor lifecycle callbacks");
        Check(driver._avatars.ContainsKey(1) && driver._pending.ContainsKey(1) && driver._peerEnv.ContainsKey(1),
            "same-ID rejoin created by a final destructor callback keeps its fresh queued state");

        foreach (string failure in new[] { "native-reset", "fragments", "avatar:1" })
        {
            driver = Start(); CleanupPorts.Fail.Add(failure);
            Invoke(driver.RetireAll); Retired(driver, 1); Retired(driver, 2);
            Check(CleanupPorts.Figures.Count == 0 && CleanupPorts.Props.Count == 0,
                "session retirement restores all held objects after any earlier reset/destructor failure");
            Check(CleanupPorts.Calls.Contains("avatar:2"), "an earlier teardown failure never skips later peers");
        }

        driver = Start();
        CleanupPorts.After = name => { if (name == "avatar:1") driver.Add(3); };
        Invoke(driver.RetireAll); Retired(driver, 1); Retired(driver, 2);
        Check(driver._avatars.Count == 1 && driver._avatars.ContainsKey(3) && driver._pending.ContainsKey(3)
            && driver._peerEnv.ContainsKey(3) && CleanupPorts.Figures.Contains(3),
            "teardown snapshot preserves a new peer created during a destructor callback");

        driver = Start();
        bool replacing = false;
        CleanupPorts.After = name =>
        {
            if (name != "avatar:1" || replacing) return;
            replacing = true; driver.Retire(2); driver.Add(2);
        };
        Invoke(driver.RetireAll); Retired(driver, 1);
        Check(driver._avatars.ContainsKey(2) && driver._pending.ContainsKey(2) && driver._peerEnv.ContainsKey(2),
            "snapshot identity check preserves a same-ID replacement created during another peer's teardown");
        Check(CleanupPorts.Calls.Count(x => x == "avatar:2") == 1, "old identity is destroyed once and replacement is never destroyed");

        foreach (string failure in new[] { "unsubscribe", "native-reset", "cardfx-reset", "focus-reset", "story-reset",
            "maproom-reset", "mapstory-reset", "lifecycle-reset", "video-reset", "props-clear", "version-reset", "badges-reset" })
        {
            driver = Start(); CleanupPorts.Fail.Add(failure);
            Invoke(driver.Disable); Retired(driver, 1); Retired(driver, 2);
            Check(CleanupPorts.Figures.Count == 0 && CleanupPorts.Props.Count == 0,
                "hot disable still restores held objects after cosmetic reset failure");
            Check(CleanupPorts.Calls.Contains("badges-reset") && CleanupPorts.Calls.Contains("avatar:2"),
                "hot disable reaches later cleanup and peer retirement after every earlier external failure");
        }

        driver = Start(); driver._transport = new object();
        Invoke(driver.Disable); Retired(driver, 1); Retired(driver, 2);
        Check(!CleanupPorts.Calls.Any(x => x.StartsWith("transport:") || x == "fragments"),
            "flat or non-FFS transport needs no unsupported transport calls");

        CleanupPorts.Reset();
        for (int attempt = 0; attempt < 20; attempt++)
        {
            driver = new NetAvatarDriver(); driver.Add(1);
            CleanupPorts.Fail.Add("avatar:1"); Invoke(() => driver.Retire(1));
        }
        Check(VRLog.Lines.Count == 1, "stable lifecycle guard keys bound repeat failure logs across repeated peers");
        UnityEngine.Time.unscaledTime += 11f;
        driver = new NetAvatarDriver(); driver.Add(1); Invoke(() => driver.Retire(1));
        Check(VRLog.Lines.Count == 2 && VRLog.Lines[1].Contains("still throwing"), "persistent owner failure remains discoverable at the shared report cadence");

        // The current complete enable/disable bodies and exact forwarding callback
        // execute against an explicit event source, without a native window/pose.
        driver = Start();
        var window = new object();
        Check(ReferenceEquals(NetAvatarDriver.Current, driver)
            && GloomhavenVR.Core.Events.VREvents.HandlerCount == 1,
            "enable attaches the current driver's exact Results visibility callback once");
        GloomhavenVR.Core.Events.VREvents.Publish(window, true);
        Check(ScenarioResultsPose.Observed == 1 && ReferenceEquals(ScenarioResultsPose.Window, window) && ScenarioResultsPose.Shown,
            "the original forwarding callback preserves exact window and visibility payload");
        Check(SharedWindowScrollSync.Observed == 1 && ReferenceEquals(SharedWindowScrollSync.Window, window) && SharedWindowScrollSync.Shown,
            "shared scroll observes the original exact window visibility transition");
        Invoke(driver.Disable);
        Check(GloomhavenVR.Core.Events.VREvents.HandlerCount == 0,
            "disable retires the exact Results visibility subscription");
        GloomhavenVR.Core.Events.VREvents.Publish(window, false);
        Check(ScenarioResultsPose.Observed == 1,
            "a disabled driver cannot receive a later native Results opening");
        Check(SharedWindowScrollSync.Observed == 1,
            "a disabled driver cannot receive a later shared scroll transition");
        Invoke(driver.Disable);
        Check(GloomhavenVR.Core.Events.VREvents.HandlerCount == 0,
            "repeated disable never reinstates a retired Results callback");

        driver = Start(); Invoke(() => driver.Retire(1));
        Check(!CleanupPorts.Stories.Contains(1) && CleanupPorts.Stories.Contains(2),
            "departed peer story bindings retire independently before numeric-ID reuse");
        Check(CleanupPorts.Calls.IndexOf("story-lifecycle:1") < CleanupPorts.Calls.IndexOf("avatar:1"),
            "story occurrence identity retires before replacement-avatar callbacks");
        driver = Start(); CleanupPorts.Fail.Add("story-lifecycle:1");
        Invoke(() => driver.Retire(1)); Retired(driver, 1);
        Check(CleanupPorts.Calls.Contains("avatar:1") && CleanupPorts.Stories.Contains(2)
            && driver._avatars.ContainsKey(2),
            "a failed story owner cannot skip later cosmetic cleanup or retire another peer");
        driver = Start();
        CleanupPorts.After = name => { if (name == "avatar:1") driver.Add(1); };
        Invoke(() => driver.Retire(1));
        Check(CleanupPorts.Stories.Contains(1) && driver._avatars.ContainsKey(1),
            "a replacement native identity keeps its fresh story state after final destructor reentry");
        Console.WriteLine($"Peer retirement: {_checks} assertions passed.");
    }
}
