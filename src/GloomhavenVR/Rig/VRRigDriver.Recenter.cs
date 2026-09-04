using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    /// <summary>
    /// Recenter the live rig, if any (the comfort entry point — chord/panel/dev key).
    ///
    /// <para>DELIBERATELY WITHOUT THE SPAWN RING'S AZIMUTH SOLVE, and that half of the rule is
    /// unchanged. This is the entry point a HUMAN pulls (B+Y hold, the dev key), and re-solving the
    /// multiplayer ring here would teleport a player around the table on an unrelated request —
    /// because a PEER moved. (It used to be reachable from a config change too — the [Comfort]
    /// TableHeightOffset handler re-ran recenter live; that setting is gone, so today EVERY caller
    /// is a deliberate human act, which only strengthens the rule.) The ring as a JOIN PLACEMENT —
    /// which side of the table you get — lives entirely in <see cref="TickSpawnRingSettle"/>, and
    /// this path never runs it. It also CLOSES the ring's window: a player who has just placed
    /// themselves by hand must never be moved again.</para>
    ///
    /// <para>WHAT DID CHANGE (2026-09-04, hardware): the ring's GEOMETRY. The user: "Wenn man Y und
    /// B gedrückt hält re-spawnt man an eine Stelle. Soweit so gut - allerdings ist der Spawnpunkt
    /// IN dem Spielfeld wenn man sich so recentered. Ich will das der Spawnpunkt derselbe ist an dem
    /// man am Anfang auch reingespawnt ist, der bereits die Regeln enthält." A recenter now solves
    /// the radius, the height and the facing the way the arrival seat did — at the azimuth the
    /// player ALREADY HAS, which is what keeps the paragraph above true. See
    /// <see cref="SpawnRing.TryRecenterSeat"/>.</para>
    /// </summary>
    internal static void RequestRecenter()
    {
        VRRigDriver? drv = Instance;
        if (drv == null)
            return;
        drv.CloseRingWindow("the player recentered manually");
        drv.Recenter();
    }

    /// <summary>
    /// Reposition the rig so the player's CURRENT head pose ends up back at the table. Called
    /// automatically on the first tracked pose, and bound to the B+Y hold chord (see
    /// <see cref="Comfort"/>).
    ///
    /// <para>TWO RULES, IN THIS ORDER, and the first one is new (2026-09-04, hardware — "allerdings
    /// ist der Spawnpunkt IN dem Spielfeld wenn man sich so recentered. Ich will das der Spawnpunkt
    /// derselbe ist an dem man am Anfang auch reingespawnt ist, der bereits die Regeln enthält"):
    /// <list type="number">
    /// <item>THE ARRIVAL SEAT'S GEOMETRY, whenever the board can be measured
    /// (<see cref="SpawnRing.TryRecenterSeat"/>): out to the board's own EDGE along the seat
    /// direction plus the standing clearance, backed off until the whole play field fits in a
    /// glance, <see cref="SpawnRing.RingHeadAboveFocusMeters"/> above the board plane, facing the
    /// board centre. The AZIMUTH is not re-solved — it is the one the ring seated this player at on
    /// arrival, or failing that the side of the table they are already on.</item>
    /// <item>THE TABLE-EDGE FALLBACK, byte-for-byte what this method has always done, when there is
    /// no measurable board: eyes <see cref="ComfortSettings.EffectiveEyeHeightMeters"/> (real) above
    /// the orbit focus plane and <see cref="ComfortSettings.EffectiveEyeBackMeters"/> back — both
    /// the bare STANDING preset (0.30 above / 0.70 back), since there is no seated preset any more
    /// and, since the 2026-08 ruling, no [Comfort] TableHeightOffset to add to the height either.
    /// That rule is right in a world with no play field and wrong in one with a big play field,
    /// which is precisely the defect above: <c>FocusPoint</c> moves with the player's view over a
    /// scenario, and 0.70 m back from it lands in the middle of a large diorama.</item>
    /// </list>
    /// Both outcomes write ONE log line, both name the rule they took, and the fallback names WHY
    /// the board could not be measured.</para>
    ///
    /// <para>NOT CONFIGURABLE, ON PURPOSE. The seat is one derived pose so that "recenter" means the
    /// same thing every time — the deterministic way back to the table from wherever free locomotion
    /// left you. Eye height is the player's own business: the stick (see <see cref="Flight"/>) and
    /// the world grab move them up, down and through the scene at will, which is precisely why the
    /// height dial was removed rather than kept alongside them. No new key was added for any of the
    /// above either.</para>
    ///
    /// <para>THE RING'S PLACEMENT IS STILL NOT HERE: this method borrows the ring's GEOMETRY and
    /// never its azimuth solve, does not open, re-arm or consult the settle window, and must not be
    /// given a <c>useSpawnRing</c> flag back. Why that shape failed is recorded once, at
    /// <see cref="TickSpawnRingSettle"/>.</para>
    /// </summary>
    internal void Recenter()
    {
        if (_rigRoot == null || _camera == null)
            return;

        // Controls lesson: the "re-seat yourself" step. Reported once the recentre is going to
        // happen, before the per-rig-kind branches — all three of them are a recentre.
        Compat.ControlsProgress.Notify(Compat.ControlAction.Recenter);

        if (_kind == RigKind.Menu)
        {
            RecenterMenu();
            return;
        }

        if (_kind == RigKind.Map)
        {
            // The map room's seat is a pure function of the parchment bounds, so "recenter" there
            // means "back to the table" without consulting the orbit camera at all — which is
            // also why the CameraController null-check below must not be allowed to swallow it.
            RecenterMap();
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return;

        float scale = _rigRoot.transform.localScale.x;

        // World tilt composition: BOTH seat rules below are authored for a yaw-only rig (the
        // fallback's seatYaw reads the current rotation, and the ring geometry's facing is a
        // yaw-only LookRotation; both assume a level horizon). Flatten the tilt out first —
        // TickWorldTilt re-applies the configured tilt on top of the fresh seat in LateUpdate this
        // same frame, so a recenter lands at the solved seat viewed through the tilt, with no
        // untilted frame ever rendered. (ApplyRingSeat flattens again on rule 1; YawOnly of a
        // yaw-only rotation is the identity, so the second flatten is a no-op, not a second event.)
        if (_tiltActive)
            _rigRoot.transform.rotation = YawOnly(_rigRoot.transform.rotation);
        // Attribute this frame's tilt re-apply (the LateUpdate heal after the flatten
        // above) to the recenter in the WorldTilt change log — and let it act as a
        // MASKED RE-AIM event: TickWorldTilt instantly re-aims the tilt at the current
        // view yaw, exactly what Demeo's recenter does by absorbing the head yaw into
        // the avatar root (InputTracking.Recenter / AvatarController.cs:684-694).
        _axisSnapReason = "recenter";

        // ---- RULE 1: THE ARRIVAL SEAT'S GEOMETRY, at an azimuth nobody re-solves ---------------
        // Read the board NOW (not at arrival) and at the CURRENT rig scale, so a player who has
        // pinch-zoomed the table since they sat down still lands at its edge. Everything the solver
        // returns in ...Meters is a REAL metre and everything in ...World is a world unit; the only
        // conversion is the rig scale it was handed, and it is applied exactly once in each
        // direction (SpawnRing.FillSeatGeometry states the frames).
        if (SpawnRing.TryRecenterSeat(controller.FocusPoint, scale, _camera.transform.position,
                                      _scenarioBaseYaw, _ringSeatAngleValid, _ringSeatAngleDegrees,
                                      out SpawnRing.Seat ringSeat, out SpawnRing.Footprint board,
                                      out string whyNotRing))
        {
            Vector3 ringHead = ApplyRingSeat(ringSeat, "recenter");

            // VERIFY THE OUTCOME, NOT THE PATH — the same re-measure the ring's own SEATED line
            // does. Everything above is what was ASKED for; this is the picture the player got,
            // read back off the real camera transform after RigClamp.
            SpawnRing.Framing achieved = SpawnRing.Frame(board, _camera.transform.position, scale);

            // HW-VERIFY: THE LINE THAT DECIDES THE 2026-09-04 REPORT. "(table-edge seat)" is kept
            // verbatim because it is the grep token every previous hardware round was read with;
            // the clause after it names the rule that actually produced the pose. If the chord still
            // drops the player into the diorama, this line says whether the ring geometry ran at all
            // (SEAT RULE) and, if it did, whether it ACHIEVED being clear of the board — which
            // localises the defect to the geometry rather than to the trigger.
            VRLog.Note("Rig", $"Recentered — head at {ringHead}, rig root at " +
                              $"{_rigRoot.transform.position} (table-edge seat). " +
                              $"SEAT RULE: the spawn ring's own geometry at azimuth " +
                              $"{ringSeat.AngleDegrees:F1}deg — " +
                              $"{(ringSeat.AzimuthRemembered ? "the REMEMBERED ARRIVAL AZIMUTH, i.e. the seat this scenario spawned the player at" : "the player's CURRENT side of the table (no ring seat was ever placed in this scenario)")}, " +
                              $"never re-solved against peers. Radius {ringSeat.RadiusMeters:F2} m " +
                              $"({ringSeat.RadiusWorld:F2} world units at rig scale {scale:F2}) = board " +
                              $"edge {ringSeat.EdgeMeters:F2} m along that direction + " +
                              $"{ringSeat.ClearanceMeters:F2} m clearance" +
                              $"{(ringSeat.FramingSteps > 0 ? $" [BACKED OFF {ringSeat.FramingSteps} step(s) to fit the play field in view]" : "")}" +
                              $"{(ringSeat.RadiusRaisedToFloor ? $" [raised to the {SpawnRing.MinRadiusMeters:F2} m table-edge floor]" : "")}; " +
                              $"{SpawnRing.RingHeadAboveFocusMeters:F2} m above the board plane, facing " +
                              $"the board centre {ringSeat.Center} over {ringSeat.TileCount} tile(s). " +
                              $"SOLVED FOR: {ringSeat.Framing}. ACHIEVED: {achieved} => " +
                              $"{(achieved.Acceptable ? "REQUIREMENT MET" : "REQUIREMENT NOT MET")}.");
            return;
        }

        // ---- RULE 2: THE TABLE-EDGE FALLBACK, exactly as it has always been --------------------
        // Reached only when there is no measurable board (whyNotRing says which). The orbit focus
        // point is then the only reference there is, and 0.70 m back from it is right in a world
        // with nothing to sit around.
        Quaternion seatYaw = _rigRoot.transform.rotation;
        Vector3 desiredHeadWorld = controller.FocusPoint
                                   + seatYaw * (Vector3.back * (ComfortSettings.EffectiveEyeBackMeters * scale))
                                   + Vector3.up * (ComfortSettings.EffectiveEyeHeightMeters * scale);

        Vector3 headOffsetWorld = seatYaw * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;
        RigClamp.Apply(_rigRoot.transform);
        RigPoseVersion++; // P6: world-anchored panels re-derive their seat yaw on recenter

        // HW-VERIFY: the same decision line for the OTHER outcome. A hardware round that reports a
        // bad recenter and finds THIS variant is looking at a board the mod could not measure, and
        // the reason is printed rather than inferred — the ring geometry never ran, so the pose is
        // the board-blind one and blaming its numbers would be blaming the wrong stage.
        VRLog.Note("Rig", $"Recentered — head at {desiredHeadWorld}, rig root at " +
                          $"{_rigRoot.transform.position} (table-edge seat). " +
                          $"SEAT RULE: the TABLE-EDGE FALLBACK — eyes " +
                          $"{ComfortSettings.EffectiveEyeBackMeters:F2} m back and " +
                          $"{ComfortSettings.EffectiveEyeHeightMeters:F2} m above the orbit focus " +
                          $"{controller.FocusPoint} at the current rig yaw, at rig scale {scale:F2}. " +
                          $"It ran because {whyNotRing}.");
    }

    /// <summary>
    /// Write a solved spawn-ring seat onto the rig. Same transform math as
    /// <see cref="Recenter"/>, but the seat direction AND the facing come out of the solve
    /// instead of the current rig yaw.
    ///
    /// <para>FACE THE BOARD, LITERALLY. The user's complaint was two-part: "spawnt man direkt
    /// hinter oder IN der anderen Maske UND MUSS SICH ERST AUSRICHTEN". Writing
    /// <c>seat.Yaw</c> straight onto the rig only fixes the first half — the player's VIEW is
    /// <c>rigRotation ∘ headLocalRotation</c>, so someone physically turned away at the moment of
    /// the join would still arrive looking at the wall. Absorbing the head's own yaw into the rig
    /// (the masked re-aim Demeo performs in InputTracking.Recenter / AvatarController.cs:684-694,
    /// and the reason <c>_axisSnapReason</c> is set below) makes the HEAD's world yaw exactly
    /// <c>seat.Yaw</c>, whatever direction the player is standing in.</para>
    /// </summary>
    /// <param name="seat">The solved seat.</param>
    /// <param name="axisSnapReason">What to attribute this frame's tilt re-aim to in the WorldTilt
    /// change log. The ring's own placement is "spawn ring seat"; the B+Y chord passes "recenter",
    /// because from the tilt's point of view that is exactly the masked re-aim it has always been —
    /// the seat rule underneath it changed, the event did not.</param>
    /// <returns>The world head position the seat was written for (log material).</returns>
    private Vector3 ApplyRingSeat(in SpawnRing.Seat seat, string axisSnapReason = "spawn ring seat")
    {
        float scale = _rigRoot!.transform.localScale.x;

        if (_tiltActive)
            _rigRoot.transform.rotation = YawOnly(_rigRoot.transform.rotation);
        _axisSnapReason = axisSnapReason;

        Quaternion seatYaw = seat.Yaw * Quaternion.Inverse(YawOnly(_camera!.transform.localRotation));
        _rigRoot.transform.rotation = seatYaw;

        // RING SEATS SPAWN RAISED (user ruling 2026-08-04: "Heb den Spawn-Ring etwas an, ich will
        // dass alle Spieler etwas höher als das Spielfeld selber spawnen"). The lift is ON TOP of
        // the standing eye height. IT NOW APPLIES TO THE B+Y RECENTER TOO — that carve-out was
        // removed on 2026-09-04, when the user asked for the opposite of what it assumed: "Ich will
        // das der Spawnpunkt derselbe ist an dem man am Anfang auch reingespawnt ist, der bereits
        // die Regeln enthält". The elevated arrival vantage that reads the whole field at a glance
        // IS one of "die Regeln", and with stick flight the player descends in a second if they
        // want to.
        //
        // THE HEIGHT COMES FROM SpawnRing.RingHeadAboveFocusMeters, not from a second copy of the
        // sum. The solver measures the picture at the head pose it is asking for; if this call site
        // computed a different height, the framing the log proves and the framing the player gets
        // would be two different things. seat.HeadWorld is that same pose, already resolved.
        Vector3 desiredHeadWorld = seat.HeadFlat
                                   + Vector3.up * (SpawnRing.RingHeadAboveFocusMeters * scale);
        Vector3 headOffsetWorld = seatYaw * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;
        RigClamp.Apply(_rigRoot.transform);
        RigPoseVersion++;
        return desiredHeadWorld;
    }

    /// <summary>
    /// THE WHOLE SPAWN RING, and the only code that ever places a join seat. It runs once on the
    /// first tracked pose and then every <see cref="CircleReseatIntervalFrames"/> frames while the
    /// window is open — both call sites are THIS method, so there is exactly one place where the
    /// decision is made and exactly one place where it is logged. Round 1 split the logic between
    /// <c>Recenter(useSpawnRing: true)</c> and a poll, and its single failure mode ended up
    /// reported inside a line that says "Recentered"; here EVERY outcome — including "did nothing
    /// because X" — leaves its own line. That is a hard user requirement: the next hardware test
    /// must be diagnosable from the log alone.
    ///
    /// <para>ROUND 4 (2026-09-02, "man spawned IM Spielfeld ... kommt es immer noch vor"). The
    /// hardware log holds the whole failure in three lines: the ring armed, its FIRST attempt found
    /// <c>board tiles=0</c>, and the window was then closed by "the player moved themselves (stick
    /// flight)" after TWO attempts — inside ~35 frames of the rig existing. Both halves of that were
    /// this method's fault, and neither was the seat geometry:</para>
    ///
    /// <para>(1) THE TRIGGER WAS A CLOCK. The ring retried on a cadence and gave up on a deadline,
    /// so whether it ran at all depended on how the load happened to be paced. The trigger is now
    /// the BOARD BECOMING MEASURABLE — the tile cache going from empty to non-empty, confirmed by
    /// one repeat poll (<see cref="RingBoardStablePolls"/>). The game offers no event to hang this
    /// on: <c>ObjectCacheService</c> has no add/complete callback and there is no
    /// <c>OnBoardReady</c> anywhere in the decompile, so a cheap <c>HashSet.Count</c> read is as
    /// close to the event as the game allows. The deadline that remains answers only "is there a
    /// board in this rig at all", and its expiry is a CORRECT outcome rather than a give-up: a
    /// scenario with no tiles has nothing to frame, so the ordinary table-edge seat is right.</para>
    ///
    /// <para>(2) THE FUSE COULD NOT TELL A HAND FROM AN ESCAPE. "The player moved themselves"
    /// closed the window permanently — but the thing a player DOES when they are dropped inside the
    /// diorama is fly, to get out of it. The guard was reading the symptom as consent. It is now a
    /// judgement about the RESULTING VANTAGE rather than about the input:
    /// <list type="bullet">
    /// <item>after the ring has seated the player, ANY locomotion closes the window at once — they
    /// are refining a vantage the ring gave them, and that is theirs;</item>
    /// <item>before the ring has ever seated them, locomotion is RECORDED, not obeyed. When the
    /// board becomes measurable, the player's own head is measured against the same acceptance test
    /// the seat has to pass (<see cref="SpawnRing.Framing"/>). Clear of the play field ⇒ they placed
    /// themselves, close the window and never move them. Down among the tiles
    /// (<see cref="SpawnRing.Framing.InsideTheDiorama"/>) ⇒ they are escaping a bad spawn, seat them
    /// ONCE and then close.</item>
    /// </list>
    /// FALSE POSITIVE, stated plainly: a player who, in the first seconds of a scenario and before
    /// any ring seat existed, deliberately flies DOWN INTO the diorama gets pulled out to the ring
    /// seat exactly once. It is bounded to one teleport, to the arrival window, and to the state in
    /// which the player has never had a ring seat — and the alternative is the shipped bug. FALSE
    /// NEGATIVE: a player who is badly spawned but flies to some other bad vantage that is merely
    /// OUTSIDE the footprint is left there. That is deliberate — outside the board is a vantage
    /// somebody might choose, inside it is not.</para>
    ///
    /// <para>THE WINDOW is opened at the FIRST TRACKED POSE (the round-1 window was largely
    /// consumed by the scenario load before the player was even tracked). Inside it we place ONCE
    /// and allow at most ONE correction — for a peer's first pose landing late, or for the
    /// footprint growing under us.</para>
    /// </summary>
    private void TickSpawnRingSettle()
    {
        if (_ringSettled)
            return;

        // CONFIG GATE, logged and latched: off ⇒ one line, then the step is gone from the frame.
        if (!Plugin.SpawnInCircle.Value)
        {
            CloseRingWindow("[Rig] SpawnInCircle is off");
            return;
        }

        if (_rigRoot == null || _camera == null)
        {
            CloseRingWindow("the rig went away before a seat could be placed");
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
        {
            CloseRingWindow("the scenario CameraController is gone (no focus plane to seat on)");
            return;
        }

        float now = Time.unscaledTime;
        float scale = _rigRoot.transform.localScale.x;

        // ---- THE TRIGGER: has the board arrived? -------------------------------------------
        // One HashSet.Count plus a pass over the tile transforms; 3 Hz, and the whole step is
        // removed from the frame the moment the ring is done (see the poll gate in UpdateBody).
        bool measurable = SpawnRing.TryFootprint(controller.FocusPoint.y, out SpawnRing.Footprint board);
        if (measurable)
            _ringBoardSeen = true;

        // ---- THE DEADLINE, and why each of its three expiries is CORRECT ---------------------
        if (now >= _ringWindowEnd)
        {
            if (_ringPlaced)
                CloseRingWindow($"the {SpawnRingSettleSeconds:F0}s arrival window closed on a placed " +
                                "seat — it is final now");
            else if (!_ringBoardSeen)
                // NOT A GIVE-UP. No tile ever reached the object cache in this rig, which means
                // there is no play field to sit around and nothing the ring could improve. The
                // ordinary table-edge seat is exactly right in that world.
                CloseRingWindow($"no scenario board ever reached the object cache in " +
                                $"{SpawnRingSettleSeconds:F0}s — there is nothing to sit around, so the " +
                                "ordinary table-edge seat is exactly right");
            else
                CloseRingWindow($"DEFECT: the board WAS measurable but the {SpawnRingSettleSeconds:F0}s " +
                                $"arrival window closed without a seat. Last outcome {_ringOutcome} " +
                                $"({_ringProbe}). The player is on the board-blind table-edge seat", alert: true);
            return;
        }

        _ringAttempts++;

        if (!measurable)
        {
            // Waiting for the board. Not an error and not a timeout — the tile cache goes from
            // empty to complete inside a single frame (SpawnRing.TryFootprint doc), we simply have
            // not reached that frame yet.
            _ringTileCount = 0;
            _ringStablePolls = 0;
            bool changed = _ringOutcome != SpawnRing.Outcome.BoardPending || _ringAttempts == 1;
            _ringOutcome = SpawnRing.Outcome.BoardPending;
            if (changed || now >= _ringNextLogTime)
            {
                _ringNextLogTime = now + RingLogIntervalSeconds;
                VRLog.Info("Rig", "Spawn ring: waiting for the board — the scenario's hex tiles are not " +
                                  $"in the object cache yet. Attempt {_ringAttempts}, board tiles=0. " +
                                  "The ordinary table-edge seat stands until then; the ring places on the " +
                                  "tiles ARRIVING, not on a timer" +
                                  $"{(_ringPlayerMoved ? $", and the player's own movement ({_ringPlayerMovedWhat}) has NOT closed the window — it is judged when the board can be measured" : "")}.");
            }
            return;
        }

        // ---- CONFIRM THE MEASUREMENT ---------------------------------------------------------
        // Two agreeing polls before anything is written. A map-alignment retry DestroyImmediates
        // and rebuilds every map (decompiled Choreographer.cs:14844-14850), so a footprint caught
        // mid-churn is a perfect measurement of a board that is about to stop existing.
        if (board.TileCount != _ringTileCount)
        {
            _ringTileCount = board.TileCount;
            _ringStablePolls = 1;
            return;
        }
        if (_ringStablePolls < RingBoardStablePolls)
        {
            _ringStablePolls++;
            if (_ringStablePolls < RingBoardStablePolls)
                return;
        }

        // ---- CONSENT: did the player put themselves somewhere, and is it survivable? ----------
        // Measured on the PLAYER'S OWN HEAD with the same test the seat must pass, which is the
        // only way "I chose this" and "I am escaping" can be told apart.
        SpawnRing.Framing playerFraming = SpawnRing.Frame(board, _camera.transform.position, scale);
        if (_ringPlayerMoved && !_ringPlaced && !playerFraming.InsideTheDiorama)
        {
            // HW-VERIFY: this is the line that says the new consent rule fired and chose to keep
            // its hands off — a hardware round that reports "it moved me when I did not want it to"
            // is decided here.
            VRLog.Note("Rig", $"Spawn ring: STANDING DOWN — the player moved themselves " +
                              $"({_ringPlayerMovedWhat}) and their own vantage is clear of the play " +
                              $"field, so it is theirs: {playerFraming}. Board tiles={board.TileCount}.");
            CloseRingWindow("the player placed themselves and their vantage is clear of the play field");
            return;
        }

        bool overridingPlayer = _ringPlayerMoved && !_ringPlaced;

        SpawnRing.Outcome outcome = SpawnRing.Solve(controller.FocusPoint, _scenarioBaseYaw, scale,
                                                    out SpawnRing.Seat seat, out SpawnRing.Probe probe);
        bool outcomeChanged = outcome != _ringOutcome;
        _ringOutcome = outcome;
        _ringProbe = probe;

        if (outcome != SpawnRing.Outcome.Placed)
        {
            // NOT PLACED — and that is a logged event, not silence. The only reachable reason here
            // is PeersUnknown (multiplayer, no peer position known yet); BoardPending cannot occur,
            // the footprint was measured two lines above. We keep polling the window out.
            if (outcomeChanged || now >= _ringNextLogTime)
            {
                _ringNextLogTime = now + RingLogIntervalSeconds;
                VRLog.Info("Rig", $"Spawn ring: NOT SEATED ({Explain(outcome)}) — attempt " +
                                  $"{_ringAttempts}, {probe}. Keeping the ordinary table-edge seat; " +
                                  $"retrying for another {Mathf.Max(0f, _ringWindowEnd - now):F0}s.");
            }
            return;
        }

        // PLACED. Either the join seat (first placement) or the ONE allowed correction. A
        // correction needs something genuinely new: a peer we could not see before, or a footprint
        // that grew under the seat we already wrote.
        bool correction = _ringPlaced;
        if (correction)
        {
            bool newPeer = seat.PeerCount > _ringPeersAtPlacement;
            bool grew = _ringTilesAtPlacement > 0
                        && seat.TileCount > _ringTilesAtPlacement * RingFootprintGrowthFactor;
            if (!newPeer && !grew)
                return; // nothing new to correct with — never re-write a settled seat
        }

        Vector3 head = ApplyRingSeat(seat);
        _ringPlaced = true;
        // REMEMBER WHICH SIDE OF THE TABLE THE PLAYER WAS GIVEN. This is the only writer of the
        // arrival azimuth, and it is deliberately HERE rather than inside ApplyRingSeat: the B+Y
        // recenter also applies a seat through that method, and a recenter must never be able to
        // promote its own azimuth into "the seat you spawned at". The one allowed correction
        // overwrites it, which is right — the corrected seat IS the arrival seat.
        _ringSeatAngleDegrees = seat.AngleDegrees;
        _ringSeatAngleValid = true;
        _ringPeersAtPlacement = seat.PeerCount;
        _ringTilesAtPlacement = seat.TileCount;
        _ringNextLogTime = now + RingLogIntervalSeconds;

        // VERIFY THE OUTCOME, NOT THE PATH. Everything above describes what was ASKED for. This
        // re-reads the head the player actually ends up with — after RigClamp, after the transform
        // write, through the real camera transform — and re-runs the acceptance test on it. A seat
        // that was computed and a seat that frames the board are two different claims, and only the
        // second one is the requirement.
        SpawnRing.Framing achieved = SpawnRing.Frame(board, _camera.transform.position, scale);

        // HW-VERIFY: THE PROOF LINE. It states the picture the player got — clear of the board by
        // how much, how high above it, and how much of the view the play field takes — not merely
        // that a seat was solved. If the next hardware round still reports spawning inside the
        // board, this line either did not appear (the ring never ran: read the "waiting for the
        // board" / "window closed" lines instead) or it appeared with ACHIEVED saying OVER THE
        // BOARD, which localises the defect to the geometry rather than to the trigger.
        VRLog.Note("Rig", $"Spawn ring: SEATED{(correction ? " (CORRECTION)" : "")}" +
                          $"{(overridingPlayer ? " [OVER THE PLAYER'S OWN MOVEMENT — they were inside the diorama, which is an escape, not a choice]" : "")}" +
                          $"{(seat.Solo ? " [SOLO — no peers, azimuth from the scenario base yaw; the ring supplies the RADIUS so the seat cannot land on the board]" : "")} at azimuth " +
                          $"{seat.AngleDegrees:F1}deg, nearest peer {seat.MinGapDegrees:F1}deg away " +
                          $"(180 = straight across the board), facing the board centre {seat.Center}. " +
                          $"Radius {seat.RadiusMeters:F2} m ({seat.RadiusWorld:F2} world units) = board " +
                          $"edge {seat.EdgeMeters:F2} m along that direction + {seat.ClearanceMeters:F2} m " +
                          $"clearance{(seat.FramingSteps > 0 ? $" [BACKED OFF {seat.FramingSteps} step(s) to fit the play field in view]" : "")}" +
                          $"{(seat.RadiusRaisedToFloor ? $" [raised to the {SpawnRing.MinRadiusMeters:F2} m table-edge floor]" : "")}; " +
                          $"board half-extents {seat.BoardHalfMeters.x:F2}x{seat.BoardHalfMeters.y:F2} m over " +
                          $"{seat.TileCount} tile(s). SOLVED FOR: {seat.Framing}. ACHIEVED: {achieved} " +
                          $"=> {(achieved.Acceptable ? "REQUIREMENT MET" : "REQUIREMENT NOT MET")}. " +
                          $"{probe}{(seat.FromIndexFallback ? " — INDEX FALLBACK (no peer pose yet; the one correction will refine it)" : "")}. " +
                          $"Head at {head}, rig root at {_rigRoot.transform.position}.");

        if (correction || overridingPlayer)
            CloseRingWindow(overridingPlayer
                ? "the one seat allowed over the player's own movement has been spent — the ring is done"
                : "the one allowed correction has been spent — the seat is final");
    }

    /// <summary>Human-readable WHY for a non-placing outcome (log text only).</summary>
    private static string Explain(SpawnRing.Outcome outcome) => outcome switch
    {
        SpawnRing.Outcome.Offline =>
            "unreachable since 2026-08-03: single player takes the same ring (see SpawnRing.Outcome.Offline)",
        SpawnRing.Outcome.PeersUnknown =>
            "multiplayer, but no peer position is known yet: no peer rig packet has arrived and the " +
            "FFSNet participant list is still empty (it lags the join handshake by seconds)",
        SpawnRing.Outcome.BoardPending =>
            "the scenario's hex tiles are not in the object cache yet — no board to sit around",
        _ => outcome.ToString(),
    };

    /// <summary>
    /// Close the spawn-ring window for good and say why. Called on every terminal path: window
    /// expiry, the spent correction, the config gate, and the player placing themselves somewhere
    /// the ring accepts. Idempotent, and silent once latched.
    ///
    /// <para>AT THE <c>Note</c> TIER ON PURPOSE. This is the ring's terminal verdict, one line per
    /// scenario, and it is what a hardware round reads when the answer is "it did nothing".</para>
    /// </summary>
    /// <param name="reason">Why the window closed — printed verbatim.</param>
    /// <param name="alert">True for the one expiry that is a genuine defect rather than a correct
    /// outcome (a measurable board and no seat).</param>
    private void CloseRingWindow(string reason, bool alert = false)
    {
        if (_ringSettled)
            return;
        _ringSettled = true;
        if (_kind != RigKind.Scenario)
            return;

        string line = $"Spawn ring: window closed — {reason}. " +
                      $"{(_ringPlaced ? "Seated" : "Never seated")} after {_ringAttempts} attempt(s); " +
                      $"board {(_ringBoardSeen ? $"was measurable ({_ringTileCount} tile(s))" : "NEVER appeared")}" +
                      $"{(_ringPlayerMoved ? $"; the player had moved themselves ({_ringPlayerMovedWhat})" : "")}.";
        if (alert)
            // HW-VERIFY: the ring had a board and still left the player on the board-blind seat.
            // This is the one line that says the round-4 fix itself failed.
            VRLog.Alert("Rig", line);
        else
            // HW-VERIFY: the ring's terminal verdict. When a hardware round reports a bad spawn and
            // no SEATED line exists, this line names the reason there is none.
            VRLog.Note("Rig", line);
    }

    /// <summary>
    /// The player moved themselves — world grab, stick turn, stick flight, manual recenter.
    ///
    /// <para>THIS IS NO LONGER A KILL SWITCH, and that change is the round-4 fix. It used to close
    /// the ring's window on the first frame of any locomotion, which on 2026-09-02 ended the whole
    /// feature ~35 frames into a scenario, before the board had even reached the object cache —
    /// because the player was flying to get OUT of the board they had been dropped into. A fuse
    /// that counts the escape as consent protects the bug.</para>
    ///
    /// <para>What it does now depends on whether the ring has already done its job:
    /// <list type="bullet">
    /// <item>SEATED ALREADY ⇒ close immediately, exactly as before. The player is adjusting a
    /// vantage the ring gave them and nothing may move them again.</item>
    /// <item>NOT SEATED YET ⇒ record it. <see cref="TickSpawnRingSettle"/> judges it against the
    /// board once there IS a board, and only overrules the player if their head is down among the
    /// tiles.</item>
    /// </list></para>
    ///
    /// <para>Static and cheap (one null check plus an already-latched early-out) because it is
    /// called from per-frame locomotion paths; the log line below is guarded by
    /// <c>_ringPlayerMoved</c> and therefore fires at most once per scenario.</para>
    /// </summary>
    internal static void NotifyPlayerLocomotion(string what)
    {
        VRRigDriver? drv = Instance;
        if (drv == null || drv._ringSettled)
            return;

        if (drv._ringPlaced)
        {
            drv.CloseRingWindow($"the player moved themselves ({what}) after the ring had seated " +
                                "them — the vantage is theirs from here");
            return;
        }

        if (drv._ringPlayerMoved)
            return;

        drv._ringPlayerMoved = true;
        drv._ringPlayerMovedWhat = what;
        if (drv._kind == RigKind.Scenario)
            // HW-VERIFY: the moment the old fuse used to blow. If a hardware round shows this line
            // followed by neither a SEATED nor a STANDING DOWN line, the ring is stuck waiting for
            // a board that never arrives — which is a different defect from the one this fixes.
            VRLog.Note("Rig", $"Spawn ring: the player moved themselves ({what}) before a seat was " +
                              "placed. NOT closing the window — a player who was dropped inside the " +
                              "board flies to get out of it, and that is the symptom, not consent. " +
                              "The move is judged against the play field as soon as the board can be " +
                              "measured: clear of it and the vantage is theirs, inside it and the ring " +
                              "seats them once.");
    }

    /// <summary>
    /// Menu recenter: put the head back at the menu camera's authored vantage (1:1).
    /// Sign convention (verified against hardware test #3 logs): rig = anchor − yaw·headLocal
    /// puts head world = rig + yaw·headLocal = anchor exactly. With floor-origin
    /// tracking headLocal.y ≈ eye height, so the rig root legitimately sits ~1.1–1.7 m
    /// BELOW the anchor.
    /// </summary>
    private void RecenterMenu()
    {
        if (_rigRoot == null || _camera == null)
            return;
        _rigRoot.transform.rotation = _menuAnchorYaw;
        // Offset with the NEW yaw applied (rig scale is 1 in the menu).
        Vector3 headOffsetWorld = _menuAnchorYaw * _camera.transform.localPosition;
        _rigRoot.transform.position = _menuAnchorPos - headOffsetWorld;
        RigPoseVersion++;
        VRLog.Info("Rig", $"Menu rig recentered at the menu camera vantage (anchor {_menuAnchorPos}, " +
                          $"head local {_camera.transform.localPosition}, rig root {_rigRoot.transform.position}).");
    }

    /// <summary>
    /// Base diorama scale, ALWAYS derived from the runtime hex tile size
    /// (<c>UnityGameEditorRuntime.s_TileSize</c>, BOARD-INPUT §2: x = hex width in
    /// world units) so one hex reads as ~15 cm on the table. Falls back to 12× when the
    /// tile size isn't initialized yet (outside a scenario). The old <c>[Rig] WorldScale</c>
    /// manual override is LEGACY (user ruling 2026-08, "Tischgröße" removed): the auto
    /// derivation was the default all along, and the table size a player tunes is the
    /// two-hand pinch gesture ([Comfort] SavedScaleMultiplier) on top of this base.
    /// </summary>
    private static float ResolveWorldScale()
    {
        float tileSize = UnityGameEditorRuntime.s_TileSize.x;
        if (tileSize <= 0.001f)
            return FallbackWorldScale;

        return Mathf.Clamp(tileSize / TargetHexSizeMeters, 1f, 100f);
    }
}
