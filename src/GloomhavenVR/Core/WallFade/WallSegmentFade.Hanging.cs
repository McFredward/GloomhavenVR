using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// HANGING PLANTS (ModBuild 410, nonfading_plants.jpg — mossy stone walls faded to translucent,
/// the ivy hanging down their faces fully solid and still hiding the figures behind it; user:
/// <i>"Efeu das noch die Sicht blockiert und nicht mit der Mauer mit-faded"</i>).
///
/// <para><b>THE MECHANISM, READ OFF THE ModBuild 407 LOG.</b> The LEFTOVER OVER A FADED WALL
/// rows name it: <c>[OBSTRUCTING] 'CR_RU_Vines (2)'[mesh] foot 0.93 wu / top 2.84 wu over room
/// 0's floor, hides 2 of 16 … NO ProceduralWall anywhere above it, no prop unit at all … DRAWING
/// 1.36 wu from 'Wall 3' whose fade is 1.00 — not adopted because: anchor 0.93 under the airborne
/// bar</c>, and <c>'CR_RU_Vines (3)' foot 0.72 / top 2.94 … gap 0.00 from 'Wall 1' fade 1.00</c>.
/// Three lanes, three reasons: (a) the FOLIAGE lane is scoped to a wall's OWN subtree (the
/// vines the log shows as <c>← foliage of 'Wall 3'</c> hang under
/// <c>Wall 3/Generated Content/…</c>; these hang under the tile, so no shader family could ever
/// have reached them); (b) the MOUNTED lane's first term is the airborne bar and a vine that
/// reaches down to 0.7-0.9 wu is <c>belowBar</c>; (c) the STANDING PROP rule does not refuse
/// them either — they have no prop unit — but nothing was left to take them. So a vine that
/// hangs on a wall face was, by construction, nobody's.</para>
///
/// <para><b>THE RULE.</b> Inside the mounted pass, after the free-standing riders and before the
/// leavers loop: a drawing MESH no segment owns, whose foot is under the airborne bar, rides the
/// nearest decision-valid wall (not a doorway, not a free-standing unit) when it hugs that wall
/// (XZ gap ≤ <see cref="FadeDriver.MountedLinkMaxXZ"/>), its top clears the ground band by
/// <see cref="FadeDriver.HangingPlantMinAboveBandWU"/> (the ground-band rule stands: a floor plant
/// topping out inside the band is never touched), and it is either taller than the standing-prop
/// height bar (<see cref="FadeDriver.FreeStandingMinTopWU"/>) or overlaps the wall's own Y span.
/// Every other refusal stands: figures, doorway assemblies, water, standing props by the two-arm
/// rule, architecture-scale meshes, non-occluders, and anything under a ProceduralWall (a wall
/// run's own leftover is the wall path's business, not a rider). A rider is a
/// <see cref="MountedProp"/> on the wall's <c>Mounted</c> list: it dissolves with the wall's own
/// ramp, is sticky while the wall is faded, and is restored bit-for-bit — the mounted lane's own
/// machinery, unchanged. Lights are never written.</para>
///
/// <para><b>INSTRUMENT.</b> The WALL-MOUNTED DRESSING line gains a hanging-plant clause: the
/// count adopted this rescan, up to four names with their wall and foot/top, the two refusal
/// counts, and a readable zero. The clause is part of that line's change trigger, so a rescan
/// that adopts a different number prints.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>A hanging plant's top must clear the ground band (floor + 1.0 wu) by this
        /// much: a bush topping out at 1.2 wu beside a wall is floor cover, a vine reaching 2.8 wu
        /// is hanging.</summary>
        private const float HangingPlantMinAboveBandWU = 0.5f;

        private const int HangingPlantNameCap = 4;

        private int _censusHangingPlants;
        private int _censusHangingPlantsRefusedStanding;
        private int _censusHangingPlantsRefusedBand;
        private int _lastLoggedHangingPlants = -1;
        private readonly List<string> _hangingPlantNames = new();

        /// <summary>ModBuild 411: every candidate that reached the election and was refused by a
        /// term OTHER than the ground band is named with its foot/top/gap and the term, up to
        /// this many — so a vine that stays solid is on the line with the reason, and "94 in
        /// the ground band" can no longer stand in for it.</summary>
        private const int HangingRefusalNameCap = 4;
        private int _censusHangingPlantsRefusedOther;
        private readonly List<string> _hangingRefusalNames = new();

        /// <summary>ModBuild 412: refusals are kept PER TERM — a count and up to two names each —
        /// so a flat list capped at four can no longer hide a vine behind two light shafts and a
        /// door frame (the 411 log: "refused by another term: 16 (LightShaft…, CV_StoneDoorFrame…,
        /// +12 more)" and no 'Vines' named).</summary>
        private readonly Dictionary<string, int> _hangingRefusalByTerm = new();
        private readonly Dictionary<string, string> _hangingRefusalNamesByTerm = new();
        private const int HangingRefusalNamesPerTerm = 2;

        private void NoteHangingRefusal(string name, float foot, float top, float gap, string term)
        {
            _censusHangingPlantsRefusedOther++;
            _hangingRefusalByTerm.TryGetValue(term, out int seen);
            _hangingRefusalByTerm[term] = seen + 1;
            if (seen >= HangingRefusalNamesPerTerm)
                return;
            string row = $"'{name}' foot {foot:F2} / top {top:F2} wu over the floor, nearest wall gap "
                + (float.IsInfinity(gap) ? "none" : gap.ToString("F2"));
            _hangingRefusalNamesByTerm[term] = seen == 0
                ? row
                : _hangingRefusalNamesByTerm[term] + "; " + row;
            if (_hangingRefusalNames.Count < HangingRefusalNameCap)
                _hangingRefusalNames.Add(row + $" — {term}");
        }

        /// <summary>ModBuild 412: the ground-band refusals are named too (up to four, with the
        /// wall's own Y span beside the plant's foot/top), so "N in the ground band" is decidable
        /// against a vine on a cliff face the next time.</summary>
        private readonly List<string> _hangingBandNames = new();

        private void NoteHangingBandRefusal(string name, float foot, float top, Segment wall, float floorY)
        {
            _censusHangingPlantsRefusedBand++;
            if (_hangingBandNames.Count >= HangingRefusalNameCap)
                return;
            string w = wall.Anchor != null ? wall.Anchor.name : "<dead>";
            _hangingBandNames.Add(
                $"'{name}' foot {foot:F2} / top {top:F2} wu over room floor {floorY:F2}, beside "
                + $"'{w}' wy[{wall.Bounds.min.y - floorY:F2}..{wall.Bounds.max.y - floorY:F2}] over that floor");
        }

        private void CollectHangingPlants(float minFloorY)
        {
            _censusHangingPlants = 0;
            _censusHangingPlantsRefusedStanding = 0;
            _censusHangingPlantsRefusedBand = 0;
            _censusHangingPlantsRefusedOther = 0;
            _hangingPlantNames.Clear();
            _hangingRefusalNames.Clear();
            _hangingRefusalByTerm.Clear();
            _hangingRefusalNamesByTerm.Clear();
            _hangingBandNames.Clear();
            if (float.IsInfinity(minFloorY) || _factCount == 0)
                return;

            float airborneBar = minFloorY + MountedClearanceWU;
            float bandTop = minFloorY + GroundExclusionHeightWU + HangingPlantMinAboveBandWU;
            float reachMinX = float.PositiveInfinity, reachMaxX = float.NegativeInfinity;
            float reachMinZ = float.PositiveInfinity, reachMaxZ = float.NegativeInfinity;
            foreach (Segment seg in _live.Segments.Values)
            {
                if (!seg.HasBounds || seg.IsFreeStanding || seg.DoorRoot != null)
                    continue;
                if (seg.Bounds.min.x < reachMinX) reachMinX = seg.Bounds.min.x;
                if (seg.Bounds.max.x > reachMaxX) reachMaxX = seg.Bounds.max.x;
                if (seg.Bounds.min.z < reachMinZ) reachMinZ = seg.Bounds.min.z;
                if (seg.Bounds.max.z > reachMaxZ) reachMaxZ = seg.Bounds.max.z;
            }
            if (float.IsInfinity(reachMinX))
                return;
            float reach = MountedLinkMaxXZ + CensusBoundsSlackWU;
            reachMinX -= reach; reachMaxX += reach;
            reachMinZ -= reach; reachMaxZ += reach;

            for (int fi = 0; fi < _factCount; fi++)
            {
                ref RendererFact f = ref _facts[fi];
                // Meshes only — a particle system is the mounted lane's own class and is judged
                // by its emitter there. A wall-fade shader has its own tracking. A FOLIAGE
                // shader does NOT skip (ModBuild 411): the ModBuild 410 version skipped
                // f.FoliageShader here, uncounted, and the ivy IS Foliage-family — the 407 log
                // shows the same 'CR_RU_Vines' assets as "← foliage of 'Wall 3'" where they hang
                // under a wall's subtree. The foliage lane is scoped to that subtree, so a
                // tile-borne vine on a Foliage shader was skipped by this pass for carrying the
                // one shader the other lane could not reach it on. That was the whole defect
                // ("94 candidate(s) … in the ground band" were the floor cover; the vines were
                // never among the candidates at all).
                if (f.Mesh == null || f.Mod || f.Figure || f.Particles
                    || f.WallFadeShader || f.WaterSurface)
                {
                    continue;
                }
                // Cached bounds may only REJECT: clearly airborne (the mounted lane's population)
                // or clearly inside the ground band.
                if (f.Bounds.min.y >= airborneBar + CensusBoundsSlackWU)
                    continue;
                if (f.Bounds.max.y < bandTop - CensusBoundsSlackWU)
                    continue;
                if (f.Bounds.max.x < reachMinX || f.Bounds.min.x > reachMaxX
                    || f.Bounds.max.z < reachMinZ || f.Bounds.min.z > reachMaxZ)
                {
                    continue;
                }
                MeshRenderer r = f.Mesh!;
                if (r == null || _mountedOwned.Contains(r) || _attachmentOwned.ContainsKey(r))
                    continue;
                if (!r.enabled && !_mountedTouched.ContainsKey(r))
                    continue; // the GAME disabled it — not ours to manage
                Bounds b = r.bounds;
                float anchorY = b.min.y;
                float topY = b.max.y;
                if (anchorY >= airborneBar)
                    continue; // airborne dressing is the mounted lane's, judged one loop up

                Segment? best = null;
                float bestGap = float.PositiveInfinity;
                float nearestGap = float.PositiveInfinity; // any wall, for the refusal row
                float nearestFloor = minFloorY;            // that wall's room floor, ditto
                bool bandRefused = false;
                bool spanRefused = false;
                Segment? bandWall = null;                   // the wall the band refusal was judged against
                float bandFloor = minFloorY;                // and that wall's room floor
                foreach (Segment seg in _live.Segments.Values)
                {
                    if (!seg.HasBounds || seg.IsFreeStanding || seg.DoorRoot != null
                        || !RoomDecisionValid(seg.RoomIndex))
                    {
                        continue;
                    }
                    float gap = HorizontalGap(seg.Bounds, b);
                    if (gap < nearestGap)
                    {
                        nearestGap = gap;
                        nearestFloor = _live.RoomFloorY[seg.RoomIndex];
                    }
                    if (gap > MountedLinkMaxXZ || gap >= bestGap)
                        continue;
                    float floorY = _live.RoomFloorY[seg.RoomIndex];
                    // ORDER OF TERMS (ModBuild 412). The ground band protects FLOOR COVER ON THE
                    // ROOM FLOOR: a plant that stands on that floor (foot at or above it, within
                    // a quarter band) and tops out inside the band. It does not protect a cliff
                    // face BELOW the room floor (nonfading_plants.jpg — the ivy hangs on the
                    // outer faces of a raised platform, so against the room floor it "tops out in
                    // the band" while hugging a faded wall's face and overlapping that wall's own
                    // Y span). So: floor cover is refused; everything else is judged on the wall
                    // face — taller than the standing-prop height bar, or overlapping the wall's
                    // own span — whatever the room floor says.
                    bool standsOnRoomFloor = anchorY >= floorY - GroundExclusionHeightWU * 0.25f;
                    bool inBand = topY < floorY + GroundExclusionHeightWU + HangingPlantMinAboveBandWU;
                    if (standsOnRoomFloor && inBand)
                    {
                        bandRefused = true;
                        bandWall = seg;
                        bandFloor = floorY;
                        continue;
                    }
                    bool tall = topY >= floorY + FreeStandingMinTopWU;
                    bool onFace = topY >= seg.Bounds.min.y && anchorY <= seg.Bounds.max.y;
                    if (!tall && !onFace)
                    {
                        spanRefused = true;
                        continue;
                    }
                    best = seg;
                    bestGap = gap;
                }
                string name = f.Name ?? r.name;
                float footOver = anchorY - nearestFloor;
                float topOver = topY - nearestFloor;
                if (best == null)
                {
                    if (bandRefused && bandWall != null)
                        NoteHangingBandRefusal(name, anchorY - bandFloor, topY - bandFloor, bandWall, bandFloor);
                    else if (spanRefused)
                        NoteHangingRefusal(name, footOver, topOver, nearestGap,
                            $"neither taller than {FreeStandingMinTopWU:0.0} wu nor overlapping the wall's own Y span");
                    else if (nearestGap > MountedLinkMaxXZ)
                        NoteHangingRefusal(name, footOver, topOver, nearestGap,
                            $"no decision-valid wall within {MountedLinkMaxXZ:0.00} wu");
                    continue;
                }
                if (IsDoorwayAssembly(r, b, name, out string doorWhy))
                {
                    NoteDoorwayRefusal("hanging plant", name, b, doorWhy);
                    NoteHangingRefusal(name, footOver, topOver, bestGap, "doorway — " + doorWhy);
                    continue; // doorways never fade
                }
                if (IsWaterProtected(b))
                {
                    NoteHangingRefusal(name, footOver, topOver, bestGap, "WATER rect (user ruling 2026-08-09)");
                    continue;
                }
                if (IsFigureOrActorRenderer(r))
                {
                    NoteHangingRefusal(name, footOver, topOver, bestGap, "FIGURE (round-7 ruling)");
                    continue;
                }
                if (IsNonOccludingRenderer(r))
                {
                    NoteHangingRefusal(name, footOver, topOver, bestGap, "non-occluding shader/queue");
                    continue;
                }
                if (IsArchitectureScale(b.size, 0f))
                {
                    NoteHangingRefusal(name, footOver, topOver, bestGap,
                        "architecture-scale (2 fat axes or volume > 1.5 wu³) — a wall's or a unit's business");
                    continue;
                }
                if (r.GetComponentInParent<ProceduralWall>() != null)
                {
                    NoteHangingRefusal(name, footOver, topOver, bestGap,
                        "under a ProceduralWall — the wall path's own leftover, named on the LEFTOVER line");
                    continue;
                }
                if (IsStandingFigureProp(r))
                {
                    _censusHangingPlantsRefusedStanding++;
                    NoteHangingRefusal(name, footOver, topOver, bestGap, "STANDING PROP (two-arm rule)");
                    continue; // a floor-standing prop unit keeps its protection
                }
                if (best.Mounted.Count >= MountedMaxPerSegment)
                {
                    NoteHangingRefusal(name, footOver, topOver, bestGap,
                        $"wall SATURATED at {MountedMaxPerSegment} mounted props");
                    continue;
                }
                if (!_mountedTouched.TryGetValue(r, out MountedProp? prop))
                    prop = ClassifyProp(r);
                best.Mounted.Add(prop);
                _mountedOwned.Add(r);
                _attachmentOwned[r] = new OwnerRef(best, "hanging plant");
                string wall = best.Anchor != null ? best.Anchor.name : "<dead>";
                NoteOwnershipChange(r, $"mounted:'{wall}'(hanging plant)");
                _censusHangingPlants++;
                if (_hangingPlantNames.Count < HangingPlantNameCap)
                {
                    float floorY = _live.RoomFloorY[best.RoomIndex];
                    _hangingPlantNames.Add(
                        $"'{name}' foot {anchorY - floorY:F2} / top {topY - floorY:F2} wu over the "
                        + $"floor, gap {bestGap:F2} → '{wall}'{WallIdTag(best)} (fade {best.Fade:F2})");
                }
            }
        }

        /// <summary>The clause appended to the WALL-MOUNTED DRESSING line.</summary>
        private string HangingPlantsClause()
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append(" HANGING PLANTS (ModBuild 410 — a floor-footed mesh hugging a wall face, ")
              .Append("topping out ≥ ").Append((GroundExclusionHeightWU + HangingPlantMinAboveBandWU).ToString("0.0"))
              .Append(" wu over the floor and either taller than ")
              .Append(FreeStandingMinTopWU.ToString("0.0"))
              .Append(" wu or overlapping the wall's own Y span, rides that wall as mounted ")
              .Append("dressing; the foliage lane is scoped to a wall's OWN subtree and the ")
              .Append("mounted lane's first term is the airborne bar, so a tile-borne vine was ")
              .Append("nobody's): ");
            if (_censusHangingPlants == 0)
            {
                sb.Append("none adopted this rescan — ")
                  .Append(_censusHangingPlantsRefusedBand)
                  .Append(" candidate(s) beside a wall topped out in the ground band (floor ")
                  .Append("cover, untouched by design), ")
                  .Append(_censusHangingPlantsRefusedStanding)
                  .Append(" refused as standing props");
            }
            else
            {
                sb.Append(_censusHangingPlants).Append(" hanging plant(s) adopted — ")
                  .Append(string.Join("; ", _hangingPlantNames));
                if (_censusHangingPlants > _hangingPlantNames.Count)
                    sb.Append("; +").Append(_censusHangingPlants - _hangingPlantNames.Count).Append(" more");
                sb.Append(" (").Append(_censusHangingPlantsRefusedBand)
                  .Append(" in the ground band, ").Append(_censusHangingPlantsRefusedStanding)
                  .Append(" standing props refused)");
            }
            // ModBuild 411: the refusals that are NOT the ground band, named with the term — a
            // solid vine must be on this line with its reason, or it never reached the election.
            sb.Append("; refused by another term: ").Append(_censusHangingPlantsRefusedOther);
            if (_hangingRefusalNames.Count > 0)
            {
                sb.Append(" — ").Append(string.Join("; ", _hangingRefusalNames));
                if (_censusHangingPlantsRefusedOther > _hangingRefusalNames.Count)
                    sb.Append("; +").Append(_censusHangingPlantsRefusedOther - _hangingRefusalNames.Count).Append(" more");
            }
            // ModBuild 412: BY TERM, so no term hides behind another — count and up to two names
            // each; and the ground-band refusals named with the wall's own span beside them,
            // because "in the ground band" against the room floor was the ambiguity of the 411 log.
            if (_hangingRefusalByTerm.Count > 0)
            {
                sb.Append(". By term:");
                bool first = true;
                foreach (KeyValuePair<string, int> kv in _hangingRefusalByTerm)
                {
                    sb.Append(first ? " " : " | ").Append(kv.Key).Append(": ").Append(kv.Value);
                    if (_hangingRefusalNamesByTerm.TryGetValue(kv.Key, out string? names))
                        sb.Append(" (").Append(names).Append(')');
                    first = false;
                }
            }
            sb.Append(". Ground-band refusals named (up to ").Append(HangingRefusalNameCap)
              .Append(", floor cover = stands on the ROOM floor and tops out under floor + ")
              .Append((GroundExclusionHeightWU + HangingPlantMinAboveBandWU).ToString("0.0"))
              .Append(" wu; a plant hugging a wall face BELOW the room floor is judged on that ")
              .Append("wall's span instead, ModBuild 412): ")
              .Append(_hangingBandNames.Count == 0 ? "none" : string.Join("; ", _hangingBandNames));
            sb.Append(". Foliage-family shaders are candidates here since ModBuild 411 (the 410 ")
              .Append("pass skipped them uncounted, and the ivy is Foliage-family)");
            sb.Append('.');
            return sb.ToString();
        }
    }
}
