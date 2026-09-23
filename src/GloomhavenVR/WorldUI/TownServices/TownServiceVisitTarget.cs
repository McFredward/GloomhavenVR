using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The resident itself is the visit affordance. Both pointers use the original
/// destination's guarded opening path; neither pointing nor touching buys anything.</summary>
internal sealed class TownServiceVisitTarget : IPokeable, IDisposable
{
    private static readonly List<TownServiceVisitTarget> Targets = new(3);
    private static TownServiceVisitTarget? _hover;
    private readonly GameObject _root;
    private readonly BoxCollider _collider;
    private readonly EGuildmasterMode _mode;
    private bool _visible;
    private float _pressedAt = -10f;

    internal static byte ServiceOf(EGuildmasterMode mode) => mode == EGuildmasterMode.Merchant ? (byte)1
        : mode == EGuildmasterMode.Temple ? (byte)2 : mode == EGuildmasterMode.Enchantress ? (byte)3 : (byte)0;
    internal static bool Replaces(EGuildmasterMode mode)
    {
        byte service = ServiceOf(mode);
        return service != 0 && WorldUIConfig.ImmersiveTownServices.Value && TownServicePopulation.Available(service);
    }

    internal TownServiceVisitTarget(byte service, Transform station)
    {
        _mode = service == 1 ? EGuildmasterMode.Merchant : service == 2 ? EGuildmasterMode.Temple : EGuildmasterMode.Enchantress;
        _root = new GameObject("GloomhavenVR.TownService.Visit");
        _root.transform.SetParent(station, false);
        _root.transform.localPosition = new Vector3(0f, 1.30f, .65f);
        _collider = _root.AddComponent<BoxCollider>();
        _collider.isTrigger = true; _collider.size = new Vector3(.55f, .80f, .40f);
        _collider.enabled = false;
        VRLayers.Apply(_root);
        VRInteractables.RegisterPokeable(this, _collider);
        Targets.Add(this);
    }

    private bool Available => _visible && WorldUIConfig.ImmersiveTownServices.Value
        && !StoryComposite.PointOfNoReturn
        && MapRoomDriver.CanVisitTownService(_mode);
    internal void Tick(bool visible)
    { _visible = visible; _collider.enabled = visible; }

    private void Visit(VRHand hand)
    {
        if (!Available || Time.unscaledTime - _pressedAt < ButtonTuning.PokePressCooldownSeconds) return;
        _pressedAt = Time.unscaledTime;
        if (MapRoomDriver.PressGuildmasterMode(_mode, "town resident")) hand.SendHaptic(HapticPreset.ClickPulse);
    }
    public void OnPokeEnter(VRHand hand) { if (Available) hand.SendHaptic(HapticPreset.HoverTick); }
    public void OnPokeExit(VRHand hand) { }
    public void OnPoke(VRHand hand) => Visit(hand);

    internal static float OccludingDistance(Vector3 origin, Vector3 direction, float maximum)
    {
        float nearest = float.PositiveInfinity;
        var ray = new Ray(origin, direction);
        foreach (TownServiceVisitTarget target in Targets)
            if (target._collider.enabled && target._collider.Raycast(ray, out RaycastHit hit, Mathf.Min(maximum, nearest)))
                nearest = hit.distance;
        return nearest;
    }

    internal static void TickLaser()
    {
        VRHand? hand = VRHands.Primary;
        if (hand == null || !hand.HasPose || !hand.Ray.Active || hand.Grabber.Held != null || hand.RayGrab.OwnsPointerFrame)
        { _hover = null; return; }
        hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        var ray = new Ray(origin, direction);
        float maximum = 20f * hand.WorldScale, best = maximum;
        TownServiceVisitTarget? hit = null;
        Vector3 point = default;
        foreach (TownServiceVisitTarget target in Targets)
            if (target._collider.enabled && target._collider.Raycast(ray, out RaycastHit result, best))
            { hit = target; best = result.distance; point = result.point; }
        if (hit != null)
        {
            float bar = RayGrabDriver.OccludingBarDistance(origin, direction, maximum);
            if (!LaserPointerPolicy.TargetBeforeBlocker(best, bar)
                || (hand.RayUgui.HasHit && hand.RayUgui.HitDistance < best)
                || hand.Ray.SolidOccluderDistance < best - .005f * hand.WorldScale) hit = null;
        }
        if (hit != _hover && hit != null && hit.Available) hand.SendHaptic(HapticPreset.HoverTick);
        _hover = hit;
        if (hit == null) return;
        hand.Ray.UiHitOverride = point;
        if (hand.TriggerDown) { hand.Ray.SuppressFarClick(); hit.Visit(hand); }
    }

    public void Dispose()
    {
        Targets.Remove(this);
        if (_hover == this) _hover = null;
        VRInteractables.UnregisterPokeable(this);
        if (_root != null) { _root.SetActive(false); UnityEngine.Object.Destroy(_root); }
    }
}
