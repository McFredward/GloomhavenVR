using System;
using System.Collections.Generic;
using System.Reflection;
using ClockStone;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Keep original figure-audio producers and all native settings/callbacks intact.
/// Exact native CharacterManager/ActorPrefab associations (or actual ActorBehaviour
/// ancestry) author a generation-bound emitter-follow receipt.</summary>
internal static class NativeFigureAudioPatches
{
    private static bool _installed;
    internal static void Install()
    {
        if (_installed || VRSession.Harmony == null) return;
        VRSession.Harmony.PatchAll(typeof(FigureAudioProducer));
        VRSession.Harmony.PatchAll(typeof(FigureAudioObjects));
        VRSession.Harmony.PatchAll(typeof(PickCommit));
        VRSession.Harmony.PatchAll(typeof(PickRequests));
        _installed = true;
    }
    [HarmonyPatch]
    private static class FigureAudioProducer
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
    [HarmonyPatch(typeof(CardsHandUI), "OnLoseCardClick")]
    private static class PickCommit
    {
        [HarmonyPrefix]
        private static void Prefix(CardsHandUI __instance, out NativePickAudioAudit.Scope? __state)
            => __state = NativePickAudioAudit.Begin(__instance);
        [HarmonyFinalizer]
        private static Exception? Finalizer(NativePickAudioAudit.Scope? __state, Exception? __exception)
        { NativePickAudioAudit.End(__state, __exception); return __exception; }
    }
    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlayAudioItem))]
    private static class PickRequests
    {
        [HarmonyPostfix]
        private static void Postfix(AudioItem sndItem, AudioObject? __result) => NativePickAudioAudit.Observe(sndItem, __result);
    }
    [HarmonyPatch(typeof(AudioController), nameof(AudioController.PlayAudioSubItem))]
    private static class FigureAudioObjects
    {
        [HarmonyPostfix]
        private static void Postfix(AudioObject? __result) => NativeFigureAudio.Observe(__result);
    }
}
