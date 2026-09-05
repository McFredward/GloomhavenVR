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
        // The size/anchor diagnostic runs BEFORE every early-out below, including the gripped one:
        // the state it exists to prove is exactly the state a gripped board is in.
        if (_root != null && _wantVisible)
            TickBoardAnchorDiagnostics();
        // ARRIVAL WINDOW EDGE, observed UNCONDITIONALLY and for the same reason: the window can
        // open while the tray is still hidden or its placement still deferred (a scenario load is
        // exactly such a moment), and an edge missed there is an arrival never guarded. Arming is
        // free; the judging half runs below, after the pin carry.
        ObserveArrivalWindow();
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
        // ARRIVAL SEAT GUARD — AFTER SyncPinHolder, and that order is load-bearing. Both react to
        // the same RigPoseVersion bump; the carry is the cheap answer (keep the player's own
        // offset) and the guard is the verdict on the result. Judging BEFORE the carry would judge
        // a pose that is about to be replaced, and the carry would then undo the correction from a
        // rig-local cache taken before it — the classic "measured the wrong stage" shape.
        TickArrivalSeatGuard();
        // NO world-tilt compensation (user decision 2026-08, supersedes item 11): a PINNED
        // board is deliberately WORLD-static — a tilt change leaves it untouched (it then
        // looks tilted like the rest of the world; the user re-adjusts it in Free mode).
        // The old SyncWorldTiltComp counter-rotation visibly dragged the board through the
        // tilt tween ("nachziehen") and was removed outright.

        // GRAB EDGE. The size the board had the frame the hand closed on it, so
        // PersistPoseToConfig can tell a CARRY from a RESIZE (see the guard there): a one-hand
        // carry must never re-author the board's size, and since 2026-08-25 the live size can
        // legitimately differ from the configured one (the push below), which would otherwise
        // make every carry write the push into the config and re-seat BoardScale_{board}.
        bool heldNow = _handle != null && _handle.IsGrabbed;
        if (heldNow && !_wasHeld)
        {
            _scaleAtGrabStart = _root.localScale.x;
            LogResizeWindow();
        }
        _wasHeld = heldNow;

        // A gripped board is being deliberately placed — never touch it mid-carry. The pinned
        // freeze sentinel drops its baseline too: a grab is the user's own hand, and the pose it
        // leaves behind is by definition sanctioned (it re-baselines silently on release).
        if (heldNow)
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
        // IT RUNS IN BOTH MODES AGAIN (user ruling 2026-08-25). The FOLLOW-ONLY gate that stood
        // here came from the 2026-08-07 report ("beim Zoomen nach einer Weile wird das fixierte
        // Board kleiner oder größer") and it was the right fix for the code AS IT THEN WAS: the
        // window was measured against the live rig while the board's world size was frozen, so the
        // window drifted across a stationary board and the clamp "corrected" the board every frame,
        // in both directions, forever — 18× "Board size CLAMPED" in one session with a CONSTANT
        // parent chain ×40.10 against rig scales ×5.13–×137.19.
        //
        // What makes it safe now is not the gate, it is that ClampApparentSize is a PUSH: it moves
        // the board only on the frames the window has walked PAST it by more than the freeze
        // sentinel's own noise band, and never moves it back. So a zoom out and back leaves the
        // board at the size the outward leg pushed it to and touches it on no other frame, which is
        // the ruling verbatim — "dann wächst das board mit dem minimum mit … aber eben nur bei
        // diesen Zwei Randfällen — ansonsten bleibt es fix". A gate here would only make the FIXIERT
        // board the one place where his own configured 18/140 cm limits do not hold.
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
                NotePinnedWrite("tracking-origin carry (SyncPinHolder — rig rebuild/recentre)");
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
        // The holder scale is therefore written ONCE, at PIN time, and left alone.
        //
        // 2026-08-25 — THE HOLDER SCALE NOW HAS A SECOND WRITER, AND IT IS NOT A LIVE RESCALE.
        // PlayTray.TryRestoreCapturedPinFrame re-creates the holder across a BOARD SWITCH with the
        // scale the previous holder CARRIED, read from the capture taken microseconds earlier in the
        // same rebuild. That is the opposite of what this block forbids: the value comes from the
        // frozen holder, never from the live rig, and it is written once per switch rather than once
        // per frame. The invariant this block defends — a pinned board never rides the world-grab
        // zoom — is exactly what that restore preserves, because the pre-2026-08-25 code DID re-seed
        // the fresh holder from the live rig (via ApplyFollowMode) and that is what shrank the board
        // to a third of its size on his hardware. Do not "simplify" the restore back into
        // ApplyFollowMode: that is the bug.

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

    /// <summary>
    /// The sentinel's POSITION epsilon in world units, in PLAYER-perceivable terms: the parent
    /// chain (the pin holder carries the diorama scale) converts tray metres to world units, so
    /// 2 mm of perceived motion is 0.002 x that. Hoisted out of the sentinel because
    /// <see cref="TickBoardAnchorDiagnostics"/> has to adjudicate against the SAME number — its
    /// own "did it move" test is a flat 1e-4 world units, which is finer, and the gap between the
    /// two is the difference between "a writer nobody saw" and "float noise". Two copies of this
    /// number would be exactly the mirrored constant scripts/check-mirrors.sh exists to hunt.
    /// </summary>
    /// <summary>The sentinel's world-SCALE epsilon, relative (0.2 % — one clamp frame in the
    /// 2026-08-07 log moved it ~1 %, comfortably above; float noise stays below). Hoisted for the
    /// same reason as the position one: <see cref="TickBoardAnchorDiagnostics"/> adjudicates
    /// "float noise between two differently-fine instruments" against BOTH terms, and it has to be
    /// the sentinel's own numbers rather than a second copy of them.</summary>
    private const float PinFreezeScaleEpsilon = 0.002f;

    private float PinFreezePosEpsilon(float worldScale)
    {
        float local = _root != null ? _root.localScale.x : 1f;
        float unit = Mathf.Max(worldScale / Mathf.Max(local, 1e-4f), 1e-4f);
        return 0.002f * unit;
    }

    /// <summary>
    /// Sanctioned PINNED world-pose writes the freeze sentinel has ACTUALLY OBSERVED this session
    /// (announced by <see cref="NotePinnedWrite"/> and followed by a real pose change), and the
    /// writer named by the most recent one. Read by <see cref="TickBoardAnchorDiagnostics"/> as a
    /// delta since its own last line, exactly the way <see cref="_sizePushCount"/> already is.
    /// </summary>
    private int _pinSanctionedMoveCount;
    private string? _pinSanctionedMoveLast;

    /// <summary>Observed PINNED pose changes with NO writer announcing itself. This is the count
    /// that means what the anchor line's defect verdict has always claimed to mean.</summary>
    private int _pinUnknownMoveCount;

    /// <summary>
    /// SANCTIONED WRITES THE SENTINEL WAS BLIND FOR, and the last one's name. A writer announces
    /// itself through <see cref="NotePinnedWrite"/>, but the sentinel only runs from
    /// <see cref="TickLostWatchdog"/> BELOW its `!_wantVisible || !_placed` early-out, so the one
    /// write it can never adjudicate is the FIRST PLACEMENT of a scenario — the tray is still
    /// hidden and unplaced at the moment PlaceAtHead announces itself, the announcement is then
    /// consumed by the first pass with no baseline to compare against, and the move goes into
    /// neither tally.
    ///
    /// <para>That is the ModBuild 434 log's line 930 exactly: the BOARD ANCHOR line measured
    /// 8469.27 mm-world and a ×2.17060 rescale on the same frame the sentinel reported "no move at
    /// all", and the two instruments were BOTH right — one samples above that early-out and the
    /// other below it. The anchor line then reached its last branch and called a legitimate first
    /// placement a defect. Counting the blind announcement separately (rather than folding it into
    /// <see cref="_pinSanctionedMoveCount"/>, which means "announced AND observed to move" and
    /// must keep meaning that) lets the anchor line name the writer without either instrument
    /// claiming to have seen something it could not.</para>
    /// </summary>
    private int _pinBlindAnnouncementCount;
    private string? _pinBlindAnnouncementLast;

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
            // Epsilons in PLAYER-perceivable terms; both now live beside each other as
            // PinFreezePosEpsilon / PinFreezeScaleEpsilon, because TickBoardAnchorDiagnostics has
            // to adjudicate against these exact numbers and a second copy of them would be the
            // mirrored constant scripts/check-mirrors.sh exists to hunt. The rotation term stays
            // inline: nothing else compares against it.
            float posEps = PinFreezePosEpsilon(worldScale);
            bool moved = (pos - _pinFreezePos).sqrMagnitude > posEps * posEps
                         || Quaternion.Angle(rot, _pinFreezeRot) > 0.25f
                         || Mathf.Abs(worldScale - _pinFreezeWorldScale)
                            > PinFreezeScaleEpsilon * Mathf.Max(_pinFreezeWorldScale, 1e-4f);
            if (moved)
            {
                // TALLY BEFORE THE THROTTLE, and tally what was OBSERVED rather than what was
                // announced. An announcement (NotePinnedWrite) is a claim that a writer is about
                // to write; several of them are world-pose-PRESERVING by construction
                // (ApplyFollowMode's re-parent), so counting announcements would let a write that
                // moved nothing "explain" a different move in the same window. What is counted
                // here is the conjunction the sentinel is already computing every frame: the pose
                // really changed AND a writer had named itself. The counters are deliberately
                // outside the log throttle — five of the seven pin carries in the 2026-09-05
                // hardware log were suppressed by it, and a term that goes silent under load is
                // exactly how an instrument starts agreeing with a broken build.
                if (source != null)
                {
                    _pinSanctionedMoveCount++;
                    _pinSanctionedMoveLast = source;
                }
                else
                {
                    _pinUnknownMoveCount++;
                }
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
        else if (source != null)
        {
            // A WRITER ANNOUNCED ITSELF AND THERE WAS NOTHING TO COMPARE IT AGAINST — see
            // _pinBlindAnnouncementCount. No verdict is possible here (whether the pose actually
            // changed is exactly what a baseline would have told us), so this is recorded as what
            // it is: a named writer, unadjudicated. The anchor line reads it only when its OWN
            // measurement says the board moved, which is the term the sentinel is missing.
            _pinBlindAnnouncementCount++;
            _pinBlindAnnouncementLast = source;
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

    /// <summary>Was the handle gripped last tick? Rising-edge detector for
    /// <see cref="_scaleAtGrabStart"/>.</summary>
    private bool _wasHeld;

    /// <summary>The board's own localScale the frame the current grab started, or -1 when no grab
    /// is in flight. <see cref="PersistPoseToConfig"/> consumes it to tell a CARRY (size unchanged,
    /// so the config's size must not be rewritten) from a RESIZE (the player authored a new size).
    /// </summary>
    private float _scaleAtGrabStart = -1f;

    /// <summary>Log throttle for the push (at most one line a second while it rides a bound).</summary>
    private float _nextSizeClampLog;

    /// <summary>Change gate for the push line: the (rig scale, resulting localScale) pair it last
    /// reported. A zoom is a continuous stream, so the throttle alone would still print a line a
    /// second forever once the board is parked against a bound.</summary>
    private float _loggedPushRig = -1f;
    private float _loggedPushScale = -1f;

    /// <summary>How many frames the push has fired this session. A push is PERMANENT (it never
    /// springs back), so this is the honest measure of how much of the board's current size the
    /// player did not author himself.</summary>
    private int _sizePushCount;

    /// <summary>
    /// The dead band the push shares with the PINNED FREEZE SENTINEL, and it is deliberately that
    /// sentinel's own number rather than a new tuned one: the sentinel calls a world-scale ratio
    /// within 5e-4 of 1 "float noise, not tolerance" (see <see cref="TickBoardAnchorDiagnostics"/>).
    /// A violation smaller than that is not a violation, it is the same number twice — and a write
    /// that small would be invisible AND would be reported as "no change" by the very line that
    /// exists to watch this. It also answers the touching-condition flicker the user's "an das
    /// Minimum angrenzt" describes: the board is only moved once the bound has actually walked
    /// PAST it by more than noise. Because the push clamps the LIVE size (not a re-derived one),
    /// skipped sub-band violations accumulate into the next frame's comparison instead of being
    /// lost, so the dead band cannot let the board drift out of the window by more than itself.
    /// </summary>
    private const float ScaleNoiseEpsilon = 5e-4f;

    /// <summary>
    /// THE SMALLEST RESIZE WINDOW THAT IS STILL A CONTROL RATHER THAN A DECORATION, as a ratio of
    /// its own two ends. It is an ASSERTION, not a tuning dial — nothing clamps to it; the window
    /// is measured against it and the verdict is printed by <see cref="LogResizeWindow"/>, so a
    /// build that ships a useless window says so in one line instead of looking like a broken
    /// gesture. That is exactly how the ModBuild 350 defect presented: "Das konnte ich im Test
    /// nicht" — nothing threw, nothing logged, the pinch simply did nothing.
    ///
    /// <para>1.5, i.e. the board must be able to reach half again its size or two thirds of it,
    /// and the number is read off the GESTURE rather than off taste. The two-hand pinch scales by
    /// the ratio of the hand separation to its separation at grab time (<c>PanelGrabHandle</c>:
    /// <c>_rootScale0 × d / _anchorDistance</c>), so traversing a span of S needs the hands to
    /// travel a factor S apart. A comfortable two-hand grip sits at roughly 40 cm of separation
    /// and opens to about 60 cm / closes to about 25 cm without the player moving their feet — a
    /// reachable factor of ~1.5 in each direction from rest. A window narrower than that cannot be
    /// traversed by the gesture that is supposed to traverse it, and a window of a few per cent is
    /// arithmetically non-zero and useless in a headset.</para>
    ///
    /// <para>For scale: the shipped 18–140 cm apparent window is a span of ×7.78, five times this
    /// floor, and it is ×7.78 AT EVERY ZOOM because both of its ends carry the same divisor. The
    /// floor can therefore only fire if someone dials <c>[Cards] BoardMinWidthMeters</c> up against
    /// <c>BoardMaxWidthMeters</c>, which is the one remaining way to make this window small.</para>
    /// </summary>
    private const float MinUsableSpan = 1.5f;

    /// <summary>
    /// THE BOARD'S RESIZE WINDOW AT THE CURRENT ZOOM, in units of the tray's own localScale, and
    /// the ONE place it is computed — <see cref="WorldUI.IPanelGrabOwner.GrabScaleLimits"/> (what
    /// the pinch may write) and <see cref="LogResizeWindow"/> (what the log reports) read it from
    /// here, or the log would be describing a window the gesture does not have.
    ///
    /// <para>It is the user's apparent-metre window and nothing else:
    /// <c>[Cards] BoardMinWidthMeters</c> / <c>BoardMaxWidthMeters</c> divided by the apparent
    /// width one localScale unit buys at this instant
    /// (<see cref="TryGetApparentWidthPerScaleUnit"/>, the same measure the push uses). Both ends
    /// carry that same divisor, so the SPAN is the constant <c>Max/Min</c> — ×7.78 at the shipped
    /// 18/140 cm — at every rig scale and in both anchor modes. That constancy IS the user's rule:
    /// "Gewährleiste dass man in jeder Zoomgröße innerhalb des jeweiligen dortigen Minimums und
    /// Maximums auch das board selber noch skalieren kann."</para>
    ///
    /// <para><b>IT IS NO LONGER INTERSECTED WITH WHAT THE CONFIG CAN STORE (ModBuild 351).</b> The
    /// full reasoning lives at the call site in <c>PlayTray.1.Core.cs</c>; in one line, the
    /// storable band <c>TrayScale × BoardScale_{board}</c> is a fixed pair of numbers while this
    /// window is PROPORTIONAL TO THE RIG SCALE in FIXIERT, and a fixed band cannot contain a
    /// window that slides across it. In the ModBuild 350 hardware log, at rig ×73.93 this window
    /// was localScale 5.17–40.23 against a storable band of 0.14–2.17: no overlap at all.</para>
    /// </summary>
    private bool TryGetResizeWindow(out float lo, out float hi, out float live,
                                    out float perUnit, out float rigScale)
    {
        lo = hi = live = perUnit = 0f;
        rigScale = 1f;
        if (_root == null || !TryGetApparentWidthPerScaleUnit(out perUnit, out _, out rigScale))
            return false;
        if (!(perUnit > 1e-6f) || float.IsInfinity(perUnit))
            return false;
        live = _root.localScale.x;
        if (!(live > 1e-4f) || float.IsInfinity(live))
            return false;
        lo = MinWidthMeters / perUnit;
        hi = MaxWidthMeters / perUnit;   // MaxWidthMeters is floored at MinWidthMeters + 2 cm
        return hi > lo;
    }

    /// <summary>
    /// THE FOUR NUMBERS, ONCE PER GRAB: the min, the max, the current size, and the room left in
    /// each direction. Printed on the rising edge of the handle grip, because that is the instant
    /// the player is about to try to resize and the instant "did the clamp eat it" is decided.
    ///
    /// <para>IT EXISTS BECAUSE THE ModBuild 350 LOG COULD NOT ANSWER THAT QUESTION. That log has
    /// 124 <c>BOARD SIZE PUSHED</c> lines and 2487 <c>BOARD ANCHOR</c> lines, and between them they
    /// print the apparent width, both bounds in both units, the rig scale, the parent chain and the
    /// push count — everything except the window the GESTURE was actually handed. So "ich konnte
    /// nicht skalieren" and "the clamp ate every frame of it" were indistinguishable, and the only
    /// evidence that separated them was indirect: seven consecutive "Tray layout persisted … SIZE
    /// NOT WRITTEN — the grab was a carry, localScale 5.567 → 5.567" lines, i.e. releases from
    /// grabs during which the size had not moved by even float noise.</para>
    ///
    /// <para>Note tier, and HW-VERIFY: the next hardware round has to be judgeable from this line
    /// at the SHIPPED log level, without a DEBUG build.</para>
    /// </summary>
    private void LogResizeWindow()
    {
        if (_root == null)
            return;
        string mode = CardsConfig.TrayFollow.Value ? "FOLGEN" : "FIXIERT";
        if (!TryGetResizeWindow(out float lo, out float hi, out float live,
                                out float perUnit, out float rigScale))
        {
            // HW-VERIFY
            VRLog.Note("Cards", $"BOARD RESIZE WINDOW ({mode}): measure unavailable (no rig, or a " +
                                "degenerate transform), so the pinch falls back to " +
                                $"PanelGrabHandle's generic {WorldUI.PanelGrabHandle.MinScale}–" +
                                $"{WorldUI.PanelGrabHandle.MaxScale} factor range. If the board " +
                                "resizes wrongly at this timestamp, THIS line is why.");
            return;
        }

        ControlBoard board = CardsConfig.CurrentBoard;
        float boardScale = Mathf.Max(0.01f, CardsConfig.BoardScale(board).Value);
        float roomDown = live / Mathf.Max(lo, 1e-6f);   // how many times SMALLER it may still go
        float roomUp = hi / Mathf.Max(live, 1e-6f);     // how many times BIGGER it may still go
        float span = hi / Mathf.Max(lo, 1e-6f);
        // HW-VERIFY
        VRLog.Note("Cards", $"BOARD RESIZE WINDOW ({mode}, rig ×{rigScale:F2}): current localScale " +
                            $"{live:F3} = {live * perUnit * 100f:F1} cm apparent. The min and max " +
                            $"that apply AT THIS ZOOM are {MinWidthMeters * 100f:F0}–" +
                            $"{MaxWidthMeters * 100f:F0} cm apparent = localScale {lo:F3}–{hi:F3} " +
                            $"({perUnit * 100f:F2} cm apparent per unit), so the pinch has ×" +
                            $"{roomDown:F2} of room DOWN and ×{roomUp:F2} of room UP. Span ×" +
                            $"{span:F2} against the usable floor ×{MinUsableSpan:F2} — " +
                            (span >= MinUsableSpan
                                ? "USABLE, and it is this same span at every zoom because both ends "
                                  + "carry the same divisor."
                                : "TOO NARROW TO USE: raise [Cards] BoardMaxWidthMeters or lower "
                                  + "BoardMinWidthMeters. The gesture is not broken, the window is.") +
                            " For reference and NOT intersected any more (ModBuild 351): the " +
                            $"storable band TrayScale {CardsConfig.TrayScaleMin}–" +
                            $"{CardsConfig.TrayScaleMax} × BoardScale_{board} {boardScale:F5} = " +
                            $"{CardsConfig.TrayScaleMin * boardScale:F3}–" +
                            $"{CardsConfig.TrayScaleMax * boardScale:F3}. That band does NOT ride " +
                            "the rig and this window does, so intersecting the two is what " +
                            "collapsed this window onto a single point at every deep zoom.");
    }

    /// <summary>
    /// Hold the board's APPARENT width inside [<see cref="MinWidthMeters"/>,
    /// <see cref="MaxWidthMeters"/>] — apparent meaning AS THE PLAYER SEES IT, i.e. world width
    /// divided by the LIVE rig scale (see <see cref="TryGetApparentWidthPerScaleUnit"/> for why
    /// that divisor, and why it is the live rig in FIXIERT too since 2026-08-25).
    ///
    /// <para><b>THIS METHOD IS NOW A PUSH, AND ONLY A PUSH.</b> User ruling 2026-08-25, verbatim
    /// and it is the whole specification: <i>"im 'Fixed' Modus ist das NICHT der Fall. Damit meine
    /// ich nur die Randfälle, dass ich zB ein super kleines board hab und ich mach mich größer und
    /// größer — damit wächst jetzt nun auch das erlaubte minimum der Größe vom board. Wenn jetzt
    /// die Größe des boards an das minimum angrenzt und der Spieler macht sich trotzdem noch größer
    /// — dann wächst das board mit dem minimum mit im 'fixed' Modus. Genau das Selbe andersrum.
    /// Aber eben nur bei diesen Zwei Randfällen — ansonsten bleibt es fix."</i></para>
    ///
    /// <para>Read literally, that is a clamp of the LIVE size against a window that moves with the
    /// player — which is exactly the expression below and nothing more:</para>
    /// <code>
    /// apparent  = localScale × BoardW × parentChain ÷ rigScale     (player metres)
    /// localScale ← clamp(apparent, min, max) ÷ (BoardW × parentChain ÷ rigScale)
    /// </code>
    /// <para>Three properties of that one line are the ruling, and each is worth naming because a
    /// different implementation of the same sentence would break one of them:</para>
    /// <list type="bullet">
    /// <item>IT IS MONOTONE. The window can only ever walk INTO the board; a clamp of a value that
    /// is already inside its window is the identity. So the board is never pulled — "ansonsten
    /// bleibt es fix" holds bit-exactly, at every zoom, without a mode flag.</item>
    /// <item>IT DOES NOT SPRING BACK. The board carries no memory of the size it had before a push:
    /// the moment the player reverses, the bound stops biting, the clamp is the identity again and
    /// the board is simply fixed at the size the bound left it at. An implementation that recomputed
    /// the target from the CONFIGURED size instead would be reversible — and would therefore drag
    /// the board back down behind the player, which is a move he did not ask for.</item>
    /// <item>IT IS NOT "THE BOARD FOLLOWS THE PLAYER". FIXIERT still means world-frozen; between the
    /// bounds the board's world pose and world scale are untouched. Only the WINDOW rides the rig.
    /// The five builds listed in NetProtocol's 2026-08-18 block failed by making the BOARD ride it.
    /// </item>
    /// </list>
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
    /// <para>Only the tray's OWN localScale is written, so the rig/world scale the player chose for
    /// the DIORAMA is never touched — the board simply stops following it past the limit. The write
    /// ANNOUNCES itself to the board-pose watchdog and to the pinned freeze sentinel, because a push
    /// is a sanctioned re-pose: silently changing the scale is precisely what those exist to catch.
    /// It is also what keeps MULTIPLAYER correct for free — the wire samples the board's WORLD scale
    /// (NetAvatarDriver: <c>board.lossyScale.x</c>), which is the quantity this method writes, so a
    /// peer reproduces the sender's pushed board at the sender's world size with no conversion and
    /// no new wire field. The bounds are and must stay LOCAL: they are each player's own eyes.</para>
    ///
    /// <para>THE PUSH NEVER REACHES THE CONFIG. It writes only the live localScale;
    /// <see cref="PersistPoseToConfig"/> runs on grab release alone and skips the size entirely
    /// unless the grab really resized the board (see the guard there). That is what keeps the
    /// hand-tuned <c>BoardScale_{board}</c> / <c>TrayScale</c> out of this feature's way.</para>
    /// </summary>
    private void ClampApparentSize()
    {
        if (_root == null || _handle == null)
            return;
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out float parent, out float rigScale))
            return;
        Vector3 local = _root.localScale;
        float width = perUnit * local.x;   // PLAYER metres — the unit the two config limits are in
        if (!(width > 1e-6f) || float.IsInfinity(width))
            return;
        float min = MinWidthMeters, max = MaxWidthMeters;
        float wanted = Mathf.Clamp(width, min, max);
        float factor = wanted / width;
        // Dead band: inside the window (or within the freeze sentinel's own float-noise band of it)
        // this is the identity, and the identity must not be written — a write would churn the
        // sentinel, the watchdog and the wire's "board is moving" test for nothing.
        if (Mathf.Abs(factor - 1f) <= ScaleNoiseEpsilon)
            return;

        _root.localScale = local * factor;
        _sizePushCount++;
        NotePinnedWrite("apparent-size push (ClampApparentSize — a moving bound walked into the board)");

        // Sanctioned: the watchdog must be able to tell a push apart from a game event moving the
        // board behind our back. Without this the write reads as an UNSANCTIONED recompute — which
        // is exactly how the shipped 2026-08-03 bug announced itself.
        CardsDriver.NoteExpectedPoseChange("board size push (min/max apparent width)");

        float now = Time.unscaledTime;
        // CHANGE-GATED, not merely throttled: parked against a bound at a steady zoom the push can
        // legitimately fire every frame with the same numbers, and a line a second forever is the
        // "probe that answered is spent" failure. The gate is the pair that makes the verdict.
        bool changed = Mathf.Abs(rigScale - _loggedPushRig) > _loggedPushRig * 0.01f + 1e-3f
                       || Mathf.Abs(_root.localScale.x - _loggedPushScale) > 1e-4f;
        if (!changed || now < _nextSizeClampLog)
            return;
        _nextSizeClampLog = now + 1f;
        _loggedPushRig = rigScale;
        _loggedPushScale = _root.localScale.x;
        bool low = wanted <= min;
        VRLog.Info("Cards", $"BOARD SIZE PUSHED by the {(low ? "MINIMUM" : "MAXIMUM")} " +
                            $"({(CardsConfig.TrayFollow.Value ? "FOLGEN" : "FIXIERT")}): apparent width " +
                            $"{width * 100f:F1} cm → {wanted * 100f:F1} cm against the window " +
                            $"{min * 100f:F0}–{max * 100f:F0} cm APPARENT ([Cards] BoardMinWidthMeters/" +
                            $"BoardMaxWidthMeters). Same board, same instant, the other unit: world width " +
                            $"{width * rigScale:F3} → {wanted * rigScale:F3} m-world against the same " +
                            $"window expressed in WORLD metres, {min * rigScale:F3}–{max * rigScale:F3} m " +
                            $"— that window is what moves, and it moves because it is divided by the LIVE " +
                            $"rig ×{rigScale:F2} (parent chain ×{parent:F2}, {perUnit * 100f:F2} cm " +
                            $"apparent per localScale unit). Own localScale {local.x:F3} → " +
                            $"{_root.localScale.x:F3}. Push #{_sizePushCount} this session. This is the " +
                            "2026-08-25 exception: the bound moved into the board and pushed it, the " +
                            "board does NOT spring back when the player reverses, and between the bounds " +
                            "nothing here writes at all.");
    }

    /// <summary>
    /// The BOARD ANCHOR line's defect wording, BYTE FOR BYTE what it has always been — the grep
    /// token every earlier hardware round was read with, and still the loudest thing this line can
    /// say, because an unexplained move on a FIXIERT board really is a defect.
    ///
    /// <para>WHAT CHANGED (2026-09-05) IS WHEN IT IS REACHED, not what it says. It used to be the
    /// else-branch of the SIZE PUSH alone, so it fired on every sanctioned pin carry: 8 times in
    /// that day's hardware log, every one of them legitimate — and from the arrival-seat guard
    /// onwards it would have fired on every arrival correction too, i.e. the instrument would have
    /// accused the fix of being the bug, in the same log, four lines under the line that explains
    /// the fix. An instrument that asserts a cause it cannot observe is a shape this project has
    /// paid for repeatedly. It now asks the freeze sentinel, which DOES observe it, and keeps this
    /// verdict only when nothing accounts for the move.</para>
    /// </summary>
    private const string DefectVerdict =
        "FIXIERT, NOT HELD, AND IT MOVED WITH NO PUSH — this is a defect. Something "
        + "wrote the board's world transform. The ruling is 'keinerlei Abhängigkeit "
        + "zum Spieler, fix in der Welt' outside the two Randfälle; grep the PINNED "
        + "tray transform WRITE line at this timestamp, it names the writer.";

    /// <summary>
    /// THE BOARD ANCHOR LINE, and it reports the invariant the user actually stated rather than the
    /// one four builds guessed at. User, 2026-08-18, verbatim: <i>"Fixiert heißt FIX. Keinerlei
    /// Abhängigkeit zum Spieler mehr, sondern fix in der Welt."</i>
    ///
    /// <para>SO THE THING TO PROVE IS A WORLD INVARIANT: while FIXIERT and not being actively
    /// grabbed or resized, the board's WORLD position and WORLD scale must not change by one bit,
    /// whatever the rig scale does. This line prints both, with the rig scale beside them, so a
    /// single grep answers it. Every earlier version of this diagnostic reported a PLAYER-frame
    /// quantity — apparent width (world size ÷ rig scale), then distance and subtended angle — and
    /// each of those is CONSTANT for a board that is riding the player, which is why the line agreed
    /// with four builds the user rejected. A diagnostic must measure the invariant that was asked
    /// for, not the one the implementation happens to hold.</para>
    ///
    /// <para>2026-08-25 — THE INVARIANT NOW HAS ONE SANCTIONED EXCEPTION AND THIS LINE ADJUDICATES
    /// IT. The size window moves with the player's scale, and on the frames it walks past the board
    /// <see cref="ClampApparentSize"/> pushes the board's world scale so the window is not violated.
    /// So "world scale changed while FIXIERT and not held" is no longer automatically a defect — it
    /// is a defect only if no push fired. The line therefore prints the push count SINCE THE LAST
    /// LINE next to the world-scale delta, and both units of the size with the window in each, so
    /// the next log answers "did the exception fire, and was that the thing that moved it" without
    /// a second grep and without the eye.</para>
    ///
    /// <para>2026-09-05 — "NO PUSH FIRED" WAS NEVER THE SAME QUESTION AS "NOTHING WROTE IT", and
    /// treating it as one made this line accuse eight legitimate pin carries of being a defect in
    /// one session. The push is only ONE of the sanctioned writers; the others move the board's
    /// world POSITION rather than its scale — the tracking-origin carry
    /// (<see cref="SyncPinHolder"/>), the arrival-seat correction
    /// (<see cref="TickArrivalSeatGuard"/>), the first placement, the lost-board recovery, a
    /// settings live-apply, a board-switch pose restore — and every one of them announces itself
    /// through <see cref="NotePinnedWrite"/>. The line no longer INFERS a writer from the absence
    /// of a push: it asks <see cref="TickPinnedFreezeSentinel"/>, which runs every frame and
    /// already measures the one thing that matters, namely whether an announcement was followed by
    /// a real pose change. Four outcomes are now distinguishable where two used to print the same
    /// sentence: convicted / explained / float noise / a real move nothing detected. The defect
    /// wording itself (<see cref="DefectVerdict"/>) is unchanged and still fires for the first and
    /// last of those.</para>
    /// </summary>
    private void TickBoardAnchorDiagnostics()
    {
        if (_root == null)
            return;
        float now = Time.unscaledTime;
        Vector3 worldPos = _root.position;
        float worldScale = _root.lossyScale.x;
        if (!IsFinite(worldPos) || !(worldScale > 1e-6f))
            return;

        bool follow = CardsConfig.TrayFollow.Value;
        bool held = _handle != null && _handle.IsGrabbed;
        bool haveBaseline = _anchorLogValid;
        float posDelta = haveBaseline ? Vector3.Distance(worldPos, _anchorLogPos) : 0f;
        float scaleRatio = haveBaseline ? worldScale / Mathf.Max(_anchorLogScale, 1e-6f) : 1f;
        // A world-frozen board moves by exactly zero. The thresholds are float noise, not tolerance:
        // 0.1 mm of world units and 0.05 % of scale (the same ScaleNoiseEpsilon the push's dead band
        // is derived from — one number, one meaning). Anything above them while FIXIERT and not held
        // is a WRITER, and unless the push accounts for it the line says so in those words.
        bool moved = haveBaseline && (posDelta > 1e-4f || Mathf.Abs(scaleRatio - 1f) > ScaleNoiseEpsilon);
        int pushesSince = _sizePushCount - _loggedAnchorPushCount;
        // …and the same shape for the two things the FREEZE SENTINEL observed since the last line:
        // moves a writer announced itself for, and moves nobody announced. Deltas, not totals, so
        // the verdict below is about THIS window (see the sentinel's own tally for why they are
        // observations rather than announcements).
        int sanctionedSince = _pinSanctionedMoveCount - _loggedAnchorSanctionedCount;
        int unknownSince = _pinUnknownMoveCount - _loggedAnchorUnknownCount;
        int blindSince = _pinBlindAnnouncementCount - _loggedAnchorBlindCount;
        if (!moved && now < _nextAnchorLog)
            return;
        _nextAnchorLog = now + 5f;

        Transform? rig = Rig.VRRigDriver.RigRoot;
        float rigScale = rig != null ? rig.lossyScale.x : 1f;
        // THE WINDOW, PRINTED IN BOTH UNITS AND EACH BESIDE THE VALUE IT BOUNDS. The config limits
        // are APPARENT metres, so the apparent width is compared against them directly; the same
        // window multiplied by the LIVE rig scale is the world-metre window, which is the one that
        // moves when the player scales. Since 2026-08-25 the divisor is the live rig in BOTH modes
        // (see TryGetApparentWidthPerScaleUnit), so per-unit is EXPECTED to move in FIXIERT — the
        // opposite of what this line demanded of it before, and printing the divisor keeps the
        // arithmetic checkable: perUnit × rigScale ÷ parent must always be BoardW = 0.64.
        string sizes;
        if (TryGetApparentWidthPerScaleUnit(out float perUnit, out float parent, out _, out float divisor))
        {
            float apparent = perUnit * _root.localScale.x;
            float world = apparent * rigScale;
            float min = MinWidthMeters, max = MaxWidthMeters;
            sizes = $"size {apparent * 100f:F1} cm APPARENT in a {min * 100f:F0}–{max * 100f:F0} cm " +
                    $"apparent window, = {world:F3} m-world in the SAME window seen in world metres, " +
                    $"{min * rigScale:F3}–{max * rigScale:F3} m (that one rides the rig — it is the " +
                    $"'erlaubtes Minimum' that grows when the player grows). Per unit " +
                    $"{perUnit * 100f:F2} cm (parent ×{parent:F2} ÷ divisor ×{divisor:F2}, the LIVE " +
                    $"rig in both modes since 2026-08-25)";
        }
        else
        {
            sizes = "size/limits measure unavailable";
        }
        string pushes = pushesSince > 0
            ? $"PUSH FIRED {pushesSince}× since the last line (total {_sizePushCount})"
            : "push did not fire since the last line";
        // WHO MOVED IT, in the same "since the last line" frame as the push term. The sentinel
        // runs every frame with a coarser position epsilon than this line's own 1e-4 (see
        // PinFreezePosEpsilon), so a delta below that band is float noise rather than a writer
        // nobody saw — the verdict below tells those two apart instead of calling both a defect.
        float sentinelEps = PinFreezePosEpsilon(worldScale);
        // BOTH TERMS, because this line trips on either: its own thresholds (1e-4 world units,
        // ScaleNoiseEpsilon = 5e-4 relative) are FINER than the sentinel's on both axes, so a
        // delta that lands between the two pairs is a difference of instruments and not a writer.
        // Testing only position would print a sentence about position for a scale-only case.
        bool belowSentinelBand = posDelta <= sentinelEps
                                 && Mathf.Abs(scaleRatio - 1f) <= PinFreezeScaleEpsilon;
        string writers = unknownSince > 0
            ? $"the freeze sentinel observed {unknownSince} move(s) with NO writer announcing itself"
              + (sanctionedSince > 0 ? $" and {sanctionedSince} sanctioned one(s)" : "")
            : sanctionedSince > 0
                ? $"the freeze sentinel observed {sanctionedSince} SANCTIONED move(s), most recently "
                  + $"[{_pinSanctionedMoveLast}]"
                : $"the freeze sentinel observed no move at all (its bands are "
                  + $"{sentinelEps * 1000f:F2} mm-world and {PinFreezeScaleEpsilon * 100f:F1} % of "
                  + $"scale, against this line's 0.10 mm-world and {ScaleNoiseEpsilon * 100f:F2} %)";
        // THE DEFECT WORDING IS UNCHANGED, BYTE FOR BYTE, and it is still the loudest thing this
        // line can say — the grep token every earlier hardware round was read with survives, and
        // an unexplained move on a FIXIERT board really is a defect. What changed (2026-09-05) is
        // WHEN it is reached. It used to be the else-branch of the SIZE PUSH alone, so it fired on
        // every sanctioned pin carry: 8 times in that day's log, every one of them legitimate, and
        // from this build on it would also have fired on every arrival-seat correction — i.e. the
        // instrument would have accused the fix of being the bug, in the same log, four lines under
        // the line that explains the fix. An instrument that asserts a cause it cannot observe is
        // the shape this project has paid for repeatedly. It now asks the sentinel, which DOES
        // observe it, and only keeps this verdict when nothing accounts for the move.
        // THE BRANCH ORDER IS THE VERDICT, so it is written as statements rather than as a
        // five-deep conditional expression: this used to be a two-way ternary and the whole point
        // of the change is that a reader can see, at a glance, which explanation outranks which.
        //   1. an unannounced writer was CONVICTED      -> the defect verdict, now with evidence
        //   2. the apparent-size push fired             -> the 2026-08-25 exception (unchanged)
        //   3. a sanctioned writer announced and moved  -> EXPECTED, and the writer is named
        //   4. the delta is under the sentinel's band   -> float noise between two instruments
        //   5. it really moved and nothing saw it       -> a DIFFERENT defect, and it says so
        string verdict;
        if (follow)
        {
            verdict = "FOLGEN — the board hangs off the player and is SUPPOSED to move with them.";
        }
        else if (held)
        {
            verdict = "FIXIERT, HELD — the hand is carrying it, so a change here is the player's own.";
        }
        else if (!moved)
        {
            verdict = "FIXIERT, NOT HELD — world pose and world scale are FROZEN, which is the "
                      + "invariant. The rig scale beside them is free to move and normally has.";
        }
        else if (unknownSince > 0)
        {
            // AN UNANNOUNCED WRITER WAS CONVICTED. The original verdict, unchanged, plus the count
            // that now stands behind it — this is no longer inferred from the absence of a push, it
            // is what the per-frame sentinel actually saw.
            verdict = DefectVerdict + $" The sentinel CONVICTED {unknownSince} unannounced "
                      + "write(s) in this window, so that claim is now an observation rather than "
                      + "an inference from 'no push fired'.";
        }
        else if (pushesSince > 0)
        {
            verdict = "FIXIERT, NOT HELD, AND THE SIZE MOVED — EXPECTED: this is the 2026-08-25 "
                      + "exception. A bound walked into the board and pushed it; the world POSITION "
                      + "must still be zero-delta, and the board must NOT come back down when the "
                      + "player reverses. Grep BOARD SIZE PUSHED at this timestamp for which bound.";
        }
        else if (sanctionedSince > 0)
        {
            // EXPLAINED. The pin carry through a tracking-origin change, the arrival-seat
            // correction, the first placement, the lost-board recovery, a settings live-apply, a
            // board-switch pose restore — every one announces itself through NotePinnedWrite, and
            // the sentinel saw the announcement land on a real move. Naming the writer is the
            // point: "explained" and "not detected" are different readings and used to print the
            // same sentence.
            verdict = "FIXIERT, NOT HELD, AND IT MOVED — EXPECTED: a SANCTIONED writer accounts "
                      + $"for it ({sanctionedSince} observed move(s) in this window, most recently "
                      + $"[{_pinSanctionedMoveLast}]). The freeze sentinel saw the writer announce "
                      + "itself and then saw the pose change, so this is the mod moving its own "
                      + "furniture on purpose, not a leftover writer. Grep the PINNED tray "
                      + "transform WRITE line at this timestamp for the full before/after.";
        }
        else if (belowSentinelBand)
        {
            // Under the sentinel's own noise band. Not a writer and not a defect: the two epsilons
            // differ on purpose (this line is the finer of the two) and saying so beats a defect
            // verdict on half a millimetre.
            verdict = "FIXIERT, NOT HELD — the world pose and size both moved by less than the "
                      + $"freeze sentinel's own bands ({sentinelEps * 1000f:F2} mm-world and "
                      + $"{PinFreezeScaleEpsilon * 100f:F1} % of scale, against this line's "
                      + $"0.10 mm-world and {ScaleNoiseEpsilon * 100f:F2} %), so it is float noise "
                      + "between two differently-fine instruments, not a writer. The invariant "
                      + "holds.";
        }
        else if (blindSince > 0)
        {
            // NAMED, BUT UNADJUDICATED. The sentinel does not run while the tray is hidden or its
            // placement is still deferred (TickLostWatchdog's own early-out), which is precisely
            // the state a scenario's FIRST PLACEMENT happens in — so the writer announced itself
            // into a pass with no baseline. That is not "nothing was detected": the announcement
            // names the writer, this line's own two samples measure the move, and between them
            // the change is fully accounted for. It is deliberately NOT the defect wording.
            verdict = "FIXIERT, NOT HELD, AND IT MOVED — EXPECTED, BUT THE SENTINEL WAS BLIND FOR "
                      + $"IT: {blindSince} sanctioned write(s) announced themselves while it had "
                      + "no baseline to compare against (the tray was hidden or its placement was "
                      + "still deferred, i.e. the state a scenario's first placement happens in), "
                      + $"most recently [{_pinBlindAnnouncementLast}]. The writer is NAMED, the "
                      + "move is this line's own measurement, and there will be no PINNED tray "
                      + "transform WRITE line to grep because the sentinel could not adjudicate "
                      + "it. Grep BOARD ARRIVAL SIZE at this timestamp for what the size was "
                      + "solved from.";
        }
        else
        {
            // A REAL MOVE THAT NOTHING SAW. Distinct from a convicted writer and worth its own
            // words: the per-frame sentinel should have caught this and did not, so the change
            // happened somewhere it cannot look.
            verdict = DefectVerdict + " NOTHING WAS DETECTED WRITING IT: the per-frame freeze "
                      + "sentinel observed neither a sanctioned nor an unannounced move in this "
                      + "window, so the change happened where it cannot look — while the tray was "
                      + "hidden, or across a rebuild that dropped its baseline. That is a DIFFERENT "
                      + "defect from a convicted writer and there will be no PINNED tray transform "
                      + "WRITE line to grep.";
        }

        _anchorLogPos = worldPos;
        _anchorLogScale = worldScale;
        _anchorLogValid = true;
        _loggedAnchorPushCount = _sizePushCount;
        _loggedAnchorSanctionedCount = _pinSanctionedMoveCount;
        _loggedAnchorUnknownCount = _pinUnknownMoveCount;
        _loggedAnchorBlindCount = _pinBlindAnnouncementCount;

        VRLog.Info("Cards", $"BOARD ANCHOR: world pos {worldPos}, world scale {worldScale:F3} " +
                            $"(own localScale {_root.localScale.x:F3}) against rig ×{rigScale:F2}. " +
                            (haveBaseline
                                ? $"Since the last line: Δ world pos {posDelta * 1000f:F2} mm-world, " +
                                  $"world scale ×{scaleRatio:F5}, {pushes}; {writers}. "
                                : $"first sample, {pushes}; {writers}. ") +
                            $"{sizes}. {verdict} " +
                            "The board's PERCEIVED size is deliberately NOT frozen between the " +
                            "bounds: a pinned board is world geometry, so zooming changes how big it " +
                            "looks exactly as it changes how big the dungeon looks. What IS " +
                            "guaranteed is that it never looks smaller than the min or bigger than " +
                            "the max — that is the only thing the push exists for.");
    }

    private Vector3 _anchorLogPos;
    private float _anchorLogScale;
    private bool _anchorLogValid;
    private float _nextAnchorLog;

    /// <summary>Values of <see cref="_pinSanctionedMoveCount"/> and
    /// <see cref="_pinUnknownMoveCount"/> the last BOARD ANCHOR line reported, so the next one
    /// states what the freeze sentinel observed BETWEEN the two samples rather than since boot —
    /// the same delta discipline the push count below already uses.</summary>
    private int _loggedAnchorSanctionedCount;
    private int _loggedAnchorUnknownCount;

    /// <summary>Value of <see cref="_sizePushCount"/> the last BOARD ANCHOR line reported, so the
    /// next one can state how many pushes happened between the two samples.</summary>
    private int _loggedAnchorPushCount;

    /// <summary>Value of <see cref="_pinBlindAnnouncementCount"/> the last BOARD ANCHOR line
    /// reported — the same delta discipline as the two counters above it.</summary>
    private int _loggedAnchorBlindCount;

    /// <summary>
    /// The tray's apparent width (PLAYER metres — the frame <c>[Cards] BoardMinWidthMeters</c> /
    /// <c>BoardMaxWidthMeters</c> are written in) PER UNIT of the tray's own localScale. It is the
    /// shared measure of the frame-by-frame push (<see cref="ClampApparentSize"/>) and of the live
    /// two-hand gesture window (<see cref="WorldUI.IPanelGrabOwner.GrabScaleLimits"/>). One
    /// expression on purpose: the two enforcement points MUST agree, or a size the gesture allows
    /// would be snapped back the frame the player lets go (the exact "es wird sofort wieder kleiner"
    /// failure the corrected measure fixed on 2026-08-03).
    ///
    /// <para>Since 2026-08-25 the divisor is the LIVE RIG SCALE IN BOTH ANCHOR MODES, which is the
    /// user's reversal of the 2026-08-18 frozen-divisor rule and the whole of "das Maximum und
    /// Minimum … verschiebt sich dynamisch mit der Größe mit" — see the block inside the method for
    /// the arithmetic and for why that, and only that, makes the allowed MINIMUM grow when the
    /// player grows.</para>
    ///
    /// <para>False when no board exists or a transform is degenerate — callers then skip their
    /// clamp (push) or fall back to the generic factor range (gesture window).
    /// <paramref name="parent"/>/<paramref name="rigScale"/>/<c>divisor</c> are
    /// surfaced for the diagnostic lines only; <c>divisor</c> is now always equal to
    /// <paramref name="rigScale"/> and is kept as a separate out-parameter so the BOARD ANCHOR line
    /// keeps printing the quotient it has printed since ModBuild 162 — a future change that
    /// re-freezes it would show up in that log instead of silently.</para>
    /// </summary>
    private bool TryGetApparentWidthPerScaleUnit(out float perUnit, out float parent, out float rigScale) =>
        TryGetApparentWidthPerScaleUnit(out perUnit, out parent, out rigScale, out _);

    private bool TryGetApparentWidthPerScaleUnit(
        out float perUnit, out float parent, out float rigScale, out float divisor)
    {
        perUnit = 0f;
        parent = 1f;
        rigScale = 1f;
        divisor = 1f;
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
        // NO RIG, NO ANSWER — and this is a HARD FAIL rather than the "world units ARE player
        // units" fallback that stood here (2026-08-25). That fallback was harmless while the only
        // consumer clamped a FOLLOW-mode board whose parent chain is the rig itself; it is not
        // harmless now. A pinned board hangs under a holder carrying the diorama scale (×13.10 in
        // the ModBuild 267 log), so pretending rigScale is 1 for a frame reports its 43 cm board as
        // a 6.3 m one and the push — which is PERMANENT, it never springs back — would shrink it
        // 4.5× on the spot and leave it there. Returning false instead makes the push skip the
        // frame and the gesture window fall back to the handle's generic factor range, both of
        // which are already the documented "measure unavailable" behaviour.
        if (rig == null)
            return false;
        rigScale = rig.lossyScale.x;
        if (!(rigScale > 1e-6f) || float.IsInfinity(rigScale))
            return false;

        // THE DIVISOR IS THE LIVE RIG, IN BOTH MODES. User ruling 2026-08-25, and it REVERSES the
        // 2026-08-18 "Fixiert heißt FIX" reading of this one line (he opened with "Ich will nun
        // doch", so the older rule is not an argument against it):
        //
        //   "Ich will nun doch, dass es sich dynamisch mit der Größe mitverschiebt … Damit meine
        //    ich nur die Randfälle, dass ich zB ein super kleines board hab und ich mach mich
        //    größer und größer — damit wächst jetzt nun auch das erlaubte minimum der Größe vom
        //    board."
        //
        // FIXIERT STILL MEANS THE BOARD IS WORLD-FROZEN; what he changed his mind about is what the
        // MIN/MAX are measured in. BoardMinWidthMeters/BoardMaxWidthMeters are written in PLAYER
        // metres ("narrowest the board is allowed to LOOK"), so the divisor that turns a world width
        // into that unit is the LIVE rig scale and nothing else — at rig scale 21 the player IS
        // twenty-one times larger, so a world metre is 1/21 of a perceived metre. Dividing by the
        // pin holder instead (the rig scale AS IT WAS AT PIN TIME) answered a different question —
        // "how wide did this board look when I pinned it" — which is a constant and therefore
        // cannot grow when the player grows. His example only comes out right with the live rig:
        //
        //     bound expressed in localScale = limitMetres × rigScale / (BoardW × parent)
        //
        // — PROPORTIONAL TO THE RIG SCALE, so the player scaling UP raises the allowed minimum,
        // which is exactly the sentence above. That direction falls out of this line, not out of a
        // paraphrase of it; it is also why the correction in ClampApparentSize can only ever be a
        // PUSH (the window walks into the board, never away from it).
        //
        // THE BOARD ITSELF DOES NOT RIDE THE DIVISOR — ONLY THE WINDOW DOES. ClampApparentSize
        // moves the board solely on the frame this window has crossed it; "ansonsten bleibt es fix".
        //
        // FOLLOW mode is unaffected in every arithmetic detail: there the tray hangs under the rig
        // anchor, so `parent` IS the live rig scale and this division cancels it exactly as it did
        // before (hardware log, ModBuild 267: "parent ×13.10 ÷ divisor ×13.10", perUnit a constant
        // 64.00 cm). The 2026-08-03 "extrem winzig" shrink-onto-shrink multiplication stays closed
        // off there for the same reason it always was.
        divisor = rigScale;

        perUnit = BoardHalfWidthLocal * 2f * parent / divisor;
        return perUnit > 1e-6f && !float.IsInfinity(perUnit);
    }

    // ------------------------------------------------------- ARRIVAL SIZE (2026-09-05 report) --
    //
    // THE REPORT (user, verbatim): "Das Board spawnt jetzt viel zu gross! Es soll eine normale
    // angemessene Groesse haben, die zum aktuellen Zoomfaktor passt, mit dem man spawned."
    //
    // WHAT THE HARDWARE LOG SAYS, AND IT IS NOT THE ARRIVAL SEAT GUARD. In the ModBuild 434 log
    // the guard gave three verdicts and made ZERO corrections ("BESIDE THE PLAYER - nothing to
    // correct", lines 931/1363/1455), and ReseatBesidePlayer restores the live localScale over
    // whatever PlaceAtHead re-derived anyway. The writer is PlaceAtHead's ordinary first
    // placement, at line 920: "Control board placed (Steel: ... scale 2.17x) - FIRST SEAT". It
    // solved against ComputeBoardScale = ClampedTrayScale x BoardScale_Steel, and the log names
    // both terms four lines later: "the storable band TrayScale 0.25-4 x BoardScale_Steel
    // 0.54265 = 0.136-2.171" - i.e. TrayScale was sitting at its MAXIMUM, 4, and the product
    // 2.17060 is the band's own upper end. The BOARD ANCHOR line measured exactly that jump
    // (x2.17060, localScale 1.000 -> 2.171) and 138.9 cm apparent in an 18-140 cm window: the
    // board spawned at 99.2 % of the largest size it is allowed to be.
    //
    // WHY TrayScale WAS PINNED AT 4. It is written by PersistPoseToConfig as
    // `live localScale / BoardScale`, CLAMPED to the storable band. A localScale is a WORLD-frame
    // number: what it looks like depends on the parent chain divided by the live rig scale at the
    // moment it is used. A size dialled in FIXIERT after a deep zoom needs localScale 5-40 (the
    // ModBuild 351 measurement quoted in LogResizeWindow), the band tops out at 2.171, so the
    // clamp fires and the file keeps a number that means 138.9 cm the next time the two frames
    // agree. PersistPoseToConfig has always SAID so in its own HW-VERIFY line ("the size that
    // holds your 18-140 cm window there is a WORLD size and rides the rig, while this band does
    // not"); this is that cost, arriving.
    //
    // THE FIX IS THE UNIT, and it is the user's own sentence: a size must be stated in the frame
    // it is SEEN in. So the board's size at a placement is solved as an APPARENT WIDTH in real
    // metres and only then divided by the live apparent-per-unit to get a localScale - the same
    // measure ClampApparentSize and the two-hand gesture window already share, so all three agree
    // by construction. Two sources feed it, in this order:
    //
    //   1. HIS OWN SIZE, when he has authored one. [Cards] BoardApparentWidth_{board} is written
    //      by the two-hand resize in apparent metres, which is LOSSLESS: the gesture window
    //      already bounds it to BoardMin/BoardMaxWidthMeters, so unlike TrayScale it can never be
    //      clamped into meaning something else. Re-solved against the LIVE per-unit, so it comes
    //      back looking the size he made it at whatever zoom he spawns at.
    //   2. THE SEAT-DERIVED DEFAULT otherwise, CardsConfig.SeatBoardWidthMeters - the width that
    //      subtends [Cards] SpawnBoardWidthDegrees at the distance the first seat already puts the
    //      board at. It reads Spawn{Side,Forward,Down}Meters VERBATIM and moves nothing.
    //
    // A CONFIG WRITTEN BY AN OLDER BUILD HAS NO RECORDED APPARENT WIDTH, so it takes (2) - which
    // is the whole of this report, because 2.171 is exactly such a value and there is no way to
    // convert it: the clamp destroyed the information, it did not merely re-frame it. The first
    // resize after this build records the width and (1) takes over for good.
    //
    // WHAT IS DELIBERATELY NOT TOUCHED. TrayScale and BoardScale_{board} are still written by the
    // gesture exactly as before and still mean exactly what they meant, so the settings window's
    // own size range, LogResizeWindow's storable-band line and PersistPoseToConfig's
    // "outside what the config can store" verdict are all unmoved.
    //
    // AND ONE ASYMMETRY, STATED RATHER THAN DISCOVERED LATER. ReapplyOrientation's PINNED branch
    // still writes ComputeBoardScale verbatim: that path exists to show the player the dial he is
    // dragging RIGHT NOW (BoardScale_{board} from the debug menu, TrayScale from the settings
    // window), and routing it through the solve would make those dials inert. Its FOLLOW branch
    // re-places through PlaceAtHead — pre-existing structure, unchanged — so in FOLLOW mode a
    // settings live-apply takes the solve like any other placement. The consequence, in the only
    // terms that matter on hardware: with a recorded apparent width, dragging the size dial moves
    // the FIXIERT board immediately and the next placement puts it back at the recorded width;
    // the way to change the size for good is the two-hand gesture, which is what records it.

    /// <summary>
    /// The board's own localScale for a placement, solved so the board LOOKS the intended size at
    /// the zoom the player is standing at. See the block above. <paramref name="sizeSource"/> is
    /// the field the hardware round is decided on: which of the three answers was in force.
    /// False when no apparent measure exists this frame (no rig, degenerate transform) - the
    /// caller then falls back to <c>ComputeBoardScale</c>, which is what every build before this
    /// one did unconditionally, and <paramref name="localScale"/> already holds it.
    /// </summary>
    private bool TrySolveBoardScale(ControlBoard board, out float localScale,
                                    out string sizeSource, out float targetApparent)
    {
        localScale = ComputeBoardScale(board);
        targetApparent = 0f;
        sizeSource = "A CARRIED-OVER config product (no apparent measure this frame, so this is "
                     + "the pre-2026-09-05 behaviour verbatim)";
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out _, out _))
            return false;

        BepInEx.Configuration.ConfigEntry<float>? authoredEntry = CardsConfig.BoardApparentWidthMeters(board);
        float authored = authoredEntry != null ? authoredEntry.Value : 0f;
        bool ownSetting = authored > 1e-3f;
        float wanted = ownSetting ? authored : CardsConfig.SeatBoardWidthMeters;
        targetApparent = Mathf.Clamp(wanted, MinWidthMeters, MaxWidthMeters);
        localScale = targetApparent / perUnit;
        sizeSource = ownSetting
            ? $"THE PLAYER'S OWN SETTING ([Cards] BoardApparentWidth_{board} = "
              + $"{authored * 100f:F1} cm, the width the two-hand resize last authored, re-solved "
              + "against this zoom's per-unit)"
            : $"AN ARRIVAL SOLVE (seat-derived: {SeatWidthDegrees:F0}deg at "
              + $"{CardsConfig.SeatDistanceMeters:F2} m = "
              + $"{CardsConfig.SeatBoardWidthMeters * 100f:F1} cm apparent; "
              + $"[Cards] BoardApparentWidth_{board} is 0, i.e. no size of his is on record)";
        return true;
    }

    /// <summary>The arrival-size comfort dial, with the shipped default as the pre-bind
    /// fallback so a placement that somehow runs before the config is bound still reports the
    /// number it actually used.</summary>
    private static float SeatWidthDegrees => CardsConfig.SpawnBoardWidthDegrees != null
        ? CardsConfig.SpawnBoardWidthDegrees.Value
        : Defaults.SpawnBoardWidthDegrees;

    /// <summary>Placement size solves this session - the liveness field on the BOARD ARRIVAL SIZE
    /// line, so "the solver never ran" and "it ran and chose the carried-over product" are
    /// different readings rather than the same absent line.</summary>
    private int _boardSizeSolveCount;

    /// <summary>
    /// Record the apparent width a real two-hand RESIZE just authored, in the unit the player saw
    /// it in. Called from <see cref="PersistPoseToConfig"/> beside the TrayScale write and under
    /// exactly the same "this was a resize, not a carry" guard, so a carry can never re-author a
    /// size he did not set. Returns the width written, or -1 when there was no apparent measure to
    /// take (the stored value is then left alone rather than zeroed - a missing measure is not
    /// evidence that he never sized it).
    /// </summary>
    private float RecordAuthoredApparentWidth(ControlBoard board)
    {
        if (_root == null || !TryGetApparentWidthPerScaleUnit(out float perUnit, out _, out _))
            return -1f;
        BepInEx.Configuration.ConfigEntry<float>? entry = CardsConfig.BoardApparentWidthMeters(board);
        if (entry == null)
            return -1f;
        float apparent = perUnit * _root.localScale.x;
        if (!(apparent > 1e-4f) || float.IsInfinity(apparent))
            return -1f;
        entry.Value = Mathf.Clamp(apparent, 0f, 4f); // the entry's own storage range
        return entry.Value;
    }

    // ------------------------------------------------------------- ARRIVAL SEAT GUARD --
    //
    // THE REPORT (user, 2026-09-05, verbatim): "Ich hatte beim Test den Fall, dass mein
    // Controlboard hinter dem Spielfeld gespawnt ist und ich es erst suchen musste. Das
    // Controlboard muss zwingend immer neben einem spawnen, niemals weiter weg."
    //
    // WHAT THE HARDWARE LOG SAYS, AND IT IS NOT THE PLACEMENT MATHS (LogOutput.log, ModBuild
    // 430-433 era, three scenario starts at lines 785 / 5980 / 11282). The line
    //   "[Cards] Control board placed (... ) - FIRST SEAT: fixed spot beside the head on the LEFT"
    // fires EXACTLY ONCE in the whole session, at line 896, in the FIRST scenario. The second and
    // third scenarios never re-seat the tray at all. The reason is structural: the seat is armed by
    // `_everPlaced`, which is cleared only in PlayTray.Destroy - i.e. when the HANDS ROOT goes
    // away, not when the player arrives at a scenario. The tray instance and its root survive a
    // scenario change, so "beim ersten Spawnen" quietly meant "the first scenario of the session".
    //
    // WHAT HAPPENED INSTEAD is that SyncPinHolder faithfully carried the board by its RIG-RELATIVE
    // pose across every origin change (7 carries in that session, each 24-42 m of world
    // displacement and a 138-176 degree flip). That carry is correct and stays: it is what keeps a
    // board the player has arranged where the player put it. But what it preserves is the offset
    // the PREVIOUS scenario ended with - and in scenario 3 that offset put the board 28.55 world
    // units from the head (2.05 m at rig scale 13.94), out past the far edge of the play field,
    // drifting to 41.08 wu / 2.95 m before the player found it and grabbed it back by hand
    // (Board pose [user-grab] at line 12084). That is the report, in numbers.
    //
    // THE ORDERING IS THE OTHER HALF. In that same log the recenter and every carry run BEFORE the
    // board footprint is even measurable and BEFORE the ring seats the player:
    //   S3: rig built 11282 -> carry 11323 -> recentered 11348 -> board measurable 11512 ->
    //       ring SEATED 11562 -> carry 11564.
    // A seat authored at any stage before the last of those is authored against a rig pose the
    // arrival is about to abandon. So the invariant this guard holds is stated on the OUTCOME, not
    // on any one stage:
    //
    //   AT EVERY STAGE OF A SCENARIO ARRIVAL THAT MOVES THE PLAYER WITHOUT THE PLAYER ASKING, AND
    //   AT THE END OF THE ARRIVAL, THE CONTROL BOARD IS WITHIN [Cards] SpawnMaxReachMeters OF THE
    //   HEAD AND WITHIN [Cards] SpawnMaxBearingDegrees OF THE PLAYER'S FORWARD.
    //
    // WHY THIS IS NOT THE FORBIDDEN RECALL. The block at the top of this file forbids re-adding a
    // distance- or visibility-based recall, on a ruling this guard obeys literally: it NEVER writes
    // on a frame where the rig origin is unchanged. Walking away from a FIXIERT board, reading a
    // menu next to it, flying off with the stick - none of those bump VRRigDriver.RigPoseVersion,
    // so none of them can reach the correction. The only writes are on the stages where the MOD
    // teleported the player (the arrival itself, the ring's join seat, its one allowed correction,
    // the first-pose recenter), and only while VRRigDriver.ScenarioArrivalPending is true, and only
    // until the player's own hand touches the board. Outside that window the guard is a READING and
    // nothing else.
    //
    // MULTIPLAYER: nothing on the wire, and nothing needs to be. Every input is local - this
    // client's own head camera, its own rig scale, its own board transform, its own rig pose
    // version. The JOIN case is covered for free and is the reason the arrival window is keyed on
    // the ring rather than on a timer: when the local NetworkPlayer has no id yet the ring reports
    // PeersUnknown and may seat the player LATE, or spend its one correction seconds later when a
    // peer's first pose finally lands - both of those are RigPoseVersion bumps inside the window,
    // so the board is re-verified against each of them. The board's world pose is already sampled
    // onto the wire by NetAvatarDriver.TickExtrasSend, so peers see the corrected seat with no new
    // field; and this is a purely LOCAL seating decision about the local player's own furniture,
    // which is why it needs none (there is no per-sub-feature sync setting - [Net] RemoteBoards
    // already decides whether peers draw the thing at all).

    /// <summary>Hard cap on how many verdicts one arrival may spend. The window holds an arrival
    /// seat, at most one ring correction and a recenter, so three or four is the real number; the
    /// cap is defence in depth against a rig that bumps its pose version in a loop, and it bounds
    /// the log at the same time.</summary>
    private const int MaxArrivalChecks = 8;

    /// <summary>True while a scenario arrival is being watched — armed on the rising edge of
    /// <see cref="VRRigDriver.ScenarioArrivalPending"/>, disarmed by the terminal verdict, by the
    /// player's own hand, or by the check cap.</summary>
    private bool _arrivalArmed;

    /// <summary>Last observed value of <see cref="VRRigDriver.ScenarioArrivalPending"/> — the edge
    /// detector, so one arrival arms the guard exactly once and the NEXT arrival re-arms it.</summary>
    private bool _arrivalPendingSeen;

    /// <summary><see cref="VRRigDriver.RigPoseVersion"/> the last verdict was taken at, and whether
    /// one has been taken at all. The first verdict of an arrival is deliberately unconditional:
    /// the pose the board carries into a new scenario was authored in the previous one.</summary>
    private bool _arrivalHaveVersion;
    private int _arrivalVersion;

    /// <summary>Verdicts and corrections spent this arrival — both printed on every line, so
    /// "the guard never ran" and "the guard ran and found nothing" are different readings.</summary>
    private int _arrivalChecks;
    private int _arrivalCorrections;

    /// <summary>Clear the whole guard for a fresh root (called from <see cref="Destroy"/>). The
    /// PlayTray INSTANCE outlives its root, so a stale "already checked" would skip an arrival.</summary>
    private void ResetArrivalSeatGuard()
    {
        _arrivalArmed = false;
        _arrivalPendingSeen = false;
        _arrivalHaveVersion = false;
        _arrivalVersion = 0;
        _arrivalChecks = 0;
        _arrivalCorrections = 0;
    }

    /// <summary>
    /// Arm on the rising edge of the rig's arrival window. Deliberately separate from, and ahead
    /// of, <see cref="TickArrivalSeatGuard"/>'s judging half: the window opens during the scenario
    /// load, when the tray may still be hidden with its placement deferred, and an edge that is
    /// only looked for once the tray is placed is an edge that can be missed entirely. Two field
    /// reads and a bool compare — allocation-free, and the whole cost on a steady frame.
    /// </summary>
    private void ObserveArrivalWindow()
    {
        bool pending = VRRigDriver.ScenarioArrivalPending;
        if (pending && !_arrivalPendingSeen)
        {
            _arrivalArmed = true;
            _arrivalHaveVersion = false;
            _arrivalChecks = 0;
            _arrivalCorrections = 0;
        }
        _arrivalPendingSeen = pending;
    }

    /// <summary>
    /// The judging half. Runs after <see cref="SyncPinHolder"/> (see the call site for why that
    /// order is load-bearing) and writes ONLY on a frame where the rig origin changed — see the
    /// block above for why that single rule is what keeps this out of the recall the standing
    /// ruling forbids. Allocation-free until it actually has a verdict to give.
    /// </summary>
    private void TickArrivalSeatGuard()
    {
        if (!_arrivalArmed || _root == null || !_placed)
            return;

        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return; // no head to measure against — spend nothing, decide nothing, retry next frame

        // THE PLAYER'S OWN HAND OUTRANKS THE GUARD, permanently. A board being carried is being
        // placed, and the ruling this file opens with says that pose is theirs from then on.
        if (_handle != null && _handle.IsGrabbed)
        {
            EvaluateArrivalSeat(head, VRRigDriver.RigPoseVersion,
                                "the player took the board in their own hand", mayCorrect: false);
            _arrivalArmed = false;
            return;
        }

        int version = VRRigDriver.RigPoseVersion;
        bool originChanged = !_arrivalHaveVersion || version != _arrivalVersion;
        if (!originChanged)
        {
            if (VRRigDriver.ScenarioArrivalPending)
                return; // the arrival is still running and nothing has moved — nothing to say
            // THE TERMINAL VERDICT, and it is a READING, never a write: the origin is stable, so
            // whatever the board's distance is now is a consequence of where the PLAYER went.
            EvaluateArrivalSeat(head, version,
                                "the arrival has settled — the rig will not move the player again",
                                mayCorrect: false);
            _arrivalArmed = false;
            return;
        }

        _arrivalHaveVersion = true;
        _arrivalVersion = version;
        EvaluateArrivalSeat(head, version,
                            _arrivalChecks == 0
                                ? "the scenario arrival (first verdict — the board carries the pose "
                                  + "the PREVIOUS scenario left it at)"
                                : "the rig moved the player (spawn-ring seat, its one correction, or "
                                  + "the first-pose recenter)",
                            mayCorrect: true);
        if (_arrivalChecks >= MaxArrivalChecks)
            _arrivalArmed = false;
    }

    /// <summary>
    /// One verdict: measure head to board, decide, correct when allowed, and say all of it in a
    /// single line. Split out of <see cref="TickArrivalSeatGuard"/> so the log call does not sit
    /// inside a method a reader (or scripts/check-hw-verify.py) would take for per-frame chatter —
    /// it fires at most <see cref="MaxArrivalChecks"/> times per scenario arrival.
    /// </summary>
    private void EvaluateArrivalSeat(Camera head, int version, string stage, bool mayCorrect)
    {
        if (_root == null)
            return;
        if (CardsConfig.SpawnMaxReachMeters == null || CardsConfig.SpawnMaxBearingDegrees == null)
            return; // config not bound yet (no scenario can have started) — nothing is spent

        float maxReach = CardsConfig.SpawnMaxReachMeters.Value;
        float maxBearing = CardsConfig.SpawnMaxBearingDegrees.Value;

        // MEASURE FIRST, SPEND SECOND: a verdict that could not be taken (a non-finite pose, which
        // is the lost-board watchdog's business) must not consume one of the arrival's few checks.
        if (!TryMeasureArrivalSeat(head, out float outWorld, out float outMeters, out float upMeters,
                                   out float bearing, out float rigScale))
            return;

        _arrivalChecks++;

        bool tooFar = outMeters > maxReach;
        bool behind = Mathf.Abs(bearing) > maxBearing;

        // THE DIALS MUST NOT FIGHT EACH OTHER. The first seat is itself authored from
        // [Cards] Spawn{Side,Forward,Down}Meters, so a player who widens those past the guard's own
        // bounds would otherwise be corrected to a seat that fails the very next verdict. Their
        // dials win, and the line says so rather than looping.
        Vector3 seat = CardsConfig.SpawnSeatOffset;
        var seatFlat = new Vector3(seat.x, 0f, seat.z);
        float seatReach = seatFlat.magnitude;
        float seatBearing = Mathf.Abs(Vector3.SignedAngle(Vector3.forward, seatFlat, Vector3.up));
        bool seatOutsideItsOwnBounds = seatReach > maxReach || seatBearing > maxBearing;

        string verdict;
        string after = string.Empty;
        if (!tooFar && !behind)
        {
            verdict = "BESIDE THE PLAYER — nothing to correct";
        }
        else
        {
            string wrong = tooFar && behind ? "OUT OF REACH AND BEHIND THE PLAYER"
                         : tooFar ? "OUT OF REACH"
                         : "BEHIND THE PLAYER";
            if (!mayCorrect)
            {
                verdict = wrong + " — NOT corrected: " + stage + ", so this is a reading only. "
                        + "The board is only ever moved on a frame where the rig itself moved the "
                        + "player, which is what keeps this out of the automatic recall the "
                        + "2026-08-03 ruling removed";
            }
            else if (seatOutsideItsOwnBounds)
            {
                verdict = wrong + " — NOT corrected: the configured first seat is itself "
                        + $"{seatReach:F2} m out at {seatBearing:F0}deg ([Cards] Spawn"
                        + "SideMeters/SpawnForwardMeters), which these bounds would reject too. "
                        + "The dials are the player's; widen the bounds or narrow the seat";
            }
            else
            {
                ReseatBesidePlayer(stage);
                _arrivalCorrections++;
                verdict = wrong + " — RE-SEATED at the configured first seat";
                if (TryMeasureArrivalSeat(head, out float w2, out float m2, out float u2,
                                          out float b2, out _))
                    after = $" AFTER the correction: {w2:F2} world units / {m2:F2} m out, "
                          + $"{b2:F0}deg off forward, {u2:F2} m vertical.";
            }
        }

        // HW-VERIFY: THE LINE THAT DECIDES THE 2026-09-05 REPORT ("das Controlboard muss zwingend
        // immer neben einem spawnen"). It fires on every verdict, including the ones that change
        // nothing, so "the guard never ran" (no line at all in a scenario) and "the guard ran and
        // found the board where it belongs" are different readings. If a hardware round still
        // reports hunting for the board, either this line is absent — read the [Rig] "Spawn ring:
        // window closed" line for whether an arrival was ever declared — or it is present with a
        // verdict, and the verdict names the stage and the numbers it was taken on.
        VRLog.Note("Cards", $"CONTROL BOARD ARRIVAL SEAT: verdict #{_arrivalChecks} of at most " +
                            $"{MaxArrivalChecks}, stage: {stage}. Board at {_root.position}, " +
                            $"{outWorld:F2} world units / {outMeters:F2} m from the head at rig " +
                            $"scale x{rigScale:F2} (the same distance in both units — the rig scale " +
                            $"is the only conversion), bearing {bearing:F0}deg off the player's " +
                            $"forward (0 = dead ahead, + = right, 180 = directly behind), " +
                            $"{upMeters:F2} m vertically. Bounds {maxReach:F2} m " +
                            $"([Cards] SpawnMaxReachMeters) and {maxBearing:F0}deg " +
                            $"([Cards] SpawnMaxBearingDegrees); the configured first seat is " +
                            $"{seatReach:F2} m out at {seatBearing:F0}deg. VERDICT: {verdict}." +
                            after +
                            $" Corrections this arrival {_arrivalCorrections}; mode " +
                            $"{(CardsConfig.TrayFollow.Value ? "FOLGEN" : "FIXIERT")}; rig pose " +
                            $"version {version}; arrival window " +
                            $"{(VRRigDriver.ScenarioArrivalPending ? "OPEN" : "CLOSED")}.");
    }

    /// <summary>
    /// Head-to-board geometry in BOTH units, because a bound named …Meters compared against a
    /// world-unit product is a mistake this project has already shipped: <paramref name="outWorld"/>
    /// is raw world units, <paramref name="outMeters"/> is that divided by the LIVE rig scale, and
    /// the rig scale is handed back so the arithmetic in the log line is checkable. Horizontal
    /// distance and bearing are taken in the flat world frame, matching <see cref="PlaceAtHead"/>'s
    /// own frame exactly (world, yaw-only). False when the pose is not measurable.
    /// </summary>
    private bool TryMeasureArrivalSeat(Camera head, out float outWorld, out float outMeters,
                                       out float upMeters, out float bearing, out float rigScale)
    {
        outWorld = outMeters = upMeters = bearing = 0f;
        rigScale = 1f;
        if (_root == null)
            return false;

        Transform? rigRoot = VRRigDriver.RigRoot;
        rigScale = rigRoot != null ? Mathf.Max(1e-4f, rigRoot.lossyScale.x) : 1f;

        Transform headT = head.transform;
        Vector3 delta = _root.position - headT.position;
        if (!IsFinite(delta))
            return false; // a non-finite pose is the lost-board watchdog's verdict, not this one

        var flat = new Vector3(delta.x, 0f, delta.z);
        outWorld = flat.magnitude;
        outMeters = outWorld / rigScale;
        upMeters = delta.y / rigScale;

        Vector3 flatForward = headT.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-4f)
            flatForward = Vector3.forward;
        bearing = outWorld > 1e-4f ? Vector3.SignedAngle(flatForward, flat, Vector3.up) : 0f;
        return true;
    }

    /// <summary>
    /// The correction: put the board back at the seat the player configured, keeping the size they
    /// dialled in (the "SIZE IS NOT PART OF A RECALL" ruling that <see cref="RecoverLostBoard"/>
    /// obeys applies verbatim here). It re-authors the pin against the CURRENT tracking origin and
    /// refreshes the rig-local cache in the same breath, so the very next
    /// <see cref="SyncPinHolder"/> cannot carry the board back to the pose this just replaced.
    /// </summary>
    private void ReseatBesidePlayer(string stage)
    {
        if (_root == null)
            return;
        Vector3 before = _root.position;
        Vector3 keepScale = _root.localScale;
        bool keepScaleValid = IsFinite(keepScale) && keepScale.x > 1e-4f;

        _placed = false;
        PlaceAtHead(forceFirstSeat: true); // defers safely if the head lost its pose; retried next verdict
        if (keepScaleValid)
            _root.localScale = keepScale;

        NotePinnedWrite($"arrival seat guard ({stage})"); // freeze sentinel: a sanctioned write
        // Sanction it for CardsDriver's issue-C pose watch as well, which would otherwise report a
        // silent recompute. Drained by CardsDriver right after TickLostWatchdog returns.
        _pinHousekeepingMove = "arrival seat guard re-seated the board beside the player";

        // Re-author the pin against the CURRENT origin, and re-cache the rig-relative pose from the
        // NEW world pose. Without the second half, the next origin change would carry the board by
        // an offset measured before this correction and quietly undo it.
        _pinPoseVersion = VRRigDriver.RigPoseVersion;
        Transform? rig = VRRigDriver.RigRoot;
        if (rig != null)
        {
            _rigLocalPinPos = rig.InverseTransformPoint(_root.position);
            _rigLocalPinRot = Quaternion.Inverse(rig.rotation) * _root.rotation;
            _rigLocalPinValid = IsFinite(_rigLocalPinPos);
        }
        if (_wantVisible)
            SetVisible(true); // a correction must never leave the board hidden
        VRLog.Info("Cards", $"Control board re-seated beside the player by the ARRIVAL SEAT GUARD " +
                            $"({stage}): {before} → {_root.position}.");
    }

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));
}
