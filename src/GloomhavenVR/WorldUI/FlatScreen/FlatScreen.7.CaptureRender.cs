using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private bool _captureRenderHooked;
    private bool _captureBoundaryHooked;
    private bool _captureBoundaryFaultNoted;
    private Camera? _captureMaskedCamera;
    private int _captureMask;

    /// <summary>
    /// Frame638 captures MainMenuVideo with maskFFFFFFFF. Its stereo mirror already
    /// excludes the mod layer, but the original did not: it could draw the floating
    /// screen and hands back into the very menu RT that the screen samples. Native
    /// video preparation/absent frames can expose that feedback, depending on pose.
    /// Exclude only mod visuals for the captured camera's actual render, then restore
    /// its CURRENT native mask. Native UI/geometry and the head draw stay unchanged.
    /// A late native target/stereo write is also corrected at this final boundary.
    /// The headset report motivates the repair; only hardware can confirm its picture.
    /// </summary>
    private void SetCaptureRenderGuard(bool enabled)
    {
        if (_captureRenderHooked == enabled)
            return;
        _captureRenderHooked = enabled;
        if (enabled)
        {
            try
            {
                ScenarioCameraCullBoundary.Install();
                ScenarioCameraCullBoundary.Subscribe(OnCapturePreCull);
                _captureBoundaryHooked = true;
            }
            catch (System.Exception error)
            {
                // Capture stays visible if the optional final seam cannot be installed.
                // The ordinary callback still closes Update/LateUpdate writes.
                Camera.onPreCull += OnCapturePreCull;
                if (!_captureBoundaryFaultNoted)
                {
                    _captureBoundaryFaultNoted = true;
                    VRLog.Warn("WorldUI", "Screen capture final cull guard unavailable (" + error.GetType().Name
                        + "); ordinary render callback retained.");
                }
            }
            Camera.onPostRender += OnCapturePostRender;
            RenderPipelineManager.beginCameraRendering += OnCaptureBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnCaptureEndCameraRendering;
        }
        else
        {
            if (_captureBoundaryHooked)
                ScenarioCameraCullBoundary.Unsubscribe(OnCapturePreCull);
            else
                Camera.onPreCull -= OnCapturePreCull;
            _captureBoundaryHooked = false;
            Camera.onPostRender -= OnCapturePostRender;
            RenderPipelineManager.beginCameraRendering -= OnCaptureBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnCaptureEndCameraRendering;
            RestoreCaptureMask();
        }
    }

    private void OnCaptureBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline != null)
            OnCapturePreCull(camera);
    }

    private void OnCaptureEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (GraphicsSettings.currentRenderPipeline != null)
            OnCapturePostRender(camera);
    }

    private void OnCapturePreCull(Camera camera)
    {
        RestoreCaptureMask(); // Interrupted renders cannot leave native projection writers blinded.
        if (!VRSession.IsRunning || camera == null || camera == Rig.VRRigDriver.HeadCamera)
            return;
        if (!CapturedSet.TryGetValue(camera, out CapturedCamera captured))
        {
            // A native camera created/enabled after the Update census is absent
            // from CapturedSet, so its first render would bypass every guard:
            // target null, stereo Both and a mask containing the floating screen.
            // Adopt through the same stack policy now, before native culling. This
            // rare discovery also syncs stack clears, split routing and stereo
            // mirrors; later renders use only the existing dictionary lookup.
            // Foreign preview RTs and disabled/manual cameras remain independent.
            if (!_captureRenderHooked || _rt == null || !camera.isActiveAndEnabled || camera.targetTexture != null)
                return;
            CaptureStack();
            if (!CapturedSet.TryGetValue(camera, out captured)) return;
            if (VRLog.WantsDebug)
                VRLog.Info("WorldUI", $"FlatScreen first-render discovery: '{camera.name}' — " +
                    "late native backbuffer camera joined the capture before culling.");
        }
        VRCameraPolicy.ExcludeStereo(camera, "screen capture render");
        RenderTexture? target = TargetFor(captured);
        if (target != null && camera.targetTexture != target)
            camera.targetTexture = target;
        int mask = camera.cullingMask;
        // An exhausted unnamed-layer pool falls back to the native UI layer.
        // That shared layer cannot be excluded without blanking the real menu.
        if (VRLayers.ModLayerMask == VRLayers.GameUiLayerMask || (mask & VRLayers.ModLayerMask) == 0)
            return;
        _captureMaskedCamera = camera;
        _captureMask = mask;
        camera.cullingMask = mask & ~VRLayers.ModLayerMask;
    }

    private void OnCapturePostRender(Camera camera)
    {
        if (_captureMaskedCamera != null && camera == _captureMaskedCamera)
            RestoreCaptureMask();
    }

    private void RestoreCaptureMask()
    {
        if (_captureMaskedCamera == null)
            return;
        _captureMaskedCamera.cullingMask = _captureMask;
        _captureMaskedCamera = null;
    }
}
