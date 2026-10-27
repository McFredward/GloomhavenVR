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
    private readonly List<Camera> _menuDepthCameras = new(8);
    private RenderTexture? _menuDepthRight, _menuDepthLeft;
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

    private void CaptureMenuDepthScreen(Camera head)
    {
        if (head != Rig.VRRigDriver.HeadCamera || _menuDepthFrame == Time.frameCount) return;
        _menuDepthFrame = Time.frameCount;
        if (!_visible || !_splitRouting || _uiRt == null || _quad == null || _glassMaterial == null
            || !HasNativeMenuDepth() || !MenuDepthDisplay.TryGetEyePositions(head, out Vector3 left, out Vector3 right))
        { ReleaseMenuDepthScreen(); return; }
        try
        {
            if (_menuDepthRight == null || _menuDepthLeft != _uiRt)
            {
                ReleaseMenuDepthScreen();
                _menuDepthRight = new RenderTexture(_uiRt.descriptor)
                {
                    name = "GloomhavenVR.FlatScreenRT.MenuDepthRight", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = _uiRt.filterMode, wrapMode = _uiRt.wrapMode,
                };
                if (!_menuDepthRight.Create()) throw new InvalidOperationException("Native menu right capture target was refused.");
                _menuDepthLeft = _uiRt;
            }
            _menuDepthCameras.Clear();
            for (int i = 0; i < _captured.Count; i++)
            {
                CapturedCamera source = _captured[i];
                if (!source.IsUi || source.Camera == null || !source.Camera.isActiveAndEnabled) continue;
                int at = _menuDepthCameras.Count;
                while (at > 0 && _menuDepthCameras[at - 1].depth > source.Camera.depth) at--;
                _menuDepthCameras.Insert(at, source.Camera);
            }
            CaptureMenuDepthUi(_uiRt, new MenuDepthEye(false, left, head));
            CaptureMenuDepthUi(_menuDepthRight, new MenuDepthEye(true, right, head));
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
                try
                {
                    MenuDepthSources.BeforeCaptureWhere(eye, _menuDepthApplies!, _menuDepthMapper!, _menuDepthPixels!);
                    source.targetTexture = target;
                    if (VRLayers.ModLayerMask != VRLayers.GameUiLayerMask) source.cullingMask &= ~VRLayers.ModLayerMask;
                    if (MenuDepthSources.HasPreparedSources) Canvas.ForceUpdateCanvases();
                    source.Render();
                }
                finally
                {
                    MenuDepthSources.AfterCapture();
                    if (source != null) { source.targetTexture = originalTarget; source.cullingMask = originalMask; }
                    _menuDepthBorrowedCamera = null;
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
            && camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? _menuDepthRight : _uiRt;
    }
    private void AfterMenuDepthHead(Camera camera)
    {
        if (camera == Rig.VRRigDriver.HeadCamera && _glassMaterial != null && _uiRt != null)
            _glassMaterial.mainTexture = _uiRt;
    }
    private void ReleaseMenuDepthScreen()
    {
        if (_glassMaterial != null && _menuDepthRight != null && _glassMaterial.mainTexture == _menuDepthRight)
            _glassMaterial.mainTexture = _uiRt;
        if (_menuDepthRight != null) { _menuDepthRight.Release(); UnityEngine.Object.Destroy(_menuDepthRight); }
        _menuDepthRight = _menuDepthLeft = null;
    }
}
