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
///   trigger + trigger CAPACITIVE touch → index (trigger untouched = pointing, even at
///   full grip; see VRHand.UpdateCurlTargets); grip value → middle/ring/pinky;
///   thumb capacitive touch (primaryTouch/secondaryTouch/primary2DAxisTouch) → thumb
///   (forced fully closed while the hand makes a fist, see VRHand.UpdateCurlTargets).
/// Actual joint rotations are smoothed (exponential lerp) to avoid jitter from the
/// binary touch signals and controller value noise.
///
/// FIST DIAGNOSTICS (on-device "mostly no fist" investigation): the full-curl joint
/// angles are read live from <see cref="HandsConfig"/> (defaults 75/95/65; thumb
/// 25/45/60 scaled proportionally), the actually-applied per-joint angles are recorded
/// for <see cref="GetAppliedAngles"/>, and <see cref="Tick"/> measures whether any
/// driven joint was rotated AWAY from what we applied last frame (an Animator or other
/// writer running after our Update would show up here) — read via
/// <see cref="ConsumeExternalDrift"/>.
/// </summary>
internal sealed class FingerCurler
{
    /// <summary>
    /// Default full-curl joint angles (degrees, local X) per joint index (root/mid/tip).
    /// Raised 65/80/50 → 75/95/65 (2026-07 hardware evidence: even at curl 1.0 the
    /// 65/80/50 fist read visibly open on the bulky styled hands — a real fist flexes
    /// roughly 90/100/70 at MCP/PIP/DIP). Render-verified per style via the
    /// unity/hand-prep/rig_hand.py fist matrix at the new angles.
    /// </summary>
    internal static readonly Vector3 DefaultFingerMaxAngles = new(75f, 95f, 65f);
    internal static readonly Vector3 DefaultThumbMaxAngles = new(25f, 45f, 60f);

    /// <summary>
    /// GLOVE-PINKY splay fix (2026-07 hardware round): the glove pinky MESH tube leans
    /// ~18° outward in XZ (PCA (+0.30,+0.25,+0.92)) while its bone chain is straight
    /// (0,0,1), so a full local-X curl leaves the curled pinky mesh visibly splayed
    /// outward from the fist. A Blender bone-ROLL fix cannot correct this — roll only
    /// spins the flexion axis within the plane perpendicular to the bone, and the
    /// desired mesh-plane axis's perpendicular component IS the existing +X — so the
    /// least-invasive fix is runtime: a small counter-abduction about the root joint's
    /// local Z (the palm normal at rest; Unity's ZXY euler order applies Z BEFORE the
    /// X flexion) coupled to the curl value. Positive local-Z adducts the LEFT pinky
    /// toward the ring finger (local +X→+Y under +Z; the bone tilts toward world -X);
    /// the RIGHT hand mirrors, so the sign flips per side. Glove style only — the
    /// styled hands' refit chains follow their mesh tubes and need no compensation.
    ///
    /// <para>2026-07, SUPERSEDED BY THE ASSET (value kept, default not yet changed):
    /// the premise above — "a Blender bone-ROLL fix cannot correct this" — is true but
    /// too narrow. Roll is constrained to the plane perpendicular to the bone; the
    /// rig contract only requires that local +X be the flexion axis, and re-framing the
    /// bone node (local +X := the digit's hinge axis, local Y := the digit direction,
    /// heads and mesh untouched) is unconstrained. unity/hand-prep/aim_curl_axes.py did
    /// exactly that to VRHand_{L,R}_rig.fbx: the glove's hinge-vs-digit error is now
    /// 0.00° on all five digits (was pinky +15.2°, index −10.5°, thumb −9.0°), so the
    /// splay this term compensates no longer exists. Render-measured on the fixed rig, a
    /// full fist at 0° already tucks the pinky beside the ring finger (tip spacing
    /// 11.9 mm, ring↔middle 22.4 mm); the historical 14° pulls it a further 3.4 mm and
    /// over-adducts. RECOMMENDED VALUE FOR THE FIXED RIG: 0. The default is left at 14
    /// here because it is a persisted user setting — change it deliberately, not as a
    /// side effect.</para>
    /// </summary>
    internal const float DefaultGlovePinkyCounterAbductionDeg = 14f;

    /// <summary>
    /// Per-STYLE curl-range clamp, indexed by (int)<see cref="HandStyle"/> (Glove/
    /// Plate/Arcane). History: the first styled builds over-closed ("donut" tips)
    /// because the skin weights dragged palm membranes, so 0.72/0.85 clamps were
    /// added — but combined with the round-2 weight caps that froze the MCP
    /// knuckles, the result was "almost only the fingertips move". With the round-3
    /// rig (adaptive digit caps in unity/hand-prep/rig_hand.py: knuckle zone
    /// t&gt;=-0.10 owned by its finger, caps scaled to measured shell thickness) the
    /// posed-mesh closure metric puts Plate/Arcane fingertips 65-76 mm from the palm
    /// anchor at FULL range — the same closure band as the accepted glove
    /// (59-72 mm) — with no tip-through-palm in the 9-pose render matrix. So all
    /// styles now run the full default-angle range. Kept as a tuning point for
    /// future styles whose authored rest pose over- or under-closes.
    ///
    /// <para>KEEP — this is NOT dead code, despite being all-1.0 (refactor Batch D, verified at
    /// HEAD): the constructor below indexes it every time a hand is built, so it is a live
    /// lookup whose current values happen to be identity. "All entries are 1" is the RESULT the
    /// round-3 rig achieved, and the history above is the record of what the earlier
    /// 0.72/0.85 clamps cost.</para>
    /// </summary>
    private static readonly float[] StyleCurlScale = { 1f, 1f, 1f };

    /// <summary>Smoothing rate (1/s). ~60 ms to close most of the gap.</summary>
    private const float LerpSpeed = 18f;

    private readonly HandRig _rig;
    private readonly float _curlScale;
    private readonly Quaternion[][] _baseRotations = new Quaternion[5][];
    private readonly float[] _current = new float[5];
    private readonly float[] _target = new float[5];

    // Diagnostics: what THIS curler actually wrote last Tick, per finger/joint.
    private readonly Vector3[] _appliedAngles = new Vector3[5];
    private readonly Quaternion[][] _lastWritten = new Quaternion[5][];
    private bool _hasWritten;
    private float _maxExternalDriftDeg;

    /// <summary>±1 on the glove (sign per hand side), 0 for styled hands — see
    /// <see cref="DefaultGlovePinkyCounterAbductionDeg"/>.</summary>
    private readonly float _pinkySplaySign;

    internal FingerCurler(HandRig rig, HandSide side)
    {
        _rig = rig;
        _curlScale = StyleCurlScale[(int)HandStyles.Clamp((int)rig.VisualStyle)];
        _pinkySplaySign = rig.VisualStyle == HandStyle.Glove
            ? (side == HandSide.Left ? 1f : -1f)
            : 0f;
        for (int f = 0; f < 5; f++)
        {
            FingerJoints joints = rig.GetFinger((Finger)f);
            _baseRotations[f] = new[]
            {
                joints.Root.localRotation,
                joints.Mid.localRotation,
                joints.Tip.localRotation
            };
            _lastWritten[f] = new Quaternion[3];
            // Slight rest curl so an idle hand does not look like a plank.
            _current[f] = _target[f] = 0.1f;
        }
    }

    /// <summary>Current smoothed curl of a finger (0..1) — poke/pose logic reads this.</summary>
    public float GetCurl(Finger finger) => _current[(int)finger];

    /// <summary>Set the target curl of a finger (clamped 0..1).</summary>
    public void SetTarget(Finger finger, float curl) => _target[(int)finger] = Mathf.Clamp01(curl);

    /// <summary>The per-joint angles (degrees: root/mid/tip) actually written on the last <see cref="Tick"/>.</summary>
    public Vector3 GetAppliedAngles(Finger finger) => _appliedAngles[(int)finger];

    /// <summary>
    /// Largest angle (degrees) any driven joint had been rotated away from the value
    /// this curler wrote, measured at the start of each <see cref="Tick"/> since the
    /// last call. Nonzero ⇒ something ELSE (Animator pass, other script) overwrites
    /// the finger bones after our Update — the smoking gun for "curl applied but the
    /// rendered hand does not close". Resets on read.
    /// </summary>
    public float ConsumeExternalDrift()
    {
        float drift = _maxExternalDriftDeg;
        _maxExternalDriftDeg = 0f;
        return drift;
    }

    /// <summary>Advance smoothing and write joint rotations. Called once per frame by VRHand.</summary>
    public void Tick(float deltaTime)
    {
        // Live-tunable max angles ([Hands] CurlProximal/CurlMiddle/CurlTip). The thumb
        // keeps its authored 25/45/60 proportions by scaling with the finger ratios.
        Vector3 fingerMax = HandsConfig.FingerMaxAnglesSafe(DefaultFingerMaxAngles) * _curlScale;
        Vector3 thumbMax = new(
            DefaultThumbMaxAngles.x * (fingerMax.x / DefaultFingerMaxAngles.x),
            DefaultThumbMaxAngles.y * (fingerMax.y / DefaultFingerMaxAngles.y),
            DefaultThumbMaxAngles.z * (fingerMax.z / DefaultFingerMaxAngles.z));

        float k = 1f - Mathf.Exp(-LerpSpeed * deltaTime);
        for (int f = 0; f < 5; f++)
        {
            _current[f] = Mathf.Lerp(_current[f], _target[f], k);

            FingerJoints joints = _rig.GetFinger((Finger)f);
            Vector3 max = f == (int)Finger.Thumb ? thumbMax : fingerMax;
            Quaternion[] baseRot = _baseRotations[f];
            Quaternion[] written = _lastWritten[f];
            float curl = _current[f];

            // External-overwrite detector: if the bone no longer holds what WE wrote
            // last frame, another writer ran after us (Animator update, other script).
            if (_hasWritten)
            {
                float drift = Quaternion.Angle(joints.Root.localRotation, written[0]);
                drift = Mathf.Max(drift, Quaternion.Angle(joints.Mid.localRotation, written[1]));
                drift = Mathf.Max(drift, Quaternion.Angle(joints.Tip.localRotation, written[2]));
                if (drift > _maxExternalDriftDeg)
                    _maxExternalDriftDeg = drift;
            }

            _appliedAngles[f] = max * curl;
            // Glove-pinky counter-abduction: curl-coupled local-Z on the ROOT joint only.
            float rootRz = f == (int)Finger.Pinky && _pinkySplaySign != 0f
                ? _pinkySplaySign * HandsConfig.GlovePinkyCounterAbductionSafe(
                    DefaultGlovePinkyCounterAbductionDeg) * curl
                : 0f;
            written[0] = joints.Root.localRotation = baseRot[0] * Quaternion.Euler(max.x * curl, 0f, rootRz);
            written[1] = joints.Mid.localRotation = baseRot[1] * Quaternion.Euler(max.y * curl, 0f, 0f);
            written[2] = joints.Tip.localRotation = baseRot[2] * Quaternion.Euler(max.z * curl, 0f, 0f);
        }
        _hasWritten = true;
    }
}
