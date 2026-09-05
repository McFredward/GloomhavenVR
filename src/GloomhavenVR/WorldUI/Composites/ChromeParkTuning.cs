namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE NUMBERS EVERY "PARK A CONTROL BESIDE A WINDOW" IMPLEMENTATION IN THIS MOD SHARES.</b>
/// ENTER DUNGEON on the loadout screen, REISEN and the ready-up toggle in the map room, the intro
/// hint on its owner window, the quest picture beside the story dialog, the enchantress's controls
/// — five private implementations of one idea, each with its own painted-bounds sweep and its own
/// park/unpark state machine.
///
/// <para><b>WHAT THIS FIXES AND WHAT IT DELIBERATELY DOES NOT (ModBuild 439, survey row R35).</b>
/// The survey's recommendation was explicit: the shared core is <i>not</i> the bounds sweep — the
/// five sweeps legitimately differ, only <c>LoadoutConfirmPark</c> clips to uGUI masks and its own
/// comment refuses to extend that to the others' unions — it is the park/unpark machinery, "which
/// is where the hand-copied constants live". This file is that half: the constants, in one place,
/// with the reason each was copied written once instead of four times. The state machines are left
/// standing and are a scheduled item, not a squeezed-in one; the sweeps are left alone until the
/// question row R7 opened (what a bounds sweep should be) is settled.</para>
///
/// <para><b>WHY IT IS A SHARED CONSTANT AND NOT A LINT.</b> <c>check-mirrors.sh</c>'s own header
/// makes the case for linting a pair rather than merging it — "each site keeps its own doc comment
/// explaining what the number means THERE, which a shared constant would have flattened". That
/// argument holds for numbers that mean different things in different places. It does not hold
/// here: every copy below is accompanied, in its own file, by a comment naming the file it was
/// copied FROM and saying "for its reason". A constant whose documentation is a pointer to another
/// constant is not a second meaning; it is one meaning written twice.</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT IN HERE.</b> Two nearby numbers look like members and are not.
/// <c>DecisionDockSurface.ClaimGraceSeconds</c> is also 1.5 s and answers a different question —
/// how long a PROMPT may fail to isolate before the generic float takes over — so it is neither a
/// copy nor bound to this one. <c>HintOnOwnerComposite.HintGapPx</c> is also 24 px and carries its
/// own argument ("small on purpose: the hint sits ON the window like a tooltip, it does not hang
/// off it") rather than a pointer at the others; the same number for an independently argued reason
/// is exactly what must not be collapsed.</para>
///
/// <para><b>MULTIPLAYER:</b> nothing here goes on the wire. Parking chrome beside a window is local
/// presentation, decided per client from that client's own measurement of its own layout.</para>
/// </summary>
internal static class ChromeParkTuning
{
    /// <summary>
    /// Below this the re-place is skipped, in the host window's own authored uGUI px.
    ///
    /// <para>A settled uGUI layout is not bit-identical frame to frame. Writing the solved offset
    /// back every frame would dirty the host rect and keep the panel's content fit re-measuring
    /// forever — and a rect rewritten every frame is indistinguishable from a write war in a log.
    /// Compared squared at some call sites and absolutely at others; the value is the same either
    /// way.</para>
    /// </summary>
    internal const float OffsetEpsilonPx = 0.5f;

    /// <summary>
    /// How often the measured ZERO — the painted edge the parked control is solved from — is
    /// re-derived, in unscaled seconds.
    ///
    /// <para>Not per frame: the sweep walks every Graphic under a window that can carry thousands of
    /// transforms, and what it measures only changes when the content does (a roster edit, a tab
    /// switch, a longer quest). 5 Hz is fast enough that the control follows a change visibly at
    /// once and costs nothing in between. The first tick after a park always measures, whatever this
    /// says.</para>
    /// </summary>
    internal const float AnchorRefreshIntervalSeconds = 0.2f;

    /// <summary>
    /// How long a park CLAIM may stand while the control is on screen but not yet parked, in
    /// unscaled seconds.
    ///
    /// <para>Long enough to cover the slowest legitimate path to a floated panel (the catch-all's
    /// enrolment grace plus one conversion), short enough that a player who closes the window gets
    /// his button back almost at once. The direction of the bound is the ruling behind it: a confirm
    /// nobody can reach is worse than an ugly one, so the claim is RELEASED on expiry rather than
    /// held.</para>
    ///
    /// <para>It is not <c>ReadyToggleParkClaim.ClaimLifetimeSeconds</c>: that one bounds how long a
    /// claim survives WITHOUT BEING RE-ASSERTED (how quickly a dead parker releases its refusal),
    /// while this one bounds how long a LIVE parker may keep asserting a claim it cannot make good
    /// on.</para>
    /// </summary>
    internal const float ClaimGraceSeconds = 1.5f;

    /// <summary>
    /// The gap between a window's painted edge and the painted edge of the chrome parked against it,
    /// in the host window's own authored uGUI px.
    ///
    /// <para>Both edges are measured as INK, never as authored rects — the ModBuild 239 failure was
    /// exactly that: a 24 px gap applied to the authored rect of a mostly-transparent host drew at
    /// 888 px and then 786 px. On a committed panel 24 px is roughly 17-25 mm: visibly one gap, and
    /// never a separation. It is deliberately a constant and not a config dial — the user asked for
    /// the control to be beside the content, not for a spacing control, and every new dial is a
    /// surface somebody has to tune.</para>
    /// </summary>
    internal const float ChromeGapPx = 24f;
}
