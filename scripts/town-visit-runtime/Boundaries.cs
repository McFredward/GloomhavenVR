using System.Collections.Generic;
using UnityEngine;

// Explicit external boundaries: native destination permissions/callbacks, input-device state,
// haptics and global registration. Target geometry, transforms and Collider.Raycast are Unity.
internal enum EGuildmasterMode { None, Merchant, Temple, Enchantress }
namespace GloomhavenVR.Core
{
    internal static class VRLayers { internal static void Apply(GameObject root) { } }
}
namespace GloomhavenVR.Hands
{
    internal enum HapticPreset { HoverTick, ClickPulse }
    internal sealed class VRHand
    {
        internal bool HasPose = true, TriggerDown;
        internal float WorldScale = 1f;
        internal Vector3 Origin = new(0, 1.3f, -2), Direction = Vector3.forward;
        internal readonly Interact.RayInteractor Ray = new();
        internal readonly GrabberBoundary Grabber = new();
        internal readonly CarryBoundary RayGrab = new();
        internal readonly UiBoundary RayUgui = new();
        internal int Hover, Click;
        internal void GetAimRay(out Vector3 origin, out Vector3 direction) { origin = Origin; direction = Direction; }
        internal void SendHaptic(HapticPreset preset) { if (preset == HapticPreset.HoverTick) Hover++; else Click++; }
    }
    internal sealed class GrabberBoundary { internal object? Held; }
    internal sealed class CarryBoundary { internal bool OwnsPointerFrame; }
    internal sealed class UiBoundary { internal bool HasHit; internal float HitDistance; }
    internal static class VRHands { internal static VRHand? Primary; }
}
namespace GloomhavenVR.Hands.Interact
{
    internal interface IPokeable { void OnPokeEnter(VRHand hand); void OnPokeExit(VRHand hand); void OnPoke(VRHand hand); }
    internal static class VRInteractables
    {
        internal static readonly Dictionary<IPokeable, Collider> Pokes = new();
        internal static void RegisterPokeable(IPokeable target, Collider collider) => Pokes.Add(target, collider);
        internal static void UnregisterPokeable(IPokeable target) => Pokes.Remove(target);
    }
    internal static class RayGrabDriver
    {
        internal static float Distance = float.PositiveInfinity;
        internal static float OccludingBarDistance(Vector3 origin, Vector3 direction, float maximum) => Distance;
    }
    internal sealed partial class RayInteractor
    {
        internal bool Active = true;
        internal float FanOccluderDistance = float.PositiveInfinity, SolidOccluderDistance = float.PositiveInfinity;
        internal bool SolidOccluderIsBoard;
        internal Vector3? UiHitOverride;
        internal int Suppressions;
        internal static bool WantFanOcclusionNote => false;
        internal void NoteFanOcclusion(string surface, float distance) { }
        internal void SuppressFarClick() => Suppressions++;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class BooleanSetting { internal bool Value = true; }
    internal static class WorldUIConfig { internal static readonly BooleanSetting ImmersiveTownServices = new(); }
    internal static class StoryComposite { internal static bool PointOfNoReturn; }
    internal static class ButtonTuning { internal const float PokePressCooldownSeconds = .2f; }
    internal static class TownServicePopulation
    {
        internal static bool Ready = true;
        internal static bool Available(byte service) => Ready;
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver
    {
        internal static bool CanVisit = true, Accept = true;
        internal static int Presses;
        internal static EGuildmasterMode Mode;
        internal static string? Context;
        internal static bool CanVisitTownService(EGuildmasterMode mode) => CanVisit;
        internal static bool PressGuildmasterMode(EGuildmasterMode mode, string context)
        { Presses++; Mode = mode; Context = context; return Accept; }
    }
}

namespace GloomhavenVR.WorldUI {internal static class TownServiceEnhancementHandoff {internal static bool Enabled=true;} }

// Physical cabinet occlusion is tested by its dedicated interaction fixture.
namespace GloomhavenVR.WorldUI { internal static class TownServicePhysicalRay { internal static float OccludingDistance(Vector3 origin, Vector3 direction, float maximum) => float.PositiveInfinity; } }
