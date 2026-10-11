using System;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class FrameMrOptionsProgram
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    { _assertions++; if (!condition) throw new InvalidOperationException(message); }
    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
        return rect;
    }
    private static void Runtime(bool frame, FrameNativePassthroughStatus status, bool available)
    { FrameNativePassthrough.Required = frame; FrameNativePassthrough.Status = status; FrameNativePassthrough.IsAvailable = available; }
    private static void Hoverable(GraphicRaycaster raycaster, Camera camera, GameObject control)
    {
        Canvas.ForceUpdateCanvases(); camera.Render();
        var data = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(camera, control.transform.position) };
        var hits = new List<RaycastResult>(); raycaster.Raycast(data, hits);
        Check(hits.Exists(hit => hit.gameObject == control), "disabled MR tile remains an actual GraphicRaycaster hit");
        var target = control.GetComponent<UITextTooltipTarget>();
        Check(target != null && target.TooltipEnabled && target.CanBeShown,
            "disabled MR control retains a live original game tooltip target");
        Check(target!.ShownTooltipText == VROptionsTab.Hint,
            "original game tooltip receives the current compatibility explanation");
    }
    public static int Run(string evidence)
    {
        _assertions = 0;
        var eventSystem = new GameObject("Native UI event system", typeof(EventSystem));
        var camera = new GameObject("Native UI camera", typeof(Camera)).GetComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = .21f;
        camera.transform.position = new Vector3(0, 0, -1); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.08f, .08f, .08f, 1);
        var target = new RenderTexture(800, 460, 24); target.Create(); camera.targetTexture = target;
        var canvas = Rect("Native world options canvas", new GameObject("Fixture root").transform, new Vector2(700, 400), Vector2.zero).gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera; canvas.transform.localScale = Vector3.one * .001f;
        var raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
        var tileRoot = Rect("Mixed Reality", canvas.transform, new Vector2(232, 180), new Vector2(-120, 60));
        var border = tileRoot.gameObject.AddComponent<Image>(); border.raycastTarget = true;
        var button = tileRoot.gameObject.AddComponent<Button>(); button.targetGraphic = border;
        var picture = Rect("Picture", tileRoot, new Vector2(220, 130), new Vector2(0, 17)).gameObject.AddComponent<Image>(); picture.raycastTarget = false;
        var label = Rect("Caption", tileRoot, new Vector2(220, 35), new Vector2(0, -62)).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset; label.fontSize = 21; label.alignment = TextAlignmentOptions.Center; label.text = "Mixed Reality"; label.raycastTarget = false;
        var template = Rect("Toggle template", canvas.transform, new Vector2(510, 40), new Vector2(0, -95));
        var title = Rect("Title", template, new Vector2(350, 35), new Vector2(-60, 0)).gameObject.AddComponent<TextMeshProUGUI>();
        title.font = TMP_Settings.defaultFontAsset; title.fontSize = 21; title.raycastTarget = true;
        var option = Rect("Option", template, new Vector2(40, 30), new Vector2(220, 0));
        var toggleGraphic = option.gameObject.AddComponent<Image>(); var donor = option.gameObject.AddComponent<Toggle>();
        donor.targetGraphic = toggleGraphic; donor.graphic = toggleGraphic;
        template.gameObject.SetActive(false);
        foreach (bool german in new[] { false, true })
        {
            Loc.German = german; VROptionsTab.Clear(); VROptionsTab.IsOpen = true;
            MixedReality.Enabled.Value = false; SkyAlternative.Style.Value = SkyStyle.Cellar;
            Runtime(true, FrameNativePassthroughStatus.Checking, false);
            var choose = (Action)VROptionsTab.MakeTile(button, picture, label);
            var toggle = VROptionsTab.BuildSwitch(template.gameObject, canvas.transform);
            Check(!button.interactable && !toggle.interactable, "checking capability disables both MR controls");
            Check(button.colors.disabledColor != button.colors.normalColor && label.color != Color.white,
                "unavailable MR has native disabled tint and a dimmed caption");
            Check(!VROptionsTab.KeyColorVisible, "native Frame hides its ineffective chromakey color row");
            Check(VROptionsTab.Hint == Loc.Mod("mr_frame_checking"), "checking state has its own localized explanation");
            Check(VROptionsTab.EnvironmentIndex() == (int)SkyStyle.Cellar, "dormant MR preference leaves the visible sky selected");
            Canvas.ForceUpdateCanvases(); label.ForceMeshUpdate(); Hoverable(raycaster, camera, button.gameObject);
            choose(); VROptionsTab.GenericEnable();
            Check(!MixedReality.Enabled.Value, "tile and generic Advanced callback cannot enable unavailable MR");
            toggle.onValueChanged.Invoke(true);
            Check(!MixedReality.Enabled.Value && !toggle.isOn, "unavailable programmatic toggle is corrected to stored value");
            foreach (var state in new[] { FrameNativePassthroughStatus.Unsupported, FrameNativePassthroughStatus.QueryFailed, FrameNativePassthroughStatus.ActivationFailed })
            {
                Runtime(true, state, false); VROptionsTab.Refresh();
                Check(!button.interactable && !toggle.interactable, "all unsupported runtime outcomes remain disabled");
                Check(VROptionsTab.Hint.Contains("Proton") && VROptionsTab.Hint.Contains("SteamVR"), "incompatible Frame help names both updatable runtime components");
                Hoverable(raycaster, camera, button.gameObject);
            }
            var fallback = VROptionsTab.BuildFallback(canvas.transform);
            Check(fallback.options[VROptionsTab.MixedRealityEnvironmentIndex].text.Contains("#8C8C8C"),
                "fallback dropdown visibly dims its unavailable MR choice");
            Check(fallback.transform.parent.Find("Title").GetComponent<UITextTooltipTarget>().ShownTooltipText == VROptionsTab.Hint,
                "fallback caption immediately explains why MR is unavailable");
            fallback.onValueChanged.Invoke(VROptionsTab.MixedRealityEnvironmentIndex);
            Check(!MixedReality.Enabled.Value && fallback.value == (int)SkyStyle.Cellar,
                "unavailable fallback selection returns to the visible environment");
            var pixels = new Texture2D(800, 460, TextureFormat.RGBA32, false);
            camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 800, 460), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(evidence, german ? "disabled-de.png" : "disabled-en.png"), pixels.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pixels); RenderTexture.active = null;
            Runtime(true, FrameNativePassthroughStatus.Available, true); VROptionsTab.Refresh();
            Check(button.interactable && toggle.interactable, "already-open menu enables MR immediately when the live session is compatible");
            Check(!fallback.options[VROptionsTab.MixedRealityEnvironmentIndex].text.Contains("#8C8C8C"),
                "open fallback restores its MR label when compatibility appears");
            choose(); Check(MixedReality.Enabled.Value, "supported MR tile enables the existing saved preference");
            Check(VROptionsTab.EnvironmentIndex() == VROptionsTab.MixedRealityEnvironmentIndex, "available MR selection reflects the active environment choice");
            MixedReality.Enabled.Value = false; toggle.SetIsOnWithoutNotify(false); toggle.onValueChanged.Invoke(true);
            Check(MixedReality.Enabled.Value, "supported original bool control enables MR");
            toggle.SetIsOnWithoutNotify(true); Runtime(true, FrameNativePassthroughStatus.Unsupported, false); VROptionsTab.Refresh();
            Check(toggle.interactable && !button.interactable, "incompatible saved-on MR can still be switched off");
            Check(VROptionsTab.EnvironmentIndex() == (int)SkyStyle.Cellar, "unsupported saved-on preference does not claim to be rendered");
            toggle.onValueChanged.Invoke(false);
            Check(!MixedReality.Enabled.Value, "switching off preserves an escape from persisted incompatible MR");
            VROptionsTab.GenericDisable();
            MixedReality.Enabled.Value = true; VROptionsTab.PickSky(SkyStyle.SwampNight);
            Check(!MixedReality.Enabled.Value && SkyAlternative.Style.Value == SkyStyle.SwampNight,
                "choosing another environment clears an incompatible saved MR preference");
            Runtime(false, FrameNativePassthroughStatus.Unsupported, false); VROptionsTab.Refresh();
            Check(button.interactable && toggle.interactable, "PC streaming MR remains available without Frame capabilities");
            Check(VROptionsTab.KeyColorVisible, "PC streaming keeps its chromakey color row available");
            Check(VROptionsTab.Hint.Contains("chroma-key"), "PC streaming retains its existing chroma-key explanation");
            choose(); Check(MixedReality.Enabled.Value, "PC retains its original independent chromakey enable path");
            VROptionsTab.Clear();
            Runtime(true, FrameNativePassthroughStatus.Unsupported, false); VROptionsTab.Refresh();
            Check(button.interactable, "removed rows release availability callbacks");
        }
        File.WriteAllText(Path.Combine(evidence, "scope.txt"),
            "Production MR availability, tile callback/tint, bool row, generic Apply guard and native tooltip attachment execute unchanged. Actual Unity uGUI/TMP and original GH UITextTooltipTarget/raycast filter execute. Configuration store, OpenXR lifecycle, authored clone layout/skin and whole-menu rebuild are explicit boundaries; no headset passthrough or complete options window is claimed.\n");
        camera.targetTexture = null; UnityEngine.Object.DestroyImmediate(target);
        return _assertions;
    }
}
