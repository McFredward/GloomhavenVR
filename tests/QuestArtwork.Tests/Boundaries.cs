using System;
using UnityEngine;
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
