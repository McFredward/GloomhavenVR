using GloomhavenVR.Core;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    private static float _nextStoryRecoveryPoll;

    /// <summary>The maintainer's753 report explicitly permits this narrow lost-story rescue.
    /// Only the common placement author moves it; native continuation remains untouched.</summary>
    private static void TickStoryRecovery(float now)
    {
        if (now < _nextStoryRecoveryPoll) return;
        _nextStoryRecoveryPoll = now + 0.25f;
        bool online = SharedWindows.SessionIsOnline();
        bool author = !online || SharedWindowSpawnBridge.IsInitialAuthor;
        Camera? head = CanvasConversion.WorldCamera;
        bool anyEligible = false;
        foreach (WindowPanel wp in Converted)
        {
            SharedWindowKind kind = SharedWindows.KindOf(wp.Window);
            if (kind is not (SharedWindowKind.ScenarioStory or SharedWindowKind.MapStory)) continue;
            if (head != null && StoryRecoveryEligible(wp, author)) anyEligible = true;
            else wp.StoryRecovery.Tick(false, false, false, now);
        }
        // A tile footprint is an O(tiles) engine-boundary walk. Do no such work
        // during ordinary scenario play, on observers or for hidden/held stories.
        if (!anyEligible || head == null) return;
        float reference = SharedWindowSize.ReferenceWorldScale;
        Bounds playfield;
        if (MapRoom.MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float mapScale))
            playfield = new Bounds(center, Vector3.one * mapScale * 1.2f);
        else if (CameraController.s_CameraController != null
            && SpawnRing.TryFootprint(CameraController.s_CameraController.FocusPoint.y, out SpawnRing.Footprint board))
            playfield = new Bounds(board.Center,
                new Vector3(board.OuterHalfWorld.x * 2f, 0f, board.OuterHalfWorld.y * 2f));
        else return; // no measured playfield during loading: no speculative recovery

        foreach (WindowPanel wp in Converted)
        {
            SharedWindowKind kind = SharedWindows.KindOf(wp.Window);
            if (kind is not (SharedWindowKind.ScenarioStory or SharedWindowKind.MapStory)) continue;
            bool eligible = StoryRecoveryEligible(wp, author);
            Vector3 old = wp.Panel.HostGo != null ? wp.Panel.HostGo.transform.position : Vector3.zero;
            bool far = playfield.SqrDistance(old) > reference * reference * 4f;
            bool changed = (old - wp.StoryRecoverySeat).sqrMagnitude > reference * reference * 0.0001f;
            if (!wp.StoryRecovery.Tick(eligible, far, changed, now) || wp.Grab == null) continue;
            // The existing opening/gesture register rejects every current remote grip.
            // The corrected pose is published through the ordinary story pose stream.
            if (online && !SharedWindowMoves.BeginAutomatic(kind)) continue;
            try
            {
                Vector3 forward = head.transform.forward; forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
                forward.Normalize();
                Vector3 target = head.transform.position + forward * (WindowDistanceMeters * reference);
                PanelPoseWatch.Announce(wp.Panel, PanelPoseWatch.Writer.StoryRecovery,
                    "progression story remained far outside the measured playfield for three seconds");
                wp.Grab.SnapFrameTo(target, Quaternion.LookRotation(forward, Vector3.up));
                wp.SpawnAnchor = default;
                wp.PoseRePlaceDone = true;
                wp.ReflowCancelled = true;
                wp.StoryRecoverySeat = target;
                wp.StoryRecovery.Recovered();
                VRLog.Note("WorldUI", $"STORY WINDOW RECOVERED: kind={kind}, "
                    + $"old={old}, position={target}; common placement author restored reachability, "
                    + "native content and continuation unchanged.");
            }
            finally { if (online) SharedWindowInteractionBridge.Released?.Invoke(kind); }
        }
    }

    private static bool StoryRecoveryEligible(WindowPanel wp, bool author) => author
        && wp.Window != null && wp.Window.IsOpen && ReflowVisible(wp)
        && wp.Grab != null && !wp.Grab.IsGrabbed;
}
