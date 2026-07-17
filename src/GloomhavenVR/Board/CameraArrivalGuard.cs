using GloomhavenVR.Core;

namespace GloomhavenVR.Board;

/// <summary>
/// ROOT FIX for test #14 item 5 (hero-placement destination click dead): while VR
/// runs, the game camera never "arrives" — so camera-follow transitions that pause
/// the game never unpause it, and the paused clock blocks the hover-arming path.
///
/// The vanilla chain (all decompiled GH.Runtime, verified 2026-07-17):
/// <code>
///   // CameraTargetFocalFollowController.cs:53-63 — SetPoint(Vector3, bool):
///   if (pauseDuringTransition &amp;&amp; !TimeManager.IsPaused)
///   {   _pauseDuringTransition = true;  TimeManager.PauseTime();  }
///   _isFollowingTarget = true;
///   // :65-73 — OnArrivedToPoint(): _isFollowingTarget = false;
///   //   if (_pauseDuringTransition) { TimeManager.UnpauseTime(); ... }
///   // The ONLY caller of OnArrivedToPoint is the focal-point lerp inside
///   // CameraController.LateUpdate (CameraController.cs:1432-1435):
///   //   if (vector.sqrMagnitude &lt; 0.01f) _targetFocalFollowController.OnArrivedToPoint();
/// </code>
/// Pausing transitions are scripted all over the Choreographer, e.g. selecting a
/// hero on the initiative track during scenario-start card selection:
/// <c>SmartFocus(FindClientActorGameObject(playerActor), pauseDuringTransition: true)</c>
/// (Choreographer.cs:3950; also :4347/:4594/:4885/:5234/:6060/:9914) →
/// <c>CameraController.SmartFocus</c> → <c>SetPoint(position2, pauseDuringTransition)</c>
/// (CameraController.cs:575).
///
/// IN VR the rig owns the head pose and <c>CameraController.LateUpdate</c> is
/// prefix-skipped entirely (Rig/CameraControllerPatches.cs:30-34, CAMERA-POLICY §3)
/// — the arrival check can never run, so the FIRST pausing SmartFocus leaves
/// <c>TimeManager.IsPaused</c> true for the rest of the session. Clicks keep
/// working (<c>Controller.LateUpdate</c> dispatches via <c>CommonLoop(isPaused:
/// false)</c>, Controller.cs:168 — pause-independent), but
/// <c>WorldspaceStarHexDisplay.Update</c> requires <c>!TimeManager.IsPaused</c>
/// (WorldspaceStarHexDisplay.cs:425) before it may hover-arm
/// <c>Waypoint.s_PlacementTile</c> via HighlightSelectedPlacementHex (:436/:627) —
/// the exact fresh-log signature: TileHandler clicks with armed=null and the hover
/// diagnostic silent all session.
///
/// FIX: with a parked camera every transition is by definition already "arrived",
/// so each frame this guard completes any pending focal-follow transition through
/// the game's OWN arrival path (<c>OnArrivedToPoint()</c> — the balanced
/// <c>UnpauseTime()</c>, the follow-flag clear; no rules state touched, no
/// hand-rolled unpause). Zero-length pauses are a vanilla-reachable state
/// (SmartFocus early-outs without pausing when the target is on screen,
/// CameraController.cs:558-562). Deliberately NOT phase-gated: a leaked pause
/// freezes the 3D clock in every phase (enemy turns, attack focus), not just
/// placement. While VR is NOT running (flat / [Dev] mode) the guard is inert and
/// the vanilla LateUpdate owns arrival, 100% unchanged.
///
/// Polled (not a Harmony patch on SetPoint) on purpose: covers every caller, has
/// no Mono inline risk on the small SetPoint body, and also recovers a pause left
/// stuck from BEFORE a hot reload. All private members via the build-time
/// publicizer (csproj Publicize="true").
/// </summary>
internal static class CameraArrivalGuard
{
    /// <summary>Per-frame from <see cref="BoardDriver"/>. Event-driven log only.</summary>
    public static void Tick()
    {
        if (!VRSession.IsRunning)
            return; // vanilla CameraController.LateUpdate is running and owns arrival

        CameraController? controller = CameraController.s_CameraController;
        CameraTargetFocalFollowController? follow = controller != null
            ? controller._targetFocalFollowController
            : null;
        if (follow == null)
            return;

        // _pauseDuringTransition without _isFollowingTarget cannot happen in the
        // vanilla flow; checked anyway so a stuck pause is always recovered.
        if (!follow._isFollowingTarget && !follow._pauseDuringTransition)
            return;

        bool wasPausing = follow._pauseDuringTransition;
        follow.OnArrivedToPoint();
        VRLog.Info("Board", "[Placement] camera-follow transition completed instantly " +
                            $"(VR camera is parked): unpaused={wasPausing}, " +
                            $"TimeManager.IsPaused now {TimeManager.IsPaused}.");
    }
}
