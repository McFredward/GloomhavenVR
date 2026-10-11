using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>The original cabinet face canvas and shelf-only stock annotation.
/// Both the local face and its inert native provenance bank use this construction;
/// observers receive the owner's actual canvas, band and caption through capture.</summary>
internal static class TownServiceCardFace
{
    internal static GameObject CreateCanvas()
    {
        var face = new GameObject("Face", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        face.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        return face;
    }

    internal static GameObject CreateTemplate(TMP_Text? font, string caption)
    {
        GameObject face = CreateCanvas();
        AddStockBand(face.transform, Vector2.one, font, caption);
        return face;
    }

    internal static GameObject AddStockBand(Transform face, Vector2 size, TMP_Text? font, string caption)
    {
        var band = new GameObject("OriginalStockSoldOut", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = (RectTransform)band.transform;
        rect.SetParent(face, false);
        rect.sizeDelta = new Vector2(size.x * .92f, size.y * .18f);
        rect.localPosition = new Vector3(0f, 0f, -.003f);
        Image ink = band.GetComponent<Image>();
        ink.color = new Color(.19f, .035f, .055f, .94f);
        ink.raycastTarget = false;
        var label = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        if (font != null) { label.font = font.font; label.fontSharedMaterial = font.fontSharedMaterial; }
        label.text = caption;
        label.fontSize = 38f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 22f;
        label.fontSizeMax = 38f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, .9f, .72f, 1f);
        label.raycastTarget = false;
        return band;
    }
}
