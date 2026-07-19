using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>One rigid pose in the shared world frame (game units).</summary>
internal struct RigPose
{
    public Vector3 Position;
    public Quaternion Rotation;
}

/// <summary>One hand: pose + optional 5 quantized finger curls (0..1 → byte) + tracked flag.</summary>
internal struct HandStateSample
{
    public bool Tracked;
    public RigPose Pose;
    // Thumb, Index, Middle, Ring, Pinky curls (0..1). Only meaningful when the packet's
    // FlagHasFingers bit is set; always length 5.
    public float Curl0, Curl1, Curl2, Curl3, Curl4;
}

/// <summary>
/// The full cosmetic avatar state sampled from / applied to one VR player: head + two hands,
/// expressed in the SHARED world frame (see <see cref="AvatarSerializer"/> for the frame
/// contract) plus the sender's diorama scale so the receiver can size the floating hands to
/// match. Purely visual — never touches game state.
/// </summary>
internal struct AvatarState
{
    public bool HeadValid;
    public RigPose Head;

    public HandStateSample Left;
    public HandStateSample Right;

    public bool HasFingers;

    /// <summary>
    /// Which head "mask" the sender picked in VR settings (0..2 — exactly three masks ship in
    /// the bundle at <c>Assets/Bundle/Head/Mask_&lt;id&gt;.prefab</c>). Receivers clamp to that
    /// range and fall back to the placeholder head when the chosen mask is not in the bundle.
    /// </summary>
    public byte MaskId;

    /// <summary>
    /// Sender's rig <c>lossyScale</c> (game units per real meter). The receiver sizes the
    /// remote hands/head by this so a 15 cm real hand reads the same physical size above the
    /// shared board regardless of the sender's diorama zoom. 1 when unknown.
    /// </summary>
    public float WorldScale;
}
