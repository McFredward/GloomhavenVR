using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Samples the LOCAL VR rig (owned head camera + the two <see cref="VRHand"/>s) into an
/// <see cref="AvatarState"/> expressed in the shared frame. Read-only w.r.t. the rig — it
/// only reads transforms the Rig/Hands modules own. Returns false when there is nothing
/// worth sending (no rig / no head), so the caller sends nothing and flat peers stay quiet.
/// </summary>
internal static class LocalRigSampler
{
    public static bool TrySample(IBoardAnchor anchor, bool includeFingers, out AvatarState state)
    {
        state = default;

        // Sender scale: live rig lossyScale (accounts for pinch-zoom) so the receiver can
        // size the floating hands to the same physical size above the shared board.
        Transform? rigRoot = VRRigDriver.RigRoot;
        state.WorldScale = rigRoot != null ? rigRoot.lossyScale.x : 1f;
        if (!(state.WorldScale > 0f))
            state.WorldScale = 1f;

        Camera? head = VRRigDriver.HeadCamera;
        if (head != null)
        {
            Transform t = head.transform;
            anchor.ToAnchor(t.position, t.rotation, out Vector3 hp, out Quaternion hr);
            state.Head.Position = hp;
            state.Head.Rotation = hr;
            state.HeadValid = true;
        }

        state.HasFingers = includeFingers;
        SampleHand(anchor, VRHands.Left, includeFingers, ref state.Left);
        SampleHand(anchor, VRHands.Right, includeFingers, ref state.Right);

        // Nothing to say if we have neither a head nor a tracked hand.
        return state.HeadValid || state.Left.Tracked || state.Right.Tracked;
    }

    private static void SampleHand(IBoardAnchor anchor, VRHand? hand, bool includeFingers, ref HandStateSample sample)
    {
        if (hand == null || !hand.IsTracked || hand.Rig == null || hand.Rig.Root == null)
        {
            sample.Tracked = false;
            return;
        }

        Transform t = hand.Rig.Root;
        anchor.ToAnchor(t.position, t.rotation, out Vector3 p, out Quaternion r);
        sample.Tracked = true;
        sample.Pose.Position = p;
        sample.Pose.Rotation = r;

        if (includeFingers)
        {
            sample.Curl0 = hand.GetCurl(Finger.Thumb);
            sample.Curl1 = hand.GetCurl(Finger.Index);
            sample.Curl2 = hand.GetCurl(Finger.Middle);
            sample.Curl3 = hand.GetCurl(Finger.Ring);
            sample.Curl4 = hand.GetCurl(Finger.Pinky);
        }
    }
}
