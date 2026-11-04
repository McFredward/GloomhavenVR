using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    public static IEnumerator RunStandaloneConfirmation758(string output)
    {
        _checks = 0;
        WorldUIConfig.ConversionActive = WorldUIConfig.Dialogs.Value = true;
        FlatScreen.ManualScreenActive = false;
        VRModeStateMachine.TableInFrontOfPlayer = true;
        MenuWorldContext.IsReady = false;
        NativeFont();
        UIConfirmationBoxManager? previous = Singleton<UIConfirmationBoxManager>.Instance;
        Camera? previousCamera = CanvasConversion.WorldCamera;
        float previousScale = PanelLayout.WorldScale;
        PanelLayout.WorldScale = 1f;
        var home = Rect("original persistent confirmation home758", null, 1920, 1080);
        Object.DontDestroyOnLoad(home.gameObject);
        var document = new XmlDocument();
        document.Load(Path.Combine(_inputs, "native-confirmation-tree.xml"));
        var imported = ImportNode739(document.DocumentElement!, home, new Dictionary<string, TMP_FontAsset>());
        imported.Find("Box/Box Content/Swappable Content/Secondary Content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Swappable Content/Secondary General Content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Report content").gameObject.SetActive(false);
        imported.Find("Box/Box Content/Swappable Content/Primary Content/Text").GetComponent<TMP_Text>().text = "Return to Main Menu?";
        var box = imported.gameObject.AddComponent<ConfirmationBox>();
        var window = imported.gameObject.AddComponent<UIWindow>();
        var manager = new UIConfirmationBoxManager { CurrentBox = box };
        Singleton<UIConfirmationBoxManager>.Instance = manager;
        var surface = new DialogSurface();
        var eye = new GameObject("late actual confirmation eye758").AddComponent<Camera>();
        eye.enabled = false; eye.orthographic = false; eye.fieldOfView = 65;
        eye.nearClipPlane = .01f; eye.farClipPlane = 10;
        eye.clearFlags = CameraClearFlags.SolidColor; eye.backgroundColor = Color.black;
        eye.cullingMask = (1 << 5) | (1 << 26);
        eye.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(0, 25, 0));
        var target = new RenderTexture(800, 600, 24); target.Create(); eye.targetTexture = target;
        GameObject? events = EventSystem.current == null ? new GameObject("actual confirmation pointer758", typeof(EventSystem)) : null;
        int confirms = 0;
        var yes = imported.Find("Box/Box Content/Buttons/Yes").gameObject.AddComponent<Button>();
        yes.onClick.AddListener(() => confirms++);
        var no = imported.Find("Box/Box Content/Buttons/No").gameObject.AddComponent<Button>();
        try
        {
            ConfirmationOnOwner.Reset();
            CanvasConversion.WorldCamera = null;
            box.IsOpen = window.IsOpen = window.IsVisible = true;
            int beforeConverts = CanvasConversion.Converts, beforePoses = CanvasConversion.PoseAuthors;
            surface.Tick();
            var panel = CanvasConversion.ActivePanels.Find(p => ReferenceEquals(p.Target, imported))
                ?? throw new InvalidOperationException("Original confirmation conversion was not created.");
            Check(CanvasConversion.Converts == beforeConverts + 1 && confirms == 0,
                "camera-less native confirmation retains one original conversion and pending callback");
            for (int i = 0; i < 3; i++) { surface.Tick(); yield return null; }
            Check(CanvasConversion.Converts == beforeConverts + 1 && CanvasConversion.PoseAuthors == beforePoses,
                "missing head never allocates duplicate native confirmation hosts or fabricates an opening pose");
            CanvasConversion.WorldCamera = eye;
            surface.Tick();
            Vector3 expected = eye.transform.position + eye.transform.forward * .75f;
            Check(Vector3.Distance(panel.HostRect.position, expected) < .00001f
                && Quaternion.Angle(panel.HostRect.rotation, Quaternion.Euler(0, 25, 0)) < .001f,
                "late head places the already-open native confirmation at its original first-opening pose");
            Check(CanvasConversion.Converts == beforeConverts + 1 && CanvasConversion.PoseAuthors == beforePoses + 1,
                "late head reuses the original confirmation and authors its first pose once");
            panel.HostCanvas.worldCamera = eye; // Existing converter lifecycle's live camera binding is a declared port.
            var pixels = Capture(eye, Path.Combine(output, "late-head-native-confirmation758.png"));
            Check(BrightPixels(pixels) > 30, "late head sees actual original native confirmation text in final eye pixels");
            Object.DestroyImmediate(pixels);
            foreach (Button button in new[] { yes, no })
            {
                var pointer = new PointerEventData(EventSystem.current) { position = eye.WorldToScreenPoint(button.transform.position) };
                var hits = new List<RaycastResult>(); panel.HostRaycaster.Raycast(pointer, hits);
                Check(hits.Exists(hit => hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)),
                    "late-head original confirmation option remains reachable through its actual graphic raycaster");
            }
            Check(confirms == 0, "recovering a missing opening pose never chooses a native option");
            panel.HostRect.position += Vector3.up * .12f;
            Vector3 moved = panel.HostRect.position; Quaternion facing = panel.HostRect.rotation;
            eye.transform.position += Vector3.right * .1f;
            for (int i = 0; i < 4; i++) { surface.Tick(); yield return null; }
            Check(Vector3.Distance(panel.HostRect.position, moved) < .000001f
                && Quaternion.Angle(panel.HostRect.rotation, facing) < .001f
                && CanvasConversion.PoseAuthors == beforePoses + 1,
                "settled or manually moved native confirmation never reacquires head-follow placement");
            ExecuteEvents.Execute(yes.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            Check(confirms == 1, "late-head native option invokes only the player's explicit pointer action");
            box.IsOpen = window.IsOpen = window.IsVisible = false; surface.Tick();
            Check(imported.parent == home && !CanvasConversion.ActivePanels.Contains(panel),
                "closing a recovered original confirmation restores its exact native home");
            CanvasConversion.WorldCamera = null;
            box.IsOpen = window.IsOpen = window.IsVisible = true; surface.Tick();
            CanvasConversion.WorldCamera = eye; surface.Tick();
            var reopened = CanvasConversion.ActivePanels.Find(p => ReferenceEquals(p.Target, imported))!;
            Check(Vector3.Distance(reopened.HostRect.position, eye.transform.position + eye.transform.forward * .75f) < .00001f
                && CanvasConversion.PoseAuthors == beforePoses + 2,
                "fresh pooled native confirmation receives its own delayed first pose after prior closure");
        }
        finally
        {
            surface.Shutdown(); ConfirmationOnOwner.Reset();
            Singleton<UIConfirmationBoxManager>.Instance = previous;
            CanvasConversion.WorldCamera = previousCamera; PanelLayout.WorldScale = previousScale;
            Object.DestroyImmediate(home.gameObject); Object.DestroyImmediate(eye.gameObject);
            target.Release(); Object.DestroyImmediate(target); if (events != null) Object.DestroyImmediate(events);
        }
        yield return _checks;
    }
}
