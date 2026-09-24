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

/// <summary>The shared ability/item reading/grasp solver. Merchant samples use this exact
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
        // The card must be a child of the grab socket, exactly like an ability card. Converting
        // yesterday's world pose into today's socket reintroduces wrist translation/rotation
        // into the interpolation and makes the object trail the fingers (build-549 hardware).
        float t = 1f - Mathf.Exp(-CardsConfig.CardLerpSpeed.Value * 1.5f * Time.deltaTime);
        float grasp = HeldCardGrip.Blend(hand);
        if (grasp > 0f && HeldCardGrip.TryPose(hand, width, height, out Vector3 grip, out Quaternion rotation))
        {
            Vector3 position = grip;
            Quaternion localRotation = rotation;
            if (grasp < 1f)
            {
                position = Vector3.Lerp(readingPosition, grip, grasp);
                localRotation = Quaternion.Slerp(LocalBillboard(card, socket), rotation, grasp);
            }
            card.localPosition = Vector3.Lerp(card.localPosition, position, t);
            card.localRotation = Quaternion.Slerp(card.localRotation, localRotation, t);
        }
        else
        {
            card.localPosition = Vector3.Lerp(card.localPosition, readingPosition, t);
            card.localRotation = Quaternion.Slerp(card.localRotation, LocalBillboard(card, socket), t);
        }
        card.localScale = Vector3.Lerp(card.localScale, Vector3.one * targetScale, t);
    }

    private static Quaternion LocalBillboard(Transform card, Transform socket)
    {
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null) return card.localRotation;
        Vector3 away = card.position - head.transform.position;
        return away.sqrMagnitude <= 1e-6f ? card.localRotation
            : Quaternion.Inverse(socket.rotation) * Quaternion.LookRotation(away.normalized, head.transform.up);
    }
}
