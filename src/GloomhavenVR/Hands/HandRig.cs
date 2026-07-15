using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>Which hand. FROZEN Phase-2 API.</summary>
internal enum HandSide
{
    Left = 0,
    Right = 1
}

/// <summary>Finger index into <see cref="HandRig.GetFinger"/>. FROZEN Phase-2 API.</summary>
internal enum Finger
{
    Thumb = 0,
    Index = 1,
    Middle = 2,
    Ring = 3,
    Pinky = 4
}

/// <summary>The three driven joints of one finger (root = proximal/metacarpal, tip = distal).</summary>
internal readonly struct FingerJoints
{
    public readonly Transform Root;
    public readonly Transform Mid;
    public readonly Transform Tip;

    public FingerJoints(Transform root, Transform mid, Transform tip)
    {
        Root = root;
        Mid = mid;
        Tip = tip;
    }

    public bool IsValid => Root != null && Mid != null && Tip != null;
}

/// <summary>
/// The hand transform contract (FROZEN Phase-2 API). Both the procedural fallback hand
/// and bundle-loaded glove prefabs expose exactly this set of transforms, so swapping
/// visuals is a data change, not a code change.
///
/// Space conventions (see unity/GloomhavenVR.Assets/Assets/Bundle/Hands/README.md):
/// - <see cref="Root"/> sits at the wrist; +Z points along the fingers, +Y out of
///   the BACK of the hand (both hands — the mesh is mirrored, the frame is not).
/// - <see cref="PalmCenter"/>: +Y is the palm normal (OUT of the palm), +Z along fingers.
/// - All anchors are children of the tracked hand, so world positions/scales follow
///   the diorama rig automatically. Multiply "real meters" constants by the hand's
///   lossyScale when comparing world distances.
/// </summary>
internal sealed class HandRig
{
    /// <summary>Hand-space origin at the wrist (child of the tracked device pose, static visual offset).</summary>
    public Transform Root = null!;

    /// <summary>Wrist joint (== Root for the procedural hand).</summary>
    public Transform Wrist = null!;

    /// <summary>Center of the palm; up = palm normal (out of the palm).</summary>
    public Transform PalmCenter = null!;

    /// <summary>Tip of the index finger — poke origin.</summary>
    public Transform IndexTip = null!;

    /// <summary>Where grabbed objects snap (palm-aligned).</summary>
    public Transform GrabAnchor = null!;

    private readonly FingerJoints[] _fingers = new FingerJoints[5];

    /// <summary>World-space palm normal (points out of the palm).</summary>
    public Vector3 PalmNormal => PalmCenter.up;

    public FingerJoints GetFinger(Finger finger) => _fingers[(int)finger];

    internal void SetFinger(Finger finger, in FingerJoints joints) => _fingers[(int)finger] = joints;

    /// <summary>True once every contract transform is present.</summary>
    public bool IsComplete
    {
        get
        {
            if (Root == null || Wrist == null || PalmCenter == null || IndexTip == null || GrabAnchor == null)
                return false;
            for (int i = 0; i < _fingers.Length; i++)
            {
                if (!_fingers[i].IsValid)
                    return false;
            }
            return true;
        }
    }
}
