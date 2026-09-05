using GloomhavenVR.Core;
using GloomhavenVR.Hands;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A grabbable that can have its pre-grab glow taken off and put back WITHOUT a re-hover — the one
/// capability the walk-in edge needs of it.
///
/// <para>Implemented by <see cref="FigureGrabbable"/> and <see cref="GrabbableProp"/>. It is
/// deliberately about the GLOW and nothing else: the two types elect, hold, sync and describe
/// themselves along completely different paths, and the audit's section 4 item 13 records why their
/// ELECTIONS must stay separate ("a figure carries a mod-owned reach extension, a busy predicate, a
/// stretch gesture and a refusal probe that all have to agree within one frame. A prop has none of
/// that"). Nothing here touches any of that.</para>
/// </summary>
internal interface IWalkInHighlightTarget
{
    /// <summary>Is the glow standing right now?</summary>
    bool HighlightActive { get; }

    /// <summary>Take the glow off and leave the hover record alone. The hand has NOT stopped
    /// hovering — it is the mode that changed — so a full un-hover would drop the record this
    /// class needs to put the glow back.</summary>
    void ClearHighlightVisualOnly();

    /// <summary>Put the glow back, reporting what was overlaid. False means it must not or could
    /// not be re-lit (the visual is gone, a remote player holds it, the overlay found nothing) —
    /// in which case nothing is logged, because there is nothing to report.</summary>
    bool TryRelightHighlight(out string overlay);

    /// <summary>How the log names this target.</summary>
    string HighlightLabel { get; }
}

/// <summary>
/// THE WALK-IN GLOW EDGES, FOR FIGURES AND PROPS TOGETHER.
///
/// <para>The grab system is EDGE-DRIVEN: <c>ProximityGrabber.SetHighlighted</c> raises
/// <c>OnGrabHighlight</c> when the hover changes and never polls. So nothing in the pipeline
/// notices the player stepping INTO or OUT OF the board while something is already under his hand,
/// and both directions matter: entering walk-in must CLEAR a glow that is already standing, and
/// leaving it must bring the glow back WITHOUT requiring a re-hover. That is why the hover is
/// recorded separately from the glow — knowing WHO is hovered even while suppressed is the whole
/// mechanism.</para>
///
/// <para><b>WHY IT IS SHARED (redundancy survey R10, 2026-09-05).</b> Figures had all of this and
/// props had none of it: <c>GrabbableProp.OnGrabHighlight</c> was a one-way gate that read
/// <c>FigureGrabConfig.HighlightAllowedHere</c> and returned, with no hover record and no mode
/// tick. Since that flag flips at RUNTIME off <c>WallSegmentFade.WalkInsideEngaged</c>, the two
/// diverged in OPPOSITE directions on the same two edges: hold a hand over a chest and step INTO
/// the board and the chest kept glowing while a miniature under the other hand went dark; step back
/// OUT under the same standing hover and the miniature re-lit by itself while the chest stayed dark
/// until the hand moved away and back. ModBuilds 435-436 put figures and props on ONE held-pose
/// path at the user's request ("versuche Redundanz im Code zu vermeiden"); this is that same
/// request one layer up, in the corner it left behind.</para>
///
/// <para>Edge-gated, so the steady state is one bool compare — <see cref="Tick"/> runs every frame.
/// It reads the same narrow latch the wall fade uses, which each client evaluates from its own
/// head, so there is nothing here to synchronise: the pre-grab glow has never been a wire field and
/// no peer has ever seen it. A remote hold is a local VETO on it (via <c>NetHeldFigures.Owns</c>,
/// inside the figure's own <see cref="IWalkInHighlightTarget.TryRelightHighlight"/>) and that is
/// the only net term involved.</para>
/// </summary>
internal static class WalkInHighlightEdges
{
    /// <summary>What each hand is currently hovering, indexed by <see cref="HandSide"/> — recorded
    /// whether or not the glow was actually applied, which is the point.</summary>
    private static readonly IWalkInHighlightTarget?[] Hovered = new IWalkInHighlightTarget?[2];

    /// <summary>The walk-in verdict this pass last acted on, so only the EDGES do work.</summary>
    private static bool _allowedLast = true;

    /// <summary>Record (or clear) what <paramref name="side"/> is hovering. Called from every
    /// <c>OnGrabHighlight</c> BEFORE any suppression check, for the reason in the class doc.</summary>
    internal static void NoteHover(HandSide side, IWalkInHighlightTarget? target) =>
        Hovered[(int)side] = target;

    /// <summary>
    /// Drop <paramref name="target"/> from the hover record whichever hand held it.
    ///
    /// <para>Called from every path that ends a hover, INCLUDING the ones that are not un-hovers:
    /// a grab, a release glide, a restore. A stale entry there would have <see cref="Tick"/> re-light
    /// something that is no longer under anybody's hand.</para>
    /// </summary>
    internal static void Forget(IWalkInHighlightTarget target)
    {
        for (int i = 0; i < Hovered.Length; i++)
        {
            if (ReferenceEquals(Hovered[i], target))
                Hovered[i] = null;
        }
    }

    /// <summary>Drop every record. A scenario teardown, where the objects behind these references
    /// are about to be destroyed.</summary>
    internal static void Reset()
    {
        for (int i = 0; i < Hovered.Length; i++)
            Hovered[i] = null;
        _allowedLast = true;
    }

    /// <summary>Drive the glow across a walk-in edge, in both directions, for whatever each hand is
    /// hovering. One bool compare in the steady state.</summary>
    internal static void Tick()
    {
        bool allowed = FigureGrabConfig.HighlightAllowedHere;
        if (allowed == _allowedLast)
            return;
        _allowedLast = allowed;

        for (int i = 0; i < Hovered.Length; i++)
        {
            IWalkInHighlightTarget? t = Hovered[i];
            if (t == null)
                continue;

            if (!allowed)
            {
                // A full un-hover would drop the hover record with it, and the hand has NOT stopped
                // hovering — it is the mode that changed. Clear the visual only.
                if (t.HighlightActive)
                {
                    t.ClearHighlightVisualOnly();
                    VRLog.Info("FigureGrab",
                        $"pre-grab highlight CLEARED ({t.HighlightLabel}) — walk-in mode engaged under a "
                        + "standing hover; the hover itself is untouched and grabbing still works.");
                }
                continue;
            }

            if (t.HighlightActive)
                continue;
            if (t.TryRelightHighlight(out string overlay))
                // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
                // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
                VRLog.Note("FigureGrab",
                    $"pre-grab highlight ENGAGED ({t.HighlightLabel}) — walk-in mode released under a "
                    + $"standing hover, so the glow returns without needing a re-hover: {overlay}.");
        }
    }
}
