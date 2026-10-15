using System;
using System.Reflection;
using GloomhavenVR.Core.SteamLink;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// Mod-owned, settings-controlled source-carriage diagnostic. This never activates passthrough.
/// An explicit request prepares immutable originals while options remain usable, then waits for
/// their closure. Reopening options interrupts output synchronously, before their native Show.
/// </summary>
internal static class SteamLinkMaskRuntime
{
    private const string Scope = "SteamLink";
    private static Driver? _driver;

    internal static void ResetAtBoot()
    {
        SteamLinkMaskConfig.Bind();
        SteamLinkMaskConfig.SetRequested(false);
    }

    internal static void Install(GameObject host)
    {
        if (_driver != null) Shutdown();
        _driver = host.AddComponent<Driver>();
        _driver.Configure(pcSource: !FrameDefaults.Active);
    }

    internal static void Shutdown()
    {
        Driver? driver = _driver;
        if (driver == null) return;
        driver.StopCurrent("core shutdown"); // Reset while this driver still owns the request.
        _driver = null;
        UnityEngine.Object.Destroy(driver);
    }

    // May run before Bind/Install. A pending menu request must survive its own OnShow/rebuild;
    // only an already running eye override is stopped and its persisted request reset here.
    internal static void NotifyOptionsOpening()
    {
        Driver? driver = _driver;
        if (driver != null && driver.StopForOptionsOpening())
            driver.StopCurrent("VR options opened");
    }

    // Camera callback gate: no config writes, logs, disposal, disk reads or allocations here.
    internal static bool CanRender(Camera camera, ProbePolicy policy, PayloadOutput output)
    {
        Driver? driver = _driver;
        return driver != null && driver.CanRender(camera, policy, output);
    }

    private sealed class Driver : MonoBehaviour
    {
        private SteamLinkMaskLifecycle _lifecycle = new();
        private ProbeTexture? _left, _right;
        private Camera? _camera;
        private PayloadOutput? _output;
        private bool _pcSource;
        private bool _renderStop;

        internal void Configure(bool pcSource) => _pcSource = pcSource;

        private void Update()
        {
            try { Tick(); }
            catch (Exception error)
            {
                VRLog.Alert(Scope, "PAYLOAD source test failed: " + error.GetType().Name + ": " + Bounded(error.Message));
                if (VRLog.WantsDebug) VRLog.Debug(Scope, "PAYLOAD failure context: " + error);
                StopCurrent("preparation or lifecycle failure");
            }
        }

        private void Tick()
        {
            bool requested = SteamLinkMaskConfig.Requested;
            // Ordinary players pay one request/ownership check; no menu/camera/clock sampling while off.
            if (!requested && !_lifecycle.HasRequest && _output == null && _left == null && _right == null)
                return;
            if (_output != null) DrainObservations();
            if (_renderStop)
            {
                StopCurrent("render authorization ended");
                return;
            }
            requested = SteamLinkMaskConfig.Requested;
            bool optionsVisible = VROptionsTab.IsOpen;
            bool sameCamera = _camera != null && ReferenceEquals(_camera, VRRigDriver.HeadCamera);
            SteamLinkMaskStep step = _lifecycle.Tick(requested, optionsVisible,
                VRSession.IsRunning && _pcSource, sameCamera, Time.realtimeSinceStartupAsDouble);
            switch (step)
            {
                case SteamLinkMaskStep.Prepare:
                    _left = ProbeTexture.Load("left");
                    _right = ProbeTexture.Load("right");
                    ProbeManifest.CheckPair(_left.Manifest, _right.Manifest);
                    if (VRLog.WantsDebug)
                        VRLog.Debug(Scope, "PAYLOAD prepared embedded static generation: session=" + _left.Manifest.Session
                            + " content=" + _left.Manifest.ContentId + " canvas=" + _left.Texture.width + "x" + _left.Texture.height
                            + " RGB=straight texture_linear=false filter=Point mipmaps=false activeColorSpace="
                            + QualitySettings.activeColorSpace + " left_png_sha256=" + _left.PngSha256 + " right_png_sha256=" + _right.PngSha256);
                    break;
                case SteamLinkMaskStep.Ready:
                    StartOutput();
                    break;
                case SteamLinkMaskStep.Stop:
                    StopCurrent(!_pcSource ? "PC streaming source required" : "request, VR session, camera or deadline ended");
                    break;
            }
        }

        private void StartOutput()
        {
            Camera? target = VRRigDriver.HeadCamera;
            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy
                || target.name != ProbePolicy.HeadCameraName || _left == null || _right == null
                || target.GetComponent<PayloadOutput>() != null)
            {
                StopCurrent("authorized head camera unavailable");
                return;
            }
            if (!_lifecycle.Policy.Start(Time.realtimeSinceStartupAsDouble, true,
                    GraphicsSettings.currentRenderPipeline == null,
                    VRSession.IsRunning && XRSettings.enabled && XRSettings.isDeviceActive && target.stereoEnabled,
                    XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass,
                    target.stereoTargetEye == StereoTargetEyeMask.Both))
            {
                StopCurrent("requires built-in XR MultiPass and both eyes");
                return;
            }
            _camera = target;
            _output = target.gameObject.AddComponent<PayloadOutput>();
            _output.Initialize(target, _lifecycle.Policy, _left, _right);
            VRLog.Note(Scope, "PAYLOAD source test started; static diagnostic generation only, no passthrough. "
                + "Ends within 30 seconds or immediately when VR options reopen.");
            if (VRLog.WantsDebug)
            {
                VRLog.Debug(Scope, "PAYLOAD attached camera_id=" + target.GetInstanceID() + " stereo=" + XRSettings.stereoRenderingMode
                    + " xr_device=" + XRSettings.loadedDeviceName + " graphics=" + SystemInfo.graphicsDeviceType);
                MonoBehaviour[] components = target.GetComponents<MonoBehaviour>();
                int count = Math.Min(components.Length, 32), effects = 0;
                for (int index = 0; index < count; index++)
                {
                    MonoBehaviour component = components[index];
                    if (component == null || component == _output) continue;
                    MethodInfo? method = component.GetType().GetMethod("OnRenderImage",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method == null) continue;
                    effects++;
                    VRLog.Debug(Scope, "PAYLOAD image-effect index=" + index + " enabled=" + component.enabled
                        + " type=" + Bounded(component.GetType().FullName ?? component.GetType().Name));
                }
                VRLog.Debug(Scope, "PAYLOAD image-effect census inspected=" + count + " total=" + components.Length + " effects=" + effects
                    + "; later effects and native composition remain unproved.");
            }
        }

        internal bool StopForOptionsOpening() => _lifecycle.StopForOptionsOpening();

        internal bool CanRender(Camera camera, ProbePolicy policy, PayloadOutput output)
        {
            bool sameComponent = ReferenceEquals(_output, output);
            bool sameCamera = ReferenceEquals(_camera, camera);
            bool owned = ReferenceEquals(_lifecycle.Policy, policy) && sameComponent && sameCamera;
            bool allowed = owned && _pcSource && ReferenceEquals(VRRigDriver.HeadCamera, camera)
                && VRSession.IsRunning && SteamLinkMaskConfig.Requested && !VROptionsTab.IsOpen;
            bool result = SteamLinkMaskCallbackGate.Admit(_lifecycle.Policy, policy, sameComponent,
                sameCamera, allowed, owned ? Time.realtimeSinceStartupAsDouble : 0, out bool stopCurrent);
            if (stopCurrent) _renderStop = true;
            return result;
        }

        internal void StopCurrent(string reason)
        {
            bool ownsRequest = SteamLinkMaskCallbackGate.OwnsRequest(_driver, this);
            bool active = (ownsRequest && SteamLinkMaskConfig.Requested) || _lifecycle.HasRequest || _left != null || _right != null;
            _lifecycle.Stop(); // Admission closes before component/texture destruction.
            if (ownsRequest) SteamLinkMaskConfig.SetRequested(false);
            if (_output != null)
            {
                if (VRLog.WantsDebug)
                    VRLog.Debug(Scope, "PAYLOAD callback totals left=" + _output.LeftCallbacks + " right=" + _output.RightCallbacks
                        + " fallback=" + _output.FallbackCallbacks);
                Destroy(_output);
            }
            _output = null;
            _camera = null;
            _left?.Dispose();
            _right?.Dispose();
            _left = _right = null;
            _renderStop = false;
            _lifecycle = new SteamLinkMaskLifecycle();
            if (active) VRLog.Note(Scope, "PAYLOAD source test stopped: " + reason + "; ordinary source output restored, request off.");
        }

        private void DrainObservations()
        {
            if (_output == null) return;
            for (int slot = 0; slot < 3; slot++)
            {
                if (!_output.TakeObservation(slot, out PayloadOutput.Observation o)) continue;
                if (!VRLog.WantsDebug) continue;
                VRLog.Debug(Scope, "PAYLOAD callback eye=" + o.Eye + " actual_eye=" + o.ActualEye + " payload_blit=" + o.SubmittedPayload
                    + " camera_current_matches=" + o.CurrentCameraMatches + " stereo=" + o.StereoMode
                    + " xr=" + o.XrEnabled + "/" + o.XrDeviceActive + "/" + o.CameraStereoEnabled
                    + " builtin=" + o.BuiltInPipeline + " both=" + o.BothEyes + " gl_srgb_write=" + o.SrgbWrite
                    + " color_space=" + o.ColorSpace + " source=" + o.SourceWidth + "x" + o.SourceHeight
                    + ":" + o.SourceFormat + ":" + o.SourceDimension + ":" + o.SourceVrUsage + " source_srgb=" + o.SourceSrgb
                    + " destination_present=" + o.HasDestination + " destination=" + o.DestinationWidth + "x" + o.DestinationHeight
                    + ":" + o.DestinationFormat + ":" + o.DestinationDimension + ":" + o.DestinationVrUsage
                    + " destination_srgb=" + o.DestinationSrgb + "; native carriage/mapping unproved.");
            }
            string? fault = _output.TakeFault();
            if (fault == null) return;
            VRLog.Alert(Scope, "PAYLOAD callback failed: " + Bounded(fault));
            StopCurrent("render callback failed");
        }

        private static string Bounded(string message) => message.Length > 512 ? message.Substring(0, 512) : message;

        private void OnDestroy()
        {
            StopCurrent("diagnostic driver destroyed");
            if (ReferenceEquals(_driver, this)) _driver = null;
        }
    }
}
