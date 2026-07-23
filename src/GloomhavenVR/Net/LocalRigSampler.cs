using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
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

        // Stamp the locally-chosen head mask (read live so changing it updates remotes at once).
        state.MaskId = (byte)LocalMaskId();

        // Stamp the locally-chosen hand style the same way (additive trailing byte on the
        // wire; old peers ignore it and render default Glove hands).
        state.HandStyle = (byte)HandVisuals.LocalStyle();

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

        // Which hand is dominant (mirror flag so remotes place the card fan on the correct side).
        state.DominantRight = LocalDominantRight();

        // Held figure (cosmetic): the figure the local player physically holds, in the shared
        // frame. NetFigures is a no-op stub in the foundation, so this is false until worker C.
        state.HasHeldFigure = NetFigures.TrySampleHeld(out state.HeldFigureActorId, out Vector3 fp, out Quaternion fr);
        if (state.HasHeldFigure)
        {
            anchor.ToAnchor(fp, fr, out Vector3 ap, out Quaternion ar);
            state.HeldFigurePose.Position = ap;
            state.HeldFigurePose.Rotation = ar;
        }

        // Held card (cosmetic, additive FlagHeldCard field): a single VRCard grip-held in
        // either hand (plucked from the fan or a pile viewer). Pose only — the card's
        // identity NEVER rides the wire (peers render a back slab; anti-cheat stance of
        // the remote fan). The open fan itself is covered by the extras packet's count.
        state.HasHeldCard = TrySampleHeldCard(out Vector3 cp, out Quaternion cr);
        if (state.HasHeldCard)
        {
            anchor.ToAnchor(cp, cr, out Vector3 acp, out Quaternion acr);
            state.HeldCardPose.Position = acp;
            state.HeldCardPose.Rotation = acr;
        }

        // Nothing to say if we have neither a head nor a tracked hand.
        return state.HeadValid || state.Left.Tracked || state.Right.Tracked;
    }

    /// <summary>
    /// Which hand is the local player's DOMINANT hand: the RIGHT hand unless the tracked
    /// non-dominant hand is the Right hand. Defaults true (right-dominant) when the non-dominant
    /// hand is unknown (controller absent / hot reload). Exposed so the driver can stamp the same
    /// value onto the extras packet.
    /// </summary>
    public static bool LocalDominantRight() => NonDominantHold.Hand?.Side != HandSide.Left;

    /// <summary>The locally-chosen mask id, clamped to [0, MaskCount-1]. Guarded so an unbound
    /// config (net module never inited) falls back to mask 0 rather than throwing.</summary>
    public static int LocalMaskId() =>
        NetModule.MaskId != null ? Mathf.Clamp(NetModule.MaskId.Value, 0, HeadMaskLibrary.MaskCount - 1) : 0;

    /// <summary>The world pose of the single card the local player grip-holds, if any (left
    /// hand wins when both hold one — matches the mirror's slab order). False when no hand
    /// holds a <see cref="Cards.VRCard"/>.</summary>
    private static bool TrySampleHeldCard(out Vector3 pos, out Quaternion rot)
    {
        return TryHeldCard(VRHands.Left, out pos, out rot) || TryHeldCard(VRHands.Right, out pos, out rot);
    }

    private static bool TryHeldCard(VRHand? hand, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        if (hand == null || hand.Grabber == null || hand.Grabber.Held is not Cards.VRCard card || card == null)
            return false;
        Transform t = card.transform;
        pos = t.position;
        rot = t.rotation;
        return true;
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
