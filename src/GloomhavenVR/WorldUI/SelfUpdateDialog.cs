using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>What the update window is currently saying.</summary>
internal enum SelfUpdateDialogMode
{
    /// <summary>"A newer version exists" — two buttons, "Ignorieren" and "Updaten".</summary>
    Choice,

    /// <summary>Downloading / unpacking — progress bar plus a single "Abbrechen".</summary>
    Progress,

    /// <summary>A final message — one button that only closes the window.</summary>
    Done,
}

/// <summary>
/// The mod-owned "a newer version is available" window: a world-space panel floated in front of
/// the HMD, with two buttons, and — once the user has chosen to update — a progress bar.
///
/// <para>WHY THIS IS A SECOND CLASS AND NOT A REUSE OF <c>Net/VersionDialog</c>. That is the right
/// machinery and this is built out of the same parts (world-space Canvas + GraphicRaycaster + TMP +
/// real uGUI Buttons + <see cref="UguiPokeSurfaces"/> for laser and poke, placed by
/// <see cref="PanelPlacement"/>), but its shape is fixed: exactly two buttons at hard-coded anchors
/// and a body rect whose bottom 190 px are reserved for them, with no accessor for the root and no
/// way to host a third element. A progress bar, a button row that changes between two buttons and
/// one, and a label that updates every frame do not fit inside it, and widening its surface would
/// change a dialog that fires during a MULTIPLAYER JOIN — the one moment nothing should be
/// experimented on. The other candidate,
/// <c>WorldUI/ModalFallback.10.CatchAll.cs</c>, floats the GAME'S OWN error box: it has no raise
/// API at all, it polls <c>SceneController.Instance._errorMessage</c>, and its buttons are whatever
/// the game's prefab carries. Neither can carry this.</para>
///
/// <para>NOTHING HERE RE-ORIENTS WITH HEAD MOVEMENT and nothing here blocks turning. Placement is
/// the shared <see cref="PanelPlacement"/> keep-in-view clamp used by every floating modal in the
/// mod: inside the view cone the position passes through untouched, and the panel is only pulled
/// back when the head has walked or turned away from it entirely.</para>
///
/// <para>NO GAME STATE IS WRITTEN. Every object below is created by this file and destroyed by it;
/// no <c>Show</c>, <c>Hide</c>, <c>SetActive</c>, <c>CanvasGroup</c> or <c>Escape</c> is called on
/// anything the game owns. The button listeners are added ONCE at build time and are permanent
/// trampolines into <see cref="Fire"/>, which catches everything — a <c>UnityEvent.Invoke</c> has
/// no per-listener catch, so a listener that can throw would take every listener after it with it.
/// Rebinding a button means swapping a delegate field, never touching the UnityEvent.</para>
/// </summary>
internal sealed class SelfUpdateDialog
{
    // 660 x 560 with a FIXED layout in all three modes: title 22..72 from the top, body 84..354,
    // progress row 362..436, button row 464..536. The rows never move, so switching modes only
    // shows and hides — nothing re-flows under the user's hand while it is reaching for a button.
    private const float PanelWidth = 660f;
    private const float PanelHeight = 560f;

    /// <summary>Body height = PanelHeight + this. Leaves 8 px above the progress row.</summary>
    private const float BodyHeightInset = -290f;

    private GameObject? _root;
    private Canvas? _canvas;
    private Vector3 _pos;

    private TextMeshProUGUI? _title;
    private TextMeshProUGUI? _body;
    private TextMeshProUGUI? _status;

    private GameObject? _progressRow;
    private RectTransform? _progressFill;

    private GameObject? _buttonLeft;
    private GameObject? _buttonRight;
    private GameObject? _buttonCentre;
    private TextMeshProUGUI? _labelLeft;
    private TextMeshProUGUI? _labelRight;
    private TextMeshProUGUI? _labelCentre;

    private Action? _onLeft;
    private Action? _onRight;
    private Action? _onCentre;

    /// <summary>True while the window exists.</summary>
    internal bool IsShowing => _root != null;

    /// <summary>Current mode, for the caller's own bookkeeping.</summary>
    internal SelfUpdateDialogMode Mode { get; private set; } = SelfUpdateDialogMode.Choice;

    // ---- lifecycle ---------------------------------------------------------------------------

    /// <summary>
    /// Build and show the window in <see cref="SelfUpdateDialogMode.Choice"/>. Any previous
    /// instance is closed first, so this can never leave two panels in the air.
    /// </summary>
    internal void ShowChoice(string title, string body, string ignoreLabel, string updateLabel,
        Action onIgnore, Action onUpdate)
    {
        Close();
        Build();

        SetText(_title, title);
        SetText(_body, body);
        _onLeft = onIgnore;
        _onRight = onUpdate;
        SetText(_labelLeft, ignoreLabel);
        SetText(_labelRight, updateLabel);
        SetMode(SelfUpdateDialogMode.Choice);
    }

    /// <summary>Switch the open window to the progress view. Does nothing when it is not open.</summary>
    internal void ShowProgress(string body, string cancelLabel, Action onCancel)
    {
        if (_root == null)
            return;
        SetText(_body, body);
        SetText(_status, string.Empty);
        _onCentre = onCancel;
        SetText(_labelCentre, cancelLabel);
        SetProgress(0f);
        SetMode(SelfUpdateDialogMode.Progress);
    }

    /// <summary>Switch the open window to a final message with a single close button.</summary>
    internal void ShowDone(string body, string closeLabel, Action onClose)
    {
        if (_root == null)
            return;
        SetText(_body, body);
        SetText(_status, string.Empty);
        _onCentre = onClose;
        SetText(_labelCentre, closeLabel);
        SetMode(SelfUpdateDialogMode.Done);
    }

    /// <summary>The line under the progress bar — bytes, phase, whatever the caller wants to say.</summary>
    internal void SetStatus(string text) => SetText(_status, text);

    /// <summary>Fill fraction, clamped. Cheap enough to call every frame.</summary>
    internal void SetProgress(float fraction)
    {
        if (_progressFill == null)
            return;
        float f = Mathf.Clamp01(fraction);
        _progressFill.anchorMin = new Vector2(0f, 0f);
        _progressFill.anchorMax = new Vector2(f, 1f);
        _progressFill.offsetMin = Vector2.zero;
        _progressFill.offsetMax = Vector2.zero;
    }

    /// <summary>Per-frame keep-in-view. Same heal contract as every other floating panel.</summary>
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

    /// <summary>Destroy the window. Idempotent.</summary>
    internal void Close()
    {
        if (_canvas != null)
            UguiPokeSurfaces.Unregister(_canvas);
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _root = null;
        _canvas = null;
        _title = null;
        _body = null;
        _status = null;
        _progressRow = null;
        _progressFill = null;
        _buttonLeft = null;
        _buttonRight = null;
        _buttonCentre = null;
        _labelLeft = null;
        _labelRight = null;
        _labelCentre = null;
        _onLeft = null;
        _onRight = null;
        _onCentre = null;
    }

    // ---- construction ------------------------------------------------------------------------

    private void Build()
    {
        var go = new GameObject("GloomhavenVR.SelfUpdateDialog") { layer = 5 };
        _root = go;
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = CanvasConversion.WorldCamera;
        go.AddComponent<GraphicRaycaster>();
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var bg = new GameObject("Background") { layer = 5 };
        bg.transform.SetParent(go.transform, worldPositionStays: false);
        var image = bg.AddComponent<Image>();
        image.color = new Color(0.09f, 0.10f, 0.16f, 0.96f);
        MrBacking.Opacify(image); // mixed reality wants a fully solid dialog plate
        Stretch((RectTransform)bg.transform, Vector2.zero);

        _title = MakeText(go.transform, "Title", string.Empty, 34f, FontStyles.Bold,
            TextAlignmentOptions.Top);
        var titleRect = (RectTransform)_title.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -22f);
        titleRect.sizeDelta = new Vector2(-44f, 50f);
        _title.color = new Color(0.95f, 0.85f, 0.6f); // brass, like the modal chrome

        _body = MakeText(go.transform, "Body", string.Empty, 24f, FontStyles.Normal,
            TextAlignmentOptions.TopLeft);
        var bodyRect = (RectTransform)_body.transform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -84f);
        bodyRect.sizeDelta = new Vector2(-56f, BodyHeightInset);

        BuildProgressRow(go.transform);

        _buttonLeft = MakeButton(go.transform, "Ignore", new Color(0.24f, 0.26f, 0.34f), 0.27f,
            out _labelLeft, () => Fire(_onLeft, "left"));
        _buttonRight = MakeButton(go.transform, "Update", new Color(0.22f, 0.42f, 0.30f), 0.73f,
            out _labelRight, () => Fire(_onRight, "right"));
        _buttonCentre = MakeButton(go.transform, "Centre", new Color(0.35f, 0.24f, 0.22f), 0.5f,
            out _labelCentre, () => Fire(_onCentre, "centre"));

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

    private void BuildProgressRow(Transform parent)
    {
        var row = new GameObject("Progress") { layer = 5 };
        _progressRow = row;
        row.transform.SetParent(parent, worldPositionStays: false);
        var rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 0f);
        rowRect.pivot = new Vector2(0.5f, 0f);
        rowRect.anchoredPosition = new Vector2(0f, 124f);
        rowRect.sizeDelta = new Vector2(-56f, 74f);

        var track = new GameObject("Track") { layer = 5 };
        track.transform.SetParent(row.transform, worldPositionStays: false);
        var trackImage = track.AddComponent<Image>();
        trackImage.color = new Color(0.05f, 0.06f, 0.09f, 1f);
        MrBacking.Opacify(trackImage);
        var trackRect = (RectTransform)track.transform;
        trackRect.anchorMin = new Vector2(0f, 1f);
        trackRect.anchorMax = new Vector2(1f, 1f);
        trackRect.pivot = new Vector2(0.5f, 1f);
        trackRect.anchoredPosition = Vector2.zero;
        trackRect.sizeDelta = new Vector2(0f, 28f);

        var fill = new GameObject("Fill") { layer = 5 };
        fill.transform.SetParent(track.transform, worldPositionStays: false);
        var fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.42f, 0.66f, 0.44f, 1f);
        MrBacking.Opacify(fillImage);
        _progressFill = (RectTransform)fill.transform;
        SetProgress(0f);

        _status = MakeText(row.transform, "Status", string.Empty, 22f, FontStyles.Normal,
            TextAlignmentOptions.Top);
        var statusRect = (RectTransform)_status.transform;
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(1f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.anchoredPosition = Vector2.zero;
        statusRect.sizeDelta = new Vector2(0f, 34f);
    }

    private void SetMode(SelfUpdateDialogMode mode)
    {
        Mode = mode;
        bool choice = mode == SelfUpdateDialogMode.Choice;
        bool progress = mode == SelfUpdateDialogMode.Progress;

        // SetActive on objects THIS file created — never on anything the game owns.
        if (_buttonLeft != null)
            _buttonLeft.SetActive(choice);
        if (_buttonRight != null)
            _buttonRight.SetActive(choice);
        if (_buttonCentre != null)
            _buttonCentre.SetActive(!choice);
        if (_progressRow != null)
            _progressRow.SetActive(progress);
    }

    /// <summary>
    /// The ONE listener every button carries, added once at build time. It reads the current
    /// delegate rather than being re-added, and it swallows everything: a throw out of
    /// <c>UnityEvent.Invoke</c> kills every listener registered after it, on any object.
    /// </summary>
    private void Fire(Action? action, string which)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            VRLog.Error("SelfUpdate", $"update dialog {which} button handler threw: {e}");
        }
    }

    // ---- small builders (the DevPanels / VersionDialog pattern) -------------------------------

    private static void SetText(TMP_Text? label, string text)
    {
        if (label != null)
            label.text = text;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size,
        FontStyles style, TextAlignmentOptions align)
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

    private static GameObject MakeButton(Transform parent, string name, Color color, float anchorX,
        out TextMeshProUGUI label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name) { layer = 5 };
        go.transform.SetParent(parent, worldPositionStays: false);
        var image = go.AddComponent<Image>();
        image.color = color;
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(onClick);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(anchorX, 0f);
        rect.anchorMax = new Vector2(anchorX, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.sizeDelta = new Vector2(280f, 72f);

        label = MakeText(go.transform, "Label", string.Empty, 24f, FontStyles.Bold,
            TextAlignmentOptions.Center);
        Stretch((RectTransform)label.transform, new Vector2(-12f, -8f));
        return go;
    }

    private static void Stretch(RectTransform rect, Vector2 margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = margin;
    }
}
