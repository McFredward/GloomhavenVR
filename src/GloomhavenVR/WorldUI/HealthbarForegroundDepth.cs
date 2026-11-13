using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Build672: ZTest Always meant "ignore all depth", including a held card or the
/// wrist board. Through-wall bars now sample depth rasterized from the actual solid
/// VR foreground. Wall/world renderers are never selected; actual shader vertex,
/// skinning, cutout and ZWrite semantics are retained, with no substitute geometry.
/// A ghost hand or fading remote board still has its original ZWrite Off and alpha
/// blend: stamping it as an opaque silhouette would restore the old ghost defect.
///
/// Registration uses existing visual-build boundaries. There is no world renderer
/// census or second world camera. A bounded, slow subtree refresh catches pooled
/// or late children; transforms/skinning/material replacements are read at render.
/// Each MultiPass eye executes and clears its own target before transparent UI.
/// The main camera colour/depth target is restored without clearing or rewriting it.
/// </summary>
internal static class HealthbarForegroundDepth
{
    private static readonly List<Transform> Roots = new(64);
    private static readonly List<Renderer> Renderers = new(256);
    private static readonly List<Renderer> Scratch = new(64);
    private static readonly List<Material> Materials = new(8);
    private static readonly HashSet<int> Seen = new();
    private static readonly List<XRDisplaySubsystem> Displays = new(1);
    private static readonly Dictionary<Shader, int> ForwardPasses = new();
    private static readonly ShaderTagId LightMode = new("LightMode");
    private static readonly int DepthId = Shader.PropertyToID("_GHVRActorForegroundDepth");
    private static readonly int SizeId = Shader.PropertyToID("_GHVRActorForegroundSize");
    private static readonly int ViewportId = Shader.PropertyToID("_GHVRActorForegroundViewport");
    private static readonly int ReadyId = Shader.PropertyToID("_GHVRActorForegroundReady");
    private static Camera? _camera;
    private static CommandBuffer? _capture;
    private static CommandBuffer? _release;
    private static bool _active;
    private static bool _dirty;
    private static float _nextRefresh;
    private static bool _viewportQueryWarned;
    private static bool _captureWarned;
    private static bool _viewportLogged;

    // These are existing owner identities, not a blanket mod-layer/name heuristic.
    // Sky/room, haunt figures, world geometry, lasers, particles and highlights are
    // deliberately absent. They must not turn "through walls" back into wall hiding.
    internal static bool IsForegroundRoot(string name) =>
        name == "GloomhavenVR.Hands" || name == "GloomhavenVR.PlayTray"
        || name == "GloomhavenVR.WristHud" || name == "GloomhavenVR.CardFan"
        || name == "GloomhavenVR.CardFanArrival" || name == "GloomhavenVR.ItemsPile"
        || name == "GloomhavenVR.CombatLogPanel" || name == "GloomhavenVR.MrBacking"
        || name == "VRCard" || name.StartsWith("VRCard_", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.Controller_", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.RemoteAvatar[", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.RemoteControlBoard[", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.RemoteHandFan[", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.RemoteItemFan[", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.RemoteBrowserFan[", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.RemoteCardFx[", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.RemoteBurnFx[", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.ModalGrab_", StringComparison.Ordinal)
        || name.StartsWith("GloomhavenVR.SurfaceGrab_", StringComparison.Ordinal);

    internal static void Register(Transform visual)
    {
        Transform? owner = visual;
        while (owner != null && !IsForegroundRoot(owner.name)) owner = owner.parent;
        if (owner == null) return;
        _dirty = true;
        for (int i = Roots.Count - 1; i >= 0; i--)
        {
            Transform root = Roots[i];
            if (root == null) { Roots.RemoveAt(i); continue; }
            if (owner == root || owner.IsChildOf(root)) return;
            if (root.IsChildOf(owner)) Roots.RemoveAt(i);
        }
        Roots.Add(owner);
    }

    internal static void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        if (active) Camera.onPreRender += BeforeCamera;
        else
        {
            Camera.onPreRender -= BeforeCamera;
            Detach();
        }
    }

    private static void BeforeCamera(Camera camera)
    {
        if (!_active || camera == null || camera != VRRigDriver.HeadCamera) return;
        try { Prepare(camera); }
        catch (Exception ex)
        {
            // A failed native render resource must not interrupt other camera
            // listeners or leave stale per-eye commands attached. Retry next render.
            Detach();
            if (!_captureWarned)
            {
                _captureWarned = true;
                string reason = ex.Message.Replace('\r', ' ').Replace('\n', ' ');
                if (reason.Length > 200) reason = reason.Substring(0, 200);
                VRLog.Warn("WorldUI", $"Healthbar foreground capture deferred ({ex.GetType().Name}: {reason}); retrying on the next head-camera render.");
            }
        }
    }

    internal static void Prepare(Camera camera)
    {
        if (_camera != camera)
        {
            Detach();
            _camera = camera;
            _capture = new CommandBuffer { name = "GloomhavenVR.HealthbarForegroundDepth" };
            _release = new CommandBuffer { name = "GloomhavenVR.HealthbarForegroundDepth.Release" };
            camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _capture);
            camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _release);
        }
        if (_dirty || Time.unscaledTime >= _nextRefresh) Refresh();
        int width = camera.targetTexture != null ? camera.targetTexture.width
            : camera.stereoEnabled && XRSettings.enabled ? XRSettings.eyeTextureWidth : camera.scaledPixelWidth;
        int height = camera.targetTexture != null ? camera.targetTexture.height
            : camera.stereoEnabled && XRSettings.enabled ? XRSettings.eyeTextureHeight : camera.scaledPixelHeight;
        Rect viewport = Viewport(camera, ref width, ref height);
        if (width < 1 || height < 1) { Detach(); return; }
        CommandBuffer capture = _capture!;
        capture.Clear();
        var descriptor = new RenderTextureDescriptor(width, height, RenderTextureFormat.Depth, 24)
        {
            msaaSamples = 1,
            // This is an ordinary camera-depth texture, not a shadow-map texture.
            // None retains raw non-comparison sampling for SAMPLE_DEPTH_TEXTURE.
            shadowSamplingMode = ShadowSamplingMode.None,
        };
        capture.GetTemporaryRT(DepthId, descriptor, FilterMode.Point);
        capture.SetRenderTarget(DepthId);
        capture.ClearRenderTarget(true, false, Color.clear);
        capture.SetViewport(viewport);
        for (int i = 0; i < Renderers.Count; i++)
        {
            Draw(capture, camera, Renderers[i]);
        }
        // Actual native held figures/doors keep their game layer. These two bounded
        // hold registries supply cached renderers, only while physically attached;
        // release/non-idle return removes them in the very next eye pass.
        DrawHeld(capture, camera, FigureGrabbable.HeldBy(HandSide.Left)?.HeldRenderers());
        DrawHeld(capture, camera, FigureGrabbable.HeldBy(HandSide.Right)?.HeldRenderers());
        DrawHeld(capture, camera, GrabbableProp.HeldBy(HandSide.Left)?.HeldRenderers());
        DrawHeld(capture, camera, GrabbableProp.HeldBy(HandSide.Right)?.HeldRenderers());
        Materials.Clear();
        capture.SetGlobalTexture(DepthId, DepthId);
        capture.SetGlobalVector(SizeId, new Vector4(1f / width, 1f / height, width, height));
        capture.SetGlobalVector(ViewportId, new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
        capture.SetGlobalFloat(ReadyId, 1f);
        capture.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        // SetRenderTarget resets the viewport to the complete allocation. Restore
        // the actual eye rectangle, including reduced Frame eye-resolution scales.
        capture.SetViewport(viewport);
        _release!.Clear();
        _release.SetGlobalFloat(ReadyId, 0f);
        _release.ReleaseTemporaryRT(DepthId);
        ActorBarDepthShaders.SetCaptureAvailable(true);
    }

    private static Rect Viewport(Camera camera, ref int width, ref int height)
    {
        // Unity renders the entire targetTexture; Camera.rect/pixelRect are ignored.
        if (camera.targetTexture != null) return new Rect(0, 0, width, height);
        if (!camera.stereoEnabled || !XRSettings.enabled) return camera.pixelRect;
        try
        {
            SubsystemManager.GetInstances(Displays);
            int eye = camera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? 1 : 0;
            for (int i = 0; i < Displays.Count; i++)
            {
                XRDisplaySubsystem display = Displays[i];
                if (!display.running || display.GetRenderPassCount() <= eye) continue;
                display.GetRenderPass(eye, out XRDisplaySubsystem.XRRenderPass pass);
                if (pass.GetRenderParameterCount() == 0) continue;
                pass.GetRenderParameter(camera, 0, out XRDisplaySubsystem.XRRenderParameter parameter);
                if (parameter.viewport.width > 0 && parameter.viewport.height > 0
                    && pass.renderTargetDesc.width > 0 && pass.renderTargetDesc.height > 0)
                {
                    Rect pixels = XrViewportPixels(parameter.viewport, pass.renderTargetDesc, ref width, ref height);
                    if (!_viewportLogged)
                    {
                        _viewportLogged = true;
                        VRLog.Info("WorldUI", $"Healthbar foreground XR viewport: normalized {parameter.viewport} -> pixels {pixels}; target {width}x{height}, eye {camera.stereoActiveEye}.");
                    }
                    return pixels;
                }
            }
        }
        catch (Exception ex)
        {
            if (!_viewportQueryWarned)
            {
                _viewportQueryWarned = true;
                VRLog.Debug("WorldUI", $"Healthbar foreground XR viewport query deferred ({ex.GetType().Name}); requested viewport scale retained.");
            }
        }
        // Built-in pipeline can expose no XRRenderPass to managed callers. Its
        // fixed MultiPass eye starts at the origin; retain the engine's requested
        // viewport fraction instead of stretching the UI to the entire allocation.
        float scale = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
        return new Rect(0, 0, width * scale, height * scale);
    }

    // Build674 hardware exposes (0,0,1,1) for each 3560x3560 OpenXR eye. The
    // provider viewport is normalized (Unity2021.3.5 URP XRPass.XRView performs
    // this same conversion); CommandBuffer.SetViewport and the shader's
    // capture-pixel mapping both require pixels. Passing that Rect through unchanged
    // captured/restored only one pixel and made every bar sample the same depth:
    // a single card then hid all actors and flickered as that sample changed. The
    // Build672 targetTexture fixtures bypassed this XR boundary entirely.
    internal static Rect XrPixelViewport(Rect viewport, int width, int height) =>
        new(viewport.x * width, viewport.y * height, viewport.width * width, viewport.height * height);

    private static Rect XrViewportPixels(Rect normalized, RenderTextureDescriptor target, ref int width, ref int height)
    {
        // XRRenderParameter.viewport is normalized, including on desktop providers:
        // Unity2021.3.5's XRView constructor multiplies it by renderTargetDesc.
        // B674 Quest evidence reports (0,0,.8,.8) for a 1680x1760 target. Passing
        // that rectangle to CommandBuffer.SetViewport requests a subpixel capture
        // and restores the eye target with that same incorrect subpixel viewport.
        // Use this pass's allocation rather than the global left-eye dimensions;
        // MultiPass providers can supply a differently sized right-eye target.
        width = target.width;
        height = target.height;
        return XrPixelViewport(normalized, width, height);
    }

    private static void DrawHeld(CommandBuffer capture, Camera camera, Renderer[]? renderers)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++) Draw(capture, camera, renderers[i]);
    }

    private static void Draw(CommandBuffer capture, Camera camera, Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || renderer.forceRenderingOff
            || !renderer.gameObject.activeInHierarchy
            || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) return;
        // Original materials retain alpha clip, vertex animation and ZWrite state.
        // Select the visible forward pass, never all shadow/add/light passes.
        Materials.Clear(); renderer.GetSharedMaterials(Materials);
        for (int submesh = 0; submesh < Materials.Count; submesh++)
        {
            Material material = Materials[submesh];
            if (material == null || material.passCount == 0) continue;
            capture.DrawRenderer(renderer, material, submesh, ForwardPass(material.shader));
        }
    }

    private static int ForwardPass(Shader shader)
    {
        if (ForwardPasses.TryGetValue(shader, out int selected)) return selected;
        // Native ForwardBase and ForwardAdd can share the name FORWARD. Select
        // the actual visible base by its tag; names alone are ambiguous. Untagged
        // single-pass/unlit visuals retain their authored first colour pass.
        selected = 0;
        for (int pass = 0; pass < shader.passCount; pass++)
        {
            if (!string.Equals(shader.FindPassTagValue(pass, LightMode).name,
                    "ForwardBase", StringComparison.OrdinalIgnoreCase)) continue;
            selected = pass;
            break;
        }
        ForwardPasses[shader] = selected;
        return selected;
    }

    private static void Refresh()
    {
        _dirty = false;
        _nextRefresh = Time.unscaledTime + 0.5f;
        // A native item-chip backing can move out of its pile into a hand seat.
        // Retain its already-established physical ownership until destruction;
        // don't lose the depth blocker merely because native gameplay reparents it.
        Seen.Clear();
        for (int i = Renderers.Count - 1; i >= 0; i--)
        {
            Renderer renderer = Renderers[i];
            if (renderer == null) Renderers.RemoveAt(i);
            else Seen.Add(renderer.GetInstanceID());
        }
        for (int i = Roots.Count - 1; i >= 0; i--)
        {
            Transform root = Roots[i];
            if (root == null) { Roots.RemoveAt(i); continue; }
            Scratch.Clear(); root.GetComponentsInChildren(includeInactive: true, Scratch);
            for (int n = 0; n < Scratch.Count; n++)
            {
                Renderer renderer = Scratch[n];
                if (renderer == null || (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)) continue;
                if (renderer.gameObject.layer != VRLayers.ModLayer && root.name != "GloomhavenVR.MrBacking") continue;
                // A Camera inside an otherwise owned visual belongs to a native RT
                // preview. Its subject must never enter a head-camera depth pass.
                if (renderer.GetComponentInParent<Camera>() != null) continue;
                if (Seen.Add(renderer.GetInstanceID())) Renderers.Add(renderer);
            }
        }
        Scratch.Clear(); Seen.Clear();
    }

    private static void Detach()
    {
        if (_camera != null)
        {
            if (_capture != null) _camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _capture);
            if (_release != null) _camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _release);
        }
        _capture?.Release(); _release?.Release(); _capture = null; _release = null; _camera = null;
        Shader.SetGlobalFloat(ReadyId, 0f);
        _viewportLogged = false;
        ActorBarDepthShaders.SetCaptureAvailable(false);
    }
}
