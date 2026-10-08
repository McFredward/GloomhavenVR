using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MirrorProgram
{
    private static TMP_Text CaptionText646(Transform root, string path) => root.Find(path).GetComponent<TMP_Text>();
    private static void Economic646(Transform root, int price, int quantity, int total, int discount, bool affordable)
    {
        CaptionText646(root, "Price/TextMeshPro Text").text = price.ToString();
        CaptionText646(root, "Price/Price warning/TextMeshPro Text (1)").text = price.ToString();
        CaptionText646(root, "Amount").text = quantity + "/" + total;
        var color = affordable ? new Color(.95f, .8f, .2f, 1f) : new Color(.8f, .1f, .1f, 1f);
        CaptionText646(root, "Price/TextMeshPro Text").color = color;
        root.Find("Price/Icon").GetComponent<Image>().color = color;
        root.Find("Price/Reputation").localScale = new Vector3(1f, discount > 0 ? -1f : 1f, 1f);
        root.Find("Price/Reputation").gameObject.SetActive(discount != 0);
        // Native Initialize repeatedly restores the original font and layout via the source mirror.
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.fontSize = text.name.Contains("Name") ? 22f : 20f;
        root.Find("Content").gameObject.SetActive(true);
        root.Find("Price/Price warning").gameObject.SetActive(!affordable);
    }
    private static void CaptionGeometry646(Transform original, Transform shown)
    {
        Check(!shown.Find("Content").gameObject.activeSelf, "duplicate native name and item icon are absent from the caption");
        Check(original.Find("Content").gameObject.activeSelf, "original backend name and icon remain intact");
        var price = CaptionText646(shown, "Price/TextMeshPro Text"); var amount = CaptionText646(shown, "Amount");
        Check(price.fontSize >= 36f && amount.fontSize >= 36f, "legible native caption uses the enlarged price and quantity");
        Check(price.text == CaptionText646(original, "Price/TextMeshPro Text").text && amount.text == CaptionText646(original, "Amount").text,
            "native economic text is never reconstructed or changed by caption layout");
        Check(price.color.Equals(CaptionText646(original, "Price/TextMeshPro Text").color), "native affordability colour remains exact");
        Check(shown.Find("Price/Price warning").gameObject.activeSelf == original.Find("Price/Price warning").gameObject.activeSelf,
            "native price warning remains exact without reopening the duplicate title");
        Check(shown.Find("Price/Reputation").localScale.Equals(original.Find("Price/Reputation").localScale)
            && shown.Find("Price/Reputation").gameObject.activeSelf == original.Find("Price/Reputation").gameObject.activeSelf,
            "native reputation triangle keeps discount direction and visibility");
        Check(shown.Find("Price/Icon").GetComponent<Image>().sprite == original.Find("Price/Icon").GetComponent<Image>().sprite,
            "caption retains the actual original gold sprite");
        var p = new Vector3[4]; var q = new Vector3[4];
        ((RectTransform)shown.Find("Price/Icon")).GetWorldCorners(p); ((RectTransform)shown.Find("Amount")).GetWorldCorners(q);
        float middle = (p.Min(v => v.x) + q.Max(v => v.x)) * .5f;
        Check(Mathf.Abs(middle - shown.position.x) < .0025f, "price gold triangle and quantity form a centered caption");
        price.ForceMeshUpdate(); amount.ForceMeshUpdate();
        Check(price.textInfo.characterCount == price.text.Length && amount.textInfo.characterCount == amount.text.Length,
            "every price and quantity glyph is present at the legibility floor");
        Check(price.preferredWidth <= price.rectTransform.rect.width && amount.preferredWidth <= amount.rectTransform.rect.width,
            "enlarged economic glyphs fit without clipping or forced shrinking");
    }
    private static Color32[] CaptionRender646(Transform surface, string name)
    {
        foreach (GameObject fixture in Objects) if (fixture != null) Layer(fixture.transform, 30);
        Layer(surface.parent, 27); foreach (Canvas canvas in surface.parent.GetComponentsInChildren<Canvas>(true)) canvas.worldCamera = _camera;
        _camera.cullingMask = 1 << 27; _camera.orthographicSize = .026f;
        _camera.transform.SetPositionAndRotation(surface.position - Vector3.forward * 10f, Quaternion.identity);
        Canvas.ForceUpdateCanvases();
        var target = new RenderTexture(1280, 360, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(1280, 360, TextureFormat.RGBA32, false);
        try
        {
            _camera.targetTexture = target; _camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 360), 0, 0); texture.Apply();
            File.WriteAllBytes(Path.Combine(_output, name + ".png"), texture.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(_output, name + ".jpg"), texture.EncodeToJPG(90));
            return texture.GetPixels32();
        }
        finally { RenderTexture.active = null; _camera.targetTexture = null; Object.DestroyImmediate(target); Object.DestroyImmediate(texture); }
    }
    private static IEnumerator Caption646()
    {
        TownServiceMirror.Shutdown();
        Transform backend = Rect("Native source bank", null!, Vector2.zero, new Vector2(547.75f, 70f));
        Transform native = NativeRow632(backend, "Laufstiefel");
        Transform mount = Go("Merchant local caption mount").transform;
        var canvas = mount.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        mount.localScale = Vector3.one * (.5f / 2400f);
        using var mirror = new RemoteWidgetMirror(native, mount);
        var actual = new Caption646Driver(mirror, native);
        Economic646(native, 20, 2, 2, -1, true);
        mirror.TickLive();
        CaptionRender646(mirror.Root, "caption646-before");
        actual.Present(); CaptionGeometry646(native, mirror.Root);
        Transform observerMount = Go("Merchant remote caption mount").transform;
        observerMount.position = Vector3.right * .3f; observerMount.localScale = mount.localScale;
        var observerCanvas = observerMount.gameObject.AddComponent<Canvas>(); observerCanvas.renderMode = RenderMode.WorldSpace;
        var replica = Object.Instantiate(native.gameObject, observerMount, false);
        replica.SetActive(false); TownServiceNeutralize.Apply(replica); replica.SetActive(true);
        using var observer = new TownServiceBinding(replica.transform);
        var picture = new List<string> { "price,amount,total,discount,affordable,local_font,remote_font" };
        ulong sequence = 1;
        foreach (int price in new[] { 5, 20, 100, 999 })
        foreach (int stock in new[] { 0, 1, 2, 12 })
        foreach (int discount in new[] { -2, 0, 2 })
        foreach (bool affordable in new[] { false, true })
        {
            Economic646(native, price, stock, 12, discount, affordable);
            mirror.TickLive(); actual.Present(); CaptionGeometry646(native, mirror.Root);
            using var binding = new TownServiceBinding(mirror.Root);
            var frame = new TownServiceFrame { Service = 1, Session = 646, Sequence = sequence++, Module = 11, Template = 1,
                TemplateAddress = "merchant.row|", Structure = binding.Structure, Visible = true,
                Nodes = TownServiceDelta.Retain(new TownServiceFrame { Nodes = binding.Read(TownServiceMirror.Assets) }).Nodes,
                Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } };
            byte[] bytes = TownServiceCodec.Write(frame);
            Check(TownServiceCodec.TryRead(bytes, bytes.Length, out var received), "actual merchant caption codec accepts the original native presentation");
            observer.Apply(received!, TownServiceMirror.Assets); CaptionGeometry646(native, observer.Root);
            var localRects = mirror.Root.GetComponentsInChildren<RectTransform>(true);
            var remoteRects = observer.Root.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < localRects.Length; i++)
            {
                Check(localRects[i].anchorMin.Equals(remoteRects[i].anchorMin) && localRects[i].anchorMax.Equals(remoteRects[i].anchorMax)
                    && localRects[i].pivot.Equals(remoteRects[i].pivot) && localRects[i].sizeDelta.Equals(remoteRects[i].sizeDelta)
                    && localRects[i].anchoredPosition3D.Equals(remoteRects[i].anchoredPosition3D),
                    "remote caption has every original local intermediate rectangle exactly");
            }
            picture.Add(price + "," + stock + ",12," + discount + "," + affordable + ",36,36");
        }
        Economic646(native, 30, 2, 4, -2, true); mirror.TickLive(); actual.Present();
        using (var binding = new TownServiceBinding(mirror.Root)) observer.Apply(new TownServiceFrame { Service = 1, Session = 646,
            Module = 11, Template = 1, Structure = binding.Structure, Nodes = binding.Read(TownServiceMirror.Assets), Visible = true,
            Pose = new[] { 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f } }, TownServiceMirror.Assets);
        var localPixels = CaptionRender646(mirror.Root, "caption646-local");
        var remotePixels = CaptionRender646(observer.Root, "caption646-remote");
        int different = 0;
        for (int i = 0; i < localPixels.Length; i++)
            if (Math.Abs(localPixels[i].r - remotePixels[i].r) + Math.Abs(localPixels[i].g - remotePixels[i].g)
                + Math.Abs(localPixels[i].b - remotePixels[i].b) > 30) different++;
        Check(localPixels.Count(p => Math.Max(p.r, Math.Max(p.g, p.b)) > 40) > 1000
            && remotePixels.Count(p => Math.Max(p.r, Math.Max(p.g, p.b)) > 40) > 1000,
            "actual rendered native caption contains visible economic glyphs and original sprites");
        int white = localPixels.Count(p => p.r > 170 && p.g > 170 && p.b > 170);
        int gold = localPixels.Count(p => p.r > 140 && p.g > 90 && p.b < 120);
        Check(white > 150 && gold > 150, "rendered caption has both white quantity glyphs and gold price glyphs");
        Check(different < localPixels.Length * .005f, "rendered native caption remains identical across the shared codec");
        File.WriteAllText(Path.Combine(_output, "caption646-pixels.txt"), "different=" + different + "/" + localPixels.Length + " pixels; white="+white+" gold="+gold+"; em=7.5mm at cabinet scale\n");
        for (int rebuild = 0; rebuild < 4; rebuild++)
        {
            mirror.Rebuild(); mirror.TickLive(); actual.Present(); CaptionGeometry646(native, mirror.Root);
        }
        File.WriteAllLines(Path.Combine(_output, "caption646-economics.csv"), picture);
        yield return null;
    }
}
