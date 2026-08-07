using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// GATE COLUMN — the doorway-embedding split (user ruling 2026-08-07, torbogen_mauer.png,
/// refining the 2026-08-02 doorway ruling): "Es ist gewollt, dass der Torbogen direkt nicht
/// verschwindet, ABER hier ist der Torbogen in einer größeren Mauer eingebettet. Das Drumrum
/// soll genauso reagieren wie alle anderen Mauern. Nur der Torbogen — die rechteckigen
/// Assets direkt um das Tor — bleiben."
///
/// THE PERMANENTLY-SOLID SET of a doorway is therefore ONLY the rectangular ARCH:
/// <list type="bullet">
/// <item>the DOORWAY segment's own renderers (the door leaf + everything the 2026-08-02
///   recognition groups per door — unchanged), and</item>
/// <item>the ARCH RECT (read from the ModBuild-66 data): the door prop's AABB expanded by
///   <see cref="FadeDriver.ArchMarginXZ"/> (1.0 wu) in XZ, up to door-top +
///   <see cref="FadeDriver.ArchHeadroomWU"/> (1.3 wu — door top 2.2 → 3.5, covering the
///   authored frame trims 'TO_EXT_House_Door_trim' y[1.4..3.2] and the 'CV_DoorSign'
///   y[2.7..3.4] at gap ≤ 0.81), plus any '*Door*'-named asset whose center lies in the
///   rect regardless of the headroom (the authored door-frame family).</item>
/// </list>
///
/// EVERYTHING ELSE embedding the gate — 'polySurface1/2' y[3.1..6.0], the fort courses
/// above the arch — must fade like any wall. The log proved neither flanking columns nor
/// corner pieces can reach them ("only a DOORWAY nearby … no fadeable wall within reach":
/// the nearest fadeable wall is beyond even the near-miss radius), so each door gets its
/// own GATE COLUMN: a pseudo-segment keyed by the <c>UnityGameEditorDoorProp</c> COMPONENT
/// (a distinct dictionary key on the same object the doorway segment anchors by transform),
/// seeded with the door AABB. The stacked pass then adopts the embedding pieces onto it
/// (same band/face/engulf/figure rules; the arch rect itself is excluded from EVERY
/// adopter), the normal merged-room coverage decides it exactly like other walls, and the
/// pieces ride its fade through the established enabled/ramp delivery. Approaching the
/// gate face opens the wall around the arch; the arch stands.
///
/// ROUND 10 (ModBuild-68 hardware log — the gate column reached fade 1.00 but drove ZERO
/// renderers, every embedding piece "ARCH"-rejected): the seed/rect had been derived from
/// the WHOLE door-prop subtree union — the prop parents the full 6-wu gatehouse wall
/// piece, so the arch rect covered the entire face AND the stack band's origin top sat at
/// 6.03 (all courses below it). Fixed: the arch derives from the '*Door*'-NAMED renderers
/// of the subtree only (fallback: the lowest <see cref="FadeDriver.FallbackArchHeightWU"/>
/// wu, logged as FALLBACK), membership is CONTAINMENT (≥60% XZ inside, or centered with
/// ≤0.5 wu overhang — never mere intersection: a wall-wide course crossing the rect is
/// embedding), and the change-triggered "GATE COLUMN … arch rect …" line plus containment
/// fractions in the reject census let the next hardware log verify the rect is tight.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>GATE COLUMN marker (user ruling 2026-08-07): a fadeable pseudo-wall for
        /// the geometry EMBEDDING a doorway. Never carries wall renderers of its own; its
        /// pieces arrive via the stacked pass, its AABB seeds from the door prop.</summary>
        public bool IsGateColumn;
        /// <summary>The ARCH RECT this gate keeps permanently solid (see the file header).
        /// Valid only while <see cref="IsGateColumn"/>.</summary>
        public float ArchMinX, ArchMaxX, ArchMinZ, ArchMaxZ, ArchTopY;
        /// <summary>Signature of the last LOGGED arch rect (change-triggered log — the
        /// next hardware log must prove the rect is tight).</summary>
        public float ArchLogSig = float.NaN;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>XZ expansion of the door AABB that defines the arch rect (wu). Data:
        /// frame trims at gap 0.01–0.08, the door sign at 0.81 — 1.0 covers the authored
        /// rectangular arch without reaching the embedding courses (their tops break the
        /// headroom rule anyway).</summary>
        private const float ArchMarginXZ = 1.0f;
        /// <summary>Arch height above the door AABB top (wu). Data: door top 2.20, lintel
        /// trim top 3.2, door sign top 3.4 → 1.3 covers both; the embedding courses top
        /// 5.0–6.0 stay far outside.</summary>
        private const float ArchHeadroomWU = 1.3f;
        /// <summary>ROUND-10 CONTAINMENT rule (ModBuild-68 hardware log: the arch rect was
        /// derived from the 6-wu-tall prop-subtree union, so EVERY embedding piece was
        /// "ARCH"-rejected and the gate column faded 0 renderers): a piece is protected only
        /// when at least this fraction of its XZ footprint lies INSIDE the rect — mere
        /// intersection is not membership ('TO_Fort_WallTop02' spans the whole wall width
        /// and crosses the rect, yet it is embedding wall and must fade).</summary>
        private const float ArchContainmentMin = 0.6f;
        /// <summary>Alternative membership (coordinator spec): centered on the rect with at
        /// most this much span excess per side.</summary>
        private const float ArchOverhangWU = 0.5f;
        /// <summary>When the door prop subtree has NO door-named renderer to derive the arch
        /// from, the fallback arch is the lowest this-many wu of the subtree (the observed
        /// authored arch scale: door 2.2 + trims/sign to ~3.4). The gate log line marks the
        /// fallback so the next hardware round shows which path ran.</summary>
        private const float FallbackArchHeightWU = 3.5f;
        /// <summary>Minimum plausible arch-seed height (wu, round-11 sliver guard): a real
        /// door leaf is ≥ ~2 wu; a sub-0.8 seed is a floor marker, not an arch.</summary>
        private const float MinArchSeedHeightWU = 0.8f;
        /// <summary>Sliver-skipped door props already logged (once per scene).</summary>
        private readonly HashSet<string> _gateSliverLogged = new();

        /// <summary>
        /// Create/refresh one fadeable GATE COLUMN per live door prop (called from Rescan
        /// right after the door registry is rebuilt, BEFORE the adoption/stacked passes so
        /// the stacked pass sees its bounds and face domain). The segment is keyed by the
        /// door prop COMPONENT — distinct from the doorway segment's transform anchor.
        /// </summary>
        private void SeedGateColumns(UnityGameEditorDoorProp[] doorProps)
        {
            foreach (UnityGameEditorDoorProp dp in doorProps)
            {
                if (dp == null)
                    continue;
                // ROUND-10 FIX (ModBuild-68 log: gate column faded 0 renderers): the arch
                // must derive from the actual DOOR geometry — the '*Door*'-named renderers
                // of the prop subtree — NOT the whole subtree union: the prop parents the
                // full 6-wu gatehouse wall piece, so the old union made the arch rect cover
                // the entire face (everything "ARCH"-rejected) AND put the stack band's
                // origin top at 6.03 (every embedding course below the band). The seed is
                // now the arch-source union, so the band admits the courses at base 3.1–3.5
                // and the column grows from the arch upward.
                Bounds seed = default;
                bool have = false;
                Bounds all = default;
                bool haveAll = false;
                var srcNames = new System.Text.StringBuilder();
                int srcCount = 0;
                foreach (MeshRenderer r in
                    dp.GetComponentsInChildren<MeshRenderer>(includeInactive: false))
                {
                    if (r == null)
                        continue;
                    Bounds rb = r.bounds;
                    if (!haveAll)
                    {
                        all = rb;
                        haveAll = true;
                    }
                    else
                    {
                        all.Encapsulate(rb);
                    }
                    if (r.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (!have)
                    {
                        seed = rb;
                        have = true;
                    }
                    else
                    {
                        seed.Encapsulate(rb);
                    }
                    srcCount++;
                    if (srcNames.Length < 96)
                    {
                        if (srcNames.Length > 0)
                            srcNames.Append(", ");
                        srcNames.Append('\'').Append(r.name).Append('\'');
                    }
                }
                bool fallback = !have;
                if (fallback)
                {
                    if (!haveAll)
                        continue; // prop without renderers — nothing to anchor a gate on
                    // No door-named renderer: arch = the lowest FallbackArchHeightWU of the
                    // subtree (the authored arch scale) — logged as FALLBACK below.
                    Vector3 max = all.max;
                    max.y = Mathf.Min(max.y, all.min.y + FallbackArchHeightWU);
                    seed = default;
                    seed.SetMinMax(all.min, max);
                }
                // ROUND-11 SLIVER GUARD (ModBuild-69 log: two FALLBACK gates seeded from
                // floor-level slivers, seed wy[-0.40..-0.05], arch topY 1.2 — a rect at
                // ground height is no arch and its column/protection only destabilizes
                // nearby ownership): a door prop whose seed has no plausible arch height
                // seeds NO gate column at all. The DOORWAY segment ruling is unaffected.
                if (seed.size.y < MinArchSeedHeightWU)
                {
                    if (_segments.TryGetValue(dp, out Segment? stale) && stale.IsGateColumn)
                    {
                        RestoreSegmentStacked(stale);
                        RestoreSegmentMounted(stale);
                        _segments.Remove(dp);
                    }
                    if (_gateSliverLogged.Add(dp.name))
                        VRLog.Info(Name,
                            $"GATE COLUMN '{dp.name}': SKIPPED — seed is a floor-level "
                            + $"sliver (wy[{seed.min.y:F2}..{seed.max.y:F2}], height "
                            + $"{seed.size.y:F2} < {MinArchSeedHeightWU:0.0} wu), no "
                            + "plausible arch; no column, no protection rect.");
                    continue;
                }
                if (!_segments.TryGetValue(dp, out Segment? gate))
                {
                    gate = new Segment { Anchor = dp, FromWallCache = false, IsGateColumn = true };
                    _segments.Add(dp, gate);
                }
                BeginRefresh(gate);
                gate.IsGateColumn = true;
                gate.Bounds = seed;
                gate.HasBounds = true;
                gate.ShaderNames = "gate column (doorway-embedding wall — user ruling 2026-08-07)";
                gate.ArchMinX = seed.min.x - ArchMarginXZ;
                gate.ArchMaxX = seed.max.x + ArchMarginXZ;
                gate.ArchMinZ = seed.min.z - ArchMarginXZ;
                gate.ArchMaxZ = seed.max.z + ArchMarginXZ;
                gate.ArchTopY = seed.max.y + ArchHeadroomWU;
                FinishRefresh(gate);

                // Change-triggered rect log — the proof line the next hardware round reads.
                float sig = gate.ArchMinX + 3f * gate.ArchMaxX + 7f * gate.ArchMinZ
                    + 13f * gate.ArchMaxZ + 31f * gate.ArchTopY;
                if (float.IsNaN(gate.ArchLogSig) || Mathf.Abs(sig - gate.ArchLogSig) > 0.25f)
                {
                    gate.ArchLogSig = sig;
                    VRLog.Info(Name,
                        $"GATE COLUMN '{dp.name}': arch rect x[{gate.ArchMinX:F1}.."
                        + $"{gate.ArchMaxX:F1}] z[{gate.ArchMinZ:F1}..{gate.ArchMaxZ:F1}] "
                        + $"topY {gate.ArchTopY:F1} "
                        + (fallback
                            ? $"(FALLBACK — no door-named renderer; lowest {FallbackArchHeightWU:0.0} wu "
                              + "of the prop subtree)"
                            : $"(from {srcCount} door-named renderer(s): {srcNames})")
                        + $" — seed bounds wy[{seed.min.y:F2}..{seed.max.y:F2}]; only pieces "
                        + $"≥{ArchContainmentMin:0.00} XZ-contained (or centered, ≤"
                        + $"{ArchOverhangWU:0.0} wu overhang) are ARCH-protected; the "
                        + "embedding wall stack-adopts onto this column and fades.");
                }
            }
        }

        /// <summary>Fraction of the piece's XZ footprint that lies inside the gate's arch
        /// rect (0..1) — the round-10 membership metric AND the number the reject census
        /// prints, so the next hardware log proves the rect is tight.</summary>
        private static float ArchContainmentFraction(Segment gate, Bounds b)
        {
            float ox = Mathf.Min(b.max.x, gate.ArchMaxX) - Mathf.Max(b.min.x, gate.ArchMinX);
            float oz = Mathf.Min(b.max.z, gate.ArchMaxZ) - Mathf.Max(b.min.z, gate.ArchMinZ);
            if (ox <= 0f || oz <= 0f)
                return 0f;
            float area = Mathf.Max(b.size.x * b.size.z, 0.0001f);
            return Mathf.Clamp01(ox * oz / area);
        }

        /// <summary>Is the piece part of this gate's permanently-solid ARCH? Round-10 rules
        /// (containment, not intersection — a wall-wide course crossing the rect is
        /// embedding, not arch): an authored '*Door*'-named asset centered in the rect
        /// (regardless of headroom — trims, door sign), OR — under the headroom — a piece
        /// whose XZ footprint is ≥<see cref="ArchContainmentMin"/> inside the rect or
        /// centered with ≤<see cref="ArchOverhangWU"/> wu overhang per side.</summary>
        private static bool IsGateArchPiece(Segment gate, Bounds b, string name)
        {
            if (!gate.IsGateColumn)
                return false;
            Vector3 c = b.center;
            bool centerIn = c.x >= gate.ArchMinX && c.x <= gate.ArchMaxX
                && c.z >= gate.ArchMinZ && c.z <= gate.ArchMaxZ;
            if (centerIn && name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (b.max.y > gate.ArchTopY)
                return false;
            if (ArchContainmentFraction(gate, b) >= ArchContainmentMin)
                return true;
            return centerIn
                && b.size.x <= (gate.ArchMaxX - gate.ArchMinX) + 2f * ArchOverhangWU
                && b.size.z <= (gate.ArchMaxZ - gate.ArchMinZ) + 2f * ArchOverhangWU;
        }

        /// <summary>Does this door root have a LIVE gate column (an arch rect exists)?
        /// Round-11 torch fix: only then can the doorway grouping be narrowed to the arch —
        /// sliver-skipped doors keep the old full-radius grouping.</summary>
        private bool HasGateColumnFor(Transform doorRoot)
        {
            UnityGameEditorDoorProp? dp = doorRoot.GetComponent<UnityGameEditorDoorProp>();
            return dp != null && _segments.TryGetValue(dp, out Segment? g) && g.IsGateColumn;
        }

        /// <summary>Piece inside ANY gate's arch — excluded from EVERY adopter (stack,
        /// corner, fast reclaim, mounted): the arch is the doorway ruling's permanently
        /// solid remainder, refined 2026-08-07 to exactly this rectangle.</summary>
        private bool IsArchProtected(Bounds b, string name) =>
            IsArchProtected(b, name, out _);

        /// <summary>Overload reporting the matched gate's containment fraction (census).</summary>
        private bool IsArchProtected(Bounds b, string name, out float containment)
        {
            containment = 0f;
            foreach (Segment s in _segments.Values)
            {
                if (s.IsGateColumn && IsGateArchPiece(s, b, name))
                {
                    containment = ArchContainmentFraction(s, b);
                    return true;
                }
            }
            return false;
        }
    }
}
