using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
        // An independent original foreground at the same world metric separates coordinator
        // placement failures from direct-native SDF behavior. Real captured windows have their
        // own native-unit font treatment; this fixture's direct-render port does not run it.
        var referenceHost = Rect("independent original confirmation canvas759", null, 1920, 1080);
        var referenceCanvas = referenceHost.gameObject.AddComponent<Canvas>();
        referenceCanvas.renderMode = RenderMode.WorldSpace; referenceCanvas.worldCamera = eye;
        var referenceNative = ImportNode739(document.DocumentElement!, referenceHost, new Dictionary<string, TMP_FontAsset>());
        referenceNative.Find("Box/Box Content/Swappable Content/Secondary Content").gameObject.SetActive(false);
        referenceNative.Find("Box/Box Content/Swappable Content/Secondary General Content").gameObject.SetActive(false);
        referenceNative.Find("Box/Box Content/Report content").gameObject.SetActive(false);
        referenceNative.Find("Box/Box Content/Swappable Content/Primary Content/Text").GetComponent<TMP_Text>().text = "Return to Main Menu?";
        referenceNative.GetComponent<Image>().enabled = false;
        referenceNative.Find("Blur").GetComponent<Image>().enabled = false;
        referenceHost.gameObject.SetActive(false);
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

            var scaleRows = new List<string>
            {
                "rigScale,near,far,hostWorldPerPixel,centerDistance,brightPixels,originalBrightPixels,yesScreenX,yesScreenY,noScreenX,noScreenY"
            };
            var directTextLimits = new List<string>();
            Vector2? referenceYes = null, referenceNo = null;
            foreach (float rigScale in new[] { 47.69f * .02f, 47.69f * .05f, 47.69f * .2f, 1f, 10f, 47.69f, 200f })
            {
                // A fresh pooled opening at each scale must place only after its first head.
                // Camera planes are independent inputs following the real rig policy, not
                // enlarged from the observed dialog bounds to make a failing picture pass.
                box.IsOpen = window.IsOpen = window.IsVisible = false; surface.Tick();
                Check(imported.parent == home,
                    "each scale opening first restores the same original pooled confirmation home");
                PanelLayout.WorldScale = rigScale;
                eye.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(0, 25, 0));
                eye.nearClipPlane = Mathf.Clamp(.05f * rigScale, .01f, .5f);
                // A legitimate scenario camera with build scale 1, base far 100 and no
                // additional sky budget; the same 50000 far/near cap as the real rig.
                eye.farClipPlane = Mathf.Min(Mathf.Max(100f, 100f * rigScale), eye.nearClipPlane * 50000f);
                CanvasConversion.WorldCamera = null;
                int scaleConverts = CanvasConversion.Converts, scalePoses = CanvasConversion.PoseAuthors;
                box.IsOpen = window.IsOpen = window.IsVisible = true; surface.Tick();
                var scaled = CanvasConversion.ActivePanels.Find(p => ReferenceEquals(p.Target, imported))
                    ?? throw new InvalidOperationException("Scaled original confirmation was not converted.");
                for (int frame = 0; frame < 2; frame++) { surface.Tick(); yield return null; }
                Check(CanvasConversion.Converts == scaleConverts + 1 && CanvasConversion.PoseAuthors == scalePoses,
                    "every scale preserves one pending conversion without inventing a camera-less pose");
                CanvasConversion.WorldCamera = eye; surface.Tick();
                scaled.HostCanvas.worldCamera = eye; // Declared surrounding converter camera-binding port.
                float tolerance = Mathf.Max(.00001f, rigScale * .000001f);
                Check(Vector3.Distance(scaled.HostRect.position,
                    eye.transform.position + eye.transform.forward * (.75f * rigScale)) < tolerance
                    && Vector3.Distance(scaled.HostRect.lossyScale, Vector3.one * (.001f * .7f * rigScale)) < tolerance,
                    "every scale uses the original physical reading distance and millimeters-per-pixel law");
                Check(CanvasConversion.Converts == scaleConverts + 1 && CanvasConversion.PoseAuthors == scalePoses + 1,
                    "every delayed scale opening authors exactly one first pose");
                float centerDistance = Vector3.Dot(scaled.HostRect.position - eye.transform.position, eye.transform.forward);
                Check(centerDistance > eye.nearClipPlane && centerDistance < eye.farClipPlane,
                    "standalone confirmation center remains inside independently configured rig clip planes");
                string label = rigScale.ToString("0.##", CultureInfo.InvariantCulture);
                Texture2D image = Capture(eye, Path.Combine(output, "standalone-scale759-" + label + ".png"));
                int bright = BrightPixels(image);
                Vector2 yesScreen = eye.WorldToScreenPoint(yes.transform.position);
                Vector2 noScreen = eye.WorldToScreenPoint(no.transform.position);
                foreach (Button button in new[] { yes, no })
                {
                    Vector3 screen = eye.WorldToScreenPoint(button.transform.position);
                    Check(screen.z > eye.nearClipPlane && screen.x > 0 && screen.x < target.width
                        && screen.y > 0 && screen.y < target.height,
                        "both original confirmation options stay visibly inside the perspective viewport at every scale");
                    var data = new PointerEventData(EventSystem.current) { position = screen };
                    var hits = new List<RaycastResult>(); scaled.HostRaycaster.Raycast(data, hits);
                    var hitRows = new List<string> { "scale=" + label + " button=" + button.name + " screen=" + screen.ToString("R")
                        + " raycaster=" + scaled.HostRaycaster.isActiveAndEnabled + " hits=" + hits.Count
                        + " eventCamera=" + (scaled.HostRaycaster.eventCamera == eye) };
                    foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>(true))
                        hitRows.Add(graphic.name + " depth=" + graphic.depth + " active=" + graphic.isActiveAndEnabled
                            + " raycast=" + graphic.raycastTarget + " cull=" + graphic.canvasRenderer.cull
                            + " contains=" + RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, screen, eye)
                            + " filter=" + graphic.Raycast(screen, eye));
                    File.AppendAllLines(Path.Combine(output, "standalone-native-hits759.txt"), hitRows);
                    Check(hits.Exists(hit => hit.gameObject == button.gameObject || hit.gameObject.transform.IsChildOf(button.transform)),
                        "both original confirmation options retain actual native graphic hits at every scale");
                }
                scaled.HostGo.SetActive(false);
                referenceHost.SetPositionAndRotation(eye.transform.position + eye.transform.forward * (.75f * rigScale),
                    Quaternion.Euler(0, 25, 0));
                referenceHost.localScale = Vector3.one * (.001f * .7f * rigScale);
                referenceHost.gameObject.SetActive(true);
                Texture2D nativePixels = Capture(eye, Path.Combine(output, "standalone-original759-" + label + ".png"));
                int originalBright = BrightPixels(nativePixels);
                referenceHost.gameObject.SetActive(false); scaled.HostGo.SetActive(true);
                // Comparing two originals temporarily disables the current Canvas. Rebuild its
                // graphic depths after restoring it before asking its actual native raycaster.
                Canvas.ForceUpdateCanvases();
                Check(BrightPixels(nativePixels) <= 30 ? bright <= 30 : bright > 30,
                    "scaled standalone original glyph visibility matches an independent native foreground at the same metric");
                if (originalBright <= 30)
                    directTextLimits.Add(label + ": current=" + bright + ", independentOriginal=" + originalBright
                        + "; direct-native SDF port is not proof of the actual captured-window font path");
                Object.DestroyImmediate(image); Object.DestroyImmediate(nativePixels);
                scaleRows.Add(string.Join(",", Array.ConvertAll(new[] { rigScale, eye.nearClipPlane, eye.farClipPlane,
                    scaled.HostRect.lossyScale.x, centerDistance, bright, originalBright, yesScreen.x, yesScreen.y, noScreen.x, noScreen.y },
                    value => value.ToString("R", CultureInfo.InvariantCulture))));
                File.WriteAllLines(Path.Combine(output, "standalone-scale759.csv"), scaleRows);
                File.WriteAllLines(Path.Combine(output, "standalone-direct-font-limits759.txt"), directTextLimits);
                if (referenceYes.HasValue && referenceNo.HasValue)
                    Check(Vector2.Distance(yesScreen, referenceYes.Value) < 1.5f
                        && Vector2.Distance(noScreen, referenceNo.Value) < 1.5f,
                        "physical scaling preserves the apparent perspective framing of both original options");
                referenceYes = yesScreen; referenceNo = noScreen;
                Check(confirms == 1,
                    "scale validation and pooled recovery never synthesize another native decision");
            }
        }
        finally
        {
            surface.Shutdown(); ConfirmationOnOwner.Reset();
            Singleton<UIConfirmationBoxManager>.Instance = previous;
            CanvasConversion.WorldCamera = previousCamera; PanelLayout.WorldScale = previousScale;
            Object.DestroyImmediate(home.gameObject); Object.DestroyImmediate(eye.gameObject);
            Object.DestroyImmediate(referenceHost.gameObject);
            target.Release(); Object.DestroyImmediate(target); if (events != null) Object.DestroyImmediate(events);
        }
        yield return _checks;
    }
}
