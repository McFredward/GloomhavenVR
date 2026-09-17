using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    private readonly ScenarioRetrySeat<ArrivalPose> _scenarioStart = new();

    private bool _retryBoardRestorePending;

    internal struct RetryBoardPose
    {
        internal Vector3 Position;
        internal Quaternion Rotation;
        internal float WorldScale;
    }

    private struct ArrivalPose
    {
        internal Vector3 Head;
        internal Quaternion HeadYaw;
        internal Quaternion BaseYaw;
        internal float Scale;
        internal bool HasAzimuth;
        internal float Azimuth;
        internal bool HasBoard;
        internal RetryBoardPose Board;
    }

    private void RememberScenarioStart()
    {
        if (_kind != RigKind.Scenario || _camera == null || _rigRoot == null)
            return;
        ArrivalPose previous = _scenarioStart.Pose;
        ArrivalPose next = new ArrivalPose
        {
            Head = _camera.transform.position,
            HeadYaw = YawOnly(_camera.transform.rotation),
            BaseYaw = _scenarioBaseYaw,
            Scale = _rigRoot.transform.localScale.x,
            HasAzimuth = _ringSeatAngleValid,
            Azimuth = _ringSeatAngleDegrees,
        };
        if (_scenarioStart.HasPose && previous.HasBoard)
        {
            // Refine the ORIGINAL board seat with the automatic head correction. Re-reading the
            // live board here would replace the baseline with a position the player just dragged.
            Quaternion turn = next.HeadYaw * Quaternion.Inverse(previous.HeadYaw);
            float scaleChange = next.Scale / previous.Scale;
            next.HasBoard = true;
            next.Board = previous.Board;
            next.Board.Position = next.Head + turn * ((previous.Board.Position - previous.Head) * scaleChange);
            next.Board.Rotation = turn * previous.Board.Rotation;
            next.Board.WorldScale = previous.Board.WorldScale * scaleChange;
        }
        _scenarioStart.CaptureArrival(next);
    }

    /// <summary>
    /// Retry used to erase the arrival azimuth and solve a fresh ring at the player's saved zoom,
    /// relative to peers who had also moved. Preserve the actual original head pose and scale,
    /// including the ring's final correction, instead. Apply only on the replacement scenario's
    /// first tracked pose; never from the result button while the old scene is still alive.
    /// This does not depend on tiles arriving or on stale peer packets during scene loading.
    /// </summary>
    private void RestoreScenarioStart()
    {
        ArrivalPose pose = _scenarioStart.Pose;
        Transform root = _rigRoot!.transform;
        Transform head = _camera!.transform;
        root.localScale = Vector3.one * pose.Scale;
        root.rotation = pose.HeadYaw * Quaternion.Inverse(YawOnly(head.localRotation));
        root.position = pose.Head - root.rotation * (head.localPosition * pose.Scale);
        _scenarioBaseYaw = pose.BaseYaw;
        _ringSeatAngleValid = pose.HasAzimuth;
        _ringSeatAngleDegrees = pose.Azimuth;
        _axisSnapReason = "scenario retry";
        RigClamp.Apply(root);
        RigPoseVersion++;
        _ringPlaced = true;
        _scenarioStart.CompleteRestore();
        CloseRingWindow("the explicit scenario retry restored the original arrival pose");
        // The same-frame reset reads the restored head and scale. If the tray has not been built,
        // its deferred placement consumes the saved board pose after native loading and adoption.
        // No native game state is changed.
        _retryBoardRestorePending = pose.HasBoard;
        Cards.PlayTray.RequestRetryReset();
        WorldUI.Surfaces.CombatLogSurface.RequestRecenterReset("scenario retry restored the original arrival pose");
        VRLog.Note("Rig", $"Scenario retry: restored original arrival head {pose.Head}, " +
                          $"yaw {pose.HeadYaw.eulerAngles.y:F1}, scale {pose.Scale:F2}; " +
                          "the spawn ring will not re-solve against moved peers.");
    }

    internal static void RememberRetryRequest(bool roundReload)
    {
        if (!VRSession.IsRunning || Instance == null)
            return;
        if (roundReload)
            Instance._scenarioStart.PreserveForRoundReload();
        else
            Instance._scenarioStart.RequestRetry();
    }

    internal static void RememberScenarioBoard(Vector3 position, Quaternion rotation, float worldScale)
    {
        VRRigDriver? driver = Instance;
        if (driver == null || !driver._scenarioStart.CanCapture || !driver._scenarioStart.HasPose
            || driver._scenarioStart.Pose.HasBoard || driver._kind != RigKind.Scenario)
            return;
        ArrivalPose pose = driver._scenarioStart.Pose;
        pose.HasBoard = true;
        pose.Board = new RetryBoardPose { Position = position, Rotation = rotation, WorldScale = worldScale };
        driver._scenarioStart.CaptureArrival(pose);
    }

    internal static bool TryTakeRetryBoard(out RetryBoardPose pose)
    {
        pose = default;
        VRRigDriver? driver = Instance;
        if (driver == null || !driver._retryBoardRestorePending || !driver._scenarioStart.HasPose)
            return false;
        driver._retryBoardRestorePending = false;
        pose = driver._scenarioStart.Pose.Board;
        return true;
    }

    internal static void ForgetRetryOnDestination(SceneController.ESceneType sceneType)
    {
        if (sceneType != SceneController.ESceneType.Scenario && Instance != null)
        {
            Instance._scenarioStart.LeaveScenario();
            Instance._retryBoardRestorePending = false;
        }
    }
}

/// <summary>
/// Both defeat callbacks reach these presentation-side entry points on every participant after
/// native ready-up. Patching only UIResultsManager.Retry would miss non-host clients. Ordinary
/// round restart preserves the baseline without requesting a teleport. Native methods run unchanged.
/// </summary>
[HarmonyPatch]
internal static class ScenarioRetryStart_Patch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SceneController), "RestartScenario");
        yield return AccessTools.Method(typeof(SceneController), "RestartScenarioFromInitial");
        yield return AccessTools.Method(typeof(SceneController), "RegenerateAndRestartScenario");
    }

    private static void Prefix(MethodBase __originalMethod) =>
        TickGuard.Run("Rig.RetryStart", () => VRRigDriver.RememberRetryRequest(__originalMethod.Name == "RestartScenario"));
}

/// <summary>A map/menu/error exit retires an interrupted retry before another scenario can enter.</summary>
[HarmonyPatch(typeof(SceneController), "LoadSceneCoroutine")]
internal static class ScenarioRetryDestination_Patch
{
    private static void Prefix(SceneController.ESceneType sceneType) => VRRigDriver.ForgetRetryOnDestination(sceneType);
}
