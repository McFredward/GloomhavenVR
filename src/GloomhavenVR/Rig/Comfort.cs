using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GloomhavenVR.Rig;

/// <summary>
/// Height &amp; recenter (completes the P1 recenter stub):
///
/// - BUTTON: hold the upper face button (B + Y — <c>VRHand.SecondaryButton</c>, the only
///   spare buttons the frozen P2 hand API exposes; the OpenXR menu/system button is
///   runtime-reserved and not surfaced) on BOTH controllers for
///   <c>[Comfort] RecenterHoldSeconds</c> → recenter. Requiring the two-hand chord keeps
///   single B/Y presses free for future features and makes accidental fires unlikely.
/// - Recenter re-aligns the rig so the HMD sits at the fixed standing spot at the table
///   edge: 0.30 m above the table plane, 0.70 m back, at the azimuth the player is already
///   at. That preset is the WHOLE seat now — the old seated-mode preset is GONE (user:
///   irrelevant — the world is freely draggable) and so is the
///   <c>[Comfort] TableHeightOffset</c> dial that used to be added to the height (user
///   ruling 2026-08). Recenter therefore has NO configuration left except how long the
///   chord is held: it is one fixed pose, the deterministic way back to the table from
///   anywhere, and nothing else.
/// - Dev harness: F11 recenters (desktop, [Dev] Enabled only).
///
/// Persistence: pinch-scale multiplier persists via <c>[Comfort] SavedScaleMultiplier</c>
/// (WorldGrab writes it, the rig build re-applies it). Eye height is NOT persisted and no
/// longer can be — it is whatever the player's own locomotion (stick flight, world grab)
/// leaves them at, exactly like their horizontal position, which was never persisted either:
/// it is scenario-world dependent, and recenter is the deterministic way back.
/// </summary>
internal sealed class Comfort : MonoBehaviour
{
    private float _chordHeldSeconds;
    private bool _chordFired;

    /// <summary>
    /// ONE-SHOT LOG LATCHES for the recentre chord, both STATIC so they mean "once per session"
    /// even across a rig rebuild that replaces this component.
    ///
    /// <para>WHY THEY EXIST (user, 2026-09-03: <i>"Der Test mit B und Y gedrückt halten wird auch
    /// nie erfolgreich"</i>). The chord's only line was <c>VRLog.Info</c>, which is the DEBUG tier
    /// in this mod and is NOT printed at the shipped default level — so a hardware log with no such
    /// line cannot distinguish "he never held both buttons" from "he held them and the hold never
    /// completed" from "it fired and the lesson step still did not tick". These two Notes split
    /// those three cases apart, and they are one-shot because a per-frame line on a held button
    /// would bury the rest of the round\'s log.</para>
    ///
    /// <para>They are pure instrumentation: nothing in <see cref="UpdateRecenterChord"/> reads
    /// either of them for anything except deciding whether its line has already been printed.</para>
    /// </summary>
    private static bool _notedChordDown;
    private static bool _notedChordFired;

    internal static Comfort? Instance { get; private set; }

    /// <summary>Chord progress 0..1 for gizmos/panel feedback.</summary>
    internal float ChordProgress
    {
        get
        {
            float hold = ComfortSettings.IsBound ? ComfortSettings.RecenterHoldSeconds.Value : 0f;
            return hold <= 0f ? 0f : Mathf.Clamp01(_chordHeldSeconds / hold);
        }
    }

    // ---- runtime ops (the UI-facing surface, docs/INTERFACES-P4.md) ----------------------
    // RequestRecenter is still driven from this class (B+Y chord, F11). SetScaleMultiplier has NO
    // caller any more: the WorldUI.SettingsPanel table-scale slider that drove it is gone and
    // VROptionsTab offers no replacement row. Nothing is broken — WorldGrab's pinch reaches the
    // same state — but giving the row back is a product decision, not a cleanup.

    /// <summary>Recenter now (same path as the button chord). Safe no-op without a rig.</summary>
    internal static void RequestRecenter() => VRRigDriver.RequestRecenter();

    /// <summary>
    /// Set the diorama scale multiplier directly (panel slider). Scales around the HMD
    /// world position so the view doesn't lurch, clamps, persists.
    /// </summary>
    internal static void SetScaleMultiplier(float multiplier)
    {
        Transform? rig = RigTarget.Current;
        if (rig == null || !ComfortSettings.IsBound)
            return;

        float baseScale = RigTarget.BaseScale;
        float s = Mathf.Clamp(multiplier, ComfortSettings.EffectiveScaleMin, ComfortSettings.EffectiveScaleMax)
                  * baseScale;
        float current = rig.localScale.x;
        if (Mathf.Approximately(s, current))
            return;

        Camera? head = VRRigDriver.HeadCamera;
        Vector3 pivot = head != null ? head.transform.position : rig.position;
        rig.position = pivot + (rig.position - pivot) * (s / current);
        rig.localScale = Vector3.one * s;
        RigClamp.Apply(rig);
        // [Sky] 3D environment scale-follow (SkyAlternative class doc, ZOOM note): mirror the
        // scale write onto the world-anchored room so it keeps its real size and real offset —
        // the pivot here is the head, which this write deliberately keeps still.
        Core.SkyAlternative.NotifyRigScaled(pivot, current, s);
        ComfortSettings.PersistScaleMultiplier(s / baseScale);
    }

    // ---- lifecycle -------------------------------------------------------------------------

    private void Awake() => Instance = this;

    // OnEnable/OnDisable existed ONLY to subscribe [Comfort] TableHeightOffset.Changed to a
    // live re-recenter. Both are gone with the setting (user ruling 2026-08): no comfort entry
    // feeds the recenter pose any more, so there is nothing left to react to and an empty pair
    // of Unity messages is per-frame cost for no behaviour.

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (!ComfortSettings.IsBound)
            return;

        UpdateRecenterChord();

        // Dev harness: F11 = recenter on the desktop.
        if (Plugin.DevMode.Value && !VRSession.IsRunning)
        {
            Keyboard? kb = Keyboard.current;
            if (kb != null && kb.f11Key.wasPressedThisFrame)
            {
                VRLog.Info("Comfort", "Dev recenter requested (F11).");
                RequestRecenter();
            }
        }
    }

    private void UpdateRecenterChord()
    {
        float hold = ComfortSettings.RecenterHoldSeconds.Value;
        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;

        // THE BUTTONS ALONE, measured before the other terms are ANDed in. This is the term that
        // separates "he never held both buttons" from "he held them and something else refused",
        // and it is deliberately NOT the same expression as `chordDown` below: a line printed
        // inside chordDown could only ever report HasPose == true, which answers nothing.
        bool bothSecondary = left != null && right != null
                             && left.SecondaryButton && right.SecondaryButton;

        if (bothSecondary && !_notedChordDown)
        {
            _notedChordDown = true;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("Comfort", "Recenter chord: BOTH secondary buttons (B+Y) are down for the "
                + "first time this session. The terms it still has to satisfy: hold "
                + $"{hold:0.00} s ([Comfort] RecenterHoldSeconds — 0 disables the chord entirely), "
                + $"left pose {left!.HasPose}, right pose {right!.HasPose}. A tracked pose is "
                + "required on BOTH hands, so a controller that has gone to sleep in the other hand "
                + "stops the hold from ever accumulating. Printed once per session.");
        }

        bool chordDown = hold > 0f
                         && bothSecondary
                         && left!.HasPose && right!.HasPose;

        if (!chordDown)
        {
            _chordHeldSeconds = 0f;
            _chordFired = false;
            return;
        }

        // UNSCALED, corrected 2026-09-03. This is a HOLD DURATION on a comfort control that has to
        // work while the game is paused or running a slow-motion sequence — Time.timeScale is not
        // ours and the game does move it — and a scaled clock makes the required hold silently
        // longer, or infinite at timeScale 0. Every other timer in this feature's neighbourhood
        // (ControlsTutorial's dwells and ceilings, the fingertip cooldown) is already on
        // Time.unscaledTime for exactly this reason; this accumulator was the odd one out.
        _chordHeldSeconds += Time.unscaledDeltaTime;
        if (_chordFired || _chordHeldSeconds < hold)
            return;

        _chordFired = true;
        VRLog.Info("Comfort", "Recenter chord (B+Y held) — recentering.");
        if (!_notedChordFired)
        {
            _notedChordFired = true;
            // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
            // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
            VRLog.Note("Comfort", "Recenter chord FIRED for the first time this session after "
                + $"{_chordHeldSeconds:0.00} s of hold (required {hold:0.00} s) — the rig is being "
                + "recentred. If the controls lesson's 'ctl_recenter' card did NOT tick after this "
                + "line, the defect is downstream of the chord, in VRRigDriver.Recenter or the "
                + "lesson's own waiting state, not in the button read. Printed once per session.");
        }
        RequestRecenter();
        left!.SendHaptic(HapticPreset.GrabPulse);
        right!.SendHaptic(HapticPreset.GrabPulse);
    }
}

/// <summary>
/// Comfort guard applied after every rig manipulation (grab, scale, turn, recenter):
/// the head must never end up inside/under the table. Enforces a minimum real-world
/// eye clearance above the table plane (the orbit-focus ground plane) by lifting the
/// rig back up — pure rig-space correction, game objects untouched.
///
/// TEST #10: fully disabled while <c>[Comfort] FreeMovement</c> (default ON) — the
/// player may dive below the table or float above it without limits; the recenter
/// chord (B+Y hold) is the deterministic way back from anywhere. Setting
/// FreeMovement=false restores this clamp.
/// </summary>
internal static class RigClamp
{
    /// <summary>Minimum real-meter eye clearance above the table plane.</summary>
    internal const float MinEyeAboveTableMeters = 0.10f;

    /// <summary>True when the last <see cref="Apply"/> had to correct (gizmos).</summary>
    internal static bool LastClampActive { get; private set; }

    internal static void Apply(Transform rig)
    {
        LastClampActive = false;

        if (ComfortSettings.PositionalClampsDisabled)
            return; // [Comfort] FreeMovement: no positional limits at all (test #10)

        Camera? head = VRRigDriver.HeadCamera;
        CameraController controller = CameraController.s_CameraController;
        if (head == null || controller == null)
            return; // dev proxy / rig torn down — nothing to guard

        float scale = rig.localScale.x;
        float minHeadY = controller.FocusPoint.y + MinEyeAboveTableMeters * scale;
        float headY = head.transform.position.y;
        if (headY >= minHeadY)
            return;

        rig.position += Vector3.up * (minHeadY - headY);
        LastClampActive = true;
    }
}
