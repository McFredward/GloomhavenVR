using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// A PROP'S RENDERERS ARE NEVER SPLIT ACROSS WALL UNITS — the decapitated skeleton, second
/// report (2026-08-19, hardware, ModBuild 166, verbatim): <i>"Der Kopf des Skeletts wird immer
/// noch ausgeblendet (selber Effekt wie in dem Screenshot zuvor)."</i> The screenshot is
/// <c>.planning/debug/skelet.jpg</c> — a skeleton against a low wall, head missing.
///
/// <para>THE CAUSE, from <c>.planning/debug/LogOutput.log</c> rather than from reasoning. The
/// mounted pass's NEAR-MISS census prints, for every airborne prop candidate, the one segment
/// that already holds it:</para>
/// <code>
/// 'CR_OS_Skeleton_Statue_Skull'[mesh]  anchor 5.1 gap 0.00: already the wall renderer of 'Wall 2' (that wall's fade 1.00)
/// 'CR_OS_Skeleton_Statue_Skull'[mesh]  anchor 5.1 gap 0.00: already the wall renderer of 'Wall 3' (that wall's fade 0.00)
/// 'CR_OS_Skeleton_Statue_Skull'[mesh]  anchor 5.1 gap 0.00: already the wall renderer of 'Wall 6' (that wall's fade 1.00)
/// 'CR_OS_Skeleton_Statue_Body'[mesh]   anchor 3.5 gap 0.00: already the wall renderer of 'Wall 1' (that wall's fade 0.00)
/// 'CR_OS_Skeleton_Statue_Body'[mesh]   anchor 3.5 gap 0.00: already the wall renderer of 'Wall 6' (that wall's fade 0.00)
/// 'CR_OS_Skeleton_Statue_Broken'[mesh] anchor 3.6 gap 0.00: already the wall renderer of 'Wall 1' (that wall's fade 0.00)
/// </code>
/// <para>The skull's wall fades while the body's wall does not, so the statue is decapitated. The
/// parts are wall geometry — the session's floor planes are all at <c>sampY[0.05..0.05]</c> and
/// these anchors are 3.5 and 5.1 wu up, i.e. a statue built INTO the wall — so refusing the claim
/// outright would leave a solid skull inside a dissolved wall, which is a worse artefact and is
/// explicitly not what was asked for. The unit has to fade; it has to fade AS ONE.</para>
///
/// <para>WHY <c>WallSegmentFade.Standing.cs</c> (ModBuild 157) could not cure this case, and both
/// reasons are it working correctly: that rule needs figure/actor ancestry, and this instance
/// appears in no <c>FIGURE-GUARD</c> line of this session at all; and it needs the unit's foot
/// inside the ground band, while this one hangs 3.5 wu above the floor. Its census keeps printing
/// unchanged, and nothing below weakens it.</para>
///
/// <para>A CLASS, NOT A PROP. <c>'SB_AncCaverns_DemonHead'</c> stands in the same census lines
/// against the same walls; so do <c>'SB_AC_Arch_Top'</c> + <c>'SB_AC_Arch_Pillars'</c> (one
/// archway, two meshes) and <c>'CR_ST_WallShelf_Stone_Bone'</c> with its <c>_Bone</c> and
/// <c>_Skull</c> pieces. Any multi-part architectural feature whose parts land on different wall
/// units tears apart the same way, so the fix is stated over UNITS.</para>
///
/// <para>HOW A UNIT IS FOUND, and this is the part that had to be decided honestly from what the
/// scene actually offers rather than from the names in the log:</para>
/// <list type="number">
/// <item><b>The shared ancestor is the unit</b>, and it is the primary rule. The walk starts at
///   the renderer's parent and climbs while the subtree stays PROP-SIZED, keeping the HIGHEST
///   node that still qualifies — the closest thing to a prefab-instance root available at
///   runtime, and the same manoeuvre <see cref="FadeDriver.FindAssetRoot"/> already makes for the
///   Torbogen ruling. It is known to be the right shape here:
///   <c>WallSegmentFade.Standing.cs</c> read this very asset out of an earlier hardware log as
///   "FOUR sibling MeshRenderers under one root (<c>CR_OS_Skeleton_Statue</c> → <c>_Body</c>,
///   <c>_Skull</c>, <c>_Sword</c>, <c>_Broken</c>)".</item>
/// <item><b>A name stem is the fallback, and only with the geometry agreeing.</b> If a tileset
///   parents the pieces FLAT under the wall there is no shared ancestor to find, and then — and
///   only then — renderers under the SAME parent whose names share an underscore-delimited stem
///   are considered. That grouping is never accepted on the name alone: the group must also pass
///   the same size caps as a hierarchical unit, which is what stops <c>CR_OS_Wall_01_Main</c> +
///   <c>CR_OS_Wall_01_Skulls</c> (a wall course, tens of wu long) from being read as one prop.
///   The census says which of the two rules grouped each unit, so the next hardware log shows
///   whether the fallback ever fires at all.</item>
/// </list>
///
/// <para>TWO SIZE CAPS, for the catastrophe <c>WallSegmentFade.Standing.cs</c> names: if the walk
/// were allowed to reach a room container, every wall renderer under it would become one "unit"
/// with one owner and wall see-through would silently stop working. So a unit holds at most
/// <see cref="FadeDriver.PropUnitMaxRenderers"/> renderers and spans at most
/// <see cref="FadeDriver.PropUnitMaxSpanWU"/> wu horizontally, the walk stops at any node that IS
/// a segment anchor or CONTAINS a <c>ProceduralWall</c>, and it climbs at most
/// <see cref="FadeDriver.PropUnitMaxDepth"/> levels. The census prints each unit's renderer count
/// so a tileset sitting near a cap is visible immediately.</para>
///
/// <para>WHO OWNS THE UNIT is <see cref="WallPropUnit.ChooseOwner"/> — sticky while faded, then
/// majority, then nearest centroid, then ordinal key order. It lives in a Unity-free file because
/// its failure mode (an owner that flips every rescan → a statue that pops while neither wall
/// changes state) is invisible to everything in this repository and observable only by eye from
/// inside a headset. See that file's header, and
/// <c>tests/GloomhavenVR.WireTests/WallPropUnitVectors.cs</c>, which drives the exact case above.</para>
///
/// <para>A UNIT FADES WHOLE OR NOT AT ALL — ModBuild 258, and it is the same sentence read one
/// step further. Giving a torn prop ONE OWNER is not enough if a per-renderer rule then refuses
/// half of it: the ModBuild-257 hardware log reports <c>FADE WRITE: … grouped into 85 prop
/// unit(s), 49 of them TORN</c>, and 105 of those lines are one asset —
/// <c>TORN 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' 4/6 written … LEFT SOLID under the same
/// root: FR_Stones_06 (1), FR_Stones_02 (2)</c>. The wall mesh and its three Foliage attachments
/// dissolve; the two ground-hugging stones of the same prop stay fully drawn. That is
/// <c>wandproblem3.jpg</c>. The rule that held them is <see cref="FadeDriver.StripGroundRenderers"/>
/// and its 1.0 wu band, re-applied by <see cref="FadeDriver.PropUnitRecruit"/> — and one line away
/// in the same log the standing rule has already judged this exact root
/// <c>height 3.0 wu — architecture, not a floor prop</c>. Two rules, one prop, opposite answers.
/// The UNIT-level verdict wins: the band is per-renderer and cannot see what a piece belongs to.
/// See <see cref="FadeDriver.PropUnitRecruit"/> for why this cannot reach a floor tile.</para>
///
/// <para>WHAT THE PASS DOES with the answer: every renderer of the unit is removed from the
/// segments that are NOT the owner (clearing our property block off it, or it would carry a
/// stale fade forever — the <see cref="FadeDriver.StripGroundRenderers"/> discipline) and added to
/// the owner. A member that no segment claimed at all is offered to the owner through the ONE
/// choke point, <see cref="FadeDriver.CollectWallFadeInfo"/>, so the standing-prop guard, the
/// toggle-native accounting and the authored-cutoff pick all see it exactly as a normal
/// collection would.</para>
///
/// <para>EVERY MEMBER GETS A CHANNEL, OR THE UNIT IS REFUSED WHOLE — ModBuild 259, and it is the
/// user's ruling verbatim (2026-08-24, <c>neues_wandproblem.jpg</c>): <i>"Entweder verschwindet
/// die ganze Wand mit ALLEM was dazu gehört (Bäume, Gestrüp, etc.) oder sie ist vollständig da.
/// So ein Zwischending soll es nicht geben."</i></para>
///
/// <para>THE NUMBER THAT MADE THE RULE. ModBuild 258's own instrument earned this round: its
/// <c>PROP UNIT</c> line reads <c>37 recruited from no owner at all, 106 left visible</c>, and the
/// per-member term is identical for every one of the 106 —
/// <c>'FR_Floor_Detail_Grass_06_PR (2)': Foliage-family shader with no wall-fade channel — it can
/// only ride a fade through seg.Foliage, and nothing offered it there</c>. Half of ModBuild 258
/// worked (<c>37 member(s) recruited THROUGH the ground band</c>, so the band was never the term
/// that held them); the other half wrote fade 1.00 to renderers that had nothing to receive it
/// with. <c>'PCG_FR_Pillar_Tree_Trunk_02_PR'</c> alone is <c>20 renderer(s) … 2 recruited, 17 LEFT
/// VISIBLE</c>.</para>
///
/// <para>THE TWO ARMS. A member with a wall-fade channel joins <see cref="Segment.Renderers"/> as
/// before. A member WITHOUT one becomes <see cref="Segment.UnitDressing"/>: a
/// <see cref="MountedProp"/> record driven by <see cref="FadeDriver.ApplyUnitDressing"/> with the
/// discipline every other attachment class already uses — <see cref="FadeDriver.DriveProp"/> on
/// whatever channel the material actually has, <see cref="FadeDriver.EnsureDissolveChannel"/> for
/// a channel-less NON-foliage material, and a guaranteed <c>renderer.enabled = false</c> at the
/// held threshold. Foliage is never material-swapped (ModBuild 255 ruling: swapping a leaf card
/// onto the masonry fade shader in one frame IS the fade-out pop) — a channel-less leaf is
/// STAGGERED off its own identity hash, exactly as <c>ApplyFoliage</c> does it.</para>
///
/// <para>AND THE FALLBACK IS EXPLICIT. If any member is one this mod may not write — a FIGURE, a
/// water feature, or a unit the standing rule holds — the unit CANNOT fade whole, so it does not
/// fade at all: <see cref="FadeDriver.RefusePropUnit"/> pulls the unit's members back off the
/// owner's renderer and foliage lists and names the term in the census. A wall that stays is a
/// nuisance; a wall with holes is the report.</para>
///
/// <para>UNIT DRESSING IS DELIBERATELY NOT IN THE COVERAGE NUMERATOR
/// (<see cref="FadeDriver.RayHitsWallMesh"/> walks Renderers/Foliage/Siblings/Body/Stacked and not
/// this list). A piece recruited to keep a fade WHOLE must not be able to change the decision that
/// started the fade — that circularity is the ModBuild-257 tree defect wearing a new hat, and
/// ModBuild 258 has just re-based every coverage number on the playable-hex denominator. FALSIFIER
/// if this is wrong: a wall that fades with a hole where its dressing used to block rays.</para>
///
/// <para>THE FALSIFIER READS THE RENDERER. <c>ApplyUnitDressing</c> asks
/// <see cref="FadeDriver.IsActuallyDrawing"/> of every piece on a frame where the segment was
/// ALREADY held hidden — i.e. of a piece we disabled last frame and that is on screen again. That
/// count and the "left visible" count must both be ZERO in the next hardware log; if they are and
/// the picture still shows standing vegetation, the residue is not a prop unit and this rule is
/// not where to look. (ModBuild 252 shipped an instrument that watched the driver and reported a
/// single-frame switch as an animation; this one watches <c>renderer.enabled</c>.)</para>
///
/// <para>BOUNDS. The owner's decision AABB grows to enclose what it now controls; the losers' are
/// deliberately left alone. Their bounds already included these renderers this rescan, so leaving
/// them is a strict no-change against shipped behaviour, whereas shrinking them would move a wall's
/// coverage reading as a side effect of a prop changing hands — a fade decision must not turn on
/// where a statue lives. A loser left with NOTHING is switched boundless, which takes it out of
/// the decision and attachment passes until the next rescan rebuilds it; otherwise it would keep a
/// fade that covers no wall and could still drag dressing under with it.</para>
///
/// <para>MULTIPLAYER: purely local, and identical on every machine. Nothing here produces a
/// decision, a wire record or peer-visible state; each peer runs the same rules over the same
/// scene, and the last tie-break is a stable key precisely so two peers cannot disagree.</para>
///
/// <para>STEREO: this changes WHICH segment writes a renderer's fade, never HOW. One CPU-side
/// decision per rescan reaches both eyes identically; no per-eye term exists here to get wrong.
/// <c>.planning/wall-fade-stereo-rivalry.md</c> (parked) is about the masonry shader's
/// screen-radial discard, and nothing below touches <c>_Cutoff</c>, the dissolve, the occlusion
/// map or the vignette.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>
        /// PROP-UNIT DRESSING (ModBuild 259) — members of a prop unit this segment owns that have
        /// NO wall-fade channel of their own, delivered as <see cref="MountedProp"/> records so
        /// they dissolve with the unit instead of standing over it.
        ///
        /// <para>Kept per SEGMENT and not in the shared mounted ledger for the reason
        /// <see cref="SiblingProps"/> gives: these are owned STRUCTURALLY (by the prop unit the
        /// segment won), not by the geometric mounted sweep, and putting them in that ledger's
        /// ownership table would make the orphan guard release them every rescan. They ARE
        /// registered in <c>_mountedOwned</c> during the mounted pass so the sweep does not adopt
        /// them a second time and the orphan guard does not undo them — see
        /// <c>CollectWallMountedProps</c>.</para>
        /// </summary>
        public readonly List<MountedProp> UnitDressing = new();
        public readonly List<MountedProp> PrevUnitDressing = new();
        /// <summary>0 = restored/untouched, 1 = dissolving, 2 = hidden.</summary>
        public int UnitDressingState;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>Most renderers one prop unit may hold. Deliberately the same 24 as
        /// <see cref="StandingPropMaxRenderers"/>: that constant already encodes this project's
        /// "above this it is architecture, not a prop", and a second number would drift from it.
        /// The skeleton statue holds four; the report's revealed map tiles carry 30-520.</summary>
        private const int PropUnitMaxRenderers = 24;

        /// <summary>Largest horizontal extent a unit may span, matched to
        /// <see cref="StandingPropMaxSpanWU"/> for the same reason. The report's rooms measure
        /// 6.7 x 6.9 wu and up and its wall runs tens of wu, so nothing structural fits.</summary>
        private const float PropUnitMaxSpanWU = 6.0f;

        /// <summary>How far the shared-ancestor walk may climb, matched to
        /// <see cref="MaxAssetRootDepth"/>. Apparance nests a prop a level or two under the thing
        /// it decorates; four levels is slack, not licence.</summary>
        private const int PropUnitMaxDepth = 4;


        /// <summary>Scratch for <see cref="PropUnitRootOf"/> alone. Deliberately NOT
        /// <c>_subtreeScratch</c>: that list is iterated by the asset-sibling collection while it
        /// calls into this file family, and sharing scratch with something that walks it is how a
        /// list gets cleared underneath its own iteration.</summary>
        private readonly List<MeshRenderer> _propUnitWalkScratch = new(64);

        /// <summary>Renderers this pass actually moved to (or recruited for) their unit's owner
        /// this rescan — read by the FADE WRITE census so a written renderer can say that the
        /// prop-unit pass is what put it where it is.</summary>
        private readonly HashSet<Renderer> _propUnitTouched = new(64);

        /// <summary>Every renderer some segment holds this rescan, so a unit can tell a member
        /// that merely has no owner from one that has a different owner.</summary>
        private readonly HashSet<MeshRenderer> _propUnitClaimed = new(256);


        /// <summary>
        /// PERF S3 (2026-08-23) — THE THREE PER-NODE FACTS <see cref="PropUnitRootOf"/> ASKS,
        /// MEMOISED PER NODE FOR ONE COMMIT.
        ///
        /// <para>THE DEFECT. <see cref="PropUnitRootOf"/> climbs up to
        /// <see cref="PropUnitMaxDepth"/> (4) ancestors and asks each one three questions, two of
        /// which are FULL SUBTREE WALKS: <c>GetComponentsInChildren&lt;MeshRenderer&gt;</c> and
        /// <c>GetComponentInChildren&lt;ProceduralWall&gt;(includeInactive: true)</c>. The
        /// existing memos (<see cref="CommittedTable.PropUnitRootMemo"/> and <c>_standingRootMemo</c>) cache
        /// the ROOT by the renderer's own parent, so two renderers under two DIFFERENT parents
        /// that share a grandparent each walk that grandparent's whole subtree — and the walk
        /// only stops climbing AFTER the count comes back over the cap, so the most expensive
        /// walk of the climb is always taken. In the ModBuild 228 scene the commit runs this
        /// climb for every renderer under every cache wall (the standing-prop choke point in
        /// <c>CollectWallFadeInfo</c>) — the log's 810 claimed + the wall subtrees' non-fade
        /// meshes on top — and the shared upper nodes are map-tile content containers holding
        /// hundreds to thousands of renderers. That is the nested walk the ~100 µs-per-renderer
        /// commit cost is shaped like.</para>
        ///
        /// <para>WHY THE VERDICT CANNOT MOVE. Each entry caches the RESULT OF THE IDENTICAL CALL
        /// on the identical node — no predicate is reformulated, no early-out is invented (an
        /// early-out would be a different question, and <c>GetComponentsInChildren</c>'s
        /// inactive-subtree pruning rule is not something to re-implement from memory). The only
        /// claim being made is that the three facts are CONSTANT for the duration of one commit,
        /// and they are: the commit runs synchronously inside one frame, no game code runs inside
        /// it, and the class itself never calls <c>SetActive</c>, never re-parents and never adds
        /// or destroys a <c>ProceduralWall</c> — it writes <c>renderer.enabled</c>, material
        /// property blocks and its own segment table, none of which any of the three facts read
        /// (<c>GetComponentsInChildren(includeInactive: false)</c> filters on GameObject
        /// activeness, not on <c>Renderer.enabled</c>). This is the same argument
        /// <c>FigureAncestryMemo</c> makes, and the same one the two root memos above already
        /// rely on.</para>
        ///
        /// <para>LIFETIME. Cleared once per commit in <c>BeginStandingPropScope</c> — the
        /// FIRST scope of the rescan, opened before any wall is refreshed — and deliberately NOT
        /// re-cleared in <see cref="BeginPropUnitScope"/>: sharing them across the standing pass
        /// and the prop-unit pass is where most of the saving is, and unlike the root memos these
        /// facts do not depend on <see cref="CommittedTable.PropUnitAnchors"/> (which IS re-read between the
        /// two scopes, and is exactly why those two memos must stay separate). Transform keys
        /// therefore live no longer than the existing memos' do.</para>
        /// </summary>
        private readonly Dictionary<Transform, int> _nodeRendererCount = new(256);
        private readonly Dictionary<Transform, bool> _nodeIsWallEntity = new(256);
        private readonly Dictionary<Transform, bool> _nodeContainsWallEntity = new(256);

        /// <summary>True only between <c>BeginStandingPropScope</c> (the first scope of a
        /// commit) and <c>EndCommitPhases</c> (its <c>finally</c>). OUTSIDE that window — the
        /// WALL-PATH AUDIT reaches the same walk from the heartbeat, which runs after the commit
        /// has closed — every fact is taken live, so the constancy argument above only ever has
        /// to hold for the synchronous stretch it was made about.</summary>
        private bool _nodeFactsActive;

        /// <summary>Drop the per-node fact memos and open the window in which they may be read.
        /// Called from <c>BeginStandingPropScope</c> at the top of every commit.</summary>
        private void ClearNodeFactMemos()
        {
            _nodeRendererCount.Clear();
            _nodeIsWallEntity.Clear();
            _nodeContainsWallEntity.Clear();
            _nodeFactsActive = true;
        }

        /// <summary>Close the window and drop the transform keys. Called from
        /// <c>EndCommitPhases</c>.</summary>
        private void EndNodeFactMemos()
        {
            _nodeFactsActive = false;
            _nodeRendererCount.Clear();
            _nodeIsWallEntity.Clear();
            _nodeContainsWallEntity.Clear();
        }

        /// <summary><c>node.GetComponentsInChildren&lt;MeshRenderer&gt;(includeInactive: false)</c>
        /// .Count, memoised — see <see cref="_nodeRendererCount"/>.</summary>
        private int NodeRendererCount(Transform node)
        {
            if (_nodeFactsActive && _nodeRendererCount.TryGetValue(node, out int cached))
                return cached;
            _propUnitWalkScratch.Clear();
            node.GetComponentsInChildren(includeInactive: false, _propUnitWalkScratch);
            int count = _propUnitWalkScratch.Count;
            _propUnitWalkScratch.Clear();
            if (_nodeFactsActive)
                _nodeRendererCount[node] = count;
            return count;
        }

        /// <summary><c>node.GetComponent&lt;ProceduralWall&gt;() != null</c>, memoised.</summary>
        private bool NodeIsWallEntity(Transform node)
        {
            if (_nodeFactsActive && _nodeIsWallEntity.TryGetValue(node, out bool cached))
                return cached;
            bool verdict = node.GetComponent<ProceduralWall>() != null;
            if (_nodeFactsActive)
                _nodeIsWallEntity[node] = verdict;
            return verdict;
        }

        /// <summary><c>node.GetComponentInChildren&lt;ProceduralWall&gt;(includeInactive: true)
        /// != null</c>, memoised.</summary>
        private bool NodeContainsWallEntity(Transform node)
        {
            if (_nodeFactsActive && _nodeContainsWallEntity.TryGetValue(node, out bool cached))
                return cached;
            bool verdict = node.GetComponentInChildren<ProceduralWall>(includeInactive: true) != null;
            if (_nodeFactsActive)
                _nodeContainsWallEntity[node] = verdict;
            return verdict;
        }

        /// <summary>The rescan's units, by index; <see cref="_propUnitByRoot"/> and
        /// <see cref="_propUnitByStem"/> point into it.</summary>
        private readonly List<PropUnit> _propUnits = new(32);
        private readonly Dictionary<Transform, int> _propUnitByRoot = new(32);
        private readonly Dictionary<string, int> _propUnitByStem = new(32);
        private readonly List<PropUnit> _propUnitPool = new(32);

        /// <summary>Previous rescan's owner per unit id — the input to the STICKY rule. Keyed by
        /// the unit's POSITIONAL id string (never a Transform), so an Apparance rebuild at the
        /// same place is recognised as the same unit and nothing dangles.</summary>
        private readonly Dictionary<string, string> _propUnitOwnerLast = new(32);
        private readonly Dictionary<string, string> _propUnitOwnerNow = new(32);

        /// <summary>Scratch lists. Separate from <see cref="_subtreeScratch"/> because this pass
        /// calls <see cref="CollectWallFadeInfo"/>, and sharing scratch with a callee is how a
        /// list gets cleared underneath its own iteration.</summary>
        private readonly List<MeshRenderer> _propUnitScratch = new(64);
        private readonly List<WallPropUnit.Claim> _propUnitClaimScratch = new(8);
        private readonly List<Segment> _propUnitLosers = new(8);

        /// <summary>Census rows for the line at the end of the rescan (capped; the counters
        /// below are the full totals).</summary>
        private readonly List<string> _propUnitCensus = new(8);
        private int _propUnitRegrouped;
        private int _propUnitMoved;
        private int _propUnitRecruited;
        private int _propUnitUnfadeable;
        private int _propUnitCensusSig = -1;

        /// <summary>How many members the ModBuild-258 whole-unit rule pulled back THROUGH the
        /// ground band this rescan — the direct falsifier for the scrub wall's two stones (see
        /// <see cref="PropUnitRecruit"/>). Zero in the reported scene means the band was not what
        /// held them and the <see cref="_propUnitLeftVisible"/> list says what did.</summary>
        private int _propUnitGroundLifted;
        private readonly List<string> _propUnitGroundNames = new(8);

        /// <summary>Members the recruit could not drive, each with the TERM that refused it. The
        /// ModBuild-257 line collapsed all of these into one "no fade channel" count, which is
        /// unable to tell a tileset fact from one of our own rules — and that is exactly the
        /// question this round turns on. Since ModBuild 259 this list is the RESIDUE only: a
        /// channel-less member is dressed rather than abandoned, so a non-empty list means a
        /// member fell through BOTH arms and is the next thing to read.</summary>
        private readonly List<string> _propUnitLeftVisible = new(8);

        /// <summary>Members given a dissolve channel as <see cref="Segment.UnitDressing"/> this
        /// rescan — the ModBuild-259 arm. The 106 "left visible" of the ModBuild-258 log are this
        /// population, and this counter is what they became.</summary>
        private int _propUnitDressed;

        /// <summary>Units refused WHOLE because one member is something this mod may not write.
        /// The unit then stays entirely solid — see <see cref="RefusePropUnit"/>.</summary>
        private int _propUnitRefusedUnits;
        private readonly List<string> _propUnitRefused = new(8);

        /// <summary>Members skipped because the STANDING rule holds them as a floor prop of their
        /// own unit. Not a refusal (see the comment at the test), and a number that is expected to
        /// be non-zero in a scene full of floor grass — it is here so a leftover the eye finds can
        /// be checked against it rather than guessed at.</summary>
        private int _propUnitFloorSkipped;

        /// <summary>ModBuild 262: members skipped because the WATER rect holds them as part of the
        /// water feature's OWN unit. A skip, not a refusal — see the comment at the test. THE
        /// NUMBER THAT MADE IT A SKIP: 10 of 10 unit refusals in the whole ModBuild-261 hardware
        /// session were this arm, and they pulled 7-10 renderer(s) each back off 'Wall 4' on every
        /// rescan. It is here so the next log states the cost of the water ruling per RENDERER
        /// instead of per WALL.</summary>
        private int _propUnitWaterSkipped;

        /// <summary>MODBUILD 291: figure-armed members that did NOT refuse their unit because the
        /// wall generator built them — the ModBuild-266 provenance lift at the whole-unit refusal
        /// site (user, 2026-08-25, <c>sollte_faden.jpg</c>). Its own counter and its own roster,
        /// never folded into the refusal count beside it: three wrong diagnoses in this subsystem
        /// came from two populations sharing one number.
        ///
        /// <para>THE FALSIFIER FOR THE WHOLE CHANGE IS THIS ROSTER'S PATHS. Every row prints the
        /// bounded four-level ancestry of the member. A row whose path contains no <c>Wall</c>
        /// node — a hero, a monster, a summon, a floor hex, a <c>PCG_*_Clutter_Floor_*</c> member
        /// — means the window is not the discriminator and the lift must be WITHDRAWN, not
        /// retuned. The asset NAME is not the falsifier: this level parents
        /// <c>CV_Ice_Crystal_Form_02</c> both under <c>Generated Content/Full/</c> (protected floor
        /// formation, user ruling 2026-08-24) and under <c>Wall N/Generated Content/</c> (wall
        /// furniture the same user now rules must fade).</para></summary>
        private int _propUnitWallBuiltFigures;

        /// <summary>The names and ancestries behind that count — capped, and the count above is
        /// not.</summary>
        private readonly List<string> _propUnitWallBuiltNames = new(8);

        /// <summary>Renderers this segment's unit dressing had already hidden and that are DRAWING
        /// again on a later frame of the same held state — read off <c>renderer.enabled</c> and
        /// <c>activeInHierarchy</c> by <see cref="IsActuallyDrawing"/>, never off our ledger. This
        /// is the picture-side falsifier for the whole rule and it must read 0.</summary>
        private int _unitDressingRedrawn;
        private readonly List<string> _unitDressingRedrawnNames = new(8);

        /// <summary>Dressing pieces kept HIDDEN with their still-faded wall this rescan rather
        /// than being restored as leavers (ModBuild 265), and the ones conceded to another
        /// owner's dressing list on the same rescan. Both are reported: a stickiness rule that
        /// is never observed is a rule nobody can falsify.</summary>
        private int _unitDressingHeldFaded;
        private int _unitDressingHandedOver;
        private readonly List<string> _unitDressingHeldNames = new(8);

        /// <summary>Renderers currently delivered as prop-unit dressing by some segment — seeded
        /// into <c>_mountedOwned</c> by the mounted pass so one piece can never have two owners
        /// (the ModBuild-258 blue-flame class).</summary>
        private readonly HashSet<Renderer> _unitDressingOwned = new(64);

        /// <summary>Staging for the ModBuild-259 two-pass resolve: PASS 1 decides every member's
        /// channel and writes nothing, PASS 2 commits — because "whole or nothing" cannot be
        /// decided halfway through writing the unit.</summary>
        private readonly List<MeshRenderer> _unitStageWall = new(24);
        private readonly List<MeshRenderer> _unitStageDress = new(24);

        /// <summary>How many census rows the line carries. Six is what the standing-prop and
        /// mounted lines settled on: enough to name the offender, short of a wall of text.</summary>
        private const int PropUnitCensusCap = 6;

        /// <summary>How many distinct member names the two per-member lists carry.</summary>
        private const int PropUnitLeftVisibleCap = 8;

        /// <summary>One grouped prop: its members, and which segments hold them.</summary>
        private sealed class PropUnit
        {
            /// <summary>Positional id — "<c>label</c>@qx:qz" over the unit centroid, quantised the
            /// way <c>ComputeWireKeys</c> quantises anchors. Stable across rescans and machines.</summary>
            public string Id = "?";
            public string Label = "?";
            /// <summary>True when the name-stem fallback grouped this unit rather than a shared
            /// ancestor — printed, so the next hardware log says whether it ever fires.</summary>
            public bool ByStem;
            public readonly List<MeshRenderer> Members = new(8);
            public readonly List<Segment> ClaimSegs = new(4);
            public readonly List<int> ClaimCounts = new(4);

            public void Reset()
            {
                Id = "?";
                Label = "?";
                ByStem = false;
                Members.Clear();
                ClaimSegs.Clear();
                ClaimCounts.Clear();
            }

            public void AddClaim(Segment seg)
            {
                for (int i = 0; i < ClaimSegs.Count; i++)
                {
                    if (ReferenceEquals(ClaimSegs[i], seg))
                    {
                        ClaimCounts[i]++;
                        return;
                    }
                }
                ClaimSegs.Add(seg);
                ClaimCounts.Add(1);
            }
        }

        // ---- the pass ---------------------------------------------------------------------

        /// <summary>
        /// Regroup every multi-part prop whose renderers ended up on more than one wall unit (or
        /// only partly on one) and give each unit exactly ONE owner. Runs after the stacked-shell
        /// pass and BEFORE <see cref="CollectAdoptedSiblings"/> and
        /// <see cref="CollectWallMountedProps"/>, so those two see the corrected lists: the asset
        /// siblings attach to the wall that actually ends up owning their asset, and the mounted
        /// pass's ownership table — the very table whose census reported this defect — reports the
        /// truth afterwards.
        /// </summary>
        private void EnforcePropUnitCohesion()
        {
            BeginPropUnitScope();

            // PHASE 1 — who holds what. Every (segment, renderer) pair contributes one claim, so a
            // renderer that somehow sits in TWO segments' lists produces two claims and is healed
            // by the same arithmetic as a torn-apart prop.
            foreach (Segment seg in _live.Segments.Values)
            {
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null)
                        _propUnitClaimed.Add(r);
                }
            }
            foreach (Segment seg in _live.Segments.Values)
            {
                if (!SplitPieceMayClaim(seg))
                    continue;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r == null)
                        continue;
                    PropUnit? unit = UnitOf(r);
                    unit?.AddClaim(seg);
                }
            }

            // PHASE 2 — resolve every unit that is not already coherent.
            foreach (PropUnit unit in _propUnits)
            {
                int unclaimed = 0;
                foreach (MeshRenderer m in unit.Members)
                {
                    if (m != null && !_propUnitClaimed.Contains(m))
                        unclaimed++;
                }
                if (unit.ClaimSegs.Count <= 1 && unclaimed == 0)
                    continue; // one owner already, nothing hanging loose — the common case
                ResolvePropUnit(unit);
            }

            // ModBuild 259: restore any dressing a segment held last rescan and lost this one.
            FinishPropUnitDressing();

            // Carry this rescan's owners into the next one's STICKY input. Assigned wholesale
            // rather than merged: a unit that no longer exists must not keep voting.
            _propUnitOwnerLast.Clear();
            foreach (KeyValuePair<string, string> kv in _propUnitOwnerNow)
                _propUnitOwnerLast[kv.Key] = kv.Value;

            // ModBuild 261, and it must stay LAST in this method: both stagger-keyed lists are
            // final at this point, and every frame until the next commit reads the memo this
            // fills without being allowed to fill it itself. See WarmStaggerKeys.
            WarmStaggerKeys();
        }

        /// <summary>Open a fresh scope: drop last rescan's transform-keyed memos (they dangle),
        /// re-read the segment anchors the walk must stop at, and reset the census counters.</summary>
        private void BeginPropUnitScope()
        {
            foreach (PropUnit u in _propUnits)
            {
                u.Reset();
                _propUnitPool.Add(u);
            }
            _propUnits.Clear();
            _propUnitByRoot.Clear();
            _propUnitByStem.Clear();
            _live.PropUnitRootMemo.Clear();
            _propUnitClaimed.Clear();
            _propUnitOwnerNow.Clear();
            _propUnitCensus.Clear();
            _propUnitTouched.Clear();
            _propUnitGroundNames.Clear();
            _propUnitLeftVisible.Clear();
            _propUnitRefused.Clear();
            _propUnitRegrouped = 0;
            _propUnitMoved = 0;
            _propUnitRecruited = 0;
            _propUnitUnfadeable = 0;
            _propUnitGroundLifted = 0;
            _propUnitDressed = 0;
            _propUnitRefusedUnits = 0;
            _propUnitFloorSkipped = 0;
            _propUnitWaterSkipped = 0;
            _propUnitWallBuiltFigures = 0;
            _propUnitWallBuiltNames.Clear();
            // Reset per RESCAN, not per frame: the counter then reads "how many pieces came back
            // on screen over a hidden wall since the last commit" (~2 s of frames), which is a
            // number an outcome can be judged by. Zeroing it per frame would make a piece that
            // redraws every second frame read 0 half the time.
            _unitDressingRedrawn = 0;
            _unitDressingRedrawnNames.Clear();
            _unitDressingHeldFaded = 0;
            _unitDressingHandedOver = 0;
            _unitDressingHeldNames.Clear();

            // ModBuild 259: park every segment's dressing list the way CollectPlainWallBody parks
            // the body, so a piece that stops being dressing this rescan is RESTORED rather than
            // left hidden with no owner (the foliage-orphan lesson).
            _unitDressingOwned.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                seg.PrevUnitDressing.Clear();
                seg.PrevUnitDressing.AddRange(seg.UnitDressing);
                seg.UnitDressing.Clear();
            }

            RefreshPropUnitAnchors();

            // PERF S4 — ADOPT THE PREPARE STAGE'S ROOT PREWARM, BUT ONLY AGAINST THE SET IT WAS
            // DERIVED FROM. PropUnitRootOf's answer depends on _live.PropUnitAnchors (the climb stops
            // AT a segment anchor), and that set is re-read one line above from the FINAL table —
            // which is precisely why the memo is dropped wholesale each rescan. The prepare stage
            // cannot know the final table, so it derived its answers against the LAST COMMITTED
            // one and recorded that set in _prepAnchors. Equal sets mean equal answers, renderer
            // for renderer, and the adoption is then a pure cost saving. Unequal means a wall was
            // adopted or died during this cycle: drop the whole prewarm and let every climb be
            // taken live, which is exactly what happened on every cycle before PERF S4.
            //
            // The memo's LIFETIME is unchanged by this: it is still emptied here, once per
            // rescan, and still holds transform keys for no longer than one cycle.
            if (_propUnitRootPrewarm.Count > 0)
            {
                if (_live.PropUnitAnchors.SetEquals(_prepAnchors))
                {
                    foreach (KeyValuePair<Transform, Transform?> kv in _propUnitRootPrewarm)
                        _live.PropUnitRootMemo[kv.Key] = kv.Value;
                }
                else
                {
                    _cyclePrepDroppedAnchors++;
                }
                _propUnitRootPrewarm.Clear();
                _prepAnchors.Clear();
            }
        }

        /// <summary>
        /// Leavers pass for <see cref="Segment.UnitDressing"/>, run once at the end of the pass —
        /// and since ModBuild 265 a STICKY pass, because a leaver whose wall is still gone may
        /// not be put back.
        ///
        /// <para>WHAT IT USED TO DO AND WHY THAT WAS THE BUG. It called <c>RestoreProp</c> on
        /// anything a segment held last rescan and does not hold now, and <c>RestoreProp</c> ends
        /// with <c>r.enabled = true</c>. In the ModBuild-264 hardware session that fired 66 times
        /// with the owner's fade at 1.00 — the back wall's scrub, switched back on over masonry
        /// that is not drawn. The user's ruling is not "restore it as authored", it is
        /// <i>"das Gestrüp soll gar nicht mehr auftauchen, solange die Wand gefaded ist"</i>.</para>
        ///
        /// <para>THREE OUTCOMES, IN ORDER, and each answers what the one before it cannot:</para>
        /// <list type="number">
        /// <item>HANDOVER — <c>_unitDressingOwned</c> is this rescan's complete set of renderers
        ///   some unit owner has taken as dressing, and it is final at this point (every
        ///   <see cref="ResolvePropUnit"/> call has run). A renderer in it has a live owner whose
        ///   <see cref="ApplyUnitDressing"/> drives it on the same frame, in both directions.
        ///   Restoring it here would be a write war with that applier AND would drop the shared
        ///   <c>MountedProp</c> record out of <c>_mountedTouched</c> — the record the new owner is
        ///   already holding. Concede it, silently as far as the picture is concerned.</item>
        /// <item>STICKY WHILE FADED — the wall is still gone and nobody else took the piece, so
        ///   this segment keeps it. Re-added to <c>seg.UnitDressing</c>, so the segment's own
        ///   applier stays its driver and <c>RestoreSegmentUnitDressing</c> gives it back on the
        ///   un-fade edge (<c>want == 0</c>), which is the one place that knows the masonry is
        ///   solid again. The predicate is <see cref="SegmentStillHiding"/>, shared verbatim with
        ///   the mounted sweep's sticky loop — the enclosing <c>if</c> already asserts its lane
        ///   term, so what discriminates here is the wall's own fade.</item>
        /// <item>RESTORE — the backstop, and TODAY IT IS UNREACHABLE. The enclosing
        ///   <c>if (seg.UnitDressingState != 0)</c> already satisfies the first term of
        ///   <see cref="SegmentStillHiding"/>, so outcome 2 always wins. That is not an
        ///   oversight and it is not a latent hole: a piece carried while the wall is in fact
        ///   solid is back in <c>seg.UnitDressing</c>, and <see cref="ApplyUnitDressing"/> then
        ///   takes the <c>want == 0</c> branch on its very next run and restores the whole list
        ///   through <see cref="RestoreSegmentUnitDressing"/> — one frame later than a direct
        ///   restore, never later than that. The branch stays because it is the only correct
        ///   behaviour if that enclosing guard is ever widened; do not read a
        ///   <c>RestoreProp</c> here in a log, because none can be emitted from this method
        ///   while the guard stands.</item>
        /// </list>
        ///
        /// <para>NOTHING CAN STAY HIDDEN WITHOUT AN OWNER. A sticky-carried piece is in
        /// <c>seg.UnitDressing</c>, which <c>CollectWallMountedProps</c> reads into
        /// <c>_mountedOwned</c> at the top of the very next phase — so the orphan guard leaves it
        /// alone while the segment lives and restores it the moment the segment does not.</para>
        ///
        /// <para>AND IT CLOSES THIS LANE'S OWN BLIND SPOT. <c>ApplyUnitDressing</c>'s picture-side
        /// falsifier is gated on <c>alreadyHeld = seg.UnitDressingState == 2</c>, and the tail of
        /// this method zeroes that state whenever the list empties. Under the old code a split
        /// piece's whole dressing list emptied together on the oscillating rescan, so the probe
        /// was structurally unable to see the 66 — it printed 0 in every one of the 34 census
        /// lines of a session that had the defect. With the list no longer emptying mid-fade the
        /// probe is live again, and it is the falsifier for this method.</para>
        ///
        /// <para>MULTIPLAYER: presentation only. Every term is local scene state (a renderer, a
        /// segment fade, a list this process built); nothing here reads or writes the wire.</para>
        /// </summary>
        private void FinishPropUnitDressing()
        {
            foreach (Segment seg in _live.Segments.Values)
            {
                if (seg.UnitDressingState != 0)
                {
                    foreach (MountedProp prev in seg.PrevUnitDressing)
                    {
                        if (prev.Renderer == null || seg.UnitDressing.Contains(prev))
                            continue;
                        if (_unitDressingOwned.Contains(prev.Renderer))
                        {
                            _unitDressingHandedOver++;
                            continue;
                        }
                        // ONE DRIVER PER PIECE ON ONE SEGMENT — the rule
                        // IsSegmentDressedElsewhere states for the dressing side, applied to the
                        // sticky carry. A leaver that became this segment's own foliage / body /
                        // stacked piece / wall renderer this rescan already rides THIS wall's
                        // fade, so carrying it back into the dressing list would give it two
                        // appliers on the same frame (the foliage stagger wants it enabled while
                        // the dressing ramp wants it off — the flicker). Skip it: no restore, no
                        // carry, and it stays hidden with the same wall either way.
                        if (SegmentAlreadyDrives(seg, prev.Renderer))
                        {
                            _unitDressingHandedOver++;
                            continue;
                        }
                        if (SegmentStillHiding(seg, seg.UnitDressingState))
                        {
                            seg.UnitDressing.Add(prev);
                            _unitDressingOwned.Add(prev.Renderer);
                            _unitDressingHeldFaded++;
                            NoteUnitDressingHeld(prev.Renderer);
                            continue;
                        }
                        RestoreProp(prev, seg,
                            "prop-unit dressing — this unit no longer fades with this wall");
                    }
                    if (seg.UnitDressing.Count == 0)
                        seg.UnitDressingState = 0;
                }
                seg.PrevUnitDressing.Clear();
            }
        }

        /// <summary>Does this segment already drive this renderer through one of its OTHER
        /// lists? The <see cref="IsSegmentDressedElsewhere"/> question, asked of a
        /// <see cref="Renderer"/> rather than a <see cref="MeshRenderer"/> and widened to the
        /// wall's own renderer list, because the sticky carry in
        /// <see cref="FinishPropUnitDressing"/> has to answer it for a piece it did not just
        /// classify. Every list here is per-segment and short; this runs only for leavers of a
        /// segment that is still hiding, i.e. never on the steady-state path.</summary>
        private static bool SegmentAlreadyDrives(Segment seg, Renderer r)
        {
            foreach (MeshRenderer m in seg.Renderers)
            {
                if (ReferenceEquals(m, r))
                    return true;
            }
            foreach (MeshRenderer f in seg.Foliage)
            {
                if (ReferenceEquals(f, r))
                    return true;
            }
            foreach (MeshRenderer sib in seg.Siblings)
            {
                if (ReferenceEquals(sib, r))
                    return true;
            }
            foreach (MountedProp p in seg.Body)
            {
                if (ReferenceEquals(p.Renderer, r))
                    return true;
            }
            foreach (MountedProp p in seg.Stacked)
            {
                if (ReferenceEquals(p.Renderer, r))
                    return true;
            }
            // seg.Mounted is DELIBERATELY not consulted: CommitPhase.Mounted runs AFTER this
            // pass, so at this point that list still holds the PREVIOUS rescan's adoptions and
            // reading it would be reading stale ownership. It needs no cover anyway — the
            // mounted phase's first act is to register every seg.UnitDressing renderer into
            // _mountedOwned, so a piece carried here is spoken for before the sweep looks at it.
            return false;
        }

        /// <summary>Record a dressing piece this rescan kept hidden with its still-faded wall.
        /// Capped by name; the count is the full total.</summary>
        private void NoteUnitDressingHeld(Renderer r)
        {
            if (_unitDressingHeldNames.Count >= PropUnitLeftVisibleCap
                || _unitDressingHeldNames.Contains(r.name))
            {
                return;
            }
            _unitDressingHeldNames.Add(r.name);
        }

        /// <summary>Re-read the segment anchors the unit walk must stop at. Called from
        /// <c>BeginStandingPropScope</c> at the top of the rescan (the standing rule's
        /// FLOOR arm walks before any wall has been refreshed) and again from
        /// <see cref="BeginPropUnitScope"/> once the table is final. A wall adopted for the first
        /// time THIS rescan is therefore missing from the early set for one pass — and the size
        /// caps are what catch it: a unit that reached across a wall is either wider than
        /// <see cref="PropUnitMaxSpanWU"/> or taller than
        /// <see cref="WallStandingProp.MaxHeightWU"/>, and is refused on those numbers rather than
        /// on the table.</summary>
        private void RefreshPropUnitAnchors()
        {
            _live.PropUnitAnchors.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                if (seg.Anchor != null)
                    _live.PropUnitAnchors.Add(seg.Anchor.transform);
            }
        }

        /// <summary>The unit a claimed renderer belongs to, creating it on first sight. Null when
        /// the renderer is not part of a groupable multi-piece prop at all — which is the answer
        /// for the overwhelming majority of wall masonry, and the cheap path.</summary>
        private PropUnit? UnitOf(MeshRenderer r)
        {
            Transform? parent = r.transform.parent;
            if (parent == null)
                return null;

            // PRIMARY RULE — the shared ancestor.
            Transform? root = PropUnitRootMemoized(parent);
            if (root != null)
            {
                if (_propUnitByRoot.TryGetValue(root, out int existing))
                    return existing == UnitRefused ? null : _propUnits[existing];
                _propUnitScratch.Clear();
                root.GetComponentsInChildren(includeInactive: false, _propUnitScratch);
                PropUnit? made = MakeUnit(root.name, byStem: false, _propUnitScratch);
                // PERF E (ModBuild 279): the REFUSAL is memoised too — see UnitRefused.
                _propUnitByRoot[root] = made != null ? _propUnits.Count - 1 : UnitRefused;
                return made;
            }

            // FALLBACK — same parent, same name stem, and the geometry has to agree. Only reached
            // when there is no shared ancestor to find, i.e. when a tileset parented the pieces
            // flat under the thing they decorate.
            string? stem = NameStemOf(r.name);
            if (stem == null)
                return null;
            string stemKey = parent.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture)
                             + "|" + stem;
            if (_propUnitByStem.TryGetValue(stemKey, out int have))
                return have == UnitRefused ? null : _propUnits[have];
            _propUnitScratch.Clear();
            _subtreeScratch.Clear();
            parent.GetComponentsInChildren(includeInactive: false, _subtreeScratch);
            foreach (MeshRenderer sib in _subtreeScratch)
            {
                if (sib != null && string.Equals(NameStemOf(sib.name), stem, System.StringComparison.Ordinal))
                    _propUnitScratch.Add(sib);
            }
            _subtreeScratch.Clear();
            PropUnit? stemUnit = MakeUnit(stem + "_*", byStem: true, _propUnitScratch);
            // PERF E (ModBuild 279) — THE REFUSAL IS MEMOISED TOO. The note that stood here said
            // recomputing a refusal is "a handful of bounds reads on a path that is rare by
            // construction". The RARITY claim is about the ROOT case (a prop with no shared
            // ancestor); the COST claim is what is wrong. When a group IS refused — by the
            // 2-renderer floor, the PropUnitMaxRenderers ceiling or either size cap — nothing was
            // stored, so the NEXT renderer under the same parent with the same stem re-walked the
            // whole parent subtree and re-read NameStemOf(sib.name) for every renderer in it.
            // `Object.name` allocates a managed string per read, so a flat-parented masonry
            // parent holding K renderers paid O(K^2) name allocations per commit — and
            // flat-parented masonry is precisely the tileset shape this fallback exists for.
            _propUnitByStem[stemKey] = stemUnit != null ? _propUnits.Count - 1 : UnitRefused;
            return stemUnit;
        }

        /// <summary>PERF E (ModBuild 279) — the <see cref="_propUnitByRoot"/> /
        /// <see cref="_propUnitByStem"/> entry meaning "this group was examined this rescan and
        /// REFUSED", as opposed to "never examined".
        ///
        /// <para>WHY MEMOISING A REFUSAL IS NO WEAKER THAN MEMOISING A SUCCESS, which is the only
        /// question here. <see cref="MakeUnit"/>'s verdict is a function of the subtree membership
        /// and the members' live bounds. Both memos are cleared once per rescan
        /// (<c>BeginPropUnitScope</c>), and inside one commit nothing this subsystem does can move
        /// either: it writes <c>renderer.enabled</c> and property blocks, never
        /// <c>SetActive</c> (grep the file set — there is no SetActive in it), so the
        /// <c>includeInactive: false</c> walk returns the same members, and scenery does not move
        /// within a frame. The SUCCESS memo beside it has always made exactly this assumption and
        /// hands back a unit built from the first sighting's geometry; the refusal memo makes the
        /// same one and no more.</para></summary>
        private const int UnitRefused = -1;

        /// <summary>Build a unit from a candidate member list, or refuse it against the two size
        /// caps. A single-renderer group is refused outright: it cannot be torn apart, so it has
        /// nothing to gain and would only cost a dictionary entry per wall stone in the scene.</summary>
        private PropUnit? MakeUnit(string label, bool byStem, List<MeshRenderer> candidates)
        {
            if (candidates.Count < 2 || candidates.Count > PropUnitMaxRenderers)
                return null;
            Bounds union = default;
            bool have = false;
            int kept = 0;
            foreach (MeshRenderer m in candidates)
            {
                if (m == null || IsModObject(m))
                    continue;
                kept++;
                if (!have) { union = m.bounds; have = true; }
                else union.Encapsulate(m.bounds);
            }
            if (!have || kept < 2)
                return null;
            if (Mathf.Max(union.size.x, union.size.z) > PropUnitMaxSpanWU)
                return null;

            PropUnit unit;
            if (_propUnitPool.Count > 0)
            {
                unit = _propUnitPool[_propUnitPool.Count - 1];
                _propUnitPool.RemoveAt(_propUnitPool.Count - 1);
                unit.Reset();
            }
            else
            {
                unit = new PropUnit();
            }
            unit.Label = label;
            unit.ByStem = byStem;
            foreach (MeshRenderer m in candidates)
            {
                if (m != null && !IsModObject(m))
                    unit.Members.Add(m);
            }
            int qx = Mathf.RoundToInt(union.center.x * 2f);
            int qz = Mathf.RoundToInt(union.center.z * 2f);
            unit.Id = $"{label}@{qx}:{qz}";
            _propUnits.Add(unit);
            return unit;
        }

        /// <summary>
        /// The unit root of a renderer's parent: the HIGHEST ancestor (starting at that parent)
        /// that is still prop-sized. The walk stops dead at a segment anchor, at anything carrying
        /// or containing a <c>ProceduralWall</c>, and at the first container-scale subtree — the
        /// three ways a wall could otherwise be swallowed whole into a "prop".
        /// </summary>
        private Transform? PropUnitRootOf(Transform parent)
        {
            Transform? node = parent;
            Transform? best = null;
            for (int depth = 0; node != null && depth < PropUnitMaxDepth; depth++)
            {
                // PERF S3: the three questions below are the SAME three calls this walk always
                // made, answered out of a per-commit per-node memo — see _nodeRendererCount for
                // why a node's answer cannot change inside one commit, and why the existing
                // per-PARENT root memos do not already cover this (they re-walk every shared
                // ancestor once per distinct parent, and the priciest walk of the climb is the
                // one that decides to stop).
                if (_live.PropUnitAnchors.Contains(node) || NodeIsWallEntity(node))
                    break;
                int count = NodeRendererCount(node);
                if (count > PropUnitMaxRenderers)
                    break; // container scale — this node and everything above it are architecture
                if (count < 2)
                {
                    node = node.parent; // a node wrapping one renderer groups nothing; keep climbing
                    continue;
                }
                if (NodeContainsWallEntity(node))
                    break; // the subtree contains a wall entity: not a prop, whatever its size
                best = node;
                node = node.parent;
            }
            return best;
        }

        /// <summary>
        /// MODBUILD 268 - IS THERE A WALL IN THIS UNIT'S OWN WINDOW? The bounded provenance scan
        /// the standing rule's FLOOR arm consults, and the correction of the ModBuild-267 miss.
        ///
        /// <para><b>SAME NODES, SAME BOUND, NO EARLY EXIT.</b> It walks the identical sequence
        /// <see cref="PropUnitRootOf"/> walks - the renderer's PARENT and up - for the identical
        /// <see cref="PropUnitMaxDepth"/> levels, and asks the identical three memoised questions.
        /// The one difference is that it does not stop when the ROOT has been decided: deciding
        /// where a unit ends and asking what is above it are two questions, and ModBuild 267
        /// shipped the first one's answer to the second one's caller.</para>
        ///
        /// <para><b>THE BOUND IS THE SAFETY PROPERTY AND IT IS NOW THE ONLY ONE.</b> Four levels,
        /// never more. <c>GetComponentInParent&lt;ProceduralWall&gt;()</c> - what
        /// <c>IsWallGeneratedMember</c> and <c>WallGeneratorAncestry</c> both ask - climbs to the
        /// scene root, and in this session that reaches far enough to answer YES for a rigged
        /// skeleton's thighs, a light shaft and 60 ice-crystal renderers the user has ruled may
        /// stay. The ice formation's logged path is
        /// <c>'L : (...)/Generated Content/Full/PCG_CV_Ice_Clutter_Floor_07_PR/CV_Ice_Crystal_Form_02 (2)/...'</c>:
        /// four non-wall nodes sit above the renderer's parent before <c>L :</c> is reached, and
        /// <c>Full</c> and <c>Generated Content</c> are SIBLINGS of <c>Walls/</c>, not children of
        /// it. Widen this bound and that separation is gone. Do not widen it to "fix" a subject
        /// that does not fire; if a subject needs more than four levels it is not in a wall's own
        /// dressing, and this is the wrong rule for it.</para>
        ///
        /// <para>COST: at most four node visits, each answered from the per-commit memos
        /// <see cref="NodeIsWallEntity"/> / <see cref="NodeContainsWallEntity"/> already fill for
        /// this very walk, and the result is memoised per renderer PARENT by the caller. Rescan
        /// cadence, no scene sweep, nothing held across frames.</para>
        /// </summary>
        private bool WallInUnitWindow(Transform parent)
        {
            Transform? node = parent;
            for (int depth = 0; node != null && depth < PropUnitMaxDepth; depth++)
            {
                if (_live.PropUnitAnchors.Contains(node) || NodeIsWallEntity(node)
                    || NodeContainsWallEntity(node))
                {
                    return true;
                }
                node = node.parent;
            }
            return false;
        }

        /// <summary>
        /// MODBUILD 291 — IS THIS FIGURE-ARMED MEMBER A PIECE OF WALL FURNITURE THE WALL GENERATOR
        /// BUILT? The term that lifts the whole-unit FIGURE refusal, and the only place in this
        /// subsystem where that refusal may be lifted at all.
        ///
        /// <para><b>WHY IT IS THE BOUNDED WINDOW AND NOT <c>IsWallGeneratedDressing</c>.</b> That
        /// predicate is the ModBuild-266 lift the other four sites use, and it would be the
        /// obvious thing to reuse here. It cannot be reused here, and the reason is written down in
        /// <see cref="WallInUnitWindow"/>: its provenance term is
        /// <c>GetComponentInParent&lt;ProceduralWall&gt;()</c>, an UNBOUNDED climb to the scene
        /// root, and in this very tileset that climb answers YES for the floor crystal formation
        /// the user rules must STAY (<c>Generated Content/Full/PCG_CV_Ice_Clutter_Floor_0N_PR/
        /// CV_Ice_Crystal_Form_02 (…)</c>), for a light shaft and for a rigged skeleton's thighs.
        /// At the four ModBuild-266 sites the unbounded climb is safe because a second, geometric
        /// term stands beside it; HERE it would be the only term, and the thing it would release
        /// is a whole unit. So the provenance question is asked with the four-level window instead
        /// — the same walk, the same memos and the same bound the standing rule's own wall terms
        /// use, which for the protected formation answers NO because <c>Full</c> and
        /// <c>Generated Content</c> are SIBLINGS of <c>Walls/</c>, not children of it.</para>
        ///
        /// <para><b>THE ROUND-7 RULING IS NOT RELAXED</b> (figures are NEVER touched, Lights-rule
        /// severity). The <c>ActorBehaviour</c> / <c>CInteractableActor</c> chain stays an ABSOLUTE
        /// VETO, character for character as <c>IsWallGeneratedDressing</c> writes it, and
        /// provenance is added ON TOP of it. The <c>Animator</c> arm — which is what this tileset's
        /// crystals, and only this tileset's crystals, trip — is the one this term is allowed to
        /// out-vote, and only inside a wall's own four-level window. <c>Choreographer</c> parents
        /// every figure it spawns to the BOARD root and nowhere else (three spawn paths, all three
        /// checked), so a hero, a monster or a summon is on no wall's ancestor chain and cannot
        /// reach this line at all.</para>
        ///
        /// <para><paramref name="why"/> is the BLOCKER-NAMING instrument this round exists for.
        /// It names WHICH arm of <see cref="IsFigureOrActorRenderer"/> fired, on WHICH GameObject,
        /// and the member's bounded ancestry — for the release as well as for the refusal. The
        /// ModBuild-290 line said <c>'CV_Ice_Crystal_Form_04': FIGURE</c> and stopped there, which
        /// is a predicate's return value, not a cause; a reader could not tell an <c>Animator</c>
        /// on a crystal from an <c>ActorBehaviour</c> on a monster, and those two demand opposite
        /// treatment.</para>
        /// </summary>
        private bool IsWallBuiltUnitMember(Renderer r, out string why)
        {
            Transform t = r.transform;
            string arm = DescribeFigureArm(r);
            Transform? parent = t.parent;
            string path = parent != null ? UnitWindowPath(parent) : "<no parent>";
            if (IsModObject(r))
            {
                why = $"{arm}; a MOD-OWNED visual, never scenery @ {path}";
                return false;
            }
            // THE ABSOLUTE VETO, FIRST. Verbatim from IsWallGeneratedDressing, and deliberately
            // NOT routed through the figure memo: that memo bundles Animator in with the two actor
            // components, and Animator is exactly the term this predicate exists to stop deciding
            // on its own.
            if (r.GetComponentInParent<ActorBehaviour>() != null
                || r.GetComponentInParent<CInteractableActor>() != null)
            {
                why = $"{arm}; an ActorBehaviour / CInteractableActor sits above it — ABSOLUTE "
                      + $"VETO, round-7 ruling, never lifted @ {path}";
                return false;
            }
            if (parent == null || !WallInUnitWindow(parent))
            {
                why = $"{arm}; NO wall inside the member's own bounded {PropUnitMaxDepth}-level "
                      + $"window, so the wall generator is not what built it (an unbounded "
                      + $"ProceduralWall climb is NOT substituted: it answers YES for the floor "
                      + $"crystal formation the user rules must stay) @ {path}";
                return false;
            }
            why = $"{arm}, but the WALL GENERATOR built it: a wall is inside the member's own "
                  + $"bounded {PropUnitMaxDepth}-level window and no actor component sits above "
                  + $"it, so it is wall furniture and fades with its wall (user 2026-08-25, "
                  + $"sollte_faden.jpg) @ {path}";
            return true;
        }

        /// <summary>MODBUILD 291 — WHICH ARM OF <see cref="IsFigureOrActorRenderer"/> FIRED, and on
        /// which GameObject. Four arms answer one bool, and this round turned entirely on which of
        /// them it was: an <c>Animator</c> on a crystal is wall furniture, an <c>ActorBehaviour</c>
        /// on the same renderer would be a figure. Built only for a row a census actually keeps
        /// (one refusal per unit, capped rosters), so the three ancestor walks it costs are a
        /// handful per rescan and never a sweep.</summary>
        private static string DescribeFigureArm(Renderer r)
        {
            if (r is SkinnedMeshRenderer)
                return "FIGURE arm: SkinnedMeshRenderer (the renderer's own type)";
            var actor = r.GetComponentInParent<ActorBehaviour>();
            if (actor != null)
                return $"FIGURE arm: ActorBehaviour on '{actor.gameObject.name}'";
            var interactable = r.GetComponentInParent<CInteractableActor>();
            if (interactable != null)
                return $"FIGURE arm: CInteractableActor on '{interactable.gameObject.name}'";
            var animator = r.GetComponentInParent<Animator>();
            if (animator != null)
                return $"FIGURE arm: Animator on '{animator.gameObject.name}'";
            // Not reachable from the call site (the caller has already had `true` from the
            // predicate), and stated rather than asserted: a held instrument that can only print
            // one sentence reads as a dead one.
            return "FIGURE arm: none of the four arms answers now — the predicate's memo and this "
                   + "walk disagree, which is itself the finding";
        }

        /// <summary>MODBUILD 268 - the renderer's ancestry as the window sees it, at most
        /// <see cref="PropUnitMaxDepth"/> levels, for the standing rule's subject roll-call. The
        /// ModBuild-267 round was lost partly because no log row said where the failing shelf was
        /// PARENTED: the instance that fades prints its path in the PROP UNIT census, and the
        /// instance that does not is refused before any census sees it, so its path has never once
        /// been in a log. Cheap (a bounded string join over four names) and built only for the
        /// handful of rows the roll-call keeps.</summary>
        private string UnitWindowPath(Transform parent)
        {
            var sb = new System.Text.StringBuilder();
            Transform? node = parent;
            for (int depth = 0; node != null && depth < PropUnitMaxDepth; depth++)
            {
                if (sb.Length > 0)
                    sb.Insert(0, '/');
                sb.Insert(0, node.name);
                node = node.parent;
            }
            return sb.ToString();
        }

        /// <summary>The unit root of a parent, out of (and into) the per-rescan memo. The ONLY
        /// writer of <see cref="CommittedTable.PropUnitRootMemo"/>: everything that reads the memo without
        /// filling it — <see cref="FadeDriver.StaggerRootOf"/>, which runs per frame and may not
        /// walk — depends on this having been called for that parent during the commit. See
        /// <see cref="WarmStaggerKeys"/>.</summary>
        private Transform? PropUnitRootMemoized(Transform parent)
        {
            if (!_live.PropUnitRootMemo.TryGetValue(parent, out Transform? root))
            {
                root = PropUnitRootOf(parent);
                _live.PropUnitRootMemo[parent] = root;
            }
            return root;
        }

        /// <summary>
        /// MODBUILD 261 — MAKE THE MEMO COMPLETE FOR EVERYTHING THE APPLIERS WILL ASK ABOUT.
        ///
        /// <para>THE DEFECT THIS CLOSES. Two appliers stagger channel-less leafy pieces off a
        /// prop-unit key: <c>ApplyFoliage</c> over <c>seg.Foliage</c> and
        /// <see cref="ApplyUnitDressing"/> over <see cref="Segment.UnitDressing"/>. Both run every
        /// frame, so neither may resolve the root itself (<see cref="PropUnitRootOf"/> is a
        /// hierarchy climb with two subtree walks per level — the per-frame scene-walk defect this
        /// subsystem has shipped three times). They therefore read
        /// <see cref="CommittedTable.PropUnitRootMemo"/> read-only, and a parent MISSING from it makes them fall
        /// back to the renderer's own id. Before this pass the memo held only the parents
        /// <see cref="UnitOf"/> happened to touch — i.e. parents of renderers in
        /// <c>seg.Renderers</c> — so a conifer whose needle cards sit in <c>seg.Foliage</c> under a
        /// DIFFERENT parent from its dressed twigs got a renderer key on one half and a root key
        /// on the other, and tore mid-ramp. Whether it did depended on rescan order, which is why
        /// it was intermittent.</para>
        ///
        /// <para>WHERE, AND WHY EXACTLY HERE. At the tail of
        /// <see cref="EnforcePropUnitCohesion"/>: <c>seg.UnitDressing</c> is built by this very
        /// pass and final only now, <c>seg.Foliage</c> was final at the Ground2 phase and is only
        /// ever SHRUNK afterwards (this pass moves members out of it; the Siblings and Mounted
        /// phases that follow read it and never add), and the per-node fact memos
        /// (<c>_nodeRendererCount</c> and friends) are still open, so each fresh climb answers out
        /// of the same cache the rest of the commit already paid for. The memo is cleared once per
        /// rescan in <see cref="BeginPropUnitScope"/> and nothing clears it in between, so one
        /// warm here covers every frame until the next commit.</para>
        ///
        /// <para>COST: one dictionary lookup per stagger-keyed renderer per RESCAN (~2 s), and a
        /// climb only for parents no earlier phase resolved. It removes work from the per-frame
        /// path rather than adding any.</para>
        ///
        /// <para>FALSIFIER: <c>NoteStaggerKeyMiss</c> counts every applier lookup that still finds
        /// nothing, and the SHOW EDGE line prints the count with an <c>[ALARM]</c>. A future
        /// applier that stagger-keys a THIRD list will make it non-zero on the next run rather
        /// than tear silently.</para>
        /// </summary>
        private void WarmStaggerKeys()
        {
            foreach (Segment seg in _live.Segments.Values)
            {
                foreach (MeshRenderer f in seg.Foliage)
                {
                    if (f != null && f.transform.parent != null)
                        PropUnitRootMemoized(f.transform.parent);
                }
                foreach (MountedProp p in seg.UnitDressing)
                {
                    Renderer r = p.Renderer;
                    if (r != null && r.transform.parent != null)
                        PropUnitRootMemoized(r.transform.parent);
                }
            }
        }

        /// <summary>
        /// A segment that IS one renderer, keyed by that renderer — the shape both
        /// <see cref="RefreshSplitWall"/> (a cache wall whose combined AABB proved too fat) and
        /// <see cref="NeutralizeEngulfingSegments"/> (a segment that swallows its own room's floor
        /// samples) deliberately produce. Those splits exist BECAUSE the pieces must decide
        /// separately, so this pass steps around them entirely: it neither reads their claims nor
        /// takes renderers off them. Merging two of them back into one owner would undo the jungle
        /// -floor and engulfing-wall fixes, which is a bigger regression than the defect being
        /// cured — and it is not the reported case anyway, whose census names whole walls
        /// ('Wall 2', 'Wall 3', 'Wall 6') and not renderer-named pieces. ACCEPTED LIMITATION: a
        /// prop torn between a split piece and a normal wall is left alone, and the census
        /// therefore stays silent about it rather than reporting a fix it did not make.
        /// </summary>
        private static bool IsPerRendererSplit(Segment seg) => seg.Anchor is Renderer;

        /// <summary>
        /// MODBUILD 262 — A SPLIT PIECE THAT IS DRIVEN BY A RUN MAY OWN ITS OWN PROP UNIT.
        ///
        /// <para>THE DEFECT, out of the ModBuild-261 log and not out of reasoning. The user's
        /// report is <i>"Das Gestrüp an der hinteren Wand ist immer noch nicht weg — das soll
        /// vollständig alles mit-weg-faden"</i> (mauerproblem_erneut2.jpg). That log's FADE WRITE
        /// census names the shape exactly:
        /// <c>TORN 'PCG_FR_Wall_Grassy_Verge_01_PR' 3/5 written … ← wall renderer[split segment] of
        /// 'CR_FR_Wall_Grassy_Verge_01' fade 1.00 … — LEFT SOLID under the same root:
        /// CR_FR_Wall_Grassy_Verge_Grass_01, CR_FR_Wall_Grassy_Verge_Plants_01</c>, and
        /// <c>TORN 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' 4/6 written … — LEFT SOLID under
        /// the same root: FR_Stones_06 (1), FR_Stones_02 (2)</c>. Every owner in those rows is a
        /// <c>[split segment]</c>, and this pass — the one pass whose whole job is "one unit, one
        /// owner, every member gets a channel" — skipped ALL of them by construction. That is the
        /// ACCEPTED LIMITATION written above, and it is what the photograph shows: the wall
        /// generator's own scrub standing in front of a run at fade 1.00.</para>
        ///
        /// <para>WHY IT IS SAFE NOW AND WAS NOT BEFORE. The comment above refuses split pieces
        /// because <i>"those splits exist BECAUSE the pieces must decide separately"</i> — merging
        /// two of them under one owner would undo the jungle-floor and engulfing-wall fixes. That
        /// is still true for DECIDING, and nothing here changes who decides: a run member's fade
        /// comes from its RUN (<see cref="Segment.RunOwner"/>, ModBuild 261's SplitRunUnified), so
        /// pieces of one run already carry one fade and a prop shared between two of them cannot
        /// tear whatever this pass does. Two further guards keep the decision untouched:</para>
        /// <list type="number">
        /// <item>only a piece with a LIVE run anchor qualifies (an orphan keeps its own decision,
        ///   fail-open, and is exactly the case that must stay separate);</item>
        /// <item>the loop at <c>ReferenceEquals(seg, owner) || IsPerRendererSplit(seg)</c> is
        ///   UNCHANGED, so no renderer is ever taken OFF a split piece — recruitment can only add
        ///   members that no segment held;</item>
        /// <item>and a split owner's decision AABB is not grown by what it recruits (see the
        ///   bounds loop in <see cref="ResolvePropUnit"/>). Growing it is precisely how the
        ///   engulfing fix would be undone, and it is also the PASSENGER discipline this subsystem
        ///   already runs on: recruited scenery takes the run's verdict and never steers one.</item>
        /// </list>
        ///
        /// <para>FALSIFIED BY: a SPLIT RUN / RUN FADE union or best-single number that moves on the
        /// same scenario (the decision changed, which this must not do), or a new
        /// <c>!ENGULF</c>/boundless entry naming a split piece.</para>
        /// </summary>
        private bool SplitPieceMayClaim(Segment seg)
            => !IsPerRendererSplit(seg)
               || (seg.FromSplitRun && seg.RunOwner != null
                   && _live.SplitAnchors.Contains(seg.RunOwner));

        /// <summary>The underscore-delimited stem of a piece name
        /// (<c>CR_OS_Skeleton_Statue_Skull</c> → <c>CR_OS_Skeleton_Statue</c>), or null when the
        /// name carries no stem worth grouping by. Never sufficient on its own — see the file
        /// header and the size caps in <see cref="MakeUnit"/>.</summary>
        private static string? NameStemOf(string name)
        {
            int cut = name.LastIndexOf('_');
            return cut >= 4 ? name.Substring(0, cut) : null;
        }

        // ---- resolution -------------------------------------------------------------------

        /// <summary>Give one torn-apart unit a single owner and move every member onto it.</summary>
        private void ResolvePropUnit(PropUnit unit)
        {
            _propUnitClaimScratch.Clear();
            for (int i = 0; i < unit.ClaimSegs.Count; i++)
            {
                Segment seg = unit.ClaimSegs[i];
                _propUnitClaimScratch.Add(new WallPropUnit.Claim(
                    ClaimKeyOf(seg),
                    unit.ClaimCounts[i],
                    seg.HasBounds ? UnitCentroidGapXZ(seg, unit) : float.MaxValue,
                    seg.Fade > 0f));
            }
            _propUnitOwnerLast.TryGetValue(unit.Id, out string? sticky);
            // PERF E (ModBuild 279): the rule SENTENCE is built only while the census that
            // prints it can still take a row. NotePropUnitCensus is its one and only consumer
            // and its first statement is that same cap test; the list only ever grows, and the
            // only site that grows it is that method, which runs once at the END of this one —
            // so a cap that is not reached here cannot be reached before the row is written.
            int pick = WallPropUnit.ChooseOwner(_propUnitClaimScratch, sticky, out string rule,
                                                _propUnitCensus.Count < PropUnitCensusCap);
            if (pick == WallPropUnit.NoOwner)
                return;
            Segment owner = unit.ClaimSegs[pick];
            // Recorded whether or not anything moves, so the STICKY rule still knows this unit's
            // owner next rescan — an owner that is only remembered on the rescans that changed
            // something is an owner that forgets itself the moment it settles.
            _propUnitOwnerNow[unit.Id] = _propUnitClaimScratch[pick].Key;

            // ---- PASS 1 (ModBuild 259) — VERDICT ONLY, NOT ONE WRITE ----------------------
            // "Whole or nothing" cannot be decided halfway through writing the unit, so every
            // member's channel is settled before anything moves. Nothing below this point touches
            // a renderer, a property block or a segment list.
            _unitStageWall.Clear();
            _unitStageDress.Clear();
            string? refusedBy = null;
            foreach (MeshRenderer m in unit.Members)
            {
                if (m == null || IsModObject(m))
                    continue;                       // ours; never part of the user's picture
                if (owner.Renderers.Contains(m))
                    continue;                       // already on the wall's own fade channel
                // THE PICTURE, NOT THE LEDGER: a renderer that is not drawing cannot leave a hole,
                // so it must never be the reason a whole unit is refused. Read off the renderer
                // (enabled + activeInHierarchy), the way the LEFTOVER audit reads it.
                //
                // …EXCEPT ONE **WE** HOLD HIDDEN — MODBUILD 265, AND A CLAIM MUST NOT MEASURE
                // ITSELF. `_mountedTouched` is the shared ledger of every prop this subsystem is
                // currently driving, and `ApplyUnitDressing` ends its `want == 2` branch with
                // `r.enabled = false`. So one rescan after a dressing piece is hidden, the test
                // above reads back OUR OWN WRITE, drops the member from both staging lists, and
                // `FinishPropUnitDressing` then restores it as a leaver — re-enabling it over a
                // wall at fade 1.00. The rescan after that it is drawing again, gets re-dressed
                // and is hidden again: the two-rescan oscillation of the user's report,
                // 2026-08-24, verbatim — "An der hinteren Wand das Gestrüp verschwindet erst,
                // ploppt dann aber plötzlich wieder auf wenn man ein wenig die Perspektive
                // ändert und ploppt eventuell wieder weg."
                //
                // The exception is written the way the mounted sweep already writes it ("A
                // renderer the GAME disabled is not ours to manage — except one WE hold hidden"),
                // and it is the same predicate, not a second one.
                //
                // THE NUMBER THAT MADE THIS CHANGE: 66 of the ModBuild-264 session's 76 RELEASED
                // OVER A FADED WALL warns, every one of them
                // "prop-unit dressing — this unit no longer fades with this wall" at fade 1.00,
                // every subject a scrub piece (FR_Floor_PlantsBushes_*, FR_Floor_Detail_*,
                // FR_Floor_LargeBush_*), 0 in the ModBuild-258 session. THE NUMBER THAT WOULD
                // FALSIFY IT: a non-zero "dressing piece(s) were DRAWING over a wall this pass
                // had already hidden" in the PROP UNIT line — that probe is a picture-side read
                // and cannot be satisfied by this ledger term.
                if (!IsActuallyDrawing(m) && !_mountedTouched.ContainsKey(m))
                    continue;
                if (IsSegmentDressedElsewhere(owner, m))
                    continue;                       // this wall already drives it another way
                // THE WATER RULING IS A SKIP AND NOT A REFUSAL — MODBUILD 262, and it is the
                // author of ModBuild 259's OWN falsifier firing: "REFUSED WHOLE above zero in the
                // next hardware log, with a wall visibly standing." The next hardware log
                // (ModBuild 261) reads "10 unit(s) refused this rescan" on EVERY rescan that has
                // any, all ten by this arm, and mauerproblem_erneut2.jpg is the right-hand wall
                // standing. The units and the cost, from that log:
                //   'PCG_FR_Wall_Space_01_PR'         (13 renderers, owner 'Wall 4')  9 pulled back
                //   'PCG_FR_Pillar_Tree_Trunk_03b_PR' (20)                           10 pulled back
                //   'PCG_FR_Pillar_Tree_Trunk_01_PR'  (17) x3                       7-8 each
                //   'PCG_FR_Pillar_Tree_Trunk_02_PR'  (20)                            7 pulled back
                //   'PCG_FR_Wall_Space_02_PR'         (12) x2                          9 each
                // and NOT ONE of the named members is water. They are
                // 'CR_FR_Wall_Rocky_Verge_Bushes_02 (1)', 'FR_Floor_LargeBush_04 (1)',
                // 'FR_Floor_LargeBush_06 (1)', 'FR_Floor_Detail_Grass_05_PR (1)',
                // 'CR_RU_Vines (3)' and 'FR_Floor_PlantsBushes_01 (5)' — bank vegetation the pond
                // rect legitimately covers (WATER FEATURE: 'FR_SW_Pond_Medium (2)' top 1.1,
                // '(1)' top 0.4; rect ceiling = water top + 1.0). The user's report against that
                // build is "Die rechte Wand faded garnicht mehr richtig", and this arm is why.
                //
                // WHY A SKIP IS THE RULING AND NOT A WEAKENING OF IT. The water rect is ITSELF a
                // whole-unit protection — of a DIFFERENT unit: the pond, its basin, bank, rim and
                // its own emitters, held solid together by geometry (WallSegmentFade.Water.cs). A
                // piece inside the rect therefore already HAS an owner with a verdict, exactly as
                // a floor prop does, so the two verdicts are not in conflict — which is word for
                // word the argument the STANDING rule below is a skip on. The water feature stays
                // whole and solid: nothing here writes a renderer the rect covers, and
                // StripGroundRenderers has already taken every such piece off the wall's own
                // renderer/foliage/body lists with our block cleared. What changes is that a wall
                // stops staying whole and solid WITH it.
                //
                // AND THE REFUSAL WAS THE ONLY PATH ON WHICH A WATER PIECE COST MORE THAN ITSELF:
                // PropUnitRecruit — the choke point PASS 2 offers every unclaimed member through —
                // has always refused a water-protected renderer PER RENDERER and named it in LEFT
                // VISIBLE. This arm is now the same rule at the same granularity.
                //
                // FALSIFIED BY: a FADE WRITE row for a renderer IsWaterProtected returns true for,
                // or a WATER FEATURE census whose pond/basin/rim is not solid. Either means the
                // skip leaked and this goes back to being a refusal.
                if (IsWaterProtected(m.bounds))
                {
                    _propUnitWaterSkipped++;
                    NotePropUnitLeftVisible(m, "the WATER rect holds it as part of the water "
                        + "feature's own unit (user ruling 2026-08-09, brunnen.png) — a wall may "
                        + "not claim it, and this is a skip rather than a refusal for the reason "
                        + "the standing rule below gives");
                    continue;
                }
                // A FIGURE STILL REFUSES THE WHOLE UNIT (round-7 ruling, Lights-rule severity):
                // that arm is about a thing this mod may not touch AT ALL, not about a thing with
                // another owner. It fired ZERO times in the whole ModBuild-261 session, so none of
                // this round's evidence bears on it and it is left bit-for-bit alone.
                //
                // …EXCEPT FOR A PIECE THE WALL GENERATOR BUILT — MODBUILD 291, AND THIS IS THE
                // FIFTH SITE OF THE ModBuild-266 LIFT, THE ONE IT NEVER REACHED.
                //
                // THE REPORT (user, 2026-08-25, sollte_faden.jpg): "In der Map fandet ein Wandteil
                // nicht … sondern das was wirklich als Wand vor den Figuren zu sehen ist mit dem
                // Gestein daneben. Das sollte wie jedes andere Element auch faden."
                //
                // THE ModBuild-290 LOG NAMES THE BLOCKER OUTRIGHT, and it is this line: "5 unit(s)
                // refused this rescan — 'PCG_CV_Ice_Feature_Medium_02_PR' (22 renderer(s), owner
                // 'Wall 1' @fade 0.00) STAYS WHOLE AND SOLID — 'CV_Ice_Crystal_Form_04': FIGURE
                // (never touched — round-7 ruling, Lights-rule severity); 16 renderer(s) pulled
                // back off that wall", twice for that feature (Wall 1 and Wall 3) and three more
                // times for 'PCG_CV_Ice_Bay_Small_01_PR' on 'CV_Ice_Crystal_Form_02'. All five
                // refusals in the session are this arm and all five name a crystal that the wall
                // generator itself parented under Wall N/Generated Content/. The same log's
                // standing census RELEASES those very asset families as "[WALL-SECTION] … the game
                // uses this AS a wall section" — but that ModBuild-275 release lives inside
                // IsStandingProp, and THIS test is asked FIRST and asks the raw predicate, so the
                // remedy could never reach the site that was actually holding the wall. "A rule
                // read too late never runs", one gate earlier than the last four times.
                //
                // THE TERM IS THE BOUNDED WINDOW AND NOT IsWallGeneratedDressing, and the
                // difference is the whole safety argument — see IsWallBuiltUnitMember. In one
                // sentence: IsWallGeneratedDressing climbs with GetComponentInParent<ProceduralWall>()
                // to the scene root, which in THIS scene answers YES for the floor crystal
                // formation the user rules must STAY; the four-level window does not.
                //
                // THE ROUND-7 RULING IS NOT RELAXED. IsWallBuiltUnitMember carries the
                // ActorBehaviour / CInteractableActor chain as an ABSOLUTE VETO, exactly as
                // IsWallGeneratedDressing does, and adds provenance ON TOP of it.
                if (IsFigureOrActorRenderer(m))
                {
                    // NAME THE BLOCKER, NOT THE COUNT. Six rounds were once spent tuning a
                    // coverage FRACTION on this subsystem and one field naming WHICH renderer
                    // blocked the ray ended it. "FIGURE" alone says a predicate returned true; it
                    // does not say WHICH of its four arms, on WHICH GameObject, or where the piece
                    // is parented — and those three facts are the whole adjudication here.
                    bool wallBuilt = IsWallBuiltUnitMember(m, out string memberWhy);
                    if (!wallBuilt)
                    {
                        refusedBy = $"'{m.name}': FIGURE (never touched — round-7 ruling, "
                                    + $"Lights-rule severity) via {memberWhy}";
                        break;
                    }
                    _propUnitWallBuiltFigures++;
                    if (_propUnitWallBuiltNames.Count < PropUnitLeftVisibleCap)
                        _propUnitWallBuiltNames.Add($"'{m.name}' — {memberWhy}");
                    // Falls through: the member is wall furniture and is staged like any other.
                }
                // THE STANDING RULE IS A SKIP AND NOT A REFUSAL, deliberately, and the precedent
                // is CollectWallFadeInfo: the one choke point every wall-renderer collection goes
                // through treats a standing prop as a renderer it does not take, not as a reason
                // to stop the wall. The rule is itself a WHOLE-UNIT verdict — about a different
                // unit, the floor prop's own — so the two verdicts are not in conflict, and
                // ModBuild 167's Gestrüpp-Wand ruling ("Die anderen 'gestrüpp-wände' versperren
                // mir nun auch manchmal die Sicht. Das darf niemals passieren.") is what a wall
                // claiming it would hand back. Counted and named, never silent.
                if (IsStandingFigureProp(m))
                {
                    _propUnitFloorSkipped++;
                    NotePropUnitLeftVisible(m, "the standing rule holds it as a FLOOR PROP of its "
                        + "own unit (WallSegmentFade.Standing.cs) — a wall may not claim it, and "
                        + "this is a skip rather than a refusal for the reason CollectWallFadeInfo "
                        + "gives");
                    continue;
                }
                if (RendererHasWallFadeChannel(m))
                    _unitStageWall.Add(m);
                else
                    _unitStageDress.Add(m);         // ModBuild 259: dressed, not abandoned
            }
            if (refusedBy != null)
            {
                RefusePropUnit(unit, owner, refusedBy);
                return;
            }

            // ---- PASS 2 — COMMIT ----------------------------------------------------------
            int moved = 0, recruited = 0, dressed = 0, unfadeable = 0;
            _propUnitLosers.Clear();
            foreach (MeshRenderer m in unit.Members)
            {
                if (m == null)
                    continue;
                // Take it off every segment that is not the owner. The block MUST be cleared here:
                // FinishRefresh only clears leavers during a refresh, and this is not one, so a
                // renderer dropped from a currently-faded segment would keep that fade forever —
                // the exact restitution StripGroundRenderers performs for the ground band.
                foreach (Segment seg in _live.Segments.Values)
                {
                    if (ReferenceEquals(seg, owner) || IsPerRendererSplit(seg))
                        continue;
                    int at = seg.Renderers.IndexOf(m);
                    if (at < 0)
                        continue;
                    if (seg.HasBlock)
                        m.SetPropertyBlock(null);
                    seg.Renderers.RemoveAt(at);
                    moved++;
                    _propUnitTouched.Add(m); // so the FADE WRITE census can attribute it
                    if (!_propUnitLosers.Contains(seg))
                        _propUnitLosers.Add(seg);
                }
            }
            foreach (MeshRenderer m in _unitStageWall)
            {
                if (m == null || owner.Renderers.Contains(m))
                    continue;
                // A member no segment held: offer it through the one choke point, so the
                // standing-prop guard, the ground/water rules below and the toggle-native
                // accounting all see it exactly as a normal collection would.
                if (PropUnitRecruit(owner, m))
                {
                    recruited++;
                    _propUnitTouched.Add(m);
                    continue;
                }
                // PASS 1 said this member had a channel and the choke point disagrees — the only
                // way that happens is an Apparance material stream landing between the two passes.
                // Dress it rather than abandon it; the unit still fades whole.
                if (DressUnitMember(owner, m))
                {
                    dressed++;
                    _propUnitTouched.Add(m);
                }
                else
                {
                    unfadeable++;
                }
            }
            foreach (MeshRenderer m in _unitStageDress)
            {
                if (m == null)
                    continue;
                if (DressUnitMember(owner, m))
                {
                    dressed++;
                    _propUnitTouched.Add(m);
                }
                else
                {
                    unfadeable++;
                    NotePropUnitLeftVisible(m, "no wall-fade channel AND no dressing record could "
                        + "be built for it — the one shape ModBuild 259 does not cover");
                }
            }

            // The owner now controls geometry it did not before; its decision AABB has to enclose
            // it. Losers keep theirs (see the file header) — except one left with nothing at all,
            // which goes boundless so the decision and attachment passes skip it until the next
            // rescan rebuilds it.
            //
            // MODBUILD 262 — EXCEPT A PER-RENDERER SPLIT PIECE, whose AABB is not a renderer union
            // but the DECISION BOX the jungle-floor and engulfing fixes carved it down to. Growing
            // that box by what the piece recruits is exactly how those fixes get undone, so a run
            // member takes its passengers WITHOUT them ever entering its own decision — the same
            // discipline Segment.RunPassenger already states ("no cells into the union"). Its fade
            // comes from the run either way. See SplitPieceMayClaim.
            if (!IsPerRendererSplit(owner))
            {
                foreach (MeshRenderer m in unit.Members)
                {
                    if (m == null || !owner.Renderers.Contains(m))
                        continue;
                    if (!owner.HasBounds) { owner.Bounds = m.bounds; owner.HasBounds = true; }
                    else owner.Bounds.Encapsulate(m.bounds);
                }
            }
            foreach (Segment loser in _propUnitLosers)
            {
                if (loser.Renderers.Count > 0 || loser.Body.Count > 0 || loser.IsGateColumn)
                    continue;
                RestoreSegmentFoliage(loser);
                RestoreSegmentSiblings(loser);
                RestoreSegmentMounted(loser);
                RestoreSegmentStacked(loser);
                loser.HasBlock = false;
                loser.HasBounds = false;
            }
            _propUnitLosers.Clear();

            // Counted and reported only when the pass ACTUALLY did something. A unit whose single
            // claimant already owns everything it can own is not a fixed defect, and putting it in
            // the census would bury the one line that matters under every mixed-material asset in
            // the scene — the ModBuild-164 failure mode wearing the opposite hat.
            if (moved == 0 && recruited == 0 && dressed == 0)
                return;
            _propUnitRegrouped++;
            _propUnitMoved += moved;
            _propUnitRecruited += recruited;
            _propUnitDressed += dressed;
            _propUnitUnfadeable += unfadeable;
            NotePropUnitCensus(unit, owner, rule, moved, recruited, dressed, unfadeable);
        }

        /// <summary>
        /// REFUSE THE WHOLE UNIT (ModBuild 259, the user's ruling: <i>"Entweder verschwindet die
        /// ganze Wand mit ALLEM … oder sie ist vollständig da. So ein Zwischending soll es nicht
        /// geben."</i>). One member cannot be written, so none of them is: every member is pulled
        /// back off the owner's renderer list AND off its foliage list, with our property block
        /// cleared, so the unit reads exactly as the flat game draws it.
        ///
        /// <para>WHY THIS IS THE SAFER FAILURE and not a hedge. A unit left half-written is a
        /// prop with a hole in it, which is what every hardware round since ModBuild 255 has been
        /// about; a unit left whole is a piece of scenery that did not disappear. He ranked those
        /// two himself. The census names the unit AND the term, so a refusal that is actually a
        /// missing channel gets fixed as a channel rather than lived with.</para>
        ///
        /// <para>FALSIFIER: <c>REFUSED WHOLE</c> above zero in the next hardware log, with a wall
        /// visibly standing. Then the named member is the thing to give a channel to — not this
        /// rule to weaken.</para>
        /// </summary>
        private void RefusePropUnit(PropUnit unit, Segment owner, string why)
        {
            int pulled = 0;
            foreach (MeshRenderer m in unit.Members)
            {
                if (m == null)
                    continue;
                int at = owner.Renderers.IndexOf(m);
                if (at >= 0)
                {
                    if (owner.HasBlock)
                        m.SetPropertyBlock(null);
                    owner.Renderers.RemoveAt(at);
                    _propUnitClaimed.Remove(m);
                    pulled++;
                }
                // …and off the foliage list, or the leaf half of the unit would still dissolve
                // while its trunk stayed. Same restitution StripGroundRenderers performs.
                int fi = owner.Foliage.IndexOf(m);
                if (fi < 0)
                    continue;
                if (owner.FoliageProps.TryGetValue(m, out MountedProp? fp))
                {
                    owner.FoliageProps.Remove(m);
                    RestorePropSwap(fp, m);
                }
                if (owner.FoliageState != 0)
                    RestoreFoliageRenderer(m);
                owner.Foliage.RemoveAt(fi);
                pulled++;
            }
            _propUnitRefusedUnits++;
            if (_propUnitRefused.Count >= PropUnitLeftVisibleCap)
                return;
            string ownerName = owner.Anchor != null ? owner.Anchor.name : "<dead>";
            _propUnitRefused.Add(
                $"'{unit.Label}' ({unit.Members.Count} renderer(s), owner '{ownerName}' @fade "
                + $"{owner.Fade:0.00}) STAYS WHOLE AND SOLID — {why}; {pulled} renderer(s) pulled "
                + "back off that wall");
        }

        /// <summary>
        /// Give a channel-less unit member a DISSOLVE RECORD on its owner, so it fades with the
        /// unit instead of standing over it. The record is reused from the shared prop ledger when
        /// we are already driving this renderer, which is what keeps
        /// <see cref="EnsureDissolveChannel"/> from rebuilding a swap material every rescan:
        /// <c>MountedProp.SwapChecked</c> latches on the record, and the record survives for as
        /// long as the piece is being driven.
        /// </summary>
        private bool DressUnitMember(Segment owner, MeshRenderer m)
        {
            if (m == null)
                return false;
            // ONE OWNER, ALWAYS. A renderer two units both reach (Apparance nests these subtrees)
            // must not land in two dressing lists: two owners with different fades is the
            // one-prop-two-owners class the ModBuild-258 blue flame belonged to.
            if (!_unitDressingOwned.Add(m))
                return true;
            if (!_mountedTouched.TryGetValue(m, out MountedProp? p))
                p = ClassifyProp(m);
            owner.UnitDressing.Add(p);
            return true;
        }

        /// <summary>Does any shared material carry a wall-fade channel — the SAME two tests
        /// <see cref="CollectWallFadeInfo"/> makes, with none of its side effects. It exists
        /// because PASS 1 has to know the answer before it is allowed to write anything, and the
        /// choke point mutates the segment (toggle-native count, authored cutoff, variant flags,
        /// swap-template donation) as it decides.</summary>
        private bool RendererHasWallFadeChannel(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null || m.shader == null)
                    continue;
                // PERF E (ModBuild 279) — THE CACHED VERDICT, NOT A FRESH INTEROP NAME READ.
                // `Shader.name` allocates a managed string on every read and this loop runs per
                // material per member of every prop unit, every commit. FadeNameOf caches
                // `name.Contains("WallFade")` per Shader object under the name ByName — the
                // IDENTICAL expression on the IDENTICAL object, not an equivalent (see
                // ShaderFadeName's ctor). PERF S4 built that cache for CollectWallFadeInfo and
                // this site and one in WallSegmentFade.Standing.cs were missed.
                if (FadeNameOf(m.shader).ByName || HasLiveWallFadeToggle(m))
                    return true;
            }
            return false;
        }

        /// <summary>Is this owner already driving the renderer through one of its OTHER lists? A
        /// piece with two drivers on the same wall is a piece that flickers between them — the
        /// foliage stagger wants it enabled on the same frame the dressing ramp wants it off.
        /// Only the OWNER's lists are consulted: a piece held by a different segment is the
        /// one-prop-two-owners class, which the mounted pass's unit affinity settles.</summary>
        private static bool IsSegmentDressedElsewhere(Segment owner, MeshRenderer m)
        {
            if (owner.Foliage.Contains(m) || owner.Siblings.Contains(m))
                return true;
            // Stacked and Body are both final by now — CommitPhase.Stacked runs before
            // CommitPhase.PropUnits, and a body is collected during the wall-cache refresh.
            foreach (MountedProp p in owner.Stacked)
            {
                if (ReferenceEquals(p.Renderer, m))
                    return true;
            }
            foreach (MountedProp p in owner.Body)
            {
                if (ReferenceEquals(p.Renderer, m))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Offer an unclaimed member to the winning segment. False when the renderer has no fade
        /// channel at all, or is water-protected — and each of those is a fact the census reports
        /// rather than hides.
        ///
        /// <para>THE GROUND BAND GETS NO VOTE HERE, and that is the ModBuild-258 fix
        /// (<c>wandproblem3.jpg</c>, verbatim: <i>"ein Teil der Wand bleibt nun stehen und faded
        /// garnicht mehr"</i>). Until this build the method re-applied
        /// <c>GroundExclusionHeightWU</c> to every candidate, which is what the ModBuild-257 log
        /// is 105 lines of:
        /// <c>TORN 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' 4/6 written … LEFT SOLID under the
        /// same root: FR_Stones_06 (1), FR_Stones_02 (2)</c> — the wall mesh and its three foliage
        /// attachments dissolve, the two ground-hugging stones of the SAME prop stay fully drawn.
        /// Ranked over that whole session the pieces left solid are <c>FR_Stones_06 (1)</c> ×105,
        /// <c>FR_Stones_02 (2)</c> ×105, <c>FR_Floor_Detail_Grass_05_PR (1)</c> ×28,
        /// <c>FR_Floor_LargeBush_02 (1)</c> ×22 — all of them the BASE of a prop whose top is
        /// gone.</para>
        ///
        /// <para>WHY THE UNIT WINS AND THE BAND LOSES, from the same log rather than from taste.
        /// The band is a PER-RENDERER rule that cannot see what a piece belongs to; the standing
        /// rule is a PER-UNIT rule that has already answered the same question for this exact
        /// root, and its answer is printed one line away:
        /// <c>NEAR MISS … 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' height 3.0 wu —
        /// architecture, not a floor prop (cap 2.5 wu)</c>. The mod has decided this unit is wall.
        /// A wall's bottom metre is wall. Two rules disagreeing about one prop is exactly what
        /// TORN measures, and the whole-unit verdict is the one that can be right.</para>
        ///
        /// <para>WHY THIS CANNOT EAT THE FLOOR — the defect <see cref="StripGroundRenderers"/> was
        /// written for, where fading a wall took its room-edge ground hexes with it. This method
        /// is only ever reached for a MEMBER of a <see cref="PropUnit"/>, i.e. for a renderer
        /// under a root that <see cref="PropUnitRootOf"/> produced, and that walk stops dead at a
        /// segment anchor, at any <c>ProceduralWall</c>, and at the first container-scale subtree.
        /// This tileset parents a floor tile as
        /// <c>Wall N/Generated Content/PCG_CR_Floor_BaseHex_Plain/EN_CR_Floor_BaseHex_Plain</c> —
        /// a ONE-renderer wrapper under a container-scale node — so it yields no unit and can
        /// never be a member. The floor assets that DO group into a unit are protected by the
        /// standing rule's FLOOR arm as WHOLE units (<c>'PCG_FR_Floor_Grass_Hex_Split_PR' … height
        /// 0.5 wu, 2 renderer(s) — floor prop</c>, ModBuild 257), which means no member of theirs
        /// is ever claimed by a wall, which means the unit never gets an owner and this method is
        /// never called for it. The lift therefore reaches exactly one population: members of a
        /// unit the FLOOR arm has already called architecture. See
        /// <see cref="WallStandingProp.UnitFadesAsOne"/>.</para>
        ///
        /// <para>THE FALSIFIER. The <c>FADE WRITE</c> census's TORN count for these units must go
        /// to zero. If <c>'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR'</c> still reads <c>4/6</c>
        /// in the next hardware log, the ground band was not what held the stones and the PROP
        /// UNIT line's new <c>left visible</c> breakdown says which term did instead — this method
        /// now records the refusing term per member rather than one aggregate count.</para>
        /// </summary>
        private bool PropUnitRecruit(Segment owner, MeshRenderer m)
        {
            if (IsModObject(m) || !m.enabled)
                return false;
            // WATER stays out (user ruling 2026-08-09, brunnen.png): a fountain's basin and its
            // water plane are a feature that never fades, and unlike the ground band that is a
            // ruling about the OBJECT, not a band the object happens to sit in.
            if (IsWaterProtected(m.bounds))
            {
                NotePropUnitLeftVisible(m, "water feature (user ruling 2026-08-09) — never fades");
                return false;
            }
            // The one choke point. It re-asks the standing rule (so a member of a PROTECTED unit
            // is refused here exactly as it would be anywhere else) and then asks whether the
            // renderer has a fade channel at all. A renderer with no channel is a fact about the
            // tileset, reported rather than papered over.
            if (!CollectWallFadeInfo(m, owner))
            {
                NotePropUnitLeftVisible(m, RendererUsesFoliage(m)
                    ? "Foliage-family shader with no wall-fade channel — it can only ride a fade "
                      + "through seg.Foliage, and nothing offered it there"
                    : "no wall-fade channel on any of its materials (neither the WallFade shader "
                      + "family nor a live _WallFade_On toggle)");
                return false;
            }
            if (RoomDecisionValid(owner.RoomIndex) && owner.RoomIndex < _live.RoomFloorY.Count
                && m.bounds.max.y <= _live.RoomFloorY[owner.RoomIndex] + GroundExclusionHeightWU)
            {
                // Recruited THROUGH the ground band — the ModBuild-258 lift. Counted separately
                // so the next log states how many pieces the rule actually recovered, and from
                // which units.
                _propUnitGroundLifted++;
                NotePropUnitGroundLift(m);
            }
            owner.Renderers.Add(m);
            _propUnitClaimed.Add(m);
            return true;
        }

        /// <summary>Record a member the recruit could not drive, WITH the term that refused it.
        /// The ModBuild-257 census counted these into one <c>left visible (no fade channel)</c>
        /// number, which cannot distinguish "the tileset gave it no channel" from "a rule of ours
        /// held it back" — and that distinction is the whole question this round turns on.</summary>
        private void NotePropUnitLeftVisible(Renderer m, string why)
        {
            if (_propUnitLeftVisible.Count >= PropUnitLeftVisibleCap)
                return;
            string entry = $"'{m.name}': {why}";
            if (!_propUnitLeftVisible.Contains(entry))
                _propUnitLeftVisible.Add(entry);
        }

        /// <summary>Record a member the whole-unit rule pulled back through the ground band —
        /// the direct falsifier for the scrub wall's two stones.</summary>
        private void NotePropUnitGroundLift(Renderer m)
        {
            if (_propUnitGroundNames.Count >= PropUnitLeftVisibleCap)
                return;
            if (!_propUnitGroundNames.Contains(m.name))
                _propUnitGroundNames.Add(m.name);
        }

        // ---- delivery (ModBuild 259) --------------------------------------------------------

        /// <summary>Restore ALL of a segment's prop-unit dressing — called from
        /// <see cref="RestoreSegmentBody"/> and <see cref="RestoreSegmentStacked"/>, which between
        /// them cover every path a segment leaves the table on (unfade, drop, group split,
        /// toggle-off, teardown), so no recruited piece can stay hidden without an owner.</summary>
        private void RestoreSegmentUnitDressing(Segment seg)
        {
            if (seg.UnitDressingState == 0)
                return;
            seg.UnitDressingState = 0;
            foreach (MountedProp p in seg.UnitDressing)
                RestoreProp(p, seg, seg.Fade > 0f ? "segment dropped mid-fade" : "wall solid again");
        }

        /// <summary>
        /// Drive the segment's prop-unit dressing alongside its fade — the ModBuild-259 arm that
        /// turns the ModBuild-258 log's <c>106 left visible</c> into pieces that actually go.
        ///
        /// <para>TWO DISCIPLINES, and which one applies is decided by the shader family, not by
        /// taste. A NON-foliage channel-less material gets <see cref="EnsureDissolveChannel"/> —
        /// the same swap the gate's masonry courses and the wall body already ride. A FOLIAGE
        /// material never does: ModBuild 255 established that replacing a leaf card's alpha-cutout
        /// shader with the masonry fade shader in one frame IS the fade-out "Ploppen" the user
        /// reported, and that fix is accepted. A leaf with a channel of its own ramps on it; a
        /// leaf without one is STAGGERED off its own identity hash, exactly as
        /// <c>ApplyFoliage</c> does it, and is disabled outright at the held threshold.</para>
        ///
        /// <para>THE FALSIFIER IS ON THE RENDERER. Before this frame writes anything, a piece that
        /// the segment had ALREADY hidden (state 2) is asked <see cref="IsActuallyDrawing"/> —
        /// <c>enabled</c> and <c>activeInHierarchy</c>, read off the renderer. A non-zero count
        /// means something re-enabled a piece over a fully faded wall, which is the defect this
        /// whole rule exists for, and the PROP UNIT line names it.</para>
        ///
        /// <para>COST. One <c>enabled</c> compare per piece per frame in the held steady state,
        /// which is what every other attachment applier costs. The swap is built ONCE per piece
        /// (<c>MountedProp.SwapChecked</c> latches on a record that is reused out of
        /// <c>_mountedTouched</c> across rescans) and only for non-foliage members — in the
        /// ModBuild-258 scene every one of the 106 is Foliage-family, so the measured swap count
        /// for this arm in that scene is ZERO. Ceiling: one <see cref="Material"/> per non-foliage
        /// dressed renderer, bounded by <see cref="PropUnitMaxRenderers"/> (24) per unit and
        /// destroyed by <see cref="RestorePropSwap"/> on every restore path.</para>
        /// </summary>
        private void ApplyUnitDressing(Segment seg)
        {
            if (seg.UnitDressing.Count == 0)
            {
                if (seg.UnitDressingState != 0)
                    RestoreSegmentUnitDressing(seg);
                return;
            }
            int segWant = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            // MODBUILD 271 — THE UNION RULE. Same entry point, same argument as ApplyMounted: the
            // early-out is a whole-SEGMENT decision, and the reported defect is a piece whose own
            // wall is the one that has NOT faded. See FadeDriver._mountedUnion.
            if (segWant == 0 && !LaneHasUnionFade(seg.UnitDressing, seg))
            {
                RestoreSegmentUnitDressing(seg);
                return;
            }
            bool alreadyHeld = seg.UnitDressingState == 2;
            bool lost = false;
            int highest = 0;
            int raisedBefore = _unionRaisedPropFrames;
            foreach (MountedProp p in seg.UnitDressing)
            {
                Renderer r = p.Renderer;
                if (r == null)
                {
                    lost = true;
                    continue;
                }
                // The fade this PIECE reads — its owner's, or that of any fade-eligible segment
                // its own AABB reaches into, whichever is higher. Nothing here writes a segment.
                float eff = UnionFade(r, seg);
                int want = eff >= FoliageHideFade ? 2 : eff > 0f ? 1 : 0;
                if (want == 0)
                {
                    // Neither this piece's wall nor anything it overlaps is fading. Edge-gated —
                    // see MountedProp.Driven for why a per-frame restore would be churn.
                    if (p.Driven)
                        RestoreProp(p, seg,
                            "wall solid again, and nothing it overlaps is fading");
                    continue;
                }
                p.Driven = true;
                if (want > highest)
                    highest = want;
                // PICTURE-SIDE FALSIFIER — read BEFORE this frame writes the renderer.
                if (want == 2 && alreadyHeld && IsActuallyDrawing(r))
                    NoteUnitDressingRedrawn(r);
                _mountedTouched[r] = p;
                bool leafy = r is MeshRenderer lm && lm != null && RendererUsesFoliage(lm);
                bool ownChannel = p.System != null || p.ColorId >= 0 || p.CutoffId >= 0
                    || p.DissolveControlId >= 0;
                if (!leafy)
                    EnsureDissolveChannel(p); // never for foliage — ModBuild 255 ruling
                if (leafy && !ownChannel)
                {
                    // ModBuild 261: ONE implementation of the staggered-return rule, shared with
                    // ApplyFoliage. This lane and the foliage lane each had their own and the two
                    // resolved the KEY differently on a memo miss, which tore any prop split
                    // between seg.Foliage and seg.UnitDressing. See StaggerThresholdFor.
                    bool hide = want == 2 || eff >= StaggerThresholdFor(r);
                    if (hide)
                        HideByEnable(r);
                    else if (!r.enabled)
                        ShowIfWeHid(r);
                    ShowEdge(p, !hide, eff);
                    continue;
                }
                if (want == 2)
                {
                    DriveProp(p, 1f);
                    p.Return = ReturnPhase.HeldHidden; // ModBuild 265 — see ShowAttachmentPiece
                    ShowEdge(p, false, eff);
                    HideByEnable(r);
                }
                else
                {
                    // ModBuild 265: one shared show branch — the ramp, the return gate, the
                    // audit call and the enable. See FadeDriver.ShowAttachmentPiece.
                    ShowAttachmentPiece(p, r, eff, eff);
                }
            }
            if (_unionRaisedPropFrames != raisedBefore)
                _unionRaisedPieces++;
            if (lost)
                _nextRescan = 0f; // Apparance regenerated a member mid-fade — re-collect promptly
            seg.UnitDressingState = highest;
        }

        /// <summary>Record a dressing piece that is drawing again over a wall this segment has
        /// already hidden. Capped by name; the count is the full total.</summary>
        private void NoteUnitDressingRedrawn(Renderer r)
        {
            _unitDressingRedrawn++;
            if (_unitDressingRedrawnNames.Count >= PropUnitLeftVisibleCap
                || _unitDressingRedrawnNames.Contains(r.name))
                return;
            _unitDressingRedrawnNames.Add(r.name);
        }

        /// <summary>The deterministic cross-machine identity of a claiming segment: anchor name
        /// plus quantised XZ, the recipe <see cref="ComputeWireKeys"/> hashes. The last tie-break
        /// compares these ordinally, so it must contain nothing process-local.</summary>
        private static string ClaimKeyOf(Segment seg)
        {
            Component? a = seg.Anchor;
            if (a == null)
                return "<dead>";
            Vector3 p = a.transform.position;
            return $"{a.name}|{Mathf.RoundToInt(p.x * 2f)}|{Mathf.RoundToInt(p.z * 2f)}";
        }

        /// <summary>XZ distance from a segment's AABB centre to the unit's centroid.</summary>
        private static float UnitCentroidGapXZ(Segment seg, PropUnit unit)
        {
            Bounds union = default;
            bool have = false;
            foreach (MeshRenderer m in unit.Members)
            {
                if (m == null)
                    continue;
                if (!have) { union = m.bounds; have = true; }
                else union.Encapsulate(m.bounds);
            }
            if (!have)
                return float.MaxValue;
            float dx = seg.Bounds.center.x - union.center.x;
            float dz = seg.Bounds.center.z - union.center.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ---- census -----------------------------------------------------------------------

        /// <summary>Record one regrouped unit for the proof line: WHO it is, how many renderers it
        /// holds, which walls claimed parts of it, which one won and by what rule, and the fade
        /// that owner is actually carrying.</summary>
        private void NotePropUnitCensus(PropUnit unit, Segment owner, string rule,
                                        int moved, int recruited, int dressed, int unfadeable)
        {
            if (_propUnitCensus.Count >= PropUnitCensusCap)
                return;
            var claimants = new System.Text.StringBuilder();
            for (int i = 0; i < unit.ClaimSegs.Count; i++)
            {
                if (i > 0)
                    claimants.Append(", ");
                Component? a = unit.ClaimSegs[i].Anchor;
                claimants.Append($"'{(a != null ? a.name : "<dead>")}'×{unit.ClaimCounts[i]}"
                                 + $"@fade {unit.ClaimSegs[i].Fade:0.00}");
            }
            string ownerName = owner.Anchor != null ? owner.Anchor.name : "<dead>";
            _propUnitCensus.Add(
                $"'{unit.Label}'{(unit.ByStem ? "[name stem]" : "[shared ancestor]")} "
                + $"{unit.Members.Count} renderer(s), claimed by [{claimants}] → WON by "
                + $"'{ownerName}' ({rule}), fade {owner.Fade:0.00} applied to all"
                + (moved > 0 ? $", {moved} moved" : "")
                + (recruited > 0 ? $", {recruited} recruited" : "")
                + (dressed > 0 ? $", {dressed} given a dissolve channel as unit dressing" : "")
                + (unfadeable > 0 ? $", {unfadeable} LEFT VISIBLE (no channel of any kind)" : ""));
        }

        /// <summary>
        /// The proof line, emitted at the END of the rescan — after the pass has actually run, so
        /// every number in it is an outcome and not an initialiser (the ModBuild-164 lesson: a
        /// census that can only ever print its own starting state answers nothing). Silent while
        /// no prop is torn apart, and change-triggered on a signature that INCLUDES the owners'
        /// fades, so the line reprints the moment a regrouped unit's wall actually dissolves —
        /// which is the frame the report is about.
        /// </summary>
        private void LogPropUnitCensus()
        {
            if (_propUnitRegrouped == 0 && _propUnitRefusedUnits == 0
                && _propUnitWaterSkipped == 0 && _unitDressingHeldFaded == 0
                && _unitDressingHandedOver == 0 && _propUnitWallBuiltFigures == 0)
            {
                _propUnitCensusSig = -1;
                return;
            }
            int sig = _propUnitRegrouped * 977 + _propUnitMoved * 97 + _propUnitRecruited * 13
                      + _propUnitUnfadeable * 7 + _propUnitGroundLifted * 3
                      + _propUnitDressed * 101 + _propUnitRefusedUnits * 1009
                      + _unitDressingRedrawn * 61 + _propUnitFloorSkipped * 5
                      + _unitDressingHeldFaded * 149 + _unitDressingHandedOver * 71
                      + _propUnitWaterSkipped * 17 + _propUnitWallBuiltFigures * 1013
                      + _propUnitCensus.Count;
            foreach (string row in _propUnitCensus)
                sig = unchecked(sig * 31 + row.GetHashCode());
            if (sig == _propUnitCensusSig)
                return;
            _propUnitCensusSig = sig;
            VRLog.Info(Name,
                $"PROP UNIT: {_propUnitRegrouped} multi-part prop(s) were split across wall units "
                + $"and now have ONE owner each — every renderer of a unit fades together and by "
                + $"the same amount (user report 2026-08-19, skelet.jpg: 'Der Kopf des Skeletts "
                + $"wird immer noch ausgeblendet'). Grouping: shared prefab-ish ancestor, "
                + $"prop-sized only (≤ {PropUnitMaxRenderers} renderers, span ≤ "
                + $"{PropUnitMaxSpanWU:0.0} wu, walk stops at any wall entity); name stem only as "
                + $"a geometry-checked fallback. Owner: sticky while faded, then majority, then "
                + $"nearest centroid, then key order. {_propUnitMoved} renderer(s) moved to their "
                + $"unit's owner, {_propUnitRecruited} recruited from no owner at all, "
                + $"{_propUnitDressed} given a DISSOLVE CHANNEL as unit dressing, "
                + $"{_propUnitUnfadeable} LEFT VISIBLE. "
                + $"EVERY MEMBER GETS A CHANNEL OR THE UNIT IS REFUSED WHOLE (ModBuild 259, user "
                + $"2026-08-24 neues_wandproblem.jpg: 'Entweder verschwindet die ganze Wand mit "
                + $"ALLEM was dazu gehört (Bäume, Gestrüp, etc.) oder sie ist vollständig da'). "
                + $"The ModBuild-258 line read '106 left visible', every one of them "
                + $"Foliage-family with nothing to receive the fade — those are the "
                + $"{_propUnitDressed} above. A member this mod may not write AT ALL — a "
                + $"FIGURE, round-7 ruling — refuses its WHOLE unit instead, and a refusal can "
                + $"never cost more than one unit ({PropUnitMaxRenderers} renderers, span "
                + $"{PropUnitMaxSpanWU:0.0} wu): {_propUnitRefusedUnits} unit(s) refused this "
                + $"rescan"
                + (_propUnitRefused.Count > 0 ? $" — {string.Join("; ", _propUnitRefused)}" : "")
                + ". EVERY REFUSAL ROW NAMES WHICH ARM OF THE FIGURE PREDICATE FIRED, ON WHICH "
                + "GameObject, AND THE MEMBER'S BOUNDED ANCESTRY (ModBuild 291): 'FIGURE' alone is "
                + "a predicate's return value and not a cause, and the ModBuild-290 log's five "
                + "refusals — all five of them a crystal the wall generator itself parented under "
                + "'Wall N/Generated Content/' — could not be told apart from a monster by any "
                + "field it printed. "
                + $"WALL FURNITURE DOES NOT REFUSE ITS UNIT (ModBuild 291, user 2026-08-25, "
                + $"sollte_faden.jpg: 'das was wirklich als Wand vor den Figuren zu sehen ist mit "
                + $"dem Gestein daneben. Das sollte wie jedes andere Element auch faden'): "
                + $"{_propUnitWallBuiltFigures} figure-armed member(s) were released this rescan "
                + $"because a wall sits inside their own bounded {PropUnitMaxDepth}-level window "
                + $"and no ActorBehaviour / CInteractableActor sits above them (that chain stays "
                + $"an ABSOLUTE VETO — round-7 ruling, NOT relaxed; the bounded window and NOT "
                + $"IsWallGeneratedDressing's unbounded ProceduralWall climb, which answers YES "
                + $"for the floor crystal formation the user rules must STAY)"
                + (_propUnitWallBuiltNames.Count > 0
                    ? $": {string.Join("; ", _propUnitWallBuiltNames)}. THE FALSIFIER IS THE PATH "
                      + "IN THESE ROWS AND NEVER THE ASSET NAME — this level parents "
                      + "'CV_Ice_Crystal_Form_02' both under 'Generated Content/Full/' (the "
                      + "protected floor formation, ruling 2026-08-24) and under "
                      + "'Wall N/Generated Content/' (wall furniture). A row whose path holds no "
                      + "Wall node means the window is not the discriminator and this lift must be "
                      + "WITHDRAWN, not retuned"
                    : " — zero, so no unit changed hands on this term")
                + $". {_propUnitFloorSkipped} member(s) skipped as FLOOR PROPS of their own unit "
                + $"(the standing rule, a skip and not a refusal — ModBuild 167's Gestrüpp-Wand "
                + $"ruling), {_propUnitWaterSkipped} skipped as part of the WATER FEATURE's own "
                + $"unit (ModBuild 262 — the SAME shape, and it used to refuse the whole wall "
                + $"unit instead: every one of the 10 refusals in the ModBuild-261 session was "
                + $"this arm, named a BUSH or a VINE and not water, and pulled 7-10 renderer(s) "
                + $"back off 'Wall 4' while the user photographed that wall standing. A non-zero "
                + $"count here is EXPECTED and healthy; the water, its basin, bank and rim are "
                + $"still solid because nothing writes a renderer the rect covers)"
                + ". PICTURE-SIDE FALSIFIER (renderer.enabled + activeInHierarchy, never our "
                + $"ledger): {_unitDressingRedrawn} dressing piece(s) were DRAWING over a wall "
                + $"this pass had already hidden"
                + (_unitDressingRedrawnNames.Count > 0
                    ? $": {string.Join(", ", _unitDressingRedrawnNames)} — that is the defect, not "
                      + "the fix"
                    : " — zero, which is what a working whole-unit rule reads")
                + ". OWNERSHIP STICKY WHILE FADED (ModBuild 265, user 2026-08-24: 'das "
                + "Gestrüp soll gar nicht mehr auftauchen, solange die Wand gefaded "
                + "ist'): a dressing piece a segment stops owning is NEVER switched back "
                + "on while that segment is still hiding — it is conceded to whoever took "
                + "it, or kept hidden here until the masonry is solid again. The "
                + "ModBuild-264 session restored 66 of them at fade 1.00. This rescan "
                + $"{_unitDressingHeldFaded} piece(s) were HELD with their faded wall"
                + (_unitDressingHeldNames.Count > 0
                    ? $" ({string.Join(", ", _unitDressingHeldNames)})"
                    : string.Empty)
                + $" and {_unitDressingHandedOver} CONCEDED to another driver — another unit "
                + "owner's dressing list, or one of this same wall's own lanes (foliage, body, "
                + "stacked, wall renderer) — without ever being restored. "
                + $"WHOLE-UNIT RULE (ModBuild 258, wandproblem3.jpg 'ein Teil der Wand bleibt nun "
                + $"stehen und faded garnicht mehr'): a unit the standing rule calls ARCHITECTURE "
                + $"fades base and all — the per-renderer ground band "
                + $"({GroundExclusionHeightWU:0.0} wu) gets no vote inside it, because it cannot "
                + $"see that the piece it is holding is the bottom metre of a dissolving wall. "
                + $"{_propUnitGroundLifted} member(s) recruited THROUGH the ground band this "
                + $"rescan"
                + (_propUnitGroundNames.Count > 0
                    ? $": {string.Join(", ", _propUnitGroundNames)}"
                    : " — ZERO, which for the reported scrub wall means the band was never what "
                      + "held its stones; read the left-visible terms below instead")
                + ". "
                + (_propUnitLeftVisible.Count > 0
                    ? $"LEFT VISIBLE, by the term that refused each one: "
                      + $"{string.Join("; ", _propUnitLeftVisible)}. "
                    : string.Empty)
                + $"{string.Join("; ", _propUnitCensus)}.");
        }
    }
}
