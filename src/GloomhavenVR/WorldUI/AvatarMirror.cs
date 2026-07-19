using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// A classic VR "mirror" preview of the LOCAL player's own avatar — the chosen head
/// <see cref="HeadMaskLibrary">mask</see> + two floating hands (the same visuals
/// <see cref="RemoteAvatar"/> renders for peers) — so the player can see how they look before
/// / while other VR players see them. Purely local + cosmetic: it is INDEPENDENT of the net
/// send, so it works in single-player and when the networking hook is off. Toggled live by
/// <c>[Net] MirrorEnabled</c>.
///
/// The reflection is a real planar mirror: the local head + hand world poses are reflected
/// across a VERTICAL plane <see cref="MirrorDistance"/> in front of the head (normal =
/// horizontal head-forward). Reflecting position AND the forward/up basis means that when the
/// player leans, tilts or reaches, the reflection mirrors it — exactly like looking in a glass.
/// The mask faces back toward the player (the reflected +Z points at the head), so they see the
/// front of their own mask.
///
/// Sources are the SAME ones <see cref="LocalRigSampler"/> samples (the owned head camera +
/// <see cref="VRHands"/>), so the mirror matches what is broadcast. Rendered on the mod layer
/// (<see cref="VRLayers"/>) so the owned head camera draws it, unlit (the void has no lights).
/// Poses are written directly every frame (a mirror is 1:1, never eased) so the first enabled
/// frame is already correct — no origin streak. No per-frame allocations in the hot path; every
/// game reference is guarded so a torn-down rig is a silent no-op.
/// </summary>
internal sealed class AvatarMirror
{
    /// <summary>Distance (real metres, at scale 1) from the head to the mirror plane.</summary>
    private const float MirrorDistance = 0.7f;

    private GameObject? _root;
    private Transform? _headHolder;
    private Transform? _leftHolder;
    private Transform? _rightHolder;

    private HandRig? _leftRig;
    private HandRig? _rightRig;
    private FingerCurler? _leftCurler;
    private FingerCurler? _rightCurler;

    private int _appliedMaskId = -1;
    private float _appliedScale = -1f;
    private Vector3 _lastNormal = Vector3.forward; // reused when head-forward is near-vertical

    // Neutral tint for the placeholder head (until the real masks ship in the bundle).
    private static readonly Color PlaceholderTint = new(0.70f, 0.72f, 0.78f);

    /// <summary>Per-frame entry point. Builds/updates the mirror while enabled, tears it down when
    /// disabled or when no head camera exists. Guarded — never throws into the tick chain.</summary>
    public void Tick()
    {
        bool want = NetModule.MirrorEnabled != null && NetModule.MirrorEnabled.Value;
        Camera? head = VRRigDriver.HeadCamera;

        if (!want || head == null)
        {
            if (_root != null)
                Teardown();
            return;
        }

        if (_root == null)
            Build();
        if (_root == null || _headHolder == null || _leftHolder == null || _rightHolder == null)
            return;

        // Swap the head visual if the local mask choice changed while the mirror is open.
        int maskId = LocalRigSampler.LocalMaskId();
        if (maskId != _appliedMaskId)
            BuildHead(maskId);

        // Match the on-table size of the local rig (diorama zoom), like RemoteAvatar.
        Transform? rigRoot = VRRigDriver.RigRoot;
        float scale = rigRoot != null ? rigRoot.lossyScale.x : 1f;
        if (!(scale > 0f))
            scale = 1f;
        if (!Mathf.Approximately(scale, _appliedScale))
        {
            _appliedScale = scale;
            _headHolder.localScale = Vector3.one * scale;
            _leftHolder.localScale = Vector3.one * scale;
            _rightHolder.localScale = Vector3.one * scale;
        }

        // Build the mirror plane from the head: a point MirrorDistance ahead (scaled with the
        // diorama so the mirror sits a comfortable arm's length away at any zoom), normal =
        // horizontal head-forward.
        Transform ht = head.transform;
        Vector3 headPos = ht.position;
        Vector3 fwd = ht.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
            fwd = _lastNormal; // looking near-straight up/down: keep the last stable normal
        else
            fwd.Normalize();
        _lastNormal = fwd;
        Vector3 planePoint = headPos + fwd * (MirrorDistance * scale);

        // Head: reflect the head-camera pose.
        Reflect(headPos, ht.rotation, planePoint, fwd, out Vector3 hp, out Quaternion hr);
        _headHolder.SetPositionAndRotation(hp, hr);

        UpdateHand(_leftHolder, _leftCurler, VRHands.Left, planePoint, fwd);
        UpdateHand(_rightHolder, _rightCurler, VRHands.Right, planePoint, fwd);
    }

    private void UpdateHand(Transform holder, FingerCurler? curler, VRHand? hand,
        Vector3 planePoint, Vector3 normal)
    {
        if (hand == null || !hand.IsTracked || hand.Rig == null || hand.Rig.Root == null)
        {
            if (holder.gameObject.activeSelf)
                holder.gameObject.SetActive(false);
            return;
        }

        Transform t = hand.Rig.Root;
        Reflect(t.position, t.rotation, planePoint, normal, out Vector3 p, out Quaternion r);
        if (!holder.gameObject.activeSelf)
            holder.gameObject.SetActive(true);
        holder.SetPositionAndRotation(p, r);

        if (curler != null)
        {
            curler.SetTarget(Finger.Thumb, hand.GetCurl(Finger.Thumb));
            curler.SetTarget(Finger.Index, hand.GetCurl(Finger.Index));
            curler.SetTarget(Finger.Middle, hand.GetCurl(Finger.Middle));
            curler.SetTarget(Finger.Ring, hand.GetCurl(Finger.Ring));
            curler.SetTarget(Finger.Pinky, hand.GetCurl(Finger.Pinky));
            curler.Tick(Time.unscaledDeltaTime);
        }
    }

    /// <summary>Reflect a world pose across the vertical plane (point, unit normal). Position uses
    /// the plane; orientation reflects the forward/up basis (a mirror flips handedness — for the
    /// rigid mask + hands this reads exactly like a glass reflection).</summary>
    private static void Reflect(Vector3 pos, Quaternion rot, Vector3 planePoint, Vector3 n,
        out Vector3 outPos, out Quaternion outRot)
    {
        outPos = pos - 2f * Vector3.Dot(pos - planePoint, n) * n;

        Vector3 fwd = rot * Vector3.forward;
        Vector3 up = rot * Vector3.up;
        Vector3 rFwd = fwd - 2f * Vector3.Dot(fwd, n) * n;
        Vector3 rUp = up - 2f * Vector3.Dot(up, n) * n;
        outRot = rFwd.sqrMagnitude > 1e-8f && rUp.sqrMagnitude > 1e-8f
            ? Quaternion.LookRotation(rFwd, rUp)
            : rot;
    }

    // ---- lifecycle ----------------------------------------------------------------------

    private void Build()
    {
        _root = new GameObject("GloomhavenVR.AvatarMirror");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        _headHolder = new GameObject("Head").transform;
        _headHolder.SetParent(_root.transform, worldPositionStays: false);
        BuildHead(LocalRigSampler.LocalMaskId());

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

        _appliedScale = -1f;
        VRLayers.Apply(_root);
        VRLog.Info("WorldUI", "Avatar mirror enabled (local self-preview).");
    }

    private void BuildHead(int maskId)
    {
        if (_headHolder == null)
            return;
        _appliedMaskId = Mathf.Clamp(maskId, 0, HeadMaskLibrary.MaskCount - 1);
        for (int i = _headHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_headHolder.GetChild(i).gameObject);
        HeadMaskLibrary.BuildHead(_headHolder, _appliedMaskId, PlaceholderTint);
        if (_root != null)
            VRLayers.Apply(_root);
    }

    private void Teardown()
    {
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        _headHolder = null;
        _leftHolder = null;
        _rightHolder = null;
        _leftRig = null;
        _rightRig = null;
        _leftCurler = null;
        _rightCurler = null;
        _appliedMaskId = -1;
        _appliedScale = -1f;
        VRLog.Info("WorldUI", "Avatar mirror disabled.");
    }

    /// <summary>Module-shutdown / hot-reload teardown.</summary>
    public void Shutdown()
    {
        if (_root != null)
            Teardown();
    }
}
