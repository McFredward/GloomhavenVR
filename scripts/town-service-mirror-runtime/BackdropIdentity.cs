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
        var red = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "T_noise_shards" };
        var blue = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "T_noise_shards" };
        var redTwin = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "T_noise_shards" };
        red.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); red.Apply();
        blue.SetPixels(new[] { Color.blue, Color.blue, Color.blue, Color.blue }); blue.Apply();
        redTwin.SetPixels(new[] { Color.red, Color.red, Color.red, Color.red }); redTwin.Apply();
        var owner = new TownServiceAssets(); var observer = new TownServiceAssets();
        string redKey = owner.Key(red), blueKey = owner.Key(blue);
        Check(redKey != blueKey, "same-name noise textures with different pixels never alias");
        Check(observer.Key(redTwin) == redKey, "independent client resolves same original noise pixels without instance IDs");
        var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            { name = "sactx-0-4096x4096-DXT5|BC3-BattleOverlayCanvas-811e9640" };
        var atlasTwin = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = atlas.name };
        atlas.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green }); atlas.Apply();
        atlasTwin.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green }); atlasTwin.Apply();
        Check(owner.Key(atlas) == owner.Key(atlasTwin),
            "native atlas wrappers sharing the embedded content ID retain one asset identity");
        Object.Destroy(red); Object.Destroy(blue); Object.Destroy(redTwin);
        Object.Destroy(atlas); Object.Destroy(atlasTwin);
    }
}
