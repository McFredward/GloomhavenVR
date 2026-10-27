using System;
using System.Collections;
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
    private static Action? _discoveryPump;
    private static bool _pumping;
    private static int _pumpFailures;
    private static readonly List<IMenuDepthDirectSource> DirectPrimes = new(8);
    private static readonly List<IMenuDepthDirectSource> PrimeCandidates = new(8);
    private static bool _primingDirect, _cleaningDirect;
    private static int _directPrimeFrame = -1, _primeFailures;
    private static MenuDepthDirectCleanup? _directCleanup;
    internal static int ExcludedDirectLayers { get; set; }
    internal static event Action<Camera>? BeforeHeadCapture;
    internal static bool HasPreparedSources => _startedCount != 0;

    /// <summary>One active driver's pending identity/readiness work; never a capture-time scene census.</summary>
    internal static void SetDiscoveryPump(Action? pump)
    {
        _discoveryPump = pump;
        if (pump != null) EnsureHook(); else UnhookIfIdle();
    }

    private static void FlushPendingContent()
    {
        if (_capturing || _pumping || _discoveryPump == null) return;
        _pumping = true;
        try { _discoveryPump(); }
        catch (Exception error)
        {
            int count = ++_pumpFailures;
            if (count <= 4 || (count & (count - 1)) == 0)
            {
                VRLog.Warn("WorldUI", "MENU DEPTH CONTENT REFRESH: " + error.GetType().Name + ": " + error.Message + "; count=" + count);
                if (VRLog.WantsDebug) VRLog.Debug("WorldUI", "MENU DEPTH CONTENT REFRESH DETAIL: " + error);
            }
        }
        finally { _pumping = false; UnhookIfIdle(); }
    }

    internal static void Register(IMenuDepthSource source)
    {
        if (!Sources.Contains(source)) Sources.Add(source);
        EnsureHook();
    }

    private static void EnsureHook()
    {
        if (!_hooked)
        {
            _hooked = true;
            Camera.onPreCull += BeforeDirectCamera;
            Camera.onPostRender += AfterDirectCamera;
            Canvas.preWillRenderCanvases += PrimeDirectSources;
        }
    }

    internal static void Unregister(IMenuDepthSource source)
    {
        Sources.Remove(source);
        if (source is IMenuDepthDirectSource direct && DirectPrimes.Remove(direct))
            RestoreDirect(direct, false, "direct unregister");
        if (Sources.Count == 0 && _directCamera != null)
        {
            _directCamera = null;
            AfterCapture();
        }
        UnhookIfIdle();
    }

    internal static bool HasContent(Transform root)
    {
        FlushPendingContent();
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
        && graphic.color.a > 0f && !graphic.canvasRenderer.cull && graphic.canvasRenderer.GetInheritedAlpha() > 0f;

    private static bool Applies(IMenuDepthSource source, Transform root) => Visible(source.Graphic)
        && source.Graphic.transform.IsChildOf(root) && source.IsReady;

    internal static void CollectReadyGraphics(List<Graphic> destination)
    {
        FlushPendingContent();
        destination.Clear();
        for (int i = 0; i < Sources.Count; i++)
        {
            IMenuDepthSource source = Sources[i];
            if (Visible(source.Graphic) && source.IsReady
                && !destination.Contains(source.Graphic)) destination.Add(source.Graphic);
        }
    }

    internal static void CollectReadyForegroundGraphics(List<Graphic> destination)
    {
        FlushPendingContent(); destination.Clear();
        for (int i = 0; i < Sources.Count; i++)
        {
            IMenuDepthSource source = Sources[i];
            if (source is not IMenuDepthBoundsSource) continue;
            Graphic graphic = source.Graphic;
            if (Visible(graphic) && source.IsReady && !destination.Contains(graphic)) destination.Add(graphic);
        }
    }

    internal static bool TryGetForegroundBounds(Graphic graphic, MenuDepthEye eye, out Rect graphicLocalBounds)
    {
        FlushPendingContent();
        for (int i = 0; i < Sources.Count; i++)
        {
            IMenuDepthSource source = Sources[i];
            if (source is IMenuDepthBoundsSource bounds
                && ReferenceEquals(source.Graphic, graphic) && Visible(graphic) && source.IsReady)
                return bounds.TryGetForegroundBounds(eye, out graphicLocalBounds);
        }
        graphicLocalBounds = default;
        return false;
    }

    /// <summary>Screen-space UI uses the physical window-to-original-graphic eye mapper.</summary>
    internal static void BeforeCaptureWhere(MenuDepthEye eye, Predicate<Graphic> applies,
        Func<Graphic, MenuDepthEye> mapper, Func<Graphic, Vector2>? pixels = null)
    {
        if (_capturing) throw new InvalidOperationException("Nested menu-depth preparation is not owned by this capture.");
        FlushPendingContent();
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
                IMenuDepthSource source = Prepared[i];
                MenuDepthEye mapped = mapper(source.Graphic);
                if (source is IMenuDepthSampledSource && pixels != null)
                    mapped = WithPixels(mapped, pixels(source.Graphic));
                source.BeforeCapture(mapped);
            }
        }
        catch { AfterCapture(); throw; }
    }

    /// <summary>Call before the private capture; a failing source is also restored.</summary>
    internal static void BeforeCapture(Transform root, MenuDepthEye eye, Camera? raster = null)
    {
        if (_capturing) throw new InvalidOperationException("Nested menu-depth preparation is not owned by this capture.");
        FlushPendingContent();
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
                Prepared[i].BeforeCapture(SampleEye(Prepared[i], eye, raster));
            }
        }
        catch
        {
            AfterCapture();
            throw;
        }
    }

    private static MenuDepthEye WithPixels(MenuDepthEye eye, Vector2 pixels)
        => new(eye.Right, eye.WorldPosition, eye.HeadCamera, eye.ProjectToGraphicPlane, pixels,
            eye.HasWorldToHeadClip ? eye.WorldToHeadClip : null);

    private static MenuDepthEye SampleEye(IMenuDepthSource source, MenuDepthEye eye, Camera? raster = null)
        => source is IMenuDepthSampledSource && eye.PixelFootprint == Vector2.zero && eye.HeadCamera != null
            && eye.HeadCamera == VRRigDriver.HeadCamera
            ? WithPixels(eye, MenuDepthProjection.PixelFootprint(source.Graphic.rectTransform, eye, raster)) : eye;

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
                    NoteRestoreFailure(error, "prepared source");
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

    private static void NoteRestoreFailure(Exception error, string phase)
    {
        int count = ++_cleanupFailures;
        if (count <= 4 || (count & (count - 1)) == 0)
        {
            VRLog.Warn("WorldUI", "MENU DEPTH SOURCE RESTORE: " + error.GetType().Name + ": " + error.Message
                + "; count=" + count + "; phase=" + phase + "; native flow continues.");
            if (VRLog.WantsDebug) VRLog.Debug("WorldUI", "MENU DEPTH SOURCE RESTORE DETAIL: " + phase + "; " + error);
        }
    }

    private static void RestoreDirect(IMenuDepthDirectSource source, bool failed, string phase)
    {
        try { if (failed) source.CancelDirectCapture(); else source.AfterCapture(); }
        catch (Exception error) { NoteRestoreFailure(error, phase); }
    }

    private static void NotePrimeFailure(Exception error, string phase)
    {
        int count = ++_primeFailures;
        if (count <= 4 || (count & (count - 1)) == 0)
        {
            VRLog.Warn("WorldUI", "MENU DEPTH DIRECT BATCH: " + error.GetType().Name + ": " + error.Message
                + "; count=" + count + "; phase=" + phase);
            if (VRLog.WantsDebug) VRLog.Debug("WorldUI", "MENU DEPTH DIRECT BATCH DETAIL: " + phase + "; " + error);
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
        if (!_hooked || Sources.Count != 0 || _capturing || _pumping || _primingDirect || _cleaningDirect || _discoveryPump != null) return;
        Camera.onPreCull -= BeforeDirectCamera;
        Camera.onPostRender -= AfterDirectCamera;
        Canvas.preWillRenderCanvases -= PrimeDirectSources;
        _hooked = false;
        CompleteDirectFrame();
        if (_directCleanup != null)
        {
            GameObject owner = _directCleanup.gameObject; _directCleanup = null;
            UnityEngine.Object.Destroy(owner);
        }
    }

    private static bool DirectlyVisible(IMenuDepthSource source, Camera camera)
        => Visible(source.Graphic) && source.Graphic.canvas.rootCanvas.renderMode == RenderMode.WorldSpace
            && source.IsReady && (ExcludedDirectLayers & (1 << source.Graphic.gameObject.layer)) == 0
            && (camera.cullingMask & (1 << source.Graphic.gameObject.layer)) != 0;

    // uGUI snapshots a world's texture before Camera.onPreCull. Loan the stable
    // source-left identity while native Canvas batches are built, without a model
    // render. Each actual head eye refreshes its pixels later, using its own view.
    private static void PrimeDirectSources()
    {
        Camera? head = VRRigDriver.HeadCamera;
        if (_capturing || _primingDirect || head == null || !head.isActiveAndEnabled) return;
        _primingDirect = true;
        try
        {
            FlushPendingContent();
            PrimeCandidates.Clear();
            for (int i = 0; i < Sources.Count; i++)
                if (Sources[i] is IMenuDepthDirectSource direct && DirectlyVisible(direct, head)) PrimeCandidates.Add(direct);
            for (int i = DirectPrimes.Count - 1; i >= 0; i--)
                if (!PrimeCandidates.Contains(DirectPrimes[i]))
                { IMenuDepthDirectSource retired = DirectPrimes[i]; DirectPrimes.RemoveAt(i); RestoreDirect(retired, false, "retired direct prime"); }
            if (PrimeCandidates.Count == 0) return;
            Vector3 left = head.transform.position, right = left;
            MenuDepthDisplay.TryGetEyePositions(head, out Vector3 observedLeft, out Vector3 observedRight);
            if (MenuDepthProjection.Finite(observedLeft) && MenuDepthProjection.Finite(observedRight) && observedLeft != observedRight)
            { left = observedLeft; right = observedRight; }
            for (int i = 0; i < PrimeCandidates.Count; i++)
            {
                IMenuDepthDirectSource source = PrimeCandidates[i];
                MenuDepthEye l = SampleEye(source, new MenuDepthEye(false, left, head));
                MenuDepthEye r = SampleEye(source, new MenuDepthEye(true, right, head));
                try
                {
                    source.PrimeDirectCapture(WithPixels(l, Vector2.Max(l.PixelFootprint, r.PixelFootprint)));
                    if (!Sources.Contains(source)) { RestoreDirect(source, false, "removed during direct prime"); continue; }
                    if (!DirectPrimes.Contains(source)) DirectPrimes.Add(source);
                    _directPrimeFrame = Time.frameCount;
                    if (_directCleanup == null)
                    {
                        var owner = new GameObject("GloomhavenVR.MenuDepth.DirectCleanup") { hideFlags = HideFlags.HideAndDontSave };
                        UnityEngine.Object.DontDestroyOnLoad(owner);
                        _directCleanup = owner.AddComponent<MenuDepthDirectCleanup>();
                    }
                }
                catch (Exception error)
                {
                    RestoreDirect(source, true, "failed direct prime");
                    NotePrimeFailure(error, "source preparation");
                }
            }
        }
        catch (Exception error)
        {
            CompleteDirectFrame(failed: true);
            NotePrimeFailure(error, "candidate selection");
        }
        finally { PrimeCandidates.Clear(); _primingDirect = false; UnhookIfIdle(); }
    }

    internal static void CompleteDirectFrame(bool onlyIfStale = false, bool failed = false)
    {
        if (_cleaningDirect || (onlyIfStale && _directPrimeFrame == Time.frameCount)) return;
        _cleaningDirect = true;
        try
        {
            if (_directCamera != null) { _directCamera = null; AfterCapture(); }
            while (DirectPrimes.Count > 0)
            {
                IMenuDepthDirectSource source = DirectPrimes[DirectPrimes.Count - 1]; DirectPrimes.RemoveAt(DirectPrimes.Count - 1);
                RestoreDirect(source, failed, failed ? "failed direct frame" : "direct frame completion");
            }
            _directPrimeFrame = -1;
        }
        finally { _cleaningDirect = false; UnhookIfIdle(); }
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
            FlushPendingContent();
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
                if (DirectlyVisible(source, camera))
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
                MenuDepthEye sampled = SampleEye(Prepared[i], eye);
                if (Prepared[i] is IMenuDepthDirectSource direct && DirectPrimes.Contains(direct)) direct.BeforeDirectCapture(sampled);
                else Prepared[i].BeforeCapture(sampled);
            }
        }
        catch (Exception error)
        {
            AfterCapture(); _directCamera = null;
            CompleteDirectFrame(failed: true);
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

// No per-eye global Canvas rebuild. The loop only restores an orphaned early
// texture loan when a head draw/post callback was skipped or the application hid.
[DefaultExecutionOrder(-32000)]
internal sealed class MenuDepthDirectCleanup : MonoBehaviour
{
    private static readonly WaitForEndOfFrame End = new();
    private IEnumerator Start()
    {
        while (true) { yield return End; MenuDepthSources.CompleteDirectFrame(); }
    }
    private void Update() => MenuDepthSources.CompleteDirectFrame(onlyIfStale: true);
    private void OnDisable() => MenuDepthSources.CompleteDirectFrame();
}
