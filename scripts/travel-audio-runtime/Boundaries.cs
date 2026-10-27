using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static Harmony? Harmony; }
    internal static class VRLog
    {
        internal static readonly List<string> Lines = new();
        internal static void Info(string scope, string value) => Lines.Add(scope + ": " + value);
        internal static void Alert(string scope, string value) => throw new InvalidOperationException("Unexpected production alert: " + value);
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active; }
}

// Only movement, camera, scene bootstrap and the delayed arrival clock are ports.
// The original GH.Runtime travel producers, AudioControllerUtils, AudioController,
// AudioObject, audio scheduling/fades and Unity AudioSources execute unchanged.
[HarmonyPatch]
internal static class TravelPorts
{
    internal static Action? Encounter, Arrival;
    internal static readonly List<Action> Delayed = new();
    internal static bool ThrowFromEncounter, LeaveRoomDuringEncounter;

    [HarmonyPrefix, HarmonyPatch(typeof(MapTimedMovementFlow), "MovePartyToFadeIn")]
    private static bool TimedEncounter(Action? onFaded)
    {
        Encounter = onFaded;
        if (LeaveRoomDuringEncounter) GloomhavenVR.WorldUI.MapRoom.MapRoomDriver.Active = false;
        if (ThrowFromEncounter) throw new InvalidOperationException("fixture native movement boundary failed");
        return false;
    }
    [HarmonyPrefix, HarmonyPatch(typeof(MapTimedMovementFlow), "MovePartyToFadeOutDestination")]
    private static bool TimedArrival(Action? onFinished) { Arrival = onFinished; return false; }

    [HarmonyPrefix, HarmonyPatch(typeof(PartyToken), nameof(PartyToken.PartyMoveTo),
        new[] { typeof(Vector3[]), typeof(Action), typeof(Action<float>) })]
    private static bool PointsArrival(Action? callback) { Arrival = callback; return false; }

    [HarmonyPrefix, HarmonyPatch(typeof(MapMovementFlow<MapTimedFlowConfig>), nameof(MapMovementFlow.IncreaseZoomToParty))]
    private static bool Camera() => false;

    [HarmonyPrefix, HarmonyPatch(typeof(CoroutineHelper), nameof(CoroutineHelper.DelayedStartCoroutine),
        new[] { typeof(float), typeof(Action) })]
    private static bool Delay(Action function, ref IEnumerator __result)
    { Delayed.Add(function); __result = Empty(); return false; }
    private static IEnumerator Empty() { yield break; }

    [HarmonyPrefix, HarmonyPatch(typeof(MapChoreographer), "Awake")]
    private static bool Bootstrap() => false;
    [HarmonyPrefix, HarmonyPatch(typeof(MapChoreographer), "OnDestroy")]
    private static bool Teardown() => false;
}
