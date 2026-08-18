using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Shared per-frame tracker for the NON-dominant lower face button (A/X) — the one
/// button that carries both hold chords (P6), each firing AT the shared
/// <c>[WorldUI] ManualScreenChordSeconds</c> threshold while the button is STILL held:
///
/// - modal escape chord: closes the top floating modal (<see cref="ModalFallback"/>) —
///   the test-#17 hard-lock guarantee. Runs FIRST, so while a modal floats the press
///   means "close it".
/// - manual flat-screen toggle: the in-scenario self-rescue (<see cref="FlatScreen"/>).
///
/// One tracker, one truth: both consumers read the same press and mark it
/// <see cref="Consumed"/>, so one physical press can only ever produce one action.
/// Ticked exactly once per frame by the WorldUI driver, before any consumer.
///
/// A TAP rides the same tracker: press and release inside <c>TapFallbackSeconds</c>
/// with the press left unconsumed by either chord surfaces as
/// <see cref="ShortTapThisFrame"/> (the game OPTIONS window toggle,
/// <see cref="OptionsToggle"/>). Taps and holds never collide: a chord has already
/// consumed the press before the release frame, so a clean sub-window release belongs
/// to the tap alone.
/// </summary>
internal static class NonDominantHold
{
    /// <summary>The tap window. It is the only boundary left: the mod's settings panel and its
    /// configurable short-hold are gone (see the ShortTapThisFrame edge below).</summary>
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
    /// inside <c>TapFallbackSeconds</c> (the game OPTIONS window toggle).
    /// </summary>
    internal static bool ShortTapThisFrame { get; private set; }

    /// <summary>The tracked non-dominant hand (for haptic confirms), null only when the
    /// controller instance is absent (hot reload / headset off). Present even while the
    /// controller is positionally UNTRACKED — haptics and the face button still work.</summary>
    internal static VRHand? Hand { get; private set; }

    /// <summary>
    /// True only when the button is OBSERVABLY up this frame: the controller is present
    /// AND its PrimaryButton is not pressed. False while the button is held AND false
    /// while the controller instance is absent. The press-cycle latch in
    /// <see cref="OptionsToggle"/> re-arms only on this signal, so a held button can never
    /// re-toggle without a genuine, observed physical release — but a merely stationary
    /// (untracked) controller still re-arms it, since its button is read all the same.
    /// </summary>
    internal static bool ButtonIsUp { get; private set; }

    private static bool _wasDown;

    /// <summary>
    /// Monotonic identity of the CURRENT (or most-recent) physical press, bumped once on
    /// each fresh down-edge. It lets <see cref="OptionsToggle"/> tell the press that
    /// CLOSED the pause menu (the game's own gamepad-escape on the button, or a laser
    /// click on the menu) apart from the fresh, independent press that must RE-OPEN it:
    /// one physical X press serves exactly one intent, so the close-press can never
    /// double-serve as the re-open tap and the next press always opens (P6 reopen fix).
    /// </summary>
    internal static int PressId { get; private set; }

    /// <summary>Whether the non-dominant controller was present LAST frame (phantom-edge guard).</summary>
    private static bool _hadHand;

    internal static void Tick()
    {
        VRHand? primary = VRHands.Primary;
        VRHand? hand = primary == null ? null
            : VRHands.Get(primary.Side == HandSide.Left ? HandSide.Right : HandSide.Left);

        // OBSERVE THE BUTTON INDEPENDENTLY OF POSITIONAL TRACKING (X-menu "opens only
        // once" fix). A Quest / Virtual Desktop controller that is held still — resting
        // after the player reads the floated pause menu, or simply lowered — drops to
        // isTracked=false, so VRHand.HasPose (== IsTracked) goes false. But VRHand still
        // reads its PrimaryButton from the VALID device every frame (VRHand.cs:346/383 —
        // the tracked gate skips only the pose, never the button). The old HasPose gate
        // here therefore FROZE the whole tracker the moment the non-dominant hand went
        // idle: the pause-menu tap was seen exactly once and every later tap vanished
        // (no ShortTap edge, nothing logged, no re-open). Gate on the controller INSTANCE
        // instead — its button is trustworthy whenever it exists (a truly gone device is
        // ClearInput'd to a clean "up", VRHand.cs:341/428, so it reads as no press, never
        // a phantom). Positional pose is irrelevant to a face-button tap.
        Hand = hand;
        bool handPresent = hand != null;

        ReleasedThisFrame = false;
        ReleasedAfterSeconds = 0f;
        ShortTapThisFrame = false;

        if (!handPresent)
        {
            // NO CONTROLLER INSTANCE at all (hot reload / headset removed): freeze the
            // press. Do NOT manufacture a release edge — a phantom release would be
            // re-pressed when the controller returns and read as a spurious tap. Leave
            // HeldSeconds and _wasDown untouched so the press resumes seamlessly. Not
            // observably up, so the latch cannot re-arm here.
            ButtonIsUp = false;
            _hadHand = false;
            return;
        }

        bool down = hand!.PrimaryButton;

        if (down)
        {
            if (!_wasDown)
            {
                Consumed = false; // a fresh press starts unconsumed
                PressId++;        // ...and carries a new identity (reopen fix)
            }
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

        // Short-TAP edge: a fresh press released inside the tap window and NOT consumed by a hold
        // chord. Consumed reflects the hold chords (they set it WHILE held, i.e. before this
        // release frame) and no consumer sets it on a release frame, so reading it here is settled.
        //
        // The boundary used to be [SettingsPanel] ChordHoldSeconds, because the tap had to stay
        // BELOW the short hold that opened the mod's settings panel. That panel is gone — its
        // settings live in the game's own options window now — so there is no chord to stay below
        // and no reason to keep a tuning knob for it. The fallback window this code already used
        // whenever the chord was disabled is simply the window.
        if (ReleasedThisFrame && !Consumed)
            ShortTapThisFrame = ReleasedAfterSeconds < TapFallbackSeconds;


        // SELF-HEAL belt-and-suspenders. Now that the button is observed every frame the
        // controller instance exists (above), a stranded latch is no longer possible from
        // mere tracking loss. This still clears any residual press/Consumed latch the
        // instant the button is observed genuinely up on a settled frame — e.g. a full
        // controller-absence gap that straddled a press — so the next genuine press is
        // always a fresh edge (down && !_wasDown) whose release yields a tap.
        //
        // ReleasedThisFrame guards the consumed-hold release frame (Consumed must stay
        // true through that frame's tap check so the hold's release never emits a phantom
        // tap); the heal then runs on the following settled-up frame, harmlessly, since
        // the press is already over.
        if (ButtonIsUp && !ReleasedThisFrame && (_wasDown || Consumed))
        {
            _wasDown = false;
            Consumed = false;
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
        PressId = 0;
        _hadHand = false;
    }
}
