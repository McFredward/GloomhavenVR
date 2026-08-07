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
        /// <summary>Gate columns: the door prop's seed center (the gate-lift link test).</summary>
        public Vector3 GateSeedCenter;
        /// <summary>ROUND-12 GATE LIFT: the gate column whose face this wall EMBEDS (its XZ
        /// footprint contains the door). The keep's gatehouse wall ('Wall 3') owns the
        /// embedding masonry as its own toggle-native renderers — when the gate column's
        /// decision turns ON, this wall's fade target lifts with it, so the masonry
        /// dissolves NATIVELY (full animation) instead of being fought over by adopters
        /// (the round-12 ownership-churn flicker). Null for most walls.</summary>
        public Segment? GateLift;
        /// <summary>Last gate-lift contribution (edge logging only).</summary>
        public bool GateLiftActive;
        /// <summary>Round-13 lift LINGER: the lift holds until this time even if the gate
        /// segment died (Apparance prop churn) — bridges the rebirth window so the
        /// embedding wall does not flap with the prop lifecycle.</summary>
        public float GateLiftUntil;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>XZ expansion of the door AABB that defines the GEOMETRIC arch rect (wu).
        /// ROUND 12 tightened 1.0 → 0.25 (user: "zwei Säulen die nicht zum Rechteck
        /// gehören" — the flanking pillars sat inside the old 1.0-wu inflation and were
        /// wrongly arch-protected; the frame trims hug the door at gap 0.01–0.08, so 0.25
        /// still holds the authored rectangle). Door-NAMED assets (sign, trims) keep a
        /// wider reach via <see cref="ArchNameExtraWU"/>.</summary>
        private const float ArchMarginXZ = 0.25f;
        /// <summary>Extra XZ reach (wu) for the door-NAMED membership rule only — the
        /// authored door family (CV_DoorSign at gap 0.81) belongs to the arch even beyond
        /// the tight geometric rect.</summary>
        private const float ArchNameExtraWU = 0.75f;
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

        // ---- ROUND-13 LIFECYCLE PROOFING --------------------------------------------------
        // ModBuild-71 log: the GATE COLUMN rect was re-logged every rescan and the heartbeat
        // said 0 GATE columns — Apparance regen churn DESTROYS the UnityGameEditorDoorProp,
        // the dead-anchor sweep removes the gate keyed on it, and until the next rescan
        // re-seeds a FRESH segment there is (a) no arch protection (the arch sign/trims/fire
        // particles mounted on Wall 1 and went out with it — 51 churn WARNs) and (b) a
        // decision reset (EMA/dwell restart from zero every ≤2s, so the gate never reached
        // ON and zero GATE-LIFT lines exist). Three defenses:
        //   1. ARCH RECTS PERSIST INDEPENDENTLY of segment liveness (_archRects, pruned only
        //      after ArchRectRetainSeconds unseen / scene load) — protection can never gap.
        //   2. GATE MEMORY transplants Fade/State/log-sig onto the reborn segment (keyed by
        //      the quantized door center), so the decision survives prop churn.
        //   3. The GATE LIFT lingers GateLiftLingerSeconds past the gate's death, bridging
        //      the no-prop window without unfading the embedding wall.

        /// <summary>A persistent arch-protection rectangle (see above).</summary>
        private struct ArchRect
        {
            public float MinX, MaxX, MinZ, MaxZ, TopY;
            public float LastSeen;
        }

        private readonly List<ArchRect> _archRects = new();
        private const float ArchRectRetainSeconds = 10f;

        /// <summary>Reborn-gate state memory, keyed by the quantized door center.</summary>
        private readonly Dictionary<(int, int), (float fade, bool state, float sig)>
            _gateMemory = new();

        /// <summary>How long the gate-lift holds after the gate segment vanished (prop
        /// churn) — rescans re-seed within ~2s, so 4s bridges every observed gap.</summary>
        private const float GateLiftLingerSeconds = 4f;

        private static (int, int) GateMemoryKey(Vector3 center) =>
            (Mathf.RoundToInt(center.x * 2f), Mathf.RoundToInt(center.z * 2f));

        /// <summary>Upsert the persistent arch rect for a freshly-seeded gate and prune
        /// stale entries (nothing seen for <see cref="ArchRectRetainSeconds"/>).</summary>
        private void UpsertArchRect(Segment gate)
        {
            float now = Time.unscaledTime;
            for (int i = _archRects.Count - 1; i >= 0; i--)
            {
                ArchRect a = _archRects[i];
                bool same = Mathf.Abs(a.MinX - gate.ArchMinX) < 0.6f
                    && Mathf.Abs(a.MinZ - gate.ArchMinZ) < 0.6f;
                if (same)
                {
                    _archRects.RemoveAt(i);
                }
                else if (now - a.LastSeen > ArchRectRetainSeconds)
                {
                    _archRects.RemoveAt(i);
                }
            }
            _archRects.Add(new ArchRect
            {
                MinX = gate.ArchMinX, MaxX = gate.ArchMaxX,
                MinZ = gate.ArchMinZ, MaxZ = gate.ArchMaxZ,
                TopY = gate.ArchTopY, LastSeen = now,
            });
        }

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
                    // ROUND-13 GATE MEMORY: Apparance prop churn destroys the door prop and
                    // with it this segment (dead-anchor sweep) — the reborn gate inherits
                    // the previous incarnation's fade/decision/log state so the EMA-dwell
                    // pipeline does NOT restart from zero every ≤2s (the round-13 log:
                    // rect re-logged every rescan, gate never reached ON, zero GATE-LIFT).
                    if (_gateMemory.TryGetValue(GateMemoryKey(seed.center),
                            out (float fade, bool state, float sig) mem))
                    {
                        gate.Fade = mem.fade;
                        gate.State = mem.state;
                        gate.ArchLogSig = mem.sig;
                    }
                }
                BeginRefresh(gate);
                gate.IsGateColumn = true;
                gate.Bounds = seed;
                gate.HasBounds = true;
                gate.GateSeedCenter = seed.center;
                gate.ShaderNames = "gate column (doorway-embedding wall — user ruling 2026-08-07)";
                gate.ArchMinX = seed.min.x - ArchMarginXZ;
                gate.ArchMaxX = seed.max.x + ArchMarginXZ;
                gate.ArchMinZ = seed.min.z - ArchMarginXZ;
                gate.ArchMaxZ = seed.max.z + ArchMarginXZ;
                gate.ArchTopY = seed.max.y + ArchHeadroomWU;
                FinishRefresh(gate);
                UpsertArchRect(gate); // persistent protection (round 13)
                _gateMemory[GateMemoryKey(seed.center)] =
                    (gate.Fade, gate.State, gate.ArchLogSig);

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
        private static float ArchContainmentFraction(in ArchRect a, Bounds b)
        {
            float ox = Mathf.Min(b.max.x, a.MaxX) - Mathf.Max(b.min.x, a.MinX);
            float oz = Mathf.Min(b.max.z, a.MaxZ) - Mathf.Max(b.min.z, a.MinZ);
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
        private static bool IsArchRectPiece(in ArchRect a, Bounds b, string name)
        {
            Vector3 c = b.center;
            bool centerIn = c.x >= a.MinX && c.x <= a.MaxX
                && c.z >= a.MinZ && c.z <= a.MaxZ;
            // Door-NAMED family (sign, trims): wider reach than the tight geometric rect.
            if (name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0
                && c.x >= a.MinX - ArchNameExtraWU && c.x <= a.MaxX + ArchNameExtraWU
                && c.z >= a.MinZ - ArchNameExtraWU && c.z <= a.MaxZ + ArchNameExtraWU)
                return true;
            if (b.max.y > a.TopY)
                return false;
            if (ArchContainmentFraction(in a, b) >= ArchContainmentMin)
                return true;
            return centerIn
                && b.size.x <= (a.MaxX - a.MinX) + 2f * ArchOverhangWU
                && b.size.z <= (a.MaxZ - a.MinZ) + 2f * ArchOverhangWU;
        }

        /// <summary>
        /// ROUND-12 GATE-LIFT LINK (called at the end of Rescan, bounds final): bind every
        /// wall whose XZ footprint contains a gate's door center (expanded 0.5 wu slack) to
        /// that gate — it IS the embedding wall (the gatehouse 'Wall 3' whose own renderers
        /// are the masonry the round-12 churn fought over). Its fade target then lifts with
        /// the gate column's decision and the masonry dissolves natively. Doorway segments
        /// and gates themselves never link.
        /// </summary>
        private void LinkGateLifts()
        {
            foreach (Segment seg in _segments.Values)
            {
                seg.GateLift = null;
                if (seg.IsGateColumn || seg.DoorRoot != null || !seg.HasBounds)
                    continue;
                foreach (Segment gate in _segments.Values)
                {
                    if (!gate.IsGateColumn || !gate.HasBounds)
                        continue;
                    Vector3 c = gate.GateSeedCenter;
                    if (c.x >= seg.Bounds.min.x - 0.5f && c.x <= seg.Bounds.max.x + 0.5f
                        && c.z >= seg.Bounds.min.z - 0.5f && c.z <= seg.Bounds.max.z + 0.5f)
                    {
                        seg.GateLift = gate;
                        break;
                    }
                }
            }
        }

        /// <summary>Edge log for gate-lift fades (a lifted wall never flips its own State,
        /// so LogStateFlip stays silent — this line is its counterpart).</summary>
        private void LogGateLiftEdge(Segment seg, bool lifted)
        {
            if (lifted == seg.GateLiftActive)
                return;
            seg.GateLiftActive = lifted;
            if (seg.State)
                return; // locally faded anyway
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            string gate = seg.GateLift?.Anchor != null ? seg.GateLift.Anchor.name : "?";
            VRLog.Info(Name, lifted
                ? $"GATE-LIFT fade ON '{wall}' (embedding wall of '{gate}' — the masonry "
                  + "dissolves natively with the gate face, round 12)"
                : $"GATE-LIFT fade OFF '{wall}' (gate face solid again).");
        }

        /// <summary>Does this door root have a LIVE gate column (an arch rect exists)?
        /// Round-11 torch fix: only then can the doorway grouping be narrowed to the arch —
        /// sliver-skipped doors keep the old full-radius grouping.</summary>
        private bool HasGateColumnFor(Transform doorRoot)
        {
            UnityGameEditorDoorProp? dp = doorRoot.GetComponent<UnityGameEditorDoorProp>();
            return dp != null && _segments.TryGetValue(dp, out Segment? g) && g.IsGateColumn;
        }

        /// <summary>Piece inside ANY arch — excluded from EVERY adopter (stack, corner,
        /// fast reclaim, mounted): the arch is the doorway ruling's permanently solid
        /// remainder. ROUND 13: reads the PERSISTENT rect list, not gate segments — the
        /// protection holds even while Apparance prop churn kills and rebirths the gate
        /// segment (the round-13 arch-torch blackout: with the gate dead at sweep time,
        /// the sign/trims/fire particles mounted on Wall 1 and faded with it).</summary>
        private bool IsArchProtected(Bounds b, string name) =>
            IsArchProtected(b, name, out _);

        /// <summary>Overload reporting the matched arch's containment fraction (census).</summary>
        private bool IsArchProtected(Bounds b, string name, out float containment)
        {
            containment = 0f;
            for (int i = 0; i < _archRects.Count; i++)
            {
                ArchRect a = _archRects[i];
                if (IsArchRectPiece(in a, b, name))
                {
                    containment = ArchContainmentFraction(in a, b);
                    return true;
                }
            }
            return false;
        }
    }
}
