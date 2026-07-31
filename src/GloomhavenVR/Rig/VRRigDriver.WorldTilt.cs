using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    // ---- Demeo-style world tilt ([Rig] WorldTiltDegrees) -----------------------------------

    /// <summary>Configured tilt target, clamped to the supported 0-60° range (0 while unbound).</summary>
    private static float TargetTiltDegrees =>
        Plugin.WorldTiltDegrees != null ? Mathf.Clamp(Plugin.WorldTiltDegrees.Value, 0f, 60f) : 0f;

    /// <summary>
    /// The yaw-only (horizon-aligned) part of a rig rotation, via swing–twist decomposition
    /// about world up: for a unit quaternion q, the twist around Y is
    /// <c>normalize(0, q.y, 0, q.w)</c>. This is EXACT for every pose our writers produce —
    /// algebraically, twist(T ∘ Y) = Y for ANY tilt T about a HORIZONTAL axis composed onto a
    /// yaw Y (the horizontal tilt vector is orthogonal to the yaw vector, so the y/w
    /// components of the product are just cos(t/2)·(sin, cos of the half-yaw)), and likewise
    /// twist(Y₂ ∘ T ∘ Y) = Y₂·Y for snap-turn's world-up compositions. The former
    /// forward-projection version was only exact while the tilt axis was the yaw's own right
    /// axis; the player-relative tilt axis (TickWorldTilt) broke that assumption — projection
    /// would have bled a per-frame yaw drift into the healing loop.
    /// </summary>
    private static Quaternion YawOnly(Quaternion rotation)
    {
        float y = rotation.y;
        float w = rotation.w;
        float mag = Mathf.Sqrt(y * y + w * w);
        if (mag < 1e-6f)
            return Quaternion.identity; // pure 180° flip about a horizontal axis; unreachable
        return new Quaternion(0f, y / mag, 0f, w / mag);
    }

    /// <summary>
    /// Assert the world tilt on the scenario rig (LOCAL-ONLY, rig-side — Demeo model):
    /// reconstruct the desired pose as <c>tilt(target°, about the RIG-YAW-RELATIVE horizontal
    /// axis) ∘ yawOnly(current)</c> and rotate the rig into it around the BOARD CENTER
    /// (<c>CameraController.FocusPoint</c> — the same orbit focus the rig was built at).
    /// Because the rotation happens about the pivot, the player's virtual head orbits up and
    /// over the board while the board itself appears to tilt toward them; world coordinates
    /// of every game object are untouched, so nothing changes for multiplayer peers except
    /// our own (honestly moved) avatar pose.
    ///
    /// TILT AXIS (rounds 5+6 — the DEMEO MODEL plus view aim; provenance + algebra on
    /// the axis-field comment block). The axis is the rig's yaw-frame right composed
    /// with the head-local aim yaw, <c>(yawOnly(rig) ∘ R_up(aim)) * Vector3.right</c> —
    /// the rig-yaw factor is Demeo's yaw-parent/tilt-child chain (co-rotates exactly
    /// with stick turns/world-grab, zero writes), and the aim factor keeps the tilt
    /// tipping toward the VIEW direction. The aim is updated ONLY under perceptual
    /// masking: instantly at masked events (recenter/stick turn/world grab — Demeo's
    /// InputTracking.Recenter analog) and gradually at a subthreshold gain while the
    /// head itself rotates fast (redirected rotation). Under head-only motion below
    /// the masking threshold every input to the desired pose is constant, so
    /// desired == current and no transform write happens — the world is bit-frozen.
    ///
    /// Per-frame reconstruction (not an incremental delta) is what makes every composition
    /// free: recenter and rig rebuilds re-run their yaw-only math and the tilt re-applies
    /// the same frame; snap turn (RotateAround world-up) preserves the pitch and lands
    /// within epsilon; WorldGrab's two-hand yaw-flatten is healed before render. YawOnly's
    /// swing–twist decomposition keeps the yaw extraction exact under the head-relative
    /// (non-yaw-aligned) tilt axis. At the default 0° with no tilt ever applied the method
    /// returns before touching the transform — bit-identical to the pre-feature rig.
    /// </summary>
    private void TickWorldTilt()
    {
        if (_kind != RigKind.Scenario || _rigRoot == null)
            return;

        float target = TargetTiltDegrees;
        if (target <= 0f && !_tiltActive)
        {
            // Fast path: feature off and nothing to undo — zero transform writes, 0°
            // bit-identical to the pre-feature rig. Keep the magnitude edge armed so a
            // later enable tweens up from 0° attributed as the user's config-change.
            _lastTiltTarget = 0f;
            _tiltApplied = 0f;
            _tiltTweenFrom = 0f;
            _prevHeadYawValid = false; // no head-rate tracking while off → no stale spike on enable
            WorldTiltRotation = Quaternion.identity; // perceived-level frame = world level while off
            return;
        }

        CameraController controller = CameraController.s_CameraController;
        if (controller == null)
            return; // anchor died mid-frame; the Update health check tears down next tick

        Transform rig = _rigRoot.transform;
        Quaternion current = rig.rotation;
        Quaternion yawOnly = YawOnly(current);
        Vector3 pivot = controller.FocusPoint;

        // TILT MAGNITUDE (Demeo AvatarController.Tilt/StartTilt): changes only on the
        // discrete [Rig] WorldTiltDegrees ±5° settings click (its sole writer — the
        // Demeo analog of the thumb-flick 15° step; zoom/scale never touch it) and
        // animates with Demeo's 0.2 s LINEAR ramp. A fresh rig (teardown sentinel -1)
        // adopts the configured tilt instantly — the whole world just (re)appeared,
        // there is no motion the tween would mask.
        string? changeTrigger = null;
        bool seedAimFromHead = false;
        if (!Mathf.Approximately(target, _lastTiltTarget))
        {
            bool freshRig = _lastTiltTarget < 0f;
            _tiltTweenFrom = freshRig ? target : _tiltApplied;
            _tiltTweenStartTime = Time.unscaledTime;
            _lastTiltTarget = target;
            changeTrigger = freshRig ? "rig-build" : "config-change";
            // Fresh rig, or a tween up from FLAT: the axis direction is currently
            // invisible (0° applied), so adopting the player's view yaw as the aim is
            // free — the tilt grows toward wherever they are actually looking.
            seedAimFromHead = freshRig || _tiltTweenFrom <= 0f;
            _nextTiltLogTime = 0f; // edge-trigger the periodic diagnostic line below
        }
        float tweenT = Mathf.Clamp01((Time.unscaledTime - _tiltTweenStartTime) / TiltTweenSeconds);
        _tiltApplied = Mathf.Lerp(_tiltTweenFrom, target, tweenT);

        // VIEW-AIM MAINTENANCE (round 6; mechanism + provenance on the axis comment
        // block). The head-local yaw is the PURE DEVICE pose (TrackedPoseDriver writes
        // rig-local), so every quantity here is invariant under rig writes — snap/stick
        // turns and world-grab can never fake a head rotation, and under head-only
        // motion nothing below writes any state that feeds the desired pose unless a
        // masking condition holds.
        bool grabActive = WorldGrab.Instance != null && WorldGrab.Instance.IsGrabbing;
        bool maskedStep = false;
        float aimError = 0f;
        if (_camera != null)
        {
            float headYawDeg = YawOnly(_camera.transform.localRotation).eulerAngles.y;
            float dt = Time.unscaledDeltaTime;
            float headRate = _prevHeadYawValid && dt > 1e-5f
                ? Mathf.DeltaAngle(_prevHeadYawDeg, headYawDeg) / dt
                : 0f;
            _prevHeadYawDeg = headYawDeg;
            _prevHeadYawValid = true;

            aimError = Mathf.DeltaAngle(_tiltAimYawDeg, headYawDeg);
            if (_axisSnapReason != null || grabActive || seedAimFromHead)
            {
                // MASKED EVENT: the world is already jumping (recenter, stick turn,
                // grab release) or being dragged (active grab — continuous re-aim) or
                // the tilt is still flat — consume the whole error at once, invisibly.
                // This is exactly Demeo's recenter mechanism (InputTracking.Recenter
                // absorbs the head yaw into the root behind a fade).
                _tiltAimYawDeg = headYawDeg;
                aimError = 0f;
            }
            else if (Mathf.Abs(headRate) >= MaskedReaimHeadRateDps
                     && Mathf.Abs(aimError) > MaskedReaimDeadbandDeg)
            {
                // MASKED ROTATION (redirected-rotation, subthreshold gain): correct
                // toward the view ONLY while the head itself rotates fast enough to
                // mask it. The gate re-evaluates every frame from the CURRENT head
                // rate, so the instant the head slows the writes stop — residual
                // error waits, bit-frozen, for the next fast rotation or event.
                float step = Mathf.Sign(aimError)
                             * Mathf.Min(Mathf.Abs(aimError),
                                         MaskedReaimGainFrac * Mathf.Abs(headRate) * dt);
                _tiltAimYawDeg = Mathf.DeltaAngle(0f, _tiltAimYawDeg + step);
                aimError -= step;
                maskedStep = true;
                if (!_burstActive)
                {
                    _burstActive = true;
                    _burstStartTime = Time.unscaledTime;
                    _burstDegrees = 0f;
                    _burstPeakHeadRate = 0f;
                }
                _burstDegrees += Mathf.Abs(step);
                _burstPeakHeadRate = Mathf.Max(_burstPeakHeadRate, Mathf.Abs(headRate));
                _burstLastStepTime = Time.unscaledTime;
            }
        }
        // ONE summary line per masked-correction burst, at its END — never per-frame.
        if (_burstActive && !maskedStep && Time.unscaledTime - _burstLastStepTime > BurstEndGraceSeconds)
        {
            _burstActive = false;
            VRLog.Info("Rig", $"WorldTilt masked re-aim burst: consumed {_burstDegrees:F1}° over " +
                              $"{Mathf.Max(0f, _burstLastStepTime - _burstStartTime):F2}s " +
                              $"(peak head rate {_burstPeakHeadRate:F0}°/s, residual view error {aimError:F1}°).");
        }

        // Tilt axis (round-5 Demeo parenting model + round-6 view aim): the rig's own
        // yaw frame composed with the head-local aim yaw —
        //   desired = AngleAxis(tilt, axis) ∘ yawOnly,  axis = (yawOnly ∘ R_up(aim)) · right
        // The rig-yaw factor makes a pure world-up rig yaw (snap/stick turn, grab
        // rotate) co-rotate the axis exactly — R∘(T∘Y) is bit-reconstructed for the
        // new yaw R·Y, zero correction write — while the aim factor tips the tilt
        // toward the view direction the masked channels last captured. Both factors
        // are constant under head-only motion → desired == current → no writes.
        Quaternion aimYaw = yawOnly * Quaternion.AngleAxis(_tiltAimYawDeg, Vector3.up);
        Vector3 axis = aimYaw * Vector3.right;

        Quaternion desired = _tiltApplied > 0f
            ? Quaternion.AngleAxis(_tiltApplied, axis) * yawOnly
            : yawOnly;

        // Publish the perceived-level frame (control-board item 11): the tilt factor of the pose
        // this frame asserts. Consumers compose "level for the player" as WorldTiltRotation * pose.
        WorldTiltRotation = _tiltApplied > 0f
            ? Quaternion.AngleAxis(_tiltApplied, axis)
            : Quaternion.identity;

        // Periodic diagnostic while active (hardware-log contract): rigYaw/aim/axis must
        // read IDENTICAL across consecutive lines unless a 'WorldTilt change [trigger]'
        // or a 'masked re-aim burst' line sits between them — every change is either a
        // locomotion/config event or a masked-rotation burst, never bare head movement.
        // viewErr is the head-vs-aim yaw error currently waiting (frozen) for masking.
        if (target > 0f && Time.unscaledTime >= _nextTiltLogTime)
        {
            _nextTiltLogTime = Time.unscaledTime + TiltLogIntervalSeconds;
            Vector3 headPos = _camera != null ? _camera.transform.position : Vector3.zero;
            VRLog.Info("Rig", $"WorldTilt {_tiltApplied:F1}°/{target:0}°: pivot {pivot}, head {headPos}, " +
                              $"rigYaw {yawOnly.eulerAngles.y:F1}°, aim {_tiltAimYawDeg:F1}° (head-local), " +
                              $"viewErr {aimError:F1}°, axis {axis} (last change [{_lastChangeTrigger}]).");
        }

        float error = Quaternion.Angle(current, desired);
        if (error > 0.01f)
        {
            // CHANGE-ATTRIBUTED write (hardware-log contract): every world motion the
            // tilt system causes is logged with its trigger — a locomotion event
            // (NotifyTiltAxisSnap reason, e.g. recenter's yaw-flatten + instant re-aim
            // healed here), an active world grab (its per-frame two-hand yaw-flatten +
            // continuous re-aim healed here), a masked-rotation step, or the user's
            // tilt config click. 'rig-pose-heal' would mean an unattributed external
            // writer flattened the rig — investigate if it ever appears. 'masked-reaim'
            // frames stay QUIET here — their one-line-per-burst summary above is the
            // log (never per-frame).
            string trigger = _axisSnapReason
                             ?? changeTrigger
                             ?? (grabActive ? "world-grab"
                                 : maskedStep ? "masked-reaim"
                                 : "rig-pose-heal");
            bool repeatTrigger = trigger == _lastChangeTrigger;
            _lastChangeTrigger = trigger;
            if (trigger != "masked-reaim" && (!repeatTrigger || Time.unscaledTime >= _nextChangeLogTime))
            {
                _nextChangeLogTime = Time.unscaledTime + ChangeLogThrottleSeconds;
                VRLog.Info("Rig", $"WorldTilt change [{trigger}]: tilt {_tiltApplied:F1}°/{target:0}°, " +
                                  $"rigYaw {yawOnly.eulerAngles.y:F1}°, healed {error:F2}°.");
            }

            // Rotate the rig into the desired pose AROUND the board center so the pose
            // change reads as the viewpoint orbiting the board, not the world snapping.
            Quaternion delta = desired * Quaternion.Inverse(current);
            rig.position = pivot + delta * (rig.position - pivot);
            rig.rotation = desired;
        }
        _axisSnapReason = null; // attribution is per-frame; a no-write frame consumes it too

        _tiltActive = _tiltApplied > 0f;
    }
}
