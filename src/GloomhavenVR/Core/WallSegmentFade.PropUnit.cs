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
/// collection would; members it refuses are counted in the census as left visible, because a
/// renderer with no fade channel is a fact about the tileset and not something to paper over.</para>
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

        /// <summary>Segment anchors this rescan — the walk stops AT one rather than climbing
        /// through it, so a wall can never be swallowed into a "prop unit". Refreshed by
        /// <see cref="RefreshPropUnitAnchors"/> from BOTH scopes that need it: this pass's, and —
        /// since the standing rule's FLOOR arm started using the same walk — the standing-prop
        /// scope, which opens at the very top of the rescan.</summary>
        private readonly HashSet<Transform> _propUnitAnchors = new(64);

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

        /// <summary>Unit-root memo, keyed by the renderer's PARENT (siblings share an answer).
        /// Per-rescan only — Apparance rebirths these subtrees constantly, and a transform cached
        /// across rescans is a dangling reference within a couple of seconds
        /// (<c>WallSegmentFade.Standing.cs</c> pays for that lesson already).</summary>
        private readonly Dictionary<Transform, Transform?> _propUnitRootMemo = new(128);

        /// <summary>
        /// PERF S3 (2026-08-23) — THE THREE PER-NODE FACTS <see cref="PropUnitRootOf"/> ASKS,
        /// MEMOISED PER NODE FOR ONE COMMIT.
        ///
        /// <para>THE DEFECT. <see cref="PropUnitRootOf"/> climbs up to
        /// <see cref="PropUnitMaxDepth"/> (4) ancestors and asks each one three questions, two of
        /// which are FULL SUBTREE WALKS: <c>GetComponentsInChildren&lt;MeshRenderer&gt;</c> and
        /// <c>GetComponentInChildren&lt;ProceduralWall&gt;(includeInactive: true)</c>. The
        /// existing memos (<see cref="_propUnitRootMemo"/> and <c>_standingRootMemo</c>) cache
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
        /// <para>LIFETIME. Cleared once per commit in <see cref="BeginStandingPropScope"/> — the
        /// FIRST scope of the rescan, opened before any wall is refreshed — and deliberately NOT
        /// re-cleared in <see cref="BeginPropUnitScope"/>: sharing them across the standing pass
        /// and the prop-unit pass is where most of the saving is, and unlike the root memos these
        /// facts do not depend on <see cref="_propUnitAnchors"/> (which IS re-read between the
        /// two scopes, and is exactly why those two memos must stay separate). Transform keys
        /// therefore live no longer than the existing memos' do.</para>
        /// </summary>
        private readonly Dictionary<Transform, int> _nodeRendererCount = new(256);
        private readonly Dictionary<Transform, bool> _nodeIsWallEntity = new(256);
        private readonly Dictionary<Transform, bool> _nodeContainsWallEntity = new(256);

        /// <summary>True only between <see cref="BeginStandingPropScope"/> (the first scope of a
        /// commit) and <c>EndCommitPhases</c> (its <c>finally</c>). OUTSIDE that window — the
        /// WALL-PATH AUDIT reaches the same walk from the heartbeat, which runs after the commit
        /// has closed — every fact is taken live, so the constancy argument above only ever has
        /// to hold for the synchronous stretch it was made about.</summary>
        private bool _nodeFactsActive;

        /// <summary>Drop the per-node fact memos and open the window in which they may be read.
        /// Called from <see cref="BeginStandingPropScope"/> at the top of every commit.</summary>
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
        /// question this round turns on.</summary>
        private readonly List<string> _propUnitLeftVisible = new(8);

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
            foreach (Segment seg in _segments.Values)
            {
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null)
                        _propUnitClaimed.Add(r);
                }
            }
            foreach (Segment seg in _segments.Values)
            {
                if (IsPerRendererSplit(seg))
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

            // Carry this rescan's owners into the next one's STICKY input. Assigned wholesale
            // rather than merged: a unit that no longer exists must not keep voting.
            _propUnitOwnerLast.Clear();
            foreach (KeyValuePair<string, string> kv in _propUnitOwnerNow)
                _propUnitOwnerLast[kv.Key] = kv.Value;
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
            _propUnitRootMemo.Clear();
            _propUnitClaimed.Clear();
            _propUnitOwnerNow.Clear();
            _propUnitCensus.Clear();
            _propUnitTouched.Clear();
            _propUnitGroundNames.Clear();
            _propUnitLeftVisible.Clear();
            _propUnitRegrouped = 0;
            _propUnitMoved = 0;
            _propUnitRecruited = 0;
            _propUnitUnfadeable = 0;
            _propUnitGroundLifted = 0;

            RefreshPropUnitAnchors();
        }

        /// <summary>Re-read the segment anchors the unit walk must stop at. Called from
        /// <see cref="BeginStandingPropScope"/> at the top of the rescan (the standing rule's
        /// FLOOR arm walks before any wall has been refreshed) and again from
        /// <see cref="BeginPropUnitScope"/> once the table is final. A wall adopted for the first
        /// time THIS rescan is therefore missing from the early set for one pass — and the size
        /// caps are what catch it: a unit that reached across a wall is either wider than
        /// <see cref="PropUnitMaxSpanWU"/> or taller than
        /// <see cref="WallStandingProp.MaxHeightWU"/>, and is refused on those numbers rather than
        /// on the table.</summary>
        private void RefreshPropUnitAnchors()
        {
            _propUnitAnchors.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (seg.Anchor != null)
                    _propUnitAnchors.Add(seg.Anchor.transform);
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
            if (!_propUnitRootMemo.TryGetValue(parent, out Transform? root))
            {
                root = PropUnitRootOf(parent);
                _propUnitRootMemo[parent] = root;
            }
            if (root != null)
            {
                if (_propUnitByRoot.TryGetValue(root, out int existing))
                    return _propUnits[existing];
                _propUnitScratch.Clear();
                root.GetComponentsInChildren(includeInactive: false, _propUnitScratch);
                PropUnit? made = MakeUnit(root.name, byStem: false, _propUnitScratch);
                if (made != null)
                    _propUnitByRoot[root] = _propUnits.Count - 1;
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
                return _propUnits[have];
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
            // Only successes are memoised — a refusal has no index to store, and recomputing one
            // is a handful of bounds reads on a path that is rare by construction (it needs a
            // multi-piece prop parented flat, with no shared ancestor to find).
            if (stemUnit != null)
                _propUnitByStem[stemKey] = _propUnits.Count - 1;
            return stemUnit;
        }

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
                if (_propUnitAnchors.Contains(node) || NodeIsWallEntity(node))
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
            int pick = WallPropUnit.ChooseOwner(_propUnitClaimScratch, sticky, out string rule);
            if (pick == WallPropUnit.NoOwner)
                return;
            Segment owner = unit.ClaimSegs[pick];
            // Recorded whether or not anything moves, so the STICKY rule still knows this unit's
            // owner next rescan — an owner that is only remembered on the rescans that changed
            // something is an owner that forgets itself the moment it settles.
            _propUnitOwnerNow[unit.Id] = _propUnitClaimScratch[pick].Key;

            int moved = 0, recruited = 0, unfadeable = 0;
            _propUnitLosers.Clear();
            foreach (MeshRenderer m in unit.Members)
            {
                if (m == null)
                    continue;
                // Take it off every segment that is not the owner. The block MUST be cleared here:
                // FinishRefresh only clears leavers during a refresh, and this is not one, so a
                // renderer dropped from a currently-faded segment would keep that fade forever —
                // the exact restitution StripGroundRenderers performs for the ground band.
                foreach (Segment seg in _segments.Values)
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
                if (owner.Renderers.Contains(m))
                    continue;
                // A member no segment held: offer it through the one choke point, so the
                // standing-prop guard, the ground/water rules below and the toggle-native
                // accounting all see it exactly as a normal collection would.
                if (!PropUnitRecruit(owner, m))
                {
                    unfadeable++;
                    continue;
                }
                recruited++;
                _propUnitTouched.Add(m);
            }

            // The owner now controls geometry it did not before; its decision AABB has to enclose
            // it. Losers keep theirs (see the file header) — except one left with nothing at all,
            // which goes boundless so the decision and attachment passes skip it until the next
            // rescan rebuilds it.
            foreach (MeshRenderer m in unit.Members)
            {
                if (m == null || !owner.Renderers.Contains(m))
                    continue;
                if (!owner.HasBounds) { owner.Bounds = m.bounds; owner.HasBounds = true; }
                else owner.Bounds.Encapsulate(m.bounds);
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
            if (moved == 0 && recruited == 0)
                return;
            _propUnitRegrouped++;
            _propUnitMoved += moved;
            _propUnitRecruited += recruited;
            _propUnitUnfadeable += unfadeable;
            NotePropUnitCensus(unit, owner, rule, moved, recruited, unfadeable);
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
            if (RoomDecisionValid(owner.RoomIndex) && owner.RoomIndex < _roomFloorY.Count
                && m.bounds.max.y <= _roomFloorY[owner.RoomIndex] + GroundExclusionHeightWU)
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
                                        int moved, int recruited, int unfadeable)
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
                + (unfadeable > 0 ? $", {unfadeable} left visible (no fade channel)" : ""));
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
            if (_propUnitRegrouped == 0)
            {
                _propUnitCensusSig = -1;
                return;
            }
            int sig = _propUnitRegrouped * 977 + _propUnitMoved * 97 + _propUnitRecruited * 13
                      + _propUnitUnfadeable * 7 + _propUnitGroundLifted * 3
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
                + $"{_propUnitUnfadeable} left visible. "
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
