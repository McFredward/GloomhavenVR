using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
        for (int index = 0; index < _captured.Count && index < 8; index++)
        {
            CapturedCamera capture = _captured[index];
            UnityEngine.Debug.Log("[Quest startup] presentation menu-producer scene=" + menu.name
                + " sample=" + sample + " ui=" + capture.IsUi + " " + QuestCameraFacts(capture.Camera));
        }
        if (_visible)
        {
            QuestProbeCapture(_uiRt, menu.name, sample, "glass");
            QuestProbeCapture(_rt, menu.name, sample, "background");
        }
    }

    private static string QuestCameraFacts(Camera? camera) => camera == null ? "none"
        : camera.name + ",enabled=" + camera.isActiveAndEnabled + ",mask=" + camera.cullingMask
            + ",depth=" + camera.depth + ",clear=" + camera.clearFlags
            + ",eye=" + camera.stereoTargetEye + ",target=" + (camera.targetTexture == null ? "backbuffer"
                : camera.targetTexture.name + "," + camera.targetTexture.width + "x" + camera.targetTexture.height);

    private static string QuestConsumerFacts(Renderer? renderer, Material? material) => renderer == null ? "none"
        : renderer.name + ",active=" + renderer.gameObject.activeInHierarchy + ",enabled=" + renderer.enabled
            + ",layer=" + renderer.gameObject.layer + ",bounds=" + renderer.bounds
            + ",shader=" + (material == null ? "none" : material.shader.name + ",supported="
                + material.shader.isSupported + ",queue=" + material.renderQueue)
            + ",texture=" + (material == null || material.mainTexture == null ? "none" : material.mainTexture.name);

    private static void QuestProbeCapture(RenderTexture? target, string scene, int sampleIndex, string role)
    {
        if (target == null || !target.IsCreated()) return;
        // Only four tiny probes per menu opening. Unsupported async readback is
        // reported, never replaced with a main-thread GPU wait during startup.
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            UnityEngine.Debug.Log("[Quest startup] presentation menu-pixels scene=" + scene + " sample=" + sampleIndex
                + " role=" + role + " readback=unsupported target=" + target.name);
            return;
        }
        RenderTexture probe = new(16, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture? previous = RenderTexture.active;
        try
        {
            if (!probe.Create()) throw new System.InvalidOperationException("Menu evidence target could not be created.");
            Graphics.Blit(target, probe);
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
                    int rgbMax = 0, alphaMax = 0;
                    for (int index = 0; index < pixels.Length; index++)
                    {
                        Color32 pixel = pixels[index];
                        rgbMax = System.Math.Max(rgbMax, System.Math.Max(pixel.r, System.Math.Max(pixel.g, pixel.b)));
                        alphaMax = System.Math.Max(alphaMax, pixel.a);
                    }
                    UnityEngine.Debug.Log("[Quest startup] presentation menu-pixels scene=" + scene + " sample=" + sampleIndex
                        + " role=" + role + " samples=" + pixels.Length + " rgbMax=" + rgbMax + " alphaMax=" + alphaMax);
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
}
