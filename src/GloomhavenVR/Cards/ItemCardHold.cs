using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>A physical item inspection whose presentation is published outside the scenario
/// inventory stream. This capability shares hand behavior, never item-use authority.</summary>
internal interface IItemCardHold
{
    bool IsItemCard { get; }
    Transform? HeldRoot { get; }
    bool Transfer(VRHand from, VRHand to);
    bool TryTouch(Vector3 point, out float distance);
}

/// <summary>The existing scenario item reading/grasp solver. Merchant samples use this exact
/// implementation rather than a second hand-relative pose with different scale and controls.</summary>
internal static class ItemCardHold
{
    internal static void ReadingPose(VRHand hand, float height, float fraction,
        out Vector3 position, out Quaternion rotation)
    {
        FingerJoints thumb = hand.Rig.GetFinger(Finger.Thumb), index = hand.Rig.GetFinger(Finger.Index);
        Vector3 pinch = thumb.IsValid && index.IsValid
            ? hand.Rig.GrabAnchor.InverseTransformPoint((thumb.Tip.position + index.Tip.position) * .5f)
            : new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
        float side = Board.FigureGrab.HeldPoseMirror.OffsetSign(hand.Side == HandSide.Left);
        Vector3 offset = CardsConfig.HeldPinchOffset.Value; offset.x *= side;
        CardGripPose.ReadingPose(CardsConfig.HeldFaceBias.Value, side, pinch + offset,
            height, fraction, out position, out rotation);
    }

    internal static void Tick(Transform card, VRHand hand, Transform socket, Vector3 readingPosition,
        float targetScale, float width, float height)
    {
        // Keep the original scenario interpolation and clock. Both callers must behave alike.
        float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * 1.5f * Time.deltaTime);
        float grasp = HeldCardGrip.Blend(hand);
        Vector3 position = readingPosition;
        Quaternion localRotation = Quaternion.Inverse(socket.rotation) * card.rotation;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = card.position - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                localRotation = Quaternion.Inverse(socket.rotation)
                    * Quaternion.LookRotation(away.normalized, head.transform.up);
        }
        if (grasp > 0f && HeldCardGrip.TryPose(hand, width, height, out Vector3 grip, out Quaternion rotation))
        {
            position = Vector3.Lerp(position, grip, grasp);
            localRotation = Quaternion.Slerp(localRotation, rotation, grasp);
        }
        card.position = socket.TransformPoint(Vector3.Lerp(socket.InverseTransformPoint(card.position), position, t));
        card.rotation = socket.rotation * Quaternion.Slerp(Quaternion.Inverse(socket.rotation) * card.rotation, localRotation, t);
        card.localScale = Vector3.Lerp(card.localScale, Vector3.one * targetScale, t);
    }

    /// <summary>Uses the same receiving-hand contact/trigger gesture as scenario item transfer.
    /// Transaction release is suppressed by the host during the synchronous adoption.</summary>
    internal static bool TryTransfer()
    {
        VRHand? left = VRHands.Left, right = VRHands.Right;
        VRHand? owner = left?.Grabber.Held is IItemCardHold { IsItemCard: true } ? left : right?.Grabber.Held is IItemCardHold { IsItemCard: true } ? right : null;
        if (owner == null) return false;
        VRHand? free = owner == left ? right : left;
        if (free == null || !free.HasPose || free.Grabber.Held != null) return true;
        var card = (IItemCardHold)owner.Grabber.Held!;
        if (!card.TryTouch(free.Rig.IndexTip.position, out float tip)
            || !card.TryTouch(free.Rig.PalmCenter.position, out float palm)) return true;
        if (tip > .035f * free.WorldScale || palm > .07f * free.WorldScale) return true;
        if (free.Ray.Enabled) free.Ray.SuppressFarClick();
        if (!free.RayUgui.HasHit && free.TriggerDown) card.Transfer(owner, free);
        return true;
    }
}
