using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Net.TownServices;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static void AmbientDiscovery674()
    {
        var originalTexture = new Texture2D(2, 2) { name = "Native retired artwork 674" };
        var bakedTexture = new Texture2D(2, 2) { name = "Surviving baked artwork 674" };
        var validTexture = new Texture2D(2, 2) { name = "Current native artwork 674" };
        var original = Sprite.Create(originalTexture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        var baked = Sprite.Create(bakedTexture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        var valid = Sprite.Create(validTexture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        original.name = "Native retired sprite 674"; baked.name = "Surviving baked sprite 674";
        valid.name = "Current native sprite 674";
        Assets.Add(originalTexture); Assets.Add(bakedTexture); Assets.Add(validTexture);
        Assets.Add(baked); Assets.Add(valid);
        CardFaceMipBake.SeedOriginal674(baked, original);
        var registry = new TownServiceAssets();
        registry.Key(baked); // Cache the original identity while that native object is alive.
        Object.DestroyImmediate(original);
        Check(baked != null && CardFaceMipBake.OriginalFor(baked) == null,
            "production mip reverse lookup exposes the retired Unity original of a live baked sprite");
        bool nativeFailure = false;
        try { _ = CardFaceMipBake.OriginalFor(baked).texture; }
        catch (Exception error) when (error is MissingReferenceException || error is NullReferenceException || error is UnityException)
        {
            nativeFailure = true;
            File.WriteAllText(Path.Combine(_output, "native-retired-getter.txt"), error.ToString());
        }
        Check(nativeFailure, "retired original reproduces an actual native sprite getter failure");
        bool strict = false;
        try { registry.Key(baked); }
        catch (InvalidDataException error) { strict = error.Message == "Original town-service asset has retired."; }
        Check(strict, "required lookup rejects a retired original even when its old identity was cached");

        // The production census is global: an unfinished native TMP sprite may belong
        // to a completely unrelated scenario. A required lookup still fails; ambient
        // discovery must continue to the valid current artwork in the same census.
        TMP_SpriteAsset Legacy(string name)
        {
            var value = ScriptableObject.CreateInstance<TMP_SpriteAsset>(); value.name = name;
            var material = new Material(Shader.Find("Hidden/InternalErrorShader")); value.material = material;
            Assets.Add(value); Assets.Add(material); return value;
        }
        var getterLegacy = Legacy("Native getter legacy sprite asset 674");
        bool legacyFailure = false;
        try { _ = getterLegacy.spriteCharacterTable.Count; }
        catch (NullReferenceException error)
        {
            legacyFailure = true;
            File.WriteAllText(Path.Combine(_output, "native-legacy-getter.txt"), error.ToString());
        }
        Check(legacyFailure, "unfinished native legacy TMP asset reproduces the unrelated getter failure");
        var requiredLegacy = Legacy("Required legacy sprite asset 674");
        strict = false;
        try { registry.Key(requiredLegacy); } catch (NullReferenceException) { strict = true; }
        Check(strict, "required TMP artwork lookup remains strict for unfinished native state");
        // Native upgrade writes its version before throwing; use a fresh asset for the
        // census so the earlier diagnostic getter cannot silently repair this control.
        var legacy = Legacy("Unfinished census legacy sprite asset 674");
        for (int partial = 0; partial < 8; partial++) Legacy("Repeated unrelated partial sprite " + partial);
        const string warning = "TownServices: Original town artwork discovery skipped a transient ";
        int warnings = GloomhavenVR.Core.VRLog.Messages.FindAll(line => line.StartsWith(warning, StringComparison.Ordinal)).Count;
        registry.Scan();
        Check(GloomhavenVR.Core.VRLog.Messages.FindAll(line => line.StartsWith(warning, StringComparison.Ordinal)).Count == warnings + 1,
            "repeated unrelated native failures produce one bounded discovery diagnostic per kind");
        var keys = (Dictionary<int, string>)typeof(TownServiceAssets).GetField("_keys", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(registry)!;
        Check(!keys.ContainsKey(baked.GetInstanceID()) && keys.ContainsKey(valid.GetInstanceID()),
            "ambient stale original cannot block discovery of current native artwork");
        registry.RegisterOriginal("native-required-674", valid);
        Check(ReferenceEquals(registry.Resolve<Sprite>("native-required-674"), valid),
            "ambient failure leaves verified required original identity intact");
        for (int lifetime = 0; lifetime < 12; lifetime++)
        {
            registry.Clear(); registry.Scan();
            Check(keys.ContainsKey(valid.GetInstanceID()) && !keys.ContainsKey(baked.GetInstanceID()),
                "scenario and menu registry rebuild survives an unrelated retired sprite");
        }
        CardFaceMipBake.ClearOriginals674(); registry.Clear(); registry.Scan();
        Check(keys.ContainsKey(baked.GetInstanceID()) && keys.ContainsKey(valid.GetInstanceID()),
            "later discovery recovers when the native original provenance becomes current");
        typeof(TMP_SpriteAsset).GetField("m_Version", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(legacy, "1.1.0");
        registry.Clear(); registry.Scan();
        Check(keys.ContainsKey(legacy.GetInstanceID()), "later census retries the native asset after its authored state becomes ready");
    }
}
