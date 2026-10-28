using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    // Capture135518 shows tracked devices and a live owned head, but the player reports
    // hands warping with head movement. Those facts cannot distinguish incorrect poses
    // from incorrect shader matrices. Observe both at the actual head render boundary;
    // never compensate a controller or head offset without that evidence. The literal
    // prefix retains these bounded Debug records in the startup log's reserved budget.
    // Capture 215640 spent five of six records before MainMenu and retained no
    // head-motion comparison there. Keep three observations for each scene visit
    // (after 1, 4 and 15 seconds), with an independent lifetime cap across rig rebuilds.
    // Provider records observe actual XR subrects; an accepted viewport setter or
    // a finite Camera matrix does not establish the submitted rendering contract.
    private const int QuestTrackingSampleLimit = 18;
    private const int QuestTrackingSceneSampleLimit = 3;
    private const int QuestTrackingProviderLimit = 2;
    private readonly List<XRDisplaySubsystem> _questTrackingDisplays = new(2);
    private int _questTrackingSamples;
    private int _questTrackingSceneSamples;
    private float _questTrackingNextSample;
    private string _questTrackingScene = "startup";

    private void StartQuestTrackingEvidence()
    {
        if (!QuestStandalonePlatform.Enabled)
            return;
        _questTrackingNextSample = Time.realtimeSinceStartup + 1f;
        Camera.onPreCull += CaptureQuestTrackingEvidence;
    }

    private void NoteQuestTrackingScene(string scene)
    {
        if (!QuestStandalonePlatform.Enabled)
            return;
        if (_questTrackingScene == scene)
            return;
        _questTrackingScene = scene;
        _questTrackingSceneSamples = 0;
        _questTrackingNextSample = Time.realtimeSinceStartup + 1f;
    }

    private void StopQuestTrackingEvidence() => Camera.onPreCull -= CaptureQuestTrackingEvidence;

    private void CaptureQuestTrackingEvidence(Camera rendering)
    {
        // No device, transform, matrix or clock queries at the normal log level or on
        // desktop. The lifetime cap survives rig rebuilds; scene loads only re-arm time.
        if (!QuestStandalonePlatform.Enabled || !VRLog.Wants(VRLogLevel.Debug)
            || rendering != _camera || _rigRoot == null || _questTrackingSamples >= QuestTrackingSampleLimit
            || _questTrackingSceneSamples >= QuestTrackingSceneSampleLimit)
            return;
        float now = Time.realtimeSinceStartup;
        if (now < _questTrackingNextSample)
            return;
        _questTrackingSamples++;
        _questTrackingSceneSamples++;
        _questTrackingNextSample = now + (_questTrackingSceneSamples == 1 ? 3f : 11f);
        try
        {
            var text = new StringBuilder(3072);
            text.Append("[Quest startup] presentation tracking sample=").Append(_questTrackingSamples)
                .Append(" scene=").Append(_questTrackingScene).Append(" sceneSample=").Append(_questTrackingSceneSamples)
                .Append(" phase=head-pre-cull frame=").Append(Time.frameCount)
                .Append(" mode=").Append(XRSettings.stereoRenderingMode)
                .Append(" stereoEnabled=").Append(rendering.stereoEnabled).Append(" targetEye=").Append(rendering.stereoTargetEye)
                .Append(" activeEye=").Append(rendering.stereoActiveEye)
                .Append(" viewportScale=");
            TrackingNumber(text, XRSettings.renderViewportScale);
            text.Append(" allocationScale="); TrackingNumber(text, XRSettings.eyeTextureResolutionScale);
            AppendTrackingRect(text, "cameraRect", rendering.rect);
            AppendTrackingRect(text, "cameraPixelRect", rendering.pixelRect);
            text.Append(" headLocal="); TrackingVector(text, rendering.transform.localPosition);
            text.Append(" headRotation="); TrackingRotation(text, rendering.transform.localRotation);
            text.Append(" headWorld="); TrackingVector(text, rendering.transform.position);
            text.Append(" headScale="); TrackingVector(text, rendering.transform.lossyScale);
            text.Append(" rigPosition="); TrackingVector(text, _rigRoot.transform.position);
            text.Append(" rigRotation="); TrackingRotation(text, _rigRoot.transform.rotation);
            text.Append(" rigScale="); TrackingVector(text, _rigRoot.transform.lossyScale);
            text.Append(" clips=("); TrackingNumber(text, rendering.nearClipPlane); text.Append(',');
            TrackingNumber(text, rendering.farClipPlane); text.Append(')');
            AppendTrackingDevice(text, "headDevice", XRNode.Head, rendering.transform);
            AppendTrackingHand(text, "left", XRNode.LeftHand, VRHands.Left);
            AppendTrackingHand(text, "right", XRNode.RightHand, VRHands.Right);
            AppendTrackingProjection(text, "monoProjection", rendering.projectionMatrix);
            AppendTrackingProjection(text, "leftProjection", rendering.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left));
            AppendTrackingProjection(text, "rightProjection", rendering.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right));
            AppendTrackingMatrix(text, "monoView", rendering.worldToCameraMatrix);
            AppendTrackingMatrix(text, "leftView", rendering.GetStereoViewMatrix(Camera.StereoscopicEye.Left));
            AppendTrackingMatrix(text, "rightView", rendering.GetStereoViewMatrix(Camera.StereoscopicEye.Right));
            UnityEngine.Debug.Log(text.ToString());
            CaptureQuestProviderEvidence(rendering);
        }
        catch (System.Exception error)
        {
            // Observation is optional and may never prevent the owned camera rendering.
            UnityEngine.Debug.Log("[Quest startup] presentation tracking unavailable sample=" + _questTrackingSamples
                + " scene=" + _questTrackingScene + " error=" + error.GetType().Name + ": " + error.Message);
        }
    }

    private void CaptureQuestProviderEvidence(Camera rendering)
    {
        // These exact fields and GetRenderParameter APIs exist in the pinned
        // Unity 2021.3.5 XRModule. Built-in rendering may expose no passes here;
        // report that absence rather than deriving a viewport from the request.
        try
        {
            SubsystemManager.GetInstances(_questTrackingDisplays);
            if (_questTrackingDisplays.Count == 0)
                UnityEngine.Debug.Log(QuestProviderPrefix() + " displays=0");
            for (int displayIndex = 0; displayIndex < _questTrackingDisplays.Count
                && displayIndex < QuestTrackingProviderLimit; displayIndex++)
            {
                XRDisplaySubsystem display = _questTrackingDisplays[displayIndex];
                var text = new StringBuilder(1536);
                text.Append(QuestProviderPrefix()).Append(" display=").Append(displayIndex)
                    .Append(" displays=").Append(_questTrackingDisplays.Count).Append(" running=").Append(display.running);
                int passes = display.running ? display.GetRenderPassCount() : 0;
                text.Append(" passes=").Append(passes).Append(" viewportScale=");
                TrackingNumber(text, XRSettings.renderViewportScale);
                text.Append(" providerViewportScale="); TrackingNumber(text, display.scaleOfAllViewports);
                text.Append(" providerAllocationScale="); TrackingNumber(text, display.scaleOfAllRenderTargets);
                UnityEngine.Debug.Log(text.ToString());
                for (int passIndex = 0; passIndex < passes && passIndex < QuestTrackingProviderLimit; passIndex++)
                {
                    display.GetRenderPass(passIndex, out XRDisplaySubsystem.XRRenderPass pass);
                    RenderTextureDescriptor descriptor = pass.renderTargetDesc;
                    int parameters = pass.GetRenderParameterCount();
                    for (int parameterIndex = 0; parameterIndex < parameters
                        && parameterIndex < QuestTrackingProviderLimit; parameterIndex++)
                    {
                        pass.GetRenderParameter(rendering, parameterIndex, out XRDisplaySubsystem.XRRenderParameter parameter);
                        text.Clear();
                        text.Append(QuestProviderPrefix()).Append(" display=").Append(displayIndex)
                            .Append(" pass=").Append(passIndex).Append('/').Append(passes)
                            .Append(" parameter=").Append(parameterIndex).Append('/').Append(parameters)
                            .Append(" slice=").Append(parameter.textureArraySlice)
                            .Append(" target=(").Append(descriptor.width).Append(',').Append(descriptor.height)
                            .Append(',').Append(descriptor.dimension).Append(",depth=").Append(descriptor.volumeDepth)
                            .Append(",msaa=").Append(descriptor.msaaSamples).Append(",format=").Append(descriptor.colorFormat)
                            .Append(",sRGB=").Append(descriptor.sRGB).Append(')');
                        AppendTrackingRect(text, "viewport", parameter.viewport);
                        AppendTrackingProjection(text, "projection", parameter.projection);
                        AppendTrackingMatrix(text, "view", parameter.view);
                        UnityEngine.Debug.Log(text.ToString());
                    }
                }
            }
        }
        catch (System.Exception error)
        {
            UnityEngine.Debug.Log(QuestProviderPrefix() + " unavailable=" + error.GetType().Name);
        }
        finally { _questTrackingDisplays.Clear(); }
    }

    private string QuestProviderPrefix() => "[Quest startup] presentation provider sample=" + _questTrackingSamples
        + " scene=" + _questTrackingScene + " phase=head-pre-cull";

    private static void AppendTrackingRect(StringBuilder text, string name, Rect value)
    {
        text.Append(' ').Append(name).Append("=(");
        TrackingNumber(text, value.x); text.Append(','); TrackingNumber(text, value.y); text.Append(',');
        TrackingNumber(text, value.width); text.Append(','); TrackingNumber(text, value.height); text.Append(')');
    }

    private void AppendTrackingHand(StringBuilder text, string name, XRNode node, VRHand? hand)
    {
        text.Append(' ').Append(name).Append("Present=").Append(hand != null);
        if (hand == null)
            return;
        text.Append(' ').Append(name).Append("Tracked=").Append(hand.IsTracked);
        text.Append(' ').Append(name).Append("RigPosition=");
        TrackingVector(text, _rigRoot!.transform.InverseTransformPoint(hand.transform.position));
        text.Append(' ').Append(name).Append("Rotation="); TrackingRotation(text, hand.transform.localRotation);
        if (hand.Rig != null && hand.Rig.Root != null)
        {
            text.Append(' ').Append(name).Append("VisualRigPosition=");
            TrackingVector(text, _rigRoot.transform.InverseTransformPoint(hand.Rig.Root.position));
            text.Append(' ').Append(name).Append("VisualScale="); TrackingVector(text, hand.Rig.Root.lossyScale);
        }
        AppendTrackingDevice(text, name + "Device", node, hand.transform);
    }

    private void AppendTrackingDevice(StringBuilder text, string name, XRNode node, Transform observed)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        text.Append(' ').Append(name).Append("Valid=").Append(device.isValid);
        if (!device.isValid)
            return;
        text.Append(' ').Append(name).Append("Tracked=")
            .Append(device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked);
        if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
        {
            text.Append(' ').Append(name).Append("Position="); TrackingVector(text, position);
            text.Append(' ').Append(name).Append("PositionDelta=");
            TrackingNumber(text, Vector3.Distance(_rigRoot!.transform.InverseTransformPoint(observed.position), position));
        }
        else text.Append(' ').Append(name).Append("Position=unavailable");
        if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
        {
            text.Append(' ').Append(name).Append("Rotation="); TrackingRotation(text, rotation);
            text.Append(' ').Append(name).Append("RotationDelta=");
            TrackingNumber(text, Quaternion.Angle(Quaternion.Inverse(_rigRoot!.transform.rotation) * observed.rotation, rotation));
        }
        else text.Append(' ').Append(name).Append("Rotation=unavailable");
    }

    private static void AppendTrackingProjection(StringBuilder text, string name, Matrix4x4 matrix)
    {
        AppendTrackingMatrix(text, name, matrix);
        bool perspective = Mathf.Abs(matrix.m32 + 1f) < 0.001f && Mathf.Abs(matrix.m33) < 0.001f;
        text.Append(' ').Append(name).Append("Perspective=").Append(perspective);
        if (perspective && Mathf.Abs(matrix.m00) > 0.000001f && Mathf.Abs(matrix.m11) > 0.000001f)
        {
            // Frustum slopes at unit distance along camera-forward (-Z); no FOV guess.
            text.Append(' ').Append(name).Append("Slopes=(");
            TrackingNumber(text, (matrix.m02 - 1f) / matrix.m00); text.Append(',');
            TrackingNumber(text, (matrix.m02 + 1f) / matrix.m00); text.Append(',');
            TrackingNumber(text, (matrix.m12 - 1f) / matrix.m11); text.Append(',');
            TrackingNumber(text, (matrix.m12 + 1f) / matrix.m11); text.Append(')');
        }
    }

    private static void AppendTrackingMatrix(StringBuilder text, string name, Matrix4x4 matrix)
    {
        bool finite = true;
        text.Append(' ').Append(name).Append("=(");
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
            {
                float value = matrix[row, column];
                finite &= !float.IsNaN(value) && !float.IsInfinity(value);
                if (row != 0 || column != 0) text.Append(column == 0 ? ';' : ',');
                TrackingNumber(text, value);
            }
        text.Append(") ").Append(name).Append("Finite=").Append(finite);
    }

    private static void TrackingVector(StringBuilder text, Vector3 value)
    {
        text.Append('('); TrackingNumber(text, value.x); text.Append(','); TrackingNumber(text, value.y);
        text.Append(','); TrackingNumber(text, value.z); text.Append(')');
    }

    private static void TrackingRotation(StringBuilder text, Quaternion value)
    {
        text.Append('('); TrackingNumber(text, value.x); text.Append(','); TrackingNumber(text, value.y);
        text.Append(','); TrackingNumber(text, value.z); text.Append(','); TrackingNumber(text, value.w); text.Append(')');
    }

    private static void TrackingNumber(StringBuilder text, float value) =>
        text.Append(value.ToString("F4", CultureInfo.InvariantCulture));
}
