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
            // ARM: remember where the player WAS in the world, from last frame's sample.
            _originJumpArmed = true;
            _originJumpFrames = 0;
            _preJumpHeadLocal = _lastHeadLocal;
            _preJumpHeadWorld = _lastHeadWorld;
            _preJumpHeadWorldYaw = _lastHeadWorldYaw;
            VRLog.Info("Rig", $"Tracking-origin JUMP suspected: the head moved {moved:F2} m / " +
                              $"{turned:F0}° in ONE frame in tracking space — no neck does that. " +
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
