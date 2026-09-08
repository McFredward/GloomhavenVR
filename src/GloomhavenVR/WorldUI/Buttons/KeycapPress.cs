using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>WHAT COUNTS AS A PRESS ON A KEYCAP LYING FLAT ON A TABLE — ONE VOCABULARY, TWO SKINS.</b>
///
/// <para>The mod builds two families of physical keycap and they are the same affordance twice:
/// <c>Cards.PlayTray.BoardButton</c>, the mod-owned mesh keys on the control board, and
/// <c>WorldUI.MapRoom.MapButtonRail.Cap</c>, the map table's rim caps, which wrap the game's own
/// <c>UIGuildmasterButton</c>s and sample the game's art. Before 2026-09-05 they answered the
/// question "did the player press this?" differently:</para>
///
/// <list type="bullet">
///   <item>a BOARD cap fired at <see cref="ButtonTuning.PressFireFraction"/> of its travel, re-armed
///   only after the cap rose back past <see cref="ButtonTuning.PressRearmFraction"/>, and was
///   debounced by <see cref="ButtonTuning.PokePressCooldownSeconds"/> across BOTH input paths;</item>
///   <item>a MAP cap fired on fingertip CONTACT — the 8 mm the interactor calls a touch — with no
///   travel requirement and no cooldown at all. Its 7 mm of travel was a post-hoc 70 ms animation
///   STARTED BY the press rather than the thing the press was measured from.</item>
/// </list>
///
/// <para><b>THE PLAYER'S VERSION OF THAT SENTENCE:</b> brushing CONFIRM on the board does nothing,
/// and brushing <i>Händler</i> on the map table opens the merchant. The board's principle is the
/// reference and this type is it, because it is the one that carries a reason — user requirement #6,
/// verbatim in <c>BoardButton.Update</c>: the press fires when the cap reaches the bottom, "so a
/// brush against the button does nothing and the press feels like actually pushing the key in".
/// A press must be INTENDED, and only travel can say that it was.</para>
///
/// <para><b>WHAT THIS TYPE DOES NOT UNIFY, DELIBERATELY.</b> The LOOK. Map caps drop
/// <c>NativeButtonSkin.CreateFace</c> and sample the game's own button art on a standing user
/// ruling ("die Symbole haben nun einen viereckigen Rahmen statt direkt auf dem button zu sitzen"),
/// they animate with a hold-and-spring rather than the board's authored
/// <see cref="ButtonStroke"/> curve, and their travel is the table's 7 mm rather than the board's
/// configurable 4 mm because a table cap is read from a metre away. None of that is what a press
/// IS. One press vocabulary, two skins.</para>
///
/// <para><b>THE SIX CHANNELS, AND WHERE EACH ONE STANDS.</b> The redundancy audit's R11 found the
/// two families diverging on six at once. This is the written contract for all six, so that the
/// next person to touch either family can see which differences are answers and which were
/// accidents:</para>
/// <list type="number">
///   <item><b>Commit rule</b> — SHARED, here. Travel-depth fire with re-arm hysteresis. The map
///   caps' contact-fire is gone.</item>
///   <item><b>Debounce</b> — SHARED, here. <see cref="ButtonTuning.PokePressCooldownSeconds"/>
///   across every physical path of both families. The map caps had none.</item>
///   <item><b>Press haptic</b> — SHARED RULE, each family's own call: one
///   <c>HapticPreset.ClickPulse</c> at the single commit point, after every gate. The map caps had
///   none at all; a laser press on one was silent to the hand.</item>
///   <item><b>Disabled caption</b> — SHARED FACTOR,
///   <see cref="NativeButtonSkin.DisabledLabelDim"/>. Each family keeps its own base colour. The
///   board's caption did not dim at all.</item>
///   <item><b>Hover visual</b> — <b>DELIBERATELY STILL DIFFERENT.</b> A map cap warms 45 % toward
///   <see cref="NativeButtonSkin.LabelColor"/> under a pointer; a board keycap does not change
///   colour at all. This is NOT the same omission as the four above, because a board keycap is not
///   silent under the beam: it ticks (<c>BoardButton.OnPokeEnter</c>, which the laser path calls
///   too) and the beam visibly clamps onto it. What a board cap would have to warm is a sampled
///   keycap-atlas face under a per-category <c>[ButtonColors]</c> tint the user has tuned through
///   six rounds; a map cap is a flat sampled sprite with no such stack. Adding an unasked colour
///   term on top of that is a LOOK change to the mod's most-tuned object, and the look is exactly
///   what audit section 4 item 6 protects. If a round ever asks for it, the shared answer is the
///   45 % lerp — put it here, do not write a second one.</item>
///   <item><b>Disabled input</b> — <b>DELIBERATELY STILL DIFFERENT, and both sides carry an
///   argument.</b> A board keycap keeps its collider LIVE while disabled and routes the press to
///   <c>BoardButton.Press</c>, which logs REJECTED with the exact gate state — test #14's
///   requirement that "a silent dead button can no longer happen", which is a diagnostic the
///   project has spent hardware rounds on. A map cap sets <c>Collider.enabled = live</c> and is
///   physically inert to finger and beam — its own "HONEST AFFORDANCE" ruling, and the second half
///   of the ModBuild 200 defect where a cap drawn dead was still pressable. Neither is wrong; they
///   are answers to two different questions (is a dead control DIAGNOSABLE, is a dead control
///   HONEST). Left as it stands, named here so it is a decision and not a discovery.</item>
/// </list>
///
/// <para><b>WHAT THIS TYPE DOES NOT DECIDE.</b> Whether the press is ALLOWED — the board's grip
/// chord, the map cap's <c>Pressable</c> predicate, the modal commit gate, the activation guard.
/// Those are policy and they belong to the owner; this is only the question of whether a gesture
/// happened. The gate is stepped, then the owner applies its own refusals, then the owner commits.
/// That order is why a refusal never consumes the arm.</para>
///
/// <para><b>MULTIPLAYER: nothing here goes on the wire.</b> A gate is one bool and one clock per
/// cap, driven by one local hand's local fingertip. What DOES cross is each family's own press
/// EDGE, reported from each family's own commit point after every gate (<c>BoardCapPress.Report</c>
/// for the board; the map room's surface mirror for the table), which is unchanged by this.</para>
/// </summary>
internal static class KeycapPress
{
    /// <summary>
    /// <b>HOW DEEP THE FINGERTIP IS INTO A KEYCAP, AS A FRACTION OF THAT CAP'S TRAVEL.</b> The
    /// single implementation of the measurement both families' depth-fire rides on, and the reason
    /// the two can agree at all: it is the same tip/collider probe
    /// <c>PokeInteractor</c> uses for contact, so "the finger is touching" and "the cap is at the
    /// bottom" are answers about the same geometry rather than two independent guesses.
    ///
    /// <para>penetration = <paramref name="fingertipRadius"/>·worldScale − distance(tip → nearest
    /// point on the collider), converted through the cap's WORLD depth scale so the key follows the
    /// finger in real space rather than in local units, and clamped to one full travel.</para>
    ///
    /// <para>Framerate-independent by construction: a pure function of where the fingertip is this
    /// frame, with no accumulation. That matters — an integrating version would drift on a dropped
    /// frame and fire a press the player did not make.</para>
    /// </summary>
    /// <param name="collider">The cap's own trigger collider.</param>
    /// <param name="depthScale">The cap's world scale along its travel axis (normally
    /// <c>Mathf.Abs(transform.lossyScale.z)</c> of the object the travel is authored on).</param>
    /// <param name="travelLocal">Full press travel in the cap's LOCAL units.</param>
    /// <param name="fingertipRadius">Contact radius at rig scale 1 — pass the caller's own
    /// mirrored copy of <c>PokeInteractor.FingertipRadius</c>, never a fresh literal. It stays a
    /// parameter rather than being read here because that constant's declaration carries a written
    /// ruling against exporting it, and the copies are held together by
    /// <c>scripts/check-mirrors.sh</c> instead.</param>
    internal static float FollowDepth01(Collider? collider, VRHand hand, float fingertipRadius,
                                        float travelLocal, float depthScale)
    {
        if (collider == null || !hand.HasPose)
            return 0f;
        Vector3 tip = hand.Rig.IndexTip.position;
        float dist = Vector3.Distance(tip, collider.ClosestPoint(tip));
        float penetration = fingertipRadius * hand.WorldScale - dist;
        if (penetration <= 0f)
            return 0f;
        float travelWorld = travelLocal * Mathf.Abs(depthScale);
        return travelWorld > 1e-6f ? Mathf.Clamp01(penetration / travelWorld) : 0f;
    }
}

/// <summary>
/// <b>ONE KEYCAP'S PRESS STATE — the depth-fire hysteresis and the shared debounce, as a value.</b>
/// See <see cref="KeycapPress"/> for what this vocabulary is and why the board's is the reference.
///
/// <para>A struct with two fields, held BY VALUE on the cap it belongs to. The default value is
/// the correct initial state (armed, cooldown expired), which is why the negated field name below
/// is not a stylistic choice: a bool field defaults to false, and the state a fresh cap must start
/// in is ARMED.</para>
///
/// <para><b>THE TWO TERMS ARE INDEPENDENT AND BOTH ARE NEEDED.</b> The hysteresis
/// (<see cref="AtFireDepth"/> → <see cref="TryFireFromDepth"/>) stops a finger resting at the
/// bottom of a key from machine-gunning it: the cap must rise back past
/// <see cref="ButtonTuning.PressRearmFraction"/> before another press can fire. The cooldown
/// (<see cref="TryCommit"/>) stops the re-fire the hysteresis structurally cannot see — a
/// PokeInteractor hover flicker that exits and re-enters inside one physical poke, and a
/// cross-path poke+laser double inside the same window. A laser click has no travel to measure,
/// so it takes the cooldown alone, which is exactly why the cooldown is a separate term.</para>
/// </summary>
internal struct KeycapPressGate
{
    /// <summary>NEGATED so that <c>default</c> means ARMED (see the type doc). True while the cap
    /// still owes a retract past the re-arm fraction before it may fire again.</summary>
    private bool _awaitingRetract;

    /// <summary>Unscaled time before which no press of any kind commits on this cap.</summary>
    private float _nextPressAt;

    /// <summary>Log material: is this cap ready to fire on the next bottom-out?</summary>
    internal bool Armed => !_awaitingRetract;

    /// <summary>
    /// Step the hysteresis with this frame's normalised travel and answer whether the fingertip is
    /// AT the fire depth. Returning true is not a press — it is "the gesture happened"; the caller
    /// then applies its own refusals and calls <see cref="TryFireFromDepth"/>.
    ///
    /// <para>The re-arm is done HERE, on the retract, rather than at the commit, so a refusal (a
    /// missing grip chord, a cap the game would not accept) leaves the gate exactly as it found it
    /// and the player's next honest push still fires.</para>
    /// </summary>
    internal bool AtFireDepth(float depth01)
    {
        if (depth01 >= ButtonTuning.PressFireFraction)
            return true;
        if (depth01 <= ButtonTuning.PressRearmFraction)
            _awaitingRetract = false;
        return false;
    }

    /// <summary>Called when the cap stops being under a finger at all (hover lost, hidden, torn
    /// down): a cap nobody is touching is a cap that has retracted.</summary>
    internal void Rearm() => _awaitingRetract = false;

    /// <summary>
    /// The depth path's commit test: armed AND out of cooldown. Consumes the arm on success — the
    /// caller must then actually commit, because this has already spent the gesture.
    ///
    /// <para>It does NOT stamp the cooldown. The owner's single commit entry point does that
    /// through <see cref="TryCommit"/>, so that both input paths are debounced by one clock
    /// written in one place and a poke can never bypass the stamp a laser click applies.</para>
    /// </summary>
    internal bool TryFireFromDepth()
    {
        if (_awaitingRetract || Time.unscaledTime < _nextPressAt)
            return false;
        _awaitingRetract = true;
        return true;
    }

    /// <summary>
    /// <b>THE SINGLE COMMIT TEST, for every input path alike</b> — the fingertip depth-fire (which
    /// has already pre-checked the same window, so a legitimate poke always passes here and
    /// re-stamps it) and the laser click (which has no travel and is debounced by this alone).
    /// Stamps the cooldown on success. Returns false when the press is inside the window, i.e. it
    /// is the caller's cue to log a DEBOUNCED line rather than to act.
    /// </summary>
    internal bool TryCommit()
    {
        if (Time.unscaledTime < _nextPressAt)
            return false;
        _nextPressAt = Time.unscaledTime + ButtonTuning.PokePressCooldownSeconds;
        return true;
    }
}
