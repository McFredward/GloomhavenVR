using GloomhavenVR.WorldUI;
using UnityEngine;

string bundlePath = Path.Combine(AppContext.BaseDirectory, TownServiceAssets.BundleName);
if (args[0] == "missing")
{
    File.Delete(bundlePath);
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
AssetBundle.Next = new AssetBundle();
AssetBundle.Next.Assets.Add("assets/bundle/townservices/prefabs/townmerchant.prefab", new GameObject());
AssetBundle.Next.Assets.Add("assets/bundle/townservices/prefabs/townpriestess.prefab", new GameObject());
AssetBundle.Next.Assets.Add("assets/bundle/townservices/prefabs/townenchantress.prefab", new GameObject());
AssetBundle.Next.Assets.Add("assets/bundle/townservices/prefabs/townworktray.prefab", new GameObject());
AssetBundle.Next.Assets.Add("assets/bundle/townservices/shaders/townnpc.shader", new Shader());
AssetBundle.Next.Assets.Add("assets/bundle/townservices/audio/merchant-greet.wav", new AudioClip());
AssetBundle.Next.Assets.Add("assets/bundle/townservices/audio/merchant-greet.json", new TextAsset());
AssetBundle.Next.Assets.Add("assets/bundle/townservices/textures/irrelevant.png", new Texture());
TownServiceAssets.BeginPreload();
Check(TownServiceAssets.IsLoading && AssetBundle.LoadCalls == 1, "async bundle started once");
Check(TownServiceAssets.Prefab("townmerchant") == null && AssetBundle.Next.Requests.Count == 0,
    "no main-thread bundle completion or premature asset load");
TownServiceAssets.Tick();
Check(AssetBundle.Next.Requests.Count == 0, "unfinished bundle remains untouched");
AssetBundle.BundleRequest!.isDone = true;
TownServiceAssets.Tick();
Check(AssetBundle.Next.Requests.Count == 7 && TownServiceAssets.IsLoading,
    "only direct prefab, shader and voice assets requested");
Check(TownServiceAssets.Audio("merchant-greet") == null, "unfinished assets remain invisible");
foreach (AssetBundleRequest request in AssetBundle.Next.Requests) request.isDone = true;
TownServiceAssets.Tick();
Check(!TownServiceAssets.IsLoading, "all requests completed");
Check(TownServiceAssets.Prefab("townmerchant") != null
    && TownServiceAssets.Prefab("townworktray") != null
    && TownServiceAssets.Shader("townnpc") != null
    && TownServiceAssets.Audio("merchant-greet") != null
    && TownServiceAssets.Text("merchant-greet") != null, "every direct lookup resolves by bundle path");
TownServiceAssets.Reset();
TownServiceAssets.BeginPreload();
Check(AssetBundle.LoadCalls == 1 && TownServiceAssets.Prefab("townmerchant") != null,
    "shutdown preserves uncancellable or loaded art");
File.Delete(bundlePath);
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
        public AssetBundle? assetBundle => isDone ? AssetBundle.Next : throw new Exception("blocking bundle read");
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
        public static AssetBundle? Next;
        public static AssetBundleCreateRequest? BundleRequest;
        public readonly Dictionary<string, Object> Assets = new();
        public readonly List<AssetBundleRequest> Requests = new();
        public string name => TownServiceAssets.BundleName;
        public static IEnumerable<AssetBundle> GetAllLoadedAssetBundles() => Array.Empty<AssetBundle>();
        public static AssetBundleCreateRequest LoadFromFileAsync(string path)
        { LoadCalls++; return BundleRequest = new AssetBundleCreateRequest(); }
        public string[] GetAllAssetNames() => Assets.Keys.ToArray();
        public AssetBundleRequest LoadAssetAsync(string path)
        {
            var request = new AssetBundleRequest { value = Assets[path] };
            Requests.Add(request); return request;
        }
    }
}
