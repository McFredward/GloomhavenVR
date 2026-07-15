using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Dev support (ROADMAP P3c #9): <c>[WorldUI] DevShowAllPanels</c> spawns the full
/// panel layout with dummy content on the desktop — no HMD, no scenario needed —
/// so slot placement, scale and poke behavior (under <c>[Dev] SimulateHands</c>)
/// are exercisable. Dummy canvases are registered with
/// <see cref="UguiPokeSurfaces"/> and contain a real uGUI Button each, so the
/// synthesized-pointer pipeline can be tested end to end.
/// </summary>
internal sealed class DevPanels
{
    private static readonly PanelSlot[] Slots =
    {
        PanelSlot.InitiativeTrack, PanelSlot.ElementBoard, PanelSlot.Objectives,
        PanelSlot.CombatLog, PanelSlot.StatPanel, PanelSlot.ButtonCluster,
    };

    private readonly List<GameObject> _panels = new();
    private bool _spawned;

    public void Tick()
    {
        bool want = Plugin.DevMode.Value && WorldUIConfig.DevShowAllPanels.Value;
        if (want && !_spawned)
            Spawn();
        else if (!want && _spawned)
            Despawn();

        if (!_spawned)
            return;

        // Track slot poses (anchor may move with camera in dev fallback).
        for (int i = 0; i < _panels.Count; i++)
        {
            if (_panels[i] == null)
                continue;
            if (PanelLayout.TryGetPose(Slots[i], out Vector3 pos, out Quaternion rot))
            {
                float scale = PanelLayout.WorldScale;
                Transform t = _panels[i].transform;
                t.SetPositionAndRotation(pos, rot);
                t.localScale = Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * scale);
            }
        }
    }

    private void Spawn()
    {
        _spawned = true;
        for (int i = 0; i < Slots.Length; i++)
            _panels.Add(CreateDummy(Slots[i]));
        VRLog.Info("WorldUI", $"DevShowAllPanels: spawned {_panels.Count} dummy panels.");
    }

    private GameObject CreateDummy(PanelSlot slot)
    {
        var go = new GameObject($"GloomhavenVR.DevPanel_{slot}");
        go.layer = 5;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = CanvasConversion.WorldCamera;
        go.AddComponent<GraphicRaycaster>();
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(420f, 180f);

        var bg = new GameObject("Background") { layer = 5 };
        bg.transform.SetParent(go.transform, worldPositionStays: false);
        var image = bg.AddComponent<Image>();
        image.color = new Color(0.1f, 0.12f, 0.2f, 0.85f);
        var bgRect = (RectTransform)bg.transform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        var textGo = new GameObject("Label") { layer = 5 };
        textGo.transform.SetParent(go.transform, worldPositionStays: false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.text = $"<b>{slot}</b>\ndummy panel — poke the button";
        text.fontSize = 32f;
        text.alignment = TextAlignmentOptions.Top;
        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = new Vector2(-20f, -20f);
        WorldUIAssets.TryAssignGameFont(text);

        var buttonGo = new GameObject("PokeButton") { layer = 5 };
        buttonGo.transform.SetParent(go.transform, worldPositionStays: false);
        var buttonImage = buttonGo.AddComponent<Image>();
        buttonImage.color = new Color(0.3f, 0.6f, 0.4f, 1f);
        var button = buttonGo.AddComponent<Button>();
        PanelSlot captured = slot;
        button.onClick.AddListener(() => VRLog.Info("WorldUI", $"DevPanel '{captured}' poked."));
        var buttonRect = (RectTransform)buttonGo.transform;
        buttonRect.anchorMin = new Vector2(0.5f, 0f);
        buttonRect.anchorMax = new Vector2(0.5f, 0f);
        buttonRect.pivot = new Vector2(0.5f, 0f);
        buttonRect.anchoredPosition = new Vector2(0f, 12f);
        buttonRect.sizeDelta = new Vector2(160f, 52f);

        UguiPokeSurfaces.Register(canvas);
        return go;
    }

    private void Despawn()
    {
        _spawned = false;
        for (int i = 0; i < _panels.Count; i++)
        {
            if (_panels[i] == null)
                continue;
            Canvas canvas = _panels[i].GetComponent<Canvas>();
            if (canvas != null)
                UguiPokeSurfaces.Unregister(canvas);
            Object.Destroy(_panels[i]);
        }
        _panels.Clear();
        VRLog.Info("WorldUI", "DevShowAllPanels: dummy panels removed.");
    }

    public void Shutdown() => Despawn();
}
