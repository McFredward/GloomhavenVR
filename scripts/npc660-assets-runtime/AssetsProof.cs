using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using Object = UnityEngine.Object;

public static class AssetsProof
{
    private static int assertions;
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new Exception(message); }

    public static IEnumerator Run(string banks, string output)
    {
        assertions = 0;
        var loaded = new List<AssetBundle>();
        var natives = new Dictionary<string, Texture2D[]>();
        foreach (string name in new[] { "Default-Particle", "T_sphere_norm" })
        {
            var textures = new Texture2D[2];
            for (int i = 0; i < 2; i++)
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(Path.Combine(banks, name + (i == 0 ? "-a.bundle" : "-b.bundle")));
                Check(bundle != null, "exact original wrapper bank loads: " + name);
                loaded.Add(bundle);
                textures[i] = bundle.LoadAsset<Texture2D>("native/texture");
                Check(textures[i] != null && textures[i].name == name, "unchanged original texture loads: " + name);
            }
            Check(!ReferenceEquals(textures[0], textures[1]) && textures[0].GetInstanceID() != textures[1].GetInstanceID(),
                "separate native wrappers are actually different objects: " + name);
            Check(!textures[0].isReadable && !textures[1].isReadable, "real GPU-only native texture is not a CPU surrogate: " + name);
            natives.Add(name, textures);
        }
        yield return null;
        var owner = new TownServiceAssets();
        var observer = new TownServiceAssets();
        owner.Scan(); observer.Scan();
        Shader shader = Shader.Find("Unlit/Texture");
        Check(shader != null, "explicit unlit material-validation boundary is available");
        var retained = new List<GameObject>();
        var materials = new List<Material>();
        foreach (var pair in natives)
        {
            Texture2D a = pair.Value[0], b = pair.Value[1];
            string descriptor = "texture|" + a.name + "|" + a.width + "|" + a.height + "|" + (int)a.format + "|" + a.mipmapCount;
            Check(owner.Resolve<Texture>(descriptor) != null && observer.Resolve<Texture>(descriptor) != null,
                "loaded duplicates never permanently poison the actual native registry: " + pair.Key);
            Check(descriptor == owner.Key(a), "exact original wrapper key matches audited descriptor: " + pair.Key);
            Check(descriptor == owner.Key(b), "canonical exact original wrappers retain one stable identity: " + pair.Key);
            var roots = new[] { Surface(a, shader, retained, materials), Surface(b, shader, retained, materials) };
            // Capture and playback clients borrow the same immutable model faces
            // in opposite orders. The full property basis must use the same key.
            TownServiceTemplateAssets.Register(owner, "face.325", roots[0]);
            TownServiceTemplateAssets.Register(owner, "face.329", roots[0]);
            TownServiceTemplateAssets.Register(observer, "face.329", roots[1]);
            TownServiceTemplateAssets.Register(observer, "face.325", roots[1]);
            Check(owner.Key(a) == observer.Key(b) && owner.Key(a) == descriptor,
                "native model borrow order cannot change the exact omitted-material basis: " + pair.Key);
            Material source = roots[0].GetComponent<RawImage>().material;
            TownServiceValue value = TownServiceMaterial.Read(source, owner);
            TownServiceMaterial.Validate(value, observer);
            Material copy = TownServiceMaterial.Apply(value, observer, null)
                ?? throw new Exception("Native material playback did not produce a material: " + pair.Key);
            Check(copy != null && observer.Key(copy.mainTexture) == descriptor,
                "actual complete native material validates and binds exact texture after duplicate discovery: " + pair.Key);
            Texture2D left = ReadTexture(a), right = ReadTexture(copy.mainTexture);
            Check(left.GetPixels32().SequenceEqual(right.GetPixels32()),
                "GPU-only original texture pixels survive actual material asset resolution: " + pair.Key);
            Object.Destroy(left); Object.Destroy(right); TownServiceMaterial.Release(copy);
        }
        var wrong = new Texture2D(32, 64, TextureFormat.RGBA32, true) { name = "Default-Particle" };
        Check(!TownServiceAssets.SameKnownNativeIdentity(wrong), "same native name with different geometry is never admitted");
        Object.Destroy(wrong);
        var unknown = new[] { new Texture2D(4, 4) { name = "unverified-same-name" }, new Texture2D(4, 4) { name = "unverified-same-name" } };
        var registry = new TownServiceAssets(); registry.Key(unknown[0]);
        bool refused = false;
        try { registry.Key(unknown[1]); } catch (InvalidDataException) { refused = true; }
        Check(refused, "unverified same-name textures still require exact original provenance");
        foreach (Texture2D texture in unknown) Object.Destroy(texture);
        foreach (GameObject go in retained) Object.Destroy(go);
        foreach (Material material in materials) Object.Destroy(material);
        owner.Clear(); observer.Clear(); registry.Clear(); TownServiceMaterial.Reset();
        foreach (AssetBundle bundle in loaded) bundle.Unload(true);
        File.WriteAllText(output, assertions + " assertions; actual duplicate native objects, reversed model order, full material validation and GPU pixels.\n");
    }

    private static Transform Surface(Texture texture, Shader shader, List<GameObject> objects, List<Material> materials)
    {
        var go = new GameObject("Native effect dependency", typeof(RectTransform), typeof(RawImage)); objects.Add(go);
        var material = new Material(shader) { mainTexture = texture }; materials.Add(material);
        go.GetComponent<RawImage>().texture = texture; go.GetComponent<RawImage>().material = material;
        return go.transform;
    }

    private static Texture2D ReadTexture(Texture texture)
    {
        RenderTexture before = RenderTexture.active;
        RenderTexture target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(texture, target); RenderTexture.active = target;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
        RenderTexture.active = before; RenderTexture.ReleaseTemporary(target); return image;
    }
}
