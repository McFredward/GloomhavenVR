using System;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using Object = UnityEngine.Object;

public static class InteractionProgram
{
    private static int _assertions;
    private static void Check(bool value, string message)
    { _assertions++; if (!value) throw new Exception(message); }
    private static void Near(float expected, float actual, string message) => Check(Mathf.Abs(expected - actual) < .001f, message);
    private static void Reset()
    {
        VRHands.Primary = new VRHand(); RayGrabDriver.Distance = float.PositiveInfinity;
        WorldUIConfig.ImmersiveTownServices.Value = true; StoryComposite.PointOfNoReturn = false;
        TownServicePopulation.Ready = true; MapRoomDriver.CanVisit = MapRoomDriver.Accept = true;
        MapRoomDriver.Presses = 0; MapRoomDriver.Context = null;
    }
    private static void WithTarget(byte service, Action<TownServiceVisitTarget, GameObject, VRHand> action)
    {
        Reset(); var station = new GameObject("visit fixture station");
        var target = new TownServiceVisitTarget(service, station.transform);
        try { target.Tick(true); Physics.SyncTransforms(); action(target, station, VRHands.Primary!); }
        finally { target.Dispose(); Object.DestroyImmediate(station); VRHands.Primary = null; }
        Check(VRInteractables.Pokes.Count == 0, "touch registry cleans up after every resident");
    }
    private static float Distance(VRHand hand, float maximum = 20) => TownServiceVisitTarget.OccludingDistance(hand.Origin, hand.Direction, maximum);
    public static int Run()
    {
        _assertions = 0;
        for (byte service = 1; service <= 3; service++) WithTarget(service, (target, station, hand) =>
        {
            Check(VRInteractables.Pokes.ContainsKey(target), "resident registers physical touch collider");
            Check(VRInteractables.Pokes[target] is BoxCollider collider && collider.isTrigger, "real trigger BoxCollider");
            Near(2.45f, Distance(hand), "ray intersects original resident bounds");
            target.OnPokeEnter(hand); Check(hand.Hover == 1 && MapRoomDriver.Presses == 0, "touch hover cannot open destination");
            target.OnPoke(hand); target.OnPoke(hand); hand.TriggerDown = true; TownServiceVisitTarget.TickLaser();
            Check(MapRoomDriver.Presses == 1, "touch and ray open guarded destination only once within cooldown");
            Check((byte)MapRoomDriver.Mode == service && MapRoomDriver.Context == "town resident", "all three residents retain native destination");
            Check(hand.Click == 1, "one accepted destination produces one click haptic");
        });
        WithTarget(1, (target, station, hand) =>
        {
            TownServiceVisitTarget.TickLaser(); TownServiceVisitTarget.TickLaser();
            Check(hand.Hover == 1 && MapRoomDriver.Presses == 0, "stable laser hover is edge-triggered and cannot visit");
            Check(hand.Ray.UiHitOverride.HasValue, "resident clamps visible pointer endpoint");
            Near(.45f, hand.Ray.UiHitOverride!.Value.z, "pointer endpoint is real collider surface");
            hand.TriggerDown = true; TownServiceVisitTarget.TickLaser();
            Check(MapRoomDriver.Presses == 1 && hand.Click == 1 && hand.Ray.Suppressions == 1, "laser invokes native guarded path and consumes far click");
        });
        foreach (string gate in new[] { "commit", "permission", "hidden", "disabled" }) WithTarget(1, (target, station, hand) =>
        {
            if (gate == "commit") StoryComposite.PointOfNoReturn = true;
            if (gate == "permission") MapRoomDriver.CanVisit = false;
            if (gate == "hidden") { target.Tick(false); Physics.SyncTransforms(); }
            if (gate == "disabled") WorldUIConfig.ImmersiveTownServices.Value = false;
            target.OnPokeEnter(hand); target.OnPoke(hand); hand.TriggerDown = true; TownServiceVisitTarget.TickLaser();
            Check(MapRoomDriver.Presses == 0, gate + " resident cannot invoke native destination");
            Check(hand.Click == 0 && hand.Hover == 0, gate + " resident cannot signal availability");
            if (gate == "hidden") Check(float.IsPositiveInfinity(Distance(hand)), "hidden assets leave no occluder");
            else Near(2.45f, Distance(hand), gate + " visible resident still blocks background interaction");
        });
        WithTarget(1, (target, station, hand) =>
        {
            MapRoomDriver.Accept = false; target.OnPoke(hand);
            Check(MapRoomDriver.Presses == 1 && hand.Click == 0, "native rejection does not claim successful opening");
        });
        foreach (string blocker in new[] { "bar", "ui", "solid", "held", "carry", "tracking", "beam" }) WithTarget(1, (target, station, hand) =>
        {
            hand.TriggerDown = true;
            if (blocker == "bar") RayGrabDriver.Distance = 1f;
            if (blocker == "ui") { hand.RayUgui.HasHit = true; hand.RayUgui.HitDistance = 1f; }
            if (blocker == "solid") hand.Ray.SolidOccluderDistance = 1f;
            if (blocker == "held") hand.Grabber.Held = new object();
            if (blocker == "carry") hand.RayGrab.OwnsPointerFrame = true;
            if (blocker == "tracking") hand.HasPose = false;
            if (blocker == "beam") hand.Ray.Active = false;
            TownServiceVisitTarget.TickLaser();
            Check(MapRoomDriver.Presses == 0 && hand.Hover == 0 && hand.Ray.Suppressions == 0, blocker + " wins before resident input");
            Check(!hand.Ray.UiHitOverride.HasValue, blocker + " retains pointer ownership");
        });
        WithTarget(2, (target, station, hand) =>
        {
            hand.RayUgui.HasHit = true; hand.RayUgui.HitDistance = 8; RayGrabDriver.Distance = 9;
            hand.Ray.ComputeResidentOcclusion(hand.Origin, hand.Direction, 20, float.PositiveInfinity);
            Near(2.45f, hand.Ray.SolidOccluderDistance, "resident participates in early ray arbitration");
            Check(!hand.Ray.SolidOccluderIsBoard, "resident is not misclassified as board surface");
            var panel = new GameObject("background native panel").AddComponent<Canvas>();
            try
            {
                Check(BoundUiArbitration.Pick(hand, panel, 8f) == null, "foreground resident blocks background uGUI before hover and click");
                Check(BoundUiArbitration.Pick(hand, panel, 1f) == panel, "foreground uGUI remains interactive");
                float limit = LaserPointerPolicy.PickLimit(20, hand.Ray.SolidOccluderDistance, 9, 8, false);
                Check(!LaserPointerPolicy.TargetBeforeBlocker(8f, limit), "foreground resident blocks background map target");
            }
            finally { Object.DestroyImmediate(panel.gameObject); }
            hand.TriggerDown = true; TownServiceVisitTarget.TickLaser();
            Check(MapRoomDriver.Presses == 1, "resident in front of other UI remains visitable with own solid bound");
        });
        WithTarget(3, (target, station, hand) =>
        {
            station.transform.position = new Vector3(4, -2, 7); station.transform.rotation = Quaternion.Euler(0, 37, 0);
            station.transform.localScale = Vector3.one * 2;
            hand.Origin = station.transform.TransformPoint(new Vector3(0, 1.3f, -2));
            hand.Direction = station.transform.TransformDirection(Vector3.forward); hand.WorldScale = 2;
            Physics.SyncTransforms(); Near(4.9f, Distance(hand), "scaled rotated resident uses world-space collider distance");
            Check(float.IsPositiveInfinity(Distance(hand, 4)), "resident beyond beam reach does not occlude");
            hand.TriggerDown = true; TownServiceVisitTarget.TickLaser();
            Check(MapRoomDriver.Presses == 1, "rotated scaled resident remains visitable");
        });
        WithTarget(1, (target, station, hand) =>
        {
            var fartherStation = new GameObject("farther resident"); fartherStation.transform.position = Vector3.forward * 3;
            var farther = new TownServiceVisitTarget(2, fartherStation.transform);
            try
            {
                farther.Tick(true); Physics.SyncTransforms(); hand.TriggerDown = true; TownServiceVisitTarget.TickLaser();
                Check(MapRoomDriver.Mode == EGuildmasterMode.Merchant && MapRoomDriver.Presses == 1, "nearest visible resident wins among multiple residents");
                target.Dispose(); Physics.SyncTransforms(); Near(5.45f, Distance(hand), "disposed resident no longer blocks other residents immediately");
                hand.Ray.UiHitOverride = null; TownServiceVisitTarget.TickLaser();
                Check(MapRoomDriver.Mode == EGuildmasterMode.Temple && MapRoomDriver.Presses == 2, "dispose releases ray hover to remaining resident");
                farther.Dispose(); Physics.SyncTransforms(); Check(float.IsPositiveInfinity(Distance(hand)), "disposing final resident removes every ray target");
                hand.Ray.UiHitOverride = null; TownServiceVisitTarget.TickLaser();
                Check(MapRoomDriver.Presses == 2 && !hand.Ray.UiHitOverride.HasValue, "destroy deferred until frame end cannot leave ghost input");
            }
            finally { farther.Dispose(); Object.DestroyImmediate(fartherStation); }
        });
        Reset();
        Check(!TownServiceVisitTarget.Replaces(EGuildmasterMode.None), "unrelated map buttons remain native");
        Check(TownServiceVisitTarget.Replaces(EGuildmasterMode.Merchant), "available resident replaces corresponding map button");
        TownServicePopulation.Ready = false; Check(!TownServiceVisitTarget.Replaces(EGuildmasterMode.Merchant), "missing resident assets retain native entry");
        TownServicePopulation.Ready = true; WorldUIConfig.ImmersiveTownServices.Value = false;
        Check(!TownServiceVisitTarget.Replaces(EGuildmasterMode.Merchant), "disabled immersion retains native entry");
        VRHands.Primary = null; TownServiceVisitTarget.TickLaser();
        return _assertions;
    }
}
