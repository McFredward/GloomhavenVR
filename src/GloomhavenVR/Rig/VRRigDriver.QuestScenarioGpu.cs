using System;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    /// <summary>
    /// Observe the same actual native mesh with its original first material and
    /// a private white POSITION/UV-only shader. An ordinary disabled mono camera
    /// fits the native bounds; this is not a full scene or final XR picture, and
    /// explicit draws do not reproduce native per-object light/probe selection.
    /// Never replace a game material or bind this target during a head render.
    /// </summary>
    private static void QuestScenarioGpu(int sample, Renderer? renderer)
    {
        if (renderer == null || !SystemInfo.supportsAsyncGPUReadback)
        { QuestScenarioRecord("sample=" + sample + " gpu=unavailable renderer=" + (renderer != null) + " async=" + SystemInfo.supportsAsyncGPUReadback); return; }
        Mesh? mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null || mesh.vertexCount == 0 || !TryQuestScenarioPart(renderer, mesh, out int submesh, out Material original))
        { QuestScenarioRecord("sample=" + sample + " gpu=unavailable reason=no-mesh-or-material"); return; }
        Bounds bounds = renderer.bounds;
        Vector3 centre = bounds.center, size = bounds.size;
        if (!FiniteScenario(centre.x) || !FiniteScenario(centre.y) || !FiniteScenario(centre.z)
            || !FiniteScenario(size.x) || !FiniteScenario(size.y) || !FiniteScenario(size.z) || size.sqrMagnitude <= 0f)
        { QuestScenarioRecord("sample=" + sample + " gpu=unavailable reason=invalid-native-bounds"); return; }
        RenderTexture? native = null, control = null;
        Material? white = null;
        try
        {
            Shader? shader = BundleShaders.Resolve("GloomhavenVR/HeadUnlit", "Rig",
                "A bounded private scenario draw distinguishes native shader output from actual mesh geometry.",
                "The optional scenario GPU control is unavailable; native presentation remains unchanged.");
            if (shader == null || !shader.isSupported)
            { QuestScenarioRecord("sample=" + sample + " gpu=unavailable reason=no-control-shader"); return; }
            white = new Material(shader) { color = Color.white, mainTexture = Texture2D.whiteTexture };
            white.SetFloat("_Cull", (float)CullMode.Off);
            MaterialPropertyBlock? properties = null;
            if (renderer.HasPropertyBlock())
            {
                properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties, submesh);
                if (properties.isEmpty) renderer.GetPropertyBlock(properties);
            }
            native = RenderQuestScenarioProbe(renderer, mesh, original, properties, submesh);
            control = RenderQuestScenarioProbe(renderer, mesh, white, null, submesh);
            RenderTexture first = native, second = control;
            string nativeShader = original.shader != null ? Short(original.shader.name) : "none";
            int pending = 2;
            string nativePixels = "pending", controlPixels = "pending";
            void Complete(AsyncGPUReadbackRequest request, bool reference, RenderTexture target)
            {
                try
                {
                    string stats;
                    if (request.hasError) stats = "readback-failed";
                    else
                    {
                        var pixels = request.GetData<Color32>();
                        int max = 0, nonBlack = 0, alpha = 0;
                        foreach (Color32 pixel in pixels)
                        {
                            max = Math.Max(max, Math.Max(pixel.r, Math.Max(pixel.g, pixel.b)));
                            if (pixel.r > 1 || pixel.g > 1 || pixel.b > 1) nonBlack++;
                            if (pixel.a > 1) alpha++;
                        }
                        stats = "max:" + max + ",nonBlack:" + nonBlack + ",alpha:" + alpha;
                    }
                    if (reference) controlPixels = stats; else nativePixels = stats;
                }
                catch (Exception error)
                { if (reference) controlPixels = error.GetType().Name; else nativePixels = error.GetType().Name; }
                finally
                {
                    target.Release(); UnityEngine.Object.Destroy(target);
                    if (--pending == 0)
                        QuestScenarioRecord("sample=" + sample + " gpu=bounds-fit phase=update width=32 height=32 submesh=" + submesh + " pass=0 shader=" + nativeShader + " native="
                            + nativePixels + " whitePositionUvControl=" + controlPixels + " finalXrPixelsObserved=false nativePerObjectLightingObserved=false");
                }
            }
            AsyncGPUReadback.Request(first, 0, TextureFormat.RGBA32, request => Complete(request, false, first));
            native = null; // that request now owns its target
            AsyncGPUReadback.Request(second, 0, TextureFormat.RGBA32, request => Complete(request, true, second));
            control = null;
        }
        catch (Exception error) { QuestScenarioRecord("sample=" + sample + " gpu=unavailable detail=" + error.GetType().Name); }
        finally
        {
            if (native != null) { native.Release(); UnityEngine.Object.Destroy(native); }
            if (control != null) { control.Release(); UnityEngine.Object.Destroy(control); }
            if (white != null) UnityEngine.Object.Destroy(white);
        }
    }

    private static bool FiniteScenario(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool TryQuestScenarioPart(Renderer renderer, Mesh mesh, out int submesh, out Material material)
    {
        Material[] materials = renderer.sharedMaterials;
        for (int index = 0; index < mesh.subMeshCount && index < materials.Length; index++)
            if (materials[index] != null && mesh.GetTopology(index) == MeshTopology.Triangles && mesh.GetIndexCount(index) > 0)
            { submesh = index; material = materials[index]; return true; }
        submesh = -1; material = null!; return false;
    }

    private static RenderTexture RenderQuestScenarioProbe(Renderer renderer, Mesh mesh, Material material, MaterialPropertyBlock? properties, int submesh)
    {
        var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "GloomhavenVR.QuestScenarioProbe", filterMode = FilterMode.Point };
        var cameraObject = new GameObject("GloomhavenVR.QuestScenarioProbe") { hideFlags = HideFlags.HideAndDontSave };
        Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        UnityEngine.XR.XRDevice.DisableAutoXRCameraTracking(camera, true);
        camera.cullingMask = 0; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
        camera.allowHDR = false; camera.allowMSAA = false; camera.renderingPath = RenderingPath.Forward;
        Bounds bounds = renderer.bounds;
        float radius = Mathf.Max(.05f, bounds.extents.magnitude);
        camera.transform.position = bounds.center + new Vector3(.25f, 1f, -.35f).normalized * radius * 3f;
        camera.transform.LookAt(bounds.center);
        camera.orthographic = true; camera.orthographicSize = radius * 1.1f;
        camera.nearClipPlane = .01f; camera.farClipPlane = radius * 8f + 1f;
        camera.targetTexture = target;
        RenderTexture? previous = RenderTexture.active;
        var commands = new CommandBuffer { name = "GloomhavenVR.QuestScenarioProbe" };
        bool retained = false;
        try
        {
            if (!target.Create()) throw new InvalidOperationException("Scenario GPU target unavailable.");
            commands.DrawMesh(mesh, renderer.localToWorldMatrix, material, submesh, 0, properties);
            camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
            camera.Render(); retained = true; return target;
        }
        finally
        {
            camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, commands); camera.targetTexture = null;
            UnityEngine.Object.Destroy(cameraObject); commands.Release(); RenderTexture.active = previous;
            if (!retained) { target.Release(); UnityEngine.Object.Destroy(target); }
        }
    }
}
