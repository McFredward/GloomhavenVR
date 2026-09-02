using System;
using GloomhavenVR.Board.FigureGrab;

namespace GloomhavenVR.WireTests;

/// <summary>
/// WHOSE GEOMETRY GETS THE HOVER GLOW — the body, or the furniture hanging off it.
///
/// <para><b>WHY THIS IS ON THIS HARNESS.</b> Every other gate in this repository agreed with the
/// broken builds. "Der Boss-Drache hat immer noch KEIN Highlighting … alle anderen Figuren schon"
/// was first filed against ModBuild 293 and was not answered until ModBuild 342, and across that
/// whole run the mod compiled with zero warnings, the guard diff was clean, the highlight ENGAGED
/// six times against six CLEARs, <c>Apply</c> returned true, a real material was built and a real
/// pulse animated it. The glow was drawn. It was drawn on a dart. Nothing in the repository could
/// have said so, because the defect is not an exception, a null or a byte — it is a JUDGEMENT
/// about which subtree is the miniature, and its output is a picture inside a headset.</para>
///
/// <para><b>WHY VECTORS AND NOT A HARDWARE ROUND.</b> The judgement is arithmetic: five integers
/// in, one grade out. So the one part of this feature that can be settled without a headset is
/// settled here, once, and it is settled against <see cref="FigureHighlight.GradeOf"/> ITSELF —
/// <c>src/GloomhavenVR/Board/FigureGrab/FigureGlowGrade.cs</c> is LINKED into this assembly
/// (see the .csproj), not copied into it. A mirrored re-implementation would have passed happily
/// through every one of the builds above, which is the whole reason the file was written free of
/// UnityEngine.</para>
///
/// <para><b>WHERE THE NUMBERS COME FROM.</b> Section A is not invented: every row is a figure the
/// ModBuild 340 hardware log actually measured, with its real candidate counts and its real vertex
/// totals, named in the comment beside it. Four figures and one prop. They exist so a future
/// change to the rule has to state, in this file, what it is doing to the five figures that have
/// ever been observed.</para>
///
/// <para><b>WHAT THE PROPERTY SECTIONS PROTECT.</b> The vectors above them are a finite list and
/// a finite list can never be complete — the roster of Gloomhaven figures is in asset bundles this
/// project cannot open. So the claims the feature is ARGUED from are asserted as properties over a
/// generated range instead:</para>
/// <list type="bullet">
///   <item><description>the refusal guard's <c>&gt;=</c> fires at EXACTLY equal vertex counts, and
///   is silent one vertex below — the boundary, from both sides;</description></item>
///   <item><description><c>Proven</c> ⟺ the restriction drops nothing, which is the sentence the
///   hover line prints to the user, asserted as arithmetic;</description></item>
///   <item><description>nothing dropped ⇒ never <c>Refused</c>, i.e. the guard added in ModBuild
///   341 cannot change the behaviour of any figure that was already fine;</description></item>
///   <item><description>and the null-safety one, exhaustively over the enum:
///   <c>RestrictFor(g, hasAnimatedObject: false)</c> is false for EVERY grade. That is the whole
///   argument that the clone loop never runs its ancestor test against a null transform, so it is
///   asserted over <c>Enum.GetValues</c> rather than for the one grade that happens to reach it
///   today — a grade added later is covered the moment it is declared.</description></item>
/// </list>
///
/// <para><b>THIS SUITE IS KNOWN TO BE ABLE TO FAIL.</b> It was driven against four deliberate
/// mutations of the shipped source before it was committed — the guard's <c>&gt;=</c> weakened to
/// <c>&gt;</c>, the guard deleted outright (which is ModBuild 340, the unhardened rule),
/// <see cref="FigureHighlight.RestrictFor"/> stripped of its <c>hasAnimatedObject</c> term, and
/// the "restriction removes nothing" case graded <c>Furniture</c> instead of <c>Proven</c>. Each
/// turned this file red, and the counts are in the ModBuild 342 hardening report. A suite that
/// cannot fail reports green forever and nobody notices.</para>
/// </summary>
internal static class FigureHighlightGradeVectors
{
    private const bool Animated = true;
    private const bool NoAnimator = false;

    /// <summary>
    /// One figure, driven through BOTH pure methods at once: the grade it earns and the decision
    /// the clone loop then obeys. They are asserted together because they are read together — a
    /// grade is only ever consumed through <see cref="FigureHighlight.RestrictFor"/>.
    /// </summary>
    private static void Figure(Harness t, int candidates, int under, int keptVerts, int droppedVerts,
                               bool hasAnimatedObject, FigureHighlight.MiniatureGrade wantGrade,
                               bool wantRestrict, string what)
    {
        FigureHighlight.MiniatureGrade got =
            FigureHighlight.GradeOf(candidates, under, keptVerts, droppedVerts, hasAnimatedObject);
        bool restrict = FigureHighlight.RestrictFor(got, hasAnimatedObject);

        t.True(got == wantGrade, $"{what}: grade is {got}, expected {wantGrade}");
        t.True(restrict == wantRestrict,
               $"{what}: restrict is {restrict}, expected {wantRestrict} (grade {got})");
    }

    internal static void Run(Harness t)
    {
        RunMeasuredFigures(t);
        RunGuardBoundary(t);
        RunForeignAnimator(t);
        RunRemainingGrades(t);
        RunNullSafety(t);
        RunProperties(t);
    }

    // ---- A. THE FIVE THINGS THE HARDWARE LOG ACTUALLY MEASURED --------------------------------
    //
    // Four figures and one prop, from the ModBuild 340 LogOutput.log. These are the only figures
    // anyone has ever measured this rule against, so they are the only ones a change to it can be
    // checked against without a headset. If one of these rows has to be edited, the edit is a
    // statement that a figure the user has already looked at now glows differently.
    private static void RunMeasuredFigures(Harness t)
    {
        t.Case("figureglow/measured-figures");

        // THE BOSS. 'MO_ElderDrake_MESH' at 14 846 verts is kept; two bands — 'geo_bendyband' and
        // 'geo_bendyband (1)' at 60 verts each — are dropped. This is the shape the whole rule
        // exists for: a body plus actor furniture, and the body wins by a factor of 124.
        Figure(t, 3, 1, 14846, 120, Animated,
               FigureHighlight.MiniatureGrade.Furniture, true,
               "MO_Elder_Drake: 1 of 3 candidates under the animator, 14846 kept vs 120 dropped");

        // THE FOUR THAT WERE NEVER IN DOUBT. Every candidate already lies under
        // m_AnimatedGameObject, so the restriction is the identity and no judgement is
        // load-bearing on them. Their kept-vertex totals are the sums of the renderers the log
        // names one by one (Brute: 834+545+298+131+341+9447).
        Figure(t, 9, 9, 11596, 0, Animated,
               FigureHighlight.MiniatureGrade.Proven, true,
               "HE_Brute: 9 of 9 candidates, nothing dropped");
        Figure(t, 12, 12, 15464, 0, Animated,
               FigureHighlight.MiniatureGrade.Proven, true,
               "HE_Mindthief: 12 of 12 candidates, nothing dropped");
        // 3 of 3: MO_Spitting_Drake_Mesh 11462 + LOD1 7687 + LOD2 5079.
        Figure(t, 3, 3, 24228, 0, Animated,
               FigureHighlight.MiniatureGrade.Proven, true,
               "MO_SpittingDrake: 3 of 3 candidates, nothing dropped");

        // THE PROP, and the reason RestrictFor takes a second argument at all. 'coinpile' is a
        // MoneyToken: it has no ActorBehaviour, therefore no m_AnimatedGameObject, therefore no
        // transform for the clone loop's ancestor test to dereference. It grades Proven for the
        // same reason the four above do — nothing is dropped — but it must NOT restrict.
        Figure(t, 1, 1, 1766, 0, NoAnimator,
               FigureHighlight.MiniatureGrade.Proven, false,
               "coinpile prop: no m_AnimatedGameObject at all, so nothing to restrict TO");
    }

    // ---- B. THE GUARD'S BOUNDARY, FROM BOTH SIDES ---------------------------------------------
    //
    // The refusal guard is `droppedVerts >= keptVerts`. A single character of that comparison is
    // the difference between "the boss keeps its glow" and "the boss falls back to the whole
    // actor root", so it is asserted at exactly equal, one below, and one above.
    private static void RunGuardBoundary(Harness t)
    {
        t.Case("figureglow/guard-boundary");

        // The negative direction first: the guard must be a proven NO-OP on every figure ever
        // measured. It is not enough that the boss is not refused — the margin is the claim.
        t.True(FigureHighlight.GradeOf(3, 1, 14846, 120, Animated)
               != FigureHighlight.MiniatureGrade.Refused,
               "the boss is not refused at its real numbers");

        // The bands would have to grow by a factor of 124 before the guard could touch it. At one
        // vertex below the kept total it is still FURNITURE and still restricts.
        Figure(t, 3, 1, 14846, 14845, Animated,
               FigureHighlight.MiniatureGrade.Furniture, true,
               "the boss with its bands inflated to 14845 verts is STILL furniture");

        // ...and at exactly equal it refuses. This is the `>=` rather than `>`, and it is the one
        // assertion that separates the shipped guard from the obvious weaker spelling of it.
        Figure(t, 3, 1, 14846, 14846, Animated,
               FigureHighlight.MiniatureGrade.Refused, false,
               "at EXACTLY equal vertex counts the restriction is refused (the >= boundary)");

        // One vertex above, for completeness: still refused, so the boundary is a step and not a
        // spike.
        Figure(t, 3, 1, 14846, 14847, Animated,
               FigureHighlight.MiniatureGrade.Refused, false,
               "one vertex past equal is refused too");

        // The same boundary at the smallest numbers that can express it, so the assertion is not
        // an artefact of the boss's scale.
        Figure(t, 2, 1, 10, 9, Animated,
               FigureHighlight.MiniatureGrade.Furniture, true, "9 dropped against 10 kept: furniture");
        Figure(t, 2, 1, 10, 10, Animated,
               FigureHighlight.MiniatureGrade.Refused, false, "10 against 10: refused");
        Figure(t, 2, 1, 10, 11, Animated,
               FigureHighlight.MiniatureGrade.Refused, false, "11 against 10: refused");
    }

    // ---- C. CASE (ii): A FOREIGN ANIMATED OBJECT ----------------------------------------------
    //
    // MF.GetGameObjectAnimator returns the FIRST Animator with a controller in depth-first order
    // (decompiled MF.cs:395-406), which is not defined to be the character's own. The boss's
    // FIGURE REACH line names a WP_Scoundrel_Dart and two WP_Dummy objects hanging off it. If the
    // walk reaches one of those first, m_AnimatedGameObject IS a dart — and the pre-341 rule would
    // have restricted the glow to it, producing exactly the symptom the user reported: a highlight
    // that engages, logs, returns true and cannot be seen.
    private static void RunForeignAnimator(Harness t)
    {
        t.Case("figureglow/foreign-animator");

        Figure(t, 3, 1, 298, 14906, Animated,
               FigureHighlight.MiniatureGrade.Refused, false,
               "m_AnimatedGameObject is a dart: 298 kept against 14906 dropped");

        // The same shape at the smallest expressible size — a one-vertex animated subtree against
        // a two-vertex body. The rule is about the RATIO, not about the scale.
        Figure(t, 2, 1, 1, 2, Animated,
               FigureHighlight.MiniatureGrade.Refused, false,
               "a one-vertex animator subtree against a 2-vertex body");

        // And the direction the refusal errs in, which is the design decision behind it: refusing
        // falls back to the WHOLE ACTOR ROOT, i.e. it can only ever glow MORE. Over-glow is
        // visible and can be reported; under-glow is invisible and cost 49 builds.
        t.True(!FigureHighlight.RestrictFor(FigureHighlight.MiniatureGrade.Refused, Animated),
               "a refused restriction falls back to the whole actor root — over-glow, never under");
    }

    // ---- D. THE TWO GRADES NO MEASURED FIGURE HAS EVER TAKEN ----------------------------------
    private static void RunRemainingGrades(Harness t)
    {
        t.Case("figureglow/remaining-grades");

        // An animated object holding no clonable renderer at all: the ModBuild 294 safety. The
        // whole root is used, so it must not restrict.
        Figure(t, 5, 0, 0, 9000, Animated,
               FigureHighlight.MiniatureGrade.RootFallback, false,
               "an animated object holding not one clonable renderer falls back to the root");
        Figure(t, 1, 0, 0, 1, Animated,
               FigureHighlight.MiniatureGrade.RootFallback, false,
               "...and at one candidate, which is the smallest case that can reach it");

        // Nothing to glow at all. `candidates <= 0` is tested FIRST, so it wins over every other
        // term including the animated object.
        Figure(t, 0, 0, 0, 0, Animated,
               FigureHighlight.MiniatureGrade.Nothing, false,
               "no clonable renderer anywhere under the actor");
        Figure(t, 0, 0, 0, 0, NoAnimator,
               FigureHighlight.MiniatureGrade.Nothing, false,
               "...and the same with no animated object either");
        Figure(t, -1, 0, 0, 0, Animated,
               FigureHighlight.MiniatureGrade.Nothing, false,
               "a negative candidate count cannot fall through to a restriction");
    }

    // ---- E. THE NULL-SAFETY PROPERTY ----------------------------------------------------------
    //
    // The clone loop dereferences m_AnimatedGameObject's transform ONLY when Restrict is true. So
    // "Restrict is never true without an animated object" is the whole of the null argument, and
    // it is asserted over EVERY member of the enum rather than over the grades that reach it
    // today — a sixth grade added next year is covered by this loop the moment it is declared.
    private static void RunNullSafety(Harness t)
    {
        t.Case("figureglow/null-safety");

        int seen = 0;
        foreach (FigureHighlight.MiniatureGrade g in
                 Enum.GetValues(typeof(FigureHighlight.MiniatureGrade)))
        {
            seen++;
            t.True(!FigureHighlight.RestrictFor(g, NoAnimator),
                   $"RestrictFor({g}, hasAnimatedObject: false) must be false — there is no "
                   + "transform to restrict to, and the clone loop would dereference it");
        }

        // The enum is the thing being swept, so its size is pinned: a member added without a line
        // in this file is a member nobody decided about.
        t.True(seen == 5,
               $"the sweep covered {seen} grades; MiniatureGrade declares 5 (Nothing, Proven, "
               + "RootFallback, Furniture, Refused) — add the new one to this file deliberately");

        // The positive half, so the loop above cannot pass by RestrictFor returning false always.
        t.True(FigureHighlight.RestrictFor(FigureHighlight.MiniatureGrade.Furniture, Animated),
               "Furniture WITH an animated object does restrict — the control for the sweep");
        t.True(FigureHighlight.RestrictFor(FigureHighlight.MiniatureGrade.Proven, Animated),
               "Proven WITH an animated object restricts too, which is what keeps the log saying "
               + "\"Cloned from m_AnimatedGameObject\"");
        t.True(!FigureHighlight.RestrictFor(FigureHighlight.MiniatureGrade.Nothing, Animated),
               "Nothing never restricts");
        t.True(!FigureHighlight.RestrictFor(FigureHighlight.MiniatureGrade.RootFallback, Animated),
               "RootFallback never restricts — that is what 'fallback to the root' means");
        t.True(!FigureHighlight.RestrictFor(FigureHighlight.MiniatureGrade.Refused, Animated),
               "Refused never restricts");
    }

    // ---- F. THE PROPERTY SWEEP ----------------------------------------------------------------
    //
    // The two claims the user-facing sentence is built on, over a generated range rather than a
    // list: the roster of figures lives in asset bundles this project cannot open, so a finite
    // list of vectors can never be complete and a property is the only honest form.
    private static void RunProperties(Harness t)
    {
        t.Case("figureglow/properties");

        int swept = 0;
        int[] vertexScales = { 0, 1, 60, 1766, 14846, 999999 };

        for (int candidates = 1; candidates <= 40; candidates++)
        {
            for (int under = 0; under <= candidates; under++)
            {
                foreach (int kept in vertexScales)
                {
                    foreach (int dropped in vertexScales)
                    {
                        FigureHighlight.MiniatureGrade g =
                            FigureHighlight.GradeOf(candidates, under, kept, dropped, Animated);
                        swept++;

                        // CLAIM 1 — "PROVEN means the restriction drops nothing", which is the
                        // sentence MiniatureVerdict.Headline prints to the user, asserted as
                        // arithmetic in both directions.
                        if (under >= candidates)
                            t.True(g == FigureHighlight.MiniatureGrade.Proven,
                                   $"under({under}) >= candidates({candidates}) must be Proven, got {g}");
                        else
                            t.True(g != FigureHighlight.MiniatureGrade.Proven,
                                   $"under({under}) < candidates({candidates}) must NOT be Proven, got {g}");

                        // CLAIM 2 — the guard can never fire on a figure that drops nothing, so
                        // ModBuild 341's guard cannot have changed the behaviour of any figure
                        // that was already correct. This is the claim that made the guard
                        // shippable without a hardware round.
                        if (under >= candidates)
                            t.True(g != FigureHighlight.MiniatureGrade.Refused,
                                   $"a figure that drops nothing is never Refused (got {g} at "
                                   + $"{under}/{candidates}, kept {kept}, dropped {dropped})");

                        // CLAIM 3 — the two fallback grades never restrict, so they can only ever
                        // glow MORE than the restriction would. Asserted inside the sweep as well
                        // as in section E because here it is asserted about grades the sweep
                        // PRODUCED, not about ones named by hand.
                        if (g == FigureHighlight.MiniatureGrade.Refused
                            || g == FigureHighlight.MiniatureGrade.RootFallback)
                            t.True(!FigureHighlight.RestrictFor(g, Animated),
                                   $"{g} must not restrict");
                    }
                }
            }
        }

        // The sweep's own size, so a loop that silently stopped iterating cannot pass. 860
        // (candidates, under) pairs times 36 vertex pairs.
        t.True(swept == 860 * vertexScales.Length * vertexScales.Length,
               $"the property sweep evaluated {swept} grades, expected "
               + $"{860 * vertexScales.Length * vertexScales.Length}");

        // The sweep above never passes hasAnimatedObject: false, because with no animated object
        // there is no split to reason about. That branch is a property of its own — every
        // candidate count and every vertex pair grades Proven and never restricts — so it is
        // swept separately rather than left to the single 'coinpile' row in section A.
        t.Case("figureglow/properties-no-animator");
        int props = 0;
        for (int candidates = 1; candidates <= 40; candidates++)
        {
            foreach (int kept in vertexScales)
            {
                foreach (int dropped in vertexScales)
                {
                    FigureHighlight.MiniatureGrade g =
                        FigureHighlight.GradeOf(candidates, 0, kept, dropped, NoAnimator);
                    props++;
                    t.True(g == FigureHighlight.MiniatureGrade.Proven,
                           $"a prop with {candidates} candidate(s) grades Proven, got {g}");
                    t.True(!FigureHighlight.RestrictFor(g, NoAnimator),
                           "a prop never restricts, whatever it grades");
                }
            }
        }
        t.True(props == 40 * vertexScales.Length * vertexScales.Length,
               $"the prop sweep evaluated {props} grades, expected "
               + $"{40 * vertexScales.Length * vertexScales.Length}");
    }
}
