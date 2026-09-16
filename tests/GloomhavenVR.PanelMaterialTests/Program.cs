using System;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Main()
    {
        var entry = new PanelSupersample.Entry();
        WorldUIConfig.NeutraliseGrabPassBlur = new ConfigBool { Value = true };
        Check(PanelGraphicMaterial.Read(null) == null, "missing graphic has no material");
        PanelSupersample.Capture(entry, null);
        Check(entry.GrabPassNeutralised == 0, "missing graphic is not neutralized");
        var font = new Material(new Shader("TextMeshPro/Distance Field"));
        var sprite = new Material(new Shader("TextMeshPro/Sprite"));
        var pooled = new TMP_SubMeshUI();
        var text = new TMP_Text();
        Check(PanelGraphicMaterial.Read(pooled) == null, "unpopulated submesh has no source material");
        Check(PanelGraphicMaterial.Read(text) == null, "unpopulated font has no source material");
        PanelSupersample.Capture(entry, pooled);
        PanelSupersample.Capture(entry, text);
        Check(pooled.MaterialReads == 0 && text.MaterialReads == 0, "TMP inspection never evaluates allocating getters");
        // Pooling/repopulation must remain observable: no negative material cache and no
        // forced material instance can strand a newly populated native glyph or sprite.
        for (int i = 0; i < 1000; i++)
        {
            Material? current = i % 3 == 0 ? null : i % 3 == 1 ? font : sprite;
            pooled.sharedMaterial = current;
            text.fontSharedMaterial = current;
            Check(ReferenceEquals(PanelGraphicMaterial.Read(pooled), current), "submesh observes native material repopulation without cloning");
            Check(ReferenceEquals(PanelGraphicMaterial.Read(text), current), "font observes native material repopulation without cloning");
            PanelSupersample.Capture(entry, pooled);
            PanelSupersample.Capture(entry, text);
        }
        Check(pooled.MaterialReads == 0 && text.MaterialReads == 0, "repeated capture performs no TMP material instantiation");
        Check(pooled.MaterialWrites == 0 && text.MaterialWrites == 0, "native font and sprite assignments are preserved");
        Check(pooled.enabled && text.enabled && pooled.color.a == .5f && text.color.a == .5f, "text appearance is preserved");
        Check(entry.GrabPassNeutralised == 0, "text does not count as a neutralized backdrop");
        foreach (Graphic graphic in new Graphic[] { new Image(), new RawImage(), new Text() })
        {
            Check(ReferenceEquals(PanelGraphicMaterial.Read(graphic), Graphic.defaultGraphicMaterial), "ordinary graphics retain native default fallback");
            var authored = new Material(new Shader("Custom/Card"));
            graphic.material = authored;
            int writes = graphic.MaterialWrites;
            int reads = graphic.MaterialReads;
            var color = graphic.color;
            PanelSupersample.Capture(entry, graphic);
            Check(graphic.MaterialReads == reads + 1, "capture reads ordinary material once");
            Check(graphic.MaterialWrites == writes && graphic.enabled && graphic.color.Equals(color), "non-GrabPass graphic remains unchanged");
            Check(ReferenceEquals(PanelGraphicMaterial.Read(graphic), authored), "authored non-GrabPass material retained");
        }
        var blurMaterial = new Material(new Shader("Custom/SimpleGrabPassBlur"));
        var blur = new Image { material = blurMaterial };
        WorldUIConfig.NeutraliseGrabPassBlur = null;
        PanelSupersample.Capture(entry, blur);
        Check(blur.enabled && entry.GrabPassNeutralised == 0, "unbound blur config leaves source unchanged");
        WorldUIConfig.NeutraliseGrabPassBlur = new ConfigBool { Value = false };
        PanelSupersample.Capture(entry, blur);
        Check(blur.enabled && entry.GrabPassNeutralised == 0, "disabled blur config leaves source unchanged");
        WorldUIConfig.NeutraliseGrabPassBlur.Value = true;
        PanelSupersample.Capture(entry, blur);
        Check(!blur.enabled && blur.color.Equals(Color.clear) && entry.GrabPassNeutralised == 1, "GrabPass backdrop is neutralized and counted");
        Check(ReferenceEquals(blur.material, Graphic.defaultGraphicMaterial), "blur uses native default-material reset");
        Check(blurMaterial.shader!.name == "Custom/SimpleGrabPassBlur", "shared blur asset remains unchanged");
        Check(blur.raycastTarget, "neutralization preserves native hit-testing configuration");
        PanelSupersample.Capture(entry, blur);
        Check(entry.GrabPassNeutralised == 1, "neutralization is idempotent");
        var lower = new Image { material = new Material(new Shader("custom/grabpassblur")) };
        PanelSupersample.Capture(entry, lower);
        Check(!lower.enabled && entry.GrabPassNeutralised == 2, "GrabPass detection remains case insensitive");
        var noShader = new Image { material = new Material(null) };
        PanelSupersample.Capture(entry, noShader);
        Check(noShader.enabled && entry.GrabPassNeutralised == 2, "missing shader leaves source untouched");
        bool propagated = false;
        try { PanelGraphicMaterial.Read(new Graphic { ThrowOnRead = true }); }
        catch (InvalidOperationException ex) { propagated = ex.Message == "unrelated graphic failure"; }
        Check(propagated, "unrelated graphic exceptions are not swallowed");
        Console.WriteLine($"Panel material tests passed: {_assertions} assertions.");
    }
}
