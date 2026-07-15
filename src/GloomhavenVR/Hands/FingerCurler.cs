using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// Drives per-finger curl (0 = straight .. 1 = fully curled) by rotating the three
/// <see cref="HandRig"/> joints of each finger around their local X axis (the
/// convention baked into both the procedural hand and the bundle prefab contract:
/// +Z along the finger, +Y away from the palm ⇒ positive local-X rotation curls
/// toward the palm). LCVR FingerCurler pattern, no Animator involved.
///
/// Curl targets are set once per frame from controller input by <see cref="VRHand"/>:
///   trigger value → index; grip value → middle/ring/pinky;
///   thumb capacitive touch (primaryTouch/secondaryTouch/primary2DAxisTouch) → thumb.
/// Actual joint rotations are smoothed (exponential lerp) to avoid jitter from the
/// binary touch signals and controller value noise.
/// </summary>
internal sealed class FingerCurler
{
    /// <summary>Full-curl joint angles (degrees, local X) per joint index (root/mid/tip).</summary>
    private static readonly Vector3 FingerMaxAngles = new(65f, 80f, 50f);
    private static readonly Vector3 ThumbMaxAngles = new(25f, 45f, 60f);

    /// <summary>Smoothing rate (1/s). ~60 ms to close most of the gap.</summary>
    private const float LerpSpeed = 18f;

    private readonly HandRig _rig;
    private readonly Quaternion[][] _baseRotations = new Quaternion[5][];
    private readonly float[] _current = new float[5];
    private readonly float[] _target = new float[5];

    internal FingerCurler(HandRig rig)
    {
        _rig = rig;
        for (int f = 0; f < 5; f++)
        {
            FingerJoints joints = rig.GetFinger((Finger)f);
            _baseRotations[f] = new[]
            {
                joints.Root.localRotation,
                joints.Mid.localRotation,
                joints.Tip.localRotation
            };
            // Slight rest curl so an idle hand does not look like a plank.
            _current[f] = _target[f] = 0.1f;
        }
    }

    /// <summary>Current smoothed curl of a finger (0..1) — poke/pose logic reads this.</summary>
    public float GetCurl(Finger finger) => _current[(int)finger];

    /// <summary>Set the target curl of a finger (clamped 0..1).</summary>
    public void SetTarget(Finger finger, float curl) => _target[(int)finger] = Mathf.Clamp01(curl);

    /// <summary>Advance smoothing and write joint rotations. Called once per frame by VRHand.</summary>
    public void Tick(float deltaTime)
    {
        float k = 1f - Mathf.Exp(-LerpSpeed * deltaTime);
        for (int f = 0; f < 5; f++)
        {
            _current[f] = Mathf.Lerp(_current[f], _target[f], k);

            FingerJoints joints = _rig.GetFinger((Finger)f);
            Vector3 max = f == (int)Finger.Thumb ? ThumbMaxAngles : FingerMaxAngles;
            Quaternion[] baseRot = _baseRotations[f];
            float curl = _current[f];

            joints.Root.localRotation = baseRot[0] * Quaternion.Euler(max.x * curl, 0f, 0f);
            joints.Mid.localRotation = baseRot[1] * Quaternion.Euler(max.y * curl, 0f, 0f);
            joints.Tip.localRotation = baseRot[2] * Quaternion.Euler(max.z * curl, 0f, 0f);
        }
    }
}
