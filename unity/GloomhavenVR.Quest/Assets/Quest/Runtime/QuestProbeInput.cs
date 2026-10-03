using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace GloomhavenVR.Quest
{
    /// <summary>Reads a single OpenXR pose; grip and aim are deliberately separate bindings.</summary>
    public sealed class QuestProbePoseInput : IDisposable
    {
        readonly InputAction position, rotation, tracked, state;
        public string Layout => position.controls.Count > 0 ? position.controls[0].device.layout : "unbound";
        public bool Bound => position.controls.Count > 0 && rotation.controls.Count > 0 &&
            tracked.controls.Count > 0 && state.controls.Count > 0;

        public QuestProbePoseInput(string name, string positionPath, string rotationPath,
            string trackedPath, string statePath)
        {
            position = Create(name + " position", positionPath);
            rotation = Create(name + " rotation", rotationPath);
            tracked = Create(name + " tracked", trackedPath);
            state = Create(name + " state", statePath);
        }

        public static QuestProbePoseInput ForController(string name, string prefix, bool aim)
        {
            string pose = aim ? "pointer" : "devicePose";
            return new QuestProbePoseInput(name, prefix + pose + "/position", prefix + pose + "/rotation",
                prefix + pose + "/isTracked", prefix + pose + "/trackingState");
        }

        public static InputAction Create(string name, string binding)
        {
            var action = new InputAction(name, InputActionType.Value, binding);
            action.Enable();
            return action;
        }

        public bool TryRead(out Vector3 location, out Quaternion orientation)
        {
            location = Vector3.zero;
            orientation = Quaternion.identity;
            if (!Bound || tracked.ReadValue<float>() <= .5f ||
                (state.ReadValue<int>() & 3) != 3) return false;
            location = position.ReadValue<Vector3>();
            orientation = rotation.ReadValue<Quaternion>();
            return Valid(location, orientation);
        }

        public static bool Valid(Vector3 location, Quaternion orientation)
        {
            return Finite(location.x) && Finite(location.y) && Finite(location.z) &&
                Finite(orientation.x) && Finite(orientation.y) && Finite(orientation.z) && Finite(orientation.w) &&
                Mathf.Abs(Quaternion.Dot(orientation, orientation) - 1) < .05f;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool TryReadDevice(UnityEngine.XR.InputDevice device, out Vector3 location, out Quaternion orientation)
        {
            location = Vector3.zero;
            orientation = Quaternion.identity;
            return device.TryGetFeatureValue(CommonUsages.isTracked, out bool valid) && valid &&
                device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState flags) &&
                (flags & (InputTrackingState.Position | InputTrackingState.Rotation)) ==
                    (InputTrackingState.Position | InputTrackingState.Rotation) &&
                device.TryGetFeatureValue(CommonUsages.devicePosition, out location) &&
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out orientation) && Valid(location, orientation);
        }

        public void Dispose() { position.Dispose(); rotation.Dispose(); tracked.Dispose(); state.Dispose(); }
    }

    /// <summary>Diagnostic navigation changes only the tracking origin, never tracked local poses.</summary>
    public sealed class QuestProbeLocomotion
    {
        bool ready, turnLatched;
        public int snapTurns { get; private set; }
        public float distance { get; private set; }

        public void Suspend() { ready = false; turnLatched = false; }

        public void Step(Transform origin, Transform head, Vector2 movement, float turn, float deltaTime, bool enabled)
        {
            if (!enabled) { Suspend(); return; }
            if (!ready)
            {
                ready = movement.sqrMagnitude <= .04f && Mathf.Abs(turn) <= .3f;
                return; // Require neutral controls after startup, tracking loss or app resume.
            }
            if (movement.sqrMagnitude > .04f)
            {
                var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(origin.forward, Vector3.up);
                forward.Normalize();
                var right = Vector3.Cross(Vector3.up, forward);
                var axis = Vector2.ClampMagnitude(movement, 1);
                var offset = (forward * axis.y + right * axis.x) * (.75f * Mathf.Clamp(deltaTime, 0, .1f));
                origin.position += offset;
                distance += offset.magnitude;
            }
            if (Mathf.Abs(turn) <= .3f) turnLatched = false;
            if (!turnLatched && Mathf.Abs(turn) >= .7f)
            {
                // Rotate around the current head, so turning does not orbit the seated/standing player.
                origin.RotateAround(head.position, Vector3.up, Mathf.Sign(turn) * 30);
                turnLatched = true;
                ++snapTurns;
            }
        }
    }
}
