using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using InControl;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using Object = UnityEngine.Object;
using Mouse = UnityEngine.InputSystem.Mouse;

public sealed class NativePointer760 : PointerInputModuleExtended
{
    public override void Process() { }
    internal PointerEventData ReadOriginal() => GetMousePointerEventData(0).GetButtonState(PointerEventData.InputButton.Left).eventData.buttonData;
    internal void HoverOriginal(PointerEventData data) => ProcessMove(data);
}
public sealed class CommonHover760 : MonoBehaviour, IPointerEnterHandler
{
    internal int Enters;
    public void OnPointerEnter(PointerEventData data) { Enters++; }
}
internal static class NativeMouse760
{
    internal static int Calls;
    internal static bool Prefix(Vector2 position, ref Vector3 __result)
    { Calls++; __result = NativeWindowedDisplayPort.RelativeMouseAtScaled(position); return false; }
}
public static class UguiCoordinates760Program
{
    private static int checks;
    private static Action<string>? failureWitness;
    private static bool witnessedFailure;
    private static void Check(bool value, string message)
    {
        checks++;
        if (value) return;
        if (!witnessedFailure)
        {
            witnessedFailure = true;
            try { failureWitness?.Invoke(message); }
            catch (Exception error) { Console.WriteLine("Pointer fixture witness failed: " + error); }
        }
        throw new Exception(message);
    }
    private static List<RaycastResult> CapturedHits(EventSystem system, Vector2 point)
    {
        var data = new PointerEventData(system) { position = point };
        var hits = new List<RaycastResult>();
        using (UguiRaycastCoordinates.EnterCaptured(data)) system.RaycastAll(data, hits);
        return hits;
    }
    private static void AppendBounds(StringBuilder text, string label, Vector3[] corners, Camera camera)
    {
        text.Append(label).Append(" world/screen corners:");
        foreach (Vector3 corner in corners)
            text.Append(" ").Append(corner.ToString("F3")).Append("/").Append(camera.WorldToScreenPoint(corner).ToString("F3"));
        text.AppendLine();
    }
    private static bool? RaycasterRegistered(GraphicRaycaster raycaster)
    {
        // The game's reference exposes this manager privately; inspect only the
        // executed assembly on failure instead of compiling against the editor API.
        try
        {
            return (typeof(EventSystem).Assembly.GetType("UnityEngine.EventSystems.RaycasterManager")?
                .GetMethod("GetRaycasters", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
                .Invoke(null, null) as IList)?.Contains(raycaster);
        }
        catch { return null; }
    }
    private static RectTransform Rect(string name, Transform? parent, Vector2 size)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false); rect.sizeDelta = size; return rect;
    }
    private static void Configure(Mouse mouse, Vector2 position, bool parked = false, int age = 0)
    {
        mouse.MakeCurrent(); InputState.Change(mouse.position, position);
        VirtualMouse.BindFixture(mouse, parked, age); VirtualMouse.ShadowFixture(position);
    }
    private static void HoverAdapter(GameObject go)
    {
        var trigger = go.AddComponent<EventTrigger>();
        foreach (EventTriggerType type in new[] { EventTriggerType.PointerEnter, EventTriggerType.PointerExit, EventTriggerType.Select, EventTriggerType.Deselect })
            trigger.triggers.Add(new EventTrigger.Entry { eventID = type });
    }

    public static IEnumerator Run(Action<int> report)
    {
        checks = 0; NativeMouse760.Calls = 0; witnessedFailure = false; failureWitness = null;
        string runtime = Path.Combine(Environment.GetEnvironmentVariable("GHVR_COORDINATES760_EVIDENCE")!, "executed-runtime");
        Directory.CreateDirectory(runtime);
        foreach (Type type in new[] { typeof(GraphicRaycaster), typeof(PointerInputModuleExtended), typeof(Utilities.InputSystemUtilities), typeof(Mouse) })
            File.Copy(type.Assembly.Location, Path.Combine(runtime, type.Assembly.GetName().Name + ".dll"), true);
        GloomhavenVR.Core.VRSession.IsRunning = true;
        NativeMouseDisplay.main.renderingWidth = 1920; NativeMouseDisplay.main.renderingHeight = 1080;
        NativeMouseDisplay.main.systemWidth = 3440; NativeMouseDisplay.main.systemHeight = 1440; NativeMouseScreen.fullScreen = false;
        var harmony = new Harmony("gvr.fixture.common760." + typeof(UguiCoordinates760Program).Assembly.GetName().Name);
        harmony.Patch(UguiRaycastCoordinates.Original, prefix: new HarmonyMethod(typeof(NativeMouse760), nameof(NativeMouse760.Prefix)));
        Mouse? mouse = null, physical = null; RenderTexture? target = null, foreign = null;
        Utilities.VirtualMouseUtilities? legacy = null;
        try
        {
            var es = new GameObject("Original EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            var module = es.gameObject.AddComponent<NativePointer760>();
            EventSystem.current = es;
            var camera = new GameObject("Original captured UI camera", typeof(Camera)).GetComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 200f;
            camera.transform.position = new Vector3(0, 0, -1000); camera.farClipPlane = 2000; camera.tag = "UICamera";
            target = new RenderTexture(3200, 1800, 0); camera.targetTexture = target;
            var root = Rect("Native full-screen canvas", null, new Vector2(3200, 1800));
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 500;
            var raycaster = root.gameObject.AddComponent<GraphicRaycaster>();
            var buttonRect = Rect("Options", root, new Vector2(140, 80)); buttonRect.anchoredPosition = new Vector2(800, 600);
            var image = buttonRect.gameObject.AddComponent<Image>(); var button = buttonRect.gameObject.AddComponent<Button>();
            var hover = buttonRect.gameObject.AddComponent<CommonHover760>(); int clicks = 0; button.onClick.AddListener(() => clicks++);
            var secondRect = Rect("Other Options", root, new Vector2(140, 80)); secondRect.anchoredPosition = new Vector2(550, 600);
            secondRect.gameObject.AddComponent<Image>();
            var flat = new FlatScreen(); flat.BindFixture(camera, target);
            Vector2 witnessPoint = default;
            bool hasWitnessPoint = false, hasWheelBounds = false, hasOverlayBounds = false, retainedScrollFixture = false;
            float wheelDeltaTime = float.NaN, wheelNormalized = float.NaN;
            int finalNativeCallsBefore = -1, finalNativeCallsAfter = -1;
            PointerEventData? witnessPointer = null;
            var wheelCorners = new Vector3[4]; var overlayCorners = new Vector3[4];
            failureWitness = message =>
            {
                // One bounded record per failing case, including intentional causal controls.
                // The original failed run did not retain its production delta or actual top hit.
                var text = new StringBuilder();
                text.AppendLine("Assertion: " + message);
                text.AppendLine($"checks={checks} frame={Time.frameCount} dt={Time.unscaledDeltaTime:R} wheelDt={wheelDeltaTime:R} wheelNormalized={wheelNormalized:R}");
                text.AppendLine($"storedPixel={witnessPoint:F3} hasPixel={hasWitnessPoint} currentOptionsPixel={camera.WorldToScreenPoint(buttonRect.position):F3}");
                text.AppendLine($"captureOwned={FlatScreen.OwnsNativeUiPixels(canvas, camera)} canvasMode={canvas.renderMode} raycasterEnabled={raycaster.isActiveAndEnabled} raycasterRegistered={RaycasterRegistered(raycaster)} buttonInteractable={button.interactable} retainedScrollFixture={retainedScrollFixture}");
                text.AppendLine($"simulatedFullscreen={NativeMouseScreen.fullScreen} simulatedRender={NativeMouseDisplay.main.renderingWidth}x{NativeMouseDisplay.main.renderingHeight} simulatedSystem={NativeMouseDisplay.main.systemWidth}x{NativeMouseDisplay.main.systemHeight} target={camera.targetTexture?.width}x{camera.targetTexture?.height}");
                text.AppendLine($"currentMouse={Mouse.current?.name} fixtureMouse={mouse?.name} added={mouse?.added} enabled={mouse?.enabled} processedPosition={(mouse != null && mouse.added ? mouse.position.ReadValue().ToString("F3") : "unavailable")} ownsStoredPosition={hasWitnessPoint && VirtualMouse.OwnsProcessedPosition(witnessPoint)}");
                text.AppendLine($"lastNativePointerPosition={witnessPointer?.position} lastNativePointerHit={witnessPointer?.pointerCurrentRaycast.gameObject?.name} nativeCallsBeforeFinal={finalNativeCallsBefore} nativeCallsAfterFinal={finalNativeCallsAfter}");
                if (hasWheelBounds) AppendBounds(text, "After original wheel", wheelCorners, camera);
                if (hasOverlayBounds) AppendBounds(text, "Controlled overlap", overlayCorners, camera);
                if (hasWitnessPoint)
                {
                    var hits = CapturedHits(es, witnessPoint);
                    text.AppendLine("Captured hit count=" + hits.Count);
                    for (int i = 0; i < Math.Min(8, hits.Count); i++)
                        text.AppendLine($"hit[{i}]={hits[i].gameObject?.name} depth={hits[i].depth} sortingLayer={hits[i].sortingLayer} sortingOrder={hits[i].sortingOrder} distance={hits[i].distance:R}");
                }
                for (int i = Math.Max(0, GloomhavenVR.Core.VRLog.DebugLines.Count - 8); i < GloomhavenVR.Core.VRLog.DebugLines.Count; i++)
                    text.AppendLine("log: " + GloomhavenVR.Core.VRLog.DebugLines[i]);
                for (int i = Math.Max(0, GloomhavenVR.Core.VRLog.Warnings.Count - 8); i < GloomhavenVR.Core.VRLog.Warnings.Count; i++)
                    text.AppendLine("warning: " + GloomhavenVR.Core.VRLog.Warnings[i]);
                string evidence = Environment.GetEnvironmentVariable("GHVR_COORDINATES760_EVIDENCE")!;
                string path = Path.Combine(evidence, "failure-witness-" + typeof(UguiCoordinates760Program).Assembly.GetName().Name + ".txt");
                File.WriteAllText(path, text.ToString());
                Console.WriteLine("Pointer fixture failure witness: " + path);
            };
            yield return null; yield return null; Canvas.ForceUpdateCanvases();
            Check(ReferenceEquals(es.currentInputModule, module), "real EventSystem selects the native mouse module under test");
            Check(FlatScreen.OwnsNativeUiPixels(canvas, camera), "actual capture producer admits its current native UI camera and render target");
            Vector2 a = camera.WorldToScreenPoint(buttonRect.position), b = camera.WorldToScreenPoint(secondRect.position);
            witnessPoint = a; hasWitnessPoint = true;
            Check(a.y > 1080f, "canonical capture provides a legitimate UI point beyond the physical desktop window");
            mouse = InputSystem.AddDevice<Mouse>("ConsoleVirtualMouse760"); physical = InputSystem.AddDevice<Mouse>("PhysicalMouse760");
            Configure(mouse, a);
            var before = module.ReadOriginal();
            Check(before.pointerCurrentRaycast.gameObject == null, "old native mouse module reproduces the captured RT window-coordinate miss");
            harmony.CreateClassProcessor(typeof(GraphicRaycaster_SyntheticCoordinates)).Patch();
            harmony.CreateClassProcessor(typeof(PointerInputModuleExtended_CapturedCoordinates)).Patch();
            flat.ClickFixture(a);
            Check(clicks == 1, "explicit captured-screen click reaches its original button immediately");
            var current = module.ReadOriginal(); module.HoverOriginal(current);
            witnessPointer = current;
            Check(current.pointerCurrentRaycast.gameObject == button.gameObject && hover.Enters == 1,
                "actual original native mouse module hovers the same button immediately");
            Check(current.position == a, "native module retains its own processed pointer position");
            var foreignData = new PointerEventData(es) { position = a }; var nativeHits = new List<RaycastResult>();
            int calls = NativeMouse760.Calls; es.RaycastAll(foreignData, nativeHits);
            Check(nativeHits.Count == 0 && NativeMouse760.Calls > calls, "unrelated native PointerEventData retains original display conversion");
            var otherModule = es.gameObject.AddComponent<NativePointer760>();
            calls = NativeMouse760.Calls; var other = otherModule.ReadOriginal();
            Check(other.pointerCurrentRaycast.gameObject == null && NativeMouse760.Calls > calls,
                "non-current native module never borrows the current module coordinate context");
            otherModule.enabled = false;
            VirtualMouse.ShadowFixture(b); InputSystem.QueueStateEvent(mouse, new MouseState { position = b });
            Check(VirtualMouse.OwnsProcessedPosition(a) && !VirtualMouse.OwnsProcessedPosition(b),
                "processed mouse position remains valid while the next position is still queued");
            current = module.ReadOriginal();
            Check(current.position == a && current.pointerCurrentRaycast.gameObject == button.gameObject,
                "native queued-update boundary retains immediate hover on the processed original position");
            InputSystem.Update(); mouse.MakeCurrent(); VirtualMouse.BindFixture(mouse);
            current = module.ReadOriginal();
            Check(current.position == b && current.delta == b - a && current.pointerCurrentRaycast.gameObject == secondRect.gameObject,
                "original native module computes its own moving pointer delta after processing the queued state");
            Configure(mouse, a); physical.MakeCurrent();
            Check(!VirtualMouse.OwnsProcessedPosition(a), "physical mouse takeover never inherits virtual screen coordinates");
            mouse.MakeCurrent();
            Check(VirtualMouse.OwnsProcessedPosition(a), "original virtual device reclaim restores its own processed coordinate provenance");
            InputSystem.DisableDevice(mouse);
            Check(!VirtualMouse.OwnsProcessedPosition(a), "disabled tracked virtual device never claims coordinates"); InputSystem.EnableDevice(mouse); mouse.MakeCurrent();
            legacy = new Utilities.VirtualMouseUtilities(); legacy.TryStart();
            Check(!VirtualMouse.OwnsProcessedPosition(a), "original registered legacy mouse provider retains its own coordinate path"); legacy.Stop(); legacy = null;
            VirtualMouse.BindFixture(mouse, parked: true);
            Check(!VirtualMouse.OwnsProcessedPosition(a), "explicit parking revokes provenance before a queued state is processed");
            Configure(mouse, VirtualMouse.ParkPixel);
            Check(!VirtualMouse.OwnsProcessedPosition(VirtualMouse.ParkPixel), "off-screen yielding pixel never claims an active pointer");
            Configure(mouse, a, age: 3);
            Check(!VirtualMouse.OwnsProcessedPosition(a), "undriven stale virtual position does not retain ownership"); Configure(mouse, a);
            GloomhavenVR.Core.VRSession.IsRunning = false;
            Check(!FlatScreen.OwnsNativeUiPixels(canvas, camera), "stopped VR never claims a captured native input domain");
            Check(!VirtualMouse.OwnsProcessedPosition(a), "stopped VR leaves the original mouse coordinate path intact"); GloomhavenVR.Core.VRSession.IsRunning = true;
            foreign = new RenderTexture(3200, 1800, 0); camera.targetTexture = foreign;
            Check(!FlatScreen.OwnsNativeUiPixels(canvas, camera), "a replaced camera target cannot impersonate the captured source"); camera.targetTexture = target;
            flat.VisibleFixture(false);
            Check(!FlatScreen.OwnsNativeUiPixels(canvas, camera), "hidden flat capture cannot retain active native pixel ownership"); flat.VisibleFixture(true);
            canvas.renderMode = RenderMode.WorldSpace;
            Check(!FlatScreen.OwnsNativeUiPixels(canvas, camera), "world panels never borrow native full-screen coordinates");
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
            var list = Rect("Native starter scroll", root, new Vector2(300, 200)); list.anchoredPosition = new Vector2(800, 250);
            var viewport = list.gameObject.AddComponent<Image>(); var scroll = list.gameObject.AddComponent<ScrollRect>();
            var content = Rect("Content", list, new Vector2(300, 800)); content.anchorMin = content.anchorMax = new Vector2(.5f, 1f); content.pivot = new Vector2(.5f, 1f);
            scroll.content = content; scroll.viewport = list; scroll.horizontal = false; scroll.inertia = false; scroll.scrollSensitivity = 30;
            var card = Rect("Full card", content, new Vector2(160, 120)); card.anchorMin = card.anchorMax = new Vector2(.5f, 1f); card.anchoredPosition = new Vector2(0, -70);
            card.gameObject.AddComponent<Image>(); HoverAdapter(card.gameObject);
            yield return null;
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = 1f; Canvas.ForceUpdateCanvases();
            Check(FlatScreen.OwnsNativeUiPixels(canvas, camera), "restored native capture still owns the original list pixel domain");
            var hand = new VRHand { Side = HandSide.Right, Thumbstick = new Vector2(0, -1) };
            wheelDeltaTime = Time.unscaledDeltaTime;
            flat.ScrollFixture(camera.WorldToScreenPoint(card.position), hand);
            wheelNormalized = scroll.verticalNormalizedPosition; card.GetWorldCorners(wheelCorners); hasWheelBounds = true;
            Check(scroll.verticalNormalizedPosition < .999f, "original flat starter-card list scrolls while a pointer-only adapter is hovered; "
                + flat.ScrollWitnessFixture() + " normalized=" + scroll.verticalNormalizedPosition + " dt=" + Time.unscaledDeltaTime);
            Check(flat.DragHandlerFixture(camera.WorldToScreenPoint(list.position)), "captured native list still owns its actual drag gesture");
            // This unmasked list belongs only to the completed wheel/drag subtest. A slow
            // wheel frame can move its later-sibling card over Options. Force that native
            // state independently of frame timing, then retire it before the separate click
            // checks. This proves fixture isolation, not the unmeasured original failure's dt.
            scroll.verticalNormalizedPosition = .5f; Canvas.ForceUpdateCanvases();
            card.GetWorldCorners(overlayCorners); hasOverlayBounds = true;
            var overlayHits = CapturedHits(es, a);
            Check(overlayHits.Count > 0 && overlayHits[0].gameObject == card.gameObject,
                "controlled unmasked scroll card is the Options pixel's actual top hit before teardown");
            Object.DestroyImmediate(list.gameObject);
            Canvas.ForceUpdateCanvases();
            var sliderRoot = Rect("Native slider", root, new Vector2(300, 50)); sliderRoot.anchoredPosition = new Vector2(800, 0);
            sliderRoot.gameObject.AddComponent<Image>(); var slider = sliderRoot.gameObject.AddComponent<Slider>();
            var container = Rect("Handle area", sliderRoot, new Vector2(280, 50));
            var handle = Rect("Handle", container, new Vector2(20, 50)); handle.gameObject.AddComponent<Image>(); slider.handleRect = handle;
            slider.minValue = 0; slider.maxValue = 1; slider.value = .5f;
            yield return null; Canvas.ForceUpdateCanvases();
            Vector2 middle = camera.WorldToScreenPoint(sliderRoot.position);
            Vector2 right = camera.WorldToScreenPoint(sliderRoot.TransformPoint(new Vector3(130, 0, 0)));
            flat.BeginFixture(middle); flat.MoveFixture(right); flat.EndFixture();
            Check(slider.value > .8f, "original primary-display slider drag uses its raw captured pixel without another patch");
            int oldClicks = clicks; button.interactable = false; flat.ClickFixture(a);
            Check(clicks == oldClicks, "original disabled native button permission remains unchanged"); button.interactable = true;
            raycaster.enabled = false; flat.ClickFixture(a);
            Check(clicks == oldClicks, "original disabled native raycaster remains unreachable"); raycaster.enabled = true;
            NativeMouseDisplay.main.renderingWidth = NativeMouseDisplay.main.systemWidth;
            NativeMouseDisplay.main.renderingHeight = NativeMouseDisplay.main.systemHeight; NativeMouseScreen.fullScreen = true;
            if (retainedScrollFixture)
            {
                // The skip-teardown control qualifies only when the original card really
                // intercepts this exact final query; a setup error has a different oracle.
                var retainedHits = CapturedHits(es, a);
                Check(retainedHits.Count > 0 && retainedHits[0].gameObject == card.gameObject,
                    "retained scroll control has the full card as the final click's actual top hit");
            }
            finalNativeCallsBefore = NativeMouse760.Calls;
            flat.ClickFixture(a);
            finalNativeCallsAfter = NativeMouse760.Calls;
            Check(clicks == oldClicks + 1, "fullscreen captured native click retains ordinary behavior");
            var nativeRaycast = AccessTools.Method(typeof(EventSystem), nameof(EventSystem.RaycastAll), new[] { typeof(PointerEventData), typeof(List<RaycastResult>) });
            var originalCall = new CodeInstruction(OpCodes.Callvirt, nativeRaycast);
            var unsafePrefix = new CodeInstruction(OpCodes.Tailcall);
            var refused = new List<CodeInstruction>(PointerInputModuleExtended_CapturedCoordinates.Transpiler(new[] { unsafePrefix, originalCall }));
            Check(refused.Count == 2 && ReferenceEquals(refused[0], unsafePrefix) && ReferenceEquals(refused[1], originalCall),
                "unsupported original mouse call prefix falls back intact");
            InputSystem.RemoveDevice(mouse);
            Check(!VirtualMouse.OwnsProcessedPosition(a), "removed virtual device cannot leave a live ownership claim"); mouse = null;
            report(checks);
        }
        finally
        {
            failureWitness = null;
            legacy?.Stop(); harmony.UnpatchAll(harmony.Id); FlatScreen.ClearFixture();
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (physical != null && physical.added) InputSystem.RemoveDevice(physical);
            if (target != null) Object.DestroyImmediate(target); if (foreign != null) Object.DestroyImmediate(foreign);
        }
    }
}
