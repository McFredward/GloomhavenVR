using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static Harmony? Harmony; internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static readonly List<string> Lines = new();
        internal static bool WantsDebug = true;
        internal static void Info(string scope, string value) => Lines.Add(scope + ": " + value);
        internal static void Note(string scope, string value) => Lines.Add(scope + ": " + value);
        internal static void Debug(string scope, string value) => Lines.Add(scope + ": " + value);
        internal static void Alert(string scope, string value) => throw new InvalidOperationException("Unexpected production alert: " + value);
    }
}
// Native ActorBehaviour is real but its unrelated gameplay Update is disabled; only its exact
// read-only Actor association and native Transform hierarchy are used for audio admission.
// AudioController, AudioObject, ObjectPoolController, all four native producers and Unity audio
// execute original compiled publisher methods. Synthetic probe waveforms replace copyrighted audio.

// The diagnostic fixture creates an inert original CardsHandUI solely to read its public
// mode/actor/selection metadata. Its unrelated scene-input teardown is not part of this proof.
[HarmonyLib.HarmonyPatch(typeof(CardsHandUI), "OnDestroy")]
internal static class FigureAudioPorts
{
    [HarmonyLib.HarmonyPrefix] private static bool Prefix() => false;
}
