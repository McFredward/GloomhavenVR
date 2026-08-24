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

            internal StandingMeasure(bool ok, WallStandingProp.Unit unit, float floorY,
                                     bool vegetation)
            {
                Ok = ok;
                Unit = unit;
                FloorY = floorY;
                Vegetation = vegetation;
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
            _standingPropDesc.Clear();
            _standingNearMiss.Clear();
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
        private Transform? StandingFloorUnitRootOf(Renderer r)
        {
            Transform? parent = r.transform.parent;
            if (parent == null)
                return null;
            if (_standingRootMemo.TryGetValue(parent, out Transform? cached))
                return cached;
            Transform? root = PropUnitRootOf(parent);
            _standingRootMemo[parent] = root;
            return root;
        }

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
            Transform? root = figure
                ? FigurePropRootOf(r.transform)
                : StandingFloorUnitRootOf(r);
            if (root == null)
                return false;
            if (!MeasureStandingUnit(root, out WallStandingProp.Unit unit, out float floorY,
                                     out bool vegetation))
            {
                return false;
            }

            bool verdict = WallStandingProp.StandsOnFloor(unit, floorY, figure, vegetation,
                                                          out string why);
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
        private bool MeasureStandingUnit(Transform root, out WallStandingProp.Unit unit,
                                         out float floorY, out bool vegetation)
        {
            if (_standingUnitMemo.TryGetValue(root, out StandingMeasure memo))
            {
                unit = memo.Unit;
                floorY = memo.FloorY;
                vegetation = memo.Vegetation;
                return memo.Ok;
            }
            unit = default;
            floorY = 0f;
            vegetation = false;
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
                if (!have) { union = piece.bounds; have = true; }
                else union.Encapsulate(piece.bounds);
            }
            _standingUnitScratch.Clear();
            bool ok = have && NearestAnchoredFloorY(union.min.y, out floorY);
            if (ok)
            {
                unit = new WallStandingProp.Unit(union.min.y, union.max.y,
                                                 union.size.x, union.size.z, kept);
            }
            _standingUnitMemo[root] = new StandingMeasure(ok, unit, floorY, vegetation);
            return ok;
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
            int sig = protectedUnits * 977 + _standingBlockedCount * 13
                      + _standingBlocked.Count * 7 + _standingNearMiss.Count;
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
                + $"WHOLE-UNIT RULE (ModBuild 258): a unit in the NEAR MISS list has been judged "
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
