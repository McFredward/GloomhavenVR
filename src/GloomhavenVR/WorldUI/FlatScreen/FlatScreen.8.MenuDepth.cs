using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MenuDepth;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private readonly List<Graphic> _menuDepthGraphics = new(32);
    private readonly List<Graphic> _menuDepthBoundsGraphics = new(4);
    private readonly List<Camera> _menuDepthCameras = new(8);
    private RenderTexture? _menuDepthRight, _menuDepthLeft, _menuDepthCarrierLeft;
    private MenuDepthDesktopCarrier? _menuDepthCarrier;
    private Rect _menuDepthFrameUv = new(0f, 0f, 1f, 1f);
    private float _menuDepthShrinkSince = -1f;
    private Vector2 _menuDepthPhysicalScale;
    private Camera? _menuDepthBorrowedCamera;
    private Predicate<Graphic>? _menuDepthApplies;
    private Func<Graphic, MenuDepthEye>? _menuDepthMapper;
    private Func<Graphic, Vector2>? _menuDepthPixels;
    private MenuDepthEye _menuDepthEye;
    private bool _menuDepthHooked;
    private int _menuDepthFrame = -1, _menuDepthFailures;

    private void SetMenuDepthGuard(bool active)
    {
        if (_menuDepthHooked == active) return;
        _menuDepthHooked = active;
        if (active)
        {
            _menuDepthApplies ??= MenuDepthApplies;
            _menuDepthMapper ??= MapMenuDepthEye;
            _menuDepthPixels ??= MenuDepthPixels;
            MenuDepthSources.BeforeHeadCapture += CaptureMenuDepthScreen;
            Camera.onPreRender += BeforeMenuDepthHead;
            Camera.onPostRender += AfterMenuDepthHead;
        }
        else
        {
            MenuDepthSources.BeforeHeadCapture -= CaptureMenuDepthScreen;
            Camera.onPreRender -= BeforeMenuDepthHead;
            Camera.onPostRender -= AfterMenuDepthHead;
            ReleaseMenuDepthScreen();
            MenuDepthNativeUiScope.Clear();
        }
    }

    // The registry can become empty and remove its head callback. Ordinary screen
    // ticks still release the old right image immediately instead of freezing it.
    private void TickMenuDepthScreen()
    {
        if (_menuDepthRight != null && (!HasNativeMenuDepth() || !_splitRouting
            || _menuDepthLeft != _uiRt || _uiRt == null)) ReleaseMenuDepthScreen();
    }

    private static Camera? NativeUiCamera(Graphic graphic)
    {
        Canvas canvas = graphic.canvas;
        if (canvas == null || canvas.rootCanvas == null || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceCamera) return null;
        return canvas.worldCamera != null ? canvas.worldCamera : canvas.rootCanvas.worldCamera;
    }

    private bool HasNativeMenuDepth()
    {
        MenuDepthSources.CollectReadyGraphics(_menuDepthGraphics);
        for (int i = 0; i < _menuDepthGraphics.Count; i++)
        {
            Camera? camera = NativeUiCamera(_menuDepthGraphics[i]);
            if (camera != null && camera.isActiveAndEnabled && CapturedSet.TryGetValue(camera, out CapturedCamera captured)
                && captured.IsUi) return true;
        }
        return false;
    }

    private static RenderTexture MenuDepthTarget(RenderTexture original, int width, int height, string eye)
    {
        var descriptor = original.descriptor; descriptor.width = width; descriptor.height = height;
        var target = new RenderTexture(descriptor) { name = "GloomhavenVR.FlatScreenRT.MenuDepth" + eye,
            hideFlags = HideFlags.HideAndDontSave, filterMode = original.filterMode, wrapMode = TextureWrapMode.Clamp };
        if (target.Create()) return target;
        UnityEngine.Object.Destroy(target); throw new InvalidOperationException("Native menu eye target was refused.");
    }

    private float DepthSide(Vector3 eye) => _quad != null
        && Vector3.Dot(eye - _quad.transform.position, _quad.transform.forward) > 0f ? -1f : 1f;

    private Rect MenuDepthCaptureFrame(Camera head, Vector3 left, Vector3 right)
    {
        Vector2 min = Vector2.zero, max = Vector2.one;
        MenuDepthSources.CollectReadyForegroundGraphics(_menuDepthBoundsGraphics);
        for (int i = 0; i < _menuDepthBoundsGraphics.Count; i++)
        {
            Graphic graphic = _menuDepthBoundsGraphics[i]; Camera? camera = NativeUiCamera(graphic);
            if (camera == null || !CapturedSet.TryGetValue(camera, out CapturedCamera captured) || !captured.IsUi) continue;
            for (int side = 0; side < 2; side++)
            {
                if (!MenuDepthScreenProjection.TryMap(_quad!.transform, camera, graphic.rectTransform,
                    side == 0 ? left : right, out Vector3 eye)) continue;
                if (!MenuDepthSources.TryGetForegroundBounds(graphic, new MenuDepthEye(side != 0, eye, head, true), out Rect bounds)) continue;
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = graphic.rectTransform.TransformPoint(new Vector3((corner & 1) == 0 ? bounds.xMin : bounds.xMax,
                        (corner & 2) == 0 ? bounds.yMin : bounds.yMax));
                    Vector3 viewport = camera.WorldToViewportPoint(point);
                    if (!MenuDepthProjection.Finite(viewport)) throw new InvalidOperationException("The complete foreground raster is not finite.");
                    Vector2 uv = new(camera.rect.x + viewport.x * camera.rect.width, camera.rect.y + viewport.y * camera.rect.height);
                    min = Vector2.Min(min, uv); max = Vector2.Max(max, uv);
                }
            }
        }
        // Stable pixel pages avoid allocation churn at a moving silhouette edge.
        float x0 = min.x < 0 ? -Mathf.Ceil((-min.x * _uiRt!.width + 2f) / 64f) * 64f / _uiRt.width : 0f;
        float y0 = min.y < 0 ? -Mathf.Ceil((-min.y * _uiRt!.height + 2f) / 64f) * 64f / _uiRt.height : 0f;
        float x1 = max.x > 1 ? 1f + Mathf.Ceil(((max.x - 1f) * _uiRt!.width + 2f) / 64f) * 64f / _uiRt.width : 1f;
        float y1 = max.y > 1 ? 1f + Mathf.Ceil(((max.y - 1f) * _uiRt!.height + 2f) / 64f) * 64f / _uiRt.height : 1f;
        Rect requested = Rect.MinMaxRect(x0,y0,x1,y1);
        if (_menuDepthRight != null)
            requested = MenuDepthDesktopResources.RetainFrame(_menuDepthFrameUv, requested, Time.unscaledTime, ref _menuDepthShrinkSince);
        return requested;
    }

    private void CaptureMenuDepthScreen(Camera head)
    {
        if (head != Rig.VRRigDriver.HeadCamera) return;
        if (_menuDepthFrame == Time.frameCount) { BeforeMenuDepthHead(head); return; }
        _menuDepthFrame = Time.frameCount;
        if (!_visible || !_splitRouting || _uiRt == null || _quad == null || _glassMaterial == null
            || !HasNativeMenuDepth() || !MenuDepthDisplay.TryGetEyePositions(head, out Vector3 left, out Vector3 right))
        { ReleaseMenuDepthScreen(); return; }
        try
        {
            Rect frame = MenuDepthCaptureFrame(head, left, right);
            int width = Mathf.RoundToInt(frame.width * _uiRt.width), height = Mathf.RoundToInt(frame.height * _uiRt.height);
            if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
                throw new InvalidOperationException("The complete native foreground exceeds the graphics texture limit.");
            bool expanded = frame != new Rect(0f, 0f, 1f, 1f);
            if (!MenuDepthDesktopResources.Fits(_uiRt.descriptor, width, height, expanded))
                throw new InvalidOperationException("The complete native foreground exceeds its additional eye-target memory budget.");
            if (_menuDepthRight == null || _menuDepthLeft != _uiRt
                || _menuDepthRight.width != width || _menuDepthRight.height != height
                || expanded != (_menuDepthCarrierLeft != null))
            {
                ReleaseMenuDepthScreen();
                _menuDepthRight = MenuDepthTarget(_uiRt, width, height, "Right");
                if (expanded) _menuDepthCarrierLeft = MenuDepthTarget(_uiRt, width, height, "Left");
                _menuDepthLeft = _uiRt;
            }
            _menuDepthFrameUv = frame;
            float physical = 2f * Mathf.Sqrt(_quad.transform.TransformVector(Vector3.right).magnitude
                * _quad.transform.TransformVector(Vector3.up).magnitude);
            if (SystemInfo.usesReversedZBuffer) physical = -physical;
            _menuDepthPhysicalScale = new Vector2(DepthSide(left) * physical, DepthSide(right) * physical);
            _menuDepthCameras.Clear();
            for (int i = 0; i < _captured.Count; i++)
            {
                CapturedCamera source = _captured[i];
                if (!source.IsUi || source.Camera == null || !source.Camera.isActiveAndEnabled) continue;
                int at = _menuDepthCameras.Count;
                while (at > 0 && _menuDepthCameras[at - 1].depth > source.Camera.depth) at--;
                _menuDepthCameras.Insert(at, source.Camera);
            }
            CaptureMenuDepthUi(_menuDepthCarrierLeft ?? _uiRt, new MenuDepthEye(false, left, head));
            CaptureMenuDepthUi(_menuDepthRight!, new MenuDepthEye(true, right, head));
            _menuDepthCarrier ??= new MenuDepthDesktopCarrier(_quad.transform, _glassMaterial,
                _menuDepthCarrierLeft ?? _uiRt, _menuDepthRight!);
            // The visual replacement must exist before head culling. PreRender
            // still selects the actual eye, but cannot admit an inactive mesh.
            BeforeMenuDepthHead(head);
        }
        catch (Exception error)
        {
            ReleaseMenuDepthScreen();
            int count = ++_menuDepthFailures;
            if (count <= 2 || (count & (count - 1)) == 0)
                VRLog.Warn("WorldUI", "MENU DEPTH DESKTOP FALLBACK: " + error.GetType().Name + ": " + error.Message
                    + "; count=" + count + "; original menu input and continuation retained.");
            // A left-eye preparation could already have drawn. Recover the exact
            // original mono UI after every source lease has been restored.
            if (_uiRt != null)
                for (int i = 0; i < _menuDepthCameras.Count; i++)
                {
                    Camera source = _menuDepthCameras[i];
                    if (source == null) continue;
                    try { Canvas.ForceUpdateCanvases(); source.Render(); }
                    catch (Exception fallback) { if (count <= 2) VRLog.Warn("WorldUI", "MENU DEPTH DESKTOP RESTORE: " + fallback.GetType().Name); }
                }
        }
    }

    private void CaptureMenuDepthUi(RenderTexture target, MenuDepthEye eye)
    {
        RenderTexture? active = RenderTexture.active;
        try
        {
            RenderTexture.active = target; GL.Clear(true, true, Color.clear);
            RestoreCaptureMask();
            _menuDepthEye = eye;
            for (int i = 0; i < _menuDepthCameras.Count; i++)
            {
                Camera source = _menuDepthCameras[i];
                if (source == null || !source.isActiveAndEnabled) continue;
                RenderTexture? originalTarget = source.targetTexture;
                int originalMask = source.cullingMask;
                _menuDepthBorrowedCamera = source;
                MenuDepthNativeUiScope? depthScope = null;
                try
                {
                    bool rear = _quad != null && Vector3.Dot(eye.WorldPosition - _quad.transform.position, _quad.transform.forward) > 0f;
                    depthScope = new MenuDepthNativeUiScope(source, MenuDepthNativeUiScope.FindCanvas(source, _menuDepthGraphics), rear);
                    MenuDepthSources.BeforeCaptureWhere(eye, _menuDepthApplies!, _menuDepthMapper!, _menuDepthPixels!);
                    if (VRLayers.ModLayerMask != VRLayers.GameUiLayerMask) source.cullingMask &= ~VRLayers.ModLayerMask;
                    if (MenuDepthSources.HasPreparedSources) Canvas.ForceUpdateCanvases();
                    depthScope.ExpandCapture(target, _menuDepthFrameUv, _menuDepthGraphics);
                    source.Render();
                }
                finally
                {
                    try { MenuDepthSources.AfterCapture(); }
                    finally
                    {
                        try { depthScope?.Dispose(); }
                        finally
                        {
                            if (source != null) { source.targetTexture = originalTarget; source.cullingMask = originalMask; }
                            _menuDepthBorrowedCamera = null;
                        }
                    }
                }
            }
        }
        finally { _menuDepthBorrowedCamera = null; RenderTexture.active = active; }
    }

    private bool MenuDepthApplies(Graphic graphic) => NativeUiCamera(graphic) == _menuDepthBorrowedCamera;

    private Vector2 MenuDepthPixels(Graphic graphic)
        => _quad != null && _menuDepthBorrowedCamera != null
            ? MenuDepthScreenProjection.PixelFootprint(_quad.transform, _menuDepthBorrowedCamera,
                graphic.rectTransform, _menuDepthEye) : Vector2.zero;

    private MenuDepthEye MapMenuDepthEye(Graphic graphic)
    {
        if (_quad == null || _menuDepthBorrowedCamera == null
            || !MenuDepthScreenProjection.TryMap(_quad.transform, _menuDepthBorrowedCamera, graphic.rectTransform,
                _menuDepthEye.WorldPosition, out Vector3 position))
            throw new InvalidOperationException("The original menu graphic has no valid physical eye mapping.");
        return new MenuDepthEye(_menuDepthEye.Right, position, _menuDepthEye.HeadCamera, true);
    }

    private void BeforeMenuDepthHead(Camera camera)
    {
        if (camera != Rig.VRRigDriver.HeadCamera || _menuDepthRight == null || _glassMaterial == null || _uiRt == null) return;
        _glassMaterial.mainTexture = _menuDepthFrame == Time.frameCount
            && camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? _menuDepthRight : _menuDepthCarrierLeft ?? _uiRt;
        bool right = camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right;
        if (_menuDepthFrame == Time.frameCount && _menuDepthCarrier != null)
            _menuDepthCarrier.Draw((RenderTexture)_glassMaterial.mainTexture, _menuDepthFrameUv,
                right ? _menuDepthPhysicalScale.y : _menuDepthPhysicalScale.x);
    }
    private void AfterMenuDepthHead(Camera camera)
    {
        if (camera != Rig.VRRigDriver.HeadCamera) return;
        _menuDepthCarrier?.EndDraw();
        if (_glassMaterial != null && _uiRt != null) _glassMaterial.mainTexture = _uiRt;
    }
    private void ReleaseMenuDepthScreen()
    {
        _menuDepthCarrier?.Dispose(); _menuDepthCarrier = null;
        if (_glassMaterial != null) _glassMaterial.mainTexture = _uiRt;
        if (_menuDepthCarrierLeft != null) { _menuDepthCarrierLeft.Release(); UnityEngine.Object.Destroy(_menuDepthCarrierLeft); }
        _menuDepthCarrierLeft = null;
        if (_menuDepthRight != null) { _menuDepthRight.Release(); UnityEngine.Object.Destroy(_menuDepthRight); }
        _menuDepthRight = _menuDepthLeft = null; _menuDepthShrinkSince = -1f;
    }
}
