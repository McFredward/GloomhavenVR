using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Far pickup uses the same registered physical token as direct touch. uGUI asks
/// this query before dispatch so a window behind a card cannot consume the same trigger.</summary>
internal static class TownServicePhysicalRay
{
    internal static bool TryPick(VRHand hand, out TownServiceToken? target, out Vector3 point, out float distance)
    {
        target = null; point = default; distance = float.PositiveInfinity;
        if (!hand.Ray.Active || VRHands.Primary != hand || !hand.Grabber.Enabled || hand.Grabber.Held != null) return false;
        hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        var ray = new Ray(origin, direction);
        float limit = RayGrabDriver.MaxDistanceMeters * hand.WorldScale;
        var entries = VRInteractables.Grabbables;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Target is not TownServiceToken token || !token.IsPhysical || !token.CanGrab
                || !token.AllowsHand(hand) || !VRInteractables.IsUsablePickShape(entries[i].Collider)) continue;
            if (entries[i].Collider.Raycast(ray, out RaycastHit hit, limit) && hit.distance < distance)
            { target = token; point = hit.point; distance = hit.distance; }
        }
        if (target == null) return false;
        float epsilon = .005f * hand.WorldScale;
        PickPose pick = hand.Ray.Current;
        if ((pick.HasHit && pick.HitDistance < distance - epsilon)
            || hand.Ray.SolidOccluderDistance < distance - epsilon)
        { target = null; return false; }
        return true;
    }
    internal static void Tick(VRHand hand)
    {
        if (!TryPick(hand, out TownServiceToken? target, out Vector3 point, out float distance)
            || hand.RayUgui.IsPressing || (hand.RayUgui.HasHit && hand.RayUgui.HitDistance < distance)) return;
        hand.Ray.SetPanelUiHit(point, "a physical town service sample");
        if (hand.TriggerDown && !hand.Grabber.TriggerGrabOffered)
            hand.Grabber.ForceGrab(target!, releaseOnTriggerUp: true);
    }
}
