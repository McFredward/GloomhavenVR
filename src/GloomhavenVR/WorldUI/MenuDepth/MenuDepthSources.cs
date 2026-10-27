using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MenuDepth;

/// <summary>Finite registration and exception-safe borrowing for actual visible native graphics.</summary>
internal static class MenuDepthSources
{
    private static readonly List<IMenuDepthSource> Sources = new(32);
    private static readonly List<IMenuDepthSource> Prepared = new(16);
    private static int _cleanupFailures;
    private static int _directFailures;
    private static int _startedCount;
    private static bool _capturing;
    private static bool _hooked;
    private static Camera? _directCamera;
    private static readonly int WorldEyeId = Shader.PropertyToID("_GvrMenuDepthWorldEye");
    private static readonly int ProjectMaskId = Shader.PropertyToID("_GvrMenuDepthProjectMask");
    private static Vector4 _previousWorldEye;
    private static float _previousProjectMask;
    private static bool _maskScoped;
    internal static int ExcludedDirectLayers { get; set; }
    internal static event Action<Camera>? BeforeHeadCapture;

    internal static void Register(IMenuDepthSource source)
    {
        if (!Sources.Contains(source)) Sources.Add(source);
        if (!_hooked)
        {
            _hooked = true;
            Camera.onPreCull += BeforeDirectCamera;
            Camera.onPostRender += AfterDirectCamera;
        }
    }

    internal static void Unregister(IMenuDepthSource source)
    {
        Sources.Remove(source);
        if (Sources.Count == 0 && _directCamera != null)
        {
            _directCamera = null;
            AfterCapture();
        }
        UnhookIfIdle();
    }

    internal static bool HasContent(Transform root)
    {
        for (int i = Sources.Count - 1; i >= 0; i--)
        {
            IMenuDepthSource source = Sources[i];
            if (source.Graphic == null) { Sources.RemoveAt(i); continue; }
            if (Applies(source, root)) return true;
        }
        return false;
    }

    private static bool Visible(Graphic graphic) => graphic != null && graphic.isActiveAndEnabled
        && graphic.gameObject.activeInHierarchy && graphic.canvas != null && graphic.canvas.isActiveAndEnabled
        && graphic.color.a > 0f && graphic.canvasRenderer.GetInheritedAlpha() > 0f;

    private static bool Applies(IMenuDepthSource source, Transform root) => Visible(source.Graphic)
        && source.Graphic.transform.IsChildOf(root) && source.IsReady;

    internal static void CollectReadyGraphics(List<Graphic> destination)
    {
        destination.Clear();
        for (int i = 0; i < Sources.Count; i++)
        {
            IMenuDepthSource source = Sources[i];
            if (Visible(source.Graphic) && source.IsReady
                && !destination.Contains(source.Graphic)) destination.Add(source.Graphic);
        }
    }

    /// <summary>Screen-space UI uses the physical window-to-original-graphic eye mapper.</summary>
    internal static void BeforeCaptureWhere(MenuDepthEye eye, Predicate<Graphic> applies,
        Func<Graphic, MenuDepthEye> mapper)
    {
        if (_capturing) throw new InvalidOperationException("Nested menu-depth preparation is not owned by this capture.");
        _capturing = true; _startedCount = 0; Prepared.Clear();
        try
        {
            BeginMaskScope(eye);
            for (int i = 0; i < Sources.Count; i++)
            {
                IMenuDepthSource source = Sources[i];
                if (Visible(source.Graphic) && source.IsReady
                    && applies(source.Graphic)) Prepared.Add(source);
            }
            for (int i = 0; i < Prepared.Count; i++)
            {
                _startedCount = i + 1;
                Prepared[i].BeforeCapture(mapper(Prepared[i].Graphic));
            }
        }
        catch { AfterCapture(); throw; }
    }

    /// <summary>Call before the private capture; a failing source is also restored.</summary>
    internal static void BeforeCapture(Transform root, MenuDepthEye eye)
    {
        if (_capturing) throw new InvalidOperationException("Nested menu-depth preparation is not owned by this capture.");
        _capturing = true;
        _startedCount = 0;
        Prepared.Clear();
        try
        {
            BeginMaskScope(eye);
            // Snapshot membership before callbacks: a native texture setter may
            // synchronously hide or repopulate another original window.
            for (int i = 0; i < Sources.Count; i++)
                if (Applies(Sources[i], root)) Prepared.Add(Sources[i]);
            for (int i = 0; i < Prepared.Count; i++)
            {
                _startedCount = i + 1;
                Prepared[i].BeforeCapture(eye);
            }
        }
        catch
        {
            AfterCapture();
            throw;
        }
    }

    /// <summary>Restore every prepared source, including partial preparation, exactly once.</summary>
    internal static void AfterCapture()
    {
        if (!_capturing) return;
        try
        {
            for (int i = _startedCount - 1; i >= 0; i--)
            {
                try { Prepared[i].AfterCapture(); }
                catch (Exception error)
                {
                    int count = ++_cleanupFailures;
                    if (count <= 4 || (count & (count - 1)) == 0)
                        VRLog.Warn("WorldUI", "MENU DEPTH SOURCE RESTORE: " + error.GetType().Name
                            + "; count=" + count + "; native flow continues.");
                }
            }
        }
        finally
        {
            Prepared.Clear(); _startedCount = 0; _capturing = false;
            if (_maskScoped)
            {
                _maskScoped = false;
                Shader.SetGlobalVector(WorldEyeId, _previousWorldEye);
                Shader.SetGlobalFloat(ProjectMaskId, _previousProjectMask);
            }
            UnhookIfIdle();
        }
    }

    private static void BeginMaskScope(MenuDepthEye eye)
    {
        _previousWorldEye = Shader.GetGlobalVector(WorldEyeId);
        _previousProjectMask = Shader.GetGlobalFloat(ProjectMaskId);
        _maskScoped = true;
        Shader.SetGlobalVector(WorldEyeId, eye.WorldPosition);
        Shader.SetGlobalFloat(ProjectMaskId, eye.ProjectToGraphicPlane ? 0f : 1f);
    }

    private static void UnhookIfIdle()
    {
        if (!_hooked || Sources.Count != 0 || _capturing) return;
        Camera.onPreCull -= BeforeDirectCamera;
        Camera.onPostRender -= AfterDirectCamera;
        _hooked = false;
    }

    // Supersampling can be switched off independently. Those original world-space
    // canvases already draw through the real eye, so only their content lease is
    // needed. Private capture layers are excluded by the head mask and cannot be
    // prepared a second time through this path.
    private static void BeforeDirectCamera(Camera camera)
    {
        if (camera == null || camera != VRRigDriver.HeadCamera) return;
        // A missing post callback must not retain a texture lease into the next
        // head render. Unity cannot recursively render that same camera.
        if (_directCamera == camera) { _directCamera = null; AfterCapture(); }
        if (_capturing) return;
        string phase = "offscreen coordinators";
        IMenuDepthSource? sourceInProgress = null;
        try
        {
            // Run all offscreen coordinators before any direct source lease is
            // held across this head draw. Subscriber order must not change it.
            BeforeHeadCapture?.Invoke(camera);
            _capturing = true; _startedCount = 0; Prepared.Clear();
            phase = "direct source selection";
            for (int i = 0; i < Sources.Count; i++)
            {
                IMenuDepthSource source = Sources[i];
                sourceInProgress = source;
                // Screen-space consumers belong to the original UI camera stack
                // and need its per-Graphic mapped eye, even if the head's transient
                // culling mask still contains the native UI layer. Pooled alpha-zero
                // consumers likewise must not borrow a shared model camera.
                if (Visible(source.Graphic) && source.Graphic.canvas.rootCanvas.renderMode == RenderMode.WorldSpace
                    && source.IsReady
                    && (ExcludedDirectLayers & (1 << source.Graphic.gameObject.layer)) == 0
                    && (camera.cullingMask & (1 << source.Graphic.gameObject.layer)) != 0)
                    Prepared.Add(source);
            }
            if (Prepared.Count == 0) { AfterCapture(); return; }
            _directCamera = camera;
            bool right = camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right;
            Vector3 position = camera.stereoEnabled
                ? camera.GetStereoViewMatrix(right ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left)
                    .inverse.MultiplyPoint3x4(Vector3.zero)
                : camera.transform.position;
            var eye = new MenuDepthEye(right, position, camera);
            BeginMaskScope(eye);
            phase = "direct source preparation";
            for (int i = 0; i < Prepared.Count; i++)
            {
                _startedCount = i + 1;
                sourceInProgress = Prepared[i];
                Prepared[i].BeforeCapture(eye);
            }
        }
        catch (Exception error)
        {
            AfterCapture(); _directCamera = null;
            int count = ++_directFailures;
            if (count <= 4 || (count & (count - 1)) == 0)
            {
                Graphic? graphic = null;
                try { graphic = sourceInProgress?.Graphic; } catch { } // Diagnostics must not replace the original failure.
                string cameraName = camera != null ? camera.name : "destroyed";
                string context = "phase='" + phase + "', camera='" + cameraName + "', source='"
                    + (sourceInProgress?.GetType().Name ?? "none") + "', graphic='" + (graphic != null ? graphic.name : "none") + "'";
                VRLog.Warn("WorldUI", "MENU DEPTH DIRECT CAPTURE: " + error.GetType().Name
                    + ": " + error.Message + "; count=" + count + "; " + context + "; original UI remains available.");
                if (VRLog.WantsDebug) VRLog.Debug("WorldUI", "MENU DEPTH DIRECT CAPTURE DETAIL: " + context + "; " + error);
            }
        }
    }

    private static void AfterDirectCamera(Camera camera)
    {
        if (!ReferenceEquals(camera, _directCamera)) return;
        _directCamera = null;
        AfterCapture();
    }
}
