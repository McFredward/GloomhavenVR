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

    private readonly HandRig? _leftRig;
    private readonly HandRig? _rightRig;
    private readonly FingerCurler? _leftCurler;
    private readonly FingerCurler? _rightCurler;

    private AvatarState _target;
    private bool _hasTarget;
    private float _appliedScale = -1f;
    private int _appliedMaskId = -1; // which HeadMaskLibrary mask the head currently shows

    /// <summary>Seconds since the last accepted packet (staleness bookkeeping).</summary>
    public float TimeSinceUpdate { get; private set; }

    public int PlayerId { get; }

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
        _leftRig = HandVisuals.Build(_leftHolder, HandSide.Left);
        _leftCurler = _leftRig != null ? new FingerCurler(_leftRig) : null;
        _leftHolder.gameObject.SetActive(false);

        _rightHolder = new GameObject("Hand_Right").transform;
        _rightHolder.SetParent(_root.transform, worldPositionStays: false);
        _rightRig = HandVisuals.Build(_rightHolder, HandSide.Right);
        _rightCurler = _rightRig != null ? new FingerCurler(_rightRig) : null;
        _rightHolder.gameObject.SetActive(false);

        // Whole subtree onto the mod layer so the owned head camera renders it (no-op when
        // VR is not running, exactly like the local hands).
        VRLayers.Apply(_root);

        VRLog.Info("Net", $"Remote avatar created for player {playerId}.");
    }

    /// <summary>Accept a freshly-decoded state as the new interpolation target.</summary>
    public void SetTarget(in AvatarState state)
    {
        _target = state;
        _hasTarget = true;
        TimeSinceUpdate = 0f;

        // Swap the head mask when the sender's choice changes (cheap; only on change).
        if (state.MaskId != _appliedMaskId)
            BuildHeadMask(state.MaskId);

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

    /// <summary>Per-frame interpolation toward the latest target. Call from the driver's Update.</summary>
    public void Tick(float deltaTime)
    {
        TimeSinceUpdate += deltaTime;
        // Defensive: if our root was destroyed out from under us (should not happen — it is
        // DontDestroyOnLoad + HideAndDontSave and owned solely by us) skip rather than throw.
        if (_root == null || !_hasTarget)
            return;

        float dt = Mathf.Max(deltaTime, 0f);
        float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);

        UpdatePart(_headHolder, _target.HeadValid, in _target.Head, k);
        UpdateHand(_leftHolder, _leftCurler, in _target.Left, _target.HasFingers, k, dt);
        UpdateHand(_rightHolder, _rightCurler, in _target.Right, _target.HasFingers, k, dt);
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
        if (_root != null)
            Object.Destroy(_root);
        VRLog.Info("Net", $"Remote avatar destroyed for player {PlayerId}.");
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
