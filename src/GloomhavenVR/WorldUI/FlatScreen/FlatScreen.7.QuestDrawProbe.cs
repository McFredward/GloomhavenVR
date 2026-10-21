using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    /// <summary>
    /// The texture-copy probes establish producer pixels, not consumer drawing.
    /// Draw each actual menu mesh/material once into a tiny ordinary target at
    /// Update, using the actual left-eye view/projection and object transform.
    /// This distinguishes a bad loaded mesh/material from the final XR draw.
    /// Never bind an ordinary target inside a head rendering callback. There are
    /// two probes per launch, at Debug only; no full-eye readback or render loop.
    /// </summary>
    private static void QuestProbeConsumer(Renderer? renderer, Material? material,
        Camera head, string scene, string role)
    {
        if (!SystemInfo.supportsAsyncGPUReadback) return;
        RenderTexture? target = null;
        try
        {
            Matrix4x4 view = head.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 projection = head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            target = RenderQuestConsumerProbe(renderer, material, view, projection);
            if (target == null) return;
            RenderTexture retained = target;
            AsyncGPUReadback.Request(retained, 0, TextureFormat.RGBA32, request =>
            {
                try
                {
                    if (request.hasError)
                    {
                        UnityEngine.Debug.LogWarning("[Quest startup] presentation menu-consumer-draw scene=" + scene
                            + " role=" + role + " readback=failed");
                        return;
                    }
                    var pixels = request.GetData<Color32>();
                    int rgbMax = 0, nonBlack = 0, alphaPixels = 0;
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color32 pixel = pixels[index];
                        rgbMax = System.Math.Max(rgbMax, System.Math.Max(pixel.r, System.Math.Max(pixel.g, pixel.b)));
                        if (pixel.r > 1 || pixel.g > 1 || pixel.b > 1) nonBlack++;
                        if (pixel.a > 1) alphaPixels++;
                    }
                    UnityEngine.Debug.Log("[Quest startup] presentation menu-consumer-draw scene=" + scene
                        + " role=" + role + " phase=update-offscreen eye=left width=48 height=32"
                        + " samples=" + pixels.Length + " rgbMax=" + rgbMax + " nonBlack=" + nonBlack
                        + " alphaPixels=" + alphaPixels + " finalXrPixelsObserved=false");
                }
                finally { retained.Release(); Object.Destroy(retained); }
            });
            target = null; // asynchronous request owns this target now
        }
        catch (System.Exception error)
        {
            UnityEngine.Debug.LogWarning("[Quest startup] presentation menu-consumer-draw scene=" + scene
                + " role=" + role + " readback=unavailable detail=" + error.Message);
        }
        finally
        {
            if (target != null) { target.Release(); Object.Destroy(target); }
        }
    }

    internal static void ProbeQuestLoadingDraw(Renderer? renderer, Camera head, int sample)
    {
        if (!SystemInfo.supportsAsyncGPUReadback || renderer == null || renderer.sharedMaterial == null) return;
        RenderTexture? target = null;
        try
        {
            Matrix4x4 view = head.stereoEnabled ? head.GetStereoViewMatrix(Camera.StereoscopicEye.Left) : head.worldToCameraMatrix;
            Matrix4x4 projection = head.stereoEnabled ? head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) : head.projectionMatrix;
            target = RenderQuestConsumerProbe(renderer, renderer.sharedMaterial, view, projection);
            if (target == null) return;
            RenderTexture retained = target;
            int frame = Time.frameCount;
            AsyncGPUReadback.Request(retained, 0, TextureFormat.RGBA32, request =>
            {
                try
                {
                    int visible = 0;
                    if (!request.hasError)
                    {
                        var pixels = request.GetData<Color32>();
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            Color32 pixel = pixels[i];
                            if (pixel.a > 1 && (pixel.r > 1 || pixel.g > 1 || pixel.b > 1)) visible++;
                        }
                    }
                    UnityEngine.Debug.Log("[Quest startup] presentation spinner-final stage=spinner-source sample=" + sample
                        + " frame=" + frame + " visiblePixels=" + visible + " failed=" + request.hasError
                        + " width=48 height=32 finalXrPixelsObserved=false");
                }
                finally { retained.Release(); Object.Destroy(retained); }
            });
            target = null;
        }
        catch (System.Exception error)
        {
            UnityEngine.Debug.LogWarning("[Quest startup] presentation spinner-final stage=spinner-source sample=" + sample
                + " unavailable=" + error.GetType().Name);
        }
        finally { if (target != null) { target.Release(); Object.Destroy(target); } }
    }

    private static RenderTexture? RenderQuestConsumerProbe(Renderer? renderer, Material? material,
        Matrix4x4 view, Matrix4x4 projection)
    {
        Mesh? mesh = renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (mesh == null || material == null) return null;
        MaterialPropertyBlock? properties = null;
        if (renderer!.HasPropertyBlock())
        {
            properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
        }
        return RenderQuestMenuMeshProbe(mesh, renderer.localToWorldMatrix, material, properties, view, projection);
    }

    private static readonly MethodInfo? PopulateMenuImage = typeof(Image).GetMethod("OnPopulateMesh",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VertexHelper) }, null);

    private static RenderTexture? RenderQuestGraphicProbe(Graphic graphic, Camera? source)
    {
        if (source == null || graphic == null || graphic.GetType() != typeof(Image)
            || graphic.materialForRendering == null || PopulateMenuImage == null) return null;
        var mesh = new Mesh { name = "GloomhavenVR.QuestMenuGraphicProbe" };
        try
        {
            // Unity2021 CanvasRenderer has no mesh getter. Ask the original
            // Image implementation for its current Sprite geometry without
            // changing its CanvasRenderer, state, texture, or authored material.
            // This source eligibility check is separate from the real captured
            // target/consumer proof; it cannot prove final XR composition.
            using (var vertices = new VertexHelper())
            {
                PopulateMenuImage.Invoke(graphic, new object[] { vertices });
                vertices.FillMesh(mesh);
            }
            if (mesh.vertexCount < 3) return null;
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_MainTex", graphic.mainTexture);
            return RenderQuestMenuMeshProbe(mesh, graphic.transform.localToWorldMatrix,
                graphic.materialForRendering, properties, source.worldToCameraMatrix, source.projectionMatrix);
        }
        finally { Object.Destroy(mesh); }
    }

    private static RenderTexture? RenderQuestMenuMeshProbe(Mesh mesh, Matrix4x4 localToWorld,
        Material material, MaterialPropertyBlock? properties, Matrix4x4 view, Matrix4x4 projection)
    {
        var target = new RenderTexture(48, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "GloomhavenVR.QuestMenuConsumerProbe", filterMode = FilterMode.Point };
        RenderTexture? previous = RenderTexture.active;
        var commands = new CommandBuffer { name = "GloomhavenVR.QuestMenuConsumerProbe" };
        var cameraObject = new GameObject("GloomhavenVR.QuestMenuConsumerProbe")
            { hideFlags = HideFlags.HideAndDontSave };
        Camera probeCamera = cameraObject.AddComponent<Camera>();
        probeCamera.enabled = false;
        probeCamera.stereoTargetEye = StereoTargetEyeMask.None;
        UnityEngine.XR.XRDevice.DisableAutoXRCameraTracking(probeCamera, true);
        probeCamera.cullingMask = 0; // only the explicit live consumer draw below
        probeCamera.clearFlags = CameraClearFlags.SolidColor;
        probeCamera.backgroundColor = Color.clear;
        probeCamera.allowHDR = false;
        probeCamera.allowMSAA = false;
        probeCamera.renderingPath = RenderingPath.Forward;
        probeCamera.targetTexture = target;
        probeCamera.worldToCameraMatrix = view;
        probeCamera.projectionMatrix = projection;
        bool retained = false;
        try
        {
            if (!target.Create()) throw new System.InvalidOperationException("Menu consumer probe target could not be created.");
            // Let an ordinary camera establish its complete native draw state.
            // Builtin GPU matrices are not reliably queryable shader globals;
            // manual SetViewProjectionMatrices/save/restore would add another
            // unproven state boundary to the very defect being investigated.
            // This camera is disabled, has a private target and renders only at
            // Update, never nested inside a head-camera rendering callback.
            commands.DrawMesh(mesh, localToWorld, material, 0, 0, properties);
            probeCamera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
            probeCamera.Render();
            retained = true;
            return target;
        }
        finally
        {
            probeCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
            probeCamera.targetTexture = null;
            Object.Destroy(cameraObject);
            commands.Release();
            RenderTexture.active = previous;
            if (!retained) { target.Release(); Object.Destroy(target); }
        }
    }
}
