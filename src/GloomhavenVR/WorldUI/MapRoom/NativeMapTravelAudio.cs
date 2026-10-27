using System;
using System.Collections.Generic;
using System.Reflection;
using ClockStone;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Build726: after the road encounter the maintainer heard two cart loops until arrival.
/// MapTimedMovementFlow.MovePartyToEncounter starts a drum plus scheduled MovingAudioItem,
/// but never stops that loop. MovePartyFromEncounterToDestination plays it a second time.
/// MapPointsMovementFlow has the same two producers. The original serialized cart item is
/// LoopSubitem with unlimited instances and no stop events; UIMapEncounter has no stop event
/// either. The725 logs prove the two legs ran, but contain no playing-source census/audio.
///
/// Bind only these native producers. Record their actual objects, cancel the previous owned
/// request when a new leg starts, and let every native statement/callback run unmodified.
/// No audio ID is globally suppressed; simultaneous layers within a request and unrelated
/// producers are retained. Flat calls remain native, and flow destruction clears any owned
/// scheduled request even when the map room has already stood down.
/// </summary>
internal static class NativeMapTravelAudio
{
    private static bool _installed;

    internal static void Install()
    {
        if (_installed || VRSession.Harmony == null) return;
        VRSession.Harmony.PatchAll(typeof(Producer));
        VRSession.Harmony.PatchAll(typeof(Objects));
        VRSession.Harmony.PatchAll(typeof(FlowEnd));
        _installed = true;
    }

    [HarmonyPatch]
    internal static class Producer
    {
        private struct Scope
        {
            internal bool Entered;
            internal NativeTravelLoop.Capture? Previous;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MapTimedMovementFlow), "MovePartyFromOriginToDestination",
                new[] { typeof(PartyToken), typeof(Action), typeof(Action) });
            foreach (Type type in new[] { typeof(MapTimedMovementFlow), typeof(MapPointsMovementFlow) })
            {
                yield return AccessTools.Method(type, nameof(MapMovementFlow.MovePartyToEncounter));
                yield return AccessTools.Method(type, nameof(MapMovementFlow.MovePartyFromEncounterToDestination));
            }
            yield return AccessTools.Method(typeof(MapPointsMovementFlow), nameof(MapMovementFlow.MovePartyFromOriginToDestination));
        }

        [HarmonyPrefix]
        private static void Prefix(MapMovementFlow __instance, out Scope __state)
        {
            __state = default;
            if (!MapRoomDriver.Active) return;
            string? item = __instance is MapTimedMovementFlow timed ? timed.mapConfig?.MovingAudioItem
                : (__instance as MapPointsMovementFlow)?.mapConfig?.MovingAudioItem;
            __state.Entered = true;
            __state.Previous = NativeTravelLoop.Begin(__instance, item);
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Scope __state, Exception? __exception)
        {
            // Flat nested calls must leave an outer producer's scope alone as well.
            Scope state = __state;
            if (state.Entered) NativeTravelLoop.End(state.Previous);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlayAudioSubItem))]
    internal static class Objects
    {
        [HarmonyPostfix]
        private static void Postfix(AudioSubItem subItem, AudioObject? __result)
            => NativeTravelLoop.Observe(subItem, __result);
    }

    [HarmonyPatch]
    internal static class FlowEnd
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MapTimedMovementFlow), "OnDestroy");
            yield return AccessTools.Method(typeof(MapPointsMovementFlow), "OnDestroy");
        }

        [HarmonyPrefix]
        private static void Prefix(MapMovementFlow __instance) => NativeTravelLoop.Release(__instance);
    }
}
