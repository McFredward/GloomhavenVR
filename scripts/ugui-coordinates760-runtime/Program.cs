using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GloomhavenVR.Hands.Interact;
using Object = UnityEngine.Object;

public sealed class Coordinates760Filter : MonoBehaviour, ICanvasRaycastFilter
{
    internal Action? Probe;
    private bool busy;
    public bool IsRaycastLocationValid(Vector2 point, Camera camera)
    {
        if (busy) return true;
        busy = true; try { Probe?.Invoke(); return true; } finally { busy = false; }
    }
}
internal static class NativeMouse760
{
    internal static int Calls;
    internal static bool Throw;
    internal static bool Prefix(Vector2 position, ref Vector3 __result)
    {
        Calls++;
        if (Throw) throw new InvalidOperationException("native window-coordinate error");
        __result = NativeWindowedDisplayPort.RelativeMouseAtScaled(position); return false;
    }
}
public static class UguiCoordinates760Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static RectTransform Rect(string name, Transform? parent, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
    }
    private static void Windowed()
    {
        NativeMouseDisplay.main.renderingWidth = 1920; NativeMouseDisplay.main.renderingHeight = 1080;
        NativeMouseDisplay.main.systemWidth = 3440; NativeMouseDisplay.main.systemHeight = 1440;
        NativeMouseScreen.fullScreen = false;
    }
    private static void Fullscreen()
    {
        NativeMouseDisplay.main.renderingWidth = NativeMouseDisplay.main.systemWidth;
        NativeMouseDisplay.main.renderingHeight = NativeMouseDisplay.main.systemHeight;
        NativeMouseScreen.fullScreen = true;
    }
    public static IEnumerator Run(Action<int> report)
    {
        checks = 0; NativeMouse760.Throw = false; NativeMouse760.Calls = 0;
        GloomhavenVR.Core.VRLog.DebugEnabled = false;
        Check(UguiRaycastCoordinates.Available, "exact original Unity display delegate binds");
        string evidence = Environment.GetEnvironmentVariable("GHVR_COORDINATES760_EVIDENCE")!;
        string runtime = Path.Combine(evidence, "executed-runtime"); Directory.CreateDirectory(runtime);
        File.Copy(typeof(GraphicRaycaster).Assembly.Location, Path.Combine(runtime, "UnityEngine.UI.dll"), true);
        var harmony = new Harmony("gvr.fixture.coordinates760." + typeof(UguiCoordinates760Program).Assembly.GetName().Name);
        var target = UguiRaycastCoordinates.Original!;
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(NativeMouse760), nameof(NativeMouse760.Prefix)));
        RenderTexture? texture = null;
        try
        {
            new GameObject("EventSystem", typeof(EventSystem));
            var camera = new GameObject("Original head event camera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 200f;
            camera.transform.position = new Vector3(0, 0, -1000); camera.farClipPlane = 2000;
            texture = new RenderTexture(3200, 3200, 0); camera.targetTexture = texture;
            var root = Rect("Converted native menu", null, new Vector2(600, 400));
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            var raycaster = root.gameObject.AddComponent<GraphicRaycaster>();
            var buttonRect = Rect("Original Options button", root, new Vector2(30, 30)); buttonRect.anchoredPosition = new Vector2(100, 100);
            var image = buttonRect.gameObject.AddComponent<Image>(); var button = buttonRect.gameObject.AddComponent<Button>();
            int clicks = 0; button.onClick.AddListener(() => clicks++);
            var otherRoot = Rect("Unrelated native menu", null, new Vector2(600, 400));
            var otherCanvas = otherRoot.gameObject.AddComponent<Canvas>(); otherCanvas.renderMode = RenderMode.WorldSpace; otherCanvas.worldCamera = camera;
            var otherCaster = otherRoot.gameObject.AddComponent<GraphicRaycaster>();
            var otherRect = Rect("Other original button", otherRoot, new Vector2(30, 30)); otherRect.anchoredPosition = new Vector2(100, 100);
            otherRect.gameObject.AddComponent<Image>();
            yield return null; Canvas.ForceUpdateCanvases();
            Vector3 origin = camera.transform.position;
            Vector3 direction = (buttonRect.position - origin).normalized;
            Check(InputPlane760.Test(canvas, origin, direction, 2000, out _, out Vector3 physicalHit), "actual production canvas intersection reaches the original visible button");
            Vector2 screen = InputPlane760.Screen(canvas, physicalHit);
            var data = new PointerEventData(EventSystem.current) { position = screen };
            var hits = new List<RaycastResult>();
            Fullscreen(); raycaster.Raycast(data, hits);
            Check(hits.Count == 1 && hits[0].gameObject == button.gameObject, "original fullscreen raycaster reaches the same button");
            Windowed(); Vector3 warped = NativeWindowedDisplayPort.RelativeMouseAtScaled(screen);
            Check((new Vector2(warped.x, warped.y) - screen).sqrMagnitude > 1f, "original windowed display utility changes the already-projected camera coordinate");
            hits.Clear(); raycaster.Raycast(data, hits);
            Check(hits.Count == 0, "old original raycaster reproduces the windowed synthetic-coordinate miss");
            harmony.CreateClassProcessor(typeof(GraphicRaycaster_SyntheticCoordinates)).Patch();
            var pointer = new UguiPointer(); int nativeCalls = NativeMouse760.Calls;
            Check(pointer.FixturePick(raycaster, data, out RaycastResult hit) && hit.gameObject == button.gameObject,
                "scoped actual production pointer reaches the original button in windowed mode");
            Check(NativeMouse760.Calls == nativeCalls, "only the synthetic call bypasses operating-system mouse conversion");
            ExecuteEvents.Execute(hit.gameObject, data, ExecuteEvents.pointerClickHandler);
            Check(clicks == 1, "original native Button click remains available after the unchanged raycast filters");
            hits.Clear(); raycaster.Raycast(data, hits);
            Check(hits.Count == 0 && NativeMouse760.Calls == nativeCalls + 1, "native caller after the pointer lease retains original windowed conversion");
            var filter = button.gameObject.AddComponent<Coordinates760Filter>();
            bool nestedDataWasNative = false, nestedCasterWasNative = false;
            filter.Probe = () =>
            {
                var nestedData = new PointerEventData(EventSystem.current) { position = screen };
                var nested = new List<RaycastResult>(); raycaster.Raycast(nestedData, nested);
                nestedDataWasNative = nested.Count == 0;
                nested.Clear(); otherCaster.Raycast(data, nested); nestedCasterWasNative = nested.Count == 0;
            };
            Check(pointer.FixturePick(raycaster, data, out _), "outer synthetic raycast survives nested native queries");
            Check(nestedDataWasNative, "nested native pointer data never inherits the synthetic coordinate exception");
            Check(nestedCasterWasNative, "nested unrelated native raycaster never inherits the synthetic coordinate exception");
            filter.Probe = () => { throw new InvalidOperationException("native filter exception"); };
            bool threw = false;
            try { pointer.FixturePick(raycaster, data, out _); } catch (InvalidOperationException error) { threw = error.Message == "native filter exception"; }
            Check(threw, "original graphic-filter exception propagates without replacement");
            filter.Probe = null; hits.Clear(); nativeCalls = NativeMouse760.Calls; raycaster.Raycast(data, hits);
            Check(hits.Count == 0 && NativeMouse760.Calls == nativeCalls + 1, "exception unwinding restores the original coordinate scope");
            using (UguiRaycastCoordinates.Enter(otherCaster, data))
            {
                Check(pointer.FixturePick(raycaster, data, out _), "nested owned pointer may temporarily replace its outer owner");
                Check(UguiRaycastCoordinates.RelativeMouseAtScaled(screen, otherCaster, data) == new Vector3(screen.x, screen.y, camera.targetDisplay),
                    "nested owned pointer restores the preceding exact coordinate owner");
            }
            NativeMouse760.Throw = true; threw = false;
            try { UguiRaycastCoordinates.RelativeMouseAtScaled(screen, raycaster, data); }
            catch (InvalidOperationException error) { threw = error.Message == "native window-coordinate error"; }
            Check(threw, "native display exception remains unwrapped outside the exact owned pointer"); NativeMouse760.Throw = false;
            image.raycastTarget = false;
            Check(!pointer.FixturePick(raycaster, data, out _), "original non-raycast-target filter remains intact"); image.raycastTarget = true;
            var group = button.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
            Check(!pointer.FixturePick(raycaster, data, out _), "original CanvasGroup raycast lock remains intact"); group.blocksRaycasts = true;
            buttonRect.localRotation = Quaternion.Euler(0, 180, 0);
            Check(!pointer.FixturePick(raycaster, data, out _), "original reversed-graphic rejection remains intact"); buttonRect.localRotation = Quaternion.identity;
            data.position = new Vector2(-10, 100);
            Check(!pointer.FixturePick(raycaster, data, out _), "original event-camera viewport rejection remains intact"); data.position = screen;
            var block = new GameObject("Original physics blocker", typeof(BoxCollider)); block.transform.position = new Vector3(100, 100, -100);
            block.transform.localScale = new Vector3(100, 100, 10); Physics.SyncTransforms(); raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.ThreeD;
            Check(Physics.Raycast(camera.ScreenPointToRay(screen), out _, 2000), "fixture places a real physical blocker on the original event-camera ray");
            Fullscreen(); hits.Clear(); raycaster.Raycast(data, hits); bool originalBlocked = hits.Count == 0;
            Windowed(); bool ownedBlocked = !pointer.FixturePick(raycaster, data, out _);
            Check(ownedBlocked == originalBlocked, "original three-dimensional blocker behavior remains identical");
            Object.DestroyImmediate(block); raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            camera.targetDisplay = 1;
            Check(pointer.FixturePick(raycaster, data, out hit) && hit.displayIndex == 1, "synthetic coordinate retains the original event-camera display index"); camera.targetDisplay = 0;
            Fullscreen(); hits.Clear(); raycaster.Raycast(data, hits);
            Check(hits.Count == 1, "ordinary native fullscreen caller remains unchanged after patching");
            Check(pointer.FixturePick(raycaster, data, out _), "ordinary owned fullscreen pointer remains unchanged");
            var dm = new DynamicMethod("labels", typeof(void), Type.EmptyTypes); var il = dm.GetILGenerator(); var label = il.DefineLabel();
            var seam = new CodeInstruction(OpCodes.Call, target); seam.labels.Add(label);
            var viewportMethod = AccessTools.Method(typeof(Camera), nameof(Camera.ScreenToViewportPoint), new[] { typeof(Vector3) });
            var viewportSeam = new CodeInstruction(OpCodes.Callvirt, viewportMethod);
            var patched = new List<CodeInstruction>(GraphicRaycaster_SyntheticCoordinates.Transpiler(new[] { seam, viewportSeam }, il));
            Check(patched.Count == 13 && patched[0].labels.Contains(label) && seam.Calls(target)
                && patched.FindAll(instruction => instruction.Calls(viewportMethod)).Count == 1,
                "transpiler preserves labels and the original camera conversion without mutating its original instruction");
            var unknown = new CodeInstruction(OpCodes.Nop);
            var refused = new List<CodeInstruction>(GraphicRaycaster_SyntheticCoordinates.Transpiler(new[] { unknown }, il));
            Check(refused.Count == 1 && ReferenceEquals(refused[0], unknown), "unknown native seam falls back to untouched original instructions");
            var second = new CodeInstruction(OpCodes.Call, target);
            refused = new List<CodeInstruction>(GraphicRaycaster_SyntheticCoordinates.Transpiler(new[] { seam, second, viewportSeam }, il));
            Check(refused.Count == 3 && ReferenceEquals(refused[0], seam), "ambiguous native seam falls back to untouched original instructions");
            var boundary = new CodeInstruction(OpCodes.Call, target); boundary.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            refused = new List<CodeInstruction>(GraphicRaycaster_SyntheticCoordinates.Transpiler(new[] { boundary, viewportSeam }, il));
            Check(refused.Count == 2 && ReferenceEquals(refused[0], boundary), "unsupported exception boundary falls back to untouched original instructions");
            refused = new List<CodeInstruction>(GraphicRaycaster_SyntheticCoordinates.Transpiler(new[] { new CodeInstruction(OpCodes.Tailcall), seam, viewportSeam }, il));
            Check(refused.Count == 3 && ReferenceEquals(refused[1], seam), "unsupported call prefix falls back atomically without partially patching display coordinates");

            var backCamera = new GameObject("Actual backbuffer event camera", typeof(Camera)).GetComponent<Camera>();
            backCamera.enabled = false; backCamera.fieldOfView = 60; backCamera.aspect = 1f; backCamera.farClipPlane = 100;
            backCamera.transform.position = Vector3.zero;
            var pane = Rect("Admitted physical pane", null, new Vector2(1000, 800));
            pane.localScale = Vector3.one * .001f;
            pane.position = new Vector3(Mathf.Tan(38f * Mathf.Deg2Rad) * 4f, 0, 4f);
            var backCanvas = pane.gameObject.AddComponent<Canvas>(); backCanvas.renderMode = RenderMode.WorldSpace; backCanvas.worldCamera = backCamera;
            var backCaster = pane.gameObject.AddComponent<GraphicRaycaster>();
            var actualButton = Rect("Real physical button", pane, new Vector2(300, 200)); actualButton.gameObject.AddComponent<Image>();
            yield return null; Canvas.ForceUpdateCanvases();
            Vector3 worldPoint = actualButton.position;
            Check(backCamera.targetTexture == null, "backbuffer reproduction has no proxy or render texture");
            Matrix4x4 eyeProjection = Matrix4x4.Perspective(100f, 1f, .1f, 100f);
            foreach (float eyeX in new[] { -.032f, .032f })
            {
                Matrix4x4 eyeView = backCamera.worldToCameraMatrix * Matrix4x4.Translate(new Vector3(-eyeX, 0, 0));
                Vector4 clip = eyeProjection * eyeView * new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1);
                Check(clip.w > 0 && Mathf.Abs(clip.x) < clip.w && Mathf.Abs(clip.y) < clip.w,
                    "independently supplied 100-degree XR eye frustum sees the same physical button");
            }
            Vector2 backScreen = backCamera.WorldToScreenPoint(worldPoint);
            var backData = new PointerEventData(EventSystem.current) { position = backScreen };
            Check(InputPlane760.Test(backCanvas, backCamera.transform.position, (worldPoint - backCamera.transform.position).normalized,
                20f, out _, out Vector3 admitted) && Vector3.Distance(admitted, worldPoint) < .0001f,
                "actual source plane admission reaches the off-mono but eye-visible physical button");
            hits.Clear(); using (UguiRaycastCoordinates.Enter(backCaster, backData)) backCaster.Raycast(backData, hits);
            Check(hits.Count == 0, "unadmitted or native-remapped query retains its original mono viewport classification");
            backCamera.aspect = 3440f / 1440f; backData.position = backCamera.WorldToScreenPoint(worldPoint);
            hits.Clear(); using (UguiRaycastCoordinates.Enter(backCaster, backData)) backCaster.Raycast(backData, hits);
            Check(hits.Count == 1, "old viewport accepts the unchanged physical button only after widening desktop mono aspect");
            backCamera.aspect = 1f; backData.position = backCamera.WorldToScreenPoint(worldPoint);
            Check(pointer.FixturePick(backCaster, backData, out hit, backCanvas) && hit.gameObject == actualButton.gameObject,
                "admitted world pane uses logical physical bounds independent of desktop mono aspect");
            Ray originalRay = backCamera.ScreenPointToRay(backData.position);
            float along = (worldPoint.z - originalRay.origin.z) / originalRay.direction.z;
            Check(Vector3.Distance(originalRay.GetPoint(along), worldPoint) < .0001f,
                "original raw ScreenPointToRay still reaches the exact unchanged button geometry");
            using (UguiRaycastCoordinates.Enter(backCaster, backData, backCanvas))
            {
                var otherData = new PointerEventData(EventSystem.current) { position = backData.position };
                hits.Clear(); backCaster.Raycast(otherData, hits);
                Check(hits.Count == 0, "native different pointer data never inherits the world logical viewport");
                Vector3 off = new(-2f, -3f, 0);
                Vector3 rawOutside = backCamera.WorldToScreenPoint(pane.TransformPoint(new Vector3(2000, 0, 0)));
                Check(UguiRaycastCoordinates.Viewport(off, rawOutside, backCamera, backCaster, backData) == off,
                    "outside physical frame retains original viewport without clamping");
                Check(UguiRaycastCoordinates.Viewport(off, new Vector3(float.NaN, 0, 0), backCamera, backCaster, backData) == off,
                    "nonfinite pointer coordinate retains original viewport");
                Vector3 rawInterior = backCamera.WorldToScreenPoint(pane.TransformPoint(new Vector3(150, 40, 0)));
                Vector3 normalized = UguiRaycastCoordinates.Viewport(off, rawInterior, backCamera, backCaster, backData);
                Check(Mathf.Abs(normalized.x - .65f) < .001f && Mathf.Abs(normalized.y - .55f) < .001f,
                    "logical viewport derives actual physical coordinates instead of a constant inside value");
                Vector3 oldPosition = pane.position;
                pane.position = new Vector3(0, 0, -4);
                Check(UguiRaycastCoordinates.Viewport(off, backCamera.WorldToScreenPoint(pane.position), backCamera, backCaster, backData) == off,
                    "backward physical frame retains original viewport classification"); pane.position = oldPosition;
                Vector3 oldScale = pane.localScale; pane.localScale = Vector3.zero;
                Check(UguiRaycastCoordinates.Viewport(off, rawInterior, backCamera, backCaster, backData) == off,
                    "singular physical frame retains original viewport classification"); pane.localScale = oldScale;
            }
            using (UguiRaycastCoordinates.EnterCaptured(backData))
                Check(UguiRaycastCoordinates.Viewport(new Vector3(-2, -3, 0), backData.position, backCamera, backCaster, backData) == new Vector3(-2, -3, 0),
                    "captured native group never inherits a world viewport exception");
            var groupWorld = actualButton.gameObject.AddComponent<CanvasGroup>(); groupWorld.blocksRaycasts = false;
            Check(!pointer.FixturePick(backCaster, backData, out _, backCanvas), "world logical viewport retains native CanvasGroup permission"); groupWorld.blocksRaycasts = true;
            backCamera.farClipPlane = 1;
            Check(!pointer.FixturePick(backCaster, backData, out _, backCanvas), "world logical viewport retains native far-clip rejection"); backCamera.farClipPlane = 100;
            var nestedRoot = Rect("Large logical nested canvas", pane, new Vector2(1920, 1080));
            var nestedCanvas = nestedRoot.gameObject.AddComponent<Canvas>(); nestedCanvas.worldCamera = backCamera;
            UguiPokeSurfaces.Nested[backCanvas] = new List<Canvas> { nestedCanvas };
            var nestedCaster = nestedRoot.gameObject.AddComponent<GraphicRaycaster>();
            var nestedButton = Rect("Nested button", nestedRoot, new Vector2(200, 150)); nestedButton.gameObject.AddComponent<Image>();
            yield return null; Canvas.ForceUpdateCanvases();
            Check(pointer.FixturePick(nestedCaster, backData, out hit, backCanvas) && hit.gameObject == nestedButton.gameObject,
                "nested caster retains the originally admitted physical host frame");
            nestedButton.anchoredPosition = new Vector2(650, 0); Canvas.ForceUpdateCanvases();
            Vector3 nestedWorld = nestedButton.position; backData.position = backCamera.WorldToScreenPoint(nestedWorld);
            Check(!pointer.FixturePick(nestedCaster, backData, out _, backCanvas), "large nested logical canvas cannot extend its admitted host into empty space");
            GloomhavenVR.WorldUI.CanvasConversion.SetFixtureHitRect(backCanvas, new Rect(-500, -400, 1300, 800));
            Check(InputPlane760.Test(backCanvas, Vector3.zero, nestedWorld.normalized, 20, out _, out _),
                "source ray admission uses the same committed overspill frame");
            Check(pointer.FixturePick(nestedCaster, backData, out hit, backCanvas) && hit.gameObject == nestedButton.gameObject,
                "committed interaction overspill remains reachable beyond authored host bounds");
            pane.localScale = new Vector3(-.001f, -.002f, .003f); nestedButton.anchoredPosition = new Vector2(150, 40);
            Canvas.ForceUpdateCanvases(); backData.position = backCamera.WorldToScreenPoint(nestedButton.position);
            Check(pointer.FixturePick(nestedCaster, backData, out _, backCanvas), "negative and nonuniform host scales retain the original physical target");
            GloomhavenVR.Core.VRLog.DebugEnabled = true; nativeCalls = NativeMouse760.Calls;
            Check(pointer.FixturePick(raycaster, data, out _), "debug configuration receipt never changes native pointer eligibility");
            Check(NativeMouse760.Calls == nativeCalls + 1 && GloomhavenVR.Core.VRLog.DebugLines.Count == 1
                && GloomhavenVR.Core.VRLog.DebugLines[0].Contains("pixelRect=") && GloomhavenVR.Core.VRLog.DebugLines[0].Contains("native="),
                "debug receipt samples actual native coordinates and camera configuration once");
            pointer.FixturePick(raycaster, data, out _);
            Check(NativeMouse760.Calls == nativeCalls + 1 && GloomhavenVR.Core.VRLog.DebugLines.Count == 1,
                "unchanged hover never becomes an expensive debug coordinate stream");
            GloomhavenVR.Core.VRLog.DebugEnabled = false;
            report(checks);
        }
        finally { harmony.UnpatchAll(harmony.Id); if (texture != null) Object.DestroyImmediate(texture); }
    }
}
