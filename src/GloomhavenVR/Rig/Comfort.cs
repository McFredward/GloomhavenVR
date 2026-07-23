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
/// - Recenter re-aligns the rig so the HMD sits at the configured spot at the table
///   edge: the standing preset (0.70 m above / 0.70 m back) plus
///   <c>[Comfort] TableHeightOffset</c>. Changing the offset re-runs recenter live (the
///   P3c panel gets immediate feedback for free). The old seated-mode preset is GONE
///   (user: irrelevant — the world is freely draggable).
/// - Dev harness: F11 recenters (desktop, [Dev] Enabled only).
///
/// Persistence: pinch-scale multiplier persists via <c>[Comfort] SavedScaleMultiplier</c>
/// (WorldGrab writes it, the rig build re-applies it); height persists as the
/// TableHeightOffset config itself. Drag position is intentionally NOT
/// persisted — it is scenario-world dependent; recenter is the deterministic way back.
/// </summary>
internal sealed class Comfort : MonoBehaviour
{
    private float _chordHeldSeconds;
    private bool _chordFired;

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

    // ---- runtime ops (P3c settings-panel surface, see docs/INTERFACES-P4.md) -------------

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
        ComfortSettings.PersistScaleMultiplier(s / baseScale);
    }

    // ---- lifecycle -------------------------------------------------------------------------

    private void Awake() => Instance = this;

    private void OnEnable()
    {
        if (!ComfortSettings.IsBound)
            return;
        ComfortSettings.TableHeightOffset.Changed += OnHeightOffsetChanged;
    }

    private void OnDisable()
    {
        if (!ComfortSettings.IsBound)
            return;
        ComfortSettings.TableHeightOffset.Changed -= OnHeightOffsetChanged;
    }

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

        bool chordDown = hold > 0f
                         && left != null && right != null
                         && left.HasPose && right.HasPose
                         && left.SecondaryButton && right.SecondaryButton;

        if (!chordDown)
        {
            _chordHeldSeconds = 0f;
            _chordFired = false;
            return;
        }

        _chordHeldSeconds += Time.deltaTime;
        if (_chordFired || _chordHeldSeconds < hold)
            return;

        _chordFired = true;
        VRLog.Info("Comfort", "Recenter chord (B+Y held) — recentering.");
        RequestRecenter();
        left!.SendHaptic(HapticPreset.GrabPulse);
        right!.SendHaptic(HapticPreset.GrabPulse);
    }

    private void OnHeightOffsetChanged(float _) => RequestRecenter();
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
