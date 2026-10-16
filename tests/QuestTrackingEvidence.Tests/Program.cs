using System;
using System.Globalization;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.XR;

internal static class Program
{
    static int assertions;
    static void Check(bool value, string proof)
    { assertions++; if (!value) throw new InvalidOperationException(proof); }
    static void Contains(string value, string expected) => Check(value.Contains(expected, StringComparison.Ordinal), expected);
    static Matrix4x4 Projection(float shift)
    {
        var value = Matrix4x4.identity;
        value.m00 = 2f; value.m11 = 4f; value.m02 = shift; value.m12 = -.5f;
        value.m32 = -1f; value.m33 = 0f;
        return value;
    }
    static void Sample(Camera head, float now)
    { Time.Now = now; Camera.Render(head); }
    static int Main()
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var rig = new GameObject();
            rig.transform.localPosition = new Vector3(7, 3, -4);
            rig.transform.localRotation = new Quaternion(0, .70710677f, 0, .70710677f);
            rig.transform.localScale = new Vector3(12, 12, 12);
            var head = new Camera(); head.transform.parent = rig.transform;
            head.transform.localPosition = new Vector3(.1f, 1.6f, .2f);
            head.projectionMatrix = Projection(0);
            head.LeftProjection = Projection(.1f); head.RightProjection = Projection(-.1f);
            head.LeftView = Matrix4x4.identity; head.LeftView[0, 3] = -.032f;
            head.RightView = Matrix4x4.identity; head.RightView[0, 3] = .032f;
            head.stereoEnabled = true; head.stereoTargetEye = StereoTargetEyeMask.Both;
            XRSettings.renderViewportScale = .8f; XRSettings.eyeTextureResolutionScale = 1f;
            var handRoot = new Transform { parent = rig.transform };
            var left = new VRHand { IsTracked = true };
            left.transform.parent = handRoot; left.transform.localPosition = new Vector3(-.3f, 1.2f, .5f);
            left.Rig.Root.parent = left.transform; left.Rig.Root.localPosition = new Vector3(0, 0, .04f);
            var right = new VRHand { IsTracked = true };
            right.transform.parent = handRoot; right.transform.localPosition = new Vector3(.3f, 1.2f, .5f);
            VRHands.Left = left; VRHands.Right = right;
            InputDevices.Set(XRNode.Head, head.transform.localPosition, head.transform.localRotation);
            InputDevices.Set(XRNode.LeftHand, left.transform.localPosition, left.transform.localRotation);
            InputDevices.Set(XRNode.RightHand, right.transform.localPosition + new Vector3(.2f, 0, 0), Quaternion.identity);
            var driver = new VRRigDriver(head, rig);

            QuestStandalonePlatform.Enabled = false; VRLog.DebugEnabled = true;
            Probe.Queries = 0; driver.Start(); driver.Scene("desktop"); Camera.Render(head);
            Check(Camera.Subscribers == 0 && Probe.Queries == 0, "desktop has no diagnostic subscription or queries");
            QuestStandalonePlatform.Enabled = true; VRLog.DebugEnabled = false;
            Time.Now = 0; driver.Start(); Check(Camera.Subscribers == 1, "single owned callback");
            Probe.Queries = 0; Camera.Render(head);
            Check(Probe.Queries == 0 && UnityEngine.Debug.Messages.Count == 0, "normal level performs no device clock matrix or transform queries");
            VRLog.DebugEnabled = true; Probe.Queries = 0; Camera.Render(new Camera());
            Check(Probe.Queries == 0 && UnityEngine.Debug.Messages.Count == 0, "foreign camera performs no diagnostic sampling");
            Sample(head, .9f); Check(UnityEngine.Debug.Messages.Count == 0, "settling delay");
            Vector3 headBefore = head.transform.localPosition, rigBefore = rig.transform.localPosition;
            Quaternion rotationBefore = rig.transform.localRotation;
            Sample(head, 1f); Check(UnityEngine.Debug.Messages.Count == 1, "first actual head render sample");
            string first = UnityEngine.Debug.Messages[0];
            Check(first.StartsWith("[Quest startup] presentation tracking ", StringComparison.Ordinal), "reserved startup log prefix");
            Contains(first, "phase=head-pre-cull"); Contains(first, "mode=SinglePassInstanced");
            Contains(first, "viewportScale=0.8000 allocationScale=1.0000");
            Contains(first, "headLocal=(0.1000,1.6000,0.2000)");
            Contains(first, "rigScale=(12.0000,12.0000,12.0000)");
            Contains(first, "headDevicePositionDelta=0.0000"); Contains(first, "headDeviceRotationDelta=0.0000");
            Contains(first, "leftRigPosition=(-0.3000,1.2000,0.5000)"); Contains(first, "leftDevicePositionDelta=0.0000");
            Contains(first, "rightDevicePositionDelta=0.2000");
            Contains(first, "leftVisualRigPosition=(-0.3000,1.2000,0.5400)");
            Contains(first, "leftProjectionSlopes=(-0.4500,0.5500,-0.3750,0.1250)");
            Contains(first, "rightProjectionSlopes=(-0.5500,0.4500,-0.3750,0.1250)");
            Contains(first, "leftView=(1.0000,0.0000,0.0000,-0.0320;");
            Contains(first, "rightView=(1.0000,0.0000,0.0000,0.0320;");
            Check(first.Length < 4096, "complete numeric evidence fits startup record limit");
            Check(head.transform.localPosition == headBefore && rig.transform.localPosition == rigBefore
                && rig.transform.localRotation == rotationBefore, "diagnostic never changes observed poses");
            Sample(head, 3.9f); Check(UnityEngine.Debug.Messages.Count == 1, "three second sample cadence");
            Sample(head, 4f); Check(UnityEngine.Debug.Messages.Count == 2, "second sample");
            driver.Scene("MainMenu");
            VRHands.Right = null; InputDevices.States[XRNode.LeftHand].PositionAvailable = false;
            InputDevices.States[XRNode.LeftHand].RotationAvailable = false;
            InputDevices.States[XRNode.Head].Valid = false;
            var invalid = head.LeftProjection; invalid.m00 = float.NaN; head.LeftProjection = invalid;
            var infinite = head.RightProjection; infinite[1, 3] = float.PositiveInfinity; head.RightProjection = infinite;
            Sample(head, 4.9f); Check(UnityEngine.Debug.Messages.Count == 2, "scene change permits another settling interval");
            Sample(head, 5f); Check(UnityEngine.Debug.Messages.Count == 3, "actual new scene sampled promptly");
            string scene = UnityEngine.Debug.Messages.Last(); Contains(scene, "scene=MainMenu"); Contains(scene, "rightPresent=False");
            Contains(scene, "headDeviceValid=False"); Contains(scene, "leftDevicePosition=unavailable");
            Contains(scene, "leftDeviceRotation=unavailable"); Contains(scene, "leftProjectionFinite=False");
            Contains(scene, "rightProjectionFinite=False");
            InputDevices.ThrowOnRead = true;
            Sample(head, 8f); Check(UnityEngine.Debug.Messages.Count == 4, "probe exception is bounded and contained");
            Contains(UnityEngine.Debug.Messages.Last(), "presentation tracking unavailable sample=4");
            InputDevices.ThrowOnRead = false;
            Sample(head, 38f); Check(UnityEngine.Debug.Messages.Count == 5, "subsequent sample survives optional probe error");
            driver.Stop(); Check(Camera.Subscribers == 0, "teardown releases actual render callback");
            Sample(head, 100f); Check(UnityEngine.Debug.Messages.Count == 5, "destroyed rig never samples");
            driver.Start(); Sample(head, 101f); Check(UnityEngine.Debug.Messages.Count == 6, "rig rebuild retains remaining global sample budget");
            driver.Scene("AnotherScene"); Probe.Queries = 0; Sample(head, 150f);
            Check(UnityEngine.Debug.Messages.Count == 6 && Probe.Queries == 0, "six sample lifetime cap survives scene changes");
            driver.Stop(); Check(Camera.Subscribers == 0, "rebuild callback also released");
            Console.WriteLine("PASS Quest tracking evidence: " + assertions + " assertions; managed observations only, no headset image claim");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL " + error.Message); return 1; }
    }
}
