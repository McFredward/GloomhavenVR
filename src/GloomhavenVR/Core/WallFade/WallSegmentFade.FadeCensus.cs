using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WHICH RENDERERS THIS MOD IS ACTUALLY FADING, BY NAME — the census that ends the guessing about
/// the decapitated skeleton.
///
/// <para>THE REPORT, fourth round (2026-08-19, hardware, ModBuild 167, verbatim): <i>"Der Schädel
/// ist immer noch nicht sichtbar."</i> The photograph is <c>.planning/debug/skelet.jpg</c>: a full
/// human skeleton slumped on a WOODEN DECK at floor level, leaning back against a low masonry wall,
/// legs splayed across the planks. Ribcage, clavicles, arms, pelvis and legs solid and fully drawn;
/// THE SKULL GONE, a stub of vertebrae above the ribcage; and the low wall directly behind it
/// MID-DISSOLVE, its top edge carrying the ragged noise pattern of the masonry fade.</para>
///
/// <para>WHY THIS FILE EXISTS AT ALL, and it is the deliverable of this round rather than the fix.
/// Four rounds have now been spent on this defect without anyone being able to name the renderer
/// that disappears. In the ModBuild-167 log the <c>PROP UNIT</c> pass printed ZERO lines — it never
/// fired — and the <c>CR_OS_Skeleton_Statue_*</c> renderers the last two rounds were built around
/// are anchored 3.5 and 5.1 wu above the floor with their owners in agreement, i.e. wall statues
/// somewhere else in the level. Nothing named Skull, Bone or Skeleton appears at floor level in any
/// census the mod prints. So the object we are asked to protect has never once been NAMED by a log
/// line, and every round has therefore had to argue from a photograph. That is what this census
/// stops.</para>
///
/// <para>WHAT IT PRINTS, and every field of it is an OUTCOME. It runs AFTER the frame's
/// <c>Apply</c> pass, so it reports renderers this mod has actually written a non-zero fade onto —
/// not renderers it intends to. (ModBuild 164 shipped <c>MESH SWAP: no film mesh handled yet</c>,
/// a line that could only ever print its own initialiser, and it cost a whole round.) For each such
/// renderer: its name, its renderer type, a few levels of ancestor path, its anchor height over the
/// nearest anchored room floor (AABB <c>min.y</c>, the same anchor the mounted census prints), its
/// AABB centre and size, WHICH PATH wrote it — wall renderer, split segment, foliage, asset
/// sibling, wall body mesh, stacked shell, mounted dressing, shared corner piece, and whether the
/// prop-unit pass is what put it there — and the fade its owning segment is carrying.</para>
///
/// <para>RANKED BY DEFECT CLASS, then SMALLEST FIRST because a skull is small. SPLIT-OWNER units
/// come first, then TORN units, then everything else by size. Every capped list in the line states
/// its own truncation as <c>named K of N, dropped M</c>, BEFORE the list — a truncated list is not
/// absence, and this project has three wrong diagnoses that came from reading one as if it were.
/// A unit is torn when a fade path wrote SOME of a prop's renderers and left the rest solid, and
/// that is precisely the shape of the photograph — skull gone, ribcage and legs still there.</para>
///
/// <para>ONE POPULATION, NUMERATOR AND DENOMINATOR (ModBuild 271, and the reason this file was
/// reopened). Until ModBuild 270 the numerator counted writes keyed on ONE transform while the
/// denominator counted that transform's whole SUBTREE, so a child renderer's own write became a
/// SEPARATE unit and the parent reported TORN even though both halves had been written. It made
/// every flat-parented prop with a written descendant a guaranteed false positive: 166, 54, 31, 26
/// and 18 occurrences of five such props are the top five "torn" offenders of the whole
/// ModBuild-270 session, and all five are fine. Both terms are now counted over the countable
/// renderers of the unit's root — see <see cref="FadeDriver.BuildFadeUnits"/> for the invariant,
/// the merge that enforces it, and the two populations the denominator must not count.</para>
///
/// <para>SO DO SPLIT-OWNER UNITS, since ModBuild 258 — the SECOND shape a prop tears in, and one
/// this line could state only by accident before. <c>'CA_ICY_WallLight'</c> put its ice meshes on
/// <c>wall renderer of 'Wall 4'</c> and its blue torch emitters on <c>mounted dressing of
/// 'Wall 1'</c>: two owners, two independent fades, so whichever wall went first the other half of
/// the wall light stayed lit in mid-air (<c>wandproblem3.jpg</c>, <i>"die blaue Flamme ist nun
/// wieder sichtbar ohne dass sie gefaded ist"</i>). Such a unit can be 9/9 written and therefore
/// NOT torn, so without its own term the six-unit cap drops it by size. See
/// <see cref="FadeDriver.FadeUnit.OwnerSplit"/>, and <c>WallSegmentFade.Mounted.cs</c>'s
/// unit-affinity rule, which is what the count is meant to hold at zero.</para>
///
/// <para>HOW A PROP IS GROUPED: ModBuild 167's walk, <see cref="FadeDriver.PropUnitRootOf"/> — the
/// highest still prop-sized ancestor, stopping dead at a segment anchor, at anything carrying or
/// containing a <c>ProceduralWall</c>, and at the first container-scale subtree — and then, since
/// ModBuild 271, the census's own MERGE of descendant writes into that same unit key, under the
/// same bound and the same stopping rules. Every row says which of the two grouped it, because
/// "1 of 2 written" means something different in each case: no marker = the fade code's own walk;
/// <c>[no prop unit — subtree grouped by the census]</c> = only the census, which is the signal
/// that the tileset parented the prop flat; <c>[no prop unit — a unit of one]</c> = one renderer
/// and nothing else under it, which cannot be torn; <c>[node-scoped]</c> = the root's subtree is
/// container-scale, so it is not one prop and the denominator is the root node alone.</para>
///
/// <para>LOG HYGIENE. Rate-limited AND change-triggered on a signature over the written set and
/// their quantised fades: while nothing changes it prints nothing, and the expensive half (the
/// ancestor walks, the union bounds, the sort, the strings) only runs on a frame where the
/// signature actually moved. The cheap half is a walk of the segment table's lists, which the
/// module already does several times per rescan.</para>
///
/// <para>MULTIPLAYER: a diagnostic. It writes no renderer, no material, no segment and no wire
/// record, and suppressing it cannot change a pixel.</para>
///
/// <para>STEREO: nothing per-eye enters it; it reads CPU-side lists once per frame, after both eyes
/// have been given the same property block. <c>.planning/wall-fade-stereo-rivalry.md</c> (parked)
/// is untouched.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>Shortest gap between two FADE WRITE lines. Two seconds is the rescan cadence:
        /// a faster line could only ever repeat itself, and the census is meant to be read, not
        /// scrolled past.</summary>
        private const float FadeCensusIntervalSeconds = 2f;

        /// <summary>How many UNITS one line names. SIX until ModBuild 271, and six was the whole
        /// reason the ModBuild-270 log could not be read: 61-89 units per pass were reported TORN,
        /// nearly all of them the population artifact fixed in <see cref="BuildFadeUnits"/>, and
        /// the six slots went to them while the <c>49 unit(s) have TWO OR MORE OWNERS</c> clause
        /// named none of the 49. Twelve now, and the two genuine defect classes are ranked ahead
        /// of everything else (see <see cref="FadeUnitOrder"/>). The line always states how many
        /// units it dropped, in the same <c>named K of N, dropped M</c> form every other capped
        /// list in this census uses.</summary>
        private const int FadeCensusUnitCap = 12;

        /// <summary>How many SPLIT-OWNER units the header clause names. That clause used to be a
        /// bare number — <c>49 unit(s) have TWO OR MORE OWNERS on independent fades</c> — and a
        /// number without names is not evidence of anything. It now names them, each with its
        /// owners, their fades and how many renderers each owner holds.</summary>
        private const int FadeCensusOwnerSplitNameCap = 8;

        /// <summary>How many written renderers one unit names before it says "+N more".</summary>
        private const int FadeCensusMembersPerUnit = 4;

        /// <summary>How many still-solid siblings a torn unit names — the other half of the
        /// comparison, and the half that makes "torn" a fact rather than an inference. Raised
        /// from 3 to 6 in ModBuild 258: the two worst offenders in the ModBuild-257 log are
        /// <c>'PCG_FR_Pillar_Tree_Trunk_01_PR' 7/17 written … LEFT SOLID … +6 more</c> and
        /// <c>'CA_ICY_WallLight' 4/9 written … LEFT SOLID: p_fire_torch (8), fx_sparks (1),
        /// distort, +2 more</c>, i.e. both hid the tail of the very list the round turns on.</summary>
        private const int FadeCensusSolidNamesPerUnit = 6;

        /// <summary>One renderer this mod is currently fading, and by which path.</summary>
        private readonly struct FadeWrite
        {
            internal readonly Renderer R;
            /// <summary>Which fade path wrote it — the list it was found in, plus the markers for
            /// a per-renderer split segment and for the prop-unit pass having moved it.</summary>
            internal readonly string Path;
            /// <summary>The owning segment's anchor name.</summary>
            internal readonly string Owner;
            /// <summary>That segment's fade, 0..1 (mounted/stacked/body/sibling pieces ride a
            /// leading ramp off the same value — see <c>MountedFadeLead</c>).</summary>
            internal readonly float Fade;

            internal FadeWrite(Renderer r, string path, string owner, float fade)
            {
                R = r;
                Path = path;
                Owner = owner;
                Fade = fade;
            }
        }

        /// <summary>One grouped prop as the census sees it.</summary>
        private sealed class FadeUnit
        {
            public string Label = "?";
            /// <summary>The unit root. Held only for the duration of ONE census call and dropped
            /// in <see cref="Reset"/> — Apparance rebirths these subtrees constantly, and a
            /// transform kept across calls is a dangling reference within a couple of seconds
            /// (<c>WallSegmentFade.Standing.cs</c> pays for that lesson already).</summary>
            public Transform? Root;
            public readonly List<FadeWrite> Written = new(8);
            /// <summary>Names of the unit's renderers that NO fade path wrote — the comparison
            /// that makes a torn prop visible. Capped at
            /// <see cref="FadeCensusSolidNamesPerUnit"/>; <see cref="SolidTotal"/> is how many
            /// there actually are, so the line can state its own truncation.</summary>
            public readonly List<string> Solid = new(8);
            /// <summary>How many of the unit's renderers NO fade path wrote — the full count,
            /// not the named subset.</summary>
            public int SolidTotal;
            /// <summary>DISTINCT renderers written, which is the numerator TORN is judged on.
            /// <see cref="Written"/> can hold the same renderer twice — a renderer reachable from
            /// two segments, or from two of one segment's lists, is noted once per list — and
            /// counting those duplicates could push <c>Written.Count</c> up to or past
            /// <see cref="TotalRenderers"/> and hide a real tear.</summary>
            public int DistinctWritten;
            public int TotalRenderers;
            public float SizeRank;
            public float MinY;
            public float MaxY;
            public float FloorY;
            /// <summary>The fade code's OWN grouping walk (<see cref="PropUnitRootOf"/>, via
            /// <see cref="StandingFloorUnitRootOf"/>) returned this exact root for at least one of
            /// the unit's writes. False means the census grouped it and the fade code did not —
            /// which is itself the finding the header calls out: the tileset parented it flat and
            /// the grouping, not the geometry, is what needs widening.</summary>
            public bool FadeGrouped;
            /// <summary>The unit is judged NODE-SCOPED: its root's subtree is container-scale, so
            /// the subtree is not one prop and the denominator is the renderers ON the root node
            /// only. Such a unit is 1/1 by construction and can never be torn — which is the
            /// honest answer, not a manufactured one.</summary>
            public bool NodeScoped;
            /// <summary>Computed ONCE per census (see <see cref="OwnerSplit"/>), because the sort
            /// below reads it and a comparator that allocates a StringBuilder per comparison is a
            /// diagnostic that costs more than the thing it diagnoses.</summary>
            public string Owners = string.Empty;

            public void Reset()
            {
                Label = "?";
                Root = null;
                Written.Clear();
                Solid.Clear();
                SolidTotal = 0;
                DistinctWritten = 0;
                TotalRenderers = 0;
                SizeRank = 0f;
                MinY = 0f;
                MaxY = 0f;
                FloorY = 0f;
                FadeGrouped = false;
                NodeScoped = false;
                Owners = string.Empty;
            }

            /// <summary>
            /// TORN, over ONE population. Both terms are now counted over the same set of
            /// renderers — the countable renderers of this unit's root (see
            /// <see cref="CountsTowardFadeUnit"/> and <see cref="MeasureFadeUnit"/>) — which is
            /// the whole of the ModBuild-271 fix and the thing that must not silently regress.
            ///
            /// <para>WHAT IT USED TO DO, and why it sent a round to a wrong root cause. The
            /// numerator counted writes keyed on ONE transform; the denominator counted the whole
            /// SUBTREE of that transform. A child renderer's own write was keyed on the CHILD and
            /// became a separate unit, so a prop whose every renderer was written still reported
            /// <c>1/2</c> and was filed as a defect. In the ModBuild-270 log the same census print
            /// carried both <c>TORN 'CR_GE_Candle_04'[a unit of one] 1/2 written</c> and
            /// <c>'Candle_Fire_FX_02 (3)' 1/1 written … under
            /// 'Walls/Wall 3/Generated Content/CR_GE_Candle_04'</c>: same wall, same fade,
            /// parent and child, both halves written, and the line said TORN. The top five torn
            /// offenders of that whole session (166x, 54x, 31x, 26x, 18x) were that artifact and
            /// nothing else.</para>
            /// </summary>
            public bool Torn => WallStandingProp.IsTorn(DistinctWritten, TotalRenderers);

            /// <summary>
            /// THE OTHER WAY A PROP TEARS, and the ModBuild-257 log could not state it: not
            /// "some of me was written" but "I have TWO OWNERS, on two independent fades". The
            /// census printed the owner per renderer and then truncated at four members, so the
            /// split in <c>'CA_ICY_WallLight' 4/9</c> — ice meshes on <c>wall renderer of
            /// 'Wall 4'</c>, torch emitters on <c>mounted dressing of 'Wall 1'</c> — had to be
            /// reconstructed by hand across two different lines of the log. A unit with two
            /// owners half-survives every fade, whichever wall goes first, and after the
            /// ModBuild-258 unit-affinity rule (<c>WallSegmentFade.Mounted.cs</c>,
            /// <c>_mountedUnitHome</c>) there should be none. Stated per unit so one grep
            /// falsifies that.
            /// </summary>
            public string OwnerSplit()
            {
                var sb = new System.Text.StringBuilder();
                int distinct = 0;
                // Index loops on purpose: FadeWrite is a readonly STRUCT, so an identity test
                // between two copies would box and never be true. "First occurrence" is a
                // position, not a reference.
                for (int i = 0; i < Written.Count; i++)
                {
                    string owner = Written[i].Owner;
                    bool seen = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (string.Equals(Written[j].Owner, owner, System.StringComparison.Ordinal))
                        {
                            seen = true;
                            break;
                        }
                    }
                    if (seen)
                        continue;
                    int n = 0;
                    float fade = 0f;
                    for (int j = 0; j < Written.Count; j++)
                    {
                        if (!string.Equals(Written[j].Owner, owner, System.StringComparison.Ordinal))
                            continue;
                        // RENDERERS this owner holds, not WRITES. The same renderer can be noted
                        // twice under one owner (two of a segment's lists reach it), and an
                        // inflated x-count is the same lie in miniature that DistinctWritten
                        // fixes for the numerator.
                        bool already = false;
                        for (int k = 0; k < j; k++)
                        {
                            if (ReferenceEquals(Written[k].R, Written[j].R)
                                && string.Equals(Written[k].Owner, owner,
                                                 System.StringComparison.Ordinal))
                            {
                                already = true;
                                break;
                            }
                        }
                        if (already)
                            continue;
                        n++;
                        fade = Written[j].Fade;
                    }
                    distinct++;
                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append('\'').Append(owner).Append("'×").Append(n)
                      .Append("@fade ").Append(fade.ToString("0.00"));
                }
                return distinct > 1 ? sb.ToString() : string.Empty;
            }
        }

        private readonly List<FadeWrite> _fadeWrites = new(128);
        private readonly List<FadeUnit> _fadeUnits = new(32);
        private readonly List<FadeUnit> _fadeUnitPool = new(32);
        private readonly Dictionary<Transform, int> _fadeUnitByRoot = new(32);
        private readonly List<Renderer> _fadeCensusScratch = new(32);
        /// <summary>The solid-half enumeration's own list. Deliberately NOT
        /// <see cref="_fadeCensusScratch"/>: that one is walked while this one is being read, and
        /// a scratch list shared between two live iterations is how one gets cleared underneath
        /// the other.</summary>
        private readonly List<Renderer> _fadeSolidScratch = new(32);

        /// <summary>
        /// THE TERM THAT KEEPS ONE RENDERER SOLID — the field this census did not have.
        ///
        /// <para>THE REPORT (user, 2026-09-03, hardware, ModBuild 380, verbatim): <i>"In einem
        /// Level (dem ersten Level was ich geladen habe) faden die Säulen nicht, obwohl sie die
        /// Sicht versperren"</i>, <c>.planning/debug/säulen.jpg</c>. Free-standing square stone
        /// pillars stand in a row where the masonry beside them has already dissolved, hiding the
        /// floor hexes and the figures behind them.</para>
        ///
        /// <para>THE CENSUS ALREADY NAMED THEM AND STILL COULD NOT ANSWER IT. In the ModBuild-380
        /// log the pillar is named in 99 of the 127 prints of its own unit:
        /// <c>TORN 'CR_OS_Pillar_Large_02' 6/7 written … — LEFT SOLID under the same root, named
        /// 1 of 1, dropped 0: CR_OS_Pillar_Large_02</c>. Its own skull dressing
        /// (<c>EN_CR_LBSkull</c>) fades; the 1.5x3.5x1.5 wu pillar mesh does not. That is the
        /// photograph. But the <c>LEFT SOLID</c> clause carried NAMES ONLY, and the
        /// <c>TWO OWNERS</c> clause beside it names the owners of the WRITTEN half — so the log
        /// could state which renderer stays and never which term keeps it.</para>
        ///
        /// <para>WHY THE ANSWER WAS STRUCTURALLY UNREACHABLE, and it is one line:
        /// <see cref="LogFadeWriteCensus"/> walks the segment table under
        /// <c>if (seg.Fade &lt;= 0f) continue;</c>. A renderer whose owning segment is at fade
        /// ZERO is therefore skipped before the census ever looks at it, so the one owner that
        /// could explain a still-solid piece is exactly the owner the loop refuses to visit. The
        /// LEFTOVER OVER A FADED WALL line cannot cover for it either: that sweep reports
        /// renderers NO segment claimed, and a claimed wall renderer under a solid segment is
        /// outside its population by construction — which is why <c>CR_OS_Pillar_Large_02</c>
        /// appears in no LEFTOVER class anywhere in the 380 log. The blind spot was the lead.</para>
        ///
        /// <para>WHAT THIS INDEX IS: renderer → its owning segment, that segment's list (the same
        /// path string the WRITTEN rows print) and that segment's CURRENT fade, built over ALL
        /// segments including the zero-fade ones. It exists so a still-solid renderer can be
        /// classified into one of two arms, and the two arms send the next round to two different
        /// lines: <c>[OWNER SOLID]</c> — a wall segment does own it and that segment decided
        /// solid, so the question is a COVERAGE DECISION and the PER-WALL line for that segment
        /// carries the numbers; <c>[UNOWNED]</c> — no segment owns it, so the question is ADOPTION
        /// and the LEFTOVER line carries the reject reason.</para>
        ///
        /// <para>WHAT IT DOES NOT CLAIM. It reports an OWNER and that owner's FADE, both read
        /// back off the live segment table. It does not say why that owner decided solid — the
        /// PER-WALL line owns that question and already answers it per segment — and it must
        /// never be edited into saying so, because it cannot observe it.</para>
        ///
        /// <para>COST: one pass over the segment table's lists, on a frame that is ALREADY
        /// printing the census — i.e. behind both the 2 s cadence gate and the change-trigger
        /// signature. In the ModBuild-380 session that is 68 passes over at most ~1 225 renderer
        /// entries across a ~20-minute run, against a decision loop that walks the same lists
        /// several times per 2 s rescan. It writes no renderer, no material and no segment.</para>
        /// </summary>
        private readonly struct SolidOwner
        {
            // FIELD NAMES ARE DELIBERATELY PREFIXED. check-instrument-writes.py resolves reads by
            // BARE FIELD NAME across the whole tree, so a diagnostic struct with fields called
            // Owner/Path/Fade collects a read from every unrelated `Owner`, `Path` and `Fade` in
            // the mod and reports itself as load-bearing. That is exactly why FadeWrite's four
            // fields sit in .planning/refactor/INSTRUMENT-WRITES.baseline. A unique prefix costs
            // nothing and keeps this struct out of the baseline instead of adding to it.

            /// <summary>The owning segment's anchor name.</summary>
            internal readonly string SegOwner;
            /// <summary>Which of the segment's lists holds it — the same vocabulary the WRITTEN
            /// rows use, so the two halves of a torn unit are read in one currency.</summary>
            internal readonly string SegPath;
            /// <summary>That segment's fade RIGHT NOW. Zero is the interesting value: it is the
            /// term, and it is the value the write loop skips on.</summary>
            internal readonly float SegFade;

            internal SolidOwner(string owner, string path, float fade)
            {
                SegOwner = owner;
                SegPath = path;
                SegFade = fade;
            }
        }

        /// <summary>Renderer → owning segment, rebuilt per printed census and cleared at the end
        /// of it. Held only for the duration of ONE census call for the same reason
        /// <see cref="FadeUnit.Root"/> is: Apparance rebirths these subtrees constantly, and a
        /// renderer kept across calls is a dangling reference within a couple of seconds.</summary>
        private readonly Dictionary<Renderer, SolidOwner> _solidOwners = new(256);

        /// <summary>Unit indices with at least one still-solid renderer, ordered LARGEST FIRST
        /// for <see cref="EmitSolidBlockerLine"/>. A list rather than a re-sort of
        /// <c>_fadeUnits</c>: that order is the debug census's and is load-bearing for it.</summary>
        private readonly List<int> _solidBlockerOrder = new(32);

        /// <summary>Tallies over the still-solid population of THIS census, by the term that keeps
        /// each piece. Written and read only by the census.</summary>
        private int _solidOwnerSolid;
        private int _solidUnowned;
        private int _solidOwnerFading;

        /// <summary>
        /// Index every renderer the segment table holds, INCLUDING the segments at fade zero that
        /// <see cref="LogFadeWriteCensus"/>'s write loop skips. Same lists, same order, same path
        /// vocabulary — the only difference is that this one does not filter on the fade, because
        /// the fade is the answer.
        /// </summary>
        private void BuildSolidOwnerIndex()
        {
            _solidOwners.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                string owner = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                string wallPath = IsPerRendererSplit(seg) ? "wall renderer[split segment]"
                                                          : "wall renderer";
                foreach (MeshRenderer r in seg.Renderers)
                    NoteSolidOwner(r, wallPath, owner, seg.Fade);
                foreach (MeshRenderer r in seg.Foliage)
                    NoteSolidOwner(r, "foliage", owner, seg.Fade);
                foreach (MeshRenderer r in seg.Siblings)
                    NoteSolidOwner(r, "asset sibling", owner, seg.Fade);
                foreach (MountedProp p in seg.Body)
                    NoteSolidOwner(p.Renderer, "wall body mesh", owner, seg.Fade);
                foreach (MountedProp p in seg.Stacked)
                    NoteSolidOwner(p.Renderer, "stacked shell", owner, seg.Fade);
                foreach (MountedProp p in seg.Mounted)
                    NoteSolidOwner(p.Renderer, "mounted dressing", owner, seg.Fade);
                foreach (MountedProp p in seg.UnitDressing)
                    NoteSolidOwner(p.Renderer, "prop-unit dressing", owner, seg.Fade);
            }
        }

        /// <summary>Record one ownership. FIRST writer wins, and deliberately: these lists are
        /// walked in the same order the write loop walks them, so a renderer reachable from two of
        /// them is attributed to the same one in both halves of the line.</summary>
        private void NoteSolidOwner(Renderer? r, string path, string owner, float fade)
        {
            if (r == null || IsModObject(r))
                return;
            if (!_solidOwners.ContainsKey(r))
                _solidOwners[r] = new SolidOwner(owner, path, fade);
        }

        /// <summary>
        /// One still-solid renderer, with the term that keeps it. Replaces the bare
        /// <c>piece.name</c> the LEFT SOLID clause used to print — the marker text around it is
        /// unchanged, this only fills the slot the marker introduces.
        /// </summary>
        private string DescribeSolidPiece(Renderer piece)
        {
            if (!_solidOwners.TryGetValue(piece, out SolidOwner o))
                return piece.name + " ← NO WALL SEGMENT OWNS IT [UNOWNED]";
            return piece.name + " ← " + o.SegPath + " of '" + o.SegOwner + "' fade "
                   + o.SegFade.ToString("0.00")
                   + (o.SegFade <= 0f ? " [OWNER SOLID]"
                                      : " [OWNER FADING, THIS PIECE UNWRITTEN]");
        }

        /// <summary>Tally one still-solid renderer by its term. SEPARATE from
        /// <see cref="DescribeSolidPiece"/> on purpose: the names are capped at
        /// <see cref="FadeCensusSolidNamesPerUnit"/> per unit and the TALLIES ARE NOT, so a term
        /// that only ever appears in a unit's seventh solid piece is still counted. A tally that
        /// silently shared the name cap would be a summary stat over a truncated list, which is
        /// the shape of two wrong diagnoses in this project already.</summary>
        private void ClassifySolidPiece(Renderer piece)
        {
            if (!_solidOwners.TryGetValue(piece, out SolidOwner o))
                _solidUnowned++;
            else if (o.SegFade <= 0f)
                _solidOwnerSolid++;
            else
                _solidOwnerFading++;
        }
        /// <summary>Countable renderers under a node, memoised for the duration of ONE census
        /// call — see <see cref="CensusRendererCount"/>. Cleared at the end of every call:
        /// Apparance rebirths these subtrees constantly and a transform kept across calls is a
        /// dangling reference within a couple of seconds.</summary>
        private readonly Dictionary<Transform, int> _fadeUnitSubtreeCount = new(64);
        /// <summary>Candidate root -> the highest candidate root ABOVE it, or null. The merge
        /// map of <see cref="BuildFadeUnits"/> pass 2.</summary>
        private readonly Dictionary<Transform, Transform?> _fadeUnitMerge = new(64);
        private readonly List<Transform> _fadeUnitCandidates = new(64);
        private readonly HashSet<Transform> _fadeUnitCandidateSet = new(64);
        /// <summary>Index-aligned with <see cref="_fadeWrites"/>: the candidate root of each
        /// write, computed in pass 1 and re-read in pass 4 rather than recomputed.</summary>
        private readonly List<Transform> _fadeWriteCandidate = new(128);
        private float _nextFadeCensus;
        private int _fadeCensusSig = -1;

        /// <summary>
        /// Collect every renderer the frame's fade paths just wrote and, when that set has changed,
        /// print it. Called at the end of <c>Tick</c>, after <c>Apply</c> and
        /// <c>ApplyCornerPieces</c> — so every number is a result.
        /// </summary>
        private void LogFadeWriteCensus(float now)
        {
            EmitShowEdgeAudit(now); // ModBuild 261 — its own cadence, see below
            if (now < _nextFadeCensus)
                return;
            _nextFadeCensus = now + FadeCensusIntervalSeconds;

            _fadeWrites.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                if (seg.Fade <= 0f)
                    continue;
                string owner = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                // A segment whose anchor IS a renderer is one of the deliberate per-renderer
                // splits (RefreshSplitWall / NeutralizeEngulfingSegments). The prop-unit pass
                // steps around those on purpose, so the census has to say when a write came from
                // one — otherwise "why did the prop-unit pass not heal this" has no answer.
                string wallPath = IsPerRendererSplit(seg) ? "wall renderer[split segment]"
                                                          : "wall renderer";
                foreach (MeshRenderer r in seg.Renderers)
                    NoteFadeWrite(r, wallPath, owner, seg.Fade);
                foreach (MeshRenderer r in seg.Foliage)
                    NoteFadeWrite(r, "foliage", owner, seg.Fade);
                foreach (MeshRenderer r in seg.Siblings)
                    NoteFadeWrite(r, "asset sibling", owner, seg.Fade);
                foreach (MountedProp p in seg.Body)
                    NoteFadeWrite(p.Renderer, "wall body mesh", owner, seg.Fade);
                foreach (MountedProp p in seg.Stacked)
                    NoteFadeWrite(p.Renderer, "stacked shell", owner, seg.Fade);
                foreach (MountedProp p in seg.Mounted)
                    NoteFadeWrite(p.Renderer, "mounted dressing", owner, seg.Fade);
                // ModBuild 259: the whole-unit arm. Without this line every member the prop-unit
                // pass gave a dissolve channel to would still be counted LEFT SOLID and the TORN
                // number would not move — the census would report the fix as the defect.
                foreach (MountedProp p in seg.UnitDressing)
                    NoteFadeWrite(p.Renderer, "prop-unit dressing", owner, seg.Fade);
            }
            foreach (CornerPiece cp in _live.CornerPieces)
            {
                float fade = cp.B == null ? cp.A.Fade : Mathf.Min(cp.A.Fade, cp.B.Fade);
                if (fade <= 0f)
                    continue;
                string owner = cp.A.Anchor != null ? cp.A.Anchor.name : "<dead>";
                NoteFadeWrite(cp.Prop.Renderer, "shared corner piece", owner, fade);
            }

            // Change trigger. Quantised to 1/16 of a fade so a ramp does not reprint every line of
            // its own transition, but a piece arriving or leaving does.
            int sig = _fadeWrites.Count * 397;
            foreach (FadeWrite w in _fadeWrites)
            {
                sig = unchecked(sig * 31 + w.R.GetInstanceID());
                sig = unchecked(sig * 31 + Mathf.RoundToInt(w.Fade * 16f));
                sig = unchecked(sig * 31 + w.Path.GetHashCode());
                // ModBuild 258: the OWNER is part of the signature. A prop changing hands between
                // two walls at the same fade is now the defect this line reports, and without
                // this term the line stays silent through exactly that transition.
                sig = unchecked(sig * 31 + w.Owner.GetHashCode());
            }
            if (sig == _fadeCensusSig)
                return;
            _fadeCensusSig = sig;
            if (_fadeWrites.Count == 0)
                return;

            // The zero-fade half of the segment table, which the write loop above skipped
            // on `seg.Fade <= 0f`. Built HERE — after the cadence gate AND after the change
            // trigger — so it costs nothing at all on a frame that prints nothing.
            _solidOwnerSolid = 0;
            _solidUnowned = 0;
            _solidOwnerFading = 0;
            BuildSolidOwnerIndex();
            BuildFadeUnits();
            EmitFadeWriteCensus();
            EmitSolidBlockerLine();
            _solidOwners.Clear();
        }

        /// <summary>Record one write, skipping renderers that are gone or ours.</summary>
        private void NoteFadeWrite(Renderer? r, string path, string owner, float fade)
        {
            if (r == null || IsModObject(r))
                return;
            if (_propUnitTouched.Contains(r))
                path += "[prop unit]";
            _fadeWrites.Add(new FadeWrite(r, path, owner, fade));
        }

        /// <summary>How many merge hops <see cref="ResolveFadeUnitRoot"/> will follow. Each hop is
        /// strictly upward in the hierarchy so the chain cannot cycle; the bound is here so a
        /// diagnostic can never become an infinite loop on a hierarchy nobody on the build machine
        /// can open.</summary>
        private const int FadeUnitMergeMaxHops = 8;

        /// <summary>
        /// GROUP THE WRITES INTO PROP UNITS — over ONE population, which is the ModBuild-271
        /// correction and the invariant every future edit of this method has to preserve.
        ///
        /// <para><b>THE INVARIANT.</b> For every unit, the numerator
        /// (<see cref="FadeUnit.DistinctWritten"/>) and the denominator
        /// (<see cref="FadeUnit.TotalRenderers"/>) count renderers drawn from the SAME set: the
        /// countable renderers of the unit's root — its whole subtree, or, when that subtree is
        /// container-scale, the root node alone. If a renderer can appear in the denominator whose
        /// write is filed against a DIFFERENT unit, this method is broken and every <c>TORN</c> it
        /// prints is a manufactured tear.</para>
        ///
        /// <para><b>WHAT WAS BROKEN.</b> The unit key was
        /// <c>StandingFloorUnitRootOf(w.R) ?? w.R.transform</c> and the denominator was
        /// <c>root.GetComponentsInChildren&lt;Renderer&gt;()</c>. For a prop parented flat under
        /// <c>Wall N/Generated Content</c> the walk breaks at the wall entity immediately, so the
        /// key became the renderer's OWN transform while the denominator stayed the whole subtree
        /// — and a child renderer's own write was keyed on the CHILD and filed as a separate unit.
        /// Any such unit whose descendants were also written was GUARANTEED to report torn.
        /// ModBuild 270's log carries the proof inside a single census print: <c>TORN
        /// 'CR_GE_Candle_04'[a unit of one] 1/2 written … wall renderer of 'Wall 3' fade 1.00</c>
        /// and, five rows later, <c>'Candle_Fire_FX_02 (3)' 1/1 written … under
        /// 'Walls/Wall 3/Generated Content/CR_GE_Candle_04' … mounted dressing of 'Wall 3' fade
        /// 1.00</c>. Same XZ, same wall, same fade, parent and child, both halves written — and
        /// the line filed it as the defect. That artifact is the top five torn offenders of the
        /// whole session (166x, 54x, 31x, 26x, 18x), and it pushed the genuine
        /// <c>TWO OR MORE OWNERS</c> units out of every one of the six printed slots.</para>
        ///
        /// <para><b>WHICH POPULATION IS CANONICAL: THE SUBTREE.</b> A unit is what the eye sees as
        /// one prop, and the question the line exists to answer — <i>which of its renderers was
        /// left solid</i> — has no answer at all if the unit is one node: a node-only unit is 1/1
        /// by construction and can never name a solid sibling. So the subtree stays the
        /// denominator and the NUMERATOR is made to match, by collecting descendant writes into
        /// the same unit key (passes 1-3 below).</para>
        ///
        /// <para><b>HOW.</b> Pass 1 gives every write a CANDIDATE root — the fade code's own
        /// grouping walk when it returns one, the renderer's transform otherwise. Pass 2 asks, for
        /// every distinct candidate, whether a HIGHER candidate root sits above it within the same
        /// bounded window the grouping walk uses (<see cref="PropUnitMaxDepth"/> levels, stopping
        /// dead at a segment anchor or a wall entity, so a wall can never be swallowed into a
        /// "prop"). Pass 3 follows those links to their top. The result: every write anywhere in a
        /// written-rooted subtree lands in ONE unit, and the candle above becomes
        /// <c>'CR_GE_Candle_04' 2/2</c>.</para>
        ///
        /// <para><b>WHAT THE DENOMINATOR MUST NOT COUNT — one thing, and it is a COUNT, not a
        /// renderer type.</b> See <see cref="CountsTowardFadeUnit"/> for why no type test may ever
        /// be added here: the numerator is whatever the fade paths wrote, so any filter the
        /// numerator does not share pulls the two terms apart again — and since ModBuild 266 the
        /// fade paths write skinned renderers on provenance.</para>
        /// <list type="number">
        /// <item>A CONTAINER-SCALE subtree. <see cref="PropUnitRootOf"/> already breaks at
        ///   <c>count &gt; PropUnitMaxRenderers</c> — "this node and everything above it are
        ///   architecture" — but it counts <c>MeshRenderer</c> while this census counts every
        ///   <c>Renderer</c>, so a node holding 158 renderers of which few are mesh passed that
        ///   test and was reported as one prop. In the ModBuild-270 log that node is
        ///   <c>Board</c>: <c>TORN 'Board' 6/158 written … LEFT SOLID: Ribbon, WP_Berserker_Axe,
        ///   … +146 more</c> — the entire game board with every figure standing on it, filed as a
        ///   half-faded prop. The same threshold is now applied in the census's own currency, both
        ///   when accepting a candidate root and when measuring the unit.</item>
        /// </list>
        /// </summary>
        private void BuildFadeUnits()
        {
            foreach (FadeUnit u in _fadeUnits)
            {
                u.Reset();
                _fadeUnitPool.Add(u);
            }
            _fadeUnits.Clear();
            _fadeUnitByRoot.Clear();
            _fadeUnitSubtreeCount.Clear();
            _fadeUnitMerge.Clear();
            _fadeUnitCandidates.Clear();
            _fadeUnitCandidateSet.Clear();
            _fadeWriteCandidate.Clear();

            // PASS 1 - the candidate root of every write, index-aligned with _fadeWrites.
            // _fadeUnitMerge doubles as the "already seen this candidate" set; pass 2 overwrites
            // every placeholder it puts here.
            foreach (FadeWrite w in _fadeWrites)
            {
                Transform cand = FadeUnitCandidateOf(w.R);
                _fadeWriteCandidate.Add(cand);
                if (_fadeUnitMerge.ContainsKey(cand))
                    continue;
                _fadeUnitMerge[cand] = null;
                _fadeUnitCandidates.Add(cand);
                // ELIGIBLE AS A MERGE TARGET only if it is prop-scale. A renderer can sit
                // directly ON a container node - 'Generated Content' carries wall meshes - and
                // that node then becomes its own candidate. Merging every write beneath it into
                // that node would rebuild, in one step, exactly the 'Board' 6/158 artifact this
                // rewrite exists to remove. Such a candidate still gets its own unit (node-scoped,
                // see below); what it may not be is somebody else's root.
                if (CensusRendererCount(cand) <= PropUnitMaxRenderers)
                    _fadeUnitCandidateSet.Add(cand);
            }

            // PASS 2 - the merge map: candidate -> the highest candidate root above it, if any.
            foreach (Transform cand in _fadeUnitCandidates)
                _fadeUnitMerge[cand] = HighestCandidateRootAbove(cand);

            // PASS 3 - resolve each candidate to the top of its chain and file the write there.
            for (int i = 0; i < _fadeWrites.Count; i++)
            {
                FadeWrite w = _fadeWrites[i];
                Transform root = ResolveFadeUnitRoot(_fadeWriteCandidate[i]);
                if (!_fadeUnitByRoot.TryGetValue(root, out int at))
                {
                    FadeUnit made = RentFadeUnit();
                    made.Label = root.name;
                    made.Root = root;
                    // The container-scale test in the census's own currency - see the doc above.
                    made.NodeScoped = CensusRendererCount(root) > PropUnitMaxRenderers;
                    MeasureFadeUnit(root, made);
                    at = _fadeUnits.Count;
                    _fadeUnits.Add(made);
                    _fadeUnitByRoot[root] = at;
                }
                FadeUnit unit = _fadeUnits[at];
                unit.Written.Add(w);
                // Did the FADE CODE's own walk group this write here, or did only the census? The
                // header reports the difference; it is the "the tileset parented it flat" signal.
                if (ReferenceEquals(StandingFloorUnitRootOf(w.R), root))
                    unit.FadeGrouped = true;
            }

            // DISTINCT renderers written - the numerator TORN is judged on. A renderer reachable
            // from two segments, or from two of one segment's lists, is noted once per list, and
            // those duplicates could push the count up to TotalRenderers and HIDE a real tear.
            foreach (FadeUnit u in _fadeUnits)
            {
                int distinct = 0;
                for (int i = 0; i < u.Written.Count; i++)
                {
                    bool seen = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (ReferenceEquals(u.Written[j].R, u.Written[i].R)) { seen = true; break; }
                    }
                    if (!seen)
                        distinct++;
                }
                u.DistinctWritten = distinct;
            }

            // The still-solid half. A renderer of the unit that nothing wrote is what makes the
            // unit TORN; the names are what make it readable. There is NO exemption here any more:
            // ModBuild 270 skipped this enumeration for every unit the grouping walk had not
            // grouped, so exactly the units that printed "1 of 2 written" then refused to name the
            // missing one - which is the whole point of the clause.
            foreach (FadeUnit u in _fadeUnits)
            {
                if (u.Root == null || u.DistinctWritten >= u.TotalRenderers)
                    continue;
                CollectFadeUnitRenderers(u.Root, u.NodeScoped, _fadeSolidScratch);
                foreach (Renderer piece in _fadeSolidScratch)
                {
                    if (!CountsTowardFadeUnit(piece))
                        continue;
                    bool written = false;
                    foreach (FadeWrite w in u.Written)
                    {
                        if (ReferenceEquals(w.R, piece)) { written = true; break; }
                    }
                    if (written)
                        continue;
                    u.SolidTotal++;
                    // Every solid piece is CLASSIFIED; only the first six are NAMED. See
                    // ClassifySolidPiece for why those two populations are deliberately different.
                    ClassifySolidPiece(piece);
                    if (u.Solid.Count < FadeCensusSolidNamesPerUnit)
                        u.Solid.Add(DescribeSolidPiece(piece));
                }
                _fadeSolidScratch.Clear();
            }

            // The owner split, once per unit - the sort below reads it. See FadeUnit.Owners.
            foreach (FadeUnit u in _fadeUnits)
                u.Owners = u.OwnerSplit();

            // SPLIT first, then TORN, then SMALLEST first (a skull is small) - see FadeUnitOrder.
            // Insertion sort: the list is tens of entries and this runs at most once every two
            // seconds, on a frame where something actually changed.
            for (int i = 1; i < _fadeUnits.Count; i++)
            {
                FadeUnit key = _fadeUnits[i];
                int j = i - 1;
                while (j >= 0 && FadeUnitOrder(key, _fadeUnits[j]) < 0)
                {
                    _fadeUnits[j + 1] = _fadeUnits[j];
                    j--;
                }
                _fadeUnits[j + 1] = key;
            }

            // The grouping scratch holds TRANSFORMS and is dead the moment the units are built.
            // Apparance rebirths these subtrees constantly, so it is dropped here rather than at
            // the start of the next call - the same discipline FadeUnit.Root keeps, and the same
            // one WallSegmentFade.Standing.cs already pays for having learned.
            _fadeUnitSubtreeCount.Clear();
            _fadeUnitMerge.Clear();
            _fadeUnitCandidates.Clear();
            _fadeUnitCandidateSet.Clear();
            _fadeWriteCandidate.Clear();
        }

        /// <summary>
        /// Negative when <paramref name="a"/> must be printed before <paramref name="b"/> —
        /// RANKED BY DEFECT CLASS, most-broken first, since ModBuild 271.
        ///
        /// <para>Until ModBuild 271 the two classes shared one rank and the tie broke on size, so
        /// the six slots went to whatever was smallest — which, with 61-89 units per pass reported
        /// torn by the population artifact <see cref="BuildFadeUnits"/> now fixes, meant the
        /// slots went to false positives while the <c>49 unit(s) have TWO OR MORE OWNERS</c>
        /// clause named none of the 49.</para>
        ///
        /// <list type="number">
        /// <item>TWO OR MORE OWNERS. A unit with two owners can be 9/9 written and therefore NOT
        ///   torn, and it is still the defect (CA_ICY_WallLight — see FadeUnit.OwnerSplit); it is
        ///   also the class the ModBuild-258 unit-affinity rule is meant to hold at ZERO, so any
        ///   member of it is a live falsification of a shipped rule. It goes first.</item>
        /// <item>TORN. Now that both terms are counted over one population it means what the line
        ///   says it means.</item>
        /// <item>Everything else, SMALLEST FIRST, because a skull is small.</item>
        /// </list>
        /// </summary>
        private static int FadeUnitOrder(FadeUnit a, FadeUnit b)
        {
            bool asplit = a.Owners.Length > 0;
            bool bsplit = b.Owners.Length > 0;
            if (asplit != bsplit)
                return asplit ? -1 : 1;
            if (a.Torn != b.Torn)
                return a.Torn ? -1 : 1;
            return a.SizeRank < b.SizeRank ? -1 : a.SizeRank > b.SizeRank ? 1 : 0;
        }

        /// <summary>
        /// May this renderer be counted as a member of a fade unit? The denominator, the unit
        /// bounds and the solid enumeration all go through here.
        ///
        /// <para><b>THE RULE, and it is the reason this predicate is two clauses and not three.</b>
        /// The NUMERATOR does not go through here — it is whatever the fade paths actually wrote,
        /// collected by <see cref="NoteFadeWrite"/>. So this filter may only ever exclude a
        /// renderer the numerator CANNOT contain. Exclude anything the numerator can hold and the
        /// two terms come apart again, in the direction that is worse: <c>written</c> then exceeds
        /// <c>total</c>, <see cref="WallStandingProp.IsTorn"/> returns FALSE, and a REAL tear is
        /// hidden. Both surviving clauses satisfy the rule structurally: <c>NoteFadeWrite</c>
        /// drops a null and drops <see cref="IsModObject"/>, so neither can ever be in the
        /// numerator.</para>
        ///
        /// <para><b>WHY THERE IS NO RENDERER-TYPE CLAUSE HERE, and why one must never be added.</b>
        /// A draft of this rewrite excluded <c>SkinnedMeshRenderer</c>, reasoning that no fade path
        /// may write a figure. That premise was TRUE when it was written and has been false since
        /// ModBuild 266, which lifted the figure-renderer refusal for wall-generator content on
        /// provenance ("Ich möchte, dass die Flagge inklusive der Stange vollständig mit faded");
        /// ModBuild 268 added a fifth site for the same reason. The ModBuild-270 log falsifies the
        /// premise 124 times over, in the ACCEPTED list:
        /// <c>'EN_CR_Hanging_01_Cloth_Post'[skinned→cutoff] anchor 2.3 gap 0.00 → 'Wall 2'
        /// [WALL-BUILT: adopted on provenance, ModBuild 266]</c>. Its sibling
        /// <c>'EN_CR_Hanging_01_Mesh'[skinned]</c> is REFUSED in the same log
        /// (<c>anchor 0.44 under the airborne bar 1.00 — reads as floor-supported</c>) — so that
        /// prop is a real half-faded banner right now, and the type test would have printed it
        /// <c>1/0 written</c> and NOT torn. Which types may be written is a POLICY, owned by
        /// another file, and it has already moved twice; an instrument that encodes it drifts
        /// silently the next time it moves. The census asks structural questions only.</para>
        ///
        /// <para>The figure population this clause was meant to remove — the whole game board with
        /// every actor on it — is removed by the CONTAINER-SCALE rejection in
        /// <see cref="BuildFadeUnits"/> instead, which is a count and not a policy. And its own
        /// motivating examples did not need it: <c>left_clavicle01</c> and <c>left_thigh01</c>,
        /// the two "figure rig bones" of the ModBuild-270 split-owner list, are
        /// <c>[mesh]</c> renderers of a SCENERY skeleton
        /// (<c>Generated Content/PCG_Test_Feature_Medium_1/skeleton_Standing 1/chest01</c>) — the
        /// skelet.jpg subject itself, which no type test may drop.</para>
        /// </summary>
        private static bool CountsTowardFadeUnit(Renderer? piece)
            => piece != null && !IsModObject(piece);

        /// <summary>The renderers of one unit, into <paramref name="into"/> — the ONE place that
        /// decides what a unit's renderers are, so the count, the solid enumeration and the bounds
        /// cannot disagree. A node-scoped unit (container-scale subtree) is the root node's own
        /// renderers; every other unit is the subtree.</summary>
        private static void CollectFadeUnitRenderers(Transform root, bool nodeScoped,
                                                     List<Renderer> into)
        {
            into.Clear();
            if (nodeScoped)
                root.GetComponents(into);
            else
                root.GetComponentsInChildren(includeInactive: false, into);
        }

        /// <summary>Countable renderers under a node, memoised for the duration of one census
        /// call. Same predicate as the measurement, so "is this container-scale" and "how big is
        /// this unit" are answered in the same currency.</summary>
        private int CensusRendererCount(Transform node)
        {
            if (_fadeUnitSubtreeCount.TryGetValue(node, out int cached))
                return cached;
            _fadeCensusScratch.Clear();
            node.GetComponentsInChildren(includeInactive: false, _fadeCensusScratch);
            int kept = 0;
            foreach (Renderer piece in _fadeCensusScratch)
            {
                if (CountsTowardFadeUnit(piece))
                    kept++;
            }
            _fadeCensusScratch.Clear();
            _fadeUnitSubtreeCount[node] = kept;
            return kept;
        }

        /// <summary>PASS 1 of <see cref="BuildFadeUnits"/>: the fade code's own grouping when it
        /// returns one and that group is prop-scale BY THE CENSUS'S OWN COUNT, the renderer's
        /// transform otherwise. Rejecting a container-scale group here is what stops the whole
        /// game board being reported as one half-faded prop — see the doc on
        /// <see cref="BuildFadeUnits"/>.</summary>
        private Transform FadeUnitCandidateOf(Renderer r)
        {
            Transform? root = StandingFloorUnitRootOf(r);
            if (root != null && CensusRendererCount(root) <= PropUnitMaxRenderers)
                return root;
            return r.transform;
        }

        /// <summary>PASS 2 of <see cref="BuildFadeUnits"/>: the HIGHEST candidate root above
        /// <paramref name="cand"/>, or null. Bounded and guarded exactly as
        /// <see cref="PropUnitRootOf"/> is — <see cref="PropUnitMaxDepth"/> levels, stopping dead
        /// at a segment anchor and at a wall entity — so no amount of merging can ever pull a wall
        /// into a prop unit. Only nodes that are ALREADY candidate roots are merge targets, and
        /// those have passed the container-scale test in pass 1, so the merged unit cannot be
        /// container-scale either.</summary>
        private Transform? HighestCandidateRootAbove(Transform cand)
        {
            Transform? best = null;
            Transform? node = cand.parent;
            for (int depth = 0; node != null && depth < PropUnitMaxDepth; depth++, node = node.parent)
            {
                if (_live.PropUnitAnchors.Contains(node) || NodeIsWallEntity(node))
                    break;
                if (_fadeUnitCandidateSet.Contains(node))
                    best = node;
            }
            return best;
        }

        /// <summary>PASS 3 of <see cref="BuildFadeUnits"/>: follow the merge links to the top.
        /// Composing the per-hop window is what lets a chain deeper than
        /// <see cref="PropUnitMaxDepth"/> still resolve to ONE unit — the wall torch is
        /// <c>distort</c> under <c>p_fire_torch (8)</c> under <c>CR_St_WallTorch_Fire_Orange</c>,
        /// three candidate roots and one prop.</summary>
        private Transform ResolveFadeUnitRoot(Transform cand)
        {
            Transform node = cand;
            for (int hops = 0; hops < FadeUnitMergeMaxHops; hops++)
            {
                if (!_fadeUnitMerge.TryGetValue(node, out Transform? up) || up == null)
                    break;
                node = up;
            }
            return node;
        }

        private FadeUnit RentFadeUnit()
        {
            if (_fadeUnitPool.Count == 0)
                return new FadeUnit();
            FadeUnit u = _fadeUnitPool[_fadeUnitPool.Count - 1];
            _fadeUnitPool.RemoveAt(_fadeUnitPool.Count - 1);
            u.Reset();
            return u;
        }

        /// <summary>Union AABB, renderer count and floor reference for one unit — the same
        /// measurement the standing rule makes, so the two lines cannot disagree about a prop's
        /// size or its foot.</summary>
        private void MeasureFadeUnit(Transform root, FadeUnit unit)
        {
            // ONE selector for the unit's renderers (CollectFadeUnitRenderers) and ONE predicate
            // for which of them count (CountsTowardFadeUnit) — the same two the numerator and the
            // solid enumeration go through. A denominator built from a different population than
            // the numerator is the ModBuild-270 defect; see BuildFadeUnits.
            CollectFadeUnitRenderers(root, unit.NodeScoped, _fadeCensusScratch);
            Bounds union = default;
            bool have = false;
            int kept = 0;
            foreach (Renderer piece in _fadeCensusScratch)
            {
                if (!CountsTowardFadeUnit(piece))
                    continue;
                kept++;
                if (!have) { union = piece.bounds; have = true; }
                else union.Encapsulate(piece.bounds);
            }
            _fadeCensusScratch.Clear();
            unit.TotalRenderers = kept;
            if (!have)
                return;
            unit.MinY = union.min.y;
            unit.MaxY = union.max.y;
            unit.SizeRank = WallStandingProp.SizeRank(union.size.x, union.size.y, union.size.z);
            if (!NearestAnchoredFloorY(union.min.y, out float floorY))
                floorY = 0f;
            unit.FloorY = floorY;
        }

        /// <summary>
        /// Build and emit the line.
        ///
        /// <para>EVERY CAPPED LIST IN HERE STATES ITS OWN TRUNCATION, in the form
        /// <c>named K of N, dropped M</c>, and states it BEFORE the list rather than as a trailing
        /// ellipsis. Three wrong diagnoses in this project came from reading an ellipsis-capped
        /// list as evidence of absence — a truncated list is not absence, and a summary stat is
        /// not the field. There are four such lists: the units shown, the written members of a
        /// unit, the still-solid renderers of a unit, and the split-owner units named in the
        /// header.</para>
        /// </summary>
        private void EmitFadeWriteCensus()
        {
            int torn = 0, ungrouped = 0, split = 0, nodeScoped = 0;
            foreach (FadeUnit u in _fadeUnits)
            {
                if (u.Torn)
                    torn++;
                if (!u.FadeGrouped)
                    ungrouped++;
                if (u.Owners.Length > 0)
                    split++;
                if (u.NodeScoped)
                    nodeScoped++;
            }
            int shown = Mathf.Min(FadeCensusUnitCap, _fadeUnits.Count);
            var rows = new System.Text.StringBuilder();
            for (int i = 0; i < shown; i++)
            {
                FadeUnit u = _fadeUnits[i];
                if (rows.Length > 0)
                    rows.Append(" | ");
                rows.Append(u.Torn ? "TORN " : string.Empty)
                    .Append('\'').Append(u.Label).Append('\'')
                    .Append(FadeUnitGroupingMark(u))
                    .Append(' ').Append(u.DistinctWritten).Append('/').Append(u.TotalRenderers)
                    .Append(" written, unit y[").Append(u.MinY.ToString("0.0")).Append("..")
                    .Append(u.MaxY.ToString("0.0")).Append("] over floor ")
                    .Append(u.FloorY.ToString("0.0")).Append(", widest ")
                    .Append(u.SizeRank.ToString("0.0")).Append(" wu");
                int members = Mathf.Min(FadeCensusMembersPerUnit, u.Written.Count);
                rows.Append(" — WRITTEN named ").Append(members).Append(" of ")
                    .Append(u.Written.Count).Append(", dropped ")
                    .Append(u.Written.Count - members).Append(": ");
                for (int m = 0; m < members; m++)
                {
                    if (m > 0)
                        rows.Append(", ");
                    AppendWrittenRow(rows, u.Written[m], u.FloorY);
                }
                if (u.SolidTotal > 0)
                {
                    rows.Append(" — LEFT SOLID under the same root, named ").Append(u.Solid.Count)
                        .Append(" of ").Append(u.SolidTotal).Append(", dropped ")
                        .Append(u.SolidTotal - u.Solid.Count).Append(": ")
                        .Append(string.Join(", ", u.Solid));
                }
                if (u.Owners.Length > 0)
                {
                    rows.Append(" — TWO OWNERS on independent fades: ").Append(u.Owners)
                        .Append(" (whichever goes first, the other half of this prop survives it)");
                }
            }

            // The split-owner units BY NAME. Until ModBuild 271 this clause was a bare count —
            // it read 49 on 24 of the 114 prints of the ModBuild-270 log and named none of them,
            // because torn artifacts outranked them in the sort and took every printed slot. A
            // number without names is not evidence of anything.
            var splits = new System.Text.StringBuilder();
            int splitNamed = 0;
            foreach (FadeUnit u in _fadeUnits)
            {
                if (u.Owners.Length == 0)
                    continue;
                if (splitNamed >= FadeCensusOwnerSplitNameCap)
                    break;
                if (splits.Length > 0)
                    splits.Append("; ");
                splits.Append('\'').Append(u.Label).Append('\'').Append(FadeUnitGroupingMark(u))
                      .Append(' ').Append(u.DistinctWritten).Append('/').Append(u.TotalRenderers)
                      .Append(" written, owners: ").Append(u.Owners);
                splitNamed++;
            }
            if (splitNamed == 0)
                splits.Append("none");

            VRLog.Info(Name,
                $"FADE WRITE: {_fadeWrites.Count} renderer(s) carry a non-zero wall fade right "
                + $"now, grouped into {_fadeUnits.Count} prop unit(s), {torn} of them TORN — a "
                + $"fade path wrote part of a prop and left the rest solid, which is the shape of "
                + $"the report (user 2026-08-19, skelet.jpg: 'Der Schädel ist immer noch nicht "
                + $"sichtbar' — a skeleton on a deck with its ribcage, arms and legs drawn and its "
                + $"skull gone, against a wall mid-dissolve). "
                + $"RANKED BY DEFECT CLASS since ModBuild 271: split-owner units first, then torn, "
                + $"then SMALLEST first because a skull is small; units named {shown} of "
                + $"{_fadeUnits.Count}, dropped {_fadeUnits.Count - shown}. "
                + $"BOTH TERMS OF EVERY x/y HERE ARE COUNTED OVER ONE POPULATION (ModBuild 271), "
                + $"AND THAT IS THE INVARIANT TO CHECK FIRST IF A NUMBER HERE LOOKS WRONG: every "
                + $"denominator is the renderers of that unit's root — its whole subtree, or the "
                + $"root node alone when the subtree is container-scale — and every numerator is "
                + $"DISTINCT renderers of that same set. No renderer-type filter is applied to "
                + $"either: since ModBuild 266 the fade paths write skinned renderers on "
                + $"provenance ('EN_CR_Hanging_01_Cloth_Post'[skinned→cutoff], 124 accepted rows "
                + $"in the ModBuild-270 log), so a type test would drop from the denominator what "
                + $"the numerator still holds and HIDE a real tear. Expect x/y to satisfy "
                + $"0 <= x <= y; an x > y here means that invariant has been broken again. Until "
                + $"ModBuild 270 the numerator counted writes keyed on ONE node while the "
                + $"denominator counted the whole SUBTREE, so a prop whose every renderer was "
                + $"written still printed TORN: 'CR_GE_Candle_04' 1/2 and 'Candle_Fire_FX_02 (3)' "
                + $"1/1 were the two halves of one candle, on one wall, at one fade, in one print. "
                + $"That artifact was the top five torn offenders of that whole session. "
                + $"Grouping: the fade code's own walk (highest still prop-sized ancestor, stops "
                + $"at any wall entity or segment anchor), plus the census's own merge of "
                + $"descendant writes into the same unit key; {ungrouped} unit(s) were NOT grouped "
                + $"by the fade code's walk and {nodeScoped} were judged node-scoped "
                + $"(container-scale subtree, denominator is the root node only). A missing piece "
                + $"in the NOT-grouped bucket means the tileset parented it flat and the grouping, "
                + $"not the geometry, is what needs widening next — the standing-prop FLOOR arm "
                + $"cannot fire there. "
                + $"{split} unit(s) have TWO OR MORE OWNERS on independent fades — the second way "
                + $"a prop tears, and the one this census could not state until ModBuild 258: "
                + $"'CA_ICY_WallLight' put its ice meshes on 'Wall 4' as wall renderers and its "
                + $"blue torch emitters on 'Wall 1' as mounted dressing, so half of it survived "
                + $"every fade either wall made (wandproblem3.jpg, 'die blaue Flamme ist nun "
                + $"wieder sichtbar ohne dass sie gefaded ist'). The unit-affinity rule in "
                + $"WallSegmentFade.Mounted.cs is meant to hold this at ZERO; here they are, "
                + $"named {splitNamed} of {split}, dropped {split - splitNamed}: {splits}. "
                + $"A write on the 'prop-unit dressing' path is the ModBuild-259 arm: a member "
                + $"with no wall-fade channel that the prop-unit pass gave one to rather than "
                + $"leaving it standing (the ModBuild-258 line's '106 left visible'). "
                + $"Anchor = AABB min.y over the nearest anchored room floor, the same anchor the "
                + $"mounted census prints. {rows}");
        }

        /// <summary>How many still-solid renderers the DEFAULT-tier line names. Four, not the
        /// twelve the debug census names: this line is printed at <c>VRLog.Note</c> and a hardware
        /// round has to be able to read it in a Player.log, not scroll past it. The full list, with
        /// the same per-piece term, is on the FADE WRITE line one tier down.</summary>
        private const int SolidBlockerNameCap = 4;

        /// <summary>
        /// SOLID BLOCKER — the pillar report's answer, at a tier the shipped default prints.
        ///
        /// <para>WHY IT IS A SEPARATE LINE AND NOT A PROMOTION. ModBuild 331 moved
        /// <c>VRLog.Info</c> to the DEBUG tier, so the FADE WRITE census — the line that carries
        /// this evidence — is invisible in a default Player.log. Promoting THAT line is not an
        /// option: it is kilobytes wide by design. So the verdict gets its own line, short enough
        /// to survive the default level, and it carries NAMES rather than a count, because a
        /// summary stat is not the field.</para>
        ///
        /// <para>WHAT IT ANSWERS. For every prop unit this frame's fade paths wrote PART of, it
        /// names the renderers that stayed solid and, for each, the term that keeps it:
        /// <c>[OWNER SOLID]</c> (a wall segment owns it and that segment is at fade 0.00 — the
        /// question is a coverage DECISION, and the PER-WALL line for that named segment has the
        /// numbers), <c>[UNOWNED]</c> (no segment owns it — the question is ADOPTION, and the
        /// LEFTOVER OVER A FADED WALL line has the reject reason), or
        /// <c>[OWNER FADING, THIS PIECE UNWRITTEN]</c> (the owner is fading and the write did not
        /// reach this renderer — a DELIVERY question, and the DISSOLVE CENSUS has the channel).
        /// Three arms, three different next lines to read, and until now the log could name the
        /// renderer and none of the three.</para>
        ///
        /// <para>IT ASSERTS NOTHING IT CANNOT SEE. Owner and fade are read back off the live
        /// segment table after the frame's Apply pass. The line does not say why an owner decided
        /// solid, and must not be edited into saying so.</para>
        ///
        /// <para>CADENCE. Behind the census's own 2 s gate AND its change-trigger signature, and
        /// gated again on there being at least one torn unit — so a session with nothing to report
        /// prints nothing at all. In the ModBuild-380 log the census printed 68 times in about
        /// twenty minutes.</para>
        ///
        /// <para>MULTIPLAYER: a diagnostic. It writes no renderer, no material, no segment and no
        /// wire record; suppressing it cannot change a pixel on any peer.</para>
        /// </summary>
        private void EmitSolidBlockerLine()
        {
            // ONE POPULATION, as everywhere else in this census: every unit in _fadeUnits was
            // built FROM a write, so a unit with SolidTotal > 0 is by construction a unit some
            // path wrote and some path did not. The three tallies below are counted over exactly
            // these units' solid pieces — not a wider set and not a narrower one.
            int tornUnits = 0, solidPieces = 0, namedAvailable = 0;
            _solidBlockerOrder.Clear();
            for (int i = 0; i < _fadeUnits.Count; i++)
            {
                FadeUnit u = _fadeUnits[i];
                if (u.SolidTotal <= 0)
                    continue;
                tornUnits++;
                solidPieces += u.SolidTotal;
                namedAvailable += u.Solid.Count;
                _solidBlockerOrder.Add(i);
            }
            if (tornUnits == 0)
                return;

            // LARGEST UNIT FIRST, and deliberately the OPPOSITE end from the debug census. That
            // line sorts SMALLEST first because the defect it was built for is a skull; this one
            // exists for a 3.5 wu pillar, and a four-name cap applied to a smallest-first list
            // would drop exactly the renderer the report is about. A truncated list is not
            // absence — so the two lines truncate from opposite ends and each says which.
            // Insertion sort over tens of entries, on a frame that is already printing.
            for (int i = 1; i < _solidBlockerOrder.Count; i++)
            {
                int key = _solidBlockerOrder[i];
                int j = i - 1;
                while (j >= 0 && _fadeUnits[_solidBlockerOrder[j]].SizeRank
                                 < _fadeUnits[key].SizeRank)
                {
                    _solidBlockerOrder[j + 1] = _solidBlockerOrder[j];
                    j--;
                }
                _solidBlockerOrder[j + 1] = key;
            }

            var named = new System.Text.StringBuilder();
            int shown = 0;
            foreach (int idx in _solidBlockerOrder)
            {
                FadeUnit u = _fadeUnits[idx];
                for (int i = 0; i < u.Solid.Count && shown < SolidBlockerNameCap; i++, shown++)
                {
                    if (named.Length > 0)
                        named.Append(" | ");
                    named.Append('\'').Append(u.Label).Append("' ")
                         .Append(u.DistinctWritten).Append('/').Append(u.TotalRenderers)
                         .Append(" written, y[").Append(u.MinY.ToString("0.0")).Append("..")
                         .Append(u.MaxY.ToString("0.0")).Append("] over floor ")
                         .Append(u.FloorY.ToString("0.0")).Append(", widest ")
                         .Append(u.SizeRank.ToString("0.0")).Append(" wu: ").Append(u.Solid[i]);
                }
                if (shown >= SolidBlockerNameCap)
                    break;
            }

            // HW-VERIFY
            VRLog.Note(Name,
                $"SOLID BLOCKER: {solidPieces} renderer(s) stayed SOLID inside {tornUnits} prop "
                + $"unit(s) whose OTHER renderers this mod faded this pass — the shape of the "
                + $"pillar report (user 2026-09-03, säulen.jpg: 'faden die Säulen nicht, "
                + $"obwohl sie die Sicht versperren'). BY THE TERM THAT KEEPS EACH ONE, counted "
                + $"over ALL of them and not over the named subset: {_solidOwnerSolid} "
                + $"[OWNER SOLID] — a wall segment DOES own it and that segment is at fade 0.00, "
                + $"so this is a coverage DECISION and the PER-WALL line for the named segment "
                + $"carries its numbers; {_solidUnowned} [UNOWNED] — no wall segment owns it at "
                + $"all, so this is ADOPTION and the LEFTOVER OVER A FADED WALL line carries the "
                + $"reject reason; {_solidOwnerFading} [OWNER FADING, THIS PIECE UNWRITTEN] — the "
                + $"owner is mid-fade and the write did not reach this renderer, so this is "
                + $"DELIVERY and the DISSOLVE CENSUS carries the channel. Three arms, three "
                + $"different next lines. WIDEST UNIT FIRST — the debug census names smallest "
                + $"first (a skull is small) and this line names widest first (a pillar is not), "
                + $"so the two truncate from opposite ends. Named {shown} of {namedAvailable} "
                + $"nameable "
                + $"({solidPieces} solid in total; the FADE WRITE census one tier down names six "
                + $"per unit and carries the same per-piece term for every one of them), "
                + $"dropped {namedAvailable - shown}: {named}");
        }

        /// <summary>How this unit came to be one unit — printed on every row, because "1 of 2
        /// written" means three different things depending on it.</summary>
        private static string FadeUnitGroupingMark(FadeUnit u)
        {
            if (u.NodeScoped)
                return "[node-scoped — container-scale subtree, denominator is the root node]";
            if (u.FadeGrouped)
                return string.Empty;
            return u.TotalRenderers > 1
                ? "[no prop unit — subtree grouped by the census]"
                : "[no prop unit — a unit of one]";
        }

        /// <summary>One written renderer, with everything needed to identify it in a scene nobody
        /// on the build machine can open: name, type, ancestry, foot height over its room's floor,
        /// AABB, the path that wrote it and the fade it is carrying.</summary>
        private static void AppendWrittenRow(System.Text.StringBuilder sb, FadeWrite w, float floorY)
        {
            if (w.R == null)
            {
                sb.Append("<destroyed mid-frame>");
                return;
            }
            Bounds b = w.R.bounds;
            sb.Append('\'').Append(w.R.name).Append("'[").Append(RendererKind(w.R))
              .Append("] under '").Append(AncestorPath(w.R.transform))
              .Append("' anchor ").Append((b.min.y - floorY).ToString("0.00"))
              .Append(" over floor, AABB c(")
              .Append(b.center.x.ToString("0.0")).Append(',')
              .Append(b.center.y.ToString("0.0")).Append(',')
              .Append(b.center.z.ToString("0.0")).Append(") s(")
              .Append(b.size.x.ToString("0.0")).Append(',')
              .Append(b.size.y.ToString("0.0")).Append(',')
              .Append(b.size.z.ToString("0.0")).Append(") ← ").Append(w.Path)
              .Append(" of '").Append(w.Owner).Append("' fade ")
              .Append(w.Fade.ToString("0.00"));
        }

        /// <summary>A few levels of ancestor path, top-down, for a renderer — enough to tell two
        /// instances of the same asset apart and to see what a prop hangs under, without printing
        /// a path to the scene root for every row.</summary>
        private static string AncestorPath(Transform t)
        {
            var parts = new List<string>(4);
            Transform? node = t.parent;
            for (int i = 0; node != null && i < 4; i++)
            {
                parts.Add(node.name);
                node = node.parent;
            }
            parts.Reverse();
            return parts.Count == 0 ? "<scene root>" : string.Join("/", parts);
        }

        // ---- SHOW EDGE AUDIT (ModBuild 261) --------------------------------------------------

        /// <summary>
        /// WHAT A PIECE LOOKS LIKE ON THE FRAME IT BECOMES VISIBLE AGAIN — read off the RENDERER
        /// and its property block, never off the ledger that decided it.
        ///
        /// <para>WHY IT IS BUILT THIS WAY. ModBuild 252 shipped an instrument reporting "every
        /// transition is animated end to end" while the dissolve was a one-frame switch, because
        /// it watched the DRIVER. The ModBuild-260 DISSOLVE CENSUS did the same thing in the
        /// other direction: it named four pieces as poppers that were dissolving, and captioned
        /// them with an un-fade-edge evaluation that the same log's STEP totals
        /// (<c>0 material swap</c>, <c>0 swap removed</c>) prove never ran. So this audit asks
        /// only questions whose answer is a property of what will be SAMPLED this frame:</para>
        /// <list type="number">
        /// <item>MATERIAL — is the renderer wearing our dissolve-swap copies rather than its
        ///   authored <c>sharedMaterials</c>? A swap copy is a different shader family and cannot
        ///   look like the authored asset (that is the whole of the ModBuild 255 foliage
        ///   ruling).</item>
        /// <item>CLIP VALUE — read back with <c>Renderer.GetPropertyBlock</c>, i.e. the number the
        ///   shader will actually clip against. Texture alpha never exceeds 1, so a piece shown
        ///   with <c>_Cutoff ≥ 1</c> is shown discarding every texel it has: drawn, paid for, and
        ///   contributing nothing. That is not a dissolve, it is an invisible frame with the
        ///   piece's OTHER slots still opaque — a half-drawn object.</item>
        /// <item>TORN RETURN — did the rest of this piece's PROP UNIT come back at a different
        ///   fade? Both halves are read from the renderers, so this term can contradict
        ///   <see cref="StaggerThresholdFor"/> and is the falsifier for it. Since ModBuild 261 it
        ///   sees BOTH stagger-keyed lists — <c>seg.Foliage</c> and <c>seg.UnitDressing</c> — and
        ///   the foliage half is the one that was previously unwatched. This is the shape of
        ///   the report (user 2026-08-24, <c>wände_problem4.mp4</c>: a fir standing as bare twigs
        ///   from t = 25.10 s and its whole needle crown appearing in the single frame between
        ///   t = 25.517 s and t = 25.533 s).</item>
        /// </list>
        ///
        /// <para>COST: nothing per frame. The three tests run only on the <c>false → true</c>
        /// edge of a piece's drawing state, which happens once per piece per fade episode; the
        /// property-block read-back is one managed call on that frame only. The line itself is
        /// rate-limited and prints a healthy result exactly once after it becomes healthy.</para>
        ///
        /// <para>MULTIPLAYER: diagnostic only — it writes no renderer, no material and no wire
        /// record.</para>
        /// </summary>
        private const float ShowEdgeAuditIntervalSeconds = 5f;

        /// <summary>How many offenders one SHOW EDGE line names.</summary>
        private const int ShowEdgeNameCap = 8;

        /// <summary>Two members of the same prop unit returning within this much fade of each
        /// other count as returning TOGETHER. One frame of a ramp moves the fade by well under
        /// this, so it forgives frame granularity and nothing else.</summary>
        private const float ShowEdgeSameReturn = 0.05f;

        /// <summary>A unit's return is one episode as long as its members keep arriving inside
        /// this window; a later arrival starts a new one.</summary>
        private const float ShowEdgeEpisodeSeconds = 1f;

        private MaterialPropertyBlock? _showEdgeMpb;
        private readonly List<string> _showEdgeNames = new(ShowEdgeNameCap);
        private readonly Dictionary<Transform, Vector2> _showEdgeUnitReturn = new(32);

        /// <summary>Scratch for the episode-window prune below. ModBuild 261: with the foliage
        /// list now reporting here too, the key set is every prop root in the scene rather than a
        /// handful of dressed units, and Apparance replaces those constantly — an unpruned
        /// Transform-keyed table would grow all session and compare a live prop against a
        /// destroyed one. Cleared on teardown as well (WallSegmentFade.Mounted.cs).</summary>
        private readonly List<Transform> _showEdgeStale = new(32);
        private int _showEdgeTotal;
        private int _showEdgeSwapped;
        private int _showEdgeBlankClip;
        private int _showEdgeTorn;
        private int _showEdgeLastReported = -1;
        private float _nextShowEdgeAudit;

        /// <summary>Stagger lookups since the last SHOW EDGE line that found NO entry for the
        /// renderer's parent in <c>_live.PropUnitRootMemo</c>. See
        /// <see cref="FadeDriver.StaggerThresholdFor"/>: that is the only remaining state in which
        /// two members of one prop can key differently, and it can only arise if an applier
        /// stagger-keys a list <c>WarmStaggerKeys</c> does not walk. This counter is the
        /// mechanism that catches such an edit; the comment there is not.</summary>
        private int _staggerKeyMiss;
        private string? _staggerKeyMissFirst;

        private void NoteStaggerKeyMiss(Renderer r)
        {
            _staggerKeyMiss++;
            _staggerKeyMissFirst ??= r.name;
        }

        /// <summary>Track one piece's drawing state and audit the frame it turns back on.</summary>
        private void ShowEdge(MountedProp p, bool drawing, float fade)
        {
            if (drawing && !p.WasDrawing)
                AuditShowEdge(p, fade);
            p.WasDrawing = drawing;
            if (!drawing)
                p.ShownAtFade = -1f;
        }

        private void AuditShowEdge(MountedProp p, float fade)
        {
            Renderer? r = p.Renderer;
            if (r == null)
                return;
            _showEdgeTotal++;
            p.ShownAtFade = fade;
            string? fault = null;

            // 1. MATERIAL — what this renderer will be drawn WITH, asked of the renderer.
            if (p.SwapCopies != null)
            {
                _showEdgeSwapped++;
                fault = "shown wearing our dissolve-swap copies, not its authored materials";
            }

            // 2. CLIP VALUE — read back the block that will be sampled this frame.
            if (fault == null && p.CutoffId >= 0)
            {
                _showEdgeMpb ??= new MaterialPropertyBlock();
                _showEdgeMpb.Clear();
                r.GetPropertyBlock(_showEdgeMpb);
                float clip = _showEdgeMpb.GetFloat(p.CutoffId);
                if (clip >= 1f)
                {
                    _showEdgeBlankClip++;
                    fault = $"shown with _Cutoff {clip:F2} (authored {p.BaseCutoff:F2}) — texture "
                        + "alpha never exceeds 1, so every texel of this renderer is discarded "
                        + "while its opaque slots keep drawing";
                }
            }

            // 3. TORN RETURN — did the rest of the prop come back at a different fade?
            // ModBuild 261: grouped by the SAME lookup the stagger rule keys on
            // (StaggerRootOf, an O(1) memo read) rather than by a live PropUnitRootOf climb. Two
            // reasons: this runs on an edge frame OUTSIDE the commit, where the per-node fact
            // memos are shut and every climb is a full subtree walk — and with the foliage list
            // now reporting here too that is hundreds of walks on one frame; and the term only
            // means "the pieces the rule kept together did not arrive together" if it groups the
            // way the rule groups. It stays a falsifier: both fades below are read off the
            // renderers on their own edge frames, never off the threshold that placed them.
            Transform? root = StaggerRootOf(r, out _);
            if (root != null)
            {
                float now = Time.unscaledTime;
                if (_showEdgeUnitReturn.TryGetValue(root, out Vector2 prev)
                    && now - prev.y <= ShowEdgeEpisodeSeconds)
                {
                    if (Mathf.Abs(prev.x - fade) > ShowEdgeSameReturn)
                    {
                        _showEdgeTorn++;
                        fault ??= $"its prop unit '{root.name}' returned IN PIECES — an earlier "
                            + $"member came back at fade {prev.x:F2}, this one at {fade:F2}";
                    }
                }
                else
                {
                    _showEdgeUnitReturn[root] = new Vector2(fade, now);
                }
            }

            if (fault != null && _showEdgeNames.Count < ShowEdgeNameCap)
                _showEdgeNames.Add($"'{r.name}' [{p.Tier}] at fade {fade:F2}: {fault}");
        }

        private void EmitShowEdgeAudit(float now)
        {
            if (now < _nextShowEdgeAudit)
                return;
            _nextShowEdgeAudit = now + ShowEdgeAuditIntervalSeconds;

            // Drop episode records that can no longer group anything. The window is the existing
            // ShowEdgeEpisodeSeconds — no new number — so this removes exactly what AuditShowEdge
            // would already have ignored, and nothing it would have read. It is also what bounds
            // the table: an Apparance-destroyed root stops being written to and leaves within one
            // audit interval, so no dangling Transform is held for longer than that.
            _showEdgeStale.Clear();
            foreach (KeyValuePair<Transform, Vector2> kv in _showEdgeUnitReturn)
            {
                if (now - kv.Value.y > ShowEdgeEpisodeSeconds)
                    _showEdgeStale.Add(kv.Key);
            }
            foreach (Transform t in _showEdgeStale)
                _showEdgeUnitReturn.Remove(t);
            _showEdgeStale.Clear();

            int faults = _showEdgeSwapped + _showEdgeBlankClip + _showEdgeTorn;
            if (faults == 0 && _staggerKeyMiss == 0 && _showEdgeLastReported == 0)
            {
                _showEdgeTotal = 0;
                _showEdgeNames.Clear();
                return; // healthy, and the line already said so once
            }
            _showEdgeLastReported = faults + _staggerKeyMiss;
            string keys = _staggerKeyMiss > 0
                ? $" STAGGER KEY MISS [ALARM]: {_staggerKeyMiss} lookup(s) found no prop-unit "
                  + $"root memo entry for their parent (first '{_staggerKeyMissFirst}'). "
                  + "WarmStaggerKeys() resolves every parent in seg.Foliage and seg.UnitDressing "
                  + "at the end of the PropUnits commit phase, so non-zero means an applier now "
                  + "stagger-keys a list that pass does not walk, and the two halves of one prop "
                  + "can key differently again — which is the ModBuild-261 tear."
                : " Stagger keys: 0 memo misses, so every piece keyed on its prop unit.";
            string detail = faults > 0
                ? " — " + string.Join("; ", _showEdgeNames)
                  + (faults > _showEdgeNames.Count ? "; …" : string.Empty)
                : " — every piece that came back came back as authored.";
            VRLog.Info(Name,
                $"SHOW EDGE: {_showEdgeTotal} piece(s) became visible again since the last line, "
                + $"{faults} of them NOT as authored ({_showEdgeSwapped} wearing swap copies, "
                + $"{_showEdgeBlankClip} shown with a clip value that discards every texel, "
                + $"{_showEdgeTorn} whose prop unit returned in pieces). Every term is read off "
                + "the RENDERER and its property block on the edge frame — the material it will "
                + "be drawn with and the number the shader will clip against — never off the "
                + "ledger that decided it (ModBuild 252 shipped a driver-side claim that "
                + "contradicted the picture; ModBuild 260's DISSOLVE CENSUS named four dissolving "
                + "pieces as poppers and blamed an un-fade-edge evaluation its own STEP totals "
                + "show never ran). ZERO is the acceptance bar for the ModBuild-261 report "
                + "(wände_problem4.mp4, the fir's crown appearing in one frame at 25.53s). "
                + "MODBUILD 265 — the blank-clip term is what the user's \"1s undefinierter "
                + "Matsch an den Ästen\" is: 123 of 278 pieces here were shown at fade 0.82-0.92 "
                + "with _Cutoff 1.07-1.20 against an authored 0.50, i.e. on the first frames of "
                + "an un-fade, drawing nothing, and resolved out of nothing over the next 0.6s. "
                + "A piece we hid is now held disabled until its PROP UNIT's stagger fade and "
                + "written with its authored value on the frame it is turned on "
                + "(FadeDriver.ShowAttachmentPiece). So a blank-clip residue can no longer be a "
                + "held piece returning: read the FADES below — first-frame-of-un-fade fades "
                + "would falsify the rule, any other fade is a piece adopted mid-return that the "
                + $"rule never held, which is a different defect.{keys}{detail}");
            _showEdgeNames.Clear();
            _showEdgeSwapped = 0;
            _showEdgeBlankClip = 0;
            _showEdgeTorn = 0;
            _showEdgeTotal = 0;
            _staggerKeyMiss = 0;
            _staggerKeyMissFirst = null;
        }
    }
}
