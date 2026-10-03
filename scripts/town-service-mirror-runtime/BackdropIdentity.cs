using TownServiceAssets = GloomhavenVR.Net.TownServices.TownServiceAssets;
using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static Dictionary<string, Transform> BackdropRoots(bool reverse, out Texture2D merchant, out Texture2D temple)
    {
        // Same descriptor, different original pixels, as with native path IDs 62/282.
        merchant = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Black_Backdrop" };
        temple = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Black_Backdrop" };
        merchant.SetPixels(new[] { Color.red, Color.green, Color.red, Color.green }); merchant.Apply();
        temple.SetPixels(new[] { Color.blue, Color.yellow, Color.blue, Color.yellow }); temple.Apply();
        Assets.Add(merchant); Assets.Add(temple);
        var first = Sprite.Create(merchant, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        var second = Sprite.Create(temple, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        first.name = second.name = "Black_Backdrop"; Assets.Add(first); Assets.Add(second);
        string[] keys = { "merchant", "temple", "enchant", "item.confirm", "enhance.confirm" };
        if (reverse) Array.Reverse(keys);
        var roots = new Dictionary<string, Transform>();
        foreach (string key in keys)
        {
            var root = Go("Native " + key).transform;
            root.gameObject.AddComponent<Image>().sprite = key == "merchant" || key == "enchant" ? first : second;
            roots.Add(key, root);
        }
        return roots;
    }

    private static void BackdropIdentity()
    {
        MaterialPropertyBudget();
        DuplicateNativeTextures();
        var ownerRoots = BackdropRoots(false, out Texture2D ownerMerchant, out Texture2D ownerTemple);
        var viewerRoots = BackdropRoots(true, out Texture2D viewerMerchant, out Texture2D viewerTemple);
        var owner = new TownServiceAssets(); var viewer = new TownServiceAssets();
        // A prior broad discovery collision must not poison later verified provenance.
        owner.Key(ownerMerchant);
        string previousSpriteKey = owner.Key(ownerRoots["merchant"].GetComponent<Image>().sprite);
        bool refused = false;
        try { owner.Key(ownerTemple); } catch (InvalidDataException) { refused = true; }
        Check(refused, "different original pixels with same descriptor are not guessed equal");
        TownServiceBackdropAssets.Register(owner, key => ownerRoots[key]);
        TownServiceBackdropAssets.Register(viewer, key => viewerRoots[key]);
        Check(owner.Key(ownerRoots["merchant"].GetComponent<Image>().sprite) != previousSpriteKey,
            "explicit original sprite replaces its previously cached descriptor key");
        for (int lifetime = 0; lifetime < 2; lifetime++)
        {
            string first = owner.Key(ownerMerchant), second = owner.Key(ownerTemple);
            Check(first != second, "different native backdrops retain distinct identities");
            Check(first == "native-town|backdrop|merchant|texture", "shared original keeps merchant provenance");
            Check(second == "native-town|backdrop|temple|texture", "shared confirmation backdrop keeps temple provenance");
            Check(viewer.Key(viewerMerchant) == first && viewer.Key(viewerTemple) == second,
                "different client objects and lookup order produce identical original keys");
            ushort module = 1;
            foreach (string template in new[] { "merchant", "temple", "enchant", "item.confirm", "enhance.confirm" })
            {
                using var source = new TownServiceBinding(ownerRoots[template]);
                using var target = new TownServiceBinding(viewerRoots[template]);
                var frame = new TownServiceFrame { Service = 2, Session = 1, Module = module++, Template = 1,
                    Sequence = 1, Structure = source.Structure, Visible = true, Nodes = source.Read(owner),
                    Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
                byte[] bytes = TownServiceCodec.Write(frame);
                Check(TownServiceCodec.TryRead(bytes, bytes.Length, out TownServiceFrame? decoded), template + " original capture encodes");
                target.Validate(decoded!, viewer); target.Apply(decoded!, viewer);
                var copied = target.Root.GetComponent<Image>().sprite.texture;
                var expected = template == "merchant" || template == "enchant" ? viewerMerchant : viewerTemple;
                Check(copied == expected, template + " observer binds exact corresponding original backdrop");
                Check(copied.GetPixel(0, 0) == ownerRoots[template].GetComponent<Image>().sprite.texture.GetPixel(0, 0),
                    template + " different backdrop pixels survive capture and playback");
            }
            uint previous = owner.Generation;
            owner.Clear(); viewer.Clear();
            Check(owner.Generation != previous, "registry reset invalidates native binding generation");
            TownServiceBackdropAssets.Register(owner, key => ownerRoots[key]);
            TownServiceBackdropAssets.Register(viewer, key => viewerRoots[key]);
        }
        // Unknown same-name content remains fail-closed; the fix is explicit provenance,
        // not weakening descriptor collision detection for arbitrary dynamic card textures.
        var unrelated = new TownServiceAssets(); unrelated.Key(ownerMerchant);
        refused = false;
        try { unrelated.Key(ownerTemple); } catch (InvalidDataException) { refused = true; }
        Check(refused, "unregistered descriptor collisions still fail closed");
    }

    private static void MaterialPropertyBudget()
    {
        Shader shader = Shader.Find("GVR/TownManyProps");
        Check(shader != null && shader.GetPropertyCount() == 65,
            "material fixture has more properties than the old 60-property production limit");
        var material = new Material(shader);
        material.SetFloat("_P64", .73f);
        var owner = new TownServiceAssets(); var observer = new TownServiceAssets();
        owner.Key(shader); observer.Key(shader);
        TownServiceValue sampled = TownServiceMaterial.Read(material, owner);
        Check(sampled.Text.Length == 132 && sampled.Numbers.Length == 329,
            "all 65 original shader properties fit the bounded wire value");
        TownServiceMaterial.Validate(sampled, observer);
        Material? mirrored = TownServiceMaterial.Apply(sampled, observer, null);
        Check(mirrored != null && Mathf.Abs(mirrored.GetFloat("_P64") - .73f) < .0001f,
            "65th original material property survives peer reconstruction");
        TownServiceMaterial.Release(mirrored);
        Object.Destroy(material);
    }

    private static void DuplicateNativeTextures()
    {
        var red = new Texture2D(512, 512, TextureFormat.DXT1, true) { name = "T_noise_shards" };
        var redTwin = new Texture2D(512, 512, TextureFormat.DXT1, true) { name = "T_noise_shards" };
        var owner = new TownServiceAssets(); var observer = new TownServiceAssets();
        string redKey = owner.Key(red);
        Check(owner.Key(redTwin) == redKey && observer.Key(redTwin) == redKey,
            "duplicate wrappers of the one original 512x512 noise resource share a GPU-independent identity");
        var unknown = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "T_noise_shards" };
        var unknownTwin = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "T_noise_shards" };
        owner.Key(unknown);
        bool refused = false;
        try { owner.Key(unknownTwin); } catch (InvalidDataException) { refused = true; }
        Check(refused, "same-named noise textures outside the verified native resource size still fail closed");
        var wrongFormat = new Texture2D(512, 512, TextureFormat.RGBA32, true) { name = "T_noise_shards" };
        var wrongFormatTwin = new Texture2D(512, 512, TextureFormat.RGBA32, true) { name = "T_noise_shards" };
        owner.Key(wrongFormat); refused = false;
        try { owner.Key(wrongFormatTwin); } catch (InvalidDataException) { refused = true; }
        Check(refused, "same-named noise textures with a different native format still fail closed");
        var wrongMips = new Texture2D(512, 512, TextureFormat.DXT1, false) { name = "T_noise_shards" };
        var wrongMipsTwin = new Texture2D(512, 512, TextureFormat.DXT1, false) { name = "T_noise_shards" };
        owner.Key(wrongMips); refused = false;
        try { owner.Key(wrongMipsTwin); } catch (InvalidDataException) { refused = true; }
        Check(refused, "same-named noise textures without ten native mip levels still fail closed");
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            { name = "sactx-0-4096x4096-DXT5|BC3-BattleOverlayCanvas-811e9640" };
        var atlasTwin = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = atlas.name };
        atlas.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green }); atlas.Apply();
        atlasTwin.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green }); atlasTwin.Apply();
        Check(owner.Key(atlas) == owner.Key(atlasTwin),
            "native atlas wrappers sharing the embedded content ID retain one asset identity");
        foreach (var verified in new[]
        {
            (name: "AbilityCardSpriteAtlas", width: 2048, height: 2048, format: TextureFormat.DXT5),
            (name: "Sarala-Regular SDF Atlas", width: 2048, height: 1024, format: TextureFormat.Alpha8)
        })
        {
            var original = new Texture2D(verified.width, verified.height, verified.format, false) { name = verified.name };
            var wrapper = new Texture2D(verified.width, verified.height, verified.format, false) { name = verified.name };
            var otherFormat = new Texture2D(verified.width, verified.height, TextureFormat.RGBA32, false) { name = verified.name };
            var otherFormatTwin = new Texture2D(verified.width, verified.height, TextureFormat.RGBA32, false) { name = verified.name };
            Check(owner.Key(original) == owner.Key(wrapper) && observer.Key(wrapper) == owner.Key(original),
                "verified original item/text atlas wrappers share one identity on both peers: " + verified.name);
            owner.Key(otherFormat);
            refused = false;
            try { owner.Key(otherFormatTwin); } catch (InvalidDataException) { refused = true; }
            Check(refused, "unverified texture format still fails closed: " + verified.name);
            Object.Destroy(original); Object.Destroy(wrapper); Object.Destroy(otherFormat); Object.Destroy(otherFormatTwin);
        }
        foreach (var verified in new[]
        {
            (name: "T_Noise_Spherical_Sparks", width: 512, height: 512, format: TextureFormat.RGB24, mips: true),
            (name: "HeroHighlight_Darken", width: 300, height: 218, format: TextureFormat.RGBA32, mips: false),
            (name: "T_flowmap_outwards", width: 1024, height: 1024, format: TextureFormat.DXT5, mips: true)
        })
        {
            var original = new Texture2D(verified.width, verified.height, verified.format, verified.mips) { name = verified.name };
            var wrapper = new Texture2D(verified.width, verified.height, verified.format, verified.mips) { name = verified.name };
            Check(owner.Key(original) == owner.Key(wrapper) && observer.Key(wrapper) == owner.Key(original),
                "Build609's duplicated audited original dependency binds on both peers: " + verified.name);
            var mismatch = new Texture2D(verified.width, verified.height, verified.format, !verified.mips) { name = verified.name };
            var mismatchTwin = new Texture2D(verified.width, verified.height, verified.format, !verified.mips) { name = verified.name };
            owner.Key(mismatch); refused = false;
            try { owner.Key(mismatchTwin); } catch (InvalidDataException) { refused = true; }
            Check(refused, "the audited descriptor does not admit a different native mip chain: " + verified.name);
            if (verified.name == "HeroHighlight_Darken")
            {
                Sprite first = Sprite.Create(original, new Rect(0f, 0f, 300f, 218f), Vector2.one * .5f);
                Sprite second = Sprite.Create(wrapper, new Rect(0f, 0f, 300f, 218f), Vector2.one * .5f);
                first.name = second.name = verified.name;
                Transform row = Rect("Native merchant row", Go("native original merchant row holder").transform,
                    Vector2.zero, new Vector2(300f, 218f));
                row.gameObject.AddComponent<Image>().sprite = first;
                TownServiceTemplateAssets.Register(owner, "merchant.row", row);
                string key = owner.Key(first);
                Check(key.StartsWith("sprite|texture|HeroHighlight_Darken|", StringComparison.Ordinal),
                    "native merchant hover sprite has canonical resource identity rather than first template-borrow identity");
                Check(observer.Key(second) == key && observer.Resolve<Sprite>(key) == second,
                    "merchant hover sprite resolves when the receiving original item row was borrowed in another order");
                Object.Destroy(first); Object.Destroy(second);
            }
            Object.Destroy(original); Object.Destroy(wrapper); Object.Destroy(mismatch); Object.Destroy(mismatchTwin);
        }
        Object.Destroy(red); Object.Destroy(redTwin); Object.Destroy(unknown); Object.Destroy(unknownTwin);
        Object.Destroy(wrongFormat); Object.Destroy(wrongFormatTwin); Object.Destroy(wrongMips); Object.Destroy(wrongMipsTwin);
        Object.Destroy(atlas); Object.Destroy(atlasTwin);
    }
}
