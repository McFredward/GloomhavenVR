using System;
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
                Bounds seed = default;
                bool have = false;
                foreach (MeshRenderer r in
                    dp.GetComponentsInChildren<MeshRenderer>(includeInactive: false))
                {
                    if (r == null)
                        continue;
                    if (!have)
                    {
                        seed = r.bounds;
                        have = true;
                    }
                    else
                    {
                        seed.Encapsulate(r.bounds);
                    }
                }
                if (!have)
                    continue;
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
            }
        }

        /// <summary>Is the piece part of this gate's permanently-solid ARCH? Center inside
        /// the arch rect AND top under the headroom — or an authored '*Door*'-family asset
        /// (frame trims, door sign) centered in the rect regardless of headroom.</summary>
        private static bool IsGateArchPiece(Segment gate, Bounds b, string name)
        {
            if (!gate.IsGateColumn)
                return false;
            Vector3 c = b.center;
            bool inRect = c.x >= gate.ArchMinX && c.x <= gate.ArchMaxX
                && c.z >= gate.ArchMinZ && c.z <= gate.ArchMaxZ;
            if (!inRect)
                return false;
            if (b.max.y <= gate.ArchTopY)
                return true;
            return name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Piece inside ANY gate's arch rect — excluded from EVERY adopter (stack,
        /// corner, fast reclaim, mounted): the arch is the doorway ruling's permanently
        /// solid remainder, refined 2026-08-07 to exactly this rectangle.</summary>
        private bool IsArchProtected(Bounds b, string name)
        {
            foreach (Segment s in _segments.Values)
            {
                if (s.IsGateColumn && IsGateArchPiece(s, b, name))
                    return true;
            }
            return false;
        }
    }
}
