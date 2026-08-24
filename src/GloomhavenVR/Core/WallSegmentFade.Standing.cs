using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// A PROP THAT STANDS ON THE FLOOR IS NEVER WALL GEOMETRY — the decapitated skeleton, fourth
/// round (user report 2026-08-19, hardware, ModBuild 167, verbatim): <i>"Der Schädel ist immer
/// noch nicht sichtbar."</i> The original report (2026-08-15) reads: <i>"In der Map die wir
/// gespielt haben wurde der Kopf eines Skelets mit der Wand mit ausgeblendet. Füge das auf die
/// Ausnahmeliste hinzu, dass er richtig dargestellt wird."</i>
///
/// <para>THE PHOTOGRAPH, <c>.planning/debug/skelet.jpg</c>, read off the image. A full human
/// skeleton is slumped on a WOODEN DECK at floor level, leaning back against a low masonry wall,
/// legs splayed forward across the planks, burning hexes and scattered parchment around it.
/// Ribcage, clavicles, both arms, pelvis and both legs are solid and fully drawn. THE SKULL IS
/// GONE — above the ribcage there is a stub of vertebrae and nothing else. The low wall directly
/// behind it is MID-DISSOLVE: its top edge carries the ragged noise pattern of the masonry fade.
/// A floor-level prop, one renderer of which vanished with a fading wall.</para>
///
/// <para>WHAT THIS ROUND CHANGES, and why the previous three did not cure it. The rule below used
/// to print its own three terms into the log — <c>STANDING PROP: … Rule: figure/actor ancestry AND
/// the unit's union AABB reaches within 1.0 wu of its room's floor AND the unit is prop-sized (span
/// ≤ 6.0 wu, ≤ 24 renderers) — wall-MOUNTED dressing never reaches the floor and keeps fading with
/// its masonry.</c> A scenery skeleton lying on a deck carries no <c>Animator</c>, no
/// <c>ActorBehaviour</c> and no <c>CInteractableActor</c>, so the FIRST term refused it however
/// well the other two fitted; and that term was doing no work the others were not, because the
/// line's own justification for the geometric test is the discriminator that matters. So the rule
/// now has TWO ARMS: the shipped FIGURE arm, bit-for-bit, and a FLOOR arm that asks only for
/// geometry. The arithmetic of both is <see cref="WallStandingProp"/> — Unity-free, wire-tested
/// against the case in the photograph.</para>
///
/// <para>WHY A HEIGHT CAP HAD TO COME WITH THE WIDENING, measured rather than argued. A wall
/// course stands on the floor too, and without a vertical term the floor arm would protect one —
/// which does not look like a bug, it looks like wall see-through being switched off. The
/// ModBuild-167 log prints the AABB top of every renderer a wall fades (<c>fade ON … 87
/// toggle-native: Blocks@2.8, Pillar@3.3, Wall@3.3, WallTop@3.4, CR_OS_Pillar_Large_03@3.7 …</c>):
/// across that whole session the lowest-topped piece of masonry reaches 2.8 wu, while the tallest
/// dressing in the same lists (<c>CR_ST_Shelf_Books_Sparse_02@2.5</c>, <c>EN_CR_LBSkull@2.4</c>,
/// <c>SB_AncCaverns_DemonHead@1.9</c>, <c>CR_ST_Shelf_Alchemy_Balance@1.3</c>,
/// <c>EN_CR_Candle_06@1.2</c>) tops out at 2.5. See <see cref="WallStandingProp.MaxHeightWU"/>.</para>
///
/// <para>WHAT A UNIT IS, and this is why a lone skull is not judged airborne. The FIGURE arm keeps
/// the nearest figure/actor ancestor (<see cref="FadeDriver.FigurePropRootOf"/>). The FLOOR arm
/// uses ModBuild 167's grouping — <see cref="FadeDriver.PropUnitRootOf"/>, the HIGHEST still
/// prop-sized ancestor, whose walk stops dead at a segment anchor, at anything carrying or
/// containing a <c>ProceduralWall</c>, and at the first container-scale subtree. A renderer with no
/// such unit is not judged by the floor arm at all, which is the answer for the overwhelming
/// majority of wall masonry (it hangs directly off its wall entity) and is the cheap path. The
/// verdict is computed for the UNIT and applied to every renderer under it: the skeleton's skull,
/// ribcage and legs get one answer, so no viewing angle can split them again.</para>
///
/// <para>NOW ALSO ON THE MOUNTED PATH, and without that the floor arm would be theatre. Until this
/// round the guard was consulted by <see cref="FadeDriver.CollectWallFadeInfo"/> (the one choke
/// point of every wall-renderer collection) and by the two foliage paths. A renderer refused there
/// is unclaimed — and <see cref="FadeDriver.CollectWallMountedProps"/> runs afterwards, is purely
/// geometric, and would have adopted the skull as sconce dressing and faded it anyway. The FIGURE
/// arm never noticed because that sweep has its own figure guard; the FLOOR arm would have. The
/// mounted rule itself is unchanged and keeps printing: a prop whose UNIT hangs a metre over the
/// floor is not a unit that reaches the floor, so nothing it protects can meet this.</para>
///
/// <para>THE THIRD ARM IS GONE. ModBuild 257 added a FREE-STANDING TREE arm here (vegetation AND
/// ≥ 2.0 h/w lifts the height cap) and ModBuild 258 retired it against the next hardware log. The
/// numbers and the reasoning are the tombstone in <see cref="WallStandingProp"/>; the one-line
/// version is that it fired on exactly ONE unit in the whole session, that unit was
/// <c>'PCG_FR_Wall_Space_04_PR' [h 4.5 wu / w 2.1 wu = 2.11 h/w, vegetation]</c> — a WALL — and it
/// then appeared in no <c>FADE WRITE</c> line at all, i.e. it never faded once. That is
/// <c>wandproblem3.jpg</c>: <i>"ein Teil der Wand bleibt nun stehen und faded garnicht mehr"</i>.
/// Membership is back to the two arms above. Do not re-derive a shape term here.</para>
///
/// <para>WHAT REPLACED IT, ModBuild 258 — A UNIT FADES WHOLE OR NOT AT ALL. The same log's
/// <c>FADE WRITE</c> census reads <c>grouped into 85 prop unit(s), 49 of them TORN</c>, and one
/// unit accounts for 105 of those lines:
/// <c>TORN 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' 4/6 written … LEFT SOLID under the same
/// root: FR_Stones_06 (1), FR_Stones_02 (2)</c>. This file's FLOOR arm has already judged that
/// unit — the <c>NEAR MISS</c> line reads <c>height 3.0 wu — architecture, not a floor prop</c> —
/// so the mod is fading it as a wall while a PER-RENDERER ground band holds its two base stones
/// solid. One verdict per unit, applied to every renderer under it, is the whole rule; the recruit
/// that carries it out lives in <c>WallSegmentFade.PropUnit.cs</c>
/// (<see cref="FadeDriver.PropUnitRecruit"/>), because that is the pass that already owns "one
/// unit, one owner".</para>
///
/// <para>MODBUILD 266 — THE RULE WAS JUDGING A FRAGMENT OF A WALL. User report 2026-08-25,
/// hardware, ModBuild 265: <i>"In dem Level gibt es Bücherregale die als Wandersatz dienen …
/// faden aber nicht in den Tests"</i> and <i>"Noch eine Wandhalterung/Regal/Brett faded nicht mit,
/// obwohl es an der Wand hängt"</i>. The same log measures the SAME shelf prefab at two different
/// unit roots — <c>TORN 'PCG_Test_Feature_Small_2' 19/21 written, unit y[0.0..3.4]</c> (the whole
/// wall feature: a 2.8 wu masonry block at anchor 0.00, a pillar capital, the shelf bracketed to
/// it; refused as architecture, and it fades) versus
/// <c>'CR_ST_Shelves_Stone_Wood'[shared ancestor] 2 renderer(s)</c> (the shelf alone, y[0.9..1.3],
/// 0.4 wu tall; accepted as a floor prop, and it is the leftover in the photographs). No number
/// here was wrong. The unit was. The term added for it — a unit whose own walk passed a WALL
/// inside its bounded window is a FRAGMENT of that wall's feature, not a prop standing in the
/// room — lives in <see cref="WallStandingProp"/> with the numbers and the falsifier; read that
/// header before touching anything here. The FIGURE arm is untouched, and so is every
/// constant.</para>
///
/// <para>WHAT THIS ROUND DELIBERATELY DOES <b>NOT</b> FIX, because the honest boundary is worth
/// more than an over-reach. <c>'EN_CR_Curtain_Mesh'</c> (151 leftovers) and
/// <c>'CR_BT_BanditBanner_Wall'</c> (305) are wall dressing held solid over faded walls by the
/// <b>FIGURE</b> arm, not by the floor arm — the ModBuild-265 census reads
/// <c>'EN_CR_Curtain_Cloth' foot 0.3 wu … 2 renderer(s) — figure/actor prop</c>. Widening the
/// figure arm is the round-7 ruling's territory (figures are NEVER touched, Lights-rule severity)
/// and it is the flags lane's <c>IsWallGeneratedDressing</c>, which carries an
/// <c>ActorBehaviour</c>/<c>CInteractableActor</c> veto for exactly that reason. Nothing here
/// touches it.</para>
///
/// <para>THE STACKED-SHELL PASS IS DELIBERATELY LEFT ALONE. Its own admission test requires a
/// piece's base to sit at or above the wall's ORIGINAL course top minus 1.2 wu — floor-band
/// geometry cannot satisfy that, so adding a second guard there would be a line of code that can
/// never change an outcome, and the next reader would have to prove that again.</para>
///
/// <para>THE EARLIER ROUNDS' EVIDENCE IS KEPT BELOW rather than deleted, because it is what stops
/// the next round from re-fixing them. ModBuild 157 read the crypt ossuary statue out of a hardware
/// log as FOUR sibling MeshRenderers under one root (<c>CR_OS_Skeleton_Statue</c> → <c>_Body</c>,
/// <c>_Skull</c>, <c>_Sword</c>, <c>_Broken</c>) and found the hole that let a wall's OWN renderer
/// collection fade one of them: <c>LogOutput.log</c> — <c>TOGGLE-NATIVE MATERIAL
/// 'EN_Crypt_Ossary_Skellington' (shader 'Amp_Basic_N_MRAO'): authored _WallFade_On=1, …
/// keywords=[_WALLFADE_ON_ON]</c>, so the TOGGLE test accepted it while the slot-0 shader the
/// census prints is <c>Amp_Basic_Prop_Shader</c> — a shader-NAME glance misses it entirely.
/// <see cref="FadeDriver.IsFigureOrActorRenderer"/> was consulted by the ADOPTION sweeps only; a
/// wall's own renderer collection never asked it. That hole is real and stays closed. It is simply
/// not the photograph: in the ModBuild-167 log those renderers are anchored at 3.5 and 5.1 wu above
/// the floor and their owners agree, and the prop in the picture sits on a deck.</para>
///
/// <para>WHY A BLANKET FIGURE GUARD IS STILL WRONG (ModBuild 157's measurement, still true).
/// Cross-referencing the session's <c>FIGURE-GUARD</c> name set against the renderers that appear
/// in <c>fade ON</c> lines, <c>CR_ST_WallShelf_Stone_Bone</c> and
/// <c>CR_ST_WallShelf_Stone_Bone_Skull</c> are in BOTH — wall shelves carrying an Animator ancestor
/// that fade correctly with the masonry they hang on. A guard without the floor term would leave
/// them in mid-air when their wall goes: the fountain-basin defect with the stonework and the prop
/// swapped.</para>
///
/// <para>DIAGNOSTIC. The <c>STANDING PROP</c> census names every protected unit with the
/// measurements the verdict was made on, which ARM protected it, and every wall-renderer claim it
/// blocked this rescan. It now also names the NEAR MISSES — units whose foot IS in the floor band
/// but that failed another term, with the number they failed on — because "the rule did not fire"
/// without "and by which number" is exactly what cost this defect its fourth round. The other half
/// of that answer is the <c>FADE WRITE</c> census in <c>WallSegmentFade.FadeCensus.cs</c>, which
/// names every renderer any fade path actually wrote.</para>
///
/// <para>NOT THE PARKED PROBLEM. <c>.planning/wall-fade-stereo-rivalry.md</c> is about the masonry
/// shader discarding the SAME wall in one eye and not the other — a per-eye disagreement on a
/// renderer the fade legitimately owns. This is a per-RENDERER ownership defect: the skull was
/// faded in both eyes, correctly and consistently, by a wall that should never have owned it.
/// Nothing here touches the discard math, the vignette, <c>_Cutoff</c> or the dissolve.</para>
///
/// <para>MULTIPLAYER: purely local. This only removes renderers from a LOCAL wall's own lists; it
/// produces no decision, no wire record and no peer-visible state, and every peer runs the
/// identical geometric rule against the identical scene.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>How far above its room's floor a unit's LOWEST point may sit and still count as
        /// standing on that floor. One definition, in <see cref="WallStandingProp"/>, because the
        /// census prints it and the wire tests drive it.</summary>
        private const float StandingPropFootBandWU = WallStandingProp.FootBandWU;

        /// <summary>Largest horizontal extent a unit may have and still be a PROP — the
        /// anti-catastrophe cap (see <see cref="WallStandingProp"/>).</summary>
        private const float StandingPropMaxSpanWU = WallStandingProp.MaxSpanWU;

        /// <summary>Most renderers a unit may own and still be a PROP — the second half of the same
        /// cap.</summary>
        private const int StandingPropMaxRenderers = WallStandingProp.MaxRenderers;

        /// <summary>Tallest a unit may be and still be a floor-standing prop rather than a course of
        /// masonry — the FLOOR arm only. Without it that arm protects walls.</summary>
        private const float StandingPropMaxHeightWU = WallStandingProp.MaxHeightWU;

        /// <summary>One unit's measured extent for this rescan, memoised by its root so the union
        /// is computed once and reused by every renderer under it.</summary>
        private readonly struct StandingMeasure
        {
            internal readonly bool Ok;
            internal readonly WallStandingProp.Unit Unit;
            internal readonly float FloorY;
            /// <summary>Any renderer under the unit root on a Foliage-family shader — the TREE
            /// arm's second term, measured over the WHOLE unit so a bark trunk counts as
            /// vegetation when its own canopy is part of the same prop.</summary>
            internal readonly bool Vegetation;

            /// <summary>MODBUILD 266 — the unit walk passed a WALL ENTITY inside its own bounded
            /// window, i.e. this unit is a FRAGMENT of a wall's feature. See
            /// <see cref="FadeDriver.StandingFloorUnitRootOf"/>.</summary>
            internal readonly bool WallCut;

            /// <summary>MODBUILD 266, REPORTED ONLY and deciding nothing. Does any member of the
            /// unit carry an authored wall-fade channel (a WallFade-family shader NAME or a live
            /// <c>_WallFade_On</c>/<c>_ToggleWallfade</c>/<c>_ToggleWallFadeLocal</c> gate)?
            ///
            /// <para>It is here because it is the fact that bounds this round's blast radius, and
            /// it was asserted once already in this lane instead of measured. Lifting the FLOOR
            /// arm's veto cannot make a renderer fade unless it ALSO passes the material walk in
            /// <see cref="FadeDriver.CollectWallFadeInfo"/>; so for each of the five named
            /// acceptance subjects the next hardware log states, in one grep, whether this rule
            /// was ever what held it. A "no channel" subject cannot move whatever this term
            /// decides.</para></summary>
            internal readonly bool FadeChannel;

            internal StandingMeasure(bool ok, WallStandingProp.Unit unit, float floorY,
                                     bool vegetation, bool wallCut, bool fadeChannel)
            {
                Ok = ok;
                Unit = unit;
                FloorY = floorY;
                Vegetation = vegetation;
                WallCut = wallCut;
                FadeChannel = fadeChannel;
            }
        }

        /// <summary>Per-rescan measurement memo, keyed by the PROP UNIT root (not the renderer).
        /// Cleared at the start of every rescan — never holds transform references across frames,
        /// the same discipline <see cref="FigureAncestryMemo"/> keeps, because Apparance rebirths
        /// these props constantly and a cached transform dangles within a couple of seconds.</summary>
        private readonly Dictionary<Transform, StandingMeasure> _standingUnitMemo = new(64);

        /// <summary>Unit-root memo for the FLOOR arm, keyed by the renderer's PARENT (siblings
        /// share an answer). Per-rescan only, for the same dangling-reference reason.</summary>
        private readonly Dictionary<Transform, Transform?> _standingRootMemo = new(128);

        /// <summary>Description of each protected unit for the census, keyed by unit root.</summary>
        private readonly Dictionary<Transform, string> _standingPropDesc = new(64);

        /// <summary>MODBUILD 266 — roots of the units the WALL-FRAGMENT term refused this rescan.
        /// Its COUNT is what the census prints. A SET and not a counter because this rule is asked
        /// once per RENDERER while its verdict is a property of the UNIT, and the near-miss map
        /// cannot stand in for it (that one stops filling at
        /// <see cref="StandingNearMissCap"/>).</summary>
        private readonly HashSet<Transform> _standingWallCutRoots = new(32);

        /// <summary>MODBUILD 266 — the per-subject ROLL-CALL. One line per distinct renderer NAME
        /// this rescan, carrying the two facts that adjudicate the five named acceptance subjects
        /// (shelf + board must fade; curtain must fade; ice crystal, skeleton limbs and light
        /// shaft must stay): was a wall inside this unit's walk window, and does the unit carry an
        /// authored fade channel at all.
        ///
        /// <para>Keyed by NAME rather than by root on purpose — the subjects are named in the
        /// user's report and in the leftover audit by name, and one line per name is what makes
        /// the next log answer all five with a single grep instead of five cross-references. The
        /// cap is what stops a roll-call becoming a census.</para></summary>
        private readonly Dictionary<string, string> _standingSubjectRoll = new(64);

        /// <summary>How many distinct names the roll-call carries before it stops collecting.</summary>
        private const int StandingSubjectRollCap = 48;

        /// <summary>Scratch for the fade-channel probe. Deliberately NOT <c>_matScratch</c>:
        /// <see cref="FadeDriver.CollectWallFadeInfo"/> and
        /// <see cref="FadeDriver.HasGatedOffWallFadeToggle"/> both iterate that one, and this runs
        /// from inside the standing check those call.</summary>
        private readonly List<Material> _standingMatScratch = new(8);

        /// <summary>NEAR MISSES: units whose foot IS in the floor band but that failed another
        /// term, with the number they failed on. Keyed by root so one unit is named once.</summary>
        private readonly Dictionary<Transform, string> _standingNearMiss = new(64);

        /// <summary>How many near misses are recorded before the census stops collecting. 40, not
        /// the ModBuild-167 value of 8: the whole point of the ModBuild-257 shape column is that
        /// the next log carries the DISTRIBUTION of h/w across this tileset's units, and a cap of
        /// 8 with a 320-character print budget showed four of them.</summary>
        private const int StandingNearMissCap = 40;

        /// <summary>Scratch for a unit's renderer sweep (reused; never held).</summary>
        private readonly List<Renderer> _standingUnitScratch = new(32);

        /// <summary>Wall-renderer claims refused this rescan: "'renderer' → 'segment'".</summary>
        private readonly List<string> _standingBlocked = new(8);

        /// <summary>How many claims were refused this rescan (the list above is capped).</summary>
        private int _standingBlockedCount;

        /// <summary>Change-triggered census signature — this line must never become per-rescan
        /// spam (the arch/water-rect lesson).</summary>
        private int _standingCensusSig = -1;

        /// <summary>Open a fresh standing-prop scope for one rescan: drop last rescan's
        /// measurements and roots (Apparance rebirths these props constantly) and reset the block
        /// census. The prop-unit anchor set is refreshed HERE as well as in
        /// <see cref="BeginPropUnitScope"/>, because the FLOOR arm's walk must stop at segment
        /// anchors and it runs at wall-collection time, long before that later scope opens.</summary>
        private void BeginStandingPropScope()
        {
            _standingUnitMemo.Clear();
            _standingRootMemo.Clear();
            _standingRootCutMemo.Clear();
            _standingPropDesc.Clear();
            _standingNearMiss.Clear();
            _standingWallCutRoots.Clear();
            _standingSubjectRoll.Clear();
            _standingBlocked.Clear();
            _standingBlockedCount = 0;
            // PERF S3: the per-node subtree facts PropUnitRootOf reads are dropped HERE and only
            // here — this is the first scope of the commit, and keeping them across the standing
            // pass and the later prop-unit pass is the point of them. See _nodeRendererCount.
            ClearNodeFactMemos();
            RefreshPropUnitAnchors();
        }

        /// <summary>The PROP UNIT of a renderer for the FIGURE arm: the nearest ancestor (itself
        /// included) carrying one of the figure/actor components. Null when there is none — then
        /// the renderer matched only as a <c>SkinnedMeshRenderer</c> and there is no unit to
        /// protect as a whole.</summary>
        private static Transform? FigurePropRootOf(Transform? t)
        {
            while (t != null)
            {
                if (t.GetComponent<ActorBehaviour>() != null
                    || t.GetComponent<CInteractableActor>() != null
                    || t.GetComponent<Animator>() != null)
                {
                    return t;
                }
                t = t.parent;
            }
            return null;
        }

        /// <summary>The PROP UNIT of a renderer for the FLOOR arm: ModBuild 167's grouping, the
        /// HIGHEST still prop-sized ancestor of the renderer's parent, memoised per parent because
        /// siblings share the answer. Null when the walk finds nothing — a renderer hanging
        /// directly off its wall entity, which is most of the masonry in any scene and is the
        /// cheap path out of this rule.</summary>
        /// <summary>The unit root alone, for the two censuses that only want to GROUP renderers
        /// by unit (<c>WallSegmentFade.FadeCensus.cs</c>, <c>WallSegmentFade.Mounted.cs</c>).
        /// They ask no verdict, so they need no <c>wallCut</c>, and giving them one would put a
        /// second reader on a fact only <see cref="IsStandingProp"/> may act on.</summary>
        private Transform? StandingFloorUnitRootOf(Renderer r) => StandingFloorUnitRootOf(r, out _);

        /// <inheritdoc cref="StandingFloorUnitRootOf(Renderer)"/>
        /// <param name="wallCut">MODBUILD 266 — the walk passed a WALL ENTITY inside its own
        /// bounded window. Memoised beside the root, never recomputed by a second climb.</param>
        private Transform? StandingFloorUnitRootOf(Renderer r, out bool wallCut)
        {
            wallCut = false;
            Transform? parent = r.transform.parent;
            if (parent == null)
                return null;
            if (_standingRootMemo.TryGetValue(parent, out Transform? cached))
            {
                // The cut is a property of the WALK, so it is memoised beside the root it
                // produced and never recomputed with a second, differently-bounded climb — the
                // whole safety argument of this term is that it sees exactly the window the unit
                // walk saw, and a separate probe would quietly stop being that.
                wallCut = _standingRootCutMemo.Contains(parent);
                return cached;
            }
            Transform? root = PropUnitRootOf(parent, out wallCut);
            _standingRootMemo[parent] = root;
            if (wallCut)
                _standingRootCutMemo.Add(parent);
            return root;
        }

        /// <summary>MODBUILD 266 — the <c>wallInWindow</c> half of <see cref="_standingRootMemo"/>,
        /// keyed by the same renderer PARENT. Per-rescan, cleared with everything else.</summary>
        private readonly HashSet<Transform> _standingRootCutMemo = new(128);

        /// <summary>
        /// Is this renderer part of a prop that STANDS ON THE FLOOR — and therefore never wall
        /// geometry, whatever shader or material slot it carries? See the file header for the two
        /// arms and <see cref="WallStandingProp"/> for the arithmetic. Used by the wall-renderer
        /// choke point and by the mounted sweep.
        /// </summary>
        private bool IsStandingFigureProp(Renderer r) => IsStandingProp(r, floorArm: true);

        /// <summary>
        /// The FOLIAGE paths' rule: the ModBuild-157 FIGURE arm alone, and deliberately never the
        /// plain FLOOR arm. (ModBuild 257 briefly added the TREE arm here as well; ModBuild 258
        /// retired it — see the file header and the tombstone in <see cref="WallStandingProp"/>.
        /// This call site is therefore bit-for-bit what ModBuild 256 shipped.)
        ///
        /// <para>WHY THE FOLIAGE PATHS DO NOT GET THE FLOOR ARM, and this is a deliberate
        /// inconsistency rather than an oversight. Those paths exist for the "Gestrüpp-Wand"
        /// report (gebüsch.png): grass, vines and bushes DRESSING a wall carry no fade path of
        /// their own, so when the wall dissolves they used to stay behind as a view-blocking
        /// thicket, and they are hidden with the wall instead. A bush is a multi-piece thing that
        /// stands on the ground and is well under the height cap, i.e. it is precisely what the
        /// FLOOR arm would protect — so applying the widening here would hand that report straight
        /// back. The exclusion those call sites actually need is the one ModBuild 157 wrote for
        /// them ("a mossy statue would vanish whole instead of losing a head"), and that is a
        /// figure/actor question. The photographed skull is a mesh on a masonry shader and never
        /// reaches a foliage list at all.</para>
        /// </summary>
        private bool IsStandingFigureOnlyProp(Renderer r) => IsStandingProp(r, floorArm: false);

        private bool IsStandingProp(Renderer r, bool floorArm)
        {
            if (r == null)
                return false;
            // Which arm. The FIGURE arm is ModBuild 157 unchanged; the FLOOR arm is the ModBuild
            // 167 widening the skeleton photograph needed — a scenery skeleton on a deck has no
            // figure ancestry at all. There is no third arm (ModBuild 258 retired it).
            bool figure = IsFigureOrActorRenderer(r);
            bool wallCut = false;
            Transform? root = figure
                ? FigurePropRootOf(r.transform)
                : StandingFloorUnitRootOf(r, out wallCut);
            if (root == null)
                return false;
            if (!MeasureStandingUnit(root, wallCut, out WallStandingProp.Unit unit,
                                     out float floorY, out bool vegetation,
                                     out bool fadeChannel))
            {
                return false;
            }

            bool verdict = WallStandingProp.StandsOnFloor(unit, floorY, figure, vegetation,
                                                          wallCut, out string why);
            // MODBUILD 266 — how many UNITS the new term is the one that refused. Read off the
            // refusal SENTENCE's own tag rather than a second copy of the term's arithmetic:
            // one definition, no drift.
            if (!verdict && why.StartsWith(WallStandingProp.WallFragmentTag,
                                           System.StringComparison.Ordinal))
            {
                _standingWallCutRoots.Add(root);
            }
            NoteStandingSubject(r, figure, wallCut, fadeChannel, verdict);
            // THE FOLIAGE PATHS TAKE THE FIGURE ARM AND NEVER THE PLAIN FLOOR ARM, and that split
            // is the whole reason this method has a flag. ModBuild 167's note holds word for word:
            // a bush is a multi-piece thing standing on the ground under the height cap, so
            // handing the foliage paths the FLOOR arm would protect the Gestrüpp-Wand and give
            // back "Die anderen 'gestrüpp-wände' versperren mir nun auch manchmal die Sicht. Das
            // darf niemals passieren."
            bool armed = verdict && (floorArm || figure);

            if (armed)
            {
                _standingPropDesc[root] = $"'{root.name}' {why}";
            }
            else if (!verdict && unit.MinY - floorY <= WallStandingProp.FootBandWU
                     && _standingNearMiss.Count < StandingNearMissCap)
            {
                // A unit that DID reach the floor and was refused on some other term is the one
                // shape a still-missing prop can take, so the log has to name it and the number.
                // The cap is 40 and not 8 since ModBuild 257, and it stays 40: this list is now
                // ALSO the roster of units the ModBuild-258 whole-unit rule applies to (a near
                // miss IS a unit the mod has called architecture), so a truncated list hides the
                // very units whose bases should have been recruited.
                _standingNearMiss[root] = $"'{root.name}' {why}";
            }
            return armed;
        }

        /// <summary>Measure a unit once per rescan: the union AABB of every renderer under its
        /// root (inactive included — an inactive piece is still part of the prop) and the anchored
        /// room floor nearest its foot. False when the unit has no measurable geometry or the scene
        /// has no anchored floor at all — and with zero anchors every wall is fail-safe solid
        /// anyway, so refusing to protect costs nothing.</summary>
        private bool MeasureStandingUnit(Transform root, bool wallCut,
                                         out WallStandingProp.Unit unit,
                                         out float floorY, out bool vegetation,
                                         out bool fadeChannel)
        {
            if (_standingUnitMemo.TryGetValue(root, out StandingMeasure memo))
            {
                unit = memo.Unit;
                floorY = memo.FloorY;
                vegetation = memo.Vegetation;
                fadeChannel = memo.FadeChannel;
                return memo.Ok;
            }
            unit = default;
            floorY = 0f;
            vegetation = false;
            fadeChannel = false;
            _standingUnitScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, _standingUnitScratch);
            Bounds union = default;
            bool have = false;
            int kept = 0;
            foreach (Renderer piece in _standingUnitScratch)
            {
                if (piece == null || IsModObject(piece))
                    continue;
                kept++;
                // VEGETATION, over the whole unit and not over the reported renderer — the same
                // reason the AABB is a union. A tree's trunk is bark on the masonry shader family
                // and its canopy is the Foliage-shaded part; asking the trunk alone would answer
                // "no vegetation" for a tree, which is the identical mistake as judging a skull
                // airborne on its own. Shader verdicts are cached per Shader, so this costs one
                // dictionary hit per material per unit per rescan.
                if (!vegetation && piece is MeshRenderer mesh && RendererUsesFoliage(mesh))
                    vegetation = true;
                // MODBUILD 266, REPORTED ONLY: does the unit carry an authored fade channel at
                // all? Same two tests CollectWallFadeInfo runs, in the same order, so the answer
                // is the one that actually gates admission. Shader verdicts are cached per
                // Shader and the material walk is a list fill, so this is a handful of dictionary
                // hits per unit per RESCAN.
                if (!fadeChannel && RendererHasFadeChannel(piece))
                    fadeChannel = true;
                if (!have) { union = piece.bounds; have = true; }
                else union.Encapsulate(piece.bounds);
            }
            _standingUnitScratch.Clear();
            bool ok = have && NearestAnchoredFloorY(union.min.y, out floorY);
            if (ok)
            {
                unit = new WallStandingProp.Unit(union.min.y, union.max.y,
                                                 union.size.x, union.size.z, kept);
                // THE TWO STANDING RULINGS. A unit inside the water rect (2026-08-09,
                // brunnen.png) or the doorway-arch rect (2026-08-02) has an owner of its own, and
                // the wall-fragment term must never claim it for a wall. Asked HERE because this
                // is the one place holding the whole prop's union box, which is the geometry
                // every other consumer of those rects is asked with. Same two rects, same order,
                // as IsWallGeneratedMember and the mounted sweep.
                if (wallCut && (IsWaterProtected(union) || IsArchProtected(union, root.name)))
                    wallCut = false;
            }
            _standingUnitMemo[root] =
                new StandingMeasure(ok, unit, floorY, vegetation, wallCut, fadeChannel);
            return ok;
        }

        /// <summary>
        /// MODBUILD 266, REPORTED ONLY — does this renderer carry an AUTHORED wall-fade channel?
        /// The same two tests <see cref="CollectWallFadeInfo"/> gates admission on: a
        /// WallFade-family shader NAME, or a live <c>_WallFade_On</c> / <c>_ToggleWallfade</c> /
        /// <c>_ToggleWallFadeLocal</c> gate.
        ///
        /// <para>IT DECIDES NOTHING, and it is measured because the alternative was to assert it.
        /// The FLOOR arm is a VETO at the collection choke point; lifting it cannot make anything
        /// fade that would not also pass this test. So printing it per subject is what turns
        /// "the crystal and the light shaft cannot be affected" from a claim into a number the
        /// next hardware log states. The 2026-08-25 measurement that put it here: this session's
        /// TOGGLE-NATIVE roster is 'CR_RU_PillarThin_MAT', 'CR_RU_Rock_MAT',
        /// 'CR_ST_Candlestick_MAT', 'EN_CR_PropAtlas_01_Temp_MAT', 'FR_Floor_LargeBush_Dead_M'
        /// and 'FR_UnderWall_Rock_M' — a candlestick, a generic prop atlas and a FLOOR BUSH among
        /// the masonry, which is why the channel is NOT a "the artist marked this as
        /// wall-attached" flag and cannot be the discriminator on its own. CollectWallFadeInfo's
        /// own note says the same thing: "N_MRAO dresses half the scenery, and a scene-wide
        /// toggle-based adoption would claim all of it as walls".</para>
        /// </summary>
        private bool RendererHasFadeChannel(Renderer r)
        {
            _standingMatScratch.Clear();
            r.GetSharedMaterials(_standingMatScratch);
            bool any = false;
            foreach (Material m in _standingMatScratch)
            {
                if (m == null || m.shader == null)
                    continue;
                if (m.shader.name.Contains("WallFade") || HasLiveWallFadeToggle(m))
                {
                    any = true;
                    break;
                }
            }
            _standingMatScratch.Clear();
            return any;
        }

        /// <summary>
        /// MODBUILD 266 — one roll-call line per distinct renderer NAME, carrying the two facts
        /// that adjudicate this round's five named acceptance subjects. The coordinator's
        /// acceptance is five named cases rather than one number, and a truncated
        /// PROTECTED/NEAR-MISS name list cannot answer five questions at once — that is the
        /// "a summary stat is not the field" lesson, paid for twice in this subsystem.
        /// </summary>
        private void NoteStandingSubject(Renderer r, bool figure, bool wallCut, bool fadeChannel,
                                         bool verdict)
        {
            if (_standingSubjectRoll.Count >= StandingSubjectRollCap
                || _standingSubjectRoll.ContainsKey(r.name))
            {
                return;
            }
            _standingSubjectRoll[r.name] =
                $"'{r.name}' {(figure ? "FIGURE arm" : "FLOOR arm")}, "
                + (wallCut ? "under a wall" : "no wall above") + ", "
                + (fadeChannel ? "HAS a fade channel" : "NO fade channel — this rule cannot move it")
                + ", " + (verdict ? "PROTECTED" : "fades with its wall");
        }

        /// <summary>The anchored room floor plane nearest to a prop's foot. Rooms can be
        /// terraced (the registry keeps same-CMap volumes at different heights apart on
        /// purpose), so the LOWEST floor in the scene is the wrong reference for a prop standing
        /// on an upper terrace. False when no room is anchored at all.</summary>
        private bool NearestAnchoredFloorY(float propMinY, out float floorY)
        {
            floorY = 0f;
            float best = float.PositiveInfinity;
            for (int i = 0; i < _roomFloorY.Count && i < _roomFloorAnchored.Count; i++)
            {
                if (!_roomFloorAnchored[i])
                    continue;
                float d = Mathf.Abs(_roomFloorY[i] - propMinY);
                if (d < best)
                {
                    best = d;
                    floorY = _roomFloorY[i];
                }
            }
            return !float.IsInfinity(best);
        }

        /// <summary>Record a refused claim for the census (capped list, full count). Called from
        /// the choke points every collection path goes through.</summary>
        private void NoteStandingPropBlocked(Renderer r, Segment? seg)
        {
            _standingBlockedCount++;
            if (_standingBlocked.Count >= 6)
                return;
            string wall = seg == null
                ? "<mounted sweep>"
                : seg.Anchor != null ? seg.Anchor.name : "<no anchor>";
            string entry = $"'{r.name}' → '{wall}'";
            if (!_standingBlocked.Contains(entry))
                _standingBlocked.Add(entry);
        }

        /// <summary>
        /// The proof line. Names every protected prop unit (with the measurements the verdict was
        /// made on and WHICH ARM made it), every claim refused this rescan, and every NEAR MISS —
        /// a unit that reached the floor band and was refused on some other term, with that term's
        /// number. Change-triggered, like the water census.
        /// </summary>
        private void LogStandingPropCensus()
        {
            int protectedUnits = _standingPropDesc.Count;
            int wallCutRefused = _standingWallCutRoots.Count;
            // ModBuild 266: the new term and the roll-call are IN the signature, or their whole
            // population can turn over under a line that never reprints (the held-instrument
            // lesson).
            int sig = protectedUnits * 977 + _standingBlockedCount * 13
                      + _standingBlocked.Count * 7 + _standingNearMiss.Count
                      + wallCutRefused * 31 + _standingSubjectRoll.Count * 3;
            if (sig == _standingCensusSig)
                return;
            _standingCensusSig = sig;
            if (protectedUnits == 0 && _standingBlockedCount == 0 && _standingNearMiss.Count == 0)
                return;

            // PRINT BUDGETS. The protected list stays short — it is dominated by hundreds of
            // identical floor-grass units and naming six of them says everything six hundred
            // would. The NEAR MISS list is the opposite and keeps its own 3000-character budget:
            // it is the h/w distribution that retired the TREE arm (0.23…2.11, one value over the
            // 2.0 bar and it was a WALL), and since ModBuild 258 it is also the roster of units
            // the whole-unit rule applies to.
            var names = new System.Text.StringBuilder();
            foreach (KeyValuePair<Transform, string> kv in _standingPropDesc)
            {
                if (names.Length > 320)
                {
                    names.Append("; …");
                    break;
                }
                if (names.Length > 0)
                    names.Append("; ");
                names.Append(kv.Value);
            }
            var roll = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, string> kv in _standingSubjectRoll)
            {
                if (roll.Length > 2200)
                {
                    roll.Append("; …");
                    break;
                }
                if (roll.Length > 0)
                    roll.Append("; ");
                roll.Append(kv.Value);
            }
            var misses = new System.Text.StringBuilder();
            foreach (KeyValuePair<Transform, string> kv in _standingNearMiss)
            {
                if (misses.Length > 3000)
                {
                    misses.Append("; …");
                    break;
                }
                if (misses.Length > 0)
                    misses.Append("; ");
                misses.Append(kv.Value);
            }
            VRLog.Info(Name,
                $"STANDING PROP: {protectedUnits} prop(s) STAND ON THE FLOOR and are never wall "
                + $"geometry on any path — whole prop, every renderer (user report 2026-08-19, "
                + $"skelet.jpg: 'Der Schädel ist immer noch nicht sichtbar'). TWO ARMS: FIGURE = "
                + $"figure/actor ancestry AND the unit's union AABB reaches within "
                + $"{StandingPropFootBandWU:0.0} wu of its room's floor AND prop-sized (span ≤ "
                + $"{StandingPropMaxSpanWU:0.0} wu, ≤ {StandingPropMaxRenderers} renderers); FLOOR "
                + $"= the same geometry WITHOUT the ancestry term, for scenery that stands on the "
                + $"ground, plus height ≤ {StandingPropMaxHeightWU:0.0} wu so a course of masonry "
                + $"can never qualify. Wall-MOUNTED dressing never reaches the floor and keeps "
                + $"fading with its masonry. TWO ARMS AND NO MORE: ModBuild 257's third (TREE = "
                + $"vegetation AND ≥ 2.0 h/w, lifting the height cap) was RETIRED in ModBuild 258 "
                + $"because in the whole ModBuild-257 hardware session it fired on exactly ONE "
                + $"unit and that unit was 'PCG_FR_Wall_Space_04_PR' [h 4.5 wu / w 2.1 wu = 2.11 "
                + $"h/w] — a WALL, which then appeared in no FADE WRITE line at all, i.e. never "
                + $"faded once ('ein Teil der Wand bleibt nun stehen und faded garnicht mehr', "
                + $"wandproblem3.jpg). Every actual trunk unit in that session measured 1.39, "
                + $"1.46 and 1.79 h/w, so no tree ever reached the bar. Do not re-derive a shape "
                + $"term here; the h/w column below is REPORTED and decides nothing. "
                + $"WALL-FEATURE FRAGMENT (ModBuild 266, bücherregale1/2.jpg + regal_brett.jpg): "
                + $"the FLOOR arm also refuses a unit whose own walk passed a WALL inside its "
                + $"bounded window and whose top clears the ground band "
                + $"({StandingPropFootBandWU:0.0} wu). It is NOT a new threshold — the ModBuild-265 "
                + $"log measures ONE shelf prefab at two roots: as the wall feature it belongs to "
                + $"('PCG_Test_Feature_Small_2', 21 renderer(s), unit y[0.0..3.4], refused as "
                + $"architecture, fades) and as a 2-renderer FRAGMENT of that same feature "
                + $"(y[0.9..1.3], 0.4 wu tall, accepted as a floor prop and left drawing over a "
                + $"wall at fade 1.00). The rule was judging a piece of a wall. Membership rides "
                + $"the unit walk's own four-level window, never "
                + $"GetComponentInParent<ProceduralWall>() — that probe reaches the scene root and "
                + $"in this same log answers YES for a skeleton's thighs, a light shaft and 348 "
                + $"ice-crystal renderers the user allows to stay. "
                + $"{wallCutRefused} unit(s) refused by this term this rescan"
                + (wallCutRefused == 0
                    ? " — ZERO with the shelves still solid means the window does not see their "
                      + "wall and this term is the wrong lever."
                    : ".")
                + (roll.Length > 0
                    ? $" SUBJECT ROLL-CALL (one line per renderer NAME, up to "
                      + $"{StandingSubjectRollCap}; this is what adjudicates the named cases — "
                      + $"shelf + board and curtain must read 'fades with its wall', the ice "
                      + $"crystal, the skeleton limbs and the light shaft must read 'no wall "
                      + $"above' or 'NO fade channel'): {roll}."
                    : string.Empty)
                + $" WHOLE-UNIT RULE (ModBuild 258): a unit in the NEAR MISS list has been judged "
                + $"architecture, so every renderer under its root fades with its owner — the "
                + $"per-renderer ground band gets no vote inside it (see PROP UNIT). "
                + $"Protected: {(protectedUnits > 0 ? names.ToString() : "none")}. "
                + $"{_standingBlockedCount} claim(s) refused this rescan"
                + (_standingBlocked.Count > 0
                    ? $": {string.Join(", ", _standingBlocked)}."
                    : ".")
                + (misses.Length > 0
                    ? $" NEAR MISS (reached the floor band, refused on another term — this is "
                      + $"where a still-missing prop shows up, it is the h/w DISTRIBUTION that "
                      + $"retired the tree bar, and since ModBuild 258 it is the roster of units "
                      + $"whose ground-band members are recruited back into their own fade; up to "
                      + $"{StandingNearMissCap} units, no longer 8): {misses}."
                    : string.Empty));
        }
    }
}
