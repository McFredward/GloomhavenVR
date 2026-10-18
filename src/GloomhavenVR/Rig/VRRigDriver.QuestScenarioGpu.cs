using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    /// <summary>
    /// Observe every actual native material part through an ordinary private
    /// MeshRenderer and camera, using the source's active native light inputs
    /// (including DontSave room-light clones), probe anchor, lightmaps and MPBs.
    /// The old CommandBuffer first-part draw had no per-object lighting and
    /// sampled only the floor rim: its black Amp_Basic result could not diagnose
    /// a missing floor. This bounded comparison is unshadowed and mono, never a
    /// final XR picture or proof of the source's multi-instance draw variant.
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
        RenderTexture? native = null, control = null, emissive = null;
        Material[]? emissiveSlots = null;
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
            List<Light> lights = QuestScenarioLights(renderer.gameObject.layer, out int candidates);
            Vector3 probePosition = renderer.probeAnchor != null ? renderer.probeAnchor.position : renderer.bounds.center;
            LightProbes.GetInterpolatedProbe(probePosition, renderer, out SphericalHarmonicsL2 sourceProbe);
            string lighting = " pixelLights=" + QualitySettings.pixelLightCount + " probeSH0="
                + Number(sourceProbe[0, 0]) + "," + Number(sourceProbe[1, 0]) + "," + Number(sourceProbe[2, 0]) + " lightTypes=";
            for (int index = 0; index < Math.Min(lights.Count, 3); index++)
            {
                Light light = lights[index];
                lighting += light.type + ":" + Number(light.intensity) + "," + Vector(light.transform.position)
                    + ",r" + Number(light.range) + ",rgb" + Number(light.color.r) + "," + Number(light.color.g) + "," + Number(light.color.b)
                    + ",mode=" + light.renderMode + ",shadows=" + light.shadows + ";";
            }
            string context = " sourceLayer=" + renderer.gameObject.layer + " copiedLights=" + lights.Count + "/" + candidates
                + " probes=" + renderer.lightProbeUsage + " api=" + SystemInfo.graphicsDeviceType + " tier=" + Graphics.activeTier
                + " space=" + QualitySettings.activeColorSpace + lighting;
            int parts = mesh.subMeshCount;
            native = RenderQuestScenarioObserved(renderer, mesh, null, lights);
            control = RenderQuestScenarioObserved(renderer, mesh, white, lights);
            // Amp_Basic's ORIGINAL explicit emissive switch can isolate native
            // texture/program output from scene illumination without changing a
            // live renderer, light or original material. This is private Debug
            // evidence, never a material replacement or a proposed visual fix.
            emissiveSlots = QuestScenarioEmissiveSlots(renderer);
            if (emissiveSlots != null) emissive = RenderQuestScenarioObserved(renderer, mesh, null, lights, emissiveSlots);
            RenderTexture first = native, second = control;
            RenderTexture? third = emissive;
            string nativeShader = original.shader != null ? Short(original.shader.name) : "none";
            int pending = third != null ? 3 : 2;
            string nativePixels = "pending", controlPixels = "pending", emissivePixels = third != null ? "pending" : "not-qualified";
            void Complete(AsyncGPUReadbackRequest request, int arm, RenderTexture target)
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
                    if (arm == 2) emissivePixels = stats; else if (arm == 1) controlPixels = stats; else nativePixels = stats;
                }
                catch (Exception error)
                { if (arm == 2) emissivePixels = error.GetType().Name; else if (arm == 1) controlPixels = error.GetType().Name; else nativePixels = error.GetType().Name; }
                finally
                {
                    target.Release(); UnityEngine.Object.Destroy(target);
                    if (--pending == 0)
                        QuestScenarioRecord("sample=" + sample + " gpu=bounds-fit phase=update probe=private-unshadowed-nonXR width=32 height=32 parts=" + parts + " shader=" + nativeShader + " native="
                            + nativePixels + " nativeEmission1=" + emissivePixels + " whitePositionUvControl=" + controlPixels + context
                            + " nativePerObjectLightingObserved=private-copy unshadowed=true sourceInstancingVariantObserved=false finalXrPixelsObserved=false");
                }
            }
            AsyncGPUReadback.Request(first, 0, TextureFormat.RGBA32, request => Complete(request, 0, first));
            native = null; // that request now owns its target
            AsyncGPUReadback.Request(second, 0, TextureFormat.RGBA32, request => Complete(request, 1, second));
            control = null;
            if (third != null)
            {
                AsyncGPUReadback.Request(third, 0, TextureFormat.RGBA32, request => Complete(request, 2, third));
                emissive = null;
            }
        }
        catch (Exception error) { QuestScenarioRecord("sample=" + sample + " gpu=unavailable detail=" + error.GetType().Name); }
        finally
        {
            if (native != null) { native.Release(); UnityEngine.Object.Destroy(native); }
            if (control != null) { control.Release(); UnityEngine.Object.Destroy(control); }
            if (emissive != null) { emissive.Release(); UnityEngine.Object.Destroy(emissive); }
            if (white != null) UnityEngine.Object.Destroy(white);
            if (emissiveSlots != null) foreach (Material material in emissiveSlots) if (material != null) UnityEngine.Object.Destroy(material);
        }
    }

    private static bool FiniteScenario(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static List<Light> QuestScenarioLights(int sourceLayer, out int candidates)
    {
        var lights = new List<Light>(); candidates = 0;
        // FindObjectsOfType omits DynamicAmbience's DontSave room-light clones.
        // Loaded scene ownership distinguishes those real lights from assets;
        // no native light is ever enabled, moved, re-layered or reconfigured.
        foreach (Light light in Resources.FindObjectsOfTypeAll<Light>())
        {
            if (light == null || !light.isActiveAndEnabled || !light.gameObject.scene.IsValid()
                || !light.gameObject.scene.isLoaded || (light.cullingMask & (1 << sourceLayer)) == 0
                || light.bakingOutput.isBaked) continue;
            candidates++;
            if (lights.Count < 64) lights.Add(light);
        }
        return lights;
    }

    private static Material[]? QuestScenarioEmissiveSlots(Renderer source)
    {
        // A native MPB can override the private material switch. Do not change
        // its properties or claim an emissive arm we cannot actually select.
        if (source.HasPropertyBlock()) return null;
        Material[] originals = source.sharedMaterials;
        foreach (Material material in originals)
            if (material == null || material.shader == null || material.shader.name != "Amp_Basic"
                || !material.HasProperty("_EmissionMap")) return null;
        var copies = new Material[originals.Length];
        try
        {
            for (int index = 0; index < originals.Length; index++)
            { copies[index] = new Material(originals[index]); copies[index].SetFloat("_EmissionMap", 1f); }
            return copies;
        }
        catch
        {
            foreach (Material material in copies) if (material != null) UnityEngine.Object.Destroy(material);
            throw;
        }
    }

    private static RenderTexture RenderQuestScenarioObserved(Renderer source, Mesh mesh, Material? control, List<Light> lights,
        Material[]? materialOverrides = null)
    {
        const int privateLayer = 31;
        var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { name = "GloomhavenVR.QuestScenarioLitProbe", filterMode = FilterMode.Point };
        var owner = new GameObject("GloomhavenVR.QuestScenarioLitProbe") { hideFlags = HideFlags.HideAndDontSave };
        var copiedLights = new List<Light>();
        MeshRenderer? copy = null;
        Camera? camera = null;
        RenderTexture? previous = RenderTexture.active;
        bool retained = false;
        try
        {
            var surface = new GameObject("Observed native mesh") { layer = privateLayer, hideFlags = HideFlags.HideAndDontSave };
            surface.transform.SetParent(owner.transform, false);
            surface.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            surface.transform.localScale = source.transform.lossyScale;
            Matrix4x4 pose = source.localToWorldMatrix, copied = surface.transform.localToWorldMatrix;
            for (int index = 0; index < 16; index++)
                if (Mathf.Abs(pose[index] - copied[index]) > .001f)
                    throw new InvalidOperationException("Source mesh pose contains unsupported shear.");
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            copy = surface.AddComponent<MeshRenderer>(); copy.enabled = false;
            Material[] slots = materialOverrides ?? source.sharedMaterials;
            if (control != null)
            {
                slots = new Material[mesh.subMeshCount];
                for (int slot = 0; slot < slots.Length; slot++) slots[slot] = control;
            }
            copy.sharedMaterials = slots;
            copy.shadowCastingMode = ShadowCastingMode.Off; copy.receiveShadows = false;
            copy.lightProbeUsage = source.lightProbeUsage;
            copy.reflectionProbeUsage = source.reflectionProbeUsage;
            copy.renderingLayerMask = source.renderingLayerMask;
            copy.probeAnchor = source.probeAnchor;
            copy.lightProbeProxyVolumeOverride = source.lightProbeProxyVolumeOverride;
            copy.lightmapIndex = source.lightmapIndex; copy.lightmapScaleOffset = source.lightmapScaleOffset;
            copy.realtimeLightmapIndex = source.realtimeLightmapIndex;
            copy.realtimeLightmapScaleOffset = source.realtimeLightmapScaleOffset;
            if (control == null && source.HasPropertyBlock())
            {
                var block = new MaterialPropertyBlock(); source.GetPropertyBlock(block); copy.SetPropertyBlock(block);
                for (int slot = 0; slot < slots.Length; slot++)
                { source.GetPropertyBlock(block, slot); copy.SetPropertyBlock(block, slot); }
            }
            foreach (Light original in lights)
            {
                if (original == null) continue;
                var lightObject = new GameObject("Observed native light") { hideFlags = HideFlags.HideAndDontSave };
                lightObject.transform.SetParent(owner.transform, false);
                lightObject.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                Light light = lightObject.AddComponent<Light>(); light.enabled = false; copiedLights.Add(light);
                light.type = original.type; light.color = original.color; light.intensity = original.intensity;
                light.range = original.range; light.spotAngle = original.spotAngle; light.innerSpotAngle = original.innerSpotAngle;
                light.renderMode = original.renderMode; light.cookie = original.cookie; light.cookieSize = original.cookieSize;
                light.colorTemperature = original.colorTemperature; light.useColorTemperature = original.useColorTemperature;
                light.bounceIntensity = original.bounceIntensity;
                light.shadows = LightShadows.None; light.cullingMask = 1 << privateLayer;
            }
            var cameraObject = new GameObject("Observed native camera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(owner.transform, false);
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            UnityEngine.XR.XRDevice.DisableAutoXRCameraTracking(camera, true);
            camera.cullingMask = 1 << privateLayer; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear; camera.allowHDR = false; camera.allowMSAA = false;
            camera.renderingPath = RenderingPath.Forward; camera.useOcclusionCulling = false;
            Bounds bounds = source.bounds; float radius = Mathf.Max(.05f, bounds.extents.magnitude);
            camera.transform.position = bounds.center + new Vector3(.25f, 1f, -.35f).normalized * radius * 3f;
            camera.transform.LookAt(bounds.center); camera.orthographic = true; camera.orthographicSize = radius * 1.1f;
            camera.nearClipPlane = .01f; camera.farClipPlane = radius * 8f + 1f; camera.targetTexture = target;
            if (!target.Create()) throw new InvalidOperationException("Scenario lit GPU target unavailable.");
            // These private objects exist only inside this synchronous manual
            // render. The head can admit layer31, so disable both surface and
            // light copies in finally BEFORE Unity resumes ordinary rendering.
            copy.enabled = true;
            foreach (Light light in copiedLights) light.enabled = true;
            camera.Render(); retained = true; return target;
        }
        finally
        {
            if (copy != null) copy.enabled = false;
            foreach (Light light in copiedLights) if (light != null) light.enabled = false;
            if (camera != null) camera.targetTexture = null;
            UnityEngine.Object.Destroy(owner); RenderTexture.active = previous;
            if (!retained) { target.Release(); UnityEngine.Object.Destroy(target); }
        }
    }

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
