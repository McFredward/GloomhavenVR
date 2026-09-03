using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// TASK #2 — PRE-GRAB proximity highlight for a board figure: an ANIMATED additive glow overlaid
/// ON TOP OF the figure's own textures (NO size change) so the player can see which mini their hand
/// would pluck before they grab it.
///
/// The earlier build "popped" the figure 1.12× larger; the user did not want a scale change. This
/// implementation instead clones the figure's renderers (sharing the SAME bones, so the overlay
/// tracks the live idle animation) and re-draws them with the bundled <c>GloomhavenVR/Overlay</c>
/// shader in ADDITIVE blend with a warm amber tint — a candle-lit shimmer laid over the mini's own
/// meshes. It is OCCLUSION-CORRECT: <c>_ZTest = LEqual</c> + <c>_ZWrite = 0</c> means the figure's
/// own opaque depth hides the glow behind walls exactly like the mini (see <see cref="FigureOverlay"/>).
/// An <see cref="OverlayPulse"/> component animates the tint over <c>Time.unscaledTime</c> so it
/// visibly breathes.
///
/// Driven purely by the single-winner <c>OnGrabHighlight(hand, true/false)</c> callback (the driver
/// suppresses every non-winner, so this only ever engages on the one grab candidate). Snapshot-free:
/// the overlay is a separate throwaway object graph, so clearing it restores the figure byte-identical
/// (its own renderers/materials are never touched).
///
/// <para><b>WHY THE RENDERER SEARCH MOVED OFF <c>m_AnimatedGameObject</c> IN ModBuild 294.</b> The
/// user's ModBuild 293 report was "der Boss-Drache hat immer noch KEIN Highlighting … alle anderen
/// Figuren schon". The hardware log says the opposite of what that sounds like: the boss engaged
/// SIX times (<c>pre-grab highlight ENGAGED (Right near ElderDrakeID …)</c>) against six CLEARs,
/// exactly like every other figure, and each of those six lines ends "overlaid on the figure's own
/// meshes", i.e. <see cref="Apply"/> returned TRUE. The highlight is not failing to fire. Something
/// is being drawn and the player cannot see it.</para>
///
/// <para>The one term that could produce that and is boss-specific is WHICH SUBTREE was cloned.
/// Until now that was <c>ActorBehaviour.m_AnimatedGameObject</c>, and the decompiled game sets it to
/// <c>MF.GetGameObjectAnimator(root).gameObject</c> — <b>the FIRST Animator in the actor's subtree
/// that owns a runtimeAnimatorController</b> (decompiled MF.cs:135-146), in depth-first order. That
/// is not defined to be the character's own Animator, it is defined to be whichever one the walk
/// reaches first, and the boss is the one figure in the log whose subtree provably carries foreign
/// animated content: its FIGURE REACH line names a <c>WP_Scoundrel_Dart</c> and two <c>WP_Dummy</c>
/// objects hanging off it, and its ActorBars census counts 36 active renderers of which 31 are
/// neither mesh nor skinned mesh. Clone the wrong Animator's subtree and you get a true return
/// value, a real material, a real pulse — and a glow on something the size of a dart.</para>
///
/// <para><b>THAT IS A HYPOTHESIS, AND THIS CLASS NO LONGER DEPENDS ON IT.</b> The search now starts
/// at the ACTOR ROOT, which is the same subtree <c>ActorBars</c> and <c>FigureGrabDriver</c> already
/// measure the figure with, so the glow covers whatever those two call "the figure". And
/// <see cref="Apply"/> now returns a REPORT: how many renderers were cloned, what world box they
/// span, and how many of them lay under <c>m_AnimatedGameObject</c> and what box THOSE span. One
/// hardware line then settles the hypothesis instead of the next round guessing again — if the two
/// boxes differ on the boss and agree everywhere else, the paragraph above was right; if they agree
/// on the boss too, it was wrong and the cause is elsewhere, and the line says so either way.</para>
///
/// <para><b>AND IN ModBuild 336 THE REPORT MOVED ONTO THE COPIES.</b> ModBuild 335's hardware log
/// answered the boss with "3 renderer(s) cloned from the ACTOR ROOT, spanning world y -1.18..5.66"
/// — every number in it read off the ORIGINAL renderers. It could not say where the clones landed,
/// whether they were enabled, what layer they were on, whether the head camera renders that layer,
/// what shader they carry, or whether a camera drew them; the complaint it exists to answer is "I
/// cannot see it". The report is now <see cref="FigureOverlay.MeasureClones"/> over the container
/// this class just built, compared against the originals' combined box, and
/// <see cref="OverlayVisibilityProbe"/> adds the outcome — <see cref="Renderer.isVisible"/> two
/// frames later, once a camera has had a chance to cull them.</para>
///
/// <para><b>ModBuild 339 — ACTOR FURNITURE IS NOT THE MINIATURE, AND THE LOG ALREADY SAID SO.</b>
/// The 338 hardware round produced a photograph: on the boss the glow is not missing, it lands on
/// TWO SMALL HORIZONTAL BARS at the dragon's snout and on nothing else. The census in the same log
/// names the three clones only as a count, but its <c>m_AnimatedGameObject</c> clause separates
/// them: <c>"1 of them lie under m_AnimatedGameObject 'MO_Elder_Drake' spanning world y
/// -1.15..5.54"</c> — and the union of ALL THREE spans exactly y -1.15..5.54, so the ONE under the
/// animator object is the body and the other two are contained within it. Those two are
/// <c>geo_bendyband</c> and <c>geo_bendyband (1)</c>: the wall-fade census tracks both as
/// <c>[mesh]</c> objects FLOATING at foot 2.97..3.07 wu, about 5-10 cm tall, drifting frame to
/// frame — thin horizontal bands riding the boss's head, at the height its own reach census puts
/// <c>WP_Dummy</c> (3.35) and <c>WP_Scoundrel_Dart</c> (3.24..3.38); and the WorldUI bar-anchor
/// census for <c>ElderDrakeID</c> names <c>geo_bendyband (1)</c> among that actor's own five mesh
/// renderers. They are actor furniture drawn by the game with a material that shapes the band; an
/// unlit copy of the raw mesh is a straight amber stroke standing where nothing is.</para>
///
/// <para>So the search restricts itself again — but on a MEASUREMENT, not on the 294 hypothesis.
/// The 338 log carries the same clause for every figure that works: <c>ALL of them lie under
/// m_AnimatedGameObject</c> for <c>HE_Brute</c>, <c>MO_RendingDrake_Elite</c> and
/// <c>MO_SpittingDrake</c>. Restricting to that subtree is therefore a proven NO-OP on all three
/// and removes exactly the two bands on the boss. The fallback keeps 294's safety: if the animator
/// object holds no clonable renderer at all, the whole actor root is used and the report says so,
/// so the "wrong Animator" case 294 was written against is visible in one line instead of being
/// silently glowed.</para>
///
/// <para><b>ModBuild 341 — THE MINIATURE VERDICT: WHERE THE GUARANTEE STOPS, SAID OUT LOUD.</b>
/// The user's question after 340 was "kannst du nun mit Sicherheit sagen, dass es mit ALLEN Figuren
/// im gesamten Spiel funktioniert?" — and the 13 MB hardware log that fixed the boss contains
/// exactly FOUR figure types (<c>HE_Brute</c>, <c>HE_Mindthief</c>, <c>MO_SpittingDrake</c>,
/// <c>MO_Elder_Drake</c>) out of a roster of 22 playable classes and 215 <c>CClass.ENPCModel</c>
/// entries. The figure prefabs live in Addressable asset bundles that are not on this machine, so
/// no offline enumeration of their renderer LAYOUT is possible at all. A "yes" backed by four
/// observations out of ~238 would be a guess.</para>
///
/// <para>So the answer is made structural instead of statistical, and it rests on two facts read
/// out of the decompiled game rather than out of a log:</para>
/// <list type="number">
///   <item><description><b>The game itself defines an actor's renderers as this exact subtree.</b>
///   <c>ActorBehaviour.SetActor</c> (decompiled ActorBehaviour.cs:121) sets
///   <c>m_Renderers = m_Animator.gameObject.GetComponentsInChildren&lt;Renderer&gt;()</c> — that is
///   the array its invisibility dissolve walks (:398-418). Any renderer outside
///   <c>m_AnimatedGameObject</c> is one the game will not even hide when the figure turns
///   invisible.</description></item>
///   <item><description><b>The game MOVES that subtree and not the root.</b>
///   <c>ActorBehaviour.DoTransform</c> (:468-548) writes
///   <c>m_AnimatedGameObject.transform.position</c> on every locomotion path and gives the root
///   only a rotation. A body renderer parented outside the animated object would therefore STAY
///   BEHIND every time the figure walks — a defect the base game would show without the mod. That
///   is not a hypothesis about the boss's bands; it is what the wall-fade census actually recorded
///   about them ("FLOATING … drifting frame to frame").</description></item>
/// </list>
///
/// <para>Between them those two close the "real body geometry outside <c>m_AnimatedGameObject</c>"
/// case for any figure that moves. What they do NOT close is the second case:
/// <c>MF.GetGameObjectAnimator</c>'s depth-first walk reaching a FOREIGN animated object before the
/// body, which would make the field point at the wrong subtree entirely. So
/// <see cref="Judge"/> adds a guard and a verdict:</para>
/// <list type="bullet">
///   <item><description>the restriction is REFUSED when it would discard at least as many vertices
///   as it keeps — a proven no-op on all five figures ever measured (four drop nothing at all; the
///   boss drops 120 vertices against 14 846), which turns that failure from an invisible
///   under-glow into a visible over-glow;</description></item>
///   <item><description>every figure is graded PROVEN / ROOT FALLBACK / FURNITURE DROPPED /
///   RESTRICTION REFUSED, and the grade is printed — <c>PROVEN</c> meaning the restriction dropped
///   NOTHING on this figure, so its glow is the same set of renderers the pre-340 whole-root glow
///   would have cloned and nothing about it rests on an assumption;</description></item>
///   <item><description><see cref="AuditFigure"/> runs that grade at ADOPTION, on every figure the
///   scenario spawns rather than only the ones a hand reaches, one line per distinct model per
///   session. A played session therefore PRODUCES the roster the bundles refuse to hand over, with
///   a verdict attached to each entry, and anything that is not PROVEN arrives as a
///   <c>VRLog.Alert</c>.</description></item>
/// </list>
/// </summary>
internal sealed partial class FigureHighlight
{
    // Warm amber-gold — the candle-lit-dungeon palette of Gloomhaven, added as light over the mini.
    private static readonly Color GlowTint = new Color(1.0f, 0.62f, 0.26f);

    /// <summary>
    /// Mod-owned objects are named with this prefix (<c>VROverlay</c>, <c>VRFigureHighlight</c>,
    /// <c>VR_FigureReach</c>). Cloning the figure from its ROOT means the walk can now reach our own
    /// previous overlay, and an overlay of an overlay doubles every frame it is re-applied. Nothing
    /// the game ships under an actor starts with these two letters (they are <c>HE_</c>, <c>MO_</c>,
    /// <c>WP_</c>, <c>C_*_JNT</c>, <c>Base</c>, <c>Actor(Clone)</c>).
    /// </summary>
    private const string ModOwnedPrefix = "VR";

    private static readonly List<Renderer> Scratch = new(32);

    /// <summary>Pass-1 survivors: the renderers that passed the kind/ours/ring/enabled filters and
    /// are still eligible when pass 2 decides which of them are the MINIATURE.</summary>
    private static readonly List<Renderer> Candidates = new(16);

    /// <summary>Scratch for <see cref="AuditFigure"/>, which runs on a different call than
    /// <see cref="Apply"/> and must not share <see cref="Candidates"/> with a live hover.</summary>
    private static readonly List<Renderer> AuditScratch = new(16);

    /// <summary>Pass-3 input (ModBuild 366): the pass-1 survivors that pass 2 also kept, i.e. the
    /// exact set the clone loop will walk. Its own list rather than a slice of
    /// <see cref="Candidates"/>, because the surface guard's denominator has to be that set and
    /// nothing else — see the pass-3 note in <see cref="Apply"/>.</summary>
    private static readonly List<Renderer> SurfaceScratch = new(16);

    /// <summary>
    /// Figures already audited this session — <see cref="AuditFigure"/> writes one line per
    /// DISTINCT figure, not one per spawn. Never read outside the audit.
    ///
    /// <para>The key is the class id PLUS the name of <c>m_AnimatedGameObject</c>, not the class id
    /// alone, because one class can wear more than one model and the model is what this audit is
    /// about: <c>CClass.Models</c> / <c>CActor.ChosenModelIndex</c> pick between several, and the
    /// Demolitionist's mech form is an <c>OverrideCharacterModel</c> on the SAME
    /// <c>CCharacterClass</c> (decompiled CChangeCharacterModelActiveBonus.cs:18 →
    /// ChangeModelSMB.cs:49-71, which despawns the old figure and spawns a new one). Keying on the
    /// class id alone would audit the Demolitionist and then skip her mech.</para>
    /// </summary>
    private static readonly HashSet<string> Audited = new();

    private GameObject? _overlayRoot;

    /// <summary>True while the highlight overlay exists.</summary>
    public bool Active => _overlayRoot != null;

    /// <summary>How many renderers the LAST <see cref="Apply"/> cloned (0 = it lit nothing), and
    /// whether the container it built was active in the hierarchy at that instant. Kept past
    /// <see cref="Clear"/> so a grab that consumed the hover can still report what the hover
    /// covered — read by <c>ActorPropBody.LogHoldPicture</c>.</summary>
    public int LastCloned { get; private set; }

    public bool LastContainerActive { get; private set; }

    /// <summary>How the pass-1 filters disposed of the renderers they refused, for the report.</summary>
    private struct Filtered
    {
        public int OnActiveObjects, Kind, ModOwned, Ring, Disabled;
    }

    // The MiniatureGrade enum moved to FigureGlowGrade.cs, together with the two methods that
    // decide it. That file is a part of this class, so every MiniatureGrade.* below still names
    // this type and every hardware log keeps the vocabulary it has used since ModBuild 340.

    /// <summary>
    /// The structural answer for ONE figure: what the ModBuild 340 rule would do to it, how much
    /// geometry that keeps and drops, and how many Animators the game had to choose between when it
    /// picked <c>m_AnimatedGameObject</c>. <see cref="Restrict"/> is the DECISION the clone loop
    /// obeys, so the verdict and the behaviour cannot drift apart.
    /// </summary>
    internal readonly struct MiniatureVerdict
    {
        public MiniatureVerdict(MiniatureGrade grade, bool restrict, int candidates, int underAnimated,
                                int keptVerts, int droppedVerts, int animators, string animatedLayer,
                                string droppedNames)
        {
            Grade = grade;
            Restrict = restrict;
            Candidates = candidates;
            UnderAnimated = underAnimated;
            KeptVerts = keptVerts;
            DroppedVerts = droppedVerts;
            Animators = animators;
            AnimatedLayer = animatedLayer;
            DroppedNames = droppedNames;
        }

        public readonly MiniatureGrade Grade;

        /// <summary>True when the clone loop must copy ONLY the renderers under
        /// <c>m_AnimatedGameObject</c>. False means the whole actor root.</summary>
        public readonly bool Restrict;

        public readonly int Candidates;
        public readonly int UnderAnimated;
        public readonly int KeptVerts;
        public readonly int DroppedVerts;

        /// <summary>Animators owning a <c>runtimeAnimatorController</c> on an ACTIVE object under
        /// the actor root — the exact population <c>MF.GetGameObjectAnimator</c> picks the FIRST of.
        /// 1 means the game had no choice to get wrong.</summary>
        public readonly int Animators;

        /// <summary>
        /// THE GAME'S OWN LABEL FOR "THIS IS THE ANIMATED MINIATURE OBJECT": the Unity layer name of
        /// <c>m_AnimatedGameObject</c>, or <c>&lt;none&gt;</c> when the actor has no such object.
        ///
        /// <para><c>Choreographer.CreateCharacterActor</c> (decompiled Choreographer.cs:886, and the
        /// identical predicate at :1069) does NOT use <c>MF.GetGameObjectAnimator</c> to find the
        /// character. It picks <c>GetComponentsInChildren&lt;Animator&gt;().FirstOrDefault(x =&gt;
        /// x.gameObject.layer == LayerMask.NameToLayer("Hero") || ... "Monster")</c> and parents the
        /// interaction object under it — with an explicit <c>LogWarning</c> fallback for "unable to
        /// find animator on correct layer". So the game maintains a SECOND, layer-based notion of
        /// which Animator is the miniature, and the two can in principle disagree.</para>
        ///
        /// <para>Reporting the layer is therefore an INDEPENDENT check on the same question the
        /// vertex comparison answers by size: if <c>m_AnimatedGameObject</c> sits on Hero or
        /// Monster, the game's own two mechanisms agree about it. It is reported, never acted on —
        /// a layer name is data, and a new instrument's first output is a hypothesis.</para>
        /// </summary>
        public readonly string AnimatedLayer;

        /// <summary>Up to four <see cref="FigureOverlay.DescribeSource"/> strings for the renderers
        /// the restriction drops, or the empty string when it drops none.</summary>
        public readonly string DroppedNames;

        public int Dropped => Candidates - UnderAnimated;

        /// <summary>True when nothing about this figure rests on a judgement — the restricted and
        /// unrestricted candidate sets are the SAME set, so the rule cannot have removed anything
        /// the player can see.</summary>
        public bool ProvenByConstruction => Grade == MiniatureGrade.Proven;

        /// <summary>One sentence, appended to the always-printed hover line and reused verbatim by
        /// <see cref="AuditFigure"/>, so the two can never say different things about one figure.</summary>
        public string Headline => Grade switch
        {
            MiniatureGrade.Nothing =>
                "VERDICT: NOTHING TO GLOW — no clonable renderer survived the filters.",
            MiniatureGrade.Proven =>
                $"VERDICT: PROVEN — the m_AnimatedGameObject restriction drops NOTHING here "
                + $"({UnderAnimated} of {Candidates} candidate renderer(s) already lie under it), so "
                + "this glow is the same set of renderers the whole-actor-root glow would have "
                + "cloned. No assumption is load-bearing on this figure.",
            MiniatureGrade.RootFallback =>
                $"VERDICT: ROOT FALLBACK — m_AnimatedGameObject (layer '{AnimatedLayer}') holds NOT "
                + $"ONE of the {Candidates} clonable renderer(s), so the whole actor root is glowed. "
                + "That is the game's own m_Renderers array empty for this actor — the shape of a "
                + "CObjectActor whose visible geometry is an AttachedProp outside its own root. The "
                + "glow errs toward MORE, never less.",
            MiniatureGrade.Furniture =>
                $"VERDICT: FURNITURE DROPPED — the restriction removes {Dropped} of {Candidates} "
                + $"candidate renderer(s), {DroppedVerts} vertices against the {KeptVerts} it keeps. "
                + $"m_AnimatedGameObject is on layer '{AnimatedLayer}' and the game chose it from "
                + $"{Animators} Animator(s) with a controller under this actor. This figure's glow "
                + $"rests on that choice being the miniature. DROPPED: {DroppedNames}",
            _ =>
                "VERDICT: RESTRICTION REFUSED — restricting to m_AnimatedGameObject would have "
                + $"dropped {Dropped} of {Candidates} candidate renderer(s) carrying {DroppedVerts} "
                + $"vertices against only {KeptVerts} kept, so it is not plausibly the miniature "
                + $"(layer '{AnimatedLayer}', chosen from {Animators} Animator(s) with a controller "
                + "under this actor). The WHOLE ACTOR ROOT is glowed instead — too much rather than "
                + $"too little. WOULD HAVE DROPPED: {DroppedNames}",
        };
    }

    /// <summary>
    /// Build the animated additive overlay over the figure at <paramref name="figureRoot"/>. No-op
    /// if already active, or if the bundled Overlay shader / any renderer is unavailable (returns
    /// false). The overlay container is parented under <paramref name="figureRoot"/> so the game's
    /// own renderer sweeps (jump-exit opacity, invisibility) never enumerate the extra passes.
    ///
    /// <para><paramref name="animatedRoot"/> is the game's <c>m_AnimatedGameObject</c>; whether the
    /// clone is restricted to it is decided by <see cref="Judge"/> and stated in the same line.
    /// <paramref name="excludeSubtree"/> is the actor's selection ring (<c>m_Hilight</c>), which
    /// hangs off the actor root, draws <c>ZTest Always</c> and is not part of the miniature:
    /// gilding it would change how every figure looks, not just the boss.</para>
    ///
    /// <para>Returns true when at least one renderer was cloned; <paramref name="report"/> is always
    /// written and is what the caller logs.</para>
    /// </summary>
    public bool Apply(GameObject figureRoot, GameObject? animatedRoot, Transform? excludeSubtree,
                      string label, out string report)
    {
        report = string.Empty;
        if (Active)
            return true;
        if (figureRoot == null)
        {
            report = "no figure root";
            return false;
        }

        // THE AUDIT MUST NOT DEPEND ON THE DRIVER REMEMBERING TO CALL IT. AuditFigure's real home is
        // the adoption pass, where it sees every figure the scenario spawns; calling it here as well
        // costs one dictionary probe per hover (it is keyed per model and returns immediately on the
        // second call) and guarantees that a build which loses the adoption call still grades every
        // figure the player actually reaches. A verdict nobody invokes is the shape of the "gated
        // remedy that never ran" — the fix shipped behind the instrument built to test it.
        AuditFigure(figureRoot, animatedRoot, excludeSubtree, label);

        Material? mat = FigureOverlay.MakeOverlayMaterial(GlowTint, additive: true);
        if (mat == null)
        {
            report = "the bundle is missing the GloomhavenVR/Overlay shader";
            return false; // skip rather than pierce walls
        }

        var root = new GameObject("VRFigureHighlight");
        root.transform.SetParent(figureRoot.transform, worldPositionStays: false);

        // PASS 1 — WHICH RENDERERS ARE CANDIDATES AT ALL (kind, ours, the ring, switched off).
        Candidates.Clear();
        Filtered f = CollectCandidates(figureRoot, excludeSubtree, root.transform,
                                       requireEnabled: true, Candidates);

        // PASS 2 — WHICH OF THE CANDIDATES ARE THE MINIATURE. The rule and its verdict are ONE
        // computation (see Judge): `verdict.Restrict` is what the loop below obeys, so no future
        // edit can improve the rule while leaving the report describing the old one.
        MiniatureVerdict verdict = Judge(figureRoot, animatedRoot, Candidates);
        Transform? animatedT = animatedRoot != null ? animatedRoot.transform : null;

        // PASS 3 — WHICH OF THOSE ACTUALLY DRAW THEIR OWN SURFACE (ModBuild 366). User,
        // 2026-09-03: "zusätzlich zu dem korrekten Highlighting noch so ein Rechteck was da drin
        // steckt und nicht hingehört." A decal box or a beam card contributes NO pixels of its own
        // geometry in the game and a hard rectangle in an unlit additive re-draw of it. The rule,
        // the evidence for it and the alternatives that were rejected all live in one place —
        // FigureOverlay's surface-rule note — because the frozen ghost has the identical defect and
        // two copies of a rule is two places for the next fix to land on one of them.
        //
        // Judged over the set that survives PASS 2, not over every candidate: `verdict.Restrict`
        // may already have removed most of the actor, and a guard whose denominator is the wrong
        // population is the "ratio with two populations" defect this project has already paid for.
        SurfaceScratch.Clear();
        for (int i = 0; i < Candidates.Count; i++)
        {
            Renderer r = Candidates[i];
            if (verdict.Restrict && !IsUnder(r.transform, animatedT!))
                continue;
            SurfaceScratch.Add(r);
        }
        FigureOverlay.SurfaceVerdict surfaces = FigureOverlay.JudgeSurfaces(SurfaceScratch);
        SurfaceScratch.Clear();

        int cloned = 0;
        Bounds originals = default;
        var clonedDesc = new List<string>(6);
        var sliverDesc = new List<string>(4);
        for (int i = 0; i < Candidates.Count; i++)
        {
            Renderer r = Candidates[i];
            if (verdict.Restrict && !IsUnder(r.transform, animatedT!))
                continue; // actor furniture — named by the verdict, not listed a second time here

            if (surfaces.Enforce && !FigureOverlay.DrawsOwnSurface(r, out _))
                continue; // non-surface — named in full by the surface verdict, not twice here

            if (!CloneOne(r, root.transform, mat))
                continue;

            // NAME WHAT WAS COPIED. A combined box cannot separate a dragon from a 5 cm band
            // hanging off its head — the boss's ModBuild 338 census reported three clones whose
            // union AGREED with the originals to 0 mm while two of the three were the bands the
            // player photographed. See FigureOverlay.DescribeSource.
            if (clonedDesc.Count < 6)
                clonedDesc.Add(FigureOverlay.DescribeSource(r));
            // …AND EVERY SLIVER IS NAMED WHATEVER THE CAP SAYS (ModBuild 366). The list above is
            // capped at six and this prop kind has 26 renderers; the one the user photographed is
            // by definition the odd one out, so it is the one an ellipsis eats. A renderer whose
            // box is a card rather than an object gets its own entry even when the cap is full.
            else if (sliverDesc.Count < 8 && FigureOverlay.IsSliver(r.bounds, out _, out _))
                sliverDesc.Add(FigureOverlay.DescribeSource(r));

            // The ORIGINAL's box, accumulated only so the report has something to compare the
            // CLONES against. It is never the answer on its own — see the report below.
            Bounds b = r.bounds;
            if (cloned == 0) originals = b; else originals.Encapsulate(b);
            cloned++;
        }
        Candidates.Clear();

        LastCloned = cloned;
        LastContainerActive = cloned > 0 && root.activeInHierarchy;

        if (cloned == 0)
        {
            Object.Destroy(root);
            Object.Destroy(mat);
            report = $"NOTHING TO GLOW — {f.OnActiveObjects} renderer(s) on active objects under the "
                     + $"actor root, {f.Kind} non-mesh, {f.Disabled} with "
                     + $"Renderer.enabled=false, {f.Ring} on the selection ring, "
                     + $"{f.ModOwned} mod-owned, {verdict.Dropped} actor furniture outside "
                     + $"m_AnimatedGameObject. {verdict.Headline} {surfaces.Headline}";
            return false;
        }

        OverlayPulse pulse = root.AddComponent<OverlayPulse>();
        pulse.Init(mat, GlowTint); // pulse owns + destroys the material
        _overlayRoot = root;

        // THE ONE LINE THAT MAKES AN INVISIBLE HIGHLIGHT ANSWERABLE. Before ModBuild 294 the caller
        // logged a boolean, and a boolean cannot tell "the glow covers the dragon" from "the glow
        // covers a dart hanging off the dragon" — which is exactly the pair the ModBuild 293 log
        // could not separate for ElderDrakeID.
        //
        // ...AND FROM ModBuild 336 IT MEASURES THE CLONES. The 294 report was still built from
        // `r.bounds` on the ORIGINAL renderers, so "3 renderer(s) cloned from the ACTOR ROOT,
        // spanning world y -1.18..5.66" described the renderers it had copied FROM. It said nothing
        // about where the copies landed, whether they were enabled, what layer they were on,
        // whether the head camera renders that layer, or what shader they carry — and the complaint
        // it was written to answer is "I cannot see it". FigureOverlay.MeasureClones now walks the
        // container that was just built, and OverlayVisibilityProbe reports two frames later
        // whether anything actually drew it.
        //
        // ...AND FROM ModBuild 341 IT CARRIES THE VERDICT. Every number above still had to be read
        // and divided by a human before it said whether the 340 rule was SAFE on this figure: the
        // clause "1 of 3 candidate renderer(s) lie under it" is the whole boss defect written as an
        // arithmetic problem nobody was asked to solve. verdict.Headline states the answer instead.
        string sourceSays = verdict.Restrict
            ? $"Cloned from m_AnimatedGameObject '{animatedRoot!.name}', not the whole actor root: "
              + $"{verdict.UnderAnimated} of {verdict.Candidates} candidate renderer(s) lie under it."
            : animatedT == null
                ? "Cloned from the ACTOR ROOT; the actor has NO m_AnimatedGameObject."
                : $"Cloned from the ACTOR ROOT; m_AnimatedGameObject '{animatedRoot!.name}' is not "
                  + "the subtree the glow was restricted to — see the verdict.";

        report = FigureOverlay.MeasureClones(root.transform, "GLOW", cloned > 0, originals, cloned)
                 + " " + sourceSays
                 + $" CLONED: {string.Join("; ", clonedDesc)}"
                 + (cloned > clonedDesc.Count ? $" (+{cloned - clonedDesc.Count} more not named)" : "")
                 + "."
                 + (sliverDesc.Count == 0
                     ? " No CLONED renderer past the named ones is a card-shaped sliver."
                     : $" CLONED SLIVERS past the cap ({sliverDesc.Count}, named whatever the cap "
                       + $"says): {string.Join("; ", sliverDesc)}.")
                 + " Skipped: "
                 + $"{f.Kind} non-mesh, {f.Disabled} with Renderer.enabled=false, "
                 + $"{f.Ring} on the selection ring, {f.ModOwned} mod-owned, of "
                 + $"{f.OnActiveObjects} renderer(s) on active objects. "
                 + verdict.Headline + " " + surfaces.Headline;
        OverlayVisibilityProbe.Attach(root, $"{label}/glow", $"GLOW on {label}");
        return true;
    }

    /// <summary>
    /// THE ENUMERATION MECHANISM. Judge ONE figure the moment the driver adopts it — hovered or
    /// not — and write one line per DISTINCT figure class per session, so a played session produces
    /// the roster of every miniature that stood on the board together with the verdict for each.
    ///
    /// <para><b>Why this is not folded into <see cref="Apply"/>.</b> Apply only ever runs on a
    /// figure the player's hand reached. The ModBuild 340 hardware log is 13 MB and contains four
    /// figure types for exactly that reason, out of a roster of many — which is why "does it work
    /// for ALL figures?" could not be answered from it at all. Auditing at ADOPTION turns the
    /// question from "which figures did he happen to hover" into "which figures did the scenario
    /// spawn".</para>
    ///
    /// <para><b>Why it counts DISABLED renderers too</b> (<c>requireEnabled: false</c>). Adoption
    /// happens the frame a figure appears, while <c>MaterialLoaderData.LoadMaterials</c> may still
    /// have its renderer components switched off. The audit asks a STRUCTURAL question — which
    /// subtree holds this figure's geometry — and a renderer that is momentarily disabled is
    /// structurally in the same place. <see cref="Apply"/>'s own verdict, computed with the live
    /// filter, is printed on every hover and is the one that describes what was actually drawn.</para>
    ///
    /// <para><b>Cost.</b> Two <c>GetComponentsInChildren</c> walks over one actor subtree (36
    /// renderers on the largest figure measured), ONCE per figure class per session, on the same
    /// call that already runs <c>FigureGrabDriver.LogFigureReach</c>. Nothing per frame.</para>
    /// </summary>
    internal static void AuditFigure(GameObject? figureRoot, GameObject? animatedRoot,
                                     Transform? excludeSubtree, string label)
    {
        string model = animatedRoot != null ? animatedRoot.name : "<no animated object>";
        if (figureRoot == null || string.IsNullOrEmpty(label) || !Audited.Add(label + "|" + model))
            return;

        AuditScratch.Clear();
        CollectCandidates(figureRoot, excludeSubtree, ownContainer: null,
                          requireEnabled: false, AuditScratch);
        MiniatureVerdict verdict = Judge(figureRoot, animatedRoot, AuditScratch);
        // ModBuild 366 — the roster answers the OVER-exclusion question too. "Does the surface rule
        // eat any figure?" cannot be answered from the four figures a player's hand happened to
        // reach; the audit sees every figure the scenario spawns, so it is the only instrument that
        // can say so before a hardware round is spent on it.
        FigureOverlay.SurfaceVerdict surfaces = FigureOverlay.JudgeSurfaces(AuditScratch);
        AuditScratch.Clear();

        string who = $"'{label}' (m_AnimatedGameObject '{model}', layer '{verdict.AnimatedLayer}', "
                     + $"{verdict.Animators} Animator(s) with a controller under the actor)";

        if (verdict.ProvenByConstruction)
        {
            // HW-VERIFY: this is the roster line — one per distinct figure per session, PROVEN or
            // not. It must stay at a tier the DEFAULT log level prints, because a session whose
            // audit printed nothing is indistinguishable from a session in which the audit never
            // ran, and that ambiguity is the whole reason the line exists.
            VRLog.Note("FigureGrab",
                $"MINIATURE AUDIT {Audited.Count}: {who} — {verdict.Headline} {surfaces.Headline}");
            return;
        }

        // HW-VERIFY: the figures that are NOT proven by construction are the entire residual risk
        // in "does the glow work on every figure". This must print at the default level or a figure
        // can under-glow in a corner of the game with nothing in the log to say so — which is
        // exactly what happened to the boss for sixteen builds.
        VRLog.Alert("FigureGrab",
            $"MINIATURE AUDIT {Audited.Count}: {who} is NOT proven by construction — {verdict.Headline}"
            + $" {surfaces.Headline}");
    }

    /// <summary>
    /// PASS 1, SHARED. Fill <paramref name="into"/> with the renderers under
    /// <paramref name="figureRoot"/> that could be cloned at all, and report how the refusals split.
    /// <paramref name="requireEnabled"/> is the one axis on which the hover path and the audit
    /// differ, and it is a PARAMETER rather than a second copy of the loop for the same reason
    /// <see cref="FigureOverlay.SourceMesh"/> exists: two copies are two places to disagree.
    /// </summary>
    private static Filtered CollectCandidates(GameObject figureRoot, Transform? excludeSubtree,
                                              Transform? ownContainer, bool requireEnabled,
                                              List<Renderer> into)
    {
        var f = default(Filtered);
        Scratch.Clear();
        figureRoot.GetComponentsInChildren(includeInactive: false, Scratch);
        f.OnActiveObjects = Scratch.Count;

        for (int i = 0; i < Scratch.Count; i++)
        {
            Renderer r = Scratch[i];
            if (r == null)
                continue;
            // Only SURFACE geometry, the same filter ActorBars and FigureGrabDriver use: a particle
            // or trail renderer is an effect volume, and an additive clone of one is a smear.
            if (r is not MeshRenderer && r is not SkinnedMeshRenderer)
            {
                f.Kind++;
                continue;
            }
            if ((ownContainer != null && IsUnder(r.transform, ownContainer))
                || HasModOwnedAncestor(r.transform, figureRoot.transform))
            {
                f.ModOwned++;
                continue;
            }
            if (excludeSubtree != null && IsUnder(r.transform, excludeSubtree))
            {
                f.Ring++;
                continue;
            }
            // A renderer the game has switched OFF must not acquire a glowing double. This is not
            // hypothetical: the async material loader disables the component while materials stream
            // (MaterialLoaderData.LoadMaterials), and a sheathed weapon is disabled outright. The
            // pre-ModBuild-294 code walked with includeInactive:TRUE and never checked `enabled`,
            // so every hidden prop on the figure got a visible amber ghost of itself in mid-air.
            if (requireEnabled && !r.enabled)
            {
                f.Disabled++;
                continue;
            }
            into.Add(r);
        }
        Scratch.Clear();
        return f;
    }

    /// <summary>
    /// THE RULE AND ITS OWN VERDICT, IN ONE PLACE. Decide whether the clone must be restricted to
    /// <c>m_AnimatedGameObject</c>, and grade how much that decision rests on an assumption.
    ///
    /// <para><b>The rule (ModBuild 340, unchanged).</b> Restrict when that subtree holds at least
    /// one candidate; fall back to the whole actor root when it holds none.</para>
    ///
    /// <para><b>The guard (ModBuild 341, new).</b> REFUSE the restriction when it would discard at
    /// least as many vertices as it keeps. That comparison cannot fire on any figure measured so
    /// far — the four known-good ones drop nothing at all, and the boss drops 120 vertices against
    /// 14 846 — so it is a proven no-op on every piece of evidence that exists, and it converts the
    /// one remaining structural failure (<c>MF.GetGameObjectAnimator</c>'s depth-first walk
    /// reaching a FOREIGN animated object before the body) from an invisible under-glow into a
    /// visible over-glow that also announces itself.</para>
    ///
    /// <para><b>Why vertex count.</b> See <see cref="FigureOverlay.VertexCount"/>: renderer COUNT
    /// makes one body mesh a minority against two bands, and a bounding BOX cannot separate a band
    /// stretched across a snout from a wing.</para>
    ///
    /// <para><b>The Animator census is the term that measures case (ii).</b>
    /// <c>ActorBehaviour.SetActor</c> sets <c>m_Animator = MF.GetGameObjectAnimator(root)</c> and
    /// <c>m_AnimatedGameObject = m_Animator.gameObject</c> (decompiled ActorBehaviour.cs:114-115),
    /// and that method (decompiled MF.cs:395-406) returns the FIRST Animator in
    /// <c>GetComponentsInChildren&lt;Animator&gt;()</c> order — active objects only — that owns a
    /// <c>runtimeAnimatorController</c>. When exactly ONE such Animator exists under the actor the
    /// game had no choice to get wrong, and <c>m_AnimatedGameObject</c> IS the animated character.
    /// The count is REPORTED rather than acted on, because a figure carrying two of them is not
    /// thereby wrong — it is thereby worth looking at.</para>
    /// </summary>
    internal static MiniatureVerdict Judge(GameObject figureRoot, GameObject? animatedRoot,
                                           List<Renderer> candidates)
    {
        Transform? animatedT = animatedRoot != null ? animatedRoot.transform : null;
        int under = 0, keptVerts = 0, droppedVerts = 0;
        var droppedDesc = new List<string>(4);

        for (int i = 0; i < candidates.Count; i++)
        {
            Renderer r = candidates[i];
            int verts = FigureOverlay.VertexCount(r);
            if (animatedT == null || IsUnder(r.transform, animatedT))
            {
                under++;
                keptVerts += verts;
            }
            else
            {
                droppedVerts += verts;
                if (droppedDesc.Count < 4)
                    droppedDesc.Add(FigureOverlay.DescribeSource(r));
            }
        }

        int dropped = candidates.Count - under;
        string droppedNames = droppedDesc.Count == 0
            ? string.Empty
            : string.Join("; ", droppedDesc)
              + (dropped > droppedDesc.Count ? $" (+{dropped - droppedDesc.Count} more not named)" : "")
              + ".";
        int animators = CountAnimatorsWithController(figureRoot);
        string layer = animatedRoot != null ? LayerMask.LayerToName(animatedRoot.layer) : "<none>";
        if (string.IsNullOrEmpty(layer))
            layer = animatedRoot != null ? $"#{animatedRoot.layer} (unnamed)" : "<none>";

        bool hasAnimated = animatedT != null;
        MiniatureGrade grade = GradeOf(candidates.Count, under, keptVerts, droppedVerts, hasAnimated);
        return new MiniatureVerdict(grade, RestrictFor(grade, hasAnimated), candidates.Count, under,
                                    keptVerts, droppedVerts, animators, layer,
                                    grade == MiniatureGrade.Proven || grade == MiniatureGrade.Nothing
                                        ? string.Empty : droppedNames);
    }

    // THE PURE DECISION — GradeOf, RestrictFor and the MiniatureGrade enum they produce — NOW
    // LIVES IN FigureGlowGrade.cs, a part of this same class that references no Unity type at
    // all. tests/GloomhavenVR.WireTests links that file, so FigureHighlightGradeVectors.cs
    // drives the SHIPPED methods rather than a copy of them; see the header of that file for
    // why a copy would have agreed with every broken build. Moved unchanged.

    /// <summary>
    /// How many Animators owning a <c>runtimeAnimatorController</c> sit on ACTIVE objects under
    /// <paramref name="figureRoot"/> — deliberately the same population, and the same
    /// <c>includeInactive:false</c> default, that <c>MF.GetGameObjectAnimator</c> takes the first of.
    /// </summary>
    private static int CountAnimatorsWithController(GameObject figureRoot)
    {
        int n = 0;
        foreach (Animator a in figureRoot.GetComponentsInChildren<Animator>())
            if (a != null && a.runtimeAnimatorController != null)
                n++;
        return n;
    }

    /// <summary>Destroy the overlay (idempotent). Restores the figure byte-identical — its own
    /// renderers and materials were never modified.</summary>
    public void Clear()
    {
        if (_overlayRoot != null)
        {
            Object.Destroy(_overlayRoot); // OverlayPulse.OnDestroy frees the material
            _overlayRoot = null;
        }
    }

    /// <summary>
    /// Re-draw one renderer's mesh with <paramref name="overlayMat"/> under
    /// <paramref name="container"/>. A skinned clone references the ORIGINAL bones and rootBone, so
    /// it deforms in lock-step with the live figure and needs no per-frame tracking; its bounds are
    /// therefore identical to the original's and it is culled with it.
    ///
    /// <para>This duplicates <see cref="FigureOverlay.CloneRenderersSharingBones"/>'s per-renderer
    /// body rather than calling it, because that method takes a subtree ROOT and this class now has
    /// to filter the subtree renderer by renderer (mod-owned objects, the selection ring, disabled
    /// components). The ghost path in <see cref="FigureOverlay"/> is untouched.</para>
    /// </summary>
    private static bool CloneOne(Renderer r, Transform container, Material overlayMat)
    {
        if (r is SkinnedMeshRenderer smr)
        {
            if (smr.sharedMesh == null)
                return false;
            var go = new GameObject("VROverlay");
            go.transform.SetParent(container, worldPositionStays: false);
            var clone = go.AddComponent<SkinnedMeshRenderer>();
            clone.sharedMesh = smr.sharedMesh;
            clone.bones = smr.bones;            // share the LIVE bones → tracks animation
            clone.rootBone = smr.rootBone;
            clone.localBounds = smr.localBounds;
            clone.quality = smr.quality;
            clone.updateWhenOffscreen = smr.updateWhenOffscreen;
            // Blend-shape weights live on the RENDERER, not the bones a clone shares, and a fresh
            // SkinnedMeshRenderer starts them all at zero — an overlay of the figure in a pose the
            // figure is not in. See FigureOverlay.CopyBlendShapeWeights.
            FigureOverlay.CopyBlendShapeWeights(smr, clone);
            clone.sharedMaterials = Fill(smr.sharedMesh.subMeshCount, overlayMat);
            clone.shadowCastingMode = ShadowCastingMode.Off;
            clone.receiveShadows = false;
            return true;
        }

        if (r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
        {
            // Static mesh part (a weapon, a shield, a prop). Parent under the container so the whole
            // overlay is torn down by destroying that one object, and match the part's current world
            // transform. It rides the figure via the container; a bone-driven static part that moves
            // during the hover is an accepted edge case (the hover is short and the part is small).
            var go = new GameObject("VROverlay");
            go.transform.SetParent(container, worldPositionStays: false);
            go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            // MATCH THE PART'S WORLD SCALE, which the pre-ModBuild-294 code left at the container's.
            // The container hangs off the actor root, so a prop with any local scale of its own drew
            // its glow at the wrong size — invisible on a mini, and on the 198x diorama not. The
            // arithmetic moved into FigureOverlay in ModBuild 336, when the ghost path was found to
            // be missing this same term; one copy now, so the next fix cannot land on one of two.
            FigureOverlay.MatchCloneWorldScale(go.transform, container, r.transform);
            var cf = go.AddComponent<MeshFilter>();
            cf.sharedMesh = mf.sharedMesh;
            var cr = go.AddComponent<MeshRenderer>();
            cr.sharedMaterials = Fill(mf.sharedMesh.subMeshCount, overlayMat);
            cr.shadowCastingMode = ShadowCastingMode.Off;
            cr.receiveShadows = false;
            return true;
        }
        return false;
    }

    private static Material[] Fill(int count, Material m)
    {
        var mats = new Material[Mathf.Max(1, count)];
        for (int i = 0; i < mats.Length; i++)
            mats[i] = m;
        return mats;
    }

    /// <summary>True when <paramref name="t"/> is <paramref name="ancestor"/> or sits under it.</summary>
    private static bool IsUnder(Transform t, Transform ancestor)
    {
        for (Transform? c = t; c != null; c = c.parent)
            if (ReferenceEquals(c, ancestor))
                return true;
        return false;
    }

    /// <summary>
    /// True when any transform between <paramref name="t"/> and <paramref name="stopAt"/>
    /// (inclusive of t, exclusive of stopAt) is a mod-owned object. See <see cref="ModOwnedPrefix"/>.
    /// </summary>
    private static bool HasModOwnedAncestor(Transform t, Transform stopAt)
    {
        for (Transform? c = t; c != null && !ReferenceEquals(c, stopAt); c = c.parent)
            if (c.name.StartsWith(ModOwnedPrefix, System.StringComparison.Ordinal))
                return true;
        return false;
    }
}
