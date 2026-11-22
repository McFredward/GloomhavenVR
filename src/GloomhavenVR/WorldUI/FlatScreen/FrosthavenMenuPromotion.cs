using GLOOM.MainMenu;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>The FrosthavenVR teaser uses the movable menu's frame and has its own stereo graphics.</summary>
internal sealed class FrosthavenMenuPromotion
{
    private const string Resources = "GloomhavenVR.Assets.MenuPromotion.";
    private const float ArtAspect = 16f / 9f;
    private const float CaptionHeight = 38f;
    private const float CaptionGap = 8f;

    private readonly Vector3[] _corners = new Vector3[4];
    private RectTransform? _tile;
    private Canvas? _canvas;
    private FrosthavenWordmark? _wordmarkVolume;
    private RawImage? _logoFallback;
    private bool _volumeUnavailable;
    private float _nextVolumePoll;
    private float _logoAspect;
    private TextMeshProUGUI? _caption;
    private bool _assetsUnavailable;

    internal void Tick(RectTransform frame, Camera? head, bool covered)
    {
        UIMainOptionsMenu? main = MainMenuUIManager.Instance?.mainMenu;
        if (covered || head == null || main == null || !main.isActiveAndEnabled
            || !Visible(main.window) || Visible(main.suboptionsMenu?.window)
            || Visible(Singleton<UIOptionsWindow>.Instance?.m_Window)
            || !TryDlcBounds(Singleton<PromotionDLCManager>.Instance, frame.rect, out Rect dlc)
            || !TrySeat(frame.rect, dlc, out Rect seat))
        {
            if (_tile != null && _tile.gameObject.activeSelf) _tile.gameObject.SetActive(false);
            return;
        }

        if (_tile == null)
        {
            TextMeshProUGUI? sample = main.optionsButton?.Button?.text;
            if (_assetsUnavailable || sample == null || !Create(frame, sample)) return;
        }
        float height = seat.width / ArtAspect;
        Vector2 size = new(seat.width, seat.height);
        Vector3 center = new(seat.center.x, seat.center.y, -18f);
        if (_tile!.sizeDelta != size) _tile.sizeDelta = size;
        if (_tile.localPosition != center) _tile.localPosition = center;
        if (_canvas!.worldCamera != head) _canvas.worldCamera = head;
        UpgradeWordmark();
        // Keep the cap at the same fitted position. The volume extends back
        // toward the picture and preserves the native DLC clearance.
        Vector3 logoOffset = new(0f, -((CaptionHeight + CaptionGap) + height * .56f) * .5f, -seat.width * .14f);
        float logoWidth = Mathf.Min(seat.width * .88f, height * .32f * _logoAspect);
        Vector2 logoSize = new(logoWidth, logoWidth / _logoAspect);
        if (_wordmarkVolume != null)
            _wordmarkVolume.Place(logoSize, seat.width, logoOffset);
        else if (_logoFallback != null)
        {
            RectTransform logo = _logoFallback.rectTransform;
            if (logo.sizeDelta != logoSize) logo.sizeDelta = logoSize;
            if (logo.localPosition != logoOffset) logo.localPosition = logoOffset;
        }
        string caption = Loc.Mod("frosthaven_promo_caption");
        if (_caption!.text != caption) _caption.text = caption;
        if (!_tile.gameObject.activeSelf) _tile.gameObject.SetActive(true);
    }

    private static bool Visible(UIWindow? window) =>
        window != null && window.gameObject.activeInHierarchy && (window.IsOpen || window.IsVisible);

    private bool TryDlcBounds(PromotionDLCManager? promotion, Rect frame, out Rect bounds)
    {
        bounds = default;
        if (promotion == null || !Visible(promotion.window)) return true;

        // The native manager alternates slots. In the mouse scene their authored
        // Y positions differ. Keep both rectangles reserved through any rotation.
        if (!TryProject(promotion.buyDLCPrefab?.transform as RectTransform, frame, out Rect buy)
            || !TryProject(promotion.comingSoonDLCPrefab?.transform as RectTransform, frame, out Rect soon))
            return false;
        bounds = Rect.MinMaxRect(Mathf.Min(buy.xMin, soon.xMin), Mathf.Min(buy.yMin, soon.yMin),
            Mathf.Max(buy.xMax, soon.xMax), Mathf.Max(buy.yMax, soon.yMax));
        return true;
    }

    private bool TryProject(RectTransform? slot, Rect frame, out Rect rect)
    {
        rect = default;
        if (slot == null || slot.rect.width <= 1f || slot.rect.height <= 1f) return false;
        Canvas? canvas = slot.GetComponentInParent<Canvas>(includeInactive: true)?.rootCanvas;
        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) return false;
        Camera? source = canvas.worldCamera;
        bool overlay = canvas.renderMode == RenderMode.ScreenSpaceOverlay;
        if (!overlay && source == null) return false;
        Rect viewport = overlay ? canvas.pixelRect : source!.rect;
        if (viewport.width <= 0f || viewport.height <= 0f) return false;

        slot.GetWorldCorners(_corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in _corners)
        {
            Vector2 uv;
            if (overlay) uv = Rect.PointToNormalized(viewport, RectTransformUtility.WorldToScreenPoint(null, corner));
            else
            {
                Vector3 eye = source!.WorldToViewportPoint(corner);
                if (eye.z <= 0f) return false;
                uv = viewport.min + Vector2.Scale(eye, viewport.size);
            }
            if (float.IsNaN(uv.x) || float.IsInfinity(uv.x) || float.IsNaN(uv.y) || float.IsInfinity(uv.y)) return false;
            // The headset quad displays the full captured viewport, including its crop.
            Vector2 point = frame.min + Vector2.Scale(uv, frame.size);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private static bool TrySeat(Rect frame, Rect dlc, out Rect seat)
    {
        float width = Mathf.Min(340f, frame.width * .18f);
        float height = width / ArtAspect + CaptionHeight + CaptionGap;
        float margin = frame.width * .026f;
        float right = frame.xMax - margin;
        float top = frame.yMax - frame.height * .055f;
        // A projected DLC column gets the first row and a comfortable optical
        // gutter, including the logo's slight projection forward from the glass.
        if (dlc.width > 0f && dlc.xMax > right - width - margin && dlc.xMin < right + margin)
            top = Mathf.Min(top, dlc.yMin - margin);
        seat = new Rect(right - width, top - height, width, height);
        return width > 0f && seat.yMin >= frame.yMin + margin;
    }

    private bool Create(RectTransform frame, TextMeshProUGUI sample)
    {
        Texture2D? art = EmbeddedTexture.Get(Resources + "Frosthaven_scene.jpg", false, TextureWrapMode.Clamp);
        Texture2D? wordmark = EmbeddedTexture.Get(Resources + "FrosthavenVR_logo.png", false, TextureWrapMode.Clamp);
        if (art == null || wordmark == null) { _assetsUnavailable = true; return false; }

        var tile = new GameObject("GloomhavenVR.FrosthavenPromotion", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        tile.SetActive(false);
        _tile = (RectTransform)tile.transform;
        _tile.SetParent(frame, false);
        _canvas = tile.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1;
        var group = tile.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var border = Graphic<Image>("Teaser frame", Vector2.zero, Vector2.one);
        border.color = new Color(.17f, .28f, .34f, 1f);
        border.rectTransform.offsetMax = new(0f, -CaptionHeight - CaptionGap);
        var picture = Graphic<RawImage>("Frosthaven panorama", Vector2.zero, Vector2.one);
        picture.texture = art;
        picture.rectTransform.offsetMin = new(2f, 2f);
        picture.rectTransform.offsetMax = new(-2f, -CaptionHeight - CaptionGap - 2f);

        if (!TryMountWordmark(wordmark))
        {
            _logoFallback = Graphic<RawImage>("FrosthavenVR raised logo", Vector2.one * .5f, Vector2.one * .5f);
            _logoFallback.texture = wordmark;
        }
        _logoAspect = (float)wordmark.width / wordmark.height;

        var captionAnchor = new Vector2(0f, 1f);
        var titleBacking = Graphic<Image>("Teaser caption backing", captionAnchor, Vector2.one);
        titleBacking.rectTransform.offsetMin = new(0f, -CaptionHeight);
        titleBacking.color = new Color(.05f, .12f, .16f, .96f);
        _caption = Graphic<TextMeshProUGUI>("Teaser caption", captionAnchor, Vector2.one);
        _caption.rectTransform.offsetMin = new(0f, -CaptionHeight);
        _caption.font = sample.font;
        _caption.fontSharedMaterial = sample.fontSharedMaterial;
        _caption.fontSize = 32f;
        _caption.alignment = TextAlignmentOptions.Center;
        _caption.color = new Color(.91f, .97f, 1f, 1f);
        _caption.enableWordWrapping = false;

        VRLayers.Apply(tile);
        return true;
    }

    private void UpgradeWordmark()
    {
        // The main bundle normally prewarms before the menu opens. Retain the
        // front if it is still loading, then mount the volume on the same frame.
        if (_volumeUnavailable || _logoFallback == null || Time.unscaledTime < _nextVolumePoll)
            return;
        if (!TryMountWordmark((Texture2D)_logoFallback.texture)) return;
        VRLayers.Apply(_wordmarkVolume!.Transform.gameObject);
        _logoFallback.gameObject.SetActive(false);
        Object.Destroy(_logoFallback.gameObject);
        _logoFallback = null;
    }

    private bool TryMountWordmark(Texture2D wordmark)
    {
        _nextVolumePoll = Time.unscaledTime + .5f;
        Shader? shader = PlayTray.OverlayShader();
        if (shader == null) return false;
        if (shader.isSupported)
            _wordmarkVolume = FrosthavenWordmark.Create(_tile!, wordmark, shader);
        _volumeUnavailable = _wordmarkVolume == null;
        return !_volumeUnavailable;
    }

    private T Graphic<T>(string name, Vector2 anchorMin, Vector2 anchorMax) where T : Graphic
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(T));
        var graphic = node.GetComponent<T>();
        RectTransform rect = graphic.rectTransform;
        rect.SetParent(_tile, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        graphic.raycastTarget = false;
        return graphic;
    }

    internal void Release()
    {
        if (_tile != null)
        {
            _tile.gameObject.SetActive(false);
            Object.Destroy(_tile.gameObject);
        }
        _tile = null;
        _canvas = null;
        _wordmarkVolume?.Release();
        _wordmarkVolume = null;
        _logoFallback = null;
        _volumeUnavailable = false;
        _nextVolumePoll = 0f;
        _caption = null;
    }
}
