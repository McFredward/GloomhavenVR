using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Pair = GloomhavenVR.Net.RemoteWidgetMirror.Pair;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics = "";
    private static void Check(bool value, string reason)
    { Checks++; if (!value) throw new InvalidOperationException(reason); }

    private sealed class Fixture : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly Transform Source;
        internal readonly Transform Destination;
        internal readonly Graphic? SourceGraphic;
        internal readonly Graphic? DestinationGraphic;
        internal readonly CanvasGroup? SourceGroup;
        internal readonly CanvasGroup? DestinationGroup;
        internal readonly Sprite[] Sprites = new Sprite[3];
        internal readonly Texture2D[] Textures = new Texture2D[3];
        internal readonly Material SourceMaterial;
        internal readonly Material DestinationMaterial;
        internal Pair Pair;
        internal Fixture(string kind)
        {
            Proof.Recording = false;
            Root = new GameObject("test-root", typeof(RectTransform), typeof(Canvas));
            Root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var source = new GameObject("source", kind == "plain" ? typeof(Transform) : typeof(RectTransform));
            var destination = new GameObject("destination", kind == "plain" ? typeof(Transform) : typeof(RectTransform));
            Source = source.transform; Destination = destination.transform;
            Source.SetParent(Root.transform, false); Destination.SetParent(Root.transform, false);
            if (kind != "plain")
            {
                SourceGroup = source.AddComponent<CanvasGroup>(); DestinationGroup = destination.AddComponent<CanvasGroup>();
                if (kind == "image") { SourceGraphic = source.AddComponent<Image>(); DestinationGraphic = destination.AddComponent<Image>(); }
                if (kind == "raw") { SourceGraphic = source.AddComponent<RawImage>(); DestinationGraphic = destination.AddComponent<RawImage>(); }
                if (kind == "virtual-image") { SourceGraphic = source.AddComponent<ChangingImage>(); DestinationGraphic = destination.AddComponent<Image>(); }
                if (kind == "text")
                {
                    SourceGraphic = source.AddComponent<Text>(); DestinationGraphic = destination.AddComponent<Text>();
                    ((Text)SourceGraphic).font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    ((Text)DestinationGraphic).font = ((Text)SourceGraphic).font;
                }
                if (kind == "virtual")
                {
                    SourceGraphic = source.AddComponent<ChangingText>(); DestinationGraphic = destination.AddComponent<Text>();
                    ((Text)SourceGraphic).font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                    ((Text)DestinationGraphic).font = ((Text)SourceGraphic).font;
                }
                destination.AddComponent<MirrorRectProbe>();
                DestinationGraphic!.RegisterDirtyVerticesCallback(() => Proof.Callback("vertices"));
                DestinationGraphic.RegisterDirtyMaterialCallback(() => Proof.Callback("material"));
                DestinationGraphic.RegisterDirtyLayoutCallback(() => Proof.Callback("layout"));
            }
            for (int i = 0; i < 3; i++)
            {
                Textures[i] = new Texture2D(8, 8) { name = "texture-" + i };
                Sprites[i] = Sprite.Create(Textures[i], new Rect(0, 0, 8, 8), Vector2.one / 2); Sprites[i].name = "sprite-" + i;
            }
            SourceMaterial = new Material(Shader.Find("UI/Default")) { name = "source-material" };
            DestinationMaterial = new Material(SourceMaterial) { name = "destination-material" };
            if (SourceGraphic != null) { SourceGraphic.material = SourceMaterial; DestinationGraphic!.material = DestinationMaterial; }
            Pair = new Pair(Source, Destination, 17);
        }
        internal void Animate(int frame)
        {
            Proof.Recording = false;
            float f = (frame + 1) / 50f;
            Source.localRotation = Quaternion.Euler(f * 8, f * 13, f * 27);
            Source.localScale = Vector3.one + new Vector3(f, f * .7f, f * .5f);
            if (Source is RectTransform rect)
            {
                rect.anchorMin = new Vector2(f, f * .7f); rect.anchorMax = new Vector2(.8f + f, .7f + f);
                rect.pivot = new Vector2(.4f + f, .3f + f);
                rect.sizeDelta = new Vector2(100 + frame * 2, 77 + frame);
                rect.anchoredPosition3D = new Vector3(frame * 3 + 2, frame + 1, f);
            }
            else Source.localPosition = new Vector3(f * 7, f * 11, f * 3);
            if (SourceGraphic != null)
            {
                SourceGraphic.color = new Color(f, .4f + f, .6f, .2f + f);
                SourceGraphic.canvasRenderer.SetColor(new Color(.7f, f, .6f + f, .3f + f));
                SourceGroup!.alpha = .25f + f;
                if (SourceGraphic is Image image)
                { image.sprite = Sprites[frame % 3]; image.overrideSprite = Sprites[(frame + 1) % 3]; image.type = frame % 2 == 0 ? Image.Type.Filled : Image.Type.Simple; image.fillAmount = .1f + f; }
                if (SourceGraphic is RawImage raw)
                { raw.texture = Textures[frame % 3]; raw.uvRect = new Rect(f, f * .2f, .8f, .6f + f); }
                if (SourceGraphic is Text text) text.text = "animation-" + frame;
            }
        }
        internal void CallbackMutations()
        {
            Proof.OnCallback = point =>
            {
                if (point == "rect:_dstRect.anchorMin" && Source is RectTransform rect)
                    rect.anchorMax = new Vector2(.91f, .86f);
                if (SourceGraphic is Image image)
                {
                    if (point == "vertices:_dstGraphic.color") { image.sprite = Sprites[2]; image.fillAmount = .73f; SourceGroup!.alpha = .37f; }
                    if (point == "vertices:_dstImage.sprite") image.overrideSprite = Sprites[1];
                    if (point == "vertices:_dstImage.type") image.fillAmount = .61f;
                }
                if (SourceGraphic is RawImage raw && point == "vertices:_dstRaw.texture") raw.uvRect = new Rect(.13f, .27f, .7f, .9f);
            };
        }
        internal bool Apply(bool original, bool root = false, bool rects = true)
        { Proof.Recording = true; bool result = original ? Pair.ApplyOriginal(root, rects) : Pair.Apply(root, rects); Proof.Recording = false; return result; }
        internal string State(bool source = false)
        {
            Transform t = source ? Source : Destination;
            Graphic? g = source ? SourceGraphic : DestinationGraphic;
            CanvasGroup? group = source ? SourceGroup : DestinationGroup;
            string s = t.gameObject.activeSelf + ":" + Vector(t.localPosition) + ":" + Vector(t.localScale) + ":" + QuaternionText(t.localRotation);
            if (t is RectTransform rect) s += ":" + Vector(rect.anchorMin) + ":" + Vector(rect.anchorMax) + ":" + Vector(rect.pivot) + ":" + Vector(rect.sizeDelta) + ":" + Vector(rect.anchoredPosition3D);
            if (g != null) s += ":" + g.enabled + ":" + ColorText(g.color) + ":" + ColorText(g.canvasRenderer.GetColor()) + ":" + g.material.name + ":" + group!.alpha.ToString("R");
            if (g is Image image) s += ":" + Name(image.sprite) + ":" + Name(image.overrideSprite) + ":" + image.type + ":" + image.fillAmount.ToString("R");
            if (g is RawImage raw) s += ":" + Name(raw.texture) + ":" + raw.uvRect.ToString("F8");
            if (g is Text text) s += ":" + text.text;
            return s;
        }
        public void Dispose()
        {
            Proof.Recording = false; Object.DestroyImmediate(Root); Object.DestroyImmediate(SourceMaterial); Object.DestroyImmediate(DestinationMaterial);
            foreach (Sprite sprite in Sprites) Object.DestroyImmediate(sprite);
            foreach (Texture2D texture in Textures) Object.DestroyImmediate(texture);
        }
    }
    private static string Name(Object? value) => value == null ? "null" : value.name;
    private static string Vector(Vector2 v) => v.x.ToString("R") + "," + v.y.ToString("R");
    private static string Vector(Vector3 v) => v.x.ToString("R") + "," + v.y.ToString("R") + "," + v.z.ToString("R");
    private static string QuaternionText(Quaternion v) => Vector(new Vector3(v.x,v.y,v.z)) + "," + v.w.ToString("R");
    private static string ColorText(Color v) => Vector(new Vector3(v.r,v.g,v.b)) + "," + v.a.ToString("R");
    private static string Effects() => string.Join("|", Proof.Trace.Where(t => !t.StartsWith("read:", StringComparison.Ordinal)));
    private static string Trace() => string.Join("|", Proof.Trace);
    private static int Reads(string key) => Proof.Reads.TryGetValue(key, out int count) ? count : 0;

    public static IEnumerator Run()
    {
        Checks = 0; Proof.Reset();
        foreach ((Type type, string name) in new[] { (typeof(RectTransform), "anchorMin"), (typeof(RectTransform), "anchorMax"), (typeof(RectTransform), "pivot"), (typeof(RectTransform), "sizeDelta"), (typeof(RectTransform), "anchoredPosition3D"), (typeof(Transform), "localPosition"), (typeof(Transform), "localRotation"), (typeof(Transform), "localScale"), (typeof(Behaviour), "enabled"), (typeof(Image), "sprite"), (typeof(Image), "overrideSprite"), (typeof(Image), "type"), (typeof(Image), "fillAmount"), (typeof(RawImage), "texture"), (typeof(RawImage), "uvRect"), (typeof(CanvasGroup), "alpha") })
            Check(!type.GetProperty(name)!.GetMethod!.IsVirtual, "Reused source getter must be nonvirtual: " + name);
        Check(typeof(Graphic).GetProperty("color")!.GetMethod!.IsVirtual, "Virtual Graphic getter remains unrestricted");
        Check(typeof(Text).GetProperty("text")!.GetMethod!.IsVirtual, "Virtual Text getter remains unrestricted");
        foreach (string kind in new[] { "image", "raw", "text", "plain" })
        {
            using var f = new Fixture(kind); f.Animate(0); f.CallbackMutations(); Proof.Reset(); f.CallbackMutations(); f.Apply(true);
            if (kind == "image")
                Check(((RectTransform)f.Source).anchorMax == new Vector2(.91f, .86f), "native dimension callback changes the later source anchor");
            string expected = f.State(), native = f.State(true), trace = Trace(), effects = Effects();
            Check(kind == "plain" || effects.Contains("callback:"), "Real native UI callbacks execute");
            using var off = new Fixture(kind); off.Animate(0); Proof.Reset(); off.CallbackMutations(); PerfConfig.SharedUiWindowReadsOn = false; off.Apply(false);
            Check(Trace() == trace && off.State() == expected && off.State(true) == native, "Off read/write/callback trace equals original Apply");
            using var on = new Fixture(kind); on.Animate(0); Proof.Reset(); on.CallbackMutations(); PerfConfig.SharedUiWindowReadsOn = true; on.Apply(false);
            Check(on.State() == expected && on.State(true) == native && Effects() == effects, "native callback writes retain live later source fields");
            if (kind != "plain") Check(Reads("_srcRect.anchorMin") == 1, "changed native getter read once");
        }
        using (var virtualSource = new Fixture("virtual"))
        {
            PerfConfig.SharedUiWindowReadsOn = true; Proof.Reset(); virtualSource.Apply(false);
            Check(((Text)virtualSource.DestinationGraphic!).text == "virtual-2", "virtual getter keeps original second observation");
            Check(Reads("_srcText.text") == 2, "virtual getter keeps original second observation");
        }
        using (var virtualSource = new Fixture("virtual-image"))
        {
            PerfConfig.SharedUiWindowReadsOn = true; Proof.Reset(); virtualSource.Apply(false);
            Check(virtualSource.DestinationGraphic!.color.r == .2f && Reads("_srcGraphic.color") == 2,
                "virtual Graphic getter keeps original second observation");
        }
        foreach (string kind in new[] { "image", "raw", "text", "plain" })
        {
            using var original = new Fixture(kind); using var current = new Fixture(kind);
            for (int frame = 0; frame < 16; frame++)
            {
                original.Animate(frame); current.Animate(frame);
                Proof.Reset(); original.Apply(true); string state = original.State(); string effects = Effects();
                PerfConfig.SharedUiWindowReadsOn = frame != 7 && frame != 11; Proof.Reset(); current.Apply(false);
                Check(current.State() == state && Effects() == effects, "same-frame animation state matches original Apply");
                Check(current.State(true) == original.State(true), "sampling never writes original source");
            }
            PerfConfig.SharedUiWindowReadsOn = true;
            for (int pass = 0; pass < 40; pass++)
            {
                Proof.Reset(); current.Apply(false);
                Check(!Proof.Trace.Any(x => x.StartsWith("write:", StringComparison.Ordinal)), "unchanged destination writes nothing");
                Check(PerfConfig.ModeReads == 1, "read mode is resolved once per visible Apply");
            }
        }
        using (var original = new Fixture("image"))
        using (var current = new Fixture("image"))
        {
            for (int frame = 0; frame < 4; frame++)
            {
                original.SourceGraphic!.enabled = current.SourceGraphic!.enabled = frame % 2 != 0;
                Proof.Reset(); original.Apply(true); string expected = original.State(), effects = Effects();
                Proof.Reset(); PerfConfig.SharedUiWindowReadsOn = true; current.Apply(false);
                Check(current.State() == expected && Effects() == effects, "native graphic enablement callbacks retain original writes");
                Check(Reads("_srcGraphic.enabled") == 1, "changed enabled getter read once");
            }
        }
        using (var f = new Fixture("image"))
        {
            f.Animate(0); f.Destination.gameObject.SetActive(false);
            PerfConfig.SharedUiWindowReadsOn = true; Proof.Reset();
            Proof.OnCallback = point => { if (point == "enable:Dst.active") ((Image)f.SourceGraphic!).fillAmount = .84f; };
            f.Apply(false); Check(((Image)f.DestinationGraphic!).fillAmount == .84f, "native activation callback source change appears immediately");
            f.Source.gameObject.SetActive(false); Proof.Reset();
            Check(!f.Apply(false) && !f.Destination.gameObject.activeSelf, "inactive source branch retains original skip gate");
            Proof.Reset(); Check(f.Apply(false, root: true) && f.Destination.gameObject.activeSelf, "root keeps owner visibility despite inactive native source");
            f.Pair.MarkExternal(); string before = f.State(); Proof.Reset();
            Check(!f.Apply(false) && f.State() == before && Proof.SourceReads == 0, "external branch retains owner content and geometry");
        }
        using (var f = new Fixture("image"))
        {
            f.Pair.Suppress(); f.Destination.gameObject.SetActive(true); Proof.Reset();
            Check(!f.Apply(false) && !f.Destination.gameObject.activeSelf && Proof.SourceReads == 0, "suppressed branch never reactivates or reads content");
        }
        using (var f = new Fixture("image"))
        {
            f.Animate(3); Vector2 before = ((RectTransform)f.Destination).sizeDelta; Proof.Reset(); f.Apply(false, rects: false);
            Check(((RectTransform)f.Destination).sizeDelta == before && Reads("_srcRect.sizeDelta") == 0, "owner-column geometry remains locally authored");
            Check(f.Destination.localScale == f.Source.localScale && ((Image)f.DestinationGraphic!).fillAmount == ((Image)f.SourceGraphic!).fillAmount, "owner-column content and non-layout transforms stay live");
            f.DestinationGraphic!.material = f.DestinationMaterial;
            f.Pair.OwnMaterial(f.DestinationMaterial); f.SourceGraphic!.material = f.SourceMaterial; Proof.Reset(); f.Apply(false);
            Check(ReferenceEquals(f.DestinationGraphic!.material, f.DestinationMaterial), "native owner material token retains priority");
            f.Pair.ReleaseMaterial(f.DestinationMaterial);
            Check(ReferenceEquals(f.DestinationGraphic.material, f.SourceMaterial), "native material release restores exact source");
        }
        using (var f = new Fixture("image"))
        {
            f.Animate(0); Proof.Reset(); PerfConfig.SharedUiWindowReadsOn = true;
            Proof.OnCallback = point => { if (point == "vertices:_dstGraphic.color") PerfConfig.SharedUiWindowReadsOn = false; };
            f.Apply(false); Check(Reads("_srcRect.anchorMin") == 1 && Reads("_srcImage.fillAmount") == 1, "read mode is stable within one exact Apply");
            ((Image)f.SourceGraphic!).fillAmount = .91f;
            Proof.Reset(); f.Apply(false);
            Check(Reads("_srcImage.fillAmount") == 2 && PerfConfig.ModeReads == 1, "live Off from native callback restores next Apply's legacy read");
        }
        int oldReads = 0, newReads = 0;
        for (int panel = 0; panel < 20; panel++)
        {
            using var source = new Fixture("image"); source.Animate(0);
            string sourceBefore = source.State(true);
            for (int observer = 0; observer < 3; observer++)
            {
                using var old = new Fixture("image"); old.Pair = new Pair(source.Source, old.Destination, 17);
                Proof.Reset(); old.Apply(true); oldReads += Proof.SourceReads;
                using var next = new Fixture("image"); next.Pair = new Pair(source.Source, next.Destination, 17);
                Proof.Reset(); PerfConfig.SharedUiWindowReadsOn = true; next.Apply(false); newReads += Proof.SourceReads;
                Check(next.State() == old.State(), "same-frame measured observer output remains exact");
                Check(source.State(true) == sourceBefore, "all observers leave their shared original source intact");
            }
        }
        Check(newReads < oldReads, "tracked native source reads are reduced across 60 mirror applications");
        Metrics = "20 animated Image nodes x 3 independent observers: tracked source getters " + oldReads + " -> " + newReads + "; 16 intermediate animation steps, 40 steady passes per kind; no frame or peer cache";
        yield return null;
        Proof.Reset();
    }
}
