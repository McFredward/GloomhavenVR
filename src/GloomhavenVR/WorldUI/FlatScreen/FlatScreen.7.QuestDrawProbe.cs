using UnityEngine;
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
                        Debug.LogWarning("[Quest startup] presentation menu-consumer-draw scene=" + scene
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
                    Debug.Log("[Quest startup] presentation menu-consumer-draw scene=" + scene
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
            Debug.LogWarning("[Quest startup] presentation menu-consumer-draw scene=" + scene
                + " role=" + role + " readback=unavailable detail=" + error.Message);
        }
        finally
        {
            if (target != null) { target.Release(); Object.Destroy(target); }
        }
    }

    private static RenderTexture? RenderQuestConsumerProbe(Renderer? renderer, Material? material,
        Matrix4x4 view, Matrix4x4 projection)
    {
        Mesh? mesh = renderer != null ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (mesh == null || material == null) return null;
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
            MaterialPropertyBlock? properties = null;
            if (renderer!.HasPropertyBlock())
            {
                properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
            }
            commands.DrawMesh(mesh, renderer.localToWorldMatrix, material, 0, 0, properties);
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
