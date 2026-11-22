using System.IO;
using GLOOM.MainMenu;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
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
    // Same relief strength as the FrosthavenVR promotion. Displacement is into
    // the picture and shorter than its 18 px clearance from the native menu.
    private const float ArtworkDepth = .046f;
    private static MenuDepthHeightfield? _sceneDepth;
    private static bool _sceneDepthRead;

    private readonly Vector3[] _corners = new Vector3[4];
    private RectTransform? _tile;
    private Canvas? _canvas;
    private Sprite? _sceneSprite;
    private RectTransform? _logo;
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
        // Physical separation gives the wordmark its binocular depth. It follows
        // the same window transform on a resize.
        Vector3 logoOffset = new(0f, -((CaptionHeight + CaptionGap) + height * .56f) * .5f, -seat.width * .14f);
        float logoWidth = Mathf.Min(seat.width * .88f, height * .32f * _logoAspect);
        Vector2 logoSize = new(logoWidth, logoWidth / _logoAspect);
        if (_logo!.sizeDelta != logoSize) _logo.sizeDelta = logoSize;
        if (_logo.localPosition != logoOffset) _logo.localPosition = logoOffset;
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
        var picture = Graphic<Image>("Frosthaven panorama", Vector2.zero, Vector2.one);
        _sceneSprite = Sprite.Create(art, new Rect(0f, 0f, art.width, art.height),
            Vector2.one * .5f, 100f, 0, SpriteMeshType.FullRect);
        picture.sprite = _sceneSprite;
        picture.rectTransform.offsetMin = new(2f, 2f);
        picture.rectTransform.offsetMax = new(-2f, -CaptionHeight - CaptionGap - 2f);
        MenuDepthHeightfield? depth = ReadSceneDepth(art);
        if (depth != null)
        {
            MenuDepthGraphic? relief = picture.gameObject.AddComponent<MenuDepthGraphic>();
            if (relief != null) relief.ConfigureStandaloneArtwork(picture, depth, ArtworkDepth);
        }

        var logo = Graphic<RawImage>("FrosthavenVR raised logo", Vector2.one * .5f, Vector2.one * .5f);
        logo.texture = wordmark;
        _logo = logo.rectTransform;
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

    private static MenuDepthHeightfield? ReadSceneDepth(Texture2D art)
    {
        if (_sceneDepthRead) return _sceneDepth;
        _sceneDepthRead = true;
        try
        {
            using Stream? stream = typeof(FrosthavenMenuPromotion).Assembly.GetManifestResourceStream(Resources + "Frosthaven_scene.gmd");
            if (stream == null) throw new InvalidDataException("Missing artwork depth.");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            var field = MenuDepthHeightfield.Read(bytes.ToArray());
            if (field.SourceWidth != art.width || field.SourceHeight != art.height)
                throw new InvalidDataException("The depth image does not match the panorama dimensions.");
            _sceneDepth = field;
        }
        catch (System.Exception error) when (error is IOException or InvalidDataException)
        {
            VRLog.Warn("WorldUI", $"Frosthaven panorama depth unavailable: {error.Message}");
        }
        return _sceneDepth;
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
        if (_sceneSprite != null) Object.Destroy(_sceneSprite);
        _sceneSprite = null;
        _logo = null;
        _caption = null;
    }
}
