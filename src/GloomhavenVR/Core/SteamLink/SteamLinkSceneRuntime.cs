using System;
using GloomhavenVR.WorldUI;
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
    internal static bool Requested => MixedReality.SteamLinkNative != null
        && MixedReality.SteamLinkNative.Value && !FrameNativePassthrough.Required;

    internal static bool TryEnter(Camera? head)
    {
        if (head == null || !head.isActiveAndEnabled || !VRSession.IsRunning || VROptionsTab.IsOpen)
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
            _output.TickCold();
            if (!_output.Ready)
            {
                Refuse(head, string.IsNullOrEmpty(_output.Failure)
                    ? "The current head source was closed or its material scope failed." : _output.Failure);
                return false;
            }
            return _output.Ready;
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
            if (!_output.Prepare(head, ++_epoch, pack, coverage))
            {
                Refuse(head, _output.Failure);
                return false;
            }
            _reportedReason = null;
            VRLog.Info("Core", "Steam Link native MR source prepared: scene-contribution-atlas-v1; "
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
        Exit();
        _failedHead = null;
        _reportedReason = null;
    }

    internal static void Failed(Camera head, string reason) => Refuse(head, reason);

    internal static void Exit()
    {
        SceneHeadOutput? previous = _output;
        _output = null; // Revoke callback authority before releasing any owned resource.
        if (previous == null) return;
        previous.Close();
        UnityEngine.Object.Destroy(previous);
    }

    internal static bool Owns(SceneHeadOutput output, Camera head, ulong epoch) =>
        ReferenceEquals(_output, output) && epoch == _epoch && VRSession.IsRunning
        && VRCameraPolicy.AllowedHead == head && Requested && MixedReality.Enabled.Value
        && !VROptionsTab.IsOpen;
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

    internal bool Prepare(Camera head, ulong epoch, Shader pack, Shader coverage)
    {
        Head = head; _epoch = epoch;
        _target = head.targetTexture;
        _pack = new SceneEyeOutput(pack);
        _coverage = new SceneEyeCoverage(coverage);
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
        if (!SteamLinkSceneRuntime.Owns(this, Head, _epoch)) Close();
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
            try
            {
                SceneEyeResult result = _pack!.TryRender(source, destination, Head.stereoActiveEye);
                packed = result == SceneEyeResult.Rendered;
                if (!packed) Failure = result.ToString();
                else if (VRLog.WantsDebug)
                {
                    int eyeBit = Head.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? 2 : 1;
                    if ((_reportedEyes & eyeBit) == 0)
                    {
                        _reportedEyes |= eyeBit;
                        VRLog.Debug("Core", "Steam Link real scene eye=" + Head.stereoActiveEye
                            + " source=" + source.width + "x" + source.height + " format=" + source.graphicsFormat
                            + " samples=" + source.antiAliasing + " carrier="
                            + (destination == null ? "XR target" : destination.width + "x" + destination.height)
                            + "; scene-contribution-atlas-v1.");
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

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
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
