using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Drives <see cref="PlayTray.TickPinnedZoomCarry"/> — and it exists ONLY to pin the PHASE. The rule
/// it carries is small; getting it into the right instant of the frame is the part that has already
/// failed once on hardware.
///
/// <para><b>WHY LateUpdate AND NOT Update.</b> ModBuild 159 re-asserted the pin holder from the live
/// rig inside <c>CardsDriver.Update</c>. Unity's Update order among mod drivers is unspecified, and
/// <c>WorldGrab.Update</c> — which writes the new rig scale — ran AFTER it. The board was therefore
/// carried against LAST frame's rig scale every single frame of every pinch, and the hardware log
/// printed the identity outright: <c>parent chain ×64.79 ÷ rig ×68.50</c>, where 64.79 is the previous
/// frame's rig scale, in 82 of 158 moving-zoom samples. That is a ±10 % breathing through every zoom,
/// and no arithmetic in <see cref="BoardZoomCarry"/> can express it — a right answer computed one frame
/// early is still wrong. LateUpdate is a whole phase after Update, so every Update-phase rig writer
/// (WorldGrab, SnapTurn, Comfort, Recenter) has already committed.</para>
///
/// <para><b>WHY EXECUTION ORDER 20000.</b> LateUpdate alone is not enough: <c>VRRigDriver.LateUpdate</c>
/// is itself a rig writer — it re-asserts the world tilt so that any writer which flattened the rig to
/// yaw-only this frame is healed before the player sees an untilted frame — and it sits at the default
/// order 0. Reading the rig at order 0 would be a coin flip against that heal. 20000 sits above every
/// default-order driver and below <c>Core/PerfFrameSplit</c>'s 30000 (the last main-thread instant of
/// the logic phase), so the rig this reads is the rig that will be rendered. ModBuild 160 chose the
/// same number for the same reason and that part of it was correct; it is the only part of 160 kept.</para>
///
/// <para><b>WHY A SEPARATE COMPONENT.</b> <c>CardsDriver</c> deliberately carries no
/// <c>[DefaultExecutionOrder]</c> — pinning it would freeze an order the rest of the card code does not
/// rely on, which this codebase treats as a hazard (see Board/BoardDriver.cs and
/// Board/FigureGrab/FigureGrabDriver.cs, where the same attribute is rejected in as many words). Only
/// this one read needs the guarantee, so only this one read gets the attribute. It hangs off the same
/// GameObject, so it lives and dies with the card driver.</para>
/// </summary>
[DefaultExecutionOrder(20000)]
internal sealed class BoardZoomCarryDriver : MonoBehaviour
{
    /// <summary>The name the frame-order lock and the hardware logs know this step by.</summary>
    internal const string StepName = "Cards.BoardZoomCarry";

    private void LateUpdate() =>
        // Guarded like every other mod tick: an unhandled MonoBehaviour.LateUpdate exception is logged
        // by Unity as an anonymous line and would silently stop this carry for the rest of the session,
        // which presents as report 2 coming back with no explanation anywhere in the log.
        TickGuard.Run(StepName, static () => PlayTray.Current?.TickPinnedZoomCarry());
}
