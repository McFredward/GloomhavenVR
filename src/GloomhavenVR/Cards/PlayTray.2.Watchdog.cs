using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.Rig;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

// PlayTray part 2 of 7 (see PlayTray.1.Core.cs for the split map and its rules).
// Region: the LOST-BOARD WATCHDOG, whole and alone.
//
// Several of the blocks below are COMMENT WITH NO CODE UNDER IT. That is deliberate and it is
// the invariant: they record approaches that were tried on hardware and reverted, and deleting
// them discards the record of why not to re-add them (INVARIANTS-Cards §6, SyncPinHolder).

internal sealed partial class PlayTray
{
    // ------------------------------------------------------------------ LOST-BOARD WATCHDOG --
    //
    // ROOT CAUSE THIS EXISTS FOR (incident: "walked around the room, briefly took the headset
    // off and put it back on — the control board was GONE").
    //
    // Every recovery path the board had before this watchdog hung off ONE signal: the
    // VRPresenceWatch doff/don edge (SessionResumed → CardsDriver.ReassertBoardKeepingPose →
    // ReassertPlacement above). The hardware log of the incident run proves that signal never
    // arrived:
    //   * LogOutput.log has exactly ONE "[Core] User presence lost" line, at boot in the menu,
    //     and NO "[Core] Session resumed" line anywhere — so no recovery ever ran;
    //   * Player.log shows the OpenXR session sitting in XR_SESSION_STATE_FOCUSED from
    //     11:03:22 straight through to the 11:10:51 shutdown — SteamVR/OpenXR (the runtime in
    //     use, not VDXR) neither idled the session nor reported the proximity sensor flipping,
    //     so neither VRPresenceWatch signal (userPresence feature / frozen head pose) fired.
    // A doff/don that the runtime hides from us is therefore INVISIBLE to every event-driven
    // recovery we have. Meanwhile the board CAN legitimately end up out of reach:
    //   * FOLLOW mode anchors the board in RIG space, so physically walking across the room
    //     leaves it standing where it was (and at the diorama scale ~20.7 world units per real
    //     metre, "a few steps" is tens of world units of separation);
    //   * PINNED mode anchors it in raw WORLD space, so a recentre — which TELEPORTS the rig to
    //     the table-edge/circle seat, VRRigDriver.Recenter — strands it at the old seat;
    //   * the pin holder's scale was baked ONCE at pin time from the rig's lossy scale, so a
    //     later world-grab zoom silently rescaled the pinned board's world OFFSET with it.
    // So the safety net must be UNCONDITIONAL and per-frame, not event-driven. It mirrors the
    // proven LOST-MENU RECALL in WorldUI.ModalFallback.TickMenuRecall: dwell timer, generous
    // envelope, never yank a board the user is holding, one loud log line stating WHY.

    /// <summary>Watchdog reach envelope, REAL metres (× diorama scale): within this the board is
    /// AT HAND — never recalled, no matter where the head is looking (a lectern below the chin
    /// legitimately leaves the frustum whenever the player looks up at the dungeon).</summary>
    private const float WatchReachMeters = 1.8f;

    /// <summary>Watchdog "findable" distance, REAL metres (× diorama scale): farther than this the
    /// board is unusable even if it is technically on screen.</summary>
    private const float WatchFindableMeters = 4f;

    /// <summary>Frustum slack for the watchdog visibility test (fraction of the viewport) — a board
    /// half off the view edge is findable by turning the head and must not be yanked back.</summary>
    private const float WatchViewMargin = 0.35f;

    /// <summary>How long the board must be continuously BOTH out of reach AND out of view before it
    /// is recovered. Long enough that leaning away / turning around never moves it; short enough
    /// that a doff/don or a stranding glitch is fixed before the player can call it "gone".</summary>
    private const float WatchLostDwellSeconds = 3f;

    /// <summary>Unscaled time the board first read LOST, 0 while it is fine. Reset on recovery,
    /// on a grab (the user is deliberately carrying it) and whenever it reads reachable/visible.</summary>
    private float _lostSince;

    /// <summary><see cref="VRRigDriver.RigPoseVersion"/> the PINNED world pose was authored under.
    /// The version bumps ONLY on a rig (re)build or a deliberate recentre — i.e. exactly the
    /// tracking-origin changes that move the player without moving the world — so a mismatch means
    /// the pinned pose was authored against a DIFFERENT origin and must be carried along.</summary>
    private int _pinPoseVersion = -1;

    /// <summary>Pending SANCTIONED-move label from <see cref="SyncPinHolder"/>, null when it wrote
    /// nothing. The pin housekeeping legitimately changes the board's PARENT-LOCAL pose (the holder
    /// rescale) or its world pose (the tracking-origin carry), and CardsDriver's issue-C pose watch
    /// Warns on any change it did not sanction — so the driver drains this right after the tick.</summary>
    private string? _pinHousekeepingMove;

    /// <summary>Take (and clear) the pending pin-housekeeping move label — see <see cref="_pinHousekeepingMove"/>.</summary>
    internal string? ConsumePinHousekeepingMove()
    {
        string? move = _pinHousekeepingMove;
        _pinHousekeepingMove = null;
        return move;
    }

    /// <summary>
    /// Per-frame safety net (CardsDriver.Update). Returns true when the board must be recovered
    /// THIS frame; the caller sanctions the resulting pose change (issue-C pose watchdog) and
    /// calls <see cref="RecoverLostBoard"/> with the returned reason. Never mutates the pose
    /// itself apart from the pin-holder housekeeping documented below, which is pose-PRESERVING.
    /// </summary>
    internal bool TickLostWatchdog(out string why)
    {
        why = string.Empty;
        // !_placed = the initial head-relative placement is still deferred (untracked head at
        // scenario start). The root is hidden and still sits at its spawn pose, so any verdict
        // here would be about a pose that does not exist yet; TickPlacement is already retrying
        // every frame and logs its own "placement deferred" line.
        if (_root == null || !_wantVisible || !_placed)
        {
            _lostSince = 0f;
            return false;
        }

        // Housekeeping first: keep the PINNED holder honest (scale drift + tracking-origin
        // changes). Both are pose-preserving in the user's frame of reference, so they run
        // before the lost test and can stop the board from ever reading lost.
        SyncPinHolder();

        // A gripped board is being deliberately placed — never recall mid-carry (the
        // ModalFallback recall rule; yanking a panel out of the user's hand is worse than
        // whatever it was doing).
        if (_handle != null && _handle.IsGrabbed)
        {
            _lostSince = 0f;
            return false;
        }

        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
        {
            _lostSince = 0f; // no head this tick — no verdict is possible, and no timer may run
            return false;
        }

        Vector3 pos = _root.position;
        // NON-FINITE: no dwell timer, no debate. A NaN/Inf transform is corrupt, everything
        // parented to it (cards, docked game canvases) renders undefined, and it can never heal
        // by itself.
        if (!IsFinite(pos) || !IsFinite(_root.localScale))
        {
            _lostSince = 0f;
            why = $"NON-FINITE transform (pos {pos}, localScale {_root.localScale})";
            return true;
        }

        float scale = Mathf.Max(_root.parent != null ? _root.parent.lossyScale.x : 1f, 1e-4f);
        Vector3 headPos = head.transform.position;
        Vector3 delta = pos - headPos;
        var horizontal = new Vector3(delta.x, 0f, delta.z);
        float reachM = horizontal.magnitude / scale;
        float dropM = delta.y / scale;

        // AT HAND: inside the reach envelope the board is usable whatever the head is doing.
        bool reachable = reachM <= WatchReachMeters && Mathf.Abs(dropM) <= WatchReachMeters;
        // FINDABLE: outside reach it must at least be ON SCREEN and close enough to walk to,
        // otherwise the player has no way of knowing where it went. Roaming away from a PINNED
        // board while still SEEING it stays legitimate — that is the whole point of pinning.
        bool findable = !reachable
                        && delta.magnitude / scale <= WatchFindableMeters
                        && IsInHeadView(head, pos);

        if (reachable || findable)
        {
            _lostSince = 0f;
            return false;
        }

        float now = Time.unscaledTime; // the pause menu may freeze timeScale
        if (_lostSince <= 0f)
        {
            _lostSince = now;
            return false;
        }
        float lostFor = now - _lostSince;
        if (lostFor < WatchLostDwellSeconds)
            return false;

        // Angle off the view axis is the number that tells the next reader whether the board was
        // BEHIND the player (walked away / recentre stranding) or merely too far ahead.
        Vector3 flat = new Vector3(delta.x, 0f, delta.z);
        Vector3 headFlat = new Vector3(head.transform.forward.x, 0f, head.transform.forward.z);
        float angle = flat.sqrMagnitude > 1e-6f && headFlat.sqrMagnitude > 1e-6f
            ? Vector3.Angle(headFlat, flat)
            : 0f;
        why = $"out of reach AND out of view for {lostFor:F1}s " +
              $"({reachM:F2} m out, {dropM:+0.00;-0.00} m vertical, {angle:F0}° off the view axis, " +
              $"mode {(CardsConfig.TrayFollow.Value ? "FOLLOW" : "PINNED")}, rig scale {scale:F1})";
        return true;
    }

    /// <summary>
    /// Recover a lost board: re-seat it at the configured head-relative offset (through
    /// <see cref="PlaceAtHead"/>'s clamp, which also re-pins a PINNED board at the fresh pose)
    /// and force it visible. Logs LOUDLY with the previous pose and the reason — this is the line
    /// to grep for after an incident: "[Cards] CONTROL BOARD RECOVERED".
    /// </summary>
    internal void RecoverLostBoard(string why)
    {
        if (_root == null)
            return;
        Vector3 before = _root.position;
        _lostSince = 0f;
        _placed = false;   // force a fresh, clamped head-relative seat in BOTH modes
        PlaceAtHead();     // defers safely when the head has no pose yet (TickPlacement retries)
        if (_wantVisible)
            SetVisible(true); // a recovery must never leave the board hidden
        // Re-author the pinned world pose against the CURRENT tracking origin so the next
        // recentre carries it instead of stranding it again.
        _pinPoseVersion = VRRigDriver.RigPoseVersion;
        VRLog.Info("Cards", $"CONTROL BOARD RECOVERED — {why}. Was at {before}, re-homed to " +
                            $"{_root.position} in front of the player. The board must never be " +
                            "unreachable; report this line with the surrounding log.");
    }

    /// <summary>
    /// PINNED-holder housekeeping, pose-preserving. Two silent glitch sources live here:
    ///
    /// 1. SCALE DRIFT. <see cref="ApplyFollowMode"/> bakes the rig's lossy scale into the world
    ///    holder ONCE, at pin time, so the tray's own localScale keeps its 0.5×–2× semantics.
    ///    But WorldGrab rescales the rig live (0.1×–12× of base), and the tray sits at a
    ///    non-zero LOCAL offset under the holder — so a later zoom would multiply that offset
    ///    and shift the pinned board across the room without anyone touching it. Re-assert the
    ///    holder scale from the live rig scale every frame and restore the tray's WORLD pose
    ///    around the write, so the board stays bolted to its world spot at any zoom.
    ///
    /// 2. TRACKING-ORIGIN CHANGE. A pinned pose is raw WORLD space, but a recentre teleports the
    ///    RIG (VRRigDriver.Recenter — table-edge seat / multiplayer spawn circle) without moving
    ///    the world, which strands the board at the old seat. RigPoseVersion bumps on exactly
    ///    those events (rig (re)build + deliberate recentre) and on nothing else — snap turns and
    ///    world-grab deliberately do NOT bump it — so it is the stable signal to key on: carry the
    ///    pinned board along by its RIG-RELATIVE pose, i.e. it keeps the same position/orientation
    ///    relative to the player, which is what "my board stayed where I put it" means to the user.
    /// </summary>
    private void SyncPinHolder()
    {
        int version = VRRigDriver.RigPoseVersion;
        if (_root == null || CardsConfig.TrayFollow.Value || _pinRoot == null)
        {
            // FOLLOW mode is rig-parented: both problems are structurally impossible there.
            _pinPoseVersion = version;
            return;
        }

        Transform? rig = VRRigDriver.RigRoot;
        // (2) tracking-origin change — carry the pinned board with the rig.
        if (rig != null && _pinPoseVersion >= 0 && _pinPoseVersion != version && _rigLocalPinValid)
        {
            Vector3 pos = rig.TransformPoint(_rigLocalPinPos);
            Quaternion rot = rig.rotation * _rigLocalPinRot;
            if (IsFinite(pos))
            {
                Vector3 before = _root.position;
                _root.SetPositionAndRotation(pos, rot);
                _pinHousekeepingMove = "pin carried through a tracking-origin change";
                VRLog.Info("Cards", $"Control board (PINNED) carried through a tracking-origin change " +
                                    $"(rig pose version {_pinPoseVersion} → {version}: rig rebuild or " +
                                    $"recentre): {before} → {pos}. A world-space pin would have been " +
                                    "stranded at the old seat.");
            }
        }
        _pinPoseVersion = version;

        // (1) NO live holder rescale. DO NOT DELETE THIS COMMENT BLOCK BECAUSE IT HAS NO CODE
        // UNDER IT — the absence of code IS the invariant, and the block is the only thing that
        // stops the removed rescale from being "restored" as an obvious omission.
        // An earlier cut of this housekeeping re-asserted
        // _pinRoot.localScale from the LIVE rig scale every frame, on the theory that a world-grab
        // zoom would otherwise drift the pinned board. That theory was wrong twice over:
        //   * it cannot drift. The holder sits at the world ORIGIN with identity rotation and a
        //     scale baked once at pin time (ApplyFollowMode); it is NOT parented under the rig, so
        //     rescaling the rig cannot move or resize anything underneath it.
        //   * the rescale itself was the bug the tester then reported ("world zoom zooms the
        //     pinned control board too — that must not happen, the board is scaled independently
        //     by the player"). With the holder tracking the rig, the board's WORLD size grows with
        //     the zoom while its world POSITION is held — so it swells on screen exactly as the
        //     rest of the world shrinks. With the holder FIXED, world size and world distance are
        //     both constant, so a pinned board is completely unaffected by zoom, which is the
        //     whole point of pinning it. Board size stays what the player dialled in (BoardScale /
        //     the two-hand resize), and nothing else.
        // The holder scale is therefore written ONCE, by ApplyFollowMode, and left alone.

        // Re-cache the rig-relative pin pose every frame the origin is stable, so the NEXT
        // origin change has a fresh, correct offset to carry the board by.
        if (rig != null)
        {
            _rigLocalPinPos = rig.InverseTransformPoint(_root.position);
            _rigLocalPinRot = Quaternion.Inverse(rig.rotation) * _root.rotation;
            _rigLocalPinValid = IsFinite(_rigLocalPinPos);
        }
        else
        {
            _rigLocalPinValid = false;
        }
    }

    private Vector3 _rigLocalPinPos;
    private Quaternion _rigLocalPinRot = Quaternion.identity;
    private bool _rigLocalPinValid;

    /// <summary>Board centre inside the head frustum with <see cref="WatchViewMargin"/> slack.</summary>
    private static bool IsInHeadView(Camera head, Vector3 worldPos)
    {
        Vector3 vp = head.WorldToViewportPoint(worldPos);
        return vp.z > 0f
               && vp.x >= -WatchViewMargin && vp.x <= 1f + WatchViewMargin
               && vp.y >= -WatchViewMargin && vp.y <= 1f + WatchViewMargin;
    }

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));
}
