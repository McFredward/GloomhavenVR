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
            // The old48x32 full-eye probe resolves the9.5degree loading disc to
            // approximately three pixels on Quest. A perfectly good narrow ring
            // can miss all of those sample centres while it rotates. Fit this
            // exact mesh to the private target instead, so zero no longer
            // masquerades as missing source artwork because of undersampling.
            Matrix4x4 tightProjection = CropQuestProbeToRenderer(renderer, view, projection);
            target = RenderQuestConsumerProbe(renderer, renderer.sharedMaterial, view, tightProjection);
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
                        + " framing=exact-consumer-bounds width=48 height=32 finalXrPixelsObserved=false");
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

    private static Matrix4x4 CropQuestProbeToRenderer(Renderer renderer, Matrix4x4 view, Matrix4x4 projection)
    {
        Bounds bounds = renderer.bounds;
        Vector3 centre = bounds.center, extent = bounds.extents;
        Matrix4x4 worldToClip = projection * view;
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 world = centre + new Vector3((corner & 1) == 0 ? -extent.x : extent.x,
                (corner & 2) == 0 ? -extent.y : extent.y, (corner & 4) == 0 ? -extent.z : extent.z);
            Vector4 clip = worldToClip * new Vector4(world.x, world.y, world.z, 1f);
            if (clip.w <= .0001f) return projection;
            float x = clip.x / clip.w, y = clip.y / clip.w;
            left = Mathf.Min(left, x); right = Mathf.Max(right, x);
            bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y);
        }
        float width = right - left, height = top - bottom;
        if (width < .0001f || height < .0001f) return projection;
        var crop = Matrix4x4.identity;
        crop.m00 = 1.8f / width; crop.m03 = -.9f * (right + left) / width;
        crop.m11 = 1.8f / height; crop.m13 = -.9f * (top + bottom) / height;
        return crop * projection;
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
        Material? unmasked = null;
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
            Material material = graphic.materialForRendering;
            // A Mask gives an otherwise visible native Image a derived Equal
            // stencil material. Its parent mask does not draw into this private
            // eligibility target, whose stencil starts empty. Replaying that
            // derived comparison here rejects real menu artwork indefinitely.
            // The private source-only draw therefore uses a private comparison;
            // real CanvasRenderer masking, inherited alpha/cull, the actual
            // capture and both actual consumer pixel proofs remain unchanged.
            if (material.HasProperty("_StencilComp")
                && material.GetInt("_StencilComp") != (int)CompareFunction.Always
                && material.GetInt("_StencilComp") != (int)CompareFunction.Disabled)
            {
                unmasked = new Material(material) { name = "GloomhavenVR.QuestMenuEligibility" };
                unmasked.SetInt("_StencilComp", (int)CompareFunction.Always);
                unmasked.SetInt("_StencilWriteMask", 0);
                material = unmasked;
            }
            return RenderQuestMenuMeshProbe(mesh, graphic.transform.localToWorldMatrix,
                material, properties, source.worldToCameraMatrix, source.projectionMatrix);
        }
        finally { Object.Destroy(mesh); if (unmasked != null) Object.Destroy(unmasked); }
    }

    private static RenderTexture? RenderQuestMenuMeshProbe(Mesh mesh, Matrix4x4 localToWorld,
        Material material, MaterialPropertyBlock? properties, Matrix4x4 view, Matrix4x4 projection)
    {
        var target = new RenderTexture(48, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "GloomhavenVR.QuestMenuConsumerProbe", filterMode = FilterMode.Point,
                // A managed callback capture is not a Unity asset-reachability
                // edge. Native startup can collect unused assets while the
                // async readback is outstanding; this owned target must live
                // until that callback explicitly releases/destroys it.
                hideFlags = HideFlags.HideAndDontSave };
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
