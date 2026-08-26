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
    /// Which HAND STYLE the sender picked in VR settings (<c>[Hands] HandStyle</c>:
    /// 0 Glove / 1 Plate / 2 Arcane — see <see cref="Hands.HandStyle"/>). Carried as an
    /// ADDITIVE trailing byte gated by <see cref="NetProtocol.FlagHandStyle"/>, so v3
    /// peers built before this field simply ignore it and render the default Glove hands
    /// (no version bump, no compat break). Receivers clamp to the shipped style range and
    /// degrade Plate/Arcane to Glove when the styled prefab is missing from their bundle.
    /// </summary>
    public byte HandStyle;

    /// <summary>
    /// Sender's rig <c>lossyScale</c> (game units per real meter). The receiver sizes the
    /// remote hands/head by this so a 15 cm real hand reads the same physical size above the
    /// shared board regardless of the sender's diorama zoom. 1 when unknown.
    /// </summary>
    public float WorldScale;

    /// <summary>
    /// True when the sender is physically holding a figure this frame (wire flag
    /// <see cref="NetProtocol.FlagHeldFigure"/>). When set, <see cref="HeldFigureActorId"/> and
    /// <see cref="HeldFigurePose"/> carry the grabbed figure's stable id and world pose so the
    /// receiver can move the same figure in the grabber's hand. Purely cosmetic.
    /// </summary>
    public bool HasHeldFigure;

    /// <summary>Stable cross-client id of the held figure (meaningful only when
    /// <see cref="HasHeldFigure"/>). 0 when none.</summary>
    public int HeldFigureActorId;

    /// <summary>World-frame pose of the held figure (meaningful only when
    /// <see cref="HasHeldFigure"/>).</summary>
    public RigPose HeldFigurePose;

    /// <summary>
    /// True when the sender's DOMINANT hand is the RIGHT hand (wire flag
    /// <see cref="NetProtocol.FlagDominantRight"/>). Lets the receiver place the cosmetic card fan
    /// on the correct non-dominant side. Defaults true (right-dominant) when unknown.
    /// </summary>
    public bool DominantRight;

    /// <summary>
    /// True when the sender grip-holds a single card in a hand this frame (wire flag
    /// <see cref="NetProtocol.FlagHeldCard"/> — ADDITIVE trailing field after the hand-style
    /// byte; older peers ignore it). When set, <see cref="HeldCardPose"/> carries the card's
    /// world pose; receivers render one card-BACK slab there. No card identity is transmitted.
    /// </summary>
    public bool HasHeldCard;

    /// <summary>World-frame pose of the held card (meaningful only when
    /// <see cref="HasHeldCard"/>).</summary>
    public RigPose HeldCardPose;
}
