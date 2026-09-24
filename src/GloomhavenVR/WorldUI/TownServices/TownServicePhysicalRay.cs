using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Far pickup uses the same registered physical token as direct touch. uGUI asks
/// this query before dispatch so a window behind a card cannot consume the same trigger.</summary>
internal static class TownServicePhysicalRay
{
    private static VRHand? _leftStamp, _rightStamp;
    private static int _leftFrame=-10, _rightFrame=-10;
    internal static void Claim(VRHand hand)
    {
        hand.Ray.SuppressFarClick();
        if(hand.Side==HandSide.Left){_leftStamp=hand;_leftFrame=Time.frameCount;}
        else{_rightStamp=hand;_rightFrame=Time.frameCount;}
    }
    internal static bool OwnsPointerFrame(VRHand hand)=>
        hand.Grabber.Held is TownServiceToken or TownServiceMerchantDrawer
        ||(hand==_leftStamp&&_leftFrame==Time.frameCount)||(hand==_rightStamp&&_rightFrame==Time.frameCount);
    private static bool PhysicalTarget(IGrabbable candidate) => candidate is TownServiceToken { IsPhysical: true }
        or TownServiceMerchantDrawer
        || candidate is VRCard card && TownServiceEnhancementHandoff.CanReclaim(card)
        || candidate is ItemsPile.ItemChip chip && TownServiceMerchantHandoff.CanReclaim(chip);

    internal static float OccludingDistance(Vector3 origin,Vector3 direction,float maxDistance)
    {
        float nearest=TownServiceCatalogCategory.OccludingDistance(origin,direction,maxDistance);var ray=new Ray(origin,direction);
        var entries=VRInteractables.Grabbables;
        for(int i=0;i<entries.Count;i++)
        {
            IGrabbable candidate=entries[i].Target;
            if (!PhysicalTarget(candidate)) continue;
            if(!candidate.CanGrab||!VRInteractables.IsUsablePickShape(entries[i].Collider))continue;
            if(entries[i].Collider.Raycast(ray,out RaycastHit hit,maxDistance)&&hit.distance<nearest)nearest=hit.distance;
        }
        return nearest;
    }
    internal static bool TryPick(VRHand hand, out IGrabbable? target, out Vector3 point, out float distance)
    {
        target = null; point = default; distance = float.PositiveInfinity;
        if (!hand.Ray.Active || VRHands.Primary != hand || !hand.Grabber.Enabled || hand.Grabber.Held != null) return false;
        hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        var ray = new Ray(origin, direction);
        float limit = RayGrabDriver.MaxDistanceMeters * hand.WorldScale;
        var entries = VRInteractables.Grabbables;
        for (int i = 0; i < entries.Count; i++)
        {
            IGrabbable candidate=entries[i].Target;
            if (!PhysicalTarget(candidate)) continue;
            if (!candidate.CanGrab || (candidate is IGrabbableHandFilter filter && !filter.AllowsHand(hand))
                || !VRInteractables.IsUsablePickShape(entries[i].Collider)) continue;
            if (entries[i].Collider.Raycast(ray, out RaycastHit hit, limit) && hit.distance < distance)
            { target = candidate; point = hit.point; distance = hit.distance; }
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
        if(OwnsPointerFrame(hand)){Claim(hand);return;}
        if (!TryPick(hand, out IGrabbable? target, out Vector3 point, out float distance)
            || hand.RayUgui.IsPressing || (hand.RayUgui.HasHit && hand.RayUgui.HitDistance < distance)) return;
        hand.Ray.SetPanelUiHit(point, "a physical town service sample");
        if (hand.TriggerDown && !hand.Grabber.TriggerGrabOffered)
        {
            if(target is TownServiceMerchantDrawer drawer)drawer.BeginLaser();
            if(hand.Grabber.ForceGrab(target!, releaseOnTriggerUp: true))Claim(hand);
        }
    }
}
