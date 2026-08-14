using System;
using GloomhavenVR.Board;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;

namespace GloomhavenVR.Rig;

/// <summary>
/// IS ANYTHING REALLY READING MY STICK? The one question <see cref="VRMode.BoardTargeting"/> does
/// not answer, and the reason board state used to freeze stick turning — first on every peer in
/// the session, then, one scope narrower, on the acting player himself.
///
/// <para>USER REPORT (hardware, ModBuild 137, finding 14): "Während dessen ein Mitspieler gerade
/// eine Bewegung bestätigen musste konnte keiner der Mitspieler (inklusive mir) sich mehr mit dem
/// Joystick drehen. Wenn man außerhalb dieser Entscheidung eines Mitspielers ist geht es wieder bei
/// jedem. Das darf nicht sein."</para>
///
/// <para>ROOT CAUSE. <c>VRModeStateMachine</c> derives <c>BoardTargeting</c> from the CHOREOGRAPHER
/// STATE (<c>VRModeStateMachine.cs:143-150, 348-355</c>: <c>TargetingStates</c> ∋
/// <c>WaitingForPlayerWaypointSelection</c> …), and the Choreographer is the game's SHARED,
/// networked scenario state machine — it is not a per-client view of "what am *I* being asked to
/// do". When any player in the session is placing a waypoint and confirming a move, every client's
/// Choreographer sits in that wait state, so every client's mode machine reports BoardTargeting.
/// <see cref="SnapTurn"/> then hard-disabled turning (<c>SnapTurn.cs:88</c>) and
/// <see cref="Flight"/> stood the strafe axis down (<c>Flight.cs:303</c>) on ALL of them. The
/// documented justification for both was "targeting owns the thumbstick", i.e. AoE pattern rotation
/// — a GAME action.</para>
///
/// <para>THE PROOF THAT THE SUPPRESSION WAS PURE LOSS ON THE OTHER CLIENTS: the claimed consumer
/// refuses to consume there. <c>AoeControl.CanRotate</c> (Board/AoeControl.cs:112-135) starts with
/// <c>if (choreographer == null || !choreographer.ThisPlayerHasTurnControl) return false;</c>. So on
/// a client that does not hold turn control, the stick's sideways axis rotates nothing at all —
/// there is no contention to arbitrate, and the arbitration was taking a control away in favour of
/// a consumer that had already declined it.</para>
///
/// <para>THE PREDICATE. <c>Choreographer.ThisPlayerHasTurnControl</c> (decompiled
/// GH.Runtime/Choreographer.cs:557-580) is the game's OWN "is this seat the acting seat" test —
/// the same one <c>ReadyButton</c> (:501), <c>UndoButton</c> (:285), <c>SkipButton</c> (:162) and
/// <c>WorldspaceStarHexDisplay</c> (:442, :469) gate their interactability on. It returns true when
/// <c>!FFSNetwork.IsOnline</c> (so SINGLE PLAYER BEHAVIOUR IS BIT-IDENTICAL to before this file
/// existed), true when there is no current actor, true when <c>m_CurrentActor.IsUnderMyControl</c>,
/// and false when the acting actor belongs to somebody else — which is exactly the reported case
/// (a fellow player confirming a movement) and also the enemies' turn.</para>
///
/// <para>THE RULE THIS ENCODES (user, standing): another actor's turn or decision may gate GAME
/// actions; it may NEVER gate the local player's locomotion or view. Locomotion is comfort, it is
/// local-only, and it must not depend on remote state. That is the inverse of the project's usual
/// multiplayer 1:1 ruling (STATE.md §3) and is deliberate: §3 is about what other players SEE of
/// you, never about what your own body may do.</para>
///
/// <para>SECOND REPORT (hardware, ModBuild 138): "Wenn ich die Bewegung bei einem Character
/// ausgewählt habe und das Feld auswählen soll, kann ich immer noch nicht mit dem joystick drehen.
/// Die drehung soll nie blockiert sein!" The multiplayer half was fixed — his own log carries only
/// the "board targeting is MINE" verdict, never the other one — so what remained was the LOCAL
/// case, and with it the ruling HARDENED from "not another seat's targeting" to <b>TURN NEVER</b>.
/// Turning may not be suppressed by board state at all.</para>
///
/// <para>THE SAME MISTAKE, ONE SCOPE NARROWER. Picking a movement ability and then a destination
/// hex puts the local, ACTING player into BoardTargeting with turn control — so the ModBuild-137
/// gate said "MINE" and stood turning down. But the claimed consumer declines there too:
/// <c>AoeControl.CanRotate</c> demands <c>CurrentDisplayState == TargetSelection</c> (movement
/// selection reports <c>MovementSelection</c>), an AoE ability display type, <c>AbilityRange
/// &gt; 1</c>, and refuses outright in <c>WaitingForTileSelected</c> — which is the wait state the
/// report describes. Not one of those holds while a waypoint is being placed. The stick was again
/// taken for a consumer that had already declined it, exactly as on the remote peers.</para>
///
/// <para>SO THE GATE NOW ASKS THE CONSUMER, NOT THE MODE (change a). <see cref="AoeOwnsStick"/>
/// forwards to <c>AoeControl.ClaimsStick</c>, which is computed from the very gates
/// <c>AoeControl.Tick</c> applies. Board STATE never suppresses a comfort control again; only a
/// consumer that would really read that same physical axis this frame can.</para>
///
/// <para>AND EVEN THAT CLAIM MAY NOT COST THE PLAYER THEIR TURNING (change b), because "never"
/// admits no exception. <c>AoeControl.ResolveRotationHand</c> moves pattern rotation onto the hand
/// that is NOT <c>[Comfort] TurnHand</c>, so the claim structurally cannot land on the turn stick
/// while turning is on. That makes the gate below a belt-and-braces that should now read false
/// forever — kept, not deleted, because it is the thing that stays true if a future change ever
/// puts a second consumer on the turn stick, and because a log line saying so is how the next
/// report gets diagnosed in one round instead of three.</para>
///
/// <para>SUPERSEDED (was: REJECTED) — gating on the full <c>AoeControl.CanRotate</c> predicate. The
/// ModBuild-137 pass rejected it, arguing that inside the local player's own turn the two controls
/// really contend and a frame-accurate handover would make turning flicker while aiming. The second
/// report is that argument being wrong in practice: the "contention" it protected was mostly
/// imaginary (movement selection is not target selection), and the flicker it feared is now
/// impossible anyway, because after (b) the two controls no longer share a stick to hand over.</para>
///
/// <para>REJECTED: adding the ownership test to <c>VRModeStateMachine</c> itself, so that a
/// non-acting client never enters BoardTargeting at all. It is the smaller diff and the wrong
/// change: that mode ALSO carries the interactor policy (Ray|Poke, dominant-hand-only laser —
/// <c>VRModeStateMachine.cs:167,192-193</c>), the fingertip-ping arbitration
/// (<c>TargetingActive</c>, :120) and the board-pick behaviour a SPECTATOR still wants while
/// watching a teammate aim. One report about locomotion must not silently re-cut every one of
/// those. The mode keeps meaning "the board is waiting for a target"; this file answers the
/// separate question of whose target it is, at the two consumers that actually had it wrong.</para>
///
/// <para>REJECTED: reading a mod-side "is it my turn" from <c>Net/**</c>, or reusing
/// <c>CharacterFocus.LocalOwnsTurn</c> (Board/CharacterFocus.cs:444). Both are correct as far as
/// they go, but the contention being arbitrated here is with <c>AoeControl</c>, and AoeControl gates
/// on <c>ThisPlayerHasTurnControl</c>. Arbitrating a consumer with a DIFFERENT predicate than the
/// consumer uses is how a gate ends up disagreeing with the thing it gates — the two would part
/// company on mind-controlled enemies and on summons, both of which that property handles and
/// neither of which a hero-actor test does.</para>
///
/// <para>REJECTED: deleting AoE stick rotation instead, which would also satisfy "turning is never
/// blocked". It is a working game control the user has never complained about, and the keyboard
/// path it mirrors (<c>ROTATE_TARGET_BUTTON</c>) has no VR equivalent — removing it would make
/// ranged AoE abilities unaimable in VR to fix a locomotion complaint.</para>
///
/// <para>REJECTED: keeping AoE on the primary hand and simply letting BOTH consumers read the
/// x axis at once. The flick thresholds overlap (turn engages at 0.7, rotation at the configured
/// 0.2–0.95 flick threshold), so one push would yaw the world AND spin the pattern — the same
/// class of accidental double-action as the scroll-vs-turn complaint of 2026-08-11, and the user
/// has already ruled on that shape of bug once.</para>
///
/// <para>REJECTED: a config entry to choose the AoE rotation hand. Standing ruling — settings may
/// configure optional content, never repair a broken control. The hand is DERIVED from
/// <c>TurnHand</c>, so it follows the dial the player already set and cannot be mis-set into the
/// collision this fix exists to remove.</para>
/// </summary>
internal static class LocalTurnControl
{
    /// <summary>Last logged verdict of <see cref="TargetingOwnsStick"/>, so the line is edge-only.</summary>
    private static bool? _logged;

    /// <summary>
    /// Does the LOCAL seat hold turn control right now? Straight from the game
    /// (<c>Choreographer.ThisPlayerHasTurnControl</c>), never mirrored.
    ///
    /// <para>Fails OPEN-FOR-THE-PLAYER in both degenerate cases — no Choreographer, or the property
    /// throwing — by answering <c>false</c>, i.e. "nothing here claims the stick". For a gate whose
    /// only job is to take a comfort control away, the safe answer is always to leave it alone.</para>
    ///
    /// <para><c>AoeControl.CanRotate</c> asks this too, and <c>false</c> is the safe answer there
    /// as well: it means "do not rotate the pattern", which is exactly what the hand-written
    /// <c>choreographer == null || !ThisPlayerHasTurnControl</c> it replaced did. One predicate for
    /// both the consumer and the arbitration that quotes the consumer — see the class doc's
    /// standing rejection of arbitrating a consumer with a different test than it uses.</para>
    /// </summary>
    internal static bool ThisSeatActs
    {
        get
        {
            // Unity-null: a destroyed Choreographer compares equal to null.
            Choreographer? choreographer = Choreographer.s_Choreographer;
            if (choreographer == null)
                return false;
            try
            {
                return choreographer.ThisPlayerHasTurnControl;
            }
            catch (Exception ex)
            {
                VRLog.Error("Comfort", $"TURN GATE: Choreographer.ThisPlayerHasTurnControl threw — " +
                                       $"treating the stick as free so locomotion cannot be lost to it. {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// Resolve a <see cref="TurnHandChoice"/> to a physical controller. <c>Dominant</c> is not a
    /// hand, it is a pointer to one (<c>[Hands] PrimaryHand</c>) — resolve before comparing, or
    /// "Dominant vs Right" reads as two different hands on a right-handed rig.
    ///
    /// <para>Right when the hands are down: with no <c>VRHands.Primary</c> there is nothing to
    /// follow, and Right is both the shipped <c>PrimaryHand</c> and the shipped <c>TurnHand</c>,
    /// so the answer matches what will be true a frame after the hands come up.</para>
    ///
    /// <para>ONE resolver for the whole arbitration: <see cref="Flight"/> compares two choices with
    /// it and <c>AoeControl</c> takes the OPPOSITE of it. Two copies of this switch would be two
    /// places to disagree about which stick is which.</para>
    /// </summary>
    internal static HandSide Resolve(TurnHandChoice choice) => choice switch
    {
        TurnHandChoice.Left => HandSide.Left,
        TurnHandChoice.Right => HandSide.Right,
        _ => VRHands.Primary != null ? VRHands.Primary.Side : HandSide.Right,
    };

    /// <summary>
    /// Is a REAL AoE-rotation claim sitting on this specific physical stick right now?
    ///
    /// <para>The single place where "the board is targeting" is turned into "something is reading
    /// MY stick". The answer comes from the consumer itself (<c>AoeControl.ClaimsStick</c>), never
    /// from <see cref="VRMode"/> — see the class doc for the two reports that made the mode the
    /// wrong question. Silent by design: the caller that owns the turn stick logs
    /// (<see cref="TargetingOwnsStick"/>), and <see cref="Flight"/> already logs its own strafe
    /// verdict, so a shared line here would just fight between them.</para>
    ///
    /// <para>Fails to "no claim" on a throw, like everything else in this file.</para>
    /// </summary>
    internal static bool AoeOwnsStick(HandSide side)
    {
        try
        {
            return AoeControl.ClaimsStick(side);
        }
        catch (Exception ex)
        {
            VRLog.Error("Comfort", "TURN GATE: AoeControl.ClaimsStick threw — treating the stick as " +
                                   $"free so locomotion cannot be lost to it. {ex}");
            return false;
        }
    }

    /// <summary>
    /// Does anything really claim the TURN stick — i.e. is stick turning legitimately suppressed?
    ///
    /// <para>After the ModBuild-138 hand split this should be false forever: <c>AoeControl</c>
    /// resolves its stick as the one <c>[Comfort] TurnHand</c> does NOT use whenever turning is
    /// on, and when turning is Off <see cref="SnapTurn"/> has already returned before it asks.
    /// The property stays because the log line below is the diagnostic that proves it, and because
    /// it is the correct shape of test if a second stick consumer is ever added.</para>
    ///
    /// <para><c>ComfortGizmos</c> also renders this, which is why it stays a parameterless property
    /// meaning "the turn stick specifically" rather than "some stick somewhere": the dev overlay
    /// must show the gate <see cref="SnapTurn"/> actually applies, not a claim on the other hand.</para>
    /// </summary>
    internal static bool TargetingOwnsStick
    {
        get
        {
            // Scoped to targeting purely so the diagnostic below stays quiet elsewhere; the claim
            // itself re-checks the mode. Unbound config = nothing to resolve a turn hand from, and
            // the fail-open answer is "no claim".
            if (VRModeStateMachine.CurrentMode != VRMode.BoardTargeting || !ComfortSettings.IsBound)
            {
                _logged = null; // next entry into targeting logs its verdict afresh
                return false;
            }

            bool claimed = AoeOwnsStick(Resolve(ComfortSettings.TurnHand.Value));
            if (_logged != claimed)
            {
                _logged = claimed;
                VRLog.Info("Comfort", claimed
                    ? "TURN GATE: a live AoE pattern would really rotate on the TURN stick this frame, "
                      + "so turning stands down. This line is not expected to appear at all — AoeControl "
                      + "resolves its stick as the one [Comfort] TurnHand does not use. If you are "
                      + "reading it, the two resolved to one controller and the ModBuild-138 hand split "
                      + "is not doing its job."
                    : "TURN GATE: board targeting is up and NOTHING claims the turn stick — turning and "
                      + "strafing stay live. Board state alone never suppresses them (TURN NEVER, user "
                      + "ModBuild 138: \"Die drehung soll nie blockiert sein!\"); only a consumer that "
                      + "would really read this same axis this frame can, and AoE rotation reads the "
                      + "other hand's stick.");
            }
            return claimed;
        }
    }
}
