using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Shared per-frame tracker for the NON-dominant lower face button (A/X) — the one
/// button that carries TWO hold chords (P6):
///
/// - settings panel: short hold, fires on RELEASE (<see cref="SettingsPanel"/>),
/// - manual flat-screen toggle: long hold, fires AT the threshold while still held
///   (<see cref="FlatScreen"/>, the in-scenario self-rescue).
///
/// One tracker, one truth: both consumers read the same press, and the long-hold
/// consumer marks the press <see cref="Consumed"/> so its release cannot ALSO
/// trigger the short-hold action. Ticked exactly once per frame by the WorldUI
/// driver, before any consumer.
///
/// A THIRD gesture rides the same tracker: a short TAP — press then release BELOW
/// the settings-panel hold threshold, with the press left unconsumed by any hold
/// chord — surfaces as <see cref="ShortTapThisFrame"/> (the game OPTIONS window
/// toggle, <see cref="OptionsToggle"/>). Taps and holds never collide: both hold
/// chords act at/after their threshold (the long chords fire while STILL held and
/// mark the press <see cref="Consumed"/>; the settings short-hold requires
/// <see cref="ReleasedAfterSeconds"/> ≥ the threshold), so a clean sub-threshold
/// release belongs to the tap alone.
/// </summary>
internal static class NonDominantHold
{
    /// <summary>Tap window when the settings chord is disabled (ChordHoldSeconds = 0) — still allow a quick tap.</summary>
    private const float TapFallbackSeconds = 0.35f;
    /// <summary>Seconds the button has been held so far in the current press (0 while up).</summary>
    internal static float HeldSeconds { get; private set; }

    /// <summary>True only on the frame the button was released.</summary>
    internal static bool ReleasedThisFrame { get; private set; }

    /// <summary>Duration of the press that just ended (valid while <see cref="ReleasedThisFrame"/>).</summary>
    internal static float ReleasedAfterSeconds { get; private set; }

    /// <summary>Set by a chord that consumed the CURRENT press — later consumers skip it. Reset on the next press.</summary>
    internal static bool Consumed { get; set; }

    /// <summary>
    /// True only on the frame a short TAP completed: an unconsumed press released
    /// below the settings-panel hold threshold (the game OPTIONS window toggle).
    /// </summary>
    internal static bool ShortTapThisFrame { get; private set; }

    /// <summary>The tracked non-dominant hand (for haptic confirms), null without a pose.</summary>
    internal static VRHand? Hand { get; private set; }

    /// <summary>
    /// True only when the button is OBSERVABLY up this frame: the hand has a pose AND
    /// its PrimaryButton is not pressed. False while the button is held AND false during
    /// a pose hiccup (we cannot confirm "up" without a pose). The press-cycle latch in
    /// <see cref="OptionsToggle"/> re-arms only on this signal, so a held/hiccuping button
    /// can never re-toggle without a genuine, observed physical release.
    /// </summary>
    internal static bool ButtonIsUp { get; private set; }

    private static bool _wasDown;

    /// <summary>Whether the non-dominant hand had a pose LAST frame (phantom-edge guard).</summary>
    private static bool _hadHand;

    internal static void Tick()
    {
        VRHand? primary = VRHands.Primary;
        VRHand? hand = primary == null ? null
            : VRHands.Get(primary.Side == HandSide.Left ? HandSide.Right : HandSide.Left);
        Hand = hand != null && hand.HasPose ? hand : null;
        bool handPresent = Hand != null;

        ReleasedThisFrame = false;
        ReleasedAfterSeconds = 0f;
        ShortTapThisFrame = false;

        if (!handPresent)
        {
            // POSE HICCUP (or ClearInput, which only fires alongside loss of tracking):
            // FREEZE the press. Do NOT manufacture a release edge — the physical button
            // may still be held, and a phantom release would be re-pressed the instant
            // the pose returns and read as a spurious tap (this was the real source of the
            // X-menu flicker). Leave HeldSeconds and _wasDown untouched so the press
            // resumes seamlessly. Not observably up, so the latch cannot re-arm here.
            ButtonIsUp = false;
            _hadHand = false;
            return;
        }

        bool down = Hand!.PrimaryButton;

        if (down)
        {
            if (!_wasDown)
                Consumed = false; // a fresh press starts unconsumed
            HeldSeconds += Time.unscaledDeltaTime; // unscaled: a paused modal (timeScale 0) must not freeze tap/hold timing
        }
        else
        {
            // Fire a release ONLY when the hand was present this frame AND last frame; a
            // release on the frame the pose returns is a phantom (button state during the
            // gap is unknown) — suppress it and just resync HeldSeconds.
            if (_wasDown && _hadHand)
            {
                ReleasedThisFrame = true;
                ReleasedAfterSeconds = HeldSeconds;
            }
            HeldSeconds = 0f;
        }
        _wasDown = down;
        ButtonIsUp = !down;
        _hadHand = true;

        // Short-TAP edge: a fresh press released BELOW the settings-panel hold
        // threshold and NOT consumed by a hold chord. Consumed reflects the hold
        // chords (they set it WHILE held, i.e. before this release frame) and no
        // consumer sets it on a release frame, so reading it here is settled. When
        // the settings chord is disabled (ChordHoldSeconds ≤ 0) a fixed fallback
        // window keeps the tap alive.
        if (ReleasedThisFrame && !Consumed)
        {
            float tapMax = WorldUIConfig.SettingsChordHoldSeconds.Value;
            if (tapMax <= 0f)
                tapMax = TapFallbackSeconds;
            ShortTapThisFrame = ReleasedAfterSeconds < tapMax;
        }
    }

    /// <summary>Hot-reload / driver teardown reset.</summary>
    internal static void Reset()
    {
        HeldSeconds = 0f;
        ReleasedThisFrame = false;
        ReleasedAfterSeconds = 0f;
        ShortTapThisFrame = false;
        Consumed = false;
        Hand = null;
        ButtonIsUp = false;
        _wasDown = false;
        _hadHand = false;
    }
}
