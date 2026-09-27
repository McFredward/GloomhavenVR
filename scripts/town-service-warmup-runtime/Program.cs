using GloomhavenVR.WorldUI;
using UnityEngine;

string bundlePath = Path.Combine(AppContext.BaseDirectory, TownServiceAssets.BundleName);
string voicePath = Path.Combine(AppContext.BaseDirectory, TownServiceAssets.VoiceBundleName);
if (args[0] == "missing")
{
    File.Delete(bundlePath);
    File.Delete(voicePath);
    TownServiceAssets.BeginPreload();
    Check(!TownServiceAssets.IsLoading && AssetBundle.LoadCalls == 0, "optional missing bundle");
    File.WriteAllText(bundlePath, "fixture");
    TownServiceAssets.Reset();
    TownServiceAssets.BeginPreload();
    Check(TownServiceAssets.IsLoading && AssetBundle.LoadCalls == 1, "hot reload retries installed bundle");
    File.Delete(bundlePath);
    Console.WriteLine("Town warmup missing/retry fixture passed");
    return;
}

File.WriteAllText(bundlePath, "fixture");
File.WriteAllText(voicePath, "fixture");
AssetBundle.Art = new AssetBundle(TownServiceAssets.BundleName);
AssetBundle.Voices = new AssetBundle(TownServiceAssets.VoiceBundleName);
AssetBundle.Art.Assets.Add("assets/bundle/townservices/prefabs/townmerchant.prefab", new GameObject());
AssetBundle.Art.Assets.Add("assets/bundle/townservices/prefabs/townpriestess.prefab", new GameObject());
AssetBundle.Art.Assets.Add("assets/bundle/townservices/prefabs/townenchantress.prefab", new GameObject());
AssetBundle.Art.Assets.Add("assets/bundle/townservices/prefabs/townworktray.prefab", new GameObject());
AssetBundle.Art.Assets.Add("assets/bundle/townservices/shaders/townnpc.shader", new Shader());
AssetBundle.Art.Assets.Add("assets/bundle/townservices/textures/irrelevant.png", new Texture());
AssetBundle.Voices.Assets.Add("assets/bundle/townservices/audio/merchant-greet.wav", new AudioClip());
AssetBundle.Voices.Assets.Add("assets/bundle/townservices/audio/merchant-greet.json", new TextAsset());
TownServiceAssets.BeginPreload();
Check(TownServiceAssets.IsLoading && AssetBundle.LoadCalls == 2, "both bundles start asynchronously once");
Check(TownServiceAssets.Prefab("townmerchant") == null && AssetBundle.Art.Requests.Count == 0,
    "no main-thread bundle completion or premature asset load");
TownServiceAssets.Tick();
Check(AssetBundle.Art.Requests.Count == 0, "unfinished bundle remains untouched");
AssetBundle.ArtRequest!.isDone = true;
TownServiceAssets.Tick();
Check(AssetBundle.Art.Requests.Count == 0 && AssetBundle.Voices.Requests.Count == 0,
    "partially loaded bundle pair never starts a blocking asset read");
AssetBundle.VoiceRequest!.isDone = true;
TownServiceAssets.Tick();
Check(AssetBundle.Art.Requests.Count == 5 && AssetBundle.Voices.Requests.Count == 2
    && TownServiceAssets.IsLoading, "art and speech each request only their own direct assets");
Check(TownServiceAssets.Audio("merchant-greet") == null, "unfinished assets remain invisible");
foreach (AssetBundleRequest request in AssetBundle.Art.Requests.Concat(AssetBundle.Voices.Requests)) request.isDone = true;
TownServiceAssets.Tick();
Check(!TownServiceAssets.IsLoading, "all requests completed");
Check(TownServiceAssets.Prefab("townmerchant") != null
    && TownServiceAssets.Prefab("townworktray") != null
    && TownServiceAssets.Shader("townnpc") != null
    && TownServiceAssets.Audio("merchant-greet") != null
    && TownServiceAssets.Text("merchant-greet") != null, "every direct lookup resolves by bundle path");
TownServiceAssets.Reset();
TownServiceAssets.BeginPreload();
Check(AssetBundle.LoadCalls == 2 && TownServiceAssets.Prefab("townmerchant") != null,
    "shutdown preserves uncancellable or loaded art");
File.Delete(bundlePath);
File.Delete(voicePath);
Console.WriteLine("Town warmup async lifecycle fixture passed");

static void Check(bool condition, string what)
{
    if (!condition) throw new Exception(what);
}

namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig
    {
        internal static readonly Toggle ImmersiveTownServices = new();
        internal sealed class Toggle { internal bool Value = true; }
    }
}

namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Debug(string area, string message) { }
        internal static void Warn(string area, string message) { }
    }
}

namespace UnityEngine
{
    public class Object { }
    public sealed class GameObject : Object { }
    public sealed class Shader : Object { }
    public sealed class AudioClip : Object { }
    public sealed class TextAsset : Object { }
    public sealed class Texture : Object { }
    public static class Time { public static float realtimeSinceStartup => 1f; }
    public sealed class AssetBundleCreateRequest
    {
        public bool isDone;
        public AssetBundle bundle = null!;
        public AssetBundle? assetBundle => isDone ? bundle : throw new Exception("blocking bundle read");
    }
    public sealed class AssetBundleRequest
    {
        public bool isDone;
        public Object value = null!;
        public Object asset => isDone ? value : throw new Exception("blocking asset read");
    }
    public sealed class AssetBundle
    {
        public static int LoadCalls;
        public static AssetBundle Art = null!;
        public static AssetBundle Voices = null!;
        public static AssetBundleCreateRequest? ArtRequest;
        public static AssetBundleCreateRequest? VoiceRequest;
        public readonly Dictionary<string, Object> Assets = new();
        public readonly List<AssetBundleRequest> Requests = new();
        public string name { get; }
        public AssetBundle(string name) { this.name = name; }
        public static IEnumerable<AssetBundle> GetAllLoadedAssetBundles() => Array.Empty<AssetBundle>();
        public static AssetBundleCreateRequest LoadFromFileAsync(string path)
        {
            LoadCalls++;
            var request = new AssetBundleCreateRequest {
                bundle = Path.GetFileName(path) == TownServiceAssets.VoiceBundleName ? Voices : Art
            };
            if (request.bundle == Voices) VoiceRequest = request;
            else ArtRequest = request;
            return request;
        }
        public string[] GetAllAssetNames() => Assets.Keys.ToArray();
        public AssetBundleRequest LoadAssetAsync(string path)
        {
            var request = new AssetBundleRequest { value = Assets[path] };
            Requests.Add(request); return request;
        }
    }
}
