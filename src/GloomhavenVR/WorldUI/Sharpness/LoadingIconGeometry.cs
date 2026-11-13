using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>The native uGUI Simple Image drawing rectangle, including sprite trim.</summary>
internal static class LoadingIconGeometry
{
    internal static Rect DrawingRect(Image image)
    {
        Sprite sprite = image.overrideSprite != null ? image.overrideSprite : image.sprite;
        Rect rect = image.GetPixelAdjustedRect();
        if (sprite == null)
            return rect;

        // Unity 2021.3 uGUI Image.GetDrawingDimensions uses the full authored
        // sprite rect and DataUtility padding. LoadingBase is authored at 128x128,
        // with an 111.84776x112.84776 crop; LoadingOverlay keeps its full 128x128.
        // Normalizing each crop independently enlarges the base by about 14% and
        // makes its pulsing overlay look like a second, smaller loading symbol.
        Vector2 size = sprite.rect.size;
        if (image.preserveAspect && size.sqrMagnitude > 0f && rect.width > 0f && rect.height > 0f)
        {
            float aspect = size.x / size.y;
            if (aspect > rect.width / rect.height)
            {
                float previous = rect.height;
                rect.height = rect.width / aspect;
                rect.y += (previous - rect.height) * image.rectTransform.pivot.y;
            }
            else
            {
                float previous = rect.width;
                rect.width = rect.height * aspect;
                rect.x += (previous - rect.width) * image.rectTransform.pivot.x;
            }
        }

        Vector4 padding = DataUtility.GetPadding(sprite);
        float width = Mathf.Max(1, Mathf.RoundToInt(size.x));
        float height = Mathf.Max(1, Mathf.RoundToInt(size.y));
        return Rect.MinMaxRect(rect.x + rect.width * padding.x / width,
            rect.y + rect.height * padding.y / height,
            rect.xMax - rect.width * padding.z / width,
            rect.yMax - rect.height * padding.w / height);
    }
}
