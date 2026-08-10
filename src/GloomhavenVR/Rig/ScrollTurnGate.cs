using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// MAY THE TURN AXIS BE READ WHILE MENU SCROLLING OWNS THIS STICK? A three-state latch, and the
/// whole quality of the feature is in its two exits.
///
/// <para>User report 2026-08-11 (hardware, ModBuild 107): "Während dessen man in einem menu
/// scrollt soll auch die Drehung blockiert sein, das passiert mir immer wieder versehentlich
/// ungewollt."</para>
///
/// <para>ROOT CAUSE. Scrolling and turning are on the SAME physical stick for this player and
/// read its two axes: <c>RayUguiDriver.TickStickScroll</c> takes y, <see cref="SnapTurn"/> takes
/// x. A thumb pushing a list is not axis-pure — a straight-up push carries an incidental x of a
/// few tenths — and the shipped turn mode is Smooth, whose deadzone is 0.2. So every scroll came
/// with a slow, unwanted yaw. (Evidence that the two really are one stick on his rig: the
/// ModBuild-107 log's "[Comfort] stick flight: sideways strafe OFF — the sideways axis is
/// claimed … turning on this same stick", plus every "SUSPENDED on the Right hand" scroll line.)</para>
///
/// <para>WHY A LATCH AND NOT A PLAIN "is scrolling" TEST. Because the plain test moves the bug
/// instead of fixing it. A scroll ends the moment the beam leaves the list or the grace expires,
/// and at that moment the stick is still deflected — the same deflection that was being ignored
/// fires the turn a fraction of a second later. So the block does not lift on its own: once
/// armed, the TURN AXIS must come back to rest before turning is allowed again
/// (<see cref="State.Blocked"/> → <see cref="State.Open"/>). Only the turn axis, deliberately —
/// requiring the whole stick to re-centre would mean a player sweeping from "push up to scroll"
/// to "push right to turn" (a path that never passes near the centre) finds turning dead.</para>
///
/// <para>THE SECOND EXIT EXISTS SO THE PLAYER CAN NEVER BE STRANDED. A block whose only exit is
/// "relax your thumb" would leave a player who parks the beam on a long list — the options menu
/// he is reading right now — unable to turn at all, and turning is load-bearing in a way flight
/// is not ("a player who cannot turn is stuck facing one way", <c>Flight.StrafeAllowed</c>). So
/// an UNAMBIGUOUS sideways flick — <see cref="DeliberateDeflection"/> of travel AND more x than
/// y — is honoured even while the block is armed (<see cref="State.Override"/>). That test is
/// exactly the shape the accidental input is not: the incidental x of a scroll push is small and
/// always dominated by the y the player is actually pushing, while a turn flick is large and
/// x-dominant. The override is HELD until the axis re-centres rather than re-evaluated per frame,
/// or easing off mid-rotation would drop back into the block and the yaw would stutter.</para>
///
/// <para>REJECTED: a watchdog that force-opens the block after N seconds of not scrolling. It
/// would be a third exit whose only observable effect is to fire the very turn the user
/// complained about, just later. It is also unnecessary: both exits above depend on the STICK
/// ALONE, never on the menu, the panel, the hand or <c>UiScrollFocus</c>, so no external
/// disappearance (panel destroyed, menu closed, list emptied, hand untracked) can strand the
/// latch — a released thumb clears it on the next frame it is read. <see cref="Reset"/> covers
/// the remaining structural cases (turn hand switched, turning disabled, mode change).</para>
///
/// <para>REJECTED: blocking BOTH hands. Scroll ownership is stamped per hand by
/// <c>UiScrollFocus</c>, and a player who scrolls with one hand and turns with the other has two
/// physically separate sticks with no cross-talk to fix; blocking the idle hand's stick would
/// take turning away for no reason at all.</para>
///
/// <para>Free of Unity beyond <c>Mathf</c> and of every project type on purpose: this is a
/// FEEL decision that cannot be observed from outside a headset, so it is driven case by case in
/// <c>tests/GloomhavenVR.WireTests/ScrollTurnGateVectors.cs</c> — the same reasoning that put
/// <c>ConfigSteps</c> and <c>RelaunchCommand</c> on that harness.</para>
/// </summary>
internal struct ScrollTurnGate
{
    /// <summary>
    /// Sideways travel that counts as a deliberate turn flick even while scrolling owns the
    /// stick. Same number as <c>SnapTurn.SnapEngageThreshold</c>, and for the same reason: it is
    /// the deflection the project already treats as "the player meant this", far above anything
    /// a thumb produces sideways while pushing the stick up a list.
    /// </summary>
    internal const float DeliberateDeflection = 0.7f;

    internal enum State
    {
        /// <summary>Nothing in the way — turning reads the stick normally.</summary>
        Open,

        /// <summary>Scrolling owned the stick; turning waits for the axis to come back to rest.</summary>
        Blocked,

        /// <summary>A deliberate sideways flick overrode an armed block and is still held.</summary>
        Override,
    }

    private State _state;

    internal State Current => _state;

    /// <summary>True while the gate is actively refusing turn input (diagnostics / gizmos).</summary>
    internal bool IsBlocking => _state == State.Blocked;

    /// <summary>
    /// Fail open. Called on every structural change that makes the latch meaningless — the turn
    /// hand switched, turning was disabled, a mode owns the stick outright — so no stale state
    /// can survive into a situation it was never about.
    /// </summary>
    internal void Reset() => _state = State.Open;

    /// <summary>
    /// One frame's verdict. <paramref name="scrollOwnsStick"/> is
    /// <c>UiScrollFocus.IsScrolling</c> for the TURN hand; <paramref name="x"/>/<paramref name="y"/>
    /// are that hand's raw thumbstick axes; <paramref name="rearmThreshold"/> is the deflection
    /// the active turn mode itself treats as no input (snap's re-arm, smooth's deadzone), so
    /// "back at rest" means exactly what that mode already means by it.
    ///
    /// <para>The three transitions are applied in sequence, NOT as an if/else chain: a block that
    /// arms on a stick which is already at rest must be able to clear again in the same frame, or
    /// merely pointing at a list with an untouched stick would cost one frame of turning per
    /// frame — i.e. all of it.</para>
    /// </summary>
    /// <returns>True if turning may read the stick this frame.</returns>
    internal bool Evaluate(bool scrollOwnsStick, float x, float y, float rearmThreshold)
    {
        float ax = Mathf.Abs(x);
        bool atRest = ax <= rearmThreshold;

        if (_state == State.Open && scrollOwnsStick)
            _state = State.Blocked;

        if (_state == State.Blocked)
        {
            if (atRest)
                _state = State.Open;
            else if (ax >= DeliberateDeflection && ax > Mathf.Abs(y))
                _state = State.Override;
        }
        else if (_state == State.Override && atRest)
        {
            // Back at rest: hand the axis over to whoever owns it now. Still scrolling ⇒ straight
            // back into the block (nothing to turn at this deflection anyway, so the one frame
            // costs nothing); otherwise fully open.
            _state = scrollOwnsStick ? State.Blocked : State.Open;
        }

        return _state != State.Blocked;
    }
}
