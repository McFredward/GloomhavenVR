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
/// <para>MODBUILD 268 — THE ModBuild-267 TERM NEVER FIRED, AND THE OTHER HALF OF THE SUBJECT IS
/// NOT IN THIS FILE. User, 2026-08-25, on the 267 build: <i>"Weiterhin faded weder das große
/// Regal, dass wie eine Wand benutzt wird, noch das kleine Wandhalterungsregal an der Wand. Auch
/// mit dem Fix nicht."</i> Two findings, and they belong to different owners:</para>
/// <list type="number">
/// <item>THE SMALL WALL SHELF is this file's. The <c>wallCut</c> input was read off the unit
///   walk's BREAK REASON, and that walk breaks on container scale at <c>Generated Content</c> one
///   node below <c>Wall N</c>, so the flag could never be set — <c>under a wall</c> appears zero
///   times in a 391-row session. Corrected by <c>WallInUnitWindow</c> (same nodes, same bound, no
///   early exit). See the tombstone in <see cref="WallStandingProp"/>.</item>
/// <item>THE BRACKET HAS A SECOND GATE BEHIND THIS ONE, in a file this lane does not own:
///   <c>'CR_ST_Shelves_Stone_Wood'[mesh] anchor 0.9 gap 0.00: anchor 0.90 under the airborne bar
///   1.00 — reads as floor-supported</c>, which is the MOUNTED sweep
///   (<c>WallSegmentFade.Mounted.cs</c>). Even a correct fix here can therefore read as "no
///   change" in a headset until that bar is dealt with, and BOTH halves must ship before the
///   subject can be judged. Do not conclude from a negative hardware result that this term is
///   wrong without checking the census counter first.</item>
/// </list>
/// <para>THE LARGE "REGAL" IS A THIRD SUBJECT AND IS NEITHER OF THE ABOVE. The 267 log's split-run
/// census reads <c>'CR_ST_Shelves_Stone_Wood' of run 'Wall 1' r3 ema 0.00 blk 0/16 solid (verdict
/// from the run)</c> — a shelf that the tileset registers as a wall piece IN ITS OWN RIGHT, held
/// solid by its RUN's coverage verdict (it blocks 0 of 16 playable-tile samples). No rule in this
/// file is consulted for it and no change here can move it; it is the fade DECISION's business.
/// The same census shows <c>'CR_FR_Wall_Log_Structure_03' … blk 0/16 solid</c> beside it, and the
/// roll-call already reads <c>'CR_FR_Wall_Log_Structure_03' FLOOR arm … fades with its wall</c> —
/// i.e. the standing rule is releasing it correctly and the run is still holding it.</para>
///
/// <para>MODBUILD 275 — THE ModBuild-266 TERM WAS ON THE ARM THE SHELF DOES NOT TAKE. User,
/// 2026-08-25, hardware, ModBuild 274: <i>"Die ein/ausblendung sind schon fast perfekt in dem
/// Level - was noch fehlt sind die Großen Bücherregale die als ganze Wandsektion verwendet werden
/// vom Spiel und nicht ausblenden."</i> The 274 log answers it in one row, printed 23 rescans
/// running: <c>'CR_ST_WallShelf_Stone_Wood' FIGURE arm, under a wall, HAS a fade channel,
/// PROTECTED @ Wall 4/Generated Content/PCG_CR_ST_WallShelf_Stone_Wood/CR_ST_WallShelf_Stone_Wood</c>,
/// with the unit measured as <c>foot 0.0 wu over floor 0.0, height 3.3 wu, span 2.3x1.8 wu, 4
/// renderer(s) — figure/actor prop</c> and all four of its renderers listed in the refused-claims
/// roll as <c>'…' → 'Wall 4'</c>. So <see cref="FadeDriver.CollectWallFadeInfo"/> — the choke point
/// every wall-renderer collection goes through — was ALREADY asking for this shelf on behalf of
/// the very wall it substitutes for, and THIS FILE was the only thing saying no.</para>
///
/// <para>The ModBuild-266 <c>wall-feature fragment</c> term is written <c>!figureAncestry &amp;&amp;
/// wallCut &amp;&amp; …</c>, i.e. the FLOOR arm only, and so is the height cap. This tileset hangs
/// an <c>Animator</c> over its wall shelves — ModBuild 157 measured exactly that for
/// <c>CR_ST_WallShelf_Stone_Bone</c> and recorded it in this header — so the shelf takes the FIGURE
/// arm, where neither term is asked at all. Nothing was mistuned; the rule simply has no wall term
/// on the arm the subject travels. <c>WallStandingProp.IsWallBuiltSection</c> is that term, with
/// the same two conjuncts the FLOOR arm's carries (a wall inside the unit walk's own BOUNDED
/// window, and the unit rising clear of the ground band) and a third the FIGURE arm requires:
/// <see cref="FadeDriver.IsWallGeneratedDressing"/>, which is provenance ON TOP OF an absolute
/// <c>ActorBehaviour</c>/<c>CInteractableActor</c> veto. THE ROUND-7 RULING IS NOT RELAXED.</para>
///
/// <para>WHAT THIS LANDS THE SHELF IN, and it is none of the three adoption lanes. Released here,
/// the shelf is collected by <c>'Wall 4'</c>'s OWN renderer list through the toggle-native path
/// (its materials carry a live <c>_WallFade_On</c> gate — the census column reads <c>HAS a fade
/// channel</c>), which is the same applier that already fades the masonry beside it and the only
/// one that drives a 4-renderer, 3.3 wu, 2.3x1.8 wu piece correctly. The mounted lane's
/// <c>architecture-scale (AABB volume 3.4 wu³ &gt; 1.5)</c> refusal is DOWNSTREAM of this and is
/// left exactly as it is: the mounted sweep only ever saw the shelf because the wall's own
/// collection had refused it, and its hand-off to "stacked-shell territory" reaches nothing —
/// the stacked pass requires a piece's base at or above the wall's original course top minus
/// 1.2 wu, and this unit's foot is 0.0 wu, on the floor.</para>
///
/// <para>THE STACKED-SHELL PASS IS DELIBERATELY LEFT ALONE./// <para>THE STACKED-SHELL PASS IS DELIBERATELY LEFT ALONE. Its own admission test requires a
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

        /// <summary>MODBUILD 268 — the NAMES behind that count, with the measurement, on their own
        /// unconditional budget.
        ///
        /// <para>ModBuild 267 printed the count and nothing else, and its refusals were then
        /// swallowed by the near-miss list's cap and print budget: the census said "2 unit(s)
        /// refused by this term" while the term's own refusal tag appeared ZERO times in the whole
        /// log, so the one question that mattered — <i>which units?</i> — could not be answered
        /// from the log at all, and a reader with only the truncated column concluded the term was
        /// inert. A count whose subjects cannot be named is not a measurement, it is a rumour.
        /// This list is small, deduplicated by sentence, and printed before anything that can be
        /// truncated.</para></summary>
        private readonly Dictionary<string, int> _standingWallCutNames = new(16);

        /// <summary>How many distinct wall-fragment refusal sentences are named. Small on purpose
        /// — the population it describes is small, and if it ever is not, the count beside it says
        /// so.</summary>
        private const int StandingWallCutNameCap = 12;

        /// <summary>MODBUILD 275 — roots of the units the WALL-SECTION term released this rescan.
        /// Its COUNT is what the census prints, and it is a SET for the same reason
        /// <see cref="_standingWallCutRoots"/> is: the rule is asked once per RENDERER while its
        /// verdict is a property of the UNIT.
        ///
        /// <para>DELIBERATELY ITS OWN CONTAINER, never merged with the ModBuild-266
        /// <c>[WALL-BUILT]</c> tag of the mounted lane nor with ModBuild 271's below-the-bar
        /// counter. Three wrong diagnoses in this subsystem came from two populations sharing one
        /// number.</para></summary>
        private readonly HashSet<Transform> _wallSectionRoots = new(16);

        /// <summary>The NAMES behind that count, deduplicated by sentence with an instance count —
        /// the same shape <see cref="_standingWallCutNames"/> keeps, and for the same ModBuild-267
        /// reason: a count whose subjects cannot be named is a rumour.</summary>
        private readonly Dictionary<string, int> _wallSectionNames = new(16);

        /// <summary>Every DISTINCT wall-section sentence seen this rescan, capped or not — the N
        /// in the line's "named K of N, dropped M". Without it the named list could only ever
        /// report its own size, which is the ellipsis-as-absence failure this subsystem has paid
        /// for three times.</summary>
        private readonly HashSet<string> _wallSectionSeen = new(16);

        /// <summary>THE FAILURE ARM, keyed by unit root so one unit is named once: units that
        /// reached this term and were refused by it, carrying WHICH half of the conjunction said
        /// no and the number it said no on. Without it "the term did not fire" carries zero
        /// information — the in-repo lesson is that a gated remedy that never runs and a remedy
        /// that ran and was wrong look identical in a log.</summary>
        private readonly Dictionary<Transform, string> _wallSectionFail = new(32);

        /// <summary>Every unit root the failure arm has already counted this rescan. Separate from
        /// the capped map above ON PURPOSE: keying the de-duplication off a CAPPED container makes
        /// the counter beside it count RENDERERS once the cap is full and UNITS before it, which
        /// is a counter that changes what it measures halfway through a scene — the exact shape of
        /// "a summary stat is not the field".</summary>
        private readonly HashSet<Transform> _wallSectionFailSeen = new(32);

        /// <summary>One unit's WALL-SECTION geometry verdict for this rescan — the two conjuncts
        /// that are properties of the UNIT, memoised by root so the sentence behind them is built
        /// once. Never holds a transform across frames (Apparance rebirths these props constantly),
        /// the same discipline every other memo in this file keeps.</summary>
        private readonly Dictionary<Transform, WallSectionMeasure> _wallSectionGeomMemo = new(32);

        private readonly struct WallSectionMeasure
        {
            internal readonly bool Geometry;
            internal readonly string Why;

            internal WallSectionMeasure(bool geometry, string why)
            {
                Geometry = geometry;
                Why = why;
            }
        }

        /// <summary>The unit half of the ModBuild-275 term, out of (and into) the per-rescan memo.
        /// FIGURE arm only — the caller gates on it, and the memo would otherwise hold two
        /// different questions' answers under one key on a root that happens to be both a figure
        /// root and a prop-unit root.</summary>
        private bool WallSectionGeometry(Renderer r, Transform root,
                                         in WallStandingProp.Unit unit,
                                         float floorY, bool windowWall, bool wallCut,
                                         bool vegetation, out string why)
        {
            if (_wallSectionGeomMemo.TryGetValue(root, out WallSectionMeasure memo))
            {
                why = memo.Why;
                return memo.Geometry;
            }
            // MODBUILD 291 — THE UNIT THE GROUND-BAND CONJUNCT IS ASKED OF. See
            // WallStandingProp.IsWallBuiltSection for the report, the two measurements of one
            // physical wall feature that produced it, and why this cannot eat the floor.
            //
            // The four-level prop-unit walk is the SAME walk the FLOOR arm's root comes out of
            // (StandingFloorUnitRootOf, memoised per renderer PARENT) and the same one wallCut is
            // read off, so this introduces no new traversal and no new constant. It is asked only
            // for a renderer that has already passed `verdict && figure` at the call site — three
            // units in the whole ModBuild-274 scene, and the log's own FLOOR-arm near-miss roster
            // proves the enclosing unit is already measured every rescan for these very roots.
            float enclosingTop = float.NaN;
            string enclosingLabel = string.Empty;
            int enclosingRenderers = 0;
            Transform? unitRoot = StandingFloorUnitRootOf(r);
            if (unitRoot != null && unitRoot != root
                && MeasureStandingUnit(unitRoot, windowWall,
                                       out WallStandingProp.Unit encUnit, out float encFloorY,
                                       out _, out _, out bool encWallCut)
                // THE ENCLOSING UNIT MUST ITSELF BE UNDER A WALL. Without this the term would
                // read the top of any prop-sized ancestor, which is the unbounded-climb mistake
                // wearing a bounded walk's clothes.
                && encWallCut)
            {
                enclosingTop = encUnit.MaxY - encFloorY;
                enclosingLabel = unitRoot.name;
                enclosingRenderers = encUnit.RendererCount;
            }
            bool geometry = WallStandingProp.IsWallBuiltSection(
                unit, floorY, figureAncestry: true, wallCut, vegetation, enclosingTop,
                enclosingLabel, enclosingRenderers, out why);
            _wallSectionGeomMemo[root] = new WallSectionMeasure(geometry, why);
            return geometry;
        }

        /// <summary>How many distinct failure sentences the census names. The count beside it is
        /// the full population.</summary>
        private const int WallSectionNameCap = 12;

        /// <summary>How many units reached the term and were refused by it this rescan (the map
        /// above is capped, this is not).</summary>
        private int _wallSectionFailCount;

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

        /// <summary>MODBUILD 268 — the BASELINE half of the roll-call: rows the FLOOR arm already
        /// releases ("fades with its wall"), which is 45 of the 70 distinct names in the
        /// ModBuild-267 log. Kept separate and given a small quota of its own so it can never
        /// crowd out the PROTECTED rows, which are the ones this rule is actively holding.
        ///
        /// <para>THE ModBuild-267 FAILURE THIS FIXES, and it is an instrument failure rather than
        /// a rule failure. That build's roll-call was one dictionary with a first-come cap of 48.
        /// The session opens with 848 refused floor-grass claims, so the 48 slots were spent on
        /// 'FR_Floor_Grass_Half_02' and its siblings before the subject of the round was ever
        /// reached, and the one line written to adjudicate the fix could not show the fix's own
        /// subject. A census that cannot show the subject cannot adjudicate anything — the
        /// in-repo lesson is "a summary stat is not the field", and this is the same failure with
        /// a cap instead of a statistic.</para></summary>
        private readonly Dictionary<string, string> _standingSubjectBaseline = new(16);

        /// <summary>How many distinct names the roll-call carries before it stops collecting.</summary>
        private const int StandingSubjectRollCap = 64;

        /// <summary>How many BASELINE names the roll-call keeps — deliberately small. Their whole
        /// job is to show that the boring majority is still boring; sixteen of them says that as
        /// well as six hundred would, and every slot beyond that is a slot the subject cannot
        /// have.</summary>
        private const int StandingSubjectBaselineCap = 16;

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

        /// <summary>
        /// Open a fresh standing-prop MEASUREMENT scope for one rescan: drop last rescan's
        /// measurements and roots (Apparance rebirths these props constantly). The prop-unit
        /// anchor set is refreshed HERE as well as in <see cref="BeginPropUnitScope"/>, because
        /// the FLOOR arm's walk must stop at segment anchors and it runs at wall-collection
        /// time, long before that later scope opens.
        ///
        /// <para>PERF S4 SPLIT THIS SCOPE IN TWO, AND THE REASON IS AN INSTRUMENT, NOT A COST.
        /// </para>
        ///
        /// <para>The MEMO half (this method) is the three per-rescan derivation caches, and it
        /// now opens one stage earlier — in <c>BeginPrepareStage</c> — because the prepare stage
        /// exists to fill exactly them. The CENSUS half
        /// (<see cref="BeginStandingCensusScope"/>) is the roll of what the rule DECIDED, and it
        /// stays at the top of the commit where it has always been.</para>
        ///
        /// <para>WHY THEY MAY NOT MOVE TOGETHER. The census containers are read by the
        /// heartbeat's WALL-PATH AUDIT and by the standing-prop census line, both of which run
        /// between commits. Clearing them at prepare time would leave them EMPTY for the whole
        /// length of the stage — a diagnostic that reports "0 protected units" for a scene full
        /// of them, which is the "instrument shipped and lying" failure this project has paid
        /// for more than once. Left where it is, the roll holds the previous rescan's outcome
        /// until the rescan that replaces it actually begins, exactly as before.</para>
        /// </summary>
        private void BeginStandingMemoScope()
        {
            _standingUnitMemo.Clear();
            _standingRootMemo.Clear();
            _standingRootCutMemo.Clear();
            // ModBuild 275: a DERIVATION cache, so it belongs in the memo half of the scope and
            // not in the census half — see this method's doc for why the two may not move
            // together.
            _wallSectionGeomMemo.Clear();
            // PERF E (ModBuild 279): the Describe() sentence cache. Also a DERIVATION cache —
            // it holds no outcome, only the text of one, and every operand in it is measured
            // geometry that this scope is dropping on the line above.
            _standingWhyMemo.Clear();
            // PERF S3 dropped the per-node subtree facts here too. PERF S4 moved that single
            // line to the top of CommitWallCache: this scope now opens one stage earlier, and
            // the node-fact WINDOW may not move with it, because its constancy argument is about
            // one synchronous pass and a prepare stage spans frames. Two lifetimes, two call
            // sites, neither widened. See _nodeRendererCount.
            RefreshPropUnitAnchors();
        }

        /// <summary>The CENSUS half of the standing-prop scope — the roll of what the rule
        /// decided this rescan. Opened at the top of the commit and nowhere else; see
        /// <see cref="BeginStandingMemoScope"/> for why it may not move earlier with the
        /// memos.</summary>
        private void BeginStandingCensusScope()
        {
            _standingPropDesc.Clear();
            _standingNearMiss.Clear();
            _standingWallCutRoots.Clear();
            _standingWallCutNames.Clear();
            _standingSubjectRoll.Clear();
            _standingSubjectBaseline.Clear();
            _standingBlocked.Clear();
            _standingBlockedCount = 0;
            _wallSectionRoots.Clear();
            _wallSectionNames.Clear();
            _wallSectionSeen.Clear();
            _wallSectionFail.Clear();
            _wallSectionFailSeen.Clear();
            _wallSectionFailCount = 0;
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

        /// <summary>MODBUILD 268 — "is there a wall in this renderer's unit window", memoised by
        /// renderer PARENT (siblings share the answer, exactly as the root memo does). Separate
        /// from <see cref="StandingFloorUnitRootOf"/> because the FIGURE arm needs the same fact
        /// and does not use that walk at all — see the note at the call site for what a
        /// half-measured column did to the ModBuild-267 log.</summary>
        private bool WallInUnitWindowMemoized(Transform parent)
        {
            if (_standingRootCutMemo.TryGetValue(parent, out bool cached))
                return cached;
            bool verdict = WallInUnitWindow(parent);
            _standingRootCutMemo[parent] = verdict;
            return verdict;
        }

        /// <summary>MODBUILD 268 — the <c>wallInWindow</c> answer, memoised by renderer PARENT
        /// (siblings share it), the same discipline <see cref="_standingRootMemo"/> keeps.
        /// Per-rescan only: Apparance rebirths these props constantly and the segment table the
        /// answer is measured against is rebuilt every rescan too.</summary>
        private readonly Dictionary<Transform, bool> _standingRootCutMemo = new(128);

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
            if (!ResolveStandingUnit(r, out bool figure, out Transform? root,
                                     out WallStandingProp.Unit unit, out float floorY,
                                     out bool vegetation, out bool fadeChannel, out bool wallCut))
            {
                return false;
            }

            // PERF E (ModBuild 279) — THE VERDICT IS TAKEN, THE SENTENCE IS NOT.
            // WallStandingProp.Judge is the five ifs of StandsOnFloor with nothing else in them;
            // WallStandingProp.Describe holds the five sentences verbatim. This method is called
            // once per child renderer of every cache wall — thousands of times per commit — and
            // used to build a `shape` column plus one of those sentences on EVERY call, on
            // net472 where each `$"…"` is a string.Format(string, object[]): an array plus a box
            // per float. Below, `Why()` builds it at the four sites that actually consume it and
            // memoises the result per UNIT ROOT (see _standingWhyMemo) — which is exact, not
            // approximate: unit / floorY / vegetation / wallCut all come out of
            // _standingUnitMemo keyed by that same root, so the only per-RENDERER term in the
            // sentence is the arm, and the memo carries the arm with it.
            WallStandingProp.FloorVerdict judged =
                WallStandingProp.Judge(unit, floorY, figure, wallCut);
            bool verdict = judged == WallStandingProp.FloorVerdict.StandsOnFloor;
            string? sectionOverride = null;
            // MODBUILD 266 — how many UNITS the new term is the one that refused. Read off the
            // verdict ENUM rather than the refusal sentence's leading tag: one definition, no
            // drift, and no sentence built to be tested for a prefix. (PERF E: this is the same
            // question — WallFragmentTag is the first thing Describe writes for exactly this
            // verdict and for no other.)
            if (judged == WallStandingProp.FloorVerdict.WallFeatureFragment)
            {
                _standingWallCutRoots.Add(root!);
                string named = $"'{root!.name}' {Why()}";
                if (_standingWallCutNames.TryGetValue(named, out int seen))
                    _standingWallCutNames[named] = seen + 1;
                else if (_standingWallCutNames.Count < StandingWallCutNameCap)
                    _standingWallCutNames[named] = 1;
            }
            // MODBUILD 275 — THE FIGURE ARM'S WALL-SECTION TERM. See WallStandingProp
            // .IsWallBuiltSection for the subject, the evidence and the three conjuncts; the
            // fourth and last one is here because it is the only ancestor walk in the rule.
            //
            // THE ORDER IS THE COST ARGUMENT, not a style choice. figure/verdict are already in
            // hand, wallCut came out of a per-parent memo and the ground-band compare is two
            // floats — so IsWallGeneratedDressing is asked only for a unit that has already
            // passed all three, which in the ModBuild-274 session is THREE units in the whole
            // scene. The predicate's own note says the un-memoised actor pair costs "a handful
            // per rescan"; this ordering is what keeps that true.
            //
            // THE ROUND-7 RULING IS NOT RELAXED. IsWallGeneratedDressing carries the
            // ActorBehaviour / CInteractableActor chain as an ABSOLUTE VETO and this call site
            // adds provenance ON TOP of it — it is strictly narrower on figures than the arm it
            // stands beside, exactly as the four ModBuild-266 sites are.
            bool wallSection = false;
            if (verdict && figure)
            {
                // THE GEOMETRY HALF IS A PROPERTY OF THE UNIT, so it is measured once per unit
                // per rescan and not once per renderer — the same discipline _standingUnitMemo
                // keeps, and for the same reason: StandsOnFloor already builds a shape column and
                // a sentence on EVERY call, and a second one of those per renderer over the whole
                // protected population is exactly the kind of per-rescan string cost this
                // subsystem is currently being blamed for. The memo is only ever filled on the
                // FIGURE branch, so an entry can never be read with the other arm's meaning.
                // The window answer is re-read from the SAME per-parent memo ResolveStandingUnit
                // filled a few statements ago (WallInUnitWindowMemoized) rather than threaded
                // through as a seventh out-parameter: one dictionary hit, and one fact with one
                // owner. It seeds the enclosing unit's measurement exactly as the FLOOR arm's
                // would, so a root both arms reach carries one value and not two.
                bool windowWall = r.transform.parent != null
                                  && WallInUnitWindowMemoized(r.transform.parent);
                bool geometry = WallSectionGeometry(r, root!, unit, floorY, windowWall, wallCut,
                                                    vegetation, out string sectionWhy);
                if (geometry && IsWallGeneratedDressing(r))
                {
                    verdict = false;
                    wallSection = true;
                    sectionOverride = sectionWhy; // Why() returns this from here on, as before
                    _wallSectionRoots.Add(root!);
                    string sentence = $"'{root!.name}' {sectionWhy}";
                    _wallSectionSeen.Add(sentence);
                    if (_wallSectionNames.TryGetValue(sentence, out int seen))
                        _wallSectionNames[sentence] = seen + 1;
                    else if (_wallSectionNames.Count < WallSectionNameCap)
                        _wallSectionNames[sentence] = 1;
                }
                else if (_wallSectionFailSeen.Add(root!))
                {
                    // THE FAILURE ARM. Which half said no, with the number it said no on — and
                    // never a constant string, because a change-gated line whose reason never
                    // varies prints once and then reads as a dead instrument.
                    _wallSectionFailCount++;
                    if (_wallSectionFail.Count < WallSectionNameCap)
                    {
                        _wallSectionFail[root!] = $"'{root!.name}' "
                            + (geometry
                                ? "GEOMETRY half PASSED, PROVENANCE half refused — no "
                                  + "ProceduralWall built it, or an ActorBehaviour / "
                                  + "CInteractableActor above it vetoes it (absolute, round-7 "
                                  + $"ruling): {sectionWhy}"
                                : $"GEOMETRY half refused: {sectionWhy}");
                    }
                }
            }
            NoteStandingSubject(r, figure, wallCut, fadeChannel, verdict, wallSection);
            // THE FOLIAGE PATHS TAKE THE FIGURE ARM AND NEVER THE PLAIN FLOOR ARM, and that split
            // is the whole reason this method has a flag. ModBuild 167's note holds word for word:
            // a bush is a multi-piece thing standing on the ground under the height cap, so
            // handing the foliage paths the FLOOR arm would protect the Gestrüpp-Wand and give
            // back "Die anderen 'gestrüpp-wände' versperren mir nun auch manchmal die Sicht. Das
            // darf niemals passieren."
            bool armed = verdict && (floorArm || figure);

            if (armed)
            {
                _standingPropDesc[root!] = $"'{root!.name}' {Why()}";
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
                _standingNearMiss[root!] = $"'{root!.name}' {Why()}";
            }
            return armed;

            // PERF E — the refusal/verdict SENTENCE, built at most once per unit root per arm
            // per rescan instead of once per renderer. The wall-section override short-circuits
            // it because that branch has its own sentence and always had.
            string Why()
            {
                if (sectionOverride != null)
                    return sectionOverride;
                if (_standingWhyMemo.TryGetValue(root!, out StandingWhy cached)
                    && cached.Figure == figure)
                {
                    return cached.Why;
                }
                string built = WallStandingProp.Describe(unit, floorY, figure, vegetation,
                                                         wallCut, judged);
                _standingWhyMemo[root!] = new StandingWhy(figure, built);
                return built;
            }
        }

        /// <summary>PERF E (ModBuild 279) — one unit root's <see cref="WallStandingProp.Describe"/>
        /// sentence, WITH the arm it was written for.
        ///
        /// <para>WHY IT IS EXACT AND NOT MERELY CHEAPER. Every operand of that sentence except
        /// the arm is a per-UNIT fact out of <see cref="_standingUnitMemo"/> (unit, floorY,
        /// vegetation, wallCut) or the verdict those facts produce, so two renderers under one
        /// root on the same arm get character-for-character the same string. The arm IS
        /// per-renderer — <c>IsFigureOrActorRenderer</c> is asked of the renderer, not of the
        /// root — so it is stored and compared, and a mismatch rebuilds. Every consumer still
        /// writes on every call, so LAST WRITE STILL WINS exactly as before; the memo removes
        /// the rebuild, never a write.</para>
        ///
        /// <para>Per-rescan, cleared with the other standing memos: Apparance rebirths these
        /// props constantly and the sentence carries measured geometry.</para></summary>
        private readonly struct StandingWhy
        {
            internal StandingWhy(bool figure, string why)
            {
                Figure = figure;
                Why = why;
            }

            internal readonly bool Figure;
            internal readonly string Why;
        }

        private readonly Dictionary<Transform, StandingWhy> _standingWhyMemo = new(128);

        /// <summary>
        /// PERF S4 — THE PROLOGUE OF <see cref="IsStandingProp"/>, EXTRACTED SO THERE IS ONE
        /// COPY OF IT.
        ///
        /// <para>Every statement here was the first five statements of that method and is
        /// unchanged, line for line. It is separated only because it is the half that is a PURE
        /// DERIVATION — four memo-backed reads and nothing else — and the prepare stage
        /// (WallSegmentFade.Prepare.cs) needs to run exactly it, ahead of the commit, to fill
        /// those memos. Extracting rather than copying is deliberate: this project has shipped a
        /// second implementation wearing the same name before, and the two then drifted.</para>
        ///
        /// <para>WRITES NOTHING BUT MEMOS. <see cref="IsFigureOrActorRenderer"/> fills
        /// <c>FigureAncestryMemo</c>, <see cref="WallInUnitWindowMemoized"/> fills
        /// <c>_standingRootCutMemo</c>, <see cref="StandingFloorUnitRootOf"/> fills
        /// <c>_standingRootMemo</c> and <see cref="MeasureStandingUnit"/> fills
        /// <c>_standingUnitMemo</c> (plus the per-Shader verdict caches). No renderer, material,
        /// property block or segment field is touched — which is what makes the warm safe. The
        /// CENSUS side (<c>NoteStandingSubject</c>, <c>_standingPropDesc</c>, the near-miss and
        /// wall-cut rolls) stays in the caller on purpose: those are outcomes, and a warm that
        /// recorded them would double every count the census line prints.</para>
        /// </summary>
        private bool ResolveStandingUnit(Renderer r, out bool figure, out Transform? root,
                                         out WallStandingProp.Unit unit, out float floorY,
                                         out bool vegetation, out bool fadeChannel,
                                         out bool wallCut)
        {
            figure = false;
            root = null;
            unit = default;
            floorY = 0f;
            vegetation = false;
            fadeChannel = false;
            wallCut = false;
            if (r == null)
                return false;
            // Which arm. The FIGURE arm is ModBuild 157 unchanged; the FLOOR arm is the ModBuild
            // 167 widening the skeleton photograph needed — a scenery skeleton on a deck has no
            // figure ancestry at all. There is no third arm (ModBuild 258 retired it).
            figure = IsFigureOrActorRenderer(r);
            // MODBUILD 268 — THE WINDOW IS ASKED ON BOTH ARMS. ModBuild 267 initialised wallCut
            // to false and only assigned it on the FLOOR branch, so every FIGURE row in the
            // census printed "no wall above" out of a FIELD INITIALISER rather than a
            // measurement — which is how the 267 log came to state
            // 'CR_BT_BanditBanner_Wall' FIGURE arm, no wall above for a banner hanging on a wall.
            // (The in-repo name for this is "a default value names an unbuilt thing"; it has cost
            // a build before.) The VERDICT is unchanged either way — StandsOnFloor consults
            // wallCut only when !figureAncestry, so the figure arm stays bit-for-bit ModBuild 157
            // — but a column that is a constant on half its rows cannot adjudicate anything, and
            // adjudicating is the only reason this column exists.
            bool windowWall = r.transform.parent != null
                              && WallInUnitWindowMemoized(r.transform.parent);
            root = figure
                ? FigurePropRootOf(r.transform)
                : StandingFloorUnitRootOf(r);
            if (root == null)
                return false;
            // MODBUILD 268 — AND THE VALUE THE VERDICT READS COMES BACK OUT OF THE UNIT MEMO.
            // ModBuild 267 passed wallCut IN by value, cleared it for the water and arch rects
            // inside, stored the cleared value in the memo — and then let the caller hand its own
            // UNcleared local to StandsOnFloor. So the two standing rulings were dead code on this
            // path (harmlessly, because the term never fired), and the flag the verdict read was a
            // per-RENDERER fact on a rule whose every other term is per-UNIT. Both are fixed by
            // making it an out-parameter: one value, measured once per unit, rects applied.
            return MeasureStandingUnit(root, windowWall, out unit, out floorY, out vegetation,
                                       out fadeChannel, out wallCut);
        }

        /// <summary>PERF S4 — run <see cref="ResolveStandingUnit"/> for its memo side only, and
        /// discard every answer. The prepare stage's whole standing-prop contribution; see THE
        /// PREPARE INVARIANT in WallSegmentFade.Prepare.cs.</summary>
        private void WarmStandingUnit(Renderer r)
        {
            ResolveStandingUnit(r, out _, out _, out _, out _, out _, out _, out _);
        }

        /// <summary>Measure a unit once per rescan: the union AABB of every renderer under its
        /// root (inactive included — an inactive piece is still part of the prop) and the anchored
        /// room floor nearest its foot. False when the unit has no measurable geometry or the scene
        /// has no anchored floor at all — and with zero anchors every wall is fail-safe solid
        /// anyway, so refusing to protect costs nothing.</summary>
        private bool MeasureStandingUnit(Transform root, bool windowWall,
                                         out WallStandingProp.Unit unit,
                                         out float floorY, out bool vegetation,
                                         out bool fadeChannel, out bool wallCut)
        {
            if (_standingUnitMemo.TryGetValue(root, out StandingMeasure memo))
            {
                unit = memo.Unit;
                floorY = memo.FloorY;
                vegetation = memo.Vegetation;
                fadeChannel = memo.FadeChannel;
                wallCut = memo.WallCut;   // ModBuild 268: the verdict reads the UNIT's value
                return memo.Ok;
            }
            unit = default;
            floorY = 0f;
            vegetation = false;
            fadeChannel = false;
            wallCut = false;
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
                // THE TWO STANDING RULINGS, and from ModBuild 268 they are actually READ. A unit
                // inside the water rect (2026-08-09, brunnen.png) or the doorway-arch rect
                // (2026-08-02) has an owner of its own and this term must never claim it for a
                // wall. Asked here because this is the one place holding the whole prop's union
                // box, which is the geometry every other consumer of those rects is asked with.
                wallCut = windowWall
                          && !IsWaterProtected(union)
                          && !IsArchProtected(union, root.name);
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
                // PERF E (ModBuild 279) — the cached per-Shader verdict, not a fresh interop
                // name read. See the twin site in WallSegmentFade.PropUnit.cs
                // (RendererHasWallFadeChannel) for the argument; FadeNameOf's ByName IS
                // `shader.name.Contains("WallFade")` on the same Shader object.
                if (FadeNameOf(m.shader).ByName || HasLiveWallFadeToggle(m))
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
        /// <param name="wallSection">MODBUILD 275 — this row was RELEASED by the wall-section
        /// term. It must share the PROTECTED tier's priority and never fall into the baseline: the
        /// baseline keeps 16 names out of hundreds, so a released subject dropped into it is a
        /// subject the next log cannot show — which is precisely the ModBuild-267 instrument
        /// failure this tiering was built to end.</param>
        private void NoteStandingSubject(Renderer r, bool figure, bool wallCut, bool fadeChannel,
                                         bool verdict, bool wallSection)
        {
            // PERF E (ModBuild 279) — THE CAP IS ASKED BEFORE THE NAME, and the name is read
            // ONCE.
            //
            // `Object.name` is an interop call that allocates a managed string on EVERY read,
            // and this method is called once per renderer of every cache wall — the ModBuild-277
            // scenario runs it over a 734-renderer population per commit. It read r.name TWICE
            // unconditionally (the two ContainsKey operands, with no local) and a third time on
            // a new name, to feed two rolls capped at 64 + 16 entries.
            //
            // WHY THE HOIST CANNOT CHANGE WHAT IS RECORDED. When BOTH rolls are full every path
            // through the rest of this method returns without writing: the priority branch
            // returns at `_standingSubjectRoll.Count >= StandingSubjectRollCap`, the baseline
            // branch at `_standingSubjectBaseline.Count >= StandingSubjectBaselineCap`, and the
            // two ContainsKey probes above only ever caused an EARLIER return. So on a full pair
            // of rolls this is a pure no-op either way, and in the steady state — which is what
            // the 94.8 ms commit is made of — the rolls are full. This is the same argument
            // StructuralSkipArmed makes for the mounted reject list, one file over.
            if (_standingSubjectRoll.Count >= StandingSubjectRollCap
                && _standingSubjectBaseline.Count >= StandingSubjectBaselineCap)
            {
                return;
            }
            string subject = r.name;
            if (_standingSubjectRoll.ContainsKey(subject)
                || _standingSubjectBaseline.ContainsKey(subject))
            {
                return;
            }
            // WHICH TIER, and the split is measured off the ModBuild-267 log rather than
            // guessed. That log carries 70 distinct roll names: 45 read "fades with its wall" and
            // only 25 read PROTECTED. PROTECTED is the anomaly — it means THIS RULE is actively
            // holding the renderer back — and every one of the five acceptance subjects is in it
            // (the shelf pair, the curtain, the ice crystal, the skeleton limbs, the light
            // shaft). "Fades with its wall" is the boring majority and belongs in the baseline.
            //
            // My first attempt at this tiering keyed on the fade CHANNEL instead, and the same
            // log falsifies that in one line: 66 of the 70 names HAVE a channel, so it would have
            // sorted almost nothing and the subject could have been crowded out again. Sorting by
            // relevance rather than by arrival is the fix; the caps then bound the STRING only,
            // never the question the line can answer.
            bool priority = verdict || wallSection;
            if (priority)
            {
                if (_standingSubjectRoll.Count >= StandingSubjectRollCap)
                    return;
                _standingSubjectRoll[subject] = Row(withPath: true);
                return;
            }
            if (_standingSubjectBaseline.Count >= StandingSubjectBaselineCap)
                return;
            _standingSubjectBaseline[subject] = Row(withPath: false);

            string Row(bool withPath)
            {
                string row = $"'{subject}' {(figure ? "FIGURE arm" : "FLOOR arm")}, "
                    + (wallCut ? "under a wall" : "no wall above") + ", "
                    + (fadeChannel
                        ? "HAS a fade channel"
                        : "NO fade channel — this rule cannot move it")
                    + ", " + (verdict
                        ? "PROTECTED"
                        : wallSection
                            ? "[WALL-SECTION] RELEASED — the game uses this AS a wall section"
                            : "fades with its wall");
                // THE PATH, for priority rows only. ModBuild 267 was diagnosed blind because the
                // shelf instance that FAILS is refused before any census sees it, so its parenting
                // has never appeared in a log — while the instance that WORKS prints its path in
                // the PROP UNIT census every rescan. Two instances of one prefab behaving
                // differently is the whole question, and it cannot be answered from one of them.
                if (withPath && r.transform.parent != null)
                    row += " @ " + UnitWindowPath(r.transform.parent);
                return row;
            }
        }

        /// <summary>The anchored room floor plane nearest to a prop's foot. Rooms can be
        /// terraced (the registry keeps same-CMap volumes at different heights apart on
        /// purpose), so the LOWEST floor in the scene is the wrong reference for a prop standing
        /// on an upper terrace. False when no room is anchored at all.</summary>
        private bool NearestAnchoredFloorY(float propMinY, out float floorY)
        {
            floorY = 0f;
            float best = float.PositiveInfinity;
            for (int i = 0; i < _live.RoomFloorY.Count && i < _live.RoomFloorAnchored.Count; i++)
            {
                if (!_live.RoomFloorAnchored[i])
                    continue;
                float d = Mathf.Abs(_live.RoomFloorY[i] - propMinY);
                if (d < best)
                {
                    best = d;
                    floorY = _live.RoomFloorY[i];
                }
            }
            return !float.IsInfinity(best);
        }

        /// <summary>The wall-fragment refusals as one deduplicated, counted string. Built before
        /// any budgeted list so it can never be the part that gets cut.</summary>
        private string WallCutNames()
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kv in _standingWallCutNames)
            {
                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append(kv.Key);
                if (kv.Value > 1)
                    sb.Append(" ×").Append(kv.Value);
            }
            // NO AppendTally HERE, DELIBERATELY. This list's entries are distinct SENTENCES and
            // the only total standing beside it is a count of distinct ROOTS — many roots share
            // one sentence, so "named 1 of 3, dropped 2" would be a wrong number where the ×N
            // suffix already carries the right one. The unit count is printed next to this list
            // by the caller.
            return sb.ToString();
        }

        /// <summary>
        /// MODBUILD 275 — HOW MUCH A CAP COST, stated in the line itself: "named K of N, dropped
        /// M". Appends nothing at all when nothing was dropped, so the boring case stays quiet.
        ///
        /// <para>WHY EVERY BUDGETED LIST IN THIS CENSUS NOW ENDS IN ONE. A bare "…" says a list
        /// was cut and says nothing about by how much, and three wrong diagnoses in this project
        /// came from reading an ellipsis-capped list as evidence of absence — including one in
        /// this very file, where a counter said a term fired 2-6 times per rescan while its tag
        /// appeared ZERO times in the log, because every one of its refusals was inside the part
        /// that got cut. The two numbers never disagreed; one was a truncated sample of the
        /// other's population, and nothing printed said so.</para>
        /// </summary>
        private static void AppendTally(System.Text.StringBuilder sb, int shown, int total)
        {
            if (total <= shown)
                return;
            sb.Append("; … (named ").Append(shown).Append(" of ").Append(total)
              .Append(", dropped ").Append(total - shown).Append(')');
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
            int wallSectionUnits = _wallSectionRoots.Count;
            int sig = protectedUnits * 977 + _standingBlockedCount * 13
                      + _standingBlocked.Count * 7 + _standingNearMiss.Count
                      + wallCutRefused * 31 + _standingSubjectRoll.Count * 3
                      + _standingSubjectBaseline.Count * 2
                      // ModBuild 275: BOTH halves of the new term are in the signature. A line
                      // that cannot reprint when its own population turns over is the
                      // held-instrument failure, and a FAILURE arm that never reprints is worse
                      // than none at all.
                      + wallSectionUnits * 1553 + _wallSectionFailCount * 17;
            if (sig == _standingCensusSig)
                return;
            _standingCensusSig = sig;
            if (protectedUnits == 0 && _standingBlockedCount == 0 && _standingNearMiss.Count == 0
                && wallSectionUnits == 0 && _wallSectionFailCount == 0)
            {
                return;
            }

            // PRINT BUDGETS. The protected list stays short — it is dominated by hundreds of
            // identical floor-grass units and naming six of them says everything six hundred
            // would. The NEAR MISS list is the opposite and keeps its own 3000-character budget:
            // it is the h/w distribution that retired the TREE arm (0.23…2.11, one value over the
            // 2.0 bar and it was a WALL), and since ModBuild 258 it is also the roster of units
            // the whole-unit rule applies to.
            //
            // EVERY CAPPED LIST BELOW NOW STATES "named K of N, dropped M" IN THE LINE ITSELF.
            // ModBuild 274's own log is the argument: its roll-call ran out of budget mid-list
            // and ended in a bare "…", so reading it as the population is exactly the
            // ellipsis-as-absence mistake this project has made three times. A cap is only honest
            // when the reader can see what it cost.
            var names = new System.Text.StringBuilder();
            int namesShown = 0;
            foreach (KeyValuePair<Transform, string> kv in _standingPropDesc)
            {
                if (names.Length > 320)
                    break;
                if (names.Length > 0)
                    names.Append("; ");
                names.Append(kv.Value);
                namesShown++;
            }
            AppendTally(names, namesShown, _standingPropDesc.Count);
            var roll = new System.Text.StringBuilder();
            int rollShown = 0;
            foreach (KeyValuePair<string, string> kv in _standingSubjectRoll)
            {
                if (roll.Length > 3000)
                    break;
                if (roll.Length > 0)
                    roll.Append("; ");
                roll.Append(kv.Value);
                rollShown++;
            }
            AppendTally(roll, rollShown, _standingSubjectRoll.Count);
            // The baseline tier LAST and on its own budget, so a flood of it can never again
            // crowd out the subject the round is about (ModBuild 267: 48 first-come slots, spent
            // before the shelf was ever asked, and the one line written to adjudicate the fix
            // could not show the fix's own subject).
            if (_standingSubjectBaseline.Count > 0)
            {
                roll.Append(" || BASELINE (units the FLOOR arm already releases — sampled, not "
                            + $"enumerated; this tier is hard-capped at "
                            + $"{StandingSubjectBaselineCap} names and drops the rest SILENTLY at "
                            + "collection time, so its size is a quota and never a population): ");
                bool first = true;
                foreach (KeyValuePair<string, string> kv in _standingSubjectBaseline)
                {
                    if (!first)
                        roll.Append("; ");
                    first = false;
                    roll.Append(kv.Value);
                }
            }
            // MODBUILD 268 — DEDUPLICATE THE NEAR-MISS TEXT BEFORE SPENDING THE BUDGET, and this
            // is what reconciled the two counters that appeared to contradict each other. The map
            // is keyed by unit ROOT, and a tileset places dozens of instances of one prefab, so
            // the ModBuild-267 log spent its entire 3000-character budget printing
            // 'PCG_CR_Wall_Underground1 height 7.2 wu …' and two siblings over and over: THREE
            // distinct sentences out of up to forty collected units, then an ellipsis. Every
            // wall-fragment refusal the term actually made was inside the part that got cut —
            // which is why that tag appears ZERO times in a session whose own counter says it
            // fired 2-6 times per rescan. The two numbers never disagreed; one of them was a
            // truncated sample of the other's population. Identical sentences carry no extra
            // information, so they are collapsed with an instance count and the budget buys
            // distinct facts instead of repeats.
            var missSeen = new Dictionary<string, int>(_standingNearMiss.Count);
            var missOrder = new List<string>(_standingNearMiss.Count);
            foreach (KeyValuePair<Transform, string> kv in _standingNearMiss)
            {
                if (missSeen.TryGetValue(kv.Value, out int had))
                {
                    missSeen[kv.Value] = had + 1;
                    continue;
                }
                missSeen[kv.Value] = 1;
                missOrder.Add(kv.Value);
            }
            var misses = new System.Text.StringBuilder();
            int missShown = 0;
            foreach (string sentence in missOrder)
            {
                if (misses.Length > 3000)
                    break;
                missShown++;
                if (misses.Length > 0)
                    misses.Append("; ");
                misses.Append(sentence);
                int n = missSeen[sentence];
                if (n > 1)
                    misses.Append(" ×").Append(n);
            }
            AppendTally(misses, missShown, missOrder.Count);
            // THE WALL-SECTION BLOCK IS BUILT BEFORE ANYTHING THAT CAN BE TRUNCATED and printed
            // on its own budget, the discipline ModBuild 268 wrote for the wall-fragment names:
            // this is the round's whole subject, and the near-miss and roll-call budgets have
            // each swallowed a subject before.
            var section = new System.Text.StringBuilder();
            int sectionShown = 0;
            foreach (KeyValuePair<string, int> kv in _wallSectionNames)
            {
                if (section.Length > 0)
                    section.Append("; ");
                section.Append(kv.Key);
                if (kv.Value > 1)
                    section.Append(" ×").Append(kv.Value);
                sectionShown++;
            }
            AppendTally(section, sectionShown, _wallSectionSeen.Count);
            var sectionFail = new System.Text.StringBuilder();
            int failShown = 0;
            foreach (KeyValuePair<Transform, string> kv in _wallSectionFail)
            {
                if (sectionFail.Length > 0)
                    sectionFail.Append("; ");
                sectionFail.Append(kv.Value);
                failShown++;
            }
            AppendTally(sectionFail, failShown, _wallSectionFailCount);
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
                + (_standingWallCutNames.Count > 0
                    ? ", NAMELY: " + WallCutNames() + "."
                    : string.Empty)
                + (wallCutRefused == 0
                    ? " — ZERO with the shelves still solid means the window does not see their "
                      + "wall and this term is the wrong lever."
                    : ".")
                + $" [WALL-SECTION] (ModBuild 275, 'die Großen Bücherregale die als ganze "
                + $"Wandsektion verwendet werden vom Spiel und nicht ausblenden'): the FIGURE arm "
                + $"now releases a unit the WALL GENERATOR built, with NO ActorBehaviour / "
                + $"CInteractableActor anywhere above it (absolute veto, round-7 ruling, NOT "
                + $"relaxed), with a wall inside the unit walk's own bounded window, and rising "
                + $"clear of the ground band ({StandingPropFootBandWU:0.0} wu). It is the "
                + $"ModBuild-266 wall-fragment term on the arm that term cannot reach — the shelf "
                + $"carries an Animator ancestor, so neither the fragment term nor the "
                + $"{StandingPropMaxHeightWU:0.0} wu height cap was ever asked for it. Its own "
                + $"tag, its own counter: never merged with the mounted lane's ModBuild-266 "
                + $"[WALL-BUILT] tag nor with ModBuild 271's below-the-bar counter. "
                + $"{wallSectionUnits} unit(s) RELEASED this rescan"
                + (section.Length > 0 ? ", NAMELY: " + section + "." : ".")
                + $" REFUSED BY THIS TERM: {_wallSectionFailCount} unit(s) reached it and were "
                + $"turned away, each naming WHICH half of the conjunction said no"
                + (sectionFail.Length > 0 ? ": " + sectionFail + "." : ".")
                + (wallSectionUnits == 0
                    ? " ZERO RELEASED is NOT evidence the term is inert — read the REFUSED roster "
                      + "above: if it names the shelf with 'GEOMETRY half refused', the window or "
                      + "the ground band is the wrong lever; if it names it with 'PROVENANCE half "
                      + "refused', an actor component or a missing ProceduralWall is, and the "
                      + "term must be withdrawn rather than retuned."
                    : " FALSIFIER: this roster naming a hero, a monster, a summon, a floor hex, "
                      + "'CV_Ice_Crystal_Form_02/03' or 'LightShaft_Prefab (1)' (user rulings "
                      + "2026-08-24 and round 7). Then a conjunct is not the discriminator.")
                + (roll.Length > 0
                    ? $" SUBJECT ROLL-CALL (one line per renderer NAME, up to "
                      + $"{StandingSubjectRollCap}, PROTECTED rows first and with their parent "
                      + $"path, because a PROTECTED row is this rule actively holding something "
                      + $"and that is where a still-missing prop shows up; this is what "
                      + $"adjudicates the named cases — "
                      + $"shelf + board and curtain must read 'fades with its wall' or "
                      + $"'[WALL-SECTION] RELEASED', the ice "
                      + $"crystal, the skeleton limbs and the light shaft must read 'no wall "
                      + $"above' or 'NO fade channel'): {roll}."
                    : string.Empty)
                + $" WHOLE-UNIT RULE (ModBuild 258): a unit in the NEAR MISS list has been judged "
                + $"architecture, so every renderer under its root fades with its owner — the "
                + $"per-renderer ground band gets no vote inside it (see PROP UNIT). "
                + $"Protected: {(protectedUnits > 0 ? names.ToString() : "none")}. "
                + $"{_standingBlockedCount} claim(s) refused this rescan"
                + (_standingBlocked.Count > 0
                    ? $" (named {_standingBlocked.Count} of {_standingBlockedCount}, dropped "
                      + $"{_standingBlockedCount - _standingBlocked.Count}): "
                      + $"{string.Join(", ", _standingBlocked)}."
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
