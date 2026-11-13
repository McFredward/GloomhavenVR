using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private int _questEvidenceSceneHandle = -1;
    private float _questEvidenceSceneStart;
    private int _questEvidenceSamples;

    /// <summary>
    /// B624 reached native MainMenu and played music while the HMD stayed black.
    /// Keep two bounded menu-handover records even at the ordinary log level,
    /// separating a hidden screen, missing producer, black capture and consumer
    /// material failure. Detailed repeating camera streams remain at Debug.
    /// </summary>
    private void TickQuestPresentationEvidence(bool wanted)
    {
        if (!QuestStandalonePlatform.Enabled || _questEvidenceSamples >= 2) return;
        Scene menu = SceneManager.GetSceneByName("MainMenu");
        if (!menu.IsValid() || !menu.isLoaded)
            menu = SceneManager.GetSceneByName("MainMenu_gamepad");
        if (!menu.IsValid() || !menu.isLoaded) return;
        if (menu.handle != _questEvidenceSceneHandle)
        {
            _questEvidenceSceneHandle = menu.handle;
            _questEvidenceSceneStart = Time.realtimeSinceStartup;
            _questEvidenceSamples = 0;
        }
        float age = Time.realtimeSinceStartup - _questEvidenceSceneStart;
        if (_questEvidenceSamples >= 2 || age < (_questEvidenceSamples == 0 ? 4f : 12f)) return;
        int sample = ++_questEvidenceSamples;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        UnityEngine.Debug.Log("[Quest startup] presentation menu-handover scene=" + menu.name
            + " sample=" + sample + " mode=" + VRModeStateMachine.CurrentMode
            + " wanted=" + wanted + " visible=" + _visible
            + " loadingSuppressed=" + LoadingIndicator.FlatScreenSuppressed
            + " mapRoom=" + MapRoom.MapRoomDriver.Active + " nativeVideoWindow=" + NativeVideoWindow.Visible
            + " split=" + _splitRouting + " capturedCameras=" + _captured.Count
            + " head=" + QuestCameraFacts(head)
            + " glass=" + QuestConsumerFacts(_quadRenderer, _glassMaterial)
            + " background=" + QuestConsumerFacts(_backRenderer, _screenMaterial));
        if (QuestStandalonePlatform.DebugLogging)
        {
            QuestDrawEvidence(menu.name, sample, "glass", _quadRenderer, _glassMaterial);
            QuestDrawEvidence(menu.name, sample, "background", _backRenderer, _screenMaterial);
        }
        for (int index = 0; index < _captured.Count && index < 8; index++)
        {
            CapturedCamera capture = _captured[index];
            UnityEngine.Debug.Log("[Quest startup] presentation menu-producer scene=" + menu.name
                + " sample=" + sample + " ui=" + capture.IsUi + " " + QuestCameraFacts(capture.Camera));
        }
        try { if (QuestStandalonePlatform.DebugLogging) QuestCanvasEvidence(menu.name, sample); }
        catch (System.Exception error)
        {
            UnityEngine.Debug.LogWarning("[Quest startup] presentation menu-canvas evidence=unavailable detail=" + error.Message);
        }
        if (_visible)
        {
            QuestProbeCapture(_uiRt, menu.name, sample, "glass");
            QuestProbeCapture(_rt, menu.name, sample, "background-left");
            RenderTexture? right = _screenMaterial == null ? null : _screenMaterial.HasProperty("_RightTex")
                ? _screenMaterial.GetTexture("_RightTex") as RenderTexture : _screenMaterial.mainTexture as RenderTexture;
            if (right != null && right != _rt) QuestProbeCapture(right, menu.name, sample, "background-right");
            if (sample == 2 && QuestStandalonePlatform.DebugLogging && head != null)
            {
                QuestProbeConsumer(_quadRenderer, _glassMaterial, head, menu.name, "glass");
                QuestProbeConsumer(_backRenderer, _screenMaterial, head, menu.name, "background");
            }
        }
    }

    private static string QuestCameraFacts(Camera? camera) => camera == null ? "none"
        : camera.name + ",enabled=" + camera.isActiveAndEnabled + ",mask=" + camera.cullingMask
            + ",depth=" + camera.depth + ",clear=" + camera.clearFlags
            + ",pose=" + camera.transform.position + ",near=" + camera.nearClipPlane + ",far=" + camera.farClipPlane
            + ",path=" + camera.actualRenderingPath + ",ortho=" + camera.orthographic
            + ",orthoSize=" + camera.orthographicSize + ",fov=" + camera.fieldOfView
            + ",aspect=" + camera.aspect + ",rect=" + camera.pixelRect
            + ",eye=" + camera.stereoTargetEye + ",target=" + (camera.targetTexture == null ? "backbuffer"
                : camera.targetTexture.name + "," + camera.targetTexture.width + "x" + camera.targetTexture.height);

    private static string QuestConsumerFacts(Renderer? renderer, Material? material) => renderer == null ? "none"
        : renderer.name + ",active=" + renderer.gameObject.activeInHierarchy + ",enabled=" + renderer.enabled
            + ",layer=" + renderer.gameObject.layer + ",bounds=" + renderer.bounds
            + ",scale=" + renderer.transform.lossyScale
            + ",shader=" + (material == null ? "none" : material.shader.name + ",supported="
                + material.shader.isSupported + ",queue=" + material.renderQueue)
            + ",texture=" + (material == null || material.mainTexture == null ? "none" : material.mainTexture.name);

    private static void QuestDrawEvidence(string scene, int sample, string role, Renderer? renderer, Material? material)
    {
        if (renderer == null || material == null) return;
        Mesh? mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
        string[] keywords = material.shaderKeywords;
        // Two settled-menu samples only, with bounded keyword text. CPU mesh and
        // material facts do not prove the final GPU vertex bindings or eye pixels.
        UnityEngine.Debug.Log("[Quest startup] presentation menu-draw scene=" + scene + " sample=" + sample
            + " role=" + role + " shader=" + material.shader.name + " instancing=" + material.enableInstancing
            + " rendererBlock=" + renderer.HasPropertyBlock() + " batchingStatic=" + renderer.isPartOfStaticBatch
            + " globalStereoInstancing=" + Shader.IsKeywordEnabled("STEREO_INSTANCING_ON")
            + " globalStereoMultiview=" + Shader.IsKeywordEnabled("STEREO_MULTIVIEW_ON")
            + " globalSinglePass=" + Shader.IsKeywordEnabled("UNITY_SINGLE_PASS_STEREO")
            + " materialKeywords=" + string.Join(",", keywords, 0, System.Math.Min(keywords.Length, 16))
            + " mesh=" + (mesh == null ? "none" : mesh.name + ",vertices=" + mesh.vertexCount
                + ",stride=" + mesh.GetVertexBufferStride(0) + ",indexFormat=" + mesh.indexFormat)
            + " texture=" + (material.mainTexture == null ? "none" : material.mainTexture.name));
    }

    private static void QuestProbeCapture(RenderTexture? target, string scene, int sampleIndex, string role)
    {
        if (target == null || !target.IsCreated()) return;
        string targetName = target.name;
        // At most six tiny probes per launch. Unsupported async readback is
        // reported, never replaced with a main-thread GPU wait during startup.
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            UnityEngine.Debug.Log("[Quest startup] presentation menu-pixels scene=" + scene + " sample=" + sampleIndex
                + " role=" + role + " readback=unsupported target=" + targetName);
            return;
        }
        // One complete RGBA8 thumbnail, including its prefix, must fit Android's
        // roughly 1 KiB logcat payload as well as the startup log's bounded menu
        // reserve. A 32x16 base64 record was truncated by logcat and consumed the
        // reserve before the final consumer diagnostics could be retained.
        RenderTexture probe = new(16, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture? previous = RenderTexture.active;
        try
        {
            if (!probe.Create()) throw new System.InvalidOperationException("Menu evidence target could not be created.");
            QuestTextureCopy.Copy(target, probe, Vector2.one, Vector2.zero);
            AsyncGPUReadback.Request(probe, 0, TextureFormat.RGBA32, request =>
            {
                try
                {
                    if (request.hasError)
                    {
                        UnityEngine.Debug.LogWarning("[Quest startup] presentation menu-pixels scene=" + scene
                            + " sample=" + sampleIndex + " role=" + role + " readback=failed");
                        return;
                    }
                    var pixels = request.GetData<Color32>();
                    int rgbMax = 0, alphaMax = 0, nonBlack = 0, alphaPixels = 0;
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color32 pixel = pixels[index];
                        rgbMax = System.Math.Max(rgbMax, System.Math.Max(pixel.r, System.Math.Max(pixel.g, pixel.b)));
                        alphaMax = System.Math.Max(alphaMax, pixel.a);
                        if (pixel.r > 1 || pixel.g > 1 || pixel.b > 1) nonBlack++;
                        if (pixel.a > 1) alphaPixels++;
                    }
                    UnityEngine.Debug.Log("[Quest startup] presentation menu-pixels scene=" + scene + " sample=" + sampleIndex
                        + " role=" + role + " target=" + targetName + " copyRecipe=" + QuestTextureCopy.Recipe
                        + " samples=" + pixels.Length + " rgbMax=" + rgbMax + " alphaMax=" + alphaMax
                        + " nonBlack=" + nonBlack + " alphaPixels=" + alphaPixels);
                    if (sampleIndex == 2)
                    {
                        // This complete 16x8 RGBA8 thumbnail also fits a single
                        // logcat record. No full-resolution readback or recurring scan.
                        byte[] rgba = new byte[pixels.Length * 4];
                        for (int index = 0; index < pixels.Length; index++)
                        {
                            Color32 pixel = pixels[index]; int offset = index * 4;
                            rgba[offset] = pixel.r; rgba[offset + 1] = pixel.g;
                            rgba[offset + 2] = pixel.b; rgba[offset + 3] = pixel.a;
                        }
                        UnityEngine.Debug.Log("[Quest startup] presentation menu-thumbnail scene=" + scene
                            + " sample=" + sampleIndex + " role=" + role + " target=" + targetName
                            + " width=16 height=8 format=RGBA8-linear bottomRowFirst=true base64="
                            + System.Convert.ToBase64String(rgba));
                    }
                    if (role == "glass" && rgbMax <= 1 && sampleIndex == 2)
                        VRLog.Alert("WorldUI", "Quest native menu UI capture remains black after handover; retain the hardware capture for diagnosis.");
                }
                finally { probe.Release(); Object.Destroy(probe); }
            });
        }
        catch (System.Exception error)
        {
            probe.Release(); Object.Destroy(probe);
            UnityEngine.Debug.LogWarning("[Quest startup] presentation menu-pixels scene=" + scene + " role=" + role
                + " readback=unavailable detail=" + error.Message);
        }
        finally { RenderTexture.active = previous; }
    }

    private static void QuestCanvasEvidence(string scene, int sample)
    {
        int canvasCount = 0, graphicCount = 0;
        Canvas[] canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        // The persistent loading/tooltip canvases were created before MainMenu.
        // Spend the small widget budget on the actual menu before those owners.
        for (int pass = 0; pass < 2; pass++)
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null || !canvas.isActiveAndEnabled || !canvas.gameObject.scene.IsValid()
                || !canvas.gameObject.scene.isLoaded || canvas.renderMode != RenderMode.ScreenSpaceCamera) continue;
            if ((canvas.gameObject.scene.name == scene) != (pass == 0)) continue;
            if (canvasCount >= 6) break;
            canvasCount++;
            UnityEngine.Debug.Log("[Quest startup] presentation menu-canvas scene=" + scene + " sample=" + sample
                + " name=" + canvas.name + " mode=" + canvas.renderMode + " order=" + canvas.sortingOrder
                + " plane=" + canvas.planeDistance + " camera=" + QuestCameraFacts(canvas.worldCamera));
            // Native graphics can raycast and play sounds while their inherited
            // alpha is zero, their renderer is culled, or their material is blank.
            // Observe those states; never override native fades or interaction.
            // Capture135518 exhausted the shared widget budget in the hidden
            // main-options canvas before reaching the active first-run EULA.
            // Keep a per-canvas share and a bounded total at Debug only.
            int canvasGraphics = 0;
            foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(false))
            {
                if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvas != canvas) continue;
                if (graphicCount >= 24 || canvasGraphics >= 8) break;
                graphicCount++; canvasGraphics++;
                CanvasRenderer renderer = graphic.canvasRenderer;
                Material? material = renderer.materialCount > 0 ? renderer.GetMaterial(0) : graphic.material;
                Texture? texture = graphic.mainTexture;
                Vector2 pixel = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,
                    graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));
                UnityEngine.Debug.Log("[Quest startup] presentation menu-widget scene=" + scene + " sample=" + sample
                    + " name=" + graphic.name + " type=" + graphic.GetType().Name + " canvas=" + canvas.name
                    + " alpha=" + renderer.GetInheritedAlpha() + " tint=" + graphic.color + " culled=" + renderer.cull
                    + " rect=" + graphic.rectTransform.rect + " centerPixel=" + pixel
                    + " shader=" + (material == null || material.shader == null ? "none" : material.shader.name + ",supported=" + material.shader.isSupported)
                    + " texture=" + (texture == null ? "none" : texture.name + "," + texture.width + "x" + texture.height));
            }
        }
    }
}
