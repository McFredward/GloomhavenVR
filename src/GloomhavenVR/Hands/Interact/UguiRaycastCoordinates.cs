using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Our pointer coordinates already belong to the original event camera, rather
/// than the operating system's desktop mouse. Windowed multi-display remapping
/// must not offset them a second time. Only the exact scoped raycaster and event
/// data take this route; other pointers and nested native calls keep Unity's API.
/// </summary>
internal static class UguiRaycastCoordinates
{
    internal static readonly MethodInfo? Original = FindOriginal();
    private static readonly Func<Vector2, Vector3>? Native = BindOriginal();
    internal static bool Available => Original != null && Native != null;
    private static readonly Dictionary<int, ReceiptConfiguration> Receipts = new(4);
    private static float _nextReceiptAt;
    private static int _receiptCount;

    private readonly struct ReceiptConfiguration
    {
        private readonly int _width, _height, _renderWidth, _renderHeight, _systemWidth, _systemHeight, _target;
        private readonly FullScreenMode _mode;
        private readonly Rect _pixels;
        private readonly float _aspect;
        internal ReceiptConfiguration(Camera? camera)
        {
            _width = Screen.width; _height = Screen.height; _mode = Screen.fullScreenMode;
            _renderWidth = Display.main.renderingWidth; _renderHeight = Display.main.renderingHeight;
            _systemWidth = Display.main.systemWidth; _systemHeight = Display.main.systemHeight;
            _pixels = camera != null ? camera.pixelRect : default; _aspect = camera != null ? camera.aspect : 0f;
            _target = camera != null && camera.targetTexture != null ? camera.targetTexture.GetInstanceID() : 0;
        }
        internal bool Same(in ReceiptConfiguration other) => _width == other._width && _height == other._height
            && _mode == other._mode && _renderWidth == other._renderWidth && _renderHeight == other._renderHeight
            && _systemWidth == other._systemWidth && _systemHeight == other._systemHeight
            && _pixels == other._pixels && _aspect == other._aspect && _target == other._target;
    }

    private readonly struct Expected
    {
        internal readonly GraphicRaycaster? Raycaster;
        internal readonly PointerEventData Data;
        internal readonly bool Captured;
        internal readonly Canvas? AdmittedHost;
        internal Expected(GraphicRaycaster? raycaster, PointerEventData data, bool captured, Canvas? admittedHost)
        { Raycaster = raycaster; Data = data; Captured = captured; AdmittedHost = admittedHost; }
    }
    [ThreadStatic] private static Expected? _expected;

    internal readonly struct Lease : IDisposable
    {
        private readonly Expected? _previous;
        internal Lease(GraphicRaycaster? raycaster, PointerEventData data, bool captured, Canvas? admittedHost)
        { _previous = _expected; _expected = new Expected(raycaster, data, captured, admittedHost); }
        public void Dispose() => _expected = _previous;
    }

    internal static Lease Enter(GraphicRaycaster raycaster, PointerEventData data, Canvas? admittedHost = null) => new(raycaster, data, false, admittedHost);
    internal static Lease EnterCaptured(PointerEventData data) => new(null, data, true, null);

    private static MethodInfo? FindOriginal()
    {
        try
        {
            return typeof(GraphicRaycaster).Assembly.GetType("UnityEngine.UI.MultipleDisplayUtilities")?
                .GetMethod("RelativeMouseAtScaled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Vector2) }, null);
        }
        catch (Exception error)
        {
            VRLog.Warn("Interact", "SYNTHETIC UGUI COORDINATES: native display signature unavailable; keeping original input. " + error.GetType().Name);
            return null;
        }
    }

    private static Func<Vector2, Vector3>? BindOriginal()
    {
        try { return Original == null ? null : (Func<Vector2, Vector3>)Delegate.CreateDelegate(typeof(Func<Vector2, Vector3>), Original); }
        catch (Exception error)
        {
            VRLog.Warn("Interact", "SYNTHETIC UGUI COORDINATES: native display utility unavailable; keeping original input. " + error.GetType().Name);
            return null;
        }
    }

    internal static Vector3 RelativeMouseAtScaled(Vector2 position, GraphicRaycaster raycaster, PointerEventData data)
    {
        if (_expected.HasValue && ReferenceEquals(_expected.Value.Data, data)
            && (ReferenceEquals(_expected.Value.Raycaster, raycaster)
                || _expected.Value.Captured && WorldUI.FlatScreen.OwnsNativeUiPixels(raycaster.GetComponent<Canvas>(), raycaster.eventCamera)))
        {
            Canvas canvas = raycaster.GetComponent<Canvas>();
            Camera camera = raycaster.eventCamera;
            int display = canvas.renderMode != RenderMode.ScreenSpaceOverlay && camera != null
                ? camera.targetDisplay : canvas.targetDisplay;
            NoteConfiguration(canvas, camera, position, display);
            return new Vector3(position.x, position.y, display);
        }
        // Installed only after binding this exact original delegate succeeds.
        // Native exceptions propagate without reflection wrapping or replacement.
        return Native!(position);
    }

    /// <summary>For an admitted physical WORLD pane, desktop-mono screen bounds
    /// are not its interaction bounds. Only the exact pointer/caster may use the
    /// original admitted host's measured frame. Native/captured and remapped
    /// raised surfaces retain their original viewport classification.</summary>
    internal static Vector3 Viewport(Vector3 original, Vector3 screen, Camera camera,
        GraphicRaycaster raycaster, PointerEventData data)
    {
        if (!_expected.HasValue || _expected.Value.Captured
            || !ReferenceEquals(_expected.Value.Raycaster, raycaster) || !ReferenceEquals(_expected.Value.Data, data))
            return original;
        Canvas? host = _expected.Value.AdmittedHost;
        if (host == null || host.renderMode != RenderMode.WorldSpace || !host.isActiveAndEnabled
            || !ReferenceEquals(host.worldCamera != null ? host.worldCamera : Camera.main, camera)
            || !Finite(original) || !Finite(screen))
            return original;
        var rectangle = host.transform as RectTransform;
        if (rectangle == null) return original;
        Rect bounds = WorldUI.CanvasConversion.TryGetHitRect(host, out Rect committed) ? committed : rectangle.rect;
        if (!Finite(bounds.xMin) || !Finite(bounds.xMax) || !Finite(bounds.yMin) || !Finite(bounds.yMax)
            || !Finite(bounds.width) || !Finite(bounds.height) || bounds.width <= 0f || bounds.height <= 0f)
            return original;
        Ray ray = camera.ScreenPointToRay(screen);
        Vector3 forward = camera.transform.forward;
        if (!Finite(ray.origin) || !Finite(ray.direction) || !Finite(forward) || Vector3.Dot(ray.direction, forward) <= 0f)
            return original;
        Vector3 origin = rectangle.InverseTransformPoint(ray.origin);
        Vector3 direction = rectangle.InverseTransformVector(ray.direction);
        if (!Finite(origin) || !Finite(direction) || direction.z == 0f) return original;
        double distance = -(double)origin.z / direction.z;
        if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0d) return original;
        double x = origin.x + direction.x * distance, y = origin.y + direction.y * distance;
        if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y)
            || x < bounds.xMin || x > bounds.xMax || y < bounds.yMin || y > bounds.yMax)
            return original;
        return new Vector3((float)((x - bounds.xMin) / bounds.width), (float)((y - bounds.yMin) / bounds.height), original.z);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);

    /// <summary>Bounded configuration receipts, not a pointer/hover stream.</summary>
    private static void NoteConfiguration(Canvas canvas, Camera? camera, Vector2 position, int display)
    {
        if (!VRLog.WantsDebug || _receiptCount >= 12 || Time.unscaledTime < _nextReceiptAt)
            return;
        _nextReceiptAt = Time.unscaledTime + .5f;
        bool counted = false;
        try
        {
            int id = camera != null ? camera.GetInstanceID() : 0;
            var configuration = new ReceiptConfiguration(camera);
            if (Receipts.TryGetValue(id, out ReceiptConfiguration previous) && previous.Same(in configuration))
                return;
            Receipts[id] = configuration;
            _receiptCount++;
            counted = true;
            Vector3 native = Native!(position);
            string projection = "no event camera";
            if (camera != null)
            {
                bool plane = RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)canvas.transform,
                    position, camera, out Vector3 world);
                Vector3 mono = plane ? camera.WorldToViewportPoint(world) : default;
                string eyes = camera.stereoEnabled && plane
                    ? $"left={Inside(camera.WorldToViewportPoint(world, Camera.MonoOrStereoscopicEye.Left))}, "
                      + $"right={Inside(camera.WorldToViewportPoint(world, Camera.MonoOrStereoscopicEye.Right))}"
                    : "stereo unavailable";
                projection = $"camera='{camera.name}' target={(camera.targetTexture != null ? camera.targetTexture.name : "backbuffer")} "
                    + $"pixelRect={camera.pixelRect} aspect={camera.aspect:F3} canvas-plane={plane} monoVP={mono:F3} monoInside={plane && Inside(mono)} {eyes}";
            }
            VRLog.Debug("Interact", $"UGUI COORDINATE RECEIPT {_receiptCount}/12: raw={position:F1} native={native:F1} retainedDisplay={display}; "
                + $"window={Screen.width}x{Screen.height} mode={Screen.fullScreenMode} displayRender={Display.main.renderingWidth}x{Display.main.renderingHeight} "
                + $"displaySystem={Display.main.systemWidth}x{Display.main.systemHeight}; {projection}. Native sample is diagnostic only; camera/render state is unchanged.");
        }
        catch (Exception error)
        {
            if (!counted) _receiptCount++;
            VRLog.Debug("Interact", "UGUI COORDINATE RECEIPT: native display sample unavailable: " + error.GetType().Name);
        }
    }

    private static bool Inside(Vector3 viewport) => viewport.z >= 0f && viewport.x >= 0f && viewport.x <= 1f
        && viewport.y >= 0f && viewport.y <= 1f;
}

/// <summary>Keep native permission/graphic filters and callbacks; classify only
/// admitted physical panes against their own frame instead of desktop bounds.</summary>
[HarmonyPatch(typeof(GraphicRaycaster), nameof(GraphicRaycaster.Raycast), new[] { typeof(PointerEventData), typeof(List<RaycastResult>) })]
internal static class GraphicRaycaster_SyntheticCoordinates
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = new List<CodeInstruction>(instructions);
        MethodInfo? original = UguiRaycastCoordinates.Original;
        MethodInfo? viewport = AccessTools.Method(typeof(Camera), nameof(Camera.ScreenToViewportPoint), new[] { typeof(Vector3) });
        int count = 0, viewportCount = 0;
        bool unsupportedBoundary = false;
        for (int i = 0; i < code.Count; i++)
        {
            bool displayCall = original != null && code[i].Calls(original);
            bool viewportCall = viewport != null && code[i].Calls(viewport);
            if (!displayCall && !viewportCall) continue;
            if (displayCall) count++;
            if (viewportCall) viewportCount++;
            unsupportedBoundary |= code[i].blocks.Count != 0 || i > 0 && code[i - 1].opcode.OpCodeType == OpCodeType.Prefix;
        }
        if (!UguiRaycastCoordinates.Available || count != 1 || viewportCount != 1 || unsupportedBoundary)
        {
            VRLog.Warn("Interact", "SYNTHETIC UGUI COORDINATES: original raycaster display seam changed; keeping original input.");
            return code;
        }
        MethodInfo replacement = AccessTools.Method(typeof(UguiRaycastCoordinates), nameof(UguiRaycastCoordinates.RelativeMouseAtScaled));
        MethodInfo viewportReplacement = AccessTools.Method(typeof(UguiRaycastCoordinates), nameof(UguiRaycastCoordinates.Viewport));
        LocalBuilder screenArgument = generator.DeclareLocal(typeof(Vector3));
        LocalBuilder cameraArgument = generator.DeclareLocal(typeof(Camera));
        var result = new List<CodeInstruction>(code.Count + 11);
        foreach (CodeInstruction instruction in code)
        {
            if (instruction.Calls(original))
            {
                var owner = new CodeInstruction(OpCodes.Ldarg_0);
                owner.labels.AddRange(instruction.labels);
                result.Add(owner); result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                result.Add(new CodeInstruction(OpCodes.Call, replacement));
            }
            else if (instruction.Calls(viewport))
            {
                var capture = new CodeInstruction(OpCodes.Stloc, screenArgument); capture.labels.AddRange(instruction.labels);
                result.Add(capture); result.Add(new CodeInstruction(OpCodes.Dup));
                result.Add(new CodeInstruction(OpCodes.Stloc, cameraArgument)); result.Add(new CodeInstruction(OpCodes.Ldloc, screenArgument));
                result.Add(new CodeInstruction(instruction.opcode, instruction.operand));
                result.Add(new CodeInstruction(OpCodes.Ldloc, screenArgument)); result.Add(new CodeInstruction(OpCodes.Ldloc, cameraArgument));
                result.Add(new CodeInstruction(OpCodes.Ldarg_0)); result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                result.Add(new CodeInstruction(OpCodes.Call, viewportReplacement));
            }
            else result.Add(instruction);
        }
        return result;
    }
}
