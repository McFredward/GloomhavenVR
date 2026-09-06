using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    // ───────────────────────────────────────────────── TRACKING-ORIGIN JUMP GUARD ──
    //
    // USER REPORT 2026-08-03: "Wenn ich die Brille absetze und wieder aufsetze befinde ich mich
    // oft an einem anderen Ort als zuvor." — with the correct caveat that it may be the runtime's
    // doing. It is: on a doff/don a runtime is free to re-establish its LOCAL space, which moves
    // the tracking origin under us. Our rig root did not move, so the same physical spot now maps
    // to a different world spot and the player finds themselves somewhere else.
    //
    // WHY WE CAN FIX IT ANYWAY. The runtime owns the ORIGIN; we own the RIG ROOT. A re-origin is
    // observable without any runtime cooperation: the head's pose IN TRACKING SPACE
    // (camera.localPosition/localRotation) jumps discontinuously in one frame — a distance no
    // neck can travel in a frame. When that happens we shift the rig root by the inverse, so the
    // head's WORLD pose is continuous across the event and the player stays exactly where they
    // were. This needs no presence event, which matters: the hardware log of this very report
    // has three "[Core] User presence lost" lines and NO "Session resumed" line — the runtime
    // hid the don from us, exactly as it did in the lost-board incident. An event-driven fix
    // would have nothing to react to.
    //
    // WHY IT DOES NOT BUMP RigPoseVersion. That version means "the tracking origin changed, carry
    // rig-relative things along" — and things keyed on it (a PINNED control board) are carried by
    // their RIG-RELATIVE pose. Here the whole point is that the player did NOT move in the world:
    // a world-anchored board must stay exactly where it is. Bumping the version would drag it.
    //
    // FALSE-POSITIVE DISCIPLINE. A tracking blip that jumps out and back would, if compensated
    // immediately, leave the world permanently offset by the blip. So a jump is only ARMED here
    // and must still be there ConfirmFrames later, having neither returned to the pre-jump pose
    // nor jumped again; only then is it compensated, using the world pose recorded BEFORE the
    // jump. The cost of that caution is ~5 frames of displacement on a real re-origin, which is
    // far below the "I am somewhere else now" the guard exists to prevent.
    //
    // ───────────────────────────── THE FLIP REFUSAL (user report 2026-09-06, item 10) ──
    //
    // USER REPORT: a co-player running the SteamVR overlay OVR Advanced Settings with "Space
    // Drag" on the right thumbstick "hat sich immer auf den Kopf gestellt und sieht dann die Welt
    // komplett upside-down". Recentring cleared it; it came back. As a side effect the control
    // board "ist unter die Map geglitched und wir konnten es nicht mehr finden".
    //
    // WHAT THE HARDWARE LOG SHOWS (ModBuild 459, co-player .planning/debug/remote/Player.log;
    // the HOST log has ZERO of every token below, so this is entirely one-sided):
    //   - 17 `Tracking-origin JUMP suspected`, 1 `CANCELLED`, 16 `TRACKING ORIGIN CHANGED`;
    //   - 16 of the 17 report `turned` = 179-180° in ONE frame, and `turned` is
    //     Quaternion.Angle — AXIS-AGNOSTIC, a total rotation, not a yaw;
    //   - the dYaw those same 16 events actually APPLIED is 2, 0, 2, -1, -140, -2, 17, 0, 75, 0,
    //     -43, -1, 15, 0, -34, -2 degrees — nine of sixteen within ±2° of zero.
    // A 180° total rotation with a ~0° yaw component is 180° of ROLL/PITCH. That is the flip,
    // measured, in TRACKING space, sixteen times — it is upstream of us, in the tracking origin,
    // and no rig-root write can be blamed for it (every rig-root rotation writer in this mod is
    // yaw-only: Quaternion.Euler(0,y,0), YawOnly(), seatYaw, and this guard's own world-up
    // RotateAround — none can introduce roll).
    //   - `[Core] User presence lost` sits at 16:56 and 17:30 while the storm runs 17:16-17:25,
    //     so the compensator's printed cause ("typical after an HMD doff/don") is FALSE for all
    //     16 — the headset was never doffed. The 17 jumps instead match 1:1, by timestamp, with
    //     17 bursts of `xrEndFrame: XR_ERROR_HANDLE_INVALID` against an OpenXR session whose
    //     Instance/Session/System ids never change: a foreign process re-establishing the space.
    //
    // WHY THE GUARD MADE IT WORSE. Its remedy vocabulary is (world-up yaw, translation). It
    // cannot express a roll, and by the yaw-only standing ruling it must not. But it ARMS on the
    // axis-agnostic `turned` and on `moved` (1.2-5.0 m here), so it fires anyway and applies the
    // half it can: it drags the rig root until the head's WORLD POSITION is back where it was —
    // 7.4 to 91.7 world units, almost all in Y, sign-flipping the rig root every time (36.65 →
    // -13.05, 45.63 → -43.74, 39.48 → -52.17). At the co-player's rig scale ×18.45 those are
    // 0.4-5.0 REAL metres, i.e. the honest inverse of the local jump — the guard is arithmetically
    // correct and semantically wrong. The player ends up upside down AND re-seated, and the
    // control board, which is placed HEAD-RELATIVE, follows an inverted head under the map.
    //
    // THE REFUSAL. A jump whose rotation is mostly NOT yaw is one this guard can only half-apply,
    // and half of this correction is worse than none: the flip stays either way, and applying the
    // translation is what loses the world and the board. So when the head's UP vector swings more
    // than OriginFlipDegrees in the ONE frame that would arm the guard, we refuse outright —
    // re-baseline and say so, loudly, at Note tier. Strictly FEWER transform writes than before;
    // no new behaviour, only a withheld one.
    //
    // WHAT THIS DOES NOT DO — and the honest answer to "kann man immun gegen Space-Drag sein?".
    // It does NOT make the player immune to the flip. The roll arrives on the camera's LOCAL
    // rotation, written by Unity's stock TrackedPoseDriver (VRRigDriver.HeadCamera.cs) from the
    // device pose; the mod composes no head pose of its own and owns no seam between the runtime
    // and that write, so there is nothing to flatten. Cancelling it would mean counter-rolling
    // the rig root by a sustained head roll — which cannot be told from a player who leans, is a
    // new behaviour with its own failure mode, and was NOT built here on the user's explicit
    // scope instruction ("ich will aber nicht zu viel Energie da rein stecken"). What is bought
    // cheaply is that the MOD stops amplifying the runtime's flip into a lost world and a lost
    // board; the player's own recentre then clears the flip, exactly as it already did.

    /// <summary>Head tracking-space movement in ONE frame beyond which the origin, not the
    /// player, moved. A neck at a sprint does ~2 m/s; at 90 Hz that is 22 mm per frame, so 0.5 m
    /// is ~20× any real motion and still far under a typical re-origin.</summary>
    private const float OriginJumpMeters = 0.5f;

    /// <summary>Head tracking-space YAW change in one frame with the same reasoning (a fast head
    /// turn is ~400 °/s ⇒ ~4.5° per frame at 90 Hz).</summary>
    private const float OriginJumpDegrees = 45f;

    /// <summary>Frames a jump must persist before it is compensated (see the discipline note).</summary>
    private const int OriginConfirmFrames = 5;

    /// <summary>How close to the pre-jump tracking pose counts as "it came back" (metres).</summary>
    private const float OriginReturnMeters = 0.15f;

    /// <summary>Head UP-vector swing in ONE frame, in TRACKING space, beyond which the jump is a
    /// ROLL/PITCH flip this guard cannot correct — see the flip-refusal note above. A neck at a
    /// sprint does ~400 °/s, i.e. ~5.5° per frame at 72 Hz, so 60° is ~11× any real motion and far
    /// under the 179-180° the hardware log recorded 16 times. Measured on the up VECTOR, not on
    /// eulerAngles.z, because roll and yaw degenerate in Euler decomposition at the near-90° pitch
    /// of a player looking down at the board — the mod's most ordinary posture.</summary>
    private const float OriginFlipDegrees = 60f;

    /// <summary>Minimum seconds between full flip-refusal lines. Past the throttle nothing is
    /// lost: every line carries the running total, so a burst is summarised, not silenced.</summary>
    private const float FlipLogThrottleSeconds = 5f;

    /// <summary>The camera/rig-root the remembered sample belongs to, and the RigPoseVersion it was
    /// taken under. ANY of the three changing invalidates the sample — see the note in
    /// <see cref="TickOriginGuard"/>.</summary>
    private Camera? _originCamera;
    private GameObject? _originRig;
    private int _originPoseVersion = -1;

    private bool _originKnown;
    private Vector3 _lastHeadLocal;
    private Quaternion _lastHeadLocalRot = Quaternion.identity;
    private Vector3 _lastHeadWorld;
    private float _lastHeadWorldYaw;

    private bool _originJumpArmed;
    private int _originJumpFrames;
    private Vector3 _preJumpHeadLocal;
    private Vector3 _preJumpHeadWorld;
    private float _preJumpHeadWorldYaw;
    private int _originCompensations;
    private int _originFlipRefusals;
    private float _armUpSwing;
    private float _nextFlipLogTime;

    /// <summary>
    /// Per-frame origin-jump guard (see the block comment). Runs on the SCENARIO rig only: the
    /// menu rig re-derives its own pose from the menu camera every time it is built, so a
    /// re-origin there heals by itself on the next recenter, and compensating it would fight
    /// <see cref="RecenterMenu"/>.
    /// </summary>
    private void TickOriginGuard()
    {
        if (_rigRoot == null || _camera == null || _kind != RigKind.Scenario
            || !ComfortSettings.KeepPlaceOnReorigin.Value)
        {
            _originKnown = false;
            _originJumpArmed = false;
            return;
        }

        // INVALIDATE ON A REBUILD/RECENTRE (hardware log 2026-08-03, line 483 — my own regression
        // from build 30). The remembered sample is a HEAD-LOCAL pose plus the world pose it
        // produced. Both belong to ONE camera under ONE rig root at ONE tracking origin. When the
        // rig is rebuilt — menu rig → scenario rig at every scenario start — the camera is a
        // different object with a different local pose, and the remembered WORLD pose is the menu
        // rig's. The guard read that as a 1.02 m one-frame jump and "put the player back", which
        // meant dragging the fresh scenario rig 21 m to place the head at the MENU's world origin.
        //
        // RigPoseVersion is the game-side signal for exactly the events that legitimately move the
        // player without the head moving in tracking space (rig (re)build, deliberate recentre),
        // and the object identities cover a rebuild that reuses the version. Any of the three
        // changing means: forget the old sample, take a fresh one, decide nothing this frame.
        int poseVersion = RigPoseVersion;
        if (!ReferenceEquals(_camera, _originCamera) || !ReferenceEquals(_rigRoot, _originRig)
            || poseVersion != _originPoseVersion)
        {
            _originCamera = _camera;
            _originRig = _rigRoot;
            _originPoseVersion = poseVersion;
            _originKnown = false;
            _originJumpArmed = false;
        }

        Transform head = _camera.transform;
        Vector3 local = head.localPosition;
        Quaternion localRot = head.localRotation;
        Vector3 world = head.position;
        float worldYaw = head.eulerAngles.y;

        if (!_originKnown)
        {
            _originKnown = true;
            Remember(local, localRot, world, worldYaw);
            return;
        }

        float moved = (local - _lastHeadLocal).magnitude;
        float turned = Quaternion.Angle(localRot, _lastHeadLocalRot);

        if (!_originJumpArmed)
        {
            if (moved <= OriginJumpMeters && turned <= OriginJumpDegrees)
            {
                Remember(local, localRot, world, worldYaw);
                return;
            }
            // THE FLIP REFUSAL (see the note at the top of this file). How far the head's UP
            // vector swung in TRACKING space in this one frame: ~0° for the yaw+translation
            // re-origin this guard was built for, ~180° for the external-playspace-mover flip
            // the co-player hit 16 times. Above the threshold the rotation is one the guard
            // cannot express and must not apply, so it applies NOTHING — the translation half is
            // what threw the rig root 0.4-5.0 real metres and took the head-relative control
            // board under the map.
            float upSwing = Vector3.Angle(_lastHeadLocalRot * Vector3.up, localRot * Vector3.up);
            if (upSwing > OriginFlipDegrees)
            {
                _originFlipRefusals++;
                if (Time.unscaledTime >= _nextFlipLogTime)
                {
                    _nextFlipLogTime = Time.unscaledTime + FlipLogThrottleSeconds;
                    // HW-VERIFY
                    VRLog.Note("Rig", $"PLAYSPACE FLIPPED — compensation REFUSED " +
                        $"(#{_originFlipRefusals}). The head's up vector swung {upSwing:F0}° in ONE " +
                        $"frame in tracking space (moved {moved:F2} m, total rotation {turned:F0}°): " +
                        "something OUTSIDE the game re-established the tracking space and rolled it " +
                        "— a SteamVR playspace overlay (OVR Advanced Settings 'Space Drag') or an " +
                        "OpenXR handle reset, never an HMD doff/don, which does not roll. This guard " +
                        "can only write world-up yaw and translation, so it cannot undo a roll; " +
                        "applying the translation half would drag the rig root metres and take the " +
                        "head-relative control board with it. Refusing outright: the mod writes " +
                        "NOTHING and the runtime's flip stands alone. Recentre to clear it.");
                }
                Remember(local, localRot, world, worldYaw);
                return;
            }

            // ARM: remember where the player WAS in the world, from last frame's sample.
            _originJumpArmed = true;
            _originJumpFrames = 0;
            _preJumpHeadLocal = _lastHeadLocal;
            _preJumpHeadWorld = _lastHeadWorld;
            _preJumpHeadWorldYaw = _lastHeadWorldYaw;
            _armUpSwing = upSwing;
            // 'turned' is Quaternion.Angle — AXIS-AGNOSTIC. Printing the up-vector swing beside it
            // splits that total into the part this guard can correct and the part it cannot, so a
            // future log never has to infer the split from the applied dYaw (which is exactly what
            // hid the 2026-09-06 flip for a whole session).
            VRLog.Info("Rig", $"Tracking-origin JUMP suspected: the head moved {moved:F2} m / " +
                              $"{turned:F0}° total in ONE frame in tracking space (up-vector swing " +
                              $"{upSwing:F0}°, under the {OriginFlipDegrees:F0}° flip refusal, so " +
                              "this is a yaw/translation re-origin) — no neck does that. " +
                              $"Watching for {OriginConfirmFrames} frames before compensating " +
                              "(a blip that returns is not an origin change).");
            _lastHeadLocal = local;
            _lastHeadLocalRot = localRot;
            return;
        }

        // ARMED: did it come back? Then it was a blip and the world must not move.
        if ((local - _preJumpHeadLocal).magnitude <= OriginReturnMeters)
        {
            _originJumpArmed = false;
            VRLog.Info("Rig", "Tracking-origin jump CANCELLED — the head returned to its pre-jump " +
                              "tracking pose, so it was a tracking blip, not a re-origin. Nothing moved.");
            Remember(local, localRot, world, worldYaw);
            return;
        }

        _lastHeadLocal = local;
        _lastHeadLocalRot = localRot;
        if (++_originJumpFrames < OriginConfirmFrames)
            return;

        // CONFIRMED: put the player back where they were, in the world.
        _originJumpArmed = false;
        _originCompensations++;
        Vector3 beforePos = _rigRoot.transform.position;
        float dYaw = Mathf.DeltaAngle(worldYaw, _preJumpHeadWorldYaw);
        _rigRoot.transform.RotateAround(head.position, Vector3.up, dYaw);
        Vector3 delta = _preJumpHeadWorld - head.position;
        _rigRoot.transform.position += delta;

        VRLog.Info("Rig", $"TRACKING ORIGIN CHANGED — compensated (#{_originCompensations}). The " +
                          $"runtime moved its origin under us (typical after an HMD doff/don); the " +
                          $"rig root was shifted {delta.magnitude:F2} m and turned {dYaw:F0}° so your " +
                          $"HEAD is back at world {_preJumpHeadWorld:F2} facing {_preJumpHeadWorldYaw:F0}° " +
                          $"(arming up-vector swing was {_armUpSwing:F0}°, i.e. NOT a playspace " +
                          "flip — this really was a yaw/translation re-origin) " +
                          $"— exactly where you were. Rig root {beforePos:F2} → " +
                          $"{_rigRoot.transform.position:F2}. RigPoseVersion deliberately NOT bumped: " +
                          "you did not move in the world, so nothing world-anchored may be carried.");

        Remember(head.localPosition, head.localRotation, head.position, head.eulerAngles.y);
    }

    private void Remember(Vector3 local, Quaternion localRot, Vector3 world, float worldYaw)
    {
        _lastHeadLocal = local;
        _lastHeadLocalRot = localRot;
        _lastHeadWorld = world;
        _lastHeadWorldYaw = worldYaw;
    }
}
