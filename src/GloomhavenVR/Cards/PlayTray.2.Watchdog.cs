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
    //   * FOLLOW mode anchors the board in RIG space, so physically walking across the room
    //     leaves it standing where it was (and at the diorama scale ~20.7 world units per real
    //     metre, "a few steps" is tens of world units of separation);
    //   * PINNED mode anchors it in raw WORLD space, so a recentre — which TELEPORTS the rig to
    //     the table-edge/circle seat, VRRigDriver.Recenter — strands it at the old seat;
    //   * the pin holder's scale was baked ONCE at pin time from the rig's lossy scale, so a
    //     later world-grab zoom silently rescaled the pinned board's world OFFSET with it.
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

        // Housekeeping: keep the PINNED holder honest across tracking-origin changes. It is
        // pose-preserving in the user's frame of reference — the board keeps the same place
        // RELATIVE TO THE PLAYER across a recentre, which is what "it stayed where I put it"
        // means — so it is not a move under the ruling above.
        SyncPinHolder();
        // Runs AFTER SyncPinHolder (which is what makes the anchor honest) and BEFORE the
        // gripped early-out below — a held board is exactly the case it exists for.
        TickHeldSizeFreeze();
        // NO world-tilt compensation (user decision 2026-08, supersedes item 11): a PINNED
        // board is deliberately WORLD-static — a tilt change leaves it untouched (it then
        // looks tilted like the rest of the world; the user re-adjusts it in Free mode).
        // The old SyncWorldTiltComp counter-rotation visibly dragged the board through the
        // tilt tween ("nachziehen") and was removed outright.

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
        // FOLLOW MODE ONLY (user ruling 2026-08-07: "Fixiert heißt: völlig unabhängig vom
        // Character, bewegt sich in KEINSTER Weise, außer es wird aktiv verschoben oder
        // skaliert"). The clamp measures in PLAYER units — world width ÷ LIVE rig scale — and
        // for a PINNED board that measure is the bug, not the safeguard: the board's WORLD size
        // is frozen, but a world-grab zoom rescales the PLAYER, so the apparent width drifts
        // across the 18/140 cm limits without anyone touching the board, and the clamp then
        // "corrected" the frozen world size frame after frame. That is precisely the 2026-08-07
        // report ("beim Zoomen nach einer Weile wird das fixierte Board kleiner oder größer");
        // the hardware log convicts it — 18× "Board size CLAMPED" with a CONSTANT parent chain
        // ×40.10 (the pin-holder snapshot) against rig scales ×5.13–×137.19 (the zoom). A pinned
        // board's apparent size changing with zoom is what pinning MEANS; the size was legal
        // when the player set it (the two-hand gesture window GrabScaleLimits bounds every
        // explicit resize live, in BOTH modes), so while pinned nothing may re-derive it.
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
    ///    holder at pin time. The rig is then rescaled live by the table zoom (0.1×–12× of base),
    ///    and a holder left at the baked value no longer carries the player's scale — so the
    ///    board's APPARENT size, its size bounds and the metres its head-relative offsets are
    ///    persisted in all start drifting with the zoom. Re-assert the holder scale from the live
    ///    rig scale every frame and restore the tray's WORLD pose around the write, so the board
    ///    stays bolted to its world spot AND the same size to the eye at any zoom.
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
                NotePinnedWrite("tracking-origin carry (SyncPinHolder — rig rebuild/recentre)");
                VRLog.Info("Cards", $"Control board (PINNED) carried through a tracking-origin change " +
                                    $"(rig pose version {_pinPoseVersion} → {version}: rig rebuild or " +
                                    $"recentre): {before} → {pos}. A world-space pin would have been " +
                                    "stranded at the old seat.");
            }
        }
        _pinPoseVersion = version;

        // (1) LIVE HOLDER RESCALE — REINSTATED, deliberately, against the block that used to
        // stand here forbidding it. That block is quoted in full below because reversing a
        // written invariant without quoting it is how an invariant gets reversed twice.
        //
        //     "NO live holder rescale. […] it cannot drift. The holder sits at the world ORIGIN
        //      with identity rotation and a scale baked once at pin time; it is NOT parented under
        //      the rig, so rescaling the rig cannot move or resize anything underneath it. […] the
        //      rescale itself was the bug the tester then reported ('world zoom zooms the pinned
        //      control board too — that must not happen'). With the holder FIXED, world size and
        //      world distance are both constant, so a pinned board is completely unaffected by
        //      zoom, which is the whole point of pinning it."
        //
        // WHAT THAT ARGUMENT MISSED. "Unaffected by zoom" was asserted in WORLD units, and the
        // player does not live in world units. A zoom rescales the PLAYER: with the holder frozen,
        // the board's world size is constant while the player's own scale runs 0.1×–12×, so its
        // APPARENT size — the only size anybody can see — changes by the full zoom factor. The
        // tester has now reported exactly that three times:
        //   2026-08-07  "beim Zoomen nach einer Weile wird das fixierte Board kleiner oder größer"
        //   2026-08-15  "in einer Hand das Controllboard und dann gezoomed — dann hat das
        //                controllboard mitgezoomed, das soll nicht passieren"
        // and his standing FIXIERT ruling is the tie-breaker, read literally:
        //   "Fixiert heißt: völlig unabhängig vom Character, bewegt sich in KEINSTER Weise, außer
        //    es wird aktiv verschoben oder skaliert."
        // Zooming is not actively scaling the board. A fixed board whose apparent size follows the
        // zoom is changing without being touched, which that sentence forbids.
        //
        // WHAT THE REINSTATEMENT COSTS AND WHY IT IS CHEAP NOW. The board's WORLD size does follow
        // the zoom again — that is unavoidable: constant apparent size × a growing player IS a
        // growing world size. The 2026-08 complaint about that ("world zoom zooms the pinned
        // control board too") was made against a version that ALSO let the rescale drag the board
        // across the room, because the tray sits at a non-zero local offset under the holder and
        // nothing restored its world pose. Here the pose is captured and written back around the
        // holder write, so position and rotation are bit-stable and only the size follows.
        //
        // WHAT IT BUYS: parentScale ≡ rigScale in BOTH anchor modes (BoardSizeFrame's invariant).
        // Every quantity that was silently zoom-coupled while pinned falls out at once — the
        // apparent size, the two-hand gesture's window, the min/max the player can reach, and the
        // divisor PersistPoseToConfig stores the head-relative offsets with (his ModBuild 158 log
        // persisted "fwd 3.39 m, down 1.36 m" for a board an arm's length from his face: that is
        // the stale divisor, measured).
        if (rig != null)
        {
            float live = rig.lossyScale.x;
            float baked = _pinRoot.localScale.x;
            if (live > 1e-5f && !float.IsInfinity(live)
                && Mathf.Abs(live - baked) > 1e-4f * Mathf.Max(baked, 1e-4f))
            {
                Vector3 keepPos = _root.position;
                Quaternion keepRot = _root.rotation;
                _pinRoot.localScale = Vector3.one * live;
                _root.SetPositionAndRotation(keepPos, keepRot); // holder scales about the origin
                NotePinnedWrite("zoom-invariant size hold (SyncPinHolder — holder tracks the rig scale)");
                CardsDriver.NoteExpectedPoseChange("pinned board size held against the table zoom");
                float now = Time.unscaledTime;
                if (now >= _nextPinScaleLog)
                {
                    _nextPinScaleLog = now + 5f;
                    VRLog.Info("Cards", $"Board (FIXIERT) size held against the zoom: pin holder " +
                                        $"×{baked:F2} → ×{live:F2} (the live rig scale), world pose " +
                                        $"preserved. The board's APPARENT width is unchanged — that " +
                                        "is what FIXIERT means (user ruling 2026-08-07, re-reported " +
                                        "2026-08-15); its world size follows the player because it " +
                                        "has to.");
                }
            }
        }

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

    /// <summary>Log throttle for the pin holder's zoom tracking (a zoom sweep moves it every
    /// frame; one line per 5 s is enough to prove it is working).</summary>
    private float _nextPinScaleLog;

    // ------------------------------------------------------------- PINNED FREEZE SENTINEL --
    //
    // User ruling 2026-08-07: a FIXIERT board is a world-frozen object — no position, rotation
    // or scale change from ANY automatic source; only an explicit user grab/resize may move it.
    // The sentinel is the enforcement's black box: every frame it compares the pinned tray's
    // WORLD pose (position, rotation, LOSSY scale — world size, the quantity the ruling freezes;
    // the CardsDriver issue-C watch compares parent-LOCAL and thus cannot see a holder rescale)
    // against last frame's, and any change is logged WITH ITS SOURCE. Sanctioned writers
    // announce themselves via NotePinnedWrite; a change with no announcement logs as a Warn —
    // one grep ("PINNED tray transform WRITE") convicts a leftover writer in the next hardware
    // log instantly. It never mutates anything; it only observes and re-baselines.

    private Vector3 _pinFreezePos;
    private Quaternion _pinFreezeRot = Quaternion.identity;
    private float _pinFreezeWorldScale = -1f;
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

    /// <summary>Per-frame world-pose diff of a PINNED tray — see the sentinel block above.</summary>
    private void TickPinnedFreezeSentinel()
    {
        if (_root == null || CardsConfig.TrayFollow.Value)
        {
            _pinFreezeValid = false;
            _pinFreezeSource = null;
            return;
        }
        Vector3 pos = _root.position;
        Quaternion rot = _root.rotation;
        float worldScale = _root.lossyScale.x;
        string? source = _pinFreezeSource;
        _pinFreezeSource = null; // announcements are valid for exactly one sentinel pass

        if (_pinFreezeValid && IsFinite(pos))
        {
            // Epsilons in PLAYER-perceivable terms: the parent chain (the pin holder carries the
            // diorama scale) converts tray metres to world units, so 2 mm of perceived motion is
            // 0.002 × that in world units. Scale compares relatively (0.2 % — one clamp frame in
            // the 2026-08-07 log moved it ~1 %, comfortably above; float noise stays below).
            float unit = Mathf.Max(worldScale / Mathf.Max(_root.localScale.x, 1e-4f), 1e-4f);
            float posEps = 0.002f * unit;
            bool moved = (pos - _pinFreezePos).sqrMagnitude > posEps * posEps
                         || Quaternion.Angle(rot, _pinFreezeRot) > 0.25f
                         || Mathf.Abs(worldScale - _pinFreezeWorldScale)
                            > 0.002f * Mathf.Max(_pinFreezeWorldScale, 1e-4f);
            if (moved)
            {
                float now = Time.unscaledTime;
                if (now >= _nextPinFreezeLog) // an unknown per-frame writer must not flood the log
                {
                    _nextPinFreezeLog = now + 1f;
                    string detail = $"world pos {_pinFreezePos} → {pos}, " +
                                    $"rot Δ{Quaternion.Angle(rot, _pinFreezeRot):F1}°, " +
                                    $"world scale {_pinFreezeWorldScale:F3} → {worldScale:F3} " +
                                    $"(own localScale {_root.localScale.x:F3}).";
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
        _pinFreezeWorldScale = worldScale;
        _pinFreezeValid = IsFinite(pos);
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
    /// <para>WITH <see cref="SyncPinHolder"/> KEEPING THE ANCHOR ON THE LIVE RIG SCALE, THIS SHOULD
    /// NEVER FIRE, and that is the point of the log line: it is an assertion, not a mechanism. The
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
        // Defensive: the caller gates this to FOLLOW mode (pinned = world-frozen, ruling
        // 2026-08-07); should any future path run it on a pinned tray, the freeze sentinel
        // names it instead of warning about an unknown writer.
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
    /// <item>while held: that the size freeze is engaged and what the apparent size is.</item>
    /// </list>
    /// <para>WHAT WOULD DISPROVE THE FIX. "anchor 1.00" is the whole claim. If a future log shows
    /// this line with an anchor ratio away from 1.00 while a zoom is in progress, the pin holder is
    /// not tracking the rig and every bound printed beside it is zoom-coupled again — that is the
    /// 2026-08-15 defect, and it would read here as a number before the player ever notices it.
    /// Equally: two of these lines from either side of a zoom sweep MUST agree on "apparent" and on
    /// the min/max pair. If they do not, the freeze is not holding.</para>
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
        VRLog.Info("Cards",
            $"BOARD SIZE [{trigger}]: {apparent * 100f:F1} cm apparent ({units:F3} size units, own " +
            $"localScale {_root.localScale.x:F3}). WORLD SCALE {rigScale:F2} = base {baseScale:F2} " +
            $"(auto from the game's tile size) × table zoom {zoom:F2}x ([Comfort] " +
            $"SavedScaleMultiplier, the world pinch) — and anchor {BoardSizeFrame.AnchorRatio(parent, rigScale):F2} " +
            $"(parent chain ×{parent:F2} ÷ rig ×{rigScale:F2}; 1.00 = the board's anchor carries the " +
            $"player's scale and NOTHING about its size depends on the zoom). BOUNDS {unitsLo:F3}–" +
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

    /// <summary>Throttle for <see cref="LogBoardSizeDiagnostics"/> on the per-frame path.</summary>
    private float _nextBoardSizeLog;

    /// <summary>Per-frame diagnostic gate: one line per 10 s, plus one immediately whenever the
    /// apparent size or the grip state actually CHANGES (the two events worth a line — a zoom sweep
    /// that leaves the size alone is the fix working and must not flood the log).</summary>
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
        if (!changed && now < _nextBoardSizeLog)
            return;
        _nextBoardSizeLog = now + 10f;
        _loggedApparent = apparent;
        _loggedHeld = held;
        LogBoardSizeDiagnostics(changed ? (held ? "grip/size change" : "size change") : "periodic");
    }

    private float _loggedApparent = -1f;
    private bool _loggedHeld;

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));
}
