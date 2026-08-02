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
    /// the dev key) plus the [Comfort] TableHeightOffset change handler, and re-solving the
    /// multiplayer ring here would teleport a player around the table on an unrelated slider edit.
    /// The ring is a JOIN placement, not a recenter behaviour: it is applied once, by the rig's own
    /// first-pose recenter, and at most once more inside the settle window (see
    /// <see cref="TickSpawnRingSettle"/>). A manual recenter keeps the azimuth the player is
    /// currently at — exactly what it has always done.</para>
    /// </summary>
    internal static void RequestRecenter() => Instance?.Recenter();

    /// <summary>
    /// Reposition the rig so the player's CURRENT head pose ends up at the configured
    /// table-edge spot: eyes <see cref="ComfortSettings.EffectiveEyeHeightMeters"/> (real)
    /// above the orbit focus plane and <see cref="ComfortSettings.EffectiveEyeBackMeters"/>
    /// back (the STANDING preset + [Comfort] TableHeightOffset — there is no seated preset any
    /// more, see <see cref="ComfortSettings.StandingEyeHeightMeters"/>). Called automatically
    /// on the first tracked pose, and bound to the B+Y hold chord (see <see cref="Comfort"/>).
    /// </summary>
    /// <param name="useSpawnRing">
    /// True only for the JOIN placements (first-pose recenter and the one settle-window
    /// correction): solve the multiplayer spawn ring and seat the player in the largest free wedge
    /// around the board instead of at the shared table-edge spot. False — every other caller —
    /// keeps the historical behaviour byte for byte.
    /// </param>
    internal void Recenter(bool useSpawnRing = false)
    {
        if (_rigRoot == null || _camera == null)
            return;

        if (_kind == RigKind.Menu)
        {
            RecenterMenu();
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return;

        float scale = _rigRoot.transform.localScale.x;

        // SPAWN RING (multiplayer join comfort — see SpawnRing for the full WHY), solved BEFORE
        // anything is written so a solve that cannot place can still abort the whole recenter
        // below without having disturbed the rig.
        //
        // Only the join placements ask for it. Single-player, the SpawnInCircle-off case and every
        // other caller keep the EXACT prior behaviour: rotation untouched, seat direction = current
        // yaw, seat point = the orbit focus. When it DOES apply, both the seat direction and the
        // facing come out of the solve: the head lands on a ring around the BOARD's own centre at
        // the free-est azimuth, looking at that centre. The re-seated head world pose is then
        // broadcast unchanged (the embodiment sync is world-frame), so peers see us arrive at the
        // free seat with no extra wire traffic.
        SpawnRing.Seat seat = default;
        bool ringAttempted = useSpawnRing && Plugin.SpawnInCircle.Value;
        bool ringApplied = false;
        if (ringAttempted)
        {
            _ringOutcome = SpawnRing.Solve(controller.FocusPoint, _scenarioBaseYaw, scale, out seat);
            ringApplied = _ringOutcome == SpawnRing.Outcome.Placed;

            // ALREADY SEATED AND THE RING CANNOT PLACE ⇒ DO NOTHING AT ALL. Falling through to the
            // ordinary table-edge seat here would drag a player who is already standing where the
            // ring put them back to the shared spot — the precise "never fight the player" rule
            // this feature is bound by. Only the very first (deferred) placement is allowed to
            // land on the ordinary seat, and at that point the player has not been placed yet.
            if (!ringApplied && _ringPlaced)
            {
                VRLog.Info("Rig", $"Spawn ring: correction skipped — solve returned {_ringOutcome}; " +
                                  $"leaving the player where they are.");
                return;
            }
        }

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
        Vector3 desiredHeadWorld;
        if (ringApplied)
        {
            // FACE THE BOARD, LITERALLY. The user's complaint was two-part: "spawnt man direkt
            // hinter oder IN der anderen Maske UND MUSS SICH ERST AUSRICHTEN". Writing seat.Yaw
            // straight onto the rig only fixes the first half — the player's VIEW is
            // rigRotation ∘ headLocalRotation, so someone physically turned away at the moment of
            // the join would still arrive looking at the wall. Absorbing the head's own yaw into
            // the rig (the masked re-aim Demeo performs in InputTracking.Recenter /
            // AvatarController.cs:684-694, and the reason _axisSnapReason is set above) makes the
            // HEAD's world yaw exactly seat.Yaw, whatever direction the player is standing in.
            // The position write below is unaffected: it derives the rig root from the desired
            // HEAD pose, using this same rotation for the head offset.
            seatYaw = seat.Yaw * Quaternion.Inverse(YawOnly(_camera.transform.localRotation));
            _rigRoot.transform.rotation = seatYaw;
            desiredHeadWorld = seat.HeadFlat + Vector3.up * (ComfortSettings.EffectiveEyeHeightMeters * scale);
        }
        else
        {
            desiredHeadWorld = controller.FocusPoint
                               + seatYaw * (Vector3.back * (ComfortSettings.EffectiveEyeBackMeters * scale))
                               + Vector3.up * (ComfortSettings.EffectiveEyeHeightMeters * scale);
        }

        Vector3 headOffsetWorld = seatYaw * (_camera.transform.localPosition * scale);
        _rigRoot.transform.position = desiredHeadWorld - headOffsetWorld;
        RigClamp.Apply(_rigRoot.transform);
        RigPoseVersion++; // P6: world-anchored panels re-derive their seat yaw on recenter

        if (ringApplied)
        {
            _ringPlaced = true;
            _ringPeersAtPlacement = seat.PeerCount;
            // THE HARDWARE-LOG PROOF LINE: everything needed to verify the placement from a log
            // alone — the chosen azimuth, the minimum angular distance it achieved, the ring
            // radius and where it came from, the board centre, and how many peers were known.
            VRLog.Info("Rig", $"Spawn ring: seated at azimuth {seat.AngleDegrees:F1}deg, min angular " +
                              $"distance to peers {seat.MinGapDegrees:F1}deg, ring radius " +
                              $"{seat.RadiusMeters:F2} m ({seat.RadiusWorld:F2} world units, from board " +
                              $"footprint radius {seat.BoardRadiusMeters:F2} m + reach " +
                              $"{SpawnRing.ReachMarginMeters:F2} m), board centre {seat.Center}, " +
                              $"{seat.PeerCount} peer pose(s) known of {seat.ParticipantCount - 1} peer(s) in " +
                              $"the session{(seat.FromIndexFallback ? " — INDEX FALLBACK (no peer pose yet)" : "")}. " +
                              $"Head at {desiredHeadWorld}, rig root at {_rigRoot.transform.position}.");
        }
        else
        {
            VRLog.Info("Rig", $"Recentered — head at {desiredHeadWorld}, rig root at {_rigRoot.transform.position} " +
                              $"(table-edge seat{(ringAttempted ? $", spawn ring deferred: {_ringOutcome}" : "")}).");
        }
    }

    /// <summary>
    /// The spawn ring's bounded settle window, polled every
    /// <see cref="CircleReseatIntervalFrames"/> frames while a scenario rig lives. It exists for
    /// the two degenerate cases the join placement cannot solve on its own, and it does NOTHING
    /// else — in particular it never re-seats a settled player.
    ///
    /// <list type="number">
    ///   <item><b>Board / session not ready yet.</b> The rig can be built (and the first tracked
    ///     pose can arrive) before the scenario's hex tiles are in the object cache or before the
    ///     FFSNet participant list is populated. The placement is then deferred: we keep the
    ///     ordinary table-edge seat and retry here — but only once
    ///     <see cref="SpawnRing.Ready"/> says a solve would actually place, so a single-player
    ///     session never gets a speculative recenter thrown at it.</item>
    ///   <item><b>A peer arrived after we were seated.</b> Peers stream at
    ///     <see cref="Net.NetProtocol.SendRateHz"/>, so a player who joins first (or faster) can be
    ///     placed while knowing nobody. Exactly ONE correction is allowed, only while the window is
    ///     open, only when the number of known peer poses actually grew, and it is logged.</item>
    /// </list>
    ///
    /// <para>When the window closes — or when the one correction is spent — <c>_ringSettled</c>
    /// latches and this method is a pure early-out for the rest of the scenario. From that moment
    /// the player's own locomotion is the only thing that moves them.</para>
    /// </summary>
    private void TickSpawnRingSettle()
    {
        if (_ringSettled)
            return;

        if (Time.unscaledTime >= _ringWindowEnd)
        {
            _ringSettled = true;
            // Only the "we wanted to place and never could" case is worth a line; single-player
            // is a silent no-op by design and must not log once per scenario.
            if (!_ringPlaced && _ringOutcome == SpawnRing.Outcome.BoardPending)
                VRLog.Info("Rig", $"Spawn ring: gave up after {SpawnRingSettleSeconds:F0}s — the board's " +
                                  $"tiles never appeared; keeping the ordinary table-edge seat.");
            return;
        }

        if (!_ringPlaced)
        {
            if (SpawnRing.Ready())
                Recenter(useSpawnRing: true);
            return;
        }

        int peers = SpawnRing.KnownPeerCount();
        if (peers <= _ringPeersAtPlacement)
            return;

        _ringSettled = true; // the ONE correction — spent whether or not the solve places again
        VRLog.Info("Rig", $"Spawn ring: correcting the join seat — {peers} peer pose(s) known now, " +
                          $"{_ringPeersAtPlacement} when we were seated. This is the one correction the " +
                          $"settle window allows; the seat is final afterwards.");
        Recenter(useSpawnRing: true);
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
