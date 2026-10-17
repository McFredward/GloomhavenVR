using System;
using UnityEngine;
using UnityEngine.UI;

// Native object boundaries only. Discovery, source selection, sampling, quotas
// and state restoration execute the unchanged production observer in Unity.
public sealed class FullAbilityCard : MonoBehaviour { public Image headerImage = null!; }
public sealed class InitiativeTrack : MonoBehaviour { public static InitiativeTrack? Instance; }
public sealed class InitiativeTrackActorAvatar : MonoBehaviour { public Image selectionImage = null!; }
public sealed class ActorStatPanel : MonoBehaviour { public Image characterPortrait = null!; }
public static class Singleton<T> where T : class
{
    public static T Instance = null!;
    public static bool IsInitialized => Instance != null;
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled; }
    internal static class VRLog
    {
        internal static bool WantsDebug;
        internal static void Info(string scope, string text) => Debug.Log(text);
        internal static void Warn(string scope, string text) => Debug.LogWarning(text);
    }
    internal static class PerfMonitor
    {
        internal readonly struct Measure : IDisposable { public void Dispose() {} }
        internal static Measure Scope(string name) => new();
        internal static void Count(string name) {}
    }
}
namespace GloomhavenVR.Cards
{
    internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
    internal static class CardsConfig { internal static readonly Setting<bool> FaceMipBake = new(true); }
    internal static class CardFace { internal static void Offer(Component root) {} internal static void MaintainArtArrival() {} }
    internal static class CardFxBounds { internal static void Reseat(Component root) {} }
}
