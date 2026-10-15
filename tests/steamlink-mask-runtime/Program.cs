using System;
using System.IO;
using GloomhavenVR.Core.SteamLink;

internal static class PortableControls
{
    private static int _checks;
    private static void Require(bool condition, string label)
    {
        _checks++;
        if (!condition) throw new Exception("Control failed: " + label);
    }
    private static void Reject(Action operation, string label)
    {
        bool failed = false;
        try { operation(); } catch (InvalidDataException) { failed = true; }
        Require(failed, label);
    }

    private static void LifecycleControls()
    {
        var pending = new SteamLinkMaskLifecycle();
        Require(pending.Tick(false, true, true, false) == SteamLinkMaskStep.Idle, "disabled leaves normal rendering");
        Require(pending.Tick(true, true, true, false) == SteamLinkMaskStep.Prepare, "explicit setting prepares once while options visible");
        Require(!pending.StopForOptionsOpening(), "pending request survives own options OnShow");
        for (int update = 0; update < 128; update++)
            Require(pending.Tick(true, true, true, false) == SteamLinkMaskStep.WaitForOptions,
                    "pending request waits for explicit options closure without autonomous expiration");
        Require(pending.Tick(true, false, true, false) == SteamLinkMaskStep.Ready, "menu closure admits start");
        Require(pending.Policy.Start(true, true, true, true, true), "real policy begins only after closure");
        for (int callback = 0; callback < 128; callback++)
        {
            Require(pending.Tick(true, false, true, true) == SteamLinkMaskStep.Continue,
                    "ongoing valid lifecycle remains active until a real stop edge");
            Require(SteamLinkMaskCallbackGate.Admit(pending.Policy, pending.Policy, true, true, true, out bool stop)
                    && !stop && pending.Policy.IsRunning,
                    "ongoing valid callbacks never autonomously expire");
        }
        Require(pending.StopForOptionsOpening(), "normal menu/options opening interrupts active output");
        Require(!pending.Policy.IsRunning, "synchronous interruption closes render admission");
        Require(pending.Tick(true, false, true, true) == SteamLinkMaskStep.Stop, "closed policy never rearms unchanged request");
        foreach (int cause in new[] { 0, 1, 2, 3 })
        {
            var run = new SteamLinkMaskLifecycle();
            run.Tick(true, true, true, false);
            run.Policy.Start(true, true, true, true, true);
            bool requested = cause != 0, menu = cause == 1, supported = cause != 2, sameCamera = cause != 3;
            Require(run.Tick(requested, menu, supported, sameCamera) == SteamLinkMaskStep.Stop,
                    "disable/menu/VR loss/camera replacement terminates the request");
        }
        Require(new SteamLinkMaskLifecycle().Tick(true, false, false, false) == SteamLinkMaskStep.Stop,
                "standalone or unavailable VR rejects without preparing resources");
        var cancelledPending = new SteamLinkMaskLifecycle();
        cancelledPending.Tick(true, true, true, false);
        Require(cancelledPending.Tick(false, true, true, false) == SteamLinkMaskStep.Stop,
                "disable while options open cancels pending preparation");
        object retiredDriver = new object(), currentDriver = new object();
        bool freshRequest = true;
        if (SteamLinkMaskCallbackGate.OwnsRequest(currentDriver, retiredDriver)) freshRequest = false;
        Require(freshRequest, "deferred retired driver teardown cannot clear a new installed driver's request");
        Require(SteamLinkMaskCallbackGate.OwnsRequest(currentDriver, currentDriver), "current driver can clear its own request");
        Require(!SteamLinkMaskCallbackGate.OwnsRequest(null, retiredDriver), "unloaded driver owns no future request");
        var retired = new ProbePolicy();
        retired.Start(true, true, true, true, true);
        retired.Stop();
        var newRun = new ProbePolicy();
        newRun.Start(true, true, true, true, true);
        Require(!SteamLinkMaskCallbackGate.Admit(newRun, retired, false, false, false, out bool stopNew)
            && !stopNew && newRun.IsRunning,
            "deferred retired callback cannot cancel freshly enabled output");
        Require(SteamLinkMaskCallbackGate.Admit(newRun, newRun, true, true, true, out stopNew) && !stopNew,
            "current component continues after retired callback");
        Require(!SteamLinkMaskCallbackGate.Admit(newRun, newRun, false, true, false, out stopNew)
            && !stopNew && newRun.IsRunning,
            "foreign component cannot mutate current owner policy");
        Require(!SteamLinkMaskCallbackGate.Admit(newRun, newRun, true, true, false, out stopNew)
            && stopNew && !newRun.IsRunning,
            "current callback menu/disable/VR guard immediately closes output before Update");
        var fresh = new SteamLinkMaskLifecycle();
        Require(fresh.Tick(true, true, true, false) == SteamLinkMaskStep.Prepare,
                "fresh explicit request supports another run after cleanup");
        Require(fresh.Tick(true, false, true, false) == SteamLinkMaskStep.Ready
                && fresh.Policy.Start(true, true, true, true, true),
                "fresh explicit off/on cycle can resume output with a new policy");
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new Exception("Supply the original local payload directory.");
        for (int mask = 0; mask < 32; mask++)
        {
            var policy = new ProbePolicy();
            bool started = policy.Start((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                                        (mask & 8) != 0, (mask & 16) != 0);
            Require(started == (mask == 31), "all five start gates");
        }
        for (int mask = 0; mask < 64; mask++)
            foreach (ProbeEye eye in new[] { ProbeEye.Unknown, ProbeEye.Left, ProbeEye.Right, (ProbeEye)99 })
            {
                var policy = new ProbePolicy();
                Require(policy.Start(true, true, true, true, true), "prepare valid running policy");
                ProbeEye actual = policy.Select((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                    (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0, eye);
                ProbeEye expected = mask == 63 && (eye == ProbeEye.Left || eye == ProbeEye.Right) ? eye : ProbeEye.Unknown;
                Require(actual == expected, "camera/XR/both-eye/eye admission matrix");
            }
        var one = new ProbePolicy();
        Require(one.Phase == ProbePhase.Prepared && !one.IsRunning, "request stays inactive before explicit start");
        Require(one.Start(true, true, true, true, true), "supported explicit request starts output");
        for (int callback = 0; callback < 128; callback++)
        {
            Require(one.IsRunning, "running policy remains active without an automatic timer");
            ProbeEye eye = callback % 2 == 0 ? ProbeEye.Left : ProbeEye.Right;
            Require(one.Select(true, true, true, true, true, true, eye) == eye,
                    "repeated stereo callbacks retain their actual eye until manually stopped");
        }
        Require(!one.Start(true, true, true, true, true), "duplicate start cannot replace an active request");
        one.Stop();
        Require(!one.IsRunning && one.Select(true, true, true, true, true, true, ProbeEye.Left) == ProbeEye.Unknown,
                "manual stop immediately uses source fallback");
        Require(!one.Start(true, true, true, true, true), "stopped policy cannot silently rearm");

        string root = Path.GetFullPath(args[0]);
        Func<string, ProbeManifest> load = eye => ProbeManifest.Load(File.ReadAllBytes(Path.Combine(root, eye + "-source.json")), eye);
        ProbeManifest left = load("left"), right = load("right");
        ProbeManifest.CheckPair(left, right);
        Require(left.Session == "0123456789abcdef0123456789abcdef" && left.ContentId == 7, "actual trusted fixture source ID");
        Reject(() => ProbeManifest.Load(File.ReadAllBytes(Path.Combine(root, "right-source.json")), "left"), "wrong-eye source record");
        ProbeManifest bad = load("right"); bad.ContentId++;
        Reject(() => ProbeManifest.CheckPair(left, bad), "cross-generation pair");
        bad = load("right"); bad.Session = "fedcba9876543210fedcba9876543210";
        Reject(() => ProbeManifest.CheckPair(left, bad), "cross-session pair");
        bad = load("left"); bad.SourceExtent[0] = 513;
        Reject(() => bad.Check("left"), "bounded source extent");
        bad = load("left"); bad.Placements.Coverage[0]++;
        Reject(() => bad.Check("left"), "layout offset change");
        bad = load("left"); bad.Convention = "premultiplied";
        Reject(() => bad.Check("left"), "unsupported convention cannot silently convert");
        bad = load("left"); bad.PassthroughAuthorized = true;
        Reject(() => bad.Check("left"), "no activation input");
        bad = load("left"); bad.OpaqueFallbackRequired = false;
        Reject(() => bad.Check("left"), "no missing opaque fallback");
        bad = load("left"); bad.CanvasSha256 = new string('G', 64);
        Reject(() => bad.Check("left"), "strict digest encoding");
        bad = load("left"); bad.HeaderCopies = 1;
        Reject(() => bad.Check("left"), "cannot reduce identity redundancy");
        bad = load("left"); bad.Session = new string('0', 32);
        Reject(() => bad.Check("left"), "zero session nonce");

        Reject(() => ProbeManifest.Load(new byte[8193], "left"), "bounded embedded record");
        LifecycleControls();
        Console.WriteLine("{\"schema\":\"ghvr-steamlink-mask-controls-v1\",\"checks\":" + _checks
            + ",\"passed\":true,\"unity_callbacks_tested\":false,\"native_carriage_proved\":false}");
        return 0;
    }
}
