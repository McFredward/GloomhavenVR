using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GloomhavenVR.Core.SteamLink;

/// <summary>
/// Real-image source for the explicitly installed Frame receiver. It owns only
/// the rig head's coverage commands and final same-eye image effect. There is no
/// extra scene camera, CPU pixel path, previous-eye pair, timer or sender hook.
/// </summary>
internal static class SteamLinkSceneRuntime
{
    private static SceneHeadOutput? _output;
    private static ulong _epoch;
    private static string? _reportedReason;
    private static Camera? _failedHead;

    internal static bool Active => _output != null && _output.Ready;
    internal static bool ProjectionProbeSourceReady => _output != null && _output.ReadyForProjection
        && _output.Head.isActiveAndEnabled && StereoReady(_output.Head)
        && Owns(_output, _output.Head, _epoch);
    internal static bool SourceProfileSupported => Supported(out _);
    internal static bool Requested => MixedReality.SteamLinkNative != null
        && MixedReality.SteamLinkNative.Value && !FrameNativePassthrough.Required
        && SteamLinkConnection.Available;

    internal static bool TryEnter(Camera? head)
    {
        SteamLinkProjectionProbe.Tick();
        if (head == null || !head.isActiveAndEnabled || !VRSession.IsRunning
            || !StereoReady(head) || !TransportPrepared())
        {
            Exit();
            return false;
        }
        if (_failedHead == head) return false;
        if (!Supported(out string reason))
        {
            Refuse(head, reason);
            return false;
        }
        if (_output != null && _output.Head == head)
        {
            if (_output.OrdinaryRgba != SteamLinkCoverageBridge.CanAcceptOrdinaryRgba)
            {
                // Route transitions start a fresh complete stereo epoch. A
                // background capability reply must not repack only the second
                // eye of an already-rendering frame.
                Exit();
                return false;
            }
            _output.TickCold();
            if (_output.Ready) return true;
            if (!string.IsNullOrEmpty(_output.Failure))
            {
                Refuse(head, _output.Failure);
                return false;
            }
            // OnDisable/ownership loss releases render resources immediately.
            // A camera re-enabled after loading needs a fresh source, not a
            // permanent failed-head latch. Actual preparation/render failures
            // retain their nonempty Failure and still refuse this head.
        }
        Exit();
        if (!ScenePayloadAssets.TryGet(out Shader pack, out Shader coverage, out reason))
        {
            Refuse(head, reason);
            return false;
        }
        try
        {
            // The existing MR camera owner saves/restores this permission. No XR
            // size, MSAA or native camera target is changed here.
            MrNativeCamera.Apply(head);
            _output = head.gameObject.AddComponent<SceneHeadOutput>();
            if (!_output.Prepare(head, ++_epoch, pack, coverage, SteamLinkCoverageBridge.CanAcceptOrdinaryRgba))
            {
                Refuse(head, _output.Failure);
                return false;
            }
            _reportedReason = null;
            VRLog.Info("Core", "Steam Link native MR source prepared: "
                + (_output.OrdinaryRgba ? "ordinary-rgba-coverage-v1" : pack.name == SceneAtlasProfile.RobustShaderName ? SceneAtlasProfile.RobustProfile
                    : pack.name == SceneAtlasProfile.FullShaderName ? SceneAtlasProfile.FullProfile : SceneAtlasProfile.Profile) + "; "
                + "same-eye image and coverage, MultiPass SDR. The matching Frame receiver is required.");
            return true;
        }
        catch (Exception ex)
        {
            Refuse(head, ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    private static bool Supported(out string reason)
    {
        if (QualitySettings.activeColorSpace != ColorSpace.Gamma)
            reason = "The current Steam Link scene profile requires Gamma colour space.";
        else if (GraphicsSettings.currentRenderPipeline != null)
            reason = "The current Steam Link scene profile requires the built-in renderer.";
        else if (StereoModeConfig.Current != StereoModeConfig.Mode.MultiPass
            || XRSettings.stereoRenderingMode != XRSettings.StereoRenderingMode.MultiPass)
            reason = "The current Steam Link scene profile requires MultiPass stereo.";
        else { reason = string.Empty; return true; }
        return false;
    }

    private static void Refuse(Camera head, string reason)
    {
        _failedHead = head;
        Exit();
        if (reason == _reportedReason) return;
        _reportedReason = reason;
        VRLog.Warn("Core", "Steam Link native MR kept the ordinary image: " + reason);
    }

    internal static void Reset()
    {
        SteamLinkCoverageBridge.Reset();
        SteamLinkProjectionProbe.Stop();
        Exit();
        _failedHead = null;
        _reportedReason = null;
    }

    internal static void Failed(Camera head, string reason) => Refuse(head, reason);

    internal static void Exit()
    {
        SteamLinkProjectionProbe.Stop();
        SceneHeadOutput? previous = _output;
        _output = null; // Revoke callback authority before releasing any owned resource.
        if (previous == null) return;
        previous.Close();
        UnityEngine.Object.Destroy(previous);
    }

    internal static bool Owns(SceneHeadOutput output, Camera head, ulong epoch) =>
        ReferenceEquals(_output, output) && epoch == _epoch && VRSession.IsRunning
        && VRCameraPolicy.AllowedHead == head && Requested && MixedReality.Enabled.Value
        && TransportPrepared();

    // Metadata-only host boundary, queried again at the rendering authority
    // seam: a setting/width change or helper completion can race the cold Tick.
    // Edge-quality opt-out preserves the original native-source behavior.
    internal static bool TransportPrepared() => TransportPrepared(XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight);
    internal static bool TransportPrepared(int width, int height)
    {
        // Keep the established output's route fixed across its current pair.
        // A capability gain is consumed only by the cold owner above; a loss
        // immediately leaves ordinary RGB, never a stale remote mask.
        bool ordinary = _output != null ? _output.OrdinaryRgba : SteamLinkCoverageBridge.CanAcceptOrdinaryRgba;
        return ordinary ? SteamLinkCoverageBridge.CanAcceptOrdinaryRgba && SteamLinkStreamQuality.LegacyReleased
            : SteamLinkStreamQuality.HostPrepared(
                MixedReality.SteamLinkEdgeQuality != null && MixedReality.SteamLinkEdgeQuality.Value, width, height);
    }

    // A running VR session does not certify a camera's current XR attachment.
    // Startup/load teardown can temporarily render that same head as mono or
    // target only one eye. Never introduce the packed atlas during that gap.
    // This is temporary admission loss, not a shader/attachment fault latch.
    internal static bool StereoReady(Camera head) => head.stereoEnabled
        && head.stereoTargetEye == StereoTargetEyeMask.Both;
}

// Unity invokes this only on the camera it is attached to. Unknown eyes and
// failed packing preserve the ordinary RGB output; they never publish an old
// mask or infer an eye from callback order.
[RequireComponent(typeof(Camera))]
internal sealed class SceneHeadOutput : MonoBehaviour
{
    internal Camera Head { get; private set; } = null!;
    internal bool Ready => !_closed && _coverage != null && _coverage.Ready
        && _pack != null && _materials != null && _materials.Ready;
    internal bool ReadyForProjection => Ready && !OrdinaryRgba && _cohortReady && _pack!.Profile == SceneAtlasProfile.RobustProfile;
    internal bool OrdinaryRgba { get; private set; }
    internal string Failure { get; private set; } = "Scene source has not been prepared.";
    private SceneEyeCoverage? _coverage;
    private SceneEyeOutput? _pack;
    private SceneTransparentMaterials? _materials;
    private bool _materialCallbacks;
    private RenderTexture? _target;
    private float _nextMaterialRefresh;
    private ulong _epoch;
    private bool _closed;
    private int _reportedEyes;
    private int _cohortFrame = -1, _cohortEyes;
    private bool _cohortReady;

    internal bool Prepare(Camera head, ulong epoch, Shader pack, Shader coverage, bool ordinaryRgba = false)
    {
        Head = head; _epoch = epoch; OrdinaryRgba = ordinaryRgba;
        _target = head.targetTexture;
        _pack = new SceneEyeOutput(pack);
        _coverage = new SceneEyeCoverage(coverage);
        _coverage.SetOpaqueBackdropSource(static () => WorldUI.FlatScreen.OpaqueBackdrop);
        if (!_coverage.Arm(head, epoch))
        {
            Failure = _coverage.Reason.ToString();
            Close();
            return false;
        }
        _materials = new SceneTransparentMaterials(ScenePayloadAssets.FindVariant);
        if (!_materials.BindHead(head) || !_materials.RefreshCold())
        {
            Failure = _materials.Reason.ToString();
            Close();
            return false;
        }
        Camera.onPreRender += _materials.EnterHead;
        Camera.onPostRender += _materials.ExitHead;
        _materialCallbacks = true;
        _nextMaterialRefresh = Time.unscaledTime + 1f;
        Failure = string.Empty;
        return true;
    }

    internal void TickCold()
    {
        if (!Ready || Time.unscaledTime < _nextMaterialRefresh) return;
        _nextMaterialRefresh = Time.unscaledTime + 1f;
        try
        {
            if (!_materials!.RefreshCold())
            {
                Failure = "Owned material refresh: " + _materials.Reason;
                Close();
            }
        }
        catch (Exception ex)
        {
            Failure = ex.GetType().Name + ": " + ex.Message;
            Close();
        }
    }

    private void OnPreCull()
    {
        if (!Ready) return;
        if (!SteamLinkSceneRuntime.Owns(this, Head, _epoch)
            || !SteamLinkSceneRuntime.StereoReady(Head)) Close();
        else if (Head.allowHDR || Head.targetTexture != _target
            || Head.clearFlags != CameraClearFlags.SolidColor
            || Head.actualRenderingPath != RenderingPath.Forward)
        {
            Failure = "Head camera render attachments or clear policy changed during scene output.";
            Close();
            SteamLinkSceneRuntime.Failed(Head, Failure);
        }
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        bool packed = false;
        if (!_closed && _materials != null && !_materials.Ready)
        {
            Failure = "Owned material scope: " + _materials.Reason;
            Close();
            SteamLinkSceneRuntime.Failed(Head, Failure);
        }
        if (Ready && SteamLinkSceneRuntime.Owns(this, Head, _epoch)
            && _coverage!.IsArmedFor(Head, _epoch))
        {
            // Unity can report Mono while an XR camera starts/stops even after
            // the previous cold tick observed a running session. Do not publish
            // an atlas for that ordinary render or permanently poison this head;
            // a later admitted stereo render prepares a fresh coverage epoch.
            Camera.MonoOrStereoscopicEye eye = Head.stereoActiveEye;
            if (!SteamLinkSceneRuntime.StereoReady(Head)
                || !SteamLinkSceneRuntime.TransportPrepared(source.width, source.height)
                || (eye != Camera.MonoOrStereoscopicEye.Left
                    && eye != Camera.MonoOrStereoscopicEye.Right))
            {
                Close();
                Graphics.Blit(source, destination);
                return;
            }
            if (!StereoCohortReady(eye, Time.frameCount))
            {
                Graphics.Blit(source, destination);
                return;
            }
            try
            {
                SceneEyeResult result = OrdinaryRgba
                    ? _pack!.TryRenderOrdinary(source, destination, eye)
                    : _pack!.TryRender(source, destination, eye);
                packed = result == SceneEyeResult.Rendered;
                if (!packed) Failure = result.ToString();
                else if (VRLog.WantsDebug)
                {
                    int eyeBit = eye == Camera.MonoOrStereoscopicEye.Right ? 2 : 1;
                    if ((_reportedEyes & eyeBit) == 0)
                    {
                        _reportedEyes |= eyeBit;
                        VRLog.Debug("Core", "Steam Link real scene eye=" + eye
                            + " source=" + source.width + "x" + source.height + " format=" + source.graphicsFormat
                            + " samples=" + source.antiAliasing + " carrier="
                            + (destination == null ? "XR target" : destination.width + "x" + destination.height)
                            + "; " + (OrdinaryRgba ? "ordinary-rgba-coverage-v1; native provider owns texture/AU association" : _pack!.Profile) + ".");
                    }
                }
            }
            catch (Exception ex) { Failure = ex.GetType().Name + ": " + ex.Message; }
            if (!packed)
            {
                Close();
                SteamLinkSceneRuntime.Failed(Head, Failure);
            }
        }
        if (!packed) Graphics.Blit(source, destination);
    }

    private bool StereoCohortReady(Camera.MonoOrStereoscopicEye eye, int frame)
    {
        // AddComponent may happen after the first eye of an already-running XR
        // frame. Seeing only its remaining Right must not switch that one eye
        // to an atlas while Left was ordinary. Observe an explicitly labelled
        // complete Left+Right frame first, keeping BOTH images ordinary, then
        // begin packing at the following frame boundary. Store no image, infer
        // no eye from callback order, and never carry admission to a new epoch.
        if (_cohortFrame != frame)
        {
            if (!_cohortReady && _cohortEyes == 3)
            {
                _cohortReady = true;
                if (VRLog.WantsDebug)
                    VRLog.Debug("Core", "Steam Link native MR stereo cohort ready: frame=" + frame
                        + "; previous current-head frame observed Left+Right; atlas starts at this boundary.");
            }
            _cohortFrame = frame;
            _cohortEyes = 0;
        }
        _cohortEyes |= eye == Camera.MonoOrStereoscopicEye.Left ? 1 : 2;
        return _cohortReady;
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        SteamLinkProjectionProbe.Tick();
        if (_materials != null)
        {
            if (_materialCallbacks)
            {
                Camera.onPreRender -= _materials.EnterHead;
                Camera.onPostRender -= _materials.ExitHead;
                _materialCallbacks = false;
            }
            _materials.DisarmImmediate();
            _materials.Dispose();
            _materials = null;
        }
        _coverage?.Disarm();
        _coverage?.Dispose();
        _pack?.Dispose();
        _coverage = null; _pack = null;
    }

    private void OnDisable() => Close();
    private void OnDestroy() => Close();
}
