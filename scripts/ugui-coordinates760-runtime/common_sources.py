"""Source-bound captured-screen and native-mouse caller kernels for the common proof."""
import re
from pathlib import Path


def generated(root, capture_root, block):
    base = root / 'src/GloomhavenVR'
    pointer_path = base / 'Hands/Interact/UguiPointer.cs'
    flat_path = base / 'WorldUI/FlatScreen/FlatScreen.6.Pointer.cs'
    mouse_path = base / 'WorldUI/FlatScreen/VirtualMouseBridge.cs'
    native_path = base / 'Hands/Interact/NativeMouseRaycastCoordinates.cs'
    capture_path = capture_root / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.1.Core.cs'
    stack_path = capture_root / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.2.CameraStack.cs'
    focus_path = base / 'Hands/Interact/UiScrollFocus.cs'
    pointer = pointer_path.read_text(); flat = flat_path.read_text(); mouse = mouse_path.read_text()
    capture = capture_path.read_text(); stack = stack_path.read_text()
    actual_mouse = '''using UnityEngine;using UnityEngine.InputSystem;using UnityEngine.InputSystem.LowLevel;using GloomhavenVR.Core;
namespace GloomhavenVR.WorldUI {internal static class VirtualMouse {
private static Mouse? _mouse;private static MouseState _shadow;private static bool _parked;private static int _lastDriveFrame=-1;
internal static void BindFixture(Mouse mouse,bool parked=false,int age=0){_mouse=mouse;_parked=parked;_lastDriveFrame=Time.frameCount-age;}
internal static void ShadowFixture(Vector2 point){_shadow.position=point;}
'''
    for name in ['ParkAfterFrames', 'ParkPixel']:
        actual_mouse += re.search(r'^    (?:private const int|internal static readonly Vector2) ' + name + r' = [^;]+;', mouse, re.M)[0] + '\n'
    actual_mouse += block(mouse, 'internal static bool OwnsProcessedPosition(Vector2 position)') + '}}'
    actual_flat = '''using System.Collections.Generic;using UnityEngine;using UnityEngine.EventSystems;using UnityEngine.UI;
using GloomhavenVR.Core;using GloomhavenVR.Hands;using GloomhavenVR.Hands.Interact;
namespace GloomhavenVR.WorldUI {internal sealed partial class FlatScreen {
private sealed class CapturedCamera {internal bool IsUi=true;}
private static FlatScreen? _coverageOwner;private bool _visible=true,_scrubActive;
private RenderTexture? _rt,_uiRt,_scrubRt;
private readonly List<Camera> _scrubbed=new();private static readonly Dictionary<Camera,CapturedCamera> CapturedSet=new();
private static readonly List<RaycastResult> s_raycastResults=new(16);private static readonly List<Component> s_scrollHandlers=new(4);
private PointerEventData? _screenDragData,_hitTestData,_stickScrollData;private EventSystem? _hitTestEventSystem,_stickScrollEventSystem;
private GameObject? _screenDragPress,_screenDragTarget,_lastStickScrollTarget;
private bool _screenDragActive,_screenDragBegun,_mapPanGesture;private Vector2 _screenDragLastPos;
private float _lastScreenDragLog=float.NegativeInfinity,_lastStickScrollLog=float.NegativeInfinity,_lastStickScrollMissLog=float.NegativeInfinity;
internal void BindFixture(Camera camera,RenderTexture target){_coverageOwner=this;_rt=target;CapturedSet.Clear();CapturedSet[camera]=new CapturedCamera();}
internal void VisibleFixture(bool visible){_visible=visible;}
internal void ClickFixture(Vector2 point)=>DirectClick(point);
internal void ScrollFixture(Vector2 point,VRHand hand)=>TickStickScroll(point,hand,false);
internal string ScrollWitnessFixture()=>_stickScrollData?.pointerCurrentRaycast.gameObject?.name??"no original hit";
internal bool DragHandlerFixture(Vector2 point)=>PointerOverUiHandler<IDragHandler>(point);
internal void BeginFixture(Vector2 point)=>BeginScreenDrag(point);
internal void MoveFixture(Vector2 point)=>UpdateScreenDrag(point);
internal void EndFixture()=>EndScreenDrag();
internal static void ClearFixture(){CapturedSet.Clear();_coverageOwner=null;}
'''
    actual_flat += block(capture, 'internal static bool OwnsNativeUiPixels(Canvas? canvas, Camera? camera)') + '\n'
    start = stack.index('private static bool IsUiCamera(')
    actual_flat += stack[start:stack.index(';', start) + 1] + '\n'
    for text, name, kind in [(capture, 'ScreenDragPointerId', 'int'), (capture, 'ScreenDragLogSeconds', 'float'),
        (flat, 'StickScrollDeadzone', 'float'), (flat, 'StickScrollNotchesPerSecond', 'float'),
        (flat, 'StickScrollPointerId', 'int'), (flat, 'StickScrollLogSeconds', 'float'), (flat, 'HitTestPointerId', 'int')]:
        actual_flat += re.search(r'^    private const ' + kind + ' ' + name + r' = [^;]+;', text, re.M)[0] + '\n'
    for signature in ['private void DirectClick(Vector2 pixel)', 'private bool PointerOverUiHandler<T>(Vector2 pixel)',
        'private void TickStickScroll(Vector2 pixel, VRHand hand, bool mapActive)', 'private void BeginScreenDrag(Vector2 pixel)',
        'private void UpdateScreenDrag(Vector2 pixel)', 'private void EndScreenDrag()', 'private static void LogUnderPointer(Vector2 pixel)']:
        actual_flat += block(flat, signature) + '\n'
    actual_flat += '}}'
    shared_scroll = ('using System.Collections.Generic;using UnityEngine;using UnityEngine.EventSystems;namespace GloomhavenVR.Hands.Interact {'
        'internal static class UguiPointer {internal static bool ShouldWithholdUnstartedWindowClick(GameObject target,string tag)=>false;'
        + block(pointer, 'internal static GameObject? FindScrollTarget(GameObject start, List<Component> handlers)') + '}}')
    boundary = '''using System.Collections.Generic;using UnityEngine;
namespace GloomhavenVR.Core {
internal static class VRSession {internal static bool IsRunning=true;}
internal static class VRLog {internal static readonly List<string> Warnings=new(),DebugLines=new();internal static bool WantsDebug=>false;
internal static void Debug(string scope,string text)=>DebugLines.Add(text);internal static void Warn(string scope,string text)=>Warnings.Add(text);internal static void Info(string scope,string text)=>DebugLines.Add(text);}}
namespace GloomhavenVR.WorldUI {internal static class VRKeyboard {internal static void NoticeClick(GameObject? target){}}}
namespace GloomhavenVR.WorldUI {internal static class CanvasConversion {internal static bool TryGetHitRect(Canvas? canvas,out Rect rect){rect=default;return false;}}}
namespace GloomhavenVR.Hands {internal enum HandSide {Left,Right}internal sealed class VRHand {internal HandSide Side;internal Vector2 Thumbstick;}}
internal sealed class NativeMouseMetrics {internal int renderingWidth,renderingHeight,systemWidth,systemHeight;}
internal static class NativeMouseDisplay {internal static readonly NativeMouseMetrics main=new();internal static Vector3 RelativeMouseAt(Vector2 point)=>new(point.x,point.y,0);}
internal static class NativeMouseScreen {internal static bool fullScreen;}
'''
    sources = {'ActualMouseOwner.cs': actual_mouse, 'ActualFlatPointer.cs': actual_flat,
        'NativeMouseRaycastCoordinates.cs': native_path.read_text(), 'UiScrollFocus.cs': focus_path.read_text(),
        'SharedScroll.cs': shared_scroll, 'Boundary.cs': boundary}
    cases = [
        ('production', '', '', '', ''),
        ('lost-captured-context', 'UguiRaycastCoordinates.cs', '_expected.Value.Captured && WorldUI.FlatScreen.OwnsNativeUiPixels',
         'false && _expected.Value.Captured && WorldUI.FlatScreen.OwnsNativeUiPixels', 'explicit captured-screen click reaches its original button immediately'),
        ('lost-native-mouse-context', 'NativeMouseRaycastCoordinates.cs', 'if (VRSession.IsRunning && system != null',
         'if (false && VRSession.IsRunning && system != null', 'actual original native mouse module hovers the same button immediately'),
        ('queued-instead-of-processed', 'ActualMouseOwner.cs', '_mouse.position.ReadValue() == position',
         '_shadow.position == position', 'processed mouse position remains valid while the next position is still queued'),
        ('wrong-current-device', 'ActualMouseOwner.cs', '!ReferenceEquals(Mouse.current, _mouse) || ', '',
         'physical mouse takeover never inherits virtual screen coordinates'),
        ('ignore-legacy-provider', 'ActualMouseOwner.cs', ' || Utilities.InputSystemUtilities.GetCurrentVirtualMouse() != null', '',
         'original registered legacy mouse provider retains its own coordinate path'),
        ('ignore-capture-lifetime', 'ActualFlatPointer.cs', 'if (!VRSession.IsRunning || owner == null', 'if (false && !VRSession.IsRunning || owner == null',
         'stopped VR never claims a captured native input domain'),
        ('old-flat-card-wheel', 'SharedScroll.cs', 'if (!handlesScroll)', 'if (!handlesScroll && handlers.Count < 0)',
         'original flat starter-card list scrolls while a pointer-only adapter is hovered'),
        ('split-native-call-prefix', 'NativeMouseRaycastCoordinates.cs', '|| unsupportedBoundary)', '|| false && unsupportedBoundary)',
         'unsupported original mouse call prefix falls back intact'),
    ]
    return sources, cases, [pointer_path, flat_path, mouse_path, native_path, capture_path, stack_path, focus_path]
