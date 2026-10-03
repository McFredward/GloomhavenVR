using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using InputDevice = UnityEngine.InputSystem.InputDevice;
using UnityEngine.XR.OpenXR.Features.Interactions;

[InitializeOnLoad]
public static class ProbeInputFixture
{
    static int assertions;
    static ProbeInputFixture()
    {
        EditorApplication.playModeStateChanged += mode =>
        {
            if (mode != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("QuestInputPending", false)) return;
            SessionState.SetBool("QuestInputPending", false);
            RunAll();
        };
    }
    public static void Start()
    {
        SessionState.SetBool("QuestInputPending", true);
        EditorApplication.isPlaying = true;
    }
    static void Check(bool valid, string message) { ++assertions; if (!valid) throw new Exception(message); }
    static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < .0001f, message);
    static void PoseEvent(InputDevice device, bool aim, Vector3 position, Quaternion rotation, float tracked = 1, int flags = 3)
    {
        string root = aim ? "pointer/" : "devicePose/";
        using (StateEvent.From(device, out InputEventPtr value))
        {
            ((UnityEngine.InputSystem.Controls.ButtonControl)device[root + "isTracked"]).WriteValueIntoEvent(tracked, value);
            ((UnityEngine.InputSystem.Controls.IntegerControl)device[root + "trackingState"]).WriteValueIntoEvent(flags, value);
            ((UnityEngine.InputSystem.Controls.Vector3Control)device[root + "position"]).WriteValueIntoEvent(position, value);
            ((UnityEngine.InputSystem.Controls.QuaternionControl)device[root + "rotation"]).WriteValueIntoEvent(rotation, value);
            InputSystem.QueueEvent(value);
        }
        InputSystem.Update();
    }
    static object Controller(Type type, bool aim) => type.GetMethod("ForController").Invoke(null,
        new object[] { "fixture", "<XRController>{LeftHand}/", aim });
    static bool Read(object input, out Vector3 position, out Quaternion rotation)
    {
        var values = new object[] { Vector3.zero, Quaternion.identity };
        bool valid = (bool)input.GetType().GetMethod("TryRead").Invoke(input, values);
        position = (Vector3)values[0]; rotation = (Quaternion)values[1]; return valid;
    }
    static void Run(Type poseType, Type movementType, InputDevice device)
    {
        using (var grip = (IDisposable)Controller(poseType, false))
        using (var aim = (IDisposable)Controller(poseType, true))
        {
            var gripPosition = new Vector3(.3f, 1.1f, -.4f);
            var aimPosition = new Vector3(.4f, 1.2f, -.3f);
            var gripRotation = Quaternion.Euler(60, 20, 0);
            var aimRotation = Quaternion.Euler(0, 45, 0);
            PoseEvent(device, false, gripPosition, gripRotation);
            PoseEvent(device, true, aimPosition, aimRotation);
            Debug.Log("Fixture grip bound=" + grip.GetType().GetProperty("Bound").GetValue(grip) +
                " tracked=" + device["devicePose/isTracked"].ReadValueAsObject() +
                " flags=" + device["devicePose/trackingState"].ReadValueAsObject() +
                " position=" + device["devicePose/position"].ReadValueAsObject() +
                " rotation=" + device["devicePose/rotation"].ReadValueAsObject());
            Check(Read(grip, out var position, out var rotation), "grip valid");
            Near(position, gripPosition, "grip position retained");
            Check(Quaternion.Angle(rotation, gripRotation) < .01f, "grip orientation retained");
            Check(Read(aim, out position, out rotation), "aim valid");
            Near(position, aimPosition, "aim independent from grip");
            Check(Quaternion.Angle(rotation, aimRotation) < .01f, "aim orientation independent from grip");
            PoseEvent(device, true, aimPosition, aimRotation, flags: 1);
            Check(!Read(aim, out _, out _), "rotation-invalid aim rejected");
            Check(Read(grip, out _, out _), "invalid aim does not hide valid grip");
            PoseEvent(device, true, aimPosition, aimRotation, tracked: 0);
            Check(!Read(aim, out _, out _), "untracked aim rejected");
            PoseEvent(device, true, aimPosition, default);
            Check(!Read(aim, out _, out _), "zero quaternion rejected");
            // Input System can sanitize/disambiguate nonfinite event values; test the production boundary directly.
            Check(!(bool)poseType.GetMethod("Valid").Invoke(null, new object[] { new Vector3(float.NaN, 0, 0), aimRotation }),
                "nonfinite pose rejected");
        }
        var origin = new GameObject("fixture origin").transform;
        var head = new GameObject("fixture head").transform;
        head.SetParent(origin, false);
        head.localPosition = new Vector3(.4f, 1.6f, -.2f);
        head.localRotation = Quaternion.Euler(35, 90, 0);
        var movement = Activator.CreateInstance(movementType);
        var step = movementType.GetMethod("Step");
        Action<Vector2, float, float, bool> apply = (axis, turn, delta, enabled) =>
            step.Invoke(movement, new object[] { origin, head, axis, turn, 0f, delta, enabled });
        Action<float, float, bool> rise = (height, delta, enabled) =>
            step.Invoke(movement, new object[] { origin, head, Vector2.zero, 0f, height, delta, enabled });
        var localPosition = head.localPosition;
        var localRotation = head.localRotation;
        apply(Vector2.up, 1, .1f, true);
        Near(origin.position, Vector3.zero, "startup-held sticks do not move");
        apply(Vector2.zero, 0, .1f, true);
        apply(new Vector2(.1f, .1f), 0, .1f, true);
        Near(origin.position, Vector3.zero, "deadzone holds origin");
        apply(Vector2.up, 0, .1f, true);
        Near(origin.position, new Vector3(.075f, 0, 0), "movement follows projected head yaw without vertical drift");
        apply(Vector2.up, 0, 8, true);
        Near(origin.position, new Vector3(.15f, 0, 0), "long resumed frame is bounded");
        var pivot = head.position;
        apply(Vector2.zero, 1, .1f, true);
        Near(head.position, pivot, "snap turn pivots about actual head");
        Check(Mathf.Abs(Mathf.DeltaAngle(origin.eulerAngles.y, 30)) < .01f, "30 degree snap");
        apply(Vector2.zero, 1, .1f, true);
        Check(Mathf.Abs(Mathf.DeltaAngle(origin.eulerAngles.y, 30)) < .01f, "held turn stick fires once");
        apply(Vector2.zero, 0, .1f, true);
        apply(Vector2.zero, -1, .1f, true);
        Check(Mathf.Abs(Mathf.DeltaAngle(origin.eulerAngles.y, 0)) < .01f, "neutral re-arms opposite turn");
        Near(head.localPosition, localPosition, "tracked head local position untouched");
        Check(Quaternion.Angle(head.localRotation, localRotation) < .01f, "tracked head local rotation untouched");
        var before = origin.position;
        apply(Vector2.up, 1, .1f, false);
        apply(Vector2.up, 1, .1f, true);
        Near(origin.position, before, "resume-held sticks remain disarmed");
        Check(Mathf.Abs(Mathf.DeltaAngle(origin.eulerAngles.y, 0)) < .01f, "resume-held turn remains disarmed");
        apply(Vector2.zero, 0, .1f, true);
        apply(Vector2.up, 0, .1f, true);
        Check(Vector3.Distance(origin.position, before) > .07f, "neutral enables movement after resume");
        before = origin.position;
        rise(.2f, .1f, true);
        Near(origin.position, before, "vertical deadzone holds origin");
        rise(1, .1f, true);
        Near(origin.position, before + Vector3.up * .05f, "vertical input follows world up");
        rise(-1, 8, true);
        Near(origin.position, before, "vertical descent and long frame are bounded");
        Near(head.localPosition, localPosition, "vertical motion preserves tracked local head position");
        rise(1, .1f, false);
        rise(1, .1f, true);
        Near(origin.position, before, "resume-held height remains disarmed");
        rise(0, .1f, true);
        rise(-1, .1f, true);
        Near(origin.position, before - Vector3.up * .05f, "neutral enables descent after resume");
        UnityEngine.Object.DestroyImmediate(origin.gameObject);
    }
    static void RunAll()
    {
        string output = Environment.GetEnvironmentVariable("GHVR_INPUT_FIXTURE_OUTPUT");
        try
        {
            Check(Application.unityVersion == "2021.3.5f1", "game-exact Unity required");
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            // The native XR descriptor specializes the real package Touch layout, including distinct pose state offsets.
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>("Haptic");
            InputSystem.RegisterLayout(typeof(OculusTouchControllerProfile.OculusTouchController).GetProperty("devicePose").PropertyType, "Pose");
            InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>(matches:
                new InputDeviceMatcher().WithInterface("XRInput").WithProduct("Oculus Touch Controller OpenXR"));
            var features = new List<XRFeatureDescriptor>
            { new XRFeatureDescriptor { name = "thumbstick", featureType = FeatureType.Axis2D } };
            foreach (string pose in new[] { "devicePose", "pointer" })
            {
                foreach (var pair in new[] {
                    ("isTracked", FeatureType.Binary), ("trackingState", FeatureType.DiscreteStates),
                    ("position", FeatureType.Axis3D), ("rotation", FeatureType.Rotation),
                    ("velocity", FeatureType.Axis3D), ("angularVelocity", FeatureType.Axis3D) })
                    features.Add(new XRFeatureDescriptor { name = pose + "/" + pair.Item1, featureType = pair.Item2 });
            }
            var descriptor = new XRDeviceDescriptor { deviceId = 93,
                characteristics = InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.HeldInHand | InputDeviceCharacteristics.Left,
                inputFeatures = features };
            var device = InputSystem.AddDevice(new InputDeviceDescription { interfaceName = "XRInputV1",
                product = "Oculus Touch Controller OpenXR", capabilities = JsonUtility.ToJson(descriptor) });
            InputSystem.SetDeviceUsage(device, "LeftHand");
            Check(device["devicePose/position"].stateBlock.byteOffset != device["pointer/position"].stateBlock.byteOffset,
                "real descriptor separates grip and aim state");
            Run(typeof(GloomhavenVR.Quest.QuestProbePoseInput), typeof(GloomhavenVR.Quest.QuestProbeLocomotion), device);
            var cases = new Dictionary<string, string> { { "AimDefect", "aim independent from grip" },
                { "TrackingDefect", "rotation-invalid aim rejected" }, { "PivotDefect", "snap turn pivots about actual head" },
                { "ResumeDefect", "resume-held sticks remain disarmed" },
                { "HeightDefect", "vertical input follows world up" } };
            foreach (var defect in cases)
            {
                string name = defect.Key;
                bool rejected = false;
                try { Run(Type.GetType("GloomhavenVR." + name + ".QuestProbePoseInput, Assembly-CSharp"),
                    Type.GetType("GloomhavenVR." + name + ".QuestProbeLocomotion, Assembly-CSharp"), device); }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    rejected = error.Message == defect.Value;
                    if (!rejected) throw new Exception("Wrong defect failure " + name, error);
                    Debug.Log("Expected defect " + name + ": " + error.Message);
                }
                Check(rejected, "runtime fixture detects " + name);
            }
            InputSystem.RemoveDevice(device);
            File.WriteAllText(output, "PASS: " + assertions + " real-Unity assertions and five rejected runtime defects.\n");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); File.WriteAllText(output, "FAIL: " + error + "\n"); EditorApplication.Exit(1); }
    }
}
