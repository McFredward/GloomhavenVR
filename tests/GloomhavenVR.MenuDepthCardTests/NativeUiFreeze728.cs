using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MenuDepth;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class MenuDepthCardProgram
{
    private static bool _freezeTracing;
    private static readonly List<Transform> FreezeRoots = new();
    private static bool WarmKnown(Graphic graphic) => ((HashSet<Graphic>)AccessTools.Field(typeof(MenuDepthDriver), "KnownGraphics").GetValue(null)!).Contains(graphic);
    private static string _freezePhase = "cold";
    private static readonly Dictionary<string, int[]> FreezeNotifications = new();

    // Observe the same real native callback as production. This trace never
    // changes its outcome or substitutes a Driver/observer/hierarchy boundary.
    [HarmonyPatch(typeof(Graphic), "OnCanvasHierarchyChanged")]
    private static class NativeFreezeNotifications
    {
        [HarmonyPostfix]
        private static void After(Graphic __instance)
        {
            if (!_freezeTracing || __instance is not UnityEngine.UI.Image) return;
            bool owned = false; foreach (Transform root in FreezeRoots) owned |= __instance.transform.IsChildOf(root);
            if (!owned) return;
            if (!FreezeNotifications.TryGetValue(_freezePhase, out int[] counts))
            { counts = new int[2]; FreezeNotifications.Add(_freezePhase, counts); }
            if (WarmKnown(__instance)) counts[0]++;
            else counts[1]++;
        }
    }

    private static IEnumerator ProveNativeUiFreeze728(string output)
    {
        MenuDepthDriver.Shutdown(); WorldUIConfig.ConversionActive = true;
        var trace = new Harmony("MenuDepthCards.NativeFreeze.Trace." + typeof(MenuDepthCardProgram).Assembly.GetName().Name);
        trace.CreateClassProcessor(typeof(NativeFreezeNotifications)).Patch();
        var camera = Go("original warm native UI projector").AddComponent<Camera>();
        camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 100;
        camera.nearClipPlane = -100; camera.farClipPlane = 1000; camera.allowHDR = camera.allowMSAA = false;
        camera.cullingMask = 1 << 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        var target = new RenderTexture(320, 240, 24); target.Create(); camera.targetTexture = target;
        var expanded = new RenderTexture(448, 368, 24); expanded.Create();
        var root = Go("actual native UI owner"); root.layer = 5; var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 100;
        var scaler = root.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(320, 240); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var second = Go("second original ScreenSpaceCamera root"); second.layer = 5;
        var secondCanvas = second.AddComponent<Canvas>(); secondCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        secondCanvas.worldCamera = camera; secondCanvas.planeDistance = 180;
        var secondScaler = second.AddComponent<CanvasScaler>(); secondScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        secondScaler.referenceResolution = new Vector2(640, 480); secondScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        // The supplied native audit contains exactly this authored parent-mode
        // shape in level1: PersistentOverlay / Notification Manager_unified.
        var overlay = Go("native PersistentOverlay ancestor"); overlay.layer = 5;
        var overlayCanvas = overlay.AddComponent<Canvas>(); overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var notification = Go("native Notification Manager_unified child", overlay.transform); notification.layer = 5;
        var notificationCanvas = notification.AddComponent<Canvas>(); notificationCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        notificationCanvas.worldCamera = camera; notificationCanvas.planeDistance = 100;
        var rect = (RectTransform)root.transform;
        Image? genuineParentChange = null;
        var frame = new Rect(-.2f, -.2666667f, 1.4f, 1.5333334f); var ready = Array.Empty<Graphic>();
        Camera.CameraCallback? onRender = null;
        for (int i = 0; i < 400; i++)
        {
            var item = Go("original native ordinary Image " + i, i < 200 ? root.transform : i < 399 ? second.transform : notification.transform); item.layer = 5;
            var imageRect = (RectTransform)item.transform; imageRect.sizeDelta = new Vector2(10, 10);
            imageRect.anchoredPosition = new Vector2((i % 20) * 14 - 135, (i / 20) * 10 - 95);
            var nativeImage = item.AddComponent<Image>(); nativeImage.color = Color.white;
            if (i == 0) genuineParentChange = nativeImage;
        }
        CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = root.transform });
        CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = second.transform });
        CanvasConversion.ActivePanels.Add(new ConvertedPanel { Target = notification.transform });
        try
        {
            MenuDepthDriver.Tick(); Canvas.ForceUpdateCanvases(); camera.Render();
            int settling = Time.frameCount; while (Time.frameCount <= settling) yield return null;
            MenuDepthDriver.Tick(); Canvas.ForceUpdateCanvases(); MenuDepthDriver.Tick();
            MenuDepthNativeUiScope.FindCanvas(camera, ready);
            Matrix4x4 secondOriginal = second.transform.localToWorldMatrix, notificationOriginal = notification.transform.localToWorldMatrix;
            float secondFactor = secondCanvas.scaleFactor, secondPixels = secondCanvas.referencePixelsPerUnit;
            File.WriteAllText(Path.Combine(output, "native-canvas-families.txt"),
                "primaryMode=" + canvas.renderMode + "; primaryRoot=" + canvas.isRootCanvas
                + "; secondMode=" + secondCanvas.renderMode + "; secondRoot=" + secondCanvas.isRootCanvas
                + "; nativeNotificationMode=" + notificationCanvas.renderMode + "; nativeNotificationRoot=" + notificationCanvas.isRootCanvas
                + "; nativeNotificationRootCanvas=" + notificationCanvas.rootCanvas.name + "\n");
            Matrix4x4 original = rect.localToWorldMatrix, projection = camera.projectionMatrix,
                view = camera.worldToCameraMatrix, culling = camera.cullingMatrix;
            Vector2 minimum = rect.anchorMin, maximum = rect.anchorMax, pivot = rect.pivot, size = rect.sizeDelta;
            Vector3 position = rect.localPosition, scale = rect.localScale; Quaternion rotation = rect.localRotation;
            float factor = canvas.scaleFactor, pixelsPerUnit = canvas.referencePixelsPerUnit;
            int census = MenuDepthDriver.RootCensuses, ancestors = MenuDepthDriver.AncestorVisits, visits = MenuDepthDriver.BehaviourVisits;
            FreezeNotifications.Clear(); FreezeRoots.Clear(); FreezeRoots.Add(rect); FreezeRoots.Add(second.transform); FreezeRoots.Add(notification.transform); _freezeTracing = true;
            var rows = new List<string> { "frame,phase,rootCensuses,ancestorVisits,behaviourVisits" };
            for (int step = 0; step < 24; step++)
            {
                for (int eye = 0; eye < 2; eye++)
                {
                    _freezePhase = "synchronous expanded draw"; ClearWarmTarget(expanded);
                    using (var scope = new MenuDepthNativeUiScope(camera, canvas, eye != 0))
                    {
                        scope.ExpandCapture(expanded, frame, ready);
                        Check(WarmSame(rect.localToWorldMatrix, original), "extended raster freezes the exact live native owner matrix");
                        Check(secondCanvas.renderMode == RenderMode.WorldSpace && !secondScaler.enabled && WarmSame(second.transform.localToWorldMatrix, secondOriginal),
                            "an actual second native camera root freezes its independent scaler and exact native basis");
                        camera.Render();
                        Check(WarmSame(second.transform.localToWorldMatrix, secondOriginal), "actual extended rendering preserves every native camera-family root matrix");
                        Check(WarmSame(rect.localToWorldMatrix, original), "actual expanded Camera.Render keeps the complete native owner matrix");
                        _freezePhase = "synchronous restore";
                    }
                    Check(canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == camera && canvas.planeDistance == 100
                        && scaler.enabled && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                        && scaler.referenceResolution == new Vector2(320, 240) && scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.Expand
                        && canvas.scaleFactor == factor && canvas.referencePixelsPerUnit == pixelsPerUnit,
                        "each actual eye immediately restores original native Canvas and active CanvasScaler state");
                    Check(camera.targetTexture == target && camera.rect == new Rect(0, 0, 1, 1)
                        && camera.clearFlags == CameraClearFlags.SolidColor && camera.nearClipPlane == -100 && camera.farClipPlane == 1000
                        && WarmSame(camera.projectionMatrix, projection) && WarmSame(camera.worldToCameraMatrix, view) && WarmSame(camera.cullingMatrix, culling),
                        "each actual expanded eye restores the complete native camera target viewport matrices and clipping state");
                    Check(secondCanvas.renderMode == RenderMode.ScreenSpaceCamera && secondCanvas.worldCamera == camera && secondCanvas.planeDistance == 180
                        && secondScaler.enabled && secondScaler.referenceResolution == new Vector2(640, 480)
                        && secondCanvas.scaleFactor == secondFactor && secondCanvas.referencePixelsPerUnit == secondPixels
                        && WarmSame(second.transform.localToWorldMatrix, secondOriginal) && WarmSame(notification.transform.localToWorldMatrix, notificationOriginal),
                        "each eye restores all native root-family scalers and leaves the actual overlay-parent notification hierarchy intact");
                    Check(rect.anchorMin == minimum && rect.anchorMax == maximum && rect.pivot == pivot && rect.sizeDelta == size
                        && rect.localPosition == position && rect.localScale == scale && rect.localRotation == rotation && WarmSame(rect.localToWorldMatrix, original),
                        "each actual expanded eye restores exact native anchors size pivot and local TRS");
                }
                _freezePhase = "after ForceUpdateCanvases"; Canvas.ForceUpdateCanvases();
                rows.Add(step + ",after-force," + MenuDepthDriver.RootCensuses + "," + MenuDepthDriver.AncestorVisits + "," + MenuDepthDriver.BehaviourVisits);
                _freezePhase = "next actual frame";
                int current = Time.frameCount; while (Time.frameCount <= current) yield return null;
                MenuDepthDriver.Tick();
                rows.Add(step + ",after-native-tick," + MenuDepthDriver.RootCensuses + "," + MenuDepthDriver.AncestorVisits + "," + MenuDepthDriver.BehaviourVisits);
                File.WriteAllLines(Path.Combine(output, "warm-native-census.csv"), rows);
                Check(MenuDepthDriver.RootCensuses == census && MenuDepthDriver.AncestorVisits == ancestors && MenuDepthDriver.BehaviourVisits == visits,
                    "warm actual native captures perform zero root census or ancestor rediscovery after own Canvas hierarchy notifications");
                Check(WarmSame(rect.localToWorldMatrix, original) && scaler.enabled && secondScaler.enabled
                    && WarmSame(second.transform.localToWorldMatrix, secondOriginal) && WarmSame(notification.transform.localToWorldMatrix, notificationOriginal),
                    "the next real native frame retains its restored CanvasScaler and owner geometry");
            }
            File.WriteAllText(Path.Combine(output, "warm-only-notifications.txt"), WarmNotificationSummary());
            _freezePhase = "genuine native parent change";
            int beforeParent = MenuDepthDriver.AncestorVisits;
            genuineParentChange!.transform.SetParent(second.transform, false);
            int changedFrame = Time.frameCount; while (Time.frameCount <= changedFrame) yield return null;
            MenuDepthDriver.Tick();
            Check(MenuDepthDriver.AncestorVisits > beforeParent && MenuDepthDriver.RootCensuses == census,
                "genuine native Graphic parent changes remain discovered without repeating a root census");
            Image? birth = null;
            _freezePhase = "actual onPreRender native birth";
            onRender = drawn =>
            {
                if (drawn != camera || birth != null) return;
                var born = Go("genuine native Image component birth during onPreRender", root.transform); born.layer = 5;
                birth = born.AddComponent<Image>(); birth.color = Color.white;
            };
            Camera.onPreRender += onRender; ClearWarmTarget(expanded);
            using (var scope = new MenuDepthNativeUiScope(camera, canvas))
            { scope.ExpandCapture(expanded, frame, ready); camera.Render(); }
            Camera.onPreRender -= onRender; onRender = null;
            int bornFrame = Time.frameCount; while (Time.frameCount <= bornFrame) yield return null;
            Canvas.ForceUpdateCanvases(); MenuDepthDriver.Tick();
            Check(birth != null && WarmKnown(birth), "real native Graphic birth during actual capture is still discovered");
            File.WriteAllText(Path.Combine(output, "native-freeze-scope.txt"),
                "ordinaryImages=400; independentSCCRoots=2; warmFrames=24; actualWarmCameraRenders=48; actualNativeBirthRenders=1; observerGuard=none; noArtificialHierarchyNotifications=true\n");
            File.WriteAllText(Path.Combine(output, "native-hierarchy-notifications.txt"), WarmNotificationSummary());
            RenderTexture prior = RenderTexture.active; RenderTexture.active = expanded;
            var image = new Texture2D(expanded.width, expanded.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply(); RenderTexture.active = prior;
            File.WriteAllBytes(Path.Combine(output, "warm-native-expanded.png"), image.EncodeToPNG());
            int visible = 0; foreach (Color32 pixel in image.GetPixels32()) if (pixel.r > 100 && pixel.g > 100 && pixel.b > 100) visible++;
            Check(visible > 500, "the quiet warm census proof actually draws original native UI into its cleared expanded GPU target");
            Object.DestroyImmediate(image);
        }
        finally
        {
            if (onRender != null) Camera.onPreRender -= onRender;
            File.WriteAllText(Path.Combine(output, "native-hierarchy-notifications.txt"), WarmNotificationSummary());
            _freezeTracing = false; FreezeRoots.Clear(); MenuDepthDriver.Shutdown(); MenuDepthNativeUiScope.Clear();
            CanvasConversion.ActivePanels.Clear(); trace.UnpatchAll(trace.Id);
            target.Release(); expanded.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(expanded);
        }
    }

    private static string WarmNotificationSummary()
    {
        string result = "phase,known-native-hierarchy-callbacks,unknown-native-hierarchy-callbacks\n";
        foreach (var pair in FreezeNotifications) result += pair.Key + "," + string.Join(",", pair.Value) + "\n";
        return result;
    }
    private static bool WarmSame(Matrix4x4 a, Matrix4x4 b)
    { for (int i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > .00001f) return false; return true; }
    private static void ClearWarmTarget(RenderTexture target)
    { RenderTexture prior = RenderTexture.active; RenderTexture.active = target; GL.Clear(true, true, Color.clear); RenderTexture.active = prior; }
}
