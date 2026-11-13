using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Retain the cloned options' authored rectangles when recovered TMP stencil state is absent.</summary>
internal static class QuestOptionsMask
{
    internal static bool ApplyViewport(ScrollRect? scroll, bool questStandalone) =>
        QuestTutorialViewportMask.Apply(scroll != null ? scroll.viewport : null, questStandalone);

    internal static bool ApplySlider(Slider? slider, bool questStandalone)
    {
        if (!questStandalone || slider == null || slider.fillRect == null)
            return false;
        RectTransform fill = slider.fillRect;
        Image? image = fill.GetComponent<Image>();
        Mask? mask = fill.GetComponent<Mask>();
        // The original Master Volume donor has a plain horizontal filled Image
        // and a second amount caption under its Mask. Its TMP shader recovered
        // Comp Always, so the second caption leaks into the unfilled bar and
        // looks like an offset duplicate. Do not disable either native caption:
        // one is the grey unfilled amount, the other the filled amount. The
        // matching rectangle retains this original two-colour presentation.
        if (image == null || !image.enabled || image.type != Image.Type.Filled
            || image.fillMethod != Image.FillMethod.Horizontal || !image.fillCenter
            || mask == null || !mask.enabled || !mask.showMaskGraphic
            || fill.GetComponent<QuestOptionsSliderMask>() != null)
            return false;
        var repair = fill.gameObject.AddComponent<QuestOptionsSliderMask>();
        repair.Initialize(slider, image);
        return true;
    }
}

/// <summary>Match the exact native horizontal fill without changing its rect, art or slider ownership.</summary>
internal sealed class QuestOptionsSliderMask : MonoBehaviour
{
    private Slider? _slider;
    private Image? _image;
    private RectMask2D? _rectangle;

    internal void Initialize(Slider slider, Image image)
    {
        _slider = slider;
        _image = image;
        _rectangle = GetComponent<RectMask2D>() ?? gameObject.AddComponent<RectMask2D>();
        slider.onValueChanged.AddListener(OnValueChanged);
        Refresh();
    }

    private void OnValueChanged(float value) => Refresh();
    private void OnRectTransformDimensionsChange() => Refresh();
    private void OnEnable() => Refresh();

    private void Refresh()
    {
        if (_image == null || _rectangle == null) return;
        float hidden = Mathf.Max(0f, _image.rectTransform.rect.width) * (1f - _image.fillAmount);
        _rectangle.padding = _image.fillOrigin == (int)Image.OriginHorizontal.Right
            ? new Vector4(hidden, 0f, 0f, 0f)
            : new Vector4(0f, 0f, hidden, 0f);
    }

    private void OnDestroy()
    {
        if (_slider != null) _slider.onValueChanged.RemoveListener(OnValueChanged);
    }
}
