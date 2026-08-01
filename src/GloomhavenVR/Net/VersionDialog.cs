using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>
/// The MOD-OWNED "mod version mismatch" dialog: a world-space panel floated in front of the
/// HMD with two buttons ("Als Flat-Spieler joinen" / "Abbrechen"). Deliberately NOT game UI
/// (no <c>UIConfirmationBoxManager</c>): the game's confirmation boxes ride its own modal
/// stack and nav state machine, and a mod dialog that injects itself there can wedge that
/// machine mid-join — this panel owes the game nothing and can appear at any moment of the
/// join window.
///
/// Built exactly like the mod's other from-scratch canvases (<c>DevPanels.CreateDummy</c> is
/// the pattern): world-space Canvas + GraphicRaycaster + TMP + real uGUI Buttons, registered
/// with <see cref="UguiPokeSurfaces"/> — which makes it BOTH laser-clickable
/// (<c>RayUguiDriver</c> drives every registered surface) and poke-clickable
/// (<c>PokeInteractor</c>) with zero extra input code. Placement reuses
/// <see cref="PanelPlacement"/> (spawn in front of the head, then clamp into the view cone
/// every tick, so it cannot be lost behind the player).
/// </summary>
internal sealed class VersionDialog
{
    private GameObject? _root;
    private Canvas? _canvas;
    private Vector3 _pos;

    public bool IsShowing => _root != null;

    /// <summary>Build + show the dialog. Callbacks fire once on the respective button; the
    /// dialog closes itself first so a double-tap cannot fire twice.</summary>
    public void Show(string title, string body, string flatLabel, string cancelLabel,
        Action onJoinFlat, Action onCancel)
    {
        Close();

        var go = new GameObject("GloomhavenVR.VersionDialog") { layer = 5 };
        _root = go;
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = CanvasConversion.WorldCamera;
        go.AddComponent<GraphicRaycaster>();
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(640f, 400f);

        // Backdrop (mirrors the mod's panel palette: muted dark plate).
        var bg = new GameObject("Background") { layer = 5 };
        bg.transform.SetParent(go.transform, worldPositionStays: false);
        var image = bg.AddComponent<Image>();
        image.color = new Color(0.09f, 0.10f, 0.16f, 0.96f);
        WorldUI.MrBacking.Opacify(image); // near-opaque, but MR wants a fully solid dialog plate
        Stretch((RectTransform)bg.transform, Vector2.zero);

        var titleText = MakeText(go.transform, "Title", title, 34f, FontStyles.Bold,
            TextAlignmentOptions.Top);
        var titleRect = (RectTransform)titleText.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -22f);
        titleRect.sizeDelta = new Vector2(-44f, 50f);
        titleText.color = new Color(0.95f, 0.85f, 0.6f); // brass, like the modal chrome

        var bodyText = MakeText(go.transform, "Body", body, 24f, FontStyles.Normal,
            TextAlignmentOptions.Top);
        var bodyRect = (RectTransform)bodyText.transform;
        bodyRect.anchorMin = new Vector2(0f, 0f);
        bodyRect.anchorMax = new Vector2(1f, 1f);
        bodyRect.pivot = new Vector2(0.5f, 1f);
        bodyRect.anchoredPosition = new Vector2(0f, -84f);
        bodyRect.sizeDelta = new Vector2(-44f, -190f);

        // Two buttons side by side along the bottom. "Join flat" carries the calmer accent —
        // it is the keep-playing path; "Abbrechen" (leave the session) reads warning-red.
        MakeButton(go.transform, "JoinFlat", flatLabel, new Color(0.22f, 0.42f, 0.30f),
            new Vector2(0.27f, 0f), () => Choose(onJoinFlat));
        MakeButton(go.transform, "Cancel", cancelLabel, new Color(0.45f, 0.22f, 0.20f),
            new Vector2(0.73f, 0f), () => Choose(onCancel));

        UguiPokeSurfaces.Register(_canvas);
        VRLayers.Apply(go);

        // Fresh spawn straight in front of the head; Tick keeps it in view from here on.
        Camera? head = CanvasConversion.WorldCamera;
        float ws = PanelLayout.WorldScale;
        if (head != null)
        {
            PanelPlacement.Spawn(head, ws, out _pos, out Quaternion rot);
            go.transform.SetPositionAndRotation(_pos, rot);
        }
        go.transform.localScale = Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * ws);
    }

    /// <summary>Per-frame keep-in-view (same heal contract as the floating modals).</summary>
    public void Tick()
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

    public void Close()
    {
        if (_canvas != null)
            UguiPokeSurfaces.Unregister(_canvas);
        if (_root != null)
            UnityEngine.Object.Destroy(_root);
        _canvas = null;
        _root = null;
    }

    private void Choose(Action action)
    {
        // Close FIRST: the callback may tear the whole session down (leave path), and a
        // half-dead dialog must never be able to fire a second choice.
        Close();
        try { action(); }
        catch (Exception e) { VRLog.Error("Net", $"VersionDialog choice handler threw: {e}"); }
    }

    // ---- small builders (DevPanels pattern) ---------------------------------------------

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

    private void MakeButton(Transform parent, string name, string label, Color color,
        Vector2 anchorX, Action onClick)
    {
        var go = new GameObject(name) { layer = 5 };
        go.transform.SetParent(parent, worldPositionStays: false);
        var image = go.AddComponent<Image>();
        image.color = color;
        var button = go.AddComponent<Button>();
        button.onClick.AddListener(() => onClick());
        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(anchorX.x, 0f);
        rect.anchorMax = new Vector2(anchorX.x, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.sizeDelta = new Vector2(270f, 72f);

        var text = MakeText(go.transform, "Label", label, 24f, FontStyles.Bold,
            TextAlignmentOptions.Center);
        Stretch((RectTransform)text.transform, new Vector2(-12f, -8f));
    }

    private static void Stretch(RectTransform rect, Vector2 margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = margin;
    }
}
