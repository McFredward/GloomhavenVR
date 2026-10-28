using System;
using UnityEngine;

namespace BepInEx { internal static class Paths { internal static string ConfigPath => System.IO.Path.GetFullPath("FixtureConfig"); } }
namespace GloomhavenVR { internal static class MyPluginInfo { internal const string PLUGIN_GUID = "fixture.quest.loading"; } }
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled = true; }
    // The Android embedded bank loader has its separate production provenance
    // gate. This native Linux fixture compiles and renders its exact shader
    // source; it cannot load an Android AssetBundle into the Linux Editor.
    internal static class QuestLoadingAssets
    {
        internal static Shader? ResolveShader() => Shader.Find("GloomhavenVR/QuestLoadingSpinner");
    }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal enum VRLogLevel { Debug }
    internal static class VRLog
    {
        internal static bool Wants(VRLogLevel level) => true;
        internal static void Info(string category, string text) => UnityEngine.Debug.Log("[Lifecycle] " + text);
        internal static void Warn(string category, string text) => UnityEngine.Debug.LogWarning("[Lifecycle] " + text);
        internal static void Error(string category, string text) => UnityEngine.Debug.LogError("[Lifecycle] " + text);
    }
    internal static class VRLayers
    {
        internal static void Apply(GameObject root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 29;
        }
    }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.WorldUI
{
    internal sealed partial class FlatScreen
    {
        internal static bool ScreenVisible;
        private bool _visible;
        private Renderer? _quadRenderer;
        private sealed class CapturedCamera { internal bool IsUi; internal RenderTexture? Target; }
        private static readonly System.Collections.Generic.Dictionary<Camera, CapturedCamera> CapturedSet = new();
        private RenderTexture? TargetFor(CapturedCamera camera) => camera.Target;
        internal void Setup(Camera camera, RenderTexture target, GameObject consumer)
        {
            _visible = ScreenVisible = true;
            _quadRenderer = consumer.GetComponent<Renderer>();
            CapturedSet[camera] = new CapturedCamera { IsUi = true, Target = target };
            consumer.AddComponent<QuestMenuDrawWitness>().Owner = this;
            Camera.onPostRender += ObserveQuestMenuProducer;
        }
        internal void FrameTick()
        {
#if QUEST_MENU_PIXEL_WITNESS
            ObserveQuestMenuFrame();
#endif
        }
        internal void Stop()
        {
#if QUEST_MENU_PIXEL_WITNESS
            ReleaseQuestMenuReceipt();
#endif
            Camera.onPostRender -= ObserveQuestMenuProducer;
            CapturedSet.Clear(); _visible = ScreenVisible = false;
        }
    }
    internal sealed class Entry { internal bool Value; internal Entry(bool value) { Value = value; } }
    internal static class WorldUIConfig
    {
        internal static readonly Entry LoadingIndicator = new Entry(true);
        internal static readonly Entry ImmersiveTownServices = new Entry(false);
    }
    internal static class ScenarioInteractionPreparation { internal static bool ShowsLoadingIndicator => false; }
}
namespace GloomhavenVR.WorldUI.MapRoom { internal static class MapRoomDriver { internal static bool Active => false; } }
namespace GloomhavenVR.Net { internal static class NetAvatarDriver { internal static bool PrepareMerchantCardsForLoading() => true; } }
namespace GloomhavenVR.Net.TownServices
{
    internal static class TownServiceAssets { internal static bool IsUnavailable => false; internal static bool IsLoading => false; }
    internal static class TownServiceAvailability { internal static bool NativeUnlocked(byte service) => false; }
    internal static class TownServicePopulation { internal static bool Available(byte service) => true; }
    internal static class TownServiceGrantSync { internal static bool CanUseImmersive => false; }
    internal static class TownServicePresentation { internal static bool NativeFallbackFor(byte service) => false; }
    internal static class TownServicePublicMerchant { internal static object? Catalog => null; internal static bool PrepareCatalogForLoading() => true; }
}
