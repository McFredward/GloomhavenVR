using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Compat;

/// <summary>
/// The lesson's own panel: one instruction, a bar that fills as the player does it, and two
/// buttons. Built exactly like the mod's other from-scratch canvases (<see cref="Net.VersionDialog"/>
/// is the pattern) — world-space Canvas + TMP + real uGUI Buttons registered with
/// <see cref="UguiPokeSurfaces"/>, which makes it both laser-clickable and poke-clickable with no
/// input code of its own.
///
/// <para>DELIBERATELY NOT A GAME WIDGET. The game's level-message boxes are driven by
/// <c>LevelEventsController</c>'s trigger chain: a box appears because a trigger fired and closes
/// because another one did. A lesson step that ends when the player physically turns the table
/// has no trigger to offer, and injecting synthetic ones to fake it would put mod state inside
/// the machine the rest of the tutorial depends on. This panel owes that machine nothing.</para>
///
/// <para>BOTH BUTTONS ARE ALWAYS THERE, and that is a decision about the player rather than about
/// the code. NEXT is how the welcome and closing cards advance, and it is also the way out of any
/// step whose motion the room cannot currently offer — no window open to reel in, no card in the
/// fan yet. SKIP ends the lesson outright. A tutorial that can strand somebody is worse than one
/// they leave early.</para>
/// </summary>
internal sealed class ControlsPanel
{
    private GameObject? _root;
    private Canvas? _canvas;
    private TextMeshProUGUI? _title;
    private TextMeshProUGUI? _body;
    private TextMeshProUGUI? _counter;
    private RectTransform? _barFill;
    private TextMeshProUGUI? _nextLabel;
    private Vector3 _pos;

    private readonly Action _onNext;
    private readonly Action _onSkip;

    internal bool IsShowing => _root != null;

    internal ControlsPanel(Action onNext, Action onSkip)
    {
        _onNext = onNext;
        _onSkip = onSkip;
    }

    internal void Show()
    {
        if (_root != null)
            return;

        var go = new GameObject("GloomhavenVR.ControlsPanel") { layer = 5 };
        _root = go;
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = CanvasConversion.WorldCamera;
        go.AddComponent<GraphicRaycaster>();
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(660f, 430f);

        var bg = new GameObject("Background") { layer = 5 };
        bg.transform.SetParent(go.transform, worldPositionStays: false);
        var image = bg.AddComponent<Image>();
        image.color = new Color(0.09f, 0.10f, 0.16f, 0.96f);
        MrBacking.Opacify(image);
        Stretch((RectTransform)bg.transform, Vector2.zero);

        _counter = MakeText(go.transform, "Counter", string.Empty, 20f, FontStyles.Normal,
            TextAlignmentOptions.TopRight);
        var counterRect = (RectTransform)_counter.transform;
        counterRect.anchorMin = new Vector2(0f, 1f);
        counterRect.anchorMax = new Vector2(1f, 1f);
        counterRect.pivot = new Vector2(0.5f, 1f);
        counterRect.anchoredPosition = new Vector2(0f, -16f);
        counterRect.sizeDelta = new Vector2(-44f, 28f);
        _counter.color = new Color(0.62f, 0.60f, 0.55f);

        _title = MakeText(go.transform, "Title", string.Empty, 32f, FontStyles.Bold,
            TextAlignmentOptions.TopLeft);
        var titleRect = (RectTransform)_title.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -44f);
        titleRect.sizeDelta = new Vector2(-48f, 48f);
        _title.color = new Color(0.95f, 0.85f, 0.6f);

        _body = MakeText(go.transform, "Body", string.Empty, 23f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft);
        var bodyRect = (RectTransform)_body.transform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -100f);
        bodyRect.sizeDelta = new Vector2(-48f, -214f);

        BuildBar(go.transform);

        MakeButton(go.transform, "Next", string.Empty, new Color(0.22f, 0.42f, 0.30f),
            0.27f, out _nextLabel, () => Fire(_onNext));
        MakeButton(go.transform, "Skip", Loc.Mod("ctl_skip"), new Color(0.30f, 0.28f, 0.32f),
            0.73f, out _, () => Fire(_onSkip));

        UguiPokeSurfaces.Register(_canvas);
        VRLayers.Apply(go);

        Camera? head = CanvasConversion.WorldCamera;
        float ws = PanelLayout.WorldScale;
        if (head != null)
        {
            PanelPlacement.Spawn(head, ws, out _pos, out Quaternion rot);
            go.transform.SetPositionAndRotation(_pos, rot);
        }
        go.transform.localScale = Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * ws);
    }

    private void BuildBar(Transform parent)
    {
        var track = new GameObject("BarTrack") { layer = 5 };
        track.transform.SetParent(parent, worldPositionStays: false);
        var trackImage = track.AddComponent<Image>();
        trackImage.color = new Color(0.18f, 0.19f, 0.24f, 1f);
        var trackRect = (RectTransform)track.transform;
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(1f, 0f);
        trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(0f, 112f);
        trackRect.sizeDelta = new Vector2(-48f, 14f);

        var fill = new GameObject("BarFill") { layer = 5 };
        fill.transform.SetParent(track.transform, worldPositionStays: false);
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.95f, 0.76f, 0.32f, 1f);
        _barFill = (RectTransform)fill.transform;
        _barFill.anchorMin = Vector2.zero;
        _barFill.anchorMax = new Vector2(0f, 1f);
        _barFill.pivot = new Vector2(0f, 0.5f);
        _barFill.anchoredPosition = Vector2.zero;
        _barFill.sizeDelta = new Vector2(0f, 0f);
    }

    /// <summary>Write the running step into the panel. <paramref name="progress"/> is 0..1;
    /// <paramref name="nextLabel"/> is the NEXT button's caption, which changes between "next"
    /// and "I cannot do this here" depending on the step.</summary>
    internal void SetStep(string title, string body, string counter, string nextLabel,
                          float progress, bool showBar)
    {
        if (_root == null)
            return;
        if (_title != null) _title.text = title;
        if (_body != null) _body.text = body;
        if (_counter != null) _counter.text = counter;
        if (_nextLabel != null) _nextLabel.text = nextLabel;
        if (_barFill != null)
        {
            var track = (RectTransform)_barFill.parent;
            track.gameObject.SetActive(showBar);
            float width = track.rect.width * Mathf.Clamp01(progress);
            _barFill.sizeDelta = new Vector2(width - track.rect.width, 0f);
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            _barFill.sizeDelta = Vector2.zero;
        }
    }

    /// <summary>Per-frame keep-in-view, the same heal contract the floating modals use.</summary>
    internal void Tick()
    {
        if (_root == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        float ws = PanelLayout.WorldScale;
        PanelPlacement.ClampIntoView(head, ws, ref _pos, out Quaternion rot);
        _root.transform.SetPositionAndRotation(_pos, rot);
        _root.transform.localScale = Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * ws);
    }

    internal void Close()
    {
        if (_canvas != null)
            UguiPokeSurfaces.Unregister(_canvas);
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _canvas = null;
        _root = null;
        _title = _body = _counter = _nextLabel = null;
        _barFill = null;
    }

    private static void Fire(Action action)
    {
        try { action(); }
        catch (Exception e) { VRLog.Error("Tutorial", $"Controls panel button threw: {e}"); }
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, string text,
        float size, FontStyles style, TextAlignmentOptions align)
    {
        var go = new GameObject(name) { layer = 5 };
        go.transform.SetParent(parent, worldPositionStays: false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.enableWordWrapping = true;
        WorldUIAssets.TryAssignGameFont(tmp);
        return tmp;
    }

    private static void MakeButton(Transform parent, string name, string label, Color color,
        float anchorX, out TextMeshProUGUI labelText, Action onClick)
    {
        var go = new GameObject(name) { layer = 5 };
        go.transform.SetParent(parent, worldPositionStays: false);
        var image = go.AddComponent<Image>();
        image.color = color;
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(() => onClick());
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(anchorX, 0f);
        rect.anchorMax = new Vector2(anchorX, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.sizeDelta = new Vector2(280f, 70f);

        labelText = MakeText(go.transform, "Label", label, 23f, FontStyles.Bold,
            TextAlignmentOptions.Center);
        Stretch((RectTransform)labelText.transform, new Vector2(-12f, -8f));
    }

    private static void Stretch(RectTransform rect, Vector2 margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = margin;
    }
}
