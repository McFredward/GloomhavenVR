using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GloomhavenVR.Core;
using GloomhavenVR.Core.SteamLink;
using UnityEngine;
using UnityEngine.XR;

internal static class Program
{
    private static int _checks;
    private static ControlledReader _reader = null!;
    private static readonly InputDevice Head = new(7, InputDeviceCharacteristics.HeadMounted);
    private static readonly InputDevice Controller = new(11, InputDeviceCharacteristics.Controller);
    private const string CameraRegression = "CAMERA_LOADING_REGRESSION: camera/loading must not revoke a proven Frame connection";

    private sealed class ControlledReader
    {
        internal readonly List<TaskCompletionSource<SteamLinkConnection.Snapshot>> Results = new();
        internal readonly List<CancellationToken> Tokens = new();
        internal Task<SteamLinkConnection.Snapshot> Read(CancellationToken token)
        {
            Tokens.Add(token);
            var result = new TaskCompletionSource<SteamLinkConnection.Snapshot>();
            Results.Add(result);
            return result.Task;
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private static void Tick(float time)
    { Time.unscaledTime = time; SteamLinkConnectionLifecycle.Tick(); }
    private static void Initialize(bool camera = true)
    {
        SteamLinkConnectionLifecycle.Reset();
        Check(InputDevices.ConnectedObservers == 0 && InputDevices.DisconnectedObservers == 0
            && InputDevices.ConfiguredObservers == 0, "reset removes every XR observer");
        _reader = new ControlledReader();
        SteamLinkConnection.State = new SteamLinkConnection.Monitor(_reader.Read);
        Application.platform = RuntimePlatform.WindowsPlayer;
        VRSession.IsRunning = true;
        VRSession.RuntimeName = "SteamVR/OpenXR";
        FrameNativePassthrough.Required = false;
        VRCameraPolicy.AllowedHead = camera ? new Camera() : null;
        InputDevices.Head = Head;
    }
    private static SteamLinkConnection.Snapshot Ready() =>
        SteamLinkConnection.Classify(true, 1, "cv", "Deckard MP", "");
    private static void BeginReady()
    {
        Initialize();
        Tick(0);
        Check(_reader.Results.Count == 1 && SteamLinkConnection.Current.State == SteamLinkConnection.Reason.Pending,
            "eligible session starts one asynchronous identity read");
        Check(SteamLinkConnectionLifecycle.Checking, "checking is visible while the eligible session is pending");
        _reader.Results[0].SetResult(Ready());
        Tick(.01f);
        Check(SteamLinkConnection.Available && !SteamLinkConnectionLifecycle.Checking,
            "actual classifier result is published and ends checking");
    }
    private static void CameraLoads()
    {
        BeginReady();
        for (int load = 0; load != 4; load++)
        {
            VRCameraPolicy.AllowedHead = null;
            Tick(load * 10 + 1);
            Check(SteamLinkConnection.Available, CameraRegression);
            Check(!SteamLinkConnectionLifecycle.Checking, "scene camera absence is not another hardware check");
            VRCameraPolicy.AllowedHead = new Camera { isActiveAndEnabled = false };
            Tick(load * 10 + 2);
            Check(SteamLinkConnection.Available, CameraRegression);
            VRCameraPolicy.AllowedHead.isActiveAndEnabled = true;
            Tick(load * 10 + 3);
            Check(SteamLinkConnection.Available, CameraRegression);
            VRCameraPolicy.AllowedHead = new Camera();
            Tick(load * 10 + 4);
            Check(SteamLinkConnection.Available && _reader.Results.Count == 1,
                "replacement/reenabled loading cameras reuse the proven identity without child requery");
        }
        Check(InputDevices.ConnectedObservers == 1 && InputDevices.DisconnectedObservers == 1
            && InputDevices.ConfiguredObservers == 1, "repeated scene loads never multiply XR observers");
    }
    private static void CameraIndependentStartup()
    {
        Initialize(false);
        Tick(0);
        Check(_reader.Results.Count == 1 && SteamLinkConnectionLifecycle.Checking,
            "identity lookup starts before the scene head camera exists");
        _reader.Results[0].SetResult(Ready());
        Tick(.1f);
        Check(SteamLinkConnection.Available, "initial asynchronous identity can resolve with no head camera");
        VRCameraPolicy.AllowedHead = new Camera { isActiveAndEnabled = false };
        Tick(.2f);
        Check(SteamLinkConnection.Available && _reader.Results.Count == 1,
            "initial disabled scene head camera does not revoke ready identity");
    }
    private static void ControllerEvents()
    {
        BeginReady();
        InputDevices.Connect(Controller);
        InputDevices.Disconnect(Controller);
        InputDevices.Configure(Controller);
        Tick(3);
        Check(SteamLinkConnection.Available && _reader.Results.Count == 1,
            "all controller event kinds leave the HMD identity untouched");
        InputDevices.Disconnect(new InputDevice(200, InputDeviceCharacteristics.None, false));
        Tick(4);
        Check(SteamLinkConnection.Available, "unrelated invalid device removal is ignored");
    }
    private static void TrueHeadEvents()
    {
        Action<InputDevice>[] events = { InputDevices.Connect, InputDevices.Disconnect, InputDevices.Configure };
        foreach (Action<InputDevice> notify in events)
        {
            BeginReady();
            notify(Head);
            Check(!SteamLinkConnection.Available && SteamLinkConnectionLifecycle.Checking,
                "true HMD event immediately invalidates the positive identity");
            Tick(1);
            Check(_reader.Results.Count == 1, "HMD event keeps the original query rate limit");
            Tick(2);
            Check(_reader.Results.Count == 2, "HMD event starts exactly one fresh lookup after its rate limit");
            _reader.Results[1].SetResult(Ready());
            Tick(2.1f);
            Check(SteamLinkConnection.Available && !SteamLinkConnectionLifecycle.Checking,
                "new HMD epoch may regain availability from its own result");
        }
        BeginReady();
        InputDevices.Disconnect(new InputDevice(Head.Id, InputDeviceCharacteristics.None, false));
        Check(!SteamLinkConnection.Available && SteamLinkConnectionLifecycle.Checking,
            "removed current HMD remains recognizable after losing characteristics");
    }
    private static void StaleAsyncResults()
    {
        Initialize(false);
        Tick(0);
        Check(_reader.Results.Count == 1, "stale-result case starts actual asynchronous monitor");
        InputDevices.Configure(Head);
        Check(_reader.Tokens[0].IsCancellationRequested, "HMD replacement cancels the outstanding reader");
        _reader.Results[0].SetResult(Ready());
        Tick(.2f);
        Check(!SteamLinkConnection.Available && SteamLinkConnectionLifecycle.Checking,
            "late positive result from the old HMD epoch is never published");
        Tick(2);
        Check(_reader.Results.Count == 2, "old result is reaped before fresh identity lookup");
        _reader.Results[1].SetResult(SteamLinkConnection.Classify(true, 1, "virtualdesktop", "Steam Frame", "vd/device"));
        Tick(2.1f);
        Check(SteamLinkConnection.Current.State == SteamLinkConnection.Reason.WrongTransport
            && !SteamLinkConnectionLifecycle.Checking,
            "fresh unsupported transport stays unavailable and is no longer pending");
    }
    private static void SessionBoundaries()
    {
        Action[] endSession = {
            () => VRSession.IsRunning = false,
            () => VRSession.RuntimeName = "Other/OpenXR",
            () => Application.platform = RuntimePlatform.LinuxPlayer,
            () => FrameNativePassthrough.Required = true
        };
        foreach (Action stop in endSession)
        {
            BeginReady();
            stop(); Tick(1);
            Check(SteamLinkConnection.Current.State == SteamLinkConnection.Reason.NoSession
                && !SteamLinkConnectionLifecycle.Checking, "real eligibility loss resets connection and checking");
            Check(InputDevices.ConnectedObservers == 0 && InputDevices.DisconnectedObservers == 0
                && InputDevices.ConfiguredObservers == 0, "real eligibility loss unsubscribes XR callbacks");
            InputDevices.Configure(Head);
            Check(SteamLinkConnection.Current.State == SteamLinkConnection.Reason.NoSession,
                "unsubscribed events cannot resurrect or mutate a stopped session");
            Application.platform = RuntimePlatform.WindowsPlayer;
            VRSession.IsRunning = true; VRSession.RuntimeName = "steamvr/openxr";
            FrameNativePassthrough.Required = false;
            VRCameraPolicy.AllowedHead = null;
            Tick(3);
            Check(_reader.Results.Count == 2 && SteamLinkConnectionLifecycle.Checking,
                "real session restart requires a fresh identity even without a scene camera");
            _reader.Results[1].SetResult(Ready()); Tick(3.1f);
            Check(SteamLinkConnection.Available, "restarted eligible session accepts its current identity result");
        }
        Initialize(); Tick(0); VRSession.IsRunning = false; Tick(.1f);
        Check(_reader.Tokens[0].IsCancellationRequested, "session stop cancels an outstanding child request");
        _reader.Results[0].SetResult(Ready());
        VRSession.IsRunning = true; Tick(3);
        Check(!SteamLinkConnection.Available && _reader.Results.Count == 2,
            "late stopped-session ready result cannot bypass restart identity lookup");
    }
    public static int Main()
    {
        try
        {
            CameraLoads(); CameraIndependentStartup(); ControllerEvents();
            TrueHeadEvents(); StaleAsyncResults(); SessionBoundaries();
            SteamLinkConnectionLifecycle.Reset();
            Check(!SteamLinkConnectionLifecycle.Checking && !SteamLinkConnection.Available,
                "final lifecycle reset restores ordinary unsupported-safe state");
            Check(InputDevices.ConnectedObservers + InputDevices.DisconnectedObservers + InputDevices.ConfiguredObservers == 0,
                "all fixture callbacks are reaped");
            Console.WriteLine("PASS " + _checks + " lifecycle/actual-monitor assertions; Unity/XR/session are explicit boundaries.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return error.Message == CameraRegression ? 7 : 1;
        }
    }
}
