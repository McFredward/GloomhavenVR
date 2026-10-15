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
        Require(pending.Tick(false, true, true, false, 1) == SteamLinkMaskStep.Idle, "disabled leaves normal rendering");
        Require(pending.Tick(true, true, true, false, 2) == SteamLinkMaskStep.Prepare, "explicit setting prepares once while options visible");
        Require(!pending.StopForOptionsOpening(), "pending request survives own options OnShow");
        Require(pending.Tick(true, true, true, false, 90000) == SteamLinkMaskStep.WaitForOptions, "options waiting never spends output timer");
        Require(pending.Tick(true, false, true, false, 90001) == SteamLinkMaskStep.Ready, "menu closure admits start");
        Require(pending.Policy.Start(90001, true, true, true, true, true), "real policy begins only after closure");
        Require(pending.Tick(true, false, true, true, 90002) == SteamLinkMaskStep.Continue, "running same current camera continues");
        Require(pending.StopForOptionsOpening(), "synchronous options open interrupts active output");
        Require(pending.Policy.Phase == ProbePhase.Stopped, "synchronous interruption closes render admission");
        Require(pending.Tick(true, false, true, true, 90003) == SteamLinkMaskStep.Stop, "closed policy never rearms unchanged request");
        foreach (int cause in new[] { 0, 1, 2, 3, 4 })
        {
            var run = new SteamLinkMaskLifecycle();
            run.Tick(true, true, true, false, 10);
            run.Policy.Start(20, true, true, true, true, true);
            bool requested = cause != 0, menu = cause == 1, supported = cause != 2, sameCamera = cause != 3;
            double now = cause == 4 ? 50 : 21;
            Require(run.Tick(requested, menu, supported, sameCamera, now) == SteamLinkMaskStep.Stop,
                    "disable/menu/VR loss/camera replacement/deadline request termination");
        }
        Require(new SteamLinkMaskLifecycle().Tick(true, false, false, false, 1) == SteamLinkMaskStep.Stop,
                "standalone or unavailable VR rejects without preparing resources");
        var cancelledPending = new SteamLinkMaskLifecycle();
        cancelledPending.Tick(true, true, true, false, 1);
        Require(cancelledPending.Tick(false, true, true, false, 2) == SteamLinkMaskStep.Stop,
                "disable while options open cancels pending preparation");
        object retiredDriver = new object(), currentDriver = new object();
        bool freshRequest = true;
        if (SteamLinkMaskCallbackGate.OwnsRequest(currentDriver, retiredDriver)) freshRequest = false;
        Require(freshRequest, "deferred retired driver teardown cannot clear a new installed driver's request");
        Require(SteamLinkMaskCallbackGate.OwnsRequest(currentDriver, currentDriver), "current driver can clear its own request");
        Require(!SteamLinkMaskCallbackGate.OwnsRequest(null, retiredDriver), "unloaded driver owns no future request");
        var retired = new ProbePolicy();
        retired.Start(1, true, true, true, true, true);
        retired.Stop();
        var newRun = new ProbePolicy();
        newRun.Start(10, true, true, true, true, true);
        Require(!SteamLinkMaskCallbackGate.Admit(newRun, retired, false, false, false, 999, out bool stopNew)
            && !stopNew && newRun.Phase == ProbePhase.Running && newRun.StartedAt == 10,
            "deferred retired callback cannot cancel or expire freshly enabled output");
        Require(SteamLinkMaskCallbackGate.Admit(newRun, newRun, true, true, true, 11, out stopNew) && !stopNew,
            "current component continues after retired callback");
        Require(!SteamLinkMaskCallbackGate.Admit(newRun, newRun, false, true, false, 999, out stopNew)
            && !stopNew && newRun.Phase == ProbePhase.Running,
            "foreign component cannot mutate current owner policy");
        Require(!SteamLinkMaskCallbackGate.Admit(newRun, newRun, true, true, false, 12, out stopNew)
            && stopNew && newRun.Phase == ProbePhase.Stopped,
            "current callback menu/disable/VR guard immediately closes output before Update");
        var fresh = new SteamLinkMaskLifecycle();
        Require(fresh.Tick(true, true, true, false, 100) == SteamLinkMaskStep.Prepare,
                "fresh explicit request supports another run after cleanup");
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1) throw new Exception("Supply the original local payload directory.");
        for (int mask = 0; mask < 32; mask++)
        {
            var policy = new ProbePolicy();
            bool started = policy.Start(10, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                                        (mask & 8) != 0, (mask & 16) != 0);
            Require(started == (mask == 31), "all five start gates");
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d })
            Require(!new ProbePolicy().Start(invalid, true, true, true, true, true), "invalid start time");
        for (int mask = 0; mask < 64; mask++)
            foreach (ProbeEye eye in new[] { ProbeEye.Unknown, ProbeEye.Left, ProbeEye.Right, (ProbeEye)99 })
            {
                var policy = new ProbePolicy();
                Require(policy.Start(10, true, true, true, true, true), "prepare valid running policy");
                ProbeEye actual = policy.Select(11, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                    (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0, eye);
                ProbeEye expected = mask == 63 && (eye == ProbeEye.Left || eye == ProbeEye.Right) ? eye : ProbeEye.Unknown;
                Require(actual == expected, "camera/XR/both-eye/eye admission matrix");
            }
        foreach (double expired in new[] { 40d, 41d, 9d, double.NaN, double.PositiveInfinity })
        {
            var policy = new ProbePolicy();
            policy.Start(10, true, true, true, true, true);
            Require(!policy.IsRunning(expired), "deadline or invalid clock stops output");
            Require(policy.Phase == ProbePhase.Stopped, "stop is permanent");
            Require(!policy.Start(200, true, true, true, true, true), "no rearm after stop");
        }
        var one = new ProbePolicy();
        Require(one.Phase == ProbePhase.Prepared && !one.IsRunning(99999), "waiting for F8 consumes no run window");
        Require(one.Start(100000, true, true, true, true, true), "F8 start after arbitrary preparation wait");
        Require(one.IsRunning(100029.999), "not expired before original deadline");
        Require(!one.Start(100010, true, true, true, true, true), "second start cannot reset deadline");
        Require(one.StartedAt == 100000, "original time remains immutable");
        one.Stop();
        Require(one.Select(100011, true, true, true, true, true, true, ProbeEye.Left) == ProbeEye.Unknown,
                "manual stop immediately uses source fallback");

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
