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
        // FOLLOW MODE ONLY — and the REASON has changed, so read this before re-deciding it.
        //
        // The old reason, DELETED because it is now false: it used to say that the clamp's measure
        // (world width ÷ LIVE rig scale) was itself the bug on a pinned board, because the board's
        // world size was frozen while a zoom rescaled the player, so the apparent width swept across
        // the 18/140 cm limits with nobody touching the board — and it concluded that "a pinned
        // board's apparent size changing with zoom is what pinning MEANS". That conclusion is exactly
        // what the user has now rejected three times over (ModBuild 158 "dann hat das controllboard
        // mitgezoomed, das soll nicht passieren"; 159 "Das Board zoomed immer noch im Fixiert modus
        // mit"). The measure was never the bug — the frozen world size was. Since TickPinnedZoomCarry
        // the pin holder tracks the rig across a scale change, so parent/rigScale is a CONSTANT for a
        // pinned board and the apparent width no longer drifts at all; the drift this paragraph used
        // to describe cannot happen any more.
        //
        // The reason it is STILL follow-only is now the plain one, and it is ruling 4 (2026-08-07):
        // "Fixiert heißt: völlig unabhängig vom Character, bewegt sich in KEINSTER Weise, außer es
        // wird aktiv verschoben oder skaliert". A clamp is an AUTOMATIC writer of the board's scale.
        // Running it on a pinned board would be adding back exactly the class of writer the freeze
        // sentinel below exists to convict, and it would do so for no benefit: the size was already
        // legal when the player set it (GrabScaleLimits bounds every explicit resize live, in BOTH
        // modes, and is itself zoom-invariant now), and nothing can push it out of the band while
        // pinned because nothing re-derives it. So the clamp stays a FOLLOW-mode safety net.
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

        // (1) NO holder rescale HERE. The holder DOES now track the rig — but in LateUpdate, gated
        // on the rig scale actually having changed, and never from this method. See
        // TickPinnedZoomCarry below; this block records why neither of the two obvious placements
        // works, because both have shipped and both came back as a user report.
        //
        // NOT HERE, BECAUSE OF THE PHASE. An earlier cut re-asserted _pinRoot.localScale from the
        // live rig scale in exactly this spot. This method runs from CardsDriver.Update, and
        // WorldGrab.Update — which writes the new rig scale — runs after it. So the holder was
        // carried against LAST frame's rig scale on every frame of every pinch, and the hardware log
        // printed the identity: "parent chain ×64.79 ÷ rig ×68.50", 64.79 being the previous frame's
        // rig scale, in 82 of 158 moving-zoom samples. A ±10 % breathing through every zoom. The
        // read has to happen after every Update-phase rig writer AND after VRRigDriver.LateUpdate's
        // tilt heal, which is what BoardZoomCarryDriver's [DefaultExecutionOrder(20000)] LateUpdate
        // buys and what nothing in the Update phase can.
        //
        // AND NOT UNCONDITIONALLY, WHEREVER IT IS PUT. The other shipped cut (ModBuild 160) carried
        // the board's rig-local pose on EVERY frame. That does make it zoom-invariant — and it also
        // makes it travel with the player, which is the half the user rejected in the same sentence:
        // "Fixiert funktioniert nun garnicht mehr — egal was man dort einstellt, das board zoomed
        // nun immer mit und geht immer mit." The gate on "did the scale change" is the entire
        // difference between a board that ignores the zoom and a board that follows you around.
        //
        // What is still true from the original block, and is the reason this method leaves the holder
        // alone: the holder is NOT parented under the rig, so nothing the rig does can drag it, and
        // every write to it is deliberate. There are exactly two writers — ApplyFollowMode, which
        // seats it at the origin while ADOPTING the tray, and CarryPinnedBoardAcrossZoom.
        //
        // The original block also said the holder "sits at the world ORIGIN with identity rotation",
        // and that is no longer true: the carry solves the holder's full pose so the tray's
        // parent-local transform never has to move. Do not re-derive anything from an assumed origin
        // — ApplyFollowMode's re-seat is guarded on `_root.parent != _pinRoot` for exactly this
        // reason, and removing that guard would teleport an established board by the whole
        // accumulated carry.

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

    // ------------------------------------------------- FIXIERT: THE ZOOM CARRY (rule + state) --
    //
    // THE RULE, whole: on any frame in which the rig's lossy SCALE changed, re-derive the pinned
    // board's world pose — position, rotation AND scale — from its rig-local pose cached at the end
    // of the previous frame. On every other frame, touch nothing at all.
    //
    // WHY THAT IS THE ANSWER, and why neither pure anchor is. The world pinch does not scale about
    // the head: Rig/WorldGrab.cs solves `rig.position = _midAnchorWorld - rot * (mid * s)`, keeping
    // the GLUED HAND MIDPOINT fixed. So a scale change moves the head in world space as well, and
    // the pose a player PERCEIVES for a world point P is the rig-local one, rot⁻¹·(P − rigPos)/s.
    // Therefore:
    //   * hold the RIG-LOCAL pose  → the board is invisible to the zoom (ModBuild 160 did this on
    //     every frame, so the board also travelled with the player: "geht immer mit");
    //   * hold the WORLD pose      → the board stays in the room (ModBuild 158 did this on every
    //     frame, so the board rode every zoom: "zoomt mit").
    // Gating the first on "did the scale change" gives both at once, because the SCALE is the only
    // rig quantity a zoom moves that locomotion does not. Walking, stick locomotion, teleport,
    // flight, snap turn and a world-grab DRAG all leave the scale alone → nothing is written → the
    // board stays bolted to the room (ruling 4, and the "geht immer mit" half of report 3). A pinch
    // changes it → the pose is carried → the board does not move in the eye by so much as a pixel
    // (reports 1 and 2, and the "zoomt mit" half of report 3).
    //
    // DECISION — A COMBINED TWO-HAND ROTATE+ZOOM ALSO CARRIES THE YAW, and that is deliberate.
    // A two-hand world grab can change the rig's yaw and its scale in the same frame, so a full
    // rig-local carry re-orients the board with the player, while a pure rotate-drag (no scale
    // change) leaves it alone entirely. That asymmetry looks arbitrary and is not: THE TWO ARE NOT
    // SEPARABLE. The obvious decomposition — "cancel only the part of the motion the scale caused",
    // i.e. scale the board's offset about the rig origin by s1/s0 and never touch its rotation —
    // fails on the simplest case of all, a PURE zoom, because the pinch's own solve TRANSLATES the
    // rig as a direct function of s (the line quoted above). During a pure zoom the rig's
    // translation IS the scale change, so there is no frame in which "the scale part" and "the pose
    // part" are different things, and a rule that tried to split them would leave the board swimming
    // through every pinch. Carrying the whole rig-local pose is the only formulation that holds the
    // eye still, and on a combined gesture it reads correctly anyway: a two-hand world grab is the
    // player re-seating and re-sizing THEMSELVES, and an instrument that stays put in your hands
    // while you turn is what "fixiert" means for the duration of that gesture.

    /// <summary>Board pose in the player's own frame, cached at the END of the previous carry tick —
    /// the pose the next scale change re-derives the world pose from.</summary>
    private Vector3 _zoomCarryRigLocalPos;
    private Quaternion _zoomCarryRigLocalRot = Quaternion.identity;

    /// <summary>Rig scale at the last CARRY (or re-baseline), NOT at the last frame. The gate compares
    /// against this on purpose: a creep of less than one epsilon per frame would never fire a
    /// frame-to-frame comparison while still summing to a real zoom over a second, and the board would
    /// ride it exactly as it did in report 2. Against the last carried value a sub-threshold creep
    /// accumulates until it crosses, so the gate can be one epsilon late but can never be missed.</summary>
    private float _zoomCarryRigScale = -1f;

    /// <summary><see cref="VRRigDriver.RigPoseVersion"/> the cache was taken under. A rig rebuild or a
    /// recentre means <see cref="SyncPinHolder"/> has already carried the board by the SAME rig-local
    /// pose this frame, in the Update phase; re-deriving it again here would be a second write of an
    /// answer that is already correct. On a version change this tick re-baselines and carries nothing,
    /// which is also what makes the two writers unable to compound.</summary>
    private int _zoomCarryPoseVersion = -1;

    private bool _zoomCarryValid;

    /// <summary>How many carries have been logged in full. The first few are logged unconditionally,
    /// with the before/after rig scale, so the next hardware log can PROVE the gate fired only on
    /// zooms — the one claim that separates this round from ModBuild 160.</summary>
    private int _zoomCarriesLogged;
    private float _nextZoomCarryLog;

    /// <summary>
    /// THE ZOOM CARRY. Driven by <see cref="BoardZoomCarryDriver"/> in LateUpdate at execution order
    /// 20000 — see that class for why the phase is the load-bearing part and why ModBuild 159's
    /// identical arithmetic in the Update phase produced a ±10 % breathing instead of a fix.
    /// </summary>
    internal void TickPinnedZoomCarry()
    {
        // Same preconditions as the watchdog: an unplaced board is still at its spawn pose and a
        // hidden one has no pose worth preserving, so there is nothing to hold steady yet.
        if (_root == null || !_wantVisible || !_placed)
        {
            _zoomCarryValid = false;
            return;
        }
        Transform? rig = VRRigDriver.RigRoot;
        if (rig == null)
        {
            _zoomCarryValid = false;
            return;
        }
        float rigScale = rig.lossyScale.x;
        if (!(rigScale > 1e-6f) || float.IsInfinity(rigScale))
        {
            // A degenerate rig scale is not a zoom. Dropping the baseline is the safe verdict: the
            // next healthy frame re-caches rather than carrying the board by a ratio against garbage.
            _zoomCarryValid = false;
            return;
        }

        // FOLLOW mode is rig-parented, so the zoom is structurally invisible to it already; there is
        // no holder to carry and nothing to derive. The DIAGNOSTIC still runs, because the whole point
        // of the new line is that the two modes can be compared in one log: a FOLLOW board and a
        // FIXIERT board must now report the same three invariants through a zoom, and if only one of
        // them does, the log says which.
        if (CardsConfig.TrayFollow.Value || _pinRoot == null)
        {
            _zoomCarryValid = false;
            TickBoardSizeDiagnostic(rig, rigScale);
            return;
        }

        int version = VRRigDriver.RigPoseVersion;
        // A rebuild/recentre has already been handled in the Update phase (SyncPinHolder case 2), and
        // a first-ever tick has no previous frame to carry from. Both re-baseline and write nothing.
        bool rebaseline = !_zoomCarryValid || _zoomCarryPoseVersion != version;

        if (rebaseline)
        {
            _zoomCarryRigScale = rigScale;
        }
        else if (BoardZoomCarry.ScaleChanged(_zoomCarryRigScale, rigScale, out float relativeDelta)
                 && CarryPinnedBoardAcrossZoom(rig, rigScale, relativeDelta))
        {
            // ADVANCE THE BASELINE ONLY ON A CARRY THAT ACTUALLY WROTE. Two failure modes meet here.
            // Advancing it unconditionally would swallow a zoom the carry declined (non-finite
            // operands) and leave the board riding it. NOT advancing it at all — which is what this
            // line was missing when it was first written — is far worse: the gate would then compare
            // every later frame against the scale at the last re-baseline, so it would stay open for
            // the rest of the session AND CarryHolderScale would re-apply the whole accumulated ratio
            // rigScale/previousRigScale on every single frame, compounding the holder to infinity in
            // about a second. The wire vectors did not catch it because they drive the arithmetic and
            // reproduce the sequencing; the phase lint below now pins this assignment for that reason.
            _zoomCarryRigScale = rigScale;
        }

        // Re-cache from the pose that ACTUALLY survived this frame — after our own carry, after the
        // hand's carry if the board is being held, after everything. That is what makes the rule
        // "cached at the end of the previous frame" true rather than aspirational: if the player
        // walked, dragged the world or re-placed the board by hand this frame, the next zoom holds
        // THAT pose steady and not a stale one.
        _zoomCarryRigLocalPos = BoardZoomCarry.ToRigLocal(_root.position, rig.position, rig.rotation, rigScale);
        _zoomCarryRigLocalRot = BoardZoomCarry.Conjugate(rig.rotation) * _root.rotation;
        _zoomCarryValid = IsFinite(_zoomCarryRigLocalPos);
        _zoomCarryPoseVersion = version;

        TickBoardSizeDiagnostic(rig, rigScale);
    }

    private float _nextBoardSizeLog;
    private float _lastLoggedApparentWidth = -1f;
    private float _lastLoggedDistance = -1f;
    private float _lastLoggedSubtended = -1f;

    /// <summary>
    /// THE BOARD SIZE LINE. It reports what the EYE gets, and it exists in this shape because its
    /// predecessor did not.
    ///
    /// <para><b>Why the old measure was not enough, and it is not a detail.</b> The diagnostic used to
    /// print apparent size alone — world size ÷ rig scale — and <b>that number agreed with four
    /// broken builds in a row</b>. It had to: what the eye actually judges is ANGULAR size, which is
    /// size ÷ DISTANCE, and a world-static board under a zoom has both its perceived size and its
    /// perceived distance scale by 1/s. Its apparent-size line therefore swings wildly while the board
    /// looks exactly the same, and — worse in the other direction — a board whose size is pinned but
    /// whose distance is not reads to the player as "mitgezoomed" while the same line reads as
    /// perfect. Four rounds were signed off against a number that could not see the defect. So this
    /// line prints the pair AND the angle they imply, each with its delta since the previous line, and
    /// the next hardware round is decidable from the log alone: under this rule all three deltas must
    /// be ~0 through a zoom, and the DISTANCE delta must be non-zero when the player walks.</para>
    /// </summary>
    private void TickBoardSizeDiagnostic(Transform rig, float rigScale)
    {
        if (_root == null)
            return;
        if (!TryGetApparentWidthPerScaleUnit(out float perUnit, out float parent, out _))
            return;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;

        float apparentWidth = perUnit * _root.localScale.x;
        float distance = BoardZoomCarry.PerceivedMeters(
            Vector3.Distance(_root.position, head.transform.position), rigScale);
        float subtended = BoardZoomCarry.SubtendedDegrees(apparentWidth, distance);
        if (!(apparentWidth > 0f) || !(distance > 0f))
            return;

        // Throttled to once a second, and quiet while nothing moves: a heartbeat every 10 s keeps the
        // measure present in an idle log without filling it.
        float now = Time.unscaledTime;
        if (now < _nextBoardSizeLog)
            return;
        bool first = _lastLoggedApparentWidth < 0f;
        bool moved = first
                     || Mathf.Abs(apparentWidth - _lastLoggedApparentWidth) > 0.002f * Mathf.Max(_lastLoggedApparentWidth, 1e-4f)
                     || Mathf.Abs(distance - _lastLoggedDistance) > 0.002f * Mathf.Max(_lastLoggedDistance, 1e-4f)
                     || Mathf.Abs(subtended - _lastLoggedSubtended) > 0.01f;
        if (!moved && now < _nextBoardSizeLog + 9f)
            return;

        string deltas = first
            ? "first sample"
            : $"Δ width {(apparentWidth - _lastLoggedApparentWidth) * 100f:+0.00;-0.00;0.00} cm, " +
              $"Δ distance {distance - _lastLoggedDistance:+0.000;-0.000;0.000} m, " +
              $"Δ angle {subtended - _lastLoggedSubtended:+0.00;-0.00;0.00}°";
        _nextBoardSizeLog = now + 1f;
        _lastLoggedApparentWidth = apparentWidth;
        _lastLoggedDistance = distance;
        _lastLoggedSubtended = subtended;

        VRLog.Info("Cards", $"BOARD SIZE ({(CardsConfig.TrayFollow.Value ? "FOLLOW" : "FIXIERT")}): " +
                            $"apparent width {apparentWidth * 100f:F1} cm at {distance:F2} player-metres " +
                            $"→ subtends {subtended:F2}°. {deltas}. " +
                            $"(parent chain ×{parent:F2} ÷ rig ×{rigScale:F2} = " +
                            $"×{parent / Mathf.Max(rigScale, 1e-6f):F4}, the ratio the min/max limits " +
                            "ride on — it must be CONSTANT through a zoom.) Angular size, not apparent " +
                            "size, is what the player judges: size ÷ distance.");
    }

    /// <summary>
    /// Re-derive the pinned board's world pose so the zoom leaves the eye unchanged. Two writes, and
    /// THE ORDER IS LOAD-BEARING.
    ///
    /// <para>(a) THE HOLDER takes the rig's scale change. The board's perceived size is
    /// <c>holder × localScale / rigScale</c>, so tracking the holder to the rig holds it constant
    /// without touching <c>_root.localScale</c> — the number the player dialled in with the two-hand
    /// gesture and <c>BoardScale</c> is never written by this path, in either mode. This is also what
    /// makes the min/max stop drifting: <see cref="TryGetApparentWidthPerScaleUnit"/> computes
    /// <c>BoardHalfWidthLocal × 2 × parent / rigScale</c> with <c>parent</c> = the holder, so once the
    /// holder tracks the rig the ratio <c>parent/rigScale</c> is a constant and both the release-time
    /// clamp and <c>GrabScaleLimits</c> become zoom-invariant. That is report 1's second sentence
    /// ("Weiterhin hat sich damit auch das Maximum und Minimum wieder verschoben") falling out for
    /// free, with no limit loosened and no new clamp anywhere.</para>
    ///
    /// <para>(b) THE TRAY ITSELF IS NEVER WRITTEN. The carry moves the HOLDER — its position, its
    /// rotation and its scale — and solves that pose so that the tray's PARENT-LOCAL transform comes
    /// out bit-for-bit unchanged while its WORLD pose lands exactly where the rule wants it. Unity
    /// stores local values and derives world ones, so re-posing a parent leaves every child's
    /// localPosition/localRotation/localScale untouched by construction; there is nothing to restore
    /// and nothing to order wrongly.</para>
    ///
    /// <para><b>Why that indirection is worth it, and it is not cosmetic.</b> CardsDriver's issue-C
    /// pose watch compares the tray's PARENT-LOCAL pose and warns about any change nobody sanctioned
    /// (CardsDriver.2.Update.cs, TickBoardPoseWatch). Writing the tray's world pose directly would move
    /// its localPosition on every frame of every pinch — the holder sits away from the tray, so the
    /// rescale alone shifts it by ~1 m of local offset across his recorded ×19→×137 sweep — and the
    /// watch would then emit a "Board pose [...]" line at frame rate for the whole gesture. Solving the
    /// HOLDER instead means that in the board's own frame of reference nothing happened, which is the
    /// literal truth of what a zoom carry is: the board did not move, the player's scale did. The
    /// world-space freeze sentinel still sees the write and still has it announced, so the one
    /// watchdog that SHOULD notice does, and the one that should not, does not.</para>
    ///
    /// <para><b>A GRIPPED BOARD KEEPS ITS WORLD POSITION.</b> While the handle is held the hand is the
    /// authority on where the board is — that is ruling 4's "außer es wird aktiv verschoben", and
    /// <c>PanelGrabHandle.Update</c> lerps <c>root.position</c> toward a palm-relative target every
    /// frame, so a rig-local write here would simply be lerped away again and read as a smear. What
    /// the carry still does while held is (a), the size — and that is exactly report 1, which is a
    /// report about a board held in ONE hand ("Ich hatte in einer Hand das Controllboard und habe dann
    /// gezoomed"): its perceived size is now constant through the pinch. Same solve, with the board's
    /// CURRENT world pose as the target instead of the rig-local one.</para>
    /// </summary>
    /// <returns>True when the carry wrote; false when it declined (degenerate operands), which
    /// leaves the caller's baseline where it was so the next healthy frame retries the same zoom.</returns>
    private bool CarryPinnedBoardAcrossZoom(Transform rig, float rigScale, float relativeDelta)
    {
        if (_root == null || _pinRoot == null)
            return false;

        float previousRigScale = _zoomCarryRigScale;
        Vector3 beforePos = _root.position;
        Quaternion beforeRot = _root.rotation;
        float beforeWorldScale = _root.lossyScale.x;

        bool grabbed = _handle != null && _handle.IsGrabbed;
        Vector3 wantPos = grabbed
            ? beforePos
            : BoardZoomCarry.ToWorld(_zoomCarryRigLocalPos, rig.position, rig.rotation, rigScale);
        Quaternion wantRot = grabbed ? beforeRot : rig.rotation * _zoomCarryRigLocalRot;

        float holder = _pinRoot.localScale.x;
        float wantedHolder = BoardZoomCarry.CarryHolderScale(holder, previousRigScale, rigScale);

        // Solve the holder pose that leaves the tray's LOCAL transform alone and puts its WORLD pose
        // on target: world = holderPos + holderRot · (localPos · H), world rot = holderRot · localRot.
        Vector3 localPos = _root.localPosition;
        Quaternion localRot = _root.localRotation;
        Quaternion wantedHolderRot = wantRot * BoardZoomCarry.Conjugate(localRot);
        Vector3 wantedHolderPos = wantPos - wantedHolderRot * (localPos * wantedHolder);

        // Validate EVERYTHING before writing ANYTHING. A half-applied carry (holder rescaled, pose
        // not) is the offset drift the holder exists to prevent, and a non-finite transform is the one
        // verdict the lost-board watchdog treats as unrecoverable.
        if (!IsFinite(wantPos) || !IsFinite(wantedHolderPos)
            || !(wantedHolder > 1e-6f) || float.IsInfinity(wantedHolder))
            return false;

        _pinRoot.localScale = Vector3.one * wantedHolder;
        _pinRoot.SetPositionAndRotation(wantedHolderPos, wantedHolderRot);

        // The freeze sentinel compares the pinned board's WORLD pose against last frame's and warns
        // about any change no writer announced. This carry is a sanctioned writer — announcing it is
        // what keeps "PINNED tray transform WRITE [UNKNOWN WRITER]" meaning what it says.
        //
        // NOTE the deliberate absence of a _pinHousekeepingMove label here, unlike SyncPinHolder's
        // tracking-origin carry. That label sanctions a PARENT-LOCAL change for the issue-C watch, and
        // this carry makes none — see (b) above. Setting it anyway would be announcing a write that
        // never happens.
        NotePinnedWrite($"zoom carry (rig ×{previousRigScale:F2} → ×{rigScale:F2})");

        // OUTCOME, not intention: the first few carries are logged in full, with the rig scale on
        // both sides, so the next hardware log can be read for the one property that separates this
        // build from ModBuild 160 — that the gate fired ONLY on zooms. If these lines appear while
        // the player is merely walking, the gate is wrong and the log says so without being asked.
        float now = Time.unscaledTime;
        if (_zoomCarriesLogged < 8 || now >= _nextZoomCarryLog)
        {
            _zoomCarriesLogged++;
            _nextZoomCarryLog = now + 2f;
            VRLog.Info("Cards", $"FIXIERT board carried across a zoom (#{_zoomCarriesLogged}): rig scale " +
                                $"×{previousRigScale:F3} → ×{rigScale:F3} (Δ {relativeDelta * 100f:F3} %), " +
                                $"holder ×{holder:F3} → ×{wantedHolder:F3}, world pos {beforePos} → " +
                                $"{_root.position}, world size ×{beforeWorldScale:F3} → " +
                                $"{_root.lossyScale.x:F3}{(grabbed ? ", HELD (hand keeps the position)" : "")}. " +
                                "The board's PERCEIVED pose and size are unchanged by this write — that is " +
                                "what it is for. A line here while nobody is zooming is a bug.");
        }
        return true;
    }

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

        perUnit = BoardHalfWidthLocal * 2f * parent / rigScale;
        return perUnit > 1e-6f && !float.IsInfinity(perUnit);
    }

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));
}
