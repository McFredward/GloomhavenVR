using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

public static partial class MirrorProgram
{
    private static Texture2D PublicTexture(string name, Color first, Color second)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name };
        texture.SetPixels(new[] { first, second, second, first }); texture.Apply(); Assets.Add(texture);
        return texture;
    }
    private static Sprite PublicSprite(Texture2D texture, string name)
    {
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f));
        sprite.name = name; Assets.Add(sprite); return sprite;
    }
    private static Texture2D PublicNativeCoin()
    {
        // This is the audited native descriptor, including its DXT5 format. The compressed
        // white blocks are deterministic; both wrappers refer to the same original pixels.
        var texture = new Texture2D(128, 128, TextureFormat.DXT5, false) { name = "CoinIcon2_White" };
        var blocks = new byte[128 * 128];
        for (int i = 0; i < blocks.Length; i += 16)
        { blocks[i] = blocks[i + 1] = blocks[i + 8] = blocks[i + 9] = blocks[i + 10] = blocks[i + 11] = 255; }
        texture.LoadRawTextureData(blocks); texture.Apply(false); Assets.Add(texture); return texture;
    }
    private static Texture2D PublicNativeRing()
    {
        var texture = new Texture2D(512, 512, TextureFormat.RGB24, true) { name = "T_disc_ring" };
        var pixels = new Color[512 * 512]; for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.magenta;
        texture.SetPixels(pixels); texture.Apply(); Assets.Add(texture); return texture;
    }
    private static Transform LayeredPublicItem(Transform frame, string name, Sprite art, Sprite coin, Texture2D noise)
    {
        Transform hostedCanvas = Rect(name + " FaceCanvas", frame, Vector2.zero, new Vector2(260, 260));
        var canvas = hostedCanvas.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = _camera;
        Transform root = Rect(name, hostedCanvas, Vector2.zero, new Vector2(260, 260));
        root.gameObject.AddComponent<CanvasGroup>();
        root.gameObject.AddComponent<GameplayFixture>();
        Transform background = Rect("CardBackground", root, Vector2.zero, new Vector2(260, 260));
        background.gameObject.AddComponent<Image>().sprite = art;
        Transform price = Rect("PriceIcon", root, new Vector2(70f, -75f), new Vector2(55f, 55f));
        price.gameObject.AddComponent<Image>().sprite = coin;
        Transform effects = Rect("NativeCardEffects", root, new Vector2(-25f, 45f), new Vector2(70f, 60f));
        var image = effects.gameObject.AddComponent<Image>(); image.color = new Color(.5f, .3f, .9f, .55f);
        var material = new Material(Shader.Find("UI/Default")); material.SetTexture("_MainTex", noise);
        image.material = material; Assets.Add(material);
        Transform hidden = Rect("UnusedOriginalSlot", root, Vector2.zero, new Vector2(30, 30));
        hidden.gameObject.AddComponent<Image>().sprite = coin; hidden.gameObject.SetActive(false);
        return root;
    }
    private static IEnumerator OriginalPublicItemFronts()
    {
        // Real capture, codec, delta expansion, cloning, neutralization and playback operate on
        // a layered original item presentation. Only the game pool/bootstrap are fixture seams;
        // these images/materials are actual Unity objects, not mocked art-success predicates.
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 4;
        Transform ownerFrame = Go("public merchant original frame").transform;
        Transform viewerFrame = Go("public merchant observer frame").transform;
        viewerFrame.position = new Vector3(3f, 0f, 0f);
        var sharedCoin = PublicSprite(PublicNativeCoin(), "CoinIcon2_White");
        var duplicateCoin = PublicSprite(PublicNativeCoin(), "CoinIcon2_White");
        var noise = PublicNativeRing();
        var duplicateNoise = PublicNativeRing();
        var art = PublicSprite(PublicTexture("original item face artwork", Color.green, Color.cyan), "original item artwork");
        Transform first = LayeredPublicItem(ownerFrame, "Original merchant owned item", art, sharedCoin, noise);
        Transform second = LayeredPublicItem(ownerFrame, "Original second fan item", art, duplicateCoin, duplicateNoise);
        first.localPosition = new Vector3(-.6f, .3f, 0f); second.localPosition = new Vector3(.6f, .3f, 0f);
        first.localScale = second.localScale = Vector3.one * .01f;
        var earlier = new GloomhavenVR.Net.TownServices.TownServiceAssets(); var later = new GloomhavenVR.Net.TownServices.TownServiceAssets();
        TownServiceTemplateAssets.Register(earlier, "item.41", first);
        TownServiceTemplateAssets.Register(earlier, "item.42", second);
        TownServiceTemplateAssets.Register(later, "item.42", second);
        TownServiceTemplateAssets.Register(later, "item.41", first);
        Check(earlier.Key(art.texture) == later.Key(art.texture),
            "different borrow orders retain model-aware artwork identity");
        Check(earlier.Key(sharedCoin.texture) == earlier.Key(duplicateCoin.texture)
            && later.Key(duplicateCoin.texture) == later.Key(sharedCoin.texture)
            && earlier.Key(sharedCoin.texture) == later.Key(duplicateCoin.texture),
            "different borrow orders retain one original texture identity");
        Check(earlier.Key(noise) == later.Key(duplicateNoise),
            "different borrow orders retain the original native effect identity");
        // Exact native dependencies must be installed BEFORE broad descriptor discovery can
        // confuse equal names/dimensions, or a complete original front is refused atomically.
        TownServiceTemplateAssets.Register(TownServiceMirror.Assets, "item.41", first);
        TownServiceTemplateAssets.Register(TownServiceMirror.Assets, "item.42", second);
        TownServiceMirror.RegisterTemplate(1, 1, first, address: "item.41|");
        TownServiceMirror.RegisterTemplate(1, 1, second, address: "item.42|");
        TownServiceMirror.BeginSession(1, 1204, ownerFrame, ownerFrame);
        TownServiceMirror.RegisterModule(11, 1, first, address: "item.41|");
        TownServiceMirror.RegisterModule(12, 1, second, address: "item.42|");
        List<byte[]> packets = Capture();
        Check(packets.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out var frame) && frame!.Module == 11)
            && packets.Exists(bytes => TownServiceCodec.TryRead(bytes, bytes.Length, out var frame) && frame!.Module == 12),
            "original public item fronts capture even when native texture descriptors collide");
        TownServiceMirror.EndSession(); NetPlayerActors.Peer = 3;
        Receive(4, packets); TownServiceMirror.InteractionOwner(1);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewerFrame);
        var held = Remote(4, 11); var fan = Remote(4, 12);
        Check(held != null && fan != null, "observer creates every public original held/fan front");
        Check(held!.Root.Find("CardBackground").GetComponent<Image>().sprite == art,
            "public held item retains its complete original artwork sprite");
        Check(fan!.Root.Find("PriceIcon").GetComponent<Image>().sprite.texture == sharedCoin.texture,
            "public fan binds the audited original price/icon wrapper");
        Check(held.Root.Find("NativeCardEffects").GetComponent<Image>().material.GetTexture("_MainTex") == noise
            && fan.Root.Find("NativeCardEffects").GetComponent<Image>().material.GetTexture("_MainTex") == noise,
            "original public card effects bind the audited native ring across wrappers");
        Check(!held.Root.Find("UnusedOriginalSlot").gameObject.activeSelf
            && !fan.Root.Find("UnusedOriginalSlot").gameObject.activeSelf,
            "original inactive item slots stay inactive on observers");
        Check(held.Root.GetComponentsInChildren<GameplayFixture>(true).Length == 0
            && fan.Root.GetComponentsInChildren<GameplayFixture>(true).Length == 0,
            "public item fronts remain inert presentation without game controllers");
        ComparePixels(first, held.Root, "public-held-original-item");
        ComparePixels(second, fan.Root, "public-fan-original-item");

        // A secondary visitor retains the same full original item front while another peer
        // authors the shared palm. It cannot substitute a generic slab or a concealed face.
        InteractionManifest(2, 1, 1202, 1, modules: Array.Empty<ushort>());
        TownServiceMirror.RemovePeer(4); TownServiceMirror.InteractionOwner(1);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        Check(TownServiceMirror.InteractionOwner(1) == 2, "other visitor acquires the released merchant lease");
        Receive(4, packets); TownServiceMirror.TickRemote(_ => viewerFrame);
        Check(TownServiceMirror.InteractionOwner(1) == 2 && Remote(4, 11) != null && Remote(4, 12) != null,
            "non-elected visitor keeps complete original public item fronts");
        TownServiceMirror.RemovePeer(2); TownServiceMirror.InteractionOwner(1);
        for (float until = Time.unscaledTime + .13f; Time.unscaledTime < until;) yield return null;
        TownServiceMirror.TickRemote(_ => viewerFrame);
        Check(Remote(4, 11) != null && Remote(4, 11)!.Root.Find("CardBackground").GetComponent<Image>().sprite == art,
            "merchant authorship handover preserves the same original held artwork");
        TownServiceMirror.Shutdown(); NetPlayerActors.Peer = 1;
    }
}
