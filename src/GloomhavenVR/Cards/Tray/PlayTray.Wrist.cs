using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Cards;

internal enum WristBoardHand { NonMain, Left, Right }

internal sealed partial class PlayTray
{
    // The wrist is a POSE SOURCE, never the parent. Hand visual roots have their own
    // scale and can be rebuilt on a style change; parenting the complete original board
    // there would resize it and destroy its docks along with the hand. Keep the same rig
    // parent and write the tracked wrist pose at Update/LateUpdate/before-render instead.
    // No continuous easing: the attached board must move as directly as the hand itself.
    private bool _wristAttached;
    private bool _wristReturning;
    private bool _wristReturnRebuild;
    private bool _wristKeepSeatOnDestroy;
    private Transform? _wristReturnSeat;
    private bool _wristReturnFollow;
    private Vector3 _wristFollowPosition;
    private Quaternion _wristFollowRotation;
    private float _wristFollowScale;
    private readonly FollowPinAnchor _wristReturnAnchor = new("GloomhavenVR.TrayWristReturn", dontDestroyOnLoad: true);
    private float _wristReturnConfiguredScale;
    private Transform? _wristSource;
    private Vector3 _wristBlendPosition;
    private Quaternion _wristBlendRotation;
    private Vector3 _wristBlendScale;
    private float _wristBlendStarted;

    internal bool WristControlsHidden => _wristAttached || _wristReturning || _wristReturnRebuild;
    private static bool WantsWrist => CardsConfig.WristBoardEnabled != null && CardsConfig.WristBoardEnabled.Value;

    internal static HandSide WristSide(WristBoardHand choice, bool mainRight) => choice switch
    {
        WristBoardHand.Left => HandSide.Left,
        WristBoardHand.Right => HandSide.Right,
        _ => mainRight ? HandSide.Left : HandSide.Right,
    };

    private bool TickWristAnchor()
    {
        if (_root == null || _anchorParent == null) return false;
        if (!WantsWrist)
        {
            // Rebuild restores its captured intermediate pose synchronously in RestorePose.
            // Do not consume the saved normal seat from the newly constructed default root.
            if (_wristReturnRebuild) return true;
            if (_wristAttached) BeginWristReturn();
            if (!_wristReturning) return false;
            RefreshWristControls();
            AdvanceWristReturn();
            return true;
        }
        if (!_wristAttached)
        {
            // Preserve the ordinary rig-relative frame once. A style rebuild destroys the
            // root, not the PlayTray instance, so its saved ordinary seat remains available.
            if (_wristReturnSeat == null && _placed) CaptureWristReturnSeat();
            CancelBoardReFace();
            _wristAttached = true;
            _wristReturning = false;
            _wristReturnRebuild = false;
            _root.SetParent(_anchorParent, worldPositionStays: true);
            _anchor.DestroyHolder(immediate: true);
            _wristSource = null;
            RefreshWristControls();
            VRLog.Info("Cards", "Control board wrist attachment enabled; remembered follow/fixed preference is unchanged.");
        }
        RefreshWristReturnSize();
        RefreshWristControls();
        bool mainRight = VRHands.Primary == VRHands.Right;
        VRHand? hand = VRHands.Get(WristSide(CardsConfig.WristBoardHand.Value, mainRight));
        // Tracking loss retains the last valid rig-relative pose. Never follow a zero/default
        // hand, substitute the head, or place the board at the other player's wrist.
        if (hand == null || !hand.IsTracked || hand.Rig == null || hand.Rig.Wrist == null) return true;
        Transform wrist = hand.Rig.Wrist;
        if (wrist != _wristSource)
        {
            BeginWristBlend();
            _wristSource = wrist;
        }
        float rigScale = _anchorParent.lossyScale.x;
        if (!IsUsableScale(rigScale)) return true;
        Vector3 offset = CardsConfig.WristBoardOffsetMeters.Value;
        Vector3 angles = CardsConfig.WristBoardAnglesDegrees.Value;
        if (!FiniteWrist(offset) || !FiniteWrist(angles)) return true;
        Vector3 position = wrist.position + wrist.rotation * (offset * rigScale);
        Quaternion rotation = wrist.rotation * Quaternion.Euler(angles);
        float size = ComputeBoardScale(CardsConfig.CurrentBoard) * CardsConfig.WristBoardScale.Value;
        if (!IsUsableScale(size)) return true;
        float t = WristBlendAmount();
        Vector3 start = _anchorParent.TransformPoint(_wristBlendPosition);
        Quaternion startRotation = _anchorParent.rotation * _wristBlendRotation;
        _root.SetPositionAndRotation(Vector3.Lerp(start, position, t), Quaternion.Slerp(startRotation, rotation, t));
        _root.localScale = Vector3.Lerp(_wristBlendScale, Vector3.one * size, t);
        _placed = true;
        _everPlaced = true;
        CardsDriver.NoteExpectedPoseChange("tracked wrist attachment");
        if (_wantVisible && !_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        return true;
    }

    private void CaptureWristReturnSeat()
    {
        if (_root == null || _anchorParent == null) return;
        var seat = new GameObject("GloomhavenVR.WristRememberedBoardSeat");
        seat.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(seat);
        _wristReturnSeat = seat.transform;
        _wristReturnSeat.SetParent(_root.parent, worldPositionStays: false);
        _wristReturnSeat.SetPositionAndRotation(_root.position, _root.rotation);
        _wristReturnSeat.localScale = _root.localScale;
        _wristReturnFollow = CardsConfig.TrayFollow.Value;
        // Reuse the genuine frozen pin frame and tracking-origin carry policy. This
        // contains no renderer, collider or controller and survives board style rebuilds.
        _wristReturnAnchor.Apply(_wristReturnSeat, follow: false, _anchorParent);
        _wristReturnAnchor.ReauthorOrigin();
        _wristReturnAnchor.RecacheRigLocal(_wristReturnSeat);
        CacheWristFollowFrame();
        _wristReturnConfiguredScale = ComputeBoardScale(CardsConfig.CurrentBoard);
    }

    private void CacheWristFollowFrame()
    {
        Transform? rig = Rig.VRRigDriver.RigRoot;
        if (_wristReturnSeat == null || rig == null) return;
        _wristFollowPosition = rig.InverseTransformPoint(_wristReturnSeat.position);
        _wristFollowRotation = Quaternion.Inverse(rig.rotation) * _wristReturnSeat.rotation;
        _wristFollowScale = _wristReturnSeat.lossyScale.x / Mathf.Max(rig.lossyScale.x, 1e-4f);
    }

    private void RefreshWristReturnSize()
    {
        if (_wristReturnSeat == null) return;
        // The durable invisible seat never hangs below HandsRoot: hand/rig teardown
        // must not delete the remembered ordinary pose during an attachment or exit.
        float configured = ComputeBoardScale(CardsConfig.CurrentBoard);
        if (IsUsableScale(configured) && IsUsableScale(_wristReturnConfiguredScale))
        {
            float ratio = configured / _wristReturnConfiguredScale;
            _wristReturnSeat.localScale *= ratio;
            _wristFollowScale *= ratio;
        }
        _wristReturnConfiguredScale = configured;
        if (_wristReturnFollow != CardsConfig.TrayFollow.Value)
        {
            _wristReturnFollow = CardsConfig.TrayFollow.Value;
            CacheWristFollowFrame();
            _wristReturnAnchor.ReauthorOrigin();
            _wristReturnAnchor.RecacheRigLocal(_wristReturnSeat);
        }
        if (_wristReturnFollow)
        {
            Transform? rig = Rig.VRRigDriver.RigRoot;
            if (rig != null)
            {
                _wristReturnSeat.SetPositionAndRotation(rig.TransformPoint(_wristFollowPosition),
                    rig.rotation * _wristFollowRotation);
                float parentScale = _wristReturnSeat.parent != null ? _wristReturnSeat.parent.lossyScale.x : 1f;
                _wristReturnSeat.localScale = Vector3.one * (_wristFollowScale * rig.lossyScale.x / Mathf.Max(parentScale, 1e-4f));
            }
        }
        else _wristReturnAnchor.TickCarry(_wristReturnSeat, follow: false);
    }

    private void ReleaseWristReturnSeat()
    {
        if (_wristReturnSeat != null) Object.DestroyImmediate(_wristReturnSeat.gameObject);
        _wristReturnSeat = null;
        _wristReturnAnchor.DestroyHolder(immediate: true);
        _wristReturnAnchor.ResetCarry();
    }

    private void BeginWristBlend()
    {
        if (_root == null || _anchorParent == null) return;
        _wristBlendPosition = _anchorParent.InverseTransformPoint(_root.position);
        _wristBlendRotation = Quaternion.Inverse(_anchorParent.rotation) * _root.rotation;
        _wristBlendScale = _root.localScale;
        // A board that has not yet been shown needs no journey from an invalid spawn pose.
        _wristBlendStarted = Time.unscaledTime - (_placed ? 0f : WorldUI.GrabBarTween.DurationSeconds);
    }

    private float WristBlendAmount() => Mathf.SmoothStep(0f, 1f,
        Mathf.Clamp01((Time.unscaledTime - _wristBlendStarted) / WorldUI.GrabBarTween.DurationSeconds));

    private void BeginWristReturn()
    {
        CancelBoardReFace();
        BeginWristBlend();
        _wristAttached = false;
        _wristReturning = true;
        _wristSource = null;
        VRLog.Info("Cards", "Control board wrist attachment disabled; returning to remembered follow/fixed placement.");
    }

    private void AdvanceWristReturn()
    {
        if (_root == null || _anchorParent == null) return;
        if (_wristReturnSeat == null)
        {
            // No former seat (mode selected before first placement): only the original
            // successful head placement may create one. Missing/untracked HMD keeps the
            // visible root at its last valid pose and retries, never memorizes that pose.
            Vector3 position = _root.position;
            Quaternion rotation = _root.rotation;
            float worldScale = _root.lossyScale.x;
            _placed = false;
            _wristReturning = false;
            PlaceAtHead();
            _wristReturning = true;
            if (!_placed) return;
            CaptureWristReturnSeat();
            _root.SetParent(_anchorParent, worldPositionStays: true);
            _root.SetPositionAndRotation(position, rotation);
            _root.localScale = Vector3.one * (worldScale / Mathf.Max(_anchorParent.lossyScale.x, 1e-4f));
            BeginWristBlend();
        }
        if (_wristReturnSeat == null) return;
        RefreshWristReturnSize();
        float t = WristBlendAmount();
        _root.SetPositionAndRotation(
            Vector3.Lerp(_anchorParent.TransformPoint(_wristBlendPosition), _wristReturnSeat.position, t),
            Quaternion.Slerp(_anchorParent.rotation * _wristBlendRotation, _wristReturnSeat.rotation, t));
        Vector3 scale = Vector3.one * (_wristReturnSeat.lossyScale.x / Mathf.Max(_anchorParent.lossyScale.x, 1e-4f));
        _root.localScale = Vector3.Lerp(_wristBlendScale, scale, t);
        CardsDriver.NoteExpectedPoseChange("wrist attachment return");
        if (t < 1f) return;
        _wristReturning = false;
        _placed = true;
        // Re-establish the remembered frozen pin frame before applying its original
        // behavior. Rig zoom or locomotion during attachment cannot move/resize it.
        if (!_wristReturnFollow && _wristReturnAnchor.Holder != null)
        {
            Transform pin = _anchor.EnsureHolder();
            pin.localScale = _wristReturnAnchor.Holder.localScale;
            _root.SetParent(pin, worldPositionStays: true);
        }
        ApplyFollowMode();
        _anchor.ReauthorOrigin();
        _anchor.RecacheRigLocal(_root);
        ReleaseWristReturnSeat();
        RefreshWristControls();
    }

    private void RefreshWristControls()
    {
        bool show = !WristControlsHidden;
        // Hide the entire anchor, including engraving, cap colliders and any ongoing cap
        // animation. A wrist attachment is not a movable/pinnable board: its bar must not
        // register invisible grab or laser hits. Remote observers use the same owner flag.
        if (_followAnchor != null && _followAnchor.gameObject.activeSelf != show) _followAnchor.gameObject.SetActive(show);
        if (_handle != null && _handle.gameObject.activeSelf != show) _handle.gameObject.SetActive(show);
    }

    internal void PrepareWristRebuild() => _wristKeepSeatOnDestroy = true;

    private void WristRootDestroyed()
    {
        if (_wristKeepSeatOnDestroy)
            _wristReturnRebuild |= _wristReturning;
        else
        {
            _wristReturnRebuild = false;
            ReleaseWristReturnSeat();
        }
        _wristKeepSeatOnDestroy = false;
        _wristAttached = false;
        _wristReturning = false;
        _wristSource = null;
        // Keep the saved ordinary pose across a style rebuild; never replace it with the
        // currently attached wrist pose. It is consumed only by a genuine mode exit.
    }

    private static bool FiniteWrist(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
        && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
}
