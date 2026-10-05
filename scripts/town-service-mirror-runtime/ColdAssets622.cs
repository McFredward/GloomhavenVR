#if PUBLISHER622
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using Object = UnityEngine.Object;
using NativeAssets = GloomhavenVR.Net.TownServices.TownServiceAssets;

internal static class Publisher622PurseTemplate { internal static Transform? Source; }

public static partial class MirrorProgram
{
    private static IEnumerator ColdAssets622()
    {
        string[] names = { "TownNpc", "TownEye", "TownCornea", "TownFlame" };
        foreach (string shortName in names)
            Check(Shader.Find("GloomhavenVR/" + shortName) == null,
                "cold observer has no previously loaded original town shader");
        var resolver = new NativeAssets();
        bool pending = false;
        try { resolver.Resolve<Shader>("shader|GloomhavenVR/TownNpc"); }
        catch (InvalidDataException) { pending = true; }
        Check(pending, "a missing bank is pending rather than a cached substitute shader");

        string[] args = Environment.GetCommandLineArgs();
        string bankPath = args[Array.IndexOf(args, "-publisher622Bank") + 1];
        AssetBundle bank = AssetBundle.LoadFromFile(bankPath);
        Check(bank != null, "observer loads the actual original town shader bank");
        try
        {
            var paths = new HashSet<string>(bank.GetAllAssetNames(), StringComparer.OrdinalIgnoreCase);
            foreach (string shortName in names)
            {
                string name = "GloomhavenVR/" + shortName;
                Check(paths.Contains("Assets/Bundle/TownServices/Shaders/" + shortName + ".shader"),
                    "loaded bank contains each registered original shader asset path");
                Check(Shader.Find(name) == null,
                    "opening a bank alone does not preload its original shader assets");
                Shader? shader = null;
                try { shader = resolver.Resolve<Shader>("shader|" + name); }
                catch (InvalidDataException) { }
                Check(shader != null && shader.name == name && shader.GetPropertyCount() > 0 && shader.isSupported,
                    "cold asset resolution loads the original town shader through its bank path");
                var how = (Dictionary<string, string>)typeof(BundleShaders)
                    .GetField("HowResolved", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                Check(how.TryGetValue(name, out string route) && route.StartsWith("AssetBundle.LoadAsset<Shader>", StringComparison.Ordinal),
                    "cold resolution is proven by AssetBundle.LoadAsset rather than Shader.Find");
                var original = new Material(shader!);
                Material? observed = null;
                try
                {
                    if (original.HasProperty("_TownVisibility")) original.SetFloat("_TownVisibility", .43f);
                    TownServiceValue value = TownServiceMaterial.Read(original, resolver);
                    var recipient = new NativeAssets();
                    TownServiceMaterial.Validate(value, recipient);
                    observed = TownServiceMaterial.Apply(value, recipient, null);
                    Check(observed != null && observed.shader == shader && value.Text.Length == 2 + shader!.GetPropertyCount() * 2,
                        "cold original material validates every actual shader property");
                    Check(!original.HasProperty("_TownVisibility") || Mathf.Abs(observed!.GetFloat("_TownVisibility") - .43f) < .00001f,
                        "cold original material retains its actual owner visibility");
                }
                finally { TownServiceMaterial.Release(observed); Object.DestroyImmediate(original); }
            }
            File.WriteAllLines(Path.Combine(_output, "cold-bank-assets.txt"), bank.GetAllAssetNames());
        }
        finally { bank.Unload(true); }
        AsyncOperation unloaded = Resources.UnloadUnusedAssets();
        yield return unloaded;
        Check(unloaded.isDone, "native unused-asset operation completes before the next proof");
    }

    private static void ColdPhysicalPurse622()
    {
        TownServiceMirror.Shutdown();
        GameObject bank = Go("cold physical template bank"); bank.SetActive(false);
        LazyTemplateProbe.Open(bank);
        try
        {
            Publisher622PurseTemplate.Source = null;
            LazyTemplateProbe.PreparePhysicalPurses();
            Check(LazyTemplateProbe.FixtureEntries == 0,
                "startup prewarm waits for the actual native money bag template");
            GameObject native = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Objects.Add(native); native.name = "Original purse geometry boundary";
            Object.DestroyImmediate(native.GetComponent<Collider>());
            Publisher622PurseTemplate.Source = native.transform;
            int awakes = GameplayFixture.Awakes, enables = GameplayFixture.Enables;
            LazyTemplateProbe.PreparePhysicalPurses();
            Check(LazyTemplateProbe.FixtureEntries == 2,
                "native purse arrival freezes both body and held addresses before any visitor publishes");
            foreach (string key in new[] { "ritual.purse", "ritual.purse.held" })
            {
                var parts = LazyTemplateProbe.Parts(key);
                Check(parts.Count == 1 && parts[0].Original.GetComponent<MeshRenderer>() != null
                    && !parts[0].Original.gameObject.activeInHierarchy,
                    "both prewarmed native purse templates contain inert physical geometry");
                Check(LazyTemplateProbe.Resolve(2, 1, key + "|"),
                    "both purse templates resolve before the first temple manifest");
            }
            for (int i = 0; i < 8; i++) LazyTemplateProbe.PreparePhysicalPurses();
            Check(LazyTemplateProbe.FixtureEntries == 2 && GameplayFixture.Awakes == awakes && GameplayFixture.Enables == enables,
                "repeated physical prewarm preserves the original two templates without gameplay callbacks");
        }
        finally { Publisher622PurseTemplate.Source = null; LazyTemplateProbe.Close(); TownServiceMirror.Shutdown(); }
    }
}
#endif
