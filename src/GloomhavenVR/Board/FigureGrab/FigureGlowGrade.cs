namespace GloomhavenVR.Board.FigureGrab;

// =================================================================================================
//  THE MINIATURE GRADE — the whole "body or furniture" decision, and NOT ONE UNITY TYPE.
//
//  WHY THIS FILE EXISTS AND WHY IT IS EMPTY OF UnityEngine. It is compiled into
//  tests/GloomhavenVR.WireTests verbatim (see that project's .csproj), so
//  FigureHighlightGradeVectors.cs drives THESE two methods rather than a copy of them. That is the
//  whole reason the file references nothing at all: which renderers a figure's hover glow covers is
//  observed ONLY by eye, from inside a headset, one hardware round per report — "der Boss-Drache
//  hat immer noch KEIN Highlighting" took from ModBuild 293 to ModBuild 342 to answer, and every
//  round in between compiled, logged, engaged and returned true. A grade that is one comparison
//  wrong does not throw and does not look wrong; it glows a dart. A mirrored re-implementation in
//  the test would have agreed with every one of those builds.
//
//  It is a PART of FigureHighlight rather than a class of its own so that MiniatureGrade keeps the
//  name every hardware log, verdict headline and call site since ModBuild 340 has used for it —
//  the same reasoning, and the same shape, as HauntFigures.Math.cs.
//
//  MOVED HERE UNCHANGED in the ModBuild 342 hardening pass — character for character, doc comments
//  included, out of the `BEGIN PURE DECISION` / `END PURE DECISION` bracket in FigureHighlight.cs.
//  Nothing about the decision changed with the move, and the refactor guard's empty diff is what
//  says so.
// =================================================================================================

internal sealed partial class FigureHighlight
{
    /// <summary>
    /// WHAT THE ModBuild 340 RULE ASSUMES ABOUT ONE FIGURE, AND WHETHER THAT FIGURE HONOURS IT.
    /// See the MINIATURE VERDICT note on <see cref="FigureHighlight"/> for why each grade means
    /// what it means.
    /// </summary>
    internal enum MiniatureGrade
    {
        /// <summary>No clonable renderer at all. Nothing to glow, and nothing to reason about.</summary>
        Nothing,

        /// <summary>The restriction would drop NOTHING — every candidate already lies under
        /// <c>m_AnimatedGameObject</c>, or the actor has none and the whole root is used. The glow
        /// is then the same set of renderers the pre-340 whole-root glow would have cloned, so no
        /// assumption about that field is load-bearing on this figure.</summary>
        Proven,

        /// <summary>The actor HAS an <c>m_AnimatedGameObject</c> and it holds not one clonable
        /// renderer, so the whole actor root is used. Over-glow, not under-glow — but it means the
        /// game's own <c>m_Renderers</c> array is empty for this actor, which is worth a look.</summary>
        RootFallback,

        /// <summary>The restriction drops renderers, and what it KEEPS carries strictly more
        /// geometry than what it drops. This is the boss shape: a body plus actor furniture.</summary>
        Furniture,

        /// <summary>The restriction would drop AT LEAST AS MUCH geometry as it keeps, so
        /// <c>m_AnimatedGameObject</c> is not plausibly the miniature. The restriction is REFUSED
        /// and the whole actor root is glowed instead — over-glow, which is visible, rather than
        /// under-glow, which is not.</summary>
        Refused,
    }

    /// <summary>
    /// THE WHOLE RULE, AS ARITHMETIC. Five numbers in, one grade out, no Unity type involved — so
    /// the decision that governs whether a figure's body or its furniture gets the glow can be
    /// driven case by case without a headset, an engine or a scenario.
    ///
    /// <list type="bullet">
    ///   <item><description><paramref name="candidates"/> — clonable renderers under the actor root.</description></item>
    ///   <item><description><paramref name="under"/> — how many of those lie under <c>m_AnimatedGameObject</c>.</description></item>
    ///   <item><description><paramref name="keptVerts"/> / <paramref name="droppedVerts"/> — the
    ///   vertex totals of the two halves that split makes.</description></item>
    ///   <item><description><paramref name="hasAnimatedObject"/> — false for every board PROP,
    ///   which has no <c>ActorBehaviour</c> and therefore no such field.</description></item>
    /// </list>
    /// </summary>
    internal static MiniatureGrade GradeOf(int candidates, int under, int keptVerts,
                                           int droppedVerts, bool hasAnimatedObject)
    {
        if (candidates <= 0)
            return MiniatureGrade.Nothing;

        // No animated object at all: the whole root is used and NOTHING is dropped, so there is no
        // judgement to get wrong. Every prop takes this branch.
        if (!hasAnimatedObject)
            return MiniatureGrade.Proven;

        // An animated object that holds no clonable renderer: fall back to the whole root. This is
        // the safety ModBuild 294 moved to the root FOR, and it over-glows rather than under-glows.
        if (under <= 0)
            return MiniatureGrade.RootFallback;

        // The restriction removes nothing — the restricted and unrestricted sets are the SAME set.
        if (under >= candidates)
            return MiniatureGrade.Proven;

        // THE GUARD. `under < candidates` is established, so something really would be dropped and
        // two meshless sets cannot produce a false refusal out of 0 >= 0.
        if (droppedVerts >= keptVerts)
            return MiniatureGrade.Refused;

        return MiniatureGrade.Furniture;
    }

    /// <summary>
    /// Whether a grade means "clone only <c>m_AnimatedGameObject</c>".
    ///
    /// <para><paramref name="hasAnimatedObject"/> is not decoration: <see cref="MiniatureGrade.Proven"/>
    /// is reached BOTH by "every candidate is already under the animated object" and by "there is no
    /// animated object", and a true here in the second case would send the clone loop into an
    /// ancestor test against a null transform. Restricting under Proven changes nothing either way
    /// — the two sets are equal by definition of the grade — so this returns true there only to
    /// keep the log saying "Cloned from m_AnimatedGameObject 'HE_Brute'", the vocabulary every
    /// hardware round since ModBuild 294 has been read in.</para>
    /// </summary>
    internal static bool RestrictFor(MiniatureGrade grade, bool hasAnimatedObject) =>
        hasAnimatedObject
        && (grade == MiniatureGrade.Furniture || grade == MiniatureGrade.Proven);
}
