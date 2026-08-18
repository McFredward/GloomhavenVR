using GloomhavenVR.Core;
using GloomhavenVR.Rig;
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
    //   * BOTH anchor modes now hold the board in RIG space, so physically walking across the room
    //     leaves it standing where it was (and at the diorama scale ~20.7 world units per real
    //     metre, "a few steps" is tens of world units of separation);
    //   * the two PINNED-specific ways the board used to get lost are gone with the world-space
    //     pin (2026-08-18, see SyncPinHolder): a recentre TELEPORTING the rig to the table-edge
    //     seat stranded a world-pinned board at the old seat, and the holder's scale — baked once
    //     at pin time from the rig's lossy scale — let a later world-grab zoom rescale the pinned
    //     board's world OFFSET with it. A rig-local pin has neither failure available to it.
    // So the safety net must be UNCONDITIONAL and per-frame, not event-driven. It mirrors the
    // then-proven LOST-MENU RECALL in WorldUI.ModalFallback: dwell timer, generous envelope,
    // never yank a board the user is holding, one loud log line stating WHY.
    //
    // THAT PRECEDENT NO LONGER EXISTS. ModBuild 149 deleted the menu recall outright — dwell
    // timer AND distance clause — on the same user ruling the block below states for the tray
    // ("Die Fenster sollen ... dort dauerhaft fest sitzen wenn sie nicht aktiv verschoben
    // werden"). The paragraph above is kept as the history of how this watchdog was arrived at,
    // not as a live cross-reference: nothing in WorldUI recalls anything on a timer any more.

    // ---------------------------------------------------- THE AUTOMATIC RECALL IS GONE (RULING)
    //
    // DO NOT RE-ADD A DISTANCE- OR VISIBILITY-BASED RECALL. DO NOT DELETE THIS BLOCK BECAUSE IT
    // HAS NO CODE UNDER IT — the absence of the code IS the invariant (INVARIANTS-Cards §6).
    //
    // What used to be here: a per-frame envelope (reach 1.8 m, findable 4 m, 0.35 viewport slack)
    // with a 3 s dwell that re-homed the board in front of the player once it read BOTH out of
    // reach AND out of view. It did exactly what it was written to do — and that turned out to be
    // the bug. USER RULING (2026-08-03, after it fired while the options menu was open in a
    // tutorial): "das darf niemals passieren, das Controllboard muss immer wie angewurzelt an der
    // Position sein — es darf niemals (egal was passiert) eine Position plötzlich wechseln
    // (Respektiere natürlich nach wie vor fixed/Folgen)."
    //
    // The hardware log of that run shows the mechanism precisely: reading a menu parks the head
    // away from a PINNED board for longer than the dwell, so
    //   [Cards] CONTROL BOARD RECOVERED — out of reach AND out of view for 3.0s (2.64 m out,
    //           -1.12 m vertical, 69° off the view axis, mode PINNED, rig scale 8.1)
    // fired four times in one session, each time teleporting the board in front of the player.
    // No envelope tuning can fix that: "the player is not looking at it and it is more than an
    // arm away" is the NORMAL state of a pinned board, not evidence of a glitch.
    //
    // What remains (below): the NON-FINITE verdict — a NaN/Inf transform is not a position at all,
    // nothing parented to it renders, and it can never heal by itself — plus the pose-PRESERVING
    // pin housekeeping. The user-facing recovery is meant to be the explicit one, "Board
    // zurückholen" (CardsDriver.RequestBoardRecall → _recallBoard), which is a deliberate action
    // and therefore always allowed — but note it has NO button wired to it yet, so today the
    // non-finite verdict is in practice the only automatic recovery left.
    /// <summary><see cref="VRRigDriver.RigPoseVersion"/> the pin last saw. The version bumps ONLY on
    /// a rig (re)build or a deliberate recentre — i.e. exactly the tracking-origin changes that move
    /// the player without moving the world. It is no longer a TRIGGER for anything: a rig-local pin
    /// is carried through those events by <see cref="TrayPinFrame"/> adopting the new rig, with no
    /// arithmetic to run. It survives so the event can be LABELLED in the log and sanctioned for the
    /// pose watchdogs — a carry that is invisible in code should still be legible in a log.</summary>
    private int _pinPoseVersion = -1;

    /// <summary>Whether the pin frame had a live rig to copy last tick — the edge that legitimately
    /// re-expresses the pin (see <see cref="SyncPinHolder"/>).</summary>
    private bool _pinHadRig;

    /// <summary>Pending SANCTIONED-move label from <see cref="SyncPinHolder"/>, null when it had
    /// nothing to announce. The pin housekeeping can move the board's WORLD pose (the holder adopts
    /// a rebuilt rig), and CardsDriver's issue-C pose watch Warns on any change it did not sanction
    /// — so the driver drains this right after the tick.</summary>
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
        // The size diagnostic runs BEFORE every early-out below: the one state it exists to prove
        // (apparent size constant across a zoom) is exactly the state a gripped or unplaced board
        // is in, and a diagnostic that stops at the interesting moment is not one.
        if (_root != null && _wantVisible)
            TickBoardSizeDiagnostics();
        // !_placed = the initial head-relative placement is still deferred (untracked head at
        // scenario start). The root is hidden and still sits at its spawn pose, so any verdict
        // here would be about a pose that does not exist yet; TickPlacement is already retrying
        // every frame and logs its own "placement deferred" line.
        if (_root == null || !_wantVisible || !_placed)
            return false;

        // Housekeeping: put the pin holder on THIS frame's rig frame before anything below reads
        // the anchor. Pose-preserving in the user's frame of reference by construction — the board
        // keeps the same place relative to the player, which is what "it stayed where I put it"
        // means — so it is not a move under the ruling above.
        SyncPinHolder();
        // Runs AFTER SyncPinHolder (which is what makes the anchor honest) and BEFORE the
        // gripped early-out below — a held board is exactly the case it exists for.
        TickHeldSizeFreeze();
        // NO world-tilt compensation, and none is needed any more. The 2026-08 decision here
        // ("a PINNED board is deliberately WORLD-static — a tilt change leaves it untouched, it
        // then looks tilted like the rest of the world") was made in the world frame, and the old
        // SyncWorldTiltComp counter-rotation it rejected visibly dragged the board through the
        // tilt tween ("nachziehen"). Under the rig-local pin the question dissolves: the world
        // tilt is a rotation OF THE RIG, and the holder copies the rig's rotation, so the board
        // rides the tween rigidly — no counter-rotation, no drag, and nothing moves in the
        // player's view. That is the 2026-08-18 ruling applied to the tilt ("EGAL wie man zoomed
        // oder sich bewegt"), and it does reverse the earlier decision's OUTCOME: a tilted world
        // no longer tilts the board relative to the player. Flagged to the user as such.

        // A gripped board is being deliberately placed — never touch it mid-carry. The pinned
        // freeze sentinel drops its baseline too: a grab is the user's own hand, and the pose it
        // leaves behind is by definition sanctioned (it re-baselines silently on release).
        if (_handle != null && _handle.IsGrabbed)
        {
            _pinFreezeValid = false;
            _pinFreezeSource = null;
            return false;
        }

        // APPARENT-SIZE LIMITS (user ruling 2026-08-03: "Wir brauchen ein Limit für eine
        // Maximalgröße des Boards und eine Minimalgröße"). Enforced on the FINAL, world-visible
        // size rather than on the tray's own localScale, because localScale is not what got out of
        // hand: the two-hand pinch already clamps it to [0.15, 2], but in FOLLOW mode the board
        // hangs off the RIG, so shrinking yourself with the world grab shrinks the board with you.
        // Alternating pin → grow → follow → shrink multiplies one shrink onto the other, which is
        // exactly the "extrem winzig" the report describes, and no clamp on a factor can see it.
        // The board's apparent width IS visible here (BoardW × the root's lossy scale), so that is
        // what is clamped — every frame, so no gesture combination can slip past it.
        //
        // FOLLOW MODE ONLY — and the reason has CHANGED, so read this before re-deciding it.
        //
        // WHY IT WAS GATED (2026-08-07, and this half is now WRONG): the clamp measures in PLAYER
        // units — world width ÷ LIVE rig scale — and while the pin was stored in WORLD space that
        // measure drifted on its own: the board's world size was frozen, a world-grab zoom rescaled
        // the PLAYER, and the apparent width therefore swept across the 18/140 cm limits with
        // nobody touching the board, whereupon the clamp "corrected" the frozen world size frame
        // after frame (18× "Board size CLAMPED" in one log, with a CONSTANT parent chain ×40.10
        // against rig scales ×5.13–×137.19). The block that stood here concluded from that: "A
        // pinned board's apparent size changing with zoom is what pinning MEANS." That sentence is
        // deleted, not softened — it is the exact fault the user reported three times and it was
        // written down here as intent.
        //
        // WHY IT STAYS GATED ANYWAY (recommendation, 2026-08-18 — the user should rule): under the
        // rig-local pin a FIXIERT board's apparent size CANNOT drift, so the drift that made the
        // clamp harmful is gone and running it in both modes would now be safe arithmetic. It is
        // still left off, because "safe" is not the test: every path that can set the size already
        // bounds it (the two-hand gesture window bounds every explicit resize live in BOTH modes,
        // and ComputeBoardScale clamps the configured product), so in FIXIERT this clamp has no
        // reachable trigger left EXCEPT a hand-edited cfg — and firing on that would be an
        // automatic resize of a board the ruling says only the player may resize. If the user wants
        // a hand-edited size corrected rather than obeyed, delete the gate; it is one line.
        if (CardsConfig.TrayFollow.Value)
            ClampApparentSize();

        // PINNED FREEZE SENTINEL: convict any remaining automatic writer instantly (see below).
        TickPinnedFreezeSentinel();

        // NON-FINITE: the ONLY verdict left. A NaN/Inf transform is not a position — everything
        // parented to it (cards, docked game canvases) renders undefined and it can never heal by
        // itself, so leaving it alone is not "keeping it where the user put it", it is keeping it
        // nowhere. Distance and visibility are explicitly NOT verdicts (see the ruling above).
        Vector3 pos = _root.position;
        if (!IsFinite(pos) || !IsFinite(_root.localScale))
        {
            why = $"NON-FINITE transform (pos {pos}, localScale {_root.localScale})";
            return true;
        }
        return false;
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
        // SIZE IS NOT PART OF A RECALL (user, 2026-08-03: "Es hat in dem Test auch seine Größe
        // geändert. Das darf nicht sein!"). "Bring it back" means bring it back — the size the
        // player dialled in with the two-handed gesture is theirs, so it is captured here and
        // written back over whatever PlaceAtHead re-derived from config.
        Vector3 keepScale = _root.localScale;
        bool keepScaleValid = IsFinite(keepScale) && keepScale.x > 1e-4f;
        _placed = false;   // force a fresh, clamped head-relative seat in BOTH modes
        PlaceAtHead();     // defers safely when the head has no pose yet (TickPlacement retries)
        if (keepScaleValid)
            _root.localScale = keepScale;
        NotePinnedWrite($"lost-board recovery ({why})"); // freeze sentinel: sanctioned re-seat
        if (_wantVisible)
            SetVisible(true); // a recovery must never leave the board hidden
        // Adopt the CURRENT tracking origin as the one this pin was authored under, so the next
        // rebuild/recentre is logged as one event and not as this recovery's aftershock. (It no
        // longer decides anything: PlaceAtHead wrote a world pose, the holder is the player's own
        // frame, and the pin that falls out of that re-parenting is rig-local like every other.)
        _pinPoseVersion = VRRigDriver.RigPoseVersion;
        VRLog.Info("Cards", $"CONTROL BOARD RECOVERED — {why}. Was at {before}, re-homed to " +
                            $"{_root.position} in front of the player. The board must never be " +
                            "unreachable; report this line with the surrounding log.");
    }

    /// <summary>
    /// PINNED-holder housekeeping — one call, because the pin is now STORED in the frame it is
    /// defined in instead of being recomputed into it every frame.
    ///
    /// <para>WHAT USED TO BE HERE, and why it is gone. Two blocks of arithmetic: a LIVE HOLDER
    /// RESCALE that re-asserted the world holder's scale from the rig's lossy scale (restoring the
    /// tray's world pose around the write), and a TRACKING-ORIGIN CARRY that cached the board's
    /// rig-relative pose every frame and re-applied it whenever <c>RigPoseVersion</c> bumped. Both
    /// were correct descriptions of what a rig-local pin needs — and both were performed BY HAND,
    /// in <c>CardsDriver.Update</c>, one phase before <c>WorldGrab.Update</c> writes the frame's
    /// new rig scale. The 2026-08-18 hardware log proves the consequence by identity: L3467 prints
    /// rig ×64.79 and L3474 prints "parent chain ×64.79 ÷ rig ×68.50" — the holder is EXACTLY one
    /// frame behind. Over 223 <c>BOARD SIZE</c> lines the anchor ratio swung 0.89…1.11 during
    /// zooms (only 79 of them read 1.00), i.e. the board breathed ±10 % through every pinch. Hand
    /// arithmetic in the wrong phase cannot be tuned into being in the right one, so it was
    /// deleted: <see cref="TrayPinFrame"/> copies the rig frame onto the holder in LateUpdate,
    /// after every rig writer, and the board hangs under it with a CONSTANT local pose.</para>
    ///
    /// <para>WHAT REMAINS. One <see cref="TrayPinFrame.Seat"/> call in this (Update) phase, so the
    /// size diagnostic and the size clamp that run a few lines below read an anchor sampled THIS
    /// frame rather than the previous one, and one log line when the rig transform was replaced.
    /// The seat is idempotent and never touches the board's pose under the holder — the board
    /// cannot move in the player's frame no matter how often it runs.</para>
    ///
    /// <para>THE TRACKING-ORIGIN CARRY IS NOW FREE, which is the point. A recentre or a rig
    /// rebuild hands the follower a different rig transform; it copies that one, and the board —
    /// whose pose is stored relative to the holder — arrives at the same place relative to the
    /// player. <c>RigPoseVersion</c> is no longer a trigger for anything here; it is kept only to
    /// LABEL the event in the log, because "the board was carried and nothing had to act" is worth
    /// exactly one line in a hardware log and no code at all.</para>
    /// </summary>
    private void SyncPinHolder()
    {
        int version = VRRigDriver.RigPoseVersion;
        if (_root == null || CardsConfig.TrayFollow.Value || _pinRoot == null || _pinFrame == null)
        {
            // FOLLOW mode hangs the tray under the rig-space anchor itself: the pin frame does not
            // exist there, and there is nothing to keep honest.
            _pinPoseVersion = version;
            return;
        }

        _pinFrame.Seat();

        // The holder can only track a rig that exists. Crossing that edge in either direction moves
        // the board in the player's frame ONCE, legitimately — acquiring a rig converts a world pin
        // authored with no rig (menu boot, flat dev proxy) into a rig-local one, and losing the rig
        // leaves the holder standing still while the head keeps moving. Announce both, or the
        // freeze sentinel reports the transition as an unknown writer and the one grep that is
        // supposed to convict a real one gets a false positive to live with.
        if (_pinHadRig != _pinFrame.HasRig)
        {
            _pinHadRig = _pinFrame.HasRig;
            NotePinnedWrite(_pinHadRig
                ? "pin frame acquired the player's rig (world pin → rig-local pin, board unmoved)"
                : "pin frame lost the rig (holding the last frame until one exists again)");
        }

        if (_pinPoseVersion >= 0 && _pinPoseVersion != version)
        {
            // A rebuild/recentre landed. The board did not have to be moved — announce it anyway,
            // because a sanctioned label is cheaper than a Warn from a watchdog that sees the
            // holder's world pose jump to the new tracking origin.
            _pinHousekeepingMove = "pin frame adopted a rebuilt/recentred rig (rig-local pin carried)";
            NotePinnedWrite("tracking-origin carry (TrayPinFrame — rig rebuild/recentre)");
        }
        _pinPoseVersion = version;
    }

    // ------------------------------------------------------------- PINNED FREEZE SENTINEL --
    //
    // User ruling 2026-08-07, re-stated 2026-08-18: a FIXIERT board changes NOTHING — position,
    // orientation or size — unless the player grabs or resizes it. The sentinel is the
    // enforcement's black box: every frame it diffs the pinned tray's pose against last frame's
    // and logs any change WITH ITS SOURCE. Sanctioned writers announce themselves via
    // NotePinnedWrite; a change with no announcement logs as a Warn — one grep ("PINNED tray
    // transform WRITE") convicts a leftover writer in the next hardware log instantly. It never
    // mutates anything; it only observes and re-baselines.
    //
    // IT MEASURES IN THE PLAYER'S FRAME, NOT THE WORLD'S — changed 2026-08-18 with the pin itself.
    // It used to diff the WORLD pose and the LOSSY scale, which was the right instrument while the
    // pin was stored in world coordinates. It is the wrong one now and would be pure noise: a
    // rig-local board's world pose and world size move on every zoom, snap turn, teleport and step
    // of locomotion BY DESIGN — that motion is what keeps it still in the player's eye. What must
    // not change is its pose UNDER THE PIN HOLDER, which is the rig frame: position in player
    // metres, orientation relative to the player, and its own localScale (its apparent size, since
    // the holder carries exactly one rig-scale factor). Those are the three numbers the ruling
    // freezes, so those are the three the sentinel now watches — and it is a STRICTER test than the
    // old one, because it also convicts anything that moves the board while the player is moving.

    /// <summary>Last frame's tray pose in the PLAYER's frame (position in player metres,
    /// orientation relative to the player) and the size the player sees — the three quantities the
    /// FIXIERT ruling freezes. NOT the world pose: see the block above.</summary>
    private Vector3 _pinFreezePos;
    private Quaternion _pinFreezeRot = Quaternion.identity;
    private float _pinFreezeSize = -1f;
    private bool _pinFreezeValid;
    private string? _pinFreezeSource;
    private float _nextPinFreezeLog;

    /// <summary>
    /// A transform/scale writer announces itself BEFORE/AS it writes a PINNED tray, so the
    /// freeze sentinel can name it instead of warning about an unknown writer. No-op in FOLLOW
    /// mode. Multiple writers in one frame are concatenated.
    /// </summary>
    private void NotePinnedWrite(string source)
    {
        if (CardsConfig.TrayFollow.Value)
            return;
        _pinFreezeSource = _pinFreezeSource == null ? source : _pinFreezeSource + " + " + source;
    }

    /// <summary>Per-frame PLAYER-FRAME pose diff of a PINNED tray — see the sentinel block
    /// above.</summary>
    private void TickPinnedFreezeSentinel()
    {
        if (_root == null || CardsConfig.TrayFollow.Value
            || !TryGetPlayerFramePose(out Vector3 pos, out Quaternion rot, out float size))
        {
            _pinFreezeValid = false;
            _pinFreezeSource = null;
            return;
        }
        string? source = _pinFreezeSource;
        _pinFreezeSource = null; // announcements are valid for exactly one sentinel pass

        if (_pinFreezeValid && IsFinite(pos))
        {
            // Epsilons directly in PLAYER-perceivable terms — no conversion left to get wrong: the
            // pin holder carries exactly one rig-scale factor, so a metre in this frame IS a metre
            // to the player. 2 mm of drift, a quarter degree, and 0.2 % of size (one clamp frame in
            // the 2026-08-07 log moved the size ~1 %, comfortably above; float noise stays below).
            bool moved = (pos - _pinFreezePos).sqrMagnitude > 0.002f * 0.002f
                         || Quaternion.Angle(rot, _pinFreezeRot) > 0.25f
                         || Mathf.Abs(size - _pinFreezeSize)
                            > 0.002f * Mathf.Max(_pinFreezeSize, 1e-4f);
            if (moved)
            {
                float now = Time.unscaledTime;
                if (now >= _nextPinFreezeLog) // an unknown per-frame writer must not flood the log
                {
                    _nextPinFreezeLog = now + 1f;
                    string detail = $"pose in the PLAYER's frame {_pinFreezePos} → {pos} " +
                                    $"(player metres, {(pos - _pinFreezePos).magnitude * 100f:F1} cm), " +
                                    $"rot Δ{Quaternion.Angle(rot, _pinFreezeRot):F1}°, " +
                                    $"size {_pinFreezeSize:F3} → {size:F3}. The world pose is " +
                                    "deliberately NOT compared: a rig-local pin moves through the " +
                                    "world whenever the player does, and that is what holds it still " +
                                    "in the eye.";
                    if (source != null)
                        VRLog.Info("Cards", $"PINNED tray transform WRITE [{source}]: {detail}");
                    else
                        VRLog.Warn("Cards", "PINNED tray transform WRITE [UNKNOWN WRITER — a " +
                                            "FIXIERT board moved/rescaled with no writer announcing " +
                                            "itself; report this line with the surrounding log]: " + detail);
                }
            }
        }

        _pinFreezePos = pos;
        _pinFreezeRot = rot;
        _pinFreezeSize = size;
        _pinFreezeValid = IsFinite(pos);
    }

    /// <summary>
    /// The tray's pose IN THE PLAYER'S OWN FRAME: position in player metres, orientation relative
    /// to the player, and the size the player sees (the tray's own localScale, since the pin holder
    /// contributes exactly one rig-scale factor and nothing else). This is the frame the FIXIERT
    /// ruling freezes, and while pinned it is simply the tray's local pose under the holder — no
    /// arithmetic, which is the whole reason the pin was moved into it. Falls back to an explicit
    /// inverse-transform if anything ever re-parents the tray away from the holder, and returns
    /// false in FOLGEN or with no holder (there is nothing to freeze there).
    /// </summary>
    private bool TryGetPlayerFramePose(out Vector3 pos, out Quaternion rot, out float size)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        size = 0f;
        if (_root == null || _pinRoot == null)
            return false;
        if (ReferenceEquals(_root.parent, _pinRoot))
        {
            pos = _root.localPosition;
            rot = _root.localRotation;
        }
        else
        {
            pos = _pinRoot.InverseTransformPoint(_root.position);
            rot = Quaternion.Inverse(_pinRoot.rotation) * _root.rotation;
        }
        size = _root.lossyScale.x / Mathf.Max(_pinRoot.lossyScale.x, 1e-5f);
        return IsFinite(pos) && size > 1e-5f && !float.IsInfinity(size);
    }

    // IsInHeadView lived here and is GONE with the automatic recall (see the ruling block at the
    // top of this file): the board's visibility to the player is not a reason to move it, so
    // nothing may test it. Left as a comment so re-adding the helper reads as re-adding the
    // recall, which is what it would be.

    /// <summary>Widest the board is allowed to look, real metres (config, sanity-ordered).</summary>
    private static float MaxWidthMeters => Mathf.Max(
        CardsConfig.BoardMaxWidthMeters != null ? CardsConfig.BoardMaxWidthMeters.Value : 1.4f,
        MinWidthMeters + 0.02f);

    /// <summary>Narrowest the board is allowed to look, real metres (config).</summary>
    private static float MinWidthMeters =>
        CardsConfig.BoardMinWidthMeters != null ? CardsConfig.BoardMinWidthMeters.Value : 0.18f;

    /// <summary>Log throttle for the clamp (one line per direction per second at most).</summary>
    private float _nextSizeClampLog;

    // ------------------------------------------------------------------ HELD-SIZE FREEZE --

    /// <summary>Apparent width (perceived metres) the current grip started at; −1 = not gripped.</summary>
    private float _heldApparent = -1f;

    /// <summary>The localScale this freeze last saw, so a DELIBERATE write (the two-hand resize)
    /// can be told apart from the anchor drifting under a static one.</summary>
    private float _heldLocalScale = -1f;

    /// <summary>Whether this grip has already reported a correction (one line per grip).</summary>
    private bool _heldFreezeReported;

    /// <summary>
    /// THE HELD BOARD'S SIZE FREEZE. User report, ModBuild 158 hardware: "Ich hatte in einer Hand
    /// das Controllboard und habe dann gezoomed — dann hat das controllboard mitgezoomed, das soll
    /// nicht passieren."
    ///
    /// <para>THE INVARIANT: <b>the board's apparent size at the hand is constant for the whole
    /// duration of a grip</b> — from the frame the first hand closes to the frame the last one
    /// opens — and the ONLY thing allowed to change it in that window is the two-hand resize
    /// gesture, which is the player deliberately resizing it.</para>
    ///
    /// <para>BOTH EDGES ARE POP-FREE BY CONSTRUCTION, which is the part that is easy to get wrong.
    /// PICKUP writes nothing: it only READS the live apparent size as the baseline, so grabbing a
    /// board can never resize it. RELEASE writes nothing either: the last frame's localScale simply
    /// stands, and <see cref="PersistPoseToConfig"/> stores that size, so letting go cannot resize
    /// it either. Freezing the wrong factor is what would pop — hold the localScale and the board
    /// changes size the moment the anchor moves; hold the WORLD size and it changes the moment the
    /// player does. The apparent size is the only one of the three that is the player's own
    /// question ("how big does it look in my hand?"), so that is the one held.</para>
    ///
    /// <para>THE TWO-HAND GESTURE IS NOT FOUGHT. There is no API on the shared handle that says
    /// "two hands are on the bar", so the test is behavioural and better for it: a localScale that
    /// CHANGED since last frame was written by somebody on purpose (the pinch), and the baseline
    /// re-seats to it. Only a localScale that stood still while the apparent size moved is a drift,
    /// and only that is corrected.</para>
    ///
    /// <para>WITH THE PIN STORED IN THE PLAYER'S OWN FRAME (<see cref="TrayPinFrame"/>) THIS SHOULD
    /// NEVER FIRE, and that is the point of the log line: it is an assertion, not a mechanism. It
    /// could not make that claim before 2026-08-18 — the anchor was re-derived by hand one phase
    /// too early, so a zoom sweep moved the apparent size by up to ±10 % and this freeze was doing
    /// real work while claiming to be an assertion. The
    /// paths that could still trip it are the ones that write the board mid-grip from somewhere
    /// else — a settings live-apply (<c>ReapplyOrientation</c>), a rig rebuild re-homing the tray.
    /// A "held size RE-ASSERTED" line in a future log names one of those; silence over a zoom sweep
    /// with a grip on the bar is the fix holding.</para>
    /// </summary>
    private void TickHeldSizeFreeze()
    {
        if (_root == null || _handle == null || !_handle.IsGrabbed)
        {
            _heldApparent = -1f;
            _heldLocalScale = -1f;
            _heldFreezeReported = false;
            return;
        }
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out _, out _))
            return;
        float local = _root.localScale.x;
        float apparent = perUnit * local;
        bool scaleWritten = _heldLocalScale > 0f
                            && Mathf.Abs(local - _heldLocalScale) > 1e-4f * Mathf.Max(_heldLocalScale, 1e-4f);
        if (_heldApparent < 0f || scaleWritten)
        {
            // Grip start, or the two-hand resize just moved it: this IS the size now.
            _heldApparent = apparent;
            _heldLocalScale = local;
            return;
        }
        if (Mathf.Abs(apparent - _heldApparent) <= 0.005f * Mathf.Max(_heldApparent, 1e-3f))
        {
            _heldLocalScale = local;
            return;
        }
        float wanted = _heldApparent / perUnit;
        _root.localScale = Vector3.one * wanted;
        _heldLocalScale = wanted;
        NotePinnedWrite("held-size freeze (TickHeldSizeFreeze — apparent size held for the grip)");
        CardsDriver.NoteExpectedPoseChange("held board size freeze");
        if (!_heldFreezeReported)
        {
            _heldFreezeReported = true;
            VRLog.Info("Cards", $"Board held size RE-ASSERTED: it had drifted {apparent * 100f:F1} cm " +
                                $"away from the {_heldApparent * 100f:F1} cm it was picked up at " +
                                $"(own scale {local:F3} → {wanted:F3}). A board in your hand keeps the " +
                                "size it had when you took it, at any zoom — only the two-hand resize " +
                                "may change it. This line means something OTHER than the zoom wrote " +
                                "the board mid-grip; report it with the surrounding log.");
        }
    }

    /// <summary>
    /// Hold the board's APPARENT width inside [<see cref="MinWidthMeters"/>,
    /// <see cref="MaxWidthMeters"/>] — apparent meaning AS THE PLAYER SEES IT.
    ///
    /// <para>THE BUG THIS METHOD SHIPPED WITH, and it made the board unusable (user, hardware test
    /// 2026-08-03: "es spawned VIEL ZU KLEIN neben mir ... Ich kann es nicht mehr groesser machen,
    /// es wird sofort wieder kleiner"). The first version measured the board's WORLD width — local
    /// width times the root's LOSSY scale — and called that "what the player actually sees". It is
    /// not, and the log line the old version printed contains its own refutation:</para>
    /// <code>
    /// Board size CLAMPED: apparent width 698.3 cm -> 140.0 cm (limits 18-140 cm).
    ///   Own scale 0.512 -> 0.103; everything above the board contributes x21.31 (rig/world scale)
    /// </code>
    /// <para>The player is not a world-sized observer: the RIG IS SCALED (~21x here — that is what
    /// turns a scenario into a tabletop diorama), and the player's eyes, hands and interpupillary
    /// distance are scaled with it. A board hanging under that rig at 698 cm of WORLD width is
    /// perceived at 698/21.31 = 32.8 cm — a perfectly ordinary board. Feeding the world width into
    /// limits that are written in perceived centimetres made a normal board read as a seven-metre
    /// one, so the clamp fired on EVERY frame: it squashed the first placement to a fifth of its
    /// size (0.512 -> 0.103, the "viel zu klein" spawn), and then instantly undid every enlargement
    /// the player made (log: 0.598 -> 0.103 one frame after a two-handed resize). It also wrote the
    /// scale outside any sanctioned trigger, which is what produced the "UNSANCTIONED recompute"
    /// warnings in the same log.</para>
    ///
    /// <para>THE MEASURE, CORRECTED: divide the world width by the LIVE RIG SCALE. That expresses
    /// the board in the player's own units, which is the only frame in which "18 to 140 cm" means
    /// anything. It is the same distinction stick flight already makes for its speed dial, for the
    /// same reason: a length is meaningless until you say whose metres it is in. With the board
    /// hanging under the rig (FOLLOW) or under a pin holder that has the rig scale baked into it
    /// (FIXIERT), the division cancels the parent chain and the measure reduces to the tray's own
    /// scale — which is exactly the quantity the two-handed gesture and <c>BoardScale</c> speak in,
    /// so the limits now bound the thing the player is actually adjusting. The division is done
    /// explicitly rather than by assuming that cancellation, so an unexpected parent chain still
    /// yields a player-relative answer instead of a silent wrong one.</para>
    ///
    /// <para>Only the tray's OWN localScale is written, so the rig/world scale the player chose for
    /// the DIORAMA is never touched — the board simply stops following it past the limit. When the
    /// clamp does fire it now ANNOUNCES the write to the board-pose watchdog, because a clamp is a
    /// sanctioned re-pose: silently changing the scale is precisely what that watchdog exists to
    /// catch, and it was right to complain.</para>
    /// </summary>
    private void ClampApparentSize()
    {
        if (_root == null || _handle == null)
            return;
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out float parent, out float rigScale))
            return;
        Vector3 local = _root.localScale;
        float width = perUnit * local.x;
        float min = MinWidthMeters, max = MaxWidthMeters;
        if (width >= min && width <= max)
            return;

        float wanted = Mathf.Clamp(width, min, max);
        float factor = wanted / width;
        _root.localScale = local * factor;
        // Defensive: the caller gates this to FOLLOW mode (a FIXIERT board is frozen in the
        // player's frame — rulings 2026-08-07 and 2026-08-18, and the recommendation at that
        // gate); should any future path run it on a pinned tray, the freeze sentinel names it
        // instead of warning about an unknown writer.
        NotePinnedWrite("apparent-size clamp (ClampApparentSize)");

        // Sanctioned: the watchdog must be able to tell a clamp apart from a game event moving the
        // board behind our back. Without this the clamp's own write reads as an UNSANCTIONED
        // recompute — which is exactly how the shipped bug announced itself.
        CardsDriver.NoteExpectedPoseChange("board size clamp (min/max apparent width)");

        float now = Time.unscaledTime;
        if (now >= _nextSizeClampLog)
        {
            _nextSizeClampLog = now + 1f;
            VRLog.Info("Cards", $"Board size CLAMPED: apparent width {width * 100f:F1} cm → " +
                                $"{wanted * 100f:F1} cm (limits {min * 100f:F0}–{max * 100f:F0} cm, " +
                                $"[Cards] BoardMinWidthMeters/BoardMaxWidthMeters). Own scale " +
                                $"{local.x:F3} → {_root.localScale.x:F3}. Measured in PLAYER units: " +
                                $"parent chain ×{parent:F2} ÷ rig scale ×{rigScale:F2} " +
                                "(the diorama scale itself is never touched).");
        }
    }

    /// <summary>
    /// The tray's apparent width (perceived metres, the frame the min/max limits are written in)
    /// PER UNIT of the tray's own localScale — the shared measure of the release-time safety
    /// clamp (<see cref="ClampApparentSize"/>) and the live two-hand gesture window
    /// (<see cref="WorldUI.IPanelGrabOwner.GrabScaleLimits"/>). One expression on purpose: the
    /// two enforcement points MUST agree, or a size the gesture allows would be snapped back the
    /// frame the player lets go (the exact "es wird sofort wieder kleiner" failure the corrected
    /// measure fixed on 2026-08-03). False when no board exists or a transform is degenerate —
    /// callers then skip their clamp (safety net) or fall back to the generic factor range
    /// (gesture window). <paramref name="parent"/>/<paramref name="rigScale"/> are surfaced for
    /// the clamp's diagnostic line only.
    /// </summary>
    private bool TryGetApparentWidthPerScaleUnit(out float perUnit, out float parent, out float rigScale)
    {
        perUnit = 0f;
        parent = 1f;
        rigScale = 1f;
        if (_root == null)
            return false;
        Vector3 local = _root.localScale;
        if (!IsFinite(local) || local.x <= 1e-5f)
            return false;
        parent = _root.lossyScale.x / local.x; // scale contributed by everything ABOVE us
        if (!(parent > 1e-6f) || float.IsInfinity(parent))
            return false;

        // The player's own scale. Everything the player perceives is measured against this: at rig
        // scale 21 they ARE twenty-one times larger, so a world metre is 1/21 of a perceived metre.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        rigScale = rig != null ? rig.lossyScale.x : 1f;
        if (!(rigScale > 1e-6f) || float.IsInfinity(rigScale))
            rigScale = 1f; // no rig yet (menu boot): world units ARE player units, clamp as-is

        // ONE arithmetic, shared with the gesture window, the persist and the wire vectors — see
        // BoardSizeFrame. parent/rigScale is 1 by construction in BOTH anchor modes (FOLGEN: the
        // tray hangs under the rig; FIXIERT: SyncPinHolder keeps the holder on the live rig scale),
        // and it is divided out explicitly anyway so a stale anchor yields a player-relative answer
        // instead of a silently wrong one.
        return BoardSizeFrame.TryWidthPerScaleUnit(parent, rigScale, out perUnit);
    }

    /// <summary>The board's CURRENT size in the frame <c>TrayScale × BoardScale</c> speak in
    /// (<see cref="BoardSizeFrame.SizeUnits"/>), i.e. its apparent width divided by the board's own
    /// width. This is the quantity that must round-trip through the config, and the ONE the size
    /// bounds are written in — it is zoom-free by construction, which the localScale it is derived
    /// from is not while an anchor is stale. False when no board exists or a transform is
    /// degenerate.</summary>
    internal bool TryGetSizeUnits(out float units)
    {
        units = 0f;
        if (_root == null)
            return false;
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out _, out _))
            return false;
        units = perUnit * _root.localScale.x / BoardSizeFrame.BoardWidthLocal;
        return units > 1e-5f && !float.IsInfinity(units);
    }

    /// <summary>
    /// THE BOARD-SIZE DIAGNOSTIC (house style: mechanism, measured numbers, measured-vs-assumed,
    /// and what a future log would have to say to disprove the claim). One line, throttled, naming
    /// in order:
    /// <list type="bullet">
    /// <item>the three factors of the world scale WITH their sources — base (auto from the game's
    ///   tile size, <c>VRRigDriver.BaseWorldScale</c>), table zoom (<c>[Comfort]
    ///   SavedScaleMultiplier</c>, the two-hand world pinch), and the anchor ratio (parent chain ÷
    ///   rig, which MUST read 1.00);</item>
    /// <item>the board's resulting size in both frames — size units and apparent centimetres;</item>
    /// <item>its min/max and where each bound came from — the board's own 64 cm of geometry and
    ///   <c>[Cards] BoardMinWidthMeters</c>/<c>BoardMaxWidthMeters</c>, plus the effective ceiling
    ///   the shared handle's factor range imposes on top;</item>
    /// <item>THE DENOMINATOR: how far the board is from the head in PLAYER metres, the angle it
    ///   subtends there, and what both did since the previous line;</item>
    /// <item>while held: that the size freeze is engaged and what the apparent size is.</item>
    /// </list>
    /// <para>THIS DIAGNOSTIC WAS ITSELF A DEFECT, and that is why the distance is on it. Through
    /// ModBuild 159 it printed "apparent cm", found it constant across a zoom sweep, and said so —
    /// while the user was reporting, in the same session, that the board zoomed with him. Both were
    /// true: apparent size is a NUMERATOR, the board's distance in player metres was sweeping with
    /// the zoom, and an instrument that measures one term of a ratio will agree with any build that
    /// breaks the other. It now prints the distance, the angular width, and their deltas.</para>
    /// <para>WHAT WOULD DISPROVE THE FIX, in the order a hardware log should be read:
    /// <list type="number">
    /// <item>"anchor 1.00" — the size claim. Away from 1.00 during a zoom means the holder is not
    ///   on the live rig frame and every bound printed beside it is zoom-coupled again. The
    ///   2026-08-18 log has this at 0.89…1.11 (only 79 of 223 lines read 1.00), which is the
    ///   one-frame lag the LateUpdate seat removed.</item>
    /// <item>"distance … HELD" — the place claim, and the new one. Two lines from either side of a
    ///   zoom, a snap turn, a teleport or a world grab MUST agree on the distance and on the
    ///   angular width to within float noise. Only physically WALKING may change them: the board is
    ///   nailed to the play space, so moving inside that space moves you relative to it.</item>
    /// <item>the min/max pair, unchanged across the same sweep.</item>
    /// </list></para>
    /// </summary>
    private void LogBoardSizeDiagnostics(string trigger)
    {
        if (_root == null)
            return;
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out float parent, out float rigScale))
            return;
        float units = perUnit * _root.localScale.x / BoardSizeFrame.BoardWidthLocal;
        float apparent = perUnit * _root.localScale.x;
        float minW = MinWidthMeters, maxW = MaxWidthMeters;
        BoardSizeFrame.Bounds(minW, maxW, out float unitsLo, out float unitsHi);
        BoardSizeFrame.ReachableWidths(minW, maxW, parent, rigScale, out float reachLo, out float reachHi);
        float baseScale = Rig.VRRigDriver.BaseWorldScale;
        float zoom = baseScale > 0f ? rigScale / baseScale : 0f;
        bool held = _handle != null && _handle.IsGrabbed;
        bool haveDistance = TryGetHeadDistance(out float distance, out float angular);
        string distanceText;
        if (!haveDistance)
        {
            distanceText = "DISTANCE unknown (no tracked head this frame) — the angular size claim " +
                           "cannot be checked on this line.";
        }
        else
        {
            bool hadPrevious = _loggedDistance > 0f;
            float dDist = hadPrevious ? distance - _loggedDistance : 0f;
            float dAng = hadPrevious ? angular - _loggedAngular : 0f;
            bool steady = hadPrevious && Mathf.Abs(dAng) <= 0.5f && Mathf.Abs(dDist) <= 0.01f;
            distanceText =
                $"DISTANCE {distance:F2} m in PLAYER metres ⇒ the board subtends {angular:F1}° — " +
                $"that RATIO (size ÷ distance), not the centimetres above on their own, is what " +
                $"the eye judges. " +
                (!hadPrevious
                    ? "First line of this session: nothing to compare it against yet."
                    : steady
                        ? $"HELD since the previous line (Δ{dDist * 100f:+0.0;-0.0;0.0} cm, " +
                          $"Δ{dAng:+0.0;-0.0;0.0}°) — a zoom, snap turn, teleport or world grab may " +
                          "never move these; only physically walking may."
                        : $"CHANGED since the previous line (Δ{dDist * 100f:+0.0;-0.0;0.0} cm, " +
                          $"Δ{dAng:+0.0;-0.0;0.0}°). Legitimate ONLY if you walked, grabbed the board " +
                          "or resized it; if the log shows a zoom or a turn at this timestamp and no " +
                          "grab, the pin is not rig-local any more — report this line.");
            _loggedDistance = distance;
            _loggedAngular = angular;
        }
        string anchorState = CardsConfig.TrayFollow.Value
            ? "FOLGEN (the tray hangs under the rig itself)"
            : _pinFrame == null
                ? "FIXIERT but with NO pin frame — report this line"
                : _pinFrame.HasRig
                    ? $"FIXIERT on the player's own frame (rig ×{_pinFrame.SeatedScale:F2}, " +
                      $"{_pinFrame.RigChanges} rebuild/recentre carr{(_pinFrame.RigChanges == 1 ? "y" : "ies")} so far)"
                    : "FIXIERT, holding its last frame (no live rig right now)";
        VRLog.Info("Cards",
            $"BOARD SIZE [{trigger}]: {apparent * 100f:F1} cm apparent ({units:F3} size units, own " +
            $"localScale {_root.localScale.x:F3}). WORLD SCALE {rigScale:F2} = base {baseScale:F2} " +
            $"(auto from the game's tile size) × table zoom {zoom:F2}x ([Comfort] " +
            $"SavedScaleMultiplier, the world pinch) — and anchor {BoardSizeFrame.AnchorRatio(parent, rigScale):F2} " +
            $"(parent chain ×{parent:F2} ÷ rig ×{rigScale:F2}; 1.00 = the board's anchor carries the " +
            $"player's scale and NOTHING about its size depends on the zoom). {distanceText} " +
            $"ANCHOR: {anchorState}. BOUNDS {unitsLo:F3}–" +
            $"{unitsHi:F3} units = {minW * 100f:F0}–{maxW * 100f:F0} cm, from the board's own " +
            $"{BoardSizeFrame.BoardWidthLocal * 100f:F0} cm of geometry and [Cards] BoardMinWidthMeters/" +
            $"BoardMaxWidthMeters — no rig, no mask, no avatar dial; the two-hand gesture reaches " +
            $"{reachLo * 100f:F0}–{reachHi * 100f:F0} cm of that (the shared handle caps the raw " +
            $"factor at {BoardSizeFrame.GestureFactorMax:F2}). " +
            (held
                ? $"HELD: the size freeze is ENGAGED — apparent size stays {apparent * 100f:F1} cm for " +
                  "the whole grip, at any zoom; only the two-hand resize may change it."
                : $"Not held ({(CardsConfig.TrayFollow.Value ? "FOLGEN" : "FIXIERT")})."));
    }

    /// <summary>
    /// The board's distance from the head in PLAYER metres and the angle its width subtends there —
    /// the term the size diagnostic was blind to until 2026-08-18 (see
    /// <see cref="LogBoardSizeDiagnostics"/>). Measured head-to-board-centre, which is the distance
    /// the player's own "how big does it look" judgement uses. False with no tracked head or a
    /// degenerate rig scale.
    /// </summary>
    private bool TryGetHeadDistance(out float metres, out float angularDegrees)
    {
        metres = 0f;
        angularDegrees = 0f;
        if (_root == null)
            return false;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return false;
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out _, out float rigScale))
            return false;
        if (!BoardSizeFrame.TryPlayerDistance(head.transform.position, _root.position, rigScale, out metres))
            return false;
        angularDegrees = BoardSizeFrame.AngularWidthDegrees(perUnit * _root.localScale.x, metres);
        return true;
    }

    /// <summary>Throttle for <see cref="LogBoardSizeDiagnostics"/> on the per-frame path.</summary>
    private float _nextBoardSizeLog;

    /// <summary>Per-frame diagnostic gate: one line per 10 s, plus one immediately whenever the
    /// apparent size or the grip state actually CHANGES (a zoom sweep that leaves the size alone is
    /// the fix working and must not flood the log), plus a RATE-LIMITED one when the board's
    /// angular size moves. That third trigger is the 2026-08-18 addition and it is deliberately
    /// throttled where the other two are not: walking changes the angular size legitimately and
    /// continuously, so an unthrottled line would flood a log with the fix WORKING — but a zoom
    /// that moves it is the defect itself, and one line per second is enough to catch it in the
    /// act, timestamped next to whatever moved the rig.</summary>
    private void TickBoardSizeDiagnostics()
    {
        if (_root == null)
            return;
        bool held = _handle != null && _handle.IsGrabbed;
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out _, out _))
            return;
        float apparent = perUnit * _root.localScale.x;
        bool changed = _loggedApparent < 0f
                       || Mathf.Abs(apparent - _loggedApparent) > 0.005f * Mathf.Max(_loggedApparent, 1e-3f)
                       || held != _loggedHeld;
        float now = Time.unscaledTime;
        bool angularMoved = false;
        if (!changed && _loggedAngular > 0f && now >= _nextBoardAngleLog
            && TryGetHeadDistance(out _, out float angular)
            && Mathf.Abs(angular - _loggedAngular) > 0.02f * _loggedAngular)
        {
            angularMoved = true;
            _nextBoardAngleLog = now + 1f;
        }
        if (!changed && !angularMoved && now < _nextBoardSizeLog)
            return;
        _nextBoardSizeLog = now + 10f;
        _loggedApparent = apparent;
        _loggedHeld = held;
        LogBoardSizeDiagnostics(changed
            ? (held ? "grip/size change" : "size change")
            : angularMoved ? "angular size moved" : "periodic");
    }

    private float _loggedApparent = -1f;
    private bool _loggedHeld;

    /// <summary>The distance/angular pair the previous <see cref="LogBoardSizeDiagnostics"/> line
    /// reported, so the next one can state what they DID rather than only what they are. −1 until
    /// the first line of a session.</summary>
    private float _loggedDistance = -1f;
    private float _loggedAngular = -1f;

    /// <summary>Throttle for the angular-size trigger — see <see cref="TickBoardSizeDiagnostics"/>
    /// for why this one is rate-limited and the size trigger is not.</summary>
    private float _nextBoardAngleLog;

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));
}
