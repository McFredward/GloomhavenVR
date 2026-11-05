using System;
using System.Collections.Generic;
using System.Reflection;
using ClockStone;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Keep original figure-audio producers and all native settings/callbacks intact.
/// Only actual ActorBehaviour descendants author a source-follow receipt.</summary>
internal static class NativeFigureAudioPatches
{
    private static bool _installed;
    internal static void Install()
    {
        if (_installed || VRSession.Harmony == null) return;
        VRSession.Harmony.PatchAll(typeof(Producer));
        VRSession.Harmony.PatchAll(typeof(Objects));
        _installed = true;
    }
    [HarmonyPatch]
    private static class Producer
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SFXOnEnable), "PlaySound");
            yield return AccessTools.Method(typeof(FootstepSound), nameof(FootstepSound.MakeFootstepSound));
            yield return AccessTools.Method(typeof(AudioEffectPlayer), nameof(AudioEffectPlayer.PlayEffect));
            yield return AccessTools.Method(typeof(PlayAudio), "_OnEventTriggered");
        }
        [HarmonyPrefix]
        private static void Prefix(Component __instance, out NativeFigureAudio.Scope __state)
        {
            PlayAudio? trigger = __instance as PlayAudio;
            __state = NativeFigureAudio.Begin(__instance,
                __instance is SFXOnEnable || trigger != null && trigger.position != PlayAudio.PlayPosition.Global,
                trigger == null || trigger.soundType == PlayAudio.SoundType.SFX);
        }
        [HarmonyFinalizer]
        private static Exception? Finalizer(NativeFigureAudio.Scope __state, Exception? __exception)
        { NativeFigureAudio.End(__state); return __exception; }
    }
    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlayAudioSubItem))]
    private static class Objects
    {
        [HarmonyPostfix]
        private static void Postfix(AudioObject? __result) => NativeFigureAudio.Observe(__result);
    }
}
