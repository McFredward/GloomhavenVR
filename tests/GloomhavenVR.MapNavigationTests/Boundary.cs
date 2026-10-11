using System;
using System.Collections.Generic;
using Code.State;
using GloomhavenVR.Hands;
using Script.GUI.SMNavigation.States.CampaignMapStates;

// The boundary records native dispatch and state ownership; eligibility and
// cleanup decisions come from the extracted production methods, not these stubs.
namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        public static void Destroy(Object value) => value.Destroyed = true;
    }
    internal static class Time { internal static int frameCount = 47; }
}
namespace Code.State
{
    internal interface IState { }
    internal sealed class UnknownState : IState { }
}
namespace Script.GUI.SMNavigation.States.CampaignMapStates
{
    internal enum CampaignMapStateTag { WorldMap, LocationHover }
    internal class LoadoutState : IState { }
    internal class LocationHoverState : IState { }
    internal class WorldMapState : IState { }
    internal sealed class MapStoryState : IState { }
    internal sealed class PersonalQuestChoiceState : IState { }
    internal sealed class MerchantState : IState { }
    internal sealed class TempleState : IState { }
    internal sealed class MapEventState : IState { }
    internal sealed class TravelMapState : IState { }
    internal sealed class CampaignRewardState : IState { }
    internal sealed class MapLocationStateData
    {
        internal readonly MapLocation Location;
        internal MapLocationStateData(MapLocation location) => Location = location;
    }
}
namespace Script.GUI.SMNavigation.States.CampaignMapStates.Enhancment
{
    internal sealed class EnhancmentCardSelectState : IState { }
    internal sealed class EnhancmentConfirmationState : IState { }
}
namespace Script.GUI.SMNavigation.States.PopupStates
{
    internal sealed class PersonalQuestCompletedState : IState { }
    internal sealed class GuildmasterRewardsState : IState { }
    internal sealed class DistributeGoldRewardsState : IState { }
}
namespace Script.GUI.SMNavigation
{
    internal sealed class UINavigation
    {
        internal NavigationStateMachine? StateMachine;
    }
    internal sealed class NavigationStateMachine
    {
        internal IState? CurrentState;
        internal int EnterCount;
        internal int ExitCount;
        internal object? Payload;
        internal void Enter(CampaignMapStateTag tag, object? payload = null)
        {
            if (CurrentState != null) ExitCount++;
            EnterCount++;
            Payload = payload;
            CurrentState = tag == CampaignMapStateTag.LocationHover
                ? new LocationHoverState() : new WorldMapState();
        }
    }
}
internal static class Singleton<T> where T : class
{
    internal static bool IsInitialized;
    internal static T Instance = null!;
}
internal sealed class AdventureMapUIManager
{
    internal bool IsLocked;
}
internal sealed class MapLocation : UnityEngine.Object
{
    internal int EnterCount, ExitCount;
    internal Action? BeforeEnter, BeforeExit;
    internal void OnPointerEnter(object? pointer)
    {
        EnterCount++;
        BeforeEnter?.Invoke();
    }
    internal void OnPointerExit(object? pointer)
    {
        ExitCount++;
        BeforeExit?.Invoke();
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Warn(string scope, string message) => throw new InvalidOperationException(message);
        internal static void Info(string scope, string message) { }
    }
}
namespace GloomhavenVR.Hands
{
    internal enum HapticPreset { HoverTick }
    internal sealed class HandRay { internal int Mask; }
    internal sealed class VRHand
    {
        internal readonly HandRay Ray = new();
        internal int HapticCount;
        internal void SendHaptic(HapticPreset preset) => HapticCount++;
    }
    internal static class VRHands { internal static VRHand? Left, Right; }
}
namespace GloomhavenVR.Hands.Interact
{
    internal static class VRInteractables
    {
        internal static int Unregistered;
        internal static void UnregisterPokeable(object value) => Unregistered++;
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal sealed class MapLocationPoke : UnityEngine.Object { }
    internal sealed class MapIconHoverPads
    {
        internal int ReleaseCount;
        internal void Release(string reason) => ReleaseCount++;
    }
    internal static class MapHoverVerdict { internal static void Reset() { } }
    internal static class MapIconHoverAnimation
    {
        internal static int Exits, Enters;
        internal static void ReportHoverExit(MapLocation? location) { if (location != null) Exits++; }
        internal static void ReportHoverEnter(MapLocation location) => Enters++;
        internal static void Rearm() { }
    }
    internal sealed partial class MapLocationInteractor
    {
#pragma warning disable CS0414, CS0649 // Full production teardown fields; no alternative implementation.
        private const string Scope = "MapRoom";
        private MapLocation? _hover;
        private int _hoverFrame, _scanGeneration, _pokePickFrame, _scanFrame;
        private string _hoverHow = "?";
        private bool _verdictDone, _capitalForced, _reported, _maskTaken;
        private float _refusedLoggedAt;
        private int _maskWasLeft, _maskWasRight, _maskInForce;
        private MapLocation? _pokePickOwner;
        private readonly List<MapLocationPoke> _pokes = new();
        private readonly List<MapLocation> _locations = new();
        private readonly HashSet<string> _capitalTermsLogged = new(), _refusedSelections = new();
        private readonly MapIconHoverPads _pads = new();
#pragma warning restore CS0414, CS0649
        // Capital highlight behavior is a separate visual boundary, never navigation admission.
        private static bool CapitalRouteAllowed(MapLocation location) => false;
        private static bool CapitalHover(MapLocation location, bool active) => false;
        internal MapLocation? HoverForFixture => _hover;
        internal int PadReleaseCount => _pads.ReleaseCount;
        internal void SeedHover(MapLocation location) => _hover = location;
        internal MapLocationPoke SeedOwnedAdapter()
        {
            var poke = new MapLocationPoke();
            _pokes.Add(poke);
            _locations.Add(new MapLocation());
            _maskTaken = true;
            _maskWasLeft = 15;
            _maskWasRight = 23;
            return poke;
        }
    }
}
