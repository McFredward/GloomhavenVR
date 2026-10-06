using System;
using UnityEngine;
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Info(string scope, string text) => Debug.Log(scope + ": " + text);
        internal static void Warn(string scope, string text) => Debug.LogWarning(scope + ": " + text);
    }
    internal static class BundleShaders { internal static Shader? Resolve(string name, string scope, string ready, string pending) => Shader.Find(name); }
    internal static class PerfMonitor
    {
        internal readonly struct Measure : IDisposable { public void Dispose() { } }
        internal static Measure Scope(string name) => new();
        internal static void Count(string name) { }
    }
}
namespace GloomhavenVR.WorldUI { internal static class PanelMipBake { internal static Texture OriginalFor(Texture texture) => texture; } }
namespace GloomhavenVR.Cards
{
    internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
    internal static class CardsConfig { internal static readonly Setting<bool> FaceMipBake = new(true); }
    // Native silhouettes and effect-layout code are unrelated to texture minification.
    // Actual Unity Image, Sprite, texture readback/cache/bake/watch code is production.
    internal static class CardFace { internal static void Offer(Component root) { } internal static void MaintainArtArrival() { } }
    internal static class CardFxBounds { internal static void Reseat(Component root) { } }
}
