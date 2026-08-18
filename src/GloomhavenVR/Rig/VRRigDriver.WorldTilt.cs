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

    // ============================== FEATURE PARKED (2026-08) ==============================
    // WORLD TILT IS DISABLED BY USER RULING ("Die Weltneigung macht zu viele Probleme — bitte
    // entferne sie vorerst"). The ONE line below forces the effective tilt to 0 regardless of
    // the [Rig] WorldTiltDegrees value, which provably reduces every tilt code path to the
    // pre-feature behavior (the tilt-0 bit-identical invariant maintained through every
    // iteration): TickWorldTilt takes its fast path (zero transform writes),
    // CurrentTiltSwing returns identity (WorldGrab's rotation write is pure yaw again), and
    // NotifyWorldGrabMotion returns before touching any state. The cfg entry stays bound
    // (documented dormant at the bind site) so a tuned angle survives in the file.
    // REVIVAL = restore the commented-out line below + the curated options row
    // (VROptionsTab.4.Curated.cs, "Rig/WorldTiltDegrees") — nothing else was removed.
    // =======================================================================================

    /// <summary>Effective tilt target — FORCED TO 0 while the feature is parked (see above).
    /// Was: <c>Plugin.WorldTiltDegrees != null ? Mathf.Clamp(Plugin.WorldTiltDegrees.Value, 0f, 60f) : 0f</c>.</summary>
    private static float TargetTiltDegrees => 0f;

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
    /// INTERNAL (not private): <see cref="WorldGrab"/> shares this exact extraction so its
    /// yaw anchor/target math reads the same twist the healing loop reconstructs — two
    /// different yaw definitions on the same rig were the Bug-A feedback loop.
    /// </summary>
    internal static Quaternion YawOnly(Quaternion rotation)
    {
        float y = rotation.y;
        float w = rotation.w;
        float mag = Mathf.Sqrt(y * y + w * w);
        if (mag < 1e-6f)
            return Quaternion.identity; // pure 180° flip about a horizontal axis; unreachable
        return new Quaternion(0f, y / mag, 0f, w / mag);
    }

    /// <summary>
    /// The tilt SWING factor TickWorldTilt would compose onto the given yaw-only rotation
    /// THIS frame — <c>AngleAxis(_tiltApplied, (yawOnly ∘ R_up(aim)) · right)</c>, identity
    /// while the tilt is off/no scenario rig. For <see cref="WorldGrab"/>'s two-hand write:
    /// composing its new yaw as <c>CurrentTiltSwing(yawRot) * yawRot</c> makes the write
    /// TILT-CONSISTENT — the healing loop reconstructs desired = AngleAxis(tilt, axis(Y)) ∘ Y
    /// from Y = YawOnly(current), and YawOnly(T ∘ Y) = Y exactly (T's axis is horizontal, see
    /// <see cref="YawOnly"/>), so desired == what the grab wrote and the heal performs ZERO
    /// writes: no per-frame flatten/re-tilt fight, no feedback through the grab's exponential
    /// smoothing (the old fling).
    ///
    /// AIM is read from <c>_tiltAimYawDeg</c> — the SAME field TickWorldTilt's reconstruction
    /// reads that frame — so the grab's write and the heal's desired pose stay provably in
    /// LOCKSTEP. (Round 7: the old grab-active branch re-seeded the aim to the live head yaw
    /// every frame, which forced this method to sample the head too; that instant-consume was
    /// the press/release scene jump and is gone — the grab's re-aim is motion-proportional
    /// now, see <see cref="NotifyWorldGrabMotion"/>, which WorldGrab calls BEFORE composing
    /// this swing, so any grab-motion-masked aim step this frame is already in the field by
    /// the time the grab writes with it.) Residual same-frame divergence, each bounded,
    /// non-accumulating (the heal reconstructs from the exact twist, not a delta) and
    /// attributed "world-grab" (grabActive) — never "rig-pose-heal":
    ///  - a masked-rotation step in the tick (head-rate channel, ≤ gain·headRate·dt): an
    ///    axis-only change at unchanged magnitude, healed about the HEAD pivot — pure
    ///    view-direction drift, zero player translation;
    ///  - <c>_tiltApplied</c> during the 0.2 s config tween (the tick advances it in
    ///    LateUpdate, after us): a one-frame lag of at most tweenSlope·dt (≈0.35° at 72 Hz
    ///    for a 5° step).
    /// At tilt 0 this returns identity → the grab write reduces bit-identically to the
    /// pre-tilt yaw-only behavior.
    /// </summary>
    internal static Quaternion CurrentTiltSwing(Quaternion yawOnly)
    {
        VRRigDriver? drv = Instance;
        if (drv == null || drv._kind != RigKind.Scenario || drv._tiltApplied <= 0f)
            return Quaternion.identity;
        Vector3 axis = yawOnly * Quaternion.AngleAxis(drv._tiltAimYawDeg, Vector3.up) * Vector3.right;
        return Quaternion.AngleAxis(drv._tiltApplied, axis);
    }

    // ---- grab-motion-masked aim consumption (round 7) --------------------------------------
    //
    // ROOT CAUSE this replaced (round-7 hardware log): the old branch consumed the WHOLE view
    // error at grab PRESS/RELEASE — instants at which the grab has moved the world by nothing
    // (the deadzones have not even latched), so a ~25° banked error snapped unmasked and the
    // scene visibly jumped. Genuine masking exists only while the grab is actually MOVING the
    // world, in proportion to that motion. These constants map the grab's APPLIED world motion
    // each frame to the aim degrees it may consume; each ratio is conservative so the
    // re-aim-induced world motion (a rotation of sin(tilt)·step ≤ 0.87·step about the HEAD —
    // see the pivot selection in TickWorldTilt) stays well below the masking motion itself:
    //  - DegPerMeter 20: 1 cm of real-meter drag masks 0.2° of re-aim → at a typical 1–2 m
    //    board distance the induced displacement stays under ~half the drag displacement;
    //  - DegPerWorldYawDeg 0.5: induced rotation ≤ 0.87·0.5 ≈ 0.44× the world yaw actually
    //    sweeping the scene;
    //  - DegPerScaleOctave 15: a full doubling/halving of the world sweeps everything
    //    radially past the player — masks 15° of re-aim;
    //  - MaxDegPerFrame 1.5 caps single-frame consumption (~108°/s at 72 Hz) so a motion
    //    spike can never turn back into a snap.
    // When the grab holds still the budget is ~0 and the error stays frozen, exactly like
    // head-only motion outside a grab.
    private const float GrabReaimDegPerMeter = 20f;
    private const float GrabReaimDegPerWorldYawDeg = 0.5f;
    private const float GrabReaimDegPerScaleOctave = 15f;
    private const float GrabReaimMaxDegPerFrame = 1.5f;

    /// <summary>
    /// Grab-motion-masked aim consumption: called by <see cref="WorldGrab"/> once per frame
    /// it applies world motion, BEFORE it composes <see cref="CurrentTiltSwing"/> into its
    /// rotation write — so the aim step is already in <c>_tiltAimYawDeg</c> when both the
    /// grab write and this frame's TickWorldTilt reconstruction read it (lockstep, see
    /// CurrentTiltSwing). Arguments are the APPLIED world motion this frame: real meters the
    /// world slid under the hands, degrees of world yaw, octaves (log2) of scale change.
    /// Consumes view error toward the current head yaw up to the motion-proportional budget
    /// (tuning constants above) and feeds the same one-line-per-burst diagnostic as the
    /// masked-rotation channel. Touches NO state while the tilt is flat/off (seedAimFromHead
    /// owns the tween-up-from-flat case) — the tilt-0 bit-identical no-op invariant holds.
    /// </summary>
    internal static void NotifyWorldGrabMotion(float dragMeters, float worldYawDeg, float scaleOctaves)
    {
        VRRigDriver? drv = Instance;
        if (drv == null || drv._kind != RigKind.Scenario || drv._tiltApplied <= 0f
            || drv._camera == null)
            return;
        float budget = Mathf.Abs(dragMeters) * GrabReaimDegPerMeter
                       + Mathf.Abs(worldYawDeg) * GrabReaimDegPerWorldYawDeg
                       + Mathf.Abs(scaleOctaves) * GrabReaimDegPerScaleOctave;
        budget = Mathf.Min(budget, GrabReaimMaxDegPerFrame);
        if (budget <= 1e-4f)
            return; // grab holding still → error stays frozen, zero writes
        float headYawDeg = YawOnly(drv._camera.transform.localRotation).eulerAngles.y;
        float aimError = Mathf.DeltaAngle(drv._tiltAimYawDeg, headYawDeg);
        if (Mathf.Abs(aimError) <= 1e-3f)
            return;
        float step = Mathf.Sign(aimError) * Mathf.Min(Mathf.Abs(aimError), budget);
        drv._tiltAimYawDeg = Mathf.DeltaAngle(0f, drv._tiltAimYawDeg + step);
        drv.RecordMaskedAimStep(step, 0f, grabMasked: true);
    }

    /// <summary>Burst accounting shared by the masked-rotation (head-rate) and grab-motion
    /// channels — one summary log line per burst, emitted by TickWorldTilt at burst end
    /// (never per-frame; the logging contract).</summary>
    private void RecordMaskedAimStep(float stepDeg, float headRateDps, bool grabMasked)
    {
        if (!_burstActive)
        {
            _burstActive = true;
            _burstStartTime = Time.unscaledTime;
            _burstDegrees = 0f;
            _burstGrabDegrees = 0f;
            _burstPeakHeadRate = 0f;
        }
        _burstDegrees += Mathf.Abs(stepDeg);
        if (grabMasked)
            _burstGrabDegrees += Mathf.Abs(stepDeg);
        _burstPeakHeadRate = Mathf.Max(_burstPeakHeadRate, Mathf.Abs(headRateDps));
        _burstLastStepTime = Time.unscaledTime;
    }

    /// <summary>
    /// Assert the world tilt on the scenario rig (LOCAL-ONLY, rig-side — Demeo model):
    /// reconstruct the desired pose as <c>tilt(target°, about the RIG-YAW-RELATIVE horizontal
    /// axis) ∘ yawOnly(current)</c> and rotate the rig into it around a cause-selected pivot:
    /// tilt MAGNITUDE changes orbit the BOARD CENTER (<c>CameraController.FocusPoint</c> —
    /// the same orbit focus the rig was built at), while AXIS-ONLY re-aims at unchanged
    /// magnitude pivot about the HEAD (round 7 — see the pivot selection below). Under the
    /// FocusPoint orbit the player's virtual head orbits up and over the board while the
    /// board itself appears to tilt toward them; world coordinates of every game object are
    /// untouched, so nothing changes for multiplayer peers except our own (honestly moved)
    /// avatar pose.
    ///
    /// TILT AXIS (rounds 5+6 — the DEMEO MODEL plus view aim; provenance + algebra on
    /// the axis-field comment block). The axis is the rig's yaw-frame right composed
    /// with the head-local aim yaw, <c>(yawOnly(rig) ∘ R_up(aim)) * Vector3.right</c> —
    /// the rig-yaw factor is Demeo's yaw-parent/tilt-child chain (co-rotates exactly
    /// with stick turns/world-grab, zero writes), and the aim factor keeps the tilt
    /// tipping toward the VIEW direction. The aim is updated ONLY under perceptual
    /// masking: instantly at masked events (recenter/stick turn — Demeo's
    /// InputTracking.Recenter analog), gradually at a subthreshold gain while the
    /// head itself rotates fast (redirected rotation), and — round 7 — in proportion
    /// to the world motion an active grab actually applies each frame
    /// (<see cref="NotifyWorldGrabMotion"/>; a grab that merely PRESSES or RELEASES
    /// the stick moves nothing and therefore re-aims nothing). Under head-only motion
    /// below the masking threshold every input to the desired pose is constant, so
    /// desired == current and no transform write happens — the world is bit-frozen.
    ///
    /// Per-frame reconstruction (not an incremental delta) is what makes every composition
    /// free: recenter and rig rebuilds re-run their yaw-only math and the tilt re-applies
    /// the same frame; snap turn (RotateAround world-up) preserves the pitch and lands
    /// within epsilon; WorldGrab's two-hand write is TILT-CONSISTENT since Bug A (it composes
    /// <see cref="CurrentTiltSwing"/> onto its yaw, so there is no flatten to heal — only the
    /// one-frame tween lag documented there). YawOnly's
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
        float appliedBefore = _tiltApplied;
        _tiltApplied = Mathf.Lerp(_tiltTweenFrom, target, tweenT);
        // A tween CONTINUATION frame (edge already consumed changeTrigger) is still a
        // magnitude write of the user's config click: attribute it "config-change", never
        // the 'rig-pose-heal' unattributed-writer alarm, and keep the FocusPoint orbit.
        bool magnitudeStep = !Mathf.Approximately(_tiltApplied, appliedBefore);

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
            if (_axisSnapReason != null || seedAimFromHead)
            {
                // MASKED EVENT: the world is already jumping (recenter, stick turn) or
                // the tilt is still flat — consume the whole error at once, invisibly.
                // This is exactly Demeo's recenter mechanism (InputTracking.Recenter
                // absorbs the head yaw into the root behind a fade). World-grab
                // press/release is deliberately NOT in this set (round 7 — root cause on
                // the grab-motion constants above); a grab re-aims only in proportion to
                // the motion it actually applies (NotifyWorldGrabMotion, Update phase),
                // and the head-rate channel below stays live during a grab.
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
                RecordMaskedAimStep(step, headRate, grabMasked: false);
            }
        }
        // ONE summary line per masked-correction burst, at its END — never per-frame.
        // Covers BOTH masked channels: head-rate steps (here) and grab-motion-masked steps
        // (NotifyWorldGrabMotion, Update phase) share the burst accounting; the grab share
        // is broken out so the hardware log proves grab re-aim tracked actual motion.
        if (_burstActive && !maskedStep && Time.unscaledTime - _burstLastStepTime > BurstEndGraceSeconds)
        {
            _burstActive = false;
            VRLog.Info("Rig", $"WorldTilt masked re-aim burst: consumed {_burstDegrees:F1}° " +
                              $"({_burstGrabDegrees:F1}° grab-motion-masked) over " +
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

        // Publish the perceived-level frame: the tilt factor of the pose this frame asserts.
        // Consumers compose "level for the player" as WorldTiltRotation * pose. (No consumer
        // right now — the control board was decoupled from the tilt, user decision 2026-08;
        // see the property doc for why it stays published.)
        WorldTiltRotation = _tiltApplied > 0f
            ? Quaternion.AngleAxis(_tiltApplied, axis)
            : Quaternion.identity;

        // Periodic diagnostic while active (hardware-log contract): rigYaw/aim/axis must
        // read IDENTICAL across consecutive lines unless a 'WorldTilt change [trigger]'
        // or a 'masked re-aim burst' line sits between them (or a burst is still in
        // progress — its summary lands at burst end) — every change is either a
        // locomotion/config event or a masked burst (head-rate or grab-motion channel),
        // never bare head movement.
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
            // PIVOT SELECTION (round 7). A heal whose ONLY cause is an AXIS/aim change at
            // UNCHANGED tilt magnitude must rotate about the HEAD: orbiting FocusPoint
            // translates the head whenever it stands away from the pivot (the press/release
            // scene jump this round fixed), while the same rotation about the head keeps
            // the player perfectly still as the tilt direction drifts — imperceptible.
            // Tilt MAGNITUDE changes (config click/tween, rig-build, recenter's re-tilt of
            // a flattened pose) keep the FocusPoint orbit — the board tilting toward you
            // around its own center is the intended Demeo feel. The axis-only test is
            // exact, not a heuristic: desired is reconstructed FROM the current yaw twist,
            // so it can differ from current only in swing magnitude and swing axis —
            // equal swing angles ⇒ pure axis change.
            float currentSwingDeg = Quaternion.Angle(current, yawOnly);
            bool axisOnlyHeal = Mathf.Abs(currentSwingDeg - _tiltApplied) <= 0.05f;
            Vector3 healPivot = axisOnlyHeal && _camera != null
                ? _camera.transform.position
                : pivot;

            // CHANGE-ATTRIBUTED write (hardware-log contract): every world motion the
            // tilt system causes is logged with its trigger — a locomotion event
            // (NotifyTiltAxisSnap reason, e.g. recenter's yaw-flatten + instant re-aim
            // healed here), the user's tilt config click INCLUDING its tween continuation
            // frames (magnitudeStep — the edge frame consumes changeTrigger, the ramp
            // frames must not fall through to the alarm), an active world grab
            // (tilt-consistent since Bug A — only its head-rate re-aim steps and the
            // one-frame tween lag land here), or a masked-rotation step. 'rig-pose-heal'
            // would mean an unattributed external writer flattened the rig — investigate
            // if it ever appears; it is unreachable from grabs and from tweens.
            // 'masked-reaim' frames stay QUIET here — their one-line-per-burst summary
            // above is the log (never per-frame).
            string trigger = _axisSnapReason
                             ?? changeTrigger
                             ?? (magnitudeStep ? "config-change"
                                 : grabActive ? "world-grab"
                                 : maskedStep ? "masked-reaim"
                                 : "rig-pose-heal");
            bool repeatTrigger = trigger == _lastChangeTrigger;
            _lastChangeTrigger = trigger;
            if (trigger != "masked-reaim" && (!repeatTrigger || Time.unscaledTime >= _nextChangeLogTime))
            {
                _nextChangeLogTime = Time.unscaledTime + ChangeLogThrottleSeconds;
                VRLog.Info("Rig", $"WorldTilt change [{trigger}]: tilt {_tiltApplied:F1}°/{target:0}°, " +
                                  $"rigYaw {yawOnly.eulerAngles.y:F1}°, healed {error:F2}° " +
                                  $"(pivot {(axisOnlyHeal ? "head" : "focus")}).");
            }

            // Rotate the rig into the desired pose around the selected pivot: FocusPoint
            // so magnitude changes read as the viewpoint orbiting the board, the head so
            // axis-only re-aims never translate the player.
            Quaternion delta = desired * Quaternion.Inverse(current);
            rig.position = healPivot + delta * (rig.position - healPivot);
            rig.rotation = desired;
        }
        _axisSnapReason = null; // attribution is per-frame; a no-write frame consumes it too

        _tiltActive = _tiltApplied > 0f;
    }
}
