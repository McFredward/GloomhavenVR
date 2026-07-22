using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Visual proxy for one remote VR player: a floating head "mask" + two floating hands
/// (Demeo style — no humanoid IK), anchored in the SHARED world frame and eased toward the
/// latest received pose with Lerp/Slerp (never snapped). Purely cosmetic; touches no game
/// state. Built lazily by <see cref="NetAvatarDriver"/> on the first rig packet from a peer
/// and destroyed on player-left or staleness.
///
/// Hands reuse the mod's own <see cref="HandVisuals"/> (bundle glove or procedural fallback)
/// so remote hands look exactly like the local ones. The head mask is the one the SENDER
/// picked in their VR settings (<see cref="AvatarState.MaskId"/>): it loads
/// <c>Assets/Bundle/Head/Mask_&lt;id&gt;.prefab</c> from the ALREADY-LOADED mod bundle via
/// <see cref="HeadMaskLibrary"/> (see PLAN.md "mask asset contract"); until the masks ship, a
/// low-poly placeholder head stands in so the feature is testable now. A peer changing their
/// mask at runtime rebuilds only the head (cheap; only on change).
///
/// Sender scale: each part holder is scaled by the sender's rig <c>WorldScale</c> so a remote
/// player's 15 cm hand reads the same physical size above the shared board regardless of the
/// sender's diorama zoom. Positions are absolute world (game units).
/// </summary>
internal sealed class RemoteAvatar
{
    private readonly GameObject _root;
    private readonly Transform _headHolder;
    private readonly Transform _leftHolder;
    private readonly Transform _rightHolder;
    private readonly Color _tint;

    // Not readonly: rebuilt in place when the sender switches their hand style.
    private HandRig? _leftRig;
    private HandRig? _rightRig;
    private FingerCurler? _leftCurler;
    private FingerCurler? _rightCurler;

    private readonly RemoteHandFan _handFan;
    private readonly RemoteControlBoard _controlBoard;

    private AvatarState _target;
    private bool _hasTarget;
    private float _appliedScale = -1f;
    private int _appliedMaskId = -1; // which HeadMaskLibrary mask the head currently shows
    private int _appliedHandStyle = -1; // which HandStyle the hand holders currently wear

    /// <summary>Seconds since the last accepted packet (staleness bookkeeping).</summary>
    public float TimeSinceUpdate { get; private set; }

    public int PlayerId { get; }

    // ---- PUBLIC SEAM (read-only) — the feature workers (ghost fan / control board) build off
    //      these holders + received data without reaching into RemoteAvatar's privates. -------

    /// <summary>The avatar's root transform (world origin of the whole proxy).</summary>
    public Transform Root => _root.transform;

    /// <summary>Head-mask holder transform.</summary>
    public Transform HeadHolder => _headHolder;

    /// <summary>Left-hand holder transform.</summary>
    public Transform LeftHandHolder => _leftHolder;

    /// <summary>Right-hand holder transform.</summary>
    public Transform RightHandHolder => _rightHolder;

    /// <summary>The holder for the sender's NON-dominant hand (where the card fan sits). When the
    /// sender is right-dominant the non-dominant hand is the Left hand, and vice versa.</summary>
    public Transform NonDominantHandHolder => DominantRight ? LeftHandHolder : RightHandHolder;

    /// <summary>Stable per-player tint (matches the head/hand tint).</summary>
    public Color Tint => _tint;

    /// <summary>The sender-scale currently applied to the part holders (1 until the first packet).</summary>
    public float AppliedScale => _appliedScale > 0f ? _appliedScale : 1f;

    // ---- received extras / rig data (world frame) --------------------------------------------

    /// <summary>True when the sender broadcast a control-board world pose.</summary>
    public bool HasBoard { get; private set; }

    /// <summary>Control-board world position (valid when <see cref="HasBoard"/>).</summary>
    public Vector3 BoardPosition { get; private set; }

    /// <summary>Control-board world rotation (valid when <see cref="HasBoard"/>).</summary>
    public Quaternion BoardRotation { get; private set; } = Quaternion.identity;

    /// <summary>Control-board uniform scale (valid when <see cref="HasBoard"/>).</summary>
    public float BoardScale { get; private set; } = 1f;

    /// <summary>How many cards are in the sender's hand fan (rendered as backs only).</summary>
    public int HandCardCount { get; private set; }

    /// <summary>True when the sender's dominant hand is the RIGHT hand (default true).</summary>
    public bool DominantRight { get; private set; } = true;

    /// <summary>True when the sender is physically holding a figure this frame.</summary>
    public bool HasHeldFigure { get; private set; }

    /// <summary>Stable id of the held figure (valid when <see cref="HasHeldFigure"/>).</summary>
    public int HeldFigureActorId { get; private set; }

    /// <summary>Held-figure world position (valid when <see cref="HasHeldFigure"/>).</summary>
    public Vector3 HeldFigurePosition { get; private set; }

    /// <summary>Held-figure world rotation (valid when <see cref="HasHeldFigure"/>).</summary>
    public Quaternion HeldFigureRotation { get; private set; } = Quaternion.identity;

    public RemoteAvatar(int playerId)
    {
        PlayerId = playerId;

        _root = new GameObject($"GloomhavenVR.RemoteAvatar[{playerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.position = Vector3.zero;
        _root.transform.rotation = Quaternion.identity;
        _root.transform.localScale = Vector3.one;

        _tint = TintFor(playerId);

        _headHolder = new GameObject("Head").transform;
        _headHolder.SetParent(_root.transform, worldPositionStays: false);
        BuildHeadMask(0); // mask 0 until the first packet reports the sender's real choice
        _headHolder.gameObject.SetActive(false);

        _leftHolder = new GameObject("Hand_Left").transform;
        _leftHolder.SetParent(_root.transform, worldPositionStays: false);
        _leftHolder.gameObject.SetActive(false);

        _rightHolder = new GameObject("Hand_Right").transform;
        _rightHolder.SetParent(_root.transform, worldPositionStays: false);
        _rightHolder.gameObject.SetActive(false);

        // Default Glove until the first packet reports the sender's real style —
        // mirrors the mask-0 default above.
        BuildHands(0);

        // Whole subtree onto the mod layer so the owned head camera renders it (no-op when
        // VR is not running, exactly like the local hands).
        VRLayers.Apply(_root);

        // Cosmetic add-ons built off the public seam (ghost card fan + read-only control board).
        // Foundation stubs today; the feature workers fill their bodies. Owned here: ticked from
        // Tick and torn down from Destroy.
        _handFan = new RemoteHandFan(this);
        _controlBoard = new RemoteControlBoard(this);

        VRLog.Info("Net", $"Remote avatar created for player {playerId}.");
    }

    /// <summary>Accept a freshly-decoded state as the new interpolation target.</summary>
    public void SetTarget(in AvatarState state)
    {
        _target = state;
        _hasTarget = true;
        TimeSinceUpdate = 0f;

        // Carry the rig packet's held-figure + dominant-hand data onto the seam (world frame:
        // the driver has already converted the poses). Consumed by the feature workers.
        DominantRight = state.DominantRight;
        HasHeldFigure = state.HasHeldFigure;
        if (state.HasHeldFigure)
        {
            HeldFigureActorId = state.HeldFigureActorId;
            HeldFigurePosition = state.HeldFigurePose.Position;
            HeldFigureRotation = state.HeldFigurePose.Rotation;
        }
        else
        {
            HeldFigureActorId = 0;
        }

        // Swap the head mask when the sender's choice changes (cheap; only on change).
        if (state.MaskId != _appliedMaskId)
            BuildHeadMask(state.MaskId);

        // Swap the hand pair when the sender's HAND STYLE changes (same pattern; peers
        // without the styled prefab in their bundle degrade to Glove inside HandVisuals).
        if (state.HandStyle != _appliedHandStyle)
            BuildHands(state.HandStyle);

        // Apply sender scale to the part holders when it changes (cosmetic sizing only).
        float scale = state.WorldScale > 0f ? state.WorldScale : 1f;
        if (!Mathf.Approximately(scale, _appliedScale))
        {
            _appliedScale = scale;
            _headHolder.localScale = Vector3.one * scale;
            _leftHolder.localScale = Vector3.one * scale;
            _rightHolder.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Accept a freshly-decoded EXTRAS packet (board pose + hand count + dominant hand).
    /// Poses are already in world frame (converted by the driver).</summary>
    public void SetExtras(in PresenceState p)
    {
        HasBoard = p.HasBoard;
        if (p.HasBoard)
        {
            BoardPosition = p.Board.Position;
            BoardRotation = p.Board.Rotation;
            BoardScale = p.BoardScale > 0f ? p.BoardScale : 1f;
        }
        HandCardCount = p.HandCardCount;
        DominantRight = p.DominantRight;
    }

    /// <summary>Per-frame interpolation toward the latest target. Call from the driver's Update.</summary>
    public void Tick(float deltaTime)
    {
        TimeSinceUpdate += deltaTime;
        // Defensive: if our root was destroyed out from under us (should not happen — it is
        // DontDestroyOnLoad + HideAndDontSave and owned solely by us) skip rather than throw.
        if (_root == null)
            return;

        float dt = Mathf.Max(deltaTime, 0f);

        if (_hasTarget)
        {
            float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);
            UpdatePart(_headHolder, _target.HeadValid, in _target.Head, k);
            UpdateHand(_leftHolder, _leftCurler, in _target.Left, _target.HasFingers, k, dt);
            UpdateHand(_rightHolder, _rightCurler, in _target.Right, _target.HasFingers, k, dt);
        }

        // Cosmetic add-ons (own their own guards; stubs today).
        _handFan.Tick(dt);
        _controlBoard.Tick(dt);
    }

    private static void UpdatePart(Transform holder, bool valid, in RigPose pose, float k)
    {
        if (!valid)
        {
            if (holder.gameObject.activeSelf) holder.gameObject.SetActive(false);
            return;
        }
        if (!holder.gameObject.activeSelf)
        {
            // First activation: snap to avoid a lerp streak from the origin.
            holder.SetPositionAndRotation(pose.Position, pose.Rotation);
            holder.gameObject.SetActive(true);
            return;
        }
        holder.SetPositionAndRotation(
            Vector3.Lerp(holder.position, pose.Position, k),
            Quaternion.Slerp(holder.rotation, pose.Rotation, k));
    }

    private static void UpdateHand(Transform holder, FingerCurler? curler, in HandStateSample hand, bool fingers, float k, float dt)
    {
        UpdatePart(holder, hand.Tracked, in hand.Pose, k);
        if (!hand.Tracked || curler == null)
            return;
        if (fingers)
        {
            curler.SetTarget(Finger.Thumb, hand.Curl0);
            curler.SetTarget(Finger.Index, hand.Curl1);
            curler.SetTarget(Finger.Middle, hand.Curl2);
            curler.SetTarget(Finger.Ring, hand.Curl3);
            curler.SetTarget(Finger.Pinky, hand.Curl4);
        }
        curler.Tick(dt);
    }

    public void Destroy()
    {
        _handFan.Destroy();
        _controlBoard.Destroy();
        if (_root != null)
            Object.Destroy(_root);
        VRLog.Info("Net", $"Remote avatar destroyed for player {PlayerId}.");
    }

    // ---- hands -------------------------------------------------------------------------

    /// <summary>(Re)build both hand visuals for the given wire hand-style: clears the hand
    /// holders' children, rebuilds the rigs + curlers with the sender's chosen prefab pair
    /// (Glove fallback inside <see cref="HandVisuals"/>), and re-applies the mod layer.
    /// Cheap and only on change — mirrors <see cref="BuildHeadMask"/>.</summary>
    private void BuildHands(int handStyle)
    {
        var style = Hands.HandStyles.Clamp(handStyle);
        _appliedHandStyle = handStyle;

        for (int i = _leftHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_leftHolder.GetChild(i).gameObject);
        for (int i = _rightHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_rightHolder.GetChild(i).gameObject);

        _leftRig = HandVisuals.Build(_leftHolder, HandSide.Left, style);
        _leftCurler = _leftRig != null ? new FingerCurler(_leftRig) : null;
        _rightRig = HandVisuals.Build(_rightHolder, HandSide.Right, style);
        _rightCurler = _rightRig != null ? new FingerCurler(_rightRig) : null;

        // HandVisuals.Build wrote the per-style visual scale onto the HOLDERS
        // (ApplyStyleScale) — force the sender-scale block in SetTarget to re-apply,
        // or a mid-session style swap leaves the remote hands at the wrong size.
        _appliedScale = -1f;

        // Keep the whole subtree on the mod layer so the owned head camera renders it.
        VRLayers.Apply(_root);
    }

    // ---- head mask ----------------------------------------------------------------------

    /// <summary>(Re)build the head mask for the given mask id: clears any existing head visual,
    /// instantiates the chosen <see cref="HeadMaskLibrary"/> prefab, or stands in the placeholder
    /// head when that mask has not shipped yet. Re-applies the mod layer so the head camera renders
    /// the new mesh.</summary>
    private void BuildHeadMask(int maskId)
    {
        maskId = Mathf.Clamp(maskId, 0, HeadMaskLibrary.MaskCount - 1);
        _appliedMaskId = maskId;

        // Clear any existing head visual (children of the holder).
        for (int i = _headHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_headHolder.GetChild(i).gameObject);

        HeadMaskLibrary.BuildHead(_headHolder, maskId, _tint);

        // Keep the whole subtree on the mod layer so the owned head camera renders it.
        VRLayers.Apply(_root);
    }

    /// <summary>Stable per-player tint so avatars are distinguishable at a glance.</summary>
    private static Color TintFor(int playerId)
    {
        float hue = (playerId * 0.61803398875f) % 1f; // golden-ratio hashing → spread hues
        return Color.HSVToRGB(hue, 0.45f, 1f);
    }
}
