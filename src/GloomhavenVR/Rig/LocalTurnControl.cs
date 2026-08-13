using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;

namespace GloomhavenVR.Rig;

/// <summary>
/// WHOSE DECISION IS THE BOARD WAITING FOR? The one question <see cref="VRMode.BoardTargeting"/>
/// does not answer, and the reason a fellow player's pending confirmation used to freeze every
/// player's snap turn.
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
/// <para>REJECTED: gating on the full <c>AoeControl.CanRotate</c> predicate (display state ==
/// TargetSelection, ability type, <c>AbilityRange &gt; 1</c>, <c>LockView</c>, pause). It would
/// hand the stick back in more cases still — but every one of those extra cases is INSIDE the local
/// player's own turn, where the two controls really do contend and where a frame-accurate handover
/// would make turning flicker on and off while the player aims. The question the report asks is
/// "whose decision is this", and that is answered by turn control alone.</para>
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
/// <para>REJECTED: dropping the BoardTargeting suppression entirely. During the local player's own
/// AoE aim the contention is real — one physical stick, two consumers — and the existing ruling
/// (turning yields to the aimed pattern) is the user's.</para>
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
    /// True only when board targeting is up AND it is THIS player's targeting — the one case in
    /// which the AoE pattern really is competing for the sideways stick axis.
    ///
    /// <para>Both locomotion consumers ask this instead of testing the mode directly, so there is
    /// exactly one place where "targeting" is turned into "MY targeting".</para>
    /// </summary>
    internal static bool TargetingOwnsStick
    {
        get
        {
            if (VRModeStateMachine.CurrentMode != VRMode.BoardTargeting)
            {
                _logged = null; // next entry into targeting logs its verdict afresh
                return false;
            }

            bool mine = ThisSeatActs;
            if (_logged != mine)
            {
                _logged = mine;
                VRLog.Info("Comfort", mine
                    ? "TURN GATE: board targeting is MINE — the thumbstick's sideways axis goes to AoE "
                      + "rotation and stick turning stands down, as it always has."
                    : "TURN GATE: board targeting belongs to ANOTHER seat (Choreographer."
                      + "ThisPlayerHasTurnControl = false — a fellow player is confirming, or the enemies "
                      + "are acting). Turning and strafing stay MINE: locomotion is local comfort and is "
                      + "never gated by whose decision the board is waiting for (user 2026-08-13, #14).");
            }
            return mine;
        }
    }
}
