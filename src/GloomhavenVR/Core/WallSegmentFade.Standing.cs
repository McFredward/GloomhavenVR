using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// STANDING PROPS ARE NEVER WALL GEOMETRY — the decapitated skeleton (user report 2026-08-15,
/// skelet.jpg): "In der Map die wir gespielt haben wurde der Kopf eines Skelets mit der Wand
/// mit ausgeblendet. Füge das auf die Ausnahmeliste hinzu, dass er richtig dargestellt wird."
///
/// <para>WHAT THE HARDWARE LOG SHOWS, and it names the culprit outright. The prop is the crypt
/// ossuary's skeleton statue, and it is FOUR sibling MeshRenderers under one root
/// (<c>CR_OS_Skeleton_Statue</c> → <c>_Body</c>, <c>_Skull</c>, <c>_Sword</c>, <c>_Broken</c>),
/// not one skinned mesh — <c>LogOutput.log:1920</c>:</para>
/// <code>
/// FLOOR CENSUS room 1 'Room_2'x2 center(18.1,10.4) floorY 0.00:
///   [ABOVE] 'CR_OS_Skeleton_Statue_Skull' y[2.9..3.6] sh='Amp_Basic_Prop_Shader' q2000;
///   [ABOVE] 'CR_OS_Skeleton_Statue_Body'  y[0.0..3.6] sh='Amp_Basic_Prop_Shader' q2000; …
/// </code>
/// <para>The skull is claimed AS WALL by the segment refresh (<c>remote1/LogOutput.log</c>,
/// mounted-pass near-miss line): <c>'CR_OS_Skeleton_Statue_Skull'[mesh] anchor 5,1 gap 0,00:
/// already the wall renderer of 'Wall 1' (that wall's fade 1,00)</c> — while the body is only a
/// near-miss (<c>architecture-scale … never sconce dressing</c>) and therefore stays. One prop,
/// two verdicts: body visible, head gone. That is the screenshot.</para>
///
/// <para>WHY IT QUALIFIED AS WALL, exactly. Apparance parents this statue inside the
/// ProceduralWall subtree it decorates, and one of its material slots is the masonry family's:
/// <c>LogOutput.log:3139</c> — <c>TOGGLE-NATIVE MATERIAL 'EN_Crypt_Ossary_Skellington' (shader
/// 'Amp_Basic_N_MRAO'): authored _WallFade_On=1, … _Cutoff=0.5, keywords=[_WALLFADE_ON_ON]</c>.
/// So <see cref="FadeDriver.CollectWallFadeInfo"/>'s TOGGLE test accepts it, and the renderer
/// lands in <c>seg.Renderers</c> — visible in the fade line itself, <c>LogOutput.log:38063</c>:
/// <c>fade ON 'Wall 5' … 20 toggle-native: CR_OS_Skeleton_Statue_Body@5.5,
/// CR_OS_Skeleton_Statue_Skull@5.4, …</c>. Note the slot-0 shader the census prints is
/// <c>Amp_Basic_Prop_Shader</c>: a shader-NAME glance misses this entirely, which is why the
/// defect survived every previous round.</para>
///
/// <para>AND WHY NO EXISTING GUARD CAUGHT IT. The figure guard DID classify the prop correctly —
/// <c>LogOutput.log:1094</c>: <c>FIGURE-GUARD: 3 adoption candidate(s) excluded … :
/// CR_OS_Skeleton_Statue_Skull, CR_OS_Skeleton_Statue, WP_Bandit_Knife_01</c> — but
/// <see cref="FadeDriver.IsFigureOrActorRenderer"/> is consulted by the ADOPTION sweeps only
/// (stacked shell, corner, fast-reclaim, mounted dressing, plain wall body). A wall's own
/// renderer collection never asked it. That was the single hole, and it is the one path on which
/// a renderer can be faded without ever being an "adoption candidate" at all.</para>
///
/// <para>WHY THE OBVIOUS FIX IS WRONG, measured rather than argued. Simply calling the figure
/// guard from the wall path would ALSO stop legitimate wall dressing from fading with its wall:
/// cross-referencing the session's <c>FIGURE-GUARD</c> name set against the renderers that
/// actually appear in <c>fade ON</c> lines, <c>CR_ST_WallShelf_Stone_Bone</c> and
/// <c>CR_ST_WallShelf_Stone_Bone_Skull</c> are in BOTH — wall shelves that carry an Animator
/// ancestor and today fade correctly with the masonry they hang on. A blanket guard would leave
/// them floating in mid-air when their wall goes: the fountain-basin defect again, with the
/// stonework and the prop swapped. So the guard needs a second term.</para>
///
/// <para>THE RULE THIS FILE STATES, and it is a class rule, not a name:
/// <b>a figure/actor prop that STANDS ON THE FLOOR is never wall geometry — the whole prop, all
/// of its renderers, on every path.</b> Three terms, each with its reason:</para>
/// <list type="number">
/// <item>FIGURE/ACTOR ANCESTRY selects the candidate — the same
///   <see cref="FadeDriver.IsFigureOrActorRenderer"/> predicate every other sweep uses, so there
///   is one definition of "this is not scenery" in the file family and no second one to drift.
///   The round-7 header already states the intent this extends: the Animator arm exists to
///   exclude "animated props (chests…), which no wall system should ever hide anyway".</item>
/// <item>THE PROP UNIT is the NEAREST ancestor carrying one of those components
///   (<see cref="FadeDriver.FigurePropRootOf"/>), and the verdict is computed for the UNIT, then
///   applied to every renderer under it. This is what makes the fix whole-prop rather than
///   per-piece: the statue's skull, body, sword and broken half all get the same answer, so no
///   future viewing angle can split them again. It is the same manoeuvre
///   <see cref="FadeDriver.CollectWaterFeatures"/> makes for the fountain — protect the FEATURE,
///   not the mesh that happened to be reported.</item>
/// <item>FLOOR-SUPPORTED is the discriminator against wall dressing: the unit's union AABB must
///   reach down into its room's ground band (<see cref="FadeDriver.StandingPropFootBandWU"/>,
///   the same 1.0 wu <c>GroundExclusionHeightWU</c> already calls "ground, never wall"). The
///   skeleton statue spans y[0.0..3.6] — it stands on the floor. A wall shelf, a sconce, a
///   hanging banner and a torch do not reach the floor at all, so they keep today's behaviour
///   bit-for-bit and still dissolve with the masonry they are mounted on.</item>
/// </list>
///
/// <para>TWO SIZE CAPS, and they exist to make one specific catastrophe impossible. If some
/// tileset hangs an Animator high in the hierarchy — on an Apparance content root, say — the
/// nearest-ancestor walk would return a unit containing the whole room, its union AABB would
/// reach the floor trivially, and EVERY wall renderer under it would become "protected": wall
/// see-through would silently stop working. So a unit only counts as a prop when its horizontal
/// span is at most <see cref="FadeDriver.StandingPropMaxSpanWU"/> and it owns at most
/// <see cref="FadeDriver.StandingPropMaxRenderers"/> renderers. Both are far above this statue
/// (span 1.2 wu-class, 4 renderers) and far below any wall run or room container. The census
/// prints the unit's span and renderer count on every protected prop, so the next hardware log
/// shows immediately if a tileset ever sits near either cap.</para>
///
/// <para>DIAGNOSTIC (the "one grep away" requirement): the <c>STANDING PROP</c> census names
/// every protected unit with its foot height, span and renderer count, AND every wall-renderer
/// claim it blocked this rescan with the segment that would have taken it. A future creature
/// part swallowed by a wall is therefore either already in that list (fixed) or provably not a
/// floor-standing figure prop (a different cause, and the line says so).</para>
///
/// <para>NOT THE PARKED PROBLEM. <c>.planning/wall-fade-stereo-rivalry.md</c> is about the
/// masonry shader discarding the SAME wall in one eye and not the other — a per-eye disagreement
/// on a renderer the fade legitimately owns. This is a per-RENDERER ownership defect: the skull
/// was faded in both eyes, correctly and consistently, by a wall that should never have owned
/// it. Nothing here touches the discard math, the vignette, <c>_Cutoff</c> or the dissolve, and
/// nothing here reopens that topic.</para>
///
/// <para>MULTIPLAYER: purely local. This only removes renderers from a LOCAL wall's own list; it
/// produces no decision, no wire record and no peer-visible state, and every peer runs the
/// identical geometric rule against the identical scene (the report's three machines all logged
/// the same claim, so all three drop it the same way).</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>How far above its room's floor plane a prop's LOWEST point may sit and still
        /// count as standing ON that floor. Deliberately the same 1.0 wu as
        /// <c>GroundExclusionHeightWU</c>: that constant already encodes this tileset's "within
        /// this band of the floor is ground, never wall", and a prop whose foot is inside the
        /// ground band is standing on the ground by the same measurement.</summary>
        private const float StandingPropFootBandWU = 1.0f;

        /// <summary>Largest horizontal extent a figure/actor unit may have and still be a PROP.
        /// The anti-catastrophe cap (see the file header): an Animator sitting on a room-sized
        /// container must never turn that whole container into a protected prop. 6.0 wu is
        /// several times the skeleton statue's footprint and well under this tileset's wall
        /// runs and room containers (the report's rooms measure 6.7 x 6.9 wu and up, walls tens
        /// of wu long).</summary>
        private const float StandingPropMaxSpanWU = 6.0f;

        /// <summary>Most renderers a figure/actor unit may own and still be a PROP — the second
        /// half of the same cap. The statue has 4; the report's revealed map tiles carry 30-520
        /// renderers each, so nothing structural can slip under this.</summary>
        private const int StandingPropMaxRenderers = 24;

        /// <summary>Per-rescan verdict memo, keyed by the PROP UNIT root (not the renderer):
        /// one union-bounds computation per unit, reused by every renderer under it. Cleared at
        /// the start of every rescan — never holds transform references across frames, the same
        /// discipline <see cref="FigureAncestryMemo"/> keeps.</summary>
        private readonly Dictionary<Transform, bool> _standingPropVerdict = new(64);

        /// <summary>Description of each protected unit for the census, keyed by unit root.</summary>
        private readonly Dictionary<Transform, string> _standingPropDesc = new(64);

        /// <summary>Scratch for a unit's renderer sweep (reused; never held).</summary>
        private readonly List<Renderer> _standingUnitScratch = new(32);

        /// <summary>Wall-renderer claims refused this rescan: "'renderer' → 'segment'".</summary>
        private readonly List<string> _standingBlocked = new(8);

        /// <summary>How many claims were refused this rescan (the list above is capped).</summary>
        private int _standingBlockedCount;

        /// <summary>Change-triggered census signature — this line must never become per-rescan
        /// spam (the arch/water-rect lesson).</summary>
        private int _standingCensusSig = -1;

        /// <summary>Open a fresh standing-prop scope for one rescan: drop last rescan's verdicts
        /// (Apparance rebirths these props constantly, so a cached transform is a dangling
        /// reference within a couple of seconds) and reset the block census.</summary>
        private void BeginStandingPropScope()
        {
            _standingPropVerdict.Clear();
            _standingPropDesc.Clear();
            _standingBlocked.Clear();
            _standingBlockedCount = 0;
        }

        /// <summary>The PROP UNIT of a renderer: the nearest ancestor (itself included) carrying
        /// one of the figure/actor components. Null when there is none — then the renderer is
        /// not a figure/actor renderer at all and this file has no opinion about it.</summary>
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

        /// <summary>
        /// Is this renderer part of a figure/actor prop that STANDS ON THE FLOOR — and therefore
        /// never wall geometry, whatever shader or material slot it carries? See the file header
        /// for the three terms and the two caps.
        /// </summary>
        private bool IsStandingFigureProp(Renderer r)
        {
            // Term 1 — the same "not scenery" predicate every other sweep uses. Cheapest first:
            // a renderer with no figure ancestry can never reach the rest of this method.
            if (r == null || !IsFigureOrActorRenderer(r))
                return false;
            Transform? root = FigurePropRootOf(r.transform);
            if (root == null)
                return false; // skinned-only match: no unit to protect as a whole
            if (_standingPropVerdict.TryGetValue(root, out bool cached))
                return cached;

            // Term 2 — the verdict is a property of the UNIT, computed once for all its pieces.
            _standingUnitScratch.Clear();
            root.GetComponentsInChildren(includeInactive: true, _standingUnitScratch);
            bool verdict = false;
            if (_standingUnitScratch.Count > 0
                && _standingUnitScratch.Count <= StandingPropMaxRenderers)
            {
                Bounds union = default;
                bool have = false;
                foreach (Renderer piece in _standingUnitScratch)
                {
                    if (piece == null || IsModObject(piece))
                        continue;
                    if (!have) { union = piece.bounds; have = true; }
                    else union.Encapsulate(piece.bounds);
                }
                if (have
                    && Mathf.Max(union.size.x, union.size.z) <= StandingPropMaxSpanWU
                    && NearestAnchoredFloorY(union.min.y, out float floorY)
                    && union.min.y <= floorY + StandingPropFootBandWU)
                {
                    verdict = true;
                    _standingPropDesc[root] =
                        $"'{root.name}' foot {(union.min.y - floorY):0.0} wu over floor "
                        + $"{floorY:0.0}, span {union.size.x:0.0}x{union.size.z:0.0} wu, "
                        + $"{_standingUnitScratch.Count} renderer(s)";
                }
            }
            _standingUnitScratch.Clear();
            _standingPropVerdict[root] = verdict;
            return verdict;
        }

        /// <summary>The anchored room floor plane nearest to a prop's foot. Rooms can be
        /// terraced (the registry keeps same-CMap volumes at different heights apart on
        /// purpose), so the LOWEST floor in the scene is the wrong reference for a prop standing
        /// on an upper terrace. False when no room is anchored at all — and with zero anchors
        /// every wall is fail-safe solid anyway, so refusing to protect costs nothing.</summary>
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

        /// <summary>Record a refused wall-renderer claim for the census (capped list, full
        /// count). Called from the one choke point every collection path goes through.</summary>
        private void NoteStandingPropBlocked(Renderer r, Segment seg)
        {
            _standingBlockedCount++;
            if (_standingBlocked.Count >= 6)
                return;
            string wall = seg.Anchor != null ? seg.Anchor.name : "<no anchor>";
            string entry = $"'{r.name}' → '{wall}'";
            if (!_standingBlocked.Contains(entry))
                _standingBlocked.Add(entry);
        }

        /// <summary>
        /// The proof line. Names every protected prop unit (with the measurements the verdict
        /// was made on) and every wall-renderer claim refused this rescan, so a future
        /// creature-part-eaten-by-a-wall report is one grep away from being either "already in
        /// this list" or "a different cause". Change-triggered, like the water census.
        /// </summary>
        private void LogStandingPropCensus()
        {
            int protectedUnits = 0;
            foreach (KeyValuePair<Transform, bool> kv in _standingPropVerdict)
            {
                if (kv.Value)
                    protectedUnits++;
            }
            int sig = protectedUnits * 977 + _standingBlockedCount * 13 + _standingBlocked.Count;
            if (sig == _standingCensusSig)
                return;
            _standingCensusSig = sig;
            if (protectedUnits == 0 && _standingBlockedCount == 0)
                return;

            var names = new System.Text.StringBuilder();
            foreach (KeyValuePair<Transform, string> kv in _standingPropDesc)
            {
                if (names.Length > 200)
                    break;
                if (names.Length > 0)
                    names.Append("; ");
                names.Append(kv.Value);
            }
            VRLog.Info(Name,
                $"STANDING PROP: {protectedUnits} figure/actor prop(s) STAND ON THE FLOOR and are "
                + $"never wall geometry on any path — whole prop, every renderer (user report "
                + $"2026-08-15, skelet.jpg: 'der Kopf eines Skelets wurde mit der Wand mit "
                + $"ausgeblendet'). Rule: figure/actor ancestry AND the unit's union AABB reaches "
                + $"within {StandingPropFootBandWU:0.0} wu of its room's floor AND the unit is "
                + $"prop-sized (span ≤ {StandingPropMaxSpanWU:0.0} wu, ≤ {StandingPropMaxRenderers} "
                + $"renderers) — wall-MOUNTED dressing never reaches the floor and keeps fading "
                + $"with its masonry. Protected: {names}. "
                + $"{_standingBlockedCount} wall-renderer claim(s) refused this rescan"
                + (_standingBlocked.Count > 0
                    ? $": {string.Join(", ", _standingBlocked)}."
                    : "."));
        }
    }
}
