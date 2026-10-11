using System;
using System.Globalization;
using System.IO;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class NotesProgram
{
    private static int count;
    private static void Check(bool value, string message)
    { count++; if (!value) throw new InvalidOperationException(message); }
    private static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = size;
        return rect;
    }
    private static void Flush(RectTransform content, TMP_Text text)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        text.ForceMeshUpdate();
        Canvas.ForceUpdateCanvases();
    }
    private static GameObject Template(Transform parent)
    {
        var row = Rect("Native toggle row skeleton", parent, new Vector2(700, 44));
        var layout = row.gameObject.AddComponent<LayoutElementExtended>();
        layout.minHeight = layout.preferredHeight = 44;
        layout.flexibleHeight = 0;
        layout.MaxHeight = 44;
        layout.scalePreferredHeight = .75f;
        row.gameObject.AddComponent<Image>().color = new Color(.14f, .19f, .22f, 1);
        var title = Rect("Title", row, Vector2.zero);
        title.anchorMin = Vector2.zero;
        title.anchorMax = new Vector2(.55f, 1);
        title.offsetMin = new Vector2(12, 8);
        title.offsetMax = new Vector2(-12, -8);
        title.gameObject.AddComponent<TextMeshProUGUI>().font = VROptionsTab.Font;
        Rect("Option", row, new Vector2(100, 30));
        Rect("MenuElementFrame", row, new Vector2(100, 30));
        row.gameObject.SetActive(false);
        return row.gameObject;
    }
    private static void Capture(Camera camera, RectTransform row, RectTransform next, string path)
    {
        var target = new RenderTexture(840, 480, 24);
        var pixels = new Texture2D(840, 480, TextureFormat.RGBA32, false);
        target.Create(); camera.targetTexture = target; camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, 840, 480), 0, 0); pixels.Apply();
        Color32[] rendered = pixels.GetPixels32();
        int ink = 0; var lower = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var upper = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int y = 0; y < 480; y++)
            for (int x = 0; x < 840; x++)
            {
                Color32 pixel = rendered[y * 840 + x];
                if (pixel.r <= 180 || pixel.g <= 180 || pixel.b <= 180) continue;
                ink++; lower = Vector2.Min(lower, new Vector2(x, y)); upper = Vector2.Max(upper, new Vector2(x, y));
            }
        var corners = new Vector3[4]; row.GetWorldCorners(corners);
        Vector3 minimum = camera.WorldToScreenPoint(corners[0]);
        Vector3 maximum = camera.WorldToScreenPoint(corners[2]);
        next.GetWorldCorners(corners); float nextTop = camera.WorldToScreenPoint(corners[1]).y;
        Check(ink > 100 && lower.x >= minimum.x - 1 && lower.y >= minimum.y - 1
            && upper.x <= maximum.x + 1 && upper.y <= maximum.y + 1,
            "actual rendered glyph ink remains within the growing row without visible clipping");
        Check(lower.y > nextTop + 1,
            "actual rendered glyph ink leaves visible separation from the following native row");
        File.WriteAllText(Path.ChangeExtension(path, ".ink.txt"), "white glyph pixels=" + ink
            + "; bounds=" + lower + ".." + upper + "; row=" + minimum + ".." + maximum + "; nextTop=" + nextTop + "\n");
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
    }
    private static void Case(string evidence, string language, string value, float width, bool multiline)
    {
        var host = new GameObject("Explicit menu canvas", typeof(RectTransform), typeof(Canvas));
        var canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        host.transform.localScale = Vector3.one * .001f;
        ((RectTransform)host.transform).sizeDelta = new Vector2(800, 360);
        var content = Rect("Native vertical content", host.transform, new Vector2(width, 300));
        var group = content.gameObject.AddComponent<VerticalLayoutGroupExtended>();
        group.padding = new RectOffset(16, 16, 10, 10); group.spacing = 6;
        group.childForceExpandHeight = false; group.childForceExpandWidth = true;
        group.childAlignment = TextAnchor.UpperLeft;
        VROptionsTab.Configure(Template(host.transform));
        VROptionsTab.Note(content, value, multiline);
        var row = (RectTransform)content.GetChild(0);
        var text = row.Find("Title").GetComponent<TMP_Text>();
        var layout = row.GetComponent<LayoutElementExtended>();
        var next = Rect("Next original native setting row", content, new Vector2(600, 44));
        var nextLayout = next.gameObject.AddComponent<LayoutElementExtended>();
        nextLayout.minHeight = nextLayout.preferredHeight = 44;
        next.gameObject.AddComponent<Image>().color = new Color(.12f, .24f, .15f, 1);
        Flush(content, text);
        Check(text.font != null && text.textInfo.characterCount == value.Length,
            "real TMP resolves every original localized note character");
        Check(!row.Find("Option").gameObject.activeSelf && !row.Find("MenuElementFrame").gameObject.activeSelf,
            "notes keep original option and hover frame hidden");
        if (multiline)
        {
            Check(text.rectTransform.rect.width > row.rect.width * .9f,
                "multiline note occupies the full available native row width");
            Check(!text.enableAutoSizing && Mathf.Abs(text.fontSize - VROptionsTab.AuthoredFontSize) < .01f,
                "multiline note retains its readable authored font size without shrink-to-fit");
            Check(text.enableWordWrapping && text.textInfo.lineCount >= 2,
                "long original EN and DE notes wrap to multiple complete lines");
            Check(row.rect.height > 44 && layout.MaxHeight == -1 && layout.scalePreferredHeight == 1,
                "multiline note grows native extended layout instead of retaining a fixed height");
            float glyphBottom = float.PositiveInfinity;
            foreach (var glyph in text.textInfo.characterInfo)
            {
                if (!glyph.isVisible) continue;
                // TMP SDF quads include antialias/bearing padding beyond preferred
                // line metrics (the native 'j' extends 0.28 px below its title rect).
                // The note uses Overflow with eight-pixel row insets. Its visible
                // envelope is the original row, not an artificial per-title mask.
                Vector3 bottom = row.InverseTransformPoint(text.transform.TransformPoint(glyph.bottomLeft));
                Vector3 top = row.InverseTransformPoint(text.transform.TransformPoint(glyph.topRight));
                Check(bottom.y >= row.rect.yMin - .1f && top.y <= row.rect.yMax + .1f,
                    "every rendered note glyph remains inside the growing native row without clipping");
                glyphBottom = Mathf.Min(glyphBottom, text.transform.TransformPoint(glyph.bottomLeft).y);
            }
            var noteCorners = new Vector3[4]; var nextCorners = new Vector3[4];
            row.GetWorldCorners(noteCorners); next.GetWorldCorners(nextCorners);
            Check(noteCorners[0].y >= nextCorners[1].y + .0059f && glyphBottom > nextCorners[1].y,
                "measured note glyphs and row never overlap the next original layout row");
        }
        else
        {
            Check(text.rectTransform.anchorMax.x == .55f && layout.MaxHeight == 44
                && layout.scalePreferredHeight == .75f && Mathf.Abs(row.rect.height - 44) < .1f,
                "existing short variant notes retain their authored column and native height");
            Check(text.enableAutoSizing && !text.enableWordWrapping && text.textInfo.lineCount == 1,
                "existing short variant notes retain the original one-line caption policy");
        }
        string name = language + "-" + width.ToString(CultureInfo.InvariantCulture) + (multiline ? "-profile" : "-short");
        File.WriteAllText(Path.Combine(evidence, name + ".json"), "{\"language\":\"" + language
            + "\",\"content_width\":" + width.ToString(CultureInfo.InvariantCulture)
            + ",\"row_width\":" + row.rect.width.ToString(CultureInfo.InvariantCulture)
            + ",\"row_height\":" + row.rect.height.ToString(CultureInfo.InvariantCulture)
            + ",\"title_width\":" + text.rectTransform.rect.width.ToString(CultureInfo.InvariantCulture)
            + ",\"font_size\":" + text.fontSize.ToString(CultureInfo.InvariantCulture)
            + ",\"line_count\":" + text.textInfo.lineCount + ",\"character_count\":" + text.textInfo.characterCount + "}\n");
        if (multiline)
        {
            var cameraGo = new GameObject("Bounded note screenshot camera");
            var camera = cameraGo.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.position = new Vector3(0, 0, -1); camera.orthographic = true;
            camera.orthographicSize = .22f; camera.aspect = 840f / 480f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.05f, .07f, .08f);
            Capture(camera, row, next, Path.Combine(evidence, name + ".png"));
            UnityEngine.Object.DestroyImmediate(cameraGo);
        }
        UnityEngine.Object.DestroyImmediate(host);
    }
    public static int Run(string evidence)
    {
        count = 0;
        Check(typeof(LayoutElementExtended).Assembly.GetName().Name == "GH.Runtime"
            && typeof(VerticalLayoutGroupExtended).Assembly.GetName().Name == "GH.Runtime",
            "extended native element and layout group execute the original game assembly");
        File.WriteAllText(Path.Combine(evidence, "native-layout-assembly.txt"), typeof(LayoutElementExtended).Assembly.Location + "\n");
        VROptionsTab.Font = TMP_Settings.defaultFontAsset;
        Check(VROptionsTab.Font != null && VROptionsTab.Font.faceInfo.pointSize == 86,
            "imported TMP essentials provide real calibrated glyph metrics");
        foreach (float width in new[] { 760f, 440f })
        {
            Case(evidence, "en", OriginalNotes.English, width, true);
            Case(evidence, "de", OriginalNotes.German, width, true);
            Case(evidence, "en", "Control board: Bronze", width, false);
            Case(evidence, "de", "Kontrollboard: Bronze", width, false);
        }
        return count;
    }
}
