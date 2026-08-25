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

        // GRAB EDGE. The size the board had the frame the hand closed on it, so
        // PersistPoseToConfig can tell a CARRY from a RESIZE (see the guard there): a one-hand
        // carry must never re-author the board's size, and since 2026-08-25 the live size can
        // legitimately differ from the configured one (the push below), which would otherwise
        // make every carry write the push into the config and re-seat BoardScale_{board}.
        bool heldNow = _handle != null && _handle.IsGrabbed;
        if (heldNow && !_wasHeld)
            _scaleAtGrabStart = _root.localScale.x;
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
        string verdict = follow
            ? "FOLGEN — the board hangs off the player and is SUPPOSED to move with them."
            : held
                ? "FIXIERT, HELD — the hand is carrying it, so a change here is the player's own."
                : moved
                    ? pushesSince > 0
                        ? "FIXIERT, NOT HELD, AND THE SIZE MOVED — EXPECTED: this is the 2026-08-25 "
                          + "exception. A bound walked into the board and pushed it; the world POSITION "
                          + "must still be zero-delta, and the board must NOT come back down when the "
                          + "player reverses. Grep BOARD SIZE PUSHED at this timestamp for which bound."
                        : "FIXIERT, NOT HELD, AND IT MOVED WITH NO PUSH — this is a defect. Something "
                          + "wrote the board's world transform. The ruling is 'keinerlei Abhängigkeit "
                          + "zum Spieler, fix in der Welt' outside the two Randfälle; grep the PINNED "
                          + "tray transform WRITE line at this timestamp, it names the writer."
                    : "FIXIERT, NOT HELD — world pose and world scale are FROZEN, which is the "
                      + "invariant. The rig scale beside them is free to move and normally has.";

        _anchorLogPos = worldPos;
        _anchorLogScale = worldScale;
        _anchorLogValid = true;
        _loggedAnchorPushCount = _sizePushCount;

        VRLog.Info("Cards", $"BOARD ANCHOR: world pos {worldPos}, world scale {worldScale:F3} " +
                            $"(own localScale {_root.localScale.x:F3}) against rig ×{rigScale:F2}. " +
                            (haveBaseline
                                ? $"Since the last line: Δ world pos {posDelta * 1000f:F2} mm-world, " +
                                  $"world scale ×{scaleRatio:F5}, {pushes}. "
                                : $"first sample, {pushes}. ") +
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

    /// <summary>Value of <see cref="_sizePushCount"/> the last BOARD ANCHOR line reported, so the
    /// next one can state how many pushes happened between the two samples.</summary>
    private int _loggedAnchorPushCount;

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
    /// <paramref name="parent"/>/<paramref name="rigScale"/>/<paramref name="divisor"/> are
    /// surfaced for the diagnostic lines only; <paramref name="divisor"/> is now always equal to
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

    private static bool IsFinite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsInfinity(v.x)
          || float.IsNaN(v.y) || float.IsInfinity(v.y)
          || float.IsNaN(v.z) || float.IsInfinity(v.z));
}
