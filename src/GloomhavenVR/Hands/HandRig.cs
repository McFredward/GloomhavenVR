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

    /// <summary>
    /// Wrist joint. What lands here is the zero-offset <c>Socket_Wrist</c> child that
    /// <c>HandVisuals.CreateSocket</c> interposes for scale compensation — on
    /// <c>Anchor_Wrist</c> for a prefab hand, on <see cref="Root"/> itself for the
    /// procedural one (whose wrist frame is therefore Root's frame). It is never the
    /// same object as <see cref="Root"/>.
    ///
    /// <para>ITS AXES ARE NOT <see cref="Root"/>'S — read this before parenting anything to it.
    /// In every shipped prefab (VRHand, VRHandPlate, VRHandArcane, L and R alike) <c>Anchor_Wrist</c>
    /// carries a +90° X rotation relative to the prefab root, so relative to the Root frame
    /// documented above:
    /// <code>
    ///   wrist +X -> root +X   across the hand      (identical on both hands, like Root's +X)
    ///   wrist +Y -> root +Z   ALONG THE FINGERS
    ///   wrist +Z -> root -Y   OUT OF THE PALM
    /// </code>
    /// The prefabs state it in their own data three times over: <c>Anchor_Middle_Root</c> sits at
    /// wrist-local (0.010, 0.096, -0.017), nearly 10 cm straight up +Y to the knuckle;
    /// <c>Anchor_Palm</c> at (0.008, 0.049, 0.003), the palm centre half way along that line, with
    /// <c>Anchor_Grab</c> a further centimetre out along +Z where a held object rests ON the palm;
    /// and <c>Anchor_Palm</c>'s own rotation (0, .7071, .7071, 0) — a half turn about (0,1,1) —
    /// maps its local +Y, which the Hands README requires to point OUT OF THE PALM, onto wrist +Z.
    /// No hand prefab carries a negative scale, so nothing parented here is mirrored by the rig.</para>
    ///
    /// <para>PARENTING A CANVAS HERE: a uGUI canvas is read from its <b>-Z</b> side (identity
    /// canvas, Unity's default camera at negative z), so a plate meant to be read off the PALM
    /// needs its -Z on wrist +Z — a half turn about wrist +Y, not the identity. Getting that
    /// backwards does not hide the plate; it shows it mirror-reversed, because uGUI's shader does
    /// not cull the back face. See <c>WorldUI.WristHud.PalmFlat</c>.</para>
    ///
    /// <para>WHY THIS IS WRITTEN DOWN (2026-08-09): <c>WorldUI.WristHud</c> parented its plate here
    /// and reasoned in Root's frame for four consecutive rounds. Nothing failed loudly — the pose
    /// dials simply absorbed the missing 90°, which is why every shipped trim was a ~-90° pitch and
    /// why the comment block above the rotation kept contradicting the hardware.</para>
    /// </summary>
    public Transform Wrist = null!;

    /// <summary>Center of the palm; up = palm normal (out of the palm).</summary>
    public Transform PalmCenter = null!;

    /// <summary>Tip of the index finger — poke origin.</summary>
    public Transform IndexTip = null!;

    /// <summary>
    /// Index-finger ROOT joint (knuckle). Curl-independent — the visible laser starts
    /// here, not at <see cref="IndexTip"/>: the tip curls with the trigger pull
    /// (FingerCurler), which made the beam swing on every press (hardware test #7).
    /// Optional (additive to the frozen P2 contract); null falls back to IndexTip.
    /// </summary>
    public Transform? IndexKnuckle;

    /// <summary>Where grabbed objects snap (palm-aligned).</summary>
    public Transform GrabAnchor = null!;

    /// <summary>
    /// The style the visuals were ACTUALLY built with (additive to the frozen P2
    /// contract). Can differ from the requested <c>[Hands] HandStyle</c> when a styled
    /// prefab is missing from an old bundle and the build degraded to the Glove pair —
    /// per-style tunables (scale/seat trims, VRHand.SyncVisualOffset) key off THIS so
    /// a degraded glove is never shrunk by the Plate scale.
    /// </summary>
    public HandStyle VisualStyle = HandStyle.Glove;

    private readonly FingerJoints[] _fingers = new FingerJoints[5];

    /// <summary>World-space palm normal (points out of the palm).
    ///
    /// <para>KEEP — unread at HEAD, deliberately (refactor Batch D). This one line is the
    /// NAMED statement of the palm-frame contract that <c>HandVisuals.FillMissingAnchors</c>'
    /// 180° Z flip exists to satisfy (INVARIANTS §5) and that <c>docs/INTERFACES-P2.md</c>
    /// §HandRig documents as frozen surface. Deleting it removes a contract statement to save
    /// a line — and the next person to wire a glove asset would have to rediscover which way
    /// "out of the palm" points.</para></summary>
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
