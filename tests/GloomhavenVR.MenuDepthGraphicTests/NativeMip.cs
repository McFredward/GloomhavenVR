using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthGraphicProgram
{
    public static int NativeMip(string output, Sprite native)
    {
        _checks = 0; _output = output;
        Check(native.name == "CR_DF_03_Demon" && native.rect.size == new Vector2(1920, 1080)
            && native.textureRectOffset.x > 10f && native.textureRectOffset.x < 10.2f
            && native.textureRect.width < 1893f && native.textureRect.width > 1892.8f,
            "the actual shipped illustration retains its genuine fractional native trim metadata");
        Check(MenuDepthCatalogue.EnsureLoaded(), "the actual complete catalogue loads before native mip binding");
        Sprite mip = CardFaceMipBake.ReplacementFor(native)
            ?? throw new Exception("Actual mip producer failed to reconstruct the native illustration.");
        Check(mip != native && CardFaceMipBake.OriginalFor(mip) == native && mip.texture.mipmapCount > 1
            && mip.rect.position == Vector2.zero && mip.textureRectOffset == Vector2.zero,
            "the entire production mip producer rebuilds the genuinely trimmed sprite and registers exact original provenance");
        MenuDepthCatalogue.Artwork art = MenuDepthCatalogue.NarrativeArt(native.name, mip)
            ?? throw new Exception("the actual trimmed native mip clone resolves its original prepared field through owned provenance");
        Check(art.source_id == "ff379b37216c9ff005c7f6f313a3f5cb",
            "the actual trimmed native mip clone resolves its original prepared field through owned provenance");
        Check(MenuDepthCatalogue.TryDepthUv(art, mip, out Vector4 nativeUv) && nativeUv == new Vector4(1, 1, 0, 0),
            "the observed full-texture illustration keeps exact source pixel alignment after rounded trim reconstruction");
        Sprite spoof = Sprite.Create(mip.texture, mip.rect, Vector2.one * .5f); spoof.name = mip.name;
        Check(CardFaceMipBake.OriginalFor(spoof) == spoof && MenuDepthCatalogue.NarrativeArt(native.name, spoof) == null,
            "an unrelated sprite with the exact VR-mip display name cannot impersonate owned source provenance");
        MenuDepthHeightfield field = MenuDepthCatalogue.Load(art)!;
        Check(field != null && field.SourceWidth == 1920 && field.SourceHeight == 1080,
            "the observed illustration binds its actual prepared SHA and CRC checked relief");
        Sprite second = Resources.FindObjectsOfTypeAll<Sprite>().First(s => s != native && s != mip && s != spoof
            && s.texture != null && MenuDepthCatalogue.NarrativeArt(s.name, s) != null);
        using (var scene = new Scene(native))
        {
            var toolsObject = new GameObject("actual UIInfoTools mip input"); toolsObject.SetActive(false);
            UIInfoTools tools = toolsObject.AddComponent<UIInfoTools>();
            tools.narrativeImages = new[] { new NarrativeConfigUI { id = native.name, picture = native },
                new NarrativeConfigUI { id = second.name, picture = second } };
            tools.defaultEventImage = native; tools.defaultCityEventImage = null!;
            typeof(UIInfoTools).GetField("<Instance>k__BackingField", Static)!.SetValue(null, tools);
            var animatorObject = new GameObject("unplayed source animator"); animatorObject.SetActive(false);
            GUIAnimator animator = animatorObject.AddComponent<LeanTweenGUIAnimator>();
            var storyObject = new GameObject("native loadout story owner"); storyObject.SetActive(false);
            storyObject.transform.SetParent(scene.Panel, false);
            StoryImageViewer story = storyObject.AddComponent<StoryImageViewer>();
            Set(story, "loadImagesAnimator", animator); Set(story, "imageHolder", scene.Image); Set(story, "currentImage", native.name);
            storyObject.SetActive(true);
            CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = scene.Panel });
            try
            {
                MenuDepthDriver.Tick();
                MenuDepthGraphic graphic = scene.Image.GetComponent<MenuDepthGraphic>()
                    ?? throw new Exception("actual story driver admits the original before its native mip replacement");
                Check(ReferenceEquals(Field(graphic), field), "actual story driver admits the original before its native mip replacement");
                Set(story, "currentImage", second.name); scene.Image.sprite = mip; MenuDepthDriver.Tick();
                Check(graphic.enabled && graphic.IsReady && ReferenceEquals(Field(graphic), field) && scene.Image.sprite == mip,
                    "a native-to-mip swap during async story loading retains the visible old picture's own relief");
                scene.Image.sprite = second; MenuDepthDriver.Tick();
                Check(graphic.enabled && graphic.IsReady && Field(graphic) != null && !ReferenceEquals(Field(graphic), field),
                    "an arriving new native picture replaces the old relief before its later mip swap");
                Sprite secondMip = CardFaceMipBake.ReplacementFor(second) ?? second;
                scene.Image.sprite = secondMip; MenuDepthDriver.Tick();
                Check(graphic.enabled && graphic.IsReady && scene.Image.sprite == secondMip,
                    "the newly arrived picture retains its own prepared relief through presentation replacement");
                Set(story, "currentImage", native.name); scene.Image.sprite = mip; MenuDepthDriver.Tick();
                Check(graphic.enabled && graphic.IsReady && ReferenceEquals(Field(graphic), field),
                    "reopening the native loadout picture binds an already prepared mip clone on its first tick");
                Shader originalShader = Shader.Find("UI/Dissolve mask") ?? throw new Exception("Native dissolve reference missing");
                var material = new Material(originalShader); material.SetTexture("_Mask", Texture2D.whiteTexture);
                material.SetFloat("_EdgeWeight", 1f); material.SetFloat("_Cutoff", 0f); scene.Image.material = material;
                try
                {
                    var vertices = ModifiedMesh(scene.Image, graphic);
                    Check(vertices.Max(v => v.position.z) - vertices.Min(v => v.position.z) > 2f,
                        "the actually bound native mip illustration has measured nonflat relief in original window units");
                    int pixels;
                    using (var left = scene.Capture(new Vector3(-.35f, .02f, 0), "native-mip-left"))
                    using (var right = scene.Capture(new Vector3(.35f, .02f, 0), "native-mip-right"))
                        pixels = Difference(left.Image, right.Image, new RectInt(60, 35, 200, 110));
                    File.WriteAllText(Path.Combine(output, "native-mip-measurements.json"), "{\"parallax_pixels\":" + pixels
                        + ",\"minimum_depth\":" + vertices.Min(v => v.position.z).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + ",\"maximum_depth\":" + vertices.Max(v => v.position.z).ToString(System.Globalization.CultureInfo.InvariantCulture) + "}\n");
                    Check(pixels > 40, "the actual driver-bound mip illustration renders measured eye-dependent parallax pixels");
                    using (var revealed = scene.Capture(Vector3.zero, "native-mip-revealed"))
                    {
                        material.SetFloat("_Cutoff", .8f); // No SetMaterialDirty: native tween changes this original instance.
                        using (var partial = scene.Capture(Vector3.zero, "native-mip-live-dissolve"))
                            Check(Difference(revealed.Image, partial.Image, new RectInt(60, 35, 200, 110)) > 100,
                                "native dissolve clock remains live on the actual mip-bound relief material");
                    }
                    Check(scene.Image.material == material && material.GetFloat("_Cutoff") == .8f,
                        "relief capture leaves native material authority and intermediate animation value unchanged");
                }
                finally { scene.Image.material = null; Object.DestroyImmediate(material); }
                DefaultMipKeys(scene, tools, native, mip);
            }
            finally
            {
                CanvasConversion.ActivePanels.Clear(); MenuDepthDriver.Shutdown();
                Object.DestroyImmediate(storyObject); Object.DestroyImmediate(animatorObject); Object.DestroyImmediate(toolsObject);
            }
        }
        Object.DestroyImmediate(spoof);
        AtlasMipMapping();
        return _checks;
    }

    private static void DefaultMipKeys(Scene scene, UIInfoTools tools, Sprite original, Sprite mip)
    {
        var ownerObject = new GameObject("unplayed native event owner"); ownerObject.SetActive(false);
        UIEventPanel owner = ownerObject.AddComponent<UIEventPanel>();
        try
        {
            Type driver = typeof(MenuDepthDriver), specType = driver.GetNestedType("Spec", BindingFlags.NonPublic)!;
            var specs = (Array)driver.GetField("Specs", Static)!.GetValue(null)!;
            object spec = specs.Cast<object>().First(s => (string)specType.GetField("TypeName", Instance)!.GetValue(s)! == "UIEventPanel");
            Type bindingType = driver.GetNestedType("Binding", BindingFlags.NonPublic)!;
            object binding = Activator.CreateInstance(bindingType, Instance, null,
                new object[] { owner, spec, typeof(UIEventPanel).GetField("eventImage", Instance)! }, null)!;
            scene.Image.sprite = mip;
            object[] args = { binding, scene.Image, string.Empty };
            string key = (string)driver.GetMethod("Key", Static)!.Invoke(null, args)!;
            Check(key == "defaultEventImage" && (string)args[2] == key,
                "the actual event driver recognizes a baked default picture while the other default slot is null");
            tools.defaultEventImage = null!; tools.defaultCityEventImage = original; args[2] = string.Empty;
            key = (string)driver.GetMethod("Key", Static)!.Invoke(null, args)!;
            Check(key == "defaultCityEventImage", "the actual event driver recognizes a baked city default with a missing road default");
            Sprite dead = Sprite.Create(original.texture, original.rect, Vector2.one * .5f); tools.defaultEventImage = dead;
            Object.DestroyImmediate(dead); args[2] = string.Empty;
            Check((string)driver.GetMethod("Key", Static)!.Invoke(null, args)! == "defaultCityEventImage",
                "a destroyed optional native default cannot break the surviving default image binding");
        }
        finally { Object.DestroyImmediate(ownerObject); }
    }

    private static void AtlasMipMapping()
    {
        var texture = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "native-atlas-region-witness" };
        var pixels = new Color32[128 * 128];
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            pixels[y * 128 + x] = x % 7 == 0 || y % 9 == 0 ? new Color32(15, 15, 15, 255) : new Color32((byte)(x + 60), (byte)(y + 70), 200, 255);
        texture.SetPixels32(pixels); texture.Apply();
        Sprite original = Sprite.Create(texture, new Rect(16, 24, 32, 40), Vector2.one * .5f); original.name = "actual-native-atlas-region";
        Sprite replacement = CardFaceMipBake.ReplacementFor(original) ?? throw new Exception("Actual native region mip bake absent");
        var art = Art(new string('d', 32), original);
        Check(replacement.texture.width == 32 && replacement.texture.height == 40 && replacement.rect.position == Vector2.zero,
            "the real production mip producer selects its separate logical-full region reconstruction path");
        Check(MenuDepthCatalogue.TryDepthUv(art, replacement, out Vector4 uv) && uv == new Vector4(.25f, .3125f, .125f, .1875f),
            "actual atlas region mip UVs map colour pixels to the original full-texture heightfield");
        MenuDepthHeightfield field = MenuDepthHeightfield.Read(DepthBytes(4, 4, 128, 128, false));
        using (var nativeScene = new Scene(original))
        using (var mipScene = new Scene(replacement))
        {
            var nativeDepth = nativeScene.Image.gameObject.AddComponent<MenuDepthGraphic>(); nativeDepth.Configure(nativeScene.Image, field, .03f);
            var mipDepth = mipScene.Image.gameObject.AddComponent<MenuDepthGraphic>(); mipDepth.ConfigureMapped(mipScene.Image, field, .03f, uv);
            nativeScene.Panel.gameObject.SetActive(false); mipScene.Panel.gameObject.SetActive(true);
            TextureOwner mipPixels = mipScene.Capture(new Vector3(-.3f, .03f, 0), "atlas-mip-relief");
            mipScene.Panel.gameObject.SetActive(false); nativeScene.Panel.gameObject.SetActive(true);
            using (mipPixels)
            using (var nativePixels = nativeScene.Capture(new Vector3(-.3f, .03f, 0), "atlas-original-relief"))
            {
                int mismatch = Difference(mipPixels.Image, nativePixels.Image, new RectInt(80, 50, 160, 80));
                File.WriteAllText(Path.Combine(_output, "atlas-uv-measurements.json"), "{\"original_vs_mip_mismatched_pixels\":"
                    + mismatch + ",\"roi_pixels\":12800,\"maximum_accepted_mismatches\":19}\n");
                Check(mismatch < 20,
                    "actual native and region-mip relief retain measured colour/depth alignment under an oblique eye");
            }
        }
        Object.DestroyImmediate(original); Object.DestroyImmediate(texture);
    }
}
