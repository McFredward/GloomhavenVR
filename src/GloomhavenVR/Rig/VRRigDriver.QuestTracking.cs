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
    private const int QuestTrackingSampleLimit = 6;
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
            || rendering != _camera || _rigRoot == null || _questTrackingSamples >= QuestTrackingSampleLimit)
            return;
        float now = Time.realtimeSinceStartup;
        if (now < _questTrackingNextSample)
            return;
        _questTrackingSamples++;
        _questTrackingSceneSamples++;
        _questTrackingNextSample = now + (_questTrackingSceneSamples == 1 ? 3f : 30f);
        try
        {
            var text = new StringBuilder(3072);
            text.Append("[Quest startup] presentation tracking sample=").Append(_questTrackingSamples)
                .Append(" scene=").Append(_questTrackingScene).Append(" phase=head-pre-cull frame=").Append(Time.frameCount)
                .Append(" mode=").Append(XRSettings.stereoRenderingMode)
                .Append(" stereoEnabled=").Append(rendering.stereoEnabled).Append(" targetEye=").Append(rendering.stereoTargetEye)
                .Append(" viewportScale=");
            TrackingNumber(text, XRSettings.renderViewportScale);
            text.Append(" allocationScale="); TrackingNumber(text, XRSettings.eyeTextureResolutionScale);
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
        }
        catch (System.Exception error)
        {
            // Observation is optional and may never prevent the owned camera rendering.
            UnityEngine.Debug.Log("[Quest startup] presentation tracking unavailable sample=" + _questTrackingSamples
                + " scene=" + _questTrackingScene + " error=" + error.GetType().Name + ": " + error.Message);
        }
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
