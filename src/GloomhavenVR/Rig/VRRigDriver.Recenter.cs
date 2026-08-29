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
    /// <para>DELIBERATELY WITHOUT THE SPAWN RING. This is the entry point a HUMAN pulls (B+Y hold,
    /// the dev key), and re-solving the multiplayer ring here would teleport a player around the
    /// table on an unrelated request. (It used to be reachable from a config change too — the
    /// [Comfort] TableHeightOffset handler re-ran recenter live; that setting is gone, so today
    /// EVERY caller is a deliberate human act, which only strengthens the rule.)
    /// The ring is a JOIN placement, not a recenter behaviour — it lives entirely in
    /// <see cref="TickSpawnRingSettle"/>. A manual recenter keeps the azimuth the player is
    /// currently at, exactly what it has always done, and it also CLOSES the ring's window: a
    /// player who has just placed themselves by hand must never be moved again.</para>
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
    /// Reposition the rig so the player's CURRENT head pose ends up at the FIXED table-edge
    /// spot: eyes <see cref="ComfortSettings.EffectiveEyeHeightMeters"/> (real) above the orbit
    /// focus plane and <see cref="ComfortSettings.EffectiveEyeBackMeters"/> back — both the bare
    /// STANDING preset (0.30 above / 0.70 back), since there is no seated preset any more and, since the
    /// 2026-08 ruling, no [Comfort] TableHeightOffset to add to the height either. Called
    /// automatically on the first tracked pose, and bound to the B+Y hold chord (see
    /// <see cref="Comfort"/>).
    ///
    /// <para>NOT CONFIGURABLE, ON PURPOSE. The seat is one constant pose so that "recenter" means
    /// the same thing every time — the deterministic way back to the table from wherever free
    /// locomotion left you. Eye height is the player's own business now: the stick (see
    /// <see cref="Flight"/>) and the world grab move them up, down and through the scene at will,
    /// which is precisely why the height dial was removed rather than kept alongside them.</para>
    ///
    /// <para>UNCONDITIONAL BEHAVIOUR: this method no longer knows the ring exists — do not give it
    /// a <c>useSpawnRing</c> flag back. Why that shape failed is recorded once, at
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

        // World tilt composition: the seat math below is authored for a yaw-only rig
        // (seatYaw reads the current rotation; offsets assume a level horizon). Flatten the
        // tilt out first — TickWorldTilt re-applies the configured tilt on top of the fresh
        // seat in LateUpdate this same frame, so a recenter lands at the standard table-edge
        // seat viewed through the tilt, with no untilted frame ever rendered.
        if (_tiltActive)
            _rigRoot.transform.rotation = YawOnly(_rigRoot.transform.rotation);
        // Attribute this frame's tilt re-apply (the LateUpdate heal after the flatten
        // above) to the recenter in the WorldTilt change log — and let it act as a
        // MASKED RE-AIM event: TickWorldTilt instantly re-aims the tilt at the current
        // view yaw, exactly what Demeo's recenter does by absorbing the head yaw into
        // the avatar root (InputTracking.Recenter / AvatarController.cs:684-694).
        _axisSnapReason = "recenter";

        Quaternion seatYaw = _rigRoot.transform.rotation;
        Vector3 desiredHeadWorld = controller.FocusPoint
                                   + seatYaw * (Vector3.back * (ComfortSettings.EffectiveEyeBackMeters * scale))
                                   + Vector3.up * (ComfortSettings.EffectiveEyeHeightMeters * scale);

        Vector3 headOffsetWorld = seatYaw * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;
        RigClamp.Apply(_rigRoot.transform);
        RigPoseVersion++; // P6: world-anchored panels re-derive their seat yaw on recenter

        VRLog.Info("Rig", $"Recentered — head at {desiredHeadWorld}, rig root at " +
                          $"{_rigRoot.transform.position} (table-edge seat).");
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
    /// <returns>The world head position the seat was written for (log material).</returns>
    private Vector3 ApplyRingSeat(in SpawnRing.Seat seat)
    {
        float scale = _rigRoot!.transform.localScale.x;

        if (_tiltActive)
            _rigRoot.transform.rotation = YawOnly(_rigRoot.transform.rotation);
        _axisSnapReason = "spawn ring seat";

        Quaternion seatYaw = seat.Yaw * Quaternion.Inverse(YawOnly(_camera!.transform.localRotation));
        _rigRoot.transform.rotation = seatYaw;

        // RING SEATS SPAWN RAISED (user ruling 2026-08-04: "Heb den Spawn-Ring etwas an, ich will
        // dass alle Spieler etwas höher als das Spielfeld selber spawnen"). The lift is ON TOP of
        // the standing eye height and applies to RING seats only — the B+Y recenter chord keeps
        // the plain table-edge seat, because that gesture means "put me back AT the table", while
        // the ring is an ARRIVAL pose: a slightly elevated vantage reads the whole field at a
        // glance, and with stick flight the player descends in a second if they want to. Real
        // metres (times rig scale), same unit as the eye-height preset beside it.
        Vector3 desiredHeadWorld = seat.HeadFlat
                                   + Vector3.up * ((ComfortSettings.EffectiveEyeHeightMeters
                                                    + SpawnRing.RingSeatLiftMeters) * scale);
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
    /// <para>THE WINDOW (opened at the FIRST TRACKED POSE, not at rig build — the round-1 window
    /// was largely consumed by the scenario load before the player was even tracked) is what keeps
    /// this from being a leash. Inside it we retry while the board or the peers are still coming
    /// up, place ONCE, and allow at most ONE correction if a peer's first pose lands after we were
    /// seated. It also closes EARLY the moment the player moves themselves
    /// (<see cref="CloseRingWindow"/>) — from that instant their own locomotion is authoritative
    /// and nothing may move them again.</para>
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

        if (Time.unscaledTime >= _ringWindowEnd)
        {
            CloseRingWindow(_ringPlaced
                ? $"the {SpawnRingSettleSeconds:F0}s window closed on a placed seat — it is final now"
                : $"the {SpawnRingSettleSeconds:F0}s window closed WITHOUT a placement after " +
                  $"{_ringAttempts} attempt(s); last reason: {_ringOutcome} ({_ringProbe}). " +
                  "The ordinary table-edge seat stands");
            return;
        }

        _ringAttempts++;
        float scale = _rigRoot.transform.localScale.x;
        SpawnRing.Outcome outcome = SpawnRing.Solve(controller.FocusPoint, _scenarioBaseYaw, scale,
                                                    out SpawnRing.Seat seat, out SpawnRing.Probe probe);
        bool outcomeChanged = outcome != _ringOutcome || _ringAttempts == 1;
        _ringOutcome = outcome;
        _ringProbe = probe;

        if (outcome != SpawnRing.Outcome.Placed)
        {
            // NOT PLACED — and that is a logged event, not silence. Offline latches nothing (a
            // session can come online while a scenario already runs: the 2026-08-02 host log shows
            // exactly that host, alone in its scenario, going online ~600 log lines after its
            // recenter), so we keep polling the window out; we simply do not spam it. Offline is
            // logged on CHANGE only — single player must not repeat the same line ten times per
            // scenario — while the two transient "still coming up" reasons do beat, because how
            // long they persist is exactly what a future test needs to see.
            if (outcomeChanged
                || (outcome != SpawnRing.Outcome.Offline && Time.unscaledTime >= _ringNextLogTime))
            {
                _ringNextLogTime = Time.unscaledTime + RingLogIntervalSeconds;
                VRLog.Info("Rig", $"Spawn ring: NOT SEATED ({Explain(outcome)}) — attempt " +
                                  $"{_ringAttempts}, {probe}. Keeping the ordinary table-edge seat; " +
                                  $"retrying for another {Mathf.Max(0f, _ringWindowEnd - Time.unscaledTime):F0}s.");
            }
            return;
        }

        // PLACED. Either the join seat (first placement) or the ONE allowed correction.
        bool correction = _ringPlaced;
        if (correction && seat.PeerCount <= _ringPeersAtPlacement)
            return; // nothing new to correct with — never re-write a settled seat

        Vector3 head = ApplyRingSeat(seat);
        _ringPlaced = true;
        _ringPeersAtPlacement = seat.PeerCount;
        _ringNextLogTime = Time.unscaledTime + RingLogIntervalSeconds;

        // THE HARDWARE-LOG PROOF LINE: everything needed to verify the placement from a log alone —
        // the chosen azimuth, the angular distance it achieved (180° = straight across from a lone
        // peer), where the radius came from, the footprint it was measured on, and the evidence.
        VRLog.Info("Rig", $"Spawn ring: SEATED{(correction ? " (CORRECTION)" : "")}" +
                          $"{(seat.Solo ? " [SOLO — no peers, azimuth from the scenario base yaw; the ring supplies the RADIUS so the seat cannot land on the board]" : "")} at azimuth " +
                          $"{seat.AngleDegrees:F1}deg, nearest peer {seat.MinGapDegrees:F1}deg away " +
                          $"(180 = straight across the board), facing the board centre {seat.Center}. " +
                          $"Radius {seat.RadiusMeters:F2} m ({seat.RadiusWorld:F2} world units) = board " +
                          $"edge {seat.EdgeMeters:F2} m along that direction + {SpawnRing.EdgeClearanceMeters:F2} m " +
                          $"standing clearance{(seat.RadiusClamped ? $" [CLAMPED to {SpawnRing.MinRadiusMeters:F2}..{SpawnRing.MaxRadiusMeters:F2} m]" : "")}; " +
                          $"board half-extents {seat.BoardHalfMeters.x:F2}x{seat.BoardHalfMeters.y:F2} m. " +
                          $"{probe}{(seat.FromIndexFallback ? " — INDEX FALLBACK (no peer pose yet; the one correction will refine it)" : "")}. " +
                          $"Head at {head}, rig root at {_rigRoot.transform.position}.");

        if (correction)
            CloseRingWindow("the one allowed correction has been spent — the seat is final");
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
    /// expiry, the spent correction, the config gate, and — the "do not fight the player" rule —
    /// the first time the player moves themselves (<see cref="NotifyPlayerLocomotion"/>).
    /// Idempotent, and silent once latched.
    /// </summary>
    private void CloseRingWindow(string reason)
    {
        if (_ringSettled)
            return;
        _ringSettled = true;
        if (_kind == RigKind.Scenario)
            VRLog.Info("Rig", $"Spawn ring: window closed — {reason}. " +
                              $"{(_ringPlaced ? "Seated" : "Never seated")} after {_ringAttempts} attempt(s).");
    }

    /// <summary>
    /// The player moved themselves — world grab, stick turn, manual recenter. The spawn ring is a
    /// JOIN placement and nothing more, so this closes its window permanently: after this, only
    /// the player's own locomotion ever moves the player. Static and cheap (one null check plus an
    /// already-latched early-out) because it is called from per-frame locomotion paths.
    /// </summary>
    internal static void NotifyPlayerLocomotion(string what)
    {
        VRRigDriver? drv = Instance;
        if (drv != null && !drv._ringSettled)
            drv.CloseRingWindow($"the player moved themselves ({what})");
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
