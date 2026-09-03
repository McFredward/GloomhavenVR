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

        private void CollectHangingPlants(float minFloorY)
        {
            _censusHangingPlants = 0;
            _censusHangingPlantsRefusedStanding = 0;
            _censusHangingPlantsRefusedBand = 0;
            _hangingPlantNames.Clear();
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
                // by its emitter there. Wall-fade and foliage shaders have their own tracking.
                if (f.Mesh == null || f.Mod || f.Figure || f.Particles
                    || f.WallFadeShader || f.FoliageShader || f.WaterSurface)
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
                bool bandRefused = false;
                foreach (Segment seg in _live.Segments.Values)
                {
                    if (!seg.HasBounds || seg.IsFreeStanding || seg.DoorRoot != null
                        || !RoomDecisionValid(seg.RoomIndex))
                    {
                        continue;
                    }
                    float gap = HorizontalGap(seg.Bounds, b);
                    if (gap > MountedLinkMaxXZ || gap >= bestGap)
                        continue;
                    float floorY = _live.RoomFloorY[seg.RoomIndex];
                    // THE GROUND-BAND RULE STANDS: a plant topping out in the band, or barely
                    // over it, is floor cover — the one thing this pass must never touch.
                    if (topY < floorY + GroundExclusionHeightWU + HangingPlantMinAboveBandWU)
                    {
                        bandRefused = true;
                        continue;
                    }
                    bool tall = topY >= floorY + FreeStandingMinTopWU;
                    bool onFace = topY >= seg.Bounds.min.y && anchorY <= seg.Bounds.max.y;
                    if (!tall && !onFace)
                        continue;
                    best = seg;
                    bestGap = gap;
                }
                if (best == null)
                {
                    if (bandRefused)
                        _censusHangingPlantsRefusedBand++;
                    continue;
                }
                string name = f.Name ?? r.name;
                if (IsDoorwayAssembly(r, b, name))
                    continue; // doorways never fade
                if (IsWaterProtected(b))
                    continue; // fountain/pond (user ruling 2026-08-09)
                if (IsFigureOrActorRenderer(r))
                    continue; // FIGURES are never touched (round-7 ruling, Lights severity)
                if (IsNonOccludingRenderer(r))
                    continue; // hides nothing, never fades
                if (IsArchitectureScale(b.size, 0f))
                    continue; // architecture is a wall's or a unit's business, never dressing
                if (r.GetComponentInParent<ProceduralWall>() != null)
                    continue; // a wall run's own leftover is named on the LEFTOVER line, not hidden here
                if (IsStandingFigureProp(r))
                {
                    _censusHangingPlantsRefusedStanding++;
                    continue; // a floor-standing prop unit keeps its protection
                }
                if (best.Mounted.Count >= MountedMaxPerSegment)
                    continue;
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
            sb.Append('.');
            return sb.ToString();
        }
    }
}
