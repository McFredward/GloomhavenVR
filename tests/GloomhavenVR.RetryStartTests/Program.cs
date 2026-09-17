using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Cards;
using GloomhavenVR.Rig;
using UnityEngine;

static class Program
{
    static int assertions;
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    static void Near(float a, float b, string message) => Check(MathF.Abs(a - b) < 0.001f, message);
    static void Near(Vector3 a, Vector3 b, string message)
    {
        Near(a.x, b.x, message); Near(a.y, b.y, message); Near(a.z, b.z, message);
    }
    static void Main()
    {
        var state = new ScenarioRetrySeat<int>();
        var original = new object();
        Check(state.EnterScenario(original), "First scenario must enter");
        Check(!state.HasPose, "First scenario must not inherit a pose");
        state.CaptureArrival(11);
        state.CaptureArrival(12);
        Check(state.Pose == 12, "Final ring refinement must replace the provisional seat");
        Check(!state.EnterScenario(original), "Camera rebuild must retain scenario identity");
        Check(state.Pose == 12, "Camera rebuild must preserve original seat");
        state.RequestRetry(); state.RequestRetry();
        state.CaptureArrival(99);
        Check(state.Pose == 12, "Teardown callbacks must not replace the original arrival");
        Check(!state.EnterScenario(original), "Retry must not apply in the old scenario");
        Check(!state.RestorePending, "Old scenario must not consume retry early");
        Check(state.EnterScenario(new object()) && state.RestorePending, "New scenario must restore an armed retry");
        state.CaptureArrival(99);
        Check(state.Pose == 12, "Replacement rig must not overwrite a pending restore");
        state.CompleteRestore();
        Check(!state.RestorePending && state.Pose == 12, "Completed restore must retain the original for repeated retries");
        state.PreserveForRoundReload(); state.EnterScenario(new object());
        Check(state.HasPose && !state.RestorePending && state.Pose == 12, "Round reload must preserve the original without requesting a teleport");
        state.CaptureArrival(500);
        Check(state.Pose == 12, "Round reload arrival must not replace original level start");
        state.RequestRetry(); state.EnterScenario(new object());
        Check(state.RestorePending && state.Pose == 12, "Defeat after round reload must return to original level start");
        state.CompleteRestore();
        state.RequestRetry(); state.LeaveScenario(); state.EnterScenario(new object());
        Check(!state.HasPose && !state.RestorePending, "Map/menu exit must cancel interrupted retry");
        state.RequestRetry(); state.EnterScenario(new object());
        Check(!state.RestorePending, "Retry without a captured arrival must use normal spawning");
        state.CaptureArrival(18); state.EnterScenario(new object());
        Check(!state.HasPose && !state.RestorePending, "Ordinary scenario entry must discard the previous seat");

        // Each participant has their own original head position. Retry must not depend on host
        // authority, fresh peer packets, physical headset orientation, or the latest saved zoom.
        for (int player = 0; player < 4; player++)
        {
            var driver = new VRRigDriver(); VRRigDriver.Instance = driver;
            object scenario = new(); driver.Enter(scenario);
            driver.Pose(new Vector3(15 * player, 0, -12), 90 * player, 20, new Vector3(.12f, 1.6f, -.08f), 14);
            driver.Capture();
            Vector3 start = driver.HeadPosition;
            Quaternion startYaw = driver.HeadYaw;
            var tray = new PlayTray(); PlayTray.Current = tray;
            Vector3 boardStart = start + new Vector3(-12, -5, 1);
            Quaternion boardYaw = Quaternion.Yaw(63);
            tray.Root.position = boardStart; tray.Root.rotation = boardYaw;
            tray.Root.localScale = Vector3.one * 16;
            tray.Capture();
            tray.Root.position = new Vector3(999, 200, -100); tray.Capture();

            typeof(ScenarioRetryStart_Patch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                new object[] { typeof(SceneController).GetMethod("RestartScenario")! });
            scenario = new object(); driver.Enter(scenario);
            Check(!driver.Pending, "Native round restart must preserve without restoring the retry seat");
            driver.Pose(new Vector3(-150, 0, 92), 140, 31, new Vector3(.2f, 1.8f, .3f), 35);
            driver.Capture(); // Normal round-reload arrival must not overwrite original level start.
            for (int retry = 0; retry < 3; retry++)
            {
                driver.Pose(new Vector3(300, -100, 80), 77, 47, new Vector3(-.3f, 1.75f, .4f), -48);
                // Invoke the production Harmony prefix, as the native ready-up callback does on
                // hosts and non-hosts. No host flag exists in this presentation path.
                typeof(ScenarioRetryStart_Patch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { typeof(SceneController).GetMethod(player % 2 == 0 ? "RestartScenarioFromInitial" : "RegenerateAndRestartScenario")! });
                driver.Enter(scenario);
                Check(!driver.Pending, "Retry callback must leave the old moved rig alone");
                scenario = new object(); driver.Enter(scenario);
                Check(driver.Pending, "Every participant must arm restoration in the replacement scenario");
                int boardCalls = GloomhavenVR.Cards.PlayTray.Calls;
                int version = VRRigDriver.RigPoseVersion;
                Check(!tray.Restore(), "Outgoing tray must not consume a new scenario restore");
                if (retry != 1) tray.Bind();
                if (retry == 1) PlayTray.Current = null; // The new tray can arrive after the rig.
                driver.Restore();
                if (retry == 1)
                {
                    Check(PlayTray.Calls == boardCalls, "Missing tray must retain pending restore without publishing a reset");
                    Check(!tray.Restore(), "Outgoing tray must not consume a new scenario restore");
                    tray = new PlayTray { Pinned = player % 2 == 0 }; PlayTray.Current = tray;
                    CardsDriver.NativeSceneLoadInProgress = true;
                    Check(!tray.Restore(), "Native loading must defer board restore");
                    CardsDriver.NativeSceneLoadInProgress = false;
                    tray.SetGrabbed(true);
                    Check(!tray.Restore(), "In-hand retry board must defer without consuming pose");
                    tray.SetGrabbed(false);
                    Check(tray.Restore(), "Deferred recreated tray must restore original board pose");
                }
                Check(!tray.Restore(), "Each board restore must be consumed once");
                Near(tray.Root.position, boardStart, "Retry must restore actual original board world position");
                Near(tray.Root.rotation * new Vector3(0, 0, 1), boardYaw * new Vector3(0, 0, 1), "Retry must restore actual original board orientation");
                Near(tray.Root.lossyScale.x, 16, "Retry must preserve original board world size across follow and pinned parents");
                Check(tray.ArrivalDisarmed && tray.Placed, "Retry board must prevent a later arrival guard from re-solving it");
                Near(tray.Pin.Position, boardStart, "Pinned board must recache original world pose");
                Check(tray.Pin.Version == version + 1, "Pinned board must reauthor against new rig version");
                Near(driver.HeadPosition, start, "Retry must restore the original head world position");
                Near(driver.HeadYaw * new Vector3(0, 0, 1), startYaw * new Vector3(0, 0, 1), "Retry must absorb changed physical headset yaw");
                Near(driver.Scale, 20, "Retry must restore original scale instead of the moved/saved zoom");
                Check(driver.Placed && !driver.Pending && driver.Closed == retry + 1, "Restoration must close the ring before late peers can move it");
                Check(driver.SnapReason == "scenario retry", "Retry must re-aim world tilt at the restored seat");
                Check(GloomhavenVR.Cards.PlayTray.Calls == boardCalls + 1, "Retry must reset the board once");
                Near(GloomhavenVR.Cards.PlayTray.HeadAtReset, start, "Board must read the restored head in the same frame");
                Near(GloomhavenVR.Cards.PlayTray.ScaleAtReset, 20, "Board must read the restored scale");
                Check(GloomhavenVR.Cards.PlayTray.VersionAtReset == version + 1, "Board must capture the new rig version");
            }
            VRRigDriver.RememberRetryRequest(false);
            VRRigDriver.ForgetRetryOnDestination(SceneController.ESceneType.Scenario);
            driver.Enter(new object()); Check(driver.Pending, "Scenario load must keep retry armed");
            VRRigDriver.ForgetRetryOnDestination(SceneController.ESceneType.MainMenu);
            Check(!driver.HasPose && !driver.Pending, "Error/menu load must retire a pending retry");
        }
        // An automatic late ring refinement moves the original board baseline with the original
        // seat, even if the live board has already been grabbed and moved elsewhere.
        var correction = new VRRigDriver(); VRRigDriver.Instance = correction;
        correction.Enter(new object());
        correction.Pose(new Vector3(0, 0, 0), 0, 20, new Vector3(0, 1.5f, 0), 0);
        correction.Capture();
        var correctedTray = new PlayTray(); PlayTray.Current = correctedTray;
        correctedTray.Root.position = new Vector3(-10, 24, 2);
        correctedTray.Root.rotation = Quaternion.Yaw(15);
        correctedTray.Root.localScale = Vector3.one * 8;
        correctedTray.Capture();
        correctedTray.Root.position = new Vector3(900, 900, 900);
        correctedTray.Root.localScale = Vector3.one * 500;
        correctedTray.SetGrabbed(true);
        correction.Pose(new Vector3(30, 0, -12), 90, 20, new Vector3(0, 1.5f, 0), 0);
        correction.Capture();
        Vector3 correctedBoard = correction.HeadPosition + Quaternion.Yaw(90) * new Vector3(-10, -6, 2);
        VRRigDriver.RememberRetryRequest(false);
        correction.Enter(new object()); correctedTray.Bind(); correctedTray.SetGrabbed(false);
        correction.Restore();
        Near(correctedTray.Root.position, correctedBoard, "Late ring correction must carry original board without adopting the dragged board");
        Near(correctedTray.Root.lossyScale.x, 8, "Late ring correction must retain original board size");
        Near(correctedTray.Root.rotation * new Vector3(0, 0, 1), Quaternion.Yaw(105) * new Vector3(0, 0, 1), "Late ring correction must carry original board heading");
        var fresh = new VRRigDriver(); VRRigDriver.Instance = fresh;
        fresh.Enter(new object());
        fresh.Pose(new Vector3(0, 0, 0), 0, 20, new Vector3(0, 1.5f, 0), 0); fresh.Capture();
        correctedTray.Root.position = new Vector3(888, 888, 888);
        correctedTray.Capture(); // Old/map owner must not seed this new scenario's missing board.
        var freshTray = new PlayTray(); PlayTray.Current = freshTray;
        freshTray.Root.position = new Vector3(-9, 25, 0); freshTray.Capture();
        VRRigDriver.RememberRetryRequest(false); fresh.Enter(new object()); freshTray.Bind(); fresh.Restore();
        Near(freshTray.Root.position, new Vector3(-9, 25, 0), "Outgoing/map tray must not seed new scenario board baseline");
        var flat = new VRRigDriver(); VRRigDriver.Instance = flat;
        flat.Enter(new object()); flat.Capture(); VRSession.IsRunning = false;
        VRRigDriver.RememberRetryRequest(false); flat.Enter(new object());
        Check(!flat.Pending, "Flat play must not arm VR retry placement");
        VRSession.IsRunning = true;
        var targets = (IEnumerable<MethodBase>)typeof(ScenarioRetryStart_Patch).GetMethod("TargetMethods", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        var names = new HashSet<string>(); foreach (var method in targets) names.Add(method.Name);
        Check(names.SetEquals(new[] { "RestartScenario", "RestartScenarioFromInitial", "RegenerateAndRestartScenario" }), "Both defeat callbacks and preserve-only round restart must be covered");
        Console.WriteLine($"Retry start: {assertions} runtime assertions passed.");
    }
}
