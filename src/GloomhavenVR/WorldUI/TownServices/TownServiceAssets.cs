using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Separate optional-at-runtime art and voice banks; an incomplete install retains native services.
/// The name deliberately avoids the historical main-bundle substring discovery contract.</summary>
internal static class TownServiceAssets
{
    internal const string BundleName = "ghvr-town.bundle";
    internal const string VoiceBundleName = "ghvr-town-voices.bundle";

    private enum LoadPhase { Idle, Bundle, Assets, Ready, Missing }
    private sealed class PendingAsset
    {
        internal string Path = null!;
        internal AssetBundleRequest Request = null!;
    }

    private static LoadPhase _phase;
    private static AssetBundle? _bundle;
    private static AssetBundle? _voiceBundle;
    private static AssetBundleCreateRequest? _bundleRequest;
    private static AssetBundleCreateRequest? _voiceBundleRequest;
    private static bool _voiceFileMissing;
    private static readonly List<PendingAsset> Pending = new();
    private static readonly Dictionary<string, UnityEngine.Object> Loaded = new(StringComparer.OrdinalIgnoreCase);
    private static float _started;

    internal static bool IsLoading => _phase is LoadPhase.Bundle or LoadPhase.Assets;
    internal static bool IsUnavailable => _phase == LoadPhase.Missing;

    /// <summary>Start while the player is still in the menu. The build-571 first map frame
    /// spent 4262.80 ms in TownServicePopulation; its first prefab query opened this
    /// 100 MB UnityFS bundle through synchronous LoadFromFile. Prefabs and resident
    /// speech were then first touched by that same population pass. A map reached
    /// before these async requests complete keeps its native windows and retries.</summary>
    internal static void BeginPreload()
    {
        if (_phase != LoadPhase.Idle) return;
        _started = Time.realtimeSinceStartup;
        try
        {
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null) continue;
                if (String.Equals(bundle.name, BundleName, StringComparison.OrdinalIgnoreCase)) _bundle = bundle;
                if (String.Equals(bundle.name, VoiceBundleName, StringComparison.OrdinalIgnoreCase)) _voiceBundle = bundle;
            }

            string directory = RuntimeDepsLoader.PluginDir;
            string artPath = Path.Combine(directory, BundleName);
            if (_bundle == null && !File.Exists(artPath)) { _phase = LoadPhase.Missing; return; }
            if (_bundle == null) _bundleRequest = AssetBundle.LoadFromFileAsync(artPath);
            string voicePath = Path.Combine(directory, VoiceBundleName);
            _voiceFileMissing = _voiceBundle == null && !File.Exists(voicePath);
            if (_voiceBundle == null && File.Exists(voicePath))
                _voiceBundleRequest = AssetBundle.LoadFromFileAsync(voicePath);
            else if (_voiceFileMissing)
                VRLog.Warn("TownServices", "Town voice bundle is missing; resident art remains available without speech.");
            if (_bundleRequest == null && _voiceBundleRequest == null) BeginAssetLoads();
            else
            {
                _phase = LoadPhase.Bundle;
                VRLog.Debug("TownServices", "Town art and voice async preload started.");
            }
        }
        catch (Exception e)
        {
            _phase = LoadPhase.Missing;
            VRLog.Warn("TownServices", "Town art preload could not start (" + e.GetType().Name
                + "); native service windows remain available.");
        }
    }

    internal static void Tick()
    {
        if (_phase == LoadPhase.Idle)
        {
            if (WorldUIConfig.ImmersiveTownServices.Value) BeginPreload();
            else return;
        }
        if (_phase == LoadPhase.Bundle)
        {
            if (_bundleRequest != null && !_bundleRequest.isDone
                || _voiceBundleRequest != null && !_voiceBundleRequest.isDone) return;
            // Reading assetBundle before isDone blocks the Unity main thread.
            if (_bundleRequest != null) _bundle = _bundleRequest.assetBundle;
            if (_voiceBundleRequest != null) _voiceBundle = _voiceBundleRequest.assetBundle;
            _bundleRequest = null;
            _voiceBundleRequest = null;
            if (_bundle == null)
            {
                _phase = LoadPhase.Missing;
                VRLog.Warn("TownServices", "Town art bundle could not be loaded; native service windows remain available.");
                return;
            }
            if (_voiceBundle == null && !_voiceFileMissing)
                VRLog.Warn("TownServices", "Town voice bundle could not be loaded; resident art remains available without speech.");
            BeginAssetLoads();
        }
        if (_phase != LoadPhase.Assets) return;
        foreach (PendingAsset asset in Pending)
            if (!asset.Request.isDone) return;
        foreach (PendingAsset asset in Pending)
        {
            // Reading asset before isDone can synchronously finish decompression.
            UnityEngine.Object value = asset.Request.asset;
            if (value != null) Loaded[asset.Path] = value;
        }
        int loaded = Loaded.Count;
        Pending.Clear();
        _phase = LoadPhase.Ready;
        VRLog.Debug("TownServices", "Town service assets async preload ready: " + loaded + " assets in "
            + (Time.realtimeSinceStartup - _started).ToString("0.00") + " s.");
    }

    private static void BeginAssetLoads()
    {
        try
        {
            // Resolve by full bundle path, not Object.name: shader names are their
            // render-pipeline identifiers, not their filenames.
            foreach (AssetBundle? bundle in new[] { _bundle, _voiceBundle })
            {
                if (bundle == null) continue;
                foreach (string path in bundle.GetAllAssetNames())
                {
                    if (!path.StartsWith("assets/bundle/townservices/", StringComparison.OrdinalIgnoreCase)
                        || !(path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))) continue;
                    Pending.Add(new PendingAsset { Path = path, Request = bundle.LoadAssetAsync(path) });
                }
            }
            _phase = Pending.Count > 0 ? LoadPhase.Assets : LoadPhase.Missing;
            if (_phase == LoadPhase.Missing)
                VRLog.Warn("TownServices", "Town art bundle contains no service assets; native windows remain available.");
        }
        catch (Exception e)
        {
            Pending.Clear();
            _phase = LoadPhase.Missing;
            VRLog.Warn("TownServices", "Town art assets could not be preloaded (" + e.GetType().Name
                + "); native service windows remain available.");
        }
    }

    private static T? Get<T>(string path) where T : UnityEngine.Object
    {
        if (_phase == LoadPhase.Idle) BeginPreload();
        Tick();
        return _phase == LoadPhase.Ready && Loaded.TryGetValue(path, out UnityEngine.Object? value)
            ? value as T : null;
    }

    internal static GameObject? Prefab(string name) => Get<GameObject>(
        "assets/bundle/townservices/prefabs/" + name + ".prefab");

    internal static Shader? Shader(string name) => Get<Shader>(
        "assets/bundle/townservices/shaders/" + name + ".shader");

    internal static AudioClip? Audio(string name) => Get<AudioClip>(
        "assets/bundle/townservices/audio/" + name + ".wav");

    internal static TextAsset? Text(string name) => Get<TextAsset>(
        "assets/bundle/townservices/audio/" + name + ".json");

    internal static void Reset()
    {
        // Unity offers no cancellation for AssetBundleCreateRequest/AssetBundleRequest.
        // Keep in-flight requests and immutable completed assets across a WorldUI
        // shutdown, then adopt them on the next Init. The old loader also retained
        // the loaded bundle for hot reload rather than unloading live instances.
        if (_phase == LoadPhase.Missing) _phase = LoadPhase.Idle;
    }
}
